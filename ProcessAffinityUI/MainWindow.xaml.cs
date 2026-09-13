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
        ProcessEventHandler _processEventArrived = null;
        ProcessEventHandler _processCreated = null;
        ProcessEventHandler _processDeleted = null;
        ProcessEventHandler _processModified = null;

        public MainWindow()
        {
                InitializeComponent();

                this._processAffinityNotifyIcon = new System.Windows.Forms.NotifyIcon();
                this._processAffinityNotifyIcon.Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("/ProcessAffinity.ico", UriKind.RelativeOrAbsolute)).Stream);


                _processAffinityNotifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(ProcessAffinityNotifyIconMouseDoubleClick);

                this.StateChanged += new EventHandler(WindowStateChanged);

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

            for(int i = 0;i < processes.Count; i++) 
            {
                try
                {

                    if (processes[i] != null)
                    {
                        processUserControl = new ProcessUserControl(processes[i]);
                        bool processUserControlExists = ProcessUserControlExists(processes[i]);

                        if (processUserControlExists == false)
                        {
                            this.processWrapPanel.Children.Add(processUserControl);

                            SetProcessUserControlVisibility(processUserControl);
                        }
                        else
                        {
                            GetProcessUserControl(processes[i]).SetProcess(processes[i]);
                        }
                    }
                }
                catch
                {
                    //this.processWrapPanel.Children.Add(new ProcessUserControl(new Process(this.ComputerNameLabel.Content.ToString(), 0, processes[i] == null?"Not exists":processes[i].ProcessName)));
                }
            }
        }

        private void SubscribeProcessEventHandlers()
        {
            this.SubscribeProcessEventHandlers(this._processes);
        }

        private void SubscribeProcessEventHandlers(Processes processes)
        {
            if (_processEventArrived == null || _processEventArrived.GetInvocationList().LongLength == 0) 
            {
                _processEventArrived = new ProcessEventHandler(Processes_ProcessEventArrived);
                processes.ProcessEventArrived += _processEventArrived;
            }

            if (_processCreated == null || _processCreated.GetInvocationList().LongLength == 0)
            {
                _processCreated = new ProcessEventHandler(Processes_ProcessCreated);
                processes.ProcessCreated += _processCreated;
            }

            if (_processDeleted == null || _processDeleted.GetInvocationList().LongLength == 0)
            {
                _processDeleted = new ProcessEventHandler(Processes_ProcessDeleted);
                processes.ProcessDeleted += _processDeleted;
            }

            if (_processModified == null || _processModified.GetInvocationList().LongLength == 0)
            {
                _processModified = new ProcessEventHandler(Processes_ProcessModified);
                processes.ProcessModified += _processModified;
            }

            EventsSubscritionsLabel.Content = _processEventArrived.GetInvocationList().LongLength.ToString() + "-" + _processCreated.GetInvocationList().LongLength.ToString() + "-" + _processDeleted.GetInvocationList().LongLength.ToString() + "-" + _processModified.GetInvocationList().LongLength.ToString();
        }

        private void UnsubscribeProcessEventHandlers()
        {
            this.UnsubscribeProcessEventHandlers(this._processes);
        }

            private void UnsubscribeProcessEventHandlers(Processes processes)
        {

                processes.ProcessEventArrived -= _processEventArrived;
                processes.ProcessCreated -= _processCreated;
                processes.ProcessDeleted -= _processDeleted;
                processes.ProcessModified -= _processModified;

        }

        private void ClearProcessWrapPanel()
        {
            UnsubscribeProcessEventHandlers();

            for(int i = 0;i < processWrapPanel.Children.Count;i++)
            {
                ProcessUserControl puc = (ProcessUserControl)processWrapPanel.Children[i];
                puc.DataContext = null;
                processWrapPanel.Children.Remove(puc);
                puc = null;
            }

            this.SubscribeProcessEventHandlers();
        }

        private void InitializeCPUComboBox(Processes processes)
        {
            int numberOfProcessors = 0;

            if (
                    ComputerNameTextBox.Text.Trim().ToLower() == "localhost")
            {
                numberOfProcessors = processes.GetProcessorsProperties().NumberOfLogicalProcessors;
            }
            else
            {
                numberOfProcessors = processes.GetProcessorsProperties().NumberOfProcessors;
            }

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
            
        }

        private void SetProcessUserControlVisibility(ProcessUserControl processUserControl)
        {
            if (CPUComboBox.SelectedValue.ToString() == "ALL")
            {
                processUserControl.Visibility = Visibility.Visible;
            }
            else if (ProcessAffinityWindow.ToBinary((ulong)processUserControl.Process.GetProcessorAffinity(), CPUComboBox.Items.Count - 1).Substring(int.Parse(CPUComboBox.SelectedIndex.ToString()), 1) == "1") // Si CPU sélectionnée => 0 (?!)
            {
                processUserControl.Visibility = Visibility.Visible;
            }
            else
            {
                processUserControl.Visibility = Visibility.Collapsed;
            }
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
                    break;
                case "Priority all processes":
                    ProcessPriorityWindow processPriorityWindow = new ProcessPriorityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().ToList());
                    processPriorityWindow.ShowDialog();
                    break;
                case "Priority selected processes":
                    ProcessPriorityWindow selectedProcessPriorityWindow = new ProcessPriorityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().Where(puc => puc.IsSelected == true).ToList());
                    selectedProcessPriorityWindow.ShowDialog();
                    break;

                case "Affinity selected processes":
                    ProcessAffinityWindow selectedProcessAffinityWindow = new ProcessAffinityWindow(this.processWrapPanel.Children.Cast<ProcessUserControl>().Where(puc => puc.IsSelected == true).ToList());
                    selectedProcessAffinityWindow.ShowDialog();
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

                Parallel.ForEach(_processes, (process) => {
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
            this.ProcessesCountLabel.Content = this._processes.Count.ToString(); 
            this.ProcessControlsCountLabel.Content = this.processWrapPanel.Children.Count.ToString();
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

            ProcessUserControl processUserControl = this.GetProcessUserControl(process);

            if (processUserControl != null)
            {
                this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() => this.processWrapPanel.Children.Remove(processUserControl)));
            }
            //}
        }

        private bool ProcessUserControlExists(Process process)
        {
            bool processUserControlExists = false;

            IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                                                  where child.ProcessID == process.ProcessID
                                                                  select child;

            foreach (ProcessUserControl processUserControl in processUserControls)
            {
                processUserControlExists = true;
                break;
            }

            return processUserControlExists;
        }

        private ProcessUserControl GetProcessUserControl(Process process)
        {
            ProcessUserControl processUserControl = null;

            IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                                                  where child.ProcessID == process.ProcessID
                                                                  select child;

            foreach (ProcessUserControl puc in processUserControls)
            {
                processUserControl = puc;
                break;
            }

            return processUserControl;
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


                if (UsertextBox.Text.Trim() == string.Empty || ComputerNameTextBox.Text.Trim().ToLower() == "localhost")
                {
                    processes = new Processes();
                    services = new Processes(TargetInstanceEnum.Win32_Service);
                }
                else
                {
                    processes = new Processes(ComputerNameTextBox.Text, DomainTextBox.Text, UsertextBox.Text, passwordBox.Password);
                    services = new Processes(ComputerNameTextBox.Text, DomainTextBox.Text, UsertextBox.Text, passwordBox.Password, TargetInstanceEnum.Win32_Service);
                }

                processes.AddRange(services);

                this._processes = processes;


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
                    this.Title = "ProcessAffinity on " + ((ProcessUserControl)this.processWrapPanel.Children[i]).Process.ComputerName;
                    break;
                }
                
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
