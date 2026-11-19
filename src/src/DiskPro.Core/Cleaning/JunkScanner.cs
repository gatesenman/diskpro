using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.Core.Cleaning;

/// <summary>
/// Discovers reclaimable junk on C: without deleting. Paths are expanded from environment variables
/// so unit tests can point at a fake home with DISKPRO_FAKE_HOME / DISKPRO_FAKE_WINDIR.
/// </summary>
public sealed class JunkScanner
{
    private readonly IRecycleBin _bin;
    public JunkScanner(IRecycleBin bin) => _bin = bin;

    public string WindowsDir => EnvOr("DISKPRO_FAKE_WINDIR", Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"C:\Windows");
    public string UserProfile => EnvOr("DISKPRO_FAKE_HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("HOME") ?? "/tmp");
    public string LocalAppData => EnvOr("DISKPRO_FAKE_LOCALAPPDATA", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine(UserProfile, "AppData", "Local"));
    public string ProgramData => EnvOr("DISKPRO_FAKE_PROGRAMDATA", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Path.Combine(SystemRoot, "ProgramData"));
    public string ProgramFilesX86 => EnvOr("DISKPRO_FAKE_PF86", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Path.Combine(SystemRoot, "Program Files (x86)"));
    public string SystemRoot => Path.GetDirectoryName(WindowsDir.TrimEnd('\\', '/')) is { Length: > 0 } r ? r : (Path.GetPathRoot(WindowsDir) ?? "C:\\");
    public string AppData => EnvOr("DISKPRO_FAKE_APPDATA", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine(UserProfile, "AppData", "Roaming"));

    private static string EnvOr(string env, string folder, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrEmpty(v)) return v;
        return string.IsNullOrEmpty(folder) ? fallback : folder;
    }

