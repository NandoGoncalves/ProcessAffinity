using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Management;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;


namespace ProcessAffinityUI.Threading
{
    public delegate void ProcessEventHandler(object sender, ProcessEventArgs e);

    


    public class Processes : List<Process>, IDisposable
    {
        public event ProcessEventHandler ProcessEventArrived;
        public event ProcessEventHandler ProcessCreated;
        public event ProcessEventHandler ProcessDeleted;

        /// <summary>
        /// Conservé pour la signature et l'abonnement, mais plus jamais déclenché.
        /// Il l'était par __InstanceModificationEvent sur Win32_Process, qui part
        /// à chaque variation de propriété — WorkingSetSize en tête : c'était le
        /// gros de la charge du watcher. Son seul effet visible, passer le bandeau
        /// de nom en jaune, était de toute façon effacé dans la seconde par
        /// l'échantillonneur, qui réécrit ce fond à chaque tick ; et l'icône qu'il
        /// posait au passage l'est déjà à la création de la tuile.
        /// </summary>
#pragma warning disable 67
        public event ProcessEventHandler ProcessModified;
#pragma warning restore 67


        private string _computerName = string.Empty;
        private string _domain = string.Empty;
        private string _user = string.Empty;
        private string _password = string.Empty;
        private ManagementScope _scope = null;

        private TargetInstanceEnum _targetInstance = TargetInstanceEnum.Win32_Process;

        /// <summary>Intervalle unique d'échantillonnage du % CPU et de diff.</summary>
        private const int CPUSamplingIntervalMilliseconds = 1000;

        /// <summary>
        /// Nombre de ticks entre deux balayages des services. Les créations et
        /// suppressions de processus se déduisent gratuitement de l'appel système
        /// déjà fait chaque seconde ; les services, eux, n'existent que pour WMI
        /// et imposent une requête. Mesurée sur cette machine, la requête projetée
        /// coûte 21 ms (contre 338 ms pour le « SELECT * » d'origine) : à 5 s
        /// d'intervalle elle représente 0,4 % d'un cœur. Un service qui démarre ou
        /// s'arrête est un évènement d'administration, pas de la respiration du
        /// système : 5 s est en deçà du seuil où l'utilisateur s'en aperçoit.
        /// </summary>
        private const int ServiceScanIntervalTicks = 5;

        /// <summary>
        /// Intervalle de réveil du thread de matérialisation en l'absence de
        /// signal : il reprend les PID dont la requête WMI a échoué.
        /// </summary>
        private const int MaterializationIdleMilliseconds = 1000;

        /// <summary>
        /// Tentatives consécutives au-delà desquelles un PID est abandonné : il
        /// entre alors dans la référence pour ne plus être redemandé.
        /// </summary>
        private const int MaximumMaterializationAttempts = 3;

        private readonly CancellationTokenSource _cpuSamplingCancellation = new CancellationTokenSource();

        /// <summary>
        /// Sérialise les accès à la collection : le diff ajoute et retire des
        /// éléments depuis le thread d'échantillonnage pendant que l'IHM la lit.
        /// </summary>
        private readonly object _syncRoot = new object();

        /// <summary>
        /// PID vers date de création, tels que vus au tick précédent. Null tant
        /// que la référence n'est pas établie.
        /// </summary>
        private Dictionary<int, long> _knownProcessCreateTimes = null;

        /// <summary>
        /// Nom de service vers PID de son hôte, tels que vus au dernier balayage.
        /// </summary>
        private Dictionary<string, int> _knownServiceProcessIDs = null;

        /// <summary>
        /// PID détectés mais pas encore matérialisés, avec leur nombre de
        /// tentatives.
        /// </summary>
        private readonly Dictionary<int, PendingCreation> _pendingCreations = new Dictionary<int, PendingCreation>();

        /// <summary>
        /// Sérialise l'état du diff — référence et file d'attente — entre le
        /// thread d'échantillonnage qui détecte et celui qui matérialise. Distinct
        /// de <see cref="_syncRoot"/>, qui garde la collection : les deux ne sont
        /// jamais tenus ensemble.
        /// </summary>
        private readonly object _diffSyncRoot = new object();

        private readonly AutoResetEvent _materializationSignal = new AutoResetEvent(false);

        private int _tickCount = 0;

