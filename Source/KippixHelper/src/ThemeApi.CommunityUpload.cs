using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (1.0.1) Community › Yours › Upload sends straight to the gallery: signed in to GitHub (once, with a code: "ghsignin"),
    /// the share goes to the gallery as a request its owner approves. Your uploads are listed on Yours with what happened to
    /// each (waiting, in the gallery, turned down). Without a GitHub account, the files are still saved to post by hand.
    /// </summary>
    public partial class ThemeApi
    {
        private string GitHubToken
        {
            get
            {
                if (string.IsNullOrEmpty(settings.GitHubToken)) return null;
                try { return Unprotect(settings.GitHubToken); } catch (Exception) { return null; }
            }
        }

        private bool GitHubSignedIn { get { return !string.IsNullOrEmpty(settings.GitHubLogin) && GitHubToken != null; } }

        private void GitHubSignOut()
        {
            settings.GitHubToken = "";
            settings.GitHubLogin = "";
            SaveSettings();
        }

        // the upload waiting for the sign-in, and the sign-in itself
        private string pendingUploadKind, pendingUploadFolder, pendingUploadId;
        private GitHubApi.DeviceCode ghCode;
        private DispatcherTimer ghTimer;
        private DateTime ghExpiresAt, ghNextPoll;
        private string ghStatus;
        private bool ghPolling;
        private bool uploadBusy;
        private string uploadStatus;   // what Yours says about the upload going on now (or the last one)

        /// <summary>After Share has saved the files: send them (signing in first when needed).</summary>
        private void SendShareToGallery(string kind, string folder, string id)
        {
            if (string.IsNullOrEmpty(CommunityRepo)) return;
            if (!GitHubSignedIn)
            {
                pendingUploadKind = kind; pendingUploadFolder = folder; pendingUploadId = id;
                OpenPage("ghsignin", "", true);
                return;
            }
            StartUpload(kind, folder, id);
        }

        private void StartUpload(string kind, string folder, string id)
        {
            if (uploadBusy) return;
            string token = GitHubToken, login = settings.GitHubLogin, repo = CommunityRepo;
            string branch = string.IsNullOrWhiteSpace(settings.CommunityBranch) ? "main" : settings.CommunityBranch.Trim();
            uploadBusy = true;
            SetUploadStatus(Loc("LOC_KX_UploadSending", "Sending it to the gallery…"));
            KippixShared.SafeWork.Queue(_ =>
            {
                GitHubApi.Sent sent = null;
                string error = null;
                bool signedOut = false;
                try
                {
                    sent = GitHubApi.Upload(token, login, repo, branch, kind, folder, id, null, step => OnUi(() =>
                    {
                        string s = step == "fork" ? Loc("LOC_KX_UploadStepFork", "Sending it to the gallery… (making your copy of the gallery, first time only)")
                                 : step == "files" ? Loc("LOC_KX_UploadStepFiles", "Sending it to the gallery… (the files)")
                                 : step == "request" ? Loc("LOC_KX_UploadStepAsk", "Sending it to the gallery… (asking for it to be added)")
                                 : Loc("LOC_KX_UploadSending", "Sending it to the gallery…");
                        SetUploadStatus(s);
                    }));
                }
                catch (GitHubApi.Failure f) { error = f.Message; signedOut = f.Status == 401; logger.Warn("Kippix: community upload: " + f.Message); }
                catch (Exception ex) { error = ex.Message; logger.Warn("Kippix: community upload: " + ex.Message); }
                OnUi(() =>
                {
                    uploadBusy = false;
                    if (sent != null)
                    {
                        if (settings.CommunitySent == null) settings.CommunitySent = new List<CommunitySentItem>();
                        string name = id;
                        try
                        {
                            var entry = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, id + ".entry.json"))) as Dictionary<string, object>;
                            if (entry != null && entry.ContainsKey("name")) name = Convert.ToString(entry["name"], CultureInfo.InvariantCulture);
                        }
                        catch (Exception) { }
                        settings.CommunitySent.Insert(0, new CommunitySentItem { Kind = kind, Name = name, Number = sent.Number, Url = sent.Url, At = DateTime.Now, State = "open" });
                        while (settings.CommunitySent.Count > 30) settings.CommunitySent.RemoveAt(settings.CommunitySent.Count - 1);
                        SaveSettings();
                        logger.Info("Kippix: community upload sent: " + sent.Url);
                        SetUploadStatus(Loc("LOC_KX_UploadSent", "Sent! It's waiting for approval, and shows in the gallery once it's accepted. Your uploads (below) say how it's going."));
                    }
                    else
                    {
                        if (signedOut) GitHubSignOut();
                        SetUploadStatus(signedOut
                            ? Loc("LOC_KX_UploadSignedOut", "GitHub signed you out, so it wasn't sent. Sign in again (below) and upload it again.")
                            : string.Format(Loc("LOC_KX_UploadFailed", "It couldn't be sent ({0}). The files are saved in {1}: Open the sharing page posts them by hand."), error, folder));
                        communitySharedKind = kind;   // (the next build has the sharing page's button)
                    }
                });
            });
        }

        private void SetUploadStatus(string s)
        {
            uploadStatus = s;
            if (IsCommunityHub && communityTab == "mine")
            {
                communityStatus = s;
                var row = FocusedPageItem() as PageRow;
                ShowCommunityPage("mine", row != null ? row.Key : null);
            }
            else communityStatus = s;
        }

        // ---------------- Yours: GitHub and your uploads ----------------

        private void AddCommunityGitHubRows()
        {
            if (string.IsNullOrEmpty(CommunityRepo)) return;
            AddPageHeader(Loc("LOC_KX_SecGitHub", "GITHUB"));
            if (GitHubSignedIn)
                AddMineRow("gh:out", "profile", string.Format(Loc("LOC_KX_GitHubSignedIn", "Signed in to GitHub as {0}"), settings.GitHubLogin),
                           Loc("LOC_KX_GitHubSignOutSub", "Uploads go straight to the gallery. Choose this to sign out (github.com › Settings › Applications also removes Kippix's access)"),
                           () => { GitHubSignOut(); PlaySound("back"); ShowCommunityPage("mine", "gh:in"); });
            else
                AddMineRow("gh:in", "profile", Loc("LOC_KX_GitHubSignIn", "Sign in to GitHub"),
                           Loc("LOC_KX_GitHubSignInSub", "To upload straight to the gallery (a free GitHub account). Without one, Upload saves the files for you to post"),
                           () => { pendingUploadKind = null; OpenPage("ghsignin", "", true); });

            var sentList = settings.CommunitySent ?? new List<CommunitySentItem>();
            if (sentList.Count == 0) return;
            AddPageHeader(Loc("LOC_KX_SecYourUploads", "YOUR UPLOADS"));
            foreach (var s in sentList.Take(10))
            {
                var item = s;
                string state = item.State == "merged" ? Loc("LOC_KX_UploadStateIn", "In the gallery")
                             : item.State == "closed" ? Loc("LOC_KX_UploadStateNo", "Not added")
                             : Loc("LOC_KX_UploadStateWaiting", "Waiting for approval");
                PageRows.Add(new PageRow
                {
                    Key = "sent:" + item.Number, Icon = CommunityKindIcon(item.Kind), Text = item.Name,
                    Sub = CommunityKindOne(item.Kind) + "   ·   " + item.At.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
                    Right = state,
                    Command = new RelayCommand(_ => { if (!string.IsNullOrEmpty(item.Url)) { PlaySound("select"); OpenLink(item.Url); } })
                });
            }
            RefreshUploadStates();
        }

        private DateTime uploadStatesAt = DateTime.MinValue;

        /// <summary>What happened to the uploads still waiting (at most once a minute; GitHub's answer, off the screen's thread).</summary>
        private void RefreshUploadStates()
        {
            var waiting = (settings.CommunitySent ?? new List<CommunitySentItem>()).Where(s => s.State == "open" && s.Number > 0).Take(10).ToList();
            if (waiting.Count == 0 || (DateTime.Now - uploadStatesAt).TotalSeconds < 60) return;
            uploadStatesAt = DateTime.Now;
            string token = GitHubToken, repo = CommunityRepo;
            KippixShared.SafeWork.Queue(_ =>
            {
                var changed = new Dictionary<int, string>();
                foreach (var s in waiting)
                {
                    try { string st = GitHubApi.State(token, repo, s.Number); if (st != s.State) changed[s.Number] = st; }
                    catch (Exception) { }
                }
                if (changed.Count == 0) return;
                OnUi(() =>
                {
                    foreach (var s in settings.CommunitySent) { string st; if (changed.TryGetValue(s.Number, out st)) s.State = st; }
                    SaveSettings();
                    if (changed.Values.Contains("merged")) communityAt = DateTime.MinValue;   // (the gallery has something new)
                    if (IsCommunityHub && communityTab == "mine")
                        foreach (var row in PageRows.Where(r => r.Key != null && r.Key.StartsWith("sent:")).ToList())
                        {
                            int n; string st;
                            if (int.TryParse(row.Key.Substring(5), out n) && changed.TryGetValue(n, out st))
                                row.SetRight(st == "merged" ? Loc("LOC_KX_UploadStateIn", "In the gallery") : st == "closed" ? Loc("LOC_KX_UploadStateNo", "Not added") : Loc("LOC_KX_UploadStateWaiting", "Waiting for approval"));
                        }
                });
            });
        }

        // ---------------- the sign-in page ----------------

        private void BuildGitHubSignInPage()
        {
            PageTitle = Loc("LOC_KX_GitHubSignInTitle", "Sign in to GitHub");
            PageSub = Loc("LOC_KX_GitHubSignInPageSub", "Once, so your uploads go straight to the Community gallery. Kippix never sees your password.");
            if (ghCode == null || DateTime.Now > ghExpiresAt) { ghCode = null; StartGitHubCode(); }
            GitHubSignInRows();
            StartGitHubPoll();
        }

        private void StartGitHubCode()
        {
            ghStatus = Loc("LOC_KX_GitHubGettingCode", "Getting a code from GitHub…");
            int gen = pageGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                GitHubApi.DeviceCode code = null;
                string error = null;
                try { code = GitHubApi.StartSignIn(); }
                catch (Exception ex) { error = ex.Message; logger.Warn("Kippix: GitHub sign-in: " + ex.Message); }
                OnUi(() =>
                {
                    ghCode = code;
                    if (code != null) { ghExpiresAt = DateTime.Now.AddSeconds(code.ExpiresIn - 10); ghNextPoll = DateTime.Now.AddSeconds(code.Interval); ghStatus = Loc("LOC_KX_GitHubWaiting", "Waiting for you to enter the code…"); }
                    else ghStatus = string.Format(Loc("LOC_KX_GitHubNoCode", "GitHub couldn't be reached ({0}). Try again in a moment."), error);
                    if (gen == pageGen && openPanel == "page" && pageKey == "ghsignin") GitHubSignInRows();
                });
            });
        }

        private void GitHubSignInRows()
        {
            var focused = FocusedPageItem() as PageRow;
            string focusKey = focused != null ? focused.Key : null;
            PageRows.Clear();
            if (ghCode != null)
            {
                AddPageHeader(Loc("LOC_KX_GitHubStep1", "ON YOUR PHONE OR COMPUTER"));
                PageRows.Add(new PageRow { Key = "gh:addr", Icon = "globe", Text = "github.com/login/device", Sub = Loc("LOC_KX_GitHubGoTo", "Go here and sign in to GitHub (or make a free account)") });
                PageRows.Add(new PageRow { Key = "gh:code", Icon = "lock", Text = ghCode.User, Sub = Loc("LOC_KX_GitHubEnterCode", "Enter this code, then press Authorise"), Right = Loc("LOC_KX_GitHubCodeRight", "Your code") });
                AddPageHeader(Loc("LOC_KX_GitHubOrHere", "OR ON THIS SCREEN"));
                PageRows.Add(new PageRow
                {
                    Key = "gh:here", Icon = "link", Text = Loc("LOC_KX_GitHubOpenHere", "Open the sign-in page here"),
                    Sub = Loc("LOC_KX_GitHubOpenHereSub", "In Kippix's browser: sign in, type the code, Authorise, then come back"),
                    Command = new RelayCommand(_ => { PlaySound("select"); OpenBrowser(ghCode != null ? ghCode.Uri : "https://github.com/login/device"); })
                });
            }
            AddPageHeader(Loc("LOC_KX_GitHubWhatHappens", "WHAT KIPPIX CAN DO"));
            PageRows.Add(new PageRow { Key = "gh:what", Icon = "info", Text = Loc("LOC_KX_GitHubWhat", "Only send your uploads to the gallery"),
                                       Sub = Loc("LOC_KX_GitHubWhatSub", "GitHub asks you to allow access to public repositories: Kippix uses it to make your copy of the gallery and propose your upload from it. The sign-in stays on this PC, encrypted, and you can remove it any time (Yours, or github.com › Settings › Applications).") });
            if (pendingUploadKind != null)
                PageRows.Add(new PageRow
                {
                    Key = "gh:skip", Icon = "folder", Text = Loc("LOC_KX_GitHubNoAccount", "No GitHub account? Post the files yourself"),
                    Sub = Loc("LOC_KX_GitHubNoAccountSub", "They're saved already: this opens the sharing page to attach them"),
                    Command = new RelayCommand(_ =>
                    {
                        string kind = pendingUploadKind;
                        pendingUploadKind = null;
                        StopGitHubPoll();
                        communitySharedKind = kind;
                        communityStatus = string.Format(Loc("LOC_KX_ShareSavedShort", "Saved in {0}, ready to post: Open the sharing page and attach the three files."), ShareFolderFor(kind));
                        ShowCommunityPage("mine", "c:post");
                    })
                });
            SetPageStatus(ghStatus ?? "");
            NotifyPageLists();
            var again = focusKey != null ? PageRows.FirstOrDefault(r => r.Key == focusKey && r.Command != null) : null;
            FocusPageSoon(again ?? PageRows.FirstOrDefault(r => r.Key == "gh:here"));
        }

        private void StartGitHubPoll()
        {
            if (ghTimer == null)
            {
                ghTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
                ghTimer.Tick += (s, e) => GitHubPollTick();
            }
            ghTimer.Start();
        }

        private void StopGitHubPoll()
        {
            if (ghTimer != null) ghTimer.Stop();
        }

        private void GitHubPollTick()
        {
            // only while the page is open: leaving it stops the waiting
            if (!(openPanel == "page" && pageKey == "ghsignin")) { StopGitHubPoll(); return; }
            if (ghCode == null || ghPolling || DateTime.Now < ghNextPoll) return;
            if (DateTime.Now > ghExpiresAt)
            {
                ghCode = null;
                ghStatus = Loc("LOC_KX_GitHubExpired", "The code ran out. Here's a new one.");
                StartGitHubCode();
                GitHubSignInRows();
                return;
            }
            ghPolling = true;
            var code = ghCode;
            int gen = pageGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                string token = null, login = null, error = null;
                bool denied = false;
                try
                {
                    token = GitHubApi.PollSignIn(code);
                    if (token != null) login = GitHubApi.Login(token);
                }
                catch (GitHubApi.Failure f) { error = f.Message; denied = f.Status == 403; }
                catch (Exception ex) { error = ex.Message; }
                OnUi(() =>
                {
                    ghPolling = false;
                    ghNextPoll = DateTime.Now.AddSeconds(code.Interval);
                    bool here = gen == pageGen && openPanel == "page" && pageKey == "ghsignin";
                    if (token != null && !string.IsNullOrEmpty(login))
                    {
                        settings.GitHubToken = Protect(token);
                        settings.GitHubLogin = login;
                        SaveSettings();
                        ghCode = null;
                        StopGitHubPoll();
                        logger.Info("Kippix: signed in to GitHub as " + login);
                        PlaySound("select");
                        string kind = pendingUploadKind, folder = pendingUploadFolder, id = pendingUploadId;
                        pendingUploadKind = null;
                        communityStatus = string.Format(Loc("LOC_KX_GitHubSignedInNow", "Signed in to GitHub as {0}."), login);
                        ShowCommunityPage("mine", kind != null ? "u:" + kind : "gh:out");
                        if (kind != null) StartUpload(kind, folder, id);
                        return;
                    }
                    if (error != null)
                    {
                        ghCode = null;
                        ghStatus = denied ? Loc("LOC_KX_GitHubDenied", "The sign-in was turned down on GitHub. Here's a new code if you want to try again.")
                                          : string.Format(Loc("LOC_KX_GitHubSignInFailed", "The sign-in didn't work ({0}). Here's a new code."), error);
                        if (here) { StartGitHubCode(); GitHubSignInRows(); }
                    }
                });
            });
        }
    }
}
