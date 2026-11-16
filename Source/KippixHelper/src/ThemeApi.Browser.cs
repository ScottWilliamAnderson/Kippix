using System;
using System.Windows.Media;
using System.Windows.Data;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>
    /// A web browser inside the fullscreen theme, so store pages (and buying) work without leaving it.
    /// It's Playnite's own Chromium (CefSharp, already loaded and signed in wherever Playnite's library logins are),
    /// made by reflection so the add-on doesn't depend on one CefSharp version.
    /// Controller: left stick moves a cursor, A clicks (hold to drag), right stick scrolls, D-pad sends arrow keys,
    /// Y types with the theme's keyboard (it also comes up by itself when A is pressed on a text box), LB / RB zoom,
    /// X reloads, B goes back a page (or closes), View / Start close.
    /// Only exists while it's open: closing it disposes the browser and stops the cursor timer.
    /// </summary>
    public partial class ThemeApi
    {
        private FrameworkElement browser;
        private Type browserType;
        private string browserReturn;              // the panel underneath (usually the Store)
        private IInputElement browserReturnFocus;
        private DispatcherTimer browserTick;
        private Point cursorPos;                   // in KX_BrowserHost's coordinates (1080p units)
        private bool cursorDown;
        private bool hitTestBefore;
        private int cursorHides;
        private double scrollCarryX, scrollCarryY;
        private NativePoint lastSysCursor;
        private Border browserHost;
        private FrameworkElement browserCursor;

        public bool IsBrowserOpen { get { return openPanel == "browser"; } }
        public string BrowserTitle { get; private set; }
        public string BrowserAddress { get; private set; }
        public bool BrowserLoading { get; private set; }
        public string BrowserZoomText { get { return Math.Round(Math.Pow(1.2, settings.BrowserZoom) * 100) + "%"; } }

        public ICommand BrowserBackCommand { get; private set; }
        public ICommand BrowserForwardCommand { get; private set; }
        public ICommand BrowserReloadCommand { get; private set; }
        public ICommand BrowserCloseCommand { get; private set; }

        private void InitBrowserCommands()
        {
            BrowserBackCommand = new RelayCommand(_ => BrowserCommand("BackCommand"));
            BrowserForwardCommand = new RelayCommand(_ => BrowserCommand("ForwardCommand"));
            BrowserReloadCommand = new RelayCommand(_ => BrowserCommand("ReloadCommand"));
            BrowserCloseCommand = new RelayCommand(_ => CloseBrowser());
        }

        private void NotifyBrowser()
        {
            foreach (string n in new[] { "IsBrowserOpen", "BrowserTitle", "BrowserAddress", "BrowserLoading", "BrowserZoomText" }) Notify(n);
        }

        // ---------------- open / close ----------------

        public bool BrowserAutoKeyboard
        {
            get { return settings.BrowserAutoKeyboard; }
            set { if (SetSetting(ref settings.BrowserAutoKeyboard, value)) Notify("BrowserAutoKeyboard"); }
        }
        public ICommand CycleLinksOpenInCommand { get { return cycleLinksOpenIn ?? (cycleLinksOpenIn = new RelayCommand(_ => { settings.LinksOpenIn = settings.LinksOpenIn == 0 ? 1 : 0; SaveSettings(); Notify("LinksOpenInText"); })); } }
        private ICommand cycleLinksOpenIn;
        public string LinksOpenInText { get { return settings.LinksOpenIn == 0 ? Loc("LOC_KX_LinksInTheme", "Built-in browser") : Loc("LOC_KX_LinksInDefault", "Default browser"); } }

        /// <summary>
        /// A link to read (a news post, a game's link, a guide, a mod's page): in the theme's browser, or in Windows' default
        /// browser when that's chosen (Store settings › Browser › Open links in). What has to stay in the theme's browser
        /// (signing in, where Playnite's own browser keeps the sign-in; the Store's pages, which have their own setting;
        /// a streaming service set to open in the theme) calls OpenBrowser itself.
        /// </summary>
        internal void OpenLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (settings.LinksOpenIn == 1 && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
                catch (Exception ex) { logger.Warn("Kippix: the default browser didn't open " + url + ": " + ex.Message); }
            }
            OpenBrowser(url);
        }

        /// <summary>Shows a web page in the theme's browser (or Windows' default browser if Playnite's can't be used).</summary>
        internal void OpenBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!EnsureBrowser())
            {
                logger.Warn("Kippix: the in-theme browser isn't available, opening " + url + " in the default browser");
                try { Process.Start(url); } catch (Exception) { }
                return;
            }
            SetBrowserValue("Address", url);
            BrowserAddress = url;
            if (openPanel != "browser")
            {
                if (openPanel == "store") { StopStoreVideos(); storePickerOpen = false; NotifyStoreOverlays(); }
                // (a link in a menu that closes as it opens the page, like the game page's More: the highlight is still on the
                //  menu's row, which is going. Where that menu was handing it back to is where the browser hands it back to too:
                //  otherwise closing the browser found nothing to go back to and put the highlight on the top bar's ⋯ button)
                if (openPanel == null) { focusBeforePanel = focusClosingPanel ?? Keyboard.FocusedElement; RememberLowerFocus(); }
                browserReturn = openPanel;
                browserReturnFocus = Keyboard.FocusedElement;
                openPanel = "browser";
                NotifyPanels();
                UpdateTopBarFade();
                StartBrowserInput();
                UpdateMusicState();
            }
            NotifyBrowser();
            ui.BeginInvoke(new Action(() =>
            {
                if (browser == null) return;
                Keyboard.Focus(browser);
                var host = BrowserHost();
                if (host != null) { cursorPos = new Point(host.ActualWidth / 2, host.ActualHeight / 2); MoveCursor(true); }
            }), DispatcherPriority.Loaded);
        }

        private void CloseBrowser()
        {
            if (openPanel != "browser") return;
            PlaySound("back");
            StopBrowserInput();
            DisposeBrowser();
            string back = browserReturn;
            var focus = browserReturnFocus as UIElement;
            browserReturn = null;
            browserReturnFocus = null;
            if (back == null) { ClosePanels(); UpdateMusicState(); return; }
            openPanel = back;
            NotifyPanels();
            NotifyBrowser();
            UpdateTopBarFade();
            UpdateMusicState();
            ui.BeginInvoke(new Action(() => { if (focus != null && focus.IsVisible && focus.Focusable) Keyboard.Focus(focus); else FocusPanelSoon(); }), DispatcherPriority.ContextIdle);
        }

        /// <summary>B: back a page, or close on the first page.</summary>
        private void BrowserBackOrClose()
        {
            object can = GetBrowserValue("CanGoBack");
            if (can is bool && (bool)can) { PlaySound("back"); BrowserCommand("BackCommand"); }
            else CloseBrowser();
        }

        private Border BrowserHost()
        {
            if (browserHost != null) return browserHost;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            browserHost = w != null ? UiTools.FindByName(w, "KX_BrowserHost") as Border : null;
            return browserHost;
        }

        private bool EnsureBrowser()
        {
            if (browser != null) return true;
            try
            {
                var host = BrowserHost();
                if (host == null) return false;
                if (browserType == null) browserType = FindBrowserType();
                if (browserType == null) return false;
                browser = (FrameworkElement)Activator.CreateInstance(browserType);
                // 60 frames a second instead of Chromium's 30 for an off-screen page (smoother scrolling and cursor)
                try
                {
                    object bs = GetBrowserValue("BrowserSettings");
                    var fr = bs != null ? bs.GetType().GetProperty("WindowlessFrameRate") : null;
                    if (fr != null && fr.CanWrite) fr.SetValue(bs, 60, null);
                }
                catch (Exception) { }
                browser.Focusable = true;
                browser.FocusVisualStyle = null;
                host.Child = browser;
                foreach (string name in new[] { "Title", "Address", "IsLoading" })
                {
                    var dpd = DependencyPropertyDescriptor.FromName(name, browserType, browserType);
                    if (dpd != null) dpd.AddValueChanged(browser, OnBrowserChanged);
                }
                // our keys at the browser itself, before Playnite's own window shortcuts see them
                browser.AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler((s, e) => BrowserKey(e)), true);
                browser.AddHandler(Keyboard.KeyUpEvent, new KeyEventHandler((s, e) => BrowserKey(e)), true);
                // a text box on the page pressed (A, a tap, a click): the keyboard comes up (BrowserTextBoxPicked)
                browser.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) => browserPressAt = DateTime.UtcNow), true);
                browser.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler((s, e) => browserPressAt = DateTime.UtcNow), true);
                browser.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((s, e) => browserPressAt = DateTime.UtcNow), true);
                browser.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>((s, e) => browserPressAt = DateTime.UtcNow), true);
                HookBrowserEvent("FrameLoadEnd", "BrowserFrameLoaded");
                HookBrowserEvent("ConsoleMessage", "BrowserConsoleMessage");
                // pop-ups and "new tab" links open in this same browser (a separate window couldn't be used with the controller)
                try
                {
                    var lsh = browserType.GetProperty("LifeSpanHandler");
                    object handler = lsh != null ? BrowserPopups.Create(lsh.PropertyType) : null;
                    if (handler != null) lsh.SetValue(browser, handler, null);
                }
                catch (Exception ex) { logger.Warn("Kippix: pop-ups will open in their own window: " + ex.Message); }
                // (an empty / about:blank pop-up is one a page fills in itself: following it left a blank page)
                BrowserPopups.Open = url => OnUi(() =>
                {
                    Dbg("browser pop-up: " + url);
                    if (browser != null && !string.IsNullOrEmpty(url) && !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) SetBrowserValue("Address", url);
                });
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Kippix: couldn't create the browser");
                browser = null;
                return false;
            }
        }

        // ---------------- the keyboard by itself ----------------
        //
        // Typing needed Y first: nothing said a text box had been picked. Now every page (and every frame in it) gets a few
        // lines of script that say so on the page's console when a text box has the page's focus after a press, and the
        // browser's console messages are listened to. Only a press of yours counts (within a moment of it): a page that puts
        // the cursor in its search box by itself, or the box getting its focus back when the keyboard closes, brings up nothing.
        // The keyboard is the one Y opens: Done types the text into the box, B closes it.

        private const string TextBoxMark = "__kxkb:1";
        private const string TextBoxScript =
            "(function(){if(window.__kxkb)return;window.__kxkb=1;" +
            "function ed(t){if(!t||!t.tagName)return false;var n=t.tagName.toLowerCase();" +
            "if(n==='textarea')return !t.readOnly&&!t.disabled;" +
            "if(n==='input'){var y=(t.getAttribute('type')||'text').toLowerCase();return !t.readOnly&&!t.disabled&&['text','search','email','password','url','tel','number'].indexOf(y)>=0;}" +
            "return !!t.isContentEditable;}" +
            "function act(){var a=document.activeElement;while(a&&a.shadowRoot&&a.shadowRoot.activeElement)a=a.shadowRoot.activeElement;return a;}" +
            "function say(){if(ed(act()))console.log('" + TextBoxMark + "');}" +
            "document.addEventListener('focusin',say,true);document.addEventListener('mouseup',say,true);document.addEventListener('touchend',function(){setTimeout(say,50);},true);})();";

        private DateTime browserPressAt = DateTime.MinValue;

        /// <summary>One of the browser's own events (its argument type is CefSharp's: the handler takes any EventArgs).</summary>
        private void HookBrowserEvent(string name, string method)
        {
            try
            {
                var ev = browserType.GetEvent(name);
                var m = typeof(ThemeApi).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                if (ev == null || m == null) return;
                ev.AddEventHandler(browser, Delegate.CreateDelegate(ev.EventHandlerType, this, m));
            }
            catch (Exception ex) { logger.Warn("Kippix: browser " + name + " (the keyboard won't come up by itself): " + ex.Message); }
        }

        /// <summary>A frame finished loading (on Chromium's thread): it gets the script.</summary>
        private void BrowserFrameLoaded(object sender, EventArgs e)
        {
            try
            {
                var fp = e.GetType().GetProperty("Frame");
                object frame = fp != null ? fp.GetValue(e, null) : null;
                if (frame == null) return;
                var run = frame.GetType().GetMethod("ExecuteJavaScriptAsync", new[] { typeof(string), typeof(string), typeof(int) });
                if (run == null)
                    foreach (var i in frame.GetType().GetInterfaces()) { run = i.GetMethod("ExecuteJavaScriptAsync", new[] { typeof(string), typeof(string), typeof(int) }); if (run != null) break; }
                if (run != null) run.Invoke(frame, new object[] { TextBoxScript, "about:blank", 1 });
            }
            catch (Exception) { }
        }

        /// <summary>The page's console (on Chromium's thread): our script's line.</summary>
        private void BrowserConsoleMessage(object sender, EventArgs e)
        {
            try
            {
                var mp = e.GetType().GetProperty("Message");
                if (mp == null || !string.Equals(mp.GetValue(e, null) as string, TextBoxMark, StringComparison.Ordinal)) return;
                OnUi(BrowserTextBoxPicked);
            }
            catch (Exception) { }
        }

        private void BrowserTextBoxPicked()
        {
            if (!settings.BrowserAutoKeyboard || openPanel != "browser" || browser == null || kbOpen) return;
            if ((DateTime.UtcNow - browserPressAt).TotalMilliseconds > 1500) return;   // (not from a press of yours)
            browserPressAt = DateTime.MinValue;
            Dbg("browser: a text box was pressed, the keyboard comes up");
            AskBrowserText();
        }

        private Type FindBrowserType()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (a.GetName().Name != "CefSharp.Wpf") continue;
                    var t = a.GetType("CefSharp.Wpf.ChromiumWebBrowser");
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            string path = Path.Combine(api.Paths.ApplicationPath, "CefSharp.Wpf.dll");
            if (!File.Exists(path)) return null;
            return Assembly.LoadFrom(path).GetType("CefSharp.Wpf.ChromiumWebBrowser");
        }

        private void DisposeBrowser()
        {
            steamLoginLoads.Clear();
            steamLoginLoopPage = null;
            if (steamSigningIn) SteamSignInClosed();   // signed in on Steam's page (it doesn't always reload): check, then send the wishlist
            steamSigningIn = false;
            if (browser == null) return;
            var b = browser;
            browser = null;
            try
            {
                foreach (string name in new[] { "Title", "Address", "IsLoading" })
                {
                    var dpd = DependencyPropertyDescriptor.FromName(name, browserType, browserType);
                    if (dpd != null) dpd.RemoveValueChanged(b, OnBrowserChanged);
                }
                var host = BrowserHost();
                if (host != null) host.Child = null;
                var d = b as IDisposable;
                if (d != null) d.Dispose();
            }
            catch (Exception ex) { logger.Warn("Kippix: closing the browser: " + ex.Message); }
            BrowserTitle = "";
            BrowserAddress = "";
            BrowserLoading = false;
            BrowserPopups.Open = null;
        }

        private void OnBrowserChanged(object sender, EventArgs e)
        {
            if (browser == null) return;
            // (called from inside the browser's own property change: nothing may escape from here)
            try
            {
                bool wasLoading = BrowserLoading;
                BrowserTitle = Convert.ToString(GetBrowserValue("Title"));
                string addr = Convert.ToString(GetBrowserValue("Address"));
                if (addr != BrowserAddress) Dbg("browser: " + addr);
                BrowserAddress = addr;
                object loading = GetBrowserValue("IsLoading");
                BrowserLoading = loading is bool && (bool)loading;
                if (wasLoading && !BrowserLoading) ApplyZoom();   // a new site starts at 100%: put your zoom back
                NotifyBrowser();
                if (steamSigningIn && (BrowserLoading != wasLoading)) SteamSignInProgress(addr, BrowserLoading);
                if (BrowserLoading && !wasLoading) SteamLoginLoopCheck(addr);
            }
            catch (Exception ex) { logger.Warn("Kippix: browser change: " + ex.Message); }
        }

        private object GetBrowserValue(string name)
        {
            try
            {
                var p = browserType != null && browser != null ? browserType.GetProperty(name) : null;
                return p != null ? p.GetValue(browser, null) : null;
            }
            catch (Exception) { return null; }
        }

        private void SetBrowserValue(string name, object value)
        {
            try
            {
                var p = browserType != null && browser != null ? browserType.GetProperty(name) : null;
                if (p != null && p.CanWrite) p.SetValue(browser, value, null);
            }
            catch (Exception ex) { logger.Warn("Kippix: browser " + name + ": " + ex.Message); }
        }

        private void BrowserCommand(string name)
        {
            var cmd = GetBrowserValue(name) as ICommand;
            if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
        }

        private void Zoom(int step)
        {
            settings.BrowserZoom = Math.Max(-3, Math.Min(4, settings.BrowserZoom + step * 0.5));
            SaveSettings();
            ApplyZoom();
            Notify("BrowserZoomText");
        }

        /// <summary>Chromium keeps zoom per site, so set it again on every page (nudging it so the value really changes).</summary>
        private void ApplyZoom()
        {
            SetBrowserValue("ZoomLevel", settings.BrowserZoom + 0.001);
            SetBrowserValue("ZoomLevel", settings.BrowserZoom);
        }

        // ---------------- controller ----------------

        private void StartBrowserInput()
        {
            var w = Application.Current.MainWindow;
            hitTestBefore = w.IsHitTestVisible;
            w.IsHitTestVisible = true;              // Playnite turns mouse input off when it hides the cursor
            cursorHides = 0;
            do { cursorHides++; } while (ShowCursor(false) >= 0 && cursorHides < 10);   // we draw our own cursor (undone call for call on close)
            GetCursorPos(out lastSysCursor);
            scrollCarryX = scrollCarryY = 0;
            if (browserTick == null)
            {
                browserTick = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(16) };
                browserTick.Tick += (s, e) => BrowserTick();
            }
            browserTick.Start();
            HideBehindBrowser();
        }

        // ---- don't draw what the browser covers ----
        // WPF redraws everything under a part of the screen that changes, and the browser page changes all the time:
        // with the library, backgrounds and panels hidden underneath it only has to draw the page.
        private readonly List<Tuple<UIElement, object>> hiddenBehind = new List<Tuple<UIElement, object>>();

        private void HideBehindBrowser()
        {
            RestoreBehindBrowser();
            try
            {
                var view = UiTools.FindByName(Application.Current.MainWindow, "KX_BrowserView") as UIElement;
                DependencyObject child = view;
                for (int level = 0; level < 2 && child != null; level++)
                {
                    var parent = VisualTreeHelper.GetParent(child) as Panel;
                    if (parent == null) break;
                    foreach (UIElement el in parent.Children)
                    {
                        if (ReferenceEquals(el, child) || el.Visibility != Visibility.Visible) continue;
                        hiddenBehind.Add(Tuple.Create(el, el.ReadLocalValue(UIElement.VisibilityProperty)));
                        el.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Hidden);
                    }
                    child = parent;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: hiding behind the browser: " + ex.Message); }
        }

        private void RestoreBehindBrowser()
        {
            foreach (var h in hiddenBehind)
            {
                try
                {
                    var el = h.Item1;
                    var expr = h.Item2 as BindingExpressionBase;
                    if (expr != null) { var b = BindingOperations.GetBindingBase(el, UIElement.VisibilityProperty); if (b == null) BindingOperations.SetBinding(el, UIElement.VisibilityProperty, expr.ParentBindingBase); else expr.UpdateTarget(); }
                    else if (h.Item2 == DependencyProperty.UnsetValue) el.InvalidateProperty(UIElement.VisibilityProperty);
                    else el.SetCurrentValue(UIElement.VisibilityProperty, h.Item2);
                    if (el.Visibility == Visibility.Hidden && h.Item2 == DependencyProperty.UnsetValue) el.ClearValue(UIElement.VisibilityProperty);
                }
                catch (Exception) { }
            }
            hiddenBehind.Clear();
        }

        private void StopBrowserInput()
        {
            RestoreBehindBrowser();
            if (browserTick != null) browserTick.Stop();
            if (cursorDown) { MouseButton(false); }
            try { Application.Current.MainWindow.IsHitTestVisible = hitTestBefore; } catch (Exception) { }
            for (int i = 0; i < cursorHides; i++) ShowCursor(true);
            cursorHides = 0;
        }

        private static readonly double[] CursorSpeeds = { 0, 7, 11, 16, 22, 30 };   // 1080p px per tick at full tilt

        private void BrowserTick()
        {
            if (openPanel != "browser" || browser == null) { if (browserTick != null) browserTick.Stop(); return; }
            short lx, ly, rx, ry;
            ReadSticks(out lx, out ly, out rx, out ry);
            // the right stick only scrolls: Steam (its desktop controls for the Steam Controller) also moves the mouse with it,
            // so while it's tilted (and just after) the mouse is held where the browser's cursor is
            if (Math.Abs((int)rx) + Math.Abs((int)ry) > 3000)   // (well under the scroll dead zone: Steam moves the mouse from a slight tilt)
            {
                if (browserRightAt == DateTime.MinValue) logger.Info("Kippix: browser: the right stick scrolls (" + browserStickFrom + ")");
                browserRightAt = DateTime.UtcNow;
            }
            bool holdMouse = (DateTime.UtcNow - browserRightAt).TotalMilliseconds < 350;
            double speed = CursorSpeeds[Math.Max(1, Math.Min(5, settings.BrowserCursorSpeed))];
            double dx = Curve(lx) * speed, dy = Curve(ly) * speed;
            var host = BrowserHost();
            if (host == null || host.ActualWidth <= 0) return;
            if (dx != 0 || dy != 0)
            {
                cursorPos = new Point(Math.Max(0, Math.Min(host.ActualWidth - 1, cursorPos.X + dx)), Math.Max(0, Math.Min(host.ActualHeight - 1, cursorPos.Y + dy)));
                MoveCursor(true);
            }
            else
            {
                // someone used the real mouse: follow it
                NativePoint p;
                if (GetCursorPos(out p) && (p.X != lastSysCursor.X || p.Y != lastSysCursor.Y) && holdMouse)
                    MoveCursor(true);   // Steam's copy of the right stick: the mouse goes back where it was
                else if (GetCursorPos(out p) && (p.X != lastSysCursor.X || p.Y != lastSysCursor.Y))
                {
                    if (!browserMouseLogged && SteamPadConnected() && ControllerUsedWithin(5))
                    {
                        browserMouseLogged = true;
                        logger.Info("Kippix: browser: the mouse moved with no right stick reported (Steam pad connected)");
                    }
                    lastSysCursor = p;
                    try
                    {
                        var local = host.PointFromScreen(new Point(p.X, p.Y));
                        if (local.X >= 0 && local.Y >= 0 && local.X < host.ActualWidth && local.Y < host.ActualHeight) { cursorPos = local; MoveCursor(false); }
                    }
                    catch (Exception) { }
                }
            }
            // right stick scrolls (wheel units: 120 = one notch)
            scrollCarryY += -Curve(ry) * 45;
            scrollCarryX += Curve(rx) * 45;
            if (Math.Abs(scrollCarryY) >= 1) { int n = (int)scrollCarryY; scrollCarryY -= n; Wheel(n, false); }
            if (Math.Abs(scrollCarryX) >= 1) { int n = (int)scrollCarryX; scrollCarryX -= n; Wheel(n, true); }
            if (ry == 0) scrollCarryY = 0;
            if (rx == 0) scrollCarryX = 0;
        }

        private static double Curve(short v)
        {
            double f = v / 32767.0, dz = 0.2;
            double a = Math.Abs(f);
            if (a < dz) return 0;
            double s = Math.Min(1, (a - dz) / (1 - dz));
            return Math.Sign(f) * s * s;
        }

        private DateTime stickLogAt = DateTime.MinValue;
        private DateTime browserRightAt = DateTime.MinValue;
        private bool browserMouseLogged;
        private string browserStickFrom = "";

        private void ReadSticks(out short lx, out short ly, out short rx, out short ry)
        {
            lx = ly = rx = ry = 0;
            try
            {
                foreach (var c in api.GetConnectedControllers())
                {
                    var g = c as IGamepad;
                    if (g == null || !g.Enabled) continue;
                    var a = g.AnalogInputState;
                    short x, y, x2, y2;
                    if (!a.TryGetValue(ControllerInput.LeftStickX, out x)) x = 0;
                    if (!a.TryGetValue(ControllerInput.LeftStickY, out y)) y = 0;
                    if (!a.TryGetValue(ControllerInput.RightStickX, out x2)) x2 = 0;
                    if (!a.TryGetValue(ControllerInput.RightStickY, out y2)) y2 = 0;
                    if (settings.DebugLogging && (DateTime.UtcNow - stickLogAt).TotalSeconds > 2 && (x != 0 || y != 0 || x2 != 0 || y2 != 0))
                        Dbg("browser sticks: " + g.Name + " L " + x + "," + y + "  R " + x2 + "," + y2);
                    if (Math.Abs((int)x) + Math.Abs((int)y) > Math.Abs((int)lx) + Math.Abs((int)ly)) { lx = x; ly = y; }
                    if (Math.Abs((int)x2) + Math.Abs((int)y2) > Math.Abs((int)rx) + Math.Abs((int)ry)) { rx = x2; ry = y2; browserStickFrom = g.Name; }
                }
            }
            catch (Exception) { }
            if (settings.DebugLogging && (DateTime.UtcNow - stickLogAt).TotalSeconds > 2 && (lx != 0 || ly != 0 || rx != 0 || ry != 0)) stickLogAt = DateTime.UtcNow;
        }

        /// <summary>Is the left stick tilted? Playnite marks the direction pressed before it sends the arrow key it makes from it.</summary>
        private bool LeftStickActive()
        {
            try
            {
                foreach (var c in api.GetConnectedControllers())
                {
                    var g = c as IGamepad;
                    if (g == null || !g.Enabled) continue;
                    var b = g.ButtonInputState;
                    foreach (var d in new[] { ControllerInput.LeftStickUp, ControllerInput.LeftStickDown, ControllerInput.LeftStickLeft, ControllerInput.LeftStickRight })
                    {
                        ControllerInputState st;
                        if (b.TryGetValue(d, out st) && st == ControllerInputState.Pressed) return true;
                    }
                    short x, y;
                    var a = g.AnalogInputState;
                    if (a.TryGetValue(ControllerInput.LeftStickX, out x) && Math.Abs((int)x) > 9000) return true;
                    if (a.TryGetValue(ControllerInput.LeftStickY, out y) && Math.Abs((int)y) > 9000) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Draws our cursor there (and puts Windows' cursor there too, so the page sees hover and clicks).</summary>
        private void MoveCursor(bool moveSystem)
        {
            if (browserCursor == null) browserCursor = UiTools.FindByName(Application.Current.MainWindow, "KX_BrowserCursor");
            var cursor = browserCursor;
            if (cursor != null) { Canvas.SetLeft(cursor, cursorPos.X); Canvas.SetTop(cursor, cursorPos.Y); }
            if (!moveSystem) return;
            var host = BrowserHost();
            if (host == null) return;
            try
            {
                var s = host.PointToScreen(cursorPos);
                SetCursorPos((int)Math.Round(s.X), (int)Math.Round(s.Y));
                GetCursorPos(out lastSysCursor);
            }
            catch (Exception) { }
        }

        /// <summary>Controller buttons in the browser. Keyboard keys go to the page, except the arrows Playnite makes from the left stick.</summary>
        private void BrowserKey(KeyEventArgs e)
        {
            if (kbOpen || ReferenceEquals(e, padHandled)) return;   // the theme keyboard is up, or the press was dealt with already
            try
            {
                if (openPanel != "browser" || e.GetType().Name != "GameControllerInputEventArgs") return;
                PropertyInfo button = e.GetType().GetProperty("Button");
                PropertyInfo state = e.GetType().GetProperty("ButtonState");
                string name = button != null ? Convert.ToString(button.GetValue(e, null)) : "";
                object st = state != null ? state.GetValue(e, null) : null;
                bool pressed = st == null || st.ToString() == "Pressed";
                switch (name)
                {
                    case "A": MouseButton(pressed); break;
                    case "B": if (pressed) BrowserBackOrClose(); break;
                    case "X": if (pressed) { PlaySound("select"); BrowserCommand("ReloadCommand"); } break;
                    case "Y": if (pressed) ui.BeginInvoke(new Action(AskBrowserText), DispatcherPriority.Background); break;
                    case "LeftShoulder": if (pressed) Zoom(-1); break;
                    case "RightShoulder": if (pressed) Zoom(1); break;
                    case "Back":
                    case "Start": if (pressed) CloseBrowser(); break;
                    default: return;   // D-pad, sticks, triggers: their arrow / page keys reach the page
                }
                e.Handled = true;
                lastPanelKey = e;   // the window's handler sees this event after us (and after a close): it must not act on it again
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: browser key failed"); }
        }

        private void BrowserKeyFromWindow(KeyEventArgs e)
        {
            if (browser == null) return;
            BrowserKey(e);
            if (openPanel == "browser" && browser != null && !browser.IsKeyboardFocusWithin) Keyboard.Focus(browser);
        }

        /// <summary>Window PreviewKeyDown while the browser is open: true when the key must not reach the page.</summary>
        private bool BrowserPreviewKey(KeyEventArgs e)
        {
            bool arrow = e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down;
            if (arrow && LeftStickActive()) { e.Handled = true; return true; }   // that's the cursor moving, not arrow keys
            if (browser != null && !browser.IsKeyboardFocusWithin && e.GetType().Name != "GameControllerInputEventArgs") Keyboard.Focus(browser);
            return false;
        }

        /// <summary>Y: type with Playnite's on-screen keyboard; the text goes into whatever field is selected on the page.</summary>
        private void AskBrowserText()
        {
            if (openPanel != "browser" || browser == null) return;
            if (cursorDown) MouseButton(false);
            if (browserTick != null) browserTick.Stop();   // the sticks drive the keyboard now, not the cursor
            Action back = () =>
            {
                if (browser == null || openPanel != "browser") return;
                GetCursorPos(out lastSysCursor);
                if (browserTick != null) browserTick.Start();
                Keyboard.Focus(browser);
            };
            // the theme's keyboard over the bottom of the page; Done types it into the box that was selected
            OpenKeyboard(Loc("LOC_KX_BrowserType", "Type into the selected box on the page"), "", text =>
            {
                back();
                if (browser == null || openPanel != "browser" || string.IsNullOrEmpty(text)) return;
                ui.BeginInvoke(new Action(() =>
                {
                    if (browser == null) return;
                    Keyboard.Focus(browser);
                    TypeText(text);
                }), DispatcherPriority.ContextIdle);
            }, null, back, 500);
        }

        // ---------------- Windows input ----------------

        private const int InputMouse = 0, InputKeyboard = 1;
        private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseWheel = 0x0800, MouseHWheel = 0x01000;
        private const uint KeyUnicode = 0x0004, KeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public IntPtr Extra; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeybdInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeybdInput Key; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput { public int Type; public InputUnion U; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint p);
        [DllImport("user32.dll")] private static extern int ShowCursor(bool show);

        private static void Send(params NativeInput[] inputs)
        {
            try { SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeInput))); } catch (Exception) { }
        }

        private void MouseButton(bool down)
        {
            if (down == cursorDown) return;
            if (down) MoveCursor(true);
            cursorDown = down;
            var i = new NativeInput { Type = InputMouse };
            i.U.Mouse.Flags = down ? MouseLeftDown : MouseLeftUp;
            Send(i);
        }

        private static void Wheel(int amount, bool horizontal)
        {
            var i = new NativeInput { Type = InputMouse };
            i.U.Mouse.Flags = horizontal ? MouseHWheel : MouseWheel;
            i.U.Mouse.MouseData = unchecked((uint)amount);
            Send(i);
        }

        private static void TypeText(string text)
        {
            foreach (char c in text)
            {
                var down = new NativeInput { Type = InputKeyboard };
                down.U.Key.Scan = c;
                down.U.Key.Flags = KeyUnicode;
                var up = down;
                up.U.Key.Flags = KeyUnicode | KeyUp;
                Send(down, up);
            }
        }
    }

    /// <summary>
    /// A CefSharp ILifeSpanHandler made at run time (so the add-on isn't tied to one CefSharp version): OnBeforePopup
    /// sends the pop-up's address to <see cref="Open"/> and cancels the separate window; every other method does the default.
    /// </summary>
    public static class BrowserPopups
    {
        public static Action<string> Open;

        /// <summary>Called by the generated handler (on Chromium's thread).</summary>
        public static void Popup(string url)
        {
            var open = Open;
            if (open != null) open(url);
        }

        private static readonly System.Collections.Generic.Dictionary<Type, Type> made = new System.Collections.Generic.Dictionary<Type, Type>();

        public static object Create(Type iface)
        {
            if (iface == null || !iface.IsInterface) return null;
            Type t;
            lock (made)
            {
                if (!made.TryGetValue(iface, out t))
                {
                    t = Build(iface);
                    made[iface] = t;
                }
            }
            return Activator.CreateInstance(t);
        }

        private static Type Build(Type iface)
        {
            var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("KippixPopups"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var mod = asm.DefineDynamicModule("KippixPopups");
            var tb = mod.DefineType("KippixPopups.LifeSpan", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
            tb.AddInterfaceImplementation(iface);
            var popup = typeof(BrowserPopups).GetMethod("Popup");
            foreach (var m in iface.GetMethods())
            {
                var ps = m.GetParameters();
                var mb = tb.DefineMethod(m.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                                         m.ReturnType, ps.Select(p => p.ParameterType).ToArray());
                var il = mb.GetILGenerator();
                // out parameters must be set: null / default
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!ps[i].IsOut || !ps[i].ParameterType.IsByRef) continue;
                    var el = ps[i].ParameterType.GetElementType();
                    il.Emit(System.Reflection.Emit.OpCodes.Ldarg, (short)(i + 1));
                    if (el.IsValueType) il.Emit(System.Reflection.Emit.OpCodes.Initobj, el);
                    else { il.Emit(System.Reflection.Emit.OpCodes.Ldnull); il.Emit(System.Reflection.Emit.OpCodes.Stind_Ref); }
                }
                bool isPopup = m.Name == "OnBeforePopup";
                if (isPopup)
                {
                    int url = Array.FindIndex(ps, p => p.Name == "targetUrl" && p.ParameterType == typeof(string));
                    if (url < 0) url = Array.FindIndex(ps, p => p.ParameterType == typeof(string));
                    if (url >= 0) { il.Emit(System.Reflection.Emit.OpCodes.Ldarg, (short)(url + 1)); il.Emit(System.Reflection.Emit.OpCodes.Call, popup); }
                }
                if (m.ReturnType == typeof(bool)) il.Emit(isPopup ? System.Reflection.Emit.OpCodes.Ldc_I4_1 : System.Reflection.Emit.OpCodes.Ldc_I4_0);   // true = no pop-up window
                else if (m.ReturnType != typeof(void))
                {
                    var local = il.DeclareLocal(m.ReturnType);
                    if (m.ReturnType.IsValueType) { il.Emit(System.Reflection.Emit.OpCodes.Ldloca, local); il.Emit(System.Reflection.Emit.OpCodes.Initobj, m.ReturnType); }
                    else { il.Emit(System.Reflection.Emit.OpCodes.Ldnull); il.Emit(System.Reflection.Emit.OpCodes.Stloc, local); }
                    il.Emit(System.Reflection.Emit.OpCodes.Ldloc, local);
                }
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                tb.DefineMethodOverride(mb, m);
            }
            return tb.CreateType();
        }
    }
}
