using DiskPro.Core.Cleaning;
using DiskPro.Core.Logging;
using DiskPro.Core.Maintenance;
using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Reporting;
using DiskPro.Core.Safety;
using DiskPro.Core.Scanning;
using Xunit;
namespace DiskPro.Tests;

[Collection("env")]
public class FeatureTests
{
    private static SafetyGuard G() => new(Array.Empty<string>(), Array.Empty<string>());

    private static void WithFakeEnv(TempDir t, Action a)
    {
        var vars = new Dictionary<string, string>
        {
            ["DISKPRO_FAKE_WINDIR"] = t.Dir("Windows"), ["DISKPRO_FAKE_LOCALAPPDATA"] = t.Dir("Local"),
            ["DISKPRO_FAKE_APPDATA"] = t.Dir("Roaming"), ["DISKPRO_FAKE_HOME"] = t.Path,
            ["DISKPRO_FAKE_PROGRAMDATA"] = t.Dir("ProgramData"), ["DISKPRO_FAKE_PF86"] = t.Dir("PF86"), ["DISKPRO_FAKE_DOCS"] = t.Dir("Docs"),
        };
        foreach (var kv in vars) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        try { a(); } finally { foreach (var k in vars.Keys) Environment.SetEnvironmentVariable(k, null); }
    }

    [Fact]
    public void ExtendedJunkRules()
    {
        using var t = new TempDir();
        t.File("Local/D3DSCache/a/b.idx", 100);
        t.File("Local/NVIDIA/DXCache/x.bin", 50);
        t.File("Roaming/Code/CachedData/abc/x", 70);
        t.File("Roaming/Code/User/settings.json", 9); // must NOT be included
        t.File("PF86/Steam/steamapps/shadercache/1/x", 40);
        t.File("Local/Microsoft/Windows/INetCache/IE/x.dat", 30);
        t.File("ProgramData/Microsoft/Windows Defender/Scans/History/Service/d.log", 20);
        t.File("ProgramData/Microsoft/Windows Defender/Definition Updates/mpavbase.vdm", 999); // never
        t.File("Windows/Installer/$PatchCache$/Managed/x.msp", 500);
        var old = t.File("Windows/Panther/setupact.log", 25, mtime: DateTime.UtcNow.AddDays(-30));
        t.File("Windows/Panther/new.log", 25); // recent: excluded
        WithFakeEnv(t, () =>
        {
            var r = new JunkScanner(new NoRecycleBin()).Scan();
            long C(string id) => r.Categories.Single(c => c.Id == id).TotalSize;
            Assert.Equal(150, C("shader"));
            Assert.Equal(110, C("appcache"));
            Assert.Equal(30, C("inetcache"));
            Assert.Equal(20, C("defender"));
            Assert.Equal(25, C("syslogs"));
            var msi = r.Categories.Single(c => c.Id == "msi");
            Assert.True(msi.ReportOnly); Assert.Equal(500, msi.TotalSize);
            var all = r.Categories.SelectMany(c => c.Items).Select(i => i.Path).ToList();
            Assert.DoesNotContain(all, p => p.Contains("settings.json") || p.Contains("Definition Updates"));
            // report-only never in delete scopes
            Assert.DoesNotContain(JunkScanner.ScopesFrom(r), s => s.Contains("$PatchCache$"));
            Assert.DoesNotContain(r.Categories, c => c.Id == "recent"); // privacy optional off by default
        });
    }

    [Fact]
    public void JunkCleanerQuickCleanOnlySafeAndNeverReportOnly()
    {
        using var t = new TempDir();
        var shader = t.File("Local/D3DSCache/x.bin", 100);
        var msp = t.File("Windows/Installer/$PatchCache$/x.msp", 100);
        var code = t.File("Roaming/Code/Cache/c", 10);
        WithFakeEnv(t, () =>
        {
            var r = new JunkScanner(new NoRecycleBin()).Scan();
            JunkCleaner.SelectWhere(r, c => c.QuickClean);
            foreach (var i in r.Categories.Single(c => c.Id == "msi").Items) i.Selected = true; // even if forced
            var log = new NullLogger();
            var guard = new SafetyGuard(new[] { Path.Combine(t.Path, "Windows", "System32") }, new[] { t.Path });
            var res = new JunkCleaner(new SafeDeleter(guard, new NoRecycleBin(), log), new NoRecycleBin(), log).Clean(r, DeleteMode.Permanent, false);
            Assert.False(File.Exists(shader));
            Assert.True(File.Exists(msp));        // report-only
            Assert.True(File.Exists(code));       // appcache is Low risk → not in quick clean
            Assert.True(Directory.Exists(Path.Combine(t.Path, "Local", "D3DSCache"))); // folder kept, contents removed
            Assert.True(res.FreedBytes >= 100);
        });
    }

