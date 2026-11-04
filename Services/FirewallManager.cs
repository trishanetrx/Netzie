using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NetSentry.Models;
using Serilog;

namespace NetSentry.Services
{
    public class FirewallManager
    {
        private static readonly object _cacheLock = new();
        private static List<BlockedRuleInfo>? _cachedRules;
        private static DateTime _lastRulesFetch = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

        public static string SanitizeArgument(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string clean = input
                .Replace("\"", "")
                .Replace("&", "")
                .Replace("|", "")
                .Replace(">", "")
                .Replace("<", "")
                .Replace(";", "")
                .Replace("`", "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            while (clean.Contains("  "))
            {
                clean = clean.Replace("  ", " ");
            }

            return clean;
        }

        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedRules = null;
                _lastRulesFetch = DateTime.MinValue;
            }
        }

        public static bool BlockApplication(string processName, string exePath, out string error)
        {
            error = string.Empty;
            string cleanPath = SanitizeArgument(exePath);
            string cleanName = SanitizeArgument(processName);

            if (string.IsNullOrWhiteSpace(cleanPath) || !File.Exists(cleanPath))
            {
                error = "Executable path is invalid or file does not exist.";
                Log.Warning("BlockApplication rejected invalid executable path: {Path}", exePath);
                return false;
            }

            string ruleName = "NetSentry Block: " + cleanName;
            string arguments = string.Format("advfirewall firewall add rule name=\"{0}\" dir=out action=block program=\"{1}\" enable=yes", ruleName, cleanPath);

            Log.Information("Attempting to block application '{Name}' at '{Path}'", cleanName, cleanPath);
            bool success = RunNetsh(arguments, out error);
            if (success)
            {
                InvalidateCache();
                Log.Information("Successfully blocked application '{Name}'", cleanName);
            }
            else
            {
                Log.Error("Failed to block application '{Name}': {Error}", cleanName, error);
            }

            return success;
        }

        public static bool BlockService(string serviceName, out string error)
        {
            error = string.Empty;
            string cleanService = SanitizeArgument(serviceName);
            string ruleName = "NetSentry Block Service: " + cleanService;
            string arguments = string.Format("advfirewall firewall add rule name=\"{0}\" dir=out action=block service=\"{1}\" enable=yes", ruleName, cleanService);

            Log.Information("Attempting to block service '{Service}'", cleanService);
            bool success = RunNetsh(arguments, out error);
            if (success)
            {
                InvalidateCache();
                Log.Information("Successfully blocked service '{Service}'", cleanService);
            }
            else
            {
                Log.Error("Failed to block service '{Service}': {Error}", cleanService, error);
            }

            return success;
        }

        public static bool UnblockRule(string ruleName, out string error)
        {
            error = string.Empty;
            string cleanRule = SanitizeArgument(ruleName);
            string arguments = string.Format("advfirewall firewall delete rule name=\"{0}\"", cleanRule);

            Log.Information("Attempting to delete firewall rule '{Rule}'", cleanRule);
            bool success = RunNetsh(arguments, out error);
            if (success)
            {
                InvalidateCache();
                Log.Information("Successfully removed firewall rule '{Rule}'", cleanRule);
            }
            else
            {
                Log.Warning("Failed to delete firewall rule '{Rule}': {Error}", cleanRule, error);
            }

            return success;
        }

        public static bool UnblockApplication(string processName, string exePath, out string error)
        {
            error = string.Empty;
            string cleanName = SanitizeArgument(processName);
            string cleanPath = SanitizeArgument(exePath);

            string ruleName = "NetSentry Block: " + cleanName;
            
            // Delete by exact rule name
            bool ok = UnblockRule(ruleName, out error);

            // Also delete any existing rules matching the exact program path
            if (!string.IsNullOrEmpty(cleanPath))
            {
                string args = string.Format("advfirewall firewall delete rule name=all program=\"{0}\"", cleanPath);
                RunNetsh(args, out _);
            }

            InvalidateCache();
            return ok;
        }