        /// <summary>Processus vu au relevé, en attente de sa requête WMI.</summary>
        private sealed class PendingCreation
        {
            public PendingCreation(long createTime)
            {
                this.CreateTime = createTime;
            }

            public long CreateTime { get; private set; }

            public int Attempts { get; set; }
        }

        public Processes()
            :this(TargetInstanceEnum.Win32_Process)
        { }

        public Processes(TargetInstanceEnum targetInstance)
            : this("localhost", string.Empty, string.Empty, string.Empty, targetInstance)
        {
            // Pas de second Initialize() : le constructeur délégué l'a déjà
            // appelé. L'énumération se faisait deux fois, d'où les 612 entrées
            // relevées pour 311 processus.
        }

        public Processes(List<Process> processes)
            :this(processes, TargetInstanceEnum.Win32_Process)
        { }

        public Processes(List<Process> processes, TargetInstanceEnum targetInstance) :base(processes)
        {
            this._targetInstance = targetInstance;
        }

        public Processes(string computerName, string domain, string user, string password)
            :this(computerName, domain, user, password, TargetInstanceEnum.Win32_Process)
        {
        }

        public Processes(string computerName, string domain, string user, string password, TargetInstanceEnum targetInstance)
        {
            this._computerName = computerName;
            this._domain = domain;
            this._user = user;
            this._password = password;
            this._targetInstance = targetInstance;

            this.Initialize();

            Task.Factory.StartNew(() => ListenCPUUsages(this._cpuSamplingCancellation.Token), TaskCreationOptions.LongRunning);
            Task.Factory.StartNew(() => ListenCreations(this._cpuSamplingCancellation.Token), TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Arrête la boucle d'échantillonnage du % CPU et, avec elle, celle de
        /// matérialisation. Idempotent.
        /// </summary>
        public void StopCPUSampling()
        {
            if (!this._cpuSamplingCancellation.IsCancellationRequested)
            {
                this._cpuSamplingCancellation.Cancel();
            }
        }

        public void Dispose()
        {
            // Plus de ManagementEventWatcher à arrêter : Stop() bloquait le
            // thread appelant le temps que WMI vide sa file d'évènements, et le
            // GC.Collect() qui suivait ne servait qu'à forcer la libération des
            // objets de souscription. Dispose() est désormais immédiat.
            this.StopCPUSampling();

            // Les deux boucles attendent sur ces poignées ; elles interceptent
            // l'ObjectDisposedException et sortent.
            this._cpuSamplingCancellation.Dispose();
            this._materializationSignal.Dispose();
        }

        private void Initialize()
        {
                ManagementScope scope;                

            try
            {
                if (!this._computerName.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    ConnectionOptions Conn = new ConnectionOptions();
                    Conn.Username = this._user;
                    Conn.Password = this._password;
                    Conn.Authority = "ntlmdomain:" + this._domain;
                    scope = new ManagementScope(String.Format("\\\\{0}\\root\\CIMV2", this._computerName), Conn);
                }
                else
                {
                    scope = new ManagementScope(String.Format("\\\\{0}\\root\\CIMV2", this._computerName), null);
                }
                    this._scope = scope;

                    //if (scope.IsConnected == false)
                    //{
                    scope.Connect();
                    //}

                    ObjectQuery Query = null;
                    if (_targetInstance == TargetInstanceEnum.Win32_Process)
                    {
                        Query = new ObjectQuery("SELECT * FROM Win32_Process WHERE ProcessID <> 0");
                    }
                    else
                    {
                        Query = new ObjectQuery("SELECT * FROM Win32_Service WHERE State LIKE 'Running'");
                    }
                    ManagementObjectSearcher Searcher = new ManagementObjectSearcher(scope, Query);

                
                    foreach (ManagementObject WmiObject in Searcher.Get())
                    {
                        try
                        {
                            this.AddProcess(new Process(WmiObject, scope, _targetInstance));
                        }
                        catch
                        {
                            lock (this._syncRoot)
                            {
                                this.Add(new Process(this._computerName, 0, "error"));
                            }
                        }
                    }

                    lock (this._syncRoot)
                    {
                        this.Sort((x, y) => string.Compare(x.ProcessName, y.ProcessName));
                    }

            }
            catch //(Exception e)
            {
                //throw new Exception(e.Message);
            }
 
        }

        public void AddProcess(Process process)
        {
            lock (this._syncRoot)
            {
                if (!this.ProcessExists(process))
                {
                    this.Add(process);
                }
            }
        }

        /// <summary>
        /// Ajout en bloc sous verrou : List&lt;T&gt;.AddRange muterait la collection
        /// pendant que l'échantillonneur la lit.
        /// </summary>
        public void AddProcesses(IEnumerable<Process> processes)
        {
            lock (this._syncRoot)
            {
                this.AddRange(processes);
            }
        }

        public bool ProcessExists(Process process)
        {
            lock (this._syncRoot)
            {
                bool processExists = false;
                if (this.Exists(p => p.IsSameEntry(process)))
                {
                    processExists = true;
                }

                return processExists;
            }
        }

        /// <summary>
        /// Nombre d'abonnés réellement portés par chacun des quatre événements,
        /// dans l'ordre : arrivée, création, suppression, modification.
        /// Un événement n'expose que += et -= à l'extérieur de sa classe : sans
        /// cet accesseur, l'indicateur ne peut compter que les champs délégués de
        /// l'abonné, ce qui ne dit rien de l'abonnement réel.
        /// </summary>
        public int[] GetEventSubscriberCounts()
        {
            return new int[]
            {
                ProcessEventArrived == null ? 0 : ProcessEventArrived.GetInvocationList().Length,
                ProcessCreated == null ? 0 : ProcessCreated.GetInvocationList().Length,
                ProcessDeleted == null ? 0 : ProcessDeleted.GetInvocationList().Length,
                ProcessModified == null ? 0 : ProcessModified.GetInvocationList().Length,
            };
        }

        /// <summary>
        /// Copie de la liste prise sous verrou, pour une lecture depuis un autre
        /// thread que celui qui la mute.
        /// </summary>
        public Process[] Snapshot()
        {
            lock (this._syncRoot)
            {
                return this.ToArray();
            }
        }

        private void ListenCPUUsages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SetCPUUsages();
                }
                catch
                {
                    // Un tick en échec ne doit jamais interrompre les suivants :
                    // sans cela, l'échantillonnage s'arrêterait définitivement et
                    // silencieusement à la première exception.
                }

                try
                {
                    // Attente interruptible : l'annulation est prise en compte
                    // immédiatement, sans attendre la fin de l'intervalle.
                    if (cancellationToken.WaitHandle.WaitOne(CPUSamplingIntervalMilliseconds))
                    {
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Source d'annulation libérée par Dispose().
                    break;
                }
            }
        }

