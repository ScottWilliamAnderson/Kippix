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
                if (!ExtraRowsWanted) LeaveExtraRows(false);
                NotifyHomeRows();
                RebuildExtraRowsSoon();   // (the Running now row comes and goes with it)
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

        /// <summary>Playnite's "X is starting / running" screen is on screen.</summary>
        private static bool StatusScreenUp()
        {
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                object vm = w != null ? w.DataContext : null;
                object shown = vm != null ? Prop(vm, "GameStatusVisible") : null;
                return shown is bool && (bool)shown;
            }
            catch (Exception) { return false; }
        }

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

        // =====================================================================
        // The "Running now" row (ThemeApi.HomeRows.cs, key "running"): A switches to the app, X closes it
        // =====================================================================

        /// <summary>The highlight is in the Running now row.</summary>
        private bool InRunningRow { get { return InExtraRow && ExtraRowsList[extraRowAt - 1].Key == "running"; } }

        /// <summary>Something started or stopped: the row is filled again (it goes when nothing runs).</summary>
        private void RunningListChanged()
        {
            if (!IsFullscreen || ui == null) return;
            bool rowOn = settings.RunKeepBrowsing || (settings.ExtraRows && ExtraKeys().Contains("running"));
            if (rowOn && ExtraRowsWanted) ui.BeginInvoke(new Action(RebuildExtraRows), DispatcherPriority.Background);
            RunningBadgesChanged();
        }

        /// <summary>The green dot on the extra rows' tiles of what's running (the game cards' own "Running" pill follows Playnite's IsRunning in the theme).</summary>
        private void RunningBadgesChanged()
        {
            foreach (var row in ExtraRowsList)
                foreach (var t in row.Tiles) t.ShowRunning = settings.BadgeRunning && Running.IsRunning(t.GameId);
        }

        /// <summary>
        /// Where Kippix itself starts a game (A with "Skip the game page" on): it's running already, so its window comes to the
        /// front instead (Playnite would do nothing at all: it never starts a game twice). False: not running, start it.
        /// </summary>
        private bool TrySwitchInsteadOfPlay(Guid id)
        {
            if (id == Guid.Empty || !Running.IsRunning(id)) return false;
            logger.Info("Kippix: running: play on a game that's already running: switching to it");
            return RunningSwitchTo(id);
        }

        /// <summary>X on the highlighted tile, when it's a Running now tile (true: the press was ours, also the let-go).</summary>
        private bool RunningRowX(bool pressed)
        {
            if (extraRowAt == 0) return false;
            var f = Keyboard.FocusedElement as FrameworkElement;
            var tile = f != null ? f.DataContext as RowTile : null;
            if (tile == null || !tile.RunRow) return false;
            if (pressed) RunningClose(tile.GameId);
            return true;
        }

        private static string RunHeader { get { return Loc("LOC_KX_RowRunning", "Running now").ToUpper(); } }

        /// <summary>A on a Running now tile: that app's window comes to the front. False when it isn't running.</summary>
        private bool RunningSwitchTo(Guid id)
        {
            var entry = Running.Get(id);
            if (entry == null) return false;
            PlaySound("select");
            KippixShared.SafeWork.Queue(_ =>
            {
                IntPtr h = Running.FindWindow(entry);
                if (h == IntPtr.Zero)
                {
                    logger.Info("Kippix: running: switch to " + entry.Name + ": no window found");
                    OnUi(() => ShowCard(RunHeader, entry.Name, string.Format(Loc("LOC_KX_RunNoWindow", "Couldn't find {0}'s window"), entry.Name), "", null));
                    return;
                }
                logger.Info("Kippix: running: switch to " + entry.Name + ": window 0x" + h.ToInt64().ToString("X"));
                KippixOverlay.Native.ForceForeground(h);
            });
            return true;
        }

        private readonly HashSet<Guid> runClosing = new HashSet<Guid>();

        /// <summary>
        /// X on a Running now tile: its windows are asked to close, the way their own close button would (it can save, or ask
        /// first); nothing is ever forced here. Still running 10 seconds later: a question offers to force it closed.
        /// </summary>
        private void RunningClose(Guid id)
        {
            var entry = Running.Get(id);
            if (entry == null || runClosing.Contains(id)) return;   // (asked already: wait for the 10 seconds)
            runClosing.Add(id);
            PlaySound("select");
            ShowCard(RunHeader, string.Format(Loc("LOC_KX_RunClosing", "Closing {0}"), entry.Name), "", "", null);
            KippixShared.SafeWork.Queue(_ =>
            {
                var wins = Running.WindowsOf(entry, false);
                if (wins.Count == 0) { IntPtr h = Running.FindWindow(entry); if (h != IntPtr.Zero) wins.Add(h); }
                int asked = 0;
                foreach (IntPtr h in wins) if (Running.AskToClose(h)) asked++;
                logger.Info("Kippix: running: close " + entry.Name + ": " + asked + " of " + wins.Count + " window(s) asked to close");
                if (wins.Count == 0) OnUi(() => ShowCard(RunHeader, entry.Name, string.Format(Loc("LOC_KX_RunNoWindow", "Couldn't find {0}'s window"), entry.Name), "", null));
            });
            var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                runClosing.Remove(id);
                if (!ReferenceEquals(Running.Get(id), entry)) return;   // closed (or closed and started again)
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                if (w == null || !w.IsActive || confirmOpen || !IsFullscreen)
                {
                    logger.Info("Kippix: running: " + entry.Name + " is still open after 10 s (not asked about forcing it: Playnite isn't in front, or a question is up)");
                    return;
                }
                logger.Info("Kippix: running: " + entry.Name + " is still open after 10 s: asking whether to force it closed");
                Confirm(string.Format(Loc("LOC_KX_RunStillOpen", "{0} is still open"), entry.Name), Loc("LOC_KX_RunForceText", "Forcing it closed loses anything it hasn't saved."), Loc("LOC_KX_RunForce", "Force close"), () => RunningForceClose(entry));
            };
            timer.Start();
        }

        /// <summary>Force close, after the question: its programs and everything they started are ended (taskkill /T /F).</summary>
        private void RunningForceClose(RunningEntry entry)
        {
            if (!ReferenceEquals(Running.Get(entry.Id), entry)) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                var pids = Running.ProcessesOf(entry);
                if (pids.Count == 0)
                {
                    logger.Info("Kippix: running: force close " + entry.Name + ": none of its programs found");
                    OnUi(() => ShowCard(RunHeader, entry.Name, string.Format(Loc("LOC_KX_RunNoWindow", "Couldn't find {0}'s window"), entry.Name), "", null));
                    return;
                }
                logger.Info("Kippix: running: force close " + entry.Name + ": taskkill /T /F on process " + string.Join(", ", pids.Select(p => p.ToString()).ToArray()));
                KippixOverlay.Native.RunHidden("taskkill", string.Join(" ", pids.Select(p => "/PID " + p).ToArray()) + " /T /F");
            });
        }
    }
}
