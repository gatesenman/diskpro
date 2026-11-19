using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DiskPro.App.Services;
using DiskPro.App.ViewModels;
namespace DiskPro.App;

public partial class MainWindow : Window
{
    private readonly Stack<(FrameworkElement Card, Action Cancel)> _overlays = new();
    public bool ForceClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Title = "DiskPro C盘清理大师";
        StateChanged += (_, _) => UpdateMaxState();
        SourceInitialized += (_, _) => UpdateMaxState();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _overlays.Count > 0) { _overlays.Peek().Cancel(); e.Handled = true; } };
        PageHost.TargetUpdated += (_, _) => AnimatePage();
        ((MainViewModel)DataContext).PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.CurrentPage)) AnimatePage(); };
        Closing += (_, e) =>
        {
            if (!ForceClose && AppHost.Settings.EnableTray && AppHost.Settings.MinimizeToTray && TrayService.IsActive)
            {
                e.Cancel = true; Hide();
                TrayService.Balloon("DiskPro 仍在后台运行", "双击托盘图标可重新打开主界面。");
            }
        };
    }

    public MainViewModel Vm => (MainViewModel)DataContext;

    private void UpdateMaxState()
    {
        // WindowChrome maximized windows overflow the screen by the resize border; compensate.
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaxBtn.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
    }

    private void AnimatePage()
    {
        var sb = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        PageHost.BeginAnimation(OpacityProperty, sb);
        var tt = new System.Windows.Media.TranslateTransform(0, 8);
        PageHost.RenderTransform = tt;
        tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- overlay dialogs ----
    public void ShowOverlay(FrameworkElement card, Action cancel)
    {
        _overlays.Push((card, cancel));
        OverlayContent.Content = card;
        Overlay.Visibility = Visibility.Visible;
        card.Opacity = 0;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        var st = new System.Windows.Media.ScaleTransform(0.96, 0.96); card.RenderTransformOrigin = new Point(0.5, 0.5); card.RenderTransform = st;
        var a = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, a); st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, a);
    }

    public void CloseOverlay(FrameworkElement card)
    {
        if (_overlays.Count > 0 && _overlays.Peek().Card == card) _overlays.Pop();
        if (_overlays.Count > 0) OverlayContent.Content = _overlays.Peek().Card;
        else { OverlayContent.Content = null; Overlay.Visibility = Visibility.Collapsed; }
    }

    public void AddToast(FrameworkElement toast)
    {
        ToastPanel.Children.Add(toast);
        toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => ToastPanel.Children.Remove(toast);
            toast.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
        while (ToastPanel.Children.Count > 4) ToastPanel.Children.RemoveAt(0);
    }
}
