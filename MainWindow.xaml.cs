using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ProjectPlanViewer
{
    public partial class MainWindow : Window
    {
        private List<ProjectTask> tasks = new List<ProjectTask>();
        private DateTime projectStartDate;
        private DateTime projectEndDate;
        private const int RowHeight = 40;
        private const int TimelineHeaderHeight = 50;
        
        // For zoom
        private double currentZoom = 1.0;
        private int baseDayWidth = 20;
        private int DayWidth => (int)(baseDayWidth * currentZoom);
        
        // Critical path tracking
        private HashSet<int> criticalPathTaskIds = new HashSet<int>();
        
        // For drag and drop - task bars
        private Border draggedTaskBar;
        private ProjectTask draggedTask;
        private Point dragStartPoint;
        private double originalLeft;

        // For milestone drag and drop
        private System.Windows.Shapes.Path draggedMilestone;
        private ProjectTask draggedMilestoneTask;
        private Point milestoneDragStartPoint;
        private double milestoneOriginalLeft;

        public MainWindow()
        {
            InitializeComponent();
            
            if (ZoomSlider != null)
            {
                ZoomSlider.Value = 1.0;
            }
            
            string defaultFile = "mes_project_plan.csv";
            if (File.Exists(defaultFile))
            {
                LoadProjectPlan(defaultFile);
            }
        }

        private void LoadCSV_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = "Select Project Plan CSV File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadProjectPlan(openFileDialog.FileName);
            }
        }

        private void LoadProjectPlan(string filePath)
        {
            try
            {
                StatusText.Text = "Loading project plan...";
                tasks.Clear();

                var lines = File.ReadAllLines(filePath);
                if (lines.Length < 2)
                {
                    MessageBox.Show("CSV file is empty or invalid.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    var values = ParseCSVLine(lines[i]);
                    if (values.Length >= 7)
                    {
                        try
                        {
                            var task = new ProjectTask
                            {
                                ID = int.Parse(values[0]),
                                WBS = values[1],
                                TaskName = values[2],
                                Start = DateTime.Parse(values[3]),
                                Finish = DateTime.Parse(values[4]),
                                Duration = int.Parse(values[5]),
                                Predecessors = string.IsNullOrWhiteSpace(values[6]) 
                                    ? new List<int>() 
                                    : values[6].Split(';').Select(p => int.Parse(p.Trim())).ToList()
                            };
                            tasks.Add(task);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing line {i}: {ex.Message}");
                        }
                    }
                }

                if (tasks.Count == 0)
                {
                    MessageBox.Show("No valid tasks found in CSV file.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                projectStartDate = tasks.Min(t => t.Start);
                projectEndDate = tasks.Max(t => t.Finish);

                DetermineTaskProperties();
                CalculateCriticalPath();
                RenderGanttChart();

                int totalDays = (projectEndDate - projectStartDate).Days + 1;
                int criticalTaskCount = criticalPathTaskIds.Count;
                ProjectInfoText.Text = $"{tasks.Count} Tasks | " +
                    $"{projectStartDate:MMM yyyy} - {projectEndDate:MMM yyyy} | " +
                    $"{totalDays} days | {criticalTaskCount} Critical Tasks";

                StatusText.Text = $"Loaded {tasks.Count} tasks successfully | Critical path calculated";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error loading project plan";
            }
        }

        private string[] ParseCSVLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            string currentValue = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentValue.Trim());
                    currentValue = "";
                }
                else
                {
                    currentValue += c;
                }
            }
            result.Add(currentValue.Trim());

            return result.ToArray();
        }

        private void DetermineTaskProperties()
        {
            foreach (var task in tasks)
            {
                string wbs = task.WBS;
                int level = wbs.Split('.').Length - 1;
                task.IndentLevel = level;
                task.IsMilestone = task.Duration == 0;

                if (wbs.StartsWith("1."))
                    task.PhaseColor = (SolidColorBrush)FindResource("Phase1Color");
                else if (wbs.StartsWith("2."))
                    task.PhaseColor = (SolidColorBrush)FindResource("Phase2Color");
                else if (wbs.StartsWith("3."))
                    task.PhaseColor = (SolidColorBrush)FindResource("Phase3Color");
                else
                    task.PhaseColor = (SolidColorBrush)FindResource("Phase1Color");

                task.RowBackground = (tasks.IndexOf(task) % 2 == 0) 
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(250, 250, 250));
            }
        }

        private void CalculateCriticalPath()
        {
            criticalPathTaskIds.Clear();

            // Reset all task calculations
            foreach (var task in tasks)
            {
                task.EarlyStart = DateTime.MinValue;
                task.EarlyFinish = DateTime.MinValue;
                task.LateStart = DateTime.MaxValue;
                task.LateFinish = DateTime.MaxValue;
                task.TotalFloat = 0;
                task.IsOnCriticalPath = false;
            }

            // Forward pass - calculate Early Start and Early Finish
            CalculateForwardPass();

            // Backward pass - calculate Late Start and Late Finish
            CalculateBackwardPass();

            // Calculate float and identify critical path
            foreach (var task in tasks)
            {
                if (task.EarlyStart != DateTime.MinValue && task.LateStart != DateTime.MaxValue)
                {
                    task.TotalFloat = (task.LateStart - task.EarlyStart).Days;
                    
                    // Critical path tasks have zero or near-zero float
                    if (task.TotalFloat <= 0)
                    {
                        task.IsOnCriticalPath = true;
                        criticalPathTaskIds.Add(task.ID);
                    }
                }
            }
        }

        private void CalculateForwardPass()
        {
            // Find tasks with no predecessors (start tasks)
            var startTasks = tasks.Where(t => t.Predecessors == null || t.Predecessors.Count == 0).ToList();
            
            foreach (var task in startTasks)
            {
                task.EarlyStart = task.Start;
                task.EarlyFinish = task.Finish;
            }

            // Process remaining tasks in dependency order
            bool changed = true;
            int iterations = 0;
            int maxIterations = tasks.Count * 2; // Prevent infinite loops

            while (changed && iterations < maxIterations)
            {
                changed = false;
                iterations++;

                foreach (var task in tasks)
                {
                    if (task.EarlyStart != DateTime.MinValue)
                        continue; // Already calculated

                    if (task.Predecessors != null && task.Predecessors.Count > 0)
                    {
                        bool allPredecessorsCalculated = true;
                        DateTime maxPredecessorFinish = DateTime.MinValue;

                        foreach (var predId in task.Predecessors)
                        {
                            var predecessor = tasks.FirstOrDefault(t => t.ID == predId);
                            if (predecessor != null)
                            {
                                if (predecessor.EarlyFinish == DateTime.MinValue)
                                {
                                    allPredecessorsCalculated = false;
                                    break;
                                }
                                if (predecessor.EarlyFinish > maxPredecessorFinish)
                                {
                                    maxPredecessorFinish = predecessor.EarlyFinish;
                                }
                            }
                        }

                        if (allPredecessorsCalculated && maxPredecessorFinish != DateTime.MinValue)
                        {
                            // Early start is the day after the latest predecessor finishes
                            task.EarlyStart = maxPredecessorFinish.AddDays(1);
                            task.EarlyFinish = task.EarlyStart.AddDays(task.Duration);
                            changed = true;
                        }
                    }
                }
            }
        }

        private void CalculateBackwardPass()
        {
            // Find the project end date (latest Early Finish)
            DateTime projectEnd = tasks.Where(t => t.EarlyFinish != DateTime.MinValue)
                                       .Max(t => t.EarlyFinish);

            // Find tasks with no successors (end tasks)
            var taskIds = new HashSet<int>(tasks.Select(t => t.ID));
            var successorIds = new HashSet<int>();
            
            foreach (var task in tasks)
            {
                if (task.Predecessors != null)
                {
                    foreach (var predId in task.Predecessors)
                    {
                        successorIds.Add(predId);
                    }
                }
            }

            var endTasks = tasks.Where(t => !successorIds.Contains(t.ID)).ToList();

            foreach (var task in endTasks)
            {
                if (task.EarlyFinish != DateTime.MinValue)
                {
                    task.LateFinish = task.EarlyFinish;
                    task.LateStart = task.LateFinish.AddDays(-task.Duration);
                }
            }

            // Process remaining tasks in reverse dependency order
            bool changed = true;
            int iterations = 0;
            int maxIterations = tasks.Count * 2;

            while (changed && iterations < maxIterations)
            {
                changed = false;
                iterations++;

                foreach (var task in tasks)
                {
                    if (task.LateFinish != DateTime.MaxValue)
                        continue; // Already calculated

                    // Find all successors of this task
                    var successors = tasks.Where(t => t.Predecessors != null && 
                                                     t.Predecessors.Contains(task.ID)).ToList();

                    if (successors.Count > 0)
                    {
                        bool allSuccessorsCalculated = true;
                        DateTime minSuccessorStart = DateTime.MaxValue;

                        foreach (var successor in successors)
                        {
                            if (successor.LateStart == DateTime.MaxValue)
                            {
                                allSuccessorsCalculated = false;
                                break;
                            }
                            if (successor.LateStart < minSuccessorStart)
                            {
                                minSuccessorStart = successor.LateStart;
                            }
                        }

                        if (allSuccessorsCalculated && minSuccessorStart != DateTime.MaxValue)
                        {
                            // Late finish is the day before the earliest successor starts
                            task.LateFinish = minSuccessorStart.AddDays(-1);
                            task.LateStart = task.LateFinish.AddDays(-task.Duration);
                            changed = true;
                        }
                    }
                }
            }
        }

        private void RenderGanttChart()
        {
            TaskListItems.ItemsSource = null;
            TimelineCanvas.Children.Clear();

            int totalDays = (projectEndDate - projectStartDate).Days + 1;
            double timelineWidth = totalDays * DayWidth;
            double timelineHeight = tasks.Count * RowHeight + TimelineHeaderHeight;

            TimelineCanvas.Width = timelineWidth;
            TimelineCanvas.Height = timelineHeight;

            DrawTimelineHeader(timelineWidth);
            DrawGridLines(timelineWidth, timelineHeight);
            DrawTaskBars();
            DrawDependencies();

            TaskListItems.ItemsSource = tasks;
        }

        private void DrawTimelineHeader(double width)
        {
            var headerBackground = new Rectangle
            {
                Width = width,
                Height = TimelineHeaderHeight,
                Fill = new SolidColorBrush(Color.FromRgb(236, 240, 241))
            };
            Canvas.SetTop(headerBackground, 0);
            Canvas.SetLeft(headerBackground, 0);
            TimelineCanvas.Children.Add(headerBackground);

            DateTime currentMonth = new DateTime(projectStartDate.Year, projectStartDate.Month, 1);
            DateTime endMonth = new DateTime(projectEndDate.Year, projectEndDate.Month, 1).AddMonths(1);

            while (currentMonth < endMonth)
            {
                DateTime nextMonth = currentMonth.AddMonths(1);
                DateTime monthEnd = nextMonth < endMonth ? nextMonth : projectEndDate.AddDays(1);
                
                int daysFromStart = (currentMonth - projectStartDate).Days;
                
                double x = Math.Max(0, daysFromStart * DayWidth);

                var separator = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = TimelineHeaderHeight,
                    Stroke = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                    StrokeThickness = 1
                };
                TimelineCanvas.Children.Add(separator);

                var monthLabel = new TextBlock
                {
                    Text = currentMonth.ToString("MMM yyyy"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80))
                };
                Canvas.SetTop(monthLabel, 8);
                Canvas.SetLeft(monthLabel, x + 10);
                TimelineCanvas.Children.Add(monthLabel);

                DateTime weekStart = currentMonth;
                while (weekStart < monthEnd)
                {
                    int weekDaysFromStart = (weekStart - projectStartDate).Days;
                    double weekX = weekDaysFromStart * DayWidth;
                    
                    var weekLabel = new TextBlock
                    {
                        Text = $"W{CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(weekStart, CalendarWeekRule.FirstDay, DayOfWeek.Monday)}",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141))
                    };
                    Canvas.SetTop(weekLabel, 28);
                    Canvas.SetLeft(weekLabel, weekX + 2);
                    TimelineCanvas.Children.Add(weekLabel);

                    weekStart = weekStart.AddDays(7);
                }

                currentMonth = nextMonth;
            }

            var bottomBorder = new Line
            {
                X1 = 0,
                Y1 = TimelineHeaderHeight,
                X2 = width,
                Y2 = TimelineHeaderHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                StrokeThickness = 2
            };
            TimelineCanvas.Children.Add(bottomBorder);
        }

        private void DrawGridLines(double width, double height)
        {
            int weekNumber = 0;
            for (DateTime date = projectStartDate; date <= projectEndDate; date = date.AddDays(7))
            {
                int daysFromStart = (date - projectStartDate).Days;
                double x = daysFromStart * DayWidth;
                double weekWidth = 7 * DayWidth;
                
                if (weekNumber % 2 == 1)
                {
                    var weekShading = new Rectangle
                    {
                        Width = Math.Min(weekWidth, width - x),
                        Height = height - TimelineHeaderHeight,
                        Fill = new SolidColorBrush(Color.FromArgb(25, 52, 152, 219)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(weekShading, x);
                    Canvas.SetTop(weekShading, TimelineHeaderHeight);
                    TimelineCanvas.Children.Add(weekShading);
                }
                
                weekNumber++;
            }

            for (DateTime date = projectStartDate; date <= projectEndDate; date = date.AddDays(7))
            {
                int daysFromStart = (date - projectStartDate).Days;
                double x = daysFromStart * DayWidth;

                var line = new Line
                {
                    X1 = x,
                    Y1 = TimelineHeaderHeight,
                    X2 = x,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    StrokeThickness = 1
                };
                TimelineCanvas.Children.Add(line);
            }

            for (int i = 0; i <= tasks.Count; i++)
            {
                double y = TimelineHeaderHeight + i * RowHeight;
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    StrokeThickness = 1
                };
                TimelineCanvas.Children.Add(line);
            }

            if (DateTime.Today >= projectStartDate && DateTime.Today <= projectEndDate)
            {
                int daysFromStart = (DateTime.Today - projectStartDate).Days;
                double x = daysFromStart * DayWidth;

                var todayLine = new Line
                {
                    X1 = x,
                    Y1 = TimelineHeaderHeight,
                    X2 = x,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 3 }
                };
                Panel.SetZIndex(todayLine, 100);
                TimelineCanvas.Children.Add(todayLine);
            }
        }

        private void DrawTaskBars()
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                double y = TimelineHeaderHeight + i * RowHeight;

                int startDay = (task.Start - projectStartDate).Days;
                int duration = Math.Max(1, (task.Finish - task.Start).Days + 1);

                double x = startDay * DayWidth;
                double width = duration * DayWidth;

                if (task.IsMilestone)
                {
                    DrawMilestone(x, y + RowHeight / 2, task);
                }
                else
                {
                    DrawTaskBar(x, y, width, task);
                }
            }
        }

        private void DrawTaskBar(double x, double y, double width, ProjectTask task)
        {
            // Use critical path color if task is on critical path
            SolidColorBrush barColor = task.IsOnCriticalPath 
                ? (SolidColorBrush)FindResource("CriticalPathColor")
                : task.PhaseColor;

            var taskBar = new Border
            {
                Width = width - 4,
                Height = 24,
                Background = barColor,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                BorderThickness = new Thickness(task.IsOnCriticalPath ? 2 : 0.5),
                Cursor = Cursors.Hand,
                Tag = task,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = task.IsOnCriticalPath ? Colors.Red : Colors.Black,
                    Opacity = task.IsOnCriticalPath ? 0.4 : 0.2,
                    BlurRadius = task.IsOnCriticalPath ? 6 : 4,
                    ShadowDepth = 2
                }
            };

            var label = new TextBlock
            {
                Text = task.TaskName,
                FontSize = 10,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5, 0, 5, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
                FontWeight = task.IsOnCriticalPath ? FontWeights.Bold : FontWeights.Normal
            };
            taskBar.Child = label;

            Canvas.SetLeft(taskBar, x + 2);
            Canvas.SetTop(taskBar, y + (RowHeight - 24) / 2);
            Panel.SetZIndex(taskBar, task.IsOnCriticalPath ? 15 : 10);
            TimelineCanvas.Children.Add(taskBar);

            string criticalPathInfo = task.IsOnCriticalPath 
                ? "\n⚠ CRITICAL PATH - No schedule slack!" 
                : $"\nFloat: {task.TotalFloat} days";

            taskBar.ToolTip = $"{task.TaskName}\n" +
                             $"Start: {task.Start:MMM dd, yyyy}\n" +
                             $"Finish: {task.Finish:MMM dd, yyyy}\n" +
                             $"Duration: {task.Duration} days" +
                             criticalPathInfo +
                             $"\n(Drag to reschedule)";

            taskBar.MouseLeftButtonDown += TaskBar_MouseLeftButtonDown;
            taskBar.MouseMove += TaskBar_MouseMove;
            taskBar.MouseLeftButtonUp += TaskBar_MouseLeftButtonUp;
        }

        private void TaskBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            draggedTaskBar = sender as Border;
            draggedTask = draggedTaskBar.Tag as ProjectTask;
            dragStartPoint = e.GetPosition(TimelineCanvas);
            originalLeft = Canvas.GetLeft(draggedTaskBar);
            
            draggedTaskBar.CaptureMouse();
            Panel.SetZIndex(draggedTaskBar, 1000);
            draggedTaskBar.Opacity = 0.7;
            
            e.Handled = true;
        }

        private void TaskBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedTaskBar != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPosition = e.GetPosition(TimelineCanvas);
                double deltaX = currentPosition.X - dragStartPoint.X;
                
                int daysDelta = (int)Math.Round(deltaX / DayWidth);
                double newLeft = originalLeft + (daysDelta * DayWidth);
                
                if (newLeft >= 0 && newLeft + draggedTaskBar.Width <= TimelineCanvas.Width)
                {
                    Canvas.SetLeft(draggedTaskBar, newLeft);
                }
                
                e.Handled = true;
            }
        }

        private void TaskBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (draggedTaskBar != null)
            {
                draggedTaskBar.ReleaseMouseCapture();
                draggedTaskBar.Opacity = 1.0;
                Panel.SetZIndex(draggedTaskBar, 10);
                
                double newLeft = Canvas.GetLeft(draggedTaskBar);
                int daysDelta = (int)Math.Round((newLeft - originalLeft) / DayWidth);
                
                if (daysDelta != 0)
                {
                    draggedTask.Start = draggedTask.Start.AddDays(daysDelta);
                    draggedTask.Finish = draggedTask.Finish.AddDays(daysDelta);
                    
                    RedrawDependencies();
                    
                    StatusText.Text = $"Task '{draggedTask.TaskName}' rescheduled: {draggedTask.Start:MMM dd} - {draggedTask.Finish:MMM dd}";
                }
                
                draggedTaskBar = null;
                draggedTask = null;
                e.Handled = true;
            }
        }

        private void DrawMilestone(double x, double y, ProjectTask task)
        {
            var fillBrush = task.IsOnCriticalPath 
                ? (SolidColorBrush)FindResource("CriticalPathColor")
                : (SolidColorBrush)FindResource("MilestoneColor");

            var strokeBrush = task.IsOnCriticalPath
                ? new SolidColorBrush(Color.FromRgb(169, 50, 38))
                : new SolidColorBrush(Color.FromRgb(192, 57, 43));

            var diamond = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 12,0 L 24,12 L 12,24 L 0,12 Z"),
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = task.IsOnCriticalPath ? 2 : 1.5,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Fill,
                Tag = task,
                Cursor = Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = task.IsOnCriticalPath ? Colors.Red : Colors.Black,
                    Opacity = task.IsOnCriticalPath ? 0.4 : 0.3,
                    BlurRadius = task.IsOnCriticalPath ? 4 : 3,
                    ShadowDepth = 1
                }
            };

            Canvas.SetLeft(diamond, x - 12);
            Canvas.SetTop(diamond, y - 12);
            Panel.SetZIndex(diamond, task.IsOnCriticalPath ? 15 : 10);
            TimelineCanvas.Children.Add(diamond);

            diamond.MouseLeftButtonDown += Milestone_MouseLeftButtonDown;
            diamond.MouseMove += Milestone_MouseMove;
            diamond.MouseLeftButtonUp += Milestone_MouseLeftButtonUp;

            var label = new TextBlock
            {
                Text = task.TaskName,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
                FontWeight = task.IsOnCriticalPath ? FontWeights.Bold : FontWeights.Bold,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, x + 15);
            Canvas.SetTop(label, y - 8);
            TimelineCanvas.Children.Add(label);

            string criticalPathInfo = task.IsOnCriticalPath 
                ? "\n⚠ CRITICAL MILESTONE - No schedule slack!" 
                : $"\nFloat: {task.TotalFloat} days";

            diamond.ToolTip = $"{task.TaskName}\n" +
                             $"Date: {task.Start:MMM dd, yyyy}\n" +
                             $"Milestone" +
                             criticalPathInfo +
                             $" (Drag to reschedule)";
        }

        private void Milestone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            draggedMilestone = sender as System.Windows.Shapes.Path;
            draggedMilestoneTask = draggedMilestone.Tag as ProjectTask;
            milestoneDragStartPoint = e.GetPosition(TimelineCanvas);
            milestoneOriginalLeft = Canvas.GetLeft(draggedMilestone) + 12;
            
            draggedMilestone.CaptureMouse();
            Panel.SetZIndex(draggedMilestone, 1000);
            draggedMilestone.Opacity = 0.7;
            
            e.Handled = true;
        }

        private void Milestone_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedMilestone != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPosition = e.GetPosition(TimelineCanvas);
                double deltaX = currentPosition.X - milestoneDragStartPoint.X;
                
                int daysDelta = (int)Math.Round(deltaX / DayWidth);
                double newLeft = milestoneOriginalLeft + (daysDelta * DayWidth);
                
                if (newLeft >= 0 && newLeft <= TimelineCanvas.Width)
                {
                    Canvas.SetLeft(draggedMilestone, newLeft - 12);
                }
                
                e.Handled = true;
            }
        }

        private void Milestone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (draggedMilestone != null)
            {
                draggedMilestone.ReleaseMouseCapture();
                draggedMilestone.Opacity = 1.0;
                Panel.SetZIndex(draggedMilestone, 10);
                
                double newLeft = Canvas.GetLeft(draggedMilestone) + 12;
                int daysDelta = (int)Math.Round((newLeft - milestoneOriginalLeft) / DayWidth);
                
                if (daysDelta != 0)
                {
                    draggedMilestoneTask.Start = draggedMilestoneTask.Start.AddDays(daysDelta);
                    draggedMilestoneTask.Finish = draggedMilestoneTask.Finish.AddDays(daysDelta);
                    
                    RedrawDependencies();
                    
                    StatusText.Text = $"Milestone '{draggedMilestoneTask.TaskName}' rescheduled to: {draggedMilestoneTask.Start:MMM dd, yyyy}";
                }
                
                draggedMilestone = null;
                draggedMilestoneTask = null;
                e.Handled = true;
            }
        }

        private void DrawDependencies()
        {
            foreach (var task in tasks)
            {
                if (task.Predecessors != null && task.Predecessors.Count > 0)
                {
                    foreach (var predId in task.Predecessors)
                    {
                        var predecessor = tasks.FirstOrDefault(t => t.ID == predId);
                        if (predecessor != null)
                        {
                            DrawDependencyLine(predecessor, task);
                        }
                    }
                }
            }
        }

        private void RedrawDependencies()
        {
            var dependencyElements = TimelineCanvas.Children.OfType<System.Windows.Shapes.Path>()
                .Where(p => p.Tag?.ToString() == "dependency")
                .ToList();
            
            foreach (var element in dependencyElements)
            {
                TimelineCanvas.Children.Remove(element);
            }
            
            var arrowElements = TimelineCanvas.Children.OfType<Polygon>()
                .Where(p => p.Tag?.ToString() == "dependency")
                .ToList();
            
            foreach (var element in arrowElements)
            {
                TimelineCanvas.Children.Remove(element);
            }
            
            DrawDependencies();
        }

        private void DrawDependencyLine(ProjectTask from, ProjectTask to)
        {
            int fromIndex = tasks.IndexOf(from);
            int toIndex = tasks.IndexOf(to);

            double fromX = (from.Finish - projectStartDate).Days * DayWidth + DayWidth;
            double fromY = TimelineHeaderHeight + fromIndex * RowHeight + RowHeight / 2;

            double toX = (to.Start - projectStartDate).Days * DayWidth;
            double toY = TimelineHeaderHeight + toIndex * RowHeight + RowHeight / 2;

            // Check if this dependency is part of critical path
            bool isCriticalDependency = from.IsOnCriticalPath && to.IsOnCriticalPath;

            var pathFigure = new PathFigure { StartPoint = new Point(fromX, fromY) };
            
            double midX = (fromX + toX) / 2;
            pathFigure.Segments.Add(new BezierSegment(
                new Point(midX, fromY),
                new Point(midX, toY),
                new Point(toX - 8, toY),
                true
            ));

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(pathFigure);

            var dependencyPath = new System.Windows.Shapes.Path
            {
                Data = pathGeometry,
                Stroke = isCriticalDependency 
                    ? (SolidColorBrush)FindResource("CriticalPathDependencyColor")
                    : (SolidColorBrush)FindResource("DependencyLineColor"),
                StrokeThickness = isCriticalDependency ? 2 : 1.5,
                StrokeDashArray = isCriticalDependency ? null : new DoubleCollection { 4, 2 },
                Tag = "dependency"
            };

            Panel.SetZIndex(dependencyPath, isCriticalDependency ? 5 : 1);
            TimelineCanvas.Children.Add(dependencyPath);

            var arrowHead = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(toX - 8, toY),
                    new Point(toX - 8, toY - 4),
                    new Point(toX, toY),
                    new Point(toX - 8, toY + 4)
                },
                Fill = isCriticalDependency 
                    ? (SolidColorBrush)FindResource("CriticalPathDependencyColor")
                    : (SolidColorBrush)FindResource("DependencyLineColor"),
                Tag = "dependency"
            };

            Panel.SetZIndex(arrowHead, isCriticalDependency ? 5 : 1);
            TimelineCanvas.Children.Add(arrowHead);
        }

        private void TaskName_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox?.DataContext is ProjectTask task)
            {
                UpdateTaskBarLabel(task);
            }
        }

        private void UpdateTaskBarLabel(ProjectTask task)
        {
            var taskBars = TimelineCanvas.Children.OfType<Border>()
                .Where(b => b.Tag is ProjectTask t && t.ID == task.ID);
            
            foreach (var taskBar in taskBars)
            {
                var label = taskBar.Child as TextBlock;
                if (label != null)
                {
                    label.Text = task.TaskName;
                }
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (tasks.Count == 0) return;
            ApplyZoom(e.NewValue);
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
        public List<int> Predecessors { get; set; }
        
        public int IndentLevel { get; set; }
        public bool IsMilestone { get; set; }
        public SolidColorBrush PhaseColor { get; set; }
        public SolidColorBrush RowBackground { get; set; }
        
        // Critical Path Analysis properties
        public DateTime EarlyStart { get; set; }
        public DateTime EarlyFinish { get; set; }
        public DateTime LateStart { get; set; }
        public DateTime LateFinish { get; set; }
        public int TotalFloat { get; set; }
        public bool IsOnCriticalPath { get; set; }
        
        public Thickness IndentMargin => new Thickness(IndentLevel * 20, 0, 0, 0);
        public FontWeight FontWeight => IndentLevel == 0 ? FontWeights.Bold : FontWeights.Normal;

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}