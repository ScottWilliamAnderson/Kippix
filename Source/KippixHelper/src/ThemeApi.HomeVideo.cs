using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// (issue #13) Home settings > Background > Gameplay video behind the games (HomeVideo, off by default).
    /// Once the highlight has rested on a game for a moment (HomeVideoDelayMs, 1.5 s), a muted, looping video of it fades in
    /// over its background art, and goes again the moment another game is highlighted, the game page or a panel opens, a game
    /// starts or Playnite isn't in front. No video: the art just stays. Where the video comes from, in this order:
    ///   1. Extra Metadata Loader's files: ExtraMetadata\games\[id]\VideoMicrotrailer.mp4, then VideoTrailer.mp4.
    ///   2. For emulated games, the video ES-DE (EmulationStation Desktop Edition) scraped:
    ///      [MediaDirectory]\[system]\videos\[rom name].mp4 / .mkv, [system] being the folder the ROM sits in.
    ///      MediaDirectory: HomeVideoMediaDir, or (left empty) the one in ES-DE's es_settings.xml, or ES-DE's / EmuDeck's usual place.
    ///   3. The Steam store trailer the game page uses (ThemeApi.Trailer.cs: downloaded once, then reused), downloaded only
    ///      when HomeVideoDownload allows it.
    /// It's a different thing from Background > Trailer (muted) (HomeBgMode 4, ThemeApi.LiveBackground.cs), which only pauses
    /// its trailer: here the player is made when a video starts and closed and thrown away when it stops, so no file stays open
    /// and nothing is decoded while a game runs or Playnite is in the background. Never more than one player.
    /// Only over the game's art (HomeBgMode 0) and only in the Row layout for now (HomeVideoLayoutOk). The player sits in
    /// KX_LiveBgHost (inside KX_BgLayers, over the art), named KX_HomeVideo. A 250 ms timer runs only while it's turned on.
    /// </summary>
    public partial class ThemeApi
    {
        private const int HomeVideoFadeInMs = 400;
        private const int HomeVideoFadeOutMs = 250;
        private static readonly string[] HomeVideoLocalNames = { "VideoMicrotrailer.mp4", "VideoTrailer.mp4" };
        private static readonly string[] HomeVideoEsDeTypes = { ".mp4", ".mkv" };

        private DispatcherTimer hvTimer;
        private Guid hvGame = Guid.Empty;     // the highlighted game the wait is for
        private DateTime hvSince;             // since when it has been highlighted (and nothing was in the way)
        private bool hvStarted;               // its video has been looked for (once per rest)
        private int hvRequest;                // a search / download that comes back for an older one is dropped
        private MediaElement hvPlayer;        // the one player (null: none)
        private MediaElement hvLeaving;       // the player fading out (closed when the fade ends, or sooner)
        private bool hvShown;                 // the video has opened and faded in
        private bool hvWindowHooked;
        private DateTime hvLaunchUntil = DateTime.MinValue;   // a game is starting: nothing plays meanwhile
        private readonly HashSet<string> hvLogged = new HashSet<string>();
        private readonly HashSet<string> hvBadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // files the player couldn't play (this session)

        public bool HomeVideo
        {
            get { return settings.HomeVideo; }
            set
            {
                if (settings.HomeVideo == value) return;
                settings.HomeVideo = value;
                SaveSettings();
                NotifyHomeVideo();
                try { HomeVideoStop(true); hvGame = Guid.Empty; EnsureHomeVideoTimer(); } catch (Exception ex) { logger.Warn("Kippix: home video: " + ex.Message); }
            }
        }

        /// <summary>The Steam store trailer may be downloaded when the game has no video of its own.</summary>
        public bool HomeVideoDownload
        {
            get { return settings.HomeVideoDownload; }
            set { if (settings.HomeVideoDownload == value) return; settings.HomeVideoDownload = value; SaveSettings(); Notify("HomeVideoDownload"); hvGame = Guid.Empty; }
        }

        /// <summary>ES-DE's media folder (downloaded_media). Empty: found by itself.</summary>
        public string HomeVideoMediaDir
        {
            get { return settings.HomeVideoMediaDir ?? ""; }
            set
            {
                string v = value ?? "";
                if ((settings.HomeVideoMediaDir ?? "") == v) return;
                settings.HomeVideoMediaDir = v;
                SaveSettings();
                Notify("HomeVideoMediaDir");
                hvGame = Guid.Empty;
            }
        }

        public int HomeVideoDelayMs
        {
            get { return settings.HomeVideoDelayMs; }
            set { if (settings.HomeVideoDelayMs == value) return; settings.HomeVideoDelayMs = value; SaveSettings(); Notify("HomeVideoDelayMs"); }
        }

        /// <summary>The switch shows while the background is the game's art (the other backgrounds have their own trailer / nothing to play over).</summary>
        public bool HomeVideoRowShown { get { return settings.HomeBgMode == 0; } }
        public bool HomeVideoDownloadShown { get { return settings.HomeBgMode == 0 && settings.HomeVideo; } }

        private void NotifyHomeVideo()
        {
            foreach (string n in new[] { "HomeVideo", "HomeVideoDownload", "HomeVideoMediaDir", "HomeVideoDelayMs", "HomeVideoRowShown", "HomeVideoDownloadShown" }) Notify(n);
        }

        /// <summary>The layouts it plays in: Row only for now (add the others here once they've been tried).</summary>
        private bool HomeVideoLayoutOk()
        {
            return settings.HomeLayoutMode == 0;
        }

        /// <summary>Starts the timer while it's turned on (fullscreen only), stops it and the video otherwise. Called from the tick and the switch.</summary>
        private void EnsureHomeVideoTimer()
        {
            if (ui == null) return;
            if (!settings.HomeVideo)
            {
                if (hvTimer != null && hvTimer.IsEnabled)
                {
                    hvTimer.Stop();
                    HomeVideoStop(false);
                    hvGame = Guid.Empty;
                    logger.Info("Kippix: home video: off");
                }
                return;
            }
            if (hvTimer != null && hvTimer.IsEnabled) return;
            if (!IsFullscreen) return;
            if (hvTimer == null)
            {
                hvTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
                hvTimer.Tick += (s, e) => { try { HomeVideoTick(); } catch (Exception ex) { HomeVideoLogOnce("tick: " + ex.Message); } };
            }
            hvGame = Guid.Empty;
            hvTimer.Start();
            logger.Info("Kippix: home video: on (" + HomeVideoDelay() + " ms after a game is highlighted; Steam trailer downloads " + (settings.HomeVideoDownload ? "allowed" : "off") + ")");
        }

        private int HomeVideoDelay()
        {
            return Math.Max(0, Math.Min(30000, settings.HomeVideoDelayMs));
        }

        /// <summary>Playnite going to the background stops it at once (the timer would only notice a moment later).</summary>
        private void HomeVideoHookWindow(Window win)
        {
            if (hvWindowHooked || win == null) return;
            hvWindowHooked = true;
            win.Deactivated += (s, e) =>
            {
                // a WPF event: a mistake in here must not reach Playnite
                try { if (hvPlayer != null || hvLeaving != null || hvStarted) { HomeVideoStop(false); hvGame = Guid.Empty; hvStarted = false; } } catch (Exception) { }
            };
        }

        /// <summary>Another game was highlighted: what was playing fades away now; the timer starts the wait for the new one.</summary>
        private void HomeVideoSelectionChanged(Game game)
        {
            if (!settings.HomeVideo || (hvPlayer == null && !hvStarted)) return;
            if (game != null && game.Id == hvGame) return;
            HomeVideoStop(true);
            hvStarted = false;
            hvGame = Guid.Empty;   // (the timer takes the new game up, and counts from then)
        }

        /// <summary>A game is about to start: the video goes now and stays away while it starts (then "a game is running" / the window takes over).</summary>
        internal void HomeVideoGameStarting()
        {
            if (!settings.HomeVideo) return;
            OnUi(() =>
            {
                hvLaunchUntil = DateTime.Now.AddSeconds(20);
                HomeVideoStop(false);
                hvGame = Guid.Empty;
                hvStarted = false;
            });
        }

        /// <summary>Why nothing may play right now (for the log), or null when the home screen's background is in sight.</summary>
        private string HomeVideoHeldWhy(Window win)
        {
            if (!settings.HomeVideo) return "turned off";
            if (!HomeVideoLayoutOk()) return "the home layout isn't Row";
            if (settings.HomeBgMode != 0) return "the home background isn't the game's art";
            if (gameRunningNow) return "a game is running";
            if (DateTime.Now < hvLaunchUntil) return "a game is starting";
            if (win == null || !win.IsActive) return "Playnite isn't in front";
            if (detailsOpen) return "the game page is open";
            if (openPanel != null) return "a panel is open";
            if (libraryLoading || ambientOn || HomeBgBlack || IntroCovering || stageOpacity < 1) return "the home background is covered";
            if (LiveBatteryPause()) return "handheld mode pauses live backgrounds (on battery)";
            if (NightNoAutoplayNow) return "night mode holds autoplay back";
            if (previewPlaying || previewShown) return "the trailer preview is playing";
            return null;
        }

        private void HomeVideoTick()
        {
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            HomeVideoHookWindow(win);
            string why = HomeVideoHeldWhy(win);
            Game game = why == null ? SelectedGameNow() : null;
            if (why == null && game == null) why = "no game is highlighted";
            if (why != null)
            {
                if (hvPlayer != null || hvStarted)
                {
                    // a panel or the game page over it: a short fade; a game or another window in front: gone at once
                    HomeVideoStop(win != null && win.IsActive && !gameRunningNow);
                }
                HomeVideoLogOnce("not playing: " + why);
                hvStarted = false;
                hvGame = Guid.Empty;   // (the wait starts again when the home screen is back)
                return;
            }
            if (game.Id != hvGame)
            {
                if (hvPlayer != null) HomeVideoStop(true);
                hvGame = game.Id;
                hvSince = DateTime.Now;
                hvStarted = false;
                return;
            }
            if (hvStarted || (DateTime.Now - hvSince).TotalMilliseconds < HomeVideoDelay()) return;
            hvStarted = true;
            HomeVideoStart(game);
        }

        // ---------------- which video ----------------

        /// <summary>Looks for the game's video off the UI thread (files on a slow disk, ES-DE's settings), then plays it, or asks for the Steam trailer.</summary>
        private void HomeVideoStart(Game game)
        {
            int request = ++hvRequest;
            Guid id = game.Id;
            string name = game.Name ?? "?";
            string extraDir = Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", id.ToString());
            string rom = HomeVideoRomPath(game);
            string mediaSetting = settings.HomeVideoMediaDir ?? "";
            string steamSaved = game.PluginId == SteamPluginId && !string.IsNullOrEmpty(game.GameId)
                ? Path.Combine(plugin.GetPluginUserDataPath(), "Trailers", game.GameId, "trailer.mp4") : null;
            KippixShared.SafeWork.Queue(_ =>
            {
                string kind = null, file = null;
                try
                {
                    foreach (string n in HomeVideoLocalNames)
                    {
                        string f = Path.Combine(extraDir, n);
                        if (HomeVideoUsable(f)) { file = f; kind = "local video"; break; }
                    }
                    if (file == null)
                    {
                        file = HomeVideoEsDeFile(rom, HomeVideoMediaRoot(mediaSetting));
                        if (file != null) kind = "ES-DE video";
                    }
                    if (file == null && steamSaved != null && HomeVideoUsable(steamSaved)) { file = steamSaved; kind = "Steam trailer"; }   // (already downloaded: no need to ask)
                }
                catch (Exception ex) { HomeVideoLogOnce(name + ": looking for its video: " + ex.Message); }
                OnUi(() =>
                {
                    if (file != null) HomeVideoPicked(request, id, name, kind, file);
                    else HomeVideoSteam(request, game, name);
                });
            });
        }

        /// <summary>3. The Steam store trailer, through the game page's downloader and cache (never on the UI thread; at most two downloads at once).</summary>
        private void HomeVideoSteam(int request, Game game, string name)
        {
            if (request != hvRequest) return;
            if (!settings.HomeVideoDownload || game.PluginId != SteamPluginId || string.IsNullOrEmpty(game.GameId))
            {
                HomeVideoLogOnce(name + ": none (the art stays)");
                return;
            }
            Guid id = game.Id;
            string appId = game.GameId;
            ResolveSteamTrailer(game, files =>
            {
                string file = files != null ? files.Video : null;
                if (string.IsNullOrEmpty(file) || !HomeVideoUsable(file)) { HomeVideoLogOnce(name + ": none (the art stays)"); return; }
                HomeVideoPicked(request, id, name, "Steam trailer", file);
            }, () =>
            {
                bool go;
                lock (trailerWaiters) go = trailerWaiters.ContainsKey(appId) || trailerWaiters.Count < 2;
                HomeVideoLogOnce(name + (go ? ": downloading its Steam trailer" : ": its Steam trailer waits (other trailers are downloading)"));
                return go;
            });
        }

        private void HomeVideoPicked(int request, Guid id, string name, string kind, string file)
        {
            HomeVideoLogOnce(name + ": " + kind + " (" + file + ")");
            if (request != hvRequest || id != hvGame || !hvStarted) return;   // moved on meanwhile
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (HomeVideoHeldWhy(win) != null) return;
            HomeVideoPlay(file);
        }

        private bool HomeVideoUsable(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            lock (hvBadFiles) if (hvBadFiles.Contains(file)) return false;
            try { return File.Exists(file); } catch (Exception) { return false; }
        }

        /// <summary>The game's first ROM, with Playnite's variables ({InstallDir}...) filled in; null when it has none.</summary>
        private string HomeVideoRomPath(Game game)
        {
            try
            {
                if (game.Roms == null || game.Roms.Count == 0 || game.Roms[0] == null) return null;
                string path = game.Roms[0].Path;
                if (string.IsNullOrEmpty(path)) return null;
                try { path = api.ExpandGameVariables(game, path); } catch (Exception) { }
                if (string.IsNullOrEmpty(path) || path.IndexOf('{') >= 0) return null;
                if (!Path.IsPathRooted(path))
                {
                    string dir = game.InstallDirectory;
                    if (string.IsNullOrEmpty(dir)) return null;
                    try { dir = api.ExpandGameVariables(game, dir); } catch (Exception) { }
                    path = Path.Combine(dir, path);
                }
                return path;
            }
            catch (Exception) { return null; }
        }

        // ---------------- ES-DE's scraped videos ----------------

        private static readonly object hvMediaLock = new object();
        private static string hvMediaFor;          // the HomeVideoMediaDir the answer below is for
        private static string hvMediaRoot;         // ES-DE's media folder (null: not found)
        private static DateTime hvMediaAt = DateTime.MinValue;

        /// <summary>
        /// ES-DE's media folder: the setting, else MediaDirectory from ES-DE's es_settings.xml, else ES-DE's own default
        /// (downloaded_media in its data folder). Null when there's none (asked again a minute later). Worker thread.
        /// </summary>
        private string HomeVideoMediaRoot(string setting)
        {
            setting = (setting ?? "").Trim().Trim('"');
            lock (hvMediaLock)
            {
                if (hvMediaFor == setting && (hvMediaRoot != null || (DateTime.Now - hvMediaAt).TotalSeconds < 60)) return hvMediaRoot;
            }
            string root = null, from = null;
            try
            {
                if (setting.Length > 0)
                {
                    root = HomeVideoFullPath(setting);
                    from = "HomeVideoMediaDir";
                    if (root == null || !Directory.Exists(root)) { HomeVideoLogOnce("ES-DE media folder " + setting + " (HomeVideoMediaDir) isn't there"); root = null; }
                }
                else
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var dataDirs = new List<string>();
                    string env = Environment.GetEnvironmentVariable("ESDE_APPDATA_DIR");   // ES-DE's own way to say where its data is
                    if (!string.IsNullOrEmpty(env)) dataDirs.Add(env);
                    dataDirs.Add(Path.Combine(home, "ES-DE"));
                    dataDirs.Add(Path.Combine(roaming, "EmuDeck", "EmulationStation-DE", "ES-DE"));   // EmuDeck's portable ES-DE
                    dataDirs.Add(Path.Combine(home, ".emulationstation"));                           // ES-DE 2.x
                    foreach (string dir in dataDirs)
                    {
                        string file = Path.Combine(dir, dir.EndsWith(".emulationstation", StringComparison.OrdinalIgnoreCase) ? "es_settings.xml" : "settings\\es_settings.xml");
                        if (!File.Exists(file)) continue;
                        string value = HomeVideoEsSetting(file, "MediaDirectory");
                        string found = string.IsNullOrEmpty(value) ? Path.Combine(dir, "downloaded_media") : HomeVideoFullPath(value);
                        if (found != null && Directory.Exists(found)) { root = found; from = file; break; }
                    }
                    if (root == null)
                    {
                        string usual = Path.Combine(home, "ES-DE", "downloaded_media");
                        if (Directory.Exists(usual)) { root = usual; from = "ES-DE's usual place"; }
                    }
                }
            }
            catch (Exception ex) { HomeVideoLogOnce("ES-DE media folder: " + ex.Message); }
            lock (hvMediaLock) { hvMediaFor = setting; hvMediaRoot = root; hvMediaAt = DateTime.Now; }
            HomeVideoLogOnce(root != null ? "ES-DE media folder: " + root + " (from " + from + ")"
                                          : "ES-DE media folder: not found (only the folder next to the ROMs is tried; HomeVideoMediaDir can name it)");
            return root;
        }

        /// <summary>A path as ES-DE writes it (mixed slashes, ~ for the user's folder, %variables%) made whole; null if it can't be.</summary>
        private static string HomeVideoFullPath(string path)
        {
            try
            {
                path = Environment.ExpandEnvironmentVariables((path ?? "").Trim());
                if (path.Length == 0) return null;
                if (path[0] == '~') path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
                if (!Path.IsPathRooted(path)) return null;
                return Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');
            }
            catch (Exception) { return null; }
        }

        /// <summary>One value from es_settings.xml (lines like &lt;string name="MediaDirectory" value="..." /&gt;, with no root element, so not read as XML).</summary>
        private static string HomeVideoEsSetting(string file, string name)
        {
            try
            {
                var m = Regex.Match(File.ReadAllText(file), "<string\\s+name=\"" + Regex.Escape(name) + "\"\\s+value=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 2. The video ES-DE scraped for a ROM: [media]\[system]\videos\[rom name].mp4 (.mkv), [system] being the folder the ROM
        /// sits in. A ROM in a folder of its own inside the system's folder is found too (ES-DE keeps the same folders under
        /// videos), and next to the known media folder EmuDeck's layout is tried ([roms]\..\storage\downloaded_media) and
        /// ES-DE's ([roms]\..\ES-DE\downloaded_media). Worker thread. Null when there's none.
        /// </summary>
        private string HomeVideoEsDeFile(string rom, string mediaRoot)
        {
            if (string.IsNullOrEmpty(rom)) return null;
            rom = rom.Replace('/', '\\').TrimEnd('\\');
            string rel = Path.GetFileNameWithoutExtension(rom);
            string dir = Path.GetDirectoryName(rom);
            if (string.IsNullOrEmpty(rel)) return null;
            for (int up = 0; up < 3 && !string.IsNullOrEmpty(dir); up++)
            {
                string system = Path.GetFileName(dir);
                string romsRoot = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(system)) break;
                var roots = new List<string>();
                if (mediaRoot != null) roots.Add(mediaRoot);
                if (!string.IsNullOrEmpty(romsRoot))
                {
                    string above = Path.GetDirectoryName(romsRoot);
                    if (!string.IsNullOrEmpty(above))
                    {
                        roots.Add(Path.Combine(above, "storage", "downloaded_media"));
                        roots.Add(Path.Combine(above, "ES-DE", "downloaded_media"));
                    }
                }
                foreach (string root in roots)
                    foreach (string type in HomeVideoEsDeTypes)
                    {
                        string f = Path.Combine(root, system, "videos", rel + type);
                        if (HomeVideoUsable(f)) return f;
                    }
                rel = Path.Combine(system, rel);
                dir = romsRoot;
            }
            return null;
        }

        // ---------------- the player ----------------

        private void HomeVideoPlay(string path)
        {
            var host = LiveHost();
            if (host == null) { HomeVideoLogOnce("KX_LiveBgHost wasn't found: nowhere to put the video"); return; }
            HomeVideoStop(false);   // never two players (and a fade-out still running ends here)
            var me = new MediaElement
            {
                Name = "KX_HomeVideo",
                LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsMuted = true, Volume = 0, IsHitTestVisible = false, Opacity = 0, ScrubbingEnabled = false
            };
            me.MediaOpened += (s, e) =>
            {
                // only now is there a picture: until then it's see-through and the art shows (no black flash)
                if (!ReferenceEquals(s, hvPlayer)) return;
                try
                {
                    hvShown = true;
                    SetMotionPaused(true);   // the background zoom holds still under it
                    me.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(HomeVideoFadeInMs)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
                }
                catch (Exception) { }
            };
            me.MediaEnded += (s, e) =>
            {
                if (!ReferenceEquals(s, hvPlayer)) return;
                try { me.Position = TimeSpan.Zero; me.Play(); } catch (Exception) { }   // loops
            };
            me.MediaFailed += (s, e) =>
            {
                try { HomeVideoFailed(me, path, e.ErrorException != null ? e.ErrorException.Message : "unknown"); } catch (Exception) { }
            };
            hvPlayer = me;
            hvShown = false;
            try
            {
                host.Children.Add(me);   // (last: over the art and anything else in there)
                me.Source = new Uri(path);
                me.Play();
            }
            catch (Exception ex) { HomeVideoFailed(me, path, ex.Message); }
        }

        /// <summary>A file the player can't play: said once, not tried again this session, and the game's next video (if it has another) is looked for.</summary>
        private void HomeVideoFailed(MediaElement me, string path, string why)
        {
            bool first;
            lock (hvBadFiles) first = hvBadFiles.Add(path);
            if (first) logger.Warn("Kippix: home video: couldn't play " + path + ": " + why);
            if (!ReferenceEquals(me, hvPlayer)) { HomeVideoRelease(me); return; }
            HomeVideoStop(false);   // the art stays
            if (!first || !hvStarted || hvGame == Guid.Empty) return;
            var game = SelectedGameNow();
            if (game != null && game.Id == hvGame) HomeVideoStart(game);
        }

        /// <summary>Stops the video: a short fade back to the art (or at once), then the player is closed and thrown away.</summary>
        private void HomeVideoStop(bool fade)
        {
            hvRequest++;
            if (hvLeaving != null) { var old = hvLeaving; hvLeaving = null; HomeVideoRelease(old); }
            var me = hvPlayer;
            if (me == null) return;
            hvPlayer = null;
            bool shown = hvShown;
            hvShown = false;
            if (shown) SetMotionPaused(previewShown);
            if (fade && shown && !settings.ReduceMotion)
            {
                hvLeaving = me;
                try
                {
                    var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(HomeVideoFadeOutMs));
                    a.Completed += (s, e) =>
                    {
                        if (ReferenceEquals(hvLeaving, me)) hvLeaving = null;
                        HomeVideoRelease(me);
                    };
                    me.BeginAnimation(UIElement.OpacityProperty, a);
                    return;
                }
                catch (Exception) { hvLeaving = null; }
            }
            HomeVideoRelease(me);
        }

        /// <summary>Closes a player (its file handle and decoder go) and takes it out of the window. Safe to call twice.</summary>
        private static void HomeVideoRelease(MediaElement me)
        {
            if (me == null) return;
            try { me.BeginAnimation(UIElement.OpacityProperty, null); me.Opacity = 0; } catch (Exception) { }
            try { me.Stop(); } catch (Exception) { }
            try { me.Close(); } catch (Exception) { }
            try { me.Source = null; } catch (Exception) { }
            try
            {
                var parent = VisualTreeHelper.GetParent(me) as Panel;
                if (parent != null) parent.Children.Remove(me);
            }
            catch (Exception) { }
        }

        /// <summary>"Kippix: home video: ...", each line once a session (which video was picked for which game, why it stopped...).</summary>
        private void HomeVideoLogOnce(string text)
        {
            lock (hvLogged) { if (hvLogged.Count > 2000) hvLogged.Clear(); if (!hvLogged.Add(text)) return; }
            logger.Info("Kippix: home video: " + text);
        }
    }
}
