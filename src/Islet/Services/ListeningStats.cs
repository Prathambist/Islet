using System.IO;
using System.Text.Json;

namespace Islet
{
    /// <summary>How much music played in one day, and what.</summary>
    public sealed record DaySummary(TimeSpan Total, string? TopArtist, string? TopSong)
    {
        public static readonly DaySummary Empty = new(TimeSpan.Zero, null, null);
    }

    /// <summary>
    /// Keeps count of how long music actually plays each day, by artist and by song, for the
    /// daily summary. Only playing time counts, not paused. Kept for 60 days, in its own small
    /// file, and nowhere else: none of it leaves the PC.
    /// </summary>
    public sealed class ListeningStats
    {
        public const int DaysKept = 60;

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
        Store store = new();
        DateTime lastSaved = DateTime.MinValue;
        bool dirty;

        public ListeningStats(string folder)
        {
            path = Path.Combine(folder, "listening.json");
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
                string song = artist.Length > 0 ? $"{title} — {artist}" : title;
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
            counts.Count == 0 ? null : counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key).First().Key;

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

            // Older days than this aren't shown anywhere
            string oldest = Key(DateTime.Now.AddDays(-DaysKept));

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
            catch
            {
                store = new Store();
            }
        }
    }
}
