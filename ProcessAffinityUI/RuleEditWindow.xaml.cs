using System;
using System.Windows.Media;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;

using ProcessAffinityUI.Configuration;
using ProcessAffinityUI.Threading;

namespace ProcessAffinityUI
{
    /// <summary>
    /// Modifie une règle enregistrée, sans qu'aucun processus ne la porte.
    ///
    /// Les fenêtres d'affinité et de priorité existantes travaillent sur des
    /// processus vivants : elles lisent l'état courant et l'écrivent. Ici il n'y
    /// a rien à lire ni à écrire dans Windows — seulement un enregistrement à
    /// corriger, qui s'appliquera au prochain lancement.
    ///
    /// Le chemin de l'exécutable ne se modifie pas : c'est l'identité de la
    /// règle, et la changer reviendrait à en créer une autre. La création n'a
    /// pas sa place ici, une règle naissant d'un processus réel.
    /// </summary>
    public partial class RuleEditWindow : Window
    {
        private const string ApplicationName = "ProcessAffinity";

        private readonly ProcessRule _original;
        private readonly int _processorCount;

        private readonly List<CheckBox> _affinityBoxes = new List<CheckBox>();
        private readonly List<CheckBox> _cpuSetBoxes = new List<CheckBox>();

        /// <summary>La règle corrigée, valable seulement si la fenêtre rend vrai.</summary>
        public ProcessRule Result { get; private set; }

        public RuleEditWindow(ProcessRule rule)
        {
            InitializeComponent();

            this._original = rule;
            this._processorCount = BitOperations.PopCount((ulong)RuleEngine.GetAllProcessorsMask());

            if (this._processorCount <= 0)
            {
                this._processorCount = Environment.ProcessorCount;
            }

            this.ExecutablePathTextBlock.Text = rule.ExecutablePath;

            DescribeExecutable(rule);
            BuildProcessorBoxes();
            BuildCombos();
            LoadRule(rule);
            UpdateInconsistencyWarning();
        }

        /// <summary>
        /// Dit si le chemin existe encore. Une règle orpheline se corrige
        /// toujours, mais il n'y a pas de raison de laisser croire qu'elle
        /// s'appliquera.
        /// </summary>
        private void DescribeExecutable(ProcessRule rule)
        {
            bool exists;

            try
            {
                exists = File.Exists(rule.ExecutablePath);
            }
            catch
            {
                exists = true;
            }

            this.ExecutableStateTextBlock.Text = exists
                ? "The rule applies to every launch of this executable."
                : "This file no longer exists on disk. The rule is kept, but it cannot apply "
                  + "until the program is installed there again.";
        }

        private void BuildProcessorBoxes()
        {
            for (int processor = 0; processor < this._processorCount; processor++)
            {
                this._affinityBoxes.Add(AddProcessorBox(this.AffinityPanel, processor, AffinityBoxChanged));
                this._cpuSetBoxes.Add(AddProcessorBox(this.CpuSetsPanel, processor, CpuSetBoxChanged));
            }
        }

        private CheckBox AddProcessorBox(WrapPanel panel, int processor, RoutedEventHandler changed)
        {
            CheckBox box = new CheckBox
            {
                Content = "CPU " + processor,
                Width = 72,
                Margin = new Thickness(0, 2, 6, 2),
                Tag = processor,
            };

            box.Checked += changed;
            box.Unchecked += changed;

            panel.Children.Add(box);

            return box;
        }

