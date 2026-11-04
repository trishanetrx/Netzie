# NetSentry - Real-Time Bandwidth Guard & Firewall Sentinel

**NetSentry** is a high-performance Windows desktop application designed to protect mobile hotspot and capped internet connections by monitoring real-time network usage and giving you full 1-click control over background data drains.

---

## What's New in Version 1.2

### 🔕 Reliable Alert Muting
* Mute high-usage alerts with one click directly from the dashboard header (`Mute Alerts` checkbox) or the system tray context menu.
* When muted, all desktop floating toasts, popup banners, and system tray balloons are completely silenced.
* Remembers your muted status across app launches and PC reboots.

### 📶 Multi-Criteria App Sorting (Tab 1)
* **Quick Sort Pills:** Sort active applications instantly by `⚡ Total Rate`, `↓ Download`, `↑ Upload`, `🔤 Name (A-Z)`, or `🔒 Blocked Apps First`.
* **Interactive Column Headers:** Click on `APPLICATION / PROCESS`, `↓ DOWNLOAD`, `↑ UPLOAD`, or `⚡ TOTAL RATE` to toggle sorting with directional indicator arrows (`▲` / `▼`).

### 💎 Classy, Minimalist Design
* Clean, professional layout with subtle, low-contrast footer credits (`v1.2 • TM • Ventiqo Technologies`).
* Removed repetitive titles and awkward context menu watermarks for an authentic, premium tool aesthetic.

---

## What's New in Version 1.1

### ⚡ Windows Update & System Services Manager Tab
Manage built-in Windows Update and Store background services directly from the app GUI:
* **Windows Update & Delivery Optimization (`wuauserv`, `DoSvc`):** 1-Click Toggle Block / Unblock.
* **Microsoft Store & WinGet (`InstallService`, `WindowsPackageManagerServer`, `winget`):** 1-Click Toggle Block / Unblock.
* **BITS Background Transfer (`BITS`):** 1-Click Toggle Block / Unblock.
* **Update Medic & Orchestrator (`WaaSMedic`, `UsoSvc`):** 1-Click Toggle Block / Unblock.
* **Master Controls:**
  * **`[ 🔒 Lock All Updates ]`**: Applies all 8 outbound firewall blocks and disables update services in one click.
  * **`[ 🔓 Unlock All Updates ]`**: Removes all firewall blocks and re-enables services.

---

## Core Features

### 1. Taskbar & System Tray Resident
* Sits in your system tray (notification area) with a dedicated shield icon.
* **Hover Tooltip:** Shows live network speed at a glance (e.g. `NetSentry (Session: 14.5 MB)\n↓ 1.2 MB/s | ↑ 120 KB/s`).
* **Single Click / Double Click:** Instantly toggles the sleek dark mode dashboard.
* **Minimize-to-Tray:** Clicking the minimize or close (`X`) button keeps NetSentry protecting your internet silently in the background.

### 2. Live Speed & Bandwidth Meters
* Accurate total **Download** and **Upload** speeds in KB/s, MB/s, and Mbps.
* Hardware network adapter polling for ultra-low latency.

### 3. High-Bandwidth Detection (> 1 Mbps Alert)
* Continuously inspects active network processes.
* Any app exceeding threshold triggers a floating toast alert and system tray warning (unless muted).
* **Customizable Threshold Slider:** Tune sensitivity anywhere from 0.5 Mbps to 10.0 Mbps.

### 4. One-Click Firewall Block & Unblock
* **1-Click Block:** Outbound Windows Filtering Platform rule blocking all network traffic for that executable.
* **1-Click Unblock:** Delete the firewall rule and restore full internet connectivity.
* **Kill Process:** Instantly terminate stubborn rogue processes.
* **Block Custom Program:** Browse and block any `.exe` on your system.

### 5. Session History & Data Logs
* Automatically tracks every session when NetSentry starts and restarts.
* Logs **Start Time**, **Time Window**, **Duration**, **Download**, **Upload**, and **Total Data Consumed**.
* Persisted to `%LocalAppData%\NetSentry\session_history.json`.

---

## Location, Downloads & Releases

* **GitHub Repository:** [github.com/trishanetrx/Netzie](https://github.com/trishanetrx/Netzie)
* **Automated CI/CD Builds:** Download directly from [GitHub Releases](https://github.com/trishanetrx/Netzie/releases) or the [Actions Tab](https://github.com/trishanetrx/Netzie/actions)
* **Standalone Portable Executable:** `F:\INTERNET\NetSentry\Standalone\NetSentry.exe` (Zero runtime dependencies, copy & run anywhere!)
* **Desktop Shortcut:** `C:\Users\Trishane\Desktop\NetSentry.lnk`
* **Local Source Solution:** `F:\INTERNET\NetSentry` (.NET 8 WPF)
* **Hardening & Security Audit Guide:** [`docs/Windows_Update_Disable_Summary.md`](docs/Windows_Update_Disable_Summary.md)

---

**v1.2 • TM • Ventiqo Technologies**
