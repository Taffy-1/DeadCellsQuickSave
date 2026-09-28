using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DeadCellsUniversalQuickSave;

internal sealed class ModHostForm : Form
{
    private const int HotkeySave = 3001;
    private const int HotkeyDeath = 3002;
    private const int HotkeyLoad = 3003;

    private readonly AppSettings _settings;
    // _baseDir is the writable DataDir.
    private readonly string _baseDir;
    private readonly string _installDir;
    private readonly SaveManager _saveManager;
    private readonly SoftReloadController _softReload;
    private readonly AutoDiscovery _discovery;
    private readonly AutoSaveWatcher _watcher;
    private readonly OverlayToast _toast;
    private readonly LoadingOverlay _loadingOverlay;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _slotStatusItem;
    private readonly ToolStripMenuItem _slotMenu;
    private readonly System.Windows.Forms.Timer _discoveryTimer;

    private string? _activeSavePath;
    private string? _saveDirectory;
    private DateTime _suppressAutoSnapshotUntil = DateTime.MinValue;
    private DateTime _suppressDeathUntil = DateTime.MinValue;
    private CancellationTokenSource? _deathRestoreDelayCts;
    private bool _busy;
    private bool _disposed;

    public ModHostForm(
        AppSettings settings,
        string dataDir,
        string installDir)
    {
        _settings = settings;
        _baseDir = dataDir;
        _installDir = installDir;
        _saveManager = new SaveManager(settings, dataDir);
        _softReload = new SoftReloadController(_saveManager);
        _discovery = new AutoDiscovery(
            settings,
            dataDir,
            installDir);
        _toast = new OverlayToast(_saveManager);
        _loadingOverlay = new LoadingOverlay(_saveManager);
        _watcher = new AutoSaveWatcher(settings, OnStableSaveWriteAsync);

        Text = "Dead Cells Universal QuickSave Host";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        Opacity = 0;

        var menu = new ContextMenuStrip();

        _slotStatusItem = new ToolStripMenuItem("当前槽位：检测中")
        {
            Enabled = false
        };

        _slotMenu = new ToolStripMenuItem("存档槽位");

        menu.Items.Add(_slotStatusItem);
        menu.Items.Add(_slotMenu);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(
            "启动 Dead Cells",
            null,
            (_, _) =>
            {
                if (!GameLauncher.TryLaunch(
                        _settings,
                        _baseDir,
                        _installDir,
                        out var error,
                        this))
                {
                    ShowTrayError(error ?? "无法启动 Dead Cells。");
                }
            });

        menu.Items.Add("立即保存  F5", null, async (_, _) => await ManualSnapshotAsync());
        menu.Items.Add("立即读档  Ctrl+Shift+F9", null, async (_, _) => await RestoreAsync());
        menu.Items.Add("备用死亡读档  Ctrl+Shift+F8", null, async (_, _) => await RestoreAsync());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(
            "重新自动检测",
            null,
            (_, _) =>
            {
                RefreshDiscovery();
                RebuildSlotMenu();
                ShowStatusBalloon();
            });

        menu.Items.Add(
            "选择游戏程序…",
            null,
            (_, _) =>
            {
                if (!FirstRunSetup.ChooseGameExe(
                        _settings,
                        _baseDir,
                        _installDir,
                        this,
                        out var error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        ShowTrayError(error);
                    return;
                }

                RefreshDiscovery();
                ShowStatusBalloon();
            });

        menu.Items.Add(
            "选择存档目录…",
            null,
            (_, _) =>
            {
                if (!FirstRunSetup.ChooseSaveDirectory(
                        _settings,
                        _baseDir,
                        _installDir,
                        this,
                        out var error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        ShowTrayError(error);
                    return;
                }

                _activeSavePath = _settings.SaveFilePath;
                RefreshDiscovery();
                RebuildSlotMenu();
                ShowStatusBalloon();
            });

        menu.Items.Add(
            "打开快照目录",
            null,
            (_, _) => _saveManager.OpenSnapshotFolder());

        menu.Items.Add(new ToolStripSeparator());

        var autoDeathItem = new ToolStripMenuItem("自动死亡回档")
        {
            Checked = _settings.AutoDeathRestore,
            CheckOnClick = false
        };
        autoDeathItem.Click += (_, _) =>
        {
            _settings.AutoDeathRestore =
                !_settings.AutoDeathRestore;

            autoDeathItem.Checked =
                _settings.AutoDeathRestore;

            _settings.Save(_baseDir);
        };
        menu.Items.Add(autoDeathItem);

        var toastItem = new ToolStripMenuItem("游戏内保存/读档提示")
        {
            Checked = _settings.ShowGameOverlayToast,
            CheckOnClick = false
        };
        toastItem.Click += (_, _) =>
        {
            _settings.ShowGameOverlayToast =
                !_settings.ShowGameOverlayToast;

            toastItem.Checked =
                _settings.ShowGameOverlayToast;

            _settings.Save(_baseDir);
        };
        menu.Items.Add(toastItem);

        var loadingOverlayItem = new ToolStripMenuItem("读档黑屏遮罩")
        {
            Checked = _settings.ShowLoadingOverlay,
            CheckOnClick = false
        };
        loadingOverlayItem.Click += (_, _) =>
        {
            _settings.ShowLoadingOverlay =
                !_settings.ShowLoadingOverlay;

            loadingOverlayItem.Checked =
                _settings.ShowLoadingOverlay;

            _settings.Save(_baseDir);
        };
        menu.Items.Add(loadingOverlayItem);

        var autoStartItem = new ToolStripMenuItem("随 Windows 登录启动 QuickSave")
        {
            Checked = AutoStartManager.IsEnabled(),
            CheckOnClick = false
        };
        autoStartItem.Click += (_, _) =>
        {
            var enable = !AutoStartManager.IsEnabled();
            if (AutoStartManager.SetEnabled(enable, out var error))
            {
                autoStartItem.Checked = enable;
                _tray!.BalloonTipTitle = "QuickSave";
                _tray.BalloonTipText = enable
                    ? "已启用随 Windows 登录后台启动。"
                    : "已关闭随 Windows 登录后台启动。";
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(1800);
            }
            else
            {
                ShowTrayError(error ?? "修改自动启动设置失败。");
            }
        };
        menu.Items.Add(autoStartItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 QuickSave", null, (_, _) => Close());

        _tray = new NotifyIcon
        {
            Text = "Dead Cells QuickSave Mod",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowStatusBalloon();

        menu.Opening += (_, _) =>
        {
            RefreshDiscovery();
            RebuildSlotMenu();
        };

        _discoveryTimer = new System.Windows.Forms.Timer
        {
            Interval = 2000
        };
        _discoveryTimer.Tick += (_, _) => RefreshDiscovery();

        Shown += (_, _) =>
        {
            Hide();

            FirstRunSetup.TryAutoConfigure(
                _settings,
                _baseDir,
                _installDir);

            RefreshDiscovery();
            RebuildSlotMenu();
            _discoveryTimer.Start();

            _tray.BalloonTipTitle = "Dead Cells QuickSave";
            _tray.BalloonTipText =
                "后台 Mod 已启动。右键托盘可查看槽位、启动游戏或调整配置。";
            _tray.BalloonTipIcon = ToolTipIcon.Info;
            _tray.ShowBalloonTip(2200);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        RegisterRequiredHotKey(
            HotkeySave,
            NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_F5,
            "F5");

        RegisterRequiredHotKey(
            HotkeyDeath,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_F8,
            "Ctrl+Shift+F8");

        RegisterRequiredHotKey(
            HotkeyLoad,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_F9,
            "Ctrl+Shift+F9");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HotkeySave:
                    _ = ManualSnapshotAsync();
                    return;

                case HotkeyDeath:
                    _ = RestoreAsync();
                    return;

                case HotkeyLoad:
                    _ = RestoreAsync();
                    return;
            }
        }

        base.WndProc(ref m);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        NativeMethods.UnregisterHotKey(Handle, HotkeySave);
        NativeMethods.UnregisterHotKey(Handle, HotkeyDeath);
        NativeMethods.UnregisterHotKey(Handle, HotkeyLoad);
        base.OnHandleDestroyed(e);
    }

    private void RegisterRequiredHotKey(int id, uint modifiers, uint key, string displayName)
    {
        if (NativeMethods.RegisterHotKey(Handle, id, modifiers, key))
            return;

        var error = Marshal.GetLastWin32Error();
        _tray.BalloonTipTitle = "QuickSave 热键冲突";
        _tray.BalloonTipText = $"{displayName} 注册失败，Win32={error}";
        _tray.BalloonTipIcon = ToolTipIcon.Warning;
        _tray.ShowBalloonTip(2500);
    }

    private void RebuildSlotMenu()
    {
        _slotMenu.DropDownItems.Clear();

        var autoItem = new ToolStripMenuItem(
            "自动跟随实际写入槽位")
        {
            Checked = _settings.PinnedSlotNumber < 0
        };

        autoItem.Click += (_, _) =>
            SetPinnedSlot(-1);

        _slotMenu.DropDownItems.Add(autoItem);
        _slotMenu.DropDownItems.Add(
            new ToolStripSeparator());

        var slots = _discovery.DiscoverSlots()
            .GroupBy(x => x.Number)
            .Select(g => g
                .OrderByDescending(x => x.LastWriteUtc)
                .First())
            .OrderBy(x => x.Number)
            .ToArray();

        if (slots.Length == 0)
        {
            _slotMenu.DropDownItems.Add(
                new ToolStripMenuItem("未发现 user_N.dat")
                {
                    Enabled = false
                });
        }
        else
        {
            foreach (var slot in slots)
            {
                var slotNumber = slot.Number;
                var item = new ToolStripMenuItem(
                    $"固定槽位 {slotNumber} · user_{slotNumber}.dat")
                {
                    Checked =
                        _settings.PinnedSlotNumber ==
                        slotNumber
                };

                item.Click += (_, _) =>
                    SetPinnedSlot(slotNumber);

                _slotMenu.DropDownItems.Add(item);
            }
        }

        UpdateSlotStatus(
            _discovery.DiscoverActiveSlot());
    }

    private void SetPinnedSlot(int slotNumber)
    {
        if (slotNumber >= 0)
        {
            var selected =
                _discovery.DiscoverSlot(slotNumber);

            if (selected is null)
            {
                ShowTrayError(
                    $"未找到槽位 {slotNumber} 的 user_{slotNumber}.dat。");
                return;
            }

            _settings.PinnedSlotNumber = slotNumber;
            _settings.FollowWrittenSlot = false;
            _settings.LastActiveSlotNumber =
                slotNumber;
            _settings.SaveFilePath =
                selected.Path;
            _activeSavePath =
                selected.Path;
        }
        else
        {
            _settings.PinnedSlotNumber = -1;
            _settings.FollowWrittenSlot = true;

            var observed =
                _settings.LastActiveSlotNumber >= 0
                    ? _discovery.DiscoverSlot(
                        _settings.LastActiveSlotNumber)
                    : null;

            if (observed is not null)
            {
                _settings.SaveFilePath =
                    observed.Path;
                _activeSavePath =
                    observed.Path;
            }
        }

        _settings.Save(_baseDir);
        RefreshDiscovery();
        RebuildSlotMenu();

        _tray.BalloonTipTitle =
            "QuickSave 槽位";

        _tray.BalloonTipText =
            slotNumber >= 0
                ? $"已固定到槽位 {slotNumber}。"
                : "已切换为自动跟随实际写入槽位。";

        _tray.BalloonTipIcon =
            ToolTipIcon.Info;
        _tray.ShowBalloonTip(1600);
    }

    private void ObserveWrittenSlot(
        string path,
        int slot)
    {
        if (_settings.PinnedSlotNumber >= 0 ||
            !_settings.FollowWrittenSlot)
        {
            return;
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        var changed =
            _settings.LastActiveSlotNumber != slot ||
            !string.Equals(
                _settings.SaveFilePath,
                fullPath,
                StringComparison.OrdinalIgnoreCase);

        _activeSavePath = fullPath;
        _settings.SaveFilePath = fullPath;
        _settings.LastActiveSlotNumber = slot;

        if (changed)
            _settings.Save(_baseDir);

        UpdateSlotStatus(
            new SaveSlot(
                slot,
                fullPath,
                File.Exists(fullPath)
                    ? File.GetLastWriteTimeUtc(fullPath)
                    : DateTime.UtcNow));
    }

    private void UpdateSlotStatus(
        SaveSlot? active)
    {
        if (active is not null)
        {
            var mode =
                _settings.PinnedSlotNumber >= 0
                    ? "固定"
                    : "自动";

            _slotStatusItem.Text =
                $"当前槽位：{active.Number} · {mode}";
            return;
        }

        _slotStatusItem.Text =
            _settings.PinnedSlotNumber >= 0
                ? $"当前槽位：{_settings.PinnedSlotNumber} · 未找到存档"
                : "当前槽位：未检测";
    }

    private void RefreshDiscovery()
    {
        if (_disposed || !_settings.AutoDiscover)
            return;

        try
        {
            _discovery.ApplyDetectedConfiguration();

            var active = _discovery.DiscoverActiveSlot();
            if (active is not null)
            {
                _activeSavePath = active.Path;
                _settings.SaveFilePath = active.Path;
            }
            else if (_settings.PinnedSlotNumber >= 0)
            {
                _activeSavePath = null;
            }

            var directory = active is not null
                ? Path.GetDirectoryName(active.Path)
                : _discovery.DiscoverSaveDirectory();

            if (!string.Equals(
                    _saveDirectory,
                    directory,
                    StringComparison.OrdinalIgnoreCase))
            {
                _saveDirectory = directory;
                _watcher.Bind(directory);
            }

            UpdateSlotStatus(active);
        }
        catch
        {
        }
    }

    private async Task OnStableSaveWriteAsync(string path)
    {
        if (InvokeRequired)
        {
            var tcs = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            BeginInvoke(async () =>
            {
                try
                {
                    await OnStableSaveWriteAsync(path);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            await tcs.Task;
            return;
        }

        if (_disposed)
            return;

        if (!AutoDiscovery.TryGetSlotNumber(path, out var slot))
            return;

        if (_settings.PinnedSlotNumber >= 0 &&
            slot != _settings.PinnedSlotNumber)
        {
            return;
        }

        ObserveWrittenSlot(path, slot);

        if (!_settings.AutoSnapshotOnWrite ||
            DateTime.Now < _suppressAutoSnapshotUntil)
        {
            return;
        }

        long previousBytes = 0;
        long currentBytes = 0;
        var snapshotCreated = false;

        try
        {
            var slotDir = Path.Combine(
                _saveManager.SnapshotRoot,
                Path.GetFileNameWithoutExtension(path));

            var priorCurrent = Path.Combine(
                slotDir,
                "auto-current.dat");

            if (File.Exists(priorCurrent))
                previousBytes = new FileInfo(priorCurrent).Length;
        }
        catch
        {
            previousBytes = 0;
        }

        await RunSerializedAsync(async () =>
        {
            _activeSavePath = path;
            _settings.SaveFilePath = path;

            var result = await _saveManager.CreateAutoCheckpointAsync(path);
            currentBytes = result.Bytes;
            snapshotCreated = true;

            if (_settings.ShowGameOverlayToast)
            {
                _toast.ShowMessage(
                    $"已自动保存 · 槽位 {slot}",
                    _settings.OverlayToastDurationMs);
            }

            Debug.WriteLine(
                $"[QuickSave] Auto snapshot slot={slot} bytes={result.Bytes} sha={result.Sha256}");
        });

        if (!snapshotCreated ||
            !_settings.AutoDeathRestore ||
            !_settings.DeathDetectionEnabled ||
            DateTime.Now < _suppressDeathUntil)
        {
            return;
        }

        var decision = DeathWriteDetector.Evaluate(
            _settings,
            previousBytes,
            currentBytes);

        AppendDeathDetectionLog(slot, decision);

        if (!decision.IsCandidate)
            return;

        await ScheduleDelayedDeathRestoreAsync(
            path,
            slot,
            decision);
    }

    private async Task ScheduleDelayedDeathRestoreAsync(
        string savePath,
        int slot,
        DeathWriteDecision decision)
    {
        CancelPendingDeathRestore();

        var checkpoint =
            _saveManager.SelectPreferredRestoreCheckpoint(savePath);

        if (checkpoint is null)
        {
            ShowTrayError("检测到死亡，但没有可用的安全 checkpoint。");
            return;
        }

        string pinnedCheckpoint;
        try
        {
            pinnedCheckpoint = CreatePinnedDeathCheckpoint(
                checkpoint,
                slot);
        }
        catch (Exception ex)
        {
            ShowTrayError($"锁定死亡前 checkpoint 失败：{ex.Message}");
            return;
        }

        var reboundTimeoutMs = Math.Clamp(
            _settings.DeathReboundTimeoutMs,
            1500,
            20000);

        var secondPassDelayMs = Math.Clamp(
            _settings.DeathSecondPassDelayMs,
            1000,
            10000);

        var cts = new CancellationTokenSource();
        _deathRestoreDelayCts = cts;

        // Freeze checkpoint rotation while waiting for Dead Cells to finish
        // converting the death-state save into a normal post-death/new-run save.
        _suppressAutoSnapshotUntil = DateTime.Now.AddMilliseconds(
            reboundTimeoutMs + secondPassDelayMs + 45000);

        _suppressDeathUntil = DateTime.Now.AddMilliseconds(
            reboundTimeoutMs +
            secondPassDelayMs +
            Math.Clamp(
                _settings.DeathRestoreSuppressMs,
                4000,
                30000) +
            15000);

        _toast.ShowMessage(
            "检测到死亡 · 等待游戏完成归零",
            1800);

        AppendDeathRestoreScheduleLog(
            slot,
            decision,
            pinnedCheckpoint,
            reboundTimeoutMs);

        try
        {
            var rebound = await WaitForPostDeathReboundAsync(
                savePath,
                decision.CurrentBytes,
                cts.Token);

            if (_disposed || cts.IsCancellationRequested)
                return;

            AppendDeathReboundLog(
                slot,
                rebound.Ready,
                rebound.Size,
                rebound.WaitedMs);

            if (rebound.Ready)
            {
                // The save-size rebound proves the death write has been
                // converted into a normal post-death/new-run save, but the UI
                // can still be in a special transition state. Normalize that
                // state WITHOUT touching the pinned checkpoint, then perform
                // the one real checkpoint restore under one continuous overlay.
                await ExecuteNormalizedDeathRestoreAsync(
                    savePath,
                    pinnedCheckpoint,
                    slot,
                    cts.Token);

                return;
            }

            // Fallback only: if Dead Cells never writes a normal-sized
            // post-death save on its own, retain the proven v1.9 two-pass path.
            _toast.ShowMessage(
                "归零写盘未出现 · 启用兼容恢复",
                1600);

            await RestoreAsync(
                checkpointOverride: pinnedCheckpoint,
                cancelPendingDeathDelay: false,
                createPostRestoreCheckpoint: false,
                showCompletion: false);

            await Task.Delay(secondPassDelayMs, cts.Token);

            if (_disposed || cts.IsCancellationRequested)
                return;

            await RestoreAsync(
                checkpointOverride: pinnedCheckpoint,
                cancelPendingDeathDelay: false,
                createPostRestoreCheckpoint: true,
                showCompletion: true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            TryDeletePinnedCheckpoint(pinnedCheckpoint);

            if (ReferenceEquals(_deathRestoreDelayCts, cts))
            {
                _deathRestoreDelayCts = null;
                cts.Dispose();
            }
        }
    }

    private async Task ExecuteNormalizedDeathRestoreAsync(
        string savePath,
        string pinnedCheckpoint,
        int slot,
        CancellationToken cancellationToken)
    {
        await RunSerializedAsync(async () =>
        {
            var overlayShown =
                _settings.ShowLoadingOverlay &&
                _loadingOverlay.ShowOverGame();

            try
            {
                var normalize = await _softReload.NormalizeCurrentSaveAsync(
                    savePath,
                    cancellationToken);

                AppendDeathNormalizeLog(
                    slot,
                    normalize.Success,
                    normalize.ProcessIdBefore,
                    normalize.ProcessIdAfter,
                    normalize.Error);

                if (!normalize.Success)
                {
                    ShowTrayError(
                        $"死亡状态归一化失败：{normalize.Error ?? "未知错误"}");
                    return;
                }

                // NormalizeCurrentSaveAsync already waits for the current
                // post-death/new-run save to finish loading into gameplay.
                // From this known-good state, perform the one real checkpoint
                // restore. Keep the same overlay active for both operations.
                await RestoreCoreAsync(
                    savePath,
                    pinnedCheckpoint,
                    slot,
                    createPostRestoreCheckpoint: true,
                    showCompletion: true,
                    manageLoadingOverlay: false,
                    cancellationToken);
            }
            finally
            {
                if (overlayShown)
                {
                    await Task.Delay(250, cancellationToken);
                    _loadingOverlay.HideOverlay();
                }
            }
        });
    }

    private void AppendDeathNormalizeLog(
        int slot,
        bool success,
        int pidBefore,
        int pidAfter,
        string? error)
    {
        try
        {
            var safeError = string.IsNullOrWhiteSpace(error)
                ? ""
                : error.Replace("\r", " ").Replace("\n", " ");

            var line =
                $"{DateTime.Now:O}\tslot={slot}\taction=death-normalize\tsuccess={success}\tpidBefore={pidBefore}\tpidAfter={pidAfter}\terror={safeError}{Environment.NewLine}";

            var logPath = Path.Combine(
                _baseDir,
                "data",
                "death-detection.log");

            Directory.CreateDirectory(
                Path.GetDirectoryName(logPath)!);

            File.AppendAllText(logPath, line);
        }
        catch
        {
        }
    }

    private async Task<(bool Ready, long Size, int WaitedMs)>
        WaitForPostDeathReboundAsync(
            string savePath,
            long deathStateBytes,
            CancellationToken cancellationToken)
    {
        var readyMinBytes = Math.Max(
            deathStateBytes + 256,
            Math.Clamp(
                _settings.DeathReboundReadyMinBytes,
                3401,
                20000));

        var stableMs = Math.Clamp(
            _settings.DeathReboundStableMs,
            250,
            3000);

        var timeoutMs = Math.Clamp(
            _settings.DeathReboundTimeoutMs,
            1500,
            20000);

        var started = DateTime.UtcNow;
        DateTime? stableSince = null;
        long lastSize = -1;
        DateTime lastWriteUtc = DateTime.MinValue;
        long observedSize = deathStateBytes;

        while ((DateTime.UtcNow - started).TotalMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size = 0;
            DateTime writeUtc = DateTime.MinValue;

            try
            {
                if (File.Exists(savePath))
                {
                    var info = new FileInfo(savePath);
                    size = info.Length;
                    writeUtc = info.LastWriteTimeUtc;
                    observedSize = size;
                }
            }
            catch
            {
            }

            if (size >= readyMinBytes)
            {
                if (size == lastSize &&
                    writeUtc == lastWriteUtc)
                {
                    stableSince ??= DateTime.UtcNow;

                    if ((DateTime.UtcNow - stableSince.Value)
                        .TotalMilliseconds >= stableMs)
                    {
                        return (
                            true,
                            size,
                            (int)(DateTime.UtcNow - started)
                                .TotalMilliseconds);
                    }
                }
                else
                {
                    stableSince = DateTime.UtcNow;
                }
            }
            else
            {
                stableSince = null;
            }

            lastSize = size;
            lastWriteUtc = writeUtc;

            await Task.Delay(120, cancellationToken);
        }

        return (
            false,
            observedSize,
            (int)(DateTime.UtcNow - started).TotalMilliseconds);
    }

    private void AppendDeathReboundLog(
        int slot,
        bool ready,
        long size,
        int waitedMs)
    {
        try
        {
            var line =
                $"{DateTime.Now:O}\tslot={slot}\taction=death-rebound\tready={ready}\tsize={size}\twaitedMs={waitedMs}{Environment.NewLine}";

            var logPath = Path.Combine(
                _baseDir,
                "data",
                "death-detection.log");

            Directory.CreateDirectory(
                Path.GetDirectoryName(logPath)!);

            File.AppendAllText(logPath, line);
        }
        catch
        {
        }
    }

    private string CreatePinnedDeathCheckpoint(
        string checkpoint,
        int slot)
    {
        if (!File.Exists(checkpoint))
            throw new FileNotFoundException("安全 checkpoint 不存在。", checkpoint);

        var directory = Path.Combine(
            _baseDir,
            "data",
            "pending-death");

        Directory.CreateDirectory(directory);

        var pinned = Path.Combine(
            directory,
            $"slot-{slot}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.dat");

        File.Copy(checkpoint, pinned, overwrite: false);
        return pinned;
    }

    private static void TryDeletePinnedCheckpoint(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private void CancelPendingDeathRestore()
    {
        var pending = _deathRestoreDelayCts;
        _deathRestoreDelayCts = null;

        if (pending is null)
            return;

        try
        {
            pending.Cancel();
        }
        catch
        {
        }
        finally
        {
            pending.Dispose();
        }
    }

    private void AppendDeathRestoreScheduleLog(
        int slot,
        DeathWriteDecision decision,
        string checkpoint,
        int reboundTimeoutMs)
    {
        try
        {
            var line =
                $"{DateTime.Now:O}\tslot={slot}\taction=death-rebound-watch\ttimeoutMs={reboundTimeoutMs}\tprev={decision.PreviousBytes}\tcurr={decision.CurrentBytes}\tcheckpoint={checkpoint}{Environment.NewLine}";

            var logPath = Path.Combine(
                _baseDir,
                "data",
                "death-detection.log");

            Directory.CreateDirectory(
                Path.GetDirectoryName(logPath)!);

            File.AppendAllText(logPath, line);
        }
        catch
        {
        }
    }

    private void AppendDeathDetectionLog(
        int slot,
        DeathWriteDecision decision)
    {
        try
        {
            var line =
                $"{DateTime.Now:O}\tslot={slot}\tprev={decision.PreviousBytes}\tcurr={decision.CurrentBytes}\tdrop={decision.DropBytes}\tratio={decision.Ratio:F3}\tcandidate={decision.IsCandidate}\treason={decision.Reason}{Environment.NewLine}";

            var logPath = Path.Combine(
                _baseDir,
                "data",
                "death-detection.log");

            Directory.CreateDirectory(
                Path.GetDirectoryName(logPath)!);

            File.AppendAllText(logPath, line);
        }
        catch
        {
        }
    }

    private async Task ManualSnapshotAsync()
    {
        RefreshDiscovery();

        var path = ResolveActiveSave();
        if (path is null)
        {
            ShowTrayError("未找到 user_N.dat 存档。");
            return;
        }

        if (!AutoDiscovery.TryGetSlotNumber(path, out var slot))
            slot = -1;

        await RunSerializedAsync(async () =>
        {
            _settings.SaveFilePath = path;
            var result = await _saveManager.CreateManualCheckpointAsync(path);

            if (_settings.ShowGameOverlayToast)
            {
                _toast.ShowMessage(
                    slot >= 0 ? $"存档成功 · 槽位 {slot}" : "存档成功",
                    _settings.OverlayToastDurationMs);
            }

            _tray.BalloonTipTitle = "QuickSave";
            _tray.BalloonTipText =
                slot >= 0
                    ? $"槽位 {slot} 已保存 · {result.Bytes:N0} bytes"
                    : $"已保存 · {result.Bytes:N0} bytes";
            _tray.BalloonTipIcon = ToolTipIcon.Info;
        });
    }

    private async Task RestoreAsync(
        string? checkpointOverride = null,
        bool cancelPendingDeathDelay = true,
        bool createPostRestoreCheckpoint = true,
        bool showCompletion = true)
    {
        if (cancelPendingDeathDelay)
            CancelPendingDeathRestore();

        RefreshDiscovery();

        var path = ResolveActiveSave();
        if (path is null)
        {
            ShowTrayError("没有可恢复的活动槽位。");
            return;
        }

        var checkpoint = checkpointOverride ??
            _saveManager.SelectPreferredRestoreCheckpoint(path);

        if (checkpoint is null || !File.Exists(checkpoint))
        {
            ShowTrayError("当前槽位还没有可用的安全 checkpoint。");
            return;
        }

        if (!AutoDiscovery.TryGetSlotNumber(path, out var slot))
            slot = -1;

        await RunSerializedAsync(async () =>
        {
            await RestoreCoreAsync(
                path,
                checkpoint,
                slot,
                createPostRestoreCheckpoint,
                showCompletion,
                manageLoadingOverlay: true,
                CancellationToken.None);
        });
    }

    private async Task<bool> RestoreCoreAsync(
        string path,
        string checkpoint,
        int slot,
        bool createPostRestoreCheckpoint,
        bool showCompletion,
        bool manageLoadingOverlay,
        CancellationToken cancellationToken)
    {
        _settings.SaveFilePath = path;
        _suppressAutoSnapshotUntil = DateTime.Now.AddSeconds(30);
        _suppressDeathUntil = DateTime.Now.AddMilliseconds(
            Math.Clamp(
                _settings.DeathRestoreSuppressMs,
                4000,
                30000));

        var overlayShown =
            manageLoadingOverlay &&
            _settings.ShowLoadingOverlay &&
            _loadingOverlay.ShowOverGame();

        var success = false;
        string? error = null;
        var pidBefore = 0;
        var pidAfter = 0;

        try
        {
            var result = await _softReload.ReloadAsync(
                checkpoint,
                path,
                cancellationToken);

            success = result.Success;
            error = result.Error;
            pidBefore = result.ProcessIdBefore;
            pidAfter = result.ProcessIdAfter;
        }
        finally
        {
            if (overlayShown)
            {
                await Task.Delay(250, cancellationToken);
                _loadingOverlay.HideOverlay();
            }
        }

        if (!success)
        {
            ShowTrayError(
                $"同进程读档失败：{error ?? "未知错误"}");
            return false;
        }

        var checkpointCreated = false;
        string? checkpointWarning = null;

        if (createPostRestoreCheckpoint &&
            _settings.AutoCheckpointAfterRestore)
        {
            await Task.Delay(
                Math.Clamp(
                    _settings.PostRestoreCheckpointDelayMs,
                    100,
                    2000),
                cancellationToken);

            try
            {
                await _saveManager.CreateManualCheckpointAsync(
                    path,
                    cancellationToken);

                await _saveManager.ResetAutoCheckpointBaselineAsync(
                    path,
                    cancellationToken);

                checkpointCreated = true;
            }
            catch (Exception ex)
            {
                checkpointWarning = ex.Message;
            }
        }

        if (showCompletion &&
            _settings.ShowGameOverlayToast)
        {
            var message = checkpointCreated
                ? (slot >= 0
                    ? $"读档完成 · 已重设存档点 · 槽位 {slot}"
                    : "读档完成 · 已重设存档点")
                : (slot >= 0
                    ? $"读档完成 · 槽位 {slot}"
                    : "读档完成");

            _toast.ShowMessage(
                message,
                _settings.OverlayToastDurationMs);
        }

        if (showCompletion)
        {
            _tray.BalloonTipTitle = "QuickSave";
            _tray.BalloonTipText = checkpointWarning is null
                ? (checkpointCreated
                    ? $"同进程读档完成 · 自动 F5 已完成 · PID {pidBefore} → {pidAfter}"
                    : $"同进程读档完成 · PID {pidBefore} → {pidAfter}")
                : $"读档完成，但自动 F5 失败：{checkpointWarning}";

            _tray.BalloonTipIcon = checkpointWarning is null
                ? ToolTipIcon.Info
                : ToolTipIcon.Warning;

            _tray.ShowBalloonTip(2200);
        }

        return true;
    }

    private string? ResolveActiveSave()
    {
        if (!string.IsNullOrWhiteSpace(_activeSavePath) &&
            File.Exists(_activeSavePath))
        {
            return _activeSavePath;
        }

        var active = _discovery.DiscoverActiveSlot();
        if (active is not null)
        {
            _activeSavePath = active.Path;
            return active.Path;
        }

        if (!string.IsNullOrWhiteSpace(_settings.SaveFilePath) &&
            File.Exists(_settings.SaveFilePath))
        {
            return _settings.SaveFilePath;
        }

        return null;
    }

    private async Task RunSerializedAsync(Func<Task> action)
    {
        if (_busy || _disposed)
            return;

        _busy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowTrayError(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowStatusBalloon()
    {
        RefreshDiscovery();

        var slots = _discovery.DiscoverSlots();
        var active = ResolveActiveSave();

        var activeText = active is null
            ? "未检测到活动槽位"
            : Path.GetFileNameWithoutExtension(active);

        var modeText =
            _settings.PinnedSlotNumber >= 0
                ? $"固定槽位 {_settings.PinnedSlotNumber}"
                : "自动跟随槽位";

        _tray.BalloonTipTitle = "Dead Cells QuickSave";
        _tray.BalloonTipText =
            $"{activeText} · {modeText} · 已发现 {slots.Count} 个槽位\nF5 保存 · Ctrl+Shift+F9 读档";
        _tray.BalloonTipIcon = ToolTipIcon.Info;
        _tray.ShowBalloonTip(2200);
    }

    private void ShowTrayError(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowTrayError(message));
            return;
        }

        _tray.BalloonTipTitle = "QuickSave";
        _tray.BalloonTipText = message.Length > 220 ? message[..220] : message;
        _tray.BalloonTipIcon = ToolTipIcon.Error;
        _tray.ShowBalloonTip(2600);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;

        if (disposing)
        {
            CancelPendingDeathRestore();
            _discoveryTimer.Stop();
            _discoveryTimer.Dispose();
            _watcher.Dispose();
            _toast.Dispose();
            _loadingOverlay.Dispose();

            _tray.Visible = false;
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
