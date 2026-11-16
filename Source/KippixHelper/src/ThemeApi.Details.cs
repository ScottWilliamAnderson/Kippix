using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Game page (Playnite's game details view): its settings, an instant background (no black flash),
    /// the achievements button for just this game, and opening / closing the page from our options.
    /// </summary>
    public partial class ThemeApi
    {
        private const int DetBase = 320;                                  // default: the block starts 320 px down
        private const int DetOffsetMin = -320, DetOffsetMax = 100, DetOffsetStep = 10;   // lower than +100 cuts off Play etc.
        private const int MediaOffsetMin = -200, MediaOffsetMax = 200;
        private const double DetTrackWidth = 480;

        public ICommand CycleDetDescPositionCommand { get; private set; }
        public ICommand CycleDetLogoPositionCommand { get; private set; }
        public ICommand CycleDetArtCommand { get; private set; }
        public ICommand DetOffsetResetCommand { get; private set; }
        public ICommand OpenGameAchievementsCommand { get; private set; }
        public ICommand OpenGamePageSettingsCommand { get; private set; }
        public ICommand CycleDetMediaSizeCommand { get; private set; }
        public ICommand MediaOffsetResetCommand { get; private set; }

        // where the game page settings were opened from (decides what B does)
        private bool gamePageFromDetails;
        private bool weOpenedDetails;

        private void InitDetailsCommands()
        {
            CycleDetDescPositionCommand = new RelayCommand(_ => { settings.DetDescPosition = (settings.DetDescPosition + 1) % 3; SaveSettings(); NotifyDetails(); });
            CycleDetLogoPositionCommand = new RelayCommand(_ => { settings.DetLogoPosition = (settings.DetLogoPosition + 1) % 3; SaveSettings(); NotifyDetails(); });
            CycleDetLayoutCommand = new RelayCommand(_ => { settings.DetLayout = (Layout + 1) % 3; SaveSettings(); NotifyDetails(); });
            CycleDetArtCommand = new RelayCommand(_ => { settings.DetArt = (settings.DetArt + 1) % 3; SaveSettings(); NotifyDetails(); UpdateTrailer(); });
            DetOffsetResetCommand = new RelayCommand(_ => { settings.DetOffset = 0; SaveSettings(); NotifyDetails(); });
            OpenGameAchievementsCommand = new RelayCommand(_ => OpenAchievementsForSelected());
            OpenGamePageSettingsCommand = new RelayCommand(_ =>
            {
                gamePageFromDetails = true;
                weOpenedDetails = false;
                if (openPanel != "options") TogglePanel("options");
                SetOptionsPage("gamepage");
            });
            CycleDetMediaSizeCommand = new RelayCommand(_ => { settings.DetMediaSize = (settings.DetMediaSize + 1) % 4; SaveSettings(); NotifyDetails(); });
            MediaOffsetResetCommand = new RelayCommand(_ => { settings.DetMediaOffset = 0; SaveSettings(); NotifyDetails(); });
        }

        // ---------------- settings ----------------

        private bool SetDet(ref bool field, bool value)
        {
            if (field == value) return false;
            field = value;
            SaveSettings();
            NotifyDetails();
            return true;
        }

        public bool DetShowLogo { get { return settings.DetShowLogo; } set { SetDet(ref settings.DetShowLogo, value); } }
        public bool DetShowInfo { get { return settings.DetShowInfo; } set { SetDet(ref settings.DetShowInfo, value); } }
        public bool DetShowDescription { get { return settings.DetShowDescription; } set { SetDet(ref settings.DetShowDescription, value); } }
        public bool DetShowAchButton { get { return settings.DetShowAchButton; } set { SetDet(ref settings.DetShowAchButton, value); } }
        public bool DetShowDate { get { return settings.DetShowDate; } set { SetDet(ref settings.DetShowDate, value); } }
        public bool DetShowHints { get { return settings.DetShowHints; } set { SetDet(ref settings.DetShowHints, value); } }
        public bool DetBlackBackground { get { return settings.DetBlackBackground; } set { SetDet(ref settings.DetBlackBackground, value); } }
        public bool DetShowBackground { get { return !settings.DetBlackBackground; } }
        public bool DetShowOptionsButton { get { return settings.DetShowOptionsButton; } set { SetDet(ref settings.DetShowOptionsButton, value); } }
        public bool DetShowTrailerControls { get { return settings.DetShowTrailerControls; } set { SetDet(ref settings.DetShowTrailerControls, value); } }

        // ---------------- layout: left side / right side / centre ----------------

        public ICommand CycleDetLayoutCommand { get; private set; }
        private int Layout { get { return Math.Max(0, Math.Min(2, settings.DetLayout)); } }

        /// <summary>(0.89) Game page › Layout › Page design: Default (info left, media right) or Centred (media at the top
        /// middle, the info centred under it).</summary>
        internal void SetDetDesign(bool centred)
        {
            settings.DetLayout = centred ? 2 : 0;
            SaveSettings();
            NotifyDetails();
            try { if (detailsOpen) ApplyDetPlacement(); } catch (Exception) { }
        }
        private ICommand cycDetDesign;
        public ICommand CycleDetDesignCommand { get { return cycDetDesign ?? (cycDetDesign = new RelayCommand(_ => { SetDetDesign(Layout != 2); PlaySound("move"); Notify("DetDesignText"); })); } }
        public string DetDesignText { get { return Layout == 2 ? Loc("LOC_KX_DetDesignCentred", "Centred") : Loc("LOC_KX_DetDesignDefault", "Default"); } }

        public string DetLayoutText
        {
            get
            {
                return Layout == 1 ? Loc("LOC_KX_LayoutRight", "Right side")
                     : Layout == 2 ? Loc("LOC_KX_LayoutCentre", "Centre")
                     : Loc("LOC_KX_LayoutLeft", "Left side");
            }
        }
        /// <summary>Left side uses the logo position setting; Right side and Centre put the logo centred over the info.</summary>
        public bool DetLogoPositionEnabled { get { return settings.DetShowLogo && Layout == 0; } }
        /// <summary>Height / media height bars: greyed out while Master edit moves things on the game page.</summary>
        public bool DetPlacementEnabled { get { return true; } }
        public bool DetMediaPlacementEnabled { get { return settings.DetArt != 2; } }
        public bool DetLogoOverInfo { get { return Layout != 0; } }

        // media: right column (left side layout), left column (right side layout), or across the top (centre)
        public int DetMediaColumn { get { return Layout == 0 ? 2 : 0; } }
        public int DetMediaColumnSpan { get { return Layout == 2 ? 3 : 1; } }
        public HorizontalAlignment DetMediaAlignment
        {
            get { return Layout == 1 ? HorizontalAlignment.Left : Layout == 2 ? HorizontalAlignment.Center : HorizontalAlignment.Right; }
        }
        public VerticalAlignment DetMediaVAlignment { get { return Layout == 2 ? VerticalAlignment.Top : VerticalAlignment.Center; } }

        public HorizontalAlignment DetInfoAlignment
        {
            get { return Layout == 1 ? HorizontalAlignment.Right : Layout == 2 ? HorizontalAlignment.Center : HorizontalAlignment.Left; }
        }
        /// <summary>The rows inside the info block: left-aligned normally, centred on the block for Right side and Centre.</summary>
        public HorizontalAlignment DetBlockAlignment { get { return Layout == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center; } }
        public HorizontalAlignment DetLogoInnerAlignment { get { return Layout == 0 ? Align(settings.DetLogoPosition) : HorizontalAlignment.Center; } }
        public bool DetShadeLeft { get { return Layout == 0; } }
        public bool DetShadeRight { get { return Layout == 1; } }
        public bool DetShadeCentre { get { return Layout == 2; } }

        // left side layout: the Description position setting; Right side and Centre centre it
        public HorizontalAlignment DetDescAlignment { get { return Layout == 0 ? Align(settings.DetDescPosition) : HorizontalAlignment.Center; } }
        public HorizontalAlignment DetLogoAlignment
        {
            get { return Layout == 1 ? HorizontalAlignment.Right : Layout == 2 ? HorizontalAlignment.Center : Align(settings.DetLogoPosition); }
        }
        public string DetDescPositionText { get { return PositionText(settings.DetDescPosition); } }
        public string DetLogoPositionText { get { return PositionText(settings.DetLogoPosition); } }

        public string DetArtText
        {
            get
            {
                return settings.DetArt == 1 ? Loc("LOC_KX_ArtTrailer", "Trailer")
                     : settings.DetArt == 2 ? Loc("LOC_KX_ArtHidden", "Nothing")
                     : Loc("LOC_KX_ArtCover", "Game cover");
            }
        }
        public bool DetShowArt { get { return settings.DetArt != 2; } }
        public bool DetArtVideo { get { return settings.DetArt == 1; } }
        public bool DetMediaEnabled { get { return settings.DetArt != 2; } }

        // media sizes (cover height, trailer width) for small / medium / large / extra large
        private static readonly double[] CoverHeights = { 440, 560, 680, 800 };
        private static readonly double[] VideoWidths = { 560, 720, 880, 1040 };
        public string DetMediaSizeText { get { return SizeText(settings.DetMediaSize); } }
        public double DetCoverHeight { get { return CoverHeights[Math.Max(0, Math.Min(3, settings.DetMediaSize))]; } }
        public double DetVideoWidth { get { return VideoWidths[Math.Max(0, Math.Min(3, settings.DetMediaSize))]; } }
        public double DetVideoHeight { get { return Math.Round(DetVideoWidth * 9 / 16); } }

        /// <summary>The info column leaves room for the media on the right (or uses the whole width when there's none).</summary>
        public double DetInfoMaxWidth
        {
            get
            {
                double full = DesignWidth - 192;   // (1.0.0) the screen minus the page's side margins (1728 on 16:9)
                if (Layout == 2) return 1300;   // centred under the media: a readable width
                // (0.90) the media's room is kept even when it's hidden or on the lower page, so nothing else moves when it comes and goes
                double media = settings.DetArt == 1 ? DetVideoWidth : DetCoverHeight * 0.75;
                return Math.Max(700, full - 80 - media);
            }
        }

        // media height: VerticalAlignment Center, so a margin on one side moves the centre by half of it
        public Thickness DetMediaMargin
        {
            get
            {
                int o = settings.DetMediaOffset;
                if (Layout == 2) return new Thickness(0, Math.Max(0, -o), 0, 0);   // at the top: it can only move down
                return o >= 0 ? new Thickness(0, 0, 0, 2 * o) : new Thickness(0, -2 * o, 0, 0);
            }
        }
        public string DetMediaOffsetText
        {
            get
            {
                int o = settings.DetMediaOffset;
                if (o == 0) return Loc("LOC_KX_CardsDefault", "Default");
                return o > 0 ? string.Format(Loc("LOC_KX_CardsUp", "{0} px higher"), o) : string.Format(Loc("LOC_KX_CardsDown", "{0} px lower"), -o);
            }
        }
        private double MediaFraction { get { return (double)(settings.DetMediaOffset - MediaOffsetMin) / (MediaOffsetMax - MediaOffsetMin); } }
        public double MediaFillWidth { get { return MediaFraction * DetTrackWidth; } }
        public Thickness MediaKnobMargin { get { return new Thickness(MediaFraction * DetTrackWidth - 12, 0, 0, 0); } }

        private void NudgeMedia(int delta)
        {
            int v = Math.Max(MediaOffsetMin, Math.Min(MediaOffsetMax, settings.DetMediaOffset + delta));
            if (v == settings.DetMediaOffset) return;
            settings.DetMediaOffset = v;
            SaveSettings();
            NotifyDetails();
        }

        // vertical placement of the logo / buttons / info / description
        public Thickness DetInfoMargin
        {
            get
            {
                if (Layout == 2)
                {
                    // centre: the info starts under the media's place (trailer + its buttons, or the cover). (0.90) That place is kept
                    // when the media is hidden, moved to the lower page or moved down, so the info doesn't jump about with it.
                    double media = settings.DetArt == 1 ? DetVideoHeight + (settings.DetShowTrailerControls ? 84 : 0) : DetCoverHeight;
                    int io = settings.DetOffset;
                    double top = media + 34 + io;
                    return new Thickness(0, Math.Max(0, top), 0, 0);
                }
                return new Thickness(0, DetBase + settings.DetOffset, 0, 0);
            }
        }
        public string DetOffsetText
        {
            get
            {
                int o = settings.DetOffset;
                if (o == 0) return Loc("LOC_KX_CardsDefault", "Default");
                return o < 0 ? string.Format(Loc("LOC_KX_CardsUp", "{0} px higher"), -o) : string.Format(Loc("LOC_KX_CardsDown", "{0} px lower"), o);
            }
        }
        // the bar reads left = lower, right = higher (like the card placement bar)
        private double DetFraction { get { return 1.0 - (double)(settings.DetOffset - DetOffsetMin) / (DetOffsetMax - DetOffsetMin); } }
        public double DetFillWidth { get { return DetFraction * DetTrackWidth; } }
        public Thickness DetKnobMargin { get { return new Thickness(DetFraction * DetTrackWidth - 12, 0, 0, 0); } }

        private void NudgeDetails(int delta)
        {
            // right on the bar = higher on screen = smaller offset
            int v = Math.Max(DetOffsetMin, Math.Min(DetOffsetMax, settings.DetOffset - delta));
            if (v == settings.DetOffset) return;
            settings.DetOffset = v;
            SaveSettings();
            NotifyDetails();
        }

        private void NotifyDetails()
        {
            foreach (string n in new[] { "DetShowLogo", "DetShowInfo", "DetShowDescription", "DetShowAchButton", "DetAchButtonShown", "DetShowDate", "DetShowHints", "DetBlackBackground", "DetShowBackground",
                                         "DetShowOptionsButton", "DetShowTrailerControls", "DetPlacementEnabled", "DetMediaPlacementEnabled", "FriendsSourceText", "DetLayoutText", "DetDesignText", "DetLogoPositionEnabled", "DetLogoOverInfo", "DetMediaColumn", "DetMediaColumnSpan",
                                         "DetMediaAlignment", "DetMediaVAlignment", "DetInfoAlignment", "DetBlockAlignment", "DetLogoInnerAlignment",
                                         "DetShadeLeft", "DetShadeRight", "DetShadeCentre",
                                         "DetDescAlignment", "DetLogoAlignment", "DetDescPositionText", "DetLogoPositionText",
                                         "DetArtText", "DetShowArt", "DetArtVideo", "DetMediaEnabled", "DetInfoMaxWidth", "DetMediaSizeText", "DetCoverHeight",
                                         "DetVideoWidth", "DetVideoHeight", "DetMediaMargin", "DetMediaOffsetText", "MediaFillWidth", "MediaKnobMargin",
                                         "DetInfoMargin", "DetOffsetText", "DetFillWidth", "DetKnobMargin", "DetDateText" })
                Notify(n);
        }

        // ---------------- moving between the buttons and the trailer ----------------

        /// <summary>
        /// On the game page, Left / Right between the round buttons and the trailer go straight across
        /// (Windows' own guess lands on the description, which sits nearer). True if it moved the focus.
        /// </summary>
        private bool DetailsSideNav(KeyEventArgs e)
        {
            if (DetailsSideNavMove(e)) return true;
            return DetailsDeadEnd(e);
        }

        /// <summary>
        /// Game page (both pages): a direction with nothing there that you can see keeps the highlight where it is.
        /// WPF's own guess otherwise lands on something invisible, e.g. Left from the first "More like this" cover went to
        /// KX_DetMoreSink (the 1-pixel see-through stop at the bottom left of the lower page) and the highlight vanished.
        /// </summary>
        private bool DetailsDeadEnd(KeyEventArgs e)
        {
            if (e.Handled || IsPadRelease(e)) return false;
            var focused = Keyboard.FocusedElement as UIElement;
            if (focused == null) return false;
            var root = UiTools.FindByName(Application.Current.MainWindow, "PART_ElemGameDetails");
            if (root == null || !root.IsAncestorOf(focused)) return false;
            var dir = e.Key == Key.Left ? FocusNavigationDirection.Left : e.Key == Key.Right ? FocusNavigationDirection.Right
                    : e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
            DependencyObject next = null;
            try { next = focused.PredictFocus(dir); } catch (Exception) { }
            if (next != null && SeenOnGamePage(next, root)) return false;   // somewhere real to go: WPF moves there
            e.Handled = true;   // nothing that way (or nothing you'd see): stay put
            var more = UiTools.FindByName(Application.Current.MainWindow, "KX_DetMore");
            if (next != null && e.Key == Key.Down && ShowDetMore && more != null && !more.IsAncestorOf(focused))
            {
                // (Down on the top page toward something hidden: the lower page, as when nothing is below)
                DetailsScrollTo(true, true);
                ui.BeginInvoke(new Action(FocusLowerPage), DispatcherPriority.Loaded);
            }
            return true;
        }

        /// <summary>Shown on screen: visible, more than a dot, and not see-through (itself or anything it sits in, up to the game page).</summary>
        private static bool SeenOnGamePage(DependencyObject d, DependencyObject root)
        {
            var fe = d as FrameworkElement;
            if (fe == null) return true;   // (not something we can judge: leave it to WPF)
            if (!fe.IsVisible || fe.ActualWidth < 4 || fe.ActualHeight < 4) return false;
            if (fe.Name == "KX_DetMoreSink") return false;
            DependencyObject p = d;
            while (p != null && !ReferenceEquals(p, root))
            {
                var u = p as UIElement;
                if (u != null && u.Opacity < 0.05) return false;
                p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p);
            }
            return true;
        }

        private bool DetailsSideNavMove(KeyEventArgs e)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused == null) return false;
            bool vertical = e.Key == Key.Up || e.Key == Key.Down;
            if (vertical != (Layout == 2)) return false;   // centre: the trailer is above; otherwise beside
            var video = Ancestor(focused, "VideoArea");
            var buttons = video == null ? Ancestor(focused, "DetButtons") : null;
            if (video == null && buttons == null) return false;
            var topPage = UiTools.FindByName(Application.Current.MainWindow, "PART_ViewHost");
            if (video != null && (topPage == null || !topPage.IsAncestorOf(video))) return false;   // the trailer was sent to the lower page
            bool towardInfo = Layout == 2 ? e.Key == Key.Down : Layout == 1 ? e.Key == Key.Right : e.Key == Key.Left;
            var dir = e.Key == Key.Left ? FocusNavigationDirection.Left : e.Key == Key.Right ? FocusNavigationDirection.Right
                    : e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
            var next = (focused as UIElement) != null ? ((UIElement)focused).PredictFocus(dir) as DependencyObject : null;

            if (video != null)
            {
                if (next != null && Ancestor(next, "VideoArea") == video) return false;   // moving within the trailer controls
                e.Handled = true;
                if (towardInfo) FocusDetElement(Layout == 2 ? "PART_ButtonContext" : "KX_DetSettings");
                return true;
            }
            // from the round buttons toward the trailer
            if (towardInfo || (!vertical && next != null && Ancestor(next, "DetButtons") == buttons)) return false;
            var area = UiTools.FindByName(Application.Current.MainWindow, "VideoArea");
            if (area == null || !area.IsVisible || topPage == null || !topPage.IsAncestorOf(area)) return false;
            e.Handled = true;
            FocusDetElement("KX_DetVideo");
            return true;
        }

        private static FrameworkElement Ancestor(DependencyObject d, string name)
        {
            while (d != null)
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.Name == name) return fe;
                d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        private static void FocusDetElement(string name)
        {
            var el = UiTools.FindByName(Application.Current.MainWindow, name);
            if (el != null && el.IsVisible) Keyboard.Focus(el);
        }

        // ---------------- date ----------------

        private string detDateText = "";
        public string DetDateText { get { return detDateText; } }

        private void RefreshDetailsDate()
        {
            var now = DateTime.Now;
            string t = now.ToString(settings.Clock24h ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);
            if (t == detDateText) return;
            detDateText = t;
            Notify("DetDateText");
        }

        // ---------------- instant background ----------------

        private ImageSource selectedBackground;
        private string selectedBackgroundPath;
        private int backgroundRequest;

        /// <summary>The selected game's background, already decoded, so the game page never flashes black while it loads.</summary>
        public ImageSource SelectedBackground { get { return selectedBackground; } }

        private void PreloadSelectedBackground(string path)
        {
            if (path == selectedBackgroundPath) return;
            selectedBackgroundPath = path;
            int request = ++backgroundRequest;
            KippixShared.SafeWork.Queue(_ =>
            {
                var img = Images.LoadUncached(path, 1920);
                OnUi(() =>
                {
                    if (request != backgroundRequest) return;
                    selectedBackground = img;
                    Notify("SelectedBackground");
                });
            });
        }

        // ---------------- Playnite's view model ----------------

        private static object MainVm()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            return w != null ? w.DataContext : null;
        }

        private static bool GameDetailsVisible()
        {
            try
            {
                object vm = MainVm();
                PropertyInfo p = vm != null ? vm.GetType().GetProperty("GameDetailsVisible") : null;
                object v = p != null ? p.GetValue(vm, null) : null;
                return v is bool && (bool)v;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Opens the game page (for the last played game) if it isn't showing already.</summary>
        private bool EnsureGameDetailsOpen()
        {
            try
            {
                if (GameDetailsVisible()) return false;
                var win = Application.Current.MainWindow;
                var list = UiTools.FindByName(win, "PART_ListGameItems") as ListBox;
                Game last = api.Database.Games.Where(g => !g.Hidden && g.LastActivity.HasValue).OrderByDescending(g => g.LastActivity).FirstOrDefault();
                if (list != null && last != null)
                {
                    foreach (object entry in list.Items)
                    {
                        if (EntryId(entry) != last.Id) continue;
                        list.SelectedItem = entry;
                        list.ScrollIntoView(entry);
                        break;
                    }
                }
                RunMainCommand("ToggleGameDetailsCommand");
                // the game page takes focus when it opens; hand it back to the options panel
                var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(350) };
                t.Tick += (s, e) => { t.Stop(); if (openPanel == "options") FocusPanelSoon(); };
                t.Start();
                return true;
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: couldn't open the game page"); return false; }
        }

        /// <summary>Closes the game page; focusList moves the highlight to the selected cover (otherwise our panel keeps it).</summary>
        private void CloseGameDetails(bool focusList)
        {
            try
            {
                if (GameDetailsVisible()) RunMainCommand("ToggleGameDetailsCommand");
                if (!focusList)
                {
                    // Playnite moves focus to the list when the page closes; take it back to the options
                    var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        if (openPanel == "options" && !UiTools.FocusCommandParameter("KX_OptionsPanel", "gamepage")) FocusPanelSoon();
                    };
                    t.Start();
                    return;
                }
                ui.BeginInvoke(new Action(() =>
                {
                    var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
                    if (list == null || list.SelectedItem == null) return;
                    var c = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as UIElement;
                    if (c != null) Keyboard.Focus(c);
                }), DispatcherPriority.ContextIdle);
            }
            catch (Exception) { }
        }

        private static Guid EntryId(object entry)
        {
            if (entry == null) return Guid.Empty;
            try
            {
                var g = entry as Game;
                if (g != null) return g.Id;
                PropertyInfo p = entry.GetType().GetProperty("Id");
                object v = p != null ? p.GetValue(entry, null) : null;
                if (v is Guid) return (Guid)v;
                p = entry.GetType().GetProperty("Game");
                g = p != null ? p.GetValue(entry, null) as Game : null;
                return g != null ? g.Id : Guid.Empty;
            }
            catch (Exception) { return Guid.Empty; }
        }

        // ---------------- achievements for this game ----------------

        private Guid pendingAchGame = Guid.Empty;
        private bool achOpenedForGame;

        private void OpenAchievementsForSelected()
        {
            Game game = null;
            try { game = api.MainView.SelectedGames != null ? api.MainView.SelectedGames.FirstOrDefault() : null; } catch (Exception) { }
            if (game == null) { OpenAchievementsView(); return; }
            pendingAchGame = game.Id;
            achOpenedForGame = true;
            if (openPanel != "achievements") TogglePanel("achievements");
            achPicker = "";
            if (achRows.Count == 0 || achLoading) { achPage = "games"; NotifyAch(); LoadAchievements(); return; }
            OpenPendingAchGame();
        }

        /// <summary>After the data is there: jump straight to the requested game's list.</summary>
        private bool OpenPendingAchGame()
        {
            if (pendingAchGame == Guid.Empty) return false;
            Guid id = pendingAchGame;
            pendingAchGame = Guid.Empty;
            var row = achRows.FirstOrDefault(r => r.Data.Id == id);
            if (row == null) { OpenAchGameAlone(id); return true; }
            OpenAchGame(row);
            return true;
        }

        private int achAloneRequest;

        /// <summary>
        /// The game isn't in the achievements list read earlier (e.g. Playnite Achievements has it with no achievements, as with
        /// Subnautica 2, or hadn't fetched it yet): its own page opens straight away, empty, while this game alone is looked up again
        /// (Playnite Achievements, SuccessStory, Steam's cache). Found: its list fills in (and it joins the list); not found: it says so.
        /// </summary>
        private void OpenAchGameAlone(Guid id)
        {
            Game game = null;
            try { game = api.Database.Games.Get(id); } catch (Exception) { }
            int request = ++achAloneRequest;
            var empty = new AchGameData { Id = id, Name = game != null ? game.Name : "" };
            string coverFile = CoverOf(id);
            var covers = new Dictionary<Guid, string>();
            covers[id] = coverFile;
            achOpenedForGame = true;
            achGameNoData = Loc("LOC_KX_AchLookingForGame", "Looking for this game's achievements…");
            OpenAchGame(MakeGameRow(empty, covers));
            AchGameCountText = "";
            Notify("AchGameCountText");
            var shownRow = achGameRow;
            KippixShared.SafeWork.Queue(_ =>
            {
                ImageSource img = null;
                try { img = Images.Load(coverFile, CoverPx); } catch (Exception) { }
                AchGameData found = null;
                string source = null;
                try { found = AchievementSource.LoadOne(api, game, out source); }
                catch (Exception ex) { logger.Warn("Kippix: looking up achievements for one game failed: " + ex.Message); }
                OnUi(() =>
                {
                    bool stillShown = request == achAloneRequest && openPanel == "achievements" && achPage != "games" && ReferenceEquals(achGameRow, shownRow);
                    if (found == null)
                    {
                        logger.Info("Kippix: no achievements found for " + (game != null ? game.Name : id.ToString()));
                        if (!stillShown) return;
                        shownRow.Cover = img;
                        if (AchGameCover == null && img != null) { AchGameCover = img; Notify("AchGameCover"); }
                        achGameNoData = Loc("LOC_KX_AchNoneForGameYet", "No achievements found for this game yet.");
                        ApplyAchItems();
                        return;
                    }
                    logger.Info("Kippix: achievements for " + found.Name + " from " + source + " (not in the list read earlier)");
                    // it joins the list (for the achievements screen, the game page's highlights, the profile)
                    var list = achData.Where(d => d != null && d.Id != found.Id).ToList();
                    list.Add(found);
                    achData = list;
                    achRows.RemoveAll(r => r.Data.Id == found.Id);
                    var row = MakeGameRow(found, covers);
                    row.Cover = img;
                    achRows.Add(row);
                    ApplyAchGames();
                    if (stillShown) OpenAchGame(row);
                });
            });
        }
    }
}
