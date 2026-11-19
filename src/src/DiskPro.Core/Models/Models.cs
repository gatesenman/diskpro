namespace DiskPro.Core.Models;

public enum RiskLevel { Safe, Low, Medium, High }

public sealed record FileEntry(string Path, long Size, DateTime LastWriteUtc, DateTime CreationUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
}

public sealed class ScanProgress
{
    public long FilesScanned { get; init; }
    public long BytesScanned { get; init; }
    public string? CurrentPath { get; init; }
    public double? Percent { get; init; }
    public string? Stage { get; init; }
}

public enum DeleteMode { RecycleBin, Permanent }

public sealed record DeleteRequest(IReadOnlyList<string> Paths, IReadOnlyList<string> AllowedScopes, DeleteMode Mode, bool DryRun);

public sealed record DeleteItemResult(string Path, long Size, bool Success, string? Reason);

public sealed class DeleteResult
{
    public List<DeleteItemResult> Items { get; } = new();
    public long FreedBytes => Items.Where(i => i.Success).Sum(i => i.Size);
    public int SuccessCount => Items.Count(i => i.Success);
    public int SkippedCount => Items.Count(i => !i.Success);
    public bool DryRun { get; init; }
}
