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

        [Theory]
        [InlineData("ArcticMonkeysVEVO", "Arctic Monkeys - Do I Wanna Know? (Official Video)")]
        [InlineData("The Strokes", "The Strokes - Selfless (Official Audio)")]
        [InlineData("Some Uploader", "Tame Impala - Let It Happen (Lyrics)")]
        [InlineData("Some Uploader", "Frank Ocean - Nights [Lyric Video]")]
        [InlineData("Billie Eilish", "Billie Eilish - BIRDS OF A FEATHER (Official Music Video)")]
        [InlineData("Some Artist", "Song (Official Visualizer)")]
        [InlineData("Fleetwood Mac - Topic", "Dreams")]
        [InlineData("TaylorSwiftVEVO", "Taylor Swift - Anti-Hero")]
        [InlineData("Radiohead", "Radiohead - Jigsaw Falling Into Place")]
        [InlineData("Radiohead Official", "Radiohead - Creep")]
        [InlineData("arcticmonkeysmusic", "Arctic Monkeys - 505")]
        public void KnowsAMusicVideoByItsTitleOrChannel(string channel, string title) =>
            Assert.True(BrowserPreviews.LooksLikeMusicVideo(channel, title));

        [Theory]
        [InlineData("Mrwhosetheboss", "I bought the cheapest smartphones on the planet.")]
        [InlineData("Some Vlogger", "Reacting to the new Drake video")]
        [InlineData("Lofi Girl", "lofi hip hop radio - beats to relax/study to")]
        [InlineData("Gadget Doodles", "Sony Cyber-shot WX100: Still a Great Compact Camera?")]
        [InlineData("Some Channel", "")]
        [InlineData("Some Reviewer", "Radiohead - Kid A album review")]
        public void OtherVideosArentMusicVideos(string channel, string title) =>
            Assert.False(BrowserPreviews.LooksLikeMusicVideo(channel, title));

        [Fact]
        public void AConfirmedMusicVideoInTheTabInFrontShowsAtOnce()
        {
            var second = TimeSpan.FromSeconds(1);

            Assert.False(BrowserPreviews.Holds(MediaSource.Browser, "", false, second, titleOnScreen: true, knownSong: true));

            // Still a hover preview when it isn't the page you're on
            Assert.True(BrowserPreviews.Holds(MediaSource.Browser, "", false, second, titleOnScreen: false, knownSong: true));
        }

        [Theory]
        [InlineData("(3) Arctic Monkeys - Do I Wanna Know? (Official Video) - YouTube - Brave", "Arctic Monkeys", "YouTube")]
        [InlineData("Let It Happen - YouTube Music - Google Chrome", "Tame Impala", "YouTube Music")]
        [InlineData("Snooze • SZA - Brave", "SZA", "Spotify Web")]
        [InlineData("Spotify – Web Player: Music for everyone - Microsoft Edge", "", "Spotify Web")]
        [InlineData("Dreams by Fleetwood Mac – Apple Music - Google Chrome", "Fleetwood Mac", "Apple Music Web")]
        [InlineData("Stream Some Track by Someone | Listen online for free on SoundCloud - Brave", "Someone", "SoundCloud")]
        [InlineData("Some page - Brave", "Someone", null)]
        public void TellsTheSiteFromItsTab(string window, string artist, string? site) =>
            Assert.Equal(site, BrowserPreviews.SiteOf(window, artist));

        [Theory]
        [InlineData("Radiohead", "Radiohead - Kid A (Full Album)", 50)]
        [InlineData("Lofi Girl", "lofi hip hop mix - beats to relax/study to", 60)]
        [InlineData("Some Channel", "Best of Fleetwood Mac - Greatest Hits Playlist", 75)]
        [InlineData("Some Channel", "Chill songs 2 hours", 120)]
        [InlineData("ArcticMonkeysVEVO", "Arctic Monkeys - Live at Glastonbury 2013 (Official Video)", 70)]
        public void KnowsAMixOrFullAlbum(string channel, string title, int minutes) =>
            Assert.True(BrowserPreviews.LooksLikeMix(channel, title, TimeSpan.FromMinutes(minutes)));

        [Theory]
        [InlineData("Some Channel", "Artist - Song (Radio Mix)", 4)]
        [InlineData("ArcticMonkeysVEVO", "Arctic Monkeys - Do I Wanna Know? (Official Video)", 5)]
        [InlineData("Some Vlogger", "My 2 hour road trip vlog", 120)]
        public void ASongOrAnotherLongVideoIsntAMix(string channel, string title, int minutes) =>
            Assert.False(BrowserPreviews.LooksLikeMix(channel, title, TimeSpan.FromMinutes(minutes)));

        [Theory]
        [InlineData("Ed Sheeran – Shape of You", "Ed Sheeran - Shape of You")]
        [InlineData("Ed Sheeran — Shape of You", "Ed Sheeran - Shape of You")]
        [InlineData("Mahalia - Simmer | A COLORS SHOW", "Mahalia - Simmer")]
        [InlineData("Mahalia | Simmer | A COLORS SHOW", "Mahalia - Simmer")]
        [InlineData("Billie Eilish: bad guy (Official Video)", "Billie Eilish - bad guy (Official Video)")]
        [InlineData("bad guy by Billie Eilish (Official Video)", "Billie Eilish - bad guy (Official Video)")]
        [InlineData("Arctic Monkeys - 505", "Arctic Monkeys - 505")]
        public void ReadsTheUsualWaysVideoTitlesAreWritten(string title, string expected) =>
            Assert.Equal(expected, BrowserPreviews.ArtistDashSong(title));

        [Theory]
        [InlineData("COLORS", "Mahalia - Simmer | A COLORS SHOW")]
        [InlineData("Lyrical Lemonade", "Juice WRLD - Lucid Dreams (Directed by Cole Bennett)")]
        [InlineData("7clouds", "Rihanna - Diamonds")]
        [InlineData("Atlantic Records", "Ed Sheeran – Shape of You")]
        [InlineData("SZA", "SZA ft. Travis Scott - Love Galore")]
        [InlineData("Mark Ronson", "Mark Ronson & Bruno Mars - Uptown Funk")]
        [InlineData("Bruno Mars", "Mark Ronson, Bruno Mars - Uptown Funk")]
        [InlineData("SZA", "Love Galore - SZA ft. Travis Scott")]
        public void KnowsLabelsCuratorsAndJointArtists(string channel, string title) =>
            Assert.True(BrowserPreviews.LooksLikeMusicVideo(channel, title));

        // A curator's video still has to name an artist and song (which the catalogs then check),
        // and someone else posting an artist's song isn't the artist's own channel
        [Theory]
        [InlineData("COLORS", "Behind the scenes at our studio")]
        [InlineData("Some Reviewer", "Mark Ronson & Bruno Mars - Uptown Funk REACTION")]
        public void ACuratorsOtherVideosOrSomeoneElsesArent(string channel, string title) =>
            Assert.False(BrowserPreviews.LooksLikeMusicVideo(channel, title));

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
