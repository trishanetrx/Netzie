using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NetSentry.Models;

namespace NetSentry.Services
{
    public class FirewallManager
    {
        public static bool BlockApplication(string processName, string exePath, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                error = "Executable path is invalid or file does not exist.";
                return false;
            }

            string ruleName = "NetSentry Block: " + processName;
            string arguments = string.Format("advfirewall firewall add rule name=\"{0}\" dir=out action=block program=\"{1}\" enable=yes", ruleName, exePath);

            return RunNetsh(arguments, out error);
        }

        public static bool BlockService(string serviceName, out string error)
        {
            error = string.Empty;
            string ruleName = "NetSentry Block Service: " + serviceName;
            string arguments = string.Format("advfirewall firewall add rule name=\"{0}\" dir=out action=block service=\"{1}\" enable=yes", ruleName, serviceName);

            return RunNetsh(arguments, out error);
        }

        public static bool UnblockRule(string ruleName, out string error)
        {
            error = string.Empty;
            string arguments = string.Format("advfirewall firewall delete rule name=\"{0}\"", ruleName);
            return RunNetsh(arguments, out error);
        }

        public static bool UnblockApplication(string processName, string exePath, out string error)
        {
            error = string.Empty;
            string ruleName = "NetSentry Block: " + processName;
            
            // Delete by exact rule name
            bool ok = UnblockRule(ruleName, out error);

            // Also delete any existing rules matching the exact program path
            if (!string.IsNullOrEmpty(exePath))
            {
                string args = string.Format("advfirewall firewall delete rule name=all program=\"{0}\"", exePath);
                RunNetsh(args, out _);
            }

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
            catch { }
            return set;
        }

        public static bool KillProcess(int processId, out string error)
        {
            error = string.Empty;
            try
            {
                var proc = Process.GetProcessById(processId);
                proc.Kill(true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static List<BlockedRuleInfo> GetBlockedRules()
        {
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
            }
            catch { }

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
                return false;
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }
    }
}