        private void SetCPUUsages()
        {
            long timestamp;

            // Un seul appel système pour tous les processus, sans ouverture de
            // handle : aucun contrôle d'accès par processus.
            Dictionary<int, SystemProcessTimes.ProcessTimes> processTimesByProcessID =
                SystemProcessTimes.GetProcessTimes(out timestamp);

            if (processTimesByProcessID == null)
            {
                return;
            }

            Process[] processes;

            lock (this._syncRoot)
            {
                processes = this.ToArray();
            }

            foreach (Process process in processes)
            {
                SystemProcessTimes.ProcessTimes processTimes;

                // Association par PID : une recherche linéaire par processus
                // donnerait un coût quadratique à chaque tick.
                if (processTimesByProcessID.TryGetValue(process.ProcessID, out processTimes))
                {
                    process.UpdateCPUUsage(processTimes.CreateTime, processTimes.TotalProcessorTime, timestamp);
                }
            }

            // Le même relevé sert à détecter les créations et les suppressions :
            // aucun appel supplémentaire.
            this.DetectProcessChanges(processTimesByProcessID);

            this._tickCount++;

            if (this._tickCount % ServiceScanIntervalTicks == 0)
            {
                this.DetectServiceChanges();
            }
        }

        /// <summary>
        /// Compare le relevé courant au précédent. Le PID seul ne suffit pas :
        /// Windows les recycle, et un PID réapparu avec une autre date de création
        /// est un autre processus — soit une suppression suivie d'une création.
        /// </summary>
        private void DetectProcessChanges(Dictionary<int, SystemProcessTimes.ProcessTimes> processTimesByProcessID)
        {
            Dictionary<int, long> createTimesByProcessID = new Dictionary<int, long>(processTimesByProcessID.Count);

            foreach (KeyValuePair<int, SystemProcessTimes.ProcessTimes> pair in processTimesByProcessID)
            {
                // La liste est bâtie sur « WHERE ProcessID <> 0 » : le processus
                // Idle n'y a jamais figuré, il ne doit pas y entrer maintenant.
                if (pair.Key == 0)
                {
                    continue;
                }

                createTimesByProcessID[pair.Key] = pair.Value.CreateTime;
            }

            List<int> deletions = new List<int>();
            bool hasNewCreations = false;

            lock (this._diffSyncRoot)
            {
                if (this._knownProcessCreateTimes == null)
                {
                    // Premier tick : la liste vient d'être énumérée, on se
                    // contente d'établir la référence sans rien signaler.
                    this._knownProcessCreateTimes = createTimesByProcessID;
                    return;
                }

                foreach (KeyValuePair<int, long> pair in this._knownProcessCreateTimes)
                {
                    long createTime;

                    if (!createTimesByProcessID.TryGetValue(pair.Key, out createTime) || createTime != pair.Value)
                    {
                        deletions.Add(pair.Key);
                    }
                }

                foreach (int processID in deletions)
                {
                    this._knownProcessCreateTimes.Remove(processID);
                }

                // Un PID en attente qui a quitté le relevé n'aura jamais de
                // tuile. Il n'est jamais entré dans la référence : il ne sera pas
                // non plus signalé comme supprimé.
                List<int> obsolete = null;

                foreach (KeyValuePair<int, PendingCreation> pair in this._pendingCreations)
                {
                    long createTime;

                    if (!createTimesByProcessID.TryGetValue(pair.Key, out createTime) || createTime != pair.Value.CreateTime)
                    {
                        obsolete = obsolete ?? new List<int>();
                        obsolete.Add(pair.Key);
                    }
                }

                if (obsolete != null)
                {
                    foreach (int processID in obsolete)
                    {
                        this._pendingCreations.Remove(processID);
                    }
                }

                foreach (KeyValuePair<int, long> pair in createTimesByProcessID)
                {
                    long known;

                    if (this._knownProcessCreateTimes.TryGetValue(pair.Key, out known) && known == pair.Value)
                    {
                        continue;
                    }

                    PendingCreation pending;

                    if (this._pendingCreations.TryGetValue(pair.Key, out pending) && pending.CreateTime == pair.Value)
                    {
                        // Déjà en file : on garde son compteur de tentatives.
                        continue;
                    }

                    this._pendingCreations[pair.Key] = new PendingCreation(pair.Value);
                    hasNewCreations = true;
                }
            }

            foreach (int processID in deletions)
            {
                this.RaiseProcessDeleted(processID);
            }

            if (hasNewCreations)
            {
                this._materializationSignal.Set();
            }
        }

