namespace Islet
{
    /// <summary>Every shape the island can morph between.</summary>
    public enum Surface
    {
        Hidden,
        Notch,
        CompactMusic,
        SmallMusic,
        StatusSmall,
        StatusRing,
        MiniMusic,
        MiniIdle,
        CompactAmbient,
        Volume,
        Peek,
        Notify,
        ExpandedMusic,
        ExpandedAmbient,
        TimerPill,
        Shelf,
        Palette,
        Locks
    }

    /// <summary>Everything that decides which shape the island takes, at one moment.</summary>
    public sealed record IslandInputs
    {
        /// <summary>A full-screen app is in front and the island hides for it.</summary>
        public bool HiddenForApp { get; init; }

        public bool TimerActive { get; init; }
        public bool TimerRinging { get; init; }

        /// <summary>A file dragged over, or the shelf, "What's using it?" or colours panel opened.</summary>
        public bool ShelfWanted { get; init; }
        public bool LocksWanted { get; init; }
        public bool PaletteWanted { get; init; }

        public bool Hovering { get; init; }
        public bool HasTrack { get; init; }
        public bool Idle { get; init; }

        /// <summary>A volume, brightness or device change being shown.</summary>
        public bool StatusShowing { get; init; }
        public bool NotificationShowing { get; init; }

        /// <summary>The next song's title, briefly, as it starts.</summary>
        public bool PeekShowing { get; init; }

        public IdleMode IdleMode { get; init; } = IdleMode.Notch;
        public MusicLook MusicLook { get; init; } = MusicLook.Pill;
    }

    /// <summary>
    /// Decides the island's shape, kept apart from the window so every rule can be tested.
    /// The order is the priority: earlier rules win over later ones.
    /// </summary>
    public static class IslandState
    {
        public static Surface Choose(IslandInputs i)
        {
            // You set the timer yourself, so unlike a message its alert does show over a full-screen app
            if (i.HiddenForApp)
                return i.TimerRinging ? Surface.TimerPill : Surface.Hidden;

            // A file being dragged over, or a panel you opened, comes before everything else
            if (i.ShelfWanted)
                return Surface.Shelf;

            if (i.LocksWanted)
                return Surface.Locks;

            if (i.PaletteWanted)
                return Surface.Palette;

            if (i.Hovering)
                return i.HasTrack ? Surface.ExpandedMusic : Surface.ExpandedAmbient;

            if (i.StatusShowing)
            {
                return SystemStatus.LayoutFor(i.Idle, i.IdleMode, i.MusicLook) switch
                {
                    StatusLayout.Ring => Surface.StatusRing,
                    StatusLayout.Small => Surface.StatusSmall,
                    _ => Surface.Volume
                };
            }

            // Messages show up even when the idle island is hidden
            if (i.NotificationShowing)
                return Surface.Notify;

            if (i.Idle && i.IdleMode == IdleMode.Hide)
                return i.TimerActive ? Surface.TimerPill : Surface.Hidden;

            if (!i.Idle)
                return i.PeekShowing ? Surface.Peek
                    : i.MusicLook == MusicLook.Disc ? Surface.MiniMusic
                    : i.MusicLook == MusicLook.Small ? Surface.SmallMusic
                    : Surface.CompactMusic;

            return i.IdleMode switch
            {
                IdleMode.Notch => Surface.Notch,
                IdleMode.Circle => Surface.MiniIdle,
                _ => Surface.CompactAmbient
            };
        }

        /// <summary>
        /// Idle once nothing has played for a while: no song at all, or one paused for longer
        /// than the delay. A delay of 0 means only when the player closes.
        /// </summary>
        public static bool IsIdle(bool hasTrack, bool playing, DateTime? pausedSince, int idleDelaySeconds, DateTime now)
        {
            if (!hasTrack)
                return true;

            if (playing || pausedSince == null || idleDelaySeconds <= 0)
                return false;

            return (now - pausedSince.Value).TotalSeconds >= idleDelaySeconds;
        }
    }
}
