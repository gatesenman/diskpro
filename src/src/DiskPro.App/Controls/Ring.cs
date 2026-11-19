using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace DiskPro.App.Controls;

/// <summary>Circular gauge (0-100) with rounded caps; IsSpinning shows an animated scanning arc.</summary>
public sealed class Ring : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Ring), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Ring), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(Ring), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(Ring), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(nameof(IsSpinning), typeof(bool), typeof(Ring), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Ring)d).UpdateSpin()));
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double), typeof(Ring), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public bool IsSpinning { get => (bool)GetValue(IsSpinningProperty); set => SetValue(IsSpinningProperty, value); }
    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }

    private void UpdateSpin()
    {
        if (IsSpinning) BeginAnimation(AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
        else BeginAnimation(AngleProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var t = StrokeThickness; var r = (size - t) / 2; var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(TrackBrush, t), c, r, r);
        var pen = new Pen(Brush, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (IsSpinning) { DrawArc(dc, pen, c, r, Angle, 90); return; }
        var v = Math.Clamp(Value, 0, 100);
        if (v <= 0) return;
        if (v >= 99.99) { dc.DrawEllipse(null, new Pen(Brush, t), c, r, r); return; }
        DrawArc(dc, pen, c, r, 0, 360 * v / 100);
    }

    private static void DrawArc(DrawingContext dc, Pen pen, Point c, double r, double startDeg, double sweepDeg)
    {
        Point P(double deg) { var a = (deg - 90) * Math.PI / 180; return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)); }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(startDeg), false, false);
            ctx.ArcTo(P(startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
