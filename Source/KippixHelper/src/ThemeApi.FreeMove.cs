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
    /// Edits › Free move: a pointer on the screen (D-pad or mouse). Point at any part of the screen or a widget:
    /// A picks it up to move it, X opens its settings, B is done. With the mouse: drag it and let go to drop it.
    /// While anything is being moved, it can also be dragged with the mouse (and widgets sized with the wheel).
    /// </summary>
    public partial class ThemeApi
    {
        public ICommand FreeMoveCommand { get; private set; }
        private bool freeMove;
        private double cursorX = 960, cursorY = 540, cursorSpeed = 14;
        private DateTime lastCursorKey = DateTime.MinValue, modeChangedAt = DateTime.MinValue;
        private string freeTarget, freeTargetName;   // element key, or "w:" + widget id
        private string freeOrigin;                    // the options page Free move was started from
        private string freeResumePage;                // X opened this page: Back from it returns to Free move
        private bool freeOpenedDetails;               // started from the game page settings: we opened a game page to edit
        private bool mouseHooked, dragging;
        private FrameworkElement freeCursorFrom;      // (0.93) the pointer starts in the middle of this (the tile A was held on)
        private Point dragStart, lastMouse = new Point(double.NaN, double.NaN);
        private double mouseTravel;   // (1.0.2) how far the mouse has moved since the edit started
        private double dragX0, dragY0;

        private void InitFreeMoveCommands()
        {
            FreeMoveCommand = new RelayCommand(p => StartFreeMove(p as string == "freemove-game", false));
        }

        public bool IsFreeCursor { get { return freeMove && masterMoving == null && widgetMoving == null; } }

        // (0.99.1) Editing works the same everywhere: the highlight jumps from part to part with the stick / D-pad (as on the board),
        // A picks it up, the grid shows behind. Start (Tab) switches to precise edit: the pointer, moved freely to pick anything up.
        private bool freePrecise;
        private bool freeStartPrecise;     // the next Free move starts as precise edit (Start on the board's editing)
        private bool freeFromBoardEdit;    // ... and B / Start go back to editing the board
        public bool FreeCursorShown { get { return IsFreeCursor && freePrecise; } }
        public bool FreeGridShown { get { return freeMove; } }   // (0.99.2: in precise edit too)

        /// <summary>The grid behind normal editing: a line every grid square (every few when the squares are tiny), brighter every fourth.</summary>
        public System.Windows.Media.Brush EditGridBrush
        {
            get
            {
                double g = Math.Max(1, settings.MasterGrid), step = g * Math.Ceiling(16 / g);
                double big = step * 4;
                var minor = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                var major = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                var group = new System.Windows.Media.DrawingGroup();
                var lines = new System.Windows.Media.GeometryGroup();
                for (int i = 1; i < 4; i++)
                {
                    lines.Children.Add(new System.Windows.Media.LineGeometry(new Point(i * step, 0), new Point(i * step, big)));
                    lines.Children.Add(new System.Windows.Media.LineGeometry(new Point(0, i * step), new Point(big, i * step)));
                }
                var edges = new System.Windows.Media.GeometryGroup();
                edges.Children.Add(new System.Windows.Media.LineGeometry(new Point(0, 0), new Point(big, 0)));
                edges.Children.Add(new System.Windows.Media.LineGeometry(new Point(0, 0), new Point(0, big)));
                group.Children.Add(new System.Windows.Media.GeometryDrawing(null, new System.Windows.Media.Pen(minor, 1), lines));
                group.Children.Add(new System.Windows.Media.GeometryDrawing(null, new System.Windows.Media.Pen(major, 1), edges));
                var brush = new System.Windows.Media.DrawingBrush(group)
                {
                    TileMode = System.Windows.Media.TileMode.Tile, Stretch = System.Windows.Media.Stretch.None,
                    Viewport = new Rect(0, 0, big, big), ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, big, big), ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute
                };
                brush.Freeze();
                return brush;
            }
        }

        private void NotifyFreeMode()
        {
            Notify("FreeCursorShown");
            Notify("FreeGridShown");
            Notify("MasterMoveName");
            Notify("MasterMoveText");
            Notify("IsFreeCursor");
        }
        public bool IsPlacing { get { return masterMoving != null || widgetMoving != null; } }

        private string FreeMoveText()
        {
            string note = HistoryNoteNow;
            if (note != null) return note;
            if (layerNote != null && freeTargetName != null && layerNoteTarget == freeTarget) return freeTargetName + "   ·   " + layerNote;
            if (!freePrecise)
                return freeTargetName != null ? freeTargetName : Loc("LOC_KX_FreeChoose", "Choose a part to move it");
            return freeTargetName != null ? string.Format(Loc("LOC_KX_FreePointing", "Pointing at: {0}"), freeTargetName)
                                          : Loc("LOC_KX_FreePoint", "Point at something to move it");
        }

        /// <summary>A, Enter, B, Esc straight after starting or ending something: the same press, seen twice. Ignored.</summary>
        private bool JustChanged { get { return (DateTime.Now - modeChangedAt).TotalMilliseconds < 180; } }

        // (0.99.50) Start switches between the board's editing and precise edit once per press: the press was being seen twice
        // (once to go in, once more to come straight back out). Let go of Start (or 1.5 s) and it works again.
        private bool startLatched;
        private DateTime startLatchedAt = DateTime.MinValue;
        private bool StartLatched { get { return startLatched && (DateTime.Now - startLatchedAt).TotalMilliseconds < 1500; } }
        private void LatchStart() { startLatched = true; startLatchedAt = DateTime.Now; }
        private void ModeChanged() { modeChangedAt = DateTime.Now; }

        // ---------------- starting and stopping ----------------

        /// <summary>game: from the game page settings (opens a game page to edit if none is showing). resume: back from a settings page X opened.</summary>
        private void StartFreeMove(bool game, bool resume)
        {
            if (!IsFullscreen || IsMasterMoving) return;
            bool opening = false;
            if (!resume)
            {
                freeFromHold = false;   // started from the settings (holding A sets it again just after)
                freeOrigin = optionsPage;
                freeOpenedDetails = false;
                if (game && !GameDetailsVisible()) opening = freeOpenedDetails = EnsureGameDetailsOpen();
            }
            freeResumePage = null;
            masterReturnPage = freeOrigin;
            freeMove = true;
            if (!resume) { freePrecise = freeStartPrecise; freeStartPrecise = false; }
            lastMouse = new Point(double.NaN, double.NaN);   // (1.0.2) the mouse has to really move in this edit to switch to precise
            mouseTravel = 0;
            EditBegin();   // (undo / redo: the layout as it is now)
            freeTarget = freeTargetName = null;
            ModeChanged();
            openPanel = "master";
            NotifyPanels();
            NotifyMove();
            UpdateTopBarFade();
            Action take = () =>
            {
                if (!freeMove) return;
                var canvas = UiTools.FindByName(Application.Current.MainWindow, "KX_MasterCanvas");
                if (!resume)
                {
                    cursorX = canvas != null && canvas.ActualWidth > 0 ? canvas.ActualWidth / 2 : 960;
                    cursorY = canvas != null && canvas.ActualHeight > 0 ? canvas.ActualHeight / 2 : 540;
                    var from = freeCursorFrom;
                    freeCursorFrom = null;
                    if (from != null && canvas != null && from.IsVisible && from.ActualWidth > 0)
                    {
                        try
                        {
                            var c = from.TransformToVisual(canvas).Transform(new Point(from.ActualWidth / 2, from.ActualHeight / 2));
                            cursorX = Math.Max(0, Math.Min(canvas.ActualWidth, c.X));
                            cursorY = Math.Max(0, Math.Min(canvas.ActualHeight, c.Y));
                        }
                        catch (Exception) { }
                    }
                }
                if (!TakeMasterKeys()) { EndFreeMove(null); return; }
                PlaceCursor();
                if (!freePrecise && !resume) { var at = FreeHitAt(cursorX, cursorY) ?? FreeNearestTo(cursorX, cursorY); FreeSnapTo(at, false); }
                UpdateFreeTarget();
                NotifyFreeMode();
            };
            if (!opening) { ui.BeginInvoke(take, DispatcherPriority.Loaded); return; }
            // the game page takes the focus as it opens: take the keys once it has
            var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (s, e) => { t.Stop(); take(); };
            t.Start();
        }

        /// <summary>Back to the options (to <paramref name="page"/>, or where Free move was started).</summary>
        private void EndFreeMove(string page)
        {
            if (!freeMove) return;
            if (page == null && freeFromBoardEdit) { FreeToBoardEdit(null); return; }   // (0.99.1: precise edit from the board's editing: back to it)
            freeFromBoardEdit = false;
            EditEnd();   // (the last change is a step; what's changed in the settings isn't)
            freeMove = false;
            freeResumePage = page;   // a settings page (X): Back from it comes back here
            if (page == null && freeOpenedDetails) { freeOpenedDetails = false; CloseGameDetails(false); }
            StopDrag();
            freeTarget = freeTargetName = null;
            ModeChanged();
            if (page == null && EndHoldFreeMove()) return;   // started by holding A: back to that screen
            openPanel = "options";
            NotifyPanels();
            SetOptionsPage(page ?? masterReturnPage ?? (detailsOpen ? "gamepage" : "home"), false);
            if (page == "widgetedit") NotifyWidgetEdit();
            NotifyMove();
            UpdateTopBarFade();
            ui.BeginInvoke(new Action(() =>
            {
                if (page != null || (!UiTools.FocusCommandParameter("KX_OptionsPanel", "freemove") && !UiTools.FocusCommandParameter("KX_OptionsPanel", "freemove-game"))) UiTools.FocusInside("KX_OptionsPanel");
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>The outline takes the keys (it's focused; its KeyDown runs before Playnite's own bindings) and the canvas the mouse.</summary>
        private bool TakeMasterKeys()
        {
            var win = Application.Current.MainWindow;
            var outline = UiTools.FindByName(win, "KX_MasterOutline");
            if (outline != null && !masterKeysHooked)
            {
                outline.KeyDown += OnMasterKeyDown;
                masterKeysHooked = true;
            }
            HookMasterMouse();
            HookRotateMouse();   // (a Steam Controller's trigger is a mouse click too: not a pick or a drop)
            if (outline == null) return false;
            outline.Focusable = true;
            outline.FocusVisualStyle = null;
            return ReferenceEquals(Keyboard.Focus(outline), outline) || outline.IsKeyboardFocused;
        }

        // ---------------- the pointer ----------------

        private void FreeMoveKey(string button, Key key, bool repeat)
        {
            if (button == null && !repeat && HistoryKeyboardKey(key)) return;   // Ctrl+Z / Ctrl+Y
            if (button == "LeftShoulder" || button == "RightShoulder") { EditUndo(button == "RightShoulder"); return; }   // LB undo, RB redo
            if (button == "Start") { if (!JustChanged && !StartLatched) { LatchStart(); FreeTogglePrecise(); } return; }   // (0.99.1) precise edit / back (0.99.50: once per press)
            if (button == null)
            {
                if (repeat && key != Key.Left && key != Key.Right && key != Key.Up && key != Key.Down) return;   // a held Enter / Esc: once
                if (!freePrecise && (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down)) { FreeSnapMove(key); return; }
                switch (key)
                {
                    case Key.PageUp: case Key.PageDown: UpdateFreeTarget(); LayerStep(freeTarget, key == Key.PageDown ? 1 : -1); return;   // (0.99.2: LT / RT, layer back / forward)
                    case Key.Tab: FreeTogglePrecise(); return;
                    case Key.Left: MoveCursor(-1, 0); return;
                    case Key.Right: MoveCursor(1, 0); return;
                    case Key.Up: MoveCursor(0, -1); return;
                    case Key.Down: MoveCursor(0, 1); return;
                    case Key.Enter: case Key.Space: if (!JustChanged) FreePick(); return;
                    case Key.Escape: case Key.Back: if (!JustChanged) EndFreeMove(null); return;
                    case Key.S: case Key.Apps: FreeSettings(); return;
                    case Key.Z: FreeResize(); return;
                    case Key.Delete: FreeDelete(); return;
                }
                return;
            }
            switch (button)
            {
                case "A": if (!JustChanged) FreePick(); return;
                case "B": if (!JustChanged) EndFreeMove(null); return;
                case "X": FreeSettings(); return;
                case "Y": FreeResize(); return;
                case "Back": FreeDelete(); return;
            }
        }

        /// <summary>D-pad: small steps for a tap, faster the longer you hold it.</summary>
        private void MoveCursor(int dx, int dy)
        {
            var now = DateTime.Now;
            cursorSpeed = (now - lastCursorKey).TotalMilliseconds < 220 ? Math.Min(70, cursorSpeed * 1.3) : 14;
            lastCursorKey = now;
            var canvas = UiTools.FindByName(Application.Current.MainWindow, "KX_MasterCanvas");
            double w = canvas != null && canvas.ActualWidth > 0 ? canvas.ActualWidth : 1920, h = canvas != null && canvas.ActualHeight > 0 ? canvas.ActualHeight : 1080;
            cursorX = Math.Max(0, Math.Min(w - 1, cursorX + dx * cursorSpeed));
            cursorY = Math.Max(0, Math.Min(h - 1, cursorY + dy * cursorSpeed));
            PlaceCursor();
            UpdateFreeTarget();
        }

        private void PlaceCursor()
        {
            var c = UiTools.FindByName(Application.Current.MainWindow, "KX_FreeCursor");
            if (c == null) return;
            Canvas.SetLeft(c, cursorX);
            Canvas.SetTop(c, cursorY);
        }

        /// <summary>Everything that can be moved on the screen showing now, with where it is (turned, when it's turned).</summary>
        private List<Tuple<string, string, TurnFrame>> FreeTargets()
        {
            var list = new List<Tuple<string, string, TurnFrame>>();
            var win = Application.Current.MainWindow;
            var canvas = UiTools.FindByName(win, "KX_MasterCanvas");
            if (canvas == null) return list;
            bool game = GameDetailsVisible();
            foreach (var el in MasterElements.Where(m => m.Screen == (game ? "game" : "home")))
            {
                var f = MasterFrame(el, canvas);
                if (f != null) list.Add(Tuple.Create(el.Key, Loc(el.LocKey, el.Fallback), f));
            }
            if (!game && ShowWidgets)
            {
                var items = UiTools.FindByName(win, "KX_Widgets") as ItemsControl;
                if (items != null)
                    foreach (var w in Widgets)
                    {
                        if (IsBoardMode && w.BoardRect.HasValue) continue;   // in a board tile: the tile is what moves
                        var f = WidgetFrame(w, canvas);
                        if (f != null) list.Add(Tuple.Create("w:" + w.Config.Id, w.Name, f));
                    }
            }
            if (!game && IsBoardMode)
            {
                // the board's tiles (and the board itself): picking one hands over to editing the board
                var board = UiTools.FindByName(win, "KX_Board");
                var host = UiTools.FindByName(win, "KX_BoardHost");
                if (host != null && host.IsVisible && host.ActualWidth > 0)
                {
                    // the board: round its tiles (the room it has is the whole screen's middle)
                    var box = Rect.Empty;
                    if (board != null && board.IsVisible)
                        foreach (var t in BoardTiles.ToList())
                        {
                            if (!t.IsCard || t.Width <= 0 || t.Height <= 0) continue;
                            var tf = TurnFrame.Of(board, new Rect(t.Left, t.Top, t.Width, t.Height), canvas, t.Rotation);
                            if (tf != null) box.Union(tf.Bounds());
                        }
                    var f = !box.IsEmpty ? OnScreen(TurnFrame.OfBox(box), canvas) : OnScreen(TurnFrame.Of(host, new Rect(0, 0, host.ActualWidth, host.ActualHeight), canvas, 0), canvas);
                    if (f != null) list.Add(Tuple.Create("board", Loc("LOC_KX_FreeTheBoard", "The board"), f));
                }
                if (board != null && board.IsVisible)
                    foreach (var t in BoardTiles.ToList())
                    {
                        if (!t.IsCard || t.Card == null || t.Width <= 0) continue;
                        var f = TurnFrame.Of(board, new Rect(t.Left, t.Top, t.Width, t.Height), canvas, t.Rotation);
                        if (f != null) list.Add(Tuple.Create("b:" + t.Card.Id, string.Format(Loc("LOC_KX_FreeTile", "{0}"), string.IsNullOrEmpty(t.Name) ? Loc("LOC_KX_ATile", "A tile") : t.Name), f));
                    }
            }
            return list;
        }

        /// <summary>
        /// What's pointed at can be deleted: things you added (widgets, board tiles), and the screen's parts that have a Show switch
        /// (they're hidden with it, so their settings bring them back; the button hints too, since 0.75). Not the screen's core: the
        /// games, the Play button and the buttons round it, the profile button, the shortcut bar.
        /// </summary>
        public bool FreeCanDelete
        {
            get { return IsFreeCursor && CanDeleteTarget(freeTarget); }
        }

        /// <summary>What's picked up (being moved or sized) can be deleted: View (Delete on the keyboard).</summary>
        public bool PickedCanDelete
        {
            get { return widgetMoving != null || (masterMoving != null && HideablePart(masterMoving.Key) != null); }
        }

        private static bool CanDeleteTarget(string t)
        {
            return t != null && (t.StartsWith("w:", StringComparison.Ordinal) || t.StartsWith("b:", StringComparison.Ordinal) || HideablePart(t) != null);
        }

        /// <summary>A screen part that "delete" hides (with its own Show setting): the game page part's key, or the element's own key. Null: it stays.</summary>
        private static string HideablePart(string key)
        {
            switch (key)
            {
                case "game.logo": return "logo";
                case "game.stats": case "game.facts": return "info";   // (one Show switch for both)
                case "game.desc": return "desc";
                case "game.media": return "media";
                case "game.friends": return "friends";
                case "game.clock": case "game.hints": case "home.logo": case "home.info": case "home.filter": case "home.hints":
                case "home.hltb": case "home.players": case "home.friends": case "home.progress": return key;   // (0.97: the parts under the game info too)
            }
            if (key != null && key.StartsWith("game.sec.", StringComparison.Ordinal)) return key.Substring(9);
            return null;
        }

        /// <summary>View button / Delete: deletes the widget or board tile pointed at, or hides the part (no question: it's easy to bring back).</summary>
        private void FreeDelete()
        {
            UpdateFreeTarget();
            string t = freeTarget;
            if (!FreeCanDelete) { PlaySound("back"); return; }
            if (t.StartsWith("w:", StringComparison.Ordinal))
            {
                var w = Widgets.FirstOrDefault(x => "w:" + x.Config.Id == t);
                if (w == null) { PlaySound("back"); return; }
                RemoveWidget(w);
                DeletedNotice(Loc("LOC_KX_DeletedHead", "DELETED"), w.Name, Loc("LOC_KX_WidgetBackHow", "Add it again: Settings › Home settings › Widgets"));
            }
            else if (t.StartsWith("b:", StringComparison.Ordinal))
            {
                var card = BCards != null ? BCards.FirstOrDefault(c => "b:" + c.Id == t) : null;
                if (card == null) { PlaySound("back"); return; }
                if (card.Kind == "widget" && card.Target != null)
                {
                    var w = Widgets.FirstOrDefault(x => x.Config.Id == card.Target);
                    if (w != null) w.SetBoard(null, false);   // the widget itself stays (it's back where it was before it went in the tile)
                }
                BCards.Remove(card);
                BoardChanged(IsGameKind(card));
            }
            else { HideScreenPart(t); return; }
            PlaySound("back");
            freeTarget = freeTargetName = null;
            Notify("FreeCanDelete");
            // the screen has changed: what's under the pointer now
            ui.BeginInvoke(new Action(() => { if (IsFreeCursor) { UpdateFreeTarget(); Notify("MasterMoveText"); } }), DispatcherPriority.ContextIdle);
        }

        /// <summary>A widget gone: from the settings, the screen, and any board tile that showed it.</summary>
        private void RemoveWidget(WidgetItem w)
        {
            settings.Widgets.RemoveAll(c => c.Id == w.Config.Id);
            Widgets.Remove(w);
            if (ReferenceEquals(editingWidget, w)) editingWidget = null;
            // a board tile that showed it goes too (in the Board, Manual and free placement alike)
            string wid = w.Config.Id;
            foreach (var cards in new[] { settings.BoardCards, settings.ManualCards, settings.FreeCards })
                if (cards != null) cards.RemoveAll(c => c != null && c.Kind == "widget" && c.Target == wid);
            SaveSettings();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
            if (IsBoardMode) BoardChanged(false);
        }

        /// <summary>
        /// View (or Delete) while something is picked up: a widget is deleted; a screen part that has a Show switch is put back where
        /// it was picked up from and hidden (its settings bring it back, in the same place and size). No question: it's easy to
        /// bring back, and the card that comes up says how.
        /// </summary>
        private void PickedDelete()
        {
            if (widgetMoving != null)
            {
                var w = widgetMoving;
                bool wasNew = widgetIsNew;
                StopDrag();
                widgetMoving = null;
                RemoveWidget(w);
                PlaySound("back");
                if (!wasNew) DeletedNotice(Loc("LOC_KX_DeletedHead", "DELETED"), w.Name, Loc("LOC_KX_WidgetBackHow", "Add it again: Settings › Home settings › Widgets"));
                if (freeMove) { BackToFreeCursor(); return; }
                // placed from its settings page: that page has gone with it, back to the widgets
                ModeChanged();
                NotifyMove();
                openPanel = "options";
                NotifyPanels();
                editingWidget = null;
                SetOptionsPage("widgets", false);
                NotifyWidgetEdit();
                UpdateTopBarFade();
                ui.BeginInvoke(new Action(() => UiTools.FocusInside("KX_OptionsPanel")), DispatcherPriority.ContextIdle);
                return;
            }
            var el = masterMoving;
            if (el == null || HideablePart(el.Key) == null) { PlaySound("back"); return; }
            string key = el.Key;
            if (sizing) EndSizing(false); else EndMasterMove(false);   // (back where it was first: it comes back there if it's shown again)
            HideScreenPart(key);
        }

        /// <summary>Hides a screen part with its own Show setting (Free move's "delete").</summary>
        private void HideScreenPart(string key)
        {
            string part = HideablePart(key);
            if (part == null) { PlaySound("back"); return; }
            var el = MasterElements.FirstOrDefault(m => m.Key == key);
            string name = el != null ? Loc(el.LocKey, el.Fallback) : key;
            KeepRoomOf(part);   // the parts round it stay where they are (its room is kept), now and next time
            string how;
            switch (part)
            {
                case "home.hints": ShowButtonHints = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "game.hints": DetShowHints = false; how = Loc("LOC_KX_DetHintsBackHow", "Bring it back: Settings › Game page › Buttons & extras › Button hints"); break;
                case "home.logo": ShowLogo = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "home.info": ShowGameInfo = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "home.filter": ShowFilterLabel = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "home.hltb": HomeHltb = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "home.players": InfoPlayers = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "home.friends": InfoFriends = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "home.progress": HomeProgress = false; how = Loc("LOC_KX_HomePartBackHow", "Bring it back: Settings › Home settings › Logo, game info & button hints"); break;
                case "game.clock": DetShowDate = false; how = Loc("LOC_KX_ClockBackHow", "Bring it back: Settings › Game page › Buttons & extras"); break;
                default:
                    if (part == "info") name = DetPartName(part);   // (the play time and the game's details go together)
                    SetDetPartOn(part, false);
                    how = Loc("LOC_KX_PartBackHow", "Bring it back: Settings › Game page › Top and lower page");
                    break;
            }
            SyncFreeGaps();   // (its room straight away, before the page is drawn without it)
            PlaySound("back");
            DeletedNotice(Loc("LOC_KX_HiddenHead", "HIDDEN"), name, how);
            freeTarget = freeTargetName = null;
            Notify("FreeCanDelete");
            NotifyMove();
            ui.BeginInvoke(new Action(() => { if (IsFreeCursor) { UpdateFreeTarget(); Notify("MasterMoveText"); } }), DispatcherPriority.ContextIdle);
        }

        private void DeletedNotice(string head, string name, string how)
        {
            try { ShowNotice(head, name, how, "", null); } catch (Exception) { }
        }

        // ---------------- a part deleted here keeps its room (0.75) ----------------
        //
        // Deleting a part hides it with its Show switch, and a hidden part takes no room: the parts after it in the same line or
        // column (the lower page's cards, the info column, the home screen's logo / info / hints) slid into its place. Now the
        // room it had is kept: an empty, see-through placeholder of its size goes where it was (MasterSizes "<key>#gap" =
        // [width, height, 0, 0], so it's saved with the layout, profiles and saved layouts, and the layout resets clear it).
        // Its Show switch on again (its settings) gives the room back to the part itself. A card that has nothing to show for a
        // game (no play history, no HowLongToBeat times...) took no room for that game before either, so its placeholder doesn't.

        private static string GapKey(string key) { return key + "#gap"; }

        private readonly Dictionary<string, Border> freeGaps = new Dictionary<string, Border>();
        private bool syncingGaps;

        /// <summary>Its line or column (a placeholder can sit in it): a panel the theme lays out, not a grid, a canvas or a list's own panel.</summary>
        private static Panel GapParent(FrameworkElement fe)
        {
            var p = (System.Windows.Media.VisualTreeHelper.GetParent(fe) ?? fe.Parent) as Panel;
            if (p == null || p.IsItemsHost) return null;
            return p is StackPanel || p is WrapPanel || p is DockPanel ? p : null;
        }

        /// <summary>The parts with this Show switch, just before it goes off: the room each takes now is kept.</summary>
        private void KeepRoomOf(string part)
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                win.UpdateLayout();   // (as it is now: a move or a size cancelled a moment ago is back in place)
                if (settings.MasterSizes == null) settings.MasterSizes = new Dictionary<string, double[]>();
                foreach (var m in MasterElements)
                {
                    if (HideablePart(m.Key) != part) continue;
                    var fe = UiTools.FindByName(win, m.Names[0]);
                    if (fe == null || !fe.IsVisible || GapParent(fe) == null) continue;
                    var d = fe.DesiredSize;   // (its margins too; a part sized like a widget: the room it keeps, not what it draws)
                    if (d.Width < 1 || d.Height < 1) continue;
                    settings.MasterSizes[GapKey(m.Key)] = new[] { Math.Round(d.Width, 1), Math.Round(d.Height, 1), 0.0, 0.0 };
                }
            }
            catch (Exception) { }
        }

        /// <summary>The part's own Show switch.</summary>
        private bool PartSwitchOn(string part)
        {
            switch (part)
            {
                case "home.logo": return settings.ShowLogo;
                case "home.info": return settings.ShowGameInfo;
                case "home.filter": return settings.ShowFilterLabel;
                case "home.hltb": return settings.HomeHltb;
                case "home.players": return settings.InfoPlayers;
                case "home.friends": return settings.InfoFriends;
                case "home.progress": return settings.HomeProgress;
                case "home.hints": return settings.ShowButtonHints;
                case "game.hints": return settings.DetShowHints;
                case "game.clock": return settings.DetShowDate;
            }
            return DetPartOn(part);
        }

        /// <summary>Would the part show (take room) for this game if it were switched on?</summary>
        private bool GapWouldShow(string key)
        {
            switch (key)
            {
                // (0.99.51) the lower page's parts keep their place on every game, with or without anything for it (ThemeApi.DetBlanks.cs)
                case "game.sec.saves": return LudusaviInstalled;
                case "game.friends": return detFriends != null && detFriends.Count > 0;
            }
            return true;
        }

        private static bool SameSize(double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.IsNaN(a) && double.IsNaN(b);
            return Math.Abs(a - b) < 0.5;
        }

        private static void DropGap(Border ph)
        {
            var p = ph != null ? ph.Parent as Panel : null;
            if (p != null) p.Children.Remove(ph);
        }

        /// <summary>
        /// Every kept room in its place: right before its part (which is hidden, so the placeholder sits exactly where it was),
        /// following it when it moves to the other page of the game page. Called when the screen's parts are placed again.
        /// </summary>
        private void SyncFreeGaps()
        {
            if (syncingGaps) return;
            syncingGaps = true;
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                bool dropped = false;
                foreach (var m in MasterElements)
                {
                    string gk = GapKey(m.Key);
                    double[] g = null;
                    if (settings.MasterSizes != null) settings.MasterSizes.TryGetValue(gk, out g);
                    Border ph;
                    freeGaps.TryGetValue(m.Key, out ph);
                    string part = HideablePart(m.Key);
                    if (g != null && (part == null || PartSwitchOn(part) || g.Length < 2))
                    {
                        settings.MasterSizes.Remove(gk);   // shown again: the part has its own room back
                        g = null;
                        dropped = true;
                    }
                    if (MasterAway(m)) continue;   // (its placeholder stays as it is)
                    FrameworkElement fe = g != null ? MasterFind(win, m.Names[0]) : null;
                    Panel parent = fe != null ? GapParent(fe) : null;
                    if (parent == null || !GapWouldShow(m.Key)) { DropGap(ph); continue; }
                    if (ph == null)
                    {
                        ph = new Border { IsHitTestVisible = false, Focusable = false, Tag = "bbgap" };
                        freeGaps[m.Key] = ph;
                    }
                    bool across = parent is WrapPanel || (parent is StackPanel && ((StackPanel)parent).Orientation == Orientation.Horizontal);
                    double w = across ? Math.Max(0, g[0]) : double.NaN, h = Math.Max(0, g[1]);
                    if (!SameSize(ph.Width, w)) ph.Width = w;
                    if (!SameSize(ph.Height, h)) ph.Height = h;
                    ph.HorizontalAlignment = across ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                    if (parent is DockPanel) DockPanel.SetDock(ph, DockPanel.GetDock(fe));
                    var cur = ph.Parent as Panel;
                    if (!ReferenceEquals(cur, parent) || parent.Children.IndexOf(ph) != parent.Children.IndexOf(fe) - 1)
                    {
                        if (cur != null) cur.Children.Remove(ph);
                        parent.Children.Insert(parent.Children.IndexOf(fe), ph);
                    }
                }
                if (dropped) SaveSettings();
            }
            catch (Exception ex) { logger.Warn("Kippix: kept room of deleted parts: " + ex.Message); }
            finally { syncingGaps = false; }
        }

        /// <summary>The board's own editing takes over from Free move (the board moves on its grid, not freely).</summary>
        private void FreeToBoardEdit(string target)
        {
            BoardTile pick = null;
            if (target != null && target.StartsWith("b:", StringComparison.Ordinal))
                pick = BoardTiles.FirstOrDefault(x => x.IsCard && x.Card != null && "b:" + x.Card.Id == target);
            freeMove = false;
            freeResumePage = null;
            freeOpenedDetails = false;
            freeFromHold = false;
            freeFromBoardEdit = false;
            freePrecise = false;
            StopDrag();
            freeTarget = freeTargetName = null;
            ModeChanged();
            openPanel = null;
            NotifyPanels();
            NotifyMove();
            UpdateTopBarFade();
            string id = pick != null ? pick.Card.Id : null;
            ui.BeginInvoke(new Action(() =>
            {
                var again = id != null ? BoardTiles.FirstOrDefault(x => x.IsCard && x.Card != null && x.Card.Id == id) : null;
                BoardStartEdit(again);
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>What the pointer is over (the smallest thing under it, so a widget wins over the row behind it), outlined.</summary>
        private void UpdateFreeTarget()
        {
            if (!IsFreeCursor) return;
            try
            {
                Tuple<string, string, TurnFrame> hit;
                if (!freePrecise)
                {
                    // normal editing: the highlighted part stays highlighted (wherever it's gone); gone: the nearest one
                    var all = FreeTargets();
                    hit = all.FirstOrDefault(t => t.Item1 == freeTarget) ?? FreeNearestTo(cursorX, cursorY, all);
                    if (hit != null) { var c = hit.Item3.Bounds(); cursorX = c.X + c.Width / 2; cursorY = c.Y + c.Height / 2; PlaceCursor(); }
                }
                else hit = FreeHitAt(cursorX, cursorY);
                string before = freeTarget;
                freeTarget = hit != null ? hit.Item1 : null;
                freeTargetName = hit != null ? hit.Item2 : null;
                var outline = UiTools.FindByName(Application.Current.MainWindow, "KX_MasterOutline");
                if (outline != null)
                {
                    if (hit == null) outline.Opacity = 0;
                    else
                    {
                        outline.Opacity = 0.75;
                        PlaceOutline(outline, hit.Item1 == "home.logo" ? LogoLineFrame(hit.Item3) : hit.Item3);   // (0.88: the logo's line)
                    }
                }
                if (before != freeTarget) { sizeRefusedFor = null; layerNote = null; Notify("MasterMoveText"); Notify("FreeCanDelete"); Notify("FreeCanLayer"); if (freeTarget != null) PlaySound("move"); }
                UpdateDetBlankMarks();
            }
            catch (Exception) { }
        }

        /// <summary>What's under a point (the smallest thing there, so a widget wins over the row behind it).</summary>
        private Tuple<string, string, TurnFrame> FreeHitAt(double x, double y)
        {
            return FreeTargets().Where(t => t.Item3.Contains(new Point(x, y)))
                                .OrderBy(t => t.Item3.W * t.Item3.H).ThenBy(t => t.Item1 == "board" ? 1 : 0).FirstOrDefault();   // (a board of one tile: the tile)
        }

        private Tuple<string, string, TurnFrame> FreeNearestTo(double x, double y) { return FreeNearestTo(x, y, FreeTargets()); }

        private static Tuple<string, string, TurnFrame> FreeNearestTo(double x, double y, List<Tuple<string, string, TurnFrame>> all)
        {
            Tuple<string, string, TurnFrame> best = null;
            double bestD = double.MaxValue;
            foreach (var t in all)
            {
                if (t.Item1 == "board" && all.Count > 1) continue;
                var b = t.Item3.Bounds();
                double dx = Math.Max(0, Math.Max(b.Left - x, x - b.Right)), dy = Math.Max(0, Math.Max(b.Top - y, y - b.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        /// <summary>Normal editing: the highlight on this part (the pointer, hidden, follows it to its middle).</summary>
        private void FreeSnapTo(Tuple<string, string, TurnFrame> t, bool sound)
        {
            if (t == null) return;
            var b = t.Item3.Bounds();
            cursorX = b.X + b.Width / 2;
            cursorY = b.Y + b.Height / 2;
            string before = freeTarget;
            freeTarget = t.Item1;
            freeTargetName = t.Item2;
            PlaceCursor();
            var outline = UiTools.FindByName(Application.Current.MainWindow, "KX_MasterOutline");
            if (outline != null) { outline.Opacity = 0.75; PlaceOutline(outline, t.Item1 == "home.logo" ? LogoLineFrame(t.Item3) : t.Item3); }
            UpdateDetBlankMarks();
            if (before != freeTarget) { sizeRefusedFor = null; layerNote = null; Notify("MasterMoveText"); Notify("FreeCanDelete"); Notify("FreeCanLayer"); if (sound) PlaySound("move"); }
        }

        public bool FreeCanLayer { get { return IsFreeCursor && CanLayer(freeTarget); } }

        /// <summary>The stick / D-pad in normal editing: to the nearest part that way (as on the board).</summary>
        private void FreeSnapMove(Key dir)
        {
            var all = FreeTargets();
            var cur = all.FirstOrDefault(t => t.Item1 == freeTarget);
            if (cur == null) { FreeSnapTo(FreeNearestTo(cursorX, cursorY, all), true); return; }
            Rect a = cur.Item3.Bounds();
            double acx = a.X + a.Width / 2, acy = a.Y + a.Height / 2;
            Tuple<string, string, TurnFrame> best = null;
            double bestScore = double.MaxValue;
            foreach (var t in all)
            {
                if (ReferenceEquals(t, cur) || t.Item1 == "board") continue;
                Rect b = t.Item3.Bounds();
                if (b.Contains(a) && b.Width * b.Height > a.Width * a.Height * 1.5) continue;   // (what it sits in isn't "that way")
                double bcx = b.X + b.Width / 2, bcy = b.Y + b.Height / 2, gap, side;
                switch (dir)
                {
                    case Key.Right:
                        if (bcx <= acx + 4) continue;
                        gap = Math.Max(0, b.Left - a.Right); side = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom)) + Math.Abs(bcy - acy) * 0.25; break;
                    case Key.Left:
                        if (bcx >= acx - 4) continue;
                        gap = Math.Max(0, a.Left - b.Right); side = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom)) + Math.Abs(bcy - acy) * 0.25; break;
                    case Key.Down:
                        if (bcy <= acy + 4) continue;
                        gap = Math.Max(0, b.Top - a.Bottom); side = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right)) + Math.Abs(bcx - acx) * 0.25; break;
                    default:
                        if (bcy >= acy - 4) continue;
                        gap = Math.Max(0, a.Top - b.Bottom); side = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right)) + Math.Abs(bcx - acx) * 0.25; break;
                }
                double score = gap + side * 2;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best == null) { PlaySound("back"); return; }
            FreeSnapTo(best, true);
        }

        /// <summary>Start (Tab): precise edit (the pointer) / back to normal editing. Started from the board's editing: back to that.</summary>
        private void FreeTogglePrecise()
        {
            if (!IsFreeCursor) return;
            PlaySound("move");
            ModeChanged();
            if (freePrecise && freeFromBoardEdit) { FreeToBoardEdit(freeTarget); return; }
            freePrecise = !freePrecise;
            if (freePrecise) { PlaceCursor(); UpdateFreeTarget(); }
            else FreeSnapTo(FreeHitAt(cursorX, cursorY) ?? FreeNearestTo(cursorX, cursorY), false);
            NotifyFreeMode();
        }

        /// <summary>(0.99.1) Start while editing the board (nothing picked up): precise edit, the pointer on the highlighted tile.</summary>
        private void BoardToPrecise()
        {
            var at = Keyboard.FocusedElement as FrameworkElement;
            PlaySound("move");
            BoardEndEdit(false);
            freeCursorFrom = at;
            freeStartPrecise = true;
            StartFreeMove(false, false);
            freeFromBoardEdit = true;
            freeHoldFocus = at;
            freeFromHold = true;   // (B there comes back to the board's editing; this keeps the settings shut)
        }

        private void FreePick()
        {
            UpdateFreeTarget();
            string t = freeTarget;
            if (t == null) { PlaySound("back"); return; }
            PlaySound("select");
            ModeChanged();
            if (t.StartsWith("b:", StringComparison.Ordinal) && freePrecise) { boardPreciseFromBoard = freeFromBoardEdit; FreeToBoardEdit(t); boardPrecise = true; return; }   // (0.99.2) freely, like the screen's parts
            if (t == "board" || t.StartsWith("b:", StringComparison.Ordinal)) { FreeToBoardEdit(t); return; }
            if (t.StartsWith("w:", StringComparison.Ordinal))
            {
                var w = Widgets.FirstOrDefault(x => "w:" + x.Config.Id == t);
                if (w != null) StartWidgetMove(w, false);
                return;
            }
            var el = MasterElements.FirstOrDefault(m => m.Key == t);
            if (el != null) StartMasterMove(el);
        }

        /// <summary>X: the settings for what you're pointing at (a widget's own page, or the page with that part's set spots).</summary>
        private void FreeSettings()
        {
            UpdateFreeTarget();
            string t = freeTarget;
            if (t == null) { PlaySound("back"); return; }
            PlaySound("select");
            if (t.StartsWith("w:", StringComparison.Ordinal))
            {
                editingWidget = Widgets.FirstOrDefault(x => "w:" + x.Config.Id == t);
                EndFreeMove(editingWidget != null ? "widgetedit" : null);
                return;
            }
            if (t.StartsWith("b:", StringComparison.Ordinal)) { EndFreeMove("board"); return; }
            string page;
            switch (t)
            {
                case "home.cards": page = "cards"; break;
                case "board": page = "board"; break;
                case "home.topbar": case "home.shortcuts": page = "topbar"; break;
                case "home.profile":
                    // its settings live in the profile menu (Your name and picture)
                    EndFreeMove(null);
                    ClosePanels();
                    ui.BeginInvoke(new Action(() => { ToggleProfile(); SetProfilePage("me"); }), DispatcherPriority.Loaded);
                    return;
                case "home.filter": case "home.hltb": case "home.players": case "home.friends": case "home.progress": page = "cardinfo"; break;
                case "home.logo": case "home.info": case "home.hints": page = "cardinfo"; break;
                case "game.logo": detPartKey = "logo"; page = "detpart"; break;     // show it, send it to the lower page...
                case "game.media": detPartKey = "media"; page = "detpart"; break;
                case "game.stats": case "game.facts": detPartKey = "info"; page = "detpart"; break;
                case "game.desc": detPartKey = "desc"; page = "detpart"; break;
                case "game.friends": detPartKey = "friends"; page = "detpart"; break;
                case "game.buttons": case "game.hints": page = "gameextras"; break;
                case "game.info": page = "gamesections"; break;
                default:
                    if (t.StartsWith("game.sec.", StringComparison.Ordinal)) { detPartKey = t.Substring(9); page = "detpart"; break; }
                    page = "gamelayout";
                    break;
            }
            EndFreeMove(page);
        }

        /// <summary>A move started from Free move has ended: back to the pointer.</summary>
        private void BackToFreeCursor()
        {
            EditCheckpoint();   // what was just put down (or deleted) is an undo step
            ModeChanged();
            NotifyMove();
            ui.BeginInvoke(new Action(() =>
            {
                if (!freeMove) return;
                TakeMasterKeys();
                PlaceCursor();
                UpdateFreeTarget();
                Notify("MasterMoveText");
            }), DispatcherPriority.Loaded);
        }

        // ---------------- the mouse ----------------

        private void HookMasterMouse()
        {
            if (mouseHooked) return;
            var canvas = UiTools.FindByName(Application.Current.MainWindow, "KX_MasterCanvas");
            if (canvas == null) return;
            mouseHooked = true;
            canvas.MouseMove += (s, e) =>
            {
                var p = e.GetPosition(canvas);
                // WPF also sends MouseMove after a layout change with the mouse still: only real moves move the pointer
                // (1.0.2) the first message only says where the mouse is (it used to count as a move, so every edit started
                // in precise edit); then it takes a real move (about a finger's width) to switch, not a nudge of the desk
                if (double.IsNaN(lastMouse.X)) { lastMouse = p; return; }
                double step = Math.Abs(p.X - lastMouse.X) + Math.Abs(p.Y - lastMouse.Y);
                lastMouse = p;
                if (step <= 0.5) return;
                if (dragging) { DragTo(p); return; }
                if (!IsFreeCursor) return;
                if (!freePrecise)
                {
                    mouseTravel += step;
                    if (mouseTravel < 40) return;
                    freePrecise = true; NotifyFreeMode();   // (0.99.1: the mouse points: precise edit)
                }
                cursorX = p.X;
                cursorY = p.Y;
                PlaceCursor();
                UpdateFreeTarget();
            };
            canvas.MouseLeftButtonDown += (s, e) =>
            {
                var p = e.GetPosition(canvas);
                e.Handled = true;
                if (IsFreeCursor)
                {
                    cursorX = p.X;
                    cursorY = p.Y;
                    UpdateFreeTarget();
                    if (freeTarget == null) return;
                    FreePick();
                    if (!IsPlacing) return;   // (then dragged: let go to drop it)
                }
                if (!IsPlacing || sizing) return;   // sizing: the D-pad only
                BeginDrag(p);
                canvas.CaptureMouse();
            };
            canvas.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging) return;
                e.Handled = true;
                StopDrag();
                // let go: it stays there (with only a mouse there's no other way to keep it)
                if (masterMoving != null) EndMasterMove(true);
                else if (widgetMoving != null) EndWidgetMove(true);
            };
            canvas.MouseRightButtonUp += (s, e) =>
            {
                e.Handled = true;
                StopDrag();
                if (sizing && masterMoving != null) EndSizing(false);
                else if (masterMoving != null) EndMasterMove(false);
                else if (widgetMoving != null) EndWidgetMove(false);
                else if (freeMove && !JustChanged) EndFreeMove(null);
            };
            canvas.MouseWheel += (s, e) =>
            {
                if (widgetMoving == null) return;
                e.Handled = true;
                WidgetResize(e.Delta > 0 ? 1 : -1, false);
            };
            canvas.LostMouseCapture += (s, e) => { dragging = false; };
        }

        private void BeginDrag(Point p)
        {
            dragStart = p;
            if (masterMoving != null) { var o = MasterOffset(masterMoving.Key); dragX0 = o[0]; dragY0 = o[1]; }
            else if (widgetMoving != null) { dragX0 = widgetMoving.Config.X; dragY0 = widgetMoving.Config.Y; }
            else return;
            dragging = true;
        }

        private void StopDrag()
        {
            bool was = dragging;
            dragging = false;
            if (!was) return;
            var canvas = UiTools.FindByName(Application.Current.MainWindow, "KX_MasterCanvas");
            if (canvas != null && canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
        }

        /// <summary>Follows the mouse; on the grid when Snap to a grid is on.</summary>
        private void DragTo(Point p)
        {
            double x = dragX0 + (p.X - dragStart.X), y = dragY0 + (p.Y - dragStart.Y);
            if (settings.MasterSnap)
            {
                double g = Math.Max(1, settings.MasterGrid);
                x = Math.Round(x / g) * g;
                y = Math.Round(y / g) * g;
            }
            if (masterMoving != null)
            {
                var el = masterMoving;
                SetMasterOffset(el.Key, Math.Max(-1900, Math.Min(1900, x)), Math.Max(-1060, Math.Min(1060, y)));
                ApplyMasterOffsets();
                if (el.Key == "home.cards") ApplyHomeLayoutSoon();
            }
            else if (widgetMoving != null)
            {
                var w = widgetMoving;
                var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets");
                double sw = layer != null && layer.ActualWidth > 0 ? layer.ActualWidth : 1920;
                w.Config.X = Math.Max(-w.Width / 2, Math.Min(sw - w.Width / 2, x));
                w.Config.Y = Math.Max(-w.Height / 2, Math.Min((layer != null && layer.ActualHeight > 0 ? layer.ActualHeight : 1080) - w.Height / 2, y));
                w.Place();
            }
            else return;
            Notify("MasterMoveText");
            ui.BeginInvoke(new Action(UpdateMasterOutline), DispatcherPriority.Render);
        }
    }
}
