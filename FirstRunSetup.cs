namespace DeadCellsUniversalQuickSave;

internal static class FirstRunSetup
{
    public static bool TryAutoConfigure(
        AppSettings settings,
        string dataDir,
        string installDir)
    {
        var discovery = new AutoDiscovery(
            settings,
            dataDir,
            installDir);

        discovery.ApplyDetectedConfiguration();

        var gameReady =
            !string.IsNullOrWhiteSpace(settings.GameExePath) &&
            File.Exists(settings.GameExePath);

        var saveReady =
            discovery.DiscoverPreferredSlot() is not null;

        var ready = gameReady && saveReady;

        if (ready != settings.FirstRunCompleted)
        {
            settings.FirstRunCompleted = ready;
            settings.Save(dataDir);
        }

        return ready;
    }

    public static bool EnsureGameExeForLaunch(
        AppSettings settings,
        string dataDir,
        string installDir,
        IWin32Window? owner,
        out string? error)
    {
        error = null;

        var discovery = new AutoDiscovery(
            settings,
            dataDir,
            installDir);

        var detected = discovery.DetectGameExe();

        if (!string.IsNullOrWhiteSpace(detected) &&
            File.Exists(detected))
        {
            settings.GameExePath = detected;
            settings.Save(dataDir);
            return true;
        }

        return ChooseGameExe(
            settings,
            dataDir,
            installDir,
            owner,
            out error);
    }

    public static bool ChooseGameExe(
        AppSettings settings,
        string dataDir,
        string installDir,
        IWin32Window? owner,
        out string? error)
    {
        error = null;

        using var dialog = new OpenFileDialog
        {
            Title = "选择 Dead Cells 游戏程序",
            Filter =
                "Dead Cells|deadcells.exe;deadcells_gl.exe|" +
                "Executable (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(settings.GameExePath))
        {
            try
            {
                dialog.InitialDirectory =
                    Path.GetDirectoryName(
                        Path.GetFullPath(settings.GameExePath));
            }
            catch
            {
            }
        }

        var result = owner is null
            ? dialog.ShowDialog()
            : dialog.ShowDialog(owner);

        if (result != DialogResult.OK)
        {
            error = "未选择 Dead Cells 可执行文件。";
            return false;
        }

        var name = Path.GetFileName(dialog.FileName);

        if (!name.Equals(
                "deadcells.exe",
                StringComparison.OrdinalIgnoreCase) &&
            !name.Equals(
                "deadcells_gl.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            error = "请选择 deadcells.exe 或 deadcells_gl.exe。";
            return false;
        }

        settings.GameExePath =
            Path.GetFullPath(dialog.FileName);

        settings.Save(dataDir);

        TryAutoConfigure(
            settings,
            dataDir,
            installDir);

        return true;
    }

    public static bool ChooseSaveDirectory(
        AppSettings settings,
        string dataDir,
        string installDir,
        IWin32Window? owner,
        out string? error)
    {
        error = null;

        using var dialog = new FolderBrowserDialog
        {
            Description =
                "选择包含 user_0.dat / user_1.dat 等文件的 Dead Cells 存档目录",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(settings.SaveFilePath))
            {
                var current = Path.GetDirectoryName(
                    Path.GetFullPath(settings.SaveFilePath));

                if (!string.IsNullOrWhiteSpace(current) &&
                    Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }
            }
        }
        catch
        {
        }

        var result = owner is null
            ? dialog.ShowDialog()
            : dialog.ShowDialog(owner);

        if (result != DialogResult.OK)
        {
            error = "未选择存档目录。";
            return false;
        }

        string[] slots;

        try
        {
            slots = Directory
                .EnumerateFiles(
                    dialog.SelectedPath,
                    "user_*.dat",
                    SearchOption.TopDirectoryOnly)
                .Where(x =>
                    AutoDiscovery.TryGetSlotNumber(
                        x,
                        out _))
                .OrderByDescending(
                    File.GetLastWriteTimeUtc)
                .ToArray();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (slots.Length == 0)
        {
            error = "所选目录中没有 user_N.dat 存档文件。";
            return false;
        }

        settings.SaveFilePath =
            Path.GetFullPath(slots[0]);

        if (AutoDiscovery.TryGetSlotNumber(
                settings.SaveFilePath,
                out var slot))
        {
            settings.LastActiveSlotNumber = slot;
        }

        settings.Save(dataDir);

        TryAutoConfigure(
            settings,
            dataDir,
            installDir);

        return true;
    }
}
