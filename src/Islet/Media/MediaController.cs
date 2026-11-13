using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Islet
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
        double Rate = 1,
        bool? Shuffle = null,
        MediaPlaybackAutoRepeatMode? Repeat = null,
        bool CanShuffle = false,
        bool CanRepeat = false,
        string? Site = null,
        bool VideoClip = false,
        bool Mix = false,
        bool Edited = false)
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

        // What's known of each browser player, to keep previews and feed clips off the island
        sealed class Seen
        {
            public DateTime? PlayingSince;
            public string? Title;
            public string Album = "";
            public bool Music;
            public bool CoverSeen;
            public bool OnScreen;
            public bool Accepted;
            public bool Looking;
            public DateTime LookedAt;

            // A music video taken as the song it is
            public ArtworkService.SongFromVideo? Song;

            // The site playing in this tab, once its title has said
            public string? Site;

            // A mix, full album or long set: shown as it is, never counted as one song
            public bool Mix;
        }

        readonly Dictionary<GlobalSystemMediaTransportControlsSession, Seen> seen = new();
        DateTime nextLook = DateTime.MaxValue;

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

                var held = HeldBack(sessions);
                var session = PickSession(sessions.Except(held).ToList(), IsPlaying, wiredSession, manager.GetCurrentSession());

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

        // Browser players not to show yet: see BrowserPreviews. Called under the lock.
        List<GlobalSystemMediaTransportControlsSession> HeldBack(List<GlobalSystemMediaTransportControlsSession> sessions)
        {
            var now = DateTime.UtcNow;
            var held = new List<GlobalSystemMediaTransportControlsSession>();

            foreach (var gone in seen.Keys.Except(sessions).ToList())
                seen.Remove(gone);

            foreach (var s in sessions)
            {
                var (source, _, process) = Identify(s.SourceAppUserModelId);

                if (source != MediaSource.Browser)
                    continue;

                if (!seen.TryGetValue(s, out var info))
                    seen[s] = info = new Seen();

                info.PlayingSince = IsPlaying(s) ? info.PlayingSince ?? now : null;
                _ = LookAsync(s, info, process);

                // Until its title is known, it waits
                TimeSpan? playedFor = now - info.PlayingSince;

                // Videos aren't shown at all unless asked for
                bool allowed = info.Music || AppSettings.Current.ShowWebVideos;

                if (info.Title != null && allowed && !BrowserPreviews.Holds(source, info.Album, info.Accepted, playedFor, info.OnScreen, info.Song != null))
                {
                    info.Accepted = true;
                    continue;
                }

                held.Add(s);

                // Looked at again when it has played long enough, without waiting for the next poll
                if (playedFor is TimeSpan played && info.Title != null && allowed)
                    LookAgainAt(now + BrowserPreviews.NextLook(played, info.OnScreen));
            }

            return held;
        }

        void LookAgainAt(DateTime when)
        {
            if (when >= nextLook && nextLook > DateTime.UtcNow)
                return;

            nextLook = when;

            _ = Task.Delay(when - DateTime.UtcNow + TimeSpan.FromMilliseconds(100)).ContinueWith(_ =>
            {
                lock (sync)
                    nextLook = DateTime.MaxValue;

                return RefreshAsync();
            });
        }

        // Reads a browser player's title, and whether it's the page on screen, off the lock
        async Task LookAsync(GlobalSystemMediaTransportControlsSession session, Seen info, string process)
        {
            // Reading every browser window's title on each playback event is more than needed
            if (info.Looking || DateTime.UtcNow - info.LookedAt < TimeSpan.FromSeconds(1.5))
                return;

            info.Looking = true;
            info.LookedAt = DateTime.UtcNow;

            try
            {
                var media = await session.TryGetMediaPropertiesAsync();

                if (media != null && await LearnAsync(session, info, media, process))
                    await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Info("Couldn't read a browser player: " + ex.Message);
            }
            finally
            {
                info.Looking = false;
            }
        }

        /// <summary>
        /// Takes in what a browser player is playing now. A new title that isn't the page on screen
        /// starts its wait afresh, even in a tab shown before: hovering over videos on YouTube's
        /// home page plays them in the same tab as the video watched earlier. True if anything changed.
        /// </summary>
        async Task<bool> LearnAsync(GlobalSystemMediaTransportControlsSession session, Seen info, GlobalSystemMediaTransportControlsSessionMediaProperties media, string process)
        {
            string title = media.Title ?? "", album = media.AlbumTitle ?? "", artist = media.Artist ?? "";
            string? window = BrowserPreviews.WindowTitleWith(process, title);
            bool onScreen = window != null;
            string? site = (window != null ? BrowserPreviews.SiteOf(window, artist) : null) ?? info.Site;

            // Music or a video; the cover's shape only matters, and is only read, for a new song
            // with an artist but no album
            // (browsers often send the cover a moment after the title, so until one arrives it's asked again)
            bool music = info.Music;
            bool coverSeen = info.CoverSeen;
            var song = title == info.Title ? info.Song : null;
            bool mix = title == info.Title && info.Mix;

            if (title != info.Title || album != info.Album || (!music && !coverSeen))
            {
                music = !string.IsNullOrWhiteSpace(album);
                coverSeen = false;
                song = null;
                mix = false;

                if (!music && !string.IsNullOrWhiteSpace(artist))
                {
                    var bytes = await ReadImageAsync(media.Thumbnail);
                    var cover = bytes == null ? null : DecodeImage(bytes);
                    // A site with no artwork of its own gets its icon, or the browser's, sent instead;
                    // square, but not a cover
                    if (cover != null && ArtworkService.LooksLikeIcon(cover))
                        cover = null;

                    coverSeen = cover != null;
                    music = BrowserPreviews.IsMusic(artist, album, cover != null && ArtworkService.IsSquare(cover));
                }

                var timeline = session.GetTimelineProperties();
                mix = !music && AppSettings.Current.MusicVideosAsSongs &&
                      BrowserPreviews.LooksLikeMix(artist, title, timeline.EndTime - timeline.StartTime);

                if (mix)
                    music = true;

                // A music video, lyric video or audio upload on YouTube is the song it is, once the
                // catalogs confirm that song exists; any other video stays a video
                if (!music && AppSettings.Current.MusicVideosAsSongs && BrowserPreviews.LooksLikeMusicVideo(artist, title))
                {
                    song = await ArtworkService.SongFromVideoAsync(artist, title);
                    music = song != null;

                    // The catalogs couldn't be asked: asked again on the next look
                    coverSeen = song != null;
                }
            }

            lock (sync)
            {
                bool newTitle = info.Title != null && info.Title != title;
                bool changed = info.Title != title || info.Album != album || info.OnScreen != onScreen || info.Music != music;
                info.Music = music;
                info.CoverSeen = coverSeen;
                info.Song = song;
                info.Mix = mix;
                info.Site = song != null || mix ? site ?? "YouTube" : site;

                if (newTitle && !onScreen && string.IsNullOrWhiteSpace(album))
                {
                    info.Accepted = false;
                    info.PlayingSince = info.PlayingSince == null ? null : DateTime.UtcNow;
                }

                info.Title = title;
                info.Album = album;
                info.OnScreen = onScreen;
                return changed;
            }
        }

        public async Task<TrackInfo?> GetTrackAsync() => await GetTrackAsync(lookAgain: true);

        async Task<TrackInfo?> GetTrackAsync(bool lookAgain)
        {
            if (Session == null && !await RefreshAsync())
                return null;

            var session = Session!;

            var media = await session.TryGetMediaPropertiesAsync();

            // Some players, for a moment, have no details to give at all
            if (media == null)
                return null;

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var (source, sourceName, process) = Identify(session.SourceAppUserModelId);

            // The browser tab being shown has moved on to something else, perhaps a preview:
            // looked at again before it's shown, so a preview's thumbnail never flashes up
            Seen? info;

            lock (sync)
                seen.TryGetValue(session, out info);

            if (lookAgain && info != null && info.Title != (media.Title ?? "") &&
                await LearnAsync(session, info, media, process))
            {
                await RefreshAsync();
                return await GetTrackAsync(lookAgain: false);
            }

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

            // The last word, on exactly what would be shown: browser media that isn't music, or
            // hasn't been let through yet, never reaches the island, even for the moment between
            // a clip looping and it being looked at again
            if (source == MediaSource.Browser && !MayShow(session, media, artwork))
            {
                if (!lookAgain)
                    return null;

                await RefreshAsync();
                return await GetTrackAsync(lookAgain: false);
            }

            // A music video is shown as the song it is
            ArtworkService.SongFromVideo? asSong = null;
            string? site = null;
            bool isMix = false;

            if (source == MediaSource.Browser)
                lock (sync)
                    if (seen.TryGetValue(session, out var known))
                    {
                        site = known.Site;

                        if (known.Title == media.Title)
                        {
                            asSong = known.Song;
                            isMix = known.Mix;
                        }
                    }

            if (asSong != null)
                return new TrackInfo(
                    asSong.Title, asSong.Artist, "",
                    playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    asSong.Cover,
                    timeline.Position,
                    timeline.EndTime - timeline.StartTime,
                    timeline.LastUpdatedTime,
                    source, sourceName, process,
                    playback.PlaybackRate is > 0 and double videoRate ? videoRate : 1,
                    playback.IsShuffleActive,
                    playback.AutoRepeatMode,
                    playback.Controls.IsShuffleEnabled,
                    playback.Controls.IsRepeatEnabled,
                    site,
                    VideoClip: asSong.Clip,
                    Edited: asSong.Edit != null);

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
                playback.PlaybackRate is > 0 and double rate ? rate : 1,

                playback.IsShuffleActive,
                playback.AutoRepeatMode,
                playback.Controls.IsShuffleEnabled,
                playback.Controls.IsRepeatEnabled,
                site,
                Mix: isMix
            );
        }

        bool MayShow(GlobalSystemMediaTransportControlsSession session, GlobalSystemMediaTransportControlsSessionMediaProperties media, BitmapSource? cover)
        {
            bool music = BrowserPreviews.IsMusic(media.Artist ?? "", media.AlbumTitle ?? "", cover != null && ArtworkService.IsSquare(cover));
            bool videosToo = AppSettings.Current.ShowWebVideos;

            lock (sync)
            {
                if (!seen.TryGetValue(session, out var info))
                    return false;

                music |= (info.Song != null || info.Mix) && info.Title == media.Title;

                if (!music && !videosToo)
                {
                    info.Music = false;
                    info.Accepted = false;
                    return false;
                }

                return info.Accepted;
            }
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

        public async Task SetShuffleAsync(bool on)
        {
            if (Session != null)
                await Session.TryChangeShuffleActiveAsync(on);
        }

        public async Task SetRepeatAsync(MediaPlaybackAutoRepeatMode mode)
        {
            if (Session != null)
                await Session.TryChangeAutoRepeatModeAsync(mode);
        }

        /// <summary>The button cycles off, repeat all, repeat one, like most players.</summary>
        public static MediaPlaybackAutoRepeatMode RepeatAfter(MediaPlaybackAutoRepeatMode? mode) => mode switch
        {
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            MediaPlaybackAutoRepeatMode.Track => MediaPlaybackAutoRepeatMode.None,
            _ => MediaPlaybackAutoRepeatMode.List
        };

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
