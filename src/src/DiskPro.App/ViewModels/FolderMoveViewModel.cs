using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using DiskPro.App.Services;
using DiskPro.Core.Relocation;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public sealed class MoveRowVm : ObservableObject
{
    public KnownFolderKind Kind { get; init; }
    public string DisplayName { get; init; } = "";
    public string CurrentPath { get; set; } = "";
    public long Size { get; set; }
    public string SizeText => SizeFormatter.Format(Size);
    private bool _sel; public bool IsSelected { get => _sel; set => Set(ref _sel, value); }
    public bool OnSystemDrive => CurrentPath.StartsWith(AppHost.SystemDrive, StringComparison.OrdinalIgnoreCase);
    public string LocationText => OnSystemDrive ? "位于系统盘" : "已在其他盘";
    public void Refresh() { OnPropertyChanged(nameof(CurrentPath)); OnPropertyChanged(nameof(SizeText)); OnPropertyChanged(nameof(LocationText)); OnPropertyChanged(nameof(OnSystemDrive)); }
}

public sealed record MoveRecord(KnownFolderKind Kind, string PreviousPath, string NewPath, bool Junction, DateTime Time);

public sealed class FolderMoveViewModel : PageViewModel
{
    public override string Title => "文件夹搬家";
    public override string Subtitle => "把桌面、文档、下载等文件夹迁移到其他磁盘，支持回滚";
    private readonly KnownFolderMover _mover = new(AppHost.Logger);
    public ObservableCollection<MoveRowVm> Folders { get; } = new();
    public ObservableCollection<MoveRecord> History { get; } = new();
    public ObservableCollection<string> Drives { get; } = new();

    private string _target = ""; public string TargetRoot { get => _target; set => Set(ref _target, value); }
    private bool _junction; public bool UseJunction { get => _junction; set => Set(ref _junction, value); }
    private MoveRecord? _selectedHistory; public MoveRecord? SelectedHistory { get => _selectedHistory; set => Set(ref _selectedHistory, value); }

    private static string HistoryFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskPro", "moves.json");

    public FolderMoveViewModel()
    {
        var names = new Dictionary<KnownFolderKind, string>
        {
            [KnownFolderKind.Desktop] = "桌面", [KnownFolderKind.Documents] = "文档", [KnownFolderKind.Downloads] = "下载",
            [KnownFolderKind.Pictures] = "图片", [KnownFolderKind.Videos] = "视频", [KnownFolderKind.Music] = "音乐",
        };
        foreach (var kv in names) Folders.Add(new MoveRowVm { Kind = kv.Key, DisplayName = kv.Value, CurrentPath = _mover.GetCurrentPath(kv.Key) });
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try { if (d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.StartsWith(AppHost.SystemDrive, StringComparison.OrdinalIgnoreCase)) Drives.Add(d.Name); }
                catch { }
            }
        }
        catch { }
        TargetRoot = Drives.Count > 0 ? Path.Combine(Drives[0], "UserData") : "";
        try { if (File.Exists(HistoryFile)) foreach (var r in JsonSerializer.Deserialize<List<MoveRecord>>(File.ReadAllText(HistoryFile)) ?? new()) History.Add(r); } catch { }
    }

    public ICommand MeasureCommand => new AsyncCommand(Measure, () => IsIdle);
    public ICommand BrowseCommand => new RelayCommand(() => { var f = Dialogs.PickFolder(); if (f != null) TargetRoot = f; });
    public ICommand MoveCommand => new AsyncCommand(Move, () => IsIdle && Folders.Any(f => f.IsSelected) && !string.IsNullOrWhiteSpace(TargetRoot));
    public ICommand RollbackCommand => new AsyncCommand(Rollback, () => IsIdle && SelectedHistory != null);

    private Task Measure() => RunAsync("正在统计文件夹大小…", async (ct, _) =>
    {
        foreach (var f in Folders)
        {
            ct.ThrowIfCancellationRequested();
            f.CurrentPath = _mover.GetCurrentPath(f.Kind);
            f.Size = await Task.Run(() => Directory.Exists(f.CurrentPath) ? Core.Scanning.DirectorySizer.GetSize(f.CurrentPath, ct) : 0, ct);
            f.Refresh();
        }
        Status = "统计完成";
    });

    private async Task Move()
    {
        if (Path.GetPathRoot(TargetRoot)?.StartsWith(AppHost.SystemDrive, StringComparison.OrdinalIgnoreCase) == true)
        { Dialogs.Error("目标位置仍在系统盘，请选择其他磁盘。"); return; }
        var sel = Folders.Where(f => f.IsSelected).ToList();
        var msg = string.Join("\n", sel.Select(f => $"• {f.DisplayName}：{f.CurrentPath} → {Path.Combine(TargetRoot, f.Kind.ToString())}（{f.SizeText}）"));
        if (!Dialogs.Confirm($"将迁移以下文件夹（方式：{(UseJunction ? "目录联接 Junction" : "系统已知文件夹重定向")}）：\n\n{msg}\n\n迁移前请关闭正在使用这些文件的程序。继续？", "文件夹搬家")) return;
        await RunAsync("正在搬家…", async (ct, _) =>
        {
            foreach (var f in sel)
            {
                var plan = _mover.Plan(f.Kind, TargetRoot, UseJunction);
                var r = await _mover.ExecuteAsync(plan, AppHost.Settings.DryRun, ct);
                if (!r.Success) { Dialogs.Error($"{f.DisplayName} 迁移失败：{r.Error}"); continue; }
                if (!AppHost.Settings.DryRun)
                {
                    History.Insert(0, new MoveRecord(f.Kind, r.PreviousPath, r.NewPath, UseJunction, DateTime.Now));
                    SaveHistory();
                }
                f.CurrentPath = _mover.GetCurrentPath(f.Kind); f.Refresh();
            }
            Status = AppHost.Settings.DryRun ? "预览完成（未修改）" : "迁移完成，部分程序可能需要注销后生效";
        });
    }

    private async Task Rollback()
    {
        var h = SelectedHistory!;
        if (!Dialogs.Confirm($"将把 {h.Kind} 从 {h.NewPath} 移回 {h.PreviousPath}，继续？", "回滚")) return;
        await RunAsync("正在回滚…", async (ct, _) =>
        {
            var plan = new FolderMovePlan { Kind = h.Kind, CurrentPath = h.PreviousPath, TargetPath = h.NewPath, UseJunction = h.Junction };
            var r = await Task.Run(() => _mover.Rollback(plan, h.PreviousPath, h.Junction), ct);
            if (r.Success) { History.Remove(h); SaveHistory(); Status = "回滚完成"; }
            else Dialogs.Error("回滚失败：" + r.Error);
            foreach (var f in Folders) { f.CurrentPath = _mover.GetCurrentPath(f.Kind); f.Refresh(); }
        });
    }

    private void SaveHistory()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!); File.WriteAllText(HistoryFile, JsonSerializer.Serialize(History.ToList())); } catch { }
    }
}
