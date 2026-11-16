using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Keeps the highlight in the menu while it's open. When the highlighted row of a menu goes (its list changed under it),
    /// WPF hands the keyboard focus to the window, and Playnite at once puts it on the first button it finds: the game
    /// page's Play button, behind the menu ("Lost keyboard focus from known controls" in Playnite's log). That move is
    /// refused here and the highlight goes back into the menu: the same row if it's still there, else the row for the same
    /// thing, else the first row.
    /// </summary>
    public partial class ThemeApi
    {
        private WeakReference lastMenuFocus;   // the last element in the menu that had the highlight
        private object lastMenuFocusData;      // and what it showed (its row)
        private int focusLossLogs;

        private void InitFocusGuard()
        {
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGuardPreviewFocus), true);
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnMenuFocusScroll), true);
            // (0.86) the last notification dismissed: the bell hides, so a highlight on it (or coming back to it) goes onto the Board
            try
            {
                var msgs = api.Notifications.Messages as System.Collections.Specialized.INotifyCollectionChanged;
                if (msgs != null) msgs.CollectionChanged += (o, ev) =>
                {
                    try { if (api.Notifications.Count > 0) return; } catch (Exception) { return; }
                    ui.BeginInvoke(new Action(() =>
                    {
                        if (!IsBoardMode || openPanel != null || detailsOpen || confirmOpen || kbOpen) return;
                        var f = Keyboard.FocusedElement as UIElement;
                        if (f != null && f.IsVisible && !(f is Window) && boardHooked != null && boardHooked.IsAncestorOf(f)) return;
                        var fe = f as FrameworkElement;
                        if (f == null || f is Window || !f.IsVisible || (fe != null && fe.Name == "PART_ButtonNotifications")) FocusBoard();
                    }), DispatcherPriority.ContextIdle);
                };
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The first row of a menu page highlighted: the page scrolls right to its top (its heading and anything above it
        /// show, e.g. Community layouts); the last row: right to its bottom (the notes under it show).
        /// </summary>
        private void OnMenuFocusScroll(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (!lookFullscreen || !IsMainWindow(sender)) return;
                var f = e.NewFocus as FrameworkElement;
                if (f == null || openPanel == null) return;
                // (Getting started scrolls its own lists: TourListStep / TourScrollTo. And its highlighted row is drawn above its
                //  neighbours (Panel.ZIndex), which puts it last in the visual tree: here every row looked like the last one, so
                //  each move sent a list longer than the screen, like Your stores, to its bottom and the highlight out of view)
                if (openPanel == "welcome") return;
                ScrollViewer sv = null;
                for (DependencyObject d = VisualTreeHelper.GetParent(f); d != null; d = VisualTreeHelper.GetParent(d))
                {
                    sv = d as ScrollViewer;
                    if (sv != null) break;
                    var fe = d as FrameworkElement;
                    if (fe != null && (fe.Name == "KX_OptionsPanel" || fe.Name == "KX_ProfilePanel")) return;   // not in a scrolling page
                }
                if (sv == null || sv.ScrollableHeight <= 0) return;
                ButtonBase first = null, last = null;
                foreach (var b in Descendants<ButtonBase>(sv))
                {
                    if (!b.IsVisible || !b.Focusable || !b.IsEnabled) continue;
                    if (first == null) first = b;
                    last = b;
                }
                var mine = f as ButtonBase ?? FindAncestor<ButtonBase>(f, sv);
                if (mine == null) return;
                if (ReferenceEquals(mine, first)) ui.BeginInvoke(new Action(() => sv.ScrollToTop()), DispatcherPriority.Loaded);
                else if (ReferenceEquals(mine, last)) ui.BeginInvoke(new Action(() => sv.ScrollToBottom()), DispatcherPriority.Loaded);
            }
            catch (Exception) { }
        }

        private static T FindAncestor<T>(DependencyObject d, DependencyObject stop) where T : DependencyObject
        {
            for (var p = VisualTreeHelper.GetParent(d); p != null && !ReferenceEquals(p, stop); p = VisualTreeHelper.GetParent(p))
            {
                var t = p as T;
                if (t != null) return t;
            }
            return null;
        }

        private WeakReference menuRootRef;

        private UIElement OpenMenuRoot()
        {
            if (openPanel != "options" || Application.Current == null) return null;
            var p = menuRootRef != null ? menuRootRef.Target as UIElement : null;
            if (p == null || PresentationSource.FromVisual(p) == null)
            {
                p = UiTools.FindByName(Application.Current.MainWindow, "KX_OptionsPanel") as UIElement;
                menuRootRef = p != null ? new WeakReference(p) : null;
            }
            return p != null && p.IsVisible ? p : null;
        }

        private void OnGuardPreviewFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (!lookFullscreen || !IsMainWindow(sender) || kbOpen || confirmOpen) return;
                var menu = OpenMenuRoot();
                if (menu == null) { BoardLostGuard(e); return; }
                var nf = e.NewFocus as Visual;   // (a ContentElement, e.g. a link in text: left alone)
                if (nf == null) return;
                if (menu.IsAncestorOf(nf))
                {
                    lastMenuFocus = new WeakReference(nf);
                    var fe = nf as FrameworkElement;
                    lastMenuFocusData = fe != null ? fe.DataContext : null;
                    return;
                }
                if (nf is Window)
                {
                    // the highlighted element in the menu went away: note who did it (for the log), the next move is checked below
                    var old = e.OldFocus as DependencyObject;
                    if (old != null && focusLossLogs < 6 && (lastMenuFocus != null && ReferenceEquals(lastMenuFocus.Target, old)))
                    {
                        focusLossLogs++;
                        string frames = string.Join(" < ", Environment.StackTrace.Split('\n')
                            .Where(l => l.Contains("Kippix")).Select(l => l.Trim()).Skip(1).Take(6).ToArray());
                        if (frames.Length > 0) logger.Info("Kippix: the menu lost its highlight (" + optionsPage + "): " + frames);
                        else focusLossLogs--;   // (a page change: nothing to report)
                    }
                    return;
                }
                // focus going somewhere outside the open menu: only stopped when it comes from nowhere (Playnite picking
                // something after the highlight was lost), never a move someone made on purpose
                bool fromNowhere = e.OldFocus == null || e.OldFocus is Window || !IsLive(e.OldFocus as DependencyObject);
                if (!fromNowhere) return;
                e.Handled = true;
                Dbg("focus guard: kept the highlight in the menu (" + optionsPage + ") instead of " + nf.GetType().Name);
                ui.BeginInvoke(new Action(RestoreMenuFocus), DispatcherPriority.Input);
            }
            catch (Exception ex) { logger.Warn(ex, "Kippix: focus guard"); }
        }

        /// <summary>
        /// (0.86) The Board's highlight lost: e.g. the notifications bell was pressed, the last one dismissed, and the bell
        /// (now hidden) had the highlight to come back to. Playnite then puts it on a button it knows, which on the Board is
        /// hidden too, and every press after that is lost ("Lost keyboard focus from known controls"). When the highlight
        /// comes from nowhere and isn't going onto something you can see on the Board or the top bar, it goes back onto
        /// the Board's tile instead.
        /// </summary>
        private void BoardLostGuard(KeyboardFocusChangedEventArgs e)
        {
            if (!IsBoardMode || openPanel != null || detailsOpen || boardHooked == null) return;
            bool fromNowhere = e.OldFocus == null || e.OldFocus is Window || !IsLive(e.OldFocus as DependencyObject);
            if (!fromNowhere) return;
            var to = e.NewFocus as UIElement;
            if (to != null && to.IsVisible && boardHooked.IsAncestorOf(to)) return;   // onto the Board: fine
            if (to is Window) return;   // (the step in between: the next one is looked at)
            ui.BeginInvoke(new Action(() =>
            {
                if (!IsBoardMode || openPanel != null || detailsOpen || confirmOpen || kbOpen) return;
                var f = Keyboard.FocusedElement as UIElement;
                if (f != null && f.IsVisible && !(f is Window)) return;   // somewhere you can see (the Board, the top bar, Playnite's notifications): fine
                Dbg("focus guard: the Board's highlight was lost (" + (to != null ? to.GetType().Name : "nothing") + "): back onto its tile");
                FocusBoard();
            }), DispatcherPriority.Input);
        }

        private static bool IsLive(DependencyObject d)
        {
            if (d is ContentElement) return true;   // (not judged: treated as a real element)
            var u = d as UIElement;
            if (u == null || !u.IsVisible) return false;
            return PresentationSource.FromDependencyObject(d) != null;
        }

        /// <summary>Puts the highlight back in the open menu: the same element, the element now showing the same row, or the first row.</summary>
        private void RestoreMenuFocus()
        {
            try
            {
                var menu = OpenMenuRoot();
                if (menu == null) return;
                var f = Keyboard.FocusedElement as DependencyObject;
                if (f != null && menu.IsAncestorOf(f)) return;   // already back
                var was = lastMenuFocus != null ? lastMenuFocus.Target as UIElement : null;
                if (was != null && menu.IsAncestorOf(was) && IsLive(was) && was.Focusable && was.Focus()) return;
                if (lastMenuFocusData != null)
                {
                    var key = lastMenuFocusData is PickRow ? ((PickRow)lastMenuFocusData).Key : null;
                    foreach (var b in Descendants<ButtonBase>(menu))
                    {
                        if (!b.IsVisible || !b.Focusable) continue;
                        var dc = b.DataContext;
                        bool same = ReferenceEquals(dc, lastMenuFocusData) || (key != null && dc is PickRow && ((PickRow)dc).Key == key);
                        if (same && b.Focus()) return;
                    }
                }
                foreach (var b in Descendants<ButtonBase>(menu))
                    if (b.IsVisible && b.Focusable && b.IsEnabled && b.Focus()) return;
            }
            catch (Exception ex) { logger.Warn(ex, "Kippix: putting the highlight back in the menu"); }
        }

        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                var t = c as T;
                if (t != null) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }
    }
}
