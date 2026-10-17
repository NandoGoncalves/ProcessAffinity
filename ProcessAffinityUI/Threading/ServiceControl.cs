using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceProcess;

namespace ProcessAffinityUI.Threading
{
    /// <summary>Commandes de contrôle d'un service.</summary>
    internal enum ServiceCommandEnum
    {
        Start,
        Stop,
        Restart,
        Pause,
        Resume,
    }

    /// <summary>Issue d'une commande, du point de vue de ce qu'il faut en dire.</summary>
    internal enum ServiceOutcomeEnum
    {
        /// <summary>Le service a atteint l'état demandé dans le délai imparti.</summary>
        Reached,

        /// <summary>
        /// La commande a été transmise sans erreur, mais l'état attendu n'est pas
        /// venu avant la fin du délai. Ce n'est pas un échec : le gestionnaire de
        /// services peut être encore en train d'arrêter le service.
        /// </summary>
        Pending,

        /// <summary>Refus de l'application, avant tout appel : service critique.</summary>
        Refused,

        /// <summary>Accès refusé par Windows — relance élevée à proposer.</summary>
        AccessDenied,

        /// <summary>Échec signalé par le gestionnaire de services.</summary>
        Failed,

        /// <summary>Rien n'a été tenté : la commande n'était pas applicable.</summary>
        NotApplicable,
    }

    /// <summary>État lu d'un service, et ce qu'il autorise.</summary>
    internal sealed class ServiceSnapshot
    {
        public string ServiceName { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Faux quand le service n'a pas pu être ouvert ou lu.</summary>
        public bool IsReadable { get; set; }

        public ServiceControllerStatus Status { get; set; }

        public bool CanStop { get; set; }

        public bool CanPauseAndContinue { get; set; }

        public bool IsCritical { get; set; }

        /// <summary>Renseigné quand <see cref="IsReadable"/> est faux.</summary>
        public string UnreadableReason { get; set; }
    }

    /// <summary>
    /// Disponibilité d'une commande sur un service donné. Une commande
    /// indisponible n'est pas retirée du menu : elle est grisée, et porte ici
    /// de quoi expliquer pourquoi.
    /// </summary>
    internal sealed class ServiceCommandAvailability
    {
        public bool IsAvailable { get; set; }

        /// <summary>Texte de l'info-bulle, en anglais. Null quand disponible.</summary>
        public string Reason { get; set; }
    }

    /// <summary>Ce qu'une commande a donné sur un service.</summary>
    internal sealed class ServiceCommandResult
    {
        public string ServiceName { get; set; }

        public ServiceOutcomeEnum Outcome { get; set; }

        /// <summary>Précision destinée au rapport final.</summary>
        public string Detail { get; set; }
    }

    /// <summary>
    /// Contrôle des services, au-dessus de <see cref="ServiceController"/>.
    ///
    /// Aucune méthode de cette classe ne doit être appelée depuis le thread de
    /// l'interface : <see cref="Execute"/> attend l'état demandé et bloque donc
    /// son appelant jusqu'au délai imparti. La lecture de l'état
    /// (<see cref="Describe"/>) est en revanche assez rapide pour la
    /// construction d'un menu — mesurée à 0,48 ms par service, et 6 ms pour
    /// l'énumération complète de 324 services.
    /// </summary>
    internal static class ServiceControl
    {
        /// <summary>
        /// Délai au-delà duquel on cesse d'attendre l'état demandé. La commande
        /// a été transmise : on ne la rejoue pas, on le dit.
        /// </summary>
        public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Services dont l'arrêt emporte la session, et que l'application refuse
        /// quoi qu'en dise Windows.
        ///
        /// La liste est volontairement courte. Windows marque déjà la plupart
        /// d'entre eux <c>CanStop = false</c> — mesuré sur cette machine : RpcSs,
        /// DcomLaunch, RpcEptMapper, BrokerInfrastructure, LSM, SamSs, Power,
        /// CoreMessagingRegistrar et Dnscache le sont. Les y laisser sert à
        /// donner la bonne raison plutôt que « does not accept a stop request ».
        /// Restent PlugPlay et nsi, que Windows déclare arrêtables et dont
        /// l'arrêt prive la machine de ses périphériques ou de son réseau.
        ///
        /// Tout ce qui est seulement gênant — Schedule, Dhcp, EventLog — n'y
        /// figure pas : l'intérêt de cet outil est justement de faire ce que le
        /// Gestionnaire des tâches s'interdit. L'avertissement sur les services
        /// dépendants suffit à ces cas.
        /// </summary>
        private static readonly string[] CriticalServiceNames =
        {
            "RpcSs", "DcomLaunch", "RpcEptMapper", "BrokerInfrastructure", "LSM",
            "SamSs", "Power", "PlugPlay", "CoreMessagingRegistrar", "nsi",
        };

