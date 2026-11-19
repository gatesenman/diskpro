using System.Text;
namespace DiskPro.Core.Logging;

public enum LogLevel { Info, Warn, Error, Delete, DryRun }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Category, string Message)
{
    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss}\t{Level}\t{Category}\t{Message}";
}

public interface IOperationLogger
{
    void Log(LogLevel level, string category, string message);
    IReadOnlyList<LogEntry> Recent { get; }
    string LogDirectory { get; }
    event Action<LogEntry>? Logged;
}

public sealed class OperationLogger : IOperationLogger
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _recent = new();
    public string LogDirectory { get; }
    public event Action<LogEntry>? Logged;

    public OperationLogger(string? dir = null)
    {
        LogDirectory = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskPro", "logs");
        try { Directory.CreateDirectory(LogDirectory); } catch { }
    }

    public IReadOnlyList<LogEntry> Recent { get { lock (_lock) return _recent.ToList(); } }

    public string CurrentFile => Path.Combine(LogDirectory, $"diskpro-{DateTime.Now:yyyyMMdd}.log");

    public void Log(LogLevel level, string category, string message)
    {
        var e = new LogEntry(DateTime.Now, level, category, message);
        lock (_lock)
        {
            _recent.Add(e);
            if (_recent.Count > 5000) _recent.RemoveRange(0, 1000);
            try { File.AppendAllText(CurrentFile, e + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        Logged?.Invoke(e);
    }

    public IEnumerable<string> LogFiles() =>
        Directory.Exists(LogDirectory) ? Directory.GetFiles(LogDirectory, "*.log").OrderByDescending(f => f) : Enumerable.Empty<string>();
}

public sealed class NullLogger : IOperationLogger
{
    public List<LogEntry> Entries { get; } = new();
    public IReadOnlyList<LogEntry> Recent => Entries;
    public string LogDirectory => "";
    public event Action<LogEntry>? Logged;
    public void Log(LogLevel level, string category, string message)
    {
        var e = new LogEntry(DateTime.Now, level, category, message);
        Entries.Add(e); Logged?.Invoke(e);
    }
}
