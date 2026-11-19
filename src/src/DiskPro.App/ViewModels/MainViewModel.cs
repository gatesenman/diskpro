using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using DiskPro.App.Services;
namespace DiskPro.App.ViewModels;

public sealed class NavItem
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Icon { get; init; } = ""; // Segoe Fluent / MDL2 glyph
    public string Group { get; init; } = "";
    public PageViewModel Page { get; init; } = null!;
    public bool IsFooter { get; init; }
}

public sealed class MainViewModel : ObservableObject
{
    public ObservableCollection<NavItem> Nav { get; } = new();
    public ObservableCollection<NavItem> FooterNav { get; } = new();
    public ICollectionView NavView { get; }
    public JunkViewModel Junk { get; }

    private NavItem? _selected;
    public NavItem? Selected
    {
        get => _selected;
        set { if (value != null && Set(ref _selected, value)) { OnPropertyChanged(nameof(CurrentPage)); if (value.IsFooter) SelectedMain = null; else SelectedFooter = null; value.Page.OnNavigatedTo(); } }
    }
    private NavItem? _selMain; public NavItem? SelectedMain { get => _selMain; set { Set(ref _selMain, value); if (value != null) Selected = value; } }
    private NavItem? _selFooter; public NavItem? SelectedFooter { get => _selFooter; set { Set(ref _selFooter, value); if (value != null) Selected = value; } }
    public PageViewModel? CurrentPage => Selected?.Page;

    public bool DryRun => AppHost.Settings.DryRun;
    public bool IsAdmin { get; } = AppHost.IsAdmin();
    public bool IsDark => AppHost.Settings.DarkTheme;
    public string ThemeGlyph => IsDark ? "\uE706" : "\uE708"; // sun / moon
    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
    public void RaiseDryRun() { OnPropertyChanged(nameof(DryRun)); OnPropertyChanged(nameof(IsDark)); OnPropertyChanged(nameof(ThemeGlyph)); }
    public ICommand ToggleThemeCommand => new RelayCommand(() => { AppHost.Settings.DarkTheme = !AppHost.Settings.DarkTheme; AppHost.SaveSettings(); ThemeManager.Apply(AppHost.Settings.DarkTheme); RaiseDryRun(); });
    public ICommand GoCommand => new RelayCommand(p => { if (p is string k) Navigate(k); });

    public MainViewModel()
    {
        Junk = new JunkViewModel();
        void Add(string group, string key, string title, string icon, PageViewModel page, bool footer = false)
            => (footer ? FooterNav : Nav).Add(new NavItem { Group = group, Key = key, Title = title, Icon = icon, Page = page, IsFooter = footer });
        const string g1 = "清理加速", g2 = "空间管理", g3 = "系统工具";
        Add(g1, "dashboard", "总览", "\uE80F", new DashboardViewModel(this));
        Add(g1, "junk", "系统垃圾清理", "\uE74D", Junk);
        Add(g1, "privacy", "隐私清理", "\uE72E", new PrivacyViewModel());
        Add(g1, "chat", "聊天软件专清", "\uE8BD", new ChatAppViewModel());
        Add(g1, "empty", "空文件夹与快捷方式", "\uE8B7", new EmptyFoldersViewModel());
        Add(g2, "large", "大文件扫描", "\uE7C3", new LargeFilesViewModel());
        Add(g2, "space", "空间分析", "\uEB05", new SpaceViewModel());
        Add(g2, "dup", "重复文件查找", "\uE8C8", new DuplicatesViewModel());
        Add(g2, "uninstall", "软件卸载", "\uE71D", new UninstallViewModel());
        Add(g3, "optimize", "系统深度优化", "\uE945", new OptimizeViewModel());
        Add(g3, "restore", "系统还原点", "\uE777", new RestorePointsViewModel());
        Add(g3, "move", "文件夹搬家", "\uE8DE", new FolderMoveViewModel());
        Add(g3, "sysmove", "系统盘搬家", "\uEDA2", new SystemRelocationViewModel());
        Add(g3, "startup", "启动项管理", "\uE7E8", new StartupViewModel());
        Add(g3, "auto", "自动清理与提醒", "\uE823", new AutoCleanViewModel());
        Add("", "report", "报告与历史", "\uE9F9", new ReportViewModel(this), true);
        Add("", "logs", "操作日志", "\uE8A5", new LogsViewModel(), true);
        Add("", "settings", "设置", "\uE713", new SettingsViewModel(this), true);
        Add("", "about", "关于", "\uE946", new AboutViewModel(), true);
        NavView = CollectionViewSource.GetDefaultView(Nav);
        NavView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));
        SelectedMain = Nav[0];
        AppHost.SettingsChanged += RaiseDryRun;
    }

    public IEnumerable<NavItem> AllNav => Nav.Concat(FooterNav);
    public T? Page<T>() where T : PageViewModel => AllNav.Select(n => n.Page).OfType<T>().FirstOrDefault();

    public void Navigate(string key)
    {
        var n = AllNav.FirstOrDefault(x => x.Key == key);
        if (n == null) return;
        if (n.IsFooter) SelectedFooter = n; else SelectedMain = n;
    }
}
