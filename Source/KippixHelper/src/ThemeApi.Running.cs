using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK.Events;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// (issue #11) Several games and apps running at once: the list of what's running (Running.cs) is kept from here, with a
    /// one-second tick while anything runs.
    /// </summary>
    public partial class ThemeApi
    {
        private DispatcherTimer runTimer;
        private int runTickBusy;

        /// <summary>From OnGameStarted, before anything else hears about it.</summary>
        public void RunningStarted(OnGameStartedEventArgs args)
        {
            var g = args != null ? args.Game : null;
            if (g == null) return;
            string dir = g.InstallDirectory ?? "";
            try { dir = api.ExpandGameVariables(g, dir); } catch (Exception) { }
            int pid = 0;
            try { pid = args.StartedProcessId; } catch (Exception) { }
            Running.Add(g, pid, dir);
            OnUi(RunningTickOn);
        }

        /// <summary>From the stop path (a stop held back for a launcher's hand-over comes here once that has ended).</summary>
        public void RunningStopped(Game game)
        {
            if (game != null) Running.Remove(game.Id);
        }

        private void RunningTickOn()
        {
            if (runTimer == null)
            {
                runTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
                runTimer.Tick += (s, e) => RunningTick();
            }
            if (!runTimer.IsEnabled) runTimer.Start();
        }

        private void RunningTick()
        {
            if (!Running.Any) { runTimer.Stop(); return; }
            // the window in front is looked at off the UI thread (asking about other programs can be slow), one look at a time
            if (Interlocked.CompareExchange(ref runTickBusy, 1, 0) != 0) return;
            if (!KippixShared.SafeWork.Queue(_ => { try { Running.LearnForeground(); } finally { Interlocked.Exchange(ref runTickBusy, 0); } })) Interlocked.Exchange(ref runTickBusy, 0);
        }
    }
}
