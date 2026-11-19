using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Cleaning;
using DiskPro.Core.Logging;
using DiskPro.Core.Maintenance;
using DiskPro.Core.Models;
using DiskPro.Core.Reporting;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

// ======================= 隐私清理 =======================
public sealed class PrivacyViewModel : PageViewModel
{
    public override string Title => "隐私清理";
    public override string Subtitle => "清除浏览历史、Cookie、表单记录与最近使用痕迹 · 已保存的密码与书签永不触碰";
    public ObservableCollection<JunkCategoryVm> Categories { get; } = new();
    private JunkScanResult? _result;
    private long _total; public long Total { get => _total; set { Set(ref _total, value); OnPropertyChanged(nameof(TotalText)); } }
    public string TotalText => SizeFormatter.Format(Total);
    private long _sel; public long SelectedTotal { get => _sel; set { Set(ref _sel, value); OnPropertyChanged(nameof(SelectedText)); } }
    public string SelectedText => SizeFormatter.Format(SelectedTotal);
    private int _count; public int ItemCount { get => _count; set => Set(ref _count, value); }

    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand CleanCommand => new AsyncCommand(Clean, () => IsIdle && Categories.Any(c => c.Items.Any(i => i.IsSelected)));
    public ICommand OpenCommand => new RelayCommand(p => { if (p is JunkItemVm i) Dialogs.OpenLocation(i.Path); });

    private Task Scan() => RunAsync("正在扫描隐私痕迹…", async (ct, _) =>
    {
        var r = await Task.Run(() =>
        {
            var res = new PrivacyScanner().Scan(ct);
            // also offer "recent documents / jump lists / clipboard" from the junk scanner's privacy category
            var recent = new JunkScanner(AppHost.RecycleBin).Scan(null, ct, false, includePrivacyOptional: true).Categories.FirstOrDefault(c => c.Id == "recent");
            if (recent != null) res.Categories.Add(recent);
            return res;
        }, ct);
        foreach (var c in r.Categories) foreach (var i in c.Items) i.Selected = false; // privacy = explicit opt-in
        _result = r;
        Categories.ReplaceWith(r.Categories.Select(c => new JunkCategoryVm(c, Recalc)));
        Recalc();
        Status = ItemCount == 0 ? "未发现隐私痕迹" : $"发现 {ItemCount} 项隐私痕迹（{TotalText}），请勾选要清除的项目";
    });

    private void Recalc()
    {
        Total = Categories.Sum(c => c.Model.TotalSize);
        SelectedTotal = Categories.Sum(c => c.SelectedSize);
        ItemCount = Categories.Sum(c => c.Items.Count);
    }

    private async Task Clean()
    {
        if (_result == null) return;
        var rows = Categories.SelectMany(c => c.Items.Where(i => i.IsSelected).Select(i => new PreviewRow(i.Path, i.Model.Size, c.Name))).ToList();
        if (rows.Count == 0) return;
        if (!Dialogs.Confirm("清除 Cookie 后网站需要重新登录；清除历史后地址栏联想将清空。\n请先关闭 Chrome / Edge / Firefox，否则被占用的文件会被跳过。\n\n确定继续吗？", "清除隐私痕迹", danger: true)) return;
        var mode = Dialogs.ConfirmDeletion("确认清除隐私痕迹", rows, allowRecycle: false);
        if (mode == null) return;
        await RunAsync("正在清除…", async (ct, progress) =>
        {
            var r = await Task.Run(() => AppHost.Cleaner.Clean(_result!, DeleteMode.Permanent, AppHost.Settings.DryRun, progress, ct), ct);
            Dialogs.ShowResult(r, "隐私清理");
            Status = r.DryRun ? "预览完成" : $"已清除 {r.SuccessCount} 项";
        });
        if (!AppHost.Settings.DryRun) await Scan();
    }
}

// ======================= 聊天软件专清 =======================
public sealed class ChatAppCardVm
{
    public string App { get; init; } = "";
    public string Root { get; init; } = "";
    public long Total { get; init; }
    public string TotalText => SizeFormatter.Format(Total);
    public string Breakdown { get; init; } = "";
    public string Glyph => App.StartsWith("微信") ? "\uE8BD" : App == "QQ" ? "\uE8F2" : "\uE716";
}

