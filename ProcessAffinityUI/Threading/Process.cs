using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Management;
using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI.Threading
{

    public delegate void NotifyCPUUsageChangeDelegate(double? cpuUsage);

    public class Process
    {
        private WIN32_Process _win32Process = null;
        private double? _cpuUsage = null;
        private string _executablePath = string.Empty;

        // Échantillon précédent, pour le calcul du % CPU par delta.
        private bool _hasCPUSample = false;
        private long _lastCPUSampleTimestamp = 0;
        private long _lastTotalProcessorTime = 0;
        private long _lastCreateTime = 0;
        private long _lastActivitySignature = 0;

        private bool? _isProcessorAffinityReadable = null;
        private bool? _isModifiable = null;

        private NotifyCPUUsageChangeDelegate notifyCPUUsageChangeDelegate = null;
        private TargetInstanceEnum _targetInstance = TargetInstanceEnum.Win32_Process;

        public Process()
        { }

        public Process(string computerName, int processID, string processName)
        {
            this.ComputerName = computerName; // Vérifier si n'existe pas dans WIN32Process
            this.ProcessID = processID;
            this.ProcessName = processName;
        }

        public Process(WIN32_Process win32Process, ManagementScope scope)
            : this(win32Process, scope, null)
        { }

        public Process(WIN32_Process win32Process, ManagementScope scope, NotifyCPUUsageChangeDelegate notifyCPUUsageChangeDelegate)
        {
            this.ComputerName = win32Process.CSName;
            this.Scope = scope;
            this.ProcessID = (int)win32Process.ProcessId;
            this.ProcessName = win32Process.Name;
            this._executablePath = win32Process.ExecutablePath;
            this.notifyCPUUsageChangeDelegate = notifyCPUUsageChangeDelegate;
            this._win32Process = win32Process;

            InitializeFromConfigFile();
        }

        public Process(ManagementObject WmiObject, ManagementScope scope)
            :this(WmiObject, scope, TargetInstanceEnum.Win32_Process)
        {
        }

        public Process(ManagementObject WmiObject, ManagementScope scope, TargetInstanceEnum targetInstance)
        {
            this._targetInstance = targetInstance;
            this._win32Process = new ProcessAffinityUI.Threading.WIN32_Process(WmiObject);
            this.ProcessID = int.Parse(WmiObject["ProcessID"].ToString());
            this.ProcessName = WmiObject["Name"].ToString();
            this.ComputerName = (this._targetInstance == TargetInstanceEnum.Win32_Process)?WmiObject["CSName"].ToString(): WmiObject["SystemName"].ToString();
            //this.Description = WmiObject["Description"].ToString(); // Fall for some services

            // Le chemin vient du « SELECT * » déjà fait : aucune requête de plus.
            // C'est lui qui donne son icône à la tuile. Il restait vide ici, et
            // seul le watcher — dont les objets passaient par le constructeur
            // WIN32_Process — en fournissait un, au fil des évènements de
            // modification. Sans watcher, plus aucune tuile n'avait d'icône.
            //
            // Null sur les processus protégés, et absent des services : PathName
            // y porte une ligne de commande, pas un chemin exploitable.
            if (this._targetInstance == TargetInstanceEnum.Win32_Process)
            {
                object executablePath = WmiObject["ExecutablePath"];

                this._executablePath = executablePath == null ? string.Empty : executablePath.ToString();

                // WMI rend un chemin vide pour les processus élevés. Le repli
                // natif les rattrape, et avec eux leur icône — c'est à elle qu'on
                // reconnaît une application dans un panneau de trois cents tuiles.
                if (this._executablePath.Length == 0)
                {
                    this._executablePath = NativeProcessAccess.TryGetImagePath(this.ProcessID) ?? string.Empty;
                }
            }

            this.Scope = scope;




            InitializeFromConfigFile();

        }

        private void InitializeFromConfigFile()
        {
            var monitoredProcess = ProcessRetriever.GetMonitoredProcess(this.ProcessName);

            if (monitoredProcess != null)
            {
                try
                {
                    this.ToKill = monitoredProcess.Kill > 0 ? true : false;
                    this.Priority = (int)Process.ToProcessPriorityEnum((int)monitoredProcess.Priority);
                    this.SetProcessorAffinity(monitoredProcess.GetProcessorAffinity());
                    this.IsProcessMonitored = true;
                }
                catch //(Exception e)
                {
                    //System.Diagnostics.Debug.Print(e.Message);
                }
            }
        }

        public int ProcessID { get; set; }
        public string ProcessName { get; set; }
        public string ComputerName { get; set; }

        public string Description { get; set; }

        // null tant qu'aucun delta n'a pu être calculé (premier échantillon,
        // ou processus dont le temps CPU est inaccessible).
        public double? CPUUsage { get { return this._cpuUsage; } }

        /// <summary>
        /// Sort de la règle enregistrée pour cet exécutable, ou None s'il n'y en a
        /// pas.
        ///
        /// Cet état, son détail, le signalement et les compteurs sont écrits depuis
        /// le thread de matérialisation et lus depuis celui de l'IHM. Ils sont donc
        /// tous gardés par le même verrou : l'état et son détail doivent en outre
        /// être vus ensemble, une infobulle ne devant jamais présenter le détail
        /// d'un état qui n'est plus le sien.
        /// </summary>
        public Configuration.RuleStateEnum RuleState
        {
            get { lock (this._ruleSyncRoot) { return this._ruleState; } }
        }

        /// <summary>Détail de l'échec, présenté dans l'infobulle. Null si tout va bien.</summary>
        public string RuleDetail
        {
            get { lock (this._ruleSyncRoot) { return this._ruleDetail; } }
        }

        /// <summary>
        /// Rétablissements de la règle depuis le lancement de l'application. Zéro
        /// tant que personne n'y a touché.
        /// </summary>
        public int RuleEnforcementCount
        {
            get { lock (this._ruleSyncRoot) { return this._ruleEnforcementCount; } }
        }

        private readonly object _ruleSyncRoot = new object();
        private Configuration.RuleStateEnum _ruleState = Configuration.RuleStateEnum.None;
        private string _ruleDetail = null;
        private bool _hasRuleJustApplied = false;
        private int _ruleCorrectionCount = 0;
        private int _ruleEnforcementCount = 0;

        /// <summary>
        /// Classe de priorité réellement en vigueur, ou null si elle n'est pas
        /// lisible. À ne pas confondre avec <see cref="Priority"/>, qui rend la
        /// valeur WMI capturée à l'énumération et ignore les changements venus de
        /// l'extérieur.
        /// </summary>
        public int? GetPriorityClass()
        {
            return NativeProcessAccess.TryGetPriorityClass(this.ProcessID);
        }

        /// <summary>
        /// Corrections consécutives appliquées à ce processus. Remis à zéro dès
        /// qu'un contrôle le trouve conforme.
        /// </summary>
        internal int RuleCorrectionCount
        {
            get { lock (this._ruleSyncRoot) { return this._ruleCorrectionCount; } }
            set { lock (this._ruleSyncRoot) { this._ruleCorrectionCount = value; } }
        }

        internal void SetRuleState(Configuration.RuleStateEnum state, string detail)
        {
            lock (this._ruleSyncRoot)
            {
                this._hasRuleJustApplied = this._hasRuleJustApplied
                                           || (state != Configuration.RuleStateEnum.None && this._ruleState != state);

                this._ruleState = state;
                this._ruleDetail = detail;
            }
        }

        /// <summary>
        /// Signale un rétablissement de la règle. L'état ne change pas — la règle
        /// reste appliquée — mais l'utilisateur doit le voir : sans cela, un
        /// conflit survenu pendant que la fenêtre était réduite ne laisserait
        /// aucune trace à l'écran.
        /// </summary>
        internal void NotifyRuleEnforced()
        {
            lock (this._ruleSyncRoot)
            {
                this._ruleEnforcementCount++;
                this._hasRuleJustApplied = true;
            }
        }

        /// <summary>
        /// Consommé par la tuile : le signalement ne dure qu'un relevé. Test et
        /// remise à zéro sous le même verrou, sans quoi deux lectures rapprochées
        /// pourraient allumer deux fois — ou perdre un signalement posé entre les
        /// deux.
        /// </summary>
        internal bool ConsumeRuleJustApplied()
        {
            lock (this._ruleSyncRoot)
            {
                bool value = this._hasRuleJustApplied;
                this._hasRuleJustApplied = false;

                return value;
            }
        }

        /// <summary>
        /// Vrai quand une grandeur du processus a changé depuis le relevé
        /// précédent : cycles consommés, mémoire privée, poignées, threads, défauts
        /// de page. C'est ce qui rallume le bandeau de nom ; il s'éteint au relevé
        /// suivant si plus rien n'arrive.
        /// </summary>
        public bool HasRecentActivity { get; private set; }
        public string ExecutablePath { get { return this._executablePath; } }
        public ManagementScope Scope { get; set; }
        public int Priority
        {
            get
            {
                return this._targetInstance == TargetInstanceEnum.Win32_Process?(int)this._win32Process.Priority:0;
            }

            set
            {
                this._win32Process.SetPriority((int)value);
            }
        }

        /// <summary>
        /// Masque d'affinité, ou null lorsque la lecture échoue. Un masque nul
        /// n'intersecte aucun cœur : confondre l'illisible avec le vide faisait
        /// disparaître de tous les filtres les processus des autres
        /// utilisateurs.
        /// </summary>
        public nuint? GetProcessorAffinity()
        {
            nuint processorAffinity;

            if (NativeProcessAccess.TryGetProcessorAffinity(this.ProcessID, out processorAffinity))
            {
                this._isProcessorAffinityReadable = true;

                return processorAffinity;
            }

            this._isProcessorAffinityReadable = false;

            return null;
        }

        /// <summary>
        /// Résultat de la dernière lecture d'affinité, ou null si elle n'a pas
        /// encore été tentée. Évite de relire pour établir la ventilation.
        /// </summary>
        public bool? IsProcessorAffinityReadable
        {
            get { return this._isProcessorAffinityReadable; }
        }

        /// <summary>
        /// L'affinité et la priorité de ce processus peuvent-elles être écrites.
        /// Sans ce droit, la fenêtre d'affinité s'ouvrait, acceptait des cases
        /// cochées et validait sans que rien ne se produise. Stable pour la durée
        /// de vie du processus, donc évalué une seule fois.
        /// </summary>
        public bool IsModifiable
        {
            get
            {
                if (this._isModifiable == null)
                {
                    this._isModifiable = NativeProcessAccess.CanModifyProcess(this.ProcessID);
                }

                return this._isModifiable.Value;
            }
        }

        public void SetProcessorAffinity(nuint processorAffinity)
        {
            try
            {
                nint affinity = unchecked((nint)processorAffinity);
                System.Diagnostics.Process.GetProcessById(this.ProcessID, this.ComputerName).ProcessorAffinity = (IntPtr)affinity;
            }
            catch (Exception e)
            {
                //System.Windows.MessageBox.Show(e.Message, "SetProcessorAffinity");
            }
        }

        public void SetCPUUsage(double? cpuUsage)
        {
            // http://social.msdn.microsoft.com/Forums/en-US/csharplanguage/thread/469ec6b7-4727-4773-9dc7-6e3de40e87b8/
            this._cpuUsage = cpuUsage;

            if (notifyCPUUsageChangeDelegate != null)
            {
                notifyCPUUsageChangeDelegate(this._cpuUsage);
            }
        }

        /// <summary>
        /// Calcule le % CPU par delta du temps processeur sur le temps réellement
        /// écoulé, rapporté au nombre de processeurs logiques. Le premier passage ne
        /// produit aucune valeur : il ne fait qu'établir la référence.
        /// Les temps sont exprimés en unités de 100 ns.
        /// </summary>
        internal void UpdateCPUUsage(long createTime, long totalProcessorTime, long timestamp, long activitySignature)
        {
            // Un PID réutilisé porte une date de création différente : on repart
            // d'une nouvelle référence au lieu de produire une valeur aberrante.
            if (this._hasCPUSample && createTime == this._lastCreateTime)
            {
                long elapsedTicks = timestamp - this._lastCPUSampleTimestamp;

                // Le watcher rallumait le bandeau de nom à chaque modification
                // d'instance WMI. La signature du relevé joue le même rôle : elle
                // change dès qu'une grandeur du processus a bougé.
                this.HasRecentActivity = activitySignature != this._lastActivitySignature;

                if (elapsedTicks > 0)
                {
                    double elapsedSeconds = (double)elapsedTicks / System.Diagnostics.Stopwatch.Frequency;
                    double processorSeconds = (totalProcessorTime - this._lastTotalProcessorTime) / 10000000d;
                    double cpuUsage = processorSeconds / elapsedSeconds / Environment.ProcessorCount * 100d;

                    if (cpuUsage < 0d)
                    {
                        cpuUsage = 0d;
                    }
                    else if (cpuUsage > 100d)
                    {
                        cpuUsage = 100d;
                    }

                    this.SetCPUUsage(cpuUsage);
                }
            }

            this._lastActivitySignature = activitySignature;
            this._lastTotalProcessorTime = totalProcessorTime;
            this._lastCPUSampleTimestamp = timestamp;
            this._lastCreateTime = createTime;
            this._hasCPUSample = true;
        }

        public void SetNotifyCPUUsageChangeDelegate(NotifyCPUUsageChangeDelegate notifyCPUUsageChange)
        {
            notifyCPUUsageChangeDelegate = notifyCPUUsageChange;
        }

        public bool ToKill { get; set; }

        /// <summary>
        /// Processus dont la terminaison provoque un écran bleu ou la fermeture
        /// de la session. Le risque n'est plus théorique depuis que
        /// SeDebugPrivilege est activé au démarrage : en session élevée,
        /// Terminate aboutirait.
        /// </summary>
        private static readonly string[] CriticalProcessNames =
        {
            "system", "idle", "secure system", "registry", "memory compression",
            "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        };

        public bool IsCriticalSystemProcess
        {
            get
            {
                if (this.ProcessID <= 4)
                {
                    return true;
                }

                string name = this.ProcessName ?? string.Empty;

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }

                return CriticalProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// L'énumération WMI est paresseuse : elle interroge le service depuis le
        /// foreach, hors du try interne. Une portée injoignable — quota WMI
        /// saturé, connexion perdue — y levait une ManagementException qui
        /// remontait jusqu'au dispatcher et fermait l'application sans un mot.
        /// Retourne le code de Win32_Process.Terminate — 0 succès, 2 accès
        /// refusé, 3 privilège insuffisant —, ou null quand la demande n'a pas
        /// pu être transmise. Ce code était ignoré : un refus passait pour une
        /// réussite.
        /// </summary>
        public uint? Kill()
        {
            try
            {
                ManagementObjectCollection managementObjectCollection = GetManagementObjectCollection();

                uint? returnCode = null;
                bool invoked = false;

                foreach (ManagementObject managementObject in managementObjectCollection)
                {
                    invoked = true;

                    try
                    {
                        object result = managementObject.InvokeMethod("Terminate", null);
                        uint code = result == null ? 0u : Convert.ToUInt32(result);

                        // On retient le premier échec rencontré.
                        if (returnCode == null || returnCode == 0u)
                        {
                            returnCode = code;
                        }
                    }
                    catch
                    {
                        return null;
                    }
                }

                // Aucune instance : le processus a déjà disparu, c'est le
                // résultat recherché.
                return invoked ? returnCode : (uint?)0u;
            }
            catch
            {
                return null;
            }
        }

        public bool IsAlive()
        {
            bool isAlive = false;

            try
            {
                ManagementObjectCollection managementObjectCollection = GetManagementObjectCollection();

                if (managementObjectCollection != null && managementObjectCollection.Count > 0)
                {
                    isAlive = true;
                }
            }
            catch
            {
                // Même énumération paresseuse que dans Kill.
            }

            return isAlive;
        }

        private ManagementObjectCollection GetManagementObjectCollection()
        {
            //ManagementScope theScope = new ManagementScope("\\\\" + this.ComputerName + "\\root\\cimv2");

            ObjectQuery theQuery = null;
            if (_targetInstance == TargetInstanceEnum.Win32_Process)
            {
                theQuery = new ObjectQuery("SELECT * FROM Win32_Process WHERE ProcessId=" + this.ProcessID.ToString());
            }
            else
            {
                theQuery = new ObjectQuery("SELECT * FROM Win32_service WHERE ProcessId=" + this.ProcessID.ToString());
            }

            ManagementObjectSearcher theSearcher = new ManagementObjectSearcher(this.Scope, theQuery);
            ManagementObjectCollection managementObjectCollection = theSearcher.Get();

            return managementObjectCollection;
        }

        public static ProcessPriorityEnum ToProcessPriorityEnum(int priority)
        {
            ProcessPriorityEnum processPriorityEnum = ProcessPriorityEnum.Unknown;

            switch (priority)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    processPriorityEnum = ProcessPriorityEnum.Idle;
                    break;
                case 6:
                case 7:
                    processPriorityEnum = ProcessPriorityEnum.BelowNormal;
                    break;
                case 8:
                case 9:
                    processPriorityEnum = ProcessPriorityEnum.Normal;
                    break;
                case 10:
                case 11:
                case 12:
                    processPriorityEnum = ProcessPriorityEnum.AboveNormal;
                    break;
                case 13:
                case 14:
                    processPriorityEnum = ProcessPriorityEnum.HighPriority;
                    break;
                default:
                    processPriorityEnum = ProcessPriorityEnum.RealTime;
                    break;
            }

            return processPriorityEnum;
        }

        public bool IsProcessMonitored { get; private set; }

        public bool IsService { get { return this._targetInstance == TargetInstanceEnum.Win32_Service ? true : false; } }

        /// <summary>
        /// Nom du processus hôte, renseigné sur une entrée de service.
        /// </summary>
        public string HostProcessName { get; set; }

        /// <summary>
        /// Services partageant le même processus hôte, renseigné uniquement
        /// lorsqu'il y en a plusieurs — sur l'hôte comme sur chaque service.
        /// Toute modification d'affinité ou de priorité les affecte tous.
        /// </summary>
        public IList<string> SharedServiceNames { get; set; }

        /// <summary>
        /// Identité d'une entrée : type, PID et nom. Le seul PID ne suffit pas,
        /// plusieurs services partageant un même processus hôte — s'y limiter
        /// faisait disparaître des services et faisait qu'une tuile de service
        /// écrasait celle de son hôte au lieu de s'y ajouter.
        /// </summary>
        public bool IsSameEntry(Process other)
        {
            return other != null
                && this.IsService == other.IsService
                && this.ProcessID == other.ProcessID
                && string.Equals(this.ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
    }
    public enum ProcessPriorityEnum : int
    {
        Unknown = Normal,
        Idle = 64,              // 4    0;255;0     VERT
        BelowNormal = 16384,    // 6    255;255;0   JAUNE
        Normal = 32,            // 8    255;0; 0    ROUGE
        AboveNormal = 32768,    // 10   200;0;100   VIOLET
        HighPriority = 128,     // 13   100;0;100   VIOLET FONCE
        RealTime = 256          //      0;0;0       NOIR
    }
}


