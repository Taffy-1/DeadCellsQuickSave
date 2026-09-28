using System.Diagnostics;

namespace DeadCellsUniversalQuickSave;

internal static class GameLauncher
{
    public static bool TryLaunch(
        AppSettings settings,
        string dataDir,
        string installDir,
        out string? error,
        IWin32Window? owner = null)
    {
        error = null;

        try
        {
            foreach (var name in new[] { "deadcells", "deadcells_gl" })
            {
                if (Process.GetProcessesByName(name).Length > 0)
                    return true;
            }

            if (!FirstRunSetup.EnsureGameExeForLaunch(
                    settings,
                    dataDir,
                    installDir,
                    owner,
                    out error))
            {
                return false;
            }

            var discovery = new AutoDiscovery(
                settings,
                dataDir,
                installDir);
            var exe = discovery.DetectGameExe();

            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                error = "未找到 Dead Cells 可执行文件。";
                return false;
            }

            settings.GameExePath = exe;
            settings.Save(dataDir);

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
