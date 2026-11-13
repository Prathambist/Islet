using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Islet
{
    /// <summary>
    /// Keeps videos that only play in passing off the island: a YouTube thumbnail previewing under
    /// the pointer, clips autoplaying in an Instagram or X feed, and the like. Browsers report these
    /// to Windows like anything else, without saying which site or why, so it goes by how they behave.
    ///
    /// Music from the web (Spotify Web, YouTube Music) names an album and is never held back. Any
    /// other browser media has to keep playing for a few seconds first, and if its title isn't in
    /// the browser's window title, so it isn't the page you're on, for longer still: a preview or
    /// a feed clip rarely plays that long, and a video you've opened is named in its tab.
    /// </summary>
    public static class BrowserPreviews
    {
        /// <summary>How long any video from the web plays before the island shows it.</summary>
        public static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

        /// <summary>How long one that isn't the page you're on plays first, e.g. in a background tab.</summary>
        public static readonly TimeSpan NotOnScreen = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether what a browser plays is music rather than a video: it names an album (Spotify
        /// Web, YouTube Music), or an artist with a square cover (SoundCloud and the like). Videos
        /// come with a wide thumbnail, and Instagram or X clips with no artist at all.
        /// </summary>
        public static bool IsMusic(string artist, string album, bool squareCover) =>
            !string.IsNullOrWhiteSpace(album) || (!string.IsNullOrWhiteSpace(artist) && squareCover);

        static readonly System.Text.RegularExpressions.Regex MusicVideoTitle = new(
            @"official\s*(music\s*)?(video|audio|visuali[sz]er|lyric(s)?\s*video|mv)|lyric(s)?\s*video|[\(\[]\s*(lyrics?|audio|visuali[sz]er|m/?v)\s*[\)\]]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Whether a video is, by its own account, a song: an official music video, audio, lyric
        /// video or visualizer, or an upload by an artist's VEVO channel or YouTube's
        /// "Artist - Topic" channels of album tracks. Whether it really is one is checked against
        /// the music catalogs before it's shown as one.
        /// </summary>
        public static bool LooksLikeMusicVideo(string channel, string title) =>
            !string.IsNullOrWhiteSpace(title) &&
            (MusicVideoTitle.IsMatch(title) ||
             channel.TrimEnd().EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) ||
             channel.TrimEnd().EndsWith("- Topic", StringComparison.OrdinalIgnoreCase) ||
             ArtistsOwnChannel(channel, title) ||
             (CuratorChannel(channel) && ArtistAndSong(title) != null));

        // Channels that post other artists' official songs: record labels, live-session series
        // and well-known music and lyric channels. A video of theirs still has to name an artist
        // and song the catalogs know.
        static readonly System.Text.RegularExpressions.Regex Curator = new(
            @"(records|recordings|music\s*group|labels?|lyrics?)$|^(colors|colorsxstudios|lyrical\s*lemonade|7clouds|trap\s*nation|chill\s*nation|mrsuicidesheep|majestic\s*casual|proximity|the\s*vibe\s*guide|monstercat.*|nocopyrightsounds|spinnin.*|mahogany(\s*sessions)?|sofar\s*sounds|la\s*blogoth[eè]que|audiotree(\s*live)?|kexp|tiny\s*desk|npr\s*music|genius)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        static bool CuratorChannel(string channel) => Curator.IsMatch(channel.Trim());

        static readonly System.Text.RegularExpressions.Regex SongByArtist = new(
            @"^(?<song>.+?)\s+by\s+(?<artist>[^\(\[|]+?)(?<rest>\s*[\(\[|].*)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A video title in the usual "Artist - Song" form, from the other ways they're written:
        /// "Artist – Song" and "Artist — Song", "Artist | Song", "Artist: Song", "Song by Artist",
        /// and with trailing extras like "| A COLORS SHOW" dropped.
        /// </summary>
        public static string ArtistDashSong(string title)
        {
            string t = title.Replace(" – ", " - ").Replace(" — ", " - ").Replace(" ― ", " - ").Trim();
            int dash = t.IndexOf(" - ", StringComparison.Ordinal);
            int bar = t.IndexOf(" | ", StringComparison.Ordinal);

            // "Artist - Song | A COLORS SHOW": what's after the bar is about the video
            if (dash > 0 && bar > dash)
                return t[..bar].Trim();

            if (dash > 0)
                return t;

            // "Artist | Song | Official Video"
            if (bar > 0)
            {
                string rest = t[(bar + 3)..];
                int more = rest.IndexOf(" | ", StringComparison.Ordinal);
                return t[..bar].Trim() + " - " + (more > 0 ? rest[..more] : rest).Trim();
            }

            // "Artist: Song", when what's before the colon is short enough to be a name
            int colon = t.IndexOf(": ", StringComparison.Ordinal);

            if (colon > 0 && colon <= 40)
                return t[..colon].Trim() + " - " + t[(colon + 2)..].Trim();

            // "Song by Artist (Official Video)"
            var by = SongByArtist.Match(t);

            if (by.Success)
                return by.Groups["artist"].Value.Trim() + " - " + by.Groups["song"].Value.Trim() + by.Groups["rest"].Value;

            return t;
        }

        /// <summary>The artist and song a video title names, or null when it names no artist.</summary>
        public static (string Artist, string Song)? ArtistAndSong(string title)
        {
            string t = ArtistDashSong(title);
            int dash = t.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? (t[..dash].Trim(), t[(dash + 3)..].Trim()) : null;
        }

        static readonly System.Text.RegularExpressions.Regex MixTitle = new(
            @"full\s*album|album\s*stream|\bmix\b|mixtape|playlist|compilation|non-?stop|megamix|greatest\s*hits|best\s*of|\bsongs\b|\bhits\b|\b(live\s*)?(set|concert)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A full album, mix, playlist video or live set: long, and saying so in its title, or long
        /// and from a music channel. Shown on the island as what it is, but never counted as a
        /// single song ("Kid A (Full Album)" isn't fifty minutes of the song "Kid A"). A short
        /// "Radio Mix" of one song is still that song.
        /// </summary>
        public static bool LooksLikeMix(string channel, string title, TimeSpan length) =>
            !string.IsNullOrWhiteSpace(title) &&
            ((length > TimeSpan.FromMinutes(10) && MixTitle.IsMatch(title)) ||
             (length > TimeSpan.FromMinutes(15) && LooksLikeMusicVideo(channel, title)));

        // "Radiohead" posting "Radiohead - Jigsaw Falling Into Place": the artist's own channel
        // ("Radiohead Official", "radioheadmusic" too) putting up one of their songs
        static bool ArtistsOwnChannel(string channel, string title)
        {
            if (ArtistAndSong(title) is not var (left, right))
                return false;

            string owner = ArtworkService.Normalize(System.Text.RegularExpressions.Regex.Replace(
                channel, @"(official|music|vevo|channel|tv)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

            // "Artist ft. Other", "Artist & Other", "Artist, Other", "Artist x Other": any of them,
            // on either side ("Song - Artist" is written too)
            bool Names(string artists) => System.Text.RegularExpressions.Regex
                .Split(artists, @"\s*(?:,|&|\bx\b|\band\b|\bwith\b|\bfeat\.?|\bft\.?|\bfeaturing\b)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Any(name => ArtworkService.Normalize(name) == owner);

            return owner.Length >= 2 && (Names(left) || Names(right));
        }

        /// <summary>
        /// Whether to keep this player off the island for now. <paramref name="playingFor"/> is how
        /// long it has played without stopping, or null while it isn't playing.
        /// </summary>
        public static bool Holds(MediaSource source, string album, bool accepted, TimeSpan? playingFor, bool titleOnScreen, bool knownSong = false)
        {
            if (source != MediaSource.Browser || accepted || !string.IsNullOrWhiteSpace(album))
                return false;

            // A preview that stopped when the pointer moved on isn't left behind as paused media
            if (playingFor is not TimeSpan played)
                return true;

            // A music video the catalogs know, playing in the tab in front: no need to wait and see
            if (knownSong && titleOnScreen)
                return false;

            return played < Settle || (!titleOnScreen && played < NotOnScreen);
        }

        /// <summary>When a held player that keeps playing would next be looked at again, from when it started.</summary>
        public static TimeSpan NextLook(TimeSpan playingFor, bool titleOnScreen) =>
            playingFor < Settle ? Settle - playingFor
            : !titleOnScreen && playingFor < NotOnScreen ? NotOnScreen - playingFor
            : TimeSpan.Zero;

        /// <summary>Whether any of the browser's windows has <paramref name="title"/> in its title, i.e. it's the page shown in a tab in front.</summary>
        public static bool TitleOnScreen(string process, string title) => WindowTitleWith(process, title) != null;

        /// <summary>
        /// Which site is playing, from its tab's title: "Song - YouTube", "Song - YouTube Music",
        /// "Song • Artist" (Spotify's web player), "… on SoundCloud" and so on. Null when the
        /// title doesn't say.
        /// </summary>
        public static string? SiteOf(string windowTitle, string artist)
        {
            bool Has(string text) => windowTitle.Contains(text, StringComparison.OrdinalIgnoreCase);

            if (Has("YouTube Music"))
                return "YouTube Music";

            if (Has("- YouTube") || Has("– YouTube"))
                return "YouTube";

            if (Has("Apple Music"))
                return "Apple Music Web";

            if (Has("SoundCloud"))
                return "SoundCloud";

            if (Has("Spotify"))
                return "Spotify Web";

            if (Has("Deezer"))
                return "Deezer";

            if (Has("TIDAL"))
                return "TIDAL";

            if (Has("Bandcamp"))
                return "Bandcamp";

            if (Has("Amazon Music"))
                return "Amazon Music";

            // Spotify's web player names its tab "Song • Artist" while it plays
            if (artist.Length > 0 && Has(" • " + artist))
                return "Spotify Web";

            return null;
        }

        /// <summary>The title of a window of the browser whose title has <paramref name="title"/> in it: the tab in front playing it.</summary>
        public static string? WindowTitleWith(string process, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            var ids = new HashSet<uint>();

            foreach (var p in Process.GetProcessesByName(process))
            {
                ids.Add((uint)p.Id);
                p.Dispose();
            }

            if (ids.Count == 0)
                return null;

            string? found = null;
            var text = new StringBuilder(512);

            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window))
                    return true;

                GetWindowThreadProcessId(window, out uint id);

                if (!ids.Contains(id))
                    return true;

                text.Clear();
                GetWindowText(window, text, text.Capacity);

                if (text.ToString().Contains(title.Trim(), StringComparison.OrdinalIgnoreCase))
                    found = text.ToString();

                return found == null;
            }, IntPtr.Zero);

            return found;
        }

        delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    }
}
