using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Islet
{
    /// <summary>
    /// Finds the real album / single cover for a music video (YouTube etc. only
    /// hand Windows a 16:9 video thumbnail) using the iTunes Search API
    /// (free, no key).
    /// </summary>
    public static class ArtworkService
    {
        static readonly HttpClient http = CreateClient();
        static readonly Dictionary<string, BitmapSource?> cache = new();

        static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Islet/{UpdateService.CurrentVersion}");
            return client;
        }

        // A whole word only: "Daft Punk" has "ft" in it but features no one
        static readonly Regex Featuring = new(@"[\(\[]?\s*\b(feat\.?|ft\.?|featuring)\s.*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <param name="sourceName">Player name; used as the artist when a site gives none.</param>
        public static async Task<BitmapSource?> FindAsync(string artist, string title, string sourceName)
        {
            bool artistKnown = !string.Equals(artist, sourceName, StringComparison.OrdinalIgnoreCase);
            var (cleanArtist, cleanTitle) = LyricsService.Clean(artistKnown ? artist : "", title, fromBrowser: true);

            if (cleanTitle.Length == 0)
                return null;

            string term = (cleanArtist + " " + Featuring.Replace(cleanTitle, "")).Trim();

            if (cache.TryGetValue(term, out var cached))
                return cached;

            BitmapSource? result = null;

            try
            {
                string url = "https://itunes.apple.com/search?media=music&entity=song&limit=10&term=" +
                             Uri.EscapeDataString(term);

                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                string? artUrl = SelectArtworkUrl(doc.RootElement, cleanArtist, cleanTitle);

                if (artUrl != null)
                    result = await DownloadAsync(artUrl);
            }
            catch (Exception ex)
            {
                Log.Error($"Cover lookup failed for '{term}'", ex);
                return null;
            }

            if (cache.Count > 100)
                cache.Clear();

            cache[term] = result;
            return result;
        }

        /// <summary>
        /// A song's cover for the listening stats, tried several ways so as few as possible go
        /// without: the song on Deezer, then on iTunes, then its album by name, then any album by the
        /// same artist. Answered is false when a lookup couldn't be made (no network, or a service
        /// not answering) and nothing was found, so a song isn't taken to have no cover just because
        /// the PC was offline. The title is a player's own (Spotify's "Song - 2010 Remaster"), not a
        /// browser's "Artist - Song".
        /// </summary>
        public static async Task<StatsCover> FindForStatsAsync(string? artist, string title, string? album = null)
        {
            var (cleanArtist, cleanTitle) = LyricsService.Clean(artist ?? "", title, fromBrowser: false);

            // "Song (2011 Mix)", "Song - Live": the song itself is what's looked for
            cleanTitle = Bracketed.Replace(Featuring.Replace(cleanTitle, ""), "").Trim();
            int dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);

            if (dash > 0)
                cleanTitle = cleanTitle[..dash].Trim();

            cleanArtist = Featuring.Replace(cleanArtist, "").Trim();

            if (cleanTitle.Length == 0)
                return new StatsCover(true, null, false);

            string key = $"stats|{cleanArtist}|{cleanTitle}|{album}";

            if (cache.TryGetValue(key, out var cached))
                return new StatsCover(true, cached, cached != null);

            bool unanswered = false;

            async Task<string?> Try(string what, Func<Task<string?>> step)
            {
                try
                {
                    return await step();
                }
                catch (Exception ex)
                {
                    unanswered = true;
                    Log.Info($"Cover lookup ({what}) didn't get an answer for '{cleanArtist} {cleanTitle}': {ex.Message}");
                    return null;
                }
            }

            bool hasAlbum = !string.IsNullOrWhiteSpace(album);

            // The song itself, then its album, then any album by the artist, then the artist's photo
            string? url =
                await Try("Deezer song", () => DeezerSong(cleanArtist, cleanTitle)) ??
                await Try("iTunes song", () => ITunesSong(cleanArtist, cleanTitle)) ??
                (hasAlbum ? await Try("Deezer album", () => DeezerAlbum(cleanArtist, album!)) : null) ??
                (hasAlbum ? await Try("iTunes album", () => ITunesAlbum(cleanArtist, album!)) : null) ??
                await Try("Deezer artist's albums", () => DeezerAnyAlbumBy(cleanArtist)) ??
                await Try("iTunes artist's albums", () => ITunesAnyAlbumBy(cleanArtist)) ??
                await Try("Deezer artist", () => DeezerArtistPicture(cleanArtist));

            if (url == null)
            {
                if (!unanswered)
                {
                    if (cache.Count > 100)
                        cache.Clear();

                    cache[key] = null;
                }

                // Nothing on either catalog, not even the artist: most likely not music at all
                return new StatsCover(!unanswered, null, false);
            }

            try
            {
                // Not kept in memory: the stats save it, and holding every one would add up
                var cover = await DownloadAsync(url);
                return new StatsCover(true, cover, true);
            }
            catch (Exception ex)
            {
                Log.Info($"Downloading a cover for '{cleanArtist} {cleanTitle}' failed: {ex.Message}");
                return new StatsCover(false, null, true);
            }
        }

        /// <summary>A music video, as the song it is: its artist, its title and its album's cover.</summary>
        public sealed record SongFromVideo(string Artist, string Title, BitmapSource Cover)
        {
            /// <summary>A music video's own clip, which may run a few seconds of intro before the song.</summary>
            public bool Clip { get; init; }

            /// <summary>"Sped Up", "Slowed + Reverb", "Nightcore", "8D": a fan's edit of the song.</summary>
            public string? Edit { get; init; }
        }

        static readonly Regex EditTag = new(
            @"\b(sped\s*up|speed\s*up|slowed(\s*(\+|and|&)\s*reverb(ed)?)?|reverb(ed)?|nightcore|daycore|8d(\s*audio)?|bass\s*boosted)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex ClipTitle = new(@"official\s*(music\s*)?video|\bm/?v\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex NotClipTitle = new(@"lyric|audio|visuali[sz]er", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>What kind of edit a video title says it is, in a tidy form; null for the song as released.</summary>
        internal static string? EditOf(string title)
        {
            var match = EditTag.Match(title);

            if (!match.Success)
                return null;

            string tag = match.Value.ToLowerInvariant();

            return tag.StartsWith("sped") || tag.StartsWith("speed") ? "Sped Up"
                : tag.StartsWith("slowed") && tag.Contains("reverb") ? "Slowed + Reverb"
                : tag.StartsWith("slowed") ? "Slowed"
                : tag.StartsWith("reverb") ? "Reverb"
                : tag.StartsWith("nightcore") ? "Nightcore"
                : tag.StartsWith("daycore") ? "Daycore"
                : tag.StartsWith("8d") ? "8D"
                : "Bass Boosted";
        }

        static readonly Dictionary<string, SongFromVideo?> videoSongs = new();

        /// <summary>
        /// The song a music video is, when Deezer or iTunes has that very song by that artist: the
        /// channel and title cleaned up ("TheStrokesVEVO", "The Strokes - Selfless (Official Audio)"
        /// become "The Strokes", "Selfless"). Null if neither has it, or it couldn't be asked; only
        /// a real answer is remembered.
        /// </summary>
        public static async Task<SongFromVideo?> SongFromVideoAsync(string channel, string videoTitle)
        {
            string key = channel + "|" + videoTitle;

            if (videoSongs.TryGetValue(key, out var known))
                return known;

            string? edit = EditOf(videoTitle);
            string written = BrowserPreviews.ArtistDashSong(edit != null ? EditTag.Replace(videoTitle, "") : videoTitle);
            var (artist, title) = LyricsService.Clean(channel, written, fromBrowser: true);
            title = Bracketed.Replace(Featuring.Replace(title, ""), "").Trim(' ', '-', '|', '+');
            artist = Featuring.Replace(artist, "").Trim();

            if (title.Length == 0 || artist.Length == 0)
                return null;

            // Some write "Song - Artist"; that order is tried too, when the title had two halves
            bool twoHalves = BrowserPreviews.ArtistAndSong(written) != null;

            SongFromVideo? song = null;

            try
            {
                string? url = await DeezerSong(artist, title) ?? await ITunesSong(artist, title);

                if (url == null && twoHalves && (await DeezerSong(title, artist) ?? await ITunesSong(title, artist)) is string swapped)
                {
                    url = swapped;
                    (artist, title) = (title, artist);
                }

                if (url != null && await DownloadAsync(url) is BitmapSource cover)
                {
                    cover.Freeze();

                    // An edit is its own entry ("Snooze (Sped Up)"), with the original's cover
                    song = new SongFromVideo(artist, edit != null ? $"{title} ({edit})" : title, cover)
                    {
                        Edit = edit,
                        Clip = !NotClipTitle.IsMatch(videoTitle) &&
                               (ClipTitle.IsMatch(videoTitle) || !channel.TrimEnd().EndsWith("- Topic", StringComparison.OrdinalIgnoreCase))
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Info($"Couldn't check '{videoTitle}' against the music catalogs: {ex.Message}");
                return null;
            }

            if (videoSongs.Count > 100)
                videoSongs.Clear();

            videoSongs[key] = song;
            return song;
        }

        /// <summary>
        /// What a cover lookup for the stats came to. Answered is false when it couldn't be made.
        /// Known is false when neither catalog knows the song or even its artist.
        /// </summary>
        public sealed record StatsCover(bool Answered, BitmapSource? Cover, bool Known);

        static async Task<string?> ITunesAlbum(string artist, string album)
        {
            string name = Bracketed.Replace(album, "").Trim();

            using var doc = JsonDocument.Parse(await http.GetStringAsync(
                "https://itunes.apple.com/search?media=music&entity=album&limit=10&term=" + Uri.EscapeDataString($"{artist} {name}".Trim())));

            foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                string? by = item.TryGetProperty("artistName", out var a) ? a.GetString() : null;
                string? title = item.TryGetProperty("collectionName", out var t) ? t.GetString() : null;

                if (SameArtist(artist, by) && SameTitle(name, title) && item.TryGetProperty("artworkUrl100", out var art))
                    return art.GetString()?.Replace("100x100bb", "600x600bb");
            }

            return null;
        }

        static async Task<string?> ITunesAnyAlbumBy(string artist)
        {
            if (artist.Length == 0)
                return null;

            using var doc = JsonDocument.Parse(await http.GetStringAsync(
                "https://itunes.apple.com/search?media=music&entity=album&limit=10&term=" + Uri.EscapeDataString(artist)));

            foreach (var item in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                string? by = item.TryGetProperty("artistName", out var a) ? a.GetString() : null;

                if (Normalize(by) == Normalize(artist) && item.TryGetProperty("artworkUrl100", out var art))
                    return art.GetString()?.Replace("100x100bb", "600x600bb");
            }

            return null;
        }

        static async Task<string?> DeezerArtistPicture(string artist)
        {
            if (artist.Length == 0)
                return null;

            var response = await DeezerSearch("artist", artist);

            if (!response.TryGetProperty("data", out var data))
                return null;

            foreach (var item in data.EnumerateArray())
            {
                string? name = item.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (Normalize(name) == Normalize(artist) && item.TryGetProperty("picture_big", out var picture) &&
                    picture.GetString() is string url && url.Length > 0 && !url.Contains("/artist//"))
                    return url;
            }

            return null;
        }

        static readonly Regex Bracketed = new(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);

        static bool SameArtist(string want, string? found)
        {
            string a = Normalize(want), b = Normalize(found);
            return a.Length == 0 || (b.Length > 0 && (a.Contains(b) || b.Contains(a)));
        }

        static bool SameTitle(string want, string? found)
        {
            string a = Normalize(want), b = Normalize(Bracketed.Replace(found ?? "", ""));
            return a.Length > 0 && b.Length > 0 && (a == b || a.StartsWith(b) || b.StartsWith(a));
        }

        static async Task<JsonElement> DeezerSearch(string kind, string query)
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(
                $"https://api.deezer.com/search{(kind.Length > 0 ? "/" + kind : "")}?limit=10&q=" + Uri.EscapeDataString(query)));

            // Deezer reports its own errors (such as being asked too often) inside a normal response
            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new HttpRequestException("Deezer: " + error);

            return doc.RootElement.Clone();
        }

        static string? DeezerCover(JsonElement album) =>
            album.TryGetProperty("cover_big", out var big) && big.GetString() is string url && url.Length > 0 ? url
            : album.TryGetProperty("cover_medium", out var medium) ? medium.GetString()
            : null;

        /// <summary>The cover of the album a song is on, from Deezer's results for it.</summary>
        internal static string? DeezerSongCover(JsonElement response, string artist, string title)
        {
            if (!response.TryGetProperty("data", out var data))
                return null;

            foreach (var item in data.EnumerateArray())
            {
                string? name = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                string? by = item.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (SameTitle(title, name) && SameArtist(artist, by) && item.TryGetProperty("album", out var album) && DeezerCover(album) is string url)
                    return url;
            }

            return null;
        }

        static async Task<string?> DeezerSong(string artist, string title)
        {
            string strict = artist.Length > 0 ? $"artist:\"{artist}\" track:\"{title}\"" : $"track:\"{title}\"";
            var found = DeezerSongCover(await DeezerSearch("", strict), artist, title);

            // The strict search misses some spellings; a plain one finds them
            return found ?? DeezerSongCover(await DeezerSearch("", $"{artist} {title}".Trim()), artist, title);
        }

        static async Task<string?> ITunesSong(string artist, string title)
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(
                "https://itunes.apple.com/search?media=music&entity=song&limit=10&term=" + Uri.EscapeDataString($"{artist} {title}".Trim())));

            return SelectArtworkUrl(doc.RootElement, artist, title);
        }

        static async Task<string?> DeezerAlbum(string artist, string album)
        {
            string name = Bracketed.Replace(album, "").Trim();
            var response = await DeezerSearch("album", $"{artist} {name}".Trim());

            if (!response.TryGetProperty("data", out var data))
                return null;

            foreach (var item in data.EnumerateArray())
            {
                string? by = item.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? title = item.TryGetProperty("title", out var t) ? t.GetString() : null;

                if (SameArtist(artist, by) && SameTitle(name, title) && DeezerCover(item) is string url)
                    return url;
            }

            return null;
        }

        // The last try: an album by the same artist, so at least the cover is theirs
        static async Task<string?> DeezerAnyAlbumBy(string artist)
        {
            if (artist.Length == 0)
                return null;

            var response = await DeezerSearch("", $"artist:\"{artist}\"");

            if (!response.TryGetProperty("data", out var data))
                return null;

            foreach (var item in data.EnumerateArray())
            {
                string? by = item.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var n) ? n.GetString() : null;

                if (Normalize(by) == Normalize(artist) && item.TryGetProperty("album", out var album) && DeezerCover(album) is string url)
                    return url;
            }

            return null;
        }

        /// <summary>The 600px cover of the first search result that is the same song.</summary>
        internal static string? SelectArtworkUrl(JsonElement response, string artist, string title)
        {
            string wantTitle = Normalize(title);
            string wantArtist = Normalize(artist);

            if (wantTitle.Length == 0 || !response.TryGetProperty("results", out var results))
                return null;

            foreach (var item in results.EnumerateArray())
            {
                string track = Normalize(item.TryGetProperty("trackName", out var t) ? t.GetString() : null);
                string by = Normalize(item.TryGetProperty("artistName", out var a) ? a.GetString() : null);

                if (track.Length == 0 || !item.TryGetProperty("artworkUrl100", out var art))
                    continue;

                bool titleMatches = track == wantTitle ||
                                    track.StartsWith(wantTitle) || wantTitle.StartsWith(track);

                // Without a reliable artist, only an exact title is good enough
                bool artistMatches = wantArtist.Length > 0
                    ? by.Length > 0 && (by.Contains(wantArtist) || wantArtist.Contains(by))
                    : track == wantTitle;

                if (titleMatches && artistMatches)
                {
                    // 100px by default; Apple serves larger sizes from the same path
                    return art.GetString()?.Replace("100x100bb", "600x600bb");
                }
            }

            return null;
        }

        static async Task<BitmapSource?> DownloadAsync(string url)
        {
            byte[] bytes = await http.GetByteArrayAsync(url);
            return await Task.Run(() => MediaController.DecodeImage(bytes));
        }

        // "Selfless (feat. X)" and "selfless" compare equal
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            text = Featuring.Replace(text, "");
            var builder = new StringBuilder(text.Length);

            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        /// <summary>
        /// App logos (what Windows shows when a site sends no artwork) are small or
        /// have transparent corners; album art and video frames are opaque.
        /// </summary>
        public static bool LooksLikeIcon(BitmapSource image)
        {
            if (image.PixelWidth < 64 || image.PixelHeight < 64)
                return true;

            try
            {
                var pixels = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = pixels.PixelWidth, h = pixels.PixelHeight;
                var alpha = new byte[4];

                foreach (var (x, y) in new[] { (1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2) })
                {
                    pixels.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), alpha, 4, 0);

                    if (alpha[3] < 250)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>True for square-ish art (album covers), false for video frames.</summary>
        public static bool IsSquare(BitmapSource image) =>
            image.PixelHeight > 0 &&
            Math.Abs((double)image.PixelWidth / image.PixelHeight - 1) < 0.1;
    }
}
