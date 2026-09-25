using System.Windows;
using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Demande ce que doit faire le bouton « Close ». Quitter et ranger dans la
    /// zone de notification n'ont pas les mêmes conséquences — l'un interrompt
    /// l'application des règles, l'autre la poursuit — et le bouton seul ne
    /// permettait pas de choisir.
    ///
    /// La croix de la barre de titre ne passe pas par ici : elle quitte, comme
    /// elle l'a toujours fait.
    /// </summary>
    public partial class CloseConfirmationWindow : Window
    {
        public CloseConfirmationWindow()
        {
            InitializeComponent();
        }

        /// <summary>Ce que l'utilisateur a choisi. « Cancel » si la fenêtre est fermée sans répondre.</summary>
        public CloseChoice Choice { get; private set; }

        /// <summary>Vrai si le choix doit être rejoué sans question la prochaine fois.</summary>
        public bool ShouldRemember
        {
            get
            {
                // Rien à mémoriser d'une annulation : elle ne dit pas ce que le
                // bouton devrait faire, seulement qu'on ne veut rien faire cette
                // fois-ci.
                return this.RememberCheckBox.IsChecked == true && this.Choice != CloseChoice.Cancel;
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Choice = CloseChoice.Exit;
            this.DialogResult = true;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.Choice = CloseChoice.Minimize;
            this.DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Choice = CloseChoice.Cancel;
            this.DialogResult = false;
        }
    }
}
