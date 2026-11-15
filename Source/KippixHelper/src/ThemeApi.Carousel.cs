using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Something in the carousel that isn't a game: a tile before your games (Store, Achievements, a pinned game). Kept general
    /// so other things can go in the carousel too (e.g. folders): a name, a line under it, an icon, its own picture
    /// (PictureFile, decoded at the card's size when it shows) and what A does on it.
    /// </summary>
    public class CarouselItem : Observable
    {
        public string Kind { get; set; }           // "tile" (Store, Achievements, a pinned game), "folder" (Folder: a folder tile) or "game" (Tall / Full Screen)
        public string Key { get; set; }
        private string name = "", sub = "", icon = "";
        public string Name { get { return name; } set { Set(ref name, value ?? "", "Name"); } }
        public string Sub { get { return sub; } set { if (Set(ref sub, value ?? "", "Sub")) Notify("HasSub"); } }
        public bool HasSub { get { return sub.Length > 0; } }
        /// <summary>The icon when there's no picture: store, trophy, pin, folder.</summary>
        public string Icon { get { return icon; } set { Set(ref icon, value ?? "", "Icon"); } }
        private bool showDot, isPinned;
        public bool ShowDot { get { return showDot; } set { Set(ref showDot, value, "ShowDot"); } }
        /// <summary>A game pinned before the games (a small pin on its picture).</summary>
        public bool IsPinned { get { return isPinned; } set { Set(ref isPinned, value, "IsPinned"); } }
        private ImageSource picture;
        public ImageSource Picture { get { return picture; } set { if (Set(ref picture, value, "Picture")) { Notify("HasPicture"); Notify("HasNoPicture"); Notify("NameOnPicture"); } } }
        public bool HasPicture { get { return picture != null; } }
        public bool HasNoPicture { get { return picture == null; } }
        /// <summary>A folder with its photo: its name along the bottom of the photo.</summary>
        public bool NameOnPicture { get { return picture != null && Kind == "folder"; } }
        private bool showInstalled;
        /// <summary>A game that's installed, with Tile badges › Installed on (a small tick).</summary>
        public bool ShowInstalled { get { return showInstalled; } set { Set(ref showInstalled, value, "ShowInstalled"); } }
        private CornerRadius corner = new CornerRadius(10), ringCorner = new CornerRadius(14);
        /// <summary>Its rounded corners (Full Screen's panels nearly square) and its highlight edge's, just outside them.</summary>
        public CornerRadius Corner { get { return corner; } set { Set(ref corner, value, "Corner"); } }
        public CornerRadius RingCorner { get { return ringCorner; } set { Set(ref ringCorner, value, "RingCorner"); } }
        private Thickness ringMargin = new Thickness(-4);
        /// <summary>The highlight edge just outside the card (Full Screen's panels: just inside, as they reach the screen's edges).</summary>
        public Thickness RingMargin { get { return ringMargin; } set { Set(ref ringMargin, value, "RingMargin"); } }
        internal string PictureFile;               // its picture on disk (loaded at the size it's shown)
        internal string AltPictureFile;            // a game: its background, when the cover can't be read
        internal string CoverFile, BackFile;       // (0.90) a game's cover and background (Card look › Wide shows the background)
        internal bool PictureWide;                 // a landscape picture (decoded wider to fill a tall card)
        internal string PictureFor;                // the file + size the picture was loaded for
        internal Action Activate;                  // A on it
        internal object Source;                    // what it stands for (a HomeTile, a HomeFolder)
    }

    /// <summary>
    /// Home › Row: the designs drawn by the theme itself (Lower with Loop, Tall, Full Screen), as a carousel.
    /// Playnite's own row stays where it is, unseen (clipped away), and keeps the highlight and the selected game: so A, X,
    /// Start, hold A, hold X, the game info, the background and the rest work exactly as with the normal row. Every Left /
    /// Right is the carousel's own single step (it selects the game in Playnite's row), so the end of the list can come round
    /// to the start. Items that aren't games take the highlight on an unseen button (KX_CarouselFocus) whose A runs them.
    /// Drawing: KX_CarouselHost fills the whole home screen; the cards sit on a Canvas at rectangles worked out by
    /// CarouselGeometry (plain numbers: Canvas.Left / Top, Width / Height set on each card, nothing scaled). Tall, Full Screen
    /// and Folder's tiles draw everything (games too) with the theme's own KX_CarouselItemTemplate; Lower with Loop keeps
    /// Playnite's card (its shapes, wide art and badges) at Playnite's row's card size.
    /// Only the cards on screen (and one either side) are made; they're reused as the carousel moves, and nothing runs per
    /// frame once it has stopped.
    /// </summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // Settings (Home settings › Game cards and layout › Row)
        // =====================================================================

        /// <summary>
        /// The carousel draws the games: Row, and either Loop on or a design other than Lower (Folder: an opened folder in Row).
        /// Folder's tiles always (Playnite's row can only show games).
        /// </summary>
        internal bool CarouselOn { get { return (LayoutNow == 0 && (settings.RowDesign != 0 || settings.RowLoop)) || FolderTilesShown; } }
        /// <summary>
        /// The design the carousel draws now: Row's Design, except Folder's tiles, which are always drawn like Full Screen
        /// (0 Lower, 1 Tall, 2 Full Screen).
        /// </summary>
        internal int CarDesign { get { return FolderTilesShown ? 2 : Math.Max(0, Math.Min(2, settings.RowDesign)); } }
        /// <summary>Going past either end comes round to the other (Tall always does; Folder's tiles stop at the ends).</summary>
        private bool CarLoop { get { return !FolderTilesShown && (settings.RowDesign == 1 || settings.RowLoop); } }
        private bool CarTallOrFull { get { return CarouselOn && CarDesign != 0; } }
        internal const int FullCountMin = 1, FullCountMax = 10;   // (0.89: from 1)
        /// <summary>Full Screen: panels on screen at once (Folder's tiles have their own number).</summary>
        private int CarFullCount
        {
            get { return Math.Max(FullCountMin, Math.Min(FullCountMax, FolderTilesShown ? settings.FolderTileCount : settings.RowFullCount)); }
        }
        /// <summary>Tall / Full Screen (and Folder's tiles): the logo, game info and button hints are out of the way (Row's switch brings them back).</summary>
        private bool CarHidesInfo { get { return carActive && (FolderTilesShown || (CarDesign != 0 && !settings.RowShowInfo)); } }

        public string RowDesignText
        {
            get
            {
                return settings.RowDesign == 1 ? Loc("LOC_KX_RowDesignTall", "Tall")
                     : settings.RowDesign == 2 ? Loc("LOC_KX_RowDesignFull", "Full Screen")
                     : Loc("LOC_KX_RowDesignLower", "Lower");
            }
        }
        public string RowDesignSub
        {
            get
            {
                return settings.RowDesign == 1 ? Loc("LOC_KX_RowDesignTallSub3", "Big covers across the screen, the highlighted one in the middle")
                     : settings.RowDesign == 2 ? Loc("LOC_KX_RowDesignFullSub3", "Tall panels edge to edge, the whole height of the screen (Up for the top bar)")
                     : Loc("LOC_KX_RowDesignLowerSub", "A row of cards near the bottom of the screen");
            }
        }

        // ---- Full Screen: how many panels; Tall / Full Screen: the logo, info and hints ----
        public bool RowFullShown { get { return settings.RowDesign == 2; } }
        public string RowFullCountLabel { get { return Loc("LOC_KX_RowFullCount", "Games on screen"); } }
        public string RowFullCountSub { get { return Loc("LOC_KX_RowFullCountSub", "More makes the panels narrower"); } }
        public string RowFullCountText { get { return Math.Max(FullCountMin, Math.Min(FullCountMax, settings.RowFullCount)).ToString(); } }
        // (0.89) Tall: whole covers across, 1-10
        public bool RowTallShown { get { return settings.RowDesign == 1; } }
        public string RowTallCountSub { get { return Loc("LOC_KX_RowTallCountSub", "Whole covers across; the next ones show cut off at the edges"); } }
        public string RowTallCountText { get { return Math.Max(1, Math.Min(10, settings.RowTallCount)).ToString(); } }
        private ICommand cycleRowTallCountCmd;
        public ICommand CycleRowTallCountCommand
        {
            get
            {
                return cycleRowTallCountCmd ?? (cycleRowTallCountCmd = new RelayCommand(_ =>
                {
                    int now = Math.Max(1, Math.Min(10, settings.RowTallCount));
                    settings.RowTallCount = now >= 10 ? 1 : now + 1;
                    SaveSettings();
                    PlaySound("move");
                    Notify("RowTallCountText");
                    ApplyHomeLayoutSoon();
                }));
            }
        }
        private ICommand cycleRowFullCountCmd;
        public ICommand CycleRowFullCountCommand
        {
            get
            {
                return cycleRowFullCountCmd ?? (cycleRowFullCountCmd = new RelayCommand(_ =>
                {
                    int now = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.RowFullCount));
                    settings.RowFullCount = now >= FullCountMax ? FullCountMin : now + 1;
                    SaveSettings();
                    PlaySound("move");
                    Notify("RowFullCountText");
                    ApplyHomeLayoutSoon();
                }));
            }
        }
        /// <summary>Tall and Full Screen hide the logo, game info and hints unless this is on.</summary>
        public bool RowInfoShown { get { return settings.RowDesign != 0; } }
        public string RowShowInfoLabel { get { return Loc("LOC_KX_RowShowInfo", "Show the logo, game info and hints"); } }
        public string RowShowInfoSub { get { return Loc("LOC_KX_RowShowInfoSub", "Off: the cards have the screen to themselves (Full Screen shows them over the bottom of the panels)"); } }
        public bool RowShowInfo
        {
            get { return settings.RowShowInfo; }
            set
            {
                if (settings.RowShowInfo == value) return;
                settings.RowShowInfo = value;
                SaveSettings();
                Notify("RowShowInfo");
                HomeModeChanged();
            }
        }
        public bool RowLoop
        {
            get { return settings.RowLoop; }
            set
            {
                if (settings.RowLoop == value) return;
                settings.RowLoop = value;
                SaveSettings();
                Notify("RowLoop");
                carWhy = "Loop switched";
                HomeModeChanged();
            }
        }
        /// <summary>Loop has its own switch except in Tall (which always loops).</summary>
        public bool RowLoopShown { get { return settings.RowDesign != 1; } }
        /// <summary>Card size: Lower's cards (Tall shows three across, Full Screen has its own number on screen).</summary>
        public bool CardSizeShown { get { return settings.RowDesign == 0; } }
        /// <summary>The extra rows under the games are for Row › Lower only (not Folder).</summary>
        public bool IsRowLower { get { return settings.HomeLayoutMode == 0 && settings.RowDesign == 0; } }

        private ICommand cycleRowDesignCmd;
        public ICommand CycleRowDesignCommand
        {
            get
            {
                return cycleRowDesignCmd ?? (cycleRowDesignCmd = new RelayCommand(_ =>
                {
                    settings.RowDesign = (settings.RowDesign + 1) % 3;   // (room for more designs later)
                    if (settings.RowDesign != 0) LeaveExtraRows(false);
                    SaveSettings();
                    PlaySound("move");
                    carWhy = "Design changed";
                    HomeModeChanged();
                }));
            }
        }

        private void NotifyCarouselSettings()
        {
            foreach (string n in new[] { "RowDesignText", "RowDesignSub", "RowLoop", "RowLoopShown", "CardSizeShown", "IsRowLower", "CarouselShown", "RowFullShown", "RowFullCountText", "RowTallShown", "RowTallCountText",
                                         "RowInfoShown", "RowShowInfo", "ShowCardLook", "ShowCardSpacing", "ShowTriArrange", "ShowTriSpacing", "ShadeLeft", "ShadeCenter", "ShadeRight" })
                Notify(n);
            NotifyHomeBackground();   // (0.90: Full Screen has a black background)
            Notify("CardShapeText");
            try { EnsureLiveTimer(); } catch (Exception) { }
        }

        // =====================================================================
        // Where it sits
        // =====================================================================

        /// <summary>Whether the carousel is on screen (KX_CarouselHost's Visibility is set in code by CarApplyShown, nowhere else).</summary>
        public bool CarouselShown { get { return carActive && carListVisible; } }

        // where the cards may go in the host (it fills the home screen): from under the top bar (and the filter name) down to
        // the bottom, less what's kept free at the bottom (Tall with the logo / info / hints)
        private double carTopY = 112, carBottomInset;

        /// <summary>
        /// Tall / Full Screen (and Folder's tiles), from ApplyHomeLayout: the top of the room is measured from the top bar on
        /// screen (and the filter name under it); Tall keeps the logo, game info and hints clear when they show. True when it
        /// dealt with the layout.
        /// </summary>
        private bool CarouselScreenLayout(FrameworkElement root)
        {
            if (!CarTallOrFull || !carActive || root == null || root.ActualHeight <= 0) return false;
            double rh = root.ActualHeight;
            double top = CarTopBarBottom(root) + 8;
            {
                // the filter name (or the folder's name) on its own line under the top bar; (0.89) its room is kept when it's
                // switched off or deleted, so the cards don't move
                var above = new Thickness(72, 0, 72, Math.Max(0, Math.Round(rh - (top + 34))));
                if (above != filterAboveMargin) { filterAboveMargin = above; Notify("FilterAboveMargin"); }
                top += 40;
            }
            double bottom = 0;
            if (CarDesign == 1 && !CarHidesInfo)
            {
                // the logo, the info line and the hints at their largest (so the cards don't move from game to game)
                bottom = 36 + 30;
                if (settings.ShowLogo) bottom += 150 * LogoScales[Math.Max(0, Math.Min(3, settings.LogoSize))] + 14;
                if (settings.ShowGameInfo) bottom += 44 * TextScales[Math.Max(0, Math.Min(3, settings.InfoSize))];
                if (settings.ShowButtonHints) bottom += 48 * TextScales[Math.Max(0, Math.Min(3, settings.HintsSize))];
                bottom = Math.Round(bottom);
            }
            carTopY = Math.Round(top);
            carBottomInset = bottom;
            // Full Screen: the logo, info and hints (when they show) sit over the bottom of the panels
            var infoAt = new Thickness(72, 0, 72, CarDesign == 2 ? 44 : 36);
            if (infoAt != gameInfoMargin) { gameInfoMargin = infoAt; Notify("GameInfoMargin"); }
            CarouselUpdate();
            return true;
        }

        /// <summary>The bottom of the top bar on screen (in <paramref name="root"/>'s space, which is the carousel host's): where Tall / Full Screen start.</summary>
        private double CarTopBarBottom(FrameworkElement root)
        {
            double b = 104;   // (the top bar's own row, when it can't be measured)
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                // (shortcuts down the side: the bar runs down the screen, so its left part, with the profile, is measured)
                var bar = win != null ? UiTools.FindByName(win, settings.ShortcutsVertical ? "KX_TopLeft" : "KX_TopBar") as FrameworkElement : null;
                if (bar != null && bar.IsVisible && bar.ActualHeight > 0 && root.IsAncestorOf(bar))
                    b = bar.TranslatePoint(new Point(0, bar.ActualHeight), root).Y;
            }
            catch (Exception) { b = 104; }
            if (double.IsNaN(b) || double.IsInfinity(b)) b = 104;
            return Math.Round(Math.Max(40, Math.Min(root.ActualHeight * 0.22, b)));
        }

        // =====================================================================
        // State
        // =====================================================================

        private class CarSlot
        {
            public FrameworkElement El;            // Card: a ListBoxItem with Playnite's own card (Lower's games); else a ContentPresenter with KX_CarouselItemTemplate
            public bool Card;
            public int V;                          // its place along the carousel (the highlight is at carPos)
            public object Content;                 // Card: Playnite's entry; else a CarouselItem
            public UIElement Ring;                 // its highlight edge (faded in on the highlighted one)
            public FrameworkElement Tile;          // the card itself (the trailer preview sits over it)
            public string ArtKey;                  // the picture it's waiting for
            public bool Dressed;
            public int DressTries;
            public double X = double.NaN, Y = double.NaN, W = double.NaN, H = double.NaN, R = double.NaN;
            public int Z = int.MinValue;
            public void Forget() { X = Y = W = H = R = double.NaN; Z = int.MinValue; }
        }

        private static readonly Geometry CarHiddenClip = MakeHiddenClip();
        private static Geometry MakeHiddenClip() { var g = new RectangleGeometry(new Rect(0, 0, 0, 0)); g.Freeze(); return g; }
        private bool carActive, carListVisible = true;
        private DateTime carRetryAt = DateTime.MinValue;
        private int carDesign = -1;
        private ListBox carList;
        private FrameworkElement carHost;
        private Canvas carCanvas;
        private ButtonBase carProxy;
        private DataTemplate carTileTemplate;
        private readonly List<CarouselItem> carExtras = new List<CarouselItem>();
        private readonly Dictionary<int, CarSlot> carShown = new Dictionary<int, CarSlot>();
        private readonly List<CarSlot> carFree = new List<CarSlot>();
        private readonly List<int> carDrop = new List<int>();
        private bool carHomeTilesHooked;

        // where it is: carPos (moving), carTargetV (where it's going), carIndex (the item highlighted: extras first, then the games)
        private double carPos;
        private int carTargetV;
        private int carIndex;
        private DateTime carLastMove = DateTime.MinValue;
        // Full Screen: the strip's place (the item at the left edge; moving) and where it's going. The highlight moves across the
        // panels; the strip only scrolls when the highlight comes near an edge.
        private double carView;
        private int carViewTarget;
        private int carTileReturn = -1;            // a tile opened something: the highlight comes back to it
        private DateTime carDetailsClosedAt = DateTime.MinValue;

        // the cards' sizes and places (CarouselGeometry; null until the host has a size)
        private CarouselFrame carFrame;
        private string carGeoSig;

        // the log: what changed last (set where the change is made), the last state and the last layout written
        private string carWhy;
        private string carShownLogged, carLayoutLogged;

        // =====================================================================
        // On / off, and keeping up with Playnite's list
        // =====================================================================

        /// <summary>
        /// The one place that decides whether the carousel is on (from HomeModeChanged and ApplyHomeLayout: Games shown as, Row's
        /// Design / Loop, a folder opened or closed). On: Playnite's row is clipped away and the carousel shows. Off: the
        /// carousel's cards are taken away, the host is collapsed and Playnite's row (or the Board, the grid, the list) shows as normal.
        /// </summary>
        private void CarouselApplyMode()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null || !IsFullscreen) return;
                NotifyCarouselSettings();
                var list = UiTools.FindByName(win, "PART_ListGameItems") as ListBox;
                CarHook(win, list);
                bool want = CarouselOn;
                // (only with the theme's carousel in place: Playnite's row is never hidden without something to show the games)
                bool ready = list != null && carHost != null && carCanvas != null && carTileTemplate != null;
                bool on = want && ready;
                if (want && !ready && carRetryAt == DateTime.MinValue)
                    logger.Warn("Kippix: carousel can't start: " + (list == null ? "no game list " : "") + (carHost == null ? "no KX_CarouselHost " : "") +
                                (carCanvas == null ? "no KX_Carousel " : "") + (carTileTemplate == null ? "no KX_CarouselItemTemplate" : ""));
                carRetryAt = want && !ready ? DateTime.Now.AddSeconds(10) : DateTime.MinValue;   // (not there yet: try again in a while)
                if (list != null)
                {
                    // Playnite's own row: still there (it keeps the highlight), but clipped away so nothing of it shows
                    if (on) { if (!ReferenceEquals(list.Clip, CarHiddenClip)) list.Clip = CarHiddenClip; }
                    else if (ReferenceEquals(list.Clip, CarHiddenClip)) list.ClearValue(UIElement.ClipProperty);
                }
                if (!on)
                {
                    if (carActive)
                    {
                        bool onTile = CarProxyFocused();
                        carActive = false;
                        CarClear();
                        StopCarFrames();
                        foreach (string n in new[] { "ShadeLeft", "ShadeCenter", "ShadeRight" }) Notify(n);
                        CarApplyShown();
                        if (onTile) FocusSelectedCard();   // the tile it was on has gone: onto the games
                    }
                    else CarApplyShown();
                    return;
                }
                if (carDesign != CarDesign) { CarClear(); carDesign = CarDesign; }
                if (!carActive) { carActive = true; foreach (string n in new[] { "ShadeLeft", "ShadeCenter", "ShadeRight" }) Notify(n); }
                CarApplyShown();
                // the tiles before the games are items in the carousel now, not a strip that slides in
                if (homeTilesShown || tilesVisible) { homeTilesShown = false; tilesVisible = false; SetCardsLeft(0); ShiftList(0); NotifyHomeTiles(); }
                CarBuildExtras();
                CarResync();
                ApplyHomeLayoutSoon();
            }
            catch (Exception ex) { logger.Warn("Kippix: carousel: " + ex.Message); }
            finally { carWhy = null; }   // (a change that didn't show or hide it isn't the reason for a later one)
        }

        /// <summary>
        /// The host shown or collapsed (set here only): with the carousel on, and like Playnite's row, not while the game page
        /// is open. Each change goes in the log once, with what's on the home screen and what changed.
        /// </summary>
        private void CarApplyShown()
        {
            bool show = carActive && carListVisible;
            if (carHost != null)
            {
                var v = show ? Visibility.Visible : Visibility.Collapsed;
                if (carHost.Visibility != v) { carHost.Visibility = v; Notify("CarouselShown"); }
            }
            string state = (show ? "shown" : "hidden") + ": " + CarStateText();
            if (state == carShownLogged) return;
            carShownLogged = state;
            logger.Info("Kippix: carousel " + state + (carWhy != null ? " (" + carWhy + ")" : "") + (carActive && !carListVisible ? " (Playnite's row is hidden: game page)" : ""));
            carWhy = null;
        }

        /// <summary>What's on the home screen, in words (for the log).</summary>
        private string CarStateText()
        {
            string[] designs = { "Lower", "Tall", "Full Screen" };
            string row = "Row " + designs[Math.Max(0, Math.Min(2, settings.RowDesign))] + (settings.RowDesign == 1 || settings.RowLoop ? " with Loop" : "");
            string[] layouts = { row, "Grid", "List", "Board", "Manual Board" };
            int now = Math.Max(0, Math.Min(4, LayoutNow));
            string what = layouts[now];
            if (now == 0 && !CarouselOn) what += " (Playnite's row)";
            if (FolderTilesShown) return "Folder, the folder tiles (Full Screen, " + CarFullCount + " across)";
            if (FolderInside) return "Folder, a folder open as " + what;
            return what;
        }

        private void CarHook(Window win, ListBox list)
        {
            if (list != null && !ReferenceEquals(list, carList))
            {
                carList = list;
                ((INotifyCollectionChanged)list.Items).CollectionChanged += (s, e) => { if (carActive) CarItemsChangedSoon(); };
                list.SelectionChanged += (s, e) => { if (carActive) CarSelectionSoon(); };
                list.IsVisibleChanged += (s, e) =>
                {
                    bool v = (bool)e.NewValue;
                    if (v == carListVisible) return;
                    carListVisible = v;
                    try { CarApplyShown(); } catch (Exception ex) { logger.Warn("Kippix: carousel: " + ex.Message); }
                };
                carListVisible = list.IsVisible;
            }
            var host = UiTools.FindByName(win, "KX_CarouselHost");
            if (host != null && !ReferenceEquals(host, carHost))
            {
                carHost = host;
                carCanvas = UiTools.FindByName(host, "KX_Carousel") as Canvas;
                carProxy = UiTools.FindByName(host, "KX_CarouselFocus") as ButtonBase;
                carTileTemplate = host.TryFindResource("KX_CarouselItemTemplate") as DataTemplate;
                carShown.Clear();
                carFree.Clear();
                if (carCanvas != null) carCanvas.Children.Clear();   // (a new host: nothing of ours in it yet)
                host.SizeChanged += (s, e) =>
                {
                    try { if (carActive) CarouselUpdate(); } catch (Exception ex) { logger.Warn("Kippix: carousel: " + ex.Message); }
                };
                host.PreviewMouseWheel += (s, e) =>
                {
                    if (!carActive || openPanel != null || detailsOpen) return;
                    e.Handled = true;
                    try { CarStep(e.Delta < 0 ? 1 : -1); } catch (Exception ex) { logger.Warn("Kippix: carousel wheel: " + ex.Message); }
                };
            }
            if (!carHomeTilesHooked && HomeTiles != null)
            {
                carHomeTilesHooked = true;
                HomeTiles.CollectionChanged += (s, e) =>
                {
                    if (!carActive || ui == null) return;
                    ui.BeginInvoke(new Action(() =>
                    {
                        try { if (carActive) { CarBuildExtras(); CarResync(); } }
                        catch (Exception ex) { logger.Warn("Kippix: carousel tiles: " + ex.Message); }
                    }), DispatcherPriority.Background);
                };
            }
        }

        private bool carItemsSoon;

        private void CarItemsChangedSoon()
        {
            if (carItemsSoon || ui == null) return;
            carItemsSoon = true;
            ui.BeginInvoke(new Action(() =>
            {
                carItemsSoon = false;
                try { if (carActive) CarResync(); } catch (Exception ex) { logger.Warn("Kippix: carousel: " + ex.Message); }
            }), DispatcherPriority.Background);
        }

        /// <summary>The tiles before the games, as carousel items (kept when they're the same tile, so their pictures stay).</summary>
        private void CarBuildExtras()
        {
            var keep = new Dictionary<string, CarouselItem>();
            foreach (var x in carExtras) keep[x.Key ?? ""] = x;
            carExtras.Clear();
            if (HomeTiles == null) return;
            foreach (var t in HomeTiles)
            {
                CarouselItem it;
                string key = t.Key ?? "";
                if (!keep.TryGetValue(key, out it) || !ReferenceEquals(it.Source, t)) it = CarItemFromTile(t);
                carExtras.Add(it);
            }
            if (carExtras.Count > 0) try { UpdateStoreTile(); } catch (Exception) { }
            if (FolderTilesShown) CarAddFolders(keep);   // Folder: the folder tiles, after the tiles before the games
        }

        private CarouselItem CarItemFromTile(HomeTile t)
        {
            var it = new CarouselItem
            {
                Kind = "tile", Key = t.Key, Name = t.Name, Sub = t.Sub, ShowDot = t.ShowDot, IsPinned = t.IsPinned, Source = t,
                Icon = t.IsStore ? "store" : t.IsAch ? "trophy" : t.IsPinned ? "pin" : t.IsUpNext ? "forward" : t.IsLibrary ? "grid" : "folder"
            };
            var tile = t;
            it.Activate = () => { if (tile.Command != null && tile.Command.CanExecute(null)) tile.Command.Execute(null); };
            if (t.IsPinned)
            {
                try
                {
                    var g = api.Database.Games.Get(t.GameId);
                    it.PictureFile = g != null ? CarCoverFile(g) : null;
                }
                catch (Exception) { }
                if (it.PictureFile == null) it.Picture = t.Cover;
            }
            var item = it;
            t.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Sub") item.Sub = tile.Sub;
                else if (e.PropertyName == "ShowDot") item.ShowDot = tile.ShowDot;
                else if (e.PropertyName == "Cover" && item.PictureFile == null) item.Picture = tile.Cover;
            };
            return it;
        }

        /// <summary>
        /// Folder, a folder showing duplicates: the carousel's games in its own order, each one's place in Playnite's list (a game
        /// in two of the folder's lists comes twice). Null: Playnite's list as it is.
        /// </summary>
        private List<int> carSeq;

        private int CarGameCount()
        {
            if (FolderTilesShown) return 0;   // (the folder tiles only)
            if (carSeq != null) return carSeq.Count;
            return carList != null ? carList.Items.Count : 0;
        }
        private int CarCount() { return carExtras.Count + CarGameCount(); }
        private object CarItemAt(int seq)
        {
            if (seq < carExtras.Count) return carExtras[seq];
            int g = CarListIndex(seq - carExtras.Count);
            return carList != null && g >= 0 && g < carList.Items.Count ? carList.Items[g] : null;
        }

        /// <summary>The carousel's game <paramref name="g"/> (from 0): its place in Playnite's list.</summary>
        private int CarListIndex(int g)
        {
            if (FolderTilesShown) return -1;
            if (carSeq == null) return g;
            return g >= 0 && g < carSeq.Count ? carSeq[g] : -1;
        }

        /// <summary>A game in Playnite's list: its place among the carousel's games (with duplicates, the one nearest the highlight).</summary>
        private int CarSeqOf(int li)
        {
            if (carSeq == null) return li;
            int here = carIndex - carExtras.Count, n = carSeq.Count;
            if (here >= 0 && here < n && carSeq[here] == li) return here;
            int best = -1, bestD = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (carSeq[i] != li) continue;
                int d = Math.Abs(i - here);
                if (CarLoop) d = Math.Min(d, Math.Abs(n - d));
                if (d < bestD) { bestD = d; best = i; }
            }
            return Math.Max(0, best);
        }

        /// <summary>Folder › Show duplicates: each of the open folder's lists in turn, its games in Playnite's order (made when the list changes).</summary>
        private void CarBuildSeq()
        {
            carSeq = null;
            var groups = FolderInside && settings.FolderDuplicates ? folderGroups : null;
            if (groups == null || groups.Count < 2 || carList == null) return;
            int n = carList.Items.Count;
            var ids = new Guid[n];
            for (int i = 0; i < n; i++) ids[i] = EntryId(carList.Items[i]);
            var seq = new List<int>(n + 16);
            foreach (var g in groups)
                for (int i = 0; i < n; i++)
                    if (g.Contains(ids[i])) seq.Add(i);
            if (seq.Count > n) carSeq = seq;   // (no game in two of its lists: Playnite's own order and moves)
        }

        private static int Mod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }
        /// <summary>
        /// With a loop the end of the list shows before the start (only with 3 or more: two would just repeat; Full Screen only
        /// with more than fit on screen, so nothing shows twice side by side).
        /// </summary>
        private bool CarRepeats(int n)
        {
            if (!CarLoop) return false;
            return CarDesign == 2 ? n > CarFullCount : n >= 3;
        }

        /// <summary>
        /// Full Screen: where the strip should stand with the highlight on <paramref name="v"/>: it stays put while the highlight
        /// moves across the panels, and scrolls once the highlight comes near an edge (a panel of look-ahead kept either side).
        /// </summary>
        private int CarViewFor(int v, int now)
        {
            int n = CarCount();
            return CarouselGeometry.FullViewFor(v, now, CarFullCount, n, CarRepeats(n), CarTilesTucked());
        }

        /// <summary>
        /// Full Screen (and Folder's tiles) with the tiles before the games on Auto show: how many there are, kept off to the left
        /// until Left from the first game brings them in, like the normal row's tiles (0: Always show, or none).
        /// </summary>
        private int CarTilesTucked()
        {
            if (CarDesign != 2 || AlwaysTiles) return 0;
            int m = 0;
            while (m < carExtras.Count && carExtras[m].Kind == "tile") m++;
            return m;
        }

        /// <summary>The place along the carousel for item <paramref name="seq"/> nearest to <paramref name="near"/> (with a loop, items repeat every n).</summary>
        private int CarNearestV(int seq, int near, int n)
        {
            if (!CarRepeats(n) || n <= 0) return seq;
            int k = (int)Math.Round((double)(near - seq) / n, MidpointRounding.AwayFromZero);
            return seq + k * n;
        }

        private bool CarProxyFocused() { return carProxy != null && carProxy.IsKeyboardFocused; }
        private bool CarHasFocus() { return carActive && ((carList != null && carList.IsKeyboardFocusWithin) || CarProxyFocused()); }

        /// <summary>After the games / tiles changed (or the carousel came on): the highlight where it should be, without a slide.</summary>
        private void CarResync()
        {
            CarBuildSeq();
            int n = CarCount();
            if (n == 0) { carIndex = 0; carTargetV = 0; carPos = 0; CarLayout(); CarLogLayout(); return; }
            int seq;
            if (CarProxyFocused() && carExtras.Count > 0) seq = Math.Min(carIndex, carExtras.Count - 1);
            else if (FolderTilesShown)
            {
                // Folder: the folder you came from (All games at first), and the highlight off Playnite's unseen row
                seq = FolderTileSeq();
                if (carList != null && carList.IsKeyboardFocusWithin) CarFolderFocusSoon();
            }
            else if (CarProxyFocused()) { seq = carExtras.Count + CarSeqOf(Math.Max(0, carList.SelectedIndex)); FocusSelectedCard(); }
            else seq = carExtras.Count + CarSeqOf(Math.Max(0, carList != null ? carList.SelectedIndex : 0));
            seq = Math.Max(0, Math.Min(n - 1, seq));
            carIndex = seq;
            carTargetV = seq;
            carPos = seq;
            carPosTween.Running = false;
            // (Full Screen on the first game with the tiles tucked away: the strip starts on it, the tiles off to its left)
            int tucked = CarTilesTucked();
            if (tucked > 0 && seq == tucked) carViewTarget = seq;
            carViewTarget = CarDesign == 2 ? CarViewFor(seq, carViewTarget) : 0;
            carView = carViewTarget;
            carViewTween.Running = false;
            CarFocusTo(CarHasFocus() ? 1 : 0);
            CarLayout();
            CarSettled();
            CarLogLayout();
        }

        private bool carSelectionSoon;

        /// <summary>(After Playnite has finished changing its list: the selection can change in the middle of that.)</summary>
        private void CarSelectionSoon()
        {
            if (carSelectionSoon || ui == null) return;
            carSelectionSoon = true;
            ui.BeginInvoke(new Action(() =>
            {
                carSelectionSoon = false;
                try { CarSelectionChanged(); } catch (Exception ex) { logger.Warn("Kippix: carousel: " + ex.Message); }
            }), DispatcherPriority.Normal);
        }

        /// <summary>Playnite's selected game changed: the carousel shows it (not while you're in the extra rows or on a tile).</summary>
        private void CarSelectionChanged()
        {
            if (!carActive || carList == null || extraRowAt > 0 || CarProxyFocused() || CarTileReturnPending()) return;
            int i = carList.SelectedIndex;
            if (i < 0 || FolderTilesShown) return;
            CarShowSeq(carExtras.Count + CarSeqOf(i), carList.IsKeyboardFocusWithin);
        }

        /// <summary>From the window's focus handler: the highlight moved onto a game in Playnite's row (or away from the carousel).</summary>
        private void CarouselFocusChanged(KeyboardFocusChangedEventArgs e)
        {
            if (!carActive) return;
            var nf = e.NewFocus as DependencyObject;
            bool inside = nf != null && (ReferenceEquals(nf, carProxy) || (carList != null && carList.IsAncestorOf(nf)));
            CarFocusTo(inside ? 1 : 0);
            if (nf == null || carList == null || ReferenceEquals(nf, carProxy) || !carList.IsAncestorOf(nf)) return;
            if (FolderTilesShown)
            {
                CarFolderFocusSoon();   // Folder's tiles: Playnite's unseen row never keeps the highlight (at start-up, back from a panel)
                return;
            }
            if (CarTileReturnPending()) return;
            carTileReturn = -1;   // a game has the highlight now: the tile is forgotten
            var c = ItemsControl.ContainerFromElement(carList, nf) as DependencyObject ?? nf;
            int idx = carList.ItemContainerGenerator.IndexFromContainer(c);
            if (idx < 0) return;
            int sel = carList.SelectedIndex;
            if (sel >= 0 && sel != idx)
            {
                // the highlight landed on another of Playnite's unseen cards than the selected game (Playnite putting it somewhere
                // after a card was taken away): back onto the selected game, and the carousel stays where it is
                CarRefocusSoon();
                return;
            }
            var old = e.OldFocus as DependencyObject;
            bool fromHere = old != null && (ReferenceEquals(old, carProxy) || carList.IsAncestorOf(old));
            CarShowSeq(carExtras.Count + CarSeqOf(idx), fromHere);
        }

        private bool carRefocusSoon;

        /// <summary>The highlight back onto the selected game's card in Playnite's unseen row (once, after the current change).</summary>
        private void CarRefocusSoon()
        {
            if (carRefocusSoon || ui == null) return;
            carRefocusSoon = true;
            ui.BeginInvoke(new Action(() =>
            {
                carRefocusSoon = false;
                try
                {
                    if (!carActive || carList == null || !carList.IsKeyboardFocusWithin || FolderTilesShown || openPanel != null || detailsOpen) return;
                    int sel = carList.SelectedIndex;
                    if (sel < 0) return;
                    var c = RealizeListItem(carList, sel, true);
                    if (c != null && !c.IsKeyboardFocused) Keyboard.Focus(c);
                    CarShowSeq(carExtras.Count + CarSeqOf(sel), false);
                }
                catch (Exception ex) { logger.Warn("Kippix: carousel: " + ex.Message); }
            }), DispatcherPriority.Input);
        }

        /// <summary>The highlight onto an item: slides there (a short way) or jumps (coming from somewhere else).</summary>
        private void CarShowSeq(int seq, bool animate)
        {
            int n = CarCount();
            if (n == 0 || seq < 0 || seq >= n) return;
            carIndex = seq;
            int v = CarNearestV(seq, carTargetV, n);
            if (v == carTargetV && (carPosTween.Running || Math.Abs(carPos - v) < 0.001)) return;   // already there, or on its way
            CarSetTarget(v, animate);
        }

        private void CarSetTarget(int v, bool animate)
        {
            carTargetV = v;
            bool full = CarDesign == 2;
            int vt = full ? CarViewFor(v, carViewTarget) : 0;
            carViewTarget = vt;
            if (!animate || ReduceMotionNow)
            {
                carPosTween.Running = false;
                carViewTween.Running = false;
                carPos = v;
                carView = vt;
                CarLayout();
                CarSettled();
                return;
            }
            // a long way: only the last step slides (Full Screen: the highlight jumps, the strip slides its last step)
            if (full ? Math.Abs(v - carPos) > 1.5 : Math.Abs(v - carPos) > 3.5) carPos = full ? v : v - Math.Sign(v - carPos);
            if (Math.Abs(v - carPos) > 0.0001) carPosTween.Go(carPos, v, 220); else carPosTween.Running = false;
            if (full && Math.Abs(vt - carView) > 0.0001)
            {
                if (Math.Abs(vt - carView) > CarFullCount) carView = vt - Math.Sign(vt - carView);
                carViewTween.Go(carView, vt, 220);
            }
            CarLayout();
            StartCarFrames();
        }

        // =====================================================================
        // Keys
        // =====================================================================

        /// <summary>
        /// Left / Right anywhere in the carousel: every step is the carousel's own, one item per press (Playnite's unseen row never
        /// moves by itself: its own moves, scrolled out of sight with smooth scrolling, could land several games on, or somewhere
        /// the carousel wasn't). Down from a tile goes nowhere. True when handled.
        /// </summary>
        private bool CarouselKey(KeyEventArgs e)
        {
            if (!carActive || confirmOpen || freeMove || IsMasterMoving || carList == null) return false;
            bool onProxy = CarProxyFocused();
            bool inList = carList.IsKeyboardFocusWithin;
            if (!onProxy && !inList) return CarDownFromTopBar(e);
            bool sideways = e.Key == Key.Left || e.Key == Key.Right;
            // a pad button let go (Playnite sends that as a key too): no second step, and never through to the unseen row
            if (IsPadRelease(e)) return sideways || (e.Key == Key.Down && (onProxy || FolderTilesShown));
            if (inList && FolderTilesShown && (sideways || e.Key == Key.Down))
            {
                if (carProxy != null) Keyboard.Focus(carProxy);   // (Folder's tiles: never Playnite's unseen row)
                return true;
            }
            if (onProxy && carExtras.Count == 0) { FocusSelectedCard(); return true; }   // (its tile has gone)
            if (onProxy && e.Key == Key.Down) return true;   // nothing below the tiles
            if (!sideways) return false;
            int dir = e.Key == Key.Right ? 1 : -1;
            int n = CarCount(), m = carExtras.Count;
            if (n == 0) return true;
            int cur;
            if (onProxy) cur = Math.Max(0, Math.Min(carIndex, m - 1));
            else
            {
                // on the games: where the carousel is, unless Playnite has selected another game since (then from that one)
                int sel = carList.SelectedIndex;
                bool known = carIndex >= m && carIndex < n;
                if (known && (sel < 0 || CarListIndex(carIndex - m) == sel)) cur = carIndex;
                else if (sel >= 0) cur = m + CarSeqOf(sel);
                else cur = known ? carIndex : m;
            }
            // a held button: a steady pace (and a press that somehow came twice moves once)
            if ((DateTime.Now - carLastMove).TotalMilliseconds < (e.IsRepeat ? 150 : 70)) return true;
            int next = cur + dir;
            if (next < 0 || next >= n)
            {
                if (!CarLoop || n < 2) return true;   // the end: stay
                next = Mod(next, n);
            }
            CarGoTo(next, dir);
            return true;
        }

        /// <summary>
        /// Down from the top bar: onto the carousel's highlighted item (WPF's own guess would land on one of Playnite's unseen
        /// cards, wherever it happens to be). A widget in between still gets it.
        /// </summary>
        private bool CarDownFromTopBar(KeyEventArgs e)
        {
            if (e.Key != Key.Down || IsPadRelease(e)) return false;
            var focused = Keyboard.FocusedElement as UIElement;
            var bar = UiTools.FindByName(Application.Current.MainWindow, "KX_TopBar");
            if (focused == null || bar == null || !bar.IsAncestorOf(focused)) return false;
            var next = focused.PredictFocus(FocusNavigationDirection.Down) as DependencyObject;
            if (next != null && !carList.IsAncestorOf(next) && !ReferenceEquals(next, carProxy)) return false;
            if (CarCount() == 0) return false;
            PlaySound("move");
            if (carIndex < carExtras.Count && carProxy != null) Keyboard.Focus(carProxy);
            else FocusSelectedCard();
            return true;
        }

        /// <summary>One step along (the mouse wheel).</summary>
        private void CarStep(int dir)
        {
            int n = CarCount();
            if (n == 0 || (DateTime.Now - carLastMove).TotalMilliseconds < 150) return;
            int next = carIndex + dir;
            if (next < 0 || next >= n)
            {
                if (!CarLoop || n < 2) return;
                next = Mod(next, n);
            }
            CarGoTo(next, dir);
        }

        /// <summary>The highlight onto item <paramref name="seq"/>, one step that way (the carousel slides one place).</summary>
        private void CarGoTo(int seq, int dir)
        {
            int n = CarCount();
            if (n == 0) return;
            carLastMove = DateTime.Now;
            carTileReturn = -1;
            carIndex = seq;
            if (FolderTilesShown) FolderNoteHighlight(seq);
            PlaySound("move");
            // with a loop: the copy of the item one step along from where the carousel stands (so a wrap slides one place too)
            CarSetTarget(CarNearestV(seq, carTargetV + dir, n), true);
            if (seq < carExtras.Count)
            {
                if (carProxy != null && !carProxy.IsKeyboardFocused) Keyboard.Focus(carProxy);
                return;
            }
            CarFocusGame(CarListIndex(seq - carExtras.Count));
        }

        /// <summary>
        /// Playnite's row onto a game (selected and highlighted). Its unseen row is scrolled to the game every time, so its card
        /// is made and stays made (a card focused off the edge of Playnite's row could be taken away again, and the highlight with it).
        /// </summary>
        private void CarFocusGame(int g)
        {
            if (carList == null || g < 0 || g >= carList.Items.Count) return;
            var entry = carList.Items[g];
            if (!ReferenceEquals(carList.SelectedItem, entry)) carList.SelectedItem = entry;
            var c = RealizeListItem(carList, g, true);
            if (c != null) Keyboard.Focus(c);
            else if (!carList.IsKeyboardFocusWithin) carList.Focus();   // (the card isn't made: the row itself, so the keys still come here)
        }

        private static MethodInfo scrollToItem;

        /// <summary>
        /// The card for item <paramref name="index"/> in Playnite's row, scrolling the row to it when it isn't made yet (or always,
        /// with <paramref name="scroll"/>).
        /// </summary>
        private static UIElement RealizeListItem(ListBox list, int index, bool scroll = false)
        {
            if (list == null || index < 0 || index >= list.Items.Count) return null;
            var c = list.ItemContainerGenerator.ContainerFromIndex(index) as UIElement;
            if (c != null && !scroll) return c;
            try
            {
                var panel = UiTools.FindChild<VirtualizingPanel>(list);
                if (panel != null)
                {
                    var m = scrollToItem != null && scrollToItem.DeclaringType == panel.GetType() ? scrollToItem : panel.GetType().GetMethod("ScrollToItem", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null) { scrollToItem = m; m.Invoke(panel, new[] { list.Items[index] }); }
                    else
                    {
                        var si = panel as IScrollInfo;
                        if (si != null) si.SetHorizontalOffset(si.ExtentWidth * index / Math.Max(1, list.Items.Count));
                    }
                }
                list.UpdateLayout();
            }
            catch (Exception) { }
            return list.ItemContainerGenerator.ContainerFromIndex(index) as UIElement ?? c;
        }

        // =====================================================================
        // A on a tile (and coming back to it)
        // =====================================================================

        private ICommand carActivateCmd;
        /// <summary>A on the highlighted item that isn't a game (the unseen button KX_CarouselFocus has the highlight).</summary>
        public ICommand CarouselActivateCommand
        {
            get
            {
                return carActivateCmd ?? (carActivateCmd = new RelayCommand(_ =>
                {
                    if (!carActive || carIndex < 0 || carIndex >= carExtras.Count) return;
                    var it = carExtras[carIndex];
                    if (it.Activate == null) { PlaySound("back"); return; }
                    carTileReturn = carIndex;
                    it.Activate();
                }));
            }
        }

        /// <summary>A tile opened something (a game page, a panel): until that's closed, the carousel stays on the tile.</summary>
        private bool CarTileReturnPending()
        {
            if (carTileReturn < 0) return false;
            return detailsOpen || openPanel != null || (DateTime.Now - carDetailsClosedAt).TotalMilliseconds < 800;
        }

        /// <summary>The game page closed after a pinned game's tile opened it: the highlight goes back to that tile.</summary>
        private bool CarouselBackToTile()
        {
            if (!carActive || carTileReturn < 0) return false;
            int t = carTileReturn;
            carTileReturn = -1;
            if (t >= carExtras.Count || carProxy == null) return false;
            carIndex = t;
            CarShowSeq(t, false);
            Keyboard.Focus(carProxy);
            return true;
        }

        private void CarouselDetailsClosed() { carDetailsClosedAt = DateTime.Now; }

        /// <summary>"Back to the games" while the carousel's highlight is on something that isn't a game: back on that item.</summary>
        private bool CarouselBackToItem()
        {
            if (!carActive || carProxy == null || carIndex < 0 || carIndex >= carExtras.Count || !carProxy.IsVisible) return false;
            if (!carProxy.IsKeyboardFocused) Keyboard.Focus(carProxy);
            return carProxy.IsKeyboardFocused;
        }

        // =====================================================================
        // Mouse: a click on a card highlights it; a click on the highlighted one opens it
        // =====================================================================

        private void CarClicked(CarSlot s)
        {
            if (s == null || !carActive || openPanel != null || detailsOpen) return;
            int n = CarCount();
            if (n == 0) return;
            int seq = Mod(s.V, n);
            if (s.V != carTargetV)
            {
                carLastMove = DateTime.Now;
                carTileReturn = -1;
                carIndex = seq;
                if (FolderTilesShown) FolderNoteHighlight(seq);
                PlaySound("move");
                CarSetTarget(CarRepeats(n) ? s.V : seq, true);
                if (seq < carExtras.Count) { if (carProxy != null) Keyboard.Focus(carProxy); }
                else CarFocusGame(CarListIndex(seq - carExtras.Count));
                return;
            }
            if (seq < carExtras.Count) { CarouselActivateCommand.Execute(null); return; }
            CarFocusGame(CarListIndex(seq - carExtras.Count));
            PlaySound("select");
            if (settings.SkipGamePage) { PausePreviewFor(10); RunMainCommand("ActivateSelectedCommand"); return; }
            RunMainCommand("ToggleGameDetailsCommand");
        }

        // =====================================================================
        // Geometry (the numbers come from CarouselGeometry)
        // =====================================================================

        private static Type carPanelType;
        private static FieldInfo carFieldW, carFieldH, carFieldC;

        /// <summary>Playnite's row's card size and where its cards sit in it (Lower draws its cards exactly there).</summary>
        private bool CarRowGeometry(out double w, out double h, out double top)
        {
            w = h = top = 0;
            if (carList == null) return false;
            try
            {
                var panel = UiTools.FindChild<VirtualizingPanel>(carList);
                if (panel == null) return false;
                var t = panel.GetType();
                if (t != carPanelType)
                {
                    carPanelType = t;
                    carFieldW = t.GetField("itemWidth", BindingFlags.NonPublic | BindingFlags.Instance);
                    carFieldH = t.GetField("itemHeight", BindingFlags.NonPublic | BindingFlags.Instance);
                    carFieldC = t.GetField("centerMargin", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (carFieldW != null && carFieldH != null)
                {
                    // (Playnite's own fields: if they aren't numbers in this version, a card on screen below instead)
                    try
                    {
                        w = Convert.ToDouble(carFieldW.GetValue(panel));
                        h = Convert.ToDouble(carFieldH.GetValue(panel));
                        top = carFieldC != null ? Convert.ToDouble(carFieldC.GetValue(panel)) : 0;
                    }
                    catch (Exception) { w = h = top = 0; }
                    if (double.IsNaN(w) || double.IsNaN(h) || double.IsInfinity(w) || double.IsInfinity(h)) w = h = 0;
                    if (double.IsNaN(top) || double.IsInfinity(top)) top = 0;
                }
                if (w <= 0 || h <= 0)
                {
                    // (a card on screen instead)
                    for (int i = 0; i < Math.Min(carList.Items.Count, 12); i++)
                    {
                        var c = carList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                        if (c == null || c.ActualWidth <= 0) continue;
                        w = c.ActualWidth; h = c.ActualHeight;
                        top = Math.Max(0, (carList.ActualHeight - h) / 2);
                        break;
                    }
                }
            }
            catch (Exception) { return false; }
            return w > 0 && h > 0;
        }

        /// <summary>
        /// Lower: Playnite's row's card size and its cards' top-left in the host. The host and Playnite's row share a parent,
        /// so their layout offsets (not their moves: Free move and the extra rows slide them with transforms) give the place.
        /// </summary>
        private bool CarLowerRow(out double w, out double h, out double left, out double top)
        {
            left = top = 0;
            double inside;
            if (!CarRowGeometry(out w, out h, out inside) || carList == null || carHost == null) return false;
            var lo = VisualTreeHelper.GetOffset(carList);
            var ho = VisualTreeHelper.GetOffset(carHost);
            if (carList.ActualHeight > 0) { left = lo.X - ho.X; top = lo.Y - ho.Y + inside; }
            else
            {
                // (Playnite's row not laid out yet: where its margin puts it, at the bottom of the screen)
                var m = CardsMarginFor(0);
                left = m.Left;
                top = carHost.ActualHeight - m.Bottom - cardsHeight + inside;
            }
            return true;
        }

        /// <summary>
        /// Works out the cards' size for the design and the host's size now (true when anything changed). Nothing is drawn
        /// while the host has no size yet.
        /// </summary>
        private bool CarMeasure()
        {
            if (carHost == null) return false;
            double hw = carHost.ActualWidth, hh = carHost.ActualHeight;
            int design = CarDesign;
            double rw = 0, rh = 0, rl = 0, rt = 0;
            if (design == 0 && !CarLowerRow(out rw, out rh, out rl, out rt)) return false;
            // (0.89) Row › Full Screen reaches the very top (the top bar shows over it while it has the highlight); Tall: its count and size
            bool rowFull = design == 2 && !FolderTilesShown;
            double top = design == 0 || rowFull ? 0 : carTopY;
            int count = design == 1 ? Math.Max(1, Math.Min(10, settings.RowTallCount)) : CarFullCount;
            double sw = 1, sh = design == 1 ? settings.CarTallFill : rowFull ? settings.CarFullH : 1;   // (0.90) Tall: its own height (0: 2:3)
            var f = CarouselGeometry.Measure(design, hw, hh, top, design == 1 ? carBottomInset : 0, count, rw, rh, rl, rt, sw, sh, design == 1 ? CarTallAspect : 0);
            if (f == null) return false;
            string sig = f.Signature;
            carFrame = f;
            if (sig == carGeoSig) return false;
            carGeoSig = sig;
            if (design == 2)
            {
                // (a different number on screen: the strip stands where the highlight can be seen, look-ahead and all; with fewer
                // items than panels now, nothing repeats any more, so the highlight comes back to the item's own place)
                int n = CarCount();
                if (n > 0 && !CarRepeats(n) && (carTargetV < 0 || carTargetV >= n))
                {
                    carTargetV = Mod(carTargetV, n);
                    carPos = carTargetV;
                    carPosTween.Running = false;
                }
                carViewTarget = CarViewFor(carTargetV, carViewTarget);
                carView = carViewTarget;
                carViewTween.Running = false;
            }
            foreach (var s in carShown.Values) { s.Forget(); s.Dressed = false; }
            return true;
        }

        /// <summary>From ApplyHomeLayout and the host's size changing: the cards' size, and everything laid out again if that changed.</summary>
        private void CarouselUpdate()
        {
            if (!carActive) return;
            try
            {
                if (CarMeasure()) { CarLayout(); CarSettled(); CarDressSoon(); }
                else if (carShown.Count == 0 && CarCount() > 0) { CarLayout(); CarSettled(); }
                CarLogLayout();
            }
            catch (Exception ex) { logger.Warn("Kippix: carousel layout: " + ex.Message); }
        }

        /// <summary>One line in the log whenever the carousel is laid out with different numbers (not every frame).</summary>
        private void CarLogLayout()
        {
            var f = carFrame;
            if (!carActive || f == null) return;
            int n = CarCount();
            var sel = CarouselGeometry.Slot(f, carTargetV, carTargetV, carViewTarget, 0, 0);
            double firstX = double.NaN;
            int from, to;
            CarouselGeometry.Range(f, carTargetV, carViewTarget, out from, out to);
            for (int v = from; v <= to; v++)
            {
                var r = CarouselGeometry.Slot(f, v, carTargetV, carViewTarget, 0, 0);
                if (r.X + r.W > 0) { firstX = r.X; break; }
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string[] designs = { "Lower", "Tall", "Full Screen" };
            string line = string.Format(inv,
                "{0}: host {1:0}x{2:0}, topY {3:0}, bottom kept {4:0}, {5} items ({6} tiles), {7} | slot {8:0.#}x{9:0.#}, gap {10:0.#}, top {11:0.#}, first slot x {12:0.#}, highlighted {13:0.#},{14:0.#} {15:0.#}x{16:0.#}",
                FolderTilesShown ? "Folder tiles" : designs[Math.Max(0, Math.Min(2, f.Design))], f.HostW, f.HostH, f.TopY, f.BottomInset, n, carExtras.Count,
                f.Design == 2 ? f.Count + " across" : "highlight in " + (f.Design == 1 ? "the middle" : "the first place"),
                f.W, f.H, f.Gap, f.Top, firstX, sel.X, sel.Y, sel.W, sel.H);
            // (written again only when the numbers change, not when the highlight just moves to another panel)
            string key = line.Substring(0, Math.Max(0, line.IndexOf(", highlighted", StringComparison.Ordinal)));
            if (key == carLayoutLogged) return;
            carLayoutLogged = key;
            logger.Info("Kippix: carousel " + line);
        }

        // =====================================================================
        // The cards on screen
        // =====================================================================

        private void CarClear()
        {
            foreach (var s in carShown.Values.Concat(carFree)) if (carCanvas != null) carCanvas.Children.Remove(s.El);
            carShown.Clear();
            carFree.Clear();
            carGeoSig = null;
            carFrame = null;
            carLayoutLogged = null;
            carGameItems.Clear();
            carGameByEntry.Clear();
        }

        // Tall / Full Screen: each game as an item for the theme's template (kept while it's around, so its picture stays), found
        // by Playnite's entry (no lookups each frame) or by the game's id
        private readonly Dictionary<Guid, CarouselItem> carGameItems = new Dictionary<Guid, CarouselItem>();
        private readonly Dictionary<object, CarouselItem> carGameByEntry = new Dictionary<object, CarouselItem>(CarSameObject.Instance);

        private sealed class CarSameObject : IEqualityComparer<object>
        {
            public static readonly CarSameObject Instance = new CarSameObject();
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        /// <summary>A game from Playnite's list as a carousel item: its name, its cover (else its background) and the installed tick.</summary>
        private CarouselItem CarGameItem(object entry)
        {
            if (entry == null) return null;
            CarouselItem it;
            if (carGameByEntry.TryGetValue(entry, out it)) return it;
            Guid id = EntryId(entry);
            if (id == Guid.Empty) return null;
            if (carGameItems.Count > 300 || carGameByEntry.Count > 300)
            {
                // (a long scroll: only the ones on screen are kept)
                var keep = new HashSet<object>(carShown.Values.Select(x => x.Content));
                foreach (var k in carGameItems.Where(kv => !keep.Contains(kv.Value)).Select(kv => kv.Key).ToList()) carGameItems.Remove(k);
                foreach (var k in carGameByEntry.Where(kv => !keep.Contains(kv.Value)).Select(kv => kv.Key).ToList()) carGameByEntry.Remove(k);
            }
            if (carGameItems.TryGetValue(id, out it)) { it.Source = entry; carGameByEntry[entry] = it; return it; }
            Game g = null;
            try { g = api.Database.Games.Get(id); } catch (Exception) { }
            it = new CarouselItem { Kind = "game", Key = "game:" + id, Icon = "", Source = entry, Name = g != null ? g.Name : "" };
            string cover = CarCoverFile(g), back = CarBackgroundFile(g);
            it.CoverFile = cover;
            it.BackFile = back;
            if (cover != null) { it.PictureFile = cover; it.AltPictureFile = back; }
            else { it.PictureFile = back; it.PictureWide = true; }
            it.ShowInstalled = settings.BadgeInstalled && g != null && g.IsInstalled;
            carGameItems[id] = it;
            carGameByEntry[entry] = it;
            return it;
        }

        private CarSlot CarTake(object item, int v, bool card)
        {
            CarSlot s = null;
            int best = -1;
            for (int i = 0; i < carFree.Count; i++)
            {
                if (carFree[i].Card != card) continue;
                best = i;
                if (ReferenceEquals(carFree[i].Content, item)) break;   // the same item again: nothing to load
            }
            if (best >= 0) { s = carFree[best]; carFree.RemoveAt(best); }
            if (s == null) s = CarMake(card);
            if (s == null) return null;
            s.V = v;
            if (!ReferenceEquals(s.Content, item))
            {
                s.Content = item;
                if (card) ((ListBoxItem)s.El).Content = item; else ((ContentPresenter)s.El).Content = item;
                s.Dressed = false;
                s.DressTries = 0;
                s.ArtKey = null;
            }
            s.El.Visibility = Visibility.Visible;
            carShown[v] = s;
            return s;
        }

        private CarSlot CarMake(bool card)
        {
            if (carCanvas == null) return null;
            FrameworkElement el;
            if (card)
            {
                if (carList == null || carList.ItemTemplate == null) return null;
                // Playnite's own card (Lower: the theme's card look, its cover, badges, shape) outside Playnite's row
                var lbi = new ListBoxItem { Focusable = false, IsTabStop = false };
                lbi.Style = carList.ItemContainerStyle;
                lbi.ContentTemplate = carList.ItemTemplate;
                el = lbi;
            }
            else
            {
                if (carTileTemplate == null) return null;
                el = new ContentPresenter { ContentTemplate = carTileTemplate };
            }
            var s = new CarSlot { El = el, Card = card };
            el.Tag = s;
            el.HorizontalAlignment = HorizontalAlignment.Left;
            el.VerticalAlignment = VerticalAlignment.Top;
            el.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((o, e) =>
            {
                try { CarClicked(((FrameworkElement)o).Tag as CarSlot); } catch (Exception ex) { logger.Warn("Kippix: carousel click: " + ex.Message); }
            }), true);
            // (a size before it's placed: never its picture's own size)
            el.Width = carFrame != null ? carFrame.W : 1;
            el.Height = carFrame != null ? carFrame.H : 1;
            Canvas.SetLeft(el, -10000);
            Canvas.SetTop(el, 0);
            carCanvas.Children.Add(el);
            return s;
        }

        private void CarRelease(CarSlot s)
        {
            s.El.Visibility = Visibility.Collapsed;
            carFree.Add(s);
            if (carFree.Count > 24)
            {
                var old = carFree[0];
                carFree.RemoveAt(0);
                if (carCanvas != null) carCanvas.Children.Remove(old.El);
            }
        }

        /// <summary>Places every card on screen for the carousel's position now (each frame while it moves, once when it stops).</summary>
        private void CarLayout()
        {
            if (!carActive || carCanvas == null) return;
            int n = CarCount();
            var f = carFrame;
            if (n == 0 || f == null)
            {
                foreach (var s in carShown.Values) CarRelease(s);
                carShown.Clear();
                return;
            }
            bool repeat = CarRepeats(n);
            int from, to;
            CarouselGeometry.Range(f, carPos, carView, out from, out to);
            if (!repeat) { from = Math.Max(from, 0); to = Math.Min(to, n - 1); }
            carDrop.Clear();
            foreach (var kv in carShown) if (kv.Key < from || kv.Key > to) carDrop.Add(kv.Key);
            bool added = false;
            foreach (int v in carDrop) { CarRelease(carShown[v]); carShown.Remove(v); }
            for (int v = from; v <= to; v++)
            {
                object item = CarItemAt(repeat ? Mod(v, n) : v);
                if (item == null) continue;
                // Lower's games: Playnite's card; everything else (and every game in Tall / Full Screen): the theme's item
                bool card = f.Design == 0 && !(item is CarouselItem);
                if (!card && !(item is CarouselItem)) item = CarGameItem(item);
                if (item == null) continue;
                CarSlot s;
                if (carShown.TryGetValue(v, out s) && (!ReferenceEquals(s.Content, item) || s.Card != card)) { CarRelease(s); carShown.Remove(v); s = null; }
                if (s == null) { s = CarTake(item, v, card); added = true; }
                if (s == null) continue;
                CarPlace(s, v);
            }
            if (added) CarDressSoon();
        }

        /// <summary>A value differs from the one last set (NaN: never set).</summary>
        private static bool CarDiffers(double now, double was, double by) { return double.IsNaN(was) || Math.Abs(now - was) > by; }

        /// <summary>One card at its place <paramref name="v"/>: its rectangle from CarouselGeometry, set as Canvas.Left / Top and Width / Height.</summary>
        private void CarPlace(CarSlot s, int v)
        {
            var f = carFrame;
            if (f == null) return;
            if (s.Ring == null) CarFindParts(s);
            var r = CarouselGeometry.Slot(f, v, carPos, carView, carShiftL.Value, carShiftR.Value, carFocus.Value);
            if (CarDiffers(r.X, s.X, 0.05)) { s.X = r.X; Canvas.SetLeft(s.El, Math.Round(r.X * 2) / 2); }
            if (CarDiffers(r.Y, s.Y, 0.05)) { s.Y = r.Y; Canvas.SetTop(s.El, Math.Round(r.Y * 2) / 2); }
            if (CarDiffers(r.W, s.W, 0.05)) { s.W = r.W; s.El.Width = Math.Max(1, r.W); }
            if (CarDiffers(r.H, s.H, 0.05)) { s.H = r.H; s.El.Height = Math.Max(1, r.H); }
            double ring = r.Near * carFocus.Value;
            if (s.Ring != null && CarDiffers(ring, s.R, 0.005)) { s.R = ring; s.Ring.Opacity = ring; }
            double ad = Math.Abs(v - carPos);
            int z = f.Design == 0 ? (ad < 0.5 ? 90 : 20 - (int)Math.Min(19, ad)) : 100 - (int)Math.Min(99, Math.Round(ad * 10));
            if (z != s.Z) { s.Z = z; Panel.SetZIndex(s.El, z); }
        }

        /// <summary>The parts of a card the carousel uses: its highlight edge, and the card itself (for the trailer preview).</summary>
        private void CarFindParts(CarSlot s)
        {
            try
            {
                if (s.Card)
                {
                    var lbi = (ListBoxItem)s.El;
                    lbi.ApplyTemplate();
                    if (lbi.Template != null)
                    {
                        s.Ring = lbi.Template.FindName("FocusRing", lbi) as UIElement;
                        s.Tile = lbi.Template.FindName("Tile", lbi) as FrameworkElement;
                    }
                }
                else
                {
                    var cp = (ContentPresenter)s.El;
                    cp.ApplyTemplate();
                    if (carTileTemplate != null && VisualTreeHelper.GetChildrenCount(cp) > 0)
                    {
                        s.Ring = carTileTemplate.FindName("KX_CarRing", cp) as UIElement;
                        s.Tile = carTileTemplate.FindName("KX_CarTile", cp) as FrameworkElement;
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>The carousel has stopped: the unseen button sits on the highlighted card (Up / widgets measure from it).</summary>
        private void CarSettled()
        {
            if (carProxy == null) return;
            var r = CarHighlightRect();
            if (r.IsEmpty) return;
            var m = new Thickness(Math.Round(r.X + r.Width / 2), Math.Round(r.Y + r.Height / 2), 0, 0);
            if (carProxy.Margin != m) carProxy.Margin = m;
        }

        /// <summary>The highlighted card, in the host's space (where it ends up); empty before the host has a size.</summary>
        private Rect CarHighlightRect()
        {
            var f = carFrame;
            if (f == null) return Rect.Empty;
            var r = CarouselGeometry.Slot(f, carTargetV, carTargetV, carViewTarget, 0, 0);
            return new Rect(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H));
        }

        /// <summary>The highlighted card on screen (for Up to the top bar and the widgets); empty when there's none.</summary>
        private Rect CarouselHighlightBounds(Visual to)
        {
            try
            {
                if (!carActive || carHost == null || !carHost.IsVisible || to == null) return Rect.Empty;
                var r = CarHighlightRect();
                if (r.IsEmpty) return Rect.Empty;
                return carHost.TransformToVisual(to).TransformBounds(r);
            }
            catch (Exception) { return Rect.Empty; }
        }

        /// <summary>
        /// Free move's outline round the carousel: the cards it has on screen now (in KX_Carousel's space), not the host that fills
        /// the screen. Empty when it shows none.
        /// </summary>
        private Rect CarouselDrawnRect()
        {
            var box = Rect.Empty;
            if (!carActive || carCanvas == null) return box;
            foreach (var s in carShown.Values)
            {
                var el = s.El;
                if (el == null || el.Visibility != Visibility.Visible) continue;
                double x = Canvas.GetLeft(el), y = Canvas.GetTop(el);
                double w = !double.IsNaN(el.Width) ? el.Width : el.ActualWidth, h = !double.IsNaN(el.Height) ? el.Height : el.ActualHeight;   // (the size it was given)
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0 || x < -5000) continue;
                box.Union(new Rect(x, y, w, h));
            }
            return box;
        }

        /// <summary>The highlighted card when it's a game (the trailer preview sits over it).</summary>
        private FrameworkElement CarouselPreviewTile()
        {
            CarSlot s;
            if (!carActive || !carShown.TryGetValue(carTargetV, out s)) return null;
            var it = s.Content as CarouselItem;
            if (!s.Card && (it == null || it.Kind != "game")) return null;
            return s.Tile ?? s.El;
        }

        // =====================================================================
        // Moving: short tweens, only while something moves
        // =====================================================================

        private class CarTween
        {
            public double From, To, Ms, Value;
            public long Start;
            public bool Running;
            public void Go(double from, double to, double ms)
            {
                From = from; To = to; Ms = ms; Start = carClock.ElapsedMilliseconds; Running = true; Value = from;
            }
            /// <summary>Moves on; false once it has arrived.</summary>
            public bool Step()
            {
                if (!Running) return false;
                double t = Ms <= 0 ? 1 : (carClock.ElapsedMilliseconds - Start) / Ms;
                if (t >= 1) { Value = To; Running = false; return false; }
                double u = 1 - t;
                Value = From + (To - From) * (1 - u * u * u);   // eases out
                return true;
            }
        }

        private static readonly Stopwatch carClock = Stopwatch.StartNew();
        private readonly CarTween carPosTween = new CarTween();
        private readonly CarTween carViewTween = new CarTween();
        private readonly CarTween carFocus = new CarTween();
        private readonly CarTween carShiftL = new CarTween(), carShiftR = new CarTween();
        private bool carTicking;

        private void StartCarFrames()
        {
            if (carTicking) return;
            carTicking = true;
            CompositionTarget.Rendering += OnCarFrame;
        }

        private void StopCarFrames()
        {
            if (!carTicking) return;
            carTicking = false;
            CompositionTarget.Rendering -= OnCarFrame;
        }

        private void OnCarFrame(object sender, EventArgs e)
        {
            try
            {
                if (carPosTween.Running) { carPosTween.Step(); carPos = carPosTween.Value; }
                if (carViewTween.Running) { carViewTween.Step(); carView = carViewTween.Value; }
                carFocus.Step();
                carShiftL.Step();
                carShiftR.Step();
                CarLayout();
                if (!carPosTween.Running && !carViewTween.Running && !carFocus.Running && !carShiftL.Running && !carShiftR.Running)
                {
                    StopCarFrames();
                    CarSettled();
                    CarDressSoon();
                }
            }
            catch (Exception ex) { StopCarFrames(); logger.Warn("Kippix: carousel frame: " + ex.Message); }
        }

        /// <summary>The highlight edge on (the carousel has the highlight) or off, faded.</summary>
        private void CarFocusTo(double to)
        {
            double target = carFocus.Running ? carFocus.To : carFocus.Value;
            if (Math.Abs(target - to) < 0.001) return;
            if (ReduceMotionNow) { carFocus.Running = false; carFocus.Value = to; CarLayout(); return; }
            carFocus.Go(carFocus.Value, to, 180);
            StartCarFrames();
        }

        /// <summary>Lower: the trailer preview in the card pushes the cards either side out of its way (0, 0: back).</summary>
        private void CarouselShift(double left, double right, bool animate)
        {
            if (!carActive) return;
            if (ReduceMotionNow) { left = right = 0; animate = false; }
            double tl = carShiftL.Running ? carShiftL.To : carShiftL.Value, tr = carShiftR.Running ? carShiftR.To : carShiftR.Value;
            if (Math.Abs(tl - left) < 2 && Math.Abs(tr - right) < 2) return;
            if (!animate) { carShiftL.Running = carShiftR.Running = false; carShiftL.Value = left; carShiftR.Value = right; CarLayout(); return; }
            carShiftL.Go(carShiftL.Value, left, 320);
            carShiftR.Go(carShiftR.Value, right, 320);
            StartCarFrames();
        }

        // =====================================================================
        // Dressing a card: Lower's Playnite card (shape, gap, badges, wide art); the theme's items (corners, picture)
        // =====================================================================

        private bool carDressSoon;
        private string carLookSig;

        /// <summary>Cards that are new on screen (or everything, when the cards' look changed) get dressed after the next layout.</summary>
        private void CarDressSoon()
        {
            if (carDressSoon || ui == null || !carActive) return;
            carDressSoon = true;
            ui.BeginInvoke(new Action(() =>
            {
                carDressSoon = false;
                try { CarDressAll(); } catch (Exception ex) { logger.Warn("Kippix: carousel cards: " + ex.Message); }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>What the cards' look depends on: when it changes, every card on screen is dressed again.</summary>
        private string CarLookNow()
        {
            int hltb;
            lock (hltbBadges) hltb = hltbBadges.Count;
            int wide;
            lock (wideArt) wide = wideArt.Count;
            return string.Join("|", new object[]
            {
                CarDesign, settings.CardShape, settings.TriArrange, settings.TriSpacing, settings.CardSpacing, measuredSpacing, settings.BadgeInstalled,
                settings.BadgeNew, settings.BadgeAch, settings.BadgeHltb, settings.SlotPinBadge, settings.SlotPins != null ? string.Join(",", settings.SlotPins.Select(p => p.GameId + p.Slot).ToArray()) : "",
                achData != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(achData) : 0, hltb, wide, carGeoSig
            }.Select(o => o != null ? o.ToString() : "").ToArray());
        }

        private void CarDressAll()
        {
            if (!carActive) return;
            string look = CarLookNow();
            bool all = look != carLookSig;
            carLookSig = look;
            bool again = false;
            lock (carWanted) carWanted.Clear();
            foreach (var s in carShown.Values)
            {
                if (s.ArtKey != null) lock (carWanted) carWanted.Add(s.ArtKey);
                if (s.Dressed && !all) continue;
                bool done = s.Card ? CarDressGame(s) : CarDressItem(s);
                if (done) s.Dressed = true;
                else if (++s.DressTries < 4) again = true;   // its parts aren't made yet: after the next layout
                if (s.ArtKey != null) lock (carWanted) carWanted.Add(s.ArtKey);
            }
            if (again) CarDressSoon();
        }

        /// <summary>Every card on screen dressed again (an item's picture changed).</summary>
        private void CarRedress()
        {
            foreach (var s in carShown.Values) s.Dressed = false;
            CarDressSoon();
        }

        /// <summary>Lower with Loop: Playnite's card, dressed like the cards in Playnite's row (Card look applies).</summary>
        private bool CarDressGame(CarSlot s)
        {
            var c = s.El;
            if (UiTools.FindByName(c, "Root") == null) return false;
            if (s.Ring == null) CarFindParts(s);
            object entry = s.Content;
            Guid id = EntryId(entry);
            Game g = null;
            if (id != Guid.Empty) try { g = api.Database.Games.Get(id); } catch (Exception) { }
            var sp = UiTools.FindByName(c, "KX_CardSpacing") as FrameworkElement;
            if (sp != null) CarSpacing(sp);
            // its shape follows Card look (triangles take turns along the carousel)
            int shape = ShapesOn ? (CardShapes.IsTriangle(settings.CardShape) ? TriShapeAt(s.V) : settings.CardShape) : -1;
            ShapeCard(c, shape, false);
            // the wide picture (Wide and Triangle shapes), as on Playnite's row
            bool wideOn = settings.CardShape == 2 || CardShapes.IsTriangle(settings.CardShape);
            var wide = UiTools.FindByName(c, "KX_WideArt");
            if (wide != null) { var v = wideOn ? Visibility.Visible : Visibility.Collapsed; if (wide.Visibility != v) wide.Visibility = v; }
            if (wideOn && g != null) FillTileArt(c, g, false);
            BadgeCard(c, id, AnyBadges);
            return true;
        }

        /// <summary>
        /// The theme's item (Tall / Full Screen's games, the tiles, the folders): its corners, its gap (Lower), its picture decoded
        /// at the card's size (a game's cover, else its background, else its name on a plain card).
        /// </summary>
        private bool CarDressItem(CarSlot s)
        {
            if (s.Tile == null || s.Ring == null) CarFindParts(s);
            if (s.Tile == null) return false;   // (not made yet: after the next layout)
            var it = s.Content as CarouselItem;
            s.ArtKey = null;
            if (it == null) return true;
            var f = carFrame;
            int design = f != null ? f.Design : CarDesign;
            double corner = design == 2 ? Math.Min(3, LookCardCorner) : LookCardCorner;   // (the look's card corners: Theme Studio)
            it.Corner = new CornerRadius(corner);
            it.RingCorner = new CornerRadius(design == 2 ? corner : corner + 4);
            it.RingMargin = new Thickness(design == 2 ? 0 : -4);
            // a tile in Lower lines up with Playnite's cards: the same gap round it as a card has
            var gap = design == 0 ? new Thickness(Math.Max(0, CardSpacingNow) / 2.0) : new Thickness(0);
            if (s.Tile.Margin != gap) s.Tile.Margin = gap;
            // (0.89) Tall / Full Screen's games take Card look's shape too (circle, hexagon, triangles…): the card cut to it, its
            // rounded edge hidden (the highlighted one still stands out by its size)
            // (0.90) Full Screen's panels: Tall or Wide only (no cut shapes)
            int shape = design == 1 && it.Kind == "game" && ShapesOn ? (CardShapes.IsTriangle(settings.CardShape) ? TriShapeAt(s.V) : settings.CardShape) : -1;
            if (it.Kind == "game" && design != 0) CarPickArt(it, design);
            if (CardShapes.IsCut(shape)) SetCut(s.Tile, shape);
            else if (s.Tile.ReadLocalValue(CutShapeProperty) != DependencyProperty.UnsetValue) { s.Tile.ClearValue(UIElement.ClipProperty); s.Tile.ClearValue(CutShapeProperty); }
            if (s.Ring != null) { var rv = CardShapes.IsCut(shape) ? Visibility.Hidden : Visibility.Visible; if (s.Ring.Visibility != rv) s.Ring.Visibility = rv; }
            if (it.Kind == "game")
            {
                var entry = it.Source;
                Game g = null;
                try { Guid id = EntryId(entry); if (id != Guid.Empty) g = api.Database.Games.Get(id); } catch (Exception) { }
                if (g != null) { it.Name = g.Name ?? ""; it.ShowInstalled = settings.BadgeInstalled && g.IsInstalled; }
            }
            if (f == null) return true;
            for (int tries = 0; tries < 2 && !string.IsNullOrEmpty(it.PictureFile); tries++)
            {
                // a landscape picture (a background, a folder's photo) is decoded wider, to fill a tall card
                bool wideArt = it.PictureWide || it.Kind == "folder";
                int px = CarPx(Math.Max(f.W, f.H * (wideArt ? 1.4 : 0.75)) * f.Grow);
                string key = px + "|" + it.PictureFile;
                if (it.PictureFor == key) break;
                int state;
                var img = CarImage(it.PictureFile, px, out state);
                if (state == 0) { it.Picture = img; it.PictureFor = key; break; }
                if (state == 1) { s.ArtKey = key; break; }
                // couldn't be read: a game's background instead, else its name (or the tile's icon and name)
                if (!string.IsNullOrEmpty(it.AltPictureFile)) { it.PictureFile = it.AltPictureFile; it.AltPictureFile = null; it.PictureWide = true; it.PictureFor = null; continue; }
                it.PictureFor = key;
                break;
            }
            return true;
        }

        /// <summary>(0.90) Tall / Full Screen with Card look › Wide: the game's wide picture (its background) instead of its cover.</summary>
        private void CarPickArt(CarouselItem it, int design)
        {
            if (it.CoverFile == null && it.BackFile == null) return;
            bool wide = settings.CardShape == CardShapes.Wide;
            string want = wide ? (it.BackFile ?? it.CoverFile) : (it.CoverFile ?? it.BackFile);
            if (want == null || (it.PictureFile == want && it.PictureFor != null)) return;
            if (it.PictureFile == want) return;
            it.PictureFile = want;
            it.AltPictureFile = want == it.CoverFile ? it.BackFile : null;
            it.PictureWide = want == it.BackFile;
            it.PictureFor = null;
        }

        /// <summary>(0.90) Tall: the cards' height for their width, from Card look's shape.</summary>
        private double CarTallAspect
        {
            get
            {
                switch (settings.CardShape)
                {
                    case CardShapes.Square: case CardShapes.Circle: return 1;
                    case CardShapes.Wide: return 9.0 / 16;
                    case CardShapes.Hexagon: return 15.0 / 13;
                    case CardShapes.TriUp: case CardShapes.TriDown: return 13.0 / 15;
                    default: return CarouselGeometry.TallShape;
                }
            }
        }

        /// <summary>The gap round Lower's game card: the row's own.</summary>
        private void CarSpacing(FrameworkElement sp)
        {
            Thickness want;
            double w = carFrame != null ? carFrame.W : 0;
            if (TrianglesPacked && w > 0)
            {
                double wide = w / Math.Max(0.1, TriStep);
                double m = -Math.Round((wide - w) / 2);
                want = new Thickness(m, 0, m, 0);
            }
            else if (settings.CardSpacing >= 0) want = new Thickness(settings.CardSpacing / 2.0);
            else
            {
                // Playnite's own gap
                var be = BindingOperations.GetBindingExpressionBase(sp, FrameworkElement.MarginProperty);
                if (be != null) be.UpdateTarget();
                else if (playniteCardMargin.HasValue && sp.Margin != playniteCardMargin.Value) sp.SetCurrentValue(FrameworkElement.MarginProperty, playniteCardMargin.Value);
                return;
            }
            if (sp.Margin != want) sp.SetCurrentValue(FrameworkElement.MarginProperty, want);
        }

        // ---------------- sharp pictures (Tall / Full Screen) ----------------

        private readonly Dictionary<string, ImageSource> carImages = new Dictionary<string, ImageSource>();
        private readonly HashSet<string> carLoading = new HashSet<string>();
        private readonly HashSet<string> carWanted = new HashSet<string>();
        private double carScreenScale;

        /// <summary>Device pixels per 1080p unit (pictures are decoded this sharp).</summary>
        private double CarScreenScale()
        {
            if (carScreenScale > 0) return carScreenScale;
            try
            {
                var w = Application.Current.MainWindow;
                var src = PresentationSource.FromVisual(w);
                double dpi = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M22 : 1;
                double h = w.ActualHeight > 0 ? w.ActualHeight : 1080;
                carScreenScale = Math.Max(1, Math.Min(2.5, h / 1080.0 * dpi));
            }
            catch (Exception) { carScreenScale = 1; }
            return carScreenScale;
        }

        /// <summary>A few fixed sizes (not one per card size), so a picture is decoded once.</summary>
        private int CarPx(double units)
        {
            double want = units * CarScreenScale();
            foreach (int s in new[] { 300, 450, 700, 1000, 1400 }) if (want <= s) return s;
            return 2000;
        }

        /// <summary>A picture at a width: state 0 here it is, 1 loading (asked for), 2 it couldn't be read.</summary>
        private ImageSource CarImage(string file, int px, out int state)
        {
            string key = px + "|" + file;
            lock (carImages)
            {
                ImageSource img;
                if (carImages.TryGetValue(key, out img)) { state = img != null ? 0 : 2; return img; }
                state = 1;
                if (carLoading.Contains(key)) return null;
                carLoading.Add(key);
            }
            lock (carWanted) carWanted.Add(key);
            KippixShared.SafeWork.Queue(_ =>
            {
                bool wanted;
                lock (carWanted) wanted = carWanted.Contains(key);
                if (!wanted)
                {
                    // scrolled past before its turn: not decoded (asked for again if it comes back)
                    lock (carImages) carLoading.Remove(key);
                    OnUi(() => CarArtArrived(key));
                    return;
                }
                ImageSource got = null;
                try { got = Images.LoadUncached(file, px); } catch (Exception) { }
                lock (carImages)
                {
                    // big pictures: only a few kept (those on screen stay)
                    if (carImages.Count > 30)
                        lock (carWanted)
                            foreach (string k in carImages.Keys.Where(k => !carWanted.Contains(k)).ToList()) carImages.Remove(k);
                    carImages[key] = got;
                    carLoading.Remove(key);
                }
                OnUi(() => CarArtArrived(key));
            });
            return null;
        }

        private void CarArtArrived(string key)
        {
            if (!carActive) return;
            bool any = false;
            foreach (var s in carShown.Values) if (s.ArtKey == key) { s.Dressed = false; any = true; }
            if (any) CarDressSoon();
        }

        private string CarCoverFile(Game g)
        {
            string pick = g != null ? ShuffleCoverFile(g.Id) : null;   // (Shuffle covers: the one picked for now)
            if (pick != null) return pick;
            if (g == null || string.IsNullOrEmpty(g.CoverImage) || g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            return SafeFullPath(g.CoverImage);
        }

        private string CarBackgroundFile(Game g)
        {
            if (g == null || string.IsNullOrEmpty(g.BackgroundImage) || g.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            return SafeFullPath(g.BackgroundImage);
        }
    }
}
