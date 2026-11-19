using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK;

namespace KippixLinks
{
    /// <summary>
    /// Opens a saved link: always through the loopback hop (Handoff), so the address itself is never on a command line.
    /// Kippix's own way first (full screen in the browser, with the controller driving it); our own launcher without Kippix.
    /// </summary>
    internal class Opener
    {
        private static readonly string[] FirefoxLike = { "firefox", "librewolf", "waterfox", "floorp", "zen" };
        private static readonly string[] ChromiumLike = { "msedge", "chrome", "brave", "vivaldi", "opera", "chromium", "thorium" };

        private readonly IPlayniteAPI api;
        private readonly KippixSeam seam;
        private readonly Handoff handoff;
        private readonly LinkStore store;
        private readonly PanelSettings settings;

        private DispatcherTimer titleTimer;
        private LinkItem titleItem;
        private int titleSample;
        private HashSet<IntPtr> before;

        public Opener(IPlayniteAPI api, KippixSeam seam, Handoff handoff, LinkStore store, PanelSettings settings)
        {
            this.api = api;
            this.seam = seam;
            this.handoff = handoff;
            this.store = store;
            this.settings = settings;
        }

        /// <summary>UI thread.</summary>
        public void Open(LinkItem item)
        {
            if (item == null || !store.Contains(item)) return;
            string id = item.Id;
            try
            {
                bool fullscreen = false;
                try { fullscreen = api.ApplicationInfo.Mode == ApplicationMode.Fullscreen; } catch (Exception) { }
                // the windows there before it opened: a title is only ever taken from a window that came after
                before = TopWindows();
                string hop = handoff.Begin(item.Url, HoldForKippix(before));
                // Kippix's way only in fullscreen mode (its full-screen browser and controller mode belong to its theme there)
                if (fullscreen && seam != null && seam.OpenMedia(hop)) { store.MarkOpened(item); SafeLog.Info("opened through Kippix", id); }
                else
                {
                    bool ownWindow = OwnLaunch(hop);
                    store.MarkOpened(item);
                    if (ownWindow) SafeLog.Info("opened in the default browser's own window", id);
                    else SafeLog.Info("opened with the system's default handler", id);
                }
                hop = null;
                StopTitleWatch();
                if (settings.Titles && item.Name == null && string.IsNullOrEmpty(item.Title)) StartTitleWatch(item);
            }
            catch (Exception ex)
            {
                SafeLog.Warn("open failed", id, ex);
                try { handoff.Stop(); } catch (Exception) { }
                try { api.Dialogs.ShowErrorMessage("That link couldn't be opened.", "Links"); } catch (Exception) { }
            }
        }

        private const int FrontForMs = 2200;      // Kippix looks once a second: in front this long, it has looked (twice)
        private const int NewWindowWaitMs = 2500; // no new browser window by then: the page is a tab in one that was there

