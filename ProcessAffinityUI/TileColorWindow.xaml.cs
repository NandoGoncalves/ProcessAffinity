using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using ProcessAffinityUI.Configuration;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Choix d'une couleur de fond pour un processus.
    ///
    /// Elle l'emporte sur le rôle « Tile background » des réglages généraux :
    /// c'est ce qui permet de retrouver un programme dans une grille de quatre
    /// cents tuiles sans le chercher. Le bouton « Use the general colour » est
    /// le retour en arrière, et il est aussi important que le choix lui-même.
    ///
    /// Le contrôle de lisibilité est celui de la palette : la valeur chiffrée
    /// posée sur le fond est noire, et un fond sombre la rendrait illisible.
    /// On avertit, on ne corrige pas.
    /// </summary>
    public partial class TileColorWindow : Window
    {
        private bool _suspendEvents;

        /// <summary>
        /// Couleur retenue, ou null pour « suivre la couleur générale ».
        /// Valable seulement si la fenêtre rend vrai.
        /// </summary>
        public Color? Result { get; private set; }

        public TileColorWindow(string subject, Color? current)
        {
            InitializeComponent();

            this.SubjectTextBlock.Text = "Background colour for " + subject
                                         + ". A colour set here wins over the general palette.";

            Color start = current ?? ColorPalette.Get(ColorPalette.TileBackground);

            this._suspendEvents = true;

            this.RedSlider.Value = start.R;
            this.GreenSlider.Value = start.G;
            this.BlueSlider.Value = start.B;
            this.HexTextBox.Text = ColorPalette.Format(start);

            this._suspendEvents = false;

            this.GeneralButton.IsEnabled = current != null;

            UpdatePreview(start);
        }

        private void ChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this._suspendEvents)
            {
                return;
            }

            Color color = Color.FromRgb(
                (byte)this.RedSlider.Value, (byte)this.GreenSlider.Value, (byte)this.BlueSlider.Value);

            this._suspendEvents = true;
            this.HexTextBox.Text = ColorPalette.Format(color);
            this._suspendEvents = false;

            UpdatePreview(color);
        }

        private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this._suspendEvents)
            {
                return;
            }

            Color color;

            if (!ColorPalette.TryParse(this.HexTextBox.Text, out color))
            {
                this.ReadabilityTextBlock.Text = "Enter a colour as #RRGGBB.";
                return;
            }

            this._suspendEvents = true;

            this.RedSlider.Value = color.R;
            this.GreenSlider.Value = color.G;
            this.BlueSlider.Value = color.B;

            this._suspendEvents = false;

            UpdatePreview(color);
        }

        private void UpdatePreview(Color color)
        {
            this.PreviewBorder.Background = ColorPalette.Freeze(color);
            this.PreviewTextBlock.Foreground = ColorPalette.Freeze(ColorPalette.GetTextColorOver(color));

            // La valeur chiffrée de la tuile est noire : c'est contre le noir
            // que ce fond doit tenir.
            double contrast = ColorPalette.ContrastRatio(color, Colors.Black);

            string report = "Contrast with the value on the tile, which is black: "
                            + contrast.ToString("F1") + ":1.";

            if (contrast < ColorPalette.MinimumTextContrast)
            {
                report += " The CPU and memory values drawn on this colour will be hard to read; "
                          + "the accessibility threshold is "
                          + ColorPalette.MinimumTextContrast.ToString("F1") + ":1.";
            }

            if (!ColorPalette.AreDistinguishable(color, ColorPalette.Get(ColorPalette.MemoryBars)))
            {
                report += " Almost indistinguishable from the memory bars, which are drawn on top of it.";
            }

            this.ReadabilityTextBlock.Text = report;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Color color;

            if (!ColorPalette.TryParse(this.HexTextBox.Text, out color))
            {
                MessageBox.Show(
                    "Enter a colour as #RRGGBB.",
                    "ProcessAffinity", MessageBoxButton.OK, MessageBoxImage.Information);

                return;
            }

            this.Result = color;
            this.DialogResult = true;

            this.Close();
        }

        private void GeneralButton_Click(object sender, RoutedEventArgs e)
        {
            this.Result = null;
            this.DialogResult = true;

            this.Close();
        }
    }
}
