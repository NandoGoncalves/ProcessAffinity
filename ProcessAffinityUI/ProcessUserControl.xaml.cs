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
        /// <summary>Titre des boîtes de dialogue.</summary>
        private const string ApplicationName = "ProcessAffinity";

        /// <summary>Hauteur de l'emplacement d'une barre, en pixels (cf. XAML).</summary>
        private const double CPUUsageBarHeight = 56d;

        /// <summary>Fond du bandeau de nom des entrées non modifiables.</summary>
        private static readonly Brush NotModifiableNameBackgroundBrush =
            new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

        /// <summary>
        /// Durée d'affichage de l'infobulle. La valeur par défaut de WPF, 5 s, la
        /// refermerait avant qu'on ait pu suivre l'évolution de la charge ; elle se
        /// referme de toute façon dès que la souris quitte le contrôle.
        /// </summary>
        private const int ToolTipShowDurationMilliseconds = 3600000;

        private Process _process = null;
        private int _processID = 0; // Pour la suppression
        private ProcessAffinityColors _ProcessAffinityColors = null;

        private readonly ToolTip _toolTip = null;
        private readonly TextBlock _toolTipTextBlock = null;

        /// <summary>Lu depuis le thread d'échantillonnage.</summary>
        private volatile bool _isToolTipOpen = false;


        public ProcessUserControl(Process process)
        {
            InitializeComponent();

            // Contenu vivant plutôt qu'une chaîne figée : le texte peut être
            // réécrit pendant que l'infobulle est affichée.
            this._toolTipTextBlock = new TextBlock();
            this._toolTip = new ToolTip();
            this._toolTip.Content = this._toolTipTextBlock;
            this._toolTip.Opened += ToolTipOpened;
            this._toolTip.Closed += ToolTipClosed;

            this.ToolTip = this._toolTip;
            ToolTipService.SetShowDuration(this, ToolTipShowDurationMilliseconds);

            this.ToolTipOpening += ProcessUserControlToolTipOpening;

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
                this.ProcessIDlabel.Text = GetEntryLabel(process);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not initialise the process tile.\r\n\r\n" + ex.Message, ApplicationName, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            this.SetIcon(process);
            this.SetModifiableMarker(process);

            return this;
        }

        /// <summary>
        /// Les entrées dont l'affinité et la priorité ne sont pas modifiables
        /// portent leur nom en blanc sur gris foncé. Le reste de la tuile est
        /// inchangé.
        /// </summary>
        private void SetModifiableMarker(Process process)
        {
            this.ProcessNameLabelBackground = GetProcessNameBackgroundBrush();
        }

        /// <summary>
        /// Fond du bandeau de nom au repos. L'échantillon CPU le réécrit à chaque
        /// seconde : sans cela le marquage serait effacé aussitôt posé.
        /// </summary>
        private Brush GetProcessNameBackgroundBrush()
        {
            if (this._process != null && !this._process.IsModifiable)
            {
                return NotModifiableNameBackgroundBrush;
            }

            return Brushes.White;
        }

        /// <summary>
        /// Une tuile de service s'intitule « NomDuService - NomDeLHôte ».
        /// </summary>
        private static string GetEntryLabel(Process process)
        {
            if (process.IsService && !string.IsNullOrEmpty(process.HostProcessName))
            {
                return process.ProcessName + " - " + process.HostProcessName;
            }

            return process.ProcessName;
        }

        public Process Process { get { return this._process; } }

        public ImageSource Icon { get { return this.ProcessImage.Source; } }

        /// <summary>
        /// Fond du bandeau de nom. La couleur de police suit : blanche sur le
        /// gris des entrées non modifiables, noire sur le jaune de la mise en
        /// évidence comme sur le blanc au repos.
        /// </summary>
        public Brush ProcessNameLabelBackground
        {
            set
            {
                this.ProcessIDlabel.Background = value;
                this.ProcessIDlabel.Foreground = GetProcessNameForegroundBrush(value);
            }
        }

        private static Brush GetProcessNameForegroundBrush(Brush background)
        {
            SolidColorBrush solidColorBrush = background as SolidColorBrush;

            if (solidColorBrush == null)
            {
                return Brushes.Black;
            }

            Color color = solidColorBrush.Color;

            double luminance = ((0.299d * color.R) + (0.587d * color.G) + (0.114d * color.B)) / 255d;

            return luminance < 0.5d ? Brushes.White : Brushes.Black;
        }

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

        private void SetCPUUsageLabel(double? cpuUsage)
        {
            // Une tuile pousse une trentaine d'opérations par seconde dans le
            // dispatcher : aucune ne doit être postée une fois l'arrêt engagé.
            if (this.Dispatcher.HasShutdownStarted || this.Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Task.Run(() => {
                try
                {

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel16.Height = this.CPUUsagelabel15.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel16.Background = this.CPUUsagelabel15.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel15.Height = this.CPUUsagelabel14.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel15.Background = this.CPUUsagelabel14.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel14.Height = this.CPUUsagelabel13.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel14.Background = this.CPUUsagelabel13.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel13.Height = this.CPUUsagelabel12.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel13.Background = this.CPUUsagelabel12.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel12.Height = this.CPUUsagelabel11.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel12.Background = this.CPUUsagelabel11.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel11.Height = this.CPUUsagelabel10.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel11.Background = this.CPUUsagelabel10.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel10.Height = this.CPUUsagelabel9.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel10.Background = this.CPUUsagelabel9.Background; }), new object[] { });

                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel9.Height = this.CPUUsagelabel8.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel9.Background = this.CPUUsagelabel8.Background; }), new object[] { });









                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel8.Height = this.CPUUsagelabel7.Height; }), new object[] { });
                    this.CPUUsagelabel8.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel8.Background = this.CPUUsagelabel7.Background; }), new object[] { });

                    this.CPUUsagelabel7.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel7.Height = this.CPUUsagelabel6.Height; }), new object[] { });
                    this.CPUUsagelabel7.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel7.Background = this.CPUUsagelabel6.Background; }), new object[] { });

                    this.CPUUsagelabel6.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel6.Height = this.CPUUsagelabel5.Height; }), new object[] { });
                    this.CPUUsagelabel6.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel6.Background = this.CPUUsagelabel5.Background; }), new object[] { });

                    this.CPUUsagelabel5.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel5.Height = this.CPUUsagelabel4.Height; }), new object[] { });
                    this.CPUUsagelabel5.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel5.Background = this.CPUUsagelabel4.Background; }), new object[] { });

                    this.CPUUsagelabel4.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel4.Height = this.CPUUsagelabel3.Height; }), new object[] { });
                    this.CPUUsagelabel4.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel4.Background = this.CPUUsagelabel3.Background; }), new object[] { });

                    this.CPUUsagelabel3.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel3.Height = this.CPUUsagelabel2.Height; }), new object[] { });
                    this.CPUUsagelabel3.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel3.Background = this.CPUUsagelabel2.Background; }), new object[] { });

                    this.CPUUsagelabel2.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel2.Height = this.CPUUsagelabel1.Height; }), new object[] { });
                    this.CPUUsagelabel2.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel2.Background = this.CPUUsagelabel1.Background; }), new object[] { });

                    this.CPUUsagelabel1.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel1.Height = this.CPUUsagelabel0.Height; }), new object[] { });
                    this.CPUUsagelabel1.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel1.Background = this.CPUUsagelabel0.Background; }), new object[] { });

                    // La valeur affichée reste rapportée à la machine entière, pour
                    // rester comparable au Gestionnaire des tâches. La barre, elle,
                    // est pleine quand le processus sature un cœur : c'est l'échelle
                    // parlante pour un outil d'affinité, et celle de l'application
                    // d'origine. Pas encore de delta disponible (premier
                    // échantillon) : un tiret, pas un 0 % trompeur.
                    double singleCoreUsage = cpuUsage.HasValue ? cpuUsage.Value * Environment.ProcessorCount : 0d;

                    if (singleCoreUsage < 0d)
                    {
                        singleCoreUsage = 0d;
                    }
                    else if (singleCoreUsage > 100d)
                    {
                        singleCoreUsage = 100d;
                    }

                    string displayedContent = cpuUsage.HasValue ? cpuUsage.Value.ToString("F0") : "-";
                    double displayedHeight = (CPUUsageBarHeight * singleCoreUsage) / 100d;
                    int displayedColorValue = (int)Math.Round(singleCoreUsage, MidpointRounding.AwayFromZero);

                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsageValueTextBlock.Text = displayedContent; }), new object[] { });
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel0.Height = displayedHeight; }), new object[] { });
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel0.Background = new System.Windows.Media.SolidColorBrush(UIntToColor((uint)ConvertToValidRGBValue(displayedColorValue))); }), new object[] { });
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.ProcessNameLabelBackground = GetProcessNameBackgroundBrush(); }), new object[] { });

                    // Infobulle affichée : on la tient à jour. Test hors Dispatcher
                    // pour n'ajouter aucun travail aux tuiles dont elle est fermée,
                    // c'est-à-dire à toutes sauf une.
                    if (this._isToolTipOpen)
                    {
                        this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { UpdateToolTip(); }), new object[] { });
                    }

                }
                catch
                {
                    this.CPUUsagelabel0.Dispatcher.BeginInvoke(new Action(() => { this.CPUUsagelabel0.Background = Brushes.Gray; }), new object[] { });
                }
            });
        }

        /// <summary>
        /// Nombre de cœurs autorisés sur le nombre de processeurs logiques, ou
        /// « unknown » : un masque illisible ne doit pas être compté comme nul.
        /// </summary>
        private string GetAffinityText()
        {
            nuint? processorAffinity = this._process.GetProcessorAffinity();

            if (processorAffinity == null)
            {
                return "unknown";
            }

            int allowedCoreCount = System.Numerics.BitOperations.PopCount((ulong)processorAffinity.Value);

            return allowedCoreCount.ToString() + "/" + Environment.ProcessorCount.ToString();
        }

        public int ProcessID { get { return this._processID; } }

        private System.Windows.Media.Color UIntToColor(uint color)
        {
            //byte a = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);
            //byte r = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);
            //byte g = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);
            //byte b = (byte)((int)(color * Processes.GetRandomNumber() / 100) >> 0);

            // color vaut déjà 0-255 : le rapporter une seconde fois à 100 faisait
            // dépasser 255 dès ~39 % de charge, l'octet se tronquait et la couleur
            // s'assombrissait au lieu de s'intensifier.
            if (color > 255)
            {
                color = 255;
            }

            byte a = (byte)(this._ProcessAffinityColors.A * color / 255);
            byte r = (byte)this._ProcessAffinityColors.R;
            byte g = (byte)this._ProcessAffinityColors.G;
            byte b = (byte)this._ProcessAffinityColors.B;

            return System.Windows.Media.Color.FromArgb(a, r, g, b);
        }


        private int ConvertToValidRGBValue(int value)
        {
            // Même plafonnement que pour la hauteur de la barre.
            if (value < 0)
            {
                value = 0;
            }
            else if (value > 100)
            {
                value = 100;
            }

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

                // Terminer exige les mêmes droits qu'écrire l'affinité ou la
                // priorité : ne pas proposer ce qui ne peut pas aboutir.
                if (this._process.IsModifiable)
                {
                    menuItem = new MenuItem();
                    menuItem.Header = "Kill";
                    ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
                }

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
                    KillProcess();
                    break;
                case "Priority":
                    processPriorityWindow =new ProcessPriorityWindow(this._process);
                    processPriorityWindow.ShowDialog();
                    RefreshEntriesSharingHost();
                    break;
                case "Affinity":
                    processAffinityWindow = new ProcessAffinityWindow(this._process);
                    processAffinityWindow.ShowDialog();
                    RefreshEntriesSharingHost();
                    break;
            }
 
        }

        /// <summary>
        /// Refus sur les processus critiques, confirmation sur une entrée de
        /// service — dont la terminaison emporte le processus hôte, et donc tous
        /// les services qu'il héberge.
        /// </summary>
        private void KillProcess()
        {
            if (this._process.IsCriticalSystemProcess)
            {
                MessageBox.Show(
                    "\"" + this._process.ProcessName + "\" is a critical system process.\r\n\r\n" +
                    "Terminating it would crash Windows. This action is not allowed.",
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            if (this._process.IsService)
            {
                string hostName = string.IsNullOrEmpty(this._process.HostProcessName)
                    ? "its host process"
                    : "\"" + this._process.HostProcessName + "\"";

                string message = "\"" + this._process.ProcessName + "\" is a service.\r\n\r\n" +
                    "Terminating it kills " + hostName + " (PID " + this._process.ProcessID + ")";

                IList<string> sharedServiceNames = this._process.SharedServiceNames;

                if (sharedServiceNames != null && sharedServiceNames.Count > 1)
                {
                    message = message + ", and with it the " + sharedServiceNames.Count +
                        " services it hosts:\r\n" + string.Join(", ", sharedServiceNames);
                }

                message = message + ".\r\n\r\nContinue?";

                if (MessageBox.Show(message, ApplicationName, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            uint? returnCode = this._process.Kill();

            if (returnCode == null)
            {
                MessageBox.Show(
                    "The termination request could not be sent to \"" + this._process.ProcessName + "\".",
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            if (returnCode.Value != 0)
            {
                MessageBox.Show(
                    "\"" + this._process.ProcessName + "\" could not be terminated.\r\n\r\n" +
                    DescribeTerminateReturnCode(returnCode.Value),
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Codes de retour de Win32_Process.Terminate.
        /// </summary>
        private static string DescribeTerminateReturnCode(uint returnCode)
        {
            switch (returnCode)
            {
                case 2:
                    return "Access denied.";

                case 3:
                    return "Insufficient privilege. Try running ProcessAffinity as administrator.";

                case 8:
                    return "Unknown failure.";

                case 9:
                    return "Path not found.";

                case 21:
                    return "Invalid parameter.";

                default:
                    return "Windows returned code " + returnCode.ToString() + ".";
            }
        }

        public bool IsAlive { get; internal set; }

        /// <summary>
        /// La modification a porté sur le processus hôte : toutes les tuiles de
        /// ce PID doivent être rafraîchies, sans quoi les tuiles sœurs affichent
        /// une priorité et une visibilité périmées.
        /// </summary>
        private void RefreshEntriesSharingHost()
        {
            MainWindow mainWindow = Window.GetWindow(this) as MainWindow;

            if (mainWindow != null)
            {
                mainWindow.RefreshProcessUserControls(this._processID);
            }
            else
            {
                SetProcessAffinityColors();
            }
        }

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
            UpdateToolTip();
        }

        private void ProcessUserControlToolTipOpening(object sender, ToolTipEventArgs e)
        {
            UpdateToolTip();
        }

        private void ToolTipOpened(object sender, RoutedEventArgs e)
        {
            this._isToolTipOpen = true;

            // ToolTipOpening n'est levé que par le survol via ToolTipService : ce
            // second rafraîchissement garantit un contenu à jour quelle que soit
            // la façon dont l'infobulle a été ouverte.
            UpdateToolTip();
        }

        private void ToolTipClosed(object sender, RoutedEventArgs e)
        {
            this._isToolTipOpen = false;
        }

        /// <summary>
        /// Réécrit le contenu de l'infobulle. Appelée à l'ouverture, puis à chaque
        /// échantillon tant qu'elle reste affichée.
        /// </summary>
        private void UpdateToolTip()
        {
            try
            {
                double? cpuUsage = this._process.CPUUsage;

                string text = "Process ID: " +
                    this._process.ProcessID.ToString() + "\r\n" +
                    GetEntryLabel(this._process) + "\r\n" +
                    "Priority: " + this._process.Priority.ToString() + "\r\n" +
                    "Affinity: " + GetAffinityText() + "\r\n" +
                    "CPU: " + (cpuUsage.HasValue ? cpuUsage.Value.ToString("F1") + " %" : "-");

                if (!this._process.IsModifiable)
                {
                    text = text + "\r\nAffinity and priority cannot be changed without elevation.";
                }

                // Affinité et priorité s'appliquent au processus hôte : quand il
                // en héberge plusieurs, toute modification les affecte tous.
                IList<string> sharedServiceNames = this._process.SharedServiceNames;

                if (sharedServiceNames != null && sharedServiceNames.Count > 1)
                {
                    text = text + "\r\n\r\n" +
                        sharedServiceNames.Count + " services in this process:\r\n" +
                        string.Join(", ", sharedServiceNames) + "\r\n" +
                        "Any affinity or priority change affects them all.";
                }

                this._toolTipTextBlock.Text = text;
            }
            catch
            {
                if (this._process == null)
                {
                    this._toolTipTextBlock.Text = "Process don't exists";
                }
                else
                {
                    this._toolTipTextBlock.Text = "ProcessID : unreachable";
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
