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
using ProcessAffinityUI.Configuration;
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

        /// <summary>Entrees de menu des regles, utilisees a la construction et a l aiguillage.</summary>
        private const string SaveConfigurationHeader = "Save configuration";
        private const string RemoveConfigurationHeader = "Remove configuration";

        /// <summary>
        /// Entrées dont l'en-tête porte la portée entre parenthèses quand la
        /// sélection n'est pas vide.
        /// </summary>
        private const string PriorityHeader = "Priority";
        private const string AffinityHeader = "Affinity";
        private const string ClearSelectionHeader = "Clear selection";
        private const string EfficiencyModeHeader = "Efficiency mode";

        /// <summary>Hauteur de l'emplacement d'une barre, en pixels (cf. XAML).</summary>
        private const double CPUUsageBarHeight = 56d;

        /// <summary>Fond du bandeau de nom des entrées non modifiables.</summary>
        private static readonly Brush NotModifiableNameBackgroundBrush =
            new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

        /// <summary>
        /// Fond du bandeau de nom d'une entrée sous règle appliquée. Même principe
        /// que le gris : la couleur porte l'information, le reste de la tuile est
        /// inchangé. La police passe en blanc par le calcul de luminance.
        /// </summary>
        private static readonly Brush RuledNameBackgroundBrush =
            new SolidColorBrush(Color.FromRgb(0x1F, 0x5C, 0x99));

        /// <summary>Fond du bandeau de nom d'une règle en échec.</summary>
        private static readonly Brush FailedRuleNameBackgroundBrush =
            new SolidColorBrush(Color.FromRgb(0xA5, 0x32, 0x2A));

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

        /// <summary>Nombre d'étiquettes empilées formant la courbe (cf. XAML).</summary>
        private const int CPUUsageBarCount = 17;

        /// <summary>
        /// Affichage suspendu : les tuiles continuent de faire glisser leur
        /// historique en mémoire, mais ne poussent plus rien sur le dispatcher.
        /// Levé quand la fenêtre passe en zone de notification. Il n'y a qu'une
        /// fenêtre, donc l'état est global.
        /// </summary>
        public static bool IsDisplaySuspended { get; set; }

        /// <summary>
        /// Ce que les tuiles tracent. Posé par les cases de la barre du haut, donc
        /// global comme la suspension d'affichage : il n'y a qu'une fenêtre.
        ///
        /// L'historique continue d'être décalé en mémoire dans les deux cas : une
        /// courbe rallumée doit repartir continue, et non d'un trou.
        /// </summary>
        public static bool AreCpuBarsShown { get; set; } = true;

        public static bool AreMemoryBarsShown { get; set; } = false;

        /// <summary>
        /// Historique glissant, du plus récent au plus ancien. Il vit ici et non
        /// dans les étiquettes : c'est ce qui permet de continuer à le décaler
        /// quand l'affichage est suspendu, et de retrouver une courbe continue
        /// à la restauration plutôt qu'un trou.
        ///
        /// Écrit par le thread d'échantillonnage, lu par celui de l'IHM, sans
        /// verrou : au pire une barre d'un relevé se mélange au suivant, ce qui
        /// ne se voit pas sur cinq pixels de large.
        /// </summary>
        private readonly double[] _barHeights = new double[CPUUsageBarCount];
        private readonly Brush[] _barBrushes = new Brush[CPUUsageBarCount];
        private string _displayedValue = "-";

        /// <summary>
        /// Historique de la mémoire, décalé dans la même passe que celui du CPU.
        /// Un seul pinceau suffit, partagé par toutes les tuiles : les barres ne
        /// portent aucune information par la teinte, seulement par la hauteur.
        /// </summary>
        private readonly double[] _memoryBarHeights = new double[CPUUsageBarCount];
        private long _memoryBytes;

        /// <summary>
        /// À peine plus foncé que le fond de la tuile, qui est LightGray
        /// (#D3D3D3). Les barres de mémoire sont un arrière-plan : elles doivent
        /// se lire sans disputer la lecture aux barres de CPU, qui passent devant
        /// et portent l'information vive.
        /// </summary>
        private static readonly Brush MemoryBarBrush = CreateFrozenBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));

        private static Brush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();

            return brush;
        }

        /// <summary>Étiquettes de la courbe, de la plus récente à la plus ancienne.</summary>
        private Label[] _bars = null;
        private Label[] _memoryBars = null;

        /// <summary>
        /// Pinceaux de la courbe, indexés par pourcentage. Sans ce cache, le
        /// rendu allouait un pinceau par barre et par relevé, soit plus de cinq
        /// mille par seconde.
        /// </summary>
        private static readonly Brush[] BarBrushesByPercent = new Brush[101];


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
            this.RestoreSelection(process);

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
        /// Fond du bandeau de nom.
        ///
        /// Le jaune garde son rôle de clignotement : il signale qu'il arrive
        /// quelque chose pour ce processus, et il signale aussi l'instant où une
        /// règle vient de lui être appliquée.
        ///
        /// Mais il ne recouvre pas le marquage d'une entrée sous règle : une tuile
        /// sous règle doit rester identifiable d'un coup d'œil, or le clignotement
        /// la repeindrait une seconde sur deux. Sur une tuile sous règle, le jaune
        /// n'apparaît donc qu'au moment de l'application.
        /// </summary>
        private Brush GetProcessNameBackgroundBrush()
        {
            if (this._process == null)
            {
                return Brushes.White;
            }

            if (this._process.ConsumeRuleJustApplied())
            {
                return Brushes.Yellow;
            }

            if (this._process.RuleState != RuleStateEnum.None)
            {
                return GetRestingProcessNameBackgroundBrush();
            }

            if (this._process.HasRecentActivity)
            {
                return Brushes.Yellow;
            }

            return GetRestingProcessNameBackgroundBrush();
        }

        /// <summary>
        /// Couleur de repos, une fois le bandeau éteint. L'état de la règle prime
        /// sur le marquage des entrées non modifiables : une règle refusée dit déjà
        /// qu'il manque des droits, et le dit plus précisément.
        /// </summary>
        private Brush GetRestingProcessNameBackgroundBrush()
        {
            if (this._process != null)
            {
                switch (this._process.RuleState)
                {
                    case RuleStateEnum.Applied:
                        return RuledNameBackgroundBrush;

                    case RuleStateEnum.Contested:
                    case RuleStateEnum.Denied:
                    case RuleStateEnum.Orphan:
                    case RuleStateEnum.InvalidMask:
                        return FailedRuleNameBackgroundBrush;
                }

                if (!this._process.IsModifiable)
                {
                    return NotModifiableNameBackgroundBrush;
                }
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

        /// <summary>
        /// Icônes déjà extraites, par chemin d'exécutable. Un même binaire porte
        /// souvent des dizaines de processus — vingt onglets de navigateur, autant
        /// d'hôtes de services : sans ce cache, l'extraction était refaite pour
        /// chacun, au prix d'environ une seconde au chargement. Les valeurs sont
        /// gelées, donc partageables entre tuiles. Un chemin illisible est retenu
        /// aussi, sous la forme d'un null, pour ne pas être retenté.
        /// </summary>
        private static readonly Dictionary<string, ImageSource> IconsByExecutablePath =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public void SetIcon(Process process)
        {
            string executablePath = process.ExecutablePath;

            // Vide sur les services et sur les processus dont le chemin n'est pas
            // lisible sans élévation : la tuile reste sans icône.
            if (string.IsNullOrEmpty(executablePath))
            {
                return;
            }

            ImageSource imageSource;

            if (!IconsByExecutablePath.TryGetValue(executablePath, out imageSource))
            {
                try
                {
                    imageSource = ConvertToImageSource(System.Drawing.Icon.ExtractAssociatedIcon(executablePath)); //new System.Windows.Media.ImageBrush(ConvertToImageSource(System.Drawing.Icon.ExtractAssociatedIcon(process.InnerProcess.MainModule.FileName)));

                    if (imageSource != null && imageSource.CanFreeze)
                    {
                        imageSource.Freeze();
                    }
                }
                catch
                {
                    imageSource = null;
                    //this.ProcessImage.Visibility = System.Windows.Visibility.Hidden;
                }

                IconsByExecutablePath[executablePath] = imageSource;
            }

            if (imageSource != null)
            {
                this.ProcessImage.Source = imageSource;
            }
        }

        /// <summary>
        /// L'état est celui de l'entrée, la case n'en est que le reflet.
        /// </summary>
        public bool IsSelected
        {
            get { return this._process != null && this._process.IsSelected; }
            set { SetSelected(value); }
        }

        /// <summary>
        /// Rend les entrées du panneau. Posé par la fenêtre principale : la tuile
        /// a besoin de raisonner sur la sélection entière pour savoir sur quoi
        /// portent les actions de son menu, sans pour autant connaître la fenêtre.
        /// </summary>
        public static Func<Process[]> SelectionSource { get; set; }

        /// <summary>
        /// Entrées cochées, vivantes, sans doublon de PID. Une entrée dont le
        /// processus s'est terminé a quitté la liste : elle disparaît donc d'elle-
        /// même de la sélection.
        /// </summary>
        public static List<Process> GetSelectedProcesses()
        {
            List<Process> selected = new List<Process>();

            Func<Process[]> source = SelectionSource;

            if (source == null)
            {
                return selected;
            }

            HashSet<int> seenProcessIDs = new HashSet<int>();

            foreach (Process process in source())
            {
                if (process == null || !process.IsSelected || process.IsService)
                {
                    continue;
                }

                // Deux entrées ne peuvent porter le même PID qu'en passant par un
                // service ; la garde reste, l'action ne devant jamais s'appliquer
                // deux fois au même processus.
                if (!seenProcessIDs.Add(process.ProcessID))
                {
                    continue;
                }

                selected.Add(process);
            }

            return selected;
        }

        /// <summary>
        /// Étiquettes de la courbe, résolues une fois. La plus récente est
        /// CPUUsagelabel0.
        /// </summary>
        private Label[] GetBars()
        {
            if (this._bars == null)
            {
                Label[] bars = new Label[CPUUsageBarCount];

                for (int i = 0; i < CPUUsageBarCount; i++)
                {
                    bars[i] = (Label)this.FindName("CPUUsagelabel" + i.ToString());
                }

                this._bars = bars;
            }

            return this._bars;
        }

        private Label[] GetMemoryBars()
        {
            if (this._memoryBars == null)
            {
                Label[] bars = new Label[CPUUsageBarCount];

                for (int i = 0; i < CPUUsageBarCount; i++)
                {
                    bars[i] = (Label)this.FindName("MemoryUsagelabel" + i.ToString());
                }

                this._memoryBars = bars;
            }

            return this._memoryBars;
        }

        /// <summary>
        /// Hauteur de barre pour une quantité de mémoire, rapportée au plus gros
        /// consommateur du relevé. Rapporter à la mémoire physique donnerait une
        /// barre invisible pour la quasi-totalité des processus : à 32 Go, un
        /// navigateur à 500 Mo occuperait un pixel et demi.
        /// </summary>
        private static double GetMemoryBarHeight(long memoryBytes)
        {
            long maximum = Threading.Process.MaximumMemoryBytes;

            if (memoryBytes <= 0 || maximum <= 0)
            {
                return 0d;
            }

            double height = CPUUsageBarHeight * memoryBytes / maximum;

            return height > CPUUsageBarHeight ? CPUUsageBarHeight : height;
        }

        /// <summary>Mémoire en unités lisibles, pour l'infobulle.</summary>
        internal static string DescribeMemory(long memoryBytes)
        {
            if (memoryBytes <= 0)
            {
                return "unknown";
            }

            if (memoryBytes >= 1073741824L)
            {
                return (memoryBytes / 1073741824d).ToString("F2") + " GB";
            }

            if (memoryBytes >= 1048576L)
            {
                return (memoryBytes / 1048576d).ToString("F1") + " MB";
            }

            return (memoryBytes / 1024d).ToString("F0") + " KB";
        }

        /// <summary>
        /// Pinceau de la barre pour un pourcentage donné. Les cent-une valeurs
        /// possibles sont mises en cache et gelées : elles sont partagées entre
        /// toutes les tuiles.
        /// </summary>
        private Brush GetBarBrush(int percent)
        {
            if (percent < 0)
            {
                percent = 0;
            }
            else if (percent > 100)
            {
                percent = 100;
            }

            Brush brush = BarBrushesByPercent[percent];

            if (brush == null)
            {
                brush = new SolidColorBrush(UIntToColor((uint)ConvertToValidRGBValue(percent)));
                brush.Freeze();
                BarBrushesByPercent[percent] = brush;
            }

            return brush;
        }

        /// <summary>
        /// Appelée à chaque relevé depuis le thread d'échantillonnage. Le décalage
        /// de l'historique se fait toujours, en mémoire ; seul le rendu dépend de
        /// l'état de l'affichage.
        ///
        /// Ce rendu tenait auparavant en trente-six opérations postées par tuile
        /// et par relevé — une par étiquette et par propriété — lancées depuis une
        /// tâche du pool. Il en reste une seule, qui écrit tout.
        /// </summary>
        private void SetCPUUsageLabel(double? cpuUsage)
        {
            if (this.Dispatcher.HasShutdownStarted || this.Dispatcher.HasShutdownFinished)
            {
                return;
            }

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

            // Décalage de l'historique, fait dans tous les cas : c'est lui qui
            // garde la courbe continue quand l'affichage est suspendu.
            for (int i = CPUUsageBarCount - 1; i > 0; i--)
            {
                this._barHeights[i] = this._barHeights[i - 1];
                this._barBrushes[i] = this._barBrushes[i - 1];
                this._memoryBarHeights[i] = this._memoryBarHeights[i - 1];
            }

            this._barHeights[0] = (CPUUsageBarHeight * singleCoreUsage) / 100d;
            this._barBrushes[0] = GetBarBrush((int)Math.Round(singleCoreUsage, MidpointRounding.AwayFromZero));
            this._displayedValue = cpuUsage.HasValue ? cpuUsage.Value.ToString("F0") : "-";

            // La mémoire est déjà posée sur le processus par le même relevé : elle
            // se lit ici sans appel système ni opération postée supplémentaires.
            this._memoryBytes = this._process == null ? 0L : this._process.MemoryBytes;
            this._memoryBarHeights[0] = GetMemoryBarHeight(this._memoryBytes);

            if (IsDisplaySuspended)
            {
                return;
            }

            this.Dispatcher.BeginInvoke(new Action(this.RefreshDisplay));
        }

        /// <summary>
        /// Écrit l'historique en mémoire dans les étiquettes. Appelée à chaque
        /// relevé quand l'affichage est actif, et une seule fois par tuile à la
        /// restauration — directement, sans passer par le dispatcher, puisqu'on
        /// est alors déjà sur son thread.
        /// </summary>
        public void RefreshDisplay()
        {
            try
            {
                Label[] bars = GetBars();
                Label[] memoryBars = GetMemoryBars();

                bool showCpu = AreCpuBarsShown;
                bool showMemory = AreMemoryBarsShown;

                // Les deux courbes sont peintes dans la même boucle, donc dans la
                // même opération postée : la mémoire n'ajoute rien au nombre
                // d'allers-retours sur le dispatcher, qui est d'un par tuile et
                // par relevé.
                for (int i = 0; i < CPUUsageBarCount; i++)
                {
                    // Une barre éteinte est ramenée à zéro plutôt que masquée :
                    // c'est la même écriture que pour la peindre, et cela évite de
                    // faire varier le nombre d'éléments visibles à chaque bascule.
                    double memoryHeight = showMemory ? this._memoryBarHeights[i] : 0d;

                    if (memoryHeight > 0d || memoryBars[i].Height > 0d)
                    {
                        memoryBars[i].Height = memoryHeight;
                        memoryBars[i].Background = MemoryBarBrush;
                    }

                    Brush brush = this._barBrushes[i];

                    if (brush == null)
                    {
                        continue;
                    }

                    bars[i].Height = showCpu ? this._barHeights[i] : 0d;
                    bars[i].Background = brush;
                }

                this.MemoryGaugeFill.Height = showMemory ? this._memoryBarHeights[0] : 0d;
                this.CPUUsageValueTextBlock.Text = this._displayedValue;
                this.ProcessNameLabelBackground = GetProcessNameBackgroundBrush();

                // Infobulle affichée : on la tient à jour. Test hors du rendu des
                // barres pour n'ajouter aucun travail aux tuiles dont elle est
                // fermée, c'est-à-dire à toutes sauf une.
                if (this._isToolTipOpen)
                {
                    UpdateToolTip();
                }
            }
            catch
            {
                this.CPUUsagelabel0.Background = Brushes.Gray;
            }
        }

        /// <summary>
        /// Bloc de l'infobulle consacré aux deux réglages d'ordonnancement.
        ///
        /// Le mode d'efficacité est présenté comme ce qui est *demandé* à Windows,
        /// jamais comme un effet constaté : l'état se lit et s'écrit fidèlement,
        /// mais son incidence dépend de la machine et du profil d'alimentation.
        /// Mesuré nul sur cette machine — un utilisateur qui l'active sans rien
        /// voir changer doit comprendre que le réglage est bien posé.
        /// </summary>
        private string GetSchedulingToolTipText()
        {
            StringBuilder builder = new StringBuilder();

            if (ProcessPowerThrottling.IsEfficiencyModeSupported)
            {
                EfficiencyModeEnum? mode = this._process.GetEfficiencyMode();

                if (mode != null)
                {
                    builder.Append("\r\nEfficiency mode: ");

                    switch (mode.Value)
                    {
                        case EfficiencyModeEnum.Enabled:
                            builder.Append("Windows is asked to throttle this process.");
                            break;
                        case EfficiencyModeEnum.Disabled:
                            builder.Append("Windows is asked never to throttle this process.");
                            break;
                        default:
                            builder.Append("left to Windows (default).");
                            break;
                    }

                    if (mode.Value != EfficiencyModeEnum.SystemManaged)
                    {
                        builder.Append("\r\nWhether it changes anything is up to Windows, "
                                       + "and depends on the processor and the power plan.");
                    }
                }
            }

            if (ProcessPowerThrottling.AreCpuSetsSupported)
            {
                uint[] cpuSets = this._process.GetDefaultCpuSets();

                if (cpuSets != null)
                {
                    builder.Append("\r\nCPU Sets: ");
                    builder.Append(cpuSets.Length == 0
                        ? "no preference."
                        : cpuSets.Length + (cpuSets.Length > 1 ? " processors preferred" : " processor preferred")
                          + ", which Windows may override.");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Bloc de l'infobulle consacré à la règle. Vide quand le processus n'en
        /// porte pas.
        /// </summary>
        private string GetRuleToolTipText()
        {
            RuleStateEnum state = this._process.RuleState;

            if (state == RuleStateEnum.None)
            {
                return string.Empty;
            }

            string text = state == RuleStateEnum.Applied
                ? "\r\n\r\nSaved rule applied and confirmed."
                : "\r\n\r\nSaved rule — " + GetRuleStateText(state) + "\r\n"
                  + (this._process.RuleDetail ?? string.Empty);

            // Le nombre de rétablissements dit ce que l'état seul ne dit pas :
            // qu'un tiers touche à ce processus, et à quelle fréquence.
            int enforcementCount = this._process.RuleEnforcementCount;

            if (enforcementCount > 0)
            {
                text = text + "\r\nRestored " + enforcementCount
                       + (enforcementCount > 1 ? " times" : " time") + " since ProcessAffinity started.";
            }

            return text;
        }

        private static string GetRuleStateText(RuleStateEnum state)
        {
            switch (state)
            {
                case RuleStateEnum.Applied:
                    return "applied";
                case RuleStateEnum.Contested:
                    return "contested by Windows";
                case RuleStateEnum.Denied:
                    return "denied, elevation required";
                case RuleStateEnum.Orphan:
                    return "orphaned, executable missing";
                case RuleStateEnum.InvalidMask:
                    return "ignored, invalid on this machine";
                default:
                    return "none";
            }
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
                // Quand des tuiles sont cochées, l'action porte sur la sélection,
                // y compris si le clic droit a eu lieu ailleurs. L'entrée du menu
                // le dit, faute de quoi le comportement serait imprévisible.
                int selectedCount = GetSelectedProcesses().Count;
                string scope = selectedCount > 0 ? " (" + selectedCount + " selected)" : string.Empty;

                MenuItem menuItem = new MenuItem();
                menuItem.Header = PriorityHeader + scope;
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

                menuItem = new MenuItem();
                menuItem.Header = AffinityHeader + scope;
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);

                if (ProcessPowerThrottling.IsEfficiencyModeSupported)
                {
                    menu.Items.Add(CreateEfficiencyModeMenu(scope));
                }

                if (selectedCount > 0)
                {
                    menuItem = new MenuItem();
                    menuItem.Header = ClearSelectionHeader;
                    ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
                }

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

                menu.Items.Add(new Separator());

                // Enregistrer n'a de sens que sur une entrée dont l'affinité est
                // lisible et le chemin connu ; retirer, que sur une entrée qui
                // porte déjà une règle.
                // La portée est annoncée ici aussi : l'enregistrement n'agissait
                // que sur la tuile cliquée, si bien qu'une sélection de trois
                // n'obtenait qu'une règle, sans que rien ne le dise.
                if (GetRuledTargets().Count > 0)
                {
                    menuItem = new MenuItem();
                    menuItem.Header = RemoveConfigurationHeader + scope;
                    ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
                }

                menuItem = new MenuItem();
                menuItem.Header = SaveConfigurationHeader + scope;
                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click += new RoutedEventHandler(ProcessUserControlContextMenuClick);
            }

            this.ContextMenu = menu;

            
        }

        /// <summary>
        /// Sous-menu du mode d'efficacité. Un état à trois valeurs n'appelle pas
        /// une fenêtre, et surtout pas celle de la priorité : son bouton applique
        /// à la fermeture, si bien qu'y loger un second réglage le réécrirait
        /// chaque fois qu'on vient changer le premier. Une entrée de menu, elle,
        /// applique ce qu'on a cliqué et rien d'autre.
        /// </summary>
        private MenuItem CreateEfficiencyModeMenu(string scope)
        {
            MenuItem root = new MenuItem();
            root.Header = EfficiencyModeHeader + scope;

            EfficiencyModeEnum? current = this._process.GetEfficiencyMode();

            foreach (EfficiencyModeEnum mode in new[]
                     { EfficiencyModeEnum.Enabled, EfficiencyModeEnum.Disabled, EfficiencyModeEnum.SystemManaged })
            {
                MenuItem item = new MenuItem();
                item.Header = GetEfficiencyModeText(mode);
                item.Tag = mode;
                item.IsCheckable = false;

                // Coche sur l'état en place. Sans sélection multiple, elle dit
                // d'un coup d'œil ce qui est demandé à Windows pour ce processus.
                item.IsChecked = current != null && current.Value == mode;

                item.Click += EfficiencyModeMenuClick;
                root.Items.Add(item);
            }

            return root;
        }

        private static string GetEfficiencyModeText(EfficiencyModeEnum mode)
        {
            switch (mode)
            {
                case EfficiencyModeEnum.Enabled:
                    return "Ask Windows to throttle it";
                case EfficiencyModeEnum.Disabled:
                    return "Ask Windows never to throttle it";
                default:
                    return "Let Windows decide (default)";
            }
        }

        private void EfficiencyModeMenuClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = e.OriginalSource as MenuItem;

            if (item == null || !(item.Tag is EfficiencyModeEnum))
            {
                return;
            }

            EfficiencyModeEnum mode = (EfficiencyModeEnum)item.Tag;

            // Même routage que les autres actions : la sélection prime sur la
            // tuile visée.
            List<Process> targets = GetSelectedProcesses();

            if (targets.Count == 0)
            {
                targets.Add(this._process);
            }

            int appliedCount = 0;
            List<string> notModifiableNames = new List<string>();
            List<string> goneNames = new List<string>();

            foreach (Process process in targets)
            {
                if (!process.IsRunning)
                {
                    goneNames.Add(process.ProcessName);
                    continue;
                }

                if (!process.IsModifiable || !process.TrySetEfficiencyMode(mode))
                {
                    notModifiableNames.Add(process.ProcessName);
                    continue;
                }

                appliedCount++;
                RuleEngine.UpdateIfRuled(process);
            }

            ProcessAffinityWindow.ReportPartialApplication(
                "Efficiency mode", appliedCount, notModifiableNames, goneNames);
        }

        private void ProcessUserControlContextMenuClick(object sender, RoutedEventArgs e)
        {
            ProcessPriorityWindow processPriorityWindow = null;
            ProcessAffinityWindow processAffinityWindow = null;

            // L'en-tête porte la portée entre parenthèses : on aiguille sur sa
            // partie stable.
            string header = ((MenuItem)e.OriginalSource).Header.ToString();
            int parenthesis = header.IndexOf(" (");

            if (parenthesis > 0)
            {
                header = header.Substring(0, parenthesis);
            }

            // Sélection non vide : elle prime sur la tuile visée, qui est alors
            // ignorée. Vide : l'action porte sur la seule tuile visée.
            List<Process> targets = GetSelectedProcesses();

            if (targets.Count == 0)
            {
                targets.Add(this._process);
            }

            switch (header)
            {
                case ClearSelectionHeader:
                    ClearSelection();
                    return;

                case PriorityHeader:
                    processPriorityWindow = new ProcessPriorityWindow(targets);
                    processPriorityWindow.ShowDialog();
                    RefreshEntriesSharingHost();
                    return;

                case AffinityHeader:
                    processAffinityWindow = new ProcessAffinityWindow(targets);
                    processAffinityWindow.ShowDialog();
                    RefreshEntriesSharingHost();
                    return;
            }

            switch (header)
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
                case SaveConfigurationHeader:
                    SaveConfiguration(targets);
                    break;
                case RemoveConfigurationHeader:
                    RemoveConfiguration(targets);
                    break;
            }

        }

        /// <summary>
        /// Enregistre l'affinité et la priorité courantes du processus comme règle.
        /// C'est bien l'état courant qui est retenu : l'utilisateur règle la tuile
        /// comme il l'entend, puis demande que cela devienne permanent.
        /// </summary>
        private void SaveConfiguration(List<Process> targets)
        {
            ConfigurationCommands.Save(targets);
        }

        private void RemoveConfiguration(List<Process> targets)
        {
            ConfigurationCommands.Remove(targets);
        }

        /// <summary>
        /// Cibles de l'action qui portent déjà une règle. C'est ce qui décide de
        /// proposer ou non le retrait.
        /// </summary>
        private List<Process> GetRuledTargets()
        {
            List<Process> targets = GetSelectedProcesses();

            if (targets.Count == 0 && this._process != null)
            {
                targets.Add(this._process);
            }

            return ConfigurationCommands.GetRuledTargets(targets);
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
                    "CPU: " + (cpuUsage.HasValue ? cpuUsage.Value.ToString("F1") + " %" : "-") + "\r\n" +
                    // Jeu de travail privé, comme la colonne « Mémoire » du
                    // Gestionnaire des tâches. Le nommer évite de laisser croire
                    // qu'il s'agit du jeu de travail complet, bien plus grand.
                    "Memory: " + DescribeMemory(this._process.MemoryBytes) + " (private working set)";

                if (!this._process.IsModifiable)
                {
                    text = text + "\r\nAffinity and priority cannot be changed without elevation.";
                }

                text = text + GetSchedulingToolTipText() + GetRuleToolTipText();

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
            // Une entrée de service porte le PID de son hôte : la cocher
            // reviendrait à désigner deux fois le même processus. C'est l'hôte
            // qu'on sélectionne.
            if (this._process == null || this._process.IsService)
            {
                return;
            }

            bool changed = this._process.IsSelected != isSelected;

            this._process.IsSelected = isSelected;

            SelectedUserControlCheckBox.Visibility = visibility
                ?? (isSelected ? Visibility.Visible : Visibility.Hidden);

            SelectedUserControlCheckBox.IsChecked = isSelected;

            if (changed)
            {
                NotifySelectionChanged();
            }
        }

        /// <summary>
        /// Prévient la fenêtre principale : elle seule tient le compteur de la
        /// barre du bas, et rien d'autre ne l'aurait rafraîchi sur un simple clic.
        /// </summary>
        public static Action SelectionChanged { get; set; }

        private static void NotifySelectionChanged()
        {
            Action changed = SelectionChanged;

            if (changed != null)
            {
                changed();
            }
        }

        /// <summary>
        /// Décoche tout, en un geste : retrouver quelques tuiles cochées parmi
        /// plusieurs centaines n'en est pas un. Posé par la fenêtre principale,
        /// seule à pouvoir rafraîchir les tuiles et le compteur.
        /// </summary>
        public static Action ClearSelectionRequested { get; set; }

        private static void ClearSelection()
        {
            Action clear = ClearSelectionRequested;

            if (clear != null)
            {
                clear();
            }
        }

        /// <summary>
        /// Remet la case dans l'état porté par l'entrée. Appelée à la construction
        /// de la tuile : c'est ce qui fait survivre la sélection aux
        /// reconstructions du panneau.
        /// </summary>
        private void RestoreSelection(Process process)
        {
            if (process == null || process.IsService)
            {
                return;
            }

            SelectedUserControlCheckBox.IsChecked = process.IsSelected;
            SelectedUserControlCheckBox.Visibility = process.IsSelected
                ? Visibility.Visible
                : Visibility.Hidden;
        }

        /// <summary>
        /// MouseDown est levé par WPF pour tous les boutons, y compris le droit :
        /// un clic droit basculait donc la case en même temps qu'il ouvrait le
        /// menu, vidant la sélection avant qu'on ait pu s'en servir. Un clic droit
        /// ouvre le menu et ne touche jamais à la sélection.
        /// </summary>
        private void UserControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

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
