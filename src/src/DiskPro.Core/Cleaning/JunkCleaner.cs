using DiskPro.Core.Logging;
using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Util;
namespace DiskPro.Core.Cleaning;

/// <summary>Turns a junk scan result into a safe delete request and executes it (used by UI, quick clean and scheduled clean).</summary>
public sealed class JunkCleaner
{
    private readonly SafeDeleter _deleter; private readonly IRecycleBin _bin; private readonly IOperationLogger _log;
    public JunkCleaner(SafeDeleter deleter, IRecycleBin bin, IOperationLogger log) { _deleter = deleter; _bin = bin; _log = log; }

    /// <summary>Paths that would be deleted for the selected (non report-only) items. Directories are expanded to their contents.</summary>
    public static List<string> ExpandSelected(IEnumerable<JunkItem> items)
    {
        var paths = new List<string>();
        foreach (var i in items)
        {
            if (i.ReportOnly || !i.Selected || i.Id.StartsWith("recycle:")) continue;
            if (i.IsDirectory && i.ContentsOnly)
            {
                try { paths.AddRange(Directory.EnumerateFileSystemEntries(i.Path)); } catch { }
            }
            else paths.Add(i.Path);
        }
        return paths;
    }

    /// <summary>Selects only the items of categories accepted by <paramref name="filter"/>.</summary>
    public static void SelectWhere(JunkScanResult r, Func<JunkCategory, bool> filter)
    {
        foreach (var c in r.Categories)
        {
            bool on = filter(c) && !c.ReportOnly;
            foreach (var i in c.Items) i.Selected = on && !i.ReportOnly;
        }
    }

    public DeleteResult Clean(JunkScanResult result, DeleteMode mode, bool dryRun, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var selected = result.Categories.Where(c => !c.ReportOnly).SelectMany(c => c.Items).Where(i => i.Selected && !i.ReportOnly).ToList();
        var outResult = new DeleteResult { DryRun = dryRun };
        var paths = ExpandSelected(selected);
        if (paths.Count > 0)
        {
            var r = _deleter.Delete(new DeleteRequest(paths, JunkScanner.ScopesFrom(result), mode, dryRun), progress, ct);
            outResult.Items.AddRange(r.Items);
        }
        var recycle = selected.Where(i => i.Id.StartsWith("recycle:")).ToList();
        if (recycle.Count > 0)
        {
            var size = recycle.Sum(i => i.Size);
            if (dryRun) outResult.Items.Add(new DeleteItemResult("回收站", size, true, "预览"));
            else if (_bin.Empty(out var err)) { outResult.Items.Add(new DeleteItemResult("回收站", size, true, null)); _log.Log(LogLevel.Delete, "清理", $"清空回收站 {SizeFormatter.Format(size)}"); }
            else outResult.Items.Add(new DeleteItemResult("回收站", size, false, err));
        }
        return outResult;
    }
}
