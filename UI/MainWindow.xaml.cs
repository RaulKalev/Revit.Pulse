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
                Width  = p.Width;
                Height = p.Height;
                TheDiagramPanel.RestoreState(p.DiagramPanelWidth);
            }
            else
            {
                // First launch — start collapsed
                TheDiagramPanel.RestoreState(300);
            }

            TheDiagramPanel.PanelStateChanged += SavePlacement;
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SavePlacement();
            _viewModel.SaveExpandState();
        }

        private void SavePlacement()
        {
            WindowPlacementService.Save(Left, Top, Width, Height, TheDiagramPanel.GetExpandedWidth());
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
