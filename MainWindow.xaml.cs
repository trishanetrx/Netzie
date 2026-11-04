using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NetSentry.Models;
using NetSentry.Services;
using NetSentry.Views;

// Disambiguate WPF types from WinForms usings
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace NetSentry
{
    public partial class MainWindow : Window
    {
        private readonly NetworkMonitorService _monitorService;
        private readonly TrayIconManager _trayManager;
        private List<ProcessNetworkInfo> _latestProcesses = new();
        private ProcessNetworkInfo? _currentAlertProcess;
        private bool _isRealExit;
        private AlertToastWindow? _activeToast;
        private readonly Dictionary<string, DateTime> _recentToastTimes = new();
        private HashSet<string> _cachedBlockedProcessNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly SessionHistoryManager _sessionHistory = new();
        private readonly AppSettings _settings;
        private string _currentSortMode = "UsageDesc";

        public MainWindow()
        {
            InitializeComponent();

            _settings = SettingsManager.Load();
            _currentSortMode = _settings.SortMode;

            // Check if launched on Windows startup with --minimized / --tray
            string[] args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
            }

            _monitorService = new NetworkMonitorService();
            _trayManager = new TrayIconManager(this);
            _trayManager.SetMuted(_settings.AlertsMuted);

            _trayManager.OpenDashboardRequested += () =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            _trayManager.ExitRequested += () =>
            {
                _isRealExit = true;
                Close();
            };

            _trayManager.StartupToggled += (enabled) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ChkRunOnStartup.IsChecked = enabled;
                });
            };

            _trayManager.MuteToggled += (muted) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _settings.AlertsMuted = muted;
                    SettingsManager.Save(_settings);
                    ChkMuteAlerts.IsChecked = muted;
                    if (muted && _activeToast != null)
                    {
                        _activeToast.Close();
                        _activeToast = null;
                    }
                });
            };

            _monitorService.TotalSpeedUpdated += (rx, tx, sessionRx, sessionTx) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtDownloadSpeed.Text = ProcessNetworkInfo.FormatSpeed(rx);
                    TxtDownloadMbps.Text = string.Format("{0:F2} Mbps", rx * 8.0 / 1_000_000.0);

                    TxtUploadSpeed.Text = ProcessNetworkInfo.FormatSpeed(tx);
                    TxtUploadMbps.Text = string.Format("{0:F2} Mbps", tx * 8.0 / 1_000_000.0);

                    long sessionTotal = sessionRx + sessionTx;
                    TxtSessionTotal.Text = ProcessNetworkInfo.FormatBytes(sessionTotal);
                    TxtSessionDownload.Text = string.Format("↓ {0}", ProcessNetworkInfo.FormatBytes(sessionRx));
                    TxtSessionUpload.Text = string.Format("↑ {0}", ProcessNetworkInfo.FormatBytes(sessionTx));

                    var elapsed = DateTime.UtcNow - _monitorService.SessionStartTime;
                    TxtSessionDuration.Text = string.Format("⏱️ {0:D2}:{1:D2}:{2:D2}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);

                    _sessionHistory.UpdateCurrentSession(sessionRx, sessionTx);

                    _trayManager.UpdateTooltip(rx, tx, sessionRx, sessionTx);
                });
            };

            _monitorService.ProcessListUpdated += (list) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _latestProcesses = list;
                    ApplyProcessFilter();

                    var topBreach = list.FirstOrDefault(p => p.IsAboveThreshold);
                    if (topBreach != null)
                    {
                        _currentAlertProcess = topBreach;
                        AlertBanner.Visibility = Visibility.Visible;
                        TxtAlertMessage.Text = string.Format("Warning: {0} is consuming {1:F2} Mbps! (Threshold: {2:F1} Mbps)",
                            topBreach.ProcessName, topBreach.TotalSpeedMbps, _monitorService.ThresholdMbps);
                    }
                    else
                    {
                        AlertBanner.Visibility = Visibility.Collapsed;
                    }
                });
            };

            _monitorService.ThresholdBreached += (proc) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_trayManager.AlertsMuted) return;

                    _trayManager.ShowThresholdAlert(proc.ProcessName, proc.TotalSpeedMbps);

                    // Visible desktop floating alert popup (even when minimized or in tray)
                    ShowAlertToast(proc);
                });
            };

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) || a.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
            {
                Hide();
                ShowInTaskbar = true;
            }

            ChkRunOnStartup.IsChecked = StartupManager.IsRunOnStartupEnabled();
            ChkMuteAlerts.IsChecked = _settings.AlertsMuted;
            _monitorService.ThresholdMbps = _settings.ThresholdMbps;
            SliderThreshold.Value = _settings.ThresholdMbps;
            TxtThresholdVal.Text = _settings.ThresholdMbps.ToString("F1");

            UpdateSortVisuals();

            _monitorService.Start();
            RefreshSessionHistory();
            await RefreshAllAsync();
        }

        private void ShowAlertToast(ProcessNetworkInfo proc)
        {
            if (_trayManager.AlertsMuted) return;

            var now = DateTime.UtcNow;
            if (_recentToastTimes.TryGetValue(proc.ProcessName, out var lastTime))
            {
                // Cooldown: 12 seconds per process so user isn't spammed with repeated popups
                if ((now - lastTime).TotalSeconds < 12)
                {
                    return;
                }
            }
            _recentToastTimes[proc.ProcessName] = now;

            if (_activeToast == null || !_activeToast.IsLoaded)
            {
                _activeToast = new AlertToastWindow();
                _activeToast.Closed += (s, e) => _activeToast = null;
                _activeToast.DashboardRequested += () =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };
                _activeToast.ProcessBlocked += async (p) =>
                {
                    await RefreshAllAsync();
                };
            }

            _activeToast.ShowAlert(proc, _monitorService.ThresholdMbps);
        }

        private async Task RefreshAllAsync()
        {
            await RefreshSystemServicesAsync();
            await RefreshBlockedRulesAsync();
        }

        private async Task RefreshSystemServicesAsync()
        {
            var services = await Task.Run(() => SystemServiceManager.GetSystemServices());
            LstSystemServices.ItemsSource = services;
        }

        private async Task RefreshBlockedRulesAsync()
        {
            var rules = await Task.Run(() => FirewallManager.GetBlockedRules());
            _cachedBlockedProcessNames = await Task.Run(() => FirewallManager.GetBlockedProcessNames());
            LstBlockedRules.ItemsSource = rules;
            ApplyProcessFilter();
        }

        private void ApplyProcessFilter()
        {
            foreach (var p in _latestProcesses)
            {
                p.IsBlocked = _cachedBlockedProcessNames.Contains(p.ProcessName) 
                    || (!string.IsNullOrEmpty(p.ExecutablePath) && _cachedBlockedProcessNames.Contains(System.IO.Path.GetFileNameWithoutExtension(p.ExecutablePath)));
            }

            string filter = TxtSearch.Text.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(filter)
                ? _latestProcesses
                : _latestProcesses.Where(p => p.ProcessName.ToLowerInvariant().Contains(filter)).ToList();

            IEnumerable<ProcessNetworkInfo> sorted = filtered;
            switch (_currentSortMode)
            {
                case "UsageDesc":
                    sorted = sorted.OrderByDescending(p => p.TotalSpeedMbps).ThenBy(p => p.ProcessName);
                    break;
                case "DownloadDesc":
                    sorted = sorted.OrderByDescending(p => p.DownloadSpeedBytes).ThenBy(p => p.ProcessName);
                    break;
                case "UploadDesc":
                    sorted = sorted.OrderByDescending(p => p.UploadSpeedBytes).ThenBy(p => p.ProcessName);
                    break;
                case "NameAsc":
                    sorted = sorted.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.TotalSpeedMbps);
                    break;
                case "NameDesc":
                    sorted = sorted.OrderByDescending(p => p.ProcessName, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.TotalSpeedMbps);
                    break;
                case "BlockedFirst":
                    sorted = sorted.OrderByDescending(p => p.IsBlocked)
                                   .ThenByDescending(p => p.TotalSpeedMbps)
                                   .ThenBy(p => p.ProcessName);
                    break;
                default:
                    sorted = sorted.OrderByDescending(p => p.TotalSpeedMbps);
                    break;
            }

            var list = sorted.ToList();
            LstProcesses.ItemsSource = list;
            TxtAppCount.Text = string.Format("{0} Apps Active", list.Count);
        }

        private void BtnSort_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (tag == "NameAsc" && _currentSortMode == "NameAsc")
                {
                    SetSortMode("NameDesc");
                }
                else if (tag == "NameDesc" && _currentSortMode == "NameDesc")
                {
                    SetSortMode("NameAsc");
                }
                else
                {
                    SetSortMode(tag);
                }
            }
        }

        private void SetSortMode(string mode)
        {
            _currentSortMode = mode;
            if (_settings != null)
            {
                _settings.SortMode = mode;
                SettingsManager.Save(_settings);
            }
            UpdateSortVisuals();
            ApplyProcessFilter();
        }

        private void UpdateSortVisuals()
        {
            var activeBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 46, 59));
            var activeFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, 255));
            var activeBorder = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, 255));

            var defaultBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 26, 36));
            var defaultFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(139, 143, 159));
            var defaultBorder = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 42, 59));

            void ApplyBtn(Button btn, bool active)
            {
                if (btn == null) return;
                btn.Background = active ? activeBg : defaultBg;
                btn.Foreground = active ? activeFg : defaultFg;
                btn.BorderBrush = active ? activeBorder : defaultBorder;
            }

            ApplyBtn(BtnSortUsage, _currentSortMode == "UsageDesc");
            ApplyBtn(BtnSortDownload, _currentSortMode == "DownloadDesc");
            ApplyBtn(BtnSortUpload, _currentSortMode == "UploadDesc");
            ApplyBtn(BtnSortName, _currentSortMode == "NameAsc" || _currentSortMode == "NameDesc");
            ApplyBtn(BtnSortBlocked, _currentSortMode == "BlockedFirst");

            if (TxtHeaderSortName != null)
                TxtHeaderSortName.Text = _currentSortMode == "NameAsc" ? " ▲" : (_currentSortMode == "NameDesc" ? " ▼" : "");
            if (TxtHeaderSortDownload != null)
                TxtHeaderSortDownload.Text = _currentSortMode == "DownloadDesc" ? " ▼" : "";
            if (TxtHeaderSortUpload != null)
                TxtHeaderSortUpload.Text = _currentSortMode == "UploadDesc" ? " ▼" : "";
            if (TxtHeaderSortUsage != null)
                TxtHeaderSortUsage.Text = _currentSortMode == "UsageDesc" ? " ▼" : "";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyProcessFilter();
        }

        private void SliderThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_monitorService == null || TxtThresholdVal == null) return;
            double val = Math.Round(e.NewValue, 1);
            _monitorService.ThresholdMbps = val;
            TxtThresholdVal.Text = val.ToString("F1");
            if (_settings != null)
            {
                _settings.ThresholdMbps = val;
                SettingsManager.Save(_settings);
            }
        }

        private void ChkMuteAlerts_Click(object sender, RoutedEventArgs e)
        {
            bool muted = ChkMuteAlerts.IsChecked == true;
            _trayManager.SetMuted(muted);
            if (_settings != null)
            {
                _settings.AlertsMuted = muted;
                SettingsManager.Save(_settings);
            }

            if (muted && _activeToast != null)
            {
                _activeToast.Close();
                _activeToast = null;
            }
        }

        private void ChkRunOnStartup_Click(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ChkRunOnStartup.IsChecked == true;
            if (StartupManager.SetRunOnStartup(isEnabled, out string err))
            {
                _trayManager.UpdateStartupState(isEnabled);
                string status = isEnabled 
                    ? "NetSentry will now start automatically on Windows startup (minimized to system tray)." 
                    : "Automatic startup launch disabled.";
                MessageBox.Show(status, "Startup Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to change startup setting: " + err, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChkRunOnStartup.IsChecked = !isEnabled;
            }
        }

        private void BtnTestAlert_Click(object sender, RoutedEventArgs e)
        {
            var dummy = new ProcessNetworkInfo
            {
                ProcessId = Environment.ProcessId,
                ProcessName = "chrome.exe",
                ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                DownloadSpeedBytes = 3_450_000,
                UploadSpeedBytes = 220_000,
                TotalSpeedMbps = 29.36,
                IsAboveThreshold = true
            };
            ShowAlertToast(dummy);
        }

        private void BtnResetSession_Click(object sender, RoutedEventArgs e)
        {
            _monitorService.ResetSessionStats();
            _sessionHistory.StartNewSession();
            RefreshSessionHistory();
            TxtSessionTotal.Text = "0.0 B";
            TxtSessionDownload.Text = "↓ 0.0 B";
            TxtSessionUpload.Text = "↑ 0.0 B";
            TxtSessionDuration.Text = "⏱️ 00:00:00";
        }

        private async void BtnToggleService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SystemServiceInfo svc)
            {
                bool targetBlock = !svc.IsBlocked;
                string action = targetBlock ? "Block" : "Allow / Unblock";

                var result = MessageBox.Show(
                    string.Format("{0} '{1}'?\n\n{2}", action, svc.Title, svc.Description),
                    "Confirm Service Change",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    bool ok = SystemServiceManager.ToggleServiceGroup(svc.Id, targetBlock, out string err);
                    if (ok)
                    {
                        MessageBox.Show(string.Format("'{0}' is now {1}!", svc.Title, targetBlock ? "Blocked" : "Active & Allowed"), "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshAllAsync();
                    }
                    else
                    {
                        MessageBox.Show(string.Format("Failed: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnLockAllServices_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Lock ALL Windows Update, Delivery Optimization, Store, and BITS services?\n\nThis will apply firewall blocks and disable background updaters.",
                "Confirm Lock All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var services = SystemServiceManager.GetSystemServices();
                foreach (var svc in services)
                {
                    SystemServiceManager.ToggleServiceGroup(svc.Id, true, out _);
                }
                MessageBox.Show("All Windows OS update services are now LOCKED & BLOCKED!", "Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshAllAsync();
            }
        }

        private async void BtnUnlockAllServices_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Unlock ALL Windows Update, Delivery Optimization, Store, and BITS services?\n\nThis will remove firewall blocks so Windows and Store can update.",
                "Confirm Unlock All",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var services = SystemServiceManager.GetSystemServices();
                foreach (var svc in services)
                {
                    SystemServiceManager.ToggleServiceGroup(svc.Id, false, out _);
                }
                MessageBox.Show("All Windows OS update services are now UNLOCKED & ACTIVE!", "Unlocked", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshAllAsync();
            }
        }

        private async void BtnBlockProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProcessNetworkInfo info)
            {
                if (info.IsBlocked)
                {
                    // UNBLOCK
                    var result = MessageBox.Show(
                        string.Format("Unblock internet access for '{0}'?\n\nTarget: {1}\n\nThis will remove the Windows Firewall rule and restore internet connection.", info.ProcessName, info.ExecutablePath),
                        "Confirm Firewall Unblock",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        bool ok = FirewallManager.UnblockApplication(info.ProcessName, info.ExecutablePath, out string err);
                        if (ok)
                        {
                            info.IsBlocked = false;
                            MessageBox.Show(string.Format("Successfully UNBLOCKED '{0}'!\n\nInternet access has been restored.", info.ProcessName), "Unblocked", MessageBoxButton.OK, MessageBoxImage.Information);
                            await RefreshAllAsync();
                        }
                        else
                        {
                            MessageBox.Show(string.Format("Failed to unblock: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    // BLOCK
                    var result = MessageBox.Show(
                        string.Format("Block internet access for '{0}'?\n\nTarget: {1}\n\nThis will instantly block all outbound traffic using Windows Firewall.", info.ProcessName, info.ExecutablePath),
                        "Confirm Firewall Block",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        bool ok = FirewallManager.BlockApplication(info.ProcessName, info.ExecutablePath, out string err);
                        if (ok)
                        {
                            info.IsBlocked = true;
                            MessageBox.Show(string.Format("Successfully BLOCKED '{0}'!\n\nInternet access is blocked.", info.ProcessName), "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
                            await RefreshAllAsync();
                        }
                        else
                        {
                            MessageBox.Show(string.Format("Failed to block: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void BtnKillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProcessNetworkInfo info)
            {
                var result = MessageBox.Show(
                    string.Format("Terminate process '{0}' (PID: {1}) immediately?", info.ProcessName, info.ProcessId),
                    "Confirm Kill Process",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    if (FirewallManager.KillProcess(info.ProcessId, out string err))
                    {
                        MessageBox.Show(string.Format("Process '{0}' terminated.", info.ProcessName), "Terminated", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(string.Format("Failed to terminate: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnQuickBlockAlert_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAlertProcess != null)
            {
                bool ok = FirewallManager.BlockApplication(_currentAlertProcess.ProcessName, _currentAlertProcess.ExecutablePath, out string err);
                if (ok)
                {
                    MessageBox.Show(string.Format("Blocked '{0}' from using data!", _currentAlertProcess.ProcessName), "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
                    AlertBanner.Visibility = Visibility.Collapsed;
                    await RefreshAllAsync();
                }
                else
                {
                    MessageBox.Show(string.Format("Failed to block: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnUnblockRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BlockedRuleInfo rule)
            {
                var result = MessageBox.Show(
                    string.Format("Unblock rule '{0}'?\n\nInternet access will be restored for this application.", rule.RuleName),
                    "Confirm Unblock",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    bool ok = FirewallManager.UnblockRule(rule.RuleName, out string err);
                    if (ok)
                    {
                        MessageBox.Show(string.Format("Rule '{0}' removed. Internet restored!", rule.RuleName), "Unblocked", MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshAllAsync();
                    }
                    else
                    {
                        MessageBox.Show(string.Format("Failed to unblock: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnAddCustomBlock_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select Application Executable to Block",
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                string exePath = ofd.FileName;
                string procName = System.IO.Path.GetFileNameWithoutExtension(exePath);

                bool ok = FirewallManager.BlockApplication(procName, exePath, out string err);
                if (ok)
                {
                    MessageBox.Show(string.Format("Successfully blocked '{0}'!", procName), "Blocked", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshAllAsync();
                }
                else
                {
                    MessageBox.Show(string.Format("Failed to block: {0}", err), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnRefreshAll_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAllAsync();
        }

        private async void BtnRefreshBlocked_Click(object sender, RoutedEventArgs e)
        {
            await RefreshBlockedRulesAsync();
        }

        private void RefreshSessionHistory()
        {
            try
            {
                var sessions = _sessionHistory.GetAllSessions();
                LstSessionHistory.ItemsSource = sessions;
                var stats = _sessionHistory.GetLifetimeStats();
                TxtLifetimeTotal.Text = ProcessNetworkInfo.FormatBytes(stats.totalCombined);
                TxtLifetimeDownload.Text = string.Format("↓ {0}", ProcessNetworkInfo.FormatBytes(stats.totalRx));
                TxtLifetimeUpload.Text = string.Format("↑ {0}", ProcessNetworkInfo.FormatBytes(stats.totalTx));
                TxtTotalSessionCount.Text = string.Format("({0} Sessions logged)", stats.sessionCount);
            }
            catch { }
        }

        private void BtnRefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            RefreshSessionHistory();
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Clear all previous network session history logs?\n\nThis will keep only your current active session.",
                "Confirm Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _sessionHistory.ClearOldSessions();
                RefreshSessionHistory();
                MessageBox.Show("Past session history cleared.", "History Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                _sessionHistory.EndCurrentSession();
                _activeToast?.Close();
                _monitorService.Dispose();
                _trayManager.Dispose();
            }
        }
    }
}
