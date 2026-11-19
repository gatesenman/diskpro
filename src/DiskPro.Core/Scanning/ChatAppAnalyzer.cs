using DiskPro.Core.Models;
namespace DiskPro.Core.Scanning;

public enum ChatFileKind { Image, Video, File, Cache, Other }

public sealed record ChatFile(string App, string Account, ChatFileKind Kind, FileEntry Entry);

public sealed class ChatAppSummary
{
    public string App { get; init; } = "";
    public string Root { get; init; } = "";
    public Dictionary<ChatFileKind, long> SizeByKind { get; } = new();
    public long Total => SizeByKind.Values.Sum();
}

public sealed class ChatAppResult
{
    public List<ChatAppSummary> Apps { get; } = new();
    public List<ChatFile> Files { get; } = new();
    public IReadOnlyList<string> Roots => Apps.Select(a => a.Root).ToList();
}

/// <summary>Analyses WeChat / QQ / DingTalk local file storage. Read-only; deletion goes through SafeDeleter.</summary>
public sealed class ChatAppAnalyzer
{
    private readonly string _docs, _roaming;
    public ChatAppAnalyzer(string? documents = null, string? appData = null)
    {
        _docs = documents ?? (Environment.GetEnvironmentVariable("DISKPRO_FAKE_DOCS") is { Length: > 0 } d ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        _roaming = appData ?? (Environment.GetEnvironmentVariable("DISKPRO_FAKE_APPDATA") is { Length: > 0 } a ? a : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    }

    public static ChatFileKind Classify(string relPath)
    {
        var p = "\\" + relPath.Replace('/', '\\').ToLowerInvariant();
        var ext = Path.GetExtension(p);
        if (p.Contains("\\cache\\") || p.Contains("\\temp\\") || p.StartsWith("cache\\")) return ChatFileKind.Cache;
        if (p.Contains("\\video\\") || ext is ".mp4" or ".mov" or ".avi" or ".mkv") return ChatFileKind.Video;
        if (p.Contains("\\image\\") || p.Contains("\\imagefiles\\") || p.Contains("\\img\\") || ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".dat" or ".webp" or ".bmp") return ChatFileKind.Image;
        if (p.Contains("\\file\\") || p.Contains("\\filerecv\\") || p.Contains("\\files\\")) return ChatFileKind.File;
        return ChatFileKind.Other;
    }

    public ChatAppResult Analyze(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new ChatAppResult();
        // WeChat: Documents\WeChat Files\<wxid>\FileStorage\{Image,Video,File,Cache,MsgAttach}
        ScanApp(result, "微信", Path.Combine(_docs, "WeChat Files"), acc => Path.Combine(acc, "FileStorage"), progress, ct, skip: new[] { "All Users", "Applet", "WMPF" });
        // WeChat 4.x: Documents\xwechat_files\<wxid>\{msg\attach,msg\file,msg\video,cache}
        ScanApp(result, "微信 4.x", Path.Combine(_docs, "xwechat_files"), acc => acc, progress, ct, skip: new[] { "all_users", "Backup" });
        // QQ: Documents\Tencent Files\<qq>\{Image,Video,FileRecv}
        ScanApp(result, "QQ", Path.Combine(_docs, "Tencent Files"), acc => acc, progress, ct, skip: new[] { "All Users" });
        // DingTalk: AppData\Roaming\DingTalk\<id>\{ImageFiles,resource_cache}; Documents\DingTalk
        ScanApp(result, "钉钉", Path.Combine(_roaming, "DingTalk"), acc => acc, progress, ct, skip: Array.Empty<string>());
        return result;
    }

    private static void ScanApp(ChatAppResult result, string app, string root, Func<string, string> storage, IProgress<ScanProgress>? progress, CancellationToken ct, string[] skip)
    {
        if (!Directory.Exists(root)) return;
        var summary = new ChatAppSummary { App = app, Root = root };
        IEnumerable<string> accounts;
        try { accounts = Directory.EnumerateDirectories(root).Where(d => !skip.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)).ToList(); }
        catch { return; }
        foreach (var acc in accounts)
        {
            var dir = storage(acc);
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
            catch { continue; }
            long n = 0;
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(f);
                    var kind = Classify(Path.GetRelativePath(dir, f));
                    var name = Path.GetFileName(f);
                    // never offer databases / config
                    if (fi.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase) || name.Contains("Msg", StringComparison.OrdinalIgnoreCase) && fi.Extension is ".db" or ".sqlite") continue;
                    if (kind == ChatFileKind.Other) continue;
                    summary.SizeByKind[kind] = summary.SizeByKind.GetValueOrDefault(kind) + fi.Length;
                    result.Files.Add(new ChatFile(app, Path.GetFileName(acc), kind, new FileEntry(fi.FullName, fi.Length, fi.LastWriteTimeUtc, fi.CreationTimeUtc)));
                    if ((++n & 0xFF) == 0) progress?.Report(new ScanProgress { Stage = $"分析{app}", FilesScanned = result.Files.Count, CurrentPath = f });
                }
                catch { }
            }
        }
        if (summary.Total > 0) result.Apps.Add(summary);
    }
}
