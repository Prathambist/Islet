using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Islet
{
    public partial class MainWindow
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;

        readonly ClipboardHistory clipboardHistory = new(AppSettings.Folder);
        readonly DispatcherTimer clipboardPruneTimer = new() { Interval = TimeSpan.FromMinutes(5) };

        HwndSource? clipboardSource;
        bool listeningToClipboard;
        ClipboardHistoryWindow? clipboardWindow;

        public ClipboardHistory ClipboardHistory => clipboardHistory;

        void SetUpClipboard()
        {
            // Entries expire on their own, even while nothing new is copied
            clipboardPruneTimer.Tick += (s, e) => clipboardHistory.Prune();

            Closed += (s, e) =>
            {
                clipboardPruneTimer.Stop();
                StopClipboardListener();
            };
        }

        // Called from ApplySettings
        void SyncClipboard()
        {
            clipboardHistory.Retention = TimeSpan.FromHours(Math.Max(1, Settings.ClipboardRetentionHours));
            clipboardHistory.Prune();

            if (Settings.ClipboardHistoryEnabled)
            {
                StartClipboardListener();
                clipboardPruneTimer.Start();
            }
            else
            {
                StopClipboardListener();
                clipboardPruneTimer.Stop();
                clipboardWindow?.Close();
            }
        }

        void StartClipboardListener()
        {
            if (listeningToClipboard)
                return;

            var hwnd = new WindowInteropHelper(this).Handle;

            if (hwnd == IntPtr.Zero)
                return;

            clipboardSource ??= HwndSource.FromHwnd(hwnd);
            clipboardSource?.AddHook(ClipboardHook);

            if (AddClipboardFormatListener(hwnd))
                listeningToClipboard = true;
            else
                Log.Error("Listening to the clipboard failed", new System.ComponentModel.Win32Exception());
        }

        void StopClipboardListener()
        {
            if (!listeningToClipboard)
                return;

            RemoveClipboardFormatListener(new WindowInteropHelper(this).Handle);
            clipboardSource?.RemoveHook(ClipboardHook);
            listeningToClipboard = false;
        }

        IntPtr ClipboardHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Read it a moment later: the app that copied may still have the clipboard open
            if (msg == WM_CLIPBOARDUPDATE)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => CaptureClipboard(0)));

            return IntPtr.Zero;
        }

        void CaptureClipboard(int attempt)
        {
            if (!Settings.ClipboardHistoryEnabled)
                return;

            try
            {
                var data = Clipboard.GetDataObject();

                if (data == null || IsPrivate(data) || !data.GetDataPresent(DataFormats.UnicodeText))
                    return;

                clipboardHistory.Add(data.GetData(DataFormats.UnicodeText) as string);
            }
            catch (COMException) when (attempt < 5)
            {
                // Busy with another app; try again shortly
                var retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                retry.Tick += (s, e) =>
                {
                    retry.Stop();
                    CaptureClipboard(attempt + 1);
                };
                retry.Start();
            }
            catch (Exception ex)
            {
                Log.Error("Reading the clipboard failed", ex);
            }
        }

        // Password managers mark what they copy so clipboard histories leave it out
        static bool IsPrivate(IDataObject data)
        {
            foreach (string format in ClipboardHistory.PrivateFormats)
                if (data.GetDataPresent(format))
                    return true;

            if (data.GetDataPresent(ClipboardHistory.CanIncludeFormat) &&
                data.GetData(ClipboardHistory.CanIncludeFormat) is MemoryStream stream && stream.Length >= 4)
            {
                var bytes = new byte[4];
                stream.Position = 0;
                stream.ReadExactly(bytes);
                return BitConverter.ToInt32(bytes) == 0;
            }

            return false;
        }

        public void OpenClipboardHistory()
        {
            if (!Settings.ClipboardHistoryEnabled)
            {
                OpenSettings("Tools");
                return;
            }

            if (clipboardWindow == null)
            {
                clipboardWindow = new ClipboardHistoryWindow(clipboardHistory);
                clipboardWindow.Closed += (s, e) => clipboardWindow = null;
                clipboardWindow.Show();
            }

            clipboardWindow.Topmost = true;
            clipboardWindow.Activate();
            clipboardWindow.Topmost = false;
            clipboardWindow.FocusSearch();
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
