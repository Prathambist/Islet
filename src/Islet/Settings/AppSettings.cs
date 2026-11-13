using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Islet
{
    public enum FullscreenRule
    {
        Default,
        Hide,
        Show
    }

    public enum IdleMode
    {
        Notch,
        Hide,
        Clock,
        ClockWeather,
        Circle
    }

    public enum PerformanceMode
    {
        Low,
        Medium,
        High
    }

    public enum MusicLook
    {
        Pill,
        Disc,
        Small
    }

    public enum IslandTheme
    {
        Midnight,
        Graphite,
        AlbumTint,
        Translucent
    }

    public enum IslandSize
    {
        Small,
        Default,
        Large
    }

    public enum ScrollTarget
    {
        // Named "Spotify" for settings-file compatibility; means "the player shown on the island"
        Spotify,
        System
    }

    public sealed class FullscreenApp
    {
        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public FullscreenRule Rule { get; set; } = FullscreenRule.Default;
    }

    public sealed class AppSettings
    {
        // Full-screen behaviour
        public bool HideInFullscreen { get; set; } = true;
        public List<FullscreenApp> FullscreenApps { get; set; } = new();

        // General
        public bool AutoUpdate { get; set; } = true;
        public string SettingsPalette { get; set; } = "Graphite";

        // Appearance
        public IslandTheme Theme { get; set; } = IslandTheme.Midnight;
        public IslandSize Size { get; set; } = IslandSize.Default;
        // Where along the top it sits, as a share of the screen's width (0.5 is the middle)
        public double IslandX { get; set; } = 0.5;
        public MusicLook MusicLook { get; set; } = MusicLook.Pill;
        public PerformanceMode Performance { get; set; } = PerformanceMode.Medium;
        public bool ShowLyrics { get; set; } = true;
        public bool FindVideoArt { get; set; } = true;

        // Chat notifications
        public bool NotifyDiscord { get; set; } = true;
        public bool NotifyWhatsApp { get; set; } = true;
        public bool NotifyMessenger { get; set; } = true;
        public bool NotifyBrowsers { get; set; }
        public bool NotifyShowMessage { get; set; } = true;

        // Music sources the island follows
        public bool SourceSpotify { get; set; } = true;
        public bool SourceAppleMusic { get; set; } = true;
        public bool SourceBrowsers { get; set; } = true;

        /// <summary>Videos in the browser (YouTube, Instagram, X…), not just music from the web.</summary>
        public bool ShowWebVideos { get; set; }

        /// <summary>Look up covers the listening stats are missing, on Apple's iTunes catalog.</summary>
        public bool FindStatsCovers { get; set; } = true;
        public bool SourceOtherApps { get; set; }

        // Idle / ambient
        public IdleMode IdleMode { get; set; } = IdleMode.Notch;
        public int IdleDelaySeconds { get; set; } = 15;
        public string WeatherCity { get; set; } = "";
        public double? WeatherLatitude { get; set; }
        public double? WeatherLongitude { get; set; }
        public bool UseFahrenheit { get; set; }
        public bool Use24HourClock { get; set; }

        // Interaction
        public bool ScrollVolumeEnabled { get; set; } = true;
        public bool ShowSystemStatus { get; set; } = true;
        public bool ReplaceVolumePopup { get; set; } = true;
        public ScrollTarget ScrollTarget { get; set; } = ScrollTarget.Spotify;
        public bool RealAudioVisualizer { get; set; } = true;
        public bool TimerSound { get; set; } = true;
        public bool ShowPrivacyDots { get; set; } = true;
        public bool ShelfEnabled { get; set; } = true;
        public bool ShowColorPalette { get; set; } = true;
        public bool FollowDoNotDisturb { get; set; } = true;
        public bool CatchScreenshots { get; set; } = true;
        public bool FindFileLocks { get; set; } = true;
        public bool DailyMusicSummary { get; set; } = true;

        /// <summary>Minutes away before the album art screensaver; 0 turns it off.</summary>
        public int ScreensaverMinutes { get; set; } = 5;

        // Spotify Web API
        public string SpotifyClientId { get; set; } = "";
        public string? SpotifyRefreshToken { get; set; }

        [JsonIgnore]
        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Islet");

        static string FilePath => Path.Combine(Folder, "settings.json");

        static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static AppSettings Current { get; private set; } = Load();

        public static event Action? Changed;

        static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(FilePath), options) ?? new AppSettings();
            }
            catch
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, options));
            }
            catch
            {
            }

            Changed?.Invoke();
        }

        public FullscreenApp? FindApp(string processName) =>
            FullscreenApps.Find(a => string.Equals(
                a.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

        // Records an app the first time it is seen full-screen so it shows up in Settings
        public FullscreenApp RememberApp(string processName, string displayName)
        {
            var app = FindApp(processName);

            if (app != null)
                return app;

            app = new FullscreenApp
            {
                ProcessName = processName,
                DisplayName = displayName
            };

            FullscreenApps.Add(app);
            Save();
            return app;
        }

        public bool ShouldHideFor(string processName)
        {
            var rule = FindApp(processName)?.Rule ?? FullscreenRule.Default;

            return rule switch
            {
                FullscreenRule.Hide => true,
                FullscreenRule.Show => false,
                _ => HideInFullscreen
            };
        }
    }
}
