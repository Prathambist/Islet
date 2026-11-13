using System.Windows;
using System.Windows.Media;

namespace Islet
{
    /// <summary>
    /// A stand-in cover for a song, artist or album that has no picture (yet): a gradient in two
    /// colours worked out from its name, with its initials, so a list of them is never blank and
    /// the same name always looks the same.
    /// </summary>
    public static class GeneratedCover
    {
        public static (Brush Background, string Initials) For(string name, string? by = null)
        {
            uint hash = Hash((name + "|" + by).ToLowerInvariant());
            double hue = hash % 360;
            double turn = 30 + (hash >> 9) % 50;

            var brush = new LinearGradientBrush(
                FromHsl(hue, 0.55, 0.52),
                FromHsl((hue + turn) % 360, 0.6, 0.3),
                new Point(0, 0), new Point(1, 1));
            brush.Freeze();

            return (brush, Initials(name));
        }

        /// <summary>The main colour of the stand-in cover for this name.</summary>
        public static Color Colour(string name, string? by = null) =>
            ((LinearGradientBrush)For(name, by).Background).GradientStops[0].Color;

        /// <summary>"Let It Happen" → "LI", "Nights" → "N", "505" → "5"; "♪" with nothing to go on.</summary>
        public static string Initials(string name)
        {
            var words = name.Split(new[] { ' ', '-', '_', '/', '&', '+' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.FirstOrDefault(char.IsLetterOrDigit))
                .Where(c => c != default)
                .Take(2)
                .Select(char.ToUpperInvariant)
                .ToArray();

            return words.Length == 0 ? "♪" : new string(words);
        }

        // FNV-1a: the same everywhere and every run, unlike string.GetHashCode
        static uint Hash(string text)
        {
            uint hash = 2166136261;

            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return hash;
        }

        static Color FromHsl(double hue, double saturation, double lightness)
        {
            double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
            double m = lightness - chroma / 2;

            var (r, g, b) = (int)(hue / 60) switch
            {
                0 => (chroma, x, 0.0),
                1 => (x, chroma, 0.0),
                2 => (0.0, chroma, x),
                3 => (0.0, x, chroma),
                4 => (x, 0.0, chroma),
                _ => (chroma, 0.0, x)
            };

            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}
