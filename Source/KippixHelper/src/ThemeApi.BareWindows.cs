using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KippixHelper
{
    /// <summary>
    /// Playnite's own dialogs in fullscreen mode (the web view window add-ons open for a sign-in, most of all) are WindowBase
    /// windows, and the only style for WindowBase is in the desktop themes: in fullscreen they come up with no template at all,
    /// so nothing inside them is ever drawn (a black window with a title bar; the page is loaded, just not shown). Any window
    /// that loads without a template is given a plain one here: its content on the panel colour.
    /// </summary>
    public partial class ThemeApi
    {
        private static bool bareWindowsHooked;

        private void HookBareWindows()
        {
            if (bareWindowsHooked || !IsFullscreen) return;
            bareWindowsHooked = true;
            try
            {
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(BareWindowLoaded));
            }
            catch (Exception ex) { logger.Warn("Kippix: windows without a template: " + ex.Message); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ShowCursor")] private static extern int BareShowCursor(bool show);

        /// <summary>
        /// A dialog needs a pointer and clicks: in fullscreen the pointer is hidden for the thread (ShowCursor below zero) and
        /// the window can come up with hit testing off, so neither a mouse nor a tap reaches a sign-in page. Both are put right
        /// while the window is open, and the pointer is hidden again, call for call, when it closes.
        /// </summary>
        private void BareWindowPointer(Window w)
        {
            try
            {
                if (!w.IsHitTestVisible) w.IsHitTestVisible = true;
                if (System.Windows.Input.Mouse.OverrideCursor == System.Windows.Input.Cursors.None) System.Windows.Input.Mouse.OverrideCursor = null;
                int shown = 0;
                while (BareShowCursor(true) < 0 && shown < 20) shown++;
                int back = shown + 1;
                w.Closed += (s, e) => { try { for (int i = 0; i < back; i++) BareShowCursor(false); } catch (Exception) { } };
            }
            catch (Exception ex) { logger.Warn("Kippix: a dialog's pointer: " + ex.Message); }
        }

        private void BareWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var w = sender as Window;
                if (w == null || w == Application.Current.MainWindow) return;
                BareWindowPointer(w);
                if (w.Template != null) return;
                var border = new FrameworkElementFactory(typeof(Border));
                border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
                var adorner = new FrameworkElementFactory(typeof(AdornerDecorator));
                adorner.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
                border.AppendChild(adorner);
                var template = new ControlTemplate(w.GetType()) { VisualTree = border };
                template.Seal();
                if (w.Background == null)
                {
                    var bg = new SolidColorBrush(HexOr(LookNow.Panel, Color.FromRgb(0x1B, 0x1E, 0x24)));
                    bg.Freeze();
                    w.Background = bg;
                }
                w.Template = template;
                w.ApplyTemplate();
                w.UpdateLayout();
                logger.Info("Kippix: a window without a template (" + w.GetType().Name + ") was given one, so its content shows");
            }
            catch (Exception ex) { logger.Warn("Kippix: windows without a template: " + ex.Message); }
        }
    }
}
