using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.48) "Story ~60%" for the highlighted game: on the home screen (Home settings › Logo, game info &amp; button hints ›
    /// More about the game › Story progress) and in the game page's play info (its own line, in the play info's order).
    /// Worked out by StoryProgress.Estimate from the game's achievements (Playnite Achievements / SuccessStory), its play time,
    /// HowLongToBeat and its completion status; off the UI thread, kept 3 minutes per game.
    /// </summary>
    public partial class ThemeApi
    {
        private readonly Dictionary<Guid, Tuple<DateTime, ProgressResult>> progressCache = new Dictionary<Guid, Tuple<DateTime, ProgressResult>>();
        private readonly HashSet<Guid> progressLoading = new HashSet<Guid>();
        private string homeProgressText = "", detProgressText = "";
        private int homeProgressPct, detProgressPct;

        public bool HomeProgress
        {
            get { return settings.HomeProgress; }
            set { if (SetSetting(ref settings.HomeProgress, value)) { HomeProgressSwitched(); if (value) HomePartBackOnTop("home.progress"); } }
        }

        /// <summary>(0.99.50) Switched (the Logo page's switch, the Game info details list, or deleted in Free move): the other places follow.</summary>
        private void HomeProgressSwitched()
        {
            if (InfoItems != null)
            {
                var item = InfoItems.FirstOrDefault(i => i.Key == "progress");
                if (item != null && item.IsPinned != settings.HomeProgress) item.SetPinnedSilently(settings.HomeProgress);
            }
            Notify("HomeProgress");
            HomeProgressRefresh();
            Notify("HomeShowProgress");
        }
        public bool HomeShowProgress { get { return settings.HomeProgress && IsFullscreen && !homeProgressNone; } }   // (0.99.65: there while it's on, the figure fills in; gone for a game with nothing to go by)
        public string HomeProgressText { get { return homeProgressText.Length > 0 ? homeProgressText : StoryWaitText; } }
        public double HomeProgressBar { get { return homeProgressPct; } }

        public bool DetInfoShowProgress { get { return DetInfoOn("progress") && detProgressText.Length > 0; } }
        public string DetProgressText { get { return detProgressText; } }
        public double DetProgressBar { get { return detProgressPct; } }

        /// <summary>The game's estimate when it's known (null: being worked out; done() runs on the UI thread once it is).</summary>
        private ProgressResult ProgressFor(Game g, Action done)
        {
            if (g == null) return null;
            lock (progressCache)
            {
                Tuple<DateTime, ProgressResult> c;
                if (progressCache.TryGetValue(g.Id, out c) && (DateTime.Now - c.Item1).TotalMinutes < 3) return c.Item2;
                if (progressLoading.Contains(g.Id)) return null;
                progressLoading.Add(g.Id);
            }
            var game = g;
            string ext = api.Paths.ExtensionsDataPath;
            var extra = new[] { settings.CompletionBeaten, settings.Completion100 };
            KippixShared.SafeWork.Queue(_ =>
            {
                ProgressResult r = null;
                try
                {
                    var list = new List<ProgressAch>();
                    try
                    {
                        string src;
                        var d = AchievementSource.LoadOne(api, game, out src);
                        if (d != null && d.Items != null)
                            foreach (var a in d.Items)
                                list.Add(new ProgressAch { Name = a.Name, Description = a.Description, Unlocked = a.Unlocked, Rarity = a.Rarity, Counter = a.ProgressDenom.HasValue && a.ProgressDenom.Value > 1 });
                    }
                    catch (Exception) { }
                    double story = 0, extras = 0, complete = 0;
                    try
                    {
                        var h = KippixOverlay.GameData.LoadHltb(ext, game.Id);
                        if (h != null) { story = h.Story; extras = h.Extras; complete = h.Completionist; }
                    }
                    catch (Exception) { }
                    string status = null;
                    try { var cs = game.CompletionStatus; if (cs != null) status = cs.Name; } catch (Exception) { }
                    r = StoryProgress.Estimate(list, game.Playtime, story, extras, complete, status, extra);
                }
                catch (Exception ex) { logger.Warn("Kippix: story progress (" + game.Name + "): " + ex.Message); }
                lock (progressCache) { progressCache[game.Id] = Tuple.Create(DateTime.Now, r); progressLoading.Remove(game.Id); }
                if (done != null) OnUi(done);
            });
            return null;
        }

        /// <summary>The home screen's line for the highlighted game.</summary>
        /// <summary>
        /// (0.99.51) The home screen's line keeps its room: while the next game's figure is worked out the last one stays (it was
        /// emptied and the line taken away, so the parts beside it, players online, moved over and back), and a game with no
        /// figure hides it by fading it (HomeProgressOpacity), not by taking its room away.
        /// </summary>
        private void HomeProgressRefresh()
        {
            // (0.99.65) the line is always up (it used to wait for the first figure, then pop in): "Story ~…%" while the
            // figure's being worked out, then the figure. A game with nothing to go by (an app, no achievements, no
            // HowLongToBeat time) has no line at all; while the next game's figure is worked out the line stays as it was
            // (shown or not), so it doesn't flash up for a game that turns out to have none
            string t = homeProgressText;
            int pct = homeProgressPct;
            bool none = homeProgressNone;
            try
            {
                var g = settings.HomeProgress && IsFullscreen ? SelectedGameNow() : null;
                if (g == null) { t = StoryWaitText; pct = 0; }
                else
                {
                    bool known;
                    lock (progressCache) known = progressCache.ContainsKey(g.Id) && !progressLoading.Contains(g.Id);
                    var r = ProgressFor(g, HomeProgressRefresh);
                    if (r != null) { t = r.Short; pct = r.Percent; none = false; }
                    else if (known) { t = StoryWaitText; pct = 0; none = true; }
                    else { t = StoryWaitText; pct = 0; }
                }
            }
            catch (Exception) { }
            if (t == homeProgressText && none == homeProgressNone && homeProgressHas) return;
            homeProgressText = t;
            homeProgressPct = pct;
            homeProgressNone = none;
            homeProgressHas = true;
            foreach (string n in new[] { "HomeProgressText", "HomeShowProgress", "HomeProgressBar", "HomeProgressOpacity" }) Notify(n);
        }
        private string StoryWaitText { get { return string.Format(Loc("LOC_KX_StpStory", "Story ~{0}%"), "…"); } }
        private bool homeProgressHas, homeProgressNone = true;   // (not shown until the first figure is in)
        public double HomeProgressOpacity { get { return 1.0; } }   // (0.99.65: never faded away now)

        /// <summary>The game page's line (the page opened, or another game on it).</summary>
        private void DetProgressRefresh()
        {
            string t = "";
            int pct = 0;
            try
            {
                var g = detailsOpen && DetInfoOn("progress") ? SelectedGameNow() : null;
                var r = g != null ? ProgressFor(g, DetProgressRefresh) : null;
                if (r != null) { t = r.Line; pct = r.Percent; }
            }
            catch (Exception) { }
            if (t == detProgressText) return;
            detProgressText = t;
            detProgressPct = pct;
            foreach (string n in new[] { "DetProgressText", "DetInfoShowProgress", "DetProgressBar" }) Notify(n);
        }

        /// <summary>A game stopped (more play time, maybe new achievements): its estimate is worked out again next time.</summary>
        internal void ProgressForget(Guid id)
        {
            lock (progressCache) progressCache.Remove(id);
        }
    }
}
