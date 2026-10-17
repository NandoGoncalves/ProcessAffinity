using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Enchaînement complet d'une commande de service : disponibilité,
    /// avertissements, exécution hors du thread de l'interface, rapport.
    ///
    /// Le découpage reprend celui de <c>ConfigurationCommands</c> : la tuile
    /// construit le menu et aiguille, tout le reste est ici.
    /// </summary>
    internal static class ServiceCommands
    {
        private const string ApplicationName = "ProcessAffinity";

        /// <summary>
        /// Une commande à la fois. Le curseur d'attente ne bloque pas les clics,
        /// et deux arrêts lancés sur la même sélection se gêneraient.
        /// </summary>
        private static bool _running;

        /// <summary>Ordre d'affichage dans le menu.</summary>
        public static readonly ServiceCommandEnum[] MenuOrder =
        {
            ServiceCommandEnum.Start,
            ServiceCommandEnum.Stop,
            ServiceCommandEnum.Restart,
            ServiceCommandEnum.Pause,
            ServiceCommandEnum.Resume,
        };

        public static string GetHeader(ServiceCommandEnum command)
        {
            return command.ToString();
        }

        public static bool TryParseHeader(string header, out ServiceCommandEnum command)
        {
            foreach (ServiceCommandEnum candidate in MenuOrder)
            {
                if (string.Equals(header, GetHeader(candidate), StringComparison.Ordinal))
                {
                    command = candidate;
                    return true;
                }
            }

            command = ServiceCommandEnum.Start;
            return false;
        }

        /// <summary>
        /// Disponibilité sur une sélection. La commande est proposée dès qu'une
        /// cible peut la prendre : refuser l'ensemble parce qu'un service de la
        /// sélection ne s'arrête pas priverait des autres. Ce qui reste au bord
        /// de la route figure au rapport final.
        ///
        /// Quand rien n'est possible, l'info-bulle donne la raison commune si
        /// elle l'est, et sinon le compte — mieux vaut « 3 of 4 services » qu'une
        /// raison prise au hasard et fausse pour les autres.
        /// </summary>
        public static ServiceCommandAvailability GetAvailability(
            IList<ServiceSnapshot> snapshots, ServiceCommandEnum command)
        {
            List<string> reasons = new List<string>();

            foreach (ServiceSnapshot snapshot in snapshots)
            {
                ServiceCommandAvailability availability = ServiceControl.GetAvailability(snapshot, command);

                if (availability.IsAvailable)
                {
                    return availability;
                }

                reasons.Add(availability.Reason);
            }

            if (reasons.Count == 0)
            {
                return new ServiceCommandAvailability
                {
                    IsAvailable = false,
                    Reason = "No service is selected.",
                };
            }

            string first = reasons[0];

            if (reasons.Count == 1 || reasons.All(reason => string.Equals(reason, first, StringComparison.Ordinal)))
            {
                return new ServiceCommandAvailability { IsAvailable = false, Reason = first };
            }

            return new ServiceCommandAvailability
            {
                IsAvailable = false,
                Reason = "None of the " + reasons.Count + " selected services can take this command. "
                         + "Open the menu on a single service to see why.",
            };
        }

        /// <summary>
        /// Exécute la commande sur les services nommés.
        ///
        /// L'attente a lieu dans un <c>Task.Run</c> : <c>WaitForStatus</c> bloque
        /// son thread jusqu'à quinze secondes, ce qui sur le thread de
        /// l'interface figerait la fenêtre — y compris le rafraîchissement des
        /// tuiles, donc l'affichage même du changement attendu.
        /// </summary>
        public static async Task ExecuteAsync(IList<string> serviceNames, ServiceCommandEnum command)
        {
            if (_running || serviceNames == null || serviceNames.Count == 0)
            {
                return;
            }

            _running = true;

            try
            {
                List<ServiceSnapshot> snapshots;

                // Les états ont pu changer depuis la construction du menu : on
                // les relit, et c'est cette lecture qui décide.
                using (WaitCursor.Show())
                {
                    snapshots = await Task.Run(() =>
                        serviceNames.Select(ServiceControl.Describe).ToList());
                }

                List<ServiceSnapshot> eligible = new List<ServiceSnapshot>();
                List<string> skipped = new List<string>();

                foreach (ServiceSnapshot snapshot in snapshots)
                {
                    if (ServiceControl.GetAvailability(snapshot, command).IsAvailable)
                    {
                        eligible.Add(snapshot);
                    }
                    else
                    {
                        skipped.Add(snapshot.DisplayName);
                    }
                }

                if (eligible.Count == 0)
                {
                    MessageBox.Show(
                        GetHeader(command) + " cannot be applied to "
                        + (skipped.Count == 1 ? "this service." : "any of the selected services."),
                        ApplicationName, MessageBoxButton.OK, MessageBoxImage.Information);

                    return;
                }

                if (Stops(command) && !await ConfirmCascadeAsync(eligible, command))
                {
                    return;
                }

                List<ServiceCommandResult> results;

                using (WaitCursor.Show())
                {
                    List<string> names = eligible.Select(snapshot => snapshot.ServiceName).ToList();

                    results = await Task.Run(() =>
                        names.Select(name => ServiceControl.Execute(name, command)).ToList());
                }

                Report(command, eligible, results, skipped);
            }
            finally
            {
                _running = false;
            }
        }

        private static bool Stops(ServiceCommandEnum command)
        {
            return command == ServiceCommandEnum.Stop || command == ServiceCommandEnum.Restart;
        }

        /// <summary>
        /// Annonce la cascade avant de l'ouvrir. Les services dépendants sont
        /// nommés : « 4 autres services » ne dit pas si l'audio ou le réseau
        /// vont tomber.
        ///
        /// Une énumération impossible est dite comme telle, et non traitée comme
        /// une absence de dépendants : c'est la différence entre « rien ne suit »
        /// et « on ne sait pas ce qui suit ».
        /// </summary>
        private static async Task<bool> ConfirmCascadeAsync(
            IList<ServiceSnapshot> targets, ServiceCommandEnum command)
        {
            Dictionary<string, List<string>> dependentsByService;
            Dictionary<string, string> errorsByService;

            using (WaitCursor.Show())
            {
                List<string> names = targets.Select(snapshot => snapshot.ServiceName).ToList();

                Tuple<Dictionary<string, List<string>>, Dictionary<string, string>> gathered =
                    await Task.Run(() =>
                    {
                        Dictionary<string, List<string>> dependents =
                            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        Dictionary<string, string> errors =
                            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (string name in names)
                        {
                            List<string> found;
                            string error;

                            if (ServiceControl.TryGetRunningDependents(name, out found, out error))
                            {
                                if (found.Count > 0)
                                {
                                    dependents[name] = found;
                                }
                            }
                            else
                            {
                                errors[name] = error;
                            }
                        }

                        return Tuple.Create(dependents, errors);
                    });

                dependentsByService = gathered.Item1;
                errorsByService = gathered.Item2;
            }

            if (dependentsByService.Count == 0 && errorsByService.Count == 0)
            {
                return true;
            }

            StringBuilder builder = new StringBuilder();

            string verb = command == ServiceCommandEnum.Restart ? "Restarting" : "Stopping";

            builder.Append(verb).Append(targets.Count == 1
                ? " \"" + targets[0].DisplayName + "\""
                : " the " + targets.Count + " selected services")
                   .Append(" also stops the services that depend on ")
                   .Append(targets.Count == 1 ? "it." : "them.")
                   .Append("\r\n");

            foreach (ServiceSnapshot target in targets)
            {
                List<string> dependents;

                if (dependentsByService.TryGetValue(target.ServiceName, out dependents))
                {
                    builder.Append("\r\n").Append(target.DisplayName).Append(" — ")
                           .Append(dependents.Count)
                           .Append(dependents.Count == 1 ? " running dependent:\r\n" : " running dependents:\r\n")
                           .Append(string.Join(", ", dependents.Take(15)));

                    if (dependents.Count > 15)
                    {
                        builder.Append(", and ").Append(dependents.Count - 15).Append(" more");
                    }

                    builder.Append("\r\n");
                }

                string error;

                if (errorsByService.TryGetValue(target.ServiceName, out error))
                {
                    builder.Append("\r\n").Append(target.DisplayName).Append(" — ").Append(error)
                           .Append("\r\nThere may be dependent services that are not listed here.\r\n");
                }
            }

            if (command == ServiceCommandEnum.Restart)
            {
                builder.Append("\r\nWindows does not restart them afterwards.\r\n");
            }

            builder.Append("\r\nContinue?");

            return MessageBox.Show(builder.ToString(), ApplicationName,
                       MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Rapport final, silencieux quand tout a abouti. « Transmis mais pas
        /// encore là » a sa propre rubrique : ce n'est pas un échec, et le
        /// présenter comme tel pousserait à rejouer une commande déjà partie.
        /// </summary>
        private static void Report(
            ServiceCommandEnum command,
            IList<ServiceSnapshot> targets,
            IList<ServiceCommandResult> results,
            IList<string> skipped)
        {
            Dictionary<string, string> displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (ServiceSnapshot snapshot in targets)
            {
                displayNames[snapshot.ServiceName] = snapshot.DisplayName;
            }

            List<string> reached = new List<string>();
            List<string> pending = new List<string>();
            List<string> denied = new List<string>();
            List<string> failed = new List<string>();

            foreach (ServiceCommandResult result in results)
            {
                string name;

                if (!displayNames.TryGetValue(result.ServiceName, out name))
                {
                    name = result.ServiceName;
                }

                string detailed = result.Detail == null ? name : name + " (" + result.Detail + ")";

                switch (result.Outcome)
                {
                    case ServiceOutcomeEnum.Reached: reached.Add(name); break;
                    case ServiceOutcomeEnum.Pending: pending.Add(detailed); break;
                    case ServiceOutcomeEnum.AccessDenied: denied.Add(name); break;
                    default: failed.Add(detailed); break;
                }
            }

            if (pending.Count == 0 && denied.Count == 0 && failed.Count == 0 && skipped.Count == 0)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(GetHeader(command)).Append(" — ").Append(reached.Count)
                   .Append(reached.Count == 1 ? " service" : " services").Append(" done out of ")
                   .Append(results.Count + skipped.Count).Append(".\r\n");

            Append(builder, reached, "Completed");
            Append(builder, pending, "Sent, but not finished within "
                                     + (int)ServiceControl.CommandTimeout.TotalSeconds + " s");
            Append(builder, denied, "Access denied");
            Append(builder, failed, "Failed");
            Append(builder, skipped, "Not applicable");

            MessageBox.Show(builder.ToString(), ApplicationName, MessageBoxButton.OK,
                denied.Count > 0 || failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            if (denied.Count > 0)
            {
                string reason = denied.Count == 1
                    ? "Windows denied access to \"" + denied[0] + "\"."
                    : "Windows denied access to " + denied.Count + " of the selected services.";

                if (Elevation.OfferElevatedRestart(reason))
                {
                    Application.Current.Shutdown();
                }
            }
        }

        private static void Append(StringBuilder builder, IList<string> names, string title)
        {
            if (names.Count == 0)
            {
                return;
            }

            const int maximumListed = 15;

            builder.Append("\r\n").Append(title).Append(" (").Append(names.Count).Append("):\r\n")
                   .Append(string.Join(", ", names.Take(maximumListed)));

            if (names.Count > maximumListed)
            {
                builder.Append(", and ").Append(names.Count - maximumListed).Append(" more");
            }

            builder.Append("\r\n");
        }
    }
}
