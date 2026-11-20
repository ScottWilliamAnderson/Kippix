using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace KippixHelper
{
    internal class DownloadInfo
    {
        public string Key;
        public string Name;
        public string Source;      // "Steam", "Epic", "GOG", "Playnite"
        public Guid PluginId;          // the game's library add-on (to open its launcher)
        public Guid GameId;            // the game in Playnite, when known
        public string LauncherGameId;  // the launcher's own id (Steam app id, GOG product id...)
        public string State;       // "Downloading", "Paused", "Queued", ...
        public long Done;
        public long Total;
        public double Progress = -1;   // 0..1, or -1 when unknown
        public bool Estimated;         // live figures worked out from Steam's disk writes
        public double BytesPerSecond;  // > 0 while downloading

        public string Text()
        {
            string s = State;
            if (Progress >= 0) s += "  \u00B7  " + (Estimated ? "about " : "") + Math.Floor(Progress * 100) + "%";
            if (Total > 0) s += "  \u00B7  " + Size(Done) + " of " + Size(Total);
            if (BytesPerSecond > 512 * 1024) s += "  \u00B7  " + Math.Round(BytesPerSecond / 1048576.0) + " MB/s";
            return s + "  \u00B7  " + Source;
        }

        public static string Size(long bytes)
        {
            double gb = bytes / 1073741824.0;
            if (gb >= 1) return gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            return Math.Max(0, Math.Round(bytes / 1048576.0)) + " MB";
        }

        public DownloadInfo Copy() { return (DownloadInfo)MemberwiseClone(); }
    }

    /// <summary>Game downloads and updates in progress, read from the launchers' own files. Cheap enough to call every few seconds.</summary>
    internal static class Downloads
    {
        private static volatile List<DownloadInfo> cache = new List<DownloadInfo>();   // (replaced whole, never changed in place: Peek reads it without the lock)
        private static DateTime cacheAt = DateTime.MinValue;
        private static readonly object cacheLock = new object();

        /// <summary>Set by the plugin while a game is running (Steam suspends downloads during play by default).</summary>
        public static volatile bool GameRunning;

        /// <summary>The last list worked out, with no work and no waiting: for the UI thread (the overlay's System page).</summary>
        public static List<DownloadInfo> Peek() { return cache; }

        /// <summary>The next look goes to the launchers again straight away (and measures the GOG / Epic folders again).</summary>
        public static void ForceRefresh() { forceNext = true; }   // no waiting here: the next Get (under its own lock) does the work
        private static volatile bool forceNext;

        private static string steamLog;   // Steam's content_log.txt, as found by the last look

        /// <summary>
        /// The downloads now. One list for the theme and the overlay (each asks every few seconds): it's worked out again at most
        /// every 2.5 s. watching: the list is on screen (the downloads panel or widget, the overlay's System page).
        /// </summary>
        public static List<DownloadInfo> Get(IEnumerable<Playnite.SDK.Models.Game> library, bool watching = false)
        {
            // One refresh at a time (the plugin samples in the background and the Settings page reads too).
            lock (cacheLock)
            {
                if (forceNext)
                {
                    forceNext = false;
                    cacheAt = DateTime.MinValue;
                    var keys = folderSizes.Keys.ToList();
                    foreach (var k in keys) folderSizes[k] = Tuple.Create(DateTime.MinValue, folderSizes[k].Item2);
                    manifests.Clear();
                    epicItems.Clear();
                }
                double age = (DateTime.UtcNow - cacheAt).TotalSeconds;
                if (age < 2.5) return cache;
                // an empty list that nobody is looking at: the launchers' files are gone through every 15 s, or straight away
                // once Steam writes to its content log (a Steam download or update shows there first)
                if (!watching && cache.Count == 0 && age < 15 && !KippixShared.SteamContentLog.Changed(steamLog)) return cache;
                var list = new List<DownloadInfo>();
                try
                {
                    string steamPath = SteamPaths.SteamPath();
                    steamLog = steamPath != null ? Path.Combine(steamPath, @"logs\content_log.txt") : null;
                    list.AddRange(Steam(steamPath));
                }
                catch (Exception) { }
                try { list.AddRange(Epic()); } catch (Exception) { }
                List<Playnite.SDK.Models.Game> games;
                try { games = library != null ? library.Where(g => g != null).ToList() : new List<Playnite.SDK.Models.Game>(); }
                catch (Exception) { games = new List<Playnite.SDK.Models.Game>(); }   // (the library changed part-way through: no GOG or Playnite rows this time)
                try { list.AddRange(Gog(games)); } catch (Exception) { }
                try
                {
                    foreach (var g in games)
                    {
                        if (!g.IsInstalling) continue;
                        var same = list.FirstOrDefault(d => string.Equals(d.Name, g.Name, StringComparison.OrdinalIgnoreCase));
                        if (same != null) { same.GameId = g.Id; if (same.PluginId == Guid.Empty) same.PluginId = g.PluginId; continue; }
                        list.Add(new DownloadInfo { Key = "pn:" + g.Id, Name = g.Name, Source = SourceName(g), State = "Installing", PluginId = g.PluginId, GameId = g.Id, LauncherGameId = g.GameId });
                    }
                    // Steam / Epic downloads of games in the library: remember which game (the row opens that game's launcher)
                    foreach (var d in list.Where(d => d.GameId == Guid.Empty))
                    {
                        var g = games.FirstOrDefault(x => (d.LauncherGameId != null && x.GameId == d.LauncherGameId) || string.Equals(x.Name, d.Name, StringComparison.OrdinalIgnoreCase));
                        if (g != null) { d.GameId = g.Id; if (d.PluginId == Guid.Empty) d.PluginId = g.PluginId; }
                    }
                }
                catch (Exception) { }
                cache = list;
                cacheAt = DateTime.UtcNow;
                return list;
            }
        }

        // ---------------- Steam ----------------

        // appmanifest StateFlags
        private const long UpdateRequired = 2, UpdateRunning = 256, UpdatePaused = 512, UpdateStarted = 1024,
            Validating = 131072, AddingFiles = 262144, Preallocating = 524288, Downloading = 1048576,
            Staging = 2097152, Committing = 4194304;

        // each appmanifest as last read: read and parsed again only once Steam has written to it
        private class ManifestRead
        {
            public DateTime Time;
            public long Length;
            public DownloadInfo Info;   // null: installed, nothing pending
            public bool Running;
            public long Dl, ToDl, Staged, ToStage;
        }

        private static readonly Dictionary<string, ManifestRead> manifests = new Dictionary<string, ManifestRead>(StringComparer.OrdinalIgnoreCase);

        public static List<DownloadInfo> Steam(string steamPath)
        {
            var result = new List<DownloadInfo>();
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return result;
            ulong writes = 0;
            bool sampled = false;   // (Steam's disk writes are only asked for while one of its downloads runs)
            string log = Path.Combine(steamPath, @"logs\content_log.txt");
            Dictionary<string, string> logState = null;
            try { logState = KippixShared.SteamContentLog.States(log); } catch (Exception) { }
            // (0.99.72) live progress from the same log (the manifest's figure is only saved when a download starts or stops)
            Dictionary<string, KippixShared.SteamLogProgress> logProgress = null;
            try { logProgress = KippixShared.SteamContentLog.Read(log); } catch (Exception) { }

            var libraries = new List<string> { Path.Combine(steamPath, "steamapps") };
            string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                {
                    string lib = Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps");
                    if (!libraries.Any(x => string.Equals(x.TrimEnd('\\'), lib.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libraries.Add(lib);
                }
            }

            foreach (string lib in libraries)
            {
                FileInfo[] files;   // (their size and time come with the folder listing)
                try { files = Directory.Exists(lib) ? new DirectoryInfo(lib).GetFiles("appmanifest_*.acf") : new FileInfo[0]; }
                catch (Exception) { continue; }
                foreach (FileInfo fi in files)
                {
                    string f = fi.FullName;
                    ManifestRead mr;
                    if (!manifests.TryGetValue(f, out mr) || mr.Time != fi.LastWriteTimeUtc || mr.Length != fi.Length)
                    {
                        mr = new ManifestRead { Time = fi.LastWriteTimeUtc, Length = fi.Length };
                        try { mr.Info = ParseSteamManifest(File.ReadAllText(f), out mr.Running, out mr.Dl, out mr.ToDl, out mr.Staged, out mr.ToStage); manifests[f] = mr; }
                        catch (Exception) { manifests.Remove(f); }   // (tried again next time)
                    }
                    if (mr.Info == null) continue;
                    DownloadInfo d = mr.Info.Copy();
                    long staged = mr.Staged, toStage = mr.ToStage, dl = mr.Dl, toDl = mr.ToDl;
                    bool running = mr.Running;

                    // The manifest's flags are only saved when a download starts or stops, so they can't tell a
                    // running download from a suspended one. Steam's content log can.
                    string st;
                    string appId = d.Key.Substring("steam:".Length);
                    if (logState != null && logState.TryGetValue(appId, out st))
                    {
                        bool installed = d.State == "Updating";
                        switch (st)
                        {
                            case "downloading": running = true; d.State = installed ? "Updating" : "Downloading"; break;
                            case "installing": running = true; d.State = "Installing"; break;
                            case "verifying": running = true; d.State = "Verifying"; break;
                            case "suspended": running = false; d.State = "Paused"; break;
                            case "paused": running = false; d.State = "Paused"; break;
                            case "finished": continue;
                        }
                    }
                    if (running && !sampled) { writes = SteamDiskWrites(); UpdateRate(writes); sampled = true; }
                    KippixShared.SteamLogProgress lp;
                    if (running && logProgress != null && logProgress.TryGetValue(appId, out lp) && lp.Active && lp.ToDownload > 0 && (toDl <= 0 || lp.ToDownload == toDl || lp.Downloaded >= dl))
                    {
                        d.Total = lp.ToDownload;
                        d.Done = lp.Downloaded;
                        d.Progress = Math.Min(1, (double)lp.Downloaded / lp.ToDownload);
                        d.Estimated = true;
                        d.BytesPerSecond = lp.BytesPerSecond > 0 ? lp.BytesPerSecond : rate;
                    }
                    else if (running) Estimate(d, f, writes, dl, toDl, staged, toStage);
                    result.Add(d);
                }
            }
            return result;
        }

        public static DownloadInfo ParseSteamManifest(string text)
        {
            bool running;
            long a, b, c, e;
            return ParseSteamManifest(text, out running, out a, out b, out c, out e);
        }

        /// <summary>Null when the app is installed and nothing is pending.</summary>
        public static DownloadInfo ParseSteamManifest(string text, out bool running, out long dlOut, out long toDlOut, out long stagedOut, out long toStageOut)
        {
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text, "^\\s*\"(\\w+)\"\\s+\"([^\"]*)\"", RegexOptions.Multiline))
                if (!v.ContainsKey(m.Groups[1].Value)) v[m.Groups[1].Value] = m.Groups[2].Value;

            long flags = Num(v, "StateFlags");
            long toDl = Num(v, "BytesToDownload"), dl = Num(v, "BytesDownloaded");
            long toStage = Num(v, "BytesToStage"), staged = Num(v, "BytesStaged");

            dlOut = dl; toDlOut = toDl; stagedOut = staged; toStageOut = toStage;

            // "Update Started" without "Update Running" means Steam stopped it part-way: usually because a game
            // started (Steam suspends downloads during play unless "Allow downloads during gameplay" is on).
            running = (flags & (UpdateRunning | Validating | AddingFiles | Preallocating | Downloading | Staging | Committing)) != 0;
            bool started = (flags & UpdateStarted) != 0;
            bool paused = (flags & UpdatePaused) != 0;
            bool pending = toDl > 0 && dl < toDl;
            bool active = running;
            if (!running && !started && !paused && !((flags & UpdateRequired) != 0 && pending)) return null;

            var d = new DownloadInfo { Source = "Steam", PluginId = SteamLibraryId };
            string id;
            v.TryGetValue("appid", out id);
            d.Key = "steam:" + id;
            d.LauncherGameId = id;
            string name;
            d.Name = v.TryGetValue("name", out name) && name.Length > 0 ? name : "Steam app " + id;

            if (paused) d.State = "Paused";
            else if (!running && started) d.State = "Paused";
            else if ((flags & Validating) != 0) d.State = "Verifying";
            else if ((flags & (Staging | Committing)) != 0 && !pending) d.State = "Installing";
            else if (active) d.State = Num(v, "SizeOnDisk") == 0 || (flags & 4) == 0 ? "Downloading" : "Updating";
            else d.State = "Queued";

            if (toDl > 0) { d.Done = dl; d.Total = toDl; d.Progress = Math.Min(1, (double)dl / toDl); }
            else if (toStage > 0) { d.Done = staged; d.Total = toStage; d.Progress = Math.Min(1, (double)staged / toStage); }
            return d;
        }

        private static long Num(Dictionary<string, string> v, string key)
        {
            string s;
            long n;
            return v.TryGetValue(key, out s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        // ---------------- Live estimate ----------------
        // Steam only saves progress to the manifest when a download starts, pauses or finishes. While it runs,
        // the bytes Steam writes to disk track the staged data closely, so progress = saved figure + writes since.

        private class Track
        {
            public DateTime ManifestTime;
            public ulong BaseWrites;
            public long BaseStaged, BaseDl;
        }

        private static readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();
        private static ulong rateWrites;
        private static DateTime rateAt = DateTime.MinValue;
        private static double rate;

        private static void Estimate(DownloadInfo d, string manifest, ulong writes, long dl, long toDl, long staged, long toStage)
        {
            d.BytesPerSecond = rate;
            if (writes == 0) return;
            DateTime mtime = File.GetLastWriteTimeUtc(manifest);
            Track t;
            lock (tracks)
            {
                if (!tracks.TryGetValue(d.Key, out t) || t.ManifestTime != mtime || writes < t.BaseWrites)
                {
                    t = new Track { ManifestTime = mtime, BaseWrites = writes, BaseStaged = staged, BaseDl = dl };
                    tracks[d.Key] = t;
                }
            }
            long extra = (long)(writes - t.BaseWrites);
            double fraction;
            if (toStage > 0) fraction = Math.Min(1.0, (double)(t.BaseStaged + extra) / toStage);
            else if (toDl > 0) fraction = Math.Min(1.0, (double)(t.BaseDl + extra) / toDl);
            else return;
            d.Progress = fraction;
            d.Estimated = extra > 0;
            if (toDl > 0) { d.Total = toDl; d.Done = (long)(fraction * toDl); }
        }

        private static void UpdateRate(ulong writes)
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - rateAt).TotalSeconds;
            if (rateAt != DateTime.MinValue && writes >= rateWrites && dt > 0.5 && dt < 30)
            {
                double r = (writes - rateWrites) / dt;
                rate = rate <= 0 ? r : rate * 0.5 + r * 0.5;
            }
            else if (dt >= 30) rate = 0;
            rateWrites = writes;
            rateAt = now;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
        [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        private const uint StillActive = 259;

        private class SteamProc
        {
            public int Pid;
            public IntPtr Handle;
            public ulong Last;   // its write count when last asked
        }

        private static readonly List<SteamProc> steamProcs = new List<SteamProc>();
        private static DateTime steamProcsAt = DateTime.MinValue;
        private static ulong steamWrites;

        /// <summary>Total bytes Steam's processes have written to disk. Only goes up: a process that quits stops adding to it,
        /// one found later adds what it writes from then on.</summary>
        private static ulong SteamDiskWrites()
        {
            // Steam's processes are looked up by name every 60 s (30 s while there's none) or when one has quit, because a
            // lookup lists every process and thread on the PC. In between, the handles kept open are asked.
            bool quit = false;
            for (int i = steamProcs.Count - 1; i >= 0; i--)
            {
                uint code;
                if (GetExitCodeProcess(steamProcs[i].Handle, out code) && code == StillActive) continue;
                CloseHandle(steamProcs[i].Handle);
                steamProcs.RemoveAt(i);
                quit = true;
            }
            if (quit || (DateTime.UtcNow - steamProcsAt).TotalSeconds >= (steamProcs.Count > 0 ? 60 : 30)) FindSteamProcs();
            foreach (var p in steamProcs)
            {
                IoCounters c;
                if (!GetProcessIoCounters(p.Handle, out c)) continue;
                if (c.WriteTransferCount > p.Last) steamWrites += c.WriteTransferCount - p.Last;
                p.Last = c.WriteTransferCount;
            }
            return steamWrites;
        }

        private static void FindSteamProcs()
        {
            steamProcsAt = DateTime.UtcNow;
            foreach (string name in new[] { "steam", "steamservice" })
            {
                System.Diagnostics.Process[] procs;
                try { procs = System.Diagnostics.Process.GetProcessesByName(name); } catch (Exception) { continue; }
                foreach (var p in procs)
                {
                    int id = p.Id;
                    p.Dispose();
                    if (steamProcs.Any(x => x.Pid == id)) continue;
                    IntPtr h = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
                    if (h == IntPtr.Zero) continue;
                    var sp = new SteamProc { Pid = id, Handle = h };
                    // the first ones found count from their start (as before); one found later from now, so the total doesn't jump
                    IoCounters c;
                    if (steamWrites > 0 && GetProcessIoCounters(h, out c)) sp.Last = c.WriteTransferCount;
                    steamProcs.Add(sp);
                }
            }
        }

        // ---------------- Epic ----------------

        internal static readonly Guid SteamLibraryId = new Guid("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        internal static readonly Guid EpicLibraryId = new Guid("00000002-dbd1-46c6-b5d0-b1ba559d10e4");
        internal static readonly Guid GogLibraryId = new Guid("aebe8b7c-6dc3-4a66-af31-e7375c6b5e9e");

        private static string SourceName(Playnite.SDK.Models.Game g)
        {
            if (g.PluginId == SteamLibraryId) return "Steam";
            if (g.PluginId == EpicLibraryId) return "Epic";
            if (g.PluginId == GogLibraryId) return "GOG";
            return g.Source != null && !string.IsNullOrEmpty(g.Source.Name) ? g.Source.Name : "Playnite";
        }

        // folder sizes, measured now and then (they can be big): key -> (when, bytes); and when each last grew
        private static readonly Dictionary<string, Tuple<DateTime, long>> folderSizes = new Dictionary<string, Tuple<DateTime, long>>();
        private static readonly Dictionary<string, DateTime> lastGrew = new Dictionary<string, DateTime>();

        private static long FolderSize(string key, string dir, int everySeconds)
        {
            Tuple<DateTime, long> was;
            if (folderSizes.TryGetValue(key, out was) && (DateTime.UtcNow - was.Item1).TotalSeconds < everySeconds) return was.Item2;
            long size = 0;
            try { foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) { try { size += f.Length; } catch (Exception) { } } }
            catch (Exception) { }
            if (was != null && size > was.Item2) lastGrew[key] = DateTime.UtcNow;
            folderSizes[key] = Tuple.Create(DateTime.UtcNow, size);
            return size;
        }

        private static bool GrewLately(string key, int seconds)
        {
            DateTime t;
            return lastGrew.TryGetValue(key, out t) && (DateTime.UtcNow - t).TotalSeconds < seconds;
        }

        // each .item as last read: read and parsed again only once Epic has written to it
        private static readonly Dictionary<string, Tuple<DateTime, long, Dictionary<string, object>>> epicItems =
            new Dictionary<string, Tuple<DateTime, long, Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Epic marks unfinished installs; how far along it is comes from the install folder filling up
        /// against the size Epic says it will be.
        /// </summary>
        private static List<DownloadInfo> Epic()
        {
            var result = new List<DownloadInfo>();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) return result;
            JavaScriptSerializer json = null;
            // installs Epic is still working on sit in Manifests\Pending (the finished ones in Manifests, marked incomplete until done)
            var files = new List<FileInfo>();
            string pendingDir = Path.Combine(dir, "Pending");
            try { if (Directory.Exists(pendingDir)) files.AddRange(new DirectoryInfo(pendingDir).GetFiles("*.item")); } catch (Exception) { }   // first: in progress wins over "not finished"
            try { files.AddRange(new DirectoryInfo(dir).GetFiles("*.item")); } catch (Exception) { }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileInfo fi in files)
            {
                string f = fi.FullName;
                try
                {
                    Tuple<DateTime, long, Dictionary<string, object>> item;
                    if (!epicItems.TryGetValue(f, out item) || item.Item1 != fi.LastWriteTimeUtc || item.Item2 != fi.Length)
                    {
                        if (json == null) json = new JavaScriptSerializer();
                        item = Tuple.Create(fi.LastWriteTimeUtc, fi.Length, json.DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>);
                        epicItems[f] = item;
                    }
                    var d = item.Item3;
                    if (d == null) continue;
                    bool pending = f.StartsWith(pendingDir, StringComparison.OrdinalIgnoreCase);
                    object inc;
                    bool incomplete = d.TryGetValue("bIsIncompleteInstall", out inc) && inc is bool && (bool)inc;
                    if (!pending && !incomplete) continue;
                    if (!seen.Add(Path.GetFileNameWithoutExtension(f))) continue;
                    object name, loc, size, app;
                    d.TryGetValue("DisplayName", out name);
                    d.TryGetValue("InstallLocation", out loc);
                    d.TryGetValue("InstallSize", out size);
                    d.TryGetValue("AppName", out app);
                    string key = "epic:" + Path.GetFileNameWithoutExtension(f);
                    var info = new DownloadInfo
                    {
                        Key = key,
                        Name = string.IsNullOrEmpty(Convert.ToString(name)) ? "Epic game" : Convert.ToString(name),   // Convert gives "" for null, never null
                        Source = "Epic",
                        State = pending ? "Downloading" : "Not finished",
                        PluginId = EpicLibraryId,
                        LauncherGameId = Convert.ToString(app)
                    };
                    long total = 0;
                    try { total = Convert.ToInt64(size); } catch (Exception) { }
                    string folder = Convert.ToString(loc);
                    if (total > 0 && !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        long done = FolderSize(key, folder, 12);
                        info.Done = Math.Min(done, total);
                        info.Total = total;
                        info.Progress = Math.Min(1, (double)done / total);
                        if (GrewLately(key, 40)) info.State = "Downloading";
                    }
                    result.Add(info);
                }
                catch (Exception) { }
            }
            return result;
        }

        // ---------------- GOG Galaxy ----------------

        private static string gogLibrary;
        private static DateTime gogLibraryAt = DateTime.MinValue;

        /// <summary>Where GOG Galaxy puts games (its settings), or its usual folder.</summary>
        private static string GogLibraryPath()
        {
            if ((DateTime.UtcNow - gogLibraryAt).TotalMinutes < 10) return gogLibrary;
            gogLibraryAt = DateTime.UtcNow;
            gogLibrary = null;
            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"GOG.com\Galaxy\Configuration\config.json");
                if (File.Exists(cfg))
                {
                    var m = Regex.Match(File.ReadAllText(cfg), "\"(?:libraryPath|installationPath)\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) gogLibrary = m.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                }
            }
            catch (Exception) { }
            if (string.IsNullOrEmpty(gogLibrary) || !Directory.Exists(gogLibrary))
            {
                string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"GOG Galaxy\Games");
                gogLibrary = Directory.Exists(def) ? def : null;
            }
            return gogLibrary;
        }

        /// <summary>
        /// GOG Galaxy keeps its downloads in a database we can't read, so: a GOG game that isn't installed yet but whose folder
        /// in Galaxy's games folder is filling up is downloading (shown with how much has come down; Galaxy doesn't say the total).
        /// </summary>
        private static List<DownloadInfo> Gog(List<Playnite.SDK.Models.Game> games)
        {
            var result = new List<DownloadInfo>();
            string lib = GogLibraryPath();
            if (lib == null) return result;
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (string d in Directory.GetDirectories(lib)) folders[Simple(Path.GetFileName(d))] = d; } catch (Exception) { return result; }
            var installedNames = new HashSet<string>(games.Where(x => x.PluginId == GogLibraryId && x.IsInstalled).Select(x => Simple(x.Name)));
            var gogGames = games.Where(x => x.PluginId == GogLibraryId && !x.IsInstalled).GroupBy(x => Simple(x.Name)).ToDictionary(x => x.Key, x => x.First());
            foreach (var kv in folders)
            {
                if (installedNames.Contains(kv.Key) || kv.Key.Length == 0) continue;   // a finished game (an update shows up as the folder changing, below, only when it isn't installed)
                string folder = kv.Value;
                // only folders Galaxy is working in now: made in the last few hours, or filling up (an old leftover folder isn't a download)
                DateTime touched;
                try { touched = Directory.GetCreationTimeUtc(folder); } catch (Exception) { continue; }   // made lately = a new install (an old game's folder changing is an update)
                Playnite.SDK.Models.Game g;
                gogGames.TryGetValue(kv.Key, out g);
                string key = "gog:" + (g != null ? g.GameId : kv.Key);
                bool recent = (DateTime.UtcNow - touched).TotalHours < 6;
                // watched for filling up: a GOG game you have that isn't installed, or a folder Galaxy made in the last few hours
                if (g == null && !recent) continue;
                long done = FolderSize(key, folder, 10);
                bool growing = GrewLately(key, 60);
                // shown only while it's filling up (a just-finished install, or a folder named differently from the game, stays out)
                if (!growing) continue;
                long total = g != null && g.InstallSize.HasValue ? (long)g.InstallSize.Value : 0;
                result.Add(new DownloadInfo
                {
                    Key = key, Name = g != null ? g.Name : Path.GetFileName(folder), Source = "GOG", State = "Downloading",
                    PluginId = GogLibraryId, GameId = g != null ? g.Id : Guid.Empty, LauncherGameId = g != null ? g.GameId : null,
                    Done = done, Total = total, Progress = total > 0 ? Math.Min(1, (double)done / total) : -1
                });
            }
            return result;
        }

        private static string Simple(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
