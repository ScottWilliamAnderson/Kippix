using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace KippixHelper
{
    /// <summary>
    /// Edit this game (the game page's More menu, and Playnite's own game menu under Kippix): what Playnite's desktop edit
    /// window does that can be done from here, with the controller. The More menu's rows become these (as they do for Add to a
    /// collection), B goes back.
    ///   - Name (the keyboard), favourite, hidden, and Status and rating (the page that was already there).
    ///   - Categories, genres, platforms and tags: each a list of switches, with a row to make a new one.
    ///   - Download metadata: the cover, background, description, genres and release date, from the game's own library add-on
    ///     (Steam, GOG...) and, for what that doesn't give, a metadata add-on (IGDB when it's there), matched by name without
    ///     asking. Either only what's missing, or all of it again. The name and everything else is left alone.
    ///   - Open desktop mode: Playnite's edit window itself can't be opened in fullscreen (the SDK refuses), so this switches.
    /// </summary>
    public partial class ThemeApi
    {
        private string gameEditLevel;     // null: not in it; "edit": its rows; "list": one of its lists (gameEditList)
        private Guid gameEditId;
        private bool gameEditFromMore;    // opened from the More menu: B goes back to it (else B closes)
        private string gameEditList;
        private bool gameEditBusy;        // a metadata download is under way
        private DateTime gameEditPressed = DateTime.MinValue;

        /// <summary>One of the game's lists of things Playnite keeps by id.</summary>
        private class GameEditList
        {
            public string Key, Title, Icon;
            public Func<Game, List<Guid>> Get;
            public Action<Game, List<Guid>> Set;
            public Func<IEnumerable<DatabaseObject>> All;
            public Func<string, DatabaseObject> Add;
        }

        private List<GameEditList> GameEditLists()
        {
            return new List<GameEditList>
            {
                new GameEditList { Key = "categories", Title = Loc("LOC_KX_EditCategories", "Categories"), Icon = "shelf", Get = g => g.CategoryIds, Set = (g, v) => g.CategoryIds = v,
                                   All = () => api.Database.Categories.Cast<DatabaseObject>(), Add = n => api.Database.Categories.Add(n) },
                new GameEditList { Key = "genres", Title = Loc("LOC_KX_EditGenres", "Genres"), Icon = "tag", Get = g => g.GenreIds, Set = (g, v) => g.GenreIds = v,
                                   All = () => api.Database.Genres.Cast<DatabaseObject>(), Add = n => api.Database.Genres.Add(n) },
                new GameEditList { Key = "platforms", Title = Loc("LOC_KX_EditPlatforms", "Platforms"), Icon = "controller", Get = g => g.PlatformIds, Set = (g, v) => g.PlatformIds = v,
                                   All = () => api.Database.Platforms.Cast<DatabaseObject>(), Add = n => api.Database.Platforms.Add(n) },
                new GameEditList { Key = "tags", Title = Loc("LOC_KX_EditTags", "Tags"), Icon = "hash", Get = g => g.TagIds, Set = (g, v) => g.TagIds = v,
                                   All = () => api.Database.Tags.Cast<DatabaseObject>(), Add = n => api.Database.Tags.Add(n) },
            };
        }

        private Game GameEditGame()
        {
            try { return gameEditId != Guid.Empty ? api.Database.Games.Get(gameEditId) : null; } catch (Exception) { return null; }
        }

        private PickRow GameEditRow(string key) { return GameMoreRows.FirstOrDefault(r => r.Key == key); }
        private void GameEditSub(string key, string sub) { var r = GameEditRow(key); if (r != null) r.SetSub(sub); }

        /// <summary>One press, one change (a press that arrives twice can't switch something on and straight off again).</summary>
        private bool GameEditPressOnce()
        {
            if ((DateTime.UtcNow - gameEditPressed).TotalMilliseconds < 450) return false;
            gameEditPressed = DateTime.UtcNow;
            return true;
        }

        private bool GameEditSave(Game g)
        {
            try { api.Database.Games.Update(g); return true; }
            catch (Exception ex) { logger.Warn("Kippix: edit game: " + ex.Message); return false; }
        }

        // ---------------- where it opens from ----------------

        /// <summary>From BuildGameMoreRows: the More menu's own row for it.</summary>
        private void AddGameEditRow(Game g)
        {
            gameEditLevel = null;
            gameAddLevel = null;
            if (g == null) return;
            var game = g;
            GameMoreRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_EditGame", "Edit this game"), Sub = Loc("LOC_KX_EditGameSub", "Name, favourite, hidden, categories, genres, download metadata"), Icon = "note", Key = "edit",
                Command = new RelayCommand(_ => ShowGameEdit(game, true, null))
            });
        }

        /// <summary>Playnite's own game menu (Kippix › Edit this game), in fullscreen, for one game.</summary>
        internal GameMenuItem GameEditMenuItem(GetGameMenuItemsArgs args)
        {
            if (!IsFullscreen || args == null || args.Games == null || args.Games.Count != 1 || args.Games[0] == null) return null;
            Guid id = args.Games[0].Id;
            return new GameMenuItem { MenuSection = "Kippix", Description = Loc("LOC_KX_EditGame", "Edit this game"), Action = a => OpenGameEdit(id) };
        }

        /// <summary>Straight to the edit rows, from anywhere (once Playnite's menu has closed).</summary>
        private void OpenGameEdit(Guid id)
        {
            if (ui == null) return;
            var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Game g = null;
                try { g = api.Database.Games.Get(id); } catch (Exception) { }
                if (g == null || !IsFullscreen) return;
                if (openPanel != "options") TogglePanel("options");
                if (openPanel != "options") return;   // (the profile's PIN was asked for instead)
                GameMoreTitle = g.Name;
                Notify("GameMoreTitle");
                if (collectionsShown) { collectionsShown = false; Notify("GameCollectionsShown"); Notify("GameMoreListShown"); }
                gameAddLevel = null;
                moreSnapshot = null;
                gameEditId = g.Id;
                gameEditLevel = "edit";
                gameEditFromMore = false;
                GameMoreRows.Clear();
                foreach (var r in GameEditRows(g)) GameMoreRows.Add(r);
                SetOptionsPage("gamemore");
            };
            timer.Start();
        }

        /// <summary>The More menu's rows become the edit rows (the highlight stays on the row you pressed).</summary>
        private void ShowGameEdit(Game g, bool fromMore, string focusKey)
        {
            if (g == null) return;
            if (gameEditLevel == null)
            {
                gameEditFromMore = fromMore;
                if (fromMore) moreSnapshot = GameMoreRows.Select(r => r.Clone()).ToList();
            }
            gameEditId = g.Id;
            gameEditLevel = "edit";
            var fresh = GameEditRows(g);
            MorphMoreRows(fresh, fresh.FirstOrDefault(r => r.Key == focusKey) ?? fresh.FirstOrDefault(r => r.IsRow));
        }

        /// <summary>B: from a list to the edit rows, from the edit rows to the More menu (or, opened by itself, closed).</summary>
        private bool GameEditBack()
        {
            if (GameAddBack()) return true;   // (Add an app or game: ThemeApi.GameAdd.cs)
            if (openPanel != "options" || optionsPage != "gamemore" || gameEditLevel == null) return false;
            var g = GameEditGame();
            if (gameEditLevel == "list" && g != null) { PlaySound("back"); ShowGameEdit(g, gameEditFromMore, "list:" + gameEditList); return true; }
            gameEditLevel = null;
            if (!gameEditFromMore) return false;
            PlaySound("back");
            var fresh = moreSnapshot;
            moreSnapshot = null;
            var row = fresh != null ? fresh.FirstOrDefault(r => r.Key == "edit") : null;
            if (row == null)
            {
                if (g == null) return false;
                BuildGameMoreRows(g);
                FocusListItemSure("KX_GameMoreList", GameEditRow("edit"));
                return true;
            }
            MorphMoreRows(fresh, row);
            return true;
        }

        // ---------------- the edit rows ----------------

        private string GameEditNames(Game g, GameEditList list)
        {
            var ids = list.Get(g);
            if (ids == null || ids.Count == 0) return Loc("LOC_KX_EditNone", "None");
            try { return string.Join(", ", list.All().Where(x => ids.Contains(x.Id)).Select(x => x.Name).OrderBy(n => n).ToArray()); }
            catch (Exception) { return ""; }
        }

        private List<PickRow> GameEditRows(Game g)
        {
            var rows = new List<PickRow>();
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_EditHeader", "EDIT") });
            rows.Add(new PickRow
            {
                Key = "name", Icon = "keyboard", Text = Loc("LOC_KX_EditName", "Name"), Sub = g.Name,
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_EditNameAsk", "The game's name"), g.Name ?? "", text =>
                {
                    var game = GameEditGame();
                    text = (text ?? "").Trim();
                    if (game == null || text.Length == 0 || text == game.Name) return;
                    game.Name = text;
                    if (!GameEditSave(game)) return;
                    GameEditSub("name", text);
                    GameMoreTitle = text;
                    Notify("GameMoreTitle");
                    Notify("OptionsTitle");   // (the menu's heading is the game's name: it kept the old one)
                }, null, null, 200))
            });
            rows.Add(new PickRow
            {
                Key = "fav", Icon = "heart", IsSwitch = true, IsSelected = g.Favorite, Text = Loc("LOC_KX_EditFavourite", "Favourite"),
                Command = new RelayCommand(_ =>
                {
                    var game = GameEditGame();
                    if (game == null || !GameEditPressOnce()) return;
                    game.Favorite = !game.Favorite;
                    if (!GameEditSave(game)) return;
                    PlaySound("move");
                    var r = GameEditRow("fav");
                    if (r != null) r.IsSelected = game.Favorite;
                    try { RebuildExtraRowsSoon(); } catch (Exception) { }
                })
            });
            rows.Add(new PickRow
            {
                Key = "hidden", Icon = "hide", IsSwitch = true, IsSelected = g.Hidden, Text = Loc("LOC_KX_EditHidden", "Hidden"),
                Sub = Loc("LOC_KX_EditHiddenSub", "A hidden game only shows when Playnite's filter asks for hidden ones"),
                Command = new RelayCommand(_ =>
                {
                    var game = GameEditGame();
                    if (game == null || !GameEditPressOnce()) return;
                    game.Hidden = !game.Hidden;
                    if (!GameEditSave(game)) return;
                    PlaySound("move");
                    var r = GameEditRow("hidden");
                    if (r != null) r.IsSelected = game.Hidden;
                })
            });
            if (gameEditFromMore) FillStatusAndNotes(g);
            if (gameEditFromMore)
                rows.Add(new PickRow { Key = "status", Icon = "flag", Text = Loc("LOC_KX_StatusRating", "Status and rating"), Sub = DetStatusText + "  ·  " + DetStarsText, Command = new RelayCommand(_ => OpenGamePanel("gamestatus")) });
            foreach (var l in GameEditLists())
            {
                var list = l;
                rows.Add(new PickRow { Key = "list:" + l.Key, Icon = l.Icon, Text = l.Title, Sub = GameEditNames(g, l), Command = new RelayCommand(_ => ShowGameEditList(list, null)) });
            }
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_EditMetaHeader", "METADATA") });
            string from = GameEditSourceNames(g);
            if (from == null) rows.Add(new PickRow { IsInfo = true, Icon = "info", Text = Loc("LOC_KX_EditMetaNoSource", "No add-on here can download metadata for this one") });
            else
            {
                string sub = string.Format(Loc("LOC_KX_EditMetaFrom", "Cover, background, description, genres, release date  ·  from {0}"), from);
                rows.Add(new PickRow { Key = "metafill", Icon = "download", Text = Loc("LOC_KX_EditMetaFill", "Download what's missing"), Sub = sub, Command = new RelayCommand(_ => GameEditDownload(false)) });
                rows.Add(new PickRow { Key = "metaall", Icon = "refresh", Text = Loc("LOC_KX_EditMetaAll", "Download it all again"), Sub = sub, Command = new RelayCommand(_ => GameEditDownload(true)) });
            }
            rows.Add(new PickRow
            {
                Key = "desktop", Icon = "desktop", Text = Loc("LOC_KX_EditDesktop", "Open desktop mode to edit"),
                Sub = Loc("LOC_KX_EditDesktopSub", "Everything else is in Playnite's own edit window (F3 on the game there), which needs a mouse and keyboard"),
                Command = new RelayCommand(_ => Confirm(Loc("LOC_KX_EditDesktopAsk", "Switch to desktop mode?"), Loc("LOC_KX_EditDesktopText", "Playnite leaves fullscreen and opens its desktop window. Come back with F11 or its menu."),
                                                        Loc("LOC_KX_EditDesktopYes", "Switch"), () =>
                {
                    ClosePanels();
                    try { api.MainView.ToggleFullscreenView(); } catch (Exception ex) { logger.Warn("Kippix: desktop mode: " + ex.Message); }
                }))
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddLibraryHeader", "LIBRARY") });
            rows.Add(GameAddMenuRow(false));   // (ThemeApi.GameAdd.cs)
            return rows;
        }

        // ---------------- categories, genres, platforms, tags ----------------

        private PickRow GameEditListRow(GameEditList list, DatabaseObject item, bool on)
        {
            Guid id = item.Id;
            return new PickRow
            {
                Key = "i:" + id, Icon = list.Icon, IsSwitch = true, IsSelected = on, Text = item.Name,
                Command = new RelayCommand(_ =>
                {
                    var game = GameEditGame();
                    if (game == null || !GameEditPressOnce()) return;
                    var ids = (list.Get(game) ?? new List<Guid>()).ToList();
                    if (!ids.Remove(id)) ids.Add(id);
                    list.Set(game, ids);
                    if (!GameEditSave(game)) return;
                    PlaySound("move");
                    var r = GameEditRow("i:" + id);
                    if (r != null) r.IsSelected = ids.Contains(id);
                    try { RebuildExtraRowsSoon(); } catch (Exception) { }
                })
            };
        }

        private PickRow GameEditNewRow(GameEditList list)
        {
            return new PickRow
            {
                Key = "new", Icon = "add", Text = Loc("LOC_KX_EditNew", "New…"), Sub = Loc("LOC_KX_EditNewSub", "Name it, and this game gets it"),
                Command = new RelayCommand(_ => OpenKeyboard(list.Title, "", text =>
                {
                    var game = GameEditGame();
                    text = (text ?? "").Trim();
                    if (game == null || text.Length == 0) return;
                    DatabaseObject item;
                    try { item = list.Add(text); } catch (Exception ex) { logger.Warn("Kippix: edit game: " + ex.Message); return; }
                    if (item == null) return;
                    var ids = (list.Get(game) ?? new List<Guid>()).ToList();
                    if (!ids.Contains(item.Id)) ids.Add(item.Id);
                    list.Set(game, ids);
                    if (!GameEditSave(game)) return;
                    // one that was already there (the same name): its switch goes on. Else the New row becomes it (the keyboard
                    // hands the highlight back to that row), with a fresh New row under it
                    var there = GameEditRow("i:" + item.Id);
                    if (there != null) { there.IsSelected = true; return; }
                    var newRow = GameEditRow("new");
                    if (newRow == null) return;
                    newRow.BecomeLike(GameEditListRow(list, item, true));
                    GameMoreRows.Insert(GameMoreRows.IndexOf(newRow) + 1, GameEditNewRow(list));
                }, null, null, 80))
            };
        }

        private void ShowGameEditList(GameEditList list, string focusKey)
        {
            var g = GameEditGame();
            if (g == null) return;
            gameEditLevel = "list";
            gameEditList = list.Key;
            var ids = list.Get(g) ?? new List<Guid>();
            var fresh = new List<PickRow>();
            fresh.Add(new PickRow { IsHeader = true, Text = list.Title.ToUpper() });
            try
            {
                // the ones it has first, then the rest by name
                foreach (var item in list.All().Where(x => x != null).OrderBy(x => ids.Contains(x.Id) ? 0 : 1).ThenBy(x => x.Name))
                    fresh.Add(GameEditListRow(list, item, ids.Contains(item.Id)));
            }
            catch (Exception ex) { logger.Warn("Kippix: edit game: " + ex.Message); }
            fresh.Add(GameEditNewRow(list));
            MorphMoreRows(fresh, fresh.FirstOrDefault(r => r.Key == focusKey) ?? fresh.FirstOrDefault(r => r.IsRow));
        }

        // ---------------- download metadata ----------------

        private LibraryPlugin GameEditLibrary(Game g)
        {
            if (g == null || g.PluginId == Guid.Empty) return null;
            try { return api.Addons.Plugins.OfType<LibraryPlugin>().FirstOrDefault(p => p.Id == g.PluginId); } catch (Exception) { return null; }
        }

        /// <summary>The metadata add-on asked for what the library doesn't give: IGDB when it's there, else the first that has descriptions.</summary>
        private MetadataPlugin GameEditMetadataPlugin()
        {
            try
            {
                var all = api.Addons.Plugins.OfType<MetadataPlugin>().ToList();
                return all.FirstOrDefault(p => string.Equals(p.Name, "IGDB", StringComparison.OrdinalIgnoreCase))
                    ?? all.FirstOrDefault(p => p.SupportedFields != null && p.SupportedFields.Contains(MetadataField.Description));
            }
            catch (Exception) { return null; }
        }

        private string GameEditSourceNames(Game g)
        {
            var names = new List<string>();
            var lib = GameEditLibrary(g);
            if (lib != null) names.Add(lib.Name);
            var meta = GameEditMetadataPlugin();
            if (meta != null) names.Add(meta.Name);
            return names.Count == 0 ? null : string.Join(", ", names.ToArray());
        }

        private void GameEditDownload(bool replace)
        {
            var g = GameEditGame();
            if (g == null || gameEditBusy) return;
            string key = replace ? "metaall" : "metafill";
            Guid id = g.Id;
            // (filling in what's missing takes nothing away: its Download button isn't in the warning red; replacing it all is)
            Action<string, string, string, Action> ask = Confirm;
            if (!replace) ask = ConfirmPlain;
            ask(replace ? Loc("LOC_KX_EditMetaAllAsk", "Download it all again?") : Loc("LOC_KX_EditMetaFillAsk", "Download what's missing?"),
                    string.Format(replace ? Loc("LOC_KX_EditMetaAllText", "{0}: its cover, background, description, genres and release date are replaced with what {1} has, matched by name. Its name and everything else stay as they are.")
                                          : Loc("LOC_KX_EditMetaFillText", "{0}: a cover, background, description, genres or release date it doesn't have yet are taken from {1}, matched by name. What it has stays as it is."),
                                  g.Name, GameEditSourceNames(g)),
                    Loc("LOC_KX_EditMetaYes", "Download"), () =>
            {
                gameEditBusy = true;
                GameEditSub(key, Loc("LOC_KX_EditMetaBusy", "Downloading…"));
                KippixShared.SafeWork.Queue(_ =>
                {
                    string result;
                    try { result = DownloadGameMetadata(id, replace); }
                    catch (Exception ex) { logger.Warn("Kippix: metadata: " + ex); result = string.Format(Loc("LOC_KX_EditMetaFailed", "It didn't work: {0}"), ex.Message); }
                    OnUi(() =>
                    {
                        gameEditBusy = false;
                        if (gameEditLevel != "edit" || gameEditId != id) return;
                        GameEditSub(key, result);
                        var game = GameEditGame();
                        var genres = GameEditLists().FirstOrDefault(l => l.Key == "genres");
                        if (game != null && genres != null) GameEditSub("list:genres", GameEditNames(game, genres));
                    });
                });
            });
        }

        private static bool Blank(string s) { return string.IsNullOrEmpty(s); }

        /// <summary>Worker thread. What changed, as a line for the row.</summary>
        private string DownloadGameMetadata(Guid id, bool replace)
        {
            var g = api.Database.Games.Get(id);
            if (g == null) return "";
            bool wantCover = replace || Blank(g.CoverImage), wantBack = replace || Blank(g.BackgroundImage), wantDesc = replace || Blank(g.Description);
            bool wantGenres = replace || g.GenreIds == null || g.GenreIds.Count == 0, wantDate = replace || g.ReleaseDate == null;
            if (!wantCover && !wantBack && !wantDesc && !wantGenres && !wantDate) return Loc("LOC_KX_EditMetaNothingMissing", "Nothing is missing");

            MetadataFile cover = null, back = null;
            string desc = null;
            List<MetadataProperty> genres = null;
            ReleaseDate? date = null;

            // the game's own library first (Steam, GOG...)
            var lib = GameEditLibrary(g);
            if (lib != null)
            {
                LibraryMetadataProvider dl = null;
                try
                {
                    dl = lib.GetMetadataDownloader();
                    var m = dl != null ? dl.GetMetadata(g) : null;
                    if (m != null)
                    {
                        cover = m.CoverImage; back = m.BackgroundImage; desc = m.Description; date = m.ReleaseDate;
                        if (m.Genres != null && m.Genres.Count > 0) genres = m.Genres.ToList();
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: metadata: " + lib.Name + ": " + ex.Message); }
                finally { try { if (dl != null) dl.Dispose(); } catch (Exception) { } }
            }

            // then a metadata add-on, for what's still wanted (matched by name, nothing asked)
            bool more = (wantCover && cover == null) || (wantBack && back == null) || (wantDesc && Blank(desc)) || (wantGenres && genres == null) || (wantDate && date == null);
            var meta = more ? GameEditMetadataPlugin() : null;
            if (meta != null)
            {
                OnDemandMetadataProvider p = null;
                try
                {
                    p = meta.GetMetadataProvider(new MetadataRequestOptions(g, true));
                    var have = p != null ? p.AvailableFields : null;
                    var args = new GetMetadataFieldArgs();
                    if (have != null)
                    {
                        if (wantCover && cover == null && have.Contains(MetadataField.CoverImage)) cover = p.GetCoverImage(args);
                        if (wantBack && back == null && have.Contains(MetadataField.BackgroundImage)) back = p.GetBackgroundImage(args);
                        if (wantDesc && Blank(desc) && have.Contains(MetadataField.Description)) desc = p.GetDescription(args);
                        if (wantDate && date == null && have.Contains(MetadataField.ReleaseDate)) date = p.GetReleaseDate(args);
                        if (wantGenres && genres == null && have.Contains(MetadataField.Genres)) { var gs = p.GetGenres(args); if (gs != null) genres = gs.ToList(); }
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: metadata: " + meta.Name + ": " + ex.Message); }
                finally { try { if (p != null) p.Dispose(); } catch (Exception) { } }
            }

            // the pictures into Playnite's library (downloaded here, off the UI thread)
            string coverId = wantCover ? StoreMetadataFile(cover, id) : null;
            string backId = wantBack ? StoreMetadataFile(back, id) : null;

            var changed = new List<string>();
            OnUiSync(() =>
            {
                var game = api.Database.Games.Get(id);
                if (game == null) return;
                var old = new List<string>();
                if (coverId != null) { old.Add(game.CoverImage); game.CoverImage = coverId; changed.Add(Loc("LOC_KX_EditMetaCover", "cover")); }
                if (backId != null) { old.Add(game.BackgroundImage); game.BackgroundImage = backId; changed.Add(Loc("LOC_KX_EditMetaBackground", "background")); }
                if (wantDesc && !Blank(desc) && desc != game.Description) { game.Description = desc; changed.Add(Loc("LOC_KX_EditMetaDescription", "description")); }
                if (wantGenres && genres != null && genres.Count > 0)
                {
                    var ids = api.Database.Genres.Add(genres).Where(x => x != null).Select(x => x.Id).Distinct().ToList();
                    if (ids.Count > 0 && (game.GenreIds == null || !new HashSet<Guid>(ids).SetEquals(game.GenreIds))) { game.GenreIds = ids; changed.Add(Loc("LOC_KX_EditMetaGenres", "genres")); }
                }
                if (wantDate && date != null && !date.Equals(game.ReleaseDate)) { game.ReleaseDate = date; changed.Add(Loc("LOC_KX_EditMetaDate", "release date")); }
                if (changed.Count == 0) return;
                api.Database.Games.Update(game);
                // the pictures they replace (Playnite's own files only, and never one that's still in use)
                foreach (string f in old)
                    if (!Blank(f) && !f.StartsWith("http", StringComparison.OrdinalIgnoreCase) && f != game.CoverImage && f != game.BackgroundImage && f != game.Icon)
                        try { api.Database.RemoveFile(f); } catch (Exception) { }
            });
            return changed.Count == 0 ? Loc("LOC_KX_EditMetaNothing", "Nothing new was found") : string.Format(Loc("LOC_KX_EditMetaDone", "Updated: {0}"), string.Join(", ", changed.ToArray()));
        }

        /// <summary>A picture from a metadata source into Playnite's library: its id there (null: there wasn't one, or it couldn't be had).</summary>
        private string StoreMetadataFile(MetadataFile f, Guid gameId)
        {
            if (f == null) return null;
            string temp = null;
            try
            {
                if (f.HasContent)
                {
                    temp = MetadataTempFile(f.FileName);
                    File.WriteAllBytes(temp, f.Content);
                    return api.Database.AddFile(temp, gameId);
                }
                string path = f.Path;
                if (Blank(path)) return null;
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    string name = path;
                    try { name = new Uri(path).AbsolutePath; } catch (Exception) { }
                    temp = MetadataTempFile(name);
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                        wc.DownloadFile(path, temp);
                    }
                    return new FileInfo(temp).Length > 0 ? api.Database.AddFile(temp, gameId) : null;
                }
                return File.Exists(path) ? api.Database.AddFile(path, gameId) : null;
            }
            catch (Exception ex) { logger.Warn("Kippix: metadata picture: " + ex.Message); return null; }
            finally { try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch (Exception) { } }
        }

        private static string MetadataTempFile(string name)
        {
            string ext = "";
            try { ext = Path.GetExtension(name ?? ""); } catch (Exception) { }
            if (Blank(ext) || ext.Length > 5) ext = ".jpg";
            return Path.Combine(Path.GetTempPath(), "kippix_" + Guid.NewGuid().ToString("N") + ext);
        }
    }
}
