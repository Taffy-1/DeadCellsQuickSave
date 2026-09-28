namespace DeadCellsUniversalQuickSave;

internal sealed class AutoSaveWatcher : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<string, Task> _onStableWrite;
    private readonly Dictionary<string, CancellationTokenSource> _debounce =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastEmit =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    private FileSystemWatcher? _watcher;

    public AutoSaveWatcher(AppSettings settings, Func<string, Task> onStableWrite)
    {
        _settings = settings;
        _onStableWrite = onStableWrite;
    }

    public string? WatchedDirectory { get; private set; }

    public void Bind(string? directory)
    {
        if (string.Equals(WatchedDirectory, directory, StringComparison.OrdinalIgnoreCase))
            return;

        DisposeWatcher();

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var watcher = new FileSystemWatcher(directory, "user_*.dat")
        {
            IncludeSubdirectories = false,
            NotifyFilter =
                NotifyFilters.LastWrite |
                NotifyFilters.Size |
                NotifyFilters.FileName |
                NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnRenamed;

        _watcher = watcher;
        WatchedDirectory = Path.GetFullPath(directory);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        Schedule(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Schedule(e.FullPath);
    }

    private void Schedule(string path)
    {
        if (!AutoDiscovery.TryGetSlotNumber(path, out _))
            return;

        CancellationTokenSource cts;
        lock (_sync)
        {
            if (_debounce.Remove(path, out var old))
            {
                old.Cancel();
                old.Dispose();
            }

            cts = new CancellationTokenSource();
            _debounce[path] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(
                    Math.Clamp(_settings.AutoSnapshotDebounceMs, 250, 5000),
                    cts.Token);

                lock (_sync)
                {
                    if (_lastEmit.TryGetValue(path, out var last) &&
                        (DateTime.UtcNow - last).TotalMilliseconds <
                        Math.Clamp(_settings.AutoSnapshotMinIntervalMs, 500, 30000))
                    {
                        return;
                    }

                    _lastEmit[path] = DateTime.UtcNow;
                }

                if (File.Exists(path))
                    await _onStableWrite(path);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                lock (_sync)
                {
                    if (_debounce.TryGetValue(path, out var current) &&
                        ReferenceEquals(current, cts))
                    {
                        _debounce.Remove(path);
                    }
                }

                cts.Dispose();
            }
        });
    }

    public void Dispose()
    {
        DisposeWatcher();

        lock (_sync)
        {
            foreach (var cts in _debounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _debounce.Clear();
        }
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        _watcher = null;
        WatchedDirectory = null;
    }
}
