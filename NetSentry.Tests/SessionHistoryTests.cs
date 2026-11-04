using System;
using System.Text.Json;
using NetSentry.Models;
using Xunit;

namespace NetSentry.Tests
{
    public class SessionHistoryTests
    {
        [Fact]
        public void NetworkSessionRecord_Duration_CalculatedCorrectly()
        {
            var start = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 11, 4, 11, 30, 45, DateTimeKind.Utc);

            var record = new NetworkSessionRecord
            {
                StartTime = start,
                EndTime = end,
                DownloadBytes = 500 * 1024 * 1024,
                UploadBytes = 50 * 1024 * 1024
            };

            Assert.Equal(TimeSpan.FromHours(1.5) + TimeSpan.FromSeconds(45), record.Duration);
            Assert.Equal("1h 30m 45s", record.DurationFormatted);
            Assert.Equal(550L * 1024 * 1024, record.TotalBytes);
        }

        [Fact]
        public void NetworkSessionRecord_JsonSerializationRoundTrip()
        {
            var record = new NetworkSessionRecord
            {
                StartTime = DateTime.UtcNow.AddMinutes(-30),
                EndTime = DateTime.UtcNow,
                DownloadBytes = 1048576,
                UploadBytes = 524288,
                IsCurrentSession = false
            };

            string json = JsonSerializer.Serialize(record);
            var deserialized = JsonSerializer.Deserialize<NetworkSessionRecord>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(record.Id, deserialized!.Id);
            Assert.Equal(record.DownloadBytes, deserialized.DownloadBytes);
            Assert.Equal(record.UploadBytes, deserialized.UploadBytes);
        }
    }
}
