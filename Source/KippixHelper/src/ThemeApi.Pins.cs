using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Pinned games: shown first, before the games (where the Store tile goes), however Playnite sorts the list.
    /// Pin or unpin from the game's More menu. Hiding a game from the home screen uses Playnite's own Hidden flag
    /// (so it comes back from Playnite's filters, Show hidden).
    /// </summary>
    public partial class ThemeApi
    {
        private bool IsPinnedGame(Guid id) { return settings.PinnedGames != null && settings.PinnedGames.Contains(id.ToString()); }

        private void AddPinnedTiles()
        {
            if (settings.PinnedGames == null || settings.PinnedGames.Count == 0) return;
            var gone = new List<string>();
            foreach (string s in settings.PinnedGames.ToList())
            {
                Guid id;
                Game g = null;
                if (Guid.TryParse(s, out id)) try { g = api.Database.Games.Get(id); } catch (Exception) { }
                if (g == null) { gone.Add(s); continue; }
                if (g.Hidden || !ProfileAllowsGame(g)) continue;   // (Profiles: hidden from whoever's playing, or above their age)
                if (FolderInside && folderFilter != null && !folderFilter.Contains(g.Id)) continue;   // (in a folder: only its own games)
                var gid = g.Id;
                var tile = new HomeTile { Key = "pin:" + s, Name = g.Name, Sub = "", GameId = gid };
                tile.Command = new RelayCommand(_ => OpenPinnedGame(gid));
                HomeTiles.Add(tile);
                string cover = ShuffledCover(g, !string.IsNullOrEmpty(g.CoverImage) && !g.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? SafeFullPath(g.CoverImage) : null);
                if (cover != null)
                {
                    var t = tile;
                    KippixShared.SafeWork.Queue(_ => { var img = Images.Load(cover, 600); if (img != null) OnUi(() => t.Cover = img); });
                }
            }
            if (gone.Count > 0) { foreach (string s in gone) settings.PinnedGames.Remove(s); SaveSettings(); }
        }

        private void OpenPinnedGame(Guid id)
        {
            PlaySound("select");
            HideHomeTilesIfAuto();
            try { api.MainView.SelectGame(id); } catch (Exception) { }
            if (settings.SkipGamePage) { if (TrySwitchInsteadOfPlay(id)) return; launchedDirectlyAt = DateTime.Now; try { api.StartGame(id); } catch (Exception) { } return; }
            ui.BeginInvoke(new Action(() =>
            {
                if (SelectedGameId() != id) return;   // Playnite's filter hides it
                RunMainCommand("ToggleGameDetailsCommand");
            }), DispatcherPriority.Loaded);
        }

        private void TogglePin(Game g)
        {
            if (g == null) return;
            if (settings.PinnedGames == null) settings.PinnedGames = new List<string>();
            string s = g.Id.ToString();
            bool pin = !settings.PinnedGames.Contains(s);
            if (pin) settings.PinnedGames.Add(s); else settings.PinnedGames.Remove(s);
            SaveSettings();
            PlaySound(pin ? "select" : "back");
            BuildHomeTiles();
            homeTilesShown = false;
            tilesVisible = false;
            SetCardsLeft(0);
            ShiftList(0);
            ApplyHomeTiles();
            ApplyCardLayoutSoon();
        }

        private void HideFromHome(Game g)
        {
            if (g == null) return;
            try
            {
                g.Hidden = true;
                api.Database.Games.Update(g);
                if (settings.PinnedGames != null && settings.PinnedGames.Remove(g.Id.ToString())) { SaveSettings(); BuildHomeTiles(); ApplyHomeTiles(); ApplyCardLayoutSoon(); }
                PlaySound("back");
            }
            catch (Exception ex) { logger.Warn("Kippix: hide game: " + ex.Message); }
        }

        /// <summary>The More menu's first rows: pin / unpin, hide, add to a collection.</summary>
        private void AddHomeRows(Game g)
        {
            if (g == null) return;
            var game = g;
            bool pinned = IsPinnedGame(g.Id);
            GameMoreRows.Add(new PickRow
            {
                Text = pinned ? Loc("LOC_KX_Unpin", "Unpin from the home screen") : Loc("LOC_KX_Pin", "Pin to the home screen"),
                Sub = pinned ? Loc("LOC_KX_UnpinSub", "It goes back among your other games") : Loc("LOC_KX_PinHomeSub", "Pins to the home screen, accessible when moving in the opposite direction of viewing more games, i.e. left, up etc."),
                Icon = "pin", Key = "pin",
                Command = new RelayCommand(_ => { ClosePanels(); TogglePin(game); })
            });
            GameMoreRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_HideFromHome", "Hide from the home screen"),
                Sub = Loc("LOC_KX_HideFromHomeSub", ""),   // (the user's wording: no line under it)
                Icon = "hide", Key = "hide",
                Command = new RelayCommand(_ => { ClosePanels(); CloseGameDetails(true); HideFromHome(game); })
            });
            AddSlotPinRow(g);
            AddProfileFavouriteRow(g);
            var on = settings.Collections.Where(c => c != null && c.Games != null && c.Games.Contains(g.Id.ToString())).Select(c => c.Name).ToList();
            GameMoreRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_AddToCollection", "Add to a collection"),
                Sub = on.Count > 0 ? string.Format(Loc("LOC_KX_OnCollections", "On: {0}"), string.Join(", ", on.ToArray())) : Loc("LOC_KX_AddToCollectionSub2", "Your own shelves of games (profile menu › More pages › Collections)"),
                Icon = "shelf", Key = "collection",
                Command = new RelayCommand(_ => ShowCollectionChoices(game, null))
            });
        }
    }
}
