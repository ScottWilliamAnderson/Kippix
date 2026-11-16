using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
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
    /// The Mosaic layout's panel: a justified gallery. Every tile keeps its own shape (AspectOf: width / height) as nearly as
    /// its row allows: all rows are one height (RowHeight), so a whole number of them fits the list and it scrolls row by
    /// row, and each row is packed edge to edge by making its tiles a little wider or narrower than their pictures (the
    /// picture fills its tile, so a few percent of it is cropped). The last row keeps its shapes and starts at the left.
    ///
    /// It stands in for Playnite's FullscreenTilePanel as the games list's items panel (ThemeApi.Justified.cs swaps it in), so
    /// like that one it makes only the tiles on screen (plus two rows either side, made a few at a time while nothing else
    /// is going on, plus the selected and the highlighted tile, wherever they are) and does the scrolling itself (IScrollInfo,
    /// downwards only). Rows outside the ones in view are kept unseen: they show only while they slide in or out.
    /// </summary>
    public class JustifiedPanel : VirtualizingPanel, IScrollInfo
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const double MinAspect = 0.45, MaxAspect = 2.4, DefaultAspect = 2.0 / 3.0;
        private const double MaxStretch = 1.3, MinSqueeze = 0.8;   // how far a row's tiles go from their pictures' shapes to fill the width
        private const int BufferRows = 2;           // rows kept made above and below the ones in view
        private const int AheadPerPass = 4;         // tiles made ahead in one idle pass

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
        }

        private readonly List<Row> rows = new List<Row>();
        private double[] itemX = new double[0], itemW = new double[0];
        private int[] itemRow = new int[0];
        private int modelCount;
        private double modelWidth = -1;
        private bool modelValid;
        private double marginX, marginY;            // the tile's own margins inside its container (left + right, top + bottom)
        private bool resetPending, hadFocusAtReset, keepAnchorInView;
        private int failures;
        private int highlight = -1;
        private readonly HashSet<UIElement> pooled = new HashSet<UIElement>();   // tiles given back to the generator: kept on the panel, out of sight, until they're used again
        private readonly HashSet<UIElement> unseen = new HashSet<UIElement>();   // tiles of rows outside the ones in view
        private readonly HashSet<UIElement> fresh = new HashSet<UIElement>();    // tiles made (or used again) in this layout pass
        private bool aheadQueued, aheadNow, fadeRows, marginsQueued;

        private Func<object, double> aspectOf;
        private double rowHeight = 240, edgePadding, bottomRoom;

        /// <summary>An item's width / height (clamped to 0.45 - 2.4 here).</summary>
        public Func<object, double> AspectOf { get { return aspectOf; } set { aspectOf = value; InvalidateModel(); } }
        /// <summary>The height of every row (the tile's own margins included).</summary>
        public double RowHeight { get { return rowHeight; } set { if (Math.Abs(rowHeight - value) < 0.5) return; rowHeight = value; InvalidateModel(); } }
        /// <summary>Room kept free at the left and right edges (the highlighted tile grows a little).</summary>
        public double EdgePadding { get { return edgePadding; } set { if (Math.Abs(edgePadding - value) < 0.5) return; edgePadding = value; InvalidateModel(); } }
        /// <summary>Room under the last row in view, inside the list (the highlight's edge and glow).</summary>
        public double BottomRoom { get { return bottomRoom; } set { if (Math.Abs(bottomRoom - value) < 0.5) return; bottomRoom = value; InvalidateModel(); } }
        /// <summary>Scrolling slides (off: it jumps).</summary>
        public bool SmoothScroll { get; set; }
        /// <summary>The item the highlight is on, when that isn't the selected one yet (-1: the selected one). Its row is the one kept in view.</summary>
        public int HighlightIndex { get { return highlight; } set { highlight = value; } }
        /// <summary>After a layout pass that made new tiles.</summary>
        public event EventHandler ContainersGenerated;
        /// <summary>The tiles' own margins are known (or changed): whoever sized the list from them does it again.</summary>
        public event EventHandler MarginsChanged;

        public int RowCount { get { return rows.Count; } }
        public int ItemCount { get { return modelCount; } }
        public double TileMarginX { get { return marginX; } }
        public double TileMarginY { get { return marginY; } }
        /// <summary>How many whole rows the list shows.</summary>
        public int VisibleRows { get { return Math.Max(1, (int)Math.Floor((viewport.Height - bottomRoom + 1) / RowH)); } }
        /// <summary>The first row in view.</summary>
        public int TopRow { get { return (int)Math.Round(offset / RowH); } }

        private double RowH { get { return Math.Max(marginY + 20, Math.Round(rowHeight)); } }

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

        /// <summary>The shapes (or the row height) changed: the rows are worked out again at the next layout.</summary>
        public void InvalidateModel()
        {
            modelValid = false;
            keepAnchorInView = true;
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
        /// One pass over the items. A row takes items while they fit in their own shapes; the one that doesn't fit either starts
        /// the next row (this row's tiles are made a little wider to fill the width) or is squeezed in (a little narrower),
        /// whichever changes the shapes less. A tile's slot is its picture (aspect x the row's inner height, times the row's
        /// stretch) plus the tile's own margins. A row that would have to stretch too far (a few wide tiles and then one that
        /// doesn't fit) and the last row keep their shapes and start at the left.
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
            double inner = RowH - marginY;                    // the picture's height
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = Aspect(owner.Items[i]);

            int first = 0;
            while (first < n)
            {
                double sumA = 0;
                int count = 0;
                double k = 1;
                bool fill = false;
                for (int i = first; i < n; i++)
                {
                    double natural = (sumA + a[i]) * inner + (count + 1) * marginX;
                    if (natural <= w) { sumA += a[i]; count++; continue; }
                    // this one doesn't fit
                    fill = true;
                    double with = (w - (count + 1) * marginX) / ((sumA + a[i]) * inner);
                    if (count == 0) { sumA = a[i]; count = 1; k = with; break; }    // (wider than the whole row by itself)
                    double without = (w - count * marginX) / (sumA * inner);
                    if (with >= MinSqueeze && (1 / with < without || without > MaxStretch)) { sumA += a[i]; count++; k = with; }
                    else k = without;
                    break;
                }
                if (!fill)
                {
                    // the last row: as it comes, from the left (all but full: filled like the others)
                    k = count > 0 ? (w - count * marginX) / (sumA * inner) : 1;
                    if (k <= 1.08) fill = true; else k = 1;
                }
                else if (k > MaxStretch) { k = 1; fill = false; }

                // across: each slot, the ends rounded so a filled row ends exactly at the edge
                double x = edgePadding, run = 0;
                for (int i = first; i < first + count; i++)
                {
                    double slot = a[i] * inner * k + marginX;
                    double next = run + slot;
                    double left = Math.Round(run), right = Math.Round(next);
                    if (fill && i == first + count - 1) right = Math.Round(w);
                    itemX[i] = x + left;
                    itemW[i] = Math.Max(1, right - left);
                    itemRow[i] = rows.Count;
                    run = next;
                }
                rows.Add(new Row { First = first, Count = count });
                first += count;
            }
            var newExtent = new Size(width, n == 0 ? 0 : rows.Count * RowH + bottomRoom);
            if (newExtent != extent) { extent = newExtent; InvalidateScroll(); }
            logger.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Kippix: mosaic: row model: {0} items in {1} rows of {2:0}, width {3:0}, {4:0} tall, tile margins {5:0} x {6:0}",
                n, rows.Count, RowH, width, extent.Height, marginX, marginY));
        }

        /// <summary>Where an item is, in the panel's own space (not moved by the scrolling). Empty when there's no such item.</summary>
        public Rect RectOf(int index)
        {
            if (!modelValid || index < 0 || index >= modelCount) return Rect.Empty;
            return new Rect(itemX[index], itemRow[index] * RowH, itemW[index], RowH);
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
            long t0 = Perf ? Stopwatch.GetTimestamp() : 0;
            try { MeasureRows(size); }
            catch (Exception ex)
            {
                // (an error here would take Playnite down: logged a few times and the pass given up instead)
                if (failures++ < 5) logger.Error(ex, "Kippix: mosaic: layout failed");
            }
            if (Perf)
            {
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
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
            if (size != viewport) { viewport = size; keepAnchorInView = true; InvalidateScroll(); }
            var icg = owner.ItemContainerGenerator;
            if (resetPending) DropPooled(children);             // (a new set of games: the generator has forgotten the tiles it had been given back)
            DropDeadChildren(children, icg);
            ReadTileMargins(children);

            int n = owner.Items.Count;
            bool rebuilt = false;
            if (!modelValid || n != modelCount || Math.Abs(size.Width - modelWidth) > 0.5) { BuildModel(owner, size.Width); rebuilt = true; }

            int selected = SelectedIndex(owner);
            if (highlight >= n) highlight = -1;
            int anchor = highlight >= 0 ? highlight : selected;
            if (resetPending)
            {
                // a new set of games (a filter, a refresh): from the top, unless a game is selected: then that one stays in view
                resetPending = false;
                if (anchor < 0) offset = 0;
                keepAnchorInView = true;
            }
            if ((rebuilt || keepAnchorInView) && anchor >= 0) offset = OffsetShowing(anchor, offset);
            keepAnchorInView = false;
            SnapOffset();

            bool made = false;
            int keepFirst = -1, keepLast = -1;
            if (n > 0 && rows.Count > 0)
            {
                // the rows in view: made now
                int r0 = Math.Min(rows.Count - 1, TopRow), r1 = Math.Min(rows.Count - 1, r0 + VisibleRows - 1);
                made |= Generate(gen, children, rows[r0].First, rows[r1].First + rows[r1].Count - 1);
                // the selected game's tile and the highlighted one are always there (the highlight goes to them, wherever the list is scrolled to)
                if (selected >= 0 && selected < n && icg.ContainerFromIndex(selected) == null) made |= Generate(gen, children, selected, selected);
                if (highlight >= 0 && highlight < n && icg.ContainerFromIndex(highlight) == null) made |= Generate(gen, children, highlight, highlight);
                // two rows either side: kept, and made a few tiles at a time when there's nothing else to do (the nearest first),
                // so a row is already there when it scrolls in
                int b0 = Math.Max(0, r0 - BufferRows), b1 = Math.Min(rows.Count - 1, r1 + BufferRows);
                keepFirst = rows[b0].First;
                keepLast = rows[b1].First + rows[b1].Count - 1;
                if (aheadNow)
                {
                    int budget = AheadPerPass;
                    for (int d = 1; d <= BufferRows && budget > 0; d++)
                    {
                        if (r1 + d <= b1) made |= MakeMissing(gen, icg, children, rows[r1 + d], ref budget);
                        if (r0 - d >= b0 && budget > 0) made |= MakeMissing(gen, icg, children, rows[r0 - d], ref budget);
                    }
                }
                aheadNow = false;
                bool missing = false;
                for (int i = keepFirst; i <= keepLast && !missing; i++) missing = icg.ContainerFromIndex(i) == null;
                if (missing) QueueAhead();
            }

            // tiles no longer near the screen go back to the generator: never the selected one, never the one with the highlight.
            // They stay on the panel, out of sight: putting a tile on it again costs more than everything else about using it again
            var recycling = gen as IRecyclingItemContainerGenerator;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                if (pooled.Contains(child)) continue;
                int index = icg.IndexFromContainer(child);
                if (index < 0) { Forget(child); RemoveInternalChildRange(i, 1); continue; }
                if ((index >= keepFirst && index <= keepLast) || index == selected || index == highlight || child.IsKeyboardFocusWithin) continue;
                var pos = gen.GeneratorPositionFromIndex(index);
                if (pos.Offset != 0) { Forget(child); RemoveInternalChildRange(i, 1); continue; }
                if (recycling != null) { recycling.Recycle(pos, 1); pooled.Add(child); }
                else { gen.Remove(pos, 1); Forget(child); RemoveInternalChildRange(i, 1); }
            }

            // (new rows: the tiles already there take their new sizes)
            if (rebuilt)
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    if (pooled.Contains(child)) continue;
                    int index = icg.IndexFromContainer(child);
                    if (index >= 0 && index < modelCount) child.Measure(new Size(itemW[index], RowH));
                }

            // (the first tiles ever made tell us their margins: if that changes the rows, once more before anything is drawn)
            if (ReadTileMargins(children)) { modelValid = false; keepAnchorInView = true; InvalidateMeasure(); }

            if (made || hadFocusAtReset) AfterGenerating(owner, made);
        }

        private void QueueAhead()
        {
            if (aheadQueued) return;
            aheadQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                aheadQueued = false;
                aheadNow = true;
                InvalidateMeasure();
            }), DispatcherPriority.Background);
        }

        /// <summary>Makes the tiles of a row that aren't there yet, <paramref name="budget"/> of them at most.</summary>
        private bool MakeMissing(IItemContainerGenerator gen, ItemContainerGenerator icg, UIElementCollection children, Row row, ref int budget)
        {
            bool made = false;
            int to = row.First + row.Count - 1;
            int i = row.First;
            while (i <= to && budget > 0)
            {
                if (icg.ContainerFromIndex(i) != null) { i++; continue; }
                int j = i;
                while (j < to && j - i + 1 < budget && icg.ContainerFromIndex(j + 1) == null) j++;
                made |= Generate(gen, children, i, j);
                budget -= j - i + 1;
                i = j + 1;
            }
            return made;
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
                    bool again = pooled.Remove(child);          // one given back earlier: still on the panel
                    if (isNew || again || !children.Contains(child))
                    {
                        if (!again) AddInternalChild(child);
                        gen.PrepareItemContainer(child);
                        fresh.Add(child);
                        made = true;
                        if (Perf) PerfMade++;
                    }
                    child.Measure(new Size(itemW[i], RowH));
                }
            }
            return made;
        }

        private void Forget(UIElement child)
        {
            unseen.Remove(child);
            fresh.Remove(child);
        }

        /// <summary>Tiles the generator has let go of (their game left the list) are taken off the panel.</summary>
        private void DropDeadChildren(UIElementCollection children, ItemContainerGenerator icg)
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                if (pooled.Contains(child) || icg.IndexFromContainer(child) >= 0) continue;
                Forget(child);
                RemoveInternalChildRange(i, 1);
            }
        }

        private void DropPooled(UIElementCollection children)
        {
            if (pooled.Count == 0) return;
            for (int i = children.Count - 1; i >= 0; i--)
                if (pooled.Contains(children[i])) { Forget(children[i]); RemoveInternalChildRange(i, 1); }
            pooled.Clear();
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
                if (!marginsQueued)
                {
                    marginsQueued = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        marginsQueued = false;
                        var h = MarginsChanged;
                        if (h != null) try { h(this, EventArgs.Empty); } catch (Exception ex) { logger.Warn("Kippix: mosaic: tile margins: " + ex.Message); }
                    }), DispatcherPriority.Loaded);
                }
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
                    if (c != null && !c.IsFocused) { ScrollToIndex(list.SelectedIndex); c.Focus(); }
                }
                catch (Exception ex) { logger.Warn("Kippix: mosaic: highlight after layout: " + ex.Message); }
            }), DispatcherPriority.Loaded);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            long t0 = Perf ? Stopwatch.GetTimestamp() : 0;
            try
            {
                var owner = Owner;
                if (owner == null) return finalSize;
                var icg = owner.ItemContainerGenerator;
                var children = InternalChildren;
                double h = RowH;
                int top = TopRow, bottom = top + VisibleRows - 1;
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    int index = pooled.Contains(child) ? -1 : icg.IndexFromContainer(child);
                    // (the rows as last worked out, even if they're about to be worked out again: nothing jumps in between)
                    if (index < 0 || index >= modelCount || itemRow[index] >= rows.Count) { child.Arrange(new Rect(0, -10000, child.DesiredSize.Width, child.DesiredSize.Height)); continue; }
                    int r = itemRow[index];
                    child.Arrange(new Rect(itemX[index], r * h - offset, itemW[index], h));

                    // a row above or below the ones in view isn't seen (it would show in the room kept for the highlight to grow
                    // into): it fades out as it slides away and in as it comes
                    bool hide = r < top || r > bottom;
                    bool isFresh = fresh.Contains(child);
                    if (hide == unseen.Contains(child) && !isFresh) continue;
                    if (hide) unseen.Add(child); else unseen.Remove(child);
                    if (fadeRows && !isFresh) child.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, new Duration(TimeSpan.FromMilliseconds(hide ? 110 : 150))));
                    else { child.BeginAnimation(OpacityProperty, null); child.Opacity = hide ? 0 : 1; }
                }
                fresh.Clear();
                fadeRows = false;
            }
            catch (Exception ex) { if (failures++ < 5) logger.Error(ex, "Kippix: mosaic: arrange failed"); }
            if (Perf) { PerfArranges++; PerfArrangeMs += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; }
            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            // (the generator has already let go of the tiles of games that left; they come off the panel at the next layout,
            // which WPF asks for after this. Reset: WPF has cleared the panel itself)
            modelValid = false;
            highlight = -1;
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
                    keepAnchorInView = true;
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
        // Scrolling (IScrollInfo): always a whole number of rows
        // =====================================================================

        private Size viewport = new Size(0, 0), extent = new Size(0, 0);
        private double offset;
        private long lastScrollAt;
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

        /// <summary>The last row can be scrolled to the bottom of the list, no further.</summary>
        private double MaxOffset { get { return Math.Max(0, (rows.Count - VisibleRows) * RowH); } }

        private double Snapped(double v)
        {
            double h = RowH;
            return Math.Max(0, Math.Min(MaxOffset, Math.Round(v / h) * h));
        }

        private void SnapOffset()
        {
            double v = Snapped(offset);
            if (Math.Abs(v - offset) < 0.01) return;
            offset = v;
            InvalidateScroll();
        }

        /// <summary>The scroll position that has the item's row in view, moved as little as possible from <paramref name="from"/>.</summary>
        private double OffsetShowing(int index, double from)
        {
            if (!modelValid || index < 0 || index >= modelCount) return from;
            double h = RowH;
            int r = itemRow[index], top = (int)Math.Round(from / h), vis = VisibleRows;
            if (r < top) return r * h;
            if (r > top + vis - 1) return (r - vis + 1) * h;
            return top * h;
        }

        /// <summary>(The name Playnite's own panel has for it: the helper's "make this item's tile" looks for it.)</summary>
        public void ScrollToItem(object item)
        {
            var owner = Owner;
            if (owner != null) ScrollToIndex(owner.Items.IndexOf(item));
        }

        /// <summary>Scrolls just far enough for the item's row to be in view.</summary>
        public void ScrollToIndex(int index)
        {
            if (!modelValid) { keepAnchorInView = true; InvalidateMeasure(); return; }
            SetVerticalOffset(OffsetShowing(index, offset));
        }

        public void SetHorizontalOffset(double newOffset) { }

        public void SetVerticalOffset(double newOffset)
        {
            if (double.IsNaN(newOffset)) return;
            double v = Snapped(newOffset);
            double moved = v - offset;
            if (Math.Abs(moved) < 0.01) return;
            offset = v;
            InvalidateScroll();
            InvalidateMeasure();
            long now = Stopwatch.GetTimestamp();
            double since = lastScrollAt == 0 ? 10000 : (now - lastScrollAt) * 1000.0 / Stopwatch.Frequency;
            lastScrollAt = now;
            // the tiles are put at their new places at once; the panel is drawn where it was and slides the rest of the way.
            // One step eases to a stop; a run of them (a held button) moves at the pace the steps come, so it scrolls steadily.
            // (A jump further than the rows kept made has nothing to slide past: it's just there.)
            if (SmoothScroll && Math.Abs(moved) <= RowH * BufferRows + 1)
            {
                bool run = since < 260;
                var slide = new DoubleAnimation(trans.Y + moved, 0, new Duration(TimeSpan.FromMilliseconds(run ? Math.Max(90, Math.Min(180, since)) : 180)));
                if (!run) slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                trans.BeginAnimation(TranslateTransform.YProperty, slide);
                fadeRows = true;
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

        private void ScrollByRows(int count)
        {
            if (rows.Count > 0) SetVerticalOffset(offset + count * RowH);
        }

        public void LineUp() { ScrollByRows(-1); }
        public void LineDown() { ScrollByRows(1); }
        public void LineLeft() { }
        public void LineRight() { }
        public void MouseWheelUp() { ScrollByRows(-1); }
        public void MouseWheelDown() { ScrollByRows(1); }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void PageUp() { ScrollByRows(-VisibleRows); }
        public void PageDown() { ScrollByRows(VisibleRows); }
        public void PageLeft() { }
        public void PageRight() { }
    }
}
