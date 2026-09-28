namespace DeadCellsUniversalQuickSave;

internal static class DataLifecycle
{
    public static void RunStartupCleanup(
        string baseDir,
        AppSettings settings)
    {
        if (!settings.CleanupStaleDataOnStartup)
            return;

        var dataRoot = Path.Combine(baseDir, "data");

        try
        {
            CleanupPendingDeath(dataRoot);
        }
        catch
        {
        }

        try
        {
            CleanupAtomicTemps(dataRoot);
        }
        catch
        {
        }

        try
        {
            TrimDiagnosticLog(
                Path.Combine(
                    dataRoot,
                    "death-detection.log"),
                Math.Clamp(
                    settings.DiagnosticLogMaxKb,
                    64,
                    4096) * 1024L);
        }
        catch
        {
        }
    }

    private static void CleanupPendingDeath(
        string dataRoot)
    {
        var dir = Path.Combine(
            dataRoot,
            "pending-death");

        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     dir,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
            }
        }
    }

    private static void CleanupAtomicTemps(
        string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     dataRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);

            if (!name.Contains(
                    ".tmp-",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var age =
                    DateTime.UtcNow -
                    File.GetLastWriteTimeUtc(file);

                if (age >= TimeSpan.FromMinutes(10))
                    File.Delete(file);
            }
            catch
            {
            }
        }
    }

    internal static void TrimDiagnosticLog(
        string path,
        long maxBytes)
    {
        if (!File.Exists(path) ||
            maxBytes <= 0)
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length <= maxBytes)
            return;

        var keepBytes = Math.Max(
            4096L,
            maxBytes * 3 / 4);

        keepBytes = Math.Min(
            keepBytes,
            info.Length);

        byte[] tail;

        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Seek(
                -keepBytes,
                SeekOrigin.End);

            tail = new byte[
                checked((int)keepBytes)];

            var offset = 0;
            while (offset < tail.Length)
            {
                var read = stream.Read(
                    tail,
                    offset,
                    tail.Length - offset);

                if (read == 0)
                    break;

                offset += read;
            }

            if (offset != tail.Length)
                Array.Resize(ref tail, offset);
        }

        var start = 0;

        while (start < tail.Length &&
               tail[start] != (byte)'\n')
        {
            start++;
        }

        if (start < tail.Length)
            start++;

        var temp = path + ".trim";

        try
        {
            using (var output = new FileStream(
                       temp,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                output.Write(
                    tail,
                    start,
                    tail.Length - start);

                output.Flush(flushToDisk: true);
            }

            File.Move(
                temp,
                path,
                overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
            }
        }
    }
}
