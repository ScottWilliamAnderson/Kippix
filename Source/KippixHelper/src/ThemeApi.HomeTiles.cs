using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>A tile to the left of the games (Store, Achievements, Random game), the same size as a game card.</summary>
    public class HomeTile : Observable
    {
        public string Key { get; set; }
        public string Name { get; set; }
        private string sub = "";
        public string Sub { get { return sub; } set { Set(ref sub, value ?? "", "Sub"); } }
        private bool showDot;
        public bool ShowDot { get { return showDot; } set { Set(ref showDot, value, "ShowDot"); } }
        public ICommand Command { get; set; }
        public bool IsStore { get { return Key == "store"; } }
        public bool IsAch { get { return Key == "ach"; } }
        public bool IsRandom { get { return Key == "random"; } }
        public bool IsUpNext { get { return Key == "upnext"; } }
        public bool IsLibrary { get { return Key == "library"; } }   // (0.99.17)
        /// <summary>A game you pinned (its cover instead of an icon).</summary>
        public bool IsPinned { get { return Key != null && Key.StartsWith("pin:", StringComparison.Ordinal); } }
        internal Guid GameId;
        private ImageSource cover;
        public ImageSource Cover { get { return cover; } set { if (Set(ref cover, value, "Cover")) Notify("HasCover"); } }
        public bool HasCover { get { return cover != null; } }
    }

    /// <summary>
    /// Home: tiles before your games. Hidden (off), Auto show (go left past the first game and they slide in; move away and they
    /// slide out; hidden when Playnite starts) or Always show (pinned on the left while the games scroll beside them).
    /// The cover row is pushed right with a translate (so the cards keep their size).
    /// </summary>
    public partial class ThemeApi
    {
        public ObservableCollection<HomeTile> HomeTiles { get; private set; }
        public ICommand CycleHomeTilesModeCommand { get; private set; }
        private bool homeTilesShown, homeTilesHooked;
        private double tileLeft, tileTop, tileW = 300, tileH = 400, tileGap = 20;
        private TranslateTransform listShift;
        private double cardsLeft;          // Always show: room made for the tiles with a margin (so scrolling still keeps the selected card on screen)
        private double firstInset = -1;    // how far the first card sits in from the row's left edge
        private bool freeCountAsked;
        private int freeThisWeek = -1;

        private void InitHomeTileCommands()
        {
            HomeTiles = new ObservableCollection<HomeTile>();
            // (the list makes room above it for the tiles only on Always show: its size is worked out again)
            CycleHomeTilesModeCommand = new RelayCommand(_ => { settings.HomeTilesMode = (settings.HomeTilesMode + 1) % 3; SaveSettings(); NotifyHomeTileSettings(); ApplyHomeTiles(); ApplyCardLayoutSoon(); });
        }

        private void InitHomeTiles()
        {
            EventManager.RegisterClassHandler(typeof(ListBoxItem), Keyboard.KeyDownEvent, new KeyEventHandler(OnCardLeftKey), false);
            HookCardDressing();   // the list's play info (and wide art) filled in as each card goes on screen
            HookShuffleCards();   // Shuffle covers: a card's pick as it goes on screen
            BuildHomeTiles();
        }

        // ---------------- settings ----------------

        public string HomeTilesModeText
        {
            get { return settings.HomeTilesMode == 2 ? Loc("LOC_KX_TilesAlways", "Always show") : settings.HomeTilesMode == 1 ? Loc("LOC_KX_TilesAuto", "Auto show") : Loc("LOC_KX_TilesHidden", "Hidden"); }
        }
        public bool HomeTilesOn { get { return settings.HomeTilesMode != 0; } }
        /// <summary>Always there before the games (Always show). Pinned games come and go with the Store tile, like the other tiles.</summary>
        private bool AlwaysTiles { get { return settings.HomeTilesMode == 2; } }
        /// <summary>How the tiles show: the setting, except that with the tiles Hidden, pinned games still slide in (Auto show).</summary>
        private int TilesMode { get { return settings.HomeTilesMode == 0 && HomeTiles != null && HomeTiles.Any(t => t.IsPinned) ? 1 : settings.HomeTilesMode; } }
        public bool HomeTileStore { get { return settings.HomeTileStore; } set { SetTileSwitch(ref settings.HomeTileStore, value, "HomeTileStore"); } }
        public bool HomeTileAch { get { return settings.HomeTileAch; } set { SetTileSwitch(ref settings.HomeTileAch, value, "HomeTileAch"); } }
        public bool HomeTileRandom { get { return settings.HomeTileRandom; } set { SetTileSwitch(ref settings.HomeTileRandom, value, "HomeTileRandom"); } }
        public bool HomeTileUpNext { get { return settings.HomeTileUpNext; } set { SetTileSwitch(ref settings.HomeTileUpNext, value, "HomeTileUpNext"); } }
        public bool HomeTileLibrary { get { return settings.HomeTileLibrary; } set { SetTileSwitch(ref settings.HomeTileLibrary, value, "HomeTileLibrary"); } }

        private void SetTileSwitch(ref bool field, bool value, string name)
        {
            if (field == value) return;
            field = value;
            SaveSettings();
            Notify(name);
            BuildHomeTiles();
            ApplyHomeTiles();
            ApplyCardLayoutSoon();   // (the grid / list keep room for however many there are)
        }

        private void NotifyHomeTileSettings()
        {
            foreach (string n in new[] { "HomeTilesModeText", "HomeTilesOn", "HomeTileStore", "HomeTileAch", "HomeTileRandom", "HomeTileUpNext", "HomeTileLibrary" }) Notify(n);
        }

        // ---------------- what the theme binds to ----------------

        /// <summary>Stays true while they slide out, so they go smoothly instead of vanishing.</summary>
        public bool HomeTilesShown { get { return tilesVisible && HomeTiles != null && HomeTiles.Count > 0; } }
        private bool tilesVisible;
        private TranslateTransform tileSlide;
        private static Duration TileSlideTime { get { return Motion(220); } }
        public double HomeTilesLeft { get { return tileLeft; } }
        public double HomeTilesTop { get { return tileTop; } }
        public double HomeTileWidth { get { return tileW; } }
        public double HomeTileHeight { get { return tileH; } }
        public Thickness HomeTileMargin { get { return SideMode ? new Thickness(0, 0, 0, tileGap) : new Thickness(0, 0, tileGap, 0); } }

        private void NotifyHomeTiles()
        {
            foreach (string n in new[] { "HomeTilesShown", "HomeTilesLeft", "HomeTilesTop", "HomeTileWidth", "HomeTileHeight", "HomeTileMargin", "HomeTilesVertical" }) Notify(n);
        }

        private void BuildHomeTiles()
        {
            HomeTiles.Clear();
            // (Folder: before the folder tiles, and inside an opened folder before its games too, as the home layout it uses has them)
            AddPinnedTiles();
            if (settings.HomeTilesMode == 0) return;   // tiles off: only pinned games
            // (0.99.17) Library first: every game, in tabs, with a search
            if (settings.HomeTileLibrary) HomeTiles.Add(new HomeTile { Key = "library", Name = Loc("LOC_KX_Library", "Library"), Sub = Loc("LOC_KX_TileLibrarySub", "Every game, in tabs, with a search"), Command = new RelayCommand(_ => { LeaveTilesForPanel(); pageStack.Clear(); OpenPage("mylibrary", null, false); }) });
            if (settings.HomeTileStore) HomeTiles.Add(new HomeTile { Key = "store", Name = Loc("LOC_KX_Store", "Store"), Sub = Loc("LOC_KX_TileStoreSub", "Every store, best prices"), Command = new RelayCommand(_ => { LeaveTilesForPanel(); OpenStore(); }) });
            if (settings.HomeTileAch) HomeTiles.Add(new HomeTile { Key = "ach", Name = Loc("LOC_KX_Achievements", "Achievements"), Sub = Loc("LOC_KX_TileAchSub", "Your trophies"), Command = new RelayCommand(_ => { LeaveTilesForPanel(); OpenAchievementsView(); }) });
            if (settings.HomeTileUpNext) HomeTiles.Add(UpNextTile());   // (UpNext.cs)
        }

        /// <summary>After a settings change or a layout change: Always shows them, Hidden / Auto put the games back.</summary>
        private void ApplyHomeTiles()
        {
            if (HomeTiles == null) return;
            if (CarouselOn || LayoutNow == 6)   // (the mosaic is the games only)
            {
                // the carousel (Row: Lower with Loop, Tall, Full Screen): the tiles are items in it, before the first game
                bool was = homeTilesShown || tilesVisible;
                homeTilesShown = tilesVisible = false;
                SetCardsLeft(0);
                ShiftList(0);
                if (was) NotifyHomeTiles();
                return;
            }
            if (SideMode) { ShowSideTiles(false); return; }
            bool show = settings.HomeTilesMode == 2 && HomeTiles.Count > 0;
            if (TilesMode == 1 && homeTilesShown && HomeTiles.Count > 0) show = true;   // auto, and you're on them now
            SetTilesShown(show, false);
        }

        // ---------------- showing / hiding ----------------

        /// <summary>Left on the first game: the tiles slide in (Auto show) or take the highlight (Always show).</summary>
        private void OnCardLeftKey(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Handled || openPanel != null || detailsOpen || HomeTiles == null || HomeTiles.Count == 0) return;
                if (e.Key != Key.Left && e.Key != Key.Up) return;
                if (IsPadRelease(e)) return;   // (0.99.18) the press opens them; its let-go mustn't count as a second press
                var item = sender as ListBoxItem;
                if (item == null || !item.IsKeyboardFocusWithin) return;
                var owner = ItemsControl.ItemsControlFromItemContainer(item);
                if (owner == null || owner.Name != "PART_ListGameItems") return;
                if (SideMode) { if (SideTilesKey(item, owner, e.Key)) e.Handled = true; return; }
                if (LayoutNow == 6) return;
                if (e.Key != Key.Left || TilesMode == 0) return;
                if (owner.ItemContainerGenerator.IndexFromContainer(item) != 0) return;
                e.Handled = true;
                PlaySound("move");
                tilesOpenedAt = DateTime.Now;
                TilesLog("left on the first game: " + HomeTiles.Count + " tile(s), " + (TilesMode == 2 ? "always" : "auto"));
                SetTilesShown(true, true);
            }
            catch (Exception ex) { logger.Warn("Kippix: home tiles: " + ex.Message); }
        }

        private DateTime tilesOpenedAt = DateTime.MinValue, tilesLogAt = DateTime.MinValue;

        /// <summary>(0.99.18) What happened with the tiles before the games (at most every 10 s, so the log says why they didn't show).</summary>
        private void TilesLog(string what)
        {
            if ((DateTime.Now - tilesLogAt).TotalSeconds < 10) return;
            tilesLogAt = DateTime.Now;
            logger.Info("Kippix: home tiles: " + what);
        }

        /// <summary>(0.99.18) The controller's let-go of the Left that just opened the tiles: taken, so it can't move the highlight off them.</summary>
        private bool TilesReleaseKey(KeyEventArgs e)
        {
            if (!IsPadRelease(e) || (e.Key != Key.Left && e.Key != Key.Up)) return false;
            return (DateTime.Now - tilesOpenedAt).TotalMilliseconds < 800;
        }

        private void SetTilesShown(bool show, bool focus)
        {
            if (SideMode) { ShowSideTiles(focus); return; }
            bool measured = MeasureTiles();
            if (show && !measured) { TilesLog("the cards couldn't be measured"); return; }   // the cards aren't laid out yet: the next layout pass tries again
            homeTilesShown = show;
            if (AlwaysTiles && show)
            {
                ShiftList(0);
                SetCardsLeft(AlwaysMargin());
                ui.BeginInvoke(new Action(() => { MeasureTiles(); NotifyHomeTiles(); }), DispatcherPriority.Loaded);
            }
            else
            {
                SetCardsLeft(0);
                ShiftList(show ? HomeTiles.Count * (tileW + tileGap) : 0);
            }
            SlideTiles(show);
            NotifyHomeTiles();
            if (show)
            {
                UpdateStoreTile();
                HookHomeTiles();
                if (focus)
                    ui.BeginInvoke(new Action(() =>
                    {
                        var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles") as ItemsControl;
                        var last = HomeTiles.LastOrDefault();
                        var c = strip != null && last != null ? strip.ItemContainerGenerator.ContainerFromItem(last) as DependencyObject : null;
                        var btn = c != null ? FindFocusable(c) : null;
                        if (btn != null) Keyboard.Focus(btn);
                        // couldn't take the highlight (Auto show): don't leave them up with the games pushed aside
                        if (!AlwaysTiles && TilesMode == 1 && (strip == null || !strip.IsKeyboardFocusWithin)) { TilesLog("the tiles couldn't take the highlight (" + (strip == null ? "no strip" : btn == null ? "no button in it" : "focus went elsewhere") + ")"); SetTilesShown(false, false); }
                    }), DispatcherPriority.Loaded);
            }
        }

        /// <summary>Before a tile opens a panel: the highlight goes back to the games first, so closing the panel returns there.</summary>
        private void LeaveTilesForPanel()
        {
            if (CarouselOn) return;   // the carousel: the highlight stays on the tile (closing the panel comes back to it)
            if (!AlwaysTiles && TilesMode == 1) FocusSelectedCard();
            HideHomeTilesIfAuto();
        }

        private void HideHomeTilesIfAuto()
        {
            if (SideMode)
            {
                // the grid / Always show keep them up; the list on Auto show puts them away again
                if (ListAutoTiles && homeTilesShown) ListTilesShow(false, false);
                return;
            }
            if (TilesMode == 1 && homeTilesShown) SetTilesShown(false, false);
        }

        private static IInputElement FindFocusable(DependencyObject root)
        {
            var fe = root as UIElement;
            if (fe != null && fe.Focusable && fe.IsVisible) return fe;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var hit = FindFocusable(VisualTreeHelper.GetChild(root, i));
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Keys on the tiles: Right from the last goes back to the games; leaving them (Auto show) hides them.</summary>
        private void HookHomeTiles()
        {
            if (homeTilesHooked) return;
            var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
            if (strip == null) return;
            homeTilesHooked = true;
            strip.PreviewKeyDown += (s, e) =>
            {
                try
                {
                    if (e.Key != Key.Right && e.Key != Key.Down && e.Key != Key.Left) return;
                    var fe = Keyboard.FocusedElement as FrameworkElement;
                    var tile = fe != null ? fe.DataContext as HomeTile : null;
                    if (tile == null) return;
                    if (SideMode) { if (SideStripKey(tile, e.Key)) e.Handled = true; return; }
                    if (e.Key == Key.Left) return;
                    if (e.Key == Key.Down) { e.Handled = true; return; }   // nothing below the tiles
                    if (!ReferenceEquals(tile, HomeTiles.LastOrDefault())) return;
                    e.Handled = true;
                    PlaySound("move");
                    FocusSelectedCard();
                }
                catch (Exception) { }
            };
            strip.IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if ((bool)e.NewValue) return;
                // Auto show: gone once you've moved away (to the games, the top bar, a panel...)
                ui.BeginInvoke(new Action(() => { if (!strip.IsKeyboardFocusWithin) HideHomeTilesIfAuto(); }), DispatcherPriority.Input);
            };
        }

        private void FocusSelectedCard()
        {
            if (IsBoardMode && FocusBoard()) return;   // the Board has the games
            if (CarouselBackToItem()) return;           // Row's carousel highlighting a tile: back on that tile
            var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
            if (list == null) return;
            if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
            var c = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as UIElement;
            if (c == null && list.SelectedItem != null)
            {
                // not on screen yet (e.g. coming back up from the extra rows): bring it in, rather than jumping to the first game
                try { list.ScrollIntoView(list.SelectedItem); list.UpdateLayout(); } catch (Exception) { }
                c = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as UIElement;
                // Playnite's row doesn't bring a card it hasn't made into view by itself, so it's scrolled there (the carousel or not:
                // back from the game page after LB / RB had gone on to a game far along the row, the list itself took the focus,
                // no card looked chosen and the art was of a game out of sight)
                if (c == null) c = RealizeListItem(list, list.SelectedIndex, true);
            }
            if (c == null && list.SelectedItem == null) c = list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            if (c != null) Keyboard.Focus(c);
            else list.Focus();
        }

        // ---------------- geometry: the same size as a card, where the first card sits ----------------

        private bool MeasureTiles()
        {
            try
            {
                var win = Application.Current.MainWindow;
                var list = UiTools.FindByName(win, "PART_ListGameItems") as ItemsControl;
                var canvas = UiTools.FindByName(win, "KX_HomeTilesCanvas");
                if (list == null || canvas == null) return false;
                double shift = listShift != null ? listShift.X : 0;
                FrameworkElement c0 = null, c1 = null;
                int i0 = -1;
                for (int i = 0; i < Math.Min(list.Items.Count, 30); i++)
                {
                    var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (c == null || !c.IsVisible || c.ActualWidth <= 0) continue;
                    var lbi = c as ListBoxItem;
                    if (lbi != null && lbi.IsSelected) continue;   // the selected card is scaled up
                    if (c0 == null) { c0 = c; i0 = i; }
                    else { c1 = c; break; }
                }
                if (c0 == null) return false;
                var card = VisualTreeHelper.GetChildrenCount(c0) > 0 ? VisualTreeHelper.GetChild(c0, 0) as FrameworkElement : c0;
                if (card == null) card = c0;
                var p = card.TranslatePoint(new Point(0, 0), canvas);
                double w = card.ActualWidth, h = card.ActualHeight;
                if (w <= 0 || h <= 0) return false;
                if (c1 != null)
                {
                    double step = c1.TranslatePoint(new Point(0, 0), canvas).X - c0.TranslatePoint(new Point(0, 0), canvas).X;
                    int between = list.ItemContainerGenerator.IndexFromContainer(c1) - i0;
                    if (between > 0) step /= between;
                    if (step > w) tileGap = Math.Max(6, step - w);
                }
                tileW = Math.Round(w);
                tileH = Math.Round(h);
                tileTop = Math.Round(p.Y);
                // where the first card is when the row is at its start (the row may be scrolled and shifted)
                // where the row's first card sits when the row is at its start
                var sv = FindScrollViewer(list);
                double scrolled = sv != null ? sv.HorizontalOffset : 0;
                double listX = list.TranslatePoint(new Point(0, 0), canvas).X - shift;   // the row's left edge (after its margin)
                if (i0 == 0 && scrolled < 0.5) firstInset = Math.Max(0, p.X - shift - listX);
                double inset = firstInset >= 0 ? firstInset : tileGap / 2;
                // Always show: the tiles sit where the first card would be without the margin; Auto: where the first card is
                tileLeft = Math.Round(listX - cardsLeft + inset);
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: home tiles size: " + ex.Message); return false; }
        }

        /// <summary>
        /// Always show: how much margin the row needs so the tiles fit before it. The cards shrink a little with the narrower row,
        /// so solve for it: left = N·step, with step proportional to the row's width (W − left).
        /// </summary>
        private double AlwaysMargin()
        {
            var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as FrameworkElement;
            if (list == null || list.ActualWidth <= 0 || HomeTiles.Count == 0) return 0;
            double step = tileW + tileGap;
            double k = step / list.ActualWidth;
            double full = list.ActualWidth + cardsLeft;
            int n = HomeTiles.Count;
            return Math.Round(n * k * full / (1 + n * k));
        }

        public Thickness CardsLeftMargin { get { return new Thickness(cardsLeft, 0, 0, 0); } }

        private void SetCardsLeft(double x)
        {
            if (Math.Abs(x - cardsLeft) < 1) return;
            cardsLeft = x;
            Notify("CardsMargin");
            ApplyCardLayoutSoon();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                var sv = c as ScrollViewer;
                if (sv != null) return sv;
                var hit = FindScrollViewer(c);
                if (hit != null) return hit;
            }
            return null;
        }

        private double listShiftToX, listShiftToY;   // where our slide is going (so asking again doesn't start it over)

        /// <summary>
        /// Pushes the cover row right (a translate of its own, beside Master Edit's), animated. The same slide moves the list
        /// down while its tiles are in (List, Auto show); everywhere else it goes back up.
        /// </summary>
        private void ShiftList(double x)
        {
            var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems");
            if (list == null) return;
            double y = homeTilesShown && ListAutoTiles ? listTilesDrop : 0;
            bool attached = listShift != null && (ReferenceEquals(list.RenderTransform, listShift)
                            || (list.RenderTransform is TransformGroup && ((TransformGroup)list.RenderTransform).Children.Contains(listShift)));
            if (!attached) listShift = null;
            if (listShift == null)
            {
                if (Math.Abs(x) < 0.5 && Math.Abs(y) < 0.5) return;
                listShiftToX = listShiftToY = 0;
                listShift = new TranslateTransform();
                var current = list.RenderTransform;
                if (current == null || ReferenceEquals(current, Transform.Identity) || current.IsFrozen && current.Value.IsIdentity) list.RenderTransform = listShift;
                else
                {
                    var group = current as TransformGroup;
                    if (group == null || group.IsFrozen) { group = new TransformGroup(); group.Children.Add(current); list.RenderTransform = group; }
                    group.Children.Add(listShift);
                }
            }
            if (Math.Abs(listShiftToY - y) >= 0.5)
            {
                listShiftToY = y;
                listShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TileSlideTime) { EasingFunction = new CubicEase { EasingMode = y > 0 ? EasingMode.EaseOut : EasingMode.EaseIn } });
            }
            if (Math.Abs(listShiftToX - x) < 0.5 && Math.Abs(listShift.X - x) < 0.5) return;
            listShiftToX = x;
            var anim = new DoubleAnimation(x, TileSlideTime) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            listShift.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        /// <summary>The tiles slide in from the left edge (and fade), in step with the games moving over; out the same way.</summary>
        private void SlideTiles(bool show)
        {
            var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
            bool wasVisible = tilesVisible;
            if (show) tilesVisible = true;
            if (strip == null) { tilesVisible = show; return; }
            if (tileSlide == null || !ReferenceEquals(strip.RenderTransform, tileSlide))
            {
                tileSlide = new TranslateTransform();
                strip.RenderTransform = tileSlide;
            }
            double away = -(HomeTiles.Count * (tileW + tileGap) + Math.Max(0, tileLeft));
            var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
            if (show)
            {
                if (!wasVisible)
                {
                    // Always show pins them (no slide, a quick fade); Auto show slides them in
                    tileSlide.BeginAnimation(TranslateTransform.XProperty, null);
                    tileSlide.X = AlwaysTiles ? 0 : away;
                    tileSlide.BeginAnimation(TranslateTransform.YProperty, null);   // (the list's tiles come in from above)
                    tileSlide.Y = 0;
                    strip.BeginAnimation(UIElement.OpacityProperty, null);
                    strip.Opacity = 0;
                }
                tileSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TileSlideTime) { EasingFunction = ease });
                strip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TileSlideTime));
                return;
            }
            if (!wasVisible) return;
            var outAnim = new DoubleAnimation(away, TileSlideTime) { EasingFunction = ease };
            outAnim.Completed += (s, e) =>
            {
                if (homeTilesShown) return;   // came back meanwhile
                tilesVisible = false;
                Notify("HomeTilesShown");
            };
            tileSlide.BeginAnimation(TranslateTransform.XProperty, outAnim);
            strip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TileSlideTime));
        }

        /// <summary>The layout changed (card size, filter, Master Edit): keep the tiles the size and place of a card.</summary>
        private void UpdateHomeTilesLayout()
        {
            if (HomeTiles == null) return;
            if (SideMode)
            {
                if (homeTilesShown) { if (MeasureSideTiles() && ListAutoTiles) ShiftList(0); }   // (List, Auto show: the games stay moved down by the tiles' height)
                else if (SideTileCount() > 0) ShowSideTiles(false);
                return;
            }
            if (!homeTilesShown) return;
            double l = tileLeft, t = tileTop, w = tileW, h = tileH;
            if (!MeasureTiles()) return;
            if (AlwaysTiles) SetCardsLeft(AlwaysMargin());
            else ShiftList(HomeTiles.Count * (tileW + tileGap));
            if (Math.Abs(l - tileLeft) >= 1 || Math.Abs(t - tileTop) >= 1 || Math.Abs(w - tileW) >= 1 || Math.Abs(h - tileH) >= 1) NotifyHomeTiles();
        }

        // ---------------- the tiles' own content ----------------

        private void UpdateStoreTile()
        {
            var store = HomeTiles.FirstOrDefault(t => t.Key == "store");
            var ach = HomeTiles.FirstOrDefault(t => t.Key == "ach");
            if (ach != null && achData != null && achData.Count > 0)
                ach.Sub = string.Format(Loc("LOC_KX_TileAchCount", "{0} unlocked"), achData.Sum(a => a.Unlocked).ToString("N0"));
            if (store == null) return;
            var sc = Shortcuts != null ? Shortcuts.FirstOrDefault(s => s.Key == "store") : null;
            store.ShowDot = sc != null && sc.ShowDot;
            if (store.ShowDot) { store.Sub = Loc("LOC_KX_TileStoreDeal", "A wishlist game got cheaper"); return; }
            if (freeThisWeek > 0) { store.Sub = string.Format(Loc("LOC_KX_TileStoreFree", "{0} free games this week"), freeThisWeek); return; }
            if (freeCountAsked) return;
            freeCountAsked = true;   // once per session, when the tile is first shown
            KippixShared.SafeWork.Queue(_ =>
            {
                int n = 0;
                try { n = EpicFree().Count(i => i.DiscountText == "-100%"); } catch (Exception) { }
                OnUi(() => { freeThisWeek = n; if (n > 0 && !store.ShowDot) store.Sub = string.Format(Loc("LOC_KX_TileStoreFree", "{0} free games this week"), n); });
            });
        }

        /// <summary>Random game: picks one from the games on screen (your current filter) and highlights it.</summary>
        private void PickRandomGame()
        {
            var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
            if (list == null || list.Items.Count == 0) return;
            PlaySound("select");
            var pick = list.Items[Rand(list.Items.Count)];
            if (!AlwaysTiles && !SideMode && TilesMode == 1) SetTilesShown(false, false);
            list.SelectedItem = pick;
            list.ScrollIntoView(pick);
            ui.BeginInvoke(new Action(() =>
            {
                var c = list.ItemContainerGenerator.ContainerFromItem(pick) as UIElement;
                if (c != null) Keyboard.Focus(c);
            }), DispatcherPriority.Loaded);
        }
    }
}