        /// <summary>
        /// Boucle de matérialisation, sur son propre thread. Les requêtes WMI
        /// n'ont rien à faire dans le tick d'échantillonnage : mesurée à 50
        /// processus lancés d'un coup — soit une centaine avec leurs enfants —
        /// une matérialisation en série y a tenu 13 s, gelant d'autant
        /// l'affichage du % CPU. Ici la détection reste à l'heure quoi qu'il
        /// arrive, et la file se vide à plein régime.
        /// </summary>
        private void ListenCreations(CancellationToken cancellationToken)
        {
            WaitHandle[] waitHandles = new WaitHandle[] { this._materializationSignal, cancellationToken.WaitHandle };

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (WaitHandle.WaitAny(waitHandles, MaterializationIdleMilliseconds) == 1)
                    {
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                try
                {
                    this.MaterializePendingCreations(cancellationToken);
                }
                catch
                {
                    // Une passe en échec ne doit pas arrêter les suivantes.
                }
            }
        }

        private void MaterializePendingCreations(CancellationToken cancellationToken)
        {
            List<int> pass;

            lock (this._diffSyncRoot)
            {
                if (this._pendingCreations.Count == 0)
                {
                    return;
                }

                // Une passe traite chaque PID au plus une fois : sans cela un
                // échec répété tournerait en boucle sur la même requête.
                pass = new List<int>(this._pendingCreations.Keys);
            }

            foreach (int processID in pass)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                long createTime;

                lock (this._diffSyncRoot)
                {
                    PendingCreation pending;

                    if (!this._pendingCreations.TryGetValue(processID, out pending))
                    {
                        // Écarté entre-temps : le processus est mort.
                        continue;
                    }

                    createTime = pending.CreateTime;
                }

                Process process = this.MaterializeProcess(processID);

                bool add = false;

                lock (this._diffSyncRoot)
                {
                    PendingCreation pending;

                    if (!this._pendingCreations.TryGetValue(processID, out pending) || pending.CreateTime != createTime)
                    {
                        // Mort pendant la requête : on jette le résultat plutôt
                        // que d'afficher la tuile d'un processus disparu.
                        continue;
                    }

                    if (process != null)
                    {
                        this._pendingCreations.Remove(processID);
                        this._knownProcessCreateTimes[processID] = createTime;
                        add = true;
                    }
                    else if (++pending.Attempts >= MaximumMaterializationAttempts)
                    {
                        // Certains processus ne figurent jamais dans
                        // Win32_Process — « Secure System », « Registry »,
                        // « Memory Compression ». On cesse de les redemander,
                        // sans quoi chaque passe paierait leur requête
                        // indéfiniment. Les autres repasseront : c'est ce qui
                        // évite qu'un échec transitoire de WMI fasse disparaître
                        // un processus jusqu'au rechargement manuel.
                        this._pendingCreations.Remove(processID);
                        this._knownProcessCreateTimes[processID] = createTime;
                    }
                }

                if (add)
                {
                    this.AddCreatedProcess(process);
                }
            }
        }

