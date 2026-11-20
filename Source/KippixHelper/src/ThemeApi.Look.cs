using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK.Events;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Look and feel: the accent colour taken from the selected game's art, a slow drift on the background,
    /// ambient mode (a slideshow with the clock after a while with nothing pressed), a play-time reminder,
    /// and button icons that match what you're using (Xbox, PlayStation, Switch, keyboard).
    /// </summary>
    public partial class ThemeApi
    {
        private void InitLook()
        {
            InitLookExtras();   // round 12: intro / outro, depth, night mode, animated art pages (ThemeApi.Intro.cs)
            CycleAccentStrengthCommand = new RelayCommand(_ => { settings.AccentStrength = (settings.AccentStrength + 1) % 3; SaveSettings(); PlaySound("move"); NotifyLook(); ApplyAccentFor(lastAccentPath, true); });
            CycleAmbientMinutesCommand = new RelayCommand(_ => { settings.AmbientMinutes = NextIn(new[] { 2, 5, 10, 15, 30 }, settings.AmbientMinutes); SaveSettings(); PlaySound("move"); NotifyLook(); });
            CycleReminderCommand = new RelayCommand(_ => { settings.SessionReminderMinutes = NextIn(new[] { 30, 60, 90, 120, 180 }, settings.SessionReminderMinutes); SaveSettings(); PlaySound("move"); NotifyLook(); });
            CycleButtonIconsCommand = new RelayCommand(_ => { settings.ButtonIcons = (settings.ButtonIcons + 1) % 5; SaveSettings(); PlaySound("move"); NotifyLook(); ApplyPrompts(true); });
            CycleSlideSecondsCommand = new RelayCommand(_ => { settings.GallerySlideSeconds = NextIn(new[] { 3, 6, 10, 15 }, settings.GallerySlideSeconds); SaveSettings(); PlaySound("move"); NotifyLook(); });
            StartAmbientCommand = new RelayCommand(_ => StartAmbient());   // (r15: the settings stay open underneath: any button comes back to this row)
            CycleMotionSpeedCommand = new RelayCommand(_ => { settings.MotionSpeed = settings.MotionSpeed == 3 ? 0 : settings.MotionSpeed == 2 ? 3 : settings.MotionSpeed + 1; /* very slow, slow, normal, fast */ SaveSettings(); PlaySound("move"); NotifyLook(); ApplyBackgroundMotion(); });
            CycleMotionAmountCommand = new RelayCommand(_ => { settings.MotionAmount = (settings.MotionAmount + 1) % 3; SaveSettings(); PlaySound("move"); NotifyLook(); ApplyBackgroundMotion(); });
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnAnyKeyForLook), true);
            EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnMouseForLook), true);
            EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) => { lastInput = DateTime.Now; if (ambientOn) { StopAmbient(); e.Handled = true; } }), true);
            // r15: the controller's A / B / X / Y / LB / RB / Start… never reach PreviewKeyDown (only KeyDown at the highlighted
            // element), so they wake the screensaver here: registered before the theme's own early pad handler (InitPadInput)
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.KeyDownEvent, new KeyEventHandler(OnPadForAmbient), true);
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.KeyUpEvent, new KeyEventHandler(OnPadUpForAmbient), true);
        }

        private KeyEventArgs padSeenBeforeAmbient;   // the last controller press seen with the screensaver off (the A on "Try it now" still bubbling up)
        private ControllerInput ambientWakeButton = ControllerInput.None;
        private DateTime ambientWakeAt;

        /// <summary>A controller button while the screensaver is up: it goes, and the press does nothing else.</summary>
        private void OnPadForAmbient(object sender, KeyEventArgs e)
        {
            try
            {
                var button = PadButton(e);
                if (button == ControllerInput.None) return;
                if (!ambientOn) { padSeenBeforeAmbient = e; return; }
                if (ReferenceEquals(e, padSeenBeforeAmbient) || IsPadRelease(e) || !lookFullscreen || !InMainWindow(sender)) return;
                e.Handled = true;
                lastPadEvent = e;   // (the theme's early pad handler and the window's key handler leave it alone: B doesn't go back, A doesn't press)
                padHandled = e;
                ambientWakeButton = button;
                ambientWakeAt = DateTime.Now;
                lastInput = DateTime.Now;
                StopAmbient();
            }
            catch (Exception) { }
        }

        /// <summary>Letting go of the button that woke the screensaver: nothing else.</summary>
        private void OnPadUpForAmbient(object sender, KeyEventArgs e)
        {
            try
            {
                if (ambientWakeButton == ControllerInput.None) return;
                if (PadButton(e) != ambientWakeButton) return;
                ambientWakeButton = ControllerInput.None;
                if ((DateTime.Now - ambientWakeAt).TotalSeconds > 5) return;
                e.Handled = true;
                lastPadEvent = e;
            }
            catch (Exception) { }
        }

        private static int NextIn(int[] list, int now)
        {
            int i = Array.IndexOf(list, now);
            return list[(i + 1) % list.Length];
        }

        public bool IsOptionsLook { get { return optionsPage == "look"; } }
        public ICommand CycleAccentStrengthCommand { get; private set; }
        public ICommand CycleAmbientMinutesCommand { get; private set; }
        public ICommand CycleReminderCommand { get; private set; }
        public ICommand CycleButtonIconsCommand { get; private set; }
        public ICommand CycleSlideSecondsCommand { get; private set; }
        public ICommand StartAmbientCommand { get; private set; }

        public bool AccentFromGame
        {
            get { return settings.AccentFromGame; }
            set { if (settings.AccentFromGame == value) return; settings.AccentFromGame = value; SaveSettings(); NotifyLook(); if (value) { if (FolderBgBlack) FolderAccentFollow(); else ApplyAccentFor(lastAccentPath, true); } else ClearAccent(); }
        }
        public bool BackgroundMotion
        {
            get { return settings.BackgroundMotion; }
            set { if (settings.BackgroundMotion == value) return; settings.BackgroundMotion = value; SaveSettings(); NotifyLook(); ApplyBackgroundMotion(); }
        }
        public bool AmbientMode
        {
            get { return settings.AmbientMode; }
            set { if (settings.AmbientMode == value) return; settings.AmbientMode = value; SaveSettings(); NotifyLook(); }
        }
        public bool SessionReminder
        {
            get { return settings.SessionReminder; }
            set { if (settings.SessionReminder == value) return; settings.SessionReminder = value; SaveSettings(); NotifyLook(); }
        }
        public string AccentStrengthText { get { return settings.AccentStrength == 0 ? Loc("LOC_KX_Subtle", "Subtle") : settings.AccentStrength == 2 ? Loc("LOC_KX_Strong", "Strong") : Loc("LOC_KX_Normal", "Normal"); } }
        public string AmbientMinutesText { get { return string.Format(Loc("LOC_KX_AfterMinutes", "After {0} minutes"), settings.AmbientMinutes); } }
        public string ReminderText { get { return string.Format(Loc("LOC_KX_EveryMinutes", "Every {0}"), PlayLength(settings.SessionReminderMinutes)); } }
        public string SlideSecondsText { get { return string.Format(Loc("LOC_KX_Seconds", "{0} seconds"), settings.GallerySlideSeconds); } }
        public string ButtonIconsText
        {
            get
            {
                switch (settings.ButtonIcons)
                {
                    case 1: return "Xbox";
                    case 2: return "PlayStation";
                    case 3: return "Nintendo Switch";
                    case 4: return Loc("LOC_KX_IconsKeyboard", "Keyboard");
                }
                return Loc("LOC_KX_IconsAuto", "Automatic") + " (" + PromptKindName(promptKind) + ")";
            }
        }

        private string PlayLength(int minutes)
        {
            if (minutes < 60) return string.Format(Loc("LOC_KX_NMinutes", "{0} minutes"), minutes);
            if (minutes % 60 == 0) return minutes == 60 ? Loc("LOC_KX_OneHour", "hour") : string.Format(Loc("LOC_KX_NHours", "{0} hours"), minutes / 60);
            return string.Format(Loc("LOC_KX_HoursMinutes", "{0} h {1} min"), minutes / 60, minutes % 60);
        }

        private void NotifyLook()
        {
            foreach (string n in new[] { "AccentFromGame", "BackgroundMotion", "AmbientMode", "SessionReminder", "AccentStrengthText", "AmbientMinutesText",
                                         "ReminderText", "ButtonIconsText", "SlideSecondsText", "LookSummary", "MotionSpeedText", "MotionAmountText" })
                Notify(n);
        }

        public string LookSummary
        {
            get
            {
                // what's on the page (two lines at most), so you know where to look (the screensaver is the page's own name for ambient mode)
                // (r14: motion, depth and animated art moved to Home settings › Background)
                return Loc("LOC_KX_LookContents2", "Look packs, Theme Studio, colours, screensaver, intro and outro, night mode, button icons…");
            }
        }

        // =====================================================================
        // Accent from the selected game
        // =====================================================================

        private string lastAccentPath;
        private int accentGen;

        /// <summary>The picture the accent is taken from: the cover (the card's own colours, 0.81), else the background.</summary>
        private string AccentPathOf(Game game)
        {
            try
            {
                if (game != null && !string.IsNullOrEmpty(game.CoverImage) && !game.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return api.Database.GetFullFilePath(game.CoverImage);
                if (game != null && !string.IsNullOrEmpty(game.BackgroundImage) && !game.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return api.Database.GetFullFilePath(game.BackgroundImage);
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The selected game changed (home screen and game page).</summary>
        private void LookGameSelected(Game game)
        {
            string path = AccentPathOf(game);
            lastAccentPath = path;
            if (settings.AccentFromGame) { ApplyAccentFor(path, false); AccentWarmAround(); }
            // the zoom couldn't start yet (the background wasn't there) or the background was rebuilt: start it now
            if (settings.BackgroundMotion && !gameRunningNow && (motionClock == null || motionTarget == null || !ReferenceEquals(motionTarget.RenderTransform, motionScale))) ApplyBackgroundMotion();
        }

        // 0.81: each picture's colour is worked out once (kept for the session), and the cards either side of the highlighted
        // one are worked out ahead while you browse, so the glow changes the moment a card is highlighted
        private readonly Dictionary<string, Color?> accentColours = new Dictionary<string, Color?>(StringComparer.OrdinalIgnoreCase);
        private int accentWarmGen;

        private bool AccentCached(string path, out Color? c)
        {
            c = null;
            if (string.IsNullOrEmpty(path)) return true;
            lock (accentColours) return accentColours.TryGetValue(path, out c);
        }

        private Color? AccentColourOf(string path)
        {
            Color? c;
            if (AccentCached(path, out c)) return c;
            try { c = DominantColour(path); } catch (Exception) { c = null; }
            lock (accentColours) { if (accentColours.Count > 1500) accentColours.Clear(); accentColours[path] = c; }
            return c;
        }

        // Putting a colour on costs about 40 ms on the screen's thread (everything drawn with the accent hears of it), and on the
        // home screen that was in every move, before the highlight could be drawn on the next game. A game highlighted in
        // passing doesn't get its colour now: the colour goes on once the highlight has rested on a game.
        private const int AccentRestMs = 160;
        private DispatcherTimer accentRest;
        private string accentRestPath;
        private int accentRestGen;

        private void ApplyAccentFor(string path, bool now)
        {
            if (!settings.AccentFromGame || ui == null) return;
            if (!now && IsFullscreen && !detailsOpen)
            {
                accentRestPath = path;
                accentRestGen = ++accentGen;   // (a colour still being worked out for the game before isn't put on)
                if (accentRest == null)
                {
                    accentRest = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(AccentRestMs) };
                    accentRest.Tick += (s, e) =>
                    {
                        accentRest.Stop();
                        if (accentRestGen == accentGen) ApplyAccentFor(accentRestPath, true);   // (unless something else set or cleared the colour meanwhile)
                    };
                }
                accentRest.Stop();
                accentRest.Start();
                return;
            }
            if (accentRest != null) accentRest.Stop();
            int gen = ++accentGen;
            int strength = settings.AccentStrength;
            Color? known;
            if (AccentCached(path, out known))   // worked out already: straight away, no waiting for another thread
            {
                SetAccent(known.HasValue ? Tint(known.Value, strength) : LookBaseAccent());
                return;
            }
            KippixShared.SafeWork.Queue(_ =>
            {
                Color? c = AccentColourOf(path);
                OnUi(() => { if (gen == accentGen && settings.AccentFromGame) SetAccent(c.HasValue ? Tint(c.Value, strength) : LookBaseAccent()); });   // (no colour: the look pack's accent)
            });
        }

        /// <summary>The colours of the cards near the highlighted one, worked out ahead (small pictures, one at a time, stops when you move on).</summary>
        private void AccentWarmAround()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var list = win != null ? PreviewList(win) : null;
                if (list == null || list.SelectedItem == null) return;
                int i = list.Items.IndexOf(list.SelectedItem);
                if (i < 0) return;
                var paths = new List<string>();
                foreach (int d in new[] { 1, -1, 2, -2, 3, -3, 4, -4, 5, 6 })
                {
                    int j = i + d;
                    if (j < 0 || j >= list.Items.Count) continue;
                    string p = AccentPathOf(FolderEntryGame(list.Items[j]));
                    Color? dummy;
                    if (p != null && !AccentCached(p, out dummy)) paths.Add(p);
                }
                AccentWarm(paths);
            }
            catch (Exception) { }
        }

        /// <summary>Works out these pictures' colours in the background, one at a time (stops when asked for others).</summary>
        private void AccentWarm(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return;
            int gen = ++accentWarmGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                foreach (var p in paths)
                {
                    if (gen != accentWarmGen) return;
                    AccentColourOf(p);
                }
            });
        }

        /// <summary>The most colourful hue in the picture (ignoring greys, very dark and blown-out bits).</summary>
        private static Color? DominantColour(string path)
        {
            var src = Images.LoadUncached(path, 64) as BitmapSource;   // (0.82: not kept in the shared picture cache: working colours out ahead would push the covers out of it)
            if (src == null) return null;
            var bmp = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            var weight = new double[36];
            var sumR = new double[36]; var sumG = new double[36]; var sumB = new double[36];
            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i] / 255.0, g = px[i + 1] / 255.0, r = px[i + 2] / 255.0;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                double sat = max <= 0 ? 0 : (max - min) / max;
                if (max < 0.18 || sat < 0.25) continue;
                double hue;
                double d = max - min;
                if (d <= 0) continue;
                if (max == r) hue = 60 * (((g - b) / d) % 6);
                else if (max == g) hue = 60 * (((b - r) / d) + 2);
                else hue = 60 * (((r - g) / d) + 4);
                if (hue < 0) hue += 360;
                int bucket = (int)(hue / 10) % 36;
                double wgt = sat * max;
                weight[bucket] += wgt; sumR[bucket] += r * wgt; sumG[bucket] += g * wgt; sumB[bucket] += b * wgt;
            }
            int best = -1;
            double bestW = 0;
            for (int k = 0; k < 36; k++)
            {
                double around = weight[k] + 0.5 * (weight[(k + 35) % 36] + weight[(k + 1) % 36]);
                if (around > bestW) { bestW = around; best = k; }
            }
            if (best < 0 || weight[best] <= 0 || bestW < w * h * 0.01) return null;
            return Color.FromRgb((byte)(255 * sumR[best] / weight[best]), (byte)(255 * sumG[best] / weight[best]), (byte)(255 * sumB[best] / weight[best]));
        }

        /// <summary>Light enough to read on (the theme's highlights are pale), and how much of the colour shows.</summary>
        private static Color Tint(Color c, int strength)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            if (max > 0) { r /= max; g /= max; b /= max; }   // full brightness
            double mix = strength == 0 ? 0.3 : strength == 2 ? 0.8 : 0.55;
            Func<double, byte> m = v => (byte)Math.Round(255 * (1 - mix + mix * v));
            return Color.FromRgb(m(r), m(g), m(b));
        }

        /// <summary>
        /// The accent: a new brush in the theme's resources each time (brushes kept in resources get frozen, so one can't
        /// be animated; swapping it updates every highlight that uses it). A short fade is done in a few steps.
        /// </summary>
        private DispatcherTimer accentFade;
        private Color accentNow = Colors.White;

        private void SetAccent(Color c)
        {
            try
            {
                if (accentFade != null) accentFade.Stop();
                if (accentNow == c) return;   // same colour (e.g. the same game again): nothing to swap
                PutAccent(c);   // (0.81: straight away; it used to fade over a third of a second)
            }
            catch (Exception ex) { logger.Warn("Kippix: accent: " + ex.Message); }
        }

        private void PutAccent(Color c)
        {
            accentNow = c;
            LivePut("KX_AccentBrush", c, ToHex(c));   // (0.82: recoloured in place, not swapped: ThemeApi.LookPacks.cs)
            LookAccentFollow(true);    // what the look sets to follow the accent (card edge, glow, Accent highlight): ThemeApi.LookPacks.cs (0.81: at once, not 90 ms later)
        }

        /// <summary>Accent off: back to the theme's own (white).</summary>
        private void ClearAccent()
        {
            try
            {
                accentGen++;
                if (accentFade != null) accentFade.Stop();
                PutAccent(LookBaseAccent());   // the look pack's accent (white for Kippix's own)
            }
            catch (Exception) { }
        }

        // =====================================================================
        // Gentle background motion
        // =====================================================================

        private ScaleTransform motionScale;
        private AnimationClock motionClock;
        private FrameworkElement motionTarget;
        private string motionSig;
        private bool motionPaused;
        private string motionWhy;

        private static FrameworkElement FindByTypeName(DependencyObject root, string typeName)
        {
            var queue = new System.Collections.Generic.Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var d = queue.Dequeue();
                if (d.GetType().Name == typeName && d is FrameworkElement) return (FrameworkElement)d;
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
            return null;
        }

        private static readonly double[] MotionZoom = { 1.06, 1.10, 1.16 };      // how far it zooms in (a touch / a little / more)
        private static readonly double[] MotionSeconds = { 24, 14, 8, 48 };      // each way (slow / normal / quicker / (0.96) very slow)

        /// <summary>
        /// Home background: a slow zoom in, then back out, over and over (Look and feel). Paused while a trailer plays over it
        /// or in a tile, and while a game runs.
        /// </summary>
        private void ApplyBackgroundMotion()
        {
            if (!IsFullscreen) return;   // Playnite's desktop mode has its own background (found by type below): never touch it
            // (called when a game starts / stops and when Reduce motion changes: Depth, live backgrounds and animated art follow too)
            try { ApplyDepth(); EnsureLiveTimer(); Notify("DepthShown"); } catch (Exception ex) { logger.Warn("Kippix: depth / live background: " + ex.Message); }
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var bg = HomeBgVisual() ?? (win != null ? UiTools.FindByName(win, "PART_ImageBackground") : null);   // (0.99.21: Kippix's own picture when it's on)
                if (bg == null && win != null) bg = FindByTypeName(win, "FadeImage");   // the theme's background by its kind
                bool on = settings.BackgroundMotion && !gameRunningNow && !settings.ReduceMotion;
                string why = bg == null ? "the background isn't on screen yet" : !settings.BackgroundMotion ? "" : gameRunningNow ? "a game is running" : null;
                if (why != null && why.Length > 0 && why != motionWhy) logger.Info("Kippix: background zoom waiting: " + why);
                motionWhy = why;
                if (bg == null) return;
                if (!on)
                {
                    if (motionClock != null) { motionClock.Controller.Stop(); motionClock = null; }
                    if (motionScale != null) { motionScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null); motionScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null); }
                    motionScale = null;
                    motionSig = null;
                    if (!LivingOwnsBg(bg)) bg.RenderTransform = Transform.Identity;   // (r14: the living picture's drift is left alone: ThemeApi.LivingArt.cs)
                    BcMotion(win, null);
                    return;
                }
                string sig = settings.MotionSpeed + "|" + settings.MotionAmount;
                if (ReferenceEquals(bg, motionTarget) && motionScale != null && ReferenceEquals(bg.RenderTransform, motionScale) && sig == motionSig && motionClock != null)
                {
                    SetMotionPaused(motionPaused);
                    BcMotion(win, motionClock);
                    return;
                }
                if (!ReferenceEquals(bg, motionTarget))
                {
                    motionTarget = bg;
                    bg.Loaded += (s, e) => ApplyBackgroundMotion();   // the background was rebuilt: start again on the new one
                }
                if (motionClock != null) motionClock.Controller.Stop();
                motionSig = sig;
                double from = motionScale != null ? motionScale.ScaleX : 1.0;
                motionScale = new ScaleTransform(from, from);
                bg.RenderTransformOrigin = new Point(0.5, 0.5);
                bg.RenderTransform = motionScale;
                double to = MotionZoom[Math.Max(0, Math.Min(2, settings.MotionAmount))];
                double secs = MotionSeconds[Math.Max(0, Math.Min(3, settings.MotionSpeed))];
                var zoom = new DoubleAnimation(1.0, to, new Duration(TimeSpan.FromSeconds(secs)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                Timeline.SetDesiredFrameRate(zoom, 30);
                motionClock = zoom.CreateClock();
                motionScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, motionClock);
                motionScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, motionClock);
                if (motionPaused) motionClock.Controller.Pause();
                BcMotion(win, motionClock);
                logger.Info("Kippix: background zoom on (" + Math.Round(bg.ActualWidth) + "×" + Math.Round(bg.ActualHeight) + ", to " + to + " over " + secs + " s" + (motionPaused ? ", paused for a trailer" : "") + ")");
            }
            catch (Exception ex) { logger.Warn("Kippix: background motion: " + ex.Message); }
        }

        private AnimationClock bcMotionClock;

        /// <summary>(0.99) BackgroundChanger's background (over the home screen's own) zooms with it, on the same clock.</summary>
        private void BcMotion(Window win, AnimationClock clock)
        {
            try
            {
                var layers = win != null ? UiTools.FindByName(win, "KX_BgLayers") : null;
                var bc = layers != null ? UiTools.FindByName(layers, "BackgroundChanger_PluginBackgroundImage") : null;
                if (bc == null) return;
                if (clock == null) { bc.RenderTransform = Transform.Identity; bcMotionClock = null; return; }
                if (ReferenceEquals(clock, bcMotionClock) && bc.RenderTransform is ScaleTransform) return;
                var st = new ScaleTransform(1, 1);
                bc.RenderTransformOrigin = new Point(0.5, 0.5);
                bc.RenderTransform = st;
                st.ApplyAnimationClock(ScaleTransform.ScaleXProperty, clock);
                st.ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock);
                bcMotionClock = clock;
            }
            catch (Exception ex) { logger.Warn("Kippix: BackgroundChanger zoom: " + ex.Message); }
        }

        /// <summary>A trailer plays over the background or in a tile: the zoom holds still until it's gone.</summary>
        private void SetMotionPaused(bool pause)
        {
            motionPaused = pause;
            try
            {
                if (motionClock == null) return;
                if (pause && !motionClock.IsPaused) motionClock.Controller.Pause();
                else if (!pause && motionClock.IsPaused) motionClock.Controller.Resume();
            }
            catch (Exception) { }
        }

        public ICommand CycleMotionSpeedCommand { get; private set; }
        public ICommand CycleMotionAmountCommand { get; private set; }
        public string MotionSpeedText { get { return settings.MotionSpeed == 3 ? Loc("LOC_KX_VerySlow", "Very slow") : settings.MotionSpeed == 0 ? Loc("LOC_KX_Slow", "Slow") : settings.MotionSpeed == 2 ? Loc("LOC_KX_Fast", "Fast") : Loc("LOC_KX_Normal", "Normal"); } }
        public string MotionAmountText { get { return settings.MotionAmount == 0 ? Loc("LOC_KX_Subtle", "Subtle") : settings.MotionAmount == 2 ? Loc("LOC_KX_Strong", "Strong") : Loc("LOC_KX_Medium", "Medium"); } }

        // =====================================================================
        // Ambient mode
        // =====================================================================

        private DateTime lastInput = DateTime.Now;
        private bool? lookFullscreenCache;
        private bool lookFullscreen { get { if (!lookFullscreenCache.HasValue && api != null) lookFullscreenCache = IsFullscreen; return lookFullscreenCache ?? false; } }
        private bool ambientOn;
        private DispatcherTimer ambientTimer;
        private List<string> ambientFiles;
        private int ambientAt;
        // r15: the picture's file path, not a decoded ImageSource: the screensaver shows it with Playnite's FadeImage, which only
        // loads a string (a path) or Playnite's own BitmapLoadProperties and shows nothing for anything else (why it stayed black)
        private string ambientImage;
        private WeakReference ambientReturnFocus;    // the highlight when it started ("Try it now" in the settings): back there when it ends
        public bool IsAmbientOn { get { return ambientOn; } }
        public string AmbientImage { get { return ambientImage; } }
        public string AmbientCaption { get; private set; }
        private Point lookMouse;

        private DateTime lastPadUse = DateTime.MinValue;
        private DateTime mouseRunStart = DateTime.MinValue;
        private double mouseRun;
        private bool lookMouseSet;

        private void OnMouseForLook(object sender, MouseEventArgs e)
        {
            try
            {
                if (!lookFullscreen) return;
                var w = sender as Window;
                if (w == null) return;
                var p = e.GetPosition(w);
                double d = Math.Abs(p.X - lookMouse.X) + Math.Abs(p.Y - lookMouse.Y);
                lookMouse = p;
                if (!lookMouseSet) { lookMouseSet = true; return; }   // the first position isn't a move
                d = Math.Min(d, 40);   // a jump (the pointer put somewhere, a window appearing) isn't a deliberate move
                // WPF also sends "moves" when things change under a still pointer: only a real move counts
                if (d < 3) return;
                lastInput = DateTime.Now;
                if (ambientOn) StopAmbient();
                // keyboard / mouse icons only for a deliberate move (a good stretch within a second), and not while the controller's in use
                if ((DateTime.Now - mouseRunStart).TotalSeconds > 1) { mouseRunStart = DateTime.Now; mouseRun = 0; }
                mouseRun += d;
                if (mouseRun > 60 && (DateTime.Now - lastPadUse).TotalSeconds > 3 && !SteamPadConnected()) PromptDevice(4);   // (the Steam Controller's trackpads move the mouse)
            }
            catch (Exception) { }
        }

        private void OnAnyKeyForLook(object sender, KeyEventArgs e)
        {
            try
            {
                if (!lookFullscreen) return;
                if (introOn && IsMainWindow(sender))
                {
                    e.Handled = true;   // any key skips the intro / outro, and does nothing else
                    SkipIntro();
                    return;
                }
                lastInput = DateTime.Now;
                if (ambientOn && IsMainWindow(sender))
                {
                    StopAmbient();
                    e.Handled = true;   // the button that woke it does nothing else
                    return;
                }
                if (IsMainWindow(sender))
                {
                    if (LookExtraKey(e)) { e.Handled = true; return; }   // Left / Right on this round's amount rows
                    DepthNudge(e.Key);   // Background › Depth: the background answers the move
                }
                bool pad = IsPadKey(e);
                if (pad) lastPadUse = DateTime.Now;
                PromptDevice(pad ? 0 : 4);
            }
            catch (Exception) { }
        }

        private static readonly Key[] NavKeys = { Key.Up, Key.Down, Key.Left, Key.Right, Key.Enter, Key.Escape, Key.Back, Key.PageUp, Key.PageDown, Key.Space, Key.Tab, Key.F1, Key.F2, Key.F3, Key.Apps };

        /// <summary>
        /// Was this key press really the controller? Playnite (and Steam) turn controller presses into ordinary keys too,
        /// so: a controller event, or a button held on an Xbox-style pad right now, or (with a PlayStation / Switch pad that
        /// isn't read through XInput) a navigation key. Anything else is the keyboard.
        /// </summary>
        private bool IsPadKey(KeyEventArgs e)
        {
            if (e.GetType().Name == "GameControllerInputEventArgs") return true;
            // a Steam Controller in use: Steam's keys for its buttons (whatever they are) are still the controller
            if (ControllerUsedWithin(3) && SteamPadConnected()) return true;
            bool anyXInput = false;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var st = new KippixOverlay.XInput.State();
                    if (KippixOverlay.XInput.GetState(i, ref st) != 0) continue;
                    anyXInput = true;
                    var g = st.Gamepad;
                    if (g.wButtons != 0 || g.bLeftTrigger > 60 || g.bRightTrigger > 60
                        || Math.Abs((int)g.sThumbLX) > 16000 || Math.Abs((int)g.sThumbLY) > 16000) return true;
                }
            }
            catch (Exception) { }
            if (NavKeys.Contains(e.Key) && SteamPadConnected()) return true;   // the Steam Controller, as Steam's keyboard
            if (anyXInput) return (DateTime.Now - lastPadUse).TotalMilliseconds < 400 && NavKeys.Contains(e.Key);   // a key repeat from a held button
            return padKind != 1 && NavKeys.Contains(e.Key);
        }

        /// <summary>From the 2 second timer: time for ambient mode?</summary>
        private void AmbientTick()
        {
            if (!settings.AmbientMode || ambientOn || !IsFullscreen) return;
            if (openPanel != null || detailsOpen || gameRunningNow) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            if (w == null || !w.IsActive) return;
            if ((DateTime.Now - lastInput).TotalMinutes < settings.AmbientMinutes) return;
            StartAmbient();
        }

        private void StartAmbient()
        {
            if (ambientOn) return;
            ambientOn = true;
            try { var f = Keyboard.FocusedElement as UIElement; ambientReturnFocus = f != null ? new WeakReference(f) : null; } catch (Exception) { ambientReturnFocus = null; }
            Notify("IsAmbientOn");
            HideUnderAmbient();
            if (settings.ReduceMotion) ui.BeginInvoke(new Action(StillAmbient), DispatcherPriority.Loaded);
            KippixShared.SafeWork.Queue(_ =>
            {
                var files = new List<string>();
                try
                {
                    foreach (var g in api.Database.Games.Where(g => !g.Hidden && !string.IsNullOrEmpty(g.BackgroundImage) && !g.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
                    {
                        string p = SafeFullPath(g.BackgroundImage);
                        if (!string.IsNullOrEmpty(p)) files.Add(p + "|" + g.Id + "|" + g.Name);
                    }
                    foreach (var f in galleryFiles.Where(f => !f.IsVideo).OrderByDescending(f => f.Date).Take(150))
                        files.Add(f.Path + "|" + f.GameId + "|" + (f.GameName ?? ""));
                }
                catch (Exception) { }
                var rnd = new Random();
                files = files.OrderBy(x => rnd.Next()).ToList();
                OnUi(() =>
                {
                    if (!ambientOn) return;   // (woken before the list was ready)
                    logger.Info("Kippix: screensaver on: " + files.Count + " pictures (game backgrounds and screenshots)");
                    ambientFiles = files;
                    ambientAt = -1;
                    NextAmbient();
                    if (ambientTimer == null)
                    {
                        ambientTimer = new DispatcherTimer();
                        ambientTimer.Tick += (s, e) => NextAmbient();
                    }
                    ambientTimer.Interval = TimeSpan.FromSeconds(Math.Max(4, settings.AmbientSeconds));
                    ambientTimer.Start();
                });
            });
        }

        private void NextAmbient()
        {
            if (!ambientOn || ambientFiles == null || ambientFiles.Count == 0) return;
            var files = ambientFiles;
            int from = ambientAt;
            KippixShared.SafeWork.Queue(_ =>
            {
                // the next picture that's really on disk (a game's background can point at a file that's gone); the FadeImage
                // decodes it off the UI thread and fades it in
                string path = null, caption = "";
                ImageSource logo = null;
                int at = from;
                for (int tries = 0; tries < Math.Min(25, files.Count); tries++)
                {
                    at = (at + 1) % files.Count;
                    string entry = files[at];
                    int bar = entry.IndexOf('|');   // (a path can't hold "|"; a game's name can)
                    string p = bar >= 0 ? entry.Substring(0, bar) : entry;
                    bool there = false;
                    try { there = File.Exists(p); } catch (Exception) { }
                    if (!there) continue;
                    path = p;
                    // "path|game id|name" (the game's logo from Extra Metadata, when it has one)
                    string rest = bar >= 0 ? entry.Substring(bar + 1) : "";
                    int bar2 = rest.IndexOf('|');
                    Guid gid = Guid.Empty;
                    if (bar2 == 36 && Guid.TryParse(rest.Substring(0, 36), out gid)) rest = rest.Substring(37);
                    caption = rest;
                    if (gid != Guid.Empty)
                        try
                        {
                            string lf = Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", gid.ToString(), "Logo.png");
                            if (File.Exists(lf)) logo = Images.Load(lf, 700);
                        }
                        catch (Exception) { }
                    break;
                }
                OnUi(() =>
                {
                    if (!ambientOn || !ReferenceEquals(files, ambientFiles)) return;
                    ambientAt = at;
                    if (path == null) return;
                    ambientImage = path;
                    AmbientCaption = caption;
                    AmbientLogo = logo;
                    Notify("AmbientImage");
                    Notify("AmbientCaption");
                    Notify("AmbientLogo");
                    Notify("HasAmbientLogo");
                    Notify("HasAmbientCaption");
                });
            });
        }

        private void StopAmbient()
        {
            if (!ambientOn) return;
            ambientOn = false;
            if (ambientTimer != null) ambientTimer.Stop();
            ambientImage = null;
            ambientFiles = null;
            lastInput = DateTime.Now;
            RestoreUnderAmbient();
            Notify("IsAmbientOn");
            Notify("AmbientImage");
            // the highlight back where it was (the "Try it now" row in the settings, a card on the home screen), if it moved
            var back = ambientReturnFocus != null ? ambientReturnFocus.Target as UIElement : null;
            ambientReturnFocus = null;
            if (back != null && ui != null)
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (ambientOn || !back.IsVisible || !back.Focusable || back.IsKeyboardFocused) return;
                        var now = Keyboard.FocusedElement as DependencyObject;
                        if (now != null && !ReferenceEquals(Window.GetWindow(now), Window.GetWindow(back))) return;   // (another window has it: leave it)
                        Keyboard.Focus(back);
                    }
                    catch (Exception) { }
                }), DispatcherPriority.Input);
        }

        // WPF draws what is under the screensaver again for every frame of its drift, though none of it shows. It skips
        // anything at opacity 0, so the home screen and its background wait at 0 while the screensaver is up. They are not
        // Hidden: IsVisible stays true, so the highlight and whatever watches IsVisible (the carousel, the game page) stay put.
        private static readonly string[] AmbientCovers = { "KX_BgShift", "KX_Stage" };
        private readonly List<UIElement> ambientCovered = new List<UIElement>();

        private void HideUnderAmbient()
        {
            RestoreUnderAmbient();
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return;
            foreach (string name in AmbientCovers)
            {
                var el = UiTools.FindByName(win, name);
                if (el == null) continue;
                el.SetCurrentValue(UIElement.OpacityProperty, 0.0);   // (leaves KX_Stage's binding in place)
                ambientCovered.Add(el);
            }
        }

        private void RestoreUnderAmbient()
        {
            foreach (var el in ambientCovered) el.InvalidateProperty(UIElement.OpacityProperty);   // (back to its own value or binding)
            ambientCovered.Clear();
        }

        // =====================================================================
        // Play-time reminder (off unless you turn it on)
        // =====================================================================

        private DateTime sessionStart;
        private int remindersShown;

        private void ReminderGameRunning(bool running)
        {
            sessionStart = DateTime.Now;
            remindersShown = 0;
        }

        private void ReminderTick()
        {
            if (!settings.SessionReminder || !AnyRunning) return;
            int every = Math.Max(15, settings.SessionReminderMinutes);
            double minutes = (DateTime.Now - sessionStart).TotalMinutes;
            int due = (int)(minutes / every);
            if (due <= remindersShown) return;
            remindersShown = due;
            string title = string.Format(Loc("LOC_KX_ReminderTitle", "You've been playing for {0}"), PlayLength(due * every));
            string text = Loc("LOC_KX_ReminderText", "Maybe time for a stretch and a drink of water.");
            try
            {
                if (plugin.OverlayRunning) plugin.Overlay.Toast(title, text);
                else api.Notifications.Add(new Playnite.SDK.NotificationMessage("bbfs-reminder-" + due, title + ". " + text, Playnite.SDK.NotificationType.Info));
            }
            catch (Exception ex) { logger.Warn("Kippix: reminder: " + ex.Message); }
        }

        // =====================================================================
        // Button icons
        // =====================================================================

        private int promptKind = 1;           // what's showing: 1 Xbox, 2 PlayStation, 3 Switch, 4 keyboard
        private int lastDevice = -1;          // 0 controller, 4 keyboard / mouse
        private DateTime padCheckedAt = DateTime.MinValue;
        private int padKind = 1;
        private ResourceDictionary promptDict;

        private static string PromptKindName(int k)
        {
            return k == 2 ? "PlayStation" : k == 3 ? "Switch" : k == 4 ? "Keyboard" : "Xbox";
        }

        /// <summary>A key or button was used: in Automatic, switch the icons when the kind of device changes.</summary>
        private void PromptDevice(int device)
        {
            if (settings.ButtonIcons != 0) return;
            if (device == 0 && (DateTime.Now - padCheckedAt).TotalSeconds > 60)
            {
                padCheckedAt = DateTime.Now;
                KippixShared.SafeWork.Queue(_ =>
                {
                    int k = DetectPad();
                    OnUi(() => { if (k != padKind) { padKind = k; if (lastDevice == 0) PromptDevice(0); } });
                });
            }
            int want = device == 4 ? 4 : padKind;
            if (device == lastDevice && want == promptKind) return;
            lastDevice = device;
            if (want != promptKind) { promptKind = want; ApplyPrompts(false); Notify("ButtonIconsText"); }
        }

        private static bool PadConnected()
        {
            try
            {
                for (int i = 0; i < 4; i++) { var st = new KippixOverlay.XInput.State(); if (KippixOverlay.XInput.GetState(i, ref st) == 0) return true; }
            }
            catch (Exception) { }
            return false;
        }

        private static DateTime steamPadCheckedAt = DateTime.MinValue;
        private static bool steamPadSeen;

        /// <summary>
        /// A Valve controller (the Steam Controller) is connected. Windows only sees it through Steam, which (outside games)
        /// turns it into keyboard and mouse: so with one connected, arrow / Enter / Esc keys and mouse moves count as the
        /// controller (the button icons don't flip to the keyboard's, which also stops the screen redrawing on every press).
        /// Looked up at most every 15 seconds.
        /// </summary>
        private static bool SteamPadConnected()
        {
            if ((DateTime.Now - steamPadCheckedAt).TotalSeconds < 15) return steamPadSeen;
            steamPadCheckedAt = DateTime.Now;
            try { steamPadSeen = KippixOverlay.HidScanner.PathsMatching(p => p.ToLowerInvariant().Contains("vid_28de")).Count > 0; }
            catch (Exception) { steamPadSeen = false; }
            return steamPadSeen;
        }

        /// <summary>Which controller is plugged in: a Sony one, a Nintendo one, or anything else (Xbox and XInput pads).</summary>
        private static int DetectPad()
        {
            try
            {
                if (KippixOverlay.HidScanner.SonyPaths().Count > 0) return 2;   // DualShock 4 / DualSense (not other Sony gadgets)
                string[] nintendo = { "pid_2009", "pid_2006", "pid_2007", "pid_200e", "pid_2017", "pid_2019", "pid_201e" };   // Pro Controller, Joy-Cons, SNES / N64 pads
                if (KippixOverlay.HidScanner.PathsMatching(p => { string l = p.ToLowerInvariant(); return l.Contains("vid_057e") && nintendo.Any(l.Contains); }).Count > 0) return 3;
            }
            catch (Exception) { }
            return 1;
        }

        /// <summary>Puts the chosen icon set over Playnite's (the theme's hints use ButtonPromptA, ...B and so on).</summary>
        private void ApplyPrompts(bool fromSetting)
        {
            try
            {
                if (!IsFullscreen || Application.Current == null) return;
                int kind = settings.ButtonIcons == 0 ? promptKind : settings.ButtonIcons;
                if (settings.ButtonIcons == 0 && fromSetting) { padKind = DetectPad(); kind = promptKind = lastDevice == 4 && !PadConnected() ? 4 : padKind; }
                string file = null;
                switch (kind)
                {
                    case 2: file = Path.Combine(api.Paths.ApplicationPath, "Themes", "Fullscreen", "Default", "Images", "ButtonPrompts", "PlayStation", "PlayStation.xaml"); break;
                    case 3: file = Path.Combine(ThemeDir, "Prompts", "Switch.xml"); break;
                    case 4: file = Path.Combine(ThemeDir, "Prompts", "Keyboard.xml"); break;
                    default: file = Path.Combine(api.Paths.ApplicationPath, "Themes", "Fullscreen", "Default", "Images", "ButtonPrompts", "Xbox", "Xbox.xaml"); break;
                }
                var merged = Application.Current.Resources.MergedDictionaries;
                if (promptDict != null) { merged.Remove(promptDict); promptDict = null; }
                if (file == null || !File.Exists(file)) return;
                using (var fs = File.OpenRead(file))
                {
                    var ctx = new ParserContext { BaseUri = new Uri(file, UriKind.Absolute) };
                    var dict = XamlReader.Load(fs, ctx) as ResourceDictionary;
                    if (dict == null) return;
                    merged.Add(dict);
                    promptDict = dict;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: button icons: " + ex.Message); }
        }
    }
}
