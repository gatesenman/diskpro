using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class FolderNodeVm : ObservableObject
{
    public FolderNode Node { get; }
    public FolderNodeVm? Parent { get; }
    public FolderNodeVm(FolderNode n, FolderNodeVm? parent, long rootSize)
    {
        Node = n; Parent = parent; RootSize = rootSize;
        if (n.Children.Count > 0) Children.Add(Placeholder);
    }
    private static readonly FolderNodeVm Placeholder = new(new FolderNode { Name = "…" }, null, 1);
    public long RootSize { get; }
    public ObservableCollection<FolderNodeVm> Children { get; } = new();
    public string Name => Node.Name;
    public string Path => Node.Path;
    public long Size => Node.Size;
    public string SizeText => SizeFormatter.Format(Node.Size);
    public double Percent => Parent == null ? 100 : Parent.Size <= 0 ? 0 : 100.0 * Node.Size / Parent.Size;
    public string PercentText => $"{Percent:0.0}%";
    public string FilesText => $"{Node.FileCount:N0} 个文件";
    private bool _expanded;
    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (Set(ref _expanded, value) && value) EnsureChildren();
        }
    }
    public void EnsureChildren()
    {
        if (Children.Count == 1 && Children[0] == Placeholder)
        {
            Children.Clear();
            foreach (var c in Node.Children) Children.Add(new FolderNodeVm(c, this, RootSize));
        }
    }
    private bool _selected; public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

public sealed class SpaceViewModel : PageViewModel
{
    public override string Title => "空间分析";
    public override string Subtitle => "按文件夹统计占用，支持逐级下钻与矩形树图";
    public ObservableCollection<FolderNodeVm> Roots { get; } = new();

    private string _root = AppHost.SystemDrive; public string Root { get => _root; set => Set(ref _root, value); }
    private int _depth = 4; public int Depth { get => _depth; set => Set(ref _depth, value); }

    private FolderNodeVm? _current; public FolderNodeVm? Current { get => _current; set { value?.EnsureChildren(); Set(ref _current, value); OnPropertyChanged(nameof(Breadcrumb)); } }
    public string Breadcrumb => Current?.Path ?? "";

    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand BrowseCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(Root); if (f != null) Root = f; });
    public ICommand OpenCommand => new RelayCommand(p => { if (p is FolderNodeVm n) Dialogs.OpenLocation(n.Path); else if (Current != null) Dialogs.OpenLocation(Current.Path); });
    public ICommand UpCommand => new RelayCommand(() => { if (Current?.Parent != null) Current = Current.Parent; }, () => Current?.Parent != null);
    public ICommand DrillCommand => new RelayCommand(p =>
    {
        if (p is FolderNodeVm n && n.Node.Children.Count > 0) { n.IsExpanded = true; Current = n; }
    });

    private Task Scan() => RunAsync("正在分析空间占用…", async (ct, progress) =>
    {
        var node = await new FolderTreeAnalyzer(AppHost.Guard).AnalyzeAsync(Root, Math.Clamp(Depth, 1, 8), progress, ct);
        var vm = new FolderNodeVm(node, null, node.Size);
        vm.IsExpanded = true;
        Roots.ReplaceWith(new[] { vm });
        Current = vm;
        Status = $"分析完成：{Root} 共 {SizeFormatter.Format(node.Size)}，{node.FileCount:N0} 个文件";
    });
}
