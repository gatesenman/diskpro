using DiskPro.Core.Models;
using DiskPro.Core.Safety;
using DiskPro.Core.Util;
namespace DiskPro.Core.Scanning;

public sealed class LargeFileOptions
{
    public string Root { get; init; } = @"C:\";
    public long MinSizeBytes { get; init; } = 100L * 1024 * 1024;
    public int TopN { get; init; } = 200;
    public HashSet<string>? Extensions { get; init; } // null = all; lowercase with '.'
    public bool SkipSystemTrees { get; init; } = true;
    /// <summary>Only files not modified for at least this many days (0 = any).</summary>
    public int MinAgeDays { get; init; }
}

public sealed class LargeFileScanner
{
    private readonly SafetyGuard _guard;
    public LargeFileScanner(SafetyGuard guard) => _guard = guard;

    public Task<IReadOnlyList<FileEntry>> ScanAsync(LargeFileOptions opt, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(opt, progress, ct), ct);

    public IReadOnlyList<FileEntry> Scan(LargeFileOptions opt, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var heap = new PriorityQueue<FileEntry, long>(); // keep TopN largest: min-heap by size
        long files = 0, bytes = 0;
        var gate = new object();
        FastEnum.ParallelWalk(opt.Root, d => !(opt.SkipSystemTrees && _guard.IsSystemTree(d)) && !_guard.IsExcluded(d), fi =>
        {
            var n = Interlocked.Increment(ref files);
            if ((n & 0xFFF) == 0) progress?.Report(new ScanProgress { FilesScanned = n, BytesScanned = Interlocked.Read(ref bytes), CurrentPath = fi.FullName, Stage = "扫描大文件" });
            if (FastEnum.IsCloudPlaceholder(fi.Attributes)) return;
            long len; try { len = fi.Length; } catch { return; }
            Interlocked.Add(ref bytes, len);
            if (len < opt.MinSizeBytes) return;
            if (opt.Extensions is { Count: > 0 } && !opt.Extensions.Contains(fi.Extension.ToLowerInvariant())) return;
            if (opt.MinAgeDays > 0 && fi.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-opt.MinAgeDays)) return;
            if (_guard.IsExcluded(fi.FullName)) return;
            var entry = new FileEntry(fi.FullName, len, fi.LastWriteTimeUtc, fi.CreationTimeUtc);
            lock (gate)
            {
                if (heap.Count < opt.TopN) heap.Enqueue(entry, entry.Size);
                else if (entry.Size > heap.Peek().Size) { heap.Dequeue(); heap.Enqueue(entry, entry.Size); }
            }
        }, ct);
        var list = new List<FileEntry>();
        while (heap.Count > 0) list.Add(heap.Dequeue());
        list.Reverse();
        progress?.Report(new ScanProgress { FilesScanned = files, BytesScanned = bytes, Percent = 100, Stage = "完成" });
        return list;
    }
}
