using System.Drawing;
using Forms = System.Windows.Forms;

namespace Islet
{
    /// <summary>
    /// Notification-area icon: quick access to Settings and to the full-screen
    /// rule for whatever app is currently full-screen.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly FullscreenWatcher fullscreen;
        readonly CountdownTimer countdown;
        readonly Action openSettings;
        readonly Action quit;
        readonly Func<UpdateService.Release?> update;
        readonly Action installUpdate;
        readonly Action pickColor;
        readonly Icon image;

        static AppSettings Settings => AppSettings.Current;

        public TrayIcon(FullscreenWatcher fullscreen, CountdownTimer countdown, Action openSettings, Action quit,
            Func<UpdateService.Release?> update, Action installUpdate, Action pickColor)
        {
            this.pickColor = pickColor;
            this.fullscreen = fullscreen;
            this.countdown = countdown;
            this.openSettings = openSettings;
            this.quit = quit;
            this.update = update;
            this.installUpdate = installUpdate;

            image = LoadIcon();

            icon = new Forms.NotifyIcon
            {
                Icon = image,
                Text = "Islet",
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };

            icon.ContextMenuStrip.Opening += (s, e) => BuildMenu();
            icon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                    openSettings();
            };

            BuildMenu();
        }

        void BuildMenu()
        {
            var menu = icon.ContextMenuStrip!;
            menu.Items.Clear();

            if (update() is UpdateService.Release release)
            {
                var install = new Forms.ToolStripMenuItem($"Install update ({release.Tag})")
                {
                    Font = new Font(menu.Font, FontStyle.Bold)
                };
                install.Click += (s, e) => installUpdate();
                menu.Items.Add(install);
                menu.Items.Add(new Forms.ToolStripSeparator());
            }

            menu.Items.Add(BuildTimerMenu(menu));
            menu.Items.Add("Pick a colour…", null, (s, e) => pickColor());
            menu.Items.Add("Settings…", null, (s, e) => openSettings());

            if (Math.Abs(Settings.IslandX - 0.5) > 0.001)
            {
                menu.Items.Add("Move the island back to the centre", null, (s, e) =>
                {
                    Settings.IslandX = 0.5;
                    Settings.Save();
                });
            }

            menu.Items.Add(new Forms.ToolStripSeparator());

            var hideAll = new Forms.ToolStripMenuItem("Hide in full-screen apps")
            {
                Checked = Settings.HideInFullscreen
            };

            hideAll.Click += (s, e) =>
            {
                Settings.HideInFullscreen = !Settings.HideInFullscreen;
                Settings.Save();
            };

            menu.Items.Add(hideAll);

            // One-click override for the app that is full-screen right now
            if (fullscreen.ProcessName is string process)
            {
                var app = Settings.RememberApp(process, fullscreen.DisplayName ?? process);
                var sub = new Forms.ToolStripMenuItem($"When {app.DisplayName} is full-screen");

                void Add(string label, FullscreenRule rule)
                {
                    var item = new Forms.ToolStripMenuItem(label) { Checked = app.Rule == rule };
                    item.Click += (s, e) =>
                    {
                        app.Rule = rule;
                        Settings.Save();
                    };
                    sub.DropDownItems.Add(item);
                }

                Add("Use default", FullscreenRule.Default);
                Add("Always hide the island", FullscreenRule.Hide);
                Add("Always show the island", FullscreenRule.Show);
                menu.Items.Add(sub);
            }

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit Islet", null, (s, e) => quit());
        }

        Forms.ToolStripMenuItem BuildTimerMenu(Forms.ContextMenuStrip menu)
        {
            string title = countdown.State switch
            {
                CountdownState.Running => $"Timer ({TimerText.Clock(countdown.Remaining)})",
                CountdownState.Paused => $"Timer (paused, {TimerText.Clock(countdown.Remaining)})",
                CountdownState.Ringing => "Timer (finished)",
                _ => "Timer"
            };

            var timer = new Forms.ToolStripMenuItem(title);

            void Add(string label, Action action) =>
                timer.DropDownItems.Add(label, null, (s, e) => action());

            switch (countdown.State)
            {
                case CountdownState.Idle:
                    Add("Focus for 25 min (messages wait)", () => countdown.Start(TimerCard.FocusLength, focus: true));
                    timer.DropDownItems.Add(new Forms.ToolStripSeparator());

                    foreach (int minutes in new[] { 1, 3, 5, 10, 15, 25, 30, 45, 60 })
                        Add($"{minutes} min", () => countdown.Start(TimeSpan.FromMinutes(minutes)));

                    timer.DropDownItems.Add(new Forms.ToolStripSeparator());
                    timer.DropDownItems.Add(new Forms.ToolStripLabel("Custom, like 12, 1:30 or 1h30, then Enter:"));

                    var custom = new Forms.ToolStripTextBox();
                    custom.KeyDown += (s, e) =>
                    {
                        if (e.KeyCode != Forms.Keys.Enter)
                            return;

                        e.SuppressKeyPress = true;

                        if (TimerText.TryParse(custom.Text, out var duration))
                        {
                            countdown.Start(duration);
                            menu.Close();
                        }
                        else
                        {
                            custom.SelectAll();
                        }
                    };

                    timer.DropDownItems.Add(custom);
                    break;

                case CountdownState.Ringing:
                    Add("Dismiss", countdown.Stop);
                    Add("Add 1 min", () => countdown.AddTime(TimeSpan.FromMinutes(1)));
                    Add("Add 5 min", () => countdown.AddTime(TimeSpan.FromMinutes(5)));
                    break;

                default:
                    if (countdown.State == CountdownState.Paused)
                        Add("Resume", countdown.Resume);
                    else
                        Add("Pause", countdown.Pause);

                    Add("Add 1 min", () => countdown.AddTime(TimeSpan.FromMinutes(1)));
                    Add("Add 5 min", () => countdown.AddTime(TimeSpan.FromMinutes(5)));
                    Add("Cancel", countdown.Stop);
                    break;
            }

            timer.DropDownItems.Add(new Forms.ToolStripSeparator());

            var sound = new Forms.ToolStripMenuItem("Play sound") { Checked = Settings.TimerSound };
            sound.Click += (s, e) =>
            {
                Settings.TimerSound = !Settings.TimerSound;
                Settings.Save();
            };
            timer.DropDownItems.Add(sound);

            return timer;
        }

        // The size Windows wants for the tray, from the same file as the exe's icon
        static Icon LoadIcon()
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Islet.ico"));
            using var stream = resource.Stream;
            return new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            image.Dispose();
        }
    }
}
