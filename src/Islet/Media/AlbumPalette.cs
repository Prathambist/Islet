using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet
{
    /// <summary>
    /// The colours a cover is known by: its main colour and a second, different one.
    ///
    /// Averaging every coloured pixel turns a red and blue cover into a muddy purple-grey,
    /// so instead pixels are grouped by hue, and the strongest group wins, favouring vivid
    /// pixels over dull ones.
    /// </summary>
    public static class AlbumPalette
    {
        public static readonly Color Fallback = Color.FromRgb(190, 165, 255);

        // Covers with no real colour, like black and white photos
        static readonly Color Neutral = Color.FromRgb(200, 200, 210);

        const int Buckets = 18;

        public static (Color Primary, Color Secondary) From(BitmapSource? source)
        {
            if (source == null)
                return (Fallback, Fallback);

            try
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int stride = converted.PixelWidth * 4;
                byte[] pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);

                return From(pixels, converted.PixelWidth, converted.PixelHeight, stride);
            }
            catch
            {
                return (Fallback, Fallback);
            }
        }

        /// <param name="pixels">BGRA, 4 bytes a pixel.</param>
        public static (Color Primary, Color Secondary) From(byte[] pixels, int width, int height, int stride)
        {
            var weight = new double[Buckets];
            var red = new double[Buckets];
            var green = new double[Buckets];
            var blue = new double[Buckets];

            int stepX = Math.Max(1, width / 48);
            int stepY = Math.Max(1, height / 48);

            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    int index = y * stride + x * 4;
                    byte b = pixels[index], g = pixels[index + 1], r = pixels[index + 2];

                    var (hue, saturation, value) = ToHsv(r, g, b);

                    // Near-black and grey pixels say nothing about the cover's colour
                    if (value < 0.14 || saturation < 0.12)
                        continue;

                    double w = saturation * saturation * value;
                    int bucket = (int)(hue / 360 * Buckets) % Buckets;

                    weight[bucket] += w;
                    red[bucket] += r * w;
                    green[bucket] += g * w;
                    blue[bucket] += b * w;
                }
            }

            // A hue on a bucket's edge is split between two, so each counts its neighbours too
            double Score(int k) => weight[k] + 0.5 * (weight[Wrap(k - 1)] + weight[Wrap(k + 1)]);

            int best = 0;

            for (int k = 1; k < Buckets; k++)
            {
                if (Score(k) > Score(best))
                    best = k;
            }

            if (Score(best) <= 0)
                return (Neutral, Neutral);

            // The second colour has to be clearly another hue, and not a stray speck
            int second = -1;

            for (int k = 0; k < Buckets; k++)
            {
                int apart = Math.Min(Math.Abs(k - best), Buckets - Math.Abs(k - best));

                if (apart >= 3 && Score(k) >= Score(best) * 0.15 && (second < 0 || Score(k) > Score(second)))
                    second = k;
            }

            var primary = Average(best);
            return (primary, second < 0 ? primary : Average(second));

            Color Average(int k)
            {
                double w = 0, r = 0, g = 0, b = 0;

                foreach (int i in new[] { Wrap(k - 1), k, Wrap(k + 1) })
                {
                    w += weight[i];
                    r += red[i];
                    g += green[i];
                    b += blue[i];
                }

                return Color.FromRgb((byte)(r / w), (byte)(g / w), (byte)(b / w));
            }
        }

        /// <summary>The same hue at a set lightness, with its saturation kept in a pleasant range.</summary>
        public static Color Tone(Color color, double lightness, double minSaturation = 0.4, double maxSaturation = 0.85)
        {
            var (hue, saturation, _) = ToHsl(color);

            // A grey stays grey rather than turning red
            if (saturation >= 0.08)
                saturation = Math.Clamp(saturation, minSaturation, maxSaturation);

            return FromHsl(hue, saturation, Math.Clamp(lightness, 0, 1));
        }

        static int Wrap(int k) => (k + Buckets) % Buckets;

        static (double Hue, double Saturation, double Value) ToHsv(byte r, byte g, byte b)
        {
            double max = Math.Max(r, Math.Max(g, b)) / 255.0;
            double min = Math.Min(r, Math.Min(g, b)) / 255.0;
            double delta = max - min;

            return (Hue(r / 255.0, g / 255.0, b / 255.0, max, delta), max <= 0 ? 0 : delta / max, max);
        }

        static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double lightness = (max + min) / 2;
            double saturation = delta <= 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));

            return (Hue(r, g, b, max, delta), Math.Clamp(saturation, 0, 1), lightness);
        }

        static double Hue(double r, double g, double b, double max, double delta)
        {
            if (delta <= 0)
                return 0;

            // Which channel is the brightest decides the sector of the colour wheel
            double hue = r >= g && r >= b ? (g - b) / delta % 6
                : g >= b ? (b - r) / delta + 2
                : (r - g) / delta + 4;

            hue *= 60;
            return hue < 0 ? hue + 360 : hue;
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

            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }
    }
}
