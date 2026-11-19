using System.Runtime.InteropServices;
using System.Text.Json;
using DiskPro.Core.Logging;
using DiskPro.Core.Optimization;
namespace DiskPro.Core.Maintenance;

public sealed record RestorePoint(int SequenceNumber, string Description, DateTime Created, string Type);

public sealed class RestorePointManager
{
    private readonly IOperationLogger _log;
    public RestorePointManager(IOperationLogger log) => _log = log;

    /// <summary>Parses output of: Get-ComputerRestorePoint | Select SequenceNumber,Description,CreationTime,RestorePointType | ConvertTo-Json</summary>
    public static List<RestorePoint> ParseJson(string json)
    {
        var list = new List<RestorePoint>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        using var doc = JsonDocument.Parse(json);
        IEnumerable<JsonElement> items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement };
        foreach (var e in items)
        {
            int seq = e.TryGetProperty("SequenceNumber", out var s) && s.TryGetInt32(out var si) ? si : 0;
            string desc = e.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
            string ct = e.TryGetProperty("CreationTime", out var c) ? c.GetString() ?? "" : "";
            string type = e.TryGetProperty("RestorePointType", out var t) ? t.ToString() : "";
            list.Add(new RestorePoint(seq, desc, ParseWmiTime(ct), TypeName(type)));
        }
        return list.OrderByDescending(r => r.SequenceNumber).ToList();
    }

    public static DateTime ParseWmiTime(string s)
    {
        // WMI DMTF: 20240101123000.000000-000
        if (s.Length >= 14 && DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var t)) return t.ToLocalTime();
        return DateTime.TryParse(s, out var t2) ? t2 : DateTime.MinValue;
    }

    private static string TypeName(string t) => t switch
    {
        "0" => "安装程序", "1" => "卸载程序", "10" => "设备驱动", "12" => "设置更改", "13" => "取消操作", _ => t
    };

    public List<RestorePoint> List()
    {
        if (!OperatingSystem.IsWindows()) return new();
        var (code, output) = SystemOptimizer.RunCapture("powershell.exe",
            "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-ComputerRestorePoint | Select-Object SequenceNumber,Description,CreationTime,RestorePointType | ConvertTo-Json -Compress\"", 30000);
        if (code != 0) return new();
        var start = output.IndexOfAny(new[] { '[', '{' });
        if (start < 0) return new();
        try { return ParseJson(output[start..].Split('\n')[0]); } catch { return new(); }
    }

    public (bool Ok, string Message) Create(string description, bool dryRun)
    {
        if (dryRun) { _log.Log(LogLevel.DryRun, "还原点", $"[预览] 创建还原点 {description}"); return (true, "预览"); }
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        var safe = description.Replace("'", "").Replace("\"", "");
        var (code, output) = SystemOptimizer.RunCapture("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction SilentlyContinue; Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS\"", 300000);
        _log.Log(code == 0 ? LogLevel.Info : LogLevel.Error, "还原点", $"创建还原点 {safe}: {code} {output}");
        return (code == 0, code == 0 ? "已创建还原点" : "创建失败（Windows 默认 24 小时内只允许创建一个）：" + output);
    }

    [DllImport("srclient.dll")] private static extern int SRRemoveRestorePoint(int dwRPNum);

    public (bool Ok, string Message) Delete(int sequence, bool dryRun)
    {
        if (dryRun) { _log.Log(LogLevel.DryRun, "还原点", $"[预览] 删除还原点 #{sequence}"); return (true, "预览"); }
        if (!OperatingSystem.IsWindows()) return (false, "仅支持 Windows");
        try
        {
            int rc = SRRemoveRestorePoint(sequence);
            _log.Log(rc == 0 ? LogLevel.Delete : LogLevel.Error, "还原点", $"删除还原点 #{sequence} rc={rc}");
            return (rc == 0, rc == 0 ? "已删除" : $"删除失败 (错误 {rc})");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
