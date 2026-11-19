using System.Text;
using DiskPro.Core.Models;
using DiskPro.Core.Logging;
using DiskPro.Core.Safety;
using DiskPro.Core.Util;
namespace DiskPro.Core.Scanning;

public static class CsvExporter
{
    public static string Escape(string s) => s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static string ToCsv(IEnumerable<FileEntry> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("文件名,大小(字节),大小,修改时间,路径");
        foreach (var f in files)
            sb.AppendLine(string.Join(",", Escape(f.Name), f.Size, Escape(SizeFormatter.Format(f.Size)), f.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), Escape(f.Path)));
        return sb.ToString();
    }

    public static void Write(string file, IEnumerable<FileEntry> files) =>
        File.WriteAllText(file, ToCsv(files), new UTF8Encoding(true)); // BOM so Excel opens Chinese correctly
}

public sealed record MoveItemResult(string Source, string? Destination, bool Success, string? Reason, long Size);

/// <summary>Moves files to another folder/drive, preserving relative structure under a common root.</summary>
public sealed class FileMover
{
    private readonly SafetyGuard _guard; private readonly IOperationLogger _log;
    public FileMover(SafetyGuard guard, IOperationLogger log) { _guard = guard; _log = log; }

    public List<MoveItemResult> Move(IEnumerable<string> files, string sourceScope, string targetDir, bool dryRun, CancellationToken ct = default)
    {
        var results = new List<MoveItemResult>();
        if (PathUtil.IsUnder(targetDir, sourceScope) && !dryRun) { }
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            long size = 0; try { size = new FileInfo(f).Length; } catch { }
            var reason = _guard.CheckDeletable(f, new[] { sourceScope });
            if (reason != null) { results.Add(new(f, null, false, reason, size)); continue; }
            var rel = Path.GetRelativePath(sourceScope, f);
            if (rel.StartsWith("..")) rel = Path.GetFileName(f);
            var root = Path.GetPathRoot(rel);
            if (!string.IsNullOrEmpty(root)) rel = rel[root.Length..];
            var dest = Path.Combine(targetDir, rel);
            if (File.Exists(dest)) { results.Add(new(f, dest, false, "目标已存在同名文件", size)); continue; }
            if (dryRun) { results.Add(new(f, dest, true, "预览", size)); _log.Log(LogLevel.DryRun, "移动", $"[预览] {f} → {dest}"); continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(f, dest); // cross-volume: copy+delete handled by runtime
                results.Add(new(f, dest, true, null, size));
                _log.Log(LogLevel.Info, "移动", $"{f} → {dest}");
            }
            catch (Exception ex) { results.Add(new(f, dest, false, Cleaning.SafeDeleter.Describe(ex), size)); }
        }
        return results;
    }
}
