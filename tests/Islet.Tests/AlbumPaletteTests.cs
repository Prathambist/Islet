using System.Windows.Media;

namespace Islet.Tests
{
    public class AlbumPaletteTests
    {
        // A width x height BGRA image, with each pixel coloured by its column
        static byte[] Image(int width, int height, Func<int, Color> column)
        {
            var pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var c = column(x);
                    int i = (y * width + x) * 4;
                    pixels[i] = c.B;
                    pixels[i + 1] = c.G;
                    pixels[i + 2] = c.R;
                    pixels[i + 3] = 255;
                }

            return pixels;
        }

        [Fact]
        public void TwoColoursStayApartInsteadOfMuddyingTogether()
        {
            // Three fifths red, two fifths blue
            var pixels = Image(50, 10, x => x < 30 ? Color.FromRgb(220, 30, 40) : Color.FromRgb(30, 60, 220));
            var (primary, secondary) = AlbumPalette.From(pixels, 50, 10, 200);

            Assert.True(primary.R > 180 && primary.B < 80, $"primary {primary}");
            Assert.True(secondary.B > 180 && secondary.R < 80, $"secondary {secondary}");
        }

        [Fact]
        public void VividBeatsALargerDullArea()
        {
            // Mostly a dull brown, with a band of bright green
            var pixels = Image(50, 10, x => x < 35 ? Color.FromRgb(90, 75, 60) : Color.FromRgb(40, 220, 90));
            var (primary, _) = AlbumPalette.From(pixels, 50, 10, 200);

            Assert.True(primary.G > 180, $"primary {primary}");
        }

        [Fact]
        public void BlackAndWhiteCoversStayNeutral()
        {
            var pixels = Image(20, 20, x => x % 2 == 0 ? Colors.Black : Colors.White);
            var (primary, secondary) = AlbumPalette.From(pixels, 20, 20, 80);

            Assert.Equal(primary, secondary);
            Assert.True(Math.Abs(primary.R - primary.B) < 20);
        }

        [Fact]
        public void ToneKeepsTheHueAtTheAskedLightness()
        {
            var dark = AlbumPalette.Tone(Color.FromRgb(220, 30, 40), 0.25);

            Assert.True(dark.R > dark.G * 3 && dark.R < 120, $"{dark}");
            Assert.Equal(Color.FromRgb(64, 64, 64), AlbumPalette.Tone(Color.FromRgb(128, 128, 128), 0.25));
        }
    }
}
