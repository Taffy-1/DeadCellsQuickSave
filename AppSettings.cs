using System.Text.Json;

namespace DeadCellsUniversalQuickSave;

internal sealed class AppSettings
{
    public const int CurrentConfigVersion = 3;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;
    public string SaveFilePath { get; set; } = "";
    public string GameExePath { get; set; } = "";

    // -1 means "follow the slot Dead Cells actually writes".
    public int PinnedSlotNumber { get; set; } = -1;
    public int LastActiveSlotNumber { get; set; } = -1;
    public bool FollowWrittenSlot { get; set; } = true;
    public bool FirstRunCompleted { get; set; } = false;

    public int HistoryLimit { get; set; } = 30;
    public int SafetyBackupLimit { get; set; } = 12;
    public int DiagnosticLogMaxKb { get; set; } = 512;
    public bool CleanupStaleDataOnStartup { get; set; } = true;
    public int StableReadDelayMs { get; set; } = 18;
    public bool AutoDiscover { get; set; } = true;
    public bool AutoSnapshotOnWrite { get; set; } = true;
    public int AutoSnapshotDebounceMs { get; set; } = 850;
    public int AutoSnapshotMinIntervalMs { get; set; } = 2500;
    public bool ShowGameOverlayToast { get; set; } = true;
    public int OverlayToastDurationMs { get; set; } = 1200;
    public bool ShowLoadingOverlay { get; set; } = true;
    public bool AutoDeathRestore { get; set; } = true;
    public bool DeathDetectionEnabled { get; set; } = true;
    public double DeathCandidateDropRatio { get; set; } = 0.78;
    public int DeathCandidateMinDropBytes { get; set; } = 650;
    public int DeathCandidatePreviousMinBytes { get; set; } = 3800;
    public int DeathCandidateCurrentMaxBytes { get; set; } = 3400;
    public int DeathRestoreSuppressMs { get; set; } = 12000;
    public int DeathReboundReadyMinBytes { get; set; } = 3800;
    public int DeathReboundStableMs { get; set; } = 600;
    public int DeathReboundTimeoutMs { get; set; } = 8000;
    public int DeathSecondPassDelayMs { get; set; } = 3000;
    public bool AutoCheckpointAfterRestore { get; set; } = true;
    public int PostRestoreCheckpointDelayMs { get; set; } = 350;

    public static AppSettings Load(string baseDir)
    {
        var path = Path.Combine(baseDir, "settings.json");
        AppSettings settings;
        var needsRewrite = false;

        try
        {
            if (File.Exists(path))
            {
                var raw = File.ReadAllText(path);

                settings =
                    JsonSerializer.Deserialize<AppSettings>(raw) ??
                    new AppSettings();

                needsRewrite =
                    !raw.Contains(
                        "\"ConfigVersion\"",
                        StringComparison.Ordinal) ||
                    ContainsLegacyFields(raw);
            }
            else
            {
                settings = new AppSettings();
                needsRewrite = true;
            }
        }
        catch
        {
            settings = new AppSettings();
            needsRewrite = true;
        }

        var changed =
            settings.Normalize() ||
            needsRewrite;

        if (changed)
            settings.Save(baseDir);

        return settings;
    }

    public bool Normalize()
    {
        var changed = false;

        if (ConfigVersion != CurrentConfigVersion)
        {
            ConfigVersion = CurrentConfigVersion;
            changed = true;
        }

        HistoryLimit = ClampValue(HistoryLimit, 5, 200, ref changed);
        SafetyBackupLimit = ClampValue(SafetyBackupLimit, 3, 100, ref changed);
        DiagnosticLogMaxKb = ClampValue(DiagnosticLogMaxKb, 64, 4096, ref changed);
        StableReadDelayMs = ClampValue(StableReadDelayMs, 5, 250, ref changed);
        AutoSnapshotDebounceMs = ClampValue(AutoSnapshotDebounceMs, 150, 5000, ref changed);
        AutoSnapshotMinIntervalMs = ClampValue(AutoSnapshotMinIntervalMs, 500, 30000, ref changed);
        OverlayToastDurationMs = ClampValue(OverlayToastDurationMs, 500, 5000, ref changed);
        DeathCandidateMinDropBytes = ClampValue(DeathCandidateMinDropBytes, 256, 8192, ref changed);
        DeathCandidatePreviousMinBytes = ClampValue(DeathCandidatePreviousMinBytes, 1000, 20000, ref changed);
        DeathCandidateCurrentMaxBytes = ClampValue(DeathCandidateCurrentMaxBytes, 1200, 8000, ref changed);
        DeathRestoreSuppressMs = ClampValue(DeathRestoreSuppressMs, 4000, 30000, ref changed);
        DeathReboundReadyMinBytes = ClampValue(DeathReboundReadyMinBytes, 3401, 20000, ref changed);
        DeathReboundStableMs = ClampValue(DeathReboundStableMs, 250, 3000, ref changed);
        DeathReboundTimeoutMs = ClampValue(DeathReboundTimeoutMs, 1500, 20000, ref changed);
        DeathSecondPassDelayMs = ClampValue(DeathSecondPassDelayMs, 1000, 10000, ref changed);
        PostRestoreCheckpointDelayMs = ClampValue(PostRestoreCheckpointDelayMs, 100, 2000, ref changed);

        var ratio = Math.Clamp(DeathCandidateDropRatio, 0.45, 0.90);
        if (Math.Abs(ratio - DeathCandidateDropRatio) > 0.000001)
        {
            DeathCandidateDropRatio = ratio;
            changed = true;
        }

        if (PinnedSlotNumber < -1)
        {
            PinnedSlotNumber = -1;
            changed = true;
        }

        if (LastActiveSlotNumber < -1)
        {
            LastActiveSlotNumber = -1;
            changed = true;
        }

        return changed;
    }

    public void Save(string baseDir)
    {
        Directory.CreateDirectory(baseDir);
        ConfigVersion = CurrentConfigVersion;

        var json = JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions { WriteIndented = true });

        var path = Path.Combine(baseDir, "settings.json");
        var temp = path + ".tmp";

        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private static bool ContainsLegacyFields(string raw)
    {
        foreach (var name in new[]
                 {
                     "StartMinimized",
                     "AutoRestartAfterRestore",
                     "HeadlessMode",
                     "DeathAutoRestoreDelayMs"
                 })
        {
            if (raw.Contains(
                    "\"" + name + "\"",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int ClampValue(int value, int min, int max, ref bool changed)
    {
        var clamped = Math.Clamp(value, min, max);
        if (clamped != value)
            changed = true;

        return clamped;
    }
}
