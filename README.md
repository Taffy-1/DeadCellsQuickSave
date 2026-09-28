# Dead Cells QuickSave

> A lightweight external quick-save companion for **Dead Cells** on Windows.  
> 一个面向 Windows 的轻量级 **Dead Cells 外置快速存档 / 自动死亡回档工具**。

[中文说明](README_ZH.md) · [English](README_EN.md)

---

## What it does / 功能概览

- Single EXE — double-click to start QuickSave and Dead Cells.
- Manual checkpoint with `F5`.
- Immediate restore with `Ctrl+Shift+F9`.
- Automatic death detection based on save-file state changes.
- Automatic return to the previous checkpoint after death.
- Same-process reload — Dead Cells is not restarted.
- Multiple `user_N.dat` slot support.
- Rolling safety backups and post-write integrity verification.
- Black loading overlay with an animated green loading bar.

---

## Quick start / 快速开始

1. Download `DeadCellsQuickSave.exe`.
2. Double-click it.
3. QuickSave runs in the system tray and starts Dead Cells.
4. If the game or save cannot be detected automatically, use the tray menu to select the game executable or save directory.

### Hotkeys / 热键

| Hotkey | Action |
|---|---|
| `F5` | Create a manual checkpoint / 手动保存 checkpoint |
| `Ctrl+Shift+F9` | Restore immediately / 立即回档 |
| `Ctrl+Shift+F8` | Backup restore hotkey / 备用回档 |

Use `--background` to start QuickSave without launching Dead Cells.

---

## Why this is not a Workshop mod / 为什么不是创意工坊 Mod

Dead Cells' official Workshop scripting layer focuses on game-content overrides such as level structure and related data. QuickSave requires Windows-level features including save-file watching, global hotkeys, window messaging, and an external overlay.

Therefore this project is distributed as a standalone companion executable rather than a Workshop package.

---

## Compatibility / 兼容性

Developed and tested against:

- Dead Cells Update 35
- Windows x64
- `deadcells.exe`
- `deadcells_gl.exe`
- Multiple `user_N.dat` save slots

Future game updates may change menu flow, save behavior, or timing assumptions used by the same-process restore sequence.

---

## Documentation / 文档

For installation, detailed behavior, build instructions, data-safety design and implementation notes:

- [中文完整说明](README_ZH.md)
- [Full English README](README_EN.md)

---

## Disclaimer / 免责声明

This is an unofficial community tool and is not affiliated with Motion Twin, Evil Empire, or Valve.

本项目为非官方社区工具，与 Motion Twin、Evil Empire 或 Valve 无隶属或合作关系。

Do not redistribute Dead Cells game files, assets, or proprietary ModTools content with this project.