        public static bool IsCriticalService(string serviceName)
        {
            return serviceName != null
                && CriticalServiceNames.Contains(serviceName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Lit l'état et les capacités d'un service. Ne lève jamais : un service
        /// illisible revient avec <see cref="ServiceSnapshot.IsReadable"/> faux.
        /// </summary>
        public static ServiceSnapshot Describe(string serviceName)
        {
            ServiceSnapshot snapshot = new ServiceSnapshot
            {
                ServiceName = serviceName,
                DisplayName = serviceName,
                IsCritical = IsCriticalService(serviceName),
            };

            if (string.IsNullOrEmpty(serviceName))
            {
                snapshot.UnreadableReason = "The service name is unknown.";
                return snapshot;
            }

            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    snapshot.Status = controller.Status;
                    snapshot.CanStop = controller.CanStop;
                    snapshot.CanPauseAndContinue = controller.CanPauseAndContinue;
                    snapshot.DisplayName = string.IsNullOrEmpty(controller.DisplayName)
                        ? serviceName
                        : controller.DisplayName;
                    snapshot.IsReadable = true;
                }
            }
            catch (Exception exception)
            {
                snapshot.UnreadableReason = IsAccessDenied(exception)
                    ? "The state of this service cannot be read without elevation."
                    : "The state of this service could not be read: " + exception.Message;
            }

            return snapshot;
        }

        /// <summary>
        /// Disponibilité d'une commande. L'ordre des cas compte : on donne la
        /// raison la plus parlante, pas la première venue.
        /// </summary>
        public static ServiceCommandAvailability GetAvailability(
            ServiceSnapshot snapshot, ServiceCommandEnum command)
        {
            if (snapshot == null || !snapshot.IsReadable)
            {
                return Unavailable(snapshot == null || snapshot.UnreadableReason == null
                    ? "The state of this service could not be read."
                    : snapshot.UnreadableReason);
            }

            bool running = snapshot.Status == ServiceControllerStatus.Running;
            bool paused = snapshot.Status == ServiceControllerStatus.Paused;
            bool stopped = snapshot.Status == ServiceControllerStatus.Stopped;
            bool inTransition = !running && !paused && !stopped;

            if (inTransition)
            {
                return Unavailable("The service is "
                    + DescribeStatus(snapshot.Status).ToLowerInvariant()
                    + "; wait for it to settle.");
            }

            switch (command)
            {
                case ServiceCommandEnum.Start:
                    return stopped
                        ? Available()
                        : Unavailable("The service is already " + DescribeStatus(snapshot.Status).ToLowerInvariant() + ".");

                case ServiceCommandEnum.Stop:
                case ServiceCommandEnum.Restart:
                    if (snapshot.IsCritical)
                    {
                        return Unavailable("\"" + snapshot.DisplayName + "\" is a critical Windows service. "
                            + "Stopping it would leave the session unusable, so ProcessAffinity does not allow it.");
                    }

                    if (stopped)
                    {
                        return command == ServiceCommandEnum.Stop
                            ? Unavailable("The service is already stopped.")
                            : Unavailable("The service is stopped; start it instead.");
                    }

                    return snapshot.CanStop
                        ? Available()
                        : Unavailable("Windows reports that this service does not accept a stop request.");

                case ServiceCommandEnum.Pause:
                    if (!snapshot.CanPauseAndContinue)
                    {
                        return Unavailable("Windows reports that this service does not support pause and continue. "
                            + "Most services do not.");
                    }

                    return running
                        ? Available()
                        : Unavailable("The service is " + DescribeStatus(snapshot.Status).ToLowerInvariant() + ".");

                case ServiceCommandEnum.Resume:
                    if (!snapshot.CanPauseAndContinue)
                    {
                        return Unavailable("Windows reports that this service does not support pause and continue. "
                            + "Most services do not.");
                    }

                    return paused
                        ? Available()
                        : Unavailable("The service is not paused.");
            }

            return Unavailable("Unsupported command.");
        }

