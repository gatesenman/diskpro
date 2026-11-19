using DiskPro.Core.Logging;
using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Safety;
namespace DiskPro.Core.Cleaning;

/// <summary>The only component allowed to delete files. Enforces safety rules, scope, dry-run and logging.</summary>
public sealed class SafeDeleter
{
    private readonly SafetyGuard _guard;
    private readonly IRecycleBin _bin;
    private readonly IOperationLogger _log;

    public SafeDeleter(SafetyGuard guard, IRecycleBin bin, IOperationLogger log)
    { _guard = guard; _bin = bin; _log = log; }

    public Task<DeleteResult> DeleteAsync(DeleteRequest req, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Delete(req, progress, ct), ct);

    public DeleteResult Delete(DeleteRequest req, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new DeleteResult { DryRun = req.DryRun };
        int i = 0;
        foreach (var path in req.Paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            i++;
            progress?.Report(new ScanProgress { CurrentPath = path, Percent = 100.0 * i / Math.Max(1, req.Paths.Count), Stage = "删除中" });
            long size = MeasureQuick(path);
            var reason = _guard.CheckDeletable(path, req.AllowedScopes);
            if (reason != null)
            {
                result.Items.Add(new(path, size, false, reason));
                _log.Log(LogLevel.Warn, "安全", $"已跳过 {path}：{reason}");
                continue;
            }
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                result.Items.Add(new(path, 0, false, "文件不存在"));
                continue;
            }
            if (IsReparsePoint(path) && Directory.Exists(path))
            {
                // Never follow junctions/symlinks: only remove the link itself.
                if (req.DryRun) { result.Items.Add(new(path, 0, true, "预览：将移除链接")); continue; }
                try { Directory.Delete(path, false); result.Items.Add(new(path, 0, true, null)); }
                catch (Exception ex) { result.Items.Add(new(path, 0, false, Describe(ex))); }
                continue;
            }
            if (req.DryRun)
            {
                result.Items.Add(new(path, size, true, "预览模式，未实际删除"));
                _log.Log(LogLevel.DryRun, "删除", $"[预览] {path} ({Util.SizeFormatter.Format(size)})");
                continue;
            }
            if (File.Exists(path))
            {
                var inUse = InUseReason(path);
                if (inUse != null) { result.Items.Add(new(path, size, false, inUse)); _log.Log(LogLevel.Warn, "删除", $"跳过 {path}：{inUse}"); continue; }
            }
            try
            {
                bool done = false; string? err = null;
                if (req.Mode == DeleteMode.RecycleBin) done = _bin.TryRecycle(path, out err);
                if (!done)
                {
                    if (req.Mode == DeleteMode.RecycleBin && err != null && OperatingSystem.IsWindows())
                        _log.Log(LogLevel.Info, "删除", $"回收站不可用，改为直接删除：{path}（{err}）");
                    if (File.Exists(path)) { ClearReadOnly(path); File.Delete(path); }
                    else if (Directory.Exists(path)) DeleteDirectoryBestEffort(path, result, ct);
                }
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    result.Items.Add(new(path, size, true, null));
                    _log.Log(LogLevel.Delete, "删除", $"{(req.Mode == DeleteMode.RecycleBin ? "移至回收站" : "永久删除")} {path} ({Util.SizeFormatter.Format(size)})");
                }
                else if (!result.Items.Any(r => r.Path == path))
                    result.Items.Add(new(path, size, false, "部分内容被占用，已跳过"));
            }
            catch (Exception ex)
            {
                result.Items.Add(new(path, size, false, Describe(ex)));
                _log.Log(LogLevel.Error, "删除", $"{path}：{Describe(ex)}");
            }
        }
        _log.Log(LogLevel.Info, "删除", $"完成：成功 {result.SuccessCount}，跳过 {result.SkippedCount}，释放 {Util.SizeFormatter.Format(result.FreedBytes)}{(req.DryRun ? "（预览）" : "")}");
        return result;
    }

    /// <summary>Deletes directory contents file by file, skipping locked ones (common for Temp folders).</summary>
    private void DeleteDirectoryBestEffort(string dir, DeleteResult result, CancellationToken ct)
    {
        foreach (var f in SafeEnum(() => Directory.EnumerateFiles(dir)))
        {
            ct.ThrowIfCancellationRequested();
            try { ClearReadOnly(f); File.Delete(f); } catch { }
        }
        foreach (var d in SafeEnum(() => Directory.EnumerateDirectories(dir)))
        {
            if (IsReparsePoint(d)) { try { Directory.Delete(d, false); } catch { } continue; }
            DeleteDirectoryBestEffort(d, result, ct);
        }
        try { Directory.Delete(dir, false); } catch { }
    }

    private static IEnumerable<string> SafeEnum(Func<IEnumerable<string>> f)
    { try { return f().ToList(); } catch { return Array.Empty<string>(); } }

    private static void ClearReadOnly(string f)
    {
        try { var a = File.GetAttributes(f); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly); } catch { }
    }

    public static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; }
    }

    /// <summary>Returns a reason if the file is locked by another process.</summary>
    public static string? InUseReason(string path)
    {
        try { using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return null; }
        catch (UnauthorizedAccessException) { return null; } // read-only or ACL; let delete decide
        catch (IOException ex) when (ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)) { return "文件正在被其他程序使用"; }
        catch (IOException) { return null; }
    }

    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "拒绝访问（权限不足或文件受保护）",
        IOException io when io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) => "文件正在被其他程序使用",
        PathTooLongException => "路径过长",
        DirectoryNotFoundException or FileNotFoundException => "文件不存在",
        _ => ex.Message
    };

    public static long MeasureQuick(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path) && !IsReparsePoint(path)) return Scanning.DirectorySizer.GetSize(path, CancellationToken.None);
        }
        catch { }
        return 0;
    }
}