        /// <summary>
        /// Retire toutes les entrées du PID — l'hôte et les services qu'il porte,
        /// qui n'ont pas d'existence sans lui. Le watcher n'en retirait qu'une
        /// seule, et le compteur « Total » dérivait à chaque hôte de services
        /// disparu.
        /// </summary>
        private void RaiseProcessDeleted(int processID)
        {
            List<Process> removed = new List<Process>();

            lock (this._syncRoot)
            {
                for (int i = this.Count - 1; i >= 0; i--)
                {
                    if (this[i].ProcessID == processID)
                    {
                        removed.Add(this[i]);
                        this.RemoveAt(i);
                    }
                }
            }

            if (removed.Count == 0)
            {
                return;
            }

            // Une notification portant l'entrée hôte retire déjà toutes les
            // tuiles du PID. Sans hôte dans la liste, chaque service répond de
            // lui-même.
            Process host = removed.FirstOrDefault(entry => !entry.IsService);

            if (host != null)
            {
                this.RaiseDeleted(host);
                return;
            }

            foreach (Process service in removed)
            {
                this.RaiseDeleted(service);
            }
        }

        private void AddCreatedProcess(Process process)
        {
            bool added;

            lock (this._syncRoot)
            {
                added = !this.Exists(entry => entry.IsSameEntry(process));

                if (added)
                {
                    this.Add(process);
                }
            }

            if (added)
            {
                this.RaiseCreated(process);
            }
        }

        /// <summary>
        /// La détection est gratuite, la matérialisation ne l'est pas : l'objet
        /// Process déréférence son WIN32_Process pour la priorité et le chemin.
        /// Une requête ciblée par processus créé, et rien pour les autres.
        /// </summary>
        private Process MaterializeProcess(int processID)
        {
            if (this._scope == null)
            {
                return null;
            }

            try
            {
                ObjectQuery query = new ObjectQuery(
                    "SELECT * FROM Win32_Process WHERE ProcessId=" + processID.ToString(CultureInfo.InvariantCulture));

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(this._scope, query))
                {
                    foreach (ManagementObject wmiObject in searcher.Get())
                    {
                        return new Process(wmiObject, this._scope, TargetInstanceEnum.Win32_Process);
                    }
                }
            }
            catch
            {
                // Processus disparu entre le relevé et la requête, ou WMI
                // indisponible : sans objet, pas de tuile.
            }