public sealed class ChatFileRowVm : ObservableObject
{
    public ChatFile Model { get; }
    public ChatFileRowVm(ChatFile m) => Model = m;
    public string App => Model.App;
    public string Account => Model.Account;
    public string KindText => ChatAppViewModel.KindName(Model.Kind);
    public string Path => Model.Entry.Path;
    public string Name => Model.Entry.Name;
    public long Size => Model.Entry.Size;
    public string SizeText => SizeFormatter.Format(Size);
    public DateTime Modified => Model.Entry.LastWriteUtc.ToLocalTime();
    public string ModifiedText => Modified.ToString("yyyy-MM-dd");
    private bool _sel; public bool IsSelected { get => _sel; set { if (Set(ref _sel, value)) Changed?.Invoke(); } }
    public Action? Changed { get; set; }
}

public sealed class ChatAppViewModel : PageViewModel
{
    public override string Title => "聊天软件专清";
    public override string Subtitle => "分析微信 / QQ / 钉钉 的图片、视频、文件与缓存占用 · 聊天记录数据库永不删除";
    public ObservableCollection<ChatAppCardVm> Apps { get; } = new();
    public ObservableCollection<ChatFileRowVm> Files { get; } = new();
    private ChatAppResult? _result;
    private List<ChatFileRowVm> _all = new();

    public static string KindName(ChatFileKind k) => k switch { ChatFileKind.Image => "图片", ChatFileKind.Video => "视频", ChatFileKind.File => "文件", ChatFileKind.Cache => "缓存", _ => "其他" };
    public IReadOnlyList<string> KindFilters { get; } = new[] { "全部类型", "图片", "视频", "文件", "缓存" };
    private int _kind; public int KindIndex { get => _kind; set { if (Set(ref _kind, value)) ApplyFilter(); } }
    public IReadOnlyList<string> AgeFilters { get; } = new[] { "任意时间", "30 天前", "90 天前", "180 天前", "1 年前" };
    private int _age = 2; public int AgeIndex { get => _age; set { if (Set(ref _age, value)) ApplyFilter(); } }
    private string _summary = ""; public string FilterSummary { get => _summary; set => Set(ref _summary, value); }
    public string SelectedText => $"已选 {_all.Count(f => f.IsSelected):N0} 项 · {SizeFormatter.Format(_all.Where(f => f.IsSelected).Sum(f => f.Size))}";
    private long _grand; public string GrandTotalText => SizeFormatter.Format(_grand);

    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand SelectFilteredCommand => new RelayCommand(() => { foreach (var f in Files) f.IsSelected = true; });
    public ICommand SelectNoneCommand => new RelayCommand(() => { foreach (var f in _all) f.IsSelected = false; });
    public ICommand OpenCommand => new RelayCommand(p => { if (p is ChatFileRowVm f) Dialogs.OpenLocation(f.Path); else if (p is ChatAppCardVm a) Dialogs.OpenLocation(a.Root); });
    public ICommand DeleteCommand => new AsyncCommand(Delete, () => IsIdle && _all.Any(f => f.IsSelected));

    private Task Scan() => RunAsync("正在分析聊天软件文件…", async (ct, progress) =>
    {
        var r = await Task.Run(() => new ChatAppAnalyzer().Analyze(progress, ct), ct);
        _result = r;
        Apps.ReplaceWith(r.Apps.OrderByDescending(a => a.Total).Select(a => new ChatAppCardVm
        {
            App = a.App, Root = a.Root, Total = a.Total,
            Breakdown = string.Join("  ·  ", a.SizeByKind.OrderByDescending(k => k.Value).Select(k => $"{KindName(k.Key)} {SizeFormatter.Format(k.Value)}"))
        }));
        _grand = r.Apps.Sum(a => a.Total); OnPropertyChanged(nameof(GrandTotalText));
        _all = r.Files.OrderByDescending(f => f.Entry.Size).Select(f => new ChatFileRowVm(f) { Changed = () => OnPropertyChanged(nameof(SelectedText)) }).ToList();
        ApplyFilter();
        Status = r.Apps.Count == 0 ? "未检测到微信 / QQ / 钉钉的本地文件目录" : $"共 {r.Files.Count:N0} 个文件，{GrandTotalText}";
    });

