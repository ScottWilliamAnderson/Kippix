using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Events;

namespace KippixLinks
{
    /// <summary>Open: open ResultItem. List: show the full list, on ResultItem (asked from the "Opening" card).</summary>
    internal enum PanelResult { None, Open, List }

    /// <summary>Switches for the offline tools in tests\ (never set inside Playnite).</summary>
    public static class TestHooks
    {
        /// <summary>tests\preview.ps1: the panel is drawn without looking at the clipboard.</summary>
        public static bool NoClipboard;
    }

    /// <summary>
    /// The Links panel: its own top-level window over the right side of Playnite's, like Kippix's options panel.
    /// It is where the saved links are shown, so it is kept out of screenshots and recordings (display affinity), is never
    /// a layered window, is tagged "kx-private", and shows nothing to UI Automation (see PrivatePeers below).
    /// Rename happens in here too, with its own on-screen keyboard (Kippix's keyboard is drawn in Playnite's main window,
    /// which can be captured, and keeps the text after it closes, so a name never goes there).
    ///
    /// Coexistence with Kippix (checked in its 1.0.5 source): its early controller handler (ThemeApi.PadInput.cs,
    /// a class handler on every UIElement) returns at once for any window that isn't Playnite's main one, after only its
    /// L3+R3 failsafe and the hold-B timer's release; it doesn't mark our presses handled. Its message-box helper
    /// (ThemeApi.AddOnsPrompt.cs) only takes the focus for Playnite's own MessageBoxWindow type, never for a foreign
    /// window. So nothing fights this window for focus or eats its pad events. Side effects that come with any separate
    /// window and are left as they are: Kippix shows the mouse pointer for non-main windows (it is hidden over the panel
    /// until the mouse really moves), and its music and home video pause while the main window isn't the active one.
    /// When something else is activated (a tap on the main window, an overlay, Alt+Tab) the panel closes, so it is
    /// never left on screen without the controller reaching it.
    /// </summary>
    internal class LinksPanel : Window
    {
        private enum RowKind { Add, Link, ForgetAll, Keep, ConfirmForget, Key, Direct, SiteIcon }
        private enum KeyKind { Char, Shift, Space, Delete, Done }

        private class RowInfo
        {
            public RowKind Kind;
            public LinkItem Item;
            public TextBlock Detail;
            // keys of the rename keyboard
            public KeyKind Key;
            public char Ch;
            public int Row, Col, Span;
            public TextBlock Label;
        }

        private const int NameMax = 60;   // (LinkStore.SetName clips to the same)

        private readonly LinkStore store;
        private readonly KippixSeam seam;
        private readonly LinkItem focusFirst;
        private readonly PanelSettings settings;   // (null in the offline preview)
        // The "Opening" card: the top bar button pressed with a link to open. It shows which one for a moment and then opens
        // it; the confirm button in that moment shows the full list instead, the cancel button stops it.
        private readonly bool quick;
        private DispatcherTimer quickTimer;
        private const int QuickMs = 1100;
        private const double QuickHeight = 236;    // (logical units: the title, the link's two lines, the hint)
        private readonly ControllerInput confirmButton;
        private readonly ControllerInput cancelButton;

        private Brush panelBrush, textBrush, subBrush, dimBrush, focusFill, focusRing;
        private Thickness ringThickness = new Thickness(1.5);
        private ContentControl body;
        private TextBlock hint;
        private readonly List<Button> rows = new List<Button>();
        private Button addRow;
        private bool confirming;
        private bool closing;
        /// <summary>Display affinity couldn't be set: names, titles and sites are not shown at all.</summary>
        private bool masked;

        // rename (in this window only)
        private LinkItem renaming;
        private StringBuilder nameText;
        private TextBlock nameView;
        private bool shift;
        private bool fresh;   // rename: the old name is still showing; the first key typed (or Delete) replaces all of it
        private Point? mouseStart;
        private readonly System.Diagnostics.Stopwatch shownAt = new System.Diagnostics.Stopwatch();
        private bool reclaimed;

        public PanelResult Result { get; private set; }
        public LinkItem ResultItem { get; private set; }
        /// <summary>It closed because another window was activated (so the main window isn't pulled to the front).</summary>
        public bool ClosedByDeactivation { get; private set; }

