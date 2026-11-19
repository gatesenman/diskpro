using System.Windows;
namespace DiskPro.App.Services;

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;
        // keep converters; replace theme
        ResourceDictionary? theme = null;
        foreach (var d in dicts.ToList())
            if (d.Source?.OriginalString is string src && (src.EndsWith("Dark.xaml") || src.EndsWith("Light.xaml"))) dicts.Remove(d);
        theme = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        dicts.Insert(0, theme);
    }
}
