using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet.Tests
{
    public class CoverBackdropTests
    {
        [Fact]
        public void BlurKeepsTheCoverColours()
        {
            byte[]? pixels = null;

            // WPF drawing needs an STA thread
            var thread = new Thread(() =>
            {
                var red = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null,
                    Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 16).SelectMany(p => p).ToArray(), 16);

                var blurred = MainWindow.BlurredCover(red);
                pixels = new byte[4];
                blurred.CopyPixels(new System.Windows.Int32Rect(60, 60, 1, 1), pixels, 4, 0);
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            // Middle pixel: still red, fully opaque
            Assert.True(pixels![2] > 200 && pixels[1] < 40 && pixels[0] < 40 && pixels[3] == 255,
                $"BGRA {string.Join(",", pixels)}");
        }
    }
}
