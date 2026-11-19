using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskPro.App.ViewModels;
namespace DiskPro.App.Views;

/// <summary>Squarified treemap of the current folder's children.</summary>
public sealed class TreemapControl : FrameworkElement
{
    public static readonly DependencyProperty NodeProperty = DependencyProperty.Register(
        nameof(Node), typeof(FolderNodeVm), typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TreemapControl)d)._rects.Clear()));

    public FolderNodeVm? Node { get => (FolderNodeVm?)GetValue(NodeProperty); set => SetValue(NodeProperty, value); }
    public event EventHandler<FolderNodeVm>? NodeClicked;

    private readonly List<(Rect R, FolderNodeVm N)> _rects = new();
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x0A,0x84,0xFF), Color.FromRgb(0x30,0xD1,0x58), Color.FromRgb(0xFF,0x9F,0x0A), Color.FromRgb(0xBF,0x5A,0xF2),
        Color.FromRgb(0xFF,0x45,0x3A), Color.FromRgb(0x64,0xD2,0xFF), Color.FromRgb(0xFF,0xD6,0x0A), Color.FromRgb(0x5E,0x5C,0xE6),
        Color.FromRgb(0xAC,0x8E,0x68), Color.FromRgb(0x66,0xD4,0xCF)
    };

    public TreemapControl() { Cursor = Cursors.Hand; ToolTipService.SetInitialShowDelay(this, 200); }

    protected override void OnRender(DrawingContext dc)
    {
        _rects.Clear();
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        var node = Node;
        if (node == null || node.Node.Children.Count == 0 || bounds.Width < 10 || bounds.Height < 10)
        {
            var msg = node == null ? "运行分析后在此显示矩形树图" : "该文件夹没有子文件夹";
            var ft = Text(msg, 13, (Brush?)TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);
            dc.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
            return;
        }
        var items = node.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size).Take(200).ToList();
        var total = items.Sum(i => (double)i.Size);
        if (total <= 0) return;
        var scaled = items.Select(i => (Vm: i, Area: i.Size / total * bounds.Width * bounds.Height)).ToList();
        Squarify(scaled, bounds);
        int idx = 0;
        foreach (var (r, n) in _rects)
        {
            var col = Palette[idx++ % Palette.Length];
            var fill = new SolidColorBrush(col) { Opacity = 0.85 };
            var inner = new Rect(r.X + 1, r.Y + 1, Math.Max(0, r.Width - 2), Math.Max(0, r.Height - 2));
            dc.DrawRoundedRectangle(fill, null, inner, 4, 4);
            if (inner.Width > 60 && inner.Height > 30)
            {
                var name = Text(n.Name, 12, Brushes.White, true);
                name.MaxTextWidth = inner.Width - 8; name.MaxLineCount = 1; name.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(name, new Point(inner.X + 4, inner.Y + 4));
                if (inner.Height > 46)
                {
                    var size = Text(n.SizeText + " · " + n.PercentText, 11, Brushes.White);
                    size.MaxTextWidth = inner.Width - 8; size.MaxLineCount = 1;
                    dc.DrawText(size, new Point(inner.X + 4, inner.Y + 22));
                }
            }
        }
    }

    private FormattedText Text(string s, double size, Brush b, bool bold = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void Squarify(List<(FolderNodeVm Vm, double Area)> items, Rect rect)
    {
        var row = new List<(FolderNodeVm Vm, double Area)>();
        int i = 0;
        while (i < items.Count)
        {
            double side = Math.Min(rect.Width, rect.Height);
            if (side <= 0) break;
            var next = items[i];
            var withNext = row.Append(next).ToList();
            if (row.Count == 0 || Worst(row, side) >= Worst(withNext, side)) { row = withNext; i++; }
            else { rect = LayoutRow(row, rect); row.Clear(); }
        }
        if (row.Count > 0) LayoutRow(row, rect);
    }

    private static double Worst(List<(FolderNodeVm Vm, double Area)> row, double side)
    {
        double sum = row.Sum(r => r.Area), max = row.Max(r => r.Area), min = row.Min(r => r.Area);
        if (sum <= 0 || min <= 0) return double.MaxValue;
        double s2 = side * side, sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }

    private Rect LayoutRow(List<(FolderNodeVm Vm, double Area)> row, Rect rect)
    {
        double sum = row.Sum(r => r.Area);
        if (rect.Width >= rect.Height)
        {
            double w = sum / rect.Height, y = rect.Y;
            foreach (var (vm, a) in row) { double h = a / w; _rects.Add((new Rect(rect.X, y, w, h), vm)); y += h; }
            return new Rect(rect.X + w, rect.Y, Math.Max(0, rect.Width - w), rect.Height);
        }
        else
        {
            double h = sum / rect.Width, x = rect.X;
            foreach (var (vm, a) in row) { double w = a / h; _rects.Add((new Rect(x, rect.Y, w, h), vm)); x += w; }
            return new Rect(rect.X, rect.Y + h, rect.Width, Math.Max(0, rect.Height - h));
        }
    }

    private FolderNodeVm? HitTestNode(Point p) => _rects.FirstOrDefault(r => r.R.Contains(p)).N;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var n = HitTestNode(e.GetPosition(this));
        if (n != null) NodeClicked?.Invoke(this, n);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var n = HitTestNode(e.GetPosition(this));
        ToolTip = n == null ? null : $"{n.Path}\n{n.SizeText}（{n.PercentText}） · {n.FilesText}";
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info) { base.OnRenderSizeChanged(info); InvalidateVisual(); }
}
