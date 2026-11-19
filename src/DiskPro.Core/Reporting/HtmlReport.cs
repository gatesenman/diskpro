using System.Net;
using System.Text;
using DiskPro.Core.Cleaning;
using DiskPro.Core.Models;
using DiskPro.Core.Scanning;
using DiskPro.Core.Util;
namespace DiskPro.Core.Reporting;

public sealed class ReportInput
{
    public DriveInfoSnapshot? Drive { get; init; }
    public JunkScanResult? Junk { get; init; }
    public IReadOnlyList<FileEntry>? LargeFiles { get; init; }
    public IReadOnlyList<CleanRecord>? History { get; init; }
    public DeleteResult? LastClean { get; init; }
    public int HealthScore { get; init; }
    public string MachineName { get; init; } = Environment.MachineName;
    public DateTime Generated { get; init; } = DateTime.Now;
}

public static class HtmlReport
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Build(ReportInput r)
    {
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>DiskPro 清理报告</title>
<style>
body{font-family:"Microsoft YaHei UI","Microsoft YaHei",sans-serif;background:#f3f6fb;color:#1c2333;margin:0;padding:32px}
.wrap{max-width:980px;margin:auto}.hero{background:linear-gradient(135deg,#1f6fff,#6a4cff);color:#fff;border-radius:16px;padding:28px 32px}
.hero h1{margin:0 0 6px;font-size:26px}.cards{display:flex;gap:16px;margin:20px 0}.card{flex:1;background:#fff;border-radius:14px;padding:18px 20px;box-shadow:0 4px 18px rgba(30,60,120,.08)}
.card .v{font-size:26px;font-weight:600;color:#1f6fff}.sec{background:#fff;border-radius:14px;padding:20px 24px;margin:16px 0;box-shadow:0 4px 18px rgba(30,60,120,.08)}
table{width:100%;border-collapse:collapse;font-size:13px}th{text-align:left;color:#6b7590;font-weight:500;border-bottom:1px solid #e5e9f2;padding:8px}
td{padding:8px;border-bottom:1px solid #f0f2f7;word-break:break-all}.r{text-align:right;white-space:nowrap}.bar{height:10px;background:#e6ebf5;border-radius:5px;overflow:hidden}
.bar i{display:block;height:100%;background:linear-gradient(90deg,#1f6fff,#6a4cff)}.muted{color:#8a93a8;font-size:12px}
</style></head><body><div class="wrap">
""");
        sb.Append($"<div class=\"hero\"><h1>DiskPro C盘清理大师 · 清理报告</h1><div>{E(r.MachineName)} · 生成于 {r.Generated:yyyy-MM-dd HH:mm}</div></div>");
        sb.Append("<div class=\"cards\">");
        if (r.Drive != null)
        {
            sb.Append($"<div class=\"card\"><div class=\"muted\">系统盘 {E(r.Drive.Name)}</div><div class=\"v\">{SizeFormatter.Format(r.Drive.FreeBytes)}</div><div class=\"muted\">可用 / 共 {SizeFormatter.Format(r.Drive.TotalBytes)}</div>");
            sb.Append($"<div class=\"bar\" style=\"margin-top:10px\"><i style=\"width:{r.Drive.UsedPercent:0}%\"></i></div></div>");
        }
        sb.Append($"<div class=\"card\"><div class=\"muted\">健康评分</div><div class=\"v\">{r.HealthScore}</div><div class=\"muted\">满分 100</div></div>");
        if (r.Junk != null) sb.Append($"<div class=\"card\"><div class=\"muted\">可清理垃圾</div><div class=\"v\">{SizeFormatter.Format(r.Junk.Categories.Where(c => !c.ReportOnly).Sum(c => c.TotalSize))}</div><div class=\"muted\">{r.Junk.Categories.Count} 个分类</div></div>");
        if (r.History != null) sb.Append($"<div class=\"card\"><div class=\"muted\">累计已释放</div><div class=\"v\">{SizeFormatter.Format(r.History.Where(h => !h.DryRun).Sum(h => h.FreedBytes))}</div><div class=\"muted\">{r.History.Count(h => !h.DryRun)} 次清理</div></div>");
        sb.Append("</div>");

        if (r.LastClean != null)
        {
            sb.Append($"<div class=\"sec\"><h3>本次清理{(r.LastClean.DryRun ? "（预览）" : "")}</h3><p>成功 {r.LastClean.SuccessCount} 项，释放 <b>{SizeFormatter.Format(r.LastClean.FreedBytes)}</b>，跳过 {r.LastClean.SkippedCount} 项。</p>");
            var skipped = r.LastClean.Items.Where(i => !i.Success).Take(100).ToList();
            if (skipped.Count > 0)
            {
                sb.Append("<table><tr><th>跳过的路径</th><th>原因</th></tr>");
                foreach (var s in skipped) sb.Append($"<tr><td>{E(s.Path)}</td><td>{E(s.Reason)}</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }
        if (r.Junk != null)
        {
            sb.Append("<div class=\"sec\"><h3>垃圾分类明细</h3><table><tr><th>分类</th><th>说明</th><th class=\"r\">大小</th></tr>");
            foreach (var c in r.Junk.Categories.Where(c => c.TotalSize > 0).OrderByDescending(c => c.TotalSize))
                sb.Append($"<tr><td>{E(c.Name)}{(c.ReportOnly ? "（仅报告）" : "")}</td><td class=\"muted\">{E(c.Description)}</td><td class=\"r\">{SizeFormatter.Format(c.TotalSize)}</td></tr>");
            sb.Append("</table></div>");
        }
        if (r.LargeFiles is { Count: > 0 })
        {
            sb.Append("<div class=\"sec\"><h3>大文件 Top 50</h3><table><tr><th>路径</th><th class=\"r\">大小</th><th class=\"r\">修改时间</th></tr>");
            foreach (var f in r.LargeFiles.Take(50)) sb.Append($"<tr><td>{E(f.Path)}</td><td class=\"r\">{SizeFormatter.Format(f.Size)}</td><td class=\"r\">{f.LastWriteUtc.ToLocalTime():yyyy-MM-dd}</td></tr>");
            sb.Append("</table></div>");
        }
        if (r.History is { Count: > 0 })
        {
            sb.Append("<div class=\"sec\"><h3>清理历史</h3><table><tr><th>时间</th><th>来源</th><th class=\"r\">项目</th><th class=\"r\">释放</th></tr>");
            foreach (var h in r.History.OrderByDescending(h => h.Time).Take(50))
                sb.Append($"<tr><td>{h.Time:yyyy-MM-dd HH:mm}</td><td>{E(h.Source)}{(h.DryRun ? "（预览）" : "")}</td><td class=\"r\">{h.Items}</td><td class=\"r\">{SizeFormatter.Format(h.FreedBytes)}</td></tr>");
            sb.Append("</table></div>");
        }
        sb.Append("<p class=\"muted\">本报告由 DiskPro 本地生成，不包含任何上传数据。</p></div></body></html>");
        return sb.ToString();
    }

    /// <summary>0-100 score: free space ratio dominates, junk & absolute free space adjust.</summary>
    public static int HealthScore(long total, long free, long junk)
    {
        if (total <= 0) return 0;
        double freePct = 100.0 * free / total;
        double score = Math.Min(100, freePct * 2.5 + 25); // 30% free → 100
        if (free < 10L << 30) score -= 15;   // <10GB free
        if (free < 5L << 30) score -= 15;    // <5GB free
        double junkGb = junk / (double)(1L << 30);
        score -= Math.Min(20, junkGb * 2);
        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }
}
