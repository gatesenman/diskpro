using DiskPro.Core.Models;
namespace DiskPro.Core.Cleaning;

public sealed class JunkItem
{
    public string Id { get; init; } = "";
    public string Path { get; init; } = "";
    public long Size { get; set; }
    public bool IsDirectory { get; init; }
    public bool Selected { get; set; } = true;
    public string? Note { get; set; }
    /// <summary>Shown for information only; never deleted by DiskPro.</summary>
    public bool ReportOnly { get; init; }
    /// <summary>When true, delete the folder contents only (keep the folder).</summary>
    public bool ContentsOnly { get; init; } = true;
}

public sealed class JunkCategory
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    public List<JunkItem> Items { get; } = new();
    public long TotalSize => Items.Sum(i => i.Size);
    public bool Selected
    {
        get => Items.Count > 0 && Items.All(i => i.Selected);
        set { foreach (var i in Items) i.Selected = value; }
    }
    public bool IsOptional { get; init; }
    public bool IsPrivacy { get; init; }
    public bool ReportOnly { get; init; }
    /// <summary>Selected by default for quick one-click clean (only safe categories).</summary>
    public bool QuickClean => Risk == RiskLevel.Safe && !IsOptional && !IsPrivacy && !ReportOnly && Id != "recycle";
}

public sealed class JunkScanResult
{
    public List<JunkCategory> Categories { get; } = new();
    public long TotalSize => Categories.Sum(c => c.TotalSize);
}
