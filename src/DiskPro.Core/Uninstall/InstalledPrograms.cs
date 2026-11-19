using System.Runtime.Versioning;
using DiskPro.Core.Models;
using DiskPro.Core.Util;
namespace DiskPro.Core.Uninstall;

public sealed class InstalledProgram
{
    public string DisplayName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string DisplayVersion { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public long EstimatedSizeBytes { get; init; }
    public string RegistryKey { get; init; } = "";
    public bool Is64Bit { get; init; }
    public string IconPath { get; init; } = "";
    /// <summary>Size measured from InstallLocation when available (more accurate than EstimatedSize).</summary>
    public long MeasuredSizeBytes { get; set; }
    public long BestSize => MeasuredSizeBytes > 0 ? MeasuredSizeBytes : EstimatedSizeBytes;
    public bool IsOnDrive(string drive) =>
        (!string.IsNullOrEmpty(InstallLocation) && InstallLocation.StartsWith(drive, StringComparison.OrdinalIgnoreCase))
        || (string.IsNullOrEmpty(InstallLocation) && UninstallString.TrimStart('"').StartsWith(drive, StringComparison.OrdinalIgnoreCase));
}

public interface IProgramEnumerator
{
    IReadOnlyList<InstalledProgram> Enumerate();
}

/// <summary>Reads HKLM/HKCU Uninstall keys including WOW6432Node. On non-Windows returns empty.</summary>
public sealed class RegistryProgramEnumerator : IProgramEnumerator
{
    public IReadOnlyList<InstalledProgram> Enumerate()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<InstalledProgram>();
        return EnumerateWindows();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<InstalledProgram> EnumerateWindows()
    {
        var list = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ReadHive(Microsoft.Win32.RegistryKey root, string sub, bool is64)
        {
            try
            {
                using var key = root.OpenSubKey(sub);
                if (key == null) return;
                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sk = key.OpenSubKey(name);
                        if (sk == null) continue;
                        var display = sk.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(display)) continue;
                        if (sk.GetValue("SystemComponent") is int sc && sc == 1) continue;
                        if (sk.GetValue("ParentKeyName") is string) continue;
                        var uninstall = sk.GetValue("UninstallString") as string ?? "";
                        if (string.IsNullOrWhiteSpace(uninstall)) continue;
                        var id = display + "|" + (sk.GetValue("DisplayVersion") as string);
                        if (!seen.Add(id)) continue;
                        long sizeKb = 0;
                        try { sizeKb = Convert.ToInt64(sk.GetValue("EstimatedSize") ?? 0); } catch { }
                        DateTime? date = null;
                        if (sk.GetValue("InstallDate") is string ds && ds.Length == 8
                            && int.TryParse(ds[..4], out var y) && int.TryParse(ds.Substring(4, 2), out var m) && int.TryParse(ds.Substring(6, 2), out var d))
                            try { date = new DateTime(y, m, d); } catch { }
                        list.Add(new InstalledProgram
                        {
                            DisplayName = display.Trim(),
                            Publisher = (sk.GetValue("Publisher") as string)?.Trim() ?? "",
                            DisplayVersion = (sk.GetValue("DisplayVersion") as string)?.Trim() ?? "",
                            InstallLocation = (sk.GetValue("InstallLocation") as string)?.Trim() ?? "",
                            UninstallString = uninstall.Trim(),
                            QuietUninstallString = (sk.GetValue("QuietUninstallString") as string)?.Trim() ?? "",
                            InstallDate = date,
                            EstimatedSizeBytes = sizeKb * 1024,
                            RegistryKey = root.Name + "\\" + sub + "\\" + name,
                            Is64Bit = is64,
                            IconPath = (sk.GetValue("DisplayIcon") as string)?.Trim() ?? ""
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }
        const string un = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        const string wow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        ReadHive(Microsoft.Win32.Registry.LocalMachine, un, true);
        ReadHive(Microsoft.Win32.Registry.LocalMachine, wow, false);
        ReadHive(Microsoft.Win32.Registry.CurrentUser, un, true);
        ReadHive(Microsoft.Win32.Registry.CurrentUser, wow, false);
        return list.OrderBy(p => p.DisplayName).ToList();
    }
}

/// <summary>Test / Linux-friendly enumerator fed from an in-memory list or JSON file.</summary>
public sealed class FakeProgramEnumerator : IProgramEnumerator
{
    private readonly List<InstalledProgram> _items;
    public FakeProgramEnumerator(IEnumerable<InstalledProgram> items) => _items = items.ToList();
    public IReadOnlyList<InstalledProgram> Enumerate() => _items;
}

public sealed class LeftoverScanResult
{
    public List<string> Folders { get; } = new();
    public List<string> RegistryHints { get; } = new();
    public long TotalSize { get; set; }
}

public sealed class LeftoverScanner
{
    public LeftoverScanResult Scan(InstalledProgram program, CancellationToken ct = default)
    {
        var result = new LeftoverScanResult();
        var name = program.DisplayName;
        var tokens = Tokenize(name);
        if (tokens.Count == 0) return result;

        var roots = new List<string>();
        void AddRoot(string? p) { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) roots.Add(p); }
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AddRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var fn = Path.GetFileName(dir);
                    if (tokens.Any(t => fn.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Don't pick up the original install location if still present under an active program - leftovers are after uninstall
                        result.Folders.Add(dir);
                        try { result.TotalSize += Scanning.DirectorySizer.GetSize(dir, ct); } catch { }
                    }
                }
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(program.Publisher))
            result.RegistryHints.Add($@"HKCU\Software\{program.Publisher}");
        result.RegistryHints.Add($@"HKCU\Software\{name}");
        return result;
    }

