using DiskPro.Core.Util;
namespace DiskPro.Core.Safety;

/// <summary>
/// Central hard safety rules. Every destructive operation must pass through <see cref="CheckDeletable"/>.
/// </summary>
public sealed class SafetyGuard
{
    private readonly List<string> _protectedExact = new();      // the path itself must not be deleted (children may be)
    private readonly List<string> _protectedTree = new();       // nothing under this tree may be deleted
    private readonly List<string> _allowedUnderProtected = new(); // explicit carve-outs inside protected trees

    private readonly List<string> _userExclusions = new();
    public IReadOnlyList<string> ProtectedTrees => _protectedTree;
    public IReadOnlyList<string> UserExclusions => _userExclusions;

    /// <summary>User-defined ignore list: paths (trees) or wildcard file patterns like *.psd. Never scanned, never deleted.</summary>
    public void SetUserExclusions(IEnumerable<string> items)
    {
        _userExclusions.Clear();
        foreach (var i in items) if (!string.IsNullOrWhiteSpace(i)) _userExclusions.Add(i.Trim());
    }

    public bool IsExcluded(string path)
    {
        foreach (var e in _userExclusions)
        {
            if (e.StartsWith("*"))
            {
                if (path.EndsWith(e[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (Path.IsPathRooted(e) && PathUtil.IsUnder(path, e)) return true;
        }
        return false;
    }
    public IReadOnlyList<string> ProtectedRoots => _protectedExact;

    public SafetyGuard(IEnumerable<string> protectedTrees, IEnumerable<string> protectedExact, IEnumerable<string>? carveOuts = null)
    {
        foreach (var p in protectedTrees) if (!string.IsNullOrWhiteSpace(p)) _protectedTree.Add(PathUtil.Normalize(p));
        foreach (var p in protectedExact) if (!string.IsNullOrWhiteSpace(p)) _protectedExact.Add(PathUtil.Normalize(p));
        if (carveOuts != null) foreach (var p in carveOuts) if (!string.IsNullOrWhiteSpace(p)) _allowedUnderProtected.Add(PathUtil.Normalize(p));
    }

    /// <summary>Default Windows rules derived from the environment.</summary>
    public static SafetyGuard CreateDefault()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(win)) win = @"C:\Windows";
        string sysDrive = Path.GetPathRoot(win) ?? @"C:\";
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string users = Path.Combine(sysDrive, "Users");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var trees = new[]
        {
            Path.Combine(win, "System32"), Path.Combine(win, "SysWOW64"), Path.Combine(win, "WinSxS"),
            Path.Combine(win, "servicing"), Path.Combine(win, "Boot"), Path.Combine(win, "Fonts"),
            Path.Combine(win, "assembly"), Path.Combine(win, "Microsoft.NET"), Path.Combine(win, "SystemApps"),
            Path.Combine(win, "SystemResources"), Path.Combine(win, "INF"), Path.Combine(win, "Installer"),
            Path.Combine(sysDrive, "Recovery"), Path.Combine(sysDrive, "System Volume Information"),
            Path.Combine(sysDrive, "Boot"),
            Path.Combine(sysDrive, "bootmgr"), Path.Combine(sysDrive, "pagefile.sys"),
            Path.Combine(sysDrive, "hiberfil.sys"), Path.Combine(sysDrive, "swapfile.sys"),
            Path.Combine(pf, "WindowsApps"),
        };
        var exact = new[]
        {
            sysDrive, win, pf, pf86, pd, users, profile,
            Path.Combine(users, "Public"), Path.Combine(users, "Default"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"),
            Path.Combine(win, "Temp"), Path.Combine(win, "SoftwareDistribution"), Path.Combine(win, "Logs"),
            Path.GetTempPath(),
        };
        static bool Valid(string p) => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p);
        return new SafetyGuard(trees.Where(Valid), exact.Where(Valid));
    }

    /// <summary>Returns null if the path may be deleted within the given scopes; otherwise a Chinese reason.</summary>
    public string? CheckDeletable(string path, IReadOnlyCollection<string> allowedScopes)
    {
        if (string.IsNullOrWhiteSpace(path)) return "路径为空";
        string full;
        try { full = PathUtil.Normalize(path); } catch { return "路径无效"; }

        var root = Path.GetPathRoot(full);
        if (root != null && full.Equals(PathUtil.Normalize(root), PathUtil.Cmp)) return "禁止删除磁盘根目录";

        foreach (var ex in _protectedExact)
            if (full.Equals(ex, PathUtil.Cmp)) return $"受保护的系统目录：{ex}";

        bool carved = _allowedUnderProtected.Any(c => PathUtil.IsUnder(full, c));
        if (!carved)
            foreach (var t in _protectedTree)
                if (PathUtil.IsUnder(full, t)) return $"位于受保护区域：{t}";

        if (IsExcluded(full)) return "位于用户排除列表中";
        if (allowedScopes.Count == 0) return "未指定允许的范围";
        if (!allowedScopes.Any(s => PathUtil.IsUnder(full, s))) return "超出所选清理范围";
        return null;
    }

    public bool IsProtected(string path) => CheckDeletable(path, new[] { Path.GetPathRoot(Path.GetFullPath(path)) ?? path }) != null;

    /// <summary>Whether a folder should be excluded from user-file scans like duplicates (system folders).</summary>
    public bool IsSystemTree(string path) => _protectedTree.Any(t => PathUtil.IsUnder(path, t));
}
