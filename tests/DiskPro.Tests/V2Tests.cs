using DiskPro.Core.Optimization;
using DiskPro.Core.Scanning;
using Xunit;
namespace DiskPro.Tests;

public class V2Tests
{
    [Fact]
    public void ParsesShadowStorageChinese()
    {
        var o = "vssadmin 1.1\r\n\r\n卷影副本存储关联\r\n   卷: (C:)\\\\?\\Volume{a}\\\r\n   卷影副本存储卷: (C:)\\\\?\\Volume{a}\\\r\n   已用卷影副本存储空间: 1.5 GB (1%)\r\n   分配的卷影副本存储空间: 2 GB (1%)\r\n   最大卷影副本存储空间: 191 GB (10%)\r\n\r\n卷影副本存储关联\r\n   卷: (D:)\\\\?\\Volume{b}\\\r\n   已用卷影副本存储空间: 0 字节 (0%)\r\n";
        var (bytes, note) = SystemOptimizer.ParseShadowStorage(o, "C:\\");
        Assert.Equal((long)(1.5 * (1L << 30)), bytes);
        Assert.Contains("191 GB", note);
    }

    [Fact]
    public void ParsesShadowStorageMissing()
    {
        var (bytes, note) = SystemOptimizer.ParseShadowStorage("没有找到项目。", "C:\\");
        Assert.Equal(0, bytes);
        Assert.False(string.IsNullOrEmpty(note));
    }

    [Theory]
    [InlineData("0 字节 (0%)", 0)]
    [InlineData("512 MB (1%)", 512L << 20)]
    [InlineData("1,024 bytes", 1024)]
    public void ParsesSize(string s, long expected) => Assert.Equal(expected, SystemOptimizer.ParseSize(s));

    [Fact]
    public void CloudPlaceholderDetection()
    {
        Assert.True(FastEnum.IsCloudPlaceholder(FileAttributes.Offline));
        Assert.True(FastEnum.IsCloudPlaceholder((FileAttributes)0x400000));
        Assert.False(FastEnum.IsCloudPlaceholder(FileAttributes.Archive));
    }

    [Fact]
    public void ParallelWalkCountsAllFiles()
    {
        using var t = new TempDir();
        for (int d = 0; d < 20; d++) for (int f = 0; f < 15; f++) t.File($"d{d}/s{d % 3}/f{f}.bin", 100);
        long n = 0;
        FastEnum.ParallelWalk(t.Path, _ => true, _ => Interlocked.Increment(ref n), CancellationToken.None);
        Assert.Equal(300, n);
        Assert.Equal(30_000, DirectorySizer.GetSize(t.Path, CancellationToken.None));
    }

    [Fact]
    public void ParallelWalkHonoursDirFilter()
    {
        using var t = new TempDir();
        t.File("keep/a.bin", 10); t.File("skip/b.bin", 10);
        long n = 0;
        FastEnum.ParallelWalk(t.Path, d => !d.EndsWith("skip"), _ => Interlocked.Increment(ref n), CancellationToken.None);
        Assert.Equal(1, n);
    }

    [Fact]
    public void LargeFileScannerFindsTopN()
    {
        using var t = new TempDir();
        t.File("a/big1.bin", 5000); t.File("b/big2.bin", 7000); t.File("c/small.bin", 10);
        var r = new LargeFileScanner(new DiskPro.Core.Safety.SafetyGuard(Array.Empty<string>(), Array.Empty<string>()))
            .Scan(new LargeFileOptions { Root = t.Path, MinSizeBytes = 1000, TopN = 1 });
        Assert.Single(r);
        Assert.Equal(7000, r[0].Size);
    }
}
