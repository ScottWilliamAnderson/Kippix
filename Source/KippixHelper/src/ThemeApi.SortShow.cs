using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Sort and show (the top bar shortcut; a code-built options page, "sortshow"): Playnite's sort order for the games on the
    /// home screen, and Installed only, without opening Playnite's own filter panel.
    ///   - Sort by sets Playnite's own sort (the one its filter panel shows): a saved filter that carries a sort of its own
    ///     puts that one back when it's chosen. Choosing the order that's already set turns it round.
    ///   - Installed only is Kippix's, on top of whatever filter is on (the one list filter, ApplyListFilter): it stays on from
    ///     one saved filter to the next, and the top bar says so. A folder keeps its own Installed games choice.
    /// </summary>
    public partial class ThemeApi
    {
        // (the orders offered, each with the direction it starts in)
        private static readonly SortOrder[] SortShowOrders = { SortOrder.Name, SortOrder.LastActivity, SortOrder.Playtime, SortOrder.UserScore, SortOrder.ReleaseDate, SortOrder.Added };

        /// <summary>The top bar's "Installed only" mark.</summary>
        public bool InstalledOnlyOn { get { return settings.InstalledOnly; } }

        private void OpenSortShow()
        {
            if (openPanel == "options" && optionsPage == "sortshow") { ClosePanels(); return; }
            PlaySound("select");
            if (openPanel != "options") TogglePanel("options");
            if (openPanel == "options") SetOptionsPage("sortshow");   // (not when the profile's PIN was asked for instead)
        }

        private string SortShowName(SortOrder o)
        {
            switch (o)
            {
                case SortOrder.LastActivity: return Loc("LOC_KX_SortShowPlayed", "Last played");
                case SortOrder.Playtime: return Loc("LOC_KX_SortShowMost", "Most played");
                case SortOrder.UserScore: return Loc("LOC_KX_SortShowScore", "Your rating");
                case SortOrder.ReleaseDate: return Loc("LOC_KX_SortShowRelease", "Release date");
                case SortOrder.Added: return Loc("LOC_KX_SortShowAdded", "Recently added");
                default: return Loc("LOC_KX_SortShowName", "Name");
            }
        }

        /// <summary>Which way round the order that's on is, in that order's own words ("A to Z", "Highest first", "Newest first").</summary>
        private string SortShowWay(SortOrder o, bool down)
        {
            switch (o)
            {
                case SortOrder.Name: return down ? Loc("LOC_KX_SortWayZA", "Z to A") : Loc("LOC_KX_SortWayAZ", "A to Z");
                case SortOrder.LastActivity: return down ? Loc("LOC_KX_SortWayRecent", "Most recent first") : Loc("LOC_KX_SortWayLongAgo", "Longest ago first");
                case SortOrder.Playtime: return down ? Loc("LOC_KX_SortWayMost", "Most played first") : Loc("LOC_KX_SortWayLeast", "Least played first");
                case SortOrder.UserScore: return down ? Loc("LOC_KX_SortWayHighest", "Highest first") : Loc("LOC_KX_SortWayLowest", "Lowest first");
                default: return down ? Loc("LOC_KX_SortWayNewest", "Newest first") : Loc("LOC_KX_SortWayOldest", "Oldest first");
            }
        }

        private void BuildSortShowRows(List<PickRow> rows)
        {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SortShowShow", "SHOW") });
            rows.Add(new PickRow
            {
                Key = "installedonly", Icon = "download", IsSwitch = true, IsSelected = settings.InstalledOnly, Text = Loc("LOC_KX_InstalledOnly", "Installed only"),
                Sub = Loc("LOC_KX_InstalledOnlySub", "Stays on whichever filter you switch to"),
                Command = new RelayCommand(_ => SetInstalledOnly(!settings.InstalledOnly))
            });
            rows.Add(new PickRow
            {
                Key = "onetile", Icon = "layout", IsSwitch = true, IsSelected = settings.OneTilePerGame, Text = Loc("LOC_KX_OneTile", "One tile per game"),
                Sub = Loc("LOC_KX_OneTileSub", "A game you have more than once shows once: the installed copy, else the one you played last"),
                Command = new RelayCommand(_ => SetOneTilePerGame(!settings.OneTilePerGame))
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SortShowSort", "SORT BY") });
            SortOrder now = SortOrder.Name;
            bool down = false;
            try { now = api.MainView.SortOrder; down = api.MainView.SortOrderDirection == SortOrderDirection.Descending; } catch (Exception) { }
            foreach (var o in SortShowOrders)
            {
                var order = o;
                bool on = o == now;
                rows.Add(new PickRow
                {
                    Key = "sort:" + o, Icon = "sort", IsSelected = on, Text = SortShowName(o),
                    Sub = on ? SortShowWay(o, down) + "  ·  " + Loc("LOC_KX_SortShowTurn", "press again to turn it round") : null,
                    Command = new RelayCommand(_ => SetSortShow(order))
                });
            }
            if (!SortShowOrders.Contains(now))
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Text = string.Format(Loc("LOC_KX_SortShowOther", "Sorted by {0} now (set in Playnite's filters)"), now) });
            if (FolderMode) rows.Add(new PickRow { IsInfo = true, Icon = "info", Text = Loc("LOC_KX_SortShowFolders", "Folders have their own Sort by and Installed games choices") });
        }

        /// <summary>Playnite's sort: that order (starting A to Z for names, highest or newest first for the rest); the order already set turns round.</summary>
        private void SetSortShow(SortOrder order)
        {
            try
            {
                bool same = api.MainView.SortOrder == order;
                var dir = same ? (api.MainView.SortOrderDirection == SortOrderDirection.Descending ? SortOrderDirection.Ascending : SortOrderDirection.Descending)
                               : order == SortOrder.Name ? SortOrderDirection.Ascending : SortOrderDirection.Descending;
                if (!same) SetPlayniteSortOrder(order);
                api.MainView.SortOrderDirection = dir;
                PlaySound("move");
            }
            catch (Exception ex) { logger.Warn("Kippix: sort: " + ex.Message); }
            GRowsAgain("sort:" + order);
        }

        /// <summary>
        /// The SDK only lets an add-on read the sort order (and set its direction). Playnite's own main view object can set it,
        /// so that's asked first; else the fullscreen view settings behind it.
        /// </summary>
        private void SetPlayniteSortOrder(SortOrder order)
        {
            object view = api.MainView;
            var p = view.GetType().GetProperty("SortOrder", BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(view, order, null); return; }
            var w = System.Windows.Application.Current != null ? System.Windows.Application.Current.MainWindow : null;
            object vm = w != null ? w.DataContext : null;
            object app = vm != null ? Prop(vm, "AppSettings") : null;
            object fs = app != null ? Prop(app, "Fullscreen") : null;
            object vs = fs != null ? Prop(fs, "ViewSettings") : null;
            var sp = vs != null ? vs.GetType().GetProperty("SortingOrder", BindingFlags.Public | BindingFlags.Instance) : null;
            if (sp == null || !sp.CanWrite) throw new InvalidOperationException("this Playnite doesn't let the sort order be set");
            sp.SetValue(vs, order, null);
        }

        // ---------------- Installed only ----------------

        private bool installedOnlyHooked;

        private void SetInstalledOnly(bool on)
        {
            settings.InstalledOnly = on;
            SaveSettings();
            PlaySound("move");
            Notify("InstalledOnlyOn");
            InstalledOnlyHook();
            ApplyListFilter(true);
            GRowsAgain("installedonly");
        }

        /// <summary>A game installed or uninstalled while Installed only is on: the list is looked at again (once, from then on).</summary>
        private void InstalledOnlyHook()
        {
            if (installedOnlyHooked || !settings.InstalledOnly) return;
            installedOnlyHooked = true;
            try
            {
                api.Database.Games.ItemUpdated += (s, e) =>
                {
                    if (!settings.InstalledOnly || e == null || e.UpdatedItems == null) return;
                    if (e.UpdatedItems.Any(u => u.OldData != null && u.NewData != null && u.OldData.IsInstalled != u.NewData.IsInstalled)) OnUi(() => ApplyListFilter(true));
                };
            }
            catch (Exception ex) { logger.Warn("Kippix: installed only: " + ex.Message); }
        }
    }
}
