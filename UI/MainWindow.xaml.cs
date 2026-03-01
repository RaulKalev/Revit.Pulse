using System;
using System.Windows;
using System.Windows.Input;
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

        public MainWindow(UIApplication uiApp)
        {
            InitializeComponent();

            _viewModel = new MainViewModel(uiApp);
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

            TheDiagramPanel.PanelStateChanged += SavePlacement;

            // Restore metrics panel state.
            bool metricsExpanded = p?.MetricsPanelExpanded ?? false;
            _metricsHeight = p?.MetricsPanelHeight ?? 300;
            MetricsPanel.IsExpanded = metricsExpanded;
            UpdateMetricsSplitter(metricsExpanded, _metricsHeight);
            MetricsPanel.ExpandedChanged += isExpanded => UpdateMetricsSplitter(isExpanded, _metricsHeight);

            // Auto-load data on first open — no need to press Refresh manually.
            _viewModel.RefreshCommand.Execute(null);
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SavePlacement();
            _viewModel.SaveExpandState();
        }

        private void UpdateMetricsSplitter(bool expanded, double height)
        {
            if (expanded)
            {
                MetricsRow.MinHeight        = 300;
                MetricsSplitterRow.Height   = new System.Windows.GridLength(1);
                MetricsSplitter.IsEnabled   = true;
                MetricsRow.Height           = new System.Windows.GridLength(Math.Max(height, 300));
            }
            else
            {
                // Snapshot current height before collapsing so we can restore it later.
                if (MetricsRow.ActualHeight > 28)
                    _metricsHeight = MetricsRow.ActualHeight;
                // Clear the minimum so the row can collapse to header-only height.
                MetricsRow.MinHeight        = 0;
                // Keep the 1px line visible but lock resizing.
                MetricsSplitterRow.Height   = new System.Windows.GridLength(1);
                MetricsSplitter.IsEnabled   = false;
                MetricsRow.Height           = new System.Windows.GridLength(28);
            }
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
