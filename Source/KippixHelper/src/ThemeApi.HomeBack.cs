using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.62) B (or Esc / Backspace) on the home screen, with nothing open: the highlight goes to the very first game
    /// (the default) or the top bar's first button, or B does nothing there (Settings › General › Controller buttons ›
    /// HOME SCREEN › B on the home screen). A folder's B (back to the folder tiles) comes first, as before.
    /// </summary>
    public partial class ThemeApi
    {
        public static readonly string[] HomeBackKeys = { "LOC_KX_HomeBackFirst", "LOC_KX_HomeBackBar", "LOC_KX_HomeBackNothing" };
        private static readonly string[] HomeBackText_ = { "The first game", "The top bar", "Nothing" };

        private string HomeBackText
        {
            get { int m = Math.Max(0, Math.Min(2, settings.HomeBackTo)); return Loc(HomeBackKeys[m], HomeBackText_[m]); }
        }

        private void CycleHomeBack()
        {
            settings.HomeBackTo = (Math.Max(0, Math.Min(2, settings.HomeBackTo)) + 1) % 3;
            SaveSettings();
            PlaySound("move");
        }

        /// <summary>From the window's key handler (the home screen, nothing open). True: handled.</summary>
        private bool HomeBackKey(KeyEventArgs e)
        {
            if (e.IsRepeat || !IsBackKey(e) || IsPadRelease(e)) return false;
            if (confirmOpen || kbOpen || freeMove || IsMasterMoving || descReaderOpen || masterMoving != null || widgetMoving != null || movingItem != null
                || boardEditing || bgMoveOn || splashEditOn || lpOpen || guideBarOpen || FolderInside) return false;
            // the games are searched (Search › Show in my games): B clears the search first, whatever B is set to do here
            try { if (HomeSearchClear()) return true; }
            catch (Exception ex) { logger.Warn("Kippix: B clears the search: " + ex.Message); }
            if (settings.HomeBackTo == 2) return false;
            try
            {
                bool done = settings.HomeBackTo == 1 ? HomeBackToBar() : HomeBackToFirstGame();
                return done;
            }
            catch (Exception ex) { logger.Warn("Kippix: B on the home screen: " + ex.Message); return false; }
        }

        private bool HomeBackToBar()
        {
            var win = Application.Current.MainWindow;
            var bar = UiTools.FindByName(win, "KX_TopBar");
            if (bar == null || !bar.IsVisible) return HomeBackToFirstGame();   // (no top bar shown: the first game instead)
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (focused != null && bar.IsAncestorOf(focused)) return true;   // (already up there)
            var buttons = new List<FrameworkElement>();
            Collect(bar, buttons);
            var pick = buttons.Where(b => b.IsVisible).Select(b => Tuple.Create(b, BoundsIn(b, win)))
                              .OrderBy(t => t.Item2.IsEmpty ? double.MaxValue : t.Item2.Left).Select(t => t.Item1).FirstOrDefault();
            if (pick == null) return HomeBackToFirstGame();
            PlaySound("move");
            Keyboard.Focus(pick);
            return true;
        }

        private bool HomeBackToFirstGame()
        {
            // the Board / Manual board: its first page, its first game tile (top left)
            if (IsBoardMode)
            {
                if (BoardTiles.Count == 0) return false;
                if (boardPage != 0) BoardFlip(0, Key.Left, new Rect(0, 0, 1, 1));
                var cards = BoardTiles.Where(t => t.IsCard && BoardUsable(t) && t.Card.Kind != "widget").OrderBy(t => t.Top).ThenBy(t => t.Left).ToList();
                var first = cards.FirstOrDefault(t => t.GameId != Guid.Empty) ?? cards.FirstOrDefault();
                if (first == null) return FocusBoard();
                var f = Keyboard.FocusedElement as FrameworkElement;
                if (f != null && ReferenceEquals(f.DataContext, first)) return true;
                PlaySound("move");
                FocusBoardTile(first);
                return true;
            }
            // Row's carousel: its first game (the folder tiles: the first folder)
            if (carActive)
            {
                int n = CarCount();
                if (n == 0) return false;
                int seq = FolderTilesShown ? 0 : carExtras.Count;
                if (seq >= n) seq = 0;
                if (carIndex == seq)
                {
                    // (already on it: just make sure it has the highlight, e.g. coming down from the top bar)
                    if (seq < carExtras.Count) { if (carProxy != null && !carProxy.IsKeyboardFocused) { PlaySound("move"); Keyboard.Focus(carProxy); } }
                    else
                    {
                        var g = carList != null ? carList.ItemContainerGenerator.ContainerFromIndex(CarListIndex(0)) as UIElement : null;
                        if (g == null || !g.IsKeyboardFocusWithin) { PlaySound("move"); CarFocusGame(CarListIndex(0)); }
                    }
                    return true;
                }
                CarGoTo(seq, seq < carIndex ? -1 : 1);
                return true;
            }
            // Grid / List (and Row without the carousel): Playnite's list, back to its first game
            var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ListBox;
            if (list == null || list.Items.Count == 0 || !list.IsVisible) return false;
            var c0 = list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
            if (list.SelectedIndex == 0 && c0 != null && c0.IsKeyboardFocusWithin) return true;
            PlaySound("move");
            list.SelectedIndex = 0;
            try { list.ScrollIntoView(list.Items[0]); list.UpdateLayout(); } catch (Exception) { }
            var sv = UiTools.FindChild<ScrollViewer>(list);
            if (sv != null) { sv.ScrollToTop(); sv.ScrollToLeftEnd(); }
            c0 = list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement ?? RealizeListItem(list, 0);
            if (c0 != null) Keyboard.Focus(c0);
            else
            {
                list.Focus();
                ui.BeginInvoke(new Action(() =>
                {
                    var c = list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
                    if (c != null) Keyboard.Focus(c);
                }), DispatcherPriority.Loaded);
            }
            return true;
        }
    }
}
