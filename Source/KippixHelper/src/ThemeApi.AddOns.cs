using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Input;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Optional support for other add-ons, used only when they're installed:
    /// Ludusavi (save backups card on the game page, back up / restore, automatic backups around playing),
    /// darklinkpower's Game Pass Catalog Browser (a Game Pass row in the Store), Nexus Mods checker (a Mods on Nexus button),
    /// Game Engine Checker (the engine in the game page's info), NVIDIA Freestyle checker (a small badge),
    /// Splash Screen (our launch screen stands aside) and Steam Trailers / Web Explorer (noticed, so nothing is doubled up).
    /// (0.98) UniPlaySong and BackgroundChanger: ThemeApi.MediaAddons.cs.
    /// Settings › General settings › Other add-ons shows only the ones that are installed.
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly Guid LudusaviId = new Guid("72e2de43-d859-44d8-914e-4277741c8208");
        private static readonly Guid GamePassBrowserId = new Guid("50c85177-570f-4494-be16-99d6aa5b8a93");
        private static readonly Guid SplashScreenId = new Guid("d8c4f435-2bd2-49d8-98f6-87b1d415934a");
        private static readonly Guid WebExplorerId = new Guid("181ddd05-2168-4162-a116-b9c2a20c652c");
        private const string NexusScriptId = "Nexus_Mods_Checker_ece2874c-be52-4a64-b178-ed379a042f85";
        private const string EngineScriptId = "Game_Engine_Checker_7a21243e-c7cc-4ca7-85bd-f6f96f22e9db";
        private const string FreestyleScriptId = "NVIDIA_Freestyle_Checker";
        private const string SteamTrailersScriptId = "Steam_Trailers";

        private bool PluginInstalled(Guid id)
        {
            try { return api.Addons.Plugins.Any(p => p != null && p.Id == id); } catch (Exception) { return false; }
        }

        /// <summary>A script add-on (no plugin object): from Playnite's list of installed add-ons, when it can be read.</summary>
        private bool ScriptInstalled(string yamlId)
        {
            try
            {
                object addons = api.Addons;
                var all = Prop(addons, "Addons") as IEnumerable;
                var off = Prop(addons, "DisabledAddons") as IEnumerable;
                if (all == null) return false;
                bool has = all.Cast<object>().Any(a => string.Equals(Convert.ToString(a), yamlId, StringComparison.OrdinalIgnoreCase));
                bool disabled = off != null && off.Cast<object>().Any(a => string.Equals(Convert.ToString(a), yamlId, StringComparison.OrdinalIgnoreCase));
                return has && !disabled;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Another plugin's own settings (its config.json), or null.</summary>
        private Dictionary<string, object> PluginConfig(Guid id)
        {
            try
            {
                string file = Path.Combine(api.Paths.ExtensionsDataPath, id.ToString(), "config.json");
                if (!File.Exists(file)) return null;
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
            }
            catch (Exception) { return null; }
        }

        private static object Cfg(Dictionary<string, object> d, params string[] path)
        {
            object cur = d;
            foreach (string p in path)
            {
                var dd = cur as Dictionary<string, object>;
                if (dd == null || !dd.TryGetValue(p, out cur)) return null;
            }
            return cur;
        }

        // =====================================================================
        // Game page bits: engine, Freestyle, Nexus Mods
        // =====================================================================

        public string DetEngine { get; private set; }
        public bool HasDetEngine { get { return settings.ShowEngine && !string.IsNullOrEmpty(DetEngine); } }
        public bool HasDetFreestyle { get; private set; }
        public bool ShowDetFreestyle { get { return settings.ShowFreestyle && HasDetFreestyle; } }
        private string detNexusUrl;
        // (r15) the Nexus Mods button (ThemeApi.NexusButton.cs): this link, or the Mods card's match in Nexus Mods' list
        public bool ShowDetNexus { get { return settings.ShowNexusLink && !string.IsNullOrEmpty(NexusButtonUrl); } }
        public ICommand OpenNexusCommand { get; private set; }

        private void FillAddonBits(Game g)
        {
            DetEngine = null;
            HasDetFreestyle = false;
            detNexusUrl = null;
            try
            {
                if (g != null)
                {
                    // Game Engine Checker: a tag "[Engine] Unreal Engine 4"
                    var tag = g.Tags != null ? g.Tags.FirstOrDefault(t => t != null && t.Name != null && t.Name.StartsWith("[Engine]", StringComparison.OrdinalIgnoreCase)) : null;
                    if (tag != null) DetEngine = tag.Name.Substring(8).Trim();
                    // NVIDIA Freestyle checker: a feature "NVIDIA Freestyle"
                    HasDetFreestyle = g.Features != null && g.Features.Any(f => f != null && string.Equals(f.Name, "NVIDIA Freestyle", StringComparison.OrdinalIgnoreCase));
                    // Nexus Mods checker: a link "Nexus Mods" (or any link to nexusmods.com), or only its "Nexus Mods" feature: a search there
                    var link = g.Links != null ? g.Links.FirstOrDefault(l => l != null && !string.IsNullOrEmpty(l.Url)
                                    && ((l.Name ?? "").IndexOf("nexus", StringComparison.OrdinalIgnoreCase) >= 0 || l.Url.IndexOf("nexusmods.com", StringComparison.OrdinalIgnoreCase) >= 0)) : null;
                    if (link != null) detNexusUrl = link.Url;
                    else if (g.Features != null && g.Features.Any(f => f != null && f.Name != null && f.Name.IndexOf("nexus", StringComparison.OrdinalIgnoreCase) >= 0))
                        detNexusUrl = "https://www.nexusmods.com/search?keyword=" + Uri.EscapeDataString(g.Name ?? "");
                }
            }
            catch (Exception) { }
            foreach (string n in new[] { "DetEngine", "HasDetEngine", "HasDetFreestyle", "ShowDetFreestyle", "ShowDetNexus", "ShowModsCardButtons" }) Notify(n);
        }

        private void InitAddOns()
        {
            OpenNexusCommand = new RelayCommand(_ => OpenNexusChoice());   // (r15) Nexus Mods, or Vortex / Mod Organizer 2 when installed
            DetSavesCommand = new RelayCommand(_ => OpenDetSaves());
            InitDialogFocus();   // Playnite's message boxes (Ludusavi's "Back up save data for …?") take the controller
        }

        // =====================================================================
        // Splash Screen: our launch screen stands aside
        // =====================================================================

        /// <summary>Splash Screen will show its own picture for this game launch (so ours keeps out of the way).</summary>
        private bool SplashScreenTakesOver(Game g)
        {
            if (!settings.SplashStandAside || !PluginInstalled(SplashScreenId)) return false;
            try
            {
                if (g != null && g.Features != null && g.Features.Any(f => f != null && string.Equals(f.Name, "[Splash Screen] Disable", StringComparison.OrdinalIgnoreCase))) return false;
                var cfg = PluginConfig(SplashScreenId);
                object on = Cfg(cfg, "GeneralSplashSettings", "FullscreenModeSettings", "IsEnabled");
                return !(on is bool) || (bool)on;
            }
            catch (Exception) { return true; }
        }

        // =====================================================================
        // Game Pass (Game Pass Catalog Browser's catalog) in the Store
        // =====================================================================

        private bool GamePassAvailable { get { return settings.UseGamePass && PluginInstalled(GamePassBrowserId) && File.Exists(GamePassCacheFile); } }
        private string GamePassCacheFile { get { return Path.Combine(api.Paths.ExtensionsDataPath, GamePassBrowserId.ToString(), "cache", "gamesCache.json"); } }

        /// <summary>The Store's Game Pass row: games on PC Game Pass (and EA Play), newest first.</summary>
        private List<StoreItem> GamePassItems()
        {
            var list = new List<StoreItem>();
            string json;
            using (var fs = new FileStream(GamePassCacheFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8)) json = sr.ReadToEnd();
            var arr = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[];
            if (arr == null) return list;
            string imgDir = Path.Combine(Path.GetDirectoryName(GamePassCacheFile), "images");
            var rows = new List<Tuple<DateTime, StoreItem>>();
            foreach (var o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                int type = 1;
                object t;
                if (d.TryGetValue("ProductType", out t) && t != null)
                {
                    // (a number, or the enum's name; TryParse would otherwise leave 0 = "collection" for a name)
                    string ts = Convert.ToString(t, CultureInfo.InvariantCulture);
                    int tv;
                    if (int.TryParse(ts, NumberStyles.Integer, CultureInfo.InvariantCulture, out tv)) type = tv;
                    else if (string.Equals(ts, "Collection", StringComparison.OrdinalIgnoreCase)) type = 0;
                    else if (ts.StartsWith("Ea", StringComparison.OrdinalIgnoreCase)) type = 2;
                }
                if (type == 0) continue;   // collections
                object child;
                if (d.TryGetValue("IsChildProduct", out child) && child is bool && (bool)child) continue;
                string name = FStr(d, "Name"), pid = FStr(d, "ProductId");
                if (name.Length == 0 || pid.Length == 0) continue;
                string bg = FStr(d, "BackgroundImage"), bgUrl = FStr(d, "BackgroundImageUrl"), cover = FStr(d, "CoverImageUrl");
                string localBg = bg.Length > 0 ? Path.Combine(imgDir, bg) : null;
                var item = new StoreItem
                {
                    Store = "", Id = "gamepass:" + pid, Name = name,
                    Image = localBg != null && File.Exists(localBg) ? localBg : (bgUrl.Length > 0 ? bgUrl : cover),
                    Link = "https://www.xbox.com/games/store/x/" + pid,
                    AppLink = "msxbox://game/?productId=" + pid,
                    Note = type == 2 ? Loc("LOC_KX_OnEaPlay", "EA Play (Game Pass)") : Loc("LOC_KX_OnGamePass", "On Game Pass")
                };
                DateTime rel = DateTime.MinValue;
                object r;
                if (d.TryGetValue("ReleaseDate", out r) && r != null)
                {
                    // JavaScriptSerializer only turns "\/Date(...)\/" into a DateTime: an ISO date stays a string
                    if (r is DateTime) rel = (DateTime)r;
                    else if (!DateTime.TryParse(Convert.ToString(r, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out rel)) rel = DateTime.MinValue;
                }
                rows.Add(Tuple.Create(rel, item));
            }
            return rows.OrderByDescending(x => x.Item1).Select(x => x.Item2).Take(30).ToList();
        }

        // =====================================================================
        // Ludusavi: save backups
        // =====================================================================

        private bool LudusaviInstalled { get { return PluginInstalled(LudusaviId); } }
        public bool ShowDetSaves { get { return settings.DetSecSaves && LudusaviInstalled; } }
        public bool DetIntSaves { get { return DetInteractive("saves") && !DetBlank("saves"); } }
        public string DetSavesLine { get; private set; }
        public string DetSavesSub { get; private set; }
        private void OpenDetSaves()
        {
            // a profile whose settings need the PIN: ask first, then come straight back here
            if (openPanel != "options" && ProfileGate("settings", OpenDetSaves)) return;
            PlaySound("select");
            savesGame = SelectedGameNow();
            if (openPanel != "options") TogglePanel("options");
            SetOptionsPage("detsaves");
        }

        public ICommand DetSavesCommand { get; private set; }
        public bool IsOptionsDetSaves { get { return optionsPage == "detsaves"; } }
        private ObservableCollection<PickRow> savesRows;
        public ObservableCollection<PickRow> SavesRows { get { return savesRows ?? (savesRows = new ObservableCollection<PickRow>()); } }
        private Game savesGame;
        private string savesStatus;            // the last thing that happened on the saves page
        private bool savesBusy;
        private DateTime? savesLast;           // the game's newest backup (null: none / not known)
        private string savesLastBackupName;
        // (0.99.67) every backup of the game, newest first: each one can be restored (not just the last)
        private List<SaveBackup> savesList = new List<SaveBackup>();
        private class SaveBackup { public DateTime When; public string Name; public string Comment; public bool Locked; }
        private readonly Dictionary<Guid, string> ludusaviTitles = new Dictionary<Guid, string>();   // "" = Ludusavi doesn't know it
        private int savesGen;
        private readonly object savesLookupLock = new object();

        private class LudusaviSetup
        {
            public string Exe = "ludusavi";
            public string BackupPath;        // null: Ludusavi's own
            public bool OwnAfterPlay;        // the add-on itself backs up after playing
        }

        private LudusaviSetup ReadLudusaviSetup()
        {
            var s = new LudusaviSetup();
            var cfg = PluginConfig(LudusaviId);
            if (cfg == null) { s.BackupPath = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\ludusavi-playnite"); s.OwnAfterPlay = true; return s; }
            string exe = Convert.ToString(Cfg(cfg, "ExecutablePath"));
            if (!string.IsNullOrWhiteSpace(exe)) s.Exe = Environment.ExpandEnvironmentVariables(exe.Trim());
            object over = Cfg(cfg, "OverrideBackupPath");
            string path = Convert.ToString(Cfg(cfg, "BackupPath"));
            if (!(over is bool) || (bool)over) s.BackupPath = Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(path) ? @"%USERPROFILE%\ludusavi-playnite" : path);
            object after = Cfg(cfg, "DoBackupOnGameStopped");
            s.OwnAfterPlay = !(after is bool) || (bool)after;
            return s;
        }

        /// <summary>One command-line argument in quotes (Windows rules: backslashes before a quote, or at the end, are doubled,
        /// so a path ending in \ doesn't swallow the closing quote).</summary>
        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in s ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Runs Ludusavi (no window) and gives back what it printed, or null if it couldn't be started.</summary>
        private string RunLudusavi(LudusaviSetup s, string command, string after, int timeoutMs, out int exit)
        {
            exit = -1;
            // only a backup or restore asks Ludusavi to check its list of games online first (looking things up stays quick)
            bool update = command.StartsWith("backup", StringComparison.Ordinal) || command.StartsWith("restore", StringComparison.Ordinal);
            string args = (update ? "--try-manifest-update " : "") + command + " --api" + (s.BackupPath != null ? " --path " + Quote(s.BackupPath) : "") + (after != null ? " " + after : "");
            var psi = new ProcessStartInfo(s.Exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    var err = new StringBuilder();
                    p.ErrorDataReceived += (o, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                    p.BeginErrorReadLine();
                    // read alongside, so the time limit holds even if Ludusavi hangs without closing its output
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } return null; }
                    p.WaitForExit();   // (the error lines finish arriving)
                    if (!outTask.Wait(10000)) return null;
                    string outText = outTask.Result;
                    exit = p.ExitCode;
                    if (exit != 0 && err.Length > 0) logger.Info("Kippix: Ludusavi " + command + ": " + err.ToString().Trim());
                    return outText;
                }
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: Ludusavi couldn't be started (" + s.Exe + "): " + ex.Message);
                return null;
            }
        }

        private static Dictionary<string, object> JsonObj(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text) as Dictionary<string, object>; } catch (Exception) { return null; }
        }

        /// <summary>Ludusavi's own name for the game ("" if it doesn't know it, null if Ludusavi couldn't be run).</summary>
        private string LudusaviTitle(LudusaviSetup s, Game g)
        {
            lock (ludusaviTitles) { string known; if (ludusaviTitles.TryGetValue(g.Id, out known)) return known; }
            string steam = SteamIdOf(g);
            int exit;
            string outText = RunLudusavi(s, "find", (steam != null ? "--steam-id " + steam + " " : "") + "-- " + Quote(g.Name), 60000, out exit);
            if (outText == null) return null;
            string title = "";
            var root = JsonObj(outText);
            var games = root != null ? Cfg(root, "games") as Dictionary<string, object> : null;
            if (exit == 0 && games != null && games.Count > 0) title = games.Keys.First();
            lock (ludusaviTitles) ludusaviTitles[g.Id] = title;
            return title;
        }

        /// <summary>The game's backups (Ludusavi's list), newest first.</summary>
        private List<SaveBackup> LudusaviBackupList(LudusaviSetup s, string title)
        {
            var list = new List<SaveBackup>();
            int exit;
            string outText = RunLudusavi(s, "backups", "-- " + Quote(title), 60000, out exit);
            var root = JsonObj(outText);
            var backups = Cfg(root, "games", title, "backups") as object[];
            if (backups == null) return list;
            foreach (var b in backups)
            {
                var d = b as Dictionary<string, object>;
                if (d == null) continue;
                DateTime when;
                if (!DateTime.TryParse(FStr(d, "when"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when)) continue;
                object lk;
                list.Add(new SaveBackup
                {
                    When = when.ToLocalTime(), Name = FStr(d, "name"), Comment = FStr(d, "comment"),
                    Locked = d.TryGetValue("locked", out lk) && lk is bool && (bool)lk
                });
            }
            return list.OrderByDescending(x => x.When).ToList();
        }

        /// <summary>"2 hours ago" (just the how-long-ago part of WhenText).</summary>
        private string WhenAgo(DateTime when)
        {
            var ago = DateTime.Now - when;
            return ago.TotalMinutes < 2 ? Loc("LOC_KX_JustNow", "just now")
                 : ago.TotalHours < 1 ? string.Format(Loc("LOC_KX_MinutesAgo", "{0} minutes ago"), (int)ago.TotalMinutes)
                 : ago.TotalDays < 1 ? string.Format(Loc("LOC_KX_HoursAgo", "{0} hours ago"), (int)ago.TotalHours)
                 : ago.TotalDays < 2 ? Loc("LOC_KX_Yesterday", "yesterday")
                 : string.Format(Loc("LOC_KX_DaysAgo", "{0} days ago"), (int)ago.TotalDays);
        }

        private string WhenText(DateTime when)
        {
            var ago = DateTime.Now - when;
            string rel = ago.TotalMinutes < 2 ? Loc("LOC_KX_JustNow", "just now")
                       : ago.TotalHours < 1 ? string.Format(Loc("LOC_KX_MinutesAgo", "{0} minutes ago"), (int)ago.TotalMinutes)
                       : ago.TotalDays < 1 ? string.Format(Loc("LOC_KX_HoursAgo", "{0} hours ago"), (int)ago.TotalHours)
                       : ago.TotalDays < 2 ? Loc("LOC_KX_Yesterday", "yesterday")
                       : string.Format(Loc("LOC_KX_DaysAgo", "{0} days ago"), (int)ago.TotalDays);
            return when.ToString("d MMM, HH:mm", CultureInfo.CurrentCulture) + " (" + rel + ")";
        }

        /// <summary>The game page's Save backups card (looked up when the game page opens; nothing waits for it).</summary>
        private void FillSavesCard(Game g)
        {
            int gen = ++savesGen;
            savesLast = null;
            savesLastBackupName = null;
            savesList = new List<SaveBackup>();
            if (!ShowDetSaves || g == null) { SetSavesCard("", ""); return; }
            SetSavesCard(Loc("LOC_KX_SavesChecking", "Checking…"), Loc("LOC_KX_SavesCardSub", "Back up now or restore"));
            var game = g;
            KippixShared.SafeWork.Queue(_ =>
            {
                // going through games quickly: only the one you stop on is looked up, one at a time
                Thread.Sleep(700);
                if (gen != savesGen) return;
                lock (savesLookupLock)
                {
                if (gen != savesGen) return;
                string line, sub = Loc("LOC_KX_SavesCardSub", "Back up now or restore");
                DateTime? last = null;
                string lastName = null;
                var all = new List<SaveBackup>();
                try
                {
                    var s = ReadLudusaviSetup();
                    string title = LudusaviTitle(s, game);
                    if (title == null) { line = Loc("LOC_KX_SavesNoProgram", "Ludusavi (the program) wasn't found"); sub = Loc("LOC_KX_SavesNoProgramSub", "Set where it is in the Ludusavi add-on's settings (desktop mode)"); }
                    else if (title.Length == 0) { line = Loc("LOC_KX_SavesUnknown", "Ludusavi doesn't know where this game saves"); sub = ""; }
                    else
                    {
                        all = LudusaviBackupList(s, title);
                        if (all.Count > 0) { last = all[0].When; lastName = all[0].Name; }
                        line = last.HasValue ? string.Format(Loc("LOC_KX_SavesLast", "Last backup: {0}"), WhenText(last.Value)) : Loc("LOC_KX_SavesNone", "No backups yet");
                        if (all.Count > 1) sub = string.Format(Loc("LOC_KX_SavesCount", "{0} backups to restore from"), all.Count);
                    }
                }
                catch (Exception ex) { line = Loc("LOC_KX_SavesError", "Couldn't ask Ludusavi"); logger.Warn("Kippix: Ludusavi: " + ex.Message); }
                OnUi(() =>
                {
                    if (gen != savesGen) return;
                    savesLast = last;
                    savesLastBackupName = lastName;
                    savesList = all;
                    SetSavesCard(line, sub);
                    if (optionsPage == "detsaves" && openPanel == "options") SavesRowsInPlace();   // (0.86: the highlight stays where you've moved it)
                });
                }
            });
        }

        private void SetSavesCard(string line, string sub)
        {
            DetSavesLine = line;
            DetSavesSub = sub;
            foreach (string n in new[] { "DetSavesLine", "DetSavesSub", "ShowDetSaves", "DetIntSaves" }) Notify(n);
        }

        /// <summary>Backs up (or restores) a game's saves off the UI thread; done() gets a line to show.</summary>
        private void LudusaviRun(Game g, bool restore, string backupName, Action<string> done)
        {
            KippixShared.SafeWork.Queue(_ =>
            {
                string result;
                try
                {
                    var s = ReadLudusaviSetup();
                    string title = LudusaviTitle(s, g);
                    if (title == null) result = Loc("LOC_KX_SavesNoProgram", "Ludusavi (the program) wasn't found");
                    else if (title.Length == 0) result = Loc("LOC_KX_SavesUnknown", "Ludusavi doesn't know where this game saves");
                    else
                    {
                        int exit;
                        string outText = restore
                            ? RunLudusavi(s, "restore --force", (backupName != null ? "--backup " + Quote(backupName) + " " : "") + "-- " + Quote(title), 600000, out exit)
                            : RunLudusavi(s, "backup --force", "-- " + Quote(title), 600000, out exit);
                        var root = JsonObj(outText);
                        object failed = Cfg(root, "errors", "someGamesFailed");
                        object files = Cfg(root, "games", title, "files");
                        int n = files is Dictionary<string, object> ? ((Dictionary<string, object>)files).Count : 0;
                        if (outText == null) result = Loc("LOC_KX_SavesError", "Couldn't ask Ludusavi");
                        else if ((failed is bool && (bool)failed) || exit != 0) result = restore ? Loc("LOC_KX_RestoreFailed", "Some files couldn't be restored") : Loc("LOC_KX_BackupFailed", "Some files couldn't be backed up");
                        else result = restore ? string.Format(Loc("LOC_KX_RestoreDone", "Restored ({0} files)"), n) : string.Format(Loc("LOC_KX_BackupDone", "Backed up ({0} files)"), n);
                    }
                }
                catch (Exception ex) { result = Loc("LOC_KX_SavesError", "Couldn't ask Ludusavi"); logger.Warn("Kippix: Ludusavi: " + ex.Message); }
                OnUi(() => done(result));
            });
        }

        private bool savesPicking;   // (0.99.68) the saves page is showing the list of backups to restore from

        /// <summary>B on the list of backups: back to the saves page, on Restore a backup. True: handled.</summary>
        private bool SavesPickBack()
        {
            if (!savesPicking || optionsPage != "detsaves") return false;
            savesPicking = false;
            BoardRowsAgain(BuildSavesRows, "restorepick");
            return true;
        }

        /// <summary>Restore a backup: every backup of the game, newest first; A on one restores it (after asking).</summary>
        private void BuildRestorePickRows(ObservableCollection<PickRow> rows, Game game)
        {
            rows.Add(new PickRow { IsHeader = true, Text = string.Format(Loc("LOC_KX_SecRestoreFor", "RESTORE A BACKUP OF {0}"), (game.Name ?? "").ToUpperInvariant()) });
            if (savesBusy || !string.IsNullOrEmpty(savesStatus))
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "pickstate", Text = savesBusy ? Loc("LOC_KX_Restoring", "Restoring…") : savesStatus });
            if (savesList.Count == 0) { rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "picknone", Text = Loc("LOC_KX_SavesNone", "No backups yet") }); return; }
            for (int i = 0; i < savesList.Count; i++)
            {
                var b = savesList[i];
                string when = WhenText(b.When);
                var bits = new List<string>();
                if (i == 0) bits.Add(Loc("LOC_KX_SavesNewest", "The newest"));
                if (i == savesList.Count - 1 && savesList.Count > 1) bits.Add(Loc("LOC_KX_SavesOldest", "The oldest"));
                if (!string.IsNullOrWhiteSpace(b.Comment)) bits.Add(b.Comment.Trim());
                if (b.Locked) bits.Add(Loc("LOC_KX_SavesLocked", "Kept (locked in Ludusavi)"));
                string name = b.Name;
                rows.Add(new PickRow
                {
                    Key = "restore:" + (name ?? i.ToString()), Icon = "download",
                    Text = b.When.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture),
                    Sub = string.Join("   ·   ", new[] { WhenAgo(b.When) }.Concat(bits)),
                    Command = new RelayCommand(_ =>
                    {
                        if (savesBusy) { PlaySound("back"); return; }
                        Confirm(Loc("LOC_KX_RestoreTitle", "Restore these saves?"),
                                string.Format(Loc("LOC_KX_RestoreText", "{0}'s save files are put back as they were on {1}. Any progress since then is replaced."), game.Name, when),
                                Loc("LOC_KX_Restore", "Restore"), () =>
                                {
                                    savesBusy = true;
                                    savesStatus = Loc("LOC_KX_Restoring", "Restoring…");
                                    SavesRowsInPlace();
                                    LudusaviRun(game, true, name, result =>
                                    {
                                        savesBusy = false;
                                        savesStatus = result;
                                        FillSavesCard(SelectedGameNow());
                                        if (optionsPage == "detsaves") SavesRowsInPlace();
                                    });
                                });
                    })
                });
            }
        }

        private void BuildSavesRows()
        {
            var rows = SavesRows;
            rows.Clear();
            var g = savesGame ?? SelectedGameNow();
            if (g == null) return;
            var game = g;
            if (savesPicking) { BuildRestorePickRows(rows, game); return; }   // (0.99.68) Restore a backup › the backups
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecThisGame", "THIS GAME") });
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "savesstate", Text = string.IsNullOrEmpty(DetSavesLine) ? Loc("LOC_KX_SavesChecking", "Checking…") : DetSavesLine, Sub = savesStatus ?? "" });
            rows.Add(new PickRow
            {
                Key = "backupnow", Icon = "upload", Text = savesBusy ? Loc("LOC_KX_SavesWorking", "Working…") : Loc("LOC_KX_BackupNow", "Back up now"),
                Sub = Loc("LOC_KX_BackupNowSub", "Copies this game's saves with Ludusavi"),
                Command = new RelayCommand(_ =>
                {
                    if (savesBusy) { PlaySound("back"); return; }
                    savesBusy = true;
                    savesStatus = Loc("LOC_KX_BackingUp", "Backing up…");
                    PlaySound("select");
                    BoardRowsAgain(BuildSavesRows, "backupnow");
                    LudusaviRun(game, false, null, result =>
                    {
                        savesBusy = false;
                        savesStatus = result;
                        FillSavesCard(SelectedGameNow());
                        if (optionsPage == "detsaves") SavesRowsInPlace();
                    });
                })
            });
            // (0.99.68) one row: Restore a backup; A on it lists every backup Ludusavi has of the game
            if (savesList.Count > 0)
                rows.Add(new PickRow
                {
                    Key = "restorepick", Icon = "download", Text = Loc("LOC_KX_RestoreABackup", "Restore a backup"),
                    Sub = savesList.Count == 1 ? string.Format(Loc("LOC_KX_RestorePickOne", "1 backup, from {0}"), WhenText(savesList[0].When))
                                               : string.Format(Loc("LOC_KX_RestorePickN", "{0} backups, the newest from {1}"), savesList.Count, WhenText(savesList[0].When)),
                    Command = new RelayCommand(_ =>
                    {
                        if (savesBusy) { PlaySound("back"); return; }
                        PlaySound("select");
                        savesPicking = true;
                        savesStatus = null;
                        string first = savesList.Count > 0 ? "restore:" + (savesList[0].Name ?? "0") : null;
                        BoardRowsAgain(BuildSavesRows, first);
                    })
                });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecAutoBackups", "AUTOMATIC BACKUPS") });
            rows.Add(BoardSwitch("play", "savesbefore", Loc("LOC_KX_SavesBefore", "Back up when a game starts"), settings.SavesBeforeLaunch,
                                 Loc("LOC_KX_SavesBeforeOn", "Its saves are copied as it launches"), Loc("LOC_KX_Off", "Off"),
                                 () => { settings.SavesBeforeLaunch = !settings.SavesBeforeLaunch; SaveSettings(); PlaySound("move"); BoardRowsAgain(BuildSavesRows, "savesbefore"); }));
            rows.Add(BoardSwitch("leave", "savesafter", Loc("LOC_KX_SavesAfter", "Back up when a game closes"), settings.SavesAfterClose,
                                 Loc("LOC_KX_SavesAfterOn", "Its saves are copied when you stop playing, without asking"), Loc("LOC_KX_Off", "Off"),
                                 () => { settings.SavesAfterClose = !settings.SavesAfterClose; SaveSettings(); PlaySound("move"); LudusaviOwnAfterPlayOff("switched on"); BoardRowsAgain(BuildSavesRows, "savesafter"); }));
            try
            {
                if (ReadLudusaviSetup().OwnAfterPlay)
                    rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "savesown", Text = Loc("LOC_KX_SavesOwn", "The Ludusavi add-on also backs up after playing (its own setting, in desktop mode)"),
                                           Sub = settings.SavesAfterClose ? Loc("LOC_KX_SavesOwnSub", "Turn off \"Back up save data … when a game stops\" in Ludusavi's settings so it doesn't ask as well") : "" });
            }
            catch (Exception) { }
        }

        /// <summary>
        /// (0.86) "Back up when a game closes" is Kippix's job: the Ludusavi add-on's own after-play backup (and its "Back up save
        /// data for …?" question) is switched off in its settings, so there's one backup and no question. Done when the switch
        /// is turned on, and once at start-up while it's on. Its other settings are left as they are.
        /// </summary>
        internal void LudusaviOwnAfterPlayOff(string why)
        {
            if (!settings.SavesAfterClose || !LudusaviInstalled) return;
            try
            {
                if (!ReadLudusaviSetup().OwnAfterPlay) return;
                var plugin = api.Addons.Plugins.FirstOrDefault(pl => pl != null && pl.Id == LudusaviId);
                if (plugin == null) return;
                object cfg = null;
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in plugin.GetType().GetFields(all))
                {
                    if (f.FieldType.GetProperty("DoBackupOnGameStopped") == null) continue;
                    cfg = f.GetValue(plugin);
                    if (cfg != null) break;
                }
                if (cfg == null)
                    foreach (var pr in plugin.GetType().GetProperties(all))
                    {
                        if (pr.GetIndexParameters().Length > 0 || pr.PropertyType.GetProperty("DoBackupOnGameStopped") == null) continue;
                        try { cfg = pr.GetValue(plugin, null); } catch (Exception) { }
                        if (cfg != null) break;
                    }
                if (cfg == null) { logger.Info("Kippix: saves: couldn't find the Ludusavi add-on's settings to turn its own after-play backup off"); return; }
                var prop = cfg.GetType().GetProperty("DoBackupOnGameStopped");
                if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanWrite) return;
                prop.SetValue(cfg, false, null);
                var save = typeof(Playnite.SDK.Plugins.Plugin).GetMethods().FirstOrDefault(m => m.Name == "SavePluginSettings" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (save != null) save.MakeGenericMethod(cfg.GetType()).Invoke(plugin, new[] { cfg });
                logger.Info("Kippix: saves: the Ludusavi add-on's own after-play backup (and its question) turned off: Kippix backs up when a game closes (" + why + ")");
            }
            catch (Exception ex) { logger.Warn("Kippix: saves: Ludusavi's own after-play backup: " + ex.Message); }
        }

        /// <summary>A game has closed (from the plugin's OnGameStopped).</summary>
        public void GameStoppedForSaves(Game game)
        {
            if (game == null) return;
            OnUi(() => AutoSavesBackup(game, false));
        }

        /// <summary>Settings › … › Back up when a game starts / closes.</summary>
        private void AutoSavesBackup(Game g, bool starting)
        {
            if (g == null || !LudusaviInstalled) return;
            if (starting ? !settings.SavesBeforeLaunch : !settings.SavesAfterClose) return;
            var game = g;
            LudusaviRun(game, false, null, result =>
            {
                if (!starting)
                    try { ShowNoticeKept(Loc("LOC_KX_SavesNotice", "SAVE BACKUP"), game.Name, result, "", null); } catch (Exception) { }
                if (detailsOpen) FillSavesCard(SelectedGameNow());
            });
        }

        // =====================================================================
        // Settings › General settings › Other add-ons
        // =====================================================================

        public bool IsOptionsAddons { get { return optionsPage == "addons"; } }
        private ObservableCollection<PickRow> addonRows;
        public ObservableCollection<PickRow> AddonRows { get { return addonRows ?? (addonRows = new ObservableCollection<PickRow>()); } }

        private bool AnyGameHas(Func<Game, bool> test)
        {
            try { return api.Database.Games.Any(g => g != null && test(g)); } catch (Exception) { return false; }
        }

        private void BuildAddonRows()
        {
            var rows = AddonRows;
            rows.Clear();
            int found = 0;
            Action<string, string, string, bool, string, Action> sw = (icon, key, text, on, onSub, flip) =>
                rows.Add(BoardSwitch(icon, key, text, on, onSub, Loc("LOC_KX_Off", "Off"), () => { flip(); SaveSettings(); PlaySound("move"); NotifyAddonSettings(); BoardRowsAgain(BuildAddonRows, key); }));

            if (LudusaviInstalled)
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "LUDUSAVI" });
                sw("upload", "addsaves", Loc("LOC_KX_SavesCardRow", "Save backups card on the game page"), settings.DetSecSaves, Loc("LOC_KX_SavesCardRowOn", "Last backup, back up now, restore"), () => settings.DetSecSaves = !settings.DetSecSaves);
                sw("play", "addsavesbefore", Loc("LOC_KX_SavesBefore", "Back up when a game starts"), settings.SavesBeforeLaunch, Loc("LOC_KX_SavesBeforeOn", "Its saves are copied as it launches"), () => settings.SavesBeforeLaunch = !settings.SavesBeforeLaunch);
                sw("leave", "addsavesafter", Loc("LOC_KX_SavesAfter", "Back up when a game closes"), settings.SavesAfterClose, Loc("LOC_KX_SavesAfterOn", "Its saves are copied when you stop playing, without asking"), () => { settings.SavesAfterClose = !settings.SavesAfterClose; LudusaviOwnAfterPlayOff("switched on"); });
            }
            if (UpsInstalled)
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "UNIPLAYSONG" });
                rows.Add(new PickRow { Key = "addups", Icon = "headphones", Text = Loc("LOC_KX_UpsOptions", "UniPlaySong"), Sub = MusicUps ? Loc("LOC_KX_UpsAddonOn", "The music: each game's own songs. What it plays and when") : Loc("LOC_KX_UpsAddonOff", "Not the music now (Settings › Audio › Music)"), Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("ups"); }) });
            }
            if (BcInstalled)
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "BACKGROUNDCHANGER" });
                rows.Add(new PickRow { Key = "addbgc", Icon = "picture", Text = "BackgroundChanger", Sub = Loc("LOC_KX_BcAddonSub", "More backgrounds and covers per game, videos too: on the home screen and game page"), Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("bgc"); }) });
            }
            if (PluginInstalled(GamePassBrowserId))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "GAME PASS CATALOG BROWSER" });
                sw("store", "addgamepass", Loc("LOC_KX_GamePassRow", "Game Pass row in the Store"), settings.UseGamePass,
                   File.Exists(GamePassCacheFile) ? Loc("LOC_KX_GamePassRowOn", "Games on PC Game Pass, on the Store's first page") : Loc("LOC_KX_GamePassNoCache", "On (its catalog shows once the add-on has loaded it)"),
                   () => { settings.UseGamePass = !settings.UseGamePass; storeRowsAt = DateTime.MinValue; });
            }
            if (ScriptInstalled(NexusScriptId) || AnyGameHas(g => (g.Links != null && g.Links.Any(l => l != null && l.Url != null && l.Url.IndexOf("nexusmods.com", StringComparison.OrdinalIgnoreCase) >= 0))
                                                              || (g.Features != null && g.Features.Any(f => f != null && f.Name == "Nexus Mods"))))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "NEXUS MODS CHECKER" });
                // (r15) the Nexus Mods button's switch is on the game page's own settings now (Buttons and extras)
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "addnexus", Text = Loc("LOC_KX_NexusInfo", "Noticed: games it found on Nexus Mods get a Nexus Mods button on the game page"), Sub = Loc("LOC_KX_NexusInfoSub", "Shown or hidden in Settings › Game page › Buttons and extras") });
            }
            if (ScriptInstalled(EngineScriptId) || AnyGameHas(g => g.Tags != null && g.Tags.Any(t => t != null && t.Name != null && t.Name.StartsWith("[Engine]"))))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "GAME ENGINE CHECKER" });
                sw("gear", "addengine", Loc("LOC_KX_EngineRow", "Engine in the game page's info"), settings.ShowEngine, Loc("LOC_KX_EngineRowOn", "From its [Engine] tag"), () => settings.ShowEngine = !settings.ShowEngine);
            }
            if (ScriptInstalled(FreestyleScriptId) || AnyGameHas(g => g.Features != null && g.Features.Any(f => f != null && f.Name == "NVIDIA Freestyle")))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "NVIDIA FREESTYLE CHECKER" });
                sw("tag", "addfreestyle", Loc("LOC_KX_FreestyleRow", "Freestyle badge on the game page"), settings.ShowFreestyle, Loc("LOC_KX_FreestyleRowOn", "Games that work with NVIDIA's Freestyle filters"), () => settings.ShowFreestyle = !settings.ShowFreestyle);
            }
            if (PluginInstalled(SplashScreenId))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "SPLASH SCREEN" });
                sw("gallery", "addsplash", Loc("LOC_KX_SplashRow", "Let Splash Screen show when a game starts"), settings.SplashStandAside,
                   Loc("LOC_KX_SplashRowOn", "Kippix's launch screen stays plain so the two don't clash"),
                   () => settings.SplashStandAside = !settings.SplashStandAside);
            }
            if (ScriptInstalled(SteamTrailersScriptId))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "STEAM TRAILERS" });
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "addtrailers", Text = Loc("LOC_KX_SteamTrailersInfo", "Noticed: the game page's trailer is Kippix's own player, so it isn't added twice (Steam Trailers' menu items still work)") });
            }
            if (PluginInstalled(WebExplorerId))
            {
                found++;
                rows.Add(new PickRow { IsHeader = true, Text = "WEB EXPLORER" });
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "addweb", Text = Loc("LOC_KX_WebExplorerInfo", "Noticed: web pages open in the built-in browser, so Web Explorer isn't added to the screens as well") });
            }
            if (found == 0)
                rows.Add(new PickRow
                {
                    IsInfo = true, Icon = "info", Key = "addnone",
                    Text = Loc("LOC_KX_AddonsNone", "None of the supported add-ons is installed"),
                    Sub = "Ludusavi, UniPlaySong, BackgroundChanger, Game Pass Catalog Browser, Nexus Mods checker, Game Engine Checker, NVIDIA Freestyle checker, Splash Screen, Steam Trailers, Web Explorer"
                });
        }

        private void NotifyAddonSettings()
        {
            foreach (string n in new[] { "HasDetEngine", "ShowDetFreestyle", "ShowDetNexus", "ShowDetSaves", "ShowDetMore" }) Notify(n);
            try { ApplyDetPlacement(); } catch (Exception) { }
        }

        private void AddonOptionsOpened(string page)
        {
            Notify("IsOptionsAddons");
            Notify("IsOptionsDetSaves");
            if (page == "addons") BuildAddonRows();
            if (page == "detsaves") { savesStatus = null; savesPicking = false; BuildSavesRows(); }
        }
    }
}
