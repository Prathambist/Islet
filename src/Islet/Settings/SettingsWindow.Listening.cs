using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

        enum StatsView { Day, Week, Month }

        PageInfo listeningPage = null!;
        StackPanel statsBody = null!;
        Popup detail = null!;
        TextBlock shareStatus = null!;
        readonly DispatcherTimer shareStatusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

        StatsView view = StatsView.Day;
        DateTime selectedDay = DateTime.Today;

        // What was last drawn, so the page is only redrawn when the numbers have moved on
        string? drawnStats;

        // The bars rise when the page opens, not on every refresh
        bool growBars;

        // Covers already read from disk, by file
        readonly Dictionary<string, BitmapImage?> covers = new(StringComparer.OrdinalIgnoreCase);

        int ViewDays => view switch { StatsView.Week => 7, StatsView.Month => 30, _ => 1 };

        DateTime First => view == StatsView.Day ? selectedDay : DateTime.Today.AddDays(1 - ViewDays);
        DateTime Last => view == StatsView.Day ? selectedDay : DateTime.Today;

        // ================= Listening =================

        void BuildListeningPage()
        {
            Page("Listening", "", "Your songs over the last five weeks: what you played, who you played, and when. Videos don't count, and none of it leaves this PC.");
            listeningPage = page;

            statsBody = new StackPanel();
            page.Content.Children.Add(statsBody);

            // Opened over the page when a song, artist or album is clicked; part of the page, so
            // it follows the palette
            detail = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = PlacementMode.Center,
                PopupAnimation = PopupAnimation.Fade
            };
            page.Content.Children.Add(detail);

            var summary = Group("Summary");
            SwitchRow(summary, "Daily music summary",
                "How long you listened today, and to whom, on the idle island at 9 PM; on Sundays, the whole week.",
                Settings.DailyMusicSummary, v => { Settings.DailyMusicSummary = v; Settings.Save(); });

            // Kept current while it's open: songs keep counting as they play
            var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            refresh.Tick += (s, e) =>
            {
                if (shown == listeningPage && !detail.IsOpen)
                    DrawStats();
            };
            refresh.Start();
            shareStatusTimer.Tick += (s, e) =>
            {
                shareStatusTimer.Stop();
                shareStatus.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
            };
            Closed += (s, e) =>
            {
                refresh.Stop();
                shareStatusTimer.Stop();
            };

            listeningPage.Nav.Checked += (s, e) =>
            {
                selectedDay = DateTime.Today;
                growBars = true;
                DrawStats(force: true);
            };

            DrawStats(force: true);
        }

        void Pick(StatsView next, DateTime day)
        {
            view = next;
            selectedDay = day.Date;
            DrawStats(force: true);
        }

        void DrawStats(bool force = false)
        {
            var stats = island.Listening;
            var today = DateTime.Today;
            int chartDays = view == StatsView.Month ? 30 : 7;
            var daily = stats.Daily(today, chartDays);
            var period = stats.Period(First, Last);
            var previous = Previous(stats);
            var songs = period.Songs.Take(SongsShown).ToList();
            var artists = period.Artists.Take(ArtistsShown).ToList();
            var albums = period.Albums.Take(AlbumsShown).ToList();

            // Minutes are what's on screen, so that's what decides whether to redraw
            string drawn = $"{view}|{selectedDay:yyyyMMdd}|{today:yyyyMMdd}|{palette.Name}|" +
                string.Join(",", daily.Select(d => (int)d.Total.TotalMinutes)) + "|" +
                string.Join("|", songs.Concat(artists).Concat(albums).Select(r => $"{r.Name}/{r.Artist}/{(int)r.Time.TotalMinutes}/{r.Plays}/{r.Cover}"));

            if (!force && drawn == drawnStats)
                return;

            drawnStats = drawn;
            statsBody.Children.Clear();
            statsBody.Children.Add(StatsHeader(today, period));

            if (songs.Count > 0)
                statsBody.Children.Add(Hero(songs[0]));

            var overview = Group(null, statsBody);
            ((FrameworkElement)statsBody.Children[^1]).Margin = new Thickness(0, songs.Count > 0 ? 12 : 0, 0, 0);
            AddToCard(overview, Padded(Tiles(period, previous)));
            AddToCard(overview, Padded(DayChart(daily, today)));
            AddToCard(overview, Padded(HourChart(period)));

            if (period.Sources.Count > 0)
                AddToCard(overview, Padded(SourcesBar(period)));

            growBars = false;

            if (songs.Count == 0)
            {
                var empty = Group(null, statsBody);
                empty.Children.Add(Padded(Note(view switch
                {
                    StatsView.Week => "No songs played this week yet.",
                    StatsView.Month => "No songs played this month yet.",
                    _ => selectedDay == today ? "No songs yet today. They show up here once they've played for a minute." : "No songs played on this day."
                })));
                ((FrameworkElement)statsBody.Children[^1]).Margin = new Thickness(0, 12, 0, 0);
                return;
            }

            Highlights(period);

            // Where each song stood last time, for the arrows beside them
            var before = previous?.Songs.Select((s, i) => (s, i)).ToDictionary(x => (x.s.Name, x.s.Artist), x => x.i + 1);
            var songCard = Group("Top songs", statsBody);

            for (int i = 0; i < songs.Count; i++)
            {
                int? was = before == null ? null : before.TryGetValue((songs[i].Name, songs[i].Artist), out int rank) ? rank : 0;
                AddToCard(songCard, Clickable(Compact(SongRow(i + 1, was, songs[i], songs[0].Time)), songs[i]));
            }

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

        // The same length of time just before, to compare with; none for a month, or before counting began
        ListeningPeriod? Previous(ListeningStats stats)
        {
            if (view == StatsView.Month)
                return null;

            var first = First.AddDays(-ViewDays);
            return stats.CountedBy(first) ? stats.Period(first, Last.AddDays(-ViewDays)) : null;
        }

        string Comparison => view == StatsView.Day ? (selectedDay == DateTime.Today ? "yesterday" : "the day before") : "last week";

        // "Today · Fri 13 Nov" on the left; Share, and Day, Week or Month, on the right
        Grid StatsHeader(DateTime today, ListeningPeriod period)
        {
            var header = new Grid { Margin = new Thickness(4, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = PeriodName(today), FontSize = 15, FontWeight = FontWeights.SemiBold });
            line.Children.Add(Faded(new TextBlock
            {
                Text = view == StatsView.Day ? selectedDay.ToString("ddd d MMM") : $"{First:d MMM} – {Last:d MMM}",
                FontSize = 12.5,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            }));
            words.Children.Add(line);

            shareStatus = Faded(new TextBlock { FontSize = 11.5, Opacity = 0, Margin = new Thickness(0, 2, 0, 0) });
            words.Children.Add(shareStatus);
            header.Children.Add(words);

            if (period.Songs.Count > 0)
            {
                var share = PillButton("Share", () => ShareRecap(period));
                share.Margin = new Thickness(0, 0, 10, 0);
                share.VerticalAlignment = VerticalAlignment.Center;
                share.ToolTip = "Copy a picture of this recap, and save it in Pictures";
                Grid.SetColumn(share, 1);
                header.Children.Add(share);
            }

            var mode = Segmented(new[] { "Day", "Week", "Month" }, (int)view,
                i => Pick((StatsView)i, i == 0 ? DateTime.Today : selectedDay));
            mode.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(mode, 2);
            header.Children.Add(mode);

            return header;
        }

        string PeriodName(DateTime today) => view switch
        {
            StatsView.Week => "Last 7 days",
            StatsView.Month => "Last 30 days",
            _ => selectedDay == today ? "Today"
                : selectedDay == today.AddDays(-1) ? "Yesterday"
                : selectedDay.ToString("dddd")
        };

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
                Cursor = Cursors.Hand,
                Background = new LinearGradientBrush(tint, palette.Card, new Point(0, 0), new Point(1, 1))
            };
            hero.SetResourceReference(Border.BorderBrushProperty, "Divider");
            hero.MouseLeftButtonUp += (s, e) => ShowDetail(song);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var art = Art(image, 104, 12, "");
            art.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = palette.Light ? 0.22 : 0.45, Color = Colors.Black };
            grid.Children.Add(art);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };

            var label = new TextBlock
            {
                Text = view switch
                {
                    StatsView.Week => "TOP SONG THIS WEEK",
                    StatsView.Month => "TOP SONG THIS MONTH",
                    _ => $"TOP SONG · {PeriodName(DateTime.Today).ToUpperInvariant()}"
                },
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

            var facts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            facts.Children.Add(Faded(new TextBlock { Text = PlaysAndTime(song), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }));

            if (song.Streak >= 3)
                facts.Children.Add(Badge($"On repeat · {song.Streak}× in a row"));

            if (song.IsNew)
                facts.Children.Add(Badge("New to you"));

            words.Children.Add(facts);

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

        // A small pill in the accent's wash: "New", "On repeat"
        static Border Badge(string text)
        {
            var label = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");

            var badge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label
            };
            badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            return badge;
        }

        static string PlaysAndTime(Ranked item) =>
            item.Plays switch
            {
                0 => Tight(item.Time),
                1 => $"1 play · {Tight(item.Time)}",
                int n => $"{n} plays · {Tight(item.Time)}"
            };

        // The headline numbers side by side, with how they compare to the time before
        UniformGrid Tiles(ListeningPeriod period, ListeningPeriod? previous)
        {
            int? peak = period.PeakHour;
            var tiles = new List<(string Value, string Label, string? Change, bool Up)>();

            string? change = null;
            bool up = false;

            if (previous != null && previous.Total.TotalMinutes >= 1)
            {
                var difference = period.Total - previous.Total;
                up = difference >= TimeSpan.Zero;
                change = Math.Abs(difference.TotalMinutes) < 1
                    ? $"Same as {Comparison}"
                    : $"{(up ? "↑" : "↓")} {Tight(difference.Duration())} vs {Comparison}";
            }

            tiles.Add((Tight(period.Total), "Listened", change, up));

            if (view != StatsView.Day)
                tiles.Add((Tight(period.Total / ViewDays), "A day, on average", null, false));

            tiles.Add((period.Plays.ToString(), period.Plays == 1 ? "Play" : "Plays", null, false));

            if (view == StatsView.Day)
                tiles.Add((period.Songs.Count.ToString(), period.Songs.Count == 1 ? "Song" : "Songs", null, false));

            tiles.Add((period.Artists.Count.ToString(), period.Artists.Count == 1 ? "Artist" : "Artists", null, false));
            tiles.Add((peak is int h ? HourName(h) : "–", "Peak hour", null, false));

            var grid = new UniformGrid { Rows = 1, Columns = tiles.Count };

            foreach (var (value, label, difference, rose) in tiles)
            {
                var tile = new StackPanel();
                tile.Children.Add(new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                tile.Children.Add(Faded(new TextBlock { Text = label, FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));

                if (difference != null)
                {
                    var note = new TextBlock { Text = difference, FontSize = 11, Margin = new Thickness(0, 3, 8, 0), TextWrapping = TextWrapping.Wrap };

                    if (rose)
                        note.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                    else
                        Faded(note);

                    tile.Children.Add(note);
                }

                grid.Children.Add(tile);
            }

            return grid;
        }

        // A bar a day for the last week or month; clicking one shows that day
        StackPanel DayChart(IReadOnlyList<(DateTime Day, TimeSpan Total)> daily, DateTime today)
        {
            const double Tallest = 64;
            double most = Math.Max(daily.Max(d => d.Total.TotalSeconds), 1);
            bool month = daily.Count > 7;

            var panel = new StackPanel();
            panel.Children.Add(Caption(month ? "This month" : "This week", "Pick a day to see it"));

            var chart = new Grid { Margin = new Thickness(0, 10, 0, 0) };

            for (int i = 0; i < daily.Count; i++)
            {
                var (day, total) = daily[i];
                bool picked = view != StatsView.Day || day == selectedDay;
                double share = total.TotalSeconds / most;

                chart.ColumnDefinitions.Add(new ColumnDefinition());

                var column = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
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
                    Visibility = view == StatsView.Day && day == selectedDay ? Visibility.Visible : Visibility.Hidden
                };
                amount.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                column.Children.Add(amount);

                var bar = new Border
                {
                    Height = Math.Max(3, share * Tallest),
                    Margin = month ? new Thickness(1.5, 0, 1.5, 0) : new Thickness(7, 0, 7, 0),
                    MaxWidth = 34,
                    CornerRadius = new CornerRadius(month ? 3 : 5),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    RenderTransformOrigin = new Point(0.5, 1),
                    RenderTransform = new ScaleTransform(1, 1)
                };
                bar.SetResourceReference(Border.BackgroundProperty, total.TotalMinutes < 1 ? "Control" : picked ? "Accent" : "AccentSoft");
                column.Children.Add(bar);

                if (growBars)
                    Rise((ScaleTransform)bar.RenderTransform, i * (month ? 10 : 35));

                // A month's days are labelled every few, so they fit
                string name = day == today ? (month ? "" : "Today") : month ? (i % 5 == 4 ? day.Day.ToString() : "") : day.ToString("ddd");

                var label = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                if (view == StatsView.Day && day == selectedDay)
                {
                    label.Text = month ? day.Day.ToString() : label.Text;
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
                column.MouseLeftButtonUp += (s, e) => Pick(StatsView.Day, day);

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

        // Each player's share of the time, in its own colour where it has a well-known one
        StackPanel SourcesBar(ListeningPeriod period)
        {
            double total = Math.Max(period.Sources.Sum(s => s.Time.TotalSeconds), 1);
            var top = period.Sources.First();

            var panel = new StackPanel();
            panel.Children.Add(Caption("Where you listen", $"{Percent(top.Time.TotalSeconds / total)} on {top.Source}"));

            var bar = new Grid { Height = 8, Margin = new Thickness(0, 10, 0, 0) };
            bar.Clip = new RectangleGeometry(new Rect(0, 0, 10000, 8), 4, 4);
            var legend = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            for (int i = 0; i < period.Sources.Count; i++)
            {
                var (source, time) = period.Sources[i];
                double share = time.TotalSeconds / total;
                var colour = SourceColour(source, i);

                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.004), GridUnitType.Star) });
                var piece = new Border { Margin = new Thickness(i == 0 ? 0 : 1.5, 0, 0, 0), ToolTip = $"{source}: {ListeningStats.Duration(time)}" };

                if (colour is Color c)
                    piece.Background = Frozen(c);
                else
                    piece.SetResourceReference(Border.BackgroundProperty, "Accent");

                Grid.SetColumn(piece, i);
                bar.Children.Add(piece);

                var key = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 2) };
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

                if (colour is Color dotColour)
                    dot.Fill = Frozen(dotColour);
                else
                    dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");

                key.Children.Add(dot);
                key.Children.Add(new TextBlock { Text = source, FontSize = 11.5 });
                key.Children.Add(Faded(new TextBlock { Text = " " + Percent(share), FontSize = 11.5 }));
                legend.Children.Add(key);
            }

            panel.Children.Add(bar);
            panel.Children.Add(legend);
            return panel;
        }

        static string Percent(double share) => share >= 0.995 ? "100%" : share < 0.01 ? "<1%" : $"{share * 100:0}%";

        static Color? SourceColour(string source, int index) => source switch
        {
            "Spotify" => Color.FromRgb(0x1D, 0xB9, 0x54),
            "Apple Music" => Color.FromRgb(0xFA, 0x2D, 0x48),
            "Chrome" => Color.FromRgb(0x42, 0x85, 0xF4),
            "Edge" => Color.FromRgb(0x2F, 0x9B, 0xE8),
            "Firefox" => Color.FromRgb(0xFF, 0x71, 0x39),
            "Brave" => Color.FromRgb(0xFB, 0x54, 0x2B),
            "Opera" => Color.FromRgb(0xFF, 0x1B, 0x2D),
            "Vivaldi" => Color.FromRgb(0xEF, 0x39, 0x39),
            _ => index == 0 ? null : Color.FromRgb(0x8E, 0x8E, 0x93)
        };

        // The things worth a mention: a song on repeat, the one skipped most, the longest
        // stretch of music, and what's new
        void Highlights(ListeningPeriod period)
        {
            var spots = new List<FrameworkElement>();

            if (period.LongestSession >= TimeSpan.FromMinutes(10) && period.LongestSessionStart is DateTime start)
            {
                string when = view == StatsView.Day ? $"From {Clock(start)}" : $"{start:ddd d MMM}, from {Clock(start)}";
                spots.Add(Highlight("", "Longest session", Tight(period.LongestSession), when, null));
            }

            var repeat = period.Songs.Where(s => s.Streak >= 3).OrderByDescending(s => s.Streak).ThenByDescending(s => s.Time).FirstOrDefault();

            if (repeat != null)
                spots.Add(Highlight("", "On repeat", repeat.Name, $"{repeat.Streak} times in a row{(repeat.Artist != null ? " · " + repeat.Artist : "")}", repeat));

            var newArtists = period.Artists.Where(a => a.IsNew).ToList();
            var newSongs = period.Songs.Where(s => s.IsNew).ToList();

            if (newArtists.Count > 0)
                spots.Add(Highlight("", "New to you",
                    newArtists.Count == 1 ? newArtists[0].Name : $"{newArtists.Count} new artists",
                    newArtists.Count == 1 ? "First heard " + (view == StatsView.Day ? "today" : $"on {newArtists[0].FirstHeard:ddd d MMM}") : string.Join(", ", newArtists.Take(3).Select(a => a.Name)),
                    newArtists.Count == 1 ? newArtists[0] : null));
            else if (newSongs.Count > 0)
                spots.Add(Highlight("", "New to you",
                    newSongs.Count == 1 ? newSongs[0].Name : $"{newSongs.Count} new songs",
                    string.Join(", ", newSongs.Take(3).Select(s => s.Name)),
                    newSongs.Count == 1 ? newSongs[0] : null));

            var skipped = period.Songs.Where(s => s.Skips >= 2).OrderByDescending(s => s.Skips).FirstOrDefault();

            if (skipped != null)
                spots.Add(Highlight("", "Skipped most", skipped.Name, $"Skipped {skipped.Skips} times{(skipped.Artist != null ? " · " + skipped.Artist : "")}", skipped));

            if (spots.Count == 0)
                return;

            var card = Group("Highlights", statsBody);
            var grid = new UniformGrid { Columns = 2 };

            foreach (var spot in spots)
                grid.Children.Add(spot);

            card.Children.Add(new Border { Padding = new Thickness(6), Child = grid });
        }

        Border Highlight(string glyph, string label, string value, string note, Ranked? opens)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
            icon.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            var symbol = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            symbol.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            icon.Child = symbol;
            grid.Children.Add(icon);

            var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            words.Children.Add(Faded(new TextBlock { Text = label, FontSize = 11.5 }));
            words.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            words.Children.Add(Faded(new TextBlock { Text = note, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }));
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);

            var spot = Hoverable(grid);
            spot.CornerRadius = new CornerRadius(10);
            spot.Padding = new Thickness(10);

            if (opens != null)
            {
                spot.Cursor = Cursors.Hand;
                spot.MouseLeftButtonUp += (s, e) => ShowDetail(opens);
            }

            return spot;
        }

        static string Clock(DateTime time) => time.ToString(Settings.Use24HourClock ? "HH:mm" : "h:mm tt");

        // A place in the top songs: rank and how it moved, cover, title and artist, plays and time,
        // and a bar against the top spot
        Grid SongRow(int rank, int? was, Ranked song, TimeSpan longest)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var place = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var number = new TextBlock { Text = rank.ToString(), FontSize = 13, FontWeight = FontWeights.SemiBold };

            // The first place in the accent; the rest quieter
            if (rank == 1)
                number.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            else
                Faded(number);

            place.Children.Add(number);

            // Up, down, or new to the list since last time
            if (was is int before)
            {
                var move = new TextBlock
                {
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Text = before == 0 ? "NEW" : before > rank ? $"▲{before - rank}" : before < rank ? $"▼{rank - before}" : "–"
                };

                if (before == 0 || before > rank)
                    move.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                else
                    Faded(move);

                move.ToolTip = before == 0 ? $"Not among {Comparison}'s songs" : $"{Ordinal(before)} {Comparison}";
                place.Children.Add(move);
            }

            grid.Children.Add(place);

            var art = Art(Cover(song.Cover), 40, 7, "");
            art.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(art, 1);
            grid.Children.Add(art);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock { Text = song.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 });

            if (song.IsNew)
                title.Children.Add(Badge("New"));
            else if (song.Streak >= 3)
                title.Children.Add(Badge($"{song.Streak}× in a row"));

            words.Children.Add(title);

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

        static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

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

                var time = Faded(new TextBlock { Text = Tight(item.Time), FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });

                if (item.IsNew)
                {
                    time.Text += " · New";
                    time.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                }

                spot.Children.Add(time);

                var hover = Hoverable(spot);
                hover.CornerRadius = new CornerRadius(10);
                hover.Padding = new Thickness(2, 8, 2, 8);
                row.Children.Add(Clickable(hover, item));
            }

            // Fewer than three still sit on the left, at the same size
            for (int i = items.Count; i < 3; i++)
                row.Children.Add(new Border());

            card.Children.Add(new Border { Padding = new Thickness(8, 8, 8, 8), Child = row });
        }

        // Clicking a song, artist or album opens its details
        Border Clickable(Border row, Ranked item)
        {
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (s, e) => ShowDetail(item);
            return row;
        }

        // ================= A song, artist or album up close =================

        void ShowDetail(Ranked item)
        {
            const int HistoryDays = 14;
            var stats = island.Listening;
            var period = stats.Period(First, Last);
            var image = Cover(item.Cover);
            bool artist = item.Kind == RankedKind.Artist;

            var panel = new StackPanel();

            // Cover or face, name, and where it comes from
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var art = Art(image, 84, artist ? 42 : 12, artist ? "" : item.Kind == RankedKind.Album ? "" : "");
            art.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.3, Color = Colors.Black };
            top.Children.Add(art);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) };
            var kind = new TextBlock { Text = item.Kind.ToString().ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold };
            kind.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            words.Children.Add(kind);
            words.Children.Add(new TextBlock { Text = item.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 50, TextTrimming = TextTrimming.CharacterEllipsis });

            string? from = item.Kind switch
            {
                RankedKind.Song => string.Join(" · ", new[] { item.Artist, item.Album }.Where(x => !string.IsNullOrEmpty(x))),
                RankedKind.Album => item.Artist,
                _ => null
            };

            if (!string.IsNullOrEmpty(from))
                words.Children.Add(Faded(new TextBlock { Text = from, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis }));

            if (item.FirstHeard is DateTime heard)
                words.Children.Add(Faded(new TextBlock { Text = $"First heard {heard:d MMM yyyy}", FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) }));

            Grid.SetColumn(words, 1);
            top.Children.Add(words);

            var close = new Button
            {
                Content = "",
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 11,
                Width = 28,
                Height = 28,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Template = TransparentTemplate(),
                ToolTip = "Close"
            };
            Faded(close);
            close.Click += (s, e) => detail.IsOpen = false;
            Grid.SetColumn(close, 2);
            top.Children.Add(close);
            panel.Children.Add(top);

            // Its numbers over the period being looked at
            var numbers = new List<(string, string)> { (Tight(item.Time), PeriodName(DateTime.Today)) };

            if (item.Plays > 0)
                numbers.Add((item.Plays.ToString(), item.Plays == 1 ? "Play" : "Plays"));

            if (item.Kind == RankedKind.Song)
            {
                if (item.Streak >= 2)
                    numbers.Add(($"{item.Streak}×", "Most in a row"));

                if (item.Skips > 0)
                    numbers.Add((item.Skips.ToString(), item.Skips == 1 ? "Skip" : "Skips"));
            }
            else
            {
                int share = (int)Math.Round(item.Time.TotalSeconds / Math.Max(period.Total.TotalSeconds, 1) * 100);
                numbers.Add(($"{share}%", "Of your listening"));
            }

            var tiles = new UniformGrid { Rows = 1, Columns = numbers.Count, Margin = new Thickness(0, 18, 0, 0) };

            foreach (var (value, label) in numbers)
            {
                var tile = new StackPanel();
                tile.Children.Add(new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeights.SemiBold });
                tile.Children.Add(Faded(new TextBlock { Text = label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }));
                tiles.Children.Add(tile);
            }

            panel.Children.Add(tiles);

            // Day by day over the last two weeks
            var history = stats.History(item, DateTime.Today, HistoryDays);
            double most = Math.Max(history.Max(h => h.Time.TotalSeconds), 1);
            var chartBox = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            chartBox.Children.Add(Caption("Last two weeks", $"{history.Count(h => h.Time.TotalMinutes >= 1)} of {HistoryDays} days"));

            var chart = new Grid { Height = 40, Margin = new Thickness(0, 8, 0, 0) };

            for (int i = 0; i < history.Count; i++)
            {
                chart.ColumnDefinitions.Add(new ColumnDefinition());
                var bar = new Border
                {
                    Height = Math.Max(2, history[i].Time.TotalSeconds / most * 40),
                    Margin = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(2.5),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = $"{history[i].Day:ddd d MMM}: {ListeningStats.Duration(history[i].Time)}"
                };
                bar.SetResourceReference(Border.BackgroundProperty, history[i].Time.TotalMinutes >= 1 ? "Accent" : "Control");
                Grid.SetColumn(bar, i);
                chart.Children.Add(bar);
            }

            chartBox.Children.Add(chart);
            panel.Children.Add(chartBox);

            // An artist's or album's songs you played
            var their = item.Kind switch
            {
                RankedKind.Artist => period.Songs.Where(s => s.Artist == item.Name).Take(4).ToList(),
                RankedKind.Album => period.Songs.Where(s => s.Artist == item.Artist && s.Album == item.Name).Take(4).ToList(),
                _ => new List<Ranked>()
            };

            if (their.Count > 0)
            {
                var list = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
                list.Children.Add(Caption("Songs you played", their.Count == 1 ? "" : $"Top {their.Count}"));

                foreach (var song in their)
                {
                    var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    row.Children.Add(Art(Cover(song.Cover), 30, 5, ""));

                    var name = new TextBlock { Text = song.Name, FontSize = 12.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetColumn(name, 1);
                    row.Children.Add(name);

                    var time = Faded(new TextBlock { Text = PlaysAndTime(song), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
                    Grid.SetColumn(time, 2);
                    row.Children.Add(time);

                    list.Children.Add(row);
                }

                panel.Children.Add(list);
            }

            var open = PillButton("Open in Spotify", () => OpenInSpotify(item));
            open.Margin = new Thickness(0, 20, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(open);

            var card = new Border
            {
                Width = 420,
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20),
                Margin = new Thickness(24),
                Child = panel,
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.35, Color = Colors.Black }
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBg");
            card.SetResourceReference(Border.BorderBrushProperty, "Divider");

            detail.PlacementTarget = (UIElement)Content;
            detail.Child = card;
            detail.IsOpen = true;
        }

        // A search in the Spotify app, or on the web without it
        static void OpenInSpotify(Ranked item)
        {
            string query = item.Artist != null && item.Kind != RankedKind.Artist ? $"{item.Name} {item.Artist}" : item.Name;

            try
            {
                Process.Start(new ProcessStartInfo("spotify:search:" + Uri.EscapeDataString(query)) { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://open.spotify.com/search/" + Uri.EscapeDataString(query)) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error("Opening Spotify failed", ex);
                }
            }
        }

        // ================= Sharing =================

        // A picture of the period's recap, copied and saved to Pictures\Islet
        void ShareRecap(ListeningPeriod period)
        {
            try
            {
                var card = ListeningRecap.Render(period, view switch
                {
                    StatsView.Week => "My week in music",
                    StatsView.Month => "My month in music",
                    _ => selectedDay == DateTime.Today ? "My day in music" : $"My {selectedDay:dddd} in music"
                }, view == StatsView.Day ? selectedDay.ToString("dddd d MMMM") : $"{First:d MMM} – {Last:d MMM yyyy}", Cover);

                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Islet");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, $"Islet recap {DateTime.Now:yyyy-MM-dd HHmmss}.png");

                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(card));
                using (var stream = File.Create(file))
                    png.Save(stream);

                Clipboard.SetImage(card);
                Flash("Copied, and saved in Pictures\\Islet");
            }
            catch (Exception ex)
            {
                Log.Error("Making the recap picture failed", ex);
                Flash("Couldn't make the picture");
            }
        }

        void Flash(string text)
        {
            shareStatus.Text = text;
            shareStatus.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            shareStatusTimer.Stop();
            shareStatusTimer.Start();
        }

        // ================= Small parts =================

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
        internal static string Tight(TimeSpan time) =>
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
