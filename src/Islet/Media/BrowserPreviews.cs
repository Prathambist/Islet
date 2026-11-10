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

        /// <summary>
        /// Whether to keep this player off the island for now. <paramref name="playingFor"/> is how
        /// long it has played without stopping, or null while it isn't playing.
        /// </summary>
        public static bool Holds(MediaSource source, string album, bool accepted, TimeSpan? playingFor, bool titleOnScreen)
        {
            if (source != MediaSource.Browser || accepted || !string.IsNullOrWhiteSpace(album))
                return false;

            // A preview that stopped when the pointer moved on isn't left behind as paused media
            if (playingFor is not TimeSpan played)
                return true;

            return played < Settle || (!titleOnScreen && played < NotOnScreen);
        }

        /// <summary>When a held player that keeps playing would next be looked at again, from when it started.</summary>
        public static TimeSpan NextLook(TimeSpan playingFor, bool titleOnScreen) =>
            playingFor < Settle ? Settle - playingFor
            : !titleOnScreen && playingFor < NotOnScreen ? NotOnScreen - playingFor
            : TimeSpan.Zero;

        /// <summary>Whether any of the browser's windows has <paramref name="title"/> in its title, i.e. it's the page shown in a tab in front.</summary>
        public static bool TitleOnScreen(string process, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            var ids = new HashSet<uint>();

            foreach (var p in Process.GetProcessesByName(process))
            {
                ids.Add((uint)p.Id);
                p.Dispose();
            }

            if (ids.Count == 0)
                return false;

            bool found = false;
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
                found = text.ToString().Contains(title.Trim(), StringComparison.OrdinalIgnoreCase);
                return !found;
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