    internal static List<string> Tokenize(string name)
    {
        var junk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "the", "for", "and", "inc", "ltd", "llc", "corp", "co", "版本", "官方", "正式版", "安装程序" };
        return name.Split(new[] { ' ', '-', '_', '(', ')', '[', ']', '.', ',', '·' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !junk.Contains(t) && !char.IsDigit(t[0]))
            .Take(4).ToList();
    }
}

public sealed class UninstallerRunner
{
    /// <summary>Force-removes the uninstall registry entry (after exporting a .reg backup).</summary>
    public (bool Ok, string Message) ForceRemoveEntry(InstalledProgram p, string backupDir, bool dryRun)
    {
        if (string.IsNullOrEmpty(p.RegistryKey)) return (false, "无注册表项");
        if (dryRun) return (true, "预览：将删除 " + p.RegistryKey);
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        Directory.CreateDirectory(backupDir);
        var safeName = new string(p.DisplayName.Where(char.IsLetterOrDigit).Take(20).ToArray());
        var file = Path.Combine(backupDir, $"uninstall-{DateTime.Now:yyyyMMddHHmmss}-{(safeName.Length > 0 ? safeName : "app")}.reg");
        var key = p.RegistryKey.Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU");
        var exp = Optimization.SystemOptimizer.RunCapture("reg.exe", $"export \"{key}\" \"{file}\" /y", 15000);
        if (exp.Code != 0) return (false, "备份注册表失败，已取消：" + exp.Output);
        var del = Optimization.SystemOptimizer.RunCapture("reg.exe", $"delete \"{key}\" /f", 15000);
        return del.Code == 0 ? (true, $"已删除卸载项，备份：{file}") : (false, del.Output);
    }

    public (string FileName, string Arguments) ParseCommand(string uninstallString)
    {
        uninstallString = uninstallString.Trim();
        if (uninstallString.StartsWith('"'))
        {
            var end = uninstallString.IndexOf('"', 1);
            if (end > 1)
            {
                var file = uninstallString[1..end];
                var args = uninstallString[(end + 1)..].Trim();
                return (file, args);
            }
        }
        var sp = uninstallString.IndexOf(' ');
        if (sp < 0) return (uninstallString, "");
        return (uninstallString[..sp], uninstallString[(sp + 1)..].Trim());
    }

    public int Run(InstalledProgram program, bool quiet, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("卸载仅支持 Windows");
        var cmd = quiet && !string.IsNullOrEmpty(program.QuietUninstallString)
            ? program.QuietUninstallString : program.UninstallString;
        var (file, args) = ParseCommand(cmd);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = file, Arguments = args, UseShellExecute = true, Verb = "runas"
        };
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("无法启动卸载程序");
        p.WaitForExit();
        return p.ExitCode;
    }
}
