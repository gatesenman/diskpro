using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Startup;
namespace DiskPro.App.ViewModels;

public sealed class StartupRowVm : ObservableObject
{
    public StartupEntry E { get; }
    private readonly IStartupStore _store;
    public StartupRowVm(StartupEntry e, IStartupStore s) { E = e; _store = s; }
    public string Name => E.Name;
    public string Command => E.Command;
    public string LocationText => E.Location switch
    {
        StartupLocation.RunHkcu => "注册表 HKCU\\Run", StartupLocation.RunHklm => "注册表 HKLM\\Run",
        StartupLocation.RunOnceHkcu => "注册表 HKCU\\RunOnce", StartupLocation.StartupFolderUser => "启动文件夹（当前用户）",
        _ => "启动文件夹（所有用户）"
    };
    public bool Enabled
    {
        get => E.Enabled;
        set
        {
            if (value == E.Enabled) return;
            if (AppHost.Settings.DryRun) { AppHost.Logger.Log(Core.Logging.LogLevel.DryRun, "启动项", $"[预览] {(value ? "启用" : "禁用")} {Name}"); OnPropertyChanged(); return; }
            if (_store.SetEnabled(E, value, out var err)) { E.Enabled = value; }
            else Dialogs.Error("操作失败：" + err);
            OnPropertyChanged();
        }
    }
}

public sealed class StartupViewModel : PageViewModel
{
    public override string Title => "启动项管理";
    public override string Subtitle => "管理注册表 Run 键与启动文件夹中的开机启动项";
    private readonly IStartupStore _store = new WindowsStartupStore(AppHost.Logger);
    public ObservableCollection<StartupRowVm> Items { get; } = new();
    public ICommand LoadCommand => new AsyncCommand(Load, () => IsIdle);

    private Task Load() => RunAsync("正在读取启动项…", async (ct, _) =>
    {
        var list = await Task.Run(() => _store.List(), ct);
        Items.ReplaceWith(list.Select(e => new StartupRowVm(e, _store)));
        Status = $"共 {list.Count} 个启动项，{list.Count(e => e.Enabled)} 个已启用";
    });
}
