using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;

using ProcessAffinityUI.Configuration;
using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Réglages de l'application : les configurations enregistrées, et les
    /// couleurs.
    ///
    /// Le premier onglet est la fenêtre réclamée de longue date. Jusqu'ici une
    /// règle n'était visible qu'à travers la tuile d'un processus en cours —
    /// encore fallait-il désigner la bonne parmi plusieurs centaines — et une
    /// règle orpheline, dont le chemin a disparu, ne l'était nulle part.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private const string ApplicationName = "ProcessAffinity";

        /// <summary>
        /// Source des processus en cours, fournie par la fenêtre principale :
        /// c'est elle qui permet de dire d'une règle qu'elle tient, qu'elle est
        /// contestée ou qu'elle attend son heure.
        /// </summary>
        private readonly Func<Process[]> _runningProcesses;

        /// <summary>
        /// Repeint les tuiles après un changement de couleur. La fenêtre de
        /// réglages ne connaît pas le panneau.
        /// </summary>
        private readonly Action _repaint;

        /// <summary>
        /// Le report d'application : l'aperçu suit la glissière au trait, mais
        /// la palette n'est posée qu'après une courte pause. Sans cela, chaque
        /// pixel de déplacement repeindrait plusieurs centaines de tuiles.
        /// </summary>
        private readonly DispatcherTimer _commitTimer;

        private ColorRoleRow _editedRole;
        private bool _suspendEditorEvents;

        public SettingsWindow(Func<Process[]> runningProcesses, Action repaint)
        {
            InitializeComponent();

            this._runningProcesses = runningProcesses ?? (() => new Process[0]);
            this._repaint = repaint ?? delegate { };

            this._commitTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CommitDelayMilliseconds),
            };

            this._commitTimer.Tick += CommitTimerTick;

            LoadRules();
            LoadColors();
        }

        /// <summary>
        /// Pause après laquelle un réglage de couleur est posé pour de bon.
        /// Assez court pour que le résultat paraisse immédiat, assez long pour
        /// qu'un glissement continu ne déclenche qu'un repeint.
        /// </summary>
        internal const int CommitDelayMilliseconds = 150;

        // =================================================== onglet des règles

        private void LoadRules()
        {
            List<ProcessRule> rules = RuleEngine.GetAllRules();
            Process[] running = this._runningProcesses();

            List<RuleRow> rows = rules
                .Select(rule => new RuleRow(rule, RuleEngine.DescribeFileState(rule, running)))
                .ToList();

            this.RulesListView.ItemsSource = rows;

            this.RuleCountTextBlock.Text = rows.Count == 0
                ? "No rule saved yet."
                : rows.Count + (rows.Count == 1 ? " rule" : " rules")
                  + ", " + rows.Count(row => row.IsFailing) + " needing attention";

            ShowRejectedFileBanner();
        }

        /// <summary>
        /// Le cas qui n'a aucune autre voie d'affichage. Le fichier existe, il
        /// est plus récent que ce binaire, rien n'a été chargé : la liste est
        /// vide sans que cela veuille dire qu'il n'y a pas de règles. Et la
        /// première sauvegarde faite ailleurs dans l'application remplacerait
        /// la totalité du fichier par une seule règle.
        /// </summary>
        private void ShowRejectedFileBanner()
        {
            if (!RuleEngine.IsFileRejectedAsTooRecent)
            {
                this.RejectedFileBorder.Visibility = Visibility.Collapsed;
                return;
            }

            this.RejectedFileBorder.Visibility = Visibility.Visible;

            this.RejectedFileTitleTextBlock.Text =
                "The rules file was written by a newer version and was not loaded.";

            this.RejectedFileTextBlock.Text =
                "It uses format version " + RuleEngine.FileFormatVersion
                + ", and this version of ProcessAffinity writes version " + RuleEngine.CurrentFormatVersion
                + ". No rule is listed below, and none is being applied — but the file is intact."
                + "\r\n\r\nSaving any configuration from a tile would rewrite the whole file in the older "
                + "format and lose everything it holds. Export a copy first.";
        }

        private void RulesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            List<RuleRow> selected = GetSelectedRules();

            this.EditRuleButton.IsEnabled = selected.Count == 1;
            this.DeleteRuleButton.IsEnabled = selected.Count > 0;

            if (selected.Count == 1)
            {
                this.RuleStateExplanationTextBlock.Text =
                    selected[0].State + " — " + RuleEngine.ExplainFileState(selected[0].FileState);

                return;
            }

            this.RuleStateExplanationTextBlock.Text = selected.Count == 0
                ? "Select a rule to see what its state means."
                : selected.Count + " rules selected.";
        }

        private List<RuleRow> GetSelectedRules()
        {
            return this.RulesListView.SelectedItems.OfType<RuleRow>().ToList();
        }

        private void RulesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GetSelectedRules().Count == 1)
            {
                EditSelectedRule();
            }
        }

        private void EditRuleButton_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedRule();
        }

        private void EditSelectedRule()
        {
            List<RuleRow> selected = GetSelectedRules();

            if (selected.Count != 1)
            {
                return;
            }

            RuleEditWindow editor = new RuleEditWindow(selected[0].Rule);
            editor.Owner = this;

            if (editor.ShowDialog() != true)
            {
                return;
            }

            string error;

            if (!RuleEngine.TryUpdate(editor.Result, out error))
            {
                MessageBox.Show(
                    "The rule was not saved.\r\n\r\n" + error,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            LoadRules();

            // Les bandeaux de nom portent l'état des règles : une règle modifiée
            // peut changer la couleur d'une tuile.
            this._repaint();
        }

        private void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
        {
            List<RuleRow> selected = GetSelectedRules();

            if (selected.Count == 0)
            {
                return;
            }

            string message = selected.Count == 1
                ? "Remove the saved configuration for\r\n\r\n" + selected[0].ExecutablePath + " ?"
                : "Remove " + selected.Count + " saved configurations?\r\n\r\n"
                  + string.Join("\r\n", selected.Take(12).Select(row => row.ExecutablePath))
                  + (selected.Count > 12 ? "\r\n… and " + (selected.Count - 12) + " more" : string.Empty);

            message += "\r\n\r\nRunning processes keep their current affinity and priority; "
                       + "only the rule is removed.";

            if (MessageBox.Show(message, ApplicationName, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            int removed;
            string error;

            bool saved = RuleEngine.TryRemoveMany(
                selected.Select(row => row.ExecutablePath), out removed, out error);

            if (!saved)
            {
                MessageBox.Show(
                    "The rules file could not be written. The rules were removed for this session only.\r\n\r\n"
                    + error,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // L'état de règle reste posé sur les processus qui la portaient :
            // sans ce nettoyage, leur bandeau resterait bleu pour une règle qui
            // n'existe plus.
            foreach (Process process in this._runningProcesses())
            {
                RuleEngine.UpdateIfRuled(process);
            }

            LoadRules();

            this._repaint();
        }

        private void ExportRulesButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Export the rules file",
                Filter = "Rules file (*.json)|*.json|All files (*.*)|*.*",
                FileName = "rules-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json",
                AddExtension = true,
                DefaultExt = ".json",
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string error;

            if (RuleEngine.TryExportTo(dialog.FileName, out error))
            {
                MessageBox.Show(
                    "The rules file was copied to\r\n\r\n" + dialog.FileName,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Information);

                return;
            }

            MessageBox.Show(error, ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ================================================= onglet des couleurs

        private void LoadColors()
        {
            List<ColorRoleRow> rows = ColorPalette.Roles
                .Select(role => new ColorRoleRow(role))
                .ToList();

            this.ColorRolesListBox.ItemsSource = rows;

            UpdatePaletteSummary();
        }

        private void UpdatePaletteSummary()
        {
            int changed = ColorPalette.Roles.Count(role => !ColorPalette.IsDefault(role.Key));

            this.PaletteSummaryTextBlock.Text = changed == 0
                ? "Every colour is at its original value."
                : changed + (changed == 1 ? " colour differs" : " colours differ") + " from the original palette.";

            this.ResetAllColorsButton.IsEnabled = changed > 0;
        }

        private void ColorRolesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitPendingColor();

            this._editedRole = this.ColorRolesListBox.SelectedItem as ColorRoleRow;

            if (this._editedRole == null)
            {
                this.ColorEditorPanel.IsEnabled = false;
                this.EditedRoleNameTextBlock.Text = "No colour selected";

                return;
            }

            this.ColorEditorPanel.IsEnabled = true;
            this.EditedRoleNameTextBlock.Text = this._editedRole.DisplayName;

            Color color = ColorPalette.Get(this._editedRole.Key);

            this._suspendEditorEvents = true;

            this.RedSlider.Value = color.R;
            this.GreenSlider.Value = color.G;
            this.BlueSlider.Value = color.B;
            this.HexTextBox.Text = ColorPalette.Format(color);

            this._suspendEditorEvents = false;

            UpdatePreview(color);
        }

        private void ChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this._suspendEditorEvents || this._editedRole == null)
            {
                return;
            }

            Color color = Color.FromRgb(
                (byte)this.RedSlider.Value, (byte)this.GreenSlider.Value, (byte)this.BlueSlider.Value);

            this._suspendEditorEvents = true;
            this.HexTextBox.Text = ColorPalette.Format(color);
            this._suspendEditorEvents = false;

            UpdatePreview(color);
            ScheduleCommit();
        }

        private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this._suspendEditorEvents || this._editedRole == null)
            {
                return;
            }

            Color color;

            if (!ColorPalette.TryParse(this.HexTextBox.Text, out color))
            {
                // Saisie en cours : on ne corrige rien, on n'applique rien.
                this.ContrastTextBlock.Text = "Enter a colour as #RRGGBB.";

                return;
            }

            this._suspendEditorEvents = true;

            this.RedSlider.Value = color.R;
            this.GreenSlider.Value = color.G;
            this.BlueSlider.Value = color.B;

            this._suspendEditorEvents = false;

            UpdatePreview(color);
            ScheduleCommit();
        }

        /// <summary>
        /// Aperçu et avertissement de contraste, au moment de la saisie. Dire
        /// après coup qu'une teinte est illisible ne sert à rien : l'utilisateur
        /// l'aurait découvert sur cinq cents tuiles.
        /// </summary>
        private void UpdatePreview(Color color)
        {
            this.PreviewBorder.Background = ColorPalette.Freeze(color);

            ColorRole role = this._editedRole == null ? null : this._editedRole.Role;

            Color textColor = role != null && role.TextOverColor == TextOverColorEnum.FixedBlack
                ? Colors.Black
                : ColorPalette.GetTextColorOver(color);

            this.PreviewTextBlock.Foreground = ColorPalette.Freeze(textColor);

            this.PreviewTextBlock.Visibility = role != null && role.TextOverColor == TextOverColorEnum.None
                ? Visibility.Hidden
                : Visibility.Visible;

            this.ContrastTextBlock.Text = DescribeReadability(role, color);
        }

        private string DescribeReadability(ColorRole role, Color color)
        {
            List<string> warnings = new List<string>();

            double? textContrast = ColorPalette.GetTextContrast(role, color);

            string report;

            if (textContrast == null)
            {
                report = "No text is drawn on this colour.";
            }
            else
            {
                report = "Text contrast " + textContrast.Value.ToString("F1") + ":1"
                         + (role.TextOverColor == TextOverColorEnum.FixedBlack
                             ? " against black."
                             : " against " + (ColorPalette.GetTextColorOver(color) == Colors.White ? "white." : "black."));

                if (textContrast.Value < ColorPalette.MinimumTextContrast)
                {
                    warnings.Add("The name written on this colour will be hard to read. "
                                 + "The accessibility threshold is "
                                 + ColorPalette.MinimumTextContrast.ToString("F1") + ":1.");
                }
            }

            if (role != null)
            {
                // Les couples voisins comptent autant que le texte : deux rôles
                // rendus identiques ne gênent pas la lecture, ils suppriment
                // l'information — et rien à l'écran ne le signalerait.
                foreach (string[] pair in ColorPalette.AdjacentPairs)
                {
                    string otherKey = null;

                    if (pair[0] == role.Key)
                    {
                        otherKey = pair[1];
                    }
                    else if (pair[1] == role.Key)
                    {
                        otherKey = pair[0];
                    }

                    if (otherKey == null)
                    {
                        continue;
                    }

                    Color other = ColorPalette.Get(otherKey);

                    if (!ColorPalette.AreDistinguishable(color, other))
                    {
                        warnings.Add("Almost indistinguishable from “" + DescribeKey(otherKey)
                                     + "”, including to a viewer with red-green colour blindness. "
                                     + "Those two sit side by side, and the difference is what carries the meaning.");
                    }
                }

                warnings.AddRange(DescribeOverlayProblems(role, color));
            }

            if (warnings.Count == 0)
            {
                this.ContrastBorder.BorderBrush = ColorPalette.Freeze(Color.FromRgb(0xDD, 0xDD, 0xDD));
                this.ContrastBorder.Background = ColorPalette.Freeze(Color.FromRgb(0xF5, 0xF5, 0xF5));

                return report;
            }

            this.ContrastBorder.BorderBrush = ColorPalette.Freeze(Color.FromRgb(0xA5, 0x32, 0x2A));
            this.ContrastBorder.Background = ColorPalette.Freeze(Color.FromRgb(0xFD, 0xF2, 0xF1));

            return report + "\r\n\r\n" + string.Join("\r\n\r\n", warnings);
        }

        /// <summary>
        /// Le jaune d'activité ne se tient pas à côté des bandeaux : il prend
        /// leur place le temps d'un relevé. C'est donc contre chacun d'eux
        /// qu'il doit se détacher, et non contre un voisin désigné. S'il
        /// ressemble à celui qu'il remplace, le clignotement n'a pas lieu.
        ///
        /// La vérification va dans les deux sens : on édite le jaune, ou on
        /// édite un bandeau que le jaune viendra recouvrir.
        /// </summary>
        private List<string> DescribeOverlayProblems(ColorRole role, Color color)
        {
            List<string> warnings = new List<string>();

            string[] targets;

            if (ColorPalette.OverlayTargets.TryGetValue(role.Key, out targets))
            {
                List<string> hidden = targets
                    .Where(key => !ColorPalette.AreDistinguishable(color, ColorPalette.Get(key)))
                    .Select(DescribeKey)
                    .ToList();

                if (hidden.Count > 0)
                {
                    warnings.Add("The activity flash would go unnoticed on "
                                 + Join(hidden) + ". This colour replaces a name band for one sample; "
                                 + "if it looks like the band it replaces, nothing appears to happen.");
                }

                return warnings;
            }

            foreach (KeyValuePair<string, string[]> overlay in ColorPalette.OverlayTargets)
            {
                if (!overlay.Value.Contains(role.Key))
                {
                    continue;
                }

                Color flash = ColorPalette.Get(overlay.Key);

                if (!ColorPalette.AreDistinguishable(color, flash))
                {
                    warnings.Add("“" + DescribeKey(overlay.Key) + "” replaces this band for one sample when "
                                 + "something happens, and the two would look the same — the flash would go "
                                 + "unnoticed on this band.");
                }
            }

            return warnings;
        }

        private static string DescribeKey(string key)
        {
            ColorRole role = ColorPalette.GetRole(key);

            return role == null ? key : role.DisplayName;
        }

        /// <summary>« a », « a and b », « a, b and c ».</summary>
        private static string Join(List<string> names)
        {
            if (names.Count == 1)
            {
                return "“" + names[0] + "”";
            }

            return "“" + string.Join("”, “", names.Take(names.Count - 1)) + "” and “"
                   + names[names.Count - 1] + "”";
        }

        private void ScheduleCommit()
        {
            this._commitTimer.Stop();
            this._commitTimer.Start();
        }

        private void CommitTimerTick(object sender, EventArgs e)
        {
            CommitPendingColor();
        }

        private void CommitPendingColor()
        {
            this._commitTimer.Stop();

            if (this._editedRole == null)
            {
                return;
            }

            Color color;

            if (!ColorPalette.TryParse(this.HexTextBox.Text, out color))
            {
                return;
            }

            ApplyColor(this._editedRole.Key, color);
        }

        private void ApplyColor(string key, Color color)
        {
            string error;

            if (!ColorPalette.TrySet(key, color, out error))
            {
                // La couleur est posée pour la session ; seule l'écriture a
                // échoué. On le dit une fois, sans bloquer le réglage.
                MessageBox.Show(
                    "The colour was applied, but could not be saved for next time.\r\n\r\n" + error,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            RefreshColorRows();

            this._repaint();
        }

        private void RefreshColorRows()
        {
            foreach (ColorRoleRow row in this.ColorRolesListBox.Items.OfType<ColorRoleRow>())
            {
                row.Refresh();
            }

            UpdatePaletteSummary();
        }

        private void ResetColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (this._editedRole == null)
            {
                return;
            }

            this._commitTimer.Stop();

            ApplyColor(this._editedRole.Key, this._editedRole.Role.Default);

            Color color = ColorPalette.Get(this._editedRole.Key);

            this._suspendEditorEvents = true;

            this.RedSlider.Value = color.R;
            this.GreenSlider.Value = color.G;
            this.BlueSlider.Value = color.B;
            this.HexTextBox.Text = ColorPalette.Format(color);

            this._suspendEditorEvents = false;

            UpdatePreview(color);
        }

        private void ResetAllColorsButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                    "Put every colour back to the value it shipped with?",
                    ApplicationName, MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            this._commitTimer.Stop();

            string error;

            if (!ColorPalette.TryResetAll(out error))
            {
                MessageBox.Show(
                    "The colours were restored, but could not be saved for next time.\r\n\r\n" + error,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            RefreshColorRows();

            this._repaint();

            // Réaffiche l'éditeur sur la valeur rendue.
            ColorRolesListBox_SelectionChanged(null, null);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CommitPendingColor();

            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            this._commitTimer.Stop();
            this._commitTimer.Tick -= CommitTimerTick;

            base.OnClosed(e);
        }

        // ============================================== lignes des deux listes

        /// <summary>Une règle, telle que la liste la montre.</summary>
        public sealed class RuleRow
        {
            public RuleRow(ProcessRule rule, RuleEngine.RuleFileStateEnum state)
            {
                this.Rule = rule;
                this.FileState = state;
            }

            public ProcessRule Rule { get; private set; }

            public RuleEngine.RuleFileStateEnum FileState { get; private set; }

            public string ExecutablePath { get { return this.Rule.ExecutablePath; } }

            public string Affinity { get { return RuleEngine.DescribeMask(this.Rule.GetAffinityMask()); } }

            public string Priority { get { return RuleEngine.DescribePriority(this.Rule.PriorityClass); } }

            public string EfficiencyMode
            {
                get
                {
                    return this.Rule.EfficiencyMode == null
                        ? "not managed"
                        : RuleEngine.DescribeEfficiencyMode(this.Rule.EfficiencyMode.Value);
                }
            }

            public string CpuSets
            {
                get
                {
                    return this.Rule.CpuSetProcessors == null
                        ? "not managed"
                        : RuleEngine.DescribeCpuSetCount(this.Rule.CpuSetProcessors.Length);
                }
            }

            public string State { get { return RuleEngine.DescribeFileState(this.FileState); } }

            public bool IsFailing
            {
                get
                {
                    return this.FileState != RuleEngine.RuleFileStateEnum.Applied
                           && this.FileState != RuleEngine.RuleFileStateEnum.NotRunning;
                }
            }
        }

        /// <summary>
        /// Un rôle de couleur dans la liste. Implémente la notification pour que
        /// la pastille et le code hexadécimal suivent un changement sans qu'on
        /// reconstruise la liste — reconstruire ferait perdre la sélection à
        /// chaque mouvement de glissière.
        /// </summary>
        public sealed class ColorRoleRow : System.ComponentModel.INotifyPropertyChanged
        {
            public ColorRoleRow(ColorRole role)
            {
                this.Role = role;
            }

            public ColorRole Role { get; private set; }

            public string Key { get { return this.Role.Key; } }

            public string DisplayName { get { return this.Role.DisplayName; } }

            public string Description { get { return this.Role.Description; } }

            public Brush Swatch { get { return ColorPalette.GetBrush(this.Role.Key); } }

            public string Hex { get { return ColorPalette.Format(ColorPalette.Get(this.Role.Key)); } }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            public void Refresh()
            {
                System.ComponentModel.PropertyChangedEventHandler handler = this.PropertyChanged;

                if (handler == null)
                {
                    return;
                }

                handler(this, new System.ComponentModel.PropertyChangedEventArgs("Swatch"));
                handler(this, new System.ComponentModel.PropertyChangedEventArgs("Hex"));
            }
        }
    }
}
