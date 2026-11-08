using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Islet
{
    public partial class MainWindow
    {
        readonly CountdownTimer countdown = new();
        readonly DispatcherTimer countdownTicker = new();
        readonly DispatcherTimer alertFlasher = new() { Interval = TimeSpan.FromMilliseconds(30) };
        readonly Stopwatch alertClock = new();
        SoundPlayer? alertSound;
        bool alertSoundOn;

        TimerFace[] timerFaces = Array.Empty<TimerFace>();
        bool timerPanelOpen;

        bool TimerActive => countdown.State != CountdownState.Idle;
        bool TimerRinging => countdown.State == CountdownState.Ringing;

        void SetUpTimer()
        {
            timerFaces = new[]
            {
                CompactTimer, SmallTimer, DiscTimer, OrbTimer, AmbientTimer, NotchTimer, PeekTimer, PillTimer,
                MusicTimerCard.Face, AmbientTimerCard.Face
            };

            MusicTimerCard.Bind(countdown);
            AmbientTimerCard.Bind(countdown);

            countdown.Changed += OnCountdownChanged;
            countdownTicker.Tick += (s, e) => TickCountdown();
            alertFlasher.Tick += (s, e) => DrawAlert();
        }

        // After a restart, carry on with whatever was counting when the app last closed
        void RestoreTimer()
        {
            countdown.Restore(CountdownStore.Load(CountdownStore.DefaultPath));

            // A saved timer too old to use would otherwise stay on disk
            CountdownStore.Save(CountdownStore.DefaultPath, countdown.Snapshot);
        }

        void OnCountdownChanged()
        {
            CountdownStore.Save(CountdownStore.DefaultPath, countdown.Snapshot);
            SyncFocus();

            bool active = TimerActive;
            var shown = active ? Visibility.Visible : Visibility.Collapsed;

            // Slots collapse when idle, so every view looks exactly as it does without a timer
            foreach (var face in timerFaces)
                face.Visibility = shown;

            CompactAmbientInfo.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
            OrbContent.Visibility = active ? Visibility.Hidden : Visibility.Visible;

            if (!active)
                timerPanelOpen = false;

            SyncTimerCard();
            SyncAlert();
            ScheduleCountdownTick();
            RefreshTimerFaces();
            UpdateSurface();
            EnsureRendering();
        }

        void SyncTimerCard()
        {
            MusicTimerCard.Sync(timerPanelOpen);
            AmbientTimerCard.Sync(timerPanelOpen);
            ApplyLyricsLayout();

            ExpandedAmbient.Height = AmbientTimerCard.Visibility == Visibility.Visible
                ? 150 + TimerCard.SectionHeight
                : 150;

            if (surface == Surface.ExpandedAmbient)
            {
                height.Target = ExpandedAmbient.Height;
                EnsureRendering();
            }
        }

        void TimerToggle_Click(object sender, RoutedEventArgs e)
        {
            timerPanelOpen = !timerPanelOpen;
            SyncTimerCard();
        }

        void ScheduleCountdownTick()
        {
            countdownTicker.Stop();

            if (countdown.State == CountdownState.Running)
                countdownTicker.Interval = countdown.UntilNextSecond;
            else if (TimerRinging)
                countdownTicker.Interval = TimeSpan.FromMilliseconds(250);
            else
                return;

            countdownTicker.Start();
        }

        void TickCountdown()
        {
            countdown.Update();

            if (!TimerActive)
                return;

            RefreshTimerFaces();

            if (countdown.State == CountdownState.Running)
                countdownTicker.Interval = countdown.UntilNextSecond;
        }

        void RefreshTimerFaces(double headGlow = 1)
        {
            if (!TimerActive)
                return;

            var remaining = countdown.Remaining;
            string clock = TimerText.Clock(remaining);
            string reading = TimerText.Short(remaining);
            double fraction = countdown.Fraction;
            bool paused = countdown.State == CountdownState.Paused;

            foreach (var face in timerFaces)
            {
                // Faces in hidden views catch up the next second, or when they come back
                if (face.IsVisible)
                    face.Show(clock, reading, fraction, paused, TimerRinging, headGlow, countdown.Focus);
            }
        }

        // Keeps every face current when the view they sit in has just appeared
        void RefreshAllTimerFaces()
        {
            if (!TimerActive)
                return;

            var remaining = countdown.Remaining;

            foreach (var face in timerFaces)
                face.Show(TimerText.Clock(remaining), TimerText.Short(remaining), countdown.Fraction,
                    countdown.State == CountdownState.Paused, TimerRinging, 1, countdown.Focus);
        }

        // Medium and High: the red tick breathes and the ticks move between seconds
        bool StepTimer()
        {
            if (countdown.State != CountdownState.Running || !Profile.TimerMotion)
                return false;

            RefreshTimerFaces(0.8 + 0.2 * Math.Cos(lastFrame.TotalSeconds * Math.PI));
            return true;
        }

        // ================= Finish alert =================

        void SyncAlert()
        {
            alertFlasher.Stop();

            if (!TimerRinging)
            {
                alertClock.Reset();
                AlertOutline.Clear();
                StopAlertSound();
                return;
            }

            // Also runs when settings change, so the switch works on an alert already ringing
            if (!alertClock.IsRunning || Settings.TimerSound != alertSoundOn)
            {
                if (Settings.TimerSound)
                    StartAlertSound();
                else
                    StopAlertSound();

                alertClock.Restart();
            }

            DrawAlert();

            // Low has its own plain timer, so its flash needs no frame loop
            if (Profile.TimerAlert == GlyphAlertStyle.Flash)
                alertFlasher.Start();
            else
                EnsureRendering();
        }

        void StartAlertSound()
        {
            try
            {
                alertSound ??= new SoundPlayer(new MemoryStream(GlyphSound.BuildWav()));
                alertSound.PlayLooping();
                alertSoundOn = true;
            }
            catch (Exception ex)
            {
                // No sound device, say; the lights still do the job
                Log.Error("Playing the timer sound failed", ex);
            }
        }

        void StopAlertSound()
        {
            alertSound?.Stop();
            alertSoundOn = false;
        }

        void DrawAlert()
        {
            if (TimerRinging)
                AlertOutline.Show(Profile.TimerAlert, alertClock.Elapsed.TotalSeconds);
        }

        bool StepAlert()
        {
            if (!TimerRinging || Profile.TimerAlert == GlyphAlertStyle.Flash)
                return false;

            DrawAlert();
            return true;
        }

        // A click anywhere on the island ends the alert, and does nothing else
        void Island_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!TimerRinging)
                return;

            e.Handled = true;
            countdown.Stop();
        }
    }
}
