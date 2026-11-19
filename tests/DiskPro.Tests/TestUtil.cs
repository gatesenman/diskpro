namespace DiskPro.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diskpro-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string rel, int size, byte fill = 0x41, DateTime? mtime = null)
    {
        var p = System.IO.Path.Combine(Path, rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        var data = new byte[size]; Array.Fill(data, fill);
        System.IO.File.WriteAllBytes(p, data);
        if (mtime != null) System.IO.File.SetLastWriteTimeUtc(p, mtime.Value);
        return p;
    }
    public string File(string rel, byte[] data)
    {
        var p = System.IO.Path.Combine(Path, rel);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, data); return p;
    }
    public string Dir(string rel) { var p = System.IO.Path.Combine(Path, rel); Directory.CreateDirectory(p); return p; }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
