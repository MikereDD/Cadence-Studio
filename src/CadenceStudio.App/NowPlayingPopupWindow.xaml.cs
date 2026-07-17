using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CadenceStudio.App.ViewModels;

namespace CadenceStudio.App;

public partial class NowPlayingPopupWindow : Window
{
    private readonly Window _mainWindow;
    private readonly DispatcherTimer _timer;

    public NowPlayingPopupWindow(MainWindowViewModel viewModel, Window mainWindow)
    {
        InitializeComponent();
        DataContext = viewModel;
        _mainWindow = mainWindow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(viewModel.SongChangePopupDurationSeconds) };
        _timer.Tick += (_, _) => HideAnimated();
        Loaded += (_, _) => { PositionPopup(viewModel.SongChangePopupCorner); ShowAnimated(); };
    }

    private void PositionPopup(string corner)
    {
        var area = SystemParameters.WorkArea;
        const double gap = 40;
        Left = corner.EndsWith("Left", StringComparison.Ordinal) ? area.Left + gap : area.Right - Width - gap;
        Top = corner.StartsWith("Top", StringComparison.Ordinal) ? area.Top + gap : area.Bottom - Height - gap;
    }

    private void ShowAnimated()
    {
        _timer.Stop();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        _timer.Start();
    }

    private void HideAnimated()
    {
        _timer.Stop();
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show(); _mainWindow.Activate(); _mainWindow.Focus();
        HideAnimated();
    }
}
