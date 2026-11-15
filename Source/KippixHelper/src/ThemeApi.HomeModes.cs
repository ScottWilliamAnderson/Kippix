using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Home in the grid and the list down one side: the tiles before the games (Store, Achievements, pinned games) stand in a
    /// column beside the grid / above the list. Also the art for wide tiles (Steam's wide picture, or the background with the logo)
    /// and the list's name / play time / last played beside each game.
    /// </summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // The tiles before the games, in the grid and the list
        // =====================================================================

        /// <summary>How many tiles stand beside the grid / above the list (0 when they're off).</summary>
        private int SideTileCount()
        {
            if (HomeTiles == null || HomeTiles.Count == 0) return 0;
            return settings.HomeTilesMode == 0 && !HomeTiles.Any(t => t.IsPinned) ? 0 : HomeTiles.Count;
        }

        /// <summary>Grid / list: the tiles stack downwards.</summary>
        public Orientation HomeTilesVertical { get { return SideMode ? Orientation.Vertical : Orientation.Horizontal; } }

        /// <summary>
        /// The list with the tiles on Auto show (or Hidden, with pinned games): the tiles wait out of sight above the list. Up from
        /// the first game brings them in at the top (the games move down under them); Down from the last tile, or moving away,
        /// sends them off again. Always show (and the grid) keeps them there all the time.
        /// </summary>
        private bool ListAutoTiles { get { return LayoutNow == 2 && TilesMode == 1 && SideTileCount() > 0; } }
        private double listTilesDrop;   // List, Auto show: how far the games move down to make room for the tiles

        /// <summary>Grid / list: the tiles are always there (nothing slides); placed from the panel's own tile size.</summary>
        private void ShowSideTiles(bool focus)
        {
            if (ListAutoTiles)
            {
                // Auto show: they stay in only while you're on them (a layout change otherwise puts them away)
                var on = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
                ListTilesShow(focus || (on != null && on.IsVisible && on.IsKeyboardFocusWithin), focus);
                return;
            }
            int n = SideTileCount();
            homeTilesShown = n > 0;
            tilesVisible = n > 0;
            SetCardsLeft(0);
            ShiftList(0);
            var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
            if (strip != null)
            {
                strip.BeginAnimation(UIElement.OpacityProperty, null);
                strip.Opacity = 1;
                if (tileSlide != null)
                {
                    tileSlide.BeginAnimation(TranslateTransform.XProperty, null); tileSlide.X = 0;
                    tileSlide.BeginAnimation(TranslateTransform.YProperty, null); tileSlide.Y = 0;
                }
            }
            MeasureSideTiles();
            NotifyHomeTiles();
            if (n == 0) return;
            UpdateStoreTile();
            HookHomeTiles();
            if (focus) ui.BeginInvoke(new Action(() => FocusHomeTile(HomeTiles.LastOrDefault())), DispatcherPriority.Loaded);
        }

        /// <summary>List, Auto show: the tiles come in above the first game (the games slide down for them), or go back out.</summary>
        private void ListTilesShow(bool show, bool focus)
        {
            if (SideTileCount() == 0) show = false;
            SetCardsLeft(0);
            if (show && !MeasureSideTiles())
            {
                // (the list isn't laid out yet: nothing to line the tiles up with)
                if (!homeTilesShown) return;
                show = false;
            }
            homeTilesShown = show;
            ShiftList(0);   // (sideways: never in the list; downwards: by listTilesDrop while they're in)
            SlideSideTiles(show);
            NotifyHomeTiles();
            if (!show)
            {
                // (put away while the highlight is still on one: it goes back to the games, not into nothing)
                var held = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
                if (held != null && held.IsKeyboardFocusWithin) FocusSelectedCard();
                return;
            }
            UpdateStoreTile();
            HookHomeTiles();
            if (!focus) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (!homeTilesShown) return;
                FocusHomeTile(HomeTiles.LastOrDefault());   // the one nearest the games
                var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles");
                // couldn't take the highlight: don't leave them in with the games pushed down
                if (ListAutoTiles && (strip == null || !strip.IsKeyboardFocusWithin)) ListTilesShow(false, false);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>List, Auto show: the tiles fade in moving down with the games, and out moving back up (Reduce motion: at once).</summary>
        private void SlideSideTiles(bool show)
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
            tileSlide.BeginAnimation(TranslateTransform.XProperty, null);
            tileSlide.X = 0;
            double away = -Math.Max(0, listTilesDrop);
            var ease = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn };
            if (show)
            {
                if (!wasVisible)
                {
                    tileSlide.BeginAnimation(TranslateTransform.YProperty, null);
                    tileSlide.Y = away;
                    strip.BeginAnimation(UIElement.OpacityProperty, null);
                    strip.Opacity = 0;
                }
                tileSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TileSlideTime) { EasingFunction = ease });
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
            tileSlide.BeginAnimation(TranslateTransform.YProperty, outAnim);
            strip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TileSlideTime));
        }

        private bool MeasureSideTiles()
        {
            try
            {
                var win = Application.Current.MainWindow;
                var list = UiTools.FindByName(win, "PART_ListGameItems") as ListBox;
                var canvas = UiTools.FindByName(win, "KX_HomeTilesCanvas");
                if (list == null || canvas == null || list.ActualWidth <= 0 || sideItemW <= 0 || sideItemH <= 0) return false;
                int n = SideTileCount();
                if (n == 0) return false;
                // the card's own spacing (Playnite's item spacing margin) so a tile lines up with a card
                Thickness m = new Thickness(0);
                for (int i = 0; i < Math.Min(list.Items.Count, 6); i++)
                {
                    var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (c == null || VisualTreeHelper.GetChildrenCount(c) == 0) continue;
                    var child = VisualTreeHelper.GetChild(c, 0) as FrameworkElement;
                    if (child != null) { m = child.Margin; break; }
                }
                var p = list.TranslatePoint(new Point(0, 0), canvas);
                if (listShift != null) p = new Point(p.X - listShift.X, p.Y - listShift.Y);   // (where the list is without our slide)
                double cols = LayoutNow == 2 ? 1 : Math.Max(1, sideCols);
                double centre = Math.Max(0, (list.ActualWidth - cols * sideItemW) / 2);
                double slotH = sideItemH;
                if (LayoutNow == 1)
                {
                    int rows = Math.Max(1, Math.Min(4, settings.GridRows));
                    if (n > rows) slotH = rows * sideItemH / n;   // more tiles than rows: they share the column's height
                    tileW = Math.Round(Math.Max(40, sideItemW - m.Left - m.Right));
                    tileH = Math.Round(Math.Max(40, slotH - m.Top - m.Bottom));
                    tileLeft = Math.Round(p.X + centre - sideItemW * 1.1 + m.Left);
                    tileTop = Math.Round(p.Y + 0.25 * sideItemH + m.Top);
                    tileGap = Math.Max(4, slotH - tileH);
                }
                else
                {
                    double pitch = sideItemH * 1.1;
                    tileW = Math.Round(Math.Max(40, sideItemW - m.Left - m.Right));
                    tileH = Math.Round(Math.Max(30, sideItemH - m.Top - m.Bottom));
                    tileLeft = Math.Round(p.X + centre + m.Left);
                    // Always show: above the list (it was made shorter for them). Auto show: where the first games are, which
                    // move down by the tiles' height while they're in
                    listTilesDrop = Math.Round(n * pitch);
                    tileTop = Math.Round(p.Y + 0.25 * sideItemH - (ListAutoTiles ? 0 : n * pitch) + m.Top);
                    tileGap = Math.Max(4, pitch - tileH);
                }
                NotifyHomeTiles();
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: side tiles: " + ex.Message); return false; }
        }

        private void FocusHomeTile(HomeTile tile)
        {
            if (tile == null) return;
            var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles") as ItemsControl;
            var c = strip != null ? strip.ItemContainerGenerator.ContainerFromItem(tile) as DependencyObject : null;
            var btn = c != null ? FindFocusable(c) : null;
            if (btn != null) Keyboard.Focus(btn);
        }

        /// <summary>Grid: Left from the first column goes to the tile beside that row. List: Up from the first game goes to the tiles above.</summary>
        private bool SideTilesKey(ListBoxItem item, ItemsControl owner, Key key)
        {
            if (!SideMode || SideTileCount() == 0) return false;
            int index = owner.ItemContainerGenerator.IndexFromContainer(item);
            if (index < 0) return false;
            if (LayoutNow == 1)
            {
                if (key != Key.Left || index % Math.Max(1, sideCols) != 0) return false;
                // the tile level with the game (nearest middles)
                var strip = UiTools.FindByName(Application.Current.MainWindow, "KX_HomeTiles") as ItemsControl;
                if (strip == null) return false;
                var win = Application.Current.MainWindow;
                double y = item.TranslatePoint(new Point(0, item.ActualHeight / 2), win).Y;
                HomeTile best = null;
                double bestD = double.MaxValue;
                foreach (var t in HomeTiles)
                {
                    var c = strip.ItemContainerGenerator.ContainerFromItem(t) as FrameworkElement;
                    if (c == null) continue;
                    double ty = c.TranslatePoint(new Point(0, c.ActualHeight / 2), win).Y;
                    if (Math.Abs(ty - y) < bestD) { bestD = Math.Abs(ty - y); best = t; }
                }
                if (best == null) return false;
                PlaySound("move");
                FocusHomeTile(best);
                return true;
            }
            if (key != Key.Up || index != 0) return false;
            PlaySound("move");
            if (ListAutoTiles) { ListTilesShow(true, true); return true; }   // Auto show: they come in, the highlight onto them
            FocusHomeTile(HomeTiles.LastOrDefault());
            return true;
        }

        /// <summary>Keys on the tiles in the grid / list: back to the games the way you came.</summary>
        private bool SideStripKey(HomeTile tile, Key key)
        {
            if (!SideMode) return false;
            bool last = ReferenceEquals(tile, HomeTiles.LastOrDefault());
            if (LayoutNow == 1)
            {
                if (key == Key.Right) { PlaySound("move"); FocusSelectedCard(); return true; }
                if (key == Key.Left || (key == Key.Down && last)) return true;   // nothing that way
                return false;
            }
            if (key == Key.Down && last) { PlaySound("move"); FocusSelectedCard(); return true; }
            if (key == Key.Left || key == Key.Right) return true;
            return false;
        }

        // =====================================================================
        // Wide art and the list's details, on each game tile
        // =====================================================================

        /// <summary>Wide tiles, or the list: the tiles show their own art (and the list its words).</summary>
        private bool TileArtOn { get { return LayoutNow != 6 && (LayoutNow == 2 || settings.CardShape == 2 || CardShapes.IsTriangle(settings.CardShape)); } }   // (never in the mosaic: the covers themselves)
        private readonly Dictionary<string, ImageSource> wideArt = new Dictionary<string, ImageSource>();
        private readonly HashSet<string> wideArtLoading = new HashSet<string>();
        private DispatcherTimer tileArtTimer;
        private ListBox tileArtList;
        private bool tileArtWasOn;   // the wide art has been on this session: keep checking reused cards once it's off

        private void RefreshTileArtSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (tileArtTimer == null)
                {
                    tileArtTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    tileArtTimer.Tick += (s, e) => { tileArtTimer.Stop(); UpdateTileArt(); };
                }
                if (!tileArtTimer.IsEnabled) tileArtTimer.Start();
            }));
        }

        private void UpdateTileArt()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var list = win != null ? UiTools.FindByName(win, "PART_ListGameItems") as ListBox : null;
                if (list == null) return;
                if (!ReferenceEquals(tileArtList, list))
                {
                    tileArtList = list;
                    // always (not only while the art is on): Playnite reuses cards as you scroll, and a reused card can still have
                    // the wide art showing from list mode after you've gone back to covers
                    list.ItemContainerGenerator.StatusChanged += (s, e) => { if (TileArtOn || tileArtWasOn || ShapesOn || shapesWereOn || SpacingOn || measuredSpacing < 0) RefreshTileArtSoon(); };
                    list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => { if (TileArtOn || tileArtWasOn || ShapesOn || shapesWereOn || SpacingOn) RefreshTileArtSoon(); }));
                    list.SizeChanged += (s, e) => { if (ShapesOn || shapesWereOn) RefreshTileArtSoon(); };
                }
                bool on = TileArtOn, listMode = LayoutNow == 2;
                if (on) tileArtWasOn = true;
                // (only the cards Playnite has made: they're its panel's children, a few out of however many games)
                var panel = VisualTreeHelper.GetChildrenCount(list) > 0 ? UiTools.FindChild<VirtualizingPanel>(list) : null;
                if (panel != null)
                {
                    foreach (UIElement child in panel.Children)
                    {
                        var c = child as FrameworkElement;
                        if (c != null) DressCard(c, list.ItemContainerGenerator.ItemFromContainer(c), on, listMode);
                    }
                }
                else
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                        if (c != null) DressCard(c, list.Items[i], on, listMode);
                    }
                if (listMode) { var l = list; ui.BeginInvoke(new Action(() => CheckListInfoFit(l)), DispatcherPriority.Loaded); }
                try { ApplyCardShapes(list); } catch (Exception ex) { logger.Warn("Kippix: card shapes: " + ex.Message); }
                try { ApplyCardSpacingTo(list); } catch (Exception ex) { logger.Warn("Kippix: card spacing: " + ex.Message); }
                if (carActive) CarDressSoon();   // the carousel's cards (their wide art, shape and gap)
            }
            catch (Exception ex) { logger.Warn("Kippix: tile art: " + ex.Message); }
        }

        /// <summary>One of Playnite's cards: the wide art shown or not, and (when on) its picture and the list's play info.</summary>
        private void DressCard(FrameworkElement c, object entry, bool on, bool listMode)
        {
            var art = UiTools.FindByName(c, "KX_WideArt");
            if (art == null) return;
            var v = on ? Visibility.Visible : Visibility.Collapsed;
            if (art.Visibility != v) art.Visibility = v;
            if (!on) return;
            Guid id = EntryId(entry);
            Game g = null;
            if (id != Guid.Empty) try { g = api.Database.Games.Get(id); } catch (Exception) { }
            FillTileArt(c, g, listMode);
        }

        // ---------------- a card dressed the moment Playnite makes it ----------------
        // (Opening a folder, a new filter or scrolling gives the list new or reused cards. They're filled in as they're put on
        // screen, before they're first drawn, instead of on the next tile art pass, so the name and play info come with them.)

        private static readonly DependencyProperty CardDressHookedProperty = DependencyProperty.RegisterAttached("BBCardDressHooked", typeof(bool), typeof(ThemeApi), new PropertyMetadata(false));
        private bool cardDressRegistered;

        /// <summary>Once, at start-up: every card of Playnite's list is filled in when it goes on screen.</summary>
        private void HookCardDressing()
        {
            if (cardDressRegistered) return;
            cardDressRegistered = true;
            EventManager.RegisterClassHandler(typeof(ListBoxItem), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnCardLoaded));
        }

        private void OnCardLoaded(object sender, RoutedEventArgs e)
        {
            var item = sender as ListBoxItem;
            if (item == null || (!TileArtOn && !tileArtWasOn)) return;
            if (!DressNow(item)) return;
            if ((bool)item.GetValue(CardDressHookedProperty)) return;
            item.SetValue(CardDressHookedProperty, true);
            // reused for another game while it's still on screen: filled in again straight away
            item.DataContextChanged += (s, a) => { if (TileArtOn || tileArtWasOn) DressNow(item); };
        }

        /// <summary>True when it's one of the cards in Playnite's list (and it was dressed).</summary>
        private bool DressNow(ListBoxItem item)
        {
            try
            {
                var owner = ItemsControl.ItemsControlFromItemContainer(item);
                if (owner == null || owner.Name != "PART_ListGameItems") return false;
                bool on = TileArtOn;
                if (on) tileArtWasOn = true;
                DressCard(item, item.DataContext, on, LayoutNow == 2);
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: tile art (card): " + ex.Message); return false; }
        }

        private void FillTileArt(FrameworkElement c, Game g, bool listMode)
        {
            var holder = UiTools.FindByName(c, "KX_WideArt") as Grid;
            var artCol = holder != null && holder.ColumnDefinitions.Count > 1 ? holder.ColumnDefinitions[0] : null;
            var infoCol = holder != null && holder.ColumnDefinitions.Count > 1 ? holder.ColumnDefinitions[1] : null;
            if (artCol != null && infoCol != null && holder != null)
            {
                if (listMode)
                {
                    // the play info takes its share of the game (Game cards and layout › Play info), the art the rest
                    var art = settings.ListShowInfo ? new GridLength(100 - settings.ListInfoShare, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
                    var info = settings.ListShowInfo ? new GridLength(settings.ListInfoShare, GridUnitType.Star) : new GridLength(0);
                    if (artCol.Width != art) artCol.Width = art;
                    if (infoCol.Width != info) infoCol.Width = info;
                }
                else
                {
                    if (artCol.Width != new GridLength(1, GridUnitType.Star)) artCol.Width = new GridLength(1, GridUnitType.Star);
                    if (infoCol.Width != new GridLength(0)) infoCol.Width = new GridLength(0);
                }
            }
            if (g == null) return;
            if (listMode)
            {
                var l1 = UiTools.FindByName(c, "KX_ListLine1") as TextBlock;
                var l2 = UiTools.FindByName(c, "KX_ListLine2") as TextBlock;
                var lines = ListInfoLines(g);   // what Game cards and layout › Play info shows, one thing per line
                string line1 = lines.Count > 0 ? lines[0] : "";
                string line2 = lines.Count > 1 ? string.Join("\n", lines.Skip(1).ToArray()) : "";
                var nameLine = UiTools.FindByName(c, "KX_ListName");
                var nv = settings.ListShowName ? Visibility.Visible : Visibility.Collapsed;
                if (nameLine != null && nameLine.Visibility != nv) nameLine.Visibility = nv;
                if (l1 != null && l1.Text != line1) l1.Text = line1;
                if (l2 != null && l2.Text != line2) l2.Text = line2;
                // the words grow with the game (a taller game, bigger text); the box round them shrinks it back if it won't fit
                var infoPanel = UiTools.FindByName(c, "KX_ListInfo");
                if (infoPanel != null)
                {
                    double gh = holder != null && holder.ActualHeight > 0 ? holder.ActualHeight : sideItemH;
                    double sc = Math.Max(0.7, Math.Min(2.2, gh / 150.0));
                    var cur = infoPanel.LayoutTransform as ScaleTransform;
                    if (cur == null || Math.Abs(cur.ScaleX - sc) > 0.01) infoPanel.LayoutTransform = new ScaleTransform(sc, sc);
                }

            }
            string key = g.Id.ToString();
            var img = UiTools.FindByName(c, "KX_WideImage") as Image;
            var logo = UiTools.FindByName(c, "KX_WideLogo") as Image;
            var shade = UiTools.FindByName(c, "KX_WideShade");
            var name = UiTools.FindByName(c, "KX_WideName");
            var back = UiTools.FindByName(c, "KX_WideBack");
            ImageSource pic, lg;
            bool known;
            lock (wideArt) { known = wideArt.TryGetValue(key, out pic); wideArt.TryGetValue(key + "|logo", out lg); }
            if (!known)
            {
                // (until its own wide picture is in: the art's backing steps aside so Playnite's own cover shows there, and
                // nothing is left from the game this card showed before)
                if (back != null && back.Visibility != Visibility.Collapsed) back.Visibility = Visibility.Collapsed;
                if (img != null && img.Source != null) img.Source = null;
                if (logo != null && logo.Source != null) logo.Source = null;
                if (shade != null && shade.Visibility != Visibility.Collapsed) shade.Visibility = Visibility.Collapsed;
                if (name != null && name.Visibility != Visibility.Collapsed) name.Visibility = Visibility.Collapsed;
                LoadWideArt(g);
                return;
            }
            if (back != null && back.Visibility != Visibility.Visible) back.Visibility = Visibility.Visible;
            if (img != null && !ReferenceEquals(img.Source, pic)) img.Source = pic;
            if (logo != null)
            {
                if (!ReferenceEquals(logo.Source, lg)) logo.Source = lg;
                logo.MaxHeight = Math.Max(24, (c.ActualHeight > 0 ? c.ActualHeight : 200) * 0.32);
                logo.MaxWidth = Math.Max(60, (c.ActualWidth > 0 ? (listMode ? c.ActualHeight * 16 / 9 : c.ActualWidth) : 300) * 0.7);
            }
            if (shade != null) shade.Visibility = lg != null ? Visibility.Visible : Visibility.Collapsed;
            // the name shows only when there's no picture, or a background without a logo
            if (name != null) name.Visibility = pic == null || (lg == null && !IsSteamHeaderArt(key)) ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly HashSet<string> steamHeaderArt = new HashSet<string>();
        private bool IsSteamHeaderArt(string key) { lock (wideArt) return steamHeaderArt.Contains(key); }

        /// <summary>
        /// The wide picture for a game: Steam's own wide picture (from Steam's library cache) when it's a Steam game,
        /// otherwise the game's background with its logo over it (or its name when there's no logo).
        /// </summary>
        private void LoadWideArt(Game g)
        {
            string key = g.Id.ToString();
            lock (wideArt) { if (wideArtLoading.Contains(key)) return; wideArtLoading.Add(key); }
            string header = g.PluginId == SteamPluginId ? SteamWideArt(g.GameId) : null;
            string bg = null, logoFile = null;
            try
            {
                if (header == null && !string.IsNullOrEmpty(g.BackgroundImage) && !g.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    bg = api.Database.GetFullFilePath(g.BackgroundImage);
                if (header == null && bg == null && !string.IsNullOrEmpty(g.CoverImage) && !g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    bg = api.Database.GetFullFilePath(g.CoverImage);
                if (header == null)
                {
                    string lf = Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", key, "Logo.png");
                    if (File.Exists(lf)) logoFile = lf;
                }
            }
            catch (Exception) { }
            KippixShared.SafeWork.Queue(_ =>
            {
                ImageSource pic = null, lg = null;
                try
                {
                    pic = Images.Load(header ?? bg, 640);
                    if (logoFile != null) lg = Images.Load(logoFile, 400);
                }
                catch (Exception) { }
                OnUi(() =>
                {
                    lock (wideArt)
                    {
                        if (wideArt.Count > 600) { wideArt.Clear(); steamHeaderArt.Clear(); }
                        wideArt[key] = pic;
                        wideArt[key + "|logo"] = lg;
                        if (header != null && pic != null) steamHeaderArt.Add(key);
                        wideArtLoading.Remove(key);
                    }
                    RefreshTileArtSoon();
                });
            });
        }

        /// <summary>The wide picture's file: Steam's wide picture, else the background, else the cover.</summary>
        private string WideArtPath(Game g)
        {
            if (g == null) return null;
            string header = g.PluginId == SteamPluginId ? SteamWideArt(g.GameId) : null;
            if (header != null) return header;
            try
            {
                if (!string.IsNullOrEmpty(g.BackgroundImage) && !g.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return api.Database.GetFullFilePath(g.BackgroundImage);
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Steam's wide library picture for an app, if Steam has it on this PC (older and newer cache layouts).</summary>
        private static string SteamWideArt(string appId)
        {
            try
            {
                if (string.IsNullOrEmpty(appId)) return null;
                string steam = SteamPaths.SteamPath();
                if (steam == null) return null;
                string cache = Path.Combine(steam, @"appcache\librarycache");
                string f = Path.Combine(cache, appId + "_header.jpg");
                if (File.Exists(f)) return f;
                string dir = Path.Combine(cache, appId);
                if (!Directory.Exists(dir)) return null;
                f = Path.Combine(dir, "header.jpg");
                if (File.Exists(f)) return f;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    f = Path.Combine(sub, "header.jpg");
                    if (File.Exists(f)) return f;
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
