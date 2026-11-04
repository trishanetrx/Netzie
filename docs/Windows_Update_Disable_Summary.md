# Windows Update, Store & Network Audit Summary

**Date:** October 29, 2026  
**User:** Trishane  
**Device Connection:** Mobile Hotspot (`Galaxy TM`)  
**Current Status:** Fully Locked & Metered (~0.8 KB/s baseline idle - Zero Background Consumption)

---

## 1. Problem History
* **Incident 1:** ~1.6 GB mobile data consumed before initial mitigation due to Windows Update + Delivery Optimization (`DoSvc`) downloading a 4.67 GB OS update.
* **Incident 2 (Post-Restart):** ~460 MB consumed right after restart because:
  1. **`WindowsPackageManagerServer` & `InstallService`:** Microsoft Store / Desktop App Installer triggered background store app updates.
  2. **`wuauserv` Service Triggers:** Internal Machine/User Policy triggers automatically flipped `wuauserv` startup type back to `Demand Start` (3) and spawned CDN sockets.
  3. **Stale Cache:** 1.32 GB of pending download chunks remained staged in `C:\Windows\SoftwareDistribution\Download`.

---

## 2. Hardening Measures Applied (Persistent Across Reboots)

### A. Wi-Fi Profiles Locked to Metered (`Cost: Fixed`)
Every Wi-Fi profile (including `Galaxy TM`, `Trishane's iPhone`, `Galaxy S24 Ultra`, etc.) is explicitly set to:
```powershell
netsh wlan set profileparameter name="Galaxy TM" cost=Fixed
```
*Effect:* Restricts automatic background downloads across all personal hotspots.

### B. Outbound Service-Level & App-Level Firewall Blocks (8 Total Rules)
Windows Filtering Platform rules permanently block outbound network traffic by Service Name and Executable:
1. **`Block Windows Update Service`** (`service="wuauserv"`)
2. **`Block Delivery Optimization Service`** (`service="DoSvc"`)
3. **`Block BITS Service`** (`service="BITS"`)
4. **`Block Medic Service`** (`service="WaaSMedicSvc"`)
5. **`Block UsoSvc Service`** (`service="UsoSvc"`)
6. **`Block Microsoft Store Install Service`** (`service="InstallService"`)
7. **`Block Windows Package Manager Server`** (`program="...WindowsPackageManagerServer.exe"`)
8. **`Block WinGet Executable`** (`program="...winget.exe"`)

*Effect:* Even if Windows restarts any of these internal services or store updaters, their network sockets are blocked at the kernel firewall level—zero bytes can leave or enter.

### C. Services Stopped and Disabled
* `wuauserv` (Windows Update): **Stopped & Disabled**
* `BITS` (Background Intelligent Transfer Service): **Stopped & Disabled**
* `DoSvc` (Delivery Optimization): **Stopped & Disabled**
* `InstallService` (Microsoft Store Install Service): **Stopped & Disabled**
* `UsoSvc` (Update Orchestrator Service): **Stopped & Disabled**
* `WaaSMedicSvc` (Windows Update Medic): **Stopped & Disabled**

### D. Windows Store Background Updates Disabled
Configured via Group Policy in Registry:
* Path: `HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore`
  * `AutoDownload` = `2` (Never automatically download app updates)
  * `DisableStoreApps` = `1`

### E. Windows Update Group Policy Lock & Blackhole
* Path: `HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`
  * `DisableWindowsUpdateAccess` = `1`
  * `DoNotConnectToWindowsUpdateInternetLocations` = `1`
  * `WUServer` = `http://127.0.0.1:8530`
  * `WUStatusServer` = `http://127.0.0.1:8530`
* Path: `HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU`
  * `NoAutoUpdate` = `1`
  * `UseWUServer` = `1`
  * `AUOptions` = `2` (Notify only)

### F. Service Triggers Deleted
* Executed `sc.exe triggerinfo wuauserv delete` to prevent automatic startup triggers from waking `wuauserv`.

### G. Download Caches Cleaned & Purged
* Emptied Delivery Optimization cache (`Delete-DeliveryOptimizationCache -Force`).
* Emptied SoftwareDistribution cache (`C:\Windows\SoftwareDistribution\Download\*` - 0 MB remaining).

