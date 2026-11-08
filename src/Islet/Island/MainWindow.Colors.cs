using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Islet
{
    public partial class MainWindow
    {
        readonly ColorPalette palette = new(AppSettings.Folder);
        readonly Panel palettePanel;

        // Closes the panel once the pointer has been away from it for a moment, like the shelf
        readonly DispatcherTimer paletteWatch = new() { Interval = TimeSpan.FromMilliseconds(150) };
        int paletteAwayTicks;

        ColorPickerWindow? picker;
        bool paletteOpen;

        // Just picked, so it pops into the badge and the panel
        Color? justPicked;

        bool PaletteOn => Settings.ShowColorPalette;

        bool PaletteWanted => PaletteOn && paletteOpen && palette.Colors.Count > 0;

        // Two rows once there are more colours than fit in one
        double PaletteHeight => palette.Colors.Count > 6 ? 188 : 130;

        void SetUpColors()
        {
            paletteWatch.Tick += (s, e) =>
            {
                if (PointerNearShelf())
                {
                    paletteAwayTicks = 0;
                    return;
                }

                if (++paletteAwayTicks >= 3)
                    ClosePalette();
            };

            palette.Changed += () => Dispatcher.BeginInvoke(PaletteChanged);
            Closed += (s, e) => paletteWatch.Stop();

            BuildPalette();
        }

        // Called from ApplySettings
        void SyncColors()
        {
            if (!PaletteOn && paletteOpen)
                ClosePalette();

            ShowColorPill();
        }

        // ================= Picking =================

        void PickColor_Click(object sender, RoutedEventArgs e) => StartPicking();

        public void StartPicking()
        {
            if (picker != null)
                return;

            // The island steps back while you pick
            hovering = false;
            paletteOpen = false;
            paletteWatch.Stop();
            UpdateSurface();

            var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

            try
            {
                picker = new ColorPickerWindow(toDevice.M11, toDevice.M22);
            }
            catch (Exception ex)
            {
                Log.Error("Starting the colour picker failed", ex);
                return;
            }

            picker.Done += color =>
            {
                picker = null;

                if (color is Color picked)
                    Dispatcher.BeginInvoke(() => Picked(picked));
            };

            picker.Show();
        }

        void Picked(Color color)
        {
            CopyText(ColorPalette.Hex(color));
            justPicked = color;
            palette.Add(color);

            Enqueue(new NotifyItem(
                "Colour picker",
                ColorPalette.Hex(color),
                "Copied · " + ColorPalette.Rgb(color),
                null,
                color,
                0xE790,
                () => OpenPalette()));
        }

        static void CopyText(string text)
        {
            // The clipboard can be busy for a moment with another app
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch
                {
                    Thread.Sleep(30);
                }
            }
        }

        // ================= Panel =================

        void PaletteChanged()
        {
            BuildPalette();
            ShowColorPill();

            if (palette.Colors.Count == 0 && paletteOpen)
            {
                ClosePalette();
                return;
            }

            if (surface == Surface.Palette)
            {
                height.Target = PaletteHeight;
                EnsureRendering();
            }
        }

        void OpenPalette()
        {
            if (!PaletteOn || palette.Colors.Count == 0)
                return;

            paletteOpen = true;
            paletteAwayTicks = 0;
            paletteWatch.Start();
            UpdateSurface();
        }

        void ClosePalette()
        {
            paletteOpen = false;
            hovering = false;
            paletteWatch.Stop();
            UpdateSurface();
            ShowColorPill();
        }

        void PalettePick_Click(object sender, RoutedEventArgs e) => StartPicking();

        void PaletteClear_Click(object sender, RoutedEventArgs e) => palette.Clear();

        void BuildPalette()
        {
            PaletteSwatches.Children.Clear();

            foreach (var color in palette.Colors)
                PaletteSwatches.Children.Add(Swatch(color, animate: color == justPicked));

            int count = palette.Colors.Count;
            PaletteCount.Text = count == 1 ? "1 colour" : $"{count} colours";
            PaletteNote.Text = "Click to copy · right-click for more";

            BuildColorPill();
        }

        FrameworkElement Swatch(Color color, bool animate)
        {
            string hex = ColorPalette.Hex(color);

            var dot = new Ellipse
            {
                Width = 34,
                Height = 34,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                StrokeThickness = 1,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };

            var label = new TextBlock
            {
                Text = hex,
                FontSize = 10,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };

            var swatch = new StackPanel
            {
                Width = 58,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = $"{hex}  ·  {ColorPalette.Rgb(color)}",
                Margin = new Thickness(0, 0, 0, 6),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };

            swatch.Children.Add(dot);
            swatch.Children.Add(label);

            var dotScale = (ScaleTransform)dot.RenderTransform;
            swatch.MouseEnter += (s, e) => Bounce(dotScale, 1.12);
            swatch.MouseLeave += (s, e) => Bounce(dotScale, 1);

            // Copied, with the label saying so for a moment
            swatch.MouseLeftButtonUp += (s, e) =>
            {
                CopyText(hex);
                label.Text = "Copied";
                Bounce(dotScale, 0.9, then: 1.12);

                var back = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                back.Tick += (_, _) =>
                {
                    back.Stop();
                    label.Text = hex;
                };
                back.Start();
            };

            var menu = new ContextMenu();
            menu.Items.Add(Item($"Copy {hex}", () => CopyText(hex)));
            menu.Items.Add(Item($"Copy {ColorPalette.Rgb(color)}", () => CopyText(ColorPalette.Rgb(color))));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Remove", () => palette.Remove(color)));
            menu.Opened += (s, e) => paletteWatch.Stop();
            menu.Closed += (s, e) => { if (paletteOpen) paletteWatch.Start(); };
            swatch.ContextMenu = menu;

            if (animate)
                PopIn(swatch);

            return swatch;
        }

        static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => action();
            return item;
        }

        static void Bounce(ScaleTransform scale, double to, double? then = null)
        {
            var grow = new DoubleAnimation(to, TimeSpan.FromMilliseconds(then == null ? 220 : 90))
            {
                EasingFunction = then == null ? new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } : null
            };

            if (then is double next)
                grow.Completed += (s, e) => Bounce(scale, next);

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        // ================= Badge =================

        // The last few colours as overlapping dots, newest in front
        void BuildColorPill()
        {
            ColorPillDots.Children.Clear();

            var shown = palette.Colors.Take(3).Reverse().ToList();

            for (int i = 0; i < shown.Count; i++)
            {
                var dot = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(shown[i]),
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.5,
                    Margin = new Thickness(i == 0 ? 0 : -5, 0, 0, 0),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform()
                };

                ColorPillDots.Children.Add(dot);

                if (i == shown.Count - 1 && shown[i] == justPicked)
                    PopIn(dot);
            }

            justPicked = null;
        }

        void ShowColorPill()
        {
            bool hiddenForApp = fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName);
            bool show = PaletteOn && palette.Colors.Count > 0 && surface != Surface.Palette && !hiddenForApp;

            ColorPill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PlaceColorPill();
        }

        // Right of the island, past the privacy dot when that's on the right too; on the left,
        // past the shelf's badge, when there's no room
        void PlaceColorPill()
        {
            if (ColorPill.Visibility != Visibility.Visible)
                return;

            const double gap = 6;
            double w = Math.Max(1, width.Value);
            double scale = islandScale.Value * dragPop.Value;
            double pill = ColorPill.ActualWidth > 0 ? ColorPill.ActualWidth : 40;
            bool islandShown = islandOpacity.Value > 0.5;

            double visualLeft = islandLeft + islandOrigin * w * (1 - scale);
            double visualRight = visualLeft + w * scale;

            if (!islandShown)
                visualLeft = visualRight = islandLeft + w / 2;

            bool privacyShown = PrivacyPill.Visibility == Visibility.Visible;
            bool privacyLeft = privacyShown && Left + islandLeft + w / 2 > ScreenWidth * 0.75;
            double privacyWidth = privacyShown ? PrivacyPill.Width + gap : 0;

            double left = visualRight + gap + (privacyShown && !privacyLeft ? privacyWidth : 0);

            if (left + pill > Width - 2)
            {
                double shelfWidth = ShelfPill.Visibility == Visibility.Visible ? ShelfPill.ActualWidth + gap : 0;
                left = visualLeft - gap - pill - (privacyLeft ? privacyWidth : 0) - shelfWidth;
            }

            double band = islandShown ? Math.Min(height.Value, 38) * scale : 30;
            double top = IslandContainer.Margin.Top + (band - ColorPill.Height) / 2;

            ColorPill.Margin = new Thickness(left, top, 0, 0);
        }

        void ColorPill_Click(object sender, MouseButtonEventArgs e) => OpenPalette();
    }
}
