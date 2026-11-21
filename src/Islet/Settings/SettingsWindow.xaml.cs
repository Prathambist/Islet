using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Islet
{
    /// <summary>
    /// Settings are applied and saved as soon as they change. Pages sit in a sidebar, with a
    /// search that finds any setting on any page; the building blocks are in SettingsWindow.Controls.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        readonly SpotifyWebApi spotifyWeb;
        readonly MainWindow island;

        StackPanel appList = null!;
        int listedApps;
        TextBlock weatherStatus = null!;
        TextBlock spotifyStatus = null!;
        Button connectButton = null!;
        TextBlock updateStatus = null!;
        StackPanel updateNotes = null!;
        Button installButton = null!;
        TextBlock notifyStatus = null!;
        CancellationTokenSource? connecting;

        static AppSettings Settings => AppSettings.Current;

        public SettingsWindow(SpotifyWebApi spotifyWeb, MainWindow island)
        {
            this.spotifyWeb = spotifyWeb;
            this.island = island;

            InitializeComponent();

            SourceInitialized += (s, e) =>
            {
                // A title bar to match the palette, on Windows 10 20H1+ / 11
                MatchTitleBar();
            };

            spotifyWeb.ConnectionChanged += OnConnectionChanged;
            AppSettings.Changed += OnSettingsChanged;

            Closed += (s, e) =>
            {
                connecting?.Cancel();
                spotifyWeb.ConnectionChanged -= OnConnectionChanged;
                AppSettings.Changed -= OnSettingsChanged;
            };

            SidebarVersion.Text = $"Version {UpdateService.CurrentVersion}";
            SetUpPalette();

            BuildIslandPage();
            BuildMusicPage();
            BuildListeningPage();
            BuildNotificationsPage();
            BuildToolsPage();
            BuildFullscreenPage();
            BuildAboutPage();

            OpenFirstPage();
        }

        void OnConnectionChanged() => Dispatcher.BeginInvoke(new Action(UpdateSpotifyStatus));

        // Apps get added to the list while this window is open
        void OnSettingsChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (listedApps != Settings.FullscreenApps.Count)
                BuildAppList();
        }));

        // ================= Island =================

        void BuildIslandPage()
        {
            Page("Island", "", "How the island looks, where it sits, and what it shows when nothing's playing.");

            var look = Group("Look");

            ChoiceRow(look, "Theme", null,
                new[] { "Midnight", "Graphite", "Album tint", "Translucent" },
                (int)Settings.Theme,
                i => { Settings.Theme = (IslandTheme)i; Settings.Save(); });

            ChoiceRow(look, "Size", null,
                new[] { "Small", "Default", "Large" },
                (int)Settings.Size,
                i => { Settings.Size = (IslandSize)i; Settings.Save(); });

            ChoiceRow(look, "While music plays", "The island's shape when it isn't open. Tiny and Line keep out of the way.",
                new[] { "Pill with bars", "Circle with bars", "Small pill", "Tiny", "Line" },
                (int)Settings.MusicLook,
                i => { Settings.MusicLook = (MusicLook)i; Settings.Save(); });

            TextBlock performanceNote = null!;
            ChoiceRow(look, "Performance", PerformanceProfile.For(Settings.Performance).Summary,
                new[] { "Low", "Medium", "High" },
                (int)Settings.Performance,
                i =>
                {
                    Settings.Performance = (PerformanceMode)i;
                    Settings.Save();
                    performanceNote.Text = PerformanceProfile.For(Settings.Performance).Summary;
                },
                out performanceNote);

            var position = Group("Position");
            ChoiceRow(position, "Edge", "At the bottom it sits above the taskbar and opens upwards.",
                new[] { "Top", "Bottom" },
                (int)Settings.IslandEdge,
                i => { Settings.IslandEdge = (IslandEdge)i; Settings.Save(); });
            Row(position, "Where it sits", "Hold the island for a second, then drag it anywhere along the top or bottom.",
                PillButton("Move to the centre", () =>
                {
                    Settings.IslandX = 0.5;
                    Settings.Save();
                }));

            var idle = Group("When nothing is playing");

            ChoiceRow(idle, "Idle look", null,
                new[] { "Tiny clock", "Hide", "Clock", "Clock + weather", "Circle" },
                (int)Settings.IdleMode,
                i =>
                {
                    Settings.IdleMode = (IdleMode)i;
                    Settings.Save();
                    _ = island.RefreshWeatherAsync();
                });

            ChoiceRow(idle, "Date", "Nepal's calendar (Bikram Sambat) on the island, in English letters or Nepali.",
                new[] { "Usual", "Nepali", "नेपाली" },
                (int)Settings.NepaliDate,
                i => { Settings.NepaliDate = (NepaliDateStyle)i; Settings.Save(); });

            int[] delays = { 5, 15, 60, 0 };
            ChoiceRow(idle, "Go idle after the music pauses for", null,
                new[] { "5 sec", "15 sec", "1 min", "Only when closed" },
                Math.Max(0, Array.IndexOf(delays, Settings.IdleDelaySeconds)),
                i => { Settings.IdleDelaySeconds = delays[i]; Settings.Save(); });

            Row(idle, "Weather location", null, null, WeatherPicker(), out weatherStatus);
            weatherStatus.Text = Settings.WeatherLatitude != null
                ? $"Using {Settings.WeatherCity}. Weather by Open-Meteo."
                : "Type a city to show weather. Weather by Open-Meteo.";
            weatherStatus.Visibility = Visibility.Visible;

            SwitchRow(idle, "Fahrenheit", null, Settings.UseFahrenheit, v =>
            {
                Settings.UseFahrenheit = v;
                Settings.Save();
                _ = island.RefreshWeatherAsync();
            });

            SwitchRow(idle, "24-hour clock", null, Settings.Use24HourClock,
                v => { Settings.Use24HourClock = v; Settings.Save(); });
        }

        FrameworkElement WeatherPicker()
        {
            var row = new DockPanel();
            var cityBox = new TextBox { Text = Settings.WeatherCity };
            var setCity = PillButton("Set", async () =>
            {
                if (string.IsNullOrWhiteSpace(cityBox.Text))
                {
                    Settings.WeatherCity = "";
                    Settings.WeatherLatitude = Settings.WeatherLongitude = null;
                    Settings.Save();
                    weatherStatus.Text = "Weather turned off.";
                    await island.RefreshWeatherAsync();
                    return;
                }

                weatherStatus.Text = "Looking up…";

                try
                {
                    var place = await WeatherService.GeocodeAsync(cityBox.Text);

                    if (place == null)
                    {
                        weatherStatus.Text = "Couldn't find that place.";
                        return;
                    }

                    Settings.WeatherCity = place.Value.Name;
                    Settings.WeatherLatitude = place.Value.Latitude;
                    Settings.WeatherLongitude = place.Value.Longitude;
                    Settings.Save();
                    cityBox.Text = place.Value.Name;
                    weatherStatus.Text = $"Using {place.Value.Name}.";
                    await island.RefreshWeatherAsync();
                }
                catch
                {
                    weatherStatus.Text = "Weather service unreachable.";
                }
            });

            setCity.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(setCity, Dock.Right);
            row.Children.Add(setCity);
            row.Children.Add(cityBox);
            return row;
        }

        // ================= Music =================

        void BuildMusicPage()
        {
            Page("Music", "", "Which players the island follows, lyrics and covers, and what happens while you're away.");

            var sources = Group("Players the island follows");
            SwitchRow(sources, "Spotify app", null, Settings.SourceSpotify,
                v => { Settings.SourceSpotify = v; Settings.Save(); });
            SwitchRow(sources, "Apple Music app", null, Settings.SourceAppleMusic,
                v => { Settings.SourceAppleMusic = v; Settings.Save(); });
            SwitchRow(sources, "Music in your browser",
                "Spotify Web, YouTube Music, SoundCloud, Apple Music on the web and more, in Chrome, Edge, Firefox, Opera or Brave.",
                Settings.SourceBrowsers, v => { Settings.SourceBrowsers = v; Settings.Save(); });
            SwitchRow(sources, "Videos on websites",
                "YouTube, Instagram, X and other videos. Off keeps the island to music; on, a video shows once it has played for a few seconds, so previews and feed clips stay off.",
                Settings.ShowWebVideos, v => { Settings.ShowWebVideos = v; Settings.Save(); });
            SwitchRow(sources, "Any other app that reports media to Windows",
                "When several are playing, the island stays on the one it's showing; when that pauses, it switches to whichever is still playing.",
                Settings.SourceOtherApps, v => { Settings.SourceOtherApps = v; Settings.Save(); });

            var playing = Group("While it plays");
            SwitchRow(playing, "Synced lyrics", "The current line appears under the song when you open the island. Lyrics by LRCLIB.",
                Settings.ShowLyrics, v => { Settings.ShowLyrics = v; Settings.Save(); });
            SwitchRow(playing, "Real-time audio visualizer",
                "The bars follow the actual bass, mids and treble coming out of your speakers. Turn off for a lighter animated pattern.",
                Settings.RealAudioVisualizer, v => { Settings.RealAudioVisualizer = v; Settings.Save(); });

            var volume = Group("Volume");
            SwitchRow(volume, "Scroll over the island to change volume", null,
                Settings.ScrollVolumeEnabled, v => { Settings.ScrollVolumeEnabled = v; Settings.Save(); });
            ChoiceRow(volume, "Scrolling controls",
                "Player volume changes the app's own level in the Windows mixer. For web players that's the whole browser.",
                new[] { "Player volume", "System volume" },
                (int)Settings.ScrollTarget,
                i => { Settings.ScrollTarget = (ScrollTarget)i; Settings.Save(); });

            var away = Group("While you're away");
            int[] screensaverMinutes = { 2, 5, 10 };
            SwitchRow(away, "Screensaver",
                "When you're away while music plays, the cover and the song's lyrics fill the screen. Not during calls, videos or games. Any key or mouse movement brings you back.",
                Settings.ScreensaverEnabled, v => { Settings.ScreensaverEnabled = v; Settings.Save(); });
            ChoiceRow(away, "Starts after", null,
                new[] { "2 min", "5 min", "10 min" },
                Math.Max(0, Array.IndexOf(screensaverMinutes, Settings.ScreensaverMinutes)),
                i => { Settings.ScreensaverMinutes = screensaverMinutes[i]; Settings.Save(); });

            // Everything that asks Deezer or iTunes, together: the main switch, then each use of it
            var online = Group("Looked up online");
            SwitchRow(online, "Look things up online",
                "Only song, artist and album names are sent, to Deezer and Apple's iTunes. Off, none of the things below leave this PC. Lyrics (LRCLIB) and weather have their own switches.",
                Settings.LookUpOnline, v => { Settings.LookUpOnline = v; Settings.Save(); });
            SwitchRow(online, "Music videos as songs",
                "Official music videos, lyric videos and audio uploads on YouTube show as the song, with its album cover, and count in your listening stats. Other videos stay off.",
                Settings.MusicVideosAsSongs, v => { Settings.MusicVideosAsSongs = v; Settings.Save(); });
            SwitchRow(online, "Real covers for music videos",
                "Video sites only share a video frame, so the song's real cover is looked up. Songs a player sends without a cover get one too.",
                Settings.FindVideoArt, v => { Settings.FindVideoArt = v; Settings.Save(); });
            SwitchRow(online, "Missing covers in your listening stats",
                "For songs played before covers were kept, or whose player gave none. Kept with the stats.",
                Settings.FindStatsCovers, v => { Settings.FindStatsCovers = v; Settings.Save(); });

            var account = Group("Spotify account");
            Row(account, "Like button",
                "Lets the heart on the music card save songs to your Liked Songs. Spotify needs a free developer app of your own (needs Premium).");

            var steps = new StackPanel();
            steps.Children.Add(Note(
                "1.  Create an app at developer.spotify.com/dashboard, choosing Web API.\n" +
                "2.  Add the redirect URI  " + SpotifyWebApi.RedirectUri + "\n" +
                "3.  Paste its Client ID here and press Connect."));

            var clientBox = new TextBox { Text = Settings.SpotifyClientId, Margin = new Thickness(0, 10, 0, 0) };
            clientBox.LostFocus += (s, e) =>
            {
                if (clientBox.Text.Trim() == Settings.SpotifyClientId)
                    return;

                Settings.SpotifyClientId = clientBox.Text.Trim();
                Settings.SpotifyRefreshToken = null;
                Settings.Save();
                UpdateSpotifyStatus();
            };
            steps.Children.Add(clientBox);
            Row(account, "Client ID", null, null, steps);

            connectButton = PillButton("Connect", async () =>
            {
                Settings.SpotifyClientId = clientBox.Text.Trim();
                Settings.Save();

                if (spotifyWeb.IsConnected)
                {
                    spotifyWeb.Disconnect();
                    return;
                }

                connecting?.Cancel();
                connecting = new CancellationTokenSource();
                connectButton.IsEnabled = false;
                spotifyStatus.Text = "Finish signing in in your browser…";

                try
                {
                    await spotifyWeb.ConnectAsync(connecting.Token);
                }
                catch (OperationCanceledException)
                {
                    spotifyStatus.Text = "Sign-in timed out.";
                    return;
                }
                catch (Exception ex)
                {
                    spotifyStatus.Text = ex.Message;
                    return;
                }
                finally
                {
                    connectButton.IsEnabled = true;
                }

                UpdateSpotifyStatus();
            });

            Row(account, "Connection", " ", connectButton, null, out spotifyStatus);
            UpdateSpotifyStatus();
        }

        // ================= Notifications =================

        void BuildNotificationsPage()
        {
            Page("Notifications", "", "Messages that drop out of the island, quiet times, and what else it shows.");

            var access = Group(null);
            var accessButton = PillButton("Allow access", async () =>
            {
                await island.Notifications.StartAsync();
                UpdateNotifyStatus();

                if (island.Notifications.Status != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ms-settings:privacy-notifications",
                        UseShellExecute = true
                    });
            });
            Row(access, "Permission to read notifications", " ", accessButton, null, out notifyStatus);
            UpdateNotifyStatus();

            var islandGroup = Group("On the island");
            SwitchRow(islandGroup, "Show notifications on the island",
                "Off, a yellow dot on the island's corner says some arrived; it goes once they're read in Windows, or click it to open them.",
                Settings.ShowNotifications, v => { Settings.ShowNotifications = v; Settings.Save(); });
            SwitchRow(islandGroup, "Hide Windows' own pop-ups",
                "Each notification the island shows is cleared from Windows, which closes its pop-up in the corner. They won't be in Windows' notification centre afterwards.",
                Settings.HideWindowsPopups, v => { Settings.HideWindowsPopups = v; Settings.Save(); });

            var messages = Group("Messages");
            SwitchRow(messages, "Discord", null, Settings.NotifyDiscord,
                v => { Settings.NotifyDiscord = v; Settings.Save(); UpdateNotifyStatus(); });
            SwitchRow(messages, "WhatsApp", "The desktop app, or WhatsApp Web installed from your browser (menu > Install app).", Settings.NotifyWhatsApp,
                v => { Settings.NotifyWhatsApp = v; Settings.Save(); UpdateNotifyStatus(); });
            SwitchRow(messages, "Messenger", null, Settings.NotifyMessenger,
                v => { Settings.NotifyMessenger = v; Settings.Save(); UpdateNotifyStatus(); });
            SwitchRow(messages, "Every other app",
                "Mail, Teams, calendars, game launchers, your browser and the websites in it, Windows itself: any notification, under its app's name and icon.",
                Settings.NotifyOtherApps, v => { Settings.NotifyOtherApps = v; Settings.Save(); UpdateNotifyStatus(); });
            SwitchRow(messages, "Show message text", "Off shows only who it's from.", Settings.NotifyShowMessage,
                v => { Settings.NotifyShowMessage = v; Settings.Save(); });

            var quiet = Group("Quiet times");
            SwitchRow(quiet, "Hold messages while Windows' Do Not Disturb is on",
                "While it's on, notifications wait behind an orange dot beside the island, and stay in Windows' notification centre. When it turns off, new ones show as usual and the held ones aren't shown again.",
                Settings.FollowDoNotDisturb, v => { Settings.FollowDoNotDisturb = v; Settings.Save(); });

            var onIsland = Group("Also on the island");
            SwitchRow(onIsland, "Volume, brightness and devices", "Changes show on the island in a form that matches its look.",
                Settings.ShowSystemStatus, v => { Settings.ShowSystemStatus = v; Settings.Save(); });
            SwitchRow(onIsland, "Use the island instead of the Windows volume pop-up", null,
                Settings.ReplaceVolumePopup, v => { Settings.ReplaceVolumePopup = v; Settings.Save(); });
            SwitchRow(onIsland, "Mic and camera dots", "A dot beside the island while an app uses your mic (orange) or camera (green).",
                Settings.ShowPrivacyDots, v => { Settings.ShowPrivacyDots = v; Settings.Save(); });
            SwitchRow(onIsland, "Timer sound", "A short sound with the lights when a timer ends.",
                Settings.TimerSound, v => { Settings.TimerSound = v; Settings.Save(); });
        }

        // ================= Tools =================

        void BuildToolsPage()
        {
            Page("Tools", "", "Things you can do with the island: park files, pick colours, and find what's holding a file open.");

            var shelf = Group("File shelf");
            SwitchRow(shelf, "Keep files dropped on the island",
                "Drag files onto the island to park them, then drag them out into any app or folder. The shelf remembers where your files are rather than copying them.",
                Settings.ShelfEnabled, v => { Settings.ShelfEnabled = v; Settings.Save(); });
            SwitchRow(shelf, "Put new screenshots on the shelf",
                "Screenshots from Win+Shift+S drop out of the island; drag one straight into a chat.",
                Settings.CatchScreenshots, v => { Settings.CatchScreenshots = v; Settings.Save(); });
            Row(shelf, "Shelf folder", "Images and links dropped from a web page are saved here.",
                PillButton("Open", () =>
                {
                    var folder = System.IO.Path.Combine(AppSettings.Folder, "Shelf");
                    System.IO.Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
                }));

            BuildClipboardGroup();

            var colours = Group("Colour picker");
            Row(colours, "Pick a colour", "Or right-click the island. The hex code is copied.",
                PillButton("Pick", () => island.StartPicking()));
            SwitchRow(colours, "Keep picked colours beside the island", "Your last 12 colours wait in a small badge right of the island.",
                Settings.ShowColorPalette, v => { Settings.ShowColorPalette = v; Settings.Save(); });

            var locks = Group("Files in use");
            SwitchRow(locks, "Find out what has a file open",
                "When Windows won't delete or change a file, drag it onto the island and let go on \"What's using it?\".",
                Settings.FindFileLocks, v => { Settings.FindFileLocks = v; Settings.Save(); });
        }

        // Retention choices, in hours; anything else is "Custom"
        public static readonly int[] ClipboardRetentionChoices = { 24, 48, 168, 336, 720 };

        void BuildClipboardGroup()
        {
            var clipboard = Group("Clipboard history");
            SwitchRow(clipboard, "Keep a history of text you copy",
                "Saved on this PC only, encrypted for your Windows account. Passwords copied from password managers are left out.",
                Settings.ClipboardHistoryEnabled, v => { Settings.ClipboardHistoryEnabled = v; Settings.Save(); });

            int choice = Array.IndexOf(ClipboardRetentionChoices, Settings.ClipboardRetentionHours);
            var customRow = new DockPanel { Visibility = choice < 0 ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
            var customBox = new TextBox { Text = Math.Max(1, Settings.ClipboardRetentionHours / 24).ToString(), Width = 70 };
            TextBlock customNote = null!;

            void ApplyCustom()
            {
                if (int.TryParse(customBox.Text.Trim(), out int days) && days is >= 1 and <= 3650)
                {
                    Settings.ClipboardRetentionHours = days * 24;
                    Settings.Save();
                    customNote.Text = $"Entries are deleted {days} day{(days == 1 ? "" : "s")} after they're copied.";
                }
                else
                {
                    customNote.Text = "Type a number of days, from 1 to 3650.";
                }
            }

            customBox.LostFocus += (s, e) => ApplyCustom();
            customBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    ApplyCustom();
            };

            customNote = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Faded(customNote);
            var days = new TextBlock { Text = "days", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(customBox, Dock.Left);
            DockPanel.SetDock(days, Dock.Left);
            customRow.Children.Add(customBox);
            customRow.Children.Add(days);
            customRow.Children.Add(customNote);

            var keep = new StackPanel();
            keep.Children.Add(Segmented(
                new[] { "24 hours", "2 days", "7 days", "14 days", "30 days", "Custom" },
                choice < 0 ? ClipboardRetentionChoices.Length : choice,
                i =>
                {
                    customRow.Visibility = i == ClipboardRetentionChoices.Length ? Visibility.Visible : Visibility.Collapsed;

                    if (i < ClipboardRetentionChoices.Length)
                    {
                        Settings.ClipboardRetentionHours = ClipboardRetentionChoices[i];
                        Settings.Save();
                    }
                    else
                    {
                        ApplyCustom();
                    }
                }));
            keep.Children.Add(customRow);
            Row(clipboard, "Keep copied text for", "Each entry is deleted this long after it was copied. Pinned entries stay until you delete them.", null, keep);

            Row(clipboard, "Your history", "Search it, copy something again, pin or delete entries. Also in the tray menu.",
                Buttons(
                    PillButton("Open", () => island.OpenClipboardHistory()),
                    PillButton("Clear", () =>
                    {
                        if (MessageBox.Show(this, "Delete your whole clipboard history, pinned entries too?", "Islet",
                                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                            island.ClipboardHistory.Clear(includePinned: true);
                    })));
        }

        // ================= Full-screen apps =================

        void BuildFullscreenPage()
        {
            Page("Full-screen apps", "", "The island steps aside for games, videos and slide shows. Choose per app here, or from the tray icon while one is full-screen.");

            var rule = Group(null);
            SwitchRow(rule, "Hide the island when an app is full-screen", "Applies to every app set to Default below.",
                Settings.HideInFullscreen, v => { Settings.HideInFullscreen = v; Settings.Save(); });

            var apps = Group("Apps seen full-screen");
            appList = new StackPanel();
            apps.Children.Add(appList);
            BuildAppList();

            var add = Group(null);
            var addRow = new DockPanel();
            var addBox = new TextBox { ToolTip = "Process name, e.g. vlc or chrome" };
            var addButton = PillButton("Add", () =>
            {
                string name = addBox.Text.Trim();

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];

                if (name.Length == 0)
                    return;

                Settings.RememberApp(name, name);
                addBox.Text = "";
                BuildAppList();
            });

            addButton.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(addButton, Dock.Right);
            addRow.Children.Add(addButton);
            addRow.Children.Add(addBox);
            Row(add, "Add an app by name", "Its process name, like vlc or chrome.", null, addRow);
        }

        void BuildAppList()
        {
            appList.Children.Clear();
            listedApps = Settings.FullscreenApps.Count;

            if (Settings.FullscreenApps.Count == 0)
            {
                appList.Children.Add(new Border
                {
                    Padding = new Thickness(16, 14, 16, 14),
                    Child = Note("None yet. Games, videos and slide shows appear here the first time they go full-screen.")
                });
                return;
            }

            foreach (var app in Settings.FullscreenApps.OrderBy(a => a.DisplayName))
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(new TextBlock { Text = app.DisplayName, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
                names.Children.Add(Faded(new TextBlock { Text = app.ProcessName + ".exe", FontSize = 11.5 }));
                row.Children.Add(names);

                var choice = Segmented(
                    new[] { "Default", "Hide", "Show" },
                    (int)app.Rule,
                    i => { app.Rule = (FullscreenRule)i; Settings.Save(); });

                choice.Margin = new Thickness(12, 0, 4, 0);
                choice.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(choice, 1);
                row.Children.Add(choice);

                var remove = new Button
                {
                    Content = "",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 10,
                    Width = 28,
                    Height = 28,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Forget this app",
                    Template = TransparentTemplate()
                };

                Faded(remove);

                remove.Click += (s, e) =>
                {
                    Settings.FullscreenApps.Remove(app);
                    Settings.Save();
                    BuildAppList();
                };

                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);

                AddToCard(appList, Hoverable(row));
            }
        }

        // ================= About =================

        void BuildAboutPage()
        {
            Page("About", "", "Updates, starting with Windows, and where to find help.");

            var updates = Group("Updates");

            installButton = PillButton("Install", async () =>
            {
                installButton.IsEnabled = false;
                updateStatus.Text = "Downloading…";
                await island.InstallUpdateAsync();
            });
            installButton.Background = new SolidColorBrush(Color.FromRgb(10, 132, 255));
            installButton.Visibility = island.AvailableUpdate != null ? Visibility.Visible : Visibility.Collapsed;

            updateNotes = new StackPanel();
            Row(updates, $"Islet {UpdateService.CurrentVersion}", " ",
                Buttons(PillButton("Check now", CheckUpdatesAsync), installButton), updateNotes, out updateStatus);
            updateStatus.Text = island.AvailableUpdate is UpdateService.Release r
                ? $"Version {r.Version} is available."
                : "Updates are signed, and checked before they're installed.";
            ShowUpdateNotes(island.AvailableUpdate);

            // With no update waiting, what's new in the version installed
            if (island.AvailableUpdate == null)
                _ = ShowInstalledNotesAsync();

            SwitchRow(updates, "Check for updates automatically", "At launch and every 12 hours.",
                Settings.AutoUpdate, v => { Settings.AutoUpdate = v; Settings.Save(); });

            var windows = Group("Windows");
            SwitchRow(windows, "Launch at login", null, StartupShortcut.IsEnabled, v => StartupShortcut.Set(v));

            var help = Group("Help");
            Row(help, "Log", "Errors Islet ran into, for when something misbehaves. Never message contents.",
                PillButton("Open log folder", () =>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = AppSettings.Folder,
                        UseShellExecute = true
                    })));
            Row(help, "Islet on GitHub", "Release notes, the source, and where to report a problem.",
                PillButton("Open", () =>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/Prathambist/Islet",
                        UseShellExecute = true
                    })));
        }

        async Task CheckUpdatesAsync()
        {
            updateStatus.Text = "Checking…";
            var release = await island.CheckForUpdatesNowAsync();

            installButton.Visibility = release != null ? Visibility.Visible : Visibility.Collapsed;
            ShowUpdateNotes(release);
            updateStatus.Text = release != null
                ? $"Version {release.Version} is available." +
                  (UpdateService.CanSelfUpdate ? "" : " This copy wasn't installed by the installer, so Install opens the download page.")
                : "You're up to date.";
        }

        async Task ShowInstalledNotesAsync()
        {
            var version = UpdateService.CurrentVersion;
            var notes = await UpdateService.NotesForAsync(null, version);

            if (notes.Count > 0 && island.AvailableUpdate == null && IsLoaded)
                ShowUpdateNotes(new UpdateService.Release(version, $"v{version}", "", "", "") { Notes = notes });
        }

        // What's new in the update found, a short line each, under the update row
        void ShowUpdateNotes(UpdateService.Release? release)
        {
            updateNotes.Children.Clear();
            updateNotes.Visibility = release is { Notes.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

            if (release is not { Notes.Count: > 0 })
                return;

            updateNotes.Children.Add(new TextBlock
            {
                Text = $"What's new in {release.Version}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (string note in release.Notes)
            {
                var line = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                line.ColumnDefinitions.Add(new ColumnDefinition());

                var dot = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0) };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
                line.Children.Add(dot);

                var text = Note(note);
                Grid.SetColumn(text, 1);
                line.Children.Add(text);

                updateNotes.Children.Add(line);
            }
        }

        void UpdateNotifyStatus()
        {
            notifyStatus.Text = island.Notifications.Status switch
            {
                Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed =>
                    "Allowed. Islet can see new messages.",
                Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Denied =>
                    "Windows blocked it. Allow it under Privacy & security > Notifications.",
                _ => "Islet needs permission to see new messages."
            };
        }

        void UpdateSpotifyStatus()
        {
            bool connected = spotifyWeb.IsConnected;
            ((TextBlock)connectButton.Content).Text = connected ? "Disconnect" : "Connect";

            // Spotify's green to connect; a plain button once connected
            if (connected)
            {
                connectButton.SetResourceReference(BackgroundProperty, "PillBg");
                connectButton.SetResourceReference(ForegroundProperty, "Text");
            }
            else
            {
                connectButton.Background = new SolidColorBrush(Color.FromRgb(30, 215, 96));
                connectButton.Foreground = Brushes.Black;
            }
            spotifyStatus.Text = connected
                ? "Connected. The heart saves to your Liked Songs."
                : "Not connected.";
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
