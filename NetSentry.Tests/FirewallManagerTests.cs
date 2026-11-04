using System;
using NetSentry.Services;
using Xunit;

namespace NetSentry.Tests
{
    public class FirewallManagerTests
    {
        [Theory]
        [InlineData("normal_app.exe", "normal_app.exe")]
        [InlineData("app\" & del /q C:\\", "app del /q C:\\")]
        [InlineData("app; rm -rf /", "app rm -rf /")]
        [InlineData("app | malicious", "app malicious")]
        [InlineData("app > out.txt", "app out.txt")]
        [InlineData("app < in.txt", "app in.txt")]
        [InlineData("app`calc`", "appcalc")]
        [InlineData("app\r\nmalicious", "appmalicious")]
        [InlineData("   spaced_name   ", "spaced_name")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void SanitizeArgument_StripsDangerousShellCharacters(string? input, string expected)
        {
            string sanitized = FirewallManager.SanitizeArgument(input);
            Assert.Equal(expected, sanitized);
        }

        [Fact]
        public void BlockApplication_WithNonExistentPath_FailsGracefully()
        {
            string fakePath = @"C:\NonExistent\Folder\FakeProcess12345.exe";
            bool result = FirewallManager.BlockApplication("FakeProcess", fakePath, out string error);
            
            Assert.False(result);
            Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
