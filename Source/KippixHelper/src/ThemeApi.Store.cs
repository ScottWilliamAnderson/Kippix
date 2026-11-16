using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>A game in the store view (a tile, and the game's own page).</summary>
    public class StoreItem : Observable
    {
        public string Store { get; set; }          // Steam, GOG, Epic, or "" for every store (merged / IsThereAnyDeal)
        public string Id { get; set; }
        public string Name { get; set; }
        public string Image { get; set; }
        private string priceText, oldPriceText, discountText, note;
        public string PriceText { get { return priceText; } set { if (Set(ref priceText, value, "PriceText")) Notify("HasPrice"); } }
        public string OldPriceText { get { return oldPriceText; } set { if (Set(ref oldPriceText, value, "OldPriceText")) Notify("HasOldPrice"); } }
        public string DiscountText { get { return discountText; } set { if (Set(ref discountText, value, "DiscountText")) Notify("HasDiscount"); } }
        public string Note { get { return note; } set { if (Set(ref note, value, "Note")) Notify("HasNote"); } }
        public string Link { get; set; }           // web page
        public string AppLink { get; set; }        // store app link (steam://, Epic launcher) when there is one
        public bool HasDiscount { get { return !string.IsNullOrEmpty(DiscountText); } }
        public bool HasOldPrice { get { return !string.IsNullOrEmpty(OldPriceText); } }
        public bool HasNote { get { return !string.IsNullOrEmpty(Note); } }
        public bool HasPrice { get { return !string.IsNullOrEmpty(PriceText); } }
        public ICommand OpenCommand { get; set; }

        private ImageSource thumb;
        /// <summary>The picture, downloaded and shrunk off the UI thread (tries the fallbacks when the first one is missing).</summary>
        public ImageSource Thumb { get { return thumb; } set { Set(ref thumb, value, "Thumb"); } }

        internal List<string> ImageFallbacks;
        internal List<StoreItem> Merged;           // every store's entry for a merged tile
        private bool isOwned;
        /// <summary>Already in the Playnite library (same name).</summary>
        public bool IsOwned { get { return isOwned; } set { Set(ref isOwned, value, "IsOwned"); } }
        private bool isDropped;
        /// <summary>Wishlist: cheaper than when it was added.</summary>
        public bool IsDropped { get { return isDropped; } set { Set(ref isDropped, value, "IsDropped"); } }
        // trailer preview on the tile (the video exists only while it plays)
        private bool isPreviewing;
        public bool IsPreviewing { get { return isPreviewing; } set { Set(ref isPreviewing, value, "IsPreviewing"); } }
        private Uri previewVideo;
        public Uri PreviewVideo { get { return previewVideo; } set { Set(ref previewVideo, value, "PreviewVideo"); } }
        private bool previewMuted = true;
        public bool PreviewMuted { get { return previewMuted; } set { Set(ref previewMuted, value, "PreviewMuted"); } }
        internal string SteamAppId;
        internal string ItadId;
        internal string Description;
        internal bool ThumbRequested;
        internal string Released;          // Steam's release date text ("12 Nov, 2026", "Q1 2027")
        private bool isWished;
        public bool IsWished { get { return isWished; } set { Set(ref isWished, value, "IsWished"); } }
    }

    /// <summary>A trailer or screenshot on a store game's page.</summary>
    public class StoreMedia : Observable
    {
        public bool IsVideo { get; set; }
        public string Title { get; set; }
        public ICommand OpenCommand { get; set; }
        private ImageSource thumb;
        public ImageSource Thumb { get { return thumb; } set { Set(ref thumb, value, "Thumb"); } }
        internal string Kind;      // image, steam (trailer we download and play), web (YouTube: opens in the browser)
        internal string Key;       // image url / Steam app id / web address
        internal string ThumbUrl;
    }

    /// <summary>A review on a store game's page.</summary>
    public class StoreReview
    {
        public bool Positive { get; set; }
        public string Verdict { get; set; }
        public string Text { get; set; }
        public string Meta { get; set; }
        public ICommand OpenCommand { get; set; }
        internal string FullText;
    }

    /// <summary>A small label on a store game's page (controller support, Steam Deck, subscriptions, owned).</summary>
    public class StoreBadge
    {
        public string Text { get; set; }
        public bool IsGood { get; set; }
        public bool IsOwned { get; set; }   // "In your library" (with a tick)
    }

    /// <summary>One store's price for the game on its page.</summary>
    public class StorePrice
    {
        public string StoreName { get; set; }
        public string PriceText { get; set; }
        public string OldPriceText { get; set; }
        public string DiscountText { get; set; }
        public string Note { get; set; }
        public bool IsBest { get; set; }
        public bool HasDiscount { get { return !string.IsNullOrEmpty(DiscountText); } }
        public bool HasOldPrice { get { return !string.IsNullOrEmpty(OldPriceText); } }
        public bool HasNote { get { return !string.IsNullOrEmpty(Note); } }
        public ICommand OpenCommand { get; set; }
        internal decimal Amount = -1;
        internal string Currency = "";
    }

    /// <summary>A store tile on the Store's first page.</summary>
    public class StoreTile : Observable
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Sub { get; set; }
        public bool HasSub { get { return !string.IsNullOrEmpty(Sub); } }
        public Brush Brush { get; set; }
        public bool IsWeb { get; set; }
        public ICommand Command { get; set; }
        private ImageSource logo;
        /// <summary>The store's own icon (from its website), shown next to the name.</summary>
        public ImageSource Logo { get { return logo; } set { if (Set(ref logo, value, "Logo")) Notify("HasLogo"); } }
        public bool HasLogo { get { return logo != null; } }
    }

    /// <summary>A store's on/off switch in the Store settings.</summary>
    public class StoreToggle : Observable
    {
        public string Id { get; set; }
        public string Name { get; set; }
        internal Action<StoreToggle> Changed;
        private bool isOn;
        public bool IsOn { get { return isOn; } set { if (Set(ref isOn, value, "IsOn") && Changed != null) Changed(this); } }
    }

    /// <summary>
    /// The Store: every store as a tile; Steam, GOG and "All stores" browsed in the theme with search, sort, genre / tag,
    /// price and sale filters (paging through the whole catalogue); Epic's free games; the rest open their own site in
    /// the theme's browser. A game's page has its pictures and trailer (full screen viewer) and every store's price.
    /// </summary>
    public partial class ThemeApi
    {
        // id, name, subtitle key, subtitle, colour from, colour to, website, IsThereAnyDeal shop id (its games in the theme), site for the icon
        private static readonly string[][] StoreDefs =
        {
            new[] { "steam", "Steam", "LOC_KX_StoreSteamSub", "Every game on Steam, by genre, tag and price", "#FF1B3A5C", "#FF101216", "https://store.steampowered.com/", "61", "store.steampowered.com" },
            new[] { "epic", "Epic Games", "LOC_KX_StoreEpicSub", "This week's free games, and the full store", "#FF34383F", "#FF101216", "https://store.epicgames.com/browse", "16", "store.epicgames.com" },
            new[] { "gog", "GOG", "LOC_KX_StoreGogSub", "DRM-free games, by genre, tag and price", "#FF4A2A6B", "#FF101216", "https://www.gog.com/", "35", "www.gog.com" },
            new[] { "ea", "EA app", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF7A1E2A", "#FF101216", "https://www.ea.com/games", "52", "www.ea.com" },
            new[] { "ubisoft", "Ubisoft Store", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF1C3C78", "#FF101216", "https://store.ubisoft.com/", "62", "store.ubisoft.com" },
            new[] { "rockstar", "Rockstar Store", "", "", "#FF6B5A12", "#FF101216", "https://store.rockstargames.com/", "", "www.rockstargames.com" },
            new[] { "battlenet", "Battle.net", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF0E4A73", "#FF101216", "https://shop.battle.net/", "4", "battle.net" },
            new[] { "xbox", "Microsoft Game Store", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF145A1E", "#FF101216", "https://www.xbox.com/games/browse", "48", "www.xbox.com" },
            new[] { "humble", "Humble Store", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF8A3A1C", "#FF101216", "https://www.humblebundle.com/store", "37", "www.humblebundle.com" },
            new[] { "fanatical", "Fanatical", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF7A2050", "#FF101216", "https://www.fanatical.com/", "6", "www.fanatical.com" },
            new[] { "gmg", "Green Man Gaming", "LOC_KX_StoreShopSub", "Its games and deals, priced against every store", "#FF1E5C3A", "#FF101216", "https://www.greenmangaming.com/", "36", "www.greenmangaming.com" },
            new[] { "itch", "itch.io", "", "", "#FF7A2A2A", "#FF101216", "https://itch.io/games", "", "itch.io" },
        };

        private static string[] StoreDef(string id) { return StoreDefs.FirstOrDefault(d => d[0] == id); }

        /// <summary>Stores whose games come from IsThereAnyDeal (everything but Steam and GOG, which have their own catalogues).</summary>
        private bool IsShopStore(string id)
        {
            var d = StoreDef(id);
            return d != null && id != "steam" && id != "gog" && d[7].Length > 0 && HasItadKey;
        }

        private int ShopId(string id)
        {
            var d = StoreDef(id);
            int n;
            return d != null && int.TryParse(d[7], out n) ? n : 0;
        }

        /// <summary>Device pixels per 1080p unit (the theme is drawn at 1080p and scaled to the screen): pictures are decoded this sharp.</summary>
        private double storeScale = 1;

        private void MeasureStoreScale()
        {
            try
            {
                var w = Application.Current.MainWindow;
                var src = PresentationSource.FromVisual(w);
                double dpi = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformToDevice.M22 : 1;
                double h = w.ActualHeight > 0 ? w.ActualHeight : 1080;
                storeScale = Math.Max(1, Math.Min(2.5, h / 1080.0 * dpi));
            }
            catch (Exception) { storeScale = 1; }
        }

        private int Px(double units) { return (int)Math.Ceiling(units * storeScale); }

        private string storePage = "home";       // home, list, game
        private string storeId = "";             // steam, gog, epic, all
        private StoreQuery storeQuery = new StoreQuery();
        private int storePageNo;
        private bool storeHasMore, storeLoadingMore;
        private int storeTotal = -1;
        private int storeRequest, storeListGen, storeGameGen;
        private StoreItem storeGame;
        private StoreItem storeListFocus;
        private HashSet<string> libraryKeys;
        private Dictionary<string, List<string>> librarySources = new Dictionary<string, List<string>>();

        /// <summary>"In your library (Steam)" / "(Steam, GOG)" / plain when Playnite doesn't know the source.</summary>
        private string OwnedBadgeText(StoreItem item)
        {
            List<string> src;
            if (item != null && librarySources != null && librarySources.TryGetValue(NameKey(item.Name), out src) && src.Count > 0)
                return string.Format(Loc("LOC_KX_StoreOwnedOn", "In your library ({0})"), string.Join(", ", src.ToArray()));
            return Loc("LOC_KX_StoreOwned", "In your library");
        }
        private bool storeListHooked;
        private bool storeSettingsFromStore;

        public ObservableCollection<StoreItem> StoreItems { get; private set; }
        public ObservableCollection<PickOption> StoreChips { get; private set; }
        public ObservableCollection<StorePrice> StorePrices { get; private set; }
        public ObservableCollection<StoreMedia> StoreMedia { get; private set; }
        public ObservableCollection<StoreTile> StoreTiles { get; private set; }
        public ObservableCollection<StoreToggle> StoreToggles { get; private set; }
        public ObservableCollection<PickOption> StorePickerItems { get; private set; }
        public ObservableCollection<PickOption> StoreRegionOptions { get; private set; }

        public ICommand OpenStoreCommand { get; private set; }
        public ICommand StoreSearchCommand { get; private set; }
        public ICommand StoreSettingsCommand { get; private set; }
        public ICommand StoreViewerActionCommand { get; private set; }
        public ICommand StoreOpenPageCommand { get; private set; }
        public ICommand CycleStoreOpenInCommand { get; private set; }
        public ICommand CycleStoreTrailersCommand { get; private set; }
        public ICommand CycleStoreImageQualityCommand { get; private set; }
        public ICommand CycleCursorSpeedCommand { get; private set; }

        private void InitStoreCommands()
        {
            InitStoreExtras();
            StoreItems = new ObservableCollection<StoreItem>();
            StoreChips = new ObservableCollection<PickOption>();
            StorePrices = new ObservableCollection<StorePrice>();
            StoreMedia = new ObservableCollection<StoreMedia>();
            StoreTiles = new ObservableCollection<StoreTile>();
            StoreToggles = new ObservableCollection<StoreToggle>();
            StorePickerItems = new ObservableCollection<PickOption>();
            StoreRegionOptions = new ObservableCollection<PickOption>();
            StoreReviews = new ObservableCollection<StoreReview>();
            OpenStoreCommand = new RelayCommand(_ => OpenStore());
            StoreSearchCommand = new RelayCommand(_ => AskStoreSearch());
            StoreSettingsCommand = new RelayCommand(_ => OpenStoreSettings());
            StoreViewerActionCommand = new RelayCommand(_ => StoreViewerAction());
            StoreOpenPageCommand = new RelayCommand(_ => { if (storeGame != null) OpenStoreLink(StoreGameWebLink(), storeGame.AppLink); });
            CycleStoreOpenInCommand = new RelayCommand(_ => { settings.StoreOpenIn = settings.StoreOpenIn == 0 ? 1 : 0; SaveSettings(); NotifyStoreSettings(); });
            CycleStoreTrailersCommand = new RelayCommand(_ => { settings.StoreTrailers = (settings.StoreTrailers + 1) % 3; SaveSettings(); NotifyStoreSettings(); });
            CycleStoreImageQualityCommand = new RelayCommand(_ => { settings.StoreImageQuality = (Math.Max(0, Math.Min(2, settings.StoreImageQuality)) + 1) % 3; SaveSettings(); gamePageCache.Clear(); NotifyStoreSettings(); });
            CycleCursorSpeedCommand = new RelayCommand(_ => { settings.BrowserCursorSpeed = settings.BrowserCursorSpeed % 5 + 1; SaveSettings(); NotifyStoreSettings(); });
        }

        // ---------------- what the theme binds to ----------------

        /// <summary>Also while the Store settings, opened from the Store's gear, sit over it.</summary>
        public bool IsStoreOpen { get { return openPanel == "store" || (openPanel == "options" && storeSettingsFromStore); } }
        public bool IsStoreHome { get { return storePage == "home"; } }
        public bool IsStoreList { get { return storePage == "list"; } }
        public bool IsStoreGame { get { return storePage == "game"; } }
        public string StoreTitle
        {
            get
            {
                if (storePage == "home") return Loc("LOC_KX_Store", "Store");
                if (storeId == "all") return Loc("LOC_KX_StoreAll", "All stores");
                if (storeId == "wishlist") return Loc("LOC_KX_RowWish", "Your wishlist");
                if (storeId == "dlc") return Loc("LOC_KX_AddOns", "Add-ons");
                if (storeId == "similar") return Loc("LOC_KX_MoreLikeThis", "More like this");
                var def = StoreDefs.FirstOrDefault(d => d[0] == storeId);
                return def != null ? def[1] : Loc("LOC_KX_Store", "Store");
            }
        }
        public string StoreSectionTitle { get; private set; }
        public string StoreStatus { get; private set; }
        public bool HasStoreStatus { get { return !string.IsNullOrEmpty(StoreStatus); } }
        public bool StoreLoadingMore { get { return storeLoadingMore; } }
        public int StoreTileColumns { get { return Math.Max(1, Math.Min(4, StoreTiles.Count)); } }
        public double StoreTileHeight { get { return StoreTiles.Count <= 4 ? 380 : StoreTiles.Count <= 8 ? 230 : 160; } }

        public StoreItem StoreGame { get { return storeGame; } }
        public ImageSource StoreHeroImage { get; private set; }
        public string StoreGameDescription { get; private set; }
        public string StoreGameFacts { get; private set; }
        public bool StoreGameOwned { get; private set; }
        public bool StorePricesLoading { get; private set; }
        public string StoreLowestText { get; private set; }
        public bool HasStoreLowest { get { return !string.IsNullOrEmpty(StoreLowestText); } }
        public bool HasStoreMedia { get { return StoreMedia != null && StoreMedia.Count > 0; } }
        public string StorePriceSource { get; private set; }
        public ObservableCollection<StoreReview> StoreReviews { get; private set; }
        public string StoreReviewTitle { get; private set; }
        public string StoreReviewLine { get; private set; }
        public string StoreScores { get; private set; }
        public bool HasStoreReviewInfo { get { return !string.IsNullOrEmpty(StoreReviewTitle) || !string.IsNullOrEmpty(StoreReviewLine) || !string.IsNullOrEmpty(StoreScores) || (StoreReviews != null && StoreReviews.Count > 0); } }
        public bool HasStoreScores { get { return !string.IsNullOrEmpty(StoreScores); } }
        public bool HasStoreReviewLine { get { return !string.IsNullOrEmpty(StoreReviewLine); } }

        private void NotifyStore()
        {
            foreach (string n in new[] { "IsStoreOpen", "IsStoreHome", "IsStoreList", "IsStoreGame", "StoreTitle", "StoreSectionTitle", "StoreStatus",
                                         "HasStoreStatus", "StoreGame", "StoreHeroImage", "StoreGameDescription", "StoreGameFacts", "StoreGameOwned",
                                         "StorePricesLoading", "StoreLowestText", "HasStoreLowest", "HasStoreMedia", "StorePriceSource",
                                         "StoreReviewTitle", "StoreReviewLine", "StoreScores", "HasStoreReviewInfo", "HasStoreScores", "HasStoreReviewLine",
                                         "StoreTileColumns", "StoreTileHeight", "StoreLoadingMore", "HasStoreDescription" })
                Notify(n);
            NotifyStoreMore();
            NotifyStoreOverlays();
        }

        private void SetStoreStatus(string text) { StoreStatus = text ?? ""; Notify("StoreStatus"); Notify("HasStoreStatus"); }
        private void SetStoreSection(string title) { StoreSectionTitle = title ?? ""; Notify("StoreSectionTitle"); }

        // ---------------- the first page: store tiles ----------------

        private void BuildStoreTiles()
        {
            StoreTiles.Clear();
            var on = new HashSet<string>((settings.StoreTiles ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var d in StoreDefs)
            {
                if (!on.Contains(d[0])) continue;
                bool web = d[0] != "steam" && d[0] != "gog" && d[0] != "epic" && !IsShopStore(d[0]);
                var tile = MakeTile(d[0], d[1], web ? "" : Loc(d[2], d[3]), d[4], d[5], web);
                StoreTiles.Add(tile);
                var t = tile;
                string site = d[8];
                EnqueueImage(new List<string> { "https://www.google.com/s2/favicons?domain=" + site + "&sz=128", "https://icons.duckduckgo.com/ip3/" + site + ".ico" },
                             Px(44), 0, img => t.Logo = img);
            }
            StoreTiles.Add(MakeTile("all", Loc("LOC_KX_StoreAll", "All stores"), Loc("LOC_KX_StoreAllSub", "Search and compare every store"), "#FF6B4A1A", "#FF101216", false));
            Notify("StoreTileColumns");
            Notify("StoreTileHeight");
        }

        private StoreTile MakeTile(string id, string name, string sub, string c1, string c2, bool web)
        {
            Brush brush;
            try
            {
                var b = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c1), (Color)ColorConverter.ConvertFromString(c2), new Point(0, 0), new Point(1, 1));
                b.Freeze();
                brush = b;
            }
            catch (Exception) { brush = Brushes.DimGray; }
            string tileId = id;
            return new StoreTile { Id = id, Name = name, Sub = sub, Brush = brush, IsWeb = web, Command = new RelayCommand(_ => OpenStoreTile(tileId)) };
        }

        private void OpenStoreTile(string id)
        {
            var def = StoreDef(id);
            bool inTheme = id == "all" || id == "steam" || id == "gog" || id == "epic" || IsShopStore(id);
            if (!inTheme && def != null) { PlaySound("select"); OpenBrowser(def[6]); return; }
            OpenStoreList(id, null);
        }

        // ---------------- navigation ----------------

        private void OpenStore()
        {
            if (ProfileGate("store", OpenStore)) return;   // a profile without the Store (or behind the PIN)
            CloseStoreOverlays();
            SetStoreDot(false);   // the wishlist alert has been seen
            dlcParent = null;
            dlcSaved = null;
            storePage = "home";
            storeId = "";
            storeQuery = new StoreQuery();
            storeRequest++;
            StoreItems.Clear();
            StoreChips.Clear();
            MeasureStoreScale();
            if (libraryKeys == null) BuildLibraryKeys();   // quick, and needed for "owned" right away
            else KippixShared.SafeWork.Queue(_ => BuildLibraryKeys());
            BuildStoreTiles();
            LoadHomeRows();
            SetStoreStatus("");
            SyncWishlistsSoon(false);   // your Steam / GOG wishlists (if switched on; at most every 15 minutes)
            if (openPanel != "store") TogglePanel("store");
            NotifyStore();
            FocusPanelSoon();
        }

        /// <summary>B in the store: picker / viewer → game page → list → the store tiles → close.</summary>
        private ICommand storeFromSettingsCmd;
        private bool storeSettingsFromMenu;
        /// <summary>
        /// Settings › Store: the Store's settings, with the Store itself showing behind them (changes show as you make them).
        /// Back puts the Store away and returns to the settings menu.
        /// </summary>
        public ICommand OpenStoreFromSettingsCommand
        {
            get
            {
                return storeFromSettingsCmd ?? (storeFromSettingsCmd = new RelayCommand(_ =>
                {
                    PlaySound("select");
                    OpenStore();            // the Store underneath
                    if (openPanel != "store") return;   // not for this profile (or the PIN is being asked for)
                    OpenStoreSettings();    // its settings over it
                    storeSettingsFromMenu = true;
                }));
            }
        }

        private bool StoreBack()
        {
            if (storeReaderOpen) { CloseReader(); return true; }
            if (storePickerOpen) { CloseStorePicker(); return true; }
            if (storeViewerOpen) { CloseStoreViewer(); return true; }
            if (storePage == "game" && storeGameFrom == "home")
            {
                StopStoreVideos();
                storeGameGen++;
                BackToHomeRow(storeGame);
                return true;
            }
            if (storePage == "game")
            {
                StopStoreVideos();
                storeGameGen++;
                storePage = "list";
                // taken off the wishlist on its page: gone from the wishlist page too (focus moves to the game in its place)
                StoreItem nextFocus = null;
                if (storeId == "wishlist" && storeGame != null && FindWish(storeGame) == null)
                {
                    int at = StoreItems.IndexOf(storeGame);
                    if (at >= 0)
                    {
                        StoreItems.RemoveAt(at);
                        if (storeTotal > 0) storeTotal--;
                        UpdateSectionTitle();
                        if (StoreItems.Count == 0) SetStoreStatus(Loc("LOC_KX_WishEmpty", "Your wishlist is empty. Add games with the heart on a game's page."));
                        else nextFocus = StoreItems[Math.Min(at, StoreItems.Count - 1)];
                    }
                }
                NotifyStore();
                var focus = nextFocus ?? storeListFocus;
                ui.BeginInvoke(new Action(() => { if (focus != null && StoreItems.Contains(focus)) UiTools.FocusItem("KX_StoreList", focus); else FocusPanelSoon(); }), DispatcherPriority.ContextIdle);
                return true;
            }
            if (storePage == "list" && IsSubList(storeId) && DlcBack()) return true;
            if (storePage == "list")
            {
                string from = storeId;
                storePage = "home";
                storeId = "";
                storeQuery = new StoreQuery();
                storeRequest++;
                storeListGen++;
                NotifyStore();
                var tile = StoreTiles.FirstOrDefault(t => t.Id == from);
                ui.BeginInvoke(new Action(() =>
                {
                    if (from == "wishlist") UiTools.FocusByName("KX_StoreWishButton");
                    else if (tile != null) UiTools.FocusItem("KX_StoreTiles", tile);
                    else FocusPanelSoon();
                }), DispatcherPriority.ContextIdle);
                return true;
            }
            return false;
        }

        private void OpenStoreList(string id, string text)
        {
            storeId = id;
            storePage = "list";
            storeQuery = new StoreQuery { Text = text ?? "" };
            if (id == "epic") storeQuery.Sort = "free";   // starts on this week's free games
            HookStoreList();
            NotifyStore();
            ReloadStoreList();
        }

        /// <summary>Runs the list's query again from the first page (after a filter or search change).</summary>
        private void ReloadStoreList() { ReloadStoreList(true); }

        /// <summary>focusFirst false: a filter chip changed, so focus stays on the chips.</summary>
        private void ReloadStoreList(bool focusFirst)
        {
            BuildChips();
            storePageNo = 0;
            UpdateSectionTitle();
            var q = storeQuery.Clone();
            string id = storeId;
            if (id == "wishlist") { var wishes = settings.StoreWishlist.ToList(); LoadStoreList(() => WishlistPage(q, wishes), focusFirst); return; }
            if (id == "dlc") { string app = dlcAppId; LoadStoreList(() => DlcPage(app, q), focusFirst); return; }
            if (id == "similar") { string app = dlcAppId; LoadStoreList(() => SimilarPage(app, q), focusFirst); return; }
            LoadStoreList(() => FetchPage(id, q, 0), focusFirst);
        }

        private StoreResults FetchPage(string id, StoreQuery q, int page)
        {
            if (id == "wishlist" || IsSubList(id)) return new StoreResults();   // one page, loaded by ReloadStoreList
            if (id == "steam") return SteamBrowse(q, page);
            if (id == "gog") return GogBrowse(q, page);
            if (id == "epic" && (q.Sort == "free" || !HasItadKey))
                return page == 0 && q.Text.Length == 0 ? new StoreResults { Items = EpicFree() } : new StoreResults();
            if (id == "epic" || IsShopStore(id))
            {
                int shop = ShopId(id);
                string one = shop.ToString(CultureInfo.InvariantCulture);
                if (q.Text.Length > 0) return page == 0 ? new StoreResults { Items = ItadSearch(q.Text, one, true) } : new StoreResults();
                return ItadDeals(ItadSort(q.Sort), page, one, !q.Sale);
            }
            if (id == "all")
            {
                // every official store's games (from IsThereAnyDeal); a genre, or Steam's own charts, use Steam + GOG
                if (!HasItadKey || AllUsesCharts(q)) return MergedPage(q, page);
                if (q.Text.Length > 0) return page == 0 ? new StoreResults { Items = ItadSearch(q.Text, ItadShops, false) } : new StoreResults();
                return ItadDeals(ItadSort(q.Sort), page, ItadShops, !q.Sale);
            }
            return new StoreResults();
        }

        /// <summary>Fetches tiles off the UI thread and shows them (ignored if you've moved on meanwhile).</summary>
        private int storeDoneRequest = -1, storeFocusRequest = -1;

        private void LoadStoreList(Func<StoreResults> fetch, bool focusFirst)
        {
            int request = ++storeRequest;
            int gen = ++storeListGen;
            StoreItems.Clear();
            storeHasMore = false;
            storeTotal = -1;
            SetStoreStatus(Loc("LOC_KX_Loading", "Loading…"));
            KippixShared.SafeWork.Queue(_ =>
            {
                StoreResults res = null;
                string error = null;
                try { res = fetch(); }
                catch (Exception ex) { error = ex.Message; logger.Warn("Kippix: store request failed: " + ex.Message); }
                OnUi(() =>
                {
                    if (request != storeRequest) return;
                    storeDoneRequest = request;
                    if (storeFocusRequest == request && !kbOpen) focusFirst = true;   // the keyboard was put away while this was loading
                    StoreItems.Clear();
                    if (res != null)
                    {
                        AddStoreItems(res.Items, gen);
                        storeHasMore = res.HasMore;
                        storeTotal = res.Total;
                    }
                    UpdateSectionTitle();
                    if (StoreItems.Count < 9 && storeHasMore) ui.BeginInvoke(new Action(LoadMoreStore), DispatcherPriority.Background);   // e.g. every game on the page is one you own
                    SetStoreStatus(error != null ? Loc("LOC_KX_StoreError", "The store didn't answer. Check the internet connection and try again.")
                                 : StoreItems.Count == 0 && storeId == "wishlist" ? Loc("LOC_KX_WishEmpty", "Your wishlist is empty. Add games with the heart on a game's page.")
                                 : StoreItems.Count == 0 ? Loc("LOC_KX_StoreNothing", "Nothing found.") : "");
                    // only move focus if you're still looking at this list (not the browser, a game page or the picker)
                    bool here = openPanel == "store" && storePage == "list" && !storePickerOpen && gen == storeListGen;
                    if (here && focusFirst && StoreItems.Count > 0)
                    {
                        var first = StoreItems[0];   // read now: the list may change before the focus call runs
                        ui.BeginInvoke(new Action(() => { if (openPanel == "store" && storePage == "list") UiTools.FocusItem("KX_StoreList", first); }), DispatcherPriority.ContextIdle);
                    }
                    else if (here && focusFirst) FocusPanelSoon();
                });
            });
        }

        private void AddStoreItems(IEnumerable<StoreItem> items, int gen)
        {
            var have = new HashSet<string>(StoreItems.Select(i => i.Store + "|" + i.Id));
            foreach (var i in items)
            {
                var item = i;
                if (!have.Add(item.Store + "|" + item.Id)) continue;
                item.IsOwned = IsOwned(item);
                MarkWished(item);
                if (settings.StoreHideOwned && item.IsOwned && storeId != "wishlist") continue;
                if (HidesFree && item.PriceText == Loc("LOC_KX_StoreFree", "Free")) continue;
                item.OpenCommand = new RelayCommand(__ => { storeGameFrom = "list"; OpenStoreGame(item); });
                StoreItems.Add(item);
                LoadItemThumb(item, gen);
            }
        }

        /// <summary>The next page, when you reach the last rows of the list.</summary>
        private void LoadMoreStore()
        {
            if (!storeHasMore || storeLoadingMore || storePage != "list") return;
            storeLoadingMore = true;
            Notify("StoreLoadingMore");
            int request = storeRequest, gen = storeListGen, page = storePageNo + 1;
            var q = storeQuery.Clone();
            string id = storeId;
            KippixShared.SafeWork.Queue(_ =>
            {
                StoreResults res = null;
                try { res = FetchPage(id, q, page); } catch (Exception ex) { logger.Warn("Kippix: store page failed: " + ex.Message); }
                OnUi(() =>
                {
                    storeLoadingMore = false;
                    Notify("StoreLoadingMore");
                    if (request != storeRequest || res == null) return;
                    storePageNo = page;
                    storeHasMore = res.HasMore;
                    int before = StoreItems.Count;
                    AddStoreItems(res.Items, gen);
                    if (StoreItems.Count - before < 3 && storeHasMore) ui.BeginInvoke(new Action(LoadMoreStore), DispatcherPriority.Background);
                    if (StoreItems.Count > 0 && before == 0) SetStoreStatus("");
                });
            });
        }

        private void HookStoreList()
        {
            if (storeListHooked) return;
            var list = UiTools.FindByName(Application.Current.MainWindow, "KX_StoreList");
            if (list == null) return;
            storeListHooked = true;
            list.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) =>
            {
                var fe = e.OriginalSource as FrameworkElement;
                var item = fe != null ? fe.DataContext as StoreItem : null;
                if (item == null) return;
                storeListFocus = item;
                int index = StoreItems.IndexOf(item);
                if (index >= 0 && index >= StoreItems.Count - 9) LoadMoreStore();   // the last three rows
            }), true);
        }

        private void UpdateSectionTitle()
        {
            var parts = new List<string>();
            if (storeId == "dlc" && dlcParent != null) parts.Add(string.Format(Loc("LOC_KX_AddOnsFor", "Add-ons for {0}"), dlcParent.Name));
            if (storeId == "similar" && dlcParent != null) parts.Add(string.Format(Loc("LOC_KX_LikeFor", "Games like {0}"), dlcParent.Name));
            if (storeQuery.Text.Length > 0) parts.Add(string.Format(Loc("LOC_KX_StoreResults", "Results for \"{0}\""), storeQuery.Text));
            else if (storeQuery.Sort == "free") parts.Add(Loc("LOC_KX_StoreEpicFree", "Free games"));
            else parts.Add(SortName(storeQuery.Sort));
            if (storeQuery.GenreName.Length > 0) parts.Add(storeQuery.GenreName);
            if (storeTotal > 0) parts.Add(string.Format(storeId == "dlc" ? Loc("LOC_KX_AddOnsCountText", "{0} add-ons") : Loc("LOC_KX_StoreCount", "{0} games"), storeTotal.ToString("N0", CultureInfo.CurrentCulture)));
            SetStoreSection(string.Join("   ·   ", parts.ToArray()));
        }

        // ---------------- filters ----------------

        /// <summary>IsThereAnyDeal's sort for ours: popularity (its ranking across stores) by default.</summary>
        private static string ItadSort(string sort)
        {
            return sort == "deals" ? "-cut" : sort == "pricelow" ? "price" : sort == "name" ? "title" : "rank";
        }

        /// <summary>All stores: a genre filter or a chart sort (top sellers, newest, best rated) needs Steam's and GOG's own catalogues.</summary>
        private static bool AllUsesCharts(StoreQuery q)
        {
            return q.Genre.Length > 0 || q.Sort == "topsellers" || q.Sort == "new" || q.Sort == "rated";
        }

        private string[][] SortOptions()
        {
            var list = new List<string[]>();
            if (storeId == "wishlist")
            {
                list.Add(new[] { "", Loc("LOC_KX_SortAdded", "Date added") });
                list.Add(new[] { "drop", Loc("LOC_KX_SortDrop", "Biggest price drop") });
                list.Add(new[] { "pricelow", Loc("LOC_KX_SortPriceLow", "Price: low to high") });
                list.Add(new[] { "name", Loc("LOC_KX_SortName", "Name (A to Z)") });
                return list.ToArray();
            }
            if (IsSubList(storeId))
            {
                list.Add(new[] { "", storeId == "similar" ? Loc("LOC_KX_SortSimilar", "Most similar") : Loc("LOC_KX_SortStoreOrder", "Store order") });
                list.Add(new[] { "pricelow", Loc("LOC_KX_SortPriceLow", "Price: low to high") });
                list.Add(new[] { "name", Loc("LOC_KX_SortName", "Name (A to Z)") });
                return list.ToArray();
            }
            if (storeId == "all" && HasItadKey)
            {
                list.Add(new[] { "", storeQuery.Text.Length > 0 ? Loc("LOC_KX_SortRelevance", "Best match") : Loc("LOC_KX_SortPopular", "Most popular") });
                list.Add(new[] { "deals", Loc("LOC_KX_SortDiscount", "Biggest discount") });
                list.Add(new[] { "pricelow", Loc("LOC_KX_SortPriceLow", "Price: low to high") });
                list.Add(new[] { "name", Loc("LOC_KX_SortName", "Name (A to Z)") });
                list.Add(new[] { "topsellers", Loc("LOC_KX_SortTopSellersSG", "Top sellers (Steam & GOG)") });
                list.Add(new[] { "new", Loc("LOC_KX_SortNewSG", "Newest (Steam & GOG)") });
                list.Add(new[] { "rated", Loc("LOC_KX_SortRatedSG", "Best rated (Steam & GOG)") });
                return list.ToArray();
            }
            if (storeId == "epic" || IsShopStore(storeId))
            {
                if (storeId == "epic") list.Add(new[] { "free", Loc("LOC_KX_StoreEpicFree", "Free games") });
                if (storeId != "epic" || HasItadKey)
                {
                    list.Add(new[] { "", storeQuery.Text.Length > 0 ? Loc("LOC_KX_SortRelevance", "Best match") : Loc("LOC_KX_SortPopular", "Most popular") });
                    list.Add(new[] { "deals", Loc("LOC_KX_SortDiscount", "Biggest discount") });
                    list.Add(new[] { "pricelow", Loc("LOC_KX_SortPriceLow", "Price: low to high") });
                    list.Add(new[] { "name", Loc("LOC_KX_SortName", "Name (A to Z)") });
                }
                return list.ToArray();
            }
            if (storeQuery.Text.Length > 0) list.Add(new[] { "", Loc("LOC_KX_SortRelevance", "Best match") });
            else list.Add(new[] { "", Loc("LOC_KX_SortTopSellers", "Top sellers") });
            list.Add(new[] { "new", Loc("LOC_KX_SortNew", "Newest") });
            list.Add(new[] { "rated", Loc("LOC_KX_SortRated", "Best rated") });
            list.Add(new[] { "pricelow", Loc("LOC_KX_SortPriceLow", "Price: low to high") });
            list.Add(new[] { "pricehigh", Loc("LOC_KX_SortPriceHigh", "Price: high to low") });
            list.Add(new[] { "name", Loc("LOC_KX_SortName", "Name (A to Z)") });
            if (storeId == "gog") list.Add(new[] { "discount", Loc("LOC_KX_SortDiscount", "Biggest discount") });

            return list.ToArray();
        }

        private string SortName(string key)
        {
            var hit = SortOptions().FirstOrDefault(o => o[0] == key);
            return hit != null ? hit[1] : Loc("LOC_KX_SortTopSellers", "Top sellers");
        }

        private decimal PriceScale
        {
            get
            {
                // roughly how many of your currency a pound / dollar / euro of game price is, so the price steps make sense
                decimal v;
                return PriceLevels.TryGetValue(CurrencyCode, out v) ? v : 1;
            }
        }

        private static readonly Dictionary<string, decimal> PriceLevels = new Dictionary<string, decimal>
        {
            { "JPY", 150 }, { "KRW", 1300 }, { "INR", 80 }, { "MXN", 18 }, { "ZAR", 18 }, { "BRL", 5 }, { "PLN", 4 }, { "NOK", 10 },
            { "SEK", 10 }, { "DKK", 7 }, { "CZK", 23 }, { "HUF", 350 }, { "TRY", 30 }, { "CNY", 7 }, { "IDR", 15000 }, { "RUB", 90 },
            { "UAH", 40 }, { "THB", 35 }, { "PHP", 55 }, { "MYR", 4.5m }, { "SGD", 1.3m }, { "HKD", 8 }, { "TWD", 30 }, { "VND", 24000 },
            { "CLP", 900 }, { "COP", 4000 }, { "PEN", 3.7m }, { "ILS", 3.7m }, { "SAR", 3.75m }, { "AED", 3.7m }, { "KZT", 450 },
            { "CRC", 520 }, { "UYU", 40 }, { "QAR", 3.6m }, { "RON", 4.6m }, { "BGN", 1.8m }, { "ISK", 140 }, { "CAD", 1.35m },
            { "AUD", 1.5m }, { "NZD", 1.65m }, { "CHF", 0.9m }
        };

        /// <summary>A price filter value as money ("35" → "Up to £35").</summary>
        private string SliderPriceText(string price)
        {
            decimal v;
            if (decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out v))
                return string.Format(Loc("LOC_KX_SliderUpTo", "Up to {0}"), Money(v, CurrencyCode));
            return price;
        }

        private string[][] PriceOptions()
        {
            var list = new List<string[]> { new[] { "", Loc("LOC_KX_PriceAny", "Any price") }, new[] { "free", Loc("LOC_KX_StoreFree", "Free") } };
            foreach (int n in new[] { 5, 10, 20, 40 })
            {
                decimal v = n * PriceScale;
                list.Add(new[] { v.ToString(CultureInfo.InvariantCulture), string.Format(Loc("LOC_KX_PriceUnder", "Under {0}"), Money(v, CurrencyCode)) });
            }
            return list.ToArray();
        }

        private static readonly string[][] SteamFeatures =
        {
            new[] { "", "LOC_KX_FeatureAny", "Any" },
            new[] { "2", "LOC_KX_FeatureSingle", "Single-player" },
            new[] { "1", "LOC_KX_FeatureMulti", "Multi-player" },
            new[] { "9", "LOC_KX_FeatureCoop", "Co-op" },
            new[] { "28", "LOC_KX_FeatureController", "Full controller support" },
        };

        private void BuildChips()
        {
            StoreChips.Clear();
            if (storeId == "wishlist" || IsSubList(storeId))
            {
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterSort", "Sort"), Value = SortName(storeQuery.Sort), IsSelected = storeQuery.Sort.Length > 0, Command = new RelayCommand(_ => PickSort()) });
                return;
            }
            if (storeId == "epic" && !HasItadKey)
            {
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_StoreEpicFree", "Free games"), IsSelected = true, Command = new RelayCommand(_ => { }) });
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_StoreSearchEpic", "Search the Epic store"), Command = new RelayCommand(_ => AskEpicSearch()) });
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_StoreBrowseEpic", "Browse the full Epic store"), Command = new RelayCommand(_ => OpenBrowser("https://store.epicgames.com/browse")) });
                return;
            }
            if (storeId == "epic" || IsShopStore(storeId))
            {
                // games from IsThereAnyDeal's list of what this store sells: search, sort, on sale, and the store's own website
                var def = StoreDef(storeId);
                StoreChips.Add(new PickOption
                {
                    Text = storeQuery.Text.Length > 0 ? storeQuery.Text : string.Format(Loc("LOC_KX_StoreSearchIn", "Search {0}"), StoreTitle),
                    IsSelected = storeQuery.Text.Length > 0, IsSearch = true, Command = StoreSearchCommand
                });
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterSort", "Sort"), Value = SortName(storeQuery.Sort), IsSelected = storeQuery.Sort.Length > 0 && storeQuery.Sort != "free", Command = new RelayCommand(_ => PickSort()) });
                if (storeQuery.Sort != "free")
                    StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterSale", "On sale"), IsSelected = storeQuery.Sale, IsToggle = true, Command = new RelayCommand(_ => { storeQuery.Sale = !storeQuery.Sale; ReloadStoreList(false); FocusChip(2); }) });
                AddHideOwnedChip();
                if (def != null) StoreChips.Add(new PickOption { Text = string.Format(Loc("LOC_KX_StoreOpenSite", "{0} website"), def[1]), Command = new RelayCommand(_ => OpenBrowser(def[6])) });
                return;
            }
            StoreChips.Add(new PickOption
            {
                Text = storeQuery.Text.Length > 0 ? storeQuery.Text : (storeId == "all" ? Loc("LOC_KX_StoreSearchAll", "Search every store") : string.Format(Loc("LOC_KX_StoreSearchIn", "Search {0}"), StoreTitle)),
                IsSelected = storeQuery.Text.Length > 0, IsSearch = true,
                Command = StoreSearchCommand
            });
            StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterSort", "Sort"), Value = SortName(storeQuery.Sort), IsSelected = storeQuery.Sort.Length > 0, Command = new RelayCommand(_ => PickSort()) });
            bool allFromItad = storeId == "all" && HasItadKey && !AllUsesCharts(storeQuery);
            {
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterGenre", "Genre & tags"), Value = storeQuery.GenreName.Length > 0 ? storeQuery.GenreName : Loc("LOC_KX_FeatureAny", "Any"), IsSelected = storeQuery.Genre.Length > 0, Command = new RelayCommand(_ => PickGenre()) });
                var price = PriceOptions().FirstOrDefault(p => p[0] == storeQuery.Price);
                if (!allFromItad)
                    StoreChips.Add(new PickOption
                    {
                        Key = "price", Text = Loc("LOC_KX_FilterPrice", "Price"),
                        Value = (price != null ? price[1] : SliderPriceText(storeQuery.Price)) + (HidesFree ? " · " + Loc("LOC_KX_NoFree", "no free games") : ""),
                        IsSelected = storeQuery.Price.Length > 0 || HidesFree, Command = new RelayCommand(_ => PickPrice())
                    });
                StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterSale", "On sale"), IsSelected = storeQuery.Sale, IsToggle = true, Command = new RelayCommand(_ => { storeQuery.Sale = !storeQuery.Sale; ReloadStoreList(false); FocusChip(allFromItad ? 3 : 4); }) });
                if (storeId == "steam")
                {
                    var f = SteamFeatures.FirstOrDefault(x => x[0] == storeQuery.Feature) ?? SteamFeatures[0];
                    StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterFeatures", "Features"), Value = Loc(f[1], f[2]), IsSelected = storeQuery.Feature.Length > 0, Command = new RelayCommand(_ => PickFeature()) });
                }
                AddHideOwnedChip();
                if (storeQuery.Genre.Length > 0 || storeQuery.Price.Length > 0 || storeQuery.Sale || storeQuery.Feature.Length > 0 || storeQuery.Sort.Length > 0)
                    StoreChips.Add(new PickOption { Text = Loc("LOC_KX_FilterClear", "Clear filters"), Command = new RelayCommand(_ => { storeQuery = new StoreQuery { Text = storeQuery.Text }; ReloadStoreList(false); FocusChip(1); }) });
            }
        }

        private void AddHideOwnedChip()
        {
            StoreChips.Add(new PickOption
            {
                Key = "owned", Text = Loc("LOC_KX_HideOwned", "Hide games I own"), IsSelected = settings.StoreHideOwned, IsToggle = true,
                Command = new RelayCommand(_ => { StoreHideOwned = !settings.StoreHideOwned; ReloadStoreList(false); FocusChipKey("owned"); })
            });
        }

        private void FocusChipKey(string key)
        {
            var chip = StoreChips.FirstOrDefault(c => c.Key == key);
            if (chip != null) ui.BeginInvoke(new Action(() => UiTools.FocusItem("KX_StoreChips", chip)), DispatcherPriority.ContextIdle);
        }

        private void FocusChip(int index)
        {
            if (index < 0 || index >= StoreChips.Count) return;
            var chip = StoreChips[index];
            ui.BeginInvoke(new Action(() => UiTools.FocusItem("KX_StoreChips", chip)), DispatcherPriority.ContextIdle);
        }

        private void PickSort()
        {
            OpenStorePicker(Loc("LOC_KX_FilterSort", "Sort"), SortOptions().Select(o => Tuple.Create(o[0], o[1])), storeQuery.Sort, key =>
            {
                storeQuery.Sort = key;
                if (storeId == "all" && !AllUsesCharts(new StoreQuery { Sort = key })) { storeQuery.Genre = ""; storeQuery.GenreName = ""; storeQuery.Price = ""; }
                ReloadStoreList(false);
                FocusChip(storeId == "wishlist" || IsSubList(storeId) ? 0 : 1);
            });
        }

        private void PickPrice()
        {
            OpenStorePicker(Loc("LOC_KX_FilterPrice", "Price"), PriceOptions().Select(o => Tuple.Create(o[0], o[1])), storeQuery.Price, key => { storeQuery.Price = key; ReloadStoreList(false); FocusChip(3); });
            StorePickerSlider = true;     // the slider under the quick choices
            SetPriceSliderFromQuery();
        }

        private void PickFeature()
        {
            OpenStorePicker(Loc("LOC_KX_FilterFeatures", "Features"), SteamFeatures.Select(o => Tuple.Create(o[0], Loc(o[1], o[2]))), storeQuery.Feature, key => { storeQuery.Feature = key; ReloadStoreList(false); FocusChip(5); });
        }

        private void PickGenre()
        {
            string id = storeId;
            string before = StoreStatus;
            string loading = Loc("LOC_KX_StoreGenresLoading", "Getting the genres and tags…");
            SetStoreStatus(loading);
            KippixShared.SafeWork.Queue(_ =>
            {
                List<KeyValuePair<string, string>> options = null;
                try { options = id == "gog" ? GogGenres() : SteamTags(); } catch (Exception ex) { logger.Warn("Kippix: genres failed: " + ex.Message); }
                OnUi(() =>
                {
                    if (StoreStatus == loading) SetStoreStatus(before);   // leave it alone if a list load changed it meanwhile
                    if (storeId != id || storePage != "list" || openPanel != "store") return;
                    if (options == null || options.Count == 0) { SetStoreStatus(Loc("LOC_KX_StoreError", "The store didn't answer. Check the internet connection and try again.")); return; }
                    var all = new List<Tuple<string, string>> { Tuple.Create("", Loc("LOC_KX_FeatureAny", "Any")) };
                    all.AddRange(options.Select(o => Tuple.Create(o.Key, o.Value)));
                    OpenStorePicker(Loc("LOC_KX_FilterGenre", "Genre & tags"), all, storeQuery.Genre, key =>
                    {
                        var hit = all.FirstOrDefault(o => o.Item1 == key);
                        storeQuery.Genre = key;
                        storeQuery.GenreName = key.Length > 0 && hit != null ? hit.Item2 : "";
                        if (storeId == "all" && key.Length > 0 && !AllUsesCharts(new StoreQuery { Sort = storeQuery.Sort })) storeQuery.Sort = "topsellers";   // genres come from Steam & GOG
                        ReloadStoreList(false);
                        FocusChip(2);
                    }, true);
                });
            });
        }

        /// <summary>Y: the search of whatever is open (store, its genre list, achievements). True if there was one.</summary>
        private bool YSearch()
        {
            if (openPanel == "store")
            {
                if (storeReaderOpen || storeViewerOpen) return false;
                PlaySound("select");
                if (storePickerOpen) AskPickerSearch(); else AskStoreSearch();
                return true;
            }
            if (openPanel == "achievements" && achPage == "games" && AchSearchCommand != null)
            {
                PlaySound("select");
                AchSearchCommand.Execute(null);
                return true;
            }
            return false;
        }

        private void AskStoreSearch()
        {
            if (storeId == "epic" && !HasItadKey) { AskEpicSearch(); return; }
            string prompt = storeId == "" || storeId == "all" ? Loc("LOC_KX_StoreSearchAll", "Search every store") : string.Format(Loc("LOC_KX_StoreSearchIn", "Search {0}"), StoreTitle);
            // the games come up above the keyboard as you type; Done goes to them. B just puts the keyboard away so you can look at them.
            storeSearchShown = storeQuery.Text;
            OpenKeyboard(prompt, storeQuery.Text,
                         text => { StoreSearchTyped(text, true); },
                         text => { StoreSearchTyped(text, false); },
                         () =>
                         {
                             storeSearchShown = null;
                             if (openPanel != "store" || storePage != "list") return;
                             if (storeDoneRequest != storeRequest) storeFocusRequest = storeRequest;   // still loading (the last letters were just sent): the results get the highlight when they come
                             else if (StoreItems.Count > 0)
                             {
                                 var first = StoreItems[0];
                                 ui.BeginInvoke(new Action(() => { if (openPanel == "store" && storePage == "list") UiTools.FocusItem("KX_StoreList", first); }), DispatcherPriority.ContextIdle);
                             }
                         }, 80);
        }

        private string storeSearchShown;

        private void StoreSearchTyped(string text, bool done)
        {
            if (openPanel != "store") return;
            text = (text ?? "").Trim();
            if (!done && text == storeSearchShown) return;
            storeSearchShown = text;
            if (storePage != "list")
            {
                if (text.Length == 0) return;
                storeId = "all";
                storePage = "list";
                storeQuery = new StoreQuery { Text = text };
                HookStoreList();
                NotifyStore();
                ReloadStoreList(done);
                return;
            }
            if (text == storeQuery.Text && !done) return;
            if (text != storeQuery.Text)
            {
                storeQuery.Text = text;
                if (text.Length > 0 && storeQuery.Sort == "free") storeQuery.Sort = "";
                ReloadStoreList(done);
            }
            else if (done) ui.BeginInvoke(new Action(() => { if (StoreItems.Count > 0) UiTools.FocusItem("KX_StoreList", StoreItems[0]); }), DispatcherPriority.ContextIdle);
        }

        private void AskEpicSearch()
        {
            OpenKeyboard(Loc("LOC_KX_StoreSearchEpic", "Search the Epic store"), "", text =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                OpenBrowser("https://store.epicgames.com/browse?q=" + Uri.EscapeDataString(text.Trim()) + "&sortBy=relevancy&sortDir=DESC&count=40");
            }, null, null, 80);
        }

        // ---------------- All stores: Steam + GOG merged ----------------

        private StoreResults MergedPage(StoreQuery q, int page)
        {
            var steamQ = q.Clone();
            var gogQ = q.Clone();
            bool useGog = true;
            if (q.Genre.Length > 0)
            {
                // the genre list is Steam's; find the same name at GOG (genre, else tag), or leave GOG out
                var g = GogGenres().FirstOrDefault(x => string.Equals(x.Value, q.GenreName, StringComparison.OrdinalIgnoreCase));
                if (g.Key != null) gogQ.Genre = g.Key; else useGog = false;
            }
            StoreResults steam = null, gog = null;
            var t1 = new Thread(() => { try { steam = SteamBrowse(steamQ, page); } catch (Exception) { } });
            var t2 = new Thread(() => { try { if (useGog) gog = GogBrowse(gogQ, page); } catch (Exception) { } });
            t1.IsBackground = t2.IsBackground = true;
            t1.Start(); t2.Start(); t1.Join(25000); t2.Join(25000);
            if (steam == null && gog == null) throw new Exception("no store answered");
            var res = new StoreResults();
            res.Items = MergeByName((steam != null ? steam.Items : new List<StoreItem>()), (gog != null ? gog.Items : new List<StoreItem>()));
            res.HasMore = (steam != null && steam.HasMore) || (gog != null && gog.HasMore);
            res.Total = Math.Max(steam != null ? steam.Total : -1, gog != null ? gog.Total : -1);
            return res;
        }

        /// <summary>The same game from several stores on one tile, showing the lowest price (in your currency).</summary>
        private List<StoreItem> MergeByName(List<StoreItem> a, List<StoreItem> b)
        {
            var byKey = new Dictionary<string, StoreItem>();
            var order = new List<string>();
            // alternate the two lists so both stores' best matches come first
            var mixed = new List<StoreItem>();
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++) { if (i < a.Count) mixed.Add(a[i]); if (i < b.Count) mixed.Add(b[i]); }
            foreach (var i in mixed)
            {
                string key = NameKey(i.Name);
                if (key.Length == 0) continue;
                StoreItem tile;
                if (!byKey.TryGetValue(key, out tile))
                {
                    tile = new StoreItem { Store = "", Id = key, Name = i.Name, Image = i.Image, ImageFallbacks = i.ImageFallbacks, Merged = new List<StoreItem>() };
                    byKey[key] = tile;
                    order.Add(key);
                }
                tile.Merged.Add(i);
                if (i.SteamAppId != null) tile.SteamAppId = i.SteamAppId;
            }
            string free = Loc("LOC_KX_StoreFree", "Free");
            foreach (var tile in byKey.Values)
            {
                var best = tile.Merged.Where(m => m.PriceText == free || ParseAmount(m.PriceText) >= 0)
                                      .OrderBy(m => m.PriceText == free ? 0 : ParseAmount(m.PriceText)).FirstOrDefault();
                if (best != null) { tile.PriceText = best.PriceText; tile.OldPriceText = best.OldPriceText; tile.DiscountText = best.DiscountText; }
                tile.Note = string.Join(" · ", tile.Merged.Select(m => m.Store).Distinct().ToArray());
            }
            return order.Select(k => byKey[k]).ToList();
        }

        // ---------------- a game's page ----------------

        private string storeWebLink;
        private string StoreGameWebLink() { return storeWebLink ?? (storeGame != null ? storeGame.Link : null); }

        private void OpenStoreGame(StoreItem item)
        {
            RememberViewed(item);
            storeDlcCount = 0;
            StoreEditions.Clear();
            storeListFocus = item;
            storeGame = item;
            storePage = "game";
            storeWebLink = item.Link ?? (item.Merged != null ? item.Merged.Select(m => m.Link).FirstOrDefault(l => !string.IsNullOrEmpty(l)) : null);
            StorePrices.Clear();
            StoreMedia.Clear();
            StopStoreVideos();
            StoreHeroImage = item.Thumb;
            StoreGameDescription = item.Description ?? "";
            StoreGameFacts = item.Note ?? "";
            StoreLowestText = "";
            StorePriceSource = "";
            StoreReviewTitle = StoreReviewLine = StoreScores = "";
            StoreReviews.Clear();
            ResetReviewRotation();
            StoreBadges.Clear();
            storeGameItadId = null;
            storeReqMin = storeReqRec = storeReqFrom = null;
            storeReviewAppId = storeReviewCursor = storeReviewSource = storeReviewSourceId = null;
            Notify("HasStoreBadges");
            Notify("HasStoreRequirements");
            SetPriceHistory(null);
            UpdateWishState();
            StorePricesLoading = true;
            StoreGameOwned = libraryKeys != null && libraryKeys.Contains(NameKey(item.Name));
            badgesDetails = new List<StoreBadge>();
            badgesDeck = new List<StoreBadge>();
            badgesSubs = new List<StoreBadge>();
            RenderBadges();
            HookStoreMediaList();
            NotifyStore();
            FocusPanelSoon();
            int request = ++storeRequest;
            int gen = ++storeGameGen;
            storeGameFocusUntouched = true;
            var cached = CachedGamePage(item);
            if (cached != null) { RestoreGamePage(cached, null, item); return; }   // opened in the last 10 minutes: straight away
            LoadStoreGame(item, request, gen);   // details, reviews and prices side by side (ThemeApi.StoreLoad.cs)
        }

        private class StoreGamePage
        {
            public string Description, Facts, Lowest, Source, WebLink;
            public List<string> Headers = new List<string>();   // the big picture, best first
            public List<string> Screens = new List<string>();
            public List<StoreMedia> Videos = new List<StoreMedia>();
            public List<StorePrice> Prices = new List<StorePrice>();
            public string ItadId, SteamAppId, ReqMin, ReqRec, ReqFrom;
            public List<PricePoint> History;
            public List<StoreBadge> Badges = new List<StoreBadge>();
            public int DlcCount;
            public List<StorePrice> Editions;
        }

        private StorePrice PriceRow(string store, StoreItem i)
        {
            string web = i.Link, app = i.AppLink;
            return new StorePrice
            {
                StoreName = store,
                PriceText = i.PriceText ?? Loc("LOC_KX_StoreNoPrice", "See the store"),
                OldPriceText = i.OldPriceText,
                DiscountText = i.DiscountText,
                Note = i.Store == "Epic" && i.HasNote ? i.Note : null,
                OpenCommand = new RelayCommand(_ => OpenStoreLink(web, app)),
                Amount = i.PriceText == Loc("LOC_KX_StoreFree", "Free") ? 0 : ParseAmount(i.PriceText),
                Currency = CurrencyCode
            };
        }

        /// <summary>Store pages open in the theme's browser, or (if you chose so) the store's app / your web browser.</summary>
        private void OpenStoreLink(string web, string app)
        {
            PlaySound("select");
            // a referral redirect remembered from before (wishlist, recently viewed): the store's own page instead
            if (IsRedirectLink(web)) web = ShopLink(web, "", storeGame != null ? storeGame.Name : "", storeGame != null ? storeGame.SteamAppId : null);
            if (settings.StoreOpenIn == 0 && !string.IsNullOrEmpty(web)) { OpenBrowser(web); return; }
            try { System.Diagnostics.Process.Start(string.IsNullOrEmpty(app) ? web : app); }
            catch (Exception)
            {
                try { if (!string.IsNullOrEmpty(web)) System.Diagnostics.Process.Start(web); } catch (Exception) { }
            }
        }

        private void BuildLibraryKeys()
        {
            try
            {
                // name → where you have it (Steam, Epic, GOG...), for "In your library (Steam)"
                var sources = new Dictionary<string, List<string>>();
                foreach (var g in api.Database.Games.Where(g => !g.Hidden))
                {
                    string k = NameKey(g.Name);
                    List<string> list;
                    if (!sources.TryGetValue(k, out list)) sources[k] = list = new List<string>();
                    string src = g.Source != null ? g.Source.Name : "";
                    if (!string.IsNullOrEmpty(src) && !list.Contains(src)) list.Add(src);
                }
                librarySources = sources;
                libraryKeys = new HashSet<string>(sources.Keys);
            }
            catch (Exception) { }
        }

        // ---------------- pictures ----------------

        private readonly Queue<Tuple<List<string>, int, int, Action<ImageSource>>> imageJobs = new Queue<Tuple<List<string>, int, int, Action<ImageSource>>>();
        private int imageWorkers;

        private void LoadItemThumb(StoreItem item, int gen)
        {
            if (item.ThumbRequested) return;
            item.ThumbRequested = true;
            var urls = new List<string>();
            Action<StoreItem> add = i =>
            {
                if (!string.IsNullOrEmpty(i.Image)) urls.Add(i.Image);
                if (i.ImageFallbacks != null) urls.AddRange(i.ImageFallbacks.Where(u => !string.IsNullOrEmpty(u)));
            };
            add(item);
            // a tile for several stores: every store's picture (one store's can be missing or broken), Steam's first
            if (item.Merged != null)
                foreach (var m in item.Merged.OrderBy(m => m.Store == "Steam" ? 0 : 1)) add(m);
            if (settings.StoreImageQuality == 0)
            {
                // low: Steam's smaller header before the big capsule
                var header = urls.FirstOrDefault(u => u.Contains("/header"));
                if (header != null) { urls.Remove(header); urls.Insert(0, header); }
            }
            urls = urls.Distinct().ToList();
            string appId = item.SteamAppId ?? (item.Merged != null ? item.Merged.Select(m => m.SteamAppId).FirstOrDefault(x => !string.IsNullOrEmpty(x)) : null);
            if (urls.Count == 0 && appId == null) return;
            // Steam's tiny search-row capsule is a last resort: first ask Steam where this game's proper pictures are
            // (newer games' sit in a hashed folder, where the guessed bigger ones can be missing)
            var tiny = appId == null ? new List<string>() : urls.Where(u => Regex.IsMatch(u, "capsule_(sm_120|231x87|184x69)")).ToList();
            urls = urls.Except(tiny).ToList();
            Func<List<string>> more = appId == null ? (Func<List<string>>)null : () => SteamArtUrls(appId).Concat(tiny).ToList();
            if (urls.Count == 0 && more == null) return;
            EnqueueImage(urls, PxImg(406), gen, img => item.Thumb = img, more);
        }

        /// <summary>A game's Steam pictures from Steam's store browse service (works for hashed asset folders too).</summary>
        private List<string> SteamArtUrls(string appId)
        {
            var list = new List<string>();
            try
            {
                var found = SteamItemsById(new List<string> { appId });
                StoreItem s;
                if (found.TryGetValue(appId, out s))
                {
                    if (!string.IsNullOrEmpty(s.Image)) list.Add(s.Image);
                    if (s.ImageFallbacks != null) list.AddRange(s.ImageFallbacks);
                }
            }
            catch (Exception) { }
            list.Add("https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/" + appId + "/header.jpg");
            list.Add("https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/header.jpg");
            return list.Distinct().ToList();
        }

        /// <summary>Downloads a picture (trying each address in turn) on up to four threads. gen 0 = always wanted.</summary>
        private void EnqueueImage(List<string> urls, int width, int gen, Action<ImageSource> done)
        {
            EnqueueImage(urls, width, gen, done, null);
        }

        private readonly Dictionary<Action<ImageSource>, Func<List<string>>> imageMore = new Dictionary<Action<ImageSource>, Func<List<string>>>();

        /// <summary>more: where else to look when none of the addresses worked (asked on the worker thread).</summary>
        private void EnqueueImage(List<string> urls, int width, int gen, Action<ImageSource> done, Func<List<string>> more)
        {
            lock (imageJobs)
            {
                if (more != null) imageMore[done] = more;
                imageJobs.Enqueue(Tuple.Create(urls, width, gen, done));
                if (imageWorkers >= 8) return;
                imageWorkers++;
            }
            KippixShared.SafeWork.Queue(_ => ImageWorker());
        }

        private void ImageWorker()
        {
            while (true)
            {
                Tuple<List<string>, int, int, Action<ImageSource>> job;
                Func<List<string>> more;
                lock (imageJobs)
                {
                    if (imageJobs.Count == 0) { imageWorkers--; return; }
                    job = imageJobs.Dequeue();
                    if (imageMore.TryGetValue(job.Item4, out more)) imageMore.Remove(job.Item4);
                }
                if (job.Item3 != 0 && job.Item3 != storeListGen) continue;   // that list is gone
                ImageSource img = null;
                var tried = new HashSet<string>();
                foreach (var u in job.Item1)
                {
                    tried.Add(u);
                    // store pictures stay on disk, not in memory (a long list would hold hundreds of them)
                    img = job.Item2 <= 160 ? Images.Load(u, job.Item2) : Images.LoadUncached(u, job.Item2);
                    if (img != null) break;
                }
                if (img == null && more != null)
                {
                    List<string> extra = null;
                    try { extra = more(); } catch (Exception) { }
                    foreach (var u in (extra ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x) && !tried.Contains(x)))
                    {
                        img = Images.LoadUncached(u, job.Item2);
                        if (img != null) break;
                    }
                }
                var done = job.Item4;
                var result = img;
                if (result != null) OnUi(() => done(result));
            }
        }

        private void BuildStoreMedia(StoreGamePage page, int gen)
        {
            StoreMedia.Clear();
            if (settings.StoreTrailers != 0)
                foreach (var v in page.Videos) AddMedia(v, gen);
            foreach (var s in page.Screens.Take(16)) AddMedia(new StoreMedia { Kind = "image", Key = s, ThumbUrl = s }, gen);
            Notify("HasStoreMedia");
        }

        private void AddMedia(StoreMedia m, int gen)
        {
            var media = m;
            media.OpenCommand = new RelayCommand(_ => OpenStoreMedia(media));
            StoreMedia.Add(media);
            if (!string.IsNullOrEmpty(media.ThumbUrl))
                EnqueueImage(new List<string> { media.ThumbUrl }, PxImg(208), 0, img => media.Thumb = img);   // also after you've moved on: the page may come back from the cache
        }

        // ---------------- Store settings (options page "store", also from the gear on the Store) ----------------

        private IInputElement storeSettingsFocus;
        private string storeSettingsSig;

        /// <summary>The Store settings that change what the Store's pages show (to refresh them only when one changed).</summary>
        private string StoreSettingsSignature()
        {
            return string.Join("|", new[] { settings.StoreTiles, settings.StoreCountry, settings.StoreResellers.ToString(), settings.StoreHideOwned.ToString(), settings.StoreHideFree.ToString(),
                settings.StoreRowWish.ToString(), settings.StoreRowRecent.ToString(), settings.StoreRowFree.ToString(), settings.StoreRowPopular.ToString(),
                settings.StoreRowSoon.ToString(), settings.StoreRowDeals.ToString(), HasItadKey.ToString() });
        }

        private void OpenStoreSettings()
        {
            storeSettingsFromStore = openPanel == "store";
            if (storeSettingsFromStore)
            {
                // the Store stays where it is underneath; the settings panel opens over it
                storeSettingsFocus = Keyboard.FocusedElement;
                storeSettingsSig = StoreSettingsSignature();
                StopTilePreview();
                if (storePickerOpen) CloseStorePicker();
            }
            if (openPanel != "options")
            {
                if (openPanel == null) { focusBeforePanel = Keyboard.FocusedElement; RememberLowerFocus(); }
                openPanel = "options";
                NotifyPanels();
                UpdateTopBarFade();
            }
            SetOptionsPage("storeopts");
        }

        private DispatcherTimer storeLiveTimer;

        /// <summary>A Store setting changed while the settings are open over the Store: refresh the Store underneath straight away (just once for quick changes).</summary>
        private void StoreLiveRefreshSoon()
        {
            ui.BeginInvoke(new Action(() =>
            {
                // the store tiles are built from the settings alone: they change at once (the rows below follow a moment later)
                if (storeSettingsFromStore && storePage == "home") { BuildStoreTiles(); NotifyStore(); }
                if (storeLiveTimer == null)
                {
                    storeLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    storeLiveTimer.Tick += (s, e) => { storeLiveTimer.Stop(); StoreLiveRefresh(); };
                }
                storeLiveTimer.Stop();
                storeLiveTimer.Start();
            }));
        }

        private void StoreLiveRefresh()
        {
            if (!storeSettingsFromStore) return;
            if (storePage != "home" && storePage != "list") return;   // a game's page reloads when you go back (it would take the highlight)
            string sig = StoreSettingsSignature();
            if (sig == storeSettingsSig) return;
            storeSettingsSig = sig;
            RefreshStoreUnderneath();
        }

        private void RefreshStoreUnderneath()
        {
            gamePageCache.Clear();   // prices in another currency / other shops
            if (storePage == "home") { BuildStoreTiles(); LoadHomeRows(); }
            else if (storePage == "list") ReloadStoreList(false);
            else if (storePage == "game" && storeGame != null) { var g = storeGame; string from = storeGameFrom; OpenStoreGame(g); storeGameFrom = from; }
            NotifyStore();
        }

        /// <summary>B on the Store settings page: when it was opened from the Store, the panel just goes away and you're back where you were.</summary>
        private bool StoreSettingsBack()
        {
            if (!storeSettingsFromStore) return false;
            if (storeSettingsFromMenu)
            {
                // opened from the settings menu: the Store goes, the menu stays (on the Store row)
                storeSettingsFromMenu = false;
                storeSettingsFromStore = false;
                if (storeLiveTimer != null) storeLiveTimer.Stop();
                CloseStoreOverlays();
                StopTilePreview();
                storeSettingsFocus = null;
                openPanel = "options";
                NotifyPanels();
                UpdateTopBarFade();
                GoBackTo("", "storeopts");
                return true;
            }
            storeSettingsFromStore = false;
            openPanel = "store";
            NotifyPanels();
            UpdateTopBarFade();
            if (storeLiveTimer != null) storeLiveTimer.Stop();
            if (StoreSettingsSignature() != storeSettingsSig) RefreshStoreUnderneath();
            NotifyStore();
            var back = storeSettingsFocus as UIElement;
            storeSettingsFocus = null;
            ui.BeginInvoke(new Action(() =>
            {
                if (back != null && back.IsVisible && back.Focusable) Keyboard.Focus(back);
                else UiTools.FocusByName("KX_StoreGear");
            }), DispatcherPriority.ContextIdle);
            return true;
        }

        private void BuildStoreToggles()
        {
            StoreToggles.Clear();
            var on = new HashSet<string>((settings.StoreTiles ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var d in StoreDefs)
            {
                var t = new StoreToggle { Id = d[0], Name = d[1] };
                t.IsOn = on.Contains(d[0]);
                t.Changed = _ =>
                {
                    settings.StoreTiles = string.Join(",", StoreToggles.Where(x => x.IsOn).Select(x => x.Id).ToArray());
                    SaveSettings();
                    Notify("StoreTilesSummary");
                };
                StoreToggles.Add(t);
            }
        }

        private void BuildRegionOptions()
        {
            StoreRegionOptions.Clear();
            StoreRegionOptions.Add(new PickOption
            {
                Text = string.Format(Loc("LOC_KX_RegionAuto", "Automatic (Windows region: {0})"), WindowsCountry),
                IsSelected = string.IsNullOrEmpty(settings.StoreCountry),
                Command = new RelayCommand(_ => SetStoreCountry(""))
            });
            foreach (var r in StoreRegions)
            {
                string code = r[0];
                StoreRegionOptions.Add(new PickOption { Text = r[1], IsSelected = settings.StoreCountry == code, Command = new RelayCommand(_ => SetStoreCountry(code)) });
            }
        }

        private void SetStoreCountry(string code)
        {
            settings.StoreCountry = code;
            SaveSettings();
            gogGenres = null;
            BuildRegionOptions();
            NotifyStoreSettings();
            GoBackTo("storeopts", "storecurrency");
        }

        public string StoreCurrencyText
        {
            get
            {
                if (string.IsNullOrEmpty(settings.StoreCountry)) return string.Format(Loc("LOC_KX_RegionAutoShort", "Automatic ({0})"), CurrencyCode);
                var r = StoreRegions.FirstOrDefault(x => x[0] == settings.StoreCountry);
                return r != null ? r[1] : settings.StoreCountry;
            }
        }
        public string StoreOpenInText { get { return settings.StoreOpenIn == 0 ? Loc("LOC_KX_OpenInTheme", "Built-in browser") : Loc("LOC_KX_OpenInApp", "The store's app / web browser"); } }
        public string StoreTrailersText
        {
            get
            {
                return settings.StoreTrailers == 0 ? Loc("LOC_KX_Off", "Off")
                     : settings.StoreTrailers == 1 ? Loc("LOC_KX_StoreTrailersRow", "With the pictures")
                     : Loc("LOC_KX_StoreTrailersAuto", "Play at the top");
            }
        }
        public bool StoreTrailersOn { get { return settings.StoreTrailers != 0; } }
        public string StoreImageQualityText
        {
            get
            {
                int q = Math.Max(0, Math.Min(2, settings.StoreImageQuality));
                return q == 0 ? Loc("LOC_KX_QualityLow", "Low (quickest)") : q == 1 ? Loc("LOC_KX_QualityMedium", "Medium") : Loc("LOC_KX_QualityHigh", "High (sharpest)");
            }
        }
        /// <summary>How big Store pictures are decoded (and which ones are fetched): low 0.65×, medium 1×, high 1.6× the size on screen.</summary>
        private double ImageQualityScale { get { int q = Math.Max(0, Math.Min(2, settings.StoreImageQuality)); return q == 0 ? 0.65 : q == 1 ? 1.0 : 1.6; } }
        private int PxImg(double units) { return Math.Min(3840, (int)Math.Ceiling(units * storeScale * ImageQualityScale)); }
        public bool StoreTrailerSound { get { return settings.StoreTrailerSound; } set { if (settings.StoreTrailerSound == value) return; settings.StoreTrailerSound = value; SaveSettings(); NotifyStoreSettings(); ApplyStoreVideoSound(); } }
        public bool StoreResellers
        {
            get { return settings.StoreResellers; }
            set { if (settings.StoreResellers == value) return; settings.StoreResellers = value; SaveSettings(); gamePageCache.Clear(); Notify("StoreResellers"); }   // (remembered pages have the old shops' prices)
        }
        public string CursorSpeedText { get { return new[] { "", Loc("LOC_KX_Speed1", "Slowest"), Loc("LOC_KX_Speed2", "Slow"), Loc("LOC_KX_Speed3", "Normal"), Loc("LOC_KX_Speed4", "Fast"), Loc("LOC_KX_Speed5", "Fastest") }[Math.Max(1, Math.Min(5, settings.BrowserCursorSpeed))]; } }
        public string StorePricesSourceText
        {
            get
            {
                return HasItadKey ? Loc("LOC_KX_ItadOn", "IsThereAnyDeal: on. Prices from every store it tracks, in your currency.")
                                  : Loc("LOC_KX_ItadOff", "IsThereAnyDeal unavailable. Prices from Steam and GOG, Epic from CheapShark (US$).");
            }
        }

        private void NotifyStoreSettings()
        {
            foreach (string n in new[] { "StoreCurrencyText", "StoreOpenInText", "StoreTrailersText", "StoreTrailersOn", "StoreTrailerSound", "CursorSpeedText", "StorePricesSourceText", "HasItadKey",
                                         "StoreTilesSummary", "StoreRowsSummary", "StoreTrailerAutoplay", "StoreImageQualityText" })
                Notify(n);
        }
    }
}
