using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DeadCellsUniversalQuickSave;

internal sealed record SaveSlot(int Number, string Path, DateTime LastWriteUtc);

internal sealed class AutoDiscovery
{
    private readonly AppSettings _settings;
    private readonly string _dataDir;
    private readonly string _installDir;

    public AutoDiscovery(
        AppSettings settings,
        string dataDir,
        string installDir)
    {
        _settings = settings;
        _dataDir = dataDir;
        _installDir = installDir;
    }

    public string? DetectGameExe(
        bool includeRunningProcess = true)
    {
        if (includeRunningProcess)
        {
            foreach (var name in new[] { "deadcells", "deadcells_gl" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            var path = process.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                                return Path.GetFullPath(path);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.GameExePath) &&
            File.Exists(_settings.GameExePath))
        {
            return Path.GetFullPath(_settings.GameExePath);
        }

        foreach (var candidate in EnumerateLocalGameCandidates())
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
            }
        }

        foreach (var candidate in EnumerateSteamGameCandidates())
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
            }
        }

        return null;
    }

    public IReadOnlyList<SaveSlot> DiscoverSlots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_settings.SaveFilePath))
        {
            try
            {
                var dir = Path.GetDirectoryName(
                    Path.GetFullPath(_settings.SaveFilePath));

                if (!string.IsNullOrWhiteSpace(dir))
                    roots.Add(dir);
            }
            catch
            {
            }
        }

        AddStandardSaveRoots(roots);

        var slots = new List<SaveSlot>();

        foreach (var dir in roots)
        {
            try
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var file in Directory.EnumerateFiles(
                             dir,
                             "user_*.dat",
                             SearchOption.TopDirectoryOnly))
                {
                    if (!TryGetSlotNumber(file, out var number))
                        continue;

                    slots.Add(new SaveSlot(
                        number,
                        Path.GetFullPath(file),
                        File.GetLastWriteTimeUtc(file)));
                }
            }
            catch
            {
            }
        }

        return slots
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Number)
            .ThenByDescending(x => x.LastWriteUtc)
            .ToArray();
    }

    public SaveSlot? DiscoverPreferredSlot()
    {
        var slots = DiscoverSlots();
        if (slots.Count == 0)
            return null;

        if (_settings.PinnedSlotNumber >= 0)
        {
            return RankSameNumber(
                slots,
                _settings.PinnedSlotNumber);
        }

        if (!string.IsNullOrWhiteSpace(_settings.SaveFilePath))
        {
            try
            {
                var current = Path.GetFullPath(_settings.SaveFilePath);

                var exact = slots.FirstOrDefault(
                    x => string.Equals(
                        x.Path,
                        current,
                        StringComparison.OrdinalIgnoreCase));

                if (exact is not null)
                    return exact;
            }
            catch
            {
            }
        }

        if (_settings.LastActiveSlotNumber >= 0)
        {
            var observed = RankSameNumber(
                slots,
                _settings.LastActiveSlotNumber);

            if (observed is not null)
                return observed;
        }

        return slots
            .OrderByDescending(x => x.LastWriteUtc)
            .FirstOrDefault();
    }

    public SaveSlot? DiscoverActiveSlot()
    {
        return DiscoverPreferredSlot();
    }

    public SaveSlot? DiscoverSlot(int slotNumber)
    {
        return RankSameNumber(
            DiscoverSlots(),
            slotNumber);
    }

    public string? DiscoverSaveDirectory()
    {
        var preferred = DiscoverPreferredSlot();
        if (preferred is not null)
            return Path.GetDirectoryName(preferred.Path);

        return DiscoverSlots()
            .GroupBy(
                x => Path.GetDirectoryName(x.Path)!,
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(x => x.LastWriteUtc))
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    public bool ApplyDetectedConfiguration()
    {
        var changed = false;

        var exe = DetectGameExe();
        if (!string.IsNullOrWhiteSpace(exe) &&
            !string.Equals(
                _settings.GameExePath,
                exe,
                StringComparison.OrdinalIgnoreCase))
        {
            _settings.GameExePath = exe;
            changed = true;
        }

        var preferred = DiscoverPreferredSlot();
        if (preferred is not null &&
            !string.Equals(
                _settings.SaveFilePath,
                preferred.Path,
                StringComparison.OrdinalIgnoreCase))
        {
            _settings.SaveFilePath = preferred.Path;
            changed = true;
        }

        if (preferred is not null &&
            _settings.LastActiveSlotNumber < 0)
        {
            _settings.LastActiveSlotNumber = preferred.Number;
            changed = true;
        }

        if (changed)
            _settings.Save(_dataDir);

        return changed;
    }

    public static bool TryGetSlotNumber(string path, out int number)
    {
        number = -1;

        try
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(
                    "user_",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(name.AsSpan(5), out number);
        }
        catch
        {
            return false;
        }
    }

    private SaveSlot? RankSameNumber(
        IReadOnlyList<SaveSlot> slots,
        int number)
    {
        var candidates = slots
            .Where(x => x.Number == number)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(_settings.SaveFilePath))
        {
            try
            {
                var currentDir = Path.GetDirectoryName(
                    Path.GetFullPath(_settings.SaveFilePath));

                var sameRoot = candidates
                    .Where(x => string.Equals(
                        Path.GetDirectoryName(x.Path),
                        currentDir,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.LastWriteUtc)
                    .FirstOrDefault();

                if (sameRoot is not null)
                    return sameRoot;
            }
            catch
            {
            }
        }

        return candidates
            .OrderByDescending(x => x.LastWriteUtc)
            .First();
    }

    private IEnumerable<string> EnumerateLocalGameCandidates()
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("..", "Dead Cells", "deadcells.exe"),
                     Path.Combine("..", "Dead Cells", "deadcells_gl.exe"),
                     Path.Combine("..", "deadcells.exe"),
                     Path.Combine("..", "deadcells_gl.exe")
                 })
        {
            string? full = null;

            try
            {
                full = Path.GetFullPath(
                    Path.Combine(_installDir, relative));
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(full))
                yield return full;
        }
    }

    private static IEnumerable<string> EnumerateSteamGameCandidates()
    {
        foreach (var steamRoot in EnumerateSteamRoots())
        {
            foreach (var library in EnumerateSteamLibraries(steamRoot))
            {
                yield return Path.Combine(
                    library,
                    "steamapps",
                    "common",
                    "Dead Cells",
                    "deadcells.exe");

                yield return Path.Combine(
                    library,
                    "steamapps",
                    "common",
                    "Dead Cells",
                    "deadcells_gl.exe");
            }
        }
    }

    private void AddStandardSaveRoots(HashSet<string> roots)
    {
        foreach (var steamRoot in EnumerateSteamRoots())
        {
            var userdata = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata))
                continue;

            try
            {
                foreach (var account in Directory.EnumerateDirectories(userdata))
                {
                    AddIfExists(
                        roots,
                        Path.Combine(account, "588650", "remote"));
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.GameExePath))
        {
            try
            {
                var gameDir = Path.GetDirectoryName(
                    Path.GetFullPath(_settings.GameExePath));

                if (!string.IsNullOrWhiteSpace(gameDir))
                    AddIfExists(roots, Path.Combine(gameDir, "save"));
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var roots = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86),
            "Steam");

        AddDirectoryIfExists(roots, defaultRoot);

        foreach (var location in new[]
                 {
                     (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath")
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(
                    location.Item1,
                    RegistryView.Default);

                using var key = baseKey.OpenSubKey(location.Item2);
                var value = key?.GetValue(location.Item3) as string;

                if (!string.IsNullOrWhiteSpace(value))
                    AddDirectoryIfExists(roots, value);
            }
            catch
            {
            }
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateSteamLibraries(
        string steamRoot)
    {
        var libraries = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            steamRoot
        };

        var file = Path.Combine(
            steamRoot,
            "steamapps",
            "libraryfolders.vdf");

        try
        {
            if (File.Exists(file))
            {
                var text = File.ReadAllText(file);

                foreach (Match match in Regex.Matches(
                             text,
                             "\"path\"\\s+\"(?<path>[^\"]+)\"",
                             RegexOptions.IgnoreCase))
                {
                    var path = match.Groups["path"].Value
                        .Replace(@"\\", @"\");

                    AddDirectoryIfExists(libraries, path);
                }
            }
        }
        catch
        {
        }

        return libraries;
    }

    private static void AddDirectoryIfExists(
        HashSet<string> roots,
        string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path))
            {
                roots.Add(Path.GetFullPath(path));
            }
        }
        catch
        {
        }
    }

    private static void AddIfExists(
        HashSet<string> roots,
        string path)
    {
        AddDirectoryIfExists(roots, path);
    }
}
