using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Autodesk.Revit.UI;
using Pulse.Helpers;
using Pulse.UI.ViewModels;

namespace Pulse.UI
{
    /// <summary>
    /// Main Pulse window. Preserves the same visual shell as the original ProSchedules window:
    /// borderless, Material Design, custom title bar, resize grips, dark theme.
    /// Content is bound to MainViewModel via MVVM.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly WindowResizer _resizer;
        private readonly MainViewModel _viewModel;
        private double _metricsHeight = 150;

        /// <summary>Exposes the root ViewModel so the launch command can flush pending ES writes on close.</summary>
        public MainViewModel ViewModel => _viewModel;

        public MainWindow(UIApplication uiApp, string initialModuleId = null)
        {
            InitializeComponent();

            _viewModel = new MainViewModel(uiApp, initialModuleId);
            DataContext = _viewModel;
            _viewModel.Initialize(this);

            _resizer = new WindowResizer(this);

            Loaded  += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var p = WindowPlacementService.Load();
            if (p != null)
            {
                Left   = p.Left;
                Top    = p.Top;
                Width  = Math.Max(p.Width,  WindowResizer.MIN_WIDTH);
                Height = Math.Max(p.Height, WindowResizer.MIN_HEIGHT);
                TheDiagramPanel.RestoreState(p.DiagramPanelWidth);
            }
            else
            {
                // First launch — start collapsed
                TheDiagramPanel.RestoreState(300);
            }

            TheDiagramPanel.PanelStateChanged += () =>
            {
                DiagramSplitter.IsEnabled = TheDiagramPanel.IsExpanded;
                SavePlacement();
            };
            DiagramSplitter.IsEnabled = TheDiagramPanel.IsExpanded;

            // Restore metrics panel state.
            bool metricsExpanded = p?.MetricsPanelExpanded ?? false;
            _metricsHeight = p?.MetricsPanelHeight ?? 300;
            MetricsPanel.IsExpanded = metricsExpanded;
            UpdateMetricsSplitter(metricsExpanded, _metricsHeight);
            MetricsPanel.ExpandedChanged += isExpanded => UpdateMetricsSplitter(isExpanded, _metricsHeight, animate: true);

            // Auto-load data on first open — no need to press Refresh manually.
            _viewModel.RefreshCommand.Execute(null);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SavePlacement();
            _viewModel.SaveExpandState();
        }

        private void UpdateMetricsSplitter(bool expanded, double height, bool animate = false)
        {
            if (expanded)
            {
                MetricsSplitterRow.Height = new GridLength(4);
                MetricsSplitter.IsEnabled = true;

                if (animate && IsLoaded)
                {
                    double from = MetricsRow.ActualHeight > 28 ? MetricsRow.ActualHeight : 28;
                    MetricsRow.MinHeight = 0;
                    AnimateRowHeight(MetricsRow, from, Math.Max(height, 300),
                        onCompleted: () => MetricsRow.MinHeight = 300);
                }
                else
                {
                    MetricsRow.MinHeight = 300;
                    MetricsRow.Height    = new GridLength(Math.Max(height, 300));
                }
            }
            else
            {
                if (MetricsRow.ActualHeight > 28)
                    _metricsHeight = MetricsRow.ActualHeight;

                MetricsRow.MinHeight      = 0;
                MetricsSplitter.IsEnabled = false;
                MetricsSplitterRow.Height = new GridLength(4);

                if (animate && IsLoaded)
                {
                    double from = MetricsRow.ActualHeight > 28
                        ? MetricsRow.ActualHeight : Math.Max(height, 300);
                    AnimateRowHeight(MetricsRow, from, 28);
                }
                else
                {
                    MetricsRow.Height = new GridLength(28);
                }
            }
        }

        private void AnimateRowHeight(RowDefinition row, double from, double to, Action onCompleted = null)
        {
            var anim = new GridLengthAnimation
            {
                From           = new GridLength(from),
                To             = new GridLength(to),
                Duration       = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior   = FillBehavior.Stop,
            };
            anim.Completed += (s, e) =>
            {
                row.BeginAnimation(RowDefinition.HeightProperty, null);
                row.Height = new GridLength(to);
                onCompleted?.Invoke();
            };
            row.BeginAnimation(RowDefinition.HeightProperty, anim);
        }

        private void SavePlacement()
        {
            if (MetricsPanel.IsExpanded && MetricsRow.ActualHeight > 28)
                _metricsHeight = MetricsRow.ActualHeight;

            WindowPlacementService.Save(
                Left, Top, Width, Height,
                TheDiagramPanel.GetExpandedWidth(),
                MetricsPanel.IsExpanded,
                _metricsHeight);
        }

        // ---- Resize Grip Handlers (same pattern as original ProSchedules) ----

        private void ResizeLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.Left);

        private void ResizeRight_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.Right);

        private void ResizeBottom_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.Bottom);

        private void ResizeBottomLeft_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.BottomLeft);

        private void ResizeBottomRight_MouseDown(object sender, MouseButtonEventArgs e)
            => _resizer.StartResizing(e, ResizeDirection.BottomRight);

        private void Window_MouseMove(object sender, MouseEventArgs e)
            => _resizer.ResizeWindow(e);

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
            => _resizer.StopResizing();
    }
}
