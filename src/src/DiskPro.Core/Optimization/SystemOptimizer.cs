using System.Diagnostics;
using DiskPro.Core.Logging;
using DiskPro.Core.Models;
using DiskPro.Core.Util;
namespace DiskPro.Core.Optimization;

public sealed class SystemOptInfo
{
    public long HiberfilBytes { get; set; }
    public bool HibernationEnabled { get; set; }
    public long PagefileBytes { get; set; }
    public string PagefilePath { get; set; } = "";
    public long WindowsOldBytes { get; set; }
    public bool WindowsOldExists { get; set; }
    public string WindowsOldPath { get; set; } = "";
    public long RestorePointBytes { get; set; }
    public string RestorePointNote { get; set; } = "";
    public List<DriverPackage> OldDrivers { get; } = new();
}

public sealed class DriverPackage
{
    public string PublishedName { get; init; } = "";
    public string Provider { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime? Date { get; init; }
    public bool IsOem { get; init; }
}

public sealed class SystemOptimizer
{
    private readonly IOperationLogger _log;
    public SystemOptimizer(IOperationLogger log) => _log = log;

    public string SystemDrive => Path.GetPathRoot(
        Environment.GetEnvironmentVariable("DISKPRO_FAKE_WINDIR")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        ?? "C:\\") ?? "C:\\";

    public Task<SystemOptInfo> GatherAsync(CancellationToken ct = default) => Task.Run(() => Gather(ct), ct);

    public SystemOptInfo Gather(CancellationToken ct = default)
    {
        var info = new SystemOptInfo();
        var drive = SystemDrive;
        var hiber = Path.Combine(drive, "hiberfil.sys");
        if (File.Exists(hiber))
        {
            try { info.HiberfilBytes = new FileInfo(hiber).Length; info.HibernationEnabled = true; } catch { }
        }
        var page = Path.Combine(drive, "pagefile.sys");
        if (File.Exists(page))
        {
            try { info.PagefileBytes = new FileInfo(page).Length; info.PagefilePath = page; } catch { }
        }
        var wold = Path.Combine(drive, "Windows.old");
        info.WindowsOldPath = wold;
        if (Directory.Exists(wold))
        {
            info.WindowsOldExists = true;
            try { info.WindowsOldBytes = Scanning.DirectorySizer.GetSize(wold, ct); } catch { }
        }
        info.RestorePointNote = "可通过“系统属性 → 系统保护”查看还原点占用；或运行 vssadmin list shadowstorage";
        try
        {
            var (code, output) = RunCapture("vssadmin", "list shadowstorage", 8000);
            if (code == 0 && !string.IsNullOrWhiteSpace(output))
            {
                (info.RestorePointBytes, info.RestorePointNote) = ParseShadowStorage(output, drive);
            }
        }
        catch { }
        if (OperatingSystem.IsWindows())
            foreach (var d in EnumerateOemDrivers()) info.OldDrivers.Add(d);
        return info;
    }

    /// <summary>Extracts "used" shadow storage for the given drive from vssadmin output (zh-CN / en-US).</summary>
    public static (long Bytes, string Note) ParseShadowStorage(string output, string drive)
    {
        var letter = drive.TrimEnd('\\', '/');
        var blocks = output.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var b in blocks)
        {
            var lines = b.Split('\n');
            var volLine = lines.FirstOrDefault(l => l.Contains("(" + letter + ")"));
            if (volLine == null || lines.ToList().IndexOf(volLine) > 1) continue;
            string? Find(params string[] keys) => lines.FirstOrDefault(l => keys.Any(k => l.Contains(k, StringComparison.OrdinalIgnoreCase)));
            var used = Find("已用", "Used Shadow");
            var max = Find("最大", "Maximum");
            string Val(string? l) => l == null ? "?" : l[(l.IndexOf(':') + 1)..].Trim();
            var bytes = ParseSize(Val(used));
            return (bytes, $"已用 {Val(used)}，上限 {Val(max)}");
        }
        return (0, "未配置卷影副本存储（系统保护可能已关闭）");
    }

