using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Nom, version et dépendances de l'application, plus les indicateurs
    /// d'abonnement aux événements qui encombraient la barre du bas.
    /// </summary>
    public partial class AboutWindow : Window
    {
        /// <summary>Une ligne de la liste des dépendances.</summary>
        public sealed class DependencyEntry
        {
            public string Name { get; set; }

            public string Version { get; set; }
        }

        /// <param name="eventSubscriptionCounts">
        /// Les quatre compteurs d'abonnement, ou null quand la couche
        /// d'échantillonnage n'est pas encore en place.
        /// </param>
        public AboutWindow(int[] eventSubscriptionCounts)
        {
            InitializeComponent();

            Assembly assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;

            this.VersionTextBlock.Text = "Version " + (version == null ? "unknown" : version.ToString(3));

            SetEventSubscriptions(eventSubscriptionCounts);

            SetCloseChoice();

            List<DependencyEntry> dependencies = BuildDependencyList();

            this.DependenciesListView.ItemsSource = dependencies;
            this.DependencyCountTextBlock.Text = dependencies.Count + " components loaded";
        }

        /// <summary>
        /// Affiche les quatre compteurs, en rouge dès que l'un d'eux s'écarte de 1 :
        /// c'est la seule valeur normale, et c'est tout ce que l'indicateur sert à
        /// dire.
        /// </summary>
        private void SetEventSubscriptions(int[] counts)
        {
            if (counts == null || counts.Length < 4)
            {
                this.EventSubscriptionsTextBlock.Text = "Evt:- New:- Del:- Mod:-";
                this.EventSubscriptionsTextBlock.Foreground = Brushes.Black;

                return;
            }

            this.EventSubscriptionsTextBlock.Text =
                "Evt:" + counts[0] + "  New:" + counts[1] + "  Del:" + counts[2] + "  Mod:" + counts[3];

            bool isAnomalous = counts.Take(4).Any(count => count != 1);

            this.EventSubscriptionsTextBlock.Foreground =
                isAnomalous ? new SolidColorBrush(Color.FromRgb(0xA5, 0x32, 0x2A)) : Brushes.Black;

            this.EventSubscriptionsTextBlock.FontWeight =
                isAnomalous ? FontWeights.Bold : FontWeights.Normal;
        }

        /// <summary>
        /// Le framework, le système, puis les assemblys effectivement chargés dans
        /// le domaine — c'est-à-dire ce qui s'exécute réellement, et non ce que le
        /// projet déclare référencer.
        /// </summary>
        private static List<DependencyEntry> BuildDependencyList()
        {
            List<DependencyEntry> entries = new List<DependencyEntry>();

            entries.Add(new DependencyEntry
            {
                Name = ".NET runtime",
                Version = RuntimeInformation.FrameworkDescription,
            });

            entries.Add(new DependencyEntry
            {
                Name = "Operating system",
                Version = RuntimeInformation.OSDescription,
            });

            entries.Add(new DependencyEntry
            {
                Name = "Process architecture",
                Version = RuntimeInformation.ProcessArchitecture.ToString(),
            });

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()
                                                   .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase))
            {
                AssemblyName name = assembly.GetName();

                entries.Add(new DependencyEntry
                {
                    Name = name.Name,
                    Version = name.Version == null ? "unknown" : name.Version.ToString(),
                });
            }

            return entries;
        }

        /// <summary>
        /// État du choix mémorisé pour le bouton « Close ». Le bouton de remise à
        /// zéro n'a de sens que s'il y a quelque chose à oublier.
        /// </summary>
        private void SetCloseChoice()
        {
            CloseChoice? remembered = SettingsStore.GetRememberedCloseChoice();

            if (remembered == null)
            {
                this.CloseChoiceTextBlock.Text =
                    "The Close button asks what to do: exit, or keep running in the notification area.";

                this.ResetCloseChoiceButton.IsEnabled = false;

                return;
            }

            this.CloseChoiceTextBlock.Text = remembered == CloseChoice.Exit
                ? "The Close button exits without asking, which stops your rules being applied."
                : "The Close button minimizes to the notification area without asking, and your rules keep being applied.";

            this.ResetCloseChoiceButton.IsEnabled = true;
        }

        private void ResetCloseChoiceButton_Click(object sender, RoutedEventArgs e)
        {
            string error;

            if (!SettingsStore.TryForgetCloseChoice(out error))
            {
                MessageBox.Show(error, "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            SetCloseChoice();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
