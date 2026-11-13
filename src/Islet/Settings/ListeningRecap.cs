using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Islet
{
    /// <summary>
    /// A picture of a day's, week's or month's listening to share: the top song large, the time
    /// listened, plays and peak hour, and the top three artists, over a wash of the top cover's
    /// colour. Drawn off screen at 1080 × 1350, the shape social apps like.
    /// </summary>
    public static class ListeningRecap
    {
        const double Width = 540, Height = 675, Scale = 2;

        static readonly Brush Text = Frozen(Colors.White);
        static readonly Brush Soft = Frozen(Color.FromArgb(185, 255, 255, 255));
        static readonly Brush Faint = Frozen(Color.FromArgb(120, 255, 255, 255));

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static BitmapSource Render(ListeningPeriod period, string title, string when, Func<string?, BitmapSource?> cover)
        {
            var song = period.Songs.FirstOrDefault();
            var songCover = cover(song?.Cover);
            var colour = songCover != null || song == null ? AlbumPalette.From(songCover).Primary : GeneratedCover.Colour(song.Name, song.Artist);

            var root = new Border
            {
                Width = Width,
                Height = Height,
                Padding = new Thickness(38, 36, 38, 30),
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(AlbumPalette.Tone(colour, 0.34, 0.35, 0.8), 0),
                        new(AlbumPalette.Tone(colour, 0.13, 0.3, 0.6), 0.55),
                        new(Color.FromRgb(10, 10, 12), 1)
                    },
                    new Point(0, 0), new Point(0.6, 1))
            };
            TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
            TextElement(root);

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Child = layout;

            // Heading
            var heading = new StackPanel();
            heading.Children.Add(Words("ISLET", 11.5, FontWeights.SemiBold, Faint));
            heading.Children.Add(Words(title, 30, FontWeights.Bold, Text, top: 6));
            heading.Children.Add(Words(when, 14, FontWeights.Normal, Soft, top: 2));
            layout.Children.Add(heading);

            // The top song
            if (song != null)
            {
                var top = new Grid { Margin = new Thickness(0, 30, 0, 0) };
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.ColumnDefinitions.Add(new ColumnDefinition());

                top.Children.Add(Picture(songCover, 150, 14, song.Name, song.Artist));

                var about = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
                about.Children.Add(Words("TOP SONG", 11, FontWeights.SemiBold, Faint));
                about.Children.Add(Words(song.Name, 23, FontWeights.SemiBold, Text, top: 4, lines: 2));

                if (song.Artist != null)
                    about.Children.Add(Words(song.Artist, 15, FontWeights.Normal, Soft, top: 2));

                about.Children.Add(Words(song.Plays > 0 ? $"{song.Plays} plays · {SettingsWindow.Tight(song.Time)}" : SettingsWindow.Tight(song.Time),
                    13, FontWeights.Normal, Faint, top: 8));
                Grid.SetColumn(about, 1);
                top.Children.Add(about);

                Grid.SetRow(top, 1);
                layout.Children.Add(top);
            }

            // The numbers
            var numbers = new Grid { Margin = new Thickness(0, 30, 0, 0) };
            numbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            numbers.ColumnDefinitions.Add(new ColumnDefinition());
            numbers.ColumnDefinitions.Add(new ColumnDefinition());
            numbers.Children.Add(Number(SettingsWindow.Tight(period.Total), "of music", 40));

            var plays = Number(period.Plays.ToString(), period.Plays == 1 ? "play" : "plays", 26);
            Grid.SetColumn(plays, 1);
            numbers.Children.Add(plays);

            if (period.PeakHour is int hour)
            {
                var peak = Number(AppSettings.Current.Use24HourClock ? $"{hour:00}:00" : hour == 0 ? "12 AM" : hour < 12 ? $"{hour} AM" : hour == 12 ? "12 PM" : $"{hour - 12} PM",
                    "peak hour", 26);
                Grid.SetColumn(peak, 2);
                numbers.Children.Add(peak);
            }

            Grid.SetRow(numbers, 2);
            layout.Children.Add(numbers);

            // The top artists
            var artists = period.Artists.Take(3).ToList();

            if (artists.Count > 0)
            {
                var block = new StackPanel { Margin = new Thickness(0, 30, 0, 0) };
                block.Children.Add(Words("TOP ARTISTS", 11, FontWeights.SemiBold, Faint));

                var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };

                for (int i = 0; i < 3; i++)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition());

                    if (i >= artists.Count)
                        continue;

                    var spot = new StackPanel();
                    var face = Picture(cover(artists[i].Cover), 92, 46, artists[i].Name, null);
                    face.HorizontalAlignment = HorizontalAlignment.Center;
                    spot.Children.Add(face);

                    var name = Words($"{i + 1}. {artists[i].Name}", 14, FontWeights.SemiBold, Text, top: 10);
                    name.TextAlignment = TextAlignment.Center;
                    spot.Children.Add(name);

                    var time = Words(SettingsWindow.Tight(artists[i].Time), 12, FontWeights.Normal, Faint, top: 1);
                    time.TextAlignment = TextAlignment.Center;
                    spot.Children.Add(time);

                    Grid.SetColumn(spot, i);
                    row.Children.Add(spot);
                }

                block.Children.Add(row);
                Grid.SetRow(block, 3);
                layout.Children.Add(block);
            }

            var footer = Words("Made with Islet", 11, FontWeights.Normal, Faint);
            footer.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(footer, 4);
            layout.Children.Add(footer);

            var size = new Size(Width, Height);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)(Width * Scale), (int)(Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            bitmap.Freeze();
            return bitmap;
        }

        static void TextElement(FrameworkElement root)
        {
            System.Windows.Documents.TextElement.SetFontFamily(root, new FontFamily("Segoe UI Variable Display, Segoe UI"));
            System.Windows.Documents.TextElement.SetForeground(root, Text);
        }

        static TextBlock Words(string text, double size, FontWeight weight, Brush colour, double top = 0, int lines = 1) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = colour,
            Margin = new Thickness(0, top, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxHeight = lines > 1 ? size * 1.35 * lines : double.PositiveInfinity
        };

        static StackPanel Number(string value, string label, double size)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            panel.Children.Add(Words(value, size, FontWeights.Bold, Text));
            panel.Children.Add(Words(label, 13, FontWeights.Normal, Soft));
            return panel;
        }

        // A cover, or one made from the name in its place
        static Border Picture(BitmapSource? image, double size, double radius, string name, string? by)
        {
            var picture = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(radius),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.45, Color = Colors.Black }
            };

            if (image != null)
            {
                picture.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                return picture;
            }

            var (background, initials) = GeneratedCover.For(name, by);
            picture.Background = background;
            picture.Child = Words(initials, size * 0.32, FontWeights.SemiBold, Text);
            ((TextBlock)picture.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)picture.Child).VerticalAlignment = VerticalAlignment.Center;
            return picture;
        }
    }
}