    private void ApplyFilter()
    {
        var days = AgeIndex switch { 1 => 30, 2 => 90, 3 => 180, 4 => 365, _ => 0 };
        var cutoff = DateTime.Now.AddDays(-days);
        ChatFileKind? kind = KindIndex switch { 1 => ChatFileKind.Image, 2 => ChatFileKind.Video, 3 => ChatFileKind.File, 4 => ChatFileKind.Cache, _ => null };
        var list = _all.Where(f => (kind == null || f.Model.Kind == kind) && (days == 0 || f.Modified < cutoff)).ToList();
        Files.ReplaceWith(list.Take(3000));
        FilterSummary = $"筛选结果 {list.Count:N0} 项 · {SizeFormatter.Format(list.Sum(f => f.Size))}" + (list.Count > 3000 ? "（列表显示最大的 3000 项，“全选筛选结果”将选中全部）" : "");
        _filtered = list;
        OnPropertyChanged(nameof(SelectedText));
    }
    private List<ChatFileRowVm> _filtered = new();
    public ICommand SelectAllFilteredCommand => new RelayCommand(() => { foreach (var f in _filtered) f.IsSelected = true; OnPropertyChanged(nameof(SelectedText)); });

    private async Task Delete()
    {
        if (_result == null) return;
        var sel = _all.Where(f => f.IsSelected).ToList();
        if (sel.Count == 0) return;
        var mode = Dialogs.ConfirmDeletion("确认删除聊天文件", sel.Select(f => new PreviewRow(f.Path, f.Size, $"{f.App} · {f.KindText}")).ToList());
        if (mode == null) return;
        await RunAsync("正在删除…", async (ct, progress) =>
        {
            var r = await AppHost.Deleter.DeleteAsync(new DeleteRequest(sel.Select(f => f.Path).ToList(), _result!.Roots.ToList(), mode.Value, AppHost.Settings.DryRun), progress, ct);
            Dialogs.ShowResult(r, "聊天软件专清");
        });
        if (!AppHost.Settings.DryRun) await Scan();
    }
}

// ======================= 空文件夹 / 无效快捷方式 =======================
public sealed class PathRowVm : ObservableObject
{
    public string Path { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Kind { get; init; } = "";
    private bool _sel = true; public bool IsSelected { get => _sel; set { if (Set(ref _sel, value)) Changed?.Invoke(); } }
    public Action? Changed { get; set; }
}

public sealed class EmptyFoldersViewModel : PageViewModel
{
    public override string Title => "空文件夹 / 无效快捷方式";
    public override string Subtitle => "找出残留的空目录，以及桌面和开始菜单中目标已不存在的快捷方式";
    public ObservableCollection<PathRowVm> Folders { get; } = new();
    public ObservableCollection<PathRowVm> Shortcuts { get; } = new();
    private string _root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Root { get => _root; set => Set(ref _root, value); }
    public string SelectedText => $"已选 空文件夹 {Folders.Count(f => f.IsSelected)} 个 · 快捷方式 {Shortcuts.Count(f => f.IsSelected)} 个";

    public ICommand BrowseCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(Root); if (f != null) Root = f; });
    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand OpenCommand => new RelayCommand(p => { if (p is PathRowVm r) Dialogs.OpenLocation(r.Kind == "lnk" ? r.Path : System.IO.Path.GetDirectoryName(r.Path) ?? r.Path); });
    public ICommand DeleteCommand => new AsyncCommand(Delete, () => IsIdle && (Folders.Any(f => f.IsSelected) || Shortcuts.Any(f => f.IsSelected)));

    private Task Scan() => RunAsync("正在查找空文件夹与无效快捷方式…", async (ct, progress) =>
    {
        var root = Root;
        var (dirs, lnks) = await Task.Run(() =>
        {
            var d = new EmptyFolderScanner(AppHost.Guard).Scan(root, ct, progress);
            var l = new ShortcutScanner().Scan(ShortcutScanner.DefaultFolders(), null, ct);
            return (d, l);
        }, ct);
        void ch() => OnPropertyChanged(nameof(SelectedText));
        Folders.ReplaceWith(dirs.Select(d => new PathRowVm { Path = d, Kind = "dir", Detail = "空文件夹", Changed = ch }));
        Shortcuts.ReplaceWith(lnks.Select(s => new PathRowVm { Path = s.Path, Kind = "lnk", Detail = s.Target, Changed = ch }));
        ch();
        Status = $"发现 {Folders.Count} 个空文件夹、{Shortcuts.Count} 个无效快捷方式";
    });

    private async Task Delete()
    {
        var dirs = Folders.Where(f => f.IsSelected).Select(f => f.Path).ToList();
        var lnks = Shortcuts.Where(f => f.IsSelected).Select(f => f.Path).ToList();
        var rows = dirs.Select(d => new PreviewRow(d, 0, "空文件夹")).Concat(lnks.Select(l => new PreviewRow(l, 0, "无效快捷方式"))).ToList();
        var mode = Dialogs.ConfirmDeletion("确认删除", rows);
        if (mode == null) return;
        await RunAsync("正在删除…", async (ct, progress) =>
        {
            var scopes = new List<string> { Root };
            scopes.AddRange(ShortcutScanner.DefaultFolders().Where(s => !string.IsNullOrEmpty(s)));
            var r = await AppHost.Deleter.DeleteAsync(new DeleteRequest(dirs.Concat(lnks).ToList(), scopes, mode.Value, AppHost.Settings.DryRun), progress, ct);
            Dialogs.ShowResult(r, "空文件夹/快捷方式");
        });
        if (!AppHost.Settings.DryRun) await Scan();
    }
}

