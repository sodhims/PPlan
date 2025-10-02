using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace GanttChartApp
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ProjectTask> tasks = new ObservableCollection<ProjectTask>();
        private DateTime projectStart;
        private DateTime projectEnd;
        private const double DayWidth = 20;
        private double currentZoom = 1.0;
        private double currentRowHeight = 40; // Default row height
        private const double MIN_ROW_HEIGHT = 25;
        private const double MAX_ROW_HEIGHT = 80;
        private const double ROW_HEIGHT_STEP = 5;
        
        private Point dragStartPoint;
        private bool isDragging = false;
        private ProjectTask draggedTask;
        private Rectangle draggedRectangle;

        public MainWindow()
        {
            InitializeComponent();
            TaskListView.ItemsSource = tasks;
            
            // Wire up mouse wheel handler for row height adjustment
            GanttCanvas.MouseWheel += GanttCanvas_MouseWheel;
            
            ZoomSlider.ValueChanged += (s, e) => ApplyZoom(e.NewValue);
        }

        private void LoadCSV_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadTasksFromCSV(openFileDialog.FileName);
            }
        }

        private void LoadTasksFromCSV(string filePath)
        {
            tasks.Clear();
            var lines = File.ReadAllLines(filePath);

            if (lines.Length < 2) return;

            var headers = lines[0].Split(',');
            int idIndex = Array.IndexOf(headers, "ID");
            int wbsIndex = Array.IndexOf(headers, "WBS");
            int taskNameIndex = Array.IndexOf(headers, "Task Name");
            int startIndex = Array.IndexOf(headers, "Start");
            int finishIndex = Array.IndexOf(headers, "Finish");
            int durationIndex = Array.IndexOf(headers, "Duration");
            int predecessorsIndex = Array.IndexOf(headers, "Predecessors");

            for (int i = 1; i < lines.Length; i++)
            {
                var values = ParseCSVLine(lines[i]);
                
                if (values.Length < headers.Length) continue;

                var task = new ProjectTask
                {
                    ID = int.Parse(values[idIndex]),
                    WBS = values[wbsIndex],
                    TaskName = values[taskNameIndex],
                    Start = DateTime.Parse(values[startIndex]),
                    Finish = DateTime.Parse(values[finishIndex]),
                    Duration = int.Parse(values[durationIndex]),
                    Predecessors = ParsePredecessors(values[predecessorsIndex])
                };

                task.IndentLevel = task.WBS.Count(c => c == '.') - 1;
                task.IsMilestone = task.Duration == 0;
                
                DeterminePhaseColor(task);
                task.RowBackground = i % 2 == 0 ? 
                    new SolidColorBrush(Color.FromRgb(248, 248, 248)) : 
                    new SolidColorBrush(Colors.White);

                tasks.Add(task);
            }

            CalculateCriticalPath();
            
            if (tasks.Count > 0)
            {
                projectStart = tasks.Min(t => t.Start);
                projectEnd = tasks.Max(t => t.Finish);
            }

            RenderGanttChart();
        }

        private string[] ParseCSVLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            string current = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.Trim());
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
            result.Add(current.Trim());
            return result.ToArray();
        }

        private List<int> ParsePredecessors(string predecessorString)
        {
            if (string.IsNullOrWhiteSpace(predecessorString))
                return new List<int>();

            return predecessorString
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => int.TryParse(p, out _))
                .Select(int.Parse)
                .ToList();
        }

        private void DeterminePhaseColor(ProjectTask task)
        {
            if (task.WBS.StartsWith("1."))
                task.PhaseColor = StyleSettings.Current.Phase1Color;
            else if (task.WBS.StartsWith("2."))
                task.PhaseColor = StyleSettings.Current.Phase2Color;
            else if (task.WBS.StartsWith("3."))
                task.PhaseColor = StyleSettings.Current.Phase3Color;
            else
                task.PhaseColor = new SolidColorBrush(Colors.Gray);
        }

        private void CalculateCriticalPath()
        {
            foreach (var task in tasks)
            {
                task.EarlyStart = task.Start;
                task.EarlyFinish = task.Finish;
            }

            foreach (var task in tasks.OrderBy(t => t.ID))
            {
                foreach (var predId in task.Predecessors)
                {
                    var pred = tasks.FirstOrDefault(t => t.ID == predId);
                    if (pred != null && pred.EarlyFinish > task.EarlyStart)
                    {
                        task.EarlyStart = pred.EarlyFinish;
                        task.EarlyFinish = task.EarlyStart.AddDays(task.Duration);
                    }
                }
            }

            var lastTask = tasks.OrderByDescending(t => t.EarlyFinish).FirstOrDefault();
            if (lastTask != null)
            {
                foreach (var task in tasks)
                {
                    task.LateFinish = lastTask.EarlyFinish;
                    task.LateStart = task.LateFinish.AddDays(-task.Duration);
                }
            }

            foreach (var task in tasks.OrderByDescending(t => t.ID))
            {
                var successors = tasks.Where(t => t.Predecessors.Contains(task.ID));
                foreach (var successor in successors)
                {
                    if (successor.LateStart < task.LateFinish)
                    {
                        task.LateFinish = successor.LateStart;
                        task.LateStart = task.LateFinish.AddDays(-task.Duration);
                    }
                }
            }

            foreach (var task in tasks)
            {
                task.TotalFloat = (int)(task.LateStart - task.EarlyStart).TotalDays;
                task.IsOnCriticalPath = task.TotalFloat == 0;
            }
        }

        private void RenderGanttChart()
        {
            if (tasks.Count == 0) return;

            GanttCanvas.Children.Clear();
            TimelineCanvas.Children.Clear();

            DrawTimeline();
            DrawTaskBars();
            DrawDependencyArrows();
        }

        private void DrawTimeline()
        {
            double x = 0;
            DateTime currentDate = projectStart;

            while (currentDate <= projectEnd)
            {
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = tasks.Count * currentRowHeight,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1
                };
                GanttCanvas.Children.Add(line);

                var dateLabel = new TextBlock
                {
                    Text = currentDate.ToString("MM/dd"),
                    FontSize = 10,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(dateLabel, x + 2);
                Canvas.SetTop(dateLabel, 2);
                TimelineCanvas.Children.Add(dateLabel);

                x += DayWidth * currentZoom;
                currentDate = currentDate.AddDays(1);
            }

            TimelineCanvas.Width = x;
            GanttCanvas.Width = x;
        }

        private void DrawTaskBars()
        {
            double y = 0;

            foreach (var task in tasks)
            {
                double startX = (task.Start - projectStart).TotalDays * DayWidth * currentZoom;
                double width = task.Duration * DayWidth * currentZoom;

                if (task.IsMilestone)
                {
                    var diamond = new Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(startX, y + currentRowHeight / 2),
                            new Point(startX + 10, y + currentRowHeight / 2 - 10),
                            new Point(startX + 20, y + currentRowHeight / 2),
                            new Point(startX + 10, y + currentRowHeight / 2 + 10)
                        },
                        Fill = task.PhaseColor,
                        Stroke = Brushes.Black,
                        StrokeThickness = 1
                    };
                    GanttCanvas.Children.Add(diamond);
                }
                else
                {
                    var rect = new Rectangle
                    {
                        Width = Math.Max(width, 5),
                        Height = currentRowHeight * 0.6,
                        Fill = task.PhaseColor,
                        Stroke = task.IsOnCriticalPath ? Brushes.Red : Brushes.Black,
                        StrokeThickness = task.IsOnCriticalPath ? 2 : 1,
                        RadiusX = 3,
                        RadiusY = 3,
                        Tag = task,
                        Cursor = Cursors.Hand
                    };

                    Canvas.SetLeft(rect, startX);
                    Canvas.SetTop(rect, y + currentRowHeight * 0.2);

                    rect.MouseLeftButtonDown += TaskBar_MouseLeftButtonDown;
                    rect.MouseMove += TaskBar_MouseMove;
                    rect.MouseLeftButtonUp += TaskBar_MouseLeftButtonUp;

                    GanttCanvas.Children.Add(rect);

                    var label = new TextBlock
                    {
                        Text = task.TaskName,
                        FontSize = StyleSettings.Current.TaskFontSize,
                        FontFamily = StyleSettings.Current.TaskFontFamily,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(label, startX + 5);
                    Canvas.SetTop(label, y + currentRowHeight * 0.3);
                    GanttCanvas.Children.Add(label);
                }

                y += currentRowHeight;
            }

            GanttCanvas.Height = tasks.Count * currentRowHeight;
        }

        private void DrawDependencyArrows()
        {
            foreach (var task in tasks)
            {
                foreach (var predId in task.Predecessors)
                {
                    var predecessor = tasks.FirstOrDefault(t => t.ID == predId);
                    if (predecessor == null) continue;

                    double predEndX = (predecessor.Finish - projectStart).TotalDays * DayWidth * currentZoom;
                    double taskStartX = (task.Start - projectStart).TotalDays * DayWidth * currentZoom;

                    int predIndex = tasks.IndexOf(predecessor);
                    int taskIndex = tasks.IndexOf(task);

                    double predY = predIndex * currentRowHeight + currentRowHeight / 2;
                    double taskY = taskIndex * currentRowHeight + currentRowHeight / 2;

                    var line = new Line
                    {
                        X1 = predEndX,
                        Y1 = predY,
                        X2 = taskStartX,
                        Y2 = taskY,
                        Stroke = StyleSettings.Current.DependencyLineColor,
                        StrokeThickness = 2
                    };

                    GanttCanvas.Children.Add(line);

                    var arrowHead = new Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(taskStartX, taskY),
                            new Point(taskStartX - 8, taskY - 4),
                            new Point(taskStartX - 8, taskY + 4)
                        },
                        Fill = StyleSettings.Current.DependencyLineColor
                    };

                    GanttCanvas.Children.Add(arrowHead);
                }
            }
        }

        private void TaskBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var rect = sender as Rectangle;
            if (rect == null) return;

            draggedTask = rect.Tag as ProjectTask;
            draggedRectangle = rect;
            dragStartPoint = e.GetPosition(GanttCanvas);
            isDragging = true;

            rect.CaptureMouse();
            e.Handled = true;
        }

        private void TaskBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging || draggedTask == null) return;

            Point currentPoint = e.GetPosition(GanttCanvas);
            double deltaX = currentPoint.X - dragStartPoint.X;
            int daysDelta = (int)(deltaX / (DayWidth * currentZoom));

            if (daysDelta != 0)
            {
                DateTime newStart = draggedTask.Start.AddDays(daysDelta);
                DateTime newFinish = draggedTask.Finish.AddDays(daysDelta);

                if (ValidateTaskMove(draggedTask, newStart, newFinish))
                {
                    MoveTaskAndSuccessors(draggedTask, daysDelta);
                    dragStartPoint = currentPoint;
                    RenderGanttChart();
                }
            }
        }

        private void TaskBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                draggedTask = null;
                draggedRectangle?.ReleaseMouseCapture();
                draggedRectangle = null;
            }
        }

        private bool ValidateTaskMove(ProjectTask task, DateTime newStart, DateTime newFinish)
        {
            foreach (var predId in task.Predecessors)
            {
                var pred = tasks.FirstOrDefault(t => t.ID == predId);
                if (pred != null && newStart < pred.Finish)
                {
                    MessageBox.Show($"Cannot move task: it would violate dependency with task {pred.ID} ({pred.TaskName}).\n\n" +
                                  $"Predecessor finishes on {pred.Finish.ToShortDateString()}, " +
                                  $"but you're trying to start on {newStart.ToShortDateString()}.",
                                  "Invalid Move", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        private void MoveTaskAndSuccessors(ProjectTask task, int daysDelta)
        {
            task.Start = task.Start.AddDays(daysDelta);
            task.Finish = task.Finish.AddDays(daysDelta);
            task.OnPropertyChanged(nameof(task.Start));
            task.OnPropertyChanged(nameof(task.Finish));

            var successors = tasks.Where(t => t.Predecessors.Contains(task.ID)).ToList();
            foreach (var successor in successors)
            {
                if (successor.Start < task.Finish)
                {
                    int successorShift = (int)(task.Finish - successor.Start).TotalDays;
                    MoveTaskAndSuccessors(successor, successorShift);
                }
            }
        }

        private void StyleMenu_Click(object sender, RoutedEventArgs e)
        {
            var styleWindow = new StyleMenuWindow(StyleSettings.Current);
            if (styleWindow.ShowDialog() == true)
            {
                StyleSettings.Current = styleWindow.Settings;
                
                foreach (var task in tasks)
                {
                    DeterminePhaseColor(task);
                }
                
                RenderGanttChart();
            }
        }

        private void GanttCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                
                if (e.Delta > 0)
                {
                    currentRowHeight = Math.Min(MAX_ROW_HEIGHT, currentRowHeight + ROW_HEIGHT_STEP);
                }
                else
                {
                    currentRowHeight = Math.Max(MIN_ROW_HEIGHT, currentRowHeight - ROW_HEIGHT_STEP);
                }
                
                UpdateRowHeightDisplay();
                RenderGanttChart();
            }
        }

        private void UpdateRowHeightDisplay()
        {
            this.Title = $"MES Project Plan Viewer - Row Height: {currentRowHeight}px";
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            double newZoom = Math.Min(3.0, ZoomSlider.Value + 0.5);
            ZoomSlider.Value = newZoom;
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            double newZoom = Math.Max(0.5, ZoomSlider.Value - 0.5);
            ZoomSlider.Value = newZoom;
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = 1.0;
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            if (tasks.Count == 0) return;
            
            double availableWidth = GanttScrollViewer.ViewportWidth;
            double totalDays = (projectEnd - projectStart).TotalDays;
            double requiredWidth = totalDays * DayWidth;
            
            double fitZoom = availableWidth / requiredWidth;
            fitZoom = Math.Max(0.5, Math.Min(3.0, fitZoom));
            
            ZoomSlider.Value = fitZoom;
        }

        private void ApplyZoom(double zoomLevel)
        {
            if (tasks.Count == 0) return;
            
            currentZoom = zoomLevel;
            ZoomPercentText.Text = $"{(int)(zoomLevel * 100)}%";
            
            RenderGanttChart();
        }
    }

    public class ProjectTask : INotifyPropertyChanged
    {
        private int id;
        private string wbs;
        private string taskName;

        public int ID 
        { 
            get => id; 
            set 
            { 
                id = value; 
                OnPropertyChanged(nameof(ID)); 
            } 
        }
        
        public string WBS 
        { 
            get => wbs; 
            set 
            { 
                wbs = value; 
                OnPropertyChanged(nameof(WBS)); 
            } 
        }
        
        public string TaskName 
        { 
            get => taskName; 
            set 
            { 
                taskName = value; 
                OnPropertyChanged(nameof(TaskName)); 
            } 
        }
        
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }
        public int Duration { get; set; }
        public List<int> Predecessors { get; set; } = new List<int>();
        
        public int IndentLevel { get; set; }
        public bool IsMilestone { get; set; }
        public SolidColorBrush PhaseColor { get; set; } = new SolidColorBrush(Colors.Blue);
        public SolidColorBrush RowBackground { get; set; } = new SolidColorBrush(Colors.White);
        
        public DateTime EarlyStart { get; set; }
        public DateTime EarlyFinish { get; set; }
        public DateTime LateStart { get; set; }
        public DateTime LateFinish { get; set; }
        public int TotalFloat { get; set; }
        public bool IsOnCriticalPath { get; set; }
        
        public Thickness IndentMargin => new Thickness(IndentLevel * 20, 0, 0, 0);
        public FontWeight FontWeight => IndentLevel == 0 ? FontWeights.Bold : FontWeights.Normal;

        public event PropertyChangedEventHandler? PropertyChanged;
        
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}