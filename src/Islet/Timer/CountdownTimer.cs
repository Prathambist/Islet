using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Islet
{
    public enum CountdownState
    {
        Idle,
        Running,
        Paused,
        Ringing
    }

    /// <summary>What it takes to carry a timer over a restart. Only one still counting is ever saved.</summary>
    public sealed record SavedCountdown(CountdownState State, DateTimeOffset EndsAt, TimeSpan Remaining, TimeSpan Total, bool Focus = false);

    /// <summary>
    /// Counts down to an absolute end time, so a late tick, a busy UI thread or the PC
    /// sleeping can't make it drift. Knows nothing about how it is shown.
    /// </summary>
    public sealed class CountdownTimer
    {
        public static readonly TimeSpan AlertLength = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan Longest = TimeSpan.FromHours(24);

        /// <summary>A timer that ended this recently while the app was closed still rings at the next start.</summary>
        public static readonly TimeSpan MissedLimit = TimeSpan.FromHours(1);

        readonly Func<DateTimeOffset> clock;
        DateTimeOffset endsAt;
        DateTimeOffset ringingSince;
        TimeSpan pausedRemaining;

        public CountdownTimer(Func<DateTimeOffset>? clock = null)
        {
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public CountdownState State { get; private set; }

        /// <summary>The full length, including time added since it started.</summary>
        public TimeSpan Total { get; private set; }

        /// <summary>A focus session: messages wait until it's over. Cleared once it's idle again.</summary>
        public bool Focus { get; private set; }

        public event Action? Changed;

        public TimeSpan Remaining => State switch
        {
            CountdownState.Running => Max(endsAt - clock(), TimeSpan.Zero),
            CountdownState.Paused => pausedRemaining,
            _ => TimeSpan.Zero
        };

        /// <summary>How much is left, from 1 at the start to 0 at the end.</summary>
        public double Fraction =>
            Total <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining / Total, 0, 1);

        /// <summary>How long until the shown time next changes, so ticks land on the second.</summary>
        public TimeSpan UntilNextSecond
        {
            get
            {
                if (State != CountdownState.Running)
                    return TimeSpan.FromSeconds(1);

                long intoSecond = Remaining.Ticks % TimeSpan.TicksPerSecond;
                return TimeSpan.FromTicks(intoSecond == 0 ? TimeSpan.TicksPerSecond : intoSecond);
            }
        }

        /// <summary>What to save right now, or null when nothing is counting.</summary>
        public SavedCountdown? Snapshot => State is CountdownState.Running or CountdownState.Paused
            ? new SavedCountdown(State, endsAt, Remaining, Total, Focus)
            : null;

        /// <summary>Picks up a saved timer, ignoring one that makes no sense or ended too long ago.</summary>
        public void Restore(SavedCountdown? saved)
        {
            if (saved == null || State != CountdownState.Idle || saved.Total <= TimeSpan.Zero || saved.Total > Longest)
                return;

            var now = clock();

            if (saved.State == CountdownState.Paused)
            {
                if (saved.Remaining <= TimeSpan.Zero || saved.Remaining > Longest)
                    return;

                Total = Max(saved.Total, saved.Remaining);
                pausedRemaining = saved.Remaining;
                Focus = saved.Focus;
                Set(CountdownState.Paused);
            }
            else if (saved.State == CountdownState.Running)
            {
                if (now - saved.EndsAt > MissedLimit || saved.EndsAt - now > Longest)
                    return;

                Total = saved.Total;
                endsAt = saved.EndsAt;
                ringingSince = now;
                Focus = saved.Focus;
                Set(now >= endsAt ? CountdownState.Ringing : CountdownState.Running);
            }
        }

        public void Start(TimeSpan duration, bool focus = false)
        {
            if (duration <= TimeSpan.Zero)
                return;

            Focus = focus;
            Total = Min(duration, Longest);
            endsAt = clock() + Total;
            Set(CountdownState.Running);
        }

        public void Pause()
        {
            if (State != CountdownState.Running)
                return;

            pausedRemaining = Remaining;
            Set(CountdownState.Paused);
        }

        public void Resume()
        {
            if (State != CountdownState.Paused)
                return;

            endsAt = clock() + pausedRemaining;
            Set(CountdownState.Running);
        }

        /// <summary>Extends it while counting. On a ringing timer, this counts that time again.</summary>
        public void AddTime(TimeSpan amount)
        {
            if (amount <= TimeSpan.Zero)
                return;

            switch (State)
            {
                case CountdownState.Running:
                    var running = Min(Remaining + amount, Longest);
                    Total = Max(Total, running);
                    endsAt = clock() + running;
                    Changed?.Invoke();
                    break;

                case CountdownState.Paused:
                    pausedRemaining = Min(pausedRemaining + amount, Longest);
                    Total = Max(Total, pausedRemaining);
                    Changed?.Invoke();
                    break;

                case CountdownState.Ringing:
                    Start(amount, Focus);
                    break;
            }
        }

        /// <summary>Ends it, whether it is counting, paused or ringing.</summary>
        public void Stop()
        {
            if (State != CountdownState.Idle)
                Set(CountdownState.Idle);
        }

        /// <summary>Moves on to ringing, or back to idle, when the time for that has come.</summary>
        public void Update()
        {
            var now = clock();

            if (State == CountdownState.Running && now >= endsAt)
            {
                // Counted from now, so waking the PC late still gives the whole alert
                ringingSince = now;
                Set(CountdownState.Ringing);
            }
            else if (State == CountdownState.Ringing && now - ringingSince >= AlertLength)
            {
                Set(CountdownState.Idle);
            }
        }

        void Set(CountdownState state)
        {
            if (state == CountdownState.Idle)
                Focus = false;

            State = state;
            Changed?.Invoke();
        }

        static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
    }

    /// <summary>
    /// Keeps the running timer in its own file, because saving settings makes the whole app
    /// reapply them, and a timer changes far more casually than settings do.
    /// </summary>
    public static class CountdownStore
    {
        public static string DefaultPath => Path.Combine(AppSettings.Folder, "timer.json");

        static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static SavedCountdown? Load(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<SavedCountdown>(File.ReadAllText(path), options)
                    : null;
            }
            catch (Exception ex)
            {
                Log.Error("Reading the saved timer failed", ex);
                return null;
            }
        }

        /// <summary>Writes the timer, or removes the file when there is nothing to keep.</summary>
        public static void Save(string path, SavedCountdown? saved)
        {
            try
            {
                if (saved == null)
                {
                    File.Delete(path);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(saved, options));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex)
            {
                // The timer itself still works; it just wouldn't survive a restart
                Log.Error("Saving the timer failed", ex);
            }
        }
    }

    public static class TimerText
    {
        // Rounds up, so it shows 0:00 only at the very end and the full length at the start
        static int WholeSeconds(TimeSpan time) => (int)Math.Ceiling(Math.Max(time.TotalSeconds, 0));

        /// <summary>The full reading: 0:45, 12:30, 1:05:00.</summary>
        public static string Clock(TimeSpan remaining)
        {
            int seconds = WholeSeconds(remaining);
            int hours = seconds / 3600;
            int minutes = seconds / 60 % 60;

            return hours > 0
                ? $"{hours}:{minutes:00}:{seconds % 60:00}"
                : $"{minutes}:{seconds % 60:00}";
        }

        /// <summary>At most two characters, for inside the small circle: 45 seconds, 12 minutes, 3H.</summary>
        public static string Short(TimeSpan remaining)
        {
            int seconds = WholeSeconds(remaining);

            if (seconds < 60)
                return seconds.ToString(CultureInfo.InvariantCulture);

            if (seconds <= 3600)
                return ((seconds + 59) / 60).ToString(CultureInfo.InvariantCulture);

            int hours = (seconds + 3599) / 3600;
            return hours >= 10 ? hours.ToString(CultureInfo.InvariantCulture) : hours + "H";
        }

        /// <summary>
        /// Reads a typed length. A plain number means minutes. Also takes 1:30 (minutes and
        /// seconds), 1:00:00, 90s, 25m, 2h and 1h30.
        /// </summary>
        public static bool TryParse(string? input, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            string text = (input ?? "").Trim().ToLowerInvariant().Replace(" ", "");

            if (text.Length == 0)
                return false;

            double seconds;

            if (text.Contains(':'))
            {
                var parts = text.Split(':');

                if (parts.Length > 3 || !parts.All(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                    return false;

                seconds = parts.Aggregate(0.0, (sum, part) => sum * 60 + int.Parse(part, CultureInfo.InvariantCulture));
            }
            else if (!TryParseUnits(text, out seconds))
            {
                return false;
            }

            if (seconds < 1 || seconds > CountdownTimer.Longest.TotalSeconds)
                return false;

            duration = TimeSpan.FromSeconds(Math.Round(seconds));
            return true;
        }

        static bool TryParseUnits(string text, out double seconds)
        {
            seconds = 0;
            int hourAt = text.IndexOf('h');

            if (hourAt >= 0)
            {
                if (!double.TryParse(text[..hourAt], NumberStyles.Float, CultureInfo.InvariantCulture, out double hours) || hours < 0)
                    return false;

                seconds = hours * 3600;
                text = text[(hourAt + 1)..];

                if (text.Length == 0)
                    return true;
            }

            double unit = 60;

            if (text.EndsWith('s'))
            {
                unit = 1;
                text = text[..^1];
            }
            else if (text.EndsWith('m'))
            {
                text = text[..^1];
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount) || amount < 0)
                return false;

            seconds += amount * unit;
            return true;
        }
    }

    /// <summary>The 5x7 dots each timer character is drawn from. A set bit is a lit dot, left to right.</summary>
    public static class DotFont
    {
        public const int Rows = 7;

        static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { ".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            [':'] = new[] { ".", ".", "#", ".", "#", ".", "." }
        };

        public static string[]? Glyph(char c) => Glyphs.TryGetValue(c, out var glyph) ? glyph : null;

        /// <summary>Width of the text in dots, with one empty column between characters.</summary>
        public static int Columns(string text)
        {
            int columns = 0;

            foreach (char c in text)
            {
                if (Glyph(c) is string[] glyph)
                    columns += glyph[0].Length + 1;
            }

            return Math.Max(columns - 1, 0);
        }
    }

    public enum GlyphAlertStyle
    {
        Flash,
        Fade,
        Bloom
    }

    /// <summary>Three short Glyph-style light strips on the island's outline, as numbers only.</summary>
    public static class GlyphPattern
    {
        public const int Strips = 3;

        /// <summary>The length of one round of flickers; the sound loops at the same length.</summary>
        public const double LoopSeconds = 1.6;

        const double FadeSeconds = 0.09;

        /// <summary>One flicker: when it starts, how long it stays lit, and which strips it lights.</summary>
        public readonly record struct Pulse(double Start, double Length, bool A, bool B, bool C)
        {
            public bool Lights(int strip) => strip switch { 0 => A, 1 => B, _ => C };
        }

        /// <summary>The strips tick on one by one, stutter together, then go dark until the next round.</summary>
        public static readonly IReadOnlyList<Pulse> Pulses = new[]
        {
            new Pulse(0.00, 0.06, true, false, false),
            new Pulse(0.11, 0.06, false, true, false),
            new Pulse(0.22, 0.06, false, false, true),
            new Pulse(0.36, 0.05, true, true, true),
            new Pulse(0.44, 0.05, true, true, true),
            new Pulse(0.52, 0.14, true, true, true)
        };

        /// <summary>The island's corner radius at a size: a capsule when small, a rounded card when open.</summary>
        public static double IslandRadius(double width, double height)
        {
            double radius = Math.Min(height / 2, 19 + (height - 38) * 0.16);
            return Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        }

        /// <summary>
        /// A point on the rounded rectangle's edge, walking clockwise from the top center,
        /// where <paramref name="fraction"/> runs once around from 0 to 1.
        /// </summary>
        public static (double X, double Y) PointOnOutline(double width, double height, double radius, double fraction)
        {
            radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);

            double straightX = width - 2 * radius;
            double straightY = height - 2 * radius;
            double arc = Math.PI / 2 * radius;
            double perimeter = 2 * straightX + 2 * straightY + 4 * arc;

            double left = (fraction - Math.Floor(fraction)) * perimeter;

            // Each stretch of the walk: half the top, then corner, side, corner and so on
            if (left <= straightX / 2)
                return (width / 2 + left, 0);
            left -= straightX / 2;

            if (left <= arc)
                return Corner(width - radius, radius, radius, -90, left, arc);
            left -= arc;

            if (left <= straightY)
                return (width, radius + left);
            left -= straightY;

            if (left <= arc)
                return Corner(width - radius, height - radius, radius, 0, left, arc);
            left -= arc;

            if (left <= straightX)
                return (width - radius - left, height);
            left -= straightX;

            if (left <= arc)
                return Corner(radius, height - radius, radius, 90, left, arc);
            left -= arc;

            if (left <= straightY)
                return (0, height - radius - left);
            left -= straightY;

            if (left <= arc)
                return Corner(radius, radius, radius, 180, left, arc);
            left -= arc;

            return (radius + left, 0);
        }

        static (double X, double Y) Corner(double cx, double cy, double radius, double startDegrees, double along, double arc)
        {
            double angle = (startDegrees + (arc <= 0 ? 0 : along / arc * 90)) * Math.PI / 180;
            return (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
        }

        /// <summary>
        /// Where a strip starts and ends around the outline: an arc on each top corner and a bar
        /// along the bottom, with long dark stretches between them on any shape of island.
        /// </summary>
        public static (double Start, double End) StripSpan(int strip, double width, double height, double radius)
        {
            radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);

            double straightX = width - 2 * radius;
            double straightY = height - 2 * radius;
            double arc = Math.PI / 2 * radius;
            double perimeter = 2 * straightX + 2 * straightY + 4 * arc;

            if (perimeter <= 0)
                return (0, 0);

            double topRight = straightX / 2 + arc / 2;
            var (center, longest, share) = strip switch
            {
                0 => (perimeter - topRight, 64.0, 0.13),
                1 => (topRight, 40.0, 0.09),
                _ => (perimeter / 2, 52.0, 0.11)
            };

            double half = Math.Min(longest, perimeter * share) / 2;
            return ((center - half) / perimeter, (center + half) / perimeter);
        }

        /// <summary>How bright a strip is, from 0 to 1, this far into the alert. Dark between flickers.</summary>
        public static double Brightness(GlyphAlertStyle style, int strip, double seconds)
        {
            double t = Math.Max(seconds, 0) % LoopSeconds;
            double level = 0;

            foreach (var pulse in Pulses)
            {
                if (!pulse.Lights(strip) || t < pulse.Start)
                    continue;

                double after = t - pulse.Start - pulse.Length;

                if (after <= 0)
                    return 1;

                // Hard on and off in Low; a quick LED-like tail otherwise
                if (style != GlyphAlertStyle.Flash && after < FadeSeconds)
                    level = Math.Max(level, 1 - after / FadeSeconds);
            }

            return level;
        }
    }

    /// <summary>The alert's sound, made here so the repo needs no audio file: a digital blip for each flicker.</summary>
    public static class GlyphSound
    {
        public const int SampleRate = 44100;

        const double Volume = 0.45;
        static readonly double[] StripPitch = { 1760, 2093, 2637 };

        /// <summary>One round of the pattern as a mono 16-bit WAV file, ready to loop.</summary>
        public static byte[] BuildWav()
        {
            int count = (int)Math.Round(GlyphPattern.LoopSeconds * SampleRate);
            var samples = new double[count];

            foreach (var pulse in GlyphPattern.Pulses)
            {
                var pitches = Enumerable.Range(0, GlyphPattern.Strips)
                    .Where(pulse.Lights)
                    .Select(strip => StripPitch[strip])
                    .ToList();

                AddBlip(samples, pulse, pitches);
            }

            using var stream = new MemoryStream(44 + count * 2);
            using var writer = new BinaryWriter(stream);

            writer.Write("RIFF"u8);
            writer.Write(36 + count * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(count * 2);

            foreach (double sample in samples)
                writer.Write((short)(Math.Clamp(sample, -1, 1) * short.MaxValue));

            writer.Flush();
            return stream.ToArray();
        }

        static void AddBlip(double[] samples, GlyphPattern.Pulse pulse, List<double> pitches)
        {
            int start = (int)(pulse.Start * SampleRate);
            int length = (int)((pulse.Length + 0.03) * SampleRate);

            for (int i = 0; i < length && start + i < samples.Length; i++)
            {
                double t = (double)i / SampleRate;

                // A 1 ms click in, then a fast decay
                double envelope = Math.Min(t / 0.001, 1) * Math.Exp(-t / (pulse.Length * 0.45));
                double tone = 0;

                foreach (double pitch in pitches)
                {
                    // An odd harmonic gives it a hard electronic edge instead of a soft beep
                    tone += Math.Sin(2 * Math.PI * pitch * t) + 0.3 * Math.Sin(2 * Math.PI * pitch * 3 * t);
                }

                samples[start + i] += Volume * envelope * tone / (1.3 * pitches.Count);
            }
        }
    }
}
