using DiskPro.Core.Logging;
using DiskPro.Core.Optimization;
namespace DiskPro.Core.Maintenance;

public enum ScheduleFrequency { Daily, Weekly, Monthly }

public sealed class AutoCleanScheduler
{
    public const string TaskName = @"DiskPro\AutoClean";
    private readonly IOperationLogger _log;
    public AutoCleanScheduler(IOperationLogger log) => _log = log;

    public static string BuildCreateArgs(string exePath, ScheduleFrequency freq, TimeSpan at)
    {
        var sc = freq switch { ScheduleFrequency.Daily => "DAILY", ScheduleFrequency.Weekly => "WEEKLY /D SUN", _ => "MONTHLY /D 1" };
        return $"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --autoclean\" /SC {sc} /ST {at:hh\\:mm} /RL HIGHEST";
    }

    public (bool Ok, string Message) Create(string exePath, ScheduleFrequency freq, TimeSpan at)
    {
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        var (code, output) = SystemOptimizer.RunCapture("schtasks.exe", BuildCreateArgs(exePath, freq, at), 20000);
        _log.Log(code == 0 ? LogLevel.Info : LogLevel.Error, "计划任务", $"创建自动清理任务 {freq} {at:hh\\:mm}: {code}");
        return (code == 0, code == 0 ? "已创建计划任务" : output);
    }

    public (bool Ok, string Message) Remove()
    {
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        var (code, output) = SystemOptimizer.RunCapture("schtasks.exe", $"/Delete /F /TN \"{TaskName}\"", 20000);
        _log.Log(LogLevel.Info, "计划任务", $"删除自动清理任务: {code}");
        return (code == 0, code == 0 ? "已删除计划任务" : output);
    }

    public bool Exists()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return SystemOptimizer.RunCapture("schtasks.exe", $"/Query /TN \"{TaskName}\"", 10000).Code == 0;
    }
}
