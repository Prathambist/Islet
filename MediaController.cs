using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MusicIsland
{
    public enum MediaSource
    {
        Spotify,
        AppleMusic,
        Browser,
        Other
    }

    public sealed record TrackInfo(
        string Title,
        string Artist,
        string Album,
        bool Playing,
        BitmapSource? Artwork,
        TimeSpan Position,
        TimeSpan Duration,
        DateTimeOffset PositionUpdated,
        MediaSource Source,
        string SourceName,
        string ProcessName,
        double Rate = 1)
    {
        /// <summary>Where playback is now; players only report it now and then.</summary>
        public TimeSpan PositionAt(DateTimeOffset now) =>
            Playing && PositionUpdated > DateTimeOffset.MinValue
                ? Position + (now - PositionUpdated) * Rate
                : Position;
    }

    /// <summary>
    /// Follows Windows' media sessions (the same ones the volume flyout shows):
    /// the Spotify and Apple Music apps, and browsers playing Spotify Web,
    /// YouTube, YouTube Music, Apple Music on the web, etc.
    /// </summary>
    public sealed class MediaController
    {
        // Shown until a player reports a title; browsers do that for a moment while a page loads
        public const string UnknownTitle = "Unknown track";

        GlobalSystemMediaTransportControlsSessionManager? manager;
        GlobalSystemMediaTransportControlsSession? wiredSession;

        // Every session we listen to for play/pause, so a player that starts
        // playing can take over the island
        // (keyed by object, since a browser can expose several sessions with the same app id)
        readonly HashSet<GlobalSystemMediaTransportControlsSession> watched = new();
        readonly object sync = new();

        // Cached per track, but reloaded whenever the player reports a change:
        // browsers often send the real thumbnail a moment after the title
        string artworkKey = "";
        BitmapSource? artwork;
        byte[]? artworkBytes;
        volatile bool artworkDirty = true;

        public GlobalSystemMediaTransportControlsSession? Session
        {
            get;
            private set;
        }

        public event Action? MediaChanged;
        public event Action? PlaybackChanged;

        public async Task<bool> ConnectAsync()
        {
            manager =
                await GlobalSystemMediaTransportControlsSessionManager
                    .RequestAsync();

            manager.SessionsChanged += (s, e) =>
            {
                _ = RefreshAsync();
            };

            manager.CurrentSessionChanged += (s, e) =>
            {
                _ = RefreshAsync();
            };

            return await RefreshAsync();
        }

        void OnMediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {
            artworkDirty = true;
            MediaChanged?.Invoke();
        }

        void OnTimelineChanged(
            GlobalSystemMediaTransportControlsSession sender,
            TimelinePropertiesChangedEventArgs args) => PlaybackChanged?.Invoke();

        void OnAnyPlaybackChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {
            // May switch to another player; always tell the UI either way
            _ = RefreshAsync();
            PlaybackChanged?.Invoke();
        }

        public static (MediaSource Source, string Name, string Process) Identify(string appId)
        {
            string id = appId.ToLowerInvariant();

            if (id.Contains("spotify"))
                return (MediaSource.Spotify, "Spotify", "Spotify");

            if (id.Contains("applemusic") || id.StartsWith("appleinc."))
                return (MediaSource.AppleMusic, "Apple Music", "AppleMusic");

            // Firefox registers under a hashed AppUserModelID
            if (id.Contains("firefox") || id == "308046b0af4a39cb")
                return (MediaSource.Browser, "Firefox", "firefox");

            if (id.Contains("msedge"))
                return (MediaSource.Browser, "Edge", "msedge");

            if (id.Contains("chrome"))
                return (MediaSource.Browser, "Chrome", "chrome");

            if (id.Contains("opera"))
                return (MediaSource.Browser, "Opera", "opera");

            if (id.Contains("brave"))
                return (MediaSource.Browser, "Brave", "brave");

            if (id.Contains("vivaldi"))
                return (MediaSource.Browser, "Vivaldi", "vivaldi");

            string process = Path.GetFileNameWithoutExtension(appId.Split('!')[0]);
            return (MediaSource.Other, process, process);
        }

        static bool IsAllowed(GlobalSystemMediaTransportControlsSession session)
        {
            var settings = AppSettings.Current;

            return Identify(session.SourceAppUserModelId).Source switch
            {
                MediaSource.Spotify => settings.SourceSpotify,
                MediaSource.AppleMusic => settings.SourceAppleMusic,
                MediaSource.Browser => settings.SourceBrowsers,
                _ => settings.SourceOtherApps
            };
        }

        static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                return session.GetPlaybackInfo().PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stick with the player we're showing while it plays; otherwise follow
        /// whichever starts playing, preferring Windows' own pick. With nothing
        /// playing, keep the last one (so paused music stays).
        /// </summary>
        internal static T? PickSession<T>(IReadOnlyList<T> sessions, Func<T, bool> isPlaying, T? shown, T? current)
            where T : class
        {
            var playing = sessions.Where(isPlaying).ToList();

            return
                playing.FirstOrDefault(s => s == shown) ??
                playing.FirstOrDefault(s => s == current) ??
                playing.FirstOrDefault() ??
                sessions.FirstOrDefault(s => s == shown) ??
                sessions.FirstOrDefault(s => s == current) ??
                sessions.FirstOrDefault();
        }

        public Task<bool> RefreshAsync()
        {
            if (manager == null)
                return Task.FromResult(false);

            bool changed;

            lock (sync)
            {
                var sessions = manager.GetSessions().Where(IsAllowed).ToList();

                foreach (var gone in watched.Except(sessions).ToList())
                {
                    gone.PlaybackInfoChanged -= OnAnyPlaybackChanged;
                    watched.Remove(gone);
                }

                foreach (var s in sessions)
                {
                    if (watched.Add(s))
                        s.PlaybackInfoChanged += OnAnyPlaybackChanged;
                }

                var session = PickSession(sessions, IsPlaying, wiredSession, manager.GetCurrentSession());

                changed = session != wiredSession;

                if (changed)
                {
                    if (wiredSession != null)
                    {
                        wiredSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        wiredSession.TimelinePropertiesChanged -= OnTimelineChanged;
                    }

                    wiredSession = session;
                    artworkDirty = true;

                    if (session != null)
                    {
                        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                        session.TimelinePropertiesChanged += OnTimelineChanged;
                    }
                }

                Session = session;
            }

            if (changed)
            {
                MediaChanged?.Invoke();
                PlaybackChanged?.Invoke();
            }

            return Task.FromResult(Session != null);
        }

        public async Task<TrackInfo?> GetTrackAsync()
        {
            if (Session == null && !await RefreshAsync())
                return null;

            var session = Session!;

            var media = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var (source, sourceName, process) = Identify(session.SourceAppUserModelId);

            string key = session.SourceAppUserModelId + "|" + media.Title + "|" + media.Artist + "|" + media.AlbumTitle;

            if (key != artworkKey || artworkDirty || (artwork == null && media.Thumbnail != null))
            {
                artworkDirty = false;
                artworkKey = key;

                var bytes = await ReadImageAsync(media.Thumbnail);

                // Browsers report changes repeatedly with the same picture; keeping the
                // same image avoids decoding it and recolouring the island each time
                if (bytes == null || artworkBytes == null || !bytes.AsSpan().SequenceEqual(artworkBytes))
                {
                    artworkBytes = bytes;
                    artwork = bytes == null ? null : await Task.Run(() => DecodeImage(bytes));

                    // With no artwork from the site, Windows hands over the browser's logo
                    if (artwork != null && ArtworkService.LooksLikeIcon(artwork))
                    {
                        Log.Info($"Ignoring app icon used as artwork by {sourceName}");
                        artwork = null;
                    }
                }
            }

            return new TrackInfo(
                string.IsNullOrWhiteSpace(media.Title)
                    ? UnknownTitle
                    : media.Title,

                // Browsers often leave artist empty (e.g. plain YouTube videos)
                string.IsNullOrWhiteSpace(media.Artist)
                    ? sourceName
                    : media.Artist,

                string.IsNullOrWhiteSpace(media.AlbumTitle)
                    ? ""
                    : media.AlbumTitle,

                playback.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus
                        .Playing,

                artwork,

                timeline.Position,
                timeline.EndTime - timeline.StartTime,
                timeline.LastUpdatedTime,

                source,
                sourceName,
                process,

                // Web players can run at other speeds (YouTube's 1.5x, for example)
                playback.PlaybackRate is > 0 and double rate ? rate : 1
            );
        }

        public static async Task<BitmapSource?> LoadImageAsync(IRandomAccessStreamReference? thumbnail)
        {
            var bytes = await ReadImageAsync(thumbnail);
            return bytes == null ? null : await Task.Run(() => DecodeImage(bytes));
        }

        static async Task<byte[]?> ReadImageAsync(IRandomAccessStreamReference? thumbnail)
        {
            if (thumbnail == null)
                return null;

            try
            {
                using var stream =
                    await thumbnail.OpenReadAsync();

                using var reader =
                    new DataReader(stream.GetInputStreamAt(0));

                await reader.LoadAsync((uint)stream.Size);

                byte[] bytes = new byte[(int)stream.Size];
                reader.ReadBytes(bytes);

                return bytes;
            }
            catch
            {
                return null;
            }
        }

        // The island never draws art larger than this, even on high-DPI screens
        const int MaxArtSize = 192;

        /// <summary>Decodes on any thread; large art is shrunk so drawing it stays cheap.</summary>
        internal static BitmapSource? DecodeImage(byte[] bytes)
        {
            try
            {
                using var memory = new MemoryStream(bytes);

                int sourceWidth = BitmapDecoder.Create(memory, BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None).Frames[0].PixelWidth;

                memory.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = memory;

                if (sourceWidth > MaxArtSize)
                    bitmap.DecodePixelWidth = MaxArtSize;

                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public async Task PreviousAsync()
        {
            if (Session == null)
                await RefreshAsync();

            if (Session != null)
                await Session.TrySkipPreviousAsync();
        }

        public async Task TogglePlayPauseAsync()
        {
            if (Session == null)
                await RefreshAsync();

            if (Session != null)
                await Session.TryTogglePlayPauseAsync();
        }

        public async Task<bool> SeekAsync(TimeSpan position)
        {
            if (Session == null)
                return false;

            var start = Session.GetTimelineProperties().StartTime;
            return await Session.TryChangePlaybackPositionAsync((start + position).Ticks);
        }

        public async Task NextAsync()
        {
            if (Session == null)
                await RefreshAsync();

            if (Session != null)
                await Session.TrySkipNextAsync();
        }
    }
}