        public static HashSet<string> GetBlockedProcessNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rules = GetBlockedRules();
                foreach (var r in rules)
                {
                    if (r.RuleName.StartsWith("NetSentry Block: ", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = r.RuleName.Substring("NetSentry Block: ".Length).Trim();
                        set.Add(name);
                    }
                    if (!string.IsNullOrEmpty(r.Target) && r.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string fn = Path.GetFileNameWithoutExtension(r.Target);
                        if (!string.IsNullOrEmpty(fn)) set.Add(fn);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error compiling blocked process names set");
            }
            return set;
        }

        public static bool KillProcess(int processId, out string error)
        {
            error = string.Empty;
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Kill(true);
                Log.Information("Successfully terminated process {PID} ({Name})", processId, proc.ProcessName);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Warning(ex, "Failed to terminate process {PID}", processId);
                return false;
            }
        }

        public static List<BlockedRuleInfo> GetBlockedRules()
        {
            lock (_cacheLock)
            {
                if (_cachedRules != null && (DateTime.UtcNow - _lastRulesFetch) < CacheDuration)
                {
                    return new List<BlockedRuleInfo>(_cachedRules);
                }
            }

            var list = new List<BlockedRuleInfo>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "advfirewall firewall show rule name=all dir=out",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);

                    string currentName = string.Empty;
                    bool currentEnabled = false;
                    string currentProgram = string.Empty;
                    string currentService = string.Empty;
                    string currentAction = string.Empty;

                    using var reader = new StringReader(output);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.StartsWith("Rule Name:"))
                        {
                            if (currentAction == "Block" && !string.IsNullOrEmpty(currentName))
                            {
                                list.Add(new BlockedRuleInfo
                                {
                                    RuleName = currentName,
                                    Target = !string.IsNullOrEmpty(currentProgram) && currentProgram != "Any" 
                                        ? currentProgram 
                                        : (!string.IsNullOrEmpty(currentService) && currentService != "Any" ? "Service: " + currentService : "General Outbound"),
                                    Type = !string.IsNullOrEmpty(currentService) && currentService != "Any" ? "Service" : "Program",
                                    Enabled = currentEnabled
                                });
                            }

                            currentName = line.Substring("Rule Name:".Length).Trim();
                            currentEnabled = false;
                            currentProgram = string.Empty;
                            currentService = string.Empty;
                            currentAction = string.Empty;
                        }
                        else if (line.StartsWith("Enabled:"))
                        {
                            currentEnabled = line.Substring("Enabled:".Length).Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (line.StartsWith("Program:"))
                        {
                            currentProgram = line.Substring("Program:".Length).Trim();
                        }
                        else if (line.StartsWith("Service:"))
                        {
                            currentService = line.Substring("Service:".Length).Trim();
                        }
                        else if (line.StartsWith("Action:"))
                        {
                            currentAction = line.Substring("Action:".Length).Trim();
                        }
                    }

                    if (currentAction == "Block" && !string.IsNullOrEmpty(currentName))
                    {
                        list.Add(new BlockedRuleInfo
                        {
                            RuleName = currentName,
                            Target = !string.IsNullOrEmpty(currentProgram) && currentProgram != "Any" 
                                ? currentProgram 
                                : (!string.IsNullOrEmpty(currentService) && currentService != "Any" ? "Service: " + currentService : "General Outbound"),
                            Type = !string.IsNullOrEmpty(currentService) && currentService != "Any" ? "Service" : "Program",
                            Enabled = currentEnabled
                        });
                    }
                }

                lock (_cacheLock)
                {
                    _cachedRules = list;
                    _lastRulesFetch = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to retrieve firewall blocked rules via netsh");
            }

            return list;
        }

        private static bool RunNetsh(string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    output = "Failed to launch netsh process.";
                    Log.Error("Could not start netsh.exe process");
                    return false;
                }

                output = proc.StandardOutput.ReadToEnd();
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);

                if (proc.ExitCode == 0 || output.Contains("Ok.") || output.Contains("The rule was deleted"))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(err)) output += " " + err;
                Log.Warning("netsh returned non-zero ({Code}): {Output}", proc.ExitCode, output);
                return false;
            }
            catch (Exception ex)
            {
                output = ex.Message;
                Log.Error(ex, "Exception running netsh: {Args}", arguments);
                return false;
            }
        }
    }
}