// ======================= 系统还原点 =======================
public sealed class RestorePointsViewModel : PageViewModel
{
    public override string Title => "系统还原点";
    public override string Subtitle => "查看、创建和删除系统还原点；在进行有风险的系统优化前建议先创建一个";
    public ObservableCollection<RestorePoint> Points { get; } = new();
    private RestorePoint? _sel; public RestorePoint? SelectedPoint { get => _sel; set => Set(ref _sel, value); }
    private string _desc = $"DiskPro 手动还原点 {DateTime.Now:yyyy-MM-dd}"; public string Description { get => _desc; set => Set(ref _desc, value); }
    private string _storage = "—"; public string StorageText { get => _storage; set => Set(ref _storage, value); }
    private readonly RestorePointManager _mgr = new(AppHost.Logger);

    public ICommand RefreshCommand => new AsyncCommand(Refresh, () => IsIdle);
    public ICommand CreateCommand => new AsyncCommand(Create, () => IsIdle && AppHost.IsAdmin());
    public ICommand DeleteCommand => new AsyncCommand(Delete, () => IsIdle && SelectedPoint != null && AppHost.IsAdmin());
    public ICommand OpenSysPropCommand => new RelayCommand(() => Dialogs.OpenUrl("SystemPropertiesProtection.exe"));

    public override void OnNavigatedTo() { if (!HasRun && IsIdle) _ = Refresh(); }

    private Task Refresh() => RunAsync("正在读取还原点…", async (ct, _) =>
    {
        var (list, storage) = await Task.Run(() =>
        {
            var l = _mgr.List();
            var (code, output) = Core.Optimization.SystemOptimizer.RunCapture("vssadmin", "list shadowstorage", 8000);
            var note = code == 0 ? Core.Optimization.SystemOptimizer.ParseShadowStorage(output, AppHost.SystemDrive).Note : "无法读取（需要管理员权限）";
            return (l, note);
        }, ct);
        Points.ReplaceWith(list.OrderByDescending(p => p.Created));
        StorageText = storage;
        Status = Points.Count == 0 ? "当前没有系统还原点（系统保护可能未开启）" : $"共 {Points.Count} 个还原点";
    });

    private async Task Create()
    {
        if (!Dialogs.Confirm($"将为系统盘创建还原点：\n「{Description}」\n\n此过程可能需要 1-2 分钟。Windows 默认 24 小时内只允许创建一个还原点。", "创建还原点")) return;
        await RunAsync("正在创建还原点（可能需要 1-2 分钟）…", async (ct, _) =>
        {
            var (ok, msg) = await Task.Run(() => _mgr.Create(Description, AppHost.Settings.DryRun), ct);
            if (ok) Dialogs.Toast(msg); else Dialogs.Error(msg, "创建失败");
        });
        await Refresh();
    }

    private async Task Delete()
    {
        var p = SelectedPoint; if (p == null) return;
        if (!Dialogs.Confirm($"确定删除还原点 #{p.SequenceNumber}「{p.Description}」？\n删除后将无法恢复到该时间点。", "删除还原点", danger: true)) return;
        await RunAsync("正在删除还原点…", async (ct, _) =>
        {
            var (ok, msg) = await Task.Run(() => _mgr.Delete(p.SequenceNumber, AppHost.Settings.DryRun), ct);
            if (ok) Dialogs.Toast(msg); else Dialogs.Error(msg, "删除失败");
        });
        await Refresh();
    }
}