    [Fact]
    public void PrivacyScannerOptInAndNeverPasswords()
    {
        using var t = new TempDir();
        var prof = "Local/Google/Chrome/User Data/Default/";
        t.File(prof + "History", 10); t.File(prof + "Network/Cookies", 20); t.File(prof + "Login Data", 30); t.File(prof + "Web Data", 5); t.File(prof + "Bookmarks", 5);
        var r = new PrivacyScanner(Path.Combine(t.Path, "Local"), Path.Combine(t.Path, "Roaming")).Scan();
        var items = r.Categories.SelectMany(c => c.Items).ToList();
        Assert.All(items, i => Assert.False(i.Selected));
        Assert.All(r.Categories, c => Assert.True(c.IsPrivacy));
        Assert.DoesNotContain(items, i => i.Path.EndsWith("Login Data") || i.Path.EndsWith("Bookmarks"));
        Assert.Equal(10, r.Categories.Single(c => c.Id == "priv-history").TotalSize);
        Assert.Equal(20, r.Categories.Single(c => c.Id == "priv-cookies").TotalSize);
    }

    [Fact]
    public void CleanHistoryPersists()
    {
        using var t = new TempDir();
        var f = Path.Combine(t.Path, "h.json");
        var h = new CleanHistory(f);
        h.Add(new CleanRecord(DateTime.Now, "一键清理", 1000, 3, 0, false));
        h.Add(new CleanRecord(DateTime.Now, "预览", 5000, 3, 0, true));
        var h2 = new CleanHistory(f);
        Assert.Equal(1000, h2.TotalFreed);
        Assert.Equal(1, h2.TotalRuns);
    }

    [Fact]
    public void UserExclusionsBlockDeleteAndScan()
    {
        using var t = new TempDir();
        var keep = t.File("data/keep/big.bin", 5000);
        var psd = t.File("data/art.psd", 5000);
        var other = t.File("data/other.bin", 5000);
        var g = G(); g.SetUserExclusions(new[] { Path.Combine(t.Path, "data", "keep"), "*.psd" });
        Assert.NotNull(g.CheckDeletable(keep, new[] { t.Path }));
        Assert.NotNull(g.CheckDeletable(psd, new[] { t.Path }));
        Assert.Null(g.CheckDeletable(other, new[] { t.Path }));
        var r = new LargeFileScanner(g).Scan(new LargeFileOptions { Root = t.Path, MinSizeBytes = 1 });
        Assert.Single(r);
    }

    [Fact]
    public void LargeFileAgeFilterCsvAndMove()
    {
        using var t = new TempDir();
        var old = t.File("src/old.bin", 3000, mtime: DateTime.UtcNow.AddDays(-400));
        t.File("src/new.bin", 3000);
        var r = new LargeFileScanner(G()).Scan(new LargeFileOptions { Root = Path.Combine(t.Path, "src"), MinSizeBytes = 1, MinAgeDays = 365 });
        Assert.Single(r);
        var csv = CsvExporter.ToCsv(r);
        Assert.Contains("old.bin", csv); Assert.StartsWith("文件名", csv);
        Assert.Equal("\"a,b\"", CsvExporter.Escape("a,b"));
        var mover = new FileMover(G(), new NullLogger());
        var dry = mover.Move(new[] { old }, Path.Combine(t.Path, "src"), Path.Combine(t.Path, "dst"), dryRun: true);
        Assert.True(dry[0].Success); Assert.True(File.Exists(old));
        var res = mover.Move(new[] { old }, Path.Combine(t.Path, "src"), Path.Combine(t.Path, "dst"), dryRun: false);
        Assert.True(res[0].Success); Assert.False(File.Exists(old)); Assert.True(File.Exists(Path.Combine(t.Path, "dst", "old.bin")));
    }

