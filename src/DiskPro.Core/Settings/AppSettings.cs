using System.Text.Json;
namespace DiskPro.Core.Settings;

public sealed class AppSettings
{
    public bool DryRun { get; set; }
    public bool DarkTheme { get; set; } = true;
    public bool IncludeOptionalAppCaches { get; set; }
    public bool PreferRecycleBin { get; set; } = true;
    public long LargeFileMinBytes { get; set; } = 100L * 1024 * 1024;
    public int LargeFileTopN { get; set; } = 200;
    public long DuplicateMinBytes { get; set; } = 64 * 1024;
    public string Language { get; set; } = "zh-CN";
    public List<string> Exclusions { get; set; } = new();
    public bool IncludePrivacyOptional { get; set; }
    public bool EnableTray { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public bool LowDiskAlert { get; set; } = true;
    public int LowDiskThresholdGb { get; set; } = 10;
    public bool RestorePointBeforeRisky { get; set; } = true;
    public string AutoCleanFrequency { get; set; } = "Weekly";
    public string AutoCleanTime { get; set; } = "12:00";

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskPro", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
