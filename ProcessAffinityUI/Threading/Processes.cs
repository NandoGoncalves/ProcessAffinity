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
        public event ProcessEventHandler ProcessModified;


        private string _computerName = string.Empty;
        private string _domain = string.Empty;
        private string _user = string.Empty;
        private string _password = string.Empty;
        private ManagementScope _scope = null;

        private ManagementEventWatcher _watcher = null;
        private TargetInstanceEnum _targetInstance = TargetInstanceEnum.Win32_Process;

        /// <summary>Intervalle unique d'échantillonnage du % CPU.</summary>
        private const int CPUSamplingIntervalMilliseconds = 1000;

        private readonly CancellationTokenSource _cpuSamplingCancellation = new CancellationTokenSource();

        /// <summary>
        /// Sérialise les accès à la collection : le watcher ajoute et retire des
        /// éléments depuis son propre thread pendant que l'échantillonneur la lit.
        /// </summary>
        private readonly object _syncRoot = new object();

        public Processes()
            :this(TargetInstanceEnum.Win32_Process)
        { }

        public Processes(TargetInstanceEnum targetInstance)
            : this("localhost", string.Empty, string.Empty, string.Empty, targetInstance)
        {
            this.Initialize();
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
        }

        /// <summary>
        /// Arrête la boucle d'échantillonnage du % CPU. Idempotent.
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
            this.StopCPUSampling();
            this._cpuSamplingCancellation.Dispose();

            // http://stackoverflow.com/questions/26229344/how-to-prevent-wmi-quotas-from-overflowing
            if (this._watcher != null)
            {
                this._watcher.Stop();
                this._watcher.Dispose();
                this._watcher = null;
            }

            GC.Collect();
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

                Task.Factory.StartNew(() => this.InitializeWatcher(), TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness);

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

        private void InitializeWatcher()
        {
                string WMI_OPER_EVENT_QUERY = string.Empty;
                if (_targetInstance == TargetInstanceEnum.Win32_Process)
                {
                    WMI_OPER_EVENT_QUERY = @"SELECT * FROM 
                            __InstanceOperationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";
                }
                else
                {
                    WMI_OPER_EVENT_QUERY = @"SELECT * FROM 
                            __InstanceOperationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Service'";
                }

                EventWatcherOptions eventWatcherOptions = new EventWatcherOptions();
                eventWatcherOptions.Timeout = new TimeSpan(0, 10, 0);
                this._watcher = new ManagementEventWatcher(this._scope, new EventQuery(WMI_OPER_EVENT_QUERY), eventWatcherOptions);
                this._watcher.Options.Timeout = new TimeSpan(0, 5, 0);

                while (true)
                {
                    ManagementBaseObject e = this._watcher.WaitForNextEvent();

                    Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                string eventType = e.ClassPath.ClassName;
                                ProcessAffinityUI.Threading.WIN32_Process win32Process = new
                                    ProcessAffinityUI.Threading.WIN32_Process(e["TargetInstance"] as ManagementBaseObject);

                                Process process = new Process(win32Process, this._scope);

                                if (ProcessEventArrived != null) ProcessEventArrived(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Unknown));
                                switch (eventType)
                                {
                                    case "__InstanceCreationEvent":
                                        bool processAdded = false;

                                        lock (this._syncRoot)
                                        {
                                            if (!this.ProcessExists(process))
                                            {
                                                this.Add(process);
                                                processAdded = true;
                                            }
                                        }

                                        if (processAdded)
                                        {
                                            if (ProcessCreated != null) ProcessCreated(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Created));
                                        }
                                        break;
                                    case "__InstanceDeletionEvent":
                                        lock (this._syncRoot)
                                        {
                                            IEnumerable<Process> processes = this.Where(item => item.ProcessID == win32Process.ProcessId);
                                            if (processes != null && processes.Count() > 0) this.Remove(processes.ElementAt(0));
                                        }

                                        if (ProcessDeleted != null) ProcessDeleted(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Deleted));
                                        break;
                                    case "__InstanceModificationEvent":
                                        if (ProcessModified != null) ProcessModified(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Modified));
                                        break;
                                }
                            }
                            catch //(Exception ex)
                            {
                                //ProcessCreated(this, new ProcessEventArgs(new Process(_computerName, 0, ex.Source), ProcessEventTypeEnum.OnError)); 
                            }
                        }, TaskCreationOptions.LongRunning);

                }
        }




//        private void InitalizeWatcher()
//        {
//            string WMI_OPER_EVENT_QUERY = @"SELECT * FROM 
//                __InstanceOperationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'";


//            EventWatcherOptions eventWatcherOptions = new EventWatcherOptions();
//            eventWatcherOptions.Timeout = new TimeSpan(0, 10, 0);
//            watcher = new ManagementEventWatcher(this._scope, new EventQuery(WMI_OPER_EVENT_QUERY), eventWatcherOptions);

//            //watcher.Query.QueryLanguage = "WQL";

//            //watcher.Query.QueryString = WMI_OPER_EVENT_QUERY;
//            //watcher.Scope = this._scope;

//            watcher.EventArrived += new EventArrivedEventHandler(watcher_EventArrived);
            
//            watcher.Start();

            

//        }

//        private void watcher_EventArrived(object sender, EventArrivedEventArgs e)
//        {
//            try
//            {
//                string eventType = e.NewEvent.ClassPath.ClassName;
//                ProcessAffinityUI.Threading.WIN32_Process win32Process = new
//                    ProcessAffinityUI.Threading.WIN32_Process(e.NewEvent["TargetInstance"] as ManagementBaseObject);

//                Process process = new Process(win32Process);

//                switch (eventType)
//                {
//                    case "__InstanceCreationEvent":
//                        this.Add(process);
//                        if (ProcessCreated != null) ProcessCreated(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Created)); break;
//                    case "__InstanceDeletionEvent":
//                        IEnumerable<Process> processes = this.Where(item => item.ProcessID == win32Process.ProcessId);
//                        if(processes != null && processes.Count() > 0)this.Remove(processes.ElementAt(0));
//                        if (ProcessDeleted != null) ProcessDeleted(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Deleted)); break;
//                    case "__InstanceModificationEvent":
//                        if (ProcessModified != null) ProcessModified(this, new ProcessEventArgs(process, ProcessEventTypeEnum.Modified)); break;
//                }
//            }
//            catch (Exception ex)
//            {
//                throw new Exception("Watcher_EventArrived : \r\n" + ex.Message);
//            }
//        }

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
        }

        public ProcessEventArgs(Process process, ProcessEventTypeEnum eventType = ProcessEventTypeEnum.Unknown)
        {
            this._process = process;
            this._processID = process.ProcessID;
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
