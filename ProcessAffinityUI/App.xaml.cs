using ProcessAffinityUI.Threading;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace ProcessAffinityUI
{

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Sans ces gestionnaires, une exception non interceptée fermait
        /// l'application sans un mot : sur le thread de l'IHM, WPF termine le
        /// processus faute de gestionnaire ; sur un autre thread, le CLR le
        /// termine toujours.
        /// </summary>
        private void InstallGlobalExceptionHandlers()
        {
            this.DispatcherUnhandledException += (sender, e) =>
            {
                // Récupérable : on signale et on laisse l'application vivre.
                ReportUnhandledException("An unexpected error occurred.", e.Exception);
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                // Non récupérable, mais au moins l'utilisateur saura pourquoi.
                ReportUnhandledException("A fatal error occurred, the application will close.", e.ExceptionObject as Exception);
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                // Observer sans rien dire est précisément ce qui avait masqué
                // l'arrêt de l'échantillonneur à l'étape 2a.
                System.Diagnostics.Debug.WriteLine("[ProcessAffinity] Exception de tâche non observée : " + e.Exception);
                e.SetObserved();
            };
        }

        /// <summary>
        /// Signatures déjà signalées : l'échantillonnage pousse environ une mise
        /// à jour par tuile et par seconde dans le dispatcher, donc une erreur
        /// récurrente ouvrirait des centaines de boîtes de dialogue. Seule la
        /// première occurrence d'une même erreur est montrée ; les suivantes ne
        /// vont qu'à la trace.
        /// </summary>
        private static readonly HashSet<string> ReportedExceptionSignatures = new HashSet<string>();

        private static readonly object ReportSyncRoot = new object();

        private static void ReportUnhandledException(string header, Exception exception)
        {
            try
            {
                string detail = exception == null
                    ? "Unknown error."
                    : exception.GetType().Name + ": " + exception.Message;

                System.Diagnostics.Debug.WriteLine("[ProcessAffinity] " + header + " " + detail);

                bool alreadyReported;

                lock (ReportSyncRoot)
                {
                    alreadyReported = !ReportedExceptionSignatures.Add(header + "|" + detail);
                }

                if (alreadyReported)
                {
                    return;
                }

                MessageBox.Show(header + "\r\n\r\n" + detail, "ProcessAffinity",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // Ne jamais laisser le rapport d'erreur provoquer une erreur.
            }
        }

        /// <summary>
        /// SeDebugPrivilege figure dans le jeton d'une session élevée, mais
        /// désactivé — et OpenProcess ne tient compte que des privilèges activés.
        /// Sans cette activation, lancer l'application en administrateur ne
        /// change rien à la couverture.
        ///
        /// Appelée en tout premier : Application.Startup précède la création de
        /// la fenêtre principale, donc celle de toute entrée dont IsModifiable
        /// met le résultat en cache.
        ///
        /// Échoue sans conséquence en session non élevée, le privilège n'étant
        /// alors pas présent dans le jeton.
        /// </summary>
        private static void EnableDebugPrivilege()
        {
            try
            {
                System.Diagnostics.Process.EnterDebugMode();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Commutateurs acceptés pour démarrer sans appliquer la moindre règle.
        /// C'est la sortie de secours : une configuration qui bride tout sur un
        /// seul cœur rendrait la machine inutilisable, et l'application avec elle.
        /// </summary>
        private static readonly string[] NoRulesSwitches =
        {
            "--no-rules", "-no-rules", "/no-rules", "/norules", "--norules",
        };

        private static bool IsNoRulesRequested(string[] args)
        {
            foreach (string argument in args)
            {
                foreach (string candidate in NoRulesSwitches)
                {
                    if (string.Equals(argument.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            InstallGlobalExceptionHandlers();
            EnableDebugPrivilege();

            // Évalué avant toute création d'entrée : aucune règle ne doit avoir eu
            // le temps d'être appliquée quand l'utilisateur demande à s'en passer.
            if (IsNoRulesRequested(e.Args))
            {
                ProcessAffinityUI.Configuration.RuleEngine.IsDisabled = true;

                MessageBox.Show(
                    "Started with saved rules disabled.\r\n\r\n"
                    + "No affinity or priority rule will be applied during this session. "
                    + "Existing rules are left untouched in the rules file.",
                    "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Le pilotage en ligne de commande attend des arguments « clé:valeur »
            // et se termine par Environment.Exit. Un commutateur seul — sans
            // deux-points — n'en fait pas partie : sans ce filtre, « --no-rules »
            // faisait lever le découpage puis quittait l'application.
            bool hasKeyedArguments = false;

            foreach (string argument in e.Args)
            {
                if (argument.IndexOf(':') > 0)
                {
                    hasKeyedArguments = true;
                    break;
                }
            }

            if (hasKeyedArguments)
            {
                string computerName = string.Empty;
                string domain = string.Empty;
                string user = string.Empty;
                string password = string.Empty;
                int core = 0;

                foreach (var a in e.Args)
                {
                    if (a.IndexOf(':') <= 0)
                    {
                        continue;
                    }

                    switch (a.ToUpper().Substring(0, a.IndexOf(':')))
                    {
                        case "COMPUTER":
                            computerName = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "C":
                            computerName = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "DOMAIN":
                            domain = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "D":
                            domain = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "USER":
                            user = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "U":
                            user = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "PASSWORD":
                            password = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "P":
                            password = a.ToUpper().Substring(a.IndexOf(':') + 1);
                            break;
                        case "CORE":
                            core = int.Parse(a.ToUpper().Substring(a.IndexOf(':') + 1));
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(computerName) & !string.IsNullOrEmpty(domain) & !string.IsNullOrEmpty(user) & !string.IsNullOrEmpty(password))
                {
                    Processes processes = new Processes(computerName, domain, user, password);

                    // Les fenêtres travaillent désormais sur les entrées et non
                    // sur les tuiles : le pilotage en ligne de commande n'a plus
                    // à construire de contrôles qu'il n'affiche jamais.
                    ProcessAffinityWindow paw = new ProcessAffinityWindow(processes.Snapshot().ToList());

                    if (core > -1)
                    {
                        paw.SetCPUCheckBoxesUnchecked();
                        paw.SetCPUCheckBox(core, true);
                    }
                    else
                    {
                        paw.SetCPUCheckBoxesChecked();
                    }

                    paw.SetProcessorsAffinities();
                    paw = null;
                }

                Environment.Exit(0);
            }
        }
    }
}
