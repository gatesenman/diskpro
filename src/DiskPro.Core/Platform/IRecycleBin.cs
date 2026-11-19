using System.Runtime.InteropServices;
namespace DiskPro.Core.Platform;

public interface IRecycleBin
{
    /// <summary>Moves a file/dir to the Recycle Bin. Returns false if not supported.</summary>
    bool TryRecycle(string path, out string? error);
    long QuerySize();
    bool Empty(out string? error);
}

public sealed class WindowsRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo;
        public ushort fFlags; public int fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    public bool TryRecycle(string path, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows()) { error = "非 Windows 平台"; return false; }
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE, pFrom = path + "\0\0",
            fFlags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI
        };
        int rc = SHFileOperation(ref op);
        if (rc != 0 || op.fAnyOperationsAborted != 0) { error = $"回收站操作失败 (0x{rc:X})"; return false; }
        return true;
    }

    public long QuerySize()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    public bool Empty(out string? error)
    {
        error = null;
        if (!OperatingSystem.IsWindows()) { error = "非 Windows 平台"; return false; }
        int rc = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
        if (rc != 0 && rc != unchecked((int)0x8000FFFF)) { error = $"清空回收站失败 (0x{rc:X})"; return false; }
        return true;
    }
}

/// <summary>Test double / non-Windows: recycling unsupported, so permanent delete is used.</summary>
public sealed class NoRecycleBin : IRecycleBin
{
    public List<string> Recycled { get; } = new();
    public bool Simulate { get; init; }
    public bool TryRecycle(string path, out string? error)
    {
        if (Simulate)
        {
            error = null; Recycled.Add(path);
            if (File.Exists(path)) File.Delete(path); else if (Directory.Exists(path)) Directory.Delete(path, true);
            return true;
        }
        error = "不支持回收站"; return false;
    }
    public long QuerySize() => 0;
    public bool Empty(out string? error) { error = null; return true; }
}
