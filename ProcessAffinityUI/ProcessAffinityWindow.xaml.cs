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
                string processorsAffinities = ToBinary((ulong)(this._process == null?this._processes[0].Process.GetProcessorAffinity():this._process.GetProcessorAffinity()), processes.GetProcessorsProperties().NumberOfLogicalProcessors);

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
                    checkBox.IsChecked = this._process == null ? true : ("1" == processorsAffinities.Substring((processorsAffinities.Length - (i + 1)), 1));
                    this.ProcessAffinityWrapPanel.Children.Add(checkBox);

                }



                processes = null;

                UpdateSelectionState();

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

        private void UpdateSelectionState()
        {
            List<CheckBox> cpuCheckBoxes = this.GetCPUCheckBoxes();

            bool anyChecked = cpuCheckBoxes.Any(cb => cb.IsChecked == true);
            bool allChecked = cpuCheckBoxes.Count > 0 && cpuCheckBoxes.All(cb => cb.IsChecked == true);

            this.CloseButton.IsEnabled = anyChecked;
            this.SelectAllButton.Content = allChecked ? "Tout désélectionner" : "Tout sélectionner";
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

        public void SetProcessorsAffinities()
        {
            for (int i = 0; i < _processes.Count; i++)
            {
                    _process = _processes[i].Process;
                    SetProcessorAffinity();
            }
        }

    }
}
