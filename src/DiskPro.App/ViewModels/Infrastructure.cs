using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DiskPro.Core.Models;
using DiskPro.Core.Util;
namespace DiskPro.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec; private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }
    public RelayCommand(Action exec, Func<bool>? can = null) : this(_ => exec(), can == null ? null : _ => can()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
    public void Execute(object? p) => _exec(p);
}

public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _exec; private readonly Func<object?, bool>? _can; private bool _running;
    public AsyncCommand(Func<object?, Task> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }
    public AsyncCommand(Func<Task> exec, Func<bool>? can = null) : this(_ => exec(), can == null ? null : _ => can()) { }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? p) => !_running && (_can?.Invoke(p) ?? true);
    public async void Execute(object? p)
    {
        _running = true; CommandManager.InvalidateRequerySuggested();
        try { await _exec(p); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Services.Dialogs.Error("操作失败：" + ex.Message); }
        finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
    }
}

/// <summary>Base for pages that run cancellable background work with progress.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public virtual string Subtitle => "";

    private bool _isBusy; public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    private string _status = "就绪"; public string Status { get => _status; set => Set(ref _status, value); }
    private double _progress; public double Progress { get => _progress; set => Set(ref _progress, value); }
    private bool _indeterminate = true; public bool IsIndeterminate { get => _indeterminate; set => Set(ref _indeterminate, value); }

    private string? _error; public string? ErrorText { get => _error; set { Set(ref _error, value); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    private bool _hasRun; public bool HasRun { get => _hasRun; set => Set(ref _hasRun, value); }
    /// <summary>Called whenever the page becomes visible.</summary>
    public virtual void OnNavigatedTo() { }
    /// <summary>Screenshot mode: optional read-only work to populate the page.</summary>
    public virtual Task? ScreenshotPrepare() => null;

    private CancellationTokenSource? _cts;
    public ICommand CancelCommand => new RelayCommand(() => _cts?.Cancel(), () => IsBusy);

    protected async Task RunAsync(string startStatus, Func<CancellationToken, IProgress<ScanProgress>, Task> work)
    {
        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        IsBusy = true; IsIndeterminate = true; Progress = 0; Status = startStatus; ErrorText = null;
        var throttle = System.Diagnostics.Stopwatch.StartNew(); long lastMs = -1000;
        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Percent is null && p.Stage == null && throttle.ElapsedMilliseconds - lastMs < 120) return;
            lastMs = throttle.ElapsedMilliseconds;
            if (p.Percent is double pc) { IsIndeterminate = false; Progress = pc; }
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(p.Stage)) parts.Add(p.Stage!);
            if (p.FilesScanned > 0) parts.Add($"已扫描 {p.FilesScanned:N0} 个文件 / {SizeFormatter.Format(p.BytesScanned)}");
            if (!string.IsNullOrEmpty(p.CurrentPath) && p.CurrentPath != p.Stage) parts.Add(Shorten(p.CurrentPath!));
            Status = string.Join("  ·  ", parts);
        });
        try { await work(_cts.Token, progress); HasRun = true; }
        catch (OperationCanceledException) { Status = "已取消"; }
        catch (Exception ex) { Status = "出错：" + ex.Message; ErrorText = ex.Message; Services.AppHost.Logger.Log(Core.Logging.LogLevel.Error, Title, ex.ToString()); }
        finally { IsBusy = false; IsIndeterminate = false; _cts.Dispose(); _cts = null; CommandManager.InvalidateRequerySuggested(); }
    }

    private static string Shorten(string p) => p.Length > 90 ? p[..30] + "…" + p[^55..] : p;

    protected static void OnUi(Action a) => Application.Current?.Dispatcher.Invoke(a);
}

public sealed class SelectableItem<T> : ObservableObject
{
    public T Item { get; }
    private bool _selected; public bool IsSelected { get => _selected; set { if (Set(ref _selected, value)) SelectionChanged?.Invoke(); } }
    public Action? SelectionChanged { get; set; }
    public SelectableItem(T item, bool selected = false) { Item = item; _selected = selected; }
}

public static class CollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> c, IEnumerable<T> items)
    {
        c.Clear(); foreach (var i in items) c.Add(i);
    }
}
