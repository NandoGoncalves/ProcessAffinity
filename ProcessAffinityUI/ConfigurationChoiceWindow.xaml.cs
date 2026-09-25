using System.Collections.Generic;
using System.Windows;
using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Une règle est attachée au chemin de l'exécutable. Quand la sélection tient
    /// plusieurs instances du même programme réglées différemment, il n'existe pas
    /// de choix défendable : en retenir une au hasard graverait une configuration
    /// que l'utilisateur n'a pas désignée, et il ne s'en apercevrait qu'au
    /// prochain lancement du programme.
    /// </summary>
    public partial class ConfigurationChoiceWindow : Window
    {
        public ConfigurationChoiceWindow(string processName, List<ConfigurationCommands.Candidate> candidates)
        {
            InitializeComponent();

            this.HeaderTextBlock.Text =
                "\"" + processName + "\" is selected " + DescribeInstanceCount(candidates)
                + ", with " + candidates.Count + " different configurations.";

            this.ChoicesListBox.ItemsSource = candidates;
            this.ChoicesListBox.SelectedIndex = 0;
        }

        /// <summary>La configuration retenue, ou null si l'exécutable est passé.</summary>
        public ConfigurationCommands.Candidate Chosen { get; private set; }

        private static string DescribeInstanceCount(List<ConfigurationCommands.Candidate> candidates)
        {
            int instances = 0;

            foreach (ConfigurationCommands.Candidate candidate in candidates)
            {
                instances += candidate.Instances.Count;
            }

            return instances + (instances == 1 ? " time" : " times");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            this.Chosen = this.ChoicesListBox.SelectedItem as ConfigurationCommands.Candidate;
            this.DialogResult = this.Chosen != null;
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            this.Chosen = null;
            this.DialogResult = false;
        }
    }
}
