using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet
{
    /// <summary>How much music played in one day, and what.</summary>
    public sealed record DaySummary(TimeSpan Total, string? TopArtist, string? TopSong)
    {
        public static readonly DaySummary Empty = new(TimeSpan.Zero, null, null);
    }

    /// <summary>
    /// A song, artist or album in a top list, with how long it played. Artist is null for an
    /// artist's own entry. Cover is a saved picture of its album, when there is one.
    /// </summary>
    public sealed record Ranked(string Name, string? Artist, TimeSpan Time)
    {
        /// <summary>How many times it was played, counting a play once it has gone on for half a minute.</summary>
        public int Plays { get; init; }

        /// <summary>The full path of its cover, saved on this PC, or null.</summary>
        public string? Cover { get; init; }
    }

    /// <summary>
    /// Everything that played over one or more days: the total, every song, artist and album
    /// longest first, and how the listening spread over the hours of the day.
    /// </summary>
    public sealed record ListeningPeriod(
        TimeSpan Total,
        IReadOnlyList<Ranked> Songs,
        IReadOnlyList<Ranked> Artists,
        IReadOnlyList<TimeSpan> Hours)
    {
        public IReadOnlyList<Ranked> Albums { get; init; } = Array.Empty<Ranked>();

        /// <summary>Every play of every song in the period.</summary>
        public int Plays => Songs.Sum(s => s.Plays);

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
    /// Keeps count of how long songs actually play each day, by song, artist, album and hour, with
    /// a small copy of each album's cover, for the daily summary and the Listening page. Only
    /// playing time counts, and only songs: not videos or other media. The last week is kept, in
    /// its own small file and a folder of covers, and nowhere else: none of it leaves the PC.
    /// </summary>
    public sealed class ListeningStats
    {
        /// <summary>Today and the six days before it.</summary>
        public const int DaysKept = 7;

        /// <summary>How long a song plays before it counts as a play.</summary>
        public static readonly TimeSpan PlayAfter = TimeSpan.FromSeconds(30);

        const string Separator = " — ";
        const int CoverSize = 192;

        // Seconds, by day ("2026-11-09"), then by artist, by "Title — Artist", by album and by hour
        sealed class Day
        {
            public double Seconds { get; set; }
            public Dictionary<string, double> Artists { get; set; } = new();
            public Dictionary<string, double> Songs { get; set; } = new();
            public Dictionary<string, double> Albums { get; set; } = new();
            public Dictionary<string, int> Plays { get; set; } = new();

            // Missing from days counted before hours were
            public double[]? Hours { get; set; }
        }

        // What's known of a song beyond its name: its album, and the cover saved for it
        sealed class Song
        {
            public string Album { get; set; } = "";
            public string? Cover { get; set; }
        }

        sealed class Store
        {
            public Dictionary<string, Day> Days { get; set; } = new();
            public Dictionary<string, Song> Songs { get; set; } = new();

            /// <summary>The last day the evening summary was shown, so it's shown once.</summary>
            public string? SummaryShownOn { get; set; }
        }

        readonly string path;
        readonly string coverFolder;
        readonly Func<DateTime> clock;
        Store store = new();
        DateTime lastSaved = DateTime.MinValue;
        bool dirty;

        // The song playing now, and for how long without a break, to count it as a play once
        string? runSong;
        double runSeconds;
        bool runCounted;

        public ListeningStats(string folder, Func<DateTime>? clock = null)
        {
            path = Path.Combine(folder, "listening.json");
            coverFolder = Path.Combine(folder, "covers");
            this.clock = clock ?? (() => DateTime.Now);
            Load();
        }

        static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

        string Oldest => Key(clock().AddDays(1 - DaysKept));

        /// <summary>
        /// Whether a track is a song, and so counts in these stats. The Spotify and Apple Music apps
        /// count when they name an artist, which their ads don't. Anything else has to look like
        /// music: an album, or an artist with a real square cover. Videos on YouTube come with a wide
        /// thumbnail, and clips on Instagram or X with no artist at all.
        /// </summary>
        public static bool Counts(TrackInfo track)
        {
            // An empty artist is filled in with the player's name for the island
            string artist = track.Artist == track.SourceName ? "" : track.Artist;

            if (track.Source is MediaSource.Spotify or MediaSource.AppleMusic)
                return !string.IsNullOrWhiteSpace(artist) && track.Title != MediaController.UnknownTitle;

            bool squareCover = track.Artwork != null && ArtworkService.IsSquare(track.Artwork) && !ArtworkService.LooksLikeIcon(track.Artwork);
            return BrowserPreviews.IsMusic(artist, track.Album, squareCover);
        }

        public void Add(DateTime when, string artist, string title, TimeSpan played, string album = "", BitmapSource? cover = null)
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
            album = album.Trim();

            if (artist.Length > 0)
                day.Artists[artist] = day.Artists.GetValueOrDefault(artist) + seconds;

            if (album.Length > 0)
            {
                string albumKey = artist.Length > 0 ? album + Separator + artist : album;
                day.Albums[albumKey] = day.Albums.GetValueOrDefault(albumKey) + seconds;
            }

            if (title.Length > 0)
            {
                string song = artist.Length > 0 ? title + Separator + artist : title;
                day.Songs[song] = day.Songs.GetValueOrDefault(song) + seconds;

                CountPlay(day, song, seconds);
                Remember(song, title, artist, album, cover);
            }

            dirty = true;

            // Written now and then rather than every second; on quitting too
            if (DateTime.UtcNow - lastSaved > TimeSpan.FromMinutes(1))
                Save();
        }

        // A play is counted once a song has gone on for half a minute without another in between
        void CountPlay(Day day, string song, double seconds)
        {
            if (song != runSong)
            {
                runSong = song;
                runSeconds = 0;
                runCounted = false;
            }

            runSeconds += seconds;

            if (!runCounted && runSeconds >= PlayAfter.TotalSeconds)
            {
                runCounted = true;
                day.Plays[song] = day.Plays.GetValueOrDefault(song) + 1;
            }
        }

        // The song's album, and a small copy of its cover, shared by every song on the album
        void Remember(string song, string title, string artist, string album, BitmapSource? cover)
        {
            if (!store.Songs.TryGetValue(song, out var known))
                store.Songs[song] = known = new Song();

            if (album.Length > 0)
                known.Album = album;

            if (cover == null || (known.Cover != null && File.Exists(Path.Combine(coverFolder, known.Cover))))
                return;

            string name = Hash(album.Length > 0 ? $"{album}|{artist}" : $"{title}|{artist}") + ".jpg";
            string file = Path.Combine(coverFolder, name);

            try
            {
                if (!File.Exists(file))
                {
                    Directory.CreateDirectory(coverFolder);
                    SaveCover(cover, file);
                }

                known.Cover = name;
            }
            catch (Exception ex)
            {
                Log.Error("Saving a cover for the listening stats failed", ex);
            }
        }

        static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant())))[..20].ToLowerInvariant();

        static void SaveCover(BitmapSource cover, string file)
        {
            double scale = Math.Min(1, (double)CoverSize / Math.Max(cover.PixelWidth, cover.PixelHeight));
            BitmapSource small = scale < 1 ? new TransformedBitmap(cover, new ScaleTransform(scale, scale)) : cover;

            var jpeg = new JpegBitmapEncoder { QualityLevel = 88 };
            jpeg.Frames.Add(BitmapFrame.Create(small));

            using var stream = File.Create(file);
            jpeg.Save(stream);
        }

        string? CoverOf(string song) =>
            store.Songs.TryGetValue(song, out var known) && known.Cover != null && File.Exists(Path.Combine(coverFolder, known.Cover))
                ? Path.Combine(coverFolder, known.Cover)
                : null;

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
            var albums = new Dictionary<string, double>();
            var plays = new Dictionary<string, int>();
            var hours = new double[24];

            for (var date = first.Date; date <= last.Date; date = date.AddDays(1))
            {
                if (!store.Days.TryGetValue(Key(date), out var day))
                    continue;

                seconds += day.Seconds;
                Merge(songs, day.Songs);
                Merge(artists, day.Artists);
                Merge(albums, day.Albums ?? new());

                foreach (var (song, count) in day.Plays ?? new())
                    plays[song] = plays.GetValueOrDefault(song) + count;

                if (day.Hours != null)
                    for (int hour = 0; hour < 24 && hour < day.Hours.Length; hour++)
                        hours[hour] += day.Hours[hour];
            }

            var songList = Ranking(songs).Select(s =>
            {
                var (name, artist) = Split(s.Key);
                return new Ranked(name, artist, TimeSpan.FromSeconds(s.Value))
                {
                    Plays = plays.GetValueOrDefault(s.Key),
                    Cover = CoverOf(s.Key)
                };
            }).ToList();

            // An artist's picture, and an album's cover, is that of their most played song that has one
            string? CoverFor(Func<Ranked, bool> matches) =>
                songList.FirstOrDefault(s => s.Cover != null && matches(s))?.Cover;

            var artistList = Ranking(artists).Select(a => new Ranked(a.Key, null, TimeSpan.FromSeconds(a.Value))
            {
                Plays = songList.Where(s => s.Artist == a.Key).Sum(s => s.Plays),
                Cover = CoverFor(s => s.Artist == a.Key)
            }).ToList();

            var albumList = Ranking(albums).Select(a =>
            {
                var (name, artist) = Split(a.Key);
                return new Ranked(name, artist, TimeSpan.FromSeconds(a.Value))
                {
                    Cover = CoverFor(s => s.Artist == artist && AlbumOf(s) == name)
                };
            }).ToList();

            return new ListeningPeriod(TimeSpan.FromSeconds(seconds), songList, artistList, hours.Select(TimeSpan.FromSeconds).ToList())
            {
                Albums = albumList
            };
        }

        string AlbumOf(Ranked song) =>
            store.Songs.TryGetValue(song.Artist == null ? song.Name : song.Name + Separator + song.Artist, out var known) ? known.Album : "";

        static void Merge(Dictionary<string, double> into, Dictionary<string, double> from)
        {
            foreach (var (key, s) in from)
                into[key] = into.GetValueOrDefault(key) + s;
        }

        // "Title — Artist" back into its title and artist
        static (string Name, string? Artist) Split(string key)
        {
            int split = key.LastIndexOf(Separator, StringComparison.Ordinal);
            return split < 0 ? (key, null) : (key[..split], key[(split + Separator.Length)..]);
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

            // Days before the last week aren't shown anywhere, and nor are their songs' covers
            string oldest = Oldest;

            foreach (var key in store.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
                store.Days.Remove(key);

            var stillPlayed = store.Days.Values.SelectMany(d => d.Songs.Keys).ToHashSet();

            foreach (var song in store.Songs.Keys.Where(s => !stillPlayed.Contains(s)).ToList())
                store.Songs.Remove(song);

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

            ForgetCovers();
        }

        // Covers no song in the last week uses any more
        void ForgetCovers()
        {
            try
            {
                if (!Directory.Exists(coverFolder))
                    return;

                var used = store.Songs.Values.Select(s => s.Cover).Where(c => c != null).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(coverFolder, "*.jpg"))
                    if (!used.Contains(Path.GetFileName(file)))
                        File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.Error("Tidying the listening stats' covers failed", ex);
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
