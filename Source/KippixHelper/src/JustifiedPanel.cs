using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK;

namespace KippixHelper
{
    /// <summary>
    /// The Mosaic layout's panel: a justified gallery. Every tile keeps its own shape (AspectOf: width / height), the tiles of a
    /// row share one height and each row is packed edge to edge; the height is worked out per row so the row fills the width
    /// exactly, as near TargetRowHeight as it can be. The last row keeps the target height and starts at the left.
    ///
    /// It stands in for Playnite's FullscreenTilePanel as the games list's items panel (ThemeApi.Justified.cs swaps it in), so
    /// like that one it makes only the tiles on screen (plus a row either side, plus the selected and the highlighted tile,
    /// wherever they are) and does the scrolling itself (IScrollInfo, downwards only).
    /// </summary>
    public class JustifiedPanel : VirtualizingPanel, IScrollInfo
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const double MinAspect = 0.45, MaxAspect = 2.4, DefaultAspect = 2.0 / 3.0;
        private const double MaxRowGrow = 1.6;      // a row is never taller than this many target heights (it stops filling the width instead)
        private const double BottomRoom = 10;       // under the last row, for its shadow

        /// <summary>Called for every new panel (the list makes one from its template whenever it's rebuilt): the helper sets it up here.</summary>
        public static Action<JustifiedPanel> Setup;

        // the stopwatch (ThemeApi.JustifiedPerf.cs): off unless Perf is set
        public static bool Perf;
        public static Action PerfDump;
        public static int PerfMeasures, PerfMade, PerfModels, PerfArranges;
        public static double PerfMeasureMs, PerfMeasureWorst, PerfArrangeMs;

        private struct Row
        {
            public int First, Count;
            public double Top, Height;
        }

        private readonly List<Row> rows = new List<Row>();
        private double[] itemX = new double[0], itemW = new double[0];
        private int[] itemRow = new int[0];
        private int modelCount;
        private double modelWidth = -1;
        private bool modelValid;
        private double marginX, marginY;            // the tile's own margins inside its container (left + right, top + bottom)
        private bool resetPending, hadFocusAtReset, keepSelectedInView;
        private int failures;

        private Func<object, double> aspectOf;
        private double targetRowHeight = 240, edgePadding;

        /// <summary>An item's width / height (clamped to 0.45 - 2.4 here).</summary>
        public Func<object, double> AspectOf { get { return aspectOf; } set { aspectOf = value; InvalidateModel(); } }
        /// <summary>The height the rows aim for (each row ends up a little over or under, so it fills the width).</summary>
        public double TargetRowHeight { get { return targetRowHeight; } set { if (Math.Abs(targetRowHeight - value) < 0.5) return; targetRowHeight = value; InvalidateModel(); } }
        /// <summary>Room kept free at the left and right edges (the highlighted tile grows a little).</summary>
        public double EdgePadding { get { return edgePadding; } set { if (Math.Abs(edgePadding - value) < 0.5) return; edgePadding = value; InvalidateModel(); } }
        /// <summary>Scrolling slides (off: it jumps).</summary>
        public bool SmoothScroll { get; set; }
        /// <summary>After a layout pass that made new tiles.</summary>
        public event EventHandler ContainersGenerated;

        public int RowCount { get { return rows.Count; } }
        public int ItemCount { get { return modelCount; } }

        public JustifiedPanel()
        {
            RenderTransform = trans;
            var setup = Setup;
            if (setup != null) try { setup(this); } catch (Exception ex) { logger.Warn("Kippix: mosaic: panel set-up: " + ex.Message); }
        }

        private ItemsControl Owner { get { return ItemsControl.GetItemsOwner(this); } }

        // =====================================================================
        // The row model
        // =====================================================================

        /// <summary>The shapes (or the target height) changed: the rows are worked out again at the next layout.</summary>
        public void InvalidateModel()
        {
            modelValid = false;
            keepSelectedInView = true;
            InvalidateMeasure();
        }

        private double Aspect(object item)
        {
            double a = DefaultAspect;
            var f = aspectOf;
            if (f != null) try { a = f(item); } catch (Exception) { a = DefaultAspect; }
            if (double.IsNaN(a) || double.IsInfinity(a) || a <= 0) a = DefaultAspect;
            return Math.Max(MinAspect, Math.Min(MaxAspect, a));
        }

