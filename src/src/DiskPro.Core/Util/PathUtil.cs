namespace DiskPro.Core.Util;

public static class PathUtil
{
    public static readonly StringComparison Cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        if (full.Length > root.Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>True if <paramref name="path"/> equals or is nested within <paramref name="parent"/>.</summary>
    public static bool IsUnder(string path, string parent)
    {
        var p = Normalize(path); var r = Normalize(parent);
        if (r.Length == 0) return false;
        if (p.Equals(r, Cmp)) return true;
        var rr = r.EndsWith(Path.DirectorySeparatorChar) ? r : r + Path.DirectorySeparatorChar;
        return p.StartsWith(rr, Cmp);
    }

    public static bool IsStrictlyUnder(string path, string parent) =>
        IsUnder(path, parent) && !Normalize(path).Equals(Normalize(parent), Cmp);

    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);
}
