using System;
using Microsoft.Win32;

namespace ProcessAffinityUI.Configuration
{
    /// <summary>
    /// Inscription au démarrage de la session, par la clé « Run » de l'utilisateur
    /// courant.
    ///
    /// HKEY_CURRENT_USER et jamais HKEY_LOCAL_MACHINE : la clé machine exige une
    /// élévation, et lancerait l'application pour tous les comptes — deux
    /// conséquences qu'une case à cocher ne saurait porter. L'écriture ici ne
    /// demande aucun droit particulier.
    /// </summary>
    public static class StartupRegistration
    {
        private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Nom de la valeur. Stable : c'est lui qui identifie notre entrée.</summary>
        private const string ValueName = "ProcessAffinity";

        private static string _keyPathOverride;

        private static string RunKeyPath
        {
            get { return string.IsNullOrEmpty(_keyPathOverride) ? DefaultRunKeyPath : _keyPathOverride; }
        }

        /// <summary>
        /// Déplace la clé vers un emplacement d'essai. Passer null rend la clé
        /// réelle.
        ///
        /// Même raison que pour le dossier des règles : une sonde ne doit pas
        /// écrire dans ce qui règle le démarrage de la session de l'utilisateur.
        /// Une entrée oubliée là ferait démarrer un exécutable d'essai à chaque
        /// ouverture de session.
        /// </summary>
        public static void RedirectTo(string subKeyPath)
        {
            _keyPathOverride = subKeyPath;
        }

        public static string RegistryPath
        {
            get { return @"HKEY_CURRENT_USER\" + RunKeyPath; }
        }

        /// <summary>
        /// Chemin de l'exécutable en cours, entre guillemets : sans eux, un chemin
        /// contenant une espace serait coupé au premier blanc par le chargeur.
        /// </summary>
        public static string GetCommandLine()
        {
            string path = Environment.ProcessPath;

            return string.IsNullOrEmpty(path) ? null : "\"" + path + "\"";
        }

        /// <summary>
        /// Valeur actuellement inscrite, ou null s'il n'y en a aucune. Distingue
        /// « absente » de « présente mais pointant ailleurs » : une entrée laissée
        /// par une autre installation ne doit pas être prise pour la nôtre.
        /// </summary>
        public static string ReadRegisteredCommandLine()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key == null ? null : key.GetValue(ValueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Vrai si l'entrée existe et désigne cet exécutable.</summary>
        public static bool IsRegistered()
        {
            string registered = ReadRegisteredCommandLine();
            string expected = GetCommandLine();

            if (string.IsNullOrEmpty(registered) || string.IsNullOrEmpty(expected))
            {
                return false;
            }

            return string.Equals(
                registered.Trim().Trim('"'),
                expected.Trim().Trim('"'),
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryRegister(out string error)
        {
            error = null;

            string commandLine = GetCommandLine();

            if (string.IsNullOrEmpty(commandLine))
            {
                error = "The path of the running executable could not be determined, "
                        + "so ProcessAffinity cannot add itself to startup.";

                return false;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        error = "The startup key could not be opened for writing.";

                        return false;
                    }

                    key.SetValue(ValueName, commandLine, RegistryValueKind.String);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "The startup entry could not be written: " + exception.Message;

                return false;
            }
        }

        /// <summary>
        /// Retire l'entrée. Une entrée déjà absente n'est pas une erreur : le
        /// résultat voulu est atteint.
        /// </summary>
        public static bool TryUnregister(out string error)
        {
            error = null;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "The startup entry could not be removed: " + exception.Message;

                return false;
            }
        }
    }
}
