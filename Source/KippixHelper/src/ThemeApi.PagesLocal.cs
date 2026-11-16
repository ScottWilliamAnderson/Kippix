using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>Pages built from your own library: collections, pick for me, storage and the year in review.</summary>
    public partial class ThemeApi
    {
        // =====================================================================
        // Collections: your own shelves of games (any of them can be a home row)
        // =====================================================================

        private GameShelf ShelfById(string id)
        {
            return settings.Collections == null ? null : settings.Collections.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>A shelf's games that still exist (any thread).</summary>
        private List<Game> ShelfGames(GameShelf shelf)
        {
            var list = new List<Game>();
            foreach (string s in shelf.Games.ToList())
            {
                Guid id;
                if (!Guid.TryParse(s, out id)) continue;
                var g = api.Database.Games.Get(id);
                if (g != null) list.Add(g);
            }
            return list;
        }

        private bool ShelfOnHome(GameShelf shelf)
        {
            return settings.ExtraRows && settings.ExtraRowKeys != null && settings.ExtraRowKeys.Contains("shelf:" + shelf.Id);
        }

        private void BuildCollectionsPage()
        {
            PageSub = Loc("LOC_KX_PageCollectionsSub", "Your own shelves of games, any of them can be a home row");
            AddPageChip(Loc("LOC_KX_NewCollection", "New collection"), "add", () => NewCollection(null, c => { if (pageKey == "collections") { pageQuietOpen = true; OpenPage("collections", "", false); } }));
            if (settings.Collections.Count > 0) AddPageHeader(Loc("LOC_KX_YourCollections", "YOUR COLLECTIONS"));
            int onHomeCount = 0;
            foreach (var c in settings.Collections)
            {
                var shelf = c;
                var games = ShelfGames(c).Where(ProfileAllowsGame).ToList();   // (what this profile may see)
                int n = games.Count;
                bool home = ShelfOnHome(c);
                if (home) onHomeCount++;
                var first = games.FirstOrDefault();
                PageRows.Add(new PageRow
                {
                    Text = c.Name, Icon = "shelf", Key = c.Id, ImagePath = first != null ? CoverOf(first.Id) : null,
                    Sub = (n == 1 ? Loc("LOC_KX_OneGame", "1 game") : string.Format(Loc("LOC_KX_NGames", "{0} games"), n))
                        + (home ? "   ·   " + Loc("LOC_KX_ShownOnHome", "a row on the home screen") : ""),
                    Right = Loc("LOC_KX_HubShelfOpen", "Open · settings"),
                    Command = new RelayCommand(_ => OpenPage("shelf", shelf.Id, true))
                });
            }
            if (settings.Collections.Count > 0)
                PageStats = new List<PageStat>
                {
                    new PageStat { Value = settings.Collections.Count.ToString(CultureInfo.CurrentCulture), Label = Loc("LOC_KX_HubShelves", "collections") },
                    new PageStat { Value = onHomeCount.ToString(CultureInfo.CurrentCulture), Label = Loc("LOC_KX_HubShelvesHome", "on the home screen") }
                };
            LoadPageImages();
            SetPageStatus(settings.Collections.Count == 0
                ? Loc("LOC_KX_CollectionsEmpty", "No collections yet. Make one here, or add a game to one from its page: ⋯ More › Add to a collection.")
                : "");
        }

        private void NewCollection(Game first, Action<GameShelf> then)
        {
            OpenKeyboard(Loc("LOC_KX_NewCollectionPrompt", "Name the new collection"), "", typed =>
            {
                if (string.IsNullOrWhiteSpace(typed)) return;
                var c = new GameShelf { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Name = typed.Trim() };
                if (first != null) c.Games.Add(first.Id.ToString());
                settings.Collections.Add(c);
                SaveSettings();
                if (then != null) then(c);
            }, null, null, 40);
        }

        private void BuildShelfPage(string id)
        {
            var shelf = ShelfById(id);
            if (shelf == null) { PageTitle = PageName("collections"); SetPageStatus(Loc("LOC_KX_CollectionGone", "This collection was deleted.")); return; }
            PageTitle = shelf.Name;
            var games = ShelfGames(shelf).Where(ProfileAllowsGame).ToList();   // (what this profile may see)
            PageSub = games.Count == 1 ? Loc("LOC_KX_OneGame", "1 game") : string.Format(Loc("LOC_KX_NGames", "{0} games"), games.Count);
            PageHint = Loc("LOC_KX_ShelfHint", "X  Take the game off this collection");
            bool onHome = ShelfOnHome(shelf);
            var home = AddPageChip(ShelfHomeChipText(onHome), "layout", () => ToggleShelfOnHome(shelf));
            home.IsSelected = onHome;
            AddPageChip(Loc("LOC_KX_Rename", "Rename"), "note", () =>
            {
                OpenKeyboard(Loc("LOC_KX_RenameCollectionPrompt", "A new name for this collection"), shelf.Name, typed =>
                {
                    if (string.IsNullOrWhiteSpace(typed)) return;
                    shelf.Name = typed.Trim();
                    SaveSettings();
                    RebuildExtraRowsSoon();
                    PageTitle = shelf.Name;
                    Notify("PageTitle");
                }, null, null, 40);
            });
            AddPageChip(Loc("LOC_KX_DeleteCollection", "Delete"), "trash", () =>
                Confirm(Loc("LOC_KX_DeleteCollectionTitle", "Delete this collection?"),
                        string.Format(Loc("LOC_KX_DeleteCollectionText", "“{0}” goes, the games stay in your library."), shelf.Name),
                        Loc("LOC_KX_DeleteCollection", "Delete"), () =>
                        {
                            settings.Collections.Remove(shelf);
                            if (settings.ExtraRowKeys != null) settings.ExtraRowKeys.Remove("shelf:" + shelf.Id);
                            SaveSettings();
                            RebuildExtraRowsSoon();
                            if (pageStack.Count > 0) pageStack.Pop();
                            OpenPage("collections", "", false);
                        }));
            foreach (var g in games)
            {
                var gid = g.Id;
                PageTiles.Add(new PageTile
                {
                    Name = g.Name, GameId = gid, CoverPath = CoverOf(gid),
                    Sub = g.Playtime > 0 ? HoursShort(g.Playtime / 3600.0) : Loc("LOC_KX_NotPlayed", "Not played"),
                    Command = new RelayCommand(_ => OpenGameFromPage(gid))
                });
            }
            SetPageStatus(games.Count == 0 ? Loc("LOC_KX_ShelfEmpty", "Nothing here yet. On a game's page: ⋯ More › Add to a collection.") : "");
            LoadPageImages();
        }

        /// <summary>The collection's "Add to home" button: what it does, or that it's on the home screen.</summary>
        private string ShelfHomeChipText(bool onHome)
        {
            if (!onHome) return Loc("LOC_KX_AddToHome", "Add to home");
            return settings.HomeLayoutMode == 0 && settings.RowDesign == 0 ? Loc("LOC_KX_OnHomeChip", "On your home screen ✓")
                                                : Loc("LOC_KX_OnHomeChipRowOnly", "Added to home ✓ (shows in the Row layout, Lower design)");
        }

        private void ToggleShelfOnHome(GameShelf shelf)
        {
            if (settings.ExtraRowKeys == null) settings.ExtraRowKeys = new List<string>();
            string key = "shelf:" + shelf.Id;
            bool on = ShelfOnHome(shelf);
            if (on) settings.ExtraRowKeys.Remove(key);
            else
            {
                if (!settings.ExtraRowKeys.Contains(key)) settings.ExtraRowKeys.Add(key);
                settings.ExtraRows = true;   // the extra rows have to be on for it to show
            }
            SaveSettings();
            PlaySound("move");
            NotifyHomeRows();
            RebuildExtraRowsSoon();
            bool nowOn = ShelfOnHome(shelf);
            var chip = PageChips.FirstOrDefault(c => c.Icon == "layout");
            if (chip != null) { chip.IsSelected = nowOn; chip.SetText(ShelfHomeChipText(nowOn)); }
            SetPageStatus(!on && (settings.HomeLayoutMode != 0 || settings.RowDesign != 0) ? Loc("LOC_KX_ShelfRowNeedsRow2", "Extra rows show when the home screen is set to Row with the Lower design (Settings & Customisation › Home settings › Game cards and layout).") : PageStatus);
        }

        private void RemoveFromShelf(string id, PageTile t)
        {
            var shelf = ShelfById(id);
            if (shelf == null) return;
            shelf.Games.Remove(t.GameId.ToString());
            SaveSettings();
            RebuildExtraRowsSoon();
            PlaySound("back");
            int at = PageTiles.IndexOf(t);
            PageTiles.Remove(t);
            NotifyPageLists();
            if (PageTiles.Count > 0) FocusPageSoon(PageTiles[Math.Min(Math.Max(at, 0), PageTiles.Count - 1)]);
            else { SetPageStatus(Loc("LOC_KX_ShelfEmpty", "Nothing here yet. On a game's page: ⋯ More › Add to a collection.")); FocusPageSoon(null); }
        }

        /// <summary>The game's More menu: which collections it's on (A adds / takes it off), and a new one.</summary>
        /// <summary>
        /// Puts the highlight on a row of a list that has just been rebuilt (the row that had it is gone, and Playnite may
        /// have moved the highlight to the games behind). Tries again a moment later if it didn't stick.
        /// </summary>
        private void FocusListItemSure(string listName, object item)
        {
            if (item == null) return;
            kbFocusTaken = true;   // e.g. a new collection named on the keyboard: its row gets the highlight, not the row the keyboard came from
            Func<bool> inList = () =>
            {
                var list = UiTools.FindByName(Application.Current.MainWindow, listName);
                var f = Keyboard.FocusedElement as DependencyObject;
                return list != null && f != null && list.IsAncestorOf(f);
            };
            // right away (the rows are laid out on the spot), then watched for a second: if anything takes the highlight
            // out of the menu meanwhile (the pressed row going, Playnite's own focus handling), it's put back on the row
            try
            {
                var host = UiTools.FindByName(Application.Current.MainWindow, listName);
                if (host != null) host.UpdateLayout();
                UiTools.FocusItem(listName, item);
            }
            catch (Exception) { }
            var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(120) };
            int tries = 0;
            int okTicks = 0;
            t.Tick += (s, e) =>
            {
                tries++;
                var options = UiTools.FindByName(Application.Current.MainWindow, "KX_OptionsPanel");
                var f = Keyboard.FocusedElement as DependencyObject;
                bool elsewhereInMenu = f != null && options != null && options.IsAncestorOf(f) && !ReferenceEquals(f, options);
                var listEl = UiTools.FindByName(Application.Current.MainWindow, listName);
                if (listEl == null || !listEl.IsVisible || kbOpen || confirmOpen) { t.Stop(); return; }   // the menu has closed, moved on, or the keyboard / a question is up
                if (inList()) { if (++okTicks >= 4) t.Stop(); return; }   // landed and stayed for half a second: done
                okTicks = 0;
                if (!elsewhereInMenu) UiTools.FocusItem(listName, item);   // lost (to the panel itself or the page behind): back on the row
                if (tries >= 9)
                {
                    t.Stop();
                    if (!inList() && !elsewhereInMenu)
                        logger.Warn("Kippix: couldn't put the highlight in " + listName + " (focus is on " + (f != null ? f.GetType().Name + " " + ((f as FrameworkElement) != null ? ((FrameworkElement)f).Name : "") : "nothing") + ")");
                }
            };
            t.Start();
        }

        /// <summary>Holds the keyboard focus on a panel itself for a moment, so rebuilding its list doesn't lose it to the screen behind.</summary>
        private static void ParkFocusIn(string panelName)
        {
            var panel = UiTools.FindByName(Application.Current.MainWindow, panelName);
            if (panel == null) return;
            panel.FocusVisualStyle = null;
            panel.Focusable = true;
            Keyboard.Focus(panel);
            // only for the moment: afterwards the panel isn't a stop of its own when moving around
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1200) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                bool held = panel.IsKeyboardFocused;
                panel.Focusable = false;
                if (held) UiTools.FocusInside(panelName);   // the list never took it: its first row has it instead
            };
            t.Start();
        }

        private Game collectionChoicesFor;   // the collections page (from the More menu) is for this game
        public ObservableCollection<PickRow> CollectionRows { get; private set; }
        private bool collectionsShown;              // the More menu is showing the collections
        private List<PickRow> moreSnapshot;         // the More menu's rows, for B
        public bool GameCollectionsShown { get { return false; } }
        public bool GameMoreListShown { get { return true; } }

        /// <summary>
        /// Changes the More menu's rows to <paramref name="fresh"/> without the highlighted row ever leaving the screen: that
        /// row turns into <paramref name="target"/> where it is, the others go, and the new ones are put round it. (Removing the
        /// highlighted row lets Playnite put the highlight back on the game page, which no amount of re-focusing undid.)
        /// </summary>
        private void MorphMoreRows(List<PickRow> fresh, PickRow target)
        {
            var keep = FocusedRow(GameMoreRows);
            int k = fresh.IndexOf(target);
            if (keep == null || k < 0)
            {
                GameMoreRows.Clear();
                foreach (var r in fresh) GameMoreRows.Add(r);
                FocusListItemSure("KX_GameMoreList", target);
                return;
            }
            keep.BecomeLike(target);
            for (int i = GameMoreRows.Count - 1; i >= 0; i--) if (!ReferenceEquals(GameMoreRows[i], keep)) GameMoreRows.RemoveAt(i);
            for (int i = 0; i < k; i++) GameMoreRows.Insert(i, fresh[i]);
            for (int i = k + 1; i < fresh.Count; i++) GameMoreRows.Add(fresh[i]);
            ui.BeginInvoke(new Action(RestoreMenuFocus), DispatcherPriority.Input);   // (if the highlight slipped out anyway, it comes back)
            // the row that kept the highlight can now be further down than the menu shows (rows went in above it: after Add an app
            // or game, Download what's missing was under the bottom edge): the menu scrolls to it
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    var list = UiTools.FindByName(Application.Current.MainWindow, "KX_GameMoreList") as ItemsControl;
                    var c = list != null ? list.ItemContainerGenerator.ContainerFromItem(keep) as FrameworkElement : null;
                    if (c != null) c.BringIntoView();
                }
                catch (Exception) { }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>The row of this list that has the highlight (null if none of them has).</summary>
        private static PickRow FocusedRow(ObservableCollection<PickRow> rows)
        {
            var f = Keyboard.FocusedElement as FrameworkElement;
            var row = f != null ? f.DataContext as PickRow : null;
            return row != null && rows.Contains(row) ? row : null;
        }

        /// <summary>More › Add to a collection: the More menu's rows become the collections (the highlight stays on the row you pressed).</summary>
        private void ShowCollectionChoices(Game g, string focusKey)
        {
            if (!collectionsShown) moreSnapshot = GameMoreRows.Select(r => r.Clone()).ToList();
            collectionChoicesFor = g;
            collectionsShown = true;
            var fresh = new List<PickRow>();
            fresh.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddToCollectionHeader", "ADD TO A COLLECTION") });
            foreach (var c in settings.Collections) fresh.Add(CollectionRow(g, c));
            fresh.Add(NewCollectionRow(g));
            var target = fresh.FirstOrDefault(r => r.Key == focusKey) ?? fresh.FirstOrDefault(r => r.IsRow);
            MorphMoreRows(fresh, target);
        }

        private PickRow NewCollectionRow(Game g)
        {
            return new PickRow
            {
                Text = Loc("LOC_KX_NewCollection", "New collection"), Sub = Loc("LOC_KX_NewCollectionSub", "Name it, and this game goes on it"), Icon = "add", Key = "new",
                Command = new RelayCommand(_ => NewCollection(g, c =>
                {
                    RebuildExtraRowsSoon();
                    // the New collection row becomes the new collection (the keyboard hands the highlight back to it), and a
                    // fresh New collection row goes under it
                    var newRow = GameMoreRows.FirstOrDefault(r => r.Key == "new");
                    if (newRow == null) return;
                    newRow.BecomeLike(CollectionRow(g, c));
                    GameMoreRows.Insert(GameMoreRows.IndexOf(newRow) + 1, NewCollectionRow(g));
                }))
            };
        }

        private PickRow CollectionRow(Game g, GameShelf c)
        {
            var shelf = c;
            var row = new PickRow { Text = c.Name, Icon = "shelf", Key = c.Id, IsSelected = c.Games.Contains(g.Id.ToString()), IsSwitch = true };
            row.Sub = row.IsSelected ? string.Format(Loc("LOC_KX_AddedTo", "{0} is on it"), g.Name) : Loc("LOC_KX_CollectionNotOn", "Not on it");
            DateTime last = DateTime.MinValue;
            row.Command = new RelayCommand(_ =>
            {
                // one press, one change (a press that arrives twice can't add it and take it straight off again)
                if ((DateTime.UtcNow - last).TotalMilliseconds < 450) return;
                last = DateTime.UtcNow;
                string id = g.Id.ToString();
                if (shelf.Games.Contains(id)) shelf.Games.Remove(id); else shelf.Games.Add(id);
                bool on = shelf.Games.Contains(id);
                // the row on screen for it (it may be another object that took this one's place): its tick and line change
                var shown = GameMoreRows.FirstOrDefault(r => r.Key == shelf.Id);
                if (shown != null)
                {
                    shown.IsSelected = on;
                    shown.SetSub(on ? string.Format(Loc("LOC_KX_AddedTo", "{0} is on it"), g.Name) : Loc("LOC_KX_TakenOff", "Taken off"));
                }
                SaveSettings();
                PlaySound("move");
                RebuildExtraRowsSoon();
            });
            return row;
        }

        /// <summary>B on the collections: back to the More menu, on Add to a collection (its line now says which ones).</summary>
        private bool CollectionsBack()
        {
            if (openPanel != "options" || optionsPage != "gamemore" || !collectionsShown) return false;
            var g = collectionChoicesFor;
            collectionChoicesFor = null;
            collectionsShown = false;
            PlaySound("back");
            var fresh = moreSnapshot ?? new List<PickRow>();
            moreSnapshot = null;
            var row = fresh.FirstOrDefault(r => r.Key == "collection");
            if (row != null && g != null)
            {
                var on = settings.Collections.Where(c => c.Games.Contains(g.Id.ToString())).Select(c => c.Name).ToList();
                row.Sub = on.Count > 0 ? string.Format(Loc("LOC_KX_OnCollections", "On: {0}"), string.Join(", ", on.ToArray()))
                                       : Loc("LOC_KX_AddToCollectionSub2", "Your own shelves of games (profile menu › More pages › Collections)");
            }
            if (fresh.Count == 0 || row == null) { if (g != null) { BuildGameMoreRows(g); FocusListItemSure("KX_GameMoreList", GameMoreRows.FirstOrDefault(r => r.Key == "collection")); } return true; }
            MorphMoreRows(fresh, row);
            return true;
        }

        // =====================================================================
        // Pick for me: a random game from your library, with filters and a roll
        // =====================================================================

        private bool pickInstalled = true, pickNever, pickShort, pickFavourite;
        private DispatcherTimer rollTimer;
        private int rollTicks;
        private bool rolling;   // from the press until it lands (the games are looked through first)

        private void BuildPickPage()
        {
            PageSub = Loc("LOC_KX_PagePickSub", "Can't decide? Roll for a game");
            PageHint = Loc("LOC_KX_PickHint", "X  Roll again");
            AddPageChip(Loc("LOC_KX_Roll", "Roll"), "dice", Roll);
            AddPickFilter(Loc("LOC_KX_PickInstalled", "Installed"), () => pickInstalled, v => pickInstalled = v);
            AddPickFilter(Loc("LOC_KX_PickNever", "Never played"), () => pickNever, v => pickNever = v);
            AddPickFilter(Loc("LOC_KX_PickShort", "Under 2 hours to beat"), () => pickShort, v => pickShort = v);
            AddPickFilter(Loc("LOC_KX_PickFavourite", "Favourites"), () => pickFavourite, v => pickFavourite = v);
            SetPageStatus(Loc("LOC_KX_PickStart", "Choose filters, then Roll."));
        }

        private void AddPickFilter(string text, Func<bool> get, Action<bool> set)
        {
            PickRow chip = null;
            chip = AddPageChip(text, "check", () => { set(!get()); chip.IsSelected = get(); PlaySound("move"); });
            chip.IsSelected = get();
        }

        private void Roll()
        {
            if (rolling) return;
            rolling = true;
            bool inst = pickInstalled, never = pickNever, shortOnly = pickShort, fav = pickFavourite;
            SetPageStatus(Loc("LOC_KX_Rolling", "Rolling…"));
            int gen = pageGen;
            string ext = api.Paths.ExtensionsDataPath;
            KippixShared.SafeWork.Queue(_ =>
            {
                var pool = new List<Game>();
                try
                {
                    foreach (var g in api.Database.Games.Where(g => g != null && !g.Hidden))
                    {
                        if (inst && !g.IsInstalled) continue;
                        if (never && g.Playtime > 0) continue;
                        if (fav && !g.Favorite) continue;
                        if (shortOnly)
                        {
                            var h = KippixOverlay.GameData.LoadHltb(ext, g.Id);
                            double main = h != null ? (h.Story > 0 ? h.Story : h.Extras) : 0;
                            if (main <= 0 || main > 2) continue;
                        }
                        pool.Add(g);
                    }
                }
                catch (Exception) { }
                OnUi(() =>
                {
                    if (gen != pageGen) { rolling = false; return; }
                    if (pool.Count == 0)
                    {
                        SetPageStatus(shortOnly ? Loc("LOC_KX_PickNoneHltb", "No games match. (Under 2 hours needs the HowLongToBeat add-on's data.)")
                                                : Loc("LOC_KX_PickNone", "No games match these filters."));
                        rolling = false;
                        return;
                    }
                    StartRoll(pool);
                });
            });
        }

        private void StartRoll(List<Game> pool)
        {
            var rnd = new Random();
            var final = pool[rnd.Next(pool.Count)];
            rollTicks = 0;
            int total = pool.Count > 1 ? 18 : 1;
            HasPageHero = true;
            Notify("HasPageHero");
            StopRoll();
            rolling = true;
            var timer = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(55) };
            rollTimer = timer;
            timer.Tick += (s, e) =>
            {
                if (rollTimer != timer) { timer.Stop(); return; }
                rollTicks++;
                bool last = rollTicks >= total;
                var g = last ? final : pool[rnd.Next(pool.Count)];
                ShowPickHero(g, last);
                if (last) { StopRoll(); PlaySound("select"); SetPageStatus(""); FocusPageSoon(null); return; }
                PlaySound("move");
                timer.Interval = TimeSpan.FromMilliseconds(55 + rollTicks * rollTicks * 1.6);   // slows down before it lands
            };
            timer.Start();
        }

        private void StopRoll()
        {
            rolling = false;
            if (rollTimer == null) return;
            rollTimer.Stop();
            rollTimer = null;
        }

        private void ShowPickHero(Game g, bool final)
        {
            pageHeroGame = final ? g.Id : Guid.Empty;
            pageHeroCover = Images.Load(CoverOf(g.Id), 360);
            PageHeroTitle = g.Name;
            PageHeroSub = final
                ? (g.Playtime > 0 ? string.Format(Loc("LOC_KX_PlayedFor", "Played {0}"), HoursShort(g.Playtime / 3600.0)) + (g.LastActivity.HasValue ? "   ·   " + AgoText(g.LastActivity.Value) : "")
                                  : Loc("LOC_KX_NeverPlayedYet", "Never played yet"))
                  + "      " + Loc("LOC_KX_PickOpen", "")
                : "";
            foreach (string n in new[] { "PageHeroCover", "PageHeroTitle", "PageHeroSub" }) Notify(n);
        }

        // =====================================================================
        // Storage: drives and installed games by size (uninstall through Playnite)
        // =====================================================================

        private int storageSort;   // 0 size, biggest first · 1 size, smallest first · 2 last played, longest ago first · 3 last played, latest first

        private void BuildStoragePage()
        {
            PageSub = Loc("LOC_KX_PageStorageSub", "Your drives and installed games by size");
            // each chip sorts by it; pressed again it turns the order round (▼ biggest / longest ago first, ▲ the other way)
            var bySize = AddPageChip(Loc("LOC_KX_SortSize", "Size") + (storageSort == 1 ? "  ▲" : "  ▼"), "drive", () => { storageSort = storageSort == 0 ? 1 : 0; pageQuietOpen = true; PlaySound("move"); OpenPage("storage", "", false); FocusPageSoon(PageChips.FirstOrDefault(c => c.Icon == "drive")); });
            var byDate = AddPageChip(Loc("LOC_KX_SortLastPlayed", "Last played") + (storageSort == 3 ? "  ▲" : "  ▼"), "clock", () => { storageSort = storageSort == 2 ? 3 : 2; pageQuietOpen = true; PlaySound("move"); OpenPage("storage", "", false); FocusPageSoon(PageChips.FirstOrDefault(c => c.Icon == "clock")); });
            bySize.IsSelected = storageSort <= 1;
            byDate.IsSelected = storageSort >= 2;

            AddPageHeader(Loc("LOC_KX_Drives", "DRIVES"));
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    double used = d.TotalSize > 0 ? 1.0 - (double)d.TotalFreeSpace / d.TotalSize : 0;
                    PageRows.Add(new PageRow
                    {
                        Text = d.Name.TrimEnd('\\') + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : "  " + d.VolumeLabel), Icon = "drive", Key = d.Name,
                        Sub = string.Format(Loc("LOC_KX_DriveFree", "{0} free of {1}"), SizeText(d.TotalFreeSpace), SizeText(d.TotalSize)),
                        Right = Math.Round(used * 100) + "%",
                        HasBar = true, BarWidth = Math.Max(4, used * 420), BarColor = used > 0.9 ? "#FFE5484D" : used > 0.75 ? "#FFF5A623" : "#FF4FC37A"
                    });
                }
            }
            catch (Exception) { }

            List<Game> games;
            try { games = api.Database.Games.Where(g => g != null && g.IsInstalled).ToList(); } catch (Exception) { games = new List<Game>(); }
            List<Game> sorted;
            switch (storageSort)
            {
                case 1: sorted = games.OrderBy(g => g.InstallSize.HasValue ? 0 : 1).ThenBy(g => g.InstallSize ?? 0).ThenBy(g => g.Name).ToList(); break;
                case 2: sorted = games.OrderBy(g => g.LastActivity ?? DateTime.MinValue).ToList(); break;
                case 3: sorted = games.OrderByDescending(g => g.LastActivity ?? DateTime.MinValue).ToList(); break;
                default: sorted = games.OrderByDescending(g => g.InstallSize ?? 0).ThenBy(g => g.Name).ToList(); break;
            }
            ulong known = (ulong)games.Where(g => g.InstallSize.HasValue).Sum(g => (decimal)g.InstallSize.Value);
            PageStats = new List<PageStat>
            {
                new PageStat { Value = games.Count.ToString(), Label = Loc("LOC_KX_StatInstalled", "games installed") },
                new PageStat { Value = SizeText((long)known), Label = Loc("LOC_KX_StatUsedByGames", "used by them") }
            };
            AddStorageSuggestions(games);   // SUGGESTIONS: what could go, biggest saving first (StorageTips.cs)
            AddPageHeader(Loc("LOC_KX_SecInstalledGames", "INSTALLED GAMES"));
            PageHint = Loc("LOC_KX_StorageHint", "A  Uninstall");
            foreach (var g in sorted)
            {
                var game = g;
                string drive = "";
                try { if (!string.IsNullOrEmpty(g.InstallDirectory)) drive = Path.GetPathRoot(g.InstallDirectory).TrimEnd('\\'); } catch (Exception) { }
                PageRows.Add(new PageRow
                {
                    Text = g.Name, ImagePath = CoverOf(g.Id), Key = g.Id.ToString(),
                    Sub = (g.LastActivity.HasValue ? string.Format(Loc("LOC_KX_LastPlayedAgo", "Last played {0}"), AgoText(g.LastActivity.Value)) : Loc("LOC_KX_NeverPlayedYet", "Never played yet"))
                        + (drive.Length > 0 ? "   ·   " + drive : ""),
                    Right = g.InstallSize.HasValue && g.InstallSize.Value > 0 ? SizeText((long)g.InstallSize.Value) : Loc("LOC_KX_SizeUnknown", "Size unknown"),
                    Command = new RelayCommand(_ => Confirm(string.Format(Loc("LOC_KX_UninstallTitle", "Uninstall {0}?"), game.Name),
                                                            Loc("LOC_KX_UninstallText", "Playnite asks the game's own launcher to remove it. Your saves and play time stay."),
                                                            Loc("LOC_KX_Uninstall", "Uninstall"),
                                                            () => { try { api.UninstallGame(game.Id); } catch (Exception ex) { logger.Warn("Kippix: uninstall: " + ex.Message); } }))
                });
            }
            if (games.Any(g => !g.InstallSize.HasValue))
                SetPageStatus(Loc("LOC_KX_SizesHelp", "Some sizes are unknown: Playnite fills them in when it scans install sizes (Playnite's library settings)."));
            LoadPageImages();
        }

        internal static string SizeText(long bytes)
        {
            double b = bytes;
            if (b >= 1e12) return (b / 1e12).ToString("0.##", CultureInfo.CurrentCulture) + " TB";
            if (b >= 1e9) return (b / 1e9).ToString(b >= 1e11 ? "0" : "0.#", CultureInfo.CurrentCulture) + " GB";
            if (b >= 1e6) return (b / 1e6).ToString("0", CultureInfo.CurrentCulture) + " MB";
            return Math.Max(0, b / 1e3).ToString("0", CultureInfo.CurrentCulture) + " KB";
        }

        // =====================================================================
        // Year in review
        // =====================================================================

        private void StepYear(int step)
        {
            int y;
            if (!int.TryParse(pageArg, out y)) y = DateTime.Now.Year;
            int to = y + step;
            if (to > DateTime.Now.Year || to < DateTime.Now.Year - 5) return;
            PlaySound("move");
            pageQuietOpen = true;
            OpenPage("year", to.ToString(), false);
        }

        private void BuildYearPage(string arg)
        {
            int y;
            if (!int.TryParse(arg, out y)) y = DateTime.Now.Year;
            pageArg = y.ToString();
            PageTitle = string.Format(Loc("LOC_KX_YourYear", "Your {0}"), y);
            // (on the profile the title is its header: the year goes in the line next to the tabs)
            PageSub = PageTitle + (y == DateTime.Now.Year ? "   ·   " + Loc("LOC_KX_YearSoFar", "The year so far") : "");
            PageHint = Loc("LOC_KX_YearHint2", "LT / RT  Another year");
            AddShareChip(y);
            SetPageStatus(Loc("LOC_KX_Counting", "Counting…"));
            int gen = pageGen;
            bool ownerView = !ProfilesOn || IsOwnerProfile;
            string profileId = CurrentProfileId;
            var achs = ownerView ? achData : null;   // achievements are the main profile's (Playnite's own)
            bool showHeat = HubSectionShown("heat"), showFinished = HubSectionShown("finished");
            KippixShared.SafeWork.Queue(_ =>
            {
                string source;
                List<PlaySession> sessions;
                try
                {
                    int days = (int)(DateTime.Now - new DateTime(y, 1, 1)).TotalDays + 2;
                    sessions = SessionsForProfile(out source, Math.Max(days, 30), ownerView, profileId).Where(s => s.Start.Year == y && s.Seconds > 0).ToList();
                }
                catch (Exception) { sessions = new List<PlaySession>(); source = ""; }
                // captures (the overlay's screenshot and clip folders)
                int captures = 0;
                try
                {
                    string shots, clipsText;
                    List<string> clips;
                    OverlayFolders(out shots, out clips, out clipsText);
                    string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string d0 in new[] { shots }.Concat(clips ?? new List<string>()).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        // Videos itself holds your own videos too: only its Captures folder (Xbox Game Bar) counts
                        bool isVideos = string.Equals(d0.TrimEnd('\\'), videos.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                        string dir = isVideos ? Path.Combine(videos, "Captures") : d0;
                        if (!Directory.Exists(dir)) continue;
                        foreach (string f in Directory.EnumerateFiles(dir, "*.*", isVideos ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".mp4" || ext == ".webp") && File.GetLastWriteTime(f).Year == y && seen.Add(f)) captures++;
                            if (captures > 99999) break;
                        }
                    }
                }
                catch (Exception) { }
                var added = 0;
                try { added = api.Database.Games.Count(g => g != null && g.Added.HasValue && g.Added.Value.Year == y && HubSees(g, ownerView)); } catch (Exception) { }
                int unlocked = 0;
                AchItemData rarest = null;
                string rarestGame = null;
                try   // off the UI thread: anything odd in the achievement data mustn't take Playnite down
                {
                    if (achs != null)
                        foreach (var d in achs.ToList())
                            if (d != null && d.Items != null)
                                foreach (var i in d.Items)
                                    if (i != null && i.Unlocked && i.Date.HasValue && i.Date.Value.Year == y)
                                    {
                                        unlocked++;
                                        if (i.Rarity.HasValue && (rarest == null || i.Rarity.Value < rarest.Rarity.Value)) { rarest = i; rarestGame = d.Name; }
                                    }
                }
                catch (Exception) { }
                // the calendar and the highlights: timed sessions only, split at midnight (ThemeApi.Heatmap.cs); finished games (ThemeApi.Completion.cs)
                var lastDay = new DateTime(y, 12, 31) > DateTime.Today ? DateTime.Today : new DateTime(y, 12, 31);
                Dictionary<DateTime, PlayDay> yearDays = null;
                FinishedInfo finished = null;
                try { yearDays = RealPlayDays(sessions, new DateTime(y, 1, 1), lastDay); } catch (Exception) { yearDays = new Dictionary<DateTime, PlayDay>(); }
                try { if (showFinished) finished = FinishedIn(y, ownerView); } catch (Exception) { }
                OnUi(() =>
                {
                    if (gen != pageGen) return;
                    if (showHeat) ShowPageHeatmap(new DateTime(y, 1, 1), lastDay, yearDays, string.Format(Loc("LOC_KX_HeatTitleYear", "PLAY CALENDAR {0}"), y));
                    if (finished != null) ShowFinishedShelf(finished, y);
                    FillYear(y, sessions, source, captures, added, unlocked, rarest, rarestGame, achs != null && achs.Count > 0, yearDays, finished);
                });
            });
        }

        private void FillYear(int y, List<PlaySession> sessions, string source, int captures, int added, int unlocked, AchItemData rarest, string rarestGame, bool haveAch,
                              Dictionary<DateTime, PlayDay> yearDays, FinishedInfo finished)
        {
            double hours = sessions.Sum(s => s.Seconds) / 3600.0;
            var byGame = sessions.GroupBy(s => s.GameId ?? s.Name).Select(g => new { Key = g.Key, Name = g.First().Name, Seconds = g.Sum(x => x.Seconds), Count = g.Count() })
                                 .OrderByDescending(g => g.Seconds).ToList();
            PageStats = new List<PageStat>
            {
                new PageStat { Value = HoursShort(hours), Label = Loc("LOC_KX_StatPlayed", "played") },
                new PageStat { Value = byGame.Count.ToString(), Label = Loc("LOC_KX_StatGames", "games") },
                new PageStat { Value = sessions.Count.ToString(), Label = Loc("LOC_KX_StatSessions", "sessions") },
                new PageStat { Value = haveAch ? unlocked.ToString("N0", CultureInfo.CurrentCulture) : "—", Label = Loc("LOC_KX_StatAchievements", "achievements") },
                new PageStat { Value = captures.ToString("N0", CultureInfo.CurrentCulture), Label = Loc("LOC_KX_StatCaptures", "captures") },
                new PageStat { Value = added.ToString(), Label = Loc("LOC_KX_StatAdded", "games added") }
            };
            if (finished != null && finished.Beaten + finished.Hundred > 0)
                PageStats.Add(new PageStat { Value = (finished.Beaten + finished.Hundred).ToString(CultureInfo.CurrentCulture), Label = Loc("LOC_KX_StatFinished", "games finished") });
            PageTilesTitle = byGame.Count > 0 ? Loc("LOC_KX_TopGames", "YOUR TOP GAMES") : "";
            foreach (var g in byGame.Take(8))
            {
                Guid id;
                Guid.TryParse(g.Key ?? "", out id);
                var gid = id;
                PageTiles.Add(new PageTile
                {
                    Name = g.Name, GameId = id, CoverPath = id != Guid.Empty ? CoverOf(id) : null,
                    Sub = HoursShort(g.Seconds / 3600.0) + "   ·   " + string.Format(Loc("LOC_KX_NSessions", "{0} sessions"), g.Count),
                    Command = new RelayCommand(_ => OpenGameFromPage(gid))
                });
            }
            if (sessions.Count > 0)
            {
                // (timed sessions only: ProfilePage.cs AddHighlightRows)
                AddHighlightRows(sessions, yearDays, true);
                if (rarest != null && !PageRows.Any(r => r.IsHeader)) AddPageHeader(Loc("LOC_KX_Highlights", "HIGHLIGHTS"));
                if (rarest != null)
                    PageRows.Add(new PageRow { Icon = "trophy", Text = Loc("LOC_KX_RarestUnlock", "Rarest achievement"), Sub = rarest.Name + "   ·   " + rarestGame, Right = string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", rarest.Rarity.Value) });

                AddPageHeader(Loc("LOC_KX_MonthByMonth", "MONTH BY MONTH"));
                var months = Enumerable.Range(1, 12).Select(m => sessions.Where(s => s.Start.Month == m).Sum(s => s.Seconds) / 3600.0).ToList();
                double max = Math.Max(0.1, months.Max());
                int lastMonth = y == DateTime.Now.Year ? DateTime.Now.Month : 12;
                for (int m = 1; m <= lastMonth; m++)
                    PageRows.Add(new PageRow
                    {
                        Text = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m), Right = months[m - 1] > 0 ? HoursShort(months[m - 1]) : "—", Key = "m" + m,
                        HasBar = true, BarWidth = Math.Max(months[m - 1] > 0 ? 6 : 0, 420 * months[m - 1] / max), BarColor = "#FF8AB4FF"
                    });
            }
            string note = sessions.Count == 0
                ? Loc("LOC_KX_YearEmpty", "No play sessions found for this year.")
                : source == "GameActivity" ? "" : Loc("LOC_KX_YearOwnSessions", "Hours come from the sessions the theme has recorded. Install the GameActivity add-on for your full history.");
            if (!haveAch) note = (note.Length > 0 ? note + "\n" : "") + Loc("LOC_KX_YearNoAch", "Achievements show once the Achievements page has loaded them (Playnite Achievements or SuccessStory).");
            SetPageStatus(note);
            NotifyPage();
            NotifyPageLists();
            LoadPageImages();
            FocusPageSoon(null);
        }
    }
}
