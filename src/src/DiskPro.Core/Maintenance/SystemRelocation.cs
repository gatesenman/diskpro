using DiskPro.Core.Logging;
using DiskPro.Core.Optimization;
namespace DiskPro.Core.Maintenance;

/// <summary>Helpers for moving pagefile and TEMP folders off the system drive.</summary>
public sealed class SystemRelocation
{
    private readonly IOperationLogger _log;
    public SystemRelocation(IOperationLogger log) => _log = log;

    public const string MemMgmtKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    /// <summary>Builds the PagingFiles REG_MULTI_SZ value. sizes in MB; 0/0 = system managed on that drive.</summary>
    public static string[] BuildPagingFiles(string targetDrive, int initialMb, int maxMb, bool keepSmallOnSystem, string systemDrive)
    {
        var d = targetDrive.TrimEnd('\\');
        var list = new List<string> { $"{d}\\pagefile.sys {initialMb} {maxMb}" };
        if (keepSmallOnSystem) list.Add($"{systemDrive.TrimEnd('\\')}\\pagefile.sys 256 1024"); // allows small crash dumps
        return list.ToArray();
    }

    public string[] CurrentPagingFiles()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<string>();
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MemMgmtKey);
        return k?.GetValue("PagingFiles") as string[] ?? Array.Empty<string>();
    }

    public (bool Ok, string Message) MovePagefile(string targetDrive, int initialMb, int maxMb, bool keepSmallOnSystem, bool dryRun)
    {
        var sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var value = BuildPagingFiles(targetDrive, initialMb, maxMb, keepSmallOnSystem, sys);
        if (dryRun) { _log.Log(LogLevel.DryRun, "虚拟内存", "[预览] PagingFiles = " + string.Join(" | ", value)); return (true, "预览"); }
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        try
        {
            // Disable "automatically manage" first, otherwise Windows ignores PagingFiles.
            SystemOptimizer.RunCapture("powershell.exe", "-NoProfile -Command \"$c=Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $c -Property @{AutomaticManagedPagefile=$false}\"", 30000);
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MemMgmtKey, true) ?? throw new InvalidOperationException("无法打开注册表");
            var old = k.GetValue("PagingFiles") as string[] ?? Array.Empty<string>();
            k.SetValue("PagingFiles.DiskProBackup", old, Microsoft.Win32.RegistryValueKind.MultiString);
            k.SetValue("PagingFiles", value, Microsoft.Win32.RegistryValueKind.MultiString);
            _log.Log(LogLevel.Info, "虚拟内存", $"PagingFiles: {string.Join(" | ", old)} → {string.Join(" | ", value)}");
            return (true, "已设置，重启电脑后生效。原设置已备份，可随时还原。");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public (bool Ok, string Message) RestorePagefile(bool dryRun)
    {
        if (dryRun) return (true, "预览");
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(MemMgmtKey, true)!;
            if (k.GetValue("PagingFiles.DiskProBackup") is string[] b) { k.SetValue("PagingFiles", b, Microsoft.Win32.RegistryValueKind.MultiString); k.DeleteValue("PagingFiles.DiskProBackup"); }
            SystemOptimizer.RunCapture("powershell.exe", "-NoProfile -Command \"$c=Get-CimInstance Win32_ComputerSystem; Set-CimInstance -InputObject $c -Property @{AutomaticManagedPagefile=$true}\"", 30000);
            _log.Log(LogLevel.Info, "虚拟内存", "已还原为系统自动管理");
            return (true, "已还原为系统自动管理，重启后生效");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public string CurrentUserTemp() => Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.User) ?? Path.GetTempPath();

    public (bool Ok, string Message) MoveUserTemp(string newDir, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(newDir) || !Path.IsPathRooted(newDir)) return (false, "路径无效");
        if (dryRun) { _log.Log(LogLevel.DryRun, "临时文件夹", $"[预览] TEMP/TMP → {newDir}"); return (true, "预览"); }
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        try
        {
            Directory.CreateDirectory(newDir);
            var old = CurrentUserTemp();
            Environment.SetEnvironmentVariable("DISKPRO_OLD_TEMP", old, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("TEMP", newDir, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable("TMP", newDir, EnvironmentVariableTarget.User);
            _log.Log(LogLevel.Info, "临时文件夹", $"TEMP/TMP: {old} → {newDir}");
            return (true, "已修改用户 TEMP/TMP，注销后对所有程序生效。");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public (bool Ok, string Message) RestoreUserTemp(bool dryRun)
    {
        if (dryRun) return (true, "预览");
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        var old = Environment.GetEnvironmentVariable("DISKPRO_OLD_TEMP", EnvironmentVariableTarget.User) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Temp");
        Environment.SetEnvironmentVariable("TEMP", old, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("TMP", old, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("DISKPRO_OLD_TEMP", null, EnvironmentVariableTarget.User);
        return (true, "已还原 TEMP/TMP");
    }
}
