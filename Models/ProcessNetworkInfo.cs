using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace NetSentry.Models
{
    public class ProcessNetworkInfo : INotifyPropertyChanged
    {
        private int _processId;
        private string _processName = string.Empty;
        private string _executablePath = string.Empty;
        private ImageSource? _icon;
        private double _downloadSpeedBytes;
        private double _uploadSpeedBytes;
        private double _totalSpeedMbps;
        private bool _isAboveThreshold;
        private bool _isBlocked;

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(); }
        }

        public string ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(); }
        }

        public string ExecutablePath
        {
            get => _executablePath;
            set { _executablePath = value; OnPropertyChanged(); }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public double DownloadSpeedBytes
        {
            get => _downloadSpeedBytes;
            set
            {
                _downloadSpeedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DownloadSpeedFormatted));
            }
        }

        public double UploadSpeedBytes
        {
            get => _uploadSpeedBytes;
            set
            {
                _uploadSpeedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UploadSpeedFormatted));
            }
        }

        public double TotalSpeedMbps
        {
            get => _totalSpeedMbps;
            set
            {
                _totalSpeedMbps = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalSpeedFormatted));
            }
        }

        public bool IsAboveThreshold
        {
            get => _isAboveThreshold;
            set { _isAboveThreshold = value; OnPropertyChanged(); }
        }

        public bool IsBlocked
        {
            get => _isBlocked;
            set { _isBlocked = value; OnPropertyChanged(); }
        }

        public string DownloadSpeedFormatted => FormatSpeed(DownloadSpeedBytes);
        public string UploadSpeedFormatted => FormatSpeed(UploadSpeedBytes);
        public string TotalSpeedFormatted => string.Format("{0:F2} Mbps", TotalSpeedMbps);

        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec >= 1024 * 1024 * 1024)
                return string.Format("{0:F2} GB/s", bytesPerSec / (1024 * 1024 * 1024));
            if (bytesPerSec >= 1024 * 1024)
                return string.Format("{0:F2} MB/s", bytesPerSec / (1024 * 1024));
            if (bytesPerSec >= 1024)
                return string.Format("{0:F1} KB/s", bytesPerSec / 1024);
            return string.Format("{0:F0} B/s", bytesPerSec);
        }

        public static string FormatBytes(double totalBytes)
        {
            if (totalBytes >= 1024.0 * 1024 * 1024)
                return string.Format("{0:F2} GB", totalBytes / (1024.0 * 1024 * 1024));
            if (totalBytes >= 1024.0 * 1024)
                return string.Format("{0:F1} MB", totalBytes / (1024.0 * 1024));
            if (totalBytes >= 1024.0)
                return string.Format("{0:F0} KB", totalBytes / 1024.0);
            return string.Format("{0:F0} B", totalBytes);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