// ======================= 系统盘搬家 (pagefile / TEMP) =======================
public sealed class SystemRelocationViewModel : PageViewModel
{
    public override string Title => "系统盘搬家";
    public override string Subtitle => "将虚拟内存（pagefile.sys）与临时文件夹（TEMP）迁移到其他磁盘，为 C 盘腾出空间";
    private readonly SystemRelocation _rel = new(AppHost.Logger);
    public ObservableCollection<string> Drives { get; } = new();
    private string? _drive; public string? TargetDrive { get => _drive; set { Set(ref _drive, value); OnPropertyChanged(nameof(TempTargetHint)); } }
    private string _paging = ""; public string PagingText { get => _paging; set => Set(ref _paging, value); }
    private string _pageSize = ""; public string PageSizeText { get => _pageSize; set => Set(ref _pageSize, value); }
    private string _temp = ""; public string TempText { get => _temp; set => Set(ref _temp, value); }
    private string _tempSize = ""; public string TempSizeText { get => _tempSize; set => Set(ref _tempSize, value); }
    public int InitialMb { get; set; } = 4096;
    public int MaxMb { get; set; } = 16384;
    public bool KeepSmallOnSystem { get; set; } = true;
    private string _tempTarget = ""; public string TempTarget { get => _tempTarget; set => Set(ref _tempTarget, value); }
    public string TempTargetHint => TargetDrive == null ? "" : System.IO.Path.Combine(TargetDrive, "Temp");
    public bool HasOtherDrive => Drives.Count > 0;

    public ICommand RefreshCommand => new RelayCommand(Refresh);
    public ICommand MovePagefileCommand => new AsyncCommand(MovePagefile, () => IsIdle && TargetDrive != null);
    public ICommand RestorePagefileCommand => new AsyncCommand(() => Apply("恢复为系统自动管理", () => _rel.RestorePagefile(AppHost.Settings.DryRun), true), () => IsIdle);
    public ICommand BrowseTempCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(TempTarget); if (f != null) TempTarget = f; });
    public ICommand MoveTempCommand => new AsyncCommand(MoveTemp, () => IsIdle);
    public ICommand RestoreTempCommand => new AsyncCommand(() => Apply("恢复默认 TEMP", () => _rel.RestoreUserTemp(AppHost.Settings.DryRun), false), () => IsIdle);

    public override void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        try
        {
            var sys = AppHost.SystemDrive;
            Drives.ReplaceWith(DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.Equals(sys, StringComparison.OrdinalIgnoreCase); } catch { return false; } })
                .Select(d => d.Name));
            TargetDrive ??= Drives.FirstOrDefault();
            OnPropertyChanged(nameof(HasOtherDrive));
            var pf = _rel.CurrentPagingFiles();
            PagingText = pf.Length == 0 ? "未配置（无页面文件）" : string.Join("；", pf.Select(p => p.StartsWith("?:") ? "所有驱动器自动管理 " + p : p));
            var sysPf = Path.Combine(sys, "pagefile.sys");
            long sz = 0; try { if (File.Exists(sysPf)) sz = new FileInfo(sysPf).Length; } catch { }
            PageSizeText = sz > 0 ? $"C 盘 pagefile.sys 当前占用 {SizeFormatter.Format(sz)}" : "C 盘上没有 pagefile.sys";
            TempText = _rel.CurrentUserTemp();
            if (string.IsNullOrEmpty(TempTarget)) TempTarget = TempTargetHint;
            Task.Run(() => SizeFormatter.Format(DirectorySizer.GetSize(TempText, CancellationToken.None))).ContinueWith(t => { if (t.IsCompletedSuccessfully) TempSizeText = "当前占用 " + t.Result; }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex) { ErrorText = ex.Message; }
    }

    private Task MovePagefile()
    {
        var target = TargetDrive!;
        var msg = $"将虚拟内存迁移到 {target}（初始 {InitialMb} MB，最大 {MaxMb} MB）" + (KeepSmallOnSystem ? "，并在 C 盘保留一个 16-1024MB 的小页面文件用于蓝屏转储" : "") + "。\n\n修改将在重启后生效。建议先创建系统还原点。";
        if (!Dialogs.Confirm(msg, "迁移虚拟内存", danger: true)) return Task.CompletedTask;
        return Apply("迁移虚拟内存", () => _rel.MovePagefile(target, InitialMb, MaxMb, KeepSmallOnSystem, AppHost.Settings.DryRun), true);
    }

    private Task MoveTemp()
    {
        var target = string.IsNullOrWhiteSpace(TempTarget) ? TempTargetHint : TempTarget;
        if (string.IsNullOrWhiteSpace(target)) { Dialogs.Error("请选择新的 TEMP 文件夹位置"); return Task.CompletedTask; }
        if (!Dialogs.Confirm($"将当前用户的 TEMP / TMP 环境变量指向：\n{target}\n\n重新登录或重启应用后生效，旧临时文件可用“系统垃圾清理”清除。", "迁移临时文件夹")) return Task.CompletedTask;
        return Apply("迁移临时文件夹", () => _rel.MoveUserTemp(target, AppHost.Settings.DryRun), false);
    }

    private Task Apply(string name, Func<(bool Ok, string Message)> action, bool reboot) => RunAsync(name + "…", async (ct, _) =>
    {
        var (ok, msg) = await Task.Run(action, ct);
        if (ok) Dialogs.Success(msg + (reboot && !AppHost.Settings.DryRun ? "\n\n需要重启电脑后生效。" : ""), name);
        else Dialogs.Error(msg, name + "失败");
        Refresh();
    });
}

