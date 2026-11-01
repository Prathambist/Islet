using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // ================= Lyric stage (High) =================
        //
        // A few reused lines. Each has its own spring and starts a moment after the one
        // before it, so the lines ripple into place instead of moving as a block.

        const int StageLines = 6;
        const double StageAnchor = 22;
        const double StageGap = 7;
        const double StageRipple = 0.035;

        sealed class StageLine
        {
            public required TextBlock Text;
            public required TranslateTransform Shift;
            public required ScaleTransform Scale;
            public readonly Spring Y = new(0, 0.62, 0.84);
            public readonly Spring Focus = new(0, 0.42, 1);
            public int LineIndex = -1;
            public double TextHeight;
            public double Delay;
            public double PendingY;
        }

        readonly StageLine[] stage = new StageLine[StageLines];
        readonly GradientStop sweepSung = new(Colors.White, 0);
        readonly GradientStop sweepAhead = new(Color.FromArgb(150, 255, 255, 255), 0.12);
        LinearGradientBrush sweepBrush = null!;
        int stageCurrent = int.MinValue;

        void BuildLyricStage()
        {
            sweepBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = { sweepSung, sweepAhead }
            };

            for (int i = 0; i < StageLines; i++)
            {
                var shift = new TranslateTransform();
                var scale = new ScaleTransform();
                var text = new TextBlock
                {
                    Width = 356,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Cursor = Cursors.Hand,
                    Opacity = 0,
                    RenderTransform = new TransformGroup { Children = { scale, shift } },
                    Tag = i
                };

                TextOptions.SetTextHintingMode(text, TextHintingMode.Animated);
                text.MouseLeftButtonUp += StageLine_Click;
                LyricStage.Children.Add(text);
                stage[i] = new StageLine { Text = text, Shift = shift, Scale = scale };
            }
        }

        void ResetStage()
        {
            stageCurrent = int.MinValue;

            foreach (var line in stage)
            {
                line.LineIndex = -1;
                line.Text.Opacity = 0;
            }
        }

        void UpdateStage(TimeSpan position)
        {
            var lines = lyrics!;
            var shown = position + TimeSpan.FromMilliseconds(200);
            int current = LyricsService.IndexAt(lines, shown);

            if (current != stageCurrent)
                MoveStage(lines, current);

            if (current < 0)
                return;

            // Lines have no word timing, so the highlight crosses them over the line's length
            var length = current + 1 < lines.Count
                ? lines[current + 1].Time - lines[current].Time
                : TimeSpan.FromSeconds(4);

            double seconds = Math.Clamp(length.TotalSeconds, 0.5, 8);
            double sung = Math.Clamp((shown - lines[current].Time).TotalSeconds / seconds, 0, 1);

            sweepSung.Offset = sung * 1.12 - 0.12;
            sweepAhead.Offset = sung * 1.12;
        }

        void MoveStage(IReadOnlyList<LyricsService.Line> lines, int current)
        {
            bool first = stageCurrent == int.MinValue;
            bool forward = current > stageCurrent;
            int start = Math.Max(0, current - 1);
            int count = Math.Min(StageLines, lines.Count - start);
            var heights = new double[count];

            foreach (var line in stage)
            {
                if (line.LineIndex < start || line.LineIndex >= start + count)
                    line.LineIndex = -1;
            }

            var added = new bool[count];

            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                var line = stage[index % StageLines];

                if (line.LineIndex != index)
                {
                    string text = lines[index].Text;
                    line.Text.Text = text.Length == 0 ? "♪" : text;
                    line.Text.Measure(new Size(line.Text.Width, double.PositiveInfinity));
                    line.TextHeight = line.Text.DesiredSize.Height;
                    line.LineIndex = index;
                    added[i] = true;
                }

                heights[i] = line.TextHeight;
            }

            var tops = LyricStageLayout.Tops(heights, current - start, StageAnchor, StageGap);

            foreach (var line in stage)
            {
                if (line.LineIndex < 0)
                    line.Text.Opacity = 0;
            }

            for (int i = 0; i < count; i++)
            {
                int index = start + i;
                var line = stage[index % StageLines];

                if (first)
                    line.Y.Snap(tops[i]);
                else if (added[i])
                    line.Y.Snap(tops[i] + (forward ? 26 : -26));

                line.PendingY = tops[i];
                line.Delay = first ? 0 : Math.Abs(index - current) * StageRipple;
                line.Focus.Target = index == current ? 1 : 0;
                line.Text.Foreground = index == current ? sweepBrush : Brushes.White;

                if (added[i])
                    line.Focus.Snap(index == current ? 1 : 0);
            }

            stageCurrent = current;
            EnsureRendering();
        }

        bool StepStage(double dt)
        {
            if (LyricStage.Visibility != Visibility.Visible || stageCurrent == int.MinValue)
                return false;

            bool moving = false;

            foreach (var line in stage)
            {
                if (line.LineIndex < 0)
                    continue;

                if (line.Delay > 0)
                {
                    line.Delay -= dt;
                    moving = true;
                }
                else
                {
                    line.Y.Target = line.PendingY;
                }

                line.Y.Step(dt);
                line.Focus.Step(dt);
                moving |= !line.Y.IsSettled || !line.Focus.IsSettled;

                double focus = line.Focus.Value;
                line.Shift.Y = line.Y.Value;
                line.Scale.ScaleX = line.Scale.ScaleY = 0.94 + 0.06 * focus;
                line.Text.Opacity = 0.3 + 0.7 * focus;
            }

            return moving;
        }

        async void StageLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (lyrics == null || track == null || sender is not TextBlock { Tag: int slot })
                return;

            int index = stage[slot].LineIndex;

            if (index < 0 || index >= lyrics.Count)
                return;

            e.Handled = true;
            await SeekToAsync(lyrics[index].Time);
        }
    }
}
