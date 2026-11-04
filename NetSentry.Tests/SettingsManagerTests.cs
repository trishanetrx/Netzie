using NetSentry.Services;
using Xunit;

namespace NetSentry.Tests
{
    public class SettingsManagerTests
    {
        [Fact]
        public void AppSettings_DefaultValues_AreSensible()
        {
            var settings = new AppSettings();

            Assert.False(settings.AlertsMuted);
            Assert.Equal(1.0, settings.ThresholdMbps);
            Assert.Equal("UsageDesc", settings.SortMode);
        }
    }
}
