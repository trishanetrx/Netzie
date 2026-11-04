using System;
using NetSentry.Models;

namespace NetSentry.Models
{
    public class NetworkSessionRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime EndTime { get; set; } = DateTime.UtcNow;
        public long DownloadBytes { get; set; }
        public long UploadBytes { get; set; }
        public bool IsCurrentSession { get; set; }

        public long TotalBytes => DownloadBytes + UploadBytes;

        public string StartTimeFormatted => StartTime.ToLocalTime().ToString("MMM dd, yyyy  hh:mm tt");
        
        public string TimeRangeFormatted => string.Format("{0} - {1}", 
            StartTime.ToLocalTime().ToString("hh:mm tt"), 
            IsCurrentSession ? "Active" : EndTime.ToLocalTime().ToString("hh:mm tt"));

        public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

        public string DurationFormatted
        {
            get
            {
                var d = Duration;
                if (d.TotalHours >= 1)
                    return string.Format("{0}h {1}m {2}s", (int)d.TotalHours, d.Minutes, d.Seconds);
                if (d.TotalMinutes >= 1)
                    return string.Format("{0}m {1}s", d.Minutes, d.Seconds);
                return string.Format("{0}s", Math.Max(1, d.Seconds));
            }
        }

        public string DownloadFormatted => ProcessNetworkInfo.FormatBytes(DownloadBytes);
        public string UploadFormatted => ProcessNetworkInfo.FormatBytes(UploadBytes);
        public string TotalFormatted => ProcessNetworkInfo.FormatBytes(TotalBytes);
        public string StatusText => IsCurrentSession ? "🟢 Active Session" : "Completed";
    }
}
