using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MusicIsland
{
    public partial class MainWindow
    {
        // Synced lyrics
        IReadOnlyList<LyricsService.Line>? lyrics;
        LyricsService.Line? currentLyric;
        readonly Dictionary<string, IReadOnlyList<LyricsService.Line>?> lyricsCache = new();

        // ================= Lyrics =================

        async Task LoadLyricsAsync(string key, TrackInfo info)
        {
            if (!Settings.ShowLyrics || Profile.Lyrics == LyricsStyle.None)
                return;

            if (lyricsCache.TryGetValue(key, out var cached))
            {
                lyrics = cached;
                ResetLyric();
                ApplyLyricsLayout();
                return;
            }

            // Browsers often report the duration a moment after the title
            if (info.Duration <= TimeSpan.Zero)
            {
                await Task.Delay(1500);

                if (key != trackKey || track == null)
                    return;

                info = track;
            }

            IReadOnlyList<LyricsService.Line>? result;

            try
            {
                result = await LyricsService.GetAsync(
                    info.Artist, info.Title, info.Album, info.Duration, info.Source == MediaSource.Browser);
            }
            catch
            {
                // Service unreachable: try again next time this song comes up
                return;
            }

            // A miss without a known length may just be too early; don't remember it
            if (result != null || info.Duration > TimeSpan.Zero)
            {
                if (lyricsCache.Count > 200)
                    lyricsCache.Clear();

                lyricsCache[key] = result;
            }

            if (key != trackKey)
                return;

            lyrics = result;
            ResetLyric();
            ApplyLyricsLayout();
        }

        void ApplyLyricsLayout()
        {
            var style = lyrics != null && Settings.ShowLyrics ? Profile.Lyrics : LyricsStyle.None;

            LyricRow.Visibility = style == LyricsStyle.Line ? Visibility.Visible : Visibility.Collapsed;
            LyricStage.Visibility = style == LyricsStyle.Stage ? Visibility.Visible : Visibility.Collapsed;
            ExpandedMusic.Height = (style switch
            {
                LyricsStyle.Line => 204,
                LyricsStyle.Stage => 266,
                _ => 172
            }) + (MusicTimerCard.Visibility == Visibility.Visible ? TimerCard.SectionHeight : 0);

            if (surface == Surface.ExpandedMusic)
            {
                height.Target = ExpandedMusic.Height;
                EnsureRendering();
            }
        }

        // Before a song's first line there is no line to replace the old text with
        void ResetLyric()
        {
            currentLyric = null;
            LyricText.Text = ((char)0x266A).ToString();
            ResetStage();
        }

        void UpdateLyric(TimeSpan position)
        {
            if (lyrics != null && LyricStage.Visibility == Visibility.Visible)
                UpdateStage(position);

            if (lyrics == null || LyricRow.Visibility != Visibility.Visible)
                return;

            // Show each line a touch early so it lands as it is sung
            var line = LyricsService.LineAt(lyrics, position + TimeSpan.FromMilliseconds(200));

            if (line == currentLyric)
                return;

            currentLyric = line;
            LyricText.Text = line == null || line.Text.Length == 0 ? ((char)0x266A).ToString() : line.Text;

            var duration = TimeSpan.FromMilliseconds(360);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            LyricText.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            LyricShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, duration) { EasingFunction = ease });
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