    [Fact]
    public void FileMoverRespectsProtection()
    {
        using var t = new TempDir();
        var sys = t.File("sys/x.dll", 10);
        var g = new SafetyGuard(new[] { Path.Combine(t.Path, "sys") }, Array.Empty<string>());
        var r = new FileMover(g, new NullLogger()).Move(new[] { sys }, t.Path, Path.Combine(t.Path, "out"), false);
        Assert.False(r[0].Success); Assert.True(File.Exists(sys));
    }

    [Fact]
    public void DuplicateMultiRootsAndExtensionFilter()
    {
        using var t = new TempDir();
        t.File("a/p.jpg", 5000, 1); t.File("b/q.jpg", 5000, 1); t.File("b/r.txt", 5000, 1);
        var r = new DuplicateFinder(G()).Find(new DuplicateOptions
        {
            Roots = new[] { Path.Combine(t.Path, "a"), Path.Combine(t.Path, "b"), Path.Combine(t.Path, "b") },
            MinSizeBytes = 1, Extensions = new HashSet<string> { ".jpg" }
        });
        Assert.Single(r); Assert.Equal(2, r[0].Files.Count);
    }

    [Fact]
    public void ChatAppAnalysis()
    {
        using var t = new TempDir();
        t.File("Docs/WeChat Files/wxid_1/FileStorage/Image/2024-01/a.dat", 100);
        t.File("Docs/WeChat Files/wxid_1/FileStorage/Video/2024-01/v.mp4", 300);
        t.File("Docs/WeChat Files/wxid_1/FileStorage/File/2024-01/doc.pdf", 50);
        t.File("Docs/WeChat Files/wxid_1/Msg/MSG0.db", 999); // never
        t.File("Docs/Tencent Files/12345/FileRecv/x.zip", 70);
        var r = new ChatAppAnalyzer(Path.Combine(t.Path, "Docs"), Path.Combine(t.Path, "Roaming")).Analyze();
        var wx = r.Apps.Single(a => a.App == "微信");
        Assert.Equal(100, wx.SizeByKind[ChatFileKind.Image]);
        Assert.Equal(300, wx.SizeByKind[ChatFileKind.Video]);
        Assert.Equal(50, wx.SizeByKind[ChatFileKind.File]);
        Assert.DoesNotContain(r.Files, f => f.Entry.Path.EndsWith(".db"));
        Assert.Equal(70, r.Apps.Single(a => a.App == "QQ").Total);
    }

    [Fact]
    public void EmptyFolders()
    {
        using var t = new TempDir();
        t.Dir("root/e1/e2/e3"); t.Dir("root/e4"); t.File("root/full/x.txt", 1); t.Dir("root/full/emptychild");
        var r = new EmptyFolderScanner(G()).Scan(Path.Combine(t.Path, "root"));
        Assert.Contains(r, p => p.EndsWith("e1"));
        Assert.DoesNotContain(r, p => p.EndsWith("e2")); // collapsed to top-most
        Assert.Contains(r, p => p.EndsWith("e4"));
        Assert.Contains(r, p => p.EndsWith("emptychild"));
        Assert.DoesNotContain(r, p => p.EndsWith("full"));
        Assert.DoesNotContain(r, p => p.EndsWith("root"));
    }

    [Fact]
    public void ShortcutParserReadsLocalBasePath()
    {
        using var t = new TempDir();
        var lnk = Path.Combine(t.Path, "x.lnk");
        File.WriteAllBytes(lnk, BuildLnk(@"C:\NoSuchDir\app.exe"));
        Assert.Equal(@"C:\NoSuchDir\app.exe", ShortcutScanner.ReadTarget(lnk));
        File.WriteAllBytes(Path.Combine(t.Path, "bad.lnk"), new byte[] { 1, 2, 3 });
        Assert.Null(ShortcutScanner.ReadTarget(Path.Combine(t.Path, "bad.lnk")));
    }

