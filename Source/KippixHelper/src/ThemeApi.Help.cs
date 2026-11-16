using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>Options › Help: GitHub, checking for updates, a support log, extra logging, donate, who made it.</summary>
    public partial class ThemeApi
    {
        // (1.0.0) the public repository: the GitHub link, update checks (its latest release) and the Community gallery
        private const string GitHubUrl = "https://github.com/Kipping0/Kippix";
        private const string GitHubRepo = "Kipping0/Kippix";
        private const string DonateUrl = "";   // (none: the Donate row is hidden)

        public ICommand HelpGitHubCommand { get; private set; }
        public ICommand HelpUpdatesCommand { get; private set; }
        public ICommand HelpLogCommand { get; private set; }
        public ICommand HelpDonateCommand { get; private set; }
        public ICommand WeatherRefreshCommand { get; private set; }
        private string helpStatus = "";
        private bool checkingUpdates, weatherRefreshing;

        private void InitHelpCommands()
        {
            HelpGitHubCommand = new RelayCommand(_ => OpenHelpLink(GitHubUrl));
            HelpDonateCommand = new RelayCommand(_ => OpenHelpLink(DonateUrl));
            HelpUpdatesCommand = new RelayCommand(_ => CheckForUpdates());
            HelpLogCommand = new RelayCommand(_ => SaveSupportLog());
            WeatherRefreshCommand = new RelayCommand(_ => RefreshWeatherNow());
        }

        public bool IsOptionsHelp { get { return optionsPage == "help"; } }
        public string HelpStatus { get { return helpStatus; } }
        public bool HasHelpStatus { get { return helpStatus.Length > 0; } }

        private void SetHelpStatus(string s)
        {
            helpStatus = s ?? "";
            Notify("HelpStatus");
            Notify("HasHelpStatus");
        }

        /// <summary>The in-game overlay on / off (takes effect the next time Playnite starts).</summary>
        public bool OverlayEnabled
        {
            get { return settings.OverlayEnabled; }
            set { if (settings.OverlayEnabled == value) return; settings.OverlayEnabled = value; SaveSettings(); Notify("OverlayEnabled"); Notify("OverlayStateText"); }
        }

        /// <summary>The overlay's "When the overlay opens" (its config file): Pause the game / Let it keep running / Automatic.</summary>
        public string OverlayWhenOpenedText
        {
            get
            {
                int m = 0;
                try { var c = OverlayConfigNow(); if (c != null) m = c.WhenOpened; } catch (Exception) { }
                switch (m)
                {
                    case 1: return Loc("LOC_KX_OverlayKeepRunning", "Let it keep running");
                    case 2: return Loc("LOC_KX_OverlayAutoRun", "Automatic");
                }
                return Loc("LOC_KX_OverlayPauseGame", "Pause the game");
            }
        }

        private ICommand cycleOverlayWhenOpened;
        public ICommand CycleOverlayWhenOpenedCommand
        {
            get { if (cycleOverlayWhenOpened == null) cycleOverlayWhenOpened = new RelayCommand(_ => CycleOverlayWhenOpened()); return cycleOverlayWhenOpened; }
        }

        private void CycleOverlayWhenOpened()
        {
            try
            {
                var ov = plugin.Overlay;
                if (ov != null)
                {
                    var c = ov.ReloadConfig();
                    c.WhenOpened = (c.WhenOpened + 1) % KippixOverlay.OverlayConfig.WhenOpenedNames.Length;
                    ov.SaveConfig();
                }
                else
                {
                    string path = Path.Combine(api.Paths.ExtensionsDataPath, KippixOverlay.OverlayPlugin.PluginId.ToString(), "config.json");
                    var c = KippixOverlay.OverlayConfig.TryLoad(path);
                    if (c == null) { PlaySound("back"); return; }   // (being written just then: nothing saved over it)
                    c.WhenOpened = (c.WhenOpened + 1) % KippixOverlay.OverlayConfig.WhenOpenedNames.Length;
                    c.Clamp();
                    c.Save(path);
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: overlay setting: " + ex.Message); }
            PlaySound("move");
            Notify("OverlayWhenOpenedText");
        }

        /// <summary>Under the switch: running, or what happens at the next start.</summary>
        public string OverlayStateText
        {
            get
            {
                var h = plugin as HelperPlugin;
                if (h != null && h.OldOverlayInstalled) return Loc("LOC_KX_OverlayOld", "The separate KippixOverlay add-on is still installed: uninstall it and restart Playnite to use this one.");
                bool running = h != null && h.OverlayRunning;
                if (running == settings.OverlayEnabled)
                    return running ? Loc("LOC_KX_OverlayRunning", "On.") : Loc("LOC_KX_OverlayOff", "Off.");
                return settings.OverlayEnabled ? Loc("LOC_KX_OverlayRestartOn", "Turns on the next time Playnite starts.") : Loc("LOC_KX_OverlayRestartOff", "Turns off the next time Playnite starts.");
            }
        }

        /// <summary>The separate KippixOverlay add-on is still installed (the built-in overlay stays off).</summary>
        private bool OldOverlayInstalled { get { var h = plugin as HelperPlugin; return h != null && h.OldOverlayInstalled; } }

        public bool DebugLogging
        {
            get { return settings.DebugLogging; }
            set { if (settings.DebugLogging == value) return; settings.DebugLogging = value; SaveSettings(); Notify("DebugLogging"); }
        }

        /// <summary>Extra lines in Playnite's extensions.log, only with Help › Detailed logging on.</summary>
        private void Dbg(string text)
        {
            if (settings.DebugLogging) logger.Info("Kippix [debug]: " + text);
        }

        public string HelpVersionText
        {
            get { return string.Format(Loc("LOC_KX_HelpVersions", "Theme {0} · Helper {1}"), ThemeVersion() ?? "?", HelperVersion() ?? "?"); }
        }

        private string ThemeVersion()
        {
            try { return YamlVersion(Path.Combine(ThemeDir, "theme.yaml")); } catch (Exception) { return null; }
        }

        private static string HelperVersion()
        {
            try { return YamlVersion(Path.Combine(Path.GetDirectoryName(typeof(ThemeApi).Assembly.Location), "extension.yaml")); } catch (Exception) { return null; }
        }

        private static string YamlVersion(string file)
        {
            if (!File.Exists(file)) return null;
            var m = Regex.Match(File.ReadAllText(file), @"(?m)^Version:\s*([0-9][0-9A-Za-z.\-]*)");
            return m.Success ? m.Groups[1].Value : null;
        }


        // ---------------- links ----------------

        private void OpenHelpLink(string url)
        {
            if (string.IsNullOrEmpty(url)) { PlaySound("back"); SetHelpStatus(Loc("LOC_KX_HelpSoon", "That link is coming soon.")); return; }
            PlaySound("select");
            OpenLink(url);
        }

        // ---------------- updates ----------------

        private bool updateCheckedOnce;
        private GitHubApi.Release updateRelease;   // a newer release, while there is one
        private bool updateInstalling;

        public bool HasHelpUpdate { get { return updateRelease != null; } }
        public string HelpUpdateTitle { get { return updateRelease != null ? string.Format(Loc("LOC_KX_HelpInstallUpdate", "Install version {0}"), updateRelease.Version) : ""; } }
        public string HelpUpdateSub
        {
            get
            {
                return updateInstalling ? Loc("LOC_KX_HelpInstalling", "Downloading…")
                     : Loc("LOC_KX_HelpInstallUpdateSub", "Downloads the theme and the helper from GitHub, then Playnite restarts to finish (your settings stay as they are)");
            }
        }
        public ICommand HelpInstallUpdateCommand { get { return helpInstallCmd ?? (helpInstallCmd = new RelayCommand(_ => InstallUpdate())); } }
        private ICommand helpInstallCmd;

        private void NotifyUpdate()
        {
            Notify("HasHelpUpdate"); Notify("HelpUpdateTitle"); Notify("HelpUpdateSub");
        }

        /// <summary>(1.0.1) Opening Help looks for a new version by itself, once a session.</summary>
        private void CheckForUpdatesOnce()
        {
            if (updateCheckedOnce || string.IsNullOrEmpty(GitHubRepo)) return;
            updateCheckedOnce = true;
            CheckForUpdates();
        }

        private void CheckForUpdates()
        {
            if (checkingUpdates || updateInstalling) return;
            if (string.IsNullOrEmpty(GitHubRepo)) { SetHelpStatus(Loc("LOC_KX_HelpNoRepo", "Update checks start once Kippix is on GitHub.")); return; }
            checkingUpdates = true;
            SetHelpStatus(Loc("LOC_KX_HelpChecking", "Checking for updates…"));
            string theme = ThemeVersion(), helper = HelperVersion();
            var t = new Thread(() =>
            {
                GitHubApi.Release rel = null;
                bool failed = false;
                try { rel = GitHubApi.Latest(GitHubRepo); }
                catch (Exception ex) { failed = true; logger.Warn("Kippix: update check: " + ex.Message); }
                OnUi(() =>
                {
                    checkingUpdates = false;
                    if (failed || rel == null || string.IsNullOrEmpty(rel.Version)) { SetHelpStatus(Loc("LOC_KX_HelpCheckFailed", "Couldn't check just now. Try again in a bit.")); return; }
                    bool newer = Newer(rel.Version, theme) || Newer(rel.Version, helper);
                    updateRelease = newer ? rel : null;
                    NotifyUpdate();
                    SetHelpStatus(newer ? string.Format(Loc("LOC_KX_HelpUpdate2", "Version {0} is out (you have {1})."), rel.Version, theme ?? helper ?? "?")
                                        : string.Format(Loc("LOC_KX_HelpUpToDate", "You have the latest version ({0})."), theme ?? helper ?? rel.Version));
                    if (newer) ui.BeginInvoke(new Action(() => UiTools.FocusByNameIfVisible("KX_HelpInstallUpdate")), DispatcherPriority.ContextIdle);
                });
            }) { IsBackground = true, Name = "Kippix update check" };
            t.Start();
        }

        /// <summary>
        /// (1.0.1) Install the new version from here: the release's theme (.pthm) and helper (.pext) are downloaded into Playnite's
        /// temp folder, handed to Playnite's own install queue (as installing them from a file does), and Playnite restarts to put
        /// them in. A PC that runs Kippix from a development folder (the theme folder is a link) is left to its Install.bat.
        /// </summary>
        private void InstallUpdate()
        {
            var rel = updateRelease;
            if (rel == null || updateInstalling) { PlaySound("back"); return; }
            string pthm = rel.Assets.Keys.FirstOrDefault(k => k.EndsWith(".pthm", StringComparison.OrdinalIgnoreCase));
            string pext = rel.Assets.Keys.FirstOrDefault(k => k.EndsWith(".pext", StringComparison.OrdinalIgnoreCase));
            if (pthm == null || pext == null)
            {
                PlaySound("back");
                SetHelpStatus(string.Format(Loc("LOC_KX_HelpUpdateNoFiles", "Version {0} doesn't have both files to install yet. Open GitHub to get it."), rel.Version));
                return;
            }
            try
            {
                var di = new DirectoryInfo(ThemeDir);
                if (di.Exists && (di.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    PlaySound("back");
                    SetHelpStatus(Loc("LOC_KX_HelpUpdateDev", "This PC runs Kippix from a development folder, so it's updated with Install.bat, not from here."));
                    return;
                }
            }
            catch (Exception) { }
            PlaySound("select");
            updateInstalling = true;
            NotifyUpdate();
            SetHelpStatus(string.Format(Loc("LOC_KX_HelpDownloading", "Downloading version {0}…"), rel.Version));
            string temp = Path.Combine(Path.GetTempPath(), "Playnite");
            string helperDir = null;
            try { helperDir = Path.GetDirectoryName(typeof(ThemeApi).Assembly.Location); } catch (Exception) { }
            string config = api.Paths.ConfigurationPath;
            var t = new Thread(() =>
            {
                string error = null, themeFile = null, helperFile = null;
                try
                {
                    Directory.CreateDirectory(temp);
                    themeFile = Path.Combine(temp, "Kippix-" + rel.Version + ".pthm");
                    helperFile = Path.Combine(temp, "KippixHelper-" + rel.Version + ".pext");
                    GitHubApi.Download(rel.Assets[pext], helperFile);
                    GitHubApi.Download(rel.Assets[pthm], themeFile);
                    foreach (string f in new[] { helperFile, themeFile })
                    {
                        var b = new byte[2];
                        using (var fs = File.OpenRead(f)) fs.Read(b, 0, 2);
                        if (b[0] != (byte)'P' || b[1] != (byte)'K' || new FileInfo(f).Length < 1024) throw new InvalidDataException(Path.GetFileName(f) + " isn't a Playnite package");
                    }
                }
                catch (Exception ex) { error = ex.Message; logger.Warn("Kippix: update download: " + ex.Message); }
                OnUi(() =>
                {
                    if (error != null)
                    {
                        updateInstalling = false;
                        NotifyUpdate();
                        SetHelpStatus(string.Format(Loc("LOC_KX_HelpDownloadFailed", "The download didn't work ({0}). Try again in a bit, or get it from GitHub."), error));
                        return;
                    }
                    try
                    {
                        // the helper in a folder other than the one Playnite installs it to (an older manual install): that one goes
                        string installTo = Path.Combine(config, "Extensions", "KippixHelper_3f6c2d1e-8a47-4b9e-b1d5-7e2a9c4f0b18");
                        var queue = new List<KeyValuePair<string, int>>();
                        if (helperDir != null && Directory.Exists(helperDir) && !string.Equals(Path.GetFullPath(helperDir).TrimEnd('\\'), Path.GetFullPath(installTo).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                            && File.Exists(Path.Combine(helperDir, "extension.yaml")))
                            queue.Add(new KeyValuePair<string, int>(helperDir, 1));
                        queue.Add(new KeyValuePair<string, int>(helperFile, 0));
                        queue.Add(new KeyValuePair<string, int>(themeFile, 0));
                        QueuePlayniteInstalls(queue, config);
                        logger.Info("Kippix: update " + rel.Version + " queued for Playnite to install; restarting");
                        SetHelpStatus(string.Format(Loc("LOC_KX_HelpRestarting", "Version {0} is ready. Playnite is restarting to put it in…"), rel.Version));
                        SaveSettings();
                        FlushSettings(true);
                        var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(2) };
                        timer.Tick += (s, e) => { timer.Stop(); RestartPlaynite(); };
                        timer.Start();
                    }
                    catch (Exception ex)
                    {
                        updateInstalling = false;
                        NotifyUpdate();
                        logger.Error(ex, "Kippix: update install");
                        SetHelpStatus(string.Format(Loc("LOC_KX_HelpInstallFailed", "It couldn't be installed from here ({0}). The files are in {1}: drag them onto Playnite's window."), ex.Message, temp));
                    }
                });
            }) { IsBackground = true, Name = "Kippix update" };
            t.Start();
        }

        /// <summary>Playnite's own install queue (what installing from a file uses): Playnite's own code when it can be reached,
        /// otherwise its queue file (extinstalls.json: 0 install, 1 uninstall), which Playnite works through when it starts.</summary>
        private static void QueuePlayniteInstalls(List<KeyValuePair<string, int>> items, string config)
        {
            Type installer = null;
            foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                installer = a.GetType("Playnite.Plugins.ExtensionInstaller", false);
                if (installer != null) break;
            }
            var install = installer != null ? installer.GetMethod("QueuePackageInstall", new[] { typeof(string) }) : null;
            var uninstall = installer != null ? installer.GetMethod("QueueExtensionUninstall", new[] { typeof(string) }) : null;
            if (install != null && uninstall != null)
            {
                foreach (var it in items) (it.Value == 1 ? uninstall : install).Invoke(null, new object[] { it.Key });
                return;
            }
            string file = Path.Combine(config, "extinstalls.json");
            var js = new JavaScriptSerializer();
            var list = new List<Dictionary<string, object>>();
            try { if (File.Exists(file)) list = js.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(file)) ?? list; } catch (Exception) { }
            foreach (var it in items) list.Add(new Dictionary<string, object> { { "InstallType", it.Value }, { "Path", it.Key } });
            File.WriteAllText(file, js.Serialize(list));
        }

        /// <summary>Playnite's own restart (it waits for this one to close before installing what's queued).</summary>
        private void RestartPlaynite()
        {
            try
            {
                foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Playnite.PlayniteApplication", false);
                    if (t == null) continue;
                    object app = t.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null, null);
                    var restart = t.GetMethod("Restart", new[] { typeof(bool) });
                    if (app != null && restart != null) { restart.Invoke(app, new object[] { true }); return; }
                }
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: Playnite's restart failed"); }
            updateInstalling = false;
            NotifyUpdate();
            SetHelpStatus(Loc("LOC_KX_HelpRestartYourself", "Ready: restart Playnite (exit it and start it again) to finish the update."));
        }

        private static bool Newer(string latest, string have)
        {
            Version a, b;
            if (string.IsNullOrEmpty(have) || !Version.TryParse(Regex.Replace(latest, @"[^0-9.].*$", ""), out a) || !Version.TryParse(Regex.Replace(have, @"[^0-9.].*$", ""), out b)) return false;
            return a > b;
        }

        // ---------------- support log ----------------

        /// <summary>
        /// Documents › Kippix Logs › (date): Playnite's logs (the end of them), our settings without keys or anything personal,
        /// and what's installed. For sending when something goes wrong.
        /// </summary>
        private void SaveSupportLog()
        {
            PlaySound("select");
            SetHelpStatus(Loc("LOC_KX_HelpSaving", "Saving…"));
            string theme = ThemeVersion(), helper = HelperVersion();
            string playnite = "?", mode = IsFullscreen ? "Fullscreen" : "Desktop";
            try { playnite = api.ApplicationInfo.ApplicationVersion.ToString(); } catch (Exception) { }
            var addons = new List<string>();
            try { addons = api.Addons.Plugins.Select(p => p.GetType().Name + " (" + p.Id + ")").ToList(); } catch (Exception) { }
            var screen = SystemParameters.PrimaryScreenWidth + " x " + SystemParameters.PrimaryScreenHeight;
            string json;
            try { json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(settings); } catch (Exception) { json = "{}"; }
            string config = api.Paths.ConfigurationPath;
            var t = new Thread(() =>
            {
                string dir = null;
                try
                {
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kippix Logs", DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(dir);
                    var info = new StringBuilder();
                    info.AppendLine("Kippix support log, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    info.AppendLine("Theme " + (theme ?? "?") + ", helper " + (helper ?? "?") + ", Playnite " + playnite + " (" + mode + ")");
                    info.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " 64-bit" : "") + ", .NET " + Environment.Version);
                    info.AppendLine("Screen " + screen + ", culture " + CultureInfo.CurrentCulture.Name);
                    info.AppendLine();
                    info.AppendLine("Add-ons:");
                    foreach (var a in addons) info.AppendLine("  " + a);
                    File.WriteAllText(Path.Combine(dir, "info.txt"), info.ToString());
                    File.WriteAllText(Path.Combine(dir, "settings (no keys).json"), CleanSettings(json));
                    foreach (string log in new[] { "playnite.log", "extensions.log" })
                        CopyTail(Path.Combine(config, log), Path.Combine(dir, log), 3 * 1024 * 1024);
                }
                catch (Exception ex) { logger.Warn("Kippix: support log: " + ex.Message); dir = null; }
                OnUi(() => SetHelpStatus(dir != null ? string.Format(Loc("LOC_KX_HelpSaved", "Saved in Documents › Kippix Logs › {0}. Zip that folder and send it."), Path.GetFileName(dir))
                                                     : Loc("LOC_KX_HelpSaveFailed", "Couldn't save the log.")));
            }) { IsBackground = true, Name = "Kippix support log" };
            t.Start();
        }

        /// <summary>No keys, no wishlist / history, no home town: just how it's set up.</summary>
        private static string CleanSettings(string json)
        {
            return KipDoctor.CleanSettings(json);   // (r12: every key, token, secret, PIN and personal thing, however deep)
        }

        private static void CopyTail(string from, string to, long maxBytes)
        {
            if (!File.Exists(from)) return;
            using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = new FileStream(to, FileMode.Create, FileAccess.Write))
            {
                if (src.Length > maxBytes) src.Seek(-maxBytes, SeekOrigin.End);
                src.CopyTo(dst);
            }
        }

        // ---------------- weather: refresh now ----------------

        private void RefreshWeatherNow()
        {
            if (Widgets == null) return;
            var list = Widgets.Where(x => x.Type == "weather").ToList();
            if (list.Count == 0) { PlaySound("back"); SetWeatherStatus(Loc("LOC_KX_WeatherNoWidget", "Add a Weather widget first.")); return; }
            if (settings.WeatherLat == 0 && settings.WeatherLon == 0) { PlaySound("back"); SetWeatherStatus(Loc("LOC_KX_WeatherSetFirst", "Set your town first (above).")); return; }
            PlaySound("select");
            foreach (var w in list) w.Status = Loc("LOC_KX_WeatherLoading", "Getting the weather…");
            SetWeatherStatus(Loc("LOC_KX_WeatherRefreshing", "Refreshing the weather…"));
            weatherRefreshing = true;
            RefreshWidgetsOfType("weather");
        }
    }
}
