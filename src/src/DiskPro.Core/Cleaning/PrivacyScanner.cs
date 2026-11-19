using DiskPro.Core.Models;
namespace DiskPro.Core.Cleaning;

/// <summary>Browser history / cookies. Every item is opt-in (Selected=false) and flagged as privacy.</summary>
public sealed class PrivacyScanner
{
    private readonly string _local, _roaming;
    public PrivacyScanner(string? localAppData = null, string? appData = null)
    {
        _local = localAppData ?? Env("DISKPRO_FAKE_LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);
        _roaming = appData ?? Env("DISKPRO_FAKE_APPDATA", Environment.SpecialFolder.ApplicationData);
    }
    private static string Env(string v, Environment.SpecialFolder f) => Environment.GetEnvironmentVariable(v) is { Length: > 0 } s ? s : Environment.GetFolderPath(f);

    public JunkScanResult Scan(CancellationToken ct = default)
    {
        var r = new JunkScanResult();
        var history = new JunkCategory { Id = "priv-history", Name = "浏览历史记录", Description = "删除后浏览器地址栏联想、历史页面将清空", Risk = RiskLevel.High, IsPrivacy = true };
        var cookies = new JunkCategory { Id = "priv-cookies", Name = "Cookie（登录状态）", Description = "⚠ 删除后所有网站需要重新登录", Risk = RiskLevel.High, IsPrivacy = true };
        var forms = new JunkCategory { Id = "priv-forms", Name = "表单自动填充数据", Description = "⚠ 不含已保存密码（Login Data 永不触碰）", Risk = RiskLevel.High, IsPrivacy = true };
        void Chromium(string baseDir, string label)
        {
            if (!Directory.Exists(baseDir)) return;
            IEnumerable<string> profiles;
            try { profiles = Directory.EnumerateDirectories(baseDir).Where(d => Path.GetFileName(d) == "Default" || Path.GetFileName(d).StartsWith("Profile ")).ToList(); } catch { return; }
            foreach (var p in profiles)
            {
                ct.ThrowIfCancellationRequested();
                var name = $"{label} {Path.GetFileName(p)}";
                foreach (var f in new[] { "History", "History-journal", "Visited Links", "Top Sites", "Top Sites-journal" }) Add(history, Path.Combine(p, f), name);
                foreach (var f in new[] { Path.Combine("Network", "Cookies"), Path.Combine("Network", "Cookies-journal"), "Cookies", "Cookies-journal" }) Add(cookies, Path.Combine(p, f), name);
                foreach (var f in new[] { "Web Data", "Web Data-journal" }) Add(forms, Path.Combine(p, f), name);
            }
        }
        Chromium(Path.Combine(_local, "Google", "Chrome", "User Data"), "Chrome");
        Chromium(Path.Combine(_local, "Microsoft", "Edge", "User Data"), "Edge");
        var ff = Path.Combine(_roaming, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ff))
            foreach (var p in Directory.EnumerateDirectories(ff))
            {
                var name = "Firefox " + Path.GetFileName(p);
                Add(history, Path.Combine(p, "places.sqlite"), name + "（含书签！）");
                Add(cookies, Path.Combine(p, "cookies.sqlite"), name);
                Add(forms, Path.Combine(p, "formhistory.sqlite"), name);
            }
        r.Categories.Add(history); r.Categories.Add(cookies); r.Categories.Add(forms);
        return r;
    }

    /// <summary>Files that must never be touched by privacy cleaning.</summary>
    public static readonly string[] NeverTouch = { "Login Data", "Login Data For Account", "logins.json", "key4.db", "Bookmarks", "Preferences", "Local State" };

    private static void Add(JunkCategory c, string path, string note)
    {
        if (NeverTouch.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)) return;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return;
            c.Items.Add(new JunkItem { Id = c.Id + ":" + path, Path = path, Size = fi.Length, Selected = false, Note = note, IsDirectory = false });
        }
        catch { }
    }
}