            return null;
        }

        /// <summary>
        /// Les services ne figurent pas dans le relevé système : leur suivi passe
        /// nécessairement par WMI, d'où une cadence propre.
        /// </summary>
        private void DetectServiceChanges()
        {
            bool tracksServices;

            lock (this._syncRoot)
            {
                tracksServices = this.Exists(entry => entry.IsService);
            }

            // Instance qui ne porte aucun service — celle qu'ouvre la fenêtre
            // d'affinité, par exemple : rien à suivre, rien à demander à WMI.
            if (!tracksServices && this._knownServiceProcessIDs == null)
            {
                return;
            }

            Dictionary<string, int> runningServices = this.GetRunningServiceProcessIDs();

            if (runningServices == null)
            {
                return;
            }

            Dictionary<string, int> previous = this._knownServiceProcessIDs;
            this._knownServiceProcessIDs = runningServices;

            if (previous == null)
            {
                return;
            }

            foreach (KeyValuePair<string, int> pair in previous)
            {
                int processID;

                if (!runningServices.TryGetValue(pair.Key, out processID) || processID != pair.Value)
                {
                    this.RaiseServiceDeleted(pair.Key, pair.Value);
                }
            }

            foreach (KeyValuePair<string, int> pair in runningServices)
            {
                int processID;

                if (!previous.TryGetValue(pair.Key, out processID) || processID != pair.Value)
                {
                    this.RaiseServiceCreated(pair.Key);
                }
            }
        }

        /// <summary>
        /// Projection explicite : mesurée à 21 ms, là où le « SELECT * » que
        /// faisait l'énumération initiale en coûte 338. Les autres propriétés ne
        /// servent qu'à matérialiser un service nouvellement apparu.
        /// </summary>
        private Dictionary<string, int> GetRunningServiceProcessIDs()
        {
            if (this._scope == null)
            {
                return null;
            }

            try
            {
                Dictionary<string, int> processIDsByServiceName =
                    new Dictionary<string, int>(256, StringComparer.OrdinalIgnoreCase);

                ObjectQuery query = new ObjectQuery("SELECT ProcessId, Name, State FROM Win32_Service WHERE State = 'Running'");

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(this._scope, query))
                {
                    foreach (ManagementObject wmiObject in searcher.Get())
                    {
                        object name = wmiObject["Name"];
                        object processID = wmiObject["ProcessId"];

                        if (name == null || processID == null)
                        {
                            continue;
                        }

                        processIDsByServiceName[name.ToString()] =
                            int.Parse(processID.ToString(), CultureInfo.InvariantCulture);
                    }
                }

                return processIDsByServiceName;
            }
            catch
            {
                return null;
            }
        }

        private void RaiseServiceDeleted(string serviceName, int processID)
        {
            Process removed = null;

            lock (this._syncRoot)
            {
                for (int i = this.Count - 1; i >= 0; i--)
                {
                    if (this[i].IsService
                        && this[i].ProcessID == processID
                        && string.Equals(this[i].ProcessName, serviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        removed = this[i];
                        this.RemoveAt(i);
                        break;
                    }
                }
            }

            // L'entrée a pu partir avec son hôte au tick précédent : dans ce cas
            // la tuile a déjà disparu et il n'y a rien à signaler.
            if (removed != null)
            {
                this.RaiseDeleted(removed);
            }
        }

        private void RaiseServiceCreated(string serviceName)
        {
            Process service = this.MaterializeService(serviceName);

            if (service == null)
            {
                return;
            }

            bool added;

            lock (this._syncRoot)
            {
                added = !this.Exists(entry => entry.IsSameEntry(service));

                if (added)
                {
                    this.Add(service);
                }
            }

            if (!added)
            {
                return;
            }

            this.ResolveServiceHost(service);
            this.RaiseCreated(service);
        }

        private Process MaterializeService(string serviceName)
        {
            // Le nom d'un service est un identifiant ; une apostrophe y romprait
            // la clause WHERE. On préfère ne pas afficher l'entrée.
            if (this._scope == null || serviceName.IndexOf('\'') >= 0 || serviceName.IndexOf('\\') >= 0)
            {
                return null;
            }

            try
            {
                ObjectQuery query = new ObjectQuery("SELECT * FROM Win32_Service WHERE Name = '" + serviceName + "'");

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(this._scope, query))
                {
                    foreach (ManagementObject wmiObject in searcher.Get())
                    {
                        return new Process(wmiObject, this._scope, TargetInstanceEnum.Win32_Service);
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// Renseigne sur le service son processus hôte, et propage la liste des
        /// services partagés à l'hôte comme aux frères — ce que faisait
        /// ResolveServiceHosts au chargement, ici pour un seul PID.
        /// </summary>
        private void ResolveServiceHost(Process service)
        {
            lock (this._syncRoot)
            {
                List<string> serviceNames = new List<string>();
                Process host = null;

                foreach (Process entry in this)
                {
                    if (entry.ProcessID != service.ProcessID)
                    {
                        continue;
                    }

                    if (entry.IsService)
                    {
                        serviceNames.Add(entry.ProcessName);
                    }
                    else if (host == null)
                    {
                        host = entry;
                    }
                }

                service.HostProcessName = host == null ? null : host.ProcessName;

                if (serviceNames.Count <= 1)
                {
                    return;
                }

                foreach (Process entry in this)
                {
                    if (entry.ProcessID == service.ProcessID)
                    {
                        entry.SharedServiceNames = serviceNames;
                    }
                }
            }
        }

        private void RaiseCreated(Process process)
        {
            ProcessEventArgs eventArgs = new ProcessEventArgs(process, ProcessEventTypeEnum.Created);

            ProcessEventHandler arrived = this.ProcessEventArrived;

            if (arrived != null)
            {
                arrived(this, eventArgs);
            }

            ProcessEventHandler created = this.ProcessCreated;

            if (created != null)
            {
                created(this, eventArgs);
            }
        }

        private void RaiseDeleted(Process process)
        {
            ProcessEventArgs eventArgs = new ProcessEventArgs(process, ProcessEventTypeEnum.Deleted);

            ProcessEventHandler arrived = this.ProcessEventArrived;

            if (arrived != null)
            {
                arrived(this, eventArgs);
            }

            ProcessEventHandler deleted = this.ProcessDeleted;

            if (deleted != null)
            {
                deleted(this, eventArgs);
            }
        }


        public ComputerSystemStruct GetProcessorsProperties()
        {
            ComputerSystemStruct processorsProperties = default(ComputerSystemStruct);

            try
            {

                    ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(this._scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem"));
                    foreach (ManagementObject item in managementObjectSearcher.Get())
                    {
                        processorsProperties.NumberOfProcessors = item["NumberOfProcessors"] == null?0:int.Parse(item["NumberOfProcessors"].ToString());
                        processorsProperties.NumberOfLogicalProcessors = item["NumberOfLogicalProcessors"] == null ?0: int.Parse(item["NumberOfLogicalProcessors"].ToString());

                        processorsProperties.Name = item["Name"] == null? string.Empty: item["Name"].ToString();
                        processorsProperties.DNSHostName = item["DNSHostName"] == null? string.Empty: item["DNSHostName"].ToString();
                        processorsProperties.UserName = item["UserName"] == null? string.Empty: item["UserName"].ToString();

                }
            }
            catch (ManagementException e)
            {
                System.Diagnostics.Debug.Print(e.Message);
            }

            return processorsProperties;

        }


    }

    public struct ComputerSystemStruct
    {
        public int NumberOfLogicalProcessors;
        public int NumberOfProcessors;
        public string Name;
        public string NameFormat;
        public string DNSHostName;
        public string UserName;
    }

    public enum TargetInstanceEnum
    {
        Win32_Process = 0,
        Win32_Service = 1
    }

    public enum ProcessEventTypeEnum
    {
        Unknown = -1,
        Created = 0,
        Modified = 1,
        Deleted = 2,
        OnError = 3
    }

    public class ProcessEventArgs : EventArgs
    {
        private Process _process = null;
        private int _processID = 0;
        private ProcessEventTypeEnum _eventType = ProcessEventTypeEnum.Unknown;

        public ProcessEventArgs()
        { 
        }

        public ProcessEventArgs(int processID, ProcessEventTypeEnum eventType = ProcessEventTypeEnum.Unknown)
        {
            this._processID = processID;
            this._eventType = eventType;
        }

        public ProcessEventArgs(Process process, ProcessEventTypeEnum eventType = ProcessEventTypeEnum.Unknown)
        {
            this._process = process;
            this._processID = process.ProcessID;
            this._eventType = eventType;
        }

        public int ProcessID
        {
            get
            {
                return this._processID;
            }
            set
            {
                this._processID = value;
            }
        }

        public Process Process
        {
            get
            {
                return this._process;
            }
            set
            {
                this._process = value;
            }
        }

        public ProcessEventTypeEnum EventType
        {
            get
            {
                return this._eventType;
            }
            set
            {
                this._eventType = value;
            }
        }

    }
}
