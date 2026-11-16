using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.57) Where the mouse pointer goes when Playnite starts and when it comes back (from a game, or another window):
    /// Settings › General › Handheld, touch and mouse › Mouse pointer. Off screen (the default): it goes to the screen's
    /// bottom right corner and is hidden until the mouse really moves. Where it was: back where it was when Playnite last
    /// lost the front (a game moves it about). Middle of the screen. Only after Playnite has been away 2 seconds or more
    /// (not for the theme's own windows, the overlay's or a notice opening and closing).
    /// </summary>
    public partial class ThemeApi
    {
        [StructLayout(LayoutKind.Sequential)] private struct ParkPoint { public int X, Y; }
        [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool ParkGetCursorPos(out ParkPoint p);
        [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool ParkSetCursorPos(int x, int y);

        private bool mouseParkHooked, mouseParkHidden;
        private ParkPoint mouseWas, mouseParkedAt;
        private bool mouseWasKnown;
        private int mouseParkTries;
        private DateTime mouseAwaySince = DateTime.MinValue;

        public static readonly string[] MouseParkKeys = { "LOC_KX_MouseParkOff", "LOC_KX_MouseParkLast", "LOC_KX_MouseParkMiddle" };
        private static readonly string[] MouseParkText_ = { "Off screen", "Where it was", "Middle of the screen" };

        private string MouseParkText
        {
            get { int m = Math.Max(0, Math.Min(2, settings.MouseParkMode)); return Loc(MouseParkKeys[m], MouseParkText_[m]); }
        }

        private void HookMousePark()
        {
            if (mouseParkHooked || !IsFullscreen) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null || !w.IsLoaded)
            {
                // (not on screen yet: again in a moment, a few times)
                if (++mouseParkTries < 20 && ui != null)
                {
                    var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
                    t.Tick += (s0, e0) => { t.Stop(); HookMousePark(); };
                    t.Start();
                }
                return;
            }
            mouseParkHooked = true;
            // another Playnite window (an add-on's sign-in page, a dialog): the pointer is needed there, and that window's
            // mouse moves never reach the main window's PreviewMouseMove below, so it's shown as soon as the window loads
            try
            {
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
                {
                    if (s != Application.Current.MainWindow) ShowParkedMouse();
                }));
            }
            catch (Exception) { }
            w.Deactivated += (s, e) =>
            {
                mouseAwaySince = DateTime.UtcNow;
                ParkPoint p;
                if (ParkGetCursorPos(out p)) { mouseWas = p; mouseWasKnown = true; }
            };
            w.Activated += (s, e) =>
            {
                bool away = mouseAwaySince != DateTime.MinValue && (DateTime.UtcNow - mouseAwaySince).TotalSeconds >= 2;
                mouseAwaySince = DateTime.MinValue;
                if (away) ParkMouse();
            };
            // the pointer comes back when the mouse really moves (not the move to the corner itself)
            w.PreviewMouseMove += (s, e) =>
            {
                if (!mouseParkHidden) return;
                ParkPoint p;
                if (!ParkGetCursorPos(out p)) return;
                if (Math.Abs(p.X - mouseParkedAt.X) + Math.Abs(p.Y - mouseParkedAt.Y) > 8) ShowParkedMouse();
            };
            // Playnite starting: once it's on screen
            ui.BeginInvoke(new Action(ParkMouse), DispatcherPriority.ApplicationIdle);
        }

        private void ParkMouse()
        {
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                if (w == null) return;
                IntPtr h = new WindowInteropHelper(w).Handle;
                var b = KippixOverlay.Capture.MonitorBounds(h);   // (pixels)
                switch (settings.MouseParkMode)
                {
                    case 1:
                        if (mouseWasKnown) ParkSetCursorPos(mouseWas.X, mouseWas.Y);
                        break;
                    case 2:
                        ParkSetCursorPos(b.Left + b.Width / 2, b.Top + b.Height / 2);
                        break;
                    default:
                        ParkSetCursorPos(b.Right - 1, b.Bottom - 1);
                        ParkGetCursorPos(out mouseParkedAt);
                        mouseParkHidden = true;
                        Mouse.OverrideCursor = Cursors.None;
                        break;
                }
            }
            catch (Exception) { }
        }

        private void ShowParkedMouse()
        {
            if (!mouseParkHidden) return;
            mouseParkHidden = false;
            try { if (Mouse.OverrideCursor == Cursors.None) Mouse.OverrideCursor = null; } catch (Exception) { }
        }

        private void CycleMousePark()
        {
            settings.MouseParkMode = (Math.Max(0, Math.Min(2, settings.MouseParkMode)) + 1) % 3;
            SaveSettings();
            PlaySound("move");
            if (settings.MouseParkMode != 0) ShowParkedMouse();
        }
    }
}
