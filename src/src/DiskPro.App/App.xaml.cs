using System.Diagnostics;
using System.Text;
using System.Windows;
using DiskPro.App.Services;
namespace DiskPro.App;

public partial class App : Application
{
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskPro");
    public static string CrashLog => Path.Combine(DataDir, "logs", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        InstallCrashHandlers();
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToList();

        if (args.Contains("--selftest"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code = SelfTest.Run();
            Shutdown(code);
            return;
        }

        try { ThemeManager.Apply(AppHost.Settings.DarkTheme); }
        catch (Exception ex) { WriteCrash("主题加载", ex); }

        try { AppHost.Logger.Log(Core.Logging.LogLevel.Info, "应用", $"DiskPro 启动 · 管理员={AppHost.IsAdmin()} · 预览模式={AppHost.Settings.DryRun}"); } catch { }

        if (args.Contains("--autoclean"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var r = ViewModels.AutoClean.Run();
                AppHost.Record("自动清理(计划任务)", r);
                Shutdown(0);
            }
            catch (Exception ex) { WriteCrash("autoclean", ex); Shutdown(1); }
            return;
        }

        int si = args.IndexOf("--screenshots");
        if (si >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var dir = si + 1 < e.Args.Length ? e.Args[si + 1] : Path.Combine(DataDir, "screenshots");
            ScreenshotRunner.Run(this, dir, args.Contains("--scan"));
            return;
        }

        if (args.Contains("--smoketest"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SmokeTest.Run(this);
            return;
        }

        try
        {
            var w = new MainWindow();
            MainWindow = w;
            w.Show();
            if (AppHost.Settings.EnableTray) TrayService.Start();
        }
        catch (Exception ex)
        {
            WriteCrash("主窗口创建", ex);
            ShowCrash(ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { TrayService.Stop(); } catch { }
        base.OnExit(e);
    }

    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, a) =>
        {
            WriteCrash("UI 线程", a.Exception);
            ShowCrash(a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
        {
            if (a.ExceptionObject is Exception ex)
            {
                WriteCrash("AppDomain" + (a.IsTerminating ? "（致命）" : ""), ex);
                if (a.IsTerminating) ShowCrash(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            WriteCrash("后台任务", a.Exception);
            a.SetObserved();
        };
    }

    public static void WriteCrash(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
            var sb = new StringBuilder();
            sb.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] DiskPro {typeof(App).Assembly.GetName().Version} / {Environment.OSVersion} =====");
            sb.AppendLine(ex.ToString());
            File.AppendAllText(CrashLog, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    private static int _shown;
    public static void ShowCrash(Exception ex)
    {
        if (Interlocked.Exchange(ref _shown, 1) == 1) return; // avoid dialog storms
        try
        {
            System.Windows.MessageBox.Show($"很抱歉，DiskPro 遇到了一个错误：\n\n{ex.Message}\n\n详细信息已写入：\n{CrashLog}\n\n程序将尽量继续运行；如问题持续，请将该日志反馈给开发者。",
                "DiskPro C盘清理大师 - 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        finally { Interlocked.Exchange(ref _shown, 0); }
    }
}

