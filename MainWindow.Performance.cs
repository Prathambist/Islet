using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media;

namespace MusicIsland
{
    public partial class MainWindow
    {
        static PerformanceProfile Profile => PerformanceProfile.For(Settings.Performance);

        // Shown instead of moving bars in Low mode
        static readonly double[] StillBars = { 0.25, 0.55, 0.8, 0.45, 0.7, 0.35, 0.2 };

        readonly DispatcherTimer trimTimer = new() { Interval = TimeSpan.FromSeconds(20) };

        bool barsStill;

        void ApplyPerformance()
        {
            var profile = Profile;

            RenderOptions.ProcessRenderMode = profile.GraphicsCard ? RenderMode.Default : RenderMode.SoftwareOnly;
            hoverTimer.Interval = TimeSpan.FromMilliseconds(profile.HoverDelayMs);
            collapseTimer.Interval = TimeSpan.FromMilliseconds(profile.CollapseDelayMs);
            visualizer.MinInterval = TimeSpan.FromMilliseconds(profile.AudioIntervalMs);
            barsStill = false;

            if (!profile.Marquee)
                StopTitleMarquee();
            else if (surface == Surface.ExpandedMusic)
                StartTitleMarquee();

            if (lyrics == null && track != null && profile.Lyrics != LyricsStyle.None)
                _ = LoadLyricsAsync(trackKey, track);

            ResetLyric();
            ScheduleMemoryTrim();
            SyncAlert();
            RefreshTimerFaces();
        }

        // The analysis only runs while something on screen uses it
        void SyncVisualizer(Surface shown)
        {
            bool barsShown = shown is Surface.CompactMusic or Surface.SmallMusic or Surface.ExpandedMusic or Surface.Peek;
            visualizer.SetActive(Settings.RealAudioVisualizer && Profile.RealAudio && playing && barsShown);
        }

        bool ApplyStillBars()
        {
            if (barsStill)
                return false;

            barsStill = true;

            for (int i = 0; i < barLevels.Length; i++)
            {
                double level = StillBars[i % StillBars.Length];
                barLevels[i] = level;
                SetBarHeights(i, level);
            }

            return false;
        }

        void SetBarHeights(int i, double level)
        {
            compactBars[i].Height = 3 + level * 17;
            smallBars[i].Height = 2 + level * 13;
            expandedBars[i].Height = 3.5 + level * 22;
            peekBars[i].Height = 3 + level * 17;
        }

        // ================= Memory =================

        [DllImport("kernel32.dll")]
        static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

        void ScheduleMemoryTrim()
        {
            trimTimer.Stop();

            if (Profile.TrimMemory)
                trimTimer.Start();
        }

        // Hands back what opening the card or changing songs used; the next hover reloads it
        void TrimMemory()
        {
            trimTimer.Stop();

            if (!Profile.TrimMemory || surface is Surface.ExpandedMusic or Surface.ExpandedAmbient)
                return;

            // Never in the way of someone about to use the island
            if (hovering)
            {
                trimTimer.Start();
                return;
            }

            // A background collection, so animations keep running while it works
            Task.Run(() =>
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: false);
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            });
        }

        // ================= Song change flair =================

        void PlaySongFlair()
        {
            if (!Profile.SongFlair)
                return;

            artPop.Snap(0.84);
            artPop.Target = 1;
            peekLift.Snap(10);
            peekLift.Target = 0;
        }

        void ApplyFlair()
        {
            AlbumArtPop.ScaleX = AlbumArtPop.ScaleY = artPop.Value;
            PeekArtPop.ScaleX = PeekArtPop.ScaleY = artPop.Value;
            PeekTextShift.Y = peekLift.Value;
            PeekText.Opacity = Math.Clamp(1 - peekLift.Value / 10, 0, 1);

            double open = openRows.Value;
            HeaderShift.Y = 5 * open;
            LyricStageShift.Y = 10 * open;
            ProgressShift.Y = 14 * open;
            ControlsShift.Y = 18 * open;
        }

        // ================= Shuffle / repeat =================

        static readonly Brush ModeOn = Brushes.White;
        static readonly Brush ModeOff = FrozenBrush(0x80, 0x80, 0x80);

        void UpdatePlaybackModes(TrackInfo info)
        {
            ShuffleButton.IsEnabled = info.CanShuffle;
            ShuffleButton.Opacity = info.CanShuffle ? 1 : 0.3;
            ShuffleButton.Foreground = info.Shuffle == true ? ModeOn : ModeOff;
            ShuffleDot.Visibility = info.Shuffle == true ? Visibility.Visible : Visibility.Hidden;

            var repeat = info.Repeat ?? MediaPlaybackAutoRepeatMode.None;

            RepeatButton.IsEnabled = info.CanRepeat;
            RepeatButton.Opacity = info.CanRepeat ? 1 : 0.3;
            RepeatButton.Foreground = repeat == MediaPlaybackAutoRepeatMode.None ? ModeOff : ModeOn;
            RepeatButton.Content = repeat == MediaPlaybackAutoRepeatMode.Track ? "" : "";
            RepeatDot.Visibility = repeat == MediaPlaybackAutoRepeatMode.None ? Visibility.Hidden : Visibility.Visible;
        }

        async void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            if (track == null)
                return;

            await media.SetShuffleAsync(track.Shuffle != true);
            QueueTrackUpdate();
        }

        async void Repeat_Click(object sender, RoutedEventArgs e)
        {
            if (track == null)
                return;

            await media.SetRepeatAsync(MediaController.RepeatAfter(track.Repeat));
            QueueTrackUpdate();
        }
    }
}
