using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Islet
{
    /// <summary>What's playing, as the screensaver needs it.</summary>
    public sealed record NowPlaying(TrackInfo Track, BitmapSource? Art, IReadOnlyList<LyricsService.Line>? Lyrics);

    /// <summary>
    /// The island grown into the whole screen while you're away and music plays: the cover
    /// large over a blurred, darkened copy of it, the song, the lyric being sung and the next
    /// one, and how far along it is. Any key, mouse movement or click brings you back.
    ///
    /// It never takes the keyboard, so a key pressed to wake it doesn't also go somewhere
    /// unexpected; waking is noticed through Windows' own record of the last input.
    /// </summary>
    sealed class AlbumScreensaver : Window
    {
        readonly Func<NowPlaying?> now;
        readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly uint openedInput = LastInput();

        readonly Grid stage = new();
        readonly ScaleTransform grow = new(0.94, 0.94);
        readonly Image backdrop = new() { Stretch = Stretch.UniformToFill };
        readonly Image cover = new() { Stretch = Stretch.UniformToFill };
        readonly Grid coverFrame = new();
        readonly StackPanel words = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(56, 0, 0, 0) };
        readonly TextBlock title = Text(40, FontWeights.SemiBold, 1);
        readonly TextBlock artist = Text(22, FontWeights.Normal, 0.75);
        readonly TextBlock lyric = Text(30, FontWeights.SemiBold, 1);
        readonly TextBlock nextLyric = Text(22, FontWeights.Normal, 0.4);
        readonly Border progressTrack = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)) };
        readonly Border progressFill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock elapsed = Text(13, FontWeights.Normal, 0.6);
        readonly TextBlock remaining = Text(13, FontWeights.Normal, 0.6);
        readonly TextBlock clock = Text(22, FontWeights.Light, 0.85);

        string shownSong = "";
        string shownLyric = "";
        bool closing;

        public event Action? Dismissed;

        public AlbumScreensaver(Func<NowPlaying?> now)
        {
            this.now = now;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Title = "Islet screensaver";
            Cursor = Cursors.None;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            Build();
            Content = stage;

            // A click lands here rather than in whatever is underneath
            MouseDown += (s, e) => Dismiss();

            tick.Tick += (s, e) => Tick();
            Loaded += (s, e) =>
            {
                Refresh();
                Appear();
                tick.Start();
            };
        }

        void Build()
        {
            // Grows down from the top of the screen, where the island is
            stage.Opacity = 0;
            stage.RenderTransformOrigin = new Point(0.5, 0);
            stage.RenderTransform = grow;
            stage.Background = Brushes.Black;

            stage.Children.Add(backdrop);
            stage.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Color.FromArgb(150, 0, 0, 0), Color.FromArgb(215, 0, 0, 0), 90)
            });

            double side = Math.Min(Height * 0.46, 440);
            coverFrame.Width = coverFrame.Height = side;
            coverFrame.Clip = new RectangleGeometry(new Rect(0, 0, side, side), 22, 22);
            coverFrame.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });
            coverFrame.Children.Add(cover);
            RenderOptions.SetBitmapScalingMode(cover, BitmapScalingMode.HighQuality);

            var coverShadow = new Grid
            {
                Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 12, Opacity = 0.55, Color = Colors.Black }
            };
            coverShadow.Children.Add(coverFrame);

            // A fixed width, and room for two lines of each lyric whatever their length, so the
            // cover and the text stay put as lines come and go
            words.Width = Math.Min(Width * 0.42, 620);
            Reserve(lyric, 2);
            Reserve(nextLyric, 2);

            words.Children.Add(title);
            words.Children.Add(artist);
            artist.Margin = new Thickness(0, 6, 0, 0);
            words.Children.Add(lyric);
            lyric.Margin = new Thickness(0, 44, 0, 0);
            words.Children.Add(nextLyric);
            nextLyric.Margin = new Thickness(0, 10, 0, 0);

            var progress = new Grid { Margin = new Thickness(0, 44, 0, 0) };
            progress.Children.Add(progressTrack);
            progress.Children.Add(progressFill);
            words.Children.Add(progress);

            var times = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(remaining, Dock.Right);
            times.Children.Add(remaining);
            times.Children.Add(elapsed);
            words.Children.Add(times);

            var middle = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            middle.Children.Add(coverShadow);
            middle.Children.Add(words);
            stage.Children.Add(middle);

            clock.HorizontalAlignment = HorizontalAlignment.Right;
            clock.VerticalAlignment = VerticalAlignment.Top;
            clock.Margin = new Thickness(0, 36, 48, 0);
            stage.Children.Add(clock);
        }

        // Always as tall as this many lines, however many the text needs
        static void Reserve(TextBlock text, int lines)
        {
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            text.LineHeight = Math.Round(text.FontSize * 1.3);
            text.Height = text.MaxHeight = text.LineHeight * lines;
        }

        static TextBlock Text(double size, FontWeight weight, double opacity) => new()
        {
            FontSize = size,
            FontWeight = weight,
            Foreground = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = size * 2.7
        };

        void Appear()
        {
            stage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(700)));

            var spring = new DoubleAnimation(1, TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            grow.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
        }

        void Tick()
        {
            // Any key or mouse movement since it opened, anywhere
            if (LastInput() != openedInput)
            {
                Dismiss();
                return;
            }

            Refresh();
        }

        void Refresh()
        {
            var playing = now();

            if (playing == null)
            {
                Dismiss();
                return;
            }

            var track = playing.Track;
            string song = $"{track.Title}\n{track.Artist}";

            // A new song crossfades in
            if (song != shownSong)
            {
                bool first = shownSong.Length == 0;
                shownSong = song;

                Swap(first, () =>
                {
                    title.Text = track.Title;
                    artist.Text = track.Artist;
                    cover.Source = playing.Art;
                    backdrop.Source = playing.Art != null ? MainWindow.BlurredCover(playing.Art) : null;
                }, coverFrame, title, artist);
            }

            clock.Text = DateTime.Now.ToString(AppSettings.Current.Use24HourClock ? "HH:mm" : "h:mm tt");

            var position = track.PositionAt(DateTimeOffset.Now);
            var duration = track.Duration;

            if (duration > TimeSpan.Zero)
            {
                double fraction = Math.Clamp(position / duration, 0, 1);
                progressFill.Width = Math.Max(0, words.ActualWidth) * fraction;
                elapsed.Text = Format(position);
                remaining.Text = "-" + Format(duration - position);
            }

            // The line being sung, and the next one fainter beneath it
            string current = "", next = "";

            if (!track.Playing)
            {
                current = "Paused";
            }
            else if (playing.Lyrics is { Count: > 0 } lines)
            {
                int index = LyricsService.IndexAt(lines, position + TimeSpan.FromMilliseconds(200));
                current = index >= 0 ? lines[index].Text : "";
                next = index + 1 < lines.Count ? lines[index + 1].Text : "";
            }

            if (current + "\n" + next != shownLyric)
            {
                bool first = shownLyric.Length == 0;
                shownLyric = current + "\n" + next;

                Swap(first, () =>
                {
                    lyric.Text = current;
                    nextLyric.Text = next;
                }, lyric, nextLyric);
            }
        }

        static string Format(TimeSpan time) =>
            time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

        // Fades what's changing out, swaps it, and fades it back in
        static void Swap(bool immediately, Action change, params UIElement[] parts)
        {
            if (immediately)
            {
                change();
                return;
            }

            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fadeOut.Completed += (s, e) =>
            {
                change();

                foreach (var part in parts)
                    part.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(380)));
            };

            for (int i = 0; i < parts.Length; i++)
                parts[i].BeginAnimation(OpacityProperty, i == 0 ? fadeOut : new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        }

        public void Dismiss()
        {
            if (closing)
                return;

            closing = true;
            tick.Stop();

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (s, e) => Close();
            stage.BeginAnimation(OpacityProperty, fade);

            Dismissed?.Invoke();
        }

        /// <summary>When the last key or mouse input happened, in Windows' milliseconds since startup.</summary>
        public static uint LastInput()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            return GetLastInputInfo(ref info) ? info.dwTime : 0;
        }

        /// <summary>How long since you last touched the keyboard or mouse.</summary>
        public static TimeSpan IdleFor() =>
            TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - LastInput()));

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    }
}
