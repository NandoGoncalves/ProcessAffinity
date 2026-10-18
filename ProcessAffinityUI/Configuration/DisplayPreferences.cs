using System;
using System.Collections.Generic;
using System.Linq;

using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Ce que l'utilisateur a décidé de l'affichage d'une entrée : son épinglage
    /// en tête, et le facteur d'agrandissement de sa tuile.
    ///
    /// Dans <c>settings.json</c> et non dans le fichier de règles. Y loger ces
    /// deux réglages ferait monter la version du format des règles pour un
    /// motif purement cosmétique, et déclencherait le refus en bloc décrit au
    /// CLAUDE.md dès qu'on reviendrait à un binaire antérieur — perdre ses
    /// règles parce qu'on a agrandi une tuile serait absurde.
    ///
    /// ## L'identité d'une entrée
    ///
    /// Trois candidats, et un seul tient :
    ///
    /// Le **PID** identifie exactement la bonne tuile et survit à toutes les
    /// reconstructions du panneau, puisque le processus, lui, continue de
    /// tourner. Mais il est réattribué : relu au démarrage suivant, il
    /// désignerait un processus sans rapport. Un épinglage persistant ne peut
    /// pas en dépendre.
    ///
    /// Le **chemin de l'exécutable** est ce qu'utilisent les règles, et il
    /// survit à tout. Mais il n'est pas lisible : mesuré sur cette machine en
    /// session non élevée, **169 processus sur 274 — 62 % — n'exposent pas leur
    /// chemin**, le descripteur de sécurité du processus l'interdisant. Un
    /// épinglage par chemin serait indisponible sur la majorité des tuiles, et
    /// il le serait précisément sur les tuiles système, celles qu'on surveille.
    ///
    /// Reste le **nom**, qui est lisible sur toutes les entrées :
    ///
    /// - pour un service, c'est son nom court, et il est **exact** — 147 noms
    ///   distincts pour 147 services, vérifié ;
    /// - pour un processus, c'est le nom de l'exécutable, qui désigne donc
    ///   **toutes les instances du même programme**. Mesuré : msedge.exe en
    ///   compte vingt sur cette machine, et svchost.exe quatre-vingt-quatorze.
    ///   Le chemin n'en identifiait que huit, les autres n'exposant pas le
    ///   leur — le nom ne crée donc pas ce regroupement, il le rend visible.
    ///
    /// Cette portée est assumée plutôt que subie : c'est déjà celle des règles,
    /// que la documentation énonce — « a rule applies to every launch of the
    /// same executable » —, et l'entrée de menu annonce le nombre de tuiles
    /// concernées avant qu'on clique.
    /// </summary>
    public static class DisplayPreferences
    {
        private const string ServicePrefix = "service:";
        private const string ProcessPrefix = "process:";

        /// <summary>
        /// Agrandissement maximal. Au-delà, une tuile couvre tant de surface
        /// qu'elle chasse tout le reste de l'écran : à 3× elle occupe déjà neuf
        /// fois les pixels d'une tuile normale.
        /// </summary>
        public const int MaximumScale = 3;

        private static readonly object SyncRoot = new object();

        /// <summary>
        /// Levé après tout changement. La fenêtre principale s'en sert pour
        /// réordonner le panneau sans le reconstruire.
        /// </summary>
        public static event Action Changed;

        public static string GetKey(Process process)
        {
            if (process == null || string.IsNullOrEmpty(process.ProcessName))
            {
                return null;
            }

            return (process.IsService ? ServicePrefix : ProcessPrefix) + process.ProcessName;
        }

        // ------------------------------------------------------------ épinglage

        /// <summary>
        /// Clés épinglées, dans l'ordre où elles l'ont été. C'est bien une
        /// liste ordonnée et non un ensemble : la demande est que les tuiles
        /// épinglées se suivent dans cet ordre.
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

        public static bool IsPinned(Process process)
        {
            return GetPinIndex(process) >= 0;
        }

        /// <summary>
        /// Rang d'épinglage, ou -1. C'est la clé de tri du panneau : les rangs
        /// d'abord, dans l'ordre, puis tout le reste.
        /// </summary>
        public static int GetPinIndex(Process process)
        {
            string key = GetKey(process);

            if (key == null)
            {
                return -1;
            }

            lock (SyncRoot)
            {
                List<string> stored = SettingsStore.Current.PinnedEntries;

                if (stored == null)
                {
                    return -1;
                }

                return stored.FindIndex(entry => string.Equals(entry, key, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static bool TryPin(Process process, out string error)
        {
            return TrySetPinned(process, true, out error);
        }

        public static bool TryUnpin(Process process, out string error)
        {
            return TrySetPinned(process, false, out error);
        }

        private static bool TrySetPinned(Process process, bool pinned, out string error)
        {
            error = null;

            string key = GetKey(process);

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
                    // Déjà épinglée : on ne la remet pas en queue d'ordre, son
                    // rang lui appartient.
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
        /// Facteur d'agrandissement, toujours entre 1 et
        /// <see cref="MaximumScale"/>. Une valeur aberrante venue d'un fichier
        /// édité à la main est ramenée dans les bornes plutôt que refusée : une
        /// préférence d'affichage ne doit pas empêcher l'application de
        /// démarrer.
        /// </summary>
        public static int GetScale(Process process)
        {
            string key = GetKey(process);

            if (key == null)
            {
                return 1;
            }

            lock (SyncRoot)
            {
                Dictionary<string, int> stored = SettingsStore.Current.TileScales;

                int scale;

                if (stored == null || !stored.TryGetValue(key, out scale))
                {
                    return 1;
                }

                return Clamp(scale);
            }
        }

        public static bool TrySetScale(Process process, int scale, out string error)
        {
            error = null;

            string key = GetKey(process);

            if (key == null)
            {
                return true;
            }

            scale = Clamp(scale);

            lock (SyncRoot)
            {
                Dictionary<string, int> stored = SettingsStore.Current.TileScales
                                                 ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Une tuile à l'échelle normale ne laisse aucune trace : le
                // fichier ne porte que ce qui s'écarte de l'ordinaire.
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
        /// Remet tout à plat. Utile depuis les vérifications, et comme sortie
        /// de secours si l'affichage devient ingérable.
        /// </summary>
        public static bool TryResetAll(out string error)
        {
            lock (SyncRoot)
            {
                SettingsStore.Current.PinnedEntries = null;
                SettingsStore.Current.TileScales = null;
            }

            bool written = SettingsStore.TrySave(out error);

            Raise();

            return written;
        }

        public static int PinnedCount
        {
            get { return PinnedKeys.Count; }
        }

        public static int ScaledCount
        {
            get
            {
                lock (SyncRoot)
                {
                    Dictionary<string, int> stored = SettingsStore.Current.TileScales;

                    return stored == null ? 0 : stored.Count;
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
