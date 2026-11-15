using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Two things around playing a game from fullscreen:
    /// - Coming back: a card (top right) says what you just played, how long this session was and how long altogether.
    ///   It waits until Playnite is in front again, stays a few seconds and goes; it never takes the highlight.
    /// - Starting: Playnite stays in front of everything (Steam's "launching" window, the taskbar) until the game is
    ///   running, so the launch screen is what you see. If the game hasn't started after a while, Playnite stops staying in
    ///   front, so anything Steam needs you to answer (an install step, a cloud-save question) can come up.
    /// </summary>
    public partial class ThemeApi
    {
        // ---------------- "You just played" ----------------

        public bool SessionToastShown { get; private set; }   // (bound straight to Visibility: PluginSettings turns bool into Visible / Collapsed)
        public string SessionToastGame { get; private set; }
        public string SessionToastSession { get; private set; }
        public string SessionToastTotal { get; private set; }
        public ImageSource SessionToastCover { get; private set; }
        public bool HasSessionToastCover { get { return SessionToastCover != null; } }
        public bool HasSessionToastLine1 { get { return !string.IsNullOrEmpty(SessionToastSession); } }
        public bool HasSessionToastLine2 { get { return !string.IsNullOrEmpty(SessionToastTotal); } }

        public bool SessionSummary
        {
            get { return settings.SessionSummary; }
            set { if (settings.SessionSummary == value) return; settings.SessionSummary = value; SaveSettings(); Notify("SessionSummary"); }
        }

        private Guid sessionGameId;
        private ulong sessionStartPlaytime;
        private DispatcherTimer toastWait, toastHide;
        private DateTime toastWaitSince;
        private bool toastIsSample;        // Notifications › Show a sample: it shows over the settings on purpose
        private string toastPanelSeen;     // the side panel open when the card was last checked

        /// <summary>
        /// The card sits over the top of the right-hand panels: a panel opening takes it away, so the panel's title and
        /// first row can be read (called whenever the panels change).
        /// </summary>
        private void HideToastUnderPanel()
        {
            string now = IsSidePanelOpen ? openPanel : null;
            bool opened = now != null && now != toastPanelSeen;
            toastPanelSeen = now;
            if (!opened || !SessionToastShown) return;
            if (toastHide != null) toastHide.Stop();
            SessionToastShown = false;
            Notify("SessionToastShown");
            ShowQueuedCard();   // (a card waiting behind it shows once the panel closes)
        }

        // r15: the "You just played" card isn't pushed out by a card that comes while it waits (the save backup's "Backed up"
        // a few seconds after the game closes replaced it before Playnite was back): those wait their turn behind it
        private bool toastIsSession;
        private readonly System.Collections.Generic.List<object[]> toastQueue = new System.Collections.Generic.List<object[]>();
        private string toastWaitWhy;       // why the card is still waiting (logged when it changes)

        private void ShowQueuedCard()
        {
            if (toastQueue.Count == 0) return;
            var c = toastQueue[0];
            toastQueue.RemoveAt(0);
            ShowCardNow((string)c[0], (string)c[1], (string)c[2], (string)c[3], c[4] as ImageSource, false);
            toastKeep = c.Length > 5 && c[5] is bool && (bool)c[5];
        }

        /// <summary>Playnite is the app in front: its main window is active, or another of its own windows is in front of it
        /// (a message box that just closed can hand the activation to a window that's gone, and the main one isn't "active").</summary>
        private bool PlayniteFrontForCard(Window w, out string why)
        {
            why = null;
            if (w.IsActive) return true;
            if (!w.IsVisible || w.WindowState == WindowState.Minimized) { why = "Playnite's window is hidden or minimised"; return false; }
            IntPtr fg = GetForegroundWindow();
            uint pid = 0;
            if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out pid);
            if (cardOwnPid == 0) { try { using (var me = System.Diagnostics.Process.GetCurrentProcess()) cardOwnPid = (uint)me.Id; } catch (Exception) { } }
            if (fg != IntPtr.Zero && pid == cardOwnPid) return true;
            if (pid != cardFrontPid)   // (the name looked up once per app in front, not every tick)
            {
                cardFrontPid = pid;
                cardFrontName = "";
                try { if (pid != 0) using (var p = System.Diagnostics.Process.GetProcessById((int)pid)) cardFrontName = p.ProcessName; } catch (Exception) { }
            }
            why = "Playnite isn't in front" + (cardFrontName.Length > 0 ? " (" + cardFrontName + " is)" : "");
            return false;
        }

        private uint cardOwnPid, cardFrontPid;
        private string cardFrontName = "";

        /// <summary>The game is starting: remember its play time so far (Playnite may add this session to it a moment later).</summary>
        private void SessionStarting(Game g)
        {
            if (g == null) return;
            sessionGameId = g.Id;
            sessionStartPlaytime = g.Playtime;
        }

        /// <summary>A game stopped: once Playnite is back in front, the card shows.</summary>
        public void GameSessionEnded(Game game, ulong seconds)
        {
            if (game != null) OnUi(() => NoteProfilePlayed(game, seconds));   // Profiles: their recent games follow along
            if (game == null || !IsFullscreen) return;
            OnUi(() =>
            {
                try
                {
                    // (0.86: no minimum any more: a quick go that's quit from the overlay gets its card too)
                    if (!settings.SessionSummary) return;
                    ulong before = game.Id == sessionGameId ? sessionStartPlaytime : 0;
                    ulong total = game.Playtime;
                    try { var fresh = api.Database.Games.Get(game.Id); if (fresh != null) total = fresh.Playtime; } catch (Exception) { }
                    if (before > 0 && total < before + seconds) total = before + seconds;   // (not added up yet)
                    if (total < seconds) total = seconds;
                    ImageSource cover = null;
                    try
                    {
                        string path = !string.IsNullOrEmpty(game.CoverImage) ? api.Database.GetFullFilePath(game.CoverImage) : null;
                        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) cover = Images.Load(path, 160);
                    }
                    catch (Exception) { }
                    ShowCardNow(Loc("LOC_KX_ToastJustPlayed", "YOU JUST PLAYED"), game.Name,
                                settings.NotifySessionLine ? string.Format(Loc("LOC_KX_ToastSession", "This session: {0}"), Duration(seconds)) : "",
                                settings.NotifyTotalLine ? string.Format(Loc("LOC_KX_ToastTotal", "{0} played altogether"), Duration(total)) : "",
                                settings.NotifyCover ? cover : null, true);
                    logger.Info("Kippix: session card for " + game.Name + " (" + Duration(seconds) + "): shows once Playnite is in front");
                }
                catch (Exception ex) { logger.Warn("Kippix: session card: " + ex.Message); }
            });
        }

        public string SessionToastHeader { get; private set; }

        /// <summary>A card about something else (Steam sign-in, the wishlist): only when Settings › Notifications › Other notices is on.</summary>
        internal void ShowNotice(string header, string title, string line1, string line2, ImageSource cover)
        {
            if (settings.NotifyOther) ShowCard(header, title, line1, line2, cover);
        }

        /// <summary>(0.86) A card that mustn't be pushed out by the next one (a wishlist game out today, a save backup): it
        /// waits its turn if a card is up or waiting, and cards after it wait behind it.</summary>
        internal void ShowNoticeKept(string header, string title, string line1, string line2, ImageSource cover)
        {
            if (!settings.NotifyOther) return;
            ShowCardKept(header, title, line1, line2, cover);
        }

        /// <summary>(0.99.39) The same, for cards with their own switch (game updates).</summary>
        internal void ShowCardKept(string header, string title, string line1, string line2, ImageSource cover)
        {
            bool busy = !toastIsSample && ((toastWait != null && toastWait.IsEnabled) || SessionToastShown);
            if (busy)
            {
                if (toastQueue.Count >= 6) toastQueue.RemoveAt(0);
                toastQueue.Add(new object[] { header, title, line1, line2, cover, true });
                return;
            }
            ShowCardNow(header, title, line1, line2, cover, false);
            toastKeep = true;
        }

        private bool toastKeep;   // the card up or waiting is one that's never replaced (session, wishlist, backup)

        /// <summary>The card (top right, a few seconds, never takes the highlight). Shows once Playnite is in front (and no game is running).</summary>
        internal void ShowCard(string header, string title, string line1, string line2, ImageSource cover)
        {
            // the session card waiting or showing: this one goes after it (at most three wait)
            if ((toastIsSession || toastKeep) && ((toastWait != null && toastWait.IsEnabled) || SessionToastShown))
            {
                if (toastQueue.Count >= 6) toastQueue.RemoveAt(0);
                toastQueue.Add(new object[] { header, title, line1, line2, cover });
                return;
            }
            ShowCardNow(header, title, line1, line2, cover, false);
        }

        private void ShowCardNow(string header, string title, string line1, string line2, ImageSource cover, bool session)
        {
            toastIsSession = session;
            toastKeep = false;
            toastWaitWhy = null;
            toastIsSample = false;
            SessionToastHeader = header ?? "";
            SessionToastGame = title ?? "";
            SessionToastSession = line1 ?? "";
            SessionToastTotal = line2 ?? "";
            SessionToastCover = cover;
            if (SessionToastShown) { SessionToastShown = false; Notify("SessionToastShown"); }   // (one showing: the new one replaces it)
            if (toastWait == null)
            {
                toastWait = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(400) };
                toastWait.Tick += (s, e) => ToastWaitTick();
            }
            toastWaitSince = DateTime.Now;
            toastWait.Start();
        }

        private void ToastWaitTick()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            string why = null;
            // a Playnite message box (e.g. Ludusavi's "Back up save data for …?") is up: it goes first, and the time it's up
            // doesn't count towards giving up (r15)
            if (playniteDialogsOpen > 0) { toastWaitSince = DateTime.Now; why = "a message box is up"; }
            else if ((DateTime.Now - toastWaitSince).TotalMinutes > 3)   // never came back to Playnite
            {
                toastWait.Stop();
                if (toastIsSession || toastKeep) logger.Info("Kippix: " + (toastIsSession ? "session" : SessionToastGame) + " card dropped: Playnite wasn't in front within 3 minutes (" + (toastWaitWhy ?? "?") + ")");
                toastIsSession = false;
                toastKeep = false;
                toastQueue.Clear();
                return;
            }
            else if (w == null) why = "no main window";
            else if (gameRunningNow) why = "a game is running";
            else if (!PlayniteFrontForCard(w, out why)) { }
            else if (IsSidePanelOpen && !toastIsSample) why = "a side panel is open";   // not over a panel: it shows once the panel closes
            if (why != null)
            {
                if ((toastIsSession || toastKeep) && why != toastWaitWhy) logger.Info("Kippix: " + (toastIsSession ? "session" : "\"" + SessionToastGame + "\"") + " card waiting: " + why);
                toastWaitWhy = why;
                return;
            }
            if (toastIsSession || toastKeep) logger.Info("Kippix: " + (toastIsSession ? "session" : "\"" + SessionToastGame + "\"") + " card shown" + (w.IsActive ? "" : " (Playnite in front, its main window not active)"));
            toastWait.Stop();
            SessionToastShown = true;
            foreach (string n in new[] { "SessionToastHeader", "SessionToastGame", "SessionToastSession", "SessionToastTotal", "SessionToastCover", "HasSessionToastCover",
                                         "HasSessionToastLine1", "HasSessionToastLine2", "SessionToastShown" }) Notify(n);
            try
            {
                var card = UiTools.FindByName(w, "KX_SessionToast") as Border;
                if (card != null)
                {
                    ApplyCardLook(card);
                    card.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(350))));
                }
            }
            catch (Exception) { }
            if (toastHide == null)
            {
                toastHide = new DispatcherTimer(DispatcherPriority.Background, ui);
                toastHide.Tick += (s, e) => { toastHide.Stop(); SessionToastShown = false; Notify("SessionToastShown"); ShowQueuedCard(); };   // (then the next one waiting, if any)
            }
            toastHide.Stop();
            toastHide.Interval = TimeSpan.FromSeconds(Math.Max(3, settings.NotifySeconds));
            toastHide.Start();
        }

        /// <summary>"45 min", "2 h 5 min", "126 h".</summary>
        private string Duration(ulong seconds)
        {
            ulong minutes = Math.Max(1, (seconds + 30) / 60);
            if (minutes < 60) return string.Format(Loc("LOC_KX_DurMin", "{0} min"), minutes);
            ulong h = minutes / 60, m = minutes % 60;
            if (h >= 20 || m == 0) return string.Format(Loc("LOC_KX_DurHours", "{0} h"), h);
            return string.Format(Loc("LOC_KX_DurHoursMin", "{0} h {1} min"), h, m);
        }

        // ---------------- Settings › Notifications ----------------

        public bool IsOptionsNotify { get { return optionsPage == "notify"; } }

        public bool NotifyCover { get { return settings.NotifyCover; } set { SetNotify(ref settings.NotifyCover, value); } }
        public bool NotifySessionLine { get { return settings.NotifySessionLine; } set { SetNotify(ref settings.NotifySessionLine, value); } }
        public bool NotifyTotalLine { get { return settings.NotifyTotalLine; } set { SetNotify(ref settings.NotifyTotalLine, value); } }
        public bool NotifyOther { get { return settings.NotifyOther; } set { if (settings.NotifyOther == value) return; settings.NotifyOther = value; SaveSettings(); NotifyNotify(); } }

        private void SetNotify(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            SaveSettings();
            NotifyNotify();
            ShowSampleCard();   // see it straight away
        }

        private static readonly string[] NotifyPosKeys = { "LOC_KX_PosTopRight", "LOC_KX_PosTopLeft", "LOC_KX_PosTopCentre", "LOC_KX_PosBottomRight", "LOC_KX_PosBottomLeft", "LOC_KX_PosBottomCentre" };
        private static readonly string[] NotifyPosText_ = { "Top right", "Top left", "Top centre", "Bottom right", "Bottom left", "Bottom centre" };
        private static readonly string[] NotifyStyleKeys = { "LOC_KX_CardDark", "LOC_KX_CardAccent", "LOC_KX_CardGlass", "LOC_KX_CardLight" };
        private static readonly string[] NotifyStyleText_ = { "Dark", "Accent colour", "See-through", "Light" };
        private static readonly string[] NotifySizeKeys = { "LOC_KX_SizeSmall", "LOC_KX_SizeNormal", "LOC_KX_SizeLarge", "LOC_KX_SizeXL" };
        private static readonly string[] NotifySizeText_ = { "Small", "Normal", "Large", "Extra large" };
        private static readonly double[] NotifyScales = { 0.85, 1.0, 1.25, 1.5 };

        public string NotifyPosText { get { int i = Clamp(settings.NotifyPos, 0, 5); return Loc(NotifyPosKeys[i], NotifyPosText_[i]); } }
        public string NotifyStyleText { get { int i = Clamp(settings.NotifyStyle, 0, 3); return Loc(NotifyStyleKeys[i], NotifyStyleText_[i]); } }
        public string NotifySizeText { get { int i = Clamp(settings.NotifySize, 0, 3); return Loc(NotifySizeKeys[i], NotifySizeText_[i]); } }
        public string NotifySecondsText { get { return string.Format(Loc("LOC_KX_NSeconds", "{0} seconds"), settings.NotifySeconds); } }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private ICommand cycNotifyPos, cycNotifyStyle, cycNotifySize, cycNotifySecs, notifySample;
        public ICommand CycleNotifyPosCommand { get { return cycNotifyPos ?? (cycNotifyPos = new RelayCommand(_ => { settings.NotifyPos = (Clamp(settings.NotifyPos, 0, 5) + 1) % 6; NotifyChanged(); })); } }
        public ICommand CycleNotifyStyleCommand { get { return cycNotifyStyle ?? (cycNotifyStyle = new RelayCommand(_ => { settings.NotifyStyle = (Clamp(settings.NotifyStyle, 0, 3) + 1) % 4; NotifyChanged(); })); } }
        public ICommand CycleNotifySizeCommand { get { return cycNotifySize ?? (cycNotifySize = new RelayCommand(_ => { settings.NotifySize = (Clamp(settings.NotifySize, 0, 3) + 1) % 4; NotifyChanged(); })); } }
        public ICommand CycleNotifySecondsCommand { get { return cycNotifySecs ?? (cycNotifySecs = new RelayCommand(_ => { settings.NotifySeconds = NextIn(new[] { 5, 8, 12, 20, 30 }, settings.NotifySeconds); NotifyChanged(); })); } }
        public ICommand NotifySampleCommand { get { return notifySample ?? (notifySample = new RelayCommand(_ => { PlaySound("select"); ShowSampleCard(); })); } }

        private void NotifyChanged()
        {
            PlaySound("move");
            SaveSettings();
            NotifyNotify();
            ShowSampleCard();
        }

        private void NotifyNotify()
        {
            foreach (string n in new[] { "SessionSummary", "NotifyCover", "NotifySessionLine", "NotifyTotalLine", "NotifyOther", "NotifyPosText", "NotifyStyleText", "NotifySizeText", "NotifySecondsText",
                                         "SessionReminder", "ReminderText", "GameUpdateNotify", "GameUpdateWhenText" })
                Notify(n);
        }

        /// <summary>What the card looks like with these settings, on the selected game (or a made-up one).</summary>
        private void ShowSampleCard()
        {
            Game g = null;
            try { g = SelectedGameNow(); } catch (Exception) { }
            ImageSource cover = null;
            try
            {
                string path = g != null && !string.IsNullOrEmpty(g.CoverImage) ? api.Database.GetFullFilePath(g.CoverImage) : null;
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) cover = Images.Load(path, 160);
            }
            catch (Exception) { }
            ulong total = g != null && g.Playtime > 0 ? g.Playtime : 12 * 3600;
            ShowCardNow(Loc("LOC_KX_ToastJustPlayed", "YOU JUST PLAYED"), g != null ? g.Name : "Game name",
                        settings.NotifySessionLine ? string.Format(Loc("LOC_KX_ToastSession", "This session: {0}"), Duration(47 * 60)) : "",
                        settings.NotifyTotalLine ? string.Format(Loc("LOC_KX_ToastTotal", "{0} played altogether"), Duration(total)) : "",
                        settings.NotifyCover ? cover : null, false);   // (straight away, never queued)
            toastIsSample = true;
        }

        /// <summary>Where the card sits, its colours and size (Settings › Notifications).</summary>
        private void ApplyCardLook(Border card)
        {
            int pos = Clamp(settings.NotifyPos, 0, 5);
            bool bottom = pos >= 3;
            int side = pos % 3;   // 0 right, 1 left, 2 centre
            card.HorizontalAlignment = side == 0 ? HorizontalAlignment.Right : side == 1 ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            card.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            card.Margin = new Thickness(side == 1 ? 56 : 0, bottom ? 0 : 112, side == 0 ? 56 : 0, bottom ? 120 : 0);
            double scale = NotifyScales[Clamp(settings.NotifySize, 0, 3)];
            card.LayoutTransform = Math.Abs(scale - 1) < 0.01 ? Transform.Identity : new ScaleTransform(scale, scale);

            Color accent = Color.FromRgb(0x3D, 0x8B, 0xFD);
            var ab = card.TryFindResource("KX_AccentBrush") as SolidColorBrush;
            if (ab != null) accent = ab.Color;
            Brush bg, line;
            bool light = false;
            switch (Clamp(settings.NotifyStyle, 0, 3))
            {
                case 1:
                    bg = new SolidColorBrush(Color.FromArgb(0xF2, (byte)(0x12 + (accent.R - 0x12) * 0.35), (byte)(0x14 + (accent.G - 0x14) * 0.35), (byte)(0x18 + (accent.B - 0x18) * 0.35)));
                    line = new SolidColorBrush(accent);
                    break;
                case 2: bg = new SolidColorBrush(Color.FromArgb(0x8C, 0x0E, 0x10, 0x14)); line = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)); break;
                case 3: bg = new SolidColorBrush(Color.FromArgb(0xF5, 0xF4, 0xF6, 0xFA)); line = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)); light = true; break;
                default: bg = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x14, 0x18)); line = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)); break;
            }
            card.Background = bg;
            card.BorderBrush = line;
            var dark = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1A));
            var dim = new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5A));
            int i = 0;
            foreach (var tb in UiTools.FindAll<System.Windows.Controls.TextBlock>(card))
            {
                if (light) tb.Foreground = i == 0 ? dim : i == 1 ? dark : dim;   // header, game, lines
                else tb.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
                i++;
            }
        }

        // ---------------- staying in front while a game starts ----------------

        private DispatcherTimer launchFront;
        private DateTime launchFrontSince;
        private volatile bool launchFrontStarted;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        /// <summary>A game is starting from fullscreen: Playnite stays on top until it runs (at most 25 seconds).</summary>
        private void HoldFrontWhileStarting()
        {
            if (!IsFullscreen) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            launchFrontSince = DateTime.Now;
            launchFrontStarted = false;   // (issue #11: this launch, not "anything is running": another app may be open already)
            if (launchFront == null)
            {
                launchFront = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
                launchFront.Tick += (s, e) => { if (launchFrontStarted || (DateTime.Now - launchFrontSince).TotalSeconds > 25) ReleaseFront(); };
            }
            launchFront.Start();
            Dbg("launch: Playnite kept in front while the game starts");
        }

        /// <summary>The game is running (or didn't start): Playnite stops staying on top, so the game comes up over it.</summary>
        private void ReleaseFront()
        {
            if (launchFront == null || !launchFront.IsEnabled) return;
            launchFront.Stop();
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null) return;
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero)
            {
                SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                // that also lifts Playnite above every normal window: the game's (in front by now) goes back over it
                IntPtr fg = GetForegroundWindow();
                uint pid;
                if (fg != IntPtr.Zero && fg != h && GetWindowThreadProcessId(fg, out pid) != 0 && pid != (uint)System.Diagnostics.Process.GetCurrentProcess().Id)
                    SetWindowPos(h, fg, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            if (w.Topmost) w.Topmost = false;
        }
    }
}
