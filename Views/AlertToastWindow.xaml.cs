using System;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NetSentry.Models;
using NetSentry.Services;

// Disambiguate WPF types
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace NetSentry.Views
{
    public partial class AlertToastWindow : Window
    {
        private ProcessNetworkInfo? _currentProcess;
        private readonly DispatcherTimer _countdownTimer;
        private int _remainingMilliseconds = 6000;
        private const int TotalDurationMs = 6000;
        private bool _isPaused;

        public event Action<ProcessNetworkInfo>? ProcessBlocked;
        public event Action? DashboardRequested;

        public AlertToastWindow()
        {
            InitializeComponent();

            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        public void ShowAlert(ProcessNetworkInfo proc, double thresholdMbps)
        {
            _currentProcess = proc;

            TxtAppName.Text = proc.ProcessName;
            TxtAppPath.Text = string.Format("PID: {0} • {1}",
                proc.ProcessId,
                string.IsNullOrEmpty(proc.ExecutablePath) ? "Active Socket" : proc.ExecutablePath);

            TxtSpeedMbps.Text = string.Format("{0:F2} Mbps", proc.TotalSpeedMbps);
            TxtSpeedRaw.Text = ProcessNetworkInfo.FormatSpeed(proc.DownloadSpeedBytes + proc.UploadSpeedBytes);
            TxtLimitTag.Text = string.Format("Limit: {0:F1} Mbps", thresholdMbps);

            if (proc.Icon != null)
            {
                ImgAppIcon.Source = proc.Icon;
            }

            // Reset UI states
            PanelButtons.Visibility = Visibility.Visible;
            BannerActionFeedback.Visibility = Visibility.Collapsed;

            // Reset countdown timer
            _remainingMilliseconds = TotalDurationMs;
            ProgressDismiss.Value = 100;
            _isPaused = false;
            _countdownTimer.Stop();
            _countdownTimer.Start();

            // Position at bottom-right corner of screen (above taskbar)
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;

            if (!IsVisible)
            {
                Show();
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch { }
            }
        }

        private void CountdownTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused) return;

            _remainingMilliseconds -= 100;
            if (_remainingMilliseconds <= 0)
            {
                _countdownTimer.Stop();
                Close();
                return;
            }

            ProgressDismiss.Value = (_remainingMilliseconds / (double)TotalDurationMs) * 100.0;
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _isPaused = true;
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _isPaused = false;
        }

        private void BtnBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProcess == null) return;

            _countdownTimer.Stop();

            bool ok = FirewallManager.BlockApplication(_currentProcess.ProcessName, _currentProcess.ExecutablePath, out string err);
            PanelButtons.Visibility = Visibility.Collapsed;
            BannerActionFeedback.Visibility = Visibility.Visible;

            if (ok)
            {
                TxtActionFeedback.Text = string.Format("✓ '{0}' blocked in Windows Firewall!", _currentProcess.ProcessName);
                TxtActionFeedback.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 163));
                ProcessBlocked?.Invoke(_currentProcess);

                // Auto-close toast after 1.8 seconds so user can see confirmation
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                closeTimer.Tick += (s, args) =>
                {
                    closeTimer.Stop();
                    Close();
                };
                closeTimer.Start();
            }
            else
            {
                TxtActionFeedback.Text = string.Format("Error: {0}", err);
                TxtActionFeedback.Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 129));
            }
        }

        private void BtnOpenNetSentry_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            DashboardRequested?.Invoke();
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _countdownTimer.Stop();
            base.OnClosed(e);
        }
    }
}
