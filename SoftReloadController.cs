using System.Diagnostics;

namespace DeadCellsUniversalQuickSave;

internal sealed record SoftReloadResult(
    bool Success,
    int ProcessIdBefore,
    int ProcessIdAfter,
    string? Error);

internal sealed class SoftReloadController
{
    private readonly SaveManager _saveManager;

    public SoftReloadController(SaveManager saveManager)
    {
        _saveManager = saveManager;
    }

    public async Task<SoftReloadResult> NormalizeCurrentSaveAsync(
        string targetSavePath,
        CancellationToken cancellationToken = default)
    {
        return await RunMenuReloadAsync(
            targetSavePath,
            checkpointPath: null,
            cancellationToken);
    }

    public async Task<SoftReloadResult> ReloadAsync(
        string checkpointPath,
        string targetSavePath,
        CancellationToken cancellationToken = default)
    {
        return await RunMenuReloadAsync(
            targetSavePath,
            checkpointPath,
            cancellationToken);
    }

    private async Task<SoftReloadResult> RunMenuReloadAsync(
        string targetSavePath,
        string? checkpointPath,
        CancellationToken cancellationToken)
    {
        using var processBefore = _saveManager.FindGameProcess();
        if (processBefore is null)
        {
            return new SoftReloadResult(
                false,
                0,
                0,
                "没有检测到正在运行的 Dead Cells。");
        }

        var pidBefore = processBefore.Id;
        var hwnd = _saveManager.GetGameWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            return new SoftReloadResult(
                false,
                pidBefore,
                pidBefore,
                "Dead Cells 主窗口不可用。");
        }

        try
        {
            var beforeWrite = SafeLastWriteUtc(targetSavePath);

            // Gameplay -> Pause
            await PressAsync(hwnd, NativeMethods.VK_ESCAPE, 90, 280, cancellationToken);

            // Default pause selection is "返回". Move to "退出游戏".
            for (var i = 0; i < 5; i++)
                await PressAsync(hwnd, NativeMethods.VK_RIGHT, 90, 330, cancellationToken);

            // Open quit confirmation.
            await PressAsync(hwnd, NativeMethods.VK_RETURN, 90, 420, cancellationToken);

            // "回到标题画面?" -> Space = Yes.
            await PressAsync(hwnd, NativeMethods.VK_SPACE, 90, 350, cancellationToken);

            // Wait for Dead Cells to finish leaving the current run.
            await WaitForTitleTransitionAsync(
                targetSavePath,
                beforeWrite,
                cancellationToken);

            // We are now at the title screen; the live run no longer owns the
            // active state. Only a true restore pass replaces user_N.dat.
            // The normalization pass deliberately leaves the current post-death
            // save untouched and simply reloads it into a standard gameplay state.
            if (!string.IsNullOrWhiteSpace(checkpointPath))
            {
                await _saveManager.RestoreCheckpointFileAsync(
                    checkpointPath,
                    targetSavePath,
                    cancellationToken);
            }

            // Title menu default selection: 游戏.
            await PressAsync(hwnd, NativeMethods.VK_RETURN, 90, 420, cancellationToken);

            // Game submenu default selection: 继续（普通模式）.
            await PressAsync(hwnd, NativeMethods.VK_RETURN, 90, 350, cancellationToken);

            // Allow loading/cutscene transition to complete while keeping
            // the same process alive.
            await Task.Delay(4300, cancellationToken);

            using var processAfter = _saveManager.FindGameProcess();
            if (processAfter is null)
            {
                return new SoftReloadResult(
                    false,
                    pidBefore,
                    0,
                    "软重载后 Dead Cells 进程消失。");
            }

            var pidAfter = processAfter.Id;
            if (pidAfter != pidBefore)
            {
                return new SoftReloadResult(
                    false,
                    pidBefore,
                    pidAfter,
                    "软重载意外更换了进程 PID。");
            }

            return new SoftReloadResult(
                true,
                pidBefore,
                pidAfter,
                null);
        }
        catch (OperationCanceledException)
        {
            return new SoftReloadResult(
                false,
                pidBefore,
                pidBefore,
                "软重载已取消。");
        }
        catch (Exception ex)
        {
            return new SoftReloadResult(
                false,
                pidBefore,
                pidBefore,
                ex.Message);
        }
    }

    private static async Task WaitForTitleTransitionAsync(
        string savePath,
        DateTime beforeWriteUtc,
        CancellationToken cancellationToken)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        var observedWrite = false;

        while (DateTime.UtcNow < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentWrite = SafeLastWriteUtc(savePath);
            if (currentWrite > beforeWriteUtc)
            {
                observedWrite = true;
                break;
            }

            await Task.Delay(120, cancellationToken);
        }

        // In the tested v35 build, title screen arrives quickly after the
        // quit-save write. If no write is observed, use a conservative delay.
        await Task.Delay(
            observedWrite ? 650 : 1800,
            cancellationToken);
    }

    private static async Task PressAsync(
        IntPtr hwnd,
        uint virtualKey,
        int holdMs,
        int afterMs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!NativeMethods.PostMessageW(
                hwnd,
                NativeMethods.WM_KEYDOWN,
                new UIntPtr(virtualKey),
                IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"无法向 Dead Cells 投递按键 0x{virtualKey:X2}。");
        }

        await Task.Delay(holdMs, cancellationToken);

        NativeMethods.PostMessageW(
            hwnd,
            NativeMethods.WM_KEYUP,
            new UIntPtr(virtualKey),
            IntPtr.Zero);

        await Task.Delay(afterMs, cancellationToken);
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
