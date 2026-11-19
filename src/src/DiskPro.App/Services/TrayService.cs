using System.Windows;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
using WF = System.Windows.Forms;
namespace DiskPro.App.Services;

/// <summary>System tray icon + low-disk-space reminder.</summary>
public static class TrayService
{
    private static WF.NotifyIcon? _icon;
    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static DateTime _lastAlert = DateTime.MinValue;
    public static bool IsActive => _icon != null;

    public static void Refresh()
    {
        if (AppHost.Settings.EnableTray) Start(); else Stop();
    }

    public static void Start()
    {
        if (_icon != null) return;
        try
        {
            var exe = Environment.ProcessPath;
            var icon = exe != null ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null;
            _icon = new WF.NotifyIcon { Icon = icon ?? System.Drawing.SystemIcons.Application, Text = "DiskPro C盘清理大师", Visible = true };
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("打开主界面", null, (_, _) => ShowMain());
            menu.Items.Add("智能扫描", null, (_, _) => { ShowMain(); (Application.Current.MainWindow as MainWindow)?.Vm.Navigate("dashboard"); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => Exit());
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => ShowMain();
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _timer.Tick += (_, _) => CheckLowDisk();
            _timer.Start();
            var first = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            first.Tick += (_, _) => { first.Stop(); CheckLowDisk(); };
            first.Start();
        }
        catch (Exception ex) { AppHost.Logger.Log(Core.Logging.LogLevel.Warn, "托盘", ex.Message); _icon = null; }
    }

    public static void Stop()
    {
        _timer?.Stop(); _timer = null;
        if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
    }

    public static void Balloon(string title, string text, WF.ToolTipIcon icon = WF.ToolTipIcon.Info)
    {
        try { _icon?.ShowBalloonTip(4000, title, text, icon); } catch { }
    }

    public static void CheckLowDisk()
    {
        if (!AppHost.Settings.LowDiskAlert) return;
        var d = DriveInfoSnapshot.For(AppHost.SystemDrive);
        if (d == null) return;
        if (d.FreeBytes < AppHost.Settings.LowDiskThresholdGb * (1L << 30) && DateTime.Now - _lastAlert > TimeSpan.FromHours(6))
        {
            _lastAlert = DateTime.Now;
            Balloon("C 盘空间不足", $"剩余 {SizeFormatter.Format(d.FreeBytes)}，低于 {AppHost.Settings.LowDiskThresholdGb} GB。点击打开 DiskPro 进行清理。", WF.ToolTipIcon.Warning);
        }
    }

    private static void ShowMain()
    {
        var w = Application.Current?.MainWindow; if (w == null) return;
        w.Show(); if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Activate();
    }

    private static void Exit()
    {
        if (Application.Current?.MainWindow is MainWindow m) m.ForceClose = true;
        Stop();
        Application.Current?.Shutdown();
    }
}
