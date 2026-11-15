using System;
using System.Windows;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.55) While a game runs and Playnite isn't in front, the theme draws nothing: the whole screen is hidden (kept, not
    /// taken apart), so nothing of it is drawn behind the game. The overlay and its windows are drawn on the same thread as
    /// Playnite, so whatever moved on the home screen or the game page (backgrounds, pictures, the clock...) made them and the
    /// game stutter. It all comes back the moment Playnite comes to the front again (the game closing, Alt+Tab, the overlay's
    /// Open Playnite), or when the game stops.
    /// </summary>
    public partial class ThemeApi
    {
        private bool playSleepHooked, playSleeping;
        private DispatcherTimer playSleepTimer;

        private void HookPlaySleep()
        {
            if (playSleepHooked) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;
            playSleepHooked = true;
            w.Activated += (s, e) => WakeFromPlay("Playnite in front");
            w.Deactivated += (s, e) => { if (AnyRunning) PlaySleepSoon(); };
        }

        /// <summary>A moment after the game has taken over (the launch screen has gone by then).</summary>
        private void PlaySleepSoon()
        {
            if (playSleepTimer == null)
            {
                playSleepTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(4) };
                playSleepTimer.Tick += (s, e) => { playSleepTimer.Stop(); SleepForPlay(); };
            }
            playSleepTimer.Stop();
            playSleepTimer.Start();
        }

        private void SleepForPlay()
        {
            try
            {
                if (playSleeping || !AnyRunning || !IsFullscreen) return;
                if (settings.RunKeepBrowsing && playniteActive) return;   // (issue #11: one of Playnite's own menus is in front, not a game)
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                var root = w != null ? w.Content as UIElement : null;
                if (root == null || w.IsActive) return;
                root.Visibility = Visibility.Hidden;
                playSleeping = true;
                Dbg("play: the theme isn't drawn while the game runs");
            }
            catch (Exception) { }
        }

        private void WakeFromPlay(string why)
        {
            if (playSleepTimer != null) playSleepTimer.Stop();
            if (!playSleeping) return;
            playSleeping = false;
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                var root = w != null ? w.Content as UIElement : null;
                if (root != null) root.Visibility = Visibility.Visible;
                Dbg("play: the theme is drawn again (" + why + ")");
            }
            catch (Exception) { }
        }

        /// <summary>From GameRunningChanged.</summary>
        private void PlaySleepGameRunning(bool running)
        {
            if (running) { HookPlaySleep(); PlaySleepSoon(); }
            else WakeFromPlay("the game stopped");
        }
    }
}
