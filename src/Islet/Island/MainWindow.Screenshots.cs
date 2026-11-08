using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet
{
    public partial class MainWindow
    {
        FileSystemWatcher? screenshotWatcher;

        // Pressed on a notification that carries a file, to drag it out from there
        Point notifyPressPoint;
        bool notifyPressed;
        bool notifyDragged;

        // A screenshot being dragged out of its card, so the island doesn't take it back
        bool notifyDragging;

        static readonly Color ScreenshotBadge = Color.FromRgb(0x0A, 0x84, 0xFF);

        void SetUpScreenshots()
        {
            PeekNotify.PreviewMouseLeftButtonDown += (s, e) =>
            {
                notifyPressed = currentNotify?.DragPath != null;
                notifyDragged = false;
                notifyPressPoint = e.GetPosition(this);
            };

            PeekNotify.MouseMove += DragFromNotify;
            Closed += (s, e) => screenshotWatcher?.Dispose();
        }

        // Called from ApplySettings
        void SyncScreenshots()
        {
            bool want = Settings.CatchScreenshots && Settings.ShelfEnabled;

            if (!want)
            {
                screenshotWatcher?.Dispose();
                screenshotWatcher = null;
                return;
            }

            if (screenshotWatcher != null)
                return;

            string? folder = ScreenshotsFolder();

            if (folder == null || !Directory.Exists(folder))
                return;

            try
            {
                // Win+Shift+S (and Win+PrtScn) save every capture here, unless that's been turned off
                screenshotWatcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName
                };

                screenshotWatcher.Created += (s, e) => _ = CatchAsync(e.FullPath);
                screenshotWatcher.Renamed += (s, e) => _ = CatchAsync(e.FullPath);
                screenshotWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Error("Watching the Screenshots folder failed", ex);
                screenshotWatcher = null;
            }
        }

        public static bool IsScreenshotFile(string path) =>
            Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

        async Task CatchAsync(string path)
        {
            if (!IsScreenshotFile(path))
                return;

            // The file can still be being written, or synced by OneDrive, for a moment
            var thumbnail = await Task.Run(() => LoadThumbnail(path));

            if (thumbnail == null)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!Settings.CatchScreenshots || !Settings.ShelfEnabled)
                    return;

                justAdded = shelf.Add(new[] { path });

                Enqueue(new NotifyItem(
                    "Screenshot",
                    "On the shelf",
                    "Drag it into a chat, or click to see the shelf",
                    thumbnail,
                    ScreenshotBadge,
                    0xE722,
                    OpenShelf,
                    DragPath: path));
            });
        }

        static BitmapSource? LoadThumbnail(string path)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 120;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }

            return null;
        }

        // Dragging a screenshot straight from its notification, without opening the shelf
        void DragFromNotify(object sender, MouseEventArgs e)
        {
            if (!notifyPressed || e.LeftButton != MouseButtonState.Pressed || currentNotify?.DragPath is not string path)
                return;

            if ((e.GetPosition(this) - notifyPressPoint).Length < 5 || !File.Exists(path))
                return;

            notifyPressed = false;
            notifyDragged = true;
            notifyDragging = true;
            notifyTimer.Stop();

            try
            {
                DragDrop.DoDragDrop(PeekNotify, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
            }
            catch (Exception ex)
            {
                Log.Error("Dragging a screenshot failed", ex);
            }
            finally
            {
                notifyDragging = false;
                notifyTimer.Start();
            }
        }

        static string? ScreenshotsFolder()
        {
            try
            {
                var id = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");

                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path) == 0)
                    return path;
            }
            catch
            {
            }

            return null;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out string path);
    }
}
