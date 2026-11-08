using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Islet
{
    /// <summary>
    /// Picks a colour from anywhere on the screen. It takes one snapshot of the screen as it
    /// opens and reads from that, so the magnifier is instant and never sees itself. Click to
    /// pick; right-click, Esc or switching away cancels.
    /// </summary>
    sealed class ColorPickerWindow : Window
    {
        // Pixels across the magnifier, and how big each is drawn
        const int Span = 11;
        const double Zoom = 11;
        const double Lens = Span * Zoom;

        readonly byte[] pixels;
        readonly int pixelWidth, pixelHeight, stride;
        readonly BitmapSource shot;
        readonly double scaleX, scaleY;

        readonly Canvas canvas = new();
        readonly Grid loupe = new() { Width = Lens, Height = Lens };
        readonly Image lens = new() { Width = Lens, Height = Lens, Stretch = Stretch.Fill };
        readonly Ellipse ring = new() { StrokeThickness = 5 };
        readonly Border label = new() { CornerRadius = new CornerRadius(10), Background = Brushes.Black, Padding = new Thickness(9, 4, 9, 4) };
        readonly TextBlock hex = new() { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Cascadia Mono, Consolas") };

        Color current;
        bool finished;

        /// <summary>The picked colour, or null when cancelled.</summary>
        public event Action<Color?>? Done;

        /// <param name="toDevice">How many screen pixels to a WPF unit, across and down.</param>
        public ColorPickerWindow(double toDeviceX, double toDeviceY)
        {
            scaleX = toDeviceX;
            scaleY = toDeviceY;

            // The snapshot, before this window is on screen
            var bounds = Forms.Screen.PrimaryScreen!.Bounds;
            pixelWidth = bounds.Width;
            pixelHeight = bounds.Height;

            using (var bitmap = new Drawing.Bitmap(pixelWidth, pixelHeight, Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

                var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, pixelWidth, pixelHeight),
                    Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);

                stride = data.Stride;
                pixels = new byte[stride * pixelHeight];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                bitmap.UnlockBits(data);
            }

            shot = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            shot.Freeze();

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            Topmost = true;
            Title = "Islet colour picker";

            // Not quite clear, so the whole screen takes the clicks
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            Cursor = Cursors.Cross;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            BuildLoupe();
            Content = canvas;

            MouseMove += (s, e) => Follow(e.GetPosition(this));
            MouseLeftButtonDown += (s, e) => Finish(current);
            MouseRightButtonDown += (s, e) => Finish(null);
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Finish(null);
            };
            Deactivated += (s, e) => Finish(null);
            Loaded += (s, e) =>
            {
                Activate();
                Follow(Mouse.GetPosition(this));
            };

            // Windows doesn't always let it take the keyboard (the island never has it to hand
            // over), so Esc is also watched directly
            var escape = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            escape.Tick += (s, e) =>
            {
                if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
                    Finish(null);
            };
            escape.Start();
            Closed += (s, e) => escape.Stop();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);

        void BuildLoupe()
        {
            RenderOptions.SetBitmapScalingMode(lens, BitmapScalingMode.NearestNeighbor);

            // Round, with the picked colour as its rim and the chosen pixel outlined in the middle
            loupe.Clip = new EllipseGeometry(new Point(Lens / 2, Lens / 2), Lens / 2, Lens / 2);
            loupe.Children.Add(lens);
            loupe.Children.Add(new Rectangle
            {
                Width = Zoom + 2,
                Height = Zoom + 2,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            loupe.Children.Add(new Rectangle
            {
                Width = Zoom + 4,
                Height = Zoom + 4,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            loupe.Children.Add(ring);
            loupe.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), StrokeThickness = 1 });

            label.Child = hex;
            canvas.Children.Add(loupe);
            canvas.Children.Add(label);

            // Grows in from the cursor rather than appearing
            loupe.RenderTransformOrigin = new Point(0, 0);
            var grow = new ScaleTransform(0.6, 0.6);
            loupe.RenderTransform = grow;
            var spring = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
        }

        void Follow(Point point)
        {
            int x = Math.Clamp((int)(point.X * scaleX), 0, pixelWidth - 1);
            int y = Math.Clamp((int)(point.Y * scaleY), 0, pixelHeight - 1);

            int at = y * stride + x * 4;
            current = Color.FromRgb(pixels[at + 2], pixels[at + 1], pixels[at]);

            // The pixels around it, kept inside the screen at its edges
            int left = Math.Clamp(x - Span / 2, 0, pixelWidth - Span);
            int top = Math.Clamp(y - Span / 2, 0, pixelHeight - Span);
            lens.Source = new CroppedBitmap(shot, new Int32Rect(left, top, Span, Span));

            ring.Stroke = new SolidColorBrush(current);
            hex.Text = ColorPalette.Hex(current);

            // Down and right of the cursor, or flipped where it would run off the screen
            double gap = 22;
            double lensLeft = point.X + gap + Lens > ActualWidth ? point.X - gap - Lens : point.X + gap;
            double lensTop = point.Y + gap + Lens + 30 > ActualHeight ? point.Y - gap - Lens - 30 : point.Y + gap;

            Canvas.SetLeft(loupe, lensLeft);
            Canvas.SetTop(loupe, lensTop);

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, lensLeft + (Lens - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, lensTop + Lens + 6);
        }

        void Finish(Color? picked)
        {
            if (finished)
                return;

            finished = true;
            Done?.Invoke(picked);
            Close();
        }
    }
}
