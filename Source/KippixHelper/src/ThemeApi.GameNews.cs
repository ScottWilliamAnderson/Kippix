using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>One post on the game page's News card (Steam news / patch notes).</summary>
    public class GameNewsPost
    {
        public string Title { get; set; }
        public string Meta { get; set; }   // its date and where it's from
        public string Text { get; set; }   // a short bit of it
        public string Url { get; set; }
        public ICommand Command { get; set; }
    }

    /// <summary>
    /// (r12) The game page's News card (the game's latest Steam news and patch notes, from Steam's public news list: no key) and
    /// the play info's "1,234 playing now" (Steam's public player count). Both only for Steam games, or games with a link to their
    /// Steam page. Looked up when the game page opens (or moves to another game), never in the background: news kept 1 hour per
    /// game, the player count 5 minutes. Also where the game page's r12 extras (Mods card, feature badges) start from.
    /// </summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // Where it starts: the game page opened, or moved to another game
        // =====================================================================

        /// <summary>From UpdateGamePageMore: the News and Mods cards and the play info's extras for this game (null: page closed).</summary>
        private void FillGameExtras(Game g, int gen)
        {
            SetNewsButtonGame(g);   // (r13) the News button: Steam games only
            FillGameNews(g, gen);
            FillGameMods(g, gen);
            FillInfoExtras(g);
        }

        /// <summary>The play info's players-online line and feature badges (also when their switch is turned on).</summary>
        private void FillInfoExtras(Game g)
        {
            FillPlayers(g);
            FillFeatureBadges(g);
        }

        private static readonly Regex SteamAppLinkRx = new Regex(@"(?:store\.steampowered\.com|steamcommunity\.com)/app/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The game's Steam app id: a Steam game's own, else from a link to its Steam store / community page. Null: none.</summary>
        private string SteamAppIdOf(Game g)
        {
            if (g == null) return null;
            try
            {
                if (g.PluginId == SteamPluginId && !string.IsNullOrEmpty(g.GameId) && g.GameId.All(char.IsDigit)) return g.GameId;
                if (g.Links != null)
                    foreach (var l in g.Links)
                    {
                        if (l == null || string.IsNullOrEmpty(l.Url)) continue;
                        var m = SteamAppLinkRx.Match(l.Url);
                        if (m.Success) return m.Groups[1].Value;
                    }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Still the game the page is showing (a lookup that comes back late for another game is dropped).</summary>
        private bool StillShowing(Guid id)
        {
            if (!detailsOpen) return false;
            var now = SelectedGameNow();
            return now != null && now.Id == id;
        }

        // =====================================================================
        // News
        // =====================================================================

        private class GameNewsCacheEntry { public DateTime At; public List<GameNewsPost> Posts; }
        private static readonly Dictionary<string, GameNewsCacheEntry> gameNewsCache = new Dictionary<string, GameNewsCacheEntry>();
        private ObservableCollection<GameNewsPost> detNews;
        public ObservableCollection<GameNewsPost> DetNews { get { return detNews ?? (detNews = new ObservableCollection<GameNewsPost>()); } }
        private string detNewsAppId;
        private int gameNewsGen;
        public bool ShowDetNews { get { return settings.DetSecNews && detNewsAppId != null && DetNews.Count > 0; } }
        public bool DetIntNews { get { return DetInteractive("news") && !DetBlank("news"); } }
        public string DetNewsSub { get { return DetNews.Count > 0 ? Loc("LOC_KX_NewsFromSteam", "From Steam") : ""; } }
        private ICommand openAllNewsCommand;
        public ICommand OpenAllNewsCommand
        {
            get
            {
                return openAllNewsCommand ?? (openAllNewsCommand = new RelayCommand(_ =>
                {
                    if (string.IsNullOrEmpty(detNewsAppId)) return;
                    PlaySound("select");
                    OpenLink("https://store.steampowered.com/news/app/" + detNewsAppId);
                }));
            }
        }

        private void NotifyNews()
        {
            foreach (string n in new[] { "ShowDetNews", "DetNewsSub", "DetIntNews", "ShowDetMore", "DetHintMoreBelow" }) Notify(n);
        }

        private void FillGameNews(Game g, int gen)
        {
            int my = ++gameNewsGen;
            DetNews.Clear();
            detNewsAppId = null;
            string appId = g != null && settings.DetSecNews ? SteamAppIdOf(g) : null;
            if (appId == null) { NotifyNews(); return; }
            detNewsAppId = appId;
            GameNewsCacheEntry hit;
            lock (gameNewsCache) gameNewsCache.TryGetValue(appId, out hit);
            if (hit != null && (DateTime.UtcNow - hit.At).TotalMinutes < 60)
            {
                foreach (var p in hit.Posts.Take(DetNewsCardCount)) DetNews.Add(p);
                NotifyNews();
                return;
            }
            NotifyNews();
            var id = g.Id;
            KippixShared.SafeWork.Queue(_ =>
            {
                List<GameNewsPost> posts = null;
                try { posts = FetchSteamNews(appId); }
                catch (Exception ex) { logger.Info("Kippix: Steam news for " + appId + " couldn't be read: " + ex.Message); }
                if (posts == null) return;
                lock (gameNewsCache) gameNewsCache[appId] = new GameNewsCacheEntry { At = DateTime.UtcNow, Posts = posts };
                OnUi(() =>
                {
                    if (my != gameNewsGen || !StillShowing(id) || detNewsAppId != appId) return;
                    DetNews.Clear();
                    foreach (var p in posts.Take(DetNewsCardCount)) DetNews.Add(p);
                    NotifyNews();
                    ApplyDetCardLooks();   // (its Background setting, now that it's there)
                });
            });
        }

        /// <summary>Steam's latest news posts for the game (the public ISteamNews list: no key needed).</summary>
        private List<GameNewsPost> FetchSteamNews(string appId)
        {
            var root = StoreJson("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=" + Uri.EscapeDataString(appId) + "&count=" + GameNewsFetchCount + "&maxlength=300&format=json");
            var items = Dig(root, "appnews", "newsitems") as IEnumerable;
            var r = new List<GameNewsPost>();
            if (items == null) return r;
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            foreach (var d in items.Cast<object>().OfType<Dictionary<string, object>>())
            {
                string title = NewsClean(FStr(d, "title"), 140);
                string url = FStr(d, "url");
                if (title.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                long secs = FNum(d, "date");
                string when = secs > 0 ? epoch.AddSeconds(secs).ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) : "";
                string feed = NewsClean(FStr(d, "feedlabel"), 60);
                string link = url;
                r.Add(new GameNewsPost
                {
                    Title = title,
                    Meta = feed.Length > 0 && when.Length > 0 ? when + "  ·  " + feed : when + feed,
                    Text = NewsClean(FStr(d, "contents"), 260),
                    Url = link,
                    Command = new RelayCommand(_ => { PlaySound("select"); OpenLink(link); })
                });
            }
            return r;
        }

        /// <summary>A post's text as plain words: Steam's BBCode, picture markers, HTML and bare links taken out, cut to <paramref name="max"/>.</summary>
        private static string NewsClean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\{STEAM_CLAN_IMAGE\}\S*", " ");
            s = Regex.Replace(s, @"\[/?[a-zA-Z0-9\*]+(?:=[^\]]*)?\]", " ");
            s = HtmlText(s);
            s = Regex.Replace(s, @"https?://\S+", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > max) s = s.Substring(0, max - 1).TrimEnd() + "…";
            return s;
        }

        // =====================================================================
        // (r13) The News button (right of Achievements) and its page: the game's posts, A opens one in the browser
        // =====================================================================

        private const int DetNewsCardCount = 8;      // the card shows the latest 8
        private const int GameNewsFetchCount = 20;   // (one lookup serves the card and the page)
        private string newsButtonAppId;
        private string newsButtonGame = "";
        private ICommand openGameNewsCommand;

        /// <summary>Game page › Buttons and extras › News button (on at first).</summary>
        public bool DetNewsButton
        {
            get { return settings.DetNewsButton; }
            set { if (SetSetting(ref settings.DetNewsButton, value)) { Notify("DetNewsButton"); Notify("ShowDetNewsButton"); } }
        }

        /// <summary>The button shows for Steam games (or games with a link to their Steam page) while it's switched on.</summary>
        public bool ShowDetNewsButton { get { return settings.DetNewsButton && newsButtonAppId != null; } }

        private void SetNewsButtonGame(Game g)
        {
            string appId = g != null ? SteamAppIdOf(g) : null;
            string name = g != null ? (g.Name ?? "") : "";
            if (appId == newsButtonAppId && name == newsButtonGame) return;
            newsButtonAppId = appId;
            newsButtonGame = name;
            Notify("ShowDetNewsButton");
        }

        public ICommand OpenGameNewsCommand
        {
            get
            {
                return openGameNewsCommand ?? (openGameNewsCommand = new RelayCommand(_ =>
                {
                    if (string.IsNullOrEmpty(newsButtonAppId)) return;
                    pageStack.Clear();
                    profileReturnKey = null;   // B: straight back to the game page, on this button
                    OpenPage("gamenews", newsButtonAppId + "|" + newsButtonGame, false);
                }));
            }
        }

        /// <summary>The game's news page (from the game page's News button): its latest Steam posts, newest first.</summary>
        private void BuildGameNewsPage(string arg)
        {
            int bar = (arg ?? "").IndexOf('|');
            string appId = bar > 0 ? arg.Substring(0, bar) : arg;
            string name = bar > 0 ? arg.Substring(bar + 1) : "";
            if (string.IsNullOrEmpty(appId)) return;
            PageTitle = name.Length > 0 ? string.Format(Loc("LOC_KX_GameNewsTitle", "{0}: news"), name) : Loc("LOC_KX_PageNews", "Game news");
            PageSub = Loc("LOC_KX_GameNewsSub", "Updates and patch notes from Steam");
            string all = "https://store.steampowered.com/news/app/" + appId;
            AddPageChip(Loc("LOC_KX_GameNewsAll", "All of its news on Steam"), "globe", () => { PlaySound("select"); OpenLink(all); });
            GameNewsCacheEntry hit;
            lock (gameNewsCache) gameNewsCache.TryGetValue(appId, out hit);
            if (hit != null && (DateTime.UtcNow - hit.At).TotalMinutes < 60) { FillGameNewsPage(hit.Posts); return; }
            SetPageStatus(Loc("LOC_KX_NewsLoading", "Getting the latest news…"));
            int gen = pageGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                List<GameNewsPost> posts = null;
                try { posts = FetchSteamNews(appId); }
                catch (Exception ex) { logger.Info("Kippix: Steam news for " + appId + " couldn't be read: " + ex.Message); }
                if (posts != null) lock (gameNewsCache) gameNewsCache[appId] = new GameNewsCacheEntry { At = DateTime.UtcNow, Posts = posts };
                OnUi(() =>
                {
                    if (gen != pageGen || openPanel != "page" || pageKey != "gamenews") return;
                    if (posts == null) { SetPageStatus(Loc("LOC_KX_GameNewsFailed", "Steam's news couldn't be reached just now. Try again in a moment.")); NotifyPage(); return; }
                    FillGameNewsPage(posts);
                });
            });
        }

        private void FillGameNewsPage(List<GameNewsPost> posts)
        {
            PageRows.Clear();
            foreach (var p in posts)
            {
                var post = p;
                PageRows.Add(new PageRow
                {
                    Text = post.Title, Icon = "news", Key = post.Url,
                    Sub = post.Meta + (string.IsNullOrEmpty(post.Text) ? "" : (post.Meta.Length > 0 ? "\n" : "") + post.Text),
                    Command = new RelayCommand(_ => { if (!string.IsNullOrEmpty(post.Url)) { PlaySound("select"); OpenLink(post.Url); } })
                });
            }
            SetPageStatus(posts.Count == 0 ? Loc("LOC_KX_GameNewsEmpty", "No news for this game on Steam yet.") : "");
            NotifyPage();
            NotifyPageLists();
            FocusFirstNewsPost();
        }

        /// <summary>The highlight on the newest post (after the page's own first focus, which lands on the chip above), unless you've moved already.</summary>
        private void FocusFirstNewsPost()
        {
            int gen = pageGen;
            ui.BeginInvoke(new Action(() =>
            {
                if (gen != pageGen || openPanel != "page" || pageKey != "gamenews" || PageRows.Count == 0) return;
                var fe = Keyboard.FocusedElement as System.Windows.FrameworkElement;
                if (fe != null && fe.DataContext is PageRow) return;   // (already on a post)
                UiTools.FocusItem("KX_PageRowList", PageRows[0]);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // =====================================================================
        // Players online now
        // =====================================================================

        private class PlayersCacheEntry { public DateTime At; public long Count; }
        private static readonly Dictionary<string, PlayersCacheEntry> playersCache = new Dictionary<string, PlayersCacheEntry>();
        private string detPlayersText = "";
        private int playersGen;
        public string DetPlayersText { get { return detPlayersText; } }
        public bool DetInfoShowPlayers { get { return DetInfoOn("players") && !string.IsNullOrEmpty(detPlayersText); } }

        private void SetPlayers(long count)
        {
            detPlayersText = count > 0 ? string.Format(Loc("LOC_KX_PlayersNow", "{0} playing now"), count.ToString("N0", CultureInfo.CurrentCulture)) : "";
            Notify("DetPlayersText");
            Notify("DetInfoShowPlayers");
        }

        private void FillPlayers(Game g)
        {
            int my = ++playersGen;
            string appId = g != null && detailsOpen && DetInfoOn("players") ? SteamAppIdOf(g) : null;
            if (appId == null) { SetPlayers(0); return; }
            PlayersCacheEntry hit;
            lock (playersCache) playersCache.TryGetValue(appId, out hit);
            if (hit != null && (DateTime.UtcNow - hit.At).TotalMinutes < 5) { SetPlayers(hit.Count); return; }
            SetPlayers(hit != null ? hit.Count : 0);   // (the last count while the new one comes)
            var id = g.Id;
            KippixShared.SafeWork.Queue(_ =>
            {
                long count = -1;
                try
                {
                    var root = StoreJson("https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid=" + Uri.EscapeDataString(appId)) as Dictionary<string, object>;
                    var resp = Dig(root, "response") as Dictionary<string, object>;
                    if (resp != null) count = FNum(resp, "result") == 1 ? FNum(resp, "player_count") : 0;
                }
                catch (Exception ex) { logger.Info("Kippix: players online for " + appId + " couldn't be read: " + ex.Message); }
                if (count < 0) return;
                lock (playersCache) playersCache[appId] = new PlayersCacheEntry { At = DateTime.UtcNow, Count = count };
                OnUi(() => { if (my == playersGen && StillShowing(id)) SetPlayers(count); });
            });
        }
    }
}
