using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace MusicIsland
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
        readonly Icon image;

        static AppSettings Settings => AppSettings.Current;

        public TrayIcon(FullscreenWatcher fullscreen, CountdownTimer countdown, Action openSettings, Action quit,
            Func<UpdateService.Release?> update, Action installUpdate)
        {
            this.fullscreen = fullscreen;
            this.countdown = countdown;
            this.openSettings = openSettings;
            this.quit = quit;
            this.update = update;
            this.installUpdate = installUpdate;

            image = CreateIcon();

            icon = new Forms.NotifyIcon
            {
                Icon = image,
                Text = "MusicIsland",
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
            menu.Items.Add("Settings…", null, (s, e) => openSettings());
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
            menu.Items.Add("Quit MusicIsland", null, (s, e) => quit());
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

        // A tiny black pill, drawn at runtime so the repo needs no .ico asset
        static Icon CreateIcon()
        {
            using var bitmap = new Bitmap(32, 32);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var path = new GraphicsPath();
                var rect = new RectangleF(2, 9, 28, 14);
                float d = rect.Height;
                path.AddArc(rect.X, rect.Y, d, d, 90, 180);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 180);
                path.CloseFigure();

                using var fill = new SolidBrush(Color.FromArgb(255, 8, 8, 10));
                using var outline = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f);
                g.FillPath(fill, path);
                g.DrawPath(outline, path);

                using var dot = new SolidBrush(Color.FromArgb(255, 190, 165, 255));
                g.FillEllipse(dot, 21, 13, 6, 6);
            }

            var handle = bitmap.GetHicon();
            var icon = (Icon)Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
            return icon;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            image.Dispose();
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }
}
