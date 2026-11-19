using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Logging;
namespace DiskPro.App.ViewModels;

public sealed class LogsViewModel : PageViewModel
{
    public override string Title => "操作日志";
    public override string Subtitle => "所有扫描、删除、优化操作都会记录在这里";
    public ObservableCollection<LogEntry> Entries { get; } = new();
    public string LogDir => AppHost.Logger.LogDirectory;

    public LogsViewModel()
    {
        Reload();
        AppHost.Logger.Logged += e => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Entries.Insert(0, e));
    }
    public ICommand ReloadCommand => new RelayCommand(Reload);
    public ICommand OpenDirCommand => new RelayCommand(() => Dialogs.OpenLocation(LogDir));

    private void Reload()
    {
        var entries = AppHost.Logger.Recent.Reverse().ToList();
        if (entries.Count == 0)
        {
            // load today's file from disk
            try
            {
                var f = AppHost.Logger.LogFiles().FirstOrDefault();
                if (f != null)
                    foreach (var line in File.ReadAllLines(f).AsEnumerable().Reverse().Take(2000))
                    {
                        var p = line.Split('\t');
                        if (p.Length >= 4 && DateTime.TryParse(p[0], out var t) && Enum.TryParse<LogLevel>(p[1], out var lv))
                            entries.Add(new LogEntry(t, lv, p[2], p[3]));
                    }
            }
            catch { }
        }
        Entries.ReplaceWith(entries);
    }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly MainViewModel _main;
    public SettingsViewModel(MainViewModel main) { _main = main; Exclusions = new(AppHost.Settings.Exclusions); }
    public override string Title => "设置";
    public override string Subtitle => "安全策略、外观、扫描偏好与排除列表";
    public bool DryRun { get => AppHost.Settings.DryRun; set { AppHost.Settings.DryRun = value; AppHost.SaveSettings(); OnPropertyChanged(); _main.RaiseDryRun(); } }
    public bool DarkTheme { get => AppHost.Settings.DarkTheme; set { AppHost.Settings.DarkTheme = value; AppHost.SaveSettings(); OnPropertyChanged(); ThemeManager.Apply(value); _main.RaiseDryRun(); } }
    public bool PreferRecycleBin { get => AppHost.Settings.PreferRecycleBin; set { AppHost.Settings.PreferRecycleBin = value; AppHost.SaveSettings(); OnPropertyChanged(); } }
    public bool IncludeOptional { get => AppHost.Settings.IncludeOptionalAppCaches; set { AppHost.Settings.IncludeOptionalAppCaches = value; AppHost.SaveSettings(); OnPropertyChanged(); } }
    public bool RestorePointBeforeRisky { get => AppHost.Settings.RestorePointBeforeRisky; set { AppHost.Settings.RestorePointBeforeRisky = value; AppHost.SaveSettings(); OnPropertyChanged(); } }
    public IEnumerable<string> ProtectedPaths => AppHost.Guard.ProtectedTrees.Concat(AppHost.Guard.ProtectedRoots.Select(r => r + "（仅根目录）"));

    public ObservableCollection<string> Exclusions { get; }
    private string _newPattern = ""; public string NewPattern { get => _newPattern; set => Set(ref _newPattern, value); }
    private string? _selEx; public string? SelectedExclusion { get => _selEx; set => Set(ref _selEx, value); }
    public ICommand AddFolderCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(); if (f != null) AddExclusion(f); });
    public ICommand AddPatternCommand => new RelayCommand(() => { if (!string.IsNullOrWhiteSpace(NewPattern)) { AddExclusion(NewPattern.Trim()); NewPattern = ""; } });
    public ICommand RemoveExclusionCommand => new RelayCommand(p => { if (p is string s) { Exclusions.Remove(s); SaveExclusions(); } });
    private void AddExclusion(string e)
    {
        if (Exclusions.Contains(e, StringComparer.OrdinalIgnoreCase)) { Dialogs.Toast("该项已在排除列表中", DialogKind.Info); return; }
        Exclusions.Add(e); SaveExclusions(); Dialogs.Toast("已添加排除项：" + e);
    }
    private void SaveExclusions() { AppHost.Settings.Exclusions = Exclusions.ToList(); AppHost.SaveSettings(); }
}

public sealed class AboutViewModel : PageViewModel
{
    public override string Title => "关于";
    public override string Subtitle => "专业、安全的 Windows C 盘空间清理工具";
    public string Version => typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
    public string DataDir => DiskPro.App.App.DataDir;
    public System.Windows.Input.ICommand OpenDataCommand => new RelayCommand(() => Dialogs.OpenLocation(DataDir));
    public string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string Os => Environment.OSVersion.VersionString;
    public string Admin => AppHost.IsAdmin() ? "是" : "否（部分功能受限）";
}
