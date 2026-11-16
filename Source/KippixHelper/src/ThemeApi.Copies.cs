using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace KippixHelper
{
    /// <summary>
    /// One tile per game (Sort and show › SHOW): a game that's in the library more than once (bought on two stores, an old and
    /// a new package of it, a console copy and a PC one) shows as one tile.
    ///   - Copies are the games with the same name, read loosely: case, punctuation, "The", ™ and a trailing edition that's
    ///     the same game ("Deluxe Edition", "2025 Edition", "for Windows") don't count. A remaster keeps its own tile.
    ///   - The tile is the copy you'd start: the installed one, else the one played last (then the most played, then a
    ///     store's copy before one added by hand). Only among the copies the filter that's on shows: filter by a console and
    ///     its copy is the tile, even if the PC one is installed.
    ///   - Nothing in the library is changed. Play time and last played on the home screen and the game page are those of
    ///     all the copies together (also the ones hidden in Playnite, which is how most people dealt with a second copy).
    ///   - The game's More menu lists the other copies: show one of them instead, or give each its own tile again.
    /// </summary>
    public partial class ThemeApi
    {
        private sealed class CopyGroup
        {
            public List<Guid> Ids;      // the copy to show first
            public ulong Seconds;       // played, all copies
            public DateTime? Last;      // last played, any copy
        }

        // (read from the list's filter and from the threads that fill the other rows: replaced whole, never changed)
        private volatile Dictionary<Guid, CopyGroup> copyGroups;
        private volatile Dictionary<Guid, object> copyEntries;   // Playnite's list entry of each game
        private string copySig;
        private bool copiesHooked, copiesSoon;
        private DispatcherTimer copiesTimer;
        private string copyTimeText, copyLastText;

        private static readonly string[][] CopyTails =
        {
            new[] { "game", "of", "the", "year", "edition" }, new[] { "game", "of", "the", "year" }, new[] { "goty", "edition" }, new[] { "goty" },
            new[] { "digital", "deluxe", "edition" }, new[] { "deluxe", "edition" }, new[] { "standard", "edition" }, new[] { "gold", "edition" },
            new[] { "ultimate", "edition" }, new[] { "premium", "edition" }, new[] { "complete", "edition" },
            new[] { "for", "windows", "10" }, new[] { "for", "windows" }, new[] { "windows", "10" }, new[] { "windows", "edition" },
            new[] { "pc", "edition" }, new[] { "steam", "edition" }, new[] { "pc" }
        };

        /// <summary>A game's name as two copies of it share it; null for no name.</summary>
        internal static string CopyKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var sb = new StringBuilder();
            foreach (char c in name.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c == '™' || c == '®' || c == '©' || c == '\'' || c == '’') continue;
                if (c == '&') { sb.Append(" and "); continue; }
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            var words = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count > 1 && words[0] == "the") words.RemoveAt(0);
            bool again = true;
            while (again && words.Count > 1)
            {
                again = false;
                int year;
                // "2025 Edition"
                if (words.Count > 2 && words[words.Count - 1] == "edition" && words[words.Count - 2].Length == 4 && int.TryParse(words[words.Count - 2], out year) && year > 1970)
                {
                    words.RemoveRange(words.Count - 2, 2);
                    again = true;
                    continue;
                }
                foreach (var tail in CopyTails)
                {
                    if (words.Count <= tail.Length) continue;
                    bool same = true;
                    for (int i = 0; i < tail.Length && same; i++) same = words[words.Count - tail.Length + i] == tail[i];
                    if (!same) continue;
                    words.RemoveRange(words.Count - tail.Length, tail.Length);
                    again = true;
                    break;
                }
            }
            return words.Count == 0 ? null : string.Join(" ", words.ToArray());
        }

        /// <summary>Every game that has another copy, with its copies (the one to show first). Empty while the setting is off.</summary>
        private Dictionary<Guid, CopyGroup> CopyGroupsNow()
        {
            var d = copyGroups;
            if (d != null) return d;
            d = new Dictionary<Guid, CopyGroup>();
            if (settings.OneTilePerGame)
            {
                try
                {
                    var apart = new HashSet<string>(settings.CopySplits ?? new List<string>());
                    var picks = new HashSet<string>(settings.CopyPicks ?? new List<string>());
                    var byName = new Dictionary<string, List<Game>>();
                    foreach (var g in api.Database.Games.ToList())
                    {
                        if (g == null || apart.Contains(g.Id.ToString())) continue;
                        string key = CopyKey(g.Name);
                        if (key == null) continue;
                        List<Game> l;
                        if (!byName.TryGetValue(key, out l)) byName[key] = l = new List<Game>();
                        l.Add(g);
                    }
                    foreach (var l in byName.Values)
                    {
                        if (l.Count < 2) continue;
                        var best = l.OrderByDescending(g => picks.Contains(g.Id.ToString()))
                                    .ThenBy(g => g.Hidden)
                                    .ThenByDescending(g => g.IsInstalled)
                                    .ThenByDescending(g => g.LastActivity ?? DateTime.MinValue)
                                    .ThenByDescending(g => g.Playtime)
                                    .ThenByDescending(g => g.PluginId != Guid.Empty)
                                    .ThenBy(g => g.Added ?? DateTime.MaxValue)
                                    .ThenBy(g => g.Id).ToList();
                        var grp = new CopyGroup { Ids = best.Select(g => g.Id).ToList() };
                        foreach (var g in best)
                        {
                            grp.Seconds += g.Playtime;
                            if (g.LastActivity.HasValue && (!grp.Last.HasValue || g.LastActivity.Value > grp.Last.Value)) grp.Last = g.LastActivity;
                            d[g.Id] = grp;
                        }
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: one tile per game: " + ex.Message); }
            }
            copyGroups = d;
            return d;
        }

        /// <summary>For the rows that aren't Playnite's list (extra rows, Tiles, Up next, search): another copy of this game is the one shown.</summary>
        private bool CopyBehind(Guid id)
        {
            if (!settings.OneTilePerGame) return false;
            try
            {
                CopyGroup grp;
                if (!CopyGroupsNow().TryGetValue(id, out grp)) return false;
                foreach (var other in grp.Ids)
                {
                    if (other == id) return false;
                    var o = api.Database.Games.Get(other);
                    if (o != null && !o.Hidden && ProfileAllowsGame(o)) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>
        /// Playnite's list: a better copy of this entry's game is in the list too (<paramref name="rest"/>: everything else the
        /// list's filter asks). Runs inside Playnite's own list updates: must never throw.
        /// </summary>
        private bool CopyBehindInList(object entry, Predicate<object> rest)
        {
            if (!settings.OneTilePerGame) return false;
            try
            {
                Guid id = EntryId(entry);
                CopyGroup grp;
                if (id == Guid.Empty || !CopyGroupsNow().TryGetValue(id, out grp)) return false;
                var entries = copyEntries;
                if (entries == null)
                {
                    entries = new Dictionary<Guid, object>();
                    var view = listFilterView;
                    if (view != null)
                        foreach (var o in view.SourceCollection)
                        {
                            Guid eid = EntryId(o);
                            if (eid != Guid.Empty) entries[eid] = o;
                        }
                    copyEntries = entries;
                }
                foreach (var other in grp.Ids)
                {
                    if (other == id) return false;
                    object e;
                    if (entries.TryGetValue(other, out e) && rest(e)) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Playnite's list (a new one, or its games changed): its entries are looked up afresh.</summary>
        private void CopyListSeen(ListCollectionView view)
        {
            copyEntries = null;
            var src = view != null ? view.SourceCollection as System.Collections.Specialized.INotifyCollectionChanged : null;
            if (src != null) src.CollectionChanged += (s, e) => { copyEntries = null; };
        }

        // ---------------- keeping up with the library ----------------

        /// <summary>A game added, removed, installed, played, renamed or hidden can change which copy shows (hooked once, from then on).</summary>
        private void CopiesHook()
        {
            if (copiesHooked || !settings.OneTilePerGame) return;
            copiesHooked = true;
            try
            {
                api.Database.Games.ItemCollectionChanged += (s, e) => CopiesChangedSoon();
                api.Database.Games.ItemUpdated += (s, e) => CopiesChangedSoon();
            }
            catch (Exception ex) { logger.Warn("Kippix: one tile per game: " + ex.Message); }
        }

        private void CopiesChangedSoon()
        {
            // (a metadata download updates games by the hundred: looked at once, half a second after the last of them)
            if (!settings.OneTilePerGame || copiesSoon || ui == null) return;
            copiesSoon = true;
            ui.BeginInvoke(new Action(() =>
            {
                if (copiesTimer == null)
                {
                    copiesTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
                    copiesTimer.Tick += (s, e) => { copiesTimer.Stop(); copiesSoon = false; CopiesAgain(false); };
                }
                copiesTimer.Stop();
                copiesTimer.Start();
            }), DispatcherPriority.Background);
        }

        /// <summary>Works the copies out again; the lists are only gone over when which copy shows has changed.</summary>
        private void CopiesAgain(bool force)
        {
            try
            {
                copyGroups = null;
                var d = CopyGroupsNow();
                var sb = new StringBuilder();
                var seen = new HashSet<CopyGroup>();
                foreach (var grp in d.Values)
                {
                    if (!seen.Add(grp)) continue;
                    foreach (var id in grp.Ids) sb.Append(id.ToString("N")).Append(',');
                    sb.Append(';');
                }
                string sig = sb.ToString();
                bool changed = sig != copySig;
                copySig = sig;
                if (force || changed)
                {
                    copyEntries = null;
                    ApplyListFilter(true);
                    try { RebuildExtraRowsSoon(); if (IsBoardMode) BoardRefreshSoon(); } catch (Exception) { }
                }
                CopySelected(SelectedGameNow());
            }
            catch (Exception ex) { logger.Warn("Kippix: one tile per game: " + ex.Message); }
        }

        private void SetOneTilePerGame(bool on)
        {
            settings.OneTilePerGame = on;
            SaveSettings();
            PlaySound("move");
            CopiesHook();
            CopiesAgain(true);
            GRowsAgain("onetile");
        }

        // ---------------- play time and last played: all the copies together ----------------

        /// <summary>Play time of all the copies of the highlighted game (shown in place of the one copy's when they differ).</summary>
        public string CopyTimeText { get { return copyTimeText ?? ""; } }
        public bool CopyTimeShown { get { return copyTimeText != null; } }
        public bool CopyOwnTimeShown { get { return copyTimeText == null; } }
        /// <summary>Last played, whichever copy that was with.</summary>
        public string CopyLastText { get { return copyLastText ?? ""; } }
        public bool CopyLastShown { get { return copyLastText != null; } }
        public bool CopyOwnLastShown { get { return copyLastText == null; } }

        private void CopySelected(Game g)
        {
            string time = null, last = null;
            try
            {
                CopyGroup grp;
                if (g != null && settings.OneTilePerGame && CopyGroupsNow().TryGetValue(g.Id, out grp))
                {
                    if (grp.Seconds != g.Playtime)
                        time = string.Format(Loc("LOC_KX_CopyTime", "{0}  ·  {1} copies"), CopyPlaytimeText(grp.Seconds), grp.Ids.Count);
                    if (grp.Last.HasValue && grp.Last != g.LastActivity) last = CopyLastPlayedText(grp.Last.Value);
                }
            }
            catch (Exception) { }
            if (time == copyTimeText && last == copyLastText) return;
            copyTimeText = time;
            copyLastText = last;
            foreach (string n in new[] { "CopyTimeText", "CopyTimeShown", "CopyOwnTimeShown", "CopyLastText", "CopyLastShown", "CopyOwnLastShown" }) Notify(n);
        }

        /// <summary>In Playnite's own words (its converters, the ones the theme's own lines use).</summary>
        private string CopyPlaytimeText(ulong seconds)
        {
            string s = CopyConverted("PlayTimeToStringConverter", seconds);
            return !string.IsNullOrEmpty(s) ? s : HoursShort(seconds / 3600.0);
        }

        private string CopyLastPlayedText(DateTime when)
        {
            string s = CopyConverted("DateTimeToLastPlayedConverter", (DateTime?)when);
            return !string.IsNullOrEmpty(s) ? s : when.ToString("d", CultureInfo.CurrentCulture);
        }

        private static string CopyConverted(string converter, object value)
        {
            try
            {
                var c = Application.Current != null ? Application.Current.TryFindResource(converter) as IValueConverter : null;
                return c != null ? c.Convert(value, typeof(string), null, CultureInfo.CurrentCulture) as string : null;
            }
            catch (Exception) { return null; }
        }

        // ---------------- the game's More menu: its other copies ----------------

        /// <summary>Where a copy is from: its store, else the system it's for.</summary>
        private string CopyFrom(Game g)
        {
            try
            {
                if (g.Source != null && !string.IsNullOrEmpty(g.Source.Name)) return g.Source.Name;
                if (g.PluginId != Guid.Empty)
                {
                    var p = api.Addons.Plugins.OfType<LibraryPlugin>().FirstOrDefault(x => x.Id == g.PluginId);
                    if (p != null && !string.IsNullOrEmpty(p.Name)) return p.Name;
                }
                var system = g.Platforms != null ? g.Platforms.FirstOrDefault() : null;
                if (system != null && !string.IsNullOrEmpty(system.Name)) return system.Name;
            }
            catch (Exception) { }
            return Loc("LOC_KX_CopyOwn", "Added by hand");
        }

        private void AddCopyRows(Game g)
        {
            if (g == null || !settings.OneTilePerGame) return;
            string key = CopyKey(g.Name);
            bool apart = settings.CopySplits != null && settings.CopySplits.Contains(g.Id.ToString());
            CopyGroup grp;
            CopyGroupsNow().TryGetValue(g.Id, out grp);
            if (grp == null && !apart) return;
            GameMoreRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecCopies", "YOUR COPIES OF THIS GAME") });
            if (apart)
            {
                GameMoreRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_CopyTogether", "One tile for all its copies again"), Icon = "layout", Key = "copytogether",
                    Command = new RelayCommand(_ =>
                    {
                        var same = new HashSet<string>(api.Database.Games.Where(o => o != null && CopyKey(o.Name) == key).Select(o => o.Id.ToString()));
                        settings.CopySplits.RemoveAll(same.Contains);
                        CopyChoiceMade(Guid.Empty);
                    })
                });
                return;
            }
            var ids = new HashSet<string>(grp.Ids.Select(i => i.ToString()));
            foreach (var otherId in grp.Ids)
            {
                if (otherId == g.Id) continue;
                Game o = null;
                try { o = api.Database.Games.Get(otherId); } catch (Exception) { }
                if (o == null) continue;
                var parts = new List<string> { o.IsInstalled ? Loc("LOC_KX_CopyInstalled", "Installed") : Loc("LOC_KX_CopyNotInstalled", "Not installed") };
                if (o.Playtime > 0) parts.Add(CopyPlaytimeText(o.Playtime));
                if (o.LastActivity.HasValue) parts.Add(CopyLastPlayedText(o.LastActivity.Value));
                if (o.Hidden) parts.Add(Loc("LOC_KX_CopyHidden", "hidden in Playnite"));
                string sub = string.Join("  ·  ", parts.ToArray());
                var show = o;
                if (o.Hidden) GameMoreRows.Add(new PickRow { IsInfo = true, Icon = "info", Text = string.Format(Loc("LOC_KX_CopyOther", "{0} copy"), CopyFrom(o)), Sub = sub });
                else
                    GameMoreRows.Add(new PickRow
                    {
                        Text = string.Format(Loc("LOC_KX_CopyShow", "Show the {0} copy instead"), CopyFrom(o)), Sub = sub, Icon = o.PluginId != Guid.Empty ? "store" : "controller", Key = "copy:" + o.Id,
                        Command = new RelayCommand(_ =>
                        {
                            if (settings.CopyPicks == null) settings.CopyPicks = new List<string>();
                            settings.CopyPicks.RemoveAll(ids.Contains);
                            settings.CopyPicks.Add(show.Id.ToString());
                            CopyChoiceMade(show.Id);
                        })
                    });
            }
            if (settings.CopyPicks != null && settings.CopyPicks.Any(ids.Contains))
                GameMoreRows.Add(new PickRow
                {
                    Text = Loc("LOC_KX_CopyAuto", "Choose the copy for me again"), Sub = Loc("LOC_KX_CopyAutoSub", "The installed one, else the one you played last"), Icon = "refresh", Key = "copyauto",
                    Command = new RelayCommand(_ =>
                    {
                        settings.CopyPicks.RemoveAll(ids.Contains);
                        // (the page goes to the copy that's the tile now)
                        CopyChoiceMade(Guid.Empty);
                        CopyGroup now;
                        if (CopyGroupsNow().TryGetValue(g.Id, out now) && now.Ids[0] != g.Id) CopyGoTo(now.Ids[0]);
                    })
                });
            GameMoreRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_CopyApart", "Give each copy its own tile"), Sub = Loc("LOC_KX_CopyApartSub", "For two different games with the same name"), Icon = "grid", Key = "copyapart",
                Command = new RelayCommand(_ =>
                {
                    if (settings.CopySplits == null) settings.CopySplits = new List<string>();
                    foreach (string id in ids) if (!settings.CopySplits.Contains(id)) settings.CopySplits.Add(id);
                    if (settings.CopyPicks != null) settings.CopyPicks.RemoveAll(ids.Contains);
                    CopyChoiceMade(Guid.Empty);
                })
            });
        }

        /// <summary>A choice from the More menu: saved, the lists gone over, and (a copy chosen) the game page moves to that copy.</summary>
        private void CopyChoiceMade(Guid showNow)
        {
            SaveSettings();
            PlaySound("select");
            ClosePanels();
            CopiesAgain(true);
            if (showNow != Guid.Empty) CopyGoTo(showNow);
        }

        private void CopyGoTo(Guid id)
        {
            ui.BeginInvoke(new Action(() =>
            {
                try { api.MainView.SelectGame(id); } catch (Exception) { }
            }), DispatcherPriority.Loaded);
        }
    }
}
