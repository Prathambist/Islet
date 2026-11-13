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

    public enum RankedKind { Song, Artist, Album }

    /// <summary>
    /// A song, artist or album in a top list, with how long it played. Artist is null for an
    /// artist's own entry. Cover is a saved picture of its album, when there is one.
    /// </summary>
    public sealed record Ranked(string Name, string? Artist, TimeSpan Time)
    {
        public RankedKind Kind { get; init; }

        /// <summary>How many times it was played, counting a play once it has gone on for half a minute.</summary>
        public int Plays { get; init; }

        /// <summary>The full path of its cover, saved on this PC, or null.</summary>
        public string? Cover { get; init; }

        /// <summary>A song's album, when it named one.</summary>
        public string? Album { get; init; }

        /// <summary>How often a song was switched away from in its first half minute.</summary>
        public int Skips { get; init; }

        /// <summary>The most times a song was played in a row.</summary>
        public int Streak { get; init; }

        /// <summary>Heard for the first time in this period.</summary>
        public bool IsNew { get; init; }

        /// <summary>The day it was first heard, as far back as Islet knows.</summary>
        public DateTime? FirstHeard { get; init; }
    }

    /// <summary>
    /// Everything that played over one or more days: the total, every song, artist and album
    /// longest first, how it spread over the hours of the day and the players it came from, and
    /// the longest stretch of music without a break.
    /// </summary>
    public sealed record ListeningPeriod(
        TimeSpan Total,
        IReadOnlyList<Ranked> Songs,
        IReadOnlyList<Ranked> Artists,
        IReadOnlyList<TimeSpan> Hours)
    {
        public IReadOnlyList<Ranked> Albums { get; init; } = Array.Empty<Ranked>();

        /// <summary>Time by player ("Spotify", "Apple Music", "Chrome"…), longest first.</summary>
        public IReadOnlyList<(string Source, TimeSpan Time)> Sources { get; init; } = Array.Empty<(string, TimeSpan)>();

        public TimeSpan LongestSession { get; init; }
        public DateTime? LongestSessionStart { get; init; }

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
    /// Keeps count of how long songs actually play each day, by song, artist, album, hour and
    /// player, with plays, skips, repeats and listening sessions, and a small copy of each album's
    /// cover, for the daily summary and the Listening page. Only playing time counts, and only
    /// songs: not videos or other media. Five weeks are kept in full; beyond that only the names
    /// of what's been heard, and when first, so something new can be told apart. All of it in its
    /// own small file and a folder of covers, and nowhere else: none of it leaves the PC.
    /// </summary>
    public sealed class ListeningStats
    {
        /// <summary>Five weeks: enough for the last month, and this week against last.</summary>
        public const int DaysKept = 35;

        /// <summary>How long a song plays before it counts as a play.</summary>
        public static readonly TimeSpan PlayAfter = TimeSpan.FromSeconds(30);

        /// <summary>A pause longer than this ends a listening session.</summary>
        public static readonly TimeSpan SessionBreak = TimeSpan.FromMinutes(5);

        // Switched away from after less than this isn't a skip, just flicking past
        static readonly TimeSpan SkipAtLeast = TimeSpan.FromSeconds(3);

        // Only this many songs' first-heard dates are kept, the most recent
        const int FirstHeardKept = 5000;

        const string Separator = " — ";
        const int CoverSize = 192;

        sealed class Day
        {
            public double Seconds { get; set; }
            public Dictionary<string, double> Artists { get; set; } = new();
            public Dictionary<string, double> Songs { get; set; } = new();
            public Dictionary<string, double> Albums { get; set; } = new();
            public Dictionary<string, double> Sources { get; set; } = new();
            public Dictionary<string, int> Plays { get; set; } = new();
            public Dictionary<string, int> Skips { get; set; } = new();
            public Dictionary<string, int> Streaks { get; set; } = new();
            public double LongestSession { get; set; }
            public DateTime? LongestSessionStart { get; set; }

            // Missing from days counted before hours were
            public double[]? Hours { get; set; }
        }

        // What's known of a song beyond its name: its album, and the cover saved for it
        sealed class Song
        {
            public string Album { get; set; } = "";
            public string? Cover { get; set; }

            /// <summary>When a cover was last looked for online and not found, so it isn't asked for again soon.</summary>
            public string? NotFoundOn { get; set; }
        }

        /// <summary>How long before a cover that wasn't found online is looked for again.</summary>
        public static readonly TimeSpan LookAgainAfter = TimeSpan.FromDays(7);

        sealed class Store
        {
            public Dictionary<string, Day> Days { get; set; } = new();
            public Dictionary<string, Song> Songs { get; set; } = new();

            // Day first heard, by song and by artist, kept longer than the days themselves
            public Dictionary<string, string> FirstHeard { get; set; } = new();
            public Dictionary<string, string> FirstHeardArtists { get; set; } = new();

            /// <summary>The first day counted, so "new" means new and not just the start of counting.</summary>
            public string? CountingSince { get; set; }

            /// <summary>The last day the evening summary was shown, so it's shown once.</summary>
            public string? SummaryShownOn { get; set; }
        }

        readonly string path;
        readonly string coverFolder;
        readonly Func<DateTime> clock;
        Store store = new();
        DateTime lastSaved = DateTime.MinValue;
        bool dirty;

        // The song playing now, and for how long without a break, to count plays and skips
        string? runSong;
        double runSeconds;
        bool runCounted;
        TimeSpan? lastPosition;

        // The song counted as played last, and how many times in a row
        string? streakSong;
        int streakCount;

        // The listening session going on now
        DateTime? sessionStart;
        DateTime sessionLast;
        double sessionSeconds;

        public ListeningStats(string folder, Func<DateTime>? clock = null)
        {
            path = Path.Combine(folder, "listening.json");
            coverFolder = Path.Combine(folder, "covers");
            this.clock = clock ?? (() => DateTime.Now);
            Load();
        }

        static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

        static DateTime? FromKey(string? key) =>
            DateTime.TryParseExact(key, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day) ? day : null;

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

        /// <summary>
        /// Counts <paramref name="played"/> of a song. <paramref name="position"/> is where in the
        /// song it is, which tells a song starting over (on repeat) from it carrying on.
        /// </summary>
        public void Add(DateTime when, string artist, string title, TimeSpan played, string album = "",
            BitmapSource? cover = null, string source = "", TimeSpan? position = null)
        {
            if (played <= TimeSpan.Zero)
                return;

            var day = DayOf(when);
            double seconds = played.TotalSeconds;
            day.Seconds += seconds;

            day.Hours ??= new double[24];
            day.Hours[when.Hour] += seconds;

            artist = artist.Trim();
            title = title.Trim();
            album = album.Trim();
            source = source.Trim();

            store.CountingSince ??= Key(when);

            if (source.Length > 0)
                day.Sources[source] = day.Sources.GetValueOrDefault(source) + seconds;

            if (artist.Length > 0)
            {
                day.Artists[artist] = day.Artists.GetValueOrDefault(artist) + seconds;
                store.FirstHeardArtists.TryAdd(artist, Key(when));
            }

            if (album.Length > 0)
            {
                string albumKey = artist.Length > 0 ? album + Separator + artist : album;
                day.Albums[albumKey] = day.Albums.GetValueOrDefault(albumKey) + seconds;
            }

            if (title.Length > 0)
            {
                string song = SongKey(title, artist);
                day.Songs[song] = day.Songs.GetValueOrDefault(song) + seconds;
                store.FirstHeard.TryAdd(song, Key(when));

                CountPlay(when, day, song, seconds, position);

                // Right as a song changes the player can still be showing the last one's cover,
                // so it's only kept once the song has been going a few seconds
                Remember(song, title, artist, album, runSeconds >= 4 ? cover : null);
            }

            CountSession(when, seconds);
            dirty = true;

            // Written now and then rather than every second; on quitting too
            if (DateTime.UtcNow - lastSaved > TimeSpan.FromMinutes(1))
                Save();
        }

        Day DayOf(DateTime when)
        {
            string key = Key(when);

            if (!store.Days.TryGetValue(key, out var day))
                store.Days[key] = day = new Day();

            return day;
        }

        static string SongKey(string title, string? artist) =>
            string.IsNullOrEmpty(artist) ? title : title + Separator + artist;

        // A play is counted once a song has gone on for half a minute. Switching away sooner is a
        // skip; starting the same song over is another play, and the plays in a row are its streak.
        void CountPlay(DateTime when, Day day, string song, double seconds, TimeSpan? position)
        {
            bool startedOver = song == runSong && position is TimeSpan now && lastPosition is TimeSpan before &&
                               now < before - TimeSpan.FromSeconds(10) && now < TimeSpan.FromSeconds(15);

            if (song != runSong || startedOver)
            {
                if (runSong != null && song != runSong && !runCounted && runSeconds >= SkipAtLeast.TotalSeconds)
                {
                    var skippedOn = DayOf(when);
                    skippedOn.Skips[runSong] = skippedOn.Skips.GetValueOrDefault(runSong) + 1;
                }

                runSong = song;
                runSeconds = 0;
                runCounted = false;
            }

            lastPosition = position;
            runSeconds += seconds;

            if (runCounted || runSeconds < PlayAfter.TotalSeconds)
                return;

            runCounted = true;
            day.Plays[song] = day.Plays.GetValueOrDefault(song) + 1;

            if (song == streakSong)
                streakCount++;
            else
            {
                streakSong = song;
                streakCount = 1;
            }

            day.Streaks[song] = Math.Max(day.Streaks.GetValueOrDefault(song), streakCount);
        }

        // Music with no pause longer than a few minutes is one session, kept on the day it began
        void CountSession(DateTime when, double seconds)
        {
            if (sessionStart == null || when - sessionLast > SessionBreak)
            {
                sessionStart = when.AddSeconds(-seconds);
                sessionSeconds = 0;
            }

            sessionLast = when;
            sessionSeconds += seconds;

            var day = DayOf(sessionStart.Value);

            if (sessionSeconds > day.LongestSession)
            {
                day.LongestSession = sessionSeconds;
                day.LongestSessionStart = sessionStart;
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

        /// <summary>
        /// Songs played from <paramref name="first"/> to <paramref name="last"/> that have no cover
        /// yet (played before covers were kept, or the player had none), longest played first,
        /// leaving out those looked for lately and not found.
        /// </summary>
        public IReadOnlyList<(string Title, string? Artist)> MissingCovers(DateTime first, DateTime last)
        {
            var songs = new Dictionary<string, double>();

            foreach (var day in DaysBetween(first, last))
                Merge(songs, day.Songs);

            string retryBefore = Key(clock() - LookAgainAfter);

            return Ranking(songs)
                .Where(s => CoverOf(s.Key) == null)
                .Where(s => !store.Songs.TryGetValue(s.Key, out var known) || known.NotFoundOn == null ||
                            string.CompareOrdinal(known.NotFoundOn, retryBefore) <= 0)
                .Select(s => Split(s.Key))
                .ToList();
        }

        /// <summary>A cover found for a song some other way, e.g. looked up online; null if none was found.</summary>
        public void SetCover(string title, string? artist, BitmapSource? cover)
        {
            string song = SongKey(title, artist);

            if (!store.Songs.TryGetValue(song, out var known))
                store.Songs[song] = known = new Song();

            if (cover == null)
                known.NotFoundOn = Key(clock());
            else
            {
                known.NotFoundOn = null;
                Remember(song, title, artist ?? "", known.Album, cover);
            }

            dirty = true;
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

        string AlbumOf(string song) => store.Songs.TryGetValue(song, out var known) ? known.Album : "";

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

        IEnumerable<Day> DaysBetween(DateTime first, DateTime last)
        {
            for (var date = first.Date; date <= last.Date; date = date.AddDays(1))
                if (store.Days.TryGetValue(Key(date), out var day))
                    yield return day;
        }

        /// <summary>Everything that played from <paramref name="first"/> to <paramref name="last"/>, both days included.</summary>
        public ListeningPeriod Period(DateTime first, DateTime last)
        {
            double seconds = 0;
            var songs = new Dictionary<string, double>();
            var artists = new Dictionary<string, double>();
            var albums = new Dictionary<string, double>();
            var sources = new Dictionary<string, double>();
            var plays = new Dictionary<string, int>();
            var skips = new Dictionary<string, int>();
            var streaks = new Dictionary<string, int>();
            var hours = new double[24];
            double longest = 0;
            DateTime? longestStart = null;

            foreach (var day in DaysBetween(first, last))
            {
                seconds += day.Seconds;
                Merge(songs, day.Songs);
                Merge(artists, day.Artists);
                Merge(albums, day.Albums);
                Merge(sources, day.Sources);
                AddCounts(plays, day.Plays);
                AddCounts(skips, day.Skips);

                foreach (var (song, streak) in day.Streaks)
                    streaks[song] = Math.Max(streaks.GetValueOrDefault(song), streak);

                if (day.Hours != null)
                    for (int hour = 0; hour < 24 && hour < day.Hours.Length; hour++)
                        hours[hour] += day.Hours[hour];

                if (day.LongestSession > longest)
                {
                    longest = day.LongestSession;
                    longestStart = day.LongestSessionStart;
                }
            }

            // "New" only once counting began before the period did, or everything would be
            bool knowsWhatsNew = store.CountingSince != null && string.CompareOrdinal(store.CountingSince, Key(first)) < 0;
            bool IsNew(string? heard) => knowsWhatsNew && heard != null &&
                string.CompareOrdinal(heard, Key(first)) >= 0 && string.CompareOrdinal(heard, Key(last)) <= 0;

            var songList = Ranking(songs).Select(s =>
            {
                var (name, artist) = Split(s.Key);
                string? heard = store.FirstHeard.GetValueOrDefault(s.Key);
                string album = AlbumOf(s.Key);

                return new Ranked(name, artist, TimeSpan.FromSeconds(s.Value))
                {
                    Kind = RankedKind.Song,
                    Plays = plays.GetValueOrDefault(s.Key),
                    Skips = skips.GetValueOrDefault(s.Key),
                    Streak = streaks.GetValueOrDefault(s.Key),
                    Cover = CoverOf(s.Key),
                    Album = album.Length > 0 ? album : null,
                    IsNew = IsNew(heard),
                    FirstHeard = FromKey(heard)
                };
            }).ToList();

            // An artist's picture, and an album's cover, is that of their most played song that has one
            string? CoverFor(Func<Ranked, bool> matches) =>
                songList.FirstOrDefault(s => s.Cover != null && matches(s))?.Cover;

            var artistList = Ranking(artists).Select(a =>
            {
                string? heard = store.FirstHeardArtists.GetValueOrDefault(a.Key);

                return new Ranked(a.Key, null, TimeSpan.FromSeconds(a.Value))
                {
                    Kind = RankedKind.Artist,
                    Plays = songList.Where(s => s.Artist == a.Key).Sum(s => s.Plays),
                    Cover = CoverFor(s => s.Artist == a.Key),
                    IsNew = IsNew(heard),
                    FirstHeard = FromKey(heard)
                };
            }).ToList();

            var albumList = Ranking(albums).Select(a =>
            {
                var (name, artist) = Split(a.Key);

                return new Ranked(name, artist, TimeSpan.FromSeconds(a.Value))
                {
                    Kind = RankedKind.Album,
                    Plays = songList.Where(s => s.Artist == artist && s.Album == name).Sum(s => s.Plays),
                    Cover = CoverFor(s => s.Artist == artist && s.Album == name)
                };
            }).ToList();

            return new ListeningPeriod(TimeSpan.FromSeconds(seconds), songList, artistList, hours.Select(TimeSpan.FromSeconds).ToList())
            {
                Albums = albumList,
                Sources = Ranking(sources).Select(s => (s.Key, TimeSpan.FromSeconds(s.Value))).ToList(),
                LongestSession = TimeSpan.FromSeconds(longest),
                LongestSessionStart = longestStart
            };
        }

        /// <summary>
        /// How long a song, artist or album played on each of the <paramref name="days"/> days up to
        /// <paramref name="last"/>, oldest first.
        /// </summary>
        public IReadOnlyList<(DateTime Day, TimeSpan Time)> History(Ranked item, DateTime last, int days)
        {
            string key = item.Kind == RankedKind.Artist ? item.Name : SongKey(item.Name, item.Artist);

            return Enumerable.Range(0, days)
                .Select(i => last.Date.AddDays(i - days + 1))
                .Select(d =>
                {
                    double s = 0;

                    if (store.Days.TryGetValue(Key(d), out var day))
                        s = item.Kind switch
                        {
                            RankedKind.Artist => day.Artists.GetValueOrDefault(key),
                            RankedKind.Album => day.Albums.GetValueOrDefault(key),
                            _ => day.Songs.GetValueOrDefault(key)
                        };

                    return (d, TimeSpan.FromSeconds(s));
                })
                .ToList();
        }

        static void Merge(Dictionary<string, double> into, Dictionary<string, double>? from)
        {
            if (from == null)
                return;

            foreach (var (key, s) in from)
                into[key] = into.GetValueOrDefault(key) + s;
        }

        static void AddCounts(Dictionary<string, int> into, Dictionary<string, int>? from)
        {
            if (from == null)
                return;

            foreach (var (key, n) in from)
                into[key] = into.GetValueOrDefault(key) + n;
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

        /// <summary>Whether there's anything counted from <paramref name="day"/> or before, to compare against.</summary>
        public bool CountedBy(DateTime day) => store.CountingSince != null && string.CompareOrdinal(store.CountingSince, Key(day)) <= 0;

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

            // Days older than five weeks go, and their songs' covers with them
            string oldest = Oldest;

            foreach (var key in store.Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
                store.Days.Remove(key);

            var stillPlayed = store.Days.Values.SelectMany(d => d.Songs.Keys).ToHashSet();

            foreach (var song in store.Songs.Keys.Where(s => !stillPlayed.Contains(s)).ToList())
                store.Songs.Remove(song);

            // First-heard dates are kept for longer, but not without end
            if (store.FirstHeard.Count > FirstHeardKept)
                foreach (var song in store.FirstHeard.OrderBy(f => f.Value, StringComparer.Ordinal)
                             .Take(store.FirstHeard.Count - FirstHeardKept).Select(f => f.Key).ToList())
                    store.FirstHeard.Remove(song);

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

        // Covers no song in the last five weeks uses any more
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

            // Stats from before first-heard dates were kept: what's there was heard on those days
            if (store.CountingSince == null && store.Days.Count > 0)
            {
                foreach (var (key, day) in store.Days.OrderBy(d => d.Key, StringComparer.Ordinal))
                {
                    foreach (var song in day.Songs.Keys)
                        store.FirstHeard.TryAdd(song, key);

                    foreach (var artist in day.Artists.Keys)
                        store.FirstHeardArtists.TryAdd(artist, key);
                }

                store.CountingSince = store.Days.Keys.Min(StringComparer.Ordinal);
            }

            // Anything older than five weeks, left from before, goes on the next save
            string oldest = Oldest;
            dirty = store.Days.Keys.Any(k => string.CompareOrdinal(k, oldest) < 0);
        }
    }
}
