using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Islet
{
    public partial class SettingsWindow
    {
        const int SongsShown = 5;
        const int ArtistsShown = 3;
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

        // ================= Listening =================

        void BuildListeningPage()
        {
            Page("Listening", "", "Your last seven days of music: when you listen, and to what. Counted on this PC only.");
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

            // Minutes are what's on screen, so that's what decides whether to redraw
            string drawn = $"{weekView}|{selectedDay:yyyyMMdd}|{today:yyyyMMdd}|" +
                string.Join(",", daily.Select(d => (int)d.Total.TotalMinutes)) + "|" +
                string.Join("|", songs.Concat(artists).Select(r => $"{r.Name}/{r.Artist}/{(int)r.Time.TotalMinutes}"));

            if (!force && drawn == drawnStats)
                return;

            drawnStats = drawn;
            statsBody.Children.Clear();

            statsBody.Children.Add(StatsHeader(today));

            var overview = Group(null, statsBody);
            AddToCard(overview, Padded(Tiles(period, weekView)));
            AddToCard(overview, Padded(WeekChart(daily, today)));
            AddToCard(overview, Padded(HourChart(period)));
            growBars = false;

            if (period.Total.TotalMinutes < 1)
            {
                var empty = Group(null, statsBody);
                empty.Children.Add(Padded(Note(weekView ? "Nothing played this week yet."
                    : selectedDay == today ? "Nothing played yet today. Songs show up here once they've played for a minute."
                    : "Nothing played on this day.")));
                ((FrameworkElement)statsBody.Children[^1]).Margin = new Thickness(0, 12, 0, 0);
                return;
            }

            // Songs and artists side by side
            var lists = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            var songColumn = new StackPanel();
            var songCard = Group("Top songs", songColumn);
            ((FrameworkElement)songColumn.Children[0]).Margin = new Thickness(4, 22, 0, 8);

            for (int i = 0; i < songs.Count; i++)
                AddToCard(songCard, Compact(RankRow(i + 1, songs[i].Name, songs[i].Artist, songs[i].Time, songs[0].Time)));

            lists.Children.Add(songColumn);

            var artistColumn = new StackPanel();
            var artistCard = Group("Top artists", artistColumn);
            ((FrameworkElement)artistColumn.Children[0]).Margin = new Thickness(4, 22, 0, 8);

            for (int i = 0; i < artists.Count; i++)
                AddToCard(artistCard, Compact(RankRow(i + 1, artists[i].Name, null, artists[i].Time, artists[0].Time)));

            if (artists.Count == 0)
                artistCard.Children.Add(Padded(Note("No artist names on what played.")));

            Grid.SetColumn(artistColumn, 2);
            lists.Children.Add(artistColumn);

            statsBody.Children.Add(lists);
        }

        // "Today · Sun 9 Nov" on the left, Day or Week on the right
        Grid StatsHeader(DateTime today)
        {
            var header = new Grid { Margin = new Thickness(4, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string title = weekView ? "Last 7 days"
                : selectedDay == today ? "Today"
                : selectedDay == today.AddDays(-1) ? "Yesterday"
                : selectedDay.ToString("dddd");

            string when = weekView
                ? $"{today.AddDays(1 - WeekDays):d MMM} – {today:d MMM}"
                : selectedDay.ToString("ddd d MMM");

            var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            words.Children.Add(Faded(new TextBlock { Text = when, FontSize = 12.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom }));
            header.Children.Add(words);

            var mode = Segmented(new[] { "Day", "Week" }, weekView ? 1 : 0, i => Pick(i == 1, i == 1 ? selectedDay : DateTime.Today));
            Grid.SetColumn(mode, 1);
            header.Children.Add(mode);

            return header;
        }

        // The headline numbers, side by side
        UniformGrid Tiles(ListeningPeriod period, bool week)
        {
            int? peak = period.PeakHour;
            var tiles = new List<(string Value, string Label)> { (Tight(period.Total), "Listened") };

            if (week)
                tiles.Add((Tight(period.Total / WeekDays), "A day, on average"));

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
            row.Padding = new Thickness(14, 9, 14, 9);
            return row;
        }

        // A place in a top list: its rank, its name, how long it played, and a bar against the top spot
        static Grid RankRow(int rank, string name, string? artist, TimeSpan time, TimeSpan longest)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var place = new TextBlock
            {
                Text = rank.ToString(),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            // The first place in the accent; the rest quieter
            if (rank == 1)
                place.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            else
                Faded(place);

            grid.Children.Add(place);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = artist == null ? name : $"{name} — {artist}"
            });

            if (artist != null)
                words.Children.Add(Faded(new TextBlock { Text = artist, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }));

            words.Children.Add(Bar(longest.TotalSeconds > 0 ? time.TotalSeconds / longest.TotalSeconds : 0));
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);

            var length = Faded(new TextBlock
            {
                Text = ListeningStats.ShortDuration(time),
                FontSize = 11.5,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(length, 2);
            grid.Children.Add(length);

            return grid;
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