        /// <summary>
        /// While Kippix follows running games and apps, it looks at the window in front once a second and writes a window's
        /// title to Playnite's extensions.log the first time it sees it there (Running.LearnForeground, Kippix 1.0.5 and
        /// 1.0.6); after that it knows the window and says nothing more about it while it stays in front. So the browser's
        /// request is held back until Kippix has looked at the browser's window: what Kippix writes is the empty window's
        /// title, not the page's. "Has looked" is read from Kippix itself; when that can't be told, in front for
        /// FrontForMs counts. Null: nothing to wait for.
        /// (What this can't cover: Kippix writes a title again when another window of a browser it follows comes to the
        /// front later. Only Kippix can stop that: Running.Remember.)
        /// </summary>
        private Func<bool> HoldForKippix(HashSet<IntPtr> windowsBefore)
        {
            try { if (seam == null || !seam.WatchingWindows()) return null; }
            catch (Exception) { }
            if (windowsBefore == null) return Handoff.After(FrontForMs + 1000);
            var gate = new object();              // (asked from several pool threads: a browser may ask twice)
            var clock = Stopwatch.StartNew();
            var browsers = new Dictionary<IntPtr, bool>();
            IntPtr front = IntPtr.Zero;
            long frontSince = 0;
            return () =>
            {
                lock (gate)
                {
                    if (!seam.WatchingWindows()) return true;   // nothing is followed any more
                    IntPtr h = NativeMethods.GetForegroundWindow();
                    bool candidate = h != IntPtr.Zero && AppWindow(h) && IsBrowser(h, browsers);
                    if (candidate && windowsBefore.Contains(h))
                        // a window that was there before: only when no new one came (a tab in it, not a window of its own)
                        candidate = clock.ElapsedMilliseconds >= NewWindowWaitMs && !AnyNewBrowserWindow(windowsBefore, browsers);
                    if (!candidate) { front = IntPtr.Zero; return false; }
                    if (h != front) { front = h; frontSince = clock.ElapsedMilliseconds; }
                    bool? looked = seam.HasLookedAt(h);
                    if (looked.HasValue) return looked.Value;
                    return clock.ElapsedMilliseconds - frontSince >= FrontForMs;
                }
            };
        }

        /// <summary>A window Kippix would look at (its IsAppWindow): visible, not owned, with a title, not a tool window.</summary>
        private static bool AppWindow(IntPtr h)
        {
            if (h == IntPtr.Zero || !NativeMethods.IsWindowVisible(h)) return false;
            if (NativeMethods.GetWindow(h, 4) != IntPtr.Zero) return false;
            if (NativeMethods.GetWindowTextLength(h) == 0) return false;
            return (NativeMethods.GetWindowLong(h, -20) & 0x80) == 0;
        }

        private static bool IsBrowser(IntPtr h, Dictionary<IntPtr, bool> known)
        {
            bool b;
            if (known.TryGetValue(h, out b)) return b;
            b = BrowserOf(h) != null;
            known[h] = b;
            return b;
        }

        private static bool AnyNewBrowserWindow(HashSet<IntPtr> windowsBefore, Dictionary<IntPtr, bool> known)
        {
            bool any = false;
            try
            {
                NativeMethods.EnumWindows((h, l) =>
                {
                    if (!windowsBefore.Contains(h) && AppWindow(h) && IsBrowser(h, known)) { any = true; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            return any;
        }

        /// <summary>The window's program when it is one of the known browsers (lower case), else null.</summary>
        private static string BrowserOf(IntPtr h)
        {
            uint pid;
            if (NativeMethods.GetWindowThreadProcessId(h, out pid) == 0 || pid == 0) return null;
            string name;
            try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName.ToLowerInvariant(); }
            catch (Exception) { return null; }
            return Array.IndexOf(FirefoxLike, name) >= 0 || Array.IndexOf(ChromiumLike, name) >= 0 ? name : null;
        }

        /// <summary>The default browser started with the hop address (true), or the shell's default handler (false).</summary>
        private static bool OwnLaunch(string hop)
        {
            string exe = DefaultBrowser();
            if (exe != null)
            {
                string n = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                string args = null;
                if (Array.IndexOf(FirefoxLike, n) >= 0) args = "--new-window \"" + hop + "\"";
                else if (Array.IndexOf(ChromiumLike, n) >= 0) args = "--app=\"" + hop + "\" --start-fullscreen";
                if (args != null)
                {
                    using (Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })) { }
                    return true;
                }
            }
            using (Process.Start(new ProcessStartInfo(hop) { UseShellExecute = true })) { }
            return false;
        }

        /// <summary>The https handler's program (HKCU UserChoice → its open command), or null.</summary>
        private static string DefaultBrowser()
        {
            try
            {
                string prog = null;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                    if (k != null) prog = k.GetValue("ProgId") as string;
                if (string.IsNullOrEmpty(prog)) return null;
                using (var k = Registry.ClassesRoot.OpenSubKey(prog + @"\shell\open\command"))
                {
                    string cmd = k != null ? k.GetValue(null) as string : null;
                    if (string.IsNullOrEmpty(cmd)) return null;
                    cmd = cmd.Trim();
                    string exe = cmd.StartsWith("\"") ? cmd.Substring(1, Math.Max(0, cmd.IndexOf('"', 1) - 1)) : cmd.Split(' ')[0];
                    return File.Exists(exe) ? exe : null;
                }
            }
            catch (Exception ex) { SafeLog.Warn("open: default browser lookup", ex); return null; }
        }

        // ---------------- the page title, from the browser's window ----------------

        private static readonly int[] SampleAtMs = { 6000, 12000, 20000 };
        private int titleWaits;   // ticks spent waiting for the browser to be sent on to the page

        private void StartTitleWatch(LinkItem item)
        {
            titleItem = item;
            titleSample = 0;
            titleWaits = 0;
            titleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(SampleAtMs[0]) };
            titleTimer.Tick += OnTitleTick;
            titleTimer.Start();
        }

