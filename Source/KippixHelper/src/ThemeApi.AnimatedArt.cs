using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>An animation decoded into ready-to-show frames (frozen, at display size) and how long each one stays.</summary>
    internal sealed class AnimFrames
    {
        public readonly List<BitmapSource> Frames = new List<BitmapSource>();
        public readonly List<int> Delays = new List<int>();   // ms
        public int Width, Height;
        public long Bytes;
        public string Source;   // where it came from (the log)
    }

    /// <summary>
    /// Home settings › Background › ANIMATED ART (Look and feel › Animated art until r14): covers (the highlighted card, or every
    /// card on screen) and the background (a "hero") that move. A game with no animated art gets a living picture made from its
    /// own art (ThemeApi.LivingArt.cs), or stays still: never its trailer (r14).
    ///
    /// Where animations come from (researched for 0.78):
    ///   - Steam's CDN serves no animated library art: library_600x900, library_hero and logo are still pictures, and the store's
    ///     trailers now come only as DASH / HLS. So nothing animated is taken from Steam's servers.
    ///   - Animated art you've set in the Steam client's own library (userdata\…\config\grid: &lt;appid&gt;p.png, &lt;appid&gt;_hero.png,
    ///     e.g. put there by SteamGridDB's tools) is used as it is, read-only, without a key.
    ///   - SteamGridDB (with your own free API key, desktop settings): animated grids and heroes, asked for as image/png (animated PNG).
    ///     SteamGridDB also has animated WebP and WebM versions; WPF can't play those (WebP needs a codec Windows may not have and
    ///     shows one frame at most; WebM isn't something MediaElement can rely on), and nothing is converted here, so they're skipped.
    ///   - WPF itself only shows an animated PNG's first frame and doesn't animate GIFs either, so both are decoded here, off the
    ///     UI thread, into frames at the size they're shown (APNG: its fcTL / fdAT chunks rebuilt into plain PNGs and composed;
    ///     GIF: GifBitmapDecoder's frames with their offsets, delays and disposal), within a memory budget.
    /// Off: nothing. Stream: looked up when a game is highlighted, kept in memory for the session. Download: also saved in
    /// ExtensionsData\animated\ and reused (a "nothing found" is remembered for a week).
    /// </summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // The SteamGridDB key (desktop settings), kept like the Steam key: encrypted for this Windows user
        // =====================================================================

        public bool HasSgdbKey { get { return !string.IsNullOrEmpty(SgdbKey); } }

        private string sgdbKeyFrom, sgdbKeyPlain;
        private string SgdbKey
        {
            get
            {
                string enc = settings.SgdbApiKey;
                if (string.IsNullOrEmpty(enc)) return null;
                if (enc != sgdbKeyFrom)
                {
                    string plain;
                    try { plain = Unprotect(enc); } catch (Exception) { plain = null; }
                    sgdbKeyPlain = plain;
                    sgdbKeyFrom = enc;
                }
                return sgdbKeyPlain;
            }
        }

        /// <summary>Saves (or with an empty value removes) the SteamGridDB API key. Used by the desktop settings page.</summary>
        internal void SetSgdbKey(string value)
        {
            string key = (value ?? "").Trim();
            settings.SgdbApiKey = key.Length == 0 ? "" : Protect(key);
            SaveSettings();
            sgdbRefused = false;
            lock (animLock) { animNone.Clear(); sgdbIds.Clear(); }
            Notify("HasSgdbKey");
            Notify("AnimArtSummary");
        }

        // =====================================================================
        // Home settings › Background › ANIMATED ART (r14: moved here from Look and feel; rows built in code, BgAnimRows),
        // and the page's RESET row
        // =====================================================================

        private ObservableCollection<PickRow> bgAnimRows;
        public ObservableCollection<PickRow> BgAnimRows
        {
            get
            {
                // built the first time it's asked for, so settings search finds these rows before the page has been opened
                if (bgAnimRows == null) { bgAnimRows = new ObservableCollection<PickRow>(); try { BuildBgAnimRows(); } catch (Exception) { } }
                return bgAnimRows;
            }
        }

        /// <summary>For the Background row on Home settings: "animated art: Stream" when it's on.</summary>
        public string AnimArtSummary
        {
            get
            {
                return settings.AnimArtMode == 0 ? Loc("LOC_KX_Off", "Off") : Loc("LOC_KX_On", "On");
            }
        }

        private string AnimModeName(int m)
        {
            if (m == 1) return Loc("LOC_KX_AnimStream", "Stream");
            if (m == 2) return Loc("LOC_KX_AnimDownload", "Download");
            return Loc("LOC_KX_Off", "Off");
        }

        private string AnimFolder { get { return Path.Combine(plugin.GetPluginUserDataPath(), "animated"); } }

        /// <summary>The background behind the home screen is the game's art (or starts as it): "Animate the background too" means something.</summary>
        private bool AnimHeroApplies { get { return settings.HomeBgMode == 0 || settings.HomeBgMode == 4 || settings.HomeBgMode == 5; } }

        private void BuildBgAnimRows()
        {
            var rows = BgAnimRows;
            rows.Clear();
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecAnimArt", "ANIMATED ART") });
            // 0.81: only the slow zoom on the highlighted cover (no animated art fetched or saved, nothing on the background)
            rows.Add(BoardSwitch("filmstrip", "animmode", Loc("LOC_KX_AnimCovers", "Animated covers"), settings.AnimArtMode != 0,
                                 Loc("LOC_KX_AnimCoversOn", "The highlighted cover zooms in and out, slowly"), Loc("LOC_KX_AnimCoversOff", "Covers stay still"),
                                 () => { settings.AnimArtMode = settings.AnimArtMode != 0 ? 0 : 1; AnimArtSettingChanged("animmode"); }));
            if (settings.AnimArtMode != 0 && settings.ReduceMotion)
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "animrm", Text = Loc("LOC_KX_AnimReduceMotion2", "Reduce motion is on (Accessibility), so nothing moves") });
            long bytes = 0;
            try { if (Directory.Exists(AnimFolder)) bytes = new DirectoryInfo(AnimFolder).GetFiles().Sum(f => f.Length); } catch (Exception) { }
            if (bytes > 0)   // (animated art saved by 0.78 – 0.80: no longer used)
                rows.Add(new PickRow
                {
                    Key = "animclear", Icon = "trash", Text = Loc("LOC_KX_AnimClear3", "Delete old animated art downloads"),
                    Sub = string.Format(Loc("LOC_KX_AnimClearSub", "{0} MB in the add-on's folder"), Math.Round(bytes / 1048576.0, 1)),
                    Command = new RelayCommand(_ => ClearAnimFolder())
                });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecReset", "RESET") });
            rows.Add(new PickRow
            {
                Key = "bgreset", Icon = "refresh", Text = Loc("LOC_KX_ResetPage", "Reset this page"), Sub = Loc("LOC_KX_ResetPageSub", "Resets this page to default"),
                Command = new RelayCommand(_ => Confirm(Loc("LOC_KX_BgResetAsk", "Reset the background?"),
                                                        Loc("LOC_KX_BgResetText2", "The game's art, where the picture sits, the fade, motion and animated art go back to how they came. Your own picture is kept on this PC."),
                                                        Loc("LOC_KX_Reset", "Reset"), ResetBackgroundPage))
            });
        }

        /// <summary>Background › Reset this page: only what lives on this page (the trailer preview's own settings stay).</summary>
        private void ResetBackgroundPage()
        {
            var d = new HelperSettings();
            settings.HomeBgColor = d.HomeBgColor;
            settings.HomeFadeDir = d.HomeFadeDir;
            settings.HomeFadeReach = d.HomeFadeReach;
            settings.HomeBgOffsetX = d.HomeBgOffsetX;   // (r15: Move the picture: back in the middle; SetHomeBgMode below applies it)
            settings.HomeBgOffsetY = d.HomeBgOffsetY;
            settings.LiveSlideWeb = d.LiveSlideWeb;
            settings.HomeVideo = d.HomeVideo; settings.HomeVideoMode = d.HomeVideoMode; settings.HomeVideoDownload = d.HomeVideoDownload; settings.HomeVideoOnBattery = d.HomeVideoOnBattery; NotifyHomeVideo();   // (issue #13; the tick stops it)
            settings.BackgroundMotion = d.BackgroundMotion;
            settings.MotionSpeed = d.MotionSpeed;
            settings.MotionAmount = d.MotionAmount;
            settings.DepthMode = d.DepthMode;
            settings.AnimArtMode = d.AnimArtMode;
            settings.AnimArtAllCards = d.AnimArtAllCards;
            settings.AnimArtHero = d.AnimArtHero;
            settings.AnimArtNoArt = d.AnimArtNoArt;
            AnimArtStopAll();
            SetHomeBgMode(d.HomeBgMode);   // (saves; gives the trailer preview its own place back)
            NotifyLook();
            NotifyLookExtras();
            ApplyBackgroundMotion();       // (Depth and the live timer follow)
            PlaySound("select");
            if (openPanel == "options" && optionsPage == "background") BoardRowsAgain(BuildBgAnimRows, "bgreset");
        }

        private bool AnimArtNudge(string key, int dir)
        {
            if (key == "animmode")
            {
                settings.AnimArtMode = settings.AnimArtMode != 0 ? 0 : 1;
            }
            else return false;
            AnimArtSettingChanged(key);
            return true;
        }

        /// <summary>Animated art just turned on with no SteamGridDB key: says where to add one (once a session).</summary>
        private void AnimArtModeNotice(int was)
        {
            // (1.0.0) said where to add a SteamGridDB key, but there's nowhere to add one any more (0.81): only Steam's own
            // animated art is used, so there's nothing to tell
        }

        private void AnimArtSettingChanged(string key)
        {
            AnimArtStopAll();
            EnsureLiveTimer();
            SaveSettings();
            PlaySound("move");
            NotifyLookExtras();
            Notify("HomeBgSummary");
            if (openPanel == "options" && optionsPage == "background") BoardRowsAgain(BuildBgAnimRows, key);
        }

        private void ClearAnimFolder()
        {
            PlaySound("select");
            AnimArtStopAll();
            string dir = AnimFolder;
            KippixShared.SafeWork.Queue(_ =>
            {
                try { if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir)) { try { File.Delete(f); } catch (Exception) { } } } catch (Exception) { }
                lock (animLock) { animNone.Clear(); animMem.Clear(); animOrder.Clear(); }
                OnUi(() => { if (openPanel == "options" && optionsPage == "background") BoardRowsAgain(BuildBgAnimRows, "animmode"); });
            });
        }

        // =====================================================================
        // On screen
        // =====================================================================

        private class AnimPlayer
        {
            public Image Target;
            public AnimFrames F;
            public int At;
            public double Left;
            public Guid Game;
            public FrameworkElement Tile;      // the card it's in (null: the hero / the intro)
            public bool Once;
            public Action Done;
            public DependencyPropertyChangedEventHandler OnContext;
        }

        private readonly List<AnimPlayer> animPlayers = new List<AnimPlayer>();
        private DispatcherTimer animTimer;
        private DateTime animLast;
        private bool animPaused;
        private Image animHero;
        private AnimPlayer animHeroPlayer;

        /// <summary>From the live background timer (every 250 ms, fullscreen): which cards / hero should move now.</summary>
        private void AnimArtTick(Game game, bool visible)
        {
            if (settings.AnimArtMode == 0 || settings.ReduceMotion || !IsFullscreen)
            {
                if (animPlayers.Any(x => !x.Once) || (animHero != null && animHero.Source != null) || LivingActive) AnimArtStopAll();
                return;
            }
            if (LivingActive && LivingBlocked) LivingStop();   // (battery saving: nothing moves)
            if (!visible) { AnimPause(true); return; }
            AnimPause(false);
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return;
            // 0.81: Animated covers is only the slow zoom on the highlighted cover. Nothing animated is fetched or saved any
            // more and the background doesn't move: anything still showing from before goes.
            foreach (var p in animPlayers.Where(p => !p.Once).ToList()) DetachAnim(p);
            if (animHeroPlayer != null) HideHero();
            if (livingBgScale != null) LivingBgStop();
            if (livingMotes != null) LivingMotesRemove();

            FrameworkElement selTile = null;
            ListBox list = PreviewList(win);
            if (carActive) selTile = CarouselPreviewTile();
            else if (list != null && list.SelectedItem != null) selTile = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as FrameworkElement;
            bool rested = (DateTime.Now - liveSince).TotalSeconds >= LivingRestSeconds;
            LivingCardTick(selTile, game, rested, false);
        }

        private static bool OnScreen(FrameworkElement c, Window win)
        {
            try
            {
                var r = c.TransformToAncestor(win).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
                return r.Right > 0 && r.Bottom > 0 && r.Left < win.ActualWidth && r.Top < win.ActualHeight;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The card shows its cover (not the wide art / list look, which use other pictures).</summary>
        private static bool CardShowsCover(FrameworkElement tile)
        {
            var wide = UiTools.FindByName(tile, "KX_WideArt");
            if (wide != null && wide.IsVisible) return false;
            var cover = UiTools.FindByName(tile, "PART_ImageCover");
            if (cover != null) return cover.IsVisible;
            return UiTools.FindByName(tile, "KX_CarTile") != null || (tile.Name == "KX_CarTile");
        }

        /// <summary>The card's size in real screen pixels (decoding at that size keeps memory down and pictures sharp).</summary>
        private static void PixelSize(FrameworkElement tile, Window win, out int w, out int h)
        {
            w = h = 0;
            try
            {
                var r = tile.TransformToAncestor(win).TransformBounds(new Rect(0, 0, tile.ActualWidth, tile.ActualHeight));
                var src = PresentationSource.FromVisual(win);
                double dpi = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M11 : 1;
                w = (int)(Math.Ceiling(r.Width * dpi / 40) * 40);
                h = (int)(Math.Ceiling(r.Height * dpi / 40) * 40);
                w = Math.Min(w, 720);
                h = Math.Min(h, 1080);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Where something moving goes in a card: just over its picture, under the badges, the name and the highlight.
        /// Playnite's cards (Row / Grid): over PART_ImageCover; wide and list cards: over KX_WideImage (the art half);
        /// the theme's carousel: over the picture Border in KX_CarTile (rounded with the same corners).
        /// </summary>
        private static bool CardMediaSpot(FrameworkElement tile, bool allowWide, out Panel parent, out int index, out double corner)
        {
            parent = null; index = 0; corner = 0;
            var wide = UiTools.FindByName(tile, "KX_WideArt") as Panel;
            var wideImg = wide != null && wide.IsVisible ? UiTools.FindByName(wide, "KX_WideImage") as FrameworkElement : null;
            var cover = UiTools.FindByName(tile, "PART_ImageCover") as FrameworkElement;
            // (Shuffle covers' picture lies just over the cover: what moves goes over that)
            var shuffled = cover != null ? UiTools.FindByName(tile, "KX_ShuffleCover") as FrameworkElement : null;
            if (shuffled != null && ReferenceEquals(VisualTreeHelper.GetParent(shuffled), VisualTreeHelper.GetParent(cover))) cover = shuffled;
            if (allowWide && wideImg != null && ReferenceEquals(VisualTreeHelper.GetParent(wideImg), wide))
            {
                parent = wide;
                index = wide.Children.IndexOf(wideImg) + 1;
            }
            else if (cover != null)
            {
                parent = VisualTreeHelper.GetParent(cover) as Panel;
                if (parent != null) index = parent.Children.IndexOf(cover) + 1;
            }
            else
            {
                var car = tile.Name == "KX_CarTile" ? tile as Grid : UiTools.FindByName(tile, "KX_CarTile") as Grid;
                if (car != null)
                {
                    parent = car;
                    index = Math.Min(2, car.Children.Count);
                    var b = car.Children.Count > 1 ? car.Children[1] as Border : null;
                    if (b != null) corner = b.CornerRadius.TopLeft;
                }
            }
            return parent != null && !parent.IsItemsHost;
        }

        private void AttachCard(FrameworkElement tile, Guid gameId, AnimFrames f)
        {
            Panel parent;
            int index;
            double corner;
            if (!CardMediaSpot(tile, false, out parent, out index, out corner)) return;
            var img = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Focusable = false, Source = f.Frames[0] };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (corner > 0)
            {
                // the theme's carousel cards round their picture with a Border: the same corners here
                double c = corner;
                SizeChangedEventHandler clip = (s, e) => img.Clip = new RectangleGeometry(new Rect(0, 0, img.ActualWidth, img.ActualHeight), c, c);
                img.SizeChanged += clip;
            }
            parent.Children.Insert(Math.Max(0, Math.Min(parent.Children.Count, index)), img);
            var p = new AnimPlayer { Target = img, F = f, At = 0, Left = f.Delays[0], Game = gameId, Tile = tile };
            // a card handed to another game (scrolling re-uses them): its animation goes at once
            p.OnContext = (s, e) => DetachAnim(p);
            tile.DataContextChanged += p.OnContext;
            animPlayers.Add(p);
            AnimTimerOn();
        }

        private void DetachAnim(AnimPlayer p)
        {
            animPlayers.Remove(p);
            try
            {
                if (p.Tile != null && p.OnContext != null) p.Tile.DataContextChanged -= p.OnContext;
                if (p.Target != null && !ReferenceEquals(p.Target, animHero))
                {
                    var parent = VisualTreeHelper.GetParent(p.Target) as Panel;
                    if (parent != null) parent.Children.Remove(p.Target);
                    p.Target.Source = null;
                }
            }
            catch (Exception) { }
        }

        private void ShowHero(Guid gameId, AnimFrames f)
        {
            var host = LiveHost();
            if (host == null) return;
            if (animHero == null || !ReferenceEquals(VisualTreeHelper.GetParent(animHero), host))
            {
                animHero = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Opacity = 0 };
                Panel.SetZIndex(animHero, 5);
                host.Children.Add(animHero);
            }
            animHero.Source = f.Frames[0];
            animHero.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(settings.ReduceMotion ? 1 : 600)));
            animHeroPlayer = new AnimPlayer { Target = animHero, F = f, At = 0, Left = f.Delays[0], Game = gameId };
            animPlayers.Add(animHeroPlayer);
            AnimTimerOn();
        }

        private void HideHero()
        {
            if (animHeroPlayer != null) { animPlayers.Remove(animHeroPlayer); animHeroPlayer = null; }
            if (animHero != null && animHero.Source != null)
            {
                var h = animHero;
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
                a.Completed += (s, e) => { if (animHeroPlayer == null) h.Source = null; };
                h.BeginAnimation(UIElement.OpacityProperty, a);
            }
        }

        /// <summary>Animated art off (or Reduce motion on): every card back to its cover, the hero and the living picture gone.</summary>
        private void AnimArtStopAll()
        {
            foreach (var p in animPlayers.Where(x => !x.Once).ToList()) DetachAnim(p);
            HideHero();
            LivingStop();
            if (animHero != null) { animHero.BeginAnimation(UIElement.OpacityProperty, null); animHero.Opacity = 0; animHero.Source = null; }
            if (animPlayers.Count == 0 && animTimer != null) animTimer.Stop();
        }

        private void AnimPause(bool pause)
        {
            if (pause == animPaused) return;
            animPaused = pause;
            if (pause) { if (animTimer != null && !animPlayers.Any(p => p.Once)) animTimer.Stop(); }
            else AnimTimerOn();
            LivingPause(pause);   // the living picture holds still too
        }

        private void AnimTimerOn()
        {
            if (ui == null || animPlayers.Count == 0) return;
            if (animTimer == null)
            {
                animTimer = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(16) };
                animTimer.Tick += (s, e) => AnimStep();
            }
            if (!animTimer.IsEnabled) { animLast = DateTime.Now; animTimer.Start(); }
        }

        private void AnimStep()
        {
            try
            {
                var now = DateTime.Now;
                double dt = Math.Min(250, (now - animLast).TotalMilliseconds);
                animLast = now;
                foreach (var p in animPlayers.ToList())
                {
                    if (animPaused && !p.Once) continue;
                    int n = p.F.Frames.Count;
                    if (n < 2) continue;
                    int was = p.At;
                    p.Left -= dt;
                    bool finished = false;
                    int guard = 0;
                    while (p.Left <= 0 && guard++ < n)
                    {
                        p.At++;
                        if (p.At >= n)
                        {
                            if (p.Once) { finished = true; p.At = n - 1; break; }
                            p.At = 0;
                        }
                        p.Left += Math.Max(16, p.F.Delays[p.At]);
                    }
                    if (p.Left <= 0) p.Left = p.F.Delays[p.At];
                    if (p.At != was) p.Target.Source = p.F.Frames[p.At];
                    if (finished)
                    {
                        animPlayers.Remove(p);
                        if (p.Done != null) { try { p.Done(); } catch (Exception) { } }
                    }
                }
                if (animPlayers.Count == 0 || (animPaused && !animPlayers.Any(x => x.Once))) animTimer.Stop();
            }
            catch (Exception ex) { logger.Warn("Kippix: animated art: " + ex.Message); if (animTimer != null) animTimer.Stop(); }
        }

        /// <summary>The intro / outro's GIF: played through once (then <paramref name="done"/>).</summary>
        private void AnimPlayOnce(Image target, AnimFrames f, Action done)
        {
            target.Source = f.Frames[0];
            animPlayers.Add(new AnimPlayer { Target = target, F = f, At = 0, Left = f.Delays[0], Once = true, Done = done });
            AnimTimerOn();
        }

        private void AnimStopOnce()
        {
            foreach (var p in animPlayers.Where(x => x.Once).ToList()) { animPlayers.Remove(p); try { p.Target.Source = null; } catch (Exception) { } }
        }

        // =====================================================================
        // Finding and fetching (one at a time, off the UI thread)
        // =====================================================================

        private class AnimGame
        {
            public Guid Id;
            public string Name, SteamId;
            public string Kind;
            public int W, H;
            public string Key { get { return Id + "|" + Kind + "|" + W + "x" + H; } }
            public string BaseKey { get { return Id + "|" + Kind; } }
        }

        private readonly object animLock = new object();
        private readonly Dictionary<string, AnimFrames> animMem = new Dictionary<string, AnimFrames>();
        private readonly List<string> animOrder = new List<string>();         // least recently used first
        private readonly HashSet<string> animNone = new HashSet<string>();    // Id|kind: nothing animated (this session)
        private readonly Dictionary<string, byte[]> animRaw = new Dictionary<string, byte[]>();   // Stream: the files, while they're small enough to keep
        private readonly Dictionary<Guid, int> sgdbIds = new Dictionary<Guid, int>();
        private readonly List<AnimGame> animQueue = new List<AnimGame>();
        private bool animWorking, sgdbRefused, sgdbLogged;
        private const long AnimMemBudget = 220L * 1024 * 1024;

        /// <summary>The decoded animation if it's ready; otherwise asks for it (and returns null for now).</summary>
        private AnimFrames AnimGet(Game game, string kind, int w, int h)
        {
            if (game == null) return null;
            var g = new AnimGame { Id = game.Id, Name = game.Name ?? "", SteamId = game.PluginId == SteamPluginId ? game.GameId : null, Kind = kind, W = w, H = h };
            lock (animLock)
            {
                if (animNone.Contains(g.BaseKey)) return null;
                AnimFrames f;
                if (animMem.TryGetValue(g.Key, out f))
                {
                    animOrder.Remove(g.Key);
                    animOrder.Add(g.Key);
                    return f;
                }
                if (!animQueue.Any(q => q.Key == g.Key)) animQueue.Add(g);
                // only what's still wanted: the latest asks first, at most a screenful waiting
                while (animQueue.Count > 16) animQueue.RemoveAt(0);
                if (animWorking) return null;
                animWorking = true;
            }
            int mode = settings.AnimArtMode;
            string key = SgdbKey, folder = AnimFolder;
            KippixShared.SafeWork.Queue(_ => AnimWorker(mode, key, folder));
            return null;
        }

        private void AnimWorker(int mode, string key, string folder)
        {
            while (true)
            {
                AnimGame g;
                lock (animLock)
                {
                    if (animQueue.Count == 0) { animWorking = false; return; }
                    g = animQueue[animQueue.Count - 1];   // newest first (the card you're on now)
                    animQueue.RemoveAt(animQueue.Count - 1);
                    if (animNone.Contains(g.BaseKey) || animMem.ContainsKey(g.Key)) continue;
                }
                AnimFrames f = null;
                bool none = false;
                try
                {
                    string source;
                    byte[] data = AnimBytes(g, mode, key, folder, out none, out source);
                    if (data != null)
                    {
                        f = AnimDecode(data, g.W, g.H, g.Kind == "hero" ? 64L * 1024 * 1024 : 36L * 1024 * 1024);
                        if (f == null) none = true;   // (a still picture, or one that can't be read)
                        else f.Source = source;
                    }
                }
                catch (Exception ex) { logger.Info("Kippix: animated art for " + g.Name + ": " + ex.Message); none = true; }   // (not asked again this session)
                lock (animLock)
                {
                    if (f != null)
                    {
                        animMem[g.Key] = f;
                        animOrder.Remove(g.Key);
                        animOrder.Add(g.Key);
                        long total = animMem.Values.Sum(x => x.Bytes);
                        while (total > AnimMemBudget && animOrder.Count > 1)
                        {
                            string old = animOrder[0];
                            animOrder.RemoveAt(0);
                            AnimFrames o;
                            if (animMem.TryGetValue(old, out o)) { total -= o.Bytes; animMem.Remove(old); }
                        }
                    }
                    else if (none) animNone.Add(g.BaseKey);
                }
            }
        }

        /// <summary>The animation's file: Steam's own library art, then a saved download, then SteamGridDB.</summary>
        private byte[] AnimBytes(AnimGame g, int mode, string key, string folder, out bool none, out string source)
        {
            none = false;
            source = null;
            // 1. animated art set in the Steam client's library (read only)
            if (!string.IsNullOrEmpty(g.SteamId))
            {
                string local = SteamGridFile(g.SteamId, g.Kind);
                if (local != null)
                {
                    try { var b = File.ReadAllBytes(local); if (IsAnimated(b)) { source = "Steam library art (" + Path.GetFileName(local) + ")"; return b; } } catch (Exception) { }
                }
            }
            string baseName = g.Id.ToString("N") + "_" + g.Kind;
            // 2. downloaded before
            if (mode == 2)
            {
                string saved = Path.Combine(folder, baseName + ".png");
                string mark = Path.Combine(folder, baseName + ".none");
                try
                {
                    if (File.Exists(saved)) { source = "SteamGridDB (downloaded before)"; return File.ReadAllBytes(saved); }
                    if (File.Exists(mark) && (DateTime.Now - File.GetLastWriteTime(mark)).TotalDays < 7) { none = true; return null; }
                }
                catch (Exception) { }
            }
            else
            {
                lock (animLock) { byte[] raw; if (animRaw.TryGetValue(baseName, out raw)) { source = "SteamGridDB (streamed)"; return raw; } }
            }
            // 3. SteamGridDB (your key)
            if (string.IsNullOrEmpty(key) || sgdbRefused) { none = true; return null; }
            byte[] data = SgdbAnimated(g, key);
            if (data == null) { none = true; }
            else source = mode == 2 ? "SteamGridDB (downloaded)" : "SteamGridDB (streamed)";
            if (mode == 2)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    if (data != null) File.WriteAllBytes(Path.Combine(folder, baseName + ".png"), data);
                    else if (!sgdbRefused) File.WriteAllText(Path.Combine(folder, baseName + ".none"), DateTime.Now.ToString("s"));
                }
                catch (Exception) { }
            }
            else if (data != null)
            {
                lock (animLock)
                {
                    if (animRaw.Values.Sum(b => (long)b.Length) + data.Length > 48L * 1024 * 1024) animRaw.Clear();
                    animRaw[baseName] = data;
                }
            }
            return data;
        }

        /// <summary>Steam's library art for this game set in the Steam client (userdata\…\config\grid), when it's there.</summary>
        private static string SteamGridFile(string appId, string kind)
        {
            try
            {
                string steam = SteamPaths.SteamPath();
                if (string.IsNullOrEmpty(steam)) return null;
                string userdata = Path.Combine(steam, "userdata");
                if (!Directory.Exists(userdata)) return null;
                string name = kind == "hero" ? appId + "_hero" : appId + "p";
                foreach (string user in Directory.GetDirectories(userdata))
                {
                    string grid = Path.Combine(user, "config", "grid");
                    if (!Directory.Exists(grid)) continue;
                    foreach (string ext in new[] { ".png", ".gif", ".apng" })
                    {
                        string f = Path.Combine(grid, name + ext);
                        if (File.Exists(f)) return f;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        private object SgdbGet(string key, string path)
        {
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Kippix";
                web.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
                web.Encoding = Encoding.UTF8;
                try
                {
                    string json = web.DownloadString("https://www.steamgriddb.com/api/v2/" + path);
                    return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
                }
                catch (WebException ex)
                {
                    var r = ex.Response as HttpWebResponse;
                    int code = r != null ? (int)r.StatusCode : 0;
                    if (code == 401 || code == 403)
                    {
                        sgdbRefused = true;
                        if (!sgdbLogged) { sgdbLogged = true; logger.Warn("Kippix: SteamGridDB refused the API key (" + code + ")"); }
                    }
                    if (code == 404) return null;   // (no such game there)
                    throw;
                }
            }
        }

        private byte[] SgdbAnimated(AnimGame g, string key)
        {
            int id;
            bool known;
            lock (animLock) known = sgdbIds.TryGetValue(g.Id, out id);
            if (!known)
            {
                id = 0;
                if (!string.IsNullOrEmpty(g.SteamId))
                {
                    var r = SgdbGet(key, "games/steam/" + Uri.EscapeDataString(g.SteamId)) as Dictionary<string, object>;
                    var d = r != null && r.ContainsKey("data") ? r["data"] as Dictionary<string, object> : null;
                    if (d != null && d.ContainsKey("id")) id = Convert.ToInt32(d["id"]);
                }
                if (id == 0 && g.Name.Length > 0 && !sgdbRefused)
                {
                    var r = SgdbGet(key, "search/autocomplete/" + Uri.EscapeDataString(g.Name)) as Dictionary<string, object>;
                    var list = r != null && r.ContainsKey("data") ? r["data"] as object[] : null;
                    if (list != null)
                        foreach (var o in list.OfType<Dictionary<string, object>>())
                        {
                            // only a clear match by name (a near miss would show another game's art)
                            string n = o.ContainsKey("name") ? Convert.ToString(o["name"]) : "";
                            if (string.Equals(SimpleName(n), SimpleName(g.Name), StringComparison.OrdinalIgnoreCase) && o.ContainsKey("id")) { id = Convert.ToInt32(o["id"]); break; }
                        }
                }
                lock (animLock) sgdbIds[g.Id] = id;
            }
            if (id == 0 || sgdbRefused) return null;
            string what = g.Kind == "hero" ? "heroes" : "grids";
            string query = "?types=animated&mimes=image/png&nsfw=false&epilepsy=false" + (g.Kind == "hero" ? "" : "&dimensions=600x900,342x482,660x930");
            var res = SgdbGet(key, what + "/game/" + id + query) as Dictionary<string, object>;
            var items = res != null && res.ContainsKey("data") ? res["data"] as object[] : null;
            if (items == null) return null;
            foreach (var o in items.OfType<Dictionary<string, object>>().Take(3))
            {
                string url = o.ContainsKey("url") ? Convert.ToString(o["url"]) : "";
                string mime = o.ContainsKey("mime") ? Convert.ToString(o["mime"]) : "";
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || (mime.Length > 0 && mime != "image/png")) continue;
                byte[] b;
                using (var web = new WebClient())
                {
                    web.Headers[HttpRequestHeader.UserAgent] = "Kippix";
                    b = web.DownloadData(url);
                }
                if (b != null && b.Length < 40 * 1024 * 1024 && IsAnimated(b)) return b;
            }
            return null;
        }

        private static string SimpleName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        // =====================================================================
        // What was found (r14: no animated art means a living picture or the still picture, never the trailer: ThemeApi.LivingArt.cs)
        // =====================================================================

        private readonly HashSet<string> animLogged = new HashSet<string>();

        private bool AnimNoneFor(Guid id, string kind)
        {
            lock (animLock) return animNone.Contains(id + "|" + kind);
        }

        private string AnimNoneReason()
        {
            if (!HasSgdbKey) return "no animated art (no SteamGridDB key; nothing in Steam's library art)";
            if (sgdbRefused) return "no animated art (SteamGridDB refused the key)";
            return "no animated art (none on SteamGridDB or in Steam's library art)";
        }

        /// <summary>Once per game, place and source a session: "Kippix: animated art for X: cover = …".</summary>
        private void LogAnimSource(Game g, string what, string source)
        {
            if (g == null) return;
            string k = g.Id + "|" + what + "|" + (source ?? "");
            lock (animLogged) { if (!animLogged.Add(k)) return; if (animLogged.Count > 4000) animLogged.Clear(); }
            logger.Info("Kippix: animated art for " + (g.Name ?? "?") + ": " + what + " = " + (source ?? "?"));
        }

        // =====================================================================
        // Decoding: animated PNG and GIF into frames at display size
        // =====================================================================

        private static bool IsAnimated(byte[] d)
        {
            if (d == null || d.Length < 16) return false;
            if (d[0] == 'G' && d[1] == 'I' && d[2] == 'F') return true;   // (a still GIF turns out to be one frame: skipped when decoded)
            if (d[0] != 0x89 || d[1] != 'P' || d[2] != 'N' || d[3] != 'G') return false;
            int pos = 8;
            while (pos + 8 <= d.Length)
            {
                int len = BE32(d, pos);
                if (len < 0) return false;
                string type = Encoding.ASCII.GetString(d, pos + 4, 4);
                if (type == "acTL") return true;
                if (type == "IDAT" || type == "IEND") return false;
                pos += 12 + len;
            }
            return false;
        }

        /// <summary>Frames that fit in <paramref name="maxW"/> × <paramref name="maxH"/> pixels and about <paramref name="budget"/> bytes. Null if it doesn't move.</summary>
        internal static AnimFrames AnimDecode(byte[] d, int maxW, int maxH, long budget)
        {
            if (d == null || d.Length < 16) return null;
            if (d[0] == 'G' && d[1] == 'I' && d[2] == 'F') return DecodeGif(d, maxW, maxH, budget);
            if (d[0] == 0x89 && d[1] == 'P' && d[2] == 'N' && d[3] == 'G') return DecodeApng(d, maxW, maxH, budget);
            return null;
        }

        private static int BE32(byte[] d, int at) { return (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3]; }
        private static int BE16(byte[] d, int at) { return (d[at] << 8) | d[at + 1]; }

        private class ApngFrame
        {
            public int W, H, X, Y, DelayNum, DelayDen, Dispose, Blend;
            public readonly List<byte[]> Data = new List<byte[]>();
        }

        /// <summary>Builds and scales the output frames as the canvas changes (keeping every nth when there are too many).</summary>
        private sealed class FrameSink
        {
            private readonly AnimFrames f;
            private readonly int cw, ch, step;
            private readonly double scale;
            private int index;
            public FrameSink(AnimFrames f, int cw, int ch, int maxW, int maxH, long budget, int count)
            {
                this.f = f; this.cw = cw; this.ch = ch;
                scale = Math.Min(1.0, Math.Min((double)maxW / cw, (double)maxH / ch));
                f.Width = Math.Max(1, (int)Math.Round(cw * scale));
                f.Height = Math.Max(1, (int)Math.Round(ch * scale));
                long per = (long)f.Width * f.Height * 4;
                int maxFrames = (int)Math.Max(1, Math.Min(300, budget / Math.Max(1, per)));
                step = Math.Max(1, (int)Math.Ceiling((double)count / maxFrames));
            }
            public void Add(byte[] canvas, int delay)
            {
                bool keep = index % step == 0;
                index++;
                if (!keep && f.Delays.Count > 0) { f.Delays[f.Delays.Count - 1] += delay; return; }
                BitmapSource src = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Pbgra32, null, canvas, cw * 4);
                BitmapSource outp;
                if (scale < 0.999)
                {
                    var t = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                    outp = new WriteableBitmap(t);
                }
                else outp = src;
                outp.Freeze();
                f.Frames.Add(outp);
                f.Delays.Add(delay);
                f.Bytes += (long)outp.PixelWidth * outp.PixelHeight * 4;
            }
        }

        private static readonly uint[] CrcTable = MakeCrcTable();
        private static uint[] MakeCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
            s.Write(len, 0, 4);
            var t = Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);
            uint c = 0xFFFFFFFFu;
            foreach (byte b in t) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            c ^= 0xFFFFFFFFu;
            s.Write(new[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c }, 0, 4);
        }

        private static AnimFrames DecodeApng(byte[] d, int maxW, int maxH, long budget)
        {
            byte[] ihdr = null;
            var pre = new List<KeyValuePair<string, byte[]>>();   // PLTE, tRNS, gAMA... copied into every frame
            var frames = new List<ApngFrame>();
            ApngFrame cur = null;
            bool seenIdat = false, defaultIsFrame = false;
            int pos = 8;
            while (pos + 12 <= d.Length)
            {
                int len = BE32(d, pos);
                if (len < 0 || pos + 12 + len > d.Length) break;
                string type = Encoding.ASCII.GetString(d, pos + 4, 4);
                int at = pos + 8;
                switch (type)
                {
                    case "IHDR": ihdr = new byte[len]; Array.Copy(d, at, ihdr, 0, len); break;
                    case "fcTL":
                        if (len < 26) break;
                        cur = new ApngFrame
                        {
                            W = BE32(d, at + 4), H = BE32(d, at + 8), X = BE32(d, at + 12), Y = BE32(d, at + 16),
                            DelayNum = BE16(d, at + 20), DelayDen = BE16(d, at + 22), Dispose = d[at + 24], Blend = d[at + 25]
                        };
                        frames.Add(cur);
                        if (!seenIdat) defaultIsFrame = true;
                        break;
                    case "IDAT":
                        seenIdat = true;
                        if (defaultIsFrame && cur != null && frames.Count == 1) { var b = new byte[len]; Array.Copy(d, at, b, 0, len); cur.Data.Add(b); }
                        break;
                    case "fdAT":
                        if (cur != null && len > 4) { var b = new byte[len - 4]; Array.Copy(d, at + 4, b, 0, len - 4); cur.Data.Add(b); }
                        break;
                    case "IEND": pos = d.Length; continue;
                    case "PLTE": case "tRNS": case "gAMA": case "cHRM": case "sRGB": case "iCCP": case "sBIT":
                        if (!seenIdat) { var b = new byte[len]; Array.Copy(d, at, b, 0, len); pre.Add(new KeyValuePair<string, byte[]>(type, b)); }
                        break;
                }
                pos = at + len + 4;
            }
            frames.RemoveAll(fr => fr.Data.Count == 0);
            if (ihdr == null || ihdr.Length < 13 || frames.Count < 2) return null;
            int cw = BE32(ihdr, 0), ch = BE32(ihdr, 4);
            if (cw <= 0 || ch <= 0 || cw > 4096 || ch > 4096) return null;

            var result = new AnimFrames();
            var sink = new FrameSink(result, cw, ch, maxW, maxH, budget, frames.Count);
            var canvas = new byte[cw * ch * 4];
            for (int i = 0; i < frames.Count; i++)
            {
                var fr = frames[i];
                if (fr.W <= 0 || fr.H <= 0 || fr.X < 0 || fr.Y < 0 || fr.X + fr.W > cw || fr.Y + fr.H > ch) break;
                byte[] px;
                using (var ms = new MemoryStream())
                {
                    ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                    var h = (byte[])ihdr.Clone();
                    h[0] = (byte)(fr.W >> 24); h[1] = (byte)(fr.W >> 16); h[2] = (byte)(fr.W >> 8); h[3] = (byte)fr.W;
                    h[4] = (byte)(fr.H >> 24); h[5] = (byte)(fr.H >> 16); h[6] = (byte)(fr.H >> 8); h[7] = (byte)fr.H;
                    WriteChunk(ms, "IHDR", h);
                    foreach (var c in pre) WriteChunk(ms, c.Key, c.Value);
                    foreach (var part in fr.Data) WriteChunk(ms, "IDAT", part);
                    WriteChunk(ms, "IEND", new byte[0]);
                    ms.Position = 0;
                    var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Pbgra32, null, 0);
                    px = new byte[fr.W * fr.H * 4];
                    conv.CopyPixels(px, fr.W * 4, 0);
                }
                int dispose = fr.Dispose;
                if (i == 0 && dispose == 2) dispose = 1;   // (the spec: "previous" on the first frame means clear)
                byte[] saved = dispose == 2 ? CopyRegion(canvas, cw, fr.X, fr.Y, fr.W, fr.H) : null;
                Compose(canvas, cw, px, fr.X, fr.Y, fr.W, fr.H, fr.Blend == 1 && i > 0);
                int den = fr.DelayDen == 0 ? 100 : fr.DelayDen;
                int ms2 = (int)(fr.DelayNum * 1000L / den);
                if (ms2 <= 10) ms2 = 100;
                sink.Add(canvas, ms2);
                if (dispose == 1) ClearRegion(canvas, cw, fr.X, fr.Y, fr.W, fr.H);
                else if (dispose == 2 && saved != null) PasteRegion(canvas, cw, saved, fr.X, fr.Y, fr.W, fr.H);
            }
            return result.Frames.Count > 1 ? result : null;
        }

        private static AnimFrames DecodeGif(byte[] d, int maxW, int maxH, long budget)
        {
            var dec = new GifBitmapDecoder(new MemoryStream(d), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count < 2) return null;
            int cw = 0, ch = 0;
            try
            {
                var m = dec.Metadata;
                if (m != null) { cw = Convert.ToInt32(m.GetQuery("/logscrdesc/Width")); ch = Convert.ToInt32(m.GetQuery("/logscrdesc/Height")); }
            }
            catch (Exception) { }
            if (cw <= 0 || ch <= 0) { cw = dec.Frames[0].PixelWidth; ch = dec.Frames[0].PixelHeight; }
            if (cw <= 0 || ch <= 0 || cw > 4096 || ch > 4096) return null;
            var result = new AnimFrames();
            var sink = new FrameSink(result, cw, ch, maxW, maxH, budget, dec.Frames.Count);
            var canvas = new byte[cw * ch * 4];
            foreach (var frame in dec.Frames)
            {
                int x = 0, y = 0, delay = 100, disposal = 0;
                try
                {
                    var m = frame.Metadata as BitmapMetadata;
                    if (m != null)
                    {
                        x = Convert.ToInt32(m.GetQuery("/imgdesc/Left"));
                        y = Convert.ToInt32(m.GetQuery("/imgdesc/Top"));
                        if (m.ContainsQuery("/grctlext/Delay")) delay = Convert.ToInt32(m.GetQuery("/grctlext/Delay")) * 10;
                        if (m.ContainsQuery("/grctlext/Disposal")) disposal = Convert.ToInt32(m.GetQuery("/grctlext/Disposal"));
                    }
                }
                catch (Exception) { }
                if (delay <= 10) delay = 100;
                int w = Math.Min(frame.PixelWidth, cw - x), h = Math.Min(frame.PixelHeight, ch - y);
                if (w <= 0 || h <= 0) continue;
                var conv = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                var px = new byte[frame.PixelWidth * frame.PixelHeight * 4];
                conv.CopyPixels(px, frame.PixelWidth * 4, 0);
                if (w != frame.PixelWidth || h != frame.PixelHeight) px = CopyRegion(px, frame.PixelWidth, 0, 0, w, h);
                byte[] saved = disposal == 3 ? CopyRegion(canvas, cw, x, y, w, h) : null;
                Compose(canvas, cw, px, x, y, w, h, true);
                sink.Add(canvas, delay);
                if (disposal == 2) ClearRegion(canvas, cw, x, y, w, h);
                else if (disposal == 3 && saved != null) PasteRegion(canvas, cw, saved, x, y, w, h);
            }
            return result.Frames.Count > 1 ? result : null;
        }

        /// <summary>Premultiplied BGRA: copy (source) or "over" the canvas.</summary>
        private static void Compose(byte[] canvas, int cw, byte[] px, int x0, int y0, int w, int h, bool over)
        {
            for (int y = 0; y < h; y++)
            {
                int src = y * w * 4, dst = ((y0 + y) * cw + x0) * 4;
                if (!over) { Buffer.BlockCopy(px, src, canvas, dst, w * 4); continue; }
                for (int x = 0; x < w; x++, src += 4, dst += 4)
                {
                    int a = px[src + 3];
                    if (a == 0) continue;
                    if (a == 255) { canvas[dst] = px[src]; canvas[dst + 1] = px[src + 1]; canvas[dst + 2] = px[src + 2]; canvas[dst + 3] = 255; continue; }
                    int inv = 255 - a;
                    canvas[dst] = (byte)(px[src] + canvas[dst] * inv / 255);
                    canvas[dst + 1] = (byte)(px[src + 1] + canvas[dst + 1] * inv / 255);
                    canvas[dst + 2] = (byte)(px[src + 2] + canvas[dst + 2] * inv / 255);
                    canvas[dst + 3] = (byte)(a + canvas[dst + 3] * inv / 255);
                }
            }
        }

        private static byte[] CopyRegion(byte[] canvas, int cw, int x0, int y0, int w, int h)
        {
            var r = new byte[w * h * 4];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(canvas, ((y0 + y) * cw + x0) * 4, r, y * w * 4, w * 4);
            return r;
        }

        private static void PasteRegion(byte[] canvas, int cw, byte[] r, int x0, int y0, int w, int h)
        {
            for (int y = 0; y < h; y++) Buffer.BlockCopy(r, y * w * 4, canvas, ((y0 + y) * cw + x0) * 4, w * 4);
        }

        private static void ClearRegion(byte[] canvas, int cw, int x0, int y0, int w, int h)
        {
            for (int y = 0; y < h; y++) Array.Clear(canvas, ((y0 + y) * cw + x0) * 4, w * 4);
        }
    }
}
