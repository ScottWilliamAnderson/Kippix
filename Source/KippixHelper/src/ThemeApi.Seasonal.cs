using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Settings › Home settings › Seasonal touches: small effects drawn over the home screen's background (behind the games):
    /// snow in winter, pumpkins and bats at Halloween, lights at Christmas, fireworks at New Year, hearts for Valentine's,
    /// blossom in spring, leaves in autumn. All the art is simple shapes drawn here. A light timer runs only while
    /// the home screen shows one; it stops while a game runs, a trailer plays, or anything covers the home screen.
    /// </summary>
    public partial class ThemeApi
    {
        public bool IsOptionsSeasonal { get { return optionsPage == "seasonal"; } }
        private ObservableCollection<PickRow> seasonalRows;
        public ObservableCollection<PickRow> SeasonalRows { get { return seasonalRows ?? (seasonalRows = new ObservableCollection<PickRow>()); } }

        private static readonly string[] SeasonKeys = { "snow", "halloween", "christmas", "newyear", "valentine", "spring", "autumn" };

        private string SeasonName(string k)
        {
            if (k.StartsWith("wx:", StringComparison.Ordinal)) return WeatherFxName(k.Substring(3));
            switch (k)
            {
                case "snow": return Loc("LOC_KX_SeasonSnow", "Snow (winter)");
                case "halloween": return Loc("LOC_KX_SeasonHalloween", "Pumpkins and bats (Halloween)");
                case "christmas": return Loc("LOC_KX_SeasonChristmas", "Lights (Christmas)");
                case "newyear": return Loc("LOC_KX_SeasonNewYear", "Fireworks (New Year)");
                case "valentine": return Loc("LOC_KX_SeasonValentine", "Hearts (Valentine's Day)");
                case "spring": return Loc("LOC_KX_SeasonSpring", "Blossom (spring)");
                case "autumn": return Loc("LOC_KX_SeasonAutumn", "Falling leaves (autumn)");
            }
            return k;
        }

        private string SeasonDates(string k)
        {
            switch (k)
            {
                case "snow": return Loc("LOC_KX_SeasonSnowDates", "1 December to the end of February");
                case "halloween": return Loc("LOC_KX_SeasonHalloweenDates", "20 October to 1 November");
                case "christmas": return Loc("LOC_KX_SeasonChristmasDates", "1 to 30 December");
                case "newyear": return Loc("LOC_KX_SeasonNewYearDates", "31 December to 2 January");
                case "valentine": return Loc("LOC_KX_SeasonValentineDates", "10 to 14 February");
                case "spring": return Loc("LOC_KX_SeasonSpringDates", "20 March to the end of May");
                case "autumn": return Loc("LOC_KX_SeasonAutumnDates", "22 September to the end of November");
            }
            return "";
        }

        private bool SeasonOn(string k)
        {
            switch (k)
            {
                case "snow": return settings.SeasonSnow;
                case "halloween": return settings.SeasonHalloween;
                case "christmas": return settings.SeasonChristmas;
                case "newyear": return settings.SeasonNewYear;
                case "valentine": return settings.SeasonValentine;
                case "spring": return settings.SeasonSpring;
                case "autumn": return settings.SeasonAutumn;
            }
            return false;
        }

        private void SetSeasonOn(string k, bool v)
        {
            switch (k)
            {
                case "snow": settings.SeasonSnow = v; break;
                case "halloween": settings.SeasonHalloween = v; break;
                case "christmas": settings.SeasonChristmas = v; break;
                case "newyear": settings.SeasonNewYear = v; break;
                case "valentine": settings.SeasonValentine = v; break;
                case "spring": settings.SeasonSpring = v; break;
                case "autumn": settings.SeasonAutumn = v; break;
            }
        }

        private static bool InSeason(string k, DateTime d)
        {
            int m = d.Month, day = d.Day;
            switch (k)
            {
                case "snow": return m == 12 || m == 1 || m == 2;
                case "halloween": return (m == 10 && day >= 20) || (m == 11 && day <= 1);
                case "christmas": return m == 12 && day <= 30;
                case "newyear": return (m == 12 && day == 31) || (m == 1 && day <= 2);
                case "valentine": return m == 2 && day >= 10 && day <= 14;
                case "spring": return (m == 3 && day >= 20) || m == 4 || m == 5;
                case "autumn": return (m == 9 && day >= 22) || m == 10 || m == 11;
            }
            return false;
        }

        /// <summary>The effects showing now (switched on, and in season unless Always on).</summary>
        private List<string> ActiveSeasons()
        {
            var list = new List<string>();
            if (settings.SeasonalOn)
            {
                var today = DateTime.Today;
                foreach (string k in SeasonKeys)
                    if (SeasonOn(k) && (settings.SeasonalMode == 1 || InSeason(k, today))) list.Add(k);
                // Halloween's pumpkins take over from the autumn leaves for its few days
                if (list.Contains("halloween") && list.Contains("autumn") && settings.SeasonalMode == 0) list.Remove("autumn");
            }
            // Weather touches (their own switch)
            foreach (string wx in WeatherKindsNow()) list.Add("wx:" + wx);   // (0.82: set by hand, more than one can show)
            return list;
        }

        public bool SeasonalShown { get { return ActiveSeasons().Count > 0; } }

        public string SeasonalSummary
        {
            get
            {
                if (!settings.SeasonalOn) return Loc("LOC_KX_Off", "Off");
                var now = ActiveSeasons().Where(k => !k.StartsWith("wx:", StringComparison.Ordinal)).ToList();
                if (now.Count == 0) return Loc("LOC_KX_SeasonalNone", "On: nothing in season today");
                return string.Format(Loc("LOC_KX_SeasonalShowing", "Showing: {0}"), string.Join(", ", now.Select(SeasonName).ToArray()));
            }
        }

        // ---------------- the settings page ----------------

        private void BuildSeasonalRows()
        {
            var rows = SeasonalRows;
            rows.Clear();
            Action again = () => { BuildSeasonalRows(); };
            rows.Add(BoardSwitch("sun", "seasonal", Loc("LOC_KX_SeasonalOn", "Seasonal touches"), settings.SeasonalOn,
                                 SeasonalSummary, Loc("LOC_KX_SeasonalOff", "Off: the home screen as usual"),
                                 () => { settings.SeasonalOn = !settings.SeasonalOn; SeasonalChanged("seasonal"); }));
            if (!settings.SeasonalOn) return;
            rows.Add(new PickRow
            {
                Key = "seasonmode", Icon = "calendar",
                Text = settings.SeasonalMode == 0 ? Loc("LOC_KX_SeasonAuto", "When: automatic by date") : Loc("LOC_KX_SeasonAlways", "When: always on"),
                Sub = settings.SeasonalMode == 0 ? Loc("LOC_KX_SeasonAutoSub", "Each one shows in its own season (A: always on)") : Loc("LOC_KX_SeasonAlwaysSub", "Every one switched on below shows all year (A: automatic by date)"),
                Command = new RelayCommand(_ => { settings.SeasonalMode = 1 - settings.SeasonalMode; SeasonalChanged("seasonmode"); })
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SeasonEffects", "EFFECTS") });
            var today = DateTime.Today;
            foreach (string k in SeasonKeys)
            {
                string key = k;
                bool now = InSeason(k, today);
                string when = settings.SeasonalMode == 0 ? SeasonDates(k) + (now ? Loc("LOC_KX_SeasonNow", "  (now)") : "") : Loc("LOC_KX_On", "On");
                string icon = k == "snow" ? "cloud" : k == "halloween" ? "moon" : k == "valentine" ? "heart" : k == "spring" || k == "autumn" ? "sun" : "star";
                rows.Add(BoardSwitch(icon, "season:" + k, SeasonName(k), SeasonOn(k), when, Loc("LOC_KX_Off", "Off"),
                                     () => { SetSeasonOn(key, !SeasonOn(key)); SeasonalChanged("season:" + key); }));
            }
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "seasoninfo", Text = Loc("LOC_KX_SeasonInfo", "They pause while a game runs or a trailer plays") });
        }

        private void SeasonalChanged(string key)
        {
            SaveSettings();
            PlaySound("move");
            Notify("SeasonalShown");
            Notify("SeasonalSummary");
            Notify("WeatherFxSummary");
            seasonalBuiltFor = null;   // the effects again
            UpdateSeasonal();
            BoardRowsAgain(BuildSeasonalRows, key);
        }

        // ---------------- drawing ----------------

        private class Flake
        {
            public FrameworkElement El;
            public TranslateTransform Pos;
            public RotateTransform Turn;
            public string Kind;
            public double X, Y, Vx, Vy, Spin, Phase, Life, MaxLife, Size;
        }

        private readonly List<Flake> flakes = new List<Flake>();   // (1.0.2) the effects' own thread's, like everything below it
        private DispatcherTimer seasonTimer;
        private Canvas seasonCanvas;                                 // (1.0.2) the effects' own thread's canvas (inside KX_Seasonal)
        private string seasonalBuiltFor;                             // (the screen's thread: what the effects were last built for)
        private readonly Random seasonRnd = new Random();
        private DateTime seasonLast = DateTime.Now;
        private double fireworkWait;
        private double fxW, fxH;
        private string fxSig = "";                                   // (their thread's copy of what's showing)                                     // the effects' room (their thread's copy)

        // (1.0.2) The effects run on a thread of their own and are shown inside KX_Seasonal through a HostVisual, so they keep
        // moving smoothly while the screen's thread is busy with your presses (on the screen's thread they waited for every
        // move, and froze or jumped while you moved around). The screen's thread only decides what shows and when.
        private Thread fxThread;
        private Dispatcher fxUi;
        private FxHostElement fxHostEl;
        private Canvas fxHostCanvas;

        private class FxHostElement : FrameworkElement
        {
            public readonly HostVisual Host = new HostVisual();
            public FxHostElement() { AddVisualChild(Host); IsHitTestVisible = false; }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index) { return Host; }
        }

        /// <summary>The effects thread's end of the HostVisual: a presentation source so its tree is laid out and drawn.</summary>
        private class FxSource : PresentationSource
        {
            private readonly VisualTarget target;
            public FxSource(HostVisual host) { target = new VisualTarget(host); }
            public override Visual RootVisual
            {
                get { return target.RootVisual; }
                set
                {
                    Visual old = target.RootVisual;
                    target.RootVisual = value;
                    RootChanged(old, value);
                    var e = value as UIElement;
                    if (e != null) { e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); e.Arrange(new Rect(e.DesiredSize)); }
                }
            }
            protected override CompositionTarget GetCompositionTargetCore() { return target; }
            public override bool IsDisposed { get { return false; } }
        }

        /// <summary>Puts the effects' host into KX_Seasonal and starts their thread (the screen's thread).</summary>
        private bool FxAttach(Canvas host)
        {
            try
            {
                var el = new FxHostElement();
                host.Children.Clear();
                host.Children.Add(el);
                var ready = new ManualResetEvent(false);
                Dispatcher made = null;
                Canvas root = null;
                var th = new Thread(() =>
                {
                    try
                    {
                        made = Dispatcher.CurrentDispatcher;
                        // an error on the effects' thread would end Playnite: it's written down and the effects stop instead
                        made.UnhandledException += (s, e) => { e.Handled = true; try { StopSeasonTimer(); logger.Warn("Kippix: seasonal (its thread): " + e.Exception.Message); } catch (Exception) { } };
                        var src = new FxSource(el.Host);
                        root = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
                        src.RootVisual = root;
                    }
                    catch (Exception ex) { logger.Warn("Kippix: seasonal: its thread didn't start: " + ex.Message); made = null; }
                    ready.Set();
                    if (made != null) Dispatcher.Run();
                });
                th.SetApartmentState(ApartmentState.STA);
                th.IsBackground = true;
                th.Name = "Kippix effects";
                th.Start();
                ready.WaitOne(3000);
                if (made == null || root == null) { host.Children.Clear(); return false; }
                fxThread = th; fxUi = made; fxHostEl = el; fxHostCanvas = host;
                var hostRef = host;
                host.SizeChanged += (s, e) => { if (hostRef == fxHostCanvas) UpdateSeasonal(); };
                FxRun(() => { seasonCanvas = root; flakes.Clear(); });
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: seasonal: " + ex.Message); return false; }
        }

        /// <summary>The view was made again (a new KX_Seasonal): the old thread ends, its effects with it.</summary>
        private void FxDetach()
        {
            var d = fxUi;
            fxUi = null; fxThread = null; fxHostEl = null; fxHostCanvas = null;
            seasonalBuiltFor = null;
            if (d != null)
            {
                try
                {
                    // (only what's this old thread's: a new one may already have its own canvas and timer)
                    d.BeginInvoke(new Action(() =>
                    {
                        var me = Dispatcher.CurrentDispatcher;
                        if (seasonTimer != null && seasonTimer.Dispatcher == me) { seasonTimer.Stop(); seasonTimer = null; }
                        if (seasonCanvas != null && seasonCanvas.Dispatcher == me) { seasonCanvas = null; flakes.Clear(); stormFlash = null; }
                    }));
                    d.BeginInvokeShutdown(DispatcherPriority.Background);
                }
                catch (Exception) { }
            }
        }

        private void FxRun(Action a)
        {
            var d = fxUi;
            if (d == null) return;
            try { d.BeginInvoke(new Action(() => { try { a(); } catch (Exception ex) { StopSeasonTimer(); logger.Warn("Kippix: seasonal (its thread): " + ex.Message); } })); } catch (Exception) { }
        }

        /// <summary>Starts, stops or rebuilds the effects (from the 2-second tick and when settings change).</summary>
        private bool seasonShownWas;

        private void UpdateSeasonal()
        {
            try
            {
                if (!IsFullscreen) return;
                var active = ActiveSeasons();
                bool run = active.Count > 0 && !gameRunningNow && !previewPlaying && !detailsOpen && !IsAmbientOn && !batterySaving;   // (they carry on behind the menus)
                if (settings.WeatherFxOn && settings.WeatherFxSync) RefreshLiveWeatherSoon();
                // set days and hours (weather touches) come and go: the layer follows
                if ((active.Count > 0) != seasonShownWas) { seasonShownWas = active.Count > 0; Notify("SeasonalShown"); }
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win != null && win.WindowState == WindowState.Minimized) run = false;
                // the view was made again (a new canvas): forget the old one and what was on it
                if (fxHostCanvas != null && !fxHostCanvas.IsLoaded) FxDetach();
                if (fxHostCanvas == null)
                {
                    if (active.Count == 0 || win == null) return;   // (nothing to show: the thread isn't started)
                    var host = UiTools.FindByName(win, "KX_Seasonal") as Canvas;
                    if (host == null || !host.IsLoaded || !FxAttach(host)) return;
                }
                string sig = string.Join(",", active.ToArray());
                double w = fxHostCanvas.ActualWidth, h = fxHostCanvas.ActualHeight;
                if (active.Count == 0)
                {
                    seasonalBuiltFor = null;
                    FxRun(() => { StopSeasonTimer(); if (seasonCanvas != null && flakes.Count > 0) { seasonCanvas.Children.Clear(); flakes.Clear(); stormFlash = null; } });
                    return;
                }
                bool rebuild = false;
                if (w > 0 && h > 0 && (sig != seasonalBuiltFor || w != fxBuiltW || h != fxBuiltH)) { seasonalBuiltFor = sig; fxBuiltW = w; fxBuiltH = h; rebuild = true; }
                var list = active.ToList();
                FxRun(() =>
                {
                    if (seasonCanvas == null) return;
                    if (rebuild)
                    {
                        fxW = w; fxH = h; fxSig = sig;
                        seasonCanvas.Width = w; seasonCanvas.Height = h;
                        BuildSeasonal(list, sig);
                        seasonCanvas.Measure(new Size(w, h));
                        seasonCanvas.Arrange(new Rect(0, 0, w, h));
                    }
                    if (run) StartSeasonTimer(); else StopSeasonTimer();   // paused: everything stays where it is
                });
            }
            catch (Exception ex) { logger.Warn("Kippix: seasonal: " + ex.Message); }
        }

        private double fxBuiltW, fxBuiltH;

        // (the effects' thread from here down)
        private void StartSeasonTimer()
        {
            if (seasonTimer == null)
            {
                // on the effects' own thread: nothing else waits behind it, and it waits for nothing
                seasonTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
                seasonTimer.Tick += (s, e) => SeasonStep();
            }
            if (!seasonTimer.IsEnabled) { seasonLast = DateTime.Now; seasonTimer.Start(); }
        }

        private void StopSeasonTimer()
        {
            if (seasonTimer != null && seasonTimer.Dispatcher != Dispatcher.CurrentDispatcher) { seasonTimer = null; return; }   // (a timer from an old thread)
            if (seasonTimer != null && seasonTimer.IsEnabled) seasonTimer.Stop();
        }

        private static Geometry Geo(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }

        // original shapes, drawn on a small grid around 0,0
        private static readonly Geometry SnowGeo = Geo("M0,-6 L0,6 M-5.2,-3 L5.2,3 M-5.2,3 L5.2,-3 M-1.6,-4.6 L0,-3.2 L1.6,-4.6 M-1.6,4.6 L0,3.2 L1.6,4.6");
        private static readonly Geometry LeafGeo = Geo("M0,-8 C5,-5 6,3 0,9 C-6,3 -5,-5 0,-8 Z");
        private static readonly Geometry LeafVein = Geo("M0,-7 L0,10 M0,-1 L3,-4 M0,3 L-3,0");
        private static readonly Geometry PetalGeo = Geo("M0,-5 C3,-4 3,3 0,5 C-3,3 -3,-4 0,-5 Z");
        private static readonly Geometry HeartGeo = Geo("M0,6 C-9,0 -7,-8 0,-3 C7,-8 9,0 0,6 Z");
        private static readonly Geometry BatGeo = Geo("M-14,0 C-11,-4 -6,-2 -4,-5 L-2,-2 L0,-4 L2,-2 L4,-5 C6,-2 11,-4 14,0 C10,-1 7,2 4,1 L0,4 L-4,1 C-7,2 -10,-1 -14,0 Z");

        private void BuildSeasonal(List<string> active, string sig)
        {
            seasonCanvas.Children.Clear();
            flakes.Clear();
            stormFlash = null;
            double w = fxW, h = fxH;
            int share = Math.Max(1, active.Count);
            foreach (string k in active)
            {
                switch (k)
                {
                    case "snow": for (int i = 0; i < 46 / share + 10; i++) AddFlake("snow", w, h, true); break;
                    case "autumn": for (int i = 0; i < 18 / share + 6; i++) AddFlake("leaf", w, h, true); break;
                    case "spring": for (int i = 0; i < 26 / share + 8; i++) AddFlake("petal", w, h, true); break;
                    case "valentine": for (int i = 0; i < 16 / share + 6; i++) AddFlake("heart", w, h, true); break;
                    case "halloween": AddPumpkins(w, h); for (int i = 0; i < 3; i++) AddFlake("bat", w, h, true); break;
                    case "christmas": AddLights(w); break;
                    case "newyear": fireworkWait = 0.5; break;
                    default: if (k.StartsWith("wx:", StringComparison.Ordinal)) AddWeatherFx(k.Substring(3), w, h); break;
                }
            }
        }

        private void AddFlake(string kind, double w, double h, bool anywhere)
        {
            var r = seasonRnd;
            FrameworkElement el;
            double size = 1;
            var f = new Flake { Kind = kind, Phase = r.NextDouble() * Math.PI * 2 };
            switch (kind)
            {
                case "snow":
                    size = 0.7 + r.NextDouble() * 1.1;
                    el = new Path { Data = SnowGeo, Stroke = Frozen(Color.FromArgb(220, 255, 255, 255)), StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                    f.Vy = 28 + r.NextDouble() * 40 * size; f.Vx = -8 + r.NextDouble() * 16; f.Spin = -30 + r.NextDouble() * 60;
                    el.Opacity = 0.55 + r.NextDouble() * 0.4;
                    break;
                case "leaf":
                {
                    size = 1.2 + r.NextDouble() * 1.0;
                    var colors = new[] { Color.FromRgb(214, 104, 38), Color.FromRgb(190, 64, 40), Color.FromRgb(226, 160, 52), Color.FromRgb(150, 92, 44) };
                    var g = new Grid();
                    g.Children.Add(new Path { Data = LeafGeo, Fill = Frozen(colors[r.Next(colors.Length)]) });
                    g.Children.Add(new Path { Data = LeafVein, Stroke = Frozen(Color.FromArgb(120, 60, 30, 10)), StrokeThickness = 0.8 });
                    el = g;
                    f.Vy = 34 + r.NextDouble() * 30; f.Vx = 10 + r.NextDouble() * 20; f.Spin = -90 + r.NextDouble() * 180;
                    el.Opacity = 0.85;
                    break;
                }
                case "petal":
                    size = 1.0 + r.NextDouble() * 0.8;
                    el = new Path { Data = PetalGeo, Fill = Frozen(r.Next(3) == 0 ? Color.FromRgb(255, 240, 245) : Color.FromRgb(248, 182, 204)) };
                    f.Vy = 26 + r.NextDouble() * 26; f.Vx = 14 + r.NextDouble() * 22; f.Spin = -120 + r.NextDouble() * 240;
                    el.Opacity = 0.8;
                    break;
                case "heart":
                    size = 1.2 + r.NextDouble() * 1.4;
                    el = new Path { Data = HeartGeo, Fill = Frozen(r.Next(2) == 0 ? Color.FromRgb(240, 82, 112) : Color.FromRgb(255, 150, 170)) };
                    f.Vy = -(22 + r.NextDouble() * 26); f.Vx = -6 + r.NextDouble() * 12; f.Spin = -20 + r.NextDouble() * 40;   // floats up
                    el.Opacity = 0.7;
                    break;
                case "bat":
                    size = 1.4 + r.NextDouble() * 0.8;
                    el = new Path { Data = BatGeo, Fill = Frozen(Color.FromRgb(24, 18, 30)) };
                    f.Vx = (r.Next(2) == 0 ? 1 : -1) * (70 + r.NextDouble() * 50); f.Vy = 0;
                    el.Opacity = 0.9;
                    break;
                default: return;
            }
            f.Size = size;
            f.El = el;
            // (1.0.1) drawn once into a small picture that's then only moved and turned (much cheaper than redrawing the shape each frame)
            el.CacheMode = new BitmapCache { RenderAtScale = 2.5, SnapsToDevicePixels = false };
            f.Pos = new TranslateTransform();
            f.Turn = new RotateTransform(r.NextDouble() * 360);
            var tg = new TransformGroup();
            tg.Children.Add(new ScaleTransform(size, size));
            tg.Children.Add(f.Turn);
            tg.Children.Add(f.Pos);
            el.RenderTransform = tg;
            el.IsHitTestVisible = false;
            f.X = r.NextDouble() * w;
            f.Y = anywhere ? r.NextDouble() * h : (f.Vy < 0 ? h + 20 : -20);
            if (kind == "bat") { f.Y = 120 + r.NextDouble() * h * 0.4; f.X = anywhere ? r.NextDouble() * w : (f.Vx > 0 ? -40 : w + 40); f.Turn.Angle = 0; f.Spin = 0; }
            Place(f);
            seasonCanvas.Children.Add(el);
            flakes.Add(f);
        }

        private static void Place(Flake f) { f.Pos.X = f.X; f.Pos.Y = f.Y; }

        /// <summary>A few jack-o'-lanterns sitting along the bottom corners, glowing softly.</summary>
        private void AddPumpkins(double w, double h)
        {
            var spots = new[] { new Point(70, h - 40), new Point(150, h - 28), new Point(w - 90, h - 36) };
            double[] sizes = { 1.5, 1.0, 1.3 };
            for (int i = 0; i < spots.Length; i++)
            {
                double s = sizes[i];
                var g = new Canvas { Width = 0, Height = 0 };
                var body = Frozen(Color.FromRgb(236, 120, 26));
                var dark = Frozen(Color.FromRgb(196, 88, 18));
                g.Children.Add(Shape(new Ellipse { Width = 34 * s, Height = 30 * s, Fill = dark }, -26 * s, -30 * s));
                g.Children.Add(Shape(new Ellipse { Width = 34 * s, Height = 30 * s, Fill = dark }, -8 * s, -30 * s));
                g.Children.Add(Shape(new Ellipse { Width = 30 * s, Height = 32 * s, Fill = body }, -15 * s, -32 * s));
                g.Children.Add(Shape(new Rectangle { Width = 5 * s, Height = 9 * s, Fill = Frozen(Color.FromRgb(70, 110, 40)), RadiusX = 2, RadiusY = 2 }, -2.5 * s, -39 * s));
                var glow = Frozen(Color.FromRgb(255, 214, 90));
                g.Children.Add(Shape(new Path { Data = Geo("M0,0 L5,-6 L10,0 Z"), Fill = glow, RenderTransform = new ScaleTransform(s, s) }, -12 * s, -18 * s));
                g.Children.Add(Shape(new Path { Data = Geo("M0,0 L5,-6 L10,0 Z"), Fill = glow, RenderTransform = new ScaleTransform(s, s) }, 2 * s, -18 * s));
                g.Children.Add(Shape(new Path { Data = Geo("M0,0 L4,3 L8,0 L12,3 L16,0 L14,5 L2,5 Z"), Fill = glow, RenderTransform = new ScaleTransform(s, s) }, -8 * s, -10 * s));
                var f = new Flake { Kind = "pumpkin", El = g, Pos = new TranslateTransform(), Turn = new RotateTransform(), X = spots[i].X, Y = spots[i].Y, Phase = i * 1.7 };
                g.RenderTransform = f.Pos;
                g.IsHitTestVisible = false;
                Place(f);
                seasonCanvas.Children.Add(g);
                flakes.Add(f);
            }
        }

        private static UIElement Shape(FrameworkElement el, double x, double y)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            return el;
        }

        /// <summary>A string of lights hanging across the top, twinkling.</summary>
        private void AddLights(double w)
        {
            int n = (int)(w / 70);
            var wire = new Polyline { Stroke = Frozen(Color.FromArgb(150, 30, 60, 30)), StrokeThickness = 2, IsHitTestVisible = false };
            Func<double, double> sag = x => 18 + 14 * Math.Abs(Math.Sin(x / w * Math.PI * 4));
            for (int i = 0; i <= 200; i++) { double x = w * i / 200.0; wire.Points.Add(new Point(x, sag(x))); }
            seasonCanvas.Children.Add(wire);
            var colors = new[] { Color.FromRgb(255, 80, 80), Color.FromRgb(90, 210, 120), Color.FromRgb(255, 210, 80), Color.FromRgb(90, 160, 255) };
            for (int i = 0; i < n; i++)
            {
                double x = 35 + i * 70;
                // a soft glow (a gradient, not an effect: cheap to draw) with the bulb on it
                var c = colors[i % colors.Length];
                var glowBrush = new RadialGradientBrush(Color.FromArgb(150, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                glowBrush.Freeze();
                var bulb = new Canvas { Width = 0, Height = 0, IsHitTestVisible = false };
                bulb.Children.Add(Shape(new Ellipse { Width = 34, Height = 34, Fill = glowBrush }, -12, -9));
                bulb.Children.Add(Shape(new Ellipse { Width = 10, Height = 15, Fill = Frozen(c) }, 0, 0));
                var f = new Flake { Kind = "light", El = bulb, Pos = new TranslateTransform(), Turn = new RotateTransform(), X = x - 5, Y = sag(x) + 1, Phase = seasonRnd.NextDouble() * Math.PI * 2 };
                bulb.RenderTransform = f.Pos;
                Place(f);
                seasonCanvas.Children.Add(bulb);
                flakes.Add(f);
            }
        }

        private void AddBurst(double w, double h)
        {
            var r = seasonRnd;
            double cx = w * (0.15 + r.NextDouble() * 0.7), cy = h * (0.12 + r.NextDouble() * 0.3);
            var colors = new[] { Color.FromRgb(255, 214, 90), Color.FromRgb(255, 120, 150), Color.FromRgb(120, 200, 255), Color.FromRgb(170, 255, 170), Color.FromRgb(255, 255, 255) };
            var c = colors[r.Next(colors.Length)];
            int n = 18;
            for (int i = 0; i < n; i++)
            {
                double a = Math.PI * 2 * i / n, sp = 90 + r.NextDouble() * 50;
                var dot = new Ellipse { Width = 5, Height = 5, Fill = Frozen(c), IsHitTestVisible = false };
                var f = new Flake { Kind = "spark", El = dot, Pos = new TranslateTransform(), Turn = new RotateTransform(), X = cx, Y = cy, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, Life = 0, MaxLife = 1.6 };
                dot.RenderTransform = f.Pos;
                Place(f);
                seasonCanvas.Children.Add(dot);
                flakes.Add(f);
            }
        }

        private void SeasonStep()
        {
            try
            {
                if (seasonCanvas == null) return;
                var now = DateTime.Now;
                double dt = Math.Min(0.1, (now - seasonLast).TotalSeconds);
                seasonLast = now;
                double w = fxW, h = fxH;
                if (w <= 0 || h <= 0) return;
                double t = (now - DateTime.Today).TotalSeconds;
                var gone = new List<Flake>();
                foreach (var f in flakes)
                {
                    switch (f.Kind)
                    {
                        case "snow":
                        case "leaf":
                        case "petal":
                        case "heart":
                            f.X += (f.Vx + Math.Sin(t * 0.9 + f.Phase) * (f.Kind == "snow" ? 14 : 24)) * dt;
                            f.Y += f.Vy * dt;
                            f.Turn.Angle += f.Spin * dt;
                            if (f.Vy > 0 && f.Y > h + 30) { f.Y = -30; f.X = seasonRnd.NextDouble() * w; }
                            if (f.Vy < 0 && f.Y < -30) { f.Y = h + 30; f.X = seasonRnd.NextDouble() * w; }
                            if (f.X > w + 40) f.X = -40; else if (f.X < -40) f.X = w + 40;
                            Place(f);
                            break;
                        case "bat":
                            f.X += f.Vx * dt;
                            f.Y += Math.Sin(t * 3 + f.Phase) * 22 * dt;
                            ((ScaleTransform)((TransformGroup)f.El.RenderTransform).Children[0]).ScaleY = f.Size * (0.6 + 0.4 * Math.Abs(Math.Sin(t * 9 + f.Phase)));   // wings flapping
                            if (f.X > w + 60 || f.X < -60) { f.Vx = -f.Vx; f.Y = 120 + seasonRnd.NextDouble() * h * 0.4; }
                            Place(f);
                            break;
                        case "pumpkin":
                            f.El.Opacity = 0.85 + 0.15 * Math.Sin(t * 1.6 + f.Phase);
                            break;
                        case "light":
                            f.El.Opacity = 0.45 + 0.55 * Math.Max(0, Math.Sin(t * 1.3 + f.Phase));
                            break;
                        case "drop":
                        case "fog":
                        case "sunglow":
                        case "mote":
                            StepWeatherFx(f, t, dt, w, h);
                            break;
                        case "spark":
                            f.Life += dt;
                            f.X += f.Vx * dt;
                            f.Y += f.Vy * dt;
                            f.Vy += 60 * dt;
                            f.Vx *= 0.985; f.Vy *= 0.985;
                            f.El.Opacity = Math.Max(0, 1 - f.Life / f.MaxLife);
                            if (f.Life >= f.MaxLife) gone.Add(f);
                            Place(f);
                            break;
                    }
                }
                foreach (var f in gone) { seasonCanvas.Children.Remove(f.El); flakes.Remove(f); }
                if (stormFlash != null) StepStorm(dt);
                if (fxSig.Contains("newyear"))
                {
                    fireworkWait -= dt;
                    if (fireworkWait <= 0) { AddBurst(w, h); fireworkWait = 1.4 + seasonRnd.NextDouble() * 2.2; }
                }
            }
            catch (Exception ex) { StopSeasonTimer(); logger.Warn("Kippix: seasonal step: " + ex.Message); }
        }

        private void SeasonalOptionsOpened(string page)
        {
            Notify("IsOptionsSeasonal");
            Notify("IsOptionsWeatherFx");
            if (page == "seasonal") BuildSeasonalRows();
            if (page == "weatherfx") BuildWeatherFxRows();
            if (page == "home") { Notify("SeasonalSummary"); Notify("WeatherFxSummary"); }
        }
    }
}
