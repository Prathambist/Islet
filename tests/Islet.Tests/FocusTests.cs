namespace Islet.Tests
{
    public class FocusTests
    {
        [Fact]
        public void SummaryCountsByAppBusiestFirst()
        {
            var (title, detail) = FocusSummary.Describe(new[] { "WhatsApp", "Discord", "Discord" });

            Assert.Equal("3 messages while you focused", title);
            Assert.Equal("Discord 2 · WhatsApp 1", detail);
            Assert.Equal("1 message while you focused", FocusSummary.Describe(new[] { "Discord" }).Title);
        }

        [Fact]
        public void DoNotDisturbHasItsOwnWording()
        {
            var (title, _) = FocusSummary.Describe(new[] { "Discord", "Discord" }, QuietReason.DoNotDisturb);

            Assert.Equal("2 messages while Do Not Disturb was on", title);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public void OnlyTheOffProfileLetsMessagesThrough(int profile, bool quiet) =>
            Assert.Equal(quiet, DoNotDisturb.IsQuietProfile(profile));

        [Theory]
        [InlineData(@"C:\Pictures\Screenshots\Screenshot 2026-11-08 151755.png", true)]
        [InlineData(@"C:\Pictures\Screenshots\shot.JPG", true)]
        [InlineData(@"C:\Pictures\Screenshots\desktop.ini", false)]
        [InlineData(@"C:\Pictures\Screenshots\Screenshot.png.tmp", false)]
        public void OnlyImagesAreCaught(string path, bool expected) =>
            Assert.Equal(expected, MainWindow.IsScreenshotFile(path));

        [Fact]
        public void FocusLastsUntilTheTimerIsIdle()
        {
            var now = DateTimeOffset.UnixEpoch;
            var timer = new CountdownTimer(() => now);

            timer.Start(TimeSpan.FromMinutes(25), focus: true);
            Assert.True(timer.Focus);

            now += TimeSpan.FromMinutes(25);
            timer.Update();
            Assert.Equal(CountdownState.Ringing, timer.State);
            Assert.True(timer.Focus);

            timer.Stop();
            Assert.False(timer.Focus);

            timer.Start(TimeSpan.FromMinutes(5));
            Assert.False(timer.Focus);
        }

        [Fact]
        public void FocusSurvivesARestart()
        {
            var now = DateTimeOffset.UnixEpoch;
            var timer = new CountdownTimer(() => now);
            timer.Start(TimeSpan.FromMinutes(25), focus: true);

            var restored = new CountdownTimer(() => now);
            restored.Restore(timer.Snapshot);

            Assert.True(restored.Focus);
            Assert.Equal(CountdownState.Running, restored.State);
        }
    }
}
