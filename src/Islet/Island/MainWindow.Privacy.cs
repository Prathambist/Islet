using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Islet
{
    public partial class MainWindow
    {
        readonly PrivacyWatcher privacy = new();

        static readonly Brush MicDotBrush = FrozenBrush(0xFF, 0x9F, 0x0A);
        static readonly Brush CameraDotBrush = FrozenBrush(0x30, 0xD1, 0x58);

        const double PrivacyGap = 6;

        void SetUpPrivacy()
        {
            privacy.Changed += ShowPrivacy;
        }

        // Called from ApplySettings, so the switch takes effect at once
        void SyncPrivacy()
        {
            if (Settings.ShowPrivacyDots)
                privacy.Start();
            else
                privacy.Stop();

            ShowPrivacy();
        }

        void ShowPrivacy()
        {
            var use = privacy.Current;
            bool hiddenForApp = fullscreen.ProcessName != null && Settings.ShouldHideFor(fullscreen.ProcessName);
            bool show = Settings.ShowPrivacyDots && use.Any && !hiddenForApp;

            PrivacyPill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PrivacyDot.Fill = use.CameraDot ? CameraDotBrush : MicDotBrush;
            PrivacyText.Text = use.Describe();

            if (!show)
                PrivacyLabel.Visibility = Visibility.Collapsed;

            PlacePrivacy();
        }

        // Follows the island every frame: just right of it (left of it on the right of
        // the screen), level with its top row, or where it would be when it's hidden
        void PlacePrivacy()
        {
            if (PrivacyPill.Visibility != Visibility.Visible)
                return;

            double w = Math.Max(1, width.Value);
            double scale = islandScale.Value * dragPop.Value;
            double visualLeft = islandLeft + islandOrigin * w * (1 - scale);
            bool islandShown = islandOpacity.Value > 0.5;
            double size = PrivacyPill.Height;

            double band = islandShown ? Math.Min(height.Value, 38) * scale : 30;
            double top = IslandContainer.Margin.Top + (band - size) / 2;
            double left = !islandShown ? islandLeft + w / 2 - size / 2
                : Left + islandLeft + w / 2 > ScreenWidth * 0.75 ? visualLeft - PrivacyGap - size
                : visualLeft + w * scale + PrivacyGap;

            PrivacyPill.Margin = new Thickness(left, top, 0, 0);

            if (PrivacyLabel.Visibility == Visibility.Visible)
            {
                // Centred under the dot, but kept inside the window
                double labelWidth = PrivacyLabel.ActualWidth;
                double labelLeft = Math.Clamp(left + size / 2 - labelWidth / 2, 4, Math.Max(4, Width - labelWidth - 4));
                PrivacyLabel.Margin = new Thickness(labelLeft, top + size + 5, 0, 0);
            }
        }

        void PrivacyPill_MouseEnter(object sender, MouseEventArgs e)
        {
            if (PrivacyText.Text.Length == 0)
                return;

            PrivacyLabel.Visibility = Visibility.Visible;
            PlacePrivacy();
        }

        void PrivacyPill_MouseLeave(object sender, MouseEventArgs e) =>
            PrivacyLabel.Visibility = Visibility.Collapsed;

        // Its width is only known after layout
        void PrivacyLabel_SizeChanged(object sender, SizeChangedEventArgs e) => PlacePrivacy();

        void PrivacyPill_Click(object sender, MouseButtonEventArgs e) =>
            OpenUrl(privacy.Current.CameraDot ? "ms-settings:privacy-webcam" : "ms-settings:privacy-microphone");
    }
}
