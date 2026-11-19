using System.Security.Cryptography;
using DiskPro.Core.Models;
using DiskPro.Core.Safety;
namespace DiskPro.Core.Scanning;

public sealed class DuplicateGroup
{
    public long Size { get; init; }
    public string Hash { get; init; } = "";
    public List<FileEntry> Files { get; } = new();
    public long WastedBytes => Size * Math.Max(0, Files.Count - 1);
}

public sealed class DuplicateOptions
{
    public string Root { get; init; } = @"C:\Users";
    /// <summary>Additional roots; when non-empty these are used together with Root.</summary>
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public HashSet<string>? Extensions { get; init; }
    public long MinSizeBytes { get; init; } = 64 * 1024;
    public bool SkipSystemTrees { get; init; } = true;
    public int PartialHashBytes { get; init; } = 64 * 1024;
}

public sealed class DuplicateFinder
{
    private readonly SafetyGuard _guard;
    public DuplicateFinder(SafetyGuard guard) => _guard = guard;

    public Task<IReadOnlyList<DuplicateGroup>> FindAsync(DuplicateOptions opt, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Find(opt, progress, ct), ct);

    public IReadOnlyList<DuplicateGroup> Find(DuplicateOptions opt, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        // Stage 1: group by size
        var bySize = new Dictionary<long, List<FileEntry>>();
        long scanned = 0, bytes = 0;
        var roots = (opt.Roots.Count > 0 ? opt.Roots : new[] { opt.Root }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // drop roots nested in other roots to avoid double counting
        roots = roots.Where(r => !roots.Any(o => o != r && Util.PathUtil.IsStrictlyUnder(r, o))).ToList();
        foreach (var root in roots) Enumerate(root, opt, bySize, ref scanned, ref bytes, progress, ct);

        // Stage 2: partial hash for size groups with >1
        var byPartial = new Dictionary<(long Size, string Partial), List<FileEntry>>();
        foreach (var kv in bySize.Where(k => k.Value.Count > 1))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var f in kv.Value)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var ph = HashPartial(f.Path, opt.PartialHashBytes);
                    var key = (kv.Key, ph);
                    if (!byPartial.TryGetValue(key, out var list)) byPartial[key] = list = new();
                    list.Add(f);
                }
                catch { }
            }
            progress?.Report(new ScanProgress { FilesScanned = scanned, BytesScanned = bytes, Stage = "部分哈希", CurrentPath = kv.Value[0].Path });
        }

        // Stage 3: full hash
        var groups = new List<DuplicateGroup>();
        foreach (var kv in byPartial.Where(k => k.Value.Count > 1))
        {
            ct.ThrowIfCancellationRequested();
            var byFull = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in kv.Value)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var h = HashFull(f.Path);
                    if (!byFull.TryGetValue(h, out var list)) byFull[h] = list = new();
                    list.Add(f);
                }
                catch { }
            }
            foreach (var g in byFull.Where(x => x.Value.Count > 1))
            {
                var dg = new DuplicateGroup { Size = kv.Key.Size, Hash = g.Key };
                dg.Files.AddRange(g.Value.OrderByDescending(f => f.LastWriteUtc));
                groups.Add(dg);
            }
            progress?.Report(new ScanProgress { FilesScanned = scanned, BytesScanned = bytes, Stage = "完整哈希" });
        }
        groups.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
        progress?.Report(new ScanProgress { Percent = 100, Stage = "完成", FilesScanned = scanned, BytesScanned = bytes });
        return groups;
    }

    private void Enumerate(string root, DuplicateOptions opt, Dictionary<long, List<FileEntry>> bySize, ref long scanned, ref long bytes, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var d = stack.Pop();
            if (opt.SkipSystemTrees && _guard.IsSystemTree(d)) continue;
            if (_guard.IsExcluded(d)) continue;
            foreach (var e in FastEnum.Entries(d))
            {
                if (e is DirectoryInfo) { stack.Push(e.FullName); continue; }
                if (e is not FileInfo fi) continue;
                if (FastEnum.IsCloudPlaceholder(fi.Attributes)) continue; // hashing would trigger a cloud download
                long len; try { len = fi.Length; } catch { continue; }
                scanned++; bytes += len;
                if ((scanned & 0x3FF) == 0) { ct.ThrowIfCancellationRequested(); progress?.Report(new ScanProgress { FilesScanned = scanned, BytesScanned = bytes, CurrentPath = fi.FullName, Stage = "按大小分组" }); }
                if (len < opt.MinSizeBytes) continue;
                if (opt.Extensions is { Count: > 0 } && !opt.Extensions.Contains(fi.Extension.ToLowerInvariant())) continue;
                if (_guard.IsExcluded(fi.FullName)) continue;
                if (!bySize.TryGetValue(len, out var list)) bySize[len] = list = new();
                list.Add(new FileEntry(fi.FullName, len, fi.LastWriteTimeUtc, fi.CreationTimeUtc));
            }
        }
    }

    public static string HashPartial(string path, int bytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[Math.Min(bytes, (int)Math.Min(fs.Length, bytes))];
        int n = fs.Read(buf, 0, buf.Length);
        return Convert.ToHexString(SHA256.HashData(buf.AsSpan(0, n)));
    }

    public static string HashFull(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    /// <summary>Selects all but keep: Newest = keep latest LastWrite; Oldest = keep earliest.</summary>
    public static IReadOnlyList<FileEntry> SelectForDeletion(DuplicateGroup group, bool keepNewest)
    {
        if (group.Files.Count <= 1) return Array.Empty<FileEntry>();
        var ordered = keepNewest
            ? group.Files.OrderByDescending(f => f.LastWriteUtc).ThenBy(f => f.Path)
            : group.Files.OrderBy(f => f.LastWriteUtc).ThenBy(f => f.Path);
        return ordered.Skip(1).ToList();
    }
}
