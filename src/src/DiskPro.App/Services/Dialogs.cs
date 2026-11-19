using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DiskPro.Core.Models;
using DiskPro.Core.Util;
namespace DiskPro.App.Services;

public sealed record PreviewRow(string Path, long Size, string? Note = null)
{
    public string SizeText => SizeFormatter.Format(Size);
}

public enum DialogKind { Info, Success, Warning, Error, Question }

/// <summary>In-window modal dialogs and toasts (replaces MessageBox). Falls back to MessageBox when no main window is available.</summary>
public static class Dialogs
{
    public static Window? Owner => Application.Current?.MainWindow;
    private static MainWindow? Host => Application.Current?.MainWindow as MainWindow is { IsLoaded: true } m ? m : null;

    private static (string Glyph, string Brush) Visual(DialogKind k) => k switch
    {
        DialogKind.Success => ("\uE73E", "SuccessBrush"),
        DialogKind.Warning => ("\uE7BA", "WarningBrush"),
        DialogKind.Error => ("\uEA39", "DangerBrush"),
        DialogKind.Question => ("\uE9CE", "AccentBrush"),
        _ => ("\uE946", "AccentBrush"),
    };

    /// <summary>Shows a modal card; returns index of pressed button (or -1 for cancel/Esc).</summary>
    public static int Show(string title, object body, DialogKind kind, params (string Text, string Style)[] buttons)
    {
        var host = Host;
        if (host == null)
        {
            var text = body as string ?? title;
            var r = MessageBox.Show(text, title, buttons.Length > 1 ? MessageBoxButton.YesNo : MessageBoxButton.OK,
                kind == DialogKind.Error ? MessageBoxImage.Error : kind == DialogKind.Warning || kind == DialogKind.Question ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return buttons.Length > 1 ? (r == MessageBoxResult.Yes ? buttons.Length - 1 : -1) : 0;
        }
        int result = -1;
        var frame = new DispatcherFrame();
        var (glyph, brushKey) = Visual(kind);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 12, 0) };
        icon.SetResourceReference(Border.BackgroundProperty, brushKey.Replace("Brush", "SoftBrush"));
        var ig = new TextBlock { Text = glyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        ig.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        ig.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        icon.Child = ig;
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        var tt = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        tt.SetResourceReference(FrameworkElement.StyleProperty, "H2");
        header.Children.Add(tt);

        UIElement content = body is string s
            ? new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = MakeText(s) }
            : (UIElement)body;

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        Border? card = null;
        void Close(int idx) { result = idx; host.CloseOverlay(card!); frame.Continue = false; }
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = buttons[i].Text, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
            b.SetResourceReference(FrameworkElement.StyleProperty, buttons[i].Style);
            b.Click += (_, _) => Close(idx);
            if (i == buttons.Length - 1) b.IsDefault = true;
            btnRow.Children.Add(b);
        }
        var stack = new DockPanel();
        DockPanel.SetDock(header, Dock.Top); DockPanel.SetDock(btnRow, Dock.Bottom);
        stack.Children.Add(header); stack.Children.Add(btnRow); stack.Children.Add(content);
        card = new Border { Child = stack, Width = body is string ? 480 : 760, Padding = new Thickness(24), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        card.SetResourceReference(UIElement.EffectProperty, "ShadowLg");
        host.ShowOverlay(card, () => Close(-1));
        Dispatcher.PushFrame(frame);
        return result;
    }

    private static TextBlock MakeText(string s)
    {
        var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
    }

    public static void Info(string msg, string title = "提示") => Show(title, msg, DialogKind.Info, ("确定", "PrimaryButton"));
    public static void Success(string msg, string title = "完成") => Show(title, msg, DialogKind.Success, ("确定", "PrimaryButton"));
    public static void Error(string msg, string title = "出错了") => Show(title, msg, DialogKind.Error, ("确定", "PrimaryButton"));
    public static bool Confirm(string msg, string title = "确认", bool danger = false)
        => Show(title, msg, danger ? DialogKind.Warning : DialogKind.Question, ("取消", "SecondaryButton"), (danger ? "确认执行" : "确定", danger ? "DangerButton" : "PrimaryButton")) == 1;

    /// <summary>Non-blocking toast in the bottom-right corner.</summary>
    public static void Toast(string msg, DialogKind kind = DialogKind.Success)
    {
        var host = Host; if (host == null) return;
        var (glyph, brushKey) = Visual(kind);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ig = new TextBlock { Text = glyph, FontSize = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        ig.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont"); ig.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        sp.Children.Add(ig);
        var tb = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, VerticalAlignment = VerticalAlignment.Center };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        sp.Children.Add(tb);
        var b = new Border { Child = sp, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 0, 0) };
        b.SetResourceReference(Border.BackgroundProperty, "Surface2Brush"); b.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); b.SetResourceReference(UIElement.EffectProperty, "ShadowSm");
        host.AddToast(b);
    }

    /// <summary>Shows a preview list with sizes; returns the chosen delete mode or null if cancelled.</summary>
    public static DeleteMode? ConfirmDeletion(string title, IReadOnlyList<PreviewRow> rows, bool allowRecycle = true)
    {
        var dry = AppHost.Settings.DryRun;
        var total = rows.Sum(r => r.Size);
        var panel = new DockPanel();
        var sum = MakeText($"共 {rows.Count:N0} 项，合计 {SizeFormatter.Format(total)}。请核对后再确认，受保护的系统路径会被自动跳过。");
        DockPanel.SetDock(sum, Dock.Top); panel.Children.Add(sum);
        if (dry)
        {
            var warn = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0) };
            warn.SetResourceReference(Border.BackgroundProperty, "WarningSoftBrush");
            var wt = new TextBlock { Text = "当前为「仅预览」模式：确认后只记录日志，不会真正删除文件。可在设置中关闭。", TextWrapping = TextWrapping.Wrap };
            wt.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
            warn.Child = wt; DockPanel.SetDock(warn, Dock.Top); panel.Children.Add(warn);
        }
        var grid = new DataGrid { ItemsSource = rows.Take(5000).ToList(), IsReadOnly = true, Height = 300, Margin = new Thickness(0, 12, 0, 0), SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = "路径", Binding = new System.Windows.Data.Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "大小", Binding = new System.Windows.Data.Binding("SizeText"), Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "说明", Binding = new System.Windows.Data.Binding("Note"), Width = 150 });
        panel.Children.Add(grid);
        var preferRecycle = allowRecycle && AppHost.Settings.PreferRecycleBin;
        var buttons = new List<(string, string)> { ("取消", "SecondaryButton") };
        if (!allowRecycle) buttons.Add(("永久删除", "DangerButton"));
        else if (preferRecycle) { buttons.Add(("永久删除", "SecondaryButton")); buttons.Add(("移至回收站", "PrimaryButton")); }
        else { buttons.Add(("移至回收站", "SecondaryButton")); buttons.Add(("永久删除", "DangerButton")); }
        var idx = Show(title, panel, DialogKind.Warning, buttons.ToArray());
        if (idx <= 0) return null;
        var text = buttons[idx].Item1;
        return text == "移至回收站" ? DeleteMode.RecycleBin : DeleteMode.Permanent;
    }

    public static void ShowResult(DeleteResult r, string source = "清理")
    {
        AppHost.Record(source, r);
        var msg = r.DryRun
            ? $"【预览模式】未实际删除任何文件。\n将处理 {r.SuccessCount:N0} 项，可释放约 {SizeFormatter.Format(r.FreedBytes)}；{r.SkippedCount:N0} 项会被跳过。"
            : $"成功处理 {r.SuccessCount:N0} 项，释放 {SizeFormatter.Format(r.FreedBytes)}；跳过 {r.SkippedCount:N0} 项（被占用或受保护）。";
        var skipped = r.Items.Where(i => !i.Success).Take(6).Select(i => $"• {i.Path}\n   {i.Reason}").ToList();
        if (skipped.Count > 0) msg += "\n\n部分跳过项：\n" + string.Join("\n", skipped);
        Show(r.DryRun ? "预览完成" : $"清理完成，释放 {SizeFormatter.Format(r.FreedBytes)}", msg, r.DryRun ? DialogKind.Info : DialogKind.Success, ("确定", "PrimaryButton"));
    }

    public static void OpenLocation(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
            else Toast("路径不存在：" + path, DialogKind.Warning);
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    public static void OpenUrl(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); } catch (Exception ex) { Error(ex.Message); }
    }

    public static string? PickFolder(string? initial = null)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择文件夹", InitialDirectory = initial ?? "" };
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public static string? SaveFile(string defaultName, string filter)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = defaultName, Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }
}
