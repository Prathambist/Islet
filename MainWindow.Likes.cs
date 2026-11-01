using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MusicIsland
{
    public partial class MainWindow
    {
        string? likeUri;
        bool liked;
        bool likeBusy;

        // ================= Like / heart =================

        async Task RefreshLikedAsync(string key, TrackInfo info)
        {
            string title = info.Title;

            // Only Spotify (app, or Spotify Web in a browser) can be liked
            if (info.Source is not (MediaSource.Spotify or MediaSource.Browser))
                return;

            HeartButton.ToolTip = spotifyWeb.IsConnected
                ? "Add to Liked Songs"
                : "Connect Spotify in Settings to like songs";

            if (!spotifyWeb.IsConnected)
                return;

            try
            {
                // The Web API can lag a moment behind the desktop app on skips
                for (int attempt = 0; attempt < 4 && key == trackKey; attempt++)
                {
                    var current = await spotifyWeb.GetCurrentTrackAsync();

                    if (current != null &&
                        string.Equals(current.Value.Title, title, StringComparison.OrdinalIgnoreCase))
                    {
                        var saved = await spotifyWeb.IsSavedAsync(current.Value.Uri);

                        if (key == trackKey)
                        {
                            likeUri = current.Value.Uri;
                            SetLiked(saved == true, animate: false);
                            UpdateSourceSlot();
                        }

                        return;
                    }

                    await Task.Delay(1200);
                }
            }
            catch
            {
            }
        }

        async void Heart_Click(object sender, RoutedEventArgs e)
        {
            if (!spotifyWeb.IsConnected)
            {
                OpenSettings();
                return;
            }

            if (likeBusy || track == null)
                return;

            likeBusy = true;

            try
            {
                if (likeUri == null)
                    await RefreshLikedAsync(trackKey, track);

                if (likeUri == null)
                    return;

                bool target = !liked;
                SetLiked(target, animate: true);

                if (!await spotifyWeb.SetSavedAsync(likeUri, target))
                    SetLiked(!target, animate: false);
            }
            catch
            {
                SetLiked(!liked, animate: false);
            }
            finally
            {
                likeBusy = false;
            }
        }

        // The heart is shown for Spotify (and for browsers once the Web API confirms
        // Spotify Web is playing this song); other players show their name instead
        void UpdateSourceSlot()
        {
            bool spotify = track?.Source == MediaSource.Spotify || likeUri != null;
            bool showHeart = track != null && spotify;

            HeartSlot.Visibility = showHeart ? Visibility.Visible : Visibility.Collapsed;
            SourceLabel.Visibility = track != null && !showHeart ? Visibility.Visible : Visibility.Collapsed;
            SourceText.Text = track?.SourceName ?? "";

            // Globe for browsers, music note for apps
            SourceGlyph.Text = ((char)(track?.Source == MediaSource.Browser ? 0xE774 : 0xE8D6)).ToString();

            // Title / artist links search Spotify, so only offer them for Spotify
            TrackTitle.Cursor = TrackArtist.Cursor = spotify ? Cursors.Hand : null;
            TrackTitle.ToolTip = spotify ? "Open album in Spotify" : null;
            TrackArtist.ToolTip = spotify ? "Open artist in Spotify" : null;
        }

        void SetLiked(bool value, bool animate)
        {
            liked = value;
            HeartGlyph.Text = value ? "\uEB52" : "\uEB51";
            HeartGlyph.Foreground = value
                ? new SolidColorBrush(Color.FromRgb(255, 55, 95))
                : new SolidColorBrush(Color.FromArgb(204, 255, 255, 255));
            HeartButton.ToolTip = value ? "Remove from Liked Songs" : "Add to Liked Songs";

            if (!animate)
                return;

            // Squash then spring back with overshoot
            heartScale.Value = value ? 0.45 : 0.75;
            heartScale.Velocity = value ? 6 : 0;
            heartScale.Target = 1;

            if (value)
            {
                var duration = TimeSpan.FromMilliseconds(520);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var grow = new DoubleAnimation(0.4, 1.25, duration) { EasingFunction = ease };

                HeartRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                HeartRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                HeartRing.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.9, 0, duration) { EasingFunction = ease });
            }

            EnsureRendering();
        }
    }
}
