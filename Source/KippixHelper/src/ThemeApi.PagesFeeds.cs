using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>Pages fed from outside: the release calendar, game news and Steam friends.</summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // Release calendar: wishlisted games (and games in your library) that come out soon, by month
        // =====================================================================

        private void BuildReleasesPage()
        {
            PageSub = Loc("LOC_KX_PageReleasesSub", "When the games on your wishlist come out");
            AddWishViewChips("releases");   // the profile's Wishlist tab: Wishlist / Release calendar
            AddPageChip(Loc("LOC_KX_Refresh", "Refresh"), "refresh", () =>
            {
                foreach (var w in settings.StoreWishlist) w.ReleaseCheckedAt = DateTime.MinValue;
                SetPageStatus(Loc("LOC_KX_Checking", "Checking…"));
                SyncWishlistsSoon(true);
                RefreshWishReleases(() => { if (pageKey == "releases") FillReleases(); });
            });
            AddPageChip(ReleaseRangeText(), "calendar", () =>
            {
                var steps = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 24, 36 };
                int i = Array.IndexOf(steps, settings.ReleaseMonths);
                settings.ReleaseMonths = steps[(i + 1) % steps.Length];
                SaveSettings();
                PlaySound("move");
                var chip = PageChips.FirstOrDefault(c => c.Icon == "calendar");
                if (chip != null) { int at = PageChips.IndexOf(chip); var nc = new PickRow { Text = ReleaseRangeText(), Icon = "calendar", Key = chip.Key, Command = chip.Command }; PageChips[at] = nc; FocusPageSoon(nc); }
                FillReleases();
            });
            FillReleases();
            // dates older than a day are checked again (Steam games), then the page fills in again
            RefreshWishReleases(() => { if (pageKey == "releases" && openPanel == "page") FillReleases(); });
        }

        private void FillReleases()
        {
            var focus = FocusedPageItem();
            PageRows.Clear();
            var today = DateTime.Today;
            var until = today.AddMonths(Math.Max(1, settings.ReleaseMonths)).AddDays(1);
            var dated = new List<Tuple<DateTime, PageRow>>();
            var undated = new List<PageRow>();
            var recent = new List<Tuple<DateTime, PageRow>>();
            foreach (var w in settings.StoreWishlist.ToList())
            {
                var wish = w;
                var row = new PageRow
                {
                    Text = w.Name, ImagePath = w.Image, Wide = true, Key = "wish:" + (w.SteamAppId ?? w.GogId ?? w.ItadId ?? w.Name),
                    Command = new RelayCommand(_ => { PlaySound("select"); LeavePages(); OpenStoreFromWidget(WishToStoreItem(wish)); })
                };
                string from = w.ImportedFrom == "steam" ? Loc("LOC_KX_FromSteamWishlist", "Steam wishlist") : w.ImportedFrom == "gog" ? Loc("LOC_KX_FromGogWishlist", "GOG wishlist") : Loc("LOC_KX_FromStoreWishlist", "Your wishlist");
                if (w.ReleaseDate.HasValue && w.ReleaseDate.Value.Date >= today)
                {
                    if (w.ReleaseDate.Value.Date > until) continue;   // further ahead than the calendar shows
                    var d = w.ReleaseDate.Value.Date;
                    row.Sub = (string.IsNullOrEmpty(w.ReleaseText) ? d.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture) : w.ReleaseText) + "   ·   " + from;
                    row.Right = DaysText(d);
                    dated.Add(Tuple.Create(d, row));
                }
                else if (w.ReleaseDate.HasValue && w.ReleaseDate.Value.Date >= today.AddDays(-30) && !w.ComingSoon)
                {
                    row.Sub = string.Format(Loc("LOC_KX_OutSince", "Out since {0}"), w.ReleaseDate.Value.ToString("d MMMM", CultureInfo.CurrentCulture)) + "   ·   " + from;
                    row.Right = Loc("LOC_KX_OutNow", "Out now");
                    recent.Add(Tuple.Create(w.ReleaseDate.Value, row));
                }
                else if (w.ComingSoon)
                {
                    row.Sub = (string.IsNullOrEmpty(w.ReleaseText) ? Loc("LOC_KX_NoDateYet", "No date yet") : w.ReleaseText) + "   ·   " + from;
                    undated.Add(row);
                }
            }
            // games already in your library (pre-orders, early access entries) with a date still to come
            try
            {
                foreach (var g in api.Database.Games.Where(g => g != null && !g.Hidden && g.ReleaseDate.HasValue))
                {
                    var rd = g.ReleaseDate.Value;
                    DateTime d;
                    try { d = new DateTime(rd.Year, rd.Month ?? 1, rd.Day ?? 1); } catch (Exception) { continue; }
                    if (d < today || d > until || (!rd.Month.HasValue && d.Year == today.Year)) continue;
                    var id = g.Id;
                    var row = new PageRow
                    {
                        Text = g.Name, ImagePath = CoverOf(g.Id), Key = "game:" + g.Id,
                        Sub = (rd.Day.HasValue ? d.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture) : rd.Month.HasValue ? d.ToString("MMMM yyyy", CultureInfo.CurrentCulture) : d.Year.ToString()) + "   ·   " + Loc("LOC_KX_InYourLibrary", "In your library"),
                        Right = rd.Day.HasValue ? DaysText(d) : "",
                        Command = new RelayCommand(_ => OpenGameFromPage(id))
                    };
                    dated.Add(Tuple.Create(d, row));
                }
            }
            catch (Exception) { }

            if (recent.Count > 0)
            {
                AddPageHeader(Loc("LOC_KX_JustOut", "JUST OUT"));
                foreach (var r in recent.OrderByDescending(r => r.Item1)) PageRows.Add(r.Item2);
            }
            string month = null;
            foreach (var r in dated.OrderBy(r => r.Item1).ThenBy(r => r.Item2.Text))
            {
                string m = r.Item1.ToString("MMMM yyyy", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
                if (m != month) { AddPageHeader(m); month = m; }
                PageRows.Add(r.Item2);
            }
            if (undated.Count > 0)
            {
                AddPageHeader(Loc("LOC_KX_SecNoDate", "NO DATE YET"));
                foreach (var r in undated.OrderBy(r => r.Text)) PageRows.Add(r);
            }
            int thisMonth = dated.Count(r => r.Item1.Year == today.Year && r.Item1.Month == today.Month);
            PageStats = new List<PageStat>
            {
                new PageStat { Value = thisMonth.ToString(), Label = Loc("LOC_KX_ThisMonth", "this month") },
                new PageStat { Value = dated.Count(r => r.Item1 <= today.AddDays(90)).ToString(), Label = Loc("LOC_KX_Next3Months", "in the next 3 months") },
                new PageStat { Value = (dated.Count + undated.Count).ToString(), Label = Loc("LOC_KX_ComingInAll", "coming in all") }
            };
            string status = PageRows.Count == 0
                ? Loc("LOC_KX_ReleasesEmpty2", "Nothing coming up. Wishlist games in the Store (the heart on a game's page), or bring in your Steam / GOG wishlist: Settings & Customisation › Store settings › Wishlists.")
                : "";
            if (wishImportProblem && !string.IsNullOrEmpty(WishImportText)) status = (status.Length > 0 ? status + "\n\n" : "") + WishImportText;
            SetPageStatus(status);
            NotifyPage();
            NotifyPageLists();
            LoadPageImages();
            if (focus != null) FocusPageSoon(focus);
        }

        private string ReleaseRangeText()
        {
            int m = settings.ReleaseMonths;
            if (m >= 24) return string.Format(Loc("LOC_KX_ReleaseYears", "Showing: the next {0} years"), m / 12);
            return m == 1 ? Loc("LOC_KX_ReleaseOneMonth", "Showing: the next month") : string.Format(Loc("LOC_KX_ReleaseMonths", "Showing: the next {0} months"), m);
        }

        private string DaysText(DateTime d)
        {
            int days = (int)(d.Date - DateTime.Today).TotalDays;
            if (days <= 0) return Loc("LOC_KX_Today", "Today");
            if (days == 1) return Loc("LOC_KX_Tomorrow", "Tomorrow");
            return string.Format(Loc("LOC_KX_InDays", "In {0} days"), days);
        }

        // =====================================================================
        // Game news: Steam's news for the Steam games you've played lately
        // =====================================================================

        private class NewsPost { public string AppId, Game, Title, Url, Summary, Label; public DateTime When; public bool Official; }
        private List<NewsPost> newsCache;
        private DateTime newsAt = DateTime.MinValue;
        private bool newsAll;

        private void BuildNewsPage()
        {
            PageSub = Loc("LOC_KX_PageNewsSub", "Updates and patch notes for the Steam games you play");
            var official = AddPageChip(Loc("LOC_KX_NewsOfficial", "From the developers"), "news", () => { newsAll = false; FillNews(); });
            var all = AddPageChip(Loc("LOC_KX_NewsAll", "Everything"), "globe", () => { newsAll = true; FillNews(); });
            official.IsSelected = !newsAll;
            all.IsSelected = newsAll;
            if (newsCache != null && (DateTime.Now - newsAt).TotalMinutes < 30) { FillNews(); return; }
            SetPageStatus(Loc("LOC_KX_NewsLoading", "Getting the latest news…"));
            List<Game> games;
            try
            {
                games = api.Database.Games.Where(g => g != null && g.PluginId == SteamPluginId && !string.IsNullOrEmpty(g.GameId) && (g.LastActivity.HasValue || g.IsInstalled))
                                          .OrderByDescending(g => g.LastActivity ?? DateTime.MinValue).Take(16).ToList();
            }
            catch (Exception) { games = new List<Game>(); }
            var list = games.Select(g => Tuple.Create(g.GameId, g.Name)).ToList();
            int gen = pageGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                var posts = new List<NewsPost>();
                foreach (var g in list)
                {
                    try
                    {
                        var root = StoreJson("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?count=4&maxlength=260&format=json&appid=" + Uri.EscapeDataString(g.Item1)) as Dictionary<string, object>;
                        var items = Dig(root, "appnews", "newsitems") as IEnumerable;
                        if (items == null) continue;
                        foreach (var it in items.Cast<object>().OfType<Dictionary<string, object>>())
                        {
                            long when = FNum(it, "date");
                            posts.Add(new NewsPost
                            {
                                AppId = g.Item1, Game = g.Item2, Title = FStr(it, "title"), Url = FStr(it, "url"), Label = FStr(it, "feedlabel"),
                                Summary = CleanNews(FStr(it, "contents")), When = when > 0 ? UnixDate(when) : DateTime.MinValue,
                                Official = FNum(it, "feed_type") == 1 || FStr(it, "feedname") == "steam_community_announcements"
                            });
                        }
                    }
                    catch (Exception) { }
                }
                OnUi(() =>
                {
                    newsCache = posts;
                    newsAt = DateTime.Now;
                    if (gen == pageGen && pageKey == "news") FillNews();
                });
            });
        }

        private static string CleanNews(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\[/?[a-zA-Z0-9]+(=[^\]]*)?\]", " ");     // [b], [url=…], [img]…
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = Regex.Replace(s, @"\{STEAM_CLAN_IMAGE\}\S*", " ");
            s = Regex.Replace(s, @"https?://\S+", " ");
            s = System.Net.WebUtility.HtmlDecode(s);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private void FillNews()
        {
            foreach (var c in PageChips) c.IsSelected = (c.Icon == "globe") == newsAll;
            PageRows.Clear();
            var posts = (newsCache ?? new List<NewsPost>()).Where(p => newsAll || p.Official).OrderByDescending(p => p.When).Take(60).ToList();
            foreach (var p in posts)
            {
                string url = p.Url;
                PageRows.Add(new PageRow
                {
                    Text = p.Title, ImagePath = SteamHeader(p.AppId), Wide = true, Key = url,
                    Sub = p.Game + "   ·   " + (p.When > DateTime.MinValue ? AgoText(p.When) : "") + (p.Summary.Length > 0 ? "\n" + p.Summary : ""),
                    Right = p.Official ? "" : p.Label,
                    Command = new RelayCommand(_ => { if (!string.IsNullOrEmpty(url)) OpenLink(url); })
                });
            }
            SetPageStatus(posts.Count == 0
                ? (newsCache == null ? Loc("LOC_KX_NewsLoading", "Getting the latest news…") : Loc("LOC_KX_NewsEmpty", "No news for the Steam games you've played lately. (Only Steam publishes news this way.)"))
                : "");
            NotifyPage();
            NotifyPageLists();
            LoadPageImages();
        }

        // =====================================================================
        // Friends: who's playing what (Steam), and each friend's recent games
        // =====================================================================

        private void BuildFriendsPage()
        {
            PageSub = Loc("LOC_KX_PageFriendsSub", "What your Steam friends are playing");
            string key = SteamKey, me = MySteamId();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(me))
            {
                AddPageChip(Loc("LOC_KX_SetSteamKey", "Add your Steam Web API key"), "lock", () => { AskSteamKey(() => { if (pageKey == "friends") OpenPage("friends", "", false); }); });
                SetPageStatus(string.IsNullOrEmpty(me)
                    ? Loc("LOC_KX_FriendsNoAccount", "Your Steam account isn't known yet: set up Playnite's Steam library add-on first.")
                    : Loc("LOC_KX_FriendsNoKey", "Friends need your own Steam Web API key (free, from steamcommunity.com/dev/apikey). It's kept encrypted for your Windows account."));
                return;
            }
            AddPageChip(Loc("LOC_KX_Refresh", "Refresh"), "refresh", () => { lock (friendLock) friendInfoAt = DateTime.MinValue; RefreshFriendsAsync(key, me); SetPageStatus(Loc("LOC_KX_Checking", "Checking…")); });
            FillFriends();
            RefreshFriendsAsync(key, me);   // fills in again when Steam answers (RebuildFriendsIfOpen)
        }

        private void FillFriends()
        {
            if (pageKey != "friends") return;
            var focus = FocusedPageItem();
            Dictionary<string, FriendInfo> info;
            lock (friendLock) info = new Dictionary<string, FriendInfo>(friendInfo);
            PageRows.Clear();
            var owned = OwnedSteamGames();
            Func<KeyValuePair<string, FriendInfo>, PageRow> make = kv =>
            {
                var f = kv.Value;
                string id = kv.Key, name = f.Name;
                bool playing = !string.IsNullOrEmpty(f.PlayingName) || !string.IsNullOrEmpty(f.PlayingAppId);
                return new PageRow
                {
                    Text = f.Name, ImagePath = f.Avatar, Key = id,
                    Sub = playing ? string.Format(Loc("LOC_KX_PlayingX", "Playing {0}"), string.IsNullOrEmpty(f.PlayingName) ? "…" : f.PlayingName) : FriendStateText(f.State),
                    Right = playing && !string.IsNullOrEmpty(f.PlayingAppId) && owned.ContainsKey(f.PlayingAppId) ? Loc("LOC_KX_InYourLibrary", "In your library") : "",
                    Command = new RelayCommand(_ => OpenPage("friend", id, true))
                };
            };
            var all = info.OrderBy(kv => kv.Value.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var playingNow = all.Where(kv => !string.IsNullOrEmpty(kv.Value.PlayingName) || !string.IsNullOrEmpty(kv.Value.PlayingAppId)).ToList();
            var online = all.Where(kv => kv.Value.State > 0 && !playingNow.Contains(kv)).ToList();
            var offline = all.Where(kv => kv.Value.State == 0 && !playingNow.Contains(kv)).ToList();
            if (playingNow.Count > 0) { AddPageHeader(Loc("LOC_KX_PlayingNow", "PLAYING NOW")); foreach (var kv in playingNow) PageRows.Add(make(kv)); }
            if (online.Count > 0) { AddPageHeader(Loc("LOC_KX_OnlineHeader", "ONLINE")); foreach (var kv in online) PageRows.Add(make(kv)); }
            if (offline.Count > 0) { AddPageHeader(Loc("LOC_KX_OfflineHeader", "OFFLINE")); foreach (var kv in offline) PageRows.Add(make(kv)); }
            PageStats = info.Count == 0 ? new List<PageStat>() : new List<PageStat>
            {
                new PageStat { Value = playingNow.Count.ToString(), Label = Loc("LOC_KX_StatPlaying", "playing") },
                new PageStat { Value = (online.Count + playingNow.Count).ToString(), Label = Loc("LOC_KX_StatOnline", "online") },
                new PageStat { Value = info.Count.ToString(), Label = Loc("LOC_KX_StatFriends", "friends") }
            };
            SetPageStatus(info.Count == 0 ? (friendsError.Length > 0 ? friendsError : Loc("LOC_KX_FriendsLoading", "Looking for your Steam friends…")) : "");
            NotifyPage();
            NotifyPageLists();
            LoadPageImages();
            if (focus != null) FocusPageSoon(focus);
        }

        private string FriendStateText(int state)
        {
            switch (state)
            {
                case 0: return Loc("LOC_KX_Offline", "Offline");
                case 2: return Loc("LOC_KX_Busy", "Busy");
                case 3: case 4: return Loc("LOC_KX_Away", "Away");
            }
            return Loc("LOC_KX_Online", "Online");
        }

        private void BuildFriendPage(string id)
        {
            FriendInfo f;
            lock (friendLock) friendInfo.TryGetValue(id, out f);
            PageTitle = f != null ? f.Name : PageName("friends");
            PageSub = f != null ? (!string.IsNullOrEmpty(f.PlayingName) ? string.Format(Loc("LOC_KX_PlayingX", "Playing {0}"), f.PlayingName) : FriendStateText(f.State)) : "";
            var owned = OwnedSteamGames();
            if (f != null && !string.IsNullOrEmpty(f.PlayingAppId))
            {
                AddPageHeader(Loc("LOC_KX_PlayingNow", "PLAYING NOW"));
                PageRows.Add(FriendGameRow(f.PlayingAppId, f.PlayingName, "", owned));
            }
            // games you both have, most played by them first (from the friends' libraries the game page already uses)
            Dictionary<string, long[]> lib = null;
            lock (friendLock) if (friendLibraries != null) friendLibraries.TryGetValue(id, out lib);
            if (lib != null && lib.Count > 0)
            {
                var both = lib.Where(kv => owned.ContainsKey(kv.Key)).OrderByDescending(kv => kv.Value[0]).Take(12).ToList();
                if (both.Count > 0)
                {
                    AddPageHeader(Loc("LOC_KX_YouBothHave", "YOU BOTH HAVE"));
                    foreach (var kv in both)
                    {
                        var g = owned[kv.Key];
                        string sub = string.Format(Loc("LOC_KX_TheyPlayed", "They've played {0}"), HoursShort(kv.Value[0] / 60.0))
                                   + "   ·   " + string.Format(Loc("LOC_KX_YouPlayed", "you {0}"), HoursShort(g.Playtime / 3600.0));
                        PageRows.Add(FriendGameRow(kv.Key, g.Name, sub, owned));
                    }
                    // Compare with a friend: achievements side by side
                    AddPageHeader(Loc("LOC_KX_CompareHeader", "COMPARE ACHIEVEMENTS"));
                    foreach (var kv in both.Take(6))
                    {
                        string app = kv.Key, friendId = id;
                        PageRows.Add(new PageRow
                        {
                            Text = owned[kv.Key].Name, Icon = "trophy", Sub = Loc("LOC_KX_CompareRowSub", "You and them, side by side"), Key = "cmp:" + app,
                            Command = new RelayCommand(_ => OpenPage("compare", friendId + "|" + app, true))
                        });
                    }
                }
            }
            SetPageStatus(Loc("LOC_KX_FriendRecentLoading", "Getting their recent games…"));
            string key = SteamKey;
            int gen = pageGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                var recent = new List<Tuple<string, string, long, long>>();
                bool ok = false;
                try
                {
                    var root = StoreJson("https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?count=12&key=" + Uri.EscapeDataString(key ?? "") + "&steamid=" + Uri.EscapeDataString(id)) as Dictionary<string, object>;
                    var games = Dig(root, "response", "games") as IEnumerable;
                    ok = Dig(root, "response") != null;
                    if (games != null)
                        foreach (var g in games.Cast<object>().OfType<Dictionary<string, object>>())
                            recent.Add(Tuple.Create(FStr(g, "appid"), FStr(g, "name"), FNum(g, "playtime_2weeks"), FNum(g, "playtime_forever")));
                }
                catch (Exception) { }
                OnUi(() =>
                {
                    if (gen != pageGen) return;
                    var focus = FocusedPageItem();
                    if (recent.Count > 0)
                    {
                        int at = PageRows.Count > 0 && PageRows[0].Text == Loc("LOC_KX_PlayingNow", "PLAYING NOW") ? 2 : 0;
                        PageRows.Insert(at++, new PageRow { IsHeader = true, Text = Loc("LOC_KX_SecLastTwoWeeks", "LAST TWO WEEKS") });
                        foreach (var r in recent)
                        {
                            string sub = string.Format(Loc("LOC_KX_TwoWeeksTime", "{0} in the last two weeks"), HoursShort(r.Item3 / 60.0))
                                       + "   ·   " + string.Format(Loc("LOC_KX_TotalTime", "{0} in all"), HoursShort(r.Item4 / 60.0));
                            PageRows.Insert(at++, FriendGameRow(r.Item1, r.Item2, sub, owned));
                        }
                    }
                    SetPageStatus(PageRows.Count == 0 ? (ok ? Loc("LOC_KX_FriendNothing", "They haven't played anything lately (or their game details are private).")
                                                             : Loc("LOC_KX_FriendPrivate", "Steam didn't share their games (their game details may be private)."))
                                                      : "");
                    NotifyPageLists();
                    LoadPageImages();
                    if (focus == null) FocusPageSoon(null);
                });
            });
            LoadPageImages();
        }

        private PageRow FriendGameRow(string appId, string name, string sub, Dictionary<string, Game> owned)
        {
            Game mine;
            owned.TryGetValue(appId ?? "", out mine);
            var id = mine != null ? mine.Id : Guid.Empty;
            return new PageRow
            {
                Text = name, Sub = sub, ImagePath = SteamHeader(appId), Wide = true, Key = appId,
                Right = mine != null ? Loc("LOC_KX_InYourLibrary", "In your library") : Loc("LOC_KX_SeeInStore", "See in the Store"),
                Command = new RelayCommand(_ =>
                {
                    if (id != Guid.Empty) OpenGameFromPage(id);
                    else if (!string.IsNullOrEmpty(appId)) { PlaySound("select"); LeavePages(); OpenSteamAppInStore(appId, name); }
                })
            };
        }
    }
}