// ======================= 自动清理与提醒 =======================
public sealed class AutoCleanViewModel : PageViewModel
{
    public override string Title => "自动清理与提醒";
    public override string Subtitle => "定时在后台自动清理安全的垃圾，并在 C 盘空间不足时提醒你";
    private readonly AutoCleanScheduler _sch = new(AppHost.Logger);
    public IReadOnlyList<string> Frequencies { get; } = new[] { "每天", "每周", "每月" };
    private int _freq = AppHost.Settings.AutoCleanFrequency switch { "Daily" => 0, "Monthly" => 2, _ => 1 };
    public int FrequencyIndex { get => _freq; set => Set(ref _freq, value); }
    private string _time = AppHost.Settings.AutoCleanTime; public string Time { get => _time; set => Set(ref _time, value); }
    private bool _exists; public bool TaskExists { get => _exists; set { Set(ref _exists, value); OnPropertyChanged(nameof(TaskStateText)); } }
    public string TaskStateText => TaskExists ? "已启用：DiskPro 将按计划自动清理" : "未启用";

    public bool EnableTray { get => AppHost.Settings.EnableTray; set { AppHost.Settings.EnableTray = value; AppHost.SaveSettings(); OnPropertyChanged(); TrayService.Refresh(); } }
    public bool MinimizeToTray { get => AppHost.Settings.MinimizeToTray; set { AppHost.Settings.MinimizeToTray = value; AppHost.SaveSettings(); OnPropertyChanged(); } }
    public bool LowDiskAlert { get => AppHost.Settings.LowDiskAlert; set { AppHost.Settings.LowDiskAlert = value; AppHost.SaveSettings(); OnPropertyChanged(); } }
    public int LowDiskThresholdGb { get => AppHost.Settings.LowDiskThresholdGb; set { AppHost.Settings.LowDiskThresholdGb = Math.Clamp(value, 1, 500); AppHost.SaveSettings(); OnPropertyChanged(); } }

    public ICommand EnableCommand => new AsyncCommand(Enable, () => IsIdle);
    public ICommand DisableCommand => new AsyncCommand(Disable, () => IsIdle && TaskExists);
    public ICommand RunNowCommand => new AsyncCommand(RunNow, () => IsIdle);

    public override void OnNavigatedTo() { try { TaskExists = _sch.Exists(); } catch { } }

    private static ScheduleFrequency Freq(int i) => i switch { 0 => ScheduleFrequency.Daily, 2 => ScheduleFrequency.Monthly, _ => ScheduleFrequency.Weekly };

    private Task Enable() => RunAsync("正在创建计划任务…", async (ct, _) =>
    {
        if (!TimeSpan.TryParse(Time, out var at)) { Dialogs.Error("时间格式应为 HH:mm，例如 12:30"); return; }
        AppHost.Settings.AutoCleanFrequency = Freq(FrequencyIndex).ToString(); AppHost.Settings.AutoCleanTime = Time; AppHost.SaveSettings();
        var exe = Environment.ProcessPath ?? "DiskPro.exe";
        var (ok, msg) = await Task.Run(() => _sch.Create(exe, Freq(FrequencyIndex), at), ct);
        TaskExists = _sch.Exists();
        if (ok) Dialogs.Toast($"已启用自动清理：{Frequencies[FrequencyIndex]} {Time}"); else Dialogs.Error(msg, "创建计划任务失败");
    });

