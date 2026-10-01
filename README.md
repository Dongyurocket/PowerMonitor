# PowerMonitor

A lightweight, borderless desktop widget for Windows that shows your PC's real-time power draw — whole-system wattage, per-component breakdown, a live history curve, and cumulative energy usage. Sits quietly in a corner of your screen and in the tray.

[English](#english) · [中文](#中文)

---

## English

### Features

- **Whole-system power** at a glance — big, always-visible wattage reading
- **Per-component breakdown** — CPU, GPU, RAM, storage, PSU, battery (whichever your hardware exposes), each with a current/peak bar
- **Live history curve** — last 2 minutes of total power, smoothed, with gradient fill
- **Energy accounting** — session / today / all-time energy (Wh / kWh), persisted across restarts
- **Smart total** — uses a real PSU sensor or battery discharge rate when available; otherwise estimates wall power from component sum + configurable baseline and PSU efficiency
- **Unintrusive** — borderless, draggable, always-on-top (toggleable), rounded corners, dark theme, tray icon with a tooltip summary

### Requirements

| Requirement | Why |
|---|---|
| Windows 10 / 11 (x64) | WinForms desktop app |
| Administrator rights | Hardware sensor access via the LibreHardwareMonitor kernel driver (the app manifest requests elevation) |
| [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) | Only for framework-dependent builds; the release zip is self-contained and needs nothing |

### Dependencies

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) **0.9.6** (NuGet, MPL-2.0 license) — hardware sensor reading. This is the only external dependency; everything else is .NET 8 / WinForms built-ins.

### Download & Run

1. Grab `PowerMonitor-vX.Y.Z-win-x64.zip` from [Releases](../../releases) and unzip it.
2. Run `PowerMonitor.exe` and approve the UAC prompt (administrator rights are required to read sensors).
3. Drag the widget anywhere; right-click it for options (always-on-top, estimation settings, reset peaks, clear energy stats, exit). Left-click the tray icon to show/hide.

To build from source instead:

```powershell
git clone https://github.com/Dongyurocket/PowerMonitor.git
cd PowerMonitor
dotnet build -c Release
# or produce a single self-contained exe:
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Notes

- **Estimation mode**: when no PSU/battery sensor exists, total power = (component sum + baseline) ÷ PSU efficiency. Tune both values via right-click → 整机估算设置 (Estimation settings). Integrated-GPU power is not double-counted when it is already included in the CPU package.
- **Data files**: `settings.json` and `energy.json` are written next to the exe. Delete them to reset everything.
- **Diagnostics**: run `PowerMonitor.exe --dump` to write all detected power sensors to `sensors.txt` (useful when reporting unsupported hardware).
- Windows Defender / SmartScreen may warn about an unsigned exe — this is expected for a personal build.

### License

MIT (see [LICENSE](LICENSE)). LibreHardwareMonitorLib is licensed separately under MPL-2.0.

---

## 中文

一个轻量的 Windows 桌面功耗监控小组件：无边框圆角悬浮窗，实时显示整机功耗、各硬件功耗明细、近 2 分钟历史曲线和累计能耗，支持系统托盘。

### 功能

- **整机功耗大字显示**，实时刷新（1 秒间隔）
- **硬件明细**：CPU / GPU / 内存 / 硬盘 / 电源 / 电池（取决于硬件是否暴露传感器），每行带当前值/峰值进度条
- **历史曲线**：近 2 分钟整机功耗平滑曲线 + 渐变填充
- **能耗统计**：本次 / 今日 / 累计用电量（Wh / kWh），关程序不丢失
- **智能整机功率**：有电源或电池传感器时用实测值；否则按"（组件合计 + 基础功耗）÷ 电源效率"估算墙插功率，参数可调
- **不打扰**：无边框可拖动、可置顶、深色主题、托盘图标悬停显示摘要

### 运行要求

| 要求 | 原因 |
|---|---|
| Windows 10 / 11 (x64) | WinForms 桌面程序 |
| 管理员权限 | LibreHardwareMonitor 驱动读取硬件传感器所需（程序清单已声明，启动时弹 UAC） |
| [.NET 8 桌面运行时](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0) | 仅源码构建需要；Release 压缩包为自包含单文件，无需安装任何运行时 |

### 依赖的库

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) **0.9.6**（NuGet，MPL-2.0 协议）——硬件传感器读取。这是唯一的外部依赖，其余均为 .NET 8 / WinForms 内置。

### 使用

1. 在 [Releases](../../releases) 下载 `PowerMonitor-vX.Y.Z-win-x64.zip` 并解压；
2. 运行 `PowerMonitor.exe`，同意 UAC 提权；
3. 左键拖动窗口；右键打开菜单（置顶、估算参数、重置峰值、清零能耗、退出）；左键点托盘图标显示/隐藏。

从源码构建：

```powershell
git clone https://github.com/Dongyurocket/PowerMonitor.git
cd PowerMonitor
dotnet build -c Release
# 或生成单文件免安装版：
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### 说明

- **估算模式**：无电源/电池传感器时，整机功率 =（组件合计 + 基础功耗）÷ 电源效率，右键菜单"整机估算设置"可调。核显功耗已包含在 CPU Package 中时不会重复计入。
- **数据文件**：`settings.json` 与 `energy.json` 保存在 exe 同目录，删除即可重置。
- **诊断**：执行 `PowerMonitor.exe --dump` 会把检测到的全部功耗传感器写入 `sensors.txt`，反馈硬件兼容性问题时请附上。
- 程序未购买代码签名，SmartScreen 提示属正常现象。

### 开源协议

MIT（见 [LICENSE](LICENSE)）。依赖库 LibreHardwareMonitorLib 遵循其自身的 MPL-2.0 协议。