        private void StopTitleWatch()
        {
            if (titleTimer != null) { titleTimer.Stop(); titleTimer.Tick -= OnTitleTick; titleTimer = null; }
            titleItem = null;
        }

        private void OnTitleTick(object sender, EventArgs e)
        {
            try
            {
                var item = titleItem;
                if (item == null || !store.Contains(item) || item.Name != null || !string.IsNullOrEmpty(item.Title)) { StopTitleWatch(); return; }
                // not before the browser was sent on to the page (a held or slow start): the window still says nothing of it
                if (!handoff.Served)
                {
                    if (++titleWaits > 12) { StopTitleWatch(); return; }
                    titleTimer.Interval = TimeSpan.FromMilliseconds(4000);
                    return;
                }
                string t = ForegroundBrowserTitle(before);
                if (t != null) { store.SetTitle(item, t); StopTitleWatch(); return; }   // (never logged)
                titleSample++;
                if (titleSample >= SampleAtMs.Length) { StopTitleWatch(); return; }
                titleTimer.Interval = TimeSpan.FromMilliseconds(SampleAtMs[titleSample] - SampleAtMs[titleSample - 1]);
            }
            catch (Exception ex) { StopTitleWatch(); SafeLog.Warn("open: title sample", ex); }
        }

        /// <summary>
        /// The window in front, when it is one of the known browsers and it wasn't there before the link was opened (so it
        /// is the one just opened, not some other page): its title, cleaned. Null otherwise, and always null for a private
        /// window (private browsing is never kept).
        /// </summary>
        private static string ForegroundBrowserTitle(HashSet<IntPtr> before)
        {
            IntPtr h = NativeMethods.GetForegroundWindow();
            if (h == IntPtr.Zero || before == null || before.Contains(h)) return null;
            if (BrowserOf(h) == null) return null;
            var sb = new StringBuilder(512);
            if (NativeMethods.GetWindowText(h, sb, sb.Capacity) <= 0) return null;
            string raw = sb.ToString();
            sb.Length = 0;
            if (LooksPrivate(raw)) return null;
            return LinkUrl.CleanTitle(raw);
        }

        private static readonly string[] PrivateMarks = { "Private Browsing", "InPrivate", "Incognito", "Private window", "Private Window" };

        private static bool LooksPrivate(string title)
        {
            foreach (string m in PrivateMarks)
                if (title.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Every top-level window right now (handles only; nothing about them is read).</summary>
        private static HashSet<IntPtr> TopWindows()
        {
            var set = new HashSet<IntPtr>();
            try { NativeMethods.EnumWindows((h, l) => { set.Add(h); return true; }, IntPtr.Zero); }
            catch (Exception ex) { SafeLog.Warn("open: window list", ex); return null; }   // (null: no title is taken)
            return set;
        }
    }

    internal static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    }
}
