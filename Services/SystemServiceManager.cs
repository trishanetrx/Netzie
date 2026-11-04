using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NetSentry.Models;
using Serilog;

namespace NetSentry.Services
{
    public class SystemServiceManager
    {
        public static List<SystemServiceInfo> GetSystemServices()
        {
            bool wuBlocked = IsRuleActive("Block Windows Update Service");
            bool storeBlocked = IsRuleActive("Block Microsoft Store Install Service") || IsRuleActive("Block Windows Package Manager Server");
            bool bitsBlocked = IsRuleActive("Block BITS Service");
            bool medicBlocked = IsRuleActive("Block Medic Service") || IsRuleActive("Block UsoSvc Service");

            return new List<SystemServiceInfo>
            {
                new SystemServiceInfo
                {
                    Id = "windows_update",
                    Title = "Windows Update & Delivery Optimization",
                    Description = "OS Feature & Cumulative Updates (wuauserv, DoSvc)",
                    Icon = "WU",
                    IsBlocked = wuBlocked
                },
                new SystemServiceInfo
                {
                    Id = "ms_store",
                    Title = "Microsoft Store & WinGet",
                    Description = "Store Apps & Desktop App Installer (InstallService, winget)",
                    Icon = "MS",
                    IsBlocked = storeBlocked
                },
                new SystemServiceInfo
                {
                    Id = "bits",
                    Title = "BITS Background Transfer",
                    Description = "Background Intelligent Transfer for Edge, Chrome & component jobs",
                    Icon = "BITS",
                    IsBlocked = bitsBlocked
                },
                new SystemServiceInfo
                {
                    Id = "waas_medic",
                    Title = "Update Medic & Orchestrator",
                    Description = "Windows Update repair engine & auto-schedule triggers (WaaSMedic, UsoSvc)",
                    Icon = "SOS",
                    IsBlocked = medicBlocked
                }
            };
        }

