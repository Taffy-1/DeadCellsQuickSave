# Dead Cells QuickSave

[中文](README_ZH.md) · [Back to project home](README.md)

A lightweight external **quick-save and automatic death-restore companion for Dead Cells** on Windows.

It is not an official Dead Cells Workshop mod. It runs as a standalone companion process and is designed to preserve the normal game executable while making checkpoint recovery faster and less disruptive.

---

## Features

- **Single EXE** — double-click `DeadCellsQuickSave.exe` to start QuickSave and Dead Cells.
- **F5 manual checkpoint** — create a checkpoint whenever you want.
- **Ctrl+Shift+F9 immediate restore** — restore the current safe checkpoint.
- **Automatic death detection** based only on `user_N.dat` save-state changes.
- **Automatic death restore** back to the checkpoint captured before death.
- **Same-process reload** — Dead Cells is not closed and relaunched.
- **Multiple save slots** — supports `user_0.dat`, `user_1.dat`, and additional slots.
- **Automatic or pinned slot mode**.
- **Rolling safety backups** before destructive restore operations.
- **Post-write integrity verification** using file length and SHA-256.
- **Loading overlay** with an animated green progress bar.
- **No OCR or continuous screen capture**.

---

## Quick start

### 1. Download

Download from GitHub Releases:

`DeadCellsQuickSave.exe`

### 2. Run

Double-click the EXE.

By default it will:

1. start QuickSave;
2. stay in the Windows system tray;
3. locate Dead Cells;
4. launch Dead Cells.

If automatic discovery fails, right-click the tray icon and use:

- **Select game executable**
- **Select save directory**
- **Re-detect configuration**

Supported game executables:

- `deadcells.exe`
- `deadcells_gl.exe`

### 3. Hotkeys

| Hotkey | Action |
|---|---|
| `F5` | Create a manual checkpoint |
| `Ctrl+Shift+F9` | Restore immediately |
| `Ctrl+Shift+F8` | Backup restore hotkey |

To start QuickSave without launching Dead Cells:

```bat
DeadCellsQuickSave.exe --background
```

---

## How automatic death restore works

QuickSave does not read character HP and does not use OCR.

It watches the currently active `user_N.dat` save file.

In the validated Dead Cells Update 35 behavior, death produces a characteristic save-state transition. QuickSave uses that transition as the death signal and then:

1. detects the death-state save change;
2. pins the safe pre-death checkpoint;
3. waits for Dead Cells to complete the post-death save reset;
4. normalizes the special post-death runtime state;
5. returns through the normal title/Continue flow in the same process;
6. restores the pinned pre-death checkpoint;
7. creates a new F5-equivalent checkpoint after a successful restore.

The Dead Cells process stays alive throughout the operation.

---

## Why this is not a Workshop mod

Dead Cells' official ModTools/Workshop system is primarily designed for:

- `res.pak` data overrides;
- Haxe structure scripts;
- level structure;
- world-map configuration;
- level parameters;
- enemy rosters and related game-content changes.

QuickSave instead requires Windows-level capabilities such as:

- monitoring external `user_N.dat` files;
- global hotkeys;
- Win32 window messages;
- an external loading overlay;
- replacing save files at a controlled point;
- driving the title/Continue flow.

Those capabilities are outside the official Workshop scripting layer, so QuickSave is distributed as a standalone companion executable.

---

## Multiple save slots

The default mode is:

**Follow the slot actually written by the game**

The `user_N.dat` that Dead Cells really writes becomes the active slot. QuickSave does not simply jump to whichever file happens to have the newest timestamp.

You can also pin a slot from the tray menu:

- slot 0 → `user_0.dat`
- slot 1 → `user_1.dat`
- slot 2 → `user_2.dat`
- …

When a slot is pinned, writes from other slots are ignored by the active QuickSave workflow.

---

## Tray menu

The tray menu provides:

- active save slot;
- automatic/pinned slot mode;
- launch Dead Cells;
- save now;
- restore now;
- re-run auto detection;
- choose game executable;
- choose save directory;
- automatic death-restore toggle;
- in-game save/load toast toggle;
- loading blackout overlay toggle;
- Windows-login startup;
- open snapshot directory.

---

## Data-safety design

QuickSave keeps more than one recovery point.

Important data channels include:

- `manual.dat` — manual checkpoint;
- `auto-current.dat` — current automatic checkpoint;
- `auto-previous.dat` — previous automatic checkpoint;
- timestamped history;
- safety backups created before a real restore;
- temporary pinned checkpoint used during death recovery.

After writing a checkpoint back to the live game save, QuickSave verifies:

- file length;
- SHA-256.

If verification fails, the restore sequence stops instead of treating the write as successful.

---

## Compatibility

Developed and tested with:

- **Dead Cells Update 35**
- **Windows x64**
- `deadcells.exe`
- `deadcells_gl.exe`

Future Dead Cells updates may change:

- title-menu layout;
- Continue behavior;
- save format;
- save-write timing;
- death-state transitions.

If a future update behaves differently, disable automatic death restore first and report the behavior through GitHub Issues.

---

## Build from source

Requirements:

- Windows x64
- .NET 9 SDK

### Release build

```bat
dotnet build UniversalQuickSave.csproj -c Release -t:Rebuild
```

### Self-test

```bat
dotnet run --project UniversalQuickSave.csproj -c Release --no-build -- --self-test
```

### Publish a self-contained single EXE

```bat
dotnet publish UniversalQuickSave.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

---

## Repository data and privacy

The public source repository intentionally excludes:

- Dead Cells game files;
- user saves;
- `settings.json`;
- checkpoints;
- safety backups;
- local logs;
- historical build EXEs.

QuickSave does not require uploading your game save to a server.

---

## Disclaimer

This is an unofficial community tool and is not affiliated with, endorsed by, or sponsored by Motion Twin, Evil Empire, or Valve.

Do not redistribute Dead Cells game files, game assets, or proprietary ModTools content with this project.
