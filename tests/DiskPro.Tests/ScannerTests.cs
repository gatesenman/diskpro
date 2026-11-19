using DiskPro.Core.Cleaning;
using DiskPro.Core.Platform;
using DiskPro.Core.Safety;
using DiskPro.Core.Scanning;
using DiskPro.Core.Uninstall;
using DiskPro.Core.Util;
using DiskPro.Core.Optimization;
using DiskPro.Core.Logging;
using Xunit;
namespace DiskPro.Tests;

[Collection("env")]
public class ScannerTests
{
    private static SafetyGuard EmptyGuard() => new(Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void LargeFilesTopNAndThreshold()
    {
        using var t = new TempDir();
        t.File("a/1.bin", 1000); t.File("a/2.bin", 5000); t.File("b/3.iso", 9000); t.File("b/c/4.bin", 7000); t.File("small.txt", 10);
        var s = new LargeFileScanner(EmptyGuard());
        var r = s.Scan(new LargeFileOptions { Root = t.Path, MinSizeBytes = 2000, TopN = 2 });
        Assert.Equal(2, r.Count);
        Assert.Equal(9000, r[0].Size);
        Assert.Equal(7000, r[1].Size);
    }

    [Fact]
    public void LargeFilesExtensionFilter()
    {
        using var t = new TempDir();
        t.File("x.iso", 3000); t.File("y.bin", 4000);
        var r = new LargeFileScanner(EmptyGuard()).Scan(new LargeFileOptions
            { Root = t.Path, MinSizeBytes = 1, TopN = 10, Extensions = new HashSet<string> { ".iso" } });
        Assert.Single(r);
        Assert.EndsWith("x.iso", r[0].Path);
    }

    [Fact]
    public void LargeFilesSkipsProtectedTree()
    {
        using var t = new TempDir();
        t.File("sys/big.bin", 5000); t.File("user/big.bin", 5000);
        var g = new SafetyGuard(new[] { Path.Combine(t.Path, "sys") }, Array.Empty<string>());
        var r = new LargeFileScanner(g).Scan(new LargeFileOptions { Root = t.Path, MinSizeBytes = 1 });
        Assert.Single(r);
        Assert.Contains("user", r[0].Path);
    }

    [Fact]
    public void LargeFilesCancellation()
    {
        using var t = new TempDir();
        for (int i = 0; i < 50; i++) t.File($"d{i}/f.bin", 10);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new LargeFileScanner(EmptyGuard()).Scan(new LargeFileOptions { Root = t.Path }, null, cts.Token));
    }

    [Fact]
    public void DirectorySizerSums()
    {
        using var t = new TempDir();
        t.File("a", 100); t.File("b/c", 200); t.File("b/d/e", 300);
        Assert.Equal(600, DirectorySizer.GetSize(t.Path, CancellationToken.None));
    }

    [Fact]
    public void FolderTreeSortedBySize()
    {
        using var t = new TempDir();
        t.File("small/a", 100); t.File("big/a", 1000); t.File("big/sub/b", 500); t.File("root.bin", 7);
        var root = new FolderTreeAnalyzer(EmptyGuard()).Analyze(t.Path, 3);
        Assert.Equal(1607, root.Size);
        Assert.Equal("big", root.Children[0].Name);
        Assert.Equal(1500, root.Children[0].Size);
        Assert.Equal(500, root.Children[0].Children[0].Size);
    }

    [Fact]
    public void FolderTreeDepthLimitStillCountsSize()
    {
        using var t = new TempDir();
        t.File("a/b/c/d/file", 400);
        var root = new FolderTreeAnalyzer(EmptyGuard()).Analyze(t.Path, 1);
        Assert.Equal(400, root.Size);
        Assert.Equal(400, root.Children[0].Size);
        Assert.Empty(root.Children[0].Children);
    }