    private Task Disable() => RunAsync("正在移除计划任务…", async (ct, _) =>
    {
        var (ok, msg) = await Task.Run(() => _sch.Remove(), ct);
        TaskExists = _sch.Exists();
        if (ok) Dialogs.Toast("已关闭自动清理"); else Dialogs.Error(msg);
    });

    private Task RunNow() => RunAsync("正在执行一次自动清理（仅安全项目）…", async (ct, progress) =>
    {
        var r = await Task.Run(() => AutoClean.Run(progress, ct), ct);
        Dialogs.ShowResult(r, "自动清理(手动触发)");
    });
}

/// <summary>Headless quick clean used by the scheduled task (--autoclean) and “立即执行”.</summary>
public static class AutoClean
{
    public static DeleteResult Run(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var scan = new JunkScanner(AppHost.RecycleBin).Scan(progress, ct);
        JunkCleaner.SelectWhere(scan, c => c.QuickClean);
        var r = AppHost.Cleaner.Clean(scan, DeleteMode.Permanent, AppHost.Settings.DryRun, progress, ct);
        AppHost.Logger.Log(LogLevel.Info, "自动清理", $"释放 {SizeFormatter.Format(r.FreedBytes)}，成功 {r.SuccessCount}，跳过 {r.SkippedCount}");
        return r;
    }
}

// ======================= 报告与历史 =======================
public sealed class ReportViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    public ReportViewModel(MainViewModel main) { _main = main; AppHost.History.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Reload); Reload(); }
    public override string Title => "报告与历史";
    public override string Subtitle => "累计清理成果、每次清理记录，并可导出完整的 HTML 磁盘健康报告";
    public ObservableCollection<CleanRecordVm> Records { get; } = new();
    private string _total = "", _runs = "", _month = ""; 
    public string TotalFreedText { get => _total; set => Set(ref _total, value); }
    public string RunsText { get => _runs; set => Set(ref _runs, value); }
    public string MonthText { get => _month; set => Set(ref _month, value); }

    public ICommand ExportCommand => new AsyncCommand(Export, () => IsIdle);
    public override void OnNavigatedTo() => Reload();

    private void Reload()
    {
        var h = AppHost.History;
        TotalFreedText = SizeFormatter.Format(h.TotalFreed);
        RunsText = h.TotalRuns.ToString("N0");
        MonthText = SizeFormatter.Format(h.FreedSince(new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)));
        Records.ReplaceWith(h.Records.AsEnumerable().Reverse().Take(500).Select(r => new CleanRecordVm(r)));
    }

    private Task Export() => RunAsync("正在生成报告…", async (ct, _) =>
    {
        var file = Dialogs.SaveFile($"DiskPro-报告-{DateTime.Now:yyyyMMdd-HHmm}.html", "HTML 报告|*.html");
        if (file == null) return;
        var drive = DriveInfoSnapshot.For(AppHost.SystemDrive);
        var junk = _main.Junk.LastResult;
        var large = _main.Page<LargeFilesViewModel>()?.Files.Select(f => f.Entry).ToList();
        var html = await Task.Run(() => HtmlReport.Build(new ReportInput
        {
            Drive = drive, Junk = junk, LargeFiles = large, History = AppHost.History.Records,
            HealthScore = drive == null ? 0 : HtmlReport.HealthScore(drive.TotalBytes, drive.FreeBytes, junk?.TotalSize ?? 0)
        }), ct);
        await File.WriteAllTextAsync(file, html, System.Text.Encoding.UTF8, ct);
        Dialogs.Toast("报告已导出：" + Path.GetFileName(file));
        Dialogs.OpenUrl(file);
    });
}

public sealed class CleanRecordVm
{
    public CleanRecord R { get; }
    public CleanRecordVm(CleanRecord r) => R = r;
    public string Time => R.Time.ToString("yyyy-MM-dd HH:mm");
    public string Source => R.Source;
    public string Freed => SizeFormatter.Format(R.FreedBytes);
    public string Items => $"{R.Items:N0} 项" + (R.Skipped > 0 ? $"（跳过 {R.Skipped:N0}）" : "");
    public string Mode => R.DryRun ? "预览" : "实际清理";
}
