using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DiskPro.Core.Logging;
using DiskPro.Core.Util;
namespace DiskPro.Core.Relocation;

public enum KnownFolderKind { Desktop, Documents, Downloads, Music, Pictures, Videos }

public sealed class FolderMovePlan
{
    public KnownFolderKind Kind { get; init; }
    public string CurrentPath { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public long EstimatedBytes { get; set; }
    public bool UseJunction { get; init; }
}

public sealed class FolderMoveResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? RollbackHint { get; init; }
    public string PreviousPath { get; init; } = "";
    public string NewPath { get; init; } = "";
}

public sealed class KnownFolderMover
{
    private readonly IOperationLogger _log;
    public KnownFolderMover(IOperationLogger log) => _log = log;

    private static readonly Dictionary<KnownFolderKind, Guid> Guids = new()
    {
        [KnownFolderKind.Desktop] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        [KnownFolderKind.Documents] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        [KnownFolderKind.Downloads] = new("374DE290-123F-4565-9164-39C4925E467B"),
        [KnownFolderKind.Music] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
        [KnownFolderKind.Pictures] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        [KnownFolderKind.Videos] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
    };

    private static readonly Dictionary<KnownFolderKind, Environment.SpecialFolder> Special = new()
    {
        [KnownFolderKind.Desktop] = Environment.SpecialFolder.DesktopDirectory,
        [KnownFolderKind.Documents] = Environment.SpecialFolder.MyDocuments,
        [KnownFolderKind.Music] = Environment.SpecialFolder.MyMusic,
        [KnownFolderKind.Pictures] = Environment.SpecialFolder.MyPictures,
        [KnownFolderKind.Videos] = Environment.SpecialFolder.MyVideos,
    };

    public string GetCurrentPath(KnownFolderKind kind)
    {
        if (kind == KnownFolderKind.Downloads)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(profile, "Downloads");
        }
        return Environment.GetFolderPath(Special[kind]);
    }

    public FolderMovePlan Plan(KnownFolderKind kind, string targetRoot, bool useJunction = false)
    {
        var current = GetCurrentPath(kind);
        var name = kind.ToString();
        var target = Path.Combine(targetRoot, name);
        long size = 0;
        if (Directory.Exists(current))
            try { size = Scanning.DirectorySizer.GetSize(current, CancellationToken.None); } catch { }
        return new FolderMovePlan { Kind = kind, CurrentPath = current, TargetPath = target, EstimatedBytes = size, UseJunction = useJunction };
    }

    public Task<FolderMoveResult> ExecuteAsync(FolderMovePlan plan, bool dryRun, CancellationToken ct = default)
        => Task.Run(() => Execute(plan, dryRun, ct), ct);

    public FolderMoveResult Execute(FolderMovePlan plan, bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plan.TargetPath))
            return new FolderMoveResult { Success = false, Error = "目标路径无效" };
        if (PathUtil.Normalize(plan.CurrentPath).Equals(PathUtil.Normalize(plan.TargetPath), PathUtil.Cmp))
            return new FolderMoveResult { Success = false, Error = "目标与当前位置相同" };
        var targetRoot = Path.GetPathRoot(plan.TargetPath);
        if (string.IsNullOrEmpty(targetRoot) || !Directory.Exists(targetRoot))
            return new FolderMoveResult { Success = false, Error = "目标磁盘不存在" };

        if (dryRun)
        {
            _log.Log(LogLevel.DryRun, "搬家", $"[预览] {plan.Kind}: {plan.CurrentPath} → {plan.TargetPath} ({SizeFormatter.Format(plan.EstimatedBytes)})");
            return new FolderMoveResult { Success = true, PreviousPath = plan.CurrentPath, NewPath = plan.TargetPath, RollbackHint = "预览模式未修改" };
        }
        if (!OperatingSystem.IsWindows())
            return new FolderMoveResult { Success = false, Error = "文件夹搬家仅支持 Windows" };

        try
        {
            Directory.CreateDirectory(plan.TargetPath);
            // Move contents
            if (Directory.Exists(plan.CurrentPath))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(plan.CurrentPath))
                {
                    ct.ThrowIfCancellationRequested();
                    var dest = Path.Combine(plan.TargetPath, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest, overwrite: false);
                }
            }

            if (plan.UseJunction)
            {
                // Replace original with junction pointing to new location
                try { if (Directory.Exists(plan.CurrentPath) && !Directory.EnumerateFileSystemEntries(plan.CurrentPath).Any()) Directory.Delete(plan.CurrentPath); } catch { }
                CreateJunction(plan.CurrentPath, plan.TargetPath);
                _log.Log(LogLevel.Info, "搬家", $"已创建联接 {plan.CurrentPath} → {plan.TargetPath}");
            }
            else
            {
                RedirectKnownFolder(plan.Kind, plan.TargetPath);
                _log.Log(LogLevel.Info, "搬家", $"已重定向已知文件夹 {plan.Kind} → {plan.TargetPath}");
            }

            return new FolderMoveResult
            {
                Success = true, PreviousPath = plan.CurrentPath, NewPath = plan.TargetPath,
                RollbackHint = plan.UseJunction
                    ? $"删除联接并把文件移回：{plan.CurrentPath}"
                    : $"将已知文件夹改回：{plan.CurrentPath}"
            };
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, "搬家", ex.Message);
            return new FolderMoveResult { Success = false, Error = ex.Message, PreviousPath = plan.CurrentPath, NewPath = plan.TargetPath };
        }
    }

    public FolderMoveResult Rollback(FolderMovePlan originalPlan, string previousPath, bool wasJunction)
    {
        if (!OperatingSystem.IsWindows()) return new FolderMoveResult { Success = false, Error = "仅支持 Windows" };
        try
        {
            if (wasJunction)
            {
                if (Directory.Exists(previousPath) && (File.GetAttributes(previousPath) & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(previousPath, false);
                Directory.CreateDirectory(previousPath);
                foreach (var entry in Directory.EnumerateFileSystemEntries(originalPlan.TargetPath))
                {
                    var dest = Path.Combine(previousPath, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) Directory.Move(entry, dest); else File.Move(entry, dest);
                }
            }
            else
            {
                RedirectKnownFolder(originalPlan.Kind, previousPath);
                foreach (var entry in Directory.EnumerateFileSystemEntries(originalPlan.TargetPath))
                {
                    var dest = Path.Combine(previousPath, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) Directory.Move(entry, dest); else File.Move(entry, dest);
                }
            }
            _log.Log(LogLevel.Info, "搬家", $"已回滚 {originalPlan.Kind}");
            return new FolderMoveResult { Success = true, PreviousPath = originalPlan.TargetPath, NewPath = previousPath };
        }
        catch (Exception ex) { return new FolderMoveResult { Success = false, Error = ex.Message }; }
    }

    [SupportedOSPlatform("windows")]
    private static void RedirectKnownFolder(KnownFolderKind kind, string newPath)
    {
        if (!Guids.TryGetValue(kind, out var guid)) throw new NotSupportedException(kind.ToString());
        int hr = SHSetKnownFolderPath(ref guid, 0, IntPtr.Zero, newPath);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, string path);

    private static void CreateJunction(string link, string target)
    {
        // Use mklink /J via cmd
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(15000);
        if (p.ExitCode != 0) throw new InvalidOperationException("创建目录联接失败: " + p.StandardError.ReadToEnd());
    }
}
