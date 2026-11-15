using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Free move › Y: size what you're pointing at. The D-pad makes it narrower / wider (Left / Right) and shorter / taller
    /// (Up / Down); LB / RB change the step, Y puts it back to its own size, A keeps, B cancels.
    ///   - the games in list mode: taller or shorter a game at a time (the list's height in MasterSizes; the number of games
    ///     on screen follows it, at about the same size), within the screen;
    ///   - the description on the game page: any width and height (the text flows to fit);
    ///   - the game page's parts (0.74; the cards: play history, how long to beat, scores..., and the logo, stats, buttons, game
    ///     details, friends, captures, More like this, the clock): sized like a widget on the Board. The D-pad pulls one edge
    ///     (Right / Down: the right / bottom edge out, Left / Up: back in; X: the top / left edges instead), the part floats (the
    ///     parts round it stay where they are: it keeps its own room in the page's layout and draws over its neighbours), and
    ///     what's inside is laid out to the new shape and scaled with it (text and icons grow once it's bigger both ways, never
    ///     so far that anything is cut off). Before, the cards took their new size in the layout (pushing the rest round) and
    ///     the other parts could only be scaled as a whole.
    /// Sizes are kept per part in MasterSizes: width, height (0 = its own), and how far pulling the top / left edge (or the
    /// list's bottom) moved it, so putting its size back also puts that move back.
    /// </summary>
    public partial class ThemeApi
    {
        private bool sizing;                       // the part in masterMoving is being sized, not moved
        private double[] sizeStart;                // to put back on B
        private double tileWStart;                 // the list's game width when sizing started
        private double tileHStart;                 // and its height

        private static readonly HashSet<string> CardKeys = new HashSet<string> { "game.sec.history", "game.sec.hltb", "game.sec.ach", "game.sec.status", "game.sec.notes", "game.sec.scores", "game.sec.saves", "game.sec.mods", "game.sec.news" };

        public bool IsSizing { get { return sizing && masterMoving != null; } }
        public bool IsMovingNotSizing { get { return IsPlacing && !IsSizing; } }

        /// <summary>Every part can be sized (0.42): the list, the description and the game page cards by their width and height,
        /// the games in one row / a grid by their card size / columns, everything else by scaling it.</summary>
        private bool CanSize(string key) { return true; }

        /// <summary>Sized by scaling it (MasterSizes [scale, 0, 0, 0]; no entry = 100 %): the home screen's parts and the game page's cover / trailer.</summary>
        private static bool IsScaleKey(string key) { return key != "home.cards" && key != "game.desc" && key != SimilarKey && !IsBoxKey(key); }

        /// <summary>
        /// Sized like a widget (MasterSizes [width, height, moved x, moved y]): one edge at a time, floating over the parts round it,
        /// what's inside laid out to the shape and scaled with it. The game page's parts, but the description (its text flows to
        /// any width and height), the cover / trailer (its own Size setting; scaled as a whole), the button hints (scaled) and
        /// More like this (0.75: sizing it makes its covers bigger or smaller, see SimilarResizeBy).
        /// </summary>
        private static bool IsBoxKey(string key)
        {
            return key != null && key.StartsWith("game.", StringComparison.Ordinal) && key != "game.desc" && key != "game.media" && key != "game.hints" && key != SimilarKey;
        }

        private const string SimilarKey = "game.sec.similar";

        private double MasterScale(string key)
        {
            var s = MasterSize(key);
            return s[0] > 0 ? s[0] : 1;
        }

        private static readonly double[] ScaleSteps = { 0.01, 0.05, 0.10, 0.25 };
        private int scaleStep = 1;   // 5 %
        private int sizeStartCardSize, sizeStartGridCols;

        private void ScaleStepSize(int dir)
        {
            scaleStep = Math.Max(0, Math.Min(ScaleSteps.Length - 1, scaleStep + dir));
            PlaySound("move");
            Notify("MasterMoveText");
        }

        /// <summary>Our scale on the element (before any transform it has, from its lined-up corner), beside our move.</summary>
        private readonly Dictionary<FrameworkElement, ScaleTransform> masterScales = new Dictionary<FrameworkElement, ScaleTransform>();

        private void SetMasterScale(FrameworkElement fe, double sc)
        {
            ScaleTransform s;
            masterScales.TryGetValue(fe, out s);
            bool attached = s != null && (ReferenceEquals(fe.RenderTransform, s)
                            || (fe.RenderTransform is TransformGroup && GroupHas((TransformGroup)fe.RenderTransform, s)));
            if (!attached)
            {
                if (Math.Abs(sc - 1) < 0.001) return;   // nothing to scale: leave the element alone
                s = new ScaleTransform();
                var cur = fe.RenderTransform;
                if (cur == null || ReferenceEquals(cur, Transform.Identity) || cur.IsFrozen && cur.Value.IsIdentity) fe.RenderTransform = s;
                else
                {
                    var g = new TransformGroup();
                    g.Children.Add(s);
                    g.Children.Add(cur);
                    fe.RenderTransform = g;
                }
                bool hooked = masterScales.ContainsKey(fe);
                masterScales[fe] = s;
                // once per element (a new scale replaces the old one in the list, the handler reads the list)
                if (!hooked)
                    fe.SizeChanged += (o, e) => { ScaleTransform now; if (masterScales.TryGetValue(fe, out now) && now != null) CenterScale(fe, now); };
            }
            CenterScale(fe, s);
            if (Math.Abs(s.ScaleX - sc) > 0.0001) s.ScaleX = sc;
            if (Math.Abs(s.ScaleY - sc) > 0.0001) s.ScaleY = sc;
        }

        private static bool GroupHas(TransformGroup g, Transform t)
        {
            foreach (var c in g.Children)
            {
                if (ReferenceEquals(c, t)) return true;
                var inner = c as TransformGroup;
                if (inner != null && GroupHas(inner, t)) return true;
            }
            return false;
        }

        /// <summary>It grows from the side it's lined up to (left / right / centre, top / bottom / middle); a part sized like a widget from its top left corner.</summary>
        private void CenterScale(FrameworkElement fe, ScaleTransform s)
        {
            if (boxWas.ContainsKey(fe))
            {
                if (Math.Abs(s.CenterX) > 0.01) s.CenterX = 0;
                if (Math.Abs(s.CenterY) > 0.01) s.CenterY = 0;
                return;
            }
            double w = fe.ActualWidth, h = fe.ActualHeight;
            double cx = fe.HorizontalAlignment == HorizontalAlignment.Left ? 0 : fe.HorizontalAlignment == HorizontalAlignment.Right ? w : w / 2;
            double cy = fe.VerticalAlignment == VerticalAlignment.Bottom ? h : fe.VerticalAlignment == VerticalAlignment.Center ? h / 2 : 0;
            if (Math.Abs(s.CenterX - cx) > 0.5) s.CenterX = cx;
            if (Math.Abs(s.CenterY - cy) > 0.5) s.CenterY = cy;
        }

        /// <summary>[width, height, moved x, moved y] (a copy).</summary>
        private double[] MasterSize(string key)
        {
            var r = new double[4];
            double[] s;
            if (settings.MasterSizes != null && settings.MasterSizes.TryGetValue(key, out s) && s != null)
                for (int i = 0; i < Math.Min(4, s.Length); i++) r[i] = s[i];
            return r;
        }

        private void StoreMasterSize(string key, double[] s)
        {
            if (settings.MasterSizes == null) settings.MasterSizes = new Dictionary<string, double[]>();
            bool any = s[0] > 0 || s[1] > 0 || Math.Abs(s[2]) > 0.01 || Math.Abs(s[3]) > 0.01;
            if (!any) settings.MasterSizes.Remove(key);
            else settings.MasterSizes[key] = new[] { Math.Max(0, s[0]), Math.Max(0, s[1]), s[2], s[3] };
        }

        private void SetMasterSize(string key, double w, double h)
        {
            var s = MasterSize(key);
            s[0] = w; s[1] = h;
            StoreMasterSize(key, s);
        }

        /// <summary>Its own size again: the size goes, and so does any move that pulling an edge made.</summary>
        private void ResetMasterSize(string key)
        {
            var s = MasterSize(key);
            var o = MasterOffset(key);
            SetMasterOffset(key, o[0] - s[2], o[1] - s[3]);
            if (settings.MasterSizes != null) settings.MasterSizes.Remove(key);
        }

        /// <summary>How far to move a part that grew by <paramref name="d"/>, so the edge not being pulled stays where it is.</summary>
        private static double KeepOtherEdge(bool alignedNear, bool alignedCentre, double d, bool pullingFar)
        {
            if (alignedCentre) return pullingFar ? d / 2 : -d / 2;
            if (alignedNear) return pullingFar ? 0 : -d;
            return pullingFar ? d : 0;   // aligned to the far side (right / bottom)
        }

        /// <summary>Y in Free move: start sizing what the pointer is on.</summary>
        private void FreeResize()
        {
            UpdateFreeTarget();
            string t = freeTarget;
            if (t == null) { PlaySound("back"); return; }
            if (t.StartsWith("w:", StringComparison.Ordinal))
            {
                // widgets size with LB / RB while they're picked up
                var w = Widgets.FirstOrDefault(x => "w:" + x.Config.Id == t);
                if (w != null) { PlaySound("select"); ModeChanged(); StartWidgetMove(w, false); }
                return;
            }
            var el = MasterElements.FirstOrDefault(m => m.Key == t);
            if (el == null || !CanSize(el.Key))
            {
                PlaySound("back");
                sizeRefusedFor = el != null ? el.Key : null;
                Notify("MasterMoveText");
                return;
            }
            PlaySound("select");
            sizeEdge = 0;
            sizeStart = MasterSize(el.Key);
            tileWStart = settings.ListTileW;
            tileHStart = settings.ListTileH;
            sizeStartCardSize = settings.CardSize;
            sizeStartGridCols = settings.GridColumns;
            sizeStartSimilarShape = settings.DetSimilarShape;
            sizeStartCar = new[] { settings.CarTallFill, settings.RowTallCount, settings.CarFullH, settings.RowFullCount };
            StartMasterMove(el);
            sizing = true;   // after: starting a move turns sizing off
            Notify("MasterMoveText");
            Notify("IsSizing");
            Notify("IsMovingNotSizing");
            Notify("SizeXText");
        }

        private int sizeStartSimilarShape;
        private double[] sizeStartCar;   // (0.89) Tall / Full Screen's sizes when sizing started (B puts them back)   // More like this's cover shape when sizing started (B puts it back)

        /// <summary>What X does while sizing (the hint at the bottom): More like this changes its covers' shape, the rest pull the other edge.</summary>
        public string SizeXText
        {
            get { return masterMoving != null && masterMoving.Key == SimilarKey ? Loc("LOC_KX_SizeShapeHint", "Shape") : Loc("LOC_KX_SizeEdge", "Top / bottom edge"); }
        }

        private string sizeRefusedFor;   // the pointer's line says "this one can't be sized" until it points somewhere else

        private string SizeText()
        {
            var el = masterMoving;
            if (el == null) return "";
            string edge = sizeEdge == 0 ? Loc("LOC_KX_EdgeBottom", "pulling the bottom edge") : Loc("LOC_KX_EdgeTop", "pulling the top edge");
            if (el.Key != "home.cards") edge = sizeEdge == 0 ? Loc("LOC_KX_EdgeBottomRight", "pulling the bottom / right edges") : Loc("LOC_KX_EdgeTopLeft", "pulling the top / left edges");
            if (el.Key == "home.cards" && LayoutNow == 0 && carActive && CarDesign == 2 && !FolderTilesShown)
                return string.Format(Loc("LOC_KX_SizeFullCount2", "{0} on screen (Left / Right)   ·   height {1}% (Up / Down)"), RowFullCountText, Math.Round(settings.CarFullH * 100));
            if (el.Key == "home.cards" && LayoutNow == 0 && carActive && CarDesign == 1 && !FolderTilesShown)
                return string.Format(Loc("LOC_KX_SizeTall2", "Zoom: {0} on screen (Left / Right)   ·   height {1} (Up / Down)"), RowTallCountText,
                                     settings.CarTallFill > 0 ? Math.Round(settings.CarTallFill * 100) + "%" : Loc("LOC_KX_SizeTallAuto", "as the covers"));
            if (el.Key == "home.cards" && LayoutNow == 0) return string.Format(Loc("LOC_KX_SizeCards", "Card size {0}   ·   Up / Right bigger, Down / Left smaller"), CardSizeText);
            if (el.Key == "home.cards" && LayoutNow == 1) return string.Format(Loc("LOC_KX_SizeGrid", "{0} columns   ·   Up / Right bigger, Down / Left smaller"), settings.GridColumns);
            if (el.Key == "home.cards" && LayoutNow == 6) return string.Format(Loc("LOC_KX_SizeJust", "{0} rows   ·   Up / Right bigger, Down / Left smaller"), JustRowsNow);
            if (el.Key == "home.cards") return ListStretchText + "   ·   " + edge;
            if (el.Key == SimilarKey)
                return string.Format(Loc("LOC_KX_SizeSimilarCovers2", "Covers {0} × {1}, {2}   ·   Up / Right bigger, Down / Left smaller   ·   X: shape"), Math.Round(DetSimilarTileW), Math.Round(DetSimilarTileH), SimilarShapeText());
            if (IsScaleKey(el.Key))
                return string.Format(Loc("LOC_KX_SizeScale", "{0}%   ·   steps of {1}%   ·   Up / Right bigger, Down / Left smaller"),
                                     Math.Round(MasterScale(el.Key) * 100), Math.Round(ScaleSteps[scaleStep] * 100));
            var fe = SizedElement(el);
            double w = fe != null ? Math.Round(fe.ActualWidth) : 0, h = fe != null ? Math.Round(fe.ActualHeight) : 0;
            double bw, bh;
            if (IsBoxKey(el.Key) && BoxSizeNow(el, out bw, out bh)) { w = Math.Round(bw); h = Math.Round(bh); }   // (what it's drawn at: its layout box is scaled)
            return string.Format(Loc("LOC_KX_SizeWH", "{0} × {1}"), w, h) + "   ·   " + edge + "   ·   " + string.Format(Loc("LOC_KX_MeFreeStep", "free, {0} px steps"), SizeStep);
        }

        private double SizeStep { get { return Math.Max(5, MasterSteps[masterStep]); } }

        private FrameworkElement SizedElement(MasterElement el)
        {
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            return win != null && el.Names.Length > 0 ? UiTools.FindByName(win, el.Names[0]) : null;
        }

        /// <summary>Keys while sizing. True if handled.</summary>
        private void SizeKey(string button, Key key)
        {
            int dx = 0, dy = 0;
            if (button == null)
            {
                switch (key)
                {
                    case Key.Left: dx = -1; break;
                    case Key.Right: dx = 1; break;
                    case Key.Up: dy = -1; break;
                    case Key.Down: dy = 1; break;
                    case Key.Enter: case Key.Space: EndSizing(true); return;
                    case Key.Escape: case Key.Back: EndSizing(false); return;
                    case Key.R: case Key.Delete: if (!JustChanged) ResizeBy(0, 0, true); return;
                    case Key.Tab: ToggleSizeEdge(); return;   // (Q / E turn it)
                    case Key.PageUp: if (SizesByScale()) ScaleStepSize(-1); else MasterStepSize(-1); return;
                    case Key.PageDown: if (SizesByScale()) ScaleStepSize(1); else MasterStepSize(1); return;
                    default: return;
                }
            }
            else
            {
                switch (button)
                {
                    case "A": EndSizing(true); return;
                    case "B": EndSizing(false); return;
                    case "Y": if (!JustChanged) ResizeBy(0, 0, true); return;   // (not the Y that started sizing: StartMasterMove marks the change)
                    case "X": ToggleSizeEdge(); return;
                    case "LeftShoulder": if (SizesByScale()) ScaleStepSize(-1); else MasterStepSize(-1); return;
                    case "RightShoulder": if (SizesByScale()) ScaleStepSize(1); else MasterStepSize(1); return;
                    default: return;
                }
            }
            ResizeBy(dx, dy, false);
        }

        private int sizeEdge;   // 0: pulling the bottom (and right) edge, 1: the top (and left) edge; X switches

        /// <summary>What's being sized grows by a percentage (LB / RB change that step), not by pixels.</summary>
        private bool SizesByScale() { return masterMoving != null && IsScaleKey(masterMoving.Key); }

        private void ToggleSizeEdge()
        {
            // More like this: X changes its covers' shape (its size is its covers' size, there's no edge to pull)
            if (masterMoving != null && masterMoving.Key == SimilarKey)
            {
                CycleSimilarShape();
                Notify("MasterMoveText");
                LaterOutline();
                return;
            }
            // only the parts sized by width / height have an edge to pull
            if (masterMoving == null || IsScaleKey(masterMoving.Key) || (masterMoving.Key == "home.cards" && LayoutNow != 2)) { PlaySound("back"); return; }
            sizeEdge = 1 - sizeEdge;
            PlaySound("move");
            Notify("MasterMoveText");
        }

        private void ResizeBy(int dx, int dy, bool reset)
        {
            var el = masterMoving;
            if (el == null) return;
            bool list = el.Key == "home.cards";
            bool rowOrGrid = list && LayoutNow != 2;
            if (reset)
            {
                // Y: its own size, and straight again
                if (Math.Abs(MasterRotation(el.Key)) > 0.001) { SetMasterRotation(el.Key, 0); ApplyMasterRotation(el); }
                rotRaw = 0;
                if (rowOrGrid && LayoutNow == 0 && carActive && CarDesign != 0 && !FolderTilesShown)
                {
                    // (0.89) Tall / Full Screen: their own size again
                    settings.CarTallW = 1; settings.CarTallH = 1; settings.CarTallFill = 0; settings.CarFullH = 1;
                    SaveSettings(); ApplyHomeLayoutSoon(); Notify("RowTallCountText"); Notify("MasterMoveText"); PlaySound("move"); LaterOutline(); return;
                }
                if (rowOrGrid) { ResetCardsSize(); PlaySound("move"); LaterOutline(); return; }
                if (list) { settings.ListTileH = 0; settings.ListTileW = 0; UpdateSliders(); }
                ResetMasterSize(el.Key);
                PlaySound("move");
                ApplyMasterOffsets();
                if (list) HomeModeChanged();
                LaterOutline();
                return;
            }
            if (rowOrGrid)
            {
                // one row: the card size; a grid: its columns (fewer = bigger)
                int d = (dy < 0 || dx > 0) ? 1 : -1;
                if (LayoutNow == 0 && carActive && CarDesign == 1 && !FolderTilesShown)
                {
                    // (0.90) Row › Tall: Up / Down their height (all the way up to the room there is); Left / Right zoom
                    // (in: fewer, bigger covers; out: more, smaller ones)
                    if (dx != 0)
                    {
                        int now = Math.Max(1, Math.Min(10, settings.RowTallCount));
                        int want = Math.Max(1, Math.Min(10, now - Math.Sign(dx)));
                        if (want == now) { PlaySound("back"); return; }
                        settings.RowTallCount = want;
                        Notify("RowTallCountText");
                    }
                    else
                    {
                        double fill = settings.CarTallFill;
                        if (fill <= 0)
                        {
                            // from the height it has now (2:3 as fits)
                            fill = 0.8;
                            try
                            {
                                if (carFrame != null && carFrame.Design == 1 && carHost != null && carHost.ActualHeight > 0)
                                {
                                    double margin = Math.Max(8, carHost.ActualHeight * CarouselGeometry.TallMarginPart);
                                    double room = carHost.ActualHeight - carBottomInset - margin - (carTopY + margin);
                                    if (room > 50) fill = carFrame.H * CarouselGeometry.TallGrow / room;
                                }
                            }
                            catch (Exception) { }
                        }
                        double nf = Math.Round(Math.Max(0.2, Math.Min(1, fill - Math.Sign(dy) * 0.03)), 2);
                        if (Math.Abs(nf - settings.CarTallFill) < 0.001) { PlaySound("back"); return; }
                        settings.CarTallFill = nf;
                    }
                    SaveSettings();
                    Notify("MasterMoveText");
                    ApplyHomeLayoutSoon();
                    PlaySound("move");
                    LaterOutline();
                    return;
                }
                if (LayoutNow == 0 && carActive && CarDesign == 2 && !FolderTilesShown && dy != 0)
                {
                    // (0.89) Row › Full Screen: Up / Down the panels' height (from the top); Left / Right how many across
                    double fh = Math.Round(Math.Max(0.3, Math.Min(1, settings.CarFullH - Math.Sign(dy) * 0.05)), 2);
                    if (Math.Abs(fh - settings.CarFullH) < 0.001) { PlaySound("back"); return; }
                    settings.CarFullH = fh;
                    SaveSettings();
                    Notify("MasterMoveText");
                    ApplyHomeLayoutSoon();
                    PlaySound("move");
                    LaterOutline();
                    return;
                }
                if (LayoutNow == 0 && carActive && CarDesign != 0)
                {
                    // Full Screen: fewer panels on screen = bigger ones
                    if (CarDesign != 2 || FolderTilesShown) { PlaySound("back"); return; }
                    int now = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.RowFullCount));
                    int want = Math.Max(FullCountMin, Math.Min(FullCountMax, now - d));
                    if (want == now) { PlaySound("back"); return; }
                    settings.RowFullCount = want;
                    SaveSettings();
                    Notify("RowFullCountText");
                    Notify("MasterMoveText");
                    ApplyHomeLayoutSoon();
                }
                else if (LayoutNow == 0)
                {
                    int before = EffectiveCardSize;
                    NudgeCardSize(d);
                    if (EffectiveCardSize == before) { PlaySound("back"); return; }
                }
                else if (LayoutNow == 6)
                {
                    // the mosaic: fewer rows on screen = bigger covers
                    int rows = Math.Max(2, Math.Min(5, JustRowsNow - d));
                    if (rows == JustRowsNow) { PlaySound("back"); return; }
                    settings.JustRows = rows;
                    SaveSettings();
                    HomeModeChanged();
                }
                else
                {
                    int cols = Math.Max(3, Math.Min(10, settings.GridColumns - d));
                    if (cols == settings.GridColumns) { PlaySound("back"); return; }
                    settings.GridColumns = cols;
                    SaveSettings();
                    HomeModeChanged();
                }
                PlaySound("move");
                LaterOutline();
                return;
            }
            if (IsScaleKey(el.Key))
            {
                int d = (dy < 0 || dx > 0) ? 1 : -1;
                double now = MasterScale(el.Key);
                double sc = Math.Round(Math.Max(0.3, Math.Min(3.0, now + d * ScaleSteps[scaleStep])), 2);
                if (Math.Abs(sc - now) < 0.001) { PlaySound("back"); return; }
                var ss = MasterSize(el.Key);
                ss[0] = Math.Abs(sc - 1) < 0.001 ? 0 : sc;
                ss[1] = 0;
                StoreMasterSize(el.Key, ss);
                ApplyMasterSizes();
                PlaySound("move");
                LaterOutline();
                return;
            }
            if (IsBoxKey(el.Key)) { BoxResizeBy(el, dx, dy); return; }
            if (el.Key == SimilarKey) { SimilarResizeBy(dx, dy); return; }
            var fe = SizedElement(el);
            if (fe == null) return;
            if (list)
            {
                // the list: Left / Right = each game's width, Up / Down = each game's height (the list is "Games to display"
                // games tall, so it grows with them; pulling the top edge keeps its bottom where it is)
                if (dx != 0) settings.ListTileW = Math.Max(200, Math.Min(1400, Math.Round(CurrentListTileW() / 20) * 20 + dx * 20));
                if (dy != 0)
                {
                    int gy0 = sizeEdge == 0 ? dy : -dy;
                    double oldH = CurrentListTileH();
                    double newH = Math.Max(60, Math.Min(400, Math.Round(oldH / 10) * 10 + gy0 * 10));
                    if (Math.Abs(newH - oldH) < 0.5) { PlaySound("back"); return; }
                    int n = Math.Max(1, Math.Min(10, settings.ListRows));
                    double grow = (newH - oldH) * (n + 0.5);
                    var va0 = fe.VerticalAlignment;
                    double sy0 = KeepOtherEdge(va0 == VerticalAlignment.Top || va0 == VerticalAlignment.Stretch, va0 == VerticalAlignment.Center, grow, sizeEdge == 0);
                    // stays on the screen: if the edge being pulled would go off it, the other edge moves instead (and if
                    // neither fits, no)
                    if (grow > 0)
                    {
                        try
                        {
                            var root = Application.Current.MainWindow.Content as FrameworkElement ?? Application.Current.MainWindow;
                            double top = fe.TranslatePoint(new Point(0, 0), root).Y, bottom = top + fe.ActualHeight;
                            Func<double, bool> fits = mv =>
                            {
                                // the part's top / bottom after growing by grow and moving by mv (it's lined up by va0)
                                double nb, nt;
                                if (va0 == VerticalAlignment.Bottom) { nb = bottom + mv; nt = nb - fe.ActualHeight - grow; }
                                else if (va0 == VerticalAlignment.Center) { nt = top - grow / 2 + mv; nb = nt + fe.ActualHeight + grow; }
                                else { nt = top + mv; nb = nt + fe.ActualHeight + grow; }
                                return nt >= 6 && nb <= root.ActualHeight - 6;
                            };
                            if (!fits(sy0))
                            {
                                double other = KeepOtherEdge(va0 == VerticalAlignment.Top || va0 == VerticalAlignment.Stretch, va0 == VerticalAlignment.Center, grow, sizeEdge != 0);
                                if (fits(other)) sy0 = other;
                                else { PlaySound("back"); return; }
                            }
                        }
                        catch (Exception) { }
                    }
                    settings.ListTileH = newH;
                    if (Math.Abs(sy0) >= 0.5)
                    {
                        var o0 = MasterOffset(el.Key);
                        double ny = Math.Max(-1060, Math.Min(1060, o0[1] + sy0));
                        var s0 = MasterSize(el.Key);
                        s0[3] += ny - o0[1];   // what it really moved (the place is kept within the screen's range)
                        StoreMasterSize(el.Key, s0);
                        SetMasterOffset(el.Key, o0[0], ny);
                        ApplyMasterOffsets();
                    }
                }
                PlaySound("move");
                HomeModeChanged();
                UpdateSliders();
                LaterOutline();
                return;
            }
            if (dx == 0 && dy == 0) { PlaySound("back"); return; }
            var s = MasterSize(el.Key);
            double w0 = s[0] > 0 ? s[0] : fe.ActualWidth, h0 = s[1] > 0 ? s[1] : fe.ActualHeight;
            double step = list ? Math.Max(40, Math.Round(listBaseItemH)) : SizeStep;   // the list: a game at a time
            int gx = sizeEdge == 0 ? dx : -dx, gy = sizeEdge == 0 ? dy : -dy;          // top / left edge: Up / Left make it bigger
            double w = w0, h = h0;
            if (gx != 0) w = Math.Max(160, Math.Min(1880, Math.Round(w0 + gx * step)));
            if (gy != 0) h = Math.Max(list ? step * 2.5 : 80, Math.Min(1040, Math.Round(h0 + gy * step)));
            double dW = w - w0, dH = h - h0;
            // where it would move, so the other edge stays put (depends on how the part is lined up)
            var ha = fe.HorizontalAlignment; var va = fe.VerticalAlignment;
            double sx = dW == 0 ? 0 : KeepOtherEdge(ha == HorizontalAlignment.Left || ha == HorizontalAlignment.Stretch, ha == HorizontalAlignment.Center, dW, sizeEdge == 0);
            double sy = dH == 0 ? 0 : KeepOtherEdge(va == VerticalAlignment.Top || va == VerticalAlignment.Stretch, va == VerticalAlignment.Center, dH, sizeEdge == 0);
            if (list && dH > 0)
            {
                // the list stays on the screen: refuse a pull that would take it past the top or bottom
                try
                {
                    var root = Application.Current.MainWindow.Content as FrameworkElement ?? Application.Current.MainWindow;
                    var p = fe.TranslatePoint(new Point(0, 0), root);
                    double top = p.Y - (dH - sy), bottom = p.Y + fe.ActualHeight + sy;
                    if (top < 6 || bottom > root.ActualHeight - 6) { PlaySound("back"); return; }
                }
                catch (Exception) { }
            }
            if (dW == 0 && dH == 0) { PlaySound("back"); return; }
            s[0] = list ? 0 : (dx != 0 || s[0] > 0 ? w : 0);
            s[1] = dy != 0 || s[1] > 0 ? h : 0;
            s[2] += sx; s[3] += sy;
            StoreMasterSize(el.Key, s);
            var o = MasterOffset(el.Key);
            SetMasterOffset(el.Key, Math.Max(-1900, Math.Min(1900, o[0] + sx)), Math.Max(-1060, Math.Min(1060, o[1] + sy)));
            PlaySound("move");
            ApplyMasterOffsets();
            if (list) HomeModeChanged();
            LaterOutline();
        }

        /// <summary>The outline and the line of text follow (the list lays itself out a moment later).</summary>
        private void LaterOutline()
        {
            ui.BeginInvoke(new Action(() => { Notify("MasterMoveText"); UpdateMasterOutline(); }), DispatcherPriority.Render);
            var later = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
            later.Tick += (s2, e2) => { later.Stop(); Notify("MasterMoveText"); UpdateMasterOutline(); };
            later.Start();
        }

        private void EndSizing(bool keep)
        {
            var el = masterMoving;
            if (el == null) return;
            if (!keep) RevertSizing();
            sizing = false;
            Notify("IsSizing");
            Notify("IsMovingNotSizing");
            EndMasterMove(keep);   // pulling the top edge moves it too: cancelling puts that back as well
        }

        /// <summary>Sizing cancelled (B, right click, or the screen closing): back to the size it had. Leaves sizing off.</summary>
        private void RevertSizing()
        {
            var el = masterMoving;
            if (!sizing || el == null) { sizing = false; return; }
            StoreMasterSize(el.Key, sizeStart ?? new double[4]);   // the place goes back with the move being cancelled
            if (el.Key == SimilarKey) settings.DetSimilarShape = sizeStartSimilarShape;   // (and the covers' shape X changed; ApplyMasterSizes shows it)
            if (el.Key == "home.cards" && (settings.ListTileW != tileWStart || settings.ListTileH != tileHStart)) { settings.ListTileW = tileWStart; settings.ListTileH = tileHStart; UpdateSliders(); }
            if (el.Key == "home.cards" && (settings.CardSize != sizeStartCardSize || settings.GridColumns != sizeStartGridCols))
            {
                settings.CardSize = sizeStartCardSize;
                settings.GridColumns = sizeStartGridCols;
                NotifyCardSettings();
                ApplyCardLayoutSoon();
            }
            if (el.Key == "home.cards" && sizeStartCar != null)
            {
                settings.CarTallFill = sizeStartCar[0]; settings.RowTallCount = (int)sizeStartCar[1]; settings.CarFullH = sizeStartCar[2]; settings.RowFullCount = (int)sizeStartCar[3];
                ApplyHomeLayoutSoon();
            }
            if (el.Key == "home.cards") HomeModeChanged(); else ApplyMasterSizes();
            sizing = false;
            Notify("IsSizing");
            Notify("IsMovingNotSizing");
        }

        /// <summary>Puts every saved size on its part (the description and the game page cards). Called with the offsets.</summary>
        private void ApplyMasterSizes()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                // another game on the page: the parts sized like a widget read their own size again straight away (their insides changed)
                Guid gameNow = detailsOpen ? SelectedGameId() : Guid.Empty;
                bool boxFresh = gameNow != boxGame;
                if (boxFresh) { boxGame = gameNow; boxTries = 0; }
                bool boxWaiting = false;
                foreach (var el in MasterElements)
                {
                    if (el.Key == SimilarKey) { ApplySimilarTileSize(); continue; }   // (its size is its covers' size)
                    if (IsBoxKey(el.Key))
                    {
                        var bs = MasterOn(el.Screen) ? MasterSize(el.Key) : new double[4];
                        for (int i = 0; i < el.Names.Length; i++)
                        {
                            var bf = UiTools.FindByName(win, el.Names[i]);
                            if (bf != null && ApplyBoxSize(el, bf, i == 0 ? bs : new double[4], boxFresh)) boxWaiting = true;   // (the size is the first one's: the Buttons' DLC line keeps its own)
                        }
                        continue;
                    }
                    if (IsScaleKey(el.Key))
                    {
                        double sc = MasterOn(el.Screen) ? MasterScale(el.Key) : 1;
                        foreach (string name in el.Names)
                        {
                            var sf = UiTools.FindByName(win, name);
                            if (sf != null) SetMasterScale(sf, sc);
                        }
                        continue;
                    }
                    if (el.Key != "game.desc" && !CardKeys.Contains(el.Key)) continue;
                    var s = MasterSize(el.Key);
                    foreach (string name in el.Names)
                    {
                        var fe = UiTools.FindByName(win, name);
                        if (fe == null) continue;
                        if (s[0] > 0) { fe.Width = s[0]; if (el.Key == "game.desc") fe.MaxWidth = Math.Max(1000, s[0]); }
                        else { fe.ClearValue(FrameworkElement.WidthProperty); if (el.Key == "game.desc") fe.MaxWidth = 1000; }
                        if (s[1] > 0) { fe.Height = s[1]; if (el.Key == "game.desc") fe.VerticalAlignment = VerticalAlignment.Top; }
                        else { fe.ClearValue(FrameworkElement.HeightProperty); if (el.Key == "game.desc") fe.ClearValue(FrameworkElement.VerticalAlignmentProperty); }
                        if (el.Key == "game.desc") FitDescSlot(fe, s[1]);
                    }
                }
                ApplyMasterRotations();   // (after the scale: the turn goes first, so it turns about the part's own middle)
                if (boxWaiting) BoxRetrySoon();
            }
            catch (Exception ex) { logger.Warn("Kippix: sizes: " + ex.Message); }
        }

        private Guid boxGame;
        private int boxTries;
        private DispatcherTimer boxTimer;

        /// <summary>A sized part wasn't laid out yet (the game page opening): tried again shortly, a few times (not only on the next tick).</summary>
        private void BoxRetrySoon()
        {
            if (ui == null || !detailsOpen || boxTries >= 8) return;
            boxTries++;
            if (boxTimer == null)
            {
                boxTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(150) };
                boxTimer.Tick += (s, e) => { boxTimer.Stop(); try { ApplyMasterSizes(); } catch (Exception) { } };
            }
            if (!boxTimer.IsEnabled) boxTimer.Start();
        }

        /// <summary>The games in one row / a grid back to their own size (card size automatic, the grid's own columns).</summary>
        private void ResetCardsSize()
        {
            if (LayoutNow == 0) settings.CardSize = -1;
            else if (LayoutNow == 1) settings.GridColumns = new HelperSettings().GridColumns;
            else if (LayoutNow == 6) settings.JustRows = new HelperSettings().JustRows;
            SaveSettings();
            NotifyCardSettings();
            ApplyCardLayoutSoon();
            HomeModeChanged();
        }

        /// <summary>
        /// The description is the last thing in its column and only gets the room left at the bottom: made taller than that,
        /// WPF cuts it off at the edge of that room. A negative bottom margin lets it run past it, so all of it shows.
        /// </summary>
        private void FitDescSlot(FrameworkElement fe, double height)
        {
            if (height <= 0) { fe.ClearValue(FrameworkElement.MarginProperty); return; }
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(fe);
                    if (slot.Height <= 0) return;
                    double over = height + fe.Margin.Top - slot.Height;
                    var m = fe.Margin;
                    double want = over > 0 ? -over : 0;
                    if (Math.Abs(m.Bottom - want) > 0.5) fe.Margin = new Thickness(m.Left, m.Top, m.Right, want);
                }
                catch (Exception) { }
            }), DispatcherPriority.Loaded);
        }

        // ---------------- the game page's parts: sized like a widget ----------------

        /// <summary>What a part had of its own before it was sized like a widget (put back when its size goes).</summary>
        private class BoxWas
        {
            public object Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight, Margin, ZIndex;
            public FrameworkElement Inner;     // a card's inside (laid out at the card's width, then fitted by the card's Viewbox)
            public object InnerWidth;
            public double OwnW, OwnH;           // its own size (no size of ours on it), last worked out at OwnAt
            public Thickness OwnMargin;         // ... its own margin then (from the theme's style, or its own)
            public double InnerOwn = double.NaN; // ... and its inside's own width
            public DateTime OwnAt = DateTime.MinValue;
        }

        private readonly Dictionary<FrameworkElement, BoxWas> boxWas = new Dictionary<FrameworkElement, BoxWas>();

        private static readonly DependencyProperty[] BoxProps =
        {
            FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, FrameworkElement.MinWidthProperty, FrameworkElement.MinHeightProperty,
            FrameworkElement.MaxWidthProperty, FrameworkElement.MaxHeightProperty, FrameworkElement.MarginProperty, System.Windows.Controls.Panel.ZIndexProperty
        };

        /// <summary>The theme sets this one itself (a binding or resource on it): left alone.</summary>
        private static bool BoxBound(DependencyObject d, DependencyProperty p)
        {
            return d.ReadLocalValue(p) is Expression;
        }

        private static void BoxPut(DependencyObject d, DependencyProperty p, object was)
        {
            if (was == DependencyProperty.UnsetValue) d.ClearValue(p);
            else d.SetValue(p, was);
        }

        /// <summary>Its own size, layout and inside back (what it had before it was sized).</summary>
        private static void BoxRestore(FrameworkElement fe, BoxWas w)
        {
            BoxPut(fe, FrameworkElement.WidthProperty, w.Width);
            BoxPut(fe, FrameworkElement.HeightProperty, w.Height);
            BoxPut(fe, FrameworkElement.MinWidthProperty, w.MinWidth);
            BoxPut(fe, FrameworkElement.MinHeightProperty, w.MinHeight);
            BoxPut(fe, FrameworkElement.MaxWidthProperty, w.MaxWidth);
            BoxPut(fe, FrameworkElement.MaxHeightProperty, w.MaxHeight);
            BoxPut(fe, FrameworkElement.MarginProperty, w.Margin);
            BoxPut(fe, System.Windows.Controls.Panel.ZIndexProperty, w.ZIndex);
            if (w.Inner != null) BoxPut(w.Inner, FrameworkElement.WidthProperty, w.InnerWidth);
        }

        /// <summary>A game page card's inside: the ContentPresenter its template lays out at a set width inside a Viewbox.</summary>
        private static FrameworkElement BoxInner(FrameworkElement fe)
        {
            var vb = FindVisual<System.Windows.Controls.Viewbox>(fe, 4);
            var inner = vb != null ? vb.Child as FrameworkElement : null;
            return inner != null && !double.IsNaN(inner.Width) && inner.Width > 0 ? inner : null;
        }

        private static T FindVisual<T>(DependencyObject root, int depth) where T : class
        {
            if (root == null || depth < 0) return null;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                var t = c as T;
                if (t != null) return t;
                var deeper = FindVisual<T>(c, depth - 1);
                if (deeper != null) return deeper;
            }
            return null;
        }

        /// <summary>A size kept from before 0.74, when these parts were scaled as a whole: [scale, 0, ...].</summary>
        private static bool IsOldScale(string key, double[] s)
        {
            return !CardKeys.Contains(key) && s[0] > 0 && s[0] <= 3.0 && s[1] <= 0;
        }

        /// <summary>
        /// Its own size (what it takes in the page with none of ours on it): its own values are put back, it's measured in the room
        /// its parent gave it, then ours go on again (in the same moment, so nothing is drawn in between). Worked out again at most
        /// every 1.5 s (its inside can change: a game's details coming in), or when <paramref name="now"/>.
        /// </summary>
        private bool BoxOwn(FrameworkElement fe, BoxWas w, bool now, out double ownW, out double ownH)
        {
            ownW = w.OwnW; ownH = w.OwnH;
            if (!now && w.OwnW > 0 && (DateTime.Now - w.OwnAt).TotalMilliseconds < 1500) return true;
            BoxRestore(fe, w);
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(fe);
            fe.Measure(new Size(slot.Width > 0 ? slot.Width : double.PositiveInfinity, double.PositiveInfinity));
            var m = fe.Margin;
            double cw = fe.HorizontalAlignment == HorizontalAlignment.Stretch && slot.Width > 0 && double.IsNaN(fe.Width) ? slot.Width - m.Left - m.Right : fe.DesiredSize.Width - m.Left - m.Right;
            double ch = fe.DesiredSize.Height - m.Top - m.Bottom;
            if (cw < 4 || ch < 4) { if (w.OwnW > 0) { ownW = w.OwnW; ownH = w.OwnH; return true; } return false; }   // (not laid out yet)
            w.OwnW = ownW = cw;
            w.OwnH = ownH = ch;
            w.OwnMargin = m;
            w.InnerOwn = w.Inner != null ? w.Inner.Width : double.NaN;   // (the template's own width, now that ours is off)
            w.OwnAt = DateTime.Now;
            return true;
        }

        /// <summary>
        /// One part sized like a widget: drawn at width × height from its own top left corner, floating (it keeps its own room in
        /// the layout: a negative margin gives back what it grew by, so the parts round it stay put, and it draws over them).
        /// Inside, it's laid out at the size ÷ k and scaled by k, where k = the smaller of width ÷ its own width and height ÷ its own
        /// height: made wider (or taller) only, the inside gets the room (laid out wider, text the same size); bigger both ways,
        /// the text and icons grow with it; smaller, they shrink so nothing is cut off. A card's inside (laid out at a set width in
        /// its Viewbox) is given the new width too.
        /// </summary>
        /// <returns>True when it's waiting to be laid out (sized, showing, but not measured yet).</returns>
        private bool ApplyBoxSize(MasterElement el, FrameworkElement fe, double[] s, bool fresh)
        {
            BoxWas w;
            boxWas.TryGetValue(fe, out w);
            bool sized = s[0] > 0 || s[1] > 0;
            if (!sized)
            {
                if (w == null) return false;
                BoxRestore(fe, w);
                boxWas.Remove(fe);
                SetMasterScale(fe, 1);
                ScaleTransform sc0;
                if (masterScales.TryGetValue(fe, out sc0) && sc0 != null) CenterScale(fe, sc0);
                return false;
            }
            if (w == null)
            {
                foreach (var p in BoxProps) if (BoxBound(fe, p)) return false;   // the theme sets its size or place itself: it keeps them
                w = new BoxWas
                {
                    Width = fe.ReadLocalValue(FrameworkElement.WidthProperty), Height = fe.ReadLocalValue(FrameworkElement.HeightProperty),
                    MinWidth = fe.ReadLocalValue(FrameworkElement.MinWidthProperty), MinHeight = fe.ReadLocalValue(FrameworkElement.MinHeightProperty),
                    MaxWidth = fe.ReadLocalValue(FrameworkElement.MaxWidthProperty), MaxHeight = fe.ReadLocalValue(FrameworkElement.MaxHeightProperty),
                    Margin = fe.ReadLocalValue(FrameworkElement.MarginProperty), ZIndex = fe.ReadLocalValue(System.Windows.Controls.Panel.ZIndexProperty)
                };
                if (CardKeys.Contains(el.Key))
                {
                    w.Inner = BoxInner(fe);
                    if (w.Inner != null)
                    {
                        if (BoxBound(w.Inner, FrameworkElement.WidthProperty)) w.Inner = null;
                        else w.InnerWidth = w.Inner.ReadLocalValue(FrameworkElement.WidthProperty);
                    }
                }
                if (!fe.IsVisible) return fe.Visibility == Visibility.Visible;   // (taken over once it shows: its own size is read then)
                boxWas[fe] = w;
            }
            if (!fe.IsVisible || fe.ActualWidth <= 0) return fe.Visibility == Visibility.Visible;   // hidden (or not laid out yet): as it was, until it shows
            double ownW, ownH;
            if (!BoxOwn(fe, w, fresh, out ownW, out ownH)) { BoxRestore(fe, w); SetMasterScale(fe, 1); return true; }
            double W, H;
            BoxTarget(el.Key, s, ownW, ownH, out W, out H);
            double k = Math.Max(0.2, Math.Min(5, Math.Min(W / ownW, H / ownH)));
            double dw = W / k, dh = H / k;
            double innerOwn = w.InnerOwn;
            var m = w.OwnMargin;
            // (setting the same values again changes nothing: only a new size, or its own size changing, lays the page out again)
            fe.MinWidth = 0;
            fe.MinHeight = 0;
            fe.MaxWidth = double.PositiveInfinity;
            fe.MaxHeight = double.PositiveInfinity;
            fe.Width = Math.Round(dw, 2);
            fe.Height = Math.Round(dh, 2);
            // its room in the layout stays its own size: the parts after it don't move
            fe.Margin = new Thickness(m.Left, m.Top, m.Right - (fe.Width - ownW), m.Bottom - (fe.Height - ownH));
            System.Windows.Controls.Panel.SetZIndex(fe, 5);   // (over its neighbours, not under them)
            if (w.Inner != null && !double.IsNaN(innerOwn) && innerOwn > 0) w.Inner.Width = Math.Max(40, Math.Round(innerOwn + (fe.Width - ownW), 2));
            SetMasterScale(fe, k);
            ScaleTransform sc;
            if (masterScales.TryGetValue(fe, out sc) && sc != null) CenterScale(fe, sc);
            return false;
        }

        /// <summary>The size a part is drawn at from what's kept: [w, h] (0 = its own), or an old whole-part scale.</summary>
        private static void BoxTarget(string key, double[] s, double ownW, double ownH, out double W, out double H)
        {
            if (IsOldScale(key, s)) { W = ownW * s[0]; H = ownH * s[0]; return; }
            W = s[0] > 0 ? s[0] : ownW;
            H = s[1] > 0 ? s[1] : ownH;
        }

        /// <summary>The size it's drawn at now (its own when it has none of ours). False when it isn't showing.</summary>
        private bool BoxSizeNow(MasterElement el, out double W, out double H)
        {
            W = H = 0;
            var fe = SizedElement(el);
            if (fe == null || !fe.IsVisible) return false;
            BoxWas w;
            double ownW, ownH;
            if (boxWas.TryGetValue(fe, out w)) { ownW = w.OwnW; ownH = w.OwnH; }   // (worked out as it was sized; read only here: this runs for the line of text too)
            else { ownW = fe.ActualWidth; ownH = fe.ActualHeight; }
            if (ownW <= 0 || ownH <= 0) return false;
            BoxTarget(el.Key, MasterSize(el.Key), ownW, ownH, out W, out H);
            return true;
        }

        /// <summary>
        /// The D-pad while sizing a game page part: Right / Down pull the right / bottom edge out, Left / Up push it back in (X: the
        /// top / left edges, Left / Up pulling them out). The edge not being pulled stays where it is.
        /// </summary>
        private void BoxResizeBy(MasterElement el, int dx, int dy)
        {
            double W, H;
            if ((dx == 0 && dy == 0) || !BoxSizeNow(el, out W, out H)) { PlaySound("back"); return; }
            int gx = sizeEdge == 0 ? dx : -dx, gy = sizeEdge == 0 ? dy : -dy;   // top / left edge: Left / Up make it bigger
            double step = SizeStep;
            double w = gx != 0 ? Math.Max(60, Math.Min(1880, Math.Round(W + gx * step))) : W;
            double h = gy != 0 ? Math.Max(40, Math.Min(1040, Math.Round(H + gy * step))) : H;
            double dW = w - W, dH = h - H;
            if (Math.Abs(dW) < 0.5 && Math.Abs(dH) < 0.5) { PlaySound("back"); return; }
            // it grows from its top left corner: pulling the top / left edge moves it by what it grew, so the other edge stays
            double sx = sizeEdge == 0 ? 0 : -dW, sy = sizeEdge == 0 ? 0 : -dH;
            var s = MasterSize(el.Key);
            if (IsOldScale(el.Key, s)) { s[0] = 0; s[1] = 0; }
            s[0] = Math.Round(w); s[1] = Math.Round(h);
            var o = MasterOffset(el.Key);
            double nx = Math.Max(-1900, Math.Min(1900, o[0] + sx)), ny = Math.Max(-1060, Math.Min(1060, o[1] + sy));
            s[2] += nx - o[0]; s[3] += ny - o[1];   // (what it really moved: the place is kept within the screen's range)
            StoreMasterSize(el.Key, s);
            SetMasterOffset(el.Key, nx, ny);
            PlaySound("move");
            ApplyMasterOffsets();
            LaterOutline();
        }

        // ---------------- the game page cards' look ----------------

        /// <summary>A card's background: 0 glass (as it comes), 1 solid, 2 none (just what's inside).</summary>
        private int DetCardLook(string part)
        {
            int v;
            return settings.DetCardLooks != null && settings.DetCardLooks.TryGetValue(part, out v) ? Math.Max(0, Math.Min(2, v)) : 0;
        }

        private string DetCardLookText(string part)
        {
            int v = DetCardLook(part);
            return v == 1 ? Loc("LOC_KX_CardSolid", "Solid") : v == 2 ? Loc("LOC_KX_CardNoBack", "None") : Loc("LOC_KX_CardGlass", "See-through");
        }

        private void CycleDetCardLook(string part)
        {
            if (settings.DetCardLooks == null) settings.DetCardLooks = new Dictionary<string, int>();
            int v = (DetCardLook(part) + 1) % 3;
            if (v == 0) settings.DetCardLooks.Remove(part); else settings.DetCardLooks[part] = v;
            SaveSettings();
            PlaySound("move");
            ApplyDetCardLooks();
        }

        /// <summary>The look goes on the card as its Tag ("solid" / "none"); the card's template does the rest.</summary>
        private void ApplyDetCardLooks()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                if (details == null) return;
                foreach (var k in new[] { "history", "hltb", "ach", "status", "notes", "scores", "saves", "mods", "news" })
                    foreach (string name in DetNames(k))
                    {
                        var fe = UiTools.FindByName(details, name);
                        if (fe == null) continue;
                        int v = DetCardLook(k);
                        fe.Tag = v == 1 ? "solid" : v == 2 ? "none" : null;
                        var ctl = fe as System.Windows.Controls.Control;
                        if (ctl != null)
                        {
                            if (v == 1) ctl.Background = Frozen(ParseColor(DetCardColor(k)) ?? System.Windows.Media.Color.FromRgb(0x15, 0x17, 0x1C));   // the card's template shows this when it's Solid
                            else ctl.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                        }
                    }
            }
            catch (Exception) { }
        }
    }
}
