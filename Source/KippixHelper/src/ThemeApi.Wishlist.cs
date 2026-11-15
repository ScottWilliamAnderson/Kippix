using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Your Steam and GOG wishlists brought into the Store's wishlist (Store settings › Import wishlists). It syncs when the Store opens
    /// (at most every 15 minutes, nothing in the background). Games taken off here aren't brought back. Nothing is changed on Steam or GOG, except
    /// that with "Add to my Steam wishlist too" on, a game wishlisted here is added on Steam.
    /// Also when wishlisted games come out (for the releases calendar).
    /// </summary>
    public partial class ThemeApi
    {
        private volatile bool wishSyncBusy;
        private bool wishImportProblem;

        public bool ImportSteamWishlist
        {
            get { return settings.ImportSteamWishlist; }
            set { if (settings.ImportSteamWishlist == value) return; settings.ImportSteamWishlist = value; settings.WishImportAt = DateTime.MinValue; SaveSettings(); Notify("ImportSteamWishlist"); if (value) SyncWishlistsSoon(true); else DropImported("steam"); }
        }
        public bool ImportGogWishlist
        {
            get { return settings.ImportGogWishlist; }
            set { if (settings.ImportGogWishlist == value) return; settings.ImportGogWishlist = value; settings.WishImportAt = DateTime.MinValue; SaveSettings(); Notify("ImportGogWishlist"); if (value) SyncWishlistsSoon(true); else DropImported("gog"); }
        }
        public string WishImportText { get; private set; }

        public bool SyncWishToSteam
        {
            get { return settings.SyncWishToSteam; }
            set { if (settings.SyncWishToSteam == value) return; settings.SyncWishToSteam = value; SaveSettings(); Notify("SyncWishToSteam"); if (value) SendWishlistToSteam(true); }
        }

        /// <summary>
        /// Wishlisted here: onto your Steam wishlist too, when that's switched on and it's a Steam game. Uses the Steam sign-in
        /// Playnite keeps (Playnite's Steam add-on, signed in through its web page); nothing happens without it. Only adds.
        /// </summary>
        private void WishedHere(string steamAppId, string name)
        {
            if (!settings.SyncWishToSteam) return;
            if (!string.IsNullOrEmpty(steamAppId) && settings.WishRemoveFromSteam != null && settings.WishRemoveFromSteam.Remove(steamAppId)) SaveSettings();   // (back on it before it came off Steam's)
            if (string.IsNullOrEmpty(steamAppId)) { logger.Info("Kippix: Steam wishlist: " + name + " has no Steam app id (not sent)"); return; }
            SendWishlistToSteam(false);   // this one, and any that didn't get there before
        }

        /// <summary>
        /// Puts games on your Steam wishlist from inside Steam's own store page (an off-screen browser with Playnite's sign-ins),
        /// the way the store's own button does: the page keeps its sign-in fresh and knows its session, which a request made
        /// from outside didn't (Steam answered "not signed in" to those). Per game: "ok", "signin", "rejected" (Steam said no:
        /// owned, or not sold) or "failed". Runs on a worker thread.
        /// </summary>
        private Dictionary<string, string> SteamWishlistAddAll(List<string> appIds) { return SteamWishlistCall(appIds, "addtowishlist"); }

        /// <param name="endpoint">"addtowishlist" or "removefromwishlist" (Steam's own store buttons use these)</param>
        private Dictionary<string, string> SteamWishlistCall(List<string> appIds, string endpoint)
        {
            var results = new Dictionary<string, string>();
            using (var view = api.WebViews.CreateOffscreenView())
            {
                view.NavigateAndWait("https://store.steampowered.com/account/preferences/");   // (an app page can be an age check; this one needs you signed in)
                foreach (var appId in appIds)
                {
                    string r = "failed";
                    try
                    {
                        // signed in: any of the signs Steam's pages have had (g_AccountID went missing from newer pages,
                        // which made every try look signed out); the session from the page or its sessionid cookie
                        string js = "(function(){try{var u=null;try{var c=document.getElementById('application_config');"
                                  + "if(c&&c.getAttribute('data-userinfo'))u=JSON.parse(c.getAttribute('data-userinfo'));}catch(e){}"
                                  + "var inn=!!((u&&u.logged_in)||(typeof g_AccountID!=='undefined'&&g_AccountID)||(typeof g_steamID==='string'&&g_steamID)||document.getElementById('account_pulldown'));"
                                  + "var sid=(typeof g_sessionID==='string'&&g_sessionID)?g_sessionID:((document.cookie.match(/(?:^|; )sessionid=([^;]+)/)||[])[1]||'');"
                                  + "if(!sid)return (inn?'nosession|':'signin|')+location.pathname;"
                                  + "var x=new XMLHttpRequest();x.open('POST','/api/" + endpoint + "',false);"
                                  + "x.setRequestHeader('Content-Type','application/x-www-form-urlencoded; charset=UTF-8');"
                                  + "x.send('sessionid='+encodeURIComponent(sid)+'&appid=" + Uri.EscapeDataString(appId) + "');"
                                  + "var t=x.responseText||'';"
                                  + "if(!inn&&t.indexOf('\"success\":true')<0&&t.indexOf('\"success\":1')<0)return 'signin|'+x.status+'|'+t;"
                                  + "return x.status+'|'+t;}catch(e){return 'error|'+e;}})()";
                        var task = view.EvaluateScriptAsync(js);
                        string said = null;
                        if (task.Wait(20000) && task.Result != null && task.Result.Success && task.Result.Result != null) said = task.Result.Result.ToString();
                        if (said == null) r = "failed";
                        else if (said.StartsWith("signin|")) r = "signin";
                        else if (said.Contains("\"success\":true") || said.Contains("\"success\":1")) r = "ok";
                        else if (said.Contains("\"success\":false")) r = "rejected";
                        logger.Info("Kippix: Steam wishlist" + (endpoint == "addtowishlist" ? "" : " (" + endpoint + ")") + ": app " + appId + ": " + r + (r == "ok" ? "" : " (" + (said == null ? "no answer" : (said.Length > 160 ? said.Substring(0, 160) : said)) + ")"));
                    }
                    catch (Exception ex) { logger.Warn("Kippix: Steam wishlist: " + ex.Message); }
                    results[appId] = r;
                    if (r == "signin") break;
                    Thread.Sleep(600);   // gently: one at a time
                }
            }
            return results;
        }

        private System.Windows.Input.ICommand steamSignInCmd;
        /// <summary>
        /// Store settings › Sign in to the Steam store: Steam's login page in the theme's browser (the same browser sign-ins
        /// the wishlist uses). Closes itself once you're signed in, then the wishlist catches up.
        /// </summary>
        public System.Windows.Input.ICommand SteamSignInCommand
        {
            get
            {
                // Steam's sign-in page in the theme's own browser (Playnite's sign-in window came up black in fullscreen).
                // Same Chromium and cookies as the wishlist sender. Left stick moves the cursor, A clicks, Y types; or scan
                // the QR code with the Steam app. Once it's back on the store you're in: it closes and the wishlist catches up.
                return steamSignInCmd ?? (steamSignInCmd = new RelayCommand(_ =>
                {
                    PlaySound("select");
                    steamSigningIn = true;
                    steamLoginSeen = false;
                    steamChecking = false;
                    steamSignInGen++;
                    // Already signed in: nothing to do. The store's sign-in is shared with Playnite's own Steam library
                    // (it reads your owned games through it), so it's never removed here: removing it signed that out too.
                    int gen = steamSignInGen;
                    string from = openPanel;
                    KippixShared.SafeWork.Queue(__ =>
                    {
                        bool? inn = null;
                        try { inn = SteamStoreSignedIn(); } catch (Exception ex) { logger.Warn("Kippix: Steam sign-in check: " + ex.Message); }
                        OnUi(() =>
                        {
                            if (gen != steamSignInGen || !steamSigningIn) return;
                            if (inn == true)
                            {
                                steamSigningIn = false;
                                logger.Info("Kippix: Steam sign-in: already signed in");
                                SetSteamSignedIn(true);
                                ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_SteamSignedIn", "Signed in to the Steam store"),
                                           settings.SyncWishToSteam ? Loc("LOC_KX_SteamSendingWish", "Sending your wishlist to Steam…") : "", "", null);
                                wishSignInBackoff = DateTime.MinValue;
                                SendWishlistToSteam(true);
                                return;
                            }
                            if (openPanel != from) { steamSigningIn = false; return; }   // you left the menu meanwhile: no browser popping up
                            logger.Info("Kippix: Steam sign-in: opening Steam's sign-in page");
                            OpenBrowser("https://store.steampowered.com/login/");
                            if (openPanel != "browser") steamSigningIn = false;   // (no browser: it went to Windows' own)
                        });
                    });
                }));
            }
        }

        private bool steamSigningIn, steamLoginSeen;

        /// <summary>The browser's address changed while signing in to Steam: back on the store (not the sign-in page) = signed in.</summary>
        private void SteamSignInProgress(string address, bool loading)
        {
            if (!steamSigningIn || string.IsNullOrEmpty(address)) return;
            if (loading)
            {
                logger.Info("Kippix: Steam sign-in: " + address.Split('?')[0]);
                if (address.IndexOf("store.steampowered.com/login", StringComparison.OrdinalIgnoreCase) >= 0) steamLoginSeen = true;   // (already signed in: it never finishes loading, it goes straight on)
                return;
            }
            Uri u;
            if (!Uri.TryCreate(address, UriKind.Absolute, out u)) return;
            if (!u.Host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase)) return;
            if (u.AbsolutePath.IndexOf("/login", StringComparison.OrdinalIgnoreCase) >= 0) { steamLoginSeen = true; return; }
            // back on the front page from the sign-in page: check with Steam that you really are signed in before closing
            // (a store page from a link on the sign-in page - the logo, Join Steam - isn't the front page and changes nothing)
            if (!steamLoginSeen || u.AbsolutePath != "/" || steamChecking) return;
            steamChecking = true;
            int checkGen = steamSignInGen;
            logger.Info("Kippix: Steam sign-in: back on the store, checking");
            KippixShared.SafeWork.Queue(_ =>
            {
                bool? inn = null;
                try { inn = SteamStoreSignedIn(); } catch (Exception ex) { logger.Warn("Kippix: Steam sign-in check: " + ex.Message); }
                OnUi(() =>
                {
                    if (checkGen != steamSignInGen) return;   // a newer sign-in started meanwhile: this check is about the old one
                    steamChecking = false;
                    if (!steamSigningIn) return;   // closed meanwhile
                    logger.Info("Kippix: Steam sign-in: " + (inn == true ? "signed in" : inn == false ? "not signed in" : "couldn't tell"));
                    SetSteamSignedIn(inn);
                    if (inn == false)
                    {
                        // stay on the page: the store's own "sign in" link at the top works from here
                        steamLoginSeen = false;
                        ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_SteamNotSignedIn", "Steam didn't sign you in"),
                                   Loc("LOC_KX_SteamNotSignedInSub", "Use \"sign in\" at the top of the page, then come back to the store's front page"), "", null);
                        return;
                    }
                    steamSigningIn = false;
                    if (kbOpen) CloseKeyboard(false);
                    if (openPanel == "browser") CloseBrowser();
                    ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_SteamSignedIn", "Signed in to the Steam store"),
                               settings.SyncWishToSteam ? Loc("LOC_KX_SteamSendingWish", "Sending your wishlist to Steam…") : "", "", null);
                    wishSignInBackoff = DateTime.MinValue;
                    SendWishlistToSteam(true);
                });
            });
        }

        private bool steamChecking;
        private readonly List<DateTime> steamLoginLoads = new List<DateTime>();
        private bool steamLoopFixing;
        private DateTime steamLoopFixedAt = DateTime.MinValue, steamLoopGaveUpAt = DateTime.MinValue;
        private string steamLoginLoopPage;

        /// <summary>
        /// A Steam sign-in page loading over and over (the checkout's "sign in again" flickered): an old sign-in Steam no
        /// longer accepts is left somewhere. Stop, clear the old sign-ins, and open Steam's sign-in page once, coming back
        /// to the cart afterwards.
        /// </summary>
        private void SteamLoginLoopCheck(string address)
        {
            Uri u;
            if (string.IsNullOrEmpty(address) || !Uri.TryCreate(address, UriKind.Absolute, out u)) return;
            if (!u.Host.EndsWith("steampowered.com", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.IndexOf("login", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (steamLoopFixing) return;
            var now = DateTime.Now;
            if ((now - steamLoopGaveUpAt).TotalMinutes < 3) return;   // stopped it already: quiet for a while
            // a real loop is the SAME sign-in page again and again, quickly (a normal sign-in passes through a few
            // different ones, once each)
            string here = u.Host + u.AbsolutePath;
            if (here != steamLoginLoopPage) { steamLoginLoopPage = here; steamLoginLoads.Clear(); }
            steamLoginLoads.Add(now);
            steamLoginLoads.RemoveAll(t => (now - t).TotalSeconds > 8);
            if (steamLoginLoads.Count < 4) return;
            steamLoginLoads.Clear();
            if ((now - steamLoopFixedAt).TotalMinutes < 3)
            {
                steamLoopGaveUpAt = now;
                // cleared a moment ago and still going round: stop there (no clearing again and again)
                BrowserCommand("StopCommand");
                logger.Info("Kippix: Steam sign-in page still reloading after clearing (" + u.Host + u.AbsolutePath + ")");
                ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_SteamLoop", "Steam's sign-in keeps reloading"),
                           Loc("LOC_KX_SteamLoopSub2", "Stopped it. Try Settings & Customisation › Store settings › Sign in to the Steam store"), "", null);
                return;
            }
            steamLoopFixing = true;
            steamLoopFixedAt = now;
            logger.Info("Kippix: Steam sign-in page keeps reloading (" + u.Host + u.AbsolutePath + "): clearing the old sign-in");
            BrowserCommand("StopCommand");
            bool cart = u.Host.StartsWith("checkout", StringComparison.OrdinalIgnoreCase) || address.IndexOf("cart", StringComparison.OrdinalIgnoreCase) >= 0;
            KippixShared.SafeWork.Queue(_ =>
            {
                try { ForgetSteamStoreLogin(); } catch (Exception ex) { logger.Warn("Kippix: Steam sign-in: removing the old sign-in: " + ex.Message); }
                OnUi(() =>
                {
                    steamLoopFixing = false;
                    if (openPanel != "browser" || browser == null) return;
                    steamSigningIn = true;
                    steamLoginSeen = false;
                    steamChecking = false;
                    steamSignInGen++;
                    ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_SteamSignInAgain", "Sign in to Steam once more"),
                               Loc("LOC_KX_SteamSignInAgainSub", "An old Steam sign-in was in the way: it's cleared"), "", null);
                    steamLoginLoads.Clear();   // (what loaded meanwhile isn't the new page's doing)
                    steamLoginLoopPage = null;
                    SetBrowserValue("Address", "https://store.steampowered.com/login/" + (cart ? "?redir=cart%2F" : ""));
                });
            });
        }

        /// <summary>The sign-in browser was closed (Steam's page signs you in without loading a new page, so nothing saw it).</summary>
        private void SteamSignInClosed()
        {
            int gen = ++steamSignInGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                bool? inn = null;
                try { inn = SteamStoreSignedIn(); } catch (Exception ex) { logger.Warn("Kippix: Steam sign-in check: " + ex.Message); }
                OnUi(() =>
                {
                    if (gen != steamSignInGen) return;
                    logger.Info("Kippix: Steam sign-in (browser closed): " + (inn == true ? "signed in" : inn == false ? "not signed in" : "couldn't tell"));
                    SetSteamSignedIn(inn);
                    if (inn != true) return;
                    ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_SteamSignedIn", "Signed in to the Steam store"),
                               settings.SyncWishToSteam ? Loc("LOC_KX_SteamSendingWish", "Sending your wishlist to Steam…") : "", "", null);
                    wishSignInBackoff = DateTime.MinValue;
                    SendWishlistToSteam(true);
                });
            });
        }
        private int steamSignInGen;

        /// <summary>Signed in to the Steam store (Playnite's browser sign-ins)? null: couldn't tell. Worker thread.</summary>
        private bool? SteamStoreSignedIn()
        {
            using (var view = api.WebViews.CreateOffscreenView())
            {
                view.NavigateAndWait("https://store.steampowered.com/");
                LogSteamCookies(view, "back on the store");
                string js = "(function(){try{var u=null;try{var c=document.getElementById('application_config');"
                          + "if(c&&c.getAttribute('data-userinfo'))u=JSON.parse(c.getAttribute('data-userinfo'));}catch(e){}"
                          + "return ((u&&u.logged_in)||(typeof g_AccountID!=='undefined'&&g_AccountID)||(typeof g_steamID==='string'&&g_steamID)||document.getElementById('account_pulldown'))?'yes':'no';}catch(e){return 'error';}})()";
                var task = view.EvaluateScriptAsync(js);
                if (!task.Wait(15000) || task.Result == null || !task.Result.Success || task.Result.Result == null) return null;
                string r = task.Result.Result.ToString();
                return r == "yes" ? true : r == "no" ? (bool?)false : null;
            }
        }

        /// <summary>
        /// When Steam's sign-in page keeps reloading: logs which Steam sign-in cookies Playnite's browser has (names and where,
        /// never values), then removes the checkout's and help site's left-over sign-ins (never the store's own).
        /// The Steam Community ones (Playnite's Steam library may use them) are left alone. Worker thread.
        /// </summary>
        private void ForgetSteamStoreLogin()
        {
            using (var view = api.WebViews.CreateOffscreenView())
            {
                LogSteamCookies(view, "before");
                // by address: that takes the site's own cookie and the ".site" one alike (the checkout's left-over sign-in
                // made its sign-in page bounce back and forth)
                // Only the checkout's and help site's left-overs: the store's own sign-in and the sign-in service's saved
                // one are shared with Playnite's Steam library (removing them signed it out and stopped owned games importing).
                foreach (string site in new[] { "https://checkout.steampowered.com", "https://help.steampowered.com" })
                    view.DeleteCookies(site, "steamLoginSecure");
                LogSteamCookies(view, "after removing");
            }
        }

        private void LogSteamCookies(Playnite.SDK.IWebView view, string when)
        {
            try
            {
                var names = new List<string>();
                foreach (var c in view.GetCookies() ?? new List<Playnite.SDK.HttpCookie>())
                {
                    if (c == null || string.IsNullOrEmpty(c.Domain) || c.Domain.IndexOf("steampowered.com", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (c.Name != "steamLoginSecure" && c.Name != "sessionid" && !c.Name.StartsWith("steamRefresh", StringComparison.OrdinalIgnoreCase) && c.Name != "steamCountry") continue;
                    names.Add(c.Name + "@" + c.Domain + (c.Expires.HasValue ? " (until " + c.Expires.Value.ToString("yyyy-MM-dd") + ")" : ""));
                }
                logger.Info("Kippix: Steam sign-in: cookies " + when + ": " + (names.Count == 0 ? "none" : string.Join(", ", names)));
            }
            catch (Exception ex) { logger.Info("Kippix: Steam sign-in: couldn't list cookies: " + ex.Message); }
        }

        private volatile bool wishRemoving;

        // ---------------- signed in or out (Store settings › Sign in to the Steam store shows it) ----------------

        private bool? steamSignedIn;
        private DateTime steamSignedInAt = DateTime.MinValue;
        private bool steamStatusChecking;

        public string SteamSignInStatus
        {
            get
            {
                if (steamSignedIn == true) return Loc("LOC_KX_SteamStatusIn", "Signed in");
                if (steamSignedIn == false) return Loc("LOC_KX_SteamStatusOut", "Signed out");
                if (!steamStatusChecking) return steamStatusFailed ? Loc("LOC_KX_SteamStatusUnknown", "Couldn't check") : Loc("LOC_KX_SteamStatusNotYet", "Not checked yet");   // (0.99.11: it said Checking… forever when nothing was checking)
                return Loc("LOC_KX_SteamStatusChecking", "Checking…");
            }
        }

        private void SetSteamSignedIn(bool? inn)
        {
            if (inn == null) return;
            bool nowIn = inn == true && steamSignedIn != true;
            steamSignedIn = inn;
            steamSignedInAt = DateTime.Now;
            Notify("SteamSignInStatus");
            // Getting started's Your stores: its sign-in row says so (it only followed the check made when the step opens), and
            // just signed in: the add-ons are asked again (Playnite's Steam library shares this sign-in)
            try { TourSignInRowAgain(); if (nowIn && TourOnStores) TourStoresSignedInCheck(); } catch (Exception) { }
        }

        /// <summary>Finds out (in the background) whether you're signed in, when Store settings open and it's not known lately.</summary>
        private bool steamStatusFailed;

        /// <param name="any">(0.99.11) Check even when the wishlist isn't sent to Steam (Getting started's Your stores).</param>
        private void CheckSteamSignInSoon(bool any = false)
        {
            if ((!settings.SyncWishToSteam && !any) || steamStatusChecking || steamSigningIn) return;
            if (steamSignedIn != null && (DateTime.Now - steamSignedInAt).TotalMinutes < 10) return;
            steamStatusChecking = true;
            KippixShared.SafeWork.Queue(_ =>
            {
                bool? inn = null;
                try { inn = SteamStoreSignedIn(); } catch (Exception ex) { logger.Warn("Kippix: Steam sign-in check: " + ex.Message); }
                OnUi(() =>
                {
                    steamStatusChecking = false;
                    steamStatusFailed = inn == null;
                    SetSteamSignedIn(inn);
                    Notify("SteamSignInStatus");
                    TourSignInRowAgain();
                });
            });
        }

        /// <summary>Takes games off your Steam wishlist that were taken off it here (kept until it works: signed out, offline).</summary>
        private void SendWishlistRemovals()
        {
            if (!settings.SyncWishToSteam || wishRemoving || settings.WishRemoveFromSteam == null || settings.WishRemoveFromSteam.Count == 0) return;
            var todo = settings.WishRemoveFromSteam.Distinct().ToList();
            wishRemoving = true;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, string> res = null;
                try { res = SteamWishlistCall(todo, "removefromwishlist"); }
                catch (Exception ex) { logger.Warn("Kippix: Steam wishlist (remove): " + ex.Message); }
                OnUi(() =>
                {
                    wishRemoving = false;
                    if (res == null) return;
                    // done (or Steam says no: not on it any more) — the rest wait for next time
                    foreach (var kv in res) if (kv.Value == "ok" || kv.Value == "rejected") settings.WishRemoveFromSteam.Remove(kv.Key);
                    bool signedOut = res.Values.Contains("signin");
                    if (signedOut) SetSteamSignedIn(false);
                    else if (res.Values.Contains("ok")) SetSteamSignedIn(true);
                    SaveSettings();
                    if (!signedOut && settings.WishRemoveFromSteam.Except(todo).Any()) SendWishlistRemovals();   // more taken off meanwhile
                });
            });
        }

        private volatile bool wishSending;
        private DateTime wishSentTriedAt = DateTime.MinValue;
        private DateTime wishSignInBackoff = DateTime.MinValue;
        private bool wishSendAgain;

        /// <summary>
        /// "Add games I wishlist here to my Steam": every game wishlisted here (not the ones that came from Steam) that's on
        /// Steam and isn't there yet goes onto your Steam wishlist. Runs when the switch goes on, when you wishlist a game, and
        /// when the Store opens (for any that didn't make it). <paramref name="tell"/>: say how it went (switching it on).
        /// </summary>
        private void SendWishlistToSteam(bool tell)
        {
            if (!settings.SyncWishToSteam) return;
            if (!tell && DateTime.Now < wishSignInBackoff) return;   // not signed in last time: not again for a while (switching it on tries at once)
            if (wishSending) { wishSendAgain = true; return; }
            var todo = settings.StoreWishlist.Where(w => w.ImportedFrom != "steam" && !w.OnSteam && !string.IsNullOrEmpty(w.SteamAppId))
                                             .Select(w => w.SteamAppId).Distinct().ToList();
            if (todo.Count == 0)
            {
                if (tell)
                {
                    WishImportText = Loc("LOC_KX_WishAllOnSteam", "Everything you've wishlisted here is on your Steam wishlist");
                    Notify("WishImportText");
                    if (toastWait != null && toastWait.IsEnabled) { SessionToastSession = WishImportText; Notify("SessionToastSession"); }   // ("Signed in" waiting to show: on its card)
                    else ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_CardWishlist", "Your wishlist"), WishImportText, "", null);
                }
                SendWishlistRemovals();
                return;
            }
            wishSending = true;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, string> res = null;
                try { res = SteamWishlistAddAll(todo); }
                catch (Exception ex) { logger.Warn("Kippix: Steam wishlist: " + ex.Message); }
                OnUi(() =>
                {
                    wishSending = false;
                    res = res ?? new Dictionary<string, string>();
                    int ok = 0;
                    foreach (var w in settings.StoreWishlist)
                    {
                        string r;
                        // on Steam now (or Steam won't take it: owned / not sold, no point asking again)
                        if (w.SteamAppId == null || !res.TryGetValue(w.SteamAppId, out r)) continue;
                        if (r == "ok") { if (!w.OnSteam) ok++; w.OnSteam = true; }
                        else if (r == "rejected" && ++w.SteamTries >= 3) w.OnSteam = true;   // Steam keeps saying no (owned / not sold): stop asking
                    }
                    SaveSettings();
                    bool signin = res.Values.Contains("signin");
                    if (signin) SetSteamSignedIn(false); else if (res.Values.Contains("ok")) SetSteamSignedIn(true);
                    if (!signin) SendWishlistRemovals();
                    wishSignInBackoff = signin ? DateTime.Now.AddMinutes(10) : DateTime.MinValue;   // (signing in again clears it)
                    if (signin)
                    {
                        WishImportText = Loc("LOC_KX_WishSteamSignIn3", "Couldn't add to Steam: you're not signed in to the Steam store in Playnite. Settings & Customisation › Store settings › Sign in to the Steam store.");
                        try { api.Notifications.Add(new Playnite.SDK.NotificationMessage("bbfs-steamwish", WishImportText, Playnite.SDK.NotificationType.Info)); } catch (Exception) { }
                    }
                    else if (ok > 0 || tell) WishImportText = string.Format(Loc("LOC_KX_WishSentCount", "{0} of {1} are on your Steam wishlist now"), ok, todo.Count);
                    Notify("WishImportText");
                    if (tell) ShowNotice(Loc("LOC_KX_CardSteam", "STEAM"), Loc("LOC_KX_CardWishlist", "Your wishlist"), WishImportText, "", null);
                    if (wishSendAgain) { wishSendAgain = false; if (!signin) SendWishlistToSteam(false); }
                });
            });
        }

        /// <summary>The Store's wishlist row and page show the change straight away (also while its settings are open over it).</summary>
        private void RefreshStoreWishViews()
        {
            try
            {
                BuildWishRow();
                Notify("StoreWishButtonText");   // the count on the Wishlist button
                if (storePage == "list" && storeId == "wishlist" && (openPanel == "store" || openPanel == "options")) ReloadStoreList(false);
                RefreshWidgets(false);   // Coming from your wishlist / deals widgets
            }
            catch (Exception ex) { logger.Warn("Kippix: wishlist refresh: " + ex.Message); }
        }

        /// <summary>An import switched off: the games it brought in come off the wishlist again (ones you added yourself stay).</summary>
        private void DropImported(string from)
        {
            int removed = settings.StoreWishlist.RemoveAll(w => w.ImportedFrom == from);
            WishImportText = removed > 0 ? string.Format(Loc("LOC_KX_WishTakenOff", "Took {0} games off"), removed) : "";
            Notify("WishImportText");
            SaveSettings();
            RefreshStoreWishViews();
        }

        /// <summary>Called when the Store opens (and when an import is switched on).</summary>
        private void SyncWishlistsSoon(bool now)
        {
            if (settings.SyncWishToSteam && (now || (DateTime.Now - wishSentTriedAt).TotalMinutes > 15)) { wishSentTriedAt = DateTime.Now; SendWishlistToSteam(false); }
            if (!settings.ImportSteamWishlist && !settings.ImportGogWishlist) return;
            if (wishSyncBusy) return;
            if (!now && (DateTime.Now - settings.WishImportAt).TotalMinutes < 15) return;
            wishSyncBusy = true;
            string steamId = settings.ImportSteamWishlist ? MySteamId() : null;
            bool gog = settings.ImportGogWishlist;
            string key = SteamKey;
            var current = settings.StoreWishlist.ToList();
            var dismissed = new HashSet<string>(settings.WishDismissed ?? new List<string>());
            KippixShared.SafeWork.Queue(_ =>
            {
                var added = new List<WishItem>();
                var stillThere = new HashSet<string>();
                string note = "";
                bool steamOk = false, gogOk = false;
                try
                {
                    if (steamId != null)
                    {
                        var steam = SteamWishlistIds(steamId, key);
                        if (steam != null)
                        {
                            steamOk = steam.Count > 0;   // an empty answer never takes your imported games away
                            foreach (var w in steam) stillThere.Add("steam:" + w.Item1);
                            var have = new HashSet<string>(current.Where(w => !string.IsNullOrEmpty(w.SteamAppId)).Select(w => w.SteamAppId));
                            var fresh = steam.Where(w => !have.Contains(w.Item1) && !dismissed.Contains("steam:" + w.Item1)).ToList();
                            var info = SteamAppsInfo(fresh.Select(f => f.Item1).ToList(), key);
                            foreach (var f in fresh)
                            {
                                SteamAppInfo a;
                                info.TryGetValue(f.Item1, out a);
                                if (a == null || string.IsNullOrEmpty(a.Name)) continue;
                                added.Add(new WishItem
                                {
                                    SteamAppId = f.Item1, Name = a.Name, Image = a.Header, Store = "Steam", Id = f.Item1, ImportedFrom = "steam",
                                    Link = "https://store.steampowered.com/app/" + f.Item1 + "/", Added = f.Item2, Currency = CurrencyCode,
                                    ReleaseDate = a.Release, ReleaseText = a.ReleaseText, ComingSoon = a.ComingSoon, ReleaseCheckedAt = DateTime.Now
                                });
                            }
                        }
                        else note = Loc("LOC_KX_WishSteamPrivate", "Couldn't read your Steam wishlist (is it public in Steam's privacy settings?)");
                    }
                    else if (settings.ImportSteamWishlist) note = Loc("LOC_KX_WishNoSteamId", "Your Steam account isn't known yet (Playnite's Steam library add-on needs to be set up)");
                    if (gog)
                    {
                        var ids = GogWishlistIds();
                        if (ids != null)
                        {
                            gogOk = ids.Count > 0;
                            foreach (string id in ids) stillThere.Add("gog:" + id);
                            var have = new HashSet<string>(current.Where(w => !string.IsNullOrEmpty(w.GogId)).Select(w => w.GogId));
                            foreach (string id in ids.Where(i => !have.Contains(i) && !dismissed.Contains("gog:" + i)).Take(60))
                            {
                                var p = GogProductInfo(id);
                                if (p == null) continue;
                                // already on the wishlist through another store (same name): don't add it twice
                                if (current.Concat(added).Any(w => NormName(w.Name) == NormName(p.Name))) continue;
                                added.Add(new WishItem
                                {
                                    GogId = id, Name = p.Name, Image = p.Header, Store = "GOG", Id = id, ImportedFrom = "gog", Link = p.Link,
                                    Added = DateTime.Now, Currency = CurrencyCode, ReleaseDate = p.Release, ComingSoon = p.ComingSoon, ReleaseCheckedAt = DateTime.Now
                                });
                            }
                        }
                        else note = (note.Length > 0 ? note + "  ·  " : "") + Loc("LOC_KX_WishGogSignIn", "Couldn't read your GOG wishlist: sign in to GOG in Playnite (its GOG library add-on) first");
                    }
                    // their IsThereAnyDeal ids, for prices (a few at a time)
                    foreach (var w in added.Take(25))
                        try { w.ItadId = ItadLookup(w.SteamAppId, w.Name); } catch (Exception) { }
                }
                catch (Exception ex) { logger.Warn("Kippix: wishlist import: " + ex.Message); }
                OnUi(() =>
                {
                    wishSyncBusy = false;
                    settings.WishImportAt = DateTime.Now;
                    // taken off on Steam / GOG: gone here too (only what was brought in from there)
                    int removed = settings.StoreWishlist.RemoveAll(w => (w.ImportedFrom == "steam" && steamOk && !stillThere.Contains("steam:" + w.SteamAppId))
                                                                     || (w.ImportedFrom == "gog" && gogOk && !stillThere.Contains("gog:" + w.GogId)));
                    foreach (var w in added)
                        if ((w.ImportedFrom == "steam" ? settings.ImportSteamWishlist : w.ImportedFrom == "gog" ? settings.ImportGogWishlist : true)   // switched off meanwhile
                            && !settings.StoreWishlist.Any(x => (x.SteamAppId != null && x.SteamAppId == w.SteamAppId) || (x.GogId != null && x.GogId == w.GogId))) settings.StoreWishlist.Add(w);
                    wishImportProblem = note.Length > 0;
                    WishImportText = note.Length > 0 ? note
                                   : added.Count > 0 ? string.Format(Loc("LOC_KX_WishImported", "Brought in {0} games"), added.Count)
                                   : Loc("LOC_KX_WishUpToDate", "Up to date");
                    Notify("WishImportText");
                    if (added.Count > 0 || removed > 0)
                    {
                        SaveSettings();
                        RefreshStoreWishViews();
                    }
                    else SaveSettings();
                });
            });
        }

        /// <summary>Remembers a game you took off the wishlist here, so the import doesn't bring it back.</summary>
        private void WishRemovedHere(WishItem w)
        {
            // taken off here: off Steam's wishlist too (games sent there, or brought in from it)
            if (w != null && settings.SyncWishToSteam && !string.IsNullOrEmpty(w.SteamAppId) && (w.OnSteam || w.ImportedFrom == "steam"))
            {
                if (settings.WishRemoveFromSteam == null) settings.WishRemoveFromSteam = new List<string>();
                if (!settings.WishRemoveFromSteam.Contains(w.SteamAppId)) settings.WishRemoveFromSteam.Add(w.SteamAppId);
                ui.BeginInvoke(new Action(() => SendWishlistRemovals()), System.Windows.Threading.DispatcherPriority.Background);
            }
            if (w == null || string.IsNullOrEmpty(w.ImportedFrom)) return;
            if (settings.WishDismissed == null) settings.WishDismissed = new List<string>();
            string key = w.ImportedFrom == "gog" ? "gog:" + w.GogId : "steam:" + w.SteamAppId;
            if (!settings.WishDismissed.Contains(key)) settings.WishDismissed.Add(key);
        }

        // ---------------- Steam ----------------

        /// <summary>The app ids on a Steam wishlist, with when each was added (null when it can't be read).</summary>
        private List<Tuple<string, DateTime>> SteamWishlistIds(string steamId, string key)
        {
            string url = "https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=" + Uri.EscapeDataString(steamId) + (string.IsNullOrEmpty(key) ? "" : "&key=" + Uri.EscapeDataString(key));
            var root = StoreJson(url) as Dictionary<string, object>;
            var resp = Dig(root, "response") as Dictionary<string, object>;
            if (resp == null) return null;
            var list = new List<Tuple<string, DateTime>>();
            var items = Dig(resp, "items") as IEnumerable;
            if (items == null) return null;   // a private wishlist answers with nothing at all (not even an empty list)
            if (items != null)
                foreach (var it in items.Cast<object>().OfType<Dictionary<string, object>>())
                {
                    string id = FStr(it, "appid");
                    if (id.Length == 0) continue;
                    long added = FNum(it, "date_added");
                    list.Add(Tuple.Create(id, added > 0 ? UnixDate(added) : DateTime.Now));
                }
            return list;
        }

        private class SteamAppInfo { public string Name, Header, ReleaseText; public DateTime? Release; public bool ComingSoon; }

        private static DateTime UnixDate(long secs) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs).ToLocalTime(); }

        /// <summary>Name, wide picture and release for Steam apps: the store's batch call, else one at a time (a few).</summary>
        private Dictionary<string, SteamAppInfo> SteamAppsInfo(List<string> ids, string key)
        {
            var result = new Dictionary<string, SteamAppInfo>();
            for (int i = 0; i < ids.Count; i += 50)
            {
                var batch = ids.Skip(i).Take(50).ToList();
                try
                {
                    var req = new Dictionary<string, object>
                    {
                        { "ids", batch.Select(id => new Dictionary<string, object> { { "appid", int.Parse(id) } }).ToList() },
                        { "context", new Dictionary<string, object> { { "language", "english" }, { "country_code", Country } } },
                        { "data_request", new Dictionary<string, object> { { "include_basic_info", true }, { "include_release", true }, { "include_assets", true } } }
                    };
                    string json = new JavaScriptSerializer().Serialize(req);
                    string url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(json) + (string.IsNullOrEmpty(key) ? "" : "&key=" + Uri.EscapeDataString(key));
                    var root = StoreJson(url) as Dictionary<string, object>;
                    var items = Dig(root, "response", "store_items") as IEnumerable;
                    if (items != null)
                        foreach (var it in items.Cast<object>().OfType<Dictionary<string, object>>())
                        {
                            string id = FStr(it, "appid");
                            if (id.Length == 0 || FStr(it, "name").Length == 0) continue;
                            var a = new SteamAppInfo { Name = FStr(it, "name") };
                            var assets = Dig(it, "assets") as Dictionary<string, object>;
                            string fmt = assets != null ? FStr(assets, "asset_url_format") : "";
                            string header = assets != null ? FStr(assets, "header") : "";
                            a.Header = header.Length > 0 && fmt.Length > 0 ? "https://shared.akamai.steamstatic.com/store_item_assets/" + fmt.Replace("${FILENAME}", header)
                                                                           : "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + id + "/header.jpg";
                            var rel = Dig(it, "release") as Dictionary<string, object>;
                            if (rel != null)
                            {
                                long when = FNum(rel, "steam_release_date");
                                if (when <= 0) when = FNum(rel, "original_release_date");
                                bool soon = Dig(rel, "is_coming_soon") is bool && (bool)Dig(rel, "is_coming_soon");
                                a.ComingSoon = soon;
                                string display = FStr(rel, "custom_release_date_message");
                                if (display.Length == 0) display = FStr(rel, "coming_soon_display");
                                bool exact = display.Length == 0 || display == "date_full";
                                if (when > 0 && exact) a.Release = UnixDate(when);
                                else if (when > 0 && soon) { a.Release = UnixDate(when); a.ReleaseText = ReleaseWords(display, a.Release.Value); }
                                else if (display.Length > 0 && display != "date_full") a.ReleaseText = display;
                            }
                            result[id] = a;
                        }
                }
                catch (Exception ex) { logger.Info("Kippix: Steam store items: " + ex.Message); }
            }
            // anything the batch call didn't give: Steam's app details, a few at a time
            int asked = 0;
            foreach (string id in ids.Where(x => !result.ContainsKey(x)))
            {
                if (asked++ >= 30) break;
                try
                {
                    var app = StoreJson("https://store.steampowered.com/api/appdetails?l=english&cc=" + Country + "&filters=basic,release_date&appids=" + Uri.EscapeDataString(id)) as Dictionary<string, object>;
                    var d = Dig(app, id, "data") as Dictionary<string, object>;
                    if (d == null) continue;
                    var a = new SteamAppInfo { Name = FStr(d, "name"), Header = FStr(d, "header_image") };
                    var rel = Dig(d, "release_date") as Dictionary<string, object>;
                    if (rel != null)
                    {
                        a.ComingSoon = Dig(rel, "coming_soon") is bool && (bool)Dig(rel, "coming_soon");
                        string date = FStr(rel, "date");
                        DateTime parsed;
                        if (DateTime.TryParse(date, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out parsed) && date.Any(char.IsDigit) && date.Length > 6) a.Release = parsed;
                        else if (date.Length > 0) a.ReleaseText = date;
                    }
                    result[id] = a;
                    Thread.Sleep(900);   // Steam limits how often this can be asked
                }
                catch (Exception) { }
            }
            return result;
        }

        private string ReleaseWords(string display, DateTime when)
        {
            switch (display)
            {
                case "date_month": return when.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
                case "date_quarter": return "Q" + ((when.Month - 1) / 3 + 1) + " " + when.Year;
                case "date_year": return when.Year.ToString();
            }
            return when.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        }

        /// <summary>Keeps the release dates of wishlisted Steam games up to date (for the calendar; at most daily per game).</summary>
        private void RefreshWishReleases(Action done)
        {
            var due = settings.StoreWishlist.Where(w => !string.IsNullOrEmpty(w.SteamAppId) && (DateTime.Now - w.ReleaseCheckedAt).TotalHours > 24).ToList();
            if (due.Count == 0) { if (done != null) done(); return; }
            string key = SteamKey;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, SteamAppInfo> info = null;
                try { info = SteamAppsInfo(due.Select(w => w.SteamAppId).Distinct().ToList(), key); } catch (Exception) { }
                OnUi(() =>
                {
                    if (info != null)
                        foreach (var w in due)
                        {
                            SteamAppInfo a;
                            if (!info.TryGetValue(w.SteamAppId, out a)) continue;
                            w.ReleaseDate = a.Release;
                            w.ReleaseText = a.ReleaseText;
                            w.ComingSoon = a.ComingSoon;
                            w.ReleaseCheckedAt = DateTime.Now;
                            if (string.IsNullOrEmpty(w.Image)) w.Image = a.Header;
                        }
                    SaveSettings();
                    if (done != null) done();
                });
            });
        }

        // ---------------- GOG ----------------

        /// <summary>The product ids on your GOG wishlist, read with the sign-in Playnite's GOG add-on keeps (null when not signed in).</summary>
        private List<string> GogWishlistIds()
        {
            string text = null;
            try
            {
                using (var view = api.WebViews.CreateOffscreenView())
                {
                    view.NavigateAndWait("https://embed.gog.com/user/wishlist.json");
                    text = view.GetPageText();
                }
            }
            catch (Exception ex) { logger.Info("Kippix: GOG wishlist: " + ex.Message); return null; }
            if (string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith("{")) return null;
            Dictionary<string, object> root;
            try { root = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>; }
            catch (Exception ex) { logger.Info("Kippix: GOG wishlist: " + ex.Message); return null; }   // (not JSON after all: "couldn't read", not "up to date")
            var wl = Dig(root, "wishlist") as Dictionary<string, object>;
            if (wl == null) return null;
            return wl.Where(kv => kv.Value is bool && (bool)kv.Value).Select(kv => kv.Key).ToList();
        }

        private class GogProduct { public string Name, Header, Link; public DateTime? Release; public bool ComingSoon; }

        private GogProduct GogProductInfo(string id)
        {
            try
            {
                var d = StoreJson("https://api.gog.com/products/" + Uri.EscapeDataString(id) + "?locale=en-US") as Dictionary<string, object>;
                if (d == null || FStr(d, "title").Length == 0) return null;
                var p = new GogProduct { Name = FStr(d, "title") };
                var images = Dig(d, "images") as Dictionary<string, object>;
                string bg = images != null ? FStr(images, "logo2x") : "";
                if (bg.Length == 0 && images != null) bg = FStr(images, "background");
                if (bg.StartsWith("//")) bg = "https:" + bg;
                p.Header = bg;
                var links = Dig(d, "links") as Dictionary<string, object>;
                p.Link = links != null && FStr(links, "product_card").Length > 0 ? FStr(links, "product_card") : "https://www.gog.com/game/" + FStr(d, "slug");
                string rel = FStr(d, "release_date");
                DateTime when;
                if (rel.Length > 0 && DateTime.TryParse(rel, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out when)) { p.Release = when.ToLocalTime(); p.ComingSoon = when > DateTime.Now; }
                var flags = Dig(d, "in_development") as Dictionary<string, object>;
                if (flags != null && Dig(flags, "active") is bool && (bool)Dig(flags, "active")) p.ComingSoon = true;
                return p;
            }
            catch (Exception) { return null; }
        }
    }
}
