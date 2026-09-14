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
        private List<Process> _processes = null;

        public ProcessPriorityWindow(Process process)
        {
            InitializeComponent();

            this._process = process;
            ShowPriorityOf(process, process.ProcessName);
        }

        public ProcessPriorityWindow(List<Process> processes)
        {
            InitializeComponent();
            SetProcesses(processes);
        }

        public ProcessPriorityWindow SetProcess(Process process)
        {
            this._process = process;

            return this;
        }

        public void SetProcesses(List<Process> processes)
        {
            this._processes = processes;
            InitializePrioritySlider();
        }

        /// <summary>
        /// Le bouton « Close » applique : le curseur doit donc partir de la
        /// priorité en place, et jamais d'une valeur inventée. Il partait de zéro,
        /// si bien qu'ouvrir puis refermer la fenêtre appliquait « Idle » à toute
        /// la sélection.
        /// </summary>
        private void InitializePrioritySlider()
        {
            if (this._processes == null || this._processes.Count == 0)
            {
                this.PriorityLabel.Content = "0";
                PrioritySlider.Value = 0;

                return;
            }

            if (this._processes.Count == 1)
            {
                ShowPriorityOf(this._processes[0], this._processes[0].ProcessName);

                return;
            }

            // Plusieurs entrées : la priorité commune quand elles la partagent,
            // celle de la première sinon — signalée comme telle.
            int first = GetDisplayedPriority(this._processes[0]);
            bool allSame = this._processes.All(process => GetDisplayedPriority(process) == first);

            this.ProcessNameLabel.Content = this._processes.Count + " processes"
                + (allSame ? string.Empty : " — mixed priorities");

            this.PriorityLabel.Content = first.ToString();
            this.PrioritySlider.Value = first;
        }

        private void ShowPriorityOf(Process process, string name)
        {
            int priority = GetDisplayedPriority(process);

            this.ProcessNameLabel.Content = name;
            this.PriorityLabel.Content = priority.ToString();
            this.PrioritySlider.Value = priority;
        }

        /// <summary>
        /// Priorité à afficher, sur l'échelle 0-31 du curseur. La classe réelle
        /// est lue en natif : Win32_Process.Priority est figé à l'énumération et
        /// montrerait la valeur d'avant un changement venu de l'extérieur.
        /// </summary>
        private static int GetDisplayedPriority(Process process)
        {
            int? priorityClass = process.GetPriorityClass();

            return priorityClass == null ? process.Priority : ToSliderValue(priorityClass.Value);
        }

        /// <summary>
        /// Valeur de curseur représentative d'une classe de priorité.
        /// <see cref="Process.ToProcessPriorityEnum"/> fait la conversion inverse,
        /// et retrouve bien la classe d'origine à partir de ces valeurs.
        /// </summary>
        private static int ToSliderValue(int priorityClass)
        {
            switch (priorityClass)
            {
                case (int)ProcessPriorityEnum.Idle:
                    return 4;
                case (int)ProcessPriorityEnum.BelowNormal:
                    return 6;
                case (int)ProcessPriorityEnum.AboveNormal:
                    return 10;
                case (int)ProcessPriorityEnum.HighPriority:
                    return 13;
                case (int)ProcessPriorityEnum.RealTime:
                    return 24;
                default:
                    return 8;
            }
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

            // Une modification faite depuis l'application sur un processus sous
            // règle met la règle à jour.
            ProcessAffinityUI.Configuration.RuleEngine.UpdateIfRuled(this._process);
        }

            public void SetProcessesPriorities()
        {
            int appliedCount = 0;
            List<string> notModifiableNames = new List<string>();
            List<string> goneNames = new List<string>();

            for (int i = 0; i < _processes.Count; i++)
            {
                Process process = _processes[i];

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

                try
                {
                    SetProcessPriority();
                    appliedCount++;
                }
                catch
                {
                    //
                }

                
            }

            ProcessAffinityWindow.ReportPartialApplication("Priority", appliedCount, notModifiableNames, goneNames);
        }



    }
}
