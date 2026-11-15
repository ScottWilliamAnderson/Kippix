using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// The game page as two pages: the top (logo, info, buttons, description, cover / trailer) and the lower page (play history,
    /// How long to beat, achievements, status, notes, scores, captures, More like this). Any part can be sent to the other page
    /// (Options › Game page › Top and lower page, or X on it in Free move). The lower page's cards can be pressed, or made just for show.
    /// Also: the description from the store (Steam first, then GOG, else Playnite's), and reading it full screen.
    /// </summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // What's where
        // =====================================================================

        private static readonly string[] DetTopKeys = { "logo", "info", "desc", "media", "friends" };
        private static readonly string[] DetSectionKeys = { "history", "hltb", "ach", "status", "notes", "scores", "saves", "mods", "news", "captures", "similar" };
        private static readonly string[] DetAllKeys = DetTopKeys.Concat(DetSectionKeys).ToArray();

        private static string[] DetNames(string key)
        {
            switch (key)
            {
                case "logo": return new[] { "DetLogo" };
                case "info": return new[] { "KX_DetStats", "KX_DetFacts" };
                case "desc": return new[] { "KX_DetDescButton" };
                case "media": return new[] { "CoverColumn" };
                case "friends": return new[] { "KX_DetFriends" };
                case "history": return new[] { "KX_SecHistory" };
                case "hltb": return new[] { "KX_SecHltb" };
                case "ach": return new[] { "KX_SecAch" };
                case "status": return new[] { "KX_SecStatus" };
                case "notes": return new[] { "KX_SecNotes" };
                case "scores": return new[] { "KX_SecScores" };
                case "saves": return new[] { "KX_SecSaves" };
                case "mods": return new[] { "KX_SecMods" };
                case "news": return new[] { "KX_SecNews" };
                case "captures": return new[] { "KX_SecCaptures" };
                case "similar": return new[] { "KX_SecSimilar" };
            }
            return new string[0];
        }

        private string DetPartName(string key)
        {
            switch (key)
            {
                case "logo": return Loc("LOC_KX_PartLogo", "Logo");
                case "info": return Loc("LOC_KX_PartInfo", "Play time and game info");
                case "desc": return Loc("LOC_KX_PartDesc", "Description");
                case "media": return Loc("LOC_KX_PartMedia", "Cover / trailer");
                case "friends": return Loc("LOC_KX_PartFriends", "Friends who play it");
                case "history": return Loc("LOC_KX_SecPlayHistory", "Play history");
                case "hltb": return Loc("LOC_KX_SecHltb", "How long to beat");
                case "ach": return Loc("LOC_KX_SecAchHighlights", "Achievements");
                case "status": return Loc("LOC_KX_SecStatusRating", "Status and rating");
                case "notes": return Loc("LOC_KX_SecNotes", "Notes");
                case "scores": return Loc("LOC_KX_SecScores", "Scores");
                case "saves": return Loc("LOC_KX_SecSaves", "Save backups");
                case "mods": return Loc("LOC_KX_PartMods", "Mods");
                case "news": return Loc("LOC_KX_PartNews", "News");
                case "captures": return Loc("LOC_KX_SecYourCaptures", "Your captures");
                case "similar": return Loc("LOC_KX_SecMoreLikeThis", "More like this");
                case "buttons": return Loc("LOC_KX_CatGameExtras", "Buttons & extras");
            }
            return key;
        }

        /// <summary>
        /// Where a part's own settings are. Most parts have a page of their own ("detpart"); the cover / trailer, friends and the
        /// buttons already have a full page each, so those are their page (one place for each setting).
        /// </summary>
        private static string DetPartPage(string key)
        {
            switch (key)
            {
                case "media": return "gamemedia";
                case "friends": return "gamefriends";
                case "buttons": return "gameextras";
            }
            return "detpart";
        }

        /// <summary>The part whose settings a page is (null: not a part's page).</summary>
        private string PartOfPage(string page)
        {
            switch (page)
            {
                case "detpart": return detPartKey;
                case "gamemedia": return "media";
                case "gamefriends": return "friends";
                case "gameextras": return "buttons";
            }
            return null;
        }

        /// <summary>The part of the game page an element is in (the buttons, the trailer, a card...), or null.</summary>
        private string DetPartAt(DependencyObject d)
        {
            for (int i = 0; d != null && i < 300; i++)
            {
                var fe = d as FrameworkElement;
                if (fe != null && !string.IsNullOrEmpty(fe.Name))
                {
                    if (fe.Name == "DetButtons" || fe.Name == "KX_DetDlc") return "buttons";
                    if (fe.Name == "VideoArea") return "media";
                    if (fe.Name == "PART_ElemGameDetails") return null;
                    foreach (string k in DetAllKeys) if (DetNames(k).Contains(fe.Name)) return k;
                }
                d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        // ---------------- X on the game page: the settings of the part you're on ----------------

        private string xPartPage, xPartKey;   // the page X opened over the game page: B from it goes straight back to the game page
        private string partFrom, partFromKey; // a part's page opened from somewhere other than its usual parent: B goes back there
        private bool xOpening;

        /// <summary>X on the game page: the options open on the settings of the part that's highlighted (all of them, in one place).</summary>
        private void OpenPartSettingsFromGame(string key)
        {
            if (!detailsOpen) return;
            if (openPanel != "options" && ProfileGate("settings", () => OpenPartSettingsFromGame(key))) return;   // a profile whose settings need the PIN
            PlaySound("select");
            string page = key != null ? DetPartPage(key) : (detMoreDown ? "gamesections" : "gamepage");
            gamePageFromDetails = true;   // B from the Game page settings goes back to the game, not to the options
            weOpenedDetails = false;
            xOpening = true;
            try
            {
                if (openPanel != "options") TogglePanel("options");
                detPartKey = key;
                partFrom = null;
                SetOptionsPage(page);
            }
            finally { xOpening = false; }
            xPartPage = page;
            xPartKey = key;
        }

        /// <summary>B on a part's own page: back to where it was opened from (the game page itself, for X). False: B as usual.</summary>
        private bool GamePartBack()
        {
            string key = PartOfPage(optionsPage);
            if (key == null && optionsPage != "gamesections" && optionsPage != "gamepage") return false;
            if (xPartPage != null && optionsPage == xPartPage && xPartKey == key)
            {
                xPartPage = null;
                ClosePanels();
                return true;
            }
            if (key != null && partFrom != null && partFromKey == key)
            {
                string from = partFrom;
                partFrom = null;
                GoBackTo(from, from == "gamesections" ? "part:" + key : key);
                return true;
            }
            return false;
        }

        private static string DetPartIcon(string key)
        {
            switch (key)
            {
                case "logo": return "tag";
                case "info": return "clock";
                case "desc": return "note";
                case "media": return "gallery";
                case "friends": return "profile";
                case "history": return "chart";
                case "hltb": return "hourglass";
                case "ach": return "trophy";
                case "status": return "flag";
                case "notes": return "note";
                case "scores": return "star";
                case "saves": return "upload";
                case "mods": return "sliders";
                case "news": return "news";
                case "captures": return "gallery";
                case "similar": return "layout";
            }
            return "layout";
        }

        private List<string> DetLowerList()
        {
            if (settings.DetLower == null) settings.DetLower = DetSectionKeys.ToList();
            return settings.DetLower;
        }

        private bool DetIsLower(string key) { return DetLowerList().Contains(key); }
        private bool DetIsSection(string key) { return DetSectionKeys.Contains(key); }

        /// <summary>Is this part switched on (its own Show setting)?</summary>
        private bool DetPartOn(string key)
        {
            switch (key)
            {
                case "logo": return settings.DetShowLogo;
                case "info": return settings.DetShowInfo;
                case "desc": return settings.DetShowDescription;
                case "media": return settings.DetArt != 2;
                case "friends": return settings.DetShowFriends;
                case "history": return settings.DetSecHistory;
                case "hltb": return settings.DetSecHltb;
                case "ach": return settings.DetSecAch;
                case "status": return settings.DetSecStatus;
                case "notes": return settings.DetSecNotes;
                case "scores": return settings.DetSecScores;
                case "saves": return settings.DetSecSaves;
                case "mods": return settings.DetSecMods;
                case "news": return settings.DetSecNews;
                case "captures": return settings.DetSecCaptures;
                case "similar": return settings.DetSecSimilar;
            }
            return false;
        }

        private void SetDetPartOn(string key, bool on)
        {
            switch (key)
            {
                case "logo": settings.DetShowLogo = on; break;
                case "info": settings.DetShowInfo = on; break;
                case "desc": settings.DetShowDescription = on; break;
                case "media": settings.DetArt = on ? (settings.DetArt == 2 ? 0 : settings.DetArt) : 2; break;
                case "friends": settings.DetShowFriends = on; break;
                case "history": settings.DetSecHistory = on; break;
                case "hltb": settings.DetSecHltb = on; break;
                case "ach": settings.DetSecAch = on; break;
                case "status": settings.DetSecStatus = on; break;
                case "notes": settings.DetSecNotes = on; break;
                case "scores": settings.DetSecScores = on; break;
                case "saves": settings.DetSecSaves = on; break;
                case "mods": settings.DetSecMods = on; break;
                case "news": settings.DetSecNews = on; break;
                case "captures": settings.DetSecCaptures = on; break;
                case "similar": settings.DetSecSimilar = on; break;
            }
            SaveSettings();
            if (key == "news" && detailsOpen) FillGameNews(SelectedGameNow(), detMoreGen);   // (r12: looked up only while they're on)
            if (key == "mods" && detailsOpen) FillGameMods(SelectedGameNow(), detMoreGen);
            Notify("");   // every game page binding reads again (cheap: the page is small)
            ApplyDetPlacement();
        }

        /// <summary>Is this part showing on the page now (switched on, and with something to show)?</summary>
        private bool DetPartShowing(string key)
        {
            switch (key)
            {
                case "logo": return settings.DetShowLogo;
                case "info": return settings.DetShowInfo;
                case "desc": return settings.DetShowDescription;
                case "media": return settings.DetArt != 2;
                case "friends": return HasDetFriends;
                case "history": return ShowDetHistory;
                case "hltb": return ShowDetHltb;
                case "ach": return ShowDetAch;
                case "status": return ShowDetStatus;
                case "notes": return ShowDetNotesCard;
                case "scores": return ShowDetScores;
                case "saves": return ShowDetSaves;
                case "mods": return ShowDetMods;
                case "news": return ShowDetNews;
                case "captures": return ShowDetCaptures;
                case "similar": return ShowDetSimilar;
            }
            return false;
        }

        // the cards: can the controller stop on them (and press them)?
        private bool DetInteractive(string key) { return settings.DetNoInteract == null || !settings.DetNoInteract.Contains(key); }
        public bool DetIntHistory { get { return DetInteractive("history") && !DetBlank("history"); } }
        public bool DetIntHltb { get { return DetInteractive("hltb") && !DetBlank("hltb"); } }
        public bool DetIntAch { get { return DetInteractive("ach") && !DetBlank("ach"); } }
        public bool DetIntStatus { get { return DetInteractive("status") && !DetBlank("status"); } }
        public bool DetIntNotes { get { return DetInteractive("notes") && !DetBlank("notes"); } }
        public bool DetIntScores { get { return DetInteractive("scores") && !DetBlank("scores"); } }
        public bool DetIntCaptures { get { return DetInteractive("captures") && !DetBlank("captures"); } }
        public bool DetIntSimilar { get { return DetInteractive("similar") && !DetBlank("similar"); } }

        private void NotifyDetInteract()
        {
            foreach (string k in DetSectionKeys) Notify("DetInt" + char.ToUpperInvariant(k[0]) + k.Substring(1));
        }

        // =====================================================================
        // Moving the parts between the two pages
        // =====================================================================

        private readonly Dictionary<FrameworkElement, Panel> detHome = new Dictionary<FrameworkElement, Panel>();
        private readonly Dictionary<Panel, List<UIElement>> detHomeOrder = new Dictionary<Panel, List<UIElement>>();

        private static int DetOrderOf(string name)
        {
            int i = 0;
            foreach (string k in DetAllKeys)
                foreach (string n in DetNames(k)) { if (n == name) return i; i++; }
            return 999;
        }

        /// <summary>Puts each part on its page (called when the page opens and when a setting changes).</summary>
        private void ApplyDetPlacement()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                if (details == null) return;
                var up = UiTools.FindByName(details, "KX_UpHost") as Panel;
                var down = UiTools.FindByName(details, "KX_DownHost") as Panel;
                if (up == null || down == null) return;
                var lower = DetLowerList();
                foreach (string key in DetAllKeys)
                {
                    bool section = DetIsSection(key);
                    bool away = section ? !lower.Contains(key) : lower.Contains(key);   // not on its own page
                    foreach (string name in DetNames(key))
                    {
                        var el = FindDetPart(details, name, up, down);
                        if (el == null) continue;
                        var parent = VisualTreeHelper.GetParent(el) as Panel ?? LogicalTreeHelper.GetParent(el) as Panel;
                        if (parent == null) continue;
                        if (!detHome.ContainsKey(el) && !ReferenceEquals(parent, up) && !ReferenceEquals(parent, down))
                        {
                            detHome[el] = parent;
                            if (!detHomeOrder.ContainsKey(parent)) detHomeOrder[parent] = parent.Children.Cast<UIElement>().ToList();
                        }
                        Panel home;
                        if (!detHome.TryGetValue(el, out home)) continue;
                        Panel target = away ? (section ? up : down) : home;
                        if (ReferenceEquals(parent, target)) continue;
                        parent.Children.Remove(el);
                        InsertInOrder(target, el, home);
                    }
                }
                ApplyDetCardLooks();
                ApplyMasterSizes();
                ApplyDetInfoOrder();
                SyncFreeGaps();
                Notify("ShowDetMore");
                Notify("DetHintMoreBelow");
                if (detMoreDown && !ShowDetMore) DetailsScrollTo(false, false);
            }
            catch (Exception ex) { logger.Warn("Kippix: game page parts: " + ex.Message); }
        }

        /// <summary>A part by name: in its own place, or already moved to the other page.</summary>
        private FrameworkElement FindDetPart(FrameworkElement details, string name, Panel up, Panel down)
        {
            return UiTools.FindByName(details, name) ?? UiTools.FindByName(up, name) ?? UiTools.FindByName(down, name);
        }

        private void InsertInOrder(Panel target, FrameworkElement el, Panel home)
        {
            List<UIElement> order;
            if (ReferenceEquals(target, home) && detHomeOrder.TryGetValue(home, out order))
            {
                // back home: before the first sibling that came after it originally
                int mine = order.IndexOf(el);
                int at = target.Children.Count;
                for (int i = 0; i < target.Children.Count; i++)
                {
                    int theirs = order.IndexOf(target.Children[i]);
                    if (theirs > mine) { at = i; break; }
                }
                target.Children.Insert(at, el);
                return;
            }
            int me = DetOrderOf(el.Name);
            int pos = target.Children.Count;
            for (int i = 0; i < target.Children.Count; i++)
            {
                var other = target.Children[i] as FrameworkElement;
                if (other != null && DetOrderOf(other.Name) > me) { pos = i; break; }
            }
            target.Children.Insert(pos, el);
        }

        // =====================================================================
        // The two pages: scrolling between them
        // =====================================================================

        public bool DetHintMoreBelow { get { return ShowDetMore && !detMoreDown; } }
        public bool DetHintBackUp { get { return detMoreDown; } }

        /// <summary>The lower page is its own page: the top part fades away completely (nothing of it hangs over the lower page).</summary>
        private void FadeTopPage(bool down, bool animate)
        {
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
            var top = details != null ? UiTools.FindByName(details, "PART_ViewHost") : null;
            if (top == null) return;
            top.IsHitTestVisible = !down;
            top.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(down ? 0 : 1, new Duration(TimeSpan.FromMilliseconds(animate ? 260 : 0))));
            Notify("DetHintMoreBelow");
            Notify("DetHintBackUp");
        }

        /// <summary>On the lower page with nothing you can stop on (all just for show): Down scrolls it instead.</summary>
        private bool ScrollLowerPage(int dir)
        {
            var sv = UiTools.FindByName(Application.Current.MainWindow, "KX_DetMore") as ScrollViewer;
            if (sv == null) return false;
            // (0.99.61) no further than the last part you can see: room kept by a part that's moved up in Free move (its place
            // in the list stays where it was), a blank, or the space under the last row isn't scrolled into
            double most = Math.Min(sv.ScrollableHeight, LowerPageMostOffset(sv));
            if (dir > 0 && sv.VerticalOffset >= most - 1) return false;
            double to = sv.VerticalOffset + dir * 260;
            to = dir > 0 ? Math.Min(most, to) : Math.Max(0, Math.Min(sv.ScrollableHeight, to));
            if (Math.Abs(to - sv.VerticalOffset) < 1) return false;
            sv.ScrollToVerticalOffset(to);
            return true;
        }

        /// <summary>(0.99.61) The furthest the lower page needs to scroll: its last part on screen (where it's drawn, Free move and all) in sight.</summary>
        private double LowerPageMostOffset(ScrollViewer sv)
        {
            try
            {
                var content = sv.Content as FrameworkElement;
                if (content == null) return sv.ScrollableHeight;
                var win = Application.Current.MainWindow;
                var parts = new List<FrameworkElement>();
                foreach (string k in DetSectionKeys) foreach (string n in DetNames(k)) { var fe = UiTools.FindByName(content, n); if (fe != null) parts.Add(fe); }
                var down = UiTools.FindByName(content, "KX_DownHost") as Panel;
                if (down != null) foreach (UIElement c in down.Children) { var fe = c as FrameworkElement; if (fe != null) parts.Add(fe); }
                double bottom = 0;
                foreach (var fe in parts)
                {
                    if (!fe.IsVisible || fe.ActualHeight < 1 || fe.Opacity < 0.05) continue;
                    if (detBlankEls.ContainsKey(fe) && !IsMasterMoving) continue;   // (an empty part's kept room: only while editing)
                    if (!content.IsAncestorOf(fe)) continue;
                    var r = fe.TransformToAncestor(content).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                    bottom = Math.Max(bottom, r.Bottom);
                }
                if (bottom <= 0) return sv.ScrollableHeight;
                return Math.Max(0, bottom + 40 - sv.ViewportHeight);
            }
            catch (Exception) { return sv.ScrollableHeight; }
        }

        /// <summary>Somewhere to keep the highlight on the lower page when nothing on it can take it.</summary>
        private void FocusLowerPage()
        {
            UiTools.FocusInside("KX_DetMore");   // the first card; or the empty spot at the end when nothing can be pressed
        }

        // =====================================================================
        // The cards: what pressing them does
        // =====================================================================

        public ICommand DetHistoryCommand { get; private set; }
        public ICommand DetHltbCommand { get; private set; }
        public ICommand DetAchCommand { get; private set; }
        public ICommand DetScoresCommand { get; private set; }
        public ICommand OpenDescReaderCommand { get; private set; }
        public ObservableCollection<PickRow> DetHistoryRows { get; private set; }
        public ObservableCollection<PickRow> GameSectionRows { get; private set; }
        public ObservableCollection<PickRow> DetPartRows { get; private set; }
        public ICommand ShowLowerPageCommand { get; private set; }
        public ICommand CycleDescSourceCommand { get; private set; }
        public bool IsOptionsDetHistory { get { return optionsPage == "dethistory"; } }
        public bool IsOptionsDetPart { get { return optionsPage == "detpart"; } }
        public string DetPartTitle { get { return DetPartName(detPartKey ?? ""); } }
        private string detPartKey;
        /// <summary>A row opening one part's own settings (CommandParameter: logo, info, desc, media, friends, buttons, or a card).</summary>
        public ICommand OpenDetPartCommand { get; private set; }
        /// <summary>"Reset this page" on Game page › Layout ("layout") and › Game media ("media").</summary>
        public ICommand ResetGameSettingsPageCommand { get; private set; }

        private void InitGamePages()
        {
            DetHistoryRows = new ObservableCollection<PickRow>();
            GameSectionRows = new ObservableCollection<PickRow>();
            DetPartRows = new ObservableCollection<PickRow>();
            OpenDetPartCommand = new RelayCommand(p => { string k = p as string; if (!string.IsNullOrEmpty(k)) OpenDetPart(k); });
            ResetGameSettingsPageCommand = new RelayCommand(p => ResetGameSettingsPage(p as string));
            DetHistoryCommand = new RelayCommand(_ => OpenGamePanel("dethistory"));
            DetHltbCommand = new RelayCommand(_ =>
            {
                var g = PanelGame();
                if (g == null) return;
                PlaySound("select");
                OpenBrowser("https://howlongtobeat.com/?q=" + Uri.EscapeDataString(g.Name));
            });
            DetAchCommand = new RelayCommand(_ => { PlaySound("select"); if (OpenGameAchievementsCommand != null && OpenGameAchievementsCommand.CanExecute(null)) OpenGameAchievementsCommand.Execute(null); });
            DetScoresCommand = new RelayCommand(_ =>
            {
                var g = PanelGame();
                if (g == null) return;
                PlaySound("select");
                var link = g.Links != null ? g.Links.FirstOrDefault(l => l != null && !string.IsNullOrEmpty(l.Url) && (l.Url.IndexOf("metacritic.com", StringComparison.OrdinalIgnoreCase) >= 0 || l.Url.IndexOf("opencritic.com", StringComparison.OrdinalIgnoreCase) >= 0)) : null;
                OpenBrowser(link != null ? link.Url : "https://www.metacritic.com/search/" + Uri.EscapeDataString(g.Name) + "/");
            });
            OpenDescReaderCommand = new RelayCommand(_ => OpenDescReader());
            ShowLowerPageCommand = new RelayCommand(_ => { if (detailsOpen && ShowDetMore) { PlaySound("move"); DetailsScrollTo(!detMoreDown, true); } });
            CycleDescSourceCommand = new RelayCommand(_ => { settings.DetDescSource = 1 - settings.DetDescSource; SaveSettings(); PlaySound("move"); Notify("DescSourceText"); var g = SelectedGameNow(); if (g != null) LoadDescription(g); });
        }

        public string DescSourceText { get { return settings.DetDescSource == 1 ? Loc("LOC_KX_DescPlaynite", "Playnite's") : Loc("LOC_KX_DescStore", "The store's (Steam first)"); } }

        /// <summary>The game's sessions, newest first (pressing the Play history card).</summary>
        private void BuildDetHistoryRows()
        {
            DetHistoryRows.Clear();
            var g = PanelGame();
            if (g == null) return;
            var sessions = LoadSessionsFor(g.Id).OrderByDescending(s => s.Start).ToList();
            if (sessions.Count == 0)
            {
                DetHistoryRows.Add(new PickRow { Text = Loc("LOC_KX_NoSessions", "No sessions recorded for this game yet."), Sub = Loc("LOC_KX_NoSessionsSub", "Sessions are kept from when the add-on was installed."), Icon = "info", IsInfo = true });
                return;
            }
            DetHistoryRows.Add(new PickRow
            {
                Text = string.Format(Loc("LOC_KX_SessionsTotal", "{0} sessions, {1} in all"), sessions.Count, HoursShort(sessions.Sum(s => s.Seconds) / 3600.0)),
                Sub = g.Playtime > 0 ? string.Format(Loc("LOC_KX_PlayniteTotal", "Playnite counts {0} altogether"), HoursShort(g.Playtime / 3600.0)) : "",
                Icon = "chart", IsInfo = true
            });
            string lastMonth = null;
            foreach (var s in sessions.Take(200))
            {
                string month = s.Start.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
                if (month != lastMonth) { DetHistoryRows.Add(new PickRow { IsHeader = true, Text = month.ToUpper(CultureInfo.CurrentCulture) }); lastMonth = month; }
                DetHistoryRows.Add(new PickRow
                {
                    Text = s.Start.ToString("dddd d MMMM, HH:mm", CultureInfo.CurrentCulture),
                    Sub = HoursShort(s.Seconds / 3600.0),
                    Icon = "clock", IsInfo = true
                });
            }
        }

        // ---------------- Options › Game page › Top and lower page ----------------

        private void BuildGameSectionRows()
        {
            GameSectionRows.Clear();
            GameSectionRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecTopPage2", "TOP PAGE") });
            foreach (string k in DetAllKeys.Where(k => !DetIsLower(k))) GameSectionRows.Add(PartRow(k));
            if (!settings.DetMoreSections) return;   // no lower page: what's on it doesn't show (the switch above brings it back)
            GameSectionRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecLowerPage2", "LOWER PAGE") });
            var lower = DetAllKeys.Where(DetIsLower).ToList();
            if (lower.Count == 0) GameSectionRows.Add(new PickRow { Text = Loc("LOC_KX_LowerEmpty", "Nothing: everything is on the top page."), Icon = "info", IsInfo = true });
            foreach (string k in lower) GameSectionRows.Add(PartRow(k));
        }

        private PickRow PartRow(string k)
        {
            string key = k;
            var bits = new List<string> { DetPartOn(k) ? Loc("LOC_KX_Shown", "Shown") : Loc("LOC_KX_Hidden", "Hidden") };
            if (DetIsSection(k) && !DetInteractive(k)) bits.Add(Loc("LOC_KX_JustForShow", "just for show"));
            return new PickRow { Text = DetPartName(k), Sub = string.Join("  ·  ", bits), Icon = DetPartIcon(k), Key = "part:" + k, Command = new RelayCommand(_ => OpenDetPart(key)) };
        }

        private void OpenDetPart(string key)
        {
            PlaySound("select");
            string page = DetPartPage(key);
            // opened from somewhere other than its usual parent (Top and lower page for the cards, Game page for the rest): B comes back here
            partFrom = OptionsParent(page) == optionsPage || (page == "detpart" && optionsPage == "gamesections") ? null : optionsPage;
            partFromKey = key;
            detPartKey = key;
            SetOptionsPage(page);
        }

        /// <summary>
        /// One part's page: show it, which page it's on, (cards) whether it can be pressed, its look and size, and the rest of its
        /// own settings. On Game media and Friends (those parts' own pages) only the "which page" row is added.
        /// </summary>
        private void BuildDetPartRows()
        {
            DetPartRows.Clear();
            string k = detPartKey;
            if (k == null) return;
            bool on = DetPartOn(k), lower = DetIsLower(k);
            if (optionsPage == "gamemedia" || optionsPage == "gamefriends")
            {
                if (lower || settings.DetMoreSections)
                {
                    DetPartRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecWhichPage", "WHICH PAGE") });
                    AddPartPageRow(k, lower);
                }
                return;
            }
            DetPartRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_PartShow", "Show it"), IsSelected = on, IsSwitch = true, Icon = DetPartIcon(k), Key = "show",
                Sub = on ? "" : Loc("LOC_KX_PartHiddenSub", "Off: it's hidden, and its settings with it"),
                Command = new RelayCommand(_ => { SetDetPartOn(k, !DetPartOn(k)); PlaySound("move"); RebuildDetPart("show"); })
            });
            // (no lower page: nothing can be sent down to it; a part already there can still come up)
            if (lower || settings.DetMoreSections) AddPartPageRow(k, lower);
            // hidden: the rest of its settings don't apply, so they go too (only the way back to every part stays)
            if (!on) { AddEveryPartRows(); return; }
            if (k == "info") AddInfoLineRows();
            // (r15) no Logo / Description position rows: Free move places both (their saved values still apply, so nothing jumps)
            if (k == "desc")
            {
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_DescSource", "Description"), Sub = DescSourceText, Icon = "note", Key = "source",
                    Command = new RelayCommand(_ => { CycleDescSourceCommand.Execute(null); RebuildDetPart("source"); })
                });
            }
            // (r12) where the News and Mods cards get what they show
            if (k == "news")
                DetPartRows.Add(new PickRow { Text = Loc("LOC_KX_NewsPartInfo", "The game's latest Steam news and patch notes"), Sub = Loc("LOC_KX_NewsPartInfoSub", "Steam games (or games with a link to their Steam page)"), Icon = "info", IsInfo = true, Key = "newsinfo" });
            if (k == "mods")
                DetPartRows.Add(new PickRow { Text = Loc("LOC_KX_ModsPartInfo", "The game on Nexus Mods"), Sub = Loc("LOC_KX_ModsPartInfoSub", "Found by the Nexus Mods checker add-on's link, or in Nexus Mods' list of games (checked once a week). Vortex and Mod Organizer 2 get a button when they're installed"), Icon = "info", IsInfo = true, Key = "modsinfo" });
            if (k == "similar")
            {
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_SimilarCoverSize", "Cover size"), Sub = SimilarSizeText(), Icon = "expand", Key = "coversize",
                    Command = new RelayCommand(_ => { CycleSimilarSize(); RebuildDetPart("coversize"); })
                });
                // the home cards' shapes (Free move › Y on it, then X, changes it too)
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_SimilarCoverShape", "Cover shape"), Sub = SimilarShapeText(), Icon = "layout", Key = "covershape",
                    Command = new RelayCommand(_ => { CycleSimilarShape(); SaveSettings(); RebuildDetPart("covershape"); })
                });
            }
            string sizeKey = k == "desc" ? "game.desc" : CardKeys.Contains("game.sec." + k) ? "game.sec." + k : null;
            bool card = sizeKey != null && sizeKey != "game.desc";
            if (DetIsSection(k) || card) DetPartRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecTheCard", "THE CARD") });
            if (DetIsSection(k))
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_PartInteractive", "Can be selected and pressed"),
                    Sub = Loc("LOC_KX_PartInteractiveSub", "Off: it's just for show, and the controller skips over it"),
                    IsSelected = DetInteractive(k), IsSwitch = true, Icon = "select", Key = "interact",
                    Command = new RelayCommand(_ =>
                    {
                        if (settings.DetNoInteract == null) settings.DetNoInteract = new List<string>();
                        if (!settings.DetNoInteract.Remove(k)) settings.DetNoInteract.Add(k);
                        SaveSettings(); PlaySound("move"); NotifyDetInteract(); RebuildDetPart("interact");
                    })
                });
            // the cards: background, and their size (set in Free move with Y)
            if (card)
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_CardBack", "Background"), Sub = DetCardLookText(k), Icon = "palette", Key = "cardlook",
                    Command = new RelayCommand(_ => { CycleDetCardLook(k); RebuildDetPart("cardlook"); })
                });
            if (card && DetCardLook(k) == 1)
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_CardColour", "Colour"), Sub = ColorName(DetCardColor(k)), Icon = "palette", Key = "cardcolor",
                    Command = new RelayCommand(_ => OpenThemeColor("card:" + k, DetCardColor(k), "detpart", "cardcolor"))
                });
            if (sizeKey != null)
            {
                var sz = MasterSize(sizeKey);
                bool sized = sz[0] > 0 || sz[1] > 0;
                DetPartRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_PartSize", "Size"),
                    Sub = sized ? string.Format(Loc("LOC_KX_PartSizeSet", "{0} × {1}  ·  A puts it back to its own size"), sz[0] > 0 ? Math.Round(sz[0]).ToString() : "auto", sz[1] > 0 ? Math.Round(sz[1]).ToString() : "auto")
                                : Loc("LOC_KX_PartSizeHow", "Its own size. To size it: Free move (hold A), point at it, press Y"),
                    Icon = "expand", Key = "size", IsInfo = !sized,
                    Command = new RelayCommand(_ => { if (!sized) return; SetMasterSize(sizeKey, 0, 0); SaveSettings(); ApplyMasterSizes(); PlaySound("move"); RebuildDetPart("size"); })
                });
            }
            AddEveryPartRows();
        }

        /// <summary>The page row: send it down to the lower page, or bring it up.</summary>
        private void AddPartPageRow(string k, bool lower)
        {
            DetPartRows.Add(new PickRow
            {
                Text = lower ? Loc("LOC_KX_MoveUp", "Move up to the top page") : Loc("LOC_KX_SendDown", "Send further down (the lower page)"),
                Sub = lower ? Loc("LOC_KX_NowLower", "It's on the lower page now") : Loc("LOC_KX_NowTop", "It's on the top page now"),
                Icon = "up", Key = "page",
                Command = new RelayCommand(_ => { MoveDetPart(k); RebuildDetPart("page"); })
            });
        }

        private void AddEveryPartRows()
        {
            DetPartRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecWhatsWhere", "EVERY PART") });
            DetPartRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_GameSectionsTitle", "Top and lower page"), Sub = Loc("LOC_KX_SeeAllParts", "Every part of the game page, which page it's on"), Icon = "layout", Key = "allparts",
                Command = new RelayCommand(_ => SetOptionsPage("gamesections"))
            });
        }

        /// <summary>The list showing the part rows now (the part's own page, or Game media / Friends).</summary>
        private string DetPartListName
        {
            get { return optionsPage == "gamemedia" ? "KX_MediaPartList" : optionsPage == "gamefriends" ? "KX_FriendsPartList" : "KX_DetPartList"; }
        }

        private void RebuildDetPart(string focusKey)
        {
            BuildDetPartRows();
            string list = DetPartListName;
            ui.BeginInvoke(new Action(() => { var r = DetPartRows.FirstOrDefault(x => x.Key == focusKey); if (r != null) UiTools.FocusItem(list, r); }), DispatcherPriority.ContextIdle);
            ShowPartPage();
        }

        // ---------------- Play time and game info: which lines it shows ----------------

        // the lines (the top line of the info: play time and the rest; under it: the game's details)
        // (How long to beat isn't in either: it has its own line in the play info, directly under the play time / last played line,
        // always there, so it isn't in the order; its switches sit at the end of PLAY INFO, where it shows)
        private static readonly string[] DetInfoTopKeys = { "playtime", "last", "ach", "progress", "status", "size", "engine", "players", "friends" };   // (0.99.48 progress: story progress)   // (0.88: feature badges have their own line under How long to beat)
        private static readonly string[] DetInfoFactKeys = { "dev", "pub", "release", "genres", "platform", "source" };

        private bool DetInfoOn(string k) { return settings.DetInfoOff == null || !settings.DetInfoOff.Contains(k); }

        public bool DetInfoShowPlaytime { get { return DetInfoOn("playtime"); } }
        public bool DetInfoShowLast { get { return DetInfoOn("last"); } }
        public bool DetInfoShowAch { get { return DetInfoOn("ach"); } }
        public bool DetInfoShowStatus { get { return DetInfoOn("status"); } }
        public bool DetInfoShowEngine { get { return DetInfoOn("engine"); } }
        // the game's details: switched on, and the game has one (a name with nothing beside it isn't shown; an app with no details shows none)
        public bool DetInfoShowDev { get { return DetInfoOn("dev") && DetHasFact("dev"); } }
        public bool DetInfoShowPub { get { return DetInfoOn("pub") && DetHasFact("pub"); } }
        public bool DetInfoShowRelease { get { return DetInfoOn("release") && DetHasFact("release"); } }
        public bool DetInfoShowGenres { get { return DetInfoOn("genres") && DetHasFact("genres"); } }
        public bool DetInfoShowPlatform { get { return DetInfoOn("platform") && DetHasFact("platform"); } }
        public bool DetInfoShowSource { get { return DetInfoOn("source") && DetHasFact("source"); } }

        /// <summary>Does the game on the page have this detail? (Asked again when the page moves to another game: NotifyDetInfo.)</summary>
        private bool DetHasFact(string k)
        {
            try
            {
                var g = SelectedGameNow();
                if (g == null) return true;
                switch (k)
                {
                    case "dev": return g.Developers != null && g.Developers.Count > 0;
                    case "pub": return g.Publishers != null && g.Publishers.Count > 0;
                    case "release": return g.ReleaseDate != null && g.ReleaseDate.Value.Year > 0;   // (an emulated game can come with a date of "0")
                    case "genres": return g.Genres != null && g.Genres.Count > 0;
                    case "platform": return g.Platforms != null && g.Platforms.Count > 0;
                    case "source": return g.Source != null && !string.IsNullOrEmpty(g.Source.Name);
                }
            }
            catch (Exception) { }
            return true;
        }
        /// <summary>How long to beat in the info (the How long to beat card stays as it is): the times switched on, when the game has them.</summary>
        public bool DetInfoShowHltb { get { return DetInfoOn("hltb") && !string.IsNullOrEmpty(DetInfoHltbText); } }
        /// <summary>(0.88) the "HowLongToBeat" label at the start of that line (off: just the times)</summary>
        public bool DetInfoShowHltbLabel { get { return DetInfoOn("hltblabel"); } }
        public string DetInfoHltbText
        {
            get
            {
                var bits = new List<string>();
                if (DetInfoOn("hltbmain") && HasHltbTime(DetHltbMain)) bits.Add(string.Format(Loc("LOC_KX_InfoHltbMain", "Main story {0}"), DetHltbMain));
                if (DetInfoOn("hltbextras") && HasHltbTime(DetHltbExtras)) bits.Add(string.Format(Loc("LOC_KX_InfoHltbExtras", "Main + extras {0}"), DetHltbExtras));
                if (DetInfoOn("hltbcomplete") && HasHltbTime(DetHltbComplete)) bits.Add(string.Format(Loc("LOC_KX_InfoHltbComplete", "Completionist {0}"), DetHltbComplete));
                return string.Join("  ·  ", bits);
            }
        }
        private static bool HasHltbTime(string t) { return !string.IsNullOrEmpty(t) && t != "—"; }
        private string detInfoSize = "";
        public string DetInfoSizeText { get { return detInfoSize; } }
        public bool DetInfoShowSize { get { return DetInfoOn("size") && !string.IsNullOrEmpty(detInfoSize); } }

        // the sizes worked out from the games' folders (Playnite only knows a game's size after its own "scan install size"; most
        // libraries don't give one, so the line had nothing to show): kept while Playnite runs
        private static readonly Dictionary<Guid, long> installSizes = new Dictionary<Guid, long>();
        private static readonly HashSet<Guid> installSizing = new HashSet<Guid>();

        /// <summary>
        /// The installed size, for the info (when the page opens or moves to another game): Playnite's, else the game's folder
        /// added up off the UI thread (once per game; the line comes in when it's done). "Not installed" for a game that isn't.
        /// </summary>
        private void FillInfoSize(Game g)
        {
            detInfoSize = "";
            if (g == null) return;
            try
            {
                if (!g.IsInstalled) { detInfoSize = Loc("LOC_KX_InfoNotInstalled", "Not installed"); return; }
                long bytes = g.InstallSize.HasValue ? (long)g.InstallSize.Value : 0;
                if (bytes <= 0) lock (installSizes) installSizes.TryGetValue(g.Id, out bytes);
                if (bytes > 0) { detInfoSize = InstallSizeText(bytes); return; }
                if (!DetInfoOn("size")) return;   // (the folder is only added up when the line is on)
                string dir = g.InstallDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                try { dir = api.ExpandGameVariables(g, dir); } catch (Exception) { }
                var id = g.Id;
                lock (installSizes) { if (!installSizing.Add(id)) return; }
                KippixShared.SafeWork.Queue(_ =>
                {
                    long total = 0;
                    try { total = FolderBytes(dir); } catch (Exception) { }
                    lock (installSizes) { installSizing.Remove(id); if (total > 0) installSizes[id] = total; }
                    if (total <= 0) return;
                    OnUi(() =>
                    {
                        var now = SelectedGameNow();
                        if (!detailsOpen || now == null || now.Id != id) return;
                        detInfoSize = InstallSizeText(total);
                        NotifyDetInfo();
                    });
                });
            }
            catch (Exception) { }
        }

        private string InstallSizeText(long bytes)
        {
            return string.Format(Loc("LOC_KX_InfoInstalled", "{0} installed"), SizeText(bytes));
        }

        /// <summary>Every file under the folder (not following links into other folders; a folder it can't read is skipped).</summary>
        private static long FolderBytes(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
            long total = 0;
            int files = 0;
            var todo = new Stack<DirectoryInfo>();
            todo.Push(new DirectoryInfo(root));
            while (todo.Count > 0 && files < 3000000)
            {
                var d = todo.Pop();
                try
                {
                    foreach (var f in d.EnumerateFiles()) { try { total += f.Length; files++; } catch (Exception) { } }
                    foreach (var sub in d.EnumerateDirectories())
                    {
                        try { if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) todo.Push(sub); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
            return total;
        }

        private void NotifyDetInfo()
        {
            foreach (string n in new[] { "DetInfoShowPlaytime", "DetInfoShowLast", "DetInfoShowAch", "DetInfoShowStatus", "DetInfoShowEngine", "DetInfoShowDev",
                                         "DetInfoShowPub", "DetInfoShowRelease", "DetInfoShowGenres", "DetInfoShowPlatform", "DetInfoShowSource",
                                         "DetInfoShowHltb", "DetInfoShowHltbLabel", "DetInfoHltbText", "DetInfoSizeText", "DetInfoShowSize", "DetInfoShowPlayers", "DetPlayersText",
                                         "DetInfoShowFeatures", "DetInfoShowFriends", "DetFriendsLineText", "DetInfoShowProgress", "DetProgressText" })
                Notify(n);
        }

        private string DetInfoName(string k)
        {
            switch (k)
            {
                case "playtime": return Loc("LOC_KX_InfoPlaytime", "Play time");
                case "last": return Loc("LOC_KX_InfoLastPlayed", "Last played");
                case "ach": return Loc("LOC_KX_InfoAch", "Achievements");
                case "status": return Loc("LOC_KX_InfoStatus", "Completion status");
                case "hltb": return Loc("LOC_KX_InfoHltb", "How long to beat");
                case "hltbmain": return Loc("LOC_KX_HltbMain", "Main story");
                case "hltbextras": return Loc("LOC_KX_HltbExtras", "Main + extras");
                case "hltbcomplete": return Loc("LOC_KX_HltbComplete", "Completionist");
                case "size": return Loc("LOC_KX_InfoSize", "Installed size");
                case "engine": return Loc("LOC_KX_InfoEngine", "Engine and NVIDIA Freestyle");
                case "dev": return Loc("LOC_KX_InfoDev", "Developers");
                case "pub": return Loc("LOC_KX_InfoPub", "Publishers");
                case "release": return Loc("LOC_KX_InfoRelease", "Release date");
                case "genres": return Loc("LOC_KX_InfoGenres", "Genres");
                case "platform": return Loc("LOC_KX_InfoPlatform", "Platform");
                case "source": return Loc("LOC_KX_InfoSource", "Source");
                case "players": return Loc("LOC_KX_InfoPlayers", "Players online now");
                case "progress": return Loc("LOC_KX_InfoProgress", "Story progress");
                case "friends": return Loc("LOC_KX_InfoFriends", "Friends playing it");
                case "features": return Loc("LOC_KX_InfoFeatures", "Feature badges");
                case "hltblabel": return Loc("LOC_KX_InfoHltbLabelRow", "Show the \"HowLongToBeat\" label");
            }
            return k;
        }

        private string DetInfoSub(string k)
        {
            switch (k)
            {
                case "ach": return Loc("LOC_KX_InfoAchSub", "Needs the Playnite Achievements add-on");
                case "hltb": return Loc("LOC_KX_InfoHltbSub3", "From the HowLongToBeat add-on, on its own line directly under the play time. The How long to beat card stays too");
                case "players": return Loc("LOC_KX_InfoPlayersSub", "How many people are playing it on Steam right now (Steam games)");
                case "progress": return Loc("LOC_KX_InfoProgressSub", "How far through it you are: from its story achievements, or play time against HowLongToBeat");
                case "friends": return Loc("LOC_KX_InfoFriendsSub", "2 friends playing now, or 3 friends play this (Steam friends with your Steam key, and PlayerActivities)");
                case "features": return Loc("LOC_KX_InfoFeaturesSub2", "Controller, co-op, split screen, online, VR and Steam Deck, on their own line under How long to beat");
                case "hltblabel": return Loc("LOC_KX_InfoHltbLabelSub", "Off: just the times on that line");
                case "size": return Loc("LOC_KX_InfoSizeSub2", "How much room it takes on the drive (Not installed when it isn't)");
                case "engine": return Loc("LOC_KX_InfoEngineSub", "When Game Engine Checker or NVIDIA Freestyle checker has marked the game");
            }
            return "";
        }

        private static string DetInfoIcon(string k)
        {
            switch (k)
            {
                case "playtime": return "clock";
                case "last": case "release": return "calendar";
                case "ach": return "trophy";
                case "status": return "flag";
                case "hltb": case "hltbmain": case "hltbextras": case "hltbcomplete": return "hourglass";
                case "hltblabel": return "tag";
                case "size": return "drive";
                case "engine": return "sliders";
                case "dev": case "pub": return "profile";
                case "genres": return "tag";
                case "platform": return "controller";
                case "source": return "store";
                case "players": return "friends";
                case "friends": return "profile";
                case "progress": return "flag";
                case "features": return "controller";
            }
            return "info";
        }

        private PickRow InfoLineRow(string k)
        {
            string key = k;
            bool moving = detInfoMoving == k;
            string sub = moving ? Loc("LOC_KX_InfoMoving", "Moving: Up / Down to put it in its place, then A") : DetInfoSub(k);
            return new PickRow
            {
                Text = DetInfoName(k), Sub = sub, Icon = moving ? "move" : DetInfoIcon(k), IsSwitch = true, IsSelected = DetInfoOn(k), Key = "info:" + k,
                Command = new RelayCommand(_ =>
                {
                    if (settings.DetInfoOff == null) settings.DetInfoOff = new List<string>();
                    if (!settings.DetInfoOff.Remove(key)) settings.DetInfoOff.Add(key);
                    SaveSettings();
                    PlaySound("move");
                    if (key == "size" && detailsOpen) FillInfoSize(SelectedGameNow());   // (works the size out now, if Playnite hasn't)
                    if ((key == "players" || key == "features") && detailsOpen) FillInfoExtras(SelectedGameNow());   // (looks it up now)
                    if (key == "friends" && detailsOpen) UpdateDetFriends();
                    if (key == "progress" && detailsOpen) DetProgressRefresh();
                    NotifyDetInfo();
                    RebuildDetPart("info:" + key);
                })
            };
        }

        /// <summary>
        /// Play time and game info: a switch for each line it can show (How long to beat's times only while it's on), in the
        /// order they show. X on a line picks it up; Up / Down move it within its block; A (or X, or B) puts it down.
        /// </summary>
        private void AddInfoLineRows()
        {
            if (!infoMoveRebuild) detInfoMoving = null;   // (a page opened afresh: nothing picked up)
            DetPartRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecInfoTop", "PLAY INFO") });
            DetPartRows.Add(new PickRow { Text = Loc("LOC_KX_InfoOrderHint", "To change the order: X on a line, Up / Down, then A"), Icon = "move", IsInfo = true, Key = "infohint" });
            foreach (string k in DetInfoOrdered(DetInfoTopKeys)) DetPartRows.Add(InfoLineRow(k));
            // How long to beat: its own line in the play info, directly under the play time / last played line (so it isn't in
            // the order); its times only while it's on
            DetPartRows.Add(InfoLineRow("hltb"));
            if (DetInfoOn("hltb"))
                foreach (string t in new[] { "hltblabel", "hltbmain", "hltbextras", "hltbcomplete" }) DetPartRows.Add(InfoLineRow(t));
            DetPartRows.Add(InfoLineRow("features"));   // (0.88) their own line, under How long to beat
            DetPartRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecInfoFacts", "GAME DETAILS") });
            foreach (string k in DetInfoOrdered(DetInfoFactKeys)) DetPartRows.Add(InfoLineRow(k));
        }

        // ---------------- Play time and game info: the lines' order ----------------

        private string detInfoMoving;      // the line picked up with X (Up / Down move it)
        private bool infoMoveRebuild;      // the rows are being built again for a move (the line stays picked up)

        /// <summary>One block's lines (the play info, or the game's details) in their order: as kept, then any line not in it yet, as it comes.</summary>
        private List<string> DetInfoOrdered(string[] block)
        {
            var r = new List<string>();
            if (settings.DetInfoOrder != null)
                foreach (string k in settings.DetInfoOrder) if (block.Contains(k) && !r.Contains(k)) r.Add(k);
            foreach (string k in block) if (!r.Contains(k)) r.Add(k);
            return r;
        }

        private bool MoveDetInfo(string k, int dir)
        {
            bool top = DetInfoTopKeys.Contains(k);
            var list = DetInfoOrdered(top ? DetInfoTopKeys : DetInfoFactKeys);
            int i = list.IndexOf(k), j = i + dir;
            if (i < 0 || j < 0 || j >= list.Count) return false;
            list.RemoveAt(i);
            list.Insert(j, k);
            var other = DetInfoOrdered(top ? DetInfoFactKeys : DetInfoTopKeys);
            settings.DetInfoOrder = top ? list.Concat(other).ToList() : other.Concat(list).ToList();
            SaveSettings();
            ApplyDetInfoOrder();
            return true;
        }

        /// <summary>The line the highlight is on (one that can be moved: not How long to beat's times, they go with it).</summary>
        private string FocusedInfoLine()
        {
            var fe = Keyboard.FocusedElement as FrameworkElement;
            var row = fe != null ? fe.DataContext as PickRow : null;
            if (row == null || row.Key == null || !row.Key.StartsWith("info:", StringComparison.Ordinal)) return null;
            string k = row.Key.Substring(5);
            return DetInfoTopKeys.Contains(k) || DetInfoFactKeys.Contains(k) ? k : null;
        }

        private static bool PadPressed(KeyEventArgs e, string name)
        {
            if (!IsPadButton(e, name)) return false;
            var st = e.GetType().GetProperty("ButtonState");
            object v = st != null ? st.GetValue(e, null) : null;
            return v == null || v.ToString() == "Pressed";
        }

        /// <summary>
        /// Keys on Play time and game info's page (from HandlePanelKey): X picks up the highlighted line, then Up / Down move it,
        /// A, X or B put it down (each step is saved straight away). True when handled.
        /// </summary>
        private bool DetInfoMoveKey(KeyEventArgs e, bool xPressed, bool back)
        {
            if (openPanel != "options" || optionsPage != "detpart" || detPartKey != "info") { detInfoMoving = null; return false; }
            if (detInfoMoving == null)
            {
                if (!xPressed) return false;
                string pick = FocusedInfoLine();
                if (pick == null) return false;
                detInfoMoving = pick;
                PlaySound("select");
                InfoMoveRebuild(pick);
                return true;
            }
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                PlaySound(MoveDetInfo(detInfoMoving, e.Key == Key.Up ? -1 : 1) ? "move" : "back");
                InfoMoveRebuild(detInfoMoving);
                return true;
            }
            bool a = e.Key == Key.Enter || e.Key == Key.Space || PadPressed(e, "A");
            if (a || xPressed || back)
            {
                string k = detInfoMoving;
                detInfoMoving = null;
                PlaySound("select");
                InfoMoveRebuild(k);
                return true;
            }
            return e.Key == Key.Left || e.Key == Key.Right;   // (the highlight stays on the line while it's picked up)
        }

        private void InfoMoveRebuild(string k)
        {
            infoMoveRebuild = true;
            try { RebuildDetPart("info:" + k); }
            finally { infoMoveRebuild = false; }
        }

        /// <summary>The game page's info lines in their order (each line is tagged "info:key" in the theme).</summary>
        private void ApplyDetInfoOrder()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                if (details == null) return;
                OrderInfoPanel(UiTools.FindByName(details, "KX_DetStatsLine") as Panel, DetInfoOrdered(DetInfoTopKeys));   // (the play line; How long to beat stays under it)
                OrderInfoPanel(UiTools.FindByName(details, "KX_DetFactsGrid") as Panel, DetInfoOrdered(DetInfoFactKeys));
            }
            catch (Exception ex) { logger.Warn("Kippix: info order: " + ex.Message); }
        }

        private static void OrderInfoPanel(Panel p, List<string> order)
        {
            if (p == null || p.IsItemsHost) return;
            var kids = p.Children.Cast<UIElement>().ToList();
            Func<UIElement, int> rank = c =>
            {
                var fe = c as FrameworkElement;
                string t = fe != null ? fe.Tag as string : null;
                int i = t != null && t.StartsWith("info:", StringComparison.Ordinal) ? order.IndexOf(t.Substring(5)) : -1;
                return i >= 0 ? i : 1000 + kids.IndexOf(c);   // (anything untagged keeps its place after the lines)
            };
            var want = kids.OrderBy(rank).ToList();
            if (want.SequenceEqual(kids)) return;
            p.Children.Clear();
            foreach (var c in want) p.Children.Add(c);
        }

        // ---------------- Reset this page (Layout, Game media) ----------------

        /// <summary>
        /// Layout: what's shown and where (logo, info, description) and every Free move change to those parts.
        /// Game media: cover / trailer, its size and Free move changes, the trailer's options, and back to the top page.
        /// Undo last reset (Layouts &amp; reset) can put it back.
        /// </summary>
        private void ResetGameSettingsPage(string which)
        {
            BackupForUndo();
            var blank = new HelperSettings();
            string[] keys;
            if (which == "media")
            {
                settings.DetArt = blank.DetArt;
                settings.DetMediaSize = blank.DetMediaSize;
                settings.DetMediaOffset = blank.DetMediaOffset;
                ResetGameTrailerOptions(blank);
                DetLowerList().Remove("media");
                keys = new[] { "game.media" };
            }
            else
            {
                settings.DetShowLogo = blank.DetShowLogo;
                settings.DetLogoPosition = blank.DetLogoPosition;
                settings.DetShowInfo = blank.DetShowInfo;
                settings.DetShowDescription = blank.DetShowDescription;
                settings.DetDescPosition = blank.DetDescPosition;
                settings.DetOffset = blank.DetOffset;
                settings.DetLayout = blank.DetLayout;
                keys = new[] { "game.logo", "game.stats", "game.buttons", "game.facts", "game.friends", "game.desc", "game.info" };
            }
            foreach (var k in keys)
            {
                double[] o;
                if (HelperSettings.GameDefaultOffsets.TryGetValue(k, out o)) SetMasterOffset(k, o[0], o[1]);   // (0.99.61: back to how it comes)
                else SetMasterOffset(k, 0, 0);
                SetMasterRotation(k, 0);
                if (settings.MasterSizes != null)
                {
                    settings.MasterSizes.Remove(k);
                    double[] sz;
                    if (HelperSettings.GameDefaultSizes.TryGetValue(k, out sz)) settings.MasterSizes[k] = (double[])sz.Clone();
                    if (HelperSettings.GameDefaultSizes.TryGetValue(k + "#gap", out sz)) settings.MasterSizes[k + "#gap"] = (double[])sz.Clone();
                }
            }
            SaveSettings();
            PlaySound("select");
            Notify("");   // every binding on the page reads again
            RefreshMasterItems();
            NotifyMoved();
            ApplyDetPlacement();
            ApplyMasterOffsets();
            if (which == "media") { try { UpdateTrailer(); } catch (Exception) { } }
            if (optionsPage == "gamemedia") BuildDetPartRows();
        }

        /// <summary>The trailer's options as they come (Game media's reset, and Reset this whole page).</summary>
        private void ResetGameTrailerOptions(HelperSettings blank)
        {
            settings.DetTrailerAutoplay = blank.DetTrailerAutoplay;
            settings.DetTrailerSound = blank.DetTrailerSound;
            settings.DetTrailerLoop = blank.DetTrailerLoop;
            settings.DetTrailerStream = blank.DetTrailerStream;
            settings.DetShowTrailerControls = blank.DetShowTrailerControls;
        }

        private void MoveDetPart(string k)
        {
            var lower = DetLowerList();
            if (!lower.Remove(k)) lower.Add(k);
            // a nudge made on its old page would put it somewhere odd on the new one: it starts in place there
            var names = DetNames(k);
            foreach (var me in MasterElements.Where(m => m.Screen == "game" && m.Names != null && m.Names.Any(n => names.Contains(n))))
                SetMasterOffset(me.Key, 0, 0);
            SaveSettings();
            PlaySound("move");
            ApplyDetPlacement();
            ApplyMasterOffsets();
            ShowPartPage();
        }

        /// <summary>While its settings are open over the game page, the page shows the half the part is on.</summary>
        private void ShowPartPage()
        {
            if (!detailsOpen) return;
            // (r15) Top and lower page: the page follows the highlighted row (GameSectionsFocusChanged); opening it keeps the half it's on
            bool down = optionsPage == "gamesections" ? (detMoreDown && ShowDetMore) : (detPartKey != null && DetIsLower(detPartKey) && ShowDetMore);
            if (down != detMoreDown) DetailsScrollTo(down, true);
        }

        /// <summary>
        /// (r15) From the window's focus handler: on Top and lower page, a row in the TOP PAGE section shows the top page behind,
        /// a row in the LOWER PAGE section the lower page. The rows above both sections leave it where it is.
        /// </summary>
        private void GameSectionsFocusChanged(object newFocus)
        {
            if (!detailsOpen || openPanel != "options" || optionsPage != "gamesections") return;
            var fe = newFocus as FrameworkElement;
            var row = fe != null ? fe.DataContext as PickRow : null;
            if (row == null || row.Key == null || !row.Key.StartsWith("part:", StringComparison.Ordinal) || !GameSectionRows.Contains(row)) return;
            bool down = DetIsLower(row.Key.Substring(5)) && ShowDetMore;
            if (down != detMoreDown) DetailsScrollTo(down, true);
        }

        /// <summary>Called when an options page opens: fills the game page's pages and follows them on the page underneath.</summary>
        private void GamePagesOptionsOpened(string page)
        {
            if (page == "" || page == "gamepage") { xPartPage = null; partFrom = null; }   // back at the top: B works as usual again
            if (page == "detpart" && detPartKey != null && DetPartPage(detPartKey) != "detpart")
            {
                // the cover / trailer, friends and the buttons: their own page has all their settings (Free move's X lands here)
                bool resume = freeResumePage == "detpart";
                SetOptionsPage(DetPartPage(detPartKey));
                if (resume) freeResumePage = optionsPage;   // B still goes back to Free move
                return;
            }
            if (page == "gamemedia" || page == "gamefriends" || page == "gameextras") detPartKey = PartOfPage(page);
            if (page == "gamesections") { BuildGameSectionRows(); ShowPartPage(); }
            else if (page == "detpart" || page == "gamemedia" || page == "gamefriends") { BuildDetPartRows(); Notify("DetPartTitle"); ShowPartPage(); }
            else if (page == "gameextras") ShowPartPage();
            else if (page == "dethistory") BuildDetHistoryRows();
            else if (detailsOpen && detMoreDown && !xOpening && (page == "gamepage" || page == "")) DetailsScrollTo(false, true);
            Notify("IsOptionsDetHistory");
            Notify("IsOptionsDetPart");
        }

        // =====================================================================
        // The description: from the store, and full screen
        // =====================================================================

        private string detDescHtml = "", detDescSource = "";
        private int descGen;
        public string DetDescHtml { get { return detDescHtml; } }
        public string DetDescSourceText
        {
            get
            {
                switch (detDescSource)
                {
                    case "steam": return Loc("LOC_KX_DescFromSteam", "From the Steam store");
                    case "gog": return Loc("LOC_KX_DescFromGog", "From the GOG store");
                }
                return Loc("LOC_KX_DescFromPlaynite", "From Playnite");
            }
        }

        private void SetDescription(string html, string source)
        {
            detDescHtml = html ?? "";
            detDescSource = source ?? "";
            Notify("DetDescHtml");
            Notify("DetDescSourceText");
        }

        /// <summary>Playnite's description straight away; the store's replaces it when it arrives (kept on disk for a month).</summary>
        private void LoadDescription(Game g)
        {
            int gen = ++descGen;
            if (g == null) { SetDescription("", ""); return; }
            string own = g.Description ?? "";
            if (settings.DetDescSource == 1) { SetDescription(own, "playnite"); return; }
            var cached = ReadDescCache(g.Id);
            if (cached != null && cached.Item1 != "none") { SetDescription(cached.Item2, cached.Item1); return; }
            SetDescription(own, "playnite");
            if (cached != null) return;   // looked before and the stores didn't have it: not again for a while
            var game = g;
            KippixShared.SafeWork.Queue(_ =>
            {
                string html = null, source = "none";
                try { html = SteamDescription(game); if (!string.IsNullOrEmpty(html)) source = "steam"; } catch (Exception ex) { logger.Info("Kippix: Steam description: " + ex.Message); }
                if (string.IsNullOrEmpty(html))
                    try { html = GogDescription(game); if (!string.IsNullOrEmpty(html)) source = "gog"; } catch (Exception ex) { logger.Info("Kippix: GOG description: " + ex.Message); }
                WriteDescCache(game.Id, source, html);
                if (source == "none") return;
                OnUi(() => { if (gen == descGen) SetDescription(html, source); });
            });
        }

        private string DescCacheDir { get { return Path.Combine(plugin.GetPluginUserDataPath(), "descriptions"); } }

        private Tuple<string, string> ReadDescCache(Guid id)
        {
            try
            {
                string f = Path.Combine(DescCacheDir, id + ".json");
                if (!File.Exists(f)) return null;
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
                if (d == null) return null;
                string src = d.ContainsKey("source") ? d["source"] as string : null;
                string html = d.ContainsKey("html") ? d["html"] as string : null;
                double days = (DateTime.Now - File.GetLastWriteTime(f)).TotalDays;
                if (src == "none" ? days > 7 : days > 30) return null;   // look again now and then
                return Tuple.Create(src ?? "none", html ?? "");
            }
            catch (Exception) { return null; }
        }

        private void WriteDescCache(Guid id, string source, string html)
        {
            try
            {
                Directory.CreateDirectory(DescCacheDir);
                var d = new Dictionary<string, object> { { "source", source }, { "html", html ?? "" } };
                File.WriteAllText(Path.Combine(DescCacheDir, id + ".json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(d));
            }
            catch (Exception) { }
        }

        /// <summary>Steam's full description (with its pictures): the game's own Steam page, or the Steam game with the same name.</summary>
        private string SteamDescription(Game g)
        {
            string appId = g.PluginId == SteamPluginId ? g.GameId : null;
            if (string.IsNullOrEmpty(appId))
            {
                var root = StoreJson("https://store.steampowered.com/api/storesearch/?l=english&cc=" + Country + "&term=" + Uri.EscapeDataString(g.Name)) as Dictionary<string, object>;
                var items = Dig(root, "items") as IEnumerable;
                string want = NormName(g.Name);
                if (items != null)
                    foreach (var it in items.Cast<object>().OfType<Dictionary<string, object>>())
                        if (NormName(FStr(it, "name")) == want) { appId = FStr(it, "id"); break; }
            }
            if (string.IsNullOrEmpty(appId)) return null;
            var app = StoreJson("https://store.steampowered.com/api/appdetails?l=english&cc=" + Country + "&appids=" + Uri.EscapeDataString(appId)) as Dictionary<string, object>;
            var d = Dig(app, appId, "data") as Dictionary<string, object>;
            if (d == null) return null;
            string html = FStr(d, "detailed_description");
            if (html.Length < 40) html = FStr(d, "about_the_game");
            return CleanStoreHtml(html);
        }

        /// <summary>GOG's description: the game's own GOG product, or the GOG game with the same name.</summary>
        private string GogDescription(Game g)
        {
            string id = g.PluginId == GogPluginId ? g.GameId : null;
            if (string.IsNullOrEmpty(id))
            {
                var root = StoreJson("https://catalog.gog.com/v1/catalog?limit=10&productType=in:game&locale=en-GB&query=like:" + Uri.EscapeDataString(g.Name)) as Dictionary<string, object>;
                var products = Dig(root, "products") as IEnumerable;
                string want = NormName(g.Name);
                if (products != null)
                    foreach (var p in products.Cast<object>().OfType<Dictionary<string, object>>())
                        if (NormName(FStr(p, "title")) == want) { id = FStr(p, "id"); break; }
            }
            if (string.IsNullOrEmpty(id)) return null;
            var d = StoreJson("https://api.gog.com/products/" + Uri.EscapeDataString(id) + "?expand=description&locale=en-US") as Dictionary<string, object>;
            var desc = Dig(d, "description") as Dictionary<string, object>;
            if (desc == null) return null;
            string full = FStr(desc, "full");
            if (full.Length < 40) full = FStr(desc, "lead");
            return CleanStoreHtml(full);
        }

        /// <summary>Store HTML made safe for the page: protocol-relative links fixed, no scripts or video players.</summary>
        private static string CleanStoreHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|iframe|video|object|embed)[^>]*>.*?</\1>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<(video|source|iframe)[^>]*/?>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = html.Replace("src=\"//", "src=\"https://").Replace("href=\"//", "href=\"https://");
            return html.Trim().Length < 40 ? null : html;
        }

        // ---------------- full screen ----------------

        private bool descReaderOpen;
        private DateTime descReaderOpenedAt;

        private static bool IsPadButton(KeyEventArgs e, string name)
        {
            if (e.GetType().Name != "GameControllerInputEventArgs") return false;
            var b = e.GetType().GetProperty("Button");
            object v = b != null ? b.GetValue(e, null) : null;
            return v != null && v.ToString() == name;
        }
        private IInputElement descReaderFocus;
        public bool IsDescReaderOpen { get { return descReaderOpen; } }

        private void OpenDescReader()
        {
            if (!detailsOpen || descReaderOpen) return;
            PlaySound("select");
            descReaderFocus = Keyboard.FocusedElement;
            descReaderOpen = true;
            descReaderOpenedAt = DateTime.Now;
            Notify("IsDescReaderOpen");
            ui.BeginInvoke(new Action(() =>
            {
                var sv = UiTools.FindByName(Application.Current.MainWindow, "KX_DescReaderScroll") as ScrollViewer;
                if (sv != null) { sv.ScrollToTop(); Keyboard.Focus(sv); }
            }), DispatcherPriority.Loaded);
        }

        private void CloseDescReader()
        {
            if (!descReaderOpen) return;
            descReaderOpen = false;
            Notify("IsDescReaderOpen");
            PlaySound("back");
            var back = descReaderFocus as UIElement;
            descReaderFocus = null;
            ui.BeginInvoke(new Action(() => { if (back != null && back.IsVisible) Keyboard.Focus(back); else UiTools.FocusByNameIfVisible("KX_DetDescButton"); }), DispatcherPriority.Loaded);
        }

        /// <summary>Keys while the description is full screen: B closes, Up / Down (and LT / RT) scroll, nothing else gets through.</summary>
        private bool DescReaderKey(KeyEventArgs e)
        {
            if (!descReaderOpen) return false;
            if (!detailsOpen) { descReaderOpen = false; Notify("IsDescReaderOpen"); return false; }
            if (IsPadRelease(e))
            {
                // B let go and the reader is still open (the press went somewhere else): close it now
                if (IsBackKey(e) && (DateTime.Now - descReaderOpenedAt).TotalMilliseconds > 400) CloseDescReader();
                e.Handled = true;
                return true;
            }
            bool settled = (DateTime.Now - descReaderOpenedAt).TotalMilliseconds > 400;   // not the same press that opened it
            if (IsBackKey(e) || (settled && IsPadButton(e, "A"))) { CloseDescReader(); e.Handled = true; return true; }
            var sv = UiTools.FindByName(Application.Current.MainWindow, "KX_DescReaderScroll") as ScrollViewer;
            if (sv != null)
            {
                double step = 0;
                if (e.Key == Key.Down) step = 140;
                else if (e.Key == Key.Up) step = -140;
                else if (e.Key == Key.PageDown) step = sv.ViewportHeight * 0.9;
                else if (e.Key == Key.PageUp) step = -sv.ViewportHeight * 0.9;
                if (step != 0) sv.ScrollToVerticalOffset(Math.Max(0, Math.Min(sv.ScrollableHeight, sv.VerticalOffset + step)));
                if (!sv.IsKeyboardFocused) Keyboard.Focus(sv);
            }
            e.Handled = true;
            return true;
        }
    }
}
