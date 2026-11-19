namespace DiskPro.Core.Scanning;

/// <summary>Fast, exception-free directory enumeration (single syscall per entry; skips reparse points, inaccessible dirs, cloud placeholders).</summary>
public static class FastEnum
{
    public static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>True when the file is a OneDrive/cloud placeholder that doesn't occupy local space.</summary>
    public static bool IsCloudPlaceholder(FileAttributes a) => (a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    public static IEnumerable<FileSystemInfo> Entries(string dir)
    {
        IEnumerator<FileSystemInfo> e;
        try { e = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Options).GetEnumerator(); }
        catch { yield break; }
        using (e)
        {
            while (true)
            {
                FileSystemInfo cur;
                try { if (!e.MoveNext()) yield break; cur = e.Current; }
                catch { yield break; }
                yield return cur;
            }
        }
    }

    /// <summary>
    /// Parallel directory walk. <paramref name="enterDir"/> decides whether to descend; <paramref name="onFile"/> is called concurrently.
    /// </summary>
    public static void ParallelWalk(string root, Func<string, bool> enterDir, Action<FileInfo> onFile, CancellationToken ct, int degree = 0)
    {
        if (degree <= 0) degree = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var queue = new System.Collections.Concurrent.BlockingCollection<string>(new System.Collections.Concurrent.ConcurrentStack<string>());
        int pending = 1; queue.Add(root);
        Exception? error = null;
        void Worker()
        {
            try
            {
                foreach (var d in queue.GetConsumingEnumerable(ct))
                {
                    try
                    {
                        foreach (var e in Entries(d))
                        {
                            if (e is DirectoryInfo)
                            {
                                if (enterDir(e.FullName)) { Interlocked.Increment(ref pending); queue.Add(e.FullName); }
                            }
                            else if (e is FileInfo fi) onFile(fi);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                    finally { if (Interlocked.Decrement(ref pending) == 0) queue.CompleteAdding(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; try { queue.CompleteAdding(); } catch { } }
        }
        var threads = Enumerable.Range(0, degree).Select(_ => new Thread(Worker) { IsBackground = true, Name = "DiskPro-walk" }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        ct.ThrowIfCancellationRequested();
        if (error != null) throw error;
    }
}
