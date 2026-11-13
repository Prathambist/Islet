using System.IO;

namespace Islet.Tests
{
    public sealed class ListeningStatsTests : IDisposable
    {
        readonly string folder = Path.Combine(Path.GetTempPath(), "IsletListeningTests", Guid.NewGuid().ToString("N"));
        static readonly DateTime Evening = new(2026, 11, 9, 21, 0, 0);

        public void Dispose()
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }

        [Fact]
        public void AddsUpTheDayAndFindsTheFavourites()
        {
            var stats = new ListeningStats(folder, () => Evening);

            stats.Add(Evening, "Arctic Monkeys", "Do I Wanna Know?", TimeSpan.FromMinutes(50));
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(40));
            stats.Add(Evening, "Tame Impala", "The Less I Know the Better", TimeSpan.FromMinutes(44));

            var today = stats.For(Evening);

            Assert.Equal(TimeSpan.FromMinutes(134), today.Total);
            Assert.Equal("Arctic Monkeys", today.TopArtist);
            Assert.Equal("Do I Wanna Know? — Arctic Monkeys", today.TopSong);
            Assert.Equal("Today: 2 h 14 min of music · mostly Arctic Monkeys", ListeningStats.Describe(today));
        }

        [Fact]
        public void EachDayIsItsOwn()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.AddDays(-1), "Someone", "Yesterday's song", TimeSpan.FromHours(1));

            Assert.Equal(DaySummary.Empty, stats.For(Evening));
            Assert.Null(ListeningStats.Describe(stats.For(Evening)));
        }

        [Fact]
        public void RemembersBetweenRunsAndTheSummaryIsShownOnce()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(12));
            stats.MarkSummaryShown(Evening);

            var reopened = new ListeningStats(folder, () => Evening);

            Assert.Equal(TimeSpan.FromMinutes(12), reopened.For(Evening).Total);
            Assert.True(reopened.SummaryShown(Evening));
            Assert.False(reopened.SummaryShown(Evening.AddDays(1)));
        }

        [Fact]
        public void SongsWithoutAnArtistStillCount()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening, "", "Some video", TimeSpan.FromMinutes(5));

            var today = stats.For(Evening);

            Assert.Null(today.TopArtist);
            Assert.Equal("Some video", today.TopSong);
            Assert.Equal("Today: 5 min of music", ListeningStats.Describe(today));
        }

        [Fact]
        public void ListsTheTopFiveSongsAndTopThreeArtists()
        {
            var stats = new ListeningStats(folder, () => Evening);

            for (int i = 1; i <= 7; i++)
                stats.Add(Evening, $"Artist {i}", $"Song {i}", TimeSpan.FromMinutes(i));

            var songs = stats.TopSongs(Evening, 5);
            var artists = stats.TopArtists(Evening, 3);

            Assert.Equal(new[] { "Song 7", "Song 6", "Song 5", "Song 4", "Song 3" }, songs.Select(s => s.Name));
            Assert.Equal("Artist 7", songs[0].Artist);
            Assert.Equal(TimeSpan.FromMinutes(7), songs[0].Time);
            Assert.Equal(new[] { "Artist 7", "Artist 6", "Artist 5" }, artists.Select(a => a.Name));
            Assert.All(artists, a => Assert.Null(a.Artist));
        }

        [Fact]
        public void ATitleWithADashStillSplitsFromItsArtist()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening, "Daft Punk", "One More Time — Radio Edit", TimeSpan.FromMinutes(3));
            stats.Add(Evening, "", "Some video", TimeSpan.FromMinutes(1));

            var songs = stats.TopSongs(Evening, 5);

            Assert.Equal(("One More Time — Radio Edit", "Daft Punk", TimeSpan.FromMinutes(3)), (songs[0].Name, songs[0].Artist, songs[0].Time));
            Assert.Equal(("Some video", (string?)null, TimeSpan.FromMinutes(1)), (songs[1].Name, songs[1].Artist, songs[1].Time));
        }

        [Fact]
        public void KeepsTheLastWeekOnly()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.AddDays(-7), "Old", "A week ago", TimeSpan.FromMinutes(30));
            stats.Add(Evening.AddDays(-6), "Someone", "Six days ago", TimeSpan.FromMinutes(20));
            stats.Add(Evening, "Someone", "Today", TimeSpan.FromMinutes(10));
            stats.Save();

            var reopened = new ListeningStats(folder, () => Evening);

            Assert.Equal(TimeSpan.FromMinutes(10), reopened.For(Evening).Total);
            Assert.Equal("Six days ago", reopened.TopSongs(Evening.AddDays(-6), 5).Single().Name);
            Assert.Empty(reopened.TopSongs(Evening.AddDays(-7), 5));
        }

        [Fact]
        public void AWeekAddsUpAcrossDays()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.AddDays(-3), "Tame Impala", "Let It Happen", TimeSpan.FromMinutes(20));
            stats.Add(Evening.AddDays(-1), "Tame Impala", "Let It Happen", TimeSpan.FromMinutes(15));
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(30));

            var week = stats.Period(Evening.AddDays(-6), Evening);

            Assert.Equal(TimeSpan.FromMinutes(65), week.Total);
            Assert.Equal(("Let It Happen", "Tame Impala", TimeSpan.FromMinutes(35)), (week.Songs[0].Name, week.Songs[0].Artist, week.Songs[0].Time));
            Assert.Equal(new[] { "Tame Impala", "Arctic Monkeys" }, week.Artists.Select(a => a.Name));
        }

        [Fact]
        public void CountsTheHoursOfTheDay()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.Date.AddHours(8), "A", "Morning", TimeSpan.FromMinutes(10));
            stats.Add(Evening, "A", "Evening", TimeSpan.FromMinutes(25));

            var day = stats.Period(Evening, Evening);

            Assert.Equal(24, day.Hours.Count);
            Assert.Equal(TimeSpan.FromMinutes(10), day.Hours[8]);
            Assert.Equal(TimeSpan.FromMinutes(25), day.Hours[21]);
            Assert.Equal(21, day.PeakHour);
            Assert.Null(stats.Period(Evening.AddDays(-1), Evening.AddDays(-1)).PeakHour);
        }

        [Fact]
        public void GivesEachDayOfTheWeekOldestFirst()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.AddDays(-2), "A", "Song", TimeSpan.FromMinutes(12));
            stats.Add(Evening, "A", "Song", TimeSpan.FromMinutes(5));

            var daily = stats.Daily(Evening, 7);

            Assert.Equal(7, daily.Count);
            Assert.Equal(Evening.Date.AddDays(-6), daily[0].Day);
            Assert.Equal(Evening.Date, daily[6].Day);
            Assert.Equal(TimeSpan.FromMinutes(12), daily[4].Total);
            Assert.Equal(TimeSpan.FromMinutes(5), daily[6].Total);
            Assert.Equal(TimeSpan.Zero, daily[5].Total);
        }

        [Fact]
        public void ADayWithNothingHasEmptyLists()
        {
            var stats = new ListeningStats(folder, () => Evening);

            Assert.Empty(stats.TopSongs(Evening, 5));
            Assert.Empty(stats.TopArtists(Evening, 3));
        }

        static System.Windows.Media.Imaging.BitmapSource Image(int width, int height) =>
            System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96,
                System.Windows.Media.PixelFormats.Gray8, null, new byte[width * height], width);

        static TrackInfo Track(MediaSource source, string album, System.Windows.Media.Imaging.BitmapSource? art, string artist = "Artist", string title = "Title") =>
            new(title, artist, album, true, art, TimeSpan.Zero, TimeSpan.FromMinutes(3),
                DateTimeOffset.Now, source, source.ToString(), "chrome");

        [Fact]
        public void OnlySongsCount()
        {
            // A YouTube video: a channel, but a wide thumbnail, or none yet
            Assert.False(ListeningStats.Counts(Track(MediaSource.Browser, "", Image(1280, 720))));
            Assert.False(ListeningStats.Counts(Track(MediaSource.Browser, "", null)));

            // Instagram or X: no artist (the island fills in the browser's name), whatever the picture
            Assert.False(ListeningStats.Counts(Track(MediaSource.Browser, "", Image(256, 256), artist: "Browser")));

            // A site's icon sent as its artwork isn't a cover
            Assert.False(ListeningStats.Counts(Track(MediaSource.Browser, "", Image(48, 48))));

            // YouTube Music, Spotify Web, SoundCloud: an album, or an artist with a square cover
            Assert.True(ListeningStats.Counts(Track(MediaSource.Browser, "", Image(544, 544))));
            Assert.True(ListeningStats.Counts(Track(MediaSource.Browser, "AM", Image(1280, 720))));

            // Video players and other apps: the same test as the browser
            Assert.False(ListeningStats.Counts(Track(MediaSource.Other, "", Image(1920, 1080))));
            Assert.True(ListeningStats.Counts(Track(MediaSource.Other, "Rumours", null)));

            // The music apps count songs, but not their ads, which name no artist
            Assert.True(ListeningStats.Counts(Track(MediaSource.Spotify, "", null)));
            Assert.True(ListeningStats.Counts(Track(MediaSource.AppleMusic, "", null)));
            Assert.False(ListeningStats.Counts(Track(MediaSource.Spotify, "", null, artist: "Spotify", title: "Advertisement")));
        }

        [Fact]
        public void APlayCountsAfterHalfAMinuteAndOncePerListen()
        {
            var stats = new ListeningStats(folder, () => Evening);

            // Twenty seconds isn't a play; another twenty, without a break, makes it one
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromSeconds(20));
            Assert.Equal(0, stats.Period(Evening, Evening).Songs[0].Plays);

            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromSeconds(20));
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(2));
            Assert.Equal(1, stats.Period(Evening, Evening).Songs[0].Plays);

            // Back to it after another song: a second play
            stats.Add(Evening, "Tame Impala", "Let It Happen", TimeSpan.FromMinutes(1));
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(1));

            var day = stats.Period(Evening, Evening);
            Assert.Equal(2, day.Songs.Single(s => s.Name == "505").Plays);
            Assert.Equal(3, day.Plays);
            Assert.Equal(2, day.Artists.Single(a => a.Name == "Arctic Monkeys").Plays);
        }

        [Fact]
        public void KeepsCoversAndAlbumsAndAnArtistTakesTheirTopSongsCover()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening, "Arctic Monkeys", "Do I Wanna Know?", TimeSpan.FromMinutes(9), "AM", Image(600, 600));
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(4), "Favourite Worst Nightmare", Image(300, 300));
            stats.Add(Evening, "Arctic Monkeys", "R U Mine?", TimeSpan.FromMinutes(5), "AM", Image(600, 600));
            stats.Save();

            var day = new ListeningStats(folder, () => Evening).Period(Evening, Evening);

            // Songs on the same album share one small cover
            var covers = Directory.GetFiles(Path.Combine(folder, "covers"));
            Assert.Equal(2, covers.Length);
            Assert.Equal(day.Songs[0].Cover, day.Songs.Single(s => s.Name == "R U Mine?").Cover);
            Assert.True(File.Exists(day.Songs[0].Cover));

            // Albums add up their songs
            Assert.Equal(("AM", "Arctic Monkeys", TimeSpan.FromMinutes(14)), (day.Albums[0].Name, day.Albums[0].Artist, day.Albums[0].Time));
            Assert.Equal(day.Songs[0].Cover, day.Albums[0].Cover);
            Assert.NotEqual(day.Albums[0].Cover, day.Albums[1].Cover);

            Assert.Equal(day.Songs[0].Cover, day.Artists[0].Cover);
        }

        [Fact]
        public void CoversGoWithTheirWeek()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.AddDays(-7), "Old", "Old song", TimeSpan.FromMinutes(5), "Old album", Image(300, 300));
            stats.Add(Evening, "New", "New song", TimeSpan.FromMinutes(5), "New album", Image(300, 300));
            stats.Save();

            Assert.Single(Directory.GetFiles(Path.Combine(folder, "covers")));
            Assert.Equal("New song", stats.Period(Evening.AddDays(-7), Evening).Songs.Single().Name);
        }

        [Theory]
        [InlineData(30, "under a minute")]
        [InlineData(45 * 60, "45 min")]
        [InlineData(2 * 3600, "2 h")]
        [InlineData(2 * 3600 + 14 * 60, "2 h 14 min")]
        public void DescribesHowLong(int seconds, string expected) =>
            Assert.Equal(expected, ListeningStats.Duration(TimeSpan.FromSeconds(seconds)));
    }
}
