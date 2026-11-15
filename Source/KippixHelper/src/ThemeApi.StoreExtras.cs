using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KippixHelper
{
    /// <summary>A row of games on the Store's first page (wishlist, free this week, popular, deals).</summary>
    public class StoreRow : Observable
    {
        public string Key { get; set; }
        private string title;
        public string Title { get { return title; } set { Set(ref title, value, "Title"); } }
        public ObservableCollection<StoreItem> Items { get; private set; }
        private bool loading = true;
        public bool IsLoading { get { return loading; } set { Set(ref loading, value, "IsLoading"); } }
        public StoreRow() { Items = new ObservableCollection<StoreItem>(); }
    }

    /// <summary>
    /// The Store's extras: rows on its first page, the wishlist (with price drops, checked when you open the Store),
    /// hiding games you own, badges (controller, Steam Deck, subscriptions), the price history chart, the review reader
    /// (LB / RB through reviews, filters), system requirements next to your PC, and the price slider.
    /// </summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // Rows on the first page
        // =====================================================================

        public ObservableCollection<StoreRow> StoreRows { get; private set; }
        private DateTime storeRowsAt = DateTime.MinValue;
        private string storeGameFrom = "list";   // where B goes back to from a game page

        private string storeRowsConfig = "";

        /// <summary>Which rows are switched on (a change rebuilds them).</summary>
        private string RowsConfig()
        {
            return string.Join("", new[] { settings.StoreRowFree, settings.StoreRowPopular, settings.StoreRowSoon, settings.StoreRowDeals, HasItadKey, settings.StoreResellers, GamePassAvailable }.Select(b => b ? "1" : "0").ToArray()) + Country;
        }

        private void LoadHomeRows()
        {
            if (StoreRows == null) StoreRows = new ObservableCollection<StoreRow>();
            BuildWishRow();
            BuildRecentRow();
            bool fresh = DateTime.Now - storeRowsAt < TimeSpan.FromMinutes(10) && storeRowsConfig == RowsConfig();
            if (fresh) { RefreshWishPrices(); return; }
            storeRowsAt = DateTime.Now;
            storeRowsConfig = RowsConfig();
            foreach (var key in new[] { "free", "popular", "soon", "deals", "gamepass" })
            {
                var old = StoreRows.FirstOrDefault(r => r.Key == key);
                if (old != null) StoreRows.Remove(old);
            }
            if (settings.StoreRowFree) AddHomeRow("free", Loc("LOC_KX_RowFree", "Free this week"), () => EpicFree());
            if (HasItadKey && settings.StoreRowPopular) AddHomeRow("popular", Loc("LOC_KX_RowPopular", "Popular right now"), () => ItadDeals("rank", 0, ItadShops, true).Items.Take(24).ToList());
            if (settings.StoreRowSoon) AddHomeRow("soon", Loc("LOC_KX_RowSoon", "Coming soon"), () => SoonItems());
            if (GamePassAvailable) AddHomeRow("gamepass", Loc("LOC_KX_RowGamePass", "On PC Game Pass"), () => GamePassItems());   // Game Pass Catalog Browser's catalog
            if (HasItadKey && settings.StoreRowDeals) AddHomeRow("deals", Loc("LOC_KX_RowDeals", "Biggest deals today"), () => ItadDeals("-cut", 0, ItadShops, false).Items.Take(24).ToList());
            RefreshWishPrices();
        }

        /// <summary>Upcoming games (Steam's popular coming soon), marked "Pre-order" when they can be bought already.</summary>
        private List<StoreItem> SoonItems()
        {
            var list = SteamBrowse(new StoreQuery { Sort = "comingsoon" }, 0).Items.Take(24).ToList();
            foreach (var i in list)
            {
                string when = ReleaseWhen(i.Released);   // "In 9 days · 27 Oct", "Out 12 Mar 2027", "Q1 2027"
                bool preorder = !string.IsNullOrEmpty(i.PriceText) && i.PriceText != Loc("LOC_KX_StoreFree", "Free");
                i.Note = string.Join("   ·   ", new[] { preorder ? Loc("LOC_KX_PreOrder", "Pre-order") : "", when }.Where(x => x.Length > 0).ToArray());
            }
            return list;
        }

        // ---------------- recently viewed ----------------

        private void RememberViewed(StoreItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Name)) return;
            string key = NameKey(item.Name);
            settings.StoreRecent.RemoveAll(r => NameKey(r.Name) == key);
            settings.StoreRecent.Insert(0, new WishItem
            {
                Name = item.Name, Image = item.Image, Link = item.Link, SteamAppId = item.SteamAppId, ItadId = item.ItadId,
                Store = item.Merged != null ? "" : item.Store, Id = item.Id, PriceText = item.PriceText, Added = DateTime.Now
            });
            while (settings.StoreRecent.Count > 20) settings.StoreRecent.RemoveAt(settings.StoreRecent.Count - 1);
            SaveSettings();
            Notify("HasStoreRecent");
            Notify("StoreClearRecentText");
        }

        private void BuildRecentRow()
        {
            var row = StoreRows.FirstOrDefault(r => r.Key == "recent");
            if (!settings.StoreRowRecent || settings.StoreRecent.Count == 0) { if (row != null) StoreRows.Remove(row); return; }
            if (row == null)
            {
                row = new StoreRow { Key = "recent" };
                int at = StoreRows.Any(r => r.Key == "wish") ? 1 : 0;
                StoreRows.Insert(at, row);
            }
            row.Title = Loc("LOC_KX_RowRecent", "Recently viewed");
            row.IsLoading = false;
            row.Items.Clear();
            foreach (var r in settings.StoreRecent)
            {
                var item = new StoreItem
                {
                    Store = r.Store ?? "", Id = r.Id ?? r.Name, Name = r.Name, Image = r.Image, Link = r.Link,
                    SteamAppId = r.SteamAppId, ItadId = r.ItadId, PriceText = r.PriceText
                };
                if (item.Store == "Steam" && item.Link != null && item.AppLink == null && item.SteamAppId != null) item.AppLink = "steam://store/" + item.SteamAppId;
                AddRowItem(row, item);
            }
        }

        private void AddHomeRow(string key, string title, Func<List<StoreItem>> fetch)
        {
            var row = new StoreRow { Key = key, Title = title };
            StoreRows.Add(row);
            KippixShared.SafeWork.Queue(_ =>
            {
                List<StoreItem> items = null;
                try { items = fetch(); } catch (Exception ex) { logger.Warn("Kippix: store row " + key + ": " + ex.Message); }
                OnUi(() =>
                {
                    row.IsLoading = false;
                    if (items == null || items.Count == 0) { StoreRows.Remove(row); return; }
                    foreach (var i in items) AddRowItem(row, i);
                });
            });
        }

        private void AddRowItem(StoreRow row, StoreItem i)
        {
            var item = i;
            if (settings.StoreHideOwned && IsOwned(item)) return;
            item.IsOwned = IsOwned(item);
            MarkWished(item);
            item.OpenCommand = new RelayCommand(_ => { storeGameFrom = "home"; OpenStoreGame(item); });
            row.Items.Add(item);
            LoadItemThumb(item, 0);
        }

        /// <summary>Back from a game opened from a row: the first page, with that game highlighted again.</summary>
        private void BackToHomeRow(StoreItem item)
        {
            storePage = "home";
            storeId = "";
            NotifyStore();
            ui.BeginInvoke(new Action(() =>
            {
                var rows = UiTools.FindByName(Application.Current.MainWindow, "KX_StoreRows");
                var same = item == null || StoreRows == null ? item
                         : StoreRows.SelectMany(r => r.Items).FirstOrDefault(i => ReferenceEquals(i, item))
                           ?? StoreRows.SelectMany(r => r.Items).FirstOrDefault(i => (item.ItadId != null && i.ItadId == item.ItadId) || NameKey(i.Name) == NameKey(item.Name));
                var target = rows != null && same != null ? FindFocusableFor(rows, same) : null;
                if (target != null) Keyboard.Focus(target); else FocusPanelSoon();
            }), DispatcherPriority.ContextIdle);
        }

        private static IInputElement FindFocusableFor(DependencyObject root, object data)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                var fe = c as FrameworkElement;
                if (fe != null && fe.Focusable && fe.IsVisible && ReferenceEquals(fe.DataContext, data)) return fe;
                var hit = FindFocusableFor(c, data);
                if (hit != null) return hit;
            }
            return null;
        }

        // =====================================================================
        // Owned games
        // =====================================================================

        private bool IsOwned(StoreItem item)
        {
            return item != null && libraryKeys != null && libraryKeys.Contains(NameKey(item.Name));
        }

        public bool StoreHideOwned
        {
            get { return settings.StoreHideOwned; }
            set
            {
                if (settings.StoreHideOwned == value) return;
                settings.StoreHideOwned = value;
                SaveSettings();
                Notify("StoreHideOwned");
                if (StoreRows == null) return;
                if (value) foreach (var row in StoreRows.Where(r => r.Key != "wish")) foreach (var i in row.Items.Where(IsOwned).ToList()) row.Items.Remove(i);
                else storeRowsAt = DateTime.MinValue;   // owned games come back next time the Store opens
            }
        }

        // =====================================================================
        // Wishlist
        // =====================================================================

        public bool StoreGameWished { get; private set; }
        public string StoreWishText { get { return StoreGameWished ? Loc("LOC_KX_Wished", "On your wishlist") : Loc("LOC_KX_Wish", "Add to wishlist"); } }
        public string StoreWishShort { get { return StoreGameWished ? Loc("LOC_KX_WishShortOn", "Wishlisted") : Loc("LOC_KX_WishShortOff", "Wishlist"); } }
        public bool StoreCanWish { get { return !string.IsNullOrEmpty(storeGameItadId); } }
        public ICommand StoreWishCommand { get; private set; }
        private string storeGameItadId;

        private void InitStoreExtras()
        {
            InitStoreMore();
            StoreRows = new ObservableCollection<StoreRow>();
            StoreBadges = new ObservableCollection<StoreBadge>();
            ReaderChips = new ObservableCollection<PickOption>();
            StoreWishCommand = new RelayCommand(_ => ToggleWish());
            StoreRequirementsCommand = new RelayCommand(_ => OpenRequirementsReader());
            StoreReaderNextCommand = new RelayCommand(_ => ReaderStep(1));
            StoreReaderPrevCommand = new RelayCommand(_ => ReaderStep(-1));
            ApplyPriceSliderCommand = new RelayCommand(_ => ApplyPriceSlider());
        }

        /// <summary>
        /// The open game's wishlist entry: by IsThereAnyDeal id, else by name (games brought in from Steam / GOG may not have
        /// the id yet, which showed them as not wishlisted and let the heart add them a second time).
        /// </summary>
        private WishItem PageWish()
        {
            if (storeGame == null || settings.StoreWishlist == null) return null;
            if (!string.IsNullOrEmpty(storeGameItadId))
            {
                var byId = settings.StoreWishlist.FirstOrDefault(w => w.ItadId == storeGameItadId);
                if (byId != null) return byId;
            }
            string key = NameKey(storeGame.Name);
            return key.Length == 0 ? null : settings.StoreWishlist.FirstOrDefault(w => string.IsNullOrEmpty(w.ItadId) && NameKey(w.Name) == key);
        }

        private void UpdateWishState()
        {
            var wish = PageWish();
            if (wish != null && string.IsNullOrEmpty(wish.ItadId) && !string.IsNullOrEmpty(storeGameItadId)) { wish.ItadId = storeGameItadId; SaveSettings(); }   // its prices from now on
            StoreGameWished = storeGameItadId != null && wish != null;
            Notify("StoreGameWished");
            Notify("StoreWishText");
            Notify("StoreWishShort");
            Notify("StoreCanWish");
            Notify("StoreAlertText");
        }

        private void ToggleWish()
        {
            if (storeGame == null || string.IsNullOrEmpty(storeGameItadId)) return;
            PlaySound("select");
            var existing = PageWish();
            if (existing != null) { WishRemovedHere(existing); settings.StoreWishlist.Remove(existing); }
            else
            {
                // remember today's best price, so a drop can be shown later
                var best = StorePrices.Where(p => p.Amount >= 0 && p.Currency == CurrencyCode).OrderBy(p => p.Amount).FirstOrDefault();
                settings.StoreWishlist.Add(new WishItem
                {
                    ItadId = storeGameItadId, SteamAppId = storeGame.SteamAppId, Name = storeGame.Name, Image = storeGame.Image,
                    Link = StoreGameWebLink(), AddedPrice = best != null ? best.Amount : -1, Currency = best != null ? best.Currency : CurrencyCode,
                    Added = DateTime.Now
                });
                WishedHere(storeGame.SteamAppId, storeGame.Name);
            }
            SaveSettings();
            UpdateWishState();
            Notify("StoreWishButtonText");
            BuildWishRow();
            RefreshWishPrices();
        }

        private void BuildWishRow()
        {
            if (StoreRows == null) return;
            var row = StoreRows.FirstOrDefault(r => r.Key == "wish");
            if (settings.StoreWishlist.Count == 0 || !settings.StoreRowWish) { if (row != null) StoreRows.Remove(row); return; }
            if (row == null) { row = new StoreRow { Key = "wish" }; StoreRows.Insert(0, row); }
            row.Title = Loc("LOC_KX_RowWish", "Your wishlist");
            row.IsLoading = false;
            row.Items.Clear();
            foreach (var w in settings.StoreWishlist.OrderByDescending(x => x.Added))
            {
                var item = new StoreItem
                {
                    Store = "", Id = w.ItadId, ItadId = w.ItadId, SteamAppId = w.SteamAppId, Name = w.Name, Image = w.Image, Link = w.Link,
                    Note = Loc("LOC_KX_WishChecking", "Checking the price…"), IsWished = true
                };
                var wish = w;
                item.OpenCommand = new RelayCommand(_ => { storeGameFrom = "home"; OpenStoreGame(item); });
                row.Items.Add(item);
                LoadItemThumb(item, 0);
            }
        }

        /// <summary>Today's best price for every wishlist game (only when the Store opens), marking the ones that got cheaper.</summary>
        private int wishGen;

        private void RefreshWishPrices()
        {
            var row = StoreRows != null ? StoreRows.FirstOrDefault(r => r.Key == "wish") : null;
            if (row == null || !HasItadKey) return;
            var wishes = settings.StoreWishlist.ToList();
            var ids = wishes.Select(w => w.ItadId).Where(x => !string.IsNullOrEmpty(x)).ToList();
            // brought in from Steam and never matched on IsThereAnyDeal (the import only matches its first few): matched here, all in one ask
            var unmatched = wishes.Where(w => string.IsNullOrEmpty(w.ItadId) && !string.IsNullOrEmpty(w.SteamAppId)).Select(w => w.SteamAppId).Distinct().ToList();
            int gen = ++wishGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, string> matched = null;
                if (unmatched.Count > 0)
                    try { matched = ItadIdsForSteam(unmatched); } catch (Exception ex) { logger.Warn("Kippix: wishlist ids: " + ex.Message); }
                if (matched != null) ids.AddRange(matched.Values);
                Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>> prices = null;
                try
                {
                    var allIds = ids.Distinct().ToList();
                    prices = new Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>>();
                    for (int i = 0; i < allIds.Count; i += 200)   // (ItadPrices takes 200 at a time)
                        foreach (var kv in ItadPrices(allIds.Skip(i).Take(200).ToList(), ItadShops)) prices[kv.Key] = kv.Value;
                }
                catch (Exception ex) { prices = null; logger.Warn("Kippix: wishlist prices: " + ex.Message); }
                OnUi(() =>
                {
                    if (gen != wishGen) return;   // a newer check is on its way
                    if (matched != null && matched.Count > 0)
                    {
                        int got = 0;
                        foreach (var w in wishes)
                        {
                            string id;
                            if (!string.IsNullOrEmpty(w.ItadId) || string.IsNullOrEmpty(w.SteamAppId) || !matched.TryGetValue(w.SteamAppId, out id)) continue;
                            w.ItadId = id;
                            got++;
                            foreach (var it in row.Items)
                                if (it != null && string.IsNullOrEmpty(it.ItadId) && it.SteamAppId == w.SteamAppId) { it.ItadId = id; it.Id = id; }
                        }
                        if (got > 0) { logger.Info("Kippix: wishlist: matched " + got + " of " + unmatched.Count + " imported games on IsThereAnyDeal"); SaveSettings(); }
                    }
                    if (prices == null) { foreach (var it in row.Items) if (it.Note == Loc("LOC_KX_WishChecking", "Checking the price…")) it.Note = ""; return; }
                    int cheaper = 0, alerts = 0;
                    foreach (var item in row.Items)   // updated in place: the highlighted tile stays highlighted
                    {
                        if (item == null) continue;
                        if (string.IsNullOrEmpty(item.ItadId))   // not on IsThereAnyDeal: no price to wait for
                        {
                            if (item.Note == Loc("LOC_KX_WishChecking", "Checking the price…")) item.Note = "";
                            continue;
                        }
                        var w = wishes.FirstOrDefault(x => x.ItadId == item.ItadId);
                        Tuple<List<ItadDeal>, ItadDeal> p;
                        if (w != null && prices.TryGetValue(item.ItadId, out p) && p.Item1.Count > 0)
                        {
                            var best = p.Item1.OrderBy(d => d.Price).First();
                            item.PriceText = best.Price == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(best.Price, best.Currency);
                            item.DiscountText = best.Cut > 0 ? "-" + best.Cut + "%" : null;
                            item.OldPriceText = best.Cut > 0 ? Money(best.Regular, best.Currency) : null;
                            bool dropped;
                            item.Note = WishNote(w, best, out dropped);   // price alert reached, or cheaper than when added
                            if (dropped && w.TargetPrice > 0) alerts++; else if (dropped) cheaper++;
                            item.IsDropped = dropped;
                        }
                        else item.Note = "";
                    }
                    var parts = new List<string> { Loc("LOC_KX_RowWish", "Your wishlist") };
                    if (alerts > 0) parts.Add(string.Format(Loc("LOC_KX_RowWishAlerts", "{0} under your alert price"), alerts));
                    if (cheaper > 0) parts.Add(string.Format(Loc("LOC_KX_RowWishCheaperShort", "{0} cheaper since you added them"), cheaper));
                    row.Title = string.Join(" · ", parts.ToArray());
                });
            });
        }

        // =====================================================================
        // Badges and price history on a game's page
        // =====================================================================

        public ObservableCollection<StoreBadge> StoreBadges { get; private set; }
        public bool HasStoreBadges { get { return StoreBadges != null && StoreBadges.Count > 0; } }
        public string StoreHighestText { get; private set; }
        public bool HasStoreHighest { get { return !string.IsNullOrEmpty(StoreHighestText); } }

        /// <summary>The highest price the game has been listed at on an official store (from its whole price history).</summary>
        private void SetPriceHistory(List<PricePoint> points)
        {
            StoreHighestText = "";
            if (points != null && points.Count > 0)
            {
                var top = points.OrderByDescending(p => p.Price).ThenBy(p => p.When).First();
                if (top.Price > 0)
                    StoreHighestText = string.Format(Loc("LOC_KX_StoreHighestAt", "Highest ever: {0} at {1}"), Money(top.Price, CurrencyCode), top.Shop)
                                       + ", " + top.When.ToLocalTime().ToString("MMM yyyy", CultureInfo.CurrentCulture);
            }
            Notify("StoreHighestText");
            Notify("HasStoreHighest");
        }

        // =====================================================================
        // Reader: reviews (LB / RB through them, filters) and system requirements
        // =====================================================================

        private bool storeReaderOpen;
        private string readerMode = "";             // reviews, reqs
        private List<StoreReview> readerReviews = new List<StoreReview>();
        private int readerIndex, readerGen;
        private string readerFilter = "all", readerCursor = "*", readerAppId;
        private bool readerLoading, readerEnd;
        private IInputElement readerReturn;
        internal string storeReviewAppId, storeReviewCursor;
        internal string storeReviewSource, storeReviewSourceId;   // where the page's reviews came from (steam / gog), for the reader
        private string readerSource = "steam";
        internal string storeReqMin, storeReqRec, storeReqFrom;

        public bool IsStoreReaderOpen { get { return storeReaderOpen; } }
        public bool ReaderIsReview { get { return readerMode == "reviews"; } }
        public bool ReaderPositive { get; private set; }
        public string ReaderTitle { get; private set; }
        public string ReaderSub { get; private set; }
        public string ReaderText { get; private set; }
        public string ReaderCounter { get; private set; }
        public string ReaderStatus { get; private set; }
        public bool HasReaderStatus { get { return !string.IsNullOrEmpty(ReaderStatus); } }
        public ObservableCollection<PickOption> ReaderChips { get; private set; }
        public ICommand StoreReaderNextCommand { get; private set; }
        public ICommand StoreReaderPrevCommand { get; private set; }
        public ICommand StoreRequirementsCommand { get; private set; }
        public bool HasStoreRequirements { get { return !string.IsNullOrEmpty(storeReqMin) || !string.IsNullOrEmpty(storeReqRec); } }

        private void NotifyReader()
        {
            foreach (string n in new[] { "IsStoreReaderOpen", "ReaderIsReview", "ReaderPositive", "ReaderTitle", "ReaderSub", "ReaderText", "ReaderCounter", "ReaderStatus", "HasReaderStatus", "HasReaderSections", "ReaderShowText" })
                Notify(n);
        }

        private void OpenReviewReader(StoreReview review)
        {
            readerMode = "reviews";
            ReaderSections.Clear();
            readerSource = storeReviewSource ?? "steam";
            readerAppId = readerSource == "steam" || readerSource == "gog" ? storeReviewSourceId : null;
            readerReviews = StoreReviews.ToList();
            readerIndex = Math.Max(0, readerReviews.IndexOf(review));
            readerFilter = "all";
            readerCursor = storeReviewCursor ?? "*";
            readerEnd = string.IsNullOrEmpty(readerAppId);
            readerGen++;
            BuildReaderChips();
            OpenReader();
            ShowReaderItem();
        }

        private void OpenRequirementsReader()
        {
            if (!HasStoreRequirements) return;
            PlaySound("select");
            readerMode = "reqs";
            readerGen++;          // a review page still loading must not land here
            readerEnd = true;
            ReaderChips.Clear();
            ReaderTitle = Loc("LOC_KX_SysReq", "System requirements");
            ReaderSub = storeGame != null ? storeGame.Name : "";
            if (!string.IsNullOrEmpty(storeReqFrom)) ReaderSub = string.Format(Loc("LOC_KX_ReqFrom", "{0}  ·  requirements of {1} (in it)"), ReaderSub, storeReqFrom);
            FillRequirementSections();   // Minimum, Recommended, Your PC: bold headings, specs line by line
            ReaderText = "";
            ReaderCounter = "";
            ReaderStatus = "";
            OpenReader();
        }

        private void OpenReader()
        {
            readerReturn = Keyboard.FocusedElement;
            storeReaderOpen = true;
            NotifyReader();
            Action focus = () =>
            {
                if (!storeReaderOpen) return;
                var sv = UiTools.FindByName(Application.Current.MainWindow, "KX_StoreReaderScroll") as ScrollViewer;
                if (sv != null && !sv.IsKeyboardFocused) { sv.Focus(); Keyboard.Focus(sv); }
            };
            ui.BeginInvoke(focus, DispatcherPriority.ContextIdle);
            ui.BeginInvoke(focus, DispatcherPriority.ApplicationIdle);
        }

        private void CloseReader()
        {
            if (!storeReaderOpen) return;
            storeReaderOpen = false;
            readerGen++;
            NotifyReader();
            var back = readerReturn as UIElement;
            ui.BeginInvoke(new Action(() => { if (back != null && back.IsVisible) Keyboard.Focus(back); else FocusPanelSoon(); }), DispatcherPriority.ContextIdle);
        }

        private void BuildReaderChips()
        {
            ReaderChips.Clear();
            foreach (var f in new[] { new[] { "all", "LOC_KX_ReviewsHelpful", "Most helpful" }, new[] { "positive", "LOC_KX_ReviewsPositive", "Positive" },
                                      new[] { "negative", "LOC_KX_ReviewsNegative", "Negative" }, new[] { "recent", "LOC_KX_ReviewsRecent", "Most recent" } })
            {
                string key = f[0];
                ReaderChips.Add(new PickOption { Text = Loc(f[1], f[2]), IsSelected = key == readerFilter, IsToggle = true, Command = new RelayCommand(_ => SetReaderFilter(key)) });
            }
        }

        private void SetReaderFilter(string key)
        {
            if (readerMode != "reviews" || string.IsNullOrEmpty(readerAppId)) return;
            PlaySound("select");
            readerFilter = key;
            readerReviews = new List<StoreReview>();
            readerIndex = 0;
            readerCursor = "*";
            readerEnd = false;
            readerGen++;
            int chip = new[] { "all", "positive", "negative", "recent" }.ToList().IndexOf(key);
            BuildReaderChips();
            ReaderText = "";
            ReaderTitle = "";
            ReaderSub = "";
            ReaderCounter = "";
            ReaderStatus = Loc("LOC_KX_Loading", "Loading…");
            NotifyReader();
            var focusChip = chip >= 0 && chip < ReaderChips.Count ? ReaderChips[chip] : null;
            ui.BeginInvoke(new Action(() => { if (focusChip != null) UiTools.FocusItem("KX_StoreReaderChips", focusChip); }), DispatcherPriority.ContextIdle);
            LoadMoreReviews();
        }

        private int readerLoadingGen = -1;

        private void LoadMoreReviews()
        {
            if (readerMode != "reviews" || readerEnd || string.IsNullOrEmpty(readerAppId)) return;
            if (readerLoading && readerLoadingGen == readerGen) return;   // already loading for this filter
            readerLoading = true;
            readerLoadingGen = readerGen;
            int gen = readerGen;
            string appId = readerAppId, filter = readerFilter, cursor = readerCursor, source = readerSource;
            KippixShared.SafeWork.Queue(_ =>
            {
                StoreReviews page = null;
                try { page = source == "gog" ? GogReviews(appId, filter, cursor, 20) : source == "steamc" ? SteamCommunityReviews(appId, filter, cursor, 20) : SteamReviews(appId, filter, cursor, 20); } catch (Exception ex) { logger.Warn("Kippix: reviews: " + ex.Message); }
                OnUi(() =>
                {
                    if (gen != readerGen)
                    {
                        // a newer filter took over; if its own load was blocked by this one, start it now
                        if (readerLoadingGen == gen) readerLoading = false;
                        if (storeReaderOpen && readerMode == "reviews" && readerReviews.Count == 0) LoadMoreReviews();
                        return;
                    }
                    readerLoading = false;
                    bool wasEmpty = readerReviews.Count == 0;
                    if (page == null)
                    {
                        // a network hiccup isn't the end of the list: moving on tries again
                        if (wasEmpty) { ReaderStatus = Loc("LOC_KX_StoreError", "The store didn't answer. Check the internet connection and try again."); NotifyReader(); }
                        return;
                    }
                    if (page.RawCount == 0 || string.IsNullOrEmpty(page.Cursor) || page.Cursor == cursor) readerEnd = true;
                    readerCursor = page.Cursor;
                    var known = new HashSet<string>(readerReviews.Select(r => r.FullText ?? r.Text));
                    var fresh = page.Items.Where(r => known.Add(r.FullText ?? r.Text)).ToList();
                    readerReviews.AddRange(fresh);
                    if (fresh.Count == 0 && !readerEnd) { LoadMoreReviews(); return; }   // all short or already shown: the next page
                    if (wasEmpty) ShowReaderItem(); else UpdateReaderCounter();
                });
            });
        }

        private void ReaderStep(int step)
        {
            if (!storeReaderOpen || readerMode != "reviews" || readerReviews.Count == 0) return;
            int next = readerIndex + step;
            if (next < 0 || next >= readerReviews.Count) { if (next >= readerReviews.Count) LoadMoreReviews(); return; }
            PlaySound("move");
            readerIndex = next;
            ShowReaderItem();
            if (readerIndex >= readerReviews.Count - 3) LoadMoreReviews();
        }

        private void ShowReaderItem()
        {
            if (readerReviews.Count == 0)
            {
                ReaderTitle = "";
                ReaderSub = "";
                ReaderText = "";
                ReaderStatus = readerLoading ? Loc("LOC_KX_Loading", "Loading…") : Loc("LOC_KX_ReviewsNone", "No reviews here.");
                ReaderCounter = "";
                NotifyReader();
                return;
            }
            var r = readerReviews[Math.Min(readerIndex, readerReviews.Count - 1)];
            ReaderPositive = r.Positive;
            ReaderTitle = r.Verdict;
            ReaderSub = r.Meta;
            ReaderText = r.FullText ?? r.Text;
            ReaderStatus = "";
            UpdateReaderCounter();
            NotifyReader();
            var sv = UiTools.FindByName(Application.Current.MainWindow, "KX_StoreReaderScroll") as ScrollViewer;
            if (sv != null) sv.ScrollToTop();
        }

        private void UpdateReaderCounter()
        {
            ReaderCounter = readerReviews.Count == 0 ? "" : (readerIndex + 1) + " / " + readerReviews.Count + (readerEnd ? "" : "+");
            Notify("ReaderCounter");
        }

        // ---------------- your PC (for system requirements) ----------------

        private static string pcSpecs;

        [StructLayout(LayoutKind.Sequential)]
        private class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

        /// <summary>Processor, graphics card(s), memory and Windows version, read from the registry once.</summary>
        private static string PcSpecs()
        {
            if (pcSpecs != null) return pcSpecs;
            var lines = new List<string>();
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) lines.Add("Processor: " + Convert.ToString(k.GetValue("ProcessorNameString")).Trim());
            }
            catch (Exception) { }
            try
            {
                var gpus = new List<string>();
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (var name in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
                            using (var k = cls.OpenSubKey(name))
                            {
                                string desc = k != null ? Convert.ToString(k.GetValue("DriverDesc")) : "";
                                if (desc.Length > 0 && desc.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) < 0 && !gpus.Contains(desc)) gpus.Add(desc);
                            }
                if (gpus.Count > 0) lines.Add("Graphics: " + string.Join(", ", gpus.ToArray()));
            }
            catch (Exception) { }
            try
            {
                var m = new MemoryStatus();
                if (GlobalMemoryStatusEx(m)) lines.Add("Memory: " + Math.Round(m.TotalPhys / 1073741824.0) + " GB RAM");
            }
            catch (Exception) { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null)
                    {
                        string product = Convert.ToString(k.GetValue("ProductName"));
                        int build;
                        int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out build);
                        if (build >= 22000) product = product.Replace("Windows 10", "Windows 11");   // Windows 11 still says 10 here
                        lines.Add("OS: " + product);
                    }
            }
            catch (Exception) { }
            pcSpecs = lines.Count > 0 ? string.Join("\n", lines.ToArray()) : "?";
            return pcSpecs;
        }

        // =====================================================================
        // Price slider (bottom of the Price picker)
        // =====================================================================

        private const int SliderSteps = 30;          // £0 .. £150 in £5 steps; one more = no limit
        private int priceSlider = SliderSteps + 1;
        public bool StorePickerSlider { get; private set; }
        public ICommand ApplyPriceSliderCommand { get; private set; }
        public string PriceSliderText
        {
            get
            {
                if (priceSlider > SliderSteps) return string.Format(Loc("LOC_KX_SliderAny", "Any price ({0}+)"), Money(SliderSteps * 5 * PriceScale, CurrencyCode));
                if (priceSlider == 0) return Loc("LOC_KX_StoreFree", "Free");
                return string.Format(Loc("LOC_KX_SliderUpTo", "Up to {0}"), Money(priceSlider * 5 * PriceScale, CurrencyCode));
            }
        }
        public double PriceSliderFill { get { return Math.Min(SliderSteps + 1, priceSlider) / (double)(SliderSteps + 1) * 560; } }
        public Thickness PriceSliderKnob { get { return new Thickness(Math.Max(0, PriceSliderFill - 12), 0, 0, 0); } }

        private void SetPriceSliderFromQuery()
        {
            decimal v;
            if (storeQuery.Price == "free") priceSlider = 0;
            else if (decimal.TryParse(storeQuery.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out v) && PriceScale > 0)
                priceSlider = (int)Math.Max(1, Math.Min(SliderSteps, Math.Round(v / PriceScale / 5)));
            else priceSlider = SliderSteps + 1;
            NotifySlider();
        }

        private void NudgePriceSlider(int step)
        {
            priceSlider = Math.Max(0, Math.Min(SliderSteps + 1, priceSlider + step));
            PlaySound("move");
            NotifySlider();
        }

        private void NotifySlider()
        {
            foreach (string n in new[] { "PriceSliderText", "PriceSliderFill", "PriceSliderKnob", "StorePickerSlider" }) Notify(n);
        }

        private void ApplyPriceSlider()
        {
            CloseStorePicker(false);
            PlaySound("select");
            storeQuery.Price = priceSlider > SliderSteps ? "" : priceSlider == 0 ? "free" : (priceSlider * 5 * PriceScale).ToString(CultureInfo.InvariantCulture);
            ReloadStoreList(false);
            FocusChip(3);
        }
    }
}