        private void BuildCombos()
        {
            foreach (ProcessPriorityEnum priority in new[]
                     {
                         ProcessPriorityEnum.Idle,
                         ProcessPriorityEnum.BelowNormal,
                         ProcessPriorityEnum.Normal,
                         ProcessPriorityEnum.AboveNormal,
                         ProcessPriorityEnum.HighPriority,
                         ProcessPriorityEnum.RealTime,
                     })
            {
                this.PriorityComboBox.Items.Add(new ComboItem
                {
                    Label = RuleEngine.DescribePriority((int)priority),
                    Value = (int)priority,
                });
            }

            this.PriorityComboBox.SelectionChanged += PriorityComboBox_SelectionChanged;

            this.EfficiencyComboBox.Items.Add(new ComboItem { Label = "Not managed by this rule", Value = -1 });
            this.EfficiencyComboBox.Items.Add(new ComboItem { Label = "Left to Windows", Value = (int)EfficiencyModeEnum.SystemManaged });
            this.EfficiencyComboBox.Items.Add(new ComboItem { Label = "Throttled", Value = (int)EfficiencyModeEnum.Enabled });
            this.EfficiencyComboBox.Items.Add(new ComboItem { Label = "Never throttled", Value = (int)EfficiencyModeEnum.Disabled });

            for (int scale = 1; scale <= DisplayPreferences.MaximumScale; scale++)
            {
                this.TileSizeComboBox.Items.Add(new ComboItem
                {
                    Label = scale == 1 ? "Normal" : scale == 2 ? "Double" : "Triple",
                    Value = scale,
                });
            }
        }

        /// <summary>
        /// Clé des préférences d'affichage de cet exécutable. La taille et la
        /// couleur vivent dans <c>settings.json</c>, sous ce chemin — jamais
        /// dans la règle elle-même.
        /// </summary>
        private string DisplayKey
        {
            get { return DisplayPreferences.GetKeyFromExecutablePath(this._original.ExecutablePath); }
        }

        private void LoadDisplayPreferences()
        {
            Select(this.TileSizeComboBox, DisplayPreferences.GetScaleByKey(this.DisplayKey));

            RefreshBackgroundSwatch();
        }

        private void RefreshBackgroundSwatch()
        {
            Color? own = DisplayPreferences.GetBackgroundColorByKey(this.DisplayKey);

            this.BackgroundSwatchBorder.Background = ColorPalette.Freeze(
                own ?? ColorPalette.Get(ColorPalette.TileBackground));

            this.BackgroundSwatchBorder.ToolTip = own == null
                ? "Follows the general palette."
                : "Own colour: " + ColorPalette.Format(own.Value);

            this.GeneralBackgroundButton.IsEnabled = own != null;
        }

        private void ChooseBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            TileColorWindow window = new TileColorWindow(
                "\"" + System.IO.Path.GetFileName(this._original.ExecutablePath) + "\"",
                DisplayPreferences.GetBackgroundColorByKey(this.DisplayKey));

            window.Owner = this;

            if (window.ShowDialog() != true)
            {
                return;
            }

            ApplyBackground(window.Result);
        }

