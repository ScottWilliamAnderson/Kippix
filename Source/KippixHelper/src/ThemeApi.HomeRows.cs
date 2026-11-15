using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    /// <summary>A game in one of the extra rows under the games.</summary>
    public class RowTile : Observable
    {
        internal Guid GameId;
        internal string CoverFile;
        internal bool RunRow;   // (issue #11) a tile in "Running now": A switches to it, X closes it
        public string Name { get; set; }
        private ImageSource cover;
        public ImageSource Cover { get { return cover; } set { if (Set(ref cover, value, "Cover")) Notify("HasCover"); } }
        public bool HasCover { get { return cover != null; } }
        public bool ShowNew { get; set; }
        public bool ShowInstalled { get; set; }
        private bool showRunning;
        /// <summary>(issue #11) Running now: a green dot.</summary>
        public bool ShowRunning { get { return showRunning; } set { Set(ref showRunning, value, "ShowRunning"); } }
        public string AchText { get; set; }
        public bool ShowAch { get { return !string.IsNullOrEmpty(AchText); } }
        public string HltbText { get; set; }
        public bool ShowHltb { get { return !string.IsNullOrEmpty(HltbText); } }
        public double Width { get; set; }
        public double Height { get; set; }
        public ICommand Command { get; set; }
        // its shape (Game cards and layout › Card shape): set in code (a Geometry is never bound from PluginSettings)
        internal int Shape;
        private bool isCut;
        public bool IsCut { get { return isCut; } set { if (Set(ref isCut, value, "IsCut")) Notify("IsRounded"); } }
        public bool IsRounded { get { return !isCut; } }
        private Geometry clip, outline;
        public Geometry Clip { get { return clip; } set { Set(ref clip, value, "Clip"); } }
        public Geometry Outline { get { return outline; } set { Set(ref outline, value, "Outline"); } }
        private Thickness badgeTop = new Thickness(8), badgeBottom = new Thickness(8);
        public Thickness BadgeTop { get { return badgeTop; } set { Set(ref badgeTop, value, "BadgeTop"); } }
        public Thickness BadgeBottom { get { return badgeBottom; } set { Set(ref badgeBottom, value, "BadgeBottom"); } }
        private Thickness tileMargin = new Thickness(0, 0, 16, 0);
        public Thickness TileMargin { get { return tileMargin; } set { Set(ref tileMargin, value, "TileMargin"); } }
        private HorizontalAlignment badgeAlign = HorizontalAlignment.Left;
        public HorizontalAlignment BadgeAlign { get { return badgeAlign; } set { Set(ref badgeAlign, value, "BadgeAlign"); } }
    }

    /// <summary>One extra row (Favourites, Recently added...).</summary>
    public class ExtraRow : Observable
    {
        internal string Key;
        public string Title { get; set; }
        public ObservableCollection<RowTile> Tiles { get; private set; }
        private double opacity = 1;
        public double RowOpacity { get { return opacity; } set { Set(ref opacity, value, "RowOpacity"); } }
        public double TileHeight { get; set; }
        public ExtraRow() { Tiles = new ObservableCollection<RowTile>(); }
    }

    /// <summary>
    /// Home: the games as one row (Playnite's own) or as a grid (rows x columns), extra rows under the row like a
    /// console dashboard (Down from the games), and small badges on the game tiles.
    /// </summary>
    public partial class ThemeApi
    {
        public ObservableCollection<ExtraRow> ExtraRowsList { get; private set; }
        public ObservableCollection<PickRow> ExtraRowChoices { get; private set; }

        private void InitHomeRows()
        {
            ExtraRowsList = new ObservableCollection<ExtraRow>();
            ExtraRowChoices = new ObservableCollection<PickRow>();
            Running.Changed += () => OnUi(RunningListChanged);   // (issue #11) the Running now row follows what's running
            CycleHomeLayoutCommand = new RelayCommand(_ =>
            {
                if (boardEditing) BoardEndEdit(false);
                settings.HomeLayoutMode = (settings.HomeLayoutMode + 1) % 6;   // row, grid, list, board, manual, folder
                carWhy = "Games shown as changed";
                if (settings.HomeLayoutMode != 0) LeaveExtraRows(false);
                SaveSettings(); PlaySound("move"); HomeModeChanged();
            });
            CycleGridRowsCommand = new RelayCommand(_ => { settings.GridRows = settings.GridRows % 4 + 1; SaveSettings(); PlaySound("move"); HomeModeChanged(); });
            CycleGridColumnsCommand = new RelayCommand(_ => { settings.GridColumns = settings.GridColumns >= 10 ? 3 : settings.GridColumns + 1; SaveSettings(); PlaySound("move"); HomeModeChanged(); });
            CycleListRowsCommand = new RelayCommand(_ => { settings.ListRows = settings.ListRows >= 10 ? 1 : settings.ListRows + 1; SaveSettings(); PlaySound("move"); HomeModeChanged(); UpdateSliders(); });
            CycleListSideCommand = new RelayCommand(_ => { settings.ListSide = 1 - settings.ListSide; SaveSettings(); PlaySound("move"); HomeModeChanged(); });
        }

        // =====================================================================
        // Settings
        // =====================================================================

        public bool IsOptionsHomeRows { get { return optionsPage == "homerows"; } }
        public bool IsOptionsExtraRows { get { return optionsPage == "extrarows"; } }
        public bool IsOptionsBadges { get { return optionsPage == "badges"; } }
        public ICommand CycleHomeLayoutCommand { get; private set; }
        public ICommand CycleGridRowsCommand { get; private set; }
        public ICommand CycleGridColumnsCommand { get; private set; }
        public ICommand CycleListRowsCommand { get; private set; }
        /// <summary>The list while sizing it: "5 games · each 640 × 180".</summary>
        public string ListStretchText
        {
            get
            {
                int n = Math.Max(1, Math.Min(10, settings.ListRows));
                return string.Format(Loc("LOC_KX_ListSizeLine", "{0} games  ·  each {1} × {2}"), n, Math.Round(CurrentListTileW()), Math.Round(CurrentListTileH()));
            }
        }
        private bool listTooTall;
        /// <summary>The list (its games x their height) is taller than the screen: the settings say so.</summary>
        public bool ListTooTall { get { return listTooTall && LayoutNow == 2; } }
        public ICommand CycleListSideCommand { get; private set; }
        /// <summary>The list down one side is on the home screen now (Folder: an opened folder in List).</summary>
        public bool IsListMode { get { return LayoutNow == 2; } }
        /// <summary>The grid or the list: the games scroll downwards and the tiles before them stand in a column.</summary>
        private bool SideMode { get { return LayoutNow == 1 || LayoutNow == 2; } }
        public string HomeLayoutText
        {
            get
            {
                return settings.HomeLayoutMode == 5 ? Loc("LOC_KX_LayoutFolder", "Folder (WIP)")
                     : settings.HomeLayoutMode == 4 ? Loc("LOC_KX_LayoutManualBoard", "Manual Board")
                     : settings.HomeLayoutMode == 3 ? Loc("LOC_KX_LayoutBoard", "Board")
                     : settings.HomeLayoutMode == 1 ? Loc("LOC_KX_LayoutGrid", "Grid")
                     : settings.HomeLayoutMode == 2 ? Loc("LOC_KX_LayoutList", "List down one side")
                     : Loc("LOC_KX_LayoutRow", "Row");
            }
        }
        public string ListRowsText { get { return settings.ListRows.ToString(); } }
        public string ListSideText { get { return settings.ListSide == 0 ? Loc("LOC_KX_Left", "Left") : Loc("LOC_KX_Right", "Right"); } }

        /// <summary>Row / grid / list (or their sizes) changed: lay everything out again.</summary>
        private void HomeModeChanged()
        {
            FolderApplyMode();
            BgFollowLayout();   // (0.87) each layout's own background place and fade   // Folder: an opened folder that isn't there any more (or Folder no longer chosen) closes
            NotifyHomeRows();
            NotifyBoard();
            BoardApplyMode();
            if (IsBoardMode) BoardRefreshSoon();
            foreach (string n in new[] { "LogoAlignment", "LogoTextAlignment", "InfoAlignment", "HintsAlignment", "FilterAlignment", "LogoInnerAlignment", "LogoInnerTextAlignment" }) Notify(n);
            ApplyHomeLayoutSoon();
            Notify("HomeTilesShown");
            Notify("HomeTilesVertical");
            Notify("HomeTileMargin");
            ApplyCardLayoutSoon();
            RebuildExtraRowsSoon();
            // the cards already on screen dressed for the layout now (e.g. into the list, whose play info comes with its games),
            // before the next frame is drawn rather than on the next tile art pass
            ui.BeginInvoke(new Action(UpdateTileArt), DispatcherPriority.DataBind);
            ui.BeginInvoke(new Action(() => { homeTilesShown = false; tilesVisible = false; SetCardsLeft(0); ShiftList(0); ApplyHomeTiles(); RefreshTileArtSoon(); }), DispatcherPriority.ContextIdle);
            ui.BeginInvoke(new Action(CarouselApplyMode), DispatcherPriority.ContextIdle);   // Row: the carousel (Lower with Loop, Tall, Full Screen)
        }
        public string GridRowsText { get { return settings.GridRows.ToString(); } }
        public string GridColumnsText { get { return settings.GridColumns.ToString(); } }
        public string HomeRowsSummary
        {
            get
            {
                if (settings.HomeLayoutMode == 1) return string.Format(Loc("LOC_KX_GridSummary", "Grid, {0} × {1}"), settings.GridColumns, settings.GridRows);
                if (settings.HomeLayoutMode == 2) return string.Format(Loc("LOC_KX_ListSummary", "List, {0} on screen"), Math.Max(1, Math.Min(10, settings.ListRows)));
                if (settings.HomeLayoutMode == 3) return string.Format(Loc("LOC_KX_BoardSummaryShort", "Board, {0} cards"), settings.BoardCards != null ? settings.BoardCards.Count : 0);
                if (settings.HomeLayoutMode == 4) return settings.ManualPlacement == 1 ? string.Format(Loc("LOC_KX_FreeSummaryShort", "Manual Board, free placement, {0} tiles"), settings.FreeCards != null ? settings.FreeCards.Count : 0)
                                                                                     : string.Format(Loc("LOC_KX_ManualSummaryShort2", "Manual Board, grid, {0} tiles"), settings.ManualCards != null ? settings.ManualCards.Count : 0);
                if (settings.HomeLayoutMode == 5) return FoldersShortSummary;
                if (settings.RowDesign != 0) return string.Format(Loc("LOC_KX_RowSummaryDesign", "Row, {0}"), RowDesignText);
                return settings.ExtraRows ? string.Format(Loc("LOC_KX_RowSummaryExtra", "Row + {0} more"), ExtraKeys().Count) : Loc("LOC_KX_LayoutRow", "Row");
            }
        }
        public bool ExtraRowsOn
        {
            get { return settings.ExtraRows; }
            set { if (settings.ExtraRows == value) return; settings.ExtraRows = value; if (!value) LeaveExtraRows(false); SaveSettings(); NotifyHomeRows(); RebuildExtraRowsSoon(); }
        }
        public bool BadgeInstalled { get { return settings.BadgeInstalled; } set { if (SetSetting(ref settings.BadgeInstalled, value)) { Notify("BadgeInstalled"); RefreshBadgesSoon(); } } }
        public bool BadgeRunning { get { return settings.BadgeRunning; } set { if (SetSetting(ref settings.BadgeRunning, value)) { Notify("BadgeRunning"); RunningBadgesChanged(); } } }   // (issue #11) "Running" on the tiles of what's running
        public bool BadgeNew { get { return settings.BadgeNew; } set { if (SetSetting(ref settings.BadgeNew, value)) { Notify("BadgeNew"); RefreshBadgesSoon(); } } }
        public bool BadgeAch { get { return settings.BadgeAch; } set { if (SetSetting(ref settings.BadgeAch, value)) { Notify("BadgeAch"); RefreshBadgesSoon(); } } }
        public bool BadgeHltb { get { return settings.BadgeHltb; } set { if (SetSetting(ref settings.BadgeHltb, value)) { Notify("BadgeHltb"); RefreshBadgesSoon(); } } }
        public string BadgesSummary
        {
            get
            {
                var on = new List<string>();
                if (settings.BadgeInstalled) on.Add(Loc("LOC_KX_BadgeInstalledShort", "Installed"));
                if (settings.BadgeRunning) on.Add(Loc("LOC_KX_BadgeRunningShort", "Running"));
                if (settings.BadgeNew) on.Add(Loc("LOC_KX_BadgeNewShort", "New"));
                if (settings.BadgeAch) on.Add(Loc("LOC_KX_BadgeAchShort", "Achievements"));
                if (settings.BadgeHltb) on.Add("HLTB");
                return on.Count == 0 ? Loc("LOC_KX_Off", "Off") : string.Join(", ", on);
            }
        }

        private void NotifyHomeRows()
        {
            foreach (string n in new[] { "IsListMode", "IsBoardMode", "IsBoardOnly", "IsManualMode", "IsNotBoardMode", "ShowCardLook", "ShowCardSpacing", "ShowTriArrange", "ShowTriSpacing", "HomeLayoutText", "GridRowsText", "GridColumnsText", "ListRowsText", "ListStretchText", "ListTooTall", "ListSideText",
                                         "HomeRowsSummary", "ExtraRowsOn", "BadgesSummary",
                                         "ShowExtraRows", "CardsMargin", "CardsWidth", "CardsHAlign", "CardsHScroll", "CardsVScroll",
                                         "RowSettingsShown", "RowDesignTopShown", "RowDesignFolderShown", "GridSettingsShown", "ListSettingsShown", "IsRowLower", "HomeLabelOwn", "HomeLabelText" })
                Notify(n);
        }

        private List<string> ExtraKeys()
        {
            if (settings.ExtraRowKeys == null) settings.ExtraRowKeys = new List<string> { "recent", "favourites" };
            return settings.ExtraRowKeys;
        }

        private string ExtraRowName(string key)
        {
            switch (key)
            {
                case "running": return Loc("LOC_KX_RowRunning", "Running now");
                case "favourites": return Loc("LOC_KX_RowFavourites", "Favourites");
                case "recent": return Loc("LOC_KX_RowRecent", "Recently added");
                case "installed": return Loc("LOC_KX_RowInstalled", "Installed");
                case "never": return Loc("LOC_KX_RowNever", "Never played");
                case "upnext": return Loc("LOC_KX_PageUpNext", "Up next");
            }
            if (key.StartsWith("shelf:", StringComparison.Ordinal))
            {
                var shelf = ShelfById(key.Substring(6));
                return shelf != null ? shelf.Name : null;
            }
            if (key.StartsWith("filter:", StringComparison.Ordinal))
            {
                Guid id;
                if (Guid.TryParse(key.Substring(7), out id))
                {
                    try { var p = api.Database.FilterPresets.Get(id); if (p != null) return p.Name; } catch (Exception) { }
                }
            }
            return null;
        }

        /// <summary>The Extra rows page: every row you can have, a switch on each (the ones on say where they come).</summary>
        private void BuildExtraRowChoices()
        {
            ExtraRowChoices.Clear();
            var keys = new List<string> { "running", "upnext", "favourites", "recent", "installed", "never" };
            try { foreach (var p in api.Database.FilterPresets.OrderBy(p => p.Name)) keys.Add("filter:" + p.Id); } catch (Exception) { }
            foreach (var c in settings.Collections) keys.Add("shelf:" + c.Id);
            var on = ExtraKeys();
            bool filtersHeader = false, shelvesHeader = false;
            ExtraRowChoices.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LibGroupGames", "YOUR GAMES") });
            foreach (string k in keys)
            {
                string name = ExtraRowName(k);
                if (name == null) continue;
                if (k.StartsWith("shelf:", StringComparison.Ordinal) && !shelvesHeader)
                {
                    ExtraRowChoices.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_YourCollections", "YOUR COLLECTIONS") });
                    shelvesHeader = true;
                }
                if (k.StartsWith("filter:", StringComparison.Ordinal) && !filtersHeader)
                {
                    ExtraRowChoices.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SavedFilters", "YOUR SAVED FILTERS") });
                    filtersHeader = true;
                }
                string key = k;
                int pos = on.IndexOf(k);
                var row = new PickRow
                {
                    Text = name, Key = k, IsSelected = pos >= 0, IsSwitch = true,
                    Sub = pos >= 0 ? string.Format(Loc("LOC_KX_RowNumber", "Row {0}"), pos + 2) : Loc("LOC_KX_Off", "Off"),
                    Icon = k.StartsWith("filter:", StringComparison.Ordinal) ? "filter" : k == "upnext" ? "forward" : k == "running" ? "play" : "shelf"
                };
                // (issue #11) Running now is always the first row, and shows by itself with Keep browsing on
                if (k == "running") { row.Sub = pos >= 0 || settings.RunKeepBrowsing ? Loc("LOC_KX_RunRowSub", "First, while something is running") : Loc("LOC_KX_Off", "Off"); row.IsSelected = pos >= 0 || settings.RunKeepBrowsing; }
                row.Command = new RelayCommand(_ => ToggleExtraRow(key));
                ExtraRowChoices.Add(row);
            }
        }

        private void ToggleExtraRow(string key)
        {
            var on = ExtraKeys();
            if (on.Contains(key)) on.Remove(key);
            else on.Add(key);
            SaveSettings();
            PlaySound("move");
            BuildExtraRowChoices();
            NotifyHomeRows();
            RebuildExtraRowsSoon();
            ui.BeginInvoke(new Action(() =>
            {
                var r = ExtraRowChoices.FirstOrDefault(x => x.Key == key);
                if (r != null) UiTools.FocusItem("KX_ExtraRowChoiceList", r);
            }), DispatcherPriority.ContextIdle);
        }

        // =====================================================================
        // Grid mode (on the cover row's own panel)
        // =====================================================================

        public Thickness CardsMarginFor(double left)
        {
            switch (LayoutNow)
            {
                case 1: return new Thickness(gridTilesRoom, 0, 0, 16 + settings.CardOffset);
                case 2: return settings.ListSide == 0 ? new Thickness(64, 0, 0, ListBottom) : new Thickness(0, 0, 64, ListBottom);
            }
            return new Thickness(left, 0, 0, -55 + settings.CardOffset);
        }
        private double ListBottom { get { return Math.Max(10, 34 + settings.CardOffset); } }
        private double cardsWidth = double.NaN;
        public double CardsWidth { get { return cardsWidth; } }
        public HorizontalAlignment CardsHAlign
        {
            get
            {
                if (LayoutNow == 1) return HorizontalAlignment.Center;
                if (LayoutNow == 2) return settings.ListSide == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                return HorizontalAlignment.Stretch;
            }
        }
        // the row scrolls sideways, the grid and the list downwards (a "Disabled" direction tells the panel it can't scroll that way)
        public ScrollBarVisibility CardsHScroll { get { return SideMode ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden; } }
        public ScrollBarVisibility CardsVScroll { get { return SideMode ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled; } }

        private DependencyProperty horizProp, rowsProp;
        private BindingBase horizBinding, rowsBinding;
        private bool horizFromTemplate, rowsFromTemplate, gridSet;
        private DependencyObject gridPanel;
        private double gridTilesRoom;          // grid: room on the left for the tiles before the games
        private int gridFixTries;

        /// <summary>
        /// Called from ApplyCardLayout. True when the grid or the list is on (it sized the list itself).
        /// Playnite's panel, laid out downwards, makes each tile (height / (rows + 0.5)) tall and fits as many across as there's room
        /// for: so the list's height sets the tile size and its width the number of columns. Everything is worked out from the
        /// space really there (not a fixed 1920 × 1080), so no column or row ends up off the screen.
        /// </summary>
        private bool ApplyGrid(DependencyObject panel, Type t, FrameworkElement list, int aw, int ah)
        {
            if (!ReferenceEquals(panel, gridPanel))
            {
                gridPanel = panel;
                horizProp = StaticProperty(t, "UseHorizontalLayoutProperty");
                rowsProp = StaticProperty(t, "RowsProperty");
                horizBinding = horizProp != null ? BindingOperations.GetBindingBase(panel, horizProp) : null;
                rowsBinding = rowsProp != null ? BindingOperations.GetBindingBase(panel, rowsProp) : null;
                horizFromTemplate = horizProp != null && DependencyPropertyHelper.GetValueSource(panel, horizProp).BaseValueSource == BaseValueSource.ParentTemplate;
                rowsFromTemplate = rowsProp != null && DependencyPropertyHelper.GetValueSource(panel, rowsProp).BaseValueSource == BaseValueSource.ParentTemplate;
                gridSet = false;
            }
            if (horizProp == null || rowsProp == null) return false;
            if (!SideMode)
            {
                // (1.0.2) Row goes across whatever Playnite's own setting says: a new Playnite lays its list downwards
                // (Playnite's fullscreen settings › Horizontal layout off), which turned Row into a grid of big cards
                object across = panel.GetValue(horizProp);
                if (!(across is bool) || !(bool)across) panel.SetValue(horizProp, true);
                if (gridSet)
                {
                    Restore(panel, rowsProp, rowsFromTemplate ? null : rowsBinding);
                    gridSet = false;
                    if (!double.IsNaN(cardsWidth)) { cardsWidth = double.NaN; Notify("CardsWidth"); }
                    gridTilesRoom = 0;
                    Notify("CardsMargin");
                    Notify("CardsHAlign");
                }
                return false;
            }
            if (aw <= 0 || ah <= 0) { aw = 2; ah = 3; }
            var root = VisualTreeHelper.GetParent(list) as FrameworkElement;
            double screenW = root != null && root.ActualWidth > 0 ? root.ActualWidth : 1920;
            double screenH = root != null && root.ActualHeight > 0 ? root.ActualHeight : 1080;
            // the tiles before the games: room is made for them only where they're always there (the list on Auto show keeps its
            // size and moves down for them when they come in, so it's the same size as a folder's list, which has none)
            int tiles = LayoutNow == 2 && TilesMode == 1 ? 0 : SideTileCount();
            int rows;
            double itemW, itemH, w, h;
            if (LayoutNow == 1)
            {
                rows = Math.Max(1, Math.Min(4, settings.GridRows));
                int cols = Math.Max(3, Math.Min(10, settings.GridColumns));
                // the columns, a little spare (the highlighted tile grows by 6%), and a column for the tiles when they're on
                double slots = cols + 0.35 + (tiles > 0 ? 1.2 : 0);
                itemW = (screenW - 128) / slots;
                itemH = itemW * ah / aw;
                double maxH = screenH - 104 - 230 - Math.Max(0, settings.CardOffset) - 16;
                h = itemH * (rows + 0.5);
                if (h > maxH) { h = maxH; itemH = h / (rows + 0.5); itemW = itemH * aw / ah; }
                w = Math.Floor(itemW * (cols + 0.35));
                double room = tiles > 0 ? Math.Round(itemW * 1.2) : 0;
                if (Math.Abs(room - gridTilesRoom) >= 1) { gridTilesRoom = room; Notify("CardsMargin"); }
                sideItemW = itemW; sideItemH = itemH; sideCols = cols;
            }
            else
            {
                // a list down one side: one game per line, wide lines (art on the left, play info beside it). Each game is the
                // size set in Game cards and layout (or worked out from the screen, for five games), and "Games to display"
                // says how many show: the list is that many games tall (and half of the next), the games don't change size
                // with it. Playnite's panel makes each game height / (rows + 0.5) tall.
                rows = Math.Max(1, Math.Min(10, settings.ListRows));
                double avail = screenH - 112 - ListBottom;
                double perTile = 1.1;   // tiles above the list: one line each, with a gap
                double autoH = avail / (Math.Max(5, rows) + 0.5 + tiles * perTile);   // automatic: five games fill the screen ((0.96) or all of them, when more show: the first was off the top)
                itemH = settings.ListTileH > 0 ? settings.ListTileH : autoH;
                listBaseItemH = itemH;
                double autoW = autoH * ListAspectW / ListAspectH;
                itemW = settings.ListTileW > 0 ? settings.ListTileW : autoW;
                double maxW = screenW * 0.9;
                if (itemW * 1.3 > maxW) itemW = maxW / 1.3;
                h = itemH * (rows + 0.5);
                bool tooTall = h + tiles * perTile * itemH > screenH - 12;
                if (tooTall != listTooTall) { listTooTall = tooTall; Notify("ListTooTall"); }
                if (listShown != rows) { listShown = rows; Notify("ListStretchText"); }
                // the panel's shape for each game: exactly this width and height
                SetListAspect(panel, itemW, itemH);
                w = Math.Floor(itemW * 1.3);
                if (gridTilesRoom != 0) { gridTilesRoom = 0; Notify("CardsMargin"); }
                sideItemW = itemW; sideItemH = itemH; sideCols = 1;
                string sig = string.Format(CultureInfo.InvariantCulture, "{0} games, each {1:0} × {2:0} ({3}), list {4:0} × {5:0}, room for {6} tiles, screen {7:0} × {8:0}",
                    rows, itemW, itemH, settings.ListTileH > 0 || settings.ListTileW > 0 ? "your size" : "automatic", w, Math.Round(h), tiles, screenW, screenH);
                if (sig != listSizeLogged) { listSizeLogged = sig; logger.Info("Kippix: list size: " + sig + " (" + CarStateText() + ")"); }
            }
            if (!(panel.GetValue(horizProp) is bool) || (bool)panel.GetValue(horizProp)) panel.SetValue(horizProp, false);
            if (!(panel.GetValue(rowsProp) is int) || (int)panel.GetValue(rowsProp) != rows) panel.SetValue(rowsProp, rows);
            gridSet = true;
            h = Math.Round(h + TopRoomFor(itemH));   // (0.96: room above the first line for the highlight to grow into)
            if (Math.Abs(h - cardsHeight) >= 1) { cardsHeight = h; Notify("CardsHeight"); ApplyHomeLayoutSoon(); }
            if (LayoutNow == 2) UpdateSliders();
            if (double.IsNaN(cardsWidth) || Math.Abs(w - cardsWidth) >= 1) { cardsWidth = w; Notify("CardsWidth"); }
            Notify("CardsMargin");
            Notify("CardsHAlign");
            gridFixTries = 0;
            gridStaleTries = 0;
            ui.BeginInvoke(new Action(() => CheckGridColumns(panel)), DispatcherPriority.ContextIdle);
            return true;
        }

        /// <summary>The list's games are exactly <paramref name="w"/> × <paramref name="h"/>: Playnite's panel is given that as its shape.</summary>
        private void SetListAspect(DependencyObject panel, double w, double h)
        {
            try
            {
                if (aspectProp == null) return;
                int aw = (int)Math.Round(w), ah = (int)Math.Round(h);
                int cw, ch;
                if (ReadAspect(panel.GetValue(aspectProp), out cw, out ch) && cw * ah == ch * aw) return;
                panel.SetValue(aspectProp, Activator.CreateInstance(aspectProp.PropertyType, new object[] { aw, ah }));
            }
            catch (Exception) { }
        }

        private double listBaseItemH = 150;   // a game's height in the list at its own size (the step when sizing it)
        private int listShown;                // games on screen in the list right now
        private double sideItemW, sideItemH;
        private int sideCols;
        internal const int ListAspectW = 7, ListAspectH = 2;

        private string listSizeLogged, listPanelLogged;
        private int gridStaleTries;

        /// <summary>After layout: Playnite's panel must show exactly the columns asked for. If rounding gave one more or one less, nudge the width.</summary>
        private void CheckGridColumns(DependencyObject panel)
        {
            try
            {
                if (!SideMode || !ReferenceEquals(panel, gridPanel) || gridFixTries > 2) return;
                var f = panel.GetType().GetField("computedColumns", BindingFlags.NonPublic | BindingFlags.Instance);
                var fw = panel.GetType().GetField("itemWidth", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null || fw == null) return;
                int got = (int)f.GetValue(panel);
                double iw = (double)fw.GetValue(panel);
                if (got <= 0 || iw <= 0) return;
                var fh = panel.GetType().GetField("itemHeight", BindingFlags.NonPublic | BindingFlags.Instance);
                double ih = fh != null ? (double)fh.GetValue(panel) : 0;
                // Playnite's panel keeps its last sizes until it's laid out again: from the row (the folder tiles, say) they're a
                // cover's, not a list game's. Only taken once it's really laid out downwards at the list's (or grid's) height.
                var fe = panel as FrameworkElement;
                bool down = horizProp != null && panel.GetValue(horizProp) is bool && !(bool)panel.GetValue(horizProp);
                int rowsNow = rowsProp != null && panel.GetValue(rowsProp) is int ? (int)panel.GetValue(rowsProp) : 0;
                double want = fe != null && fe.ActualHeight > 0 && rowsNow > 0 ? fe.ActualHeight / (rowsNow + 0.5) : ih;
                bool settled = down && fe != null && fe.IsMeasureValid && fe.IsArrangeValid && ih > 0
                               && (Math.Abs(ih - want) < 2 || iw >= fe.ActualWidth - 1)   // (or narrowed to the panel's width)
                               && (LayoutNow != 2 || Math.Abs(fe.ActualHeight - cardsHeight) < 2);
                if (LayoutNow == 2)
                {
                    string seen = string.Format(CultureInfo.InvariantCulture, "each {0:0} × {1:0}, {2} across, panel {3:0} × {4:0}{5}",
                        iw, ih, got, fe != null ? fe.ActualWidth : 0, fe != null ? fe.ActualHeight : 0, settled ? "" : " (not laid out for the list yet)");
                    if (seen != listPanelLogged) { listPanelLogged = seen; logger.Info("Kippix: list size, Playnite's panel: " + seen); }
                }
                if (!settled)
                {
                    if (gridStaleTries++ < 6) ui.BeginInvoke(new Action(() => CheckGridColumns(panel)), DispatcherPriority.ApplicationIdle);
                    return;
                }
                gridStaleTries = 0;
                sideItemW = iw;
                if (ih > 0) sideItemH = ih;
                if (got == sideCols) { UpdateHomeTilesLayout(); return; }
                gridFixTries++;
                double w = Math.Floor(iw * (sideCols + (LayoutNow == 2 ? 0.3 : 0.35)));
                if (Math.Abs(w - cardsWidth) >= 1) { cardsWidth = w; Notify("CardsWidth"); }
                ui.BeginInvoke(new Action(() => CheckGridColumns(panel)), DispatcherPriority.ContextIdle);
            }
            catch (Exception) { }
        }

        // =====================================================================
        // Extra rows (one row mode only)
        // =====================================================================

        public bool ShowExtraRows { get { return ExtraRowsWanted && ExtraRowsList.Count > 0; } }
        /// <summary>Row › Lower with the extra rows on, or (issue #11) with Keep browsing on: then "Running now" shows by itself.</summary>
        private bool ExtraRowsWanted { get { return (settings.ExtraRows || settings.RunKeepBrowsing) && settings.HomeLayoutMode == 0 && settings.RowDesign == 0; } }
        /// <summary>In one of the extra rows: the filter name above the games shows that row's name instead (same place, same look).</summary>
        private bool InExtraRow { get { return extraRowAt > 0 && extraRowAt <= ExtraRowsList.Count; } }
        /// <summary>The filter name shows a name of its own: the extra row you're in, or (Folder) the folder that's open.</summary>
        public bool HomeLabelOwn { get { return InExtraRow || FolderMode; } }
        public string HomeLabelText { get { return InExtraRow ? ExtraRowsList[extraRowAt - 1].Title : FolderMode ? FolderLabelNow : ""; } }
        public double ExtraRowsTop { get; private set; }
        private int extraRowAt;               // 0 = the games (Playnite's row), 1.. = the extra rows
        private Guid extraReturnGame;         // the game selected in the main row before you went down
        private RowTile extraFocused;
        private bool extraOpenedDetails;
        private DispatcherTimer extraSelectTimer;
        private double rowTileW = 240, rowTileH = 360;
        private const double RowTitleH = 46, RowGap = 26;

        /// <summary>The games in Playnite's list right now (its filter): only these can be selected, so only these go in the rows. Null if unknown.</summary>
        private HashSet<Guid> GamesInView()
        {
            try
            {
                var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ItemsControl;
                if (list == null) return null;
                var set = new HashSet<Guid>();
                foreach (var it in list.Items) { Guid id = EntryId(it); if (id != Guid.Empty) set.Add(id); }
                if (!rowsListHooked)
                {
                    rowsListHooked = true;
                    ((System.Collections.Specialized.INotifyCollectionChanged)list.Items).CollectionChanged += (s, e) =>
                    {
                        if (IsBoardMode) { BoardRefreshLater(); return; }   // the Board's "All games" follows Playnite's list
                        if (!ExtraRowsWanted) return;
                        if (rowsChangeTimer == null)
                        {
                            rowsChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                            rowsChangeTimer.Tick += (s2, e2) => { rowsChangeTimer.Stop(); RebuildExtraRows(); };
                        }
                        rowsChangeTimer.Stop();
                        rowsChangeTimer.Start();
                    };
                }
                return set;
            }
            catch (Exception) { return null; }
        }
        private bool rowsListHooked;
        private DispatcherTimer rowsChangeTimer;

        private void RebuildExtraRowsSoon()
        {
            if (ui == null) return;
            if (FolderMode) FolderRefreshSoon();   // (a collection or a favourite changed: what's in the folders too)
            ui.BeginInvoke(new Action(RebuildExtraRows), DispatcherPriority.Background);
        }

        /// <summary>Fills the extra rows from the library (on a worker thread), keeping where you are if you're in them.</summary>
        private void RebuildExtraRows()
        {
            if (!IsFullscreen) return;
            if (!ExtraRowsWanted) { ExtraRowsList.Clear(); Notify("ShowExtraRows"); return; }   // (Row: Lower only)
            var keys = settings.ExtraRows ? ExtraKeys().ToList() : new List<string>();
            // (issue #11) "Running now": first when it's one of your rows, and there by itself with Keep browsing on
            bool runRow = keys.Remove("running") || settings.RunKeepBrowsing;
            if (runRow) keys.Insert(0, "running");
            double w = rowTileW, h = rowTileH;
            var shown = GamesInView();
            KippixShared.SafeWork.Queue(_ =>
            {
                var built = new List<Tuple<string, string, List<Game>>>();
                try
                {
                    var all = api.Database.Games.Where(g => g != null && !g.Hidden && ProfileAllowsGame(g)).ToList();
                    foreach (string k in keys)
                    {
                        string name = null;
                        OnUiSync(() => name = ExtraRowName(k));
                        if (name == null) continue;
                        List<Game> games;
                        switch (k)
                        {
                            case "running":   // (whatever Playnite's filter or the hidden flag say: it's running), the last one started first
                                games = new List<Game>();
                                foreach (var r in Running.Snapshot().AsEnumerable().Reverse()) { var rg = api.Database.Games.Get(r.Id); if (rg != null) games.Add(rg); }
                                break;
                            case "favourites": games = all.Where(g => IsFavouriteNow(g)).OrderByDescending(g => LastPlayedNow(g) ?? DateTime.MinValue).ToList(); break;
                            case "recent": games = all.Where(g => g.Added.HasValue).OrderByDescending(g => g.Added.Value).ToList(); break;
                            case "installed": games = all.Where(g => g.IsInstalled).OrderByDescending(g => g.LastActivity ?? DateTime.MinValue).ToList(); break;
                            case "never": games = all.Where(g => g.Playtime == 0).OrderByDescending(g => g.Added ?? DateTime.MinValue).ToList(); break;
                            case "upnext":
                                {
                                    // in the queue's order (a copy of it: the list changes on the UI thread)
                                    List<string> queue = null;
                                    OnUiSync(() => queue = settings.UpNext != null ? settings.UpNext.ToList() : new List<string>());
                                    var byId = all.ToDictionary(g => g.Id.ToString());
                                    games = new List<Game>();
                                    foreach (string s in queue ?? new List<string>()) { Game q; if (byId.TryGetValue(s, out q)) games.Add(q); }
                                    break;
                                }
                            default:
                                {
                                    games = new List<Game>();
                                    if (k.StartsWith("shelf:", StringComparison.Ordinal))
                                    {
                                        GameShelf shelf = null;
                                        OnUiSync(() => { var found = ShelfById(k.Substring(6)); if (found != null) shelf = new GameShelf { Id = found.Id, Name = found.Name, Games = found.Games.ToList() }; });   // a copy: the list can change on the UI thread
                                        if (shelf != null) games = ShelfGames(shelf);
                                        break;
                                    }
                                    Guid id;
                                    if (Guid.TryParse(k.Substring(7), out id))
                                    {
                                        var p = api.Database.FilterPresets.Get(id);
                                        if (p != null && p.Settings != null) games = api.Database.GetFilteredGames(p.Settings).Where(g => !g.Hidden).OrderBy(g => g.SortingName ?? g.Name).ToList();
                                    }
                                    break;
                                }
                        }
                        if (shown != null && k != "running") games = games.Where(g => shown.Contains(g.Id)).ToList();   // Playnite can only select what its filter shows
                        if (games.Count > 0) built.Add(Tuple.Create(k, name, games.Take(40).ToList()));
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: extra rows: " + ex.Message); }
                OnUi(() =>
                {
                    int wasAt = extraRowAt;
                    // (issue #11) Running now comes and goes above the others: where you were is found again by its row and game, not its number
                    string wasKey = wasAt > 0 && wasAt <= ExtraRowsList.Count ? ExtraRowsList[wasAt - 1].Key : null;
                    Guid wasGame = extraFocused != null ? extraFocused.GameId : Guid.Empty;
                    rowOffsets.Clear();
                    ExtraRowsList.Clear();
                    foreach (var b in built)
                    {
                        rowTilePlace = 0;
                        var row = new ExtraRow { Key = b.Item1, Title = b.Item2, TileHeight = h };
                        foreach (var g in b.Item3) { var tile = MakeRowTile(g, w, h); tile.RunRow = b.Item1 == "running"; row.Tiles.Add(tile); }
                        ExtraRowsList.Add(row);
                    }
                    Notify("ShowExtraRows");
                    if (wasAt > 0)
                    {
                        if (ExtraRowsList.Count == 0) LeaveExtraRows(true);
                        else
                        {
                            int at = wasKey != null ? ExtraRowsList.ToList().FindIndex(r => r.Key == wasKey) + 1 : 0;
                            if (at <= 0) at = Math.Min(wasAt, ExtraRowsList.Count);
                            int col = 0;
                            var tiles = ExtraRowsList[at - 1].Tiles;
                            for (int i = 0; i < tiles.Count; i++) if (tiles[i].GameId == wasGame) { col = i; break; }
                            if (StatusScreenUp()) { extraRowAt = at; extraFocused = tiles[col]; }   // (Playnite's status screen has the highlight: back on the tile when it closes)
                            else ui.BeginInvoke(new Action(() => GoToExtraRow(at, col)), DispatcherPriority.Loaded);
                        }
                    }
                    LayoutExtraRows(false);
                    LoadRowCovers();
                });
            });
        }

        private void OnUiSync(Action a)
        {
            if (ui == null) return;
            if (ui.CheckAccess()) a(); else ui.Invoke(a);
        }

        private int rowTilePlace;   // where the next tile goes in its row (triangles take turns pointing up and down)

        private RowTile MakeRowTile(Game g, double w, double h)
        {
            var t = new RowTile
            {
                GameId = g.Id, Name = g.Name, Width = w, Height = h,
                CoverFile = !string.IsNullOrEmpty(g.CoverImage) && !g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? SafeFullPath(g.CoverImage) : g.CoverImage,
                ShowInstalled = settings.BadgeInstalled && g.IsInstalled,
                ShowRunning = settings.BadgeRunning && Running.IsRunning(g.Id),
                ShowNew = settings.BadgeNew && IsNewGame(g),
                AchText = settings.BadgeAch ? AchBadgeText(g.Id) : null,
                HltbText = settings.BadgeHltb ? HltbBadgeText(g.Id, false) : null
            };
            var tile = t;
            t.Command = new RelayCommand(_ => OpenRowTile(tile));
            ShapeRowTile(t, rowTilePlace++);
            return t;
        }

        private void LoadRowCovers()
        {
            foreach (var row in ExtraRowsList.ToList())
                foreach (var t in row.Tiles.ToList())
                {
                    var tile = t;
                    string f = t.CoverFile;
                    if (settings.CardShape == 2 || CardShapes.IsTriangle(settings.CardShape))
                    {
                        // wide tiles: the same wide picture as the main row
                        Game g = null;
                        try { g = api.Database.Games.Get(t.GameId); } catch (Exception) { }
                        string wide = g != null ? WideArtPath(g) : null;
                        if (wide != null) f = wide;
                    }
                    if (string.IsNullOrEmpty(f)) continue;
                    int px = (int)Math.Max(200, Math.Min(600, rowTileW * 1.4));
                    KippixShared.SafeWork.Queue(_ => { var img = Images.Load(f, px); if (img != null) OnUi(() => tile.Cover = img); });
                }
        }

        /// <summary>Tile size from the main row's covers (so the rows match), and where the rows sit.</summary>
        private void SetRowTileSize(double w, double h)
        {
            if (CardShapes.IsTriangle(settings.CardShape)) w = Math.Round(h * 15 / 13);   // the card's slot is narrower than a triangle (they overlap): the triangle's own width
            if (Math.Abs(w - rowTileW) < 1 && Math.Abs(h - rowTileH) < 1) return;
            rowTileW = w;
            rowTileH = h;
            foreach (var row in ExtraRowsList)
            {
                row.TileHeight = h;
                int place = 0;
                foreach (var t in row.Tiles) { t.Width = w; t.Height = h; ShapeRowTile(t, place++); }
            }
            var copy = ExtraRowsList.ToList();
            int wasAt = extraRowAt;
            rowOffsets.Clear();
            ExtraRowsList.Clear();
            foreach (var r in copy) ExtraRowsList.Add(r);   // re-measure the templates
            LayoutExtraRows(false);
            if (wasAt > 0 && ExtraRowsList.Count > 0) ui.BeginInvoke(new Action(() => GoToExtraRow(Math.Min(wasAt, ExtraRowsList.Count), 0)), DispatcherPriority.Loaded);
        }

        /// <summary>Puts the current row where the games normally sit: rows above it fade away, the main row slides up and out.</summary>
        private void LayoutExtraRows(bool animate)
        {
            Notify("HomeLabelOwn");
            Notify("HomeLabelText");
            Notify("HintAText"); Notify("HintXText"); Notify("ShowXPlayHint");   // (issue #11) Running now: A Switch to, X Close
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                var host = UiTools.FindByName(win, "KX_ExtraRows");
                var list = UiTools.FindByName(win, "PART_ListGameItems") as ItemsControl;
                var root = list != null ? VisualTreeHelper.GetParent(list) as FrameworkElement : null;
                if (host == null || list == null || root == null) return;
                double coverBottom;
                double top = CoverTop(list, root, out coverBottom);
                if (double.IsNaN(top)) top = root.ActualHeight - rowTileH - 60;
                else if (mainRowShift != null) top -= mainRowShift.Y;   // where the row sits, not where it's sliding to
                double pitch = RowTitleH + rowTileH + RowGap;
                // row k (1-based) sits with its tiles where the cover row's tiles are
                double y = extraRowAt == 0 ? top + rowTileH + 70 - RowTitleH : top - RowTitleH - (extraRowAt - 1) * pitch;
                Slide(host, 0, y, animate);
                for (int i = 0; i < ExtraRowsList.Count; i++) ExtraRowsList[i].RowOpacity = i + 1 < extraRowAt ? 0 : 1;
                // the main row and its tiles: away while you're in the extra rows
                double away = extraRowAt == 0 ? 0 : -(pitch * 0.5);
                FadeTo(list, extraRowAt == 0 ? 1 : 0, animate);
                var tiles = UiTools.FindByName(win, "KX_HomeTilesCanvas");
                if (tiles != null) FadeTo(tiles, extraRowAt == 0 ? 1 : 0, animate);
                SlideMainRow(list, away, animate);
                // the carousel (Lower with Loop) goes the same way as Playnite's row
                var car = carActive ? carHost : null;
                if (car != null) { FadeTo(car, extraRowAt == 0 ? 1 : 0, animate); SlideCarousel(car, away, animate); }
            }
            catch (Exception ex) { logger.Warn("Kippix: extra rows layout: " + ex.Message); }
        }

        private static void Slide(FrameworkElement el, double x, double y, bool animate)
        {
            var t = el.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen) { t = new TranslateTransform(x, y); el.RenderTransform = t; if (!animate) return; }
            var d = new Duration(TimeSpan.FromMilliseconds(animate && !ReduceMotionNow ? 320 : 0));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, d) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease });
        }

        private static void FadeTo(UIElement el, double to, bool animate)
        {
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(animate ? 260 : 0))));
            el.IsHitTestVisible = to > 0.5;
        }

        /// <summary>The main row slides up a little as it fades (an extra translate next to Master Edit's own).</summary>
        private void SlideMainRow(FrameworkElement list, double y, bool animate)
        {
            TranslateTransform t = null;
            var group = list.RenderTransform as TransformGroup;
            if (group != null) t = group.Children.OfType<TranslateTransform>().FirstOrDefault(x => ReferenceEquals(x, mainRowShift));
            else if (ReferenceEquals(list.RenderTransform, mainRowShift)) t = mainRowShift;
            if (t == null)
            {
                if (Math.Abs(y) < 0.5) return;
                mainRowShift = new TranslateTransform();
                t = mainRowShift;
                var cur = list.RenderTransform;
                if (cur == null || ReferenceEquals(cur, Transform.Identity) || (cur.IsFrozen && cur.Value.IsIdentity)) list.RenderTransform = t;
                else if (group != null && !group.IsFrozen) group.Children.Add(t);
                else { var g2 = new TransformGroup(); g2.Children.Add(cur); g2.Children.Add(t); list.RenderTransform = g2; }
            }
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, new Duration(TimeSpan.FromMilliseconds(animate && !ReduceMotionNow ? 320 : 0))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        private TranslateTransform mainRowShift;
        private double lastRowsTop = double.NaN;
        private TranslateTransform carRowShift;

        /// <summary>The carousel slides up with Playnite's row (its own translate, beside Free move's).</summary>
        private void SlideCarousel(FrameworkElement host, double y, bool animate)
        {
            var group = host.RenderTransform as TransformGroup;
            bool attached = carRowShift != null && (ReferenceEquals(host.RenderTransform, carRowShift) || (group != null && group.Children.Contains(carRowShift)));
            if (!attached)
            {
                if (Math.Abs(y) < 0.5) return;
                carRowShift = new TranslateTransform();
                var cur = host.RenderTransform;
                if (cur == null || ReferenceEquals(cur, Transform.Identity) || (cur.IsFrozen && cur.Value.IsIdentity)) host.RenderTransform = carRowShift;
                else if (group != null && !group.IsFrozen) group.Children.Add(carRowShift);
                else { var g2 = new TransformGroup(); g2.Children.Add(cur); g2.Children.Add(carRowShift); host.RenderTransform = g2; }
            }
            carRowShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, new Duration(TimeSpan.FromMilliseconds(animate && !ReduceMotionNow ? 320 : 0))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>Down from the games into the first extra row; Up / Down between rows; Up from the first back to the games.</summary>
        private bool ExtraRowKey(KeyEventArgs e)
        {
            if (!ShowExtraRows || openPanel != null || detailsOpen) return false;
            if (IsPadRelease(e)) return extraRowAt > 0 && (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right);   // a let-go: nothing (but don't let it through either)
            if (extraRowAt > 0 && IsBackKey(e)) { PlaySound("back"); LeaveExtraRows(true); return true; }
            if (extraRowAt > 0 && e.Key == Key.X && RunningRowX(true)) return true;   // (issue #11) the keyboard's X on a Running now tile: closes it (the controller's: ThemeApi.PadInput.cs)
            if (e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right) return false;
            var win = Application.Current.MainWindow;
            var games = UiTools.FindByName(win, "PART_ListGameItems");
            var host = UiTools.FindByName(win, "KX_ExtraRows");
            if (games == null || host == null) return false;
            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (extraRowAt == 0)
            {
                if (e.Key != Key.Down || !games.IsKeyboardFocusWithin) return false;
                extraReturnGame = SelectedGameId();
                GoToExtraRow(1, 0);
                return true;
            }
            if (focused == null || !host.IsKeyboardFocusWithin) { LeaveExtraRows(false); return false; }   // the highlight went elsewhere (a click): just forget the rows
            var tile = focused.DataContext as RowTile;
            var row = ExtraRowsList.FirstOrDefault(r => tile != null && r.Tiles.Contains(tile));
            int col = row != null ? row.Tiles.IndexOf(tile) : 0;
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                if (row == null) return false;
                int next = col + (e.Key == Key.Right ? 1 : -1);
                if (next < 0 || next >= row.Tiles.Count) return true;   // stay at the ends
                PlaySound("move");
                FocusRowTile(row, next);
                return true;
            }
            if (e.Key == Key.Up)
            {
                PlaySound("move");
                if (extraRowAt <= 1) { LeaveExtraRows(true); return true; }
                GoToExtraRow(extraRowAt - 1, VisibleColumn(row, col));
                return true;
            }
            if (extraRowAt >= ExtraRowsList.Count) return true;
            PlaySound("move");
            GoToExtraRow(extraRowAt + 1, VisibleColumn(row, col));
            return true;
        }

        /// <summary>Which column on screen the tile is in (so moving up / down lands on the tile underneath).</summary>
        private int VisibleColumn(ExtraRow row, int col)
        {
            if (row == null) return 0;
            double offset = RowOffset(row);
            return Math.Max(0, col - (int)Math.Round(offset / RowTileStep));
        }

        /// <summary>From one tile to the next along a row (triangles taking turns overlap by half a tile).</summary>
        private double RowTileStep { get { return CardShapes.IsTriangle(settings.CardShape) ? rowTileW * TriStep : rowTileW + TileGapX; } }

        private readonly Dictionary<ExtraRow, double> rowOffsets = new Dictionary<ExtraRow, double>();
        private double RowOffset(ExtraRow r) { double v; return rowOffsets.TryGetValue(r, out v) ? v : 0; }

        private void GoToExtraRow(int k, int screenCol)
        {
            if (k < 1 || k > ExtraRowsList.Count) return;
            PlaySound("move");
            extraRowAt = k;
            LayoutExtraRows(true);
            var row = ExtraRowsList[k - 1];
            int col = Math.Min(row.Tiles.Count - 1, screenCol + (int)Math.Round(RowOffset(row) / RowTileStep));
            FocusRowTile(row, Math.Max(0, col));
        }

        private void FocusRowTile(ExtraRow row, int index)
        {
            if (row == null || index < 0 || index >= row.Tiles.Count) return;
            var tile = row.Tiles[index];
            extraFocused = tile;
            ScrollRowTo(row, index);
            ui.BeginInvoke(new Action(() =>
            {
                var win = Application.Current.MainWindow;
                var rows = UiTools.FindByName(win, "KX_ExtraRows") as ItemsControl;
                if (rows == null) return;
                var rc = rows.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement;
                var tilesCtl = rc != null ? UiTools.FindByName(rc, "KX_RowTiles") as ItemsControl : null;
                var c = tilesCtl != null ? tilesCtl.ItemContainerGenerator.ContainerFromItem(tile) as FrameworkElement : null;
                var btn = c != null ? UiTools.FindChild<ButtonBase>(c) as UIElement : null;
                if (btn != null) Keyboard.Focus(btn);
            }), DispatcherPriority.Loaded);
            SelectRowGameSoon(tile.GameId);
        }

        /// <summary>Slides a row along so the highlighted tile is on screen (one tile in from the edge when it can be).</summary>
        private void ScrollRowTo(ExtraRow row, int index)
        {
            double step = RowTileStep;
            double viewport = DesignWidth - 72 - 40;   // (1.0.0) the real width (wider on an ultrawide screen)
            double offset = RowOffset(row);
            double x = index * step;
            if (x - offset < step * 0.5 && index > 0) offset = Math.Max(0, x - step);
            else if (x - offset < 0) offset = x;
            if (x + rowTileW - offset > viewport - step * 0.5) offset = x + rowTileW - viewport + step;
            offset = Math.Max(0, offset);
            if (index == 0) offset = 0;
            rowOffsets[row] = offset;
            var win = Application.Current.MainWindow;
            var rows = UiTools.FindByName(win, "KX_ExtraRows") as ItemsControl;
            var rc = rows != null ? rows.ItemContainerGenerator.ContainerFromItem(row) as FrameworkElement : null;
            var panel = rc != null ? UiTools.FindByName(rc, "KX_RowTiles") : null;
            if (panel != null) Slide(panel, -offset, 0, true);
        }

        /// <summary>The highlighted tile's game becomes Playnite's selected game (background, logo and info follow it).</summary>
        private void SelectRowGameSoon(Guid id)
        {
            if (extraSelectTimer == null)
            {
                extraSelectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
                extraSelectTimer.Tick += (s, e) =>
                {
                    extraSelectTimer.Stop();
                    var t = extraFocused;
                    if (t == null || extraRowAt == 0) return;
                    try { api.MainView.SelectGame(t.GameId); } catch (Exception) { }
                };
            }
            extraSelectTimer.Stop();
            extraSelectTimer.Start();
        }

        /// <summary>Back up to the games: the game you had there is selected again.</summary>
        private void LeaveExtraRows(bool focusGames)
        {
            if (extraRowAt == 0) return;
            extraRowAt = 0;
            extraFocused = null;
            if (extraSelectTimer != null) extraSelectTimer.Stop();
            LayoutExtraRows(true);
            if (!focusGames) return;
            Guid back = extraReturnGame;
            try { if (back != Guid.Empty) api.MainView.SelectGame(back); } catch (Exception) { }
            ui.BeginInvoke(new Action(FocusSelectedCard), DispatcherPriority.Loaded);
        }

        /// <summary>A controller button being let go (Playnite sends those too).</summary>
        private static bool IsPadRelease(KeyEventArgs e)
        {
            if (e.GetType().Name != "GameControllerInputEventArgs") return false;
            var st = e.GetType().GetProperty("ButtonState");
            object v = st != null ? st.GetValue(e, null) : null;
            return v != null && v.ToString() != "Pressed";
        }

        private static bool IsBackKey(KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Back) return true;
            if (e.GetType().Name != "GameControllerInputEventArgs") return false;
            var b = e.GetType().GetProperty("Button");
            object v = b != null ? b.GetValue(e, null) : null;
            return v != null && v.ToString() == "B";
        }

        private Guid SelectedGameId()
        {
            try { var g = api.MainView.SelectedGames != null ? api.MainView.SelectedGames.FirstOrDefault() : null; return g != null ? g.Id : Guid.Empty; }
            catch (Exception) { return Guid.Empty; }
        }

        private void OpenRowTile(RowTile t)
        {
            if (t == null) return;
            if (t.RunRow) { RunningSwitchTo(t.GameId); return; }   // (issue #11) Running now: its window comes to the front
            PlaySound("select");
            extraFocused = t;
            try { api.MainView.SelectGame(t.GameId); } catch (Exception) { }
            if (settings.SkipGamePage) { if (TrySwitchInsteadOfPlay(t.GameId)) return; launchedDirectlyAt = DateTime.Now; try { api.StartGame(t.GameId); } catch (Exception) { } return; }
            ui.BeginInvoke(new Action(() =>
            {
                if (SelectedGameId() != t.GameId) return;   // Playnite's filter hides it: don't open another game's page
                extraOpenedDetails = true;
                RunMainCommand("ToggleGameDetailsCommand");
            }), DispatcherPriority.Loaded);
        }

        /// <summary>The game page closed after looking at More like this games: the game you first opened is selected again.</summary>
        private void BackToOriginGame()
        {
            var id = detOriginId;
            detOriginId = Guid.Empty;
            if (id == Guid.Empty || SelectedGameId() == id) return;
            try { api.MainView.SelectGame(id); } catch (Exception) { }
        }

        /// <summary>
        /// The game page closed back to the home screen: the highlight goes on the selected game's card, even when that game was
        /// picked on the page itself (More like this, LB / RB) and its card is off screen.
        /// </summary>
        private void HighlightAfterDetails()
        {
            CarouselDetailsClosed();
            if (IsBoardMode) BoardHoldFocusAfterDetails();   // (0.82: straight back on the tile, never via the top bar)
            var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(220) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (detailsOpen || openPanel != null || extraRowAt > 0 || confirmOpen) return;
                if (CarouselBackToTile()) return;   // the carousel: opened from a pinned game's tile, back on that tile
                if (IsBoardMode) { if (!BoardHasFocus()) FocusBoard(); return; }   // back on the Board's card
                var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
                if (list == null || list.SelectedItem == null) return;
                var c = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as UIElement;
                if (c != null && c.IsKeyboardFocused) return;   // already there
                var f = Keyboard.FocusedElement as DependencyObject;
                var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
                if (f != null && strip != null && strip.IsAncestorOf(f)) return;   // on a pinned tile (opened from there)
                FocusSelectedCard();
            };
            timer.Start();
        }

        /// <summary>The game page closed: if it was opened from an extra row, back to that tile.</summary>
        private void DetailsClosedForRows()
        {
            if (!extraOpenedDetails) return;
            extraOpenedDetails = false;
            var t = extraFocused;
            if (t == null || extraRowAt == 0) return;
            var row = ExtraRowsList.FirstOrDefault(r => r.Tiles.Contains(t));
            if (row == null) return;
            int i = row.Tiles.IndexOf(t);
            var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(260) };
            timer.Tick += (s, e) => { timer.Stop(); LayoutExtraRows(false); FocusRowTile(row, i); };
            timer.Start();
        }

        // =====================================================================
        // Tile badges
        // =====================================================================

        private readonly Dictionary<Guid, string> hltbBadges = new Dictionary<Guid, string>();
        private readonly HashSet<Guid> hltbLoading = new HashSet<Guid>();
        private Dictionary<Guid, string> achBadges;
        private object achBadgesFor;
        private DispatcherTimer badgeTimer;

        private static bool IsNewGame(Game g)
        {
            return g != null && g.Added.HasValue && (DateTime.Now - g.Added.Value).TotalDays <= 14 && g.Playtime == 0;
        }

        private string AchBadgeText(Guid id)
        {
            var data = achData;
            if (data == null || data.Count == 0) return null;
            if (!ReferenceEquals(achBadgesFor, data) || achBadges == null)
            {
                var d = new Dictionary<Guid, string>();
                foreach (var g in data)
                {
                    if (g == null || g.Items == null || g.Items.Count == 0) continue;
                    int un = g.Items.Count(i => i != null && i.Unlocked);
                    d[g.Id] = Math.Round(100.0 * un / g.Items.Count) + "%";
                }
                achBadges = d;
                achBadgesFor = data;
            }
            string t;
            return achBadges.TryGetValue(id, out t) ? t : null;
        }

        /// <summary>HowLongToBeat main story time (from the HowLongToBeat add-on's files), read in the background the first time.</summary>
        private string HltbBadgeText(Guid id, bool fetch)
        {
            lock (hltbBadges)
            {
                string t;
                if (hltbBadges.TryGetValue(id, out t)) return t;
                if (!fetch || hltbLoading.Contains(id)) return null;
                hltbLoading.Add(id);
            }
            string ext = api.Paths.ExtensionsDataPath;
            KippixShared.SafeWork.Queue(_ =>
            {
                string text = null;
                try
                {
                    var h = KippixOverlay.GameData.LoadHltb(ext, id);
                    // the main story; a game with only the longer times known shows the shortest of those
                    double best = h == null ? 0 : h.Story > 0 ? h.Story : h.Extras > 0 ? h.Extras : h.Completionist;
                    if (best > 0) text = (best < 1 ? "<1" : Math.Round(best).ToString(System.Globalization.CultureInfo.InvariantCulture)) + " h";
                }
                catch (Exception) { }
                lock (hltbBadges) { hltbBadges[id] = text; hltbLoading.Remove(id); }
                if (text != null && settings.HomeHltb) OnUi(HomeHltbRefresh);   // (0.96: the home screen's How long to beat)
                if (text != null) { RefreshBadgesSoon(); if (LayoutNow == 2) RefreshTileArtSoon(); if (IsBoardMode) BoardRefillSoon(); }   // the list's play info too
            });
            return null;
        }

        private void RefreshBadgesSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (badgeTimer == null)
                {
                    badgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    badgeTimer.Tick += (s, e) => { badgeTimer.Stop(); UpdateTileBadges(); };
                }
                if (!badgeTimer.IsEnabled) badgeTimer.Start();   // at most every 300 ms, also while you scroll
            }));
        }

        private ListBox badgesList;
        private bool badgesEverOn;
        private string badgeSigWas;

        /// <summary>(0.99.17) From the tick: the badge settings changed some other way (a preset, a layout, Getting started): the tiles again.</summary>
        private void BadgeSettingsTick()
        {
            string sig = string.Join("|", settings.BadgeNew, settings.BadgeAch, settings.BadgeHltb, settings.BadgeFeatures, settings.BadgeFeatHide, settings.BadgeStore, settings.SlotPinBadge, settings.HomeLayoutMode, settings.BadgeRunning);
            if (sig == badgeSigWas) return;
            bool first = badgeSigWas == null;
            badgeSigWas = sig;
            if (!first || AnyBadges) RefreshBadgesSoon();
        }

        /// <summary>The badges on the main row's tiles (set on each tile's template parts; tiles come and go as the row scrolls).</summary>
        private void UpdateTileBadges()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var list = win != null ? UiTools.FindByName(win, "PART_ListGameItems") as ListBox : null;
                if (list == null) return;
                if (!ReferenceEquals(badgesList, list))
                {
                    badgesList = list;
                    list.ItemContainerGenerator.StatusChanged += (s, e) => { if (AnyBadges || badgesEverOn) RefreshBadgesSoon(); };
                    list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => { if (AnyBadges || badgesEverOn) RefreshBadgesSoon(); }));
                }
                bool any = AnyBadges;
                if (any) badgesEverOn = true;   // (0.99.17) once any were on, tiles that come back into view are always checked, so a switched-off badge can't linger on a reused tile
                for (int i = 0; i < list.Items.Count; i++)
                {
                    var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (c == null) continue;
                    BadgeCard(c, EntryId(list.Items[i]), any);
                }
                if (carActive) CarDressSoon();   // the carousel's cards (when the badges changed)
            }
            catch (Exception ex) { logger.Warn("Kippix: badges: " + ex.Message); }
        }

        /// <summary>One card's badges (Playnite's row, or the carousel's cards).</summary>
        private void BadgeCard(FrameworkElement c, Guid id, bool any)
        {
            Game g = null;
            if (any && id != Guid.Empty) try { g = api.Database.Games.Get(id); } catch (Exception) { }
            SetBadge(c, "KX_BadgeNew", any && settings.BadgeNew && IsNewGame(g), null, null);
            string ach = any && settings.BadgeAch && g != null ? AchBadgeText(id) : null;
            SetBadge(c, "KX_BadgeAch", ach != null, "KX_BadgeAchText", ach);
            string h = any && settings.BadgeHltb && g != null ? HltbBadgeText(id, true) : null;
            SetBadge(c, "KX_BadgeHltb", h != null, "KX_BadgeHltbText", h);
            SetBadge(c, "KX_BadgeSlot", settings.SlotPinBadge && id != Guid.Empty && SlotOf(id) > 0, null, null);
            // (issue #11) "Running": Playnite's own IsRunning shows it in the theme; what only Kippix's list knows is running (an app
            // Playnite marks as stopped at once) is said here, on the pill's Tag (a card used again for another game: taken off)
            var run = UiTools.FindByName(c, "KX_BadgeRunning");
            object runTag = settings.BadgeRunning && id != Guid.Empty && Running.IsRunning(id) ? "on" : null;
            if (run != null && !Equals(run.Tag, runTag)) run.Tag = runTag;
            BadgeCardExtras(c, any ? g : null);   // (r12) feature icons and store pill (ThemeApi.Badges.cs)
        }

        private bool AnyBadges { get { return (settings.BadgeRunning && Running.Any) || settings.BadgeNew || settings.BadgeAch || settings.BadgeHltb || settings.BadgeFeatures || settings.BadgeStore || (settings.SlotPinBadge && settings.SlotPins != null && settings.SlotPins.Count > 0); } }

        private static void SetBadge(FrameworkElement container, string name, bool show, string textName, string text)
        {
            var b = UiTools.FindByName(container, name);
            if (b == null) return;
            var v = show ? Visibility.Visible : Visibility.Collapsed;
            if (b.Visibility != v) b.Visibility = v;
            if (show && textName != null)
            {
                var t = UiTools.FindByName(container, textName) as TextBlock;
                if (t != null && t.Text != text) t.Text = text;
            }
        }
    }
}
