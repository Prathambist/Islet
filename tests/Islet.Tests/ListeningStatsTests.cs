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

            Assert.Equal(new Ranked("One More Time — Radio Edit", "Daft Punk", TimeSpan.FromMinutes(3)), songs[0]);
            Assert.Equal(new Ranked("Some video", null, TimeSpan.FromMinutes(1)), songs[1]);
        }

        [Fact]
        public void KeepsTodayAndYesterdayOnly()
        {
            var stats = new ListeningStats(folder, () => Evening);
            stats.Add(Evening.AddDays(-2), "Old", "Two days ago", TimeSpan.FromMinutes(30));
            stats.Add(Evening.AddDays(-1), "Someone", "Yesterday", TimeSpan.FromMinutes(20));
            stats.Add(Evening, "Someone", "Today", TimeSpan.FromMinutes(10));
            stats.Save();

            var reopened = new ListeningStats(folder, () => Evening);

            Assert.Equal(TimeSpan.FromMinutes(10), reopened.For(Evening).Total);
            Assert.Equal("Yesterday", reopened.TopSongs(Evening.AddDays(-1), 5).Single().Name);
            Assert.Empty(reopened.TopSongs(Evening.AddDays(-2), 5));
        }

        [Fact]
        public void ADayWithNothingHasEmptyLists()
        {
            var stats = new ListeningStats(folder, () => Evening);

            Assert.Empty(stats.TopSongs(Evening, 5));
            Assert.Empty(stats.TopArtists(Evening, 3));
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
