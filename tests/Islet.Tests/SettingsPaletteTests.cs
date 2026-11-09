using System.Windows.Media;

namespace Islet.Tests
{
    public class SettingsPaletteTests
    {
        static double Lightness(Color c) =>
            (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / 2.0 / 255;

        [Fact]
        public void UnknownChoicesFallBackToGraphite()
        {
            Assert.Same(SettingsPalette.Graphite, SettingsPalette.Find(null, Colors.Red));
            Assert.Same(SettingsPalette.Graphite, SettingsPalette.Find("Neon", Colors.Red));
            Assert.Equal("Aurora", SettingsPalette.Find("Aurora", Colors.Red).Name);
        }

        [Fact]
        public void EveryPaletteStaysDarkSoWhiteTextReads()
        {
            var all = SettingsPalette.Dark.Append(SettingsPalette.Album(Color.FromRgb(240, 200, 60)));

            foreach (var palette in all)
            {
                Assert.True(Lightness(palette.Background) < 0.1, $"{palette.Name} background");
                Assert.True(Lightness(palette.Card) < 0.16, $"{palette.Name} cards");
                Assert.True(Lightness(palette.Accent) > 0.45, $"{palette.Name} accent");
            }
        }

        [Fact]
        public void LightPalettesAreLightWithDarkTextAndReadableAccents()
        {
            foreach (var palette in SettingsPalette.LightOnes)
            {
                Assert.True(palette.Light);
                Assert.True(Lightness(palette.Background) > 0.85, $"{palette.Name} background");
                Assert.True(Lightness(palette.Card) > 0.9, $"{palette.Name} cards");

                // Deep enough to read on white
                Assert.InRange(Lightness(palette.Accent), 0.25, 0.62);
                Assert.True(Lightness(palette.Ink.Text) < 0.2, $"{palette.Name} text");
            }

            Assert.True(Lightness(SettingsPalette.Graphite.Ink.Text) > 0.9);
            Assert.Equal("Sky", SettingsPalette.Find("Sky", Colors.Red).Name);
        }

        [Fact]
        public void AlbumFollowsTheCoversColour()
        {
            var blue = SettingsPalette.Album(Color.FromRgb(30, 80, 220));
            var red = SettingsPalette.Album(Color.FromRgb(220, 40, 40));

            Assert.Equal(SettingsPalette.AlbumName, blue.Name);
            Assert.True(blue.Accent.B > blue.Accent.R);
            Assert.True(red.Accent.R > red.Accent.B);
            Assert.Equal(SettingsPalette.AlbumName, SettingsPalette.Find(SettingsPalette.AlbumName, Colors.Teal).Name);
        }
    }
}
