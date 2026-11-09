using System.IO;
using System.Text.Json;

namespace Islet
{
    /// <summary>How much music played in one day, and what.</summary>
    public sealed record DaySummary(TimeSpan Total, string? TopArtist, string? TopSong)
    {
        public static readonly DaySummary Empty = new(TimeSpan.Zero, null, null);
    }

    /// <summary>A song or artist in a top list, with how long it played. Artist is null for an artist's own entry.</summary>
    public sealed record Ranked(string Name, string? Artist, TimeSpan Time);

    /// <summary>
    /// Everything that played over one or more days: the total, every song and artist longest
    /// first, and how the listening spread over the hours of the day.
    /// </summary>
    public sealed record ListeningPeriod(TimeSpan Total, IReadOnlyList<Ranked> Songs, IReadOnlyList<Ranked> Artists, IReadOnlyList<TimeSpan> Hours)
    {
        /// <summary>The hour of the day with the most music, or null with none counted.</summary>
        public int? PeakHour
        {
            get
            {
                int peak = 0;

                for (int hour = 1; hour < Hours.Count; hour++)
                    if (Hours[hour] > Hours[peak])
                        peak = hour;

                return Hours.Count > 0 && Hours[peak] > TimeSpan.Zero ? peak : null;
            }
        }
    }

    /// <summary>
    /// Keeps count of how long music actually plays each day, by artist, by song and by hour, for
    /// the daily summary and the Listening page. Only playing time counts, not paused. The last
    /// week is kept, in its own small file, and nowhere else: none of it leaves the PC.
    /// </summary>
    public sealed class ListeningStats
    {
        /// <summary>Today and the six days before it.</summary>
        public const int DaysKept = 7;

        const string Separator = " — ";

        // Seconds, by day ("2026-11-09"), then by artist, by "Title — Artist" and by hour
        sealed class Day
        {
            public double Seconds { get; set; }
            public Dictionary<string, double> Artists { get; set; } = new();
            public Dictionary<string, double> Songs { get; set; } = new();

            // Missing from days counted before hours were
            public double[]? Hours { get; set; }
        }

        sealed class Store
        {
            public Dictionary<string, Day> Days { get; set; } = new();

            /// <summary>The last day the evening summary was shown, so it's shown once.</summary>
            public string? SummaryShownOn { get; set; }
        }

        readonly string path;
        readonly Func<DateTime> clock;
        Store store = new();
        DateTime lastSaved = DateTime.MinValue;
        bool dirty;

        public ListeningStats(string folder, Func<DateTime>? clock = null)
        {
            path = Path.Combine(folder, "listening.json");
            this.clock = clock ?? (() => DateTime.Now);
            Load();
        }

        static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

        string Oldest => Key(clock().AddDays(1 - DaysKept));

        public void Add(DateTime when, string artist, string title, TimeSpan played)
        {
            if (played <= TimeSpan.Zero)
                return;

            string key = Key(when);

            if (!store.Days.TryGetValue(key, out var day))
                store.Days[key] = day = new Day();

            double seconds = played.TotalSeconds;
            day.Seconds += seconds;

            day.Hours ??= new double[24];
            day.Hours[when.Hour] += seconds;

            artist = artist.Trim();
            title = title.Trim();

            if (artist.Length > 0)
                day.Artists[artist] = day.Artists.GetValueOrDefault(artist) + seconds;

            if (title.Length > 0)
            {
                string song = artist.Length > 0 ? title + Separator + artist : title;
                day.Songs[song] = day.Songs.GetValueOrDefault(song) + seconds;
            }

            dirty = true;

            // Written now and then rather than every second; on quitting too
            if (DateTime.UtcNow - lastSaved > TimeSpan.FromMinutes(1))
                Save();
        }

        /// <summary>
        /// Whether a track counts as music for these stats. YouTube videos don't: browsers don't
        /// say which site is playing, but a video comes with a wide thumbnail and no album, while
        /// YouTube Music, Spotify Web and the like send a square cover and usually an album.
        /// </summary>
        public static bool Counts(TrackInfo track) =>
            track.Source != MediaSource.Browser ||
            !string.IsNullOrWhiteSpace(track.Album) ||
            (track.Artwork != null && ArtworkService.IsSquare(track.Artwork));

        public DaySummary For(DateTime when)
        {
            if (!store.Days.TryGetValue(Key(when), out var day))
                return DaySummary.Empty;

            return new DaySummary(
                TimeSpan.FromSeconds(day.Seconds),
                Top(day.Artists),
                Top(day.Songs));
        }

        static string? Top(Dictionary<string, double> counts) =>
            counts.Count == 0 ? null : Ranking(counts).First().Key;

        static IEnumerable<KeyValuePair<string, double>> Ranking(Dictionary<string, double> counts) =>
            counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal);

