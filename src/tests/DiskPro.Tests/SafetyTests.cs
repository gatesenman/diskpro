using DiskPro.Core.Cleaning;
using DiskPro.Core.Logging;
using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Safety;
using DiskPro.Core.Util;
using Xunit;
namespace DiskPro.Tests;

public class SafetyTests
{
    private static (SafetyGuard guard, TempDir t, string sys32, string winRoot, string pf) Setup()
    {
        var t = new TempDir();
        var win = t.Dir("Windows");
        var sys32 = t.Dir("Windows/System32");
        var pf = t.Dir("Program Files");
        var guard = new SafetyGuard(
            new[] { sys32, Path.Combine(win, "WinSxS") },
            new[] { win, pf, t.Path });
        return (guard, t, sys32, win, pf);
    }

    [Fact]
    public void BlocksProtectedTree()
    {
        var (g, t, sys32, _, _) = Setup(); using var _t = t;
        Assert.NotNull(g.CheckDeletable(Path.Combine(sys32, "kernel32.dll"), new[] { t.Path }));
        Assert.NotNull(g.CheckDeletable(Path.Combine(sys32, "drivers", "x.sys"), new[] { t.Path }));
    }

    [Fact]
    public void BlocksProtectedRootButAllowsChildren()
    {
        var (g, t, _, win, pf) = Setup(); using var _t = t;
        Assert.NotNull(g.CheckDeletable(pf, new[] { t.Path }));
        Assert.NotNull(g.CheckDeletable(win, new[] { t.Path }));
        Assert.Null(g.CheckDeletable(Path.Combine(win, "Temp", "a.tmp"), new[] { win }));
    }

    [Fact]
    public void BlocksOutsideScope()
    {
        var (g, t, _, win, _) = Setup(); using var _t = t;
        var file = t.File("Other/x.bin", 10);
        var reason = g.CheckDeletable(file, new[] { Path.Combine(win, "Temp") });
        Assert.Equal("超出所选清理范围", reason);
    }

    [Fact]
    public void BlocksEmptyScope()
    {
        var (g, t, _, _, _) = Setup(); using var _t = t;
        Assert.NotNull(g.CheckDeletable(t.File("a", 1), Array.Empty<string>()));
    }

    [Fact]
    public void BlocksDriveRoot()
    {
        var g = new SafetyGuard(Array.Empty<string>(), Array.Empty<string>());
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.NotNull(g.CheckDeletable(root, new[] { root }));
    }

    [Fact]
    public void PrefixSiblingIsNotTreatedAsChild()
    {
        // "System32Backup" must not be considered under "System32"
        var (g, t, sys32, win, _) = Setup(); using var _t = t;
        var sibling = Path.Combine(win, "System32Backup", "f.txt");
        Assert.False(PathUtil.IsUnder(sibling, sys32));
        Assert.Null(g.CheckDeletable(sibling, new[] { win }));
    }

    [Fact]
    public void DotDotTraversalIsNormalized()
    {
        var (g, t, sys32, win, _) = Setup(); using var _t = t;
        var sneaky = Path.Combine(win, "Temp", "..", "System32", "evil.dll");
        Assert.NotNull(g.CheckDeletable(sneaky, new[] { win }));
    }

    [Fact]
    public void DeleterDryRunDoesNotDelete()
    {
        var (g, t, _, win, _) = Setup(); using var _t = t;
        var f = t.File("Windows/Temp/a.tmp", 100);
        var log = new NullLogger();
        var del = new SafeDeleter(g, new NoRecycleBin(), log);
        var r = del.Delete(new DeleteRequest(new[] { f }, new[] { Path.Combine(win, "Temp") }, DeleteMode.Permanent, DryRun: true));
        Assert.True(File.Exists(f));
        Assert.Equal(1, r.SuccessCount);
        Assert.Equal(100, r.FreedBytes);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.DryRun);
    }

    [Fact]
    public void DeleterDeletesInScopeAndSkipsProtected()
    {
        var (g, t, sys32, win, _) = Setup(); using var _t = t;
        var ok = t.File("Windows/Temp/a.tmp", 50);
        var bad = t.File("Windows/System32/k.dll", 50);
        var del = new SafeDeleter(g, new NoRecycleBin(), new NullLogger());
        var r = del.Delete(new DeleteRequest(new[] { ok, bad }, new[] { win }, DeleteMode.Permanent, false));
        Assert.False(File.Exists(ok));
        Assert.True(File.Exists(bad));
        Assert.Equal(1, r.SuccessCount);
        Assert.Equal(1, r.SkippedCount);
        Assert.Contains("受保护", r.Items.Single(i => !i.Success).Reason);
    }

    [Fact]
    public void DeleterUsesRecycleBinWhenAvailable()
    {
        var (g, t, _, win, _) = Setup(); using var _t = t;
        var f = t.File("Windows/Temp/r.tmp", 10);
        var bin = new NoRecycleBin { Simulate = true };
        var del = new SafeDeleter(g, bin, new NullLogger());
        del.Delete(new DeleteRequest(new[] { f }, new[] { win }, DeleteMode.RecycleBin, false));
        Assert.Single(bin.Recycled);
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void DeleterHandlesDirectories()
    {
        var (g, t, _, win, _) = Setup(); using var _t = t;
        t.File("Windows/Temp/sub/a.tmp", 10);
        t.File("Windows/Temp/sub/deep/b.tmp", 20);
        var dir = Path.Combine(win, "Temp", "sub");
        var del = new SafeDeleter(g, new NoRecycleBin(), new NullLogger());
        var r = del.Delete(new DeleteRequest(new[] { dir }, new[] { win }, DeleteMode.Permanent, false));
        Assert.False(Directory.Exists(dir));
        Assert.Equal(30, r.FreedBytes);
    }

    [Fact]
    public void DeleterReportsMissingFile()
    {
        var (g, t, _, win, _) = Setup(); using var _t = t;
        var del = new SafeDeleter(g, new NoRecycleBin(), new NullLogger());
        var r = del.Delete(new DeleteRequest(new[] { Path.Combine(win, "Temp", "nope") }, new[] { win }, DeleteMode.Permanent, false));
        Assert.Equal("文件不存在", r.Items[0].Reason);
    }

    [Fact]
    public void DeleterDoesNotFollowSymlinks()
    {
        if (OperatingSystem.IsWindows()) return; // symlink creation needs privilege
        var (g, t, _, win, _) = Setup(); using var _t = t;
        var precious = t.File("Precious/keep.txt", 10);
        var link = Path.Combine(win, "Temp", "link");
        Directory.CreateDirectory(Path.Combine(win, "Temp"));
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(precious)!);
        var del = new SafeDeleter(g, new NoRecycleBin(), new NullLogger());
        del.Delete(new DeleteRequest(new[] { link }, new[] { win }, DeleteMode.Permanent, false));
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void DefaultGuardHasRules()
    {
        var g = SafetyGuard.CreateDefault();
        Assert.NotNull(g); // must not throw on any OS
    }
}
