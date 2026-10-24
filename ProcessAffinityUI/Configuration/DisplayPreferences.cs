using System;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;

using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Ce que l'utilisateur a décidé de l'affichage d'une entrée : son épinglage
    /// en tête, et le facteur d'agrandissement de sa tuile.
    ///
    /// ## Deux portées, et pourquoi
    ///
    /// Le lot 5d identifiait une entrée par son nom, ce qui envoyait les vingt
    /// tuiles de msedge.exe en tête dès qu'on en épinglait une, et les
    /// agrandissait toutes ensemble. Les deux usages sont désormais séparés :
    ///
    /// **La portée d'instance** est ce que donne un geste direct sur une tuile.
    /// Elle ne vaut que pour cette instance et disparaît avec elle : rien n'est
    /// écrit sur le disque. Son identité est le couple PID et date de création,
    /// celui qui sert déjà à distinguer un PID réutilisé de son prédécesseur.
    ///
    /// **La portée exécutable** est ce qu'on demande explicitement à retenir.
    /// Elle vaut pour toutes les occurrences du programme et survit aux
    /// redémarrages, dans <c>settings.json</c>. Son identité est le chemin de
    /// l'exécutable — la même que celle des règles, ce qui est cohérent : une
    /// configuration enregistrée s'applique à tout lancement de ce binaire.
    ///
    /// Un service n'a pas de chemin utile mais un nom court exact : c'est lui
    /// qui sert de clé persistante, et il ne désigne qu'une entrée.
    ///
    /// ## Ce que coûte le chemin
    ///
    /// Mesuré en session non élevée : **169 processus sur 274 n'exposent pas
    /// leur chemin**. La portée exécutable leur est donc indisponible, comme
    /// l'est déjà l'enregistrement d'une configuration. L'entrée de menu est
    /// alors grisée, avec la raison. La portée d'instance, elle, reste
    /// disponible sur toutes les tuiles — c'est ce qui rend la séparation
    /// praticable.
    ///
    /// Les clés nommées qu'écrivait le lot 5d ne sont plus reconnues : elles
    /// sont ignorées en silence, et les épinglages qu'elles portaient
    /// disparaissent. C'est précisément ce qu'on corrige.
    /// </summary>
    public static class DisplayPreferences
    {
        private const string ServicePrefix = "service:";
        private const string PathPrefix = "path:";

        /// <summary>
        /// Agrandissement maximal. Au-delà, une tuile couvre tant de surface
        /// qu'elle chasse tout le reste : à 3× elle occupe déjà neuf fois les
        /// pixels d'une tuile normale.
        /// </summary>
        public const int MaximumScale = 3;

        private static readonly object SyncRoot = new object();

        /// <summary>
        /// Réglages d'instance, en mémoire seulement. Ils meurent avec
        /// l'application, et chaque entrée meurt avec son processus — un
        /// nettoyage passe sur la liste vivante à chaque relevé.
        /// </summary>
        private static readonly List<InstanceSetting> InstanceSettings = new List<InstanceSetting>();

        private sealed class InstanceSetting
        {
            public int ProcessID { get; set; }

            /// <summary>Zéro quand le relevé n'a pas encore eu lieu.</summary>
            public long CreateTime { get; set; }

            public bool IsService { get; set; }

            /// <summary>Nom du service, pour distinguer deux entrées d'un même PID.</summary>
            public string ServiceName { get; set; }

            public bool? ShowCpuValue { get; set; }

            public bool? ShowMemoryValue { get; set; }

            public bool Pinned { get; set; }

            public int PinOrder { get; set; }

            /// <summary>
            /// Null quand l'utilisateur n'a rien choisi pour cette instance.
            /// Trois états et non deux, pour la même raison que les deux
            /// valeurs chiffrées ci-dessus : une instance remise à Normal doit
            /// primer sur une taille retenue pour le programme, ce qu'un 1
            /// indiscernable de l'absence de choix interdisait.
            /// </summary>
            public int? Scale { get; set; }
        }

        private static int _nextPinOrder;

        /// <summary>
        /// Levé après tout changement. La fenêtre principale s'en sert pour
        /// réordonner le panneau sans le reconstruire.
        /// </summary>
        public static event Action Changed;

        // ------------------------------------------------------ identités

        /// <summary>
        /// Clé persistante, ou null quand l'entrée n'en a pas — un processus
        /// dont le chemin n'est pas lisible.
        /// </summary>
        public static string GetExecutableKey(Process process)
        {
            if (process == null)
            {
                return null;
            }

            if (process.IsService)
            {
                return string.IsNullOrEmpty(process.ProcessName)
                    ? null
                    : ServicePrefix + process.ProcessName;
            }

            return string.IsNullOrEmpty(process.ExecutablePath)
                ? null
                : PathPrefix + process.ExecutablePath;
        }

        /// <summary>
        /// Vrai quand l'entrée peut porter un réglage persistant. Faux pour un
        /// processus dont le chemin est illisible.
        /// </summary>
        public static bool CanRemember(Process process)
        {
            return GetExecutableKey(process) != null;
        }

        /// <summary>
        /// Raison du refus, pour l'info-bulle de l'entrée grisée.
        /// </summary>
        public static string DescribeWhyNotRememberable(Process process)
        {
            return "The path of this executable cannot be read without elevation, "
                   + "so there is nothing to key a saved setting on. "
                   + "The setting still applies to this instance.";
        }

        /// <summary>
        /// Retrouve le réglage d'instance d'une entrée.
        ///
        /// La date de création vaut zéro pendant la seconde qui suit
        /// l'apparition d'une entrée, avant le premier relevé. On accepte alors
        /// le rapprochement sur le seul PID, d'un côté comme de l'autre : deux
        /// PID identiques ne peuvent pas coexister, et dès que la date est
        /// connue des deux côtés, elle doit concorder.
        /// </summary>
        private static InstanceSetting FindInstance(Process process, bool create)
        {
            if (process == null)
            {
                return null;
            }

            foreach (InstanceSetting setting in InstanceSettings)
            {
                if (setting.ProcessID != process.ProcessID
                    || setting.IsService != process.IsService)
                {
                    continue;
                }

                if (process.IsService
                    && !string.Equals(setting.ServiceName, process.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (setting.CreateTime != 0 && process.CreateTime != 0
                    && setting.CreateTime != process.CreateTime)
                {
                    // PID réutilisé : le réglage appartenait au précédent.
                    continue;
                }

                // La date devient connue : on la fixe, et la comparaison
                // suivante sera exacte.
                if (setting.CreateTime == 0)
                {
                    setting.CreateTime = process.CreateTime;
                }

                return setting;
            }

            if (!create)
            {
                return null;
            }

            InstanceSetting added = new InstanceSetting
            {
                ProcessID = process.ProcessID,
                CreateTime = process.CreateTime,
                IsService = process.IsService,
                ServiceName = process.IsService ? process.ProcessName : null,
            };

            InstanceSettings.Add(added);

            return added;
        }

        /// <summary>
        /// Oublie les réglages d'instance dont le processus a disparu. Appelé
        /// depuis le relevé : sans cela, un réglage posé sur une instance morte
        /// se transmettrait à un PID réutilisé qui aurait la même date nulle.
        /// </summary>
        public static void ForgetDeadInstances(IEnumerable<Process> living)
        {
            if (living == null)
            {
                return;
            }

            HashSet<string> alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Process process in living)
            {
                if (process == null)
                {
                    continue;
                }

                alive.Add(process.ProcessID + "|" + process.CreateTime + "|"
                          + (process.IsService ? process.ProcessName : string.Empty));
            }

            bool removed = false;

            lock (SyncRoot)
            {
                for (int i = InstanceSettings.Count - 1; i >= 0; i--)
                {
                    InstanceSetting setting = InstanceSettings[i];

                    string key = setting.ProcessID + "|" + setting.CreateTime + "|"
                                 + (setting.IsService ? setting.ServiceName : string.Empty);

                    if (alive.Contains(key))
                    {
                        continue;
                    }

                    // Une entrée dont la date n'est pas encore connue ne doit pas
                    // être jetée au premier relevé venu.
                    if (setting.CreateTime == 0)
                    {
                        continue;
                    }

                    InstanceSettings.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                Raise();
            }
        }

        // ------------------------------------------------------------ épinglage

        /// <summary>
        /// Rang d'épinglage, ou -1. Le réglage d'instance prime sur celui de
        /// l'exécutable : un geste direct sur une tuile l'emporte sur ce qui a
        /// été retenu pour le programme.
        /// </summary>
        public static int GetPinIndex(Process process)
        {
            if (process == null)
            {
                return -1;
            }

            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, false);

                if (instance != null && instance.Pinned)
                {
                    return instance.PinOrder;
                }

                string key = GetExecutableKey(process);

                if (key == null)
                {
                    return -1;
                }

                List<string> stored = SettingsStore.Current.PinnedEntries;

                if (stored == null)
                {
                    return -1;
                }

                int index = stored.FindIndex(entry => string.Equals(entry, key, StringComparison.OrdinalIgnoreCase));

                // Les rangs persistants viennent après les rangs d'instance :
                // ce qu'on vient d'épingler à la main passe devant ce qui l'est
                // depuis toujours.
                return index < 0 ? -1 : index;
            }
        }

        public static bool IsPinned(Process process)
        {
            return GetPinIndex(process) >= 0;
        }

        /// <summary>Vrai quand l'épinglage vient d'un réglage d'instance.</summary>
        public static bool IsPinnedForInstance(Process process)
        {
            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, false);

                return instance != null && instance.Pinned;
            }
        }

        public static bool IsPinnedForExecutable(Process process)
        {
            string key = GetExecutableKey(process);

            if (key == null)
            {
                return false;
            }

            lock (SyncRoot)
            {
                List<string> stored = SettingsStore.Current.PinnedEntries;

                return stored != null
                       && stored.Any(entry => string.Equals(entry, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Épingle ou désépingle cette instance seulement.</summary>
        public static void SetPinnedForInstance(Process process, bool pinned)
        {
            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, pinned);

                if (instance == null)
                {
                    return;
                }

                instance.Pinned = pinned;

                if (pinned)
                {
                    instance.PinOrder = _nextPinOrder++;
                }
            }

            Raise();
        }

        /// <summary>
        /// Retient l'épinglage pour toutes les occurrences de l'exécutable, ou
        /// l'oublie.
        /// </summary>
        public static bool TrySetPinnedForExecutable(Process process, bool pinned, out string error)
        {
            error = null;

            string key = GetExecutableKey(process);

            if (key == null)
            {
                return true;
            }

            lock (SyncRoot)
            {
                List<string> stored = SettingsStore.Current.PinnedEntries ?? new List<string>();

                int index = stored.FindIndex(entry => string.Equals(entry, key, StringComparison.OrdinalIgnoreCase));

                if (pinned)
                {
                    if (index >= 0)
                    {
                        return true;
                    }

                    stored.Add(key);
                }
                else
                {
                    if (index < 0)
                    {
                        return true;
                    }

                    stored.RemoveAt(index);
                }

                SettingsStore.Current.PinnedEntries = stored.Count == 0 ? null : stored;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        // --------------------------------------------------------- agrandissement

        /// <summary>
        /// Facteur d'agrandissement effectif, toujours entre 1 et
        /// <see cref="MaximumScale"/>. Le réglage d'instance prime.
        /// </summary>
        public static int GetScale(Process process)
        {
            if (process == null)
            {
                return 1;
            }

            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, false);

                // Le choix d'instance prime dès qu'il existe, Normal compris.
                // Le tester par « > 1 » rendait la taille retenue pour le
                // programme impossible à contredire sur une tuile : choisir
                // Normal retombait sur elle, et la tuile ne bougeait pas.
                if (instance != null && instance.Scale != null)
                {
                    return Clamp(instance.Scale.Value);
                }

                string key = GetExecutableKey(process);

                if (key == null)
                {
                    return 1;
                }

                Dictionary<string, int> stored = SettingsStore.Current.TileScales;

                int scale;

                if (stored == null || !stored.TryGetValue(key, out scale))
                {
                    return 1;
                }

                return Clamp(scale);
            }
        }

        public static int GetScaleForInstance(Process process)
        {
            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, false);

                return instance == null || instance.Scale == null
                       ? 1
                       : Clamp(instance.Scale.Value);
            }
        }

        public static int GetScaleForExecutable(Process process)
        {
            string key = GetExecutableKey(process);

            if (key == null)
            {
                return 1;
            }

            lock (SyncRoot)
            {
                Dictionary<string, int> stored = SettingsStore.Current.TileScales;

                int scale;

                return stored != null && stored.TryGetValue(key, out scale) ? Clamp(scale) : 1;
            }
        }

        /// <summary>
        /// Fixe la taille de cette instance seulement. Normal s'enregistre
        /// comme les autres : c'est un choix, pas une absence de choix, et il
        /// doit pouvoir contredire la taille retenue pour le programme.
        /// </summary>
        public static void SetScaleForInstance(Process process, int scale)
        {
            scale = Clamp(scale);

            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, true);

                if (instance == null)
                {
                    return;
                }

                instance.Scale = scale;
            }

            Raise();
        }

        /// <summary>
        /// Retient l'agrandissement pour toutes les occurrences de
        /// l'exécutable. Une échelle de 1 ne laisse aucune trace : le fichier
        /// ne porte que ce qui s'écarte de l'ordinaire.
        /// </summary>
        public static bool TrySetScaleForExecutable(Process process, int scale, out string error)
        {
            error = null;

            string key = GetExecutableKey(process);

            if (key == null)
            {
                return true;
            }

            scale = Clamp(scale);

            lock (SyncRoot)
            {
                Dictionary<string, int> stored = SettingsStore.Current.TileScales
                                                 ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (scale <= 1)
                {
                    if (!stored.Remove(key))
                    {
                        return true;
                    }
                }
                else
                {
                    int current;

                    if (stored.TryGetValue(key, out current) && current == scale)
                    {
                        return true;
                    }

                    stored[key] = scale;
                }

                SettingsStore.Current.TileScales = stored.Count == 0 ? null : stored;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        // ------------------------------------------- valeurs sur la tuile

        /// <summary>
        /// Valeurs chiffrées affichées sur une tuile.
        ///
        /// Ce réglage était global : les deux entrées du menu contextuel
        /// agissaient sur toutes les tuiles. Choisir explicitement une tuile
        /// revient pourtant à écarter les autres, et l'argument de
        /// comparabilité ne tient pas — un chiffre affiché sur une seule tuile
        /// n'est pas moins lisible, il est simplement le seul.
        ///
        /// Même portée que la taille et la couleur : un geste direct ne vaut
        /// que pour l'instance, un réglage enregistré vaut pour l'exécutable.
        /// </summary>
        public static void GetValueDisplay(Process process, out bool cpu, out bool memory)
        {
            cpu = false;
            memory = false;

            if (process == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, false);

                // Trois états et non deux : « pas de réglage d'instance » se
                // distingue de « réglage d'instance à faux », sans quoi une
                // tuile ne pourrait jamais éteindre ce que l'exécutable allume.
                if (instance != null && instance.ShowCpuValue != null)
                {
                    cpu = instance.ShowCpuValue.Value;
                    memory = instance.ShowMemoryValue ?? false;

                    return;
                }

                GetValueDisplayByKey(GetExecutableKey(process), out cpu, out memory);
            }
        }

        public static void GetValueDisplayByKey(string key, out bool cpu, out bool memory)
        {
            cpu = false;
            memory = false;

            if (key == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                Dictionary<string, string> stored = SettingsStore.Current.TileValues;

                string text;

                if (stored == null || !stored.TryGetValue(key, out text) || text == null)
                {
                    return;
                }

                cpu = text.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) >= 0;
                memory = text.IndexOf("ram", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static void SetValueDisplayForInstance(Process process, bool cpu, bool memory)
        {
            lock (SyncRoot)
            {
                InstanceSetting instance = FindInstance(process, true);

                if (instance == null)
                {
                    return;
                }

                instance.ShowCpuValue = cpu;
                instance.ShowMemoryValue = memory;
            }

            Raise();
        }

        public static bool TrySetValueDisplayForExecutable(
            Process process, bool cpu, bool memory, out string error)
        {
            return TrySetValueDisplayByKey(GetExecutableKey(process), cpu, memory, out error);
        }

        public static bool TrySetValueDisplayByKey(string key, bool cpu, bool memory, out string error)
        {
            error = null;

            if (key == null)
            {
                return true;
            }

            lock (SyncRoot)
            {
                Dictionary<string, string> stored = SettingsStore.Current.TileValues
                                                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Rien d'allumé ne laisse aucune trace : le fichier ne porte
                // que ce qui s'écarte de l'ordinaire.
                if (!cpu && !memory)
                {
                    if (!stored.Remove(key))
                    {
                        return true;
                    }
                }
                else
                {
                    string text = cpu && memory ? "cpu,ram" : cpu ? "cpu" : "ram";

                    string current;

                    if (stored.TryGetValue(key, out current) && current == text)
                    {
                        return true;
                    }

                    stored[key] = text;
                }

                SettingsStore.Current.TileValues = stored.Count == 0 ? null : stored;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        // ------------------------------------------------- couleur de fond

        /// <summary>
        /// Clé persistante construite depuis un chemin, pour les appelants qui
        /// n'ont pas d'entrée vivante sous la main — l'édition d'une
        /// configuration, par exemple, qui ne connaît qu'un chemin.
        /// </summary>
        public static string GetKeyFromExecutablePath(string executablePath)
        {
            return string.IsNullOrEmpty(executablePath) ? null : PathPrefix + executablePath;
        }

        /// <summary>
        /// Couleur de fond propre à ce processus, ou null quand il suit la
        /// palette générale.
        ///
        /// Une couleur posée ici l'emporte sur le rôle « Tile background » des
        /// réglages généraux : c'est tout l'intérêt, repérer un programme dans
        /// une grille de quatre cents tuiles.
        /// </summary>
        public static Color? GetBackgroundColor(Process process)
        {
            return GetBackgroundColorByKey(GetExecutableKey(process));
        }

        public static Color? GetBackgroundColorByKey(string key)
        {
            if (key == null)
            {
                return null;
            }

            lock (SyncRoot)
            {
                Dictionary<string, string> stored = SettingsStore.Current.TileBackgrounds;

                string text;

                if (stored == null || !stored.TryGetValue(key, out text))
                {
                    return null;
                }

                Color color;

                // Valeur illisible : on retombe sur la palette générale plutôt
                // que de peindre n'importe quoi.
                return ColorPalette.TryParse(text, out color) ? color : (Color?)null;
            }
        }

        public static bool TrySetBackgroundColor(Process process, Color? color, out string error)
        {
            return TrySetBackgroundColorByKey(GetExecutableKey(process), color, out error);
        }

        /// <summary>
        /// Pose la couleur, ou la retire quand <paramref name="color"/> est
        /// null — c'est le retour à la couleur générale.
        /// </summary>
        public static bool TrySetBackgroundColorByKey(string key, Color? color, out string error)
        {
            error = null;

            if (key == null)
            {
                return true;
            }

            lock (SyncRoot)
            {
                Dictionary<string, string> stored = SettingsStore.Current.TileBackgrounds
                                                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (color == null)
                {
                    if (!stored.Remove(key))
                    {
                        return true;
                    }
                }
                else
                {
                    string text = ColorPalette.Format(color.Value);

                    string current;

                    if (stored.TryGetValue(key, out current) && current == text)
                    {
                        return true;
                    }

                    stored[key] = text;
                }

                SettingsStore.Current.TileBackgrounds = stored.Count == 0 ? null : stored;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        /// <summary>Échelle persistante lue depuis une clé, pour l'édition d'une configuration.</summary>
        public static int GetScaleByKey(string key)
        {
            if (key == null)
            {
                return 1;
            }

            lock (SyncRoot)
            {
                Dictionary<string, int> stored = SettingsStore.Current.TileScales;

                int scale;

                return stored != null && stored.TryGetValue(key, out scale) ? Clamp(scale) : 1;
            }
        }

        /// <summary>Pose l'échelle persistante depuis une clé.</summary>
        public static bool TrySetScaleByKey(string key, int scale, out string error)
        {
            error = null;

            if (key == null)
            {
                return true;
            }

            scale = Clamp(scale);

            lock (SyncRoot)
            {
                Dictionary<string, int> stored = SettingsStore.Current.TileScales
                                                 ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (scale <= 1)
                {
                    if (!stored.Remove(key))
                    {
                        return true;
                    }
                }
                else
                {
                    int current;

                    if (stored.TryGetValue(key, out current) && current == scale)
                    {
                        return true;
                    }

                    stored[key] = scale;
                }

                SettingsStore.Current.TileScales = stored.Count == 0 ? null : stored;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        private static int Clamp(int scale)
        {
            if (scale < 1)
            {
                return 1;
            }

            return scale > MaximumScale ? MaximumScale : scale;
        }

        /// <summary>
        /// Remet tout à plat, instances comprises. Sortie de secours si
        /// l'affichage devient ingérable.
        /// </summary>
        public static bool TryResetAll(out string error)
        {
            lock (SyncRoot)
            {
                InstanceSettings.Clear();

                SettingsStore.Current.PinnedEntries = null;
                SettingsStore.Current.TileScales = null;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        /// <summary>
        /// Clés persistantes épinglées, dans l'ordre où elles l'ont été. Les
        /// réglages d'instance n'y figurent pas : ils ne sont pas persistants.
        /// </summary>
        public static IReadOnlyList<string> PinnedKeys
        {
            get
            {
                lock (SyncRoot)
                {
                    List<string> stored = SettingsStore.Current.PinnedEntries;

                    return stored == null ? new List<string>() : new List<string>(stored);
                }
            }
        }

        public static int PinnedCount
        {
            get
            {
                lock (SyncRoot)
                {
                    List<string> stored = SettingsStore.Current.PinnedEntries;

                    return (stored == null ? 0 : stored.Count)
                           + InstanceSettings.Count(setting => setting.Pinned);
                }
            }
        }

        public static int ScaledCount
        {
            get
            {
                lock (SyncRoot)
                {
                    Dictionary<string, int> stored = SettingsStore.Current.TileScales;

                    return (stored == null ? 0 : stored.Count)
                           + InstanceSettings.Count(setting => setting.Scale > 1);
                }
            }
        }

        private static void Raise()
        {
            Action changed = Changed;

            if (changed != null)
            {
                changed();
            }
        }
    }
}