    public static long ParseSize(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, @"([\d.,]+)\s*(字节|bytes|B|KB|MB|GB|TB)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) return 0;
        var mul = m.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1L << 10, "MB" => 1L << 20, "GB" => 1L << 30, "TB" => 1L << 40, _ => 1L };
        return (long)(v * mul);
    }

    public IEnumerable<DriverPackage> EnumerateOemDrivers()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var (code, output) = RunCapture("pnputil", "/enum-drivers", 15000);
        if (code != 0) yield break;
        DriverPackage? cur = null;
        string pub = "", provider = "", cls = "", ver = "";
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Published Name", StringComparison.OrdinalIgnoreCase) || line.StartsWith("发布名称"))
            {
                if (cur != null && cur.IsOem) yield return cur;
                pub = AfterColon(line); provider = cls = ver = "";
                cur = null;
            }
            else if (line.StartsWith("Provider Name", StringComparison.OrdinalIgnoreCase) || line.StartsWith("提供程序名称"))
                provider = AfterColon(line);
            else if (line.StartsWith("Class Name", StringComparison.OrdinalIgnoreCase) || line.StartsWith("类名"))
                cls = AfterColon(line);
            else if (line.StartsWith("Driver Version", StringComparison.OrdinalIgnoreCase) || line.StartsWith("驱动程序版本"))
                ver = AfterColon(line);
            else if (string.IsNullOrEmpty(line) && !string.IsNullOrEmpty(pub))
            {
                cur = new DriverPackage
                {
                    PublishedName = pub, Provider = provider, ClassName = cls, Version = ver,
                    IsOem = pub.StartsWith("oem", StringComparison.OrdinalIgnoreCase)
                };
                if (cur.IsOem) yield return cur;
                pub = ""; cur = null;
            }
        }
        if (!string.IsNullOrEmpty(pub) && pub.StartsWith("oem", StringComparison.OrdinalIgnoreCase))
            yield return new DriverPackage { PublishedName = pub, Provider = provider, ClassName = cls, Version = ver, IsOem = true };
    }

    private static string AfterColon(string line)
    {
        var i = line.IndexOf(':');
        return i >= 0 ? line[(i + 1)..].Trim() : line;
    }

    public async Task<(int Code, string Output)> RunDismCleanupAsync(bool dryRun, CancellationToken ct = default)
    {
        if (dryRun)
        {
            _log.Log(LogLevel.DryRun, "优化", "[预览] DISM /Online /Cleanup-Image /StartComponentCleanup");
            return (0, "预览模式：将执行组件存储清理");
        }
        if (!OperatingSystem.IsWindows()) return (-1, "仅支持 Windows");
        _log.Log(LogLevel.Info, "优化", "开始 DISM StartComponentCleanup");
        var r = await Task.Run(() => RunCapture("dism", "/Online /Cleanup-Image /StartComponentCleanup", 600000), ct);
        _log.Log(r.Code == 0 ? LogLevel.Info : LogLevel.Error, "优化", $"DISM 退出码 {r.Code}");
        return r;
    }

    public (int Code, string Output) SetHibernation(bool enable, bool dryRun)
    {
        var arg = enable ? "on" : "off";
        if (dryRun)
        {
            _log.Log(LogLevel.DryRun, "优化", $"[预览] powercfg /hibernate {arg}");
            return (0, "预览");
        }
        if (!OperatingSystem.IsWindows()) return (-1, "仅支持 Windows");
        var r = RunCapture("powercfg", $"/hibernate {arg}", 15000);
        _log.Log(LogLevel.Info, "优化", $"休眠已{(enable ? "启用" : "关闭")} (code={r.Code})");
        return r;
    }

    public (int Code, string Output) RemoveDriver(string publishedName, bool dryRun)
    {
        if (dryRun) { _log.Log(LogLevel.DryRun, "优化", $"[预览] pnputil /delete-driver {publishedName} /uninstall"); return (0, "预览"); }
        if (!OperatingSystem.IsWindows()) return (-1, "仅支持 Windows");
        var r = RunCapture("pnputil", $"/delete-driver {publishedName} /uninstall /force", 30000);
        _log.Log(LogLevel.Info, "优化", $"删除驱动 {publishedName} code={r.Code}");
        return r;
    }

    public (int Code, string Output) RemoveWindowsOld(bool dryRun)
    {
        if (dryRun) { _log.Log(LogLevel.DryRun, "优化", "[预览] 清理 Windows.old（通过 cleanmgr / DISM）"); return (0, "预览"); }
        if (!OperatingSystem.IsWindows()) return (-1, "仅支持 Windows");
        // Prefer Scheduled Task style cleanmgr automation is fragile; use Remove-Item is unsafe.
        // Use DISM /AnalyzeComponentStore is not enough. Official way: cleanmgr /VERYLOWDISK or rd with takeown.
        // We invoke: cmd /c takeown + icacls + rd /s /q via a careful script — but safer: start cleanmgr with sageset.
        _log.Log(LogLevel.Info, "优化", "启动磁盘清理工具清理以前的 Windows 安装…");
        return RunCapture("cleanmgr", "/d C: /VERYLOWDISK", 600000);
    }

    public static (int Code, string Output) RunCapture(string file, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "无法启动进程");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-2, "超时"); }
            return (p.ExitCode, (stdout + "\n" + stderr).Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
