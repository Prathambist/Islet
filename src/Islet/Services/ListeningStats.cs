using System.IO;
using System.Text.Json;

namespace Islet
{
    /// <summary>How much music played in one day, and what.</summary>
    public sealed record DaySummary(TimeSpan Total, string? TopArtist, string? TopSong)
    {
        public static readonly DaySummary Empty = new(TimeSpan.Zero, null, null);
    }

    /// <summary>A song or artist in a day's top list, with how long it played. Artist is null for an artist's own entry.</summary>
    public sealed record Ranked(string Name, string? Artist, TimeSpan Time);

    /// <summary>
    /// Keeps count of how long music actually plays each day, by artist and by song, for the
    /// daily summary and the Listening page. Only playing time counts, not paused. Only today
    /// and yesterday are kept, in their own small file, and nowhere else: none of it leaves the PC.
    /// </summary>
    public sealed class ListeningStats
    {
        /// <summary>Today and yesterday.</summary>
        public const int DaysKept = 2;

        const string Separator = " — ";

        // Seconds, by day ("2026-11-09"), then by artist and by "Title — Artist"
        sealed class Day
        {
            public double Seconds { get; set; }
            public Dictionary<string, double> Artists { get; set; } = new();
            public Dictionary<string, double> Songs { get; set; } = new();
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

        public void Add(DateTime when, string artist, string title, TimeSpan played)
        {
            if (played <= TimeSpan.Zero)
                return;

            string key = Key(when);

            if (!store.Days.TryGetValue(key, out var day))
                store.Days[key] = day = new Day();

            double seconds = played.TotalSeconds;
            day.Seconds += seconds;

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

        /// <summary>The songs that played longest on that day, longest first, each split into title and artist.</summary>
        public IReadOnlyList<Ranked> TopSongs(DateTime when, int count)
        {
            if (!store.Days.TryGetValue(Key(when), out var day))
                return Array.Empty<Ranked>();

            return Ranking(day.Songs).Take(count).Select(s =>
            {
                int split = s.Key.LastIndexOf(Separator, StringComparison.Ordinal);
                return split < 0
                    ? new Ranked(s.Key, null, TimeSpan.FromSeconds(s.Value))
                    : new Ranked(s.Key[..split], s.Key[(split + Separator.Length)..], TimeSpan.FromSeconds(s.Value));
            }).ToList();
        }

        /// <summary>The artists heard longest on that day, longest first.</summary>
        public IReadOnlyList<Ranked> TopArtists(DateTime when, int count) =>
            store.Days.TryGetValue(Key(when), out var day)
                ? Ranking(day.Artists).Take(count).Select(a => new Ranked(a.Key, null, TimeSpan.FromSeconds(a.Value))).ToList()
                : Array.Empty<Ranked>();

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

            // Days before yesterday aren't shown anywhere
            string oldest = Key(clock().AddDays(1 - DaysKept));

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

            // Anything older than yesterday, left from before, goes on the next save
            string oldest = Key(clock().AddDays(1 - DaysKept));
            dirty = store.Days.Keys.Any(k => string.CompareOrdinal(k, oldest) < 0);
        }
    }
}
