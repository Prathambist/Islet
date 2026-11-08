using System.Windows.Media;
using System.Windows.Threading;

namespace Islet
{
    /// <summary>What a quiet spell was: one of your focus sessions, or Windows' Do Not Disturb.</summary>
    public enum QuietReason
    {
        Focus,
        DoNotDisturb
    }

    /// <summary>What the card at the end of a quiet spell says.</summary>
    public static class FocusSummary
    {
        /// <summary>"3 messages while you focused" and "Discord 2 · WhatsApp 1", busiest app first.</summary>
        public static (string Title, string Detail) Describe(IReadOnlyList<string> apps, QuietReason reason = QuietReason.Focus)
        {
            string count = apps.Count == 1 ? "1 message" : $"{apps.Count} messages";
            string title = count + (reason == QuietReason.Focus ? " while you focused" : " while Do Not Disturb was on");

            string detail = string.Join(" · ", apps
                .GroupBy(a => a)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => $"{g.Key} {g.Count()}"));

            return (title, detail);
        }
    }

    public partial class MainWindow
    {
        // Messages that came in while it was quiet, in order
        readonly List<ChatNotification> heldForFocus = new();

        // Plenty for one quiet spell; past that, the oldest give way
        const int MostHeld = 50;

        static readonly Color FocusBadge = Color.FromRgb(0x6E, 0x5B, 0xE8);

        // Windows has no event for Do Not Disturb changing, so it's checked every couple of seconds
        readonly DispatcherTimer quietCheck = new() { Interval = TimeSpan.FromSeconds(2) };

        bool focusing;
        bool focusFinished;
        bool windowsQuiet;
        bool holding;

        bool HoldForFocus(ChatNotification message)
        {
            if (!holding)
                return false;

            heldForFocus.Add(message);

            if (heldForFocus.Count > MostHeld)
                heldForFocus.RemoveAt(0);

            return true;
        }

        void SetUpQuiet()
        {
            quietCheck.Tick += (s, e) => SyncQuiet();
            Closed += (s, e) => quietCheck.Stop();
        }

        // Called from ApplySettings, so the switch takes effect at once
        void SyncDoNotDisturb()
        {
            if (Settings.FollowDoNotDisturb)
                quietCheck.Start();
            else
                quietCheck.Stop();

            SyncQuiet();
        }

        void SyncQuiet()
        {
            bool quiet = Settings.FollowDoNotDisturb && DoNotDisturb.IsOn() == true;

            if (quiet == windowsQuiet)
                return;

            windowsQuiet = quiet;
            Log.Info($"Windows Do Not Disturb {(quiet ? "on" : "off")}");
            SyncHolding(QuietReason.DoNotDisturb);
        }

        // Called whenever the timer changes: notices a focus session starting and ending
        void SyncFocus()
        {
            bool now = countdown.Focus && TimerActive;

            // Remembered while it rings, so the card can say it ran its course
            if (countdown.State == CountdownState.Ringing)
                focusFinished = true;

            if (now == focusing)
                return;

            focusing = now;

            if (focusing)
                focusFinished = false;

            Log.Info(focusing ? "Focus session started" : $"Focus session {(focusFinished ? "finished" : "ended early")}");
            SyncHolding(QuietReason.Focus);
        }

        // Messages wait while either is on; the summary comes once both are over,
        // named for whichever ended last
        void SyncHolding(QuietReason changed)
        {
            bool now = focusing || windowsQuiet;

            if (now == holding)
                return;

            holding = now;

            if (holding)
            {
                heldForFocus.Clear();
                return;
            }

            ShowFocusSummary(changed);
        }

        // One card for everything that waited; clicking it plays them in
        void ShowFocusSummary(QuietReason reason)
        {
            if (heldForFocus.Count == 0)
                return;

            var held = heldForFocus.ToList();
            heldForFocus.Clear();

            var (title, detail) = FocusSummary.Describe(held.Select(m => m.AppName).ToList(), reason);

            string header = reason == QuietReason.DoNotDisturb ? "Do Not Disturb is off"
                : focusFinished ? "Focus done"
                : "Focus ended";

            Enqueue(new NotifyItem(
                header,
                title,
                detail + " · click to see them",
                null,
                FocusBadge,
                0xE708,
                () =>
                {
                    foreach (var message in held)
                        EnqueueChat(message);
                }));
        }
    }
}
