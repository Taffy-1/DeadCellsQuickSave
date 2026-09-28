namespace DeadCellsUniversalQuickSave;

internal sealed record AppPaths(
    string InstallDir,
    string DataDir,
    bool PortableMode)
{
    public static AppPaths Resolve(
        IReadOnlyCollection<string> args)
    {
        var installDir = Path.GetFullPath(
            AppContext.BaseDirectory);

        var portable =
            args.Contains(
                "--portable",
                StringComparer.OrdinalIgnoreCase) ||
            File.Exists(
                Path.Combine(
                    installDir,
                    "portable.mode"));

        if (portable)
        {
            return new AppPaths(
                installDir,
                installDir,
                true);
        }

        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return new AppPaths(
                installDir,
                installDir,
                true);
        }

        var dataDir = Path.Combine(
            localAppData,
            "DeadCellsQuickSave");

        return new AppPaths(
            installDir,
            Path.GetFullPath(dataDir),
            false);
    }

    public void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDir);
    }

    public void MigrateLegacyPortableData()
    {
        if (PortableMode ||
            PathsEqual(InstallDir, DataDir))
        {
            return;
        }

        EnsureDataDirectory();

        CopyIfMissing(
            Path.Combine(
                InstallDir,
                "settings.json"),
            Path.Combine(
                DataDir,
                "settings.json"));

        MergeDirectoryWithoutOverwrite(
            Path.Combine(
                InstallDir,
                "data"),
            Path.Combine(
                DataDir,
                "data"));

        var marker = Path.Combine(
            DataDir,
            "migration-from-portable-v1.done");

        try
        {
            if (!File.Exists(marker))
            {
                File.WriteAllText(
                    marker,
                    $"source={InstallDir}{Environment.NewLine}" +
                    $"migratedAt={DateTimeOffset.Now:O}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    private static void CopyIfMissing(
        string source,
        string target)
    {
        try
        {
            if (!File.Exists(source) ||
                File.Exists(target))
            {
                return;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(target)!);

            File.Copy(
                source,
                target,
                overwrite: false);
        }
        catch
        {
        }
    }

    private static void MergeDirectoryWithoutOverwrite(
        string sourceRoot,
        string targetRoot)
    {
        if (!Directory.Exists(sourceRoot))
            return;

        try
        {
            foreach (var source in Directory.EnumerateFiles(
                         sourceRoot,
                         "*",
                         SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(
                    sourceRoot,
                    source);

                var target = Path.Combine(
                    targetRoot,
                    relative);

                CopyIfMissing(
                    source,
                    target);
            }
        }
        catch
        {
        }
    }

    private static bool PathsEqual(
        string left,
        string right)
    {
        try
        {
            return Path.GetFullPath(left)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Equals(
                    Path.GetFullPath(right)
                        .TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
