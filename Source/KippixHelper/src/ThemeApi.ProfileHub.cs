using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>A game cover on the profile's Library tab: its picture is only read once its row is on screen.</summary>
    public class HubTile : Observable
    {
        public string Name { get; set; }
        public string Sub { get; set; }
        public bool Installed { get; set; }
        /// <summary>An empty slot that pads the last row out to the others' width (so rows centre and still line up); never drawn or focused.</summary>
        public bool IsFiller { get; set; }
        public ICommand Command { get; set; }
        internal Guid GameId;
        internal string CoverPath;
        internal Action<HubTile> Loader;
        private bool asked;
        private ImageSource cover;
        public ImageSource Cover
        {
            get
            {
                // (WPF reads this only for rows that are drawn: scrolling down asks for the next covers)
                if (cover == null && !asked && Loader != null && !string.IsNullOrEmpty(CoverPath)) { asked = true; Loader(this); }
                return cover;
            }
        }
        internal void SetCover(ImageSource img) { cover = img; Notify("Cover"); }
        internal void SetSub(string sub) { if (sub == Sub) return; Sub = sub; Notify("Sub"); }
    }

    /// <summary>One row of covers on the Library tab (rows are what the list virtualises).</summary>
    public class HubLibRow
    {
        public List<HubTile> Tiles { get; set; }
    }

    /// <summary>
    /// The profile hub (profile menu › Profile, or a shortcut to one of its tabs): a header with your picture, name, what kind of
    /// profile it is, what you played last and your headline numbers, then tabs (LB / RB, or A on a tab): Overview, Library,
    /// Friends, Wishlist (with the release calendar), Collections (and each collection's settings) and Your year.
    /// Each tab is one of the pages (openPanel "page") and is built when it opens; nothing runs while the hub is closed.
    /// What shows follows the profile playing (kids' limits, games hidden for them, the Store taken away).
    /// </summary>
    public partial class ThemeApi
    {
        internal static readonly string[] HubTabKeys = { "overview", "library", "upnext", "friends", "wishlist", "collections", "year" };

        private string hubTab;           // the tab showing (null: the pages aren't on the profile)
        private bool hubTabDirty;        // the last tab changed: saved when the pages close
        private ObservableCollection<PickRow> hubTabs;
        public ObservableCollection<PickRow> HubTabs { get { if (hubTabs == null) hubTabs = new ObservableCollection<PickRow>(); return hubTabs; } }
        public bool IsProfileHub { get { return openPanel == "page" && hubTab != null; } }
        public bool IsNotProfileHub { get { return !IsProfileHub; } }
        public bool IsHubLibrary { get { return IsProfileHub && hubTab == "library"; } }
        public bool IsNotHubLibrary { get { return !IsHubLibrary; } }
        public bool HasHubHint { get { return IsProfileHub && !string.IsNullOrEmpty(PageHint); } }

        /// <summary>The page a tab is (the pages keep their own keys, so everything that checks pageKey still works).</summary>
        private static string HubTabOf(string key)
        {
            switch (key)
            {
                case "profilepage": return "overview";
                case "mylibrary": return "library";
                case "upnext": return "upnext";   // (UpNext.cs)
                case "friends": return "friends";
                case "wishlist":
                case "releases": return "wishlist";
                case "collections": return "collections";
                case "year": return "year";
            }
            return null;
        }

        private string HubPageKey(string tab)
        {
            switch (tab)
            {
                case "library": return "mylibrary";
                case "upnext": return "upnext";
                case "friends": return "friends";
                case "wishlist": return settings.ProfileWishView == "releases" ? "releases" : "wishlist";
                case "collections": return "collections";
                case "year": return "year";
            }
            return "profilepage";
        }

        private string HubTabName(string tab)
        {
            switch (tab)
            {
                case "library": return Loc("LOC_KX_HubTabLibrary", "Library");
                case "upnext": return Loc("LOC_KX_PageUpNext", "Up next");
                case "friends": return Loc("LOC_KX_PageFriends", "Friends");
                case "wishlist": return Loc("LOC_KX_PageWishlist", "Wishlist");
                case "collections": return Loc("LOC_KX_PageCollections", "Collections");
                case "year": return Loc("LOC_KX_HubTabYear", "Your year");
            }
            return Loc("LOC_KX_HubTabOverview", "Overview");
        }

        private static string HubTabIcon(string tab)
        {
            switch (tab)
            {
                case "library": return "grid";
                case "upnext": return "forward";
                case "friends": return "friends";
                case "wishlist": return "heart";
                case "collections": return "shelf";
                case "year": return "chart";
            }
            return "profile";
        }

        private string HubTabBlurb(string tab)
        {
            switch (tab)
            {
                case "library": return Loc("LOC_KX_HubLibBlurb", "Every game you own, whatever the home screen is showing");
                case "upnext": return Loc("LOC_KX_PageUpNextSub", "The games you mean to play next, and ones you could finish soon");
                case "friends": return Loc("LOC_KX_PageFriendsSub", "What your Steam friends are playing");
                case "wishlist": return Loc("LOC_KX_HubWishBlurb", "Your wishlist with today's prices, and the release calendar");
                case "collections": return Loc("LOC_KX_PageCollectionsSub", "Your own shelves of games, any of them can be a home row");
                case "year": return Loc("LOC_KX_PageYearSub", "Your gaming year: top games, hours, achievements");
            }
            return Loc("LOC_KX_HubOverviewBlurb", "This month, what you played last, achievements and captures");
        }

        /// <summary>The Store is taken away from this profile (the wishlist goes with it).</summary>
        private bool HubWishlistAllowed
        {
            get { var p = profileNow; return p == null || !settings.ProfilesEnabled || p.StoreAccess != 2 || unlockedNow.Contains("store"); }
        }

        private bool HubTabShown(string tab)
        {
            if (tab == "overview" || tab == hubTab) return true;
            if (tab == "wishlist" && !HubWishlistAllowed) return false;
            return settings.ProfileHubHidden == null || !settings.ProfileHubHidden.Contains(tab);
        }

        // ---------------- opening, tabs ----------------

        /// <summary>Opens the profile on a tab (sub: the Wishlist tab's view, "wishlist" or "releases").</summary>
        private void OpenProfileHub(string tab, string sub)
        {
            if (tab == null || Array.IndexOf(HubTabKeys, tab) < 0) tab = "overview";
            if (tab == "wishlist" && !HubWishlistAllowed)
            {
                string t = tab, s = sub;
                ProfileGate("store", () => OpenProfileHub(t, s));   // "not in this profile"
                return;
            }
            if (tab == "wishlist" && (sub == "wishlist" || sub == "releases") && settings.ProfileWishView != sub) { settings.ProfileWishView = sub; hubTabDirty = true; }
            if (tab == "overview") profileMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            pageStack.Clear();
            OpenPage(HubPageKey(tab), "", false);
            FocusHubTabSoon();   // (after the panel's own "first thing inside", which would be the first tab)
        }

        /// <summary>The profile menu's Profile row: the tab you used last (or Overview).</summary>
        private void OpenProfileHubFromMenu()
        {
            PlaySound("select");
            ClosePanels();
            string tab = settings.ProfileHubRemember ? settings.ProfileHubTab : "overview";
            if (tab == "wishlist" && !HubWishlistAllowed) tab = "overview";
            if (tab != "overview" && tab != null && settings.ProfileHubHidden != null && settings.ProfileHubHidden.Contains(tab)) tab = "overview";
            pageQuietOpen = true;
            OpenProfileHub(tab, null);
            if (openPanel == "page") { profileReturnKey = "profile"; profileReturnPage = ""; }   // B comes back to the menu, on Profile
        }

        /// <summary>Called by OpenPage after a page is built: the header, the tab strip and the hints follow the page.</summary>
        private void ApplyHubChrome()
        {
            string tab = openPanel == "page" ? HubTabOf(pageKey) : null;
            hubTab = tab;
            if (tab != null)
            {
                EnsureHubHeader();
                SyncHubTabs();
                if (settings.ProfileHubTab != tab) { settings.ProfileHubTab = tab; hubTabDirty = true; }
                // LB / RB are the tabs here: the month and the year step with LT / RT
                PageHint = tab == "overview" ? Loc("LOC_KX_HubHintMonth2", "Another month")
                         : tab == "year" ? Loc("LOC_KX_HubHintYear2", "Another year")
                         : tab == "upnext" ? PageHint   // (its own: open, move, take off)
                         : "";
            }
            if (tab != "library") ClearHubLibrary();
            foreach (string n in new[] { "IsProfileHub", "IsNotProfileHub", "IsHubLibrary", "IsNotHubLibrary", "HasHubHint", "PageHint", "HasHubBanner", "HasHubStats", "HasHubNow" }) Notify(n);
            SetHubTemplates(tab != null);
            NotifyHubShelves();
        }

        private void LeaveHub()
        {
            hubTab = null;
            ClearHubLibrary();
            if (hubTabDirty) { hubTabDirty = false; SaveSettings(); }
            foreach (string n in new[] { "IsProfileHub", "IsNotProfileHub", "IsHubLibrary", "IsNotHubLibrary", "HasHubHint", "HasHubBanner", "HasHubStats", "HasHubNow" }) Notify(n);
            SetHubTemplates(false);
            NotifyHubShelves();
        }

        /// <summary>The tab chips: rebuilt only when which tabs show changes (so the highlight on one stays put).</summary>
        private void SyncHubTabs()
        {
            var want = HubTabKeys.Where(HubTabShown).ToList();
            var tabs = HubTabs;
            if (!tabs.Select(t => t.Key).SequenceEqual(want))
            {
                tabs.Clear();
                foreach (string k in want)
                {
                    string key = k;
                    tabs.Add(new PickRow { Key = key, Text = HubTabName(key), Icon = HubTabIcon(key), Command = new RelayCommand(_ => PickHubTab(key)) });
                }
            }
            foreach (var t in tabs) t.IsSelected = t.Key == hubTab;
        }

        private void PickHubTab(string tab)
        {
            if (tab == hubTab && pageStack.Count == 0) return;
            PlaySound("move");
            pageQuietOpen = true;
            OpenProfileHub(tab, null);
            FocusHubTabSoon();
        }

        /// <summary>LB / RB: the next tab along (round from the end to the start).</summary>
        private void SwitchHubTab(int step)
        {
            var keys = HubTabs.Select(t => t.Key).ToList();
            if (keys.Count < 2) { PlaySound("back"); return; }
            int i = Math.Max(0, keys.IndexOf(hubTab));
            int j = ((i + step) % keys.Count + keys.Count) % keys.Count;
            PlaySound("move");
            pageQuietOpen = true;
            OpenProfileHub(keys[j], null);
            FocusHubTabSoon();
        }

        private void FocusHubTabSoon()
        {
            int gen = pageGen;
            ui.BeginInvoke(new Action(() =>
            {
                if (gen != pageGen || !IsProfileHub) return;
                FocusHubTabNow();
            }), DispatcherPriority.ContextIdle);
        }

        private void FocusHubTabNow()
        {
            var sel = HubTabs.FirstOrDefault(t => t.IsSelected) ?? HubTabs.FirstOrDefault();
            if (sel != null) UiTools.FocusItem("KX_HubTabList", sel);
        }

        /// <summary>FocusPageSoon(null) on the hub: leaves the highlight where it is when it's still on the page, else the tab.</summary>
        private bool HubFocusDefault()
        {
            if (!IsProfileHub) return false;
            var fe = Keyboard.FocusedElement as FrameworkElement;
            var view = UiTools.FindByName(Application.Current.MainWindow, "KX_PagesView");
            if (fe != null && view != null && !ReferenceEquals(fe, view) && fe.IsVisible && view.IsAncestorOf(fe)) return true;
            FocusHubTabNow();
            return true;
        }

        /// <summary>LB / RB change tab; LT / RT (Page Up / Down) step the month or the year. True: handled.</summary>
        private bool HubStepKey(KeyEventArgs e, int step)
        {
            if (!IsProfileHub || step == 0) return false;
            if (IsPadRelease(e)) return true;
            bool trigger = e.Key == Key.PageUp || e.Key == Key.PageDown;
            if (!trigger) { SwitchHubTab(step); return true; }
            if (hubTab == "overview") StepProfileMonth(step);
            else if (hubTab == "year") StepYear(step);
            return true;
        }

        // =====================================================================
        // The header: who you are and your headline numbers
        // =====================================================================

        public string HubKindText { get; private set; }
        public string HubNowText { get; private set; }
        public bool HasHubNow { get { return !string.IsNullOrEmpty(HubNowText) && IsProfileHub; } }   // (0.99.24: on the profile card, every tab)
        private List<PageStat> hubStats = new List<PageStat>();
        public List<PageStat> HubStats { get { return hubStats; } }
        public bool HasHubStats { get { return hubStats != null && hubStats.Count > 0 && IsProfileHub; } }   // (0.99.24: on the profile card, every tab)
        private ImageSource hubBanner;
        public ImageSource HubBanner { get { return hubBanner; } }
        public bool HasHubBanner { get { return hubBanner != null && IsProfileHub; } }   // (0.99.24: the top of the profile card, every tab)
        private DateTime hubHeaderAt = DateTime.MinValue;
        private string hubHeaderFor;
        private int hubHeaderGen;

        /// <summary>A game this profile sees on its profile (kids' limits, games hidden for it; Playnite-hidden ones only for the main profile).</summary>
        private bool HubSees(Game g, bool ownerView)
        {
            if (g == null || !ProfileAllowsGame(g)) return false;
            return ownerView || !g.Hidden;
        }

        private string HubKind()
        {
            if (!ProfilesOn) return Loc("LOC_KX_HubKindSolo", "Your profile on this PC");
            var p = CurrentProfile;
            if (p == null) return "";
            if (p.Id == GuestId) return Loc("LOC_KX_Guest", "Guest") + "   ·   " + Loc("LOC_KX_GuestSub", "Nothing is kept");
            var bits = new List<string>();
            if (p == OwnerProfile) bits.Add(Loc("LOC_KX_HubKindMain", "Main profile"));
            else if (p.Kind == "kids") bits.Add(p.MaxAge > 0 ? string.Format(Loc("LOC_KX_KidsUpTo", "Kids, up to age {0}"), p.MaxAge) : Loc("LOC_KX_KidsSub", "Kids"));
            else bits.Add(Loc("LOC_KX_HubKindPerson", "Profile"));
            if (!string.IsNullOrEmpty(p.SteamPersona)) bits.Add("Steam: " + p.SteamPersona);
            return string.Join("   ·   ", bits.ToArray());
        }

        /// <summary>Works the header out (off the UI thread), at most once a minute or when the profile changes.</summary>
        private void EnsureHubHeader()
        {
            string who = CurrentProfileId + "|" + ProfileName;
            if (who == hubHeaderFor && (DateTime.Now - hubHeaderAt).TotalSeconds < 60) { Notify("HasHubBanner"); return; }
            hubHeaderFor = who;
            hubHeaderAt = DateTime.Now;
            HubKindText = HubKind();
            Notify("HubKindText");
            if (achData.Count == 0 && !achLoading) { try { LoadAchievements(); } catch (Exception) { } }   // (normally already read a few seconds after start-up)
            int gen = ++hubHeaderGen;
            bool ownerView = !ProfilesOn || IsOwnerProfile;
            string profileId = CurrentProfileId;
            var achs = achData;
            var lastMap = profileLastPlayed;
            KippixShared.SafeWork.Queue(_ =>
            {
                int owned = 0, installed = 0, unlocked = 0, perfect = 0;
                long seconds = 0, yearSeconds = 0;
                Game last = null, running = null;
                string runningNames = "";
                DateTime lastAt = DateTime.MinValue;
                string bannerPath = null;
                bool haveAch = false;
                try
                {
                    var games = api.Database.Games.Where(g => HubSees(g, ownerView)).ToList();
                    owned = games.Count;
                    installed = games.Count(g => g.IsInstalled);
                    // (issue #11) several can run at once: the one started last is the one shown, the line names them all
                    var runningAll = games.Where(g => g.IsRunning).ToList();
                    var newest = Running.Newest();
                    running = (newest != null ? runningAll.FirstOrDefault(g => g.Id == newest.Id) : null) ?? runningAll.FirstOrDefault();
                    if (running != null) runningNames = string.Join(", ", new[] { running.Name }.Concat(runningAll.Where(g => g.Id != running.Id).Select(g => g.Name)).ToArray());
                    int year = DateTime.Now.Year;
                    if (ownerView)
                    {
                        foreach (var g in games)
                        {
                            seconds += (long)g.Playtime;
                            if (g.LastActivity.HasValue && g.LastActivity.Value > lastAt) { lastAt = g.LastActivity.Value; last = g; }
                        }
                        string src;
                        yearSeconds = LoadSessions(out src, DateTime.Now.DayOfYear + 2).Where(s => s != null && s.Start.Year == year).Sum(s => s.Seconds);
                    }
                    else
                    {
                        var mine = OwnSessions(3650).Where(s => s.Profile == profileId).ToList();
                        seconds = mine.Sum(s => s.Seconds);
                        yearSeconds = mine.Where(s => s.Start.Year == year).Sum(s => s.Seconds);
                        var byId = games.ToDictionary(g => g.Id.ToString(), g => g);
                        if (lastMap != null)
                            foreach (var kv in lastMap.ToList())
                            {
                                Game g;
                                if (kv.Value > lastAt && byId.TryGetValue(kv.Key, out g)) { lastAt = kv.Value; last = g; }
                            }
                    }
                    if (ownerView && achs != null && achs.Count > 0)
                    {
                        haveAch = true;
                        foreach (var d in achs.ToList())
                        {
                            if (d == null) continue;
                            unlocked += d.Unlocked;
                            if (d.Total > 0 && d.Unlocked >= d.Total) perfect++;
                        }
                    }
                    var show = running ?? last;
                    if (show != null)
                    {
                        string art = !string.IsNullOrEmpty(show.BackgroundImage) ? show.BackgroundImage : show.CoverImage;
                        if (!string.IsNullOrEmpty(art)) bannerPath = art.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? art : api.Database.GetFullFilePath(art);
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: profile header: " + ex.Message); }
                ImageSource banner = null;
                try { if (bannerPath != null) banner = Images.LoadUncached(bannerPath, 1280); } catch (Exception) { }
                OnUi(() =>
                {
                    if (gen != hubHeaderGen) return;
                    var stats = new List<PageStat>
                    {
                        new PageStat { Value = owned.ToString("N0", CultureInfo.CurrentCulture), Label = installed > 0 ? string.Format(Loc("LOC_KX_HubOwned", "games · {0} installed"), installed.ToString("N0", CultureInfo.CurrentCulture)) : Loc("LOC_KX_StatGames", "games") },
                        new PageStat { Value = HoursShort(seconds / 3600.0), Label = Loc("LOC_KX_HubPlayedAll", "played in all") },
                        new PageStat { Value = HoursShort(yearSeconds / 3600.0), Label = string.Format(Loc("LOC_KX_HubPlayedIn", "played in {0}"), DateTime.Now.Year) }
                    };
                    if (haveAch)
                    {
                        stats.Add(new PageStat { Value = unlocked.ToString("N0", CultureInfo.CurrentCulture), Label = Loc("LOC_KX_StatAchievements", "achievements") });
                        stats.Add(new PageStat { Value = perfect.ToString("N0", CultureInfo.CurrentCulture), Label = Loc("LOC_KX_HubPerfect", "perfect games") });
                    }
                    hubStats = stats;
                    HubNowText = running != null ? string.Format(Loc("LOC_KX_HubPlayingNow", "Playing {0} now"), runningNames)
                               : last != null ? string.Format(Loc("LOC_KX_HubLastPlayed", "Last played {0}   ·   {1}"), last.Name, AgoText(lastAt))
                               : "";
                    hubBanner = banner;
                    foreach (string n in new[] { "HubStats", "HasHubStats", "HubNowText", "HasHubNow", "HubBanner", "HasHubBanner" }) Notify(n);
                });
            });
        }

        // =====================================================================
        // Library: every game you own (that this profile may see), whatever the home screen's filters
        // =====================================================================

        private class HubLibEntry
        {
            public HubTile Tile;
            public string Name, Source;
            public bool Installed, Favorite;
            public DateTime Last, Added;
            public long Seconds;
        }

        private List<HubLibEntry> hubLibAll;
        private List<HubLibRow> hubLibRows = new List<HubLibRow>();
        public List<HubLibRow> HubLibRows { get { return hubLibRows; } }
        public string HubLibStatus { get; private set; }
        public bool HasHubLibStatus { get { return !string.IsNullOrEmpty(HubLibStatus); } }
        private int hubLibGen;
        private readonly Stack<Tuple<HubTile, int>> hubCoverQueue = new Stack<Tuple<HubTile, int>>();
        private bool hubCoverBusy;

        private string HubLibSortText(int i)
        {
            switch (i)
            {
                case 1: return Loc("LOC_KX_HubSortRecent", "Recently played");
                case 2: return Loc("LOC_KX_HubSortMost", "Most played");
                case 3: return Loc("LOC_KX_HubSortAdded", "Recently added");
            }
            return Loc("LOC_KX_HubSortName", "Name");
        }

        private string HubLibShowText()
        {
            switch (settings.ProfileLibShow)
            {
                case 1: return Loc("LOC_KX_HubShowInstalled", "Showing: installed");
                case 2: return Loc("LOC_KX_HubShowNotInstalled", "Showing: not installed");
            }
            return Loc("LOC_KX_HubShowAll", "Showing: every game");
        }

        private void BuildHubLibrary()
        {
            PageSub = Loc("LOC_KX_HubLibBlurb", "Every game you own, whatever the home screen is showing");
            // two buttons only (A steps through the choices): the covers are what the tab is about
            // (0.99.17) a search (results as you type), the tabs (All games first), then the sort
            // (0.99.25) four buttons: Search, Show, Store, Sort; A on Show / Store / Sort puts its choices in their place (B goes back)
            libSearch = "";
            libTab = "all";
            libStore = null;
            libSources = new List<string>();
            libChipMode = null;
            BuildLibChips(null);
            ClearHubLibrary();
            SetHubLibStatus(Loc("LOC_KX_HubLibLoading", "Getting your games…"));
            int gen = pageGen;
            bool ownerView = !ProfilesOn || IsOwnerProfile;
            string pid = CurrentProfileId;
            var lastMap = profileLastPlayed;
            KippixShared.SafeWork.Queue(_ =>
            {
                var found = new List<Tuple<Game, string, DateTime, long>>();   // game, cover file, last played, seconds (this profile's)
                try
                {
                    Dictionary<string, long> mine = null;
                    if (!ownerView)
                        mine = OwnSessions(3650).Where(s => s.Profile == pid && !string.IsNullOrEmpty(s.GameId))
                                                .GroupBy(s => s.GameId).ToDictionary(g => g.Key, g => g.Sum(s => s.Seconds));
                    foreach (var g in api.Database.Games.ToList())
                    {
                        if (gen != pageGen) return;
                        if (!HubSees(g, ownerView)) continue;
                        string cover = null;
                        if (!string.IsNullOrEmpty(g.CoverImage))
                            cover = g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? g.CoverImage : api.Database.GetFullFilePath(g.CoverImage);
                        DateTime last = DateTime.MinValue;
                        long secs = 0;
                        string id = g.Id.ToString();
                        if (ownerView) { last = g.LastActivity ?? DateTime.MinValue; secs = (long)g.Playtime; }
                        else
                        {
                            DateTime d;
                            if (lastMap != null && lastMap.TryGetValue(id, out d)) last = d;
                            long v;
                            if (mine != null && mine.TryGetValue(id, out v)) secs = v;
                        }
                        found.Add(Tuple.Create(g, cover, last, secs));
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: profile library: " + ex.Message); }
                OnUi(() =>
                {
                    if (gen != pageGen || pageKey != "mylibrary") return;
                    hubLibAll = found.Select(f => MakeHubEntry(f.Item1, f.Item2, f.Item3, f.Item4)).ToList();
                    LibSourceTabs();   // the stores your games come from (the Store button's choices)
                    ApplyHubLibrary();
                });
            });
        }

        private HubLibEntry MakeHubEntry(Game g, string cover, DateTime last, long secs)
        {
            var id = g.Id;
            var tile = new HubTile
            {
                Name = g.Name ?? "", GameId = id, CoverPath = cover, Installed = g.IsInstalled, Loader = QueueHubCover,
                Command = new RelayCommand(_ => OpenGameFromPage(id))
            };
            return new HubLibEntry
            {
                Tile = tile, Name = g.Name ?? "", Source = g.Source != null ? g.Source.Name ?? "" : "", Installed = g.IsInstalled,
                Last = last, Added = g.Added ?? DateTime.MinValue, Seconds = secs, Favorite = g.Favorite
            };
        }

        /// <summary>Sorts and filters what was read, and lays it out in rows as wide as the screen.</summary>
        private void ApplyHubLibrary()
        {
            if (hubLibAll == null) return;
            IEnumerable<HubLibEntry> list = LibFilter(hubLibAll);
            var byName = StringComparer.CurrentCultureIgnoreCase;
            switch (settings.ProfileLibSort)
            {
                case 1: list = list.OrderByDescending(e => e.Last).ThenBy(e => e.Name, byName); break;
                case 2: list = list.OrderByDescending(e => e.Seconds).ThenBy(e => e.Name, byName); break;
                case 3: list = list.OrderByDescending(e => e.Added).ThenBy(e => e.Name, byName); break;
                default: list = list.OrderBy(e => e.Name, byName); break;
            }
            var shown = list.ToList();
            foreach (var e in shown)
            {
                string sub;
                switch (settings.ProfileLibSort)
                {
                    case 1: sub = e.Last > DateTime.MinValue ? AgoText(e.Last) : Loc("LOC_KX_NeverPlayed", "Never played"); break;
                    case 2: sub = e.Seconds > 0 ? HoursShort(e.Seconds / 3600.0) : Loc("LOC_KX_NotPlayed", "Not played"); break;
                    case 3: sub = e.Added > DateTime.MinValue ? e.Added.ToString("d MMM yyyy", CultureInfo.CurrentCulture) : ""; break;
                    default: sub = e.Source; break;
                }
                e.Tile.SetSub(sub);
            }
            int cols = HubLibColumns();
            var rows = new List<HubLibRow>();
            for (int i = 0; i < shown.Count; i += cols)
            {
                var tiles = shown.Skip(i).Take(cols).Select(e => e.Tile).ToList();
                // the last row padded with empty slots: every row is as wide, so the rows centre and the columns still line up
                if (i > 0) while (tiles.Count < cols) tiles.Add(new HubTile { IsFiller = true, Name = "" });
                rows.Add(new HubLibRow { Tiles = tiles });
            }
            hubLibRows = rows;
            Notify("HubLibRows");
            int all = hubLibAll.Count, inst = hubLibAll.Count(e => e.Installed);
            PageSub = string.Format(Loc("LOC_KX_HubLibCount", "{0} games · {1} installed"), all.ToString("N0", CultureInfo.CurrentCulture), inst.ToString("N0", CultureInfo.CurrentCulture))
                    + (shown.Count != all ? "   ·   " + string.Format(Loc("LOC_KX_HubLibShowing", "showing {0}"), shown.Count.ToString("N0", CultureInfo.CurrentCulture)) : "");
            Notify("PageSub");
            SetHubLibStatus(shown.Count == 0 ? (all == 0 ? Loc("LOC_KX_HubLibEmpty", "No games yet. Playnite adds them from your libraries (Steam, GOG, Epic…): Refresh game library in the profile menu.")
                                                         : !string.IsNullOrEmpty(libSearch) ? string.Format(Loc("LOC_KX_LibNoMatch", "No games called \u201c{0}\u201d here."), libSearch)
                                                         : Loc("LOC_KX_LibNoneMatch", "No games match these filters."))
                                             : "");
        }

        private int HubLibColumns()
        {
            double w = 0;
            try
            {
                var list = UiTools.FindByName(Application.Current.MainWindow, "KX_HubLibList");
                if (list != null) w = list.ActualWidth - 48;   // (the list reaches 24 px past the page on each side: room for the glow)
                if (w <= 0)
                {
                    var view = UiTools.FindByName(Application.Current.MainWindow, "KX_PagesView");
                    if (view != null) w = view.ActualWidth - 240;
                }
            }
            catch (Exception) { }
            if (w <= 0) w = 1680;
            return Math.Max(4, Math.Min(12, (int)Math.Floor(w / 160.0)));   // 140 wide covers, 10 each side (rows are centred)
        }

        private string HubLibSortLabel()
        {
            return string.Format(Loc("LOC_KX_HubSortBy", "Sort: {0}"), HubLibSortText(settings.ProfileLibSort));
        }

        private void CycleHubLibSort()
        {
            settings.ProfileLibSort = ((settings.ProfileLibSort + 1) % 4 + 4) % 4;
            hubTabDirty = true;
            PlaySound("move");
            var chip = PageChips.FirstOrDefault(c => c.Key == "sort");
            if (chip != null) { chip.SetText(HubLibSortLabel()); chip.IsSelected = settings.ProfileLibSort != 0; }
            ApplyHubLibrary();
        }

        private void CycleHubLibShow()
        {
            settings.ProfileLibShow = (settings.ProfileLibShow + 1) % 3;
            hubTabDirty = true;
            PlaySound("move");
            var chip = PageChips.FirstOrDefault(c => c.Key == "show");
            if (chip != null) { chip.SetText(HubLibShowText()); chip.IsSelected = settings.ProfileLibShow != 0; }
            ApplyHubLibrary();
        }

        // ---------------- (0.99.17) the library's tabs and search ----------------

        private string libTab = "all", libSearch = "";

        // (0.99.25) the filters merged: Show (all / installed / favourites / recently played / not played) and Store (every store,
        // or one) work together, with Search and Sort beside them. A on Show, Store or Sort swaps the buttons for its choices;
        // picking one (or B) brings the four back, on the button that was used.
        private string libStore;                       // null: every store
        private List<string> libSources = new List<string>();
        private string libChipMode;                    // null: the four buttons; "show" / "store" / "sort": that one's choices

        private IEnumerable<Tuple<string, string>> LibShowChoices()
        {
            yield return Tuple.Create("all", Loc("LOC_KX_LibTabAll", "All games"));
            yield return Tuple.Create("installed", Loc("LOC_KX_LibTabInstalled", "Installed"));
            yield return Tuple.Create("fav", Loc("LOC_KX_LibTabFav", "Favourites"));
            yield return Tuple.Create("recent", Loc("LOC_KX_LibTabRecent", "Recently played"));
            yield return Tuple.Create("unplayed", Loc("LOC_KX_LibTabUnplayed", "Not played"));
        }

        private string LibShowName()
        {
            var c = LibShowChoices().FirstOrDefault(x => x.Item1 == libTab);
            return c != null ? c.Item2 : Loc("LOC_KX_LibTabAll", "All games");
        }

        /// <summary>The library's buttons (mode null) or one button's choices; focusKey: the chip to highlight after.</summary>
        private void BuildLibChips(string focusKey)
        {
            PageChips.Clear();
            PickRow focus = null;
            if (libChipMode == null)
            {
                var find = AddPageChip(libSearch.Length > 0 ? string.Format(Loc("LOC_KX_LibSearching", "Search: {0}"), libSearch) : Loc("LOC_KX_LibSearch", "Search"), "search", LibSearchOpen);
                find.Key = "search";
                find.IsSelected = libSearch.Length > 0;
                var show = AddPageChip(string.Format(Loc("LOC_KX_LibShowChip", "Show: {0}"), LibShowName()), "filter", () => LibChipsOpen("show"));
                show.Key = "show";
                show.IsSelected = libTab != "all";
                if (libSources.Count >= 2)
                {
                    var store = AddPageChip(string.Format(Loc("LOC_KX_LibStoreChip", "Store: {0}"), libStore ?? Loc("LOC_KX_LibStoreAll", "All stores")), "store", () => LibChipsOpen("store"));
                    store.Key = "store";
                    store.IsSelected = libStore != null;
                }
                var sort = AddPageChip(HubLibSortLabel(), "sort", () => LibChipsOpen("sort"));
                sort.Key = "sort";
                sort.IsSelected = settings.ProfileLibSort != 0;
            }
            else
            {
                var back = AddPageChip(Loc("LOC_KX_LibChoicesBack", "Back"), "close", () => { PlaySound("back"); LibChipsClose(); });
                back.Key = "back";
                List<Tuple<string, string>> choices;
                string cur;
                if (libChipMode == "show") { choices = LibShowChoices().ToList(); cur = libTab; }
                else if (libChipMode == "store")
                {
                    choices = new List<Tuple<string, string>> { Tuple.Create("", Loc("LOC_KX_LibStoreAll", "All stores")) };
                    choices.AddRange(libSources.Select(s => Tuple.Create(s, s)));
                    cur = libStore ?? "";
                }
                else { choices = Enumerable.Range(0, 4).Select(i => Tuple.Create(i.ToString(CultureInfo.InvariantCulture), HubLibSortText(i))).ToList(); cur = settings.ProfileLibSort.ToString(CultureInfo.InvariantCulture); }
                foreach (var ch in choices)
                {
                    string key = ch.Item1;
                    var c = AddPageChip(ch.Item2, null, () => LibChoicePick(key));
                    c.Key = "pick:" + key;
                    c.IsSelected = key == cur;
                    if (c.IsSelected) focus = c;
                }
            }
            if (focusKey != null) focus = PageChips.FirstOrDefault(c => c.Key == focusKey) ?? focus;
            var f = focus;
            int gen = pageGen;
            if (f != null) ui.BeginInvoke(new Action(() => { if (gen == pageGen && PageChips.Contains(f)) UiTools.FocusItem("KX_HubLibChips", f); }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void LibChipsOpen(string mode)
        {
            PlaySound("select");
            libChipMode = mode;
            BuildLibChips(null);
        }

        private void LibChipsClose()
        {
            string was = libChipMode;
            libChipMode = null;
            BuildLibChips(was);
        }

        /// <summary>B on the library: its choices go back to the four buttons first. True: handled.</summary>
        private bool LibChipsBack()
        {
            if (pageKey != "mylibrary" || libChipMode == null) return false;
            PlaySound("back");
            LibChipsClose();
            return true;
        }

        private void LibChoicePick(string key)
        {
            PlaySound("move");
            if (libChipMode == "show") libTab = key;
            else if (libChipMode == "store") libStore = key.Length > 0 ? key : null;
            else
            {
                int s;
                if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out s) && s != settings.ProfileLibSort) { settings.ProfileLibSort = s; hubTabDirty = true; }
            }
            LibChipsClose();
            ApplyHubLibrary();
        }

        private void LibSourceTabs()
        {
            if (hubLibAll == null) return;
            libSources = hubLibAll.Where(e => !string.IsNullOrEmpty(e.Source)).GroupBy(e => e.Source, StringComparer.CurrentCultureIgnoreCase)
                                  .OrderByDescending(g => g.Count()).Select(g => g.Key).Take(12).ToList();
            if (libSources.Count < 2) libSources = new List<string>();   // (one store: choosing it would be the same as All stores)
            if (libChipMode == null && libSources.Count >= 2 && !PageChips.Any(c => c.Key == "store"))
            {
                var focused = FocusedPageItem() as PickRow;
                BuildLibChips(focused != null && PageChips.Contains(focused) ? focused.Key : null);
            }
        }

        private IEnumerable<HubLibEntry> LibFilter(IEnumerable<HubLibEntry> list)
        {
            switch (libTab)
            {
                case "installed": list = list.Where(e => e.Installed); break;
                case "fav": list = list.Where(e => e.Favorite); break;
                case "recent": { var since = DateTime.Now.AddDays(-30); list = list.Where(e => e.Last >= since); break; }
                case "unplayed": list = list.Where(e => e.Seconds <= 0); break;
            }
            if (libStore != null) { string s = libStore; list = list.Where(e => string.Equals(e.Source, s, StringComparison.CurrentCultureIgnoreCase)); }
            string q = (libSearch ?? "").Trim();
            if (q.Length > 0) list = list.Where(e => e.Name != null && e.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            return list;
        }

        /// <summary>The search: the keyboard comes up, and the games narrow down as you type (every tab searched within).</summary>
        private void LibSearchOpen()
        {
            PlaySound("select");
            string was = libSearch ?? "";
            OpenKeyboard(Loc("LOC_KX_LibSearchAsk", "Search your library"), was,
                typed => { LibSearchSet(typed); },
                live => { LibSearchSet(live); },
                () => { LibSearchSet(was); }, 60);
        }

        private void LibSearchSet(string text)
        {
            text = (text ?? "").Trim();
            if (text == (libSearch ?? "")) return;
            libSearch = text;
            var chip = PageChips.FirstOrDefault(c => c.Key == "search");
            if (chip != null)
            {
                chip.SetText(text.Length > 0 ? string.Format(Loc("LOC_KX_LibSearching", "Search: {0}"), text) : Loc("LOC_KX_LibSearch", "Search"));
                chip.IsSelected = text.Length > 0;
            }
            ApplyHubLibrary();
        }

        private void SetHubLibStatus(string text)
        {
            HubLibStatus = text ?? "";
            Notify("HubLibStatus");
            Notify("HasHubLibStatus");
        }

        private void ClearHubLibrary()
        {
            hubLibGen++;
            lock (hubCoverQueue) hubCoverQueue.Clear();
            if (hubLibAll == null && hubLibRows.Count == 0) return;
            hubLibAll = null;
            hubLibRows = new List<HubLibRow>();
            Notify("HubLibRows");
            SetHubLibStatus("");
        }

        /// <summary>A cover to read (newest first, so the rows on screen come before ones scrolled past). One reader at a time; it stops when there's nothing left.</summary>
        private void QueueHubCover(HubTile t)
        {
            lock (hubCoverQueue)
            {
                hubCoverQueue.Push(Tuple.Create(t, hubLibGen));
                if (hubCoverBusy) return;
                hubCoverBusy = true;
            }
            KippixShared.SafeWork.Queue(_ =>
            {
                while (true)
                {
                    Tuple<HubTile, int> next;
                    lock (hubCoverQueue)
                    {
                        if (hubCoverQueue.Count == 0) { hubCoverBusy = false; return; }
                        next = hubCoverQueue.Pop();
                    }
                    if (next.Item2 != hubLibGen) continue;   // the library was left (or built again)
                    ImageSource img = null;
                    try { img = Images.Load(next.Item1.CoverPath, CoverPx); } catch (Exception) { }
                    if (img != null) { var tile = next.Item1; OnUi(() => tile.SetCover(img)); }
                }
            });
        }

        // =====================================================================
        // Wishlist (the release calendar is the same tab's other view)
        // =====================================================================

        private Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>> hubWishPrices;
        private DateTime hubWishPricesAt = DateTime.MinValue;
        private int hubWishSort;   // 0 recently added, 1 name, 2 price, 3 biggest discount

        /// <summary>The Wishlist tab's two views, as the first two buttons on both.</summary>
        private void AddWishViewChips(string view)
        {
            var a = AddPageChip(Loc("LOC_KX_PageWishlist", "Wishlist"), "heart", () => SetWishView("wishlist"));
            a.Key = "view:wishlist";
            a.IsSelected = view == "wishlist";
            var b = AddPageChip(Loc("LOC_KX_PageReleases", "Release calendar"), "clock", () => SetWishView("releases"));
            b.Key = "view:releases";
            b.IsSelected = view == "releases";
        }

        private void SetWishView(string view)
        {
            if (pageKey == view) return;
            if (settings.ProfileWishView != view) { settings.ProfileWishView = view; hubTabDirty = true; }
            PlaySound("move");
            pageQuietOpen = true;
            pageStack.Clear();
            OpenPage(view, "", false);
            FocusPageSoon(PageChips.FirstOrDefault(c => c.Key == "view:" + view));
        }

        private string WishSortText()
        {
            switch (hubWishSort)
            {
                case 1: return Loc("LOC_KX_WishSortName", "Sort: name");
                case 2: return Loc("LOC_KX_WishSortPrice", "Sort: lowest price");
                case 3: return Loc("LOC_KX_WishSortCut", "Sort: biggest discount");
            }
            return Loc("LOC_KX_WishSortAdded", "Sort: recently added");
        }

        private void BuildHubWishlist()
        {
            AddWishViewChips("wishlist");
            PickRow sortChip = null;
            sortChip = AddPageChip(WishSortText(), "sort", () =>
            {
                hubWishSort = (hubWishSort + 1) % 4;
                PlaySound("move");
                sortChip.SetText(WishSortText());
                FillHubWishlist();
            });
            sortChip.Key = "wishsort";
            AddPageChip(Loc("LOC_KX_HubWishInStore", "Open in the Store"), "store", () => { PlaySound("select"); ClosePanels(); OpenStore(); if (openPanel == "store") OpenWishlistPage(); });
            FillHubWishlist();
            // today's prices (IsThereAnyDeal), at most every 15 minutes, only while you're looking
            var wishes = settings.StoreWishlist != null ? settings.StoreWishlist.ToList() : new List<WishItem>();
            var ids = wishes.Select(w => w.ItadId).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            if (ids.Count == 0 || !HasItadKey || (hubWishPrices != null && (DateTime.Now - hubWishPricesAt).TotalMinutes < 15)) return;
            int gen = pageGen;
            string shops = ItadShops;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>> prices = null;
                try { prices = ItadPrices(ids, shops); } catch (Exception ex) { logger.Warn("Kippix: profile wishlist prices: " + ex.Message); }
                OnUi(() =>
                {
                    if (prices == null) return;
                    hubWishPrices = prices;
                    hubWishPricesAt = DateTime.Now;
                    if (gen == pageGen && pageKey == "wishlist" && openPanel == "page") FillHubWishlist();
                });
            });
        }

        private void FillHubWishlist()
        {
            var focus = FocusedPageItem() as PageRow;
            PageRows.Clear();
            var wishes = settings.StoreWishlist != null ? settings.StoreWishlist.ToList() : new List<WishItem>();
            var today = DateTime.Today;
            var rows = new List<Tuple<PageRow, WishItem, decimal, int>>();   // row, wish, price now (-1 unknown), discount
            int onSale = 0, coming = 0;
            foreach (var w in wishes)
            {
                var wish = w;
                decimal now = -1;
                int cut = 0;
                string right = "";
                Tuple<List<ItadDeal>, ItadDeal> p;
                if (hubWishPrices != null && !string.IsNullOrEmpty(w.ItadId) && hubWishPrices.TryGetValue(w.ItadId, out p) && p.Item1 != null && p.Item1.Count > 0)
                {
                    var best = p.Item1.OrderBy(d => d.Price).First();
                    now = best.Price;
                    cut = best.Cut;
                    right = (best.Price == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(best.Price, best.Currency)) + (best.Cut > 0 ? "   −" + best.Cut + "%" : "");
                    if (best.Cut > 0) onSale++;
                }
                bool soon = w.ComingSoon || (w.ReleaseDate.HasValue && w.ReleaseDate.Value.Date > today);
                if (soon) coming++;
                string when = soon
                    ? (!string.IsNullOrEmpty(w.ReleaseText) ? w.ReleaseText : w.ReleaseDate.HasValue ? w.ReleaseDate.Value.ToString("d MMMM yyyy", CultureInfo.CurrentCulture) : Loc("LOC_KX_NoDateYet", "No date yet"))
                    : string.Format(Loc("LOC_KX_WishAdded", "Added {0}"), w.Added.ToString("d MMM yyyy", CultureInfo.CurrentCulture));
                string from = w.ImportedFrom == "steam" ? Loc("LOC_KX_FromSteamWishlist", "Steam wishlist") : w.ImportedFrom == "gog" ? Loc("LOC_KX_FromGogWishlist", "GOG wishlist") : "";
                var row = new PageRow
                {
                    Text = w.Name, ImagePath = w.Image, Wide = true, Key = "wish:" + (w.SteamAppId ?? w.GogId ?? w.ItadId ?? w.Name),
                    Sub = (soon ? Loc("LOC_KX_HubComing", "Coming") + ": " : "") + when + (from.Length > 0 ? "   ·   " + from : ""),
                    Right = right,
                    Command = new RelayCommand(_ => OpenStoreFromWidget(WishToStoreItem(wish)))   // (the Store asks for the PIN / says no for a profile without it)
                };
                rows.Add(Tuple.Create(row, w, now, cut));
            }
            IEnumerable<Tuple<PageRow, WishItem, decimal, int>> sorted;
            switch (hubWishSort)
            {
                case 1: sorted = rows.OrderBy(r => r.Item2.Name ?? "", StringComparer.CurrentCultureIgnoreCase); break;
                case 2: sorted = rows.OrderBy(r => r.Item3 < 0 ? decimal.MaxValue : r.Item3); break;
                case 3: sorted = rows.OrderByDescending(r => r.Item4).ThenByDescending(r => r.Item2.Added); break;
                default: sorted = rows.OrderByDescending(r => r.Item2.Added); break;
            }
            foreach (var r in sorted) PageRows.Add(r.Item1);
            PageSub = wishes.Count == 1 ? Loc("LOC_KX_HubWishOne", "1 game on your wishlist") : string.Format(Loc("LOC_KX_HubWishN", "{0} games on your wishlist"), wishes.Count);
            PageStats = wishes.Count == 0 ? new List<PageStat>() : new List<PageStat>
            {
                new PageStat { Value = wishes.Count.ToString(CultureInfo.CurrentCulture), Label = Loc("LOC_KX_HubWishGames", "wishlisted") },
                new PageStat { Value = hubWishPrices != null ? onSale.ToString(CultureInfo.CurrentCulture) : "—", Label = Loc("LOC_KX_HubWishSale", "on sale now") },
                new PageStat { Value = coming.ToString(CultureInfo.CurrentCulture), Label = Loc("LOC_KX_HubWishComing", "not out yet") }
            };
            string status = wishes.Count == 0
                ? Loc("LOC_KX_HubWishEmpty", "Your wishlist is empty. Wishlist games in the Store (the heart on a game's page), or bring in your Steam / GOG wishlist: Settings & Customisation › Store settings › Wishlists.")
                : "";
            if (wishImportProblem && !string.IsNullOrEmpty(WishImportText)) status = (status.Length > 0 ? status + "\n\n" : "") + WishImportText;
            SetPageStatus(status);
            NotifyPage();
            NotifyPageLists();
            LoadPageImages();
            if (focus != null)
            {
                var again = PageRows.FirstOrDefault(r => r.Key == focus.Key);
                if (again != null) FocusPageSoon(again);
            }
        }

        // =====================================================================
        // Profile tabs: which tabs show, and which one the profile opens on
        // =====================================================================

        private void BuildHubTabsPage()
        {
            PageTitle = Loc("LOC_KX_HubTabsTitle", "Profile tabs");
            PageSub = Loc("LOC_KX_HubTabsSub", "Choose the tabs along the top of your profile (LB / RB move between them)");
            if (settings.ProfileHubHidden == null) settings.ProfileHubHidden = new List<string>();
            AddPageHeader(Loc("LOC_KX_HubTabsHeader", "TABS"));
            foreach (string k in HubTabKeys)
            {
                string key = k;
                bool fixedTab = key == "overview";
                bool on = fixedTab || !settings.ProfileHubHidden.Contains(key);
                string sub = HubTabBlurb(key);
                if (key == "wishlist" && !HubWishlistAllowed) sub = Loc("LOC_KX_HubWishNoStore", "Not for this profile: the Store is switched off for it");
                PageRows.Add(new PageRow
                {
                    Key = "tab:" + key, Icon = HubTabIcon(key), Text = HubTabName(key), Sub = sub,
                    Right = fixedTab ? Loc("LOC_KX_HubAlways", "Always there") : on ? Loc("LOC_KX_HubShown", "Shown") : Loc("LOC_KX_HubHidden", "Hidden"),
                    Command = new RelayCommand(_ =>
                    {
                        if (fixedTab) { PlaySound("back"); return; }
                        if (settings.ProfileHubHidden.Contains(key)) settings.ProfileHubHidden.Remove(key); else settings.ProfileHubHidden.Add(key);
                        SaveSettings();
                        PlaySound("move");
                        RebuildHubTabsPage("tab:" + key);
                    })
                });
            }
            // r12 B: what's on the Overview and Your year
            if (settings.ProfileHubSectionsHidden == null) settings.ProfileHubSectionsHidden = new List<string>();
            AddPageHeader(Loc("LOC_KX_HubSectionsHeader", "ON YOUR PROFILE"));
            foreach (string k in new[] { "captures", "achs", "wish", "heat", "finished", "closest", "share" })
            {
                string key = k;
                bool on = !settings.ProfileHubSectionsHidden.Contains(key);
                PageRows.Add(new PageRow
                {
                    Key = "sec:" + key, Icon = HubSectionIcon(key), Text = HubSectionName(key), Sub = HubSectionBlurb(key),
                    Right = on ? Loc("LOC_KX_HubShown", "Shown") : Loc("LOC_KX_HubHidden", "Hidden"),
                    Command = new RelayCommand(_ =>
                    {
                        if (settings.ProfileHubSectionsHidden.Contains(key)) settings.ProfileHubSectionsHidden.Remove(key); else settings.ProfileHubSectionsHidden.Add(key);
                        SaveSettings();
                        PlaySound("move");
                        RebuildHubTabsPage("sec:" + key);
                    })
                });
            }
            AddCompletionSettingRows();   // FINISHED GAMES: which status is which (ThemeApi.Completion.cs)
            AddPageHeader(Loc("LOC_KX_SCardSettingsHeader", "SHARE CARDS"));
            PageRows.Add(new PageRow
            {
                Key = "shareopen", Icon = "folder", Text = Loc("LOC_KX_SCardOpenFolder", "Open the folder afterwards"),
                Sub = Loc("LOC_KX_SCardOpenFolderSub", "Cards are saved in Pictures\\Kippix. On: that folder opens on the desktop after each one"),
                Right = settings.ShareOpenFolder ? Loc("LOC_KX_On", "On") : Loc("LOC_KX_Off", "Off"),
                Command = new RelayCommand(_ =>
                {
                    settings.ShareOpenFolder = !settings.ShareOpenFolder;
                    SaveSettings();
                    PlaySound("move");
                    RebuildHubTabsPage("shareopen");
                })
            });
            AddPageHeader(Loc("LOC_KX_HubOpensOn", "OPENS ON"));
            PageRows.Add(new PageRow
            {
                Key = "remember", Icon = "clock", Text = Loc("LOC_KX_HubRemember", "The tab you used last"),
                Sub = Loc("LOC_KX_HubRememberSub", "Off: Profile in the menu always opens on Overview (shortcuts still open their own tab)"),
                Right = settings.ProfileHubRemember ? Loc("LOC_KX_On", "On") : Loc("LOC_KX_Off", "Off"),
                Command = new RelayCommand(_ =>
                {
                    settings.ProfileHubRemember = !settings.ProfileHubRemember;
                    SaveSettings();
                    PlaySound("move");
                    RebuildHubTabsPage("remember");
                })
            });
        }

        private string HubSectionName(string key)
        {
            switch (key)
            {
                case "captures": return Loc("LOC_KX_HubSecCaptures", "Recent captures");
                case "achs": return Loc("LOC_KX_HubSecAchs", "Recent achievements");
                case "wish": return Loc("LOC_KX_HubSecWish", "Wishlist shelf");
                case "heat": return Loc("LOC_KX_HubSecHeat", "Play calendar");
                case "finished": return Loc("LOC_KX_HubSecFinished", "Finished games");
                case "closest": return Loc("LOC_KX_HubSecClosest", "Closest achievements");
            }
            return Loc("LOC_KX_HubSecShare", "Share card button");
        }

        private string HubSectionBlurb(string key)
        {
            switch (key)
            {
                case "captures": return Loc("LOC_KX_HubSecCapturesSub", "Your newest screenshots and clips, A opens them in the gallery (Overview)");
                case "achs": return Loc("LOC_KX_HubSecAchsSub", "The achievements you unlocked last, rare ones in gold (Overview)");
                case "wish": return Loc("LOC_KX_HubSecWishSub", "Your wishlist, games on sale first (Overview)");
                case "heat": return Loc("LOC_KX_HubSecHeatSub", "A square for every day of the last year, darker the more you played (Overview and Your year)");
                case "finished": return Loc("LOC_KX_HubSecFinishedSub", "What you beat, finished at 100% or dropped this year (Overview and Your year)");
                case "closest": return Loc("LOC_KX_HubSecClosestSub", "The achievements you're nearest to in the games you played lately (Overview)");
            }
            return Loc("LOC_KX_HubSecShareSub", "Save a picture of your profile or your year to share (Overview and Your year)");
        }

        private static string HubSectionIcon(string key)
        {
            switch (key)
            {
                case "captures": return "gallery";
                case "achs": return "trophy";
                case "wish": return "heart";
                case "heat": return "calendar";
                case "finished": return "flag";
                case "closest": return "trophy";
            }
            return "picture";
        }

        private void RebuildHubTabsPage(string focusKey)
        {
            pageQuietOpen = true;
            OpenPage("hubtabs", "", false);
            var row = PageRows.FirstOrDefault(r => r.Key == focusKey);
            if (row != null) FocusPageSoon(row);
        }

        // =====================================================================
        // The profile menu's top row: Refresh game library (Playnite's own "Update game library")
        // =====================================================================

        /// <summary>
        /// Runs Playnite's library update, as its own fullscreen menu does (MainViewModelBase.UpdateLibrary(metadata for new
        /// games, libraries, emulated folders)), found on the window's view model by reflection. Shows one card, nothing else.
        /// </summary>
        private void RefreshGameLibrary()
        {
            PlaySound("select");
            ClosePanels();
            string problem = null;
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                object vm = w != null ? w.DataContext : null;
                if (vm == null) problem = "no view model";
                else
                {
                    var t = vm.GetType();
                    var busy = t.GetProperty("ProgressActive", BindingFlags.Instance | BindingFlags.Public);
                    var allowed = t.GetProperty("GameAdditionAllowed", BindingFlags.Instance | BindingFlags.Public);
                    bool isBusy = busy != null && busy.GetValue(vm, null) is bool && (bool)busy.GetValue(vm, null);
                    bool isAllowed = allowed == null || !(allowed.GetValue(vm, null) is bool) || (bool)allowed.GetValue(vm, null);
                    if (isBusy || !isAllowed)
                    {
                        ShowCard(Loc("LOC_KX_LibraryHeader", "LIBRARY"), Loc("LOC_KX_LibraryBusy", "Already updating your game library…"), "", "", null);
                        return;
                    }
                    var update = t.GetMethod("UpdateLibrary", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
                    if (update == null)
                    {
                        // an older / newer Playnite: its main menu's command
                        var cmd = t.GetProperty("UpdateGamesCommand", BindingFlags.Instance | BindingFlags.Public);
                        var c = cmd != null ? cmd.GetValue(vm, null) as ICommand : null;
                        if (c != null && c.CanExecute(null)) c.Execute(null);
                        else problem = "no UpdateLibrary on " + t.Name;
                    }
                    else
                    {
                        bool meta = false;
                        try
                        {
                            var app = t.GetProperty("AppSettings", BindingFlags.Instance | BindingFlags.Public);
                            object s = app != null ? app.GetValue(vm, null) : null;
                            var m = s != null ? s.GetType().GetProperty("DownloadMetadataOnImport") : null;
                            object v = m != null ? m.GetValue(s, null) : null;
                            if (v is bool) meta = (bool)v;
                        }
                        catch (Exception) { }
                        update.Invoke(vm, new object[] { meta, true, true });   // (returns a Task: Playnite shows its own progress)
                        logger.Info("Kippix: game library update started (metadata for new games: " + meta + ")");
                    }
                }
            }
            catch (Exception ex) { problem = ex.GetBaseException().Message; }
            if (problem != null)
            {
                logger.Warn("Kippix: couldn't update the game library: " + problem);
                ShowCard(Loc("LOC_KX_LibraryHeader", "LIBRARY"), Loc("LOC_KX_LibraryFailed", "Couldn't start the library update"), Loc("LOC_KX_LibraryFailedLine", "Use Update game library in Playnite's own menu"), "", null);
                return;
            }
            ShowCard(Loc("LOC_KX_LibraryHeader", "LIBRARY"), Loc("LOC_KX_LibraryUpdating", "Updating your game library…"), "", "", null);
        }
    }
}
