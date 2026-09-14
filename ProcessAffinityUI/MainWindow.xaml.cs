using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using ProcessAffinityUI.Threading;
using System.Threading.Tasks;

using System.Configuration;

namespace ProcessAffinityUI
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    
    public partial class MainWindow : Window
    {

        private System.Windows.Forms.NotifyIcon _processAffinityNotifyIcon;

        private Processes _processes = null;
        private Processes _services = null;

        public MainWindow()
        {
                InitializeComponent();

                this._processAffinityNotifyIcon = new System.Windows.Forms.NotifyIcon();
                this._processAffinityNotifyIcon.Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("/ProcessAffinity.ico", UriKind.RelativeOrAbsolute)).Stream);


                _processAffinityNotifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(ProcessAffinityNotifyIconMouseDoubleClick);

                this.StateChanged += new EventHandler(WindowStateChanged);
                this.Closed += new EventHandler(WindowClosed);

                AttachCounterToolTip(this.ProcessControlsCountLabel);
                AttachCounterToolTip(this.ProcessesCountLabel);

                processWrapPanel.MouseRightButtonDown += ProcessWrapPanel_MouseRightButtonDown;

        }

        private void InitializeProcessWrapPanel()
        {
            if (this._processes == null)
            {
                this._processes = new Processes();
            }

            this.InitializeProcessWrapPanel(this._processes);
        }

        private void InitializeProcessWrapPanel(Processes processes)
        {
            

            ClearProcessWrapPanel();
           
            ProcessUserControl processUserControl = null;

            // Copie prise sous verrou : le diff retire des entrées depuis le
            // thread d'échantillonnage, et indexer la liste d'origine pouvait
            // sauter une tuile ou sortir des bornes en cours de construction.
            Process[] entries = processes.Snapshot();

            for(int i = 0;i < entries.Length; i++)
            {
                try
                {

                    if (entries[i] != null)
                    {
                        if (entries[i].IsService && this.ShowServicesCheckBox.IsChecked != true)
                        {
                            continue;
                        }

                        processUserControl = new ProcessUserControl(entries[i]);
                        bool processUserControlExists = ProcessUserControlExists(entries[i]);

                        if (processUserControlExists == false)
                        {
                            this.processWrapPanel.Children.Add(processUserControl);

                            SetProcessUserControlVisibility(processUserControl);
                        }
                        else
                        {
                            GetProcessUserControl(entries[i]).SetProcess(entries[i]);
                        }
                    }
                }
                catch
                {
                    //this.processWrapPanel.Children.Add(new ProcessUserControl(new Process(this.ComputerNameLabel.Content.ToString(), 0, processes[i] == null?"Not exists":processes[i].ProcessName)));
                }
            }

            SetCounters();
        }

        /// <summary>
        /// Renseigne sur chaque service le nom de son processus hôte, et — dès
        /// qu'un hôte en porte plusieurs — la liste des services concernés, sur
        /// l'hôte comme sur chacun d'eux.
        /// </summary>
        private void ResolveServiceHosts(Processes processes, Processes services)
        {
            Dictionary<int, string> hostNameByProcessID = new Dictionary<int, string>();

            foreach (Process process in processes.Snapshot())
            {
                if (!process.IsService && !hostNameByProcessID.ContainsKey(process.ProcessID))
                {
                    hostNameByProcessID[process.ProcessID] = process.ProcessName;
                }
            }

            Dictionary<int, List<string>> serviceNamesByProcessID = new Dictionary<int, List<string>>();

            foreach (Process service in services.Snapshot())
            {
                List<string> serviceNames;

                if (!serviceNamesByProcessID.TryGetValue(service.ProcessID, out serviceNames))
                {
                    serviceNames = new List<string>();
                    serviceNamesByProcessID[service.ProcessID] = serviceNames;
                }

                serviceNames.Add(service.ProcessName);
            }

            foreach (Process service in services.Snapshot())
            {
                string hostName;

                if (hostNameByProcessID.TryGetValue(service.ProcessID, out hostName))
                {
                    service.HostProcessName = hostName;
                }

                List<string> serviceNames = serviceNamesByProcessID[service.ProcessID];

                if (serviceNames.Count > 1)
                {
                    service.SharedServiceNames = serviceNames;
                }
            }

            foreach (Process process in processes.Snapshot())
            {
                List<string> serviceNames;

                if (!process.IsService
                    && serviceNamesByProcessID.TryGetValue(process.ProcessID, out serviceNames)
                    && serviceNames.Count > 1)
                {
                    process.SharedServiceNames = serviceNames;
                }
            }
        }

        private void SubscribeProcessEventHandlers()
        {
            this.SubscribeProcessEventHandlers(this._processes);
        }

        /// <summary>
        /// Abonnement inconditionnel : on détache puis on rattache. Le retrait ne
        /// sert qu'à écarter un double abonnement — il ne doit jamais empêcher le
        /// réabonnement. La garde précédente, qui ne rattachait que si le champ
        /// délégué était nul ou vide, faisait que le Unsubscribe/Subscribe de
        /// ClearProcessWrapPanel détachait sans jamais rattacher : passé la
        /// première reconstruction du panneau, plus aucun événement n'arrivait.
        /// </summary>
        private void SubscribeProcessEventHandlers(Processes processes)
        {
            if (processes == null)
            {
                SetEventsSubscriptionsIndicator();
                return;
            }

            processes.ProcessEventArrived -= Processes_ProcessEventArrived;
            processes.ProcessEventArrived += Processes_ProcessEventArrived;

            processes.ProcessCreated -= Processes_ProcessCreated;
            processes.ProcessCreated += Processes_ProcessCreated;

            processes.ProcessDeleted -= Processes_ProcessDeleted;
            processes.ProcessDeleted += Processes_ProcessDeleted;

            processes.ProcessModified -= Processes_ProcessModified;
            processes.ProcessModified += Processes_ProcessModified;

            SetEventsSubscriptionsIndicator();
        }

        /// <summary>
        /// Compte les abonnés réellement portés par l'instance courante, et non
        /// les méthodes référencées par les champs de la fenêtre : l'indicateur
        /// affichait 1-1-1-1 alors que l'instance n'en portait aucun. Il détecte
        /// désormais aussi bien la perte que le double abonnement.
        /// </summary>
        private void SetEventsSubscriptionsIndicator()
        {
            if (this._processes == null)
            {
                EventsSubscritionsLabel.Content = "Evt:- New:- Del:- Mod:-";
                return;
            }

            // Chaque nombre précédé de ce qu'il désigne : « 1-1-1-1 » n'était
            // lisible que pour qui connaissait l'ordre des quatre événements.
            int[] counts = this._processes.GetEventSubscriberCounts();

            EventsSubscritionsLabel.Content =
                "Evt:" + counts[0] + " New:" + counts[1] + " Del:" + counts[2] + " Mod:" + counts[3];
        }

        private void UnsubscribeProcessEventHandlers()
        {
            this.UnsubscribeProcessEventHandlers(this._processes);
        }

            private void UnsubscribeProcessEventHandlers(Processes processes)
        {
                if (processes == null)
                {
                    return;
                }

                processes.ProcessEventArrived -= Processes_ProcessEventArrived;
                processes.ProcessCreated -= Processes_ProcessCreated;
                processes.ProcessDeleted -= Processes_ProcessDeleted;
                processes.ProcessModified -= Processes_ProcessModified;

                SetEventsSubscriptionsIndicator();

        }

        private void ClearProcessWrapPanel()
        {
            // Retirer l'élément courant en incrémentant l'indice n'en vidait
            // qu'un sur deux, laissant des tuiles périmées dès le second
            // chargement.
            foreach (ProcessUserControl puc in processWrapPanel.Children.OfType<ProcessUserControl>().ToList())
            {
                puc.DataContext = null;
            }

            processWrapPanel.Children.Clear();

            this.SubscribeProcessEventHandlers();
        }

        private void InitializeCPUComboBox(Processes processes)
        {
            int numberOfProcessors = 0;

            if (
                    ComputerNameTextBox.Text.Trim() == ".")
            {
                numberOfProcessors = processes.GetProcessorsProperties().NumberOfLogicalProcessors;
            }
            else
            {
                numberOfProcessors = processes.GetProcessorsProperties().NumberOfProcessors;
            }

            // Détaché le temps de la reconstruction : l'abonnement était ajouté à
            // chaque chargement sans jamais être retiré, et le Items.Clear() du
            // chargement suivant le déclenchait alors qu'aucun élément n'est
            // sélectionné — la sélection valant null, l'initialisation échouait
            // et tout le reste du chargement était abandonné.
            CPUComboBox.SelectionChanged -= CPUComboBox_SelectionChanged;

            CPUComboBox.Items.Clear();
            for (int i = 0; i < numberOfProcessors; i++)
            {
                CPUComboBox.Items.Add(i.ToString());
            }
            CPUComboBox.Items.Add("ALL");
            CPUComboBox.SelectedIndex = CPUComboBox.Items.Count - 1;

            CPUComboBox.SelectionChanged += CPUComboBox_SelectionChanged;
        }

        private void CPUComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            int childrenCount = this.processWrapPanel.Children.Count;
            for (int i = 0; i < childrenCount; i++)
            {
                //if (this.processWrapPanel.Children[i].GetType() == typeof(ProcessUserControl))
                //{ 
                    SetProcessUserControlVisibility(((ProcessUserControl)this.processWrapPanel.Children[i]));
                //}
            }

            SetCounters();
        }

        /// <summary>
        /// Ventilation de l'écart entre les deux compteurs, au moment où on la
        /// consulte. Retourne null quand il n'y a rien de particulier à dire.
        /// </summary>
        private string BuildCounterBreakdown()
        {
            if (this._processes == null)
            {
                return null;
            }

            Process[] entries = this._processes.Snapshot();

            int total = entries.Length;
            int visible = GetVisibleProcessUserControlCount();

            if (total == visible)
            {
                return null;
            }

            int hiddenServices = this.ShowServicesCheckBox.IsChecked == true
                ? 0
                : entries.Count(entry => entry.IsService);

            int hiddenByCore = this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                   .Count(child => child.Visibility != Visibility.Visible);

            int unaccounted = total - hiddenServices - hiddenByCore - visible;

            StringBuilder builder = new StringBuilder();

            builder.Append(total).Append(total > 1 ? " entries in total." : " entry in total.");

            if (hiddenByCore > 0)
            {
                builder.Append("\r\n").Append(hiddenByCore)
                       .Append(" hidden by the CPU ")
                       .Append(this.CPUComboBox.SelectedValue).Append(" filter.");
            }

            if (hiddenServices > 0)
            {
                builder.Append("\r\n").Append(hiddenServices)
                       .Append(hiddenServices > 1 ? " services hidden." : " service hidden.");
            }

            if (unaccounted > 0)
            {
                builder.Append("\r\n").Append(unaccounted).Append(" not shown.");
            }

            builder.Append("\r\n").Append(visible).Append(" visible");

            // Affinité illisible : affichées quel que soit le cœur, faute de
            // savoir sur lesquels elles tournent. À ne pas imputer au filtre.
            int unreadableAffinity = hiddenByCore == 0
                ? 0
                : this.processWrapPanel.Children.OfType<ProcessUserControl>()
                      .Count(child => child.Visibility == Visibility.Visible
                                      && child.Process != null
                                      && child.Process.IsProcessorAffinityReadable == false);

            if (unreadableAffinity > 0)
            {
                builder.Append(", of which ").Append(unreadableAffinity)
                       .Append(" with unknown affinity");
            }

            builder.Append(".");

            return builder.ToString();
        }

        private void AttachCounterToolTip(FrameworkElement element)
        {
            ToolTip toolTip = new ToolTip();
            toolTip.Content = new TextBlock();
            toolTip.Opened += CounterToolTipOpened;

            element.ToolTip = toolTip;
            element.ToolTipOpening += CounterToolTipOpening;
        }

        private void CounterToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (BuildCounterBreakdown() == null)
            {
                // Les deux nombres coïncident : rien de particulier à signaler.
                e.Handled = true;
                return;
            }

            FrameworkElement element = sender as FrameworkElement;

            SetCounterToolTipText(element == null ? null : element.ToolTip as ToolTip);
        }

        private void CounterToolTipOpened(object sender, RoutedEventArgs e)
        {
            // ToolTipOpening n'est levé que par le survol via ToolTipService.
            SetCounterToolTipText(sender as ToolTip);
        }

        private void SetCounterToolTipText(ToolTip toolTip)
        {
            TextBlock textBlock = toolTip == null ? null : toolTip.Content as TextBlock;

            if (textBlock != null)
            {
                textBlock.Text = BuildCounterBreakdown() ?? string.Empty;
            }
        }

        private void SetProcessUserControlVisibility(ProcessUserControl processUserControl)
        {
            object selectedValue = CPUComboBox.SelectedValue;

            // Sélection nulle pendant une reconstruction de la liste : aucun
            // filtre ne s'applique.
            if (selectedValue == null || selectedValue.ToString() == "ALL")
            {
                processUserControl.Visibility = Visibility.Visible;
                return;
            }

            // Les cœurs occupent les indices 0 à N-1, « ALL » étant ajouté en
            // dernier : le numéro de cœur est l'indice lui-même.
            int coreNumber = CPUComboBox.SelectedIndex;

            nuint? processorAffinity = processUserControl.Process.GetProcessorAffinity();

            if (processorAffinity == null)
            {
                // Affinité illisible : on ne sait pas sur quels cœurs le
                // processus tourne. L'exclure du filtre reviendrait à affirmer
                // qu'il n'en utilise aucun.
                processUserControl.Visibility = Visibility.Visible;
                return;
            }

            // Test direct du bit. Passer par la chaîne de ToBinary inversait
            // l'ordre des cœurs : elle est de poids fort en tête, mais elle était
            // indexée par la gauche.
            bool runsOnSelectedCore =
                coreNumber >= 0
                && coreNumber < IntPtr.Size * 8
                && (processorAffinity.Value & ((nuint)1 << coreNumber)) != 0;

            processUserControl.Visibility = runsOnSelectedCore ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ProcessWrapPanel_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ContextMenu menu = new ContextMenu();

            MenuItem menuItem = new MenuItem();
            menuItem.Header = "Affinity all processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menuItem = new MenuItem();
            menuItem.Header = "Affinity selected processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menuItem = new MenuItem();
            menuItem.Header = "Priority all processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menuItem = new MenuItem();
            menuItem.Header = "Priority selected processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menuItem = new MenuItem();
            menuItem.Header = "Select all processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menuItem = new MenuItem();
            menuItem.Header = "Unselect all processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menuItem = new MenuItem();
            menuItem.Header = "Is alive all processes";
            ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

            this.ContextMenu = menu;
        }
        
        private delegate void ProcessesEventArrivedDelegate(Process process);

        private void ProcessUserControlContextMenuClick(object sender, RoutedEventArgs e)
        {
            switch (((MenuItem)e.OriginalSource).Header.ToString())
            {
                case "Affinity all processes":
                    ProcessAffinityWindow processAffinityWindow = new ProcessAffinityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().ToList());
                    processAffinityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;
                case "Priority all processes":
                    ProcessPriorityWindow processPriorityWindow = new ProcessPriorityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().ToList());
                    processPriorityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;
                case "Priority selected processes":
                    ProcessPriorityWindow selectedProcessPriorityWindow = new ProcessPriorityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().Where(puc => puc.IsSelected == true).ToList());
                    selectedProcessPriorityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;

                case "Affinity selected processes":
                    ProcessAffinityWindow selectedProcessAffinityWindow = new ProcessAffinityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().Where(puc => puc.IsSelected == true).ToList());
                    selectedProcessAffinityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;
                case "Select all processes":
                    SelectProcesses();
                    break;
                case "Unselect all processes":
                    UnselectProcesses();
                    break;
                case "Is alive all processes":
                    IsAliveProcesses();
                    break;

            }
        }

        private void SelectProcesses()
        {
            IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                                                  select child;

            foreach (ProcessUserControl processUserControl in processUserControls)
            {
                processUserControl.IsSelected = true;
            }
        }

        private void UnselectProcesses()
        {
            IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                                                  select child;

            foreach (ProcessUserControl processUserControl in processUserControls)
            {
                processUserControl.IsSelected = false;
            }
        }

        private void IsAliveProcesses()
        {
            Task.Factory.StartNew(() => {

                // Copie sous verrou : le diff mute désormais la liste depuis le
                // thread d'échantillonnage, et énumérer l'original lèverait.
                Parallel.ForEach(_processes.Snapshot(), (process) => {
                    process.IsAlive();
                });

            }, TaskCreationOptions.LongRunning);
        }

        private void Processes_ProcessEventArrived(object sender, ProcessEventArgs e)
        {

            ProcessesEventArrivedDelegate processesEventArrivedDelegate = new ProcessesEventArrivedDelegate(SetCounters);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesEventArrivedDelegate, new object[] { e.Process });

        }

        private void Processes_ProcessCreated(object sender, ProcessEventArgs e)
        {
            //this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() => this.processWrapPanel.Children.Add(new ProcessUserControl(process))), new object[] { });

            ProcessesEventArrivedDelegate processesCreatedDelegate = new ProcessesEventArrivedDelegate(CreateProcessUserControl);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesCreatedDelegate, new object[] {e.Process});

        }

        private void Processes_ProcessDeleted(object sender, ProcessEventArgs e)
        {
            ProcessesEventArrivedDelegate processesDeletedDelegate = new ProcessesEventArrivedDelegate(RemoveProcessUserControl);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesDeletedDelegate, new object[] {e.Process});

        }

        private void Processes_ProcessModified(object sender, ProcessEventArgs e)
        {
            ProcessesEventArrivedDelegate processesModifiedDelegate = new ProcessesEventArrivedDelegate(ModifyProcessUserControl);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesModifiedDelegate, new object[] {e.Process});
        }

        private void SetCounters(Process process)
        {
            SetCounters();
        }

        /// <summary>
        /// À gauche les tuiles réellement visibles — et non les tuiles créées,
        /// qui incluaient celles que le filtre par cœur replie. À droite les
        /// entrées de la liste.
        /// </summary>
        private void SetCounters()
        {
            this.ProcessControlsCountLabel.Content = "Displayed: " + GetVisibleProcessUserControlCount().ToString();
            this.ProcessesCountLabel.Content = "Total: " + (this._processes == null ? 0 : this._processes.Count).ToString();
        }

        private int GetVisibleProcessUserControlCount()
        {
            return this.processWrapPanel.Children.OfType<ProcessUserControl>()
                       .Count(child => child.Visibility == Visibility.Visible);
        }

        /// <summary>
        /// Affinité et priorité s'appliquent au processus hôte, donc à toutes les
        /// entrées de ce PID : sans ce rafraîchissement, les tuiles sœurs
        /// gardent une couleur de priorité et une visibilité périmées.
        /// </summary>
        public void RefreshProcessUserControls(int processID)
        {
            foreach (ProcessUserControl processUserControl in this.GetProcessUserControls(processID))
            {
                processUserControl.SetProcessAffinityColors();
                SetProcessUserControlVisibility(processUserControl);
            }

            SetCounters();
        }

        public void RefreshProcessUserControls()
        {
            foreach (ProcessUserControl processUserControl in this.processWrapPanel.Children.OfType<ProcessUserControl>().ToList())
            {
                processUserControl.SetProcessAffinityColors();
                SetProcessUserControlVisibility(processUserControl);
            }

            SetCounters();
        }

        private void CreateProcessUserControl(Process process)
        {
            if (process.ProcessID == 0)
            {
                // Processes.InitalizeWatcher exception
                this.processWrapPanel.Children.Add(new ProcessUserControl(process));
            }
            else
            {
                if (process.IsService && this.ShowServicesCheckBox.IsChecked != true)
                {
                    return;
                }

                if (process.ToKill)
                {
                    process.Kill();
                    RemoveProcessUserControl(process);
                }

                //IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                //                                                      where child.ProcessID == process.ProcessID
                //                                                      select child;

                //if (processUserControls != null && processUserControls.Count() == 0)
                //{
                    this.processWrapPanel.Children.Add(new ProcessUserControl(process));
                //}
            }
        }

        private void ModifyProcessUserControl(Process process)
        {
            if (process.ToKill)
            {
                process.Kill();
                RemoveProcessUserControl(process);
            }
            else
            {
                //IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                //                                                      where child.ProcessID == process.ProcessID
                //                                                      select child;

                //foreach (ProcessUserControl processUserControl in processUserControls)
                //{

                ProcessUserControl processUserControl = this.GetProcessUserControl(process);
                if (processUserControl != null)
                {
                    this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() => processUserControl.ProcessNameLabelBackground = Brushes.Yellow));

                    if (process.ExecutablePath != null && process.ExecutablePath != string.Empty)
                    {
                        this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() => { if (processUserControl.Icon == null) processUserControl.SetIcon(process); }));
                    }
                }
                //}
            }
        }

        private void RemoveProcessUserControl(Process process)
        {
            //IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
            //                                                      where child.ProcessID == process.ProcessID
            //                                                      select child;

            //foreach (ProcessUserControl processUserControl in processUserControls)
            //{

            // Une entrée de service porte le PID de son hôte : retirer tout le
            // PID effacerait l'hôte et les services frères pour l'arrêt d'un
            // seul service. La disparition d'un processus, elle, emporte bien
            // ses services, qui n'ont pas d'existence propre.
            List<ProcessUserControl> processUserControls;

            if (process.IsService)
            {
                ProcessUserControl serviceUserControl = this.GetProcessUserControl(process);

                processUserControls = serviceUserControl == null
                    ? new List<ProcessUserControl>()
                    : new List<ProcessUserControl> { serviceUserControl };
            }
            else
            {
                processUserControls = this.GetProcessUserControls(process.ProcessID);
            }

            foreach (ProcessUserControl processUserControl in processUserControls)
            {
                ProcessUserControl toRemove = processUserControl;

                this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.processWrapPanel.Children.Remove(toRemove);
                    SetCounters();
                }));
            }
            //}
        }

        private bool ProcessUserControlExists(Process process)
        {
            return this.GetProcessUserControl(process) != null;
        }

        /// <summary>
        /// Tuile portant la même entrée : type, PID et nom. Sur le seul PID, une
        /// tuile de service écrasait celle de son hôte.
        /// </summary>
        private ProcessUserControl GetProcessUserControl(Process process)
        {
            IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                                                  where child.Process != null && child.Process.IsSameEntry(process)
                                                                  select child;

            return processUserControls.FirstOrDefault();
        }

        /// <summary>
        /// Toutes les tuiles d'un même PID : le processus hôte et ses services.
        /// </summary>
        private List<ProcessUserControl> GetProcessUserControls(int processID)
        {
            return (from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                    where child.ProcessID == processID
                    select child).ToList();
        }

        private void loadProcessesButton_Click(object sender, RoutedEventArgs e)
        {
            Processes processes = null;
            Processes services = null;
            
            Mouse.OverrideCursor = Cursors.Wait;

            /*
             * WMI : la version française=> "Infrastructure de gestion Windows" https://www.generation-nt.com/reponses/windows-management-instrumentation-entraide-3645941.html
             * 
             If WMI corrupt (https://superuser.com/questions/190960/repair-wmi-on-windows-7) :
             net stop winmgmt
             Winmgmt /salvagerepository %windir%\System32\wbem
             Winmgmt /resetrepository %windir%\System32\wbem
             net start winmgmt
             */

            try
            {


                if (UsertextBox.Text.Trim() == string.Empty || ComputerNameTextBox.Text.Trim() == ".")
                {
                    processes = new Processes();
                    services = new Processes(TargetInstanceEnum.Win32_Service);
                }
                else
                {
                    processes = new Processes(ComputerNameTextBox.Text, DomainTextBox.Text, UsertextBox.Text, passwordBox.Password);
                    services = new Processes(ComputerNameTextBox.Text, DomainTextBox.Text, UsertextBox.Text, passwordBox.Password, TargetInstanceEnum.Win32_Service);
                }

                ResolveServiceHosts(processes, services);

                processes.AddProcesses(services);

                // Les objets Process des services sont désormais dans la liste
                // fusionnée : son échantillonneur les couvre. Celui de l'instance
                // services ferait une seconde énumération par tick pour rien.
                services.StopCPUSampling();

                Processes previousProcesses = this._processes;
                Processes previousServices = this._services;

                this._processes = processes;
                this._services = services;

                // L'instance abandonnée garde sinon ses gestionnaires : son
                // diff continuerait d'alimenter le panneau à partir d'une
                // liste périmée. Le réabonnement à la nouvelle instance a lieu
                // dans InitializeProcessWrapPanel, plus bas.
                UnsubscribeProcessEventHandlers(previousProcesses);

                // Un rechargement laissait tourner l'échantillonneur de l'instance
                // précédente.
                if (previousProcesses != null)
                {
                    previousProcesses.StopCPUSampling();
                }

                if (previousServices != null)
                {
                    previousServices.StopCPUSampling();
                }


                if (processes.Count > 0)
                {
                    

                    Processes selectedProcesses = null;

                    //if (CPUComboBox.Items.Count == 0 || CPUComboBox.SelectedValue.ToString() == "ALL")
                    //{
                        selectedProcesses = processes;
                        this.InitializeCPUComboBox(processes);
                    //}
                    //else
                    //{
                        //System.Threading.Tasks.Task.WaitAll(
                        //new System.Threading.Tasks.TaskFactory().StartNew(() => {
                        //    selectedProcesses = new Processes(new List<Process>(from process in processes
                        //                                                        where new ProcessAffinityWindow(process).IsCPUCheckBoxChecked(int.Parse(CPUComboBox.SelectedValue.ToString())) //(int)Math.Pow(2, int.Parse(CPUComboBox.SelectedValue.ToString())) //int.Parse(CPUComboBox.SelectedValue.ToString())
                        //                                                        select process).ToList());})) ;

                        //selectedProcesses = new Processes(new List<Process>(from process in processes
                        //                                                    where new ProcessAffinityWindow(process).IsCPUCheckBoxChecked(int.Parse(CPUComboBox.SelectedValue.ToString()))
                        //                                                    select process).ToList());

                    //}

                    

                    this.InitializeProcessWrapPanel(selectedProcesses);
                    SetTitle();

                }


            }
            catch(Exception ex)
            {

                MessageBox.Show(ex.Message + "\r\n", this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                
            }

            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void SetTitle()
        {

            for (int i = 0; i < this.processWrapPanel.Children.Count; i++)
            {
                if (this.processWrapPanel.Children[i].GetType() == typeof(ProcessUserControl))
                {
                    this.Title = "ProcessAffinity v" + GetApplicationVersion() + " on " + ((ProcessUserControl)this.processWrapPanel.Children[i]).Process.ComputerName;
                    break;
                }

            }

        }

        /// <summary>
        /// Version lue depuis l'assembly, au format majeur.mineur.correctif.
        /// </summary>
        private static string GetApplicationVersion()
        {
            Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            return version == null ? string.Empty : version.ToString(3);
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            if (this._processes != null)
            {
                this._processes.StopCPUSampling();
            }

            if (this._services != null)
            {
                this._services.StopCPUSampling();
            }
        }

        private void ShowServicesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this._processes != null)
            {
                this.InitializeProcessWrapPanel(this._processes);
            }
        }

        private void ProcessAffinityNotifyIconMouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.WindowState = WindowState.Normal;
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.ClearProcessWrapPanel();

                this.ShowInTaskbar = false;
                _processAffinityNotifyIcon.BalloonTipTitle = "ProcessAffinity minimized Sucessfully";
                _processAffinityNotifyIcon.BalloonTipText = "ProcessAffinity";
                _processAffinityNotifyIcon.ShowBalloonTip(400);
                _processAffinityNotifyIcon.Visible = true;
            }
            else if (this.WindowState == WindowState.Normal)
            {
                _processAffinityNotifyIcon.Visible = false;
                this.ShowInTaskbar = true;

                this.InitializeProcessWrapPanel();
            }
        }

        
    }
}
