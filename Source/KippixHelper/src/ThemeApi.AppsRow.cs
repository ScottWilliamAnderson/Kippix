using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Apps in a row of their own (Game cards and layout › Extra rows › Apps; Row › Lower): everything in one Playnite category
    /// ("Apps" unless another name is set: a browser, a terminal, tools) leaves the games row and sits in the first extra row,
    /// the last one used first. Off until that row is switched on.
    ///   - The games row: the one list filter (ApplyListFilter) leaves them out, whichever saved filter is on.
    ///   - The Apps row: every entry in the category that isn't hidden, whatever Playnite's filter says. Playnite can only select
    ///     what's in its list, so while the highlight is in that row the list lets them through (it's faded out then): the
    ///     background, logo, info and the game page follow the app like any other row's tile.
    ///   - Down from the games goes into it, Left / Right along it, A opens it, Up or B goes back to the game you left.
    /// </summary>
    public partial class ThemeApi
    {
        private Guid appsCatId;         // the category with a row of its own now (Guid.Empty: none)
        private bool appsRowOpen;       // the highlight is in the Apps row: Playnite's list lets them through
        private bool appsRowFlipping;   // the list changing because of that: the rows aren't built again for it
        private volatile bool appsInGames;   // what's shown now is apps and nothing else (favourites that are all apps, an "Apps" filter, a search): they stay in the games row
        private bool appsCheckQueued;

        private bool AppsRowWanted
        {
            get { return IsFullscreen && settings.ExtraRows && settings.HomeLayoutMode == 0 && settings.RowDesign == 0 && settings.ExtraRowKeys != null && settings.ExtraRowKeys.Contains("apps"); }
        }

        private string AppsCategoryName { get { return string.IsNullOrWhiteSpace(settings.AppsRowCategory) ? "Apps" : settings.AppsRowCategory.Trim(); } }

        private Guid AppsCategoryId()
        {
            try
            {
                string name = AppsCategoryName;
                var c = api.Database.Categories.FirstOrDefault(x => x != null && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                return c != null ? c.Id : Guid.Empty;
            }
            catch (Exception) { return Guid.Empty; }
        }

        /// <summary>From the list filter: is this one of the apps (so not for the games row)?</summary>
        private bool IsAppsRowGame(Game g)
        {
            return g != null && g.CategoryIds != null && g.CategoryIds.Contains(appsCatId);
        }

        /// <summary>From the tick, and whenever the rows are built: the Apps row switched on or off, or its category changed.</summary>
        private void AppsRowTick()
        {
            Guid want = AppsRowWanted ? AppsCategoryId() : Guid.Empty;
            if (want == appsCatId) return;
            appsCatId = want;
            if (want == Guid.Empty) { appsRowOpen = false; appsInGames = false; }
            ApplyListFilter(true);
            AppsRowMergeCheck();
        }

        /// <summary>From Playnite's list each time it's filled again (a saved filter chosen, a search): the check below, once it's done.</summary>
        private void AppsRowListChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (appsCatId == Guid.Empty || appsCheckQueued || ui == null) return;
            if (e != null && e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset) return;
            appsCheckQueued = true;
            // (ahead of the next frame, so a filter that is all apps doesn't show "No games found" first)
            ui.BeginInvoke(new Action(() => { appsCheckQueued = false; AppsRowMergeCheck(); }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>
        /// Taking the apps out must not leave the games row empty: when what Playnite's filter shows is apps and nothing else
        /// (favourites that are all apps, a saved filter for the apps' own category, a search for one of them), they stay in the
        /// games row, and there is no Apps row repeating them under it.
        /// </summary>
        private void AppsRowMergeCheck()
        {
            try
            {
                bool merge = false;
                var view = listFilterView;
                if (appsCatId != Guid.Empty && folderFilter == null && view != null && listWrapFilter != null && ReferenceEquals(view.Filter, listWrapFilter))
                {
                    var orig = listOrigFilter;
                    bool games = false, apps = false;
                    foreach (var o in view.SourceCollection)
                    {
                        if (profileNow != null && !ProfileAllowsId(EntryId(o))) continue;
                        if (orig != null && !orig(o)) continue;
                        if (IsAppsRowGame(FolderEntryGame(o))) apps = true; else { games = true; break; }
                    }
                    merge = apps && !games;
                }
                if (merge == appsInGames) return;
                appsInGames = merge;
                if (view != null) view.Refresh();
                RebuildExtraRowsSoon();
            }
            catch (Exception ex) { logger.Warn("Kippix: apps row: " + ex.Message); }
        }

        /// <summary>The highlight went into or out of the Apps row (GoToExtraRow, LeaveExtraRows): Playnite's list lets the apps in, or not.</summary>
        private void AppsRowSync()
        {
            // (Running now too: a running app's tile is in that row, and Playnite can only select it if it's in its list)
            string at = extraRowAt > 0 && extraRowAt <= ExtraRowsList.Count ? ExtraRowsList[extraRowAt - 1].Key : null;
            bool open = appsCatId != Guid.Empty && (at == "apps" || at == "running");
            if (open == appsRowOpen) return;
            appsRowOpen = open;
            try { appsRowFlipping = true; ApplyListFilter(true); }
            finally { appsRowFlipping = false; }
        }

        /// <summary>
        /// From RebuildExtraRows (its worker thread), once the other rows are worked out: the Apps row goes in first (after
        /// Running now, where there is one), and its apps come out of the other rows.
        /// </summary>
        private void AppsRowBuild(List<Tuple<string, string, List<Game>>> built, List<string> keys, List<Game> all)
        {
            OnUi(AppsRowTick);
            if (!keys.Contains("apps")) return;
            Guid cat = AppsCategoryId();
            if (cat == Guid.Empty) return;
            var apps = all.Where(g => g.CategoryIds != null && g.CategoryIds.Contains(cat))
                          .OrderByDescending(g => LastPlayedNow(g) ?? DateTime.MinValue).ThenBy(g => g.SortingName ?? g.Name).Take(40).ToList();
            if (apps.Count == 0) return;
            var ids = new HashSet<Guid>(apps.Select(g => g.Id));
            // (not out of Running now: an app that's running is still running. It took the terminal out of that row, and the row with it)
            foreach (var b in built) if (b.Item1 != "running") b.Item3.RemoveAll(g => ids.Contains(g.Id));
            built.RemoveAll(b => b.Item3.Count == 0);
            if (appsInGames) return;   // (they are the games row now: not a second time under it)
            built.Insert(built.Count > 0 && built[0].Item1 == "running" ? 1 : 0, Tuple.Create("apps", AppsCategoryName, apps));
        }

        // ---------------- an app's own page ----------------

        /// <summary>
        /// The game page is an app's (an entry in the apps' category, whether or not the Apps row is on): its button reads "Open"
        /// (Views/GameDetails.xaml), and there's no achievements button, How long to beat or Save backups on it.
        /// </summary>
        public bool DetIsApp
        {
            get
            {
                try
                {
                    var g = SelectedGameNow();
                    if (g == null || g.CategoryIds == null || g.CategoryIds.Count == 0) return false;
                    Guid cat = appsCatId != Guid.Empty ? appsCatId : AppsCategoryId();
                    return cat != Guid.Empty && g.CategoryIds.Contains(cat);
                }
                catch (Exception) { return false; }
            }
        }
        /// <summary>"Open" in place of Playnite's "Play" (an app that isn't installed keeps Playnite's "Install").</summary>
        public bool DetPlayOpens { get { var g = SelectedGameNow(); return g != null && g.IsInstalled && DetIsApp; } }
        public bool DetAchButtonShown { get { return settings.DetShowAchButton && !DetIsApp; } }

        private bool appsPageWas;

        /// <summary>From GameSelectedNow: another game is selected.</summary>
        private void AppsPageSelected()
        {
            bool app = DetIsApp;
            if (app == appsPageWas) return;
            appsPageWas = app;
            foreach (string n in new[] { "DetIsApp", "DetPlayOpens", "DetAchButtonShown", "ShowDetSaves", "ShowDetHltb", "ShowDetAch", "DetInfoShowHltb" }) Notify(n);
        }

        /// <summary>The Extra rows page: under the Apps switch, which category it takes (the keyboard changes it).</summary>
        private void AddAppsRowChoice()
        {
            int at = -1;
            for (int i = 0; i < ExtraRowChoices.Count; i++) if (ExtraRowChoices[i].Key == "apps") { at = i; break; }
            if (at < 0) return;
            ExtraRowChoices[at].SetSub(Loc("LOC_KX_RowAppsSub", "A row of their own, first under the games, and out of the games row"));
            var row = new PickRow { Key = "appscat", Icon = "tag", Indent = 28, Text = AppsCategoryText(), Sub = AppsCategorySub() };
            row.Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_RowAppsAsk", "The Playnite category whose entries are your apps"), AppsCategoryName, text =>
            {
                settings.AppsRowCategory = (text ?? "").Trim();
                SaveSettings();
                row.SetText(AppsCategoryText());
                row.SetSub(AppsCategorySub());
                RebuildExtraRowsSoon();
            }, null, null, 60));
            ExtraRowChoices.Insert(at + 1, row);
        }

        private string AppsCategoryText() { return string.Format(Loc("LOC_KX_RowAppsCategory", "Category: {0}"), AppsCategoryName); }

        private string AppsCategorySub()
        {
            return AppsCategoryId() == Guid.Empty ? Loc("LOC_KX_RowAppsNoCategory", "Playnite has no category with this name yet")
                                                  : Loc("LOC_KX_RowAppsCategorySub", "Everything in this Playnite category counts as an app");
        }
    }
}
