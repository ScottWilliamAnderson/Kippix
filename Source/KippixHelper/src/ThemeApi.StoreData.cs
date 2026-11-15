using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace KippixHelper
{
    /// <summary>What a store list shows: search words, sort, genre or tag, price, sale and feature filters.</summary>
    internal class StoreQuery
    {
        public string Text = "";
        public string Sort = "";        // "" = the store's default (top sellers, or relevance while searching)
        public string Genre = "";       // Steam tag id, or GOG "g:slug" (genre) / "t:slug" (tag)
        public string GenreName = "";
        public string Price = "";       // "", "free", or a top price ("5", "10", ...)
        public bool Sale;
        public string Feature = "";     // Steam: category id (single-player, co-op, controller...)
        public StoreQuery Clone() { return (StoreQuery)MemberwiseClone(); }
    }

    /// <summary>One page of a store list.</summary>
    internal class StoreResults
    {
        public List<StoreItem> Items = new List<StoreItem>();
        public int Total = -1;          // -1 = not known
        public bool HasMore;
    }

    /// <summary>A game's details from its store page.</summary>
    internal class StoreDetails
    {
        public string Description;
        public string Facts;
        public string Header;           // big picture
        public string Metacritic;
        public string Controller;       // "full" / "partial" / null
        public int DlcCount;
        public string AgeRating;        // "PEGI 16 · Violence, Bad Language"
        public List<StorePrice> Editions = new List<StorePrice>();
        public string ReqMinimum, ReqRecommended;
        public List<string> Screens = new List<string>();
        public List<StoreMedia> Videos = new List<StoreMedia>();
        public StorePrice Price;
    }

    /// <summary>What IsThereAnyDeal knows about a game (for games Steam and GOG don't sell).</summary>
    internal class ItadGameInfo
    {
        public string SteamAppId;
        public string Facts;
        public string Scores;           // "Metacritic 85 · OpenCritic 84 · Steam 92%"
        public List<string> Pictures = new List<string>();
        public DateTime? ReleaseDate;
    }

    /// <summary>A game's review score and a few reviews.</summary>
    internal class StoreReviews
    {
        public string Title;            // "Very Positive"
        public string Line;             // "92% of 12,345 Steam reviews are positive"
        public List<StoreReview> Items = new List<StoreReview>();
        public string Cursor;           // the next page
        public int RawCount;            // reviews the store sent (before very short ones are left out)
        public string Source = "steam"; // steam / gog / critics: where more come from in the reader
        public string SourceId;         // Steam app id / GOG product id
    }

    /// <summary>A point on a game's price history.</summary>
    internal class PricePoint
    {
        public DateTime When;
        public decimal Price;
        public string Shop;
    }

    /// <summary>A shop name and its id-like key, as IsThereAnyDeal lists them.</summary>
    internal class ItadDeal
    {
        public string Shop;
        public decimal Price, Regular;
        public string Currency;
        public int Cut;
        public string Url;
        public string Voucher;
        public bool StoreLow;
        public DateTime? Expiry;     // when the sale ends (UTC), if the store says
    }

    /// <summary>
    /// Where the Store's data comes from. Steam: its store search (every game, with tags, sort, price and sale filters)
    /// and app details. GOG: its catalogue API. Epic: the free games list (Epic has no public catalogue).
    /// Prices across every store: IsThereAnyDeal when a key is set, otherwise Steam + GOG + CheapShark (US$).
    /// </summary>
    public partial class ThemeApi
    {
        // ---------------- region / currency ----------------

        /// <summary>Choices for "Prices in": country code (stores price by country) and what to call it.</summary>
        internal static readonly string[][] StoreRegions =
        {
            new[] { "GB", "£  British pound (UK)" },
            new[] { "US", "$  US dollar (USA)" },
            new[] { "DE", "€  Euro (Europe)" },
            new[] { "CA", "$  Canadian dollar" },
            new[] { "AU", "$  Australian dollar" },
            new[] { "NZ", "$  New Zealand dollar" },
            new[] { "JP", "¥  Japanese yen" },
            new[] { "BR", "R$  Brazilian real" },
            new[] { "PL", "zł  Polish złoty" },
            new[] { "NO", "kr  Norwegian krone" },
            new[] { "CH", "CHF  Swiss franc" },
            new[] { "IN", "₹  Indian rupee" },
            new[] { "MX", "$  Mexican peso" },
            new[] { "ZA", "R  South African rand" },
            new[] { "KR", "₩  South Korean won" },
        };

        private static string WindowsCountry
        {
            get { try { return new RegionInfo(CultureInfo.CurrentCulture.Name).TwoLetterISORegionName; } catch (Exception) { return "GB"; } }
        }

        /// <summary>The country the store prices are for (the "Prices in" setting, or Windows' region).</summary>
        private string Country
        {
            get { return string.IsNullOrEmpty(settings.StoreCountry) ? WindowsCountry : settings.StoreCountry; }
        }

        private string CurrencyCode
        {
            get { try { return new RegionInfo(Country).ISOCurrencySymbol; } catch (Exception) { return "GBP"; } }
        }

        private string Money(decimal amount, string currency)
        {
            if (amount <= 0) return null;
            try
            {
                CultureInfo culture = null;
                // prefer the chosen country's own formatting, then Windows', then any culture using that currency
                foreach (var c in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
                {
                    try
                    {
                        var r = new RegionInfo(c.Name);
                        if (r.ISOCurrencySymbol != currency) continue;
                        if (r.TwoLetterISORegionName == Country) { culture = c; break; }
                        if (culture == null) culture = c;
                    }
                    catch (Exception) { }
                }
                if (currency == WindowsCurrency) culture = CultureInfo.CurrentCulture;
                if (culture != null)
                {
                    var nf = (NumberFormatInfo)culture.NumberFormat.Clone();
                    if (amount == Math.Floor(amount) && (currency == "JPY" || currency == "KRW")) nf.CurrencyDecimalDigits = 0;
                    return amount.ToString("C", nf);
                }
            }
            catch (Exception) { }
            return amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;
        }

        private static string WindowsCurrency
        {
            get { try { return new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol; } catch (Exception) { return "GBP"; } }
        }

        // ---------------- Steam ----------------

        private static List<KeyValuePair<string, string>> steamTags;   // (tag id, name), most popular first

        /// <summary>Steam's popular tags (genres, themes, features), fetched once.</summary>
        private List<KeyValuePair<string, string>> SteamTags()
        {
            if (steamTags != null) return steamTags;
            var list = new List<KeyValuePair<string, string>>();
            var arr = StoreJson("https://store.steampowered.com/tagdata/populartags/english") as IEnumerable;
            if (arr != null)
                foreach (var d in arr.Cast<object>().OfType<Dictionary<string, object>>())
                    list.Add(new KeyValuePair<string, string>(FStr(d, "tagid"), FStr(d, "name")));
            if (list.Count > 0) steamTags = list;
            return list;
        }

        private StoreItem SteamTile(string id, string name, string image, int final, int initial, int discount, string currency)
        {
            var item = new StoreItem
            {
                Store = "Steam", Id = id, Name = name, Image = image, SteamAppId = id,
                Link = "https://store.steampowered.com/app/" + id + "/",
                AppLink = "steam://store/" + id
            };
            if (final == 0 && initial == 0) item.PriceText = null;
            else if (final == 0) item.PriceText = Loc("LOC_KX_StoreFree", "Free");
            else item.PriceText = Money(final / 100m, currency);
            if (discount > 0 && initial > final)
            {
                item.DiscountText = "-" + discount + "%";
                item.OldPriceText = Money(initial / 100m, currency);
            }
            return item;
        }

        /// <summary>A page (50 games) of Steam's store search, with every filter the store page has.</summary>
        private StoreResults SteamBrowse(StoreQuery q, int page)
        {
            var url = new StringBuilder("https://store.steampowered.com/search/results/?infinite=1&count=50&l=english&category1=998&ndl=1");
            url.Append("&cc=").Append(Country).Append("&start=").Append(page * 50);
            if (q.Text.Length > 0) url.Append("&term=").Append(Uri.EscapeDataString(q.Text));
            string sort = q.Sort.Length > 0 ? q.Sort : q.Text.Length > 0 ? "relevance" : "topsellers";
            switch (sort)
            {
                case "topsellers": url.Append("&filter=topsellers"); break;
                case "comingsoon": url.Append("&filter=popularcomingsoon"); break;
                case "new": url.Append("&sort_by=Released_DESC"); break;
                case "rated": url.Append("&sort_by=Reviews_DESC"); break;
                case "pricelow": url.Append("&sort_by=Price_ASC"); break;
                case "pricehigh": url.Append("&sort_by=Price_DESC"); break;
                case "name": url.Append("&sort_by=Name_ASC"); break;
            }
            if (q.Genre.Length > 0) url.Append("&tags=").Append(Uri.EscapeDataString(q.Genre));
            if (q.Sale) url.Append("&specials=1");
            if (q.Price == "free") url.Append("&maxprice=free");
            else if (q.Price.Length > 0) url.Append("&maxprice=").Append(q.Price);
            if (q.Feature.Length > 0) url.Append("&category2=").Append(q.Feature);

            var root = StoreJson(url.ToString()) as Dictionary<string, object>;
            var res = new StoreResults();
            if (root == null) return res;
            res.Total = (int)FNum(root, "total_count");
            string html = FStr(root, "results_html");
            var seen = new HashSet<string>();
            foreach (Match m in Regex.Matches(html, "<a [^>]*?data-ds-appid=\"(\\d+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline))
            {
                string id = m.Groups[1].Value;
                if (!seen.Add(id)) continue;
                string block = m.Groups[2].Value;
                string name = HtmlText(Grab(block, "<span class=\"title\">(.*?)</span>"));
                if (name.Length == 0) continue;
                string img = WebUtility.HtmlDecode(Grab(block, "<img [^>]*?src=\"([^\"]+)\""));
                var item = new StoreItem
                {
                    Store = "Steam", Id = id, Name = name, SteamAppId = id,
                    Link = "https://store.steampowered.com/app/" + id + "/",
                    AppLink = "steam://store/" + id
                };
                // the search row has a small capsule; the header (same folder, so it works for new games too) looks better
                if (img.Length > 0)
                {
                    // the bigger capsule first (sharp on big screens), then the header, then the small one from the search page
                    string big = Regex.Replace(img, "capsule_(sm_120|231x87|184x69)", "capsule_616x353");
                    string header = Regex.Replace(img, "capsule_(sm_120|231x87|184x69)", "header");
                    item.Image = big;
                    item.ImageFallbacks = new List<string> { header, img }.Where(u => u != big).Distinct().ToList();
                }
                string pct = Grab(block, "class=\"discount_pct\">\\s*(-?\\d+%)");
                string original = HtmlText(Grab(block, "class=\"discount_original_price\">([^<]+)<"));
                string final = HtmlText(Grab(block, "class=\"discount_final_price[^\"]*\">([^<]+)<"));
                if (final.Length > 0) item.PriceText = final;
                if (pct.Length > 0 && original.Length > 0) { item.DiscountText = pct.StartsWith("-") ? pct : "-" + pct; item.OldPriceText = original; }
                string released = HtmlText(Grab(block, "class=\"search_released[^\"]*\">\\s*([^<]*?)\\s*</div>"));
                string review = WebUtility.HtmlDecode(Grab(block, "search_review_summary[^\"]*\" data-tooltip-html=\"([^\"]*)\""));
                int br = review.IndexOf("<br", StringComparison.OrdinalIgnoreCase);
                if (br > 0) review = review.Substring(0, br);
                item.Note = string.Join("   ·   ", new[] { released, review.Trim() }.Where(s => s.Length > 0).ToArray());
                item.Released = released;
                res.Items.Add(item);
            }
            res.HasMore = (page + 1) * 50 < res.Total && res.Items.Count > 0;
            return res;
        }

        /// <summary>
        /// Requirements for something Steam sells as a package or bundle (an edition with its DLC, a trilogy), which has none
        /// of its own: Steam's search finds it by name and lists the games in it; the most demanding one's requirements are
        /// used (most memory, then most storage). Failing that, the main game (the name without "Royal Edition", "Trilogy"...).
        /// (minimum, recommended, the game they're from) or null. Worker thread.
        /// </summary>
        private Tuple<string, string, string> SteamReqFallback(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = NameKey(name);
            var apps = new List<string>();
            Func<string, string> searchHtml = term =>
            {
                var root = StoreJson("https://store.steampowered.com/search/results/?infinite=1&count=25&l=english&ndl=1&cc=" + Country + "&term=" + Uri.EscapeDataString(term)) as Dictionary<string, object>;
                return root != null ? FStr(root, "results_html") : "";
            };
            string html = searchHtml(name);
            foreach (Match m in Regex.Matches(html, "<a [^>]*?data-ds-appid=\"([\\d,]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline))
            {
                string title = HtmlText(Grab(m.Groups[2].Value, "<span class=\"title\">(.*?)</span>"));
                if (NameKey(title) != key) continue;
                apps.AddRange(m.Groups[1].Value.Split(',').Where(x => x.Length > 0));
                break;
            }
            if (apps.Count == 0)
            {
                // the main game: drop the edition words and take the closest game Steam has
                string bare = Regex.Replace(name, @"[:\-–]?\s*\b(royal|definitive|complete|ultimate|deluxe|gold|premium|collector'?s|anniversary|enhanced|special|standard|digital|game of the year|goty)\b.*$|\b(trilogy|collection|bundle|edition|anthology|pack)\b.*$", "", RegexOptions.IgnoreCase).Trim().TrimEnd(':', '-', '–').Trim();
                if (bare.Length < 3) return null;
                string bareKey = NameKey(bare);
                html = searchHtml(bare);
                foreach (Match m in Regex.Matches(html, "<a [^>]*?data-ds-appid=\"(\\d+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline))
                {
                    string title = HtmlText(Grab(m.Groups[2].Value, "<span class=\"title\">(.*?)</span>"));
                    if (!NameKey(title).StartsWith(bareKey)) continue;
                    apps.Add(m.Groups[1].Value);
                    if (apps.Count >= 3) break;
                }
            }
            Tuple<string, string, string> best = null;
            double bestWeight = -1;
            foreach (string id in apps.Distinct().Take(5))
            {
                var root = StoreJson("https://store.steampowered.com/api/appdetails?l=english&filters=basic&cc=" + Country + "&appids=" + id) as Dictionary<string, object>;
                var d = Dig(root, id, "data") as Dictionary<string, object>;
                if (d == null || FStr(d, "type") != "game") continue;
                var req = Dig(d, "pc_requirements") as Dictionary<string, object>;
                if (req == null) continue;
                string min = HtmlText(FStr(req, "minimum")), rec = HtmlText(FStr(req, "recommended"));
                if (min.Length == 0 && rec.Length == 0) continue;
                string both = rec.Length > 0 ? rec : min;
                double weight = ReqAmount(both, RamPattern) * 1000 + ReqAmount(both, DiskPattern);
                if (weight > bestWeight) { bestWeight = weight; best = Tuple.Create(min, rec, FStr(d, "name")); }
            }
            if (best != null) logger.Info("Kippix: requirements for " + name + " from " + best.Item3 + " (it has none of its own)");
            return best;
        }

        // app details already fetched (or being fetched) for a game: a game highlighted in the Store is read ahead, so its page
        // opens with its buttons (requirements, add-ons, editions) already there; opening it shares the same download
        private bool prefetchBusy;
        private readonly Dictionary<string, Tuple<DateTime, Lazy<StoreDetails>>> steamDetailsCache = new Dictionary<string, Tuple<DateTime, Lazy<StoreDetails>>>();

        private StoreDetails SteamDetails(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            Lazy<StoreDetails> lazy;
            string ckey = appId + "|" + Country;   // prices / editions are per country
            lock (steamDetailsCache)
            {
                Tuple<DateTime, Lazy<StoreDetails>> hit;
                if (steamDetailsCache.TryGetValue(ckey, out hit) && (DateTime.Now - hit.Item1).TotalMinutes < 10) lazy = hit.Item2;
                else
                {
                    if (steamDetailsCache.Count > 60)
                        foreach (var k in steamDetailsCache.Where(x => (DateTime.Now - x.Value.Item1).TotalMinutes >= 10 || x.Value.Item2.IsValueCreated).Select(x => x.Key).ToList()) steamDetailsCache.Remove(k);
                    string id = appId;
                    lazy = new Lazy<StoreDetails>(() => { try { return SteamDetailsFetch(id); } catch (Exception) { return null; } }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
                    steamDetailsCache[ckey] = Tuple.Create(DateTime.Now, lazy);
                }
            }
            var r = lazy.Value;
            if (r == null)
                lock (steamDetailsCache)
                {
                    Tuple<DateTime, Lazy<StoreDetails>> now;
                    if (steamDetailsCache.TryGetValue(ckey, out now) && ReferenceEquals(now.Item2, lazy)) steamDetailsCache.Remove(ckey);   // a failed download is tried again next time
                }
            return r;
        }

        /// <summary>Reads a highlighted Store game's details ahead (in the background), so its page opens complete.</summary>
        private void PrefetchSteamDetails(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return;
            lock (steamDetailsCache)
            {
                Tuple<DateTime, Lazy<StoreDetails>> hit;
                if (steamDetailsCache.TryGetValue(appId + "|" + Country, out hit) && (DateTime.Now - hit.Item1).TotalMinutes < 10) return;
                if (prefetchBusy) return;   // one at a time (never a burst of downloads while scrolling)
                prefetchBusy = true;
            }
            KippixShared.SafeWork.Queue(_ =>
            {
                try { SteamDetails(appId); } catch (Exception) { }
                lock (steamDetailsCache) prefetchBusy = false;
            });
        }

        /// <summary>Description, facts, pictures, trailers and price from Steam's app details.</summary>
        private StoreDetails SteamDetailsFetch(string appId)
        {
            var root = StoreJson("https://store.steampowered.com/api/appdetails?l=english&cc=" + Country + "&appids=" + Uri.EscapeDataString(appId)) as Dictionary<string, object>;
            var d = Dig(root, appId, "data") as Dictionary<string, object>;
            if (d == null) return null;
            var r = new StoreDetails();
            // the whole "About this game", not just the one-line summary
            string about = HtmlText(FStr(d, "about_the_game"));
            if (about.Length < 40) about = HtmlText(FStr(d, "detailed_description"));
            r.Description = about.Length >= 40 ? about : HtmlText(FStr(d, "short_description"));
            r.Header = FStr(d, "header_image");
            r.Controller = FStr(d, "controller_support");
            var dlc = Dig(d, "dlc") as IEnumerable;
            if (dlc != null) r.DlcCount = dlc.Cast<object>().Count();
            r.AgeRating = SteamAgeRating(Dig(d, "ratings") as Dictionary<string, object>);
            // editions (Standard, Deluxe...) and bundles sold on the game's page
            string currency = FStr(Dig(d, "price_overview") as Dictionary<string, object> ?? new Dictionary<string, object>(), "currency");
            if (currency.Length == 0) currency = CurrencyCode;
            var groups = Dig(d, "package_groups") as IEnumerable;
            if (groups != null)
                foreach (var g in groups.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var subs = Dig(g, "subs") as IEnumerable;
                    if (subs == null) continue;
                    foreach (var sub in subs.Cast<object>().OfType<Dictionary<string, object>>())
                    {
                        string option = HtmlText(FStr(sub, "option_text"));
                        int dash = option.LastIndexOf(" - ", StringComparison.Ordinal);
                        string name = dash > 0 ? option.Substring(0, dash).Trim() : option;
                        if (name.Length == 0) continue;
                        long cents = FNum(sub, "price_in_cents_with_discount");
                        bool freeLicence = sub.ContainsKey("is_free_license") && sub["is_free_license"] is bool && (bool)sub["is_free_license"];
                        string pkg = FStr(sub, "packageid");
                        string savings = FStr(sub, "percent_savings_text").Trim();
                        string link = "https://store.steampowered.com/sub/" + pkg + "/";
                        r.Editions.Add(new StorePrice
                        {
                            StoreName = name,
                            PriceText = freeLicence || cents == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(cents / 100m, currency),
                            DiscountText = savings.Length > 0 && savings != "0%" ? savings : null,
                            Amount = cents / 100m, Currency = currency,
                            OpenCommand = new RelayCommand(_ => OpenStoreLink(link, "steam://store/" + appId))
                        });
                        if (r.Editions.Count >= 6) break;
                    }
                    if (r.Editions.Count >= 6) break;
                }
            var req = Dig(d, "pc_requirements") as Dictionary<string, object>;
            if (req != null) { r.ReqMinimum = HtmlText(FStr(req, "minimum")); r.ReqRecommended = HtmlText(FStr(req, "recommended")); }
            var mc = Dig(d, "metacritic") as Dictionary<string, object>;
            if (mc != null && FNum(mc, "score") > 0) r.Metacritic = FNum(mc, "score").ToString(CultureInfo.InvariantCulture);
            var facts = new List<string>();
            var rd = Dig(d, "release_date") as Dictionary<string, object>;
            if (rd != null) facts.Add(FStr(rd, "date"));
            var devs = Dig(d, "developers") as IEnumerable;
            if (devs != null) facts.Add(string.Join(", ", devs.Cast<object>().Select(x => Convert.ToString(x)).Take(2).ToArray()));
            var genres = Dig(d, "genres") as IEnumerable;
            if (genres != null) facts.Add(string.Join(", ", genres.Cast<object>().OfType<Dictionary<string, object>>().Select(g => FStr(g, "description")).Take(3).ToArray()));
            r.Facts = string.Join("   ·   ", facts.Where(f => !string.IsNullOrEmpty(f)).ToArray());

            var shots = Dig(d, "screenshots") as IEnumerable;
            if (shots != null) foreach (var s in shots.Cast<object>().OfType<Dictionary<string, object>>()) r.Screens.Add(FStr(s, "path_full"));
            var movies = Dig(d, "movies") as IEnumerable;
            if (movies != null)
                foreach (var mv in movies.Cast<object>().OfType<Dictionary<string, object>>().Take(1))   // the main trailer
                    r.Videos.Add(new StoreMedia { IsVideo = true, Kind = "steam", Key = appId, Title = FStr(mv, "name"), ThumbUrl = FStr(mv, "thumbnail") });

            bool free = d.ContainsKey("is_free") && d["is_free"] is bool && (bool)d["is_free"];
            var p = Dig(d, "price_overview") as Dictionary<string, object>;
            string web = "https://store.steampowered.com/app/" + appId + "/", app = "steam://store/" + appId;
            if (p != null)
            {
                int disc = (int)FNum(p, "discount_percent");
                r.Price = new StorePrice
                {
                    StoreName = "Steam",
                    PriceText = FStr(p, "final_formatted"),
                    OldPriceText = disc > 0 ? FStr(p, "initial_formatted") : null,
                    DiscountText = disc > 0 ? "-" + disc + "%" : null,
                    Amount = FNum(p, "final") / 100m,
                    Currency = FStr(p, "currency")
                };
            }
            else r.Price = new StorePrice { StoreName = "Steam", PriceText = free ? Loc("LOC_KX_StoreFree", "Free") : Loc("LOC_KX_StoreNoPrice", "See the store"), Amount = free ? 0 : -1, Currency = CurrencyCode };
            r.Price.OpenCommand = new RelayCommand(_ => OpenStoreLink(web, app));
            return r;
        }

        // ---------------- GOG ----------------

        private static List<KeyValuePair<string, string>> gogGenres;   // ("g:slug" or "t:slug", name)

        private string GogOrder(StoreQuery q)
        {
            switch (q.Sort)
            {
                case "topsellers": return "desc:bestselling";
                case "new": return "desc:releaseDate";
                case "rated": return "desc:reviewsRating";
                case "pricelow": return "asc:price";
                case "pricehigh": return "desc:price";
                case "name": return "asc:title";
                case "discount": return "desc:discount";
            }
            return q.Text.Length > 0 ? "desc:score" : "desc:trending";
        }

        /// <summary>A page (48 games) of GOG's catalogue with the same filters.</summary>
        private StoreResults GogBrowse(StoreQuery q, int page)
        {
            var url = new StringBuilder("https://catalog.gog.com/v1/catalog?limit=48&productType=in:game,pack&locale=en-GB");
            url.Append("&countryCode=").Append(Country).Append("&currencyCode=").Append(CurrencyCode);
            url.Append("&page=").Append(page + 1).Append("&order=").Append(GogOrder(q));
            if (q.Text.Length > 0) url.Append("&query=like:").Append(Uri.EscapeDataString(q.Text));
            if (q.Genre.StartsWith("g:")) url.Append("&genres=in:").Append(Uri.EscapeDataString(q.Genre.Substring(2)));
            else if (q.Genre.StartsWith("t:")) url.Append("&tags=is:").Append(Uri.EscapeDataString(q.Genre.Substring(2)));
            if (q.Sale) url.Append("&discounted=eq:true");
            if (q.Price == "free") url.Append("&price=between:0,0");
            else if (q.Price.Length > 0) url.Append("&price=between:0,").Append(q.Price);

            Dictionary<string, object> root;
            try { root = StoreJson(url.ToString()) as Dictionary<string, object>; }
            catch (WebException)
            {
                // GOG doesn't sell in every currency: ask again in its default for the country
                root = StoreJson(url.ToString().Replace("&currencyCode=" + CurrencyCode, "")) as Dictionary<string, object>;
            }
            var res = new StoreResults();
            if (root == null) return res;
            res.Total = (int)FNum(root, "productCount");
            int pages = (int)FNum(root, "pages");
            res.HasMore = page + 1 < pages;
            if (gogGenres == null) ReadGogFilters(root);
            var products = Dig(root, "products") as IEnumerable;
            if (products == null) return res;
            foreach (var d in products.Cast<object>().OfType<Dictionary<string, object>>())
            {
                var item = new StoreItem
                {
                    Store = "GOG", Id = FStr(d, "id"), Name = FStr(d, "title"),
                    Image = FStr(d, "coverHorizontal"), Link = FStr(d, "storeLink")
                };
                string vertical = FStr(d, "coverVertical");
                if (vertical.Length > 0) item.ImageFallbacks = new List<string> { vertical };
                var pd = Dig(d, "price") as Dictionary<string, object>;
                if (pd != null)
                {
                    item.PriceText = FStr(pd, "final");
                    string disc = FStr(pd, "discount");
                    if (!string.IsNullOrEmpty(disc)) { item.DiscountText = disc; item.OldPriceText = FStr(pd, "base"); }
                    var fm = Dig(pd, "finalMoney") as Dictionary<string, object>;
                    if (fm != null && FDec(fm, "amount") == 0) item.PriceText = Loc("LOC_KX_StoreFree", "Free");
                }
                var facts = new List<string>();
                string rel = FStr(d, "releaseDate");
                if (rel.Length >= 4) facts.Add(rel.Substring(0, 4));
                var dev = Dig(d, "developers") as IEnumerable;
                if (dev != null) facts.Add(string.Join(", ", dev.Cast<object>().Select(x => Convert.ToString(x)).Take(2).ToArray()));
                var gen = Dig(d, "genres") as IEnumerable;
                if (gen != null) facts.Add(string.Join(", ", gen.Cast<object>().OfType<Dictionary<string, object>>().Select(g => FStr(g, "name")).Take(3).ToArray()));
                item.Note = string.Join("   ·   ", facts.Where(f => !string.IsNullOrEmpty(f)).ToArray());
                res.Items.Add(item);
            }
            return res;
        }

        private static void ReadGogFilters(Dictionary<string, object> root)
        {
            var list = new List<KeyValuePair<string, string>>();
            var g = Dig(root, "filters", "genres") as IEnumerable;
            if (g != null) foreach (var x in g.Cast<object>().OfType<Dictionary<string, object>>()) list.Add(new KeyValuePair<string, string>("g:" + FStr(x, "slug"), FStr(x, "name")));
            var names = new HashSet<string>(list.Select(k => k.Value.ToLowerInvariant()));
            var t = Dig(root, "filters", "tags") as IEnumerable;
            if (t != null)
                foreach (var x in t.Cast<object>().OfType<Dictionary<string, object>>())
                    if (names.Add(FStr(x, "name").ToLowerInvariant())) list.Add(new KeyValuePair<string, string>("t:" + FStr(x, "slug"), FStr(x, "name")));
            if (list.Count > 0) gogGenres = list;
        }

        private List<KeyValuePair<string, string>> GogGenres()
        {
            if (gogGenres == null) try { GogBrowse(new StoreQuery(), 0); } catch (Exception) { }
            return gogGenres ?? new List<KeyValuePair<string, string>>();
        }

        /// <summary>Description, screenshots and trailers (YouTube) from GOG's product API.</summary>
        private StoreDetails GogDetails(string id)
        {
            var d = StoreJson("https://api.gog.com/products/" + Uri.EscapeDataString(id) + "?expand=description,screenshots,videos&locale=en-US") as Dictionary<string, object>;
            if (d == null) return null;
            var r = new StoreDetails();
            string lead = FStr(Dig(d, "description") as Dictionary<string, object> ?? new Dictionary<string, object>(), "lead");
            string full = FStr(Dig(d, "description") as Dictionary<string, object> ?? new Dictionary<string, object>(), "full");
            r.Description = HtmlText(full.Length > 0 ? full : lead);
            var shots = Dig(d, "screenshots") as IEnumerable;
            if (shots != null)
                foreach (var s in shots.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var formats = (Dig(s, "formatted_images") as IEnumerable ?? new object[0]).Cast<object>().OfType<Dictionary<string, object>>().ToList();
                    var pick = formats.FirstOrDefault(f => FStr(f, "formatter_name").Contains("1600"))
                            ?? formats.FirstOrDefault(f => FStr(f, "formatter_name") == "ggvgl_2x")
                            ?? formats.LastOrDefault();
                    if (pick != null) r.Screens.Add(FStr(pick, "image_url"));
                }
            var videos = Dig(d, "videos") as IEnumerable;
            if (videos != null)
                foreach (var v in videos.Cast<object>().OfType<Dictionary<string, object>>().Take(3))
                {
                    string url = FStr(v, "video_url");
                    if (url.Length == 0) continue;
                    if (url.StartsWith("//")) url = "https:" + url;
                    string thumb = FStr(v, "thumbnail_url");
                    if (thumb.StartsWith("//")) thumb = "https:" + thumb;
                    r.Videos.Add(new StoreMedia { IsVideo = true, Kind = "web", Key = url, ThumbUrl = thumb, Title = Loc("LOC_KX_StoreTrailer", "Trailer") });
                }
            return r;
        }

        // ---------------- Epic (free games) ----------------

        private List<StoreItem> EpicFree()
        {
            var root = StoreJson("https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=en-GB&country=" + Country + "&allowCountries=" + Country) as Dictionary<string, object>;
            var list = new List<StoreItem>();
            var elements = Dig(root, "data", "Catalog", "searchStore", "elements") as IEnumerable;
            if (elements == null) return list;
            foreach (var d in elements.Cast<object>().OfType<Dictionary<string, object>>())
            {
                DateTime? nowUntil = PromoDate(d, "promotionalOffers", "endDate");
                DateTime? soonFrom = PromoDate(d, "upcomingPromotionalOffers", "startDate");
                var price = Dig(d, "price", "totalPrice") as Dictionary<string, object>;
                bool freeNow = nowUntil.HasValue && price != null && FNum(price, "discountPrice") == 0;
                if (!freeNow && !soonFrom.HasValue) continue;
                string slug = EpicSlug(d);
                var images = EpicImages(d);
                var item = new StoreItem
                {
                    Store = "Epic", Id = slug, Name = FStr(d, "title"),
                    Image = images.FirstOrDefault(), ImageFallbacks = images.Skip(1).ToList(),
                    Link = "https://store.epicgames.com/p/" + slug,
                    AppLink = "com.epicgames.launcher://store/p/" + slug,
                    Description = FStr(d, "description")
                };
                var fmt = Dig(price, "fmtPrice") as Dictionary<string, object>;
                if (freeNow)
                {
                    item.PriceText = Loc("LOC_KX_StoreFree", "Free");
                    if (fmt != null) item.OldPriceText = FStr(fmt, "originalPrice");
                    item.DiscountText = "-100%";
                    item.Note = string.Format(Loc("LOC_KX_StoreFreeUntil", "Free until {0}"), nowUntil.Value.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture));
                }
                else
                {
                    item.PriceText = fmt != null ? FStr(fmt, "originalPrice") : null;
                    item.Note = string.Format(Loc("LOC_KX_StoreFreeFrom", "Free from {0}"), soonFrom.Value.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture));
                }
                list.Add(item);
            }
            return list.OrderBy(i => i.DiscountText == "-100%" ? 0 : 1).ToList();
        }

        private static DateTime? PromoDate(Dictionary<string, object> d, string kind, string field)
        {
            var groups = Dig(d, "promotions", kind) as IEnumerable;
            if (groups == null) return null;
            foreach (var g in groups.Cast<object>().OfType<Dictionary<string, object>>())
            {
                var offers = Dig(g, "promotionalOffers") as IEnumerable;
                if (offers == null) continue;
                foreach (var o in offers.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    DateTime t;
                    if (DateTime.TryParse(FStr(o, field), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return t;
                }
            }
            return null;
        }

        private static string EpicSlug(Dictionary<string, object> d)
        {
            foreach (string where in new[] { "catalogNs", "" })
            {
                var maps = (where.Length > 0 ? Dig(d, where, "mappings") : Dig(d, "offerMappings")) as IEnumerable;
                if (maps == null) continue;
                foreach (var m in maps.Cast<object>().OfType<Dictionary<string, object>>())
                    if (!string.IsNullOrEmpty(FStr(m, "pageSlug"))) return FStr(m, "pageSlug");
            }
            string slug = FStr(d, "productSlug");
            if (!string.IsNullOrEmpty(slug)) return slug.Replace("/home", "");
            return FStr(d, "urlSlug");
        }

        private static List<string> EpicImages(Dictionary<string, object> d)
        {
            var result = new List<string>();
            var images = Dig(d, "keyImages") as IEnumerable;
            if (images == null) return result;
            var list = images.Cast<object>().OfType<Dictionary<string, object>>().ToList();
            foreach (string type in new[] { "OfferImageWide", "DieselStoreFrontWide", "featuredMedia", "Thumbnail", "OfferImageTall" })
                foreach (var hit in list.Where(i => FStr(i, "type") == type)) result.Add(FStr(hit, "url"));
            foreach (var i in list) if (!result.Contains(FStr(i, "url"))) result.Add(FStr(i, "url"));
            // Epic's CDN can resize: ask for a sensible width instead of a 2560 px original
            return result.Where(u => u.Length > 0).Select(u => u.Contains("?") ? u : u + "?w=960&h=540&resize=1").Distinct().ToList();
        }

        // ---------------- IsThereAnyDeal ----------------

        /// <summary>The theme's IsThereAnyDeal app key (its "API key", not the OAuth secret). Everyone uses this one (0.30: no own key any more).</summary>
        private const string BuiltInItadKey = "b3aa17d31634c227a985fb9090ffb4834ded5d6d";

        private string ItadKey { get { return BuiltInItadKey; } }
        public bool HasItadKey { get { return !string.IsNullOrEmpty(ItadKey); } }

        /// <summary>The stores prices come from: the official stores (Steam, Epic, GOG, EA, Ubisoft, Battle.net, Microsoft),
        /// plus Humble, Fanatical and Green Man Gaming if you turn on resellers. No grey-market key sites.</summary>
        private string ItadShops { get { return "61,16,35,52,62,4,48" + (settings.StoreResellers ? ",37,6,36" : ""); } }

        private object Itad(string path, string query, string body)
        {
            string url = "https://api.isthereanydeal.com/" + path + "?key=" + Uri.EscapeDataString(ItadKey) + (string.IsNullOrEmpty(query) ? "" : "&" + query);
            return body == null ? StoreJson(url) : StorePostJson(url, body);
        }

        /// <summary>IsThereAnyDeal's id for a game: by Steam app id when we have one, otherwise by name.</summary>
        private string ItadLookup(string steamAppId, string title)
        {
            if (!HasItadKey) return null;
            Dictionary<string, object> r = null;
            if (!string.IsNullOrEmpty(steamAppId))
                try { r = Itad("games/lookup/v1", "appid=" + Uri.EscapeDataString(steamAppId), null) as Dictionary<string, object>; } catch (Exception) { }
            if ((r == null || !(Dig(r, "found") is bool && (bool)Dig(r, "found"))) && !string.IsNullOrEmpty(title))
                r = Itad("games/lookup/v1", "title=" + Uri.EscapeDataString(title), null) as Dictionary<string, object>;
            if (r == null || !(Dig(r, "found") is bool) || !(bool)Dig(r, "found")) return null;
            var g = Dig(r, "game") as Dictionary<string, object>;
            return g != null ? FStr(g, "id") : null;
        }

        /// <summary>IsThereAnyDeal's ids for many Steam games in one ask (Steam app id -> its id; the ones it doesn't know are left out).</summary>
        private Dictionary<string, string> ItadIdsForSteam(IList<string> appIds)
        {
            var result = new Dictionary<string, string>();
            if (!HasItadKey || appIds == null || appIds.Count == 0) return result;
            var all = appIds.Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
            for (int i = 0; i < all.Count; i += 200)
            {
                string body = new JavaScriptSerializer().Serialize(all.Skip(i).Take(200).Select(a => "app/" + a).ToArray());
                var map = Itad("lookup/id/shop/61/v1", null, body) as Dictionary<string, object>;   // 61: Steam
                if (map == null) continue;
                foreach (var kv in map)
                {
                    string id = kv.Value as string;
                    if (!string.IsNullOrEmpty(id) && kv.Key.StartsWith("app/")) result[kv.Key.Substring(4)] = id;
                }
            }
            return result;
        }

        /// <summary>Every shop's current price for these games, plus the lowest price ever, in the chosen country.</summary>
        private Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>> ItadPrices(IList<string> ids, string shops)
        {
            var result = new Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>>();
            if (!HasItadKey || ids == null || ids.Count == 0) return result;
            string body = new JavaScriptSerializer().Serialize(ids.Distinct().Take(200).ToArray());
            var arr = Itad("games/prices/v3", "country=" + Country + "&deals=false&vouchers=true&shops=" + shops, body) as IEnumerable;
            if (arr == null) return result;
            foreach (var g in arr.Cast<object>().OfType<Dictionary<string, object>>())
            {
                var deals = new List<ItadDeal>();
                var list = Dig(g, "deals") as IEnumerable;
                if (list != null)
                    foreach (var d in list.Cast<object>().OfType<Dictionary<string, object>>())
                    {
                        var shopInfo = Dig(d, "shop") as Dictionary<string, object>;
                        var price = Dig(d, "price") as Dictionary<string, object>;
                        var regular = Dig(d, "regular") as Dictionary<string, object>;
                        if (shopInfo == null || price == null) continue;
                        deals.Add(new ItadDeal
                        {
                            Shop = FStr(shopInfo, "name"),
                            Price = FDec(price, "amount"),
                            Currency = FStr(price, "currency"),
                            Regular = regular != null ? FDec(regular, "amount") : 0,
                            Cut = (int)FNum(d, "cut"),
                            Url = FStr(d, "url"),
                            Voucher = FStr(d, "voucher"),
                            StoreLow = false,
                            Expiry = ParseUtc(FStr(d, "expiry"))
                        });
                    }
                ItadDeal low = null;
                var all = Dig(g, "historyLow", "all") as Dictionary<string, object>;
                if (all != null) low = new ItadDeal { Price = FDec(all, "amount"), Currency = FStr(all, "currency") };
                result[FStr(g, "id")] = Tuple.Create(deals, low);
            }
            return result;
        }

        /// <summary>Search every store IsThereAnyDeal knows (includes EA, Ubisoft, Humble, Fanatical...), or only games one shop sells.</summary>
        private List<StoreItem> ItadSearch(string text, string shops, bool onlyThere)
        {
            var arr = Itad("games/search/v1", "results=40&title=" + Uri.EscapeDataString(text), null) as IEnumerable;
            var list = new List<StoreItem>();
            if (arr == null) return list;
            foreach (var g in arr.Cast<object>().OfType<Dictionary<string, object>>())
            {
                if (FStr(g, "type") != "game") continue;
                list.Add(ItadTile(g));
            }
            return AddItadPrices(list, shops, onlyThere);
        }

        private StoreItem ItadTile(Dictionary<string, object> g)
        {
            var assets = Dig(g, "assets") as Dictionary<string, object> ?? new Dictionary<string, object>();
            var pics = new[] { "banner600", "banner400", "banner300", "boxart", "banner145" }.Select(k => FStr(assets, k)).Where(u => u.Length > 0).ToList();
            return new StoreItem
            {
                Store = "", Id = FStr(g, "id"), ItadId = FStr(g, "id"), Name = FStr(g, "title"),
                Image = pics.FirstOrDefault(), ImageFallbacks = pics.Skip(1).ToList()
            };
        }

        /// <summary>Puts the cheapest current price (and where) on IsThereAnyDeal tiles. With a shop: only that shop's price, and only games it sells.</summary>
        private List<StoreItem> AddItadPrices(List<StoreItem> items, string shops, bool onlyThere)
        {
            // only games one of these stores actually sells
            var ids = items.Where(i => !string.IsNullOrEmpty(i.ItadId)).Select(i => i.ItadId).ToList();
            if (ids.Count == 0) return new List<StoreItem>();
            Dictionary<string, Tuple<List<ItadDeal>, ItadDeal>> prices;
            try { prices = ItadPrices(ids, shops); } catch (Exception) { return new List<StoreItem>(); }
            var kept = new List<StoreItem>();
            foreach (var i in items)
            {
                Tuple<List<ItadDeal>, ItadDeal> p;
                if (i.ItadId == null || !prices.TryGetValue(i.ItadId, out p) || p.Item1.Count == 0) continue;
                kept.Add(i);
                var best = p.Item1.OrderBy(d => d.Price).First();
                if (onlyThere) i.Link = ShopLink(best.Url, best.Shop, i.Name, i.SteamAppId);
                i.PriceText = best.Price == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(best.Price, best.Currency);
                if (best.Cut > 0) { i.DiscountText = "-" + best.Cut + "%"; i.OldPriceText = Money(best.Regular, best.Currency); }
                var sellers = p.Item1.Select(d => d.Shop).Distinct().ToList();
                i.Note = string.Format(Loc("LOC_KX_StoreAtShop", "at {0}"), best.Shop) + (sellers.Count > 1 ? "   ·   " + string.Format(Loc("LOC_KX_StoreShopCount", "{0} stores"), sellers.Count) : "");
            }
            return kept;
        }

        /// <summary>
        /// Current deals across every store (biggest discount first, or cheapest), or one shop's games
        /// (with nondeals: everything it sells, not only what's on sale).
        /// </summary>
        private StoreResults ItadDeals(string sort, int page, string shops, bool nondeals)
        {
            // a big page, because add-ons and soundtracks are dropped (only games are shown)
            var root = Itad("deals/v2", "country=" + Country + "&limit=150&offset=" + (page * 150) + "&sort=" + Uri.EscapeDataString(sort)
                                        + "&shops=" + shops + (nondeals ? "&nondeals=true" : ""), null) as Dictionary<string, object>;
            var res = new StoreResults();
            if (root == null) return res;
            res.HasMore = Dig(root, "hasMore") is bool && (bool)Dig(root, "hasMore");
            var list = Dig(root, "list") as IEnumerable;
            if (list == null) return res;
            foreach (var g in list.Cast<object>().OfType<Dictionary<string, object>>())
            {
                if (FStr(g, "type") != "game") continue;
                var item = ItadTile(g);
                var deal = Dig(g, "deal") as Dictionary<string, object>;
                if (deal != null)
                {
                    var price = Dig(deal, "price") as Dictionary<string, object>;
                    var regular = Dig(deal, "regular") as Dictionary<string, object>;
                    var shopInfo = Dig(deal, "shop") as Dictionary<string, object>;
                    if (price != null)
                    {
                        decimal amount = FDec(price, "amount");
                        item.PriceText = amount == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(amount, FStr(price, "currency"));
                        int cut = (int)FNum(deal, "cut");
                        if (cut > 0 && regular != null) { item.DiscountText = "-" + cut + "%"; item.OldPriceText = Money(FDec(regular, "amount"), FStr(regular, "currency")); }
                    }
                    if (shopInfo != null) item.Note = string.Format(Loc("LOC_KX_StoreAtShop", "at {0}"), FStr(shopInfo, "name"));
                    string url = ShopLink(FStr(deal, "url"), shopInfo != null ? FStr(shopInfo, "name") : "", item.Name, null);
                    if (!string.IsNullOrEmpty(url)) item.Link = url;
                }
                res.Items.Add(item);
            }
            return res;
        }

        /// <summary>The lowest price ever at the official stores, and where: "£4.99 at Steam, Mar 2025".</summary>
        private string ItadStoreLow(string itadId)
        {
            var arr = Itad("games/storelow/v2", "country=" + Country + "&shops=" + ItadShops, new JavaScriptSerializer().Serialize(new[] { itadId })) as IEnumerable;
            if (arr == null) return null;
            Dictionary<string, object> best = null;
            foreach (var g in arr.Cast<object>().OfType<Dictionary<string, object>>())
            {
                var lows = Dig(g, "lows") as IEnumerable;
                if (lows == null) continue;
                foreach (var l in lows.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var p = Dig(l, "price") as Dictionary<string, object>;
                    if (p == null) continue;
                    if (best == null || FDec(p, "amount") < FDec(Dig(best, "price") as Dictionary<string, object>, "amount")) best = l;
                }
            }
            if (best == null) return null;
            var price = Dig(best, "price") as Dictionary<string, object>;
            decimal amount = FDec(price, "amount");
            string shop = FStr(Dig(best, "shop") as Dictionary<string, object> ?? new Dictionary<string, object>(), "name");
            DateTime when;
            string date = DateTime.TryParse(FStr(best, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out when)
                ? when.ToLocalTime().ToString("MMM yyyy", CultureInfo.CurrentCulture) : "";
            string money = amount == 0 ? Loc("LOC_KX_StoreFree", "Free") : Money(amount, FStr(price, "currency"));
            return string.Format(Loc("LOC_KX_StoreLowestAt", "Lowest ever: {0} at {1}"), money, ShopName(shop)) + (date.Length > 0 ? ", " + date : "");
        }

        /// <summary>IsThereAnyDeal's and CheapShark's buy links go through their own redirect, which adds the store's affiliate code.</summary>
        private static bool IsRedirectLink(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "itad.link" || h.EndsWith(".itad.link") || h == "isthereanydeal.com" || h.EndsWith(".isthereanydeal.com")
                || h == "cheapshark.com" || h.EndsWith(".cheapshark.com");
        }

        /// <summary>
        /// A deal's link without anyone's referral code: a redirect becomes the store's own page for the game (or its search
        /// for the name). Links straight to a store are kept as they are.
        /// </summary>
        private static string ShopLink(string url, string shop, string title, string steamAppId)
        {
            if (!IsRedirectLink(url)) return url;
            string q = Uri.EscapeDataString(title ?? "");
            string s = (shop ?? "").ToLowerInvariant();
            if (s.Contains("epic")) return "https://store.epicgames.com/browse?q=" + q;
            if (s.Contains("gog")) return "https://www.gog.com/en/games?query=" + q;
            if (s.Contains("humble")) return "https://www.humblebundle.com/store/search?search=" + q;
            if (s.Contains("fanatical")) return "https://www.fanatical.com/en/search?search=" + q;
            if (s.Contains("green") || s.Contains("gmg")) return "https://www.greenmangaming.com/search?query=" + q;
            if (s.StartsWith("ea") || s.Contains("origin")) return StoreDef("ea")[6];
            if (s.Contains("ubisoft")) return StoreDef("ubisoft")[6];
            if (s.Contains("blizzard") || s.Contains("battle")) return StoreDef("battlenet")[6];
            if (s.Contains("microsoft") || s.Contains("xbox")) return StoreDef("xbox")[6];
            // Steam, or a shop we don't know: Steam's page (or search) for the game
            return !string.IsNullOrEmpty(steamAppId) ? "https://store.steampowered.com/app/" + steamAppId + "/" : "https://store.steampowered.com/search/?term=" + q;
        }

        /// <summary>IsThereAnyDeal's shop names, as the theme calls them.</summary>
        private static string ShopName(string itad)
        {
            switch (itad)
            {
                case "Epic Game Store": return "Epic Games";
                case "Blizzard": return "Battle.net";
                case "Microsoft Store": return "Microsoft Game Store";
                case "GreenManGaming": return "Green Man Gaming";
            }
            return itad;
        }

        /// <summary>Every price change at the official stores (for the highest price ever).</summary>
        private List<PricePoint> ItadHistory(string itadId)
        {
            // the whole history (without "since" it's only the last 3 months), for the highest price ever
            var arr = Itad("games/history/v2", "id=" + Uri.EscapeDataString(itadId) + "&country=" + Country + "&shops=" + ItadShops
                           + "&since=" + Uri.EscapeDataString("2000-01-01T00:00:00Z"), null) as IEnumerable;
            var list = new List<PricePoint>();
            if (arr == null) return list;
            foreach (var h in arr.Cast<object>().OfType<Dictionary<string, object>>())
            {
                DateTime when;
                if (!DateTime.TryParse(FStr(h, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out when)) continue;
                var price = Dig(h, "deal", "price") as Dictionary<string, object>;
                if (price == null) continue;
                list.Add(new PricePoint { When = when, Price = FDec(price, "amount"), Shop = ShopName(FStr(Dig(h, "shop") as Dictionary<string, object> ?? new Dictionary<string, object>(), "name")) });
            }
            return list.OrderBy(p => p.When).ToList();
        }

        /// <summary>Subscriptions the game is in (Game Pass, EA Play, Ubisoft+...). Best effort: nothing if the service doesn't answer.</summary>
        private List<string> ItadSubs(string itadId)
        {
            var names = new List<string>();
            try
            {
                var arr = Itad("games/subs/v1", "country=" + Country, new JavaScriptSerializer().Serialize(new[] { itadId })) as IEnumerable;
                if (arr == null) return names;
                foreach (var g in arr.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    var subs = Dig(g, "subs") as IEnumerable;
                    if (subs == null) continue;
                    foreach (var s in subs.Cast<object>().OfType<Dictionary<string, object>>())
                    {
                        string n = FStr(s, "name");
                        if (n.Length > 0 && !names.Contains(n)) names.Add(n);
                    }
                }
            }
            catch (Exception) { }
            return names;
        }

        /// <summary>Steam Deck compatibility: 3 verified, 2 playable, 1 unsupported, 0 unknown.</summary>
        private int SteamDeck(string appId)
        {
            try
            {
                var root = StoreJson("https://store.steampowered.com/saleaction/ajaxgetdeckappcompatibilityreport?l=english&nAppID=" + Uri.EscapeDataString(appId)) as Dictionary<string, object>;
                var res = Dig(root, "results") as Dictionary<string, object>;
                return res != null ? (int)FNum(res, "resolved_category") : 0;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>IsThereAnyDeal's details for a game: its Steam app id (for Steam's pictures and trailer), release, developer, tags, pictures.</summary>
        private ItadGameInfo ItadInfo(string itadId)
        {
            var info = Itad("games/info/v2", "id=" + Uri.EscapeDataString(itadId), null) as Dictionary<string, object>;
            if (info == null) return null;
            var r = new ItadGameInfo();
            string appid = FStr(info, "appid");
            r.SteamAppId = appid.Length > 0 && appid != "0" ? appid : null;
            var facts = new List<string>();
            string rel = FStr(info, "releaseDate");
            if (rel.Length >= 4) facts.Add(rel.Substring(0, Math.Min(10, rel.Length)));
            DateTime relDate;
            if (rel.Length >= 10 && DateTime.TryParseExact(rel.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out relDate)) r.ReleaseDate = relDate;
            var devs = Dig(info, "developers") as System.Collections.IEnumerable;
            if (devs != null) facts.Add(string.Join(", ", devs.Cast<object>().OfType<Dictionary<string, object>>().Select(d => FStr(d, "name")).Take(2).ToArray()));
            var tags = Dig(info, "tags") as System.Collections.IEnumerable;
            if (tags != null) facts.Add(string.Join(", ", tags.Cast<object>().Select(t => Convert.ToString(t)).Take(3).ToArray()));
            r.Facts = string.Join("   ·   ", facts.Where(f => !string.IsNullOrEmpty(f)).ToArray());
            var assets = Dig(info, "assets") as Dictionary<string, object> ?? new Dictionary<string, object>();
            r.Pictures = new[] { "banner600", "banner400", "boxart", "banner300" }.Select(k => FStr(assets, k)).Where(u => u.Length > 0).ToList();
            var reviews = Dig(info, "reviews") as System.Collections.IEnumerable;
            if (reviews != null)
                r.Scores = string.Join("   ·   ", reviews.Cast<object>().OfType<Dictionary<string, object>>()
                    .Where(x => FNum(x, "score") > 0)
                    .Select(x => FStr(x, "source") + " " + FNum(x, "score") + (FStr(x, "source") == "Steam" ? "%" : "")).ToArray());
            return r;
        }

        /// <summary>The age rating for your country (PEGI in the UK and Europe, ESRB in North America, USK in Germany...).</summary>
        private string SteamAgeRating(Dictionary<string, object> ratings)
        {
            if (ratings == null) return null;
            string c = Country;
            string[] order = c == "US" || c == "CA" || c == "MX" ? new[] { "esrb", "pegi" }
                           : c == "DE" ? new[] { "usk", "pegi" }
                           : c == "BR" ? new[] { "dejus", "esrb" }
                           : c == "AU" || c == "NZ" ? new[] { "steam_australia", "pegi" }
                           : new[] { "pegi", "esrb" };
            foreach (string k in order)
            {
                var r = Dig(ratings, k) as Dictionary<string, object>;
                if (r == null) continue;
                string rating = FStr(r, "rating").Trim();
                if (rating.Length == 0) continue;
                string label = k == "steam_australia" ? "AU" : k == "dejus" ? "ClassInd" : k.ToUpperInvariant();
                string desc = string.Join(", ", HtmlText(FStr(r, "descriptors")).Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(x => x.Trim()).Where(x => x.Length > 0).Take(4).ToArray());
                return label + " " + rating.ToUpperInvariant() + (desc.Length > 0 ? "   ·   " + desc : "");
            }
            return null;
        }

        /// <summary>A Steam game's add-ons (DLC), with prices.</summary>
        private List<StoreItem> SteamDlc(string appId)
        {
            var root = StoreJson("https://store.steampowered.com/api/dlcforapp/?l=english&cc=" + Country + "&appid=" + Uri.EscapeDataString(appId)) as Dictionary<string, object>;
            var list = new List<StoreItem>();
            var dlc = Dig(root, "dlc") as IEnumerable;
            if (dlc == null) return list;
            foreach (var d in dlc.Cast<object>().OfType<Dictionary<string, object>>())
            {
                string id = FStr(d, "id");
                var p = Dig(d, "price_overview") as Dictionary<string, object>;
                var item = SteamTile(id, FStr(d, "name"), FStr(d, "header_image"),
                                     p != null ? (int)FNum(p, "final") : 0, p != null ? (int)FNum(p, "initial") : 0,
                                     p != null ? (int)FNum(p, "discount_percent") : 0, p != null ? FStr(p, "currency") : CurrencyCode);
                // no price usually means not out yet / not sold here, not free
                bool isFree = d.ContainsKey("is_free") && d["is_free"] is bool && (bool)d["is_free"];
                if (p == null) item.PriceText = isFree ? Loc("LOC_KX_StoreFree", "Free") : null;
                list.Add(item);
            }
            return list;
        }

        /// <summary>Steam's review score and its most helpful reviews (in English).</summary>
        private StoreReviews SteamReviews(string appId)
        {
            return SteamReviews(appId, "all", "*", 8);
        }

        private static readonly Dictionary<string, Tuple<DateTime, StoreReviews>> steamReviewCache = new Dictionary<string, Tuple<DateTime, StoreReviews>>();

        /// <summary>(0.99.4) Steam's reviews for a game's page: kept for half an hour (going back to a game doesn't ask again), asked a
        /// second time when Steam doesn't answer (it turns busy callers away for a moment), and in any language when there are no English
        /// ones (the score line, at least). Why there are none is written in the log.</summary>
        private StoreReviews SteamReviewsSure(string appId, string name)
        {
            lock (steamReviewCache)
            {
                Tuple<DateTime, StoreReviews> c;
                if (steamReviewCache.TryGetValue(appId, out c) && (DateTime.UtcNow - c.Item1).TotalMinutes < 30) return c.Item2;
            }
            StoreReviews r = null;
            string why = null;
            // (0.99.27) Steam sent a web page: asking again won't change that, so straight on to its store page.
            // (0.99.33) And once it has, the feed isn't asked again for 6 hours (each try was a whole 1 MB web page).
            bool page = DateTime.UtcNow < steamFeedIsPageUntil;
            if (page) why = "Steam's review feed sends a web page (skipped)";
            for (int attempt = 0; attempt < 2 && r == null && !page; attempt++)
            {
                if (attempt > 0) System.Threading.Thread.Sleep(800);
                try { r = SteamReviews(appId); if (r == null) why = "no summary in Steam's answer"; }
                catch (Exception ex)
                {
                    why = ex.Message;
                    page = ex is InvalidOperationException && ex.Message.StartsWith("not JSON", StringComparison.Ordinal);
                    if (page) steamFeedIsPageUntil = DateTime.UtcNow.AddHours(6);
                }
            }
            if (r != null && !HasReviews(r))
            {
                try
                {
                    var any = SteamReviews(appId, "all", "*", 8, "all");
                    if (HasReviews(any)) r = any; else why = "Steam has no reviews for it";
                }
                catch (Exception ex) { why = ex.Message; }
            }
            if (!HasReviews(r) || (page && r.Items.Count == 0))
            {
                // (0.99.28) Steam's review feed sends its web page now: the reviews people wrote come from the game's Steam
                // Community reviews page instead (most helpful first), with the score off the store page
                try
                {
                    // (0.99.33) the score (the store page) is read at the same time as the reviews, not after them
                    StoreReviews sum = null;
                    var sumTask = System.Threading.Tasks.Task.Factory.StartNew(() => { try { sum = SteamReviewSummaryFromPage(appId); } catch (Exception) { } });
                    var c = SteamCommunityReviews(appId, "all", "1", 10);
                    logger.Info("Kippix: store reviews: Steam Community for \"" + name + "\": " + communityInfo);
                    try { sumTask.Wait(15000); } catch (Exception) { }
                    if (c == null || c.Items.Count == 0) { if (sum != null && HasReviews(sum)) { r = sum; why = null; } }
                    if (c != null && c.Items.Count > 0)
                    {
                        if (sum != null) { c.Title = sum.Title; c.Line = sum.Line; }
                        r = c;
                        why = null;
                        logger.Info("Kippix: store reviews: Steam's for \"" + name + "\" from Steam Community (" + c.Items.Count + " reviews)");
                    }
                }
                catch (Exception ex) { why = (why ?? "") + "; Steam Community: " + ex.Message; logger.Info("Kippix: store reviews: Steam Community for \"" + name + "\" failed: " + ex.Message); }
            }
            if (!HasReviews(r))
            {
                // (0.99.26) no JSON from Steam: at least its score, from the game's store page ("Very Positive · 92% of 12,345")
                try { var s = SteamReviewSummaryFromPage(appId); if (HasReviews(s)) { r = s; why = null; logger.Info("Kippix: store reviews: Steam's score for \"" + name + "\" from its store page (no reviews to read)"); } }
                catch (Exception ex) { why = (why ?? "") + "; the store page: " + ex.Message; }
            }
            if (!HasReviews(r)) logger.Info("Kippix: store reviews: Steam's for \"" + name + "\" (" + appId + ") didn't come: " + (why ?? "none"));
            else lock (steamReviewCache) steamReviewCache[appId] = Tuple.Create(DateTime.UtcNow, r);
            return r;
        }

        /// <summary>
        /// (0.99.28) A page of a game's reviews from Steam Community (steamcommunity.com/app/ID/reviews), the reviews people wrote
        /// with Recommended / Not recommended, hours on record and how many found it helpful. kind: all (most helpful), recent,
        /// positive, negative (those two filtered here). cursor: the page number ("1" first); the next one comes back in Cursor.
        /// </summary>
        private static DateTime steamFeedIsPageUntil = DateTime.MinValue;   // (0.99.33) Steam's appreviews feed sent a web page: don't ask it until then
        private static string communityInfo = "";   // (0.99.31) what the last Steam Community page held, for the log

        private StoreReviews SteamCommunityReviews(string appId, string kind, string cursor, int count)
        {
            int pageNo;
            if (!int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out pageNo) || pageNo < 1) pageNo = 1;
            string filter = kind == "recent" ? "mostrecent" : "toprated";
            string url = "https://steamcommunity.com/app/" + Uri.EscapeDataString(appId) + "/homecontent/?userreviewsoffset=" + ((pageNo - 1) * 10)
                       + "&p=" + pageNo + "&itemspage=" + pageNo + "&screenshotspage=" + pageNo + "&videospage=" + pageNo + "&artpage=" + pageNo
                       + "&allguidepage=" + pageNo + "&webguidepage=" + pageNo + "&integratedguidepage=" + pageNo + "&discussionspage=" + pageNo
                       + "&numperpage=10&browsefilter=" + filter + "&appid=" + Uri.EscapeDataString(appId) + "&appHubSubSection=10&l=english&filterLanguage=default&searchText=&maxInappropriateScore=100";
            string html;
            using (var web = StoreWeb())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
                web.Headers[HttpRequestHeader.Cookie] = "birthtime=470703601; lastagecheckage=1-0-1985; wants_mature_content=1; mature_content=1; Steam_Language=english";
                html = web.DownloadString(url);
            }
            if (html.IndexOf("class=\"apphub_Card ", StringComparison.Ordinal) < 0)
            {
                // (the first page, as a whole page, if the part didn't come)
                if (pageNo != 1) return new StoreReviews { Source = "steamc", SourceId = appId, Cursor = "" };
                using (var web = StoreWeb())
                {
                    web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
                    web.Headers[HttpRequestHeader.Cookie] = "birthtime=470703601; lastagecheckage=1-0-1985; wants_mature_content=1; mature_content=1; Steam_Language=english";
                    html = web.DownloadString("https://steamcommunity.com/app/" + Uri.EscapeDataString(appId) + "/reviews/?browsefilter=" + filter + "&l=english");
                }
            }
            var r = new StoreReviews { Source = "steamc", SourceId = appId };
            var cards = Regex.Split(html ?? "", "class=\"apphub_Card ");   // (0.99.32: Steam puts other attributes before class now)
            for (int i = 1; i < cards.Length; i++)
            {
                string card = cards[i];
                r.RawCount++;
                string verdict = HtmlText(Grab(card, "<div[^>]*class=\"title\"[^>]*>(.*?)</div>")).Trim();
                bool positive = verdict.IndexOf("Not ", StringComparison.OrdinalIgnoreCase) < 0;
                if (kind == "positive" && !positive) continue;
                if (kind == "negative" && positive) continue;
                string hours = HtmlText(Grab(card, "<div[^>]*class=\"hours\"[^>]*>(.*?)</div>")).Trim();
                string helpful = Grab(card, "found_helpful\"[^>]*>\\s*([\\d,\\.]+) (?:people|person)");
                int s = card.IndexOf("apphub_CardTextContent", StringComparison.Ordinal);
                if (s < 0) continue;
                s = card.IndexOf('>', s) + 1;
                int e = card.IndexOf("UserReviewCardContent_Footer", s, StringComparison.Ordinal);
                if (e > s) e = card.LastIndexOf('<', e);
                int e2 = card.IndexOf("apphub_CardContentAuthorBlock", s, StringComparison.Ordinal);
                if (e2 > s) e2 = card.LastIndexOf('<', e2);
                if (e < 0 || (e2 >= 0 && e2 < e)) e = e2;
                string body = e > s ? card.Substring(s, e - s) : card.Substring(s);
                body = Regex.Replace(body, "<div[^>]*class=\"date_posted\"[^>]*>.*?</div>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                body = Regex.Replace(body, "<div[^>]*class=\"early_access_review\"[^>]*>.*?</div>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                body = Regex.Replace(body, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
                string text = HtmlText(body);
                text = Regex.Replace(text, "[ \\t]+", " ");
                text = Regex.Replace(text, "\\s*\\n\\s*(\\n\\s*)+", "\n\n").Trim();
                if (text.Length < 20) continue;
                string full = text;
                if (text.Length > 700) text = text.Substring(0, 700).TrimEnd() + "…";
                var meta = new List<string>();
                if (hours.Length > 0) meta.Add(hours.Replace(" on record", " played"));
                if (helpful.Length > 0) meta.Add(string.Format(Loc("LOC_KX_ReviewHelpful", "{0} found it helpful"), helpful));
                r.Items.Add(new StoreReview
                {
                    Positive = positive,
                    Verdict = positive ? Loc("LOC_KX_ReviewUp", "Recommended") : Loc("LOC_KX_ReviewDown", "Not recommended"),
                    Text = text, FullText = full, Meta = string.Join("   ·   ", meta.ToArray())
                });
                if (r.Items.Count >= count) break;
            }
            r.Cursor = cards.Length > 1 ? (pageNo + 1).ToString(CultureInfo.InvariantCulture) : "";
            string start = Regex.Replace((html ?? "").Length > 140 ? html.Substring(0, 140) : (html ?? ""), "\\s+", " ");
            string title = Grab(html, "<title>(.*?)</title>").Trim();
            communityInfo = (html ?? "").Length + " characters, " + (cards.Length - 1) + " review cards, " + r.Items.Count + " read" + (title.Length > 0 ? ", page \"" + title + "\"" : "")
                          + (cards.Length > 1 ? "" : ". It starts: " + start);
            return r;
        }

        /// <summary>Steam's score for a game read off its store page (the "All Reviews" line), when the reviews themselves don't come.</summary>
        private static StoreReviews SteamReviewSummaryFromPage(string appId)
        {
            string html;
            using (var web = StoreWeb())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
                web.Headers[HttpRequestHeader.Cookie] = "birthtime=470703601; lastagecheckage=1-0-1985; wants_mature_content=1; Steam_Language=english";
                html = web.DownloadString("https://store.steampowered.com/app/" + Uri.EscapeDataString(appId) + "/?l=english");
            }
            var tips = Regex.Matches(html ?? "", "data-tooltip-html=\"(\\d+)% of the ([\\d,\\.]+) user reviews", RegexOptions.IgnoreCase);
            var words = Regex.Matches(html ?? "", "<span class=\"game_review_summary[^\"]*\"[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
            if (tips.Count == 0) return null;
            var tip = tips[tips.Count - 1];   // (Recent Reviews first, All Reviews after)
            long total;
            long.TryParse(tip.Groups[2].Value.Replace(",", "").Replace(".", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out total);
            var r = new StoreReviews { Source = "steam", SourceId = appId };
            r.Title = words.Count > 0 ? WebUtility.HtmlDecode(words[words.Count - 1].Groups[1].Value).Trim() : "";
            r.Line = string.Format(Loc("LOC_KX_ReviewsLine", "{0}% of {1} Steam reviews are positive"), tip.Groups[1].Value, total.ToString("N0", CultureInfo.CurrentCulture));
            return r;
        }

        /// <summary>
        /// A page of Steam reviews. kind: all (most helpful), recent, positive, negative. cursor: "*" for the first page;
        /// the next page's cursor comes back in <see cref="StoreReviews.Cursor"/>.
        /// </summary>
        private StoreReviews SteamReviews(string appId, string kind, string cursor, int count)
        {
            return SteamReviews(appId, kind, cursor, count, "english");
        }

        private StoreReviews SteamReviews(string appId, string kind, string cursor, int count, string language)
        {
            string filter = kind == "recent" ? "recent" : "all";
            string type = kind == "positive" || kind == "negative" ? kind : "all";
            // (0.99.26) Steam was answering with a web page instead of JSON ("Invalid JSON primitive: ."): asked as a browser asks
            // (JSON wanted, past the age check, "*" not written as %2A), then with only what's needed if that's a page too
            string c0 = string.IsNullOrEmpty(cursor) || cursor == "*" ? "*" : Uri.EscapeDataString(cursor);
            string q = "https://store.steampowered.com/appreviews/" + Uri.EscapeDataString(appId);
            var urls = new List<string>
            {
                q + "?json=1&language=" + language + "&filter=" + filter + "&review_type=" + type + "&purchase_type=all&num_per_page=" + count + "&cursor=" + c0,
                q + "?json=1&language=" + language + "&num_per_page=" + count + (c0 == "*" ? "" : "&cursor=" + c0) + (filter == "recent" ? "&filter=recent" : "") + (type != "all" ? "&review_type=" + type : "")
            };
            Dictionary<string, object> root = null;
            Exception last = null;
            foreach (string u in urls)
            {
                try { root = SteamJson(u) as Dictionary<string, object>; if (root != null && root.ContainsKey("query_summary")) break; }
                catch (Exception ex)
                {
                    last = ex; root = null;
                    if (ex is InvalidOperationException && ex.Message.StartsWith("not JSON", StringComparison.Ordinal)) break;   // (a web page: the other way gets one too)
                }
            }
            if (root == null && last != null) throw last;
            var sum = Dig(root, "query_summary") as Dictionary<string, object>;
            if (sum == null) return null;
            var r = new StoreReviews { Source = "steam", SourceId = appId };
            r.Cursor = FStr(root, "cursor");
            long total = FNum(sum, "total_reviews"), up = FNum(sum, "total_positive");
            r.Title = FStr(sum, "review_score_desc");
            if (total > 0)
                r.Line = string.Format(Loc("LOC_KX_ReviewsLine", "{0}% of {1} Steam reviews are positive"), Math.Round(100.0 * up / total), total.ToString("N0", CultureInfo.CurrentCulture));
            var list = Dig(root, "reviews") as IEnumerable;
            if (list != null)
                foreach (var d in list.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    r.RawCount++;
                    string text = Regex.Replace(FStr(d, "review"), "\\[/?[a-z0-9*]+[^\\]]*\\]", "", RegexOptions.IgnoreCase);   // Steam's [b], [h1], [url=..] markup
                    text = Regex.Replace(text, "\\s*\\n\\s*(\\n\\s*)+", "\n\n").Trim();
                    if (text.Length < 20) continue;
                    string full = text;
                    if (text.Length > 700) text = text.Substring(0, 700).TrimEnd() + "…";
                    var author = Dig(d, "author") as Dictionary<string, object>;
                    double hours = author != null ? FNum(author, "playtime_forever") / 60.0 : 0;
                    long helpful = FNum(d, "votes_up");
                    var meta = new List<string>();
                    if (hours > 0) meta.Add(string.Format(Loc("LOC_KX_ReviewHours", "{0} h played"), hours < 10 ? hours.ToString("0.0", CultureInfo.CurrentCulture) : Math.Round(hours).ToString("N0", CultureInfo.CurrentCulture)));
                    if (helpful > 0) meta.Add(string.Format(Loc("LOC_KX_ReviewHelpful", "{0} found it helpful"), helpful.ToString("N0", CultureInfo.CurrentCulture)));
                    bool positive = d.ContainsKey("voted_up") && d["voted_up"] is bool && (bool)d["voted_up"];
                    r.Items.Add(new StoreReview
                    {
                        Positive = positive,
                        Verdict = positive ? Loc("LOC_KX_ReviewUp", "Recommended") : Loc("LOC_KX_ReviewDown", "Not recommended"),
                        Text = text,
                        FullText = full,
                        Meta = string.Join("   ·   ", meta.ToArray())
                    });

                }
            return r;
        }

        /// <summary>
        /// A page of GOG reviews (reviews.gog.com, the API GOG's own site uses). kind: all (most helpful), recent, positive (4-5 stars),
        /// negative (1-2 stars; filtered here, GOG can't). cursor: "*" or a page number.
        /// </summary>
        private StoreReviews GogReviews(string productId, string kind, string cursor, int count)
        {
            int pageNo;
            if (!int.TryParse(cursor, out pageNo) || pageNo < 1) pageNo = 1;
            string order = kind == "recent" ? "desc:date" : "desc:votes";
            var root = StoreJson("https://reviews.gog.com/v1/products/" + Uri.EscapeDataString(productId) + "/reviews?language=in:en-US&limit=" + count
                                 + "&order=" + order + "&page=" + pageNo) as Dictionary<string, object>;
            if (root == null) return null;
            var r = new StoreReviews { Source = "gog", SourceId = productId };
            long reviewCount = FNum(root, "reviewCount"), ratingCount = FNum(root, "ratingCount");
            double avg = 0;
            object a;
            if (root.TryGetValue("overallAvgRating", out a) && a != null) double.TryParse(Convert.ToString(a, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out avg);
            if (avg > 0) r.Title = string.Format(Loc("LOC_KX_GogRating", "{0} / 5 on GOG"), avg.ToString("0.0", CultureInfo.CurrentCulture));
            if (reviewCount > 0 || ratingCount > 0)
                r.Line = string.Format(Loc("LOC_KX_GogReviewsLine", "{0} reviews · {1} ratings"), reviewCount.ToString("N0", CultureInfo.CurrentCulture), ratingCount.ToString("N0", CultureInfo.CurrentCulture));
            long pages = FNum(root, "pages");
            r.Cursor = pageNo < pages ? (pageNo + 1).ToString(CultureInfo.InvariantCulture) : "";
            var items = Dig(root, "_embedded", "items") as IEnumerable;
            if (items != null)
                foreach (var d in items.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    r.RawCount++;
                    var content = Dig(d, "content") as Dictionary<string, object> ?? new Dictionary<string, object>();
                    string text = HtmlText(FStr(content, "description")).Trim();
                    if (text.Length < 20) continue;
                    long stars = FNum(Dig(d, "rating") as Dictionary<string, object> ?? new Dictionary<string, object>(), "value");
                    if (kind == "positive" && stars < 4) continue;
                    if (kind == "negative" && (stars == 0 || stars > 2)) continue;
                    string full = text;
                    if (text.Length > 700) text = text.Substring(0, 700).TrimEnd() + "…";
                    var meta = new List<string>();
                    if (stars > 0) meta.Add(new string('★', (int)Math.Min(5, stars)) + new string('☆', (int)Math.Max(0, 5 - stars)));
                    string who = FStr(Dig(d, "reviewer") as Dictionary<string, object> ?? new Dictionary<string, object>(), "username");
                    if (who.Length > 0) meta.Add(who);
                    long helpful = FNum(Dig(d, "votes") as Dictionary<string, object> ?? new Dictionary<string, object>(), "upvotes");
                    if (helpful > 0) meta.Add(string.Format(Loc("LOC_KX_ReviewHelpful", "{0} found it helpful"), helpful.ToString("N0", CultureInfo.CurrentCulture)));
                    string title = HtmlText(FStr(content, "title")).Trim();
                    r.Items.Add(new StoreReview
                    {
                        Positive = stars >= 3,
                        Verdict = title.Length > 0 ? title : (stars >= 3 ? Loc("LOC_KX_ReviewUp", "Recommended") : Loc("LOC_KX_ReviewDown", "Not recommended")),
                        Text = text,
                        FullText = full,
                        Meta = string.Join("   ·   ", meta.ToArray())
                    });
                }
            return r;
        }

        private static DateTime? ParseUtc(string s)
        {
            DateTime t;
            if (string.IsNullOrEmpty(s) || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return null;
            return t;
        }

        private static bool HasReviews(StoreReviews r)
        {
            return r != null && (r.Items.Count > 0 || !string.IsNullOrEmpty(r.Line));
        }

        // ---------------- CheapShark (no key: Epic price, lowest ever, in US dollars) ----------------

        private Tuple<StorePrice, string> CheapShark(string name)
        {
            var found = StoreJson("https://www.cheapshark.com/api/1.0/games?limit=5&title=" + Uri.EscapeDataString(name)) as IEnumerable;
            if (found == null) return null;
            string key = NameKey(name);
            var hit = found.Cast<object>().OfType<Dictionary<string, object>>().FirstOrDefault(g => NameKey(FStr(g, "external")) == key);
            if (hit == null) return null;
            var game = StoreJson("https://www.cheapshark.com/api/1.0/games?id=" + Uri.EscapeDataString(FStr(hit, "gameID"))) as Dictionary<string, object>;
            if (game == null) return null;
            string lowest = null;
            var ever = Dig(game, "cheapestPriceEver") as Dictionary<string, object>;
            if (ever != null && !string.IsNullOrEmpty(FStr(ever, "price")))
            {
                string when = "";
                long unix = FNum(ever, "date");
                if (unix > 0) when = " (" + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime().ToString("MMM yyyy", CultureInfo.CurrentCulture) + ")";
                lowest = string.Format(Loc("LOC_KX_StoreLowestUs", "Lowest ever (US price, CheapShark): ${0}{1}"), FStr(ever, "price"), when);
            }
            StorePrice epic = null;
            var deals = Dig(game, "deals") as IEnumerable;
            if (deals != null)
            {
                var d = deals.Cast<object>().OfType<Dictionary<string, object>>().FirstOrDefault(x => FStr(x, "storeID") == "25");
                if (d != null)
                {
                    string price = FStr(d, "price"), retail = FStr(d, "retailPrice");
                    decimal savings;
                    decimal.TryParse(FStr(d, "savings"), NumberStyles.Number, CultureInfo.InvariantCulture, out savings);
                    // Epic's own search, not CheapShark's redirect (that one adds an affiliate code)
                    string link = "https://store.epicgames.com/browse?q=" + Uri.EscapeDataString(name);
                    epic = new StorePrice
                    {
                        StoreName = "Epic Games",
                        PriceText = "$" + price,
                        OldPriceText = savings >= 1 ? "$" + retail : null,
                        DiscountText = savings >= 1 ? "-" + Math.Round(savings) + "%" : null,
                        Note = Loc("LOC_KX_StoreUsPrice", "US price"),
                        Amount = ParseAmount(price),
                        Currency = "USD",
                        OpenCommand = new RelayCommand(_ => OpenStoreLink(link, null))
                    };
                }
            }
            return Tuple.Create(epic, lowest);
        }

        // ---------------- web helpers ----------------

        /// <summary>A WebClient that gives up after 20 seconds (the default is 100, which left the Store saying "Loading…" far too long).</summary>
        private class StoreWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address);
                var h = r as HttpWebRequest;
                if (h != null) { h.Timeout = 20000; h.ReadWriteTimeout = 20000; }
                else if (r != null) r.Timeout = 20000;
                return r;
            }
        }

        private static WebClient StoreWeb()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var web = new StoreWebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Kippix";
            web.Headers[HttpRequestHeader.AcceptLanguage] = "en-GB,en;q=0.8";
            web.Encoding = Encoding.UTF8;
            return web;
        }

        private static object StoreJson(string url)
        {
            string body;
            using (var web = StoreWeb()) body = web.DownloadString(url);
            return ParseStoreJson(body, url);
        }

        /// <summary>(0.99.26) Steam's store, asked the way its own pages ask: a browser's name, JSON wanted, past the age check.</summary>
        private static object SteamJson(string url)
        {
            string body;
            using (var web = StoreWeb())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";
                web.Headers[HttpRequestHeader.Accept] = "application/json, text/javascript, */*; q=0.01";
                web.Headers[HttpRequestHeader.Cookie] = "birthtime=470703601; lastagecheckage=1-0-1985; wants_mature_content=1; Steam_Language=english";
                web.Headers["X-Requested-With"] = "XMLHttpRequest";
                body = web.DownloadString(url);
            }
            return ParseStoreJson(body, url);
        }

        private static readonly HashSet<string> jsonOddLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// (0.99.26) .NET's reader first; when it turns the answer down (Steam's reviews: "Invalid JSON primitive"), the forgiving
        /// one (MiniJson). The first time for each site, the log gets the start of the answer.
        /// </summary>
        private static object ParseStoreJson(string body, string url)
        {
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(body); }
            catch (Exception first)
            {
                string host = "";
                try { host = new Uri(url).Host + new Uri(url).AbsolutePath.Split('/').Skip(1).Take(1).Select(x => "/" + x).FirstOrDefault(); } catch (Exception) { }
                bool tell;
                lock (jsonOddLogged) tell = jsonOddLogged.Add(host);
                string start = (body ?? "").Length > 160 ? body.Substring(0, 160) : (body ?? "");
                start = System.Text.RegularExpressions.Regex.Replace(start, "\\s+", " ");
                try
                {
                    if ((body ?? "").TrimStart().StartsWith("<", StringComparison.Ordinal)) throw new FormatException("a web page");
                    var o = MiniJson.Parse(body);
                    if (tell) logger.Info("Kippix: store: " + host + " sent JSON .NET couldn't read (" + first.Message + "), read it the forgiving way. It starts: " + start);
                    return o;
                }
                catch (Exception)
                {
                    if (tell) logger.Info("Kippix: store: " + host + " didn't send JSON (" + (body ?? "").Length + " characters). It starts: " + start);
                    throw new InvalidOperationException("not JSON from " + host + ": " + (start.Length > 60 ? start.Substring(0, 60) : start));
                }
            }
        }

        private static object StorePostJson(string url, string body)
        {
            using (var web = StoreWeb())
            {
                web.Headers[HttpRequestHeader.ContentType] = "application/json";
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(web.UploadString(url, "POST", body));
            }
        }

        private static object Dig(object o, params string[] path)
        {
            foreach (string p in path)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(p, out o)) return null;
            }
            return o;
        }

        private static decimal FDec(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToDecimal(v, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }

        private static string Grab(string text, string pattern)
        {
            var m = Regex.Match(text ?? "", pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>HTML to plain text (tags removed, entities decoded, spaces tidied).</summary>
        private static string HtmlText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string s = Regex.Replace(html, "<br\\s*/?>|</p>|</li>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, "[ \\t\\u00A0]+", " ");
            s = Regex.Replace(s, "\\s*\\n\\s*(\\n\\s*)+", "\n\n");
            return s.Trim();
        }

        private static decimal ParseAmount(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            var digits = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
            // "1.234,56" / "1,234.56" / "12,99": the last separator is the decimal one when 1-2 digits follow it
            int last = Math.Max(digits.LastIndexOf('.'), digits.LastIndexOf(','));
            string whole = digits, frac = "";
            if (last >= 0 && digits.Length - last - 1 <= 2) { whole = digits.Substring(0, last); frac = digits.Substring(last + 1); }
            whole = whole.Replace(".", "").Replace(",", "");
            decimal d;
            return decimal.TryParse(whole + (frac.Length > 0 ? "." + frac : ""), NumberStyles.Number, CultureInfo.InvariantCulture, out d) ? d : -1;
        }
    }
}
