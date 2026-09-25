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
using ProcessAffinityUI.Configuration;

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

        /// <summary>
        /// Vrai dès la fermeture demandée. Les opérations déjà postées sur le
        /// dispatcher s'écartent d'elles-mêmes : elles s'exécutent après, quand
        /// l'Application refuse déjà de charger la moindre ressource. Volatile,
        /// la levée venant du thread de l'IHM et la lecture parfois d'un autre.
        /// </summary>
        private volatile bool _isShuttingDown = false;

        /// <summary>Le fichier de règles illisible n'est signalé qu'une fois par session.</summary>
        private bool _ruleLoadErrorReported = false;

        public MainWindow()
        {
                InitializeComponent();

                this._processAffinityNotifyIcon = new System.Windows.Forms.NotifyIcon();
                this._processAffinityNotifyIcon.Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("/ProcessAffinity.ico", UriKind.RelativeOrAbsolute)).Stream);


                _processAffinityNotifyIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(ProcessAffinityNotifyIconMouseDoubleClick);

                this.StateChanged += new EventHandler(WindowStateChanged);

                // Closing précède la fermeture de la fenêtre, donc l'arrêt de
                // l'Application ; Closed, lui, survient quand celui-ci a déjà
                // commencé. C'est ici qu'il faut couper les producteurs
                // d'événements. Closed reste branché comme filet : StopBackgroundWork
                // est idempotent.
                this.Closing += new System.ComponentModel.CancelEventHandler(WindowClosing);
                this.Closed += new EventHandler(WindowClosed);

                AttachCounterToolTip(this.ProcessControlsCountLabel);
                AttachCounterToolTip(this.ProcessesCountLabel);

                processWrapPanel.MouseRightButtonDown += ProcessWrapPanel_MouseRightButtonDown;

                // Un clic dans le vide vide la sélection. Le ScrollViewer reçoit
                // aussi l'abonnement : quand les tuiles ne remplissent pas la
                // hauteur visible, la zone sous la dernière lui appartient et non
                // au panneau.
                processWrapPanel.MouseLeftButtonDown += ProcessWrapPanel_MouseLeftButtonDown;
                processScrollViewer.MouseLeftButtonDown += ProcessWrapPanel_MouseLeftButtonDown;

                // La tuile raisonne sur la sélection entière sans connaître la
                // fenêtre : on lui fournit la source et le geste de vidage.
                ProcessUserControl.SelectionSource = () =>
                    this._processes == null ? new Process[0] : this._processes.Snapshot();

                ProcessUserControl.ClearSelectionRequested = this.ClearSelection;
                ProcessUserControl.SelectionChanged = this.SetSelectedCounter;
                ConfigurationCommands.MarkersChanged = this.RefreshProcessUserControls;

                // La couche de commande ne connaît pas l'IHM : c'est la fenêtre qui
                // sait poser une question.
                ConfigurationCommands.ConflictResolver = this.AskWhichConfiguration;

                this.Loaded += MainWindowLoaded;
        }

        // La surcharge sans argument n'avait plus qu'un appelant, la restauration
        // depuis la zone de notification, qui ne reconstruit plus le panneau.
        // Elle créait au passage une instance Processes de secours que personne
        // n'attendait.

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

            // Une seule notification par relevé, pour l'infobulle de la zone de
            // notification : elle raisonne sur l'ensemble des entrées, pas sur
            // l'une d'elles.
            processes.SampleCompleted -= Processes_SampleCompleted;
            processes.SampleCompleted += Processes_SampleCompleted;
        }

        /// <summary>
        /// Compte les abonnés réellement portés par l'instance courante, et non
        /// les méthodes référencées par les champs de la fenêtre : l'indicateur
        /// affichait 1-1-1-1 alors que l'instance n'en portait aucun. Il détecte
        /// aussi bien la perte que le double abonnement.
        ///
        /// Ces quatre nombres n'apprennent rien à qui se sert de l'application ;
        /// ils sont présentés dans la fenêtre « About », avec ce qu'ils comptent,
        /// et non plus dans une barre où la place manque.
        /// </summary>
        private int[] GetEventSubscriptionCounts()
        {
            return this._processes == null ? null : this._processes.GetEventSubscriberCounts();
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
                processes.SampleCompleted -= Processes_SampleCompleted;

        }

        /// <summary>
        /// Texte courant de l'infobulle de la zone de notification. Sert à ne la
        /// réécrire que lorsqu'elle change réellement : la réécrire à chaque
        /// relevé la fait scintiller, Windows la reconstruisant à chaque appel
        /// de Shell_NotifyIcon.
        /// </summary>
        private string _notifyIconText = string.Empty;

        /// <summary>
        /// Longueur maximale du texte d'un NotifyIcon. WinForms rejette au-delà,
        /// et la limite porte sur la chaîne entière, sauts de ligne compris.
        /// </summary>
        /// <remarks>
        /// 127 et non 63 : la limite de 63 valait pour le shell des premières
        /// versions de Windows. Mesurée à 127 sur .NET 9, ce que la sonde
        /// constate plutôt que de s'y fier.
        /// </remarks>
        private const int NotifyIconTextMaximumLength = 127;

        /// <summary>Nombre de gros consommateurs annoncés dans l'infobulle.</summary>
        private const int NotifyIconTopProcessCount = 3;

        /// <summary>
        /// Proposition de démarrage automatique, posée une fois la fenêtre
        /// affichée. Rien n'est demandé quand l'application n'a pas démarré par
        /// son point d'entrée normal : une sonde qui instancie la fenêtre ne doit
        /// pas se retrouver bloquée sur une modale.
        /// </summary>
        private void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= MainWindowLoaded;

            if (!App.IsInteractiveLaunch)
            {
                return;
            }

            // Évalué dans tous les cas : c'est ce qui aligne le souvenir sur le
            // registre, y compris quand l'entrée a été retirée à la main.
            if (StartupPreference.Evaluate() != StartupPreference.StartupDecision.Ask)
            {
                return;
            }

            StartupPromptWindow prompt = new StartupPromptWindow();
            prompt.Owner = this;

            prompt.ShowDialog();

            string error;

            StartupPreference.TryApplyAnswer(prompt.Accepted, prompt.DoNotAskAgain, out error);

            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(error, "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Processes_SampleCompleted(Process[] processes)
        {
            if (this._isShuttingDown)
            {
                return;
            }

            string text = BuildNotifyIconText(processes);

            // Comparaison faite ici, sur le thread d'échantillonnage : quand rien
            // n'a changé — le cas ordinaire — aucune opération n'est postée.
            if (string.Equals(text, this._notifyIconText, StringComparison.Ordinal))
            {
                return;
            }

            this._notifyIconText = text;

            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (this._processAffinityNotifyIcon != null)
                    {
                        this._processAffinityNotifyIcon.Text = text;
                    }
                }
                catch
                {
                    // Une infobulle refusée ne doit pas faire tomber l'application.
                }
            }));
        }

        /// <summary>
        /// Le nom de l'application, puis les trois plus gros consommateurs de CPU,
        /// l'un sous l'autre. Les noms sont tronqués pour que l'ensemble tienne
        /// dans la limite du NotifyIcon : une chaîne trop longue serait refusée,
        /// et l'infobulle resterait celle du relevé précédent.
        /// </summary>
        internal static string BuildNotifyIconText(Process[] processes)
        {
            StringBuilder builder = new StringBuilder(ApplicationTitle);

            if (processes == null)
            {
                return builder.ToString();
            }

            List<Process> top = processes
                .Where(p => p != null && !p.IsService && p.CPUUsage.HasValue && p.CPUUsage.Value > 0d)
                .OrderByDescending(p => p.CPUUsage.Value)
                .Take(NotifyIconTopProcessCount)
                .ToList();

            foreach (Process process in top)
            {
                // Le pourcentage est rapporté à la machine entière, comme la valeur
                // écrite sur la tuile et comme le Gestionnaire des tâches.
                string percent = process.CPUUsage.Value.ToString("F0") + " %";
                string name = ShortenProcessName(process.ProcessName, NotifyIconNameLength);

                builder.Append("\r\n").Append(name).Append(' ').Append(percent);
            }

            string text = builder.ToString();

            return text.Length <= NotifyIconTextMaximumLength
                ? text
                : text.Substring(0, NotifyIconTextMaximumLength);
        }

        private const string ApplicationTitle = "ProcessAffinity";

        /// <summary>
        /// Place laissée au nom : quinze caractères par ligne au plus, dont cinq
        /// pour le pourcentage et l'espace qui le précède.
        /// </summary>
        private const int NotifyIconNameLength = 20;

        private static string ShortenProcessName(string name, int maximumLength)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "?";
            }

            // L'extension n'apprend rien ici et coûte quatre caractères sur dix.
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            return name.Length <= maximumLength ? name : name.Substring(0, maximumLength - 1) + "…";
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

            // Chaque tuile masquée est imputée au filtre qui l'écarte, et non
            // globalement à celui du cœur : avec trois filtres, un total indistinct
            // ne dirait plus lequel cache quoi.
            List<ProcessUserControl> tiles =
                this.processWrapPanel.Children.OfType<ProcessUserControl>().ToList();

            int hiddenByName = tiles.Count(child => GetHiddenReason(child) == HiddenReason.Name);
            int hiddenByCore = tiles.Count(child => GetHiddenReason(child) == HiddenReason.Core);

            int unaccounted = total - hiddenServices - hiddenByName - hiddenByCore - visible;

            StringBuilder builder = new StringBuilder();

            builder.Append(total).Append(total > 1 ? " entries in total." : " entry in total.");

            if (hiddenByName > 0)
            {
                builder.Append("\r\n").Append(hiddenByName)
                       .Append(" hidden by the name filter \"")
                       .Append(this._nameFilter).Append("\".");
            }

            if (hiddenByCore > 0)
            {
                builder.Append("\r\n").Append(hiddenByCore)
                       .Append(" hidden: not restricted to CPU ")
                       .Append(this.CPUComboBox.SelectedValue).Append(".");
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

            AppendCoreRestrictionBreakdown(builder);

            builder.Append(".");

            return builder.ToString();
        }

        /// <summary>
        /// Sépare ce qui est restreint par l'affinité de ce qui l'est par les CPU
        /// Sets, et dit combien d'entrées restent inconnues. Les confondre ferait
        /// passer une préférence souple pour une contrainte dure, et laisserait
        /// croire que les illisibles ont été jugées.
        /// </summary>
        private void AppendCoreRestrictionBreakdown(StringBuilder builder)
        {
            object selectedValue = this.CPUComboBox.SelectedValue;

            if (selectedValue == null || selectedValue.ToString() == "ALL")
            {
                return;
            }

            int coreNumber = this.CPUComboBox.SelectedIndex;

            int byAffinity = 0;
            int byCpuSets = 0;
            int unknown = 0;

            foreach (ProcessUserControl child in this.processWrapPanel.Children.OfType<ProcessUserControl>())
            {
                switch (GetCoreRestriction(child.Process, coreNumber))
                {
                    case CoreRestriction.Affinity:
                        byAffinity++;
                        break;
                    case CoreRestriction.CpuSets:
                        byCpuSets++;
                        break;
                    case CoreRestriction.Unknown:
                        unknown++;
                        break;
                }
            }

            if (byAffinity > 0 || byCpuSets > 0)
            {
                builder.Append(", of which ").Append(byAffinity)
                       .Append(" restricted by affinity and ").Append(byCpuSets)
                       .Append(" by CPU sets");
            }

            if (unknown > 0)
            {
                builder.Append(".\r\n").Append(unknown)
                       .Append(unknown > 1 ? " entries have" : " entry has")
                       .Append(" an unreadable affinity, so it cannot be told whether they are restricted");
            }
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

        /// <summary>
        /// Ce qui restreint un processus à un cœur donné.
        /// </summary>
        internal enum CoreRestriction
        {
            /// <summary>Rien : il tourne partout, ailleurs, ou on ne sait pas.</summary>
            None,

            /// <summary>Affinité dure : son masque ne couvre pas tous les processeurs.</summary>
            Affinity,

            /// <summary>Préférence de CPU Sets, l'affinité pouvant rester complète.</summary>
            CpuSets,

            /// <summary>Affinité illisible : ni restreint ni non restreint, inconnu.</summary>
            Unknown,
        }

        /// <summary>
        /// Le filtre ne montre plus ce qui est <em>autorisé</em> sur le cœur choisi,
        /// mais ce qui y est <em>restreint</em>. Presque tous les processus sont
        /// autorisés partout : l'ancien critère ne retirait quasiment rien, et
        /// l'usage visé — voir qui est affecté à ce cœur — se perdait dans la masse.
        ///
        /// Les CPU Sets comptent au même titre : un processus dont les CPU Sets
        /// désignent ce cœur y est bien cantonné, même si son affinité reste
        /// complète.
        /// </summary>
        internal static CoreRestriction GetCoreRestriction(Process process, int coreNumber)
        {
            if (process == null || coreNumber < 0 || coreNumber >= IntPtr.Size * 8)
            {
                return CoreRestriction.None;
            }

            nuint bit = (nuint)1 << coreNumber;
            nuint allProcessors = RuleEngine.GetAllProcessorsMask();

            nuint? affinity = process.GetProcessorAffinity();

            if (affinity != null)
            {
                nuint mask = affinity.Value & allProcessors;

                // Restreint, et restreint à un ensemble qui contient ce cœur. Un
                // masque complet n'apprend rien : c'est l'état par défaut.
                if (mask != allProcessors && (mask & bit) != 0)
                {
                    return CoreRestriction.Affinity;
                }
            }

            int[] cpuSetProcessors = RuleEngine.GetCpuSetProcessors(process);

            // Un tableau vide vaut « aucune préférence », et un tableau couvrant
            // tous les processeurs ne restreint rien non plus.
            if (cpuSetProcessors != null
                && cpuSetProcessors.Length > 0
                && cpuSetProcessors.Length < Environment.ProcessorCount
                && Array.IndexOf(cpuSetProcessors, coreNumber) >= 0)
            {
                return CoreRestriction.CpuSets;
            }

            // L'affinité illisible est signalée en dernier : un processus dont les
            // CPU Sets sont lisibles et concluants n'a pas à être rangé parmi les
            // inconnus.
            return affinity == null ? CoreRestriction.Unknown : CoreRestriction.None;
        }

        /// <summary>
        /// Ce qui écarte une tuile de l'affichage, ou <see cref="HiddenReason.None"/>
        /// quand rien ne l'écarte. Les motifs sont distingués pour que la
        /// ventilation du compteur puisse imputer chaque tuile manquante au bon
        /// filtre, et non les confondre toutes dans celui du cœur.
        /// </summary>
        private enum HiddenReason
        {
            None,
            Name,
            Core,
        }

        private HiddenReason GetHiddenReason(ProcessUserControl processUserControl)
        {
            if (processUserControl == null || processUserControl.Process == null)
            {
                return HiddenReason.None;
            }

            // Le filtre de nom d'abord : c'est le geste le plus récent de
            // l'utilisateur, et celui auquel il attribuera l'absence d'une tuile.
            if (!MatchesNameFilter(processUserControl.Process))
            {
                return HiddenReason.Name;
            }

            object selectedValue = CPUComboBox.SelectedValue;

            // Sélection nulle pendant une reconstruction de la liste : le filtre
            // par cœur ne s'applique pas.
            if (selectedValue == null || selectedValue.ToString() == "ALL")
            {
                return HiddenReason.None;
            }

            // Les cœurs occupent les indices 0 à N-1, « ALL » étant ajouté en
            // dernier : le numéro de cœur est l'indice lui-même. Seul un processus
            // restreint à ce cœur y figure — c'est le critère de l'étape 4c.
            CoreRestriction restriction =
                GetCoreRestriction(processUserControl.Process, CPUComboBox.SelectedIndex);

            return restriction == CoreRestriction.Affinity || restriction == CoreRestriction.CpuSets
                ? HiddenReason.None
                : HiddenReason.Core;
        }

        /// <summary>
        /// Filtre par fragment, sans distinction de casse : saisir « affi » montre
        /// « ProcessAffinity », où le fragment n'est pas en tête. Un filtre vide
        /// laisse tout passer.
        /// </summary>
        private bool MatchesNameFilter(Process process)
        {
            string filter = this._nameFilter;

            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            string name = process.ProcessName;

            return name != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetProcessUserControlVisibility(ProcessUserControl processUserControl)
        {
            processUserControl.Visibility = GetHiddenReason(processUserControl) == HiddenReason.None
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// Texte du filtre, mémorisé hors du contrôle : il est lu depuis la boucle
        /// de visibilité, appelée pour chaque tuile à chaque relevé.
        /// </summary>
        private string _nameFilter = string.Empty;

        private void NameFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Rafraîchissement à la frappe : aucun bouton à valider.
            this._nameFilter = this.NameFilterTextBox.Text == null
                ? string.Empty
                : this.NameFilterTextBox.Text.Trim();

            this.ClearNameFilterButton.Visibility = this._nameFilter.Length == 0
                ? Visibility.Collapsed
                : Visibility.Visible;

            this.RefreshProcessUserControlsVisibility();
        }

        private void ClearNameFilterButton_Click(object sender, RoutedEventArgs e)
        {
            this.NameFilterTextBox.Text = string.Empty;
            this.NameFilterTextBox.Focus();
        }

        /// <summary>
        /// Réapplique les filtres à toutes les tuiles, sans toucher aux couleurs ni
        /// aux courbes : le filtre ne change que ce qui est montré.
        /// </summary>
        private void RefreshProcessUserControlsVisibility()
        {
            foreach (ProcessUserControl processUserControl in
                     this.processWrapPanel.Children.OfType<ProcessUserControl>())
            {
                SetProcessUserControlVisibility(processUserControl);
            }

            SetCounters();
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

            // La configuration ne s'offre que sur la sélection, jamais sur tout le
            // panneau : « enregistrer » sur trois cents processus écrirait trois
            // cents règles d'un geste, sans commune mesure avec les autres actions
            // de masse, qui sont réversibles et ne laissent rien derrière elles.
            List<Process> selected = ProcessUserControl.GetSelectedProcesses();

            if (selected.Count > 0)
            {
                menu.Items.Add(new Separator());

                menuItem = new MenuItem();
                menuItem.Header = SaveSelectedConfigurationHeader + " (" + selected.Count + " selected)";
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

                if (ConfigurationCommands.GetRuledTargets(selected).Count > 0)
                {
                    menuItem = new MenuItem();
                    menuItem.Header = RemoveSelectedConfigurationHeader + " (" + selected.Count + " selected)";
                    ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
                }
            }

            this.ContextMenu = menu;
        }

        private const string SaveSelectedConfigurationHeader = "Save configuration for selected processes";
        private const string RemoveSelectedConfigurationHeader = "Remove configuration for selected processes";

        /// <summary>
        /// Une règle porte un chemin d'exécutable : quand la sélection tient
        /// plusieurs instances du même programme réglées différemment, aucune ne
        /// s'impose, et en retenir une au hasard graverait une configuration que
        /// personne n'a désignée.
        /// </summary>
        private ConfigurationCommands.Candidate AskWhichConfiguration(
            string processName, List<ConfigurationCommands.Candidate> candidates)
        {
            ConfigurationChoiceWindow choiceWindow = new ConfigurationChoiceWindow(processName, candidates);

            choiceWindow.Owner = this;
            choiceWindow.ShowDialog();

            return choiceWindow.Chosen;
        }
        
        private delegate void ProcessesEventArrivedDelegate(Process process);

        private void ProcessUserControlContextMenuClick(object sender, RoutedEventArgs e)
        {
            string header = ((MenuItem)e.OriginalSource).Header.ToString();

            // Ces deux entrées portent leur portée dans leur libellé, comme celles
            // de la tuile : leur en-tête n'est donc pas une constante.
            if (header.StartsWith(SaveSelectedConfigurationHeader, StringComparison.Ordinal))
            {
                ConfigurationCommands.Save(ProcessUserControl.GetSelectedProcesses());
                return;
            }

            if (header.StartsWith(RemoveSelectedConfigurationHeader, StringComparison.Ordinal))
            {
                ConfigurationCommands.Remove(ProcessUserControl.GetSelectedProcesses());
                return;
            }

            switch (header)
            {
                case "Affinity all processes":
                    ProcessAffinityWindow processAffinityWindow = new ProcessAffinityWindow(GetPanelProcesses());
                    processAffinityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;
                case "Priority all processes":
                    ProcessPriorityWindow processPriorityWindow = new ProcessPriorityWindow(GetPanelProcesses());
                    processPriorityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;
                case "Priority selected processes":
                    ProcessPriorityWindow selectedProcessPriorityWindow = new ProcessPriorityWindow(ProcessUserControl.GetSelectedProcesses());
                    selectedProcessPriorityWindow.ShowDialog();
                    RefreshProcessUserControls();
                    break;

                case "Affinity selected processes":
                    ProcessAffinityWindow selectedProcessAffinityWindow = new ProcessAffinityWindow(ProcessUserControl.GetSelectedProcesses());
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

        /// <summary>
        /// Entrées effectivement portées par le panneau, services exclus : ce sont
        /// elles que visent les actions « all processes », et non toute la liste.
        /// </summary>
        private List<Process> GetPanelProcesses()
        {
            List<Process> entries = new List<Process>();

            foreach (ProcessUserControl processUserControl in this.processWrapPanel.Children.OfType<ProcessUserControl>())
            {
                if (processUserControl.Process != null && !processUserControl.Process.IsService)
                {
                    entries.Add(processUserControl.Process);
                }
            }

            return entries;
        }

        /// <summary>
        /// Clic gauche dans le panneau. Un clic sur une tuile remonte jusqu'ici en
        /// bouillonnant : on ne vide la sélection que si la source d'origine
        /// n'appartient à aucune tuile, c'est-à-dire si le clic a porté dans le
        /// vide.
        /// </summary>
        private void ProcessWrapPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindTile(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            ClearSelection();
        }

        /// <summary>
        /// Remonte l'arbre visuel à la recherche de la tuile qui contient
        /// l'élément cliqué, ou null s'il n'y en a pas.
        /// </summary>
        private static ProcessUserControl FindTile(DependencyObject element)
        {
            while (element != null)
            {
                ProcessUserControl tile = element as ProcessUserControl;

                if (tile != null)
                {
                    return tile;
                }

                // GetParent de l'arbre visuel lève sur ce qui n'est pas un Visual
                // — un Run de texte, par exemple : on repasse alors par l'arbre
                // logique.
                element = element is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return null;
        }

        /// <summary>
        /// Reporte la sélection d'un jeu d'entrées sur le suivant. L'identité est
        /// celle d'IsSameEntry : le PID seul confondrait un service avec son hôte,
        /// et un PID recyclé avec le processus qu'il remplace.
        /// </summary>
        private static void CarrySelectionOver(Processes previous, Processes current)
        {
            if (previous == null || current == null)
            {
                return;
            }

            List<Process> selected = new List<Process>();

            foreach (Process process in previous.Snapshot())
            {
                if (process.IsSelected)
                {
                    selected.Add(process);
                }
            }

            if (selected.Count == 0)
            {
                return;
            }

            foreach (Process process in current.Snapshot())
            {
                foreach (Process previouslySelected in selected)
                {
                    if (process.IsSameEntry(previouslySelected))
                    {
                        process.IsSelected = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Décoche tout et rafraîchit les tuiles. Appelée depuis le menu du
        /// panneau comme depuis celui d'une tuile.
        /// </summary>
        private void ClearSelection()
        {
            if (this._processes != null)
            {
                foreach (Process process in this._processes.Snapshot())
                {
                    process.IsSelected = false;
                }
            }

            foreach (ProcessUserControl processUserControl in this.processWrapPanel.Children.OfType<ProcessUserControl>())
            {
                processUserControl.IsSelected = false;
            }

            SetCounters();
        }

        private void SelectProcesses()
        {
            IEnumerable<ProcessUserControl> processUserControls = from child in this.processWrapPanel.Children.OfType<ProcessUserControl>()
                                                                  select child;

            foreach (ProcessUserControl processUserControl in processUserControls)
            {
                processUserControl.IsSelected = true;
            }

            SetCounters();
        }

        private void UnselectProcesses()
        {
            // Une seule implémentation : ClearSelection décoche aussi les entrées
            // qui n'ont pas de tuile au moment du geste.
            ClearSelection();
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
            if (this._isShuttingDown)
            {
                return;
            }


            ProcessesEventArrivedDelegate processesEventArrivedDelegate = new ProcessesEventArrivedDelegate(SetCounters);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesEventArrivedDelegate, new object[] { e.Process });

        }

        private void Processes_ProcessCreated(object sender, ProcessEventArgs e)
        {
            if (this._isShuttingDown)
            {
                return;
            }

            //this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() => this.processWrapPanel.Children.Add(new ProcessUserControl(process))), new object[] { });

            ProcessesEventArrivedDelegate processesCreatedDelegate = new ProcessesEventArrivedDelegate(CreateProcessUserControl);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesCreatedDelegate, new object[] {e.Process});

        }

        private void Processes_ProcessDeleted(object sender, ProcessEventArgs e)
        {
            if (this._isShuttingDown)
            {
                return;
            }

            ProcessesEventArrivedDelegate processesDeletedDelegate = new ProcessesEventArrivedDelegate(RemoveProcessUserControl);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesDeletedDelegate, new object[] {e.Process});

        }

        private void Processes_ProcessModified(object sender, ProcessEventArgs e)
        {
            if (this._isShuttingDown)
            {
                return;
            }

            ProcessesEventArrivedDelegate processesModifiedDelegate = new ProcessesEventArrivedDelegate(ModifyProcessUserControl);
            this.processWrapPanel.Dispatcher.BeginInvoke(processesModifiedDelegate, new object[] {e.Process});
        }

        private void SetCounters(Process process)
        {
            // Operation postee avant la fermeture, executee apres.
            if (this._isShuttingDown)
            {
                return;
            }

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

            SetSelectedCounter();
            SetFailedRulesCounter();
            SetCoreFilterToolTip();
        }

        /// <summary>
        /// Ce que le filtre par cœur montre, sur la liste déroulante elle-même :
        /// c'est là qu'on se pose la question, et la distinction entre contrainte
        /// dure et préférence de CPU Sets s'y lit sans parcourir les tuiles.
        /// </summary>
        private void SetCoreFilterToolTip()
        {
            object selectedValue = this.CPUComboBox.SelectedValue;

            if (selectedValue == null || selectedValue.ToString() == "ALL")
            {
                this.CPUComboBox.ToolTip =
                    "Pick a CPU to show only the processes restricted to it, "
                    + "either by a hard affinity or by a CPU Sets preference.";

                return;
            }

            int coreNumber = this.CPUComboBox.SelectedIndex;

            int byAffinity = 0;
            int byCpuSets = 0;
            int unknown = 0;

            foreach (ProcessUserControl child in this.processWrapPanel.Children.OfType<ProcessUserControl>())
            {
                switch (GetCoreRestriction(child.Process, coreNumber))
                {
                    case CoreRestriction.Affinity:
                        byAffinity++;
                        break;
                    case CoreRestriction.CpuSets:
                        byCpuSets++;
                        break;
                    case CoreRestriction.Unknown:
                        unknown++;
                        break;
                }
            }

            StringBuilder builder = new StringBuilder();

            builder.Append("Processes restricted to CPU ").Append(selectedValue).Append(":")
                   .Append("\r\n").Append(byAffinity).Append(" by a hard affinity")
                   .Append("\r\n").Append(byCpuSets).Append(" by a CPU Sets preference, affinity left whole");

            if (unknown > 0)
            {
                builder.Append("\r\n\r\n").Append(unknown)
                       .Append(unknown > 1 ? " entries have" : " entry has")
                       .Append(" an unreadable affinity and cannot be judged either way.")
                       .Append("\r\nRunning ProcessAffinity as administrator usually makes them readable.");
            }

            this.CPUComboBox.ToolTip = builder.ToString();
        }

        /// <summary>
        /// Nombre de tuiles cochées, affiché seulement s'il y en a. Les actions du
        /// menu contextuel portant sur la sélection dès qu'elle n'est pas vide,
        /// l'utilisateur doit pouvoir le constater sans parcourir le panneau.
        /// </summary>
        private void SetSelectedCounter()
        {
            int selectedCount = ProcessUserControl.GetSelectedProcesses().Count;

            if (selectedCount == 0)
            {
                this.SelectedCountLabel.Visibility = Visibility.Collapsed;
                this.SelectedCountLabel.Content = string.Empty;

                return;
            }

            this.SelectedCountLabel.Visibility = Visibility.Visible;
            this.SelectedCountLabel.Content = "Selected: " + selectedCount.ToString();
        }

        /// <summary>
        /// Applique les règles enregistrées aux processus déjà en cours, puis
        /// rafraîchit le marquage des tuiles et le décompte des échecs. Signale une
        /// fois le fichier illisible, s'il l'est.
        /// </summary>
        private void ApplyRulesToExistingProcesses(Processes processes)
        {
            if (RuleEngine.IsDisabled)
            {
                return;
            }

            RuleEngine.EnsureLoaded();

            if (!string.IsNullOrEmpty(RuleEngine.LoadError) && !this._ruleLoadErrorReported)
            {
                this._ruleLoadErrorReported = true;

                MessageBox.Show(RuleEngine.LoadError, "ProcessAffinity",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (RuleEngine.Count == 0)
            {
                return;
            }

            processes.ApplyRulesToExistingProcesses();

            // Laisse le temps à l'application des règles avant de relire l'état.
            // Les tuiles, elles, se remettent à jour d'elles-mêmes à chaque relevé.
            System.Threading.Tasks.Task.Delay(2500).ContinueWith(task =>
            {
                this.processWrapPanel.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (this._isShuttingDown)
                    {
                        return;
                    }

                    RefreshProcessUserControls();
                }));
            });
        }

        /// <summary>
        /// Décompte des règles qui n'ont pas abouti, affiché seulement s'il y en a.
        /// Tout ce qui n'est pas « appliquée et confirmée » compte : une règle
        /// contestée par Windows n'a pas fait ce que l'utilisateur avait demandé.
        /// </summary>
        private void SetFailedRulesCounter()
        {
            int failedCount = 0;

            if (this._processes != null)
            {
                foreach (Process process in this._processes.Snapshot())
                {
                    if (process.RuleState != RuleStateEnum.None && process.RuleState != RuleStateEnum.Applied)
                    {
                        failedCount++;
                    }
                }
            }

            if (failedCount == 0)
            {
                this.FailedRulesLabel.Visibility = Visibility.Collapsed;
                this.FailedRulesLabel.Content = string.Empty;

                return;
            }

            this.FailedRulesLabel.Visibility = Visibility.Visible;
            this.FailedRulesLabel.Content = "Rules failed: " + failedCount.ToString();
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
            // Operation postee avant la fermeture, executee apres.
            if (this._isShuttingDown)
            {
                return;
            }

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
                    InsertProcessUserControl(new ProcessUserControl(process), process);
                //}
            }
        }

        /// <summary>
        /// Insère la tuile à sa place alphabétique, dans son bloc — les processus
        /// d'abord, les services ensuite, comme au chargement. Elle était ajoutée
        /// en fin de panneau : une application lancée après le chargement se
        /// retrouvait à plusieurs centaines de positions de l'endroit où on la
        /// cherche, et passait pour absente.
        /// </summary>
        private void InsertProcessUserControl(ProcessUserControl processUserControl, Process process)
        {
            int index = this.processWrapPanel.Children.Count;
            bool blockEntered = false;

            for (int i = 0; i < this.processWrapPanel.Children.Count; i++)
            {
                ProcessUserControl current = this.processWrapPanel.Children[i] as ProcessUserControl;

                if (current == null || current.Process == null)
                {
                    continue;
                }

                if (current.Process.IsService != process.IsService)
                {
                    // Sortie du bloc : la tuile se place juste avant ce qui suit.
                    if (blockEntered)
                    {
                        index = i;
                        break;
                    }

                    continue;
                }

                blockEntered = true;

                if (string.Compare(current.Process.ProcessName, process.ProcessName) > 0)
                {
                    index = i;
                    break;
                }
            }

            this.processWrapPanel.Children.Insert(index, processUserControl);

            // Les filtres s'appliquent aussi à ce qui apparaît après le
            // chargement. Sans cela, une application lancée pendant qu'un filtre
            // est posé s'affichait quand même : la tuile naissait visible, et rien
            // ne la soumettait au filtre avant le relevé suivant.
            SetProcessUserControlVisibility(processUserControl);

            // Et le compteur doit suivre : la tuile compte pour le total, et pour
            // l'affichage seulement si elle passe les filtres.
            SetCounters();
        }

        private void ModifyProcessUserControl(Process process)
        {
            // Operation postee avant la fermeture, executee apres.
            if (this._isShuttingDown)
            {
                return;
            }

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
            // Operation postee avant la fermeture, executee apres.
            if (this._isShuttingDown)
            {
                return;
            }

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

                // Un rechargement construit de nouvelles entrées : la sélection,
                // portée par les anciennes, serait perdue. On la reporte sur les
                // entrées équivalentes — même type, même PID, même nom.
                CarrySelectionOver(this._processes, processes);

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

                    // Règles appliquées aux processus déjà en cours, en tâche de
                    // fond : les tuiles sont déjà à l'écran, elles se remettront à
                    // jour au relevé suivant.
                    this.ApplyRulesToExistingProcesses(processes);
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

        /// <summary>
        /// Demande, plutôt que de décider. L'application applique des règles tant
        /// qu'elle tourne : quitter et ranger dans la zone de notification n'ont
        /// pas les mêmes conséquences, et le bouton les confondait.
        ///
        /// La croix de la barre de titre ne passe pas par ici et garde son
        /// comportement : elle quitte.
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseChoice? remembered = SettingsStore.GetRememberedCloseChoice();

            if (remembered != null)
            {
                ApplyCloseChoice(remembered.Value);
                return;
            }

            CloseConfirmationWindow confirmation = new CloseConfirmationWindow();
            confirmation.Owner = this;

            confirmation.ShowDialog();

            if (confirmation.ShouldRemember)
            {
                string error;

                if (!SettingsStore.TryRememberCloseChoice(confirmation.Choice, out error))
                {
                    // Ne pas laisser croire qu'on se souviendra d'un choix qui n'a
                    // pas pu être écrit : la question reviendrait sans explication.
                    MessageBox.Show(
                        error + "\r\n\r\nThe question will be asked again next time.",
                        "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            ApplyCloseChoice(confirmation.Choice);
        }

        private void ApplyCloseChoice(CloseChoice choice)
        {
            switch (choice)
            {
                case CloseChoice.Exit:
                    this.Close();
                    break;

                case CloseChoice.Minimize:
                    // Le même chemin que la réduction ordinaire : WindowStateChanged
                    // se charge de l'icône et de l'arrêt de l'affichage.
                    this.WindowState = WindowState.Minimized;
                    break;
            }
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow(this.GetEventSubscriptionCounts());

            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            this.StopBackgroundWork();
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            this.StopBackgroundWork();
        }

        /// <summary>
        /// Coupe les deux producteurs d'événements — échantillonnage du % CPU et
        /// matérialisation — puis détache les gestionnaires, avant que
        /// l'Application n'entre en fermeture. Sans cela une tuile pouvait encore
        /// être créée pendant l'arrêt, et <c>Application.LoadComponent</c> lever
        /// un « objet Application en cours de fermeture ». Idempotent.
        /// </summary>
        private void StopBackgroundWork()
        {
            this._isShuttingDown = true;

            if (this._processes != null)
            {
                this._processes.StopCPUSampling();
            }

            if (this._services != null)
            {
                this._services.StopCPUSampling();
            }

            // Détacher après avoir arrêté : un événement déjà en vol n'atteint
            // plus le dispatcher.
            this.UnsubscribeProcessEventHandlers(this._processes);
            this.UnsubscribeProcessEventHandlers(this._services);
        }

        /// <summary>
        /// Bascule des deux courbes. Rien n'est reconstruit : les tuiles existent
        /// déjà et leur historique n'a pas cessé de glisser, il suffit de les
        /// repeindre.
        /// </summary>
        private void BarsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // La case CPU porte IsChecked="True" dans le XAML, ce qui lève Checked
            // pendant le chargement — avant que la seconde case n'existe. Sans ce
            // garde, l'application tombait sur une NullReferenceException au
            // démarrage, avant même d'afficher sa fenêtre.
            if (this.ShowCpuBarsCheckBox == null || this.ShowMemoryBarsCheckBox == null)
            {
                return;
            }

            ProcessUserControl.AreCpuBarsShown = this.ShowCpuBarsCheckBox.IsChecked == true;
            ProcessUserControl.AreMemoryBarsShown = this.ShowMemoryBarsCheckBox.IsChecked == true;

            this.RefreshProcessUserControlsDisplay();
        }

        private void ShowServicesCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this._processes != null)
            {
                this.InitializeProcessWrapPanel(this._processes);
            }
        }

        /// <summary>
        /// Réécrit chaque tuile depuis son historique en mémoire, celui-ci ayant
        /// continué de glisser pendant la réduction. La courbe est donc continue
        /// au retour : elle couvre la période masquée.
        /// </summary>
        private void RefreshProcessUserControlsDisplay()
        {
            foreach (ProcessUserControl processUserControl in this.processWrapPanel.Children.OfType<ProcessUserControl>())
            {
                processUserControl.RefreshDisplay();
            }

            SetCounters();
        }

        private void ProcessAffinityNotifyIconMouseDoubleClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.WindowState = WindowState.Normal;
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                // Seul l'affichage s'arrête. Le suivi continue à l'identique :
                // liste, créations, suppressions, échantillonnage et balayage des
                // services.
                //
                // Le panneau n'est plus vidé. Le vider ne libérait rien — les
                // délégués de notification maintenaient les tuiles en vie, et
                // elles continuaient de poster onze mille opérations par seconde
                // hors de l'arbre visuel — et sa reconstruction coûtait près de
                // cinq secondes au retour.
                ProcessUserControl.IsDisplaySuspended = true;

                this.ShowInTaskbar = false;
                _processAffinityNotifyIcon.BalloonTipTitle = "ProcessAffinity minimized Sucessfully";
                _processAffinityNotifyIcon.BalloonTipText = "ProcessAffinity";
                _processAffinityNotifyIcon.ShowBalloonTip(400);
                _processAffinityNotifyIcon.Visible = true;

                return;
            }

            // Toute sortie de la réduction relance l'affichage, agrandie comme
            // normale. Ne traiter que Normal laissait les tuiles figées au retour
            // d'une fenêtre agrandie — et une fenêtre agrandie avant la réduction
            // revient agrandie.
            ProcessUserControl.IsDisplaySuspended = false;

            _processAffinityNotifyIcon.Visible = false;
            this.ShowInTaskbar = true;

            // Remise à niveau directe, sur le thread de l'IHM où l'on se
            // trouve déjà : aucune opération n'est poussée, donc aucune
            // rafale. Les tuiles sont restées en place et le panneau a suivi
            // les créations et les suppressions pendant la réduction — il n'y
            // a ni reconstruction, ni décalage à rattraper.
            this.RefreshProcessUserControlsDisplay();
        }

        
    }
}
