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
            if (justRowHeight > 0) p.TargetRowHeight = justRowHeight;
            p.EdgePadding = justEdge;
        }

        private double justRowHeight, justEdge = 16;

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
            var panel = now as JustifiedPanel;
            if (!ReferenceEquals(panel, justPanel))
            {
                if (justPanel != null) justPanel.ContainersGenerated -= OnJustContainers;
                justPanel = panel;
                if (panel != null) panel.ContainersGenerated += OnJustContainers;
            }

            // the list's size: the screen's width less a margin each side, and (as the grid) the height between the logo and
            // info under the top bar and the bottom edge. With the logo, the info line and the hints all switched off there's
            // nothing above the games to keep clear of, and the mosaic takes that room too.
            var root = VisualTreeHelper.GetParent(list) as FrameworkElement;
            double screenW = root != null && root.ActualWidth > 0 ? root.ActualWidth : 1920;
            double screenH = root != null && root.ActualHeight > 0 ? root.ActualHeight : 1080;
            double above = settings.ShowLogo || settings.ShowGameInfo || settings.ShowButtonHints ? 230 : 56;
            double avail = Math.Max(300, screenH - 104 - above - Math.Max(0, settings.CardOffset) - 16);
            double rowH = Math.Floor(avail / JustRowsNow);
            double grow = settings.Look != null && settings.Look.FocusScale > 100 ? settings.Look.FocusScale / 100.0 - 1 : 0.06;
            justRowHeight = rowH;
            justEdge = Math.Round(rowH * grow * 0.6 + 8);   // (the highlighted tile grows sideways too: room at the edges)
            if (panel != null)
            {
                panel.TargetRowHeight = rowH;
                panel.EdgePadding = justEdge;
                panel.SmoothScroll = !ReduceMotionNow;
            }
            double w = Math.Floor(screenW - 128 + justEdge * 2);
            double h = Math.Round(rowH * JustRowsNow + TopRoomFor(rowH));
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
                if (justPanel != null) justPanel.ContainersGenerated -= OnJustContainers;
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
            list.SelectedIndex = index;
            if (justPanel != null) justPanel.ScrollToIndex(index);
            list.UpdateLayout();   // (the panel makes the selected game's tile, wherever it is)
            var c = list.ItemContainerGenerator.ContainerFromIndex(index) as UIElement;
            if (c != null) Keyboard.Focus(c);
        }

        /// <summary>New tiles in the mosaic: dressed like any other card (badges; shapes and wide art taken off reused ones).</summary>
        private void OnJustContainers(object sender, EventArgs e)
        {
            if (AnyBadges || badgesEverOn) RefreshBadgesSoon();
            if (tileArtWasOn || shapesWereOn || spacingWasSet) RefreshTileArtSoon();
        }
    }
}
