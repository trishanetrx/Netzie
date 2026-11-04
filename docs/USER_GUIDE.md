# NetSentry — User Guide

Welcome to **NetSentry**, an automated Windows background network sentry, bandwidth monitor, and application firewall manager.

Developed by **TM • Ventiqo Technologies**.

---

## 🚀 Quick Start

1. Download the latest standalone **`NetSentry.exe`** from the [GitHub Releases](https://github.com/trishanetrx/Netzie/releases).
2. Run `NetSentry.exe`. No installation, .NET runtime, or dependencies are required.
3. The dashboard opens displaying live upload and download rates.
4. NetSentry resides in your Windows system tray. You can close or minimize the dashboard anytime and NetSentry will continue protecting your bandwidth in the background.

---

## 🎛️ Features Overview

### 1. Active App Traffic & 1-Click Firewall Blocking
- View all active processes making network connections in real time.
- Sort processes by **⚡ Total Rate**, **↓ Download**, **↑ Upload**, **🔤 Process Name**, or **🔒 Blocked First**.
- Filter applications instantly using the search bar.
- Click **🚫 Block** to immediately create an outbound Windows Firewall rule preventing the application from using any internet bandwidth.
- Click **🟢 Unblock** to instantly lift the block and restore access.
- Click **❌ Kill** to terminate runaway or frozen processes.

### 2. High-Bandwidth Alerts & Custom Threshold
- Customize your alert threshold (0.5 Mbps – 50.0 Mbps) with the slider in the top header.
- When an application exceeds the threshold for 5 consecutive seconds, a floating desktop alert and tray notification appear.
- Easily toggle **Mute Alerts** from either the dashboard header or the tray menu when gaming or streaming.

### 3. Windows Update & Background Service Guard
- 1-Click lockdown for aggressive Windows background downloaders:
  - **Windows Update** (`wuauserv`, `WaaSMedicSvc`, `UsoSvc`)
  - **Delivery Optimization** (`DoSvc`)
  - **Background Intelligent Transfer Service** (`BITS`)
  - **Microsoft Store / WinGet background updaters** (`InstallService`, `WindowsPackageManagerServer`)
- Toggle individual service groups or click **Lock All** / **Unlock All**.

### 4. Session History & Data Logs
- Logs data usage across restarts and sessions in `%LocalAppData%\NetSentry\session_history.json`.
- Displays session duration, total download/upload volume, and lifetime data totals.
- Auto-saves every 10 seconds so data is never lost if your PC reboots.

### 5. Windows Startup & Tray Integration
- Check **Run on Windows Startup** in the header or tray menu to start NetSentry minimized on login.
- Hover over the tray shield icon anytime to check current speeds and session data volume.

---

## 📋 System Logs & Diagnostics

Application logs are automatically written with rolling daily retention to:
```
%LocalAppData%\NetSentry\logs\netsentry-YYYYMMDD.log
```
If an unhandled exception or crash occurs, diagnostic details are recorded here.

---

*v1.2 • TM • Ventiqo Technologies*
