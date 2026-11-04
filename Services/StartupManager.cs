using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Serilog;

namespace NetSentry.Services
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "NetSentry";

        public static bool IsRunOnStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (key == null) return false;
                var val = key.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(val);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not check run-on-startup registry state");
                return false;
            }
        }

        public static bool SetRunOnStartup(bool enable, out string error)
        {
            error = string.Empty;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null)
                {
                    error = "Could not open registry Run key.";
                    Log.Error("Could not access HKCU Run registry key");
                    return false;
                }

                if (enable)
                {
                    // Target the published executable
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NetSentry.exe");
                    
                    // Launch with --minimized flag so it starts quietly in tray on Windows login
                    string command = string.Format("\"{0}\" --minimized", exePath);
                    key.SetValue(AppName, command);
                    Log.Information("Enabled Run-on-Startup: {Cmd}", command);
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    Log.Information("Disabled Run-on-Startup");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error(ex, "Failed to toggle Run-on-Startup state to {Enable}", enable);
                return false;
            }
        }
    }
}
