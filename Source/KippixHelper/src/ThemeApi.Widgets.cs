using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK.Models;

namespace KippixHelper
{
    public class WidgetLine
    {
        public string Label { get; set; }
        public string Value { get; set; }
        /// <summary>A progress bar under the line (downloads): 0..1, or below 0 for none.</summary>
        public double Bar { get; set; }
        public double BarWidth { get { return Math.Max(0, Math.Min(1, Bar)) * 420; } }
        // the bar as two shares of the widget's width (so it fits whatever shape the widget is)
        public System.Windows.GridLength BarDone { get { return new System.Windows.GridLength(Math.Max(0.001, Math.Min(1, Bar)), System.Windows.GridUnitType.Star); } }
        public System.Windows.GridLength BarLeft { get { return new System.Windows.GridLength(Math.Max(0.001, 1 - Math.Max(0, Math.Min(1, Bar))), System.Windows.GridUnitType.Star); } }
        public bool HasBar { get; set; }
        public bool IsOnline { get; set; }
        /// <summary>Press the row (downloads: opens the launcher it's downloading in); null = just for reading.</summary>
        public System.Windows.Input.ICommand Command { get; set; }
        public bool IsClickable { get { return Command != null; } }
    }

    public class WidgetCard : Observable
    {
        public string Title { get; set; }
        public string Sub { get; set; }
        private ImageSource image;
        public ImageSource Image { get { return image; } set { Set(ref image, value, "Image"); } }
        /// <summary>Opens the game (its Store page, or highlights it in your library); null when there's nothing to open.</summary>
        public ICommand Command { get; set; }
        public bool IsClickable { get { return Command != null; } }
    }

    /// <summary>One widget on the home screen (what the theme draws), made from a <see cref="WidgetConfig"/>.</summary>
    public class WidgetItem : Observable
    {
        internal WidgetConfig Config;
        internal DateTime RefreshedAt = DateTime.MinValue;
        internal int Gen;
        internal List<string> PhotoFiles;
        internal int PhotoIndex;
        internal DateTime PhotosAt = DateTime.MinValue;

        public string Type { get { return Config.Type; } }

        private string kind = "big";
        /// <summary>big (a big number / word with lines), cards (three pictures), photo, lines.</summary>
        public string Kind
        {
            get { return kind; }
            set { if (Set(ref kind, value, "Kind")) { foreach (string n in new[] { "IsBig", "IsCards", "IsPhoto", "IsLines", "IsText", "IsImage", "BoxPadding", "DesignWidth", "DesignHeight" }) Notify(n); Place(); } }
        }
        public bool IsBig { get { return kind == "big" || kind == "stats" || kind == "temp"; } }   // stats: taller (play time); temp: shorter (weather)   // stats: the same, taller (play time has more rows)
        public bool IsCards { get { return kind == "cards"; } }
        public bool IsPhoto { get { return kind == "photo"; } }
        public bool IsLines { get { return kind == "lines"; } }
        /// <summary>Your own text tile, a data widget: one block of text in its own size, colour and alignment.</summary>
        public bool IsText { get { return kind == "text"; } }
        /// <summary>A picture tile, a web snippet: a picture filling the widget.</summary>
        public bool IsImage { get { return kind == "image"; } }
        public double BaseWidth { get { return kind == "cards" ? 700 : kind == "photo" ? 580 : kind == "lines" ? 520 : kind == "text" ? 560 : kind == "image" ? 480 : 460; } }
        public double BaseHeight { get { return kind == "cards" ? 330 : kind == "photo" ? 380 : kind == "lines" ? 320 : kind == "stats" ? 360 : kind == "temp" ? 200 : kind == "text" ? 200 : kind == "image" ? 320 : 260; } }

        // ---- your own tiles (text, picture) and community widgets ----
        private CustomWidgetDef Def { get { return Config != null ? Config.Custom : null; } }
        public double TextSize { get { var d = Def; return d != null ? Math.Max(12, Math.Min(200, d.FontSize)) : 40; } }
        public TextAlignment TextAlign { get { var d = Def; return d == null || d.Align == 1 ? TextAlignment.Center : d.Align == 2 ? TextAlignment.Right : TextAlignment.Left; } }
        public FontWeight TextWeight { get { var d = Def; return d != null && d.Bold ? FontWeights.Bold : FontWeights.Normal; } }
        public Stretch ImageStretch { get { var d = Def; return d != null && d.Fit == 1 ? Stretch.UniformToFill : Stretch.Uniform; } }
        /// <summary>A web page's picture from its top; a picture tile from its middle.</summary>
        public AlignmentY ImageAlignY { get { return Type == "web" ? AlignmentY.Top : AlignmentY.Center; } }
        /// <summary>A picture goes right to the edges (with its title shown: a little room for it).</summary>
        public Thickness BoxPadding { get { return kind == "image" ? (ShowTitle ? new Thickness(14, 10, 14, 14) : new Thickness(0)) : new Thickness(24, 18, 24, 18); } }
        /// <summary>In front of / behind the other widgets (Free move › Menu › Layer).</summary>
        public int Layer { get { return Config != null ? Config.Layer : 0; } }
        public double ItemOpacity { get { return Config == null || Config.Opacity <= 0 || Config.Opacity > 1 ? 1 : Math.Max(0.1, Config.Opacity); } }
        /// <summary>Its layer, opacity or own tile settings changed.</summary>
        internal void ExtrasChanged() { foreach (string n in new[] { "Layer", "ItemOpacity", "TextSize", "TextAlign", "TextWeight", "ImageStretch", "BoxPadding" }) Notify(n); }
        // the inside is laid out at this size (its own shape, and smaller for bigger text), then scaled to fit the widget
        private double TextScaleNow { get { return Config != null ? Math.Max(0.6, Math.Min(1.8, Config.TextScale <= 0 ? 1 : Config.TextScale)) : 1; } }
        public double DesignWidth
        {
            get
            {
                double dw = Math.Round(BaseWidth * MulOf(Config != null ? Config.WidthMul : 1) / TextScaleNow);
                if (BoardRect.HasValue && BoardRect.Value.Width > 0 && BoardRect.Value.Height > 0)
                {
                    // a wide, short tile: laid out tall enough for its text, then scaled down to the tile
                    double minH = BaseHeight * 0.8 / TextScaleNow;
                    double aspect = BoardRect.Value.Width / BoardRect.Value.Height;
                    if (dw / aspect < minH) dw = Math.Round(minH * aspect);
                }
                return dw;
            }
        }
        public double DesignHeight
        {
            get
            {
                // on a Board tile: the inside takes the tile's proportions (laid out to fit, not stretched)
                if (BoardRect.HasValue && BoardRect.Value.Width > 0) return Math.Round(DesignWidth * BoardRect.Value.Height / BoardRect.Value.Width);
                return Math.Round(BaseHeight * MulOf(Config != null ? Config.HeightMul : 1) / TextScaleNow);
            }
        }
        /// <summary>Board / Manual layout: the tile it sits in (null: its own place); hidden when it has no tile there.</summary>
        internal Rect? BoardRect;
        private bool boardHidden;
        public Visibility ItemVisibility { get { return boardHidden ? Visibility.Collapsed : Visibility.Visible; } }
        internal void SetBoard(Rect? r, bool hidden)
        {
            bool same = Nullable.Equals(BoardRect, r) && hidden == boardHidden;
            BoardRect = r;
            if (boardHidden != hidden) { boardHidden = hidden; Notify("ItemVisibility"); }
            if (!same) Place();
        }
        private static double MulOf(double m) { return m <= 0 ? 1 : Math.Max(0.5, Math.Min(2.5, m)); }
        public bool IsInteractive { get { return Config == null || !Config.NotInteractive; } }

        private double left, top, width, height;
        public double Left { get { return left; } }
        public double Top { get { return top; } }
        public double Width { get { return width; } }
        public double Height { get { return height; } }

        /// <summary>Position and size from the config (call after it changes).</summary>
        internal void Place()
        {
            if (Config == null) return;
            double s = Math.Max(0.3, Math.Min(3, Config.Scale));
            left = Math.Round(Config.X); top = Math.Round(Config.Y);
            width = Math.Round(BaseWidth * MulOf(Config.WidthMul) * s); height = Math.Round(BaseHeight * MulOf(Config.HeightMul) * s);
            if (BoardRect.HasValue) { var r = BoardRect.Value; left = Math.Round(r.X); top = Math.Round(r.Y); width = Math.Round(r.Width); height = Math.Round(r.Height); }
            foreach (string n in new[] { "Left", "Top", "Width", "Height", "DesignWidth", "DesignHeight", "Angle" }) Notify(n);
        }

