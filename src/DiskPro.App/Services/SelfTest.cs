using System.Diagnostics;
using System.Text;
using DiskPro.Core.Cleaning;
using DiskPro.Core.Models;
using DiskPro.Core.Platform;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.App.Services;

/// <summary>--selftest: exercises core services without UI, writes selftest.txt, exit 0/1.</summary>
public static class SelfTest
{
    public static string OutFile => Path.Combine(DiskPro.App.App.DataDir, "selftest.txt");

    public static int Run()
    {
        var sb = new StringBuilder();
        int failures = 0;
        void Step(string name, Func<string> f)
        {
            var sw = Stopwatch.StartNew();
            try { var r = f(); sb.AppendLine($"[OK]   {name} ({sw.ElapsedMilliseconds} ms) {r}"); }
            catch (Exception ex) { failures++; sb.AppendLine($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}"); DiskPro.App.App.WriteCrash("selftest:" + name, ex); }
        }
        sb.AppendLine($"DiskPro selftest {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"OS={Environment.OSVersion} 64bit={Environment.Is64BitProcess} Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

        Step("管理员检测", () => $"admin={AppHost.IsAdmin()}");
        Step("设置加载", () => $"dryRun={AppHost.Settings.DryRun} dark={AppHost.Settings.DarkTheme}");
        Step("日志", () => { AppHost.Logger.Log(Core.Logging.LogLevel.Info, "自检", "selftest"); return AppHost.Logger.LogDirectory; });
        Step("安全规则", () =>
        {
            var g = AppHost.Guard;
            var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "kernel32.dll");
            if (g.CheckDeletable(sys32, new[] { Path.GetPathRoot(sys32)! }) == null) throw new Exception("System32 未受保护！");
            return $"trees={g.ProtectedTrees.Count} roots={g.ProtectedRoots.Count}";
        });
        Step("C盘容量", () => { var d = DriveInfoSnapshot.For(AppHost.SystemDrive) ?? throw new Exception("无法读取"); return $"{SizeFormatter.Format(d.UsedBytes)}/{SizeFormatter.Format(d.TotalBytes)}"; });
        Step("回收站查询", () => SizeFormatter.Format(AppHost.RecycleBin.QuerySize()));

        // Dry-run junk deletion on a sandbox in %TEMP%
        Step("垃圾扫描+预览删除(%TEMP%)", () =>
        {
            var sandbox = Path.Combine(Path.GetTempPath(), "DiskPro-selftest-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(sandbox);
            try
            {
                for (int i = 0; i < 5; i++) File.WriteAllBytes(Path.Combine(sandbox, $"t{i}.tmp"), new byte[4096]);
                var tempSize = DirectorySizer.GetSize(Path.GetTempPath(), CancellationToken.None);
                var del = new SafeDeleter(AppHost.Guard, new NoRecycleBin(), AppHost.Logger);
                var files = Directory.GetFiles(sandbox);
                var r = del.Delete(new DeleteRequest(files, new[] { sandbox }, DeleteMode.Permanent, DryRun: true));
                if (r.SuccessCount != 5 || Directory.GetFiles(sandbox).Length != 5) throw new Exception("预览模式行为异常");
                return $"Temp={SizeFormatter.Format(tempSize)} dryRunItems={r.SuccessCount} filesKept=5";
            }
            finally { try { Directory.Delete(sandbox, true); } catch { } }
        });
        Step("完整垃圾扫描(只读)", () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var sw = Stopwatch.StartNew(); var times = new List<string>(); string? last = null; long lastMs = 0;
            var prog = new SyncProgress(p => { if (p.Stage != null && p.Stage != last) { if (last != null) times.Add($"{last}:{sw.ElapsedMilliseconds - lastMs}ms"); last = p.Stage; lastMs = sw.ElapsedMilliseconds; } });
            var r = new JunkScanner(AppHost.RecycleBin).Scan(prog, cts.Token);
            var cats = string.Join(", ", r.Categories.Where(c => c.TotalSize > 0).OrderByDescending(c => c.TotalSize).Select(c => $"{c.Name}={SizeFormatter.Format(c.TotalSize)}"));
            return $"categories={r.Categories.Count} total={SizeFormatter.Format(r.TotalSize)}\n         [{cats}]\n         timing: {string.Join(" ", times)}";
        });
        Step("大文件扫描(小目录)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiskPro-lf-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "big.bin"), new byte[300_000]);
                File.WriteAllBytes(Path.Combine(dir, "small.bin"), new byte[1000]);
                var r = new LargeFileScanner(AppHost.Guard).Scan(new LargeFileOptions { Root = dir, MinSizeBytes = 100_000, TopN = 10 });
                if (r.Count != 1) throw new Exception($"期望 1 个结果，实际 {r.Count}");
                return "found=1";
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        Step("已安装程序枚举", () => $"count={new Core.Uninstall.RegistryProgramEnumerator().Enumerate().Count}");
        Step("启动项枚举", () => $"count={new Core.Startup.WindowsStartupStore(AppHost.Logger).List().Count}");

        // ---- real-path diagnostics (read-only / dry-run) ----
        Step("大文件扫描(C:\\ 全盘, 只读)", () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            long seen = 0; var prog = new SyncProgress(p => seen = p.FilesScanned);
            var r = new LargeFileScanner(AppHost.Guard).Scan(new LargeFileOptions { Root = AppHost.SystemDrive, MinSizeBytes = 200L << 20, TopN = 20 }, prog, cts.Token);
            return $"files={seen:N0} found={r.Count} top={(r.Count > 0 ? r[0].Name + " " + SizeFormatter.Format(r[0].Size) : "-")}";
        });
        Step("空间分析(用户目录, 深度3)", () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            var n = new FolderTreeAnalyzer(AppHost.Guard).Analyze(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 3, null, cts.Token);
            return $"size={SizeFormatter.Format(n.Size)} children={n.Children.Count}";
        });
        Step("重复文件(测试目录)", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "DiskPro-dup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(dir, "a")); Directory.CreateDirectory(Path.Combine(dir, "b"));
            try
            {
                var data = new byte[100_000]; new Random(1).NextBytes(data);
                File.WriteAllBytes(Path.Combine(dir, "a", "x.bin"), data); File.WriteAllBytes(Path.Combine(dir, "b", "y.bin"), data);
                var g = new DuplicateFinder(AppHost.Guard).Find(new DuplicateOptions { Root = dir, MinSizeBytes = 1, SkipSystemTrees = false });
                if (g.Count != 1) throw new Exception($"期望 1 组，实际 {g.Count}");
                return "groups=1";
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        });
        Step("系统信息(休眠/页面文件/Windows.old/驱动)", () =>
        {
            var i = new Core.Optimization.SystemOptimizer(AppHost.Logger).Gather();
            return $"hiberfil={SizeFormatter.Format(i.HiberfilBytes)} pagefile={SizeFormatter.Format(i.PagefileBytes)} winold={i.WindowsOldExists} drivers={i.OldDrivers.Count} rp={i.RestorePointNote}";
        });
        Step("系统还原点列表", () => $"count={new Core.Maintenance.RestorePointManager(AppHost.Logger).List().Count}");
        Step("页面文件/TEMP 当前配置", () =>
        {
            var r = new Core.Maintenance.SystemRelocation(AppHost.Logger);
            var dry = r.MoveUserTemp(Path.Combine(Path.GetTempPath(), "DiskPro-dry-temp"), dryRun: true);
            return $"paging=[{string.Join(";", r.CurrentPagingFiles())}] temp={r.CurrentUserTemp()} dryMove={dry.Ok}";
        });
        Step("隐私扫描(只读)", () => { var r = new PrivacyScanner().Scan(); return $"categories={r.Categories.Count} total={SizeFormatter.Format(r.TotalSize)}"; });
        Step("聊天软件分析(只读)", () => { var r = new ChatAppAnalyzer().Analyze(); return $"apps={r.Apps.Count} files={r.Files.Count} total={SizeFormatter.Format(r.Apps.Sum(a => a.Total))}"; });
        Step("空文件夹扫描(Temp)", () => $"count={new Core.Maintenance.EmptyFolderScanner(AppHost.Guard).Scan(Path.GetTempPath()).Count}");
        Step("无效快捷方式扫描", () => $"count={new Core.Maintenance.ShortcutScanner().Scan(Core.Maintenance.ShortcutScanner.DefaultFolders()).Count}");
        Step("自动清理计划任务查询", () => $"exists={new Core.Maintenance.AutoCleanScheduler(AppHost.Logger).Exists()}");
        Step("文件夹搬家预览(下载)", () =>
        {
            var m = new Core.Relocation.KnownFolderMover(AppHost.Logger);
            var plan = m.Plan(Core.Relocation.KnownFolderKind.Downloads, Path.Combine(Path.GetTempPath(), "DiskPro-dry-move"));
            var r = m.Execute(plan, dryRun: true);
            return $"cur={plan.CurrentPath} ok={r.Success} err={r.Error}";
        });
        Step("HTML 报告生成", () => { var h = Core.Reporting.HtmlReport.Build(new Core.Reporting.ReportInput { Drive = DriveInfoSnapshot.For(AppHost.SystemDrive), HealthScore = 80 }); return $"len={h.Length}"; });

        sb.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");
        try { Directory.CreateDirectory(Path.GetDirectoryName(OutFile)!); File.WriteAllText(OutFile, sb.ToString(), Encoding.UTF8); } catch { }
        try { Console.Out.Write(sb.ToString()); } catch { }
        return failures == 0 ? 0 : 1;
    }
}

internal sealed class SyncProgress : IProgress<ScanProgress>
{
    private readonly Action<ScanProgress> _a; public SyncProgress(Action<ScanProgress> a) => _a = a;
    public void Report(ScanProgress v) { lock (this) _a(v); }
}
