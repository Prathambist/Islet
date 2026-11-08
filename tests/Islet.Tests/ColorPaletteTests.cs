using System.IO;
using System.Windows.Media;

namespace Islet.Tests
{
    public sealed class ColorPaletteTests : IDisposable
    {
        readonly string folder = Path.Combine(Path.GetTempPath(), "IsletColorTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }

        [Fact]
        public void NewestFirstWithoutRepeats()
        {
            var palette = new ColorPalette(folder);
            var red = Color.FromRgb(255, 0, 0);
            var blue = Color.FromRgb(0, 0, 255);

            palette.Add(red);
            palette.Add(blue);
            palette.Add(red);

            Assert.Equal(new[] { red, blue }, palette.Colors);
        }

        [Fact]
        public void KeepsOnlyTheLatestTwelve()
        {
            var palette = new ColorPalette(folder);

            for (byte i = 0; i < 15; i++)
                palette.Add(Color.FromRgb(i, i, i));

            Assert.Equal(ColorPalette.MaxColors, palette.Colors.Count);
            Assert.Equal(Color.FromRgb(14, 14, 14), palette.Colors[0]);
        }

        [Fact]
        public void RemembersBetweenRuns()
        {
            new ColorPalette(folder).Add(Color.FromRgb(0x3A, 0x7B, 0xD5));

            Assert.Equal(Color.FromRgb(0x3A, 0x7B, 0xD5), Assert.Single(new ColorPalette(folder).Colors));
        }

        [Fact]
        public void FormatsAndReadsCodes()
        {
            var color = Color.FromRgb(0x3A, 0x7B, 0xD5);

            Assert.Equal("#3A7BD5", ColorPalette.Hex(color));
            Assert.Equal("rgb(58, 123, 213)", ColorPalette.Rgb(color));
            Assert.True(ColorPalette.TryParse("3a7bd5", out var parsed));
            Assert.Equal(color, parsed);
            Assert.False(ColorPalette.TryParse("#12345", out _));
            Assert.True(ColorPalette.IsLight(Colors.White));
            Assert.False(ColorPalette.IsLight(color));
        }
    }
}
