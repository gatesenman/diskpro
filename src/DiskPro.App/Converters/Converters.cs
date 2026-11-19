using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DiskPro.Core.Util;
namespace DiskPro.App.Converters;

public sealed class BoolToVis : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value == null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class SizeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => SizeFormatter.Format(System.Convert.ToInt64(value ?? 0));
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PercentToWidth : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 2 || values[0] is not double pct || values[1] is not double total) return 0.0;
        return Math.Max(0, Math.Min(total, total * pct / 100.0));
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class RiskBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var key = value?.ToString() ?? "Accent";
        if (!key.EndsWith("Brush")) key += "Brush";
        var b = Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
        if (p as string == "color") return b is SolidColorBrush sc ? sc.Color : Colors.Gray;
        return b;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBool : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

/// <summary>Visible when value (int count or ICollection) &gt; 0; Invert = visible when 0.</summary>
public sealed class CountToVis : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        long n = value switch { int i => i, long l => l, System.Collections.ICollection col => col.Count, null => 0, _ => 1 };
        var vis = n > 0; if (Invert) vis = !vis;
        return vis ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
