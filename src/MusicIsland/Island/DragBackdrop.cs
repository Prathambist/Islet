using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MusicIsland
{
    /// <summary>
    /// Dims the screen behind the island while it's dragged, lighting the strip along
    /// the top where it can go, with a mark at the middle that brightens when it snaps there.
    ///
    /// It's a see-through WPF window: setting the alpha through Windows instead leaves
    /// WPF's drawing opaque, which turned the whole screen black.
    /// </summary>
    sealed class DragBackdrop : Window
    {
        // How dark it gets, out of 255: a light tint, so what's behind stays easy to see
        const byte TintAlpha = 45;

        readonly Border centreMark;
        readonly Func<IntPtr> island;
        IntPtr hwnd;

        /// <param name="island">The island's window, which the backdrop always sits just under.</param>
        public DragBackdrop(Func<IntPtr> island)
        {
            this.island = island;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(TintAlpha, 0, 0, 0));
            Opacity = 0;
            Title = "MusicIsland drag";

            var strip = new Border
            {
                Height = 70,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(
                    Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90)
            };

            centreMark = new Border
            {
                Width = 4,
                Height = 18,
                CornerRadius = new CornerRadius(2),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 58, 0, 0)
            };

            var root = new Grid();
            root.Children.Add(strip);
            root.Children.Add(centreMark);
            Content = root;

            ShowCentre(false);
            FitScreen();

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;

                // Clicks go straight through, and it never takes focus
                long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            };
        }

        public void FitScreen()
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }

        /// <summary>Creates the window ahead of time, so showing it later is instant.</summary>
        public void Prepare() => new WindowInteropHelper(this).EnsureHandle();

        public void ShowCentre(bool snapped) => centreMark.Opacity = snapped ? 1 : 0.3;

        /// <summary>0 hides it, 1 is fully dimmed.</summary>
        public void SetLevel(double level)
        {
            // In steps the eye can't tell apart, so it isn't redrawn more than needed
            double next = Math.Round(Math.Clamp(level, 0, 1) * 40) / 40;

            if (next <= 0)
            {
                if (IsVisible)
                    Hide();

                Opacity = 0;
                return;
            }

            if (next != Opacity)
                Opacity = next;

            if (!IsVisible)
            {
                Prepare();
                Show();

                // Showing puts it on top of every window, the island included
                SetWindowPos(hwnd, island(), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        }

        const int GWL_EXSTYLE = -20;
        const long WS_EX_TRANSPARENT = 0x00000020;
        const long WS_EX_TOOLWINDOW = 0x00000080;
        const long WS_EX_NOACTIVATE = 0x08000000;
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
