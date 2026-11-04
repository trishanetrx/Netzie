using NetSentry.Models;
using Xunit;

namespace NetSentry.Tests
{
    public class FormattingTests
    {
        [Theory]
        [InlineData(0, "0 B/s")]
        [InlineData(500, "500 B/s")]
        [InlineData(1024, "1.0 KB/s")]
        [InlineData(1536, "1.5 KB/s")]
        [InlineData(1024 * 1024, "1.00 MB/s")]
        [InlineData(25 * 1024 * 1024, "25.00 MB/s")]
        [InlineData(1024L * 1024 * 1024, "1.00 GB/s")]
        public void FormatSpeed_FormatsAccuratelyAcrossUnits(double bytesPerSec, string expected)
        {
            string formatted = ProcessNetworkInfo.FormatSpeed(bytesPerSec);
            Assert.Equal(expected, formatted);
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(512, "512 B")]
        [InlineData(1024, "1 KB")]
        [InlineData(1024 * 1024, "1.0 MB")]
        [InlineData(1500 * 1024, "1.5 MB")]
        [InlineData(1024L * 1024 * 1024, "1.00 GB")]
        [InlineData(2500L * 1024 * 1024, "2.44 GB")]
        public void FormatBytes_FormatsAccuratelyAcrossUnits(double totalBytes, string expected)
        {
            string formatted = ProcessNetworkInfo.FormatBytes(totalBytes);
            Assert.Equal(expected, formatted);
        }
    }
}
