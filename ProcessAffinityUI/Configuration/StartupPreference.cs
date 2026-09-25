using System;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Accorde le souvenir du choix, dans le fichier de préférences, et l'état
    /// réel, dans le registre.
    ///
    /// Le registre fait foi. Le fichier n'est qu'un souvenir, et il peut être
    /// démenti : l'utilisateur peut retirer l'entrée avec msconfig, le
    /// Gestionnaire des tâches ou regedit, sans que l'application en sache rien.
    /// </summary>
    public static class StartupPreference
    {
        /// <summary>Ce que l'application doit faire au lancement.</summary>
        public enum StartupDecision
        {
            /// <summary>Ne rien faire : déjà inscrit, ou refus définitif.</summary>
            Nothing,

            /// <summary>Poser la question.</summary>
            Ask,

            /// <summary>L'entrée a disparu du registre alors qu'on la croyait posée.</summary>
            RemovedExternally,
        }

        /// <summary>
        /// Ce qu'il convient de faire, et l'alignement du souvenir sur le réel.
        /// N'écrit jamais dans le registre : décider n'est pas agir.
        /// </summary>
        public static StartupDecision Evaluate()
        {
            AppSettings settings = SettingsStore.Current;
            bool registered = StartupRegistration.IsRegistered();

            if (registered)
            {
                // Inscrit : rien à demander. Le souvenir est aligné s'il ne l'était
                // pas — l'utilisateur a pu poser l'entrée lui-même.
                if (settings.StartWithWindows != true)
                {
                    settings.StartWithWindows = true;
                    TrySaveQuietly();
                }

                return StartupDecision.Nothing;
            }

            if (settings.StartWithWindows == true)
            {
                // On croyait l'entrée posée, elle ne l'est plus : c'est un geste
                // délibéré de l'utilisateur, fait hors de l'application. On ne la
                // remet pas, et on cesse de demander — reposer la question au
                // prochain lancement reviendrait à contester ce geste.
                settings.StartWithWindows = false;
                settings.DoNotAskStartWithWindows = true;
                TrySaveQuietly();

                return StartupDecision.RemovedExternally;
            }

            return settings.DoNotAskStartWithWindows ? StartupDecision.Nothing : StartupDecision.Ask;
        }

        /// <summary>
        /// Enregistre la réponse et agit en conséquence. Le souvenir n'est écrit
        /// que si l'action sur le registre a réussi : retenir « oui » alors que
        /// l'entrée n'a pas pu être posée ferait croire au prochain lancement
        /// qu'elle a été retirée à la main.
        /// </summary>
        public static bool TryApplyAnswer(bool accepted, bool doNotAskAgain, out string error)
        {
            error = null;

            bool changed = accepted
                ? StartupRegistration.TryRegister(out error)
                : StartupRegistration.TryUnregister(out error);

            AppSettings settings = SettingsStore.Current;

            if (changed)
            {
                settings.StartWithWindows = accepted;
            }

            settings.DoNotAskStartWithWindows = doNotAskAgain || (accepted && changed);

            string saveError;

            if (!SettingsStore.TrySave(out saveError) && string.IsNullOrEmpty(error))
            {
                error = saveError;
            }

            return changed;
        }

        /// <summary>État courant, pour l'affichage. Lit le registre, pas le souvenir.</summary>
        public static bool IsEnabled()
        {
            return StartupRegistration.IsRegistered();
        }

        private static void TrySaveQuietly()
        {
            string error;

            SettingsStore.TrySave(out error);
        }
    }
}