        private double boardAngle;
        /// <summary>How far it's turned (degrees, clockwise, about its middle): its own, or its tile's when it sits in one on the Board.</summary>
        public double Angle { get { return BoardRect.HasValue ? boardAngle : (Config != null ? ThemeApi.NormAngle(Config.Rotation) : 0); } }
        internal void SetBoardAngle(double a)
        {
            if (Math.Abs(a - boardAngle) < 0.001) return;
            boardAngle = a;
            if (BoardRect.HasValue) Notify("Angle");
        }
        /// <summary>Its own turn changed (LT / RT while it's picked up).</summary>
        internal void TurnChanged() { Notify("Angle"); }

        private string title = "", big = "", sub = "", status = "", photoCaption = "";
        public string Title { get { return title; } set { Set(ref title, value ?? "", "Title"); } }
        public string Big { get { return big; } set { if (Set(ref big, value ?? "", "Big")) Notify("HasBig"); } }
        public string Sub { get { return sub; } set { if (Set(ref sub, value ?? "", "Sub")) Notify("HasSub"); } }
        public string Status { get { return status; } set { if (Set(ref status, value ?? "", "Status")) Notify("HasStatus"); } }
        public string PhotoCaption { get { return photoCaption; } set { Set(ref photoCaption, value ?? "", "PhotoCaption"); } }
        public bool HasBig { get { return big.Length > 0; } }
        public bool HasSub { get { return sub.Length > 0; } }
        public bool HasStatus { get { return status.Length > 0; } }
        private ImageSource photo;
        public ImageSource Photo { get { return photo; } set { Set(ref photo, value, "Photo"); } }
        private List<WidgetLine> lines = new List<WidgetLine>();
        public List<WidgetLine> Lines { get { return lines; } set { lines = value ?? new List<WidgetLine>(); Notify("Lines"); } }
        private List<WidgetCard> cards = new List<WidgetCard>();
        public List<WidgetCard> Cards { get { return cards; } set { cards = value ?? new List<WidgetCard>(); Notify("Cards"); } }

        // how it looks: background (see-through / solid / none), title, text colour
        private Brush backBrush, edgeBrush, textBrush, subBrush, dimBrush;
        private bool showTitle = true;
        private System.Windows.Media.Effects.Effect textShadow;
        public Brush BackBrush { get { return backBrush; } }
        public Brush EdgeBrush { get { return edgeBrush; } }
        public Brush TextBrush { get { return textBrush; } }
        public Brush SubBrush { get { return subBrush; } }
        public Brush DimBrush { get { return dimBrush; } }
        public bool ShowTitle { get { return showTitle; } }
        /// <summary>No background: a soft shadow behind the text so it stays readable over the art.</summary>
        public System.Windows.Media.Effects.Effect TextShadow { get { return textShadow; } }

        internal void SetLook(Brush back, Brush edge, Brush text, Brush sub, Brush dim, bool title, System.Windows.Media.Effects.Effect shadow)
        {
            backBrush = back; edgeBrush = edge; textBrush = text; subBrush = sub; dimBrush = dim; showTitle = title; textShadow = shadow;
            foreach (string n in new[] { "BackBrush", "EdgeBrush", "TextBrush", "SubBrush", "DimBrush", "ShowTitle", "TextShadow", "BoxPadding" }) Notify(n);
        }

        // options list
        private string name = "", placeText = "";
        public string Name { get { return name; } set { Set(ref name, value, "Name"); } }
        public string PlaceText { get { return placeText; } set { Set(ref placeText, value, "PlaceText"); } }
        public ICommand EditCommand { get; set; }
    }

    /// <summary>
    /// Home screen widgets: weather, clock, game suggestions, Spotlight (play time, achievements, your photos, friends),
    /// free games, wishlist deals, downloads, upcoming releases. Each is placed and sized by hand (Master Edit style:
    /// D-pad, grid or free, LB / RB size). They refresh only while the home screen is showing.
    /// </summary>
    public partial class ThemeApi
    {
        public ObservableCollection<WidgetItem> Widgets { get; private set; }
        public ICommand AddWidgetCommand { get; private set; }
        public ICommand WidgetMoveCommand { get; private set; }
        public ICommand WidgetSubjectCommand { get; private set; }
        public ICommand WidgetRemoveCommand { get; private set; }
        public ICommand WeatherTownCommand { get; private set; }
        private DispatcherTimer widgetTimer;
        private WidgetItem editingWidget;

        private static readonly string[] SpotlightSubjects = { "playtime", "achievements", "photos", "friends" };
        private static readonly string[] SuggestSubjects = { "never", "old", "nearly", "like", "random" };

        /// <summary>In the constructor: the list and commands exist before the theme binds to them.</summary>
        private void InitWidgetCommands()
        {
            Widgets = new ObservableCollection<WidgetItem>();
            WeatherChoices = new ObservableCollection<PickOption>();
            InitWidgetLookCommands();
            InitHomeBackground();
            InitListTiles();
            AddWidgetCommand = new RelayCommand(p => AddWidget(p as string));
            WidgetMoveCommand = new RelayCommand(_ => { if (editingWidget != null) StartWidgetMove(editingWidget, false); });
            WidgetSubjectCommand = new RelayCommand(_ => CycleWidgetSubject());
            WidgetRemoveCommand = new RelayCommand(_ => RemoveWidget());
            WeatherTownCommand = new RelayCommand(_ => AskWeatherTown());
        }

