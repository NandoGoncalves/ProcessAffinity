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

        /// <summary>Entrées d'affichage, communes à toutes les tuiles.</summary>
        private const string KillHeader = "Kill";

        private const string PinHeader = "Pin to front";
        private const string UnpinHeader = "Unpin from front";
        private const string TileSizeHeader = "Tile size";
        private const string CpuValueHeader = "CPU value on tile";
        private const string MemoryValueHeader = "RAM value on tile";
        private const string BackgroundColorHeader = "Background colour";

        /// <summary>Hauteur de l'emplacement d'une barre, en pixels (cf. XAML).</summary>
        private const double CPUUsageBarHeight = 56d;

        /// <summary>
        /// Fonds du bandeau de nom. Les quatre teintes viennent de la palette :
        /// la couleur porte l'information, le reste de la tuile est inchangé, et
        /// la police bascule entre noir et blanc selon la luminance — ce qui
        /// vaut aussi pour une teinte choisie par l'utilisateur.
        /// </summary>
        private static Brush NotModifiableNameBackgroundBrush
        {
            get { return ColorPalette.GetBrush(ColorPalette.NameBandNotModifiable); }
        }

        private static Brush RuledNameBackgroundBrush
        {
            get { return ColorPalette.GetBrush(ColorPalette.NameBandRuled); }
        }

        private static Brush FailedRuleNameBackgroundBrush
        {
            get { return ColorPalette.GetBrush(ColorPalette.NameBandFailedRule); }
        }

        private static Brush ActivityNameBackgroundBrush
        {
            get { return ColorPalette.GetBrush(ColorPalette.ActivityHighlight); }
        }

        private static Brush RestingNameBackgroundBrush
        {
            get { return ColorPalette.GetBrush(ColorPalette.NameBandResting); }
        }

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
        /// <summary>
        /// Valeurs chiffrées en bas à droite de la zone des courbes. Les deux
        /// sont éteintes par défaut : la tuile se lit d'abord par ses courbes,
        /// et un chiffre sur chacune des quatre cents n'apporte rien à qui
        /// balaie la grille des yeux.
        ///
        /// Les deux ensemble donnent « 190 MB - 3% ».
        /// </summary>
        public static bool IsCpuValueShown { get; set; }

        public static bool IsMemoryValueShown { get; set; }

        /// <summary>
        /// Âge au-delà duquel une valeur cesse d'être présentée comme courante.
        /// Trois relevés : un relevé manqué n'a rien d'anormal, trois veulent
        /// dire que plus rien n'arrive pour cette entrée.
        /// </summary>
        private const double StaleValueSeconds = 3d;

        /// <summary>Teinte d'une valeur qui n'est plus fraîche.</summary>
        private static readonly Brush StaleValueBrush =
            ColorPalette.Freeze(Color.FromRgb(0x8A, 0x8A, 0x8A));

        private static readonly Brush FreshValueBrush = ColorPalette.Freeze(Colors.Black);

        public static bool AreCpuBarsShown { get; set; } = true;

        public static bool AreMemoryBarsShown { get; set; } = true;

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
        ///
        /// En octets, et non en hauteurs : l'échelle est le plus gros consommateur
        /// du relevé, et elle change à chaque seconde. Un historique de hauteurs
        /// mélangerait dix-sept échelles, et la disparition du plus gros processus
        /// ferait bondir les barres déjà tracées sans qu'aucune consommation n'ait
        /// bougé. Converti au moment de peindre, l'historique entier se remet à
        /// l'échelle d'un seul mouvement.
        ///
        /// Un seul pinceau suffit, partagé par toutes les tuiles : les barres ne
        /// portent aucune information par la teinte, seulement par la hauteur.
        /// </summary>
        private readonly long[] _memoryBytesHistory = new long[CPUUsageBarCount];
        private long _memoryBytes;

        /// <summary>
        /// À peine plus foncé que le fond de la tuile. Les barres de mémoire
        /// sont un arrière-plan : elles doivent se lire sans disputer la lecture
        /// aux barres de CPU, qui passent devant et portent l'information vive.
        /// </summary>
        private static Brush MemoryBarBrush
        {
            get { return ColorPalette.GetBrush(ColorPalette.MemoryBars); }
        }

        /// <summary>Étiquettes de la courbe, de la plus récente à la plus ancienne.</summary>
        private Label[] _bars = null;
        private Label[] _memoryBars = null;

        /// <summary>
        /// Pinceaux de la courbe, par couleur de base puis par pourcentage. Sans
        /// ce cache, le rendu allouait un pinceau par barre et par relevé, soit
        /// plus de cinq mille par seconde.
        ///
        /// La couleur fait partie de la clé, et ce n'était pas le cas : le cache
        /// était un simple tableau de 101 entrées indexé par le seul
        /// pourcentage, alors que la teinte dépend de la priorité du processus.
        /// La première tuile à peindre un pourcentage imposait donc sa couleur à
        /// toutes les autres pour ce pourcentage — vérifié : une tuile de
        /// priorité Haute recevait le vert d'une tuile Idle. Six priorités et
        /// cent-une valeurs font au plus 606 pinceaux gelés, toujours partagés
        /// par toutes les tuiles : jamais un pinceau par tuile.
        /// </summary>
        private static readonly Dictionary<Color, Brush[]> BarBrushesByColor =
            new Dictionary<Color, Brush[]>();

        /// <summary>
        /// Révision de palette à laquelle le cache ci-dessus a été rempli. Un
        /// changement de couleur le vide plutôt que de le contourner : le
        /// contourner reviendrait à allouer un pinceau par tuile.
        /// </summary>
        private static int _barBrushesRevision = -1;


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

            // Les teintes réglables sont posées dès la construction : une tuile
            // créée après un changement de palette doit naître à la bonne
            // couleur, sans attendre un repeint général.
            this.Background = GetTileBackgroundBrush();
            this.MemoryGaugeFill.Fill = ColorPalette.GetBrush(ColorPalette.MemoryGauge);

            ApplyScale();

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
                return RestingNameBackgroundBrush;
            }

            if (this._process.ConsumeRuleJustApplied())
            {
                return ActivityNameBackgroundBrush;
            }

            if (this._process.RuleState != RuleStateEnum.None)
            {
                return GetRestingProcessNameBackgroundBrush();
            }

            if (this._process.HasRecentActivity)
            {
                return ActivityNameBackgroundBrush;
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

            return RestingNameBackgroundBrush;
        }

        /// <summary>
        /// Fond de la tuile : la couleur propre au processus si elle existe,
        /// sinon celle de la palette générale.
        ///
        /// Les pinceaux propres à un processus sont mis en cache par couleur et
        /// gelés, comme ceux de la palette : deux tuiles du même programme
        /// partagent le même objet, et une couleur par processus ne fait pas un
        /// pinceau par tuile.
        /// </summary>
        /// <summary>
        /// Reprend le fond de la tuile : la couleur propre au processus si elle
        /// existe, sinon celle de la palette.
        ///
        /// Appelée après un changement de préférence d'affichage. Sans elle, une
        /// couleur posée sur un processus ne se voyait qu'au prochain
        /// rechargement : le réordonnancement et l'échelle étaient repris, le
        /// fond non.
        /// </summary>
        public void ApplyTileBackground()
        {
            Brush wanted = GetTileBackgroundBrush();

            if (!ReferenceEquals(this.Background, wanted))
            {
                this.Background = wanted;
            }
        }

        private Brush GetTileBackgroundBrush()
        {
            Color? own = DisplayPreferences.GetBackgroundColor(this._process);

            if (own == null)
            {
                return ColorPalette.GetBrush(ColorPalette.TileBackground);
            }

            lock (OwnBackgroundBrushes)
            {
                Brush brush;

                if (!OwnBackgroundBrushes.TryGetValue(own.Value, out brush))
                {
                    brush = ColorPalette.Freeze(own.Value);
                    OwnBackgroundBrushes[own.Value] = brush;
                }

                return brush;
            }
        }

        private static readonly Dictionary<Color, Brush> OwnBackgroundBrushes =
            new Dictionary<Color, Brush>();

        /// <summary>
        /// Valeur chiffrée, et son âge.
        ///
        /// Tout se fait ici, dans l'opération déjà postée par le rendu : aucun
        /// aller-retour supplémentaire sur le dispatcher, ce qui est la
        /// contrainte de l'étape 2b — une opération par tuile et par relevé.
        ///
        /// Une valeur qui n'est plus fraîche n'est pas effacée mais grisée :
        /// effacer perdrait une information utile, puisqu'on veut justement
        /// savoir qu'un processus était à 96 % juste avant de cesser de
        /// répondre. Le gris dit « ceci n'est plus un relevé », et l'infobulle
        /// dit depuis quand.
        /// </summary>
        private void RefreshValueLabel()
        {
            bool showCpuValue = IsCpuValueShown;
            bool showMemoryValue = IsMemoryValueShown;

            if (!showCpuValue && !showMemoryValue)
            {
                if (this.CPUUsageValueTextBlock.Visibility != Visibility.Collapsed)
                {
                    this.CPUUsageValueTextBlock.Visibility = Visibility.Collapsed;
                }

                return;
            }

            if (this.CPUUsageValueTextBlock.Visibility != Visibility.Visible)
            {
                this.CPUUsageValueTextBlock.Visibility = Visibility.Visible;
            }

            string text;

            if (showCpuValue && showMemoryValue)
            {
                text = DescribeMemory(this._memoryBytes) + " - " + this._displayedValue + "%";
            }
            else if (showMemoryValue)
            {
                text = DescribeMemory(this._memoryBytes);
            }
            else
            {
                text = this._displayedValue + "%";
            }

            if (this.CPUUsageValueTextBlock.Text != text)
            {
                this.CPUUsageValueTextBlock.Text = text;
            }

            Brush wanted = IsSampleStale() ? StaleValueBrush : FreshValueBrush;

            if (!ReferenceEquals(this.CPUUsageValueTextBlock.Foreground, wanted))
            {
                this.CPUUsageValueTextBlock.Foreground = wanted;
            }
        }

        /// <summary>
        /// Vrai quand le dernier relevé abouti de cette entrée a plus de
        /// <see cref="StaleValueSeconds"/>. Une simple comparaison, faite dans
        /// le rendu déjà posté.
        /// </summary>
        /// <summary>Âge lisible : secondes, minutes, puis heures.</summary>
        private static string DescribeAge(double seconds)
        {
            if (seconds < 60d)
            {
                return ((int)seconds) + " s";
            }

            if (seconds < 3600d)
            {
                return ((int)(seconds / 60d)) + " min";
            }

            return (seconds / 3600d).ToString("F1") + " h";
        }

        private bool IsSampleStale()
        {
            double age = this._process == null ? 0d : this._process.SampleAgeSeconds;

            return age > StaleValueSeconds;
        }

        /// <summary>
        /// Transformations d'échelle, gelées et partagées. Une par facteur, et
        /// non une par tuile : le même objet sert à toutes les tuiles d'un même
        /// agrandissement, comme les pinceaux de barre.
        /// </summary>
        private static readonly Transform[] ScaleTransforms = BuildScaleTransforms();

        private static Transform[] BuildScaleTransforms()
        {
            Transform[] transforms = new Transform[DisplayPreferences.MaximumScale + 1];

            transforms[0] = Transform.Identity;
            transforms[1] = Transform.Identity;

            for (int scale = 2; scale <= DisplayPreferences.MaximumScale; scale++)
            {
                ScaleTransform transform = new ScaleTransform(scale, scale);
                transform.Freeze();

                transforms[scale] = transform;
            }

            return transforms;
        }

        /// <summary>
        /// Agrandit la tuile par un multiple entier.
        ///
        /// <c>LayoutTransform</c> et non <c>RenderTransform</c> : le premier
        /// informe la mise en page, si bien que le WrapPanel voit la tuile
        /// agrandie et lui réserve la place ; le second se contenterait de
        /// dessiner par-dessus les voisines.
        ///
        /// Rien n'est recalculé par relevé. Les dix-sept écritures de hauteur
        /// restent identiques, à l'échelle 1, et c'est le moteur de rendu qui
        /// applique le facteur une fois. Multiplier la géométrie des barres
        /// dans RefreshDisplay donnerait le même résultat à l'écran en ajoutant
        /// dix-sept multiplications par tuile et par seconde, pour rien.
        /// </summary>
        public void ApplyScale()
        {
            int scale = DisplayPreferences.GetScale(this._process);

            if (scale < 1 || scale >= ScaleTransforms.Length)
            {
                scale = 1;
            }

            Transform wanted = ScaleTransforms[scale];

            // Sur CurvesHost et non sur la tuile : seules les courbes — barres
            // de CPU, barres de mémoire et jauge — changent de taille. L'icône,
            // le nom, son bandeau et la valeur chiffrée gardent la leur,
            // puisqu'ils vivent hors de ce conteneur.
            //
            // Réécrire la même transformation invaliderait la mise en page pour
            // rien, et cette méthode passe sur toutes les tuiles à chaque
            // changement de préférence.
            if (!ReferenceEquals(this.CurvesHost.LayoutTransform, wanted))
            {
                this.CurvesHost.LayoutTransform = wanted;
            }
        }

        /// <summary>
        /// Reprend toutes les couleurs de la palette sur cette tuile : fond,
        /// jauge de mémoire, bandeau de nom, et barres par le rendu suivant.
        /// Appelée après un changement de palette, pour que l'application se
        /// repeigne sans redémarrage.
        /// </summary>
        public void ApplyPalette()
        {
            this.Background = GetTileBackgroundBrush();
            this.MemoryGaugeFill.Fill = ColorPalette.GetBrush(ColorPalette.MemoryGauge);

            SetModifiableMarker(this._process);

            RefreshDisplay();
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
        /// Entrées de service cochées, dédoublonnées par nom de service.
        ///
        /// Le pendant de <see cref="GetSelectedProcesses"/>, qui écarte les
        /// services : les deux familles ne prennent pas les mêmes commandes, et
        /// une sélection mêlant les deux n'applique à chacune que ce qui la
        /// concerne. Le dédoublonnage se fait sur le nom et non sur le PID,
        /// plusieurs services partageant un même hôte.
        /// </summary>
        public static List<Process> GetSelectedServices()
        {
            List<Process> selected = new List<Process>();

            Func<Process[]> source = SelectionSource;

            if (source == null)
            {
                return selected;
            }

            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Process process in source())
            {
                if (process == null || !process.IsSelected || !process.IsService)
                {
                    continue;
                }

                if (!seenNames.Add(process.ProcessName ?? string.Empty))
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
        ///
        /// L'échelle est passée en argument et non relue ici : tout l'historique
        /// d'une tuile doit être converti avec la même, et la relire par barre
        /// rendrait dix-sept lectures volatiles pour rien.
        /// </summary>
        private static double GetMemoryBarHeight(long memoryBytes, long maximum)
        {
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

            Color baseColor = this._ProcessAffinityColors == null
                ? Colors.Red
                : this._ProcessAffinityColors.BaseColor;

            lock (BarBrushesByColor)
            {
                if (_barBrushesRevision != ColorPalette.Revision)
                {
                    // La palette a changé : le cache entier est périmé, couleurs
                    // de base comprises. On le vide, et il se remplit de nouveau
                    // au fil des relevés.
                    BarBrushesByColor.Clear();
                    _barBrushesRevision = ColorPalette.Revision;
                }

                Brush[] byPercent;

                if (!BarBrushesByColor.TryGetValue(baseColor, out byPercent))
                {
                    byPercent = new Brush[101];
                    BarBrushesByColor[baseColor] = byPercent;
                }

                Brush brush = byPercent[percent];

                if (brush == null)
                {
                    brush = ColorPalette.Freeze(UIntToColor((uint)ConvertToValidRGBValue(percent)));
                    byPercent[percent] = brush;
                }

                return brush;
            }
        }

        /// <summary>
        /// Nombre de pinceaux de barre en cache. Sert aux vérifications : il doit
        /// rester borné par six priorités fois cent-une valeurs, quel que soit le
        /// nombre de tuiles.
        /// </summary>
        internal static int CountCachedBarBrushes()
        {
            lock (BarBrushesByColor)
            {
                return BarBrushesByColor.Values.Sum(brushes => brushes.Count(brush => brush != null));
            }
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
                this._memoryBytesHistory[i] = this._memoryBytesHistory[i - 1];
            }

            this._barHeights[0] = (CPUUsageBarHeight * singleCoreUsage) / 100d;
            this._barBrushes[0] = GetBarBrush((int)Math.Round(singleCoreUsage, MidpointRounding.AwayFromZero));
            this._displayedValue = cpuUsage.HasValue ? cpuUsage.Value.ToString("F0") : "-";

            // La mémoire est déjà posée sur le processus par le même relevé : elle
            // se lit ici sans appel système ni opération postée supplémentaires.
            this._memoryBytes = this._process == null ? 0L : this._process.MemoryBytes;
            this._memoryBytesHistory[0] = this._memoryBytes;

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

                // Une seule lecture de l'échelle pour toute la tuile : l'historique
                // entier se convertit avec la même, sans quoi la courbe mélangerait
                // de nouveau des échelles différentes.
                long memoryScale = showMemory ? Threading.Process.MaximumMemoryBytes : 0L;

                // Les deux courbes sont peintes dans la même boucle, donc dans la
                // même opération postée : la mémoire n'ajoute rien au nombre
                // d'allers-retours sur le dispatcher, qui est d'un par tuile et
                // par relevé.
                for (int i = 0; i < CPUUsageBarCount; i++)
                {
                    // Une barre éteinte est ramenée à zéro plutôt que masquée :
                    // c'est la même écriture que pour la peindre, et cela évite de
                    // faire varier le nombre d'éléments visibles à chaque bascule.
                    double memoryHeight = showMemory
                        ? GetMemoryBarHeight(this._memoryBytesHistory[i], memoryScale)
                        : 0d;

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

                this.MemoryGaugeFill.Height = showMemory
                    ? GetMemoryBarHeight(this._memoryBytesHistory[0], memoryScale)
                    : 0d;
                RefreshValueLabel();

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
                BuildServiceMenu(menu);
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
                    menuItem.Header = KillHeader + scope;
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

            AppendDisplayMenu(menu);

            this.ContextMenu = menu;


        }

        /// <summary>
        /// Les deux réglages d'affichage, proposés sur toute tuile — processus,
        /// service ou processus surveillé. Ils ne touchent ni à l'affinité ni à
        /// la priorité, et n'ont donc aucune raison de dépendre des droits.
        /// </summary>
        private void AppendDisplayMenu(ContextMenu menu)
        {
            if (this._process == null)
            {
                return;
            }

            menu.Items.Add(new Separator());

            // Épinglage de cette instance seulement. Le geste direct sur une
            // tuile ne vaut que pour elle et disparaît avec le processus ; ce
            // qui doit survivre passe par « Remember ».
            MenuItem pinItem = new MenuItem();
            pinItem.Header = DisplayPreferences.IsPinned(this._process) ? UnpinHeader : PinHeader;

            ((MenuItem)menu.Items[menu.Items.Add(pinItem)]).Click +=
                new RoutedEventHandler(ProcessUserControlContextMenuClick);

            menu.Items.Add(CreateRememberItem(
                DisplayPreferences.IsPinnedForExecutable(this._process),
                RememberPinMenuItemClick,
                "pinning"));

            menu.Items.Add(CreateTileSizeMenu());

            // Les deux valeurs chiffrées, et la couleur propre au processus.
            menu.Items.Add(CreateValueItem(CpuValueHeader, IsCpuValueShown, CpuValueMenuItemClick));
            menu.Items.Add(CreateValueItem(MemoryValueHeader, IsMemoryValueShown, MemoryValueMenuItemClick));
            menu.Items.Add(CreateBackgroundColorMenu());
        }

        /// <summary>
        /// Une des deux valeurs chiffrées. Ce réglage vaut pour toutes les
        /// tuiles, comme les cases « CPU » et « RAM » de la barre du haut : un
        /// chiffre affiché sur une tuile et pas sur sa voisine ne se
        /// comparerait pas.
        /// </summary>
        private static MenuItem CreateValueItem(string header, bool isChecked, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = header;
            item.IsCheckable = true;
            item.IsChecked = isChecked;
            item.Click += handler;

            return item;
        }

        private void CpuValueMenuItemClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item == null)
            {
                return;
            }

            IsCpuValueShown = item.IsChecked;

            PersistValuePreferences();
        }

        private void MemoryValueMenuItemClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item == null)
            {
                return;
            }

            IsMemoryValueShown = item.IsChecked;

            PersistValuePreferences();
        }

        private static void PersistValuePreferences()
        {
            string error;

            // Ces deux réglages se posent une fois et doivent se retrouver au
            // démarrage suivant, comme les couleurs et la taille des tuiles.
            if (!DisplayPreferences.TrySetValueDisplay(IsCpuValueShown, IsMemoryValueShown, out error))
            {
                ReportSaveFailure(error);
            }

            ValueDisplayChanged();
        }

        /// <summary>
        /// Prévenu quand l'affichage des valeurs change : la fenêtre repeint les
        /// tuiles sans attendre le relevé suivant.
        /// </summary>
        public static Action ValueDisplayChanged = delegate { };

        /// <summary>
        /// Couleur de fond propre au processus. Grisée quand le chemin de
        /// l'exécutable n'est pas lisible : il n'y a alors rien sur quoi
        /// accrocher la couleur, exactement comme pour un réglage retenu.
        /// </summary>
        private MenuItem CreateBackgroundColorMenu()
        {
            MenuItem root = new MenuItem();
            root.Header = BackgroundColorHeader;

            if (!DisplayPreferences.CanRemember(this._process))
            {
                root.IsEnabled = false;
                root.ToolTip = DisplayPreferences.DescribeWhyNotRememberable(this._process);
                ToolTipService.SetShowOnDisabled(root, true);

                return root;
            }

            MenuItem choose = new MenuItem();
            choose.Header = "Choose…";
            choose.Click += ChooseBackgroundColorClick;

            root.Items.Add(choose);

            MenuItem general = new MenuItem();
            general.Header = "Use the general colour";
            general.IsEnabled = DisplayPreferences.GetBackgroundColor(this._process) != null;
            general.Click += ResetBackgroundColorClick;

            root.Items.Add(general);

            return root;
        }

        private void ChooseBackgroundColorClick(object sender, RoutedEventArgs e)
        {
            TileColorWindow window = new TileColorWindow(
                "\"" + this._process.ProcessName + "\"",
                DisplayPreferences.GetBackgroundColor(this._process));

            window.Owner = Window.GetWindow(this);

            if (window.ShowDialog() != true)
            {
                return;
            }

            string error;

            if (!DisplayPreferences.TrySetBackgroundColor(this._process, window.Result, out error))
            {
                ReportSaveFailure(error);
            }
        }

        private void ResetBackgroundColorClick(object sender, RoutedEventArgs e)
        {
            string error;

            if (!DisplayPreferences.TrySetBackgroundColor(this._process, null, out error))
            {
                ReportSaveFailure(error);
            }
        }

        /// <summary>
        /// Entrée « retenir pour toutes les occurrences du programme ». C'est la
        /// seule qui écrit sur le disque, et la seule qui porte sur plus d'une
        /// tuile — d'où le décompte dans l'intitulé.
        ///
        /// Grisée quand le chemin de l'exécutable n'est pas lisible : il n'y a
        /// alors rien sur quoi accrocher un réglage persistant. C'est le cas de
        /// près des deux tiers des processus en session non élevée, et c'est
        /// aussi pourquoi le geste direct reste, lui, toujours disponible.
        /// </summary>
        private MenuItem CreateRememberItem(bool isRemembered, RoutedEventHandler handler, string subject)
        {
            MenuItem item = new MenuItem();
            item.IsCheckable = true;
            item.IsChecked = isRemembered;

            int affected = CountEntriesSharingExecutable();

            item.Header = affected > 1
                ? "Remember " + subject + " for all " + affected + " instances"
                : "Remember " + subject + " for this program";

            if (!DisplayPreferences.CanRemember(this._process))
            {
                item.IsEnabled = false;
                item.IsChecked = false;
                item.ToolTip = DisplayPreferences.DescribeWhyNotRememberable(this._process);
                ToolTipService.SetShowOnDisabled(item, true);

                return item;
            }

            item.ToolTip = "Applies to every launch of \"" + this._process.ProcessName
                           + "\" and survives a restart of ProcessAffinity.";

            item.Click += handler;

            return item;
        }

        /// <summary>
        /// Nombre d'entrées affichées que la portée exécutable emporterait.
        /// </summary>
        private int CountEntriesSharingExecutable()
        {
            Func<Process[]> source = SelectionSource;
            string key = DisplayPreferences.GetExecutableKey(this._process);

            if (source == null || key == null)
            {
                return 1;
            }

            int count = 0;

            foreach (Process entry in source())
            {
                if (string.Equals(DisplayPreferences.GetExecutableKey(entry), key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count < 1 ? 1 : count;
        }

        private MenuItem CreateTileSizeMenu()
        {
            MenuItem root = new MenuItem();
            root.Header = TileSizeHeader;

            int current = DisplayPreferences.GetScale(this._process);

            for (int scale = 1; scale <= DisplayPreferences.MaximumScale; scale++)
            {
                MenuItem item = new MenuItem();
                item.Header = DescribeScale(scale);
                item.IsCheckable = true;
                item.IsChecked = scale == current;
                item.Tag = scale;
                item.Click += TileSizeMenuItemClick;

                root.Items.Add(item);
            }

            root.Items.Add(new Separator());

            root.Items.Add(CreateRememberItem(
                DisplayPreferences.GetScaleForExecutable(this._process) > 1,
                RememberScaleMenuItemClick,
                "size"));

            return root;
        }

        private void RememberPinMenuItemClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item == null)
            {
                return;
            }

            string error;

            if (!DisplayPreferences.TrySetPinnedForExecutable(this._process, item.IsChecked, out error))
            {
                ReportSaveFailure(error);
            }
        }

        private void RememberScaleMenuItemClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item == null)
            {
                return;
            }

            // On retient la taille courante de la tuile, celle que l'utilisateur
            // vient de régler. Décocher oublie le réglage du programme et rend
            // la tuile à sa taille d'instance.
            int scale = item.IsChecked ? DisplayPreferences.GetScale(this._process) : 1;

            string error;

            if (!DisplayPreferences.TrySetScaleForExecutable(this._process, scale, out error))
            {
                ReportSaveFailure(error);
            }
        }

        private static void ReportSaveFailure(string error)
        {
            MessageBox.Show(
                "The change was applied, but could not be saved for next time.\r\n\r\n" + error,
                ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string DescribeScale(int scale)
        {
            switch (scale)
            {
                case 1: return "Normal";
                case 2: return "Double";
                case 3: return "Triple";
                default: return scale + " times";
            }
        }

        private void TileSizeMenuItemClick(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;

            if (item == null || !(item.Tag is int))
            {
                return;
            }

            // Cette instance seulement, et rien n'est écrit : c'est « Remember »
            // qui fait durer.
            DisplayPreferences.SetScaleForInstance(this._process, (int)item.Tag);
        }

        /// <summary>
        /// Menu d'une tuile de service : les cinq commandes de contrôle.
        ///
        /// Les commandes indisponibles sont grisées, jamais retirées. Masquer
        /// ferait croire que la fonctionnalité n'existe pas, là où le grisé dit
        /// qu'elle existe et pourquoi elle ne s'applique pas ici. Encore
        /// faut-il que l'info-bulle s'affiche sur un élément désactivé, ce que
        /// WPF ne fait pas sans <c>ToolTipService.ShowOnDisabled</c>.
        ///
        /// « Kill » ne figure pas ici, et n'y figurait pas davantage avant :
        /// terminer l'hôte d'un service est brutal là où l'arrêt existe. Le
        /// chemin reste accessible depuis la tuile du processus hôte, qui
        /// avertit des services emportés.
        /// </summary>
        private void BuildServiceMenu(ContextMenu menu)
        {
            List<Process> selection = GetSelectedServices();

            bool actsOnSelection = selection.Count > 0;
            string scope = actsOnSelection ? " (" + selection.Count + " selected)" : string.Empty;

            List<ServiceSnapshot> snapshots = (actsOnSelection
                    ? selection.Select(service => service.ProcessName)
                    : new[] { this._process.ProcessName }.AsEnumerable())
                .Select(ServiceControl.Describe)
                .ToList();

            MenuItem title = new MenuItem();
            title.IsEnabled = false;

            if (actsOnSelection)
            {
                title.Header = "Service control — " + selection.Count + " selected";
            }
            else
            {
                ServiceSnapshot snapshot = snapshots[0];

                title.Header = "Service — " + (snapshot.IsReadable
                    ? ServiceControl.DescribeStatus(snapshot.Status)
                    : "state unavailable");

                if (!snapshot.IsReadable)
                {
                    title.ToolTip = snapshot.UnreadableReason;
                    ToolTipService.SetShowOnDisabled(title, true);
                }
            }

            menu.Items.Add(title);
            menu.Items.Add(new Separator());

            foreach (ServiceCommandEnum command in ServiceCommands.MenuOrder)
            {
                ServiceCommandAvailability availability = ServiceCommands.GetAvailability(snapshots, command);

                MenuItem menuItem = new MenuItem();
                menuItem.Header = ServiceCommands.GetHeader(command) + scope;
                menuItem.IsEnabled = availability.IsAvailable;

                if (!availability.IsAvailable)
                {
                    menuItem.ToolTip = availability.Reason;
                    ToolTipService.SetShowOnDisabled(menuItem, true);
                }

                ((MenuItem)menu.Items[menu.Items.Add(menuItem)]).Click +=
                    new RoutedEventHandler(ProcessUserControlContextMenuClick);
            }

            if (!actsOnSelection)
            {
                return;
            }

            menu.Items.Add(new Separator());

            MenuItem clearItem = new MenuItem();
            clearItem.Header = ClearSelectionHeader;
            ((MenuItem)menu.Items[menu.Items.Add(clearItem)]).Click +=
                new RoutedEventHandler(ProcessUserControlContextMenuClick);
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

            // Les réglages d'affichage précèdent tout le reste : ils valent sur
            // n'importe quelle tuile, et ne dépendent ni de la sélection ni des
            // droits.
            if (header == PinHeader || header == UnpinHeader)
            {
                TogglePin();
                return;
            }

            // Les commandes de service suivent leur propre chemin : leurs cibles
            // sont les services cochés, et non les processus.
            ServiceCommandEnum serviceCommand;

            if (this._process.IsService && ServiceCommands.TryParseHeader(header, out serviceCommand))
            {
                StartServiceCommand(serviceCommand);
                return;
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
                case KillHeader:
                    KillProcesses(targets);
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
        /// Épingle la tuile en tête d'affichage, ou l'en retire. Le panneau se
        /// réordonne par l'événement de <c>DisplayPreferences</c> : la tuile
        /// n'a pas à connaître la fenêtre.
        /// </summary>
        private void TogglePin()
        {
            bool pinned = DisplayPreferences.IsPinned(this._process);

            // Désépingler une tuile que le programme épingle de façon
            // persistante ne peut pas se faire par le seul réglage d'instance :
            // il faut aussi oublier le réglage du programme, sans quoi la tuile
            // remonterait aussitôt et le geste n'aurait aucun effet visible.
            if (pinned && DisplayPreferences.IsPinnedForExecutable(this._process))
            {
                string error;

                if (!DisplayPreferences.TrySetPinnedForExecutable(this._process, false, out error))
                {
                    ReportSaveFailure(error);
                }
            }

            DisplayPreferences.SetPinnedForInstance(this._process, !pinned);
        }

        /// <summary>
        /// Lance une commande de service et rend la main aussitôt.
        ///
        /// <c>async void</c> est ici le bon outil : c'est un gestionnaire
        /// d'événement, il n'y a rien à attendre côté appelant, et toute la
        /// suite — confirmation, exécution, rapport — se déroule dans
        /// <c>ExecuteAsync</c>. L'interface reste vivante pendant l'attente, et
        /// les tuiles continuent de se rafraîchir : c'est ainsi que le nouveau
        /// PID d'un service redémarré apparaît de lui-même.
        /// </summary>
        private async void StartServiceCommand(ServiceCommandEnum command)
        {
            // Sélection non vide : elle prime sur la tuile visée, comme pour les
            // processus. Une sélection mêlant services et processus n'apporte
            // ici que ses services.
            List<string> targets = GetSelectedServices()
                .Select(service => service.ProcessName)
                .ToList();

            if (targets.Count == 0)
            {
                targets.Add(this._process.ProcessName);
            }

            try
            {
                await ServiceCommands.ExecuteAsync(targets, command);
            }
            catch (Exception exception)
            {
                // Un async void qui lève emporte l'application : le filet est
                // obligatoire, si peu probable que soit le cas.
                MessageBox.Show(
                    "The service command could not be completed.\r\n\r\n" + exception.Message,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
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
        /// <summary>
        /// Termine les cibles de l'action — la sélection si elle n'est pas vide,
        /// sinon la seule tuile visée.
        ///
        /// « Kill » n'agissait que sur la tuile cliquée : la portée était
        /// calculée comme pour les autres commandes, puis ignorée. Trois tuiles
        /// cochées n'en terminaient qu'une, et rien ne le disait — c'est le plus
        /// trompeur des défauts possibles sur cette commande.
        /// </summary>
        private void KillProcesses(List<Process> targets)
        {
            // Refus avant toute confirmation : on ne demande pas à l'utilisateur
            // de valider une cascade dont une partie sera refusée de toute façon.
            List<string> criticalNames = targets
                .Where(process => process.IsCriticalSystemProcess)
                .Select(process => process.ProcessName)
                .ToList();

            List<Process> killable = targets
                .Where(process => !process.IsCriticalSystemProcess)
                .ToList();

            if (criticalNames.Count > 0 && killable.Count == 0)
            {
                MessageBox.Show(
                    DescribeCriticalRefusal(criticalNames),
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            if (!ConfirmKill(killable, criticalNames))
            {
                return;
            }

            TerminateProcesses(killable, criticalNames);
        }

        /// <summary>
        /// Termine les cibles retenues et rend compte. Séparée de la
        /// confirmation : celle-ci est modale, et cette partie-ci doit pouvoir
        /// être vérifiée.
        /// </summary>
        private static void TerminateProcesses(List<Process> killable, List<string> criticalNames)
        {
            List<string> notSent = new List<string>();
            List<string> failed = new List<string>();
            int killedCount = 0;

            foreach (Process process in killable)
            {
                uint? returnCode = process.Kill();

                if (returnCode == null)
                {
                    notSent.Add(process.ProcessName);
                    continue;
                }

                if (returnCode.Value != 0)
                {
                    failed.Add(process.ProcessName + " (" + DescribeTerminateReturnCode(returnCode.Value) + ")");
                    continue;
                }

                killedCount++;
            }

            ReportKill(killedCount, killable.Count + criticalNames.Count, criticalNames, notSent, failed);
        }

        private static string DescribeCriticalRefusal(List<string> names)
        {
            if (names.Count == 1)
            {
                return "\"" + names[0] + "\" is a critical system process.\r\n\r\n"
                       + "Terminating it would crash Windows. This action is not allowed.";
            }

            return names.Count + " of the selected entries are critical system processes:\r\n\r\n"
                   + string.Join(", ", names)
                   + "\r\n\r\nTerminating them would crash Windows. This action is not allowed.";
        }

        /// <summary>
        /// Confirmation. Une entrée de service emporte son hôte et tous les
        /// services qu'il héberge : cela se dit avant, et nommément.
        /// </summary>
        private bool ConfirmKill(List<Process> killable, List<string> criticalNames)
        {
            StringBuilder builder = new StringBuilder();

            if (killable.Count == 1)
            {
                builder.Append("Terminate \"").Append(killable[0].ProcessName).Append("\"?");
            }
            else
            {
                builder.Append("Terminate ").Append(killable.Count).Append(" processes?\r\n\r\n")
                       .Append(string.Join(", ", killable.Take(15).Select(process => process.ProcessName)));

                if (killable.Count > 15)
                {
                    builder.Append(", and ").Append(killable.Count - 15).Append(" more");
                }
            }

            foreach (Process process in killable.Where(entry => entry.IsService))
            {
                string hostName = string.IsNullOrEmpty(process.HostProcessName)
                    ? "its host process"
                    : "\"" + process.HostProcessName + "\"";

                builder.Append("\r\n\r\n\"").Append(process.ProcessName).Append("\" is a service. ")
                       .Append("Terminating it kills ").Append(hostName)
                       .Append(" (PID ").Append(process.ProcessID).Append(")");

                IList<string> sharedServiceNames = process.SharedServiceNames;

                if (sharedServiceNames != null && sharedServiceNames.Count > 1)
                {
                    builder.Append(", and with it the ").Append(sharedServiceNames.Count)
                           .Append(" services it hosts:\r\n").Append(string.Join(", ", sharedServiceNames));
                }

                builder.Append('.');
            }

            if (criticalNames.Count > 0)
            {
                builder.Append("\r\n\r\n").Append(criticalNames.Count)
                       .Append(criticalNames.Count == 1 ? " entry is a critical" : " entries are critical")
                       .Append(" system process and will be left alone: ")
                       .Append(string.Join(", ", criticalNames)).Append('.');
            }

            return MessageBox.Show(builder.ToString(), ApplicationName,
                       MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Rapport final. Silencieux quand tout a abouti, comme les autres
        /// applications partielles : la disparition des tuiles le dit déjà.
        /// </summary>
        private static void ReportKill(
            int killedCount, int totalCount, List<string> criticalNames,
            List<string> notSent, List<string> failed)
        {
            if (criticalNames.Count == 0 && notSent.Count == 0 && failed.Count == 0)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append("Terminated ").Append(killedCount).Append(" of ").Append(totalCount)
                   .Append(totalCount == 1 ? " entry." : " entries.").Append("\r\n");

            AppendKillSection(builder, criticalNames, "Refused — critical system process");
            AppendKillSection(builder, notSent, "The request could not be sent");
            AppendKillSection(builder, failed, "Windows refused the termination");

            MessageBox.Show(builder.ToString(), ApplicationName,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void AppendKillSection(StringBuilder builder, List<string> names, string title)
        {
            if (names.Count == 0)
            {
                return;
            }

            const int maximumListed = 15;

            builder.Append("\r\n").Append(title).Append(" (").Append(names.Count).Append("):\r\n")
                   .Append(string.Join(", ", names.Take(maximumListed)));

            if (names.Count > maximumListed)
            {
                builder.Append(", and ").Append(names.Count - maximumListed).Append(" more");
            }

            builder.Append("\r\n");
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

                // L'âge du dernier relevé, quand il n'est plus frais. C'est la
                // seule chose qui distingue « 96 % maintenant » de « 96 % il y
                // a huit heures », et c'est aussi ce qui rend visible une tuile
                // restée en place alors que son processus a disparu.
                if (IsSampleStale())
                {
                    text = text + "\r\nLast reading " + DescribeAge(this._process.SampleAgeSeconds)
                           + " ago — this value is not current.";
                }

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

    /// <summary>
    /// Couleur de base des barres de CPU, choisie par la priorité du processus.
    ///
    /// Les six teintes étaient écrites ici ; elles viennent désormais de la
    /// palette, qui les nomme et permet de les changer. L'alpha reste calculé
    /// depuis la charge et ne se règle pas : c'est lui qui fait la montée en
    /// intensité de la barre.
    /// </summary>
    public class ProcessAffinityColors
    {
        private readonly ProcessPriorityEnum _processPriorityEnum;
        private readonly Color _baseColor;

        public ProcessAffinityColors(int priority)
        {
            this._processPriorityEnum = Process.ToProcessPriorityEnum(priority);
            if (priority == 0) this._processPriorityEnum = Threading.ProcessPriorityEnum.Normal;

            this._baseColor = ColorPalette.Get(GetPaletteKey(this._processPriorityEnum));
        }

        public static string GetPaletteKey(ProcessPriorityEnum priority)
        {
            switch (priority)
            {
                case Threading.ProcessPriorityEnum.Idle: return ColorPalette.CpuIdle;
                case Threading.ProcessPriorityEnum.BelowNormal: return ColorPalette.CpuBelowNormal;
                case Threading.ProcessPriorityEnum.AboveNormal: return ColorPalette.CpuAboveNormal;
                case Threading.ProcessPriorityEnum.HighPriority: return ColorPalette.CpuHigh;
                case Threading.ProcessPriorityEnum.RealTime: return ColorPalette.CpuRealTime;
                default: return ColorPalette.CpuNormal;
            }
        }

        public ProcessPriorityEnum ProcessPriorityEnum { get { return this._processPriorityEnum; } }

        /// <summary>Teinte pleine, avant application de l'alpha de charge.</summary>
        public Color BaseColor { get { return this._baseColor; } }

        public int A { get { return 255; } }
        public int R { get { return this._baseColor.R; } }
        public int G { get { return this._baseColor.G; } }
        public int B { get { return this._baseColor.B; } }
    }
}
