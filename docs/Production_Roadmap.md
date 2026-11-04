# NetSentry — Production-Grade Roadmap

> **Current State:** v1.2 Hardened · ~3,400 LOC · 24 files · 29 Unit Tests passing · GitHub CI/CD active  
> **Goal:** Transform NetSentry from a functional prototype into an enterprise-grade, polished desktop sentry.

---

## Table of Contents

- [Codebase Health Assessment](#codebase-health-assessment)
- [Phase 1 — Reliability & Safety (✅ Completed)](#phase-1--reliability--safety--completed)
- [Phase 2 — Architecture Refactoring (Next Priority)](#phase-2--architecture-refactoring-next-priority)
- [Phase 3 — Testing (✅ Foundation Complete)](#phase-3--testing--foundation-complete)
- [Phase 4 — Distribution & Updates](#phase-4--distribution--updates)
- [Phase 5 — Performance & Reliability (In Progress)](#phase-5--performance--reliability-in-progress)
- [Phase 6 — UX Polish](#phase-6--ux-polish)
- [Phase 7 — Documentation & Community (✅ Foundation Complete)](#phase-7--documentation--community--foundation-complete)
- [Phase 8 — Advanced Features](#phase-8--advanced-features)
- [Quick Wins Checklist (10/10 Completed)](#quick-wins-checklist-1010-completed)

---

## Codebase Health Assessment

| Area | Current Score | Previous | Status & Improvements |
|---|:---:|:---:|---|
| **Functionality** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | Real-time monitoring, 1-click firewall control, session history, mute alerts |
| **Logging & Diagnostics** | ⭐⭐⭐⭐ | ⭐ | Serilog file rolling sink active (`%LocalAppData%\NetSentry\logs`), 14-day retention |
| **Error Handling** | ⭐⭐⭐⭐ | ⭐ | Silent error swallowing eliminated; all services log context and errors |
| **Security** | ⭐⭐⭐⭐ | ⭐⭐ | Input sanitization on all `netsh` arguments; `asInvoker` non-elevated safety |
| **Testing** | ⭐⭐⭐ | ☆ | xUnit test suite (`NetSentry.Tests`) with 29 passing unit tests; integrated in CI |
| **Distribution** | ⭐⭐⭐ | ⭐⭐⭐ | CI/CD standalone builds + GitHub Releases, path-filtered triggers |
| **Architecture** | ⭐⭐ | ⭐⭐ | Functioning code-behind; ready for MVVM + DI refactoring |
| **Documentation** | ⭐⭐⭐⭐ | ⭐⭐ | Comprehensive README, MIT License, User Guide, and Production Roadmap |

---

## Phase 1 — Reliability & Safety (✅ Completed)

- [x] **1.1 Structured Logging (Serilog):** Replaced silent `catch { }` blocks in all services (`NetworkMonitorService`, `FirewallManager`, `SessionHistoryManager`, `SettingsManager`, `SystemServiceManager`, `StartupManager`, `App.xaml.cs`). Logs written to `%LocalAppData%\NetSentry\logs\netsentry-YYYYMMDD.log`.
- [x] **1.2 Input Sanitization:** Implemented `FirewallManager.SanitizeArgument()` to strip command injection vectors (`"`, `&`, `|`, `>`, `<`, `;`, `` ` ``, `\r`, `\n`) and collapse consecutive whitespace before passing arguments to `netsh.exe`.
- [x] **1.3 Single-Instance Mutex Guard:** Added `Global\NetSentry_SingleInstance_TM_Ventiqo` in `App.xaml.cs` to prevent conflicting dual instances and tray duplicate icons.
- [x] **1.4 Graceful ETW Fallback:** ETW access denial without elevation is handled gracefully with clear logging, maintaining full operation via Win32 IO counters.
- [x] **1.5 ETW Session Cleanup:** Automatically scans and cleans orphaned `NetSentryKernelSession_*` traces before initializing a new session.

---

## Phase 2 — Architecture Refactoring (Next Priority)

**Priority:** 🟠 High  
**Goal:** Transition from code-behind in `MainWindow.xaml.cs` to a clean MVVM structure with Dependency Injection.

### 2.1 MVVM Pattern
```
ViewModels/
    MainViewModel.cs              ← Top-level app state & tab management
    ProcessListViewModel.cs       ← Process list collection, sorting modes, filtering
    SessionHistoryViewModel.cs    ← Session records, lifetime stats, clear command
    SettingsViewModel.cs          ← Threshold slider, alert mute, startup toggle
```

- Extract `ICommand` implementations (`RelayCommand` / `AsyncRelayCommand`).
- Move all button click handlers out of code-behind into ViewModel commands.
- Keep `MainWindow.xaml.cs` minimal (< 50 lines).

### 2.2 Dependency Injection & Service Interfaces
Extract service abstractions for testability:
- `IFirewallManager`
- `INetworkMonitorService`
- `ISessionHistoryManager`
- `ISettingsManager`
- `IStartupManager`
- `ISystemServiceManager`

---

## Phase 3 — Testing (✅ Foundation Complete)

- [x] **3.1 Test Project Setup:** Created `NetSentry.Tests` targeting `net8.0-windows` with xUnit runner.
- [x] **3.2 Priority Unit Tests (29 tests passing):**
  - Shell argument sanitization across malicious injection inputs
  - Speed formatting (`FormatSpeed`) across B/s, KB/s, MB/s, GB/s
  - Byte volume formatting (`FormatBytes`)
  - Session history duration formatting and JSON round-trip serialization
  - Settings manager default configuration verification
- [x] **3.3 CI Integration:** Added automated `dotnet test` execution in GitHub Actions workflow before building standalone binaries.

---

## Phase 4 — Distribution & Updates

**Priority:** 🟡 Medium  

- [ ] **4.1 Semantic Versioning:** Introduce `MinVer` to derive version numbers automatically from Git release tags (`v1.2.0`, `v1.3.0`).
- [ ] **4.2 Auto-Update via Velopack:** Integrate seamless in-app background update checking connected to GitHub Releases.
- [ ] **4.3 Inno Setup Installer:** Build an optional lightweight `.exe` installer script alongside the portable standalone single-file binary.
- [ ] **4.4 Code Signing:** Self-sign or acquire an authenticode code signing certificate to prevent Windows SmartScreen unknown publisher warnings.

---

## Phase 5 — Performance & Reliability (In Progress)

- [x] **5.1 ListView Virtualization:** Enabled `VirtualizingPanel.IsVirtualizing="True"` and `VirtualizationMode="Recycling"` on `LstProcesses` for smooth scrolling with 100+ processes.
- [x] **5.2 Balanced Thread Priority:** ETW listener thread adjusted from `AboveNormal` to `Normal` priority.
- [x] **5.3 Firewall Query Caching:** Cached outbound firewall rule lookups with 5-second TTL and immediate invalidation on rule modification.
- [ ] **5.4 Windows Firewall COM API:** Migrate from shell-based `netsh` output parsing to native `INetFwPolicy2` COM interfaces.

---

## Phase 6 — UX Polish

- [x] **6.1 High-DPI Awareness:** Configured `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in project settings.
- [ ] **6.2 High-Resolution Multi-Size Icon:** Replace programmatic 32×32 shield icon with a multi-resolution `.ico` asset (16px to 256px).
- [ ] **6.3 Keyboard Shortcuts:** Add `Ctrl+F` (search focus) and `Escape` (minimize to tray).
- [ ] **6.4 Theme Switcher:** Light/Dark theme dictionary support.

---

## Phase 7 — Documentation & Community (✅ Foundation Complete)

- [x] **7.1 Open-Source License:** Added standard MIT License (`LICENSE`).
- [x] **7.2 Code Style Guide:** Added `.editorconfig` enforcing C# and XAML standards.
- [x] **7.3 User Guide:** Created [`docs/USER_GUIDE.md`](USER_GUIDE.md) covering features, tray controls, service lockdown, and log inspection.
- [x] **7.4 Status Badges:** Added CI Build, GitHub Release, and License badges to [`README.md`](../README.md).
- [x] **7.5 CI Optimization:** Configured `paths-ignore` in GitHub Actions workflow so doc and markdown edits do not waste pipeline compute.

---

## Phase 8 — Advanced Features

- [ ] **8.1 Real-Time Bandwidth Sparklines:** Mini trend graphs per process using `LiveCharts2`.
- [ ] **8.2 CSV / Excel Session Data Export:** Export session records for data consumption auditing.
- [ ] **8.3 Scheduled Time-Based Blocking:** Auto-block bandwidth-heavy apps during specific hours (e.g. 9 AM – 5 PM).
- [ ] **8.4 Per-Process Connection Inspector:** Display active remote IP addresses, ports, and domains.

---

## Quick Wins Checklist (10/10 Completed)

- [x] Add `Serilog` and replace silent `catch { }` blocks
- [x] Add single-instance `Mutex` guard in `App.xaml.cs`
- [x] Sanitize `netsh` arguments in `FirewallManager.cs`
- [x] Add `LICENSE` file (MIT)
- [x] Add `.editorconfig` for code style
- [x] Add `dotnet test` step to CI pipeline
- [x] Add `VirtualizingPanel.IsVirtualizing="True"` to process ListView
- [x] Clean up orphaned ETW sessions on app startup
- [x] Add High-DPI awareness (`PerMonitorV2`)
- [x] Add README badges (build status, release, license)

---

*v1.2 • TM • Ventiqo Technologies*
