using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProjectPlanViewer
{
    public partial class StyleMenuWindow : Window
    {
        public StyleSettings Settings { get; private set; }
        private bool isLoading = false;

        public StyleMenuWindow(StyleSettings currentSettings)
        {
            isLoading = true;
            Settings = currentSettings.Clone();
            InitializeComponent();
            LoadSettings();
            isLoading = false;
            UpdatePreview();
        }

        private void LoadSettings()
        {
            // Load colors - create new brushes from color values
            Phase1ColorPreview.Background = new SolidColorBrush(Settings.Phase1Color.Color);
            Phase2ColorPreview.Background = new SolidColorBrush(Settings.Phase2Color.Color);
            Phase3ColorPreview.Background = new SolidColorBrush(Settings.Phase3Color.Color);
            CriticalColorPreview.Background = new SolidColorBrush(Settings.CriticalPathColor.Color);
            ArrowColorPreview.Background = new SolidColorBrush(Settings.DependencyLineColor.Color);

            // Load font settings
            FontSizeSlider.Value = Settings.TaskFontSize;
            FontSizeLabel.Text = $"{Settings.TaskFontSize} pt";
            
            // Find and select matching font family - BE MORE FLEXIBLE with matching
            string currentFont = Settings.TaskFontFamily.Source;
            bool fontFound = false;
            
            foreach (ComboBoxItem item in FontFamilyCombo.Items)
            {
                string itemFont = item.Content.ToString() ?? "";
                // Try exact match first, then case-insensitive match
                if (itemFont.Equals(currentFont, StringComparison.OrdinalIgnoreCase))
                {
                    FontFamilyCombo.SelectedItem = item;
                    fontFound = true;
                    System.Diagnostics.Debug.WriteLine($"Font matched: '{currentFont}' to '{itemFont}'");
                    break;
                }
            }
            
            // If font not found, select first item as fallback
            if (!fontFound)
            {
                System.Diagnostics.Debug.WriteLine($"Font NOT found: '{currentFont}', using default");
                if (FontFamilyCombo.Items.Count > 0)
                {
                    FontFamilyCombo.SelectedIndex = 0;
                }
            }

            // Load arrow settings
            ArrowThicknessSlider.Value = Settings.DependencyLineThickness;
            ArrowThicknessLabel.Text = $"{Settings.DependencyLineThickness:F1} px";
            
            // Select the correct arrow style - MAKE SURE this matches Settings
            ArrowStyleCombo.SelectedIndex = Settings.DependencyLineStyle switch
            {
                DependencyLineStyle.Dashed => 0,
                DependencyLineStyle.Solid => 1,
                DependencyLineStyle.Dotted => 2,
                DependencyLineStyle.DashDot => 3,
                _ => 0
            };
        }

        private void ChoosePhase1Color_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(Settings.Phase1Color);
            if (color.HasValue)
            {
                Settings.Phase1Color = new SolidColorBrush(color.Value);
                Phase1ColorPreview.Background = Settings.Phase1Color;
                UpdatePreview();
            }
        }

        private void ChoosePhase2Color_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(Settings.Phase2Color);
            if (color.HasValue)
            {
                Settings.Phase2Color = new SolidColorBrush(color.Value);
                Phase2ColorPreview.Background = Settings.Phase2Color;
                UpdatePreview();
            }
        }

        private void ChoosePhase3Color_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(Settings.Phase3Color);
            if (color.HasValue)
            {
                Settings.Phase3Color = new SolidColorBrush(color.Value);
                Phase3ColorPreview.Background = Settings.Phase3Color;
                UpdatePreview();
            }
        }

        private void ChooseCriticalColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(Settings.CriticalPathColor);
            if (color.HasValue)
            {
                Settings.CriticalPathColor = new SolidColorBrush(color.Value);
                CriticalColorPreview.Background = Settings.CriticalPathColor;
                UpdatePreview();
            }
        }

        private void ChooseArrowColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(Settings.DependencyLineColor);
            if (color.HasValue)
            {
                Settings.DependencyLineColor = new SolidColorBrush(color.Value);
                ArrowColorPreview.Background = Settings.DependencyLineColor;
                UpdatePreview();
            }
        }

        private Color? ShowColorPicker(SolidColorBrush currentBrush)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(
                    currentBrush.Color.A,
                    currentBrush.Color.R,
                    currentBrush.Color.G,
                    currentBrush.Color.B),
                FullOpen = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                return Color.FromArgb(255, dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }

            return null;
        }

        private void FontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyCombo.SelectedItem is ComboBoxItem selected)
            {
                Settings.TaskFontFamily = new FontFamily(selected.Content.ToString());
                UpdatePreview();
            }
        }

        private void FontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeLabel != null)
            {
                Settings.TaskFontSize = e.NewValue;
                FontSizeLabel.Text = $"{e.NewValue} pt";
                UpdatePreview();
            }
        }

        private void ArrowStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (ArrowStyleCombo.SelectedIndex)
            {
                case 0:
                    Settings.DependencyLineStyle = DependencyLineStyle.Dashed;
                    break;
                case 1:
                    Settings.DependencyLineStyle = DependencyLineStyle.Solid;
                    break;
                case 2:
                    Settings.DependencyLineStyle = DependencyLineStyle.Dotted;
                    break;
                case 3:
                    Settings.DependencyLineStyle = DependencyLineStyle.DashDot;
                    break;
            }
            UpdatePreview();
        }

        private void ArrowThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ArrowThicknessLabel != null)
            {
                Settings.DependencyLineThickness = e.NewValue;
                ArrowThicknessLabel.Text = $"{e.NewValue} px";
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (PreviewBar1 == null) return;

            // Update preview bars
            PreviewBar1.Background = Settings.Phase1Color;
            PreviewBar2.Background = Settings.Phase2Color;
            
            PreviewText1.FontFamily = Settings.TaskFontFamily;
            PreviewText1.FontSize = Settings.TaskFontSize;
            PreviewText2.FontFamily = Settings.TaskFontFamily;
            PreviewText2.FontSize = Settings.TaskFontSize;

            // Update preview arrow
            PreviewArrow.Stroke = Settings.DependencyLineColor;
            PreviewArrow.StrokeThickness = Settings.DependencyLineThickness;
            PreviewArrowHead.Fill = Settings.DependencyLineColor;

            switch (Settings.DependencyLineStyle)
            {
                case DependencyLineStyle.Dashed:
                    PreviewArrow.StrokeDashArray = new DoubleCollection { 4, 2 };
                    break;
                case DependencyLineStyle.Solid:
                    PreviewArrow.StrokeDashArray = null;
                    break;
                case DependencyLineStyle.Dotted:
                    PreviewArrow.StrokeDashArray = new DoubleCollection { 1, 2 };
                    break;
                case DependencyLineStyle.DashDot:
                    PreviewArrow.StrokeDashArray = new DoubleCollection { 4, 2, 1, 2 };
                    break;
            }
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            Settings = StyleSettings.GetDefaults();
            LoadSettings();
            UpdatePreview();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}