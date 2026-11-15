using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>One game (or app) Playnite says is running.</summary>
    internal class RunningEntry
    {
        public Guid Id;
        public string Name = "";
        public DateTime Started;
        /// <summary>The program Playnite started (0 when a library add-on started it: Steam, Epic...).</summary>
        public uint StartedPid;
        internal long StartedPidCreated;      // (its creation time: a process id is used again once its program has closed)
        /// <summary>The game's folder, variables expanded, with a "\" at the end ("" when there isn't one worth matching on).</summary>
        public string InstallDir = "";
        /// <summary>The window last seen as this game's (zero until one is found).</summary>
        public IntPtr Window = IntPtr.Zero;
        internal uint WindowPid;
        /// <summary>A program taken as this game's because its window came up right after the start (nothing else to go on).</summary>
        internal uint AdoptedPid;
        internal string LastFound = "";       // (what the window finder last logged: said once, not every second)
    }

    /// <summary>
    /// (issue #11) Everything running now, one entry per game: Playnite can run several at once, Kippix used to know about one.
    /// Fed from OnGameStarted and from the stop path (HelperPlugin.cs); thread-safe. Also finds a running game's window: the
    /// one remembered if it's still there, else a visible window of a program that the started one started, or of a program
    /// from the game's folder; and learns from whichever window is in front (LearnForeground, once a second while anything runs).
    /// </summary>
    internal static class Running
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly object gate = new object();
        private static readonly List<RunningEntry> entries = new List<RunningEntry>();   // oldest first
        private static readonly uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        /// <summary>A game was added or removed (raised on the thread that did it).</summary>
        public static event Action Changed;

        public static int Count { get { lock (gate) return entries.Count; } }
        public static bool Any { get { return Count > 0; } }

        /// <summary>A copy of the list, oldest first.</summary>
        public static List<RunningEntry> Snapshot() { lock (gate) return entries.ToList(); }

        public static RunningEntry Get(Guid id) { lock (gate) return entries.FirstOrDefault(e => e.Id == id); }
        public static bool IsRunning(Guid id) { return Get(id) != null; }
        /// <summary>The one started last (null when nothing runs).</summary>
        public static RunningEntry Newest() { lock (gate) return entries.Count > 0 ? entries[entries.Count - 1] : null; }

        /// <summary>From OnGameStarted. <paramref name="installDir"/>: the game's folder with its variables already expanded.</summary>
        public static void Add(Game game, int startedPid, string installDir)
        {
            if (game == null) return;
            var e = new RunningEntry { Id = game.Id, Name = game.Name ?? "", Started = DateTime.Now, StartedPid = startedPid > 0 ? (uint)startedPid : 0, InstallDir = UsableDir(installDir) };
            if (e.StartedPid != 0) e.StartedPidCreated = CreatedAt(e.StartedPid);
            int n;
            lock (gate)
            {
                entries.RemoveAll(x => x.Id == e.Id);   // (started again without a stop in between: the new one counts)
                entries.Add(e);
                n = entries.Count;
            }
            logger.Info("Kippix: running: + " + e.Name + " (process " + (e.StartedPid != 0 ? e.StartedPid.ToString() : "-") + ", folder " + (e.InstallDir.Length > 0 ? e.InstallDir : "-") + "): " + n + " running");
            RaiseChanged();
        }

        /// <summary>From the stop path. False when it wasn't in the list.</summary>
        public static bool Remove(Guid id)
        {
            RunningEntry e;
            int n;
            lock (gate)
            {
                e = entries.FirstOrDefault(x => x.Id == id);
                if (e != null) entries.Remove(e);
                n = entries.Count;
            }
            if (e == null) return false;
            logger.Info("Kippix: running: - " + e.Name + " (after " + (int)(DateTime.Now - e.Started).TotalSeconds + " s): " + n + " running");
            RaiseChanged();
            return true;
        }

        private static void RaiseChanged()
        {
            System.Threading.Interlocked.Increment(ref version);
            var h = Changed;
            if (h != null) try { h(); } catch (Exception ex) { logger.Warn("Kippix: running: " + ex.Message); }
        }

        /// <summary>A folder worth matching programs against: a real one, not a drive or a short path, and never Windows' own.</summary>
        private static string UsableDir(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return "";
                string full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                if (full.Length <= 8) return "";
                string win = (Environment.GetEnvironmentVariable("WINDIR") ?? "").TrimEnd('\\') + "\\";
                if (win.Length > 1 && full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return "";
                foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
                {
                    string p = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(p) && string.Equals(p.TrimEnd('\\') + "\\", full, StringComparison.OrdinalIgnoreCase)) return "";   // (all of Program Files isn't one game)
                }
                return full;
            }
            catch (Exception) { return ""; }
        }

        // =====================================================================
        // The window finder
        // =====================================================================

        /// <summary>The game's window: the remembered one if it's still there, else found again. Zero when there isn't one.</summary>
        public static IntPtr FindWindow(RunningEntry e)
        {
            if (e == null) return IntPtr.Zero;
            try
            {
                if (StillThere(e)) return e.Window;
                var found = WindowsOf(e, true);
                IntPtr h = found.Count > 0 ? found[0] : IntPtr.Zero;
                Remember(e, h, h != IntPtr.Zero ? "found" : null);
                return h;
            }
            catch (Exception ex) { logger.Warn("Kippix: running: window finder: " + ex.Message); return IntPtr.Zero; }
        }

        /// <summary>The remembered window is still a window, still showing and still the same program's.</summary>
        private static bool StillThere(RunningEntry e)
        {
            IntPtr h = e.Window;
            if (h == IntPtr.Zero || !IsWindow(h) || !IsWindowVisible(h)) return false;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid != 0 && pid == e.WindowPid;
        }

        private static void Remember(RunningEntry e, IntPtr h, string how)
        {
            uint pid = 0;
            if (h != IntPtr.Zero) GetWindowThreadProcessId(h, out pid);
            e.Window = h;
            e.WindowPid = pid;
            string said = h == IntPtr.Zero ? "none" : h.ToInt64().ToString("X") + "/" + pid;
            if (said == e.LastFound) return;
            e.LastFound = said;
            if (h == IntPtr.Zero) logger.Info("Kippix: running: " + e.Name + ": no window found");
            else logger.Info("Kippix: running: " + e.Name + ": window 0x" + h.ToInt64().ToString("X") + " \"" + Title(h) + "\" (process " + pid + " " + ExeName(pid) + ", " + how + ")");
        }

        /// <summary>
        /// Every window of this game's, front-most first: visible, not owned by another window, with a title (first only: stops at
        /// the first one). A window counts when its program was started by the one Playnite started, lives in the game's folder,
        /// or was taken as the game's by LearnForeground.
        /// </summary>
        public static List<IntPtr> WindowsOf(RunningEntry e, bool firstOnly)
        {
            var list = new List<IntPtr>();
            if (e == null) return list;
            HashSet<uint> tree = TreeOf(e);
            var known = new Dictionary<uint, bool>();
            EnumWindows((h, l) =>
            {
                try
                {
                    if (!IsAppWindow(h)) return true;
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid == 0 || pid == ownPid) return true;
                    bool ok;
                    if (!known.TryGetValue(pid, out ok)) { ok = Belongs(e, pid, tree); known[pid] = ok; }
                    if (!ok) return true;
                    list.Add(h);
                    if (firstOnly) return false;
                }
                catch (Exception) { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>The programs to end when the game has to be forced closed: the one Playnite started (if it's still that one) and those with its windows.</summary>
        public static List<uint> ProcessesOf(RunningEntry e)
        {
            var pids = new List<uint>();
            if (e == null) return pids;
            try
            {
                if (e.StartedPid != 0 && e.StartedPid != ownPid && e.StartedPidCreated != 0 && CreatedAt(e.StartedPid) == e.StartedPidCreated) pids.Add(e.StartedPid);
                foreach (IntPtr h in WindowsOf(e, false))
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != 0 && pid != ownPid && !pids.Contains(pid)) pids.Add(pid);
                }
            }
            catch (Exception) { }
            return pids;
        }

        private static bool IsAppWindow(IntPtr h)
        {
            if (h == IntPtr.Zero || !IsWindowVisible(h)) return false;
            if (GetWindow(h, 4) != IntPtr.Zero) return false;                   // GW_OWNER: a dialog or a pop-up of another window
            if (GetWindowTextLength(h) == 0) return false;
            if ((GetWindowLong(h, -20) & 0x80) != 0) return false;              // WS_EX_TOOLWINDOW
            return true;
        }

        private static bool Belongs(RunningEntry e, uint pid, HashSet<uint> tree)
        {
            if (pid == 0 || pid == ownPid) return false;
            if (e.AdoptedPid != 0 && pid == e.AdoptedPid) return true;
            if (tree != null && tree.Contains(pid)) return true;
            if (e.InstallDir.Length > 0)
            {
                string path = Native.GetProcessPath(pid);
                if (path != null && path.StartsWith(e.InstallDir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The program Playnite started and everything it started in turn (it may have closed already: a launcher handing over). Null when Playnite didn't say which program.</summary>
        private static HashSet<uint> TreeOf(RunningEntry e)
        {
            if (e.StartedPid == 0) return null;
            var parents = ProcessParents();
            if (parents.Count == 0) return null;
            // the id was the started program's: if another program has it now, nothing descends from it any more
            long since = e.StartedPidCreated != 0 ? e.StartedPidCreated : e.Started.AddMinutes(-2).ToFileTimeUtc();
            var set = new HashSet<uint>();
            if (parents.ContainsKey(e.StartedPid))
            {
                long c = CreatedAt(e.StartedPid);
                if (c != 0 && e.StartedPidCreated != 0 && c != e.StartedPidCreated) return set;
                set.Add(e.StartedPid);
            }
            var children = new Dictionary<uint, List<uint>>();
            foreach (var kv in parents)
            {
                List<uint> l;
                if (!children.TryGetValue(kv.Value, out l)) { l = new List<uint>(); children[kv.Value] = l; }
                l.Add(kv.Key);
            }
            var todo = new Queue<uint>();
            todo.Enqueue(e.StartedPid);
            int guard = 0;
            while (todo.Count > 0 && guard++ < 4000)
            {
                List<uint> kids;
                if (!children.TryGetValue(todo.Dequeue(), out kids)) continue;
                foreach (uint k in kids)
                {
                    if (k == 0 || k == ownPid || set.Contains(k)) continue;
                    long c = CreatedAt(k);
                    if (c != 0 && c < since) continue;   // older than the game: its parent's id is a used-again one
                    set.Add(k);
                    todo.Enqueue(k);
                }
            }
            return set;
        }

        // =====================================================================
        // Learning from the window in front
        // =====================================================================

        private static readonly string[] NeverAdopted = { "explorer", "playnite", "steam", "steamwebhelper", "discord", "textinputhost", "searchhost", "shellexperiencehost", "startmenuexperiencehost", "applicationframehost", "lockapp", "gamebar", "taskmgr", "nvcontainer", "nvidia" };

        /// <summary>
        /// Once a second while anything runs (off the UI thread): the window in front, when it's one of a running game's, is
        /// remembered as that game's (so "switch to" goes back to the window last used). A game nothing else is known about (no
        /// started program, no folder match) takes the first new program whose window comes to the front after it started.
        /// </summary>
        private static IntPtr nobodysWindow;
        private static uint nobodysPid;
        private static int nobodysVersion = -1, version;   // (version: goes up with every start and stop)

        public static void LearnForeground()
        {
            try
            {
                var all = Snapshot();
                if (all.Count == 0) return;
                IntPtr fg = GetForegroundWindow();
                if (!IsAppWindow(fg)) return;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                if (pid == 0 || pid == ownPid) return;
                foreach (var e in all) if (e.Window == fg && e.WindowPid == pid) return;   // (known already: the usual case, nothing to work out)
                if (fg == nobodysWindow && pid == nobodysPid && version == nobodysVersion) return;         // (looked at already: another program's, and nothing has started or stopped since)
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    if (!Belongs(all[i], pid, TreeOf(all[i]))) continue;
                    Remember(all[i], fg, "in front");
                    return;
                }
                // nobody's: the newest game still without a window takes it, if the program is newer than the game
                nobodysWindow = fg; nobodysPid = pid; nobodysVersion = version;
                string exe = ExeName(pid).ToLowerInvariant();
                if (exe.Length == 0 || NeverAdopted.Any(x => exe.Contains(x))) return;
                long created = CreatedAt(pid);
                if (created == 0) return;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var e = all[i];
                    if (e.AdoptedPid != 0 || StillThere(e)) continue;
                    if ((DateTime.Now - e.Started).TotalMinutes > 3) continue;
                    // (a library add-on's game is only reported "started" a while after its program came up)
                    if (created < e.Started.AddSeconds(e.StartedPid == 0 ? -60 : -5).ToFileTimeUtc()) continue;
                    if (WindowsOf(e, true).Count > 0) continue;   // (it has a window of its own somewhere: this isn't it)
                    nobodysVersion = -1;
                    e.AdoptedPid = pid;
                    Remember(e, fg, "came to the front after it started");
                    return;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: running: learning the window: " + ex.Message); }
        }

        // =====================================================================
        // Windows
        // =====================================================================

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 pe);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 pe);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr h, out long created, out long exited, out long kernel, out long user);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public UIntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        /// <summary>Every program running now and the one that started it.</summary>
        private static Dictionary<uint, uint> ProcessParents()
        {
            var map = new Dictionary<uint, uint>();
            IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);   // TH32CS_SNAPPROCESS
            if (snap == IntPtr.Zero || snap == InvalidHandle) return map;
            try
            {
                var pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32First(snap, ref pe)) return map;
                do { map[pe.th32ProcessID] = pe.th32ParentProcessID; }
                while (Process32Next(snap, ref pe));
            }
            finally { CloseHandle(snap); }
            return map;
        }

        /// <summary>When a program was started (a file time; 0 when it can't be read or it has gone).</summary>
        private static long CreatedAt(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return 0;
            try
            {
                long c, x, k, u;
                return GetProcessTimes(h, out c, out x, out k, out u) ? c : 0;
            }
            finally { CloseHandle(h); }
        }

        private static string ExeName(uint pid)
        {
            try { string p = Native.GetProcessPath(pid); return string.IsNullOrEmpty(p) ? "" : Path.GetFileNameWithoutExtension(p); }
            catch (Exception) { return ""; }
        }

        private static string Title(IntPtr h)
        {
            var sb = new StringBuilder(200);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Asks a window to close, the way its own close button would (WM_CLOSE): it can still save, or ask first.</summary>
        public static bool AskToClose(IntPtr h)
        {
            return h != IntPtr.Zero && IsWindow(h) && PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
