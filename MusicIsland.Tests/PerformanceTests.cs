using MusicIsland;
using Windows.Media;

namespace MusicIsland.Tests;

public class PerformanceTests
{
    [Fact]
    public void Low_turns_off_everything_that_animates_constantly()
    {
        var low = PerformanceProfile.For(PerformanceMode.Low);

        Assert.False(low.AnimatedBars);
        Assert.False(low.RealAudio);
        Assert.False(low.SongPeek);
        Assert.False(low.SpinViews);
        Assert.False(low.Marquee);
        Assert.Equal(LyricsStyle.None, low.Lyrics);
    }

    [Fact]
    public void Medium_keeps_the_single_lyric_line_at_a_capped_frame_rate()
    {
        var medium = PerformanceProfile.For(PerformanceMode.Medium);

        Assert.Equal(LyricsStyle.Line, medium.Lyrics);
        Assert.True(medium.SongPeek);
        Assert.False(medium.SongFlair);
        Assert.Equal(30, medium.ContinuousFps);
    }

    [Fact]
    public void High_has_the_lyric_stage_flair_and_no_frame_cap()
    {
        var high = PerformanceProfile.For(PerformanceMode.High);

        Assert.Equal(LyricsStyle.Stage, high.Lyrics);
        Assert.True(high.SongFlair);
        Assert.Equal(0, high.ContinuousFps);
    }

    [Fact]
    public void Only_high_draws_on_the_graphics_card_and_keeps_its_memory()
    {
        var low = PerformanceProfile.For(PerformanceMode.Low);
        var medium = PerformanceProfile.For(PerformanceMode.Medium);
        var high = PerformanceProfile.For(PerformanceMode.High);

        Assert.False(low.GraphicsCard);
        Assert.False(medium.GraphicsCard);
        Assert.True(high.GraphicsCard);
        Assert.True(low.TrimMemory);
        Assert.True(medium.TrimMemory);
        Assert.False(high.TrimMemory);
    }

    [Fact]
    public void Each_mode_describes_itself_differently()
    {
        var summaries = Enum.GetValues<PerformanceMode>()
            .Select(mode => PerformanceProfile.For(mode).Summary)
            .ToList();

        Assert.DoesNotContain(summaries, string.IsNullOrWhiteSpace);
        Assert.Equal(summaries.Count, summaries.Distinct().Count());
    }

    [Fact]
    public void Current_line_sits_at_the_anchor_with_others_stacked_around_it()
    {
        var tops = LyricStageLayout.Tops(new double[] { 20, 40, 20 }, 1, 22, 5);

        Assert.Equal(new double[] { -3, 22, 67 }, tops);
    }

    [Fact]
    public void Before_the_first_line_it_waits_at_the_anchor()
    {
        var tops = LyricStageLayout.Tops(new double[] { 20, 20 }, -1, 22, 5);

        Assert.Equal(new double[] { 22, 47 }, tops);
    }

    [Fact]
    public void No_lines_gives_no_positions() =>
        Assert.Empty(LyricStageLayout.Tops(Array.Empty<double>(), -1, 22, 5));

    [Fact]
    public void Line_index_follows_position()
    {
        var lines = new[]
        {
            new LyricsService.Line(TimeSpan.FromSeconds(5), "a"),
            new LyricsService.Line(TimeSpan.FromSeconds(9), "b")
        };

        Assert.Equal(-1, LyricsService.IndexAt(lines, TimeSpan.FromSeconds(1)));
        Assert.Equal(0, LyricsService.IndexAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, LyricsService.IndexAt(lines, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(null, MediaPlaybackAutoRepeatMode.List)]
    [InlineData(MediaPlaybackAutoRepeatMode.None, MediaPlaybackAutoRepeatMode.List)]
    [InlineData(MediaPlaybackAutoRepeatMode.List, MediaPlaybackAutoRepeatMode.Track)]
    [InlineData(MediaPlaybackAutoRepeatMode.Track, MediaPlaybackAutoRepeatMode.None)]
    public void Repeat_button_cycles(MediaPlaybackAutoRepeatMode? from, MediaPlaybackAutoRepeatMode to) =>
        Assert.Equal(to, MediaController.RepeatAfter(from));
}
