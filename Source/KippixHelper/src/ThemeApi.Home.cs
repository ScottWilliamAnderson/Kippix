using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Home screen layout: sizes of the logo / game info / button hints, where the game cards sit,
    /// keeping the logo + info + hints just above the cards, and a quick first frame while Playnite loads.
    /// </summary>
    public partial class ThemeApi
    {
        // ---------------- sizes ----------------

        private static readonly double[] TextScales = { 0.8, 1.0, 1.2, 1.4 };
        private static readonly double[] LogoScales = { 0.7, 1.0, 1.3, 1.6 };

        public ICommand CycleHintsSizeCommand { get; private set; }
        public ICommand CycleLogoSizeCommand { get; private set; }
        public ICommand CycleInfoSizeCommand { get; private set; }

        private string SizeText(int size)
        {
            switch (size)
            {
                case 0: return Loc("LOC_KX_SizeSmall", "Small");
                case 2: return Loc("LOC_KX_SizeLarge", "Large");
                case 3: return Loc("LOC_KX_SizeXL", "Extra large");
            }
            return Loc("LOC_KX_SizeMedium", "Medium");
        }

        public string HintsSizeText { get { return SizeText(settings.HintsSize); } }
        public string LogoSizeText { get { return SizeText(settings.LogoSize); } }
        public string InfoSizeText { get { return SizeText(settings.InfoSize); } }

        // ---------------- card placement ----------------

        private const int CardOffsetMin = -120, CardOffsetMax = 240, CardOffsetStep = 10;
        private const double CardTrackWidth = 480;   // width of the bar in the theme

        public ICommand CardOffsetResetCommand { get; private set; }

        /// <summary>The cover row's margin: Playnite's own spot is 55 px below the bottom edge.</summary>
        public Thickness CardsMargin { get { return CardsMarginFor(cardsLeft); } }

        public string CardOffsetText
        {
            get
            {
                int o = settings.CardOffset;
                if (o == 0) return Loc("LOC_KX_CardsDefault", "Default");
                return o > 0 ? string.Format(Loc("LOC_KX_CardsUp", "{0} px higher"), o) : string.Format(Loc("LOC_KX_CardsDown", "{0} px lower"), -o);
            }
        }

        private double CardFraction { get { return (double)(settings.CardOffset - CardOffsetMin) / (CardOffsetMax - CardOffsetMin); } }
        public double CardFillWidth { get { return CardFraction * CardTrackWidth; } }
        public Thickness CardKnobMargin { get { return new Thickness(CardFraction * CardTrackWidth - 12, 0, 0, 0); } }

        private void NudgeCards(int delta)
        {
            int v = Math.Max(CardOffsetMin, Math.Min(CardOffsetMax, settings.CardOffset + delta));
            if (v == settings.CardOffset) return;
            settings.CardOffset = v;
            SaveSettings();
            NotifyCards();
        }

        private void NotifyCards()
        {
            foreach (string n in new[] { "CardsMargin", "CardOffsetText", "CardKnobMargin", "CardFillWidth" }) Notify(n);
            ApplyHomeLayoutSoon();
        }

        // ---------------- logo + info + hints sit just above the cards ----------------

        private const double InfoGap = 46;   // space between the hints and the top of the covers (room for the selected cover to grow)
        private Thickness gameInfoMargin = new Thickness(72, 0, 72, 440);
        public Thickness GameInfoMargin { get { return gameInfoMargin; } }

        private void InitHomeLayoutCommands()
        {
            CycleHintsSizeCommand = new RelayCommand(_ => { settings.HintsSize = (settings.HintsSize + 1) % 4; SaveSettings(); NotifyHome(); ApplyHomeLayoutSoon(); });
            CycleLogoSizeCommand = new RelayCommand(_ => { settings.LogoSize = (settings.LogoSize + 1) % 4; SaveSettings(); NotifyHome(); ApplyHomeLayoutSoon(); });
            CycleInfoSizeCommand = new RelayCommand(_ => { settings.InfoSize = (settings.InfoSize + 1) % 4; SaveSettings(); NotifyHome(); ApplyHomeLayoutSoon(); });
            CardOffsetResetCommand = new RelayCommand(_ => { settings.CardOffset = 0; SaveSettings(); NotifyCards(); });
        }

        private void ApplyHomeLayoutSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(ApplyHomeLayout), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Scales the logo / info line / hints (sizes are done here in code: a binding on a ScaleTransform
        /// would crash Playnite's theme loader) and moves the whole block so it sits just above the covers.
        /// </summary>
        private void ApplyHomeLayout()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                BoardApplyMode();
                if (IsFullscreen && DateTime.Now >= carRetryAt && (CarouselOn != carActive || (carActive && carDesign != CarDesign))) CarouselApplyMode();   // Row's carousel (at start-up, or after a change)
                else if (IsFullscreen) CarApplyShown();   // (the host shown exactly while the carousel is on)
                SetScale(UiTools.FindByName(win, "KX_LogoArea"), LogoScales[settings.LogoSize]);
                SetScale(UiTools.FindByName(win, "KX_InfoLine"), TextScales[settings.InfoSize]);
                SetScale(UiTools.FindByName(win, "KX_HintsRow"), TextScales[settings.HintsSize]);

                var info = UiTools.FindByName(win, "KX_GameInfo");
                var list = UiTools.FindByName(win, "PART_ListGameItems") as ItemsControl;
                var root = info != null ? VisualTreeHelper.GetParent(info) as FrameworkElement : null;
                if (info == null || list == null || root == null || root.ActualHeight <= 0) return;
                // Row › Tall / Full Screen: the carousel between the top bar and the logo / info / hints, which sit at the bottom
                if (CarouselScreenLayout(root)) return;

                double coverBottom;
                double top = CoverTop(list, root, out coverBottom);
                if (double.IsNaN(top)) return;
                double moved = MasterOffsetY("home.cards");   // master edit moves the cards on their own
                if (mainRowShift != null) moved += mainRowShift.Y;   // the row sliding away for the extra rows isn't a new place for the info
                top -= moved;
                coverBottom -= moved;
                // the highlighted cover grows by 6% from its bottom edge (plus its glow): the filter name sits above that, not under it
                double lift = Math.Round(Math.Max(0, coverBottom - top) * 0.06) + 16;
                // room for the filter name above the covers: (0.89) kept when it's switched off or deleted, so nothing round it moves
                double gap = InfoGap + (!IsBoardMode ? 34 + Math.Max(0, lift - 10) : 0);
                double bottom = Math.Max(0, Math.Round(root.ActualHeight - top + gap));
                if (ShowExtraRows && extraRowAt == 0 && Math.Abs(top - lastRowsTop) >= 1) { lastRowsTop = top; LayoutExtraRows(false); }
                Thickness want;
                if (LayoutNow == 2)
                {
                    // the list down one side: the logo and info sit in the middle of the other side, wherever the list's top is
                    // a fixed spot: the far half of the screen, centred top to bottom on the usual block's height (not on this
                    // game's, and not on the list's size: changing how many games show moves nothing)
                    double mid = Math.Max(0, Math.Round((root.ActualHeight - 330) / 2));
                    double half = Math.Round(root.ActualWidth * 0.5);
                    try
                    {
                        // a wide list (big covers, long names) keeps the info clear of it
                        // (the list can be wider than its cards, which sit in its middle: measure the cards, grown as when highlighted)
                        var at = list.TranslatePoint(new Point(0, 0), root);
                        double cardW = sideItemW > 0 ? Math.Min(list.ActualWidth, sideItemW * 1.08) : list.ActualWidth;
                        double inset = Math.Max(0, (list.ActualWidth - cardW) / 2);
                        double listFar = settings.ListSide == 0 ? at.X + inset + cardW : root.ActualWidth - (at.X + inset);
                        if (list.ActualWidth > 0 && listFar + 48 > half) half = Math.Min(Math.Round(listFar + 48), Math.Round(root.ActualWidth * 0.7));
                    }
                    catch (Exception) { }
                    want = settings.ListSide == 0 ? new Thickness(half, 0, 96, mid) : new Thickness(96, 0, half, mid);
                }
                else want = new Thickness(72, 0, 72, bottom);
                if (want != gameInfoMargin)
                {
                    gameInfoMargin = want;
                    Notify("GameInfoMargin");
                }
                var above = new Thickness(72, 0, 72, Math.Max(0, Math.Round(root.ActualHeight - top + lift)));
                var below = new Thickness(72, 0, 72, Math.Max(4, Math.Round(root.ActualHeight - coverBottom - 36)));
                if (LayoutNow == 2)
                {
                    // the list: the filter name sits just above the logo and info, in the same fixed spot
                    above = below = new Thickness(want.Left, 0, want.Right, want.Bottom + 330 + 16);
                }
                if (above != filterAboveMargin) { filterAboveMargin = above; Notify("FilterAboveMargin"); }
                if (below != filterBelowMargin) { filterBelowMargin = below; Notify("FilterBelowMargin"); }
                // Row › Lower with Loop: the carousel sits exactly where Playnite's row is (and the tiles are in it)
                if (CarouselOn) { CarouselUpdate(); return; }
                // tiles before the games: Always show puts them up once the cards are there; otherwise follow the card size
                if (HomeTiles != null && HomeTiles.Count > 0)
                {
                    if ((settings.HomeTilesMode == 2 || SideMode) && !homeTilesShown && list.Items.Count > 0) ApplyHomeTiles();
                    else UpdateHomeTilesLayout();
                }
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: home layout failed"); }
        }

        private static void SetScale(FrameworkElement el, double scale)
        {
            if (el == null) return;
            var t = el.LayoutTransform as ScaleTransform;
            if (t != null && Math.Abs(t.ScaleX - scale) < 0.001) return;
            el.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        }

        /// <summary>Top edge of the covers (in the main grid's 1080p space), from a cover that isn't selected (so it isn't scaled up).</summary>
        private static double CoverTop(ItemsControl list, FrameworkElement root, out double coverBottom)
        {
            double best = double.NaN;
            coverBottom = double.NaN;
            var gen = list.ItemContainerGenerator;
            int n = Math.Min(list.Items.Count, 12);
            for (int i = 0; i < n; i++)
            {
                var c = gen.ContainerFromIndex(i) as FrameworkElement;
                if (c == null || !c.IsVisible || c.ActualHeight <= 0) continue;
                var lbi = c as ListBoxItem;
                if (lbi != null && lbi.IsSelected) continue;
                double y = c.TranslatePoint(new Point(0, 0), root).Y;
                // the tile's own spacing margin sits inside the container
                if (VisualTreeHelper.GetChildrenCount(c) > 0)
                {
                    var child = VisualTreeHelper.GetChild(c, 0) as FrameworkElement;
                    if (child != null)
                    {
                        y += child.Margin.Top;
                        double b = y + child.ActualHeight;
                        if (double.IsNaN(coverBottom) || b > coverBottom) coverBottom = b;
                    }
                }
                if (double.IsNaN(best) || y < best) best = y;
            }
            if (double.IsNaN(best) && list.Items.Count == 0)
            {
                // nothing to measure yet: keep the block where the cards will be
                var lt = list.TranslatePoint(new Point(0, 0), root).Y;
                best = lt + list.ActualHeight * 0.12;
            }
            if (double.IsNaN(coverBottom) && !double.IsNaN(best)) coverBottom = best + 420;
            return best;
        }

        // ---------------- current filter name ----------------

        public bool ShowFilterLabel
        {
            get { return settings.ShowFilterLabel; }
            set { if (SetSetting(ref settings.ShowFilterLabel, value)) { ApplyHomeLayoutSoon(); if (value) HomePartBackOnTop("home.filter"); } }
        }
        // the filter name sits above the games; it's moved with Free move like everything else
        public bool FilterAbove { get { return settings.ShowFilterLabel && !IsBoardMode; } }
        public HorizontalAlignment FilterAlignment
        {
            get
            {
                if (LayoutNow == 2) return settings.ListSide == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;   // with the logo and info
                return HorizontalAlignment.Left;
            }
        }
        private Thickness filterAboveMargin = new Thickness(72, 0, 72, 440);
        private Thickness filterBelowMargin = new Thickness(72, 0, 72, 8);
        public Thickness FilterAboveMargin { get { return filterAboveMargin; } }
        public Thickness FilterBelowMargin { get { return filterBelowMargin; } }

        // ---------------- quick first frame ----------------

        private bool libraryLoading = true;
        private DispatcherTimer libraryTimer;
        private DateTime libraryWatchStart;

        /// <summary>True until Playnite has filled the library, so the theme doesn't flash "no games found" at startup.</summary>
        public bool LibraryLoading { get { return libraryLoading; } }

        private void StartLibraryWatch()
        {
            libraryWatchStart = DateTime.Now;
            libraryTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(200) };
            libraryTimer.Tick += (s, e) =>
            {
                bool done = false;
                HoldStartBlack();   // the black start-up screen stays until the home screen is ready (not its own 6 s fallback)
                try
                {
                    var win = Application.Current != null ? Application.Current.MainWindow : null;
                    var list = win != null ? UiTools.FindByName(win, "PART_ListGameItems") as ItemsControl : null;
                    if (list != null && list.Items.Count > 0) done = true;
                    else if (api.Database.IsOpen && (DateTime.Now - libraryWatchStart).TotalSeconds > 2.5) done = true;   // really empty (or filtered)
                    else if ((DateTime.Now - libraryWatchStart).TotalSeconds > 15) done = true;
                }
                catch (Exception) { done = true; }
                if (!done) return;
                libraryTimer.Stop();
                libraryLoading = false;
                Notify("LibraryLoading");
                ApplyCardLayout();
                ApplyHomeLayoutSoon();
                UpdateTopBarFade();
                PreloadAchievementsLater();
                if (IsFullscreen)
                {
                    RebuildExtraRowsSoon();
                    RefreshBadgesSoon();
                    RefreshTileArtSoon();
                    ApplyBackgroundMotion();
                    ApplyPrompts(true);
                }
                // your layout has been put in place: show the screen (after one more layout pass, so nothing jumps)
                var reveal = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(350) };
                reveal.Tick += (s2, e2) => { reveal.Stop(); ShowStage(); };
                reveal.Start();
            };
            libraryTimer.Start();

        }

        private double stageOpacity = 0;
        /// <summary>The home screen's own layer: 0 while Playnite starts, until your layout is applied (then it fades in).</summary>
        public double StageOpacity { get { return IsFullscreen ? stageOpacity : 1; } }

        private void ShowStage()
        {
            if (stageOpacity >= 1) return;
            // first wait (at most 2.5 s) for the background to settle on the first real selection, so the game that was
            // selected in desktop mode (or before the layout picked one) never shows: Playnite's background waits 300 ms
            // after a selection, loads the picture, then fades it in over 500 ms
            if (!stageWaitDone && ui != null && IsFullscreen)
            {
                if (stageWait != null) return;
                stageWaitSince = DateTime.Now;
                stageWait = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(100) };
                stageWait.Tick += (s, e) =>
                {
                    if ((DateTime.Now - lastSelectAt).TotalMilliseconds < 1000 && (DateTime.Now - stageWaitSince).TotalSeconds < 2.5) return;
                    stageWait.Stop();
                    stageWaitDone = true;
                    try { ShowStage(); } catch (Exception) { LiftStartBlack(); }
                };
                stageWait.Start();
                return;
            }
            stageOpacity = 1;
            // the welcome (first start) or Who's playing?, once the home screen is there
            // (under the intro they wait for it to end: Look and feel › Intro and outro)
            if (ui != null) ui.BeginInvoke(new Action(() =>
            {
                if (introOn && !introIsOutro) { startupScreensWaiting = true; return; }
                try { StartupScreens(); } catch (Exception ex) { logger.Error(ex, "Kippix: start-up screens"); }
            }), DispatcherPriority.ApplicationIdle);
            try
            {
                var stage = UiTools.FindByName(Application.Current.MainWindow, "KX_Stage");
                Notify("StageOpacity");
                if (stage != null && !settings.ReduceMotion)
                {
                    var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
                    stage.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }
            catch (Exception) { Notify("StageOpacity"); }
            LiftStartBlack();
        }

        private DispatcherTimer stageWait;
        private DateTime stageWaitSince;
        private bool stageWaitDone;
        private DateTime lastSelectAt = DateTime.MinValue;   // the last time Playnite's selected game changed
        private bool startBlackHeld, startBlackLifted;

        /// <summary>
        /// KX_StartBlack (Main.xaml) is a plain black layer over the background from the first frame, so nothing (desktop
        /// mode's selected game, last session's art) shows before the home screen is ready. Its own storyboard fades it
        /// after 6 s in case the helper never gets this far; once the helper is running, that is cancelled here and the
        /// helper lifts it when the home screen is ready (ShowStage), with its own backstop.
        /// </summary>
        private void HoldStartBlack()
        {
            if (startBlackHeld || startBlackLifted || !IsFullscreen) return;
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var black = win != null ? UiTools.FindByName(win, "KX_StartBlack") as FrameworkElement : null;
                if (black == null || !black.IsLoaded) return;   // (its fallback fade starts on Loaded: cancelled after that)
                startBlackHeld = true;
                black.BeginAnimation(UIElement.OpacityProperty, null);   // (removes the 6 s fallback fade; the layer stays black)
                black.Opacity = 1;
                StartIntro();   // Look and feel › Intro and outro: plays over the black while Playnite loads (ThemeApi.Intro.cs)
                StartNightMode();   // Look and feel › Night mode: its minute check, while fullscreen runs (ThemeApi.NightMode.cs)
                var backstop = new DispatcherTimer(DispatcherPriority.Background, black.Dispatcher) { Interval = TimeSpan.FromSeconds(6) };
                backstop.Tick += (s, e) =>
                {
                    backstop.Stop();
                    if (startBlackLifted) return;
                    logger.Info("Kippix: start-up black screen lifted by its backstop (the home screen took too long)");
                    LiftStartBlack();
                    try { stageWaitDone = true; ShowStage(); } catch (Exception) { }
                };
                backstop.Start();
            }
            catch (Exception) { }
        }

        /// <summary>The home screen is ready: the black start-up layer fades away (at once with Reduce motion).</summary>
        private void LiftStartBlack()
        {
            if (startBlackLifted) return;
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var black = win != null ? UiTools.FindByName(win, "KX_StartBlack") as UIElement : null;
                if (black == null) return;   // (not there yet: its own 6 s fade takes it away)
                startBlackLifted = true;
                if (settings.ReduceMotion)
                {
                    black.BeginAnimation(UIElement.OpacityProperty, null);
                    black.Opacity = 0;
                    black.Visibility = Visibility.Collapsed;
                    return;
                }
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
                fade.Completed += (s, e) => { try { black.Visibility = Visibility.Collapsed; } catch (Exception) { } };
                black.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception) { }
        }

        private ImageSource startupBackground;
        private bool backgroundDirty;

        /// <summary>The last selected game's background, shown until Playnite picks a game.</summary>
        public ImageSource StartupBackground { get { return startupBackground; } }

        private void LoadStartupBackground()
        {
            string path = settings.LastBackground;
            if (string.IsNullOrEmpty(path) || settings.HomeBgMode == 1 || HomeCustomShown || HomeBgBlack) return;   // a plain colour, your own picture or Folder's tiles: no game art at start either
            startupBackground = Images.Load(path, 1920);
            Notify("StartupBackground");
        }

        /// <summary>Remembers the selected game's background for next time (saved every couple of seconds, not on every move).</summary>
        internal void GameSelected(Game game)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { GameSelectedNow(game); }
            finally { if (sw.ElapsedMilliseconds >= 40) PerfLog("game selected " + sw.ElapsedMilliseconds + " ms, " + PerfWhere); }
        }

        private void GameSelectedNow(Game game)
        {
            lastSelectAt = DateTime.Now;   // (the start-up black screen waits for the background to settle)
            PreviewSelectionChanged();
            try { LiveSelectionChanged(); } catch (Exception) { }   // live backgrounds / animated art (ThemeApi.LiveBackground.cs)
            try { TileBgSelected(game); } catch (Exception) { }   // (0.88) Background › The game's tile
            try { CopySelected(game); } catch (Exception) { }   // play time of all its copies (ThemeApi.Copies.cs)
            try { HomeExtrasSelected(game, false); } catch (Exception) { }   // (r13) the info line's players online / friends (ThemeApi.HomeInfoExtras.cs)
            try { LookGameSelected(game); FolderAccentFollow(); if (detailsOpen) UpdateGamePageMore(); } catch (Exception) { }
            try { if (detailsOpen && game != null) GamePageUpdateCheck(game); } catch (Exception) { }
            try { if (detailsOpen) DetProgressRefresh(); } catch (Exception) { }   // (0.99.48)   // (0.99.39) another game on the page
            try
            {
                if (detailsOpen) { UpdateTrailer(); UpdateDetFriends(); }
                if (game == null || string.IsNullOrEmpty(game.BackgroundImage)) return;
                string path = game.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? null : api.Database.GetFullFilePath(game.BackgroundImage);
                if (string.IsNullOrEmpty(path)) return;
                PreloadSelectedBackground(path);
                if (path == settings.LastBackground) return;
                settings.LastBackground = path;
                backgroundDirty = true;
            }
            catch (Exception) { }
        }
    }
}
