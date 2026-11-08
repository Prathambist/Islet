using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Islet
{
    public partial class MainWindow
    {
        readonly Panel locksPanel;

        // Closes the panel once the pointer has been away from it for a moment, like the shelf
        readonly DispatcherTimer locksWatch = new() { Interval = TimeSpan.FromMilliseconds(150) };
        int locksAwayTicks;

        // What's being checked, and what was found last time
        string[] lockedPaths = Array.Empty<string>();
        IReadOnlyList<FileLocker> lockers = Array.Empty<FileLocker>();

        // Apps asked to close, and ones you've been asked to confirm ending
        readonly HashSet<int> closing = new();
        readonly HashSet<int> confirmingEnd = new();

        bool locksOpen;
        bool checkingLocks;
        bool hadLockers;

        static readonly Brush LockRed = FrozenBrush(0xFF, 0x45, 0x3A);
        static readonly Brush LockGreen = FrozenBrush(0x30, 0xD1, 0x58);

        bool LocksOn => Settings.FindFileLocks;

        bool LocksWanted => LocksOn && locksOpen;

        double LocksHeight
        {
            get
            {
                LocksContent.Measure(new Size(348, double.PositiveInfinity));
                return Math.Max(96, LocksContent.DesiredSize.Height + 24);
            }
        }

        void SetUpLocks()
        {
            locksWatch.Tick += (s, e) =>
            {
                if (PointerNearShelf())
                {
                    locksAwayTicks = 0;
                    return;
                }

                if (++locksAwayTicks >= 3)
                    CloseLocks();
            };

            Closed += (s, e) => locksWatch.Stop();
        }

        /// <summary>Opens the panel and asks Windows which programs have these open.</summary>
        void ShowLocks(string[] paths)
        {
            if (!LocksOn || paths.Length == 0)
                return;

            lockedPaths = paths;
            lockers = Array.Empty<FileLocker>();
            closing.Clear();
            confirmingEnd.Clear();
            hadLockers = false;

            locksOpen = true;
            locksAwayTicks = 0;
            locksWatch.Start();

            _ = CheckLocksAsync();
        }

        void CloseLocks()
        {
            locksOpen = false;
            hovering = false;
            locksWatch.Stop();
            UpdateSurface();
        }

        async Task CheckLocksAsync()
        {
            if (checkingLocks)
                return;

            checkingLocks = true;

            if (lockers.Count == 0)
                BuildLocks();

            try
            {
                var paths = lockedPaths;
                lockers = await Task.Run(() => FileLocks.Find(paths));
            }
            catch (Exception ex)
            {
                Log.Error("Finding what has a file open failed", ex);
                lockers = Array.Empty<FileLocker>();
            }
            finally
            {
                checkingLocks = false;
            }

            hadLockers |= lockers.Count > 0;

            // Ones that let go don't need their buttons any more
            closing.IntersectWith(lockers.Select(l => l.ProcessId));
            confirmingEnd.IntersectWith(lockers.Select(l => l.ProcessId));

            BuildLocks();
        }

        string LockedName => lockedPaths.Length == 1
            ? Path.GetFileName(lockedPaths[0].TrimEnd(Path.DirectorySeparatorChar))
            : $"{lockedPaths.Length} files";

        void BuildLocks()
        {
            LocksList.Children.Clear();

            if (checkingLocks && lockers.Count == 0)
            {
                LocksTitle.Text = $"Checking {LockedName}…";
                LocksNote.Text = "Asking Windows what has it open.";
            }
            else if (lockers.Count == 0)
            {
                LocksTitle.Text = hadLockers ? "It's free now" : $"Nothing is using {LockedName}";
                LocksNote.Text = hadLockers
                    ? "Nothing has it open any more, so you can delete, move or rename it."
                    : "If Windows still says it's in use, try again in a moment. It may also be read-only, or need administrator rights.";
                LocksList.Children.Add(Row(null, hadLockers ? "Free" : "Not in use", LockedName, LockGreen, null));
            }
            else
            {
                LocksTitle.Text = $"Who's using {LockedName}";

                bool one = lockers.Count == 1;
                LocksNote.Text = lockers.Any(l => l.Kind == LockerKind.App)
                    ? one ? "Close it, then try again. It can still ask you to save first."
                          : "Close them, then try again. They can still ask you to save first."
                    : lockers.Any(l => l.Kind == LockerKind.Background)
                        ? $"End {(one ? "it" : "them")}, then try again. Anything unsaved in {(one ? "it" : "them")} is lost."
                        : "Windows itself has it open; see below.";

                foreach (var locker in lockers)
                    LocksList.Children.Add(LockerRow(locker));
            }

            if (surface == Surface.Locks)
            {
                height.Target = LocksHeight;
                EnsureRendering();
            }
            else
            {
                UpdateSurface();
            }
        }

        FrameworkElement LockerRow(FileLocker locker)
        {
            string detail = locker.Kind switch
            {
                LockerKind.Explorer => "Close the Explorer window showing it, or turn off its preview pane",
                LockerKind.Service => "A Windows service: restarting the PC frees it",
                LockerKind.Critical => "Part of Windows: restarting the PC frees it",
                LockerKind.Background => "Running in the background, with no window to close",
                _ => closing.Contains(locker.ProcessId) ? "Asked to close…" : "Has it open"
            };

            Button? action = null;

            if (locker.Kind is LockerKind.App or LockerKind.Background)
            {
                bool closeFirst = locker.Kind == LockerKind.App && !closing.Contains(locker.ProcessId);
                bool confirm = confirmingEnd.Contains(locker.ProcessId);

                action = new Button
                {
                    Content = closeFirst ? "Close" : confirm ? "Sure? End it" : "End task",
                    ToolTip = closeFirst
                        ? "Asks it to close, as if you'd closed its window"
                        : "Stops it at once: anything unsaved in it is lost",
                    Focusable = false
                };

                action.SetResourceReference(StyleProperty, "TimerChip");

                if (!closeFirst)
                    action.Foreground = LockRed;

                action.Click += (s, e) =>
                {
                    if (closeFirst)
                        CloseLocker(locker);
                    else if (!confirm)
                    {
                        confirmingEnd.Add(locker.ProcessId);
                        BuildLocks();
                    }
                    else
                        EndLocker(locker);
                };
            }

            var row = Row(locker.ExePath, locker.Name, detail, null, action);
            row.ToolTip = locker.ExePath ?? locker.Name;
            return row;
        }

        // An app's icon (or a coloured dot), its name and what to do, and a button on the right
        Grid Row(string? exe, string name, string detail, Brush? dot, Button? action)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement icon;

            if (dot != null)
            {
                icon = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(13),
                    Background = dot,
                    Child = new TextBlock
                    {
                        Text = "",
                        FontFamily = (FontFamily)FindResource("IconFont"),
                        FontSize = 12,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
            }
            else
            {
                var image = new Image { Width = 26, Height = 26 };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                icon = image;

                if (exe != null)
                    _ = LoadIconAsync(exe, image);
            }

            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            if (action != null)
            {
                action.Height = 26;
                action.Margin = new Thickness(8, 0, 0, 0);
                action.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(action, 2);
                row.Children.Add(action);
            }

            return row;
        }

        static async Task LoadIconAsync(string exe, Image image)
        {
            var icon = await ShellThumbnails.GetAsync(exe, 52);

            if (icon != null)
                image.Source = icon;
        }

        // Asks politely, the way closing its window would, then looks again
        void CloseLocker(FileLocker locker)
        {
            try
            {
                using var process = Process.GetProcessById(locker.ProcessId);

                if (!process.CloseMainWindow())
                    confirmingEnd.Remove(locker.ProcessId);
            }
            catch
            {
                // Already gone
            }

            closing.Add(locker.ProcessId);
            BuildLocks();
            RecheckSoon();
        }

        void EndLocker(FileLocker locker)
        {
            try
            {
                using var process = Process.GetProcessById(locker.ProcessId);
                process.Kill();
                Log.Info($"Ended {locker.Name} to free {LockedName}");
            }
            catch (Exception ex)
            {
                Log.Error($"Ending {locker.Name} failed", ex);
            }

            RecheckSoon();
        }

        // An app saving or asking first can take a moment to let go
        void RecheckSoon()
        {
            foreach (var delay in new[] { 0.8, 2.5, 5 })
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();

                    if (locksOpen)
                        _ = CheckLocksAsync();
                };
                timer.Start();
            }
        }

        void LocksRecheck_Click(object sender, RoutedEventArgs e) => _ = CheckLocksAsync();
    }
}
