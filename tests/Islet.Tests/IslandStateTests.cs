namespace Islet.Tests
{
    public class IslandStateTests
    {
        // Music playing in the default look, nothing else going on
        static readonly IslandInputs Playing = new() { HasTrack = true };

        static readonly IslandInputs IdleNotch = new() { Idle = true, IdleMode = IdleMode.Notch };

        [Fact]
        public void PlayingShowsTheChosenMusicLook()
        {
            Assert.Equal(Surface.CompactMusic, IslandState.Choose(Playing));
            Assert.Equal(Surface.MiniMusic, IslandState.Choose(Playing with { MusicLook = MusicLook.Disc }));
            Assert.Equal(Surface.SmallMusic, IslandState.Choose(Playing with { MusicLook = MusicLook.Small }));
            Assert.Equal(Surface.Peek, IslandState.Choose(Playing with { PeekShowing = true }));
        }

        [Theory]
        [InlineData(IdleMode.Notch, Surface.Notch)]
        [InlineData(IdleMode.Circle, Surface.MiniIdle)]
        [InlineData(IdleMode.Clock, Surface.CompactAmbient)]
        [InlineData(IdleMode.ClockWeather, Surface.CompactAmbient)]
        [InlineData(IdleMode.Hide, Surface.Hidden)]
        public void IdleShowsTheChosenIdleLook(IdleMode mode, Surface expected) =>
            Assert.Equal(expected, IslandState.Choose(new IslandInputs { Idle = true, IdleMode = mode }));

        [Fact]
        public void AHiddenIdleIslandStillShowsARunningTimer()
        {
            var hidden = new IslandInputs { Idle = true, IdleMode = IdleMode.Hide, TimerActive = true };

            Assert.Equal(Surface.TimerPill, IslandState.Choose(hidden));
        }

        [Fact]
        public void HoveringOpensTheRightCard()
        {
            Assert.Equal(Surface.ExpandedMusic, IslandState.Choose(Playing with { Hovering = true }));
            Assert.Equal(Surface.ExpandedAmbient, IslandState.Choose(IdleNotch with { Hovering = true }));
        }

        [Fact]
        public void MessagesShowEvenWhenTheIdleIslandIsHidden()
        {
            var hidden = new IslandInputs { Idle = true, IdleMode = IdleMode.Hide, NotificationShowing = true };

            Assert.Equal(Surface.Notify, IslandState.Choose(hidden));
        }

        [Theory]
        [InlineData(false, IdleMode.Notch, MusicLook.Pill, Surface.Volume)]
        [InlineData(false, IdleMode.Notch, MusicLook.Disc, Surface.StatusRing)]
        [InlineData(true, IdleMode.Notch, MusicLook.Pill, Surface.StatusSmall)]
        [InlineData(true, IdleMode.Circle, MusicLook.Pill, Surface.StatusRing)]
        public void VolumeTakesAFormMatchingTheIsland(bool idle, IdleMode mode, MusicLook look, Surface expected) =>
            Assert.Equal(expected, IslandState.Choose(new IslandInputs
            {
                HasTrack = !idle,
                Idle = idle,
                IdleMode = mode,
                MusicLook = look,
                StatusShowing = true
            }));

        [Fact]
        public void FullScreenAppsHideEverythingButARingingTimer()
        {
            var busy = Playing with { HiddenForApp = true, Hovering = true, NotificationShowing = true, ShelfWanted = true };

            Assert.Equal(Surface.Hidden, IslandState.Choose(busy));
            Assert.Equal(Surface.TimerPill, IslandState.Choose(busy with { TimerRinging = true }));
        }

        [Fact]
        public void PanelsComeFirstInTheirOwnOrder()
        {
            var everything = Playing with
            {
                Hovering = true,
                NotificationShowing = true,
                StatusShowing = true,
                ShelfWanted = true,
                LocksWanted = true,
                PaletteWanted = true
            };

            Assert.Equal(Surface.Shelf, IslandState.Choose(everything));
            Assert.Equal(Surface.Locks, IslandState.Choose(everything with { ShelfWanted = false }));
            Assert.Equal(Surface.Palette, IslandState.Choose(everything with { ShelfWanted = false, LocksWanted = false }));
            Assert.Equal(Surface.ExpandedMusic, IslandState.Choose(everything with { ShelfWanted = false, LocksWanted = false, PaletteWanted = false }));
        }

        [Fact]
        public void IdleAfterBeingPausedForTheDelay()
        {
            var now = new DateTime(2026, 11, 9, 12, 0, 0);

            Assert.True(IslandState.IsIdle(false, false, null, 15, now));
            Assert.False(IslandState.IsIdle(true, true, null, 15, now));
            Assert.False(IslandState.IsIdle(true, false, now.AddSeconds(-10), 15, now));
            Assert.True(IslandState.IsIdle(true, false, now.AddSeconds(-20), 15, now));

            // "Only when closed": never idle while a song is there
            Assert.False(IslandState.IsIdle(true, false, now.AddHours(-3), 0, now));
        }
    }
}
