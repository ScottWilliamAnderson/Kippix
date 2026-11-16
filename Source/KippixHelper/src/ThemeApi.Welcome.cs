using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace KippixHelper
{
    /// <summary>(0.86) One hint on Getting started: an icon, a few words and a line.</summary>
    public class TourTip
    {
        public Geometry Icon { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
    }

    /// <summary>A progress dot on the tour.</summary>
    public class TourDot
    {
        public bool IsCurrent { get; set; }
        public bool IsDone { get; set; }
        public bool IsAhead { get { return !IsCurrent && !IsDone; } }
        public string Number { get; set; }   // (0.99.8) the rail on the left: each step's number and name
        public string Name { get; set; }
    }

    /// <summary>
    /// The first-run tour (the first time Kippix starts; again from Settings › Help › Take the tour again, or every start
    /// for testing from the helper's desktop settings): Welcome to Kippix → a starting look (the home screen shows through
    /// while you pick) → your stores (which of Playnite's library add-ons are signed in; A opens that add-on's own settings, or
    /// the Steam store sign-in) → you and your profiles → a controller check (the pad that was found, its buttons light up as
    /// they're pressed) → done. Progress dots, Back / Next / Skip under the rows; B and LB / RB step back and on.
    /// Pictures can go in the theme's Welcome folder (hello.png, tips.png).
    /// </summary>
    public partial class ThemeApi
    {
        private const string GuideUrl = "https://github.com/Kipping0/Kippix/blob/main/docs/GUIDE.md";

        private static readonly string[] WelcomeSteps = { "hello", "look", "style", "gamepage", "stores", "profiles", "done" };   // (0.87: no controller check; 0.89: + card glow & background, game page)
        private int welcomeStep;
        private string welcomeResume;   // left the tour to pick a picture / sign in: it comes back to this step
        public bool IsWelcomeOpen { get { return openPanel == "welcome"; } }
        public string WelcomeTitle { get; private set; }
        public string WelcomeText { get; private set; }
        public string WelcomeStepText { get; private set; }
        private ImageSource welcomeImage;
        public ImageSource WelcomeImage { get { return welcomeImage; } }
        public bool HasWelcomeImage { get { return welcomeImage != null; } }
        private ObservableCollection<PickRow> welcomeRows;
        public ObservableCollection<PickRow> WelcomeRows { get { return welcomeRows ?? (welcomeRows = new ObservableCollection<PickRow>()); } }

        // the tour's chrome: dots, Back / Next / Skip, the see-through look step, the controller check
        public List<TourDot> TourDots { get; private set; }
        public bool HasTourBack { get { return welcomeStep > 0; } }
        /// <summary>(0.86) A new install goes through Getting started (no Skip); taking it again from Settings › Help, or the
        /// helper's testing switch (desktop mode), gives it a Skip button.</summary>
        /// (1.0.0) Only after Getting started has been finished once: stopping part-way (or quitting Playnite) brings it back
        /// at the next start, from the beginning. The old testing switch to skip it is gone.
        private bool TourSkippable { get { return settings.WelcomeDone; } }

        /// <summary>(1.0.1) Getting started is open and hasn't been finished yet: nothing but its own buttons (and the rows
        /// that open a page from it) may close it. Holding B / Esc and the Home button used to go home from it.</summary>
        private bool TourLocked { get { return openPanel == "welcome" && !settings.WelcomeDone; } }
        public bool HasTourSkip { get { return TourSkippable && welcomeStep < WelcomeSteps.Length - 1; } }
        public bool IsTourHello { get { return openPanel == "welcome" && WelcomeSteps[welcomeStep] == "hello"; } }
        public bool IsTourStep { get { return !IsTourHello; } }
        public string WelcomeKicker { get; private set; }
        public string WelcomeHelloFoot { get { return Loc("LOC_KX_TourHelloFoot", "A few quick steps  ·  everything can be changed later in Settings & Customisation"); } }
        public List<TourTip> TourTips { get; private set; }
        public bool HasTourTips { get { return TourTips != null && TourTips.Count > 0 && !IsTourHello; } }   // (0.99.8: the Good to know box is back)

        // ---------------- (0.99.9) the choices as big picture tiles ----------------

        // ---------------- (0.99.10) Card glow on one of your games ----------------

        private bool tourCardOn;
        private bool tourCardTried;
        private ImageSource tourCardCover;
        private string tourCardBgPath;
        private int tourBgMode = -1;
        private readonly Dictionary<string, ImageSource> tourBgPics = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>(0.99.11) A background choice: the card's game shown that way (its art, its trailer or pictures, its cover on a plain
        /// colour, or the colour on its own), its card in the corner like on the home screen.</summary>
        private void TourBgShow(int m)
        {
            TourCardCoverLoad();
            bool was = IsTourBgShown;
            tourCardOn = false;
            tourBgMode = m;
            TourBgBadge = m == 4 ? Loc("LOC_KX_TourBgBadgeTrailer", "▶  Its trailer plays here")
                        : m == 2 ? Loc("LOC_KX_TourBgBadgePreview", "▶  Then its trailer")
                        : m == 5 ? Loc("LOC_KX_TourBgBadgeSlides", "Its pictures, one after another") : "";
            foreach (string n in new[] { "IsTourBgShown", "IsTourCardShown", "HasTourLookImage", "TourBgBadge", "HasTourBgBadge" }) Notify(n);
            ui.BeginInvoke(new Action(TourBgPut), DispatcherPriority.Loaded);
            int mm = m;
            ui.BeginInvoke(new Action(() => { if (tourBgMode == mm) TourBgMediaFor(mm); }), DispatcherPriority.Loaded);   // (0.99.17) trailer / slideshow
        }

        private void TourBgHide()
        {
            TourBgMediaStop();
            if (tourBgMode < 0) return;
            tourBgMode = -1;
            Notify("IsTourBgShown");
        }

        private void TourBgPut()
        {
            try
            {
                if (tourBgMode < 0) return;
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                var art = UiTools.FindByName(win, "KX_TourBgArt") as System.Windows.Controls.Border;
                var tile = UiTools.FindByName(win, "KX_TourBgTile") as System.Windows.Controls.Border;
                var mini = UiTools.FindByName(win, "KX_TourBgMini") as FrameworkElement;
                var miniArt = UiTools.FindByName(win, "KX_TourBgMiniArt") as System.Windows.Controls.Border;
                if (art == null) return;
                int m = tourBgMode;
                bool plain = m == 1 || m == 6;
                Color colour = HexOr(settings.HomeBgColor, Color.FromRgb(0x0E, 0x10, 0x14));
                if (plain) art.Background = new SolidColorBrush(colour);
                else
                {
                    var bg = TourBgPicture();
                    art.Background = bg != null ? (Brush)new ImageBrush(bg) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x20));
                }
                if (tile != null)
                {
                    tile.Visibility = m == 6 ? Visibility.Visible : Visibility.Collapsed;
                    if (tourCardCover != null) tile.Background = new ImageBrush(tourCardCover) { Stretch = Stretch.UniformToFill };
                }
                if (mini != null) mini.Visibility = m == 6 ? Visibility.Collapsed : Visibility.Visible;
                if (miniArt != null && tourCardCover != null) miniArt.Background = new ImageBrush(tourCardCover) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception) { }
        }

        /// <summary>The card's game's background picture (its cover when it has none); loaded once, then put on when it's in.</summary>
        private ImageSource TourBgPicture()
        {
            string path = !string.IsNullOrEmpty(tourCardBgPath) ? tourCardBgPath : null;
            if (path == null) return tourCardCover;
            ImageSource img;
            if (tourBgPics.TryGetValue(path, out img)) return img ?? tourCardCover;
            tourBgPics[path] = null;
            KippixShared.SafeWork.Queue(_ =>
            {
                ImageSource got = null;
                try { got = Images.Load(path, 1000); } catch (Exception) { }
                OnUi(() => { tourBgPics[path] = got; TourBgPut(); });
            });
            return tourCardCover;
        }

        /// <summary>The sample card: any game of yours with a cover (picked once), glowing as glow style <paramref name="g"/> would.</summary>
        private void TourCardShow(int g)
        {
            tourBgMode = -1;
            TourBgMediaStop();
            if (!tourCardOn) { tourCardOn = true; Notify("IsTourCardShown"); Notify("HasTourLookImage"); }
            Notify("IsTourBgShown");
            TourCardCoverLoad();
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    var win = Application.Current != null ? Application.Current.MainWindow : null;
                    var ring = win != null ? UiTools.FindByName(win, "KX_TourCardRing") as System.Windows.Controls.Border : null;
                    if (ring == null) return;
                    var L = LookNow;
                    Color glow = g == 3 ? Colors.Black : string.IsNullOrEmpty(L.GlowColor) ? accentNow : HexOr(L.GlowColor, Colors.White);
                    double size = (g == 2 ? L.GlowSize * 1.4 : L.GlowSize) * 1.6;   // (the card here is bigger than on the home screen)
                    double opacity = g == 0 ? 0 : g == 2 ? 0.95 : g == 3 ? 0.75 : 0.5;
                    ring.Effect = opacity > 0 && size > 0 ? new System.Windows.Media.Effects.DropShadowEffect { Color = glow, BlurRadius = size, ShadowDepth = 0, Opacity = opacity } : null;
                    TourCardArtPut();
                }
                catch (Exception) { }
            }), DispatcherPriority.Loaded);
        }

        private void TourCardHide()
        {
            if (!tourCardOn) return;
            tourCardOn = false;
            Notify("IsTourCardShown");
            Notify("HasTourLookImage");
        }

        private void TourCardCoverLoad()
        {
            if (tourCardTried) return;
            tourCardTried = true;
            string path = null, name = null;
            try
            {
                var games = api.Database.Games.Where(x => !x.Hidden && !string.IsNullOrEmpty(x.CoverImage) && !x.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();
                // (0.99.11) one with its own background picture too, when there is one, so Background shows the same game
                var both = games.Where(x => !string.IsNullOrEmpty(x.BackgroundImage) && !x.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase)).ToList();
                if (both.Count > 0) games = both;
                // (0.99.17) and with a trailer (downloaded by Extra Metadata Loader), so Trailer and Art then trailer have one to show
                var rnd = new Random();
                var withTrailer = games.OrderBy(x => rnd.Next()).Take(400).Where(HasLocalTrailer).ToList();
                if (withTrailer.Count > 0) games = withTrailer;
                if (games.Count > 0)
                {
                    var pick = games[rnd.Next(games.Count)];
                    tourCardGame = pick;
                    path = api.Database.GetFullFilePath(pick.CoverImage);
                    name = pick.Name;
                    if (!string.IsNullOrEmpty(pick.BackgroundImage) && !pick.BackgroundImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        string bp = api.Database.GetFullFilePath(pick.BackgroundImage);
                        if (File.Exists(bp)) tourCardBgPath = bp;
                    }
                }
            }
            catch (Exception) { }
            TourCardName = name ?? "";
            Notify("TourCardName");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                ImageSource img = null;
                try { img = Images.Load(path, 520); } catch (Exception) { }
                if (img != null) OnUi(() => { tourCardCover = img; TourCardArtPut(); TourBgPut(); });
            });
        }

        private void TourCardArtPut()
        {
            if (tourCardCover == null) return;
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var art = win != null ? UiTools.FindByName(win, "KX_TourCardArt") as System.Windows.Controls.Border : null;
                if (art != null && !(art.Background is ImageBrush)) art.Background = new ImageBrush(tourCardCover) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception) { }
        }

        private readonly Dictionary<string, ImageSource> tourTilePics = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every choice's own picture (a look's photo, else its drawing; a style's or game page's picture) and its icon as a shape.</summary>
        private void TourTileArt(string step, IEnumerable<PickRow> rows)
        {
            if (step != "look") return;   // (0.99.10: the other steps are lists)
            foreach (var r in rows)
            {
                if (r.IsHeader) continue;
                if (r.Glyph == null) r.Glyph = TourGlyph(r.Icon);
                if (r.Picture != null) continue;
                string f = null;
                try { f = TourTileFile(step, r.Key); } catch (Exception) { }
                var img = f != null ? TourTileImage(f) : null;
                if (img != null) r.Picture = img;
            }
        }

        private string TourTileFile(string step, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string dir = Path.Combine(ThemeDir, "Images", "Welcome");
            if (step == "look")
            {
                bool lookRow = key.StartsWith("look:", StringComparison.Ordinal) || key.StartsWith("rt", StringComparison.Ordinal) || key.StartsWith("ps", StringComparison.Ordinal);
                if (!lookRow) return null;
                string state = TourStateFor(key);
                if (state != null)
                {
                    string photo = Path.Combine(dir, "Looks", state.Replace("|", "_") + ".jpg");
                    if (File.Exists(photo)) return photo;
                }
                return Path.Combine(dir, TourDrawingFor(key) + ".png");
            }
            string n = TourPicFor(key);
            return n != null ? Path.Combine(dir, n + ".png") : null;
        }

        private ImageSource TourTileImage(string file)
        {
            ImageSource img;
            if (tourTilePics.TryGetValue(file, out img)) return img;
            try { img = File.Exists(file) ? Images.LoadUncached(file, 600) : null; } catch (Exception) { img = null; }
            tourTilePics[file] = img;
            return img;
        }

        private static Geometry TourGlyph(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            string key;
            switch (icon)
            {
                case "colour": key = "KX_IconPalette"; break;
                case "picture": key = "KX_IconGallery"; break;
                case "achievements": key = "KX_IconTrophy"; break;
                case "settings": key = "KX_IconGear"; break;
                case "more": case "options": key = "KX_IconDots"; break;
                case "place": key = "KX_IconFolder"; break;
                case "use": key = "KX_IconCheck"; break;
                case "remove": key = "KX_IconClose"; break;
                default: key = "KX_Icon" + char.ToUpperInvariant(icon[0]) + icon.Substring(1); break;
            }
            return TipIcon(key) ?? TipIcon("KX_IconStar");
        }

        /// <summary>(0.99.8) A step's name on the rail.</summary>
        private string TourStepName(string step)
        {
            switch (step)
            {
                case "hello": return Loc("LOC_KX_TourRailHello", "Welcome");
                case "look": return Loc("LOC_KX_TourRailLook", "Your look");
                case "style": return Loc("LOC_KX_TourRailStyle", "Style");
                case "gamepage": return Loc("LOC_KX_TourRailGamePage", "Game page");
                case "stores": return Loc("LOC_KX_TourRailStores", "Stores");
                case "profiles": return settings.ProfilesEnabled ? Loc("LOC_KX_TourRailProfiles", "Who plays") : Loc("LOC_KX_TourRailYou", "About you");
                case "controller": return Loc("LOC_KX_TourRailPad", "Controller");
                case "done": return Loc("LOC_KX_TourRailDone", "All set");
                default: return step;
            }
        }
        public string TourTipsHeader { get; private set; }

        // the logo (the theme's Images folder): the whole logo, and the K on its own
        private ImageSource welcomeLogo, welcomeMark;
        private bool welcomeLogoTried;
        public ImageSource WelcomeLogo { get { LoadWelcomeLogos(); return welcomeLogo; } }
        public ImageSource WelcomeMark { get { LoadWelcomeLogos(); return welcomeMark; } }
        public bool HasWelcomeLogo { get { LoadWelcomeLogos(); return welcomeLogo != null; } }
        public bool NoWelcomeLogo { get { return !HasWelcomeLogo; } }

        // (0.88) Pick a starting look: a picture of the look you're on (Images\Welcome\look-<key>.png in the theme; for now
        // pictures that just say the look's name), above the Good to know box
        private static readonly string[] TourLookKeys = { "row", "tall", "full", "grid", "list", "board", "folders" };
        private readonly Dictionary<string, ImageSource> tourLookPics = new Dictionary<string, ImageSource>();
        private string tourLookShown;
        private bool tourFocusHooked;
        public ImageSource TourLookImage { get; private set; }
        public bool HasTourLookImage
        {
            get
            {
                if (TourLookImage == null || openPanel != "welcome") return false;
                string st = WelcomeSteps[welcomeStep];
                return st == "gamepage";   // (0.99.10: beside the list; Pick a look's tiles carry their own; 0.99.11: Glow and background shows your game)
            }
        }
        /// <summary>(0.99.10) Pick a look shows its choices as picture tiles; every other step is a list.</summary>
        public bool IsTourTiles { get { return openPanel == "welcome" && WelcomeSteps[welcomeStep] == "look"; } }
        public bool IsTourList { get { return openPanel == "welcome" && !IsTourHello && !IsTourTiles; } }
        /// <summary>(0.99.10) Card glow: one of your own games' cards, glowing the way the highlighted choice would.</summary>
        public bool IsTourCardShown { get { return openPanel == "welcome" && WelcomeSteps[welcomeStep] == "style" && tourCardOn; } }
        /// <summary>(0.99.11) Background: the same game as the card, behind a little home screen, the way the highlighted background would show it.</summary>
        public bool IsTourBgShown { get { return openPanel == "welcome" && WelcomeSteps[welcomeStep] == "style" && tourBgMode >= 0; } }
        public string TourBgBadge { get; private set; }
        public bool HasTourBgBadge { get { return !string.IsNullOrEmpty(TourBgBadge); } }
        public string TourCardName { get; private set; }

        /// <summary>(0.89) A tour row's picture (Images\Welcome\&lt;name&gt;.png), or null for a row without one.</summary>
        private string TourPicFor(string rowKey)
        {
            if (rowKey == null) return null;
            if (Array.IndexOf(TourLookKeys, rowKey) >= 0) return "look-" + rowKey;
            switch (rowKey)
            {
                case "glow0": return "glow-none";
                case "glow1": return "glow-soft";
                case "glow2": return "glow-neon";
                case "glow3": return "glow-shadow";
                case "accent": return settings.AccentFromGame ? "accent-on" : "accent-off";
                case "bg0": return "bg-art";
                case "bg4": return "bg-trailer";
                case "bg5": return "bg-slideshow";
                case "bg2": return "bg-preview";
                case "bg6": return "bg-tile";
                case "bg1": return "bg-colour";
                case "gp0": return "gamepage-default";
                case "gp2": return "gamepage-centred";
            }
            return null;
        }

        /// <summary>The picture for what's chosen on this step (when the highlight isn't on a row with its own).</summary>
        private string TourPicNow()
        {
            switch (WelcomeSteps[welcomeStep])
            {
                case "look": return "look-" + TourLookKeyNow();
                case "style": return TourPicFor("glow" + Math.Max(0, Math.Min(3, LookNow.GlowStyle)));
                case "gamepage": return settings.DetLayout == 2 ? "gamepage-centred" : "gamepage-default";
            }
            return null;
        }

        private string TourLookKeyNow()
        {
            int m = settings.HomeLayoutMode;
            if (m == 0) return settings.RowDesign == 1 ? "tall" : settings.RowDesign == 2 ? "full" : "row";
            return m == 1 ? "grid" : m == 2 ? "list" : m == 5 ? "folders" : "board";
        }

        private void ShowTourLook(string key)   // (0.89: key = the picture's file name)
        {
            if (key == null || key == tourLookShown) return;
            tourLookShown = key;
            ImageSource img;
            if (!tourLookPics.TryGetValue(key, out img))
            {
                try { img = Images.LoadUncached(Path.Combine(ThemeDir, "Images", "Welcome", key + ".png"), 1120); } catch (Exception) { img = null; }
                tourLookPics[key] = img;
            }
            TourLookImage = img;
            Notify("TourLookImage");
            Notify("HasTourLookImage");
        }

        /// <summary>The highlight on a look's row: its picture (the look picked shows when the highlight is elsewhere).</summary>
        private void HookTourFocus()
        {
            if (tourFocusHooked) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var view = win != null ? UiTools.FindByName(win, "KX_WelcomeView") : null;
            if (view == null) return;
            tourFocusHooked = true;
            view.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((o, e) =>
            {
                try
                {
                    if (openPanel != "welcome") return;
                    string st = WelcomeSteps[welcomeStep];
                    if (st == "look") { var nf = e.NewFocus as FrameworkElement; TourZoomFollow(nf != null ? nf.DataContext as PickRow : null); return; }   // (0.99.14)
                    if (st != "style" && st != "gamepage") return;   // (0.99.10: Pick a look's tiles carry their own pictures)
                    var fe = e.NewFocus as FrameworkElement;
                    var row = fe != null ? fe.DataContext as PickRow : null;
                    if (row == null) return;   // (Back / Skip / Next: what's showing stays)
                    string k = row.Key ?? "";
                    if (st == "style" && k.StartsWith("glow", StringComparison.Ordinal) && k.Length == 5) { TourCardShow(k[4] - '0'); return; }
                    if (st == "style" && k == "accent") { TourCardShow(Math.Max(0, Math.Min(3, LookNow.GlowStyle))); return; }
                    int bm;
                    if (st == "style" && k.StartsWith("bg", StringComparison.Ordinal) && int.TryParse(k.Substring(2), out bm)) { TourBgShow(bm); return; }
                    if (st == "style") return;
                    TourCardHide();
                    ShowTourLook(TourPicFor(k) ?? TourPicNow());
                }
                catch (Exception) { }
            }), true);
        }

        private void LoadWelcomeLogos()
        {
            if (welcomeLogoTried) return;
            welcomeLogoTried = true;
            try
            {
                string dir = Path.Combine(ThemeDir, "Images");
                welcomeLogo = Images.LoadUncached(Path.Combine(dir, "kippix-logo.png"), 1512);
                welcomeMark = Images.LoadUncached(Path.Combine(dir, "kippix-mark.png"), 160);
            }
            catch (Exception ex) { logger.Warn("Kippix: getting started: the logo: " + ex.Message); }
        }

        private static Geometry TipIcon(string key)
        {
            try { return Application.Current != null ? Application.Current.TryFindResource(key) as Geometry : null; } catch (Exception) { return null; }
        }

        private void Tips(string header, params string[] parts)   // icon key, title, text, icon key, title, text…
        {
            TourTipsHeader = header;
            var l = new List<TourTip>();
            for (int i = 0; i + 2 < parts.Length; i += 3) l.Add(new TourTip { Icon = TipIcon(parts[i]), Title = parts[i + 1], Text = parts[i + 2] });
            TourTips = l;
        }
        public string TourNextText { get { return welcomeStep >= WelcomeSteps.Length - 1 ? Loc("LOC_KX_TourFinish", "Start playing") : Loc("LOC_KX_WelcomeNext", "Next"); } }
        public bool IsTourSeeThrough { get { return false; } }   // (0.87: every step on Kippix's own background, the look step too)
        public bool IsTourSolid { get { return !IsTourSeeThrough; } }
        public bool IsTourController { get { return openPanel == "welcome" && WelcomeSteps[welcomeStep] == "controller"; } }
        public string TourPadText { get; private set; }
        public string TourHint { get; private set; }
        private ICommand tourBackCmd, tourNextCmd, tourSkipCmd;
        public ICommand TourBackCommand { get { return tourBackCmd ?? (tourBackCmd = new RelayCommand(_ => { PlaySound("back"); WelcomeBack(); })); } }
        public ICommand TourNextCommand { get { return tourNextCmd ?? (tourNextCmd = new RelayCommand(_ => WelcomeNext())); } }
        public ICommand TourSkipCommand { get { return tourSkipCmd ?? (tourSkipCmd = new RelayCommand(_ => { if (!TourSkippable) { PlaySound("back"); return; } PlaySound("back"); FinishWelcome(); })); } }
        private ICommand tourAgainCmd;
        /// <summary>Settings › Help › Take the tour again.</summary>
        public ICommand TourAgainCommand { get { return tourAgainCmd ?? (tourAgainCmd = new RelayCommand(_ => { PlaySound("select"); ClosePanels(); ui.BeginInvoke(new Action(OpenWelcome), DispatcherPriority.ContextIdle); })); } }

        private DispatcherTimer tourPadTimer;
        private DateTime tourBAt = DateTime.MinValue;

        private bool WelcomeShouldShow() { return settings.WelcomeOnBoot || !settings.WelcomeDone; }

        private void OpenWelcome() { OpenWelcomeAt(0); }

        private bool welcomeStarted;   // Getting started has been opened this session

        private void OpenWelcomeAt(int step)
        {
            welcomeStarted = true;
            profileEditFromTour = false;
            // (0.89) back from picking a profile's picture: its box again
            if (tourProfileId != null)
            {
                string reopen = tourProfileId;
                if (WelcomeSteps[Math.Max(0, Math.Min(WelcomeSteps.Length - 1, step))] == "profiles") ui.BeginInvoke(new Action(() => { if (openPanel == "welcome") OpenTourProfileBox(reopen); }), DispatcherPriority.ApplicationIdle);
                else tourProfileId = null;
            }
            welcomeStep = Math.Max(0, Math.Min(WelcomeSteps.Length - 1, step));
            HideTip();
            if (openPanel != "welcome")
            {
                if (openPanel != null) ClosePanels();
                TogglePanel("welcome");
            }
            BuildWelcomeStep(null);
        }

        private void WelcomeNext()
        {
            if (WelcomeSteps[welcomeStep] == "look")
            {
                string miss = TourLookMissing();
                if (miss != null) { PlaySound("back"); TourWarnShow(miss); return; }   // (0.99.14) a layout, its design and a preset first
                TourWarnHide();
                TourZoomHide();
            }
            TourLookLeave();
            PlaySound("select");
            if (welcomeStep >= WelcomeSteps.Length - 1) { FinishWelcome(); return; }
            welcomeStep++;
            BuildWelcomeStep(null);
        }

        /// <summary>B: a step back (on the first step, nothing: Skip is there).</summary>
        private void WelcomeBack()
        {
            if (TourProfileBack()) return;   // (0.89: a profile's box closes first)
            if (tourZoomOn && IsTourTiles) { TourZoomHide(); PlaySound("back"); return; }   // (0.99.14) the big photo closes first
            if (TourPickBack()) return;      // (0.99.13: a column back first)
            if (welcomeStep == 0) return;
            TourLookLeave();
            welcomeStep--;
            BuildWelcomeStep(null);
        }

        private string tourLoggedStep;

        /// <summary>(1.0.1) What the home screen is set to, for the log: "row1, preset kx2".</summary>
        private string TourLayoutNow()
        {
            try
            {
                string slot = TourSlotNow();
                var st = slot != null ? SlotState(slot, false) : null;
                return (slot ?? "folders") + ", preset " + (st != null && !string.IsNullOrEmpty(st.Chosen) ? st.Chosen : "default");
            }
            catch (Exception) { return "?"; }
        }

        private void FinishWelcome()
        {
            TourBgMediaStop();
            TourLookLeave();
            tourLoggedStep = null;
            logger.Info("Kippix: getting started finished: " + TourLayoutNow());
            tourProfileId = null;
            tourPicking = false;
            StopTourPad();
            settings.WelcomeDone = true;
            SaveSettings();
            welcomeResume = null;
            if (openPanel == "welcome") ClosePanels();
            NotifyTour();
            // then who's playing, if profiles are on and asked for
            if (ProfilesOn && settings.AskWhoAtStart) ui.BeginInvoke(new Action(OpenWho), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>From the tick: back to the tour after picking a picture or signing in (and this round's other tick work).</summary>
        private void WelcomeResumeTick()
        {
            try { GTick(); } catch (Exception ex) { logger.Warn("Kippix: tick (r12 G): " + ex.Message); }
            // (steamSigningIn: the sign-in page opens a moment after the tour closes, and only if nothing else opened meanwhile)
            // (0.86) a new install's Getting started can't be closed some other way (the Home button, a panel): it comes back
            if (welcomeResume == null && !settings.WelcomeDone && welcomeStarted && IsFullscreen && openPanel == null && !kbOpen
                && !steamSigningIn && !confirmOpen && !gameRunningNow)
            {
                logger.Info("Kippix: getting started isn't finished: back to step " + (welcomeStep + 1));
                OpenWelcomeAt(welcomeStep);
                return;
            }
            // (0.87) Your stores: the Steam store sign-in line keeps up (checked, signed in in the browser)
            if (openPanel == "welcome" && WelcomeSteps[welcomeStep] == "stores")
            {
                var sr = WelcomeRows.FirstOrDefault(r => r.Key == "signin");
                if (sr != null)
                {
                    var tmp = new ObservableCollection<PickRow>();
                    BuildTourStores(tmp);
                    var fresh = tmp.FirstOrDefault(r => r.Key == "signin");
                    if (fresh != null && fresh.Sub != sr.Sub) sr.BecomeLike(fresh);
                }
            }
            if (welcomeResume == null || openPanel != null || kbOpen || steamSigningIn) return;
            int at = Array.IndexOf(WelcomeSteps, welcomeResume);
            welcomeResume = null;
            if (at >= 0) OpenWelcomeAt(at);
        }

        private void NotifyTour()
        {
            foreach (string n in new[] { "WelcomeTitle", "WelcomeText", "WelcomeStepText", "WelcomeImage", "HasWelcomeImage", "TourDots", "HasTourBack", "HasTourSkip",
                                         "TourNextText", "IsTourSeeThrough", "IsTourSolid", "IsTourController", "TourPadText", "TourHint",
                                         "IsTourHello", "IsTourStep", "IsTourProfileOpen", "TourBoxKicker", "TourLookImage", "HasTourLookImage", "WelcomeKicker", "TourTips", "HasTourTips", "TourTipsHeader", "WelcomeLogo", "WelcomeMark", "HasWelcomeLogo", "NoWelcomeLogo", "WelcomeHelloFoot",
                                         "IsTourTiles", "IsTourList", "IsTourCardShown", "TourCardName", "IsTourBgShown", "TourBgBadge", "HasTourBgBadge",
                                         "HasTourCol2", "HasTourCol3", "TourCol2Head", "IsTourZoom", "HasTourWarn", "TourWarn", "IsTourSplit", "IsNotTourSplit" }) Notify(n);
        }

        private void BuildWelcomeStep(string focusKey)
        {
            var rows = WelcomeRows;
            rows.Clear();
            string step = WelcomeSteps[welcomeStep];
            if (step != tourLoggedStep) { tourLoggedStep = step; logger.Info("Kippix: getting started: step " + (welcomeStep + 1) + " (" + step + "), " + TourLayoutNow()); }
            welcomeImage = null;
            if (step != "controller") StopTourPad();
            WelcomeStepText = string.Format(Loc("LOC_KX_TourStep", "Step {0} of {1}"), welcomeStep + 1, WelcomeSteps.Length);
            TourDots = WelcomeSteps.Select((s, i) => new TourDot { IsCurrent = i == welcomeStep, IsDone = i < welcomeStep, Number = (i + 1).ToString(), Name = TourStepName(s) }).ToList();
            TourHint = "";   // (0.87: the LB / RB line is gone)
            TourTips = null;
            WelcomeKicker = "";
            tourCardOn = false;
            tourBgMode = -1;
            TourBgMediaStop();
            switch (step)
            {
                case "hello": BuildTourHello(rows); break;
                case "look": BuildTourLook(rows); break;
                case "style": BuildTourStyle(rows); break;
                case "gamepage": BuildTourGamePage(rows); break;
                case "stores": BuildTourStores(rows); break;
                case "profiles": BuildTourProfiles(rows); break;
                case "controller": BuildTourController(rows); break;
                default: BuildTourDone(rows); break;
            }
            SimplifyTourRows(step, rows);
            TourTileArt(step, rows);
            if (rows == WelcomeRows) TourSplitRows();
            NotifyTour();
            string key = focusKey;
            if (key != null) kbFocusTaken = true;   // after typing a name: this row gets the highlight, not the row the keyboard came from (it's been rebuilt)
            ui.BeginInvoke(new Action(() =>
            {
                if (openPanel != "welcome" || kbOpen) return;   // (the keyboard keeps the highlight while it's up)
                if (key == null && IsTourTiles && TourCol1.Count > 0) { UiTools.FocusItem("KX_TourCol1", TourCol1[0]); return; }   // (0.99.13) Pick a look: the first layout
                var row = key != null ? WelcomeRows.FirstOrDefault(r => r.Key == key) : WelcomeRows.FirstOrDefault(r => !r.IsHeader && !r.IsInfo);
                var wl = UiTools.FindByName(Application.Current.MainWindow, "KX_WelcomeList");
                var sc = key == null && wl != null ? TourScrollerOf(wl) : null;
                if (sc != null) sc.ScrollToTop();   // (0.99.12) every step starts at its top, the highlight on its first row
                if (row != null) TourFocusRow(row);
                if (sc != null)
                {
                    sc.ScrollToTop();
                    var sc2 = sc;
                    ui.BeginInvoke(new Action(() => { try { sc2.ScrollToTop(); } catch (Exception) { } }), DispatcherPriority.ApplicationIdle);   // (0.99.14: the heading over the first row too)
                }
                else if (!UiTools.FocusByNameIfVisible("KX_TourStart") && !UiTools.FocusByNameIfVisible("KX_TourNext")) UiTools.FocusInside("KX_WelcomeView");
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>(0.99.7) Getting started reads simply: the rows' explanations go (the stores' sign-in lines and the picture's stay).</summary>
        private static void SimplifyTourRows(string step, ObservableCollection<PickRow> rows)
        {
            if (step == "stores") return;
            foreach (var r in rows)
            {
                if (r.IsHeader) continue;
                if (step == "profiles" && (r.IsInfo || r.Key == "pic" || r.Key == "name")) continue;   // who's who, and your picture once it's chosen
                r.Sub = null;
            }
        }

        /// <summary>Rows changed where they stand (the highlight stays put), focus on <paramref name="key"/>.</summary>
        private void TourRowsAgain(string key)
        {
            string step = WelcomeSteps[welcomeStep];
            var tmp = new ObservableCollection<PickRow>();
            switch (step)
            {
                case "look": BuildTourLook(tmp); break;
                case "style": BuildTourStyle(tmp); break;
                case "gamepage": BuildTourGamePage(tmp); break;
                case "stores": BuildTourStores(tmp); break;
                case "profiles": BuildTourProfiles(tmp); break;
                case "done": BuildTourDone(tmp); break;
                default: return;
            }
            SimplifyTourRows(step, tmp);
            TourTileArt(step, tmp);
            if (tmp.Count == WelcomeRows.Count)
                for (int i = 0; i < tmp.Count; i++) WelcomeRows[i].BecomeLike(tmp[i]);
            else
            {
                WelcomeRows.Clear();
                foreach (var r in tmp) WelcomeRows.Add(r);
                TourSplitRows();
            }
            NotifyTour();
            ui.BeginInvoke(new Action(() =>
            {
                if (openPanel != "welcome" || kbOpen) return;
                var row = WelcomeRows.FirstOrDefault(r => r.Key == key);
                if (row != null) TourFocusRow(row);
            }), DispatcherPriority.ContextIdle);
        }

        // ---------------- 1. hello ----------------

        private void BuildTourHello(ObservableCollection<PickRow> rows)
        {
            // (0.86) the logo, what Kippix is, three things it's about and Let's start (no rows: the big button has the highlight)
            WelcomeTitle = Loc("LOC_KX_WelcomeHello", "Welcome to Kippix");
            WelcomeText = Loc("LOC_KX_TourHelloText4", "Your games, on the big screen, your way.");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconPalette", Loc("LOC_KX_TourHi1", "Made to be yours"), Loc("LOC_KX_TourHi1Text", "Layouts, colours, tiles and widgets: almost everything can be changed, and saved as your own layout."),
                 "KX_IconController", Loc("LOC_KX_TourHi2", "Built for controllers"), Loc("LOC_KX_TourHi2Text", "Xbox, PlayStation, Switch and Steam Controllers. Hold A on something to move or resize it."),
                 "KX_IconStore", Loc("LOC_KX_TourHi3", "Everything in one place"), Loc("LOC_KX_TourHi3Text", "Every store's games, achievements, screenshots, your wishlist and friends, from the couch."));
        }

        // ---------------- 2. a starting look ----------------

        private void BuildTourLook(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickLook", "YOUR LOOK");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconLayout", Loc("LOC_KX_TourTipLook1", "Change it any time"), Loc("LOC_KX_TourTipLook1Text2", "You'll see it when you're done here. Settings › Home settings › Game cards and layout changes it later."),
                 "KX_IconGrid", Loc("LOC_KX_TourTipLook2", "Board is the most free"), Loc("LOC_KX_TourTipLook2Text", "Tiles of any size: games, widgets and shortcuts. Manual Board adds more pages."),
                 "KX_IconZoomIn", Loc("LOC_KX_TourTipLook4", "A closer look"), Loc("LOC_KX_TourTipLook4Text", "X shows the highlighted one's picture big; X or B closes it."));
            WelcomeTitle = Loc("LOC_KX_TourLook2", "Pick a look");
            if (tourCommitMode < 0) TourLookStart();
            if (tourSnapshot == null) { tourLookShown = null; ShowTourLook("look-" + TourLookKeyNow()); }
            HookTourFocus();
            WelcomeText = Loc("LOC_KX_TourLookText6", "How your games sit on the home screen.");
            BuildTourLookColumns();   // (0.99.13: three columns of picture boxes, not rows)
        }

        // ---------------- (0.89) 3. card glow, colours and background ----------------

        private static readonly int[] TourBgModes = { 0, 4, 5, 2, 6, 1 };

        private void BuildTourStyle(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickStyle", "YOUR STYLE");
            WelcomeTitle = Loc("LOC_KX_TourStyle", "Glow and background");
            WelcomeText = Loc("LOC_KX_TourStyleText2", "How the highlighted game stands out, and what's behind it.");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconPalette", Loc("LOC_KX_TourTipStyle1", "Much more in Theme Studio"), Loc("LOC_KX_TourTipStyle1Text", "Settings › General settings › Look and feel: look packs, glow size and colour, corners, fonts."),
                 "KX_IconGallery", Loc("LOC_KX_TourTipStyle2", "Your own picture"), Loc("LOC_KX_TourTipStyle2Text", "Home settings › Background can use a picture of yours, move it, and fade it."));
            tourLookShown = null;
            ShowTourLook(TourPicNow());
            HookTourFocus();
            TourCardShow(Math.Max(0, Math.Min(3, LookNow.GlowStyle)));
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_TourSecGlow", "CARD GLOW") });
            string[] glowNames = { Loc("LOC_KX_StudioGlowNone", "None"), Loc("LOC_KX_StudioGlowSoft", "Soft"), Loc("LOC_KX_StudioGlowNeon", "Neon"), Loc("LOC_KX_StudioGlowShadow", "Dark shadow") };
            string[] glowSubs = { Loc("LOC_KX_TourGlowNoneSub", "No glow round the highlighted card"), Loc("LOC_KX_TourGlowSoftSub", "A soft light round it"), Loc("LOC_KX_TourGlowNeonSub", "A bright, strong glow"), Loc("LOC_KX_TourGlowShadowSub", "A dark shadow instead of a light") };
            int glowNow = LookNow.GlowStyle;
            for (int i = 0; i < 4; i++)
            {
                int g = i;
                string key = "glow" + i;
                rows.Add(new PickRow
                {
                    Key = key, Icon = "sun", Text = glowNames[i], Sub = glowSubs[i], IsSelected = glowNow == i,
                    Command = new RelayCommand(_ => { LookNow.GlowStyle = g; StudioChanged("cards"); PlaySound("move"); TourRowsAgain(key); TourCardShow(g); })
                });
            }
            rows.Add(new PickRow
            {
                Key = "accent", Icon = "palette", IsSwitch = true, IsSelected = settings.AccentFromGame, Text = Loc("LOC_KX_TourAccent", "Colours from the game's art"),
                Sub = Loc("LOC_KX_TourAccentSub", "The highlight takes a colour from the selected game"),
                Command = new RelayCommand(_ => { AccentFromGame = !AccentFromGame; PlaySound("move"); TourRowsAgain("accent"); TourCardShow(Math.Max(0, Math.Min(3, LookNow.GlowStyle))); })
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_TourSecBg", "BACKGROUND") });
            if (BcTakesBg)
            {
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "bgbc", Text = Loc("LOC_KX_TourBgBc", "BackgroundChanger is your background"), Sub = Loc("LOC_KX_TourBgBcSub", "Its pictures and videos for each game. Settings › Home settings › Background › BackgroundChanger") });
                return;
            }
            foreach (int m in TourBgModes)
            {
                int mode = m;
                string key = "bg" + m;
                int was = settings.HomeBgMode;
                settings.HomeBgMode = m;   // (its name, as the Background setting says it)
                string name = HomeBgText;
                settings.HomeBgMode = was;
                rows.Add(new PickRow
                {
                    Key = key, Icon = m == 1 ? "colour" : m == 6 ? "grid" : m == 4 || m == 2 ? "video" : "gallery", Text = name, Sub = TourBgSub(m), IsSelected = settings.HomeBgMode == m,
                    Command = new RelayCommand(_ => { if (settings.HomeBgMode != mode) SetHomeBgMode(mode); PlaySound("move"); TourRowsAgain(key); })
                });
            }
        }

        private string TourBgSub(int m)
        {
            switch (m)
            {
                case 4: return Loc("LOC_KX_TourBgTrailerSub", "The game's trailer plays behind, muted");
                case 5: return Loc("LOC_KX_TourBgSlidesSub", "The game's pictures, one after another");
                case 2: return Loc("LOC_KX_TourBgPreviewSub", "The game's art, then its trailer after a moment");
                case 6: return Loc("LOC_KX_TourBgTileSub", "The game's cover on a plain colour");
                case 1: return Loc("LOC_KX_TourBgColourSub", "One colour behind everything");
            }
            return Loc("LOC_KX_TourBgArtSub", "The selected game's background picture");
        }

        // ---------------- (0.89) 4. the game page ----------------

        private void BuildTourGamePage(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickGamePage", "THE GAME PAGE");
            WelcomeTitle = Loc("LOC_KX_TourGamePage", "Your game pages");
            WelcomeText = Loc("LOC_KX_TourGamePageText2", "What you see when you open a game.");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconMove", Loc("LOC_KX_TourTipGp1", "Move anything"), Loc("LOC_KX_TourTipGp1Text", "Hold A on the game page to move, resize or hide its parts."),
                 "KX_IconLayout", Loc("LOC_KX_TourTipGp2", "A second page"), Loc("LOC_KX_TourTipGp2Text", "Down from the buttons: play history, achievements, save backups, news and more."));
            tourLookShown = null;
            ShowTourLook(TourPicNow());
            HookTourFocus();
            bool centred = settings.DetLayout == 2;
            rows.Add(new PickRow { Key = "gp0", Icon = "layout", Text = Loc("LOC_KX_DetDesignDefault", "Default"), Sub = Loc("LOC_KX_TourGpDefaultSub2", "The logo and buttons on the left, the cover or trailer on the right"), IsSelected = !centred,
                                   Command = new RelayCommand(_ => { TourGameTopParts(false); SetDetDesign(false); PlaySound("move"); TourRowsAgain("gp0"); }) });
            rows.Add(new PickRow { Key = "gp2", Icon = "layout", Text = Loc("LOC_KX_TourGpAlt", "Alternative (WIP)"), Sub = Loc("LOC_KX_TourGpAltSub", "The cover or trailer at the top, the info centred under it"), IsSelected = centred,
                                   Command = new RelayCommand(_ => { TourGameTopParts(true); SetDetDesign(true); PlaySound("move"); TourRowsAgain("gp2"); }) });
        }

        /// <summary>(0.99.61) The default game page has its logo, buttons and hints placed for the info-on-the-left design:
        /// picking Alternative in the tour puts them back in the middle where that design has them, Default places them again.</summary>
        private void TourGameTopParts(bool centred)
        {
            foreach (var k in new[] { "game.logo", "game.buttons", "game.hints" })
            {
                double[] o;
                if (!centred && HelperSettings.GameDefaultOffsets.TryGetValue(k, out o)) SetMasterOffset(k, o[0], o[1]);
                else SetMasterOffset(k, 0, 0);
            }
            try { ApplyMasterOffsets(); } catch (Exception) { }
        }

        private void TourSetLayout(int mode, int design)
        {
            if (settings.HomeLayoutMode == mode && (mode != 0 || settings.RowDesign == design)) { PlaySound("move"); return; }
            if (boardEditing) BoardEndEdit(false);
            settings.HomeLayoutMode = mode;
            if (mode == 0) settings.RowDesign = design;
            carWhy = "Starting look (tour)";
            if (mode != 0 || design != 0) LeaveExtraRows(false);
            SaveSettings();
            PlaySound("move");
            HomeModeChanged();
            try { NotifyCarouselSettings(); } catch (Exception) { }
            Notify("HomeLayoutText");
        }

        // ---------------- 3. your stores ----------------

        private class TourStore
        {
            public LibraryPlugin Plugin;
            public string Name;
            public int Games;
            public bool? SignedIn;
            public bool Checked;
        }

        private List<TourStore> tourStores;
        private bool tourStoresChecking;

        /// <summary>(1.0.2) More than half the library has no cover (its metadata hasn't been downloaded yet).</summary>
        private bool TourGamesWithoutArt()
        {
            try
            {
                int all = 0, none = 0;
                foreach (var g in api.Database.Games)
                {
                    if (g == null || g.Hidden) continue;
                    all++;
                    if (string.IsNullOrEmpty(g.CoverImage)) none++;
                }
                return all >= 5 && none * 2 > all;
            }
            catch (Exception) { return false; }
        }

        private void BuildTourStores(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickStores", "YOUR STORES");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconStore", Loc("LOC_KX_TourTipStores1", "Your games come from add-ons"), Loc("LOC_KX_TourTipStores1Text", "Playnite's library add-ons bring them in: Steam, Epic, GOG, Xbox and more."),
                 "KX_IconCursor", Loc("LOC_KX_TourTipStores2", "A mouse helps here"), Loc("LOC_KX_TourTipStores2Text", "Signing in to an add-on is easiest with a mouse or keyboard, in its own window."),
                 "KX_IconHeart", Loc("LOC_KX_TourTipStores3", "The Steam store sign-in"), Loc("LOC_KX_TourTipStores3Text", "Only for the Store page's wishlist and prices; your games don't need it."));
            WelcomeTitle = Loc("LOC_KX_TourStores2", "Your stores");
            WelcomeText = Loc("LOC_KX_TourStoresText2", "Sign in to bring in your games.");
            if (tourStores == null) CheckTourStores();
            if (rows == WelcomeRows) CheckSteamSignInSoon(true);   // (0.99.11: Getting started finds out too)
            // (1.0.2) a new library often has no pictures or genres yet (Playnite brings in the names; covers, backgrounds and
            // genres come from Download metadata): without them every layout shows blank cards and More like this is empty
            if (TourGamesWithoutArt())
                rows.Add(new PickRow { Key = "noart", Icon = "info", IsInfo = true, Text = Loc("LOC_KX_TourNoArt", "Most of your games have no pictures yet"),
                                       Sub = Loc("LOC_KX_TourNoArtSub", "In desktop mode: Playnite's menu › Library › Download metadata. Covers, backgrounds and genres come from it (Kippix's layouts and More like this need them).") });
            if (tourStores != null && tourStores.Count == 0)
                rows.Add(new PickRow { Key = "nostores", Icon = "info", IsInfo = true, Text = Loc("LOC_KX_TourNoStores", "No store add-ons yet"), Sub = Loc("LOC_KX_TourNoStoresSub", "Add them in desktop mode: Add-ons › Libraries (Steam, Epic, GOG, Xbox…)") });
            foreach (var s in tourStores ?? new List<TourStore>())
            {
                string state = !s.Checked ? Loc("LOC_KX_TourChecking", "checking…")
                             : s.SignedIn == true ? Loc("LOC_KX_TourSignedIn", "signed in")
                             : s.SignedIn == false ? Loc("LOC_KX_TourSignedOut", "not signed in")
                             : s.Games > 0 ? Loc("LOC_KX_TourConnected", "connected") : Loc("LOC_KX_TourUnknown", "couldn't tell");   // (0.99.11: an add-on that can't say if you're signed in, e.g. Ubisoft Connect: its games came in, so it's connected)
                rows.Add(new PickRow
                {
                    Key = "lib:" + s.Plugin.Id, Icon = s.SignedIn == false ? "flag" : s.SignedIn == true ? "check" : "store", IsSelected = false, IsInfo = true,
                    Text = s.Name, Sub = string.Format(Loc("LOC_KX_TourGames", "{0} games"), s.Games) + "   ·   " + state   // (0.99.10: just a list, as it was)
                });
            }
            rows.Add(new PickRow
            {
                Key = "signin", Icon = "store", Text = Loc("LOC_KX_SteamSignInRow", "Sign in to the Steam store"),
                Sub = Loc("LOC_KX_TourSteamStoreSub", "For the Store's wishlist (separate from the Steam library add-on)") + "   ·   " + SteamSignInStatus,
                // (0.87) the browser opens over Getting started (and closes back onto it); already signed in: the line says so
                Command = new RelayCommand(_ => { try { SteamSignInCommand.Execute(null); } catch (Exception) { } })
            });
            rows.Add(new PickRow { Key = "desktop", Icon = "desktop", Text = Loc("LOC_KX_TourDesktop", "Set up stores in desktop mode"), Sub = Loc("LOC_KX_TourDesktopSub", "Switches to desktop mode; the tour comes back next time fullscreen starts"),
                                   Command = new RelayCommand(_ => { ClosePanels(); RunMainCommand("SwitchToDesktopCommand"); }) });
        }

        /// <summary>The library add-ons, their games, and (off the UI thread, a few seconds each at most) whether they're signed in.</summary>
        private void CheckTourStores()
        {
            var list = new List<TourStore>();
            try
            {
                var counts = new Dictionary<Guid, int>();
                foreach (var g in api.Database.Games) { if (g == null) continue; int n; counts.TryGetValue(g.PluginId, out n); counts[g.PluginId] = n + 1; }
                foreach (var lib in api.Addons.Plugins.OfType<LibraryPlugin>())
                {
                    int n;
                    counts.TryGetValue(lib.Id, out n);
                    string name = lib.Id.ToString();
                    try { name = lib.Name; } catch (Exception) { }
                    list.Add(new TourStore { Plugin = lib, Name = name, Games = n });
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: tour: stores: " + ex.Message); }
            tourStores = list.OrderByDescending(s => s.Games).ThenBy(s => s.Name).ToList();
            if (tourStoresChecking || tourStores.Count == 0) return;
            tourStoresChecking = true;
            var todo = tourStores.ToList();
            KippixShared.SafeWork.Queue(_ =>
            {
                foreach (var s in todo)
                {
                    bool? signed = null;
                    try
                    {
                        var plug = s.Plugin;
                        var task = Task.Factory.StartNew(() => LibrarySignedIn(plug));
                        if (task.Wait(8000)) signed = task.Result;
                    }
                    catch (Exception) { }
                    var ss = s;
                    var sv = signed;
                    OnUi(() =>
                    {
                        ss.SignedIn = sv;
                        ss.Checked = true;
                        if (openPanel == "welcome" && WelcomeSteps[welcomeStep] == "stores") TourStoreRowAgain(ss);
                    });
                }
                OnUi(() => tourStoresChecking = false);
            });
        }

        /// <summary>
        /// (0.99.11) Up / Down in a step's list: the row above / below by its place in the list (WPF's own guess could stop at
        /// the top of what's in view, so Glow and background couldn't get back up to the glows), the heading above a row
        /// scrolled into view with it, and at the top the list goes right back to the start.
        /// </summary>
        private bool TourListStep(FrameworkElement fe, int dir)
        {
            if (!IsTourList || IsTourProfileOpen || fe == null) return false;
            var row = fe.DataContext as PickRow;
            ObservableCollection<PickRow> rowsIn;
            var list = TourListOf(fe, out rowsIn);
            if (row == null || list == null) return false;
            int i = rowsIn.IndexOf(row);
            if (i < 0) return false;
            var scroller = TourScrollerOf(list);
            for (int j = i + dir; j >= 0 && j < rowsIn.Count; j += dir)
            {
                var r = rowsIn[j];
                if (r.IsHeader || !r.IsRow) continue;
                PlaySound("move");
                TourFocusRow(r);
                bool first = !rowsIn.Take(j).Any(x => !x.IsHeader && x.IsRow);
                if (first && scroller != null) scroller.ScrollToTop();
                else TourScrollTo(list, scroller, r, j > 0 && rowsIn[j - 1].IsHeader ? rowsIn[j - 1] : null);
                return true;
            }
            if (dir > 0) return TourToNext(fe, Key.Down);   // the last row: on down to Next (or, without it, WPF's own guess)
            if (scroller != null) scroller.ScrollToTop();
            return true;
        }

        // ---------------- Pick a look's boxes, and every step's Next: the D-pad ----------------

        private static readonly string[] TourColNames = { "KX_TourCol1", "KX_TourCol2", "KX_TourCol3" };
        private FrameworkElement tourNextFrom;   // the box / row the highlight left for Next
        private Key tourNextVia;                 // and the direction it left in

        /// <summary>Up / Down anywhere in Getting started that is ours: a list's rows, Pick a look's boxes, Next.</summary>
        private bool TourUpDown(FrameworkElement fe, int dir)
        {
            Key key = dir < 0 ? Key.Up : Key.Down;
            return TourListStep(fe, dir) || TourTileStep(fe, dir) || TourColStep(fe, key) || TourNextBack(fe, key);
        }

        /// <summary>
        /// Pick a look's boxes: Up / Down along a column, Left / Right to the box beside it, and on to Next from the last box
        /// of a column (Down) or from the last column (Right). The columns had no handler of their own, so WPF's guess (the
        /// nearest control that way) took the highlight to another column's box or to Back, not to Next.
        /// </summary>
        private bool TourColStep(FrameworkElement fe, Key key)
        {
            if (!IsTourTiles || IsTourProfileOpen || fe == null) return false;
            var row = fe.DataContext as PickRow;
            var cols = new[] { TourCol1, TourCol2, TourCol3 };
            int c = row != null ? Array.FindIndex(cols, x => x.Contains(row)) : -1;
            if (c < 0) return false;
            int i = cols[c].IndexOf(row);
            if (key == Key.Up || key == Key.Down)
            {
                i += key == Key.Up ? -1 : 1;
                if (i >= cols[c].Count) return TourToNext(fe, key);
            }
            else
            {
                c += key == Key.Left ? -1 : 1;
                if (c >= cols.Length || (c >= 0 && cols[c].Count == 0)) return TourToNext(fe, key);
                if (c >= 0) i = Math.Min(i, cols[c].Count - 1);
            }
            if (i < 0 || c < 0) return true;   // the top box, the first column: the highlight stays
            PlaySound("move");
            UiTools.FocusItem(TourColNames[c], cols[c][i]);
            return true;
        }

        /// <summary>The highlight onto Next, remembering where it came from. False when Next isn't on screen.</summary>
        private bool TourToNext(FrameworkElement from, Key via)
        {
            if (!UiTools.FocusByNameIfVisible("KX_TourNext")) return false;
            tourNextFrom = from;
            tourNextVia = via;
            PlaySound("move");
            return true;
        }

        /// <summary>
        /// Next (or Skip): Up goes back to the box / row the highlight came from (on Pick a look, otherwise the last box).
        /// Left from Next does the same when the highlight came by Right; if not it goes along the bottom to Skip / Back.
        /// </summary>
        private bool TourNextBack(FrameworkElement fe, Key key)
        {
            if (fe == null || IsTourProfileOpen || (fe.Name != "KX_TourNext" && fe.Name != "KX_TourSkip")) return false;
            if (key != Key.Up && !(key == Key.Left && fe.Name == "KX_TourNext" && tourNextVia == Key.Right && tourNextFrom != null)) return false;
            if (tourNextFrom != null && tourNextFrom.IsVisible)
            {
                PlaySound("move");
                tourNextFrom.BringIntoView();
                Keyboard.Focus(tourNextFrom);
                return true;
            }
            if (!IsTourTiles) return false;
            var cols = new[] { TourCol1, TourCol2, TourCol3 };
            for (int c = cols.Length - 1; c >= 0; c--)
            {
                if (cols[c].Count == 0) continue;
                PlaySound("move");
                UiTools.FocusItem(TourColNames[c], cols[c][cols[c].Count - 1]);
                return true;
            }
            return false;
        }

        /// <summary>The highlight is on one of Pick a look's boxes, or on Next / Skip: the four directions are ours.</summary>
        private bool TourLookNavOurs(FrameworkElement fe)
        {
            if (fe == null || IsTourProfileOpen) return false;
            if (fe.Name == "KX_TourNext" || fe.Name == "KX_TourSkip") return true;
            var row = fe.DataContext as PickRow;
            return row != null && IsTourTiles && (TourCol1.Contains(row) || TourCol2.Contains(row) || TourCol3.Contains(row));
        }

        /// <summary>
        /// (0.99.12) Up / Down on Pick a look's tiles: the nearest tile above / below by where they sit (WPF's own guess stopped
        /// at a preset and couldn't get back up to the layouts once they'd scrolled out of view). Up from the top row scrolls
        /// right back to the top; Down from the last row goes on to Back / Skip / Next.
        /// </summary>
        private bool TourTileStep(FrameworkElement fe, int dir)
        {
            if (!IsTourTiles || IsTourProfileOpen || fe == null) return false;
            var row = fe.DataContext as PickRow;
            var list = UiTools.FindByName(Application.Current.MainWindow, "KX_WelcomeList") as ItemsControl;
            if (row == null || list == null || !list.IsAncestorOf(fe)) return false;
            Rect from = TourTileBounds(list, row);
            if (from.IsEmpty) return false;
            double cx = from.Left + from.Width / 2;
            PickRow best = null;
            double bestDy = double.MaxValue, bestDx = double.MaxValue;
            foreach (var r in WelcomeRows)
            {
                if (r == row || r.IsHeader || !r.IsRow) continue;
                Rect b = TourTileBounds(list, r);
                if (b.IsEmpty) continue;
                double dy = dir < 0 ? from.Top - b.Bottom : b.Top - from.Bottom;
                if (dy < -4) continue;   // not above / below
                double dx = Math.Abs(b.Left + b.Width / 2 - cx);
                if (dy < bestDy - 4 || (Math.Abs(dy - bestDy) <= 4 && dx < bestDx)) { best = r; bestDy = dy; bestDx = dx; }
            }
            var scroller = TourScrollerOf(list);
            if (best == null)
            {
                if (dir > 0) return false;
                if (scroller != null) scroller.ScrollToTop();
                return true;
            }
            PlaySound("move");
            UiTools.FocusItem("KX_WelcomeList", best);
            int at = WelcomeRows.IndexOf(best);
            bool firstGroup = !WelcomeRows.Take(at).Any(x => x.IsHeader);
            if (firstGroup && scroller != null) { scroller.ScrollToTop(); return true; }
            // the heading over that group comes into view with it (when it's just above this row of tiles)
            PickRow head = null;
            for (int k = at - 1; k >= 0; k--)
                if (WelcomeRows[k].IsHeader)
                {
                    if (Math.Abs(TourTileBounds(list, WelcomeRows[k]).Bottom - TourTileBounds(list, best).Top) < 40) head = WelcomeRows[k];
                    break;
                }
            TourScrollTo(list, scroller, best, head);
            return true;
        }

        /// <summary>(0.99.12) Scrolls the step's list so <paramref name="r"/> (and the heading over it) is in view, a little room round it.</summary>
        private static void TourScrollTo(ItemsControl list, ScrollViewer scroller, PickRow r, PickRow head)
        {
            try
            {
                if (scroller == null) return;
                scroller.UpdateLayout();
                var c = list.ItemContainerGenerator.ContainerFromItem(r) as FrameworkElement;
                if (c == null) return;
                double top = c.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y + scroller.VerticalOffset;
                double bottom = top + c.ActualHeight;
                var h = head != null ? list.ItemContainerGenerator.ContainerFromItem(head) as FrameworkElement : null;
                if (h != null) top = Math.Min(top, h.TransformToAncestor(scroller).Transform(new Point(0, 0)).Y + scroller.VerticalOffset);
                double view = scroller.ViewportHeight, at = scroller.VerticalOffset;
                if (top - 16 < at) scroller.ScrollToVerticalOffset(Math.Max(0, top - 16));
                else if (bottom + 24 > at + view) scroller.ScrollToVerticalOffset(Math.Min(scroller.ScrollableHeight, bottom + 24 - view));
            }
            catch (Exception) { }
        }

        private static Rect TourTileBounds(ItemsControl list, PickRow r)
        {
            try
            {
                var c = list.ItemContainerGenerator.ContainerFromItem(r) as FrameworkElement;
                if (c == null || c.ActualWidth <= 0) return Rect.Empty;
                return c.TransformToAncestor(list).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
            }
            catch (Exception) { return Rect.Empty; }
        }

        /// <summary>(0.99.14) The highlight is on a Getting started list (or Pick a look's old tiles): Up / Down are ours.</summary>
        private bool TourNavOurs(FrameworkElement fe)
        {
            if (TourLookNavOurs(fe)) return true;
            if (fe == null || IsTourProfileOpen || !(fe.DataContext is PickRow)) return false;
            ObservableCollection<PickRow> rows;
            return TourListOf(fe, out rows) != null;
        }

        // ---------------- (0.99.15) Glow and background in two columns side by side (no scrolling) ----------------

        private ObservableCollection<PickRow> welcomeRowsA, welcomeRowsB;
        public ObservableCollection<PickRow> WelcomeRowsA { get { return welcomeRowsA ?? (welcomeRowsA = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> WelcomeRowsB { get { return welcomeRowsB ?? (welcomeRowsB = new ObservableCollection<PickRow>()); } }
        public bool IsTourSplit { get { return openPanel == "welcome" && WelcomeSteps[welcomeStep] == "style"; } }
        public bool IsNotTourSplit { get { return !IsTourSplit; } }

        /// <summary>Glow and background: the CARD GLOW rows in one column, BACKGROUND's in the next (the same rows as WelcomeRows).</summary>
        private void TourSplitRows()
        {
            WelcomeRowsA.Clear();
            WelcomeRowsB.Clear();
            if (IsTourSplit)
            {
                int cut = -1;
                for (int i = 1; i < WelcomeRows.Count; i++) if (WelcomeRows[i].IsHeader) { cut = i; break; }
                for (int i = 0; i < WelcomeRows.Count; i++) (cut >= 0 && i >= cut ? WelcomeRowsB : WelcomeRowsA).Add(WelcomeRows[i]);
            }
            Notify("IsTourSplit");
            Notify("IsNotTourSplit");
        }

        /// <summary>The list on screen that the highlight is in, and its rows.</summary>
        private ItemsControl TourListOf(FrameworkElement fe, out ObservableCollection<PickRow> rows)
        {
            rows = null;
            if (fe == null) return null;
            var win = Application.Current.MainWindow;
            foreach (var pair in new[] { Tuple.Create("KX_WelcomeListA", WelcomeRowsA), Tuple.Create("KX_WelcomeListB", WelcomeRowsB), Tuple.Create("KX_WelcomeList", WelcomeRows) })
            {
                var list = UiTools.FindByName(win, pair.Item1) as ItemsControl;
                if (list != null && list.IsVisible && list.IsAncestorOf(fe)) { rows = pair.Item2; return list; }
            }
            return null;
        }

        /// <summary>The highlight onto a step's row, in whichever list shows it.</summary>
        private void TourFocusRow(PickRow row)
        {
            if (row == null) return;
            if (IsTourSplit) UiTools.FocusItem(WelcomeRowsB.Contains(row) ? "KX_WelcomeListB" : "KX_WelcomeListA", row);
            else UiTools.FocusItem("KX_WelcomeList", row);
        }

        private static ScrollViewer TourScrollerOf(DependencyObject d)
        {
            for (var p = d != null ? VisualTreeHelper.GetParent(d) : null; p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer) return (ScrollViewer)p;
            return null;
        }

        /// <summary>(0.99.11) The Steam store sign-in row, once the check is back.</summary>
        private void TourSignInRowAgain()
        {
            if (openPanel != "welcome" || WelcomeSteps[welcomeStep] != "stores") return;
            var row = WelcomeRows.FirstOrDefault(r => r.Key == "signin");
            if (row == null) return;
            var tmp = new ObservableCollection<PickRow>();
            BuildTourStores(tmp);
            var fresh = tmp.FirstOrDefault(r => r.Key == "signin");
            if (fresh != null && fresh.Sub != row.Sub) row.BecomeLike(fresh);
        }

        private void TourStoreRowAgain(TourStore s)
        {
            var row = WelcomeRows.FirstOrDefault(r => r.Key == "lib:" + s.Plugin.Id);
            if (row == null) return;
            var tmp = new ObservableCollection<PickRow>();
            BuildTourStores(tmp);
            var fresh = tmp.FirstOrDefault(r => r.Key == row.Key);
            if (fresh != null) row.BecomeLike(fresh);
        }

        /// <summary>Asks a library add-on's settings whether you're signed in (Playnite's own add-ons have IsUserLoggedIn). Null: it doesn't say.</summary>
        private static bool? LibrarySignedIn(LibraryPlugin lib)
        {
            object s = null;
            try { s = lib.GetSettings(false); } catch (Exception) { }
            if (s == null) return null;
            foreach (string name in new[] { "IsUserLoggedIn", "IsLoggedIn", "IsUserAuthenticated", "IsAuthenticated" })
            {
                try
                {
                    var p = s.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(s, null);
                }
                catch (Exception) { return null; }
            }
            return null;
        }

        // (ShowCursor: declared in ThemeApi.Browser.cs)

        /// <summary>
        /// A library add-on's own settings (Playnite can't open them in fullscreen), in a plain window over Kippix: a mouse,
        /// touch or keyboard is easiest there; with the controller LB / RB move between its controls, A presses, B closes.
        /// </summary>
        private void OpenLibrarySettings(TourStore st)
        {
            var lib = st.Plugin;
            ISettings s = null;
            UserControl view = null;
            try { s = lib.GetSettings(false); view = lib.GetSettingsView(false); } catch (Exception ex) { logger.Warn("Kippix: tour: " + st.Name + " settings: " + ex.Message); }
            if (s == null || view == null)
            {
                PlaySound("back");
                ShowNotice(Loc("LOC_KX_TourStoresNotice", "STORES"), st.Name, Loc("LOC_KX_TourNoSettings", "This add-on's settings can only be opened in desktop mode."), "", null);
                return;
            }
            PlaySound("select");
            int shown = 0;
            bool cursorUp = false;
            try
            {
                s.BeginEdit();
                view.DataContext = s;
                var owner = Application.Current.MainWindow;
                var win = new Window
                {
                    Title = st.Name, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
                    Width = Math.Min(1000, owner.ActualWidth * 0.8), Height = Math.Min(760, owner.ActualHeight * 0.85), ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow
                };
                var dock = new DockPanel { Margin = new Thickness(16) };
                var hint = new TextBlock { Text = Loc("LOC_KX_TourSettingsHint", "Sign in here, then Save. With the controller: LB / RB move, A presses, B closes."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.75 };
                DockPanel.SetDock(hint, Dock.Top);
                dock.Children.Add(hint);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                var save = new Button { Content = Loc("LOC_KX_TourSave", "Save"), MinWidth = 110, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
                var cancel = new Button { Content = Loc("LOC_KX_Cancel", "Cancel"), MinWidth = 110, Padding = new Thickness(12, 5, 12, 5), IsCancel = true };
                bool saved = false;
                save.Click += (a, b) =>
                {
                    List<string> errors;
                    if (!s.VerifySettings(out errors) && errors != null && errors.Count > 0) { MessageBox.Show(win, string.Join("\n", errors), st.Name); return; }
                    s.EndEdit();
                    saved = true;
                    win.Close();
                };
                cancel.Click += (a, b) => win.Close();
                buttons.Children.Add(save);
                buttons.Children.Add(cancel);
                DockPanel.SetDock(buttons, Dock.Bottom);
                dock.Children.Add(buttons);
                dock.Children.Add(new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                win.Content = dock;
                // the controller in this window: LB / RB move, A presses, B closes (its D-pad goes to Playnite's own window)
                win.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((o, e) =>
                {
                    var b = PadButton(e);
                    if (b == ControllerInput.None || IsPadRelease(e)) return;
                    var f = Keyboard.FocusedElement as UIElement;
                    if (b == ControllerInput.RightShoulder || b == ControllerInput.LeftShoulder)
                    {
                        if (f != null) f.MoveFocus(new TraversalRequest(b == ControllerInput.RightShoulder ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous));
                        else win.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                        e.Handled = true;
                    }
                    else if (b == ControllerInput.A)
                    {
                        var bb = f as System.Windows.Controls.Primitives.ButtonBase;
                        if (bb != null) { try { typeof(System.Windows.Controls.Primitives.ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bb, null); } catch (Exception) { } }
                        e.Handled = true;
                    }
                    else if (b == ControllerInput.B) { win.Close(); e.Handled = true; }
                }), true);
                win.Loaded += (a, b) => win.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                // Playnite's "hide the mouse cursor" also hides it here: shown while this is open
                while (ShowCursor(true) < 0 && shown < 20) shown++;
                cursorUp = true;
                win.ShowDialog();
                if (!saved) { try { s.CancelEdit(); } catch (Exception) { } }
            }
            catch (Exception ex) { logger.Warn("Kippix: tour: " + st.Name + " settings window: " + ex.Message); }
            finally
            {
                if (cursorUp) for (int i = 0; i <= shown; i++) ShowCursor(false);   // (every ShowCursor(true) above, taken back)
            }
            // signed in now? look again
            st.Checked = false;
            st.SignedIn = null;
            if (openPanel == "welcome" && WelcomeSteps[welcomeStep] == "stores") TourStoreRowAgain(st);
            var one = st;
            KippixShared.SafeWork.Queue(_ =>
            {
                bool? r = null;
                try { var task = Task.Factory.StartNew(() => LibrarySignedIn(one.Plugin)); if (task.Wait(8000)) r = task.Result; } catch (Exception) { }
                OnUi(() => { one.SignedIn = r; one.Checked = true; if (openPanel == "welcome" && WelcomeSteps[welcomeStep] == "stores") TourStoreRowAgain(one); });
            });
            ui.BeginInvoke(new Action(() => { var row = WelcomeRows.FirstOrDefault(r => r.Key == "lib:" + one.Plugin.Id); if (row != null) TourFocusRow(row); }), DispatcherPriority.ContextIdle);
        }

        // ---------------- 4. you and your profiles ----------------

        private void BuildTourProfiles(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickProfiles", "WHO PLAYS");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconProfile", Loc("LOC_KX_TourTipProf1", "You, top left"), Loc("LOC_KX_TourTipProf1Text", "Your name and picture: press them for your profile, achievements and the Store."),
                 "KX_IconFriends", Loc("LOC_KX_TourTipProf2", "Everyone their own"), Loc("LOC_KX_TourTipProf2Text", "With profiles, each person keeps their own layout, favourites and Steam account."),
                 "KX_IconLock", Loc("LOC_KX_TourTipProf3", "Kids and Guest"), Loc("LOC_KX_TourTipProf3Text", "Kids hides games above an age rating; Guest keeps nothing. A PIN can lock settings."));
            WelcomeTitle = settings.ProfilesEnabled ? Loc("LOC_KX_TourProfiles", "Who plays?") : Loc("LOC_KX_TourYou", "About you");
            WelcomeText = settings.ProfilesEnabled ? Loc("LOC_KX_WelcomeProfilesText2", "Everyone gets their own layout and favourites.") : Loc("LOC_KX_TourYouText2", "Your name and picture.");
            rows.Add(new PickRow
            {
                Key = "name", Icon = "profile", Text = string.Format(Loc("LOC_KX_WelcomeName", "Name: {0}"), ProfileName),
                Sub = Loc("LOC_KX_WelcomeNameSub", ""),
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_RenameAsk2", "Your name"), ProfileName, typed =>
                {
                    string n = (typed ?? "").Trim();
                    if (n.Length > 40) n = n.Substring(0, 40);
                    if (n.Length == 0) { BuildWelcomeStep("name"); return; }   // (r15: a name is required: empty keeps the one you have)
                    settings.ProfileNameOverride = n;
                    var owner = OwnerProfile;
                    if (owner != null && n.Length > 0 && settings.CurrentProfile == owner.Id) owner.Name = n;
                    SaveSettings();
                    NotifyProfile();
                    BuildWelcomeStep("name");
                }, null, () => BuildWelcomeStep("name"), 40))
            });
            rows.Add(new PickRow
            {
                Key = "pic", Icon = "gallery", Text = Loc("LOC_KX_PicChoose", "Choose a picture"),
                Picture = ProfilePicture,   // (0.99.7: the picture itself shows it's chosen)
                Command = new RelayCommand(_ =>
                {
                    PlaySound("select");
                    // (0.90) chosen in the tour's own box, not in the settings
                    StartTourPicker(file => SetProfilePicture(file), () => BuildWelcomeStep("pic"));
                })
            });
            if (!settings.ProfilesEnabled)
            {
                rows.Add(new PickRow { Key = "profoff", Icon = "friends", IsInfo = true, Text = Loc("LOC_KX_TourProfilesOff", "Profiles for everyone who plays"),
                                       Sub = Loc("LOC_KX_TourProfilesOffSub", "Their own layout, favourites and Steam account, with Kids and Guest. Being tested: switch them on in the helper's settings (desktop mode) to try them.") });
                return;
            }
            EnsureProfiles();
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_TourProfilesHeader", "PROFILES") });
            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                var p = settings.Profiles[i];
                string kind = p.Kind == "kids" ? Loc("LOC_KX_KidsSub", "Kids") : "";
                if (i == 0 || p.Id == GuestId)   // yours (and Guest) can't be deleted
                {
                    rows.Add(new PickRow { Key = "p:" + p.Id, Icon = p.Kind == "kids" ? "star" : "profile", Text = p.Name, Sub = i == 0 ? Loc("LOC_KX_TourYours", "You") : kind, IsInfo = true });
                    continue;
                }
                // (0.88) the others open their own settings: PIN, Kids, picture, Steam account… and Delete
                string pid = p.Id;
                rows.Add(new PickRow
                {
                    Key = "p:" + p.Id, Icon = p.Kind == "kids" ? "star" : "profile", Text = p.Name,
                    Sub = (kind.Length > 0 ? kind + "   ·   " : "") + Loc("LOC_KX_TourProfileSettings", "Its PIN, Kids, picture and more, or delete it"),
                    Command = new RelayCommand(_ => { PlaySound("select"); OpenTourProfileBox(pid); })
                });
            }
            rows.Add(new PickRow
            {
                Key = "addperson", Icon = "add", Text = Loc("LOC_KX_AddProfile", "Add a profile"), Sub = Loc("LOC_KX_TourAddSub", "Or skip: you can add people any time in Settings › Profiles"),
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_ProfileNameAsk", "Their name"), "", typed =>
                {
                    string n = (typed ?? "").Trim();
                    if (n.Length == 0) { BuildWelcomeStep("addperson"); return; }
                    var np = new PlayerProfile { Id = NewId(), Name = n.Length > 30 ? n.Substring(0, 30) : n };
                    settings.Profiles.Add(np);
                    SaveSettings();
                    // (0.88) straight on to its settings: a PIN, Kids, a picture… (B comes back here)
                    BuildWelcomeStep("p:" + np.Id);
                    ui.BeginInvoke(new Action(() => OpenTourProfileBox(np.Id)), DispatcherPriority.ApplicationIdle);
                }, null, () => BuildWelcomeStep("addperson"), 30))
            });
        }

        // ---------------- 5. controller check ----------------

        private void BuildTourController(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickPad", "YOUR CONTROLLER");
            Tips(Loc("LOC_KX_TourGoodToKnow", "GOOD TO KNOW"),
                 "KX_IconController", Loc("LOC_KX_TourTipPad1", "Press everything"), Loc("LOC_KX_TourTipPad1Text", "Each button lights up as you press it, so you can see they all work."),
                 "KX_IconSliders", Loc("LOC_KX_TourTipPad2", "Steam Controller"), Loc("LOC_KX_TourTipPad2Text", "Steam's extra keyboard copies of each press are taken out for you."),
                 "KX_IconMenu", Loc("LOC_KX_TourTipPad3", "In a game"), Loc("LOC_KX_TourTipPad3Text", "The Guide button opens Kippix's overlay: achievements, guides, screenshots and more."));
            WelcomeTitle = Loc("LOC_KX_TourPad", "Controller check");
            WelcomeText = Loc("LOC_KX_TourPadText", "Press every button: each lights up (the ones you've tried keep an outline). Here A and B are being tested too: Menu (Start) carries on, B twice goes back.");
            TourHint = Loc("LOC_KX_TourPadHint", "Menu  Next      B twice  Back");
            InitControllerPage();
            Notify("CtrlButtons");
            rows.Add(new PickRow { Key = "padnext", Icon = "play", Text = Loc("LOC_KX_TourPadNext", "All good: next"), Sub = Loc("LOC_KX_TourPadNextSub", "Menu (Start) on the controller, or Enter"), Command = new RelayCommand(_ => WelcomeNext()) });
            TourPadLine();
            if (tourPadTimer == null)
            {
                tourPadTimer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(33) };
                tourPadTimer.Tick += (s, e) =>
                {
                    if (!IsTourController) { StopTourPad(); return; }
                    try { ControllerTick(); } catch (Exception) { }
                    if (DateTime.Now.Second % 2 == 0) TourPadLine();
                };
            }
            tourPadTimer.Start();
        }

        private void TourPadLine()
        {
            string t = CtrlName ?? "";
            try { if (SteamDevice.Connected) t += "\n" + Loc("LOC_KX_TourSteamPad", "Steam Controller found: Steam's extra keyboard copies of each press are taken out for you."); } catch (Exception) { }
            if (t != TourPadText) { TourPadText = t; Notify("TourPadText"); }
        }

        private void StopTourPad()
        {
            if (tourPadTimer != null) tourPadTimer.Stop();
        }

        /// <summary>From OnPadDownEarly / OnPadUpEarly (before any button does its job): the controller check takes every button.</summary>
        private bool TourPadKey(KeyEventArgs e, ControllerInput button)
        {
            if (!IsTourController) return false;
            e.Handled = true;
            padHandled = e;
            if (IsPadRelease(e)) return true;
            if (button == ControllerInput.Start) { WelcomeNext(); return true; }
            if (button == ControllerInput.B)
            {
                if ((DateTime.Now - tourBAt).TotalSeconds < 1.5) { tourBAt = DateTime.MinValue; PlaySound("back"); WelcomeBack(); }
                else tourBAt = DateTime.Now;
            }
            return true;
        }

        /// <summary>From HandlePanelKey: LB / RB step through the tour.</summary>
        private bool WelcomeKey(KeyEventArgs e, int step)
        {
            if (IsTourController && e.GetType().Name == "GameControllerInputEventArgs") { e.Handled = true; return true; }
            if (IsTourProfileOpen) return step != 0 && e.Key != Key.PageUp && e.Key != Key.PageDown;   // (0.89: LB / RB do nothing while a profile's box is open)
            if (step == 0 || IsPadRelease(e)) return false;
            if (e.Key == Key.PageUp || e.Key == Key.PageDown) return false;   // (LT / RT: nothing here)
            if (step < 0) { if (welcomeStep > 0) { PlaySound("back"); WelcomeBack(); } }
            else WelcomeNext();
            return true;
        }

        // ---------------- 6. done ----------------

        private void BuildTourDone(ObservableCollection<PickRow> rows)
        {
            WelcomeKicker = Loc("LOC_KX_TourKickDone", "ALL SET");
            Tips(Loc("LOC_KX_TourWhereThings", "WHERE THINGS ARE"),
                 "KX_IconDots", Loc("LOC_KX_TourGuide1", "⋯ at the top"), Loc("LOC_KX_TourGuide1Text", "Settings & Customisation: every setting, with a search."),
                 "KX_IconMove", Loc("LOC_KX_TourGuide2", "Hold A on a game"), Loc("LOC_KX_TourGuide2Text", "Move or resize anything on the home screen and game page."),
                 "KX_IconProfile", Loc("LOC_KX_TourGuide3", "Your picture, top left"), Loc("LOC_KX_TourGuide3Text", "Your profile, achievements, the Store and more."),
                 "KX_IconController", Loc("LOC_KX_TourGuide4", "Guide button in a game"), Loc("LOC_KX_TourGuide4Text", "The overlay: achievements, guides, Discord and screenshots."),
                 "KX_IconInfo", Loc("LOC_KX_TourGuide5", "Settings › Help"), Loc("LOC_KX_TourGuide5Text", "Take this tour again, or Kippix Doctor if something's wrong."));
            WelcomeTitle = Loc("LOC_KX_WelcomeDone", "You're all set");
            WelcomeText = Loc("LOC_KX_TourDoneText2", "Have fun. Everything can be changed later in Settings.");
            rows.Add(new PickRow { Key = "finish", Icon = "check", Text = Loc("LOC_KX_WelcomeFinish", "Start playing"), Command = new RelayCommand(_ => { PlaySound("select"); FinishWelcome(); }) });
            rows.Add(new PickRow
            {
                Key = "tips", Icon = "info", IsSwitch = true, IsSelected = settings.TipsOn, Text = Loc("LOC_KX_TipsOn", "First-time tips"),
                Sub = Loc("LOC_KX_TipsOnSub", "A small card the first time a screen opens (Free edit, the Store, your profile, folders…)"),
                Command = new RelayCommand(_ => { TipsOn = !TipsOn; PlaySound("move"); TourRowsAgain("tips"); })
            });
            if (!string.IsNullOrEmpty(GuideUrl)) rows.Add(new PickRow { Key = "guide", Icon = "link", Text = Loc("LOC_KX_OpenGuide", "Open the guide"), Command = new RelayCommand(_ => { welcomeResume = null; settings.WelcomeDone = true; SaveSettings(); ClosePanels(); OpenBrowser(GuideUrl); }) });
            if (!string.IsNullOrEmpty(GitHubUrl)) rows.Add(new PickRow { Key = "github", Icon = "link", Text = "GitHub", Sub = GitHubUrl, Command = new RelayCommand(_ => { settings.WelcomeDone = true; SaveSettings(); ClosePanels(); OpenBrowser(GitHubUrl); }) });
        }

        /// <summary>A picture for a step, if the theme has one (Welcome\hello.png, Welcome\tips.png).</summary>
        private void LoadWelcomeImage(string which)
        {
            string dir = Path.Combine(ThemeDir, "Welcome");
            string file = new[] { which + ".png", which + ".jpg", which == "tips" ? "tip1.png" : null }.Where(f => f != null).Select(f => Path.Combine(dir, f)).FirstOrDefault(File.Exists);
            if (file == null) return;
            int at = welcomeStep;
            KippixShared.SafeWork.Queue(_ =>
            {
                var img = Images.LoadUncached(file, 900);
                // (only if that step is still showing)
                if (img != null) OnUi(() => { if (openPanel != "welcome" || welcomeStep != at) return; welcomeImage = img; Notify("WelcomeImage"); Notify("HasWelcomeImage"); });
            });
        }
    }
}
