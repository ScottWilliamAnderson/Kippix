using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Playnite.SDK.Events;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// (issue #11) Several games and apps running at once: the list of what's running (Running.cs) is kept from here, with a
    /// one-second tick while anything runs. With "Keep browsing while things run" on (off unless you turn it on), Playnite's
    /// "X is running" screen closes itself once the game has started, and Kippix carries on as usual whenever Playnite is the
    /// window in front (gameRunningNow, ThemeApi.Sound.cs).
    /// </summary>
    public partial class ThemeApi
    {
        private DispatcherTimer runTimer, runActiveCheck;
        private int runTickBusy;
        private bool runWindowHooked;
        private DateTime runLastStart = DateTime.MinValue;   // the last "starting" or "started"
        private Guid runStartingId;
        private UIElement runFocusBefore;                    // where the highlight was when the game was started

        /// <summary>Home › Keep browsing while things run.</summary>
        public bool RunKeepBrowsing
        {
            get { return settings.RunKeepBrowsing; }
            set
            {
                if (settings.RunKeepBrowsing == value) return;
                settings.RunKeepBrowsing = value;
                SaveSettings();
                Notify("RunKeepBrowsing");
                logger.Info("Kippix: running: keep browsing " + (value ? "on" : "off"));
                if (AnyRunning) { HookRunningWindow(); playniteActive = PlayniteActiveNow(); RunningStateChanged(); }
            }
        }

        /// <summary>From GameStarting (ThemeApi.Status.cs), on the UI thread: where the highlight is, to put it back there.</summary>
        private void RunningStarting(Game g)
        {
            runLastStart = DateTime.Now;
            runStartingId = g != null ? g.Id : Guid.Empty;
            if (!settings.RunKeepBrowsing) return;
            try
            {
                var f = Keyboard.FocusedElement as UIElement;
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var status = win != null ? UiTools.FindByName(win, "PART_ElemGameStatus") : null;
                if (f != null && status != null && status.IsAncestorOf(f)) return;   // (already on the status screen: keep what was noted before)
                runFocusBefore = f;
            }
            catch (Exception) { }
        }

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
            OnUi(() => { runLastStart = DateTime.Now; HookRunningWindow(); SetPlayniteActive(PlayniteActiveNow()); RunningTickOn(); });
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
                runTimer.Tick += (s, e) => { try { RunningTick(); } catch (Exception ex) { logger.Warn("Kippix: running: tick: " + ex.Message); } };
            }
            if (!runTimer.IsEnabled) runTimer.Start();
        }

        private void RunningTick()
        {
            if (!Running.Any) { runTimer.Stop(); return; }
            // the window in front is looked at off the UI thread (asking about other programs can be slow), one look at a time
            if (Interlocked.CompareExchange(ref runTickBusy, 1, 0) == 0)
            {
                if (!KippixShared.SafeWork.Queue(_ => { try { Running.LearnForeground(); } finally { Interlocked.Exchange(ref runTickBusy, 0); } })) Interlocked.Exchange(ref runTickBusy, 0);
            }
            if (!settings.RunKeepBrowsing) return;
            SetPlayniteActive(PlayniteActiveNow());   // (in case an Activated / Deactivated was missed)
            DismissStatusScreen();
        }

        // =====================================================================
        // Is Playnite the window in front?
        // =====================================================================

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr RunForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint RunWindowProcess(IntPtr h, out uint pid);
        private static readonly uint runOwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private void HookRunningWindow()
        {
            if (runWindowHooked) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;
            runWindowHooked = true;
            w.Activated += (s, e) => SetPlayniteActive(true);
            // one of Playnite's own menus taking over isn't "Playnite left": looked at a moment later, when the new window is in front
            w.Deactivated += (s, e) =>
            {
                if (runActiveCheck == null)
                {
                    runActiveCheck = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
                    runActiveCheck.Tick += (s2, e2) => { runActiveCheck.Stop(); SetPlayniteActive(PlayniteActiveNow()); };
                }
                runActiveCheck.Stop();
                runActiveCheck.Start();
            };
        }

        /// <summary>Playnite's main window is the active one, or one of its own menus / dialogs is in front (not the in-game overlay: that's over a game).</summary>
        private bool PlayniteActiveNow()
        {
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                if (w == null || !w.IsVisible || w.WindowState == WindowState.Minimized) return false;
                if (w.IsActive) return true;
                IntPtr fg = RunForegroundWindow();
                uint pid = 0;
                if (fg != IntPtr.Zero) RunWindowProcess(fg, out pid);
                if (fg == IntPtr.Zero || pid != runOwnPid) return false;
                var src = HwndSource.FromHwnd(fg);
                var root = src != null ? src.RootVisual : null;
                string ns = root != null ? root.GetType().Namespace ?? "" : "";
                return !ns.StartsWith("KippixOverlay", StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        private void SetPlayniteActive(bool active)
        {
            if (playniteActive == active) return;
            playniteActive = active;
            if (settings.RunKeepBrowsing && AnyRunning) RunningStateChanged();
        }

        /// <summary>Keep browsing: Playnite came to the front or left it with something running (or the switch changed): what follows "a game is running" follows along.</summary>
        private void RunningStateChanged()
        {
            bool busy = gameRunningNow;
            Dbg("running: Playnite " + (playniteActive ? "in front" : "not in front") + " with " + Running.Count + " running: " + (busy ? "quiet, as while playing" : "as usual"));
            try { UpdateMusicState(); } catch (Exception) { }
            try { if (busy) { StopTrailer(); StopAmbient(); } } catch (Exception) { }
            try { ApplyBackgroundMotion(); } catch (Exception) { }
        }

        // =====================================================================
        // Playnite's "X is running" screen closes itself (keep browsing)
        // =====================================================================

        /// <summary>
        /// Once a second while anything runs: Playnite's status screen is up, nothing is still starting, the last start was 3 s
        /// or more ago and Playnite's window is the active one: the screen closes (as its own Close button does) and the
        /// highlight goes back where it was.
        /// </summary>
        private void DismissStatusScreen()
        {
            if (!IsFullscreen || (DateTime.Now - runLastStart).TotalSeconds < 3) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            object vm = w != null ? w.DataContext : null;
            if (vm == null || !w.IsActive || w.WindowState == WindowState.Minimized) return;
            object shown = Prop(vm, "GameStatusVisible");
            if (!(shown is bool) || !(bool)shown) return;
            try
            {
                var starting = runStartingId != Guid.Empty ? api.Database.Games.Get(runStartingId) : null;
                if (starting != null && starting.IsLaunching) return;
                if (launchFront != null && launchFront.IsEnabled) return;   // (still held in front for a game that hasn't come up)
            }
            catch (Exception) { }
            object details = Prop(vm, "GameDetailsVisible");
            if (details is bool && (bool)details)
            {
                // started from the game page: back on the page, as Playnite itself does when a game stops (its Close command goes to the list)
                var vis = vm.GetType().GetProperty("GameStatusVisible");
                var foc = vm.GetType().GetProperty("GameDetailsFocused");
                if (vis == null || foc == null || !vis.CanWrite || !foc.CanWrite) return;
                vis.SetValue(vm, false, null);
                foc.SetValue(vm, true, null);
                logger.Info("Kippix: running: Playnite's status screen closed (back on the game page)");
                return;
            }
            RunMainCommand("CloseGameStatusCommand");
            logger.Info("Kippix: running: Playnite's status screen closed (back on the home screen)");
            HighlightAfterStatus();
        }

        /// <summary>The status screen closed: Kippix's own highlight goes back where it was (Playnite only knows its own row).</summary>
        private void HighlightAfterStatus()
        {
            var before = runFocusBefore;
            runFocusBefore = null;
            var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(220) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    if (detailsOpen || openPanel != null || confirmOpen) return;
                    if (extraRowAt > 0)
                    {
                        var t = extraFocused;
                        var row = t != null ? ExtraRowsList.FirstOrDefault(r => r.Tiles.Contains(t)) : null;
                        if (row != null) { LayoutExtraRows(false); FocusRowTile(row, row.Tiles.IndexOf(t)); }
                        else if (ExtraRowsList.Count > 0) GoToExtraRow(Math.Min(extraRowAt, ExtraRowsList.Count), 0);
                        else LeaveExtraRows(true);
                        return;
                    }
                    if (before != null && before.IsVisible && before.Focusable && PresentationSource.FromVisual(before) != null) { Keyboard.Focus(before); return; }
                    if (CarouselBackToTile()) return;
                    if (IsBoardMode) { if (!BoardHasFocus()) FocusBoard(); return; }
                    FocusSelectedCard();
                }
                catch (Exception ex) { logger.Warn("Kippix: running: highlight after the status screen: " + ex.Message); }
            };
            timer.Start();
        }
    }
}
