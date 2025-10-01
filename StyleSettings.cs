using System.Windows.Media;

namespace ProjectPlanViewer
{
    public enum DependencyLineStyle
    {
        Dashed,
        Solid,
        Dotted,
        DashDot
    }

    public class StyleSettings
    {
        public SolidColorBrush Phase1Color { get; set; } = new SolidColorBrush(Colors.Blue);
        public SolidColorBrush Phase2Color { get; set; } = new SolidColorBrush(Colors.Purple);
        public SolidColorBrush Phase3Color { get; set; } = new SolidColorBrush(Colors.Green);
        public SolidColorBrush CriticalPathColor { get; set; } = new SolidColorBrush(Colors.Red);
        public SolidColorBrush DependencyLineColor { get; set; } = new SolidColorBrush(Colors.Gray);
        public SolidColorBrush CriticalPathDependencyColor { get; set; } = new SolidColorBrush(Colors.DarkRed);
        
        public FontFamily TaskFontFamily { get; set; } = new FontFamily("Segoe UI");
        public double TaskFontSize { get; set; }
        
        public DependencyLineStyle DependencyLineStyle { get; set; }
        public double DependencyLineThickness { get; set; }

        public static StyleSettings GetDefaults()
        {
            return new StyleSettings
            {
                Phase1Color = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),
                Phase2Color = new SolidColorBrush(Color.FromRgb(0x7B, 0x68, 0xEE)),
                Phase3Color = new SolidColorBrush(Color.FromRgb(0x50, 0xC8, 0x78)),
                CriticalPathColor = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                DependencyLineColor = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6)),
                CriticalPathDependencyColor = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)),
                TaskFontFamily = new FontFamily("Segoe UI"),
                TaskFontSize = 10,
                DependencyLineStyle = DependencyLineStyle.Solid,  // Changed from Dashed to match what's actually rendering
                DependencyLineThickness = 1.5
            };
        }

        public StyleSettings Clone()
        {
            return new StyleSettings
            {
                Phase1Color = new SolidColorBrush(Phase1Color.Color),
                Phase2Color = new SolidColorBrush(Phase2Color.Color),
                Phase3Color = new SolidColorBrush(Phase3Color.Color),
                CriticalPathColor = new SolidColorBrush(CriticalPathColor.Color),
                DependencyLineColor = new SolidColorBrush(DependencyLineColor.Color),
                CriticalPathDependencyColor = new SolidColorBrush(CriticalPathDependencyColor.Color),
                TaskFontFamily = new FontFamily(TaskFontFamily.Source),
                TaskFontSize = TaskFontSize,
                DependencyLineStyle = DependencyLineStyle,
                DependencyLineThickness = DependencyLineThickness
            };
        }
    }
}