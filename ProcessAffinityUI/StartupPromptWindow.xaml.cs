using System.Windows;
using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Proposition d'ajouter l'application au démarrage de la session, posée une
    /// fois au premier lancement.
    ///
    /// Le chemin de la clé est affiché : l'utilisateur doit pouvoir savoir ce qui
    /// sera écrit, et où aller le défaire sans l'application.
    /// </summary>
    public partial class StartupPromptWindow : Window
    {
        public StartupPromptWindow()
        {
            InitializeComponent();

            this.RegistryPathTextBlock.Text =
                "This adds one entry under " + StartupRegistration.RegistryPath
                + ". It needs no administrator rights and affects only your account.";
        }

        /// <summary>Vrai si l'utilisateur accepte le démarrage automatique.</summary>
        public bool Accepted { get; private set; }

        /// <summary>Vrai si la question ne doit plus être posée.</summary>
        public bool DoNotAskAgain
        {
            get { return this.DoNotAskCheckBox.IsChecked == true; }
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            this.Accepted = true;
            this.DialogResult = true;
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            this.Accepted = false;
            this.DialogResult = false;
        }
    }
}
