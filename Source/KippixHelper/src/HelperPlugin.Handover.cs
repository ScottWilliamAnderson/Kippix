using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK.Events;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.54) A game whose launcher hands over to the game (The Witcher 3's REDlauncher, and others like it): Playnite watches
    /// the program it started, sees it close a couple of seconds in, and says the game has stopped while the launcher and then
    /// the game are still to come. When a game "stops" within its first minute, Kippix waits before treating it as stopped:
    /// while Steam still says the game is running (its RunningAppID), or a program from the game's folder runs, it's carried
    /// on: the overlay keeps the game (screenshots go to it, not to "Other"), no "You just played" card, and Playnite isn't left
    /// over the launcher. Once they've all closed (or nothing came within 20 seconds), it ends as usual: the play time Playnite
    /// missed is added to the game, then the session card, the save backup and the rest.
    /// </summary>
    public partial class HelperPlugin
    {
        // (issue #11) one per game: several can run at once, and one being held back mustn't end another's (or be ended by it)
        private class Handover
        {
            public Game Game;
            public ulong Elapsed;
            public DateTime StoppedAt, LastAlive, StartedAt;
            public bool Carried;
            public int Fronts;
            public string Dir, AppId;
        }
        private readonly Dictionary<Guid, Handover> hoSlots = new Dictionary<Guid, Handover>();
        private DispatcherTimer hoTimer;

        /// <summary>The held-back stop of this game ends now (it's being started again). Other games' carry on.</summary>
        private void HandoverFinishFor(Guid id)
        {
            Handover h;
            if (hoSlots.TryGetValue(id, out h)) HandoverFinish(h);
        }

        /// <summary>Playnite is closing: every held-back stop ends now.</summary>
        private void HandoverFinishAll()
        {
            foreach (var h in hoSlots.Values.ToList()) HandoverFinish(h);
        }

        private static readonly Guid SteamLibraryId = new Guid("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        /// <summary>True when the stop is held back (a launcher may be handing over).</summary>
        private bool HandoverStart(OnGameStoppedEventArgs a)
        {
            var g = a != null ? a.Game : null;
            if (g == null || a.ElapsedSeconds > 60) return false;
            string dir = null;
            try { if (!string.IsNullOrWhiteSpace(g.InstallDirectory) && System.IO.Directory.Exists(g.InstallDirectory)) dir = System.IO.Path.GetFullPath(g.InstallDirectory).TrimEnd('\\') + "\\"; } catch (Exception) { }
            string appId = g.PluginId == SteamLibraryId && !string.IsNullOrEmpty(g.GameId) ? g.GameId : null;
            if (dir == null && appId == null) return false;
            if (dir != null && dir.Length <= 4) dir = null;   // (a whole drive: no)
            HandoverFinishFor(g.Id);   // (this game held back already: that one ends first)
            var h = new Handover { Game = g, Elapsed = a.ElapsedSeconds, StoppedAt = DateTime.Now, LastAlive = DateTime.Now, StartedAt = DateTime.Now.AddSeconds(-(double)a.ElapsedSeconds - 5), Dir = dir, AppId = appId };
            hoSlots[g.Id] = h;
            if (hoTimer == null)
            {
                hoTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
                hoTimer.Tick += (s, e) =>
                {
                    if (hoSlots.Count == 0) { hoTimer.Stop(); return; }
                    foreach (var slot in hoSlots.Values.ToList())
                    {
                        try { HandoverTick(slot); } catch (Exception ex) { logger.Warn("Kippix: launcher hand-over: " + ex.Message); HandoverFinish(slot); }
                    }
                };
            }
            hoTimer.Start();
            logger.Info("Kippix: " + g.Name + " stopped after " + a.ElapsedSeconds + " s: watching whether a launcher hands over to the game (Steam app " + (appId ?? "-") + ", folder " + (dir ?? "-") + ")");
            return true;
        }

        private bool HandoverAlive(Handover h)
        {
            if (h.AppId != null)
            {
                try
                {
                    object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RunningAppID", 0);
                    if (v is int && ((int)v).ToString() == h.AppId) return true;
                    object r = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\" + h.AppId, "Running", 0);
                    if (r is int && (int)r != 0) return true;
                }
                catch (Exception) { }
            }
            if (h.Dir != null)
            {
                Process[] all = null;
                try
                {
                    all = Process.GetProcesses();
                    foreach (var p in all)
                    {
                        string path = null;
                        try { path = Native.GetProcessPath((uint)p.Id); } catch (Exception) { }
                        if (path != null && path.StartsWith(h.Dir, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch (Exception) { }
                finally { if (all != null) foreach (var p in all) try { p.Dispose(); } catch (Exception) { } }
            }
            return false;
        }

        private void HandoverTick(Handover h)
        {
            var now = DateTime.Now;
            bool alive = HandoverAlive(h);
            if (alive) h.LastAlive = now;
            if (!h.Carried)
            {
                if (alive)
                {
                    h.Carried = true;
                    logger.Info("Kippix: " + h.Game.Name + " carries on after its launcher (still running): kept as the game being played");
                }
                else if ((now - h.StoppedAt).TotalSeconds > 20) { HandoverFinish(h); return; }
            }
            if (h.Carried)
            {
                // Playnite came back over the launcher when it thought the game had closed: the launcher goes back in front
                if (h.Fronts < 4 && (now - h.StoppedAt).TotalSeconds < 15 && PlayniteInFront()) { h.Fronts++; LauncherToFront(h); }
                if (!alive && (now - h.LastAlive).TotalSeconds >= 6) HandoverFinish(h);
            }
        }

        /// <summary>Ends the held-back stop: the play time Playnite missed is added, then everything a stopped game does.</summary>
        private void HandoverFinish(Handover h)
        {
            Handover held;
            if (h == null || !hoSlots.TryGetValue(h.Game.Id, out held) || !ReferenceEquals(held, h)) return;   // (ended already)
            hoSlots.Remove(h.Game.Id);
            if (hoSlots.Count == 0 && hoTimer != null) hoTimer.Stop();
            var g = h.Game;
            ulong total = h.Elapsed;
            if (h.Carried)
            {
                double extra = Math.Max(0, (h.LastAlive - h.StoppedAt).TotalSeconds);
                total += (ulong)extra;
                try
                {
                    var fresh = PlayniteApi.Database.Games.Get(g.Id);
                    if (fresh != null && extra >= 1)
                    {
                        fresh.Playtime += (ulong)extra;
                        fresh.LastActivity = DateTime.Now;
                        PlayniteApi.Database.Games.Update(fresh);
                        g = fresh;
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: adding the play time after the launcher: " + ex.Message); }
                logger.Info("Kippix: " + g.Name + " closed after its launcher: " + (ulong)extra + " s added to its play time (Playnite counted " + h.Elapsed + " s)");
            }
            FinishStopped(g, total);
        }

        // ---------------- the launcher back in front ----------------

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

        private static bool PlayniteInFront()
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                return pid == (uint)Process.GetCurrentProcess().Id;
            }
            catch (Exception) { return false; }
        }

        private static readonly string[] NotLaunchers = { "steam", "steamwebhelper", "explorer", "discord", "nvidia", "nvcontainer", "playnite", "textinputhost", "searchhost", "shellexperiencehost", "applicationframehost", "gamebar", "msedge", "chrome", "firefox" };

        /// <summary>The newest program's window (started since the game was) goes in front of Playnite: the launcher, or the game.</summary>
        private void LauncherToFront(Handover ho)
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            IntPtr best = IntPtr.Zero;
            var started = new Dictionary<uint, bool>();
            EnumWindows((h, l) =>
            {
                if (best != IntPtr.Zero) return false;
                try
                {
                    if (!IsWindowVisible(h) || IsIconic(h) || GetWindow(h, 4) != IntPtr.Zero || GetWindowTextLength(h) == 0) return true;   // (visible, not owned, with a title)
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid == 0 || pid == me) return true;
                    bool ok;
                    if (!started.TryGetValue(pid, out ok))
                    {
                        ok = false;
                        try
                        {
                            using (var p = Process.GetProcessById((int)pid))
                            {
                                string n = p.ProcessName.ToLowerInvariant();
                                string path = Native.GetProcessPath(pid) ?? "";
                                bool inDir = ho.Dir != null && path.StartsWith(ho.Dir, StringComparison.OrdinalIgnoreCase);
                                ok = inDir || (!NotLaunchers.Any(x => n.Contains(x)) && p.StartTime >= ho.StartedAt);
                            }
                        }
                        catch (Exception) { }
                        started[pid] = ok;
                    }
                    if (ok) best = h;   // (top of the z-order first)
                }
                catch (Exception) { }
                return true;
            }, IntPtr.Zero);
            if (best == IntPtr.Zero) return;
            logger.Info("Kippix: Playnite came back over " + ho.Game.Name + "'s launcher: the launcher put back in front");
            try { KippixOverlay.Native.ForceForeground(best); } catch (Exception) { }
        }
    }
}
