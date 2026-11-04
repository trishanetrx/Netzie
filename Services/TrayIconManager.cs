using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using Serilog;

namespace NetSentry.Services
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Window _mainWindow;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _muteItem;
        private bool _isDisposed;
        public bool AlertsMuted { get; set; }

        private DateTime _lastNotificationTime = DateTime.MinValue;
        private string _lastNotifiedProcess = string.Empty;

        public event Action? OpenDashboardRequested;
        public event Action? ExitRequested;
        public event Action<bool>? StartupToggled;
        public event Action<bool>? MuteToggled;

        public TrayIconManager(Window mainWindow)
        {
            _mainWindow = mainWindow;

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateShieldIcon(Color.FromArgb(0, 210, 255)),
                Text = "NetSentry - Bandwidth Guard",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => OpenDashboardRequested?.Invoke());
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);

            _startupItem = new ToolStripMenuItem("Run on Windows Startup", null, (s, e) =>
            {
                if (s is ToolStripMenuItem mi)
                {
                    bool target = !mi.Checked;
                    if (StartupManager.SetRunOnStartup(target, out _))
                    {
                        mi.Checked = target;
                        StartupToggled?.Invoke(target);
                    }
                }
            })
            {
                Checked = StartupManager.IsRunOnStartupEnabled()
            };

            _muteItem = new ToolStripMenuItem("Mute High-Usage Alerts", null, (s, e) =>
            {
                if (s is ToolStripMenuItem mi)
                {
                    bool target = !mi.Checked;
                    SetMuted(target);
                    MuteToggled?.Invoke(target);
                }
            })
            {
                Checked = AlertsMuted
            };

            var exitItem = new ToolStripMenuItem("Exit NetSentry", null, (s, e) => ExitRequested?.Invoke());

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(_startupItem);
            contextMenu.Items.Add(_muteItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.DoubleClick += (s, e) => ToggleWindow();
            _notifyIcon.Click += (s, e) =>
            {
                if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                {
                    ToggleWindow();
                }
            };

            // Clicking the pop-up notification immediately opens the dashboard
            _notifyIcon.BalloonTipClicked += (s, e) =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            };
        }

        public void UpdateStartupState(bool isEnabled)
        {
            _startupItem.Checked = isEnabled;
        }

        public void SetMuted(bool muted)
        {
            AlertsMuted = muted;
            if (_muteItem != null)
            {
                _muteItem.Checked = muted;
            }
        }

        public void ToggleWindow()
        {
            if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }

        public void UpdateTooltip(double rxBytes, double txBytes, long sessionRx = 0, long sessionTx = 0)
        {
            string rx = NetSentry.Models.ProcessNetworkInfo.FormatSpeed(rxBytes);
            string tx = NetSentry.Models.ProcessNetworkInfo.FormatSpeed(txBytes);
            string tip;
            if (sessionRx > 0 || sessionTx > 0)
            {
                string sessTotal = NetSentry.Models.ProcessNetworkInfo.FormatBytes(sessionRx + sessionTx);
                tip = string.Format("NetSentry (Session: {0})\n↓ {1} | ↑ {2}", sessTotal, rx, tx);
            }
            else
            {
                tip = string.Format("NetSentry:\n↓ {0} | ↑ {1}", rx, tx);
            }
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _notifyIcon.Text = tip;
        }

        public void ShowThresholdAlert(string processName, double mbps)
        {
            if (AlertsMuted) return;

            var now = DateTime.UtcNow;
            if (processName == _lastNotifiedProcess && (now - _lastNotificationTime).TotalSeconds < 10)
            {
                return;
            }

            _lastNotificationTime = now;
            _lastNotifiedProcess = processName;

            _notifyIcon.BalloonTipTitle = "⚠️ High Bandwidth Alert!";
            _notifyIcon.BalloonTipText = string.Format("{0} is using {1:F2} Mbps! Click here to review or block.", processName, mbps);
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
            try
            {
                _notifyIcon.ShowBalloonTip(4000);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to display system tray balloon tip notification");
            }
        }

        private static Icon CreateShieldIcon(Color accentColor)
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(accentColor);
            var points = new[]
            {
                new PointF(16, 2),
                new PointF(29, 6),
                new PointF(26, 22),
                new PointF(16, 30),
                new PointF(6, 22),
                new PointF(3, 6)
            };
            g.FillPolygon(brush, points);

            using var innerBrush = new SolidBrush(Color.FromArgb(20, 20, 30));
            g.FillEllipse(innerBrush, 11, 10, 10, 10);

            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
