using System.Collections.ObjectModel;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Cleaning;
using DiskPro.Core.Models;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class JunkItemVm : ObservableObject
{
    public JunkItem Model { get; }
    public JunkCategoryVm Parent { get; }
    public JunkItemVm(JunkItem m, JunkCategoryVm parent) { Model = m; Parent = parent; }
    public string Path => Model.Path;
    public string Note => Model.Note ?? "";
    public string SizeText => SizeFormatter.Format(Model.Size);
    public bool IsSelected { get => Model.Selected; set { Model.Selected = value; OnPropertyChanged(); Parent.Refresh(); } }
    public void RaiseSelected() => OnPropertyChanged(nameof(IsSelected));
}

public sealed class JunkCategoryVm : ObservableObject
{
    public JunkCategory Model { get; }
    private readonly Action _changed;
    public ObservableCollection<JunkItemVm> Items { get; } = new();
    public JunkCategoryVm(JunkCategory m, Action changed)
    {
        Model = m; _changed = changed;
        foreach (var i in m.Items) Items.Add(new JunkItemVm(i, this));
        if (m.Risk >= RiskLevel.Medium || m.ReportOnly) foreach (var i in Items) i.Model.Selected = false; // risky = opt-in
        foreach (var i in Items) if (i.Model.ReportOnly) i.Model.Selected = false;
    }
    public string Name => Model.Name;
    public string Description => Model.Description;
    public string RiskText => Model.ReportOnly ? "仅报告" : Model.Risk switch { RiskLevel.Safe => "安全", RiskLevel.Low => "低风险", RiskLevel.Medium => "需确认", _ => "高风险" };
    public string RiskKey => Model.ReportOnly ? "TextTertiary" : Model.Risk switch { RiskLevel.Safe => "Success", RiskLevel.Low => "Accent", RiskLevel.Medium => "Warning", _ => "Danger" };
    public string SizeText => SizeFormatter.Format(Model.TotalSize);
    public long SelectedSize => Model.ReportOnly ? 0 : Model.Items.Where(i => i.Selected && !i.ReportOnly).Sum(i => i.Size);
    public bool CanSelect => !Model.ReportOnly;
    public bool IsError => Model.Id.StartsWith("err:");
    public string ItemCountText => $"{Items.Count} 项";
    public string SelectedSizeText => SizeFormatter.Format(SelectedSize);
    public bool HasItems => Items.Count > 0;
    public bool? IsSelected
    {
        get => Items.Count == 0 ? false : Items.All(i => i.IsSelected) ? true : Items.Any(i => i.IsSelected) ? null : false;
        set { var v = value ?? false; foreach (var i in Items) { i.Model.Selected = v; } foreach (var i in Items) i.RaiseSelected(); Refresh(); }
    }
    private bool _expanded; public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }
    public void Refresh() { OnPropertyChanged(nameof(IsSelected)); OnPropertyChanged(nameof(SelectedSizeText)); _changed(); }
}

public sealed class JunkViewModel : PageViewModel
{
    public override string Title => "系统垃圾清理";
    public override string Subtitle => "临时文件、更新缓存、浏览器缓存（不含 Cookie/密码/历史）等";
    public ObservableCollection<JunkCategoryVm> Categories { get; } = new();
    private JunkScanResult? _result;
    public JunkScanResult? LastResult => _result;
    public void Clear() { _result = null; Categories.Clear(); Recalc(); HasRun = false; }
    public override Task? ScreenshotPrepare() => Categories.Count > 0 ? null : Scan();
    public int VisibleCount => Categories.Count(c => c.HasItems);

    private long _total; public long Total { get => _total; set { Set(ref _total, value); OnPropertyChanged(nameof(TotalText)); } }
    public string TotalText => SizeFormatter.Format(Total);
    private long _selected; public long SelectedTotal { get => _selected; set { Set(ref _selected, value); OnPropertyChanged(nameof(SelectedText)); } }
    public string SelectedText => SizeFormatter.Format(SelectedTotal);

    public bool IncludeOptional { get => AppHost.Settings.IncludeOptionalAppCaches; set { AppHost.Settings.IncludeOptionalAppCaches = value; AppHost.SaveSettings(); OnPropertyChanged(); } }

    public ICommand ScanCommand => new AsyncCommand(Scan, () => IsIdle);
    public ICommand CleanCommand => new AsyncCommand(Clean, () => IsIdle && SelectedTotal > 0);
    public ICommand SelectAllCommand => new RelayCommand(() => { foreach (var c in Categories) c.IsSelected = true; });
    public ICommand SelectNoneCommand => new RelayCommand(() => { foreach (var c in Categories) c.IsSelected = false; });
    public ICommand OpenCommand => new RelayCommand(p => { if (p is JunkItemVm i) Dialogs.OpenLocation(i.Path); });

    private Task Scan() => RunAsync("正在扫描系统垃圾…", async (ct, progress) =>
    {
        var r = await new JunkScanner(AppHost.RecycleBin).ScanAsync(progress, ct, IncludeOptional);
        LoadResult(r);
        Status = $"扫描完成：发现 {TotalText} 可清理";
    });

    public void LoadResult(JunkScanResult r)
    {
        _result = r;
        Categories.ReplaceWith(r.Categories.Select(c => new JunkCategoryVm(c, Recalc)));
        Recalc();
    }

    private void Recalc()
    {
        Total = Categories.Sum(c => c.Model.TotalSize);
        SelectedTotal = Categories.Sum(c => c.SelectedSize);
        OnPropertyChanged(nameof(VisibleCount));
    }

    private async Task Clean()
    {
        if (_result == null) return;
        var selected = Categories.SelectMany(c => c.Items.Where(i => i.IsSelected).Select(i => (c, i))).ToList();
        if (selected.Count == 0) return;
        var rows = selected.Select(x => new PreviewRow(x.i.Path, x.i.Model.Size, x.c.Name)).ToList();
        var mode = Dialogs.ConfirmDeletion("确认清理系统垃圾", rows);
        if (mode == null) return;

        await RunAsync("正在清理…", async (ct, progress) =>
        {
            var r = await Task.Run(() => AppHost.Cleaner.Clean(_result!, mode.Value, AppHost.Settings.DryRun, progress, ct), ct);
            Dialogs.ShowResult(r, "系统垃圾清理");
            Status = r.DryRun ? "预览完成" : $"已释放 {SizeFormatter.Format(r.FreedBytes)}";
        });
        if (!AppHost.Settings.DryRun) await Scan();
    }
}
