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
            var stats = new ListeningStats(folder);

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
            var stats = new ListeningStats(folder);
            stats.Add(Evening.AddDays(-1), "Someone", "Yesterday's song", TimeSpan.FromHours(1));

            Assert.Equal(DaySummary.Empty, stats.For(Evening));
            Assert.Null(ListeningStats.Describe(stats.For(Evening)));
        }

        [Fact]
        public void RemembersBetweenRunsAndTheSummaryIsShownOnce()
        {
            var stats = new ListeningStats(folder);
            stats.Add(Evening, "Arctic Monkeys", "505", TimeSpan.FromMinutes(12));
            stats.MarkSummaryShown(Evening);

            var reopened = new ListeningStats(folder);

            Assert.Equal(TimeSpan.FromMinutes(12), reopened.For(Evening).Total);
            Assert.True(reopened.SummaryShown(Evening));
            Assert.False(reopened.SummaryShown(Evening.AddDays(1)));
        }

        [Fact]
        public void SongsWithoutAnArtistStillCount()
        {
            var stats = new ListeningStats(folder);
            stats.Add(Evening, "", "Some video", TimeSpan.FromMinutes(5));

            var today = stats.For(Evening);

            Assert.Null(today.TopArtist);
            Assert.Equal("Some video", today.TopSong);
            Assert.Equal("Today: 5 min of music", ListeningStats.Describe(today));
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
