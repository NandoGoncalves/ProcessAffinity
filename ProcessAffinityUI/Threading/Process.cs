using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Management;
using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI.Threading
{

    public delegate void NotifyCPUUsageChangeDelegate(int cpuUsage);

    public class Process
    {
        private WIN32_Process _win32Process = null;
        private int _cpuUsage = 0;
        private string _executablePath = string.Empty;

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

            // this._executablePath = (this._targetInstance == TargetInstanceEnum.Win32_Process) ? WmiObject["ExecutablePath"].ToString(): WmiObject["PathName"].ToString(); // Not yet initialize
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
        public int CPUUsage { get { return this._cpuUsage; } }
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

        public nuint GetProcessorAffinity()
        {
            nuint processorAffinity = 0;

            try
            {
                nint affinity = (nint)System.Diagnostics.Process.GetProcessById(this.ProcessID, this.ComputerName).ProcessorAffinity;
                processorAffinity = unchecked((nuint)affinity);
            }
            catch//(Exception e)
            {
                //System.Windows.MessageBox.Show(e.Message, "GetProcessorAffinity");
            }

            return processorAffinity;
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

        public void SetCPUUsage(int cpuUsage)
        {
            // http://social.msdn.microsoft.com/Forums/en-US/csharplanguage/thread/469ec6b7-4727-4773-9dc7-6e3de40e87b8/
            this._cpuUsage = cpuUsage;

            if (notifyCPUUsageChangeDelegate != null)
            {
                notifyCPUUsageChangeDelegate(this._cpuUsage);
            }
        }

        public void SetNotifyCPUUsageChangeDelegate(NotifyCPUUsageChangeDelegate notifyCPUUsageChange)
        {
            notifyCPUUsageChangeDelegate = notifyCPUUsageChange;
        }

        public bool ToKill { get; set; }

        public void Kill()
        {
            ManagementObjectCollection managementObjectCollection = GetManagementObjectCollection();

            foreach (ManagementObject managementObject in managementObjectCollection)
            {
                try
                {
                    managementObject.InvokeMethod("Terminate", null);
                }
                catch
                {
                    // Le process peut ne plus exister
                }
            }
        }

        public bool IsAlive()
        {
            bool isAlive = false;

            ManagementObjectCollection managementObjectCollection = GetManagementObjectCollection();

            if (managementObjectCollection != null && managementObjectCollection.Count > 0)
            {
                isAlive = true;
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


