using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Niveau d'élévation de la session, et relance élevée.
    ///
    /// Le manifeste ne demande pas <c>requireAdministrator</c> : l'application
    /// tourne avec les droits de qui la lance, et ne réclame l'élévation qu'au
    /// moment où un refus la rend nécessaire. Jusqu'ici, un refus d'accès se
    /// traduisait par un message invitant l'utilisateur à relancer lui-même ;
    /// rien n'offrait de le faire.
    /// </summary>
    internal static class Elevation
    {
        private const string ApplicationName = "ProcessAffinity";

        private static bool? _isElevated;

        /// <summary>
        /// Vrai quand le jeton courant appartient au groupe Administrateurs.
        /// C'est cette appartenance qui débloque la couverture, les DACL des
        /// processus et des services l'accordant — pas un privilège particulier.
        /// </summary>
        public static bool IsElevated
        {
            get
            {
                if (_isElevated == null)
                {
                    try
                    {
                        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                        {
                            _isElevated = new WindowsPrincipal(identity)
                                .IsInRole(WindowsBuiltInRole.Administrator);
                        }
                    }
                    catch
                    {
                        _isElevated = false;
                    }
                }

                return _isElevated.Value;
            }
        }

        /// <summary>
        /// Propose la relance élevée après un refus d'accès, et la fait si
        /// l'utilisateur accepte.
        ///
        /// Ne propose rien si la session est déjà élevée : le refus vient alors
        /// d'ailleurs, et relancer ne changerait rien.
        /// Retourne vrai quand une instance élevée a démarré, l'instance
        /// courante devant alors se retirer.
        /// </summary>
        public static bool OfferElevatedRestart(string reason)
        {
            if (IsElevated)
            {
                return false;
            }

            string message = reason
                + "\r\n\r\nMost services can only be controlled by an administrator."
                + "\r\n\r\nRestart ProcessAffinity as administrator?";

            if (MessageBox.Show(message, ApplicationName, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return false;
            }

            string error;

            if (TryRestartElevated(out error))
            {
                return true;
            }

            if (error != null)
            {
                MessageBox.Show(
                    "ProcessAffinity could not be restarted as administrator.\r\n\r\n" + error,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        /// <summary>
        /// Relance l'exécutable courant sous le verbe « runas ».
        ///
        /// Un refus de l'invite UAC se reconnaît au code 1223
        /// (ERROR_CANCELLED) : ce n'est pas une panne, et il ne faut pas en
        /// faire un message d'erreur — d'où un <paramref name="error"/> nul.
        /// </summary>
        public static bool TryRestartElevated(out string error)
        {
            error = null;

            string executablePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(executablePath))
            {
                error = "The path of the running executable could not be determined.";
                return false;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = System.IO.Path.GetDirectoryName(executablePath) ?? string.Empty,
                };

                System.Diagnostics.Process.Start(startInfo);

                return true;
            }
            catch (Win32Exception exception)
            {
                // 1223 : l'utilisateur a refusé l'invite. Il sait ce qu'il a fait.
                if (exception.NativeErrorCode != 1223)
                {
                    error = exception.Message;
                }

                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
}
