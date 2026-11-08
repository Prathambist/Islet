using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Islet
{
    public partial class MainWindow : Window
    {
        sealed class Panel
        {
            public required FrameworkElement Element;
            public required Spring Opacity;
            public double MinScale;
        }

        readonly MediaController media = new();
        readonly SpotifyWebApi spotifyWeb = new();
        readonly AudioVisualizer visualizer = new();
        readonly VolumeController volume = new();
        readonly SystemStatusWatcher systemStatus = new();
        readonly VolumeKeyHook volumeKeys = new();

        static readonly Brush VolumeBrush = FrozenBrush(0x0A, 0x84, 0xFF);
        static readonly Brush BrightnessBrush = FrozenBrush(0xFF, 0xD6, 0x0A);
        static readonly Brush ConnectedBrush = FrozenBrush(0x30, 0xD1, 0x58);
        static readonly Brush DisconnectedBrush = FrozenBrush(0x8E, 0x8E, 0x93);

        static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
        readonly NotificationWatcher notifications = new();
        readonly FullscreenWatcher fullscreen;
        TrayIcon? tray;

        readonly DispatcherTimer musicTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer weatherTimer = new() { Interval = TimeSpan.FromMinutes(20) };
        readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(170) };
        readonly DispatcherTimer collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        readonly DispatcherTimer volumeTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
        readonly DispatcherTimer peekTimer = new() { Interval = TimeSpan.FromMilliseconds(2600) };
        readonly DispatcherTimer notifyTimer = new() { Interval = TimeSpan.FromMilliseconds(4500) };
        readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(12) };

        // Shape springs (SwiftUI-style response / damping)
        readonly Spring width = new(236);
        readonly Spring height = new(38);
        readonly Spring islandOpacity = new(1, 0.35, 1);
        readonly Spring islandScale = new(1, 0.45, 0.8);
        readonly Spring volumeLevel = new(0, 0.22, 0.9);
        readonly Spring volumeRow = new(0, 0.25, 1);
        readonly Spring glow = new(0, 0.5, 1);
        readonly Spring heartScale = new(1, 0.35, 0.42);
        readonly Spring seekGrow = new(0, 0.2, 1);
        readonly Spring artPop = new(1, 0.48, 0.58);
        readonly Spring peekLift = new(0, 0.5, 0.82);
        readonly Spring openRows = new(0, 0.5, 0.86);

        // Degrees per second, so the disc winds down and back up like a record
        readonly Spring discSpeed = new(0, 0.9, 1);

        readonly Spring[] springs;
        readonly List<Panel> panels = new();
        readonly Panel statusSmallPanel, statusRingPanel, compactMusicPanel, smallMusicPanel, miniMusicPanel, miniIdlePanel, compactAmbientPanel, volumePanel, notchPanel,
            peekMusicPanel, peekNotifyPanel, expandedMusicPanel, expandedAmbientPanel, timerPillPanel;

        readonly Rectangle[] compactBars;
        readonly Rectangle[] smallBars;
        readonly Rectangle[] expandedBars;
        readonly Rectangle[] peekBars;
        readonly float[] audioLevels = new float[AudioVisualizer.BandCount];
        readonly double[] barLevels = new double[AudioVisualizer.BandCount];
        readonly SolidColorBrush barBrush = new(Color.FromRgb(214, 200, 255));

        Surface surface = (Surface)(-1);
        bool rendering;
        TimeSpan lastFrame;
        double discAngle, orbAngle;

        TrackInfo? track;
        bool playing;
        DateTime? pausedSince;
        bool hovering;
        bool volumeVisible;
        bool updating;
        bool updatePending;

        string trackKey = "";

        WeatherService.Weather? weather;

        // Track-change peek
        bool peekVisible;

        // Real cover art found for music videos, per song so switching
        // between players or back to a song is instant
        readonly Dictionary<string, BitmapSource> covers = new();

        // Dragging on the progress bar
        bool seeking;
        double seekFraction;

        // Album accent, also used by the album-tint theme
        Color accentColor = AlbumPalette.Fallback;

        // A second colour from the cover, for the album-tint theme
        Color secondColor = AlbumPalette.Fallback;

        UpdateService.Release? availableUpdate;
        string? announcedUpdate;

        public NotificationWatcher Notifications => notifications;
        public UpdateService.Release? AvailableUpdate => availableUpdate;

        static AppSettings Settings => AppSettings.Current;

        public MainWindow()
        {
            InitializeComponent();

            springs = new[] { width, height, islandOpacity, islandScale, volumeLevel, volumeRow, glow, heartScale, seekGrow, discSpeed, artPop, peekLift, openRows, dockX, grab, dragLift, dragPop, stretch, lean, backdropLevel, shelfScroll };

            compactMusicPanel = AddPanel(CompactMusic, 0.88);
            smallMusicPanel = AddPanel(SmallMusic, 0.88);
            miniMusicPanel = AddPanel(MiniMusic, 0.7);
            miniIdlePanel = AddPanel(MiniIdle, 0.7);
            compactAmbientPanel = AddPanel(CompactAmbient, 0.88);
            volumePanel = AddPanel(CompactVolume, 0.88);
            statusSmallPanel = AddPanel(StatusSmall, 0.88);
            statusRingPanel = AddPanel(StatusRing, 0.7);
            notchPanel = AddPanel(NotchPanel, 0.9);
            peekMusicPanel = AddPanel(PeekMusic, 0.9, 0.3);
            peekNotifyPanel = AddPanel(PeekNotify, 0.9, 0.3);
            expandedMusicPanel = AddPanel(ExpandedMusic, 0.9, 0.42);
            expandedAmbientPanel = AddPanel(ExpandedAmbient, 0.9, 0.42);
            timerPillPanel = AddPanel(TimerPill, 0.88);
            shelfPanel = AddPanel(ShelfPanel, 0.9, 0.3);
            palettePanel = AddPanel(PalettePanel, 0.9, 0.3);
            locksPanel = AddPanel(LocksPanel, 0.9, 0.3);

            compactBars = CreateBars(CompactBars, 3, 1.5);
            smallBars = CreateBars(SmallBars, 2, 1);
            expandedBars = CreateBars(ExpandedBars, 3.5, 1.75);
            peekBars = CreateBars(PeekBars, 3, 1.5);
            ApplyOrbColor(accentColor);
            BuildLyricStage();
            trimTimer.Tick += (s, e) => TrimMemory();
            SetUpTimer();

            fullscreen = new FullscreenWatcher(() => new WindowInteropHelper(this).Handle);
            fullscreen.Changed += OnFullscreenChanged;
            fullscreen.ForegroundChanged += KeepOnTop;
            SetUpPrivacy();
            SetUpDock();
            SetUpShelf();
            SetUpColors();
            SetUpQuiet();
            SetUpScreenshots();
            SetUpLocks();

            systemStatus.VolumeChanged += (level, muted) => Dispatcher.BeginInvoke(() =>
            {
                if (Settings.ShowSystemStatus)
                    ShowLevel(level, muted, SystemStatus.VolumeGlyph(level, muted), VolumeBrush);
            });
            systemStatus.BrightnessChanged += level => Dispatcher.BeginInvoke(() =>
            {
                if (Settings.ShowSystemStatus)
                    ShowLevel(level / 100f, false, "", BrightnessBrush);
            });
            systemStatus.DeviceChanged += (name, change) => Dispatcher.BeginInvoke(() =>
            {
                if (Settings.ShowSystemStatus)
                    ShowDevice(name, change);
            });

            hoverTimer.Tick += (s, e) =>
            {
                hoverTimer.Stop();

                if (IslandContainer.IsMouseOver && !dragging && !volumeVisible && currentNotify == null && !TimerRinging)
                {
                    hovering = true;
                    UpdateSurface();
                }
            };

            collapseTimer.Tick += (s, e) =>
            {
                collapseTimer.Stop();

                if (seeking)
                {
                    collapseTimer.Start();
                    return;
                }

                if (!IslandContainer.IsMouseOver && IslandContainer.ContextMenu?.IsOpen != true)
                {
                    hovering = false;
                    UpdateSurface();
                }
            };

            volumeTimer.Tick += (s, e) =>
            {
                volumeTimer.Stop();
                volumeVisible = false;
                volumeRow.Target = 0;

                // Still hovering once the scroll gesture ends: open up as usual
                if (IslandContainer.IsMouseOver && !hovering)
                    hoverTimer.Start();

                UpdateSurface();
            };

            peekTimer.Tick += (s, e) =>
            {
                peekTimer.Stop();
                peekVisible = false;
                UpdateSurface();
            };

            notifyTimer.Tick += (s, e) =>
            {
                notifyTimer.Stop();

                // Keep it up while the pointer rests on it
                if (IslandContainer.IsMouseOver && currentNotify != null)
                    notifyTimer.Start();
                else
                    ShowNextNotification();
            };

            notifications.Received += EnqueueChat;
            updateTimer.Tick += async (s, e) => await CheckForUpdatesAsync(announce: true);
            App.SecondInstanceLaunched += OpenSettings;

            clockTimer.Tick += (s, e) =>
            {
                UpdateClock();
                UpdateSurface();

                // Without a frame loop in Low mode, the card's progress moves once a second
                if (!Profile.AnimatedBars)
                    UpdateProgress();
            };

            weatherTimer.Tick += async (s, e) => await RefreshWeatherAsync();

            media.MediaChanged += QueueTrackUpdate;
            media.PlaybackChanged += QueueTrackUpdate;
            spotifyWeb.ConnectionChanged += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                trackKey = "";
                QueueTrackUpdate();
            }));

            AppSettings.Changed += () => Dispatcher.BeginInvoke(new Action(ApplySettings));

            SourceInitialized += (s, e) => MakeToolWindow();
            Loaded += Window_Loaded;

            Closed += (s, e) =>
            {
                musicTimer.Stop();
                clockTimer.Stop();
                weatherTimer.Stop();
                fullscreen.Stop();
                privacy.Stop();
                volumeKeys.Dispose();
                systemStatus.Dispose();
                notifications.Stop();
                updateTimer.Stop();
                trimTimer.Stop();
                countdownTicker.Stop();
                alertFlasher.Stop();
                StopAlertSound();
                visualizer.Dispose();
                volume.Dispose();
                tray?.Dispose();
            };
        }

        Panel AddPanel(FrameworkElement element, double minScale, double response = 0.24)
        {
            var panel = new Panel
            {
                Element = element,
                Opacity = new Spring(0, response, 1),
                MinScale = minScale
            };

            element.Opacity = 0;
            element.Visibility = Visibility.Hidden;
            panels.Add(panel);
            return panel;
        }

        Rectangle[] CreateBars(StackPanel host, double barWidth, double gap)
        {
            var result = new Rectangle[AudioVisualizer.BandCount];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new Rectangle
                {
                    Width = barWidth,
                    Height = barWidth,
                    RadiusX = barWidth / 2,
                    RadiusY = barWidth / 2,
                    Fill = barBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(gap / 2, 0, gap / 2, 0)
                };

                host.Children.Add(result[i]);
            }

            return result;
        }

        // Keep the island out of Alt+Tab and never steal focus from games / editors
        void MakeToolWindow()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE,
                new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        }

        async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Top = 0;
            ApplyDock(snap: true);

            tray = new TrayIcon(fullscreen, countdown, OpenSettings, () => Close(),
                () => availableUpdate, () => _ = InstallUpdateAsync(), StartPicking);

            islandScale.Snap(SizeScale);

            UpdateClock();
            clockTimer.Start();
            fullscreen.Start();
            systemStatus.Start();
            volumeKeys.Start();
            ApplySettings();
            RestoreTimer();
            _ = RefreshWeatherAsync();
            weatherTimer.Start();

            if (Settings.AutoUpdate)
            {
                _ = Task.Delay(TimeSpan.FromSeconds(20))
                    .ContinueWith(_ => Dispatcher.BeginInvoke(new Action(async () =>
                        await CheckForUpdatesAsync(announce: true))));
                updateTimer.Start();
            }

            try
            {
                await media.ConnectAsync();
                await UpdateTrackAsync();

                musicTimer.Tick += async (s, args) =>
                {
                    await media.RefreshAsync();
                    await UpdateTrackAsync();
                };

                musicTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Media sessions unavailable", ex);
                UpdateSurface();
            }
        }

        static double SizeScale => Settings.Size switch
        {
            IslandSize.Small => 0.88,
            IslandSize.Large => 1.12,
            _ => 1.0
        };

        bool notificationsRequested;

        void ApplySettings()
        {
            ApplyPerformance();
            SyncVisualizer(surface);
            SyncPrivacy();
            SyncShelf();
            SyncColors();
            SyncDoNotDisturb();
            SyncScreenshots();
            ApplyDock();

            if (Settings.NotifyDiscord || Settings.NotifyWhatsApp || Settings.NotifyMessenger || Settings.NotifyBrowsers)
            {
                if (!notificationsRequested)
                {
                    notificationsRequested = true;
                    _ = notifications.StartAsync();
                }
            }
            else
            {
                notifications.Stop();
                notificationsRequested = false;
            }

            if (Settings.AutoUpdate) updateTimer.Start(); else updateTimer.Stop();

            // Switching video cover art on/off applies to the current song right away
            if (!Settings.FindVideoArt)
                covers.Clear();
            else if (track != null && !covers.ContainsKey(trackKey))
                _ = LoadCoverAsync(trackKey, track);

            if (track != null)
                ShowArt(EffectiveArt(track, trackKey));

            ApplyTheme();
            ApplyLyricsLayout();
            islandScale.Target = surface == Surface.Hidden ? 0.7 * SizeScale : SizeScale;
            EnsureRendering();

            UpdateClock();
            UpdateSurface();

            // Music source toggles may change which player is shown
            _ = media.RefreshAsync();
            QueueTrackUpdate();
        }

        void OnFullscreenChanged()
        {
            if (fullscreen.ProcessName != null)
                Settings.RememberApp(fullscreen.ProcessName, fullscreen.DisplayName ?? fullscreen.ProcessName);

            // So "the island vanished" can be traced back to the app that hid it
            Log.Info(fullscreen.ProcessName == null
                ? "No full-screen app in front"
                : $"Full-screen app in front: {fullscreen.ProcessName}" +
                  (Settings.ShouldHideFor(fullscreen.ProcessName) ? ", hiding the island" : ", island stays"));

            UpdateSurface();
            ShowPrivacy();
            ShowShelfPill();
            ShowColorPill();
        }

        // ================= State → shape =================

        bool IsIdle() => IslandState.IsIdle(track != null, playing, pausedSince, Settings.IdleDelaySeconds, DateTime.Now);

        Surface ChooseSurface()
        {
            bool idle = IsIdle();

            return IslandState.Choose(new IslandInputs
            {
                HiddenForApp = fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName),
                TimerActive = TimerActive,
                TimerRinging = TimerRinging,
                ShelfWanted = ShelfWanted,
                LocksWanted = LocksWanted,
                PaletteWanted = PaletteWanted,
                Hovering = hovering,
                HasTrack = track != null,
                Idle = idle,
                StatusShowing = volumeVisible,
                NotificationShowing = currentNotify != null,
                PeekShowing = peekVisible,
                IdleMode = Settings.IdleMode,
                MusicLook = Settings.MusicLook
            });
        }

        void UpdateSurface()
        {
            var next = ChooseSurface();

            // When the island can't appear, Windows keeps its own pop-up
            bool hiddenForApp = fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName);
            volumeKeys.Active = Settings.ShowSystemStatus && Settings.ReplaceVolumePopup && !hiddenForApp;
            SyncVisualizer(next);

            if (next == surface)
                return;

            bool wasExpanded = surface is Surface.ExpandedMusic or Surface.ExpandedAmbient;
            surface = next;
            ShowShelfPill();
            ShowColorPill();

            if (next != Surface.Hidden)
                KeepOnTop();
            var profile = Profile;
            RefreshAllTimerFaces();
            double quick = profile.SpringResponse;

            if (wasExpanded)
                ScheduleMemoryTrim();

            (double w, double h, Panel? active) = next switch
            {
                Surface.Hidden => (100.0, 28.0, null),
                Surface.Notch => (112.0, 30.0, notchPanel),
                Surface.SmallMusic => (112.0, 30.0, smallMusicPanel),
                Surface.MiniMusic => (38.0, 38.0, miniMusicPanel),
                Surface.MiniIdle => (30.0, 30.0, miniIdlePanel),
                Surface.CompactAmbient => (210.0, 38.0, compactAmbientPanel),
                Surface.Volume => (264.0, 38.0, volumePanel),
                Surface.StatusSmall => (150.0, 30.0, statusSmallPanel),
                Surface.StatusRing => (38.0, 38.0, statusRingPanel),
                Surface.Peek => (340.0, 64.0, peekMusicPanel),
                Surface.Notify => (370.0, 76.0, peekNotifyPanel),
                Surface.TimerPill => (112.0, 30.0, timerPillPanel),
                Surface.Shelf => (380.0, ShelfHeight, shelfPanel),
                Surface.Palette => (380.0, PaletteHeight, palettePanel),
                Surface.Locks => (380.0, LocksHeight, locksPanel),
                Surface.ExpandedMusic => (400.0, ExpandedMusic.Height, expandedMusicPanel),
                Surface.ExpandedAmbient => (400.0, ExpandedAmbient.Height, expandedAmbientPanel),
                _ => (236.0, 38.0, compactMusicPanel)
            };

            bool expanding = next is Surface.ExpandedMusic or Surface.ExpandedAmbient
                or Surface.Peek or Surface.Notify or Surface.Shelf or Surface.Palette or Surface.Locks;

            // Opening gets a little overshoot, closing settles calmly, and small
            // compact-to-compact morphs (volume, notch) get a lively bounce.
            if (expanding)
            {
                width.Configure(0.5 * quick, 0.74);
                height.Configure(0.5 * quick, 0.76);

                if (next == Surface.ExpandedMusic && !wasExpanded && profile.OpenStagger)
                {
                    openRows.Snap(1);
                    openRows.Target = 0;
                }
            }
            else if (wasExpanded)
            {
                width.Configure(0.42 * quick, 0.86);
                height.Configure(0.38 * quick, 0.9);
            }
            else
            {
                width.Configure(0.4 * quick, 0.68);
                height.Configure(0.4 * quick, 0.75);
            }

            width.Target = w;
            height.Target = h;

            foreach (var panel in panels)
                panel.Opacity.Target = panel == active ? 1 : 0;

            bool hidden = next == Surface.Hidden;
            islandOpacity.Target = hidden ? 0 : 1;
            islandScale.Target = (hidden ? 0.7 : 1) * SizeScale;
            glow.Target = !profile.Glow || track?.Artwork == null || Settings.Theme == IslandTheme.AlbumTint ? 0
                : next == Surface.ExpandedMusic ? 1
                : next == Surface.Peek && profile.SongFlair ? 0.55
                : 0;

            if (!hidden)
                IslandContainer.IsHitTestVisible = true;

            if (next == Surface.ExpandedMusic)
                Dispatcher.BeginInvoke(new Action(StartTitleMarquee), DispatcherPriority.Loaded);
            else
                StopTitleMarquee();

            EnsureRendering();
        }

        // ================= Frame loop =================

        int settleFrames;

        void EnsureRendering()
        {
            settleFrames = 2;

            if (rendering)
                return;

            rendering = true;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        void OnRendering(object? sender, EventArgs e)
        {
            var time = ((RenderingEventArgs)e).RenderingTime;

            if (time == lastFrame)
                return;

            bool shapeMoving = false;

            foreach (var spring in springs)
                shapeMoving |= !spring.IsSettled;

            foreach (var panel in panels)
                shapeMoving |= !panel.Opacity.IsSettled;

            if (shapeMoving)
                settleFrames = 2;

            // Bars and spinning art can run slower; anything changing shape never does
            int fps = Profile.ContinuousFps;

            if (settleFrames == 0 && fps > 0 && lastFrame != TimeSpan.Zero &&
                (time - lastFrame).TotalSeconds < 1.0 / fps - 0.004)
                return;

            double dt = lastFrame == TimeSpan.Zero
                ? 1.0 / 60
                : Math.Clamp((time - lastFrame).TotalSeconds, 0, 0.05);

            lastFrame = time;

            bool busy = false;

            busy |= StepDrag(dt);
            busy |= UpdateBars(dt);
            busy |= UpdateProgress();
            busy |= StepStage(dt);
            busy |= UpdateMini(dt);
            busy |= StepTimer();
            busy |= StepAlert();

            // Nothing below changes once the springs rest, and it isn't free to redo
            if (settleFrames == 0)
            {
                if (!busy)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    rendering = false;
                }

                return;
            }

            settleFrames--;
            busy = busy || settleFrames > 0;

            foreach (var spring in springs)
            {
                spring.Step(dt);
                busy |= !spring.IsSettled;
            }

            ApplyShape();

            foreach (var panel in panels)
            {
                panel.Opacity.Step(dt);
                busy |= !panel.Opacity.IsSettled;
                ApplyPanel(panel);
            }

            ApplyFlair();

            double trackWidth = Math.Max(0, CompactVolumeFill.Parent is FrameworkElement f ? f.ActualWidth : 0);
            CompactVolumeFill.Width = trackWidth * Math.Clamp(volumeLevel.Value, 0, 1);
            VolumeRowFill.Width = VolumeTrack.ActualWidth * Math.Clamp(volumeLevel.Value, 0, 1);
            StatusSmallFill.Width = StatusSmallTrack.ActualWidth * Math.Clamp(volumeLevel.Value, 0, 1);
            UpdateStatusRing(Math.Clamp(volumeLevel.Value, 0, 1));
            VolumeRow.Opacity = volumeRow.Value;
            ProgressRow.Opacity = 1 - volumeRow.Value;

            ExpandedGlow.Opacity = glow.Value;
            ProgressBar.Height = 5 + 3 * seekGrow.Value;
            HeartScale.ScaleX = HeartScale.ScaleY = heartScale.Value;

            if (!busy)
            {
                CompositionTarget.Rendering -= OnRendering;
                rendering = false;
            }
        }

        void ApplyShape()
        {
            double w = Math.Max(1, width.Value);
            double h = Math.Max(1, height.Value);

            IslandContainer.Width = w;
            IslandContainer.Height = h;

            // Full capsule when small, opening into a continuous rounded card
            double radius = GlyphPattern.IslandRadius(w, h);

            IslandShape.CornerRadius = new CornerRadius(radius);

            var clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
            clip.Freeze();
            IslandContent.Clip = clip;

            IslandContainer.Opacity = Math.Clamp(islandOpacity.Value, 0, 1);
            PlaceIsland(w);

            if (surface == Surface.Hidden && islandOpacity.Value < 0.02)
                IslandContainer.IsHitTestVisible = false;

            PlacePrivacy();
            ApplyShelf();
            PlaceColorPill();
        }

        static void ApplyPanel(Panel panel)
        {
            double opacity = Math.Clamp(panel.Opacity.Value, 0, 1);
            var element = panel.Element;

            element.Opacity = opacity;
            element.Visibility = opacity > 0.003 ? Visibility.Visible : Visibility.Hidden;
            element.IsHitTestVisible = opacity > 0.6;

            if (element.RenderTransform is ScaleTransform scale)
            {
                double s = panel.MinScale + (1 - panel.MinScale) * opacity;
                scale.ScaleX = scale.ScaleY = s;
            }
        }

        bool UpdateBars(double dt)
        {
            bool visible = compactMusicPanel.Element.Visibility == Visibility.Visible ||
                           smallMusicPanel.Element.Visibility == Visibility.Visible ||
                           expandedMusicPanel.Element.Visibility == Visibility.Visible ||
                           peekMusicPanel.Element.Visibility == Visibility.Visible;

            if (!visible)
                return false;

            if (!Profile.AnimatedBars)
                return ApplyStillBars();

            barsStill = false;

            bool real = Settings.RealAudioVisualizer && visualizer.IsRunning;

            if (!playing)
                Array.Clear(audioLevels);
            else if (real)
                visualizer.GetLevels(audioLevels);
            else
                SyntheticLevels(lastFrame.TotalSeconds);

            bool moving = playing;

            for (int i = 0; i < barLevels.Length; i++)
            {
                double target = audioLevels[i];

                // Fast attack, softer release, like a real VU meter
                double rate = target > barLevels[i] ? 38 : 11;
                barLevels[i] += (target - barLevels[i]) * (1 - Math.Exp(-rate * dt));
                moving |= barLevels[i] > 0.002;

                SetBarHeights(i, barLevels[i]);
            }

            return moving;
        }

        // Spins the mini views; time-based, so a slow frame never makes them stutter
        bool UpdateMini(double dt)
        {
            bool spin = Profile.SpinViews;

            discSpeed.Target = playing && spin ? 42 : 0;

            bool disc = miniMusicPanel.Element.Visibility == Visibility.Visible;
            bool orb = miniIdlePanel.Element.Visibility == Visibility.Visible && !TimerActive;

            if (disc)
            {
                discAngle = (discAngle + discSpeed.Value * dt) % 360;
                DiscRotate.Angle = discAngle;
            }

            if (orb && !spin)
            {
                OrbDotScale.ScaleX = OrbDotScale.ScaleY = 1;
                orb = false;
            }

            if (orb)
            {
                orbAngle = (orbAngle + 75 * dt) % 360;
                OrbRotate.Angle = orbAngle;

                double breath = 1 + 0.22 * Math.Sin(lastFrame.TotalSeconds * 1.8);
                OrbDotScale.ScaleX = OrbDotScale.ScaleY = breath;
            }

            return orb || (disc && discSpeed.Value > 0.05);
        }

        void ApplyOrbColor(Color color)
        {
            var dot = new SolidColorBrush(color);
            dot.Freeze();
            OrbDot.Fill = dot;

            // Fades around the ring, so turning it reads as a soft comet
            var ring = new LinearGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B), 90);
            ring.Freeze();
            OrbRing.Stroke = ring;
        }

        // Used when loopback capture is off or unavailable
        void SyntheticLevels(double t)
        {
            for (int i = 0; i < audioLevels.Length; i++)
            {
                double v = 0.45 +
                           0.3 * Math.Sin(t * (5.1 + i * 1.7) + i * 1.3) +
                           0.2 * Math.Sin(t * (8.3 + i * 0.9) + i * 2.1);
                audioLevels[i] = (float)Math.Clamp(v, 0.05, 1);
            }
        }

        bool UpdateProgress()
        {
            if (track == null || expandedMusicPanel.Element.Visibility != Visibility.Visible)
                return false;

            var duration = track.Duration;

            if (duration <= TimeSpan.Zero)
            {
                ProgressFill.Width = 0;
                ElapsedText.Text = "";
                RemainingText.Text = "";
                return false;
            }

            var position = track.PositionAt(DateTimeOffset.Now);

            if (seeking)
                position = duration * seekFraction;

            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > duration) position = duration;

            UpdateLyric(position);

            ProgressFill.Width = ProgressTrack.ActualWidth * (position / duration);

            string elapsed = FormatTime(position);
            string remaining = "-" + FormatTime(duration - position);

            if (ElapsedText.Text != elapsed) ElapsedText.Text = elapsed;
            if (RemainingText.Text != remaining) RemainingText.Text = remaining;

            return seeking || (playing && Profile.AnimatedBars);
        }

        static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        // ================= Hover / scroll =================

        void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            collapseTimer.Stop();

            if (!hovering && !volumeVisible)
                hoverTimer.Start();
        }

        void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            hoverTimer.Stop();
            collapseTimer.Stop();
            collapseTimer.Start();
        }

        void Island_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // On the shelf, the wheel looks through the files
            if (surface == Surface.Shelf)
            {
                e.Handled = true;
                ScrollShelf(e.Delta);
                return;
            }

            if (!Settings.ScrollVolumeEnabled)
                return;

            e.Handled = true;

            // 2% per wheel notch; precision touchpads send smaller deltas
            var result = volume.Change(e.Delta / 120f * 0.02f, Settings.ScrollTarget, track?.ProcessName);

            if (result == null)
                return;

            var (level, muted, _) = result.Value;
            ShowLevel(level, muted, SystemStatus.VolumeGlyph(level, muted), VolumeBrush);
        }

        // ================= Volume / brightness / devices =================

        // Display only: the level belongs to Windows, the island just shows it
        void ShowLevel(float level, bool muted, string glyph, Brush color)
        {
            SetStatusContent(glyph, color, device: false);

            // A level change shows the status view instead of opening the island
            hoverTimer.Stop();
            volumeLevel.Target = muted ? 0 : level;

            if (hovering)
                volumeRow.Target = 1;

            VolumeRowGlyph.Text = glyph;
            VolumeRowFill.Background = color;
            CompactVolumeFill.Background = color;
            StatusSmallFill.Background = color;
            CompactVolumeText.Text = ((int)Math.Round(level * 100)).ToString();
            VolumeRowText.Text = (int)Math.Round(level * 100) + "%";

            RevealStatus(TimeSpan.FromMilliseconds(1400));
        }

        void ShowDevice(string name, DeviceChange change)
        {
            string glyph = change == DeviceChange.AudioOutput ? "\uE7F6" : "\uE702";
            var color = change == DeviceChange.Disconnected ? DisconnectedBrush : ConnectedBrush;

            SetStatusContent(glyph, color, device: true);

            hoverTimer.Stop();
            volumeLevel.Target = 1;
            CompactDeviceName.Text = name;
            CompactDeviceState.Text = SystemStatus.DeviceText(change);
            StatusSmallName.Text = name;

            RevealStatus(TimeSpan.FromMilliseconds(2600));
        }

        void SetStatusContent(string glyph, Brush color, bool device)
        {
            var level = device ? Visibility.Collapsed : Visibility.Visible;
            var details = device ? Visibility.Visible : Visibility.Collapsed;

            CompactVolumeGlyph.Text = glyph;
            CompactVolumeGlyph.Foreground = color;
            CompactVolumeTrack.Visibility = level;
            CompactVolumeText.Visibility = level;
            CompactDeviceName.Visibility = details;
            CompactDeviceState.Visibility = details;

            StatusSmallGlyph.Text = glyph;
            StatusSmallGlyph.Foreground = color;
            StatusSmallTrack.Visibility = level;
            StatusSmallName.Visibility = details;

            StatusRingGlyph.Text = glyph;
            StatusRingGlyph.Foreground = color;
            StatusRingFill.Stroke = color;
        }

        void RevealStatus(TimeSpan duration)
        {
            volumeVisible = true;
            volumeTimer.Stop();
            volumeTimer.Interval = duration;
            volumeTimer.Start();

            UpdateSurface();
            EnsureRendering();
        }

        double statusRingShown = -1;

        void UpdateStatusRing(double level)
        {
            if (Math.Abs(level - statusRingShown) < 0.002)
                return;

            statusRingShown = level;
            StatusRingFill.Visibility = level < 0.005 ? Visibility.Hidden : Visibility.Visible;

            // Dash lengths count in stroke widths; the stroke's center line is what gets dashed
            double around = Math.PI * (StatusRingFill.Width - StatusRingFill.StrokeThickness) / StatusRingFill.StrokeThickness;
            StatusRingFill.StrokeDashArray = level >= 0.995
                ? null
                : new DoubleCollection { around * level, around * 2 };
        }

        // ================= Theme =================

        static Color Darken(Color color, double factor) =>
            Color.FromRgb((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

        static Color Brighten(Color color, double amount) =>
            Color.FromRgb(
                (byte)(color.R + (255 - color.R) * amount),
                (byte)(color.G + (255 - color.G) * amount),
                (byte)(color.B + (255 - color.B) * amount));

        void ApplyAlbumTheme(BitmapSource? artwork)
        {
            var (accent, second) = AlbumPalette.From(artwork);
            var vivid = Brighten(accent, 0.35);

            accentColor = accent;
            secondColor = second;
            ApplyOrbColor(vivid);

            if (Settings.Theme == IslandTheme.AlbumTint)
                ApplyTheme();

            barBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                To = vivid,
                Duration = TimeSpan.FromMilliseconds(450),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });

            var deep = Darken(accent, 0.55);
            var glowBrush = new RadialGradientBrush
            {
                Center = new Point(0.12, 0.05),
                GradientOrigin = new Point(0.12, 0.05),
                RadiusX = 0.9,
                RadiusY = 1.3
            };

            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, deep.R, deep.G, deep.B), 0));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, deep.R, deep.G, deep.B), 0.5));
            glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            glowBrush.Freeze();

            ExpandedGlow.Background = glowBrush;
        }

        // ================= Title marquee =================

        void StartTitleMarquee()
        {
            StopTitleMarquee();

            if (surface != Surface.ExpandedMusic || TitleClip.ActualWidth <= 0 || !Profile.Marquee)
                return;

            TrackTitle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double overflow = TrackTitle.DesiredSize.Width - TitleClip.ActualWidth;

            if (overflow <= 4)
                return;

            var animation = new DoubleAnimation
            {
                From = 0,
                To = -overflow,
                BeginTime = TimeSpan.FromMilliseconds(900),
                Duration = TimeSpan.FromSeconds(Math.Max(2.5, overflow / 28)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            TitleTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        void StopTitleMarquee()
        {
            TitleTransform.BeginAnimation(TranslateTransform.XProperty, null);
            TitleTransform.X = 0;
        }

        // ================= Track =================

        void QueueTrackUpdate()
        {
            Dispatcher.BeginInvoke(new Action(async () => await UpdateTrackAsync()));
        }

        async Task UpdateTrackAsync()
        {
            // An event that arrives mid-update (e.g. while switching players) must
            // not be lost, or the island shows the old player until the next poll
            if (updating)
            {
                updatePending = true;
                return;
            }

            updating = true;

            try
            {
                var next = await media.GetTrackAsync();
                bool hadTrack = track != null;
                bool hadTitle = hadTrack && track!.Title != MediaController.UnknownTitle;
                track = next;

                if (next == null)
                {
                    TrackTitle.Text = "Not playing";
                    TrackArtist.Text = "Play something to get started";
                    CompactAlbumArt.Source = null;
                    SmallAlbumArt.Source = null;
                    AlbumArt.Source = null;
                    DiscArt.Fill = null;
                    playing = false;
                    pausedSince = null;
                    trackKey = "";
                    likeUri = null;
                    lyrics = null;
                    ApplyLyricsLayout();
                    SetLiked(false, animate: false);
                    UpdateSourceSlot();

                    if (hadTrack)
                        ApplyAlbumTheme(null);

                    UpdateSurface();
                    return;
                }

                string key = next.SourceName + "|" + next.Title + "|" + next.Artist;
                bool trackChanged = key != trackKey;

                TrackTitle.Text = next.Title;
                TrackArtist.Text = next.Artist;
                PlayPauseButton.Content = next.Playing ? "\uE769" : "\uE768";
                UpdatePlaybackModes(next);

                var art = EffectiveArt(next, key);

                if (trackChanged)
                    ApplyAlbumTheme(art);

                ShowArt(art);

                if (next.Playing)
                    pausedSince = null;
                else if (playing || pausedSince == null)
                    pausedSince = DateTime.Now;

                playing = next.Playing;

                if (trackChanged)
                {
                    bool hasTitle = next.Title != MediaController.UnknownTitle;
                    trackKey = key;
                    ScheduleMemoryTrim();

                    // iOS-style "now playing" peek when the song changes by itself
                    if (hadTitle && hasTitle)
                        PlaySongFlair();

                    if (hadTitle && hasTitle && next.Playing && !hovering && Profile.SongPeek)
                    {
                        PeekArt.Source = art;
                        PeekTitle.Text = next.Title;
                        PeekArtist.Text = next.Artist;
                        peekVisible = true;
                        peekTimer.Stop();
                        peekTimer.Start();
                    }

                    lyrics = null;
                    ResetLyric();
                    ApplyLyricsLayout();

                    // Nothing to look up yet; the real title arrives as its own change
                    if (hasTitle)
                    {
                        _ = LoadLyricsAsync(key, next);
                        _ = LoadCoverAsync(key, next);
                    }

                    likeUri = null;
                    SetLiked(false, animate: false);
                    UpdateSourceSlot();
                    _ = RefreshLikedAsync(key, next);

                    if (surface == Surface.ExpandedMusic)
                        _ = Dispatcher.BeginInvoke(new Action(StartTitleMarquee), DispatcherPriority.Loaded);
                }

                UpdateSurface();
                EnsureRendering();
            }
            catch (Exception ex)
            {
                Log.Error("Reading the media session failed", ex);
                TrackTitle.Text = "Player unavailable";
                TrackArtist.Text = "Couldn't read what's playing";
            }
            finally
            {
                updating = false;

                if (updatePending)
                {
                    updatePending = false;
                    QueueTrackUpdate();
                }
            }
        }

        // ================= Cover art for videos =================

        async Task LoadCoverAsync(string key, TrackInfo info)
        {
            if (!Settings.FindVideoArt || info.Source != MediaSource.Browser || covers.ContainsKey(key))
                return;

            // Look up straight away; whether it's needed is decided when showing
            var found = await ArtworkService.FindAsync(info.Artist, info.Title, info.SourceName);

            if (found == null)
                return;

            if (covers.Count > 200)
                covers.Clear();

            covers[key] = found;

            if (key == trackKey && track != null)
            {
                var art = EffectiveArt(track, key);

                if (art == found)
                    ApplyAlbumTheme(found);

                ShowArt(art);
            }
        }

        /// <summary>
        /// The player's own artwork when it's a real cover (Spotify, Apple Music,
        /// YouTube Music); otherwise a looked-up cover for music videos.
        /// </summary>
        BitmapSource? EffectiveArt(TrackInfo info, string key)
        {
            bool ownIsCover = info.Artwork != null &&
                              (info.Source != MediaSource.Browser || ArtworkService.IsSquare(info.Artwork));

            if (ownIsCover)
                return info.Artwork;

            return Settings.FindVideoArt && covers.TryGetValue(key, out var found) ? found : info.Artwork;
        }

        void ShowArt(BitmapSource? art)
        {
            if (CompactAlbumArt.Source == art)
                return;

            CompactAlbumArt.Source = art;
            SmallAlbumArt.Source = art;
            AlbumArt.Source = art;
            PeekArt.Source = art;
            DiscArt.Fill = art == null ? null : new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            ApplyAlbumTheme(art);
            UpdateCoverBackdrop();

            if (surface == Surface.ExpandedMusic)
            {
                glow.Target = Profile.Glow && art != null && Settings.Theme != IslandTheme.AlbumTint ? 1 : 0;
                EnsureRendering();
            }
        }

        // ================= Seeking =================

        double SeekFractionAt(MouseEventArgs e) =>
            ProgressTrack.ActualWidth <= 0
                ? 0
                : Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);

        void Seek_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (track == null || track.Duration <= TimeSpan.Zero)
                return;

            seeking = true;
            seekFraction = SeekFractionAt(e);
            seekGrow.Target = 1;
            ProgressTrack.CaptureMouse();
            EnsureRendering();
            e.Handled = true;
        }

        void Seek_MouseMove(object sender, MouseEventArgs e)
        {
            if (!seeking)
                return;

            seekFraction = SeekFractionAt(e);
            EnsureRendering();
        }

        async void Seek_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!seeking || track == null)
                return;

            seekFraction = SeekFractionAt(e);
            seeking = false;
            seekGrow.Target = 0;
            ProgressTrack.ReleaseMouseCapture();
            e.Handled = true;

            await SeekToAsync(track.Duration * seekFraction);
        }

        async Task SeekToAsync(TimeSpan target)
        {
            if (track == null)
                return;

            // Move the bar right away; the player confirms a moment later
            track = track with { Position = target, PositionUpdated = DateTimeOffset.Now };
            ResetLyric();
            EnsureRendering();

            try
            {
                if (!await media.SeekAsync(target))
                    Log.Info("Player refused to seek");
            }
            catch (Exception ex)
            {
                Log.Error("Seek failed", ex);
            }
        }

        void Seek_LostCapture(object sender, MouseEventArgs e)
        {
            if (!seeking)
                return;

            seeking = false;
            seekGrow.Target = 0;
            EnsureRendering();
        }

        // ================= Themes =================

        void ApplyTheme()
        {
            Brush background = Settings.Theme switch
            {
                IslandTheme.Graphite => new SolidColorBrush(Color.FromRgb(28, 28, 30)),
                IslandTheme.Translucent => new SolidColorBrush(Color.FromArgb(208, 20, 20, 22)),
                IslandTheme.AlbumTint => AlbumTintBrush(accentColor, secondColor),
                _ => Brushes.Black
            };

            if (background.CanFreeze)
                background.Freeze();

            IslandShape.Background = background;
        }

        // Runs from the cover's main colour, beside the art, to its second colour by the bars;
        // the same in every shape, since it scales with it. The two meet where both are
        // darkest, so very different colours don't pass through a grey band.
        static Brush AlbumTintBrush(Color primary, Color secondary)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };

            brush.GradientStops.Add(new GradientStop(AlbumPalette.Tone(primary, 0.26), 0));
            brush.GradientStops.Add(new GradientStop(AlbumPalette.Tone(primary, 0.14), 0.45));
            brush.GradientStops.Add(new GradientStop(AlbumPalette.Tone(secondary, 0.09), 0.68));
            brush.GradientStops.Add(new GradientStop(AlbumPalette.Tone(secondary, 0.19), 1));
            return brush;
        }

        // ================= Updates =================

        async Task CheckForUpdatesAsync(bool announce)
        {
            availableUpdate = await UpdateService.CheckAsync();

            if (availableUpdate == null || !announce || announcedUpdate == availableUpdate.Tag)
                return;

            announcedUpdate = availableUpdate.Tag;

            Enqueue(new NotifyItem(
                "Islet",
                "Update available",
                $"Version {availableUpdate.Version} is ready. Click to install.",
                null,
                Color.FromRgb(10, 132, 255),
                0xE896,
                () => _ = InstallUpdateAsync()));
        }

        public async Task<UpdateService.Release?> CheckForUpdatesNowAsync()
        {
            await CheckForUpdatesAsync(announce: false);
            return availableUpdate;
        }

        public async Task InstallUpdateAsync()
        {
            if (availableUpdate == null)
                return;

            try
            {
                await UpdateService.InstallAsync(availableUpdate);

                if (UpdateService.CanSelfUpdate)
                    Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error("Installing the update failed", ex);

                Enqueue(new NotifyItem(
                    "Islet",
                    "Update failed",
                    "Click to download it from GitHub instead.",
                    null,
                    Color.FromRgb(255, 69, 58),
                    0xE783,
                    () => OpenUrl(availableUpdate.PageUrl)));
            }
        }

        // ================= Clock / weather =================

        void UpdateClock()
        {
            var now = DateTime.Now;
            string time = now.ToString(Settings.Use24HourClock ? "HH:mm" : "h:mm");

            CompactTime.Text = time;
            AmbientTime.Text = time;
            AmbientDate.Text = now.ToString("dddd, MMMM d");

            bool showWeather = Settings.IdleMode == IdleMode.ClockWeather && weather != null;

            CompactAmbientGlyph.Text = showWeather ? weather!.Glyph : "";
            CompactAmbientGlyph.Visibility = showWeather ? Visibility.Visible : Visibility.Collapsed;
            CompactAmbientText.Text = showWeather
                ? Math.Round(weather!.Temperature) + "°"
                : now.ToString("ddd d");

            AmbientWeather.Visibility = weather != null ? Visibility.Visible : Visibility.Collapsed;

            // The idle circle swaps its dot for the temperature and widens its ring to fit
            OrbTemp.Text = weather != null ? Math.Round(weather.Temperature) + "°" : "";
            OrbTemp.Visibility = weather != null ? Visibility.Visible : Visibility.Collapsed;
            OrbDot.Visibility = weather != null ? Visibility.Collapsed : Visibility.Visible;
            OrbRingHolder.Width = OrbRingHolder.Height = weather != null ? 27 : 20;

            if (weather != null)
            {
                AmbientWeatherGlyph.Text = weather.Glyph;
                AmbientTemp.Text = Math.Round(weather.Temperature) + "°";
                AmbientWeatherText.Text = string.IsNullOrWhiteSpace(Settings.WeatherCity)
                    ? weather.Description
                    : weather.Description + " · " + Settings.WeatherCity;
            }
        }

        public async Task RefreshWeatherAsync()
        {
            if (Settings.WeatherLatitude is not double lat || Settings.WeatherLongitude is not double lon)
            {
                weather = null;
            }
            else
            {
                weather = await WeatherService.GetCurrentAsync(lat, lon, Settings.UseFahrenheit) ?? weather;
            }

            UpdateClock();
        }

        // ================= Actions =================

        SettingsWindow? settingsWindow;

        void OpenSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow(spotifyWeb, this);
                settingsWindow.Closed += (s, e) => settingsWindow = null;
                settingsWindow.Show();
            }

            if (settingsWindow.WindowState == WindowState.Minimized)
                settingsWindow.WindowState = WindowState.Normal;

            // Bring it in front even when another app has focus
            settingsWindow.Topmost = true;
            settingsWindow.Activate();
            settingsWindow.Topmost = false;
        }

        void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        void Quit_Click(object sender, RoutedEventArgs e) => Close();

        static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch
            {
            }
        }

        void OpenSpotifySearch(string query)
        {
            if (!string.IsNullOrWhiteSpace(query))
                OpenUrl("https://open.spotify.com/search/" + Uri.EscapeDataString(query));
        }

        void OpenSpotify_Click(object sender, RoutedEventArgs e) => OpenUrl("spotify:");

        bool IsSpotifyTrack => track?.Source == MediaSource.Spotify || likeUri != null;

        void TrackTitle_Click(object sender, MouseButtonEventArgs e)
        {
            if (track == null || !IsSpotifyTrack)
                return;

            if (!string.IsNullOrWhiteSpace(track.Album))
                OpenSpotifySearch("album:" + track.Album + " artist:" + track.Artist);
            else
                OpenSpotifySearch(track.Title + " " + track.Artist);
        }

        void TrackArtist_Click(object sender, MouseButtonEventArgs e)
        {
            if (track != null && IsSpotifyTrack)
                OpenSpotifySearch("artist:" + track.Artist);
        }

        async void Previous_Click(object sender, RoutedEventArgs e)
        {
            await media.PreviousAsync();
            await UpdateTrackAsync();
        }

        async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            await media.TogglePlayPauseAsync();
            await UpdateTrackAsync();
        }

        async void Next_Click(object sender, RoutedEventArgs e)
        {
            await media.NextAsync();
            await UpdateTrackAsync();
        }

        // Windows sometimes moves an always-on-top window down among the normal ones
        // (after Show desktop, an Explorer restart, or another app re-stacking windows)
        // while it keeps the flag, so the island ends up behind apps. Put it back on top.
        void KeepOnTop()
        {
            if (fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName))
                return;

            var hwnd = new WindowInteropHelper(this).Handle;

            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        const int GWL_EXSTYLE = -20;
        const long WS_EX_TOOLWINDOW = 0x00000080;
        const long WS_EX_NOACTIVATE = 0x08000000;
        static readonly IntPtr HWND_TOPMOST = new(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_NOOWNERZORDER = 0x0200;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    }
}
