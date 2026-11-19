using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using DiskPro.App.ViewModels;
namespace DiskPro.App.Services;

/// <summary>--smoketest: opens MainWindow, visits every page, records binding/XAML errors, closes.</summary>
public static class SmokeTest
{
    public static string OutFile => Path.Combine(DiskPro.App.App.DataDir, "smoketest.txt");

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = new();
        private readonly StringBuilder _buf = new();
        public override void Write(string? message) => _buf.Append(message);
        public override void WriteLine(string? message) { _buf.Append(message); lock (Errors) Errors.Add(_buf.ToString()); _buf.Clear(); }
    }

    public static void Run(Application app)
    {
        var sb = new StringBuilder();
        int errors = 0;
        sb.AppendLine($"DiskPro smoketest {DateTime.Now:yyyy-MM-dd HH:mm:ss} OS={Environment.OSVersion}");
        var listener = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

        MainWindow? w = null;
        try
        {
            w = new MainWindow();
            app.MainWindow = w;
            w.Show();
            Pump();
            sb.AppendLine("[OK]   MainWindow 打开");
            var vm = (MainViewModel)w.DataContext;
            foreach (var item in vm.Nav.Concat(vm.FooterNav))
            {
                try
                {
                    vm.Navigate(item.Key);
                    Pump();
                    w.UpdateLayout();
                    Pump();
                    sb.AppendLine($"[OK]   页面 {item.Title}");
                }
                catch (Exception ex) { errors++; sb.AppendLine($"[FAIL] 页面 {item.Title}: {ex.GetType().Name}: {ex.Message}"); DiskPro.App.App.WriteCrash("smoketest:" + item.Key, ex); }
            }
            try { ThemeManager.Apply(!AppHost.Settings.DarkTheme); Pump(); ThemeManager.Apply(AppHost.Settings.DarkTheme); Pump(); sb.AppendLine("[OK]   主题切换"); }
            catch (Exception ex) { errors++; sb.AppendLine($"[FAIL] 主题切换: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            errors++;
            sb.AppendLine($"[FAIL] MainWindow: {ex}");
            DiskPro.App.App.WriteCrash("smoketest", ex);
        }

        List<string> bindingErrors; lock (listener.Errors) bindingErrors = listener.Errors.Distinct().ToList();
        sb.AppendLine($"绑定错误数: {bindingErrors.Count}");
        foreach (var b in bindingErrors.Take(50)) sb.AppendLine("  [BIND] " + b);
        sb.AppendLine(errors == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({errors})");
        try { Directory.CreateDirectory(Path.GetDirectoryName(OutFile)!); File.WriteAllText(OutFile, sb.ToString(), Encoding.UTF8); } catch { }
        try { w?.Close(); } catch { }
        app.Shutdown(errors == 0 ? 0 : 1);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
