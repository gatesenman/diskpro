using System.Text.Json;
namespace DiskPro.Core.Cleaning;

public sealed record CleanRecord(DateTime Time, string Source, long FreedBytes, int Items, int Skipped, bool DryRun);

/// <summary>Persistent statistics of freed space.</summary>
public sealed class CleanHistory
{
    private readonly string _file;
    private readonly object _lock = new();
    public List<CleanRecord> Records { get; private set; } = new();
    public event Action? Changed;

    public CleanHistory(string? file = null)
    {
        _file = file ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskPro", "history.json");
        try { if (File.Exists(_file)) Records = JsonSerializer.Deserialize<List<CleanRecord>>(File.ReadAllText(_file)) ?? new(); } catch { Records = new(); }
    }

    public long TotalFreed => Records.Where(r => !r.DryRun).Sum(r => r.FreedBytes);
    public int TotalRuns => Records.Count(r => !r.DryRun);
    public long FreedSince(DateTime t) => Records.Where(r => !r.DryRun && r.Time >= t).Sum(r => r.FreedBytes);

    public void Add(CleanRecord r)
    {
        lock (_lock)
        {
            Records.Add(r);
            if (Records.Count > 2000) Records.RemoveRange(0, Records.Count - 2000);
            try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); File.WriteAllText(_file, JsonSerializer.Serialize(Records)); } catch { }
        }
        Changed?.Invoke();
    }
}
