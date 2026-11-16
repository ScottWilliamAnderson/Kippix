using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Widgets you can open: the games on the Suggestions, Free games, Wishlist deals, Coming soon and Achievements widgets.
    /// Up from the game row goes to the nearest one above; the D-pad moves between them; Up past the top reaches the top bar;
    /// Down past the bottom (or B) goes back to the games. A (or a click) opens it.
    /// </summary>
    public partial class ThemeApi
    {
        // ---------------- what a picture does ----------------

        /// <summary>A game from your library: highlighted in the game row (the filter is left alone; if it isn't in the row, Playnite selects it).</summary>
        private void ShowLibraryGame(Guid id)
        {
            try
            {
                PlaySound("select");
                if (IsBoardMode)
                {
                    // the Board: select it and put the highlight on a card showing it (else where it was)
                    try { api.MainView.SelectGame(id); } catch (Exception) { }
                    var onBoard = BoardTiles.FirstOrDefault(t => t.IsCard && t.GameId == id);
                    if (onBoard != null) FocusBoardTile(onBoard); else FocusBoard();
                    return;
                }
                var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
                object entry = list != null ? list.Items.Cast<object>().FirstOrDefault(x => EntryId(x) == id) : null;
                if (entry == null)
                {
                    try { api.MainView.SelectGame(id); } catch (Exception) { }
                    if (list != null) entry = list.Items.Cast<object>().FirstOrDefault(x => EntryId(x) == id);
                }
                if (list == null || entry == null) { FocusSelectedCard(); return; }
                list.SelectedItem = entry;
                list.ScrollIntoView(entry);
                ui.BeginInvoke(new Action(() =>
                {
                    var c = list.ItemContainerGenerator.ContainerFromItem(entry) as UIElement;
                    if (c != null) Keyboard.Focus(c);
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex) { logger.Warn("Kippix: widget game: " + ex.Message); }
        }

        /// <summary>A game in the Store: its page (Back goes to the Store's front page).</summary>
        private void OpenStoreFromWidget(StoreItem item)
        {
            if (item == null) return;
            PlaySound("select");
            OpenStore();
            if (openPanel != "store") return;   // not for this profile (or the PIN is being asked for)
            storeGameFrom = "home";
            OpenStoreGame(item);
        }

        private static StoreItem WishToStoreItem(WishItem w)
        {
            var item = new StoreItem
            {
                Store = w.Store ?? "", Id = (!string.IsNullOrEmpty(w.Store) && !string.IsNullOrEmpty(w.Id) ? w.Id : null) ?? w.ItadId ?? w.Id ?? w.Name, ItadId = w.ItadId, SteamAppId = w.SteamAppId,
                Name = w.Name, Image = w.Image, Link = w.Link, IsWished = true
            };
            if (item.SteamAppId != null && item.AppLink == null) item.AppLink = "steam://store/" + item.SteamAppId;
            return item;
        }

        // ---------------- getting to them with the D-pad ----------------

        private List<FrameworkElement> WidgetButtons()
        {
            var list = new List<FrameworkElement>();
            var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets");
            if (layer == null || !layer.IsVisible) return list;
            Collect(layer, list);
            return list;
        }

        private static void Collect(DependencyObject o, List<FrameworkElement> list)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var c = VisualTreeHelper.GetChild(o, i);
                var b = c as System.Windows.Controls.Primitives.ButtonBase;   // the theme's ButtonEx
                if (b != null) { if (b.Focusable && b.IsVisible) list.Add(b); continue; }
                Collect(c, list);
            }
        }

        private static Rect BoundsIn(FrameworkElement e, Visual root)
        {
            try { return e.TransformToVisual(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); }
            catch (Exception) { return Rect.Empty; }
        }

        /// <summary>The nearest one in that direction from <paramref name="from"/> (mostly along the direction, a little sideways).</summary>
        private static FrameworkElement Nearest(Rect from, List<Tuple<FrameworkElement, Rect>> all, Key dir)
        {
            Point a = new Point(from.Left + from.Width / 2, from.Top + from.Height / 2);
            FrameworkElement best = null;
            double bestScore = double.MaxValue;
            foreach (var t in all)
            {
                var r = t.Item2;
                if (r.IsEmpty) continue;
                Point b = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                double along, side;
                switch (dir)
                {
                    case Key.Up: along = a.Y - b.Y; side = Math.Abs(a.X - b.X); break;
                    case Key.Down: along = b.Y - a.Y; side = Math.Abs(a.X - b.X); break;
                    case Key.Left: along = a.X - b.X; side = Math.Abs(a.Y - b.Y); break;
                    default: along = b.X - a.X; side = Math.Abs(a.Y - b.Y); break;
                }
                if (along < 8) continue;
                double score = along + side * 2;
                if (score < bestScore) { bestScore = score; best = t.Item1; }
            }
            return best;
        }

        /// <summary>Home screen keys for the widgets. True when it moved the highlight (or kept it).</summary>
        private bool WidgetNavKey(KeyEventArgs e)
        {
            if (openPanel != null || detailsOpen || !settings.WidgetsOn || LibraryOpen) return false;
            var win = Application.Current.MainWindow;
            var layer = UiTools.FindByName(win, "KX_Widgets");
            var games = UiTools.FindByName(win, "PART_ListGameItems");
            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (layer == null || games == null || focused == null) return false;
            bool inWidgets = layer.IsKeyboardFocusWithin;
            bool back = e.Key == Key.Escape || e.Key == Key.Back;
            if (!back && e.GetType().Name == "GameControllerInputEventArgs")
            {
                var b = e.GetType().GetProperty("Button");
                var st = e.GetType().GetProperty("ButtonState");
                var v = b != null ? b.GetValue(e, null) : null;
                var sv = st != null ? st.GetValue(e, null) : null;
                back = v != null && v.ToString() == "B" && (sv == null || sv.ToString() == "Pressed");
            }
            if (inWidgets && back) { e.Handled = true; PlaySound("back"); FocusSelectedCard(); return true; }
            if (inWidgets && e.GetType().Name == "GameControllerInputEventArgs" && !back)
            {
                var b2 = e.GetType().GetProperty("Button");
                var v2 = b2 != null ? b2.GetValue(e, null) : null;
                if (v2 != null && v2.ToString() == "B") { e.Handled = true; return true; }   // B let go: already handled
            }
            if (e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right) return false;
            bool onBoard = BoardHasFocus();
            bool onCarousel = carActive && (games.IsKeyboardFocusWithin || CarProxyFocused());   // Row's carousel (Playnite's row is out of sight)
            if (!inWidgets && !((games.IsKeyboardFocusWithin || onBoard || onCarousel) && e.Key == Key.Up)) return false;
            if (!inWidgets && LayoutNow != 0 && !onBoard)
            {
                // the grid: Up moves between its rows; only from the top row does it go on to the widgets
                var above = focused.PredictFocus(FocusNavigationDirection.Up) as DependencyObject;
                if (above != null && games.IsAncestorOf(above)) return false;
            }
            var buttons = WidgetButtons();
            if (buttons.Count == 0) return false;
            var all = buttons.Where(x => !ReferenceEquals(x, focused)).Select(x => Tuple.Create(x, BoundsIn(x, win))).ToList();
            var from = !inWidgets && onCarousel ? CarouselHighlightBounds(win) : Rect.Empty;
            if (from.IsEmpty) from = BoundsIn(focused, win);
            var next = Nearest(from, all, e.Key);
            if (next != null)
            {
                e.Handled = true;
                PlaySound("move");
                Keyboard.Focus(next);
                return true;
            }
            if (!inWidgets) return false;                      // nothing above the games: Up works as before (the top bar)
            if (e.Key == Key.Down) { e.Handled = true; PlaySound("move"); FocusSelectedCard(); return true; }
            if (e.Key == Key.Up) return false;                 // past the top: the top bar
            e.Handled = true;                                   // nothing more that way: stay
            return true;
        }

        /// <summary>Don't swap a widget's pictures while one of them has the highlight (it would lose it): next time instead.</summary>
        private bool WidgetHasFocus(WidgetItem w)
        {
            try
            {
                var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets") as ItemsControl;
                if (layer == null || !layer.IsKeyboardFocusWithin) return false;
                var c = layer.ItemContainerGenerator.ContainerFromItem(w) as UIElement;
                return c != null && c.IsKeyboardFocusWithin;
            }
            catch (Exception) { return false; }
        }
    }
}
