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
                ProcessAffinityUI.Threading.Processes processes = new Threading.Processes();

                int processorCount = processes.GetProcessorsProperties().NumberOfLogicalProcessors;

                // Instance créée uniquement pour lire le nombre de processeurs
                // logiques : sans cet arrêt, chaque ouverture de la fenêtre
                // laissait un échantillonneur CPU tourner indéfiniment.
                processes.StopCPUSampling();
                processes = null;

                nuint? processorAffinity = GetDisplayedAffinity();

                string processorsAffinities = ToBinary((ulong)(processorAffinity ?? 0), processorCount);

                this._cpuCheckBoxes.Clear();
                this.ProcessAffinityWrapPanel.Children.Clear();

                SystemCpuSets.LogicalProcessor[] topology = SystemCpuSets.TryGet();

                if (topology == null || topology.Length != processorCount)
                {
                    // Repli : l'API n'existe qu'à partir de Windows 10, et une
                    // topologie qui ne recouvre pas le nombre de processeurs
                    // annoncé ne peut pas servir à les ranger. La grille plate
                    // reste utilisable, l'information manque, c'est tout.
                    BuildFlatGrid(processorCount, processorAffinity, processorsAffinities);
                }
                else
                {
                    BuildGroupedGrid(topology, processorAffinity, processorsAffinities);
                }

                UpdateSelectionState();
                ApplyModifiableState();

            }
            catch(Exception e)
            {
                MessageBox.Show(e.Message);
            }

        }

        /// <summary>
        /// Grille plate d'origine : une case par processeur logique, sans
        /// regroupement.
        /// </summary>
        private void BuildFlatGrid(int processorCount, nuint? processorAffinity, string processorsAffinities)
        {
            WrapPanel panel = new WrapPanel();

            for (int i = 0; i < processorCount; i++)
            {
                panel.Children.Add(CreateCpuCheckBox(i, processorAffinity, processorsAffinities));
            }

            this.ProcessAffinityWrapPanel.Children.Add(panel);
        }

        /// <summary>
        /// Cases rangées par classe d'efficacité, puis par cœur physique.
        ///
        /// Deux règles de sobriété : pas de libellé de classe quand il n'y en a
        /// qu'une — un intitulé unique pour la totalité des cases n'apprend rien —
        /// et pas d'encadré par cœur quand aucun cœur ne porte plus d'un fil, le
        /// regroupement n'ayant alors rien à signaler.
        /// </summary>
        private void BuildGroupedGrid(
            SystemCpuSets.LogicalProcessor[] topology, nuint? processorAffinity, string processorsAffinities)
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

            foreach (int efficiencyClass in efficiencyClasses)
            {
                if (showClassHeaders)
                {
                    this.ProcessAffinityWrapPanel.Children.Add(CreateGroupHeader(
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
                                CreateCpuCheckBox(thread.Index, processorAffinity, processorsAffinities));
                        }

                        continue;
                    }

                    classPanel.Children.Add(CreateCoreBox(
                        coreNumberByCoreIndex[core.Key], threads, processorAffinity, processorsAffinities));
                }

                this.ProcessAffinityWrapPanel.Children.Add(classPanel);
            }

            if (showCoreBoxes)
            {
                this.ProcessAffinityWrapPanel.Children.Add(CreateFootnote(
                    "Two boxes inside the same core are the two threads of one physical core. "
                    + "Ticking both does not give two cores."));
            }
        }

        /// <summary>
        /// Encadré d'un cœur physique : son numéro, puis ses fils.
        /// </summary>
        private Border CreateCoreBox(
            int coreNumber,
            List<SystemCpuSets.LogicalProcessor> threads,
            nuint? processorAffinity,
            string processorsAffinities)
        {
            StackPanel threadPanel = new StackPanel();
            threadPanel.Orientation = Orientation.Horizontal;

            foreach (SystemCpuSets.LogicalProcessor thread in threads)
            {
                threadPanel.Children.Add(
                    CreateCpuCheckBox(thread.Index, processorAffinity, processorsAffinities));
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

        private CheckBox CreateCpuCheckBox(int index, nuint? processorAffinity, string processorsAffinities)
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
            checkBox.IsChecked = processorAffinity == null
                ? true
                : ("1" == processorsAffinities.Substring((processorsAffinities.Length - (index + 1)), 1));

            // Les cases sont désormais réparties dans des conteneurs imbriqués :
            // la liste, elle, reste à plat et triée par numéro de processeur, car
            // SetCPUCheckBox l'indexe et SetProcessorAffinity la parcourt.
            this._cpuCheckBoxes.Add(checkBox);
            this._cpuCheckBoxes.Sort((x, y) => ((int)x.Tag).CompareTo((int)y.Tag));

            return checkBox;
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


        private void CpuCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
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

            foreach (CheckBox cpuCheckBox in this.GetCPUCheckBoxes())
            {
                cpuCheckBox.IsEnabled = false;
            }

            this.SelectAllButton.IsEnabled = false;
            this.NotModifiableTextBlock.Text = "Affinity cannot be changed without elevation.";
        }

        private void UpdateSelectionState()
        {
            List<CheckBox> cpuCheckBoxes = this.GetCPUCheckBoxes();

            bool anyChecked = cpuCheckBoxes.Any(cb => cb.IsChecked == true);
            bool allChecked = cpuCheckBoxes.Count > 0 && cpuCheckBoxes.All(cb => cb.IsChecked == true);

            // Le bouton reste actif en toutes circonstances : il est aussi la
            // sortie de la fenêtre. Le désactiver privait l'utilisateur de la
            // sienne. Quand il n'y a rien à appliquer, il se contente de fermer.
            this.SelectAllButton.Content = allChecked ? "Deselect all" : "Select all";
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            List<CheckBox> cpuCheckBoxes = this.GetCPUCheckBoxes();
            bool allChecked = cpuCheckBoxes.Count > 0 && cpuCheckBoxes.All(cb => cb.IsChecked == true);

            if (allChecked)
            {
                this.SetCPUCheckBoxesUnchecked();
            }
            else
            {
                this.SetCPUCheckBoxesChecked();
            }

            UpdateSelectionState();
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

            if (processorAffinity > 0)
            {
                try
                {
                    this._process.SetProcessorAffinity(processorAffinity);

                    // Une modification faite depuis l'application sur un processus
                    // sous règle met la règle à jour : sans cela le prochain
                    // lancement rétablirait l'ancienne valeur, et l'utilisateur
                    // croirait son changement perdu.
                    ProcessAffinityUI.Configuration.RuleEngine.UpdateIfRuled(this._process);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
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

            ReportPartialApplication("Affinity", appliedCount, notModifiableNames, goneNames);
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
