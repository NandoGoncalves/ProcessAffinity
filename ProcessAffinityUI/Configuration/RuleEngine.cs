using System;
using System.Collections.Generic;
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
            error = null;

            if (process == null)
            {
                error = "No process.";
                return false;
            }

            if (process.IsCriticalSystemProcess)
            {
                error = "\"" + process.ProcessName + "\" is a critical system process.\r\n\r\n"
                        + "Forcing its affinity or priority at every start could make Windows unusable. "
                        + "Saving a rule for it is not allowed.";
                return false;
            }

            if (process.IsService)
            {
                error = "A rule applies to an executable, not to a service entry.\r\n\r\n"
                        + "Save the rule on its host process instead.";
                return false;
            }

            if (string.IsNullOrEmpty(process.ExecutablePath))
            {
                error = "The executable path of \"" + process.ProcessName + "\" is unknown, "
                        + "so there is nothing to attach a rule to.\r\n\r\n"
                        + "Running ProcessAffinity as administrator usually makes it readable.";
                return false;
            }

            nuint restricted = RestrictToExistingProcessors(affinityMask);

            if (restricted == 0)
            {
                error = "The selected affinity covers no processor of this machine.";
                return false;
            }

            EnsureLoaded();

            ProcessRule rule = new ProcessRule
            {
                ExecutablePath = process.ExecutablePath,
                PriorityClass = priorityClass,
            };

            rule.SetAffinityMask(restricted);

            lock (SyncRoot)
            {
                _rulesByPath[rule.ExecutablePath] = rule;

                if (!RuleStore.TrySave(_rulesByPath.Values, out error))
                {
                    _rulesByPath.Remove(rule.ExecutablePath);

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

            string error;
            TrySave(process, affinity.Value, (int)Process.ToProcessPriorityEnum(process.Priority), out error);
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

            // Relecture systématique. Windows rétrograde la priorité temps réel
            // sans le dire, et peut ajuster un masque : écrire sans vérifier
            // reviendrait à afficher une règle appliquée qui ne l'est pas.
            List<string> divergences = new List<string>();

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

            int readPriority = (int)Process.ToProcessPriorityEnum(process.Priority);

            if (readPriority != rule.PriorityClass)
            {
                divergences.Add("priority requested "
                    + ((ProcessPriorityEnum)rule.PriorityClass).ToString()
                    + ", actually set " + ((ProcessPriorityEnum)readPriority).ToString());
            }

            if (divergences.Count == 0)
            {
                process.SetRuleState(RuleStateEnum.Applied, null);
                return;
            }

            process.SetRuleState(RuleStateEnum.Contested,
                "Windows did not honour the rule as saved: " + string.Join("; ", divergences) + ".");
        }

        private static string DescribeMask(nuint mask)
        {
            return "0x" + ((ulong)mask).ToString("X") + " (" + System.Numerics.BitOperations.PopCount((ulong)mask) + " cores)";
        }
    }
}
