using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Models;
using DiskPro.Core.Uninstall;
using DiskPro.Core.Util;
using PathUtil = DiskPro.Core.Util.PathUtil;
namespace DiskPro.App.ViewModels;

public sealed class ProgramVm
{
    public InstalledProgram P { get; }
    public ProgramVm(InstalledProgram p) => P = p;
    public string Name => P.DisplayName;
    public string Publisher => P.Publisher;
    public string Version => P.DisplayVersion;
    public long Size => P.EstimatedSizeBytes;
    public string SizeText => P.EstimatedSizeBytes > 0 ? SizeFormatter.Format(P.EstimatedSizeBytes) : "—";
    public DateTime InstallDate => P.InstallDate ?? DateTime.MinValue;
    public string InstallDateText => P.InstallDate?.ToString("yyyy-MM-dd") ?? "—";
    public string Arch => P.Is64Bit ? "64 位" : "32 位";
}

public sealed class UninstallViewModel : PageViewModel
{
    public override string Title => "软件卸载";
    public override string Subtitle => "读取 HKLM / HKCU（含 WOW6432Node）已安装程序，卸载后扫描残留";
    public ObservableCollection<ProgramVm> Programs { get; } = new();
    public ICollectionView View { get; }
    public ObservableCollection<SelectableItem<string>> Leftovers { get; } = new();
    public ObservableCollection<string> RegistryHints { get; } = new();

    private string _search = ""; public string Search { get => _search; set { if (Set(ref _search, value)) View.Refresh(); } }
    private ProgramVm? _selected; public ProgramVm? Selected { get => _selected; set => Set(ref _selected, value); }
    private string _summary = ""; public string Summary { get => _summary; set => Set(ref _summary, value); }
    private bool _hasLeftovers; public bool HasLeftovers { get => _hasLeftovers; set => Set(ref _hasLeftovers, value); }

    public UninstallViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Programs);
        View.Filter = o => o is ProgramVm p && (string.IsNullOrWhiteSpace(Search)
            || p.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) || p.Publisher.Contains(Search, StringComparison.OrdinalIgnoreCase));
    }

    public ICommand LoadCommand => new AsyncCommand(Load, () => IsIdle);
    public ICommand UninstallCommand => new AsyncCommand(Uninstall, () => IsIdle && Selected != null);
    public ICommand ScanLeftoversCommand => new AsyncCommand(() => ScanLeftovers(Selected?.P), () => IsIdle && Selected != null);
    public ICommand CleanLeftoversCommand => new AsyncCommand(CleanLeftovers, () => IsIdle && Leftovers.Any(l => l.IsSelected));
    public ICommand SortCommand => new RelayCommand(p =>
    {
        if (p is not string key) return;
        View.SortDescriptions.Clear();
        var dir = key is "Size" or "InstallDate" ? ListSortDirection.Descending : ListSortDirection.Ascending;
        View.SortDescriptions.Add(new SortDescription(key, dir));
    });

    private Task Load() => RunAsync("正在读取已安装程序…", async (ct, _) =>
    {
        var list = await Task.Run(() => new RegistryProgramEnumerator().Enumerate(), ct);
        Programs.ReplaceWith(list.Select(p => new ProgramVm(p)));
        Summary = $"共 {list.Count} 个程序，已知占用 {SizeFormatter.Format(list.Sum(p => p.EstimatedSizeBytes))}";
        Status = Summary;
    });

    private async Task Uninstall()
    {
        var p = Selected?.P; if (p == null) return;
        if (!Dialogs.Confirm($"确定要卸载“{p.DisplayName}”吗？\n\n将启动程序自带的卸载程序：\n{p.UninstallString}", "卸载确认")) return;
        if (AppHost.Settings.DryRun)
        {
            AppHost.Logger.Log(Core.Logging.LogLevel.DryRun, "卸载", $"[预览] 卸载 {p.DisplayName}: {p.UninstallString}");
            Dialogs.Info("预览模式：未启动卸载程序。"); return;
        }
        await RunAsync($"正在卸载 {p.DisplayName}…（请在弹出的卸载程序中完成操作）", async (ct, _) =>
        {
            var code = await Task.Run(() => new UninstallerRunner().Run(p, quiet: false, ct), ct);
            AppHost.Logger.Log(Core.Logging.LogLevel.Info, "卸载", $"{p.DisplayName} 卸载程序退出码 {code}");
            Status = $"卸载程序已退出（{code}），正在扫描残留…";
        });
        await ScanLeftovers(p);
        await Load();
    }

    private Task ScanLeftovers(InstalledProgram? p) => p == null ? Task.CompletedTask : RunAsync("正在扫描残留…", async (ct, _) =>
    {
        var r = await Task.Run(() => new LeftoverScanner().Scan(p, ct), ct);
        // Never offer protected roots or currently-installed other programs' folders
        var installedLocations = Programs.Where(x => x.P != p && !string.IsNullOrEmpty(x.P.InstallLocation)).Select(x => x.P.InstallLocation).ToList();
        var folders = r.Folders.Where(f => !AppHost.Guard.IsProtected(f)
            && !installedLocations.Any(loc => PathUtil.IsUnder(loc, f) || PathUtil.IsUnder(f, loc))).ToList();
        Leftovers.ReplaceWith(folders.Select(f => new SelectableItem<string>(f, false)));
        RegistryHints.ReplaceWith(r.RegistryHints);
        HasLeftovers = Leftovers.Count > 0;
        Status = Leftovers.Count == 0 ? "未发现残留文件夹" : $"发现 {Leftovers.Count} 个可能的残留文件夹（约 {SizeFormatter.Format(r.TotalSize)}），请仔细核对后勾选";
    });

    private async Task CleanLeftovers()
    {
        var sel = Leftovers.Where(l => l.IsSelected).Select(l => l.Item).ToList();
        var rows = sel.Select(s => new PreviewRow(s, Core.Cleaning.SafeDeleter.MeasureQuick(s))).ToList();
        var mode = Dialogs.ConfirmDeletion("确认清理卸载残留", rows);
        if (mode == null) return;
        await RunAsync("正在清理残留…", async (ct, progress) =>
        {
            var scopes = sel.Select(s => Path.GetDirectoryName(s)!).Distinct().ToList();
            var r = await AppHost.Deleter.DeleteAsync(new DeleteRequest(sel, scopes, mode.Value, AppHost.Settings.DryRun), progress, ct);
            Dialogs.ShowResult(r);
            if (!r.DryRun) foreach (var ok in r.Items.Where(i => i.Success)) { var l = Leftovers.FirstOrDefault(x => x.Item == ok.Path); if (l != null) Leftovers.Remove(l); }
        });
    }
}
