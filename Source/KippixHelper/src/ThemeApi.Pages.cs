using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>One row on a page (a release, a news post, a friend, a drive…).</summary>
    public class PageRow : Observable
    {
        public string Text { get; set; }
        public string Sub { get; set; }
        public bool HasSub { get { return !string.IsNullOrEmpty(Sub); } }
        public string Right { get; set; }
        public bool HasRight { get { return !string.IsNullOrEmpty(Right); } }
        public string Icon { get; set; }
        public bool IsHeader { get; set; }
        public bool IsRow { get { return !IsHeader; } }
        public ICommand Command { get; set; }
        public string Key { get; set; }
        internal string ImagePath;
        private ImageSource thumb;
        public ImageSource Thumb { get { return thumb; } set { if (Set(ref thumb, value, "Thumb")) Notify("HasThumb"); } }
        public bool HasThumb { get { return thumb != null; } }
        public bool Wide { get; set; }          // a wide picture (store header) rather than a square one (avatar)
        public double BarWidth { get; set; }    // 0..420; a bar under the text (storage, months)
        public bool HasBar { get; set; }
        public string BarColor { get; set; }
        /// <summary>Changes the right-hand text / the line under the text where the row stands (Up next: moving, Library health: fixed).</summary>
        public void SetRight(string r) { Right = r; Notify("Right"); Notify("HasRight"); }
        public void SetSub(string s) { Sub = s; Notify("Sub"); Notify("HasSub"); }
    }

    /// <summary>A game cover on a page (collections, top games).</summary>
    public class PageTile : Observable
    {
        public string Name { get; set; }
        private string sub;
        public string Sub { get { return sub; } set { Set(ref sub, value, "Sub"); } }   // (can change while shown: Split screen fills in as it learns)
        public Guid GameId;
        internal string CoverPath;
        internal string Tag;   // (1.0.1) what it stands for on pages without games (Shows & movies: the file or folder)
        private ImageSource cover;
        public ImageSource Cover { get { return cover; } set { Set(ref cover, value, "Cover"); } }
        public ICommand Command { get; set; }
    }

    /// <summary>A big number with a label (year in review, storage).</summary>
    public class PageStat
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    /// <summary>
    /// The extra pages (openPanel "page", KX_PagesView): releases calendar, game news, friends, collections, pick for me, storage,
    /// controller test and year in review. Each is built when it opens; nothing runs in the background, and B goes back a page.
    /// Opened from the profile menu (More pages) or a top bar shortcut.
    /// </summary>
    public partial class ThemeApi
    {
        internal static readonly string[] PageKeys = { "quick", "releases", "wishlist", "news", "friends", "collections", "splitscreen", "pick", "storage", "controller", "bluetooth", "year" };

        private string pageKey = "", pageArg = "";
        private readonly Stack<Tuple<string, string, object>> pageStack = new Stack<Tuple<string, string, object>>();
        private int pageGen;
        private bool pageQuietOpen;   // going back a page: the back sound has already played

        public ObservableCollection<PageRow> PageRows { get; private set; }
        public ObservableCollection<PageTile> PageTiles { get; private set; }
        public ObservableCollection<PickRow> PageChips { get; private set; }
        public List<PageStat> PageStats { get; private set; }
        public string PageTitle { get; private set; }
        public string PageSub { get; private set; }
        public string PageStatus { get; private set; }
        public string PageHint { get; private set; }
        public string PageTilesTitle { get; private set; }
        public bool HasPageRows { get { return PageRows != null && PageRows.Count > 0; } }
        public bool HasPageTiles { get { return PageTiles != null && PageTiles.Count > 0; } }
        public bool HasPageChips { get { return PageChips != null && PageChips.Count > 0; } }
        public bool HasPageStats { get { return PageStats != null && PageStats.Count > 0; } }
        public bool HasPageStatus { get { return !string.IsNullOrEmpty(PageStatus); } }
        public bool HasPageTilesTitle { get { return !string.IsNullOrEmpty(PageTilesTitle); } }
        public bool IsControllerPage { get { return openPanel == "page" && pageKey == "controller"; } }

        // the big picture (pick for me)
        private ImageSource pageHeroCover;
        public ImageSource PageHeroCover { get { return pageHeroCover; } }
        public string PageHeroTitle { get; private set; }
        public string PageHeroSub { get; private set; }
        public bool HasPageHero { get; private set; }
        public ICommand PageHeroCommand { get; private set; }
        private Guid pageHeroGame;

        private void InitPages()
        {
            PageRows = new ObservableCollection<PageRow>();
            PageTiles = new ObservableCollection<PageTile>();
            PageChips = new ObservableCollection<PickRow>();
            PageStats = new List<PageStat>();
            PageHeroCommand = new RelayCommand(_ => { if (pageHeroGame != Guid.Empty) OpenGameFromPage(pageHeroGame); });
            InitControllerPage();
        }

        internal string PageName(string key)
        {
            switch (key)
            {
                case "quick": return Loc("LOC_KX_QuickSettings", "Quick settings");
                case "releases": return Loc("LOC_KX_PageReleases", "Release calendar");
                case "wishlist": return Loc("LOC_KX_PageWishlist", "Wishlist");
                case "news": return Loc("LOC_KX_PageNews", "Game news");
                case "friends": return Loc("LOC_KX_PageFriends", "Friends");
                case "collections": return Loc("LOC_KX_PageCollections", "Collections");
                case "pick": return Loc("LOC_KX_PagePick", "Pick for me");
                case "splitscreen": return Loc("LOC_KX_PageSplit", "Split screen");
                case "storage": return Loc("LOC_KX_PageStorage", "Storage");
                case "controller": return Loc("LOC_KX_PageController", "Controller");
                case "bluetooth": return Loc("LOC_KX_Bluetooth", "Bluetooth");
                case "year": return Loc("LOC_KX_PageYear", "Year in review");
                case "upnext": return Loc("LOC_KX_PageUpNext", "Up next");
                case "health": return Loc("LOC_KX_PageHealth", "Library health");
                case "timesup": return Loc("LOC_KX_LimitTimesUp", "Time's up for today");
                case "mylibrary": return Loc("LOC_KX_HubTabLibrary", "Library");
                case "profilepage": return Loc("LOC_KX_Profile", "Profile");
                case "doctor": return Loc("LOC_KX_DocTitle", "Kippix Doctor");
                case "localmedia": return Loc("LOC_KX_LocalMedia", "Shows & movies");
                case "media": return Loc("LOC_KX_Media", "Media");
            }
            return key;
        }

        private string PageIcon(string key)
        {
            switch (key)
            {
                case "quick": return "sliders";
                case "releases": return "calendar";
                case "wishlist": return "heart";
                case "news": return "news";
                case "friends": return "friends";
                case "collections": return "shelf";
                case "pick": return "dice";
                case "splitscreen": return "layout";
                case "storage": return "drive";
                case "controller": return "controller";
                case "bluetooth": return "bluetooth";
                case "year": return "chart";
                case "upnext": return "forward";
                case "health": return "check";
                case "localmedia": return "localmedia";
                case "media": return "media";
            }
            return "chevron";
        }

        private string PageBlurb(string key)
        {
            switch (key)
            {
                case "quick": return Loc("LOC_KX_QuickSub2", "Volume, speakers and mic, display, Wi‑Fi, Bluetooth, controller battery");
                case "releases": return Loc("LOC_KX_PageReleasesSub", "When the games on your wishlist come out");
                case "wishlist": return Loc("LOC_KX_PageWishlistSub", "Your Store wishlist with today's best prices");
                case "news": return Loc("LOC_KX_PageNewsSub", "Updates and patch notes for the Steam games you play");
                case "friends": return Loc("LOC_KX_PageFriendsSub", "What your Steam friends are playing");
                case "collections": return Loc("LOC_KX_PageCollectionsSub", "Your own shelves of games, any of them can be a home row");
                case "pick": return Loc("LOC_KX_PagePickSub", "Can't decide? Roll for a game");
                case "splitscreen": return Loc("LOC_KX_PageSplitSub2", "Games for everyone on the sofa (or online): how many are playing?");
                case "storage": return Loc("LOC_KX_PageStorageSub", "Your drives and installed games by size");
                case "controller": return Loc("LOC_KX_PageControllerSub", "Button test, stick drift, battery");
                case "bluetooth": return Loc("LOC_KX_PageBluetoothSub", "Turn Bluetooth on or off, and connect your headphones and speakers");
                case "year": return Loc("LOC_KX_PageYearSub", "Your gaming year: top games, hours, achievements");
                case "upnext": return Loc("LOC_KX_PageUpNextSub", "The games you mean to play next, and ones you could finish soon");
                case "health": return Loc("LOC_KX_PageHealthSub", "Missing pictures and info, duplicates, broken installs");
            }
            return "";
        }

        /// <summary>The profile menu's More pages, in groups. (r15: pages that are tabs of the profile — Up next, Collections,
        /// Your year, Wishlist, Release calendar, Friends — aren't here: they're found on the profile.)</summary>
        private void BuildPageMenuRows()
        {
            ProfileRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecYourGames", "YOUR GAMES") });
            foreach (string k in new[] { "pick", "splitscreen", "storage", "health" }) AddPageMenuRow(k);
            ProfileRows.Add(GameAddMenuRow(true));   // Add an app or game (ThemeApi.GameAdd.cs)
            ProfileRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecNews", "NEWS") });
            AddPageMenuRow("news");
            ProfileRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecThisPc", "THIS PC") });
            foreach (string k in new[] { "quick", "bluetooth", "controller" }) AddPageMenuRow(k);
        }

        private void AddPageMenuRow(string k)
        {
            string key = k;
            ProfileRows.Add(new PickRow { Text = PageName(k), Sub = PageBlurb(k), Icon = PageIcon(k), Key = k, Command = new RelayCommand(_ =>
            {
                ClosePanels();
                OpenPageFromShortcut(key);
                if (openPanel != null) { profileReturnKey = key; profileReturnPage = "pages"; }   // B from that page comes back to this row
            }) });
        }

        private void OpenPageFromShortcut(string key)
        {
            if (key == "quick") { OpenQuickSettings(); return; }
            // friends, wishlist, releases, collections and your year are tabs of the profile now: their shortcuts open it on that tab
            string tab = key == "releases" ? "wishlist" : HubTabOf(key);
            if (tab != null && key != "profilepage") { OpenProfileHub(tab, key == "releases" || key == "wishlist" ? key : null); return; }
            if (key == "profile" || key == "profilepage" || key == "mylibrary") { OpenProfileHub(key == "mylibrary" ? "library" : "overview", null); return; }
            pageStack.Clear();
            OpenPage(key, "", false);
        }

        public bool IsPageOpen { get { return openPanel == "page"; } }

        private void OpenPage(string key, string arg, bool push)
        {
            if (push && openPanel == "page") pageStack.Push(Tuple.Create(pageKey, pageArg, FocusedPageItem()));
            LeavePage();
            pageKey = key;
            pageArg = arg ?? "";
            communityShots = new List<string>();
            pageGen++;
            PageRows.Clear();
            PageTiles.Clear();
            PageChips.Clear();
            PageStats = new List<PageStat>();
            PageStatus = "";
            PageTilesTitle = "";
            PageHint = Loc("LOC_KX_PageHintBack", "");
            HasPageHero = false;
            pageHeroCover = null;
            PageTitle = PageName(key);
            PageSub = "";
            if (openPanel != "page") TogglePanel("page");
            if (!pageQuietOpen) PlaySound("select");
            pageQuietOpen = false;
            switch (key)
            {
                case "releases": BuildReleasesPage(); break;
                case "gamenews": BuildGameNewsPage(arg); break;   // (the game page's News button: ThemeApi.GameNews.cs)
                case "wishlist": BuildHubWishlist(); break;
                case "mylibrary": BuildHubLibrary(); break;
                case "hubtabs": BuildHubTabsPage(); break;
                case "news": BuildNewsPage(); break;
                case "friends": BuildFriendsPage(); break;
                case "friend": BuildFriendPage(arg); break;
                case "collections": BuildCollectionsPage(); break;
                case "shelf": BuildShelfPage(arg); break;
                case "library": BuildBoardLibraryPage(arg); break;
                case "pick": BuildPickPage(); break;
                case "splitscreen": BuildSplitScreenPage(); break;
                case "profilepage": BuildProfilePage(); break;
                case "compare": BuildComparePage(arg); break;
                case "community": BuildCommunityPage(arg); break;              // (CommunityHub.cs: arg the tab, all / layouts / looks / widgets / mine)
                case "communityitem": BuildCommunityItemPage(arg); break;      // (arg kind|id)
                case "communityshare": BuildCommunitySharePage(arg); break;
                case "ghsignin": BuildGitHubSignInPage(); break;
                case "communitysend": BuildCommunitySendPage(); break;         // (1.0.4, CommunitySend.cs: pictures and a description, then Send)               // (1.0.1, CommunityUpload.cs: GitHub's sign-in code)    // (Upload: what kind; arg widgets: which one)
                case "communityget": BuildCommunityGetPage(arg); break;        // (Download: what kind, or from a file)
                case "communitylayout": BuildCommunityLayoutPage(arg); break;
                case "gamepagelist": BuildGamePageListPage(); break;           // (GamePageShare.cs: your game page layouts)
                case "gamepagefile": BuildGamePageFilePage(arg); break;        // (arg: the file)
                case "storage": BuildStoragePage(); break;
                case "controller": BuildControllerPage(); break;
                case "bluetooth": BuildBluetoothPage(); break;   // (0.99.72, ThemeApi.Bluetooth.cs)
                case "year": BuildYearPage(arg); break;
                case "doctor": BuildDoctorPage(); break;          // (Doctor.cs)
                case "upnext": BuildUpNextPage(); break;          // (UpNext.cs)
                case "health": BuildHealthPage(arg); break;       // (LibraryHealth.cs)
                case "timesup": BuildTimesUpPage(); break;        // (TimeLimits.cs)
                case "localmedia": BuildLocalMediaPage(arg); break;          // (1.0.1, LocalMedia.cs: Shows & movies)
                case "localshow": BuildLocalShowPage(arg); break;
                case "localmediafolders": BuildLocalFoldersPage(); break;
                case "localmediafolder": BuildLocalFolderPage(arg); break;
                case "localitem": BuildLocalItemPage(arg); break;              // (Select on a poster)
                case "localhidden": BuildLocalHiddenPage(); break;
            }
            ApplyHubChrome();   // the profile's header and tabs (its tabs are pages), or none
            CommunityChrome();  // the Community page's tabs (ThemeApi.CommunityHub.cs), or none
            NotifyPage();
            FocusPageSoon(null);
        }

        private object FocusedPageItem()
        {
            var fe = Keyboard.FocusedElement as FrameworkElement;
            return fe != null ? fe.DataContext : null;
        }

        /// <summary>Puts the highlight on an item (or the first thing on the page) once it's drawn.</summary>
        private void FocusPageSoon(object item)
        {
            int gen = pageGen;
            ui.BeginInvoke(new Action(() =>
            {
                if (gen != pageGen || openPanel != "page") return;
                if (item is PageRow && PageRows.Contains((PageRow)item)) { UiTools.FocusItem("KX_PageRowList", item); return; }
                if (item is PageTile && PageTiles.Contains((PageTile)item)) { UiTools.FocusItem("KX_PageTileList", item); return; }
                if (item is PickRow && PageChips.Contains((PickRow)item)) { UiTools.FocusItem("KX_PageChipList", item); return; }
                if (HasPageHero && UiTools.FocusByNameIfVisible("KX_PageHeroButton")) return;
                if (HubFocusDefault()) return;   // the profile: the highlight stays where it is, or goes on the tab
                UiTools.FocusInside("KX_PagesView");
            }), DispatcherPriority.ContextIdle);
        }

        private void NotifyPage()
        {
            NotifyCommunityShot();   // (1.0.4) the Community item's pictures show on its page only
            foreach (string n in new[] { "PageTitle", "PageSub", "PageStatus", "HasPageStatus", "PageHint", "PageStats", "HasPageStats", "HasPageRows", "HasPageTiles",
                                         "HasPageChips", "PageTilesTitle", "HasPageTilesTitle", "IsControllerPage", "PageHeroCover", "PageHeroTitle", "PageHeroSub", "HasPageHero" }) Notify(n);
            NotifyHubShelves();
        }

        /// <summary>Called when the page changes or the pages close: stops anything the page had running.</summary>
        private void LeavePage()
        {
            StopControllerTest();
            StopRoll();
            ClearHubExtras();   // the profile's play calendar and finished shelf (ThemeApi.Heatmap.cs)
            ClearHubShelves();  // the Overview's shelves (ThemeApi.ProfileShelves.cs)
        }

        private void LeavePages()
        {
            LeavePage();
            pageStack.Clear();
            pageKey = "";
            pageGen++;   // anything still loading for a page is dropped
            LeaveHub();
            CommunityChrome();
            Notify("IsControllerPage");
        }

        private bool PageBack()
        {
            PlaySound("back");
            if (pageStack.Count > 0)
            {
                var prev = pageStack.Pop();
                pageQuietOpen = true;
                OpenPage(prev.Item1, prev.Item2, false);
                // the row that was opened is a new object after the rebuild: find the one with the same key / name
                object again = prev.Item3;
                var row = again as PageRow;
                var tile = again as PageTile;
                if (row != null) again = PageRows.FirstOrDefault(r => r.Key == row.Key && r.Text == row.Text);
                else if (tile != null) again = PageTiles.FirstOrDefault(t => t.GameId == tile.GameId && t.Name == tile.Name);
                else if (again is PickRow) { var chip = (PickRow)again; again = PageChips.FirstOrDefault(c => c.Key == chip.Key && c.Icon == chip.Icon); }
                if (again != null) FocusPageSoon(again);
                return true;
            }
            if (ReturnToProfilePages()) return true;
            ClosePanels();
            return true;
        }

        /// <summary>A page opened from the profile menu's More pages: B goes back to that menu, on the same row.</summary>
        private string profileReturnKey;
        private string profileReturnPage = "pages";   // the menu page that row is on ("": the menu itself, e.g. Profile)

        private bool ReturnToProfilePages()
        {
            string key = profileReturnKey;
            if (key == null) return false;
            string menuPage = profileReturnPage ?? "pages";
            ClosePanels();
            profileReturnPage = "pages";
            profilePage = menuPage;
            BuildProfileRows();
            TogglePanel("profile");
            Notify("IsProfileMedia"); Notify("IsProfileMediaPage");
            Notify("ProfileMenuTitle");
            focusBeforePanel = null;   // closing the menu later puts the highlight on the profile button
            ui.BeginInvoke(new Action(() =>
            {
                var row = ProfileRows.FirstOrDefault(r => r.Key == key);
                if (row != null) UiTools.FocusItem("KX_ProfileList", row); else UiTools.FocusInside("KX_ProfilePanel");
            }), DispatcherPriority.ContextIdle);
            return true;
        }

        private bool PagesPanelKey(KeyEventArgs e, int step, bool xPressed, bool back)
        {
            if (pageKey == "controller") return ControllerPageKey(e, back);
            if (pageKey == "doctor" && back && DoctorBack()) return true;   // (opened from Help: back to Help)
            if (pageKey == "upnext" && UpNextPageKey(e, step, xPressed, back)) return true;   // (UpNext.cs: LB / RB move, X takes off)
            if (back && CommunityPageBack()) return true;   // (the Community page: back where it was opened from)
            if (CommunityStepKey(e, step)) return true;     // (the Community page: LB / RB tabs)
            if (back && LibChipsBack()) return true;        // (the library: its Show / Store / Sort choices back to the buttons)
            if (back) return PageBack();
            if (LocalMediaSelectKey(e)) return true;   // (1.0.1) Shows & movies: Select on a poster › its page (Hide…)
            if (xPressed)
            {
                if (LocalMediaX()) return true;   // (1.0.1) Shows & movies: watched / not watched
                if (pageKey == "pick") { Roll(); return true; }
                if (pageKey == "shelf")
                {
                    var t = FocusedPageItem() as PageTile;
                    if (t != null) { RemoveFromShelf(pageArg, t); return true; }
                }
            }
            if (HubStepKey(e, step)) return true;   // the profile: LB / RB tabs, LT / RT month or year
            if (step != 0 && pageKey == "profilepage")
            {
                if (!IsPadRelease(e)) StepProfileMonth(step);
                return true;
            }
            if (step != 0 && pageKey == "year")
            {
                if (!IsPadRelease(e)) StepYear(step);   // LT / RT also send a key when they're let go
                return true;
            }
            return false;
        }

        /// <summary>Loads the pictures on the page off the UI thread (covers, avatars, store headers).</summary>
        private void LoadPageImages()
        {
            int gen = pageGen;
            var rows = PageRows.Where(r => !string.IsNullOrEmpty(r.ImagePath) && r.Thumb == null).ToList();
            var tiles = PageTiles.Where(t => !string.IsNullOrEmpty(t.CoverPath) && t.Cover == null).ToList();
            if (rows.Count == 0 && tiles.Count == 0) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                foreach (var t in tiles)
                {
                    if (gen != pageGen) return;
                    var img = Images.Load(t.CoverPath, 240);
                    if (img != null) { var tt = t; OnUi(() => tt.Cover = img); }
                }
                foreach (var r in rows)
                {
                    if (gen != pageGen) return;
                    var img = Images.Load(r.ImagePath, r.Wide ? 300 : 96);
                    if (img != null) { var rr = r; OnUi(() => rr.Thumb = img); }
                }
            });
        }

        private void SetPageStatus(string text)
        {
            PageStatus = text ?? "";
            Notify("PageStatus");
            Notify("HasPageStatus");
        }

        private void AddPageHeader(string text) { PageRows.Add(new PageRow { IsHeader = true, Text = text }); }

        private PickRow AddPageChip(string text, string icon, Action run)
        {
            var c = new PickRow { Text = text, Icon = icon, Key = text, Command = new RelayCommand(_ => run()) };
            PageChips.Add(c);
            return c;
        }

        private void OpenGameFromPage(Guid id)
        {
            if (id == Guid.Empty) return;
            ClosePanels();   // the pages go (and their focus restore runs first), then the game's page opens
            ui.BeginInvoke(new Action(() => OpenPinnedGame(id)), DispatcherPriority.ContextIdle);
        }

        private void NotifyPageLists()
        {
            foreach (string n in new[] { "HasPageRows", "HasPageTiles", "HasPageChips", "PageStats", "HasPageStats" }) Notify(n);
            NotifyHubShelves();
        }

        // ---------------- owned games (by Steam app id, by name) ----------------

        private Dictionary<string, Game> OwnedSteamGames()
        {
            var d = new Dictionary<string, Game>();
            try
            {
                foreach (var g in api.Database.Games.Where(g => g != null && g.PluginId == SteamPluginId && !string.IsNullOrEmpty(g.GameId)))
                    if (!d.ContainsKey(g.GameId)) d[g.GameId] = g;
            }
            catch (Exception) { }
            return d;
        }

        private static string SteamHeader(string appId)
        {
            return "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + appId + "/header.jpg";
        }

        private void OpenSteamAppInStore(string appId, string name)
        {
            OpenStoreFromWidget(new StoreItem
            {
                Store = "Steam", Id = appId, SteamAppId = appId, Name = name, Image = SteamHeader(appId),
                Link = "https://store.steampowered.com/app/" + appId + "/", AppLink = "steam://store/" + appId
            });
        }
    }
}
