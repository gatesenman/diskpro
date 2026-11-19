using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Models;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class FileRowVm : ObservableObject
{
    public FileEntry Entry { get; }
    public FileRowVm(FileEntry e) => Entry = e;
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public string Directory => Entry.Directory;
    public long Size => Entry.Size;
    public string SizeText => SizeFormatter.Format(Entry.Size);
    public string Modified => Entry.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Type => string.IsNullOrEmpty(Entry.Extension) ? "文件" : Entry.Extension.TrimStart('.').ToUpperInvariant();
    private bool _sel; public bool IsSelected { get => _sel; set { if (Set(ref _sel, value)) Changed?.Invoke(); } }
    public Action? Changed { get; set; }
    public bool IsProtected { get; init; }
}

public sealed class LargeFilesViewModel : PageViewModel
{
    public override string Title => "大文件扫描";
    public override string Subtitle => "多线程遍历磁盘，找出占用最大的文件";
    public ObservableCollection<FileRowVm> Files { get; } = new();

    public string Root { get => _root; set => Set(ref _root, value); }
    private string _root = AppHost.SystemDrive;
    public string MinSizeText { get => _min; set => Set(ref _min, value); }
    private string _min = SizeFormatter.Format(AppHost.Settings.LargeFileMinBytes).Replace(" ", "");
    public int TopN { get => _topN; set => Set(ref _topN, value); }
    private int _topN = AppHost.Settings.LargeFileTopN;

    public IReadOnlyList<string> TypeFilters { get; } = new[] { "全部类型", "视频 (mp4/mkv/avi/mov)", "压缩包 (zip/rar/7z)", "镜像 (iso/img/vhd/vhdx)", "安装包 (exe/msi)", "文档 (pdf/doc/ppt)", "日志 (log/dmp)" };
    private int _typeIndex; public int TypeIndex { get => _typeIndex; set => Set(ref _typeIndex, value); }

    private long _selSize; public string SelectedText => $"已选 {Files.Count(f => f.IsSelected)} 项 · {SizeFormatter.Format(_selSize)}";

    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand BrowseCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(Root); if (f != null) Root = f; });
    public ICommand OpenCommand => new RelayCommand(p => { if (p is FileRowVm f) Dialogs.OpenLocation(f.Path); });
    public ICommand DeleteCommand => new AsyncCommand(Delete, () => IsIdle && Files.Any(f => f.IsSelected));

    private HashSet<string>? Extensions() => TypeIndex switch
    {
        1 => new() { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".m4v", ".ts" },
        2 => new() { ".zip", ".rar", ".7z", ".tar", ".gz", ".xz", ".bz2" },
        3 => new() { ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".wim", ".esd" },
        4 => new() { ".exe", ".msi", ".msix", ".appx" },
        5 => new() { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".psd" },
        6 => new() { ".log", ".dmp", ".etl" },
        _ => null
    };

    private Task Scan() => RunAsync("正在扫描大文件…", async (ct, progress) =>
    {
        if (!SizeFormatter.TryParse(MinSizeText, out var min)) min = 100L << 20;
        AppHost.Settings.LargeFileMinBytes = min; AppHost.Settings.LargeFileTopN = TopN; AppHost.SaveSettings();
        var opt = new LargeFileOptions { Root = Root, MinSizeBytes = min, TopN = Math.Clamp(TopN, 10, 5000), Extensions = Extensions(), SkipSystemTrees = true };
        // Parallel: scan each top-level directory concurrently, merge top-N.
        var tops = new List<string>();
        try { tops.AddRange(System.IO.Directory.EnumerateDirectories(Root)); } catch { }
        var scanner = new LargeFileScanner(AppHost.Guard);
        var bag = new System.Collections.Concurrent.ConcurrentBag<FileEntry>();
        long filesTotal = 0, bytesTotal = 0;
        var agg = new Progress<ScanProgress>(p => { });
        await Task.Run(() =>
        {
            Parallel.ForEach(tops, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, dir =>
            {
                var local = new Progress<ScanProgress>();
                var sub = new LargeFileOptions { Root = dir, MinSizeBytes = opt.MinSizeBytes, TopN = opt.TopN, Extensions = opt.Extensions, SkipSystemTrees = true };
                var res = scanner.Scan(sub, new InlineProgress(p =>
                {
                    Interlocked.Add(ref filesTotal, 512);
                    ((IProgress<ScanProgress>)progress).Report(new ScanProgress { Stage = "扫描大文件", FilesScanned = Interlocked.Read(ref filesTotal), CurrentPath = p.CurrentPath, BytesScanned = Interlocked.Add(ref bytesTotal, 0) });
                }), ct);
                foreach (var r in res) bag.Add(r);
            });
            // files directly in root
            try
            {
                foreach (var f in System.IO.Directory.EnumerateFiles(Root))
                {
                    var fi = new FileInfo(f);
                    if (fi.Length >= opt.MinSizeBytes && (opt.Extensions == null || opt.Extensions.Contains(fi.Extension.ToLowerInvariant())))
                        bag.Add(new FileEntry(fi.FullName, fi.Length, fi.LastWriteTimeUtc, fi.CreationTimeUtc));
                }
            }
            catch { }
        }, ct);
        var list = bag.OrderByDescending(f => f.Size).Take(opt.TopN).ToList();
        Files.ReplaceWith(list.Select(e => new FileRowVm(e) { Changed = UpdateSel, IsProtected = AppHost.Guard.IsSystemTree(e.Path) }));
        UpdateSel();
        Status = $"找到 {Files.Count} 个大文件，合计 {SizeFormatter.Format(list.Sum(f => f.Size))}";
    });

    private void UpdateSel() { _selSize = Files.Where(f => f.IsSelected).Sum(f => f.Size); OnPropertyChanged(nameof(SelectedText)); }

    private async Task Delete()
    {
        var sel = Files.Where(f => f.IsSelected).ToList();
        var mode = Dialogs.ConfirmDeletion("确认删除大文件", sel.Select(f => new PreviewRow(f.Path, f.Size)).ToList());
        if (mode == null) return;
        await RunAsync("正在删除…", async (ct, progress) =>
        {
            var r = await AppHost.Deleter.DeleteAsync(new DeleteRequest(sel.Select(f => f.Path).ToList(), new[] { Root }, mode.Value, AppHost.Settings.DryRun), progress, ct);
            Dialogs.ShowResult(r);
            if (!r.DryRun)
                foreach (var ok in r.Items.Where(i => i.Success)) { var row = Files.FirstOrDefault(f => f.Path == ok.Path); if (row != null) Files.Remove(row); }
            UpdateSel();
            Status = r.DryRun ? "预览完成" : $"已释放 {SizeFormatter.Format(r.FreedBytes)}";
        });
    }
}

internal sealed class InlineProgress : IProgress<ScanProgress>
{
    private readonly Action<ScanProgress> _a; public InlineProgress(Action<ScanProgress> a) => _a = a;
    public void Report(ScanProgress value) => _a(value);
}
