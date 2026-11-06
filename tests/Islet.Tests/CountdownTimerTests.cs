using System.IO;
using Islet;

namespace Islet.Tests;

public class CountdownTimerTests
{
    DateTimeOffset now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    CountdownTimer NewTimer() => new(() => now);

    void Pass(double seconds) => now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Counts_down_from_the_clock_alone()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromMinutes(5));
        Pass(60);

        Assert.Equal(CountdownState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(4), timer.Remaining);
        Assert.Equal(0.8, timer.Fraction, 3);
    }

    [Fact]
    public void Rings_at_the_end_even_when_updates_arrive_late()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromSeconds(10));
        Pass(9.9);
        timer.Update();
        Assert.Equal(CountdownState.Running, timer.State);

        // The PC slept through the end
        Pass(3600);
        timer.Update();

        Assert.Equal(CountdownState.Ringing, timer.State);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Fact]
    public void Alert_runs_its_full_length_from_when_it_was_noticed()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromSeconds(1));
        Pass(600);
        timer.Update();

        Pass(29);
        timer.Update();
        Assert.Equal(CountdownState.Ringing, timer.State);

        Pass(1);
        timer.Update();
        Assert.Equal(CountdownState.Idle, timer.State);
    }

    [Fact]
    public void Pause_holds_the_time_and_resume_moves_the_end()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromSeconds(100));
        Pass(40);
        timer.Pause();
        Pass(1000);
        timer.Update();

        Assert.Equal(CountdownState.Paused, timer.State);
        Assert.Equal(TimeSpan.FromSeconds(60), timer.Remaining);

        timer.Resume();
        Pass(10);

        Assert.Equal(CountdownState.Running, timer.State);
        Assert.Equal(TimeSpan.FromSeconds(50), timer.Remaining);
    }

    [Fact]
    public void Adding_time_works_running_paused_and_ringing()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromSeconds(60));
        Pass(30);
        timer.AddTime(TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(90), timer.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(90), timer.Total);

        timer.Pause();
        timer.AddTime(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(100), timer.Remaining);

        timer.Resume();
        Pass(100);
        timer.Update();
        Assert.Equal(CountdownState.Ringing, timer.State);

        timer.AddTime(TimeSpan.FromMinutes(1));
        Assert.Equal(CountdownState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);
    }

    [Fact]
    public void Adding_time_stops_at_the_longest_length()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromHours(23));
        timer.AddTime(TimeSpan.FromHours(5));

        Assert.Equal(CountdownTimer.Longest, timer.Remaining);
    }

    [Fact]
    public void Stop_ends_every_state_and_announces_each_change()
    {
        var timer = NewTimer();
        int changes = 0;
        timer.Changed += () => changes++;

        timer.Stop();
        Assert.Equal(0, changes);

        timer.Start(TimeSpan.FromSeconds(5));
        timer.Stop();

        Assert.Equal(CountdownState.Idle, timer.State);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Controls_that_do_not_apply_are_ignored()
    {
        var timer = NewTimer();

        timer.Start(TimeSpan.Zero);
        timer.Pause();
        timer.Resume();
        timer.AddTime(TimeSpan.FromMinutes(1));

        Assert.Equal(CountdownState.Idle, timer.State);
    }

    [Fact]
    public void Next_tick_lands_when_the_shown_second_changes()
    {
        var timer = NewTimer();
        timer.Start(TimeSpan.FromSeconds(10));
        Pass(0.25);

        Assert.Equal(TimeSpan.FromSeconds(0.75), timer.UntilNextSecond);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(0.2, "0:01")]
    [InlineData(45, "0:45")]
    [InlineData(750, "12:30")]
    [InlineData(3900, "1:05:00")]
    public void Clock_reading_rounds_up(double seconds, string expected)
    {
        Assert.Equal(expected, TimerText.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(45, "45")]
    [InlineData(60, "1")]
    [InlineData(61, "2")]
    [InlineData(3600, "60")]
    [InlineData(3601, "2H")]
    [InlineData(86400, "24")]
    public void Short_reading_fits_two_characters(double seconds, string expected)
    {
        Assert.Equal(expected, TimerText.Short(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData("5", 300)]
    [InlineData(" 2.5 ", 150)]
    [InlineData("1:30", 90)]
    [InlineData("1:00:00", 3600)]
    [InlineData("90s", 90)]
    [InlineData("25m", 1500)]
    [InlineData("2h", 7200)]
    [InlineData("1h30", 5400)]
    [InlineData("1h 30m", 5400)]
    [InlineData("24h", 86400)]
    public void Reads_typed_lengths(string input, double seconds)
    {
        Assert.True(TimerText.TryParse(input, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1:2:3:4")]
    [InlineData("25h")]
    public void Refuses_lengths_it_cannot_use(string? input)
    {
        Assert.False(TimerText.TryParse(input, out _));
    }

    [Fact]
    public void Every_character_a_reading_can_use_has_a_full_height_glyph()
    {
        foreach (char c in "0123456789H:")
        {
            var glyph = DotFont.Glyph(c);

            Assert.NotNull(glyph);
            Assert.Equal(DotFont.Rows, glyph.Length);
            Assert.All(glyph, row => Assert.Equal(glyph[0].Length, row.Length));
        }
    }

    [Fact]
    public void Text_width_counts_dots_and_the_gaps_between()
    {
        Assert.Equal(25, DotFont.Columns("12:34"));
        Assert.Equal(0, DotFont.Columns(""));
    }

    [Fact]
    public void Strips_flicker_one_by_one_then_together_then_go_dark()
    {
        var style = GlyphAlertStyle.Flash;

        Assert.Equal(1, GlyphPattern.Brightness(style, 0, 0.01));
        Assert.Equal(0, GlyphPattern.Brightness(style, 1, 0.01));
        Assert.Equal(1, GlyphPattern.Brightness(style, 1, 0.12));
        Assert.Equal(0, GlyphPattern.Brightness(style, 0, 0.12));
        Assert.Equal(1, GlyphPattern.Brightness(style, 2, 0.23));

        for (int strip = 0; strip < GlyphPattern.Strips; strip++)
        {
            Assert.Equal(1, GlyphPattern.Brightness(style, strip, 0.37));
            Assert.Equal(0, GlyphPattern.Brightness(style, strip, 1.2));
            Assert.Equal(1, GlyphPattern.Brightness(style, strip, GlyphPattern.LoopSeconds + 0.37));
        }
    }

    [Fact]
    public void Only_the_softer_styles_fade_after_a_flicker()
    {
        Assert.Equal(0, GlyphPattern.Brightness(GlyphAlertStyle.Flash, 0, 0.08));
        Assert.InRange(GlyphPattern.Brightness(GlyphAlertStyle.Fade, 0, 0.08), 0.01, 0.99);
        Assert.Equal(0, GlyphPattern.Brightness(GlyphAlertStyle.Bloom, 0, 0.2));
    }

    [Theory]
    [InlineData(236, 38)]
    [InlineData(112, 30)]
    [InlineData(30, 30)]
    [InlineData(400, 216)]
    public void Strips_cover_only_part_of_the_outline_and_never_touch(double width, double height)
    {
        double radius = GlyphPattern.IslandRadius(width, height);
        var spans = Enumerable.Range(0, GlyphPattern.Strips)
            .Select(strip => GlyphPattern.StripSpan(strip, width, height, radius))
            .OrderBy(span => span.Start)
            .ToList();

        Assert.True(spans.Sum(span => span.End - span.Start) < 0.4);

        for (int i = 0; i < spans.Count; i++)
        {
            Assert.True(spans[i].End > spans[i].Start);

            if (i > 0)
                Assert.True(spans[i].Start > spans[i - 1].End);
        }
    }

    [Fact]
    public void A_running_timer_carries_over_to_its_original_end_time()
    {
        var first = NewTimer();
        first.Start(TimeSpan.FromMinutes(10));
        Pass(60);
        var saved = first.Snapshot;

        // Closed for two minutes
        Pass(120);
        var second = NewTimer();
        second.Restore(saved);

        Assert.Equal(CountdownState.Running, second.State);
        Assert.Equal(TimeSpan.FromMinutes(7), second.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(10), second.Total);
    }

    [Fact]
    public void A_paused_timer_carries_over_still_paused_with_its_time_intact()
    {
        var first = NewTimer();
        first.Start(TimeSpan.FromMinutes(10));
        Pass(60);
        first.Pause();
        var saved = first.Snapshot;

        Pass(5000);
        var second = NewTimer();
        second.Restore(saved);

        Assert.Equal(CountdownState.Paused, second.State);
        Assert.Equal(TimeSpan.FromMinutes(9), second.Remaining);
    }

    [Fact]
    public void A_timer_that_ended_while_closed_rings_only_if_recent()
    {
        var running = NewTimer();
        running.Start(TimeSpan.FromMinutes(1));
        var saved = running.Snapshot;

        Pass(60 + 1800);
        var recent = NewTimer();
        recent.Restore(saved);
        Assert.Equal(CountdownState.Ringing, recent.State);

        // Gets its whole alert, however late it was noticed
        Pass(29);
        recent.Update();
        Assert.Equal(CountdownState.Ringing, recent.State);

        Pass(3600);
        var old = NewTimer();
        old.Restore(saved);
        Assert.Equal(CountdownState.Idle, old.State);
    }

    [Fact]
    public void Only_a_counting_timer_has_anything_to_save()
    {
        var timer = NewTimer();
        Assert.Null(timer.Snapshot);

        timer.Start(TimeSpan.FromSeconds(1));
        Assert.NotNull(timer.Snapshot);

        Pass(2);
        timer.Update();
        Assert.Null(timer.Snapshot);
    }

    [Fact]
    public void Nonsense_saved_timers_are_ignored()
    {
        var timer = NewTimer();

        timer.Restore(null);
        timer.Restore(new SavedCountdown(CountdownState.Running, now.AddMinutes(1), TimeSpan.Zero, TimeSpan.Zero));
        timer.Restore(new SavedCountdown(CountdownState.Running, now.AddDays(3), TimeSpan.Zero, TimeSpan.FromHours(1)));
        timer.Restore(new SavedCountdown(CountdownState.Paused, now, TimeSpan.Zero, TimeSpan.FromHours(1)));
        timer.Restore(new SavedCountdown(CountdownState.Ringing, now, TimeSpan.Zero, TimeSpan.FromHours(1)));

        Assert.Equal(CountdownState.Idle, timer.State);
    }

    [Fact]
    public void Store_round_trips_clears_and_survives_a_damaged_file()
    {
        string folder = Path.Combine(Path.GetTempPath(), "IsletTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "timer.json");

        try
        {
            Assert.Null(CountdownStore.Load(path));

            var saved = new SavedCountdown(CountdownState.Running, now, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
            CountdownStore.Save(path, saved);
            Assert.Equal(saved, CountdownStore.Load(path));

            CountdownStore.Save(path, null);
            Assert.False(File.Exists(path));

            Directory.CreateDirectory(folder);
            File.WriteAllText(path, "not json");
            Assert.Null(CountdownStore.Load(path));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Timer_sound_is_on_unless_turned_off()
    {
        Assert.True(new AppSettings().TimerSound);
    }

    [Fact]
    public void Sound_is_a_valid_wav_exactly_one_round_long()
    {
        var wav = GlyphSound.BuildWav();
        int samples = (int)Math.Round(GlyphPattern.LoopSeconds * GlyphSound.SampleRate);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44 + samples * 2, wav.Length);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
    }

    [Fact]
    public void Sound_blips_with_each_flicker_and_is_silent_between_rounds()
    {
        var wav = GlyphSound.BuildWav();

        double Peak(double from, double to)
        {
            int first = (int)(from * GlyphSound.SampleRate), last = (int)(to * GlyphSound.SampleRate);
            int peak = 0;

            for (int i = first; i < last; i++)
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, 44 + i * 2)));

            return peak / (double)short.MaxValue;
        }

        foreach (var pulse in GlyphPattern.Pulses)
            Assert.True(Peak(pulse.Start, pulse.Start + pulse.Length) > 0.1);

        Assert.Equal(0, Peak(0.8, GlyphPattern.LoopSeconds - 0.01));
        Assert.True(Peak(0, GlyphPattern.LoopSeconds) <= 1);
    }

    [Theory]
    [InlineData(236, 38)]
    [InlineData(30, 30)]
    [InlineData(112, 30)]
    [InlineData(400, 204)]
    public void Outline_points_stay_on_the_islands_real_edge(double width, double height)
    {
        double radius = GlyphPattern.IslandRadius(width, height);

        for (double f = 0; f < 1; f += 0.013)
        {
            var (x, y) = GlyphPattern.PointOnOutline(width, height, radius, f);

            // Distance to a rounded rectangle's edge is zero for any point on it
            double qx = Math.Abs(x - width / 2) - (width / 2 - radius);
            double qy = Math.Abs(y - height / 2) - (height / 2 - radius);
            double distance = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2))
                              + Math.Min(Math.Max(qx, qy), 0) - radius;

            Assert.InRange(distance, -0.001, 0.001);
        }
    }

    [Fact]
    public void Outline_walk_starts_at_top_center_going_clockwise()
    {
        var start = GlyphPattern.PointOnOutline(236, 38, 19, 0);
        var soon = GlyphPattern.PointOnOutline(236, 38, 19, 0.05);
        var half = GlyphPattern.PointOnOutline(236, 38, 19, 0.5);

        Assert.Equal((118.0, 0.0), start);
        Assert.True(soon.X > start.X);
        Assert.Equal(118, half.X, 3);
        Assert.Equal(38, half.Y, 3);
    }

    [Fact]
    public void Island_radius_is_a_capsule_when_small_and_a_card_when_open()
    {
        Assert.Equal(19, GlyphPattern.IslandRadius(236, 38));
        Assert.Equal(15, GlyphPattern.IslandRadius(30, 30));
        Assert.Equal(19 + 134 * 0.16, GlyphPattern.IslandRadius(400, 172), 6);
    }
}
