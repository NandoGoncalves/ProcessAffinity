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
using System.Windows.Shapes;
using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Logique d'interaction pour ProcessAffinityWindow.xaml
    /// </summary>
    public partial class ProcessAffinityWindow : Window
    {
        private Process _process = null;
        private List<Process> _processes = null;

        /// <summary>
        /// Les cases à cocher, à plat et triées par numéro de processeur logique.
        /// Elles sont désormais réparties dans des conteneurs imbriqués — encadré
        /// de cœur, panneau de classe — et ne peuvent plus être retrouvées en
        /// parcourant les enfants directs du panneau.
        /// </summary>
        private readonly List<CheckBox> _cpuCheckBoxes = new List<CheckBox>();

        /// <summary>Cases des CPU Sets, mêmes conventions que celles de l'affinité.</summary>
        private readonly List<CheckBox> _cpuSetCheckBoxes = new List<CheckBox>();

        /// <summary>
        /// La section des CPU Sets est-elle présentée. Fausse quand l'API manque :
        /// on n'affiche pas un réglage qu'on ne saurait pas écrire.
        /// </summary>
        private bool _areCpuSetsShown = false;

        /// <summary>
        /// Vrai quand les entrées sélectionnées ne partagent pas le même réglage,
        /// ou qu'il est illisible : l'état est alors inconnu, et non « aucune
        /// restriction ». Les cases sont présentées indéterminées et la section
        /// n'est pas écrite tant que l'utilisateur n'a rien décidé.
        ///
        /// Sans cette distinction, un état inconnu s'affichait tout coché, se
        /// lisait comme « aucune préférence », et fermer la fenêtre sans rien
        /// toucher effaçait le réglage des deux entrées.
        /// </summary>
        private bool _isBuilding = false;
        private bool _isAffinityUndetermined = false;
        private bool _areCpuSetsUndetermined = false;

        public ProcessAffinityWindow()
        {
            InitializeComponent();
        }

        public ProcessAffinityWindow(Process process)
        {
            InitializeComponent();
            SetProcess(process);
        }

        public ProcessAffinityWindow(List<Process> processes)
        {
            InitializeComponent();
            SetProcesses(processes);
        }

        public ProcessAffinityWindow SetProcess(Process process)
        {
            this._process = process;
            InitializeProcessAffinityWrapPanel();

            return this;
        }

        public void SetProcesses(List<Process> processes)
        {
            this._processes = processes;
            InitializeProcessAffinityWrapPanel();
        }

        /// <summary>
        /// Masque à refléter dans les cases, ou null quand il n'y en a pas un
        /// seul à montrer — toutes les cases sont alors proposées cochées.
        ///
        /// Le bouton « Close » applique : partir de « tout coché » pour une seule
        /// entrée revenait à lui rendre tous les cœurs au simple fait d'ouvrir
        /// puis de refermer la fenêtre.
        /// </summary>
        private nuint? GetDisplayedAffinity()
        {
            if (this._process != null)
            {
                return this._process.GetProcessorAffinity();
            }

            if (this._processes == null || this._processes.Count == 0)
            {
                return null;
            }

            nuint? common = this._processes[0].GetProcessorAffinity();

            if (common == null)
            {
                return null;
            }

            // Plusieurs entrées : on ne reflète un masque que si elles le
            // partagent toutes. Sinon il n'y a rien de fidèle à montrer.
            for (int i = 1; i < this._processes.Count; i++)
            {
                nuint? affinity = this._processes[i].GetProcessorAffinity();

                if (affinity == null || affinity.Value != common.Value)
                {
                    return null;
                }
            }

            return common;
        }

        private void InitializeProcessAffinityWrapPanel()
        {
            try
            {
                this._isBuilding = true;

                ProcessAffinityUI.Threading.Processes processes = new Threading.Processes();

                int processorCount = processes.GetProcessorsProperties().NumberOfLogicalProcessors;

                // Instance créée uniquement pour lire le nombre de processeurs
                // logiques : sans cet arrêt, chaque ouverture de la fenêtre
                // laissait un échantillonneur CPU tourner indéfiniment.
                processes.StopCPUSampling();
                processes = null;

                nuint? processorAffinity = GetDisplayedAffinity();
                this._isAffinityUndetermined = processorAffinity == null;

                string processorsAffinities = ToBinary((ulong)(processorAffinity ?? 0), processorCount);

                this._cpuCheckBoxes.Clear();
                this._cpuSetCheckBoxes.Clear();
                this.ProcessAffinityWrapPanel.Children.Clear();

                SystemCpuSets.LogicalProcessor[] topology = SystemCpuSets.TryGet();

                // Repli : l'API n'existe qu'à partir de Windows 10, et une
                // topologie qui ne recouvre pas le nombre de processeurs annoncé
                // ne peut pas servir à les ranger. La grille plate reste
                // utilisable, l'information manque, c'est tout.
                bool grouped = topology != null && topology.Length == processorCount;

                // --- Affinité, la contrainte dure ---------------------------
                this.ProcessAffinityWrapPanel.Children.Add(CreateSectionHeader(
                    "Processor affinity — a hard limit",
                    "Windows will never run this process on an unticked processor."));

                this.ProcessAffinityWrapPanel.Children.Add(grouped
                    ? BuildGroupedGrid(topology, processorAffinity, processorsAffinities, this._cpuCheckBoxes)
                    : BuildFlatGrid(processorCount, processorAffinity, processorsAffinities, this._cpuCheckBoxes));

                if (grouped && topology.GroupBy(p => p.CoreIndex).Any(g => g.Count() > 1))
                {
                    this.ProcessAffinityWrapPanel.Children.Add(CreateFootnote(
                        "Two boxes inside the same core are the two threads of one physical core. "
                        + "Ticking both does not give two cores."));
                }

                // --- CPU Sets, la préférence --------------------------------
                // Section absente quand l'API manque : mieux vaut ne rien
                // proposer que proposer ce qui ne sera pas écrit.
                this._areCpuSetsShown = ProcessPowerThrottling.AreCpuSetsSupported && grouped;

                if (this._areCpuSetsShown)
                {
                    uint[] cpuSets = GetDisplayedCpuSets();
                    this._areCpuSetsUndetermined = cpuSets == null;

                    this.ProcessAffinityWrapPanel.Children.Add(CreateSectionHeader(
                        "CPU Sets — a preference",
                        "Windows normally keeps this process on the ticked processors, "
                        + "but it may use the others when it needs to. Tick everything to place no preference."));

                    // Distinction essentielle : une liste vide est « aucune
                    // préférence », un état connu qui se coche entièrement ; null
                    // est « on ne sait pas », et se montre indéterminé.
                    nuint? cpuSetMask = cpuSets == null ? (nuint?)null : ToMask(cpuSets, topology);

                    this.ProcessAffinityWrapPanel.Children.Add(BuildGroupedGrid(
                        topology,
                        cpuSetMask,
                        ToBinary(cpuSetMask ?? 0, processorCount),
                        this._cpuSetCheckBoxes));
                }

                // Une section indéterminée le dit, et dit aussi ce qui se passera
                // si l'utilisateur n'y touche pas.
                if (this._isAffinityUndetermined || this._areCpuSetsUndetermined)
                {
                    this.ProcessAffinityWrapPanel.Children.Add(CreateFootnote(
                        "Boxes shown as undetermined mean the selected processes do not share that setting, "
                        + "or that it could not be read. Those sections are left untouched unless you change them."));
                }

                this._isBuilding = false;

                UpdateSelectionState();
                ApplyModifiableState();

            }
            catch(Exception e)
            {
                this._isBuilding = false;

                MessageBox.Show(e.Message);
            }

        }

        /// <summary>
        /// Grille plate d'origine : une case par processeur logique, sans
        /// regroupement.
        /// </summary>
        private Panel BuildFlatGrid(int processorCount, nuint? processorAffinity, string processorsAffinities, List<CheckBox> target)
        {
            WrapPanel panel = new WrapPanel();

            for (int i = 0; i < processorCount; i++)
            {
                panel.Children.Add(CreateCpuCheckBox(i, processorAffinity, processorsAffinities, target));
            }

            return panel;
        }

        /// <summary>
        /// Cases rangées par classe d'efficacité, puis par cœur physique.
        ///
        /// Deux règles de sobriété : pas de libellé de classe quand il n'y en a
        /// qu'une — un intitulé unique pour la totalité des cases n'apprend rien —
        /// et pas d'encadré par cœur quand aucun cœur ne porte plus d'un fil, le
        /// regroupement n'ayant alors rien à signaler.
        /// </summary>
        private Panel BuildGroupedGrid(
            SystemCpuSets.LogicalProcessor[] topology, nuint? processorAffinity, string processorsAffinities, List<CheckBox> target)
        {
            List<int> efficiencyClasses = topology.Select(p => p.EfficiencyClass).Distinct().ToList();
            efficiencyClasses.Sort();
            efficiencyClasses.Reverse();

            bool showClassHeaders = efficiencyClasses.Count > 1;
            bool showCoreBoxes = topology.GroupBy(p => p.CoreIndex).Any(g => g.Count() > 1);

            // CoreIndex n'est pas une numérotation continue — sur cette machine il
            // vaut 0, 2, 4, 6, 8, 10 pour six cœurs. On numérote les cœurs dans
            // l'ordre de leur premier processeur logique.
            Dictionary<int, int> coreNumberByCoreIndex = new Dictionary<int, int>();

            foreach (var core in topology.GroupBy(p => p.CoreIndex).OrderBy(g => g.Min(p => p.Index)))
            {
                coreNumberByCoreIndex[core.Key] = coreNumberByCoreIndex.Count;
            }

            StackPanel section = new StackPanel();

            foreach (int efficiencyClass in efficiencyClasses)
            {
                if (showClassHeaders)
                {
                    section.Children.Add(CreateGroupHeader(
                        GetEfficiencyClassText(efficiencyClass, efficiencyClasses)));
                }

                WrapPanel classPanel = new WrapPanel();

                var cores = topology
                    .Where(p => p.EfficiencyClass == efficiencyClass)
                    .GroupBy(p => p.CoreIndex)
                    .OrderBy(g => g.Min(p => p.Index));

                foreach (var core in cores)
                {
                    List<SystemCpuSets.LogicalProcessor> threads =
                        core.OrderBy(p => p.Index).ToList();

                    if (!showCoreBoxes)
                    {
                        foreach (SystemCpuSets.LogicalProcessor thread in threads)
                        {
                            classPanel.Children.Add(
                                CreateCpuCheckBox(thread.Index, processorAffinity, processorsAffinities, target));
                        }

                        continue;
                    }

                    classPanel.Children.Add(CreateCoreBox(
                        coreNumberByCoreIndex[core.Key], threads, processorAffinity, processorsAffinities, target));
                }

                section.Children.Add(classPanel);
            }

            return section;
        }

        /// <summary>
        /// Encadré d'un cœur physique : son numéro, puis ses fils.
        /// </summary>
        private Border CreateCoreBox(
            int coreNumber,
            List<SystemCpuSets.LogicalProcessor> threads,
            nuint? processorAffinity,
            string processorsAffinities,
            List<CheckBox> target)
        {
            StackPanel threadPanel = new StackPanel();
            threadPanel.Orientation = Orientation.Horizontal;

            foreach (SystemCpuSets.LogicalProcessor thread in threads)
            {
                threadPanel.Children.Add(
                    CreateCpuCheckBox(thread.Index, processorAffinity, processorsAffinities, target));
            }

            TextBlock header = new TextBlock();
            header.Text = "Core " + coreNumber.ToString();
            header.FontSize = 10;
            header.FontWeight = FontWeights.SemiBold;
            header.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            header.Margin = new Thickness(2, 0, 0, 2);

            StackPanel content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(threadPanel);

            Border box = new Border();
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(3);
            box.Padding = new Thickness(4, 3, 4, 3);
            box.Margin = new Thickness(0, 0, 6, 6);
            box.Child = content;

            return box;
        }

        private CheckBox CreateCpuCheckBox(int index, nuint? processorAffinity, string processorsAffinities, List<CheckBox> target)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Content = "CPU " + index.ToString();
            checkBox.Height = 25;
            checkBox.Width = 62;
            checkBox.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            checkBox.VerticalContentAlignment = System.Windows.VerticalAlignment.Top;
            checkBox.Name = "CPU" + index.ToString() + "CheckBox";
            checkBox.Tag = index;
            checkBox.Checked += CpuCheckBox_CheckedChanged;
            checkBox.Unchecked += CpuCheckBox_CheckedChanged;
            // Null ne veut pas dire « tout » : il veut dire « on ne sait pas ».
            // La case le montre plutôt que d'affirmer un état.
            checkBox.IsChecked = processorAffinity == null
                ? (bool?)null
                : ("1" == processorsAffinities.Substring((processorsAffinities.Length - (index + 1)), 1));

            checkBox.Indeterminate += CpuCheckBox_CheckedChanged;

            // Les cases sont désormais réparties dans des conteneurs imbriqués :
            // la liste, elle, reste à plat et triée par numéro de processeur, car
            // SetCPUCheckBox l'indexe et SetProcessorAffinity la parcourt.
            target.Add(checkBox);
            target.Sort((x, y) => ((int)x.Tag).CompareTo((int)y.Tag));

            return checkBox;
        }

        /// <summary>
        /// Intitulé d'une section, suivi de la phrase qui dit ce que Windows en
        /// fait. C'est elle qui distingue la contrainte dure de la préférence,
        /// sans laquelle les deux grilles se ressembleraient trait pour trait.
        /// </summary>
        private static StackPanel CreateSectionHeader(string title, string explanation)
        {
            TextBlock header = new TextBlock();
            header.Text = title;
            header.FontWeight = FontWeights.Bold;
            header.Margin = new Thickness(0, 8, 0, 1);

            TextBlock note = new TextBlock();
            note.Text = explanation;
            note.FontSize = 10;
            note.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 0, 0, 5);

            StackPanel panel = new StackPanel();
            panel.Children.Add(header);
            panel.Children.Add(note);

            return panel;
        }

        /// <summary>
        /// Masque équivalent à une liste d'identifiants de CPU Set. Les
        /// identifiants sont opaques : c'est la topologie qui fait le lien avec
        /// les numéros de processeur logique.
        /// </summary>
        private static nuint ToMask(uint[] cpuSetIds, SystemCpuSets.LogicalProcessor[] topology)
        {
            if (cpuSetIds == null || cpuSetIds.Length == 0 || topology == null)
            {
                // Aucune restriction : toutes les cases cochées, ce que dit la
                // phrase de la section.
                nuint all = 0;

                if (topology != null)
                {
                    foreach (SystemCpuSets.LogicalProcessor processor in topology)
                    {
                        all |= (nuint)1 << processor.Index;
                    }
                }

                return all;
            }

            nuint mask = 0;

            foreach (SystemCpuSets.LogicalProcessor processor in topology)
            {
                if (Array.IndexOf(cpuSetIds, processor.Id) >= 0)
                {
                    mask |= (nuint)1 << processor.Index;
                }
            }

            return mask;
        }

        /// <summary>
        /// CPU Sets à refléter, selon la même règle que l'affinité : ceux de
        /// l'entrée unique, ceux que toutes partagent, ou rien à montrer.
        /// </summary>
        private uint[] GetDisplayedCpuSets()
        {
            if (this._process != null)
            {
                return ProcessPowerThrottling.GetDefaultCpuSets(this._process.ProcessID);
            }

            if (this._processes == null || this._processes.Count == 0)
            {
                return null;
            }

            uint[] common = ProcessPowerThrottling.GetDefaultCpuSets(this._processes[0].ProcessID);

            if (common == null)
            {
                return null;
            }

            for (int i = 1; i < this._processes.Count; i++)
            {
                uint[] sets = ProcessPowerThrottling.GetDefaultCpuSets(this._processes[i].ProcessID);

                if (sets == null || sets.Length != common.Length
                    || !sets.OrderBy(id => id).SequenceEqual(common.OrderBy(id => id)))
                {
                    return null;
                }
            }

            return common;
        }

        private static TextBlock CreateGroupHeader(string text)
        {
            TextBlock header = new TextBlock();
            header.Text = text;
            header.FontWeight = FontWeights.Bold;
            header.Margin = new Thickness(0, 4, 0, 4);

            return header;
        }

        private static TextBlock CreateFootnote(string text)
        {
            TextBlock note = new TextBlock();
            note.Text = text;
            note.FontSize = 10;
            note.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(0, 2, 0, 0);

            return note;
        }

        /// <summary>
        /// Libellé d'une classe d'efficacité. La plus élevée porte les cœurs de
        /// performance, la plus basse ceux d'efficacité ; entre les deux, s'il
        /// existe un jour des architectures à trois classes, le numéro brut.
        /// </summary>
        private static string GetEfficiencyClassText(int efficiencyClass, List<int> orderedClasses)
        {
            if (efficiencyClass == orderedClasses[0])
            {
                return "Performance cores";
            }

            if (efficiencyClass == orderedClasses[orderedClasses.Count - 1])
            {
                return "Efficiency cores";
            }

            return "Cores — efficiency class " + efficiencyClass.ToString();
        }


        /// <summary>
        /// Une action de l'utilisateur sur une case lève l'indétermination de sa
        /// grille : il vient de décider, la section sera donc écrite. Les cases
        /// restées indéterminées comptent alors pour décochées.
        /// </summary>
        private void CpuCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;

            // Pendant la construction de la grille, les cases sont posées par le
            // code : ce n'est pas une décision de l'utilisateur.
            if (!this._isBuilding && checkBox != null)
            {
                if (this._cpuSetCheckBoxes.Contains(checkBox))
                {
                    this._areCpuSetsUndetermined = false;
                }
                else
                {
                    this._isAffinityUndetermined = false;
                }
            }

            UpdateSelectionState();
        }

        /// <summary>
        /// Entrée unique non modifiable : cocher des cases et valider n'aurait
        /// aucun effet. On désactive la saisie et on le dit. La fenêtre se ferme
        /// par sa case système.
        /// </summary>
        private void ApplyModifiableState()
        {
            if (this._process == null || this._process.IsModifiable)
            {
                return;
            }

            // Les CPU Sets exigent les mêmes droits en écriture que l'affinité :
            // les deux grilles se désactivent ensemble.
            foreach (CheckBox cpuCheckBox in this.GetCPUCheckBoxes())
            {
                cpuCheckBox.IsEnabled = false;
            }

            foreach (CheckBox cpuSetCheckBox in this._cpuSetCheckBoxes)
            {
                cpuSetCheckBox.IsEnabled = false;
            }

            this.SelectAllButton.IsEnabled = false;
            this.NotModifiableTextBlock.Text = "Affinity and CPU Sets cannot be changed without elevation.";
        }

        private void UpdateSelectionState()
        {
            // Le bouton reste actif en toutes circonstances : il est aussi la
            // sortie de la fenêtre. Le désactiver privait l'utilisateur de la
            // sienne. Quand il n'y a rien à appliquer, il se contente de fermer.
            this.SelectAllButton.Content = AreAllBoxesChecked() ? "Deselect all" : "Select all";

            UpdateConflictWarning();
        }

        /// <summary>
        /// Une préférence sur un processeur que l'affinité interdit ne produit
        /// rien : la contrainte dure prime. On le signale sans rien corriger — la
        /// saisie appartient à l'utilisateur, qui peut très bien être en train de
        /// préparer les deux sections dans l'ordre qui lui convient.
        /// </summary>
        private void UpdateConflictWarning()
        {
            if (this.ConflictTextBlock == null)
            {
                return;
            }

            if (!this._areCpuSetsShown)
            {
                this.ConflictTextBlock.Text = string.Empty;

                return;
            }

            List<int> ignored = new List<int>();

            foreach (CheckBox cpuSetCheckBox in this._cpuSetCheckBoxes)
            {
                if (cpuSetCheckBox.IsChecked != true)
                {
                    continue;
                }

                int index = (int)cpuSetCheckBox.Tag;

                CheckBox affinityCheckBox = this._cpuCheckBoxes
                    .FirstOrDefault(cb => (int)cb.Tag == index);

                if (affinityCheckBox != null && affinityCheckBox.IsChecked == false)
                {
                    ignored.Add(index);
                }
            }

            if (ignored.Count == 0)
            {
                this.ConflictTextBlock.Text = string.Empty;

                return;
            }

            this.ConflictTextBlock.Text =
                (ignored.Count == 1 ? "CPU " + ignored[0] + " is preferred" : "CPUs " + string.Join(", ", ignored) + " are preferred")
                + " but excluded by the affinity above, so the preference has no effect — the hard limit wins.";
        }

        /// <summary>
        /// Le bouton porte sur les deux grilles. Il ne touchait que l'affinité,
        /// si bien que « Deselect all » laissait les CPU Sets entièrement cochés
        /// et que le libellé décrivait la moitié de ce qu'on voyait.
        /// </summary>
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            bool check = !AreAllBoxesChecked();

            foreach (CheckBox checkBox in AllCheckBoxes())
            {
                checkBox.IsChecked = check;
            }

            UpdateSelectionState();
        }

        /// <summary>Les cases des deux sections, dans l'ordre d'affichage.</summary>
        private IEnumerable<CheckBox> AllCheckBoxes()
        {
            return this._cpuCheckBoxes.Concat(this._cpuSetCheckBoxes);
        }

        private bool AreAllBoxesChecked()
        {
            List<CheckBox> all = AllCheckBoxes().ToList();

            return all.Count > 0 && all.All(cb => cb.IsChecked == true);
        }

        public List<CheckBox> GetCPUCheckBoxes()
        {
            return this._cpuCheckBoxes;
        }

        public void SetCPUCheckBox(int CPUCheckBox, bool isChecked)
        {
            this.SetCPUCheckBox(this.GetCPUCheckBoxes()[CPUCheckBox], isChecked);
        }

        public void SetCPUCheckBox(CheckBox CPUCheckBox, bool isChecked)
        {
            CPUCheckBox.IsChecked = isChecked;
        }

        public void SetCPUCheckBoxesUnchecked()
        {
            foreach (CheckBox CPUCheckBox in this.GetCPUCheckBoxes())
            {
                this.SetCPUCheckBox(CPUCheckBox, false);
            }
        }

        public void SetCPUCheckBoxesChecked()
        {
            foreach (CheckBox CPUCheckBox in this.GetCPUCheckBoxes())
            {
                this.SetCPUCheckBox(CPUCheckBox, true);
            }
        }

        public bool IsCPUCheckBoxChecked(int CPUCheckBox)
        {
            return (bool)this.GetCPUCheckBoxes()[CPUCheckBox].IsChecked;
        }
        
        public static string ToBinary(ulong Decimal, int bitsNumber)
        {
            // Declare a few variables we're going to need
            ulong BinaryHolder;
            char[] BinaryArray;
            string BinaryResult = "";

            while (Decimal > 0)
            {
                BinaryHolder = Decimal % 2;
                BinaryResult += BinaryHolder;
                Decimal = Decimal / 2;
            }


            BinaryArray = BinaryResult.ToCharArray();
            Array.Reverse(BinaryArray);
            BinaryResult = new string(BinaryArray);

            BinaryResult = new string('0', bitsNumber) + BinaryResult;
            BinaryResult = BinaryResult.Substring(BinaryResult.Length - bitsNumber);

            return  BinaryResult ;
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_processes != null)
            {
                SetProcessorsAffinities();
            }
            else if (this._process != null && this._process.IsModifiable)
            {
                // Entrée non modifiable : on ferme sans tenter une écriture qui
                // échouerait de toute façon. Un masque vide est déjà écarté par
                // SetProcessorAffinity.
                SetProcessorAffinity();
            }

            this.Close();
        }

        private void SetProcessorAffinity()
        {
            nuint processorAffinity = 0;

            foreach (CheckBox cpuCheckBox in this._cpuCheckBoxes)
            {
                if (cpuCheckBox.IsChecked == true)
                {
                    processorAffinity |= (nuint)1 << (int)cpuCheckBox.Tag;
                }
            }

            // Indéterminée : l'utilisateur n'a rien décidé, on n'écrit pas.
            if (processorAffinity > 0 && !this._isAffinityUndetermined)
            {
                try
                {
                    this._process.SetProcessorAffinity(processorAffinity);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            // Les CPU Sets ne dépendent pas de l'affinité : les imbriquer dans
            // son écriture les privait de toute prise quand aucune case
            // d'affinité n'était cochée.
            ApplyCpuSets();

            // Une modification faite depuis l'application sur un processus sous
            // règle met la règle à jour : sans cela le prochain lancement
            // rétablirait l'ancienne valeur, et l'utilisateur croirait son
            // changement perdu.
            ProcessAffinityUI.Configuration.RuleEngine.UpdateIfRuled(this._process);
        }

        /// <summary>
        /// Écrit les CPU Sets du processus courant. Tout coché — ou rien — vaut
        /// « aucune préférence » : on lève alors la restriction plutôt que de
        /// décrire l'intégralité des processeurs, ce qui revient au même pour
        /// l'ordonnanceur mais se relit moins bien.
        /// </summary>
        private void ApplyCpuSets()
        {
            // Indéterminés : les entrées ne partageaient pas le même réglage et
            // l'utilisateur n'y a pas touché. Écrire ici effaçait les
            // restrictions des deux entrées d'un simple aller-retour.
            if (!this._areCpuSetsShown || this._process == null || this._areCpuSetsUndetermined)
            {
                return;
            }

            SystemCpuSets.LogicalProcessor[] topology = SystemCpuSets.TryGet();

            if (topology == null)
            {
                return;
            }

            List<uint> selected = new List<uint>();

            foreach (CheckBox checkBox in this._cpuSetCheckBoxes)
            {
                if (checkBox.IsChecked != true)
                {
                    continue;
                }

                int index = (int)checkBox.Tag;

                foreach (SystemCpuSets.LogicalProcessor processor in topology)
                {
                    if (processor.Index == index)
                    {
                        selected.Add(processor.Id);
                        break;
                    }
                }
            }

            bool unrestricted = selected.Count == 0 || selected.Count == this._cpuSetCheckBoxes.Count;

            ProcessPowerThrottling.TrySetDefaultCpuSets(
                this._process.ProcessID, unrestricted ? null : selected.ToArray());
        }

        /// <summary>
        /// Applique ce qui peut l'être et rapporte le reste : on n'annule pas ce
        /// qui a réussi.
        /// </summary>
        public void SetProcessorsAffinities()
        {
            int appliedCount = 0;
            List<string> notModifiableNames = new List<string>();
            List<string> goneNames = new List<string>();

            for (int i = 0; i < _processes.Count; i++)
            {
                Process process = _processes[i];

                // Terminé depuis l'ouverture de la fenêtre : l'écriture serait
                // avalée sans bruit et compterait pour une réussite.
                if (!process.IsRunning)
                {
                    goneNames.Add(process.ProcessName);
                    continue;
                }

                if (!process.IsModifiable)
                {
                    notModifiableNames.Add(process.ProcessName);
                    continue;
                }

                _process = process;
                SetProcessorAffinity();
                appliedCount++;
            }

            // Le sujet du rapport nomme ce qui a réellement été écrit : une
            // section laissée indéterminée n'est pas appliquée, et le dire
            // évite de laisser croire le contraire.
            bool affinityWritten = !this._isAffinityUndetermined;
            bool cpuSetsWritten = this._areCpuSetsShown && !this._areCpuSetsUndetermined;

            string subject = affinityWritten && cpuSetsWritten ? "Affinity and CPU Sets"
                : cpuSetsWritten ? "CPU Sets"
                : "Affinity";

            ReportPartialApplication(subject, appliedCount, notModifiableNames, goneNames);
        }

        /// <summary>
        /// Rapport final d'une application partielle. Silencieux quand tout a pu
        /// être appliqué.
        /// </summary>
        internal static void ReportPartialApplication(string subject, int appliedCount, List<string> notModifiableNames)
        {
            ReportPartialApplication(subject, appliedCount, notModifiableNames, new List<string>());
        }

        internal static void ReportPartialApplication(
            string subject, int appliedCount, List<string> notModifiableNames, List<string> goneNames)
        {
            if (notModifiableNames.Count == 0 && goneNames.Count == 0)
            {
                return;
            }

            StringBuilder builder = new StringBuilder();

            builder.Append(subject).Append(" applied to ").Append(appliedCount)
                   .Append(appliedCount == 1 ? " entry." : " entries.").Append("\r\n");

            AppendNames(builder, notModifiableNames,
                " cannot be changed without elevation:",
                " cannot be changed without elevation:");

            AppendNames(builder, goneNames,
                " is no longer running:",
                " are no longer running:");

            MessageBox.Show(builder.ToString(), "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void AppendNames(StringBuilder builder, List<string> names, string singular, string plural)
        {
            if (names == null || names.Count == 0)
            {
                return;
            }

            const int maximumListed = 15;

            builder.Append("\r\n").Append(names.Count)
                   .Append(names.Count == 1 ? " entry" : " entries")
                   .Append(names.Count == 1 ? singular : plural)
                   .Append("\r\n");

            builder.Append(string.Join(", ", names.Take(maximumListed)));

            if (names.Count > maximumListed)
            {
                builder.Append(", and ").Append(names.Count - maximumListed).Append(" more");
            }

            builder.Append("\r\n");
        }

    }
}