        /// <summary>At start: the widgets from the settings, and the refresh timer (it only works while the home screen shows).</summary>
        private void InitWidgets()
        {
            Widgets.Clear();
            if (settings.Widgets == null) settings.Widgets = new List<WidgetConfig>();
            FixWidgetConfigs();
            foreach (var c in settings.Widgets.ToList()) Widgets.Add(MakeWidget(c));
            widgetTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(20) };
            widgetTimer.Tick += (s, e) => WidgetTick();
            if (Widgets.Count > 0) widgetTimer.Start();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
            // first fill once the library has had time to load
            var first = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(6) };
            first.Tick += (s, e) => { first.Stop(); RefreshWidgets(true); };
            first.Start();
        }

        /// <summary>No empty entries, every widget its own id (old files, shared layouts).</summary>
        private void FixWidgetConfigs()
        {
            if (settings.Widgets == null) settings.Widgets = new List<WidgetConfig>();
            settings.Widgets.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Type));
            var seen = new HashSet<string>();
            foreach (var c in settings.Widgets)
            {
                if (string.IsNullOrEmpty(c.Id) || !seen.Add(c.Id)) { c.Id = Guid.NewGuid().ToString("N"); seen.Add(c.Id); }
                if (c.Scale <= 0 || double.IsNaN(c.Scale)) c.Scale = 1;
                c.Scale = Math.Max(0.4, Math.Min(2.5, c.Scale));
            }
        }

        private static readonly Random widgetRandom = new Random();
        /// <summary>Random is not thread-safe: one at a time.</summary>
        private static int Rand(int n) { lock (widgetRandom) return widgetRandom.Next(n); }

        public bool WidgetsOn
        {
            get { return settings.WidgetsOn; }
            set { if (settings.WidgetsOn == value) return; settings.WidgetsOn = value; SaveSettings(); Notify("WidgetsOn"); Notify("ShowWidgets"); if (value) RefreshWidgets(true); }
        }
        public bool ShowWidgets { get { return settings.WidgetsOn && Widgets != null && Widgets.Count > 0; } }
        public bool WeatherFahrenheit
        {
            get { return settings.WeatherFahrenheit; }
            set { if (settings.WeatherFahrenheit == value) return; settings.WeatherFahrenheit = value; SaveSettings(); Notify("WeatherFahrenheit"); RefreshWidgetsOfType("weather"); }
        }
        public bool RecordSessions
        {
            get { return settings.RecordSessions; }
            set { if (settings.RecordSessions == value) return; settings.RecordSessions = value; SaveSettings(); Notify("RecordSessions"); }
        }
        public string WeatherTownText { get { return string.IsNullOrEmpty(settings.WeatherTown) ? Loc("LOC_KX_WeatherNoTown", "Not set") : settings.WeatherTown; } }
        public string WidgetCountText
        {
            get { int n = Widgets != null ? Widgets.Count : 0; return n == 0 ? Loc("LOC_KX_WidgetsNone", "None yet") : string.Format(Loc("LOC_KX_WidgetsCount", "{0} on the home screen"), n); }
        }

        // ---------------- names ----------------

        private string WidgetTypeName(string type)
        {
            switch (type)
            {
                case "weather": return Loc("LOC_KX_WgWeather", "Weather");
                case "clock": return Loc("LOC_KX_WgClock", "Clock and date");
                case "suggest": return Loc("LOC_KX_WgSuggest", "Game suggestions");
                case "spotlight": return Loc("LOC_KX_WgSpotlight", "Spotlight");
                case "free": return Loc("LOC_KX_WgFree", "Free games this week");
                case "wishlist": return Loc("LOC_KX_WgWishlist", "Wishlist deals");
                case "downloads": return Loc("LOC_KX_WgDownloads", "Downloads");
                case "release": return Loc("LOC_KX_WgRelease", "Coming from your wishlist");
                case "friends": return Loc("LOC_KX_WgFriends", "Friends online");
                case "pcstats": return Loc("LOC_KX_WgPcStats", "PC stats");
                case "capture": return Loc("LOC_KX_WgCapture", "Latest capture");
                case "text": return Loc("LOC_KX_WgText", "Text tile");
                case "image": return Loc("LOC_KX_WgImage", "Picture tile");
                case "web": return Loc("LOC_KX_WgWeb", "Web snippet");
                case "feed": return Loc("LOC_KX_WgFeed", "News feed");
                case "data": return Loc("LOC_KX_WgData", "Data widget");
            }
            return type;
        }

        private string SubjectName(string type, string subject)
        {
            if (type == "spotlight")
                switch (subject)
                {
                    case "achievements": return Loc("LOC_KX_SpAch", "Recent achievements");
                    case "photos": return Loc("LOC_KX_SpPhotos", "Your photos");
                    case "friends": return Loc("LOC_KX_SpFriends", "Friends");
                    default: return Loc("LOC_KX_SpPlaytime", "Play time");
                }
            if (type == "suggest")
                switch (subject)
                {
                    case "old": return Loc("LOC_KX_SgOld", "Not played in a while");
                    case "nearly": return Loc("LOC_KX_SgNearly", "Nearly finished");
                    case "like": return Loc("LOC_KX_SgLike", "More of what you play");
                    case "random": return Loc("LOC_KX_SgRandom", "Random from your backlog");
                    default: return Loc("LOC_KX_SgNever", "Installed, never played");
                }
            return "";
        }

        private string WidgetFullName(WidgetConfig c)
        {
            if (IsCustomType(c.Type)) return CustomFullName(c);
            string s = SubjectName(c.Type, c.Subject);
            return s.Length > 0 ? WidgetTypeName(c.Type) + ": " + s : WidgetTypeName(c.Type);
        }

        private static string KindOf(WidgetConfig c)
        {
            switch (c.Type)
            {
                case "weather": return "temp";
                case "clock": return "big";
                case "suggest": case "free": case "wishlist": case "release": return "cards";
                case "downloads": case "friends": return "lines";
                case "pcstats": return "stats";
                case "capture": return "photo";
                case "text": case "data": return "text";
                case "image": case "web": return "image";
                case "feed": return "lines";
                case "spotlight":
                    return c.Subject == "achievements" ? "cards" : c.Subject == "photos" ? "photo" : c.Subject == "friends" ? "lines" : "stats";
            }
            return "big";
        }

        private WidgetItem MakeWidget(WidgetConfig c)
        {
            var w = new WidgetItem { Config = c };
            w.Kind = KindOf(c);
            w.Place();
            w.Title = WidgetTitleOf(c);
            w.Name = WidgetFullName(c);
            w.PlaceText = PlaceTextOf(c);
            var item = w;
            w.EditCommand = new RelayCommand(_ => { editingWidget = item; SetOptionsPage("widgetedit"); NotifyWidgetEdit(); });
            ApplyWidgetLook(w);
            if (c.Type == "clock") FillClock(w);
            if (c.Type == "text" || c.Type == "image") RefreshWidget(w);   // (your own tiles: straight away, nothing to wait for)
            return w;
        }

        private string PlaceTextOf(WidgetConfig c)
        {
            return string.Format(Loc("LOC_KX_WgPlace", "x {0}, y {1} · {2}%"), Math.Round(c.X), Math.Round(c.Y), Math.Round(c.Scale * 100));
        }

        // ---------------- adding, editing, removing ----------------

        /// <summary>Adds a widget and goes straight to placing it (it has no default spot: you put it where you want).</summary>
        private void AddWidget(string type)
        {
            if (string.IsNullOrEmpty(type)) return;
            if (type == "spotlight" || type == "suggest") { AskWidgetSubject(type, null); return; }   // which one first
            if (AddCustomWidget(type)) return;   // your own tiles and community widgets ask what they show first
            AddWidget(type, null);
        }

        /// <summary>A new widget straight into a Board tile (no placing it by hand): its id.</summary>
        internal string CreateWidgetForBoard(string type)
        {
            if (!settings.WidgetsOn) WidgetsOn = true;
            string subject = type == "spotlight" ? "playtime" : type == "suggest" ? "never" : null;
            var c = new WidgetConfig { Id = Guid.NewGuid().ToString("N"), Type = type, Subject = subject, Scale = 1 };
            SetupCustomConfig(c);   // (your own tiles / community widgets: their first look; what they show is set on their page)
            var w = MakeWidget(c);
            settings.Widgets.Add(c);
            Widgets.Add(w);
            SaveSettings();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
            if (widgetTimer != null && !widgetTimer.IsEnabled) widgetTimer.Start();
            RefreshWidget(w);
            return c.Id;
        }

        internal static readonly string[] WidgetTypeKeys = { "clock", "weather", "spotlight", "suggest", "free", "wishlist", "release", "downloads", "friends", "pcstats", "capture" };

        private void AddWidget(string type, string subject)
        {
            AddWidgetConfig(type, subject, null);
        }

        /// <summary>A new widget (<paramref name="made"/>: one made already, e.g. a text tile with its text, or an imported one), then placing it.</summary>
        private void AddWidgetConfig(string type, string subject, WidgetConfig made)
        {
            EditBegin();   // (undo / redo: adding it is a step once it's put down)
            if (!settings.WidgetsOn) WidgetsOn = true;   // you'd be placing something you can't see
            if (subject == null) subject = type == "spotlight" ? "playtime" : type == "suggest" ? "never" : null;
            var c = made ?? new WidgetConfig { Id = Guid.NewGuid().ToString("N"), Type = type, Subject = subject, Scale = 1 };
            if (made == null) SetupCustomConfig(c);
            var w = MakeWidget(c);
            var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets");
            double sw = layer != null && layer.ActualWidth > 0 ? layer.ActualWidth : 1920;
            c.X = Math.Round((sw - w.DesignWidth) / 2);
            c.Y = Math.Round(((layer != null && layer.ActualHeight > 0 ? layer.ActualHeight : 1080) - w.DesignHeight) / 2 - 120);
            w.Place();
            settings.Widgets.Add(c);
            Widgets.Add(w);
            SaveSettings();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
            if (widgetTimer != null && !widgetTimer.IsEnabled) widgetTimer.Start();
            RefreshWidget(w);
            StartWidgetMove(w, true);
        }

        private void RemoveWidget()
        {
            var w = editingWidget;
            if (w == null) return;
            PlaySound("back");
            settings.Widgets.RemoveAll(c => c.Id == w.Config.Id);
            Widgets.Remove(w);
            editingWidget = null;
            SaveSettings();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
            GoBackTo("widgets", "widgetadd");
        }

        private void CycleWidgetSubject()
        {
            var w = editingWidget;
            if (w == null || (w.Type != "spotlight" && w.Type != "suggest")) return;
            AskWidgetSubject(w.Type, w);
        }

        private void SetWidgetSubject(WidgetItem w, string subject)
        {
            if (w == null) return;
            w.Config.Subject = subject;
            w.Kind = KindOf(w.Config);
            w.Title = SubjectName(w.Type, w.Config.Subject);
            w.Name = WidgetFullName(w.Config);
            SaveSettings();
            NotifyWidgetEdit();
            RefreshWidget(w);
        }

        public bool IsOptionsWidgets { get { return optionsPage == "widgets"; } }
        public bool IsOptionsWidgetAdd { get { return optionsPage == "widgetadd"; } }
        public bool IsOptionsWidgetEdit { get { return optionsPage == "widgetedit"; } }
        public string EditWidgetName { get { return editingWidget != null ? editingWidget.Name : ""; } }
        public string EditWidgetPlace { get { return editingWidget != null ? PlaceTextOf(editingWidget.Config) : ""; } }
        public bool EditWidgetHasSubject { get { return editingWidget != null && (editingWidget.Type == "spotlight" || editingWidget.Type == "suggest"); } }
        public string EditWidgetSubject { get { return editingWidget != null ? SubjectName(editingWidget.Type, editingWidget.Config.Subject) : ""; } }

        private void NotifyWidgetEdit()
        {
            foreach (string n in new[] { "EditWidgetName", "EditWidgetPlace", "EditWidgetHasSubject", "EditWidgetSubject", "WidgetCountText", "WeatherTownText", "OptionsTitle" }) Notify(n);
            widgetShapeStatus = "";
            NotifyWidgetShape();
            NotifyWidgetLook();
            BuildCustomWidgetRows();   // your own tiles / community widgets: their rows
        }

        // ---------------- placing (D-pad move, LB / RB size, X grid / free, Y size back to 100 %, A keep, B cancel) ----------------

        private WidgetItem widgetMoving;
        private bool widgetIsNew;
        private double widgetStartX, widgetStartY, widgetStartScale, widgetStartRot;

        private void StartWidgetMove(WidgetItem w, bool isNew)
        {
            if (IsBoardMode && w != null && w.BoardRect.HasValue) { PlaySound("back"); return; }   // Board / Manual board: a widget in a tile moves with its tile
            EditBegin();
            if (!freeMove) masterReturnPage = isNew ? (widgetNewReturn ?? "widgets") : "widgetedit";   // (added from Settings › Community: back there)
            widgetNewReturn = null;
            ModeChanged();
            widgetMoving = w;
            widgetIsNew = isNew;
            widgetStartX = w.Config.X;
            widgetStartY = w.Config.Y;
            widgetStartScale = w.Config.Scale;
            widgetStartRot = w.Config.Rotation;
            openPanel = "master";
            NotifyPanels();
            NotifyMove();
            UpdateTopBarFade();
            ui.BeginInvoke(new Action(() =>
            {
                if (widgetMoving != w) return;
                UpdateMasterOutline();
                if (!TakeMasterKeys()) EndWidgetMove(false);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Called from the Master edit key handler while a widget is being placed.</summary>
        private void WidgetMoveKey(string button, Key key)
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
                    case Key.Enter: case Key.Space: EndWidgetMove(true); return;
                    case Key.Escape: case Key.Back: EndWidgetMove(false); return;
                    case Key.R: case Key.Delete: WidgetResize(0, true); return;
                    case Key.G: MasterSnap = !settings.MasterSnap; NotifyMove(); return;
                    case Key.PageUp: case Key.OemMinus: case Key.Subtract: WidgetResize(-1, false); return;
                    case Key.PageDown: case Key.OemPlus: case Key.Add: WidgetResize(1, false); return;
                    default: return;
                }
            }
            else
            {
                switch (button)
                {
                    case "A": EndWidgetMove(true); return;
                    case "B": EndWidgetMove(false); return;
                    case "Y": WidgetResize(0, true); return;
                    case "X": MasterSnap = !settings.MasterSnap; NotifyMove(); return;
                    case "LeftShoulder": WidgetResize(-1, false); return;
                    case "RightShoulder": WidgetResize(1, false); return;
                    default: return;
                }
            }
            WidgetNudge(dx, dy);
        }

        private void WidgetNudge(int dx, int dy)
        {
            var w = widgetMoving;
            if (w == null) return;
            double x = w.Config.X, y = w.Config.Y;
            if (settings.MasterSnap)
            {
                double g = settings.MasterGrid;
                x = dx == 0 ? x : (dx > 0 ? Math.Floor(x / g) * g + g : Math.Ceiling(x / g) * g - g);
                y = dy == 0 ? y : (dy > 0 ? Math.Floor(y / g) * g + g : Math.Ceiling(y / g) * g - g);
            }
            else
            {
                x += dx * MasterSteps[Math.Max(1, masterStep)];
                y += dy * MasterSteps[Math.Max(1, masterStep)];
            }
            var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets");
            double sw = layer != null && layer.ActualWidth > 0 ? layer.ActualWidth : 1920;
            w.Config.X = Math.Max(-w.Width / 2, Math.Min(sw - w.Width / 2, x));
            w.Config.Y = Math.Max(-w.Height / 2, Math.Min((layer != null && layer.ActualHeight > 0 ? layer.ActualHeight : 1080) - w.Height / 2, y));
            w.Place();
            Notify("MasterMoveText");
            ui.BeginInvoke(new Action(UpdateMasterOutline), DispatcherPriority.Render);
        }

        private void WidgetResize(int dir, bool reset)
        {
            var w = widgetMoving;
            if (w == null) return;
            double s = reset ? 1 : Math.Round((w.Config.Scale + dir * 0.1) * 10) / 10;
            w.Config.Scale = Math.Max(0.4, Math.Min(2.5, s));
            if (reset) { w.Config.Rotation = 0; rotRaw = 0; }   // Y: straight again too
            w.Place();
            Notify("MasterMoveText");
            ui.BeginInvoke(new Action(UpdateMasterOutline), DispatcherPriority.Render);
        }

        private void EndWidgetMove(bool keep)
        {
            var w = widgetMoving;
            if (w == null) return;
            if (!keep)
            {
                if (widgetIsNew)
                {
                    settings.Widgets.RemoveAll(c => c.Id == w.Config.Id);   // B on a new widget: not added after all
                    Widgets.Remove(w);
                }
                else { w.Config.X = widgetStartX; w.Config.Y = widgetStartY; w.Config.Scale = widgetStartScale; w.Config.Rotation = widgetStartRot; w.Place(); }
            }
            EndPickExtras(keep);   // (B: its layer and opacity back too)
            w.PlaceText = PlaceTextOf(w.Config);
            SaveSettings();
            widgetMoving = null;
            EditCheckpointAfterPick();   // put down: an undo step
            StopDrag();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
            if (freeMove) { BackToFreeCursor(); return; }
            ModeChanged();
            NotifyMove();
            openPanel = "options";
            NotifyPanels();
            if (keep && widgetIsNew) editingWidget = w;
            SetOptionsPage(keep || !widgetIsNew || masterReturnPage == "community" ? masterReturnPage : "widgets", false);
            NotifyWidgetEdit();
            UpdateTopBarFade();
            ui.BeginInvoke(new Action(() => UiTools.FocusInside("KX_OptionsPanel")), DispatcherPriority.ContextIdle);
        }

        /// <summary>Something else took over while placing (a panel, the mouse): put it back (a new one isn't added) and let go.</summary>
        private void AbortWidgetMove()
        {
            var w = widgetMoving;
            if (w == null) return;
            if (widgetIsNew) { settings.Widgets.RemoveAll(c => c.Id == w.Config.Id); Widgets.Remove(w); }
            else { w.Config.X = widgetStartX; w.Config.Y = widgetStartY; w.Config.Scale = widgetStartScale; w.Config.Rotation = widgetStartRot; w.Place(); }
            EndPickExtras(false);
            widgetMoving = null;
            SaveSettings();
            EditCheckpointAfterPick();
            NotifyMove();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
        }

        /// <summary>The move outline has the keys (otherwise the normal key handling runs, so B still gets you out).</summary>
        private bool OutlineHasFocus
        {
            get
            {
                var o = Application.Current != null ? UiTools.FindByName(Application.Current.MainWindow, "KX_MasterOutline") : null;
                return o != null && o.IsKeyboardFocused;
            }
        }

        private string WidgetMoveText()
        {
            var w = widgetMoving;
            if (w == null) return "";
            string step = settings.MasterSnap ? string.Format(Loc("LOC_KX_MeGridStep", "grid {0} px"), settings.MasterGrid)
                                              : string.Format(Loc("LOC_KX_MeFreeStep", "free, {0} px steps"), MasterSteps[Math.Max(1, masterStep)]);
            return PlaceTextOf(w.Config) + "   ·   " + step + TurnSuffix(w.Config.Rotation);
        }

        // ---------------- refreshing (only while the home screen shows) ----------------

        private bool HomeShowing
        {
            get
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                return openPanel == null && !detailsOpen && !gameRunningNow && win != null && win.IsActive;
            }
        }

        private void WidgetTick()
        {
            if (Widgets == null || Widgets.Count == 0) { widgetTimer.Stop(); return; }
            if (!settings.WidgetsOn || !HomeShowing) return;
            foreach (var w in Widgets.Where(x => x.Type == "spotlight" && x.Config.Subject == "photos")) NextPhoto(w);
            if (Widgets.Any(x => x.Type == "downloads")) RefreshDownloadsAsync();
            RefreshWidgets(false);
        }

        private static TimeSpan WidgetMaxAge(WidgetConfig c)
        {
            if (IsCustomType(c.Type)) return CustomMaxAge(c);
            switch (c.Type)
            {
                case "weather": return TimeSpan.FromMinutes(30);
                case "downloads": return TimeSpan.FromSeconds(15);
                case "pcstats": return TimeSpan.FromSeconds(3);
                case "friends": return TimeSpan.FromSeconds(70);
                case "capture": return TimeSpan.FromMinutes(5);
                case "free": case "wishlist": case "release": return TimeSpan.FromHours(2);
                case "clock": return TimeSpan.FromDays(1);
                default: return TimeSpan.FromMinutes(15);
            }
        }

        private void RefreshWidgets(bool force)
        {
            if (Widgets == null || !settings.WidgetsOn) return;
            foreach (var w in Widgets.ToList())
                if (force || DateTime.Now - w.RefreshedAt > WidgetMaxAge(w.Config)) RefreshWidget(w);
        }

        private void RefreshWidgetsOfType(string type)
        {
            if (Widgets == null) return;
            foreach (var w in Widgets.Where(x => x.Type == type).ToList()) RefreshWidget(w);
        }

        private void RefreshWidget(WidgetItem w)
        {
            w.RefreshedAt = DateTime.Now;
            int gen = ++w.Gen;
            if (RefreshCustomWidget(w, gen)) return;   // your own tiles, community widgets
            switch (w.Type)
            {
                case "clock": FillClock(w); return;
                case "downloads": FillDownloads(w); return;
                case "pcstats": FillPcStats(w); return;
                case "friends": InBackground(w, gen, () => FriendsOnlineData()); return;
                case "capture": FillLatestCapture(w, gen); return;
                case "weather": InBackground(w, gen, () => WeatherData()); return;
                case "suggest": InBackground(w, gen, () => SuggestData(w.Config.Subject)); return;
                case "free": InBackground(w, gen, () => FreeGamesData()); return;
                case "wishlist": { var wishes = settings.StoreWishlist.ToList(); InBackground(w, gen, () => WishlistDealsData(wishes)); return; }
                case "release": { var wishes = settings.StoreWishlist.ToList(); InBackground(w, gen, () => ReleaseData(wishes)); return; }
                case "spotlight":
                    switch (w.Config.Subject)
                    {
                        case "achievements": { var have = achData != null && achData.Count > 0 && (DateTime.Now - achLoadedAt).TotalMinutes < 30 ? achData : null; InBackground(w, gen, () => AchievementsData(have)); return; }
                        case "photos": InBackground(w, gen, () => PhotosData()); return;
                        case "friends": InBackground(w, gen, () => FriendsData()); return;
                        default: InBackground(w, gen, () => PlaytimeData()); return;
                    }
            }
        }

        /// <summary>What a widget shows, worked out off the UI thread.</summary>
        private class WidgetData
        {
            public string Big, Sub, Status;
            public List<WidgetLine> Lines;
            public List<Tuple<string, string, string>> Cards;   // picture (file or URL), title, line
            public List<Action> CardActions;                     // what each picture opens (same order; may be shorter / null)
            public List<string> PhotoFiles;
        }

        private void InBackground(WidgetItem w, int gen, Func<WidgetData> work)
        {
            KippixShared.SafeWork.Queue(_ =>
            {
                WidgetData d = null;
                try { d = work(); } catch (Exception ex) { logger.Warn("Kippix: widget " + w.Type + ": " + ex.Message); }
                var pics = new List<ImageSource>();
                if (d != null && d.Cards != null)
                    foreach (var c in d.Cards) pics.Add(string.IsNullOrEmpty(c.Item1) ? null : Images.Load(c.Item1, 360));
                OnUi(() =>
                {
                    if (gen != w.Gen) return;
                    if (d != null && d.Cards != null && WidgetHasFocus(w)) { w.RefreshedAt = DateTime.Now - WidgetMaxAge(w.Config) + TimeSpan.FromMinutes(1); return; }   // you're on it: try again in a minute
                    if (w.Type == "weather" && weatherRefreshing)
                    {
                        weatherRefreshing = false;
                        SetWeatherStatus(d == null ? Loc("LOC_KX_WeatherNoNet", "Couldn't reach the weather service. Check the connection and try again.")
                                         : string.IsNullOrEmpty(d.Big) ? Loc("LOC_KX_WeatherSetFirst", "Set your town first (above).")
                                         : Loc("LOC_KX_WeatherUpdated", "Weather updated."));
                    }
                    if (d == null) { w.Status = Loc("LOC_KX_WgError", "Couldn't update this just now."); return; }
                    w.Big = d.Big;
                    w.Sub = d.Sub;
                    w.Status = d.Status;
                    w.Lines = d.Lines;
                    if (d.Cards != null)
                        w.Cards = d.Cards.Select((c, i) =>
                        {
                            Action act = d.CardActions != null && i < d.CardActions.Count ? d.CardActions[i] : null;
                            return new WidgetCard { Title = c.Item2, Sub = c.Item3, Image = pics[i], Command = act != null && w.IsInteractive ? new RelayCommand(x => act()) : null };
                        }).ToList();
                    if (d.PhotoFiles != null)
                    {
                        w.PhotoFiles = d.PhotoFiles;
                        w.PhotoIndex = -1;
                        NextPhoto(w);
                    }
                });
            });
        }

        // ---------------- clock ----------------

        private void FillClock(WidgetItem w)
        {
            w.Big = ClockText;
            w.Sub = DateTime.Now.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
        }

        /// <summary>The top bar's clock ticked: the clock widgets follow.</summary>
        private void ClockWidgetsTick()
        {
            if (Widgets == null) return;
            foreach (var w in Widgets.Where(x => x.Type == "clock")) FillClock(w);
        }

        // ---------------- downloads ----------------

        private void FillDownloads(WidgetItem w)
        {
            if (WidgetHasFocus(w) && w.Lines.Count > 0) return;   // a row has the highlight: leave it be until you move off
            var list = Downloads != null ? Downloads.Take(4).Select(d => new WidgetLine { Label = d.Name, Value = d.Status, HasBar = d.HasProgress, Bar = d.HasProgress ? d.BarWidth / DownloadBarMax : 0,
                                                                          Command = w.IsInteractive ? d.Command : null }).ToList() : new List<WidgetLine>();
            w.Lines = list;
            w.Status = list.Count == 0 ? Loc("LOC_KX_WgNoDownloads", "Nothing downloading") : "";
        }

        // ---------------- weather (Open-Meteo: free, no key) ----------------

        private WidgetData WeatherData()
        {
            var d = new WidgetData { Lines = new List<WidgetLine>() };
            if (settings.WeatherLat == 0 && settings.WeatherLon == 0)
            {
                d.Status = Loc("LOC_KX_WeatherSetTown", "Set your town in Settings & Customisation › Home settings › Widgets.");
                return d;
            }
            string unit = settings.WeatherFahrenheit ? "&temperature_unit=fahrenheit" : "";
            var root = QuickJson("https://api.open-meteo.com/v1/forecast?latitude=" + settings.WeatherLat.ToString(CultureInfo.InvariantCulture)
                                 + "&longitude=" + settings.WeatherLon.ToString(CultureInfo.InvariantCulture)
                                 + "&current=temperature_2m,weather_code&timezone=auto&forecast_days=1" + unit, 10000) as Dictionary<string, object>;
            var cur = Dig(root, "current") as Dictionary<string, object>;
            if (cur == null) return null;
            d.Big = Math.Round(FDouble(cur, "temperature_2m")) + "°";
            d.Sub = WeatherWord((int)FNum(cur, "weather_code"));   // just the temperature and what it's doing (0.30)
            return d;
        }

        private static double FDouble(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }

        /// <summary>WMO weather codes in words.</summary>
        private string WeatherWord(int code)
        {
            if (code == 0) return Loc("LOC_KX_WxClear", "Clear");
            if (code <= 2) return Loc("LOC_KX_WxPartly", "Partly cloudy");
            if (code == 3) return Loc("LOC_KX_WxCloudy", "Cloudy");
            if (code == 45 || code == 48) return Loc("LOC_KX_WxFog", "Fog");
            if (code >= 51 && code <= 57) return Loc("LOC_KX_WxDrizzle", "Drizzle");
            if (code >= 61 && code <= 67) return Loc("LOC_KX_WxRain", "Rain");
            if (code >= 71 && code <= 77) return Loc("LOC_KX_WxSnow", "Snow");
            if (code >= 80 && code <= 82) return Loc("LOC_KX_WxShowers", "Showers");
            if (code == 85 || code == 86) return Loc("LOC_KX_WxSnowShowers", "Snow showers");
            if (code >= 95) return Loc("LOC_KX_WxStorm", "Thunderstorms");
            return "";
        }

        // ---------------- game suggestions (from your library) ----------------

        private string CoverFile(Game g)
        {
            string pick = g != null ? ShuffleCoverFile(g.Id) : null;   // (Shuffle covers: the one picked for now)
            if (pick != null) return pick;
            if (g == null || string.IsNullOrEmpty(g.CoverImage)) return null;
            if (g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return g.CoverImage;
            try { return api.Database.GetFullFilePath(g.CoverImage); } catch (Exception) { return null; }
        }

        private WidgetData SuggestData(string subject)
        {
            var games = api.Database.Games.Where(g => !g.Hidden).ToList();
            var picks = new List<Tuple<Game, string>>();
            switch (subject)
            {
                case "old":
                    picks = games.Where(g => g.Playtime > 3600 && g.LastActivity.HasValue && g.LastActivity.Value < DateTime.Now.AddMonths(-6))
                                 .OrderByDescending(g => g.Playtime).Take(3)
                                 .Select(g => Tuple.Create(g, string.Format(Loc("LOC_KX_SgLast", "Last played {0}"), g.LastActivity.Value.ToString("MMM yyyy", CultureInfo.CurrentCulture)))).ToList();
                    break;
                case "nearly":
                    {
                        var ach = achData != null && achData.Count > 0 ? achData : Safe(() => { string src; return AchievementSource.LoadAll(api, out src); });
                        if (ach != null)
                            picks = ach.Where(a => a.Total > 0 && a.Unlocked < a.Total && a.Unlocked >= a.Total * 0.6)
                                       .OrderByDescending(a => (double)a.Unlocked / a.Total).Take(3)
                                       .Select(a => Tuple.Create(api.Database.Games.Get(a.Id), string.Format(Loc("LOC_KX_SgAch", "{0}% of achievements"), Math.Round(100.0 * a.Unlocked / a.Total))))
                                       .Where(t => t.Item1 != null).ToList();
                        if (picks.Count == 0)
                            picks = games.Where(g => g.CompletionStatus != null && (g.CompletionStatus.Name ?? "").IndexOf("Playing", StringComparison.OrdinalIgnoreCase) >= 0)
                                         .OrderByDescending(g => g.LastActivity).Take(3).Select(g => Tuple.Create(g, Loc("LOC_KX_SgPlaying", "You're playing this"))).ToList();
                        break;
                    }
                case "like":
                    {
                        // the genre you've spent most time in, then installed games in it you've barely touched
                        var top = games.Where(g => g.Genres != null && g.Playtime > 0).SelectMany(g => g.Genres.Select(x => new { x.Name, g.Playtime }))
                                       .GroupBy(x => x.Name).OrderByDescending(x => x.Sum(y => (double)y.Playtime)).Select(x => x.Key).FirstOrDefault();
                        if (top != null)
                            picks = games.Where(g => g.IsInstalled && g.Playtime < 3600 && g.Genres != null && g.Genres.Any(x => x.Name == top))
                                         .OrderBy(_ => Rand(1 << 30)).Take(3)
                                         .Select(g => Tuple.Create(g, string.Format(Loc("LOC_KX_SgYouLike", "You love {0}"), top))).ToList();
                        break;
                    }
                case "random":
                    picks = games.Where(g => g.Playtime == 0).OrderBy(_ => Rand(1 << 30)).Take(3)
                                 .Select(g => Tuple.Create(g, g.IsInstalled ? Loc("LOC_KX_SgInstalled", "Installed") : Loc("LOC_KX_SgBacklog", "From your backlog"))).ToList();
                    break;
                default:
                    picks = games.Where(g => g.IsInstalled && g.Playtime == 0).OrderByDescending(g => g.Added).Take(3)
                                 .Select(g => Tuple.Create(g, Loc("LOC_KX_SgNeverLine", "Installed, never played"))).ToList();
                    break;
            }
            var d = new WidgetData
            {
                Cards = picks.Select(p => Tuple.Create(CoverFile(p.Item1), p.Item1.Name, p.Item2)).ToList(),
                CardActions = picks.Select(p => { Guid id = p.Item1.Id; return (Action)(() => ShowLibraryGame(id)); }).ToList()
            };
            if (d.Cards.Count == 0) d.Status = Loc("LOC_KX_SgNone", "Nothing to suggest here right now.");
            return d;
        }

        // ---------------- free games, wishlist deals, coming soon (Store data) ----------------

        private WidgetData FreeGamesData()
        {
            var list = EpicFree().Take(3).ToList();
            var d = new WidgetData
            {
                Cards = list.Select(i => Tuple.Create(i.Image, i.Name, i.Note ?? "")).ToList(),
                CardActions = list.Select(i => { var item = i; return (Action)(() => OpenStoreFromWidget(item)); }).ToList()
            };
            if (d.Cards.Count == 0) d.Status = Loc("LOC_KX_WgNoFree", "No free games right now.");
            return d;
        }

        private WidgetData WishlistDealsData(List<WishItem> all)
        {
            var d = new WidgetData { Cards = new List<Tuple<string, string, string>>(), CardActions = new List<Action>() };
            var wishes = all.Where(w => !string.IsNullOrEmpty(w.ItadId)).ToList();
            if (wishes.Count == 0 || !HasItadKey) { d.Status = Loc("LOC_KX_WgNoWish", "Add games to your wishlist in the Store."); return d; }
            var prices = ItadPrices(wishes.Select(w => w.ItadId).ToList(), ItadShops);
            var rows = new List<Tuple<WishItem, ItadDeal, bool>>();
            foreach (var w in wishes)
            {
                Tuple<List<ItadDeal>, ItadDeal> p;
                if (!prices.TryGetValue(w.ItadId, out p) || p.Item1.Count == 0) continue;
                var best = p.Item1.OrderBy(x => x.Price).First();
                bool good;
                WishNote(w, best, out good);
                rows.Add(Tuple.Create(w, best, good));
            }
            foreach (var r in rows.OrderByDescending(x => x.Item3).ThenByDescending(x => x.Item2.Cut).Take(3))
            {
                var best = r.Item2;
                string price = best.Price == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(best.Price, best.Currency);
                string line = price + (best.Cut > 0 ? "  ·  -" + best.Cut + "%" : "") + "  ·  " + ShopName(best.Shop);
                d.Cards.Add(Tuple.Create(r.Item1.Image, r.Item1.Name, line));
                var wish = r.Item1;
                d.CardActions.Add(() => OpenStoreFromWidget(WishToStoreItem(wish)));
            }
            if (d.Cards.Count == 0) d.Status = Loc("LOC_KX_WgNoDeals", "No prices for your wishlist just now.");
            return d;
        }

        private readonly Dictionary<string, DateTime?> releaseCache = new Dictionary<string, DateTime?>();

        /// <summary>
        /// Coming from your wishlist: the release calendar in small. The same dates as the calendar page (Steam / GOG wishlist
        /// imports and Steam's store; IsThereAnyDeal for the rest), the same "how far ahead"; the last card opens the whole calendar.
        /// </summary>
        private WidgetData ReleaseData(List<WishItem> all)
        {
            var d = new WidgetData { Cards = new List<Tuple<string, string, string>>(), CardActions = new List<Action>() };
            var until = DateTime.Today.AddMonths(Math.Max(1, settings.ReleaseMonths)).AddDays(1);
            var soon = new List<Tuple<WishItem, DateTime, string>>();
            int asked = 0;
            foreach (var w in all)
            {
                DateTime? when = w.ReleaseDate;
                if (!when.HasValue && !string.IsNullOrEmpty(w.ItadId))
                {
                    bool known;
                    lock (releaseCache) known = releaseCache.TryGetValue(w.ItadId, out when);
                    if (!known && asked++ < 25)
                    {
                        try
                        {
                            var info = ItadInfo(w.ItadId);
                            when = info != null ? info.ReleaseDate : null;
                            lock (releaseCache) releaseCache[w.ItadId] = when;   // only remembered when IsThereAnyDeal answered
                        }
                        catch (Exception) { }
                    }
                }
                if (when.HasValue && when.Value.Date >= DateTime.Today && when.Value.Date <= until)
                    soon.Add(Tuple.Create(w, when.Value, string.IsNullOrEmpty(w.ReleaseText) ? when.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : w.ReleaseText));
            }
            // the same games as the calendar page: dated ones first, then your library's pre-orders, then "coming, no date yet"
            var cards = soon.OrderBy(x => x.Item2).Select(x =>
            {
                var wish = x.Item1;
                return Tuple.Create(Tuple.Create(wish.Image, wish.Name, ReleaseWhen(x.Item3) + "  ·  " + DaysText(x.Item2)), (Action)(() => OpenStoreFromWidget(WishToStoreItem(wish))), x.Item2);
            }).ToList();
            try
            {
                foreach (var g in api.Database.Games.Where(g => g != null && !g.Hidden && g.ReleaseDate.HasValue))
                {
                    var rd = g.ReleaseDate.Value;
                    DateTime day;
                    try { day = new DateTime(rd.Year, rd.Month ?? 1, rd.Day ?? 1); } catch (Exception) { continue; }
                    if (day < DateTime.Today || day > until || (!rd.Month.HasValue && day.Year == DateTime.Today.Year)) continue;
                    var id = g.Id;
                    string when = rd.Day.HasValue ? day.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : day.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
                    cards.Add(Tuple.Create(Tuple.Create(CoverOf(g.Id), g.Name, when + (rd.Day.HasValue ? "  ·  " + DaysText(day) : "")), (Action)(() => ShowLibraryGame(id)), day));
                }
            }
            catch (Exception) { }
            cards = cards.OrderBy(c => c.Item3).ToList();
            foreach (var w in all.Where(w => w.ComingSoon && !(w.ReleaseDate.HasValue && w.ReleaseDate.Value.Date >= DateTime.Today)).OrderBy(w => w.Name))
            {
                var wish = w;
                cards.Add(Tuple.Create(Tuple.Create(w.Image, w.Name, string.IsNullOrEmpty(w.ReleaseText) ? Loc("LOC_KX_NoDateYet", "No date yet") : w.ReleaseText), (Action)(() => OpenStoreFromWidget(WishToStoreItem(wish))), DateTime.MaxValue));
            }
            foreach (var c in cards.Take(2))   // two games and the calendar
            {
                d.Cards.Add(c.Item1);
                d.CardActions.Add(c.Item2);
            }
            if (cards.Count > 0)
            {
                d.Cards.Add(Tuple.Create((string)null, Loc("LOC_KX_PageReleases", "Release calendar"),
                    cards.Count > 2 ? string.Format(Loc("LOC_KX_WgReleaseMore", "{0} more coming"), cards.Count - 2) : Loc("LOC_KX_WgReleaseAll", "Everything coming, by month")));
                d.CardActions.Add(() => OpenPageFromShortcut("releases"));
            }
            if (d.Cards.Count == 0) d.Status = Loc("LOC_KX_WgNoRelease", "Nothing on your wishlist is coming out soon.");
            return d;
        }

        // ---------------- Spotlight: recent achievements ----------------

        private WidgetData AchievementsData(List<AchGameData> have)
        {
            var data = have;
            if (data == null) { string src; data = AchievementSource.LoadAll(api, out src); }
            var recent = data.SelectMany(g => g.Items.Where(i => i.Unlocked && i.Date.HasValue).Select(i => new { Game = g.Name, GameId = g.Id, Item = i }))
                             .OrderByDescending(x => x.Item.Date.Value).Take(3).ToList();
            var d = new WidgetData
            {
                Cards = recent.Select(x => Tuple.Create(x.Item.Icon, x.Item.Name, x.Game + "  ·  " + AgoText(x.Item.Date.Value))).ToList(),
                CardActions = recent.Select(x => { Guid id = x.GameId; return (Action)(() => ShowLibraryGame(id)); }).ToList()
            };
            if (d.Cards.Count == 0) d.Status = Loc("LOC_KX_SpNoAch", "No achievements yet (needs Playnite Achievements or SuccessStory).");
            return d;
        }

        private string AgoText(DateTime when)
        {
            var local = when.Kind == DateTimeKind.Utc ? when.ToLocalTime() : when;
            int days = (int)(DateTime.Now.Date - local.Date).TotalDays;
            return days <= 0 ? Loc("LOC_KX_Today", "today")
                 : days == 1 ? Loc("LOC_KX_Yesterday", "yesterday")
                 : days < 14 ? string.Format(Loc("LOC_KX_DaysAgo", "{0} days ago"), days)
                 : local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        }

        // ---------------- Spotlight: your photos (Steam screenshots, Xbox Game Bar captures, Pictures\Screenshots) ----------------

        private WidgetData PhotosData()
        {
            var files = new List<FileInfo>();
            Action<string, string> add = (dir, pattern) =>
            {
                try { if (Directory.Exists(dir)) files.AddRange(new DirectoryInfo(dir).GetFiles(pattern, SearchOption.TopDirectoryOnly)); } catch (Exception) { }
            };
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string steam = k != null ? Convert.ToString(k.GetValue("SteamPath")) : null;
                    string userdata = string.IsNullOrEmpty(steam) ? null : Path.Combine(steam.Replace('/', '\\'), "userdata");
                    if (userdata != null && Directory.Exists(userdata))
                        foreach (var user in Directory.GetDirectories(userdata))
                        {
                            string remote = Path.Combine(user, "760", "remote");
                            if (!Directory.Exists(remote)) continue;
                            foreach (var app in Directory.GetDirectories(remote)) add(Path.Combine(app, "screenshots"), "*.jpg");
                        }
                }
            }
            catch (Exception) { }
            // (1.0.0) Windows' own Videos folder (it can be moved, or be on OneDrive), not always <profile>\Videos
            string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(vids)) vids = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            add(Path.Combine(vids, "Captures"), "*.png");
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"), "*.png");
            var d = new WidgetData { PhotoFiles = files.OrderByDescending(f => f.LastWriteTime).Take(40).Select(f => f.FullName).ToList() };
            if (d.PhotoFiles.Count == 0) d.Status = Loc("LOC_KX_SpNoPhotos", "No screenshots found yet (Steam, Xbox Game Bar or Pictures › Screenshots).");
            return d;
        }

        /// <summary>The next screenshot (every 20 seconds while the home screen shows).</summary>
        private void NextPhoto(WidgetItem w)
        {
            if (w.PhotoFiles == null || w.PhotoFiles.Count == 0) return;
            w.PhotoIndex = (w.PhotoIndex + 1) % w.PhotoFiles.Count;
            string file = w.PhotoFiles[w.PhotoIndex];
            int gen = w.Gen;
            KippixShared.SafeWork.Queue(_ =>
            {
                var img = Images.LoadUncached(file, 900);
                string caption = PhotoCaptionFor(file);
                OnUi(() => { if (gen != w.Gen) return; if (img != null) w.Photo = img; w.PhotoCaption = caption; });
            });
        }

        private string PhotoCaptionFor(string file)
        {
            string when = "";
            try { when = File.GetLastWriteTime(file).ToString("d MMM yyyy", CultureInfo.CurrentCulture); } catch (Exception) { }
            // Steam: ...\760\remote\<appid>\screenshots\file.jpg → the game's name from your library
            try
            {
                var parts = file.Split('\\');
                int i = Array.IndexOf(parts, "screenshots");
                if (i > 0)
                {
                    string appid = parts[i - 1];
                    var g = api.Database.Games.FirstOrDefault(x => x.PluginId == SteamPluginId && x.GameId == appid);
                    if (g != null) return g.Name + "  ·  " + when;
                }
            }
            catch (Exception) { }
            return when;
        }

        // ---------------- Spotlight: friends ----------------

        private WidgetData FriendsData()
        {
            var lines = new List<WidgetLine>();
            try
            {
                // playing right now (Steam, with your Web API key)
                lock (friendLock)
                    foreach (var f in friendInfo.Values.Where(f => !string.IsNullOrEmpty(f.PlayingAppId)).Take(4))
                    {
                        var g = api.Database.Games.FirstOrDefault(x => x.PluginId == SteamPluginId && x.GameId == f.PlayingAppId);
                        lines.Add(new WidgetLine { Label = f.Name, Value = string.Format(Loc("LOC_KX_SpPlayingNow", "Playing {0}"), g != null ? g.Name : Loc("LOC_KX_SpAGame", "a game")) });
                    }
                // who's played the game you played last
                var last = api.Database.Games.Where(g => g.LastActivity.HasValue && !g.Hidden).OrderByDescending(g => g.LastActivity).FirstOrDefault();
                if (last != null)
                    foreach (var f in FriendItemsFor(last).Where(f => !f.PlayingNow).Take(Math.Max(0, 5 - lines.Count)))
                        lines.Add(new WidgetLine { Label = f.Name, Value = last.Name + "  ·  " + f.Status });
            }
            catch (Exception ex) { logger.Warn("Kippix: friends widget: " + ex.Message); }
            return new WidgetData
            {
                Lines = lines,
                Status = lines.Count > 0 ? "" : HasPlayerActivities || !string.IsNullOrEmpty(SteamKey)
                    ? Loc("LOC_KX_SpNoFriends", "No friends playing your recent games.")
                    : Loc("LOC_KX_SpFriendsHow", "Needs PlayerActivities, or a Steam Web API key in the add-on's desktop settings.")
            };
        }

        // ---------------- Spotlight: play time ----------------

        private WidgetData PlaytimeData()
        {
            CatchUpPlaytime();
            string source;
            var sessions = LoadSessions(out source);
            var d = new WidgetData { Lines = new List<WidgetLine>() };
            if (sessions.Count == 0)
            {
                d.Status = Loc("LOC_KX_SpNoSessions", "Play something and it starts counting.");
                d.Big = "0 h";
                d.Sub = Loc("LOC_KX_SpThisWeek", "this week");
                return d;
            }
            var now = DateTime.Now;
            int back = ((int)now.DayOfWeek + 6) % 7;   // weeks start on Monday
            var weekStart = now.Date.AddDays(-back);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var yearStart = new DateTime(now.Year, 1, 1);
            Func<DateTime, double> hours = from => sessions.Where(s => s.Start >= from).Sum(s => s.Seconds) / 3600.0;
            d.Big = HoursText(hours(weekStart));
            d.Sub = Loc("LOC_KX_SpThisWeek", "this week") + (source.Length > 0 ? "  ·  " + source : "");
            d.Lines.Add(new WidgetLine { Label = Loc("LOC_KX_SpMonth", "This month"), Value = HoursText(hours(monthStart)) });
            d.Lines.Add(new WidgetLine { Label = Loc("LOC_KX_SpYear", "This year"), Value = HoursText(hours(yearStart)) });
            var top = sessions.Where(s => s.Start >= monthStart).GroupBy(s => s.Name).Select(g => new { Name = g.Key, Sec = g.Sum(x => x.Seconds) })
                              .OrderByDescending(x => x.Sec).FirstOrDefault();
            if (top != null) d.Lines.Add(new WidgetLine { Label = Loc("LOC_KX_SpTop", "Most played this month"), Value = top.Name + "  ·  " + HoursText(top.Sec / 3600.0) });
            var days = new HashSet<DateTime>(sessions.Select(s => s.Start.Date));
            var day = days.Contains(now.Date) ? now.Date : now.Date.AddDays(-1);
            int streak = 0;
            while (days.Contains(day)) { streak++; day = day.AddDays(-1); }
            d.Lines.Add(new WidgetLine { Label = Loc("LOC_KX_SpStreak", "Streak"), Value = streak == 1 ? Loc("LOC_KX_SpOneDay", "1 day") : string.Format(Loc("LOC_KX_SpDays", "{0} days"), streak) });
            return d;
        }

        private string HoursText(double h)
        {
            return h < 1 ? Math.Round(h * 60) + " min" : h < 10 ? h.ToString("0.#", CultureInfo.CurrentCulture) + " h" : Math.Round(h).ToString("N0", CultureInfo.CurrentCulture) + " h";
        }

        private static readonly Guid GameActivityId = new Guid("afbb1a0d-04a1-4d0c-9afa-c6e42ca855b4");

        private string SessionsFile { get { return Path.Combine(plugin.GetPluginUserDataPath(), "sessions.json"); } }

        private bool GameActivityInstalled
        {
            get { try { return api.Addons.Plugins.Any(p => p.Id == GameActivityId); } catch (Exception) { return false; } }
        }

        /// <summary>
        /// Play sessions of the last year: GameActivity's when it's installed (it has your history),
        /// otherwise the ones this add-on recorded (from when you turned it on).
        /// </summary>
        private List<PlaySession> LoadSessions(out string source) { return LoadSessions(out source, 400); }

        private List<PlaySession> LoadSessions(out string source, int days)
        {
            source = "";
            var list = new List<PlaySession>();
            try
            {
                string dir = Path.Combine(api.Paths.ExtensionsDataPath, GameActivityId.ToString(), "GameActivity");
                if (GameActivityInstalled && Directory.Exists(dir))
                {
                    var since = DateTime.Now.AddDays(-days);
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    foreach (var f in new DirectoryInfo(dir).GetFiles("*.json").Where(f => f.LastWriteTime >= since))
                    {
                        try
                        {
                            var root = js.DeserializeObject(File.ReadAllText(f.FullName)) as Dictionary<string, object>;
                            string name = FStr(root, "Name");
                            Guid id;
                            bool hasId = Guid.TryParse(Path.GetFileNameWithoutExtension(f.Name), out id);
                            string gameId = hasId ? id.ToString() : null;
                            if (name.Length == 0 && hasId)
                            {
                                var g = api.Database.Games.Get(id);
                                if (g != null) name = g.Name;
                            }
                            foreach (var it in (Dig(root, "Items") as IEnumerable ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>())
                            {
                                DateTime when;
                                if (!DateTime.TryParse(FStr(it, "DateSession"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when)) continue;
                                if (when.Kind == DateTimeKind.Utc) when = when.ToLocalTime();
                                long sec = FNum(it, "ElapsedSeconds");
                                if (sec > 0 && when >= since) list.Add(new PlaySession { GameId = gameId, Name = name, Start = when, Seconds = sec });
                            }
                        }
                        catch (Exception) { }
                    }
                    if (list.Count > 0) { source = "GameActivity"; return list; }
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: GameActivity: " + ex.Message); }
            try
            {
                lock (sessionLock)
                    if (File.Exists(SessionsFile))
                        list = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<PlaySession>>(File.ReadAllText(SessionsFile)) ?? new List<PlaySession>();
                foreach (var s in list) if (s != null && s.Start.Kind == DateTimeKind.Utc) s.Start = s.Start.ToLocalTime();
                list = list.Where(s => s != null && string.IsNullOrEmpty(s.Profile)).ToList();   // other profiles' sessions are theirs
            }
            catch (Exception) { list = new List<PlaySession>(); }
            return list;
        }

        private readonly object sessionLock = new object();

        /// <summary>One game's sessions (the game page): just its own GameActivity file when there is one.</summary>
        private List<PlaySession> LoadSessionsFor(Guid gameId)
        {
            try
            {
                string file = Path.Combine(api.Paths.ExtensionsDataPath, GameActivityId.ToString(), "GameActivity", gameId + ".json");
                if (GameActivityInstalled && File.Exists(file))
                {
                    var list = new List<PlaySession>();
                    var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                    foreach (var it in (Dig(root, "Items") as IEnumerable ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>())
                    {
                        DateTime when;
                        if (!DateTime.TryParse(FStr(it, "DateSession"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out when)) continue;
                        if (when.Kind == DateTimeKind.Utc) when = when.ToLocalTime();
                        long sec = FNum(it, "ElapsedSeconds");
                        if (sec > 0) list.Add(new PlaySession { GameId = gameId.ToString(), Start = when, Seconds = sec });
                    }
                    return list;
                }
            }
            catch (Exception) { }
            string src;
            return LoadSessions(out src).Where(s => string.Equals(s.GameId, gameId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>A game stopped: remember the session (unless GameActivity does that already, or you turned it off).</summary>
        public void RecordSession(Game game, ulong seconds)
        {
            try
            {
                if (!settings.RecordSessions || game == null || seconds < 60) return;
                bool someoneElse = ProfilesOn && !IsOwnerProfile;
                if (GameActivityInstalled && !someoneElse) return;   // it keeps the history already (other profiles: the theme keeps theirs)
                string who = someoneElse ? CurrentProfileId : null;
                lock (sessionLock)
                {
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    List<PlaySession> list = null;
                    if (File.Exists(SessionsFile))
                    {
                        // a file we can't read isn't overwritten (that would lose the history): keep a copy and start a new one
                        try { list = js.Deserialize<List<PlaySession>>(File.ReadAllText(SessionsFile)); }
                        catch (Exception) { try { File.Copy(SessionsFile, SessionsFile + ".unreadable", true); } catch (Exception) { return; } }
                    }
                    list = list ?? new List<PlaySession>();
                    list.Add(new PlaySession { GameId = game.Id.ToString(), Name = game.Name, Start = DateTime.Now.AddSeconds(-(double)seconds), Seconds = (long)seconds, Profile = who });
                    var cutoff = DateTime.Now.AddYears(-3);
                    list = list.Where(s => s != null && s.Start >= cutoff).ToList();
                    Directory.CreateDirectory(Path.GetDirectoryName(SessionsFile));
                    string tmp = SessionsFile + ".tmp";
                    File.WriteAllText(tmp, js.Serialize(list));
                    if (File.Exists(SessionsFile)) File.Replace(tmp, SessionsFile, null); else File.Move(tmp, SessionsFile);
                }
                OnUi(() => { if (Widgets != null) foreach (var w in Widgets.Where(x => x.Type == "spotlight" && x.Config.Subject == "playtime")) w.RefreshedAt = DateTime.MinValue; });
            }
            catch (Exception ex) { logger.Warn("Kippix: recording a session: " + ex.Message); }
        }
    }
}
