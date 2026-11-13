using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Islet
{
    public partial class SettingsWindow
    {
        const int SongsShown = 5;
        const int ArtistsShown = 3;
        const int AlbumsShown = 3;
        const int WeekDays = ListeningStats.DaysKept;

        PageInfo listeningPage = null!;
        StackPanel statsBody = null!;

        // A single day, picked from the week's chart, or the whole week
        bool weekView;
        DateTime selectedDay = DateTime.Today;

        // What was last drawn, so the page is only redrawn when the numbers have moved on
        string? drawnStats;

        // The bars rise when the page opens, not on every refresh
        bool growBars;

        // Covers already read from disk, by file
        readonly Dictionary<string, BitmapImage?> covers = new(StringComparer.OrdinalIgnoreCase);

        // ================= Listening =================

        void BuildListeningPage()
        {
            Page("Listening", "", "Your last seven days of songs: what you played, who you played, and when. Videos don't count, and none of it leaves this PC.");
            listeningPage = page;

            statsBody = new StackPanel();
            page.Content.Children.Add(statsBody);

            var summary = Group("Summary");
            SwitchRow(summary, "Daily music summary",
                "How long you listened today, and to whom, on the idle island, with a short recap at 9 PM.",
                Settings.DailyMusicSummary, v => { Settings.DailyMusicSummary = v; Settings.Save(); });

            // Kept current while it's open: songs keep counting as they play
            var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            refresh.Tick += (s, e) =>
            {
                if (shown == listeningPage)
                    DrawStats();
            };
            refresh.Start();
            Closed += (s, e) => refresh.Stop();

            listeningPage.Nav.Checked += (s, e) =>
            {
                selectedDay = DateTime.Today;
                growBars = true;
                DrawStats(force: true);
            };

            DrawStats(force: true);
        }

        void Pick(bool week, DateTime day)
        {
            weekView = week;
            selectedDay = day.Date;
            DrawStats(force: true);
        }

        void DrawStats(bool force = false)
        {
            var stats = island.Listening;
            var today = DateTime.Today;
            var daily = stats.Daily(today, WeekDays);
            var period = weekView ? stats.Period(today.AddDays(1 - WeekDays), today) : stats.Period(selectedDay, selectedDay);
            var songs = period.Songs.Take(SongsShown).ToList();
            var artists = period.Artists.Take(ArtistsShown).ToList();
            var albums = period.Albums.Take(AlbumsShown).ToList();

            // Minutes are what's on screen, so that's what decides whether to redraw
            string drawn = $"{weekView}|{selectedDay:yyyyMMdd}|{today:yyyyMMdd}|{palette.Name}|" +
                string.Join(",", daily.Select(d => (int)d.Total.TotalMinutes)) + "|" +
                string.Join("|", songs.Concat(artists).Concat(albums).Select(r => $"{r.Name}/{r.Artist}/{(int)r.Time.TotalMinutes}/{r.Plays}/{r.Cover}"));

            if (!force && drawn == drawnStats)
                return;

            drawnStats = drawn;
            statsBody.Children.Clear();
            statsBody.Children.Add(StatsHeader(today));

            if (songs.Count > 0)
                statsBody.Children.Add(Hero(songs[0]));

            var overview = Group(null, statsBody);
            ((FrameworkElement)statsBody.Children[^1]).Margin = new Thickness(0, songs.Count > 0 ? 12 : 0, 0, 0);
            AddToCard(overview, Padded(Tiles(period, weekView)));
            AddToCard(overview, Padded(WeekChart(daily, today)));
            AddToCard(overview, Padded(HourChart(period)));
            growBars = false;

            if (songs.Count == 0)
            {
                var empty = Group(null, statsBody);
                empty.Children.Add(Padded(Note(weekView ? "No songs played this week yet."
                    : selectedDay == today ? "No songs yet today. They show up here once they've played for a minute."
                    : "No songs played on this day.")));
                ((FrameworkElement)statsBody.Children[^1]).Margin = new Thickness(0, 12, 0, 0);
                return;
            }

            var songCard = Group("Top songs", statsBody);

            for (int i = 0; i < songs.Count; i++)
                AddToCard(songCard, Compact(SongRow(i + 1, songs[i], songs[0].Time)));

            // Artists and albums side by side, as faces and covers
            var shelves = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            shelves.ColumnDefinitions.Add(new ColumnDefinition());
            shelves.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            shelves.ColumnDefinitions.Add(new ColumnDefinition());

            var artistColumn = new StackPanel();
            Shelf(Group("Top artists", artistColumn), artists, round: true, "No artist names on what played.");
            shelves.Children.Add(artistColumn);

            var albumColumn = new StackPanel();
            Shelf(Group("Top albums", albumColumn), albums, round: false, "No album names on what played.");
            Grid.SetColumn(albumColumn, 2);
            shelves.Children.Add(albumColumn);

            statsBody.Children.Add(shelves);
        }

        // "Today · Mon 9 Nov" on the left, Day or Week on the right
        Grid StatsHeader(DateTime today)
        {
            var header = new Grid { Margin = new Thickness(4, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = PeriodName(today), FontSize = 15, FontWeight = FontWeights.SemiBold });
            words.Children.Add(Faded(new TextBlock
            {
                Text = weekView ? $"{today.AddDays(1 - WeekDays):d MMM} – {today:d MMM}" : selectedDay.ToString("ddd d MMM"),
                FontSize = 12.5,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            }));
            header.Children.Add(words);

            var mode = Segmented(new[] { "Day", "Week" }, weekView ? 1 : 0, i => Pick(i == 1, i == 1 ? selectedDay : DateTime.Today));
            Grid.SetColumn(mode, 1);
            header.Children.Add(mode);

            return header;
        }

        string PeriodName(DateTime today) =>
            weekView ? "Last 7 days"
            : selectedDay == today ? "Today"
            : selectedDay == today.AddDays(-1) ? "Yesterday"
            : selectedDay.ToString("dddd");

        // The top song, large, over a wash of its cover's colour
        Border Hero(Ranked song)
        {
            var image = Cover(song.Cover);
            var tint = AlbumPalette.Tone(AlbumPalette.From(image).Primary, palette.Light ? 0.86 : 0.2, 0.3, 0.75);

            var hero = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18),
                Background = new LinearGradientBrush(tint, palette.Card, new Point(0, 0), new Point(1, 1))
            };
            hero.SetResourceReference(Border.BorderBrushProperty, "Divider");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var art = Art(image, 104, 12, "");
            art.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = palette.Light ? 0.22 : 0.45, Color = Colors.Black };
            grid.Children.Add(art);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };

            var label = new TextBlock
            {
                Text = (weekView ? "TOP SONG THIS WEEK" : $"TOP SONG · {PeriodName(DateTime.Today).ToUpperInvariant()}"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            words.Children.Add(label);

            words.Children.Add(new TextBlock
            {
                Text = song.Name,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (song.Artist != null)
                words.Children.Add(Faded(new TextBlock { Text = song.Artist, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis }));

            words.Children.Add(Faded(new TextBlock
            {
                Text = PlaysAndTime(song),
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0)
            }));

            Grid.SetColumn(words, 1);
            grid.Children.Add(words);
            hero.Child = grid;

            // Arrives with a soft rise when the page opens
            if (growBars)
            {
                hero.RenderTransform = new TranslateTransform(0, 10);
                hero.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360)));
                hero.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }

            return hero;
        }

        static string PlaysAndTime(Ranked item) =>
            item.Plays switch
            {
                0 => Tight(item.Time),
                1 => $"1 play · {Tight(item.Time)}",
                int n => $"{n} plays · {Tight(item.Time)}"
            };

        // The headline numbers, side by side
        UniformGrid Tiles(ListeningPeriod period, bool week)
        {
            int? peak = period.PeakHour;
            var tiles = new List<(string Value, string Label)> { (Tight(period.Total), "Listened") };

            if (week)
                tiles.Add((Tight(period.Total / WeekDays), "A day, on average"));

            tiles.Add((period.Plays.ToString(), period.Plays == 1 ? "Play" : "Plays"));

            if (!week)
                tiles.Add((period.Songs.Count.ToString(), period.Songs.Count == 1 ? "Song" : "Songs"));

            tiles.Add((period.Artists.Count.ToString(), period.Artists.Count == 1 ? "Artist" : "Artists"));
            tiles.Add((peak is int h ? HourName(h) : "–", "Peak hour"));

            var grid = new UniformGrid { Rows = 1, Columns = tiles.Count };

            foreach (var (value, label) in tiles)
            {
                var tile = new StackPanel();
                tile.Children.Add(new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                tile.Children.Add(Faded(new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));
                grid.Children.Add(tile);
            }

            return grid;
        }

        // A bar for each of the last seven days; clicking one shows that day
        StackPanel WeekChart(IReadOnlyList<(DateTime Day, TimeSpan Total)> daily, DateTime today)
        {
            const double Tallest = 64;
            double most = Math.Max(daily.Max(d => d.Total.TotalSeconds), 1);

            var panel = new StackPanel();
            panel.Children.Add(Caption("This week", "Pick a day to see it"));

            var chart = new Grid { Margin = new Thickness(0, 10, 0, 0) };

            for (int i = 0; i < daily.Count; i++)
            {
                var (day, total) = daily[i];
                bool picked = weekView || day == selectedDay;
                double share = total.TotalSeconds / most;

                chart.ColumnDefinitions.Add(new ColumnDefinition());

                var column = new Grid { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
                column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Tallest + 16) });
                column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                column.ToolTip = $"{day:ddd d MMM}: {ListeningStats.Duration(total)}";

                // The time above the bar, for the picked day
                var amount = new TextBlock
                {
                    Text = total.TotalMinutes >= 1 ? Tight(total) : "",
                    FontSize = 10.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, Math.Max(3, share * Tallest) + 3),
                    Visibility = picked && !weekView ? Visibility.Visible : Visibility.Hidden
                };
                amount.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                column.Children.Add(amount);

                var bar = new Border
                {
                    Height = Math.Max(3, share * Tallest),
                    Margin = new Thickness(7, 0, 7, 0),
                    MaxWidth = 34,
                    CornerRadius = new CornerRadius(5),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    RenderTransformOrigin = new Point(0.5, 1),
                    RenderTransform = new ScaleTransform(1, 1)
                };
                bar.SetResourceReference(Border.BackgroundProperty, total.TotalMinutes < 1 ? "Control" : picked ? "Accent" : "AccentSoft");
                column.Children.Add(bar);

                if (growBars)
                    Rise((ScaleTransform)bar.RenderTransform, i * 35);

                var label = new TextBlock
                {
                    Text = day == today ? "Today" : day.ToString("ddd"),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                if (picked && !weekView)
                {
                    label.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                    label.FontWeight = FontWeights.SemiBold;
                }
                else
                    Faded(label);

                Grid.SetRow(label, 1);
                column.Children.Add(label);

                // Hovering lifts a bar a little; clicking shows its day
                column.MouseEnter += (s, e) => bar.Opacity = 0.8;
                column.MouseLeave += (s, e) => bar.Opacity = 1;
                column.MouseLeftButtonUp += (s, e) => Pick(false, day);

                Grid.SetColumn(column, i);
                chart.Children.Add(column);
            }

            panel.Children.Add(chart);
            return panel;
        }

        // How the listening spreads over the hours of the day
        StackPanel HourChart(ListeningPeriod period)
        {
            const double Tallest = 30;
            int? peak = period.PeakHour;
            double most = Math.Max(period.Hours.Max(h => h.TotalSeconds), 1);

            var panel = new StackPanel();
            panel.Children.Add(Caption("When you listen", peak is int p ? $"Most around {HourName(p)}" : "No times counted yet"));

            var chart = new Grid { Height = Tallest, Margin = new Thickness(0, 10, 0, 0) };
            var labels = new Grid { Margin = new Thickness(0, 4, 0, 0) };

            for (int hour = 0; hour < 24; hour++)
            {
                chart.ColumnDefinitions.Add(new ColumnDefinition());
                labels.ColumnDefinitions.Add(new ColumnDefinition());

                double share = period.Hours[hour].TotalSeconds / most;

                var bar = new Border
                {
                    Height = Math.Max(2, share * Tallest),
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    CornerRadius = new CornerRadius(2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = $"{HourName(hour)}: {ListeningStats.Duration(period.Hours[hour])}",
                    RenderTransformOrigin = new Point(0.5, 1),
                    RenderTransform = new ScaleTransform(1, 1)
                };

                if (share > 0)
                {
                    bar.SetResourceReference(Border.BackgroundProperty, "Accent");
                    bar.Opacity = hour == peak ? 1 : 0.3 + 0.55 * share;
                }
                else
                    bar.SetResourceReference(Border.BackgroundProperty, "Control");

                if (growBars)
                    Rise((ScaleTransform)bar.RenderTransform, 120 + hour * 12);

                Grid.SetColumn(bar, hour);
                chart.Children.Add(bar);

                if (hour % 6 == 0)
                {
                    var label = Faded(new TextBlock { Text = HourName(hour), FontSize = 10.5 });
                    Grid.SetColumn(label, hour);
                    Grid.SetColumnSpan(label, 6);
                    labels.Children.Add(label);
                }
            }

            panel.Children.Add(chart);
            panel.Children.Add(labels);
            return panel;
        }

        // A place in the top songs: rank, cover, title and artist, plays and time, and a bar against the top spot
        Grid SongRow(int rank, Ranked song, TimeSpan longest)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var place = new TextBlock { Text = rank.ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

            // The first place in the accent; the rest quieter
            if (rank == 1)
                place.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            else
                Faded(place);

            grid.Children.Add(place);

            var art = Art(Cover(song.Cover), 40, 7, "");
            art.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(art, 1);
            grid.Children.Add(art);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock
            {
                Text = song.Name,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = song.Artist == null ? song.Name : $"{song.Name} — {song.Artist}"
            });

            if (song.Artist != null)
                words.Children.Add(Faded(new TextBlock { Text = song.Artist, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }));

            words.Children.Add(Bar(longest.TotalSeconds > 0 ? song.Time.TotalSeconds / longest.TotalSeconds : 0));
            Grid.SetColumn(words, 2);
            grid.Children.Add(words);

            var length = Faded(new TextBlock
            {
                Text = PlaysAndTime(song),
                FontSize = 11.5,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(length, 3);
            grid.Children.Add(length);

            return grid;
        }

        // Three artists or albums in a row: a face or cover each, its name and how long it played
        void Shelf(StackPanel card, IReadOnlyList<Ranked> items, bool round, string none)
        {
            if (items.Count == 0)
            {
                card.Children.Add(Padded(Note(none)));
                return;
            }

            var row = new UniformGrid { Rows = 1, Columns = 3 };

            foreach (var item in items)
            {
                var spot = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };

                var art = Art(Cover(item.Cover), 72, round ? 36 : 9, round ? "" : "");
                art.HorizontalAlignment = HorizontalAlignment.Center;
                spot.Children.Add(art);

                spot.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = item.Artist == null ? item.Name : $"{item.Name} — {item.Artist}"
                });

                if (item.Artist != null)
                    spot.Children.Add(Faded(new TextBlock { Text = item.Artist, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));

                spot.Children.Add(Faded(new TextBlock { Text = Tight(item.Time), FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) }));
                row.Children.Add(spot);
            }

            // Fewer than three still sit on the left, at the same size
            for (int i = items.Count; i < 3; i++)
                row.Children.Add(new Border());

            card.Children.Add(new Border { Padding = new Thickness(10, 16, 10, 14), Child = row });
        }

        // A cover saved by the stats, read once and kept
        BitmapImage? Cover(string? file)
        {
            if (file == null)
                return null;

            if (covers.TryGetValue(file, out var known))
                return known;

            BitmapImage? image = null;

            try
            {
                // Read fully now, so the file isn't held open and can be tidied away later
                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 192;
                image.UriSource = new Uri(file);
                image.EndInit();
                image.Freeze();
            }
            catch (Exception ex)
            {
                Log.Info("Couldn't read a listening cover: " + ex.Message);
                image = null;
            }

            covers[file] = image;
            return image;
        }

        // A rounded picture, or a quiet glyph in its place when there's none
        Border Art(BitmapSource? image, double size, double radius, string glyph)
        {
            var art = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center };

            if (image != null)
            {
                art.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(art, BitmapScalingMode.HighQuality);
                return art;
            }

            art.SetResourceReference(Border.BackgroundProperty, "Control");
            art.Child = Faded(new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = size * 0.36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return art;
        }

        static void Rise(ScaleTransform scale, int delayMs) =>
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });

        // A chart's name, with a quieter note beside it
        static Grid Caption(string title, string note)
        {
            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition());
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            caption.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold });

            var right = Faded(new TextBlock { Text = note, FontSize = 11.5 });
            Grid.SetColumn(right, 1);
            caption.Children.Add(right);
            return caption;
        }

        static string HourName(int hour) =>
            Settings.Use24HourClock ? $"{hour:00}:00"
            : hour == 0 ? "12 AM"
            : hour < 12 ? $"{hour} AM"
            : hour == 12 ? "12 PM"
            : $"{hour - 12} PM";

        // "10h 5m", "45m": for tiles and chart labels, where space is short
        static string Tight(TimeSpan time) =>
            time.TotalHours < 1 ? $"{(int)time.TotalMinutes}m"
            : time.Minutes == 0 ? $"{(int)time.TotalHours}h"
            : $"{(int)time.TotalHours}h {time.Minutes}m";

        static Border Padded(UIElement content) => new() { Padding = new Thickness(16, 14, 16, 14), Child = content };

        // A list row that lights up under the pointer, tighter than a setting's row
        Border Compact(UIElement content)
        {
            var row = Hoverable(content);
            row.Padding = new Thickness(14, 8, 14, 8);
            return row;
        }

        // A thin bar, filled in the accent to the given share
        static Grid Bar(double share)
        {
            share = Math.Clamp(share, 0.02, 1);

            var bar = new Grid { Height = 3, Margin = new Thickness(0, 5, 0, 1) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });

            var track = new Border { CornerRadius = new CornerRadius(1.5) };
            track.SetResourceReference(Border.BackgroundProperty, "Control");
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);

            var fill = new Border { CornerRadius = new CornerRadius(1.5) };
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            bar.Children.Add(fill);

            return bar;
        }
    }
}
