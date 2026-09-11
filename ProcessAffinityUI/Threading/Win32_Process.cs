namespace ProcessAffinityUI.Threading
{
    using System;
    using System.ComponentModel;
    using System.Management;
    using System.Collections;
    using System.Globalization;
    
    
    // Les fonctions ShouldSerialize<PropertyName> sont des fonctions utilisées par l'Explorateur de propriétés VS pour contrôler si une propriété spécifique doit être sérialisée. Ces fonctions sont ajoutées pour toutes les propriétés ValueType ( propriétés de type Int32, BOOL etc. qui ne peuvent pas avoir la valeur null). Ces fonctions utilisent la fonction Is<PropertyName>Null. Ces fonctions sont également utilisées dans l'implémentation TypeConverter pour les propriétés dont la valeur NULL doit être contrôlée afin qu'une valeur vide s'affiche dans l'Explorateur de propriétés en cas de glisser-déplacer dans Visual studio.
    // Les fonctions Is<PropertyName>Null() sont utilisées pour contrôler si une propriété est NULL.
    // Les fonctions Reset<PropertyName> sont ajoutées pour les propriétés Read/Write qui autorisent la valeur Null. Ces fonctions sont utilisées dans le Concepteur VS dans l'Explorateur de propriétés pour définir une propriété à la valeur NULL.
    // Chaque propriété ajoutée à la classe pour la propriété WMI a des attributs définis pour indiquer son comportement dans le concepteur Visual Studio et pour définir un TypeConverter à utiliser.
    // Les fonctions de conversion ToDateTime et ToDmtfDateTime sont ajoutées pour la classe afin de convertir un datetime DMTF en System.DateTime et vice versa.
    // Une classe Early Bound générée pour la classe WMI.Win32_Process
    public class WIN32_Process : System.ComponentModel.Component {
        
        // Propriété privée pour contenir l'espace de noms WMI dans lequel réside la classe.
        private static string CreatedWmiNamespace = "root\\cimv2";
        
        // Propriété privée pour contenir le nom de la classe WMI qui a créé cette classe.
        private static string CreatedClassName = "Win32Process";
        
        // Variable membre privée pour contenir ManagementScope qui est utilisé par différentes méthodes.
        private static System.Management.ManagementScope statMgmtScope = null;
        
        private ManagementSystemProperties PrivateSystemProperties;
        
        // Objet WMI à liaison tardive sous-jacent.
        private System.Management.ManagementObject PrivateLateBoundObject;
        
        // La variable membre pour stocker le comportement de 'validation automatique' pour la classe.
        private bool AutoCommitProp;
        
        // Variable privée pour contenir la propriété incorporée représentant l'instance.
        private System.Management.ManagementBaseObject embeddedObj;
        
        // L'objet WMI actuellement utilisé.
        private System.Management.ManagementBaseObject curObj;
        
        // Indicateur pour indiquer si l'instance est un objet incorporé.
        private bool isEmbedded;
        
        // Ci-dessous, vous trouverez différentes surcharges des constructeurs pour initialiser une instance de la classe avec un objet WMI.
        public WIN32_Process() {
            this.InitializeObject(null, null, null);
        }
        
        public WIN32_Process(string keyHandle) {
            this.InitializeObject(null, new System.Management.ManagementPath(WIN32_Process.ConstructPath(keyHandle)), null);
        }
        
        public WIN32_Process(System.Management.ManagementScope mgmtScope, string keyHandle) {
            this.InitializeObject(((System.Management.ManagementScope)(mgmtScope)), new System.Management.ManagementPath(WIN32_Process.ConstructPath(keyHandle)), null);
        }
        
        public WIN32_Process(System.Management.ManagementPath path, System.Management.ObjectGetOptions getOptions) {
            this.InitializeObject(null, path, getOptions);
        }
        
        public WIN32_Process(System.Management.ManagementScope mgmtScope, System.Management.ManagementPath path) {
            this.InitializeObject(mgmtScope, path, null);
        }
        
        public WIN32_Process(System.Management.ManagementPath path) {
            this.InitializeObject(null, path, null);
        }
        
        public WIN32_Process(System.Management.ManagementScope mgmtScope, System.Management.ManagementPath path, System.Management.ObjectGetOptions getOptions) {
            this.InitializeObject(mgmtScope, path, getOptions);
        }
        
        public WIN32_Process(System.Management.ManagementObject theObject) {
            Initialize();
            //if ((CheckIfProperClass(theObject) == true)) {
                PrivateLateBoundObject = theObject;
                PrivateSystemProperties = new ManagementSystemProperties(PrivateLateBoundObject);
                curObj = PrivateLateBoundObject;
            //}
            //else {
            //    throw new System.ArgumentException("Le nom de classe ne correspond pas.");
            //}
        }
        
        public WIN32_Process(System.Management.ManagementBaseObject theObject) {
            Initialize();
            //if ((CheckIfProperClass(theObject) == true)) {
                embeddedObj = theObject;
                PrivateSystemProperties = new ManagementSystemProperties(theObject);
                curObj = embeddedObj;
                isEmbedded = true;
            //}
            //else {
            //    throw new System.ArgumentException("Le nom de classe ne correspond pas.");
            //}
        }
        
        // La propriété retourne l'espace de noms de la classe WMI.
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string OriginatingNamespace {
            get {
                return "root\\cimv2";
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ManagementClassName {
            get {
                string strRet = CreatedClassName;
                if ((curObj != null)) {
                    if ((curObj.ClassPath != null)) {
                        strRet = ((string)(curObj["__CLASS"]));
                        if (((strRet == null) 
                                    || (strRet == string.Empty))) {
                            strRet = CreatedClassName;
                        }
                    }
                }
                return strRet;
            }
        }
        
        // Propriété pointant vers un objet incorporé pour obtenir les propriétés System de l'objet WMI.
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ManagementSystemProperties SystemProperties {
            get {
                return PrivateSystemProperties;
            }
        }
        
        // Propriété qui retourne l'objet à liaison tardive sous-jacent.
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public System.Management.ManagementBaseObject LateBoundObject {
            get {
                return curObj;
            }
        }
        
        // ManagementScope de l'objet.
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public System.Management.ManagementScope Scope {
            get {
                if ((isEmbedded == false)) {
                    return PrivateLateBoundObject.Scope;
                }
                else {
                    return null;
                }
            }
            set {
                if ((isEmbedded == false)) {
                    PrivateLateBoundObject.Scope = value;
                }
            }
        }
        
        // Propriété qui indique le comportement de validation de l'objet WMI. Si elle a la valeur true, l'objet WMI est enregistré automatiquement à chaque modification d'une propriété (c'est-à-dire que Put() est appelé après la modification d'une propriété).
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AutoCommit {
            get {
                return AutoCommitProp;
            }
            set {
                AutoCommitProp = value;
            }
        }
        
        // ManagementPath de l'objet WMI sous-jacent.
        [Browsable(true)]
        public System.Management.ManagementPath Path {
            get {
                if ((isEmbedded == false)) {
                    return PrivateLateBoundObject.Path;
                }
                else {
                    return null;
                }
            }
            set {
                if ((isEmbedded == false)) {
                    if ((CheckIfProperClass(null, value, null) != true)) {
                        throw new System.ArgumentException("Le nom de classe ne correspond pas.");
                    }
                    PrivateLateBoundObject.Path = value;
                }
            }
        }
        
        // Propriété de portée statique publique utilisée par différentes méthodes.
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static System.Management.ManagementScope StaticScope {
            get {
                return statMgmtScope;
            }
            set {
                statMgmtScope = value;
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété Caption est une description courte (chaîne d’une ligne) de l’objet.")]
        public string Caption {
            get {
                return ((string)(curObj["Caption"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété CommandLine spécifié la ligne de commande utilisée pour démarrer un " +
            "processus particulier si c\'est applicable.")]
        public string CommandLine {
            get {
                return ((string)(curObj["CommandLine"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"CreationClassName indique le nom de la classe ou de la sous-classe utilisée dans la création d’une instance. Quand elle est utilisée avec les autres propriétés clé de cette classe, cette propriété permet à toutes les instances de cette classe et à ses sous-classes d’être identifiées de manière unique.")]
        public string CreationClassName {
            get {
                return ((string)(curObj["CreationClassName"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsCreationDateNull {
            get {
                if ((curObj["CreationDate"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Heure du début de l’exécution du processus.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public System.DateTime CreationDate {
            get {
                if ((curObj["CreationDate"] != null)) {
                    return ToDateTime(((string)(curObj["CreationDate"])));
                }
                else {
                    return System.DateTime.MinValue;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("CSCreationClassName contient le nom de la classe de création du système d’ordinat" +
            "eurs d’étendue.")]
        public string CSCreationClassName {
            get {
                return ((string)(curObj["CSCreationClassName"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Le nom du système d’ordinateurs d’étendue.")]
        public string CSName {
            get {
                return ((string)(curObj["CSName"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété Description fournit une description de l’objet sous forme de texte. " +
            "")]
        public string Description {
            get {
                return ((string)(curObj["Description"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété ExecutablePath indique le chemin vers le fichier exécutable du proce" +
            "ssus.\nExemple : C:\\WINDOWS\\EXPLORER.EXE")]
        public string ExecutablePath {
            get {
                return ((string)(curObj["ExecutablePath"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsExecutionStateNull {
            get {
                if ((curObj["ExecutionState"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Indique la condition d’opération courante du processus. Les valeurs comprennent p" +
            "rêt (2), en cours d’exécution (3), et bloqué (4), entre autres.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ExecutionStateValues ExecutionState {
            get {
                if ((curObj["ExecutionState"] == null)) {
                    return ((ExecutionStateValues)(System.Convert.ToInt32(10)));
                }
                return ((ExecutionStateValues)(System.Convert.ToInt32(curObj["ExecutionState"])));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Chaîne utilisée pour identifier le processus. Un ID de processus est un handle de" +
            " processus.")]
        public string Handle {
            get {
                return ((string)(curObj["Handle"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsHandleCountNull {
            get {
                if ((curObj["HandleCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété HandleCount spécifie le nombre total de handles actuellement ouverts par ce processus. Ce nombre est la somme de handles actuellement ouvert par chaque thread dans ce processus. Un handle est utilisé pour examiner ou modifier les ressources système. Chaque handle a une entrée dans une table maintenue en interne. Ces entrées contiennent les adresses des ressources et le moyen d’identifier le type de ressource.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint HandleCount {
            get {
                if ((curObj["HandleCount"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["HandleCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsInstallDateNull {
            get {
                if ((curObj["InstallDate"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété InstallDate indique la date et l’heure à laquelle l’objet a été inst" +
            "allé. Si cette valeur est vide, ceci n’indique pas que l’objet n’est pas install" +
            "é.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public System.DateTime InstallDate {
            get {
                if ((curObj["InstallDate"] != null)) {
                    return ToDateTime(((string)(curObj["InstallDate"])));
                }
                else {
                    return System.DateTime.MinValue;
                }
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsKernelModeTimeNull {
            get {
                if ((curObj["KernelModeTime"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Durée dans le mode noyau, en nombre de fois 100 nanosecondes. Si cette informatio" +
            "n n’est pas disponible, la valeur 0 doit être utilisée.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong KernelModeTime {
            get {
                if ((curObj["KernelModeTime"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["KernelModeTime"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsMaximumWorkingSetSizeNull {
            get {
                if ((curObj["MaximumWorkingSetSize"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété MaximumWorkingSetSize indique la taille maximale du jeu de travail du processus. Le jeu de travail d’un processus est le jeu de pages de mémoire actuellement visible par le processus dans la RAM physique. Ces pages sont résidentes et disponibles pour utilisation par une application sans déclencher de faute de page.
Exemple : 1413120.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint MaximumWorkingSetSize {
            get {
                if ((curObj["MaximumWorkingSetSize"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["MaximumWorkingSetSize"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsMinimumWorkingSetSizeNull {
            get {
                if ((curObj["MinimumWorkingSetSize"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété MaximumWorkingSetSize indique la taille maximale du jeu de travail du processus. Le jeu de travail d’un processus est le jeu de pages de mémoire actuellement visible par le processus dans la RAM physique. Ces pages sont résidentes et disponibles pour utilisation par une application sans déclencher de faute de page.
Exemple : 20480.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint MinimumWorkingSetSize {
            get {
                if ((curObj["MinimumWorkingSetSize"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["MinimumWorkingSetSize"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété Name définit le nom par lequel l’objet est connu. Lorsqu’elle est mi" +
            "se sous forme de sous-classe, la propriété Name peut être ignorée pour être une " +
            "propriété Key.")]
        public string Name {
            get {
                return ((string)(curObj["Name"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Le nom de la classe de création du système d’exploitation d’étendue.")]
        public string OSCreationClassName {
            get {
                return ((string)(curObj["OSCreationClassName"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Le nom du système d’exploitation d’étendue.")]
        public string OSName {
            get {
                return ((string)(curObj["OSName"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsOtherOperationCountNull {
            get {
                if ((curObj["OtherOperationCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété OtherOperationCount spécifie le nombre d’opérations d’E/S effectuées" +
            ", autres que les opérations de lecture et d’écriture.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong OtherOperationCount {
            get {
                if ((curObj["OtherOperationCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["OtherOperationCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsOtherTransferCountNull {
            get {
                if ((curObj["OtherTransferCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété OtherTransferCount spécifie la quantité de données transférées lors " +
            "des opérations autres que les opérations de lecture et d’écriture.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong OtherTransferCount {
            get {
                if ((curObj["OtherTransferCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["OtherTransferCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPageFaultsNull {
            get {
                if ((curObj["PageFaults"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété PageFaults indique le nombre de fautes de page générées par le proce" +
            "ssus.\nExemple : 10")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint PageFaults {
            get {
                if ((curObj["PageFaults"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["PageFaults"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPageFileUsageNull {
            get {
                if ((curObj["PageFileUsage"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété PageFileUsage indique la taille de fichier d’échange actuellement ut" +
            "ilisée par le processus.\nExemple : 102435")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint PageFileUsage {
            get {
                if ((curObj["PageFileUsage"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["PageFileUsage"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsParentProcessIdNull {
            get {
                if ((curObj["ParentProcessId"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété ParentProcessId spécifie l’identificateur unique du processus qui a créé ce processus. Les numéros d’identificateur de processus sont réutilisés ; ils n’identifient donc un processus que pour le durée de vie de ce processus. Il est possible que le processus identifié par ParentProcessId soit terminé, et ParentProcessId peut donc ne pas référencer un processus en cours d’exécution. Il est aussi possible que ParentProcessId se réfère de manière incorrecte à un processus qui a réutilisé cet identificateur de processus. La propriété CreationDate peut être utilisée pour déterminer si le parent spécifié a été créé après que ce processus ait été créé.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint ParentProcessId {
            get {
                if ((curObj["ParentProcessId"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["ParentProcessId"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPeakPageFileUsageNull {
            get {
                if ((curObj["PeakPageFileUsage"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété PeakPageFileUsage indique la taille de fichier d’échange utilisée pe" +
            "ndant la vie du processus.\nExemple : 102367")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint PeakPageFileUsage {
            get {
                if ((curObj["PeakPageFileUsage"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["PeakPageFileUsage"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPeakVirtualSizeNull {
            get {
                if ((curObj["PeakVirtualSize"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété PeakVirtualSize spécifie l’espace d’adresse virtuelle maximal que le processus a utilisé à un moment donné. L'utilisation de l’espace d’adresse virtuelle n’implique pas nécessairement l’utilisation correspondante du disque ou des pages de mémoire principales. Cependant, l’espace virtuel est fini, et en en utilisant trop, le processus peut limiter sa possibilité de charger des bibliothèques.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong PeakVirtualSize {
            get {
                if ((curObj["PeakVirtualSize"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["PeakVirtualSize"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPeakWorkingSetSizeNull {
            get {
                if ((curObj["PeakWorkingSetSize"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété PeakWorkingSetSize indique la taille du jeu de travail de pic du pro" +
            "cessus.\nExemple : 1413120")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint PeakWorkingSetSize {
            get {
                if ((curObj["PeakWorkingSetSize"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["PeakWorkingSetSize"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPriorityNull {
            get {
                if ((curObj["Priority"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété Priority indique la priorité de planification du processus dans le système d’exploitation. Plus haute est la valeur, plus haute est la priorité que le processus reçoit. Les valeurs de priorité sont comprises entre 0 (la plus faible priorité) et 31 (la plus haute priorité).
Exemple : 7.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint Priority {
            get {
                if ((curObj["Priority"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["Priority"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsPrivatePageCountNull {
            get {
                if ((curObj["PrivatePageCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété PrivatePageCount spécifie le nombre de pages allouées en cours qui n" +
            "e sont accessibles que par ce processus ")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong PrivatePageCount {
            get {
                if ((curObj["PrivatePageCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["PrivatePageCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsProcessIdNull {
            get {
                if ((curObj["ProcessId"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété ProcessId contient l’identificateur de processus global qui peut êtr" +
            "e utilisé pour identifier un processus. La valeur est valide à partir du moment " +
            "de la création du processus jusqu’à ce que le processus soit terminé.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint ProcessId {
            get {
                if ((curObj["ProcessId"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["ProcessId"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsQuotaNonPagedPoolUsageNull {
            get {
                if ((curObj["QuotaNonPagedPoolUsage"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété QuotaNonPagedPoolUsage indique la quantité de quota de l’utilisation" +
            " du pool non paginé pour le processus.\nExemple : 15")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint QuotaNonPagedPoolUsage {
            get {
                if ((curObj["QuotaNonPagedPoolUsage"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["QuotaNonPagedPoolUsage"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsQuotaPagedPoolUsageNull {
            get {
                if ((curObj["QuotaPagedPoolUsage"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété QuotaPagedPoolUsage indique la quantité quota d’utilisation du pool " +
            "paginé pour le processus.\nExemple : 22")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint QuotaPagedPoolUsage {
            get {
                if ((curObj["QuotaPagedPoolUsage"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["QuotaPagedPoolUsage"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsQuotaPeakNonPagedPoolUsageNull {
            get {
                if ((curObj["QuotaPeakNonPagedPoolUsage"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété QuotaPeakNonPagedPoolUsage indique la quantité quota pic d’utilisati" +
            "on du pool non paginé pour le processus.\nExemple : 31")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint QuotaPeakNonPagedPoolUsage {
            get {
                if ((curObj["QuotaPeakNonPagedPoolUsage"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["QuotaPeakNonPagedPoolUsage"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsQuotaPeakPagedPoolUsageNull {
            get {
                if ((curObj["QuotaPeakPagedPoolUsage"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété QuotaPeakPagedPoolUsage indique la quantité de pic quota d’utilisati" +
            "on du pool paginé pour le processus.\n Exemple : 31")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint QuotaPeakPagedPoolUsage {
            get {
                if ((curObj["QuotaPeakPagedPoolUsage"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["QuotaPeakPagedPoolUsage"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsReadOperationCountNull {
            get {
                if ((curObj["ReadOperationCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété ReadOperationCount spécifie le nombre d’opérations de lecture effect" +
            "uées.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong ReadOperationCount {
            get {
                if ((curObj["ReadOperationCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["ReadOperationCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsReadTransferCountNull {
            get {
                if ((curObj["ReadTransferCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété ReadTransferCount spécifie la quantité de données lues.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong ReadTransferCount {
            get {
                if ((curObj["ReadTransferCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["ReadTransferCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSessionIdNull {
            get {
                if ((curObj["SessionId"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété SessionId spécifie l’identificateur unique qui est généré par le sys" +
            "tème d’exploitation lorsque la session est créée. Une session s\'étale sur une pé" +
            "riode comprise entre l’ouverture et la fermeture de session sur un système parti" +
            "culier.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint SessionId {
            get {
                if ((curObj["SessionId"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["SessionId"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété Statut est une chaîne qui indique le statut actuel de l’objet. Plusieurs statuts opérationnels ou non peuvent être définis. Les statuts opérationnels sont ""OK"", ""Détérioré"" et ""Échec prévu"". ""Échec prévu"" indique qu’un élément fonctionne correctement mais qu’une défaillance est prévue dans un futur proche. Il peut s’agir, par exemple, d’un disque dur activé pour SMART. Des statuts non opérationnels peuvent également être spécifiés. Il s’agit de ""Erreur"", ""Démarrage"", ""Arrêt"" et ""Service"". Le dernier, ""Service"", peut s’appliquer lors de la recréation d’un disque en miroir, le rechargement d’une liste d’autorisations d’utilisateur, ou d’autres tâches administratives. Toutes ces tâches ne sont pas effectuées en ligne, pourtant l’élément géré n’est ni ""OK"", ni aucun des autres statuts.")]
        public string Status {
            get {
                return ((string)(curObj["Status"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsTerminationDateNull {
            get {
                if ((curObj["TerminationDate"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Heure a laquelle le processus a été arrêté ou s’est terminé.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public System.DateTime TerminationDate {
            get {
                if ((curObj["TerminationDate"] != null)) {
                    return ToDateTime(((string)(curObj["TerminationDate"])));
                }
                else {
                    return System.DateTime.MinValue;
                }
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsThreadCountNull {
            get {
                if ((curObj["ThreadCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété ThreadCount spécifie le nombre de threads actifs dans ce processus. Une instruction est l’unité de base d’exécution dans un processeur, et un thread est l’objet qui exécute les instructions. Chaque processus en cours d’exécution a au moins un thread. Cette propriété ne concerne que les ordinateurs exécutant Windows NT.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public uint ThreadCount {
            get {
                if ((curObj["ThreadCount"] == null)) {
                    return System.Convert.ToUInt32(0);
                }
                return ((uint)(curObj["ThreadCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsUserModeTimeNull {
            get {
                if ((curObj["UserModeTime"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("Durée dans le mode utilisateur, en nombre de fois 100 nanosecondes. Si cette info" +
            "rmation n’est pas disponible, la valeur 0 doit être utilisée.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong UserModeTime {
            get {
                if ((curObj["UserModeTime"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["UserModeTime"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsVirtualSizeNull {
            get {
                if ((curObj["VirtualSize"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"La propriété VirtualSize spécifie la taille actuelle en octets de l’espace d’adresse virtuelle que le processus utilise. L'utilisation de l’espace d’adresse virtuelle n’implique pas nécessairement l’utilisation correspondante du disque ou des pages de mémoire principales. L'espace virtuel est fini, et en en utilisant trop, le processus peut limiter sa possibilité de charger des bibliothèques.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong VirtualSize {
            get {
                if ((curObj["VirtualSize"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["VirtualSize"]));
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété WindowsVersion indique la version de Windows sur laquelle le process" +
            "us est en cours d’exécution.\nExemple : 4.0")]
        public string WindowsVersion {
            get {
                return ((string)(curObj["WindowsVersion"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsWorkingSetSizeNull {
            get {
                if ((curObj["WorkingSetSize"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description(@"Le montant de mémoire en octets dont un processus a besoin pour s’exécuter efficacement, pour un système d’exploitation qui utilise une gestion de la mémoire basée sur une page. Si une quantité insuffisante de mémoire est disponible (< taille du jeux de travail), il se produira un vidage. Si cette information n’est pas connue, NULL ou 0 doivent être entrés. Si ces données sont fournies, elles peuvent être suivies pour comprendre un processus de changement des spécifications de mémoire au fur et à mesure de l’exécution.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong WorkingSetSize {
            get {
                if ((curObj["WorkingSetSize"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["WorkingSetSize"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsWriteOperationCountNull {
            get {
                if ((curObj["WriteOperationCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété WriteOperationCount spécifie le nombre d’opérations d’écriture effec" +
            "tuées.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong WriteOperationCount {
            get {
                if ((curObj["WriteOperationCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["WriteOperationCount"]));
            }
        }
        
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsWriteTransferCountNull {
            get {
                if ((curObj["WriteTransferCount"] == null)) {
                    return true;
                }
                else {
                    return false;
                }
            }
        }
        
        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        [Description("La propriété WriteTransferCount spécifie la quantité de données écrites.")]
        [TypeConverter(typeof(WMIValueTypeConverter))]
        public ulong WriteTransferCount {
            get {
                if ((curObj["WriteTransferCount"] == null)) {
                    return System.Convert.ToUInt64(0);
                }
                return ((ulong)(curObj["WriteTransferCount"]));
            }
        }
        
        private bool CheckIfProperClass(System.Management.ManagementScope mgmtScope, System.Management.ManagementPath path, System.Management.ObjectGetOptions OptionsParam) {
            if (((path != null) 
                        && (string.Compare(path.ClassName, this.ManagementClassName, true, System.Globalization.CultureInfo.InvariantCulture) == 0))) {
                return true;
            }
            else {
                return CheckIfProperClass(new System.Management.ManagementObject(mgmtScope, path, OptionsParam));
            }
        }
        
        private bool CheckIfProperClass(System.Management.ManagementBaseObject theObj) {
            if (((theObj != null) 
                        && (string.Compare(((string)(theObj["__CLASS"])), this.ManagementClassName, true, System.Globalization.CultureInfo.InvariantCulture) == 0))) {
                return true;
            }
            else {
                System.Array parentClasses = ((System.Array)(theObj["__DERIVATION"]));
                if ((parentClasses != null)) {
                    int count = 0;
                    for (count = 0; (count < parentClasses.Length); count = (count + 1)) {
                        if ((string.Compare(((string)(parentClasses.GetValue(count))), this.ManagementClassName, true, System.Globalization.CultureInfo.InvariantCulture) == 0)) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        
        // Convertit un datetime au format DMTF en objet System.DateTime.
        static System.DateTime ToDateTime(string dmtfDate) {
            System.DateTime initializer = System.DateTime.MinValue;
            int year = initializer.Year;
            int month = initializer.Month;
            int day = initializer.Day;
            int hour = initializer.Hour;
            int minute = initializer.Minute;
            int second = initializer.Second;
            long ticks = 0;
            string dmtf = dmtfDate;
            System.DateTime datetime = System.DateTime.MinValue;
            string tempString = string.Empty;
            if ((dmtf == null)) {
                throw new System.ArgumentOutOfRangeException();
            }
            if ((dmtf.Length == 0)) {
                throw new System.ArgumentOutOfRangeException();
            }
            if ((dmtf.Length != 25)) {
                throw new System.ArgumentOutOfRangeException();
            }
            try {
                tempString = dmtf.Substring(0, 4);
                if (("****" != tempString)) {
                    year = int.Parse(tempString);
                }
                tempString = dmtf.Substring(4, 2);
                if (("**" != tempString)) {
                    month = int.Parse(tempString);
                }
                tempString = dmtf.Substring(6, 2);
                if (("**" != tempString)) {
                    day = int.Parse(tempString);
                }
                tempString = dmtf.Substring(8, 2);
                if (("**" != tempString)) {
                    hour = int.Parse(tempString);
                }
                tempString = dmtf.Substring(10, 2);
                if (("**" != tempString)) {
                    minute = int.Parse(tempString);
                }
                tempString = dmtf.Substring(12, 2);
                if (("**" != tempString)) {
                    second = int.Parse(tempString);
                }
                tempString = dmtf.Substring(15, 6);
                if (("******" != tempString)) {
                    ticks = (long.Parse(tempString) * ((long)((System.TimeSpan.TicksPerMillisecond / 1000))));
                }
                if (((((((((year < 0) 
                            || (month < 0)) 
                            || (day < 0)) 
                            || (hour < 0)) 
                            || (minute < 0)) 
                            || (minute < 0)) 
                            || (second < 0)) 
                            || (ticks < 0))) {
                    throw new System.ArgumentOutOfRangeException();
                }
            }
            catch (System.Exception e) {
                throw new System.ArgumentOutOfRangeException(null, e.Message);
            }
            datetime = new System.DateTime(year, month, day, hour, minute, second, 0);
            datetime = datetime.AddTicks(ticks);
            System.TimeSpan tickOffset = System.TimeZone.CurrentTimeZone.GetUtcOffset(datetime);
            int UTCOffset = 0;
            int OffsetToBeAdjusted = 0;
            long OffsetMins = ((long)((tickOffset.Ticks / System.TimeSpan.TicksPerMinute)));
            tempString = dmtf.Substring(22, 3);
            if ((tempString != "******")) {
                tempString = dmtf.Substring(21, 4);
                try {
                    UTCOffset = int.Parse(tempString);
                }
                catch (System.Exception e) {
                    throw new System.ArgumentOutOfRangeException(null, e.Message);
                }
                OffsetToBeAdjusted = ((int)((OffsetMins - UTCOffset)));
                datetime = datetime.AddMinutes(((double)(OffsetToBeAdjusted)));
            }
            return datetime;
        }
        
        // Convertit un objet System.DateTime au format datetime DMTF.
        static string ToDmtfDateTime(System.DateTime date) {
            string utcString = string.Empty;
            System.TimeSpan tickOffset = System.TimeZone.CurrentTimeZone.GetUtcOffset(date);
            long OffsetMins = ((long)((tickOffset.Ticks / System.TimeSpan.TicksPerMinute)));
            if ((System.Math.Abs(OffsetMins) > 999)) {
                date = date.ToUniversalTime();
                utcString = "+000";
            }
            else {
                if ((tickOffset.Ticks >= 0)) {
                    utcString = string.Concat("+", ((System.Int64 )((tickOffset.Ticks / System.TimeSpan.TicksPerMinute))).ToString().PadLeft(3, '0'));
                }
                else {
                    string strTemp = ((System.Int64 )(OffsetMins)).ToString();
                    utcString = string.Concat("-", strTemp.Substring(1, (strTemp.Length - 1)).PadLeft(3, '0'));
                }
            }
            string dmtfDateTime = ((System.Int32 )(date.Year)).ToString().PadLeft(4, '0');
            dmtfDateTime = string.Concat(dmtfDateTime, ((System.Int32 )(date.Month)).ToString().PadLeft(2, '0'));
            dmtfDateTime = string.Concat(dmtfDateTime, ((System.Int32 )(date.Day)).ToString().PadLeft(2, '0'));
            dmtfDateTime = string.Concat(dmtfDateTime, ((System.Int32 )(date.Hour)).ToString().PadLeft(2, '0'));
            dmtfDateTime = string.Concat(dmtfDateTime, ((System.Int32 )(date.Minute)).ToString().PadLeft(2, '0'));
            dmtfDateTime = string.Concat(dmtfDateTime, ((System.Int32 )(date.Second)).ToString().PadLeft(2, '0'));
            dmtfDateTime = string.Concat(dmtfDateTime, ".");
            System.DateTime dtTemp = new System.DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0);
            long microsec = ((long)((((date.Ticks - dtTemp.Ticks) 
                        * 1000) 
                        / System.TimeSpan.TicksPerMillisecond)));
            string strMicrosec = ((System.Int64 )(microsec)).ToString();
            if ((strMicrosec.Length > 6)) {
                strMicrosec = strMicrosec.Substring(0, 6);
            }
            dmtfDateTime = string.Concat(dmtfDateTime, strMicrosec.PadLeft(6, '0'));
            dmtfDateTime = string.Concat(dmtfDateTime, utcString);
            return dmtfDateTime;
        }
        
        private bool ShouldSerializeCreationDate() {
            if ((this.IsCreationDateNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeExecutionState() {
            if ((this.IsExecutionStateNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeHandleCount() {
            if ((this.IsHandleCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeInstallDate() {
            if ((this.IsInstallDateNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeKernelModeTime() {
            if ((this.IsKernelModeTimeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeMaximumWorkingSetSize() {
            if ((this.IsMaximumWorkingSetSizeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeMinimumWorkingSetSize() {
            if ((this.IsMinimumWorkingSetSizeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeOtherOperationCount() {
            if ((this.IsOtherOperationCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeOtherTransferCount() {
            if ((this.IsOtherTransferCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePageFaults() {
            if ((this.IsPageFaultsNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePageFileUsage() {
            if ((this.IsPageFileUsageNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeParentProcessId() {
            if ((this.IsParentProcessIdNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePeakPageFileUsage() {
            if ((this.IsPeakPageFileUsageNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePeakVirtualSize() {
            if ((this.IsPeakVirtualSizeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePeakWorkingSetSize() {
            if ((this.IsPeakWorkingSetSizeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePriority() {
            if ((this.IsPriorityNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializePrivatePageCount() {
            if ((this.IsPrivatePageCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeProcessId() {
            if ((this.IsProcessIdNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeQuotaNonPagedPoolUsage() {
            if ((this.IsQuotaNonPagedPoolUsageNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeQuotaPagedPoolUsage() {
            if ((this.IsQuotaPagedPoolUsageNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeQuotaPeakNonPagedPoolUsage() {
            if ((this.IsQuotaPeakNonPagedPoolUsageNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeQuotaPeakPagedPoolUsage() {
            if ((this.IsQuotaPeakPagedPoolUsageNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeReadOperationCount() {
            if ((this.IsReadOperationCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeReadTransferCount() {
            if ((this.IsReadTransferCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeSessionId() {
            if ((this.IsSessionIdNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeTerminationDate() {
            if ((this.IsTerminationDateNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeThreadCount() {
            if ((this.IsThreadCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeUserModeTime() {
            if ((this.IsUserModeTimeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeVirtualSize() {
            if ((this.IsVirtualSizeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeWorkingSetSize() {
            if ((this.IsWorkingSetSizeNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeWriteOperationCount() {
            if ((this.IsWriteOperationCountNull == false)) {
                return true;
            }
            return false;
        }
        
        private bool ShouldSerializeWriteTransferCount() {
            if ((this.IsWriteTransferCountNull == false)) {
                return true;
            }
            return false;
        }
        
        [Browsable(true)]
        public void CommitObject() {
            if ((isEmbedded == false)) {
                PrivateLateBoundObject.Put();
            }
        }
        
        [Browsable(true)]
        public void CommitObject(System.Management.PutOptions putOptions) {
            if ((isEmbedded == false)) {
                PrivateLateBoundObject.Put(putOptions);
            }
        }
        
        private void Initialize() {
            AutoCommitProp = true;
            isEmbedded = false;
        }
        
        private static string ConstructPath(string keyHandle) {
            string strPath = "root\\cimv2:Win32_Process";
            strPath = string.Concat(strPath, string.Concat(".Handle=", string.Concat("\"", string.Concat(keyHandle, "\""))));
            return strPath;
        }
        
        private void InitializeObject(System.Management.ManagementScope mgmtScope, System.Management.ManagementPath path, System.Management.ObjectGetOptions getOptions) {
            Initialize();
            if ((path != null)) {
                if ((CheckIfProperClass(mgmtScope, path, getOptions) != true)) {
                    throw new System.ArgumentException("Le nom de classe ne correspond pas.");
                }
            }
            PrivateLateBoundObject = new System.Management.ManagementObject(mgmtScope, path, getOptions);
            PrivateSystemProperties = new ManagementSystemProperties(PrivateLateBoundObject);
            curObj = PrivateLateBoundObject;
        }
        
        // Des surcharges différentes de GetInstances() aident à énumérer les instances de la classe WMI.
        public static ProcessCollection GetInstances() {
            return GetInstances(null, null, null);
        }
        
        public static ProcessCollection GetInstances(string condition) {
            return GetInstances(null, condition, null);
        }
        
        public static ProcessCollection GetInstances(System.String [] selectedProperties) {
            return GetInstances(null, null, selectedProperties);
        }
        
        public static ProcessCollection GetInstances(string condition, System.String [] selectedProperties) {
            return GetInstances(null, condition, selectedProperties);
        }
        
        public static ProcessCollection GetInstances(System.Management.ManagementScope mgmtScope, System.Management.EnumerationOptions enumOptions) {
            if ((mgmtScope == null)) {
                if ((statMgmtScope == null)) {
                    mgmtScope = new System.Management.ManagementScope();
                    mgmtScope.Path.NamespacePath = "root\\cimv2";
                }
                else {
                    mgmtScope = statMgmtScope;
                }
            }
            System.Management.ManagementPath pathObj = new System.Management.ManagementPath();
            pathObj.ClassName = "Win32_Process";
            pathObj.NamespacePath = "root\\cimv2";
            System.Management.ManagementClass clsObject = new System.Management.ManagementClass(mgmtScope, pathObj, null);
            if ((enumOptions == null)) {
                enumOptions = new System.Management.EnumerationOptions();
                enumOptions.EnsureLocatable = true;
            }
            return new ProcessCollection(clsObject.GetInstances(enumOptions));
        }
        
        public static ProcessCollection GetInstances(System.Management.ManagementScope mgmtScope, string condition) {
            return GetInstances(mgmtScope, condition, null);
        }
        
        public static ProcessCollection GetInstances(System.Management.ManagementScope mgmtScope, System.String [] selectedProperties) {
            return GetInstances(mgmtScope, null, selectedProperties);
        }
        
        public static ProcessCollection GetInstances(System.Management.ManagementScope mgmtScope, string condition, System.String [] selectedProperties) {
            if ((mgmtScope == null)) {
                if ((statMgmtScope == null)) {
                    mgmtScope = new System.Management.ManagementScope();
                    mgmtScope.Path.NamespacePath = "root\\cimv2";
                }
                else {
                    mgmtScope = statMgmtScope;
                }
            }
            System.Management.ManagementObjectSearcher ObjectSearcher = new System.Management.ManagementObjectSearcher(mgmtScope, new SelectQuery("Win32_Process", condition, selectedProperties));
            System.Management.EnumerationOptions enumOptions = new System.Management.EnumerationOptions();
            enumOptions.EnsureLocatable = true;
            ObjectSearcher.Options = enumOptions;
            return new ProcessCollection(ObjectSearcher.Get());
        }
        
        [Browsable(true)]
        public static WIN32_Process CreateInstance()
        {
            System.Management.ManagementScope mgmtScope = null;
            if ((statMgmtScope == null)) {
                mgmtScope = new System.Management.ManagementScope();
                mgmtScope.Path.NamespacePath = CreatedWmiNamespace;
            }
            else {
                mgmtScope = statMgmtScope;
            }
            System.Management.ManagementPath mgmtPath = new System.Management.ManagementPath(CreatedClassName);
            System.Management.ManagementClass tmpMgmtClass = new System.Management.ManagementClass(mgmtScope, mgmtPath, null);
            return new WIN32_Process(tmpMgmtClass.CreateInstance());
        }
        
        [Browsable(true)]
        public void Delete() {
            PrivateLateBoundObject.Delete();
        }
        
        public uint AttachDebugger() {
            if ((isEmbedded == false)) {
                System.Management.ManagementBaseObject inParams = null;
                System.Management.ManagementBaseObject outParams = PrivateLateBoundObject.InvokeMethod("AttachDebugger", inParams, null);
                return System.Convert.ToUInt32(outParams.Properties["ReturnValue"].Value);
            }
            else {
                return System.Convert.ToUInt32(0);
            }
        }
        
        public static uint Create(string CommandLine, string CurrentDirectory, System.Management.ManagementBaseObject ProcessStartupInformation, out uint ProcessId) {
            bool IsMethodStatic = true;
            if ((IsMethodStatic == true)) {
                System.Management.ManagementBaseObject inParams = null;
                System.Management.ManagementPath mgmtPath = new System.Management.ManagementPath(CreatedClassName);
                System.Management.ManagementClass classObj = new System.Management.ManagementClass(statMgmtScope, mgmtPath, null);
                bool EnablePrivileges = classObj.Scope.Options.EnablePrivileges;
                classObj.Scope.Options.EnablePrivileges = true;
                inParams = classObj.GetMethodParameters("Create");
                inParams["CommandLine"] = ((System.String )(CommandLine));
                inParams["CurrentDirectory"] = ((System.String )(CurrentDirectory));
                inParams["ProcessStartupInformation"] = ((System.Management.ManagementBaseObject )(ProcessStartupInformation));
                System.Management.ManagementBaseObject outParams = classObj.InvokeMethod("Create", inParams, null);
                ProcessId = System.Convert.ToUInt32(outParams.Properties["ProcessId"].Value);
                classObj.Scope.Options.EnablePrivileges = EnablePrivileges;
                return System.Convert.ToUInt32(outParams.Properties["ReturnValue"].Value);
            }
            else {
                ProcessId = System.Convert.ToUInt32(0);
                return System.Convert.ToUInt32(0);
            }
        }
        
        public uint GetOwner(out string Domain, out string User) {
            if ((isEmbedded == false)) {
                System.Management.ManagementBaseObject inParams = null;
                bool EnablePrivileges = PrivateLateBoundObject.Scope.Options.EnablePrivileges;
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = true;
                System.Management.ManagementBaseObject outParams = PrivateLateBoundObject.InvokeMethod("GetOwner", inParams, null);
                Domain = System.Convert.ToString(outParams.Properties["Domain"].Value);
                User = System.Convert.ToString(outParams.Properties["User"].Value);
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = EnablePrivileges;
                return System.Convert.ToUInt32(outParams.Properties["ReturnValue"].Value);
            }
            else {
                Domain = null;
                User = null;
                return System.Convert.ToUInt32(0);
            }
        }
        
        public uint GetOwnerSid(out string Sid) {
            if ((isEmbedded == false)) {
                System.Management.ManagementBaseObject inParams = null;
                bool EnablePrivileges = PrivateLateBoundObject.Scope.Options.EnablePrivileges;
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = true;
                System.Management.ManagementBaseObject outParams = PrivateLateBoundObject.InvokeMethod("GetOwnerSid", inParams, null);
                Sid = System.Convert.ToString(outParams.Properties["Sid"].Value);
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = EnablePrivileges;
                return System.Convert.ToUInt32(outParams.Properties["ReturnValue"].Value);
            }
            else {
                Sid = null;
                return System.Convert.ToUInt32(0);
            }
        }
        
        public uint SetPriority(int Priority) {
            if ((isEmbedded == false)) {
                System.Management.ManagementBaseObject inParams = null;
                bool EnablePrivileges = PrivateLateBoundObject.Scope.Options.EnablePrivileges;
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = true;
                inParams = PrivateLateBoundObject.GetMethodParameters("SetPriority");
                inParams["Priority"] = ((System.Int32 )(Priority));
                System.Management.ManagementBaseObject outParams = PrivateLateBoundObject.InvokeMethod("SetPriority", inParams, null);
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = EnablePrivileges;
                return System.Convert.ToUInt32(outParams.Properties["ReturnValue"].Value);
            }
            else {
                return System.Convert.ToUInt32(0);
            }
        }
        
        public uint Terminate(uint Reason) {
            if ((isEmbedded == false)) {
                System.Management.ManagementBaseObject inParams = null;
                bool EnablePrivileges = PrivateLateBoundObject.Scope.Options.EnablePrivileges;
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = true;
                inParams = PrivateLateBoundObject.GetMethodParameters("Terminate");
                inParams["Reason"] = ((System.UInt32 )(Reason));
                System.Management.ManagementBaseObject outParams = PrivateLateBoundObject.InvokeMethod("Terminate", inParams, null);
                PrivateLateBoundObject.Scope.Options.EnablePrivileges = EnablePrivileges;
                return System.Convert.ToUInt32(outParams.Properties["ReturnValue"].Value);
            }
            else {
                return System.Convert.ToUInt32(0);
            }
        }
        
        public enum ExecutionStateValues {
            
            Inconnu = 0,
            
            Autre = 1,
            
            Prêt = 2,
            
            En_cours_d_exécution = 3,
            
            Bloqué = 4,
            
            En_pause_bloqué = 5,
            
            En_pause_prêt = 6,
            
            Terminé = 7,
            
            Arrêté = 8,
            
            Agrandissement = 9,
            
            NULL_ENUM_VALUE = 10,
        }
        
        // Implémentation de l'énumérateur pour l'énumération d'instances de la classe.
        public class ProcessCollection : object, ICollection {
            
            private ManagementObjectCollection privColObj;
            
            public ProcessCollection(ManagementObjectCollection objCollection) {
                privColObj = objCollection;
            }
            
            public virtual int Count {
                get {
                    return privColObj.Count;
                }
            }
            
            public virtual bool IsSynchronized {
                get {
                    return privColObj.IsSynchronized;
                }
            }
            
            public virtual object SyncRoot {
                get {
                    return this;
                }
            }
            
            public virtual void CopyTo(System.Array array, int index) {
                privColObj.CopyTo(array, index);
                int nCtr;
                for (nCtr = 0; (nCtr < array.Length); nCtr = (nCtr + 1)) {
                    array.SetValue(new WIN32_Process(((System.Management.ManagementObject)(array.GetValue(nCtr)))), nCtr);
                }
            }
            
            public virtual System.Collections.IEnumerator GetEnumerator() {
                return new ProcessEnumerator(privColObj.GetEnumerator());
            }
            
            public class ProcessEnumerator : object, System.Collections.IEnumerator {
                
                private ManagementObjectCollection.ManagementObjectEnumerator privObjEnum;
                
                public ProcessEnumerator(ManagementObjectCollection.ManagementObjectEnumerator objEnum) {
                    privObjEnum = objEnum;
                }
                
                public virtual object Current {
                    get {
                        return new WIN32_Process(((System.Management.ManagementObject)(privObjEnum.Current)));
                    }
                }
                
                public virtual bool MoveNext() {
                    return privObjEnum.MoveNext();
                }
                
                public virtual void Reset() {
                    privObjEnum.Reset();
                }
            }
        }
        
        // TypeConverter pour gérer les valeurs null pour les propriétés ValueType
        public class WMIValueTypeConverter : TypeConverter {
            
            private TypeConverter baseConverter;
            
            private System.Type baseType;
            
            public WMIValueTypeConverter(System.Type inBaseType) {
                baseConverter = TypeDescriptor.GetConverter(inBaseType);
                baseType = inBaseType;
            }
            
            public override bool CanConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Type srcType) {
                return baseConverter.CanConvertFrom(context, srcType);
            }
            
            public override bool CanConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Type destinationType) {
                return baseConverter.CanConvertTo(context, destinationType);
            }
            
            public override object ConvertFrom(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value) {
                return baseConverter.ConvertFrom(context, culture, value);
            }
            
            public override object CreateInstance(System.ComponentModel.ITypeDescriptorContext context, System.Collections.IDictionary dictionary) {
                return baseConverter.CreateInstance(context, dictionary);
            }
            
            public override bool GetCreateInstanceSupported(System.ComponentModel.ITypeDescriptorContext context) {
                return baseConverter.GetCreateInstanceSupported(context);
            }
            
            public override PropertyDescriptorCollection GetProperties(System.ComponentModel.ITypeDescriptorContext context, object value, System.Attribute[] attributeVar) {
                return baseConverter.GetProperties(context, value, attributeVar);
            }
            
            public override bool GetPropertiesSupported(System.ComponentModel.ITypeDescriptorContext context) {
                return baseConverter.GetPropertiesSupported(context);
            }
            
            public override System.ComponentModel.TypeConverter.StandardValuesCollection GetStandardValues(System.ComponentModel.ITypeDescriptorContext context) {
                return baseConverter.GetStandardValues(context);
            }
            
            public override bool GetStandardValuesExclusive(System.ComponentModel.ITypeDescriptorContext context) {
                return baseConverter.GetStandardValuesExclusive(context);
            }
            
            public override bool GetStandardValuesSupported(System.ComponentModel.ITypeDescriptorContext context) {
                return baseConverter.GetStandardValuesSupported(context);
            }
            
            public override object ConvertTo(System.ComponentModel.ITypeDescriptorContext context, System.Globalization.CultureInfo culture, object value, System.Type destinationType) {
                if ((baseType.BaseType == typeof(System.Enum))) {
                    if ((value.GetType() == destinationType)) {
                        return value;
                    }
                    if ((((value == null) 
                                && (context != null)) 
                                && (context.PropertyDescriptor.ShouldSerializeValue(context.Instance) == false))) {
                        return  "NULL_ENUM_VALUE" ;
                    }
                    return baseConverter.ConvertTo(context, culture, value, destinationType);
                }
                if (((baseType == typeof(bool)) 
                            && (baseType.BaseType == typeof(System.ValueType)))) {
                    if ((((value == null) 
                                && (context != null)) 
                                && (context.PropertyDescriptor.ShouldSerializeValue(context.Instance) == false))) {
                        return "";
                    }
                    return baseConverter.ConvertTo(context, culture, value, destinationType);
                }
                if (((context != null) 
                            && (context.PropertyDescriptor.ShouldSerializeValue(context.Instance) == false))) {
                    return "";
                }
                return baseConverter.ConvertTo(context, culture, value, destinationType);
            }
        }
        
        // La classe incorporée pour représenter les propriétés système WMI.
        [TypeConverter(typeof(System.ComponentModel.ExpandableObjectConverter))]
        public class ManagementSystemProperties {
            
            private System.Management.ManagementBaseObject PrivateLateBoundObject;
            
            public ManagementSystemProperties(System.Management.ManagementBaseObject ManagedObject) {
                PrivateLateBoundObject = ManagedObject;
            }
            
            [Browsable(true)]
            public int GENUS {
                get {
                    return ((int)(PrivateLateBoundObject["__GENUS"]));
                }
            }
            
            [Browsable(true)]
            public string CLASS {
                get {
                    return ((string)(PrivateLateBoundObject["__CLASS"]));
                }
            }
            
            [Browsable(true)]
            public string SUPERCLASS {
                get {
                    return ((string)(PrivateLateBoundObject["__SUPERCLASS"]));
                }
            }
            
            [Browsable(true)]
            public string DYNASTY {
                get {
                    return ((string)(PrivateLateBoundObject["__DYNASTY"]));
                }
            }
            
            [Browsable(true)]
            public string RELPATH {
                get {
                    return ((string)(PrivateLateBoundObject["__RELPATH"]));
                }
            }
            
            [Browsable(true)]
            public int PROPERTY_COUNT {
                get {
                    return ((int)(PrivateLateBoundObject["__PROPERTY_COUNT"]));
                }
            }
            
            [Browsable(true)]
            public string[] DERIVATION {
                get {
                    return ((string[])(PrivateLateBoundObject["__DERIVATION"]));
                }
            }
            
            [Browsable(true)]
            public string SERVER {
                get {
                    return ((string)(PrivateLateBoundObject["__SERVER"]));
                }
            }
            
            [Browsable(true)]
            public string NAMESPACE {
                get {
                    return ((string)(PrivateLateBoundObject["__NAMESPACE"]));
                }
            }
            
            [Browsable(true)]
            public string PATH {
                get {
                    return ((string)(PrivateLateBoundObject["__PATH"]));
                }
            }
        }
    }
}
