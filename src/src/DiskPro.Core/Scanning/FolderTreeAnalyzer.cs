using DiskPro.Core.Models;
using DiskPro.Core.Safety;
namespace DiskPro.Core.Scanning;

public sealed class FolderNode
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public long Size { get; set; }
    public long FileCount { get; set; }
    public List<FolderNode> Children { get; } = new();
    public FolderNode? Parent { get; set; }
}

public sealed class FolderTreeAnalyzer
{
    private readonly SafetyGuard _guard;
    public FolderTreeAnalyzer(SafetyGuard guard) => _guard = guard;

    public Task<FolderNode> AnalyzeAsync(string root, int maxDepth = 4, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Analyze(root, maxDepth, progress, ct), ct);

    public FolderNode Analyze(string root, int maxDepth = 4, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var node = new FolderNode { Path = root, Name = System.IO.Path.GetFileName(root.TrimEnd('\\','/')) is { Length: > 0 } n ? n : root };
        Build(node, 0, maxDepth, progress, ct, 0);
        return node;
    }

    private long Build(FolderNode node, int depth, int maxDepth, IProgress<ScanProgress>? progress, CancellationToken ct, long filesSoFar)
    {
        ct.ThrowIfCancellationRequested();
        long size = 0, files = filesSoFar;
        if (!Directory.Exists(node.Path)) return 0;
        var dirs = new List<string>();
        foreach (var e in FastEnum.Entries(node.Path))
        {
            if (e is DirectoryInfo) { dirs.Add(e.FullName); continue; }
            if (e is FileInfo fi)
            {
                if (FastEnum.IsCloudPlaceholder(fi.Attributes)) continue;
                size += fi.Length; files++; node.FileCount++;
                if ((files & 0x3FF) == 0) { ct.ThrowIfCancellationRequested(); progress?.Report(new ScanProgress { FilesScanned = files, BytesScanned = size, CurrentPath = fi.FullName, Stage = "空间分析" }); }
            }
        }
        if (depth < maxDepth)
        {
            var children = dirs.Select(d => new FolderNode { Path = d, Name = System.IO.Path.GetFileName(d), Parent = node }).ToList();
            if (depth == 0)
                Parallel.ForEach(children, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) },
                    c => c.Size = Build(c, depth + 1, maxDepth, progress, ct, 0));
            else
                foreach (var c in children) c.Size = Build(c, depth + 1, maxDepth, progress, ct, files);
            foreach (var c in children) { size += c.Size; node.FileCount += c.FileCount; node.Children.Add(c); }
            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }
        else
        {
            // below maxDepth: just accumulate size without tree
            foreach (var d in dirs)
            {
                size += DirectorySizer.GetSize(d, ct);
            }
        }
        node.Size = size;
        return size;
    }
}
