using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Islet
{
    public partial class SettingsWindow
    {
        const int SongsShown = 5;
        const int ArtistsShown = 3;

        PageInfo listeningPage = null!;
        StackPanel statsBody = null!;
        bool showYesterday;

        // What was last drawn, so the page is only redrawn when the numbers have moved on
        string? drawnStats;

        // ================= Listening =================

        void BuildListeningPage()
        {
            Page("Listening", "", "What you've played today and yesterday: your top songs and artists. Counted on this PC only.");
            listeningPage = page;

            var day = Segmented(new[] { "Today", "Yesterday" }, 0, i =>
            {
                showYesterday = i == 1;
                DrawStats(force: true);
            });
            day.Margin = new Thickness(0, 0, 0, 16);
            page.Content.Children.Add(day);

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

            listeningPage.Nav.Checked += (s, e) => DrawStats(force: true);
            DrawStats(force: true);
        }

        void DrawStats(bool force = false)
        {
            var stats = island.Listening;
            var when = DateTime.Now.AddDays(showYesterday ? -1 : 0);

            var total = stats.For(when).Total;
            var songs = stats.TopSongs(when, SongsShown);
            var artists = stats.TopArtists(when, ArtistsShown);

            // Minutes are what's on screen, so that's what decides whether to redraw
            string drawn = $"{showYesterday}|{(int)total.TotalMinutes}|" +
                string.Join("|", songs.Concat(artists).Select(r => $"{r.Name}/{r.Artist}/{(int)r.Time.TotalMinutes}"));

            if (!force && drawn == drawnStats)
                return;

            drawnStats = drawn;
            statsBody.Children.Clear();

            string dayName = showYesterday ? "yesterday" : "today";

            if (total.TotalMinutes < 1)
            {
                var empty = Group(null, statsBody);
                empty.Children.Add(new Border
                {
                    Padding = new Thickness(16, 22, 16, 22),
                    Child = Note(showYesterday
                        ? "Nothing played yesterday."
                        : "Nothing played yet today. Songs show up here once they've played for a minute.")
                });
                return;
            }

            // The day's total, large
            var totals = Group(null, statsBody);
            var headline = new StackPanel();
            headline.Children.Add(new TextBlock
            {
                Text = ListeningStats.Duration(total),
                FontSize = 28,
                FontWeight = FontWeights.SemiBold
            });
            headline.Children.Add(Faded(new TextBlock
            {
                Text = artists.Count > 0 ? $"of music {dayName} · mostly {artists[0].Name}" : $"of music {dayName}",
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            }));
            totals.Children.Add(new Border { Padding = new Thickness(18, 16, 18, 16), Child = headline });

            if (songs.Count > 0)
            {
                var top = Group("Top songs", statsBody);

                for (int i = 0; i < songs.Count; i++)
                    AddToCard(top, Hoverable(RankRow(i + 1, songs[i].Name, songs[i].Artist, songs[i].Time, songs[0].Time)));
            }

            if (artists.Count > 0)
            {
                var top = Group("Top artists", statsBody);

                for (int i = 0; i < artists.Count; i++)
                    AddToCard(top, Hoverable(RankRow(i + 1, artists[i].Name, null, artists[i].Time, artists[0].Time)));
            }
        }

        // A place in a top list: its rank, its name, how long it played, and a bar
        // against the day's top spot
        static Grid RankRow(int rank, string name, string? artist, TimeSpan time, TimeSpan longest)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var place = new TextBlock
            {
                Text = rank.ToString(),
                FontSize = 15,
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
                FontSize = 13.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = artist == null ? name : $"{name} — {artist}"
            });

            if (artist != null)
                words.Children.Add(Faded(new TextBlock { Text = artist, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }));

            words.Children.Add(Bar(longest.TotalSeconds > 0 ? time.TotalSeconds / longest.TotalSeconds : 0));
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);

            var length = Faded(new TextBlock
            {
                Text = ListeningStats.Duration(time),
                FontSize = 12,
                Margin = new Thickness(16, 0, 0, 0),
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

            var bar = new Grid { Height = 3, Margin = new Thickness(0, 7, 0, 1) };
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
