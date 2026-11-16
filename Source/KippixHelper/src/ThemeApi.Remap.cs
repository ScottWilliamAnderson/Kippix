using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>One of Kippix's controller actions that can be put on another button (Settings › General › Controller buttons).</summary>
    internal class PadAction
    {
        public string Id;
        public string Context;            // home, game, menus, edit, hold
        public ControllerInput Default;
        public bool Hold;                 // held, not pressed (hold for home, hold for Free edit): read straight from the button
        public string NameKey, NameText;
    }

    /// <summary>
    /// Controller buttons: a translation layer between the button you press and the button Kippix's (and Playnite's) handlers
    /// see. An action is "a button in a place" (Y on the home screen = Search). Moving it to LB means: on the home screen, LB is
    /// handed on as Y (so every handler that answers Y there does its job), and Y itself does nothing there any more. The press
    /// is caught first (ThemeApi.PadInput.cs, before anything else sees it) and sent again as the other button; its release
    /// follows it the same way, so holds (hold X to keep a card in place) work on the new button too. The two held actions
    /// (hold for home, hold for Free edit) are read straight from their button. Playnite's A, B, the D-pad / sticks and LT / RT
    /// (which Playnite also sends as keys) can't change and are listed as fixed. The Steam Controller still comes through
    /// SteamCopyFilter first: the presses sent again here are marked so it leaves them alone.
    /// Also: the plumbing for this round's code-built settings pages (GRows: Controller buttons, Handheld, touch and mouse).
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly ControllerInput[] RemapButtons =
        {
            ControllerInput.A, ControllerInput.B, ControllerInput.X, ControllerInput.Y, ControllerInput.LeftShoulder, ControllerInput.RightShoulder,
            ControllerInput.Back, ControllerInput.Start, ControllerInput.LeftStick, ControllerInput.RightStick
        };

        private List<PadAction> padActions;
        private List<PadAction> PadActions { get { return padActions ?? (padActions = BuildPadActions()); } }

        private static PadAction PA(string id, string ctx, ControllerInput def, string key, string text)
        {
            return new PadAction { Id = id, Context = ctx, Default = def, Hold = ctx == "hold", NameKey = key, NameText = text };
        }

        private static List<PadAction> BuildPadActions()
        {
            return new List<PadAction>
            {
                PA("home.play", "home", ControllerInput.X, "LOC_KX_RmHomePlay", "Play (hold: keep the game in its spot)"),
                PA("home.menu", "home", ControllerInput.Start, "LOC_KX_RmHomeMenu", "Game options"),
                PA("home.search", "home", ControllerInput.Y, "LOC_KX_RmHomeSearch", "Search"),
                PA("home.mainmenu", "home", ControllerInput.Back, "LOC_KX_RmHomeMain", "Playnite's menu"),
                PA("home.prevfilter", "home", ControllerInput.LeftShoulder, "LOC_KX_RmHomePrevFilter", "Previous filter (or folder)"),
                PA("home.nextfilter", "home", ControllerInput.RightShoulder, "LOC_KX_RmHomeNextFilter", "Next filter (or folder)"),
                PA("home.filters", "home", ControllerInput.RightStick, "LOC_KX_RmHomeFilters", "Filters"),
                PA("home.presets", "home", ControllerInput.LeftStick, "LOC_KX_RmHomePresets", "Filter presets"),
                PA("game.x", "game", ControllerInput.X, "LOC_KX_RmGameX", "Settings for the highlighted part"),
                PA("game.prev", "game", ControllerInput.LeftShoulder, "LOC_KX_RmGamePrev", "Previous game"),
                PA("game.next", "game", ControllerInput.RightShoulder, "LOC_KX_RmGameNext", "Next game"),
                PA("menu.search", "menus", ControllerInput.Y, "LOC_KX_RmMenuSearch", "Search (Store, achievements, gallery)"),
                PA("menu.x", "menus", ControllerInput.X, "LOC_KX_RmMenuX", "Extra action (wishlist, favourite, roll again…)"),
                PA("menu.prev", "menus", ControllerInput.LeftShoulder, "LOC_KX_RmMenuPrev", "Previous tab, store or page"),
                PA("menu.next", "menus", ControllerInput.RightShoulder, "LOC_KX_RmMenuNext", "Next tab, store or page"),
                PA("edit.settings", "edit", ControllerInput.X, "LOC_KX_RmEditSettings", "Its settings"),
                PA("edit.size", "edit", ControllerInput.Y, "LOC_KX_RmEditSize", "Size it (the Board: remove it)"),
                PA("edit.delete", "edit", ControllerInput.Back, "LOC_KX_RmEditDelete", "Delete it"),
                PA("hold.home", "hold", ControllerInput.B, "LOC_KX_RmHoldHome", "Hold for the home screen"),
                PA("hold.edit", "hold", ControllerInput.A, "LOC_KX_RmHoldEdit", "Hold for Free edit (the Board: edit it)"),
            };
        }

        private string PadActionName(PadAction a) { return Loc(a.NameKey, a.NameText); }

        internal static string PadButtonName(ControllerInput b)
        {
            switch (b)
            {
                case ControllerInput.LeftShoulder: return "LB";
                case ControllerInput.RightShoulder: return "RB";
                case ControllerInput.Back: return "View";
                case ControllerInput.Start: return "Menu";
                case ControllerInput.LeftStick: return "L3";
                case ControllerInput.RightStick: return "R3";
                case ControllerInput.TriggerLeft: return "LT";
                case ControllerInput.TriggerRight: return "RT";
                case ControllerInput.None: return "—";
            }
            return b.ToString();
        }

        private ControllerInput PadMapOf(PadAction a)
        {
            string v;
            ControllerInput b;
            if (a != null && settings.PadMap != null && settings.PadMap.TryGetValue(a.Id, out v) && !string.IsNullOrEmpty(v)
                && Enum.TryParse(v, out b) && RemapButtons.Contains(b) && PadTargetAllowed(a, b)) return b;
            return a != null ? a.Default : ControllerInput.None;
        }

        /// <summary>
        /// B (back) only ever takes the hold for home, and A can't take a menu action: with A translated in every menu and page,
        /// nothing could be chosen there with the controller, not even Settings › General › Controller buttons to undo it.
        /// </summary>
        private static bool PadTargetAllowed(PadAction a, ControllerInput b)
        {
            if (a == null || a.Hold) return true;
            if (b == ControllerInput.B) return false;
            if (b == ControllerInput.A && a.Context == "menus") return false;
            return true;
        }

        private ControllerInput PadMapOf(string id) { return PadMapOf(PadActions.FirstOrDefault(a => a.Id == id)); }

        /// <summary>The button held for the home screen (B unless it was moved).</summary>
        internal ControllerInput HoldHomeButton { get { return PadMapOf("hold.home"); } }
        /// <summary>The button held for Free edit (A unless it was moved).</summary>
        internal ControllerInput HoldEditButton { get { return PadMapOf("hold.edit"); } }

        /// <summary>Where the controller is now, for the actions: home, game, menus, edit, or null (nothing translated: typing, a question, the browser, this page).</summary>
        private string PadContextNow()
        {
            if (kbOpen || confirmOpen || descReaderOpen) return null;
            if (padCapture != null) return null;
            if (freeMove || IsMasterMoving || boardEditing) return "edit";
            if (openPanel == null) return detailsOpen ? "game" : "home";
            if (openPanel == "browser" || openPanel == "welcome" || openPanel == "trailer" || IsControllerPage) return null;
            if (openPanel == "options" && optionsPage == "padbuttons") return null;   // the page itself always answers to the real buttons
            return "menus";
        }

        // ---------------- the translation ----------------

        private KeyEventArgs remapRaised;   // a press sent again here (as the button it stands for): not translated again; SteamCopyFilter leaves it alone
        private KeyEventArgs tapReplay;     // a short press of a moved hold button, sent again: does what that button does (not held again)
        private readonly Dictionary<ControllerInput, ControllerInput> remapHeld = new Dictionary<ControllerInput, ControllerInput>();
        private DateTime padCaptureEnded = DateTime.MinValue;

        /// <summary>The button a press stands for here: itself, another button (an action moved onto it), or None (its action moved away).</summary>
        private ControllerInput RemapPhysical(ControllerInput physical)
        {
            if (settings.PadMap == null || settings.PadMap.Count == 0) return physical;
            string ctx = PadContextNow();
            if (ctx == null) return physical;
            foreach (var a in PadActions)
                if (a.Context == ctx && PadMapOf(a) == physical && a.Default != physical) return a.Default;
            foreach (var a in PadActions)
                if (a.Context == ctx && a.Default == physical && PadMapOf(a) != physical) return ControllerInput.None;
            return physical;
        }

        /// <summary>A controller press: true when it was translated (sent again as another button) or swallowed.</summary>
        private bool RemapDown(KeyEventArgs e, ControllerInput physical)
        {
            if (ReferenceEquals(e, remapRaised) || ReferenceEquals(e, tapReplay)) return false;
            var logical = RemapPhysical(physical);
            if (logical == physical) { remapHeld.Remove(physical); return false; }
            remapHeld[physical] = logical;
            e.Handled = true;
            padHandled = e;
            if (logical != ControllerInput.None)
            {
                Dbg("controller buttons: " + physical + " -> " + logical);
                RaisePad(e.GetType(), logical, true, false);
            }
            return true;
        }

        /// <summary>A controller release: follows its press (the same button it was sent as, or swallowed).</summary>
        private bool RemapUp(KeyEventArgs e, ControllerInput physical)
        {
            if (ReferenceEquals(e, remapRaised) || ReferenceEquals(e, tapReplay)) return false;
            ControllerInput logical;
            if (!remapHeld.TryGetValue(physical, out logical)) return false;
            remapHeld.Remove(physical);
            e.Handled = true;
            padHandled = e;
            if (logical != ControllerInput.None) RaisePad(e.GetType(), logical, false, false);
            return true;
        }

        /// <summary>Sends a controller press or release to the highlighted element, through every handler (ours too).</summary>
        private void RaisePad(Type t, ControllerInput button, bool pressed, bool asTap)
        {
            if (t == null || ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    var ev = Activator.CreateInstance(t, Key.None, pressed ? ControllerInputState.Pressed : ControllerInputState.Released, button) as KeyEventArgs;
                    if (ev == null) return;
                    remapRaised = ev;
                    if (asTap) tapReplay = ev;
                    InputManager.Current.ProcessInput(ev);
                }
                catch (Exception ex) { logger.Warn("Kippix: controller buttons: sending " + button + ": " + ex.Message); }
                finally { remapRaised = null; tapReplay = null; }
            }), DispatcherPriority.Input);
        }

        /// <summary>Playnite's controller event type, once one has been seen (touch and the mouse send presses with it).</summary>
        private static Type padEventType;

        private static Type PadEventType()
        {
            if (padEventType != null) return padEventType;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = asm.GetName().Name;
                    if (n != "Playnite" && n != "Playnite.FullscreenApp" && n != "Playnite.DesktopApp") continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }
                    var t = types.FirstOrDefault(x => x.Name == "GameControllerInputEventArgs");
                    if (t != null) { padEventType = t; break; }
                }
            }
            catch (Exception) { }
            return padEventType;
        }

        // ---------------- "press a button" ----------------

        private string padCapture;          // the action waiting for its new button
        private DateTime padCaptureAt;
        private DispatcherTimer padCaptureTimer;

        private void StartPadCapture(string id)
        {
            padCapture = id;
            padCaptureAt = DateTime.Now;
            PlaySound("select");
            if (padCaptureTimer == null)
            {
                padCaptureTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(8) };
                padCaptureTimer.Tick += (s, e) => { padCaptureTimer.Stop(); if (padCapture != null) EndPadCapture(null, true); };
            }
            padCaptureTimer.Stop();
            padCaptureTimer.Start();
            GRowsAgain(id);
        }

        /// <summary>Ends "press a button": with the button pressed (or none: cancelled).</summary>
        private void EndPadCapture(ControllerInput? button, bool quiet)
        {
            string id = padCapture;
            padCapture = null;
            padCaptureEnded = DateTime.Now;
            if (padCaptureTimer != null) padCaptureTimer.Stop();
            var a = PadActions.FirstOrDefault(x => x.Id == id);
            if (a != null && button.HasValue)
            {
                if (settings.PadMap == null) settings.PadMap = new Dictionary<string, string>();
                if (button.Value == a.Default) settings.PadMap.Remove(a.Id); else settings.PadMap[a.Id] = button.Value.ToString();
                remapHeld.Clear();
                SaveSettings();
                PlaySound("select");
            }
            else if (!quiet) PlaySound("back");
            if (openPanel == "options" && optionsPage == "padbuttons") GRowsAgain(id);
        }

        /// <summary>From OnPadDownEarly / OnPadUpEarly: while waiting for a button, every button is caught here.</summary>
        private bool PadCaptureKey(KeyEventArgs e, ControllerInput button, bool down)
        {
            if (padCapture == null)
            {
                // the release of the button just chosen: nothing else sees it
                if (!down && (DateTime.Now - padCaptureEnded).TotalMilliseconds < 600) { e.Handled = true; padHandled = e; return true; }
                return false;
            }
            e.Handled = true;
            padHandled = e;
            if (!down || IsPadRelease(e)) return true;
            if ((DateTime.Now - padCaptureAt).TotalMilliseconds < 250) return true;   // (the A that started it)
            if (button == ControllerInput.B && padCapture != "hold.home") { EndPadCapture(null, false); return true; }
            if (!RemapButtons.Contains(button)) { PlaySound("back"); return true; }   // the D-pad, triggers, Guide: not these
            if (!PadTargetAllowed(PadActions.FirstOrDefault(x => x.Id == padCapture), button)) { PlaySound("back"); return true; }   // (A for a menu action: see PadTargetAllowed)
            EndPadCapture(button, false);
            return true;
        }

        // ---------------- the page: Settings › General › Controller buttons ----------------

        private void BuildPadButtonRows(List<PickRow> rows)
        {
            rows.Add(new PickRow { IsInfo = true, Icon = "controller", Text = Loc("LOC_KX_RmIntro", "A on an action, then press the button you want for it"),
                                   Sub = Loc("LOC_KX_RmIntroSub", "X puts that one back. Kippix's own buttons can move; Playnite's are listed at the end.") });
            string[] ctxs = { "home", "game", "menus", "edit", "hold" };
            foreach (string ctx in ctxs)
            {
                rows.Add(new PickRow { IsHeader = true, Text = PadContextHeader(ctx) });
                foreach (var a in PadActions.Where(x => x.Context == ctx))
                {
                    var act = a;
                    var now = PadMapOf(a);
                    string sub;
                    if (padCapture == a.Id) sub = a.Id == "hold.home" ? Loc("LOC_KX_RmPressHome", "Press (and let go of) the button you want…") : Loc("LOC_KX_RmPress", "Press the button you want…   B cancels");
                    else
                    {
                        sub = string.Format(Loc("LOC_KX_RmButton", "Button: {0}"), PadButtonName(now));
                        if (now != a.Default) sub += "   ·   " + string.Format(Loc("LOC_KX_RmWas", "was {0}"), PadButtonName(a.Default));
                        string clash = PadClash(a, now);
                        if (clash != null) sub += "   ·   ⚠ " + clash;
                    }
                    rows.Add(new PickRow { Key = "pad:" + a.Id, Icon = a.Hold ? "hourglass" : "controller", Text = PadActionName(a), Sub = sub,
                                           Command = new RelayCommand(_ => { if (padCapture == act.Id) return; StartPadCapture(act.Id); }) });
                }
                if (ctx == "home")
                    rows.Add(new PickRow { Key = "homeback", Icon = "reply", Text = string.Format(Loc("LOC_KX_HomeBack", "B on the home screen: {0}"), HomeBackText),
                                           Sub = Loc("LOC_KX_HomeBackSub", "Where B (or Esc) takes the highlight: the very first game, the top bar, or nowhere"),
                                           Command = new RelayCommand(_ => { CycleHomeBack(); GRowsAgain("homeback"); }) });
                if (ctx == "hold")
                {
                    string which = !settings.GuideGoesHome ? Loc("LOC_KX_Off", "Off") : settings.HomeButton == 1 ? Loc("LOC_KX_ButtonSelectStart", "Select + Start") : Loc("LOC_KX_ButtonGuide", "Guide");
                    rows.Add(new PickRow { Key = "homebutton", Icon = "home", Text = Loc("LOC_KX_RmHomeButton", "Straight to the home screen"),
                                           Sub = string.Format(Loc("LOC_KX_RmButton", "Button: {0}"), which) + "   ·   " + Loc("LOC_KX_RmHomeButtonSub", "Guide, Select + Start or off"),
                                           Command = new RelayCommand(_ =>
                                           {
                                               if (!settings.GuideGoesHome) { settings.GuideGoesHome = true; settings.HomeButton = 0; }
                                               else if (settings.HomeButton == 0) settings.HomeButton = 1;
                                               else { settings.GuideGoesHome = false; settings.HomeButton = 0; }
                                               SaveSettings();
                                               PlaySound("move");
                                               foreach (string n in new[] { "GuideGoesHome", "HomeButtonTitle", "HomeButtonText" }) Notify(n);
                                               GRowsAgain("homebutton");
                                           }) });
                }
            }
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_RmFixedHeader", "PLAYNITE'S OWN (CAN'T CHANGE)") });
            Action<string, string, string> fixedRow = (k, t, s) => rows.Add(new PickRow { Key = k, IsInfo = true, Icon = "lock", Text = t, Sub = s });
            fixedRow("f.a", Loc("LOC_KX_RmFixedA", "A: choose, open"), Loc("LOC_KX_RmFixedASub", "A and B can be swapped in Playnite's own settings (Input)"));
            fixedRow("f.b", Loc("LOC_KX_RmFixedB", "B: back"), null);
            fixedRow("f.move", Loc("LOC_KX_RmFixedMove", "D-pad and left stick: move"), null);
            fixedRow("f.lt", Loc("LOC_KX_RmFixedLt", "LT / RT: a page at a time"), Loc("LOC_KX_RmFixedLtSub", "Playnite sends them as Page Up / Page Down keys"));
            fixedRow("f.guide", Loc("LOC_KX_RmFixedGuide", "Guide in a game: the in-game overlay"), Loc("LOC_KX_RmFixedGuideSub", "Its keyboard shortcut is in the overlay's settings"));
            if (SteamDevice.Connected)
                fixedRow("f.steam", Loc("LOC_KX_RmSteam", "Steam Controller: moved buttons work on it too"), Loc("LOC_KX_RmSteamSub", "Steam's own keyboard copies of each press are still taken out first"));
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecReset", "RESET") });
            rows.Add(new PickRow { Key = "padreset", Icon = "refresh", Text = Loc("LOC_KX_ResetThisPage", "Reset this page"), Sub = Loc("LOC_KX_ResetThisPageSub", "Resets this page to default"),
                                   Command = new RelayCommand(_ =>
                                   {
                                       if (settings.PadMap != null) settings.PadMap.Clear();
                                       settings.GuideGoesHome = true;
                                       settings.HomeButton = 0;
                                       settings.HomeBackTo = 0;
                                       remapHeld.Clear();
                                       SaveSettings();
                                       PlaySound("select");
                                       foreach (string n in new[] { "GuideGoesHome", "HomeButtonTitle", "HomeButtonText" }) Notify(n);
                                       GRowsAgain("padreset");
                                   }) });
        }

        private string PadContextHeader(string ctx)
        {
            switch (ctx)
            {
                case "home": return Loc("LOC_KX_RmCtxHome", "HOME SCREEN");
                case "game": return Loc("LOC_KX_RmCtxGame", "GAME PAGE");
                case "menus": return Loc("LOC_KX_RmCtxMenus", "MENUS AND PAGES");
                case "edit": return Loc("LOC_KX_RmCtxEdit", "FREE EDIT AND THE BOARD");
            }
            return Loc("LOC_KX_RmCtxHold", "HOLD");
        }

        /// <summary>What else answers to that button in the same place, or null.</summary>
        private string PadClash(PadAction a, ControllerInput b)
        {
            var other = PadActions.FirstOrDefault(x => !ReferenceEquals(x, a) && x.Context == a.Context && PadMapOf(x) == b);
            if (other != null) return string.Format(Loc("LOC_KX_RmClash", "also {0}"), PadActionName(other));
            if (a.Context == "hold") return null;   // (a held button still does its own thing when tapped)
            if (b == ControllerInput.A) return Loc("LOC_KX_RmClashA", "takes the place of A (choose / open)");
            if (b == ControllerInput.B) return Loc("LOC_KX_RmClashB", "takes the place of B (back)");
            return null;
        }

        /// <summary>X on an action: back to its own button.</summary>
        private bool PadButtonsKey(KeyEventArgs e, bool xPressed)
        {
            if (!xPressed || padCapture != null) return false;
            var fe = Keyboard.FocusedElement as FrameworkElement;
            var row = fe != null ? fe.DataContext as PickRow : null;
            if (row == null || row.Key == null || !row.Key.StartsWith("pad:", StringComparison.Ordinal)) return false;
            string id = row.Key.Substring(4);
            if (settings.PadMap != null && settings.PadMap.Remove(id)) { remapHeld.Clear(); SaveSettings(); PlaySound("move"); GRowsAgain(row.Key); }
            else PlaySound("back");
            return true;
        }

        // =====================================================================
        // Code-built settings pages (r12 G): Controller buttons, Handheld (with touch and mouse)
        // =====================================================================

        private ObservableCollection<PickRow> gRows;
        public ObservableCollection<PickRow> GRows { get { return gRows ?? (gRows = new ObservableCollection<PickRow>()); } }
        public bool IsOptionsGRows { get { return optionsPage == "padbuttons" || optionsPage == "handheld" || optionsPage == "sortshow"; } }
        public string GRowsIntro { get; private set; }
        public bool HasGRowsIntro { get { return !string.IsNullOrEmpty(GRowsIntro); } }
        private ICommand openPadButtonsCmd, openHandheldCmd;
        public ICommand OpenPadButtonsCommand { get { return openPadButtonsCmd ?? (openPadButtonsCmd = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("padbuttons"); })); } }
        public ICommand OpenHandheldCommand { get { return openHandheldCmd ?? (openHandheldCmd = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("handheld"); })); } }
        public string PadButtonsSummary
        {
            get
            {
                int n = settings.PadMap != null ? settings.PadMap.Count : 0;
                return n == 0 ? Loc("LOC_KX_RmSummaryNone", "Put Kippix's actions on the buttons you like") : string.Format(Loc("LOC_KX_RmSummary", "{0} moved"), n);
            }
        }

        /// <summary>Hook from OptionsTitle.</summary>
        private string GOptionsTitle(string page)
        {
            if (page == "padbuttons") return Loc("LOC_KX_RmTitle", "Controller buttons");
            if (page == "handheld") return Loc("LOC_KX_HhTitle", "Handheld, touch and mouse (WIP)");
            if (page == "sortshow") return Loc("LOC_KX_SortShow", "Sort and show");
            return null;
        }

        /// <summary>Hook from OptionsParent.</summary>
        private static string GOptionsParent(string page)
        {
            if (page == "padbuttons" || page == "handheld") return "general";
            return null;
        }

        /// <summary>Hook from SetOptionsPage.</summary>
        private void GOptionsOpened(string page)
        {
            if (padCapture != null && page != "padbuttons") EndPadCapture(null, true);
            Notify("IsOptionsGRows");
            if (page == "general") { Notify("PadButtonsSummary"); Notify("HandheldSummary"); }
            if (!IsOptionsGRows) return;
            GRows.Clear();
            var rows = new List<PickRow>();
            FillGRows(page, rows);
            foreach (var r in rows) GRows.Add(r);
            GRowsIntro = page == "handheld" ? HandheldIntro() : "";
            Notify("GRowsIntro");
            Notify("HasGRowsIntro");
            ui.BeginInvoke(new Action(() =>
            {
                if (!IsOptionsGRows) return;
                var first = GRows.FirstOrDefault(r => r.IsRow && !r.IsInfo);
                if (first != null) UiTools.FocusItem("KX_GRowList", first);
            }), DispatcherPriority.ContextIdle);
        }

        private void FillGRows(string page, List<PickRow> rows)
        {
            if (page == "padbuttons") BuildPadButtonRows(rows);
            else if (page == "handheld") BuildHandheldRows(rows);
            else if (page == "sortshow") BuildSortShowRows(rows);   // (ThemeApi.SortShow.cs)
        }

        /// <summary>The page again, rows changed where they stand (the highlight stays put), then on the row with <paramref name="focusKey"/>.</summary>
        private void GRowsAgain(string focusKey)
        {
            if (!IsOptionsGRows) return;
            var rows = new List<PickRow>();
            FillGRows(optionsPage, rows);
            if (rows.Count == GRows.Count)
                for (int i = 0; i < rows.Count; i++) GRows[i].BecomeLike(rows[i]);
            else
            {
                GRows.Clear();
                foreach (var r in rows) GRows.Add(r);
            }
            if (optionsPage == "handheld") { GRowsIntro = HandheldIntro(); Notify("GRowsIntro"); Notify("HasGRowsIntro"); }
            string key = focusKey;
            if (key == null) return;
            if (key.StartsWith("pad:", StringComparison.Ordinal) == false && PadActions.Any(a => a.Id == key)) key = "pad:" + key;
            ui.BeginInvoke(new Action(() =>
            {
                var row = GRows.FirstOrDefault(r => r.Key == key);
                var fe = Keyboard.FocusedElement as FrameworkElement;
                if (row != null && (fe == null || !ReferenceEquals(fe.DataContext, row))) UiTools.FocusItem("KX_GRowList", row);
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>Hook from HandlePanelKey (options open).</summary>
        private bool GOptionsKey(KeyEventArgs e, bool xPressed, bool back)
        {
            if (optionsPage == "padbuttons")
            {
                if (padCapture != null && back) { EndPadCapture(null, false); return true; }   // (Esc on the keyboard)
                return PadButtonsKey(e, xPressed);
            }
            return false;
        }

        /// <summary>Hook from NormalizeSettings.</summary>
        private void NormalizeGSettings()
        {
            if (settings.TipsSeen == null) settings.TipsSeen = new List<string>();
            if (settings.SafeModeSaved == null) settings.SafeModeSaved = new Dictionary<string, string>();
            if (settings.PadMap == null) settings.PadMap = new Dictionary<string, string>();
            if (settings.HandheldSaved == null) settings.HandheldSaved = new Dictionary<string, string>();
            settings.HandheldMode = Math.Max(0, Math.Min(2, settings.HandheldMode));
            settings.TouchMode = Math.Max(0, Math.Min(2, settings.TouchMode));
            foreach (var k in settings.PadMap.Keys.ToList())
            {
                ControllerInput b;
                if (!Enum.TryParse(settings.PadMap[k] ?? "", out b) || !RemapButtons.Contains(b)) settings.PadMap.Remove(k);
            }
        }
    }
}