    /// <summary>Minimal MS-SHLLINK with LinkInfo/LocalBasePath (ANSI).</summary>
    internal static byte[] BuildLnk(string target)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(0x4C); w.Write(new Guid("00021401-0000-0000-C000-000000000046").ToByteArray());
        w.Write(0x02u); // HasLinkInfo
        w.Write(new byte[0x4C - 0x18]);
        var basePath = System.Text.Encoding.Latin1.GetBytes(target + "\0");
        var volId = new byte[] { 0x10, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0x10, 0, 0, 0 };
        int header = 0x1C, volOff = header, baseOff = volOff + volId.Length + 1, sufOff = baseOff + basePath.Length;
        int size = sufOff + 1;
        w.Write(size); w.Write(header); w.Write(1); w.Write(volOff); w.Write(baseOff); w.Write(0); w.Write(sufOff);
        w.Write(volId); w.Write((byte)0); w.Write(basePath); w.Write((byte)0);
        return ms.ToArray();
    }

    [Fact]
    public void BrokenShortcutDetection()
    {
        using var t = new TempDir();
        var root = Path.GetPathRoot(t.Path)!;
        var good = Path.Combine(t.Path, "exists.txt"); File.WriteAllText(good, "x");
        File.WriteAllBytes(Path.Combine(t.Path, "good.lnk"), BuildLnk(good));
        File.WriteAllBytes(Path.Combine(t.Path, "broken.lnk"), BuildLnk(Path.Combine(t.Path, "missing.exe")));
        var r = new ShortcutScanner().Scan(new[] { t.Path });
        Assert.Single(r); Assert.EndsWith("broken.lnk", r[0].Path);
    }

    [Fact]
    public void RestorePointJsonParsing()
    {
        var json = """[{"SequenceNumber":12,"Description":"Windows Update","CreationTime":"20240105103000.000000-000","RestorePointType":12},{"SequenceNumber":13,"Description":"DiskPro","CreationTime":"20240201080000.000000-000","RestorePointType":0}]""";
        var r = RestorePointManager.ParseJson(json);
        Assert.Equal(2, r.Count); Assert.Equal(13, r[0].SequenceNumber); Assert.Equal("安装程序", r[0].Type);
        Assert.Equal(2024, r[1].Created.Year);
        Assert.Single(RestorePointManager.ParseJson("""{"SequenceNumber":1,"Description":"x","CreationTime":"20240101000000.000000-000","RestorePointType":12}"""));
    }

    [Fact]
    public void PagefileValueAndScheduler()
    {
        var v = SystemRelocation.BuildPagingFiles(@"D:\", 4096, 8192, true, @"C:\");
        Assert.Equal(@"D:\pagefile.sys 4096 8192", v[0]);
        Assert.Equal(@"C:\pagefile.sys 256 1024", v[1]);
        var args = AutoCleanScheduler.BuildCreateArgs(@"C:\Program Files\DiskPro\DiskPro.exe", ScheduleFrequency.Weekly, new TimeSpan(12, 30, 0));
        Assert.Contains("--autoclean", args); Assert.Contains("/SC WEEKLY", args); Assert.Contains("/ST 12:30", args); Assert.Contains("/RL HIGHEST", args);
    }

    [Fact]
    public void HtmlReportAndHealth()
    {
        var junk = new JunkScanResult();
        var c = new JunkCategory { Id = "x", Name = "<临时>" }; c.Items.Add(new JunkItem { Path = "a", Size = 1 << 20 }); junk.Categories.Add(c);
        var html = HtmlReport.Build(new ReportInput { Drive = DriveInfoSnapshot.DemoC(), Junk = junk, HealthScore = 80 });
        Assert.Contains("&lt;临时&gt;", html); Assert.Contains("<html", html);
        Assert.Equal(100, HtmlReport.HealthScore(100L << 30, 50L << 30, 0));
        Assert.True(HtmlReport.HealthScore(100L << 30, 3L << 30, 20L << 30) < 30);
    }
}

[CollectionDefinition("env", DisableParallelization = true)]
public class EnvCollection { }
