using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Home settings › Game cards and layout › Games shown as: Folder. The home screen shows folder tiles (All games first, then
    /// yours in the order they were made), drawn by Row's carousel in the Row design; A opens one and its games show in the
    /// folder design (Row, Grid or List), B goes back to the tiles onto the folder you came from.
    /// A folder's games are whole lists (the Library tile's lists) and games picked by hand. Playnite's list shows exactly them
    /// through the one list filter (ApplyListFilter), in place of Playnite's own filter / saved filter / search (those aren't
    /// changed, and apply again once Folder isn't chosen); Playnite's hidden games, a profile's limits and each folder's own
    /// Installed games choice still apply (to the counts too), and Sort by gives the folder its own order. What's in each folder is worked out on a
    /// worker thread and kept until the library, a saved filter, a collection, the profile or the folder changes.
    /// Folders aren't moved or edited on the home screen: their name, photo and games are set on the settings pages here.
    /// </summary>
    public partial class ThemeApi
    {
        internal const string AllGamesId = "all";

        // =====================================================================
        // Where we are
        // =====================================================================

        private string folderOpenId;                 // the folder whose games are on the home screen (null: the folder tiles)
        private string folderLastId;                 // the folder the highlight goes back to on the tiles
        private HashSet<Guid> folderFilter;          // Playnite's list shows exactly these, its own filter set aside (null: no folder open)
        private List<HashSet<Guid>> folderGroups;    // the open folder's lists, each on its own (Show duplicates)

        private bool FolderMode { get { return settings.HomeLayoutMode == 5; } }
        private bool FolderTilesShown { get { return FolderMode && folderOpenId == null; } }
        private bool FolderInside { get { return FolderMode && folderOpenId != null; } }

        /// <summary>
        /// The layout the home screen is drawn in now: the setting, except Folder, whose tiles are drawn by Row's carousel (always
        /// like Full Screen) and whose opened folder uses the folder design (0 Row, 1 Grid, 2 List, 3 Board, 4 Manual Board).
        /// And the library view (ThemeApi.Justified.cs): a mosaic of every game over the layout that's set, until B.
        /// </summary>
        private int LayoutNow
        {
            get
            {
                if (LibraryOpen) return 6;
                if (settings.HomeLayoutMode != 5) return settings.HomeLayoutMode;
                return folderOpenId == null ? 0 : Math.Max(0, Math.Min(4, settings.FolderGameDesign));
            }
        }

        private List<HomeFolder> FolderList
        {
            get
            {
                // (a profile or a hand-edited file can bring in folders with parts missing: made whole before anything reads them)
                if (settings.Folders == null || settings.Folders.Count == 0 || settings.Folders[0] == null || settings.Folders[0].Id != AllGamesId
                    || settings.Folders.Any(f => f == null || f.Id == null || f.Sources == null || f.Games == null || f.Name == null || f.Picture == null || f.Installed < 0 || f.Installed > 2)) NormalizeFolders();
                return settings.Folders;
            }
        }

        private HomeFolder FolderById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return FolderList.FirstOrDefault(f => f != null && f.Id == id);
        }

        private string FolderName(HomeFolder f)
        {
            if (f == null) return "";
            if (!string.IsNullOrWhiteSpace(f.Name)) return f.Name;
            return f.Id == AllGamesId ? Loc("LOC_KX_FolderAllGames", "All games") : Loc("LOC_KX_FolderNoName", "A folder");
        }

        /// <summary>All games first (always there), every folder whole, nothing twice.</summary>
        private void NormalizeFolders()
        {
            if (settings.Folders == null) settings.Folders = new List<HomeFolder>();
            settings.Folders.RemoveAll(f => f == null || string.IsNullOrEmpty(f.Id));
            var seen = new HashSet<string>();
            settings.Folders.RemoveAll(f => !seen.Add(f.Id));
            foreach (var f in settings.Folders)
            {
                if (f.Name == null) f.Name = "";
                if (f.Picture == null) f.Picture = "";
                f.Sources = (f.Sources ?? new List<string>()).Where(k => !string.IsNullOrEmpty(k) && k != "random").Distinct().ToList();
                f.Games = (f.Games ?? new List<string>()).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
                // Installed games was one choice for every folder before 0.74: a folder without its own takes that one (once)
                if (f.Installed < 0 || f.Installed > 2) f.Installed = Math.Max(0, Math.Min(2, settings.FolderInstalled));
            }
            var all = settings.Folders.FirstOrDefault(f => f.Id == AllGamesId);
            if (all == null) all = new HomeFolder { Id = AllGamesId };
            else settings.Folders.Remove(all);
            all.Sources = new List<string>();   // (All games is every game: nothing to choose)
            all.Games = new List<string>();
            settings.Folders.Insert(0, all);
            foreach (var f in settings.Folders) if (f.Id != AllGamesId) f.Removed = false;   // (only All games can be taken off)
            if (settings.Folders.Count == 1) all.Removed = false;   // the only folder: never taken off
        }

        /// <summary>All games has been taken off the home screen (Remove this folder in its page).</summary>
        private bool AllGamesRemoved
        {
            get
            {
                var list = FolderList;
                return list.Count > 1 && list[0].Removed;
            }
        }

        /// <summary>The folders on the home screen: every folder but a removed All games.</summary>
        private List<HomeFolder> FolderTiles
        {
            get
            {
                var list = FolderList;
                return AllGamesRemoved ? list.Skip(1).ToList() : list;
            }
        }

        // =====================================================================
        // Settings (Game cards and layout › FOLDERS)
        // =====================================================================

        public bool IsFolderMode { get { return FolderMode; } }
        /// <summary>The ROW section: Row, and Folder with its games in a Row (the folder tiles have their own look).</summary>
        // (0.99) Row's Design (Lower / Tall / Full Screen) sits under HOW THE GAMES ARE LAID OUT, above Preset, for Row; for a folder's
        // games shown as a row it stays in that ROW section
        public bool RowDesignTopShown { get { return settings.HomeLayoutMode == 0; } }
        public bool RowDesignFolderShown { get { return settings.HomeLayoutMode == 5 && settings.FolderGameDesign == 0; } }
        public bool RowSettingsShown { get { return settings.HomeLayoutMode == 0 || (settings.HomeLayoutMode == 5 && settings.FolderGameDesign == 0); } }
        public bool GridSettingsShown { get { return settings.HomeLayoutMode == 1 || (settings.HomeLayoutMode == 5 && settings.FolderGameDesign == 1); } }
        public bool ListSettingsShown { get { return settings.HomeLayoutMode == 2 || (settings.HomeLayoutMode == 5 && settings.FolderGameDesign == 2); } }

        public string FoldersSectionTitle { get { return Loc("LOC_KX_SecFolders", "FOLDERS"); } }
        public string FolderDesignLabel { get { return Loc("LOC_KX_FolderDesign", "Games in a folder"); } }
        public string FolderDesignText
        {
            get
            {
                return settings.FolderGameDesign == 1 ? Loc("LOC_KX_LayoutGrid", "Grid")
                     : settings.FolderGameDesign == 2 ? Loc("LOC_KX_LayoutList", "List")
                     : settings.FolderGameDesign == 3 ? Loc("LOC_KX_LayoutBoard", "Board")
                     : settings.FolderGameDesign == 4 ? Loc("LOC_KX_LayoutManualBoard", "Manual Board")
                     : Loc("LOC_KX_LayoutRow", "Row");
            }
        }
        public string FolderDesignSub
        {
            get
            {
                return settings.FolderGameDesign == 1 ? Loc("LOC_KX_FolderDesignGridSub", "An opened folder's games as a grid (the Grid settings below)")
                     : settings.FolderGameDesign == 2 ? Loc("LOC_KX_FolderDesignListSub", "An opened folder's games as a list down one side (the List settings below)")
                     : settings.FolderGameDesign == 3 ? Loc("LOC_KX_FolderDesignBoardSub", "An opened folder's games on the Board (its tiles show only the folder's games)")
                     : settings.FolderGameDesign == 4 ? Loc("LOC_KX_FolderDesignManualSub", "An opened folder's games on the Manual Board (only the folder's games)")
                     : Loc("LOC_KX_FolderDesignRowSub2", "An opened folder's games as a row, in the Row settings below");
            }
        }
        public string FolderDupLabel { get { return Loc("LOC_KX_FolderDuplicates", "Show duplicates"); } }
        public string FolderDupSub { get { return Loc("LOC_KX_FolderDuplicatesSub", "A game in more than one of a folder's lists shows once for each (Row's Loop, Tall and Full Screen; elsewhere it shows once)"); } }
        public string FoldersRowLabel { get { return Loc("LOC_KX_FoldersTitle", "Your folders"); } }
        public string FoldersSummary
        {
            get
            {
                int n = FolderList.Count - 1;
                if (AllGamesRemoved) return n == 1 ? Loc("LOC_KX_FoldersOneNoAll", "1 folder of yours") : string.Format(Loc("LOC_KX_FoldersNNoAll", "{0} folders of yours"), n);
                if (n <= 0) return Loc("LOC_KX_FoldersOnlyAll", "All games only");
                return n == 1 ? Loc("LOC_KX_FoldersOneMore", "All games and 1 folder of yours") : string.Format(Loc("LOC_KX_FoldersNMore", "All games and {0} folders of yours"), n);
            }
        }
        /// <summary>Game cards and layout › Games shown as, the line under it.</summary>
        private string FoldersShortSummary { get { return string.Format(Loc("LOC_KX_FolderSummaryShort", "Folder, {0} folders"), FolderTiles.Count); } }
        /// <summary>The filter name's place: the open folder's name (the folder tiles: "Folders").</summary>
        private string FolderLabelNow { get { var f = FolderInside ? FolderById(folderOpenId) : null; return f != null ? FolderName(f) : Loc("LOC_KX_FoldersLabel", "Folders"); } }

        public bool FolderDuplicates
        {
            get { return settings.FolderDuplicates; }
            set
            {
                if (settings.FolderDuplicates == value) return;
                settings.FolderDuplicates = value;
                SaveSettings();
                Notify("FolderDuplicates");
                try { if (carActive && FolderInside) CarResync(); }   // (the carousel's own order, with or without them)
                catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
            }
        }

        // ---- Folders on screen: the folder tiles are always drawn like Row › Full Screen, with their own number across ----
        public string FolderTileCountLabel { get { return Loc("LOC_KX_FolderTileCount", "Folders on screen"); } }
        public string FolderTileCountSub { get { return Loc("LOC_KX_FolderTileCountSub", "The folder tiles, edge to edge across the screen: more makes them narrower"); } }
        public string FolderTileCountText { get { return Math.Max(FullCountMin, Math.Min(FullCountMax, settings.FolderTileCount)).ToString(); } }
        private ICommand cycleFolderTileCountCmd;
        public ICommand CycleFolderTileCountCommand
        {
            get
            {
                return cycleFolderTileCountCmd ?? (cycleFolderTileCountCmd = new RelayCommand(_ =>
                {
                    int now = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.FolderTileCount));
                    settings.FolderTileCount = now >= FullCountMax ? FullCountMin : now + 1;
                    SaveSettings();
                    PlaySound("move");
                    Notify("FolderTileCountText");
                    ApplyHomeLayoutSoon();
                }));
            }
        }

        private ICommand cycleFolderDesignCmd;
        public ICommand CycleFolderDesignCommand
        {
            get
            {
                return cycleFolderDesignCmd ?? (cycleFolderDesignCmd = new RelayCommand(_ =>
                {
                    settings.FolderGameDesign = (Math.Max(0, Math.Min(4, settings.FolderGameDesign)) + 1) % 5;   // Row, Grid, List, Board, Manual Board (never Folder)
                    carWhy = "Games in a folder changed";
                    SaveSettings();
                    PlaySound("move");
                    HomeModeChanged();
                }));
            }
        }

        // ---- Installed games: each folder's own (Your folders › the folder's page), its games and its count on the folder tile ----
        private static int FolderInstalledOf(HomeFolder f) { return f == null || f.Installed < 0 || f.Installed > 2 ? 0 : f.Installed; }

        private string FolderInstalledName(int v)
        {
            return v == 1 ? Loc("LOC_KX_FolderInstalledOnly", "Installed only") : v == 2 ? Loc("LOC_KX_FolderInstalledNot", "Not installed") : Loc("LOC_KX_FolderInstalledAll", "All");
        }

        // ---- Sort by (GAMES IN A FOLDER): every folder ----
        public string FolderSortLabel { get { return Loc("LOC_KX_FolderSort", "Sort by"); } }
        public string FolderSortSub { get { return Loc("LOC_KX_FolderSortSub", "The order of an opened folder's games"); } }
        public string FolderSortText
        {
            get
            {
                switch (FolderSortNow)
                {
                    case 1: return Loc("LOC_KX_FolderSortPlayed", "Recently played");
                    case 2: return Loc("LOC_KX_FolderSortMost", "Most played");
                    case 3: return Loc("LOC_KX_FolderSortAdded", "Recently added");
                    case 4: return Loc("LOC_KX_FolderSortPlaynite", "As Playnite sorts");
                    default: return Loc("LOC_KX_FolderSortName", "Name");
                }
            }
        }
        private ICommand cycleFolderSortCmd;
        public ICommand CycleFolderSortCommand
        {
            get
            {
                return cycleFolderSortCmd ?? (cycleFolderSortCmd = new RelayCommand(_ =>
                {
                    settings.FolderSort = (FolderSortNow + 1) % 5;   // Name, Recently played, Most played, Recently added, As Playnite sorts
                    SaveSettings();
                    PlaySound("move");
                    Notify("FolderSortText");
                    FolderSortApply();
                }));
            }
        }

        private void NotifyFolderSettings()
        {
            foreach (string n in new[] { "IsFolderMode", "RowSettingsShown", "RowDesignTopShown", "RowDesignFolderShown", "GridSettingsShown", "ListSettingsShown", "FolderDesignText", "FolderDesignSub", "FolderDuplicates",
                                         "FolderSortText", "PlayniteFilterMarksShown", "HomeSearchShown", "FilterAbove",
                                         "FoldersSummary", "HomeLabelOwn", "HomeLabelText", "HomeLayoutText", "HomeRowsSummary", "IsRowLower", "FolderTileCountText",
                                         "BoardSettingsShown", "IsNotBoardMode", "ShowCardLook", "ShowCardSpacing", "ShowTriArrange", "ShowTriSpacing" })
                Notify(n);
        }

        // =====================================================================
        // Opening and closing a folder
        // =====================================================================

        /// <summary>From HomeModeChanged: Folder chosen or not, a folder gone (a layout or profile came in): the home screen follows.</summary>
        private void FolderApplyMode()
        {
            try
            {
                if (folderFadeSwitch != null) FolderFadeSwitchNow(folderFadeGen);   // (a folder opening or closing behind a fade: done first)
                if (folderOpenId != null && (!FolderMode || FolderById(folderOpenId) == null))
                {
                    folderOpenId = null;
                    folderFilter = null;
                    folderGroups = null;
                    FolderSortApply();   // (Playnite's own sort back)
                    ApplyListFilter(true);
                    if (HomeTiles != null && IsFullscreen) BuildHomeTiles();
                }
                else if (folderOpenId == null && folderFilter != null) { folderFilter = null; folderGroups = null; FolderSortApply(); ApplyListFilter(true); }
                if (FolderMode && IsFullscreen) { FolderHookDb(); FolderRefreshSoon(); }
                FolderBackgroundFollow();
                NotifyFolderSettings();
                FolderPreviewSoon();   // (Folder chosen / no longer chosen on the Game cards page: the preview behind it follows)
            }
            catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
        }

        /// <summary>A on a folder tile: its games on the home screen, in the folder design (after the tiles have faded out).</summary>
        private void OpenFolder(string id)
        {
            var f = FolderById(id);
            if (f == null || !FolderMode || folderOpenId != null || folderFadeSwitch != null) return;
            if (openPanel == null) FolderPreviewForget();   // (your own move: the settings' preview doesn't put anything back afterwards)
            carTileReturn = -1;
            PlaySound("select");
            FolderTransition(true, () => OpenFolderNow(id));
        }

        private void OpenFolderNow(string id)
        {
            var f = FolderById(id);
            if (f == null || !FolderMode || folderOpenId != null) return;
            carTileReturn = -1;   // (the carousel's "back on the tile after the game page" isn't for a folder)
            var r = FolderResultFor(f);
            folderOpenId = f.Id;
            folderLastId = f.Id;
            folderGroups = r != null ? r.Groups : null;
            // (All games too: every game, not Playnite's filtered list; only if it couldn't be worked out, Playnite's list as it is)
            folderFilter = r != null && r.All != null ? r.All : f.Id == AllGamesId ? null : new HashSet<Guid>();
            FolderSwitched(true);
        }

        /// <summary>Back to the folder tiles, with the highlight on the folder you were in. False when no folder is open.</summary>
        private bool CloseFolder(bool sound)
        {
            if (openPanel == null) FolderPreviewForget();
            if (folderFadeSwitch != null)
            {
                // still fading out: a folder about to open stays shut (back as it was); one already closing just carries on
                if (folderFadeOpening) { if (sound) PlaySound("back"); folderFadeSwitch = null; FolderFadeReset(); }
                return true;
            }
            if (folderOpenId == null) return false;
            if (sound) PlaySound("back");
            FolderTransition(false, CloseFolderNow);
            return true;
        }

        private void CloseFolderNow()
        {
            if (folderOpenId == null) return;
            folderLastId = folderOpenId;
            folderOpenId = null;
            folderFilter = null;
            folderGroups = null;
            carSeq = null;
            FolderSwitched(false);
        }

        private void FolderSwitched(bool entering)
        {
            carWhy = entering ? "a folder was opened" : "back to the folder tiles";
            if (HomeTiles != null) BuildHomeTiles();   // (the tiles before the games: before the folder tiles, and before a folder's games as on the home screen)
            if (carActive) CarBuildExtras();
            FolderSortApply();       // (the folder's own order in, or Playnite's sort back, before the first game is chosen)
            ApplyListFilter(true);
            FolderBackgroundFollow();
            if (entering)
            {
                var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
                if (list != null && list.Items.Count > 0) list.SelectedIndex = 0;   // a folder opens on its first game
                boardPage = 0;          // (a folder shown as a Board: from its first page)
                boardFocusId = null;
            }
            HomeModeChanged();
            if (!folderPreviewSwitching) FolderFocusSoon(entering, 0);   // (the settings' preview: the highlight stays where it is)
        }

        private void FolderFocusSoon(bool entering, int tries)
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                try { FolderFocusAfter(entering, tries); } catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
            }), tries == 0 ? DispatcherPriority.ContextIdle : DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// After a folder opened or closed: the highlight on its first game, or back on the folder tile you came from. The carousel
        /// may still be on its way (it's laid out after the change): tried again a few times until the folder tile can take it.
        /// </summary>
        private void FolderFocusAfter(bool entering, int tries)
        {
            if (openPanel != null || detailsOpen || confirmOpen || kbOpen) return;
            if (entering)
            {
                if (!FolderInside) return;
                if (!IsBoardMode) { FocusSelectedCard(); return; }
                // shown as a Board: its tiles are made a moment later (the games are sorted onto them on a worker thread)
                if (!BoardHasFocus() && !FocusBoard() && tries < 6)
                {
                    var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(300) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        try { FolderFocusAfter(true, tries + 1); } catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
                    };
                    t.Start();
                }
                return;
            }
            if (!FolderTilesShown) return;
            if (!carActive || carProxy == null || !carProxy.IsVisible)
            {
                if (tries < 6) FolderFocusSoon(false, tries + 1);
                return;
            }
            int seq = FolderTileSeq();
            carIndex = seq;
            CarShowSeq(seq, false);
            if (!carProxy.IsKeyboardFocused) Keyboard.Focus(carProxy);
            if (!carProxy.IsKeyboardFocused && tries < 6) FolderFocusSoon(false, tries + 1);
        }

        /// <summary>
        /// Folder: "home" (the top bar's Home, hold B, the Guide button or Select + Start): out of an opened folder and onto the
        /// folder tiles, the highlight on the folder you were in. True when it dealt with it (Folder is chosen).
        /// </summary>
        private bool FolderGoHome()
        {
            if (!FolderMode || !IsFullscreen) return false;
            FolderPreviewForget();   // (home from the settings: the folder tiles, not what was showing before them)
            try { if (!CloseFolder(false)) FolderFocusSoon(false, 0); }   // (on the tiles already: back onto the folder tile)
            catch (Exception ex) { logger.Warn("Kippix: folders, home: " + ex.Message); }
            return true;
        }

        /// <summary>B in a folder (on the home screen, nothing else open): back to the folder tiles.</summary>
        private bool FolderBackKey(KeyEventArgs e)
        {
            if (PadButton(e) == Playnite.SDK.Events.ControllerInput.None && FolderFilterKey(e)) return true;   // (the keyboard's F2 / F3 / F / R: nothing in Folder)
            if ((!FolderInside && !(folderFadeSwitch != null && folderFadeOpening)) || confirmOpen || kbOpen || freeMove || IsMasterMoving || descReaderOpen || masterMoving != null || widgetMoving != null || boardEditing) return false;
            if (e.IsRepeat || !IsBackKey(e) || IsPadRelease(e)) return false;
            return CloseFolder(true);
        }

        /// <summary>
        /// Folder chosen, on the home screen (the folder tiles or a folder's games): Playnite's saved filters and filter screens don't
        /// change what's shown here (a folder always shows exactly its own games), so what would switch them does nothing: LB / RB
        /// (Playnite's previous / next saved filter), the sticks' clicks (its filters, choosing a saved filter) and F2 / F3 / F / R on
        /// the keyboard. Before, LB / RB still switched Playnite's saved filter: the folder's games stayed, but in that filter's order
        /// (e.g. Recently played first) and the list jumped back to its start. Only where Playnite's own bindings would act (the
        /// highlight in its library view); Free move, the Board's editing and every panel keep their own LB / RB.
        /// </summary>
        private bool FolderFilterKey(KeyEventArgs e)
        {
            if (!FolderMode || !IsFullscreen || openPanel != null || detailsOpen || confirmOpen || kbOpen || descReaderOpen || freeMove || IsMasterMoving || boardEditing) return false;
            var b = PadButton(e);
            bool filterKey;
            if (b != Playnite.SDK.Events.ControllerInput.None)
                filterKey = !IsPadRelease(e) && (b == Playnite.SDK.Events.ControllerInput.LeftShoulder || b == Playnite.SDK.Events.ControllerInput.RightShoulder
                                                || b == Playnite.SDK.Events.ControllerInput.LeftStick || b == Playnite.SDK.Events.ControllerInput.RightStick);
            else filterKey = e.Key == Key.F2 || e.Key == Key.F3 || ((e.Key == Key.F || e.Key == Key.R) && Keyboard.Modifiers == ModifierKeys.None);
            if (!filterKey) return false;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var host = win != null ? UiTools.FindByName(win, "PART_ViewHost") : null;
            var f = Keyboard.FocusedElement as DependencyObject;
            return host != null && f != null && host.IsAncestorOf(f);
        }

        // =====================================================================
        // The preview behind Game cards and layout (Folder): the home screen shows what the rows with the highlight are about.
        // On the page and on FOLDERS' rows: the folder tiles. On GAMES IN A FOLDER and the sections for its design (Row, Grid, List,
        // Board, Card look: the panels tagged KX_FolderGames in Main.xaml): a folder open (the one you were last in, else All games).
        // Its sub-pages: Your folders and its pages the tiles, the others (Board settings, List info...) the folder's games. Switches
        // are made straight away, without a sound or a fade, and never move the highlight out of the settings. When the settings close
        // (or leave the page) the home screen goes back to how it was before them, unless an editor started from them is now
        // working on the folder's games (Edit the board), Folder is no longer chosen, or you went home / opened or closed a folder.
        // =====================================================================

        private bool folderPreviewOn;            // the Game cards page (or one under it) is open with Folder chosen
        private bool folderPreviewTouched;       // the preview opened or closed a folder (there's something to put back)
        private bool folderPreviewFresh;         // just started: the first row with the highlight decides (else the folder tiles)
        private bool folderPreviewBackHome;      // before the settings the highlight was on the home screen's games / tiles
        private string folderPreviewWas;         // the folder open before the settings (null: the folder tiles)
        private string folderPreviewLastWas;     // ... and the folder tile the highlight was going back to
        private Guid folderPreviewGame;          // ... and the game selected in it
        private int folderPreviewGen;
        private bool folderPreviewPosted, folderPreviewShowPosted;
        private int folderPreviewWant;           // the view asked for by the highlight: 0 the folder tiles, 1 a folder's games

        private bool FolderPreviewWanted
        {
            get { return IsFullscreen && ui != null && FolderMode && openPanel == "options" && !detailsOpen && IsPageUnder(optionsPage, "cards"); }
        }

        /// <summary>The page, the panel or Games shown as changed: the preview starts, follows the page or ends (after the change is made).</summary>
        private void FolderPreviewSoon()
        {
            if (ui == null || folderPreviewPosted) return;
            folderPreviewPosted = true;
            ui.BeginInvoke(new Action(() =>
            {
                folderPreviewPosted = false;
                try { FolderPreviewFollow(); } catch (Exception ex) { logger.Warn("Kippix: folder preview: " + ex.Message); }
            }), DispatcherPriority.Normal);
        }

        private void FolderPreviewFollow()
        {
            if (!FolderPreviewWanted)
            {
                if (folderPreviewOn) FolderPreviewEnd();
                return;
            }
            if (!folderPreviewOn)
            {
                if (folderFadeSwitch != null) FolderFadeSwitchNow(folderFadeGen);   // (a folder opening or closing: done first)
                folderPreviewOn = true;
                folderPreviewTouched = false;
                folderPreviewKeep = false;
                folderPreviewFresh = true;
                folderPreviewGen++;
                folderPreviewWas = folderOpenId;
                folderPreviewLastWas = folderLastId;
                folderPreviewGame = FolderInside ? FolderSelectedGame() : Guid.Empty;
                folderPreviewBackHome = FolderHomeHasFocus(focusBeforePanel as DependencyObject);
            }
            if (optionsPage != "cards") { folderPreviewFresh = false; FolderPreviewShow(IsPageUnder(optionsPage, "folders") ? 0 : 1); return; }
            if (!folderPreviewFresh) return;   // (back on the page from one under it: the row the highlight lands on decides)
            folderPreviewFresh = false;
            // just opened (or Folder just chosen on it): once the highlight is on its row, that row decides; the page's own rows show the tiles
            int gen = folderPreviewGen;
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (gen != folderPreviewGen || !folderPreviewOn || optionsPage != "cards") return;
                    int area = FolderPreviewAreaOf(Keyboard.FocusedElement as DependencyObject);
                    FolderPreviewShow(area < 0 ? 0 : area);
                }
                catch (Exception ex) { logger.Warn("Kippix: folder preview: " + ex.Message); }
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>From the window's focus handler (the settings open): the highlight moved onto a row of the Game cards page.</summary>
        private void FolderPreviewFocusChanged(DependencyObject focused)
        {
            if (!folderPreviewOn || openPanel != "options" || optionsPage != "cards" || ui == null) return;
            int area = FolderPreviewAreaOf(focused);
            if (area < 0) return;   // (a row that's about neither: what shows stays)
            folderPreviewWant = area;
            if (folderPreviewShowPosted) return;
            folderPreviewShowPosted = true;
            // (after the focus change, before the next key)
            ui.BeginInvoke(new Action(() =>
            {
                folderPreviewShowPosted = false;
                try { if (folderPreviewOn && openPanel == "options" && optionsPage == "cards") FolderPreviewShow(folderPreviewWant); }
                catch (Exception ex) { logger.Warn("Kippix: folder preview: " + ex.Message); }
            }), DispatcherPriority.Normal);
        }

        /// <summary>Which view a settings row is about: 0 the folder tiles, 1 a folder's games, -1 neither (from its panel's Tag).</summary>
        private static int FolderPreviewAreaOf(DependencyObject d)
        {
            for (int i = 0; d != null && i < 80; i++)
            {
                var fe = d as FrameworkElement;
                if (fe != null)
                {
                    var tag = fe.Tag as string;
                    if (tag == "KX_FolderGames") return 1;
                    if (tag == "KX_FolderTiles") return 0;
                    if (fe.Name == "KX_OptionsPanel") return -1;
                }
                DependencyObject p = null;
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) p = VisualTreeHelper.GetParent(d);
                if (p == null) p = LogicalTreeHelper.GetParent(d);
                d = p;
            }
            return -1;
        }

        /// <summary>The home screen behind the settings: the folder tiles (0) or a folder's games (1), straight away.</summary>
        private void FolderPreviewShow(int area)
        {
            if (!folderPreviewOn || !FolderMode || !IsFullscreen) return;
            if (area == 1)
            {
                if (FolderInside) return;
                var last = FolderById(folderLastId);
                string id = last != null && !(last.Id == AllGamesId && AllGamesRemoved) ? folderLastId : FolderTiles[0].Id;   // (All games, unless it's been removed)
                FolderPreviewSwitch(() =>
                {
                    OpenFolderNow(id);
                    if (id == folderPreviewWas) FolderSelectGame(folderPreviewGame);   // (the folder you were in: on the game you were on)
                });
            }
            else
            {
                if (!FolderInside) return;
                FolderPreviewSwitch(CloseFolderNow);
            }
        }

        private bool folderPreviewSwitching;     // (FolderSwitched leaves the highlight where it is)

        private void FolderPreviewSwitch(Action sw)
        {
            if (folderFadeSwitch != null) FolderFadeSwitchNow(folderFadeGen);
            folderFadeGen++;          // (no fade, and none of a fade's timers left to run)
            FolderFadeReset();
            folderPreviewTouched = true;
            folderPreviewSwitching = true;
            try { sw(); }
            finally { folderPreviewSwitching = false; }
        }

        /// <summary>
        /// Edit the board / Paste to… (from these settings): the settings close and the board's editing starts a moment later, on the
        /// folder's games showing now, so they stay.
        /// </summary>
        private void FolderPreviewKeep() { if (folderPreviewOn) folderPreviewKeep = true; }
        private bool folderPreviewKeep;

        /// <summary>
        /// The settings closed or left the page (from FolderPreviewSoon, just after): the home screen as it was before them, before
        /// the closing puts the highlight back on it.
        /// </summary>
        private void FolderPreviewEnd()
        {
            bool keep = folderPreviewKeep;
            folderPreviewOn = false;
            folderPreviewFresh = false;
            folderPreviewKeep = false;
            if (!folderPreviewTouched || ui == null) return;
            folderPreviewTouched = false;
            int gen = ++folderPreviewGen;
            if (keep || boardEditing) { folderPreviewBackHome = false; return; }   // (an editor working on the folder's games: they stay)
            FolderPreviewRestore(gen);
        }

        private void FolderPreviewRestore(int gen)
        {
            if (gen != folderPreviewGen || folderPreviewOn) return;
            if (!FolderMode || !IsFullscreen || detailsOpen) return;   // (Folder no longer chosen: as it is)
            string want = folderPreviewWas != null && FolderById(folderPreviewWas) != null ? folderPreviewWas : null;
            string last = folderPreviewLastWas;
            Guid game = folderPreviewGame;
            if (want != folderOpenId)
                FolderPreviewSwitch(() =>
                {
                    if (folderOpenId != null) CloseFolderNow();
                    if (want != null) { OpenFolderNow(want); FolderSelectGame(game); }
                });
            if (want == null && last != null && last != folderLastId && FolderById(last) != null)
            {
                folderLastId = last;   // (the folder tiles: the highlight back on the tile it was on)
                if (carActive && FolderTilesShown) CarShowSeq(FolderTileSeq(), false);
            }
            // the highlight was on the home screen before the settings: back on the folder tile / the game (not when it's somewhere else now)
            if (folderPreviewBackHome && openPanel == null) FolderFocusSoon(folderOpenId != null, 0);
            folderPreviewBackHome = false;
        }

        /// <summary>Your own move on the home screen (a folder opened or closed, home): nothing is put back for the settings' preview.</summary>
        private void FolderPreviewForget()
        {
            folderPreviewGen++;
            folderPreviewOn = false;
            folderPreviewFresh = false;
            folderPreviewTouched = false;
            folderPreviewBackHome = false;
        }

        /// <summary>The highlight is on the home screen's games or folder tiles (Playnite's row, the carousel, the board, the tiles).</summary>
        private bool FolderHomeHasFocus(DependencyObject d)
        {
            if (d == null) return false;
            if (carProxy != null && ReferenceEquals(d, carProxy)) return true;
            if (!(d is Visual)) return false;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return false;
            foreach (string n in new[] { "PART_ListGameItems", "KX_Board", "KX_HomeTiles" })
            {
                var el = UiTools.FindByName(win, n);
                if (el != null && el.IsAncestorOf(d)) return true;
            }
            return false;
        }

        private Guid FolderSelectedGame()
        {
            var list = Application.Current != null ? UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox : null;
            return list != null && list.SelectedItem != null ? EntryId(list.SelectedItem) : Guid.Empty;
        }

        /// <summary>A game selected in Playnite's list again (the folder you were in: the game you were on), when it's there.</summary>
        private void FolderSelectGame(Guid id)
        {
            if (id == Guid.Empty) return;
            var list = Application.Current != null ? UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox : null;
            if (list == null) return;
            for (int i = 0; i < list.Items.Count; i++)
                if (EntryId(list.Items[i]) == id) { if (list.SelectedIndex != i) list.SelectedIndex = i; return; }
        }

        // =====================================================================
        // Playnite's filter and sort in Folder. A folder shows exactly its own games: the list filter (ApplyListFilter) lets them
        // through in place of Playnite's filter, saved filter and search, which aren't changed and apply again as they were once
        // Folder is no longer chosen. The top bar's "search" / "filtered" marks are hidden while Folder is chosen (they'd say
        // something that isn't so). GAMES IN A FOLDER › Sort by puts the folder's own order on Playnite's list while a folder is
        // open (As Playnite sorts: none of ours); Playnite's sort is put back as it was when the folder closes.
        // =====================================================================

        /// <summary>The top bar's marks for Playnite's search / filter: not while Folder is chosen (they don't apply to a folder).</summary>
        public bool PlayniteFilterMarksShown { get { return !FolderMode; } }

        private ListCollectionView folderSortView;
        private FolderSorter folderSorter;                                         // ours on Playnite's list (null: none)
        private List<System.ComponentModel.SortDescription> folderSortSaved;      // Playnite's sort from before it
        private System.Collections.IComparer folderSortSavedCustom;
        private bool folderSortApplying;

        private int FolderSortNow { get { int v = settings.FolderSort; return v < 0 || v > 4 ? 0 : v; } }
        /// <summary>A folder is open with an order of its own (not As Playnite sorts).</summary>
        private bool FolderSortWanted { get { return FolderInside && IsFullscreen && FolderSortNow != 4; } }

        /// <summary>The order of a folder's games: by the game's own details, then its name (never throws: it runs inside WPF's sorting).</summary>
        private class FolderSorter : System.Collections.IComparer
        {
            public int Mode;
            public Func<object, Game> GameOf;
            public Func<Game, DateTime?> LastPlayed;   // (the profile's own last played)
            public int Compare(object a, object b)
            {
                try
                {
                    Game x = a != null ? GameOf(a) : null, y = b != null ? GameOf(b) : null;
                    if (x == null || y == null) return x == null ? (y == null ? 0 : 1) : -1;
                    int c = 0;
                    if (Mode == 1) c = Nullable.Compare(LastPlayed(y), LastPlayed(x));   // the latest first, never played last
                    else if (Mode == 2) c = y.Playtime.CompareTo(x.Playtime);
                    else if (Mode == 3) c = Nullable.Compare(y.Added, x.Added);
                    if (c != 0) return c;
                    c = string.Compare(SortName(x), SortName(y), StringComparison.CurrentCultureIgnoreCase);
                    return c != 0 ? c : x.Id.CompareTo(y.Id);
                }
                catch (Exception) { return 0; }
            }
            private static string SortName(Game g) { return !string.IsNullOrEmpty(g.SortingName) ? g.SortingName : (g.Name ?? ""); }
        }

        private readonly Dictionary<Type, System.Reflection.PropertyInfo> folderEntryGame = new Dictionary<Type, System.Reflection.PropertyInfo>();

        /// <summary>The game behind one of Playnite's list entries (its Game property, else looked up by id).</summary>
        private Game FolderEntryGame(object o)
        {
            var g = o as Game;
            if (g != null || o == null) return g;
            try
            {
                System.Reflection.PropertyInfo p;
                var t = o.GetType();
                if (!folderEntryGame.TryGetValue(t, out p)) { p = t.GetProperty("Game", typeof(Game)); folderEntryGame[t] = p; }
                g = p != null ? p.GetValue(o, null) as Game : null;
                if (g != null) return g;
                Guid id = EntryId(o);
                return id != Guid.Empty ? api.Database.Games.Get(id) : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The folder's order on Playnite's list, or Playnite's sort back (a folder opened / closed, Sort by changed, and every
        /// tick: Playnite putting its own sort on, after a filter preset with a sort of its own, takes ours off).
        /// </summary>
        private void FolderSortApply()
        {
            if (folderSortApplying || !IsFullscreen || ui == null) return;
            bool want = FolderSortWanted;
            if (!want && folderSorter == null) return;
            bool slotsAgain = false;
            try
            {
                folderSortApplying = true;
                var view = HomeListView();
                if (view == null) return;
                if (!ReferenceEquals(view, folderSortView)) { folderSortView = view; folderSorter = null; folderSortSaved = null; folderSortSavedCustom = null; }
                bool ours = folderSorter != null && ReferenceEquals(view.CustomSort, folderSorter);
                if (!want)
                {
                    if (ours)
                    {
                        // Playnite's sort as it was before the folder
                        var custom = folderSortSavedCustom;
                        var sorts = folderSortSaved;
                        view.CustomSort = custom;
                        if (custom == null && sorts != null)
                            using (view.DeferRefresh()) { view.SortDescriptions.Clear(); foreach (var sd in sorts) view.SortDescriptions.Add(sd); }
                    }
                    folderSorter = null;
                    folderSortSaved = null;
                    folderSortSavedCustom = null;
                    slotsAgain = true;   // (games kept in place: back in their spots, after this)
                }
                else if (ours)
                {
                    if (folderSorter.Mode != FolderSortNow) { folderSorter.Mode = FolderSortNow; view.Refresh(); }
                }
                else
                {
                    // first time (or Playnite put its own sort on since): its sort now is what comes back afterwards
                    ApplySlotPins(false);   // (kept-in-place spots are off in a folder with its own order: Playnite's sort under them back first)
                    if (view.CustomSort is SlotSorter) { folderSortSavedCustom = slotSavedCustom; folderSortSaved = slotSavedSorts != null ? slotSavedSorts.ToList() : new List<System.ComponentModel.SortDescription>(); }
                    else { folderSortSavedCustom = view.CustomSort is FolderSorter ? null : view.CustomSort; folderSortSaved = view.SortDescriptions.ToList(); }
                    folderSorter = new FolderSorter { Mode = FolderSortNow, GameOf = FolderEntryGame, LastPlayed = LastPlayedNow };
                    view.CustomSort = folderSorter;   // (setting it sorts the list again)
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: folder sort: " + ex.Message); }
            finally { folderSortApplying = false; }
            if (slotsAgain) ApplySlotPins(true);
            FolderSortWatch();
        }

        private System.ComponentModel.SortDescriptionCollection folderSortWatched;

        /// <summary>
        /// While a folder has its own order: Playnite putting its own sort on (a saved filter with a sort of its own, a sort setting)
        /// takes ours off; it goes back on straight away (not only on the next tick), so a folder never shows in Playnite's order.
        /// (The list gives itself a new SortDescriptions when ours goes on: watched afresh each time.)
        /// </summary>
        private void FolderSortWatch()
        {
            try
            {
                var view = folderSorter != null ? folderSortView : null;
                var sd = view != null ? view.SortDescriptions : null;
                if (ReferenceEquals(sd, folderSortWatched)) return;
                if (folderSortWatched != null) ((System.Collections.Specialized.INotifyCollectionChanged)folderSortWatched).CollectionChanged -= FolderSortTaken;
                folderSortWatched = sd;
                if (sd != null) ((System.Collections.Specialized.INotifyCollectionChanged)sd).CollectionChanged += FolderSortTaken;
            }
            catch (Exception) { }
        }

        private void FolderSortTaken(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (folderSortApplying || folderSorter == null || ui == null) return;
            ui.BeginInvoke(new Action(() => { try { if (FolderSortWanted) FolderSortApply(); } catch (Exception) { } }), DispatcherPriority.DataBind);
        }

        // =====================================================================
        // From the folder tiles to a folder's games and back: what's on screen fades out (150 ms), the switch is made, then the
        // new view fades in: a folder's games 250 ms with a slight zoom from the folder tile, the folder tiles 280 ms as a plain fade
        // (no zoom); the background goes from black to the game's art (or back). The new view stays at opacity 0 until the fade in
        // starts, on a frame. Reduce motion (and a settings panel open): straight away. Anything that cuts it short leaves
        // everything showing (backstops at the end of the fade and 1.5 s after the switch).
        // =====================================================================

        private static readonly string[] FolderFadeNames = { "KX_CarouselHost", "PART_ListGameItems", "KX_BoardHost", "KX_GameInfo", "KX_HomeTilesCanvas", "KX_FilterAbove" };
        private Action folderFadeSwitch;          // the switch waiting for the fade out (null: none)
        private bool folderFadeOpening;
        private int folderFadeGen;
        private Point? folderFadeFrom;            // opening: the folder tile's middle, in the window (the games grow from there)
        private readonly List<UIElement> folderFaded = new List<UIElement>();
        private FrameworkElement folderZoomEl;    // the element zoomed, our scale on it and its own transform from before
        private ScaleTransform folderZoom;
        private object folderZoomWas;
        private Border folderVeil;                // black over the background (made here, beside it), faded off to show the game's art
        private bool folderBlackWas, folderAccentOff;

        private bool FolderFadeAllowed { get { return !ReduceMotionNow && IsFullscreen && ui != null && openPanel == null && !detailsOpen; } }

        /// <summary>Fades the home screen's games (or folder tiles) out, runs <paramref name="sw"/>, then fades the new ones in.</summary>
        private void FolderTransition(bool opening, Action sw)
        {
            if (folderFadeSwitch != null) FolderFadeSwitchNow(folderFadeGen);
            int gen = ++folderFadeGen;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (!FolderFadeAllowed || win == null)
            {
                FolderFadeReset();
                sw();
                return;
            }
            FolderZoomOff();
            folderFadeOpening = opening;
            folderFadeSwitch = sw;
            folderFadeFrom = null;
            if (opening && carActive && carCanvas != null)
            {
                var r = CarHighlightRect();
                if (!r.IsEmpty) try { folderFadeFrom = carCanvas.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), win); } catch (Exception) { }
            }
            var dur = new Duration(TimeSpan.FromMilliseconds(150));
            folderFaded.Clear();
            foreach (string n in FolderFadeNames)
            {
                var el = UiTools.FindByName(win, n);
                if (el == null) continue;
                folderFaded.Add(el);
                // (from wherever it is now: a fade in still going carries straight on into this one)
                el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { To = 0, Duration = dur, FillBehavior = FillBehavior.HoldEnd, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            }
            if (!opening)
            {
                // the game's art goes to black with them (the folder tiles' background)
                var veil = FolderVeil(win);
                if (veil != null)
                {
                    veil.Visibility = Visibility.Visible;
                    veil.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { To = 1, Duration = dur, FillBehavior = FillBehavior.HoldEnd });
                }
            }
            FolderFadeLater(gen, 150, () => FolderFadeSwitchNow(gen));
        }

        /// <summary>The fade out is over (or something can't wait for it): the switch, then the fade in once the new view is laid out.</summary>
        private void FolderFadeSwitchNow(int gen)
        {
            if (gen != folderFadeGen) return;
            var sw = folderFadeSwitch;
            folderFadeSwitch = null;
            if (sw == null) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (folderFadeOpening && win != null)
            {
                // black stays over the background while the game's art comes in under it
                var veil = FolderVeil(win);
                if (veil != null) { veil.BeginAnimation(UIElement.OpacityProperty, null); veil.Opacity = 1; veil.Visibility = Visibility.Visible; }
            }
            try { sw(); }
            catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
            if (!FolderFadeAllowed || ui == null) { FolderFadeReset(); return; }
            // (after the carousel, Playnite's list and the board have taken the change: they're posted before this, or sooner)
            ui.BeginInvoke(new Action(() =>
            {
                try { FolderFadeIn(gen); }
                catch (Exception ex) { FolderFadeReset(); logger.Warn("Kippix: folders, fade: " + ex.Message); }
            }), DispatcherPriority.ContextIdle);
            FolderFadeLater(gen, 1500, FolderFadeReset);   // (whatever happens in between, nothing stays faded out)
        }

        // The fade in starts on a frame (the new view has been laid out and drawn once, still unseen at opacity 0), not in the middle of
        // the dispatcher work that follows the switch: started there, its first frames were lost to that work and the view showed up
        // already part way in. Back to the folder tiles it also waits (at most 200 ms) for the tiles' photos, so they don't pop in.
        private int folderFadeWaitGen;
        private DateTime folderFadeWaitUntil;
        private bool folderFadeHooked;

        private void FolderFadeIn(int gen)
        {
            if (gen != folderFadeGen) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null || !FolderFadeAllowed) { FolderFadeReset(); return; }
            folderFadeWaitGen = gen;
            folderFadeWaitUntil = DateTime.UtcNow.AddMilliseconds(folderFadeOpening ? 0 : 200);
            FolderFadeHook(true);
        }

        private void FolderFadeHook(bool on)
        {
            if (on == folderFadeHooked) return;
            folderFadeHooked = on;
            if (on) CompositionTarget.Rendering += OnFolderFadeFrame;
            else CompositionTarget.Rendering -= OnFolderFadeFrame;
        }

        private void OnFolderFadeFrame(object sender, EventArgs e)
        {
            int gen = folderFadeWaitGen;
            try
            {
                if (gen != folderFadeGen) { FolderFadeHook(false); return; }
                if (!FolderFadeAllowed) { FolderFadeHook(false); FolderFadeReset(); return; }
                if (DateTime.UtcNow < folderFadeWaitUntil && !FolderFadeReady()) return;   // (the next frame)
                FolderFadeHook(false);
                FolderFadeStart(gen);
            }
            catch (Exception ex) { FolderFadeHook(false); FolderFadeReset(); logger.Warn("Kippix: folders, fade: " + ex.Message); }
        }

        /// <summary>The carousel's cards on screen are dressed and their pictures are in (nothing still loading).</summary>
        private bool FolderFadeReady()
        {
            if (!carActive || carHost == null || !carHost.IsVisible) return true;
            foreach (var s in carShown.Values)
            {
                if (s.El == null || s.El.Visibility != Visibility.Visible) continue;
                if (!s.Dressed || s.ArtKey != null) return false;
            }
            return true;
        }

        /// <summary>
        /// Into a folder: its games fade in growing slightly out of the folder tile. Back to the folder tiles: a plain, gentle fade
        /// (the tiles stand edge to edge across the whole screen, so coming back from 1.06 read as a jump: bigger, then snapping back).
        /// </summary>
        private void FolderFadeStart(int gen)
        {
            if (gen != folderFadeGen) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null || !FolderFadeAllowed) { FolderFadeReset(); return; }
            bool opening = folderFadeOpening;
            int ms = opening ? 250 : 280;
            var dur = new Duration(TimeSpan.FromMilliseconds(ms));
            IEasingFunction ease = opening ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseOut } : new SineEase { EasingMode = EasingMode.EaseInOut };
            foreach (var el in folderFaded)
                el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { From = 0, Duration = dur, FillBehavior = FillBehavior.Stop, EasingFunction = ease });   // (up to its own opacity)
            var veil = folderVeil;
            if (veil != null)
            {
                veil.BeginAnimation(UIElement.OpacityProperty, null);
                veil.Opacity = 0;
                if (opening) veil.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { From = 1, Duration = new Duration(TimeSpan.FromMilliseconds(320)), FillBehavior = FillBehavior.Stop, EasingFunction = ease });
                else veil.Visibility = Visibility.Collapsed;
            }
            if (opening) FolderZoomIn(win, dur, ease);
            FolderFadeLater(gen, Math.Max(ms, 320) + 80, FolderFadeReset);   // (tidy: our zoom off, the veil put away)
        }

        /// <summary>The slight zoom when a folder opens: its games grow out of the folder tile.</summary>
        private void FolderZoomIn(Window win, Duration dur, IEasingFunction ease)
        {
            FolderZoomOff();
            FrameworkElement target = null;
            if (carActive && carHost != null && carHost.IsVisible && carCanvas != null) target = carCanvas;
            else
            {
                var list = UiTools.FindByName(win, "PART_ListGameItems");
                if (list != null && list.IsVisible && !ReferenceEquals(list.Clip, CarHiddenClip) && VisualTreeHelper.GetChildrenCount(list) > 0)
                    target = VisualTreeHelper.GetChild(list, 0) as FrameworkElement;   // (inside the list: Free move's own move stays on the list)
            }
            if (target == null || target.ActualWidth <= 0 || target.ActualHeight <= 0) return;
            if (BindingOperations.IsDataBound(target, UIElement.RenderTransformProperty) || (target.RenderTransform != null && !target.RenderTransform.Value.IsIdentity)) return;
            var c = new Point(target.ActualWidth / 2, target.ActualHeight / 2);
            try { if (folderFadeFrom.HasValue) c = win.TranslatePoint(folderFadeFrom.Value, target); }
            catch (Exception) { }
            const double from = 0.92;
            folderZoomWas = target.ReadLocalValue(UIElement.RenderTransformProperty);
            folderZoom = new ScaleTransform(from, from, c.X, c.Y);
            folderZoomEl = target;
            target.RenderTransform = folderZoom;
            var a = new DoubleAnimation(from, 1, dur) { EasingFunction = ease };
            folderZoom.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            folderZoom.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        private void FolderZoomOff()
        {
            var el = folderZoomEl;
            folderZoomEl = null;
            if (el == null) return;
            try
            {
                if (ReferenceEquals(el.RenderTransform, folderZoom))
                {
                    if (folderZoomWas == null || folderZoomWas == DependencyProperty.UnsetValue) el.ClearValue(UIElement.RenderTransformProperty);
                    else el.SetValue(UIElement.RenderTransformProperty, folderZoomWas);
                }
            }
            catch (Exception) { }
            folderZoom = null;
            folderZoomWas = null;
        }

        /// <summary>Everything as it is without a fade: no animation left on the views, no zoom, the veil put away.</summary>
        private void FolderFadeReset()
        {
            try
            {
                FolderFadeHook(false);
                foreach (var el in folderFaded) el.BeginAnimation(UIElement.OpacityProperty, null);
                folderFaded.Clear();
                FolderZoomOff();
                if (folderVeil != null)
                {
                    folderVeil.BeginAnimation(UIElement.OpacityProperty, null);
                    folderVeil.Opacity = 0;
                    folderVeil.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: folders, fade: " + ex.Message); }
        }

        private void FolderFadeLater(int gen, int ms, Action act)
        {
            if (ui == null) return;
            var t = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (gen != folderFadeGen) return;
                try { act(); } catch (Exception ex) { logger.Warn("Kippix: folders, fade: " + ex.Message); }
            };
            t.Start();
        }

        /// <summary>A plain black layer just over the background's pictures (and the trailer), under everything else.</summary>
        private Border FolderVeil(Window win)
        {
            try
            {
                var bg = UiTools.FindByName(win, "PART_ImageBackground");
                var parent = bg != null ? VisualTreeHelper.GetParent(bg) as Panel : null;
                if (parent == null || parent.IsItemsHost) return null;
                if (folderVeil != null && ReferenceEquals(VisualTreeHelper.GetParent(folderVeil), parent)) return folderVeil;
                if (folderVeil != null) { var old = VisualTreeHelper.GetParent(folderVeil) as Panel; if (old != null) old.Children.Remove(folderVeil); }
                var veil = new Border { Background = Brushes.Black, IsHitTestVisible = false, Focusable = false, Opacity = 0, Visibility = Visibility.Collapsed };
                int at = parent.Children.IndexOf(bg);
                var pv = UiTools.FindByName(win, "KX_PreviewBg");
                if (pv != null && ReferenceEquals(VisualTreeHelper.GetParent(pv), parent)) at = Math.Max(at, parent.Children.IndexOf(pv));
                parent.Children.Insert(Math.Max(0, Math.Min(parent.Children.Count, at + 1)), veil);
                folderVeil = veil;
                return veil;
            }
            catch (Exception ex) { logger.Warn("Kippix: folders, background: " + ex.Message); return null; }
        }

        /// <summary>Folder's tiles have a plain black background and the theme's own highlight colour (not the hidden game's).</summary>
        private void FolderBackgroundFollow()
        {
            bool black = HomeBgBlack;
            if (black != folderBlackWas) { folderBlackWas = black; NotifyHomeBackground(); }
            FolderAccentFollow();
        }

        /// <summary>Look and feel › Accent from the game's art: not from the game hidden behind Folder's tiles.</summary>
        private void FolderAccentFollow()
        {
            if (!settings.AccentFromGame) { folderAccentOff = false; return; }
            if (FolderBgBlack)
            {
                accentGen++;   // (a colour still being worked out for the hidden game isn't put on)
                if (accentNow != Colors.White) ClearAccent();
                folderAccentOff = true;
            }
            else if (folderAccentOff)
            {
                folderAccentOff = false;
                ApplyAccentFor(lastAccentPath, true);
            }
        }

        // =====================================================================
        // The folder tiles in the carousel
        // =====================================================================

        /// <summary>The folder tiles, after the tiles before the games (kept when they're the same folder, so their photos stay).</summary>
        private void CarAddFolders(Dictionary<string, CarouselItem> keep)
        {
            FolderHookDb();
            FolderRefreshSoon();   // (their counts, when they're out of date)
            foreach (var f in FolderTiles)
            {
                CarouselItem it;
                if (!keep.TryGetValue("folder:" + f.Id, out it) || !ReferenceEquals(it.Source, f)) it = CarItemFromFolder(f);
                else FolderDressItem(it, f);
                carExtras.Add(it);
            }
        }

        private CarouselItem CarItemFromFolder(HomeFolder f)
        {
            var it = new CarouselItem { Kind = "folder", Key = "folder:" + f.Id, Icon = "folder", Source = f };
            string id = f.Id;
            it.Activate = () => OpenFolder(id);
            FolderDressItem(it, f);
            return it;
        }

        private void FolderDressItem(CarouselItem it, HomeFolder f)
        {
            it.Name = FolderName(f);
            it.Sub = FolderCountText(f);
            string pic = FolderPictureFile(f);
            if (it.PictureFile != pic) { it.PictureFile = pic; it.PictureFor = null; it.Picture = null; }
        }

        /// <summary>The folder's photo on disk, when it has one that's there.</summary>
        private static string FolderPictureFile(HomeFolder f)
        {
            try { return f != null && !string.IsNullOrEmpty(f.Picture) && File.Exists(f.Picture) ? f.Picture : null; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Folder's tiles with the highlight in Playnite's unseen row (start-up, back from a panel): onto the folder tile instead,
        /// once the carousel is laid out and showing.
        /// </summary>
        private void CarFolderFocusSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!carActive || !FolderTilesShown || carProxy == null || carList == null || !carList.IsKeyboardFocusWithin) return;
                    CarShowSeq(carIndex >= 0 && carIndex < carExtras.Count ? carIndex : FolderTileSeq(), false);
                    Keyboard.Focus(carProxy);
                }
                catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>The highlight moved onto a folder tile: that's the one it comes back to.</summary>
        private void FolderNoteHighlight(int seq)
        {
            var f = seq >= 0 && seq < carExtras.Count ? carExtras[seq].Source as HomeFolder : null;
            if (f != null) folderLastId = f.Id;
        }

        /// <summary>The carousel's place of the folder to highlight on the tiles (the last one opened; All games at first).</summary>
        private int FolderTileSeq()
        {
            int first = -1;
            for (int i = 0; i < carExtras.Count; i++)
            {
                var it = carExtras[i];
                if (it.Kind != "folder") continue;
                if (first < 0) first = i;
                var f = it.Source as HomeFolder;
                if (f != null && folderLastId != null && f.Id == folderLastId) return i;
            }
            return first >= 0 ? first : 0;
        }

        /// <summary>A folder was added, removed or changed (settings): the tiles, the counts and an open folder's games follow.</summary>
        private void FoldersChanged()
        {
            Notify("FoldersSummary");
            Notify("HomeRowsSummary");
            Notify("HomeLabelText");
            if (!FolderMode || !IsFullscreen) return;
            if (carActive && FolderTilesShown) { CarBuildExtras(); CarResync(); CarRedress(); }
            FolderRefreshSoon();
        }

        // =====================================================================
        // What's in a folder (worked out on a worker thread, kept until something it depends on changes)
        // =====================================================================

        private class FolderResult
        {
            public string Sig;
            public HashSet<Guid> All;                 // every game in it (All games: every game the folders can show)
            public List<HashSet<Guid>> Groups;        // each of its lists (and the games picked by hand) on its own
            public int Count;
        }

        private class FolderJob
        {
            public string Id, Sig;
            public List<string> Sources, Games;       // Sources null: All games
            public int Installed;                     // the folder's own Installed games choice (0 all, 1 installed only, 2 not installed)
        }

        private readonly Dictionary<string, FolderResult> folderCache = new Dictionary<string, FolderResult>();   // (UI thread only)
        private int folderLibGen;          // the library or a saved filter changed (bumped from Playnite's events, any thread)
        private int folderWorkGen;
        private int folderLibPosted;       // a refresh is already on its way to the UI thread
        private DispatcherTimer folderTimer;
        private bool folderDbHooked;

        private void FolderHookDb()
        {
            if (folderDbHooked || api == null) return;
            folderDbHooked = true;
            try
            {
                api.Database.Games.ItemCollectionChanged += (s, e) => FolderLibraryChanged();
                api.Database.Games.ItemUpdated += (s, e) => FolderLibraryChanged();
                api.Database.FilterPresets.ItemCollectionChanged += (s, e) => FolderLibraryChanged();
                api.Database.FilterPresets.ItemUpdated += (s, e) => FolderLibraryChanged();
            }
            catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
        }

        /// <summary>(Playnite's events, on any thread.) The folders are worked out again a moment after the changes stop.</summary>
        private void FolderLibraryChanged()
        {
            try
            {
                Interlocked.Increment(ref folderLibGen);
                var d = ui;
                // (a big import or metadata download sends thousands of these: one call waiting at a time is enough)
                if (d != null && Interlocked.Exchange(ref folderLibPosted, 1) == 0)
                    d.BeginInvoke(new Action(() =>
                    {
                        Interlocked.Exchange(ref folderLibPosted, 0);
                        try { if (FolderMode) FolderRefreshSoon(); } catch (Exception) { }
                    }), DispatcherPriority.Background);
            }
            catch (Exception) { }
        }

        private void FolderRefreshSoon()
        {
            if (ui == null || !FolderMode) return;
            if (!ui.CheckAccess()) { ui.BeginInvoke(new Action(FolderRefreshSoon)); return; }
            if (folderTimer == null)
            {
                folderTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(700) };
                folderTimer.Tick += (s, e) =>
                {
                    folderTimer.Stop();
                    try { FolderRefreshAll(); } catch (Exception ex) { logger.Warn("Kippix: folders: " + ex.Message); }
                };
            }
            folderTimer.Stop();
            folderTimer.Start();
        }

        /// <summary>What a folder's games depend on: the library, the profile, its lists (a collection's games) and its picks.</summary>
        private string FolderSig(HomeFolder f)
        {
            var sb = new StringBuilder();
            sb.Append(Thread.VolatileRead(ref folderLibGen)).Append('|').Append(FolderProfileSig()).Append("|i").Append(FolderInstalledOf(f));
            if (f.Id == AllGamesId) return sb.ToString();
            foreach (string k in f.Sources)
            {
                sb.Append('|').Append(k);
                if (!k.StartsWith("shelf:", StringComparison.Ordinal)) continue;
                var shelf = ShelfById(k.Substring(6));
                if (shelf != null && shelf.Games != null) { sb.Append(':').Append(shelf.Games.Count).Append(':'); foreach (string g in shelf.Games) sb.Append(g); }
            }
            sb.Append("|#");
            foreach (string g in f.Games) sb.Append(g).Append(',');
            return sb.ToString();
        }

        private string FolderProfileSig()
        {
            var p = profileNow;
            if (p == null || !settings.ProfilesEnabled) return "-";
            return p.Id + "/" + (p.HiddenGames != null ? p.HiddenGames.Count : 0) + "/" + p.MaxAge + "/" + p.HideUnrated + "/" + p.HideNonSteam + "/" + p.OnlyOwnSteamGames
                 + "/" + (p.Favourites != null ? p.Favourites.Count : 0) + "/" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ownedNow)
                 + "/" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(profileLastPlayed);
        }

        private FolderJob FolderJobFor(HomeFolder f)
        {
            return new FolderJob { Id = f.Id, Sig = FolderSig(f), Sources = f.Id == AllGamesId ? null : f.Sources.ToList(), Games = f.Games.ToList(), Installed = FolderInstalledOf(f) };
        }

        /// <summary>Copies of the collections the jobs use (they can change on the UI thread while the worker reads them).</summary>
        private Dictionary<string, GameShelf> FolderShelves(IEnumerable<FolderJob> jobs)
        {
            var shelves = new Dictionary<string, GameShelf>();
            foreach (var j in jobs)
            {
                if (j.Sources == null) continue;
                foreach (string k in j.Sources)
                {
                    if (!k.StartsWith("shelf:", StringComparison.Ordinal) || shelves.ContainsKey(k)) continue;
                    var found = ShelfById(k.Substring(6));
                    if (found != null) shelves[k] = new GameShelf { Id = found.Id, Name = found.Name, Games = found.Games.ToList() };
                }
            }
            return shelves;
        }

        /// <summary>
        /// The games a folder can show (safe from any thread): not hidden in Playnite and allowed in this profile. Each folder's own
        /// Installed games choice is applied on top (FolderCompute). Playnite's own filter, saved filter and search don't come into it.
        /// </summary>
        private List<Game> FolderLibrary()
        {
            return api.Database.Games.Where(g => g != null && !g.Hidden && ProfileAllowsGame(g)).ToList();
        }

        /// <summary>The ids a folder can show: the library above, then installed / not installed as the folder asks.</summary>
        private static HashSet<Guid> FolderAllowed(List<Game> all, int installed)
        {
            return new HashSet<Guid>(all.Where(g => installed == 0 || (installed == 1) == g.IsInstalled).Select(g => g.Id));
        }

        /// <summary>One folder's games (safe from any thread): each list on its own, then the games picked by hand.</summary>
        private FolderResult FolderCompute(FolderJob j, List<Game> all, Dictionary<string, HashSet<Guid>> allowedBy, Dictionary<string, GameShelf> shelves)
        {
            var r = new FolderResult { Sig = j.Sig };
            int inst = j.Installed < 0 || j.Installed > 2 ? 0 : j.Installed;
            HashSet<Guid> allowed;
            string ak = inst.ToString();
            if (!allowedBy.TryGetValue(ak, out allowed)) { allowed = FolderAllowed(all, inst); allowedBy[ak] = allowed; }   // (worked out once per choice)
            if (j.Sources == null) { r.All = new HashSet<Guid>(allowed); r.Count = r.All.Count; return r; }   // All games: every game (not Playnite's filtered list)
            r.Groups = new List<HashSet<Guid>>();
            foreach (string k in j.Sources)
            {
                var set = new HashSet<Guid>();
                try
                {
                    foreach (var id in BoardSourceGames(k, all, null, null, shelves))   // (the Library tile's lists)
                        if (allowed.Contains(id)) set.Add(id);
                }
                catch (Exception ex) { logger.Warn("Kippix: folder list " + k + ": " + ex.Message); }   // (one list that can't be read doesn't empty the folder)
                r.Groups.Add(set);
            }
            var picked = new HashSet<Guid>();
            foreach (string s in j.Games)
            {
                Guid id;
                if (Guid.TryParse(s, out id) && allowed.Contains(id)) picked.Add(id);
            }
            if (picked.Count > 0) r.Groups.Add(picked);
            r.All = new HashSet<Guid>();
            foreach (var g in r.Groups) r.All.UnionWith(g);
            r.Count = r.All.Count;
            return r;
        }

        /// <summary>A folder's games now (UI thread): from what's kept, or worked out here and then if it's out of date.</summary>
        private FolderResult FolderResultFor(HomeFolder f)
        {
            string sig = FolderSig(f);
            FolderResult r;
            if (folderCache.TryGetValue(f.Id, out r) && r.Sig == sig) return r;
            try
            {
                var job = FolderJobFor(f);
                var all = FolderLibrary();
                r = FolderCompute(job, all, new Dictionary<string, HashSet<Guid>>(), FolderShelves(new[] { job }));
                folderCache[f.Id] = r;
                return r;
            }
            catch (Exception ex) { logger.Warn("Kippix: folder games: " + ex.Message); return null; }
        }

        /// <summary>Every folder that's out of date, worked out again on a worker thread.</summary>
        private void FolderRefreshAll()
        {
            if (!FolderMode || !IsFullscreen) return;
            var jobs = new List<FolderJob>();
            foreach (var f in FolderTiles)   // (a removed All games isn't worked out)
            {
                string sig = FolderSig(f);
                FolderResult r;
                if (folderCache.TryGetValue(f.Id, out r) && r.Sig == sig) continue;
                jobs.Add(FolderJobFor(f));
            }
            if (jobs.Count == 0) return;
            var shelves = FolderShelves(jobs);
            int gen = ++folderWorkGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                var done = new List<KeyValuePair<string, FolderResult>>();
                try
                {
                    var all = FolderLibrary();
                    var allowedBy = new Dictionary<string, HashSet<Guid>>();
                    foreach (var j in jobs) done.Add(new KeyValuePair<string, FolderResult>(j.Id, FolderCompute(j, all, allowedBy, shelves)));
                }
                catch (Exception ex) { logger.Warn("Kippix: folder games: " + ex.Message); }
                OnUi(() => { if (gen == folderWorkGen) FolderResultsArrived(done); });
            });
        }

        private void FolderResultsArrived(List<KeyValuePair<string, FolderResult>> done)
        {
            bool openChanged = false;
            foreach (var kv in done)
            {
                folderCache[kv.Key] = kv.Value;
                if (kv.Key == folderOpenId) openChanged = true;
            }
            // the open folder: its games follow (Playnite's list is only filtered again when they really changed)
            if (openChanged && FolderInside)
            {
                var r = folderCache[folderOpenId];
                var want = r.All ?? new HashSet<Guid>();
                bool same = folderFilter != null && folderFilter.SetEquals(want);
                folderGroups = r.Groups;
                folderFilter = want;
                if (!same) ApplyListFilter(true);
                else if (carActive) CarResync();   // (its lists may have changed: the duplicates)
            }
            Notify("FoldersSummary");
            if (carActive && FolderTilesShown)
                foreach (var it in carExtras)
                {
                    var f = it.Source as HomeFolder;
                    if (f != null && it.Kind == "folder") it.Sub = FolderCountText(f);
                }
            if (openPanel == "options" && optionsPage == "folders")
                foreach (var row in FolderRows)
                {
                    if (row.Key == null || !row.Key.StartsWith("folder:", StringComparison.Ordinal)) continue;
                    var f = FolderById(row.Key.Substring(7));
                    if (f != null) row.SetSub(FolderRowSub(f));
                }
        }

        private string FolderCountText(HomeFolder f)
        {
            FolderResult r;
            if (f == null || !folderCache.TryGetValue(f.Id, out r)) return "";
            return r.Count == 1 ? Loc("LOC_KX_OneGame", "1 game") : string.Format(Loc("LOC_KX_NGames", "{0} games"), r.Count.ToString("N0"));
        }

        // =====================================================================
        // Settings pages: Your folders, a folder, Games picked by hand
        // =====================================================================

        private ObservableCollection<PickRow> folderRows;
        public ObservableCollection<PickRow> FolderRows { get { return folderRows ?? (folderRows = new ObservableCollection<PickRow>()); } }
        public bool IsOptionsFolders { get { return optionsPage == "folders" || optionsPage == "folder" || optionsPage == "folderpicks"; } }
        private string folderEditId;                                  // the folder whose page is open ("folder", "folderpicks")
        private string folderPickQuery = "";
        private List<Tuple<Guid, string, string>> folderPickIndex;    // the library for Games picked by hand: id, name, name to search

        private string FolderEditTitle { get { var f = FolderById(folderEditId); return f != null ? FolderName(f) : Loc("LOC_KX_FoldersTitle", "Your folders"); } }

        public string FolderPageHelp
        {
            get
            {
                if (optionsPage == "folderpicks") return Loc("LOC_KX_FolderPicksHelp", "Switch a game on to put it in this folder, off to take it out. Search to find one quickly.");
                return "";
            }
        }
        public bool HasFolderPageHelp { get { return FolderPageHelp.Length > 0; } }

        private void FolderOptionsOpened(string page)
        {
            Notify("IsOptionsFolders");
            Notify("FolderPageHelp");
            Notify("HasFolderPageHelp");
            if (page != "folderpicks") { folderPickIndex = null; if (page != "folder" && page != "picker") folderPickQuery = ""; }
            if (page == "folders" || page == "folder" || page == "folderpicks") BuildFolderRows();
            FolderPreviewSoon();
        }

        private void BuildFolderRows()
        {
            FolderRows.Clear();
            if (!FolderMode && !IsOptionsFolders) return;   // (Settings search: only offered while Folder is chosen)
            try
            {
                if (optionsPage == "folder") BuildOneFolderRows();
                else if (optionsPage == "folderpicks") BuildFolderPickRows();
                else BuildFolderListRows();
            }
            catch (Exception ex) { logger.Warn("Kippix: folder settings: " + ex.Message); }
        }

        private void FolderRowsAgain(string key) { BoardRowsAgain(BuildFolderRows, key); }

        /// <summary>A small copy of the folder's photo on its row (read on a worker thread).</summary>
        private void FolderRowPicture(PickRow row, string file)
        {
            if (row == null || string.IsNullOrEmpty(file)) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                System.Windows.Media.ImageSource img = null;
                try { img = Images.Load(file, 64); } catch (Exception) { }
                if (img != null) OnUi(() => row.Picture = img);
            });
        }

        private string FolderRowSub(HomeFolder f)
        {
            string count = FolderCountText(f);
            string what;
            if (f.Id == AllGamesId) what = Loc("LOC_KX_FolderAllSub", "Every game in your library");
            else
            {
                var parts = new List<string>();
                int lists = f.Sources.Count, picked = f.Games.Count;
                if (lists > 0) parts.Add(lists == 1 ? Loc("LOC_KX_OneList", "1 list") : string.Format(Loc("LOC_KX_NLists", "{0} lists"), lists));
                if (picked > 0) parts.Add(string.Format(Loc("LOC_KX_FolderNPicked", "{0} picked"), picked));
                what = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : Loc("LOC_KX_FolderEmpty", "Empty");
            }
            return count.Length > 0 ? count + "  ·  " + what : what;
        }

        // ---- Your folders ----

        private void BuildFolderListRows()
        {
            var rows = FolderRows;
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecYourFolders", "YOUR FOLDERS") });
            foreach (var f in FolderTiles)
            {
                string id = f.Id;
                var row = new PickRow
                {
                    Key = "folder:" + id, Icon = "folder", Text = FolderName(f), Sub = FolderRowSub(f),
                    Command = new RelayCommand(_ => { folderEditId = id; PlaySound("select"); SetOptionsPage("folder"); })
                };
                rows.Add(row);
                FolderRowPicture(row, FolderPictureFile(f));
            }
            rows.Add(new PickRow
            {
                Key = "fadd", Icon = "add", Text = Loc("LOC_KX_FolderAdd", "Add a folder"), Sub = Loc("LOC_KX_FolderAddSub", "Give it a name, then choose its photo and what's in it"),
                Command = new RelayCommand(_ => AddFolder())
            });
            if (AllGamesRemoved)
                rows.Add(new PickRow
                {
                    Key = "faddall", Icon = "add", Text = Loc("LOC_KX_FolderAddAll", "Add the All games folder"), Sub = Loc("LOC_KX_FolderAddAllSub", "Every game in your library, first on the home screen again"),
                    Command = new RelayCommand(_ => RestoreAllGamesFolder())
                });
            FolderRefreshSoon();   // (the counts, if they're out of date)
        }

        /// <summary>Your folders › Add the All games folder: back first on the home screen, with the name, photo and choices it had.</summary>
        private void RestoreAllGamesFolder()
        {
            var all = FolderById(AllGamesId);
            if (all == null) return;
            all.Removed = false;
            SaveSettings();
            PlaySound("select");
            FoldersChanged();
            FolderRowsAgain("folder:" + AllGamesId);
        }

        private void AddFolder()
        {
            PlaySound("select");
            OpenKeyboard(Loc("LOC_KX_FolderNamePrompt", "A name for the folder"), "", typed =>
            {
                if (string.IsNullOrWhiteSpace(typed)) { PlaySound("back"); return; }
                var f = new HomeFolder { Name = typed.Trim(), Installed = 0 };   // (a new folder: every game, installed or not)
                FolderList.Add(f);
                SaveSettings();
                folderEditId = f.Id;
                FoldersChanged();
                SetOptionsPage("folder");
            }, null, null, 40);
        }

        // ---- a folder ----

        private void BuildOneFolderRows()
        {
            var rows = FolderRows;
            var f = FolderById(folderEditId);
            if (f == null) { rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fgone", Text = Loc("LOC_KX_FolderGone", "That folder isn't there any more") }); return; }
            bool all = f.Id == AllGamesId;
            string id = f.Id;

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecName", "NAME") });
            rows.Add(new PickRow
            {
                Key = "fname", Icon = "note", Text = FolderName(f), Sub = Loc("LOC_KX_FolderRenameSub", ""),
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_FolderNamePrompt", "A name for the folder"), FolderName(f), typed =>
                {
                    var fo = FolderById(id);
                    if (fo == null) return;
                    if (string.IsNullOrWhiteSpace(typed)) { if (fo.Id != AllGamesId) return; fo.Name = ""; }   // (All games: back to its own name)
                    else fo.Name = typed.Trim();
                    SaveSettings();
                    FoldersChanged();
                    Notify("OptionsTitle");
                    FolderRowsAgain("fname");
                }, null, null, 40))
            });

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecPhoto", "PHOTO") });
            string pic = FolderPictureFile(f);
            var photo = new PickRow
            {
                Key = "fphoto", Icon = "picture",
                Text = pic != null ? Loc("LOC_KX_FolderPhotoAnother", "Choose another picture") : Loc("LOC_KX_FolderPhotoChoose", "Choose a picture"),
                Sub = pic != null ? Path.GetFileName(pic)
                    : !string.IsNullOrEmpty(f.Picture) ? Loc("LOC_KX_FolderPhotoMissing", "Its picture can't be found")
                    : Loc("LOC_KX_FolderPhotoNone", "None yet: the folder shows its name"),
                Command = new RelayCommand(_ => ChooseFolderPicture(id))
            };
            rows.Add(photo);
            FolderRowPicture(photo, pic);
            if (!string.IsNullOrEmpty(f.Picture))
                rows.Add(new PickRow
                {
                    Key = "fphotooff", Icon = "remove", Text = Loc("LOC_KX_FolderPhotoRemove", "Remove the photo"), Sub = Loc("LOC_KX_FolderPhotoRemoveSub", "The folder shows its name instead"),
                    Command = new RelayCommand(_ =>
                    {
                        var fo = FolderById(id);
                        if (fo == null) return;
                        string old = fo.Picture;
                        fo.Picture = "";
                        SaveSettings();
                        DropFolderPicture(old);
                        PlaySound("back");
                        FoldersChanged();
                        FolderRowsAgain("fphoto");
                    })
                });

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecWhatsInIt", "WHAT'S IN IT") });
            // installed / not installed: this folder's own choice (its games and its count on the folder tile)
            rows.Add(new PickRow
            {
                Key = "finst", Icon = "download",
                Text = string.Format(Loc("LOC_KX_FolderInstalledRow", "Installed games: {0}"), FolderInstalledName(FolderInstalledOf(f))),
                Sub = Loc("LOC_KX_FolderInstalledSub2", "Toggle if games that are or are not installed should show in this folder"),
                Command = new RelayCommand(_ =>
                {
                    var fo = FolderById(id);
                    if (fo == null) return;
                    fo.Installed = (FolderInstalledOf(fo) + 1) % 3;   // All, Installed only, Not installed
                    SaveSettings();
                    PlaySound("move");
                    FoldersChanged();   // (worked out again: the open folder's games and its tile's count follow)
                    FolderRowsAgain("finst");
                })
            });
            if (all)
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fallinfo", Text = Loc("LOC_KX_FolderAllInfo", "Every game in your library: Playnite's filters and search don't apply in folders") });
            else
            {
                rows.Add(new PickRow
                {
                    Key = "fpicks", Icon = "controller",
                    Text = string.Format(Loc("LOC_KX_FolderPickedRow", "Games picked by hand: {0}"), f.Games.Count),
                    Sub = Loc("LOC_KX_FolderPickedRowSub", "Add or take out single games"),
                    Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("folderpicks"); })
                });
                Action<string, List<string>> group = (head, keys) =>
                {
                    if (keys.Count == 0) return;
                    rows.Add(new PickRow { IsHeader = true, Text = head });
                    foreach (string k in keys)
                    {
                        string key = k;
                        rows.Add(BoardSwitch(k.StartsWith("filter:", StringComparison.Ordinal) ? "filter" : "shelf", "src:" + k, BoardSourceName(k), f.Sources.Contains(k),
                                             Loc("LOC_KX_FolderSrcIn", "In this folder"), Loc("LOC_KX_Off", "Off"), () =>
                        {
                            var fo = FolderById(id);
                            if (fo == null) return;
                            if (fo.Sources.Contains(key)) fo.Sources.Remove(key); else fo.Sources.Add(key);
                            SaveSettings();
                            PlaySound("move");
                            FoldersChanged();
                            FolderRowsAgain("src:" + key);
                        }));
                    }
                };
                // (the Library tile's lists)
                group(Loc("LOC_KX_LibGroupGames", "YOUR GAMES"), new List<string> { "all", "played", "favourites", "recent", "installed", "never" });
                var filters = new List<string>();
                try { foreach (var p in api.Database.FilterPresets.OrderBy(p => p.Name)) filters.Add("filter:" + p.Id); } catch (Exception) { }
                group(Loc("LOC_KX_LibGroupFilters", "SAVED FILTERS"), filters);
                group(Loc("LOC_KX_LibGroupCollections", "COLLECTIONS"), settings.Collections.Select(sh => "shelf:" + sh.Id).ToList());
                // a list that's gone (a saved filter or collection deleted since): still listed, so it can be switched off
                var gone = f.Sources.Where(k => (k.StartsWith("filter:", StringComparison.Ordinal) && !filters.Contains(k))
                                             || (k.StartsWith("shelf:", StringComparison.Ordinal) && ShelfById(k.Substring(6)) == null)).ToList();
                group(Loc("LOC_KX_FolderGoneLists", "NOT THERE ANY MORE"), gone);
            }

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecRemove", "REMOVE") });
            if (all && FolderList.Count <= 1)
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fnoremove", Text = Loc("LOC_KX_FolderAllOnly", "All games is your only folder: add one of your own first, then it can be removed") });
            else
                rows.Add(new PickRow
                {
                    Key = "fremove", Icon = "trash", Text = Loc("LOC_KX_FolderRemove", "Remove this folder"),
                    Sub = all ? Loc("LOC_KX_FolderRemoveAllSub", "It comes off the home screen; Your folders › Add the All games folder brings it back")
                              : Loc("LOC_KX_FolderRemoveSub", "The folder goes; its games stay in your library"),
                    Command = new RelayCommand(_ => RemoveFolder(id))
                });
        }

        private void RemoveFolder(string id)
        {
            var f = FolderById(id);
            if (f == null) return;
            if (f.Id == AllGamesId)
            {
                // All games: taken off the home screen, kept (name, photo, choices) for Add the All games folder
                if (FolderList.Count <= 1) return;
                f.Removed = true;
                SaveSettings();
                if (folderLastId == id) folderLastId = null;
                if (folderOpenId == id) CloseFolder(false);
                PlaySound("back");
                FoldersChanged();
                GoBackTo("folders", "faddall");
                return;
            }
            FolderList.Remove(f);
            if (FolderList.Count == 1) FolderList[0].Removed = false;   // the last of yours gone: All games is back (never no folders at all)
            SaveSettings();
            DropFolderPicture(f.Picture);
            folderCache.Remove(id);
            if (folderLastId == id) folderLastId = null;
            if (folderOpenId == id) CloseFolder(false);
            PlaySound("back");
            FoldersChanged();
            GoBackTo("folders", "fadd");
        }

        private string FolderPicturesDir { get { return Path.Combine(plugin.GetPluginUserDataPath(), "folders"); } }

        /// <summary>A folder's photo: pick one from your PC; a copy is kept in the add-on's folder (moving or deleting the original doesn't lose it).</summary>
        private void ChooseFolderPicture(string id)
        {
            string start = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            PlaySound("select");
            OpenPicker(Loc("LOC_KX_FolderPhotoTitle", "Choose a photo for the folder"), false, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }, start, "folder", file =>
            {
                var f = FolderById(id);
                folderEditId = id;
                if (f == null) { GoBackTo("folders", "fadd"); return; }
                try
                {
                    Directory.CreateDirectory(FolderPicturesDir);
                    string dest = Path.Combine(FolderPicturesDir, "folder-" + DateTime.Now.Ticks + Path.GetExtension(file).ToLowerInvariant());
                    File.Copy(file, dest, true);
                    string old = f.Picture;
                    f.Picture = dest;
                    SaveSettings();
                    DropFolderPicture(old);
                }
                catch (Exception ex)
                {
                    logger.Warn("Kippix: folder photo: " + ex.Message);
                    try { ShowNotice(Loc("LOC_KX_FolderPhotoFailedTitle", "FOLDER"), Loc("LOC_KX_FolderPhotoFailed", "That picture couldn't be used"), Path.GetFileName(file), "", null); } catch (Exception) { }
                }
                FoldersChanged();
                GoBackTo("folder", "fphoto");
            });
        }

        /// <summary>The previous copy of a photo goes, unless another folder, a saved layout or a profile still uses it.</summary>
        private void DropFolderPicture(string old)
        {
            try
            {
                if (string.IsNullOrEmpty(old) || !old.StartsWith(FolderPicturesDir, StringComparison.OrdinalIgnoreCase)) return;
                if (FolderList.Any(x => string.Equals(x.Picture, old, StringComparison.OrdinalIgnoreCase)) || BgImageInUseElsewhere(old)) return;
                if (File.Exists(old)) File.Delete(old);
            }
            catch (Exception) { }
        }

        // ---- Games picked by hand ----

        private void BuildFolderPickRows()
        {
            var rows = FolderRows;
            var f = FolderById(folderEditId);
            if (f == null || f.Id == AllGamesId) { rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fgone", Text = Loc("LOC_KX_FolderGone", "That folder isn't there any more") }); return; }
            if (folderPickIndex == null)
            {
                // (made once while the page is open: typing a search only reads it)
                folderPickIndex = api.Database.Games.Where(g => g != null && !g.Hidden && !string.IsNullOrEmpty(g.Name) && ProfileAllowsGame(g))
                                     .OrderBy(g => g.SortingName ?? g.Name, StringComparer.CurrentCultureIgnoreCase)
                                     .Select(g => Tuple.Create(g.Id, g.Name, SearchNorm(g.Name))).ToList();
            }
            rows.Add(new PickRow
            {
                Key = "fpquery", Icon = "search",
                Text = folderPickQuery.Length == 0 ? Loc("LOC_KX_FolderPickSearch", "Search your games") : folderPickQuery,
                Sub = Loc("LOC_KX_FolderPickSearchSub", ""),
                Command = new RelayCommand(_ => { PlaySound("select"); AskFolderPickQuery(); })
            });
            var names = new Dictionary<Guid, string>();
            foreach (var t in folderPickIndex) names[t.Item1] = t.Item2;
            rows.Add(new PickRow { IsHeader = true, Key = "fphead", Text = FolderPickedHead(f) });
            if (f.Games.Count == 0) rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fpnone", Text = Loc("LOC_KX_FolderPickedNone", "None yet: switch games on below") });
            foreach (string s in f.Games)
            {
                Guid gid;
                if (!Guid.TryParse(s, out gid)) continue;
                string name;
                if (!names.TryGetValue(gid, out name))
                {
                    Game g = null;
                    try { g = api.Database.Games.Get(gid); } catch (Exception) { }
                    name = g != null ? g.Name : Loc("LOC_KX_FolderPickedMissing", "(not in your library any more)");
                }
                rows.Add(FolderPickRow(f.Id, gid, name, true));
            }
            var picked = new HashSet<string>(f.Games);
            var words = SearchNorm(folderPickQuery).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            rows.Add(new PickRow { IsHeader = true, Text = words.Length > 0 ? Loc("LOC_KX_FolderPickMatches", "MATCHES") : Loc("LOC_KX_LibGroupGames", "YOUR GAMES") });
            const int MaxShown = 100;   // (a big library: search for the rest)
            int shown = 0, total = 0;
            foreach (var t in folderPickIndex)
            {
                if (picked.Contains(t.Item1.ToString())) continue;
                if (words.Length > 0 && !words.All(w => t.Item3.Contains(w))) continue;
                total++;
                if (shown >= MaxShown) continue;
                rows.Add(FolderPickRow(f.Id, t.Item1, t.Item2, false));
                shown++;
            }
            if (total == 0) rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fpnomatch", Text = words.Length > 0 ? Loc("LOC_KX_SettingsSearchNone", "Nothing matches that") : Loc("LOC_KX_FolderPickAllIn", "Every game is in it already") });
            else if (total > shown) rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "fpmore", Text = string.Format(Loc("LOC_KX_FolderPickMore", "{0} more: search to find them"), (total - shown).ToString("N0")) });
        }

        private string FolderPickedHead(HomeFolder f)
        {
            return string.Format(Loc("LOC_KX_FolderPickedHead", "PICKED BY HAND · {0}"), f.Games.Count);
        }

        /// <summary>A game's switch: on puts it in the folder, off takes it out (changed where it stands, the page isn't built again).</summary>
        private PickRow FolderPickRow(string folderId, Guid gameId, string name, bool on)
        {
            string gid = gameId.ToString();
            var row = new PickRow
            {
                Key = "pick:" + gid, Icon = "controller", Text = name, IsSwitch = true, IsSelected = on,
                Sub = on ? Loc("LOC_KX_FolderSrcIn", "In this folder") : Loc("LOC_KX_Off", "Off")
            };
            row.Command = new RelayCommand(_ =>
            {
                var f = FolderById(folderId);
                if (f == null) return;
                bool now = !f.Games.Contains(gid);
                if (now) f.Games.Add(gid); else f.Games.Remove(gid);
                row.IsSelected = now;
                row.SetSub(now ? Loc("LOC_KX_FolderSrcIn", "In this folder") : Loc("LOC_KX_Off", "Off"));
                var head = FolderRows.FirstOrDefault(r => r.Key == "fphead");
                if (head != null) head.SetText(FolderPickedHead(f));
                SaveSettings();
                PlaySound("move");
                FoldersChanged();
            });
            return row;
        }

        private void AskFolderPickQuery()
        {
            OpenKeyboard(Loc("LOC_KX_FolderPickSearch", "Search your games"), folderPickQuery, typed =>
            {
                folderPickQuery = (typed ?? "").Trim();
                if (optionsPage != "folderpicks") return;
                BuildFolderRows();
                ui.BeginInvoke(new Action(() =>
                {
                    // the first match (the search row when nothing matched)
                    var first = FolderRows.SkipWhile(r => r.Key != "fphead").FirstOrDefault(r => r.Key != null && r.Key.StartsWith("pick:", StringComparison.Ordinal) && !r.IsSelected);
                    if (first == null || !UiTools.FocusCommandParameter("KX_OptionsPanel", first.Key)) UiTools.FocusCommandParameter("KX_OptionsPanel", "fpquery");
                }), DispatcherPriority.ContextIdle);
            }, typed =>
            {
                folderPickQuery = (typed ?? "").Trim();
                if (optionsPage == "folderpicks") BuildFolderRows();
            }, null, 40);
        }
    }
}
