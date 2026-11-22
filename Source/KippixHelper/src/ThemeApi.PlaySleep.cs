using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gdi = System.Drawing;
using GdiImaging = System.Drawing.Imaging;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.55) While a game runs and Playnite isn't in front, the theme draws nothing: the whole screen is hidden (kept, not
    /// taken apart), so nothing of it is drawn behind the game. The overlay and its windows are drawn on the same thread as
    /// Playnite, so whatever moved on the home screen or the game page (backgrounds, pictures, the clock...) made them and the
    /// game stutter. It all comes back the moment Playnite comes to the front again (the game closing, Alt+Tab, the overlay's
    /// Open Playnite), or when the game stops.
    /// While it's hidden, the window shows a still picture of the screen as it was just before. The task switcher (Alt+Tab,
    /// Task View, the Xbox one) shows the window as it is, and with the screen hidden that was Playnite's own window colour,
    /// a blank near-white. A still picture is drawn once and then costs nothing.
    /// </summary>
    public partial class ThemeApi
    {
        private bool playSleepHooked, playSleeping;
        private int playSleepGen;   // (a picture still being taken when Playnite wakes is dropped)
        private DispatcherTimer playSleepTimer;

        private void HookPlaySleep()
        {
            if (playSleepHooked) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;
            playSleepHooked = true;
            w.Activated += (s, e) => WakeFromPlay("Playnite in front");
            w.Deactivated += (s, e) => { if (AnyRunning) PlaySleepSoon(); };
        }

        /// <summary>A moment after the game has taken over (the launch screen has gone by then).</summary>
        private void PlaySleepSoon()
        {
            if (playSleepTimer == null)
            {
                playSleepTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(4) };
                playSleepTimer.Tick += (s, e) => { playSleepTimer.Stop(); SleepForPlay(); };
            }
            playSleepTimer.Stop();
            playSleepTimer.Start();
        }

        /// <summary>A game in front, and the screen still drawn.</summary>
        private bool MaySleepForPlay()
        {
            if (playSleeping || !AnyRunning || !IsFullscreen) return false;
            if (settings.RunKeepBrowsing && playniteActive) return false;   // (issue #11: one of Playnite's own menus is in front, not a game)
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            return w != null && w.Content is UIElement && !w.IsActive;
        }

        /// <summary>
        /// The picture first, then the screen goes. Windows hands over the window as it last showed it (about 40 ms, on a
        /// worker thread; 8 MB at 1920 x 1080). WPF's own RenderTargetBitmap would draw the whole screen again in software,
        /// on the UI thread: about 260 ms on a ROG Xbox Ally X.
        /// </summary>
        private void SleepForPlay()
        {
            try
            {
                if (!MaySleepForPlay()) return;
                IntPtr hwnd = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                int gen = ++playSleepGen;
                KippixShared.SafeWork.Queue(_ =>
                {
                    BitmapSource picture = null;
                    try { picture = WindowPicture.Take(hwnd); } catch (Exception) { }
                    OnUi(() => { if (gen == playSleepGen) HideForPlay(picture); });
                });
            }
            catch (Exception) { }
        }

        private void HideForPlay(BitmapSource picture)
        {
            try
            {
                if (!MaySleepForPlay()) return;   // (Playnite came to the front, or the game stopped, while the picture was taken)
                var w = Application.Current.MainWindow;
                Brush shown = Brushes.Black;   // (no picture: black, not the near-white)
                if (picture != null) { var b = new ImageBrush(picture) { Stretch = Stretch.Fill }; b.Freeze(); shown = b; }
                w.SetCurrentValue(Control.BackgroundProperty, shown);   // (for now: WakeFromPlay gives back Playnite's own)
                ((UIElement)w.Content).Visibility = Visibility.Hidden;
                playSleeping = true;
                Dbg("play: the theme isn't drawn while the game runs (" + (picture != null ? "a picture of it" : "black") + " in the task switcher)");
            }
            catch (Exception) { }
        }

        private void WakeFromPlay(string why)
        {
            if (playSleepTimer != null) playSleepTimer.Stop();
            playSleepGen++;
            if (!playSleeping) return;
            playSleeping = false;
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                var root = w != null ? w.Content as UIElement : null;
                if (root != null) root.Visibility = Visibility.Visible;
                if (w != null) w.InvalidateProperty(Control.BackgroundProperty);   // (Playnite's own colour, from wherever it came)
                Dbg("play: the theme is drawn again (" + why + ")");
            }
            catch (Exception) { }
        }

        /// <summary>From GameRunningChanged.</summary>
        private void PlaySleepGameRunning(bool running)
        {
            if (running) { HookPlaySleep(); PlaySleepSoon(); }
            else if (!AnyRunning) WakeFromPlay("the game stopped");
            else PlaySleepSoon();   // (issue #11: one of several stopped. Behind the others the screen stays hidden, or hides as planned)
        }

        /// <summary>A window's picture as Windows composes it, also while other windows cover it.</summary>
        private static class WindowPicture
        {
            [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
            [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
            [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
            [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
            private const uint PW_CLIENTONLY = 1, PW_RENDERFULLCONTENT = 2;   // (the second: what's on the screen, WPF's too)

            /// <summary>The inside of the window, pixel for pixel, or null. (Made smaller it took twice as long.)</summary>
            public static BitmapSource Take(IntPtr hwnd)
            {
                RECT r;
                if (hwnd == IntPtr.Zero || IsIconic(hwnd) || !GetClientRect(hwnd, out r)) return null;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return null;
                using (var bmp = new Gdi.Bitmap(w, h, GdiImaging.PixelFormat.Format32bppRgb))
                {
                    using (var g = Gdi.Graphics.FromImage(bmp))
                    {
                        IntPtr dc = g.GetHdc();
                        bool ok;
                        try { ok = PrintWindow(hwnd, dc, PW_CLIENTONLY | PW_RENDERFULLCONTENT); }
                        finally { g.ReleaseHdc(dc); }
                        if (!ok) return null;
                    }
                    var data = bmp.LockBits(new Gdi.Rectangle(0, 0, w, h), GdiImaging.ImageLockMode.ReadOnly, GdiImaging.PixelFormat.Format32bppRgb);
                    try
                    {
                        var picture = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * h, data.Stride);
                        picture.Freeze();   // (made here, shown on the UI thread)
                        return picture;
                    }
                    finally { bmp.UnlockBits(data); }
                }
            }
        }
    }
}
