using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Islet
{
    public partial class SettingsWindow
    {
        // The window's colours live in these brushes; everything refers to them by name,
        // so changing palette is a matter of fading each brush to its new colour
        static readonly string[] BrushNames =
        {
            "WindowBg", "SidebarBg", "CardBg", "Accent", "SwitchOn",
            "Text", "TextSecondary", "TextFaint", "Divider", "Hover",
            "Control", "ControlSelected", "NavSelected", "SwitchOff", "PillBg", "Thumb", "ThumbHover",
            "AccentSoft", "AccentText"
        };

        SettingsPalette palette = SettingsPalette.Graphite;

        static Color[] ColorsOf(SettingsPalette p)
        {
            var ink = p.Ink;

            return new[]
            {
                p.Background, p.Sidebar, p.Card, p.Accent, p.SwitchOn,
                ink.Text, ink.TextSecondary, ink.TextFaint, ink.Divider, ink.Hover,
                ink.Control, ink.ControlSelected, ink.NavSelected, ink.SwitchOff, ink.Pill, ink.Thumb, ink.ThumbHover,
                p.AccentSoft, p.AccentText
            };
        }

        void SetUpPalette()
        {
            foreach (var name in BrushNames)
                Resources[name] = new SolidColorBrush(Colors.Black);

            palette = SettingsPalette.Find(Settings.SettingsPalette, island.AlbumAccent);
            ApplyPalette(palette, animate: false);
            BuildPaletteDots();

            // The Album palette follows the song as it changes, not only the one playing when Settings opened
            island.AlbumAccentChanged += FollowSong;
            Closed += (s, e) => island.AlbumAccentChanged -= FollowSong;
        }

        void FollowSong()
        {
            // The Album dot shows the song's colour whichever palette is in use
            BuildPaletteDots();

            if (palette.Name != SettingsPalette.AlbumName)
                return;

            palette = AlbumChoice;
            ApplyPalette(palette, animate: true);

            // The top song's wash on the Listening page is mixed with the palette
            if (statsBody != null && shown == listeningPage)
                DrawStats(force: true);
        }

        SettingsPalette AlbumChoice => SettingsPalette.Album(island.AlbumAccent);

        IEnumerable<SettingsPalette> Choices =>
            SettingsPalette.Dark.Append(AlbumChoice).Concat(SettingsPalette.LightOnes);

        void ChoosePalette(SettingsPalette next)
        {
            palette = next;
            Settings.SettingsPalette = next.Name;
            Settings.Save();

            ApplyPalette(next, animate: true);
            BuildPaletteDots();

            // The top song's wash is mixed with the palette's colours, so it's drawn again
            if (statsBody != null && shown == listeningPage)
                DrawStats(force: true);
        }

        void ApplyPalette(SettingsPalette next, bool animate)
        {
            var colors = ColorsOf(next);

            for (int i = 0; i < BrushNames.Length; i++)
            {
                // A fresh brush each time, with its fade already running before it's handed to the
                // window: the window freezes a still brush the moment it gets it, and a frozen
                // brush can't fade. Everything refers to them by name, so all follow along.
                var from = (Resources[BrushNames[i]] as SolidColorBrush)?.Color ?? colors[i];
                var brush = new SolidColorBrush(animate ? from : colors[i]);

                if (animate)
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(colors[i], TimeSpan.FromMilliseconds(380))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    });

                Resources[BrushNames[i]] = brush;
            }

            PaletteName.Text = next.Name == SettingsPalette.AlbumName ? "Album (follows the song)" : next.Name;
            MatchTitleBar();
        }

        // Windows draws the title bar; this asks for a dark or light one to match
        void MatchTitleBar()
        {
            var handle = new WindowInteropHelper(this).Handle;

            if (handle == IntPtr.Zero)
                return;

            int dark = palette.Light ? 0 : 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }

        // Dark palettes on one row and light ones on another, each dot in its accent colour,
        // the chosen one ringed
        void BuildPaletteDots()
        {
            PaletteDots.Children.Clear();
            PaletteDots.Orientation = Orientation.Vertical;

            foreach (var group in new[] { SettingsPalette.Dark.Append(AlbumChoice), SettingsPalette.LightOnes })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

                foreach (var choice in group)
                    row.Children.Add(Dot(choice));

                PaletteDots.Children.Add(row);
            }
        }

        Grid Dot(SettingsPalette choice)
        {
            bool chosen = choice.Name == palette.Name;
            bool album = choice.Name == SettingsPalette.AlbumName;

            var dot = new Grid
            {
                Width = 26,
                Height = 26,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                ToolTip = album ? "Album: follows the song that's playing" : choice.Name,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };

            var ring = new Ellipse { Width = 24, Height = 24, StrokeThickness = 1.5, Opacity = chosen ? 0.9 : 0 };
            ring.SetResourceReference(Shape.StrokeProperty, "Text");
            dot.Children.Add(ring);

            // A light palette's dot shows its pale background with the accent inside it
            if (choice.Light)
            {
                dot.Children.Add(new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new SolidColorBrush(choice.Background),
                    Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                    StrokeThickness = 1
                });
                dot.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(choice.Accent) });
            }
            else
            {
                dot.Children.Add(new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = album ? new LinearGradientBrush(choice.Accent, choice.Card, 45) : new SolidColorBrush(choice.Accent)
                });
            }

            if (album)
            {
                dot.Children.Add(new TextBlock
                {
                    Text = "",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 8,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var scale = (ScaleTransform)dot.RenderTransform;
            dot.MouseEnter += (s, e) => Grow(scale, 1.15);
            dot.MouseLeave += (s, e) => Grow(scale, 1);
            dot.MouseLeftButtonUp += (s, e) => ChoosePalette(choice);

            return dot;
        }

        static void Grow(ScaleTransform scale, double to)
        {
            var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }
}
