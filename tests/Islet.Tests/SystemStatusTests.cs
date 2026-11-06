using Islet;

namespace Islet.Tests;

public class SystemStatusTests
{
    [Theory]
    [InlineData(MusicLook.Pill, StatusLayout.Pill)]
    [InlineData(MusicLook.Small, StatusLayout.Small)]
    [InlineData(MusicLook.Disc, StatusLayout.Ring)]
    public void Playing_follows_the_music_look(MusicLook look, StatusLayout expected) =>
        Assert.Equal(expected, SystemStatus.LayoutFor(false, IdleMode.Circle, look));

    [Theory]
    [InlineData(IdleMode.Circle, StatusLayout.Ring)]
    [InlineData(IdleMode.Notch, StatusLayout.Small)]
    [InlineData(IdleMode.Clock, StatusLayout.Pill)]
    [InlineData(IdleMode.ClockWeather, StatusLayout.Pill)]
    [InlineData(IdleMode.Hide, StatusLayout.Pill)]
    public void Idle_follows_the_idle_look(IdleMode mode, StatusLayout expected) =>
        Assert.Equal(expected, SystemStatus.LayoutFor(true, mode, MusicLook.Disc));

    [Theory]
    [InlineData(0.5f, true, "")]
    [InlineData(0f, false, "")]
    [InlineData(0.2f, false, "")]
    [InlineData(0.5f, false, "")]
    [InlineData(0.9f, false, "")]
    public void Volume_glyph_matches_the_level(float level, bool muted, string glyph) =>
        Assert.Equal(glyph, SystemStatus.VolumeGlyph(level, muted));

    static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0);

    [Fact]
    public void Same_device_reporting_twice_shows_once()
    {
        var filter = new DeviceNoticeFilter();

        Assert.True(filter.ShouldShow("Buds", DeviceChange.Connected, Start));
        Assert.False(filter.ShouldShow("Buds", DeviceChange.Connected, Start.AddSeconds(1)));
    }

    [Fact]
    public void Audio_output_switch_right_after_a_connection_is_ignored()
    {
        var filter = new DeviceNoticeFilter();

        Assert.True(filter.ShouldShow("Buds", DeviceChange.Disconnected, Start));
        Assert.False(filter.ShouldShow("Speakers (Realtek)", DeviceChange.AudioOutput, Start.AddSeconds(1)));
    }

    [Fact]
    public void Quick_reconnect_and_later_repeat_still_show()
    {
        var filter = new DeviceNoticeFilter();

        Assert.True(filter.ShouldShow("Buds", DeviceChange.Connected, Start));
        Assert.True(filter.ShouldShow("Buds", DeviceChange.Disconnected, Start.AddSeconds(1)));
        Assert.True(filter.ShouldShow("Buds", DeviceChange.Disconnected, Start.AddSeconds(5)));
    }

    [Fact]
    public void Nameless_devices_are_skipped() =>
        Assert.False(new DeviceNoticeFilter().ShouldShow(" ", DeviceChange.Connected, Start));
}
