using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>
    /// Turning what's picked up: LT turns it anticlockwise, RT clockwise (Q / E on a keyboard). Works on a part of the screen
    /// in Free move (moving or sizing it), a widget, and a tile on the Board / Manual board. A tap turns it 5°, holding turns
    /// it about 90° a second; with Snap to a grid on it settles on 0 / 90 / 180 / 270 when it's within 3° of one. Y (put it
    /// back) makes it straight again, B puts back the turn it had.
    /// Playnite doesn't send the triggers as presses, so they're read from the controller on a short timer that only runs
    /// while something is picked up. Steam's desktop layout also turns the Steam Controller's triggers into mouse clicks:
    /// those are swallowed in Free move so they don't pick, drop or cancel anything.
    /// </summary>
    public partial class ThemeApi
    {
        private const double TurnTap = 5, TurnPerSecond = 90, TurnSnap = 3;
        private const short TriggerOn = 9830;      // 30 % of the trigger: a press
        private const short TriggerClick = 2600;   // 8 %: enough for Steam to have sent a click
        private const int TurnHoldMs = 300;        // held this long: turning smoothly
        private const int ClickQuietMs = 400;      // clicks this soon after a trigger are the trigger's

        private DispatcherTimer turnTimer;
        private bool ltDown, rtDown, turnMouseHooked;
        private DateTime ltAt, rtAt, turnTickAt, triggerSeenAt = DateTime.MinValue;
        private double rotRaw;   // while a trigger is held: the turn before snapping (so the snap is a notch, not a wall)
        private readonly HashSet<System.Windows.Input.MouseButton> swallowedDowns = new HashSet<System.Windows.Input.MouseButton>();
        private readonly Dictionary<FrameworkElement, RotateTransform> masterRotates = new Dictionary<FrameworkElement, RotateTransform>();

        // ---------------- the angle ----------------

        /// <summary>-180..180, to a tenth of a degree (0 for nonsense).</summary>
        internal static double NormAngle(double a)
        {
            if (double.IsNaN(a) || double.IsInfinity(a)) return 0;
            a %= 360;
            if (a > 180) a -= 360;
            if (a < -180) a += 360;
            return Math.Round(a, 1);
        }

        /// <summary>With Snap to a grid on: 0 / 90 / 180 / 270 when it's within 3° of one.</summary>
        private double SnapAngle(double a)
        {
            a = NormAngle(a);
            if (!settings.MasterSnap) return a;
            double q = Math.Round(a / 90) * 90;
            return Math.Abs(a - q) <= TurnSnap ? NormAngle(q) : a;
        }

        /// <summary>"   ·   rotated 15°" for the line of text while moving (nothing when it's straight).</summary>
        private string TurnSuffix(double a)
        {
            a = NormAngle(a);
            if (Math.Abs(a) < 0.05) return "";
            return "   ·   " + string.Format(Loc("LOC_KX_TurnedBy", "rotated {0}°"), Math.Round(a).ToString(CultureInfo.CurrentCulture));
        }

        private double MasterRotation(string key)
        {
            double a;
            return settings.MasterRotations != null && settings.MasterRotations.TryGetValue(key, out a) ? NormAngle(a) : 0;
        }

        private void SetMasterRotation(string key, double a)
        {
            if (settings.MasterRotations == null) settings.MasterRotations = new Dictionary<string, double>();
            a = NormAngle(a);
            if (Math.Abs(a) < 0.05) settings.MasterRotations.Remove(key);
            else settings.MasterRotations[key] = a;
        }

        /// <summary>Some part is turned (or was: its turn has to come off).</summary>
        private bool HasMasterTurns { get { return masterRotates.Count > 0 || (settings.MasterRotations != null && settings.MasterRotations.Count > 0); } }

        /// <summary>Settings loaded: the turns kept in range, straight ones dropped.</summary>
        private void NormalizeTurns()
        {
            if (settings.MasterRotations == null) settings.MasterRotations = new Dictionary<string, double>();
            foreach (var k in settings.MasterRotations.Keys.ToList())
            {
                double a = NormAngle(settings.MasterRotations[k]);
                if (Math.Abs(a) < 0.05) settings.MasterRotations.Remove(k); else settings.MasterRotations[k] = a;
            }
        }

        // ---------------- turning the screen's parts ----------------

        /// <summary>Every part's turn (with the offsets and sizes).</summary>
        private void ApplyMasterRotations()
        {
            if (!HasMasterTurns) return;
            foreach (var el in MasterElements) if (!MasterAway(el)) ApplyMasterRotation(el);
        }

        private void ApplyMasterRotation(MasterElement el)
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null || el == null) return;
                double a = MasterOn(el.Screen) ? MasterRotation(el.Key) : 0;
                foreach (string name in el.Names)
                {
                    var fe = MasterFind(win, name);
                    if (fe != null) SetMasterRotate(fe, a);
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: turning: " + ex.Message); }
        }

        /// <summary>
        /// Our turn on the element, about its middle. It goes first (before our scale, the theme's own transform and our move),
        /// so it turns about the part's own middle and the scale and move work as they did.
        /// </summary>
        private void SetMasterRotate(FrameworkElement fe, double a)
        {
            RotateTransform r;
            masterRotates.TryGetValue(fe, out r);
            bool attached = r != null && (ReferenceEquals(fe.RenderTransform, r)
                            || (fe.RenderTransform is TransformGroup && GroupHas((TransformGroup)fe.RenderTransform, r)));
            if (!attached)
            {
                if (Math.Abs(a) < 0.01) return;   // straight: leave the element alone
                r = new RotateTransform();
                var cur = fe.RenderTransform;
                if (cur == null || ReferenceEquals(cur, Transform.Identity) || cur.IsFrozen && cur.Value.IsIdentity) fe.RenderTransform = r;
                else
                {
                    var g = new TransformGroup();
                    g.Children.Add(r);
                    g.Children.Add(cur);
                    fe.RenderTransform = g;
                }
                bool hooked = masterRotates.ContainsKey(fe);
                masterRotates[fe] = r;
                // once per element (a new turn replaces the old one in the list, the handler reads the list)
                if (!hooked)
                    fe.SizeChanged += (o, e) => { RotateTransform now; if (masterRotates.TryGetValue(fe, out now) && now != null) CentreRotate(fe, now); };
            }
            else TurnFirst(fe, r);   // our scale may have gone in front of it since
            CentreRotate(fe, r);
            if (Math.Abs(r.Angle - a) > 0.001) r.Angle = a;
        }

        private static void CentreRotate(FrameworkElement fe, RotateTransform r)
        {
            double cx = fe.ActualWidth / 2, cy = fe.ActualHeight / 2;
            if (Math.Abs(r.CenterX - cx) > 0.25) r.CenterX = cx;
            if (Math.Abs(r.CenterY - cy) > 0.25) r.CenterY = cy;
        }

        /// <summary>Puts our turn first in the element's transforms (it's only ever inside groups we made).</summary>
        private static void TurnFirst(FrameworkElement fe, RotateTransform r)
        {
            var g = fe.RenderTransform as TransformGroup;
            if (g == null || g.IsFrozen) return;
            if (g.Children.Count > 0 && ReferenceEquals(g.Children[0], r)) return;
            if (!RemoveFromGroup(g, r)) return;
            g.Children.Insert(0, r);
        }

        private static bool RemoveFromGroup(TransformGroup g, Transform t)
        {
            if (g.IsFrozen) return false;
            for (int i = 0; i < g.Children.Count; i++)
            {
                if (ReferenceEquals(g.Children[i], t)) { g.Children.RemoveAt(i); return true; }
                var inner = g.Children[i] as TransformGroup;
                if (inner != null && RemoveFromGroup(inner, t)) return true;
            }
            return false;
        }

        // ---------------- what's picked up ----------------

        /// <summary>Something is picked up that the triggers turn (Free move / moving a widget, or a tile on the board).</summary>
        private bool RotateActive
        {
            get
            {
                if (masterMoving != null || widgetMoving != null) return true;
                return boardEditing && boardPicked != null && boardPicked.Card != null && cropTile == null && IsBoardMode && openPanel == null && !detailsOpen && !confirmOpen;   // (cropping: LT / RT are the step)
            }
        }

        private double PickedAngle()
        {
            if (masterMoving != null) return MasterRotation(masterMoving.Key);
            if (widgetMoving != null) return NormAngle(widgetMoving.Config.Rotation);
            if (boardPicked != null && boardPicked.Card != null) return NormAngle(boardPicked.Card.Rotation);
            return 0;
        }

        /// <summary>Turns what's picked up to <paramref name="a"/> (kept when it's put down, put back on B).</summary>
        private void SetPickedAngle(double a)
        {
            a = NormAngle(a);
            if (masterMoving != null)
            {
                SetMasterRotation(masterMoving.Key, a);
                ApplyMasterRotation(masterMoving);
                Notify("MasterMoveText");
                ui.BeginInvoke(new Action(UpdateMasterOutline), DispatcherPriority.Render);
            }
            else if (widgetMoving != null)
            {
                widgetMoving.Config.Rotation = a;
                widgetMoving.TurnChanged();
                Notify("MasterMoveText");
                ui.BeginInvoke(new Action(UpdateMasterOutline), DispatcherPriority.Render);
            }
            else if (boardPicked != null && boardPicked.Card != null)
            {
                var c = boardPicked.Card;
                c.Rotation = a;
                boardPicked.Rotation = a;
                // a widget in the tile is drawn over it: it turns with it
                if (c.Kind == "widget" && c.Target != null && Widgets != null)
                {
                    var w = Widgets.FirstOrDefault(x => x.Config != null && x.Config.Id == c.Target);
                    if (w != null) w.SetBoardAngle(a);
                }
                Notify("BoardHintText");
            }
        }

        /// <summary>A tap of a trigger (or Q / E): 5° that way.</summary>
        private void RotateTap(int dir)
        {
            if (!RotateActive) return;
            double before = PickedAngle();
            double a = SnapAngle(before + dir * TurnTap);
            rotRaw = a;
            if (Math.Abs(a - before) < 0.01) return;
            SetPickedAngle(a);
            PlaySound("move");
        }

        // ---------------- the triggers ----------------

        /// <summary>The triggers on every controller Playnite has (the most pressed of each), 0..32767.</summary>
        private void ReadTriggers(out short lt, out short rt)
        {
            lt = 0;
            rt = 0;
            try
            {
                foreach (var pad in api.GetConnectedControllers().OfType<IGamepad>())
                {
                    if (pad == null || !pad.Enabled) continue;
                    var a = pad.AnalogInputState;
                    if (a == null) continue;
                    short v;
                    if (a.TryGetValue(ControllerInput.TriggerLeft, out v) && v > lt) lt = v;
                    if (a.TryGetValue(ControllerInput.TriggerRight, out v) && v > rt) rt = v;
                }
            }
            catch (Exception) { }
        }

        /// <summary>Starts reading the triggers when something's picked up, stops when nothing is (no reading otherwise).</summary>
        private void UpdateRotTimer()
        {
            if (ui == null) return;
            if (!RotateActive) { StopRotTimer(); return; }
            HookRotateMouse();
            if (turnTimer == null)
            {
                turnTimer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(33) };
                turnTimer.Tick += (s, e) =>
                {
                    try { TurnTick(); }
                    catch (Exception ex) { logger.Warn("Kippix: triggers: " + ex.Message); StopRotTimer(); }
                };
            }
            if (turnTimer.IsEnabled) return;
            // a trigger already held when it was picked up isn't a press (let go first)
            short lt, rt;
            ReadTriggers(out lt, out rt);
            ltDown = lt > TriggerOn;
            rtDown = rt > TriggerOn;
            ltAt = rtAt = DateTime.MaxValue;   // (nor a hold, until it's pressed again)
            turnTickAt = DateTime.UtcNow;
            rotRaw = PickedAngle();
            turnTimer.Start();
        }

        private void StopRotTimer()
        {
            if (turnTimer != null && turnTimer.IsEnabled) turnTimer.Stop();
            ltDown = rtDown = false;
        }

        private void TurnTick()
        {
            if (!RotateActive) { StopRotTimer(); return; }
            var now = DateTime.UtcNow;
            double dt = Math.Max(0, Math.Min(0.1, (now - turnTickAt).TotalSeconds));
            turnTickAt = now;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null || !win.IsActive) { ltDown = rtDown = false; return; }   // (a game or another window in front)
            short lt, rt;
            ReadTriggers(out lt, out rt);
            if (lt > TriggerClick || rt > TriggerClick) triggerSeenAt = now;
            bool l = lt > TriggerOn, r = rt > TriggerOn;
            bool lNew = l && !ltDown, rNew = r && !rtDown;
            ltDown = l;
            rtDown = r;
            if (lNew) { ltAt = now; RotateTap(-1); }
            if (rNew) { rtAt = now; RotateTap(1); }
            int dir = 0;
            if (l && !lNew && (now - ltAt).TotalMilliseconds >= TurnHoldMs) dir -= 1;
            if (r && !rNew && (now - rtAt).TotalMilliseconds >= TurnHoldMs) dir += 1;
            if (dir == 0 || !RotateActive) return;
            double before = PickedAngle();
            rotRaw = NormAngle(rotRaw + dir * TurnPerSecond * dt);
            double a = SnapAngle(rotRaw);
            if (Math.Abs(a - before) > 0.01) SetPickedAngle(a);
        }

        /// <summary>Playnite sent a trigger as a press after all: its clicks are the trigger's too.</summary>
        private void NoteTrigger() { triggerSeenAt = DateTime.UtcNow; }

        // ---------------- the Steam Controller's trigger clicks ----------------

        private void HookRotateMouse()
        {
            if (turnMouseHooked) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return;
            turnMouseHooked = true;
            win.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnTurnMouse), true);
            win.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnTurnMouse), true);
        }

        /// <summary>A trigger held, or let go a moment ago (or one being pressed right now).</summary>
        private bool TriggerRecent()
        {
            if (ltDown || rtDown) return true;
            if ((DateTime.UtcNow - triggerSeenAt).TotalMilliseconds < ClickQuietMs) return true;
            short lt, rt;
            ReadTriggers(out lt, out rt);
            if (lt > TriggerClick || rt > TriggerClick) { triggerSeenAt = DateTime.UtcNow; return true; }
            return false;
        }

        /// <summary>
        /// In Free move (and while a board tile is picked up), a click that comes with a trigger is the trigger (Steam's desktop
        /// layout): swallowed, and so is its release, so it doesn't pick, drop or cancel anything.
        /// </summary>
        private void OnTurnMouse(object sender, MouseButtonEventArgs e)
        {
            try
            {
                bool down = e.RoutedEvent == Mouse.PreviewMouseDownEvent;
                if (!down)
                {
                    bool ours = swallowedDowns.Remove(e.ChangedButton) || (RotateActive && TriggerRecent());
                    if (!ours) return;
                    e.Handled = true;
                    if (dragging && e.ChangedButton == System.Windows.Input.MouseButton.Left) StopDrag();   // (its press got through: it stays picked up)
                    return;
                }
                if (!freeMove && !RotateActive) return;
                if (!TriggerRecent()) return;
                e.Handled = true;
                swallowedDowns.Add(e.ChangedButton);
            }
            catch (Exception) { }
        }

        // ---------------- outlines that follow the turn ----------------

        /// <summary>A box on the Free move canvas: its middle, its size and how far it's turned.</summary>
        private class TurnFrame
        {
            public Point C;
            public double W, H, A;

            /// <summary><paramref name="r"/> (in <paramref name="fe"/>'s own space) on <paramref name="canvas"/>, plus a turn about its middle.</summary>
            public static TurnFrame Of(Visual fe, Rect r, Visual canvas, double extra)
            {
                try
                {
                    var t = fe.TransformToVisual(canvas);
                    Point p0 = t.Transform(r.TopLeft), p1 = t.Transform(r.TopRight), p2 = t.Transform(r.BottomRight), p3 = t.Transform(r.BottomLeft);
                    var f = new TurnFrame
                    {
                        C = new Point((p0.X + p2.X) / 2, (p0.Y + p2.Y) / 2),
                        W = (p1 - p0).Length,
                        H = (p3 - p0).Length,
                        A = NormAngle(Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180 / Math.PI + extra)
                    };
                    return f.W > 0 && f.H > 0 ? f : null;
                }
                catch (Exception) { return null; }
            }

            public static TurnFrame OfBox(Rect b) { return new TurnFrame { C = new Point(b.X + b.Width / 2, b.Y + b.Height / 2), W = b.Width, H = b.Height, A = 0 }; }

            public Rect Bounds()
            {
                var box = new Rect(C.X - W / 2, C.Y - H / 2, W, H);
                return Math.Abs(A) < 0.05 ? box : new RotateTransform(A, C.X, C.Y).TransformBounds(box);
            }

            public bool Contains(Point p)
            {
                double rad = -A * Math.PI / 180, dx = p.X - C.X, dy = p.Y - C.Y;
                double x = dx * Math.Cos(rad) - dy * Math.Sin(rad), y = dx * Math.Sin(rad) + dy * Math.Cos(rad);
                return Math.Abs(x) <= W / 2 && Math.Abs(y) <= H / 2;
            }
        }

        /// <summary>
        /// A part of the screen on the canvas, hugging what it really shows (not the room it's stretched over): turned with it
        /// when it's one piece, else the box round all its pieces; kept to the screen.
        /// </summary>
        private TurnFrame MasterFrame(MasterElement el, FrameworkElement canvas)
        {
            if (el == null) return null;
            var win = Application.Current.MainWindow;
            var frames = new List<TurnFrame>();
            foreach (string name in el.Names)
            {
                if (name == "PART_ListGameItems" && carActive) continue;   // Row's carousel: Playnite's row is out of sight, the carousel is what you move
                var fe = UiTools.FindByName(win, name);
                if (fe == null || !fe.IsVisible || fe.ActualWidth <= 0) continue;
                TurnFrame f;
                if (name == "KX_CarouselHost")
                {
                    // the carousel's cards on screen (Tall: the five showing, cut at the screen's edges; Full Screen: the strip)
                    var cards = CarouselDrawnRect();
                    if (cards.IsEmpty || carCanvas == null) continue;
                    f = TurnFrame.Of(carCanvas, cards, canvas, 0);
                }
                else if (name == "PART_ListGameItems" && !boxWas.ContainsKey(fe))
                {
                    // (0.88) Playnite's own row / grid / list: round the cards you can see (the panel's room above and below them,
                    // and the cards' glow and shadow layers, left big gaps)
                    var cards = ListCardsRect(fe, canvas);
                    f = cards.IsEmpty ? TurnFrame.Of(fe, DrawnBounds(fe), canvas, 0) : TurnFrame.OfBox(cards);
                }
                else f = TurnFrame.Of(fe, boxWas.ContainsKey(fe) || detBlankEls.ContainsKey(fe) ? new Rect(0, 0, fe.ActualWidth, fe.ActualHeight) : DrawnBounds(fe), canvas, 0);   // (sized like a widget: its whole box)
                f = OnScreen(f, canvas);
                if (f != null) frames.Add(f);
            }
            if (frames.Count == 1) return frames[0];
            Rect box = Rect.Empty;
            foreach (var f in frames) box.Union(f.Bounds());
            return box.IsEmpty ? null : TurnFrame.OfBox(box);
        }

        /// <summary>(0.88) The cards of Playnite's list that are on screen, on the canvas: each card's own tile ("Tile" in its style).</summary>
        private static Rect ListCardsRect(FrameworkElement list, FrameworkElement canvas)
        {
            var box = Rect.Empty;
            var screen = new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight);
            try
            {
                foreach (var item in Descendants<ListBoxItem>(list))
                {
                    if (!item.IsVisible || item.ActualWidth <= 0) continue;
                    FrameworkElement tile = null;
                    try { tile = item.Template != null ? item.Template.FindName("Tile", item) as FrameworkElement : null; } catch (Exception) { }
                    var fe = tile != null && tile.IsVisible && tile.ActualWidth > 0 ? tile : item;
                    var r = fe.TransformToVisual(canvas).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                    r.Intersect(screen);
                    if (!r.IsEmpty && r.Width >= 1 && r.Height >= 1) box.Union(r);
                }
            }
            catch (Exception) { return Rect.Empty; }
            return box;
        }

        /// <summary>A frame cut to the screen (one that isn't turned; a turned one is left as it is). Null when it's all off screen.</summary>
        private static TurnFrame OnScreen(TurnFrame f, FrameworkElement canvas)
        {
            if (f == null || Math.Abs(f.A) >= 0.05 || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return f;
            var b = f.Bounds();
            b.Intersect(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
            return b.IsEmpty || b.Width < 1 || b.Height < 1 ? null : TurnFrame.OfBox(b);
        }

        /// <summary>
        /// What <paramref name="root"/> really draws, in its own space: everything in it that shows (not hidden or see-through,
        /// inside its clips), so a part stretched across the screen is outlined round its text, pictures and cards. The whole
        /// element when nothing in it draws.
        /// </summary>
        private static Rect DrawnBounds(FrameworkElement root)
        {
            var all = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var acc = Rect.Empty;
            int budget = 20000;
            try { DrawnWalk(root, root, new Rect(-1e6, -1e6, 2e6, 2e6), ref acc, ref budget); }
            catch (Exception) { return all; }
            return acc.IsEmpty || acc.Width < 1 || acc.Height < 1 ? all : acc;
        }

        private static void DrawnWalk(Visual v, FrameworkElement root, Rect clip, ref Rect acc, ref int budget)
        {
            if (--budget < 0) return;
            var u = v as UIElement;
            // (hidden, or faded right out; the part itself counts even while it's faded, e.g. the top bar hiding itself)
            if (u != null && (u.Visibility != Visibility.Visible || (!ReferenceEquals(v, root) && u.Opacity < 0.02))) return;
            GeneralTransform t = null;
            Rect own = VisualTreeHelper.GetContentBounds(v);
            if (!own.IsEmpty && Draws(v))
            {
                t = ReferenceEquals(v, root) ? Transform.Identity : v.TransformToVisual(root);
                var r = t.TransformBounds(own);
                r.Intersect(clip);
                if (!r.IsEmpty) acc.Union(r);
            }
            var fe = v as FrameworkElement;
            if (fe != null && (fe.ClipToBounds || fe.Clip != null || fe is ScrollContentPresenter))
            {
                if (t == null) t = ReferenceEquals(v, root) ? Transform.Identity : v.TransformToVisual(root);
                var c = new Rect(0, 0, fe.ActualWidth, fe.ActualHeight);
                if (fe.Clip != null) { var g = fe.Clip.Bounds; if (fe.ClipToBounds || fe is ScrollContentPresenter) g.Intersect(c); c = g; }
                var cr = c.IsEmpty ? Rect.Empty : t.TransformBounds(c);
                clip.Intersect(cr);
                if (clip.IsEmpty) return;
            }
            int n = VisualTreeHelper.GetChildrenCount(v);
            for (int i = 0; i < n && budget >= 0; i++)
            {
                var child = VisualTreeHelper.GetChild(v, i) as Visual;
                if (child != null) DrawnWalk(child, root, clip, ref acc, ref budget);
            }
        }

        /// <summary>It draws something you can see itself (a see-through background, there only to catch the mouse, doesn't count).</summary>
        private static bool Draws(Visual v)
        {
            var b = v as Border;
            if (b != null) return Paints(b.Background) || (Paints(b.BorderBrush) && (b.BorderThickness.Left + b.BorderThickness.Top + b.BorderThickness.Right + b.BorderThickness.Bottom) > 0);
            var p = v as Panel;
            if (p != null) return Paints(p.Background);
            var s = v as System.Windows.Shapes.Shape;
            if (s != null) return Paints(s.Fill) || (Paints(s.Stroke) && s.StrokeThickness > 0);
            var tb = v as TextBlock;
            if (tb != null) return !string.IsNullOrEmpty(tb.Text) || tb.Inlines.Count > 0;
            return true;
        }

        private static bool Paints(Brush br)
        {
            if (br == null || br.Opacity < 0.02) return false;
            var sc = br as SolidColorBrush;
            return sc == null || sc.Color.A > 3;
        }

        /// <summary>A widget on the canvas (it's turned inside its place, about its middle).</summary>
        private TurnFrame WidgetFrame(WidgetItem w, FrameworkElement canvas)
        {
            var items = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets") as ItemsControl;
            var c = items != null && w != null ? items.ItemContainerGenerator.ContainerFromItem(w) as FrameworkElement : null;
            if (c == null || !c.IsVisible) return null;
            return TurnFrame.Of(c, new Rect(0, 0, w.Width, w.Height), canvas, w.Angle);
        }

        /// <summary>The dashed outline round a frame, turned with it.</summary>
        private static void PlaceOutline(FrameworkElement outline, TurnFrame f)
        {
            Canvas.SetLeft(outline, f.C.X - f.W / 2 - 8);
            Canvas.SetTop(outline, f.C.Y - f.H / 2 - 8);
            outline.Width = f.W + 16;
            outline.Height = f.H + 16;
            if (Math.Abs(f.A) < 0.05)
            {
                if (outline.RenderTransform is RotateTransform) outline.ClearValue(UIElement.RenderTransformProperty);
                return;
            }
            var rt = outline.RenderTransform as RotateTransform;
            if (rt == null || rt.IsFrozen)
            {
                rt = new RotateTransform();
                outline.RenderTransformOrigin = new Point(0.5, 0.5);
                outline.RenderTransform = rt;
            }
            rt.Angle = f.A;
        }
    }
}
