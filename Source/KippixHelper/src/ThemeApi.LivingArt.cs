using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Home settings › Background › ANIMATED ART › Animated covers (0.81: only this — the highlighted cover's slow zoom; no
    /// sheen, nothing fetched or saved, the background code below is no longer started). Was "Living picture" (r14).
    /// A game with no animated art (none in Steam's library art or on SteamGridDB) gets a "living picture" made from its own art,
    /// never its trailer:
    ///   - the highlighted card: its cover drifts and zooms very slowly (1.00 to 1.06 and back, about 14 s each way, the centre
    ///     wandering), with a soft sheen of light sweeping across it every 8 s;
    ///   - the background (when "Animate the background too" is on and the background is the game's art): the art drifts and zooms
    ///     slowly (unless Background zoom already moves it) and a few soft motes of light float up through it.
    /// All of it is WPF animations on transforms and opacity (30 fps, no per-frame code), paused while the home screen is covered,
    /// gone with Reduce motion or while saving battery. "Still picture" keeps the cover and the art as they are.
    /// </summary>
    public partial class ThemeApi
    {
        private const double LivingRestSeconds = 1.0;   // the highlight rests this long before the card comes alive

        /// <summary>Reduce motion or battery saving: nothing living (the setting is kept).</summary>
        private bool LivingBlocked { get { return settings.ReduceMotion || batterySaving || LiveBatteryPause(); } }
        private bool LivingActive { get { return livingCard != null || livingBgScale != null || livingMotes != null; } }

        // ---------------- the highlighted card ----------------

        private Grid livingCard;
        private FrameworkElement livingCardTile;
        private Guid livingCardGame = Guid.Empty;
        private DependencyPropertyChangedEventHandler livingCardOnContext;
        private readonly List<AnimationClock> livingCardClocks = new List<AnimationClock>();
        private int livingCardGen;

        /// <summary>From AnimArtTick (home screen visible): the highlighted card's living picture, when it has no animated cover.</summary>
        private void LivingCardTick(FrameworkElement selTile, Game game, bool rested, bool cardAnimated)
        {
            bool want = selTile != null && game != null && !LivingBlocked && !cardAnimated
                        && !(previewPlaying && settings.PreviewMode == 1);   // (a trailer preview playing in the card has it)
            if (!want)
            {
                if (livingCard != null) LivingCardHide(true);
                return;
            }
            if (livingCard != null && ReferenceEquals(livingCardTile, selTile) && livingCardGame == game.Id) return;
            if (!rested) { if (livingCard != null) LivingCardHide(true); return; }
            LivingCardHide(false);
            LivingCardShow(selTile, game);
        }

        private bool LivingCardShow(FrameworkElement tile, Game game)
        {
            Panel parent;
            int index;
            double corner;
            if (!CardMediaSpot(tile, true, out parent, out index, out corner)) return false;
            // the picture the card shows: the wide art, the cover, or the carousel's picture (an ImageBrush on a Border)
            FrameworkElement src = null;
            Binding source = null;
            if (parent.Name == "KX_WideArt") src = UiTools.FindByName(parent, "KX_WideImage") as Image;
            if (src == null) { var pick = UiTools.FindByName(tile, "KX_ShuffleCover") as Image; if (pick != null && pick.Source != null) src = pick; }   // (Shuffle covers' pick, when the card shows one)
            if (src == null) src = UiTools.FindByName(tile, "PART_ImageCover") as Image;
            if (src != null) { if (((Image)src).Source == null) return false; source = new Binding("Source") { Source = src }; }
            else
            {
                var b = parent.Children.Count > 1 ? parent.Children[1] as Border : null;
                var brush = b != null ? b.Background as ImageBrush : null;
                if (brush == null || brush.ImageSource == null) return false;
                src = b;
                source = new Binding("Background.ImageSource") { Source = b };
            }
            double w = src.ActualWidth, h = src.ActualHeight;
            if (w < 40 || h < 40) return false;

            var host = new Grid { ClipToBounds = true, IsHitTestVisible = false, Focusable = false, Opacity = 0 };
            Grid.SetRow(host, Grid.GetRow(src)); Grid.SetColumn(host, Grid.GetColumn(src));
            Grid.SetRowSpan(host, Grid.GetRowSpan(src)); Grid.SetColumnSpan(host, Grid.GetColumnSpan(src));
            if (corner > 0)
            {
                double c = corner;
                host.SizeChanged += (s, e) => host.Clip = new RectangleGeometry(new Rect(0, 0, host.ActualWidth, host.ActualHeight), c, c);
            }
            var img = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.Fant);
            img.SetBinding(Image.SourceProperty, source);
            var zoom = new ScaleTransform(1, 1, w * 0.5, h * 0.5);
            img.RenderTransform = zoom;
            host.Children.Add(img);

            // (0.81: no sheen of light across it, only the zoom)

            try
            {
                parent.Children.Insert(Math.Max(0, Math.Min(parent.Children.Count, index)), host);
            }
            catch (Exception ex) { logger.Warn("Kippix: living picture (card): " + ex.Message); return false; }

            livingCard = host;
            livingCardTile = tile;
            livingCardGame = game.Id;
            livingCardOnContext = (s, e) => LivingCardHide(false);   // (the card handed to another game while scrolling)
            tile.DataContextChanged += livingCardOnContext;
            int gen = ++livingCardGen;

            // the slow drift and zoom (the centre wanders, so it pans as it zooms; at 1.0 it matches the cover exactly)
            LivingRun(livingCardClocks, zoom, new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }, PingPong(1.0, 1.06, 14));
            LivingRun(livingCardClocks, zoom, new[] { ScaleTransform.CenterXProperty }, PingPong(w * 0.34, w * 0.66, 19));
            LivingRun(livingCardClocks, zoom, new[] { ScaleTransform.CenterYProperty }, PingPong(h * 0.38, h * 0.62, 23));
            if (animPaused) foreach (var c in livingCardClocks) c.Controller.Pause();

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            host.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            return gen == livingCardGen;
        }

        private void LivingCardHide(bool fade)
        {
            var host = livingCard;
            var tile = livingCardTile;
            livingCard = null;
            livingCardTile = null;
            livingCardGame = Guid.Empty;
            livingCardGen++;
            if (tile != null && livingCardOnContext != null) { try { tile.DataContextChanged -= livingCardOnContext; } catch (Exception) { } }
            livingCardOnContext = null;
            var clocks = new List<AnimationClock>(livingCardClocks);
            livingCardClocks.Clear();
            if (host == null) { LivingStopClocks(clocks); return; }
            Action gone = () =>
            {
                LivingStopClocks(clocks);
                try { var p = VisualTreeHelper.GetParent(host) as Panel; if (p != null) p.Children.Remove(host); } catch (Exception) { }
            };
            if (!fade || settings.ReduceMotion || host.Opacity <= 0.01) { gone(); return; }
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            a.Completed += (s, e) => gone();
            host.BeginAnimation(UIElement.OpacityProperty, a);
        }

        // ---------------- the background ----------------

        private FrameworkElement livingBgTarget, livingBgFound;
        private ScaleTransform livingBgScale;
        private readonly List<AnimationClock> livingBgClocks = new List<AnimationClock>();
        private bool livingBgPaused;
        private Canvas livingMotes;
        private readonly List<AnimationClock> livingMoteClocks = new List<AnimationClock>();
        private bool livingMotesShown, livingMotesPaused;

        /// <summary>The background's art is ours to move (Background zoom isn't moving it): ApplyBackgroundMotion leaves it alone.</summary>
        private bool LivingOwnsBg(FrameworkElement bg)
        {
            return livingBgScale != null && bg != null && ReferenceEquals(bg, livingBgTarget) && ReferenceEquals(bg.RenderTransform, livingBgScale);
        }

        /// <summary>From AnimArtTick (home screen visible): the background's living picture, under an animated hero's place.</summary>
        private void LivingBgTick(Game game)
        {
            bool want = game != null && settings.AnimArtNoArt == 0 && settings.AnimArtHero && !LivingBlocked && HomeArtShown
                        && settings.HomeBgMode != 2 && !gameRunningNow;
            // something already moves over the art: an animated hero, the trailer, the slideshow or a trailer preview
            bool covered = animHeroPlayer != null || liveVideoShown || slidesOn || previewShown;
            var bg = livingBgFound;
            if (bg == null || !bg.IsLoaded)
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                bg = livingBgFound = win != null ? UiTools.FindByName(win, "PART_ImageBackground") as FrameworkElement : null;   // (looked up once, not every tick)
            }

            // the art's slow drift and zoom (Background zoom, when it's on, already moves it)
            if (want && !settings.BackgroundMotion && bg != null && bg.ActualWidth > 100)
            {
                if (!LivingOwnsBg(bg)) LivingBgStart(bg);
                LivingBgPause(covered);
            }
            else LivingBgStop();

            // the motes of light
            LivingMotesShow(want && !covered);
            if (want && !covered && game != null && AnimNoneFor(game.Id, "hero")) LogAnimSource(game, "background", AnimNoneReason() + ", so a living picture of its art");
        }

        private void LivingBgStart(FrameworkElement bg)
        {
            LivingBgStop();
            if (bg.RenderTransform != null && !(bg.RenderTransform is MatrixTransform && bg.RenderTransform.Value.IsIdentity)) return;   // (something else has it)
            double w = bg.ActualWidth, h = bg.ActualHeight;
            livingBgTarget = bg;
            livingBgScale = new ScaleTransform(1, 1, w * 0.5, h * 0.5);
            bg.RenderTransformOrigin = new Point(0, 0);
            bg.RenderTransform = livingBgScale;
            LivingRun(livingBgClocks, livingBgScale, new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }, PingPong(1.0, 1.05, 16));
            LivingRun(livingBgClocks, livingBgScale, new[] { ScaleTransform.CenterXProperty }, PingPong(w * 0.3, w * 0.7, 27));
            LivingRun(livingBgClocks, livingBgScale, new[] { ScaleTransform.CenterYProperty }, PingPong(h * 0.36, h * 0.64, 21));
            livingBgPaused = false;
        }

        private void LivingBgPause(bool pause)
        {
            if (pause == livingBgPaused) return;
            livingBgPaused = pause;
            foreach (var c in livingBgClocks) { try { if (pause) c.Controller.Pause(); else c.Controller.Resume(); } catch (Exception) { } }
        }

        private void LivingBgStop()
        {
            var clocks = new List<AnimationClock>(livingBgClocks);
            livingBgClocks.Clear();
            LivingStopClocks(clocks);
            if (livingBgTarget != null && livingBgScale != null && ReferenceEquals(livingBgTarget.RenderTransform, livingBgScale))
                livingBgTarget.RenderTransform = Transform.Identity;
            livingBgScale = null;
            livingBgTarget = null;
            livingBgPaused = false;
        }

        /// <summary>A few soft motes of light drifting up through the background (made once, then faded in and out).</summary>
        private void LivingMotesShow(bool show)
        {
            if (show && livingMotes == null)
            {
                var host = LiveHost();
                if (host == null || host.ActualWidth < 200) return;
                LivingMotesBuild(host);
            }
            if (livingMotes == null || show == livingMotesShown) return;
            livingMotesShown = show;
            var m = livingMotes;
            if (show)
            {
                LivingMotesPause(false);
                m.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(900)));
            }
            else
            {
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
                a.Completed += (s, e) => { if (!livingMotesShown) LivingMotesPause(true); };
                m.BeginAnimation(UIElement.OpacityProperty, a);
            }
        }

        private void LivingMotesPause(bool pause)
        {
            if (pause == livingMotesPaused) return;
            livingMotesPaused = pause;
            foreach (var c in livingMoteClocks) { try { if (pause) c.Controller.Pause(); else c.Controller.Resume(); } catch (Exception) { } }
        }

        private void LivingMotesBuild(Grid host)
        {
            double w = host.ActualWidth, h = host.ActualHeight;
            var canvas = new Canvas { IsHitTestVisible = false, Focusable = false, Opacity = 0, ClipToBounds = true };
            Panel.SetZIndex(canvas, 3);   // over the art and the slideshow's pictures, under the trailer (4) and an animated hero (5)
            var rnd = new Random(1407);
            // pale, warm and cool: soft discs (a radial fade, no blur effect)
            Color[] tones = { Color.FromRgb(0xFF, 0xF1, 0xDC), Color.FromRgb(0xDC, 0xE8, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF) };
            for (int i = 0; i < 9; i++)
            {
                Color t = tones[i % tones.Length];
                var fill = new RadialGradientBrush();
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, t.R, t.G, t.B), 0));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, t.R, t.G, t.B), 0.45));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, t.R, t.G, t.B), 1));
                fill.Freeze();
                double d = 10 + rnd.NextDouble() * 26;
                var e = new Ellipse { Width = d, Height = d, Fill = fill, IsHitTestVisible = false, Opacity = 0 };
                double x = w * (0.04 + rnd.NextDouble() * 0.92), y = h * (0.42 + rnd.NextDouble() * 0.5);
                Canvas.SetLeft(e, x);
                Canvas.SetTop(e, y);
                var move = new TranslateTransform();
                e.RenderTransform = move;
                canvas.Children.Add(e);

                double rise = 26 + rnd.NextDouble() * 20;              // seconds for one climb
                double up = h * (0.22 + rnd.NextDouble() * 0.25);      // how far it climbs
                var begin = TimeSpan.FromSeconds(rnd.NextDouble() * 12);
                var climb = new DoubleAnimation(0, -up, TimeSpan.FromSeconds(rise)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
                LivingRun(livingMoteClocks, move, new[] { TranslateTransform.YProperty }, climb);
                var sway = PingPong(-(14 + rnd.NextDouble() * 30), 14 + rnd.NextDouble() * 30, 8 + rnd.NextDouble() * 8);
                LivingRun(livingMoteClocks, move, new[] { TranslateTransform.XProperty }, sway);
                // in, glow, out over each climb (out by the time it jumps back down)
                var glow = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(rise)), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
                double peak = 0.45 + rnd.NextDouble() * 0.45;
                glow.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                glow.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.25), new SineEase { EasingMode = EasingMode.EaseInOut }));
                glow.KeyFrames.Add(new EasingDoubleKeyFrame(peak * 0.7, KeyTime.FromPercent(0.6), new SineEase { EasingMode = EasingMode.EaseInOut }));
                glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
                LivingRun(livingMoteClocks, e, new[] { UIElement.OpacityProperty }, glow);
            }
            host.Children.Add(canvas);
            livingMotes = canvas;
            livingMotesShown = false;
            livingMotesPaused = false;
            LivingMotesPause(true);
        }

        private void LivingMotesRemove()
        {
            var clocks = new List<AnimationClock>(livingMoteClocks);
            livingMoteClocks.Clear();
            LivingStopClocks(clocks);
            if (livingMotes != null)
            {
                try { var p = VisualTreeHelper.GetParent(livingMotes) as Panel; if (p != null) p.Children.Remove(livingMotes); } catch (Exception) { }
            }
            livingMotes = null;
            livingMotesShown = false;
            livingMotesPaused = false;
        }

        // ---------------- together ----------------

        /// <summary>The home screen is covered (or back): everything living holds still where it is.</summary>
        private void LivingPause(bool pause)
        {
            foreach (var c in livingCardClocks) { try { if (pause) c.Controller.Pause(); else c.Controller.Resume(); } catch (Exception) { } }
            if (pause) { LivingBgPauseRaw(true); LivingMotesPause(true); }
            else
            {
                LivingBgPauseRaw(livingBgPaused);
                if (livingMotesShown) LivingMotesPause(false);
            }
        }

        private void LivingBgPauseRaw(bool pause)
        {
            foreach (var c in livingBgClocks) { try { if (pause) c.Controller.Pause(); else c.Controller.Resume(); } catch (Exception) { } }
        }

        /// <summary>Everything living gone (Animated art off, Still picture, Reduce motion, battery saving).</summary>
        private void LivingStop()
        {
            LivingCardHide(false);
            LivingBgStop();
            LivingMotesRemove();
        }

        private static DoubleAnimation PingPong(double from, double to, double seconds)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        }

        /// <summary>Starts an animation on a transform / element as a clock (kept, so it can be paused and stopped), at 30 fps.</summary>
        private static void LivingRun(List<AnimationClock> into, DependencyObject target, DependencyProperty[] props, AnimationTimeline a)
        {
            Timeline.SetDesiredFrameRate(a, 30);
            var clock = a.CreateClock();
            var an = target as IAnimatable;
            if (an == null) return;
            foreach (var p in props) an.ApplyAnimationClock(p, clock);
            into.Add(clock);
        }

        private static void LivingStopClocks(List<AnimationClock> clocks)
        {
            foreach (var c in clocks) { try { c.Controller.Stop(); c.Controller.Remove(); } catch (Exception) { } }
        }
    }
}
