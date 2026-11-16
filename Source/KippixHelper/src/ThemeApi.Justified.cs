using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Games shown as: Mosaic (HomeLayoutMode 6). Every game keeps its cover's own shape (a tall box, a wide banner, a square
    /// icon) and the rows are packed edge to edge at one height each, like a photo site's gallery: nothing cut off, nothing
    /// stretched. Playnite's own panel gives every game the same size, so while Mosaic is on the games list gets our panel
    /// (JustifiedPanel) as its items panel, and Playnite's template back when it's left. The list itself (its selection, A / X /
    /// Start, the filters and the sort) stays Playnite's.
    /// </summary>
    public partial class ThemeApi
    {
        private CoverSizes coverSizes;
        private ListBox justList;                      // the list our panel is in now (null: Playnite's own panel)
        private ItemsPanelTemplate justOriginalPanel;  // Playnite's template for that list, to put back
        private ItemsPanelTemplate justTemplate;
        private JustifiedPanel justPanel;
        private bool justPrewarmed;

        private bool JustifiedMode { get { return LayoutNow == 6; } }
        /// <summary>The games scroll downwards: the grid, the list and the mosaic.</summary>
        private bool ScrollsDown { get { return SideMode || LayoutNow == 6; } }

        public ICommand CycleJustRowsCommand { get; private set; }
        public bool JustSettingsShown { get { return settings.HomeLayoutMode == 6; } }
        public string JustRowsText { get { return JustRowsNow.ToString(); } }
        private int JustRowsNow { get { return Math.Max(2, Math.Min(5, settings.JustRows)); } }

        private void InitJustified()
        {
            CycleJustRowsCommand = new RelayCommand(_ => { settings.JustRows = JustRowsNow >= 5 ? 2 : JustRowsNow + 1; SaveSettings(); PlaySound("move"); HomeModeChanged(); });
            JustifiedPanel.Setup = SetupJustPanel;
            InitJustPerf();
            OpenLibraryViewCommand = new RelayCommand(_ => OpenLibraryView());
            CloseLibraryViewCommand = new RelayCommand(_ => CloseLibraryView(true));
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyUpEvent, new KeyEventHandler((s, e) => { if (e.Key == Key.Down) libraryDownIsDown = false; }), true);
        }

        private CoverSizes Covers
        {
            get
            {
                if (coverSizes != null) return coverSizes;
                string dir = null;
                try { dir = plugin.GetPluginUserDataPath(); } catch (Exception) { }
                coverSizes = new CoverSizes(api, dir, ui ?? (Application.Current != null ? Application.Current.Dispatcher : null));
                // shapes learnt after the rows were worked out: worked out again (a few at a time, never per cover)
                coverSizes.Changed += () => { if (justPanel != null) justPanel.InvalidateModel(); };
                return coverSizes;
            }
        }

        /// <summary>A little after start-up (full screen): every cover's shape is read once on a worker, so the mosaic comes up already in shape.</summary>
        private void JustPrewarmSoon()
        {
            if (justPrewarmed || ui == null || !IsFullscreen) return;
            justPrewarmed = true;
            var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(JustifiedMode ? 0.2 : 4) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                try { Covers.Prewarm(api.Database.Games.ToList()); }
                catch (Exception ex) { logger.Warn("Kippix: mosaic: pre-warm: " + ex.Message); }
            };
            t.Start();
        }

        // ---------------- the panel ----------------

        private static PropertyInfo entryGameProp;
        private static Type entryGameType;

        /// <summary>The game of one of the list's entries (Playnite's own entry type has it as Game).</summary>
        private static Game EntryGame(object entry)
        {
            var g = entry as Game;
            if (g != null || entry == null) return g;
            var t = entry.GetType();
            if (!ReferenceEquals(t, entryGameType)) { entryGameType = t; entryGameProp = t.GetProperty("Game", BindingFlags.Public | BindingFlags.Instance); }
            return entryGameProp != null ? entryGameProp.GetValue(entry, null) as Game : null;
        }

        /// <summary>Every new JustifiedPanel (the list makes one from the template each time it's built).</summary>
        private void SetupJustPanel(JustifiedPanel p)
        {
            p.AspectOf = entry =>
            {
                Game g = null;
                try { g = EntryGame(entry); } catch (Exception) { }
                return Covers.Get(g);
            };
            p.SmoothScroll = !ReduceMotionNow;
            if (justRowHeight > 0) p.RowHeight = justRowHeight;
            p.EdgePadding = justEdge;
            p.BottomRoom = JustBottomRoom;
        }

        private const double JustBottomRoom = 12;      // under the last row in view, inside the list: the highlight's edge and glow
        private double justRowHeight, justEdge = 16;
        private double justLeft = 48, justTop, justBottom = 16;   // the list's own margins (CardsMargin)

        /// <summary>From ApplyCardLayout, before anything that needs Playnite's own panel. True: Mosaic is on and has sized the list.</summary>
        private bool JustifiedLayout(ListBox list)
        {
            if (!JustifiedMode)
            {
                if (justList != null) RestoreJustified(list);
                if (justRelink != null) RelinkListPanel();
                return false;
            }
            justRelink = null;
            try { ApplyJustified(list); }
            catch (Exception ex) { logger.Warn(ex, "Kippix: mosaic: layout failed"); }
            return true;
        }

        private void ApplyJustified(ListBox list)
        {
            var now = UiTools.FindChild<VirtualizingPanel>(list);
            if (!(now is JustifiedPanel))
            {
                if (now == null) return;   // (the list isn't built yet: the next pass)
                if (justList != null && !ReferenceEquals(justList, list)) { justList = null; justOriginalPanel = null; }   // (Playnite made a new list: that one's own template)
                if (justOriginalPanel == null) justOriginalPanel = list.ItemsPanel;   // remembered once
                if (justTemplate == null) justTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(JustifiedPanel)));
                list.ItemsPanel = justTemplate;
                LetGoOfPanel(now);
                SetListBoxExPanel(list, null);   // (Playnite's list forgets its own panel: the mouse wheel then scrolls ours)
                justList = list;
                logger.Info("Kippix: mosaic: panel swapped in (was " + now.GetType().Name + ", " + list.Items.Count + " games)");
                now = UiTools.FindChild<VirtualizingPanel>(list);
                if (!(now is JustifiedPanel)) { list.UpdateLayout(); now = UiTools.FindChild<VirtualizingPanel>(list); }
                JustPrewarmSoon();
            }
            justList = list;
            if (!ReferenceEquals(list, libraryHeaderList))
            {
                libraryHeaderList = list;
                list.SelectionChanged += (so, se) => LibraryHeaderFor(list.SelectedItem);   // the library view's header names the selected game
            }
            var panel = now as JustifiedPanel;
            if (!ReferenceEquals(panel, justPanel))
            {
                if (justPanel != null) { justPanel.ContainersGenerated -= OnJustContainers; justPanel.MarginsChanged -= OnJustMargins; }
                justPanel = panel;
                if (panel != null) { panel.ContainersGenerated += OnJustContainers; panel.MarginsChanged += OnJustMargins; }
            }

            // The list's size. Across: the pictures run from the left margin every home screen has (72) to the clock's right
            // edge. Down: a whole number of rows, all one height, between the logo and info under the top bar and the bottom
            // edge (with the logo, the info line and the hints all switched off, from just under the top bar). The library view
            // has the whole screen under the top bar's line: its header is on that line (Main.xaml, KX_LibraryHeader).
            var root = VisualTreeHelper.GetParent(list) as FrameworkElement;
            double screenW = root != null && root.ActualWidth > 0 ? root.ActualWidth : 1920;
            double screenH = root != null && root.ActualHeight > 0 ? root.ActualHeight : 1080;
            bool lib = LibraryOpen;
            double mx = panel != null ? panel.TileMarginX : 0, my = panel != null ? panel.TileMarginY : 0;   // (a tile's own margins: its slot is its picture plus these)
            double picsTop = lib ? 108 : 104 + (settings.ShowLogo || settings.ShowGameInfo || settings.ShowButtonHints ? 230 : 56);
            double picsBottom = screenH - (lib ? 30 : 16 + Math.Max(0, settings.CardOffset));
            double picsLeft = 72, picsRight = screenW - 60;
            int rowsNow = JustRowsNow;
            double rowH = Math.Floor((Math.Max(300, picsBottom - picsTop) + my) / rowsNow);
            double grow = settings.Look != null && settings.Look.FocusScale > 100 ? settings.Look.FocusScale / 100.0 - 1 : 0.06;
            justRowHeight = rowH;
            justEdge = Math.Round(rowH * grow * 0.6 + 8);   // (the highlighted tile grows sideways too: room at the edges)
            if (panel != null)
            {
                panel.RowHeight = rowH;
                panel.EdgePadding = justEdge;
                panel.BottomRoom = JustBottomRoom;
                panel.SmoothScroll = !ReduceMotionNow;
            }
            double w = Math.Round(picsRight - picsLeft + mx + justEdge * 2);
            double h = Math.Round(rowH * rowsNow + TopRoomFor(rowH) + JustBottomRoom);
            justLeft = Math.Round(picsLeft - mx / 2 - justEdge);
            justBottom = Math.Round(screenH - (picsTop - my / 2 + rowH * rowsNow) - JustBottomRoom);
            // (the list is in the screen's part under the top bar's row, 104 high: where it starts above that, a top margin of
            // less than nothing, or WPF cuts the list off there: the highlighted tile's top edge, in the first row)
            justTop = Math.Min(0, Math.Floor(screenH - justBottom - h - 104));
            if (Math.Abs(h - cardsHeight) >= 1) { cardsHeight = h; Notify("CardsHeight"); ApplyHomeLayoutSoon(); }
            if (double.IsNaN(cardsWidth) || Math.Abs(w - cardsWidth) >= 1) { cardsWidth = w; Notify("CardsWidth"); }
            if (gridTilesRoom != 0) gridTilesRoom = 0;
            Notify("CardsMargin");
            Notify("CardsHAlign");
        }

        /// <summary>Leaving Mosaic: Playnite's own panel template back on the list, and its list told about the new panel.</summary>
        private void RestoreJustified(ListBox list)
        {
            try
            {
                var was = justList;
                var template = justOriginalPanel;
                if (justPanel != null) { justPanel.ContainersGenerated -= OnJustContainers; justPanel.MarginsChanged -= OnJustMargins; }
                justPanel = null;
                justList = null;
                justOriginalPanel = null;
                // (the size the mosaic gave the list isn't the row's or the grid's: theirs is worked out again)
                if (!double.IsNaN(cardsWidth)) { cardsWidth = double.NaN; Notify("CardsWidth"); }
                Notify("CardsMargin");
                Notify("CardsHAlign");
                if (was == null || template == null || PresentationSource.FromVisual(was) == null) return;   // (that list has gone with its screen)
                var old = UiTools.FindChild<VirtualizingPanel>(was);
                if (!(old is JustifiedPanel)) return;
                was.ItemsPanel = template;
                LetGoOfPanel(old);
                var now = UiTools.FindChild<VirtualizingPanel>(was);
                if (now == null || now is JustifiedPanel) { was.UpdateLayout(); now = UiTools.FindChild<VirtualizingPanel>(was); }
                SetListBoxExPanel(was, now);
                justRelink = now == null || now is JustifiedPanel ? was : null;   // (a list out of sight makes its panel later: told then)
                logger.Info("Kippix: mosaic: panel swapped out (now " + (now != null ? now.GetType().Name : "none yet") + ")");
                ApplyCardLayoutSoon();
            }
            catch (Exception ex) { logger.Warn(ex, "Kippix: mosaic: putting Playnite's panel back failed"); }
        }

        private ListBox justRelink;

        private void RelinkListPanel()
        {
            var list = justRelink;
            if (list == null) return;
            if (PresentationSource.FromVisual(list) == null) { justRelink = null; return; }
            var now = UiTools.FindChild<VirtualizingPanel>(list);
            if (now == null || now is JustifiedPanel) return;
            justRelink = null;
            SetListBoxExPanel(list, now);
        }

        /// <summary>
        /// A panel that has just been replaced still listens to the list's generator, and (WPF) would clear the generator the
        /// next time the games change: the new panel's tiles with it. Asking it for its children makes it let go now, while
        /// there's nothing to lose.
        /// </summary>
        private static void LetGoOfPanel(Panel old)
        {
            try { if (old != null) { var gone = old.Children; } }
            catch (Exception) { }
        }

        /// <summary>
        /// Playnite's ListBoxEx finds its FullscreenTilePanel once, when it's loaded, keeps it in a private field and listens to it
        /// (to put the highlight on the selected game once its tile is made). Our swap gives the list a new panel each way, so that
        /// field is set by hand: null while ours is in (JustifiedPanel does that job itself), the new FullscreenTilePanel after.
        /// </summary>
        private static void SetListBoxExPanel(ListBox list, Panel panel)
        {
            try
            {
                FieldInfo field = null;
                MethodInfo handler = null;
                for (Type t = list.GetType(); t != null && field == null; t = t.BaseType)
                {
                    field = t.GetField("itemsPanel", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (field != null) handler = t.GetMethod("ItemsPanel_InternalChildrenGenerated", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                }
                if (field == null)
                {
                    // (a Playnite that keeps it some other way: its own Loaded handler finds the panel again)
                    logger.Warn("Kippix: mosaic: Playnite's list has no itemsPanel field; raising Loaded on it instead");
                    if (panel != null) list.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    return;
                }
                object old = field.GetValue(list);
                if (ReferenceEquals(old, panel)) return;
                if (old != null && handler != null)
                {
                    var ev = old.GetType().GetEvent("InternalChildrenGenerated");
                    if (ev != null) ev.RemoveEventHandler(old, Delegate.CreateDelegate(ev.EventHandlerType, list, handler));
                }
                if (panel != null && !field.FieldType.IsInstanceOfType(panel)) panel = null;
                field.SetValue(list, panel);
                if (panel != null && handler != null)
                {
                    var ev = panel.GetType().GetEvent("InternalChildrenGenerated");
                    if (ev != null) ev.AddEventHandler(panel, Delegate.CreateDelegate(ev.EventHandlerType, list, handler));
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: mosaic: Playnite's list and its panel: " + ex.Message); }
        }

        // ---------------- moving in it ----------------

        private double justStickyX = double.NaN;   // where a run of Up / Down presses started across the screen
        private int justMovedTo = -1;
        private DateTime justMovedAt;

        /// <summary>
        /// The arrow keys / D-pad in the mosaic (Page Up / Page Down, LT / RT: a screenful). The rows don't line up in columns, so
        /// Up / Down go to the game under or over where you started across the screen (kept while you keep going up or down),
        /// Left / Right to the next game in the list, off the end of a row onto the next. False (the key goes on to the rest):
        /// Up from the first row (the top bar takes it) and Left on the first game.
        /// </summary>
        private bool JustKey(KeyEventArgs e)
        {
            if (!JustifiedMode || justPanel == null || justList == null || confirmOpen || kbOpen || freeMove || IsMasterMoving) return false;
            bool page = e.Key == Key.PageUp || e.Key == Key.PageDown;
            if (e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right && !page) return false;
            var list = justList;
            if (!list.IsVisible || !list.IsKeyboardFocusWithin || list.Items.Count == 0) return false;
            if (IsPadRelease(e)) return true;   // a let-go: nothing (and not through to the list either)
            // from the highlighted game (the selected one, unless the highlight was put elsewhere)
            int cur = -1;
            var on = Keyboard.FocusedElement as Visual;
            var item = on as ListBoxItem ?? (on != null ? FindAncestor<ListBoxItem>(on, list) : null);
            if (item != null) cur = list.ItemContainerGenerator.IndexFromContainer(item);
            if (cur < 0) cur = list.SelectedIndex;
            if (cur != justMovedTo) justStickyX = double.NaN;   // (the highlight got here some other way: a new start)
            // a held button: a steady pace (Playnite's list does this for its own panel; these presses never reach it)
            if ((DateTime.Now - justMovedAt).TotalMilliseconds < 100) return true;
            int next;
            if (page)
            {
                next = cur;
                for (int i = 0; i < JustRowsNow; i++)
                {
                    int n = justPanel.IndexFrom(next, e.Key == Key.PageDown ? Key.Down : Key.Up, ref justStickyX);
                    if (n < 0) break;
                    next = n;
                }
                if (next == cur) return true;
            }
            else next = justPanel.IndexFrom(cur, e.Key, ref justStickyX);
            if (next < 0)
            {
                if (e.Key == Key.Up) { justStickyX = double.NaN; return false; }   // the first row: up to the top bar
                if (e.Key == Key.Left) return false;                               // the first game: nothing before it
                return true;                                                       // the last row / the last game: stay
            }
            justMovedAt = DateTime.Now;
            justMovedTo = next;
            JustGoTo(list, next);
            return true;
        }

        /// <summary>Selects a game in the mosaic, scrolls its row in and puts the highlight on its tile.</summary>
        private void JustGoTo(ListBox list, int index)
        {
            if (index < 0 || index >= list.Items.Count) return;
            bool perf = JustifiedPanel.Perf;
            long t0 = perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0, t1 = 0, t2 = 0, t3 = 0;
            if (perf) JustPerfFrames(true);
            list.SelectedIndex = index;
            if (perf) t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (justPanel != null) justPanel.ScrollToIndex(index);
            if (perf) t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            list.UpdateLayout();   // (the panel makes the selected game's tile, wherever it is)
            if (perf) t3 = System.Diagnostics.Stopwatch.GetTimestamp();
            var c = list.ItemContainerGenerator.ContainerFromIndex(index) as UIElement;
            if (c != null) Keyboard.Focus(c);
            if (perf)
            {
                double f = System.Diagnostics.Stopwatch.Frequency / 1000.0;
                long t4 = System.Diagnostics.Stopwatch.GetTimestamp();
                pfSelect.Add((t1 - t0) / f); pfScroll.Add((t2 - t1) / f); pfLayout.Add((t3 - t2) / f); pfFocus.Add((t4 - t3) / f); pfMove.Add((t4 - t0) / f);
                JustPerfSettle();
            }
        }

        // =====================================================================
        // The library view: every game as a mosaic, over the layout that's set
        // =====================================================================
        // Like an opened folder, it's somewhere you go and come back from, not a setting: LayoutNow says Mosaic while it's open
        // and the saved layout stays what it was. Playnite's filter and sort are left as they are. Opened by OpenLibraryViewCommand
        // and, in Row, by Down when there's nothing further below the games; B (or Home) closes it, on the game you were on.

        private bool libraryOpen;
        private int libraryFromMode;                 // the layout it was opened over (a layout change since: it's closed)
        private bool libraryDownIsDown, libraryDownHeld;
        private DateTime libraryDownAt;

        private bool LibraryOpen { get { return libraryOpen && settings.HomeLayoutMode == libraryFromMode; } }
        public bool IsLibraryOpen { get { return LibraryOpen; } }
        public ICommand OpenLibraryViewCommand { get; private set; }
        public ICommand CloseLibraryViewCommand { get; private set; }
        public bool LibraryDownOpens { get { return settings.LibraryDownOpens; } set { if (SetSetting(ref settings.LibraryDownOpens, value)) Notify("LibraryDownOpens"); } }

        // While it's open the home screen's own logo, info line, hints, filter name and widgets are away (they'd sit half under
        // the top of the screen, over the games), and one line on the top bar says what's shown and which game the highlight is
        // on (Main.xaml, KX_LibraryHeader). Closing it puts them back where they were.
        private string libraryTitle = "", libraryInfo = "";
        private ListBox libraryHeaderList;
        public string LibraryTitle { get { return libraryTitle; } }
        public string LibraryInfo { get { return libraryInfo; } }

        /// <summary>The header's game: its name, and its platform, time played and when it was last played.</summary>
        private void LibraryHeaderFor(object entry)
        {
            if (!LibraryOpen) return;
            string title = "", info = "";
            try
            {
                var g = EntryGame(entry);
                if (g != null)
                {
                    title = g.Name ?? "";
                    var parts = new List<string>();
                    var platforms = g.Platforms;
                    if (platforms != null && platforms.Count > 0 && platforms[0] != null && !string.IsNullOrEmpty(platforms[0].Name)) parts.Add(platforms[0].Name);
                    if (g.Playtime >= 60) parts.Add(string.Format(Loc("LOC_KX_LibPlayed", "{0} played"), KippixOverlay.GameData.FormatPlaytime(g.Playtime)));
                    if (g.LastActivity.HasValue)
                    {
                        string when = KippixOverlay.GameData.FormatLastPlayed(g.LastActivity);
                        if (when.StartsWith("Today", StringComparison.Ordinal) || when.StartsWith("Yesterday", StringComparison.Ordinal)) when = char.ToLowerInvariant(when[0]) + when.Substring(1);
                        parts.Add(string.Format(Loc("LOC_KX_LibLast", "Last played {0}"), when));
                    }
                    else parts.Add(Loc("LOC_KX_LibNever", "Not played yet"));
                    info = string.Join("   \u00B7   ", parts.ToArray());
                }
            }
            catch (Exception) { }
            if (title != libraryTitle) { libraryTitle = title; Notify("LibraryTitle"); }
            if (info != libraryInfo) { libraryInfo = info; Notify("LibraryInfo"); }
        }

        /// <summary>
        /// The header starts at the games' left edge (72). It's in the top bar, after the profile button's corner: while that is
        /// faded out (Auto-hide top bar) the header is drawn over it, and beside it while it shows.
        /// </summary>
        private void LibraryHeaderPlace()
        {
            try
            {
                if (!LibraryOpen) return;
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var header = win != null ? UiTools.FindByName(win, "KX_LibraryHeader") : null;
                var corner = win != null ? UiTools.FindByName(win, "KX_TopLeft") : null;
                if (header == null || corner == null) return;
                bool cornerAway = settings.AutoHideTopBar && !topBarActive && openPanel == null;
                double x = cornerAway || corner.ActualWidth < 1 ? 8 - corner.ActualWidth : 14;
                var shift = header.RenderTransform as TranslateTransform;
                if (shift == null || shift.IsFrozen) { header.RenderTransform = new TranslateTransform(x, 0); return; }
                if (ReduceMotionNow || !header.IsVisible) { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = x; }
                else shift.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(x, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = new System.Windows.Media.Animation.CubicEase() });
            }
            catch (Exception ex) { logger.Warn("Kippix: mosaic: library view, its header: " + ex.Message); }
        }

        /// <summary>Opened or closed: the home screen's own parts go or come back (the logo and info: BoardApplyMode).</summary>
        private void LibraryLandingChanged(bool closed)
        {
            Notify("ShowWidgets");
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                // (the filter name's Visibility is bound, so it's put out of sight by its opacity)
                var filter = UiTools.FindByName(win, "KX_FilterAbove");
                if (filter != null)
                {
                    filter.BeginAnimation(UIElement.OpacityProperty, null);
                    if (closed) filter.ClearValue(UIElement.OpacityProperty); else filter.Opacity = 0;
                }
                if (!closed || ReduceMotionNow) return;
                // back on the home screen: its logo, info and filter name come in softly, once the row is back in place
                foreach (string n in new[] { "KX_GameInfo", "KX_FilterAbove" })
                {
                    var el = UiTools.FindByName(win, n);
                    if (el == null) continue;
                    var fade = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
                    fade.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    fade.KeyFrames.Add(new System.Windows.Media.Animation.DiscreteDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
                    fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(el.Opacity, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
                    el.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: mosaic: library view, the home screen's own parts: " + ex.Message); }
        }

        /// <summary>Over Row, Grid or the List (the layouts that show Playnite's own list), on the home screen.</summary>
        private bool OpenLibraryView()
        {
            if (!IsFullscreen || LibraryOpen || settings.HomeLayoutMode < 0 || settings.HomeLayoutMode > 2) return false;
            if (detailsOpen || confirmOpen || kbOpen || freeMove || IsMasterMoving || boardEditing) return false;
            if (openPanel != null) ClosePanels();
            LeaveExtraRows(false);
            libraryFromMode = settings.HomeLayoutMode;
            libraryOpen = true;
            carWhy = "the library view was opened";
            PlaySound("select");
            logger.Info("Kippix: mosaic: library view opened (over layout " + libraryFromMode + ")");
            JustPerfFrames(true);
            try { var games = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox; if (games != null) LibraryHeaderFor(games.SelectedItem); } catch (Exception) { }
            HomeModeChanged();
            LibraryHeaderPlace();
            Notify("IsLibraryOpen");
            LibraryLandingChanged(false);
            LibraryFocusSoon(0);
            return true;
        }

        /// <summary>Back to the home screen under it, the highlight on the same game. False when it wasn't open.</summary>
        private bool CloseLibraryView(bool sound)
        {
            bool was = LibraryOpen;
            libraryOpen = false;
            if (!was) return false;
            if (sound) PlaySound("back");
            carWhy = "the library view was closed";
            logger.Info("Kippix: mosaic: library view closed");
            JustPerfSummary("library view closed");
            JustPerfFrames(false);
            HomeModeChanged();
            Notify("IsLibraryOpen");
            LibraryLandingChanged(true);
            LibraryFocusSoon(0);
            return true;
        }

        /// <summary>After opening or closing: the highlight on the selected game's card, once the list has its panel for that layout.</summary>
        private void LibraryFocusSoon(int tries)
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (openPanel != null || detailsOpen || confirmOpen || kbOpen) return;
                    bool ready = LibraryOpen ? justList != null && justPanel != null : justList == null;
                    if (!ready && tries < 6) { LibraryFocusSoon(tries + 1); return; }
                    FocusSelectedCard();
                }
                catch (Exception ex) { logger.Warn("Kippix: mosaic: library view, highlight: " + ex.Message); }
            }), tries == 0 ? DispatcherPriority.ContextIdle : DispatcherPriority.ApplicationIdle);
        }

        /// <summary>B (Esc, Backspace) with the library view open, nothing else open: back to the home screen.</summary>
        private bool LibraryBackKey(KeyEventArgs e)
        {
            if (!LibraryOpen || confirmOpen || kbOpen || freeMove || IsMasterMoving || descReaderOpen || masterMoving != null || widgetMoving != null || boardEditing) return false;
            if (e.IsRepeat || !IsBackKey(e) || IsPadRelease(e)) return false;
            return CloseLibraryView(true);
        }

        /// <summary>Every Down press on the main window: was Down already down (a held button, sent again and again)?</summary>
        private void LibraryNoteDown()
        {
            var now = DateTime.Now;
            libraryDownHeld = libraryDownIsDown && (now - libraryDownAt).TotalMilliseconds < 1500;   // (a let-go that never arrived doesn't count for ever)
            libraryDownIsDown = true;
            libraryDownAt = now;
        }

        /// <summary>Row: Down on the games with no rows under them (the rows under them call LibraryDown from their last row).</summary>
        private bool LibraryDownKey(KeyEventArgs e)
        {
            if (e.Key != Key.Down || IsPadRelease(e)) return false;
            if (!settings.LibraryDownOpens || settings.HomeLayoutMode != 0 || LibraryOpen || ShowExtraRows || extraRowAt != 0) return false;
            if (confirmOpen || kbOpen || freeMove || IsMasterMoving) return false;
            var games = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems");
            if (games == null || !games.IsKeyboardFocusWithin) return false;
            return LibraryDown(e);
        }

        /// <summary>A Down with nothing further below, in Row: opens the library view (a new press only: holding Down stops at the bottom). True when it did.</summary>
        private bool LibraryDown(KeyEventArgs e)
        {
            if (!settings.LibraryDownOpens || settings.HomeLayoutMode != 0 || LibraryOpen) return false;
            if (e.IsRepeat || libraryDownHeld || IsPadRelease(e)) return false;
            return OpenLibraryView();
        }

        /// <summary>New tiles in the mosaic: dressed like any other card (badges; shapes and wide art taken off reused ones).</summary>
        private void OnJustMargins(object sender, EventArgs e)
        {
            ApplyCardLayoutSoon();   // (the list is sized from the tiles' own margins: known once the first tile is made)
        }

        private void OnJustContainers(object sender, EventArgs e)
        {
            if (AnyBadges || badgesEverOn) RefreshBadgesSoon();
            if (tileArtWasOn || shapesWereOn || spacingWasSet) RefreshTileArtSoon();
        }
    }
}
