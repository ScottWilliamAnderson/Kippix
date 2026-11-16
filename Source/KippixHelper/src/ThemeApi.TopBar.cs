using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Top bar auto-hide: when focus leaves the top bar it fades away (except the icons you keep, and the clock
    /// if you keep it), and fades back in as soon as you move up into it. Done in code so the theme stays simple.
    /// </summary>
    public partial class ThemeApi
    {
        private bool topBarActive;
        private DateTime topBarHoldUntil = DateTime.MinValue;   // (0.99.60)
        private FrameworkElement topBarEl;

        public bool AutoHideTopBar
        {
            get { return settings.AutoHideTopBar; }
            set { if (SetSetting(ref settings.AutoHideTopBar, value)) { Notify("AutoHideTopBar"); UpdateTopBarFade(); } }
        }

        public bool TopBarKeepClock
        {
            get { return settings.TopBarKeepClock; }
            set { if (SetSetting(ref settings.TopBarKeepClock, value)) { Notify("TopBarKeepClock"); UpdateTopBarFade(); } }
        }

        private void OnStayChanged(ShortcutItem item)
        {
            settings.TopBarStayVisible = Shortcuts.Where(s => s.StayVisible).Select(s => s.Key).ToList();
            SaveSettings();
            UpdateTopBarFade();
        }

        private bool keyMenuHooked;

        /// <summary>An Alt tap (Steam's controller keys, or bringing a window in front) never opens the window's system menu.</summary>
        private static IntPtr NoKeyMenu(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0112 && (wParam.ToInt32() & 0xFFF0) == 0xF100) handled = true;   // WM_SYSCOMMAND / SC_KEYMENU
            return IntPtr.Zero;
        }

        private void OnWindowFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (!IsMainWindow(sender)) return;
                if (!keyMenuHooked && lookFullscreen)
                {
                    keyMenuHooked = true;
                    var src = System.Windows.Interop.HwndSource.FromVisual(Application.Current.MainWindow) as System.Windows.Interop.HwndSource;
                    if (src != null) src.AddHook(NoKeyMenu);
                }
                // (r13) found once, not searched for through the whole window on every highlight change
                if (topBarEl == null || !topBarEl.IsLoaded) topBarEl = UiTools.FindByName(Application.Current.MainWindow, "KX_TopBar");
                var bar = topBarEl;
                var focused = e.NewFocus as DependencyObject;
                bool inBar = bar != null && focused != null && bar.IsAncestorOf(focused);
                // (0.99.60) just back from a panel opened from the bar: a passing moment with the highlight nowhere doesn't hide it
                if (!inBar && topBarActive && DateTime.UtcNow < topBarHoldUntil && (focused == null || focused is Window || !(focused is UIElement) || !((UIElement)focused).IsVisible)) { }
                else if (inBar != topBarActive)
                {
                    topBarActive = inBar;
                    UpdateTopBarFade();
                }
                if (inBar) topBarHoldUntil = DateTime.MinValue;
                if (openPanel == "store" || tilePreviewItem != null) StoreFocusChanged(e);
                if (carActive) CarouselFocusChanged(e);   // Row's carousel follows the highlight in Playnite's row
                if (openPanel == "achievements") AchBgFocusChanged(e.NewFocus);   // Background › Game background follows the highlighted game
                if (openPanel == "options") FolderPreviewFocusChanged(e.NewFocus as DependencyObject);   // Folder: the home screen behind Game cards and layout
                if (openPanel == "options") GameSectionsFocusChanged(e.NewFocus);   // (r15) Top and lower page: the game page behind follows the section
            }
            catch (Exception ex) { logger.Warn("Kippix: focus: " + ex.Message); }
        }

        /// <summary>(0.89) Row › Full Screen's panels reach the top of the screen: the top bar keeps out of the way until Up from the
        /// games gives it the highlight (Down goes back), whatever Auto-hide is set to.</summary>
        private bool rowFullBarWas;
        private bool RowFullUnderBar { get { return IsFullscreen && carActive && LayoutNow == 0 && CarDesign == 2 && !FolderTilesShown && !detailsOpen; } }

        /// <summary>Fades each part of the top bar to where it should be right now.</summary>
        private void UpdateTopBarFade()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                bool hide = (settings.AutoHideTopBar || RowFullUnderBar) && !topBarActive && openPanel == null;

                Fade(UiTools.FindByName(win, "KX_TopLeft"), hide ? 0 : 1);
                LibraryHeaderPlace();   // the library view's header: over the profile's corner while that's away, beside it otherwise
                Fade(UiTools.FindByName(win, "KX_TopShade"), hide ? 0 : 1);
                // right side: hidden parts also fold away, so whatever stays slides together to the right edge
                FoldAway(UiTools.FindByName(win, "PART_ButtonNotifications"), hide, settings.ShortcutsVertical);
                FoldAway(UiTools.FindByName(win, "PART_ButtonProgramUpdate"), hide, settings.ShortcutsVertical);
                FoldAway(UiTools.FindByName(win, "KX_TopClock"), hide && !settings.TopBarKeepClock, settings.ShortcutsVertical);

                var list = UiTools.FindByName(win, "KX_TopShortcuts") as ItemsControl;
                if (list != null)
                    foreach (var item in Shortcuts)
                    {
                        var c = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                        FoldAway(c, hide && !item.StayVisible && !(item.IsClock && settings.TopBarKeepClock), settings.ShortcutsVertical);
                    }
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: top bar fade failed"); }
        }

        /// <summary>Fades an element out and shrinks its width to nothing (or brings it back), animated.</summary>
        private static void FoldAway(FrameworkElement el, bool away, bool vertical)
        {
            if (el == null) return;
            var scale = el.LayoutTransform as ScaleTransform;
            if (scale == null || scale.IsFrozen)
            {
                scale = new ScaleTransform(1, 1);
                el.LayoutTransform = scale;
            }
            double to = away ? 0 : 1;
            Fade(el, to);
            // the bar across the top folds sideways; down the side, it folds upwards
            var axis = vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;
            var other = vertical ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty;
            if (Math.Abs((double)scale.GetValue(other) - 1) > 0.01) { scale.BeginAnimation(other, null); scale.SetValue(other, 1.0); }
            if (Math.Abs((double)scale.GetValue(axis) - to) < 0.01) return;
            scale.BeginAnimation(axis, new DoubleAnimation(to, Motion(away ? 380 : 260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            });
        }

        private static void Fade(UIElement el, double to)
        {
            if (el == null) return;
            if (Math.Abs(el.Opacity - to) < 0.01) return;
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0.5 ? 220 : 380))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}
