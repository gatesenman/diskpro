using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Models;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class DupGroupVm : ObservableObject
{
    public DuplicateGroup Group { get; }
    public ObservableCollection<FileRowVm> Files { get; } = new();
    public DupGroupVm(DuplicateGroup g, Action changed)
    {
        Group = g;
        foreach (var f in g.Files) Files.Add(new FileRowVm(f) { Changed = changed });
    }
    public string Header => $"{Files.Count} 个相同文件 · 每个 {SizeFormatter.Format(Group.Size)} · 可节省 {SizeFormatter.Format(Group.WastedBytes)}";
    public string HashShort => Group.Hash.Length > 12 ? Group.Hash[..12] : Group.Hash;
    public string FirstName => Files.FirstOrDefault()?.Name ?? "";
}

public sealed class DuplicatesViewModel : PageViewModel
{
    public override string Title => "重复文件查找";
    public override string Subtitle => "大小 → 部分哈希 → 完整 SHA-256 三级比对，系统目录自动保护";
    public ObservableCollection<DupGroupVm> Groups { get; } = new();

    private string _root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Root { get => _root; set => Set(ref _root, value); }
    private string _min = SizeFormatter.Format(AppHost.Settings.DuplicateMinBytes).Replace(" ", "");
    public string MinSizeText { get => _min; set => Set(ref _min, value); }

    private string _summary = ""; public string Summary { get => _summary; set => Set(ref _summary, value); }
    public string SelectedText { get { var s = All.Where(f => f.IsSelected).ToList(); return $"已选 {s.Count} 项 · {SizeFormatter.Format(s.Sum(f => f.Size))}"; } }
    private IEnumerable<FileRowVm> All => Groups.SelectMany(g => g.Files);

    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand BrowseCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(Root); if (f != null) Root = f; });
    public ICommand KeepNewestCommand => new RelayCommand(() => AutoSelect(true));
    public ICommand KeepOldestCommand => new RelayCommand(() => AutoSelect(false));
    public ICommand ClearSelectionCommand => new RelayCommand(() => { foreach (var f in All) f.IsSelected = false; });
    public ICommand OpenCommand => new RelayCommand(p => { if (p is FileRowVm f) Dialogs.OpenLocation(f.Path); });
    public ICommand DeleteCommand => new AsyncCommand(Delete, () => IsIdle && All.Any(f => f.IsSelected));

    private void Changed() => OnPropertyChanged(nameof(SelectedText));

    private void AutoSelect(bool keepNewest)
    {
        foreach (var g in Groups)
        {
            var del = DuplicateFinder.SelectForDeletion(g.Group, keepNewest).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in g.Files) f.IsSelected = del.Contains(f.Path);
        }
        Changed();
    }

    private Task Scan() => RunAsync("正在查找重复文件…", async (ct, progress) =>
    {
        if (!SizeFormatter.TryParse(MinSizeText, out var min)) min = 64 * 1024;
        AppHost.Settings.DuplicateMinBytes = min; AppHost.SaveSettings();
        var groups = await new DuplicateFinder(AppHost.Guard).FindAsync(new DuplicateOptions { Root = Root, MinSizeBytes = min, SkipSystemTrees = true }, progress, ct);
        Groups.ReplaceWith(groups.Take(2000).Select(g => new DupGroupVm(g, Changed)));
        Summary = $"共 {groups.Count} 组重复文件，可节省 {SizeFormatter.Format(groups.Sum(g => g.WastedBytes))}";
        Status = "完成 · " + Summary;
        Changed();
    });

    private async Task Delete()
    {
        // Safety: never allow deleting every copy of a group.
        foreach (var g in Groups)
            if (g.Files.Count > 0 && g.Files.All(f => f.IsSelected))
            { Dialogs.Error($"“{g.FirstName}” 所在组的所有副本都被选中，至少需要保留一份。"); return; }
        var sel = All.Where(f => f.IsSelected).ToList();
        var mode = Dialogs.ConfirmDeletion("确认删除重复文件", sel.Select(f => new PreviewRow(f.Path, f.Size)).ToList());
        if (mode == null) return;
        await RunAsync("正在删除…", async (ct, progress) =>
        {
            var r = await AppHost.Deleter.DeleteAsync(new DeleteRequest(sel.Select(f => f.Path).ToList(), new[] { Root }, mode.Value, AppHost.Settings.DryRun), progress, ct);
            Dialogs.ShowResult(r);
            if (!r.DryRun)
            {
                var gone = r.Items.Where(i => i.Success).Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var g in Groups.ToList())
                {
                    foreach (var f in g.Files.Where(f => gone.Contains(f.Path)).ToList()) g.Files.Remove(f);
                    if (g.Files.Count < 2) Groups.Remove(g);
                }
            }
            Changed();
        });
    }
}
