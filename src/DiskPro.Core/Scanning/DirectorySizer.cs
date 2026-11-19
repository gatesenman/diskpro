using DiskPro.Core.Models;
namespace DiskPro.Core.Scanning;

public static class DirectorySizer
{
    public static long GetSize(string path, CancellationToken ct, IProgress<ScanProgress>? progress = null)
    {
        long total = 0; long files = 0;
        if (File.Exists(path)) { try { return new FileInfo(path).Length; } catch { return 0; } }
        FastEnum.ParallelWalk(path, _ => true, fi =>
        {
            if (FastEnum.IsCloudPlaceholder(fi.Attributes)) return;
            long len; try { len = fi.Length; } catch { return; }
            var t = Interlocked.Add(ref total, len); var n = Interlocked.Increment(ref files);
            if ((n & 0x3FF) == 0) progress?.Report(new ScanProgress { FilesScanned = n, BytesScanned = t, CurrentPath = fi.FullName });
        }, ct, depthHint(path));
        return total;
    }

    // small folders don't benefit from many threads
    private static int depthHint(string path) => 4;
}

public sealed class DriveInfoSnapshot
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes <= 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
    public string Format { get; init; } = "";

    public static DriveInfoSnapshot? For(string driveLetter)
    {
        try
        {
            var d = new DriveInfo(driveLetter);
            if (!d.IsReady) return null;
            return new DriveInfoSnapshot
            {
                Name = d.Name, Label = d.VolumeLabel, TotalBytes = d.TotalSize, FreeBytes = d.AvailableFreeSpace, Format = d.DriveFormat
            };
        }
        catch { return null; }
    }

    public static DriveInfoSnapshot DemoC() => new()
    {
        Name = "C:\\", Label = "System", TotalBytes = 256L * 1024 * 1024 * 1024, FreeBytes = 64L * 1024 * 1024 * 1024
    };
}
