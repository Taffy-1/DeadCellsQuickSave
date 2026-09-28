namespace DeadCellsUniversalQuickSave;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = SelfTest.Run();
            return;
        }

        var paths = AppPaths.Resolve(args);
        paths.EnsureDataDirectory();
        paths.MigrateLegacyPortableData();

        var settings = AppSettings.Load(
            paths.DataDir);

        DataLifecycle.RunStartupCleanup(
            paths.DataDir,
            settings);

        FirstRunSetup.TryAutoConfigure(
            settings,
            paths.DataDir,
            paths.InstallDir);

        var backgroundOnly = args.Contains(
            "--background",
            StringComparer.OrdinalIgnoreCase);

        var launchGame =
            !backgroundOnly ||
            args.Contains(
                "--launch-game",
                StringComparer.OrdinalIgnoreCase);

        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(
            true,
            @"Local\DeadCellsUniversalQuickSave.Singleton",
            out var createdNew);

        if (!createdNew)
        {
            if (launchGame)
            {
                GameLauncher.TryLaunch(
                    settings,
                    paths.DataDir,
                    paths.InstallDir,
                    out _);
            }

            return;
        }

        if (launchGame)
        {
            GameLauncher.TryLaunch(
                settings,
                paths.DataDir,
                paths.InstallDir,
                out _);
        }

        Application.Run(
            new ModHostForm(
                settings,
                paths.DataDir,
                paths.InstallDir));
        GC.KeepAlive(mutex);
    }
}

