# Dead Cells QuickSave

[English](README_EN.md) · [返回项目首页](README.md)

一个面向 Windows 的轻量级 **Dead Cells 外置快速存档 / 自动死亡回档工具**。

它不是 Dead Cells 官方创意工坊 Mod，而是一个独立运行的 Companion 程序。设计目标很简单：尽量不改变游戏本体，在死亡后自动回到上一个安全 checkpoint，同时保留手动快速存档和读档能力。

---

## 核心功能

- **单 EXE 启动**：双击 `DeadCellsQuickSave.exe` 即可启动 QuickSave 和 Dead Cells。
- **F5 手动存档**：随时创建一个手动 checkpoint。
- **Ctrl+Shift+F9 立即读档**：直接回到当前安全 checkpoint。
- **自动死亡识别**：仅根据 `user_N.dat` 的存档状态变化判断死亡。
- **自动死亡回档**：死亡后自动恢复到死亡前 checkpoint。
- **同进程读档**：不重启 Dead Cells EXE，减少游戏体验中断。
- **多槽位支持**：支持 `user_0.dat`、`user_1.dat` 等多个存档槽位。
- **自动/固定槽位**：既可以跟随游戏实际写入的槽位，也可以在托盘菜单固定指定槽位。
- **安全备份**：回档前保留 safety backup，并维护滚动 checkpoint。
- **完整性校验**：真正覆盖游戏存档后会校验文件长度与 SHA-256。
- **读档遮罩**：黑屏读档界面带绿色动画加载条。
- **无需 OCR**：不依赖持续截图、OCR 或实时画面识别。

---

## 快速开始

### 1. 下载

从 GitHub Releases 下载：

`DeadCellsQuickSave.exe`

### 2. 启动

直接双击 EXE。

默认行为：

1. 启动 QuickSave；
2. QuickSave 进入系统托盘；
3. 自动查找 Dead Cells；
4. 启动 Dead Cells。

如果没有自动找到游戏，可以右键托盘图标，选择：

- **选择游戏程序…**
- **选择存档目录…**
- **重新自动检测**

支持的游戏程序：

- `deadcells.exe`
- `deadcells_gl.exe`

### 3. 热键

| 热键 | 功能 |
|---|---|
| `F5` | 创建手动 checkpoint |
| `Ctrl+Shift+F9` | 立即回档 |
| `Ctrl+Shift+F8` | 备用手动回档 |

如果只想启动 QuickSave 后台，而不自动启动 Dead Cells：

```bat
DeadCellsQuickSave.exe --background
```

---

## 自动死亡回档是如何工作的

QuickSave 不读取角色血量，也不做 OCR。

它会监视当前活动的 `user_N.dat`。

在已经验证的 Dead Cells Update 35 中，死亡时存档会出现明显的状态变化。QuickSave 使用这种变化作为死亡信号，并执行以下流程：

1. 检测到死亡态存档变化；
2. 立即锁定死亡前的安全 checkpoint；
3. 等待游戏完成死亡后的存档归零/重建；
4. 先把游戏从死亡后的特殊状态归一化到正常可读档状态；
5. 在同一个 Dead Cells 进程中回到标题/Continue 流程；
6. 恢复锁定的死亡前 checkpoint；
7. 成功进入 checkpoint 后自动创建新的 F5 等价存档点。

整个过程中 Dead Cells EXE 不会被关闭后重启。

---

## 为什么不是创意工坊 Mod

Dead Cells 官方 ModTools/Workshop 体系主要面向：

- `res.pak` 数据覆盖；
- Haxe 结构脚本；
- 关卡结构；
- 世界图；
- 关卡参数；
- 怪物配置等游戏内容层修改。

QuickSave 当前依赖的能力则包括：

- 监视外部 `user_N.dat`；
- Windows 全局热键；
- Win32 窗口消息；
- 外置加载遮罩；
- 在安全时机替换存档文件；
- 驱动标题界面和 Continue 流程。

这些能力不属于官方 Workshop 脚本层，因此本项目以独立 Companion EXE 的形式发布，而不是放入 `mods` 目录。

---

## 多存档槽位

默认模式为：

**自动跟随实际写入槽位**

也就是说，真正被 Dead Cells 写入的 `user_N.dat` 会成为当前活动槽位，而不是简单按照“最后修改时间”随意切换。

也可以在托盘菜单固定：

- 槽位 0 → `user_0.dat`
- 槽位 1 → `user_1.dat`
- 槽位 2 → `user_2.dat`
- …

固定槽位后，其他槽位的写盘不会触发当前 QuickSave 流程。

---

## 托盘菜单

托盘菜单包含：

- 当前活动槽位；
- 自动/固定槽位模式；
- 启动 Dead Cells；
- 立即保存；
- 立即读档；
- 重新自动检测；
- 选择游戏程序；
- 选择存档目录；
- 自动死亡回档开关；
- 游戏内保存/读档提示开关；
- 读档黑屏遮罩开关；
- 随 Windows 登录启动；
- 打开快照目录。

---

## 数据安全设计

QuickSave 不会只保留一份存档。

主要数据通道包括：

- `manual.dat`：手动 checkpoint；
- `auto-current.dat`：当前自动 checkpoint；
- `auto-previous.dat`：上一份自动 checkpoint；
- history：按时间保留的历史 checkpoint；
- safety：真正覆盖游戏存档前的安全备份；
- pending-death：死亡恢复期间临时锁定的 checkpoint。

真正写回游戏存档后还会执行：

- 文件长度校验；
- SHA-256 校验。

如果校验失败，会停止继续加载，而不是把异常写入结果当成成功。

---

## 兼容性

当前开发和实际测试环境：

- **Dead Cells Update 35**
- **Windows x64**
- `deadcells.exe`
- `deadcells_gl.exe`

未来 Dead Cells 更新可能改变：

- 标题菜单结构；
- Continue 行为；
- 存档格式；
- 存档写入时机；
- 死亡状态切换流程。

因此新版本游戏如果出现异常，建议先关闭自动死亡回档，再通过 Issue 报告。

---

## 从源码构建

要求：

- Windows x64
- .NET 9 SDK

### Release 编译

```bat
dotnet build UniversalQuickSave.csproj -c Release -t:Rebuild
```

### 自测

```bat
dotnet run --project UniversalQuickSave.csproj -c Release --no-build -- --self-test
```

### 发布单文件 EXE

```bat
dotnet publish UniversalQuickSave.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

---

## 项目数据与隐私

源码仓库不会提交：

- Dead Cells 游戏文件；
- 用户存档；
- `settings.json`；
- checkpoint 数据；
- safety backup；
- 本地日志；
- 历史构建 EXE。

本项目不需要上传游戏存档到服务器。

---

## 免责声明

本项目为非官方社区工具，与 Motion Twin、Evil Empire、Valve 无隶属、合作或授权关系。

请勿把 Dead Cells 游戏文件、游戏资源或专有 ModTools 内容重新打包进本项目发行物。
