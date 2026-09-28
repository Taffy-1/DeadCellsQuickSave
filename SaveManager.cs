using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeadCellsUniversalQuickSave;

internal sealed record SnapshotResult(
    string SourcePath,
    string HistoryPath,
    string LatestPath,
    long Bytes,
    string Sha256);

internal sealed class SaveManager
{
    private readonly AppSettings _settings;
    private readonly string _baseDir;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SaveManager(AppSettings settings, string baseDir)
    {
        _settings = settings;
        _baseDir = baseDir;
    }

    public string SnapshotRoot => Path.Combine(_baseDir, "data", "snapshots");
    public string SafetyRoot => Path.Combine(_baseDir, "data", "safety");

    public string? ActiveSavePath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.SaveFilePath))
                return null;

            try
            {
                var path = Path.GetFullPath(_settings.SaveFilePath);
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }
    }

    public string? LatestSnapshotPath
    {
        get
        {
            var save = ActiveSavePath;
            if (save is null)
                return null;

            var slot = Path.GetFileNameWithoutExtension(save);
            var path = Path.Combine(SnapshotRoot, slot, "latest.dat");
            return File.Exists(path) ? path : null;
        }
    }

    public async Task<SnapshotResult> CreateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = ActiveSavePath
                ?? throw new FileNotFoundException("尚未绑定有效的 Dead Cells 存档文件。");

            var bytes = await ReadStableAsync(source, cancellationToken);
            var slot = Path.GetFileNameWithoutExtension(source);
            var slotDir = Path.Combine(SnapshotRoot, slot);
            Directory.CreateDirectory(slotDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var history = Path.Combine(slotDir, stamp + ".dat");
            var latest = Path.Combine(slotDir, "latest.dat");

            await AtomicWriteAsync(history, bytes, cancellationToken);
            await AtomicWriteAsync(latest, bytes, cancellationToken);

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var meta = new
            {
                source,
                capturedAt = DateTimeOffset.Now,
                bytes = bytes.LongLength,
                sha256 = hash
            };

            await AtomicWriteAsync(
                Path.Combine(slotDir, "latest.json"),
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })),
                cancellationToken);

            PruneHistory(slotDir);

            return new SnapshotResult(source, history, latest, bytes.LongLength, hash);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SnapshotResult> CreateAutoCheckpointAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
                throw new FileNotFoundException("自动存档源不存在。", source);

            var bytes = await ReadStableAsync(source, cancellationToken);
            var slot = Path.GetFileNameWithoutExtension(source);
            var slotDir = Path.Combine(SnapshotRoot, slot);
            Directory.CreateDirectory(slotDir);

            var current = Path.Combine(slotDir, "auto-current.dat");
            var previous = Path.Combine(slotDir, "auto-previous.dat");
            var latest = Path.Combine(slotDir, "latest.dat");

            if (File.Exists(current))
            {
                var oldCurrent = await File.ReadAllBytesAsync(current, cancellationToken);
                await AtomicWriteAsync(previous, oldCurrent, cancellationToken);
            }
            else if (!File.Exists(previous) && File.Exists(latest))
            {
                var legacyLatest = await File.ReadAllBytesAsync(latest, cancellationToken);
                await AtomicWriteAsync(previous, legacyLatest, cancellationToken);
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var history = Path.Combine(slotDir, stamp + ".dat");

            await AtomicWriteAsync(history, bytes, cancellationToken);
            await AtomicWriteAsync(current, bytes, cancellationToken);
            await AtomicWriteAsync(latest, bytes, cancellationToken);

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var meta = new
            {
                source,
                channel = "auto",
                capturedAt = DateTimeOffset.Now,
                bytes = bytes.LongLength,
                sha256 = hash
            };

            await AtomicWriteAsync(
                Path.Combine(slotDir, "auto-current.json"),
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })),
                cancellationToken);

            PruneHistory(slotDir);

            return new SnapshotResult(source, history, current, bytes.LongLength, hash);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SnapshotResult> CreateManualCheckpointAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
                throw new FileNotFoundException("手动存档源不存在。", source);

            var bytes = await ReadStableAsync(source, cancellationToken);
            var slot = Path.GetFileNameWithoutExtension(source);
            var slotDir = Path.Combine(SnapshotRoot, slot);
            Directory.CreateDirectory(slotDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var history = Path.Combine(slotDir, stamp + ".dat");
            var manual = Path.Combine(slotDir, "manual.dat");
            var latest = Path.Combine(slotDir, "latest.dat");

            await AtomicWriteAsync(history, bytes, cancellationToken);
            await AtomicWriteAsync(manual, bytes, cancellationToken);
            await AtomicWriteAsync(latest, bytes, cancellationToken);

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var meta = new
            {
                source,
                channel = "manual",
                capturedAt = DateTimeOffset.Now,
                bytes = bytes.LongLength,
                sha256 = hash
            };

            await AtomicWriteAsync(
                Path.Combine(slotDir, "manual.json"),
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })),
                cancellationToken);

            PruneHistory(slotDir);

            return new SnapshotResult(source, history, manual, bytes.LongLength, hash);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SnapshotResult> ResetAutoCheckpointBaselineAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
                throw new FileNotFoundException("重置自动存档基线时源文件不存在。", source);

            var bytes = await ReadStableAsync(source, cancellationToken);
            var slot = Path.GetFileNameWithoutExtension(source);
            var slotDir = Path.Combine(SnapshotRoot, slot);
            Directory.CreateDirectory(slotDir);

            var current = Path.Combine(slotDir, "auto-current.dat");
            var previous = Path.Combine(slotDir, "auto-previous.dat");
            var latest = Path.Combine(slotDir, "latest.dat");
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var history = Path.Combine(slotDir, stamp + "-postrestore.dat");

            await AtomicWriteAsync(history, bytes, cancellationToken);
            await AtomicWriteAsync(current, bytes, cancellationToken);
            await AtomicWriteAsync(previous, bytes, cancellationToken);
            await AtomicWriteAsync(latest, bytes, cancellationToken);

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var meta = new
            {
                source,
                channel = "post-restore-baseline",
                capturedAt = DateTimeOffset.Now,
                bytes = bytes.LongLength,
                sha256 = hash
            };

            await AtomicWriteAsync(
                Path.Combine(slotDir, "auto-current.json"),
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })),
                cancellationToken);

            PruneHistory(slotDir);

            return new SnapshotResult(
                source,
                history,
                current,
                bytes.LongLength,
                hash);
        }
        finally
        {
            _gate.Release();
        }
    }

    public string? SelectPreferredRestoreCheckpoint(string sourcePath)
    {
        try
        {
            var source = Path.GetFullPath(sourcePath);
            var slot = Path.GetFileNameWithoutExtension(source);
            var slotDir = Path.Combine(SnapshotRoot, slot);

            var manual = Path.Combine(slotDir, "manual.dat");
            var previous = Path.Combine(slotDir, "auto-previous.dat");
            var current = Path.Combine(slotDir, "auto-current.dat");
            var latest = Path.Combine(slotDir, "latest.dat");

            var safeCandidates = new[] { manual, previous }
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();

            if (safeCandidates.Length > 0)
                return safeCandidates[0];

            if (File.Exists(current))
                return current;

            if (File.Exists(latest))
                return latest;
        }
        catch
        {
        }

        return null;
    }

    public async Task RestoreCheckpointFileAsync(
        string checkpointPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var checkpoint = Path.GetFullPath(checkpointPath);
            var target = Path.GetFullPath(targetPath);

            if (!File.Exists(checkpoint))
                throw new FileNotFoundException("要恢复的 checkpoint 不存在。", checkpoint);

            var checkpointInfo = new FileInfo(checkpoint);
            if (checkpointInfo.Length <= 0 ||
                checkpointInfo.Length > 128L * 1024 * 1024)
            {
                throw new InvalidDataException(
                    "checkpoint 文件大小异常，已拒绝覆盖游戏存档。");
            }

            if (File.Exists(target))
            {
                var current = await ReadStableAsync(target, cancellationToken);
                Directory.CreateDirectory(SafetyRoot);

                var slot = Path.GetFileNameWithoutExtension(target);
                var safety = Path.Combine(
                    SafetyRoot,
                    $"{slot}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.dat");

                await AtomicWriteAsync(safety, current, cancellationToken);
                PruneSafety();
            }

            var bytes = await File.ReadAllBytesAsync(checkpoint, cancellationToken);
            await AtomicReplaceTargetAsync(target, bytes, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreLatestAsync(
        bool stopGameFirst,
        bool restartAfter,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var target = ActiveSavePath
                ?? throw new FileNotFoundException("尚未绑定有效的 Dead Cells 存档文件。");

            var slot = Path.GetFileNameWithoutExtension(target);
            var latest = Path.Combine(SnapshotRoot, slot, "latest.dat");
            if (!File.Exists(latest))
                throw new FileNotFoundException("当前槽位还没有快速快照。", latest);

            if (stopGameFirst)
                await StopGameAsync(cancellationToken);

            if (File.Exists(target))
            {
                var current = await ReadStableAsync(target, cancellationToken);
                Directory.CreateDirectory(SafetyRoot);
                var safety = Path.Combine(
                    SafetyRoot,
                    $"{slot}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.dat");
                await AtomicWriteAsync(safety, current, cancellationToken);
                PruneSafety();
            }

            var snapshot = await File.ReadAllBytesAsync(latest, cancellationToken);
            await AtomicReplaceTargetAsync(target, snapshot, cancellationToken);

            if (restartAfter)
                StartGame();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestartWithoutRestoreAsync(CancellationToken cancellationToken = default)
    {
        await StopGameAsync(cancellationToken);
        StartGame();
    }

    public Process? FindGameProcess()
    {
        var configured = NormalizePath(_settings.GameExePath);

        foreach (var name in new[] { "deadcells", "deadcells_gl" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                if (configured is null)
                    return process;

                try
                {
                    var actual = NormalizePath(process.MainModule?.FileName);
                    if (actual is not null &&
                        actual.Equals(configured, StringComparison.OrdinalIgnoreCase))
                        return process;
                }
                catch
                {
                }

                process.Dispose();
            }
        }

        return null;
    }

    public IntPtr GetGameWindowHandle()
    {
        using var process = FindGameProcess();
        if (process is null)
            return IntPtr.Zero;

        try
        {
            process.Refresh();
            return process.MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public void OpenSnapshotFolder()
    {
        Directory.CreateDirectory(SnapshotRoot);
        Process.Start(new ProcessStartInfo
        {
            FileName = SnapshotRoot,
            UseShellExecute = true
        });
    }

    private async Task StopGameAsync(CancellationToken cancellationToken)
    {
        var configured = NormalizePath(_settings.GameExePath);
        var victims = new List<Process>();

        foreach (var name in new[] { "deadcells", "deadcells_gl" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                if (configured is null)
                {
                    victims.Add(process);
                    continue;
                }

                try
                {
                    var actual = NormalizePath(process.MainModule?.FileName);
                    if (actual is not null &&
                        actual.Equals(configured, StringComparison.OrdinalIgnoreCase))
                    {
                        victims.Add(process);
                        continue;
                    }
                }
                catch
                {
                }

                process.Dispose();
            }
        }

        foreach (var process in victims)
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await Task.WhenAny(
                        process.WaitForExitAsync(cancellationToken),
                        Task.Delay(1600, cancellationToken));
                }
                catch
                {
                }
            }
        }

        if (victims.Count > 0)
            await Task.Delay(100, cancellationToken);
    }

    private void StartGame()
    {
        if (string.IsNullOrWhiteSpace(_settings.GameExePath) ||
            !File.Exists(_settings.GameExePath))
        {
            return;
        }

        var exe = Path.GetFullPath(_settings.GameExePath);
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true
        });
    }

    private async Task<byte[]> ReadStableAsync(string path, CancellationToken cancellationToken)
    {
        var delay = Math.Clamp(_settings.StableReadDelayMs, 5, 100);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var before = new FileInfo(path);
            var beforeLength = before.Length;
            var beforeWrite = before.LastWriteTimeUtc;

            await Task.Delay(delay, cancellationToken);

            byte[] data;
            await using (var stream = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (stream.Length > 128L * 1024 * 1024)
                    throw new IOException("目标文件异常大，已拒绝快照。");

                data = new byte[checked((int)stream.Length)];
                var offset = 0;

                while (offset < data.Length)
                {
                    var read = await stream.ReadAsync(data.AsMemory(offset), cancellationToken);
                    if (read == 0)
                        break;
                    offset += read;
                }

                if (offset != data.Length)
                    Array.Resize(ref data, offset);
            }

            var after = new FileInfo(path);
            if (beforeLength == after.Length &&
                beforeWrite == after.LastWriteTimeUtc &&
                after.Length == data.LongLength)
            {
                return data;
            }

            await Task.Delay(delay, cancellationToken);
        }

        throw new IOException("游戏正在持续写入存档，无法取得稳定快照。请稍后再按一次 F5。");
    }

    private static async Task AtomicWriteAsync(
        string path,
        byte[] data,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var fs = new FileStream(
                             temp,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await fs.WriteAsync(data, cancellationToken);
                await fs.FlushAsync(cancellationToken);
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
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

    private static async Task AtomicReplaceTargetAsync(
        string target,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(dir);

        var temp = Path.Combine(
            dir,
            "." + Path.GetFileName(target) + ".uqs-" + Guid.NewGuid().ToString("N"));

        try
        {
            await using (var fs = new FileStream(
                             temp,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await fs.WriteAsync(data, cancellationToken);
                await fs.FlushAsync(cancellationToken);
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
            await VerifyRestoredFileAsync(
                target,
                data,
                cancellationToken);
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

    private static async Task VerifyRestoredFileAsync(
        string path,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);

        if (info.Length != expected.LongLength)
        {
            throw new IOException(
                "回档写入后文件长度校验失败，已中止继续加载。");
        }

        byte[] actual;

        await using (var stream = new FileStream(
                         path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite | FileShare.Delete,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            actual = await SHA256.HashDataAsync(
                stream,
                cancellationToken);
        }

        var expectedHash =
            SHA256.HashData(expected);

        if (!CryptographicOperations.FixedTimeEquals(
                actual,
                expectedHash))
        {
            throw new IOException(
                "回档写入后 SHA-256 校验失败，已中止继续加载。");
        }
    }

    private void PruneHistory(string slotDir)
    {
        try
        {
            var keep = Math.Clamp(_settings.HistoryLimit, 3, 200);
            var files = Directory.EnumerateFiles(slotDir, "*.dat")
                .Where(x => !Path.GetFileName(x).Equals("latest.dat", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep)
                .ToArray();

            foreach (var file in files)
                File.Delete(file);
        }
        catch
        {
        }
    }

    private void PruneSafety()
    {
        try
        {
            var keep = Math.Clamp(
                _settings.SafetyBackupLimit,
                3,
                100);

            var files = Directory.EnumerateFiles(SafetyRoot, "*.dat")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(keep)
                .ToArray();

            foreach (var file in files)
                File.Delete(file);
        }
        catch
        {
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}