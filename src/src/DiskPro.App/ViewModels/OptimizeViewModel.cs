using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Optimization;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class OptimizeViewModel : PageViewModel
{
    public override string Title => "系统深度优化";
    public override string Subtitle => "组件存储清理、休眠文件、Windows.old、旧驱动等（需管理员权限）";
    private readonly SystemOptimizer _opt = new(AppHost.Logger);

    private SystemOptInfo? _info; public SystemOptInfo? Info { get => _info; set { Set(ref _info, value); Raise(); } }
    public ObservableCollection<SelectableItem<DriverPackage>> Drivers { get; } = new();
    private string _output = ""; public string Output { get => _output; set => Set(ref _output, value); }

    public string HiberText => Info == null ? "—" : Info.HibernationEnabled ? $"已启用 · hiberfil.sys {SizeFormatter.Format(Info.HiberfilBytes)}" : "未启用（无 hiberfil.sys）";
    public string PageText => Info == null ? "—" : Info.PagefileBytes > 0 ? $"{Info.PagefilePath} · {SizeFormatter.Format(Info.PagefileBytes)}（由系统管理，建议通过“高级系统设置”调整或移至其他盘）" : "未在系统盘发现 pagefile.sys";
    public string WinOldText => Info == null ? "—" : Info.WindowsOldExists ? $"{Info.WindowsOldPath} · {SizeFormatter.Format(Info.WindowsOldBytes)}" : "不存在";
    public string RestoreText => Info?.RestorePointNote ?? "—";
    public bool HiberEnabled => Info?.HibernationEnabled == true;
    public bool WinOldExists => Info?.WindowsOldExists == true;
    private void Raise() { foreach (var n in new[] { nameof(HiberText), nameof(PageText), nameof(WinOldText), nameof(RestoreText), nameof(HiberEnabled), nameof(WinOldExists) }) OnPropertyChanged(n); }

    public ICommand RefreshCommand => new AsyncCommand(Refresh, () => IsIdle);
    public ICommand DismCommand => new AsyncCommand(Dism, () => IsIdle);
    public ICommand HiberOffCommand => new AsyncCommand(() => Hiber(false), () => IsIdle);
    public ICommand HiberOnCommand => new AsyncCommand(() => Hiber(true), () => IsIdle);
    public ICommand WinOldCommand => new AsyncCommand(WinOld, () => IsIdle && WinOldExists);
    public ICommand RemoveDriversCommand => new AsyncCommand(RemoveDrivers, () => IsIdle && Drivers.Any(d => d.IsSelected));
    public ICommand OpenSysPropsCommand => new RelayCommand(() => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesProtection.exe") { UseShellExecute = true }); } catch (Exception ex) { Dialogs.Error(ex.Message); } });
    public ICommand OpenPerfCommand => new RelayCommand(() => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); } catch (Exception ex) { Dialogs.Error(ex.Message); } });

    private Task Refresh() => RunAsync("正在收集系统信息…", async (ct, _) =>
    {
        var info = await _opt.GatherAsync(ct);
        Info = info;
        Drivers.ReplaceWith(info.OldDrivers.Select(d => new SelectableItem<DriverPackage>(d)));
        Status = "信息已更新";
    });

    private Task Dism()
    {
        if (!Dialogs.Confirm("将执行：DISM /Online /Cleanup-Image /StartComponentCleanup\n\n这会清理 WinSxS 中被取代的组件，可能需要数分钟到半小时，期间请勿关机。继续？")) return Task.CompletedTask;
        return RunAsync("正在执行 DISM 组件存储清理（耗时较长）…", async (ct, _) =>
        {
            var (code, output) = await _opt.RunDismCleanupAsync(AppHost.Settings.DryRun, ct);
            Output = output; Status = code == 0 ? "DISM 清理完成" : $"DISM 返回 {code}";
        });
    }

    private Task Hiber(bool enable)
    {
        if (!enable && !Dialogs.Confirm($"关闭休眠将删除 hiberfil.sys（{SizeFormatter.Format(Info?.HiberfilBytes ?? 0)}），同时“快速启动”将不可用。继续？")) return Task.CompletedTask;
        return RunAsync(enable ? "正在启用休眠…" : "正在关闭休眠…", async (ct, _) =>
        {
            var (code, output) = await Task.Run(() => _opt.SetHibernation(enable, AppHost.Settings.DryRun), ct);
            Output = output; Status = code == 0 ? "操作成功" : $"失败（{code}）";
            Info = await _opt.GatherAsync(ct);
        });
    }

    private Task WinOld()
    {
        if (!Dialogs.Confirm($"Windows.old 占用 {SizeFormatter.Format(Info?.WindowsOldBytes ?? 0)}。删除后将无法回退到以前的 Windows 版本。\n\n将调用系统“磁盘清理”完成该操作，继续？")) return Task.CompletedTask;
        return RunAsync("正在清理 Windows.old…", async (ct, _) =>
        {
            var (code, output) = await Task.Run(() => _opt.RemoveWindowsOld(AppHost.Settings.DryRun), ct);
            Output = output; Status = code == 0 ? "已完成" : $"返回 {code}";
        });
    }

    private Task RemoveDrivers()
    {
        var sel = Drivers.Where(d => d.IsSelected).Select(d => d.Item).ToList();
        var list = string.Join("\n", sel.Select(d => $"• {d.PublishedName}  {d.Provider}  {d.ClassName}  {d.Version}"));
        if (!Dialogs.Confirm($"将使用 pnputil 删除以下第三方驱动包（正在使用的驱动会被系统拒绝删除）：\n\n{list}\n\n继续？")) return Task.CompletedTask;
        return RunAsync("正在删除驱动包…", async (ct, _) =>
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in sel)
            {
                ct.ThrowIfCancellationRequested();
                var (code, output) = await Task.Run(() => _opt.RemoveDriver(d.PublishedName, AppHost.Settings.DryRun), ct);
                sb.AppendLine($"{d.PublishedName}: {(code == 0 ? "成功" : "失败 " + code)}  {output}");
            }
            Output = sb.ToString();
            await Refresh();
        });
    }
}