        private void GeneralBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyBackground(null);
        }

        private void ApplyBackground(Color? color)
        {
            string error;

            if (!DisplayPreferences.TrySetBackgroundColorByKey(this.DisplayKey, color, out error))
            {
                MessageBox.Show(
                    "The colour was applied, but could not be saved for next time.\r\n\r\n" + error,
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            RefreshBackgroundSwatch();
        }

        private void LoadRule(ProcessRule rule)
        {
            ulong mask = (ulong)rule.GetAffinityMask();

            for (int processor = 0; processor < this._affinityBoxes.Count; processor++)
            {
                this._affinityBoxes[processor].IsChecked = (mask & (1UL << processor)) != 0UL;
            }

            Select(this.PriorityComboBox, rule.PriorityClass);
            Select(this.EfficiencyComboBox, rule.EfficiencyMode ?? -1);

            this.ManageCpuSetsCheckBox.IsChecked = rule.CpuSetProcessors != null;

            if (rule.CpuSetProcessors != null)
            {
                foreach (int processor in rule.CpuSetProcessors)
                {
                    if (processor >= 0 && processor < this._cpuSetBoxes.Count)
                    {
                        this._cpuSetBoxes[processor].IsChecked = true;
                    }
                }
            }

            UpdateCpuSetsAvailability();
            UpdatePriorityWarning();
        }

        private static void Select(ComboBox combo, int value)
        {
            foreach (ComboItem item in combo.Items.OfType<ComboItem>())
            {
                if (item.Value == value)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }

        private void PriorityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePriorityWarning();
        }

        private void UpdatePriorityWarning()
        {
            ComboItem selected = this.PriorityComboBox.SelectedItem as ComboItem;

            this.PriorityWarningTextBlock.Visibility =
                selected != null && selected.Value == (int)ProcessPriorityEnum.RealTime
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void AffinityBoxChanged(object sender, RoutedEventArgs e)
        {
            UpdateInconsistencyWarning();
        }

        private void CpuSetBoxChanged(object sender, RoutedEventArgs e)
        {
            UpdateInconsistencyWarning();
        }

        private void ManageCpuSetsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCpuSetsAvailability();
            UpdateInconsistencyWarning();
        }

        private void UpdateCpuSetsAvailability()
        {
            bool managed = this.ManageCpuSetsCheckBox.IsChecked == true;

            foreach (CheckBox box in this._cpuSetBoxes)
            {
                box.IsEnabled = managed;
            }
        }

        private void AffinityAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (CheckBox box in this._affinityBoxes)
            {
                box.IsChecked = true;
            }
        }

        private void AffinityNoneButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (CheckBox box in this._affinityBoxes)
            {
                box.IsChecked = false;
            }
        }

        /// <summary>
        /// Avertit sans corriger, comme la fenêtre d'affinité : cocher un
        /// processeur en CPU Sets que l'affinité exclut ne fait rien du tout,
        /// la limite dure l'emportant sur la préférence.
        /// </summary>
        private void UpdateInconsistencyWarning()
        {
            if (this.ManageCpuSetsCheckBox.IsChecked != true)
            {
                this.InconsistencyBorder.Visibility = Visibility.Collapsed;
                return;
            }

            ulong mask = BuildMask();

            List<int> outside = GetCheckedProcessors(this._cpuSetBoxes)
                .Where(processor => (mask & (1UL << processor)) == 0UL)
                .ToList();

            if (outside.Count == 0)
            {
                this.InconsistencyBorder.Visibility = Visibility.Collapsed;
                return;
            }

            this.InconsistencyBorder.Visibility = Visibility.Visible;

            this.InconsistencyTextBlock.Text =
                "CPU Sets name " + string.Join(", ", outside.Select(processor => "CPU " + processor))
                + ", which the affinity above excludes. Affinity is a hard limit and CPU Sets are only a "
                + "preference, so those processors will never be used. The rule is saved as you typed it.";
        }

        private ulong BuildMask()
        {
            ulong mask = 0UL;

            foreach (int processor in GetCheckedProcessors(this._affinityBoxes))
            {
                mask |= 1UL << processor;
            }

            return mask;
        }

        private static List<int> GetCheckedProcessors(List<CheckBox> boxes)
        {
            List<int> processors = new List<int>();

            for (int index = 0; index < boxes.Count; index++)
            {
                if (boxes[index].IsChecked == true)
                {
                    processors.Add(index);
                }
            }

            return processors;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            ulong mask = BuildMask();

            if (mask == 0UL)
            {
                MessageBox.Show(
                    "The affinity must name at least one processor. A rule with an empty mask would "
                    + "never be applied.",
                    ApplicationName, MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            ComboItem priority = this.PriorityComboBox.SelectedItem as ComboItem;
            ComboItem efficiency = this.EfficiencyComboBox.SelectedItem as ComboItem;

            ProcessRule result = new ProcessRule
            {
                ExecutablePath = this._original.ExecutablePath,
                PriorityClass = priority == null ? this._original.PriorityClass : priority.Value,
                EfficiencyMode = efficiency == null || efficiency.Value < 0 ? (int?)null : efficiency.Value,
                CpuSetProcessors = this.ManageCpuSetsCheckBox.IsChecked == true
                    ? GetCheckedProcessors(this._cpuSetBoxes).ToArray()
                    : null,
            };

            result.SetAffinityMask((nuint)mask);

            // La taille retenue dans la configuration, enregistree la ou elle vit.
            ComboItem tileSize = this.TileSizeComboBox.SelectedItem as ComboItem;

            if (tileSize != null)
            {
                string scaleError;
                DisplayPreferences.TrySetScaleByKey(this.DisplayKey, tileSize.Value, out scaleError);
            }

            this.Result = result;
            this.DialogResult = true;

            this.Close();
        }

        private sealed class ComboItem
        {
            public string Label { get; set; }

            public int Value { get; set; }

            public override string ToString()
            {
                return this.Label;
            }
        }
    }
}