internal static class SelfTest
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "DC-UQS-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var save = Path.Combine(root, "user_0.dat");
            File.WriteAllText(save, "checkpoint-A");

            var settings = new AppSettings
            {
                SaveFilePath = save,
                GameExePath = "",
                HistoryLimit = 4
            };

            var manager = new SaveManager(settings, root);
            var snap = manager.CreateSnapshotAsync().GetAwaiter().GetResult();

            if (!File.Exists(snap.LatestPath))
                throw new InvalidOperationException("Snapshot not created.");

            File.WriteAllText(save, "checkpoint-B");
            manager.RestoreLatestAsync(false, false).GetAwaiter().GetResult();

            if (File.ReadAllText(save) != "checkpoint-A")
                throw new InvalidOperationException("Restore mismatch.");

            File.WriteAllText(save, "scene-1");
            manager.CreateAutoCheckpointAsync(save).GetAwaiter().GetResult();

            File.WriteAllText(save, "scene-2");
            manager.CreateAutoCheckpointAsync(save).GetAwaiter().GetResult();

            File.WriteAllText(save, "death-state");
            manager.CreateAutoCheckpointAsync(save).GetAwaiter().GetResult();

            var preferred = manager.SelectPreferredRestoreCheckpoint(save);
            if (preferred is null)
                throw new InvalidOperationException("Preferred checkpoint missing.");

            if (File.ReadAllText(preferred) != "scene-2")
                throw new InvalidOperationException(
                    "Rolling checkpoint did not preserve the pre-death state.");

            manager.RestoreCheckpointFileAsync(preferred, save)
                .GetAwaiter()
                .GetResult();

            if (File.ReadAllText(save) != "scene-2")
                throw new InvalidOperationException(
                    "Preferred checkpoint restore mismatch.");

            manager.ResetAutoCheckpointBaselineAsync(save)
                .GetAwaiter()
                .GetResult();

            var slotDir = Path.Combine(
                manager.SnapshotRoot,
                "user_0");

            if (File.ReadAllText(Path.Combine(slotDir, "auto-current.dat")) != "scene-2" ||
                File.ReadAllText(Path.Combine(slotDir, "auto-previous.dat")) != "scene-2")
            {
                throw new InvalidOperationException(
                    "Post-restore auto baseline reset mismatch.");
            }

            TestDeathWriteDetector(settings);
            TestSettingsMigration(root);
            TestSlotSelection(root);
            TestFirstRunAutoDiscovery(root);
            TestAppPathsMigration(root);
            TestDataLifecycle(root);
            TestSafetyRetention(root);

            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "selftest-error.log"),
                    ex.ToString());
            }
            catch
            {
            }

            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void TestSettingsMigration(string root)
    {
        var dir = Path.Combine(root, "settings-migration");
        Directory.CreateDirectory(dir);

        var settingsPath = Path.Combine(dir, "settings.json");

        File.WriteAllText(
            settingsPath,
            """
            {
              "HistoryLimit": 1,
              "StartMinimized": true,
              "AutoRestartAfterRestore": true,
              "HeadlessMode": true,
              "DeathAutoRestoreDelayMs": 10000
            }
            """);

        var migrated = AppSettings.Load(dir);

        if (migrated.ConfigVersion !=
            AppSettings.CurrentConfigVersion)
        {
            throw new InvalidOperationException(
                "Settings schema migration failed.");
        }

        if (migrated.HistoryLimit != 5)
        {
            throw new InvalidOperationException(
                "Settings normalization failed.");
        }

        var rewritten =
            File.ReadAllText(settingsPath);

        foreach (var legacy in new[]
                 {
                     "StartMinimized",
                     "AutoRestartAfterRestore",
                     "HeadlessMode",
                     "DeathAutoRestoreDelayMs"
                 })
        {
            if (rewritten.Contains(
                    legacy,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Legacy setting was not removed: {legacy}");
            }
        }
    }

    private static void TestSlotSelection(string root)
    {
        var dir = Path.Combine(root, "slot-selection");
        Directory.CreateDirectory(dir);

        var slot0 = Path.Combine(dir, "user_0.dat");
        var slot2 = Path.Combine(dir, "user_2.dat");

        File.WriteAllText(slot0, "slot-0");
        File.WriteAllText(slot2, "slot-2");

        var settings = new AppSettings
        {
            SaveFilePath = slot0,
            PinnedSlotNumber = 2,
            LastActiveSlotNumber = 0
        };

        var discovery =
            new AutoDiscovery(
                settings,
                root,
                root);

        var active =
            discovery.DiscoverActiveSlot();

        if (active is null ||
            active.Number != 2 ||
            !string.Equals(
                active.Path,
                Path.GetFullPath(slot2),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Pinned slot selection failed.");
        }

        settings.PinnedSlotNumber = -1;
        settings.FollowWrittenSlot = true;
        settings.SaveFilePath = slot0;

        active = discovery.DiscoverActiveSlot();

        if (active is null ||
            active.Number != 0)
        {
            throw new InvalidOperationException(
                "Auto slot should preserve the current valid slot until a real write changes it.");
        }
    }

    private static void TestFirstRunAutoDiscovery(
        string root)
    {
        var parent = Path.Combine(
            root,
            "first-run");

        var appDir = Path.Combine(parent, "QuickSave");
        var dataDir = Path.Combine(parent, "LocalData");
        var gameDir = Path.Combine(parent, "Dead Cells");
        var saveDir = Path.Combine(parent, "Saves");

        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(saveDir);

        var exe = Path.Combine(
            gameDir,
            "deadcells.exe");

        var save = Path.Combine(
            saveDir,
            "user_0.dat");

        File.WriteAllBytes(exe, [0x4D, 0x5A]);
        File.WriteAllText(save, "slot");

        var settings = new AppSettings
        {
            SaveFilePath = save
        };

        var ready =
            FirstRunSetup.TryAutoConfigure(
                settings,
                dataDir,
                appDir);

        if (!ready ||
            !settings.FirstRunCompleted ||
            string.IsNullOrWhiteSpace(settings.GameExePath) ||
            !File.Exists(settings.GameExePath))
        {
            throw new InvalidOperationException(
                $"First-run auto discovery failed: ready={ready}, completed={settings.FirstRunCompleted}, game={settings.GameExePath}, save={settings.SaveFilePath}");
        }

        var isolatedDiscovery = new AutoDiscovery(
            new AppSettings
            {
                SaveFilePath = save
            },
            dataDir,
            appDir);

        var localExe =
            isolatedDiscovery.DetectGameExe(
                includeRunningProcess: false);

        if (!string.Equals(
                localExe,
                Path.GetFullPath(exe),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Adjacent install discovery failed: actual={localExe}, expected={Path.GetFullPath(exe)}");
        }
    }

    private static void TestAppPathsMigration(string root)
    {
        var parent = Path.Combine(root, "path-migration");
        var installDir = Path.Combine(parent, "Install");
        var dataDir = Path.Combine(parent, "LocalData");

        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(dataDir);

        File.WriteAllText(
            Path.Combine(installDir, "settings.json"),
            "{\"SaveFilePath\":\"legacy\"}");

        var legacySnapshot = Path.Combine(
            installDir,
            "data",
            "snapshots",
            "user_0");

        Directory.CreateDirectory(legacySnapshot);

        File.WriteAllText(
            Path.Combine(legacySnapshot, "manual.dat"),
            "legacy-checkpoint");

        var targetSnapshot = Path.Combine(
            dataDir,
            "data",
            "snapshots",
            "user_0");

        Directory.CreateDirectory(targetSnapshot);

        File.WriteAllText(
            Path.Combine(targetSnapshot, "manual.dat"),
            "newer-existing-checkpoint");

        var paths = new AppPaths(
            installDir,
            dataDir,
            PortableMode: false);

        paths.MigrateLegacyPortableData();

        if (!File.Exists(Path.Combine(dataDir, "settings.json")))
        {
            throw new InvalidOperationException(
                "Legacy settings migration failed.");
        }

        if (File.ReadAllText(
                Path.Combine(targetSnapshot, "manual.dat")) !=
            "newer-existing-checkpoint")
        {
            throw new InvalidOperationException(
                "Migration overwrote an existing target checkpoint.");
        }

        if (!File.Exists(
                Path.Combine(legacySnapshot, "manual.dat")))
        {
            throw new InvalidOperationException(
                "Migration deleted the legacy source checkpoint.");
        }

        if (!File.Exists(
                Path.Combine(
                    dataDir,
                    "migration-from-portable-v1.done")))
        {
            throw new InvalidOperationException(
                "Migration marker was not created.");
        }
    }

    private static void TestDataLifecycle(string root)
    {
        var dir = Path.Combine(root, "lifecycle");
        var data = Path.Combine(dir, "data");
        var pending = Path.Combine(data, "pending-death");
        var snapshots = Path.Combine(data, "snapshots", "user_0");

        Directory.CreateDirectory(pending);
        Directory.CreateDirectory(snapshots);

        var pinned = Path.Combine(pending, "stale.dat");
        File.WriteAllText(pinned, "stale");

        var temp = Path.Combine(
            snapshots,
            "auto-current.dat.tmp-deadbeef");

        File.WriteAllText(temp, "temp");
        File.SetLastWriteTimeUtc(
            temp,
            DateTime.UtcNow.AddMinutes(-20));

        var log = Path.Combine(
            data,
            "death-detection.log");

        Directory.CreateDirectory(data);
        File.WriteAllText(
            log,
            new string('x', 96 * 1024) +
            Environment.NewLine +
            "tail");

        var settings = new AppSettings
        {
            DiagnosticLogMaxKb = 64,
            CleanupStaleDataOnStartup = true
        };

        DataLifecycle.RunStartupCleanup(
            dir,
            settings);

        if (File.Exists(pinned))
        {
            throw new InvalidOperationException(
                "Pending-death cleanup failed.");
        }

        if (File.Exists(temp))
        {
            throw new InvalidOperationException(
                "Atomic temp cleanup failed.");
        }

        if (new FileInfo(log).Length >
            settings.DiagnosticLogMaxKb * 1024L)
        {
            throw new InvalidOperationException(
                "Diagnostic log trim failed.");
        }
    }

    private static void TestSafetyRetention(string root)
    {
        var dir = Path.Combine(root, "safety-retention");
        Directory.CreateDirectory(dir);

        var save = Path.Combine(dir, "user_0.dat");
        var checkpoint = Path.Combine(dir, "checkpoint.dat");

        File.WriteAllText(checkpoint, "checkpoint");

        var settings = new AppSettings
        {
            SaveFilePath = save,
            SafetyBackupLimit = 3
        };

        var manager =
            new SaveManager(settings, dir);

        for (var i = 0; i < 6; i++)
        {
            File.WriteAllText(
                save,
                $"live-{i}");

            manager.RestoreCheckpointFileAsync(
                    checkpoint,
                    save)
                .GetAwaiter()
                .GetResult();
        }

        var count = Directory.Exists(manager.SafetyRoot)
            ? Directory.EnumerateFiles(
                    manager.SafetyRoot,
                    "*.dat")
                .Count()
            : 0;

        if (count > 3)
        {
            throw new InvalidOperationException(
                $"Safety retention failed: {count} files remain.");
        }
    }

    private static void TestDeathWriteDetector(AppSettings settings)
    {
        foreach (var pair in new[]
                 {
                     (Previous: 4744L, Current: 2857L),
                     (Previous: 4473L, Current: 2797L),
                     (Previous: 4464L, Current: 2795L),
                     (Previous: 4297L, Current: 2726L)
                 })
        {
            var decision = DeathWriteDetector.Evaluate(
                settings,
                pair.Previous,
                pair.Current);

            if (!decision.IsCandidate)
            {
                throw new InvalidOperationException(
                    $"Known death transition missed: {pair.Previous}->{pair.Current}, reason={decision.Reason}");
            }
        }

        foreach (var pair in new[]
                 {
                     (Previous: 69471L, Current: 4504L),
                     (Previous: 4753L, Current: 4504L),
                     (Previous: 4464L, Current: 4464L),
                     (Previous: 36567L, Current: 4297L)
                 })
        {
            var decision = DeathWriteDetector.Evaluate(
                settings,
                pair.Previous,
                pair.Current);

            if (decision.IsCandidate)
            {
                throw new InvalidOperationException(
                    $"Non-death transition false-positive: {pair.Previous}->{pair.Current}");
            }
        }
    }
}