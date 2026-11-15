using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KippixHelper
{
    /// <summary>
    /// Card shapes. 0 tall cover (Playnite's own), 1 square, 2 wide, 3 circle, 4 hexagon, 5 triangle pointing up,
    /// 6 triangle pointing down. The outline is a Geometry (a Freezable): it is always made and set here in code, never
    /// through a PluginSettings binding (that crashed fullscreen on load).
    /// </summary>
    public static class CardShapes
    {
        public const int Tall = 0, Square = 1, Wide = 2, Circle = 3, Hexagon = 4, TriUp = 5, TriDown = 6, Count = 7;

        /// <summary>Cut to a shape (not one of the rounded rectangles).</summary>
        public static bool IsCut(int s) { return s >= Circle && s <= TriDown; }
        public static bool IsTriangle(int s) { return s == TriUp || s == TriDown; }

        /// <summary>The card's width : height for the shape (0 = Playnite's own cover shape).</summary>
        public static void Aspect(int s, out int w, out int h)
        {
            switch (s)
            {
                case Square: case Circle: w = 1; h = 1; return;
                case Wide: w = 16; h = 9; return;
                case Hexagon: w = 13; h = 15; return;      // a pointy-top hexagon: 0.866 : 1
                case TriUp: case TriDown: w = 15; h = 13; return;   // equilateral: 1 : 0.866
                default: w = 0; h = 0; return;
            }
        }

        /// <summary>The outline in a 0..1 box (for a Path that stretches it to the card).</summary>
        public static Geometry Unit(int s) { return Sized(s, 1, 1); }

        /// <summary>The outline at a size (a clip for the card's picture). Null for the rounded rectangles.</summary>
        public static Geometry Sized(int s, double w, double h)
        {
            if (w <= 0 || h <= 0) return null;
            Geometry g;
            switch (s)
            {
                case Circle: g = new EllipseGeometry(new Rect(0, 0, w, h)); break;
                case Hexagon: g = Poly(new Point(w / 2, 0), new Point(w, h * 0.25), new Point(w, h * 0.75), new Point(w / 2, h), new Point(0, h * 0.75), new Point(0, h * 0.25)); break;
                case TriUp: g = Poly(new Point(w / 2, 0), new Point(w, h), new Point(0, h)); break;
                case TriDown: g = Poly(new Point(0, 0), new Point(w, 0), new Point(w / 2, h)); break;
                default: return null;
            }
            g.Freeze();
            return g;
        }

        /// <summary>A rounded rectangle at a size (the other shapes, for cards drawn by the theme itself).</summary>
        public static Geometry Rounded(double w, double h, double r)
        {
            if (w <= 0 || h <= 0) return null;
            var g = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            g.Freeze();
            return g;
        }

        private static Geometry Poly(params Point[] pts)
        {
            var f = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
            for (int i = 1; i < pts.Length; i++) f.Segments.Add(new LineSegment(pts[i], true));
            var g = new PathGeometry();
            g.Figures.Add(f);
            return g;
        }

        /// <summary>Triangles take turns pointing up and down (so a row of them fits together): the shape at a place in the row.</summary>
        public static int Alternate(int s, int place, bool alternate)
        {
            if (!IsTriangle(s) || !alternate || place % 2 == 0) return s;
            return s == TriUp ? TriDown : TriUp;
        }

        /// <summary>Where the small badges go so the shape doesn't cut them off: margins for the top and the bottom group.</summary>
        public static void BadgePlaces(int s, double w, double h, out Thickness top, out Thickness bottom, out HorizontalAlignment align)
        {
            align = HorizontalAlignment.Left;
            top = new Thickness(8);
            bottom = new Thickness(8);
            switch (s)
            {
                case Circle:
                case Hexagon:
                    align = HorizontalAlignment.Center;
                    top = new Thickness(0, h * 0.13, 0, 0);
                    bottom = new Thickness(0, 0, 0, h * 0.13);
                    break;
                case TriUp:
                    align = HorizontalAlignment.Center;
                    top = new Thickness(0, h * 0.5, 0, 0);
                    bottom = new Thickness(0, 0, 0, 8);
                    break;
                case TriDown:
                    align = HorizontalAlignment.Center;
                    top = new Thickness(0, 8, 0, 0);
                    bottom = new Thickness(0, 0, 0, h * 0.5);
                    break;
            }
        }
    }

    public partial class ThemeApi
    {
        // =====================================================================
        // The cover row / grid / list (Playnite's own cards): shape, ring, badges, honeycomb / triangle packing
        // =====================================================================

        private bool ShapesOn { get { return CardShapes.IsCut(settings.CardShape) && LayoutNow != 6; } }   // (the mosaic: every cover its own shape, never cut)
        private bool shapesWereOn;

        /// <summary>A card's own shape (from the setting, triangles taking turns), where it is in the list.</summary>
        private int ShapeAt(int index, int cols)
        {
            int s = settings.CardShape;
            if (!CardShapes.IsTriangle(s)) return s;
            int place = index;
            if (LayoutNow == 1 && cols > 0) place = index / cols + index % cols;   // the grid: row + column (a chequerboard)
            return TriShapeAt(place);
        }

        /// <summary>Marks a card moved by the honeycomb / triangle packing (so it's put back when the shape changes).</summary>
        public static readonly DependencyProperty PackedProperty = DependencyProperty.RegisterAttached("BBPacked", typeof(bool), typeof(ThemeApi), new PropertyMetadata(false));
        /// <summary>The shape a card's picture is cut to (read again when the card changes size).</summary>
        public static readonly DependencyProperty CutShapeProperty = DependencyProperty.RegisterAttached("BBCutShape", typeof(int), typeof(ThemeApi), new PropertyMetadata(-1, null));
        /// <summary>The element already re-cuts itself when it changes size (the handler is added once).</summary>
        public static readonly DependencyProperty CutHookedProperty = DependencyProperty.RegisterAttached("BBCutHooked", typeof(bool), typeof(ThemeApi), new PropertyMetadata(false));

        /// <summary>Every card on screen: its shape and what goes with it. Runs with the wide-art pass (cards are reused as you scroll).</summary>
        private void ApplyCardShapes(ListBox list)
        {
            bool on = ShapesOn;
            if (on) shapesWereOn = true;
            if (!on && !shapesWereOn) return;
            int cols = 0;
            try
            {
                var panel = UiTools.FindChild<VirtualizingPanel>(list);
                if (panel != null && colsProp != null && panel.GetValue(colsProp) is int) cols = (int)panel.GetValue(colsProp);
            }
            catch (Exception) { }
            bool grid = LayoutNow == 1 && cols > 0;
            bool listMode = LayoutNow == 2;
            for (int i = 0; i < list.Items.Count; i++)
            {
                var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (c == null) continue;
                int s = on ? ShapeAt(i, cols) : -1;
                ShapeCard(c, s, listMode);
                PackCard(c, s, i, cols, grid && on);
            }
        }

        /// <summary>One card: its picture cut to the shape (or back to the rounded card), the ring and shadow, badges, logo.</summary>
        private void ShapeCard(FrameworkElement c, int s, bool listMode)
        {
            bool cut = CardShapes.IsCut(s);
            var root = UiTools.FindByName(c, "Root");
            if (root != null)
            {
                if (cut && !listMode) root.OpacityMask = null;   // the rounded mask would cut the shape's corners
                else root.ClearValue(UIElement.OpacityMaskProperty);
                if (!(cut && !listMode)) root.ClearValue(UIElement.ClipProperty);
            }
            // the list: only the picture at the start of the line takes the shape (the words beside it stay a box);
            // the row / grid: the whole card. Whichever isn't cut loses a cut left from the other layout.
            var img = UiTools.FindByName(c, "KX_WideImage") as FrameworkElement;
            if (listMode)
            {
                if (root != null) root.ClearValue(CutShapeProperty);
                if (img != null)
                {
                    if (cut) SetCut(img, s); else { img.ClearValue(UIElement.ClipProperty); img.ClearValue(CutShapeProperty); }
                }
            }
            else
            {
                if (img != null && img.ReadLocalValue(CutShapeProperty) != DependencyProperty.UnsetValue) { img.ClearValue(UIElement.ClipProperty); img.ClearValue(CutShapeProperty); }
                if (root != null) { if (cut) SetCut(root, s); else root.ClearValue(CutShapeProperty); }
            }

            // ring and shadow follow the shape; the rounded ones hide
            var ring = UiTools.FindByName(c, "FocusRing");
            var shapeRing = UiTools.FindByName(c, "KX_ShapeRing") as Path;
            var shadow = UiTools.FindByName(c, "KX_TileShadow");
            var shapeShadow = UiTools.FindByName(c, "KX_ShapeShadow") as Path;
            bool shapedRing = cut && !listMode;
            if (ring != null) ring.Visibility = shapedRing ? Visibility.Hidden : Visibility.Visible;
            if (shadow != null) shadow.Visibility = shapedRing ? Visibility.Hidden : Visibility.Visible;
            if (shapeRing != null)
            {
                shapeRing.Visibility = shapedRing ? Visibility.Visible : Visibility.Collapsed;
                if (shapedRing) { var g = CardShapes.Unit(s); if (!SameShape(shapeRing.Data, g)) shapeRing.Data = g; }
            }
            if (shapeShadow != null)
            {
                shapeShadow.Visibility = shapedRing ? Visibility.Visible : Visibility.Collapsed;
                if (shapedRing) { var g = CardShapes.Unit(s); if (!SameShape(shapeShadow.Data, g)) shapeShadow.Data = g; }
            }

            // badges inside the shape
            var top = UiTools.FindByName(c, "KX_BadgesTop");
            var bottom = UiTools.FindByName(c, "KX_BadgesBottom");
            double w = root != null && root.ActualWidth > 0 ? root.ActualWidth : c.ActualWidth, h = root != null && root.ActualHeight > 0 ? root.ActualHeight : c.ActualHeight;
            if (!listMode && (top != null || bottom != null))
            {
                Thickness tm, bm;
                HorizontalAlignment ha;
                CardShapes.BadgePlaces(cut ? s : -1, w, h, out tm, out bm, out ha);
                if (top != null) { top.Margin = tm; top.HorizontalAlignment = ha; }
                if (bottom != null) { bottom.Margin = bm; bottom.HorizontalAlignment = ha; }
            }

            // triangles show the game's logo on its art, centred where the triangle is widest
            var logo = UiTools.FindByName(c, "KX_WideLogo");
            if (logo != null && !listMode)
            {
                if (CardShapes.IsTriangle(s))
                {
                    logo.HorizontalAlignment = HorizontalAlignment.Center;
                    logo.VerticalAlignment = s == CardShapes.TriUp ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                    logo.Margin = s == CardShapes.TriUp ? new Thickness(0, 0, 0, h * 0.1) : new Thickness(0, h * 0.1, 0, 0);
                    logo.MaxWidth = Math.Max(40, w * 0.5);
                    logo.MaxHeight = Math.Max(20, h * 0.28);
                }
                else
                {
                    logo.HorizontalAlignment = HorizontalAlignment.Left;
                    logo.VerticalAlignment = VerticalAlignment.Bottom;
                    logo.Margin = new Thickness(14, 0, 14, 12);
                }
            }
        }

        private static bool SameShape(Geometry a, Geometry b)
        {
            if (a == null || b == null) return a == b;
            return a.GetType() == b.GetType() && a.Bounds == b.Bounds && a.ToString() == b.ToString();
        }

        /// <summary>Cuts an element's picture to a shape, and keeps it cut when the element changes size.</summary>
        private void SetCut(FrameworkElement el, int s)
        {
            bool first = !(bool)el.GetValue(CutHookedProperty);
            el.SetValue(CutHookedProperty, true);
            el.SetValue(CutShapeProperty, s);
            el.Clip = CardShapes.Sized(s, el.ActualWidth, el.ActualHeight);
            if (first)
                el.SizeChanged += (o, e) =>
                {
                    var fe = o as FrameworkElement;
                    if (fe == null || fe.ReadLocalValue(CutShapeProperty) == DependencyProperty.UnsetValue) return;
                    int shape = (int)fe.GetValue(CutShapeProperty);
                    if (CardShapes.IsCut(shape)) fe.Clip = CardShapes.Sized(shape, fe.ActualWidth, fe.ActualHeight);
                };
        }

        /// <summary>
        /// The grid: hexagons sit in a honeycomb (every other row half a card across), triangles fit together (each half a
        /// card in from the last). Only the picture moves (a render shift), so Playnite's own Up / Down / Left / Right, which go
        /// by row and column, still go to the card in the next row / column.
        /// </summary>
        private void PackCard(FrameworkElement c, int s, int index, int cols, bool grid)
        {
            double x = 0;
            bool pack = grid && s == CardShapes.Hexagon;   // (triangles are pushed together by their spacing, in ApplyCardSpacingTo)
            if (pack)
            {
                double w = c.ActualWidth;
                int row = index / cols, col = index % cols;
                if (s == CardShapes.Hexagon) x = (row % 2 == 1 ? w / 2 : 0) - w / 4;
                else x = -col * w / 2 + (cols - 1) * w / 4;
            }
            bool was = (bool)c.GetValue(PackedProperty);
            if (!pack)
            {
                if (was) { c.RenderTransform = Transform.Identity; c.SetValue(PackedProperty, false); }
                return;
            }
            var t = c.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen || !was) { t = new TranslateTransform(); c.RenderTransform = t; }
            if (Math.Abs(t.X - x) > 0.5) t.X = x;
            if (Math.Abs(t.Y) > 0.5) t.Y = 0;
            c.SetValue(PackedProperty, true);
        }

        // =====================================================================
        // Settings
        // =====================================================================

        /// <summary>The Triangle shape at a place: taking turns (up, down, up...), all up, or all down.</summary>
        private int TriShapeAt(int place)
        {
            if (settings.TriArrange == 1) return CardShapes.TriUp;
            if (settings.TriArrange == 2) return CardShapes.TriDown;
            return CardShapes.Alternate(CardShapes.TriUp, place, true);
        }

        private ICommand cycleTriCmd;
        public ICommand CycleTriArrangeCommand
        {
            get
            {
                return cycleTriCmd ?? (cycleTriCmd = new RelayCommand(_ =>
                {
                    settings.TriArrange = (settings.TriArrange + 1) % 3;
                    SaveSettings();
                    PlaySound("move");
                    Notify("TriArrangeText");
                    ApplyCardLayoutSoon();   // the cards' slots follow how they fit together
                    RefreshTileArtSoon();
                    RebuildExtraRowsSoon();
                }));
            }
        }
        public string TriArrangeText
        {
            get
            {
                return settings.TriArrange == 1 ? Loc("LOC_KX_TriAllUp", "All pointing up")
                     : settings.TriArrange == 2 ? Loc("LOC_KX_TriAllDown", "All pointing down")
                     : Loc("LOC_KX_TriTurns", "Up, down, up… (they fit together)");
            }
        }
        public bool IsTriangleShape { get { return CardShapes.IsTriangle(settings.CardShape); } }
        public bool IsNotTriangleShape { get { return !CardShapes.IsTriangle(settings.CardShape); } }
        // the Game cards page: which rows show for the layout and shape (the Manual Board has its own per tile)
        // (what's chosen, not what's on screen: in Folder the folder tiles may be showing while the settings are about its games)
        public bool IsNotBoardMode { get { return !BoardSettingsShown; } }
        /// <summary>Row › Tall / Full Screen is chosen (for the home screen or a folder's games): their cards are the default card.</summary>
        private bool RowPanelsChosen { get { return RowSettingsShown && settings.RowDesign != 0; } }
        public bool ShowCardLook { get { return !IsManualMode && settings.HomeLayoutMode != 6; } }   // (0.89: Tall / Full Screen too, for the card shape)
        public bool ShowCardSpacing { get { return IsBoardOnly || (!BoardSettingsShown && !RowPanelsChosen && !CardShapes.IsTriangle(settings.CardShape) && settings.HomeLayoutMode != 6); } }
        public bool ShowTriArrange { get { return !BoardSettingsShown && CardShapes.IsTriangle(settings.CardShape) && settings.HomeLayoutMode != 6; } }
        public bool ShowTriSpacing { get { return !BoardSettingsShown && !RowPanelsChosen && TrianglesPacked; } }
        private ICommand resetTriCmd;
        public ICommand ResetTriSpacingCommand
        {
            get
            {
                return resetTriCmd ?? (resetTriCmd = new RelayCommand(_ =>
                {
                    settings.TriSpacing = new HelperSettings().TriSpacing;   // A on the slider: as it comes
                    SaveSettings(); PlaySound("move"); ApplyCardLayoutSoon(); RefreshTileArtSoon(); RebuildExtraRowsSoon(); UpdateSliders();
                }));
            }
        }
        /// <summary>Triangles in the row / grid / extra rows: from one to the next, as a part of a triangle's width (taking turns: they overlap by half).</summary>
        private double TriStep { get { return (settings.TriArrange == 0 ? 0.5 : 1.0) + settings.TriSpacing / 100.0; } }
        private bool TrianglesPacked { get { return CardShapes.IsTriangle(settings.CardShape) && LayoutNow != 2 && LayoutNow != 6; } }

        private SliderModel triSlider;
        public SliderModel TriSpacingSlider { get { return triSlider ?? (triSlider = new SliderModel()); } }
        private void UpdateTriSlider()
        {
            TriSpacingSlider.Text = settings.TriSpacing == 0 ? Loc("LOC_KX_TriTouching", "Touching") : settings.TriSpacing + "%";
            TriSpacingSlider.SetFill(settings.TriSpacing / 40.0);
        }

        internal static string ShapeName(int s)
        {
            switch (s)
            {
                case CardShapes.Square: return Loc("LOC_KX_ShapeSquare", "Square");
                case CardShapes.Wide: return Loc("LOC_KX_ShapeWide", "Wide");
                case CardShapes.Circle: return Loc("LOC_KX_ShapeCircle", "Circle");
                case CardShapes.Hexagon: return Loc("LOC_KX_ShapeHexagon", "Hexagon");
                case CardShapes.TriUp: return Loc("LOC_KX_ShapeTriUp", "Triangle, pointing up");
                case CardShapes.TriDown: return Loc("LOC_KX_ShapeTriDown", "Triangle, pointing down");
                default: return Loc("LOC_KX_ShapeRectangle", "Tall cover");
            }
        }

        // =====================================================================
        // The extra rows' tiles (drawn by the theme): their shape comes with the tile
        // =====================================================================

        /// <summary>A row tile's shape, outline, clip and badge places (place = its place in the row, for triangles taking turns).</summary>
        private void ShapeRowTile(RowTile t, int place)
        {
            int s = CardShapes.IsTriangle(settings.CardShape) ? TriShapeAt(place) : settings.CardShape;
            t.Shape = s;
            bool cut = CardShapes.IsCut(s);
            t.IsCut = cut;
            t.Clip = cut ? CardShapes.Sized(s, t.Width, t.Height) : CardShapes.Rounded(t.Width, t.Height, 10);
            t.Outline = cut ? CardShapes.Unit(s) : null;
            Thickness tm, bm;
            HorizontalAlignment ha;
            CardShapes.BadgePlaces(cut ? s : -1, t.Width, t.Height, out tm, out bm, out ha);
            t.BadgeTop = tm;
            t.BadgeBottom = bm;
            t.BadgeAlign = ha;
            // triangles taking turns fit together: each one half a card in from the last
            t.TileMargin = CardShapes.IsTriangle(settings.CardShape) ? new Thickness(0, 0, Math.Round(t.Width * TriStep - t.Width), 0) : new Thickness(0, 0, TileGapX, 0);
        }
    }
}
