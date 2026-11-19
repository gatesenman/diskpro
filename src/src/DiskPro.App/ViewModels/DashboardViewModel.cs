using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Cleaning;
using DiskPro.Core.Models;
using DiskPro.Core.Optimization;
using DiskPro.Core.Reporting;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class DashCatVm : ObservableObject
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public long Size { get; init; }
    public string SizeText => SizeFormatter.Format(Size);
    public string Glyph { get; init; } = "\uE74D";
    public string Target { get; init; } = "junk";
    public JunkCategory? Category { get; init; }
    public bool Cleanable => Category != null;
    public string RiskText { get; init; } = "";
    private bool _sel; public bool IsSelected { get => _sel; set { if (Set(ref _sel, value)) Changed?.Invoke(); } }
    public Action? Changed { get; set; }
}

public enum DashState { Idle, Scanning, Results, Cleaning, Done }

public sealed class DashboardViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    public override string Title => "总览";
    public override string Subtitle => "一键扫描 C 盘可回收空间，安全清理，实时掌握磁盘健康";

    public DashboardViewModel(MainViewModel main)
    {
        _main = main; RefreshDrive(); RefreshStats();
        AppHost.History.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshStats);
    }

    // ---- state ----
    private DashState _state; public DashState State { get => _state; set { Set(ref _state, value); foreach (var n in new[] { nameof(IsIdleState), nameof(IsScanning), nameof(HasResults), nameof(IsCleaning), nameof(IsDone), nameof(ShowCats), nameof(IsWorking) }) OnPropertyChanged(n); } }
    public bool IsIdleState => State == DashState.Idle;
    public bool IsScanning => State == DashState.Scanning;
    public bool HasResults => State == DashState.Results;
    public bool IsCleaning => State == DashState.Cleaning;
    public bool IsDone => State == DashState.Done;
    public bool IsWorking => IsScanning || IsCleaning;
    public bool ShowCats => State is DashState.Results or DashState.Scanning or DashState.Cleaning && Categories.Count > 0;

    // ---- drive ----
    private DriveInfoSnapshot? _drive; public DriveInfoSnapshot? Drive { get => _drive; set { Set(ref _drive, value); foreach (var n in new[] { nameof(UsedText), nameof(FreeText), nameof(TotalText), nameof(UsedPercent), nameof(UsedPercentText), nameof(GaugeBrushKey), nameof(DriveLabel) }) OnPropertyChanged(n); UpdateHealth(); } }
    public string UsedText => SizeFormatter.Format(Drive?.UsedBytes ?? 0);
    public string FreeText => SizeFormatter.Format(Drive?.FreeBytes ?? 0);
    public string TotalText => SizeFormatter.Format(Drive?.TotalBytes ?? 0);
    public double UsedPercent => Drive?.UsedPercent ?? 0;
    public string UsedPercentText => $"{UsedPercent:0}%";
    public string DriveLabel => $"本地磁盘 ({AppHost.SystemDrive.TrimEnd('\\')})" + (string.IsNullOrEmpty(Drive?.Label) ? "" : $" · {Drive!.Label}");
    public string GaugeBrushKey => UsedPercent >= 90 ? "DangerBrush" : UsedPercent >= 75 ? "WarningBrush" : "AccentBrush";

    // ---- health ----
    private int _health; public int HealthScore { get => _health; set { Set(ref _health, value); OnPropertyChanged(nameof(HealthText)); OnPropertyChanged(nameof(HealthBrushKey)); OnPropertyChanged(nameof(HealthHint)); } }
    public string HealthText => HealthScore >= 85 ? "状态良好" : HealthScore >= 60 ? "建议清理" : "急需清理";
    public string HealthBrushKey => HealthScore >= 85 ? "SuccessBrush" : HealthScore >= 60 ? "WarningBrush" : "DangerBrush";
    public string HealthHint => State == DashState.Idle
        ? "建议定期扫描，清除系统垃圾与缓存，让 C 盘保持轻盈。"
        : Reclaimable > 0 ? $"发现 {ReclaimableText} 可安全清理的空间，清理后健康分将提升。" : "C 盘很干净，没有需要清理的内容。";
    private void UpdateHealth() { if (Drive != null) HealthScore = HtmlReport.HealthScore(Drive.TotalBytes, Drive.FreeBytes, _junk?.TotalSize ?? 0); }

    // ---- results ----
    public ObservableCollection<DashCatVm> Categories { get; } = new();
    private JunkScanResult? _junk;
    private long _reclaimable; public long Reclaimable { get => _reclaimable; set { Set(ref _reclaimable, value); OnPropertyChanged(nameof(ReclaimableText)); OnPropertyChanged(nameof(HealthHint)); } }
    public string ReclaimableText => SizeFormatter.Format(Reclaimable);
    private long _found; public string FoundText => SizeFormatter.Format(_found);
    private string _stage = ""; public string Stage { get => _stage; set => Set(ref _stage, value); }
    private string _freed = ""; public string FreedText { get => _freed; set => Set(ref _freed, value); }
    private string _doneDetail = ""; public string DoneDetail { get => _doneDetail; set => Set(ref _doneDetail, value); }

    // ---- stats ----
    private string _totalFreed = "", _runs = "", _month = "";
    public string TotalFreedText { get => _totalFreed; set => Set(ref _totalFreed, value); }
    public string RunsText { get => _runs; set => Set(ref _runs, value); }
    public string MonthText { get => _month; set => Set(ref _month, value); }
    private void RefreshStats()
    {
        TotalFreedText = SizeFormatter.Format(AppHost.History.TotalFreed);
        RunsText = AppHost.History.TotalRuns.ToString("N0");
        MonthText = SizeFormatter.Format(AppHost.History.FreedSince(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)));
    }

    public ICommand SmartScanCommand => new AsyncCommand(SmartScan, () => IsIdle);
    public ICommand CleanCommand => new AsyncCommand(OneClickClean, () => IsIdle && Reclaimable > 0);
    public ICommand BackCommand => new RelayCommand(() => { State = Categories.Count > 0 ? DashState.Idle : DashState.Idle; Categories.Clear(); Reclaimable = 0; });
    public ICommand RefreshCommand => new RelayCommand(RefreshDrive);
    public ICommand GoCommand => new RelayCommand(p => { if (p is string key) _main.Navigate(key); else if (p is DashCatVm c) _main.Navigate(c.Target); });
    public ICommand DetailsCommand => new RelayCommand(() => _main.Navigate("junk"));

    public void RefreshDrive() => Drive = DriveInfoSnapshot.For(AppHost.SystemDrive) ?? DriveInfoSnapshot.DemoC();

    public override Task? ScreenshotPrepare() => SmartScan();

    private static string GlyphFor(string id) => id switch
    {
        "temp" => "\uE7B8", "wu" => "\uE895", "do" => "\uE896", "thumb" => "\uE91B", "wer" => "\uE7BA", "logs" => "\uE8A5",
        "recycle" => "\uE74D", "browser" => "\uE774", "dev" => "\uE943", "syslogs" => "\uE9F9", "defender" => "\uE83D",
        "inetcache" => "\uE12B", "shader" => "\uE7F4", "appcache" => "\uE71D", "fontcache" => "\uE8D2", "msi" => "\uE7B8", _ => "\uE74D"
    };

    private Task SmartScan() => RunAsync("正在智能扫描…", async (ct, progress) =>
    {
        State = DashState.Scanning; Categories.Clear(); Reclaimable = 0; _found = 0;
        var stageProgress = new Progress<ScanProgress>(p => { if (p.Stage != null && p.Stage != "完成") Stage = "正在扫描：" + p.Stage; ((IProgress<ScanProgress>)progress).Report(p); });
        try
        {
            var junk = await new JunkScanner(AppHost.RecycleBin).ScanAsync(stageProgress, ct, AppHost.Settings.IncludeOptionalAppCaches);
            Stage = "正在检查系统文件…";
            SystemOptInfo? opt = null;
            try { opt = await Task.Run(() => { var o = new SystemOptimizer(AppHost.Logger); var i = new SystemOptInfo(); var h = Path.Combine(AppHost.SystemDrive, "hiberfil.sys"); try { if (File.Exists(h)) i.HiberfilBytes = new FileInfo(h).Length; } catch { } var wo = Path.Combine(AppHost.SystemDrive, "Windows.old"); if (Directory.Exists(wo)) { i.WindowsOldExists = true; i.WindowsOldBytes = DirectorySizer.GetSize(wo, ct); } return i; }, ct); } catch { }
            _junk = junk;
            void changed() { Reclaimable = Categories.Where(c => c.IsSelected && c.Cleanable).Sum(c => c.Size); }
            var cats = junk.Categories.Where(c => c.TotalSize > 0 && !c.Id.StartsWith("err:")).OrderByDescending(c => c.TotalSize).Select(c => new DashCatVm
            {
                Name = c.Name, Description = c.Description, Size = c.TotalSize, Glyph = GlyphFor(c.Id), Category = c.ReportOnly ? null : c,
                RiskText = c.ReportOnly ? "仅报告" : c.Risk switch { RiskLevel.Safe => "安全", RiskLevel.Low => "低风险", _ => "需确认" },
                IsSelected = !c.ReportOnly && c.Risk <= RiskLevel.Low && !c.IsOptional && !c.IsPrivacy, Changed = changed
            }).ToList();
            if (opt?.HiberfilBytes > 0) cats.Add(new DashCatVm { Name = "休眠文件 hiberfil.sys", Description = "关闭休眠功能可释放（前往系统深度优化）", Size = opt.HiberfilBytes, Glyph = "\uE708", Target = "optimize", RiskText = "需手动" });
            if (opt?.WindowsOldBytes > 0) cats.Add(new DashCatVm { Name = "Windows.old 旧系统", Description = "系统升级残留（前往系统深度优化）", Size = opt.WindowsOldBytes, Glyph = "\uE7F8", Target = "optimize", RiskText = "需手动" });
            Categories.ReplaceWith(cats);
            _found = cats.Sum(c => c.Size); OnPropertyChanged(nameof(FoundText));
            changed();
            _main.Junk.LoadResult(junk);
            RefreshDrive();
            State = DashState.Results;
            Status = $"扫描完成：发现 {FoundText}，已默认勾选可安全清理的 {ReclaimableText}";
            AppHost.Logger.Log(Core.Logging.LogLevel.Info, "智能扫描", Status);
        }
        catch { State = Categories.Count > 0 ? DashState.Results : DashState.Idle; throw; }
    });

    private async Task OneClickClean()
    {
        if (_junk == null) return;
        var selectedCats = Categories.Where(c => c.IsSelected && c.Category != null).Select(c => c.Category!).ToHashSet();
        if (selectedCats.Count == 0) return;
        var msg = $"将清理 {selectedCats.Count} 类垃圾，约 {ReclaimableText}。\n缓存与临时文件将被直接删除（不进入回收站），正在使用的文件会自动跳过。" + (AppHost.Settings.DryRun ? "\n\n当前为仅预览模式，不会真正删除。" : "");
        if (!Dialogs.Confirm(msg, "一键清理")) return;
        await RunAsync("正在清理…", async (ct, progress) =>
        {
            State = DashState.Cleaning; Stage = "正在清理，请稍候…";
            JunkCleaner.SelectWhere(_junk, c => selectedCats.Contains(c));
            foreach (var c in _junk.Categories.Where(c => selectedCats.Contains(c) && c.Risk >= RiskLevel.Medium)) foreach (var i in c.Items) i.Selected = true;
            var before = DriveInfoSnapshot.For(AppHost.SystemDrive)?.FreeBytes ?? 0;
            var r = await Task.Run(() => AppHost.Cleaner.Clean(_junk, DeleteMode.Permanent, AppHost.Settings.DryRun, progress, ct), ct);
            AppHost.Record("一键清理", r);
            RefreshDrive();
            var after = Drive?.FreeBytes ?? 0;
            FreedText = SizeFormatter.Format(r.FreedBytes);
            DoneDetail = r.DryRun
                ? $"预览模式：将处理 {r.SuccessCount:N0} 项，跳过 {r.SkippedCount:N0} 项。关闭预览模式后才会真正释放空间。"
                : $"成功清理 {r.SuccessCount:N0} 项，跳过 {r.SkippedCount:N0} 项（正在使用或受保护）。C 盘可用空间 {SizeFormatter.Format(before)} → {SizeFormatter.Format(after)}";
            _junk = null; Categories.Clear(); Reclaimable = 0; _main.Junk.Clear();
            UpdateHealth();
            State = DashState.Done;
            Status = r.DryRun ? "预览完成" : $"清理完成，释放 {FreedText}";
        });
        if (State == DashState.Cleaning) State = DashState.Results;
    }
}
