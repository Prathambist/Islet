namespace MusicIsland.Tests
{
    public class PrivacyWatcherTests
    {
        [Theory]
        [InlineData(133_000_000_000_000_000L, 0L, true)]
        [InlineData(133_000_000_000_000_000L, 133_000_000_100_000_000L, false)]
        [InlineData(0L, 0L, false)]
        public void InUseOnlyWhileStartedAndNotStopped(long start, long stop, bool expected) =>
            Assert.Equal(expected, PrivacyWatcher.IsInUse(start, stop));

        [Fact]
        public void DesktopKeysTurnBackIntoPaths()
        {
            string path = PrivacyWatcher.ExePath(@"C:#Users#DELL#AppData#Local#Discord#app-1.0.9265#Discord.exe");

            Assert.Equal(@"C:\Users\DELL\AppData\Local\Discord\app-1.0.9265\Discord.exe", path);
            Assert.Equal("Discord", PrivacyWatcher.DesktopAppName(path));
            Assert.Equal("Brave", PrivacyWatcher.DesktopAppName(@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe"));
        }

        [Theory]
        [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "WhatsApp")]
        [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "Camera")]
        [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy", "Settings")]
        [InlineData("MicrosoftWindows.Client.CBS_cw5n1h2txyewy", "Windows")]
        [InlineData("Microsoft.ZuneVideo_8wekyb3d8bbwe", "Zune Video")]
        [InlineData("SomeApp", "Some App")]
        public void StoreAppsGetReadableNames(string key, string expected) =>
            Assert.Equal(expected, PrivacyWatcher.PackagedAppName(key));

        [Fact]
        public void CameraTakesTheDotAndBothAreDescribed()
        {
            var both = new PrivacyUse(new[] { "Discord", "Zoom" }, new[] { "Zoom" });

            Assert.True(both.Any);
            Assert.True(both.CameraDot);
            Assert.Equal("Camera: Zoom · Mic: Discord, Zoom", both.Describe());

            var mic = new PrivacyUse(new[] { "Discord" }, Array.Empty<string>());

            Assert.False(mic.CameraDot);
            Assert.Equal("Mic: Discord", mic.Describe());
            Assert.False(PrivacyUse.None.Any);
        }

        [Fact]
        public void SameAppsCountAsNoChange()
        {
            var a = new PrivacyUse(new[] { "Discord" }, Array.Empty<string>());
            var b = new PrivacyUse(new[] { "Discord" }, Array.Empty<string>());

            Assert.True(a.SameAs(b));
            Assert.False(a.SameAs(PrivacyUse.None));
        }
    }
}
