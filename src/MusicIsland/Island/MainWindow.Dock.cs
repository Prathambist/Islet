using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MusicIsland
{
    public partial class MainWindow
    {
        // Gap kept between the island and the screen's sides
        const double DockInset = 10;
        const double IslandTop = 10;

        // Dropped this close to the middle, it centres itself exactly
        const double CentreSnap = 18;

        // Hold this long to pick the island up; moving first counts as an ordinary press
        readonly DispatcherTimer holdTimer = new() { Interval = TimeSpan.FromSeconds(1) };

        // The screen x the island is centred on. Near a side, that side pushes it inward
        // as it grows, so it always opens fully on screen.
        readonly Spring dockX = new(0, 0.5, 0.8);

        // How far the island's middle was from the pointer when picked up; closes to 0
        // so the island eases under the pointer instead of jumping there
        readonly Spring grab = new(0, 0.28, 0.9);

        readonly Spring dragLift = new(0, 0.3, 0.7);
        readonly Spring dragPop = new(1, 0.3, 0.55);

        // Squash and stretch from how fast it moves, wobbling like jelly when it stops,
        // and a lean into the direction it's going. Little damping, so they overshoot.
        readonly Spring stretch = new(0, 0.32, 0.36);
        readonly Spring lean = new(0, 0.3, 0.42);

        readonly Spring backdropLevel = new(0, 0.22, 1);
        DragBackdrop? backdrop;

        bool pressing;
        bool dragging;
        bool centred;
        Point pressPoint;
        double lastPointerX = double.NaN;
        double dragVelocity;

        // Worked out each frame: the island's layout left inside the window, and the
        // point it grows and scales from (0 its left side, 1 its right side)
        double islandLeft;
        double islandOrigin = 0.5;

        // The stretch and lean cost a few transforms each frame, so Low mode goes without
        static bool DragFlair => Profile.Glow;

        static double ScreenWidth => SystemParameters.PrimaryScreenWidth;

        void SetUpDock()
        {
            holdTimer.Tick += (s, e) =>
            {
                holdTimer.Stop();

                if (pressing && !seeking && Mouse.LeftButton == MouseButtonState.Pressed)
                    StartDrag();
            };

            IslandContainer.PreviewMouseLeftButtonDown += Dock_PressDown;
            IslandContainer.PreviewMouseMove += Dock_MouseMove;
            IslandContainer.PreviewMouseLeftButtonUp += Dock_PressUp;
            IslandContainer.LostMouseCapture += (s, e) =>
            {
                if (dragging)
                    DropIsland();
            };

            // Its spot is a share of the screen's width, so it stays put across resolutions
            EventHandler displayChanged = (s, e) => Dispatcher.BeginInvoke(() =>
            {
                backdrop?.FitScreen();
                ApplyDock(snap: true);
            });
            SystemEvents.DisplaySettingsChanged += displayChanged;

            Closed += (s, e) =>
            {
                SystemEvents.DisplaySettingsChanged -= displayChanged;
                backdrop?.Close();
            };
        }

        /// <summary>The island's left edge: centred on x, but pushed in from the screen's sides.</summary>
        internal static double IslandLeftFor(double x, double islandWidth, double screenWidth) =>
            Math.Clamp(x - islandWidth / 2, DockInset, Math.Max(DockInset, screenWidth - DockInset - islandWidth));

        /// <summary>The window is narrower than the screen, so it's centred on x too, staying on screen.</summary>
        internal static double WindowLeftFor(double x, double windowWidth, double screenWidth) =>
            Math.Clamp(x - windowWidth / 2, 0, Math.Max(0, screenWidth - windowWidth));

        internal static bool NearCentre(double x, double screenWidth) =>
            Math.Abs(x - screenWidth / 2) <= CentreSnap;

        // Called from ApplySettings, so a drop, the tray menu and a new resolution all move it the same way
        void ApplyDock(bool snap = false)
        {
            if (dragging)
                return;

            double x = Math.Clamp(Settings.IslandX, 0, 1) * ScreenWidth;

            if (snap)
            {
                dockX.Snap(x);
            }
            else
            {
                dockX.Configure(0.55, 0.78);
                dockX.Target = x;
            }

            EnsureRendering();
        }

        // Called from ApplyShape: moves the window along the top edge and the island inside it,
        // and shapes it while it moves
        void PlaceIsland(double w)
        {
            double x = dockX.Value + grab.Value;
            double left = IslandLeftFor(x, w, ScreenWidth);
            double windowLeft = WindowLeftFor(x, Width, ScreenWidth);

            // While dragged, only the window moves; the island keeps its place inside it,
            // so nothing has to be laid out again
            if (Math.Abs(Left - windowLeft) > 0.01)
                Left = windowLeft;

            islandLeft = left - windowLeft;
            islandOrigin = Math.Clamp((x - left) / w, 0, 1);

            var margin = new Thickness(islandLeft, IslandTop + dragLift.Value, 0, 0);

            if (IslandContainer.Margin != margin)
                IslandContainer.Margin = margin;

            IslandContainer.RenderTransformOrigin = new Point(islandOrigin, 0);

            // Faster means longer and thinner, and the bottom trails behind the top
            double speed = dragging ? dragVelocity : dockX.Velocity;
            stretch.Target = DragFlair ? Math.Min(0.1, Math.Abs(speed) / 18000) : 0;
            lean.Target = DragFlair ? Math.Clamp(-speed / 320, -7, 7) : 0;

            double scale = islandScale.Value * dragPop.Value;
            IslandScale.ScaleX = scale * (1 + stretch.Value);
            IslandScale.ScaleY = scale * (1 - 0.6 * stretch.Value);
            IslandSkew.AngleX = lean.Value;

            backdrop?.SetLevel(backdropLevel.Value);
        }

        // Called from the frame loop: follows the pointer itself, read once per frame,
        // rather than mouse events that arrive at their own pace
        bool StepDrag(double dt)
        {
            if (!dragging)
                return false;

            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                DropIsland();
                return true;
            }

            var pointer = PointerPosition();
            double x = Math.Clamp(pointer.X, 0, ScreenWidth);

            if (!double.IsNaN(lastPointerX) && dt > 0)
                dragVelocity += ((x - lastPointerX) / dt - dragVelocity) * Math.Min(1, dt * 14);

            lastPointerX = x;

            // A soft magnet at the middle, with a little bump as it catches
            bool nearCentre = NearCentre(x, ScreenWidth);

            if (nearCentre != centred)
            {
                centred = nearCentre;
                backdrop?.ShowCentre(centred);

                if (centred && DragFlair)
                    dragPop.Velocity += 0.9;
            }

            dockX.Target = centred ? ScreenWidth / 2 : x;

            // It stays on the top edge, but stretches a little toward a pointer pulling it down
            double pull = pointer.Y - IslandTop - Math.Min(height.Value, 38) / 2;
            dragLift.Target = pull > 0 ? 26 * Math.Tanh(pull / 110) : 0;

            // Keep every frame coming while it's held
            settleFrames = 2;
            return true;
        }

        Point PointerPosition()
        {
            GetCursorPos(out var point);
            var source = PresentationSource.FromVisual(this);

            return source?.CompositionTarget != null
                ? source.CompositionTarget.TransformFromDevice.Transform(new Point(point.X, point.Y))
                : new Point(point.X, point.Y);
        }

        // Back to normal when a press turns out not to be a hold
        void CancelHold()
        {
            pressing = false;
            holdTimer.Stop();

            if (!dragging)
            {
                dragPop.Configure(0.3, 0.55);
                dragPop.Target = 1;
                EnsureRendering();
            }
        }

        void Dock_PressDown(object sender, MouseButtonEventArgs e)
        {
            // An alert being dismissed, a click on a hidden island, or a file on the shelf
            if (e.Handled || surface is Surface.Hidden or Surface.Shelf)
                return;

            pressing = true;
            pressPoint = e.GetPosition(this);
            holdTimer.Stop();
            holdTimer.Start();

            // Ready before it's needed, so the pick-up doesn't stall on creating a window
            backdrop ??= new DragBackdrop(() => new WindowInteropHelper(this).Handle);
            backdrop.Prepare();

            // Sinks in slowly while held, so the pick-up a second later feels coming;
            // too slowly for a quick click to notice
            if (DragFlair)
            {
                dragPop.Configure(1.0, 1);
                dragPop.Target = 0.96;
                EnsureRendering();
            }
        }

        void Dock_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                // Nothing under the island reacts while it's carried
                e.Handled = true;
                return;
            }

            // Moving before the hold ends is a normal drag, like seeking
            if (pressing && (e.GetPosition(this) - pressPoint).Length > 4)
                CancelHold();
        }

        void Dock_PressUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging)
            {
                CancelHold();
                return;
            }

            // The drop isn't also a click on whatever was under the pointer
            e.Handled = true;
            DropIsland();
        }

        void StartDrag()
        {
            pressing = false;
            dragging = true;

            // Picked up as the small island
            hovering = false;
            hoverTimer.Stop();
            collapseTimer.Stop();

            var pointer = PointerPosition();
            double w = Math.Max(1, width.Value);
            double middle = Left + islandLeft + w / 2;

            dockX.Configure(0.07, 1);
            dockX.Snap(pointer.X);
            grab.Snap(middle - pointer.X);
            grab.Target = 0;

            lastPointerX = double.NaN;
            dragVelocity = 0;
            centred = false;

            // Pops up out of the press while the screen behind dims
            dragLift.Configure(0.3, 0.7);
            dragPop.Configure(0.34, 0.5);
            dragPop.Target = 1.08;
            backdropLevel.Target = 1;

            backdrop?.ShowCentre(false);

            Mouse.OverrideCursor = Cursors.SizeWE;
            IslandContainer.CaptureMouse();

            UpdateSurface();
            EnsureRendering();
        }

        void DropIsland()
        {
            dragging = false;
            Mouse.OverrideCursor = null;

            if (IslandContainer.IsMouseCaptured)
                IslandContainer.ReleaseMouseCapture();

            // Set down with a bounce: it springs back up to the edge, settles a touch
            // below full size, and squashes for a moment as it lands
            dragPop.Configure(0.42, 0.5);
            dragPop.Target = 1;
            dragLift.Configure(0.4, 0.45);
            dragLift.Target = 0;
            backdropLevel.Target = 0;

            if (DragFlair)
                stretch.Velocity += 1.2;

            // It stays where it was let go, without carrying on past it
            double x = Math.Clamp(dockX.Target, 0, ScreenWidth);
            dockX.Velocity *= 0.15;

            // Saving applies the settings, which settles it there
            Settings.IslandX = ScreenWidth > 0 ? x / ScreenWidth : 0.5;
            Settings.Save();

            UpdateSurface();
            EnsureRendering();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out CursorPoint point);
    }
}
