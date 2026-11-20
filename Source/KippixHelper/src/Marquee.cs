using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KippixHelper
{
    /// <summary>
    /// (0.90) Words that don't fit on a row (cut off with "…", or a note wrapped past its last line) slowly scroll while the row
    /// has the highlight or the mouse is over it, so they can be read; they go back as they were when it moves on.
    /// The text is moved for the time being into a small clipped strip in its place (a Canvas, so nothing squeezes it), and
    /// put back exactly where it was afterwards.
    /// </summary>
    internal static class Marquee
    {
        private const double Speed = 38;          // pixels a second: slow enough to read
        private const double StartPause = 0.9;    // seconds before it starts moving
        private const double EndPause = 1.6;      // ... and at the end, before it starts again

        private class Running
        {
            public TextBlock Text;
            public Panel Parent;
            public int Index;
            public Grid Host;
            public TextTrimming Trimming;
            public Thickness Margin;
            public double MaxHeight, Width;
        }

        private static bool installed;
        private static Func<bool> enabled;
        private static readonly Dictionary<UIElement, List<Running>> active = new Dictionary<UIElement, List<Running>>();

        /// <summary>Once: watches every button-like row getting / losing the highlight or the mouse.</summary>
        public static void Install(Func<bool> isOn)
        {
            enabled = isOn;
            if (installed) return;
            installed = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => { if (ReferenceEquals(s, e.OriginalSource)) Start(s as UIElement); }), true);
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => { if (ReferenceEquals(s, e.OriginalSource)) StopSoon(s as UIElement); }), true);
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseEnterEvent, new MouseEventHandler((s, e) => Start(s as UIElement)), true);
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.MouseLeaveEvent, new MouseEventHandler((s, e) => StopSoon(s as UIElement)), true);
        }

        private static void Start(UIElement owner)
        {
            try
            {
                if (owner == null || active.ContainsKey(owner) || enabled == null || !enabled()) return;
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null || Window.GetWindow(owner) != win) return;
                if (OnTheGames(owner)) return;   // (0.93) the menus and lists only: never the game cards or board tiles you move through
                var list = new List<Running>();
                foreach (var tb in Texts(owner))
                {
                    var r = Begin(tb);
                    if (r != null) list.Add(r);
                    if (list.Count >= 3) break;
                }
                if (list.Count > 0) active[owner] = list;
            }
            catch (Exception) { }
        }

        private static void StopSoon(UIElement owner)
        {
            if (owner == null) return;
            // still highlighted, or the mouse still over it: keep going
            if (owner.IsKeyboardFocused || owner.IsMouseOver) return;
            List<Running> list;
            if (!active.TryGetValue(owner, out list)) return;
            active.Remove(owner);
            foreach (var r in list) End(r);
        }

        private static readonly HashSet<string> GameAreas = new HashSet<string> { "PART_ListGameItems", "KX_Board", "KX_BoardHost", "KX_CarouselHost", "KX_Widgets" };

        private static bool OnTheGames(DependencyObject d)
        {
            for (int i = 0; d != null && i < 60; i++)
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.Name.Length > 0 && GameAreas.Contains(fe.Name)) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private static IEnumerable<TextBlock> Texts(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                var tb = c as TextBlock;
                if (tb != null) { if (tb.TextTrimming != TextTrimming.None && tb.IsVisible) yield return tb; continue; }
                foreach (var t in Texts(c)) yield return t;
            }
        }

        /// <summary>The text's strip, if it's cut off now (null when it fits).</summary>
        private static Running Begin(TextBlock tb)
        {
            var parent = VisualTreeHelper.GetParent(tb) as Panel;
            if (parent == null || parent.IsItemsHost) return null;
            double w = tb.ActualWidth, h = tb.ActualHeight;
            if (w < 20 || h < 4) return null;
            string text = tb.Text;
            if (string.IsNullOrEmpty(text)) return null;
            bool wraps = tb.TextWrapping != TextWrapping.NoWrap;
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
#pragma warning disable 618
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, tb.FlowDirection, typeface, tb.FontSize, Brushes.White);
#pragma warning restore 618
            double travel;
            if (wraps)
            {
                ft.MaxTextWidth = Math.Max(1, w);
                double full = double.IsNaN(tb.LineHeight) || tb.LineHeight <= 0 ? ft.Height : ft.Height;
                travel = full - h;
            }
            else travel = ft.WidthIncludingTrailingWhitespace - w;
            if (travel < 6) return null;

            var r = new Running { Text = tb, Parent = parent, Index = parent.Children.IndexOf(tb), Trimming = tb.TextTrimming, Margin = tb.Margin, MaxHeight = tb.MaxHeight, Width = tb.Width };
            if (r.Index < 0) return null;
            var host = new Grid { ClipToBounds = true, Height = h, Margin = tb.Margin, HorizontalAlignment = tb.HorizontalAlignment, VerticalAlignment = tb.VerticalAlignment, IsHitTestVisible = false };
            if (wraps || tb.HorizontalAlignment == HorizontalAlignment.Stretch) host.Width = w;
            else host.Width = w;
            Grid.SetColumn(host, Grid.GetColumn(tb));
            Grid.SetRow(host, Grid.GetRow(tb));
            Grid.SetColumnSpan(host, Grid.GetColumnSpan(tb));
            DockPanel.SetDock(host, DockPanel.GetDock(tb));
            var canvas = new Canvas();
            host.Children.Add(canvas);
            parent.Children.RemoveAt(r.Index);
            tb.TextTrimming = TextTrimming.None;
            tb.Margin = new Thickness(0);
            tb.MaxHeight = double.PositiveInfinity;
            if (wraps) tb.Width = w;
            canvas.Children.Add(tb);
            parent.Children.Insert(r.Index, host);
            r.Host = host;

            double secs = travel / Speed;
            var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(anim, 60);   // (under a pixel a frame at this speed; a 120 Hz screen's own rate only doubles the drawing)
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(StartPause))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(-travel, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(StartPause + secs))));
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(-travel, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(StartPause + secs + EndPause))));
            tb.BeginAnimation(wraps ? Canvas.TopProperty : Canvas.LeftProperty, anim);
            return r;
        }

        private static void End(Running r)
        {
            try
            {
                var tb = r.Text;
                tb.BeginAnimation(Canvas.LeftProperty, null);
                tb.BeginAnimation(Canvas.TopProperty, null);
                var canvas = VisualTreeHelper.GetParent(tb) as Canvas;
                if (canvas != null) canvas.Children.Remove(tb);
                int at = r.Parent.Children.IndexOf(r.Host);
                if (at >= 0) r.Parent.Children.RemoveAt(at); else at = Math.Min(r.Index, r.Parent.Children.Count);
                tb.TextTrimming = r.Trimming;
                tb.Margin = r.Margin;
                tb.MaxHeight = r.MaxHeight;
                tb.Width = r.Width;
                r.Parent.Children.Insert(Math.Min(at, r.Parent.Children.Count), tb);
            }
            catch (Exception) { }
        }
    }
}
