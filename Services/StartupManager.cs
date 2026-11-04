using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

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
            catch
            {
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
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
