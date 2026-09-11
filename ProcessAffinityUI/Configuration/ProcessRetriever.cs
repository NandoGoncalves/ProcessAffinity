using System;
using System.Text;
using System.Configuration;
using System.Linq;

namespace ProcessAffinityUI.Configuration
{
    public class ProcessRetriever
    {

        //public static ProcessRetrieverSection _Config = ConfigurationManager.GetSection("processRetriever") as ProcessRetrieverSection;
        private static ProcessElementCollection _monitoredProcesses = null;
        

        public static ProcessElementCollection GetProcesses()
        {
            if (_monitoredProcesses == null)
            {
                var configMap = new ExeConfigurationFileMap();
                configMap.ExeConfigFilename = ".\\ProcessAffinityUI.config";
                var customConfig = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
                ProcessRetrieverSection s = customConfig.Sections["processRetriever"] as ProcessRetrieverSection;
                _monitoredProcesses = s.Processes;
                
            }
            return _monitoredProcesses; //_Config.Processes;
        }

        public static ProcessElement GetMonitoredProcess(string name)
        {
            if (name == "FIH32.exe") System.Diagnostics.Debug.Print(name);

            var p = (from ProcessElement monitoredprocess in GetProcesses()
                     where name.Equals(monitoredprocess.Name, StringComparison.OrdinalIgnoreCase)
                     select monitoredprocess).FirstOrDefault();

            return p;
        }

    }
    public class ProcessRetrieverSection : ConfigurationSection
    {
        [ConfigurationProperty("processes")]
        public ProcessElementCollection Processes
        {
            get { return (ProcessElementCollection)this["processes"]; }
        }
    }

    [ConfigurationCollection(typeof(ProcessElement))]
    public class ProcessElementCollection : ConfigurationElementCollection
    {
        public ProcessElement this[int index]
        {
            get { return (ProcessElement)BaseGet(index); }
            set
            {
                if (BaseGet(index) != null)
                    BaseRemoveAt(index);

                BaseAdd(index, value);
            }
        }
        protected override ConfigurationElement CreateNewElement()
        {
            return new ProcessElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ProcessElement)element).Name;
        }
    }
    public class ProcessElement : ConfigurationElement
    {
        

        public ProcessElement() { }

        [ConfigurationProperty("name", DefaultValue = "", IsKey = true, IsRequired = true)]
        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        [ConfigurationProperty("priority", DefaultValue = "4", IsKey = false, IsRequired = true)]
        public int Priority
        {
            get { return (int)this["priority"]; }
            set { this["priority"] = value; }
        }

        [ConfigurationProperty("affinity", DefaultValue = "1", IsKey = false, IsRequired = true)]
        public string Affinity
        {
            get { return (string)this["affinity"]; }
            set { this["affinity"] = value; }
        }

        public int GetProcessorAffinity()
        {
           
            string[] affinityArray = Affinity.Split(';');
            int processorAffinity = 0;

            for (int i = 0; i < affinityArray.Length; i++)
            {
                if (int.Parse(affinityArray[i]) > 0)
                {
                    processorAffinity += (int)Math.Pow(2, i); // int.Parse(affinityArray[i]
                }
                
            }

            return processorAffinity;
        }

        [ConfigurationProperty("kill", DefaultValue = "0", IsKey = false, IsRequired = true)]
        public int Kill
        {
            get { return (int)this["kill"]; }
            set { this["kill"] = value; }
        }

        //[ConfigurationProperty("sourcePath", DefaultValue = "", IsRequired = true)]
        //public string SourcePath
        //{
        //    get { return (string)this["sourcePath"]; }
        //    set { this["sourcePath"] = value; }
        //}

        //[ConfigurationProperty("destinationPath", DefaultValue = "", IsRequired = true)]
        //public string DestinationPath
        //{
        //    get { return (string)this["destinationPath"]; }
        //    set { this["destinationPath"] = value; }
        //}

    }
}
