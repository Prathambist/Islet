using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Islet
{
    public enum TimerFaceForm
    {
        /// <summary>The time, then a row of ticks beside it.</summary>
        Line,
        /// <summary>The time over a row of ticks.</summary>
        Stacked,
        /// <summary>Just ticks around a circle, for going around something else.</summary>
        Ring,
        /// <summary>Ticks around a circle with a short reading inside.</summary>
        RingWithTime
    }

    static class TimerInk
    {
        public static readonly Color Red = Color.FromRgb(0xFF, 0x2D, 0x2D);

        /// <summary>A focus session's accent: calmer than the timer's red.</summary>
        public static readonly Color Focus = Color.FromRgb(0xA3, 0x8F, 0xFF);
    }

    /// <summary>
    /// One timer drawn in dots and ticks. Every view uses this, in the form that fits it,
    /// so they all read as the same timer. It only redraws when what it shows has changed.
    /// </summary>
    public sealed class TimerFace : FrameworkElement
    {
        static readonly Brush White = Frozen(Colors.White);
        static readonly Brush Dim = Frozen(Color.FromArgb(0x8C, 255, 255, 255));
        static readonly Brush Red = Frozen(TimerInk.Red);
        static readonly Brush Violet = Frozen(TimerInk.Focus);
        static readonly Brush TickOff = Frozen(Color.FromArgb(0x38, 255, 255, 255));

        static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        static Pen MakePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        string clock = "", shortText = "";
        double fraction;
        double headGlow = 1;
        bool paused, ringing, focus;

        public TimerFaceForm Form { get; set; }
        public int Ticks { get; set; } = 12;

        public TimerFace()
        {
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
        }

        public void Show(string clockText, string shortReading, double left, bool isPaused, bool isRinging, double head, bool isFocus = false)
        {
            // A tick is the smallest step that can be seen, unless the head is breathing
            bool same = clock == clockText && shortText == shortReading && paused == isPaused && ringing == isRinging &&
                        focus == isFocus && Math.Abs(fraction - left) * Ticks < 0.02 && Math.Abs(headGlow - head) < 0.03;

            if (same)
                return;

            clock = clockText;
            shortText = shortReading;
            fraction = left;
            paused = isPaused;
            ringing = isRinging;
            focus = isFocus;
            headGlow = head;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;

            if (w <= 0 || h <= 0)
                return;

            switch (Form)
            {
                case TimerFaceForm.Line:
                    {
                        double step = Math.Min(h / DotFont.Rows, w * 0.62 / Math.Max(DotFont.Columns(clock), 1));
                        double textWidth = DotFont.Columns(clock) * step;
                        DrawText(dc, clock, 0, (h - DotFont.Rows * step) / 2, step);
                        DrawTickRow(dc, textWidth + step * 3, w, h / 2, h * 0.42, Math.Max(step * 0.8, 1.5));
                        break;
                    }

                case TimerFaceForm.Stacked:
                    {
                        double tickRoom = Math.Max(h * 0.2, 4);
                        double step = Math.Min((h - tickRoom) / DotFont.Rows, w / Math.Max(DotFont.Columns(clock), 1));
                        double textWidth = DotFont.Columns(clock) * step;
                        DrawText(dc, clock, (w - textWidth) / 2, 0, step);
                        DrawTickRow(dc, 0, w, h - tickRoom * 0.35, tickRoom * 0.5, Math.Max(step * 0.65, 1.4));
                        break;
                    }

                case TimerFaceForm.Ring:
                    DrawTickRing(dc, w, h, Math.Min(w, h) * 0.085);
                    break;

                case TimerFaceForm.RingWithTime:
                    {
                        double length = Math.Min(w, h) * 0.085;
                        DrawTickRing(dc, w, h, length);
                        double inner = Math.Min(w, h) * 0.68;
                        double step = Math.Min(inner * 0.72 / DotFont.Rows, inner / Math.Max(DotFont.Columns(shortText), 1));
                        DrawText(dc, shortText,
                            (w - DotFont.Columns(shortText) * step) / 2,
                            (h - DotFont.Rows * step) / 2,
                            step);
                        break;
                    }
            }
        }

        void DrawText(DrawingContext dc, string text, double left, double top, double step)
        {
            Brush ink = paused ? Dim : White;
            double radius = step * 0.5;
            double x = left;

            foreach (char c in text)
            {
                if (DotFont.Glyph(c) is not string[] glyph)
                    continue;

                for (int row = 0; row < glyph.Length; row++)
                {
                    for (int column = 0; column < glyph[row].Length; column++)
                    {
                        if (glyph[row][column] == '#')
                        {
                            dc.DrawEllipse(ink, null,
                                new Point(x + (column + 0.5) * step, top + (row + 0.5) * step),
                                radius, radius);
                        }
                    }
                }

                x += (glyph[0].Length + 1) * step;
            }
        }

        // How many ticks are lit, and which is the red (or, focusing, violet) one
        int LitTicks => ringing ? Ticks : (int)Math.Ceiling(Math.Clamp(fraction, 0, 1) * Ticks);

        Pen TickPen(int index, int lit, double thickness)
        {
            Brush brush;

            if (ringing)
                brush = White;
            else if (index == lit - 1)
            {
                var accent = focus ? TimerInk.Focus : TimerInk.Red;
                brush = headGlow >= 0.99 ? (focus ? Violet : Red) : Frozen(Color.FromArgb((byte)(255 * Math.Clamp(headGlow, 0, 1)), accent.R, accent.G, accent.B));
            }
            else if (index < lit)
                brush = paused ? Dim : White;
            else
                brush = TickOff;

            return MakePen(brush, thickness);
        }

        void DrawTickRow(DrawingContext dc, double from, double to, double centerY, double length, double thickness)
        {
            if (to - from < thickness * 2)
                return;

            int lit = LitTicks;
            double gap = (to - from - thickness) / Math.Max(Ticks - 1, 1);

            for (int i = 0; i < Ticks; i++)
            {
                double x = from + thickness / 2 + i * gap;
                dc.DrawLine(TickPen(i, lit, thickness),
                    new Point(x, centerY - length / 2),
                    new Point(x, centerY + length / 2));
            }
        }

        void DrawTickRing(DrawingContext dc, double w, double h, double length)
        {
            int lit = LitTicks;
            double outer = Math.Min(w, h) / 2 - 1;
            double thickness = Math.Max(length * 0.45, 1.2);

            for (int i = 0; i < Ticks; i++)
            {
                double angle = (i / (double)Ticks) * 2 * Math.PI - Math.PI / 2;
                double cos = Math.Cos(angle), sin = Math.Sin(angle);

                dc.DrawLine(TickPen(i, lit, thickness),
                    new Point(w / 2 + cos * (outer - length), h / 2 + sin * (outer - length)),
                    new Point(w / 2 + cos * outer, h / 2 + sin * outer));
            }
        }
    }

    /// <summary>The finish alert: three white strips that flicker on the island's real outline.</summary>
    public sealed class GlyphOutline : FrameworkElement
    {
        const double Inset = 1.6;
        const double Thickness = 2.6;

        GlyphAlertStyle style;
        double seconds;

        public GlyphOutline()
        {
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
        }

        public void Show(GlyphAlertStyle alertStyle, double alertSeconds)
        {
            style = alertStyle;
            seconds = alertSeconds;
            Visibility = Visibility.Visible;
            InvalidateVisual();
        }

        public void Clear() => Visibility = Visibility.Collapsed;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;

            if (w <= Inset * 2 || h <= Inset * 2)
                return;

            double innerW = w - Inset * 2, innerH = h - Inset * 2;
            double radius = Math.Max(GlyphPattern.IslandRadius(w, h) - Inset, 0);

            for (int strip = 0; strip < GlyphPattern.Strips; strip++)
            {
                double brightness = GlyphPattern.Brightness(style, strip, seconds);

                if (brightness <= 0)
                    continue;

                var (start, end) = GlyphPattern.StripSpan(strip, innerW, innerH, radius);
                var geometry = new StreamGeometry();

                using (var context = geometry.Open())
                {
                    for (int p = 0; p <= 16; p++)
                    {
                        var (x, y) = GlyphPattern.PointOnOutline(innerW, innerH, radius, start + (end - start) * p / 16);
                        var point = new Point(x + Inset, y + Inset);

                        if (p == 0)
                            context.BeginFigure(point, false, false);
                        else
                            context.LineTo(point, true, false);
                    }
                }

                geometry.Freeze();

                // High mode's soft halo, drawn as a wider faint stroke rather than a costly effect
                if (style == GlyphAlertStyle.Bloom)
                    dc.DrawGeometry(null, Stroke(Colors.White, brightness * 0.28, Thickness * 2.6), geometry);

                dc.DrawGeometry(null, Stroke(Colors.White, brightness, Thickness), geometry);
            }
        }

        static Pen Stroke(Color color, double opacity, double thickness)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B));
            brush.Freeze();

            var pen = new Pen(brush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            pen.Freeze();
            return pen;
        }
    }

    /// <summary>
    /// The timer's section at the bottom of the expanded cards: choose a length when idle,
    /// the reading and its controls once it runs, and a dismiss button while it rings.
    /// </summary>
    public sealed class TimerCard : Grid
    {
        public const double SectionHeight = 44;

        static readonly int[] PresetMinutes = { 1, 5, 10 };

        public static readonly TimeSpan FocusLength = TimeSpan.FromMinutes(25);

        CountdownTimer? timer;

        readonly StackPanel setup = new() { Orientation = Orientation.Horizontal };
        readonly DockPanel live = new() { LastChildFill = false };
        readonly TextBlock minutesText = new()
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Width = 34,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        readonly Button pauseButton;
        readonly Button oneMoreButton;
        readonly Button fiveMoreButton;
        readonly Button stopButton;
        int customMinutes = 15;

        public TimerFace Face { get; } = new()
        {
            Form = TimerFaceForm.Stacked,
            Ticks = 24,
            Width = 132,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center
        };

        public TimerCard()
        {
            Height = SectionHeight;
            Visibility = Visibility.Collapsed;
            Children.Add(setup);
            Children.Add(live);
            setup.VerticalAlignment = VerticalAlignment.Bottom;
            live.VerticalAlignment = VerticalAlignment.Bottom;

            foreach (int minutes in PresetMinutes)
            {
                var preset = Chip(minutes + "m", "Start a " + minutes + " minute timer");
                preset.Margin = new Thickness(0, 0, 4, 0);
                preset.Click += (s, e) => timer?.Start(TimeSpan.FromMinutes(minutes));
                setup.Children.Add(preset);
            }

            // 25 minutes in which messages wait, then arrive together
            var focusChip = Chip("Focus", "Focus for 25 minutes: messages wait until it's done");
            focusChip.Margin = new Thickness(0, 0, 4, 0);
            focusChip.Click += (s, e) => timer?.Start(FocusLength, focus: true);
            setup.Children.Add(focusChip);

            // The island never takes keyboard focus, so a length is clicked or scrolled here
            var stepper = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 0, 4, 0),
                Background = Brushes.Transparent,
                ToolTip = "Click or scroll to set the minutes"
            };
            stepper.MouseWheel += Stepper_Wheel;

            var less = Icon("", "Fewer minutes");
            less.Click += (s, e) => StepMinutes(-1);
            var more = Icon("", "More minutes");
            more.Click += (s, e) => StepMinutes(1);

            stepper.Children.Add(less);
            stepper.Children.Add(minutesText);
            stepper.Children.Add(more);
            setup.Children.Add(stepper);

            var start = Chip("Start", "Start the timer");
            start.Click += (s, e) => timer?.Start(TimeSpan.FromMinutes(customMinutes));
            setup.Children.Add(start);
            minutesText.Text = customMinutes + "m";

            pauseButton = Icon("", "Pause");
            pauseButton.Click += (s, e) =>
            {
                if (timer?.State == CountdownState.Paused)
                    timer.Resume();
                else
                    timer?.Pause();
            };

            oneMoreButton = Chip("+1", "Add 1 minute");
            oneMoreButton.Click += (s, e) => timer?.AddTime(TimeSpan.FromMinutes(1));
            fiveMoreButton = Chip("+5", "Add 5 minutes");
            fiveMoreButton.Click += (s, e) => timer?.AddTime(TimeSpan.FromMinutes(5));
            stopButton = Icon("", "Cancel the timer");
            stopButton.Click += (s, e) => timer?.Stop();

            foreach (var button in new[] { stopButton, fiveMoreButton, oneMoreButton, pauseButton })
            {
                DockPanel.SetDock(button, Dock.Right);
                button.Margin = new Thickness(4, 0, 0, 0);
                live.Children.Add(button);
            }

            DockPanel.SetDock(Face, Dock.Left);
            live.Children.Add(Face);
        }

        public void Bind(CountdownTimer countdown) => timer = countdown;

        public void Sync(bool open)
        {
            var state = timer?.State ?? CountdownState.Idle;
            bool idle = state == CountdownState.Idle;

            Visibility = open || !idle ? Visibility.Visible : Visibility.Collapsed;
            setup.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            live.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;

            bool ringing = state == CountdownState.Ringing;
            pauseButton.Visibility = ringing ? Visibility.Collapsed : Visibility.Visible;
            pauseButton.Content = state == CountdownState.Paused ? "" : "";
            pauseButton.ToolTip = state == CountdownState.Paused ? "Resume" : "Pause";
            fiveMoreButton.Visibility = ringing ? Visibility.Collapsed : Visibility.Visible;
            oneMoreButton.Visibility = ringing ? Visibility.Collapsed : Visibility.Visible;
            stopButton.Visibility = ringing ? Visibility.Collapsed : Visibility.Visible;
            Face.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        }

        void StepMinutes(int change)
        {
            customMinutes = Math.Clamp(customMinutes + change, 1, 180);
            minutesText.Text = customMinutes + "m";
        }

        void Stepper_Wheel(object sender, MouseWheelEventArgs e)
        {
            // Here the wheel sets the minutes instead of the volume
            e.Handled = true;
            StepMinutes(e.Delta > 0 ? 1 : -1);
        }

        static Button Chip(string text, string tip)
        {
            var button = new Button { Content = text, ToolTip = tip, Focusable = false };
            button.SetResourceReference(StyleProperty, "TimerChip");
            return button;
        }

        static Button Icon(string glyph, string tip)
        {
            var button = new Button { Content = glyph, ToolTip = tip, Width = 30, Height = 30, FontSize = 12 };
            button.SetResourceReference(StyleProperty, "IconButton");
            return button;
        }
    }
}
