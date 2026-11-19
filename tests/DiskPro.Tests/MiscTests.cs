using DiskPro.Core.Logging;
using DiskPro.Core.Relocation;
using DiskPro.Core.Settings;
using DiskPro.Core.Startup;
using Xunit;
namespace DiskPro.Tests;

public class MiscTests
{
    [Fact]
    public void SettingsRoundTrip()
    {
        using var t = new TempDir();
        var p = Path.Combine(t.Path, "s.json");
        new AppSettings { DryRun = true, DarkTheme = false, LargeFileTopN = 42 }.Save(p);
        var s = AppSettings.Load(p);
        Assert.True(s.DryRun); Assert.False(s.DarkTheme); Assert.Equal(42, s.LargeFileTopN);
        Assert.False(AppSettings.Load(Path.Combine(t.Path, "missing.json")).DryRun);
    }

    [Fact]
    public void LoggerWritesFile()
    {
        using var t = new TempDir();
        var log = new OperationLogger(t.Path);
        LogEntry? seen = null; log.Logged += e => seen = e;
        log.Log(LogLevel.Delete, "删除", "测试 C:\\a.tmp");
        Assert.NotNull(seen);
        var file = Assert.Single(log.LogFiles());
        Assert.Contains("测试", File.ReadAllText(file));
    }

    [Fact]
    public void StartupFakeToggle()
    {
        var s = new FakeStartupStore();
        s.Items.Add(new StartupEntry { Name = "x", Location = StartupLocation.RunHkcu });
        Assert.True(s.SetEnabled(s.Items[0], false, out _));
        Assert.False(s.List()[0].Enabled);
    }

    [Fact]
    public void FolderMoverRejectsSameTargetAndDryRunDoesNothing()
    {
        using var t = new TempDir();
        var src = t.Dir("Docs"); t.File("Docs/a.txt", 5);
        var mover = new KnownFolderMover(new NullLogger());
        var same = mover.Execute(new FolderMovePlan { Kind = KnownFolderKind.Documents, CurrentPath = src, TargetPath = src }, dryRun: true);
        Assert.False(same.Success);
        var target = Path.Combine(t.Path, "Target", "Documents");
        var dry = mover.Execute(new FolderMovePlan { Kind = KnownFolderKind.Documents, CurrentPath = src, TargetPath = target }, dryRun: true);
        Assert.True(dry.Success);
        Assert.True(File.Exists(Path.Combine(src, "a.txt")));
        Assert.False(Directory.Exists(target));
    }
}