        public LinksPanel(LinkStore store, KippixSeam seam, Window main, LinkItem focusFirst, PanelSettings settings = null, bool quick = false)
        {
            this.store = store;
            this.seam = seam;
            this.focusFirst = focusFirst;
            this.settings = settings;
            this.quick = quick && focusFirst != null;
            confirmButton = PlayniteBinding("ConfirmationBinding", ControllerInput.A);
            cancelButton = PlayniteBinding("CancellationBinding", ControllerInput.B);

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = true;
            Tag = "kx-private";
            Title = "Links";
            // (P6: no AllowsTransparency: a layered window can defeat the display affinity)
            if (main != null && main.IsLoaded) { try { Owner = main; } catch (Exception) { } }
            WindowStartupLocation = WindowStartupLocation.Manual;

            panelBrush = Opaque(Res("KX_PanelBrush", "#F0141820"));
            textBrush = Res("KX_TextBrush", "#FFF4F6FA");
            subBrush = Res("KX_TextSubBrush", "#CCE4E8EF");
            dimBrush = Res("KX_TextDimBrush", "#8CE4E8EF");
            focusFill = Res("KX_FocusFillBrush", "#2EFFFFFF");
            focusRing = Res("KX_FocusRingBrush", "#E9FFFFFF");
            try
            {
                object t = Application.Current != null ? Application.Current.TryFindResource("KX_FocusRingThickness") : null;
                if (t is Thickness) ringThickness = (Thickness)t;
                else if (t is double) ringThickness = new Thickness((double)t);
            }
            catch (Exception) { }
            Background = panelBrush;
            FontFamily theFont = null;
            try { theFont = Application.Current != null ? Application.Current.TryFindResource("FontTitilliumWeb") as FontFamily : null; }
            catch (Exception) { theFont = null; }
            if (theFont != null) FontFamily = theFont;
            else if (main != null) FontFamily = main.FontFamily;
            // Kippix shows the pointer for every window but its main one: keep it hidden here until the mouse really moves
            Cursor = Cursors.None;

            double logicalHeight;
            double k = Place(main, out logicalHeight);
            if (this.quick) { logicalHeight = QuickHeight; Height = QuickHeight * k; }   // a card under the top bar, not the whole side
            Content = BuildFrame(k, logicalHeight);
            if (this.quick) ShowQuick(); else ShowList();
            // (a tap on the card is the same as the confirm button: the list)
            PreviewMouseLeftButtonDown += (s, e) => Guard(() => { if (this.quick && !closing) { e.Handled = true; Do(Act.Confirm); } });

            // typing a name: no IME or suggestion windows (they belong to other programs and would show the text outside this window)
            InputMethod.SetIsInputMethodEnabled(this, false);
            InputMethod.SetPreferredImeState(this, InputMethodState.Off);
            shownAt.Start();   // (from here, not from Loaded: a deactivation right after Show is looked at before Loaded runs)

            SourceInitialized += OnSourceInitialized;
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
            PreviewTextInput += OnPreviewTextInput;
            PreviewMouseMove += OnPreviewMouseMove;
            AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnPadDown), true);
            AddHandler(Keyboard.KeyUpEvent, new KeyEventHandler(OnPadUp), true);
            Loaded += (s, e) => Guard(() =>
            {
                shownAt.Start(); Activate(); FocusInitial();
                if (this.quick)
                {
                    quickTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(QuickMs) };
                    quickTimer.Tick += (s2, e2) => Guard(() => { StopQuick(); if (!closing) CloseWith(PanelResult.Open, this.focusFirst); });
                    quickTimer.Start();
                }
            });
            Deactivated += (s, e) => Guard(() =>
            {
                if (closing) return;
                // (closed just after the event, not inside it)
                Dispatcher.BeginInvoke(new Action(() => Guard(() =>
                {
                    if (closing || IsActive) return;
                    // just opened, and the activation went back to Playnite's own window (a menu closing behind us): take it back once
                    var owner = Owner;
                    if (!reclaimed && shownAt.IsRunning && shownAt.ElapsedMilliseconds < 1500 && owner != null && owner.IsActive)
                    {
                        reclaimed = true;
                        Activate();
                        return;
                    }
                    ClosedByDeactivation = true;
                    CloseWith(PanelResult.None, null);
                })), DispatcherPriority.Normal);
            });
            Closing += (s, e) => { closing = true; };
        }

        private static void Guard(Action a)
        {
            try { a(); } catch (Exception ex) { SafeLog.Warn("panel: handler failed", ex); }
        }

        /// <summary>Opened again while it's up: back to the front, with a row focused.</summary>
        public void Bring()
        {
            Guard(() =>
            {
                if (closing) return;
                if (!IsActive) Activate();
                if (Focused() == null)
                {
                    if (renaming != null || confirming) FocusLater(rows.FirstOrDefault());
                    else FocusInitial();
                }
            });
        }

        // ---------------- UI Automation: nothing in here is readable from outside ----------------

        // Display affinity keeps the panel out of captures, but WPF would still describe every TextBlock (names, titles, sites)
        // to any UI Automation client. These peers show the window and its rows with fixed names and no children. This gives
        // up screen-reader access to the list on purpose.
        protected override AutomationPeer OnCreateAutomationPeer() { return new PrivateWindowPeer(this); }

        private sealed class PrivateWindowPeer : WindowAutomationPeer
        {
            public PrivateWindowPeer(Window w) : base(w) { }
            protected override List<AutomationPeer> GetChildrenCore() { return null; }
            protected override string GetNameCore() { return "Links"; }
        }

        private sealed class PrivateButton : Button
        {
            protected override AutomationPeer OnCreateAutomationPeer() { return new PrivateButtonPeer(this); }
        }

        private sealed class PrivateButtonPeer : FrameworkElementAutomationPeer
        {
            public PrivateButtonPeer(Button b) : base(b) { }
            protected override List<AutomationPeer> GetChildrenCore() { return null; }
            protected override string GetNameCore() { return "Item"; }
        }

        // ---------------- look and placement ----------------

        private static Brush Res(string key, string fallback)
        {
            try
            {
                object r = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                var b = r as Brush;
                if (b != null) return b;
                if (r is Color) { var cb = new SolidColorBrush((Color)r); cb.Freeze(); return cb; }
            }
            catch (Exception) { }
            var f = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            f.Freeze();
            return f;
        }

        /// <summary>The panel itself is drawn fully opaque (a plain window has nothing behind it to blend with).</summary>
        private static Brush Opaque(Brush b)
        {
            try
            {
                var sc = b as SolidColorBrush;
                if (sc != null)
                {
                    if (sc.Color.A == 255 && sc.Opacity >= 1) return b;
                    var c = sc.Color;
                    var o = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                    o.Freeze();
                    return o;
                }
                var gb = b as GradientBrush;   // (Kippix's KX_PanelBrush is a gradient)
                if (gb != null)
                {
                    var g = gb.Clone();
                    g.Opacity = 1;
                    foreach (var st in g.GradientStops) st.Color = Color.FromRgb(st.Color.R, st.Color.G, st.Color.B);
                    if (g.CanFreeze) g.Freeze();
                    return g;
                }
            }
            catch (Exception) { }
            return b;
        }

        /// <summary>
        /// Over the right side of the main window (sizes in DIPs). k = H / 1080, except under Kippix on a screen narrower than
        /// 16:9, where Kippix lays its theme out 1920 units across (ThemeApi.DesignSize.cs), so k = W / 1920 like its panels.
        /// </summary>
        private double Place(Window main, out double logicalHeight)
        {
            Rect r = Rect.Empty;
            try
            {
                if (main != null && main.IsVisible && main.WindowState != WindowState.Minimized)
                {
                    var src = PresentationSource.FromVisual(main);
                    if (src != null && src.CompositionTarget != null)
                    {
                        // the client area on screen, in device pixels, then in DIPs (works for a maximised, borderless window too)
                        var root = main.Content as FrameworkElement;
                        Visual v = root != null && root.IsVisible && root.ActualWidth > 0 ? (Visual)root : main;
                        double w = v is FrameworkElement ? ((FrameworkElement)v).ActualWidth : main.ActualWidth;
                        double h = v is FrameworkElement ? ((FrameworkElement)v).ActualHeight : main.ActualHeight;
                        var fromDevice = src.CompositionTarget.TransformFromDevice;
                        Point a = fromDevice.Transform(v.PointToScreen(new Point(0, 0)));
                        Point b = fromDevice.Transform(v.PointToScreen(new Point(w, h)));
                        r = new Rect(a, b);
                    }
                }
            }
            catch (Exception) { r = Rect.Empty; }
            if (r.IsEmpty || r.Width < 200 || r.Height < 200) r = SystemParameters.WorkArea;
            double k = r.Height / 1080.0;
            bool kippixTheme = false;
            try { kippixTheme = Application.Current != null && Application.Current.TryFindResource("KX_PanelBrush") != null; }
            catch (Exception) { }
            if (kippixTheme) k = Math.Min(k, r.Width / 1920.0);
            logicalHeight = r.Height / k - (96 + 40);
            Width = 620 * k;
            Height = r.Height - (96 + 40) * k;
            Left = r.Right - (40 + 620) * k;
            Top = r.Top + 96 * k;
            return k;
        }

        private FrameworkElement BuildFrame(double k, double logicalHeight)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = "Links", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = textBrush, Margin = new Thickness(4, 0, 0, 18) };
            grid.Children.Add(title);
            body = new ContentControl { Focusable = false, IsTabStop = false };
            Grid.SetRow(body, 1);
            grid.Children.Add(body);
            hint = new TextBlock { FontSize = 15, Foreground = dimBrush, Margin = new Thickness(4, 16, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(hint, 2);
            grid.Children.Add(hint);
            // 620 x (944 on 16:9) logical units; the window's own background is the panel colour (painted once)
            var frame = new Border { Width = 620, Height = Math.Max(quick ? 160 : 300, logicalHeight), Padding = new Thickness(28), Child = grid };
            var outer = new Border { Child = frame, LayoutTransform = new ScaleTransform(k, k) };
            KeyboardNavigation.SetTabNavigation(outer, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(outer, KeyboardNavigationMode.None);
            return outer;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                // P6: never in a screenshot, recording or stream (Windows 10 2004+: excluded; older: shown black)
                bool ok = false;
                try { ok = NativeMethods.SetWindowDisplayAffinity(h, 0x11); } catch (Exception) { ok = false; }
                if (!ok)
                {
                    try { ok = NativeMethods.SetWindowDisplayAffinity(h, 0x1); } catch (Exception) { ok = false; }
                    if (!ok)
                    {
                        // it could be captured: show no name, title or site at all (fixed text only), and no rename
                        SafeLog.Info("panel: display affinity not set, names hidden");
                        masked = true;
                        if (quick) ShowQuick(); else ShowList();
                    }
                }
                // rounded corners on Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE = round); older Windows just ignores it
                try { int round = 2; NativeMethods.DwmSetWindowAttribute(h, 33, ref round, sizeof(int)); } catch (Exception) { }
            }
            catch (Exception ex) { masked = true; SafeLog.Warn("panel: source initialized", ex); try { if (quick) ShowQuick(); else ShowList(); } catch (Exception) { } }
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (Cursor != Cursors.None) return;
                var p = e.GetPosition(this);
                if (mouseStart == null) { mouseStart = p; return; }
                if (Math.Abs(p.X - mouseStart.Value.X) + Math.Abs(p.Y - mouseStart.Value.Y) > 4) ClearValue(CursorProperty);
            }
            catch (Exception) { }
        }

        // ---------------- rows ----------------

        private ControlTemplate rowTemplate, keyTemplate;

        private ControlTemplate MakeTemplate(Thickness padding, double radius, bool center)
        {
            var t = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.PaddingProperty, padding);
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderThicknessProperty, ringThickness);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            t.VisualTree = bd;
            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BackgroundProperty, focusFill, "Bd"));
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, focusRing, "Bd"));
            t.Triggers.Add(focused);
            t.Seal();
            return t;
        }

        private ControlTemplate RowTemplate()
        {
            if (rowTemplate == null) rowTemplate = MakeTemplate(new Thickness(18, 14, 18, 14), 14, false);
            return rowTemplate;
        }

        private ControlTemplate KeyTemplate()
        {
            if (keyTemplate == null) keyTemplate = MakeTemplate(new Thickness(0, 10, 0, 10), 12, true);
            return keyTemplate;
        }

        private Button NewButton(ControlTemplate template, object content, RowInfo info, Thickness margin)
        {
            var b = new PrivateButton
            {
                Template = template, Content = content, FocusVisualStyle = null, Margin = margin,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = info
            };
            b.Click += (s, e) => Guard(() => ActivateRow(b));   // mouse / touch
            b.GotKeyboardFocus += (s, e) => Guard(() => b.BringIntoView());
            rows.Add(b);
            return b;
        }

        private Button MakeRow(RowKind kind, LinkItem item, string primary, string detail)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = primary, FontSize = 22, Foreground = textBrush, TextTrimming = TextTrimming.CharacterEllipsis });
            TextBlock d = null;
            if (detail != null)
            {
                d = new TextBlock { Text = detail, FontSize = 15, Foreground = subBrush, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                stack.Children.Add(d);
            }
            return NewButton(RowTemplate(), stack, new RowInfo { Kind = kind, Item = item, Detail = d }, new Thickness(0, 0, 0, 4));
        }

        private void ShowList()
        {
            confirming = false;
            EndRename();
            rows.Clear();
            var list = new StackPanel();
            if (masked)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "Names are hidden: this screen can't be kept out of screenshots here.", FontSize = 15, Foreground = dimBrush,
                    Margin = new Thickness(4, 0, 0, 10), TextWrapping = TextWrapping.Wrap
                });
            }
            addRow = MakeRow(RowKind.Add, null, "Add copied link", "");
            list.Children.Add(addRow);
            string status = null;
            if (store.Unavailable) status = "The saved list couldn't be read: restart Playnite to try again.";
            else if (store.SaveFailed) status = "Changes couldn't be saved.";
            if (status != null)
            {
                list.Children.Add(new TextBlock
                {
                    Text = status, FontSize = 15, Foreground = subBrush, Margin = new Thickness(18, 2, 18, 8), TextWrapping = TextWrapping.Wrap
                });
            }
            var items = store.Items;
            if (items.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "Copy a page's address in your browser,\nthen choose Add copied link.", FontSize = 17, Foreground = dimBrush,
                    Margin = new Thickness(18, 14, 18, 0), TextWrapping = TextWrapping.Wrap
                });
            }
            int n = 0;
            foreach (var it in items)
            {
                n++;
                if (masked) list.Children.Add(MakeRow(RowKind.Link, it, "Link " + n, it.When));
                else list.Children.Add(MakeRow(RowKind.Link, it, it.Display, it.Detail));
            }
            if (items.Count > 0)
            {
                var forget = MakeRow(RowKind.ForgetAll, null, "Forget all links", null);
                forget.Margin = new Thickness(0, 14, 0, 4);
                list.Children.Add(forget);
            }
            if (settings != null)
            {
                list.Children.Add(MakeRow(RowKind.Direct, null, "Top bar button", DirectText()));
                list.Children.Add(MakeRow(RowKind.SiteIcon, null, "Button icon", SiteIconText()));
            }
            body.Content = new ScrollViewer
            {
                Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false, IsTabStop = false
            };
            UpdateAddDetail();
            SetHint();
        }

        private string DirectText()
        {
            return settings != null && settings.Direct
                ? "Opens the latest link (press again at once for this list)"
                : "Shows this list";
        }

        private string SiteIconText()
        {
            return settings != null && settings.SiteIcon
                ? "Shows the latest link's site (the top bar can be seen in screenshots)"
                : "Plain link icon";
        }

        /// <summary>The "Opening" card: the link about to open (its name, or "Link 1" when names are hidden) and what the buttons do.</summary>
        private void ShowQuick()
        {
            confirming = false;
            rows.Clear();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Opening", FontSize = 15, Foreground = dimBrush, Margin = new Thickness(4, 0, 0, 4) });
            stack.Children.Add(new TextBlock
            {
                Text = masked ? "Link 1" : focusFirst.Display, FontSize = 22, Foreground = textBrush, Margin = new Thickness(4, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            body.Content = stack;
            SetHint();
        }

        private void StopQuick()
        {
            if (quickTimer != null) { quickTimer.Stop(); quickTimer = null; }
        }

        private void ShowConfirm()
        {
            confirming = true;
            rows.Clear();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Forget all links?", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = textBrush, Margin = new Thickness(4, 0, 0, 6) });
            stack.Children.Add(new TextBlock { Text = "This clears the list. It can't be undone.", FontSize = 17, Foreground = subBrush, Margin = new Thickness(4, 0, 0, 18), TextWrapping = TextWrapping.Wrap });
            var keep = MakeRow(RowKind.Keep, null, "Keep them", null);
            stack.Children.Add(keep);
            stack.Children.Add(MakeRow(RowKind.ConfirmForget, null, "Forget all", null));
            body.Content = stack;
            SetHint();
            FocusLater(keep);
        }

        private bool CanRename { get { return !masked; } }

        private void SetHint()
        {
            bool swapped = confirmButton == ControllerInput.B;   // Playnite's "confirm with B" setting
            string ok = swapped ? "B" : "A", back = swapped ? "A" : "B";
            if (quick) hint.Text = ok + " All links   " + back + " Cancel";
            else if (renaming != null) hint.Text = ok + " Type   X Delete   Y Done   " + back + " Cancel";
            else if (confirming) hint.Text = ok + " Choose   " + back + " Back";
            else hint.Text = ok + " Open   X Forget   " + (CanRename ? "Y Rename   " : "") + back + " Back";
        }

        /// <summary>The Add row says what's on the clipboard (only whether it's a new address and its site: never the address).</summary>
        private void UpdateAddDetail()
        {
            if (addRow == null) return;
            var info = (RowInfo)addRow.Tag;
            if (info.Detail == null) return;
            string url;
            if (LinkUrl.TryParse(ReadClipboard(), out url))
                info.Detail.Text = store.Find(url) != null ? "Already saved: choose it below" : (masked ? "A new link is ready to add" : LinkUrl.Site(url) + " link ready to add");
            else info.Detail.Text = "Copy a page's address in your browser first";
            url = null;
        }

        private bool ClipboardHasNew()
        {
            return ClipboardHoldsNew(store);
        }

        /// <summary>The clipboard holds a web address that isn't in the list yet (UI thread; nothing of it is kept).</summary>
        internal static bool ClipboardHoldsNew(LinkStore store)
        {
            string url;
            return LinkUrl.TryParse(ReadClipboard(), out url) && store.Find(url) == null;
        }

        /// <summary>
        /// The clipboard's text (UI thread). One try: WPF already retries for about a second when another program has the
        /// clipboard open, and more than that would freeze the screen.
        /// </summary>
        private static string ReadClipboard()
        {
            if (TestHooks.NoClipboard) return null;
            try
            {
                if (!Clipboard.ContainsText()) return null;
                return Clipboard.GetText();
            }
            catch (Exception) { return null; }
        }

        // ---------------- rename, with a keyboard in this window ----------------

        private static readonly string[] KeyRows = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-" };

        private void ShowRename(LinkItem item)
        {
            confirming = false;
            rows.Clear();
            renaming = item;
            string start = item.Display ?? "";
            if (start.Length > NameMax) start = start.Substring(0, NameMax);
            nameText = new StringBuilder(start, NameMax + 8);
            shift = false;
            fresh = start.Length > 0;

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Name this link", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = textBrush, Margin = new Thickness(4, 0, 0, 12) });
            nameView = new TextBlock { FontSize = 22, Foreground = textBrush, TextTrimming = TextTrimming.None, TextWrapping = TextWrapping.NoWrap };
            var field = new Border
            {
                CornerRadius = new CornerRadius(14), Background = focusFill, Padding = new Thickness(18, 12, 18, 12),
                Margin = new Thickness(0, 0, 0, 16), Child = nameView, ClipToBounds = true
            };
            stack.Children.Add(field);

            var grid = new Grid();
            for (int c = 0; c < 10; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int r = 0; r < KeyRows.Length + 1; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int r = 0; r < KeyRows.Length; r++)
                for (int c = 0; c < KeyRows[r].Length; c++)
                    AddKey(grid, KeyKind.Char, KeyRows[r][c], KeyRows[r][c].ToString(), r, c, 1);
            int last = KeyRows.Length;
            AddKey(grid, KeyKind.Shift, '\0', "Shift", last, 0, 2);
            AddKey(grid, KeyKind.Space, ' ', "Space", last, 2, 4);
            AddKey(grid, KeyKind.Delete, '\0', "Delete", last, 6, 2);
            AddKey(grid, KeyKind.Done, '\0', "Done", last, 8, 2);
            stack.Children.Add(grid);

            body.Content = stack;
            ShowName();
            SetHint();
            // on the first letter, not on Done: a stray A must not finish the rename (and the next one open the link)
            FocusLater(rows.FirstOrDefault(b => ((RowInfo)b.Tag).Key == KeyKind.Char && ((RowInfo)b.Tag).Row == 1 && ((RowInfo)b.Tag).Col == 0) ?? rows.FirstOrDefault());
        }

        private void AddKey(Grid grid, KeyKind kind, char ch, string label, int row, int col, int span)
        {
            var tb = new TextBlock { Text = label, FontSize = kind == KeyKind.Char ? 22 : 17, Foreground = textBrush, HorizontalAlignment = HorizontalAlignment.Center };
            var info = new RowInfo { Kind = RowKind.Key, Key = kind, Ch = ch, Row = row, Col = col, Span = span, Label = tb };
            var b = NewButton(KeyTemplate(), tb, info, new Thickness(2));
            Grid.SetRow(b, row);
            Grid.SetColumn(b, col);
            Grid.SetColumnSpan(b, span);
            grid.Children.Add(b);
        }

        private void ShowName()
        {
            if (nameView == null || nameText == null) return;
            string s = nameText.ToString();
            if (s.Length > 34) s = "…" + s.Substring(s.Length - 33);   // the end, where the typing is
            nameView.Inlines.Clear();
            if (fresh) nameView.Inlines.Add(new Run(s) { Foreground = subBrush });   // dimmer: about to be replaced
            else nameView.Inlines.Add(new Run(s));
            nameView.Inlines.Add(new Run("|") { Foreground = subBrush });
            foreach (var b in rows)
            {
                var info = (RowInfo)b.Tag;
                if (info.Kind == RowKind.Key && info.Key == KeyKind.Char && info.Label != null)
                    info.Label.Text = (shift ? char.ToUpperInvariant(info.Ch) : info.Ch).ToString();
                if (info.Kind == RowKind.Key && info.Key == KeyKind.Shift && info.Label != null)
                    info.Label.FontWeight = shift ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private void TypeChar(char c)
        {
            if (renaming == null || nameText == null) return;
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) return;
            if (char.IsSurrogate(c)) return;   // (no half pairs at the length cap: names are plain text)
            if (fresh) { nameText.Length = 0; fresh = false; }   // typing over the old name
            if (nameText.Length >= NameMax) { Sound("back"); return; }
            nameText.Append(c);
            ShowName();
        }

        private void Backspace()
        {
            if (renaming == null || nameText == null) return;
            if (fresh) { nameText.Length = 0; fresh = false; ShowName(); return; }   // the first Delete clears the old name
            if (nameText.Length == 0) return;
            int cut = 1;
            if (nameText.Length >= 2 && char.IsLowSurrogate(nameText[nameText.Length - 1]) && char.IsHighSurrogate(nameText[nameText.Length - 2])) cut = 2;
            nameText.Length -= cut;
            ShowName();
        }

        private void PressKey(RowInfo info)
        {
            switch (info.Key)
            {
                case KeyKind.Char:
                    TypeChar(shift ? char.ToUpperInvariant(info.Ch) : info.Ch);
                    if (shift) { shift = false; ShowName(); }
                    break;
                case KeyKind.Shift: shift = !shift; ShowName(); break;
                case KeyKind.Space: TypeChar(' '); break;
                case KeyKind.Delete: Backspace(); break;
                case KeyKind.Done: FinishRename(true); break;
            }
        }

        private void FinishRename(bool save)
        {
            var item = renaming;
            if (item == null) return;
            if (save && !fresh && store.Contains(item))   // (Done with the old name untouched changes nothing)
            {
                store.SetName(item, nameText != null ? nameText.ToString() : null);
                SafeLog.Info("link renamed", item.Id);
                Sound("select");
            }
            else Sound("back");
            ShowList();
            FocusLater(RowOf(item) ?? addRow);
        }

        /// <summary>Drops the typed text (it's only kept while the keyboard is up).</summary>
        private void EndRename()
        {
            renaming = null;
            if (nameText != null) { nameText.Length = 0; nameText = null; }
            if (nameView != null) { nameView.Inlines.Clear(); nameView = null; }
            shift = false;
            fresh = false;
        }

        private void MoveKey(int dRow, int dCol)
        {
            var f = Focused();
            if (f == null) { FocusLater(rows.FirstOrDefault()); return; }
            var cur = (RowInfo)f.Tag;
            Button want = null;
            if (dRow != 0)
            {
                int r = cur.Row + dRow;
                int c = cur.Col + cur.Span / 2;
                want = rows.FirstOrDefault(b => { var i = (RowInfo)b.Tag; return i.Row == r && i.Col <= c && c < i.Col + i.Span; });
            }
            else if (dCol > 0) want = rows.FirstOrDefault(b => { var i = (RowInfo)b.Tag; return i.Row == cur.Row && i.Col == cur.Col + cur.Span; });
            else want = rows.FirstOrDefault(b => { var i = (RowInfo)b.Tag; return i.Row == cur.Row && i.Col + i.Span == cur.Col; });
            if (want == null) return;   // (clamped at the edges)
            Keyboard.Focus(want);
            Sound("move");
        }

        // ---------------- focus ----------------

        private void FocusInitial()
        {
            if (quick)
            {
                // no rows on the card: the window itself takes the buttons
                Focusable = true;
                Keyboard.Focus(this);
                return;
            }
            Button want = null;
            if (focusFirst != null) want = RowOf(focusFirst);
            if (want == null)
            {
                bool anyLinks = rows.Any(r => ((RowInfo)r.Tag).Kind == RowKind.Link);
                if (!anyLinks || ClipboardHasNew()) want = addRow;
                else want = rows.FirstOrDefault(r => ((RowInfo)r.Tag).Kind == RowKind.Link);
            }
            FocusLater(want ?? rows.FirstOrDefault());
        }

        private Button RowOf(LinkItem item)
        {
            return rows.FirstOrDefault(r => ((RowInfo)r.Tag).Kind == RowKind.Link && ((RowInfo)r.Tag).Item == item);
        }

        private void FocusLater(Button b)
        {
            if (b == null) return;
            Dispatcher.BeginInvoke(new Action(() => Guard(() => { if (b.IsVisible) { Keyboard.Focus(b); b.BringIntoView(); } })), DispatcherPriority.Loaded);
        }

        private Button Focused()
        {
            var f = Keyboard.FocusedElement as Button;
            return f != null && rows.Contains(f) ? f : null;
        }

        private void MoveFocus(int delta)
        {
            if (rows.Count == 0) return;
            var f = Focused();
            int i = f != null ? rows.IndexOf(f) : -1;
            int j = i < 0 ? 0 : Math.Max(0, Math.Min(rows.Count - 1, i + delta));   // clamped, no wrap
            if (j == i) return;
            Keyboard.Focus(rows[j]);
            rows[j].BringIntoView();
            Sound("move");
        }

        private void Sound(string kind)
        {
            if (seam != null) seam.Sound(kind);
        }

        // ---------------- input ----------------

        /// <summary>The keyboard, and the D-pad / left stick (they also come as real arrow keys).</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.None) return;   // a controller button: OnPadDown
                if (renaming != null) { RenameKey(e); return; }
                switch (e.Key)
                {
                    case Key.Up: MoveFocus(-1); e.Handled = true; break;
                    case Key.Down: MoveFocus(1); e.Handled = true; break;
                    case Key.Left: case Key.Right: e.Handled = true; break;   // nowhere to go sideways
                    // (a held key repeats: only the arrows may; one press is one action)
                    case Key.Enter: case Key.Space: e.Handled = true; if (!e.IsRepeat) Do(Act.Confirm); break;
                    case Key.Escape: case Key.Back: case Key.BrowserBack: e.Handled = true; if (!e.IsRepeat) Do(Act.Back); break;
                    case Key.Delete: e.Handled = true; if (!e.IsRepeat) Do(Act.Forget); break;
                    case Key.F2: e.Handled = true; if (!e.IsRepeat) Do(Act.Rename); break;
                }
            }
            catch (Exception ex) { SafeLog.Warn("panel: key", ex); }
        }

        /// <summary>Keys while renaming: arrows move on the keyboard, typing types, Enter is Done, Esc cancels.</summary>
        private void RenameKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up: MoveKey(-1, 0); e.Handled = true; break;
                case Key.Down: MoveKey(1, 0); e.Handled = true; break;
                case Key.Left: MoveKey(0, -1); e.Handled = true; break;
                case Key.Right: MoveKey(0, 1); e.Handled = true; break;
                case Key.Enter: e.Handled = true; if (!e.IsRepeat) FinishRename(true); break;
                case Key.Escape: case Key.BrowserBack: e.Handled = true; if (!e.IsRepeat) FinishRename(false); break;
                case Key.Back: case Key.Delete: e.Handled = true; Backspace(); break;
                case Key.Space: e.Handled = true; TypeChar(' '); break;   // (spaces come here, not through text input)
                case Key.Tab: case Key.F2: e.Handled = true; break;
            }
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                if (renaming == null) return;
                e.Handled = true;
                string t = e.Text;
                if (string.IsNullOrEmpty(t)) return;
                foreach (char c in t) if (c != ' ') TypeChar(c);
            }
            catch (Exception ex) { SafeLog.Warn("panel: text input", ex); }
        }

        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case Key.Up: case Key.Down: case Key.Left: case Key.Right: case Key.Enter: case Key.Space:
                    case Key.Escape: case Key.Back: case Key.BrowserBack: case Key.Delete: case Key.F2:
                        e.Handled = true; break;
                }
            }
            catch (Exception ex) { SafeLog.Warn("panel: key up", ex); }
        }

        private static ControllerInput PadButton(KeyEventArgs e, out bool pressed)
        {
            pressed = false;
            if (e == null || e.GetType().Name != "GameControllerInputEventArgs") return ControllerInput.None;
            var t = e.GetType();
            var bp = t.GetProperty("Button");
            var sp = t.GetProperty("ButtonState");
            object b = bp != null ? bp.GetValue(e, null) : null;
            object s = sp != null ? sp.GetValue(e, null) : null;
            pressed = s is ControllerInputState && (ControllerInputState)s == ControllerInputState.Pressed;
            return b is ControllerInput ? (ControllerInput)b : ControllerInput.None;
        }

        private static bool IsDirection(ControllerInput b)
        {
            switch (b)
            {
                case ControllerInput.DPadUp: case ControllerInput.DPadDown: case ControllerInput.DPadLeft: case ControllerInput.DPadRight:
                case ControllerInput.LeftStickUp: case ControllerInput.LeftStickDown: case ControllerInput.LeftStickLeft: case ControllerInput.LeftStickRight:
                    return true;
            }
            return false;
        }

        /// <summary>Controller buttons: Playnite sends them only as KeyDown (Key.None) to the focused element, bubbling here.</summary>
        private void OnPadDown(object sender, KeyEventArgs e)
        {
            try
            {
                bool pressed;
                var b = PadButton(e, out pressed);
                if (b == ControllerInput.None || IsDirection(b)) return;   // directions also arrive as arrow keys (handled above)
                Act? act = ActFor(b);
                if (act == null) return;
                e.Handled = true;
                if (pressed) Do(act.Value);
            }
            catch (Exception ex) { SafeLog.Warn("panel: controller", ex); }
        }

        private void OnPadUp(object sender, KeyEventArgs e)
        {
            try
            {
                bool pressed;
                var b = PadButton(e, out pressed);
                if (b == ControllerInput.None || IsDirection(b)) return;
                if (ActFor(b) != null) e.Handled = true;   // the let-go of a press we took
            }
            catch (Exception ex) { SafeLog.Warn("panel: controller up", ex); }
        }

        private enum Act { Confirm, Back, Forget, Rename, Close }

        private Act? ActFor(ControllerInput b)
        {
            if (b == confirmButton) return Act.Confirm;
            if (b == cancelButton) return Act.Back;
            if (b == ControllerInput.X) return Act.Forget;
            if (b == ControllerInput.Y) return Act.Rename;
            if (b == ControllerInput.Guide) return Act.Close;   // (Kippix's Guide does nothing outside its main window)
            return null;
        }

        /// <summary>Playnite's confirm / cancel buttons (A / B, or swapped in its settings), read from Playnite itself.</summary>
        private static ControllerInput PlayniteBinding(string name, ControllerInput fallback)
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Playnite");
                var t = asm != null ? asm.GetType("Playnite.Input.GameControllerGesture") : null;
                var p = t != null ? t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                object v = p != null ? p.GetValue(null, null) : null;
                if (v is ControllerInput && (ControllerInput)v != ControllerInput.None) return (ControllerInput)v;
            }
            catch (Exception) { }
            return fallback;
        }

        private void Do(Act act)
        {
            if (closing) return;
            var f = Focused();
            if (act == Act.Close) { StopQuick(); Sound("back"); CloseWith(PanelResult.None, null); return; }
            if (quick)
            {
                // the "Opening" card: confirm = the list instead, cancel = nothing opens; anything else lets it open
                if (act == Act.Confirm) { StopQuick(); Sound("select"); CloseWith(PanelResult.List, focusFirst); }
                else if (act == Act.Back) { StopQuick(); Sound("back"); CloseWith(PanelResult.None, null); }
                return;
            }
            if (renaming != null)
            {
                switch (act)
                {
                    case Act.Confirm: if (f != null) ActivateRow(f); break;
                    case Act.Back: FinishRename(false); break;
                    case Act.Forget: Backspace(); break;
                    case Act.Rename: FinishRename(true); break;
                }
                return;
            }
            switch (act)
            {
                case Act.Confirm: if (f != null) ActivateRow(f); break;
                case Act.Back:
                    if (confirming) { Sound("back"); ShowList(); FocusForgetAll(); }
                    else { Sound("back"); CloseWith(PanelResult.None, null); }
                    break;
                case Act.Forget: if (!confirming && f != null && ((RowInfo)f.Tag).Kind == RowKind.Link) Forget(f); break;
                case Act.Rename:
                    if (!confirming && CanRename && f != null && ((RowInfo)f.Tag).Kind == RowKind.Link)
                    {
                        Sound("select");
                        ShowRename(((RowInfo)f.Tag).Item);
                    }
                    break;
            }
        }

        private void FocusForgetAll()
        {
            FocusLater(rows.FirstOrDefault(r => ((RowInfo)r.Tag).Kind == RowKind.ForgetAll) ?? addRow);
        }

        private void ActivateRow(Button row)
        {
            if (closing) return;
            var info = (RowInfo)row.Tag;
            switch (info.Kind)
            {
                case RowKind.Add: AddFromClipboard(); break;
                case RowKind.Link: Sound("select"); CloseWith(PanelResult.Open, info.Item); break;
                case RowKind.ForgetAll: Sound("select"); ShowConfirm(); break;
                case RowKind.Keep: Sound("back"); ShowList(); FocusForgetAll(); break;
                case RowKind.ConfirmForget:
                    store.Clear();
                    SafeLog.Info("forgot all links");
                    Sound("select");
                    ShowList();
                    FocusLater(addRow);
                    break;
                case RowKind.Key: PressKey(info); break;
                case RowKind.Direct:
                    if (settings != null)
                    {
                        settings.Direct = !settings.Direct;
                        settings.Save();
                        if (info.Detail != null) info.Detail.Text = DirectText();
                        Sound("select");
                    }
                    break;
                case RowKind.SiteIcon:
                    if (settings != null)
                    {
                        settings.SiteIcon = !settings.SiteIcon;   // (the button's picture changes when the panel closes)
                        settings.Save();
                        if (info.Detail != null) info.Detail.Text = SiteIconText();
                        Sound("select");
                    }
                    break;
            }
        }

        private void AddFromClipboard()
        {
            string url;
            LinkItem item = null;
            bool existed = false;
            if (LinkUrl.TryParse(ReadClipboard(), out url))
            {
                try { item = store.AddOrTouch(url, out existed); }
                catch (ArgumentException) { item = null; }   // (fixed text only; nothing to log about the address)
            }
            url = null;
            if (item == null)
            {
                var info = (RowInfo)addRow.Tag;
                if (info.Detail != null) info.Detail.Text = "The clipboard doesn't hold a web address";
                Sound("back");
                return;
            }
            if (existed) SafeLog.Info("link added again", item.Id); else SafeLog.Info("link added", item.Id);
            ShowList();
            FocusLater(RowOf(item) ?? addRow);
            Sound("select");
        }

        private void Forget(Button row)
        {
            var item = ((RowInfo)row.Tag).Item;
            int at = rows.IndexOf(row);
            string id = item.Id;
            store.Remove(item);
            SafeLog.Info("link forgotten", id);
            ShowList();
            FocusLater(rows.Count > 0 ? rows[Math.Max(0, Math.Min(rows.Count - 1, at))] : null);
            Sound("back");
        }

        private void CloseWith(PanelResult r, LinkItem item)
        {
            if (closing) return;
            closing = true;
            Result = r;
            ResultItem = item;
            EndRename();
            Close();
        }
    }
}
