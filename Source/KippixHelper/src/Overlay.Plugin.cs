using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace KippixOverlay
{
    /// <summary>
    /// The in-game overlay. Since KippixHelper 0.29 it's part of the helper (it used to be its own add-on,
    /// KippixOverlay): HelperPlugin creates it and passes Playnite's events on. Its files (config, guides, Discord login)
    /// stay in the old add-on's data folder, so nothing is lost.
    /// </summary>
    public class OverlayPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        /// <summary>The old KippixOverlay add-on's id: its data folder (and how we spot it if it's still installed).</summary>
        public static readonly Guid PluginId = new Guid("b4a7d1c2-5e8f-4b9a-9c3d-2f6e1a8b7c55");

        public Guid Id { get { return PluginId; } }
        internal IPlayniteAPI PlayniteApi { get; private set; }
        internal bool Started { get; private set; }

        internal string GetPluginUserDataPath()
        {
            string dir = Path.Combine(PlayniteApi.Paths.ExtensionsDataPath, PluginId.ToString());
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            return dir;
        }

        internal OverlayConfig Config;
        internal Game RunningGame;
        internal DateTime GameStartedAt = DateTime.Now;
        internal IntPtr GameWindow = IntPtr.Zero;
        internal ControllerPoller Poller;
        internal SystemStats Stats = new SystemStats();
        internal List<AchievementInfo> CurrentAchievements;
        internal string CurrentAchievementInfo = "";
        internal HltbTimes CurrentHltb;
        internal DiscordService Discord;
        internal GuideStore Guides;
        private GuideSettings settings;
        private readonly HashSet<Guid> guideDownloads = new HashSet<Guid>();

        internal volatile bool DiscordRunning;

        // Mute/deafen state when Discord isn't connected through the overlay (hotkey fallback).
        private bool hotkeyMuted, hotkeyDeafened, mutedBeforeDeafen;
        private DateTime discordStartRequested = DateTime.MinValue;
        private Guid pendingSwitch = Guid.Empty;
        private readonly Dictionary<Guid, string> achievementSummaries = new Dictionary<Guid, string>();
        private volatile bool backgroundConnecting;

        private OverlayWindow overlay;
        private Dispatcher dispatcher;
        private System.Threading.Timer tracker;
        private int trackerTicks;
        private ushort lastHeld;
        private readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;

        // Discord controller mode
        private bool discordMode;
        private IntPtr discordHwnd = IntPtr.Zero;
        private HintBar hintBar;
        private KeyboardWindow keyboard;
        private System.Threading.Timer focusWatcher;
        private volatile bool focusBusy;
        private string dismissedKey;
        private string lastFocusKey;
        private bool authorizing;

        // Compose (on-screen keyboard over the overlay)
        private KeyboardWindow compose;

        public OverlayPlugin(IPlayniteAPI api)
        {
            PlayniteApi = api;
            try { Chime.SoundFile = Path.Combine(api.Paths.ConfigurationPath, "Themes", "Fullscreen", "Kippix_Kipping0", "Audio", "OverlayScreenshot.wav"); } catch (Exception) { }
            try { Chime.OpenFile = Path.Combine(api.Paths.ConfigurationPath, "Themes", "Fullscreen", "Kippix_Kipping0", "Audio", "OverlayOpen.wav"); } catch (Exception) { }
        }

        internal IPlayniteAPI Api { get { return PlayniteApi; } }

        internal string ConfigPath
        {
            get { return Path.Combine(GetPluginUserDataPath(), "config.json"); }
        }

        internal OverlayTheme Theme
        {
            get { return OverlayTheme.Get(Config.Theme); }
        }

        internal void LogInfo(string message) { try { logger.Info(message); } catch (Exception) { } }

        // ================= (0.99.49) Hold Guide: the power menu =================

        private PowerMenuWindow powerMenu;
        private DispatcherTimer guideHoldTimer;

        private void StartGuideHold()
        {
            if (guideHoldTimer == null)
            {
                guideHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                guideHoldTimer.Tick += (s, e) =>
                {
                    guideHoldTimer.Stop();
                    try
                    {
                        bool pending = guideOpenPending;
                        guideOpenPending = false;
                        if ((lastHeld & Btn.Guide) == 0 || RunningGame == null || powerMenu != null || !Config.HoldGuidePower)
                        {
                            if (pending && powerMenu == null) OpenOverlay(0);   // (the let-go was missed: it was a press)
                            return;
                        }
                        ShowPowerMenu();
                    }
                    catch (Exception ex) { LogError(ex, "Power menu failed"); }
                };
            }
            guideHoldTimer.Stop();
            guideHoldTimer.Start();
        }

        private void ShowPowerMenu()
        {
            logger.Info("Kippix overlay: power menu (Guide held)");
            bool overlayUp = overlay != null && (overlay.IsOpen || overlay.IsClosing);
            CloseCompose();
            if (overlay != null && overlay.IsOpen) overlay.CloseOverlay(null);   // (the press opened it; the hold turns it into this)
            Action open = () =>
            {
                if (powerMenu != null) return;
                // (0.99.51) the game's window, from what's in front now, if the overlay hasn't found it yet (Close game needs it)
                try { IntPtr fg = Native.GetForegroundWindow(); if (GameWindow == IntPtr.Zero && IsGameWindowCandidate(fg)) GameWindow = fg; } catch (Exception) { }
                var w = new PowerMenuWindow(this, GameWindow);
                powerMenu = w;
                w.Closed += (s, e) => { if (ReferenceEquals(powerMenu, w)) powerMenu = null; };
                w.ShowOnTop();
                if (Config.OpenVibration && Poller != null) Poller.Pulse(60, 0.0, 0.4);
            };
            if (overlayUp) RunLater(() => OnUi(open), 260); else open();
        }

        /// <summary>(0.99.49) The power menu: closes the game (as Close game does, without its notices), Playnite comes forward.</summary>
        internal void CloseGameForPower()
        {
            try
            {
                uint pid = Native.GetProcessId(GameWindow);
                if (pid != 0 && pid != ownPid && !DiscordHelper.IsDiscordPid(pid)) Native.CloseGracefully(pid, 8000, null);   // (0.99.56: asked first, forced after 8 s)
                else logger.Info("Kippix overlay: power menu: the game's window wasn't found (Windows closes it as it shuts down)");
                RunLater(() => OnUi(BringPlayniteForward), 1500);
            }
            catch (Exception ex) { LogError(ex, "Power menu: closing the game failed"); }
        }

        internal void LogError(Exception ex, string message)
        {
            try { logger.Error(ex, message); } catch (Exception) { }
        }

        // ================= Playnite events =================

        public void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            dispatcher = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            Config = OverlayConfig.Load(ConfigPath);
            if (Poller != null) Poller.OverlayHotkey = Config.OverlayHotkey ?? "";
            if (Guides == null) Guides = new GuideStore(Path.Combine(GetPluginUserDataPath(), "guides"));
            Config.Save(ConfigPath); // writes any new settings into the file

            Discord = new DiscordService(Path.Combine(GetPluginUserDataPath(), "discord-login.json"));
            Discord.Configure(Config.DiscordClientId, Config.DiscordClientSecret);
            Discord.Changed = () =>
            {
                OnUi(() => { if (overlay != null) overlay.OnLiveDataChanged(); });
                OverlayBridge.RaiseChanged();
            };
            Discord.Notified = c =>
            {
                if (Config.ShowDiscordMessages && RunningGame != null && !discordMode)
                    Toast(c.Title, Shorten(c.LastBody, 120));
            };

            Poller = new ControllerPoller();
            Poller.OverlayHotkey = Config.OverlayHotkey ?? "";
            Poller.PlayniteGuideDown = PlayniteGuideDown;
            Poller.Changed = (pressed, held) =>
            {
                // (0.99.72) when it happened: a press made while the overlay was still opening (the screen's thread busy) is told apart
                // from one made after it showed
                DateTime at = DateTime.UtcNow;
                if (dispatcher != null) dispatcher.BeginInvoke(new Action(() => HandleChanged(pressed, held, at)));
            };
            Poller.Start();

            tracker = new System.Threading.Timer(_ => TrackerTick(), null, 1000, 1000);

            // Look for new games without guides once Playnite (and SuccessStory) has settled.
            if (Config.UpdateGuidesOnStartup && Config.GuidesReady)
                RunLater(() => OnUi(() => StartGuideUpdate(false)), 60000);

            // only once everything above is up: the theme's link and the game events rely on it
            Started = true;
            OverlayBridge.Plugin = this;
        }

        public void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            if (!Started) return;
            try { if (discordMode) ExitDiscordMode(false); } catch (Exception) { }
            if (Poller != null) Poller.Stop();
            if (tracker != null) tracker.Dispose();
            Stats.Stop();
            try { Discord.Rpc.Disconnect(); } catch (Exception) { }
        }

        public void OnGameStarted(OnGameStartedEventArgs args)
        {
            if (!Started) return;
            RunningGame = args.Game;
            GameStartedAt = DateTime.Now;
            lock (achievementSummaries) achievementSummaries.Clear();
            GameWindow = IntPtr.Zero;
            installDirCache = null;

            // Fetch achievement guides in the background the first time a game is played.
            Game started = args.Game;
            RunLater(() =>
            {
                var c = ReloadConfig();
                if (c.AutoDownloadGuides && c.GuidesReady && started != null && !Guides.Has(started.Id) && !GuideAutoPaused(c))
                    DownloadGuidesBackground(started, false, null);
            }, 8000);
        }

        public void OnGameStopped(OnGameStoppedEventArgs args) { OnGameStopped(args != null ? args.Game : null); }

        public void OnGameStopped(Game stopped)
        {
            if (!Started) return;
            if (RunningGame != null && stopped != null && RunningGame.Id != stopped.Id) return;
            RunningGame = null;
            GameWindow = IntPtr.Zero;
            CloseCompose();
            if (overlay != null) overlay.CloseOverlay(null);
            lock (achievementSummaries) achievementSummaries.Clear();

            if (pendingSwitch != Guid.Empty)
            {
                Guid next = pendingSwitch;
                pendingSwitch = Guid.Empty;
                StartGameSoon(next, 1500);
            }
        }

        private int trackerBusy;

        private void TrackerTick()
        {
            // a slow tick (Discord connecting, Steam's files) mustn't pile up more ticks on the thread pool behind it
            if (Interlocked.CompareExchange(ref trackerBusy, 1, 0) != 0) return;
            try { TrackerWork(); }
            catch (Exception ex) { LogError(ex, "Overlay tracker failed"); }
            finally { Interlocked.Exchange(ref trackerBusy, 0); }
        }

        private void TrackerWork()
        {
            trackerTicks++;
            if (trackerTicks % 2 == 0) DiscordRunning = DiscordHelper.IsRunning();

            // Keep the Discord connection up in the background once it has been set up.
            if (trackerTicks % 10 == 0 && Discord != null && !Discord.Ready && !backgroundConnecting && !authorizing &&
                DiscordRunning && Discord.Rpc.Configured && Discord.Rpc.HasSavedLogin)
            {
                backgroundConnecting = true;
                try { Discord.Connect(false); } catch (Exception) { }
                backgroundConnecting = false;
            }

            if (trackerTicks % 20 == 0) CheckControllerBattery();

            // Keep sampling downloads so live progress has a starting point when Steam saves its figures.
            if (trackerTicks % 3 == 0)
            {
                Downloads.GameRunning = RunningGame != null;
                try { CurrentDownloads(); } catch (Exception) { }
            }

            if (RunningGame == null) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (IsGameWindowCandidate(fg)) GameWindow = fg;
        }

        // ================= Controller battery warning =================

        private int lastBatteryWarning;

        private void CheckControllerBattery()
        {
            if (Poller == null) return;
            int w = Poller.BatteryWarning();
            if (w > lastBatteryWarning)
            {
                if (w == 2) Toast("Controller battery almost empty", "Plug it in or swap the batteries soon.");
                else Toast("Controller battery low", "You've got a while left, but it's worth charging soon.");
            }
            lastBatteryWarning = w;   // back to 0 once charging, so the next drop warns again
        }

        // ================= Bottom bar =================

        private string audioName;
        private DateTime audioNameAt = DateTime.MinValue;

        /// <summary>The current Windows output device, re-read every few seconds.</summary>
        internal string AudioOutputName()
        {
            if ((DateTime.UtcNow - audioNameAt).TotalSeconds > 5)
            {
                audioNameAt = DateTime.UtcNow;
                // (0.99.55) looked up off the screen's thread (going through the sound devices took a moment, every 5 s, while
                // the overlay was open: a hitch each time); the last name shows meanwhile
                KippixShared.SafeWork.Queue(_ =>
                {
                    try
                    {
                        var d = Audio.OutputDevices().FirstOrDefault(x => x.IsDefault);
                        audioName = d != null ? d.Name : null;
                    }
                    catch (Exception) { }
                });
            }
            return audioName;
        }

        /// <summary>"In call", "In call · Muted", "Muted", "Discord" (running, not in a call) or null when Discord isn't running.</summary>
        internal string DiscordBarText()
        {
            if (!DiscordRunning) return null;
            bool inCall = Discord != null && Discord.Ready && Discord.Voice != null;
            if (inCall) return Deafened ? "In call \u00B7 Deafened" : MicMuted ? "In call \u00B7 Muted" : "In call";
            if (Deafened) return "Deafened";
            if (MicMuted) return "Muted";
            return "Discord";
        }

        internal List<DownloadInfo> CurrentDownloads()
        {
            IEnumerable<Game> games = null;
            try { games = PlayniteApi.Database.Games; } catch (Exception) { }
            return Downloads.Get(games);
        }

        // ================= Next achievement =================

        private Dictionary<string, string> nextAchievements;

        private string NextAchievementsPath
        {
            get { return Path.Combine(GetPluginUserDataPath(), "next-achievements.json"); }
        }

        /// <summary>The achievement name pinned as "next" for a game, or null.</summary>
        internal string GetNextAchievement(Guid gameId)
        {
            LoadNextAchievements();
            string name;
            return nextAchievements.TryGetValue(gameId.ToString(), out name) ? name : null;
        }

        internal void SetNextAchievement(Guid gameId, string achievementName)
        {
            LoadNextAchievements();
            if (string.IsNullOrEmpty(achievementName)) nextAchievements.Remove(gameId.ToString());
            else nextAchievements[gameId.ToString()] = achievementName;
            try
            {
                File.WriteAllText(NextAchievementsPath, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(nextAchievements));
            }
            catch (Exception ex) { logger.Error(ex, "Couldn't save next achievement"); }
        }

        private void LoadNextAchievements()
        {
            if (nextAchievements != null) return;
            nextAchievements = new Dictionary<string, string>();
            try
            {
                if (File.Exists(NextAchievementsPath))
                {
                    var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(NextAchievementsPath));
                    if (d != null) nextAchievements = d;
                }
            }
            catch (Exception) { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WinRect r);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MonInfo info);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct MonInfo { public int Size; public WinRect Monitor, Work; public uint Flags; }

        /// <summary>A window of another program (not Explorer or Discord) that covers its whole screen: a game.</summary>
        private bool LooksLikeGameWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return false;
                uint pid = Native.GetProcessId(hwnd);
                if (pid == 0 || pid == ownPid || DiscordHelper.IsDiscordPid(pid) || Audio.IsExeNamed(pid, "explorer.exe")) return false;
                WinRect r;
                if (!GetWindowRect(hwnd, out r)) return false;
                var mi = new MonInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MonInfo)) };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi)) return false;
                return r.Left <= mi.Monitor.Left && r.Top <= mi.Monitor.Top && r.Right >= mi.Monitor.Right && r.Bottom >= mi.Monitor.Bottom;
            }
            catch (Exception) { return false; }
        }

        private bool IsGameWindowCandidate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid = Native.GetProcessId(hwnd);
            if (pid == 0 || pid == ownPid) return false;
            if (DiscordHelper.IsDiscordPid(pid)) return false;
            if (Audio.IsExeNamed(pid, "explorer.exe")) return false;
            if (UnderInstallDir(pid)) return true;
            return GameWindow == IntPtr.Zero || !Native.IsWindow(GameWindow);
        }

        private string installDirCache;

        private bool UnderInstallDir(uint pid)
        {
            if (RunningGame == null) return false;
            if (installDirCache == null)
            {
                string dir = RunningGame.InstallDirectory ?? "";
                try { dir = PlayniteApi.ExpandGameVariables(RunningGame, dir); } catch (Exception) { }
                installDirCache = Directory.Exists(dir) ? Path.GetFullPath(dir).TrimEnd('\\') + "\\" : "";
            }
            if (installDirCache.Length == 0) return false;
            string path = Audio.ProcessPath(pid);
            return path.StartsWith(installDirCache, StringComparison.OrdinalIgnoreCase);
        }

        internal bool IsGamePid(uint pid)
        {
            if (pid == 0 || RunningGame == null) return false;
            if (pid == Native.GetProcessId(GameWindow)) return true;
            return UnderInstallDir(pid);
        }

        // ================= The Steam Controller, through Playnite =================

        private List<Playnite.SDK.Events.IGamepad> playnitePads = new List<Playnite.SDK.Events.IGamepad>();
        private DateTime playnitePadsAt = DateTime.MinValue;
        private DateTime guideDownSince = DateTime.MinValue;
        private bool guideStuck;

        /// <summary>
        /// The Steam button of a controller Playnite reads but Windows doesn't show as an Xbox pad (the Steam Controller):
        /// Playnite keeps its button states. Only Valve pads (other pads are already read directly), only when switched on.
        /// The list of pads is looked up every 3 seconds, not every loop.
        /// </summary>
        private bool PlayniteGuideDown()
        {
            if (Config == null || !Config.SteamButtonOpens) return false;
            if ((DateTime.UtcNow - playnitePadsAt).TotalSeconds > 3)
            {
                playnitePadsAt = DateTime.UtcNow;
                try
                {
                    playnitePads = PlayniteApi.GetConnectedControllers().OfType<Playnite.SDK.Events.IGamepad>()
                                   .Where(p => p != null && p.Enabled && p.Name != null && p.Name.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                }
                catch (Exception) { playnitePads = new List<Playnite.SDK.Events.IGamepad>(); }
            }
            bool down = false;
            foreach (var p in playnitePads)
            {
                var b = p.ButtonInputState;
                Playnite.SDK.Events.ControllerInputState st = Playnite.SDK.Events.ControllerInputState.Released;
                if (b != null && b.TryGetValue(Playnite.SDK.Events.ControllerInput.Guide, out st) && st == Playnite.SDK.Events.ControllerInputState.Pressed) { down = true; break; }
            }
            // Playnite may stop updating while another window has the focus (Steam's own overlay opens on that button):
            // a "held" that lasts more than 2 seconds is taken as stuck and ignored until it's let go
            var now = DateTime.UtcNow;
            if (!down) { guideDownSince = DateTime.MinValue; guideStuck = false; return false; }
            if (guideDownSince == DateTime.MinValue) guideDownSince = now;
            if ((now - guideDownSince).TotalSeconds > 2) guideStuck = true;
            return !guideStuck;
        }

        // ================= Controller =================

        private bool IsTrigger(ushort pressed, ushort held)
        {
            bool guide = (pressed & Btn.Guide) != 0;
            const ushort combo = Btn.Back | Btn.Start;
            bool comboPressed = (pressed & combo) != 0 && (held & combo) == combo;
            int mode = Config.TriggerMode;
            return (guide && mode != 1) || (comboPressed && mode != 0);
        }

        private DateTime lastPadInput = DateTime.MinValue;

        /// <summary>The buttons held right now on the controllers the overlay reads itself.</summary>
        internal ushort HeldButtons { get { return lastHeld; } }

        /// <summary>True while a controller button is held or for a moment after; keyboard copies of it are ignored then.</summary>
        internal bool ControllerRecentlyUsed
        {
            get { return lastHeld != 0 || (DateTime.UtcNow - lastPadInput).TotalMilliseconds < 400; }
        }

        private void HandleChanged(ushort pressed, ushort held, DateTime at)
        {
            // runs from the dispatcher queue: anything thrown here would reach Playnite
            try { HandleChangedCore(pressed, held, at); }
            catch (Exception ex) { LogError(ex, "Overlay controller input failed"); }
        }

        private void HandleChangedCore(ushort pressed, ushort held, DateTime at)
        {
            ushort released = (ushort)(lastHeld & ~held);
            lastHeld = held;
            lastPadInput = DateTime.UtcNow;

            // (0.99.49) the power menu takes every button while it's up
            if (powerMenu != null) { powerMenu.HandlePress(pressed); return; }
            if (capturePreview != null) { capturePreview.HandlePad(pressed, held); return; }   // (0.99.54)
            // (0.99.50) Guide in a game: a press (let go within 1.5 s) opens the overlay, a hold the power menu (nothing opens first)
            if (guideOpenPending && (released & Btn.Guide) != 0)
            {
                guideOpenPending = false;
                guideUpAt = DateTime.UtcNow;
                if (guideHoldTimer != null) guideHoldTimer.Stop();
                OpenOverlay(0);
                return;
            }
            if (guideOpenPending) return;   // (waiting to see which: nothing else meanwhile)

            if ((pressed & Btn.Mic) != 0 && Config.MicButtonMutesDiscord && DiscordRunning)
            {
                ToggleMute();
                Toast(MicMuted ? "Microphone muted" : "Microphone on", "Discord");
            }

            if (compose != null)
            {
                if ((pressed & Btn.Guide) != 0) CloseCompose();
                else if (pressed != 0) compose.HandlePress(pressed);
                return;
            }

            if (discordMode)
            {
                HandleDiscordMode(pressed, released);
                return;
            }

            if (overlay != null && overlay.IsOpen)
            {
                // (0.99.72) pressed again while it was still opening: that press was waiting in the queue and closed the overlay
                // the moment it appeared (pressing Guide again and again then never showed it). Only presses made after it showed count.
                if (pressed != 0 && at < overlayReadyAt)
                {
                    if ((pressed & Btn.Guide) != 0) logger.Info("Kippix overlay: a Guide press made while it was opening was ignored (it would have closed it straight away)");
                    return;
                }
                if ((pressed & Btn.Guide) != 0 || IsTrigger(pressed, held)) { Resume(); return; }
                overlay.HandleController(pressed);
                return;
            }

            if (pressed == 0) return;
            if (!IsTrigger(pressed, held)) return;
            if (Config.OnlyInFullscreen && PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Fullscreen) return;
            if (RunningGame == null)
            {
                // Playnite can lose track of a game (started twice, or through a launcher that exits): a full-screen window
                // in front that isn't Playnite's still counts as the game
                IntPtr fg = Native.GetForegroundWindow();
                if (!LooksLikeGameWindow(fg)) { logger.Info("Kippix overlay: not opened (no game running, nothing full screen in front)"); return; }
                logger.Info("Kippix overlay: opening over a full-screen window (Playnite doesn't see a game running)");
            }
            if ((pressed & Btn.Guide) != 0 && Config.TriggerMode != 1 && RunningGame != null && Config.HoldGuidePower)
            {
                // (0.99.50) wait for the let-go: a press opens the overlay, a hold the power menu
                guideOpenPending = true;
                guideDownAt = DateTime.UtcNow;
                StartGuideHold();
                return;
            }
            OpenOverlay(0);
        }
        private bool guideOpenPending;
        private DateTime overlayReadyAt = DateTime.MinValue;
        private DateTime guideDownAt = DateTime.MinValue, guideUpAt = DateTime.MinValue;

        // ================= Overlay =================

        private Guid gameDataFor;
        private DateTime gameDataAt = DateTime.MinValue;

        /// <summary>(0.99.56) Opening the overlay again within a minute uses what was read last time (reading them took a moment every open).</summary>
        internal void ReloadGameDataIfOld()
        {
            if (RunningGame != null && gameDataFor == RunningGame.Id && CurrentAchievements != null && (DateTime.UtcNow - gameDataAt).TotalSeconds < 60) return;
            ReloadGameData();
        }

        internal void ReloadGameData()
        {
            if (RunningGame == null) { CurrentAchievements = null; CurrentHltb = null; return; }
            gameDataFor = RunningGame.Id;
            gameDataAt = DateTime.UtcNow;
            string ext = PlayniteApi.Paths.ExtensionsDataPath;
            string info;
            CurrentAchievements = LoadAchievementsFor(RunningGame, out info);
            CurrentAchievementInfo = info + "\nGame ID: " + RunningGame.Id;
            logger.Info("Kippix achievements: " + CurrentAchievementInfo.Replace("\n", " | "));
            CurrentHltb = GameData.LoadHltb(ext, RunningGame.Id);
        }

        internal void OpenOverlay(int tab)
        {
            try { if (tab == 0 && OpenCapturePreviewIfDue()) return; } catch (Exception ex) { LogError(ex, "Capture preview failed"); capturePreview = null; }
            if (overlay != null)
            {
                if (overlay.IsOpen || overlay.IsClosing) return;   // open, or still fading out (a quick second press)
                // an overlay that closed without saying so (it kept every later open from happening until the game restarted)
                logger.Info("Kippix overlay: an old overlay window was left behind, cleared");
                var stale = overlay;
                overlay = null;
                try { stale.Abandon(); } catch (Exception) { }
            }
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long tShot = 0, tData = 0, tWin = 0;
                Config = OverlayConfig.Load(ConfigPath);
                if (Poller != null) Poller.OverlayHotkey = Config.OverlayHotkey ?? "";
                Discord.Configure(Config.DiscordClientId, Config.DiscordClientSecret);

                IntPtr fg = Native.GetForegroundWindow();
                if (IsGameWindowCandidate(fg) || (GameWindow == IntPtr.Zero && Native.GetProcessId(fg) != ownPid && !DiscordHelper.IsDiscordPid(Native.GetProcessId(fg))))
                    GameWindow = fg;

                // (0.99.56) the screen first (it's the moment you pressed), then the rest; the game's data from a minute ago is
                // used again, and Discord's state is the tracker's (looking for its process took a moment every open)
                GrabOpenShot();
                tShot = sw.ElapsedMilliseconds;
                ReloadGameDataIfOld();
                tData = sw.ElapsedMilliseconds;
                Stats.Start();

                string why;
                LetsGameRun = LetGameRun(out why);
                logger.Info("Kippix overlay: opening, " + (LetsGameRun ? "the game keeps running (keeps the focus; nothing is done to it)" : "the overlay takes the focus (games that pause when you switch away pause)") + " - " + why);

                // (0.99.20) the game as it is the moment the overlay was asked for: "Take a screenshot" saves this (straight
                // away, and exactly what was on screen), instead of closing the overlay and catching the game a moment later
                // (0.99.56: taken first, above)

                var w = new OverlayWindow(this, tab);
                tWin = sw.ElapsedMilliseconds;
                overlay = w;
                w.Closed += (s, e) =>
                {
                    if (ReferenceEquals(overlay, w)) { overlay = null; Stats.Stop(); DropOpenShot(); }
                    if (Config.OpenSound) Chime.PlayOpen();   // (0.99.37) the same light sound on the way out
                };
                w.ShowOverlay();
                overlayReadyAt = DateTime.UtcNow;
                logger.Info("Kippix overlay: shown " + sw.ElapsedMilliseconds + " ms after the press was let go (screen " + tShot + ", game data " + (tData - tShot) + ", panel " + (tWin - tData) + ", showing " + (sw.ElapsedMilliseconds - tWin) + " = " + w.ShowTimings + ")" + (guideDownAt != DateTime.MinValue ? "; the Guide button was down " + (int)(guideUpAt - guideDownAt).TotalMilliseconds + " ms" : ""));
                // (0.99.47) opened while an achievement pop-up was up: straight to that achievement
                try
                {
                    var pending = AchievementToasts.TakeForOverlay();
                    if (pending != null && tab == 0) { logger.Info("Kippix overlay: opened from the achievement pop-up: " + pending.Name); w.ShowAchievementFromToast(pending); }
                }
                catch (Exception ex) { logger.Warn("Kippix overlay: achievement from the pop-up: " + ex.Message); }
                if (Config.OpenSound) Chime.PlayOpen();   // (0.99.37) a light whoosh as it opens (Overlay settings › Sound when it opens or closes)
                // (0.99.30) a light tap on the controller as it opens (Overlay settings › Vibrate when it opens)
                if (Config.OpenVibration && Poller != null)
                {
                    // (0.99.35) soft by default (a light tick); Overlay settings › Vibration strength
                    int vs = Math.Max(0, Math.Min(2, Config.VibrationStrength));
                    Poller.Pulse(vs == 0 ? 50 : vs == 1 ? 60 : 70, vs == 2 ? 0.12 : 0.0, vs == 0 ? 0.22 : vs == 1 ? 0.4 : 0.6);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to open overlay");
                overlay = null;
                Stats.Stop();
            }
        }

        /// <summary>This time the overlay opened, the game keeps the focus (read by the window when it's made).</summary>
        internal bool LetsGameRun { get; private set; }

        /// <summary>
        /// When the overlay opens: does the game keep running (keep the focus, nothing done to it)? The setting, or for
        /// Automatic, whether Playnite lists the game as online multiplayer / MMO (features or genres).
        /// </summary>
        internal bool LetGameRun(out string why)
        {
            int mode = Config != null ? Config.WhenOpened : 0;
            if (mode == 1) { why = "setting: Let it keep running"; return true; }
            if (mode != 2) { why = "setting: Pause the game"; return false; }
            string online = OnlineSignOf(RunningGame);
            why = online != null ? "Automatic: online game (" + online + ")" : "Automatic: not an online game";
            return online != null;
        }

        /// <summary>The feature / genre that marks a game as played online ("Online PvP", "MMO", "Massively Multiplayer"…), or null.</summary>
        private static string OnlineSignOf(Game g)
        {
            if (g == null) return null;
            try
            {
                var names = new List<string>();
                if (g.Features != null) names.AddRange(g.Features.Where(f => f != null && f.Name != null).Select(f => f.Name));
                if (g.Genres != null) names.AddRange(g.Genres.Where(x => x != null && x.Name != null).Select(x => x.Name));
                foreach (var n in names)
                {
                    string l = n.ToLowerInvariant();
                    if (l.Contains("online") || l.StartsWith("mmo") || l.Contains(" mmo") || l.Contains("massively multiplayer")) return n;
                }
            }
            catch (Exception) { }
            return null;
        }

        internal void Resume()
        {
            Resume(null, 0);
        }

        /// <summary>Closes the overlay, refocuses the game, waits, then runs an action on a worker thread.</summary>
        internal void Resume(Action afterFocus, int delayMs)
        {
            CloseCompose();
            Action finish = () =>
            {
                // (0.99.59) off the screen's thread: a game busy getting back to full screen held Playnite (and the next Guide
                // press) up for seconds while it was given the focus back
                IntPtr gw = GameWindow;
                KippixShared.SafeWork.Queue(_ =>
                {
                    try { Native.ForceForeground(gw); } catch (Exception) { }
                    if (afterFocus != null) RunLater(afterFocus, delayMs);
                });
            };
            if (overlay != null) overlay.CloseOverlay(finish);
            else finish();
        }

        private void CloseOverlayThen(Action uiAction)
        {
            CloseCompose();
            if (overlay != null) overlay.CloseOverlay(uiAction);
            else if (uiAction != null) uiAction();
        }

        private void RunLater(Action work, int delayMs)
        {
            new Thread(() =>
            {
                if (delayMs > 0) Thread.Sleep(delayMs);
                try { work(); }
                catch (Exception ex) { logger.Error(ex, "Overlay action failed"); }
            }) { IsBackground = true }.Start();
        }

        private void OnUi(Action a)
        {
            if (dispatcher != null) dispatcher.BeginInvoke(a);
        }

        /// <summary>Re-reads config.json (it can be edited from the desktop settings page or by hand).</summary>
        private bool configReadFailed;

        /// <summary>The config from its file. If the file is there but can't be read (being written just then), the one in memory is kept and nothing is saved over the file.</summary>
        internal OverlayConfig ReloadConfig()
        {
            var c = OverlayConfig.TryLoad(ConfigPath);
            configReadFailed = c == null;
            if (c != null) Config = c;
            else if (Config == null) Config = new OverlayConfig();
            return Config;
        }

        internal void SaveConfig()
        {
            if (configReadFailed && !Started) return;   // defaults after a failed read: don't wipe your keys with them
            Config.Clamp();
            Config.Save(ConfigPath);
        }

        // ---------------- (0.99.54) the Guide button during a capture's notification: the preview ----------------

        private CapturePreviewWindow capturePreview;
        private string lastCaptureFile;
        private bool lastCaptureVideo;
        private Game lastCaptureGame;
        private DateTime lastCaptureUntil = DateTime.MinValue;
        private const double CaptureToastSeconds = 5;

        /// <summary>A screenshot was saved or a clip found: its notification, with Guide to see it (while it's up).</summary>
        internal void CaptureToast(string title, string file, bool isVideo, Game g)
        {
            lastCaptureFile = file;
            lastCaptureVideo = isVideo;
            lastCaptureGame = g;
            lastCaptureUntil = DateTime.Now.AddSeconds(CaptureToastSeconds + 0.4);
            bool ps = Poller != null && Poller.Kind == PadKind.PlayStation;
            string hint = (ps ? "PS" : "Guide") + " button: see it, crop " + (isVideo ? "or save a frame" : "it") + ", or delete it";
            // (0.99.55) a small picture of it on the left: read off the screen's thread first (a photo at a small size; a clip's
            // from Windows' thumbnail), then the notification; with nothing in a moment, it shows without one
            KippixHelper.ShellMedia.Run(() =>
            {
                System.Windows.Media.ImageSource pic = null;
                try
                {
                    if (!isVideo)
                    {
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bi.DecodePixelHeight = 180;
                        bi.UriSource = new Uri(file);
                        bi.EndInit();
                        bi.Freeze();
                        pic = bi;
                    }
                    else
                    {
                        var t = KippixHelper.ShellMedia.Thumbnail(file, 320);
                        if (t != null) { if (!t.IsFrozen && t.CanFreeze) t.Freeze(); pic = t; }
                    }
                }
                catch (Exception) { pic = null; }
                OnUi(() =>
                {
                    try { ToastWindow.ShowToast(title, Path.GetFileName(file), discordMode ? discordHwnd : GameWindow, Theme, CaptureToastSeconds, hint, pic, isVideo); }
                    catch (Exception ex) { logger.Error(ex, "Toast failed"); }
                });
            });
        }

        /// <summary>The Guide button now: the preview of what was just saved (true), or the overlay as usual.</summary>
        private bool OpenCapturePreviewIfDue()
        {
            if (capturePreview != null) return true;
            if (DateTime.Now > lastCaptureUntil || string.IsNullOrEmpty(lastCaptureFile) || !File.Exists(lastCaptureFile)) return false;
            lastCaptureUntil = DateTime.MinValue;
            try { ToastWindow.CloseCurrent(); } catch (Exception) { }
            try { IntPtr fg = Native.GetForegroundWindow(); if (GameWindow == IntPtr.Zero && IsGameWindowCandidate(fg)) GameWindow = fg; } catch (Exception) { }
            logger.Info("Kippix overlay: capture preview (Guide during its notification): " + lastCaptureFile);
            var w = new CapturePreviewWindow(this, lastCaptureFile, lastCaptureVideo, lastCaptureGame ?? RunningGame, GameWindow);
            capturePreview = w;
            w.Closed += (s, e) => { if (ReferenceEquals(capturePreview, w)) capturePreview = null; };
            w.ShowOnTop();
            if (Config.OpenVibration && Poller != null) Poller.Pulse(50, 0.0, 0.3);
            return true;
        }

        internal void Toast(string title, string message)
        {
            OnUi(() =>
            {
                try { ToastWindow.ShowToast(title, message, discordMode ? discordHwnd : GameWindow, Theme); }
                catch (Exception ex) { logger.Error(ex, "Toast failed"); }
            });
        }

        internal static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max - 1).TrimEnd() + "\u2026";
        }

        internal string GameName
        {
            get { return RunningGame != null ? RunningGame.Name : "Game"; }
        }

        // ================= Game info =================

        internal string CoverPath(Game g)
        {
            return ImagePath(g == null ? null : g.CoverImage);
        }

        internal string IconPath(Game g)
        {
            return ImagePath(g == null ? null : g.Icon);
        }

        private string ImagePath(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return id;
            try { return PlayniteApi.Database.GetFullFilePath(id); }
            catch (Exception) { return null; }
        }

        internal List<Game> RecentGames(int count)
        {
            try
            {
                Guid current = RunningGame != null ? RunningGame.Id : Guid.Empty;
                return PlayniteApi.Database.Games
                    .Where(g => g.LastActivity != null && g.Id != current && g.IsInstalled && !g.Hidden)
                    .OrderByDescending(g => g.LastActivity)
                    .Take(count)
                    .ToList();
            }
            catch (Exception) { return new List<Game>(); }
        }

        /// <summary>The theme's own achievement data (Playnite Achievements or SuccessStory, every achievement, locked ones too), when it has loaded.</summary>
        internal static Func<Guid, List<AchievementInfo>> MoreAchievements;
        /// <summary>A screenshot was saved / a clip was found while a game ran (the theme's gallery links it to the game).</summary>
        internal static Action<string, Game> CaptureSaved;

        /// <summary>SuccessStory first (or the theme's fuller list), then Steam's own local data for Steam games, then SuccessStory's files.</summary>
        internal List<AchievementInfo> LoadAchievementsFor(Game g, out string info)
        {
            var notes = new List<string>();
            string live;
            var list = SuccessStoryLive.Load(PlayniteApi, g.Id, out live);
            notes.Add("SuccessStory: " + live);
            // the theme's list as well: when it has more (the locked and hidden ones too), use it
            List<AchievementInfo> theme = null;
            try { if (MoreAchievements != null) theme = MoreAchievements(g.Id); } catch (Exception) { }
            if (theme != null && theme.Count > 0 && (list == null || theme.Count > list.Count))
            {
                notes.Add("Theme: " + theme.Count + " achievements (all of them, locked ones too)");
                // the theme's copy can be a little old: anything unlocked since (this session too) comes from the live list
                if (list != null && list.Count > 0)
                {
                    var live2 = new Dictionary<string, AchievementInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in list) if (a != null && a.Unlocked && !string.IsNullOrEmpty(a.Name)) live2[a.Name] = a;
                    foreach (var t in theme)
                    {
                        AchievementInfo l;
                        if (t != null && !t.Unlocked && t.Name != null && live2.TryGetValue(t.Name, out l)) { t.Unlocked = true; t.Date = l.Date; }
                    }
                }
                info = string.Join("\n", notes);
                return GameData.Sort(theme);
            }
            if (list != null && list.Count > 0 && list.Any(a => !a.Unlocked)) { info = string.Join("\n", notes); return list; }
            if (g.PluginId == SteamAchievements.SteamLibraryId && !string.IsNullOrEmpty(g.GameId))
            {
                // only unlocked ones from SuccessStory: Steam's own list has the locked ones too
                string st;
                var steamList = SteamAchievements.Load(SteamAchievements.SteamPath(), g.GameId, out st);
                if (steamList != null && list != null && steamList.Count > list.Count) { notes.Add("Steam: " + st); info = string.Join("\n", notes); return steamList; }
            }
            if (list != null && list.Count > 0) { info = string.Join("\n", notes); return list; }

            if (g.PluginId == SteamAchievements.SteamLibraryId && !string.IsNullOrEmpty(g.GameId))
            {
                string steam;
                list = SteamAchievements.Load(SteamAchievements.SteamPath(), g.GameId, out steam);
                notes.Add("Steam: " + steam);
                if (list != null && list.Count > 0) { info = string.Join("\n", notes); return list; }
            }

            list = GameData.LoadAchievements(PlayniteApi.Paths.ExtensionsDataPath, g.Id);
            notes.Add("Files: " + GameData.LastAchievementInfo);
            info = string.Join("\n", notes);
            return list;
        }

        /// <summary>"15 / 53 achievements", or null when SuccessStory has nothing for the game.</summary>
        internal string AchievementSummary(Game g)
        {
            lock (achievementSummaries)
            {
                string cached;
                if (achievementSummaries.TryGetValue(g.Id, out cached)) return cached;
            }
            string text = null;
            try
            {
                string info;
                var list = LoadAchievementsFor(g, out info);
                if (list != null && list.Count > 0) text = list.Count(a => a.Unlocked) + " / " + list.Count + " achievements";
            }
            catch (Exception) { }
            lock (achievementSummaries) achievementSummaries[g.Id] = text;
            return text;
        }

        // ================= Game actions =================

        private System.Drawing.Bitmap openShot;
        private DateTime openShotAt;

        private void GrabOpenShot()
        {
            DropOpenShot();
            try
            {
                if (Config != null && Config.ScreenshotHotkey() != null) return;   // (a screenshot hotkey: the game's own tool takes it)
                // (0.99.29) a notification still showing (the last "Screenshot saved") goes first, so it's never in the picture
                // (also left out of captures by the window itself on Windows 10 2004 and later: FloatingWindow)
                if (ToastWindow.CloseCurrent()) System.Threading.Thread.Sleep(50);   // (the window is gone at once; the screen catches up within a frame or two)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                openShot = Capture.GrabMonitor(GameWindow);
                openShotAt = DateTime.Now;
                if (sw.ElapsedMilliseconds > 120) logger.Info("Kippix overlay: the screen took " + sw.ElapsedMilliseconds + " ms to grab");
            }
            catch (Exception) { openShot = null; }
        }

        private void DropOpenShot()
        {
            var s = openShot;
            openShot = null;
            if (s != null) try { s.Dispose(); } catch (Exception) { }
        }

        internal void TakeScreenshot()
        {
            string hotkey = Config.ScreenshotHotkey();
            if (hotkey != null)
            {
                Resume(() => Native.SendHotkey(hotkey), 120);   // (0.99.20: 350 ms was a noticeable wait)
                return;
            }

            // (0.99.20) the moment the overlay was opened: saved straight away, exactly as it was on screen
            var shot = openShot;
            openShot = null;
            if (shot != null)
            {
                string f0 = Config.ScreenshotFolder, n0 = GameName;
                Game g0 = RunningGame;
                DateTime at = openShotAt;
                Resume();
                RunLater(() =>
                {
                    try
                    {
                        string file = Capture.SaveBitmap(shot, f0, n0, at);
                        if (Config.ScreenshotSound) Chime.Play();   // (0.99.30) a soft chime
                        CaptureToast("Screenshot saved", file, false, g0);
                        try { if (CaptureSaved != null) CaptureSaved(file, g0); } catch (Exception) { }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Screenshot failed");
                        Toast("Screenshot failed", ex.Message);
                    }
                    finally { try { shot.Dispose(); } catch (Exception) { } }
                }, 0);
                return;
            }

            string folder = Config.ScreenshotFolder;
            string name = GameName;
            Game game = RunningGame;
            IntPtr hwnd = GameWindow;
            Resume(() =>
            {
                try
                {
                    string file = Capture.SaveScreenshot(hwnd, folder, name);
                    if (Config.ScreenshotSound) Chime.Play();   // (0.99.30) a soft chime
                    CaptureToast("Screenshot saved", file, false, game);
                    try { if (CaptureSaved != null) CaptureSaved(file, game); } catch (Exception) { }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Screenshot failed");
                    Toast("Screenshot failed", ex.Message);
                }
            }, 150);
        }

        /// <summary>Asks NVIDIA Instant Replay (or Game Bar) to save the last few minutes, then checks a clip appeared.</summary>
        internal void SaveRecentGameplay()
        {
            string hotkey = Config.ReplayHotkey();
            if (hotkey == null) return;
            int method = Config.ReplayMethod;
            DateTime start = DateTime.Now.AddSeconds(-2);
            Game playing = RunningGame;
            Resume(() =>
            {
                if (!Native.SendHotkey(hotkey))
                {
                    Toast("Couldn't save gameplay", "The hotkey " + hotkey + " isn't valid.");
                    return;
                }
                string clip = null;
                for (int i = 0; i < 16 && clip == null; i++)
                {
                    Thread.Sleep(1500);
                    clip = NewestClipSince(start);
                }
                if (clip != null)
                {
                    CaptureToast("Clip saved", clip, true, playing);
                    try { if (CaptureSaved != null) CaptureSaved(clip, playing); } catch (Exception) { }
                    return;
                }
                string help = method == 0
                    ? "Couldn't find a new clip in Videos. In the NVIDIA App, switch on NVIDIA Overlay (Settings \u2192 Features), then Alt+Z \u2192 Record \u2192 Instant Replay \u2192 On."
                    : method == 1
                        ? "Couldn't find a new clip in Videos\\Captures. Turn on \"Record what happened\" in Windows Settings \u2192 Gaming \u2192 Captures."
                        : "Couldn't find a new clip in Videos. Check that " + hotkey + " is your recorder's save-replay hotkey.";
                Toast("No clip found", help);
            }, 350);
        }

        /// <summary>A video file created after 'since' in Videos (two folders deep, which covers NVIDIA\Game and Captures).</summary>
        private static string NewestClipSince(DateTime since)
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos) || !Directory.Exists(videos)) return null;
            var dirs = new List<string> { videos };
            try
            {
                foreach (string d in Directory.GetDirectories(videos))
                {
                    dirs.Add(d);
                    try { dirs.AddRange(Directory.GetDirectories(d)); } catch (Exception) { }
                }
            }
            catch (Exception) { }
            string best = null;
            DateTime bestTime = since;
            foreach (string d in dirs)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(d))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".mp4" && ext != ".mkv" && ext != ".mov") continue;
                        DateTime t = File.GetCreationTime(f);
                        if (t > bestTime) { bestTime = t; best = f; }
                    }
                }
                catch (Exception) { }
            }
            return best;
        }

        internal void OpenPlaynite()
        {
            CloseOverlayThen(BringPlayniteForward);
        }

        private void BringPlayniteForward()
        {
            try
            {
                Window mw = Application.Current.MainWindow;
                if (mw == null) return;
                mw.Show();
                if (mw.WindowState == WindowState.Minimized) mw.WindowState = WindowState.Normal;
                mw.Activate();
                Native.ForceForeground(new WindowInteropHelper(mw).Handle);
            }
            catch (Exception ex) { logger.Error(ex, "Open Playnite failed"); }
        }

        /// <summary>Force-closes the game. Returns false if its window couldn't be found.</summary>
        internal bool CloseGame()
        {
            uint pid = Native.GetProcessId(GameWindow);
            string name = GameName;
            CloseOverlayThen(null);
            if (pid == 0 || pid == ownPid || DiscordHelper.IsDiscordPid(pid))
            {
                Toast("Couldn't close the game", "Its window wasn't found. Try quitting from inside the game.");
                return false;
            }
            // (0.99.56) asked to close first (it saves its settings on the way out), forced only after 8 s
            Native.CloseGracefully(pid, 8000, gentle => logger.Info("Kippix overlay: Close game: " + name + (gentle ? " closed itself" : " didn't close in 8 s: forced")));
            Toast("Closing " + name, "");
            if (pendingSwitch == Guid.Empty)
                RunLater(() => OnUi(BringPlayniteForward), 1500);
            return true;
        }

        internal void SwitchTo(Game target)
        {
            pendingSwitch = target.Id;
            if (!CloseGame())
            {
                pendingSwitch = Guid.Empty;
                return;
            }
            Toast("Switching to " + target.Name, "");
            Guid id = target.Id;
            RunLater(() => OnUi(() =>
            {
                if (pendingSwitch == id)
                {
                    pendingSwitch = Guid.Empty;
                    StartGameSoon(id, 0);
                }
            }), 20000);
        }

        private void StartGameSoon(Guid id, int delayMs)
        {
            RunLater(() => OnUi(() =>
            {
                try { PlayniteApi.StartGame(id); }
                catch (Exception ex) { logger.Error(ex, "Start game failed"); }
            }), delayMs);
        }

        // ================= Discord: status & voice =================

        internal string DiscordStatus()
        {
            if (DiscordRunning)
                return Discord.Ready && Discord.Rpc.UserName != null ? "Discord is running  \u00B7  " + Discord.Rpc.UserName : "Discord is running";
            if ((DateTime.Now - discordStartRequested).TotalSeconds < 25) return "Starting Discord\u2026";
            return "Discord isn't running";
        }

        internal string DiscordConnectionText()
        {
            if (Discord.Ready) return "Connected as " + (Discord.Rpc.UserName ?? "you");
            if (!Discord.Rpc.Configured) return "Not set up";
            return Discord.Rpc.HasSavedLogin ? "Not connected right now" : "Ready to connect";
        }

        /// <summary>Changes here make live pages rebuild; anything else just refreshes text.</summary>
        internal string DiscordSignature()
        {
            if (Discord == null) return "";
            var sb = new StringBuilder();
            sb.Append(Discord.Ready).Append('|').Append(Discord.Muted).Append(Discord.Deafened).Append('|');
            var v = Discord.Voice;
            if (v != null)
            {
                sb.Append(v.ChannelId).Append(':');
                foreach (var m in Discord.MembersOf(v)) sb.Append(m.UserId).Append(m.Muted ? "m" : "").Append(m.Deafened ? "d" : "").Append(',');
            }
            var convs = Discord.Conversations();
            sb.Append('|').Append(convs.Count);
            if (convs.Count > 0) sb.Append(convs[0].ChannelId).Append(convs[0].Time.Ticks);
            return sb.ToString();
        }

        internal void StartDiscord()
        {
            if (DiscordRunning || DiscordHelper.IsRunning()) { DiscordRunning = true; return; }
            discordStartRequested = DateTime.Now;
            RunLater(() =>
            {
                DiscordHelper.StartInBackground();
                for (int i = 0; i < 40 && !DiscordRunning; i++)
                {
                    Thread.Sleep(500);
                    DiscordRunning = DiscordHelper.IsRunning();
                }
            }, 0);
        }

        internal bool MicMuted
        {
            get { return Discord != null && Discord.Ready ? Discord.Muted : hotkeyMuted; }
        }

        internal bool Deafened
        {
            get { return Discord != null && Discord.Ready ? Discord.Deafened : hotkeyDeafened; }
        }

        private bool RequireDiscord()
        {
            if (DiscordRunning) return true;
            Toast("Discord isn't running", "Choose Start Discord first.");
            return false;
        }

        internal void ToggleMute()
        {
            if (!RequireDiscord()) return;
            if (Discord.Ready)
            {
                bool target = !Discord.Muted;
                Discord.Muted = target;
                RunLater(() => { try { Discord.SetMute(target); } catch (Exception ex) { Toast("Couldn't change mute", ex.Message); } }, 0);
                return;
            }
            if (hotkeyDeafened && hotkeyMuted) hotkeyDeafened = false;
            hotkeyMuted = !hotkeyMuted;
            string key = Config.DiscordMuteHotkey;
            RunLater(() => Native.SendHotkey(key), 0);
        }

        internal void ToggleDeafen()
        {
            if (!RequireDiscord()) return;
            if (Discord.Ready)
            {
                bool target = !Discord.Deafened;
                Discord.Deafened = target;
                RunLater(() => { try { Discord.SetDeaf(target); } catch (Exception ex) { Toast("Couldn't change deafen", ex.Message); } }, 0);
                return;
            }
            if (!hotkeyDeafened)
            {
                mutedBeforeDeafen = hotkeyMuted;
                hotkeyDeafened = true;
                hotkeyMuted = true;
            }
            else
            {
                hotkeyDeafened = false;
                hotkeyMuted = mutedBeforeDeafen;
            }
            string key = Config.DiscordDeafenHotkey;
            RunLater(() => Native.SendHotkey(key), 0);
        }

        internal void LeaveCall()
        {
            if (!RequireDiscord()) return;
            if (Discord.Ready)
            {
                RunLater(() =>
                {
                    try { Discord.Leave(); Toast("Left the call", ""); }
                    catch (Exception ex) { Toast("Couldn't leave the call", ex.Message); }
                }, 0);
                return;
            }
            Toast("Leaving call\u2026", "");
            RunLater(() =>
            {
                if (DiscordHelper.LeaveCall()) Toast("Left the call", "");
                else Toast("No call found", "Open Discord to check your call.");
            }, 0);
        }

        /// <summary>First-time login: shows Discord so you can press Authorize, then comes back.</summary>
        internal void ConnectDiscord()
        {
            Config = OverlayConfig.Load(ConfigPath);
            if (Poller != null) Poller.OverlayHotkey = Config.OverlayHotkey ?? "";
            Discord.Configure(Config.DiscordClientId, Config.DiscordClientSecret);
            if (!Discord.Rpc.Configured)
            {
                Toast("Add your Discord app first", "Put the Client ID and Client Secret in the config file.");
                return;
            }
            if (Discord.Ready) { Toast("Discord is connected", Discord.Rpc.UserName ?? ""); return; }

            authorizing = true;
            EnterDiscordMode(null);
            Toast("Connecting to Discord\u2026", "If Discord asks, press Authorize.");
            RunLater(() =>
            {
                bool ok = false;
                for (int i = 0; i < 20 && !ok; i++)
                {
                    try { ok = Discord.Connect(true); } catch (Exception) { }
                    if (!ok && Discord.Rpc.LastError != "Discord isn't running" && i > 0) break;
                    if (!ok) Thread.Sleep(2000);
                }
                OnUi(() =>
                {
                    authorizing = false;
                    if (discordMode) ExitDiscordMode(RunningGame != null);
                    if (ok) Toast("Discord connected", "Signed in as " + (Discord.Rpc.UserName ?? "you"));
                    else Toast("Couldn't connect to Discord", Discord.Rpc.LastError);
                });
            }, 1500);
        }

        // ================= Discord: messages =================

        internal void SendDiscordMessage(string channelId, string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return;
            if (!Discord.Ready) { Toast("Discord isn't connected", ""); return; }
            Toast("Sending\u2026", Shorten(text, 80));
            RunLater(() =>
            {
                bool ok = false;
                string error = null;
                try
                {
                    ok = Discord.WaitForOwnMessage(channelId, text, () =>
                    {
                        // Open the chat in Discord (hidden behind the overlay) and type the message there.
                        Discord.SelectTextChannel(channelId);
                        IntPtr h = DiscordHelper.MainWindow();
                        if (h == IntPtr.Zero) h = DiscordHelper.ShowAndWait(null);
                        if (h == IntPtr.Zero) throw new Exception("Discord's window didn't open");
                        Thread.Sleep(300);
                        Native.ForceForeground(h);
                        Thread.Sleep(350);
                        if (Native.GetForegroundWindow() != h) { Native.ForceForeground(h); Thread.Sleep(300); }
                        Native.TypeText(text);
                        Thread.Sleep(150);
                        Native.TapKey(0x0D);
                    }, 6000);
                }
                catch (Exception ex) { error = ex.Message; }
                OnUi(() =>
                {
                    if (overlay != null && overlay.IsOpen) overlay.Refocus();
                    else if (!discordMode) Native.ForceForegroundSoon(GameWindow);
                });
                if (ok) Toast("Message sent", Shorten(text, 80));
                else Toast("Couldn't confirm it was sent", error ?? "Check Discord to be sure.");
            }, 0);
        }

        // ================= Compose keyboard =================

        internal bool ComposeOpen
        {
            get { return compose != null; }
        }

        internal void Compose(string title, Action<string> onSubmit)
        {
            CloseCompose();
            try
            {
                compose = new KeyboardWindow(GameWindow, Theme, Poller.Kind, title, "");
                compose.OnSubmit = text =>
                {
                    CloseCompose();
                    onSubmit(text);
                };
                compose.OnClose = CloseCompose;
                compose.Show();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Compose keyboard failed");
                compose = null;
            }
        }

        internal void ComposeType(string text) { if (compose != null) compose.AddText(text); }
        internal void ComposeBackspace() { if (compose != null) compose.Backspace(); }
        internal void ComposeSubmit() { if (compose != null) compose.Submit(); }
        internal void ComposePress(ushort pressed) { if (compose != null && pressed != 0) compose.HandlePress(pressed); }

        internal void CloseCompose()
        {
            var k = compose;
            bool game = gameKeyboard;
            compose = null;
            gameKeyboard = false;
            if (k != null) { try { k.Close(); } catch (Exception) { } }
            if (game && k != null) Resume();   // the game keyboard put away: straight back to the game
        }

        // ================= Keyboard for the game =================

        /// <summary>Set while the overlay's Keyboard is up (the overlay itself hidden behind it).</summary>
        private bool gameKeyboard;

        /// <summary>
        /// Overlay › Keyboard: the overlay hides and an on-screen keyboard comes up over the game. Start (or the Type key)
        /// goes back to the game and types what's written into it (into whatever box the game has selected); B puts the
        /// keyboard away and goes back to the game without typing anything.
        /// </summary>
        internal void OpenGameKeyboard()
        {
            if (overlay == null || !overlay.IsOpen) return;
            CloseCompose();
            KeyboardWindow k = null;
            try
            {
                k = new KeyboardWindow(GameWindow, Theme, Poller != null ? Poller.Kind : PadKind.Xbox, "Type into " + (GameName ?? "the game"), "", "type it");
                var me = k;
                k.OnSubmit = text =>
                {
                    if (!ReferenceEquals(compose, me)) return;
                    gameKeyboard = false;   // (closed here, then back to the game with the text)
                    CloseCompose();
                    if (string.IsNullOrEmpty(text)) Resume();
                    else Resume(() => Native.TypeText(text), 250);
                };
                k.OnClose = () => { if (ReferenceEquals(compose, me)) CloseCompose(); };
                compose = k;
                gameKeyboard = true;
                overlay.HideForKeyboard(true);
                k.Show();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Game keyboard failed");
                if (ReferenceEquals(compose, k)) { compose = null; gameKeyboard = false; }
                if (overlay != null) overlay.HideForKeyboard(false);
            }
        }

        // ================= Discord controller mode =================

        /// <summary>Puts Discord in front of the game and lets the controller drive it like a mouse.</summary>
        internal void EnterDiscordMode(string uri)
        {
            CloseOverlayThen(() =>
            {
                if (!DiscordRunning) discordStartRequested = DateTime.Now;
                Toast("Opening Discord\u2026", "");
                RunLater(() =>
                {
                    IntPtr h = DiscordHelper.ShowAndWait(uri);
                    OnUi(() => BeginDiscordMode(h));
                }, 0);
            });
        }

        private void BeginDiscordMode(IntPtr h)
        {
            if (h == IntPtr.Zero)
            {
                Toast("Discord didn't open", "Is the Discord app installed?");
                Native.ForceForegroundSoon(GameWindow);
                return;
            }
            DiscordRunning = true;
            // already in controller mode (opened twice): the old watcher and hint bar go first
            if (focusWatcher != null) { focusWatcher.Dispose(); focusWatcher = null; }
            if (hintBar != null) { try { hintBar.Close(); } catch (Exception) { } hintBar = null; }
            discordHwnd = h;
            discordMode = true;
            dismissedKey = null;

            Native.SetTopmost(h, true);
            if (Config.MaximizeDiscord) Native.ShowWindow(h, 3);
            Native.ForceForeground(h);

            try
            {
                hintBar = new HintBar(h, Theme, Poller.Kind);
                hintBar.Show();
            }
            catch (Exception ex) { logger.Error(ex, "Hint bar failed"); }

            Poller.MouseMode = true;
            focusWatcher = new System.Threading.Timer(_ => WatchFocus(), null, 800, 600);
        }

        private void ExitDiscordMode(bool reopenOverlay)
        {
            discordMode = false;
            Poller.MouseMode = false;
            if ((lastHeld & Btn.A) != 0) Native.mouse_event(Native.MOUSE_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            if ((lastHeld & Btn.X) != 0) Native.mouse_event(Native.MOUSE_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
            if (focusWatcher != null) { focusWatcher.Dispose(); focusWatcher = null; }
            HideKeyboard(false);
            if (hintBar != null) { try { hintBar.Close(); } catch (Exception) { } hintBar = null; }
            Native.SetTopmost(discordHwnd, false);
            if (reopenOverlay && RunningGame != null) OpenOverlay(1);
            else Native.ForceForegroundSoon(GameWindow);
        }

        private void HandleDiscordMode(ushort pressed, ushort released)
        {
            if ((pressed & Btn.Guide) != 0) { ExitDiscordMode(true); return; }

            if (keyboard != null)
            {
                keyboard.HandlePress(pressed);
                return;
            }

            if ((pressed & Btn.A) != 0) Native.mouse_event(Native.MOUSE_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            if ((released & Btn.A) != 0) Native.mouse_event(Native.MOUSE_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            if ((pressed & Btn.X) != 0) Native.mouse_event(Native.MOUSE_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            if ((released & Btn.X) != 0) Native.mouse_event(Native.MOUSE_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
            if ((pressed & Btn.B) != 0) Native.TapKey(0x1B);
            if ((pressed & Btn.Start) != 0) Native.TapKey(0x0D);
            if ((pressed & Btn.Up) != 0) Native.TapKey(0x26);
            if ((pressed & Btn.Down) != 0) Native.TapKey(0x28);
            if ((pressed & Btn.Left) != 0) Native.TapKey(0x25);
            if ((pressed & Btn.Right) != 0) Native.TapKey(0x27);
            if ((pressed & Btn.Y) != 0) ShowKeyboard();
        }

        private void ShowKeyboard()
        {
            if (keyboard != null || !discordMode) return;
            try
            {
                keyboard = new KeyboardWindow(discordHwnd, Theme, Poller.Kind);
                keyboard.OnClose = () => HideKeyboard(true);
                keyboard.Show();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Keyboard failed");
                keyboard = null;
            }
        }

        private void HideKeyboard(bool byUser)
        {
            if (byUser) dismissedKey = lastFocusKey;
            if (keyboard == null) return;
            try { keyboard.Close(); } catch (Exception) { }
            keyboard = null;
        }

        private void WatchFocus()
        {
            if (!discordMode || focusBusy) return;
            focusBusy = true;
            try
            {
                string key = DiscordHelper.FocusedTextBoxKey();
                OnUi(() =>
                {
                    lastFocusKey = key;
                    if (key == null) { dismissedKey = null; return; }
                    if (discordMode && keyboard == null && key != dismissedKey) ShowKeyboard();
                });
            }
            finally { focusBusy = false; }
        }

        // ================= Achievement guides =================

        public ISettings GetSettings(bool firstRunSettings)
        {
            if (Guides == null) Guides = new GuideStore(Path.Combine(GetPluginUserDataPath(), "guides"));
            settings = new GuideSettings(this);
            return settings;
        }

        public System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new GuideSettingsView(this, settings ?? (settings = new GuideSettings(this)));
        }

        public IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                MenuSection = "Kippix",
                Description = "Download achievement guides",
                Action = a => DownloadGuidesForGames(a.Games, false)
            };
            yield return new GameMenuItem
            {
                MenuSection = "Kippix",
                Description = "Redownload achievement guides",
                Action = a => DownloadGuidesForGames(a.Games, true)
            };
            yield return new GameMenuItem
            {
                MenuSection = "Kippix",
                Description = "Delete achievement guides",
                Action = a => DeleteGuidesForGames(a.Games)
            };
        }

        /// <summary>The game menu and desktop settings work even when the overlay itself is switched off (no store made at start).</summary>
        private void EnsureGuides()
        {
            if (Guides == null) Guides = new GuideStore(Path.Combine(GetPluginUserDataPath(), "guides"));
        }

        /// <summary>Removes the saved tips for these games (after asking).</summary>
        internal void DeleteGuidesForGames(List<Game> games)
        {
            if (games == null || games.Count == 0) return;
            EnsureGuides();
            var with = games.Where(g => g != null && Guides.Has(g.Id)).ToList();
            if (with.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(games.Count == 1 ? "This game doesn't have any saved achievement guides." : "None of those games have saved achievement guides.", "Kippix");
                return;
            }
            string question = with.Count == 1
                ? "Delete the achievement guides for " + with[0].Name + "?"
                : "Delete the achievement guides for " + with.Count + " games?";
            if (PlayniteApi.Dialogs.ShowMessage(question + "\n\nYou can download them again later.", "Kippix",
                    System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes) return;
            int n = 0;
            foreach (var g in with) if (Guides.Delete(g.Id)) n++;
            OverlayBridge.RaiseChanged();
            Toast("Achievement guides", n == 1 ? "Deleted the guides for " + with[0].Name : "Deleted the guides for " + n + " games");
        }

        private string PlatformOf(Game g)
        {
            try
            {
                if (g != null && g.Platforms != null && g.Platforms.Count > 0 && g.Platforms[0] != null) return g.Platforms[0].Name;
            }
            catch (Exception) { }
            return null;
        }

        private bool CheckGuideSetup()
        {
            var c = ReloadConfig();
            if (c.GuidesReady) return true;
            PlayniteApi.Dialogs.ShowMessage("Choose an AI provider and add its API key first:\nAdd-ons \u2192 Extension settings \u2192 Kippix Helper \u2192 Overlay tab.", "Kippix");
            return false;
        }

        internal void DownloadGuidesForLibrary(bool force)
        {
            List<Game> all;
            try { all = PlayniteApi.Database.Games.Where(g => !g.Hidden).ToList(); }
            catch (Exception) { all = new List<Game>(); }
            DownloadGuidesForGames(all, force);
        }

        internal void DownloadGuidesForSelected(bool force)
        {
            List<Game> selected = null;
            try { selected = PlayniteApi.MainView.SelectedGames.ToList(); } catch (Exception) { }
            if (selected == null || selected.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Select one or more games in your library first.", "Kippix");
                return;
            }
            DownloadGuidesForGames(selected, force);
        }

        /// <summary>Desktop-mode download with Playnite's progress window, a cost estimate and the spending cap.</summary>
        /// <summary>A failure's reason for the summary box: one line, never the AI's whole answer.</summary>
        private static string ShortReason(Exception ex)
        {
            if (ex is ArgumentException && ex.Message.IndexOf("Invalid object passed in", StringComparison.Ordinal) >= 0)
                return "the AI's answer was garbled. Try again.";
            string m = (ex.Message ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return m.Length > 200 ? m.Substring(0, 200) + "\u2026" : m;
        }

        internal void DownloadGuidesForGames(List<Game> games, bool force)
        {
            if (games == null || games.Count == 0 || !CheckGuideSetup()) return;
            EnsureGuides();
            var c = Config;

            // 1. Find which games have achievements (and don't have guides yet).
            var work = new List<KeyValuePair<Game, List<AchievementInfo>>>();
            PlayniteApi.Dialogs.ActivateGlobalProgress(p =>
            {
                p.ProgressMaxValue = games.Count;
                int i = 0;
                foreach (Game g in games)
                {
                    if (p.CancelToken.IsCancellationRequested) return;
                    p.CurrentProgressValue = ++i;
                    p.Text = "Checking achievements: " + g.Name;
                    string info;
                    List<AchievementInfo> ach = null;
                    try { ach = LoadAchievementsFor(g, out info); } catch (Exception) { }
                    // Only the achievements still missing a tip, so a game cut short by the daily limit is finished next time.
                    if (!force) ach = MissingGuides(g, ach, false);
                    if (ach != null && ach.Count > 0) work.Add(new KeyValuePair<Game, List<AchievementInfo>>(g, ach));
                }
            }, new GlobalProgressOptions("Checking your games\u2026", true) { IsIndeterminate = false });

            if (work.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(force ? "None of those games have achievements." : "Nothing to download: those games either have no achievements or already have guides.", "Kippix");
                return;
            }

            // 2. Confirm, with a cost estimate for OpenAI.
            int total = work.Sum(w => w.Value.Count);
            string question;
            if (c.GuideProvider == 1)
            {
                double est = work.Sum(w => GuideWriter.EstimateUsd(c, w.Value.Count));
                question = "Download guides for " + work.Count + " games (" + total + " achievements)?\n\n" +
                           "Estimated OpenAI cost: about $" + est.ToString("0.00") + " (rough estimate)." +
                           (c.GuideSpendCapUsd > 0 ? "\nIt will stop if it reaches your cap of $" + c.GuideSpendCapUsd.ToString("0.00") + "." : "");
            }
            else if (c.GuideProvider == 3)
            {
                question = "Download guides for " + work.Count + " games (" + total + " achievements) with Claude Code?\n\n" +
                           "This uses your Claude plan's usage allowance. If its limit is reached, the download stops and keeps everything so far. Run it again later and it finishes the rest.";
            }
            else
            {
                question = "Download guides for " + work.Count + " games (" + total + " achievements) with Gemini's free tier?\n\n" +
                           "The free tier has a daily limit. If it is reached, the download stops and keeps everything so far. Run it again the next day and it finishes the rest.";
            }
            if (PlayniteApi.Dialogs.ShowMessage(question, "Kippix", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            // 3. Download.
            int done = 0, failed = 0;
            double spent = 0;
            string stopReason = null, firstError = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(p =>
            {
                p.ProgressMaxValue = work.Count;
                int i = 0;
                foreach (var w in work)
                {
                    if (p.CancelToken.IsCancellationRequested) { stopReason = "Cancelled."; break; }
                    if (c.GuideSpendCapUsd > 0 && spent >= c.GuideSpendCapUsd) { stopReason = "Stopped at your spending cap."; break; }
                    p.CurrentProgressValue = i++;
                    Game g = w.Key;
                    try
                    {
                        string name = g.Name;
                        spent += GuideWriter.Write(c, Guides, g.Id, name, PlatformOf(g), w.Value,
                            () => p.CancelToken.IsCancellationRequested,
                            t => p.Text = "(" + i + "/" + work.Count + ") " + name + ": " + t,
                            c.GuideSpendCapUsd > 0 ? c.GuideSpendCapUsd - spent : 0);
                        done++;
                    }
                    catch (GuideQuotaException ex) { stopReason = ex.Message; break; }
                    catch (Exception ex)
                    {
                        failed++;
                        if (firstError == null) firstError = g.Name + ": " + ShortReason(ex);
                        logger.Error(ex, "Guide download failed for " + g.Name);
                        if (failed >= 3 && done == 0) break;
                    }
                }
                p.CurrentProgressValue = work.Count;
            }, new GlobalProgressOptions("Downloading achievement guides\u2026", true) { IsIndeterminate = false });

            var cfg = ReloadConfig();
            cfg.GuideSpentUsd += spent;
            SaveConfig();

            string summary = "Guides downloaded for " + done + " of " + work.Count + " games." +
                (failed > 0 ? "\n" + failed + " failed. Reason: " + firstError : "") +
                (c.GuideProvider == 1 ? "\nEstimated cost: $" + spent.ToString("0.00") + "." : "") +
                (stopReason != null ? "\n\n" + stopReason : "");
            PlayniteApi.Dialogs.ShowMessage(summary, "Kippix");
        }

        internal bool IsDownloadingGuides(Guid gameId)
        {
            lock (guideDownloads) return guideDownloads.Contains(gameId);
        }

        /// <summary>The achievements that don't have a saved tip yet.</summary>
        private List<AchievementInfo> MissingGuides(Game g, List<AchievementInfo> ach, bool skipTried)
        {
            var result = new List<AchievementInfo>();
            if (ach == null) return result;
            GameGuides saved = Guides.Load(g.Id);
            foreach (var a in ach)
            {
                if (string.IsNullOrEmpty(a.Name)) continue;
                string key = GuideStore.Key(a.Name);
                if (saved != null && saved.Items != null && saved.Items.ContainsKey(key)) continue;
                if (skipTried && saved != null && saved.Skipped != null && saved.Skipped.Contains(key)) continue;
                result.Add(a);
            }
            return result;
        }

        /// <summary>Downloads guides for one game in the background (from the overlay or when a game starts).</summary>
        internal void DownloadGuidesBackground(Game g, bool force, Action<string> done)
        {
            if (g == null) return;
            EnsureGuides();
            lock (guideDownloads)
            {
                if (guideDownloads.Contains(g.Id)) return;
                guideDownloads.Add(g.Id);
            }
            RunLater(() =>
            {
                string result;
                try
                {
                    var c = ReloadConfig();
                    if (!c.GuidesReady) result = "Set up an AI provider in the add-on settings first.";
                    else
                    {
                        string info;
                        var ach = LoadAchievementsFor(g, out info);
                        if (ach == null || ach.Count == 0) result = "This game has no achievements to write guides for.";
                        else if (!force && (ach = MissingGuides(g, ach, true)).Count == 0) result = null;
                        else
                        {
                            GameGuides before = Guides.Load(g.Id);
                            int had = before != null && before.Items != null ? before.Items.Count : 0;
                            double cost = GuideWriter.Write(c, Guides, g.Id, g.Name, PlatformOf(g), ach, null, null, c.GuideSpendCapUsd);
                            var cfg = ReloadConfig();
                            cfg.GuideSpentUsd += cost;
                            SaveConfig();
                            result = null;
                            GameGuides after = Guides.Load(g.Id);
                            int now = after != null && after.Items != null ? after.Items.Count : 0;
                            // (0.99.40) on its own (a game starting): only when the game got its guides for the first time
                            if (done != null || (had == 0 && now > 0)) Toast("Achievement guides ready", g.Name);
                            else logger.Info("Kippix: achievement guides for " + g.Name + ": " + (now - had) + " tips added");
                        }
                    }
                }
                catch (GuideQuotaException ex)
                {
                    result = ex.Message;
                    PauseAutoGuides(ex.Message);
                    if (done != null) Toast("Couldn't get achievement guides", ex.Message);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Background guide download failed");
                    result = ex.Message;
                    if (done != null) Toast("Couldn't get achievement guides", ex.Message);
                }
                lock (guideDownloads) guideDownloads.Remove(g.Id);
                if (done != null) OnUi(() => done(result));
            }, 0);
        }

        // ================= Library update (startup / overlay) =================

        private volatile bool guideUpdateRunning;
        private volatile string guideUpdateStatus;

        internal bool GuideUpdateRunning { get { return guideUpdateRunning; } }

        /// <summary>What the overlay shows under "Update achievement guides".</summary>
        internal string GuideUpdateStatus
        {
            get
            {
                if (!Config.GuidesReady) return "Set up an AI provider in Playnite desktop first";
                string s = guideUpdateStatus;
                if (!string.IsNullOrEmpty(s)) return s;
                return "Gets tips for " + (Config.GuideUpdateInstalledOnly ? "installed games" : "games") + " that don't have them yet";
            }
        }

        /// <summary>
        /// Quietly writes tips for every game (or installed game) with achievements still missing one.
        /// Runs in the background; call from the UI thread. With manual=false it only speaks up if it did something.
        /// </summary>
        internal void StartGuideUpdate(bool manual)
        {
            var c = ReloadConfig();
            if (!c.GuidesReady)
            {
                if (manual) Toast("Achievement guides", "Set up an AI provider first: Playnite desktop \u2192 Add-ons \u2192 Extension settings \u2192 Kippix Helper \u2192 Overlay tab.");
                return;
            }
            if (guideUpdateRunning) return;
            if (!manual && GuideAutoPaused(c)) { logger.Info("Kippix: achievement guides: start-up update skipped (the AI's limit was reached; waiting until " + new DateTime(c.GuideAutoPausedUntil, DateTimeKind.Utc).ToLocalTime().ToString("ddd HH:mm") + ")"); return; }
            guideUpdateRunning = true;

            List<Game> games;
            try
            {
                bool installedOnly = c.GuideUpdateInstalledOnly;
                games = PlayniteApi.Database.Games.Where(g => !g.Hidden && (!installedOnly || g.IsInstalled)).ToList();
            }
            catch (Exception) { games = new List<Game>(); }

            guideUpdateStatus = "Checking " + games.Count + " games\u2026";
            RunLater(() =>
            {
                int gamesDone = 0, tips = 0, failed = 0;
                var firstTime = new List<string>();   // (0.99.40) games that got their guides for the first time
                double spent = 0;
                string stop = null;
                double cap = c.GuideProvider == 1 ? c.GuideSpendCapUsd : 0;
                try
                {
                    int i = 0;
                    foreach (Game g in games)
                    {
                        i++;
                        if (cap > 0 && spent >= cap) { stop = "Stopped at your spending cap."; break; }
                        lock (guideDownloads) { if (guideDownloads.Contains(g.Id)) continue; }

                        string info;
                        List<AchievementInfo> ach = null;
                        try { ach = LoadAchievementsFor(g, out info); } catch (Exception) { }
                        var missing = MissingGuides(g, ach, true);
                        if (missing.Count == 0) continue;

                        guideUpdateStatus = "Writing tips for " + g.Name + "  (" + i + " of " + games.Count + ")";
                        GameGuides before = Guides.Load(g.Id);
                        int had = before != null && before.Items != null ? before.Items.Count : 0;
                        try
                        {
                            spent += GuideWriter.Write(c, Guides, g.Id, g.Name, PlatformOf(g), missing, null, null, cap > 0 ? cap - spent : 0);
                            GameGuides after = Guides.Load(g.Id);
                            int now = after != null && after.Items != null ? after.Items.Count : 0;
                            if (now > had) { gamesDone++; tips += now - had; if (had == 0) firstTime.Add(g.Name); }
                        }
                        catch (GuideQuotaException ex) { stop = ex.Message; PauseAutoGuides(ex.Message); break; }
                        catch (Exception ex)
                        {
                            failed++;
                            logger.Error(ex, "Guide update failed for " + g.Name);
                            if (failed >= 3 && gamesDone == 0) { stop = ex.Message; break; }
                        }
                    }
                }
                catch (Exception ex) { stop = ex.Message; logger.Error(ex, "Guide update failed"); }

                if (spent > 0)
                {
                    var cfg = ReloadConfig();
                    cfg.GuideSpentUsd += spent;
                    SaveConfig();
                }

                string summary = gamesDone > 0
                    ? "Added " + tips + " tips for " + gamesDone + (gamesDone == 1 ? " game" : " games")
                    : stop != null ? "No new tips" : "Every game is up to date";
                guideUpdateStatus = (stop != null ? summary + ". " + stop : summary) + "  \u00B7  " + DateTime.Now.ToString("HH:mm");
                guideUpdateRunning = false;
                if (stop == null && gamesDone > 0) { try { var pc = ReloadConfig(); if (pc.GuideAutoPausedUntil != 0) { pc.GuideAutoPausedUntil = 0; SaveConfig(); } } catch (Exception) { } }   // (working again)
                logger.Info("Kippix: achievement guides " + (manual ? "update" : "start-up update") + ": " + summary + (stop != null ? " (" + stop + ")" : ""));
                // (0.99.40) on its own (at start-up): only when a game got its guides for the first time, never "up to date" or a
                // limit message (Playnite starts again on every desktop / fullscreen switch, so it was showing every few minutes)
                if (manual)
                    Toast("Achievement guides", stop != null ? summary + ". " + stop : summary + ".");
                else if (firstTime.Count > 0)
                    Toast("Achievement guides ready", firstTime.Count == 1 ? firstTime[0]
                        : firstTime[0] + (firstTime.Count == 2 ? " and " + firstTime[1] : " and " + (firstTime.Count - 1) + " more games"));
            }, 0);
        }

        /// <summary>(0.99.40) The AI's limit was hit a while ago: the automatic guide updates wait (a manual one still runs).</summary>
        private static bool GuideAutoPaused(OverlayConfig c)
        {
            return c != null && c.GuideAutoPausedUntil > DateTime.UtcNow.Ticks;
        }

        /// <summary>(0.99.40) The AI said no (free daily limit, credit, too many at once): no automatic tries until it has reset.</summary>
        private void PauseAutoGuides(string why)
        {
            try
            {
                var c = ReloadConfig();
                DateTime until;
                if (c.GuideProvider == 1 && why != null && why.Contains("too much at once")) until = DateTime.UtcNow.AddMinutes(30);
                else if (c.GuideProvider == 1) until = DateTime.UtcNow.AddHours(12);
                else if (c.GuideProvider == 3) until = DateTime.UtcNow.AddHours(5);   // (a Claude plan's allowance comes back within about five hours)
                else
                {
                    // Gemini's free limit resets at midnight Pacific time (about 8am UK)
                    until = DateTime.UtcNow.AddHours(12);
                    try
                    {
                        var pt = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                        var nowPt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pt);
                        until = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nowPt.Date.AddDays(1).AddMinutes(10), DateTimeKind.Unspecified), pt);
                    }
                    catch (Exception) { }
                }
                c.GuideAutoPausedUntil = until.Ticks;
                SaveConfig();
                logger.Info("Kippix: achievement guides: the AI's limit was reached (" + why + "); automatic updates wait until " + until.ToLocalTime().ToString("ddd HH:mm"));
            }
            catch (Exception) { }
        }

        // ================= Fill-in file (export / import) =================

        /// <summary>scope: 0 all games, 1 installed games, 2 games selected in the library.</summary>
        internal void ExportGuideFile(int scope, bool missingOnly)
        {
            EnsureGuides();
            List<Game> games;
            try
            {
                if (scope == 2) games = PlayniteApi.MainView.SelectedGames.ToList();
                else games = PlayniteApi.Database.Games.Where(g => !g.Hidden && (scope == 0 || g.IsInstalled)).ToList();
            }
            catch (Exception) { games = new List<Game>(); }
            if (games.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(scope == 2 ? "Select one or more games in your library first." : "No games to export.", "Kippix");
                return;
            }

            var work = new List<KeyValuePair<Game, List<AchievementInfo>>>();
            PlayniteApi.Dialogs.ActivateGlobalProgress(p =>
            {
                p.ProgressMaxValue = games.Count;
                int i = 0;
                foreach (Game g in games)
                {
                    if (p.CancelToken.IsCancellationRequested) return;
                    p.CurrentProgressValue = ++i;
                    p.Text = "Reading achievements: " + g.Name;
                    string info;
                    List<AchievementInfo> ach = null;
                    try { ach = LoadAchievementsFor(g, out info); } catch (Exception) { }
                    if (missingOnly) ach = MissingGuides(g, ach, false);
                    if (ach != null && ach.Count > 0) work.Add(new KeyValuePair<Game, List<AchievementInfo>>(g, ach));
                }
            }, new GlobalProgressOptions("Reading your games\u2026", true) { IsIndeterminate = false });

            if (work.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(missingOnly ? "Nothing to export: every achievement in those games already has a tip (or the games have no achievements)." : "None of those games have achievements.", "Kippix");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save achievement list",
                Filter = "JSON file (*.json)|*.json",
                FileName = (work.Count == 1 ? Capture.SafeName(work[0].Key.Name) + " achievements" : "Achievements to fill in") + ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog() != true) return;

            GuideFile file = GuideFiles.Build(work, Guides, PlatformOf);
            try { File.WriteAllText(dlg.FileName, GuideFiles.ToJson(file), new UTF8Encoding(false)); }
            catch (Exception ex)
            {
                logger.Error(ex, "Guide export failed");
                PlayniteApi.Dialogs.ShowMessage("Couldn't save that file.\n\n" + ex.Message, "Kippix");
                return;
            }
            int total = file.games.Sum(g => g.achievements.Count);

            bool copied = false;
            try { Clipboard.SetText(GuideFiles.ChatPrompt); copied = true; } catch (Exception) { }

            PlayniteApi.Dialogs.ShowMessage(
                "Saved " + total + " achievements from " + file.games.Count + (file.games.Count == 1 ? " game" : " games") + " to:\n" + dlg.FileName + "\n\n" +
                "Upload it to ChatGPT" + (copied ? " and paste the message that's now on your clipboard" : "") + ". " +
                "When it gives you the filled-in file back, download it and choose \"Import filled-in file\" here.\n\n" +
                (total > 300 ? "Tip: that's a big list. ChatGPT does a better job with a few games (a couple of hundred achievements) per file, so you may want to export smaller batches using \"Games selected in the library\"." : ""),
                "Kippix");
        }

        internal void ImportGuideFile()
        {
            EnsureGuides();
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import filled-in achievement file",
                Filter = "Achievement file (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                string summary = GuideFiles.Import(File.ReadAllText(dlg.FileName), Guides, PlayniteApi.Database.Games.ToList());
                PlayniteApi.Dialogs.ShowMessage(summary, "Kippix");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Guide import failed");
                PlayniteApi.Dialogs.ShowMessage("Couldn't import that file.\n\n" + ex.Message, "Kippix");
            }
        }

        /// <summary>Rewrites the tip for one achievement.</summary>
        internal void RegenerateGuide(Game g, AchievementInfo a, Action<string> done)
        {
            RunLater(() =>
            {
                string result = null;
                try
                {
                    var c = ReloadConfig();
                    if (!c.GuidesReady) result = "Set up an AI provider in the add-on settings first.";
                    else
                    {
                        double cost = GuideWriter.Write(c, Guides, g.Id, g.Name, PlatformOf(g), new List<AchievementInfo> { a }, null, null, 0);
                        var cfg = ReloadConfig();
                        cfg.GuideSpentUsd += cost;
                        SaveConfig();
                    }
                }
                catch (Exception ex) { result = ex.Message; }
                if (done != null) OnUi(() => done(result));
            }, 0);
        }

        // ================= System =================

        internal List<DriveInfo> Drives()
        {
            var result = new List<DriveInfo>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.TotalSize > 0)
                            result.Add(d);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return result;
        }

        internal void Hibernate()
        {
            CloseOverlayThen(null);
            RunLater(() =>
            {
                if (!Native.SetSuspendState(true, false, false))
                    Native.RunHidden("shutdown", "/h");
            }, 600);
        }

        internal void RestartPc()
        {
            CloseOverlayThen(null);
            Native.RunHidden("shutdown", "/r /t 0");
        }

        internal void OpenConfigFile()
        {
            SaveConfig();
            string path = ConfigPath;
            CloseOverlayThen(() =>
            {
                try { using (Process.Start("notepad.exe", "\"" + path + "\"")) { } }
                catch (Exception ex) { logger.Error(ex, "Open config failed"); }
            });
        }
    }
}
