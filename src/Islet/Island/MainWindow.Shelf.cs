using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Islet
{
    public partial class MainWindow
    {
        readonly FileShelf shelf = new(AppSettings.Folder);
        readonly Panel shelfPanel;

        // A drag that brushes the island's edge while it changes shape sends a leave and
        // an enter in quick succession; waiting a moment before treating it as gone keeps
        // the island from flickering between shapes
        readonly DispatcherTimer dragLeaveTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };

        // Closes the shelf once the pointer has been away from it for a moment. It checks
        // where the pointer really is, since mouse enter and leave don't fire during a drag.
        readonly DispatcherTimer shelfWatch = new() { Interval = TimeSpan.FromMilliseconds(150) };
        int shelfAwayTicks;

        // Smooth sideways scrolling through the files
        readonly Spring shelfScroll = new(0, 0.28, 1);

        // Thumbnails by path, so reopening the shelf is instant
        readonly Dictionary<string, BitmapSource?> shelfThumbnails = new(StringComparer.OrdinalIgnoreCase);

        // Just dropped in, so they pop in rather than simply appearing
        IReadOnlyList<string> justAdded = Array.Empty<string>();

        bool shelfOpen;
        bool shelfDragOver;
        bool draggingOut;
        Point shelfPressPoint;
        string[]? shelfPressed;

        const int ThumbnailSize = 112;

        bool ShelfOn => Settings.ShelfEnabled;

        // Dragging over shows the drop zones even with the shelf off, for "What's using it?"
        bool ShelfWanted => shelfDragOver && (ShelfOn || LocksOn) || ShelfOn && (draggingOut || shelfOpen && shelf.Items.Count > 0);

        // Which zone a drag over the island would land in, and which zones it offers
        bool dropOnLocks;
        bool lockZoneShown;

        double ShelfHeight => shelf.Items.Count > 0 ? 136 : 100;

        void SetUpShelf()
        {
            dragLeaveTimer.Tick += (s, e) =>
            {
                dragLeaveTimer.Stop();
                SetDragOver(false);
            };

            shelfWatch.Tick += (s, e) =>
            {
                if (draggingOut || shelfDragOver || PointerNearShelf())
                {
                    shelfAwayTicks = 0;
                    return;
                }

                if (++shelfAwayTicks >= 3)
                    CloseShelf();
            };

            shelf.Changed += () => Dispatcher.BeginInvoke(ShelfChanged);

            Closed += (s, e) =>
            {
                shelfWatch.Stop();
                dragLeaveTimer.Stop();
            };

            BuildShelfItems();
        }

        // Called from ApplySettings
        void SyncShelf()
        {
            IslandContainer.AllowDrop = ShelfOn || LocksOn;

            if (!ShelfOn)
            {
                shelfOpen = false;
                shelfWatch.Stop();
            }
            else if (shelf.Prune())
            {
                return;
            }

            ShowShelfPill();
        }

        void ShelfChanged()
        {
            BuildShelfItems();
            ShowShelfPill();

            if (shelf.Items.Count == 0 && shelfOpen)
            {
                CloseShelf();
                return;
            }

            // Already showing it: only its height follows the files
            if (surface == Surface.Shelf)
            {
                height.Target = ShelfHeight;
                EnsureRendering();
            }
        }

        void OpenShelf()
        {
            if (!ShelfOn || shelf.Items.Count == 0 && !shelfDragOver)
                return;

            shelf.Prune();
            shelfOpen = true;
            shelfAwayTicks = 0;
            shelfWatch.Start();
            UpdateSurface();
        }

        void CloseShelf()
        {
            shelfOpen = false;
            hovering = false;
            shelfWatch.Stop();
            UpdateSurface();
            ShowShelfPill();
        }

        // ================= Dragging files in =================

        bool CanTake(DragEventArgs e) =>
            !draggingOut && !notifyDragging &&
            (ShelfOn && (e.Data.GetDataPresent(DataFormats.FileDrop) ||
                         e.Data.GetDataPresent(DataFormats.Bitmap) ||
                         e.Data.GetDataPresent(DataFormats.UnicodeText)) ||
             LocksOn && e.Data.GetDataPresent(DataFormats.FileDrop));

        // Only files can be checked; only the shelf takes images and text
        void ChooseZones(DragEventArgs e)
        {
            bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
            bool both = ShelfOn && LocksOn && files;
            bool locksOnly = !ShelfOn && LocksOn && files;

            lockZoneShown = both || locksOnly;
            ShelfZone.Visibility = locksOnly ? Visibility.Collapsed : Visibility.Visible;
            LockZone.Visibility = lockZoneShown ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(ShelfZone, both ? 1 : 3);
            Grid.SetColumn(LockZone, both ? 2 : 0);
            Grid.SetColumnSpan(LockZone, both ? 1 : 3);

            // The half the pointer is over lights up
            bool onLocks = locksOnly || both && e.GetPosition(ShelfDropHint).X > ShelfDropHint.ActualWidth / 2;

            if (onLocks != dropOnLocks || !shelfDragOver)
            {
                dropOnLocks = onLocks;
                LightZone(ShelfZoneOutline, !onLocks);
                LightZone(LockZoneOutline, onLocks);
            }
        }

        static void LightZone(System.Windows.Shapes.Rectangle outline, bool lit)
        {
            outline.Stroke = new SolidColorBrush(lit ? Colors.White : Color.FromArgb(0x55, 255, 255, 255));
            outline.BeginAnimation(OpacityProperty, lit
                ? new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                : new DoubleAnimation(0.7, TimeSpan.FromMilliseconds(150)));
        }

        void Shelf_DragEnter(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = CanTake(e) ? DragDropEffects.Copy : DragDropEffects.None;

            if (e.Effects == DragDropEffects.None)
                return;

            dragLeaveTimer.Stop();
            ChooseZones(e);
            SetDragOver(true);
        }

        void Shelf_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = CanTake(e) ? DragDropEffects.Copy : DragDropEffects.None;

            if (e.Effects != DragDropEffects.None)
            {
                dragLeaveTimer.Stop();
                ChooseZones(e);
            }
        }

        void Shelf_DragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
            dragLeaveTimer.Stop();
            dragLeaveTimer.Start();
        }

        void Shelf_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            dragLeaveTimer.Stop();

            if (!CanTake(e))
            {
                SetDragOver(false);
                return;
            }

            ChooseZones(e);

            if (dropOnLocks && e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            {
                SetDragOver(false);
                ShowLocks(dropped);
                return;
            }

            if (!ShelfOn)
            {
                SetDragOver(false);
                return;
            }

            justAdded = Take(e.Data);
            SetDragOver(false);

            // Stays open to show what landed, until the pointer moves away
            if (shelf.Items.Count > 0)
                OpenShelf();

            // Glides along to the newest, once they're laid out
            if (justAdded.Count > 0)
                Dispatcher.BeginInvoke(() =>
                {
                    shelfScroll.Snap(ShelfScroller.HorizontalOffset);
                    shelfScroll.Target = ShelfScroller.ScrollableWidth;
                    EnsureRendering();
                }, DispatcherPriority.Loaded);
        }

        void SetDragOver(bool over)
        {
            if (shelfDragOver == over)
                return;

            shelfDragOver = over;
            ShowDropHint(over);
            UpdateSurface();
            ShowShelfPill();
        }

        void ShowDropHint(bool show)
        {
            if (show)
                ShelfDropHint.Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 160 : 200));

            if (!show)
                fade.Completed += (s, e) =>
                {
                    if (!shelfDragOver)
                        ShelfDropHint.Visibility = Visibility.Collapsed;
                };

            ShelfDropHint.BeginAnimation(OpacityProperty, fade);
            ShelfContent.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0.18 : 1, TimeSpan.FromMilliseconds(180)));

            if (!show)
            {
                ShelfZoneOutline.BeginAnimation(OpacityProperty, null);
                LockZoneOutline.BeginAnimation(OpacityProperty, null);
            }
        }

        // Files are kept where they are; an image or text from a page becomes a file first
        IReadOnlyList<string> Take(IDataObject data)
        {
            try
            {
                if (data.GetData(DataFormats.FileDrop) is string[] files)
                    return shelf.Add(files);

                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");

                if (data.GetData(DataFormats.Bitmap) is BitmapSource image)
                {
                    string path = shelf.SaveOwn($"Image {stamp}.png", stream =>
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(stream);
                    });

                    return shelf.Add(new[] { path });
                }

                if (data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0)
                {
                    text = text.Trim();
                    bool link = Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

                    // A link becomes a shortcut that opens it; other text a note
                    string path = link
                        ? shelf.SaveOwn($"{uri!.Host}.url", stream => Write(stream, $"[InternetShortcut]\r\nURL={uri}\r\n"))
                        : shelf.SaveOwn($"Note {stamp}.txt", stream => Write(stream, text));

                    return shelf.Add(new[] { path });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Adding to the shelf failed", ex);
            }

            return Array.Empty<string>();

            static void Write(Stream stream, string content)
            {
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }

        // ================= The files =================

        void BuildShelfItems()
        {
            ShelfItems.Children.Clear();

            foreach (var path in shelf.Items)
                ShelfItems.Children.Add(ShelfTile(path, animate: justAdded.Contains(path, StringComparer.OrdinalIgnoreCase)));

            justAdded = Array.Empty<string>();

            int count = shelf.Items.Count;
            ShelfCount.Text = count == 1 ? "1 file" : $"{count} files";
            ShelfPillCount.Text = count.ToString();
            ShelfDragAll.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        FrameworkElement ShelfTile(string path, bool animate)
        {
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));

            var image = new Image
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(5),
                Source = shelfThumbnails.GetValueOrDefault(path)
            };

            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var remove = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromRgb(58, 58, 60)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -5, -5, 0),
                Opacity = 0,
                Cursor = Cursors.Hand,
                ToolTip = "Remove from the shelf",
                Child = new TextBlock
                {
                    Text = "",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 8,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            remove.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                shelf.Remove(path);
            };

            var box = new Grid { Width = 56, Height = 56 };
            box.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)) });
            box.Children.Add(image);
            box.Children.Add(remove);

            var label = new TextBlock
            {
                Text = name,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var tile = new StackPanel
            {
                Width = 68,
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = path,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };

            tile.Children.Add(box);
            tile.Children.Add(label);

            tile.MouseEnter += (s, e) => remove.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            tile.MouseLeave += (s, e) => remove.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));

            tile.MouseLeftButtonDown += (s, e) =>
            {
                shelfPressPoint = e.GetPosition(this);
                shelfPressed = new[] { path };
            };

            tile.MouseMove += (s, e) => DragOutIfMoved(e, tile);

            tile.MouseLeftButtonUp += (s, e) =>
            {
                if (shelfPressed == null)
                    return;

                shelfPressed = null;
                OpenPath(path);
            };

            tile.ContextMenu = ShelfMenu(path);

            if (image.Source == null)
                _ = LoadThumbnailAsync(path, image);

            if (animate)
                PopIn(tile);

            return tile;
        }

        async Task LoadThumbnailAsync(string path, Image image)
        {
            var thumbnail = await ShellThumbnails.GetAsync(path, ThumbnailSize);
            shelfThumbnails[path] = thumbnail;

            if (thumbnail == null)
                return;

            // Fades in, rather than popping in when it's ready
            image.Opacity = 0;
            image.Source = thumbnail;
            image.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        }

        static void PopIn(FrameworkElement tile)
        {
            var scale = (ScaleTransform)tile.RenderTransform;
            var grow = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        ContextMenu ShelfMenu(string path)
        {
            var menu = new ContextMenu();

            void Add(string header, Action action)
            {
                var item = new MenuItem { Header = header };
                item.Click += (s, e) => action();
                menu.Items.Add(item);
            }

            Add("Open", () => OpenPath(path));
            Add("Show in folder", () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }));

            if (LocksOn)
                Add("What's using it?", () =>
                {
                    CloseShelf();
                    ShowLocks(new[] { path });
                });
            menu.Items.Add(new Separator());
            Add("Remove from the shelf", () => shelf.Remove(path));

            // Keeps the shelf open while the menu is up
            menu.Opened += (s, e) => shelfWatch.Stop();
            menu.Closed += (s, e) => { if (shelfOpen) shelfWatch.Start(); };
            return menu;
        }

        static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Opening a shelf file failed", ex);
            }
        }

        void ShelfClear_Click(object sender, RoutedEventArgs e) => shelf.Clear();

        // ================= Dragging files out =================

        void ShelfDragAll_MouseDown(object sender, MouseButtonEventArgs e)
        {
            shelfPressPoint = e.GetPosition(this);
            shelfPressed = shelf.Items.ToArray();
            e.Handled = true;
        }

        void ShelfDragAll_MouseMove(object sender, MouseEventArgs e) => DragOutIfMoved(e, ShelfDragAll);

        void DragOutIfMoved(MouseEventArgs e, DependencyObject source)
        {
            if (shelfPressed == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            if ((e.GetPosition(this) - shelfPressPoint).Length < 5)
                return;

            var paths = shelfPressed.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            shelfPressed = null;

            if (paths.Length == 0)
            {
                shelf.Prune();
                return;
            }

            draggingOut = true;

            try
            {
                // Copy only: dropped into a folder on the same drive, Windows would otherwise
                // move the file out from where you keep it
                DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
            }
            catch (Exception ex)
            {
                Log.Error("Dragging from the shelf failed", ex);
            }
            finally
            {
                draggingOut = false;
                shelfAwayTicks = 0;
            }
        }

        // ================= Placement =================

        // Called each frame from ApplyShape
        void ApplyShelf()
        {
            if (surface == Surface.Shelf && !shelfScroll.IsSettled)
                ShelfScroller.ScrollToHorizontalOffset(shelfScroll.Value);

            PlaceShelfPill();
        }

        void ScrollShelf(int delta)
        {
            double max = ShelfScroller.ScrollableWidth;

            if (shelfScroll.IsSettled)
                shelfScroll.Snap(ShelfScroller.HorizontalOffset);

            shelfScroll.Target = Math.Clamp(shelfScroll.Target - delta * 0.6, 0, max);
            EnsureRendering();
        }

        void ShowShelfPill()
        {
            bool hiddenForApp = fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName);
            bool show = ShelfOn && shelf.Items.Count > 0 && surface != Surface.Shelf && !hiddenForApp;

            ShelfPill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PlaceShelfPill();
        }

        // Beside the island, on the side the privacy dot isn't on (or past it, if there's
        // no room), level with the island's top row
        void PlaceShelfPill()
        {
            if (ShelfPill.Visibility != Visibility.Visible)
                return;

            const double gap = 6;
            double w = Math.Max(1, width.Value);
            double scale = islandScale.Value * dragPop.Value;
            double pill = ShelfPill.ActualWidth > 0 ? ShelfPill.ActualWidth : 34;
            double size = ShelfPill.Height;
            bool islandShown = islandOpacity.Value > 0.5;

            double visualLeft = islandLeft + islandOrigin * w * (1 - scale);
            double visualRight = visualLeft + w * scale;

            if (!islandShown)
                visualLeft = visualRight = islandLeft + w / 2;

            bool privacyShown = PrivacyPill.Visibility == Visibility.Visible;
            bool privacyLeft = privacyShown && Left + islandLeft + w / 2 > ScreenWidth * 0.75;
            double privacyWidth = privacyShown ? PrivacyPill.Width + gap : 0;

            double left = visualLeft - gap - pill - (privacyLeft ? privacyWidth : 0);

            // No room on the left: the right side, past the privacy dot
            if (left < 2)
                left = visualRight + gap + (privacyShown && !privacyLeft ? privacyWidth : 0);

            double band = islandShown ? Math.Min(height.Value, 38) * scale : 30;
            double top = IslandContainer.Margin.Top + (band - size) / 2;

            ShelfPill.Margin = new Thickness(left, top, 0, 0);
        }

        void ShelfPill_Click(object sender, MouseButtonEventArgs e) => OpenShelf();

        // The island and a little around it, so moving from the badge onto it or brushing
        // past its edge doesn't close it
        bool PointerNearShelf()
        {
            var pointer = PointerPosition();
            var bounds = IslandContainer.TransformToAncestor(this).TransformBounds(new Rect(IslandContainer.RenderSize));
            bounds.Offset(Left, Top);
            bounds.Inflate(44, 14);

            return bounds.Contains(pointer);
        }
    }
}