        /// <summary>
        /// Services en cours qui dépendent de celui-ci, et que l'arrêt
        /// emporterait.
        ///
        /// L'énumération demande SERVICE_ENUMERATE_DEPENDENTS, qui n'est pas
        /// toujours accordé : mesuré, RpcSs est lisible en statut mais refuse
        /// l'énumération de ses dépendants en session non élevée. Un échec
        /// revient donc par <paramref name="error"/>, et il ne faut surtout pas
        /// le confondre avec une absence de dépendants — l'utilisateur
        /// croirait l'arrêt sans conséquence.
        /// </summary>
        public static bool TryGetRunningDependents(
            string serviceName, out List<string> dependentNames, out string error)
        {
            dependentNames = new List<string>();
            error = null;

            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    foreach (ServiceController dependent in controller.DependentServices)
                    {
                        try
                        {
                            if (dependent.Status == ServiceControllerStatus.Stopped)
                            {
                                continue;
                            }

                            dependentNames.Add(string.IsNullOrEmpty(dependent.DisplayName)
                                ? dependent.ServiceName
                                : dependent.DisplayName);
                        }
                        catch
                        {
                            // Dépendant illisible : on le compte sous son nom court
                            // plutôt que de l'omettre.
                            dependentNames.Add(dependent.ServiceName);
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = IsAccessDenied(exception)
                    ? "The list of dependent services cannot be read without elevation."
                    : "The list of dependent services could not be read: " + exception.Message;

                return false;
            }
        }

        /// <summary>
        /// Exécute une commande et attend l'état correspondant, au plus
        /// <see cref="CommandTimeout"/>.
        ///
        /// À appeler depuis un thread de fond. Le redémarrage est un arrêt, une
        /// attente, puis un démarrage : si l'arrêt n'aboutit pas, le démarrage
        /// n'est pas tenté — relancer un service qu'on n'a pas su arrêter
        /// reviendrait à espérer que les deux ordres se croisent bien.
        /// </summary>
        public static ServiceCommandResult Execute(string serviceName, ServiceCommandEnum command)
        {
            ServiceCommandResult result = new ServiceCommandResult { ServiceName = serviceName };

            if (IsCriticalService(serviceName)
                && (command == ServiceCommandEnum.Stop || command == ServiceCommandEnum.Restart))
            {
                result.Outcome = ServiceOutcomeEnum.Refused;
                result.Detail = "critical Windows service";
                return result;
            }

            try
            {
                using (ServiceController controller = new ServiceController(serviceName))
                {
                    switch (command)
                    {
                        case ServiceCommandEnum.Start:
                            controller.Start();
                            return Settle(controller, ServiceControllerStatus.Running, result);

                        case ServiceCommandEnum.Stop:
                            controller.Stop();
                            return Settle(controller, ServiceControllerStatus.Stopped, result);

                        case ServiceCommandEnum.Pause:
                            controller.Pause();
                            return Settle(controller, ServiceControllerStatus.Paused, result);

                        case ServiceCommandEnum.Resume:
                            controller.Continue();
                            return Settle(controller, ServiceControllerStatus.Running, result);

                        case ServiceCommandEnum.Restart:
                            controller.Stop();

                            ServiceCommandResult stopResult = Settle(
                                controller, ServiceControllerStatus.Stopped, new ServiceCommandResult
                                {
                                    ServiceName = serviceName,
                                });

                            if (stopResult.Outcome != ServiceOutcomeEnum.Reached)
                            {
                                stopResult.Detail = "not restarted, the service did not stop"
                                    + (stopResult.Detail == null ? string.Empty : " (" + stopResult.Detail + ")");

                                return stopResult;
                            }

                            controller.Start();
                            return Settle(controller, ServiceControllerStatus.Running, result);
                    }
                }

                result.Outcome = ServiceOutcomeEnum.NotApplicable;
                return result;
            }
            catch (Exception exception)
            {
                if (IsAccessDenied(exception))
                {
                    result.Outcome = ServiceOutcomeEnum.AccessDenied;
                    result.Detail = "access denied";
                }
                else
                {
                    result.Outcome = ServiceOutcomeEnum.Failed;
                    result.Detail = Summarize(exception);
                }

                return result;
            }
        }

        /// <summary>
        /// Attend l'état demandé. Le dépassement du délai n'est pas une erreur :
        /// l'ordre est parti et le gestionnaire de services peut encore y
        /// travailler. Le distinguer d'un échec évite de faire croire qu'il n'y
        /// a rien eu.
        /// </summary>
        private static ServiceCommandResult Settle(
            ServiceController controller, ServiceControllerStatus expected, ServiceCommandResult result)
        {
            try
            {
                controller.WaitForStatus(expected, CommandTimeout);

                result.Outcome = ServiceOutcomeEnum.Reached;
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                controller.Refresh();

                result.Outcome = ServiceOutcomeEnum.Pending;
                result.Detail = "still " + DescribeStatus(SafeStatus(controller)).ToLowerInvariant()
                    + " after " + (int)CommandTimeout.TotalSeconds + " s";
            }
            catch (Exception exception)
            {
                result.Outcome = IsAccessDenied(exception)
                    ? ServiceOutcomeEnum.AccessDenied
                    : ServiceOutcomeEnum.Failed;

                result.Detail = IsAccessDenied(exception) ? "access denied" : Summarize(exception);
            }

            return result;
        }

        private static ServiceControllerStatus SafeStatus(ServiceController controller)
        {
            try
            {
                return controller.Status;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Libellé anglais d'un état, y compris les états transitoires.</summary>
        public static string DescribeStatus(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running: return "running";
                case ServiceControllerStatus.Stopped: return "stopped";
                case ServiceControllerStatus.Paused: return "paused";
                case ServiceControllerStatus.StartPending: return "starting";
                case ServiceControllerStatus.StopPending: return "stopping";
                case ServiceControllerStatus.PausePending: return "pausing";
                case ServiceControllerStatus.ContinuePending: return "resuming";
                default: return "in an unknown state";
            }
        }

        /// <summary>
        /// Le refus d'accès se reconnaît à une <see cref="Win32Exception"/> de
        /// code 5 dans la chaîne. Mesuré :
        /// <c>InvalidOperationException("Cannot open 'X' service on computer '.'.")</c>
        /// portant une <c>Win32Exception NativeErrorCode=5</c> en interne.
        /// </summary>
        public static bool IsAccessDenied(Exception exception)
        {
            while (exception != null)
            {
                Win32Exception win32 = exception as Win32Exception;

                if (win32 != null && win32.NativeErrorCode == 5)
                {
                    return true;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        private static string Summarize(Exception exception)
        {
            Exception deepest = exception;

            while (deepest.InnerException != null)
            {
                deepest = deepest.InnerException;
            }

            return deepest.Message;
        }

        private static ServiceCommandAvailability Available()
        {
            return new ServiceCommandAvailability { IsAvailable = true };
        }

        private static ServiceCommandAvailability Unavailable(string reason)
        {
            return new ServiceCommandAvailability { IsAvailable = false, Reason = reason };
        }
    }
}
