using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace KippixHelper
{
    /// <summary>
    /// Everything the Kippix theme binds to: {PluginSettings Plugin=Kippix, Path=...}.
    /// Rule: anything the theme uses for Visibility must be a bool (Playnite converts it automatically).
    /// Split over ThemeApi.cs (shortcuts, options, panels), .Status.cs, .Discord.cs, .Achievements.cs and .AchDetail.cs.
    /// </summary>
    public partial class ThemeApi : Observable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly HelperPlugin plugin;
        private readonly IPlayniteAPI api;
        private readonly OverlayLink overlay = new OverlayLink();
        private HelperSettings settings = new HelperSettings();
        private DispatcherTimer timer;
        private Dispatcher ui;
        private int ticks;

        private static readonly Brush GreenBrush = Frozen(Color.FromRgb(0x4A, 0xDE, 0x80));
        private static readonly Brush WhiteBrush = Frozen(Colors.White);
        private static readonly Brush AmberBrush = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));
        private static readonly Brush RedBrush = Frozen(Color.FromRgb(0xFF, 0x6B, 0x6B));
        private static readonly Brush PinkBrush = Frozen(Color.FromRgb(0xFF, 0x4D, 0x6A));
        private static readonly Brush DarkBrush = Frozen(Color.FromRgb(0x0D, 0x0F, 0x12));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public ThemeApi(HelperPlugin plugin, IPlayniteAPI api)
        {
            this.plugin = plugin;
            this.api = api;
            HookDetBlanks();   // (0.99.51) the game page's lower parts keep their place on every game
            // the home screen starts hidden until your layout is in place (StageOpacity): whatever goes wrong later in
            // starting up, it's shown after 8 seconds
            try
            {
                if (Application.Current != null)
                {
                    var safety = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromSeconds(8) };
                    safety.Tick += (s0, e0) => { safety.Stop(); try { ShowStage(); } catch (Exception) { } };
                    safety.Start();
                }
            }
            catch (Exception) { }

            Shortcuts = new ObservableCollection<ShortcutItem>();
            InfoItems = new ObservableCollection<ShortcutItem>();
            Downloads = new ObservableCollection<DownloadItem>();
            VoiceMembers = new ObservableCollection<VoiceItem>();
            AudioDevices = new ObservableCollection<AudioItem>();
            DiscordList = new ObservableCollection<DiscordRow>();
            DiscordMessages = new ObservableCollection<MessageItem>();
            QuickReplies = new ObservableCollection<DiscordRow>();

            ToggleOptionsCommand = new RelayCommand(_ => TogglePanel("options"));
            ToggleDownloadsCommand = new RelayCommand(_ => TogglePanel("downloads"));
            ToggleDiscordCommand = new RelayCommand(_ => TogglePanel("discord"));
            ToggleAudioCommand = new RelayCommand(_ => TogglePanel("audio"));
            ClosePanelsCommand = new RelayCommand(_ => ClosePanels());
            BackCommand = new RelayCommand(_ => Back());
            OpenOptionsPageCommand = new RelayCommand(p =>
            {
                string page = p as string ?? "";
                if (settings.SkipGamePage && (page == "gamepage" || OptionsParent(page) == "gamepage")) return;   // the game page is turned off
                if (page == "gamepage" && optionsPage == "")
                {
                    gamePageFromDetails = false;
                    weOpenedDetails = EnsureGameDetailsOpen();
                }
                SetOptionsPage(page);
            });
            HomeCommand = new RelayCommand(_ => GoHome());
            OpenAchievementsCommand = new RelayCommand(_ => OpenAchievementsView());
            HibernateCommand = new RelayCommand(_ => AskHibernate());
            CycleHintsPositionCommand = new RelayCommand(_ => { settings.HintsPosition = (settings.HintsPosition + 1) % 3; SaveSettings(); NotifyHome(); });
            CycleLogoPositionCommand = new RelayCommand(_ => { settings.LogoPosition = (settings.LogoPosition + 1) % 3; SaveSettings(); NotifyHome(); });
            CycleInfoPositionCommand = new RelayCommand(_ => { settings.InfoPosition = (settings.InfoPosition + 1) % 3; SaveSettings(); NotifyHome(); });
            DesktopModeCommand = new RelayCommand(_ => WithOutro("desktop", () => CloseThenRun("SwitchToDesktopCommand")));
            ToggleClockCommand = new RelayCommand(_ => { settings.Clock24h = !settings.Clock24h; SaveSettings(); NotifyHome(); RefreshClock(true); });
            OpenSteamDownloadsCommand = new RelayCommand(_ => { ClosePanels(); Native.OpenUri("steam://open/downloads"); });
            RefreshDownloadsCommand = new RelayCommand(_ => RefreshDownloadsNow());

            InitDiscordCommands();
            InitAchievementCommands();
            InitHomeLayoutCommands();
            InitDetailsCommands();
            InitSoundCommands();
            InitTrailerCommands();
            InitCardCommands();
            InitFriendCommands();
            InitMasterCommands();
            InitLayouts();
            try { Marquee.Install(() => IsFullscreen && settings.MarqueeText); } catch (Exception) { }   // (0.90) words that don't fit scroll while highlighted
            InitWidgetCommands();
            InitHelpCommands();
            InitHomeTileCommands();
            InitStoreCommands();
            InitBrowserCommands();
            InitPicker();
            InitGallery();
            InitHomeRows();
            InitLook();
            InitGamePageMore();
            InitConfirm();
            InitHoldHome();
            InitPadInput();
            InitKeyboard();
            InitGamePages();
            InitWidgetShape();
            InitSearch();
            InitNewPages();
            InitAddOns();
            HookShown();

            // Read settings now (before Playnite shows its window) so the first frame already has
            // the right layout and the last game's background.
            try
            {
                LoadSettings();
                try { LoadEmptyLocKeys(); } catch (Exception) { }
                ReduceMotionNow = settings.ReduceMotion;
                try { ApplyTextSize(); ApplyContrast(); ApplyLanguage(); } catch (Exception) { }
                BuildShortcuts();
                BuildInfoItems();
                LoadStartupBackground();
                earlyInit = true;
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: early start failed"); }
        }

        private bool earlyInit;

        // (1.0.0) A mistake in Kippix's own code on the screen's thread used to close Playnite (Playnite only catches crashes
        // as the app goes down). Now an error that comes from Kippix (its code in the error's trail) is written to the log and
        // Playnite carries on; anything else is left to Playnite as before. A burst of the same error is logged once.
        private bool crashGuardHooked;
        private string crashLast;
        private int crashCount;

        private void HookCrashGuard()
        {
            if (crashGuardHooked || ui == null) return;
            crashGuardHooked = true;
            ui.UnhandledException += (s, e) =>
            {
                try
                {
                    if (e.Handled || e.Exception == null) return;
                    string trail = e.Exception.ToString();
                    if (trail.IndexOf("at KippixHelper.", StringComparison.Ordinal) < 0 && trail.IndexOf("at KippixOverlay.", StringComparison.Ordinal) < 0
                        && trail.IndexOf("at KippixShared.", StringComparison.Ordinal) < 0) return;
                    e.Handled = true;
                    crashCount++;
                    string first = e.Exception.GetType().Name + ": " + e.Exception.Message;
                    if (first != crashLast || crashCount <= 3) logger.Error(e.Exception, "Kippix: an error in Kippix was caught (Playnite carries on)");
                    crashLast = first;
                }
                catch (Exception) { }
            };
        }

        // =====================================================================
        // Start / stop
        // =====================================================================

        public void Start()
        {
            ui = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
            try { HookCrashGuard(); } catch (Exception) { }
            try { System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12; } catch (Exception) { }   // (1.0.0) every web call, on any Windows 10
            try { HookMousePark(); } catch (Exception) { }
            try { if (emptyLocKeys == null) LoadEmptyLocKeys(); } catch (Exception) { }
            try { HookDesignSize(); } catch (Exception) { }   // (0.99.58) never laid out smaller than 1920 × 1080 (16:10 and 4:3 screens)   // (0.99.57) the mouse pointer when Playnite starts / comes back
            try
            {
                System.Net.ServicePointManager.DefaultConnectionLimit = Math.Max(System.Net.ServicePointManager.DefaultConnectionLimit, 12);
                Images.DiskCacheDir = Path.Combine(plugin.GetPluginUserDataPath(), "ImageCache");
            }
            catch (Exception) { }
            if (!earlyInit)
            {
                LoadSettings();
                try { ApplyLanguage(); } catch (Exception) { }
                BuildShortcuts();
                BuildInfoItems();
            }
            if (IsFullscreen) ApplyAccessibility();
            try { StartProfiles(); } catch (Exception ex) { logger.Error(ex, "Kippix: profiles"); }
            StartLibraryWatch();
            ScheduleWishlistCheck();
            ScheduleGameUpdateCheck();
            HookSteamPriorityAuto();   // (0.99.42) new Steam games: High Priority on their own   // (0.99.39) games with an update waiting: a card once Playnite's up
            if (IsFullscreen)
            {
                try { InitWidgets(); } catch (Exception ex) { logger.Error(ex, "Kippix: widgets failed to start"); }
                try { InitHomeTiles(); } catch (Exception ex) { logger.Error(ex, "Kippix: home tiles failed to start"); }
            }
            try { StartSounds(); } catch (Exception ex) { logger.Error(ex, "Kippix: sounds failed to start"); }
            if (IsFullscreen) ui.BeginInvoke(new Action(() => { try { HookLaunchScreen(); } catch (Exception) { } }), DispatcherPriority.ApplicationIdle);
            overlay.Connect(api, () => OnUi(() => { RefreshDiscord(); OnOverlayChangedAch(); }));

            // B / Esc / Backspace go back (or close), Up / Down move a shortcut while re-ordering.
            // Controller buttons arrive as KeyDown events, so one window-level handler covers everything.
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.KeyDownEvent, new KeyEventHandler(OnWindowKeyDown), true);
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown), true);
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.KeyUpEvent, new KeyEventHandler((s, e) =>
            {
                try { if (!kbOpen && openPanel == "browser" && IsMainWindow(s) && !ReferenceEquals(e, lastPanelKey) && !ReferenceEquals(e, padHandled) && e.GetType().Name == "GameControllerInputEventArgs") BrowserKeyFromWindow(e); }
                catch (Exception ex) { logger.Error(ex, "Kippix: key handling failed"); }
            }), true);
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnWindowFocusChanged), true);
            // A on a game card (keyboard and controller): play straight away when the game page is skipped
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ListBoxItem), Keyboard.KeyDownEvent, new KeyEventHandler(OnCardKeyDown), false);

            // (0.96) once: the menu theme back to Kippix's own (the presets had brought their own; now it's one for every layout)
            if (IsFullscreen && !settings.LookReset096)
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        settings.LookReset096 = true;
                        settings.Look = null;
                        settings.LookPack = new HelperSettings().LookPack;
                        LookSettingsSwapped();
                        SaveSettings();
                        logger.Info("Kippix: the menu theme was put back to Kippix's (once, 0.96)");
                    }
                    catch (Exception ex) { logger.Warn("Kippix: menu theme reset: " + ex.Message); }
                }), DispatcherPriority.ApplicationIdle);

            // (0.94) once: the game page back to how Kippix comes, and its presets cleared (Design 1 and 2 start from it); Undo reset brings the old one back
            if (IsFullscreen && !settings.GameReset094)
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        settings.GameReset094 = true;
                        ResetLayout("game");
                        logger.Info("Kippix: the game page was put back to how it comes (once, 0.94)");
                    }
                    catch (Exception ex) { logger.Warn("Kippix: game page reset: " + ex.Message); }
                }), DispatcherPriority.ApplicationIdle);

            timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, e) => Tick();
            timer.Start();
            Tick();
        }

        public void Stop()
        {
            if (timer != null) timer.Stop();
            if (backgroundDirty) SaveSettings();
            FlushSettings(true);   // (0.93) anything still waiting to be written
            lock (saveLock) { }    // (and a write already under way has finished)
        }

        private DateTime tickPutOffSince = DateTime.MinValue;

        private void Tick()
        {
            ticks++;
            HookSlowOps();
            TickTimeStart();
            RefreshClock(false);
            RefreshBattery();
            try { MediaAddonsTick(); } catch (Exception) { }   // (0.98) UniPlaySong / BackgroundChanger, once they're loaded
            ApplyBgChangeDelay();   // (0.99.7) Playnite puts its own 300 ms back now and then
            try { BadgeSettingsTick(); } catch (Exception) { }   // (0.99.17)
            try { PlayersBootCheck(); } catch (Exception) { }    // (0.99.36) the first game's players online after fullscreen starts
            // (0.93) moving through the games on the home screen: the upkeep below waits until you stop (for up to 6 s), so it
            // never lands in the middle of a move
            bool moving = openPanel == null && !detailsOpen && (DateTime.Now - lastNavKeyAt).TotalMilliseconds < 1300;
            if (moving)
            {
                if (tickPutOffSince == DateTime.MinValue) tickPutOffSince = DateTime.Now;
                if ((DateTime.Now - tickPutOffSince).TotalSeconds < 6) { tickWatch = null; return; }
            }
            tickPutOffSince = DateTime.MinValue;
            RefreshDiscord();
            TickTime("discord");
            ApplyHomeLayout();
            TickTime("home layout");
            HookDetails();
            ApplyCardLayout();
            TickTime("card layout");
            EnsurePreviewTimer();
            if (settings.MasterOffsets.Count > 0 || masterTransforms.Count > 0 || (settings.MasterSizes != null && settings.MasterSizes.Count > 0) || HasMasterTurns) ApplyMasterOffsets();
            TickTime("places");
            if (detailsOpen && settings.DetShowFriends && HasSteamKey && DateTime.Now - friendInfoAt > FriendSummaryAge) UpdateDetFriends();
            if (settings.AutoHideTopBar || RowFullUnderBar || rowFullBarWas) { rowFullBarWas = RowFullUnderBar; UpdateTopBarFade(); }
            RefreshDetailsDate();
            AmbientTick();
            ReminderTick();
            UpdateSeasonal();
            WishReleaseTick();
            WelcomeResumeTick();
            TickTime("extras");
            BgFollowLayout();
            TickTime("per layout");
            ApplySlotPins(false);
            TickTime("pins");
            if (ProfileFilterActive || slotsAlwaysShow || folderFilter != null) ApplyListFilter(false);   // Playnite may have put its own filter back
            if (FolderSortWanted || folderSorter != null) FolderSortApply();   // (and its own sort, over a folder's)
            TickTime("filter/sort");
            PcStatsTick();
            if (backgroundDirty) { backgroundDirty = false; SaveSettings(); }
            TickTime("save");
            if (ticks % 2 == 1 || openPanel == "downloads") RefreshDownloadsAsync();
            TickTimeEnd();
        }

        private void OnUi(Action a)
        {
            if (ui == null) return;
            // results coming back from the internet: a mistake in handling one must never take Playnite down
            Action safe = () => { try { a(); } catch (Exception ex) { logger.Error(ex, "Kippix: background result"); } };
            if (ui.CheckAccess()) safe();
            else ui.BeginInvoke(safe);
        }

        /// <summary>A text from the theme's language file (read on the UI thread; store code also asks from worker threads).</summary>
        private static string Loc(string key, string fallback)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return fallback;
                string s;
                if (app.Dispatcher.CheckAccess()) s = app.TryFindResource(key) as string;
                else s = app.Dispatcher.Invoke(new Func<string>(() => app.TryFindResource(key) as string));
                if (s == null && emptyLocKeys != null && emptyLocKeys.Contains(key)) return "";   // (1.0.0) see below
                return s ?? fallback;   // an empty text in the language file means "show nothing here"
            }
            catch (Exception) { return fallback; }
        }

        // (1.0.0) Playnite drops the empty texts of a language file when it loads it, so a text emptied in en_US.xaml to take
        // it away was missing, and Loc's English fallback brought it back. The emptied keys are read from the file once.
        private static HashSet<string> emptyLocKeys;

        private void LoadEmptyLocKeys()
        {
            try
            {
                string f = Path.Combine(ThemeDir, "Localization", "en_US.xaml");
                if (!File.Exists(f)) return;
                var set = new HashSet<string>(StringComparer.Ordinal);
                var rx = new System.Text.RegularExpressions.Regex("x:Key=\"([^\"]+)\"\\s*(?:/>|>\\s*</sys:String>)");
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(f))) set.Add(m.Groups[1].Value);
                emptyLocKeys = set;
            }
            catch (Exception ex) { logger.Warn("Kippix: language file: " + ex.Message); }
        }

        // =====================================================================
        // Settings
        // =====================================================================

        private string SettingsPath
        {
            get { return Path.Combine(plugin.GetPluginUserDataPath(), "settings.json"); }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string text = File.ReadAllText(SettingsPath);
                    var s = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<HelperSettings>(text);
                    if (s != null) settings = s;
                    // set up before 0.59: the welcome is for new installs only
                    if (s != null && !text.Contains("\"WelcomeDone\"")) settings.WelcomeDone = true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Kippix: couldn't read settings");
                // the next save writes the settings as they come over it: a copy of the file that couldn't be read is kept beside it
                try { File.Copy(SettingsPath, Path.Combine(Path.GetDirectoryName(SettingsPath), "settings.unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".json"), true); } catch (Exception) { }
            }
            if (settings.SettingsVersion < 1)
            {
                // v0.2 had one "game info position" (left / right / hidden) covering the logo and the info line.
                if (settings.GameInfoPosition == 2) settings.ShowGameInfo = false;
                if (settings.GameInfoPosition == 1) settings.InfoPosition = settings.LogoPosition = settings.HintsPosition = 2;
                settings.SettingsVersion = 1;
            }
            if (settings.SettingsVersion < 3)
            {
                // v0.8 stored the volumes as a step (0..3); v0.9 uses a percentage slider
                if (settings.SettingsVersion == 2)
                {
                    if (settings.SoundVolume <= 3) settings.SoundVolume = (settings.SoundVolume + 1) * 25;
                    if (settings.MusicVolume <= 3) settings.MusicVolume = (settings.MusicVolume + 1) * 25;
                }
            }
            if (settings.SettingsVersion < 2)
            {
                // v0.7 moved the game page's default spot 320 px lower; start from the new default.
                settings.DetOffset = 0;
                settings.SettingsVersion = 2;
            }
            settings.SettingsVersion = 3;
            NormalizeSettings();
            NotifyHome();
        }

        /// <summary>Values kept in range (after loading the settings, a shared layout, or an undo).</summary>
        private void NormalizeSettings()
        {
            if (!settings.ListSideMoved033) { settings.ListSide = 0; settings.ListSideMoved033 = true; }
            if (settings.ProfileHubSectionsHidden == null) settings.ProfileHubSectionsHidden = new List<string>();   // (r12 B: profile sections)
            if (settings.CompletionBeaten == null) settings.CompletionBeaten = "";
            if (settings.Completion100 == null) settings.Completion100 = "";
            if (settings.CompletionDropped == null) settings.CompletionDropped = "";
            NormalizeGSettings();   // (r12 G: tips, safe mode, controller buttons, handheld, touch)
            if (!settings.ListCount040)
            {
                // 0.40: the list is "games to display" x each game's height now; a height of its own from 0.38 / 0.39 goes
                // (and the move sizing it made)
                if (settings.MasterSizes != null && settings.MasterSizes.ContainsKey("home.cards"))
                {
                    // the move that sizing it made goes too (it kept the old height's top in place), so the list sits
                    // where it was put, without that
                    var s0 = MasterSize("home.cards");
                    var o0 = MasterOffset("home.cards");
                    SetMasterOffset("home.cards", o0[0] - s0[2], o0[1] - s0[3]);
                    settings.MasterSizes.Remove("home.cards");
                }
                settings.ListCount040 = true;
            }
            if (!settings.MotionDefaults035) { settings.MotionSpeed = 1; settings.MotionAmount = 0; settings.MotionDefaults035 = true; }   // asked for: normal speed, subtle
            // games brought in from Steam / GOG are only kept while that import is on (0.35: switching it off takes them off)
            if (settings.StoreWishlist != null)
                settings.StoreWishlist.RemoveAll(w => w == null || (w.ImportedFrom == "steam" && !settings.ImportSteamWishlist) || (w.ImportedFrom == "gog" && !settings.ImportGogWishlist));
            // 0.33: the top bar no longer moves as one piece; a move made that way goes to the profile button and the shortcut bar
            double[] wholeBar;
            if (settings.MasterOffsets != null && settings.MasterOffsets.TryGetValue("home.topbar", out wholeBar) && wholeBar != null && wholeBar.Length >= 2)
            {
                foreach (string part in new[] { "home.profile", "home.shortcuts" })
                {
                    double[] own;
                    settings.MasterOffsets.TryGetValue(part, out own);
                    settings.MasterOffsets[part] = new[] { (own != null && own.Length >= 2 ? own[0] : 0) + wholeBar[0], (own != null && own.Length >= 2 ? own[1] : 0) + wholeBar[1] };
                }
                settings.MasterOffsets.Remove("home.topbar");
            }
            double[] infoCol;
            if (settings.MasterOffsets != null && settings.MasterOffsets.TryGetValue("game.info", out infoCol) && infoCol != null && infoCol.Length >= 2)
            {
                foreach (string part in new[] { "game.stats", "game.buttons", "game.facts", "game.friends", "game.desc" })
                    if (!settings.MasterOffsets.ContainsKey(part)) settings.MasterOffsets[part] = new[] { infoCol[0], infoCol[1] };
                settings.MasterOffsets.Remove("game.info");
            }
            settings.SoundVolume = Math.Max(0, Math.Min(100, settings.SoundVolume));
            settings.MusicVolume = Math.Max(0, Math.Min(100, settings.MusicVolume));
            settings.HintsPosition = Math.Max(0, Math.Min(2, settings.HintsPosition));
            settings.LogoPosition = Math.Max(0, Math.Min(2, settings.LogoPosition));
            settings.InfoPosition = Math.Max(0, Math.Min(2, settings.InfoPosition));
            settings.HintsSize = Math.Max(0, Math.Min(3, settings.HintsSize));
            settings.LogoSize = Math.Max(0, Math.Min(3, settings.LogoSize));
            settings.InfoSize = Math.Max(0, Math.Min(3, settings.InfoSize));
            settings.CardOffset = Math.Max(CardOffsetMin, Math.Min(CardOffsetMax, settings.CardOffset));
            if (settings.CardSize != -1) settings.CardSize = Math.Max(CardSizeMin, Math.Min(CardSizeMax, settings.CardSize));
            settings.CardShape = Math.Max(0, Math.Min(CardShapes.Count - 1, settings.CardShape));
            settings.RowDesign = Math.Max(0, Math.Min(2, settings.RowDesign));
            // 0.52: one Triangle shape for the cards, with how they point as its own setting
            if (settings.CardShape == CardShapes.TriDown) { settings.CardShape = CardShapes.TriUp; settings.TriArrange = settings.TriAlternate ? 0 : 2; settings.TriAlternate = true; }
            if (!settings.TriAlternate) { if (settings.CardShape == CardShapes.TriUp) settings.TriArrange = 1; settings.TriAlternate = true; }
            settings.TriArrange = Math.Max(0, Math.Min(2, settings.TriArrange));
            settings.TriSpacing = Math.Max(0, Math.Min(40, settings.TriSpacing));
            settings.HomeLayoutMode = Math.Max(0, Math.Min(5, settings.HomeLayoutMode));
            settings.FolderGameDesign = Math.Max(0, Math.Min(4, settings.FolderGameDesign));
            settings.FolderInstalled = Math.Max(0, Math.Min(2, settings.FolderInstalled));
            settings.FolderSort = Math.Max(0, Math.Min(4, settings.FolderSort));
            settings.RowFullCount = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.RowFullCount));
            settings.RowTallCount = Math.Max(1, Math.Min(10, settings.RowTallCount));
            settings.CarTallW = Math.Max(0.4, Math.Min(2, settings.CarTallW <= 0 ? 1 : settings.CarTallW));
            settings.CarTallH = Math.Max(0.4, Math.Min(2, settings.CarTallH <= 0 ? 1 : settings.CarTallH));
            settings.CarFullH = Math.Max(0.3, Math.Min(1, settings.CarFullH <= 0 ? 1 : settings.CarFullH));
            settings.CarTallFill = settings.CarTallFill > 0 ? Math.Max(0.2, Math.Min(1, settings.CarTallFill)) : 0;
            settings.FolderTileCount = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.FolderTileCount));
            NormalizeFolders();
            if (settings.BoardCards == null) settings.BoardCards = new List<BoardCard>();
            if (settings.ManualCards == null) settings.ManualCards = new List<BoardCard>();
            if (settings.FreeCards == null) settings.FreeCards = new List<BoardCard>();
            if (settings.ManualPages == null) settings.ManualPages = new List<BoardPage>();
            if (settings.FreePages == null) settings.FreePages = new List<BoardPage>();
            settings.ManualPages.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Id));
            settings.FreePages.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Id));
            settings.FreeCards.RemoveAll(c => c == null);
            foreach (var c in settings.FreeCards)
            {
                // (free placement keeps its own units, so only the empty parts are filled in, nothing is clamped)
                if (string.IsNullOrEmpty(c.Kind)) c.Kind = "filter";
                if (string.IsNullOrEmpty(c.Source)) c.Source = "all";
                if (c.Info == null) c.Info = new List<string>();
                if (c.Sources == null) c.Sources = new List<string>();
            }
            settings.ManualPlacement = Math.Max(0, Math.Min(1, settings.ManualPlacement));
            settings.CardSpacing = Math.Max(-1, Math.Min(80, settings.CardSpacing));
            if (settings.BoardPresets == null) settings.BoardPresets = new List<BoardCard>();
            settings.BoardCards.RemoveAll(c => c == null);
            settings.ManualCards.RemoveAll(c => c == null);
            settings.BoardPresets.RemoveAll(c => c == null);
            settings.ManualCols = Math.Max(2, Math.Min(16, settings.ManualCols));
            settings.ManualRows = Math.Max(1, Math.Min(9, settings.ManualRows));
            foreach (var c in settings.BoardCards.Concat(settings.ManualCards).Concat(settings.BoardPresets))
            {
                if (string.IsNullOrEmpty(c.Kind)) c.Kind = "filter";
                if (string.IsNullOrEmpty(c.Source)) c.Source = "all";
                if (c.Info == null) c.Info = new List<string>();
                c.Shape = Math.Max(0, Math.Min(CardShapes.Count - 1, c.Shape));
                if (c.Sources == null) c.Sources = new List<string>();
                c.Art = Math.Max(0, Math.Min(4, c.Art));
                c.Nudge = Math.Max(0, Math.Min(4, c.Nudge));
                c.W = Math.Max(0.25, Math.Min(16, double.IsNaN(c.W) ? 1 : c.W)); c.H = Math.Max(0.25, Math.Min(9, double.IsNaN(c.H) ? 1 : c.H));
                c.X = Math.Max(-2, Math.Min(18, double.IsNaN(c.X) ? 0 : c.X)); c.Y = Math.Max(-3, Math.Min(11, double.IsNaN(c.Y) ? 0 : c.Y));   // (0.82: a Manual board's tiles can go past the grid, up to the screen's edges; BoardCells keeps them on screen)
            }
            settings.BoardCols = Math.Max(2, Math.Min(8, settings.BoardCols));
            settings.BoardRows = Math.Max(1, Math.Min(6, settings.BoardRows));
            settings.BoardPageAxis = Math.Max(0, Math.Min(1, settings.BoardPageAxis));
            settings.BoardIndicator = Math.Max(0, Math.Min(2, settings.BoardIndicator));   // 0 dots, 1 "2 / 5", 2 hidden
            settings.BoardLiveSeconds = Math.Max(3, Math.Min(60, settings.BoardLiveSeconds));
            settings.ListRows = Math.Max(1, Math.Min(10, settings.ListRows));
            settings.ListSide = Math.Max(0, Math.Min(1, settings.ListSide));
            settings.MotionSpeed = Math.Max(0, Math.Min(3, settings.MotionSpeed));
            settings.MotionAmount = Math.Max(0, Math.Min(2, settings.MotionAmount));
            if (settings.PinnedGames == null) settings.PinnedGames = new List<string>();
            if (settings.WishDismissed == null) settings.WishDismissed = new List<string>();
            if (settings.ExtraPins == null) settings.ExtraPins = new List<string>();
            if (settings.Collections == null) settings.Collections = new List<GameShelf>();
            if (settings.UpNext == null) settings.UpNext = new List<string>();
            NormalizeTimeLimits();   // (TimeLimits.cs: each profile's minutes and hours in range)
            if (settings.RecentSearches == null) settings.RecentSearches = new List<string>();
            if (settings.ArtSwaps == null) settings.ArtSwaps = new List<ArtSwap>();
            settings.PreviewMode = Math.Max(0, Math.Min(2, settings.PreviewMode));
            settings.PreviewDelay = Math.Max(0, Math.Min(120, settings.PreviewDelay));
            if (settings.StoreTrailers < 1 || settings.StoreTrailers > 2) settings.StoreTrailers = 1;   // "off" is gone (v0.23): trailers are always with the pictures
            if (settings.DetFlip)
            {
                // 0.13's Flip switch is now the "Right side" layout; Flip had mirrored the logo position, so undo that
                settings.DetFlip = false;
                settings.DetLayout = 1;
                if (settings.DetLogoPosition != 1) settings.DetLogoPosition = 2 - settings.DetLogoPosition;
            }
            // 0.7x: the game page's "Info and media sides" and the two height bars are gone (Free move places every part):
            // everyone (and every layout file) gets the default side and heights
            // (0.89) the Centred design is back (Game page › Layout › Page design, and Getting started): 0 Default or 2 Centred
            if (settings.DetLayout != 2) settings.DetLayout = 0;
            settings.DetOffset = 0;
            settings.DetMediaOffset = 0;
            if (settings.DetInfoOff == null) settings.DetInfoOff = new List<string> { "hltb", "size" };
            if (settings.AchLayout < 0) settings.AchLayout = settings.AchListLayout ? 1 : 2;   // the old grid / list switch
            settings.AchLayout = Math.Min(2, settings.AchLayout);
            // 0.70: the background is a three-way choice (a layout from before has only the solid switch)
            if (settings.AchBgMode < 0 || settings.AchBgMode > 2) settings.AchBgMode = settings.AchSolidBackground ? 2 : 0;
            if (!settings.AchDefaults070)
            {
                // asked for (0.70): centred, the highlighted game's background and the level, once; after that what you choose stays
                settings.AchLayout = 2;
                settings.AchBgMode = 0;
                settings.AchTallyMode = 1;
                settings.AchDefaults070 = true;
            }
            settings.AchSolidBackground = settings.AchBgMode == 2;   // (what older versions and layout files read)
            settings.AchTallyMode = Math.Max(0, Math.Min(2, settings.AchTallyMode));
            if (settings.AchRedesign == 5) settings.AchRedesign = 0;   // (0.90: Neon is gone)
            settings.AchRedesign = Math.Max(0, Math.Min(4, settings.AchRedesign));
            if (settings.AchItemRedesign == 5) settings.AchItemRedesign = 0;
            if (settings.AchItemRedesign < 0) settings.AchItemRedesign = settings.AchRedesign;
            settings.AchItemRedesign = Math.Min(4, settings.AchItemRedesign);
            if (settings.AchItemLayout < 0) settings.AchItemLayout = settings.AchLayout;
            settings.AchItemLayout = Math.Max(0, Math.Min(2, settings.AchItemLayout));
            settings.AchSort = Math.Max(0, Math.Min(3, settings.AchSort));
            settings.AchFilter = Math.Max(0, Math.Min(3, settings.AchFilter));
            settings.AchItemFilter = Math.Max(0, Math.Min(2, settings.AchItemFilter));
            if (settings.GalleryView != "all" && settings.GalleryView != "games" && settings.GalleryView != "dates" && settings.GalleryView != "favs") settings.GalleryView = "games";
            settings.GallerySort = Math.Max(0, Math.Min(1, settings.GallerySort));
            settings.GalleryShow = Math.Max(0, Math.Min(2, settings.GalleryShow));
            // 0.70: the top bar's Home shortcut is gone (hold B, the Guide button or Select + Start go home)
            if (settings.ShortcutOrder != null) settings.ShortcutOrder.RemoveAll(k => k == "home");
            if (settings.TopBarStayVisible != null) settings.TopBarStayVisible.RemoveAll(k => k == "home");
            settings.HomeTilesMode = Math.Max(0, Math.Min(2, settings.HomeTilesMode));
            settings.FilterPosition = 0;   // (the filter name's place is set with Free move now)
            settings.DetDescPosition = Math.Max(0, Math.Min(2, settings.DetDescPosition));
            settings.DetLogoPosition = Math.Max(0, Math.Min(2, settings.DetLogoPosition));
            settings.DetMediaSize = Math.Max(0, Math.Min(3, settings.DetMediaSize));
            settings.DetArt = Math.Max(0, Math.Min(2, settings.DetArt));
            if (settings.MasterGrid <= 0) settings.MasterGrid = 20;
            if (settings.MasterOffsets == null) settings.MasterOffsets = new Dictionary<string, double[]>();
            if (settings.MasterSizes == null) settings.MasterSizes = new Dictionary<string, double[]>();
            NormalizeTurns();
            NormalizeEditExtras();   // layers, opacity, your own tiles, crops
            if (settings.DetCardLooks == null) settings.DetCardLooks = new Dictionary<string, int>();
            if (settings.DetCardColors == null) settings.DetCardColors = new Dictionary<string, string>();
            if (!settings.BgTrailer041) { if (settings.PreviewMode == 2) settings.HomeBgMode = 2; settings.BgTrailer041 = true; }
            settings.HomeBgMode = Math.Max(0, Math.Min(6, settings.HomeBgMode));   // (4 trailer, muted; 5 slideshow: ThemeApi.LiveBackground.cs; 6 the game's tile, 0.86)
            NormalizeLiveSettings();   // intro / outro, depth, animated art, night mode
            // Background › The game's trailer and Trailer preview › In the background are one setting
            if (settings.HomeBgMode == 2) settings.PreviewMode = 2;
            else if (settings.PreviewMode == 2) settings.PreviewMode = 0;
            NormalizeBgOffset();   // (r15: Background › Move the picture, ThemeApi.BgMove.cs)
            settings.HomeFadeDir = Math.Max(0, Math.Min(4, settings.HomeFadeDir));
            if (settings.HomeFadeReach < 5 || settings.HomeFadeReach > 95) settings.HomeFadeReach = 44;
            if (settings.ListInfoShare < 15 || settings.ListInfoShare > 85) settings.ListInfoShare = 50;
            if (settings.ListTileH != 0) settings.ListTileH = Math.Max(60, Math.Min(400, settings.ListTileH));
            if (settings.ListTileW != 0) settings.ListTileW = Math.Max(200, Math.Min(1400, settings.ListTileW));
            settings.ListStretch = 0;   // 0.35 only: the list's height is a size in MasterSizes now
            if (settings.Widgets == null) settings.Widgets = new List<WidgetConfig>();
            if (settings.DetNoInteract == null) settings.DetNoInteract = new List<string>();
            settings.ProfileShow = Math.Max(0, Math.Min(2, settings.ProfileShow));
            if (settings.HideTopOptions) settings.SettingsInProfileMenu = true;   // there's always a way into the settings
            settings.DetDescSource = Math.Max(0, Math.Min(1, settings.DetDescSource));
            if (!settings.EditsMerged) MergeEdits(settings);
            if (settings.StoreWishlist == null) settings.StoreWishlist = new List<WishItem>();
            if (settings.StoreRecent == null) settings.StoreRecent = new List<WishItem>();
            settings.ItadApiKey = "";   // own keys aren't used any more (the built-in one is): an old saved one is dropped
            settings.StoreImageQuality = Math.Max(0, Math.Min(2, settings.StoreImageQuality));
            // 0.58
            settings.TextSize = Math.Max(0, Math.Min(3, settings.TextSize));
            if (settings.LanguageOverride == null) settings.LanguageOverride = "";
            settings.SeasonalMode = Math.Max(0, Math.Min(1, settings.SeasonalMode));
            if (settings.SlotPins == null) settings.SlotPins = new List<SlotPin>();
            settings.SlotPins.RemoveAll(p => p == null || string.IsNullOrEmpty(p.GameId) || p.Slot < 1);
            settings.SplitPlayers = settings.SplitPlayers <= 0 ? 0 : Math.Max(2, Math.Min(8, settings.SplitPlayers));
            settings.OnlinePlayers = Math.Max(0, Math.Min(64, settings.OnlinePlayers));
            settings.SplitScreenKind = Math.Max(0, Math.Min(2, settings.SplitScreenKind));
            settings.SplitMode = Math.Max(0, Math.Min(2, settings.SplitMode));
            NormalizeLook();   // Look packs / Theme Studio (ThemeApi.LookPacks.cs)
            if (!settings.DetSavesAdded058)
            {
                // the new Save backups card goes on the lower page, with the other cards
                settings.DetSavesAdded058 = true;
                if (settings.DetLower != null && !settings.DetLower.Contains("saves")) settings.DetLower.Add("saves");
            }
            if (!settings.DetNewsModsAdded)
            {
                // (r12) the new News and Mods cards go on the lower page, with the other cards
                settings.DetNewsModsAdded = true;
                if (settings.DetLower != null)
                    foreach (string k in new[] { "news", "mods" }) if (!settings.DetLower.Contains(k)) settings.DetLower.Add(k);
            }
            if (!settings.DetNewsCardOff079)
            {
                // (r13) the News button next to Achievements replaces the News card (still there to turn on, top or lower page),
                // and the play info's new friends line starts off
                settings.DetNewsCardOff079 = true;
                settings.DetSecNews = false;
                if (settings.DetInfoOff == null) settings.DetInfoOff = new List<string> { "hltb", "size" };
                if (!settings.DetInfoOff.Contains("friends")) settings.DetInfoOff.Add("friends");
            }
        }

        private void SaveSettings()
        {
            try
            {
                settings.ShortcutOrder = Shortcuts.Select(s => s.Key).ToList();
                if (InfoItems.Count > 0) settings.InfoOrder = InfoItems.Select(s => s.Key).ToList();
            }
            catch (Exception) { }
            QueueSettingsWrite();
            if (storeSettingsFromStore) StoreLiveRefreshSoon();   // Store settings opened over the Store: the Store underneath follows along
            EditHistoryOnSave();   // Free edit / the board being edited: the change becomes an undo step
        }

        // (0.93) The file is written a moment after the last change (a burst of changes is one write), and off the UI thread:
        // turning it into text stays here (the settings only change here), the disk is the worker's. Playnite closing writes
        // anything still waiting, there and then.
        private DispatcherTimer saveTimer;
        private bool saveQueued;
        private DateTime saveFirstQueued;
        private readonly object saveLock = new object();
        private long saveSeq, saveWrittenSeq;

        private void QueueSettingsWrite()
        {
            if (ui == null || !ui.CheckAccess()) { WriteSettingsNow(true); return; }   // (from a worker thread: as before)
            if (!saveQueued) { saveQueued = true; saveFirstQueued = DateTime.Now; }
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(800) };
                saveTimer.Tick += (s, e) => { saveTimer.Stop(); FlushSettings(false); };
            }
            saveTimer.Stop();
            if ((DateTime.Now - saveFirstQueued).TotalSeconds > 5) { FlushSettings(false); return; }   // (a stream of changes never puts it off for long)
            saveTimer.Start();
        }

        /// <summary>Writes the settings now if a write is waiting (sync: on this thread, finished when it returns).</summary>
        private void FlushSettings(bool sync)
        {
            if (!saveQueued) return;
            saveQueued = false;
            if (saveTimer != null) saveTimer.Stop();
            WriteSettingsNow(sync);
        }

        private void WriteSettingsNow(bool sync)
        {
            try
            {
                // (a list a background job is changing at that moment fails once: tried again, so the change isn't lost)
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string json = null;
                for (int i = 0; json == null; i++)
                {
                    try { json = js.Serialize(settings); }
                    catch (InvalidOperationException) { if (i >= 2) throw; Thread.Sleep(30); }
                }
                string path = SettingsPath;
                long seq = Interlocked.Increment(ref saveSeq);
                if (sync) WriteSettingsFile(path, json, seq);
                else KippixShared.SafeWork.Queue(_ => WriteSettingsFile(path, json, seq));
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: couldn't save settings"); }
        }

        private void WriteSettingsFile(string path, string json, long seq)
        {
            lock (saveLock)
            {
                if (seq <= saveWrittenSeq) return;   // (a newer one is written already)
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    // written next to it first, then swapped in: Playnite closing (or the PC going off) mid-write never leaves a half file
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, json);
                    try
                    {
                        if (File.Exists(path)) File.Replace(tmp, path, null);
                        else File.Move(tmp, path);
                    }
                    catch (Exception) { File.WriteAllText(path, json); try { File.Delete(tmp); } catch (Exception) { } }
                    saveWrittenSeq = seq;
                }
                catch (Exception ex) { logger.Error(ex, "Kippix: couldn't save settings"); }
            }
        }

        // =====================================================================
        // Top bar shortcuts (pin, order, move)
        // =====================================================================

        private static readonly string[] DefaultOrder = { "search", "achievements", "gallery", "store", "discord", "audio", "hibernate", "sleep", "restart", "shutdown", "downloads", "battery",
                                                            "quick", "releases", "wishlist", "news", "friends", "collections", "splitscreen", "pick", "storage", "controller", "bluetooth", "year",
                                                            "media", "localmedia", "netflix", "youtube", "prime", "crunchyroll", "options", "clock" };
        /// <summary>(1.0.1) Media, Shows &amp; movies and each streaming service: off until you switch them on (kept with ExtraPins).</summary>
        internal static readonly string[] MediaShortcutKeys = { "media", "localmedia", "netflix", "youtube", "prime", "crunchyroll" };
        private static bool IsExtraPinKey(string key) { return PageKeys.Contains(key) || MediaShortcutKeys.Contains(key); }

        public ObservableCollection<ShortcutItem> Shortcuts { get; private set; }

        private ShortcutItem movingItem;

        private bool GetPin(string key)
        {
            switch (key)
            {
                case "search": return settings.PinSearch;
                case "achievements": return settings.PinAchievements;
                case "discord": return settings.PinDiscord;
                case "audio": return settings.PinAudio;
                case "hibernate": return settings.PinHibernate;
                case "downloads": return settings.PinDownloads;
                case "store": return settings.PinStore;
                case "gallery": return settings.PinGallery;
                case "battery": return settings.PinBattery;
                case "sleep": return settings.PinSleep;
                case "restart": return settings.PinRestart;
                case "shutdown": return settings.PinShutdown;
                case "clock": return settings.PinClock;
                case "options": return !settings.HideTopOptions;
            }
            if (IsExtraPinKey(key)) return settings.ExtraPins != null && settings.ExtraPins.Contains(key);
            return true;
        }

        private void SetPin(string key, bool v)
        {
            switch (key)
            {
                case "search": settings.PinSearch = v; break;
                case "achievements": settings.PinAchievements = v; break;
                case "discord": settings.PinDiscord = v; break;
                case "audio": settings.PinAudio = v; break;
                case "hibernate": settings.PinHibernate = v; break;
                case "downloads": settings.PinDownloads = v; break;
                case "store": settings.PinStore = v; break;
                case "gallery": settings.PinGallery = v; break;
                case "battery": settings.PinBattery = v; break;
                case "sleep": settings.PinSleep = v; break;
                case "restart": settings.PinRestart = v; break;
                case "shutdown": settings.PinShutdown = v; break;
                case "clock": settings.PinClock = v; break;
                case "options": settings.HideTopOptions = !v; if (!v) settings.SettingsInProfileMenu = true; break;   // hidden: Settings & Customisation is in the profile menu
            }
            if (IsExtraPinKey(key))
            {
                if (settings.ExtraPins == null) settings.ExtraPins = new List<string>();
                settings.ExtraPins.Remove(key);
                if (v) settings.ExtraPins.Add(key);
            }
        }

        private string ShortcutName(string key)
        {
            switch (key)
            {
                case "options": return Loc("LOC_KX_OptionsDots", "Settings & Customisation (⋯)");
                case "search": return Loc("LOC_KX_Search", "Search");
                case "achievements": return Loc("LOC_KX_Achievements", "Achievements");
                case "discord": return "Discord";
                case "audio": return Loc("LOC_KX_Audio", "Sound output");
                case "hibernate": return Loc("LOC_KX_Hibernate", "Hibernate");
                case "downloads": return Loc("LOC_KX_Downloads", "Downloads");
                case "store": return Loc("LOC_KX_Store", "Store");
                case "gallery": return Loc("LOC_KX_Gallery", "Gallery");
                case "battery": return Loc("LOC_KX_ControllerBattery", "Controller battery");
                case "sleep": return Loc("LOC_KX_Sleep", "Sleep");
                case "restart": return Loc("LOC_KX_Restart", "Restart");
                case "shutdown": return Loc("LOC_KX_PowerOff", "Power off");
                case "clock": return Loc("LOC_KX_Clock", "Clock");
            }
            if (PageKeys.Contains(key)) return PageName(key);
            switch (key)
            {
                case "media": return Loc("LOC_KX_Media", "Media");
                case "localmedia": return Loc("LOC_KX_LocalMedia", "Shows & movies");
            }
            var svc = MediaServices.FirstOrDefault(m => m.Key == key);
            if (svc != null) return svc.Name;
            return key;
        }

        private ICommand ShortcutCommand(string key)
        {
            switch (key)
            {
                case "options": return new RelayCommand(_ => ToggleOptionsCommand.Execute(null));
                case "search": return new RelayCommand(_ => OpenSearch());
                case "achievements": return new RelayCommand(_ => OpenAchievementsView());
                case "discord": return new RelayCommand(_ => TogglePanel("discord"));
                case "audio": return new RelayCommand(_ => TogglePanel("audio"));
                case "hibernate": return new RelayCommand(_ => AskHibernate());
                case "sleep": return new RelayCommand(_ => AskPower("sleep"));
                case "restart": return new RelayCommand(_ => AskPower("restart"));
                case "shutdown": return new RelayCommand(_ => AskPower("shutdown"));
                case "downloads": return new RelayCommand(_ => TogglePanel("downloads"));
                case "store": return new RelayCommand(_ => OpenStore());
                case "gallery": return new RelayCommand(_ => OpenGallery());
            }
            if (PageKeys.Contains(key)) { string k = key; return new RelayCommand(_ => OpenPageFromShortcut(k)); }
            if (key == "media") return new RelayCommand(_ => { PlaySound("select"); OpenMediaMenu(); });
            if (key == "localmedia") return new RelayCommand(_ => { PlaySound("select"); OpenLocalMedia(false); });
            if (MediaServices.Any(m => m.Key == key)) { string k = key; return new RelayCommand(_ => OpenMediaByKey(k)); }
            return null;
        }

        private void BuildShortcuts()
        {
            var order = new List<string>();
            if (!settings.OptionsInBar035)
            {
                // the ⋯ always came after the shortcuts: it starts there, with the clock after it (next to the notifications)
                settings.OptionsInBar035 = true;
                if (settings.ShortcutOrder != null) { settings.ShortcutOrder.Remove("options"); settings.ShortcutOrder.Remove("clock"); settings.ShortcutOrder.Add("options"); settings.ShortcutOrder.Add("clock"); }
            }
            if (settings.ShortcutOrder != null)
                foreach (string k in settings.ShortcutOrder)
                    if (DefaultOrder.Contains(k) && !order.Contains(k)) order.Add(k);
            // a shortcut added in a later version goes where it sits by default (after the one before it), not at the end
            for (int d = 0; d < DefaultOrder.Length; d++)
            {
                string k = DefaultOrder[d];
                if (order.Contains(k)) continue;
                int at = -1;
                for (int b = d - 1; b >= 0 && at < 0; b--) at = order.IndexOf(DefaultOrder[b]);
                order.Insert(at + 1, k);
            }

            Shortcuts.Clear();
            foreach (string k in order)
            {
                var item = new ShortcutItem { Key = k, Name = ShortcutName(k), Command = ShortcutCommand(k) };
                item.SetPinnedSilently(GetPin(k));
                item.PinChanged = OnPinChanged;
                item.MoveCommand = new RelayCommand(_ => ToggleMove(item, Shortcuts, "KX_ShortcutList"));
                item.SetStaySilently(settings.TopBarStayVisible != null && settings.TopBarStayVisible.Contains(k));
                item.StayChanged = OnStayChanged;
                Shortcuts.Add(item);
                if (item.IsPinned) LoadShortcutPicture(item);
            }
            UpdateShortcutStatus();
        }

        /// <summary>(1.0.1) A streaming service's shortcut: its own icon (only once it's in the top bar: nothing is fetched otherwise).</summary>
        private void LoadShortcutPicture(ShortcutItem item)
        {
            if (item == null || item.Picture != null) return;
            var svc = MediaServices.FirstOrDefault(m => m.Key == item.Key);
            if (svc == null) return;
            LoadMediaIcon(svc, img => item.Picture = img);
        }

        private void OnPinChanged(ShortcutItem item)
        {
            if (item.IsPinned) LoadShortcutPicture(item);
            SetPin(item.Key, item.IsPinned);
            SaveSettings();
            UpdateShortcutStatus();
            if (item.IsOptions) NotifyProfile();
        }

        /// <summary>Visibility, Discord dot and badges for every shortcut.</summary>
        /// <summary>Top bar settings: the battery percentage row only shows while the battery is in the top bar.</summary>
        public bool IsBatteryPinned { get { return settings.PinBattery; } }

        private void UpdateShortcutStatus()
        {
            Notify("IsBatteryPinned");
            foreach (var s in Shortcuts)
            {
                s.IsShown = s.IsPinned && (s.Key != "battery" || hasController);
                if (s.Key == "discord")
                {
                    s.ShowDot = dRunning;
                    s.DotBrush = dInCall ? GreenBrush : WhiteBrush;
                    s.ShowBadge = dUnread > 0;
                    s.BadgeText = dUnread > 99 ? "99+" : dUnread.ToString();
                    s.BadgeBrush = PinkBrush;
                    s.BadgeTextBrush = WhiteBrush;
                }
                else if (s.Key == "downloads")
                {
                    s.ShowBadge = downloadCount > 0;
                    s.BadgeText = downloadCount.ToString();
                    s.BadgeBrush = WhiteBrush;
                    s.BadgeTextBrush = DarkBrush;
                }
            }
        }

        // The list being re-ordered: the top bar shortcuts or the game info items.
        private ObservableCollection<ShortcutItem> movingList;
        private string movingListName;

        private void ToggleMove(ShortcutItem item, ObservableCollection<ShortcutItem> list, string listName)
        {
            if (movingItem == item) { StopMoving(); return; }
            if (movingItem != null) movingItem.IsMoving = false;
            movingItem = item;
            movingList = list;
            movingListName = listName;
            item.IsMoving = true;
            Notify("IsMovingShortcut");
        }

        private void StopMoving()
        {
            if (movingItem == null) return;
            movingItem.IsMoving = false;
            movingItem = null;
            movingList = null;
            movingListName = null;
            SaveSettings();
            Notify("IsMovingShortcut");
        }

        public bool IsMovingShortcut { get { return movingItem != null; } }

        private void MoveShortcut(int delta)
        {
            if (movingItem == null) return;
            var list = movingList ?? Shortcuts;
            int i = list.IndexOf(movingItem);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            var item = movingItem;
            string name = movingListName ?? "KX_ShortcutList";
            list.Move(i, j);
            SaveSettings();
            if (list == InfoItems) NotifyInfoColumns();
            ui.BeginInvoke(new Action(() => UiTools.FocusItemPart(name, item, "MoveButton")), DispatcherPriority.ContextIdle);
        }

        // =====================================================================
        // Game info line items (on/off + order)
        // =====================================================================

        private static readonly string[] DefaultInfoOrder = { "playtime", "lastplayed", "achievements", "platform", "source", "completion", "releaseyear", "players", "friends", "progress" };   // (0.99.50 progress: story progress)   // (r13 players / friends: ThemeApi.HomeInfoExtras.cs)

        /// <summary>The Game info details page: same row shape as the shortcuts (IsPinned = shown).</summary>
        public ObservableCollection<ShortcutItem> InfoItems { get; private set; }

        private bool GetInfo(string key)
        {
            switch (key)
            {
                case "playtime": return settings.InfoPlaytime;
                case "lastplayed": return settings.InfoLastPlayed;
                case "achievements": return settings.InfoAchievements;
                case "platform": return settings.InfoPlatform;
                case "source": return settings.InfoSource;
                case "completion": return settings.InfoCompletion;
                case "releaseyear": return settings.InfoReleaseYear;
                case "players": return settings.InfoPlayers;
                case "friends": return settings.InfoFriends;
                case "progress": return settings.HomeProgress;
            }
            return false;
        }

        private void SetInfo(string key, bool v)
        {
            switch (key)
            {
                case "playtime": settings.InfoPlaytime = v; break;
                case "lastplayed": settings.InfoLastPlayed = v; break;
                case "achievements": settings.InfoAchievements = v; break;
                case "platform": settings.InfoPlatform = v; break;
                case "source": settings.InfoSource = v; break;
                case "completion": settings.InfoCompletion = v; break;
                case "releaseyear": settings.InfoReleaseYear = v; break;
                case "players": settings.InfoPlayers = v; HomeExtrasSwitched(key); break;
                case "friends": settings.InfoFriends = v; HomeExtrasSwitched(key); break;
                case "progress": settings.HomeProgress = v; HomeProgressSwitched(); break;
            }
        }

        private string InfoName(string key)
        {
            switch (key)
            {
                case "playtime": return Loc("LOC_KX_InfoPlaytime", "Time played");
                case "lastplayed": return Loc("LOC_KX_InfoLastPlayed", "Last played");
                case "achievements": return Loc("LOC_KX_Achievements", "Achievements");
                case "platform": return Loc("LOC_KX_InfoPlatform", "System / platform");
                case "source": return Loc("LOC_KX_InfoSource", "Library (Steam, Epic...)");
                case "completion": return Loc("LOC_KX_InfoCompletion", "Completion status");
                case "releaseyear": return Loc("LOC_KX_InfoReleaseYear", "Release year");
                case "players": return Loc("LOC_KX_InfoPlayersHome", "Players online now (Steam)");
                case "friends": return Loc("LOC_KX_InfoFriendsHome", "Friends playing it (Steam key or PlayerActivities)");
                case "progress": return Loc("LOC_KX_InfoProgressHome", "Story progress");
            }
            return key;
        }

        private void BuildInfoItems()
        {
            var order = new List<string>();
            if (settings.InfoOrder != null)
                foreach (string k in settings.InfoOrder)
                    if (DefaultInfoOrder.Contains(k) && !order.Contains(k)) order.Add(k);
            foreach (string k in DefaultInfoOrder)
                if (!order.Contains(k)) order.Add(k);

            InfoItems.Clear();
            foreach (string k in order)
            {
                var item = new ShortcutItem { Key = k, Name = InfoName(k) };
                item.SetPinnedSilently(GetInfo(k));
                item.PinChanged = it => { SetInfo(it.Key, it.IsPinned); SaveSettings(); NotifyHome(); };
                item.MoveCommand = new RelayCommand(_ => ToggleMove(item, InfoItems, "KX_InfoItemList"));
                InfoItems.Add(item);
            }
            NotifyInfoColumns();
        }

        private int InfoCol(string key)
        {
            for (int i = 0; i < InfoItems.Count; i++) if (InfoItems[i].Key == key) return i;
            return 0;
        }

        // Column of each item on the home screen's info line (the theme lays them out in a Grid)
        public int InfoColPlaytime { get { return InfoCol("playtime"); } }
        public int InfoColLastPlayed { get { return InfoCol("lastplayed"); } }
        public int InfoColAchievements { get { return InfoCol("achievements"); } }
        public int InfoColPlatform { get { return InfoCol("platform"); } }
        public int InfoColSource { get { return InfoCol("source"); } }
        public int InfoColCompletion { get { return InfoCol("completion"); } }
        public int InfoColReleaseYear { get { return InfoCol("releaseyear"); } }

        private void NotifyInfoColumns()
        {
            foreach (string n in new[] { "InfoColPlaytime", "InfoColLastPlayed", "InfoColAchievements", "InfoColPlatform", "InfoColSource", "InfoColCompletion", "InfoColReleaseYear", "InfoColPlayers", "InfoColFriends" })
                Notify(n);
        }

        // =====================================================================
        // Keys: back / close / move
        // =====================================================================

        /// <summary>Only Playnite's main fullscreen window: never react to keys meant for menus or the on-screen keyboard.</summary>
        private static bool IsMainWindow(object sender)
        {
            return Application.Current != null && sender != null && ReferenceEquals(sender, Application.Current.MainWindow);
        }

        /// <summary>
        /// Arrow keys inside our panels. Moves focus ourselves, because a scrolling list otherwise swallows
        /// Up/Down at its last item and you can't get back to the buttons around it.
        /// </summary>
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (IsMainWindow(sender)) PerfKeyDown(e);   // (0.93) the slowness finder
            if (openPanel == null && IsMainWindow(sender) && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)) ApplyBgChangeDelay(false);   // (0.99.15) before the picture changes
            try
            {
                if (IsMasterMoving && OutlineHasFocus) return;   // Master edit: the outline's own key handler takes everything
                if (bgMoveOn && IsMainWindow(sender)) { BgMoveKey(e); return; }
                if (splashEditOn && IsMainWindow(sender)) { SplashEditKey(e); return; }
                if (lpOpen && IsMainWindow(sender)) { LocalPlayerKey(e); return; }   // (1.0.1: Shows & movies' player takes every key)   // (1.0.1: Splash screen layout › Edit placement takes every key)   // (r15: Background › Move the picture takes every key: ThemeApi.BgMove.cs)
                if (guideBarOpen && IsMainWindow(sender)) { GuideBarKey(e); return; }   // (0.99.46) the quick bar takes every key (ThemeApi.GuideBar.cs)
                if (galleryEditOpen && IsMainWindow(sender)) { GalleryEditKey(e); return; }   // (0.99.59) the gallery's Edit takes every key (ThemeApi.GalleryEdit.cs)
                if (kbOpen && IsMainWindow(sender)) { KeyboardKey(e); return; }   // the keyboard takes every key while it's up
                if (descReaderOpen && IsMainWindow(sender) && DescReaderKey(e)) return;
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && detailsOpen && GamePageKey(e)) { lastPanelKey = e; return; }
                if (confirmOpen && IsMainWindow(sender) && !e.Handled)
                {
                    if (HandlePanelKey(e)) { lastPanelKey = e; return; }
                    var inBox = UiTools.FindByName(Application.Current.MainWindow, "KX_ConfirmLayer");
                    var f = Keyboard.FocusedElement as DependencyObject;
                    if (inBox != null && (f == null || !inBox.IsAncestorOf(f))) { UiTools.FocusByName("KX_ConfirmNo"); e.Handled = true; }
                    return;
                }
                if (openPanel == "browser") { if (IsMainWindow(sender)) BrowserPreviewKey(e); return; }   // the page gets the keys
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && TilesReleaseKey(e)) { e.Handled = true; return; }   // (0.99.18) the tiles stay up
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && VerticalBarKey(e)) return;   // the shortcut bar down the side
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && FolderBackKey(e)) { e.Handled = true; padHandled = e; return; }   // Folder: B in a folder, back to the folder tiles
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && HomeBackKey(e)) { e.Handled = true; padHandled = e; return; }   // (0.99.62) B on the home screen: the first game / the top bar
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && BoardKey(e)) { e.Handled = true; return; }   // the Board
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && CarouselKey(e)) { e.Handled = true; return; }   // Row's carousel: round the ends, the tiles
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && ExtraRowKey(e)) { e.Handled = true; return; }   // the rows under the games
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && detailsOpen && (e.Key == Key.Up || e.Key == Key.Down) && DetailsMoreKey(e)) { e.Handled = true; return; }
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && WidgetNavKey(e)) return;   // Up from the games to the widgets
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && !detailsOpen && UpToTopBar(e)) { e.Handled = true; return; }   // Up from the top of the games: the top bar
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && detailsOpen && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down) && DetailsSideNav(e))
                    return;
                if (openPanel == null && !e.Handled && IsMainWindow(sender) && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up))
                {
                    KeepInTopBar(e);
                    return;
                }
                if (openPanel == null || e.Handled || !IsMainWindow(sender)) return;
                if (openPanel == "store" && storePage == "game") storeGameFocusUntouched = false;   // any key on a game's page: late parts leave the highlight alone
                // Back first, on the way down, so Playnite's own B (e.g. closing the game page) doesn't fire too
                if (HandlePanelKey(e)) { lastPanelKey = e; return; }
                if (e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right) return;

                if (openPanel == "store" && storeViewerOpen)
                {
                    if (ViewerArrowKey(e.Key)) e.Handled = true;
                    return;
                }
                if (openPanel == "gallery" && GalleryArrowKey(e)) { e.Handled = true; return; }

                if (movingItem != null && (e.Key == Key.Up || e.Key == Key.Down))
                {
                    MoveShortcut(e.Key == Key.Up ? -1 : 1);
                    e.Handled = true;
                    return;
                }

                var focused = Keyboard.FocusedElement as UIElement;
                if (focused == null) return;
                var fe = focused as FrameworkElement;
                if (fe != null && WidgetShapeKey(fe, e.Key)) { e.Handled = true; return; }   // a widget's width / height / text size
                if (SliderKey(fe, e.Key)) { e.Handled = true; return; }   // the KX_Slider_… rows
                if (StudioArrowKey(fe, e.Key)) { e.Handled = true; return; }   // Theme Studio's sliders and choices
                if (fe != null && fe.Name == "KX_CommunityShot" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    CommunityShotStep(e.Key == Key.Right ? 1 : -1);   // (1.0.4) a Community item's next / previous picture
                    e.Handled = true;
                    return;
                }
                if (fe != null && fe.Name == "KX_CardSlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgeCards(e.Key == Key.Right ? CardOffsetStep : -CardOffsetStep);
                    e.Handled = true;
                    return;
                }
                if (fe != null && fe.Name == "KX_CardSizeSlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgeCardSize(e.Key == Key.Right ? 1 : -1);
                    e.Handled = true;
                    return;
                }
                if (fe != null && fe.Name == "KX_StorePreviewDelaySlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgeStorePreviewDelay(e.Key == Key.Right ? 1 : -1);
                    e.Handled = true;
                    return;
                }
                if (fe != null && fe.Name == "KX_PreviewDelaySlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgePreviewDelay(e.Key == Key.Right ? 1 : -1);
                    e.Handled = true;
                    return;
                }
                if (fe != null && fe.Name == "KX_DetSlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgeDetails(e.Key == Key.Right ? DetOffsetStep : -DetOffsetStep);
                    e.Handled = true;
                    return;
                }
                if (fe != null && (fe.Name == "KX_SoundSlider" || fe.Name == "KX_MusicSlider") && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgeVolume(fe.Name == "KX_SoundSlider", e.Key == Key.Right ? 5 : -5);
                    e.Handled = true;
                    return;
                }
                if (fe != null && fe.Name == "KX_StorePriceSlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgePriceSlider(e.Key == Key.Right ? 1 : -1);
                    e.Handled = true;
                    return;
                }
                if (openPanel == "store" && storeReaderOpen && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    // Left / Right: previous / next review (on the filter chips they move between the chips)
                    var chips = UiTools.FindByName(Application.Current.MainWindow, "KX_StoreReaderChips");
                    if (chips == null || !chips.IsAncestorOf(focused))
                    {
                        ReaderStep(e.Key == Key.Right ? 1 : -1);
                        e.Handled = true;
                        return;
                    }
                }
                if (fe != null && fe.Name == "KX_MediaSlider" && (e.Key == Key.Left || e.Key == Key.Right))
                {
                    NudgeMedia(e.Key == Key.Right ? DetOffsetStep : -DetOffsetStep);
                    e.Handled = true;
                    return;
                }
                // the Store's description scrolls with Up / Down, then moves on at its ends
                var scroller = focused as System.Windows.Controls.ScrollViewer;
                if (scroller != null && (fe.Name == "KX_StoreDescScroll" || fe.Name == "KX_StoreReaderScroll") && (e.Key == Key.Up || e.Key == Key.Down))
                {
                    if (e.Key == Key.Down && scroller.VerticalOffset < scroller.ScrollableHeight - 1) { scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 90); e.Handled = true; return; }
                    if (e.Key == Key.Up && scroller.VerticalOffset > 1) { scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset - 90)); e.Handled = true; return; }
                }
                if (ReferenceEquals(e, navDoneEvent)) { e.Handled = true; return; }
                if ((openPanel == "welcome" || openPanel == "profile") && (e.Key == Key.Up || e.Key == Key.Down))
                {
                    int ud = e.Key == Key.Up ? -1 : 1;
                    bool ours = openPanel == "welcome" ? TourNavOurs(fe) : ProfileReorderMoving;
                    if (ours && IsPadRelease(e)) { e.Handled = true; return; }   // (0.99.14) the controller's let-go: nothing moves (it moved twice per press)
                    if (ours && NavIsEcho(ud)) { e.Handled = true; navDoneEvent = e; return; }
                    if (openPanel == "welcome" && TourUpDown(fe, ud)) { e.Handled = true; navDoneEvent = e; NavMoved(ud); return; }   // (0.99.11 / 0.99.12)
                    if (openPanel == "profile" && ProfileMoveKey(ud)) { e.Handled = true; navDoneEvent = e; NavMoved(ud); return; }   // (0.99.11) moving a row
                }
                // Getting started › Pick a look's boxes and Next: Left / Right too (once per press, like Up / Down)
                if (openPanel == "welcome" && (e.Key == Key.Left || e.Key == Key.Right) && TourLookNavOurs(fe))
                {
                    int lr = e.Key == Key.Left ? -2 : 2;   // (told apart from Up / Down's -1 / 1)
                    if (IsPadRelease(e) || NavIsEcho(lr)) { e.Handled = true; return; }
                    if (TourColStep(fe, e.Key) || TourNextBack(fe, e.Key)) { e.Handled = true; NavMoved(lr); return; }
                }
                var panel = UiTools.FindByName(Application.Current.MainWindow, PanelElement(openPanel));
                if (panel == null || !panel.IsAncestorOf(focused)) return;

                FocusNavigationDirection dir = e.Key == Key.Up ? FocusNavigationDirection.Up
                                             : e.Key == Key.Down ? FocusNavigationDirection.Down
                                             : e.Key == Key.Left ? FocusNavigationDirection.Left
                                             : FocusNavigationDirection.Right;
                focused.MoveFocus(new TraversalRequest(dir));
                e.Handled = true;
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: navigation failed"); }
        }

        private KeyEventArgs lastPanelKey;
        private KeyEventArgs navDoneEvent;   // (0.99.13) a press the lists already moved for (the controller's and the keyboard's paths both see it)

        /// <summary>
        /// Up from the top of the games (the Board, or the first row of the grid / the row): always onto the top bar, the button
        /// nearest above (WPF's own guess can miss it, e.g. from a tile far to the right of the profile / shortcuts).
        /// </summary>
        private bool UpToTopBar(KeyEventArgs e)
        {
            if (e.Key != Key.Up || IsPadRelease(e)) return false;
            var win = Application.Current.MainWindow;
            var focused = Keyboard.FocusedElement as FrameworkElement;
            var bar = UiTools.FindByName(win, "KX_TopBar");
            if (focused == null || bar == null || !bar.IsVisible) return false;
            var games = UiTools.FindByName(win, "PART_ListGameItems");
            bool inBoard = BoardHasFocus();
            bool inGames = games != null && games.IsVisible && games.IsKeyboardFocusWithin;
            bool inCarousel = carActive && (inGames || CarProxyFocused());   // Row's carousel: from the card you see (Playnite's row is out of sight)
            if (!inBoard && !inGames && !inCarousel) return false;
            if (inGames && !inCarousel && LayoutNow != 0)   // (0.99.20: Row is one line of games: Up from any of them goes up to the bar)
            {
                // only from the top: the next one up isn't another game (and the grid / list isn't scrolled down)
                var sv = UiTools.FindChild<System.Windows.Controls.ScrollViewer>(games);
                if (sv != null && sv.VerticalOffset > 0.5) return false;
                var up = focused.PredictFocus(FocusNavigationDirection.Up) as DependencyObject;
                if (up != null && games.IsAncestorOf(up)) return false;
            }
            var buttons = new List<FrameworkElement>();
            Collect(bar, buttons);
            var all = buttons.Select(b => Tuple.Create(b, BoundsIn(b, win))).ToList();
            var from = inCarousel ? CarouselHighlightBounds(win) : Rect.Empty;
            if (from.IsEmpty) from = BoundsIn(focused, win);
            // (0.97) the bar is above everything: the button nearest across (a bar that hides itself has its buttons folded to
            // nothing, so "straight above" found none, or a far one, and Up did nothing until you went to the far left)
            double cx = from.Left + from.Width / 2;
            var pick = all.Where(t => !t.Item2.IsEmpty && t.Item1.IsVisible).OrderBy(t => Math.Abs(t.Item2.Left + t.Item2.Width / 2 - cx)).Select(t => t.Item1).FirstOrDefault();
            if (pick == null) return false;
            PlaySound("move");
            Keyboard.Focus(pick);
            return true;
        }

        /// <summary>In the top bar, Left / Right / Up stop at the ends instead of jumping down to the games.</summary>
        private static void KeepInTopBar(KeyEventArgs e)
        {
            var focused = Keyboard.FocusedElement as UIElement;
            var bar = UiTools.FindByName(Application.Current.MainWindow, "KX_TopBar");
            if (focused == null || bar == null || !bar.IsAncestorOf(focused)) return;
            if (e.Key == Key.Up) { e.Handled = true; return; }
            var next = focused.PredictFocus(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right) as DependencyObject;
            if (next != null && bar.IsAncestorOf(next)) return;
            // WPF's guess left the bar (e.g. to a Board tile lower down): the nearest button in the bar that way, if there is one
            e.Handled = true;
            var fe = focused as FrameworkElement;
            var win = Application.Current.MainWindow;
            var buttons = new List<FrameworkElement>();
            Collect(bar, buttons);
            var others = buttons.Where(b => !ReferenceEquals(b, fe)).Select(b => Tuple.Create(b, BoundsIn(b, win))).ToList();
            var pick = fe != null && others.Count > 0 ? Nearest(BoundsIn(fe, win), others, e.Key) : null;
            if (pick != null) Keyboard.Focus(pick);
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (kbOpen || bgMoveOn || splashEditOn || lpOpen || guideBarOpen || galleryEditOpen || ReferenceEquals(e, padHandled)) return;   // the keyboard had them, or OnPadDownEarly already dealt with it (or Move the picture has them)
                if (openPanel == "browser")
                {
                    // a controller button that didn't reach the browser (focus went somewhere else): handle it and put focus back
                    if (IsMainWindow(sender) && !ReferenceEquals(e, lastPanelKey) && e.GetType().Name == "GameControllerInputEventArgs") BrowserKeyFromWindow(e);
                    return;
                }
                if (confirmOpen && IsMainWindow(sender)) { if (!ReferenceEquals(e, lastPanelKey)) HandlePanelKey(e); return; }
                // controller buttons that only arrive here (not on the way down): the description reader and the game page's lower half
                if (descReaderOpen && IsMainWindow(sender) && !ReferenceEquals(e, lastPanelKey)) { DescReaderKey(e); return; }
                if (openPanel == null && detailsOpen && IsMainWindow(sender) && !ReferenceEquals(e, lastPanelKey) && GamePageKey(e)) return;
                if (openPanel == null || !IsMainWindow(sender) || (IsMasterMoving && OutlineHasFocus)) return;
                if (ReferenceEquals(e, lastPanelKey)) return;   // already handled on the way down (PreviewKeyDown)
                HandlePanelKey(e);
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: key handling failed"); }
        }

        /// <summary>B / Esc / Backspace go back; LB / RB step through achievements. True if it handled the key.</summary>
        private bool HandlePanelKey(KeyEventArgs e)
        {
            {
                bool back = e.Key == Key.Escape || e.Key == Key.Back;
                int step = e.Key == Key.PageUp ? -1 : e.Key == Key.PageDown ? 1 : 0;
                int pageJump = step;   // LT / RT (Playnite sends them as Page Up / Page Down)
                bool xPressed = false, yPressed = false;
                if (!back && e.GetType().Name == "GameControllerInputEventArgs")
                {
                    PropertyInfo button = e.GetType().GetProperty("Button");
                    PropertyInfo state = e.GetType().GetProperty("ButtonState");
                    object b = button != null ? button.GetValue(e, null) : null;
                    object st = state != null ? state.GetValue(e, null) : null;
                    bool pressed = st == null || st.ToString() == "Pressed";
                    string name = b != null ? b.ToString() : "";
                    back = name == "B" && pressed;
                    if (pressed && name == "LeftShoulder") step = -1;
                    if (pressed && name == "RightShoulder") step = 1;
                    xPressed = pressed && name == "X";
                    yPressed = pressed && name == "Y";
                    if (!pressed) pageJump = 0;   // LT / RT: only on the press
                }

                if (confirmOpen)
                {
                    // the "are you sure?" box takes every button: B cancels, the rest only move between its two buttons
                    if (back) { CloseConfirm(false); e.Handled = true; return true; }
                    // only moving between its buttons and pressing one get through (nothing reaches the screen underneath)
                    bool padA = false;
                    if (e.GetType().Name == "GameControllerInputEventArgs")
                    {
                        var bp = e.GetType().GetProperty("Button");
                        object bv = bp != null ? bp.GetValue(e, null) : null;
                        padA = bv != null && bv.ToString() == "A";
                    }
                    bool through = padA || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Tab;
                    if (!through) { e.Handled = true; return true; }
                    return false;
                }

                if (openPanel == "search" && SearchPanelKey(e, xPressed)) return true;
                if (openPanel == "page" && PagesPanelKey(e, step, xPressed, back)) { e.Handled = true; return true; }
                if (openPanel == "welcome" && (xPressed || (e.Key == Key.X && !IsPadRelease(e) && e.GetType().Name != "GameControllerInputEventArgs")) && !IsTourProfileOpen && TourZoomToggle()) { e.Handled = true; return true; }   // (0.99.14) Pick a look: the photo big
                if (openPanel == "welcome" && WelcomeKey(e, step)) { e.Handled = true; return true; }   // (r12 G: the tour)
                if (openPanel == "options" && GOptionsKey(e, xPressed, back)) { e.Handled = true; return true; }   // (r12 G: Controller buttons)
                if (openPanel == "quick" && QuickPanelKey(e)) return true;

                if (openPanel == "trailer")
                {
                    ShowTrailerControls();
                    if (step != 0) { SeekTrailer(step * 10); e.Handled = true; return true; }
                }

                if (step != 0 && openPanel == "store" && storeReaderOpen)
                {
                    ReaderStep(step);
                    e.Handled = true;
                    return true;
                }

                if (step != 0 && openPanel == "store" && storeViewerOpen)
                {
                    StoreViewerStep(step);   // LB / RB: previous / next picture or trailer (skipping is on the controls)
                    e.Handled = true;
                    return true;
                }

                if (openPanel == "gallery" && GalleryPanelKey(e, step, pageJump, xPressed, back)) { e.Handled = true; return true; }

                // Store: LT / RT a screen at a time; X on a game tile adds it to (or takes it off) the wishlist
                if (pageJump != 0 && openPanel == "store" && StorePageJump(pageJump)) { e.Handled = true; return true; }
                if (xPressed && openPanel == "store" && WishFromTile()) { e.Handled = true; return true; }

                // Y: search where there's a search (the store, its genre list, achievements)
                // Look packs / Theme Studio: X, Y, LB / RB, LT / RT and B in a colour (ThemeApi.LookPacks.cs / ThemeStudio.cs)
                if (openPanel == "options" && !kbOpen && LookPanelKey(e, step, pageJump, xPressed, yPressed, back)) { e.Handled = true; return true; }
                if (yPressed && !kbOpen && YSearch()) { e.Handled = true; return true; }

                // Game page › Play time and game info: X picks up a line, Up / Down move it, A puts it down
                if (openPanel == "options" && DetInfoMoveKey(e, xPressed, back)) { e.Handled = true; return true; }

                if (step != 0 && openPanel == "achievements" && achPage == "item")
                {
                    StepAchItem(step);
                    e.Handled = true;
                    return true;
                }

                if (back)
                {
                    Back();
                    e.Handled = true;
                    return true;
                }
                return false;
            }
        }

        /// <summary>B: stop moving, then step back a page, then close the panel.</summary>
        private void Back()
        {
            PlaySound("back");
            if (openPanel == "welcome") { WelcomeBack(); return; }
            if (openPanel == "who") { ClosePanels(); return; }
            if (masterMoving != null) { EndMasterMove(false); return; }
            if (widgetMoving != null) { EndWidgetMove(false); return; }
            if (freeMove) { EndFreeMove(null); return; }
            if (openPanel == "trailer") { CloseTrailerFull(); return; }
            if (movingItem != null) { StopMoving(); return; }
            if (openPanel == "options" && optionsPage == "gamepage")
            {
                if (gamePageFromDetails) { ClosePanels(); return; }          // opened from the game page: stay on it
                if (weOpenedDetails) CloseGameDetails(false);               // opened from the options: back to the list, still in the options
                weOpenedDetails = false;
                GoBackTo("", "gamepage");
                return;
            }
            if (openPanel == "options" && optionsPage == "storeopts" && StoreSettingsBack()) return;
            if (openPanel == "options" && optionsPage == "galleryfolders" && GallerySettingsBack()) return;
            if (openPanel == "options" && optionsPage == "picker" && PickerBack()) return;
            if (CollectionsBack()) return;
            if (BoardSettingsBack()) return;
            if (openPanel == "options" && BringPartBack()) return;   // (0.92) Bring a part into view: the page choice → the list
            if (openPanel == "options" && (optionsPage == "gamemore" || optionsPage == "gamestatus" || optionsPage == "dethistory") && GameMoreBack()) return;
            if (openPanel == "options" && optionsPage == "detsaves" && SavesPickBack()) return;   // (0.99.68) the list of backups: back to the saves page
            if (openPanel == "options" && optionsPage == "detsaves") { ClosePanels(); return; }   // opened from the game page's Save backups card: back to it
            if (openPanel == "options" && optionsPage == "profileedit" && profileEditFromTour) { profileEditFromTour = false; ClosePanels(); return; }   // (0.88: back to Getting started)
            if (openPanel == "profile" && ProfileBack()) return;
            if (openPanel == "options" && freeResumePage != null && optionsPage == freeResumePage) { StartFreeMove(false, true); return; }   // X in Free move brought you here
            if (openPanel == "options" && GamePartBack()) return;   // a part's settings opened with X on the game page (or from Top and lower page)
            if (openPanel == "options" && optionsPage != "") { GoBackTo(OptionsParent(optionsPage), BackRowKey(optionsPage)); return; }
            if (openPanel == "achievements" && AchievementsBack()) return;
            if (openPanel == "store" && StoreBack()) return;
            if (openPanel == "gallery" && GalleryBack()) return;
            if (openPanel == "discord" && DiscordBack()) return;
            if ((openPanel == "store" || openPanel == "quick") && ReturnToProfilePages()) return;
            if (openPanel == "audio" && AudioBack()) return;   // the sound list opened from Quick settings: back to it
            ClosePanels();
        }

        // =====================================================================
        // Side panels
        // =====================================================================

        private string openPanel;
        private IInputElement focusBeforePanel;

        public bool IsOptionsOpen { get { return openPanel == "options"; } }
        public bool IsDownloadsOpen { get { return openPanel == "downloads"; } }
        public bool IsDiscordOpen { get { return openPanel == "discord"; } }
        public bool IsAudioOpen { get { return openPanel == "audio"; } }
        public bool IsAnyPanelOpen { get { return openPanel != null; } }
        public bool IsSidePanelOpen { get { return openPanel != null && openPanel != "achievements" && openPanel != "trailer" && openPanel != "master" && openPanel != "store" && openPanel != "browser" && openPanel != "gallery" && openPanel != "search" && openPanel != "page"; } }
        /// <summary>Darken the screen behind a panel, except on settings pages whose changes you want to see as you make them.</summary>
        public bool ShowPanelDim
        {
            get
            {
                if (!IsSidePanelOpen) return false;
                if (openPanel == "welcome") return false;   // (r12 G: the tour has its own background; its look step shows the home screen)
                // live preview: on every page that changes the home screen, the top bar or the game page, the screen behind isn't darkened
                return !(openPanel == "options" && (IsPageUnder(optionsPage, "home") || IsPageUnder(optionsPage, "gamepage") || IsPageUnder(optionsPage, "topbar")
                                                    || optionsPage == "cards" || optionsPage == "cardinfo" || optionsPage == "infoitems" || optionsPage == "preview"
                                                    || optionsPage == "widgets" || optionsPage == "widgetedit"));
            }
        }

        public ICommand ToggleOptionsCommand { get; private set; }
        public ICommand ToggleDownloadsCommand { get; private set; }
        public ICommand ToggleDiscordCommand { get; private set; }
        public ICommand ToggleAudioCommand { get; private set; }
        public ICommand ClosePanelsCommand { get; private set; }
        public ICommand BackCommand { get; private set; }

        private void NotifyPanels()
        {
            Notify("IsOptionsOpen");
            Notify("IsDownloadsOpen");
            Notify("IsDiscordOpen");
            Notify("IsAudioOpen");
            Notify("IsAnyPanelOpen");
            Notify("IsSidePanelOpen");
            Notify("ShowPanelDim");
            Notify("IsAchievementsOpen");
            Notify("IsTrailerFullOpen");
            Notify("IsStoreOpen");
            Notify("IsBrowserOpen");
            Notify("IsGalleryOpen");
            Notify("IsProfileOpen");
            Notify("IsSearchOpen");
            Notify("IsPageOpen");
            Notify("IsQuickOpen");
            Notify("IsControllerPage");
            Notify("IsWhoOpen");
            Notify("IsWelcomeOpen");
            HideToastUnderPanel();
            TipsSoon();   // (r12 G: first-time tips)
            FolderPreviewSoon();   // (Folder: the home screen behind Game cards and layout)
        }

        private static string PanelElement(string name)
        {
            return name == "options" ? "KX_OptionsPanel"
                 : name == "downloads" ? "KX_DownloadsPanel"
                 : name == "discord" ? "KX_DiscordPanel"
                 : name == "achievements" ? "KX_AchievementsView"
                 : name == "trailer" ? "KX_TrailerView"
                 : name == "master" ? "KX_MasterLayer"
                 : name == "store" ? "KX_StoreView"
                 : name == "browser" ? "KX_BrowserView"
                 : name == "gallery" ? "KX_GalleryView"
                 : name == "profile" ? "KX_ProfilePanel"
                 : name == "search" ? "KX_SearchView"
                 : name == "page" ? "KX_PagesView"
                 : name == "quick" ? "KX_QuickPanel"
                 : name == "who" ? "KX_WhoView"
                 : name == "welcome" ? "KX_WelcomeView"
                 : "KX_AudioPanel";
        }

        private void TogglePanel(string name)
        {
            if (name == "options" && openPanel != "options" && ProfileGate("settings", () => TogglePanel("options"))) return;   // a profile whose settings need the PIN
            freeResumePage = null;
            AbortMoves();
            if (openPanel == name) { ClosePanels(); return; }
            if (openPanel == null) { focusBeforePanel = Keyboard.FocusedElement; RememberLowerFocus(); }
            StopMoving();
            if (name != "store") storeSettingsFromStore = false;
            if ((openPanel == "gallery" || gallerySettingsFromGallery) && name != "gallery") LeaveGallery();
            if (openPanel == "achievements" && name != "achievements") LeaveAchBackground();
            if (name != "gallery") gallerySettingsFromGallery = false;
            if (openPanel == "store") CloseStoreOverlays();
            if (openPanel == "browser") { StopBrowserInput(); DisposeBrowser(); browserReturn = null; }
            if (openPanel == "page" && name != "page") LeavePages();

            if (name == "audio") RefreshAudio();
            if (name == "downloads") RefreshDownloadsAsync();
            if (name == "discord") { SetDiscordPage("main"); RefreshDiscord(); }
            if (name == "options") SetOptionsPage("", false);

            openPanel = name;
            NotifyPanels();
            FocusPanelSoon();
            UpdateTopBarFade();
        }

        /// <summary>A panel opening / closing while something is being placed: undo the move and let go (nothing stays stuck).</summary>
        private void AbortMoves()
        {
            StopDrag();
            if (widgetMoving != null) AbortWidgetMove();
            if (freeMove) { EditEnd(); freeMove = false; freeTarget = freeTargetName = null; NotifyMove(); }
            if (masterMoving != null)
            {
                RevertSizing();   // sizing cut short: the size it had
                SetMasterOffset(masterMoving.Key, moveStartX, moveStartY);
                SetMasterRotation(masterMoving.Key, moveStartRot);
                RestoreMoveStartSize(masterMoving);   // (and a size Y took back while moving)
                masterMoving = null;
                SaveSettings();
                ApplyMasterOffsets();
                RefreshMasterItems();
                NotifyMove();
            }
        }

        private void FocusPanelSoon()
        {
            if (openPanel == null) return;
            string element = PanelElement(openPanel);
            ui.BeginInvoke(new Action(() => UiTools.FocusInside(element)), DispatcherPriority.ContextIdle);
        }

        private void ClosePanels()
        {
            if (kbOpen) CloseKeyboard(false);
            if (openPanel == null) return;
            if (openPanel == "master" || openPanel == "options") freeFromHold = false;
            string closing = openPanel;
            if (closing == "search") searchPick = null;   // picking a game: closed without one
            storeSettingsFromMenu = false;
            profileReturnKey = null;
            freeResumePage = null;
            AbortMoves();
            StopMoving();
            if (openPanel == "discord") LeaveDiscordPages();
            if (openPanel == "store") CloseStoreOverlays();
            if (openPanel == "gallery" || gallerySettingsFromGallery) LeaveGallery();
            if (openPanel == "achievements") LeaveAchBackground();
            if (openPanel == "browser") { StopBrowserInput(); DisposeBrowser(); browserReturn = null; }
            if (openPanel == "page") LeavePages();
            storeSettingsFromStore = false;
            gallerySettingsFromGallery = false;
            var back = focusBeforePanel as UIElement;
            focusBeforePanel = null;
            // (0.99.60) opened from the top bar (with Auto-hide on): the highlight goes back onto the bar BEFORE the panel goes,
            // and the bar is kept out meanwhile. The panel going took the highlight with it first (nowhere for a moment), the bar
            // folded away, and only when the highlight was put back (a moment or seconds later) did it come out again.
            bool backToBar = false;
            try
            {
                if (topBarEl == null || !topBarEl.IsLoaded) topBarEl = UiTools.FindByName(Application.Current.MainWindow, "KX_TopBar");
                backToBar = (back != null && topBarEl != null && topBarEl.IsAncestorOf(back)) || (closing == "profile" && !detailsOpen);
                if (backToBar)
                {
                    topBarActive = true;
                    topBarHoldUntil = DateTime.UtcNow.AddSeconds(2);
                    if (back != null && back.IsVisible && back.Focusable) Keyboard.Focus(back);
                    else if (closing == "profile") UiTools.FocusByNameIfVisible("KX_ProfileButton");
                }
            }
            catch (Exception) { }
            openPanel = null;
            NotifyPanels();
            if (closing == "options" && BoardSizeClash()) BoardResolveSizeClash();   // a tile made bigger over others: sort it out on the board
            UpdateTopBarFade();
            ui.BeginInvoke(new Action(() =>
            {
                // the profile menu always hands the highlight back to the profile button
                if (closing == "profile" && !detailsOpen && UiTools.FocusByNameIfVisible("KX_ProfileButton")) return;
                if (RestoreLowerFocus()) return;   // a lower game page card: back on it, the lower page still showing
                if (back != null && back.IsVisible && back.Focusable) { Keyboard.Focus(back); if (!back.IsKeyboardFocused && topBarActive && !(Keyboard.FocusedElement is DependencyObject && topBarEl != null && topBarEl.IsAncestorOf((DependencyObject)Keyboard.FocusedElement))) { topBarActive = false; UpdateTopBarFade(); } }
                else
                {
                    if (IsBoardMode && (boardEditing || closing == "search") && FocusBoard()) return;   // editing the board / picking a tile's game: back on the board
                    var dots = Shortcuts.FirstOrDefault(x => x.IsOptions && x.IsShown);
                    if (dots != null) UiTools.FocusItemPart("KX_TopShortcuts", dots, "KX_TopOptions"); else FocusSelectedCard();
                }
            }), DispatcherPriority.ContextIdle);
        }

        // =====================================================================
        // Options menu (categories)
        // =====================================================================

        private string optionsPage = "";

        public bool IsOptionsRoot { get { return optionsPage == ""; } }
        public bool IsOptionsShortcuts { get { return optionsPage == "shortcuts"; } }
        public bool IsOptionsHome { get { return optionsPage == "home"; } }
        public bool IsOptionsInfoItems { get { return optionsPage == "infoitems"; } }
        public bool IsOptionsCards { get { return optionsPage == "cards"; } }
        public bool IsOptionsCardInfo { get { return optionsPage == "cardinfo"; } }
        public bool IsOptionsTopBar { get { return optionsPage == "topbar"; } }
        public bool IsOptionsGamePage { get { return optionsPage == "gamepage"; } }
        public bool IsOptionsGameMedia { get { return optionsPage == "gamemedia"; } }
        public bool IsOptionsAudio { get { return optionsPage == "audio"; } }
        public bool IsOptionsGeneral { get { return optionsPage == "general"; } }
        public bool IsOptionsPreview { get { return optionsPage == "preview"; } }
        public bool IsOptionsGameLayout { get { return optionsPage == "gamelayout"; } }
        public bool IsOptionsGameExtras { get { return optionsPage == "gameextras"; } }
        public bool IsOptionsGameFriends { get { return optionsPage == "gamefriends"; } }
        public bool IsOptionsStore { get { return optionsPage == "storeopts"; } }
        public bool IsOptionsStoreCurrency { get { return optionsPage == "storecurrency"; } }
        // widgets / layouts pages: ThemeApi.Widgets.cs, ThemeApi.Layouts.cs
        // IsOptionsStoreTiles / IsOptionsStoreRows: ThemeApi.StorePage.cs

        public string OptionsTitle
        {
            get
            {
                switch (optionsPage)
                {
                    case "shortcuts": return Loc("LOC_KX_CatShortcuts", "Shortcuts");
                    case "home": return Loc("LOC_KX_CatHome", "Home settings");
                    case "infoitems": return Loc("LOC_KX_CatInfoItems", "Game info details");
                case "cards": return Loc("LOC_KX_CatCards", "Game cards and layout");
                case "cardinfo": return Loc("LOC_KX_CatCardInfo", "Logo, game info & button hints");
                case "topbar": return Loc("LOC_KX_CatTopBar", "Top bar");
                case "preview": return Loc("LOC_KX_CatPreview", "Trailer preview");
                case "gamelayout": return Loc("LOC_KX_CatGameLayout", "Layout");
                case "gameextras": return Loc("LOC_KX_CatGameExtras", "Buttons & extras");
                case "gamefriends": return Loc("LOC_KX_CatGameFriends", "Friends");
                case "storeopts": return Loc("LOC_KX_StoreSettings", "Store settings");
                case "storecurrency": return Loc("LOC_KX_PricesIn", "Prices in");
                case "storetiles": return Loc("LOC_KX_AvailableStores", "Available stores");
                case "widgets": return Loc("LOC_KX_Widgets", "Widgets");
                case "widgetadd": return Loc("LOC_KX_WidgetAdd", "Add a widget");
                case "widgetedit": return EditWidgetName;
                case "widgetchoice": return WidgetChoiceTitle;
                case "help": return Loc("LOC_KX_CatHelp", "Help");
                case "weatherpick": return Loc("LOC_KX_WeatherWhich", "Which one?");
                case "layouts": return Loc("LOC_KX_Layouts", "Layouts & reset");
                case "layoutedit": return EditLayoutName;
                case "storerows": return Loc("LOC_KX_FrontPageRows", "Front page rows");
                case "gamepage": return Loc("LOC_KX_CatGamePage", "Game page");
                case "gamemedia": return Loc("LOC_KX_CatGameMedia", "Game media");
                case "audio": return Loc("LOC_KX_CatAudio", "Audio");
                case "general": return Loc("LOC_KX_CatGeneral", "General settings");
                case "notify": return Loc("LOC_KX_CatNotify", "Notifications");
                case "picker": return PickerTitle ?? "";
                case "layoutpick": return Loc("LOC_KX_Layout", "Layout");
                case "galleryfolders": return Loc("LOC_KX_GallerySettingsTitle", "Gallery settings");
                case "homerows": return Loc("LOC_KX_HomeRowsTitle", "Rows and grid");
                case "extrarows": return Loc("LOC_KX_ExtraRowsTitle", "Extra rows");
                case "badges": return Loc("LOC_KX_BadgesTitle", "Tile badges");
                case "board": return IsManualMode ? Loc("LOC_KX_ManualBoardTitle", "Manual Board") : Loc("LOC_KX_BoardTitle", "Board");
                case "boardcard": return Loc("LOC_KX_TileSettingsTitle", "Tile settings");
                case "boardadd": return boardAddCat != null ? BoardAddCatName(boardAddCat) : boardChangeKind != null ? Loc("LOC_KX_TileType", "Tile type") : Loc("LOC_KX_AddTileTitle", "Add a tile");
                case "boardpresets": return Loc("LOC_KX_PresetsTitle", "Tile presets");
                case "boardpages": return Loc("LOC_KX_Pages", "Pages");
                case "access": return Loc("LOC_KX_CatAccess", "Accessibility");
                case "language": return Loc("LOC_KX_Language", "Language");
                case "settingssearch": return Loc("LOC_KX_SearchSettings", "Search settings");
                case "seasonal": return Loc("LOC_KX_SeasonalOn", "Seasonal touches");
                case "weatherfx": return Loc("LOC_KX_WeatherFxOn", "Weather touches");
                case "slotpins": return Loc("LOC_KX_SlotPinsTitle", "Games kept in place");
                case "slotpin": { Guid sid; Playnite.SDK.Models.Game sg = null; if (slotPinEditing != null && Guid.TryParse(slotPinEditing, out sid)) try { sg = api.Database.Games.Get(sid); } catch (Exception) { } return sg != null ? sg.Name : Loc("LOC_KX_SlotPinsTitle", "Games kept in place"); }
                case "addons": return Loc("LOC_KX_OtherAddons", "Other add-ons");
                case "ups": return "UniPlaySong";
                case "bgc": return "BackgroundChanger";
                case "detsaves": return Loc("LOC_KX_SecSaves", "Save backups");
                case "profiles": return Loc("LOC_KX_Profiles", "Profiles");
                case "profileedit": { var ep = EditingProfile; return ep != null ? ep.Name : Loc("LOC_KX_Profiles", "Profiles"); }
                case "profilesteam": return Loc("LOC_KX_SteamAccount", "Steam account");
                case "profiletime": return Loc("LOC_KX_LimitTitle", "Daily time limit");
                case "boardlibrary": return Loc("LOC_KX_LibraryHoldsRow", "The lists it holds");
                case "folders": return Loc("LOC_KX_FoldersTitle", "Your folders");
                case "folder": return FolderEditTitle;
                case "folderpicks": return Loc("LOC_KX_FolderPicksTitle", "Games picked by hand");
                case "boardpage": { var bp = BPages != null ? BPages.FirstOrDefault(x => x.Id == boardEditPageId) : null; return bp != null ? bp.Name : Loc("LOC_KX_Pages", "Pages"); }
                case "boardsource": return Loc("LOC_KX_BoardSource", "Games from");
                case "boardgroup": return Loc("LOC_KX_GroupTitle", "Group");
                case "look": return Loc("LOC_KX_LookTitle", "Look and feel");
                case "lookpacks": return Loc("LOC_KX_LookPacks", "Look packs");
                case "lookpack": return LookPackEditTitle;
                case "studio": return Loc("LOC_KX_ThemeStudio", "Theme Studio");
                case "intro": case "nightmode": case "detanim": return LookExtraTitle;   // (ThemeApi.Intro.cs)
                case "background": return Loc("LOC_KX_BgTitle", "Background");
                case "placehome":
                case "placegame": return Loc("LOC_KX_PlaceTitle", "Put things back");
                case "presets": return Loc("LOC_KX_PresetsTitle", "Presets");
                case "bringpart": return Loc("LOC_KX_BringTitle", "Bring a part into view");
                case "gamesections": return Loc("LOC_KX_GameSectionsTitle", "Top and lower page");
                case "detpart": return DetPartTitle;
                case "dethistory": return string.Format(Loc("LOC_KX_HistoryTitle", "Play history: {0}"), GameMoreTitle);
                case "gamemore": return GameMoreTitle;
                case "listinfo": return Loc("LOC_KX_ListInfoShows", "Play info shows");
                case "gamestatus": return Loc("LOC_KX_StatusRating", "Status and rating");
                case "splash": return Loc("LOC_KX_CatSplash", "Splash screen layout");   // (1.0.1, ThemeApi.SplashLayout.cs)
                }
                string gTitle = GOptionsTitle(optionsPage);   // (r12 G: Controller buttons, Handheld)
                if (gTitle != null) return gTitle;
                string cTitle = CommunityOptionsTitle(optionsPage);   // (Settings › Community: ThemeApi.CommunityHub.cs)
                if (cTitle != null) return cTitle;
                return Loc("LOC_KX_Options", "Settings & Customisation");
            }
        }

        public ICommand OpenOptionsPageCommand { get; private set; }

        private string OptionsParent(string page)
        {
            switch (page)
            {
                case "widgetchoice":
                    if (choicePage == "back" && colorTarget != null) return colorBackPage ?? "";   // the theme's own colour (home background, a card)
                    return choicePage == "subject" && subjectAddType != null ? "widgetadd" : "widgetedit";
                case "weatherpick": return weatherPickFrom ?? "widgets";
                case "listinfo": return "cards";
                case "shortcuts": return "topbar";
                case "cards":
                case "cardinfo":
                case "preview": return "home";
                case "infoitems": return "cardinfo";
                case "gamelayout":
                case "gamemedia":
                case "gameextras":
                case "gamefriends":
                case "detanim":
                case "splash": return "gamepage";
                case "widgets": return "home";
                case "widgetadd":
                case "widgetedit": return "widgets";
                case "layoutedit":
                case "layoutpick": return "layouts";
                case "picker": return pickerReturn ?? "";
                case "homerows":
                case "badges": return "home";
                case "extrarows": return "cards";
                case "board": return "cards";
                case "boardcard": return "board";
                case "boardsource": return boardSourceGroup != null ? "boardgroup" : "boardcard";
                case "boardgroup": return "boardcard";
                case "boardpresets": return "boardcard";
                case "boardpages": return "board";
                case "language": return "general";
                case "addons": return "general";
                case "ups": return "audio";
                case "bgc": return "background";
                case "seasonal": return "home";
                case "weatherfx": return "home";
                case "slotpins": return "home";
                case "slotpin": return "slotpins";
                case "profileedit": return "profiles";
                case "profilesteam": return "profileedit";
                case "profiletime": return limitFromProfilesRoot ? "profiles" : "profileedit";   // (Profiles › Daily time limits opens it straight from the root)
                case "boardlibrary": return "boardcard";
                case "folders": return "cards";
                case "folder": return "folders";
                case "folderpicks": return "folder";
                case "boardpage": return "boardpages";
                case "boardadd": return boardChangeKind != null ? "boardcard" : "board";
                case "look": return "general";   // (0.86: its own section in General settings)
                case "lookpacks":
                case "studio": return "look";
                case "lookpack": return "lookpacks";
                case "intro": case "nightmode": return "look";
                case "background": return "home";
                case "placehome": return "home";
                case "placegame": return "gamelayout";   // (under Free move, at the top of Game page › Layout)
                case "presets": return presetSlot == "game" ? "gamelayout" : "cards";   // (0.91)
                case "bringpart": return "gamelayout";   // (0.92)
                case "gamesections": return "gamepage";
                case "detpart": return "gamesections";
                case "dethistory": return "";
                case "gamestatus": return "";
                case "storecurrency":
                case "storetiles":
                case "storerows": return "storeopts";
            }
            return GOptionsParent(page) ?? CommunityOptionsParent(page) ?? "";   // (r12 G pages, Community, else the root)
        }

        /// <summary>The page itself, or one of the pages inside it.</summary>
        private bool IsPageUnder(string page, string root)
        {
            for (int i = 0; i < 10 && page != null; i++)
            {
                if (page == root) return true;
                if (page == "") return false;
                page = OptionsParent(page);
            }
            return false;
        }

        /// <summary>Back from a page: the row on the page above that opened it.</summary>
        private string BackRowKey(string page)
        {
            if (page == "widgetchoice" && choicePage == "back" && colorTarget != null) return colorBackKey;
            if (page == "widgetchoice")
                return choicePage == "subject" ? (subjectAddType ?? "subject") : choicePage == "text" ? "colortext" : "colorback";
            if (page == "weatherpick") return weatherPickFrom == "weatherfx" ? "wxtown" : "weather";
            if (page == "detpart") return "part:" + detPartKey;
            if (page == "presets") return presetSlot == "game" ? "gamepresets" : "presets";
            if (page == "folder") return "folder:" + folderEditId;
            if (page == "folderpicks") return "fpicks";
            if (page == "lookpack" && lookEditing != null) return "pack:" + lookEditing.Id;
            if (page == "profiletime" && limitFromProfilesRoot) return "limit:" + editingProfileId;
            string cKey = CommunityBackRowKey(page);   // (Community: back to the note that opened it)
            if (cKey != null) return cKey;
            return page;
        }

        /// <summary>Goes up a page and puts the highlight back on the row you came from.</summary>
        private void GoBackTo(string parent, string child)
        {
            SetOptionsPage(parent, false);
            ui.BeginInvoke(new Action(() =>
            {
                if (!UiTools.FocusCommandParameter("KX_OptionsPanel", child)) UiTools.FocusInside("KX_OptionsPanel");
            }), DispatcherPriority.ContextIdle);
        }

        private void SetOptionsPage(string page)
        {
            SetOptionsPage(page, true);
        }

        private void SetOptionsPage(string page, bool focus)
        {
            if (page == "homerows") page = "cards";   // 0.34: Rows and grid is part of the Cards page
            StopMoving();
            optionsPage = page ?? "";
            if (freeResumePage != null && !IsPageUnder(optionsPage, freeResumePage)) freeResumePage = null;   // went somewhere else: B works as usual
            if (optionsPage == "storeopts" || optionsPage == "storetiles" || optionsPage == "storerows") { BuildStoreToggles(); NotifyStoreSettings(); }
            if (optionsPage == "storeopts") CheckSteamSignInSoon();
            if (optionsPage == "storecurrency") BuildRegionOptions();
            if (optionsPage == "help") CheckForUpdatesOnce();   // (1.0.1) Help says straight away when there's a new version
            Notify("IsOptionsRoot");
            Notify("IsOptionsShortcuts");
            Notify("IsOptionsHome");
            Notify("IsOptionsInfoItems");
            Notify("IsOptionsCards");
            Notify("IsOptionsCardInfo");
            Notify("IsOptionsTopBar");
            Notify("IsOptionsPreview");
            Notify("IsOptionsGameLayout");
            Notify("IsOptionsGameExtras");
            Notify("IsOptionsGameFriends");
            Notify("IsOptionsStore");
            Notify("IsOptionsStoreCurrency");
            Notify("IsOptionsStoreTiles");
            Notify("IsOptionsStoreRows");
            Notify("IsOptionsWidgets");
            Notify("IsOptionsWidgetAdd");
            Notify("IsOptionsWidgetEdit");
            Notify("IsOptionsWidgetChoice");
            Notify("IsOptionsWeatherPick");
            Notify("IsOptionsHelp");
            if (optionsPage == "help") { SetHelpStatus(""); Notify("HelpVersionText"); }
            Notify("IsOptionsLayouts");
            Notify("IsOptionsLayoutEdit");
            Notify("IsOptionsLayoutPick");
            Notify("IsOptionsPicker");
            Notify("IsOptionsGalleryFolders");
            Notify("IsOptionsHomeRows");
            Notify("IsOptionsExtraRows");
            Notify("IsOptionsBadges");
            Notify("IsOptionsLook");
            Notify("IsOptionsBackground");
            Notify("IsOptionsPlace");
            Notify("IsOptionsPresets");
            Notify("IsOptionsBringPart");
            if (optionsPage == "cards" || optionsPage == "gamelayout") NotifyPresets();
            Notify("IsOptionsGameSections");
            Notify("IsOptionsGameMore");
            Notify("IsOptionsListInfo");
            Notify("IsOptionsGameStatus");
            if (optionsPage == "galleryfolders") BuildGalleryFolderRows();
            Notify("IsOptionsSplash");
            if (optionsPage == "splash") BuildSplashRows();   // (1.0.1)
            if (optionsPage == "cards" || optionsPage == "home" || optionsPage == "background") UpdateSliders();
            if (optionsPage == "extrarows") BuildExtraRowChoices();
            if (optionsPage == "layouts") { RefreshLayoutProfiles(); Notify("HasUndoReset"); }
            if (optionsPage == "widgets" || optionsPage == "widgetedit") NotifyWidgetEdit();
            Notify("IsOptionsGamePage");
            Notify("IsOptionsGameMedia");
            Notify("IsOptionsAudio");
            Notify("IsOptionsGeneral");
            Notify("IsOptionsNotify");
            Notify("ShowPanelDim");
            GamePagesOptionsOpened(optionsPage);
            NewPagesOptionsOpened(optionsPage);
            BoardOptionsOpened(optionsPage);
            AccessOptionsOpened(optionsPage);
            SettingsSearchOpened(optionsPage);
            SeasonalOptionsOpened(optionsPage);
            LookExtraOptionsOpened(optionsPage);   // intro / outro, night mode, animated art (ThemeApi.Intro.cs)
            SlotPinsOptionsOpened(optionsPage);
            FolderOptionsOpened(optionsPage);
            AddonOptionsOpened(optionsPage);
            MediaAddonOptionsOpened(optionsPage);   // (0.98) UniPlaySong, BackgroundChanger (ThemeApi.MediaAddons.cs)
            ProfilesOptionsOpened(optionsPage);
            GOptionsOpened(optionsPage);   // (r12 G: Controller buttons, Handheld)
            CommunityOptionsOpened(optionsPage);   // Settings › Community (ThemeApi.CommunityHub.cs)
            LookOptionsOpened(optionsPage);
            Notify("OptionsTitle");
            if (focus) FocusPanelSoon();
        }

        // ---- Home settings ----

        public ICommand CycleHintsPositionCommand { get; private set; }
        public ICommand CycleLogoPositionCommand { get; private set; }
        public ICommand CycleInfoPositionCommand { get; private set; }
        public ICommand ToggleClockCommand { get; private set; }

        private bool SetSetting(ref bool field, bool value)
        {
            if (field == value) return false;
            field = value;
            SaveSettings();
            NotifyHome();
            return true;
        }

        public bool ShowButtonHints { get { return settings.ShowButtonHints; } set { if (SetSetting(ref settings.ShowButtonHints, value) && value) HomePartBackOnTop("home.hints"); } }
        public bool ShowLogo { get { return settings.ShowLogo; } set { if (SetSetting(ref settings.ShowLogo, value) && value) HomePartBackOnTop("home.logo"); } }
        public bool ShowGameInfo { get { return settings.ShowGameInfo; } set { if (SetSetting(ref settings.ShowGameInfo, value) && value) HomePartBackOnTop("home.info"); } }
        public bool ShowNotificationIcon { get { return settings.ShowNotificationIcon; } set { SetSetting(ref settings.ShowNotificationIcon, value); } }
        public bool ShowBatteryPercent
        {
            get { return settings.ShowBatteryPercent; }
            set { if (SetSetting(ref settings.ShowBatteryPercent, value)) { Notify("HasBatteryText"); } }
        }

        public bool InfoPlaytime { get { return settings.InfoPlaytime; } set { SetSetting(ref settings.InfoPlaytime, value); } }
        public bool InfoLastPlayed { get { return settings.InfoLastPlayed; } set { SetSetting(ref settings.InfoLastPlayed, value); } }
        public bool InfoAchievements { get { return settings.InfoAchievements; } set { SetSetting(ref settings.InfoAchievements, value); } }
        public bool InfoPlatform { get { return settings.InfoPlatform; } set { SetSetting(ref settings.InfoPlatform, value); } }
        public bool InfoSource { get { return settings.InfoSource; } set { SetSetting(ref settings.InfoSource, value); } }
        public bool InfoCompletion { get { return settings.InfoCompletion; } set { SetSetting(ref settings.InfoCompletion, value); } }
        public bool InfoReleaseYear { get { return settings.InfoReleaseYear; } set { SetSetting(ref settings.InfoReleaseYear, value); } }

        private static HorizontalAlignment Align(int pos)
        {
            return pos == 1 ? HorizontalAlignment.Center : pos == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private static TextAlignment TextAlign(int pos)
        {
            return pos == 1 ? TextAlignment.Center : pos == 2 ? TextAlignment.Right : TextAlignment.Left;
        }

        private string PositionText(int pos)
        {
            return pos == 1 ? Loc("LOC_KX_Center", "Centre") : pos == 2 ? Loc("LOC_KX_Right", "Right") : Loc("LOC_KX_Left", "Left");
        }

        // with Master edit on for the home screen, the position settings are ignored (elements start from the default spot)
        private int HomePos(int pos) { return pos; }
        public HorizontalAlignment HintsAlignment { get { return Align(ListModePos(HomePos(settings.HintsPosition))); } }
        public HorizontalAlignment LogoAlignment { get { return Align(ListModePos(HomePos(settings.LogoPosition))); } }
        public TextAlignment LogoTextAlignment { get { return TextAlign(ListModePos(HomePos(settings.LogoPosition))); } }
        public HorizontalAlignment InfoAlignment { get { return Align(ListModePos(HomePos(settings.InfoPosition))); } }
        /// <summary>The list down one side: logo and info line up on the far side from the list (right when the list is on the left).</summary>
        private int ListModePos(int pos) { return LayoutNow == 2 ? (settings.ListSide == 0 ? 2 : 0) : pos; }
        public string HintsPositionText { get { return PositionText(settings.HintsPosition); } }
        public string LogoPositionText { get { return PositionText(settings.LogoPosition); } }
        public string InfoPositionText { get { return PositionText(settings.InfoPosition); } }

        // Darker shade on whichever side the game's details sit
        private bool AnyOn(int pos)
        {
            if (CarHidesInfo) return false;   // (Row › Tall / Full Screen, Folder's tiles: none of them show)
            return (settings.ShowLogo && HomePos(settings.LogoPosition) == pos) || (settings.ShowGameInfo && HomePos(settings.InfoPosition) == pos)
                || (settings.ShowButtonHints && HomePos(settings.HintsPosition) == pos);
        }
        public bool ShadeLeft { get { return AnyOn(0); } }
        public bool ShadeCenter { get { return AnyOn(1); } }
        public bool ShadeRight { get { return AnyOn(2); } }

        public string ClockModeText { get { return settings.Clock24h ? Loc("LOC_KX_Clock24", "24-hour") : Loc("LOC_KX_Clock12", "12-hour"); } }

        private void NotifyHome()
        {
            foreach (string n in new[] { "ShowButtonHints", "ShowLogo", "ShowGameInfo", "ShowNotificationIcon", "ShowBatteryPercent",
                                         "InfoPlaytime", "InfoLastPlayed", "InfoAchievements", "InfoPlatform", "InfoSource", "InfoCompletion", "InfoReleaseYear",
                                         "HintsAlignment", "LogoAlignment", "LogoTextAlignment", "InfoAlignment", "LogoAreaWidth", "LogoInnerAlignment", "LogoInnerTextAlignment",
                                         "LogoAnchorText", "LogoAnchorSub", "HintsPositionText", "LogoPositionText", "InfoPositionText",
                                         "ShadeLeft", "ShadeCenter", "ShadeRight", "ClockModeText",
                                         "HintsSizeText", "LogoSizeText", "InfoSizeText",
                                         "ShowFilterLabel", "FilterAbove", "FilterAlignment",
                                         "FilterAboveMargin",
                                         "CardsMargin", "CardOffsetText", "CardKnobMargin", "CardFillWidth" })
                Notify(n);
        }

        // =====================================================================
        // Home, achievements, hibernate, Playnite commands
        // =====================================================================

        public ICommand HomeCommand { get; private set; }
        public ICommand OpenAchievementsCommand { get; private set; }
        public ICommand HibernateCommand { get; private set; }

        private void GoHome()
        {
            if (TourLocked) return;   // (1.0.1) the Home / Guide button doesn't close an unfinished Getting started
            ClosePanels();
            if (FolderGoHome()) return;   // Folder: home is the folder tiles, on the folder you were in
            UiTools.ScrollLibraryToStart();
        }

        /// <summary>Runs one of Playnite's own fullscreen commands (OpenSearchCommand, OpenMainMenuCommand...).</summary>
        private void RunMainCommand(string name)
        {
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                object vm = w != null ? w.DataContext : null;
                if (vm == null) return;
                PropertyInfo p = vm.GetType().GetProperty(name);
                var cmd = p != null ? p.GetValue(vm, null) as ICommand : null;
                if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: couldn't run " + name); }
        }

        public ICommand DesktopModeCommand { get; private set; }

        /// <summary>Closes our menu first, so Playnite's own menu gets the controller to itself.</summary>
        private void CloseThenRun(string command)
        {
            ClosePanels();
            ui.BeginInvoke(new Action(() => RunMainCommand(command)), DispatcherPriority.ContextIdle);
        }

        /// <summary>Sleep, restart or power off, after asking.</summary>
        private void AskPower(string what)
        {
            PowerAction(what == "sleep" || what == "restart" ? what : "shutdown");
        }

        private void AskHibernate()
        {
            Confirm(Loc("LOC_KX_HibernateAsk2", "Hibernate the PC?"),
                    Loc("LOC_BBH_HibernateAsk", "Hibernate the PC now? Everything open, including games, picks up where you left off when you turn it back on."),
                    Loc("LOC_KX_Hibernate", "Hibernate"),
                    () => WithOutro("hibernate", () => { ClosePanels(); SaveSettings(); KippixShared.SafeWork.Queue(_ => { Thread.Sleep(500); Native.Hibernate(); }); }));
        }
    }
}
