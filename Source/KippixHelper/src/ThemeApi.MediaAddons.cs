using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (0.98) UniPlaySong and BackgroundChanger, when they're installed.
    ///
    /// UniPlaySong (game music): Settings › Audio › Music has a fourth choice, UniPlaySong (picked by itself the first time it's
    /// found in fullscreen). While it's picked Kippix's own player is quiet and the volume bar is UniPlaySong's; any other choice
    /// (Off too) keeps UniPlaySong quiet in fullscreen through its UPS_MusicControl element, its settings untouched (so desktop
    /// mode plays as before). Its page
    /// (Settings › Audio › UniPlaySong) sets what it plays, through the same settings its desktop window has. Kippix's own sounds
    /// that need the room (a trailer with sound, the store's videos, the gallery, the intro) quieten it through its documented
    /// UPS_MusicControl element (Main.xaml). Skip / next song uses its playnite://uniplaysong/skip link.
    ///
    /// BackgroundChanger (more backgrounds and covers per game, pictures or videos, changing on their own): its
    /// BackgroundChanger_PluginBackgroundImage element sits on the home screen's background (when the background is the game's
    /// art) and the game page's; its BackgroundChanger_PluginCoverImage over the game page's cover. Settings › Home settings ›
    /// Background › BackgroundChanger switches them and sets how its pictures change.
    ///
    /// Both are reached by reflection (no reference to either: they stay separately versioned) and their settings are saved
    /// with Playnite's SavePluginSettings, as their own windows do.
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly Guid UpsId = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        private static readonly Guid BcId = new Guid("3afdd02b-db6c-4b60-8faa-2971d6dfad2a");

        // ---------------- reaching them ----------------

        private object PluginObj(Guid id)
        {
            try { return api.Addons.Plugins.FirstOrDefault(p => p != null && p.Id == id); } catch (Exception) { return null; }
        }

        private static object PropOf(object o, string name)
        {
            if (o == null) return null;
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p != null ? p.GetValue(o, null) : null;
            }
            catch (Exception) { return null; }
        }

        private static bool SetPropOf(object o, string name, object value)
        {
            if (o == null) return false;
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return false;
                if (p.PropertyType.IsEnum && !(value is Enum)) value = Enum.ToObject(p.PropertyType, Convert.ToInt32(value));
                else if (p.PropertyType != value.GetType()) value = Convert.ChangeType(value, p.PropertyType);
                p.SetValue(o, value, null);
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: add-on setting " + name + ": " + ex.Message); return false; }
        }

        private static bool BoolOf(object o, string name) { var v = PropOf(o, name); return v is bool && (bool)v; }
        private static int IntOf(object o, string name, int fallback) { var v = PropOf(o, name); try { return v != null ? Convert.ToInt32(v) : fallback; } catch (Exception) { return fallback; } }

        /// <summary>Saves another add-on's settings object the way its own settings window does.</summary>
        private void SaveOtherPlugin(object pluginObj, object settingsObj, string what)
        {
            if (pluginObj == null || settingsObj == null) return;
            try
            {
                var save = typeof(Playnite.SDK.Plugins.Plugin).GetMethods().FirstOrDefault(m => m.Name == "SavePluginSettings" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (save != null) save.MakeGenericMethod(settingsObj.GetType()).Invoke(pluginObj, new[] { settingsObj });
            }
            catch (Exception ex) { logger.Warn("Kippix: saving " + what + "'s settings: " + ex.Message); }
        }

        private readonly Dictionary<string, DispatcherTimer> otherSaveTimers = new Dictionary<string, DispatcherTimer>();

        /// <summary>Written a moment after the last change (a volume bar held down is one write).</summary>
        private void SaveOtherPluginSoon(string what, Func<object> pluginObj, Func<object> settingsObj)
        {
            DispatcherTimer t;
            if (!otherSaveTimers.TryGetValue(what, out t))
            {
                t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(1200) };
                t.Tick += (s, e) => { t.Stop(); SaveOtherPlugin(pluginObj(), settingsObj(), what); };
                otherSaveTimers[what] = t;
            }
            t.Stop();
            t.Start();
        }

        // =====================================================================
        // UniPlaySong
        // =====================================================================

        private object upsPlugin, upsSettings;
        private bool upsChecked, upsHooked;

        public bool UpsInstalled { get { return UpsPlugin != null; } }
        private object UpsPlugin { get { return upsPlugin ?? (upsPlugin = PluginObj(UpsId)); } }
        private object UpsSettings { get { return upsSettings ?? (upsSettings = PropOf(UpsPlugin, "Settings")); } }

        private bool UpsBool(string name) { return BoolOf(UpsSettings, name); }
        private int UpsInt(string name, int fallback) { return IntOf(UpsSettings, name, fallback); }
        private string UpsText(string name) { return Convert.ToString(PropOf(UpsSettings, name)) ?? ""; }

        private void UpsSet(string name, object value)
        {
            if (!SetPropOf(UpsSettings, name, value)) return;
            SaveOtherPluginSoon("UniPlaySong", () => UpsPlugin, () => UpsSettings);
        }

        /// <summary>Music › UniPlaySong is picked (and it's there).</summary>
        public bool MusicUps { get { return settings.MusicMode == 3 && UpsInstalled; } }

        /// <summary>Once, a little after start: the first time it's found it becomes the music; its now-playing is followed.</summary>
        private void UpsStartup()
        {
            upsChecked = true;
            if (!UpsInstalled) { if (settings.MusicMode == 3) NotifySound(); return; }
            HookUps();
            if (!settings.UpsMet && IsFullscreen)
            {
                settings.UpsMet = true;
                if (settings.MusicMode != 3) { settings.MusicMode = 3; RestartMusic(); }
                logger.Info("Kippix: UniPlaySong found: it's the music now (Settings › Audio › Music)");
                SaveSettings();
            }
            UpsQuietCheck();
            NotifySound();
        }

        private void HookUps()
        {
            if (upsHooked) return;
            var npc = UpsSettings as INotifyPropertyChanged;
            if (npc == null) return;
            upsHooked = true;
            npc.PropertyChanged += (s, e) =>
            {
                string n = e.PropertyName;
                if (n == "NowPlayingTitle" || n == "NowPlayingArtist" || n == "MusicVolume" || n == "EnableMusic" || n == "EnableDefaultMusic")
                    OnUi(() =>
                    {
                        NotifySound();
                        var r = optionsPage == "ups" ? UpsRows.FirstOrDefault(x => x.Key == "upsnext") : null;
                        if (r != null) { string np = UpsNowPlaying; r.SetSub(!string.IsNullOrEmpty(np) ? np : Loc("LOC_KX_UpsNothing", "Nothing playing right now")); }
                    });
                else if (n == "IsMusicChanged" && UpsBool("IsMusicChanged")) OnUi(UpsTrackChanged);
            };
        }

        private static int NextMusicMode(int m, bool ups)
        {
            m = (m + 1) % 4;
            if (m == 3 && !ups) m = 0;
            return m;
        }

        /// <summary>UniPlaySong's own links (play, pause, skip, ...).</summary>
        private void UpsCommand(string what)
        {
            try { Process.Start(new ProcessStartInfo("playnite://uniplaysong/" + what) { UseShellExecute = true }); }
            catch (Exception ex) { logger.Warn("Kippix: UniPlaySong " + what + ": " + ex.Message); }
        }

        private string UpsNowPlaying
        {
            get
            {
                string t = UpsText("NowPlayingTitle"), a = UpsText("NowPlayingArtist");
                if (string.IsNullOrEmpty(t)) return "";
                return string.IsNullOrEmpty(a) ? t : t + "  ·  " + a;
            }
        }

        /// <summary>Settings › Audio › UniPlaySong › Show the song when it changes.</summary>
        private void UpsTrackChanged()
        {
            if (!settings.UpsToast || !MusicUps || gameRunningNow || !IsFullscreen) return;
            string t = UpsText("NowPlayingTitle");
            if (string.IsNullOrEmpty(t)) return;
            System.Windows.Media.ImageSource art = null;
            try { string p = UpsText("NowPlayingAlbumArtPath"); if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p)) art = Images.LoadUncached(p, 160); } catch (Exception) { }
            try { ShowNotice(Loc("LOC_KX_NowPlaying", "NOW PLAYING"), t, UpsText("NowPlayingArtist"), "", art); } catch (Exception) { }
        }

        // ---------------- quiet while another music is picked, or Kippix plays sound of its own (UPS_MusicControl's Tag, Main.xaml) ----------------

        private bool? upsQuietSent;
        public bool UpsQuiet { get { return UpsQuietNow(); } }

        private bool UpsQuietNow()
        {
            try { return settings.MusicMode != 3 || (trailerPlaying && settings.DetTrailerSound) || PreviewHasSound || StoreSoundActive || GallerySoundActive || IntroSoundActive || LocalPlayerActive; }
            catch (Exception) { return settings.MusicMode != 3; }
        }

        private void UpsQuietCheck()
        {
            bool q = UpsQuietNow();
            if (upsQuietSent == q) return;
            upsQuietSent = q;
            Notify("UpsQuiet");
        }

        // ---------------- Settings › Audio › UniPlaySong ----------------

        public bool IsOptionsUps { get { return optionsPage == "ups"; } }
        private ObservableCollection<PickRow> upsRows;
        public ObservableCollection<PickRow> UpsRows { get { return upsRows ?? (upsRows = new ObservableCollection<PickRow>()); } }

        private static readonly int[] UpsPreviewLengths = { 15, 30, 45, 60 };

        private void UpsRowsAgain(string focus) { BoardRowsAgain(BuildUpsRows, focus); }

        private void BuildUpsRows()
        {
            var rows = UpsRows;
            rows.Clear();
            if (!UpsInstalled)
            {
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "upsnone", Text = Loc("LOC_KX_UpsNone", "UniPlaySong isn't installed"), Sub = Loc("LOC_KX_UpsNoneSub", "Add it from Playnite's add-ons (Generic › UniPlaySong) to give each game its own music") });
                return;
            }
            Action<string, string, string, string, bool, string, string, Action> sw = (icon, key, prop, text, on, onSub, offSub, after) =>
                rows.Add(BoardSwitch(icon, key, text, on, onSub, offSub ?? Loc("LOC_KX_Off", "Off"), () =>
                {
                    UpsSet(prop, !on);
                    if (after != null) after();
                    PlaySound("move");
                    UpsRowsAgain(key);
                }));

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_UpsSecPlaying", "PLAYING NOW") });
            string np = UpsNowPlaying;
            rows.Add(new PickRow
            {
                Key = "upsnext", Icon = "forward", Text = Loc("LOC_KX_NextSong", "Next song"),
                Sub = !string.IsNullOrEmpty(np) ? np : Loc("LOC_KX_UpsNothing", "Nothing playing right now"),
                Command = new RelayCommand(_ => { PlaySound("select"); UpsCommand("skip"); })
            });
            rows.Add(new PickRow { Key = "upspause", Icon = "pause", Text = Loc("LOC_KX_UpsPausePlay", "Pause / play"), Command = new RelayCommand(_ => { PlaySound("select"); UpsCommand("playpausetoggle"); }) });

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_UpsSecWhat", "WHAT PLAYS") });
            bool gameMusic = UpsBool("EnableMusic"), def = UpsBool("EnableDefaultMusic");
            sw("headphones", "upsgame", "EnableMusic", Loc("LOC_KX_UpsGameMusic", "Each game's own music"), gameMusic,
               Loc("LOC_KX_UpsGameMusicOn", "The highlighted game's songs (UniPlaySong downloads them)"), null, null);
            sw("media", "upsdefault", "EnableDefaultMusic", Loc("LOC_KX_UpsDefault", "Music for games without any"), def,
               Loc("LOC_KX_UpsDefaultOn", "Plays when a game has no songs of its own"), Loc("LOC_KX_UpsDefaultOff", "Off: quiet on those games"), null);
            if (def)
            {
                int src = UpsInt("DefaultMusicSourceOption", 2);
                rows.Add(new PickRow
                {
                    Key = "upssource", Icon = "shelf", Indent = 42, Text = Loc("LOC_KX_UpsSource", "That music comes from"), Sub = UpsSourceName(src),
                    Command = new RelayCommand(_ => { UpsSet("DefaultMusicSourceOption", UpsNextSource(src)); PlaySound("move"); UpsRowsAgain("upssource"); })
                });
            }
            sw("dice", "upsradio", "RadioModeEnabled", Loc("LOC_KX_UpsRadio", "Radio"), UpsBool("RadioModeEnabled"),
               Loc("LOC_KX_UpsRadioOn", "One long mix from all your games' music, whatever's highlighted"), null, null);

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_UpsSecWhen", "WHEN IT PLAYS") });
            bool onlyOpen = UpsBool("PlayOnlyOnGameSelect");
            rows.Add(BoardSwitch("grid", "upsbrowse", Loc("LOC_KX_UpsBrowse", "While moving through your games"), !onlyOpen,
                Loc("LOC_KX_UpsBrowseOn", "The highlighted game's music plays on the home screen"),
                Loc("LOC_KX_UpsBrowseOff", "Off: a game's music starts when its page opens"),
                () => { UpsSet("PlayOnlyOnGameSelect", !onlyOpen); PlaySound("move"); UpsRowsAgain("upsbrowse"); }));
            bool preview = UpsBool("EnablePreviewMode");
            sw("clock", "upspreview", "EnablePreviewMode", Loc("LOC_KX_UpsPreview", "Short previews"), preview,
               Loc("LOC_KX_UpsPreviewOn", "Each song plays for a while, then the next"), Loc("LOC_KX_UpsPreviewOff", "Off: songs play all the way through"), null);
            if (preview)
            {
                int len = UpsInt("PreviewDuration", 30);
                rows.Add(new PickRow
                {
                    Key = "upspreviewlen", Icon = "hourglass", Indent = 42, Text = Loc("LOC_KX_UpsPreviewLen", "Preview length"), Sub = string.Format(Loc("LOC_KX_SecondsN", "{0} seconds"), len),
                    Command = new RelayCommand(_ =>
                    {
                        int i = Array.IndexOf(UpsPreviewLengths, len);
                        UpsSet("PreviewDuration", UpsPreviewLengths[(i + 1) % UpsPreviewLengths.Length]);
                        PlaySound("move");
                        UpsRowsAgain("upspreviewlen");
                    })
                });
            }
            sw("refresh", "upsshuffle", "RandomizeOnEverySelect", Loc("LOC_KX_UpsShuffle", "A different song each time"), UpsBool("RandomizeOnEverySelect"),
               Loc("LOC_KX_UpsShuffleOn", "Coming back to a game picks another of its songs"), Loc("LOC_KX_UpsShuffleOff", "Off: it starts with the same song"), null);
            sw("clapper", "upstrailer", "PauseOnTrailer", Loc("LOC_KX_UpsTrailer", "Quiet for trailers"), UpsBool("PauseOnTrailer"),
               Loc("LOC_KX_UpsTrailerOn", "Fades out while a trailer with sound plays"), null, null);
            sw("play", "upsgamestart", "PauseOnGameStart", Loc("LOC_KX_UpsGameStart", "Quiet while you play"), UpsBool("PauseOnGameStart"),
               Loc("LOC_KX_UpsGameStartOn", "Stops when a game starts, back when it closes"), null, null);
            sw("moon", "upscalm", "CalmDownModeEnabled", Loc("LOC_KX_UpsCalm", "Calm down"), UpsBool("CalmDownModeEnabled"),
               Loc("LOC_KX_UpsCalmOn", "Softer and muffled, for late at night"), null, null);

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_UpsSecKippix", "WITH KIPPIX") });
            rows.Add(BoardSwitch("bell", "upstoast", Loc("LOC_KX_UpsToast", "Show the song when it changes"), settings.UpsToast,
                Loc("LOC_KX_UpsToastOn", "A small notice with its name and picture"), Loc("LOC_KX_Off", "Off"),
                () => { settings.UpsToast = !settings.UpsToast; SaveSettings(); PlaySound("move"); UpsRowsAgain("upstoast"); }));
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "upsmore", Text = Loc("LOC_KX_UpsMore", "Downloading songs, effects and the rest are in UniPlaySong's own settings"), Sub = Loc("LOC_KX_UpsMoreSub", "Playnite's desktop mode › Add-ons › Extension settings › UniPlaySong") });
        }

        private string UpsSourceName(int s)
        {
            switch (s)
            {
                case 0: return Loc("LOC_KX_UpsSrcFile", "A song you chose (in its settings)");
                case 2: return Loc("LOC_KX_UpsSrcBundled", "Its own calm tracks");
                case 3: return Loc("LOC_KX_UpsSrcFolder", "A folder you chose (in its settings)");
                case 4: return Loc("LOC_KX_UpsSrcRandom", "Another game's music, at random");
                case 5: return Loc("LOC_KX_UpsSrcRotation", "Games you picked (in its settings)");
                case 6: return Loc("LOC_KX_UpsSrcStatus", "Games by completion (in its settings)");
                case 7: return Loc("LOC_KX_UpsSrcTheme", "The theme's music");
                case 8: return Loc("LOC_KX_UpsSrcTrailer", "Nothing: the trailer's own sound");
            }
            return Loc("LOC_KX_UpsSrcOther", "As set in its settings");
        }

        /// <summary>The sources that need nothing set up first (and the one in use, whatever it is).</summary>
        private int UpsNextSource(int now)
        {
            var list = new List<int> { 2, 4 };
            if (!string.IsNullOrEmpty(UpsText("DefaultMusicFolderPath")) || now == 3) list.Add(3);
            if (!string.IsNullOrEmpty(UpsText("DefaultMusicPath")) || now == 0) list.Add(0);
            int i = list.IndexOf(now);
            return list[(i + 1) % list.Count];
        }

        // =====================================================================
        // BackgroundChanger
        // =====================================================================

        private object bcPlugin, bcSettings;
        private bool bcHooked;

        public bool BcInstalled { get { return BcPlugin != null; } }
        private object BcPlugin { get { return bcPlugin ?? (bcPlugin = PluginObj(BcId)); } }
        private object BcSettings { get { return bcSettings ?? (bcSettings = PropOf(PropOf(BcPlugin, "PluginSettingsViewModel"), "Settings")); } }

        private bool BcBool(string name) { return BoolOf(BcSettings, name); }

        private void BcSet(string name, object value)
        {
            if (!SetPropOf(BcSettings, name, value)) return;
            SaveOtherPluginSoon("BackgroundChanger", () => BcPlugin, () => BcSettings);
        }

        /// <summary>(0.99) BackgroundChanger is the background: Kippix's own background choice is put to the game's art (and comes back
        /// as it was when BackgroundChanger's backgrounds are switched off or it's taken out); the choice's rows are hidden meanwhile.</summary>
        public bool BcTakesBg { get { return settings.UseBcBackgrounds && BcInstalled && BcBool("EnableBackgroundImage"); } }
        public bool BcNotTakingBg { get { return !BcTakesBg; } }
        public string BcRowSub
        {
            get
            {
                return BcTakesBg ? Loc("LOC_KX_BcRowOn", "On: the background is its pictures and videos for each game")
                                 : Loc("LOC_KX_BcRowSub", "More backgrounds and covers per game, videos too");
            }
        }

        private void BcEnforceBg()
        {
            if (BcTakesBg)
            {
                if (settings.HomeBgMode != 0)
                {
                    if (settings.BcBgModeWas < 0) settings.BcBgModeWas = settings.HomeBgMode;
                    SetHomeBgMode(0);
                }
            }
            else if (settings.BcBgModeWas >= 0)
            {
                int was = settings.BcBgModeWas;
                settings.BcBgModeWas = -1;
                if (settings.HomeBgMode == 0 && was != 0) SetHomeBgMode(was); else SaveSettings();
            }
        }

        /// <summary>Its background on the home screen: switched on, it has one for this game, and the background is the game's art.</summary>
        public bool BcBgOnHome { get { return settings.UseBcBackgrounds && BcInstalled && BcBool("EnableBackgroundImage") && BcBool("HasDataBackground") && HomeArtShown; } }
        public bool BcBgOnPage { get { return settings.UseBcBackgrounds && BcInstalled && BcBool("EnableBackgroundImage") && BcBool("HasDataBackground"); } }
        public bool BcCoverOnPage { get { return settings.UseBcCovers && BcInstalled && BcBool("EnableCoverImage") && BcBool("HasDataCover"); } }

        private void BcStartup()
        {
            if (!BcInstalled || bcHooked) return;
            var npc = BcSettings as INotifyPropertyChanged;
            if (npc == null) return;
            bcHooked = true;
            npc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "HasDataBackground" || e.PropertyName == "HasDataCover" || e.PropertyName == "EnableBackgroundImage" || e.PropertyName == "EnableCoverImage")
                    OnUi(NotifyBc);
            };
            NotifyBc();
        }

        private void NotifyBc()
        {
            Notify("BcTakesBg");
            Notify("BcNotTakingBg");
            Notify("BcRowSub");
            Notify("BcBgOnHome");
            Notify("BcBgOnPage");
            Notify("BcCoverOnPage");
            Notify("BcInstalled");
        }

        // ---------------- Settings › Home settings › Background › BackgroundChanger ----------------

        public bool IsOptionsBc { get { return optionsPage == "bgc"; } }
        private ObservableCollection<PickRow> bcRows;
        public ObservableCollection<PickRow> BcRows { get { return bcRows ?? (bcRows = new ObservableCollection<PickRow>()); } }

        private static readonly int[] BcTimers = { 5, 10, 15, 30, 60 };

        private void BcRowsAgain(string focus) { BoardRowsAgain(BuildBcRows, focus); }

        private void BuildBcRows()
        {
            var rows = BcRows;
            rows.Clear();
            if (!BcInstalled)
            {
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "bcnone", Text = Loc("LOC_KX_BcNone", "BackgroundChanger isn't installed"), Sub = Loc("LOC_KX_BcNoneSub", "Add it from Playnite's add-ons (Generic › BackgroundChanger) for more backgrounds and covers per game, videos too") });
                return;
            }
            // backgrounds
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BcSecBg", "BACKGROUNDS") });
            bool bg = settings.UseBcBackgrounds;
            rows.Add(BoardSwitch("picture", "bcbg", Loc("LOC_KX_BcBg", "Its backgrounds"), bg,
                Loc("LOC_KX_BcBgOn", "Home screen and game page: the pictures and videos you gave each game in it"), Loc("LOC_KX_BcBgOff", "Off: each game's own background"),
                () =>
                {
                    settings.UseBcBackgrounds = !bg;
                    if (settings.UseBcBackgrounds && !BcBool("EnableBackgroundImage")) BcSet("EnableBackgroundImage", true);
                    SaveSettings(); BcEnforceBg(); PlaySound("move"); NotifyBc(); BcRowsAgain("bcbg");
                }));
            if (bg)
            {
                BcModeRows(rows, "Background", "bcbgmode", "bcbgtimer");
                bool rnd = BcBool("EnableRandomVideoStartPointBackground");
                rows.Add(new PickRow
                {
                    Key = "bcbgvideo", Icon = "video", Indent = 42, IsSwitch = true, IsSelected = rnd, Text = Loc("LOC_KX_BcVideoStart", "Videos start somewhere random"),
                    Sub = rnd ? Loc("LOC_KX_BcVideoStartOn", "Not always from the beginning") : Loc("LOC_KX_Off", "Off"),
                    Command = new RelayCommand(_ => { BcSet("EnableRandomVideoStartPointBackground", !rnd); PlaySound("move"); BcRowsAgain("bcbgvideo"); })
                });
            }
            // covers
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BcSecCover", "COVERS") });
            bool cv = settings.UseBcCovers;
            rows.Add(BoardSwitch("gallery", "bccover", Loc("LOC_KX_BcCover", "Its covers on the game page"), cv,
                Loc("LOC_KX_BcCoverOn", "The game page's cover is the one (or ones) you gave the game in it"), Loc("LOC_KX_BcCoverOff", "Off: the game's own cover"),
                () =>
                {
                    settings.UseBcCovers = !cv;
                    if (settings.UseBcCovers && !BcBool("EnableCoverImage")) BcSet("EnableCoverImage", true);
                    SaveSettings(); PlaySound("move"); NotifyBc(); BcRowsAgain("bccover");
                }));
            if (cv) BcModeRows(rows, "Cover", "bccovermode", "bccovertimer");
            ShuffleRows(rows);   // Shuffle covers on the home screen (ThemeApi.ShuffleCovers.cs)
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "bcmore", Text = Loc("LOC_KX_BcMore", "Adding pictures and videos to a game is in BackgroundChanger itself"), Sub = Loc("LOC_KX_BcMoreSub", "Playnite's desktop mode: right-click a game › BackgroundChanger") });
        }

        /// <summary>How its pictures change: one picked (favourite), at random when the game's picked / when Playnite starts, or in turn on a timer.</summary>
        private void BcModeRows(ObservableCollection<PickRow> rows, string kind, string key, string timerKey)
        {
            bool timer = BcBool("Enable" + kind + "ImageAutoChanger"), random = BcBool("Enable" + kind + "ImageRandomSelect"),
                 onStart = BcBool("Enable" + kind + "ImageRandomOnStart"), onSelect = BcBool("Enable" + kind + "ImageRandomOnSelect");
            int mode = timer ? (random ? 4 : 3) : random && onStart ? 2 : random ? 1 : 0;
            string[] names =
            {
                Loc("LOC_KX_BcModeFav", "Your favourite (or the first)"),
                Loc("LOC_KX_BcModeSelect", "A random one each time the game's highlighted"),
                Loc("LOC_KX_BcModeStart", "A random one each time Playnite starts"),
                Loc("LOC_KX_BcModeTimer", "Each in turn, changing on a timer"),
                Loc("LOC_KX_BcModeTimerRandom", "At random, changing on a timer")
            };
            rows.Add(new PickRow
            {
                Key = key, Icon = "refresh", Indent = 42, Text = Loc("LOC_KX_BcMode", "Which one shows"), Sub = names[mode],
                Command = new RelayCommand(_ =>
                {
                    int m = (mode + 1) % names.Length;
                    BcSet("Enable" + kind + "ImageAutoChanger", m >= 3);
                    BcSet("Enable" + kind + "ImageRandomSelect", m == 1 || m == 2 || m == 4);
                    BcSet("Enable" + kind + "ImageRandomOnStart", m == 2);
                    BcSet("Enable" + kind + "ImageRandomOnSelect", m == 1);
                    PlaySound("move");
                    BcRowsAgain(key);
                })
            });
            if (timer)
            {
                int secs = IntOf(BcSettings, kind + "ImageAutoChangerTimer", 10);
                rows.Add(new PickRow
                {
                    Key = timerKey, Icon = "clock", Indent = 84, Text = Loc("LOC_KX_BcTimer", "Changes every"), Sub = string.Format(Loc("LOC_KX_SecondsN", "{0} seconds"), secs),
                    Command = new RelayCommand(_ =>
                    {
                        int i = Array.IndexOf(BcTimers, secs);
                        BcSet(kind + "ImageAutoChangerTimer", BcTimers[(i + 1) % BcTimers.Length]);
                        PlaySound("move");
                        BcRowsAgain(timerKey);
                    })
                });
            }
        }

        // ---------------- pages ----------------

        private void MediaAddonOptionsOpened(string page)
        {
            Notify("IsOptionsUps");
            Notify("IsOptionsBc");
            Notify("MusicUps");
            Notify("BcInstalled");
            if (page == "ups") BuildUpsRows();
            if (page == "bgc") BuildBcRows();
        }

        /// <summary>From the tick, once each: the add-ons are loaded by now.</summary>
        private void MediaAddonsTick()
        {
            if (!upsChecked) { try { UpsStartup(); } catch (Exception ex) { upsChecked = true; logger.Warn("Kippix: UniPlaySong: " + ex.Message); } }
            if (!bcHooked && BcInstalled) { try { BcStartup(); } catch (Exception ex) { bcHooked = true; logger.Warn("Kippix: BackgroundChanger: " + ex.Message); } }
            if (!settings.BcOffOnce)
            {
                settings.BcOffOnce = true;
                settings.UseBcBackgrounds = false;
                settings.UseBcCovers = false;
                SaveSettings();
                NotifyBc();
            }
            if (ticks > 2) { try { BcEnforceBg(); } catch (Exception ex) { logger.Warn("Kippix: BackgroundChanger background: " + ex.Message); } }
            UpsQuietCheck();
            try { ShuffleTick(); } catch (Exception ex) { logger.Warn("Kippix: shuffle covers: " + ex.Message); }
        }
    }
}
