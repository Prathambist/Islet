namespace Islet.Tests
{
    public class BrowserPreviewsTests
    {
        static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

        [Fact]
        public void TellsMusicFromVideos()
        {
            // Spotify Web, YouTube Music: an album
            Assert.True(BrowserPreviews.IsMusic("Arctic Monkeys", "AM", false));

            // SoundCloud: an artist and a square cover
            Assert.True(BrowserPreviews.IsMusic("Some Producer", "", true));

            // A YouTube video: a channel name, but a wide thumbnail
            Assert.False(BrowserPreviews.IsMusic("Some Channel", "", false));

            // Instagram or X: no artist at all, whatever the picture
            Assert.False(BrowserPreviews.IsMusic("", "", true));
        }

        [Fact]
        public void AppsAndWebMusicAreNeverHeld()
        {
            Assert.False(BrowserPreviews.Holds(MediaSource.Spotify, "", false, null, false));
            Assert.False(BrowserPreviews.Holds(MediaSource.AppleMusic, "", false, Second, false));
            Assert.False(BrowserPreviews.Holds(MediaSource.Other, "", false, null, false));

            // Spotify Web and YouTube Music name an album
            Assert.False(BrowserPreviews.Holds(MediaSource.Browser, "AM", false, Second, false));
        }

        [Fact]
        public void AVideoInTheTabYouOpenedShowsAfterAFewSeconds()
        {
            Assert.True(BrowserPreviews.Holds(MediaSource.Browser, "", false, 2 * Second, true));
            Assert.False(BrowserPreviews.Holds(MediaSource.Browser, "", false, 5 * Second, true));
            Assert.Equal(3 * Second, BrowserPreviews.NextLook(2 * Second, true));
        }

        [Fact]
        public void PreviewsAndFeedClipsAreHeldMuchLonger()
        {
            // Hovering a YouTube thumbnail, or a clip playing in a feed: not the page's title
            Assert.True(BrowserPreviews.Holds(MediaSource.Browser, "", false, 12 * Second, false));
            Assert.Equal(18 * Second, BrowserPreviews.NextLook(12 * Second, false));

            // Kept playing in a background tab for long enough, it's real
            Assert.False(BrowserPreviews.Holds(MediaSource.Browser, "", false, 30 * Second, false));
        }

        [Fact]
        public void APreviewThatStoppedIsntLeftBehindButAShownVideoStays()
        {
            Assert.True(BrowserPreviews.Holds(MediaSource.Browser, "", false, null, true));

            // Paused after it was shown: still shown, paused
            Assert.False(BrowserPreviews.Holds(MediaSource.Browser, "", true, null, false));
        }
    }
}
