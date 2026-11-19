using DiskPro.Core.Cleaning;
using DiskPro.Core.Logging;
using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Safety;
using DiskPro.Core.Settings;
namespace DiskPro.App.Services;

/// <summary>Simple composition root (service locator) for the app.</summary>
public static class AppHost
{
    public static AppSettings Settings { get; } = AppSettings.Load();
    public static OperationLogger Logger { get; } = new();
    public static SafetyGuard Guard { get; } = CreateGuard();
    public static IRecycleBin RecycleBin { get; } = new WindowsRecycleBin();
    public static SafeDeleter Deleter { get; } = new(Guard, RecycleBin, Logger);
    public static JunkCleaner Cleaner { get; } = new(Deleter, RecycleBin, Logger);
    public static CleanHistory History { get; } = new();
    public static string SystemDrive => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

    private static SafetyGuard CreateGuard()
    {
        var g = SafetyGuard.CreateDefault();
        try { g.SetUserExclusions(Settings.Exclusions); } catch { }
        return g;
    }

    public static event Action? SettingsChanged;
    public static void SaveSettings() { Settings.Save(); Guard.SetUserExclusions(Settings.Exclusions); SettingsChanged?.Invoke(); }

    /// <summary>Records a clean operation into history (dry runs are recorded but flagged).</summary>
    public static void Record(string source, DeleteResult r)
    {
        try { History.Add(new CleanRecord(DateTime.Now, source, r.FreedBytes, r.SuccessCount, r.SkippedCount, r.DryRun)); } catch { }
    }

    public static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
