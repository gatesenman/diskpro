using DiskPro.Core.Models;
using DiskPro.Core.Safety;
namespace DiskPro.Core.Maintenance;

public sealed class EmptyFolderScanner
{
    private readonly SafetyGuard _guard;
    public EmptyFolderScanner(SafetyGuard guard) => _guard = guard;

    /// <summary>Returns empty folders (deepest first). A folder containing only empty folders counts as empty; the root itself is never returned.</summary>
    public List<string> Scan(string root, CancellationToken ct = default, IProgress<ScanProgress>? progress = null)
    {
        var result = new List<string>();
        long n = 0;
        bool Visit(string dir, bool isRoot)
        {
            ct.ThrowIfCancellationRequested();
            if (_guard.IsSystemTree(dir) || _guard.IsExcluded(dir)) return false;
            try { if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return false; } catch { return false; }
            bool empty = true;
            try
            {
                if (Directory.EnumerateFiles(dir).Any()) empty = false;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    if (!Visit(sub, false)) empty = false;
            }
            catch { return false; }
            if ((++n & 0x3F) == 0) progress?.Report(new ScanProgress { Stage = "查找空文件夹", CurrentPath = dir, FilesScanned = n });
            if (empty && !isRoot && _guard.CheckDeletable(dir, new[] { dir }) == null) result.Add(dir);
            return empty;
        }
        Visit(root, true);
        // keep only top-most empty folders (deleting them removes children too)
        var set = result.OrderBy(p => p.Length).ToList();
        var top = new List<string>();
        foreach (var p in set) if (!top.Any(t => Util.PathUtil.IsStrictlyUnder(p, t))) top.Add(p);
        return top;
    }
}

public sealed record BrokenShortcut(string Path, string Target);

public sealed class ShortcutScanner
{
    /// <summary>Parses a .lnk (MS-SHLLINK) and returns the local target path, or null if it can't be determined.</summary>
    public static string? ReadTarget(string lnkPath)
    {
        try
        {
            var b = File.ReadAllBytes(lnkPath);
            if (b.Length < 0x4C || BitConverter.ToUInt32(b, 0) != 0x4C) return null;
            uint flags = BitConverter.ToUInt32(b, 0x14);
            int pos = 0x4C;
            if ((flags & 0x01) != 0) { ushort idListSize = BitConverter.ToUInt16(b, pos); pos += 2 + idListSize; } // HasLinkTargetIDList
            if ((flags & 0x02) != 0) // HasLinkInfo
            {
                int liStart = pos;
                uint liSize = BitConverter.ToUInt32(b, liStart);
                uint liHeader = BitConverter.ToUInt32(b, liStart + 4);
                uint liFlags = BitConverter.ToUInt32(b, liStart + 8);
                if ((liFlags & 0x01) != 0) // VolumeIDAndLocalBasePath
                {
                    string? basePath = null, suffix = null;
                    if (liHeader >= 0x24)
                    {
                        uint off = BitConverter.ToUInt32(b, liStart + 0x1C);
                        basePath = ReadUnicodeZ(b, liStart + (int)off);
                    }
                    if (basePath == null)
                    {
                        uint off = BitConverter.ToUInt32(b, liStart + 0x10);
                        basePath = ReadAnsiZ(b, liStart + (int)off);
                    }
                    uint sufOff = BitConverter.ToUInt32(b, liStart + 0x18);
                    if (sufOff > 0) suffix = ReadAnsiZ(b, liStart + (int)sufOff);
                    return string.IsNullOrEmpty(suffix) ? basePath : System.IO.Path.Combine(basePath ?? "", suffix);
                }
                return null; // network target – don't judge
            }
            return null;
        }
        catch { return null; }
    }

    private static string ReadAnsiZ(byte[] b, int pos)
    {
        int end = pos; while (end < b.Length && b[end] != 0) end++;
        return System.Text.Encoding.Latin1.GetString(b, pos, end - pos);
    }
    private static string ReadUnicodeZ(byte[] b, int pos)
    {
        int end = pos; while (end + 1 < b.Length && (b[end] != 0 || b[end + 1] != 0)) end += 2;
        return System.Text.Encoding.Unicode.GetString(b, pos, end - pos);
    }

    public List<BrokenShortcut> Scan(IEnumerable<string> folders, Func<string, bool>? exists = null, CancellationToken ct = default)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        var list = new List<BrokenShortcut>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> lnks;
            try { lnks = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); } catch { continue; }
            foreach (var l in lnks)
            {
                ct.ThrowIfCancellationRequested();
                var t = ReadTarget(l);
                if (string.IsNullOrEmpty(t)) continue;
                // only judge local fixed-drive targets that clearly don't exist; skip removable/unmapped drives
                var root = System.IO.Path.GetPathRoot(t);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                if (!exists(t)) list.Add(new BrokenShortcut(l, t));
            }
        }
        return list;
    }

    public static IEnumerable<string> DefaultFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }
}
