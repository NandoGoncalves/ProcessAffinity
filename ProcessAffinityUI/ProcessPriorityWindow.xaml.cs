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
    /// Logique d'interaction pour ProcessPriorityWindow.xaml
    /// </summary>
    public partial class ProcessPriorityWindow : Window
    {
        private Process _process = null;
        private List<ProcessUserControl> _processes = null;

        public ProcessPriorityWindow(Process process)
        {
            InitializeComponent();

            this._process = process;
            this.ProcessNameLabel.Content = this._process.ProcessName;
            this.PriorityLabel.Content = this._process.Priority.ToString();
            this.PrioritySlider.Value = this._process.Priority;
        }

        public ProcessPriorityWindow(List<ProcessUserControl> processes)
        {
            InitializeComponent();
            SetProcesses(processes);
        }

        public ProcessPriorityWindow SetProcess(Process process)
        {
            this._process = process;

            return this;
        }

        public void SetProcesses(List<ProcessUserControl> processes)
        {
            this._processes = processes;
            InitializePrioritySlider();
        }

        private void InitializePrioritySlider()
        {
            this.PriorityLabel.Content = "0";
            PrioritySlider.Value = 0;
        }

        private void PrioritySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                this.PriorityLabel.Content = ((int)e.NewValue).ToString();
            }
            catch
            {
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_processes != null)
            {
                SetProcessesPriorities();
            }
            else
            {
                SetProcessPriority();
            }

            this.Close();
        }

        public void SetProcessPriority()
        {
            this._process.Priority = (int)Process.ToProcessPriorityEnum((int)this.PrioritySlider.Value);
        }

            public void SetProcessesPriorities()
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

                try
                {
                    SetProcessPriority();
                    appliedCount++;
                }
                catch
                {
                    //
                }

                _processes[i].SetProcessAffinityColors();
            }

            ProcessAffinityWindow.ReportPartialApplication("Priority", appliedCount, notModifiableNames);
        }



    }
}
