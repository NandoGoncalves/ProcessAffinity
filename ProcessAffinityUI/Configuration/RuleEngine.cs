using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Applique les règles enregistrées aux processus : au chargement initial pour
    /// ceux qui tournent déjà, puis à chaque création détectée.
    ///
    /// Aucune surveillance continue à ce stade. Une règle est écrite une fois, au
    /// moment où le processus apparaît, puis relue pour confirmer.
    /// </summary>
    public static class RuleEngine
    {
        private static readonly object SyncRoot = new object();

        private static Dictionary<string, ProcessRule> _rulesByPath =
            new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

        /// <summary>
        /// Désactive l'application des règles pour toute la session. Posé par le
        /// commutateur de ligne de commande : c'est la sortie de secours quand une
        /// configuration rend la machine inutilisable.
        /// </summary>
        public static bool IsDisabled { get; set; }

        /// <summary>Message de chargement à présenter à l'utilisateur, ou null.</summary>
        public static string LoadError { get; private set; }

        public static string FilePath
        {
            get { return RuleStore.FilePath; }
        }

        /// <summary>
        /// Redirige le stockage des règles vers un répertoire d'essai et vide le
        /// cache, pour que la redirection prenne effet même si des règles ont déjà
        /// été chargées. Passer null rend le dossier réel.
        ///
        /// Seul point d'entrée des sondes : aucune vérification ne doit écrire ni
        /// supprimer dans <c>%APPDATA%\ProcessAffinity</c>, qui contient la
        /// configuration de l'utilisateur.
        /// </summary>
        public static void RedirectStoreTo(string directoryPath)
        {
            lock (SyncRoot)
            {
                RuleStore.RedirectTo(directoryPath);

                _rulesByPath = new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);
                _loaded = false;
                LoadError = null;

                // Les préférences vivent dans le même dossier et suivent donc la
                // redirection : leur cache doit être vidé ici, faute de quoi des
                // préférences lues du dossier réel resteraient servies de mémoire,
                // puis réécrites dans le répertoire d'essai.
                SettingsStore.Reload();
            }
        }

        /// <summary>
        /// Exporte le fichier de règles tel quel. Passe-plat vers le stockage :
        /// l'IHM n'a pas à connaître <c>RuleStore</c>.
        /// </summary>
        public static bool TryExportTo(string destinationPath, out string error)
        {
            return RuleStore.TryExportTo(destinationPath, out error);
        }

        /// <summary>Version de format du fichier présent, ou 0 s'il n'y en a pas.</summary>
        public static int FileFormatVersion
        {
            get { return RuleStore.ReadFileFormatVersion(); }
        }

        /// <summary>Version de format que ce binaire sait écrire.</summary>
        public static int CurrentFormatVersion
        {
            get { return RuleStore.CurrentFormatVersion; }
        }

        /// <summary>
        /// Vrai quand le fichier existe mais a été refusé : plus récent que ce
        /// que ce binaire comprend. Aucune règle n'est alors chargée, et la
        /// première sauvegarde remplacerait tout — d'où l'avertissement dans la
        /// fenêtre, et l'export qui y est possible malgré tout.
        /// </summary>
        public static bool IsFileRejectedAsTooRecent
        {
            get
            {
                EnsureLoaded();

                return LoadError != null && FileFormatVersion > CurrentFormatVersion;
            }
        }

        public static void EnsureLoaded()
        {
            lock (SyncRoot)
            {
                if (_loaded)
                {
                    return;
                }

                string error;
                _rulesByPath = RuleStore.Load(out error);
                LoadError = error;
                _loaded = true;
            }
        }

        public static ProcessRule Find(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath))
            {
                return null;
            }

            EnsureLoaded();

            lock (SyncRoot)
            {
                ProcessRule rule;

                return _rulesByPath.TryGetValue(executablePath, out rule) ? rule : null;
            }
        }

        public static bool HasRule(string executablePath)
        {
            return Find(executablePath) != null;
        }

        public static int Count
        {
            get
            {
                EnsureLoaded();

                lock (SyncRoot)
                {
                    return _rulesByPath.Count;
                }
            }
        }

        /// <summary>
        /// Masque de tous les processeurs logiques de cette machine.
        /// </summary>
        public static nuint GetAllProcessorsMask()
        {
            int count = Environment.ProcessorCount;

            if (count >= 64)
            {
                return unchecked((nuint)ulong.MaxValue);
            }

            return (nuint)((1UL << count) - 1UL);
        }

        /// <summary>
        /// Restreint un masque aux processeurs qui existent réellement. Un fichier
        /// écrit sur une machine à 24 threads et relu sur une à 8 porterait des bits
        /// qui ne désignent rien.
        /// </summary>
        public static nuint RestrictToExistingProcessors(nuint mask)
        {
            return mask & GetAllProcessorsMask();
        }

        /// <summary>
        /// Enregistre ou remplace la règle d'un processus. Refuse les processus
        /// critiques du système, ceux dont le chemin est inconnu, et les entrées de
        /// service — une règle porte un exécutable, pas un service.
        /// </summary>
        public static bool TrySave(Process process, nuint affinityMask, int priorityClass, out string error)
        {
            ProcessRule rule = BuildCandidateRule(process, affinityMask, priorityClass, out error);

            return rule != null && TryStore(rule, out error);
        }

        /// <summary>
        /// Construit la règle que <see cref="TrySave"/> écrirait, sans rien écrire.
        /// Null si le processus n'est pas éligible, <paramref name="error"/> disant
        /// pourquoi.
        ///
        /// Séparer la construction de l'écriture permet de comparer ce qui serait
        /// enregistré pour plusieurs instances d'un même exécutable, avant de
        /// décider laquelle retenir. Comparer autre chose que cet objet risquerait
        /// de tenir pour identiques deux configurations qui ne le seraient pas une
        /// fois écrites.
        /// </summary>
        internal static ProcessRule BuildCandidateRule(
            Process process, nuint affinityMask, int priorityClass, out string error)
        {
            error = null;

            if (process == null)
            {
                error = "No process.";
                return null;
            }

            if (process.IsCriticalSystemProcess)
            {
                error = "\"" + process.ProcessName + "\" is a critical system process.\r\n\r\n"
                        + "Forcing its affinity or priority at every start could make Windows unusable. "
                        + "Saving a rule for it is not allowed.";
                return null;
            }

            if (process.IsService)
            {
                error = "A rule applies to an executable, not to a service entry.\r\n\r\n"
                        + "Save the rule on its host process instead.";
                return null;
            }

            if (string.IsNullOrEmpty(process.ExecutablePath))
            {
                error = "The executable path of \"" + process.ProcessName + "\" is unknown, "
                        + "so there is nothing to attach a rule to.\r\n\r\n"
                        + "Running ProcessAffinity as administrator usually makes it readable.";
                return null;
            }

            nuint restricted = RestrictToExistingProcessors(affinityMask);

            if (restricted == 0)
            {
                error = "The selected affinity covers no processor of this machine.";
                return null;
            }

            ProcessRule rule = new ProcessRule
            {
                ExecutablePath = process.ExecutablePath,
                PriorityClass = priorityClass,
            };

            rule.SetAffinityMask(restricted);

            // Les deux réglages de la version 2 sont capturés dans l'état où ils
            // se trouvent, comme l'affinité et la priorité : « Save configuration »
            // enregistre ce qu'on voit.
            CaptureSchedulingSettings(process, rule);

            return rule;
        }

        /// <summary>Écrit une règle déjà construite.</summary>
        internal static bool TryStore(ProcessRule rule, out string error)
        {
            error = null;

            if (rule == null)
            {
                error = "No rule.";
                return false;
            }

            EnsureLoaded();

            lock (SyncRoot)
            {
                ProcessRule previous;
                bool hadPrevious = _rulesByPath.TryGetValue(rule.ExecutablePath, out previous);

                _rulesByPath[rule.ExecutablePath] = rule;

                if (!RuleStore.TrySave(_rulesByPath.Values, out error))
                {
                    // Remettre ce qui s'y trouvait : une écriture refusée ne doit
                    // pas laisser la carte en mémoire en avance sur le fichier.
                    if (hadPrevious)
                    {
                        _rulesByPath[rule.ExecutablePath] = previous;
                    }
                    else
                    {
                        _rulesByPath.Remove(rule.ExecutablePath);
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Met à jour la règle d'un processus qui en porte déjà une, après une
        /// modification faite depuis l'application. Sans effet sur un processus
        /// sans règle : modifier ponctuellement n'équivaut pas à enregistrer.
        /// </summary>
        public static void UpdateIfRuled(Process process)
        {
            if (process == null || string.IsNullOrEmpty(process.ExecutablePath))
            {
                return;
            }

            if (Find(process.ExecutablePath) == null)
            {
                return;
            }

            nuint? affinity = process.GetProcessorAffinity();

            if (affinity == null)
            {
                return;
            }

            // Priorité lue en natif : Win32_Process.Priority est figé à
            // l'énumération et enregistrerait une valeur périmée dans la règle.
            int priorityClass = process.GetPriorityClass() ?? (int)Process.ToProcessPriorityEnum(process.Priority);

            string error;
            TrySave(process, affinity.Value, priorityClass, out error);

            // La règle vient d'être alignée sur l'état réel : le compteur de
            // corrections repart de zéro, une modification voulue n'étant pas une
            // contestation.
            process.RuleCorrectionCount = 0;
        }

        /// <summary>
        /// Relève le mode d'efficacité et les CPU Sets courants du processus dans
        /// la règle. Un réglage illisible reste absent : on n'enregistre pas ce
        /// qu'on n'a pas su lire.
        /// </summary>
        private static void CaptureSchedulingSettings(Process process, ProcessRule rule)
        {
            if (ProcessPowerThrottling.IsEfficiencyModeSupported)
            {
                EfficiencyModeEnum? mode = process.GetEfficiencyMode();

                rule.EfficiencyMode = mode == null ? (int?)null : (int)mode.Value;
            }

            if (!ProcessPowerThrottling.AreCpuSetsSupported)
            {
                return;
            }

            rule.CpuSetProcessors = GetCpuSetProcessors(process);
        }

        /// <summary>
        /// Les CPU Sets d'un processus, en numéros de processeur logique — les
        /// identifiants sont opaques et propres à une machine. Null quand ils sont
        /// illisibles ou que la machine ne les gère pas, ce qui ne se confond pas
        /// avec un tableau vide, lequel signifie « aucune préférence ».
        /// </summary>
        internal static int[] GetCpuSetProcessors(Process process)
        {
            if (process == null || !ProcessPowerThrottling.AreCpuSetsSupported)
            {
                return null;
            }

            uint[] cpuSets = process.GetDefaultCpuSets();
            SystemCpuSets.LogicalProcessor[] topology = SystemCpuSets.TryGet();

            if (cpuSets == null || topology == null)
            {
                return null;
            }

            List<int> processors = new List<int>();

            foreach (SystemCpuSets.LogicalProcessor processor in topology)
            {
                if (Array.IndexOf(cpuSets, processor.Id) >= 0)
                {
                    processors.Add(processor.Index);
                }
            }

            return processors.ToArray();
        }

        /// <summary>
        /// Identifiants de CPU Set correspondant aux numéros de processeur de la
        /// règle, ou null quand la conversion est impossible — topologie absente,
        /// ou aucun des processeurs cités n'existe sur cette machine.
        /// </summary>
        private static uint[] ResolveCpuSetIds(int[] processors)
        {
            if (processors == null)
            {
                return null;
            }

            if (processors.Length == 0)
            {
                // « Aucune préférence » est un réglage à part entière.
                return new uint[0];
            }

            SystemCpuSets.LogicalProcessor[] topology = SystemCpuSets.TryGet();

            if (topology == null)
            {
                return null;
            }

            List<uint> ids = new List<uint>();

            foreach (SystemCpuSets.LogicalProcessor processor in topology)
            {
                if (Array.IndexOf(processors, processor.Index) >= 0)
                {
                    ids.Add(processor.Id);
                }
            }

            // Même garde que pour le masque d'affinité : une règle venue d'une
            // machine plus large ne doit pas désigner le vide.
            return ids.Count == 0 ? null : ids.ToArray();
        }

        /// <summary>
        /// Toutes les règles du fichier, triées par chemin. C'est ce que montre
        /// la fenêtre des configurations enregistrées : jusqu'ici, une règle
        /// n'était visible qu'à travers la tuile d'un processus en cours, et une
        /// règle orpheline ne l'était nulle part.
        /// </summary>
        public static List<ProcessRule> GetAllRules()
        {
            EnsureLoaded();

            lock (SyncRoot)
            {
                return _rulesByPath.Values
                    .OrderBy(rule => rule.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        /// <summary>
        /// Remplace une règle et réécrit le fichier. Le chemin est l'identité :
        /// il ne se modifie pas, une règle désignant un autre exécutable étant
        /// une autre règle.
        /// </summary>
        public static bool TryUpdate(ProcessRule rule, out string error)
        {
            error = null;

            if (rule == null || string.IsNullOrWhiteSpace(rule.ExecutablePath))
            {
                error = "The rule has no executable path.";
                return false;
            }

            EnsureLoaded();

            lock (SyncRoot)
            {
                _rulesByPath[rule.ExecutablePath] = rule;

                return RuleStore.TrySave(_rulesByPath.Values, out error);
            }
        }

        /// <summary>
        /// Retire plusieurs règles en une seule réécriture. Les retirer une à
        /// une réécrirait le fichier autant de fois, et laisserait un état
        /// intermédiaire derrière chaque échec.
        /// </summary>
        public static bool TryRemoveMany(IEnumerable<string> executablePaths, out int removedCount, out string error)
        {
            error = null;
            removedCount = 0;

            if (executablePaths == null)
            {
                return true;
            }

            EnsureLoaded();

            lock (SyncRoot)
            {
                foreach (string path in executablePaths)
                {
                    if (!string.IsNullOrEmpty(path) && _rulesByPath.Remove(path))
                    {
                        removedCount++;
                    }
                }

                if (removedCount == 0)
                {
                    return true;
                }

                return RuleStore.TrySave(_rulesByPath.Values, out error);
            }
        }

        /// <summary>
        /// État d'une règle vue depuis le fichier, et non depuis une tuile.
        ///
        /// Les trois premiers cas se lisent sur un processus en cours ; les deux
        /// suivants se constatent sans qu'aucun processus ne tourne, et c'est
        /// précisément ce qu'on ne voyait nulle part. <see cref="NotRunning"/>
        /// n'est pas un échec : la règle attend son heure.
        /// </summary>
        public enum RuleFileStateEnum
        {
            Applied,
            Contested,
            Denied,
            Orphan,
            InvalidMask,
            NotRunning,
        }

        /// <summary>
        /// Juge une règle sans attendre qu'un processus la porte. Les deux
        /// vérifications qui ne demandent rien à Windows — le fichier existe-t-il
        /// encore, le masque couvre-t-il un processeur de cette machine — passent
        /// avant la recherche d'un processus en cours : elles valent même quand
        /// rien ne tourne.
        /// </summary>
        public static RuleFileStateEnum DescribeFileState(ProcessRule rule, IEnumerable<Process> runningProcesses)
        {
            if (rule == null)
            {
                return RuleFileStateEnum.Orphan;
            }

            try
            {
                if (!File.Exists(rule.ExecutablePath))
                {
                    return RuleFileStateEnum.Orphan;
                }
            }
            catch
            {
                // Chemin illisible : on n'en conclut pas qu'il a disparu.
            }

            if (RestrictToExistingProcessors(rule.GetAffinityMask()) == 0)
            {
                return RuleFileStateEnum.InvalidMask;
            }

            if (runningProcesses == null)
            {
                return RuleFileStateEnum.NotRunning;
            }

            bool found = false;

            foreach (Process process in runningProcesses)
            {
                if (process == null
                    || process.IsService
                    || !string.Equals(process.ExecutablePath, rule.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                found = true;

                // Le pire l'emporte : une règle qui tient sur une instance et
                // échoue sur une autre est en échec, et c'est l'échec qu'il faut
                // montrer.
                switch (process.RuleState)
                {
                    case RuleStateEnum.Denied: return RuleFileStateEnum.Denied;
                    case RuleStateEnum.Contested: return RuleFileStateEnum.Contested;
                    case RuleStateEnum.InvalidMask: return RuleFileStateEnum.InvalidMask;
                    case RuleStateEnum.Orphan: return RuleFileStateEnum.Orphan;
                }
            }

            return found ? RuleFileStateEnum.Applied : RuleFileStateEnum.NotRunning;
        }

        /// <summary>Intitulé anglais d'un état, pour la liste.</summary>
        public static string DescribeFileState(RuleFileStateEnum state)
        {
            switch (state)
            {
                case RuleFileStateEnum.Applied: return "Applied";
                case RuleFileStateEnum.Contested: return "Contested";
                case RuleFileStateEnum.Denied: return "Denied";
                case RuleFileStateEnum.Orphan: return "Orphan";
                case RuleFileStateEnum.InvalidMask: return "Invalid on this machine";
                default: return "Not running";
            }
        }

        /// <summary>Ce que l'état veut dire, pour l'info-bulle de la ligne.</summary>
        public static string ExplainFileState(RuleFileStateEnum state)
        {
            switch (state)
            {
                case RuleFileStateEnum.Applied:
                    return "The rule holds on every running instance of this executable.";

                case RuleFileStateEnum.Contested:
                    return "The rule was written but read back different. Windows silently downgrades "
                           + "real-time priority, and may adjust an affinity mask.";

                case RuleFileStateEnum.Denied:
                    return "The running process cannot be changed without elevation, so the rule was not applied.";

                case RuleFileStateEnum.Orphan:
                    return "The executable no longer exists at this path. The rule can never apply "
                           + "until the program is reinstalled there.";

                case RuleFileStateEnum.InvalidMask:
                    return "The saved affinity mask covers no processor of this machine. It was probably "
                           + "saved on a machine with more logical processors.";

                default:
                    return "No instance of this executable is running. The rule will be applied at the next launch.";
            }
        }

        public static bool TryRemove(string executablePath, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            EnsureLoaded();

            lock (SyncRoot)
            {
                if (!_rulesByPath.Remove(executablePath))
                {
                    return false;
                }

                return RuleStore.TrySave(_rulesByPath.Values, out error);
            }
        }

        /// <summary>
        /// Applique la règle du processus, s'il en a une, et consigne le résultat
        /// sur lui. Appelée depuis le thread de matérialisation — jamais depuis
        /// celui d'échantillonnage, dont le tick ne doit rien attendre.
        /// </summary>
        public static void Apply(Process process)
        {
            if (IsDisabled || process == null || process.IsService)
            {
                return;
            }

            ProcessRule rule = Find(process.ExecutablePath);

            if (rule == null)
            {
                process.SetRuleState(RuleStateEnum.None, null);
                return;
            }

            // Règle orpheline : l'exécutable a disparu. Le processus qui tourne
            // porte pourtant ce chemin — cas d'une mise à jour ou d'une
            // désinstallation en cours. On le signale sans rien écrire.
            try
            {
                if (!File.Exists(rule.ExecutablePath))
                {
                    process.SetRuleState(RuleStateEnum.Orphan,
                        "The rule points to \"" + rule.ExecutablePath + "\", which no longer exists on disk.");
                    return;
                }
            }
            catch
            {
                // Chemin illisible : on n'en conclut pas qu'il a disparu.
            }

            nuint wanted = RestrictToExistingProcessors(rule.GetAffinityMask());

            if (wanted == 0)
            {
                process.SetRuleState(RuleStateEnum.InvalidMask,
                    "The saved affinity mask covers no processor of this machine. "
                    + "It was probably saved on a machine with more logical processors. The rule was ignored.");
                return;
            }

            if (!process.IsModifiable)
            {
                process.SetRuleState(RuleStateEnum.Denied,
                    "Affinity and priority of this process cannot be changed without elevation. The rule was not applied.");
                return;
            }

            // Même traitement que pour un masque d'affinité vide : un réglage
            // inapplicable se signale, il ne se corrige pas. Sans cette garde, la
            // divergence était perpétuelle, la règle « corrigée » quatre fois en
            // vain, puis abandonnée avec un message accusant à tort un tiers de
            // la réécrire.
            if (IsCpuSetRuleUnresolvable(rule))
            {
                process.SetRuleState(RuleStateEnum.InvalidMask, DescribeUnresolvableCpuSets(rule));
                return;
            }

            process.SetProcessorAffinity(wanted);

            try
            {
                process.Priority = rule.PriorityClass;
            }
            catch
            {
                // Relu juste après : c'est la relecture qui tranche, pas l'absence
                // d'exception.
            }

            ApplySchedulingSettings(process, rule);

            // Relecture systématique. Windows rétrograde la priorité temps réel
            // sans le dire, et peut ajuster un masque : écrire sans vérifier
            // reviendrait à afficher une règle appliquée qui ne l'est pas.
            List<string> divergences = new List<string>();

            divergences.AddRange(GetSchedulingDivergences(process, rule));

            nuint? readAffinity = process.GetProcessorAffinity();

            if (readAffinity == null)
            {
                divergences.Add("the affinity could not be read back");
            }
            else if (readAffinity.Value != wanted)
            {
                divergences.Add("affinity requested " + DescribeMask(wanted)
                    + ", actually set " + DescribeMask(readAffinity.Value));
            }

            // Lecture native, pas Win32_Process.Priority : celui-ci est figé à
            // l'énumération et rendrait la valeur qu'on vient d'écrire, ce qui
            // reviendrait à se relire soi-même plutôt que Windows.
            int? readPriority = process.GetPriorityClass();

            if (readPriority == null)
            {
                divergences.Add("the priority could not be read back");
            }
            else if (readPriority.Value != rule.PriorityClass)
            {
                divergences.Add("priority requested "
                    + DescribePriority(rule.PriorityClass)
                    + ", actually set " + DescribePriority(readPriority.Value));
            }

            if (divergences.Count == 0)
            {
                process.SetRuleState(RuleStateEnum.Applied, null);
                return;
            }

            process.SetRuleState(RuleStateEnum.Contested,
                "Windows did not honour the rule as saved: " + string.Join("; ", divergences) + ".");
        }

        /// <summary>
        /// La règle cite-t-elle des CPU Sets qu'aucun processeur de cette machine
        /// ne porte. Vrai seulement quand la règle s'en mêle et que la conversion
        /// échoue : une règle sans CPU Sets, ou qui demande « aucune préférence »,
        /// est parfaitement applicable.
        /// </summary>
        private static bool IsCpuSetRuleUnresolvable(ProcessRule rule)
        {
            return rule.CpuSetProcessors != null
                   && ProcessPowerThrottling.AreCpuSetsSupported
                   && ResolveCpuSetIds(rule.CpuSetProcessors) == null;
        }

        private static string DescribeUnresolvableCpuSets(ProcessRule rule)
        {
            if (SystemCpuSets.TryGet() == null)
            {
                return "The rule sets CPU Sets, but the processor topology of this machine could not be read. "
                       + "The rule was ignored.";
            }

            return "The rule sets CPU Sets on processor"
                   + (rule.CpuSetProcessors.Length > 1 ? "s " : " ")
                   + string.Join(", ", rule.CpuSetProcessors)
                   + ", none of which exists on this machine — it has "
                   + Environment.ProcessorCount + " logical processors. "
                   + "It was probably saved on a larger machine. The rule was ignored.";
        }

        /// <summary>
        /// Écrit les deux réglages de la version 2, chacun seulement si la règle
        /// s'en mêle. Une règle de version 1 n'y touche pas.
        /// </summary>
        private static void ApplySchedulingSettings(Process process, ProcessRule rule)
        {
            if (rule.EfficiencyMode != null && ProcessPowerThrottling.IsEfficiencyModeSupported)
            {
                process.TrySetEfficiencyMode((EfficiencyModeEnum)rule.EfficiencyMode.Value);
            }

            if (rule.CpuSetProcessors == null || !ProcessPowerThrottling.AreCpuSetsSupported)
            {
                return;
            }

            uint[] ids = ResolveCpuSetIds(rule.CpuSetProcessors);

            if (ids != null)
            {
                process.TrySetDefaultCpuSets(ids.Length == 0 ? null : ids);
            }
        }

        /// <summary>
        /// Écarts constatés sur les deux réglages de la version 2, après relecture.
        /// </summary>
        private static List<string> GetSchedulingDivergences(Process process, ProcessRule rule)
        {
            List<string> divergences = new List<string>();

            if (rule.EfficiencyMode != null && ProcessPowerThrottling.IsEfficiencyModeSupported)
            {
                EfficiencyModeEnum? mode = process.GetEfficiencyMode();

                if (mode == null)
                {
                    divergences.Add("the efficiency mode could not be read back");
                }
                else if ((int)mode.Value != rule.EfficiencyMode.Value)
                {
                    divergences.Add("efficiency mode requested "
                        + DescribeEfficiencyMode(rule.EfficiencyMode.Value)
                        + ", actually set " + DescribeEfficiencyMode((int)mode.Value));
                }
            }

            if (rule.CpuSetProcessors == null || !ProcessPowerThrottling.AreCpuSetsSupported)
            {
                return divergences;
            }

            uint[] wanted = ResolveCpuSetIds(rule.CpuSetProcessors);

            if (wanted == null)
            {
                // Inapplicable : traité en amont comme un état à part entière,
                // jamais comme une divergence — celle-ci ne se résorberait
                // jamais.
                return divergences;
            }

            uint[] actual = process.GetDefaultCpuSets();

            if (actual == null)
            {
                divergences.Add("the CPU Sets could not be read back");
            }
            else if (!SameCpuSets(actual, wanted))
            {
                divergences.Add("CPU Sets requested " + DescribeCpuSetCount(wanted.Length)
                    + ", actually set " + DescribeCpuSetCount(actual.Length));
            }

            return divergences;
        }

        /// <summary>
        /// Une liste vide et la liste complète désignent le même réglage : aucune
        /// préférence. Les confondre évite une divergence perpétuelle.
        /// </summary>
        private static bool SameCpuSets(uint[] actual, uint[] wanted)
        {
            SystemCpuSets.LogicalProcessor[] topology = SystemCpuSets.TryGet();
            int total = topology == null ? -1 : topology.Length;

            bool actualUnrestricted = actual.Length == 0 || actual.Length == total;
            bool wantedUnrestricted = wanted.Length == 0 || wanted.Length == total;

            if (actualUnrestricted || wantedUnrestricted)
            {
                return actualUnrestricted && wantedUnrestricted;
            }

            if (actual.Length != wanted.Length)
            {
                return false;
            }

            foreach (uint id in wanted)
            {
                if (Array.IndexOf(actual, id) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        internal static string DescribeCpuSetCount(int count)
        {
            return count == 0 ? "no preference" : count + (count > 1 ? " processors" : " processor");
        }

        internal static string DescribeEfficiencyMode(int mode)
        {
            switch ((EfficiencyModeEnum)mode)
            {
                case EfficiencyModeEnum.Enabled:
                    return "throttled";
                case EfficiencyModeEnum.Disabled:
                    return "never throttled";
                default:
                    return "left to Windows";
            }
        }

        internal static string DescribeMask(nuint mask)
        {
            return "0x" + ((ulong)mask).ToString("X") + " (" + System.Numerics.BitOperations.PopCount((ulong)mask) + " cores)";
        }

        /// <summary>
        /// Nom lisible d'une classe de priorité. L'énumération déclare
        /// « Unknown = Normal », si bien que ToString() rend « Unknown » pour la
        /// priorité normale — trompeur dans un message destiné à l'utilisateur.
        /// </summary>
        internal static string DescribePriority(int priorityClass)
        {
            switch (priorityClass)
            {
                case (int)ProcessPriorityEnum.Idle:
                    return "Idle";
                case (int)ProcessPriorityEnum.BelowNormal:
                    return "Below normal";
                case (int)ProcessPriorityEnum.Normal:
                    return "Normal";
                case (int)ProcessPriorityEnum.AboveNormal:
                    return "Above normal";
                case (int)ProcessPriorityEnum.HighPriority:
                    return "High";
                case (int)ProcessPriorityEnum.RealTime:
                    return "Real time";
                default:
                    return "class " + priorityClass.ToString();
            }
        }

        /// <summary>
        /// Corrections consécutives au-delà desquelles on cesse d'intervenir. Un
        /// tiers qui réécrit aussi vite que nous corrigeons ne sera pas vaincu :
        /// un combat invisible à deux écritures par seconde est pire que l'abandon.
        /// </summary>
        public const int MaximumConsecutiveCorrections = 4;

        /// <summary>
        /// Contrôle de conformité, lectures seules — affinité 0,0071 ms, priorité
        /// 0,0041 ms. Appelé à chaque tick d'échantillonnage pour les seuls
        /// processus sous règle appliquée.
        ///
        /// Ne corrige rien : rend simplement vrai quand une correction s'impose,
        /// pour que l'écriture ait lieu sur le thread de matérialisation.
        /// </summary>
        public static bool NeedsEnforcement(Process process)
        {
            if (IsDisabled || process == null || process.IsService)
            {
                return false;
            }

            // Seul l'état « appliquée » est surveillé. Une règle contestée a été
            // abandonnée, une règle refusée ou orpheline n'a rien à faire valoir.
            if (process.RuleState != RuleStateEnum.Applied)
            {
                return false;
            }

            ProcessRule rule = Find(process.ExecutablePath);

            if (rule == null)
            {
                // Règle retirée depuis : plus rien à surveiller.
                process.SetRuleState(RuleStateEnum.None, null);
                process.RuleCorrectionCount = 0;

                return false;
            }

            nuint wanted = RestrictToExistingProcessors(rule.GetAffinityMask());

            if (wanted == 0)
            {
                return false;
            }

            // Inapplicable : on ne surveille pas ce qu'on ne saurait pas poser.
            if (IsCpuSetRuleUnresolvable(rule))
            {
                process.SetRuleState(RuleStateEnum.InvalidMask, DescribeUnresolvableCpuSets(rule));
                process.RuleCorrectionCount = 0;

                return false;
            }

            nuint? actualAffinity = process.GetProcessorAffinity();
            int? actualPriority = process.GetPriorityClass();

            // Illisible : le processus est peut-être en train de se terminer. On
            // ne conclut pas à une divergence sur une lecture qui a échoué.
            if (actualAffinity == null || actualPriority == null)
            {
                return false;
            }

            // Les deux réglages de la version 2 sont surveillés comme les autres,
            // quand la règle s'en mêle. Leur lecture coûte le même ordre de
            // grandeur qu'une lecture d'affinité.
            if (actualAffinity.Value == wanted
                && actualPriority.Value == rule.PriorityClass
                && GetSchedulingDivergences(process, rule).Count == 0)
            {
                process.RuleCorrectionCount = 0;

                return false;
            }

            return true;
        }

        /// <summary>
        /// Rétablit la règle. Appelée depuis le thread de matérialisation : écrire
        /// l'affinité et la priorité, puis les relire, n'a rien à faire dans le
        /// tick du % CPU.
        ///
        /// Une modification venue de l'extérieur est corrigée, jamais absorbée :
        /// la règle n'est mise à jour que par <see cref="UpdateIfRuled"/>, appelée
        /// depuis les fenêtres d'affinité et de priorité. Sans cette distinction,
        /// le perturbateur réécrirait la règle qu'il viole et la surveillance
        /// s'annulerait d'elle-même.
        /// </summary>
        public static void Enforce(Process process)
        {
            if (IsDisabled || process == null || process.RuleState != RuleStateEnum.Applied)
            {
                return;
            }

            ProcessRule rule = Find(process.ExecutablePath);

            if (rule == null)
            {
                return;
            }

            nuint wanted = RestrictToExistingProcessors(rule.GetAffinityMask());

            if (wanted == 0)
            {
                return;
            }

            // Le quota est épuisé : un tiers conteste activement. On abandonne, en
            // consignant ce qui était attendu, ce qui est en place, et quand.
            if (process.RuleCorrectionCount >= MaximumConsecutiveCorrections)
            {
                Abandon(process, rule, wanted);

                return;
            }

            process.RuleCorrectionCount++;

            process.SetProcessorAffinity(wanted);

            try
            {
                process.Priority = rule.PriorityClass;
            }
            catch
            {
            }

            ApplySchedulingSettings(process, rule);

            // L'état ne change pas — la règle reste appliquée — donc rien ne
            // rallumerait le bandeau de lui-même. Sans ce signalement, un conflit
            // survenu pendant que la fenêtre était réduite ne laisserait aucune
            // trace à l'écran au retour.
            process.NotifyRuleEnforced();
        }

        private static void Abandon(Process process, ProcessRule rule, nuint wanted)
        {
            nuint? actualAffinity = process.GetProcessorAffinity();
            int? actualPriority = process.GetPriorityClass();

            string detail =
                "Another program keeps overriding this rule. ProcessAffinity stopped correcting it after "
                + MaximumConsecutiveCorrections + " consecutive attempts.\r\n"
                + "Expected: affinity " + DescribeMask(wanted)
                + ", priority " + DescribePriority(rule.PriorityClass) + "\r\n"
                + "In place: affinity "
                + (actualAffinity == null ? "unreadable" : DescribeMask(actualAffinity.Value))
                + ", priority "
                + (actualPriority == null ? "unreadable" : DescribePriority(actualPriority.Value)) + "\r\n"
                + "Given up at " + DateTime.Now.ToString("HH:mm:ss") + ".";

            process.SetRuleState(RuleStateEnum.Contested, detail);
            process.RuleCorrectionCount = 0;
        }
    }
}
