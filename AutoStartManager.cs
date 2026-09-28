using Microsoft.Win32;

namespace DeadCellsUniversalQuickSave;

internal static class AutoStartManager
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName =
        "DeadCellsQuickSave";

    public static bool IsEnabled()
    {
        return IsEnabledForExecutable(
            Environment.ProcessPath);
    }

    public static bool IsEnabledForExecutable(
        string? executablePath)
    {
        try
        {
            using var key =
                Registry.CurrentUser.OpenSubKey(
                    RunKeyPath,
                    writable: false);

            var value =
                key?.GetValue(ValueName) as string;

            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            var exe = Path.GetFullPath(
                executablePath);

            return value.Contains(
                exe,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(
        bool enabled,
        out string? error)
    {
        return SetEnabledForExecutable(
            enabled,
            Environment.ProcessPath,
            out error);
    }

    public static bool SetEnabledForExecutable(
        bool enabled,
        string? executablePath,
        out string? error)
    {
        error = null;

        try
        {
            using var key =
                Registry.CurrentUser.CreateSubKey(
                    RunKeyPath,
                    writable: true);

            if (key is null)
            {
                error =
                    "无法打开当前用户启动项注册表。";
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(
                    ValueName,
                    throwOnMissingValue: false);

                return true;
            }

            if (string.IsNullOrWhiteSpace(
                    executablePath))
            {
                error =
                    "无法确定 QuickSave 可执行文件路径。";
                return false;
            }

            var exe = Path.GetFullPath(
                executablePath);

            if (!File.Exists(exe))
            {
                error =
                    "QuickSave 可执行文件不存在。";
                return false;
            }

            key.SetValue(
                ValueName,
                $"\"{exe}\" --background",
                RegistryValueKind.String);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
