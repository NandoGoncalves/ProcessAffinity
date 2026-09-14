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
        private List<ProcessUserControl> _processes = null;

        public ProcessAffinityWindow()
        {
            InitializeComponent();
        }

        public ProcessAffinityWindow(Process process)
        {
            InitializeComponent();
            SetProcess(process);
        }

        public ProcessAffinityWindow(List<ProcessUserControl> processes)
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

        public void SetProcesses(List<ProcessUserControl> processes)
        {
            this._processes = processes;
            InitializeProcessAffinityWrapPanel();
        }

        private void InitializeProcessAffinityWrapPanel()
        {
            try
            {
                ProcessAffinityUI.Threading.Processes processes = new Threading.Processes();

                // Affinité illisible, ou sélection multiple : on ne peut refléter
                // aucun masque courant, toutes les cases sont proposées cochées.
                nuint? processorAffinity = this._process == null
                    ? null
                    : this._process.GetProcessorAffinity();

                string processorsAffinities = ToBinary(
                    (ulong)(processorAffinity ?? 0),
                    processes.GetProcessorsProperties().NumberOfLogicalProcessors);

                for (int i = 0; i < processes.GetProcessorsProperties().NumberOfLogicalProcessors; i++)
                {

                    CheckBox checkBox = new CheckBox();
                    checkBox.Content = "CPU " + i.ToString();
                    checkBox.Height = 25;
                    checkBox.Width = 70;
                    checkBox.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
                    checkBox.VerticalContentAlignment = System.Windows.VerticalAlignment.Top;
                    checkBox.Name = "CPU" + i.ToString() + "CheckBox";
                    checkBox.Tag = i;
                    checkBox.Checked += CpuCheckBox_CheckedChanged;
                    checkBox.Unchecked += CpuCheckBox_CheckedChanged;
                    checkBox.IsChecked = processorAffinity == null
                        ? true
                        : ("1" == processorsAffinities.Substring((processorsAffinities.Length - (i + 1)), 1));
                    this.ProcessAffinityWrapPanel.Children.Add(checkBox);

                }



                // Instance créée uniquement pour lire le nombre de processeurs
                // logiques : sans cet arrêt, chaque ouverture de la fenêtre
                // laissait un échantillonneur CPU tourner indéfiniment.
                processes.StopCPUSampling();
                processes = null;

                UpdateSelectionState();
                ApplyModifiableState();

            }
            catch(Exception e)
            {
                MessageBox.Show(e.Message);
            }

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
            this.CloseButton.IsEnabled = false;
            this.NotModifiableTextBlock.Text = "Affinity cannot be changed without elevation.";
        }

        private void UpdateSelectionState()
        {
            List<CheckBox> cpuCheckBoxes = this.GetCPUCheckBoxes();

            bool anyChecked = cpuCheckBoxes.Any(cb => cb.IsChecked == true);
            bool allChecked = cpuCheckBoxes.Count > 0 && cpuCheckBoxes.All(cb => cb.IsChecked == true);

            this.CloseButton.IsEnabled = anyChecked && (this._process == null || this._process.IsModifiable);
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
            List<CheckBox> CPUCheckBoxes = new List<CheckBox>();

            foreach (UIElement element in this.ProcessAffinityWrapPanel.Children)
            {
                if (element.GetType() == typeof(CheckBox))
                {
                    CPUCheckBoxes.Add((CheckBox)element);
                }
            }

            return CPUCheckBoxes;
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
            else
            {
                SetProcessorAffinity();
            }


            this.Close();
        }

        private void SetProcessorAffinity()
        {
            nuint processorAffinity = 0;

            foreach (CheckBox cpuCheckBox in this.ProcessAffinityWrapPanel.Children)
            {
                if ((bool)cpuCheckBox.IsChecked)
                {
                    processorAffinity |= (nuint)1 << (int)cpuCheckBox.Tag;
                }
            }

            if (processorAffinity > 0)
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
        }

        /// <summary>
        /// Applique ce qui peut l'être et rapporte le reste : on n'annule pas ce
        /// qui a réussi.
        /// </summary>
        public void SetProcessorsAffinities()
        {
            int appliedCount = 0;
            List<string> notModifiableNames = new List<string>();

            for (int i = 0; i < _processes.Count; i++)
            {
                Process process = _processes[i].Process;

                if (!process.IsModifiable)
                {
                    notModifiableNames.Add(process.ProcessName);
                    continue;
                }

                _process = process;
                SetProcessorAffinity();
                appliedCount++;
            }

            ReportPartialApplication("Affinity", appliedCount, notModifiableNames);
        }

        /// <summary>
        /// Rapport final d'une application partielle. Silencieux quand tout a pu
        /// être appliqué.
        /// </summary>
        internal static void ReportPartialApplication(string subject, int appliedCount, List<string> notModifiableNames)
        {
            if (notModifiableNames.Count == 0)
            {
                return;
            }

            const int maximumListed = 15;

            StringBuilder builder = new StringBuilder();

            builder.Append(subject).Append(" applied to ").Append(appliedCount)
                   .Append(appliedCount > 1 ? " entries." : " entry.").Append("\r\n\r\n");

            builder.Append(notModifiableNames.Count)
                   .Append(notModifiableNames.Count > 1
                       ? " entries cannot be changed without elevation:"
                       : " entry cannot be changed without elevation:")
                   .Append("\r\n");

            builder.Append(string.Join(", ", notModifiableNames.Take(maximumListed)));

            if (notModifiableNames.Count > maximumListed)
            {
                builder.Append(", and ").Append(notModifiableNames.Count - maximumListed).Append(" more");
            }

            MessageBox.Show(builder.ToString(), "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Information);
        }

    }
}
