using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiskPro.App.ViewModels;
namespace DiskPro.App.Services;

/// <summary>--screenshots &lt;dir&gt; [--scan]: opens the main window, visits every page (optionally running a read-only scan first), saves PNGs.</summary>
public static class ScreenshotRunner
{
    public static async void Run(Application app, string dir, bool scan)
    {
        var log = new StringBuilder();
        try
        {
            Directory.CreateDirectory(dir);
            var w = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40 };
            app.MainWindow = w;
            w.Show();
            await Wait(800);
            var vm = w.Vm;
            int i = 0;
            foreach (var size in new[] { (1280, 800), (1100, 700) })
            {
                w.Width = size.Item1; w.Height = size.Item2;
                await Wait(300);
                foreach (var n in vm.AllNav.ToList())
                {
                    if (size.Item1 == 1100 && n.Key is not ("dashboard" or "junk" or "chat" or "settings" or "sysmove" or "large")) continue;
                    vm.Navigate(n.Key);
                    await Wait(600);
                    if (scan && size.Item1 == 1280 && n.Page.ScreenshotPrepare() is Task t)
                    {
                        var done = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(150)));
                        log.AppendLine($"{n.Key}: prepare {(done == t ? "ok" : "timeout")}");
                        await Wait(800);
                    }
                    var file = Path.Combine(dir, $"{++i:00}-{n.Key}-{size.Item1}x{size.Item2}.png");
                    Save(w, file);
                    log.AppendLine("saved " + file);
                }
            }
            // light theme samples
            ThemeManager.Apply(!AppHost.Settings.DarkTheme);
            w.Width = 1280; w.Height = 800;
            foreach (var key in new[] { "dashboard", "junk", "settings" })
            {
                vm.Navigate(key); await Wait(600);
                Save(w, Path.Combine(dir, $"{++i:00}-{key}-alt-theme.png"));
            }
            ThemeManager.Apply(AppHost.Settings.DarkTheme);
            // dialog sample
            vm.Navigate("dashboard"); await Wait(300);
            _ = w.Dispatcher.BeginInvoke(() => Dialogs.Confirm("这是对话框样式示例。确定要执行此操作吗？", "确认操作"));
            await Wait(700);
            Save(w, Path.Combine(dir, $"{++i:00}-dialog.png"));
            log.AppendLine("RESULT: PASS");
        }
        catch (Exception ex) { log.AppendLine("RESULT: FAIL " + ex); App.WriteCrash("screenshots", ex); }
        try { File.WriteAllText(Path.Combine(dir, "screenshots.txt"), log.ToString(), Encoding.UTF8); } catch { }
        Environment.Exit(0);
    }

    private static async Task Wait(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window w, string file)
    {
        var root = (FrameworkElement)w.Content;
        root.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(w);
        var rtb = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)w.FindResource("BgBrush"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file); enc.Save(fs);
    }
}
