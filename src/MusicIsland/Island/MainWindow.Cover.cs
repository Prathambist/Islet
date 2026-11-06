using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace MusicIsland
{
    public partial class MainWindow
    {
        // The cover the blurred background was made from, so each song is blurred once
        BitmapSource? blurredFrom;

        // Whenever the open card shows no lyrics (they're off, the song has none, or Low mode
        // leaves them out), it shows the song's cover, blurred and darkened, behind everything,
        // like Apple Music. Called when the art, the lyrics or the settings change.
        void UpdateCoverBackdrop()
        {
            var art = AlbumArt.Source as BitmapSource;
            bool showingLyrics = LyricRow.Visibility == Visibility.Visible || LyricStage.Visibility == Visibility.Visible;
            bool show = !showingLyrics && art != null;

            if (show && art != blurredFrom)
            {
                blurredFrom = art;
                CoverBackdropArt.Source = BlurredCover(art!);
            }

            // Faded, since a new song's lyrics arrive a moment after the song does
            double target = show ? 1 : 0;

            if (show)
                CoverBackdrop.Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(target, TimeSpan.FromMilliseconds(260));

            if (!show)
                fade.Completed += (s, e) =>
                {
                    if (CoverBackdrop.Opacity == 0)
                        CoverBackdrop.Visibility = Visibility.Collapsed;
                };

            CoverBackdrop.BeginAnimation(OpacityProperty, fade);
        }

        // Blurred once into a small bitmap rather than with a live effect, so the card's
        // animations don't redo the blur every frame; scaling it up softens it further
        internal static BitmapSource BlurredCover(BitmapSource art)
        {
            const int size = 120;
            const double overscan = size * 0.2;

            // Drawn larger than the bitmap, so the blur's faded edges fall outside it
            var image = new Image
            {
                Source = art,
                Stretch = Stretch.UniformToFill,
                Width = size + overscan * 2,
                Height = size + overscan * 2,
                Effect = new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Quality }
            };

            var host = new Canvas { Width = size, Height = size, ClipToBounds = true, Background = Brushes.Black };
            Canvas.SetLeft(image, -overscan);
            Canvas.SetTop(image, -overscan);
            host.Children.Add(image);

            host.Measure(new Size(size, size));
            host.Arrange(new Rect(0, 0, size, size));

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