        /// <summary>Everything that played from <paramref name="first"/> to <paramref name="last"/>, both days included.</summary>
        public ListeningPeriod Period(DateTime first, DateTime last)
        {
            double seconds = 0;
            var songs = new Dictionary<string, double>();
            var artists = new Dictionary<string, double>();
            var hours = new double[24];

            for (var date = first.Date; date <= last.Date; date = date.AddDays(1))
            {
                if (!store.Days.TryGetValue(Key(date), out var day))
                    continue;

                seconds += day.Seconds;

                foreach (var (song, s) in day.Songs)
                    songs[song] = songs.GetValueOrDefault(song) + s;

                foreach (var (artist, s) in day.Artists)
                    artists[artist] = artists.GetValueOrDefault(artist) + s;

                if (day.Hours != null)
                    for (int hour = 0; hour < 24 && hour < day.Hours.Length; hour++)
                        hours[hour] += day.Hours[hour];
            }

            return new ListeningPeriod(
                TimeSpan.FromSeconds(seconds),
                Ranking(songs).Select(s => SongEntry(s.Key, s.Value)).ToList(),
                Ranking(artists).Select(a => new Ranked(a.Key, null, TimeSpan.FromSeconds(a.Value))).ToList(),
                hours.Select(TimeSpan.FromSeconds).ToList());
        }

        // "Title — Artist" back into its title and artist
        static Ranked SongEntry(string key, double seconds)
        {
            int split = key.LastIndexOf(Separator, StringComparison.Ordinal);
            return split < 0
                ? new Ranked(key, null, TimeSpan.FromSeconds(seconds))
                : new Ranked(key[..split], key[(split + Separator.Length)..], TimeSpan.FromSeconds(seconds));
        }

        /// <summary>How long music played on each of the <paramref name="days"/> days up to <paramref name="last"/>, oldest first.</summary>
        public IReadOnlyList<(DateTime Day, TimeSpan Total)> Daily(DateTime last, int days) =>
            Enumerable.Range(0, days)
                .Select(i => last.Date.AddDays(i - days + 1))
                .Select(d => (d, store.Days.TryGetValue(Key(d), out var day) ? TimeSpan.FromSeconds(day.Seconds) : TimeSpan.Zero))
                .ToList();

        /// <summary>The songs that played longest on that day, longest first, each split into title and artist.</summary>
        public IReadOnlyList<Ranked> TopSongs(DateTime when, int count) => Period(when, when).Songs.Take(count).ToList();

        /// <summary>The artists heard longest on that day, longest first.</summary>
        public IReadOnlyList<Ranked> TopArtists(DateTime when, int count) => Period(when, when).Artists.Take(count).ToList();

        public bool SummaryShown(DateTime when) => store.SummaryShownOn == Key(when);

        public void MarkSummaryShown(DateTime when)
        {
            store.SummaryShownOn = Key(when);
            dirty = true;
            Save();
        }

        /// <summary>"2 h 14 min", "45 min", "under a minute".</summary>
        public static string Duration(TimeSpan time) =>
            time.TotalMinutes < 1 ? "under a minute"
            : time.TotalHours < 1 ? $"{(int)time.TotalMinutes} min"
            : time.Minutes == 0 ? $"{(int)time.TotalHours} h"
            : $"{(int)time.TotalHours} h {time.Minutes} min";

        /// <summary>"2 h 14 min", "45 min", "0 min": the same, where space is short.</summary>
        public static string ShortDuration(TimeSpan time) =>
            time.TotalHours < 1 ? $"{(int)time.TotalMinutes} min" : Duration(time);

        /// <summary>"Today: 2 h 14 min of music · mostly Arctic Monkeys", or null with nothing played yet.</summary>
        public static string? Describe(DaySummary today)
        {
            if (today.Total.TotalMinutes < 1)
                return null;

            string text = $"Today: {Duration(today.Total)} of music";
            return today.TopArtist == null ? text : $"{text} · mostly {today.TopArtist}";
        }

        public void Save()
        {
            if (!dirty)
                return;

            // Days before the last week aren't shown anywhere
            string oldest = Oldest;

            foreach (var key in store.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
                store.Days.Remove(key);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(store));
                dirty = false;
                lastSaved = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Log.Error("Saving listening stats failed", ex);
            }
        }

        void Load()
        {
            try
            {
                if (File.Exists(path))
                    store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path)) ?? new Store();
            }
            catch (Exception ex)
            {
                Log.Error("Reading listening stats failed; starting afresh", ex);
                store = new Store();
            }

            // Anything older than the last week, left from before, goes on the next save
            string oldest = Oldest;
            dirty = store.Days.Keys.Any(k => string.CompareOrdinal(k, oldest) < 0);
        }
    }
}