        private static bool IsRuleActive(string ruleName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = string.Format("advfirewall firewall show rule name=\"{0}\"", ruleName),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);
                return output.Contains("Enabled:") && output.Contains("Yes") && output.Contains("Action:") && output.Contains("Block");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to query rule status for '{Rule}'", ruleName);
                return false;
            }
        }

        public static bool ToggleServiceGroup(string groupId, bool block, out string error)
        {
            error = string.Empty;
            switch (groupId)
            {
                case "windows_update":
                    return ToggleWindowsUpdate(block, out error);
                case "ms_store":
                    return ToggleMicrosoftStore(block, out error);
                case "bits":
                    return ToggleBits(block, out error);
                case "waas_medic":
                    return ToggleMedic(block, out error);
                default:
                    error = "Unknown service group ID: " + groupId;
                    return false;
            }
        }

        private static bool ToggleWindowsUpdate(bool block, out string error)
        {
            if (block)
            {
                RunNetsh("advfirewall firewall add rule name=\"Block Windows Update Service\" dir=out action=block service=\"wuauserv\" enable=yes");
                RunNetsh("advfirewall firewall add rule name=\"Block Delivery Optimization Service\" dir=out action=block service=\"DoSvc\" enable=yes");
                RunPowerShell("Stop-Service -Name 'wuauserv', 'DoSvc' -Force -ErrorAction SilentlyContinue; Set-Service -Name 'wuauserv', 'DoSvc' -StartupType Disabled -ErrorAction SilentlyContinue");
            }
            else
            {
                RunNetsh("advfirewall firewall delete rule name=\"Block Windows Update Service\"");
                RunNetsh("advfirewall firewall delete rule name=\"Block Delivery Optimization Service\"");
                RunPowerShell("Set-Service -Name 'wuauserv' -StartupType Manual -ErrorAction SilentlyContinue; Set-Service -Name 'DoSvc' -StartupType Automatic -ErrorAction SilentlyContinue");
            }
            error = string.Empty;
            return true;
        }

        private static bool ToggleMicrosoftStore(bool block, out string error)
        {
            if (block)
            {
                RunNetsh("advfirewall firewall add rule name=\"Block Microsoft Store Install Service\" dir=out action=block service=\"InstallService\" enable=yes");
                
                string wpServerPath = FindDesktopAppInstallerPath();
                if (!string.IsNullOrEmpty(wpServerPath))
                {
                    RunNetsh(string.Format("advfirewall firewall add rule name=\"Block Windows Package Manager Server\" dir=out action=block program=\"{0}\" enable=yes", wpServerPath));
                }
                
                string userApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
                if (File.Exists(userApps))
                {
                    RunNetsh(string.Format("advfirewall firewall add rule name=\"Block WinGet Executable\" dir=out action=block program=\"{0}\" enable=yes", userApps));
                }

                RunPowerShell("Stop-Process -Name 'WindowsPackageManagerServer' -Force -ErrorAction SilentlyContinue; Stop-Service -Name 'InstallService' -Force -ErrorAction SilentlyContinue; Set-Service -Name 'InstallService' -StartupType Disabled -ErrorAction SilentlyContinue");
            }
            else
            {
                RunNetsh("advfirewall firewall delete rule name=\"Block Microsoft Store Install Service\"");
                RunNetsh("advfirewall firewall delete rule name=\"Block Windows Package Manager Server\"");
                RunNetsh("advfirewall firewall delete rule name=\"Block WinGet Executable\"");
                RunPowerShell("Set-Service -Name 'InstallService' -StartupType Manual -ErrorAction SilentlyContinue");
            }
            error = string.Empty;
            return true;
        }

        private static bool ToggleBits(bool block, out string error)
        {
            if (block)
            {
                RunNetsh("advfirewall firewall add rule name=\"Block BITS Service\" dir=out action=block service=\"BITS\" enable=yes");
                RunPowerShell("Stop-Service -Name 'BITS' -Force -ErrorAction SilentlyContinue; Set-Service -Name 'BITS' -StartupType Disabled -ErrorAction SilentlyContinue");
            }
            else
            {
                RunNetsh("advfirewall firewall delete rule name=\"Block BITS Service\"");
                RunPowerShell("Set-Service -Name 'BITS' -StartupType Automatic -ErrorAction SilentlyContinue");
            }
            error = string.Empty;
            return true;
        }

        private static bool ToggleMedic(bool block, out string error)
        {
            if (block)
            {
                RunNetsh("advfirewall firewall add rule name=\"Block Medic Service\" dir=out action=block service=\"WaaSMedicSvc\" enable=yes");
                RunNetsh("advfirewall firewall add rule name=\"Block UsoSvc Service\" dir=out action=block service=\"UsoSvc\" enable=yes");
                RunPowerShell("Stop-Service -Name 'WaaSMedicSvc', 'UsoSvc' -Force -ErrorAction SilentlyContinue; Set-Service -Name 'WaaSMedicSvc', 'UsoSvc' -StartupType Disabled -ErrorAction SilentlyContinue");
            }
            else
            {
                RunNetsh("advfirewall firewall delete rule name=\"Block Medic Service\"");
                RunNetsh("advfirewall firewall delete rule name=\"Block UsoSvc Service\"");
                RunPowerShell("Set-Service -Name 'WaaSMedicSvc' -StartupType Manual -ErrorAction SilentlyContinue; Set-Service -Name 'UsoSvc' -StartupType Automatic -ErrorAction SilentlyContinue");
            }
            error = string.Empty;
            return true;
        }

        private static string FindDesktopAppInstallerPath()
        {
            try
            {
                string windowsApps = @"C:\Program Files\WindowsApps";
                if (Directory.Exists(windowsApps))
                {
                    var dirs = Directory.GetDirectories(windowsApps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe");
                    if (dirs.Length > 0)
                    {
                        string candidate = Path.Combine(dirs[0], "WindowsPackageManagerServer.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not resolve WindowsPackageManagerServer path");
            }
            return string.Empty;
        }

        private static void RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed running netsh command with args: {Args}", args);
            }
        }

        private static void RunPowerShell(string command)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"" + command + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(6000);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed running PowerShell command: {Command}", command);
            }
        }
    }
}
