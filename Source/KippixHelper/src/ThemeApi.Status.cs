using System;
using System.Windows;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>Clock, downloads, controller battery and sound output.</summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // Clock
        // =====================================================================

        private string clockText = "";
        public string ClockText { get { return clockText; } }

        private void RefreshClock(bool force)
        {
            string t = settings.Clock24h
                ? DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)
                : DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture);
            if (force || t != clockText)
            {
                clockText = t;
                Notify("ClockText");
                ClockWidgetsTick();
            }
        }

        // =====================================================================
        // Downloads
        // =====================================================================

        private const double DownloadBarMax = 540;
        private volatile bool downloadsBusy;

        public ObservableCollection<DownloadItem> Downloads { get; private set; }
        public ICommand OpenSteamDownloadsCommand { get; private set; }

        private int downloadCount;
        public int DownloadCount { get { return downloadCount; } }
        public bool HasDownloads { get { return downloadCount > 0; } }

        private string downloadsSummary = "";
        public string DownloadsSummary { get { return downloadsSummary; } }

        public void GameRunningChanged(bool running)
        {
            bool was = anyRunning;
            if (running) launchFrontStarted = true;
            anyRunning = running || Running.Any;   // (issue #11) one of several stopping: the others still run
            global::KippixHelper.Downloads.GameRunning = anyRunning;
            OnUi(() => { ReleaseFront(); UpdateMusicState(); if (running) StopTrailer(); FadeLaunchArt(running); if (!was || !anyRunning) ReminderGameRunning(running); if (running) StopAmbient(); ApplyBackgroundMotion(); PlaySleepGameRunning(running); if (!running) CapturesAfterSession(); });
        }

        // ---------------- the launch screen (Views/GameStatus.xaml): the game's art fades in, then hands over to the game ----------------

        private bool launchHooked;
        private Game launchGame;
        private DateTime launchGameAt;

        /// <summary>A game is about to start: its art goes on the launch screen.</summary>
        public void GameStarting(Game game)
        {
            launchPageBg = null;   // (nothing from the last launch shows meanwhile)
            launchPageBgWhat = "";
            launchGame = game;
            launchGameAt = DateTime.Now;
            try { HomeVideoGameStarting(); } catch (Exception) { }   // (issue #13) no video (or open file) behind a game that's starting
            try { AutoSavesBackup(game, true); } catch (Exception) { }
            SessionStarting(game);
            OnUi(() => RunningStarting(game));   // (issue #11) keep browsing: where the highlight was
            OnUi(() =>
            {
                // the page's picture only when it's this game's, and settled (a tile started straight from home, or a
                // game switched to from the overlay, still shows the previous game's)
                try
                {
                    var sel = SelectedGameNow();
                    bool direct = (DateTime.Now - launchedDirectlyAt).TotalSeconds < 3;
                    if (game != null && sel != null && sel.Id == game.Id && !direct) launchPageBg = CurrentPageBackground(out launchPageBgWhat);
                    else launchPageBgWhat = direct ? "skipped (started from a tile)" : sel == null ? "skipped (nothing selected)" : "skipped (another game selected: " + sel.Name + ")";
                }
                catch (Exception) { launchPageBg = null; } try { if (SplashScreenShows(game)) logger.Info("Kippix: launch: Splash Screen shows its own screen (Playnite isn't held in front over it)"); else HoldFrontWhileStarting(); } catch (Exception ex) { logger.Warn("Kippix: launch front: " + ex.Message); } try { PrepareLaunchScreen(); HookLaunchScreen(); FillLaunchArt(); } catch (Exception) { } });
        }

        /// <summary>When the "game is starting" screen appears: its art fades in (or, if the game is already running, fades out).</summary>
        private void HookLaunchScreen()
        {
            if (launchHooked || !IsFullscreen) return;
            var status = UiTools.FindByName(Application.Current.MainWindow, "PART_ElemGameStatus");
            if (status == null) return;
            launchHooked = true;
            status.IsVisibleChanged += (s, e) =>
            {
                if (!(bool)e.NewValue) return;
                // (1.0.1) straight away, before the first frame: the first launch after Playnite starts builds the screen only as it
                // shows, so the picture was filled in a frame late and its bound cover flashed up first
                try
                {
                    PrepareLaunchScreen();
                    var art0 = UiTools.FindByName(status, "KX_LaunchArt");
                    if (art0 != null && !gameRunningNow) { art0.BeginAnimation(UIElement.OpacityProperty, null); art0.Opacity = 0; }
                    FillLaunchArt();
                }
                catch (Exception) { }
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var art = UiTools.FindByName(status, "KX_LaunchArt");
                        if (art == null) return;
                        // Splash Screen shows its own picture: ours stays plain underneath it
                        if (SplashScreenTakesOver(launchGame)) { art.BeginAnimation(UIElement.OpacityProperty, null); art.Opacity = 0; return; }
                        FillLaunchArt();
                        if (launchGame != null && Running.IsRunning(launchGame.Id)) { FadeLaunchArt(true); return; }   // (issue #11: this game, not just any)
                        art.BeginAnimation(UIElement.OpacityProperty, null);
                        art.Opacity = 0;
                        art.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(450))));
                    }
                    catch (Exception) { }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }

        /// <summary>(1.0.1) Builds the launch screen's parts now if Playnite hasn't shown it yet this session (it's made the first time it shows).</summary>
        private void PrepareLaunchScreen()
        {
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var status = win != null ? UiTools.FindByName(win, "PART_ElemGameStatus") as System.Windows.Controls.Control : null;
            if (status != null && UiTools.FindByName(status, "KX_LaunchArt") == null) status.ApplyTemplate();
        }

        /// <summary>Puts the starting game's background art (and its cover, for a game with no background art) on the launch screen.</summary>
        private void FillLaunchArt()
        {
            var g = launchGame;
            if (g == null || (DateTime.Now - launchGameAt).TotalMinutes > 2) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var status = win != null ? UiTools.FindByName(win, "PART_ElemGameStatus") : null;
            var art = status != null ? UiTools.FindByName(status, "KX_LaunchBgArt") as System.Windows.Controls.Border : null;
            var coverImg = status != null ? UiTools.FindByName(status, "KX_LaunchCover") as System.Windows.Controls.Image : null;
            var boundBg = status != null ? UiTools.FindByName(status, "KX_LaunchBg") : null;

            // the picture: what the game page / home screen was showing, else the game's background file, else its wide art
            System.Windows.Media.ImageSource pic = launchPageBg;
            string from = pic != null ? "the page" : null;
            if (pic == null) { pic = LaunchFile(g.BackgroundImage); if (pic != null) from = "background file"; }
            if (pic == null && (string.IsNullOrEmpty(g.BackgroundImage) || !g.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
            {
                string wide = WideArtPath(g);
                if (wide != null) { pic = Images.LoadUncached(wide, 0); if (pic != null) from = "wide art"; }
            }
            if (art != null)
                art.Background = pic != null ? new System.Windows.Media.ImageBrush(pic) { Stretch = System.Windows.Media.Stretch.UniformToFill } : null;
            // with a background, nothing else shows under it (the cover only stands in when there's no background)
            if (coverImg != null) coverImg.Visibility = pic != null ? Visibility.Collapsed : Visibility.Visible;
            if (boundBg != null) boundBg.Visibility = pic != null ? Visibility.Collapsed : Visibility.Visible;
            bool cover = pic == null && SetLaunchImage(coverImg, g.CoverImage, 900);
            try { ApplySplashLayout(status, g, pic != null); } catch (Exception ex) { logger.Warn("Kippix: splash screen layout: " + ex.Message); }   // (1.0.1)
            string said = "launch art: " + g.Name + ": screen " + (status != null ? "found" : "not found") + ", art " + (art != null ? "found" : "not found")
                        + ", background " + (pic != null ? "from " + from + " " + Describe(pic) : "none (" + (g.BackgroundImage ?? "no file") + ")") + ", cover " + (cover ? "shown" : "hidden")
                        + ", from the page: " + (launchPageBg != null ? launchPageBgWhat : "nothing [" + launchPageBgWhat + "]")
                        + ", details open: " + detailsOpen + ", trailer background: " + (settings.HomeBgMode == 2);
            if (said != lastLaunchLog) { lastLaunchLog = said; logger.Info("Kippix: " + said); }
        }

        private System.Windows.Media.ImageSource LaunchFile(string file)
        {
            if (string.IsNullOrEmpty(file) || file.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            string path = null;
            try { path = api.Database.GetFullFilePath(file); } catch (Exception) { }
            return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path) ? Images.LoadUncached(path, 0) : null;
        }

        private string lastLaunchLog;
        private System.Windows.Media.ImageSource launchPageBg;
        private string launchPageBgWhat = "";
        internal DateTime launchedDirectlyAt = DateTime.MinValue;   // a tile started the game itself (no game page): the page's picture isn't its yet

        /// <summary>The background picture showing right now (game page if it's open, else the home screen), as shown.</summary>
        private System.Windows.Media.ImageSource CurrentPageBackground(out string what)
        {
            what = "nothing";
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return null;
            var details = detailsOpen ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
            var fade = details != null ? UiTools.FindByName(details, "PART_ImageBackground") : null;
            if (fade == null || !fade.IsVisible) fade = UiTools.FindByName(win, "PART_ImageBackground");
            if (fade == null) { what = "no background element"; return null; }
            var inner = UiTools.FindAll<System.Windows.Controls.Image>(fade);
            what = "element " + (details != null ? "game page" : "home") + ", visible " + fade.IsVisible + ", " + inner.Count + " pictures inside";
            // what it's drawing: its inner picture that's showing
            foreach (var im in inner)
                if (im.Source != null && im.IsVisible && im.Opacity > 0.5) { what = Describe(im.Source); return im.Source; }
            // else what it was given
            object v = null;
            try { var p = fade.GetType().GetProperty("Source"); if (p != null) v = p.GetValue(fade, null); } catch (Exception) { }
            var src = v as System.Windows.Media.ImageSource;
            if (src != null) { what = Describe(src); return src; }
            string path = v as string;
            if (path == null && v is Uri) path = ((Uri)v).LocalPath;
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                var img = Images.LoadUncached(path, 0);
                if (img != null) { what = path + " " + Describe(img); return img; }
            }
            what += ", its Source: " + (v == null ? "none" : v.GetType().Name);
            return null;
        }

        private static string Describe(System.Windows.Media.ImageSource src)
        {
            var bi = src as System.Windows.Media.Imaging.BitmapImage;
            var bs = src as System.Windows.Media.Imaging.BitmapSource;
            string size = bs != null ? bs.PixelWidth + "x" + bs.PixelHeight : "";
            string from = bi != null && bi.UriSource != null ? bi.UriSource.ToString() : src.GetType().Name;
            return from + " " + size;
        }

        private string PixelSize(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || file.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return file ?? "none";
                string path = api.Database.GetFullFilePath(file);
                using (var fs = System.IO.File.OpenRead(path))
                {
                    var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
                    var f = dec.Frames[0];
                    return f.PixelWidth + "x" + f.PixelHeight;
                }
            }
            catch (Exception) { return "?"; }
        }

        private bool SetLaunchImage(System.Windows.Controls.Image img, string file, int width)
        {
            if (img == null || string.IsNullOrEmpty(file)) return false;
            string path = null;
            try { if (!file.StartsWith("http", StringComparison.OrdinalIgnoreCase)) path = api.Database.GetFullFilePath(file); } catch (Exception) { }
            return SetLaunchFile(img, path, width);
        }

        private bool SetLaunchFile(System.Windows.Controls.Image img, string path, int width)
        {
            if (img == null || string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;   // keep what the screen's own bindings found
            try
            {
                var b = new System.Windows.Media.Imaging.BitmapImage();
                b.BeginInit();
                b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                if (width > 0) b.DecodePixelWidth = width;
                b.UriSource = new Uri(path);
                b.EndInit();
                b.Freeze();
                img.SetCurrentValue(System.Windows.Controls.Image.SourceProperty, b);   // (the screen's own binding stays for the next game)
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: launch art " + path + ": " + ex.Message); return false; }
        }

        /// <summary>The game is running: the art fades to black (the game's window comes up over it). Back to full when it stops.</summary>
        private void FadeLaunchArt(bool running)
        {
            try
            {
                HookLaunchScreen();
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var status = win != null ? UiTools.FindByName(win, "PART_ElemGameStatus") : null;
                var art = status != null ? UiTools.FindByName(status, "KX_LaunchArt") : null;
                if (art == null) return;
                if (!running) { art.BeginAnimation(UIElement.OpacityProperty, null); art.Opacity = 1; return; }
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(1400)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(800),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };
                art.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception) { }
        }

        public ICommand RefreshDownloadsCommand { get; private set; }
        public string DownloadsCheckedText { get; private set; }

        private void RefreshDownloadsNow()
        {
            PlaySound("select");
            global::KippixHelper.Downloads.ForceRefresh();
            DownloadsCheckedText = Loc("LOC_KX_Checking", "Checking…");
            Notify("DownloadsCheckedText");
            RefreshDownloadsAsync();
        }

        private List<DownloadInfo> appliedDownloads;

        private void RefreshDownloadsAsync(bool panelOpening = false)
        {
            if (downloadsBusy) return;
            downloadsBusy = true;
            // the list on screen (the panel, or the widget on the home screen) keeps it checked every few seconds even when it is empty
            bool watching = panelOpening || openPanel == "downloads" || (settings.WidgetsOn && Widgets != null && HomeShowing && Widgets.Any(x => x.Type == "downloads"));
            KippixShared.SafeWork.Queue(_ =>
            {
                List<DownloadInfo> list = null;
                try
                {
                    IEnumerable<Game> games = null;
                    try { games = api.Database.Games; } catch (Exception) { }   // (gone through only when the list is worked out again)
                    list = global::KippixHelper.Downloads.Get(games, watching);
                }
                catch (Exception ex) { logger.Error(ex, "Kippix: downloads check failed"); }
                finally { downloadsBusy = false; }
                if (list != null) OnUi(() =>
                {
                    // (the same list as last time: nothing on screen to change)
                    if (list != appliedDownloads) { ApplyDownloads(list); appliedDownloads = list; }
                    DownloadsCheckedText = string.Format(Loc("LOC_KX_DownloadsChecked", "Checked at {0} · it keeps checking while this is open"), DateTime.Now.ToString(settings.Clock24h ? "HH:mm:ss" : "h:mm:ss tt"));
                    Notify("DownloadsCheckedText");
                });
            });
        }

        private void ApplyDownloads(List<DownloadInfo> list)
        {
            for (int i = Downloads.Count - 1; i >= 0; i--)
                if (!list.Any(d => d.Key == Downloads[i].Key)) Downloads.RemoveAt(i);

            foreach (var d in list)
            {
                var item = Downloads.FirstOrDefault(x => x.Key == d.Key);
                if (item == null)
                {
                    item = new DownloadItem { Key = d.Key };
                    Downloads.Add(item);
                }
                item.Name = d.Name;
                item.Status = d.Text();
                if (item.Command == null) { var info = d; item.Command = new RelayCommand(_ => OpenDownloadLauncher(info)); }
                item.Source = d.Source;
                item.HasProgress = d.Progress >= 0;
                item.BarWidth = d.Progress >= 0 ? Math.Max(4, d.Progress * DownloadBarMax) : 0;
            }

            int active = list.Count(d => d.State != "Paused" && d.State != "Not finished" && d.State != "Queued");
            downloadCount = list.Count;
            if (list.Count == 0) downloadsSummary = Loc("LOC_BBH_NoDownloads", "Nothing downloading right now.");
            else if (active == 0) downloadsSummary = list.Count == 1 ? Loc("LOC_BBH_OnePaused", "1 download, not running right now.") : string.Format(Loc("LOC_BBH_NPaused", "{0} downloads, none running right now."), list.Count);
            else downloadsSummary = string.Format(Loc("LOC_BBH_NActive", "{0} of {1} running."), active, list.Count);

            Notify("DownloadCount");
            Notify("HasDownloads");
            if (Widgets != null) foreach (var w in Widgets.Where(x => x.Type == "downloads")) FillDownloads(w);
            Notify("DownloadsSummary");
            UpdateShortcutStatus();
        }

        // =====================================================================
        // Controller battery
        // =====================================================================

        private const double BatteryFillMax = 23.8;   // inside width of the battery outline in the theme

        private string batteryText = "";
        private double batteryFill;
        private Brush batteryBrush = WhiteBrush;
        private bool hasController;

        /// <summary>A percentage when the controller reports one (PlayStation pads via the overlay); empty otherwise.</summary>
        public string BatteryText { get { return batteryText; } }
        public bool HasBatteryText { get { return settings.ShowBatteryPercent && batteryText.Length > 0; } }
        public double BatteryFillWidth { get { return batteryFill; } }
        public Brush BatteryBrush { get { return batteryBrush; } }
        public bool ShowBattery { get { return settings.PinBattery && hasController; } }

        // Which battery the top bar shows (Top bar settings): the controller's, or this device's own (a handheld, a laptop).
        private bool batteryCharging, batteryIsDevice;
        private ICommand cycleBatterySourceCommand;

        /// <summary>Charging or plugged in: a small bolt beside the battery.</summary>
        public bool BatteryCharging { get { return batteryCharging; } }
        public string BatterySourceText
        {
            get
            {
                switch (settings.BatterySource)
                {
                    case 1: return Loc("LOC_KX_BatterySourceDevice", "This device");
                    case 2: return Loc("LOC_KX_BatterySourceController", "Controller");
                }
                return Loc("LOC_KX_BatterySourceAuto", "Automatic");
            }
        }

        /// <summary>
        /// (1.0.4) The controller is on a cable (a handheld's built-in controls count as one): a plug, not a battery. Windows only
        /// says "wired" for those, and it was drawn as a full battery (a handheld at 7% showed full).
        /// </summary>
        public bool BatteryWired { get { return batteryWired; } }
        public bool BatteryOutlineShown { get { return !batteryWired; } }
        private bool batteryWired;
        public ICommand CycleBatterySourceCommand
        {
            get
            {
                if (cycleBatterySourceCommand == null) cycleBatterySourceCommand = new RelayCommand(_ =>
                {
                    settings.BatterySource = (Math.Max(0, Math.Min(2, settings.BatterySource)) + 1) % 3;   // (1.0.4) Automatic, This device, Controller
                    SaveSettings();
                    PlaySound("move");
                    Notify("BatterySourceText");
                    RefreshBattery();
                });
                return cycleBatterySourceCommand;
            }
        }

        /// <summary>This device's own battery as "87%" ("87%⚡" plugged in). Null: it has none (a desktop PC).</summary>
        private static string ReadDeviceBattery()
        {
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Unknown) return null;
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                if (pct < 0 || pct > 100) return null;
                return pct + "%" + (ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "⚡" : "");
            }
            catch (Exception) { return null; }
        }

        private string sdlBattery;
        private DateTime sdlBatteryAt = DateTime.MinValue;

        /// <summary>
        /// The connected controller's battery: the overlay's reading (PlayStation pads with a real percentage, Xbox pads),
        /// then XInput, then a pad only Playnite reads (the Steam Controller outside games) through SDL. Null: no controller.
        /// </summary>
        private string ReadControllerBattery()
        {
            string raw = overlay.Available ? overlay.Get<string>("ControllerBattery", null) : null;
            if (raw == null) raw = XInputBattery.Read();
            if (raw != null) return raw;
            // Playnite's own pads: battery levels change slowly, so SDL is asked at most every 20 seconds
            object pad = null;
            try { pad = api.GetConnectedControllers().OfType<Playnite.SDK.Events.IGamepad>().FirstOrDefault(g => g.Enabled); } catch (Exception) { }
            if (pad == null) { sdlBattery = null; sdlBatteryAt = DateTime.MinValue; return null; }
            if (sdlBattery == null || DateTime.Now - sdlBatteryAt > TimeSpan.FromSeconds(20))
            {
                sdlBattery = SdlBattery.Read(pad) ?? "Connected";
                sdlBatteryAt = DateTime.Now;
            }
            return sdlBattery;
        }

        private void RefreshBattery()
        {
            // (1.0.4) Automatic: this device's own battery when it has one (a handheld, a laptop), else the controller's; This device
            // (a PC without one: the controller's, as before); Controller
            int src = Math.Max(0, Math.Min(2, settings.BatterySource));
            string raw = src != 2 ? ReadDeviceBattery() : null;
            bool device = raw != null;
            if (raw == null) raw = ReadControllerBattery();

            bool has = raw != null;
            string text = "";
            double fraction = 0;
            Brush brush = WhiteBrush;
            bool charging = false;
            bool wired = false;

            if (has)
            {
                string t = raw.Trim();
                charging = t.IndexOf('⚡') >= 0;
                t = t.Replace("⚡", "").Trim();
                int pct;
                if (t.EndsWith("%") && int.TryParse(t.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out pct))
                {
                    fraction = pct / 100.0;
                    text = pct + "%";
                    brush = charging ? GreenBrush : pct <= 10 ? RedBrush : pct <= 25 ? AmberBrush : WhiteBrush;
                }
                else
                {
                    switch (t)
                    {
                        // Xbox pads only report four levels, so the percentage is a rough guide.
                        case "Full": fraction = 1; text = "100%"; break;
                        case "Medium": fraction = 0.6; text = "~60%"; break;
                        case "Low": fraction = 0.3; text = "~30%"; brush = AmberBrush; break;
                        case "Empty": fraction = 0.1; text = "~10%"; brush = RedBrush; break;
                        case "Wired": fraction = 0; wired = true; break;   // (1.0.4) a plug, not a full battery
                        default: fraction = 1; brush = WhiteBrush; break;   // connected, level unknown
                    }
                }
            }

            double fill = Math.Round(fraction * BatteryFillMax, 1);
            if (has != hasController || text != batteryText || fill != batteryFill || brush != batteryBrush || charging != batteryCharging || device != batteryIsDevice || wired != batteryWired)
            {
                batteryWired = wired;
                Notify("BatteryWired");
                Notify("BatteryOutlineShown");
                hasController = has;
                batteryText = text;
                batteryFill = fill;
                batteryBrush = brush;
                batteryCharging = charging;
                batteryIsDevice = device;
                Notify("BatteryText");
                Notify("HasBatteryText");
                Notify("BatteryFillWidth");
                Notify("BatteryBrush");
                Notify("BatteryCharging");
                Notify("ShowBattery");
                UpdateShortcutStatus();
            }
        }

        // =====================================================================
        // Sound output / Sound input (one panel, "audio": the top bar shortcut always opens it for output;
        // Quick settings' Sound input row opens it for microphones)
        // =====================================================================

        public ObservableCollection<AudioItem> AudioDevices { get; private set; }

        private bool audioInput;         // the panel lists microphones
        private bool audioInputNext;     // set just before TogglePanel("audio") by Quick settings' Sound input row
        private bool audioFromQuick;     // opened from Quick settings: picking / Back goes back there
        private bool audioFromQuickNext;

        public string AudioPanelTitle { get { return audioInput ? Loc("LOC_KX_SoundInput", "Sound input") : Loc("LOC_KX_SoundOutput", "Sound output"); } }
        public string AudioPanelHint { get { return audioInput ? Loc("LOC_KX_MicHint", "The green dot shows the microphone Windows uses now.") : Loc("LOC_KX_VolumeHint", "The green dot shows where sound is playing now."); } }

        /// <summary>From Quick settings: the sound output or input list (picking one, or Back, comes back to Quick settings).</summary>
        private void OpenAudioFromQuick(bool input)
        {
            audioInputNext = input;
            audioFromQuickNext = true;
            TogglePanel("audio");
        }

        private void RefreshAudio()
        {
            audioInput = audioInputNext;
            audioFromQuick = audioFromQuickNext;
            audioInputNext = false;
            audioFromQuickNext = false;
            Notify("AudioPanelTitle");
            Notify("AudioPanelHint");
            AudioDevices.Clear();
            List<AudioDevice> list;
            bool input = audioInput;
            try { list = input ? Audio.InputDevices() : Audio.OutputDevices(); }
            catch (Exception ex) { logger.Error(ex, "Kippix: couldn't list audio devices"); return; }
            foreach (var d in list.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name))
            {
                string id = d.Id;
                AudioDevices.Add(new AudioItem
                {
                    Id = id,
                    Name = d.Name,
                    IsDefault = d.IsDefault,
                    IsInput = input,
                    SelectCommand = new RelayCommand(_ =>
                    {
                        Audio.SetDefault(id);
                        if (!AudioBack()) ClosePanels();
                    })
                });
            }
        }

        /// <summary>Back (or a pick) in the sound list opened from Quick settings: Quick settings again, on that row.</summary>
        private bool AudioBack()
        {
            if (openPanel != "audio" || !audioFromQuick) return false;
            bool input = audioInput;
            audioFromQuick = false;
            OpenQuickSettings(input ? "mic" : "audio");
            return true;
        }
    
        /// <summary>
        /// (0.99.55) Back from a game on its page: its captures row read again (the shots and clips just taken), once now and once
        /// a few seconds on (a recorder can finish writing a clip after the game has closed).
        /// </summary>
        private void CapturesAfterSession()
        {
            foreach (int ms in new[] { 1500, 6000 })
            {
                var t = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(ms) };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    try
                    {
                        if (!detailsOpen || (!settings.RunKeepBrowsing && AnyRunning)) return;
                        var g = SelectedGameNow();
                        if (g == null || (settings.RunKeepBrowsing && Running.IsRunning(g.Id))) return;   // (issue #11: this game started again, whatever else is open)
                        galleryScannedAt = DateTime.MinValue;
                        FillCaptures(g, detMoreGen);
                    }
                    catch (Exception) { }
                };
                t.Start();
            }
        }
    }
}