    public Task<JunkScanResult> ScanAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default, bool includeOptionalAppCaches = false, bool includePrivacyOptional = false)
        => Task.Run(() => Scan(progress, ct, includeOptionalAppCaches, includePrivacyOptional), ct);

    public JunkScanResult Scan(IProgress<ScanProgress>? progress = null, CancellationToken ct = default, bool includeOptionalAppCaches = false, bool includePrivacyOptional = false)
    {
        var result = new JunkScanResult();
        void Stage(string s) => progress?.Report(new ScanProgress { Stage = s, CurrentPath = s });

        Stage("临时文件");
        result.Categories.Add(Safe(() => ScanTemp(ct)));

        Stage("Windows Update 缓存");
        result.Categories.Add(Safe(() => Cat("wu", "Windows Update 缓存", "SoftwareDistribution\\Download", RiskLevel.Low,
            Path.Combine(WindowsDir, "SoftwareDistribution", "Download"))));

        Stage("传递优化缓存");
        result.Categories.Add(Safe(() => Cat("do", "传递优化缓存", "DeliveryOptimization\\Cache", RiskLevel.Safe,
            Path.Combine(WindowsDir, "SoftwareDistribution", "DeliveryOptimization", "Cache"),
            Path.Combine(LocalAppData, "Temp", "DeliveryOptimization"))));

        Stage("缩略图 / 图标缓存");
        result.Categories.Add(Safe(() => ScanThumbnails(ct)));

        Stage("错误报告与崩溃转储");
        result.Categories.Add(Safe(() => Cat("wer", "错误报告与崩溃转储", "WER / Minidump / MEMORY.DMP", RiskLevel.Safe,
            Path.Combine(LocalAppData, "Microsoft", "Windows", "WER"),
            Path.Combine(WindowsDir, "Minidump"),
            Path.Combine(WindowsDir, "MEMORY.DMP"),
            Path.Combine(WindowsDir, "LiveKernelReports"))));

        Stage("旧日志");
        result.Categories.Add(Safe(() => ScanLogs(ct)));

        Stage("回收站");
        result.Categories.Add(Safe(() => ScanRecycle(ct)));

        Stage("浏览器缓存");
        result.Categories.Add(Safe(() => ScanBrowsers(ct)));

        Stage("开发工具缓存");
        result.Categories.Add(Safe(() => ScanDevCaches(ct)));

        Stage("系统日志与安装日志");
        result.Categories.Add(Safe(() => ScanSystemLogs(ct)));

        Stage("Windows Defender");
        result.Categories.Add(Safe(() => ScanDefender(ct)));

        Stage("Internet 临时文件");
        result.Categories.Add(Safe(() => Cat("inetcache", "Internet 临时文件", "INetCache / IE 临时文件", RiskLevel.Safe,
            Path.Combine(LocalAppData, "Microsoft", "Windows", "INetCache"),
            Path.Combine(LocalAppData, "Microsoft", "Windows", "Temporary Internet Files"))));

        Stage("着色器缓存");
        result.Categories.Add(Safe(() => Cat("shader", "DirectX / 显卡着色器缓存", "D3DSCache、NVIDIA / AMD DXCache，会自动重建", RiskLevel.Safe,
            Path.Combine(LocalAppData, "D3DSCache"),
            Path.Combine(LocalAppData, "NVIDIA", "DXCache"),
            Path.Combine(LocalAppData, "NVIDIA", "GLCache"),
            Path.Combine(LocalAppData, "AMD", "DxCache"),
            Path.Combine(LocalAppData, "AMD", "GLCache"),
            Path.Combine(LocalAppData, "Intel", "ShaderCache"))));

        Stage("应用程序缓存");
        result.Categories.Add(Safe(() => ScanAppCaches(ct)));

        Stage("字体缓存");
        result.Categories.Add(Safe(() => ScanFontCache(ct)));

        Stage("Installer 补丁缓存（仅报告）");
        result.Categories.Add(Safe(() => ScanInstallerPatchCache(ct)));

        if (includePrivacyOptional)
        {
            Stage("最近使用记录（隐私，可选）");
            result.Categories.Add(Safe(() => ScanRecent(ct)));
        }

        if (includeOptionalAppCaches)
        {
            Stage("即时通讯缓存（可选）");
            result.Categories.Add(Safe(() => ScanImCaches(ct)));
        }

        progress?.Report(new ScanProgress { Percent = 100, Stage = "完成" });
        return result;
    }

    private static JunkCategory Safe(Func<JunkCategory> f)
    {
        try { return f(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new JunkCategory { Id = "err:" + Guid.NewGuid().ToString("N")[..6], Name = "扫描出错", Description = ex.Message, ReportOnly = true }; }
    }

    private JunkCategory ScanTemp(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "temp", Name = "临时文件", Description = "用户 Temp 与 Windows\\Temp", Risk = RiskLevel.Safe };
        AddDir(cat, Path.GetTempPath(), "用户临时目录", ct);
        AddDir(cat, Path.Combine(WindowsDir, "Temp"), "Windows 临时目录", ct);
        AddDir(cat, Path.Combine(LocalAppData, "Temp"), "LocalAppData\\Temp", ct);
        return cat;
    }

    private JunkCategory ScanThumbnails(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "thumb", Name = "缩略图 / 图标缓存", Description = "Explorer 缩略图与图标缓存文件", Risk = RiskLevel.Safe };
        var explorer = Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer");
        if (Directory.Exists(explorer))
        {
            foreach (var f in SafeFiles(explorer, "thumbcache_*.db").Concat(SafeFiles(explorer, "iconcache_*.db")).Concat(SafeFiles(explorer, "iconcache.db")))
            {
                ct.ThrowIfCancellationRequested();
                AddFile(cat, f);
            }
        }
        return cat;
    }

    private JunkCategory ScanLogs(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "logs", Name = "旧日志文件", Description = "常见 *.log / CBS 日志（保留最近 7 天）", Risk = RiskLevel.Low };
        var cutoff = DateTime.UtcNow.AddDays(-7);
        void Collect(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in SafeFiles(dir, pattern, recursive: true))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTimeUtc < cutoff) AddFile(cat, f);
                }
                catch { }
            }
        }
        Collect(Path.Combine(WindowsDir, "Logs", "CBS"), "*.log");
        Collect(Path.Combine(WindowsDir, "Logs", "DISM"), "*.log");
        Collect(Path.Combine(LocalAppData, "Temp"), "*.log");
        return cat;
    }

    private JunkCategory ScanRecycle(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "recycle", Name = "回收站", Description = "清空回收站可立即释放空间", Risk = RiskLevel.Medium };
        long size = 0;
        try { size = _bin.QuerySize(); } catch { }
        // Also try measuring $Recycle.Bin on C:
        var rb = Path.Combine(Path.GetPathRoot(WindowsDir) ?? "C:\\", "$Recycle.Bin");
        if (size == 0 && Directory.Exists(rb))
        {
            try { size = DirectorySizer.GetSize(rb, ct); } catch { }
        }
        if (size > 0)
            cat.Items.Add(new JunkItem { Id = "recycle:all", Path = rb, Size = size, IsDirectory = true, Note = "清空整个回收站" });
        return cat;
    }

    private JunkCategory ScanBrowsers(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "browser", Name = "浏览器缓存", Description = "仅清理 Cache，绝不触及 Cookie / 密码 / 历史", Risk = RiskLevel.Safe };
        // Chrome / Edge / Chromium-like: Cache, Code Cache, GPUCache under Default (and Profiles)
        void Chromium(string baseDir, string label)
        {
            if (!Directory.Exists(baseDir)) return;
            foreach (var profile in SafeDirs(baseDir).Where(d =>
                         Path.GetFileName(d).Equals("Default", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(d).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "Service Worker\\CacheStorage" })
                    AddDir(cat, Path.Combine(profile, sub), $"{label} {Path.GetFileName(profile)}\\{sub}", ct);
            }
        }
        Chromium(Path.Combine(LocalAppData, "Google", "Chrome", "User Data"), "Chrome");
        Chromium(Path.Combine(LocalAppData, "Microsoft", "Edge", "User Data"), "Edge");
        Chromium(Path.Combine(LocalAppData, "Chromium", "User Data"), "Chromium");
        // Firefox: cache2 under profiles
        var ff = Path.Combine(LocalAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ff))
            foreach (var p in SafeDirs(ff))
                AddDir(cat, Path.Combine(p, "cache2"), $"Firefox {Path.GetFileName(p)}\\cache2", ct);
        return cat;
    }

    private JunkCategory ScanDevCaches(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "dev", Name = "开发工具缓存", Description = "npm / pip / NuGet / yarn 缓存", Risk = RiskLevel.Low };
        AddDir(cat, Path.Combine(AppData, "npm-cache"), "npm-cache", ct);
        AddDir(cat, Path.Combine(LocalAppData, "npm-cache"), "npm-cache (Local)", ct);
        AddDir(cat, Path.Combine(LocalAppData, "pip", "Cache"), "pip Cache", ct);
        AddDir(cat, Path.Combine(LocalAppData, "NuGet", "v3-cache"), "NuGet v3-cache", ct);
        AddDir(cat, Path.Combine(LocalAppData, "Yarn", "Cache"), "Yarn Cache", ct);
        var homeNpm = Path.Combine(UserProfile, ".npm");
        AddDir(cat, homeNpm, ".npm", ct);
        var homeCache = Path.Combine(UserProfile, ".cache", "pip");
        AddDir(cat, homeCache, ".cache/pip", ct);
        return cat;
    }

    private JunkCategory ScanImCaches(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "im", Name = "即时通讯缓存（可选）", Description = "微信 / QQ 缓存目录（请先关闭相关程序）", Risk = RiskLevel.Medium, IsOptional = true };
        // WeChat: Documents\WeChat Files\*\FileStorage\Cache or similar — keep conservative
        var docs = EnvOr("DISKPRO_FAKE_DOCS", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(UserProfile, "Documents"));
        var wechat = Path.Combine(docs, "WeChat Files");
        if (Directory.Exists(wechat))
        {
            foreach (var acc in Directory.EnumerateDirectories(wechat))
            {
                AddDir(cat, Path.Combine(acc, "FileStorage", "Cache"), "微信 Cache", ct);
                AddDir(cat, Path.Combine(acc, "FileStorage", "Temp"), "微信 Temp", ct);
            }
        }
        var qq = Path.Combine(docs, "Tencent Files");
        if (Directory.Exists(qq))
            foreach (var acc in Directory.EnumerateDirectories(qq))
                AddDir(cat, Path.Combine(acc, "Image"), "QQ Image 缓存", ct);
        return cat;
    }

    private JunkCategory ScanSystemLogs(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "syslogs", Name = "系统日志 / 安装日志", Description = "IIS 日志、Panther / setup 日志、WindowsUpdate ETL（保留 7 天内）", Risk = RiskLevel.Low };
        var cutoff = DateTime.UtcNow.AddDays(-7);
        void Old(string dir, string pattern, bool recursive)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in SafeFiles(dir, pattern, recursive))
            {
                ct.ThrowIfCancellationRequested();
                try { if (File.GetLastWriteTimeUtc(f) < cutoff) AddFile(cat, f); } catch { }
            }
        }
        Old(Path.Combine(SystemRoot, "inetpub", "logs", "LogFiles"), "*.log", true);
        Old(Path.Combine(WindowsDir, "Panther"), "*.log", true);
        Old(Path.Combine(WindowsDir, "Logs", "WindowsUpdate"), "*.etl", false);
        Old(Path.Combine(WindowsDir, "Logs", "MoSetup"), "*.log", false);
        Old(Path.Combine(WindowsDir, "Logs", "SIH"), "*.etl", false);
        Old(WindowsDir, "setup*.log", false);
        Old(Path.Combine(WindowsDir, "debug"), "*.log", false);
        return cat;
    }

    private JunkCategory ScanDefender(CancellationToken ct)
    {
        // Only scan history & support logs; never definitions, quarantine or platform files.
        var cat = new JunkCategory { Id = "defender", Name = "Windows Defender 扫描记录", Description = "扫描历史与支持日志（不含病毒库、隔离区）", Risk = RiskLevel.Low };
        var root = Path.Combine(ProgramData, "Microsoft", "Windows Defender");
        AddDir(cat, Path.Combine(root, "Scans", "History", "Service"), "扫描历史", ct);
        AddDir(cat, Path.Combine(root, "Support"), "支持日志", ct);
        AddDir(cat, Path.Combine(root, "Network Inspection System", "Support"), "NIS 日志", ct);
        return cat;
    }

    private JunkCategory ScanAppCaches(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "appcache", Name = "常用软件缓存", Description = "Office / Adobe / VS Code / JetBrains / Steam / Epic 缓存与下载缓存", Risk = RiskLevel.Low };
        AddDir(cat, Path.Combine(LocalAppData, "Microsoft", "Office", "16.0", "OfficeFileCache"), "Office 文件缓存", ct);
        AddDir(cat, Path.Combine(LocalAppData, "Microsoft", "Office", "OTele"), "Office 遥测缓存", ct);
        AddDir(cat, Path.Combine(AppData, "Adobe", "Common", "Media Cache Files"), "Adobe 媒体缓存", ct);
        AddDir(cat, Path.Combine(AppData, "Adobe", "Common", "Media Cache"), "Adobe 媒体缓存数据库", ct);
        AddDir(cat, Path.Combine(LocalAppData, "Adobe", "AcroCef", "DC", "Acrobat", "Cache"), "Acrobat 缓存", ct);
        foreach (var code in new[] { "Code", "Code - Insiders", "Cursor" })
        {
            AddDir(cat, Path.Combine(AppData, code, "Cache"), $"{code} Cache", ct);
            AddDir(cat, Path.Combine(AppData, code, "CachedData"), $"{code} CachedData", ct);
            AddDir(cat, Path.Combine(AppData, code, "Code Cache"), $"{code} Code Cache", ct);
            AddDir(cat, Path.Combine(AppData, code, "GPUCache"), $"{code} GPUCache", ct);
            AddDir(cat, Path.Combine(AppData, code, "logs"), $"{code} 日志", ct);
        }
        var jb = Path.Combine(LocalAppData, "JetBrains");
        if (Directory.Exists(jb))
            foreach (var d in SafeDirs(jb))
            {
                AddDir(cat, Path.Combine(d, "caches"), $"JetBrains {Path.GetFileName(d)} caches", ct);
                AddDir(cat, Path.Combine(d, "log"), $"JetBrains {Path.GetFileName(d)} log", ct);
            }
        var steam = Path.Combine(ProgramFilesX86, "Steam");
        AddDir(cat, Path.Combine(steam, "appcache", "httpcache"), "Steam httpcache", ct);
        AddDir(cat, Path.Combine(steam, "steamapps", "shadercache"), "Steam 着色器缓存", ct);
        AddDir(cat, Path.Combine(steam, "steamapps", "downloading"), "Steam 未完成下载", ct);
        AddDir(cat, Path.Combine(steam, "logs"), "Steam 日志", ct);
        AddDir(cat, Path.Combine(LocalAppData, "EpicGamesLauncher", "Saved", "webcache"), "Epic webcache", ct);
        AddDir(cat, Path.Combine(LocalAppData, "EpicGamesLauncher", "Saved", "webcache_4147"), "Epic webcache", ct);
        AddDir(cat, Path.Combine(LocalAppData, "EpicGamesLauncher", "Saved", "Logs"), "Epic 日志", ct);
        return cat;
    }

    private JunkCategory ScanFontCache(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "fontcache", Name = "字体缓存", Description = "FontCache*.dat（被字体缓存服务占用时会自动跳过，重启后重建）", Risk = RiskLevel.Low };
        var dir = Path.Combine(WindowsDir, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache");
        if (Directory.Exists(dir))
            foreach (var f in SafeFiles(dir, "*.dat")) { ct.ThrowIfCancellationRequested(); AddFile(cat, f); }
        var fntcache = Path.Combine(WindowsDir, "System32", "FNTCACHE.DAT");
        // System32 is protected: report only
        if (File.Exists(fntcache))
            try { cat.Items.Add(new JunkItem { Id = "fontcache:" + fntcache, Path = fntcache, Size = new FileInfo(fntcache).Length, ReportOnly = true, Selected = false, Note = "位于 System32，仅报告" }); } catch { }
        return cat;
    }

    private JunkCategory ScanInstallerPatchCache(CancellationToken ct)
    {
        // Orphaned MSP/MSI detection needs MSI API; we only report $PatchCache$ size — never delete.
        var cat = new JunkCategory { Id = "msi", Name = "Windows Installer 补丁缓存（仅报告）", Description = "删除可能导致软件无法修复/卸载，DiskPro 仅显示占用", Risk = RiskLevel.High, ReportOnly = true };
        var pc = Path.Combine(WindowsDir, "Installer", "$PatchCache$");
        if (Directory.Exists(pc))
        {
            long size = 0; try { size = DirectorySizer.GetSize(pc, ct); } catch { }
            cat.Items.Add(new JunkItem { Id = "msi:" + pc, Path = pc, Size = size, IsDirectory = true, ReportOnly = true, Selected = false, Note = "仅报告" });
        }
        return cat;
    }

    private JunkCategory ScanRecent(CancellationToken ct)
    {
        var cat = new JunkCategory { Id = "recent", Name = "最近使用记录 / 跳转列表 / 剪贴板历史", Description = "隐私项：清除后“最近使用”与任务栏跳转列表为空", Risk = RiskLevel.Medium, IsOptional = true, IsPrivacy = true };
        var recent = Path.Combine(AppData, "Microsoft", "Windows", "Recent");
        if (Directory.Exists(recent))
            foreach (var f in SafeFiles(recent, "*.lnk")) AddFile(cat, f);
        AddDir(cat, Path.Combine(recent, "AutomaticDestinations"), "跳转列表(自动)", ct);
        AddDir(cat, Path.Combine(recent, "CustomDestinations"), "跳转列表(自定义)", ct);
        AddDir(cat, Path.Combine(LocalAppData, "Microsoft", "Windows", "Clipboard"), "剪贴板历史", ct);
        foreach (var i in cat.Items) i.Selected = false;
        return cat;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); } catch { return Array.Empty<string>(); }
    }

    private JunkCategory Cat(string id, string name, string desc, RiskLevel risk, params string[] paths)
    {
        var cat = new JunkCategory { Id = id, Name = name, Description = desc, Risk = risk };
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) AddDir(cat, p, Path.GetFileName(p), CancellationToken.None);
            else if (File.Exists(p)) AddFile(cat, p);
        }
        return cat;
    }

    private void AddDir(JunkCategory cat, string path, string note, CancellationToken ct)
    {
        if (!Directory.Exists(path)) return;
        long size = 0;
        try { size = DirectorySizer.GetSize(path, ct); } catch { return; }
        if (size <= 0) { try { if (!Directory.EnumerateFileSystemEntries(path).Any()) return; } catch { return; } }
        cat.Items.Add(new JunkItem { Id = cat.Id + ":" + path, Path = path, Size = size, IsDirectory = true, Note = note });
    }

    private static void AddFile(JunkCategory cat, string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return;
            cat.Items.Add(new JunkItem { Id = cat.Id + ":" + path, Path = path, Size = fi.Length, IsDirectory = false });
        }
        catch { }
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern, bool recursive = false)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Scopes that are safe for junk deletion (the scanned categories themselves).</summary>
    public static IReadOnlyList<string> ScopesFrom(JunkScanResult result) =>
        result.Categories.Where(c => !c.ReportOnly).SelectMany(c => c.Items.Where(i => !i.ReportOnly).Select(i => i.IsDirectory ? i.Path : Path.GetDirectoryName(i.Path)!))
            .Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
