using System.Runtime.Versioning;
using DiskPro.Core.Logging;
namespace DiskPro.Core.Startup;

public enum StartupLocation { RunHkcu, RunHklm, RunOnceHkcu, StartupFolderUser, StartupFolderCommon }

public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public StartupLocation Location { get; init; }
    public bool Enabled { get; set; } = true;
    public string SourcePath { get; init; } = ""; // registry value name or .lnk path
}

public interface IStartupStore
{
    IReadOnlyList<StartupEntry> List();
    bool SetEnabled(StartupEntry entry, bool enabled, out string? error);
}

public sealed class WindowsStartupStore : IStartupStore
{
    private readonly IOperationLogger _log;
    public WindowsStartupStore(IOperationLogger log) => _log = log;

    public IReadOnlyList<StartupEntry> List()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<StartupEntry>();
        return ListWindows();
    }

    [SupportedOSPlatform("windows")]
    private List<StartupEntry> ListWindows()
    {
        var list = new List<StartupEntry>();
        ReadRun(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocation.RunHkcu, list);
        ReadRun(Microsoft.Win32.Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupLocation.RunHklm, list);
        ReadRun(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", StartupLocation.RunOnceHkcu, list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.StartupFolderUser, list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.StartupFolderCommon, list);
        // Disabled items live under HKCU\...\Explorer\StartupApproved\Run — we surface Enabled based on that when present
        ApplyApproved(list);
        return list;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadRun(Microsoft.Win32.RegistryKey root, string path, StartupLocation loc, List<StartupEntry> list)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return;
            foreach (var name in key.GetValueNames())
            {
                var val = key.GetValue(name)?.ToString() ?? "";
                list.Add(new StartupEntry { Name = name, Command = val, Location = loc, SourcePath = root.Name + "\\" + path + "\\" + name, Enabled = true });
            }
        }
        catch { }
    }

    private static void ReadFolder(string folder, StartupLocation loc, List<StartupEntry> list)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var f in Directory.EnumerateFiles(folder, "*.*"))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".lnk" or ".exe" or ".bat" or ".cmd" or ".vbs")) continue;
            list.Add(new StartupEntry { Name = Path.GetFileNameWithoutExtension(f), Command = f, Location = loc, SourcePath = f, Enabled = true });
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyApproved(List<StartupEntry> list)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            if (key == null) return;
            foreach (var e in list.Where(x => x.Location is StartupLocation.RunHkcu or StartupLocation.RunHklm))
            {
                if (key.GetValue(e.Name) is byte[] data && data.Length >= 1)
                {
                    // 02 = enabled, 03 = disabled (approximate; Microsoft uses little-endian flags)
                    e.Enabled = (data[0] & 0x01) == 0;
                }
            }
        }
        catch { }
    }

    public bool SetEnabled(StartupEntry entry, bool enabled, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows()) { error = "仅支持 Windows"; return false; }
        try
        {
            if (entry.Location is StartupLocation.StartupFolderUser or StartupLocation.StartupFolderCommon)
            {
                // Rename to .disabled
                var path = entry.SourcePath;
                if (enabled)
                {
                    if (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var neu = path[..^9];
                        File.Move(path, neu);
                    }
                }
                else
                {
                    if (!path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                        File.Move(path, path + ".disabled");
                }
                _log.Log(LogLevel.Info, "启动项", $"{(enabled ? "启用" : "禁用")} 启动文件夹项 {entry.Name}");
                return true;
            }
            // Registry Run: write StartupApproved bytes
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            // 02 00 00 00 ... = enabled; 03 00 00 00 ... = disabled
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            key?.SetValue(entry.Name, data, Microsoft.Win32.RegistryValueKind.Binary);
            _log.Log(LogLevel.Info, "启动项", $"{(enabled ? "启用" : "禁用")} 注册表启动项 {entry.Name}");
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}

public sealed class FakeStartupStore : IStartupStore
{
    public List<StartupEntry> Items { get; } = new();
    public IReadOnlyList<StartupEntry> List() => Items;
    public bool SetEnabled(StartupEntry entry, bool enabled, out string? error)
    {
        error = null; entry.Enabled = enabled; return true;
    }
}
