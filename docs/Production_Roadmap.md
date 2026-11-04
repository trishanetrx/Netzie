# NetSentry — Production-Grade Roadmap

> **Current State:** v1.2 working prototype · ~3,200 LOC · 19 source files · GitHub CI/CD active  
> **Goal:** Transform NetSentry from a functional prototype into a polished, distributable application.

---

## Table of Contents

- [Codebase Health Assessment](#codebase-health-assessment)
- [Phase 1 — Reliability & Safety](#phase-1--reliability--safety)
- [Phase 2 — Architecture Refactoring](#phase-2--architecture-refactoring)
- [Phase 3 — Testing](#phase-3--testing)
- [Phase 4 — Distribution & Updates](#phase-4--distribution--updates)
- [Phase 5 — Performance & Reliability](#phase-5--performance--reliability)
- [Phase 6 — UX Polish](#phase-6--ux-polish)
- [Phase 7 — Documentation & Community](#phase-7--documentation--community)
- [Phase 8 — Advanced Features](#phase-8--advanced-features)
- [Quick Wins Checklist](#quick-wins-checklist)

---

## Codebase Health Assessment

| Area | Score | Notes |
|---|:---:|---|
| **Functionality** | ⭐⭐⭐⭐ | Core features work — real-time monitoring, per-process blocking, session history |
| **Architecture** | ⭐⭐ | All logic lives in code-behind (`MainWindow.xaml.cs` = 701 lines), no DI, no interfaces |
| **Error Handling** | ⭐ | 48 bare `catch { }` blocks silently swallow every error |
| **Logging** | ⭐ | Only a basic `crash.log` via `AppDomain.UnhandledException` |
| **Testing** | ☆ | Zero unit or integration tests |
| **Security** | ⭐⭐ | `asInvoker` manifest present, but no input sanitization on `netsh` shell calls |
| **Distribution** | ⭐⭐⭐ | CI/CD builds standalone exe + GitHub Release, but no installer or auto-update |
| **Documentation** | ⭐⭐ | README exists, no user guide, license, or contributor docs |

### File-Level Breakdown

| File | Lines | Role |
|---|:---:|---|
| `MainWindow.xaml` | 737 | Dashboard layout (4 tabs) |
| `MainWindow.xaml.cs` | 624 | All UI logic, event handlers, state management |
| `Services/NetworkMonitorService.cs` | 415 | ETW kernel tracing, Win32 IO counters, TCP table enumeration |
| `Services/FirewallManager.cs` | 207 | Windows Firewall via `netsh` — block/unblock apps & services |
| `Services/SystemServiceManager.cs` | 219 | Windows Update / BITS / Store service management |
| `Services/TrayIconManager.cs` | 175 | System tray icon, context menu, balloon notifications |
| `Services/SessionHistoryManager.cs` | 141 | Session data persistence (JSON, auto-save every 10s) |
| `Views/AlertToastWindow.xaml` | 135 | Floating alert popup |
| `Views/AlertToastWindow.xaml.cs` | 135 | Alert toast logic |
| `Models/ProcessNetworkInfo.cs` | 107 | Process model with `INotifyPropertyChanged` |
| `Services/StartupManager.cs` | 59 | Registry-based run-on-startup toggle |
| `Services/SettingsManager.cs` | 49 | Persisted app settings (JSON) |
| `Converters/ValueConverters.cs` | 52 | WPF value converters |
| `Models/SystemServiceInfo.cs` | 51 | Service info model |
| `Models/NetworkSessionRecord.cs` | 37 | Session history record model |
| `Models/BlockedRuleInfo.cs` | 12 | Blocked firewall rule model |
| `App.xaml.cs` | 28 | App entry, crash handler |
| `AssemblyInfo.cs` | 9 | Assembly metadata |
| `App.xaml` | 9 | Application resources |

---

## Phase 1 — Reliability & Safety

**Priority:** 🔴 Critical  
**Estimated effort:** 1–2 weeks

### 1.1 Structured Logging (Replace Silent Error Swallowing)

**Problem:** 48 bare `catch { }` blocks across the codebase. When something goes wrong in production, there is zero diagnostic information.

**Solution:** Add Serilog with rolling file sinks.

```bash
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
```

Initialize in `App.xaml.cs`:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetSentry", "logs", "netsentry-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

Then replace every `catch { }` with `catch (Exception ex) { Log.Warning(ex, "context message"); }`.

**Files with the most silent catches (fix first):**

- `NetworkMonitorService.cs` — 10 silent catches (ETW failures, process enumeration, icon extraction)
- `FirewallManager.cs` — 3 silent catches (firewall operations, rule enumeration)
- `SessionHistoryManager.cs` — 3 silent catches (file I/O, JSON parse)
- `App.xaml.cs` — 2 silent catches (crash handler itself can fail)
- `TrayIconManager.cs` — 1 silent catch (BalloonTip)

### 1.2 Input Sanitization for Shell Commands

**Problem:** `FirewallManager.BlockApplication()` passes `processName` and `exePath` directly into `netsh` arguments without sanitization. A process named `foo" & del /q C:\` would allow command injection.

**Solution:** Sanitize all parameters before constructing shell commands:

```csharp
private static string SanitizeArgument(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;
    return input.Replace("\"", "")
                .Replace("&", "")
                .Replace("|", "")
                .Replace(">", "")
                .Replace("<", "")
                .Replace(";", "")
                .Replace("`", "");
}
```

Apply `SanitizeArgument()` to every parameter in `BlockApplication()`, `BlockService()`, `UnblockRule()`, and `UnblockApplication()` before passing to `RunNetsh()`.

### 1.3 Single-Instance Application Guard

**Problem:** Users can accidentally launch multiple instances. Multiple instances fight over the ETW kernel session and create duplicate tray icons.

**Solution:** Add a named `Mutex` in `App.xaml.cs`:

```csharp
private static Mutex? _appMutex;

protected override void OnStartup(StartupEventArgs e)
{
    _appMutex = new Mutex(true, "Global\\NetSentry_SingleInstance", out bool isNew);
    if (!isNew)
    {
        MessageBox.Show("NetSentry is already running in the system tray.",
            "NetSentry", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown();
        return;
    }
    base.OnStartup(e);
}
```

### 1.4 Graceful ETW Failure Handling

**Problem:** `NetworkMonitorService.StartEtwSession()` silently catches all exceptions when ETW fails (which happens on every non-elevated launch). Users have no idea the app is running in degraded mode.

**Solution:** Surface a status property and display it in the UI:

```csharp
public bool IsEtwActive { get; private set; } = false;
// Set to true on successful ETW start, false on catch
```

Display a subtle badge in the dashboard: `⚡ Full Monitoring` vs `📊 Basic Mode (run as admin for full features)`.

### 1.5 ETW Session Cleanup on Startup

**Problem:** Each app launch creates a uniquely-named ETW session (`NetSentryKernelSession_XXXXXXXX`). If the app crashes, orphaned sessions accumulate in the system.

**Solution:** Before creating a new session, clean up stale ones:

```csharp
foreach (var name in TraceEventSession.GetActiveSessionNames()
    .Where(n => n.StartsWith("NetSentryKernelSession_")))
{
    try { new TraceEventSession(name) { StopOnDispose = true }.Dispose(); }
    catch { /* already cleaned or access denied */ }
}
```

---

## Phase 2 — Architecture Refactoring

**Priority:** 🟠 High  
**Estimated effort:** 2–3 weeks

### 2.1 MVVM Pattern

**Problem:** `MainWindow.xaml.cs` is 701 lines of code-behind containing all business logic, state management, sorting, filtering, and UI updates. This is untestable and difficult to maintain.

**Target structure:**

```
ViewModels/
    MainViewModel.cs              ← Top-level app state, tab navigation
    ProcessListViewModel.cs       ← Process list, sorting, filtering, block/unblock commands
    SessionHistoryViewModel.cs    ← Session display, lifetime stats, clear/refresh
    SettingsViewModel.cs          ← Threshold, mute alerts, startup toggle
    AlertViewModel.cs             ← Alert banner state, quick-block command
```

**Key refactoring steps:**

1. Create `RelayCommand` / `AsyncRelayCommand` implementing `ICommand`
2. Extract sorting logic (`_currentSortMode`, `ApplyProcessFilter()`) → `ProcessListViewModel`
3. Extract session display logic → `SessionHistoryViewModel`
4. Extract alert/threshold logic → `AlertViewModel`
5. Convert all `BtnXxx_Click` handlers to `ICommand` bindings
6. `MainWindow.xaml.cs` should shrink to < 50 lines (just `DataContext = viewModel`)

### 2.2 Dependency Injection

```bash
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Hosting
```

```csharp
// App.xaml.cs
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        // Services
        services.AddSingleton<INetworkMonitorService, NetworkMonitorService>();
        services.AddSingleton<IFirewallManager, FirewallManager>();
        services.AddSingleton<ISessionHistoryManager, SessionHistoryManager>();
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<IStartupManager, StartupManager>();
        services.AddSingleton<ISystemServiceManager, SystemServiceManager>();
        services.AddSingleton<ITrayIconManager, TrayIconManager>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<ProcessListViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    })
    .Build();
```

### 2.3 Extract Service Interfaces

| Current | Interface | Benefit |
|---|---|---|
| `FirewallManager` (static) | `IFirewallManager` | Mock firewall in tests |
| `NetworkMonitorService` | `INetworkMonitorService` | Test without ETW/kernel access |
| `SessionHistoryManager` | `ISessionHistoryManager` | Test without disk I/O |
| `SettingsManager` (static) | `ISettingsManager` | Test with in-memory settings |
| `StartupManager` (static) | `IStartupManager` | Test without registry access |
| `SystemServiceManager` (static) | `ISystemServiceManager` | Test without `sc.exe` |

---

## Phase 3 — Testing

**Priority:** 🟠 High  
**Estimated effort:** 1–2 weeks

### 3.1 Test Project Setup

```bash
dotnet new xunit -n NetSentry.Tests
dotnet sln add NetSentry.Tests/NetSentry.Tests.csproj
dotnet add NetSentry.Tests reference NetSentry.csproj
dotnet add NetSentry.Tests package Moq
dotnet add NetSentry.Tests package FluentAssertions
```

### 3.2 Priority Test Cases

| Test Class | What It Validates |
|---|---|
| `ProcessFilterTests` | All 6 sort modes, search filter, blocked-first ordering, edge cases |
| `FirewallManagerTests` | Argument sanitization, rule name generation, `netsh` output parsing |
| `SessionHistoryTests` | JSON save/load round-trip, `ClearOldSessions`, lifetime stats arithmetic |
| `SettingsManagerTests` | Load defaults on missing file, save/load round-trip, corrupt file handling |
| `ThresholdBreachTests` | 5-second sustained breach detection, cooldown reset on drop, edge at threshold |
| `SpeedFormattingTests` | `FormatSpeed()` and `FormatBytes()` for 0, small, large, and edge values |

### 3.3 Integrate Tests in CI

Add to `.github/workflows/build-standalone.yml` before the Publish step:

```yaml
- name: Run Tests
  run: dotnet test --configuration Release --verbosity normal --logger "trx;LogFileName=test-results.trx"

- name: Upload Test Results
  uses: actions/upload-artifact@v4
  if: always()
  with:
    name: test-results
    path: "**/test-results.trx"
```

---

## Phase 4 — Distribution & Updates

**Priority:** 🟡 Medium  
**Estimated effort:** 1–2 weeks

### 4.1 Semantic Versioning

Replace the current `tag_name: latest` with proper version tags.

```bash
dotnet add package MinVer
```

Add to `NetSentry.csproj`:

```xml
<PropertyGroup>
    <MinVerTagPrefix>v</MinVerTagPrefix>
    <MinVerDefaultPreReleaseIdentifiers>preview.0</MinVerDefaultPreReleaseIdentifiers>
</PropertyGroup>
```

Release workflow: `git tag v1.3.0 && git push --tags`

### 4.2 Auto-Update via Velopack

```bash
dotnet add package Velopack
```

Velopack (successor to Squirrel.Windows) integrates directly with GitHub Releases:

```csharp
var mgr = new UpdateManager(new GithubSource(
    "https://github.com/trishanetrx/Netzie", null, false));
var newVersion = await mgr.CheckForUpdatesAsync();
if (newVersion != null)
{
    await mgr.DownloadUpdatesAsync(newVersion);
    // Prompt user or auto-apply
    mgr.ApplyUpdatesAndRestart(newVersion);
}
```

Features: delta updates (download only changed bytes), automatic rollback, silent background checks.

### 4.3 Installer Options

| Option | Format | Best For | Complexity |
|---|---|---|---|
| **Inno Setup** | `.exe` installer | Personal distribution | Low |
| **WiX v4** | `.msi` | Enterprise / GPO deployment | Medium |
| **MSIX** | `.msix` | Microsoft Store | Medium-High |

**Recommendation:** Start with Inno Setup (single `.iss` script, automatable in CI), graduate to MSIX for Store distribution.

### 4.4 Code Signing Certificate

| Tier | Cost | Provider | SmartScreen |
|---|---|---|---|
| Self-signed | Free | PowerShell `New-SelfSignedCertificate` | ❌ Still shows warnings |
| OV Code Signing | \$70–\$300/yr | DigiCert, Sectigo, SSL.com | ✅ Trusted immediately |
| EV Code Signing | \$300–\$600/yr | Same providers | ✅ Instant SmartScreen trust |

Without a certificate, Windows SmartScreen shows "Unknown publisher" on every download — a significant barrier for user adoption.

---

## Phase 5 — Performance & Reliability

**Priority:** 🟡 Medium  
**Estimated effort:** 1–2 weeks

### 5.1 Memory Leak Prevention

**Problem:** `MainWindow.xaml.cs` subscribes to events on `_monitorService` (`TotalSpeedUpdated`, `ProcessListUpdated`, `ThresholdBreached`) but never unsubscribes. Since the service outlives the window, this is a potential memory leak.

**Fix:** Unsubscribe in `Window_Closing` when `_isRealExit == true`, or use the `WeakEventManager` pattern.

### 5.2 ListView Virtualization

Add virtualization to the process list to handle 100+ active processes efficiently:

```xml
<ListView VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling"
          ScrollViewer.IsDeferredScrollingEnabled="True">
```

### 5.3 Cache Firewall Rules

**Problem:** `GetBlockedRules()` calls `netsh advfirewall firewall show rule name=all dir=out` which dumps every outbound firewall rule on the system. This runs on every UI refresh.

**Fix:**
- Cache results with a 30-second TTL, invalidate on block/unblock operations
- Or migrate to the Windows Firewall COM API (`INetFwPolicy2`) for faster, more reliable access

### 5.4 Reduce ETW Thread Priority

`NetworkMonitorService` starts the ETW thread at `ThreadPriority.AboveNormal`. For a background monitoring app, `Normal` priority is more appropriate to avoid impacting system responsiveness.

---

## Phase 6 — UX Polish

**Priority:** 🟢 Nice-to-have  
**Estimated effort:** 1–2 weeks

### 6.1 High DPI Awareness

Add to `app.manifest` inside the `<application>` block:

```xml
<windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">
        PerMonitorV2
    </dpiAwareness>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">
        true/pm
    </dpiAware>
</windowsSettings>
```

### 6.2 Light/Dark Theme Toggle

Create `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml` resource dictionaries. Allow users to switch in Settings.

### 6.3 Keyboard Accessibility

- `Ctrl+F` → Focus search box
- `Escape` → Minimize to tray
- Logical `TabIndex` across all controls
- `AutomationProperties.Name` on all interactive elements for screen reader support

### 6.4 High-Quality Tray Icon

Replace the programmatically-generated 32×32 shield icon with a proper multi-resolution `.ico` file (16, 24, 32, 48, 256px) embedded as a resource. The current icon appears pixelated on high-DPI displays.

### 6.5 Notification Toast Improvements

- Add a "Don't show again for this app" option on toast alerts
- Respect Windows Focus Assist / Do Not Disturb mode

---

## Phase 7 — Documentation & Community

**Priority:** 🟢 Nice-to-have  
**Estimated effort:** 1 week

### 7.1 Essential Repository Files

| File | Purpose |
|---|---|
| `LICENSE` | MIT License (recommended for open-source distribution) |
| `CONTRIBUTING.md` | Build instructions, coding standards, PR process |
| `CHANGELOG.md` | Version history (auto-generate with `git-cliff` or `standard-version`) |
| `.editorconfig` | Enforce consistent code style across contributors |
| `docs/USER_GUIDE.md` | End-user documentation with screenshots |

### 7.2 GitHub Repository Hygiene

- Enable **branch protection** on `main` (require passing CI, optional PR reviews)
- Add **issue templates** (`bug_report.md`, `feature_request.md`)
- Add **GitHub Discussions** for user Q&A
- Add **README badges**: build status, latest release, license

### 7.3 In-App Help

Add a help button (`?`) in the dashboard header that opens the GitHub wiki or user guide in the default browser.

---

## Phase 8 — Advanced Features

**Priority:** 🔵 Future  
**Estimated effort:** Ongoing

### 8.1 Network Usage Graphs

Use `LiveCharts2` or `ScottPlot` for real-time bandwidth visualization per process — line charts, area charts, or sparklines in the process list.

### 8.2 Data Export

Allow users to export session history as CSV or JSON for analysis:

```
Start Time, End Time, Duration, Downloaded, Uploaded, Total
2026-11-04 09:00, 2026-11-04 17:30, 8h 30m, 2.4 GB, 450 MB, 2.85 GB
```

### 8.3 Scheduled Blocking Rules

Time-based firewall rules: "Block Steam from 9 AM to 5 PM on weekdays." Implement with a background scheduler that adds/removes rules automatically.

### 8.4 Per-App Data Caps

Set data usage limits per application: "Alert me when Chrome exceeds 1 GB in a day." Track cumulative data per process across sessions.

### 8.5 Optional Crash Telemetry (Opt-In Only)

```bash
dotnet add package Sentry
```

Only enable with explicit user consent (checkbox in Settings). Helps identify crashes in the field without users needing to manually report bugs.

### 8.6 Network Connection Details

Show destination IPs, ports, and hostnames per process — turn NetSentry into a lightweight connection monitor (similar to TCPView).

---

## Quick Wins Checklist

These items take under 30 minutes each and deliver immediate value:

- [ ] Add `Serilog` and replace the top 10 most dangerous `catch { }` blocks
- [ ] Add single-instance `Mutex` guard in `App.xaml.cs`
- [ ] Sanitize `netsh` arguments in `FirewallManager.cs`
- [ ] Add `LICENSE` file (MIT)
- [ ] Add `.editorconfig` for code style
- [ ] Add `dotnet test` step to CI pipeline (validates build even with 0 tests)
- [ ] Add `VirtualizingPanel.IsVirtualizing="True"` to process ListView
- [ ] Clean up orphaned ETW sessions on app startup
- [ ] Add DPI awareness to `app.manifest`
- [ ] Add README badges (build status, release, license)

---

## Recommended Execution Order

```
Phase 1 (Reliability)  ─── CRITICAL ───  Do first, no exceptions
    │
Phase 2 (Architecture) ─── HIGH ──────  Enables everything below
    │
Phase 3 (Testing)      ─── HIGH ──────  Safety net for all future changes
    │
Phase 4 (Distribution) ─── MEDIUM ────  Professional delivery
    │
Phase 5 (Performance)  ─── MEDIUM ────  Smooth user experience
    │
Phase 6 (UX Polish)    ─── NICE ──────  Delight users
    │
Phase 7 (Documentation)─── NICE ──────  Community growth
    │
Phase 8 (Advanced)     ─── FUTURE ────  Feature expansion
```

> **Start with Phase 1.** The silent error swallowing and unsanitized shell commands are the highest-risk items. Everything else is built on having solid logging and safe foundations.

---

*v1.2 • TM • Ventiqo Technologies*

