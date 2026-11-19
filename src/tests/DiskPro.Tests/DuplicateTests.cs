using DiskPro.Core.Safety;
using DiskPro.Core.Scanning;
using Xunit;
namespace DiskPro.Tests;

public class DuplicateTests
{
    private static SafetyGuard G() => new(Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void FindsIdenticalFilesOnly()
    {
        using var t = new TempDir();
        var a = t.File("a/x.bin", 200_000, 0x11);
        var b = t.File("b/y.bin", 200_000, 0x11);
        t.File("c/z.bin", 200_000, 0x22);           // same size, different content
        t.File("d/w.bin", 150_000, 0x11);           // different size
        var r = new DuplicateFinder(G()).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 1 });
        Assert.Single(r);
        Assert.Equal(2, r[0].Files.Count);
        Assert.Equal(200_000, r[0].WastedBytes);
        Assert.Contains(r[0].Files, f => Path.GetFullPath(f.Path) == Path.GetFullPath(a));
        Assert.Contains(r[0].Files, f => Path.GetFullPath(f.Path) == Path.GetFullPath(b));
    }

    [Fact]
    public void SamePrefixDifferentTailIsNotDuplicate()
    {
        // Partial hash collides (first 64KB identical) but full hash must separate them
        using var t = new TempDir();
        var d1 = new byte[200_000]; var d2 = new byte[200_000];
        Array.Fill(d1, (byte)7); Array.Fill(d2, (byte)7);
        d2[199_999] = 8;
        t.File("p.bin", d1); t.File("q.bin", d2);
        Assert.Equal(DuplicateFinder.HashPartial(Path.Combine(t.Path, "p.bin"), 65536), DuplicateFinder.HashPartial(Path.Combine(t.Path, "q.bin"), 65536));
        var r = new DuplicateFinder(G()).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 1 });
        Assert.Empty(r);
    }

    [Fact]
    public void RespectsMinSize()
    {
        using var t = new TempDir();
        t.File("a", 100, 1); t.File("b", 100, 1);
        Assert.Empty(new DuplicateFinder(G()).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 1000 }));
        Assert.Single(new DuplicateFinder(G()).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 10 }));
    }

    [Fact]
    public void SkipsProtectedSystemTree()
    {
        using var t = new TempDir();
        t.File("sys/a", 5000, 3); t.File("user/a", 5000, 3); t.File("user/b", 5000, 3);
        var g = new SafetyGuard(new[] { Path.Combine(t.Path, "sys") }, Array.Empty<string>());
        var r = new DuplicateFinder(g).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 1 });
        Assert.Single(r);
        Assert.Equal(2, r[0].Files.Count);
        Assert.DoesNotContain(r[0].Files, f => f.Path.Contains("sys"));
    }

    [Fact]
    public void KeepNewestAndOldestSelection()
    {
        using var t = new TempDir();
        var old = t.File("old", 1000, 5, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var mid = t.File("mid", 1000, 5, new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var neu = t.File("new", 1000, 5, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var g = new DuplicateFinder(G()).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 1 }).Single();
        var delKeepNewest = DuplicateFinder.SelectForDeletion(g, keepNewest: true).Select(f => f.Path).ToList();
        Assert.Equal(2, delKeepNewest.Count);
        Assert.DoesNotContain(neu, delKeepNewest);
        var delKeepOldest = DuplicateFinder.SelectForDeletion(g, keepNewest: false).Select(f => f.Path).ToList();
        Assert.DoesNotContain(old, delKeepOldest);
        Assert.Contains(mid, delKeepOldest);
    }

    [Fact]
    public void HashIsStable()
    {
        using var t = new TempDir();
        var a = t.File("a", 70_000, 9);
        Assert.Equal(DuplicateFinder.HashFull(a), DuplicateFinder.HashFull(a));
        Assert.Equal(64, DuplicateFinder.HashFull(a).Length);
    }

    [Fact]
    public void EmptyFilesNotGroupedWhenBelowMin()
    {
        using var t = new TempDir();
        t.File("e1", 0); t.File("e2", 0);
        Assert.Empty(new DuplicateFinder(G()).Find(new DuplicateOptions { Root = t.Path, MinSizeBytes = 1 }));
    }
}