---

## 3. Post-Reboot Verification Guide for the AI Assistant / User

When the user asks you to **"check after reboot"** or **"verify if my data is safe"**, run this verification script in PowerShell:

```powershell
Write-Host "=== 1. LIVE WI-FI THROUGHPUT (Sample over 3 sec) ==="
$s1 = Get-NetAdapterStatistics | Where-Object { $_.Name -eq "Wi-Fi" }
Start-Sleep -Seconds 3
$s2 = Get-NetAdapterStatistics | Where-Object { $_.Name -eq "Wi-Fi" }
$rx = ($s2.ReceivedBytes - $s1.ReceivedBytes) / 3 / 1KB
$tx = ($s2.SentBytes - $s1.SentBytes) / 3 / 1KB
Write-Host "Wi-Fi Download: $([math]::Round($rx, 2)) KB/s | Upload: $([math]::Round($tx, 2)) KB/s"

Write-Host "`n=== 2. METERED STATUS ==="
$wlanOutput = netsh wlan show interfaces
$ssidLine = ($wlanOutput | Select-String -Pattern "^\s*SSID\s*:\s*(.+)$")
if ($ssidLine) {
    $ssid = ($ssidLine.Line -replace "^\s*SSID\s*:\s*", "").Trim()
    Write-Host "Current SSID: $ssid"
    netsh wlan show profile name="$ssid" | Select-String -Pattern "Cost"
}

Write-Host "`n=== 3. UPDATE FIREWALL RULES CHECK ==="
Get-NetFirewallRule | Where-Object { $_.DisplayName -match "Block (Windows Update|Delivery Optimization|BITS|Medic|UsoSvc|Microsoft Store|Windows Package|WinGet)" } | Select-Object DisplayName, Enabled, Direction, Action | Format-Table -AutoSize

Write-Host "`n=== 4. SERVICE STATUS CHECK ==="
Get-Service -Name "wuauserv", "DoSvc", "BITS", "WaaSMedicSvc", "UsoSvc", "InstallService" -ErrorAction SilentlyContinue | Select-Object Name, DisplayName, Status, StartType | Format-Table -AutoSize
```

**Expected Healthy State:**
1. Live download speed should remain under **5 KB/s** (idle baseline).
2. Wi-Fi Cost should show **`Cost: Fixed`**.
3. All 8 Firewall block rules must be **Enabled = True**, **Action = Block**.
4. All update & store services must be **Status = Stopped**, **StartType = Disabled**.

---

## 4. How to Restore / Re-Enable Updates (Future Unlimited Home Wi-Fi)
If Trishane ever connects to unlimited broadband and intentionally wants to run updates:

```powershell
# 1. Remove Firewall Blocks
netsh advfirewall firewall delete rule name="Block Windows Update Service"
netsh advfirewall firewall delete rule name="Block Delivery Optimization Service"
netsh advfirewall firewall delete rule name="Block BITS Service"
netsh advfirewall firewall delete rule name="Block Medic Service"
netsh advfirewall firewall delete rule name="Block UsoSvc Service"
netsh advfirewall firewall delete rule name="Block Microsoft Store Install Service"
netsh advfirewall firewall delete rule name="Block Windows Package Manager Server"
netsh advfirewall firewall delete rule name="Block WinGet Executable"

# 2. Re-enable Services
Set-Service -Name "wuauserv" -StartupType Manual
Set-Service -Name "DoSvc" -StartupType Automatic
Set-Service -Name "BITS" -StartupType Automatic
Set-Service -Name "InstallService" -StartupType Manual
Set-Service -Name "WaaSMedicSvc" -StartupType Manual
Set-Service -Name "UsoSvc" -StartupType Automatic

# 3. Remove Group Policy Overrides
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate" -Name "DisableWindowsUpdateAccess" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate" -Name "DoNotConnectToWindowsUpdateInternetLocations" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate" -Name "WUServer" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate" -Name "WUStatusServer" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU" -Name "NoAutoUpdate" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU" -Name "UseWUServer" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore" -Name "AutoDownload" -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKLM:\SOFTWARE\Policies\Microsoft\WindowsStore" -Name "DisableStoreApps" -ErrorAction SilentlyContinue
```
