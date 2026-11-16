using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.70) Settings › Game page › Animation: how the game page comes in. Off (straight away, nothing moves) or one of ten
    /// styles. Every style: the page fades up as one and the background settles in (its slow zoom); the logo, the info and the
    /// game's art (and then the description) each come in their style's way, a beat apart. Played once the page is drawn
    /// (ThemeApi.DetFade.cs DetEntranceStart). Picking one plays it on the game page behind the settings.
    /// </summary>
    public partial class ThemeApi
    {
        /// <summary>0 off; 1 rise (as it comes); 2 fade; 3 from the sides; 4 drop in; 5 zoom in; 6 settle; 7 cinematic; 8 cascade; 9 pop; 10 focus.</summary>
        private int DetAnimStyle { get { return Math.Max(0, Math.Min(10, settings.DetEntranceStyle)); } }

        private class DetAnim
        {
            public double X, Y;          // where the parts start (X: toward their own side of the screen, for "from the sides")
            public double Scale = 1;     // the page's content starts this size
            public double Blur;          // ... and this blurred
            public int Begin = 70, Stagger = 70, FadeMs = 560, MoveMs = 820;
            public Func<IEasingFunction> Ease = () => new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        }

        private static DetAnim DetAnimOf(int style)
        {
            switch (style)
            {
                case 2: return new DetAnim { FadeMs = 620 };
                case 3: return new DetAnim { X = 120, Stagger = 60, FadeMs = 480, MoveMs = 900, Ease = () => new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 } };
                case 4: return new DetAnim { Y = -26, MoveMs = 800 };
                case 5: return new DetAnim { Scale = 0.94, Stagger = 50, MoveMs = 950 };
                case 6: return new DetAnim { Scale = 1.07, Stagger = 50, MoveMs = 1000 };
                case 7: return new DetAnim { Y = 10, Begin = 380, Stagger = 110, FadeMs = 950, MoveMs = 1300, Ease = () => new SineEase { EasingMode = EasingMode.EaseOut } };
                case 8: return new DetAnim { Y = 30, Begin = 60, Stagger = 160, FadeMs = 520, MoveMs = 900 };
                case 9: return new DetAnim { Y = 28, Stagger = 60, FadeMs = 340, MoveMs = 620, Ease = () => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } };
                case 10: return new DetAnim { Blur = 26, Stagger = 50, FadeMs = 640, MoveMs = 760 };
                default: return new DetAnim { Y = 18 };   // 1: rise
            }
        }

        private static readonly string[] DetAnimParts = { "DetLogo", "InfoColumn", "CoverColumn" };
        private static readonly string[] DetAnimShifts = { "LogoShift", "InfoShift", "CoverShift" };

        /// <summary>-1 for a part left of the screen's middle, 1 for one right of it (for "from the sides").</summary>
        private static int DetSide(FrameworkElement details, UIElement fe)
        {
            try
            {
                var f = fe as FrameworkElement;
                if (f == null || details == null || f.ActualWidth < 1) return -1;
                var r = f.TransformToAncestor(details).TransformBounds(new Rect(0, 0, f.ActualWidth, f.ActualHeight));
                return r.Left + r.Width / 2 > details.ActualWidth / 2 ? 1 : -1;
            }
            catch (Exception) { return -1; }
        }

        private FrameworkElement DetViewHost(FrameworkElement details)
        {
            return details != null ? UiTools.FindByName(details, "PART_ViewHost") : null;
        }

        /// <summary>The page's parts where the entrance starts (atEnd: where it ends: Off, Reduce motion).</summary>
        private void DetEntranceHold(FrameworkElement details, bool atEnd)
        {
            var owner = FindTemplated(details, "LogoShift");
            var zero = new Duration(TimeSpan.Zero);
            var st = DetAnimOf(DetAnimStyle);
            foreach (string n in new[] { "DetLogo", "InfoColumn", "CoverColumn", "PART_ScrollHtmlDescription" })
            {
                var fe = DetPart<UIElement>(owner, details, n);
                if (fe != null) fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(atEnd ? 1 : 0, atEnd ? 1 : 0, zero));
            }
            for (int i = 0; i < 3; i++)
            {
                var tt = DetShift(owner, details, DetAnimShifts[i], DetAnimParts[i]);
                if (tt == null) continue;
                var fe = DetPart<UIElement>(owner, details, DetAnimParts[i]);
                double x = atEnd ? 0 : st.X * DetSide(details, fe), y = atEnd ? 0 : st.Y;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, x, zero));
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, y, zero));
            }
            var host = DetViewHost(details);
            if (host != null)
            {
                var sc = DetHostScale(host);
                double v = atEnd ? 1 : st.Scale;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(v, v, zero));
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(v, v, zero));
                if (!atEnd && st.Blur > 0) host.Effect = new BlurEffect { Radius = st.Blur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
                else if (host.Effect is BlurEffect) host.Effect = null;
            }
            var bg = DetPart<ScaleTransform>(owner, details, "BgScale");
            if (bg != null)
            {
                double v = atEnd && settings.DetBgZoom ? 1.06 : 1;   // (1.0.2: Background zoom off: it stays as it is)
                bg.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(v, v, zero));
                bg.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(v, v, zero));
            }
        }

        private static void DetBgScaleNow(ScaleTransform bg, double v)
        {
            bg.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            bg.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            bg.ScaleX = v;
            bg.ScaleY = v;
        }

        /// <summary>The scale on the page's content (its own: nothing else turns or sizes it).</summary>
        private static ScaleTransform DetHostScale(FrameworkElement host)
        {
            var sc = host.RenderTransform as ScaleTransform;
            if (sc == null || sc.IsFrozen)
            {
                sc = new ScaleTransform(1, 1);
                host.RenderTransformOrigin = new Point(0.5, 0.5);
                host.RenderTransform = sc;
            }
            return sc;
        }

        /// <summary>The entrance in the chosen style.</summary>
        private void DetEntrancePlay(FrameworkElement details)
        {
            var st = DetAnimOf(DetAnimStyle);
            DetEntranceCache(details);
            var page = new DoubleAnimation(details.Opacity < 0.5 ? details.Opacity : 0.003, 1, new Duration(TimeSpan.FromMilliseconds(420)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            page.Completed += (s, e) => { if (detailsOpen) DetailsFadeDone(details); };
            details.BeginAnimation(UIElement.OpacityProperty, page);
            var owner = FindTemplated(details, "LogoShift");
            var bg = DetPart<ScaleTransform>(owner, details, "BgScale");
            if (bg != null && settings.DetBgZoom)
            {
                bg.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.06, new Duration(TimeSpan.FromMilliseconds(1400))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                bg.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.06, new Duration(TimeSpan.FromMilliseconds(1400))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else if (bg != null) DetBgScaleNow(bg, 1);
            for (int i = 0; i < 3; i++)
            {
                var begin = TimeSpan.FromMilliseconds(st.Begin + st.Stagger * i);
                var fe = DetPart<UIElement>(owner, details, DetAnimParts[i]);
                if (fe != null)
                    fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(st.FadeMs)))
                    { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                var tt = DetShift(owner, details, DetAnimShifts[i], DetAnimParts[i]);
                if (tt == null) continue;
                double x0 = st.X * DetSide(details, fe);
                if (Math.Abs(x0) > 0.01) tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x0, 0, new Duration(TimeSpan.FromMilliseconds(st.MoveMs))) { BeginTime = begin, EasingFunction = st.Ease() });
                else tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 0, new Duration(TimeSpan.Zero)));
                if (Math.Abs(st.Y) > 0.01) tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(st.Y, 0, new Duration(TimeSpan.FromMilliseconds(st.MoveMs))) { BeginTime = begin, EasingFunction = st.Ease() });
                else tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 0, new Duration(TimeSpan.Zero)));
            }
            var host = DetViewHost(details);
            if (host != null)
            {
                var sc = DetHostScale(host);
                if (Math.Abs(st.Scale - 1) > 0.001)
                {
                    sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(st.Scale, 1, new Duration(TimeSpan.FromMilliseconds(st.MoveMs))) { EasingFunction = st.Ease() });
                    sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(st.Scale, 1, new Duration(TimeSpan.FromMilliseconds(st.MoveMs))) { EasingFunction = st.Ease() });
                }
                var blur = host.Effect as BlurEffect;
                if (blur != null && st.Blur > 0)
                {
                    var b = new DoubleAnimation(st.Blur, 0, new Duration(TimeSpan.FromMilliseconds(st.MoveMs))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    b.Completed += (s, e) => { if (ReferenceEquals(host.Effect, blur)) host.Effect = null; };   // (no blur left on the page)
                    blur.BeginAnimation(BlurEffect.RadiusProperty, b);
                }
                else if (blur != null) host.Effect = null;
            }
            var desc = DetPart<UIElement>(owner, details, "PART_ScrollHtmlDescription");
            if (desc != null)
                desc.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(st.FadeMs)))
                { BeginTime = TimeSpan.FromMilliseconds(st.Begin + st.Stagger * 3 + 20), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ---------------- the settings page ----------------

        private static readonly string[] DetAnimKeys = { "LOC_KX_PgAnimOff", "LOC_KX_PgAnimRise", "LOC_KX_PgAnimFade", "LOC_KX_PgAnimSides", "LOC_KX_PgAnimDrop", "LOC_KX_PgAnimZoomIn",
                                                         "LOC_KX_PgAnimSettle", "LOC_KX_PgAnimCinematic", "LOC_KX_PgAnimCascade", "LOC_KX_PgAnimPop", "LOC_KX_PgAnimFocus" };
        private static readonly string[] DetAnimNames = { "Off", "Rise", "Fade", "From the sides", "Drop in", "Zoom in", "Settle", "Cinematic", "Cascade", "Pop", "Focus" };
        private static readonly string[] DetAnimSubs =
        {
            "The page shows straight away, nothing moves",
            "Each part fades in and rises a little into place (as Kippix comes)",
            "Each part fades in where it stands",
            "What's on the left slides in from the left, what's on the right from the right",
            "Each part fades in and drops gently into place",
            "The page grows into place as it fades in",
            "The page eases back into place from a little closer",
            "The background first, then the parts, slowly",
            "The parts one after another, rising further",
            "Each part springs up into place, a little past it and back",
            "The page comes into focus from a blur"
        };

        public string DetAnimText { get { int m = DetAnimStyle; return Loc(DetAnimKeys[m], DetAnimNames[m]); } }

        private void AddDetAnimRows(ObservableCollection<PickRow> rows)
        {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecPgAnimWhen", "WHEN THE GAME PAGE OPENS") });
            for (int i = 0; i <= 10; i++)
            {
                int n = i;
                rows.Add(new PickRow
                {
                    Key = "anim" + n, Icon = n == 0 ? "close" : "play", Text = Loc(DetAnimKeys[n], DetAnimNames[n]),
                    Sub = Loc(DetAnimKeys[n] + "Sub", DetAnimSubs[n]), IsSelected = DetAnimStyle == n,
                    Command = new RelayCommand(_ => PickDetAnim(n))
                });
            }
            if (settings.ReduceMotion)
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "animrm", Text = Loc("LOC_KX_PgAnimReduceMotion", "Reduce motion is on: the page shows straight away whichever is chosen") });
            // (1.0.2) the background's slow zoom, on its own
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecPgAnimBg", "BACKGROUND") });
            rows.Add(BoardSwitch("picture", "bgzoom", Loc("LOC_KX_PgBgZoom", "Background zoom"), settings.DetBgZoom,
                                 Loc("LOC_KX_PgBgZoomOn", "The game's picture slowly zooms in as the page opens"), Loc("LOC_KX_PgBgZoomOff", "Off: the picture stays as it is"),
                                 () =>
                                 {
                                     settings.DetBgZoom = !settings.DetBgZoom;
                                     SaveSettings();
                                     PlaySound("move");
                                     BoardRowsAgain(BuildLookExtraRows, "bgzoom");
                                     try
                                     {
                                         var win = Application.Current.MainWindow;
                                         var details = detailsOpen && win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                                         var bgs = details != null ? DetPart<ScaleTransform>(FindTemplated(details, "LogoShift"), details, "BgScale") : null;
                                         if (bgs != null) DetBgScaleNow(bgs, settings.DetBgZoom ? 1.06 : 1);
                                     }
                                     catch (Exception) { }
                                 }));
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecReset", "RESET") });
            rows.Add(new PickRow
            {
                Key = "animreset", Icon = "refresh", Text = Loc("LOC_KX_ResetPage", "Reset this page"), Sub = Loc("LOC_KX_ResetPageSub", "Resets this page to default"),
                Command = new RelayCommand(_ => { settings.DetBgZoom = new HelperSettings().DetBgZoom; PickDetAnim(new HelperSettings().DetEntranceStyle); })
            });
        }

        /// <summary>A style picked: kept, and played on the game page behind the settings (when it's open) to see it.</summary>
        private void PickDetAnim(int n)
        {
            settings.DetEntranceStyle = Math.Max(0, Math.Min(10, n));
            SaveSettings();
            PlaySound("select");
            Notify("DetAnimText");
            if (openPanel == "options" && IsOptionsLookExtra) BoardRowsAgain(BuildLookExtraRows, "anim" + settings.DetEntranceStyle);
            try
            {
                var win = Application.Current.MainWindow;
                var details = detailsOpen && win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                if (details == null || !details.IsVisible) return;
                bool off = settings.ReduceMotion || DetAnimStyle == 0;
                DetEntranceHold(details, off);
                if (off) return;
                details.BeginAnimation(UIElement.OpacityProperty, null);
                details.Opacity = 0.003;
                DetEntrancePlay(details);
            }
            catch (Exception) { }
        }
    }
}