    [Fact]
    public void JunkScannerFindsFakeCategories()
    {
        using var t = new TempDir();
        var win = t.Dir("Windows"); var local = t.Dir("Local"); var roaming = t.Dir("Roaming");
        t.File("Windows/SoftwareDistribution/Download/upd.cab", 2048);
        t.File("Windows/Temp/x.tmp", 512);
        t.File("Local/Google/Chrome/User Data/Default/Cache/data_0", 1000);
        t.File("Local/Google/Chrome/User Data/Default/Cookies", 999); // must NOT be included
        t.File("Local/Microsoft/Edge/User Data/Profile 1/Code Cache/js", 300);
        t.File("Local/Mozilla/Firefox/Profiles/abc.default/cache2/entries/1", 400);
        t.File("Local/Microsoft/Windows/Explorer/thumbcache_256.db", 123);
        t.File("Local/Microsoft/Windows/WER/ReportArchive/r.wer", 77);
        t.File("Roaming/npm-cache/_cacache/x", 66);
        Environment.SetEnvironmentVariable("DISKPRO_FAKE_WINDIR", win);
        Environment.SetEnvironmentVariable("DISKPRO_FAKE_LOCALAPPDATA", local);
        Environment.SetEnvironmentVariable("DISKPRO_FAKE_APPDATA", roaming);
        Environment.SetEnvironmentVariable("DISKPRO_FAKE_HOME", t.Path);
        Environment.SetEnvironmentVariable("DISKPRO_FAKE_PROGRAMDATA", t.Dir("PD"));
        Environment.SetEnvironmentVariable("DISKPRO_FAKE_PF86", t.Dir("PF86"));
        try
        {
            var r = new JunkScanner(new NoRecycleBin()).Scan();
            long Cat(string id) => r.Categories.Single(c => c.Id == id).TotalSize;
            Assert.Equal(2048, Cat("wu"));
            Assert.True(Cat("temp") >= 512);
            Assert.Equal(1700, Cat("browser"));
            Assert.Equal(123, Cat("thumb"));
            Assert.Equal(77, Cat("wer"));
            Assert.Equal(66, Cat("dev"));
            var browserPaths = r.Categories.Single(c => c.Id == "browser").Items.Select(i => i.Path).ToList();
            Assert.DoesNotContain(browserPaths, p => p.Contains("Cookies"));
            Assert.All(browserPaths, p => Assert.True(p.EndsWith("Cache") || p.EndsWith("cache2")));
        }
        finally
        {
            foreach (var v in new[] { "DISKPRO_FAKE_WINDIR", "DISKPRO_FAKE_LOCALAPPDATA", "DISKPRO_FAKE_APPDATA", "DISKPRO_FAKE_HOME", "DISKPRO_FAKE_PROGRAMDATA", "DISKPRO_FAKE_PF86" })
                Environment.SetEnvironmentVariable(v, null);
        }
    }

    [Fact]
    public void SizeFormatting()
    {
        Assert.Equal("0 B", SizeFormatter.Format(0));
        Assert.Equal("1.00 KB", SizeFormatter.Format(1024));
        Assert.Equal("1.50 GB", SizeFormatter.Format(1610612736));
        Assert.True(SizeFormatter.TryParse("100MB", out var b)); Assert.Equal(100L << 20, b);
        Assert.True(SizeFormatter.TryParse("1.5 g", out b)); Assert.Equal((long)(1.5 * (1L << 30)), b);
        Assert.False(SizeFormatter.TryParse("abc", out _));
    }

    [Fact]
    public void UninstallCommandParsing()
    {
        var r = new UninstallerRunner();
        Assert.Equal(("C:\\Program Files\\App\\unins000.exe", "/SILENT"), r.ParseCommand("\"C:\\Program Files\\App\\unins000.exe\" /SILENT"));
        Assert.Equal(("MsiExec.exe", "/X{GUID}"), r.ParseCommand("MsiExec.exe /X{GUID}"));
        Assert.Equal(("C:\\a.exe", ""), r.ParseCommand("C:\\a.exe"));
    }

    [Fact]
    public void LeftoverTokenizer()
    {
        var tokens = LeftoverScanner.Tokenize("Foo Studio 2024 (x64) - Ltd");
        Assert.Contains("Foo", tokens);
        Assert.Contains("Studio", tokens);
        Assert.DoesNotContain("2024", tokens);
        Assert.DoesNotContain("Ltd", tokens);
    }

    [Fact]
    public void ProgramEnumeratorFake()
    {
        var e = new FakeProgramEnumerator(new[] { new InstalledProgram { DisplayName = "A" } });
        Assert.Single(e.Enumerate());
        if (!OperatingSystem.IsWindows()) Assert.Empty(new RegistryProgramEnumerator().Enumerate());
    }

    [Fact]
    public async Task OptimizerDryRunNeverExecutes()
    {
        var log = new NullLogger();
        var o = new SystemOptimizer(log);
        var (code, _) = await o.RunDismCleanupAsync(dryRun: true);
        Assert.Equal(0, code);
        Assert.Equal(0, o.SetHibernation(false, dryRun: true).Code);
        Assert.Equal(3, log.Entries.Count(e => e.Level == LogLevel.DryRun) + (o.RemoveDriver("oem1.inf", true).Code == 0 ? 1 : 0));
    }
}
