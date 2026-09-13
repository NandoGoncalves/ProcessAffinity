using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading.Tasks;

using ProcessAffinityUI.Threading;
//using ProcessAffinityUI.Configuration;
using System.Windows.Interop;
using System.Windows.Media;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Interaction logic for ProcessUserControl.xaml
    /// </summary>
    public partial class ProcessUserControl : UserControl
    {
        private Process _process = null;
        private int _processID = 0; // Pour la suppression
        private ProcessAffinityColors _ProcessAffinityColors = null;


        public ProcessUserControl(Process process)
        {
            InitializeComponent();

            SetProcess(process);
        }

        public ProcessUserControl SetProcess(Process process)
        {
            this._process = process;
            this._processID = process.ProcessID;
            this._ProcessAffinityColors = new ProcessAffinityColors(this._process.Priority);

            try
            {
                process.SetNotifyCPUUsageChangeDelegate(new NotifyCPUUsageChangeDelegate(this.SetCPUUsageLabel));
                this.ProcessIDlabel.Content = process.ProcessName;
            }
            catch (Exception ex)
            {
                MessageBox.Show("ProcessUserControl constructor error.\n\n" + ex.Message, AppDomain.CurrentDomain.FriendlyName, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            this.SetIcon(process);

            return this;
        }

        public Process Process { get { return this._process; } }

        public ImageSource Icon { get { return this.ProcessImage.Source; } }

        public Brush ProcessNameLabelBackground { set { this.ProcessIDlabel.Background = value; } }

        public void SetIcon(Process process)
        {
            try
            {
                this.ProcessImage.Source = ConvertToImageSource(System.Drawing.Icon.ExtractAssociatedIcon(process.ExecutablePath)); //new System.Windows.Media.ImageBrush(ConvertToImageSource(System.Drawing.Icon.ExtractAssociatedIcon(process.InnerProcess.MainModule.FileName)));
            }
            catch
            {
                //this.ProcessImage.Visibility = System.Windows.Visibility.Hidden; 
            }
        }

        public bool IsSelected{ get { return (bool)SelectedUserControlCheckBox.IsChecked;} set{ SetSelected(value);}}

        private void SetCPUUsageLabel(int cpuUsage)
        {
            Task.Run(() => { 
                try
                {

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel16.Content = this.CPUUsagelabel15.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel16.Height = this.CPUUsagelabel15.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel16.Background = this.CPUUsagelabel15.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel15.Content = this.CPUUsagelabel14.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel15.Height = this.CPUUsagelabel14.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel15.Background = this.CPUUsagelabel14.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel14.Content = this.CPUUsagelabel13.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel14.Height = this.CPUUsagelabel13.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel14.Background = this.CPUUsagelabel13.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel13.Content = this.CPUUsagelabel12.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel13.Height = this.CPUUsagelabel12.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel13.Background = this.CPUUsagelabel12.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel12.Content = this.CPUUsagelabel11.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel12.Height = this.CPUUsagelabel11.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel12.Background = this.CPUUsagelabel11.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel11.Content = this.CPUUsagelabel10.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel11.Height = this.CPUUsagelabel10.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel11.Background = this.CPUUsagelabel10.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel10.Content = this.CPUUsagelabel9.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel10.Height = this.CPUUsagelabel9.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel10.Background = this.CPUUsagelabel9.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel9.Content = this.CPUUsagelabel8.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel9.Height = this.CPUUsagelabel8.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel9.Background = this.CPUUsagelabel8.Background; }), new object[] { });









                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel8.Content = this.CPUUsagelabel7.Content; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel8.Height = this.CPUUsagelabel7.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel8.Background = this.CPUUsagelabel7.Background; }), new object[] { });

                    this.CPUUsagelabel7.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel7.Content = this.CPUUsagelabel6.Content; }), new object[] { });
                    this.CPUUsagelabel7.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel7.Height = this.CPUUsagelabel6.Height; }), new object[] { });
                    this.CPUUsagelabel7.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel7.Background = this.CPUUsagelabel6.Background; }), new object[] { });

                    this.CPUUsagelabel6.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel6.Content = this.CPUUsagelabel5.Content; }), new object[] { });
                    this.CPUUsagelabel6.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel6.Height = this.CPUUsagelabel5.Height; }), new object[] { });
                    this.CPUUsagelabel6.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel6.Background = this.CPUUsagelabel5.Background; }), new object[] { });

                    this.CPUUsagelabel5.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel5.Content = this.CPUUsagelabel4.Content; }), new object[] { });
                    this.CPUUsagelabel5.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel5.Height = this.CPUUsagelabel4.Height; }), new object[] { });
                    this.CPUUsagelabel5.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel5.Background = this.CPUUsagelabel4.Background; }), new object[] { });

                    this.CPUUsagelabel4.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel4.Content = this.CPUUsagelabel3.Content; }), new object[] { });
                    this.CPUUsagelabel4.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel4.Height = this.CPUUsagelabel3.Height; }), new object[] { });
                    this.CPUUsagelabel4.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel4.Background = this.CPUUsagelabel3.Background; }), new object[] { });

                    this.CPUUsagelabel3.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel3.Content = this.CPUUsagelabel2.Content; }), new object[] { });
                    this.CPUUsagelabel3.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel3.Height = this.CPUUsagelabel2.Height; }), new object[] { });
                    this.CPUUsagelabel3.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel3.Background = this.CPUUsagelabel2.Background; }), new object[] { });

                    this.CPUUsagelabel2.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel2.Content = this.CPUUsagelabel1.Content; }), new object[] { });
                    this.CPUUsagelabel2.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel2.Height = this.CPUUsagelabel1.Height; }), new object[] { });
                    this.CPUUsagelabel2.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel2.Background = this.CPUUsagelabel1.Background; }), new object[] { });

                    this.CPUUsagelabel1.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel1.Content = this.CPUUsagelabel0.Content; }), new object[] { });
                    this.CPUUsagelabel1.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel1.Height = this.CPUUsagelabel0.Height; }), new object[] { });
                    this.CPUUsagelabel1.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel1.Background = this.CPUUsagelabel0.Background; }), new object[] { });

                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel0.Content = cpuUsage.ToString(); }), new object[] { });
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel0.Height = (56 * cpuUsage) / 100; }), new object[] { });
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel0.Background = new System.Windows.Media.SolidColorBrush(UIntToColor(uint.Parse(ConvertToValidRGBValue(cpuUsage).ToString()))); }), new object[] { });
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.ProcessNameLabelBackground = Brushes.White; }), new object[] { });

                }
                catch
                {
                    this.CPUUsagelabel0.Background = Brushes.Gray;
                }
            });
        }

        public int ProcessID { get { return this._processID; } }

        private System.Windows.Media.Color UIntToColor(uint color)
        {
            //byte a = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);
            //byte r = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);
            //byte g = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);
            //byte b = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);

            byte a = (byte)(this._ProcessAffinityColors.A*color/100 >> 0);
            byte r = (byte)((this._ProcessAffinityColors.R) >> 0);
            byte g = (byte)((this._ProcessAffinityColors.G) >> 0);
            byte b = (byte)((this._ProcessAffinityColors.B) >> 0);

            return System.Windows.Media.Color.FromArgb(a, r, g, b);
        }


        private int ConvertToValidRGBValue(int value)
        {
            return (255 * value) / 100;
        }

        public System.Windows.Media.ImageSource ConvertToImageSource(System.Drawing.Icon icon)
        {
            System.Windows.Media.ImageSource imageSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            return imageSource;
        }


        private void ProcessImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            UserControl_MouseRightButtonDown(sender, e);
        }

        private void ProcessIDlabel_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            UserControl_MouseRightButtonDown(sender, e);
        }

        private void UserControl_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ContextMenu menu = new ContextMenu();

            if (this._process.IsService)
            {
                MenuItem menuItem = new MenuItem();
                menuItem.Header = "Service";
                menu.Items.Add(menuItem);

                //menuItem = new MenuItem();
                //menuItem.Header = "Priority";
                //((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
            }
            else if(this._process.IsProcessMonitored)
            {
                MenuItem menuItem = new MenuItem();
                menuItem.Header = "Monitored process";
                menu.Items.Add(menuItem);
            }
            else
            { 
                MenuItem menuItem = new MenuItem();
                menuItem.Header = "Priority";
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

                menuItem = new MenuItem();
                menuItem.Header = "Affinity";
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

                menuItem = new MenuItem();
                menuItem.Header = "Kill";
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click +=new RoutedEventHandler(ProcessUserControlContextMenuClick);

                menuItem = new MenuItem();
                menuItem.Header = "Is alive ?";
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
            }

            this.ContextMenu = menu;

            
        }

        private void ProcessUserControlContextMenuClick(object sender, RoutedEventArgs e)
        {
            ProcessPriorityWindow processPriorityWindow = null;
            ProcessAffinityWindow processAffinityWindow = null;

            switch (((MenuItem)e.OriginalSource).Header.ToString())
            {
                case "Is alive ?":
                    switch (this._process.IsAlive())
                    {
                        case false:
                            this.Opacity = 10;
                            break;
                        case true:
                            SetCPUUsageLabel(100);
                            break;
                    }
                    break;
                case "Kill":
                    this._process.Kill();
                    break;
                case "Priority":
                    processPriorityWindow =new ProcessPriorityWindow(this._process);
                    processPriorityWindow.ShowDialog();
                    SetProcessAffinityColors();
                    break;
                case "Affinity":
                    processAffinityWindow = new ProcessAffinityWindow(this._process);
                    processAffinityWindow.ShowDialog();
                    break;
            }
 
        }

        public bool IsAlive { get; internal set; }

        public void SetProcessAffinityColors()
        {
            this._ProcessAffinityColors = new ProcessAffinityColors(this._process.Priority);
        }

        private void ToHide()
        {
            this.Visibility = System.Windows.Visibility.Hidden;
        }

        private void ToShow()
        {
            this.Visibility = System.Windows.Visibility.Visible;
        }

        private void CPUUsagelabel_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                this.ToolTip = "ProcessID : " + 
                    this._process.ProcessID.ToString() + "\r\n" + 
                    this._process.ProcessName + "\r\n" + 
                    this._process.Priority.ToString() ;
            }
            catch
            {
                if (this._process == null)
                {
                    this.ToolTip = "Process don't exists";
                }
                else
                {
                    this.ToolTip = "ProcessID : unreachable";
                }
            }
        }

        private void SetSelected(bool isSelected)
        {
            this.SetSelected(isSelected, null);
        }

        private void SetSelected(bool isSelected, Visibility? visibility)
        {
            if (!this._process.IsService)
            {
                if (visibility == null)
                {
                    SelectedUserControlCheckBox.Visibility = isSelected ? Visibility.Visible : Visibility.Hidden;
                    SelectedUserControlCheckBox.IsChecked = isSelected;
                }
                else
                {
                    SelectedUserControlCheckBox.Visibility = (Visibility)visibility;
                    SelectedUserControlCheckBox.IsChecked = isSelected;
                }
            }
        }

        private void UserControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SetSelected(!IsSelected);
        }
    }

    public class ProcessAffinityColors
    {
        private ProcessPriorityEnum _processPriorityEnum;
        
        private int _a = 255;
        private int _r = 255;
        private int _g = 0;
        private int _b = 0;

        public ProcessAffinityColors(int priority)
        {
            this._processPriorityEnum = Process.ToProcessPriorityEnum(priority);
            if (priority == 0) this._processPriorityEnum = Threading.ProcessPriorityEnum.Normal;

            switch (this._processPriorityEnum)
            {
                case Threading.ProcessPriorityEnum.Idle:
                    this._a = 255;
                    this._r = 0;
                    this._g = 255;
                    this._b = 0;
                    break;
                case Threading.ProcessPriorityEnum.BelowNormal:
                    this._a = 255;
                    this._r = 255;
                    this._g = 255;
                    this._b = 0;
                    break;
                case Threading.ProcessPriorityEnum.Normal:
                    this._a = 255;
                    this._r = 255;
                    this._g = 0;
                    this._b = 0;
                    break;
                case Threading.ProcessPriorityEnum.AboveNormal:
                    this._a = 255;
                    this._r = 200;
                    this._g = 0;
                    this._b = 100;
                    break;
                case Threading.ProcessPriorityEnum.HighPriority:
                    this._a = 255;
                    this._r = 100;
                    this._g = 0;
                    this._b = 100;
                    break;
                case Threading.ProcessPriorityEnum.RealTime:
                    this._a = 255;
                    this._r = 0;
                    this._g = 0;
                    this._b = 0;
                    break;
            }
 
        }


        public ProcessPriorityEnum ProcessPriorityEnum { get { return this._processPriorityEnum; } }
        public int A{ get { return this._a; } }
        public int R{ get { return this._r; } }
        public int G{ get { return this._g; } }
        public int B{ get { return this._b; } }
    
    }
}
