using System.Globalization;
namespace DiskPro.Core.Util;

public static class SizeFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return "-" + Format(-bytes);
        double v = bytes; int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " " + Units[u];
    }

    /// <summary>Parses strings like "500MB", "1.5 GB", "100k".</summary>
    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().ToUpperInvariant().Replace(" ", "");
        long mul = 1;
        string[] suf = { "PB", "TB", "GB", "MB", "KB", "P", "T", "G", "M", "K", "B" };
        long[] mv = { 1L << 50, 1L << 40, 1L << 30, 1L << 20, 1L << 10, 1L << 50, 1L << 40, 1L << 30, 1L << 20, 1L << 10, 1 };
        for (int i = 0; i < suf.Length; i++)
            if (s.EndsWith(suf[i])) { mul = mv[i]; s = s[..^suf[i].Length]; break; }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d < 0) return false;
        bytes = (long)(d * mul);
        return true;
    }
}
