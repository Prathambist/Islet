namespace Islet.Tests
{
    public class DockTests
    {
        [Theory]
        [InlineData(960, 236, 842)]   // centred on its spot
        [InlineData(40, 236, 10)]     // pushed in from the left side
        [InlineData(1900, 400, 1510)] // and from the right, however wide it grows
        public void IslandStaysOnScreen(double x, double width, double expected) =>
            Assert.Equal(expected, MainWindow.IslandLeftFor(x, width, 1920));

        [Theory]
        [InlineData(40, 400)]
        [InlineData(230, 400)]
        [InlineData(960, 400)]
        [InlineData(1700, 400)]
        [InlineData(1910, 112)]
        public void WindowAlwaysHoldsTheIsland(double x, double width)
        {
            double island = MainWindow.IslandLeftFor(x, width, 1920);
            double window = MainWindow.WindowLeftFor(x, 500, 1920);

            Assert.InRange(window, 0, 1420);
            Assert.True(island >= window && island + width <= window + 500);
        }

        [Theory]
        [InlineData(960, true)]
        [InlineData(975, true)]
        [InlineData(990, false)]
        [InlineData(300, false)]
        public void SnapsOnlyCloseToTheMiddle(double x, bool expected) =>
            Assert.Equal(expected, MainWindow.NearCentre(x, 1920));
    }
}
