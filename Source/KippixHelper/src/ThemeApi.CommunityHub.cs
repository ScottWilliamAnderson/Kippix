using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>A picture in the Community page's gallery (a layout, a look pack, a widget).</summary>
    public class CommunityTile : PageRow
    {
        /// <summary>What it is (only on the All tab): Layout, Look pack, Widget.</summary>
        public string KindText { get; set; }
        public bool HasKind { get { return !string.IsNullOrEmpty(KindText); } }
    }

    /// <summary>
    /// Community: ONE full-screen page (openPanel "page", key "community", arg = the tab) with a tab strip (LB / RB, or A on a
    /// tab): All · Layouts · Look packs · Widgets · Yours. The gallery tabs show what other people have made (picture, name,
    /// who made it; on All also what kind it is); A opens it ("communityitem": use / add / save it, Undo).
    /// Since 0.81 there are no Upload / Download buttons along the top: Yours › UPLOAD shares a layout / a look pack / a
    /// widget ("communityshare" widgets: which one) → the files are saved, ready to post (Community.cs ShareToCommunity), and
    /// the GitHub form when there's a community page to post on; anything in the gallery is downloaded from its own page.
    /// Yours: upload, put your layout / look back, your layouts and look packs, the community widgets' switch (web snippets, news
    /// feeds, data widgets go online only with it on, and only while they're on screen) and adding those.
    /// Opened by Settings › Community, the profile menu's Community row, the notes on the Widgets / Look packs / Theme
    /// Studio / Layouts pages (on their tab) and Settings search; B goes back where it was opened from. Anything that goes
    /// through the settings on the way (a file picker, placing a widget) comes back here: the settings page "community" is
    /// only a way back to this page.
    /// </summary>
    public partial class ThemeApi
    {
        internal static readonly string[] CommunityTabKeys = { "all", "layouts", "gamepages", "looks", "widgets", "mine" };

        private string communityTab = "all";
        private string communityFocusKey;     // the next build puts the highlight on this (a picture, a row, a button), else on the tab
        private string communityStatus;       // a message for the next build (after an undo, an import, a share…)
        private string communitySharedKind;   // just shared one: the next build has the GitHub form's button
        private string communityPlacingId;    // a widget being placed from here: added, or not after all?
        private bool communityNoRedirect;     // the settings page "community" as a backdrop (file picker, placing), not a way back
        private string communityPrevPage;     // the settings page before (to tell coming from the root apart)
        private string widgetNewReturn;       // a widget added from here: placed, then back here (StartWidgetMove)
        // where B goes from the page: "options" (that settings page, on that row), "profile" (the menu, on Community), or closed
        private string communityFrom, communityFromPage, communityFromKey;

        private ObservableCollection<PickRow> communityTabs;
        public ObservableCollection<PickRow> CommunityTabs { get { return communityTabs ?? (communityTabs = new ObservableCollection<PickRow>()); } }
        private ObservableCollection<PageRow> communityTiles;
        public ObservableCollection<PageRow> CommunityTiles { get { return communityTiles ?? (communityTiles = new ObservableCollection<PageRow>()); } }
        public bool HasCommunityTiles { get { return communityTiles != null && communityTiles.Count > 0 && IsCommunityHub; } }
        public bool IsCommunityHub { get { return openPanel == "page" && pageKey == "community"; } }

        private static string CommunityTabKey(string tab)
        {
            if (tab == "community" || string.IsNullOrEmpty(tab)) return "all";
            return Array.IndexOf(CommunityTabKeys, tab) >= 0 ? tab : "all";
        }

        private string CommunityTabName(string tab)
        {
            switch (tab)
            {
                case "layouts": return Loc("LOC_KX_LayoutsShort", "Layouts");
                case "gamepages": return Loc("LOC_KX_GamePagesShort", "Game pages");
                case "looks": return Loc("LOC_KX_LookPacks", "Look packs");
                case "widgets": return Loc("LOC_KX_Widgets", "Widgets");
                case "mine": return Loc("LOC_KX_CommunityTabMine", "Yours");
            }
            return Loc("LOC_KX_CommunityTabAll", "All");
        }

        private static string CommunityTabIcon(string tab)
        {
            switch (tab)
            {
                case "layouts": return "layout";
                case "gamepages": return "controller";
                case "looks": return "palette";
                case "widgets": return "grid";
                case "mine": return "profile";
            }
            return "star";
        }

        private string CommunityTabBlurb(string tab)
        {
            switch (tab)
            {
                case "layouts": return Loc("LOC_KX_CommunityBlurbLayouts", "Home screens other people have made (Undo brings yours back)");
                case "gamepages": return Loc("LOC_KX_CommunityBlurbGamePages", "Game pages other people have made (only the game page changes; Undo brings yours back)");
                case "looks": return Loc("LOC_KX_CommunityBlurbLooks", "Colours, fonts and cards other people have made");
                case "widgets": return Loc("LOC_KX_CommunityBlurbWidgets", "Widgets and tiles other people have made");
                case "mine": return Loc("LOC_KX_CommunityBlurbMine2", "Upload yours, put your own back, your layouts and look packs, and the community widgets");
            }
            return Loc("LOC_KX_CommunityBlurbAll3", "Layouts, game pages, look packs and widgets other people have made, newest first");
        }

        // =====================================================================
        // Opening it, and going back
        // =====================================================================

        private ICommand openCommunityCmd;
        /// <summary>Settings › Community (CommandParameter "community") and buttons elsewhere (a tab: layouts, looks, widgets, mine).</summary>
        public ICommand OpenCommunityCommand { get { return openCommunityCmd ?? (openCommunityCmd = new RelayCommand(p => OpenCommunity(p as string, null, false))); } }

        /// <summary>
        /// Opens the Community page on a tab. From a settings page B comes back to the row that opened it; from the profile
        /// menu, back to the menu on Community; from elsewhere the settings' PIN is asked first when the profile needs it.
        /// </summary>
        private void OpenCommunity(string tab, string focusKey, bool fromProfile)
        {
            string t = CommunityTabKey(tab);
            if (openPanel == "options")
            {
                var b = Keyboard.FocusedElement as ButtonBase;
                communityFrom = "options";
                communityFromPage = optionsPage ?? "";
                communityFromKey = b != null ? b.CommandParameter as string : null;
            }
            else if (!IsCommunityHub)
            {
                string tt = t, fk = focusKey;
                bool fp = fromProfile;
                if (ProfileGate("settings", () => OpenCommunity(tt, fk, fp))) return;   // (it changes layouts and looks: the settings' PIN)
                communityFrom = fromProfile ? "profile" : null;
            }
            PlaySound("select");
            ShowCommunityPage(t, focusKey);
        }

        /// <summary>The page itself on a tab (where B goes is kept as it was). The highlight: <paramref name="focusKey"/>, else the tab.</summary>
        private void ShowCommunityPage(string tab, string focusKey)
        {
            if (openPanel != null && openPanel != "page") ClosePanels();
            pageStack.Clear();
            communityTab = CommunityTabKey(tab);
            communityFocusKey = focusKey;
            if (communityPlacingId != null)
            {
                bool added = settings.Widgets != null && settings.Widgets.Any(w => w.Id == communityPlacingId);
                communityPlacingId = null;
                communityStatus = added ? Loc("LOC_KX_CommunityWidgetPlaced", "Added to your home screen. Undo takes it off again.")
                                        : Loc("LOC_KX_CommunityWidgetNotPlaced", "Not added after all.");
            }
            pageQuietOpen = true;
            OpenPage("community", communityTab, false);
        }

        /// <summary>The old way in (notes, Theme Studio, a widget's page): a settings key is turned into the tab it's on.</summary>
        private void OpenCommunityAt(string page, string focusKey, bool quiet)
        {
            string k = focusKey ?? "";
            string tab = k.StartsWith("cg:") ? "gamepages" : k.StartsWith("cl:") ? "layouts" : k.StartsWith("ck:") ? "looks" : k == "cw:on" ? "mine" : k.StartsWith("cw:") ? "widgets" : "all";
            OpenCommunity(tab, k == "cw:on" ? "m:switch" : null, false);
        }

        /// <summary>The note on the Widgets / Look packs / Theme Studio / Layouts pages: A opens Community on that tab.</summary>
        private PickRow CommunityNoteRow(string section, string sub)
        {
            string tab = section == "looks" ? "looks" : section == "widgets" ? "widgets" : section == "gamepages" ? "gamepages" : "layouts";
            return new PickRow
            {
                Key = "gocommunity", Icon = "globe", Text = Loc("LOC_KX_CommunityNote", "Share or get more in Settings › Community"), Sub = sub,
                Command = new RelayCommand(_ => OpenCommunity(tab, null, false))
            };
        }

        /// <summary>The profile menu's Community row.</summary>
        private PickRow CommunityProfileRow()
        {
            return new PickRow
            {
                Text = Loc("LOC_KX_CatCommunity", "Community"), Sub = Loc("LOC_KX_CatCommunitySub2", "Layouts, game pages, look packs and widgets: get other people's, share yours"),
                Icon = "globe", Key = "community", Command = new RelayCommand(_ => OpenCommunity("all", null, true))
            };
        }

        /// <summary>B on a tab (nothing under it): back where the page was opened from. True: handled.</summary>
        private bool CommunityPageBack()
        {
            if (!IsCommunityHub || pageStack.Count > 0) return false;
            PlaySound("back");
            string from = communityFrom;
            communityFrom = null;
            if (from == "profile")
            {
                profileReturnKey = "community";
                profileReturnPage = "";
                if (ReturnToProfilePages()) return true;
            }
            ClosePanels();
            if (from == "options")
            {
                string page = communityFromPage ?? "", key = communityFromKey ?? "community";
                TogglePanel("options");
                if (openPanel == "options") GoBackTo(page, key);
            }
            return true;
        }

        /// <summary>From OpenPage and LeavePages: the tab strip follows the page.</summary>
        private void CommunityChrome()
        {
            if (!IsCommunityHub && communityTiles != null && communityTiles.Count > 0) communityTiles.Clear();
            Notify("IsCommunityHub");
            Notify("HasCommunityTiles");
        }

        /// <summary>LB / RB: the next tab along (round from the end to the start). True: handled.</summary>
        private bool CommunityStepKey(KeyEventArgs e, int step)
        {
            if (!IsCommunityHub || step == 0) return false;
            if (IsPadRelease(e)) return true;
            if (e.Key == Key.PageUp || e.Key == Key.PageDown) return false;   // (LT / RT: the page scrolls)
            int i = Math.Max(0, Array.IndexOf(CommunityTabKeys, communityTab));
            int j = ((i + step) % CommunityTabKeys.Length + CommunityTabKeys.Length) % CommunityTabKeys.Length;
            PlaySound("move");
            ShowCommunityPage(CommunityTabKeys[j], null);
            return true;
        }

        private void PickCommunityTab(string tab)
        {
            if (tab == communityTab && IsCommunityHub) return;
            PlaySound("move");
            ShowCommunityPage(tab, null);
        }

        private void SyncCommunityTabs()
        {
            var tabs = CommunityTabs;
            if (tabs.Count != CommunityTabKeys.Length)
            {
                tabs.Clear();
                foreach (string k in CommunityTabKeys)
                {
                    string key = k;
                    tabs.Add(new PickRow { Key = key, Text = CommunityTabName(key).ToUpper(CultureInfo.CurrentCulture), Icon = CommunityTabIcon(key), Command = new RelayCommand(_ => PickCommunityTab(key)) });
                }
            }
            foreach (var t in tabs) t.IsSelected = t.Key == communityTab;
        }

        /// <summary>Once the page is drawn (after the pages' own first highlight): the picture / row / button asked for, else the tab.</summary>
        private void CommunityFocusSoon()
        {
            int gen = pageGen;
            ui.BeginInvoke(new Action(() =>
            {
                if (gen != pageGen || !IsCommunityHub) return;
                string key = communityFocusKey;
                communityFocusKey = null;
                if (key != null)
                {
                    var tile = CommunityTiles.FirstOrDefault(t => t.Key == key);
                    if (tile != null) { UiTools.FocusItem("KX_CommunityTileList", tile); return; }
                    var row = PageRows.FirstOrDefault(r => r.Key == key);
                    if (row != null) { UiTools.FocusItem("KX_PageRowList", row); return; }
                    var chip = PageChips.FirstOrDefault(c => c.Key == key);
                    if (chip != null) { UiTools.FocusItem("KX_PageChipList", chip); return; }
                }
                var sel = CommunityTabs.FirstOrDefault(t => t.IsSelected) ?? CommunityTabs.FirstOrDefault();
                if (sel != null) UiTools.FocusItem("KX_CommunityTabList", sel);
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>The highlight isn't on the page any more (what it was on went away): put it back.</summary>
        private bool CommunityFocusLost()
        {
            var fe = Keyboard.FocusedElement as FrameworkElement;
            var view = UiTools.FindByName(Application.Current.MainWindow, "KX_PagesView");
            return fe == null || view == null || !fe.IsVisible || !view.IsAncestorOf(fe);
        }

        // =====================================================================
        // The page: a tab
        // =====================================================================

        private void BuildCommunityPage(string arg)
        {
            string tab = CommunityTabKey(arg);
            communityTab = tab;
            PageTitle = Loc("LOC_KX_CatCommunity", "Community");
            PageSub = CommunityTabBlurb(tab);
            PageHint = Loc("LOC_KX_CommunityHint", "LB / RB  Another tab");
            CommunityTiles.Clear();
            SyncCommunityTabs();

            // (0.81: no Upload / Download buttons along the top. Sharing yours is on Yours; something from the gallery is
            // used / added / saved from its own page, A on its picture)
            string shared = communitySharedKind;
            communitySharedKind = null;
            string post = shared != null ? CommunityPostUrl(shared) : null;
            if (post != null)
                PageChips.Insert(0, new PickRow { Key = "c:post", Icon = "link", Text = Loc("LOC_KX_OpenSharePage", "Open the sharing page"), Command = new RelayCommand(_ => { PlaySound("select"); OpenLink(post); }) });
            string note = communityStatus;
            communityStatus = null;

            if (tab == "mine")
            {
                BuildCommunityMine();
                SetPageStatus(note ?? (string.IsNullOrEmpty(CommunityRepo)
                    ? string.Format(Loc("LOC_KX_CommunityWhere3", "Uploading saves what you share in {0}, ready to post. Nothing is uploaded from here, and every one is checked before it goes in the gallery."), ShareFolder)
                    : Loc("LOC_KX_CommunityWhere4", "Upload sends what you share to the gallery (signed in to GitHub) as a request: every one is checked before it goes in. A copy is kept in Documents › Kippix Layouts › Share.")));
                CommunityFocusSoon();
                return;
            }
            foreach (string k in tab == "all" ? CommunityKinds : new[] { tab })
                if (CommunityCanUndo(k)) AddCommunityUndoChip(k);
            if (string.IsNullOrEmpty(CommunityRepo))
            {
                string none = Loc("LOC_KX_CommunityNoRepo5", "There's nothing to browse yet: the gallery needs Kippix's community page, which isn't set up. Uploading works already (Yours › Upload saves the files, ready to post), and so does adding one from a file on Yours.");
                SetPageStatus(note != null ? note + "\n\n" + none : none);
                CommunityFocusSoon();
                return;
            }
            var refresh = AddPageChip(Loc("LOC_KX_Refresh", "Refresh"), "refresh", () => { communityAt = DateTime.MinValue; PlaySound("select"); ShowCommunityPage(communityTab, "chip:refresh"); });
            refresh.Key = "chip:refresh";
            // (1.0.4) newest first, or most downloaded first
            var sort = AddPageChip(CommunitySortByDownloads ? Loc("LOC_KX_SortMostDownloaded", "Sorted: most downloaded") : Loc("LOC_KX_SortNewest", "Sorted: newest"), "sort", () =>
            {
                settings.CommunitySort = CommunitySortByDownloads ? "new" : "downloads";
                SaveSettings();
                PlaySound("move");
                ShowCommunityPage(communityTab, "chip:sort");
            });
            sort.Key = "chip:sort";
            if (communityLists != null && (DateTime.Now - communityAt).TotalMinutes < 30) { FillCommunityTiles(tab, note); CommunityFocusSoon(); return; }
            SetPageStatus(note != null ? note + "\n\n" + Loc("LOC_KX_Loading", "Loading…") : Loc("LOC_KX_Loading", "Loading…"));
            CommunityFocusSoon();
            int gen = pageGen;
            string url = CommunityRaw("index.json");
            string repo = CommunityRepo;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, List<CommunityItem>> lists = null;
                string error = null;
                try { lists = ReadCommunityIndex(url); }
                catch (Exception ex) { error = ex.Message; logger.Warn("Kippix: community gallery: " + ex.Message); }
                // (1.0.4) how many times each has been downloaded (GitHub counts the release's files); the gallery works without
                if (lists != null)
                    try { ApplyCommunityDownloads(lists, GitHubApi.ReleaseDownloads(repo, "community")); }
                    catch (Exception ex) { logger.Info("Kippix: community download counts: " + ex.Message); }
                OnUi(() =>
                {
                    communityError = error;
                    if (lists != null) { communityLists = lists; communityAt = DateTime.Now; }
                    if (gen != pageGen || !IsCommunityHub) return;
                    FillCommunityTiles(tab, note);
                    if (communityFocusKey != null || CommunityFocusLost()) CommunityFocusSoon();
                });
            });
        }

        /// <summary>The pictures for a gallery tab (All: every kind, newest first, with what kind each is).</summary>
        private void FillCommunityTiles(string tab, string note)
        {
            CommunityTiles.Clear();
            string status;
            if (communityLists == null) status = Loc("LOC_KX_CommunityOffline", "The gallery couldn't be reached. Check the internet connection and press Refresh.");
            else
            {
                var items = CommunitySorted(tab == "all"
                    ? CommunityKinds.SelectMany(k => CommunityListOf(k) ?? new List<CommunityItem>())
                    : (CommunityListOf(tab) ?? new List<CommunityItem>())).ToList();
                foreach (var c in items)
                {
                    var item = c;
                    string key = "c:" + item.Kind + ":" + item.Id;
                    string by = item.Author.Length > 0 ? string.Format(Loc("LOC_KX_ByAuthor", "by {0}"), item.Author) : "";
                    string what = item.Kind == "widgets" && item.WidgetType.Length > 0 ? CommunityWidgetKindName(item.WidgetType) : "";
                    CommunityTiles.Add(new CommunityTile
                    {
                        Text = item.Name, Key = key, Icon = CommunityKindIcon(item.Kind), Wide = true,
                        Sub = string.Join("   ·   ", new[] { by, what, CommunityDownloadsText(item), string.Join(", ", item.Tags.ToArray()) }.Where(x => x.Length > 0).ToArray()),
                        KindText = tab == "all" ? CommunityKindOne(item.Kind).ToUpper(CultureInfo.CurrentCulture) : "",
                        ImagePath = item.Preview.Length > 0 ? CommunityRaw(item.Preview) : null,
                        Right = CommunityHaveIt(item) ? Loc("LOC_KX_Got", "Saved here") : "",
                        Command = new RelayCommand(_ => { communityFocusKey = key; OpenPage("communityitem", item.Kind + "|" + item.Id, true); })
                    });
                }
                status = items.Count > 0 ? "" :
                    tab == "looks" ? Loc("LOC_KX_CommunityEmptyLooks3", "No look packs in the gallery yet. Be the first: Yours › Upload your look.")
                  : tab == "widgets" ? Loc("LOC_KX_CommunityEmptyWidgets3", "No widgets in the gallery yet. Be the first: Yours › Upload a widget.")
                  : tab == "layouts" ? Loc("LOC_KX_CommunityEmpty3", "No layouts in the gallery yet. Be the first: Yours › Upload your layout.")
                  : tab == "gamepages" ? Loc("LOC_KX_CommunityEmptyGamePages", "No game pages in the gallery yet. Be the first: Yours › Upload your game page.")
                  : Loc("LOC_KX_CommunityEmptyAll2", "Nothing in the gallery yet. Be the first: Yours › Upload shares yours.");
            }
            SetPageStatus(note != null ? (status.Length > 0 ? note + "\n\n" + status : note) : status);
            Notify("HasCommunityTiles");
            LoadCommunityTileImages();
        }

        /// <summary>The pictures, off the UI thread, at the size they're shown (stops if the page changes).</summary>
        private void LoadCommunityTileImages()
        {
            int gen = pageGen;
            var tiles = CommunityTiles.Where(t => !string.IsNullOrEmpty(t.ImagePath) && t.Thumb == null).ToList();
            if (tiles.Count == 0) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                foreach (var t in tiles)
                {
                    if (gen != pageGen) return;
                    System.Windows.Media.ImageSource img = null;
                    try { img = Images.Load(t.ImagePath, 480); } catch (Exception) { }
                    if (img != null) { var tt = t; OnUi(() => { if (gen == pageGen) tt.Thumb = img; }); }
                }
            });
        }

        // =====================================================================
        // Yours: your own back, your files, the community widgets
        // =====================================================================

        private void AddMineRow(string key, string icon, string text, string sub, Action run)
        {
            PageRows.Add(new PageRow { Key = key, Icon = icon, Text = text, Sub = sub, Command = new RelayCommand(_ => run()) });
        }

        private void BuildCommunityMine()
        {
            // UPLOAD: share yours (saved with a picture and its gallery entry, ready to post)
            AddPageHeader(Loc("LOC_KX_SecUpload", "UPLOAD"));
            AddMineRow("u:layouts", "upload", Loc("LOC_KX_UploadLayout", "Upload your layout"), Loc("LOC_KX_ShareLayoutSub2", "Your home screen layout, saved with a picture of it, ready to post"),
                       () => ShareToCommunity("layouts", null));
            AddMineRow("u:gamepages", "upload", Loc("LOC_KX_UploadGamePage", "Upload your game page"), Loc("LOC_KX_ShareGamePageSub", "Your game page layout (its design, what's on it and where): for a picture of it, upload it from a game's page"),
                       () => ShareToCommunity("gamepages", null));
            AddMineRow("u:looks", "upload", Loc("LOC_KX_UploadLook", "Upload your look"), string.Format(Loc("LOC_KX_ShareLookSub", "The look you're using, with a picture of it. Now: {0}"), LookPackText),
                       () => ShareToCommunity("looks", null));
            bool anyWidget = Widgets != null && Widgets.Any(w => w.Config != null);
            AddMineRow("u:widgets", "upload", Loc("LOC_KX_UploadWidget", "Upload a widget"),
                       anyWidget ? Loc("LOC_KX_ShareWidgetSub2", "One of your widgets or tiles (text, pictures too): you choose which next") : Loc("LOC_KX_NoWidgetsToShare", "No widgets yet: add one in Home settings › Widgets"),
                       () => { if (anyWidget) { communityFocusKey = "u:widgets"; OpenPage("communityshare", "widgets", true); } else PlaySound("back"); });

            AddCommunityGitHubRows();   // (1.0.1) signed in to GitHub, and your uploads

            // LAYOUTS
            AddPageHeader(Loc("LOC_KX_SecLayoutsUpper", "LAYOUTS"));
            if (communityUsedLayout && HasUndoReset)
                AddMineRow("m:undolayout", "refresh", Loc("LOC_KX_UndoLayoutRow", "Put your own layout back"), Loc("LOC_KX_UndoLayoutRowSub", "Undoes the community layout you used"),
                           () => { communityStatus = CommunityUndo("layouts"); PlaySound("select"); ShowCommunityPage("mine", "m:layouts"); });
            AddMineRow("m:layouts", "layout", Loc("LOC_KX_YourLayouts", "Your layouts"), Loc("LOC_KX_YourLayoutsSub", "Save, load and send layout files: Layouts & reset"),
                       () => CommunityToSettings("layouts"));
            AddMineRow("m:layoutfile", "download", Loc("LOC_KX_CommunityLayoutFile", "Add a layout from a file…"), Loc("LOC_KX_CommunityLayoutFileSub", "A layout someone sent you (.json, from Downloads, a USB stick…)"),
                       () => CommunityAddFromFile("layouts"));

            // GAME PAGES (0.89)
            AddPageHeader(Loc("LOC_KX_SecGamePagesUpper", "GAME PAGES"));
            if (CommunityCanUndo("gamepages"))
                AddMineRow("m:undogamepage", "refresh", Loc("LOC_KX_UndoGamePageRow", "Put your own game page back"), Loc("LOC_KX_UndoGamePageRowSub", "Undoes the game page layout you used"),
                           () => { communityStatus = CommunityUndo("gamepages"); PlaySound("select"); ShowCommunityPage("mine", "m:gamepages"); });
            AddMineRow("m:gamepages", "controller", Loc("LOC_KX_YourGamePages", "Your game page layouts"), Loc("LOC_KX_YourGamePagesRowSub", "Save the one you have, use or delete saved ones"),
                       () => { communityFocusKey = "m:gamepages"; OpenPage("gamepagelist", "", true); });
            AddMineRow("m:gamepagefile", "download", Loc("LOC_KX_CommunityGamePageFile", "Add a game page from a file…"), Loc("LOC_KX_CommunityGamePageFileSub", "A game page layout someone sent you (.json; a whole layout works too: its game page)"),
                       () => CommunityAddFromFile("gamepages"));

            // LOOK PACKS
            AddPageHeader(Loc("LOC_KX_SecLookPacksUpper", "LOOK PACKS"));
            if (communityLookBefore != null)
                AddMineRow("m:undolook", "refresh", Loc("LOC_KX_UndoLookRow", "Put your look back"),
                           string.Format(Loc("LOC_KX_UndoLookRowSub", "Back to {0}"), string.IsNullOrEmpty(communityLookBefore.Name) ? "Kippix" : communityLookBefore.Name),
                           () => { communityStatus = CommunityUndo("looks"); PlaySound("select"); ShowCommunityPage("mine", "m:looks"); });
            AddMineRow("m:looks", "sliders", Loc("LOC_KX_YourLookPacks", "Your look packs and Theme Studio"), Loc("LOC_KX_YourLookPacksSub", "Look and feel › Look packs"),
                       () => { lookStatus = null; CommunityToSettings("lookpacks"); });
            AddMineRow("m:lookfile", "download", Loc("LOC_KX_CommunityLookFile", "Add a look pack from a file…"), Loc("LOC_KX_LookImportSub2", "A .json look pack (or a saved layout: its look) from Downloads, a USB stick..."),
                       () => CommunityAddFromFile("looks"));

            // WIDGETS
            AddPageHeader(Loc("LOC_KX_SecWidgetsUpper", "WIDGETS"));
            PageRows.Add(new PageRow
            {
                Key = "m:switch", Icon = "globe", Text = Loc("LOC_KX_CommunityWidgets", "Community widgets"),
                Sub = settings.CommunityWidgetsOn ? Loc("LOC_KX_CommunityWidgetsOn", "Web snippets, news feeds and data widgets can go online (only while they're on screen)")
                                                  : Loc("LOC_KX_CommunityWidgetsOff", "Off: web snippets, news feeds and data widgets don't go online"),
                Right = settings.CommunityWidgetsOn ? Loc("LOC_KX_On", "On") : Loc("LOC_KX_Off", "Off"),
                Command = new RelayCommand(_ => { CommunityWidgetsOn = !CommunityWidgetsOn; PlaySound("move"); ShowCommunityPage("mine", "m:switch"); })
            });
            if (settings.CommunityWidgetsOn)
            {
                AddMineRow("m:web", "globe", WidgetTypeName("web"), Loc("LOC_KX_WgWebSub", "A picture of a web page, taken again every so often"), () => CommunityAddWidget("web"));
                AddMineRow("m:feed", "news", WidgetTypeName("feed"), Loc("LOC_KX_WgFeedSub", "Headlines from an RSS or Atom feed"), () => CommunityAddWidget("feed"));
                AddMineRow("m:data", "chart", WidgetTypeName("data"), Loc("LOC_KX_WgDataSub", "Values from an address that gives JSON, in words of your own"), () => CommunityAddWidget("data"));
            }
            AddMineRow("m:widgetfile", "download", Loc("LOC_KX_WidgetImport", "Add from a widget file…"), string.Format(Loc("LOC_KX_WidgetImportSub", "A widget someone shared (.json, from {0})"), WidgetFilesFolder),
                       () => CommunityAddFromFile("widgets"));
            if (CommunityCanUndo("widgets"))
                AddMineRow("m:undowidget", "remove", Loc("LOC_KX_UndoWidgetRow", "Take off the widget you just added"), Loc("LOC_KX_UndoWidgetRowSub", "From the community gallery or a widget file"),
                           () => { communityStatus = CommunityUndo("widgets"); ShowCommunityPage("mine", "m:widgetfile"); });

            // SHARING
            string postAll = CommunityPostUrl(null);
            if (postAll != null)
            {
                AddPageHeader(Loc("LOC_KX_SecSharing", "SHARING"));
                AddMineRow("m:post", "link", Loc("LOC_KX_OpenSharePage", "Open the sharing page"), Loc("LOC_KX_OpenSharePageSub", "Kippix's GitHub page: post the files you saved, with a line about them"),
                           () => { PlaySound("select"); OpenLink(postAll); });
            }
            NotifyPageLists();
        }

        /// <summary>One of the settings pages (Layouts &amp; reset, Look packs): the settings open on it.</summary>
        private void CommunityToSettings(string page)
        {
            PlaySound("select");
            TogglePanel("options");   // (from the page: the pages go)
            if (openPanel != "options") return;
            SetOptionsPage(page);
        }

        // =====================================================================
        // Upload and Download: what kind?
        // =====================================================================

        private bool communityChooserFresh;   // Upload / Download just opened (not come back to): the highlight on the kind of the tab you were on

        private void OpenCommunityChooser(string page, string chipKey)
        {
            communityFocusKey = chipKey;   // (B comes back on the button)
            communityChooserFresh = true;
            OpenPage(page, "", true);
        }

        /// <summary>After the pages' own first highlight: on this row (only when the chooser has just been opened).</summary>
        private void CommunityChooserFocus(string key)
        {
            bool fresh = communityChooserFresh;
            communityChooserFresh = false;
            if (!fresh) return;
            var row = PageRows.FirstOrDefault(r => r.Key == key);
            if (row == null) return;
            int gen = pageGen;
            ui.BeginInvoke(new Action(() =>
            {
                if (gen == pageGen && openPanel == "page" && PageRows.Contains(row)) UiTools.FocusItem("KX_PageRowList", row);
            }), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>Upload: a layout, a look pack or a widget (arg "widgets": which one).</summary>
        private void BuildCommunitySharePage(string arg)
        {
            if (arg == "widgets") { communityChooserFresh = false; BuildCommunityShareWidgets(); return; }
            PageTitle = Loc("LOC_KX_CommunityUpload", "Upload");
            PageSub = Loc("LOC_KX_CommunityUploadSub", "What do you want to share?");
            bool anyWidget = Widgets != null && Widgets.Any(w => w.Config != null);
            AddPageHeader(Loc("LOC_KX_CommunityShareWhat", "SHARE"));
            PageRows.Add(new PageRow { Key = "u:layouts", Icon = "layout", Text = Loc("LOC_KX_CommunityKindLayout", "Layout"), Sub = Loc("LOC_KX_ShareLayoutSub2", "Your home screen layout, saved with a picture of it, ready to post"),
                                       Command = new RelayCommand(_ => ShareToCommunity("layouts", null)) });
            PageRows.Add(new PageRow { Key = "u:gamepages", Icon = "controller", Text = Loc("LOC_KX_CommunityKindGamePage", "Game page layout"), Sub = Loc("LOC_KX_ShareGamePageSub", "Your game page layout (its design, what's on it and where): for a picture of it, upload it from a game's page"),
                                       Command = new RelayCommand(_ => ShareToCommunity("gamepages", null)) });
            PageRows.Add(new PageRow { Key = "u:looks", Icon = "palette", Text = Loc("LOC_KX_CommunityKindLook", "Look pack"), Sub = string.Format(Loc("LOC_KX_ShareLookSub", "The look you're using, with a picture of it. Now: {0}"), LookPackText),
                                       Command = new RelayCommand(_ => ShareToCommunity("looks", null)) });
            PageRows.Add(new PageRow
            {
                Key = "u:widgets", Icon = "grid", Text = Loc("LOC_KX_CommunityKindWidget", "Widget"),
                Sub = anyWidget ? Loc("LOC_KX_ShareWidgetSub2", "One of your widgets or tiles (text, pictures too): you choose which next") : Loc("LOC_KX_NoWidgetsToShare", "No widgets yet: add one in Home settings › Widgets"),
                Command = new RelayCommand(_ => { if (anyWidget) OpenPage("communityshare", "widgets", true); else PlaySound("back"); })
            });
            string post = CommunityPostUrl(null);
            if (post != null)
            {
                AddPageHeader(Loc("LOC_KX_SecSharing", "SHARING"));
                PageRows.Add(new PageRow { Key = "u:post", Icon = "link", Text = Loc("LOC_KX_OpenSharePage", "Open the sharing page"), Sub = Loc("LOC_KX_OpenSharePageSub", "Kippix's GitHub page: post the files you saved, with a line about them"),
                                           Command = new RelayCommand(_ => { PlaySound("select"); OpenLink(post); }) });
            }
            SetPageStatus(string.Format(Loc("LOC_KX_CommunityUploadNote", "Nothing is uploaded from here: it's saved in {0} with a picture and its gallery entry, ready for you to post. Every one is checked before it goes in the gallery."), ShareFolder));
            CommunityChooserFocus("u:" + communityTab);
        }

        private void BuildCommunityShareWidgets()
        {
            PageTitle = Loc("LOC_KX_ShareAWidget", "Share a widget");
            PageSub = Loc("LOC_KX_CommunityPickIntro", "Which widget? It's saved with a picture of your home screen, ready to post.");
            var list = Widgets != null ? Widgets.Where(x => x.Config != null).ToList() : new List<WidgetItem>();
            if (list.Count == 0) { SetPageStatus(Loc("LOC_KX_NoWidgetsToShare", "No widgets yet: add one in Home settings › Widgets")); return; }
            int i = 0;
            foreach (var w in list)
            {
                var item = w;
                string type = w.Config.Type;
                PageRows.Add(new PageRow
                {
                    Key = "uw:" + (i++), Icon = IsCustomType(type) ? (type == "image" ? "picture" : type == "text" ? "note" : "globe") : "grid",
                    Text = WidgetFullName(w.Config), Sub = WidgetTypeName(type), Command = new RelayCommand(_ => ShareToCommunity("widgets", item))
                });
            }
        }

        /// <summary>Download: everything or one kind (that tab, fetched again), or one from a file.</summary>
        private void BuildCommunityGetPage(string arg)
        {
            PageTitle = Loc("LOC_KX_CommunityDownload", "Download");
            PageSub = Loc("LOC_KX_CommunityDownloadSub", "What are you looking for?");
            bool repo = !string.IsNullOrEmpty(CommunityRepo);
            AddPageHeader(Loc("LOC_KX_CommunityFromGallery", "FROM THE GALLERY"));
            foreach (string k in new[] { "all", "layouts", "gamepages", "looks", "widgets" })
            {
                string tab = k;
                PageRows.Add(new PageRow
                {
                    Key = "d:" + tab, Icon = CommunityTabIcon(tab),
                    Text = tab == "all" ? Loc("LOC_KX_CommunityEverything", "Everything") : CommunityTabName(tab),
                    Sub = tab == "all" ? Loc("LOC_KX_CommunityEverythingSub2", "Layouts, game pages, look packs and widgets, newest first") : CommunityKindTitle(tab),
                    Command = new RelayCommand(_ => { communityAt = DateTime.MinValue; PlaySound("select"); ShowCommunityPage(tab, null); })
                });
            }
            AddPageHeader(Loc("LOC_KX_CommunityFromFile", "FROM A FILE"));
            PageRows.Add(new PageRow { Key = "f:layouts", Icon = "layout", Text = Loc("LOC_KX_CommunityLayoutFile", "Add a layout from a file…"), Sub = Loc("LOC_KX_CommunityLayoutFileSub", "A layout someone sent you (.json, from Downloads, a USB stick…)"),
                                       Command = new RelayCommand(_ => CommunityAddFromFile("layouts")) });
            PageRows.Add(new PageRow { Key = "f:gamepages", Icon = "controller", Text = Loc("LOC_KX_CommunityGamePageFile", "Add a game page from a file…"), Sub = Loc("LOC_KX_CommunityGamePageFileSub", "A game page layout someone sent you (.json; a whole layout works too: its game page)"),
                                       Command = new RelayCommand(_ => CommunityAddFromFile("gamepages")) });
            PageRows.Add(new PageRow { Key = "f:looks", Icon = "palette", Text = Loc("LOC_KX_CommunityLookFile", "Add a look pack from a file…"), Sub = Loc("LOC_KX_LookImportSub2", "A .json look pack (or a saved layout: its look) from Downloads, a USB stick..."),
                                       Command = new RelayCommand(_ => CommunityAddFromFile("looks")) });
            PageRows.Add(new PageRow { Key = "f:widgets", Icon = "grid", Text = Loc("LOC_KX_WidgetImport", "Add from a widget file…"), Sub = string.Format(Loc("LOC_KX_WidgetImportSub", "A widget someone shared (.json, from {0})"), WidgetFilesFolder),
                                       Command = new RelayCommand(_ => CommunityAddFromFile("widgets")) });
            if (!repo) SetPageStatus(Loc("LOC_KX_CommunityGetNoRepo", "The gallery needs Kippix's community page, which isn't set up yet, so there's nothing to browse. Adding one from a file works already."));
            CommunityChooserFocus(communityTab == "mine" ? "f:layouts" : "d:" + communityTab);
        }

        // =====================================================================
        // Adding from a file, adding a widget: through the settings, then back here
        // =====================================================================

        /// <summary>The settings open with the page "community" behind (the file picker's B, and what it does next, come back here).</summary>
        private bool CommunityOptionsBackdrop()
        {
            TogglePanel("options");   // (from the page: the pages go)
            if (openPanel != "options") return false;
            communityNoRedirect = true;
            try { SetOptionsPage("community", false); }
            finally { communityNoRedirect = false; }
            return true;
        }

        private void CommunityAddFromFile(string kind)
        {
            if (!CommunityOptionsBackdrop()) return;
            if (kind == "looks") { PlaySound("select"); ImportLookPack(); }   // (LookPacks.cs: back to "community", its message here)
            else if (kind == "widgets") ImportWidgetFile();                   // (CommunityWidgets.cs: placed, then back here)
            else if (kind == "gamepages") CommunityGamePageFile();           // (GamePageShare.cs)
            else CommunityLayoutFile();
        }

        /// <summary>A layout file someone sent: copied into the Uploaded folder (it shows in Layouts &amp; reset › Layout).</summary>
        private void CommunityLayoutFile()
        {
            PlaySound("select");
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            OpenPicker(Loc("LOC_KX_CommunityLayoutFile", "Add a layout from a file…"), false, new[] { ".json" }, Directory.Exists(start) ? start : "", "community", file =>
            {
                string msg;
                try
                {
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    if (new FileInfo(file).Length > 40L * 1024 * 1024) throw new InvalidDataException("too big");
                    var test = js.Deserialize<HelperSettings>(File.ReadAllText(file));
                    if (test == null) throw new InvalidDataException("not a layout");
                    Directory.CreateDirectory(UploadedFolder);
                    string name = Path.GetFileNameWithoutExtension(file);
                    string target = Path.Combine(UploadedFolder, name + ".json");
                    for (int i = 2; File.Exists(target) && !string.Equals(Path.GetFullPath(target), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase); i++)
                        target = Path.Combine(UploadedFolder, name + " (" + i + ").json");
                    if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) File.Copy(file, target);
                    RefreshLayoutProfiles();
                    PlaySound("select");
                    msg = string.Format(Loc("LOC_KX_CommunityLayoutAdded", "Added \"{0}\". Use it from Settings › Layouts & reset › Layout (Yours › Your layouts)."), Path.GetFileNameWithoutExtension(target));
                }
                catch (Exception ex)
                {
                    PlaySound("back");
                    msg = Loc("LOC_KX_LayoutBad", "That layout file couldn't be read.");
                    logger.Warn("Kippix: community layout file: " + ex.Message);
                }
                communityStatus = msg;
                GoBackTo("community", "community");
            });
        }

        /// <summary>A web snippet / news feed / data widget from Yours: its address (and words), then placing it, then back here.</summary>
        private void CommunityAddWidget(string type)
        {
            if (!settings.CommunityWidgetsOn) { PlaySound("back"); return; }
            PlaySound("select");
            var c = new WidgetConfig { Id = Guid.NewGuid().ToString("N"), Type = type, Scale = 1 };
            SetupCustomConfig(c);
            AskCommunityAddress(c, () => CommunityPlaceWidget(c));
        }

        /// <summary>A widget made here (the gallery, an address): the settings behind, placed by hand, then back to this page.</summary>
        private void CommunityPlaceWidget(WidgetConfig c)
        {
            ClosePanels();
            ui.BeginInvoke(new Action(() =>
            {
                TogglePanel("options");
                if (openPanel != "options") return;
                communityNoRedirect = true;
                try { SetOptionsPage("community", false); }
                finally { communityNoRedirect = false; }
                communityWidgetAdded = c.Id;
                communityPlacingId = c.Id;
                widgetNewReturn = "community";
                AddWidgetConfig(c.Type, c.Subject, c);
            }), DispatcherPriority.Loaded);
        }

        // =====================================================================
        // Hooks from the settings panel (the settings page "community" is only the way back to this page)
        // =====================================================================

        /// <summary>From OptionsTitle.</summary>
        private string CommunityOptionsTitle(string page)
        {
            return page == "community" ? Loc("LOC_KX_CatCommunity", "Community") : null;
        }

        /// <summary>From OptionsParent.</summary>
        private string CommunityOptionsParent(string page)
        {
            return page == "community" ? "" : null;
        }

        /// <summary>From BackRowKey: the row to land on going back.</summary>
        private string CommunityBackRowKey(string page)
        {
            return page == "community" ? "community" : null;
        }

        /// <summary>From SetOptionsPage: "community" opens the Community page (from the root or Settings search: B goes back there).</summary>
        private void CommunityOptionsOpened(string page)
        {
            string prev = communityPrevPage;
            communityPrevPage = page;
            if (page == "widgetadd" || page == "widgets") widgetNewReturn = null;
            if (page != "community" || communityNoRedirect) return;
            if (prev == null || prev == "" || prev == "settingssearch")
            {
                communityFrom = "options";
                communityFromPage = prev ?? "";
                communityFromKey = "community";
                communityTab = "all";
            }
            ui.BeginInvoke(new Action(() =>
            {
                if (openPanel != "options" || optionsPage != "community") return;
                ShowCommunityPage(communityTab, null);
            }), DispatcherPriority.Loaded);
        }

        // ---------------- Settings search: words people look for that aren't on a row ----------------

        /// <summary>From IndexSettings: extra hits (time limits by "screen time" / "parental", the community by "share" / "download").</summary>
        private void CommunitySearchExtras(List<SettingHit> hits)
        {
            var titles = new Dictionary<string, string>();
            if (settings.ProfilesEnabled)
            {
                var first = settings.Profiles != null ? settings.Profiles.FirstOrDefault(p => p != null && p.Id != GuestId) : null;
                hits.Add(new SettingHit
                {
                    Page = "profiles", Title = Loc("LOC_KX_LimitsTitle", "Daily time limits"),
                    Sub = "time limit limits screen time parental controls kids children play time hours a day bedtime " + Loc("LOC_KX_LimitsKeywords", "time limit screen time parental controls kids"),
                    Path = PagePath("profiles", titles), Key = first != null ? "limit:" + first.Id : "limits"
                });
            }
            else
            {
                hits.Add(new SettingHit
                {
                    Page = "", Title = Loc("LOC_KX_LimitsNeedProfiles", "Daily time limits (need Profiles)"),
                    Sub = "time limit limits screen time parental controls kids children play time profiles " + Loc("LOC_KX_LimitsKeywords", "time limit screen time parental controls kids"),
                    Path = Loc("LOC_KX_LimitsNeedProfilesPath", "Profiles: desktop mode › Add-ons › Kippix Helper › Testing"),
                    Run = () =>
                    {
                        PlaySound("select");
                        try { ShowNotice(Loc("LOC_KX_LimitHeader", "DAILY TIME"), Loc("LOC_KX_LimitsNeedProfiles", "Daily time limits (need Profiles)"),
                                         Loc("LOC_KX_LimitsNeedProfilesLine", "Switch on Profiles in desktop mode: Add-ons › Kippix Helper › Testing."),
                                         Loc("LOC_KX_LimitsNeedProfilesLine2", "Then Settings › Profiles › Daily time limits."), null); } catch (Exception) { }
                    }
                });
            }
            hits.Add(new SettingHit
            {
                Page = "community", Title = Loc("LOC_KX_CatCommunity", "Community"),
                Sub = "community share upload download gallery github layouts game pages game page layouts look packs widgets themes " + Loc("LOC_KX_CatCommunitySub", "Layouts, look packs and widgets: get other people's, share yours"),
                Path = PagePath("community", titles), Key = "community",
                Run = () => OpenCommunity("all", null, false)
            });
        }
    }
}