        /// <summary>
        /// One pass over the items. A row takes items while they fit at the target height; the one that doesn't fit either starts
        /// the next row (this row grows to fill the width) or is squeezed in (this row shrinks), whichever leaves the row nearer
        /// the target. A tile's slot is its picture (aspect x the row's inner height) plus the tile's own margins.
        /// </summary>
        private void BuildModel(ItemsControl owner, double width)
        {
            int n = owner != null ? owner.Items.Count : 0;
            rows.Clear();
            if (itemX.Length < n) { itemX = new double[n]; itemW = new double[n]; itemRow = new int[n]; }
            modelCount = n;
            modelWidth = width;
            modelValid = true;
            if (Perf) PerfModels++;
            double w = Math.Max(40, width - edgePadding * 2);
            double target = Math.Max(marginY + 20, targetRowHeight);
            double inner = target - marginY;                  // the picture's height at the target
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = Aspect(owner.Items[i]);

            double top = 0;
            int first = 0;
            while (first < n)
            {
                double sumA = 0;
                int count = 0;
                double h = target;
                bool fill = false;
                for (int i = first; i < n; i++)
                {
                    double natural = (sumA + a[i]) * inner + (count + 1) * marginX;
                    if (natural <= w) { sumA += a[i]; count++; continue; }
                    // this one doesn't fit at the target height
                    fill = true;
                    double with = (w - (count + 1) * marginX) / (sumA + a[i]) + marginY;
                    if (count == 0) { sumA = a[i]; count = 1; h = with; break; }   // (wider than the whole row by itself)
                    double without = (w - count * marginX) / sumA + marginY;
                    if (Math.Abs(with - target) < Math.Abs(without - target)) { sumA += a[i]; count++; h = with; }
                    else h = without;
                    break;
                }
                if (!fill) h = target;                                              // the last row: as it comes, from the left
                else if (h > target * MaxRowGrow) { h = target * MaxRowGrow; fill = false; }
                h = Math.Max(marginY + 20, h);

                // across: each slot, the ends rounded so a filled row ends exactly at the edge
                double pic = h - marginY, x = edgePadding, run = 0;
                for (int i = first; i < first + count; i++)
                {
                    double slot = a[i] * pic + marginX;
                    double next = run + slot;
                    double left = Math.Round(run), right = Math.Round(next);
                    if (fill && i == first + count - 1) right = Math.Round(w);
                    itemX[i] = x + left;
                    itemW[i] = Math.Max(1, right - left);
                    itemRow[i] = rows.Count;
                    run = next;
                }
                double rowH = Math.Round(h);
                rows.Add(new Row { First = first, Count = count, Top = top, Height = rowH });
                top += rowH;
                first += count;
            }
            var newExtent = new Size(width, n == 0 ? 0 : top + BottomRoom);
            if (newExtent != extent) { extent = newExtent; InvalidateScroll(); }
            logger.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Kippix: mosaic: row model: {0} items in {1} rows, width {2:0}, row height about {3:0}, {4:0} tall, tile margins {5:0} x {6:0}",
                n, rows.Count, width, targetRowHeight, extent.Height, marginX, marginY));
        }

        /// <summary>Where an item is, in the panel's own space (not moved by the scrolling). Empty when there's no such item.</summary>
        public Rect RectOf(int index)
        {
            if (!modelValid || index < 0 || index >= modelCount) return Rect.Empty;
            var r = rows[itemRow[index]];
            return new Rect(itemX[index], r.Top, itemW[index], r.Height);
        }

        public int RowOf(int index)
        {
            return modelValid && index >= 0 && index < modelCount ? itemRow[index] : -1;
        }

        /// <summary>
        /// The item a direction key lands on from <paramref name="index"/>; -1 when there's nothing that way. Left / Right go by
        /// position in the list (off the end of a row onto the next). Up / Down go to the row above / below: the item over
        /// <paramref name="stickyX"/>, or the nearest one. stickyX is where the run of Up / Down presses started (NaN: here), so
        /// going down a ragged column and back up ends where it began; Left / Right forget it.
        /// </summary>
        public int IndexFrom(int index, Key key, ref double stickyX)
        {
            if (!modelValid || modelCount == 0) return -1;
            if (index < 0 || index >= modelCount) return 0;
            if (key == Key.Left || key == Key.Right)
            {
                stickyX = double.NaN;
                int next = index + (key == Key.Right ? 1 : -1);
                return next >= 0 && next < modelCount ? next : -1;
            }
            if (key != Key.Up && key != Key.Down) return -1;
            if (double.IsNaN(stickyX)) stickyX = itemX[index] + itemW[index] / 2;
            int r = itemRow[index] + (key == Key.Down ? 1 : -1);
            if (r < 0 || r >= rows.Count) return -1;
            var row = rows[r];
            int best = row.First;
            double bestD = double.MaxValue;
            for (int i = row.First; i < row.First + row.Count; i++)
            {
                if (stickyX >= itemX[i] && stickyX < itemX[i] + itemW[i]) return i;
                double d = Math.Abs(itemX[i] + itemW[i] / 2 - stickyX);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // =====================================================================
        // Layout
        // =====================================================================

        protected override Size MeasureOverride(Size availableSize)
        {
            var size = new Size(double.IsInfinity(availableSize.Width) ? 1920 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 1080 : availableSize.Height);
            long t0 = Perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { MeasureRows(size); }
            catch (Exception ex)
            {
                // (an error here would take Playnite down: logged a few times and the pass given up instead)
                if (failures++ < 5) logger.Error(ex, "Kippix: mosaic: layout failed");
            }
            if (Perf)
            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                PerfMeasures++;
                PerfMeasureMs += ms;
                if (ms > PerfMeasureWorst) PerfMeasureWorst = ms;
            }
            return size;
        }

        private void MeasureRows(Size size)
        {
            var owner = Owner;
            if (owner == null) return;
            var children = InternalChildren;                    // (WPF: the generator isn't there until the children have been asked for)
            var gen = ItemContainerGenerator;
            if (gen == null) return;
            if (size != viewport) { viewport = size; InvalidateScroll(); }
            DropDeadChildren(children, owner);
            ReadTileMargins(children);

            int n = owner.Items.Count;
            bool rebuilt = false;
            if (!modelValid || n != modelCount || Math.Abs(size.Width - modelWidth) > 0.5) { BuildModel(owner, size.Width); rebuilt = true; }

            int selected = SelectedIndex(owner);
            if (resetPending)
            {
                // a new set of games (a filter, a refresh): from the top, unless a game is selected: then that one stays in view
                resetPending = false;
                if (selected < 0) offset = 0;
                keepSelectedInView = true;
            }
            if (rebuilt && keepSelectedInView && selected >= 0) offset = OffsetShowing(selected, offset);
            keepSelectedInView = false;
            ClampOffset();

            bool made = false;
            int firstItem = -1, lastItem = -1;
            if (n > 0 && rows.Count > 0)
            {
                int r0 = Math.Max(0, RowAt(offset) - 1);
                int r1 = Math.Min(rows.Count - 1, RowAt(offset + viewport.Height - 0.5) + 1);
                firstItem = rows[r0].First;
                lastItem = rows[r1].First + rows[r1].Count - 1;
                made |= Generate(gen, children, firstItem, lastItem);
                // the selected game's tile is always there (the highlight goes to it, wherever the list is scrolled to)
                if (selected >= 0 && selected < n && (selected < firstItem || selected > lastItem)) made |= Generate(gen, children, selected, selected);
            }

            // tiles no longer near the screen go back to the generator: never the selected one, never the one with the highlight
            var icg = owner.ItemContainerGenerator;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                int index = icg.IndexFromContainer(child);
                if (index < 0) { RemoveInternalChildRange(i, 1); continue; }
                if ((index >= firstItem && index <= lastItem) || index == selected || child.IsKeyboardFocusWithin) continue;
                var pos = gen.GeneratorPositionFromIndex(index);
                if (pos.Offset != 0) { RemoveInternalChildRange(i, 1); continue; }
                var recycling = gen as IRecyclingItemContainerGenerator;
                if (recycling != null) recycling.Recycle(pos, 1); else gen.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }

            // (the first tiles ever made tell us their margins: if that changes the rows, once more before anything is drawn)
            if (ReadTileMargins(children)) { modelValid = false; keepSelectedInView = true; InvalidateMeasure(); }

            if (made || hadFocusAtReset) AfterGenerating(owner, made);
        }

        private bool Generate(IItemContainerGenerator gen, UIElementCollection children, int from, int to)
        {
            bool made = false;
            var start = gen.GeneratorPositionFromIndex(from);
            using (gen.StartAt(start, GeneratorDirection.Forward, true))
            {
                for (int i = from; i <= to; i++)
                {
                    bool isNew;
                    var child = gen.GenerateNext(out isNew) as UIElement;
                    if (child == null) continue;
                    if (isNew || !children.Contains(child))
                    {
                        AddInternalChild(child);
                        gen.PrepareItemContainer(child);
                        made = true;
                        if (Perf) PerfMade++;
                    }
                    var r = rows[itemRow[i]];
                    child.Measure(new Size(itemW[i], r.Height));
                }
            }
            return made;
        }

        /// <summary>Tiles the generator has let go of (their game left the list) are taken off the panel.</summary>
        private void DropDeadChildren(UIElementCollection children, ItemsControl owner)
        {
            var icg = owner.ItemContainerGenerator;
            for (int i = children.Count - 1; i >= 0; i--)
                if (icg.IndexFromContainer(children[i]) < 0) RemoveInternalChildRange(i, 1);
        }

        /// <summary>The margin the tile's template keeps round the picture (Playnite's item spacing). True when it's not what the rows were worked out with.</summary>
        private bool ReadTileMargins(UIElementCollection children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var c = children[i];
                if (VisualTreeHelper.GetChildrenCount(c) == 0) continue;
                var inner = VisualTreeHelper.GetChild(c, 0) as FrameworkElement;
                if (inner == null) continue;
                var m = inner.Margin;
                double mx = Math.Max(0, m.Left + m.Right), my = Math.Max(0, m.Top + m.Bottom);
                if (Math.Abs(mx - marginX) < 0.5 && Math.Abs(my - marginY) < 0.5) return false;
                marginX = mx;
                marginY = my;
                return true;
            }
            return false;
        }

        private static int SelectedIndex(ItemsControl owner)
        {
            var s = owner as Selector;
            return s != null ? s.SelectedIndex : -1;
        }

        /// <summary>
        /// What Playnite's list does through its own panel (ListBoxEx listens to FullscreenTilePanel only): once the tiles are
        /// made, the highlight goes onto the selected game's tile if the list itself has it. Also after a new set of games took
        /// the highlighted tile away while the highlight was in the list.
        /// </summary>
        private void AfterGenerating(ItemsControl owner, bool made)
        {
            bool restore = hadFocusAtReset;
            hadFocusAtReset = false;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (made) { var h = ContainersGenerated; if (h != null) h(this, EventArgs.Empty); }
                    var list = owner as Selector;
                    if (list == null || !ReferenceEquals(Owner, owner)) return;
                    var focused = Keyboard.FocusedElement;
                    // (the highlight was on a tile that's gone: it's nowhere, on the window, or still on that tile, off the screen)
                    var fv = focused as Visual;
                    bool dropped = restore && list.IsVisible && (focused == null || focused is Window || (fv != null && PresentationSource.FromVisual(fv) == null));
                    if (!list.IsFocused && !dropped) return;
                    if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0;
                    if (list.SelectedItem == null) return;
                    var c = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as FrameworkElement;
                    if (c != null && !c.IsFocused) { c.Focus(); ScrollToIndex(list.SelectedIndex); }
                }
                catch (Exception ex) { logger.Warn("Kippix: mosaic: highlight after layout: " + ex.Message); }
            }), DispatcherPriority.Loaded);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            long t0 = Perf ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try
            {
                var owner = Owner;
                if (owner == null) return finalSize;
                var icg = owner.ItemContainerGenerator;
                var children = InternalChildren;
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    int index = icg.IndexFromContainer(child);
                    // (the rows as last worked out, even if they're about to be worked out again: nothing jumps in between)
                    if (index < 0 || index >= modelCount || itemRow[index] >= rows.Count) { child.Arrange(new Rect(0, -10000, child.DesiredSize.Width, child.DesiredSize.Height)); continue; }
                    var r = rows[itemRow[index]];
                    child.Arrange(new Rect(itemX[index], r.Top - offset, itemW[index], r.Height));
                }
            }
            catch (Exception ex) { if (failures++ < 5) logger.Error(ex, "Kippix: mosaic: arrange failed"); }
            if (Perf) { PerfArranges++; PerfArrangeMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            // (the generator has already let go of the tiles of games that left; they come off the panel at the next layout,
            // which WPF asks for after this. Reset: WPF has cleared the panel itself)
            modelValid = false;
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    resetPending = true;
                    var owner = Owner;
                    if (owner != null && owner.IsKeyboardFocusWithin) hadFocusAtReset = true;
                    break;
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                    keepSelectedInView = true;
                    break;
            }
            InvalidateMeasure();
        }

        /// <summary>The list asks for an item that has no tile yet (ScrollIntoView): scrolled to, the next layout makes it.</summary>
        protected override void BringIndexIntoView(int index)
        {
            ScrollToIndex(index);
        }

        // =====================================================================
        // Scrolling (IScrollInfo)
        // =====================================================================

        private Size viewport = new Size(0, 0), extent = new Size(0, 0);
        private double offset;
        private readonly TranslateTransform trans = new TranslateTransform();

        public bool CanVerticallyScroll { get; set; }
        public bool CanHorizontallyScroll { get; set; }
        public double ExtentWidth { get { return extent.Width; } }
        public double ExtentHeight { get { return extent.Height; } }
        public double ViewportWidth { get { return viewport.Width; } }
        public double ViewportHeight { get { return viewport.Height; } }
        public double HorizontalOffset { get { return 0; } }
        public double VerticalOffset { get { return offset; } }
        public ScrollViewer ScrollOwner { get; set; }

        private void InvalidateScroll()
        {
            var so = ScrollOwner;
            if (so != null) so.InvalidateScrollInfo();
        }

        private double MaxOffset { get { return Math.Max(0, extent.Height - viewport.Height); } }

        private void ClampOffset()
        {
            double v = Math.Max(0, Math.Min(MaxOffset, offset));
            if (Math.Abs(v - offset) < 0.01) return;
            offset = v;
            InvalidateScroll();
        }

        /// <summary>The row at a height in the panel (the last one below that, the first above).</summary>
        private int RowAt(double y)
        {
            int lo = 0, hi = rows.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (rows[mid].Top <= y) lo = mid; else hi = mid - 1;
            }
            return Math.Max(0, lo);
        }

        /// <summary>The scroll position that has the item's whole row on screen, moved as little as possible from <paramref name="from"/>.</summary>
        private double OffsetShowing(int index, double from)
        {
            if (!modelValid || index < 0 || index >= modelCount) return from;
            var r = rows[itemRow[index]];
            double bottom = r.Top + r.Height + (itemRow[index] == rows.Count - 1 ? BottomRoom : 0);
            if (r.Top < from || r.Height >= viewport.Height) return r.Top;
            if (bottom > from + viewport.Height) return bottom - viewport.Height;
            return from;
        }

        /// <summary>(The name Playnite's own panel has for it: the helper's "make this item's tile" looks for it.)</summary>
        public void ScrollToItem(object item)
        {
            var owner = Owner;
            if (owner != null) ScrollToIndex(owner.Items.IndexOf(item));
        }

        /// <summary>Scrolls just far enough for the item's row to be on screen whole.</summary>
        public void ScrollToIndex(int index)
        {
            if (!modelValid) { keepSelectedInView = true; InvalidateMeasure(); return; }
            SetVerticalOffset(OffsetShowing(index, offset));
        }

        public void SetHorizontalOffset(double newOffset) { }

        public void SetVerticalOffset(double newOffset)
        {
            if (double.IsNaN(newOffset)) return;
            double v = Math.Max(0, Math.Min(MaxOffset, newOffset));
            double moved = v - offset;
            if (Math.Abs(moved) < 0.01) return;
            offset = v;
            InvalidateScroll();
            InvalidateMeasure();
            // the tiles are put at their new places at once; the panel is drawn where it was and slides the rest of the way
            if (SmoothScroll && Math.Abs(moved) <= viewport.Height)
            {
                var slide = new DoubleAnimation(trans.Y + moved, 0, new Duration(TimeSpan.FromMilliseconds(140))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                trans.BeginAnimation(TranslateTransform.YProperty, slide);
            }
            else trans.BeginAnimation(TranslateTransform.YProperty, null);
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            try
            {
                var owner = Owner;
                if (owner == null || visual == null) return rectangle;
                // the tile the visual is in (or is)
                DependencyObject d = visual;
                while (d != null && !ReferenceEquals(VisualTreeHelper.GetParent(d), this)) d = VisualTreeHelper.GetParent(d);
                int index = d != null ? owner.ItemContainerGenerator.IndexFromContainer(d) : -1;
                if (index >= 0) ScrollToIndex(index);
            }
            catch (Exception) { }
            return rectangle;
        }

        private void ScrollByRow(int dir)
        {
            if (rows.Count == 0) return;
            int r = RowAt(offset + 0.5);
            if (dir > 0) SetVerticalOffset(r + 1 < rows.Count ? rows[r + 1].Top : MaxOffset);
            else SetVerticalOffset(rows[r].Top < offset - 0.5 ? rows[r].Top : (r > 0 ? rows[r - 1].Top : 0));
        }

        public void LineUp() { ScrollByRow(-1); }
        public void LineDown() { ScrollByRow(1); }
        public void LineLeft() { }
        public void LineRight() { }
        public void MouseWheelUp() { ScrollByRow(-1); }
        public void MouseWheelDown() { ScrollByRow(1); }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void PageUp() { SetVerticalOffset(offset - viewport.Height); }
        public void PageDown() { SetVerticalOffset(offset + viewport.Height); }
        public void PageLeft() { }
        public void PageRight() { }
    }
}
