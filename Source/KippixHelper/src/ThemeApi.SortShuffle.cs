using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Sort and show › SORT BY › Shuffle: the games on the home screen in a random order, to find something to play among
    /// whatever the filter that's on shows (with Installed only: among the installed games).
    ///   - A new order each time it's chosen, and each time Playnite starts.
    ///   - It's a sort like the others: choosing another order (here, in Playnite's filter panel, or a saved filter that
    ///     carries its own) replaces it.
    ///   - Playnite has no such order, so it's a sorter of ours on its list, under the ones for games kept in place and a
    ///     folder's own order (both take whatever sort is on as theirs to go back to).
    /// </summary>
    public partial class ThemeApi
    {
        private class ShuffleSorter : IComparer
        {
            public uint Seed;
            public Func<object, Guid> IdOf;
            private readonly Dictionary<object, uint> ranks = new Dictionary<object, uint>();

            private uint Rank(object o)
            {
                uint r;
                if (ranks.TryGetValue(o, out r)) return r;
                unchecked
                {
                    uint x = (uint)IdOf(o).GetHashCode() ^ Seed;
                    x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
                    r = x;
                }
                ranks[o] = r;
                return r;
            }

            public int Compare(object a, object b)
            {
                // called by WPF inside Playnite's own list updates: must never throw
                if (a == null || b == null) return a == null ? (b == null ? 0 : 1) : -1;
                try
                {
                    int c = Rank(a).CompareTo(Rank(b));
                    return c != 0 ? c : IdOf(a).CompareTo(IdOf(b));
                }
                catch (Exception) { return 0; }
            }
        }

        private ShuffleSorter shuffleSorter;
        private List<SortDescription> shuffleSavedSorts;   // Playnite's own sort, to put back

        /// <summary>The top bar's "Shuffled" mark.</summary>
        public bool SortShuffleOn { get { return settings.SortShuffle; } }

        private string PlayniteSortNow()
        {
            try { return api.MainView.SortOrder + "|" + api.MainView.SortOrderDirection; } catch (Exception) { return ""; }
        }

        /// <summary>Shuffle chosen: on, or (already on) another order.</summary>
        private void SetSortShuffle()
        {
            try
            {
                settings.SortShuffle = true;
                settings.SortShuffleOver = PlayniteSortNow();
                SaveSettings();
                PlaySound("move");
                Notify("SortShuffleOn");
                var old = shuffleSorter;
                shuffleSorter = new ShuffleSorter { Seed = unchecked((uint)Environment.TickCount * 2654435761u), IdOf = EntryId };
                SortShuffleApply(old);
            }
            catch (Exception ex) { logger.Warn("Kippix: shuffle: " + ex.Message); }
            GRowsAgain("sort:shuffle");
        }

        /// <summary>
        /// Puts the shuffle on Playnite's list (from the 2-second tick too: Playnite starting, or its list made anew).
        /// Playnite's sort changed since it was chosen: that's the order now, the shuffle is over.
        /// </summary>
        private void SortShuffleTick()
        {
            if (!settings.SortShuffle || !IsFullscreen) return;
            try
            {
                if (PlayniteSortNow() != (settings.SortShuffleOver ?? "")) { SortShuffleOff(); return; }
                if (shuffleSorter == null) shuffleSorter = new ShuffleSorter { Seed = unchecked((uint)Environment.TickCount * 2654435761u), IdOf = EntryId };
                SortShuffleApply(null);
            }
            catch (Exception ex) { logger.Warn("Kippix: shuffle: " + ex.Message); }
        }

        private void SortShuffleApply(ShuffleSorter old)
        {
            var view = HomeListView();
            if (view == null) return;
            var now = view.CustomSort;
            if (ReferenceEquals(now, shuffleSorter)) return;
            if (now is SlotSorter)
            {
                // games kept in place: the shuffle is the order of the others
                if (ReferenceEquals(slotSavedCustom, shuffleSorter)) return;
                if (!(slotSavedCustom is ShuffleSorter) && slotSavedSorts != null && slotSavedSorts.Count > 0) shuffleSavedSorts = slotSavedSorts.ToList();
                slotSavedCustom = shuffleSorter;
                slotSig = null;
                ApplySlotPins(true);
                return;
            }
            if (now is FolderSorter)
            {
                // an open folder with its own order: the shuffle is what comes back when it closes
                if (ReferenceEquals(folderSortSavedCustom, shuffleSorter)) return;
                if (!(folderSortSavedCustom is ShuffleSorter) && folderSortSaved != null && folderSortSaved.Count > 0) shuffleSavedSorts = folderSortSaved.ToList();
                folderSortSavedCustom = shuffleSorter;
                return;
            }
            if (!(now is ShuffleSorter) && view.SortDescriptions.Count > 0) shuffleSavedSorts = view.SortDescriptions.ToList();
            view.CustomSort = shuffleSorter;   // (setting it sorts the list again)
        }

        /// <summary>Back to Playnite's own sort.</summary>
        private void SortShuffleOff()
        {
            if (!settings.SortShuffle && shuffleSorter == null) return;
            settings.SortShuffle = false;
            SaveSettings();
            Notify("SortShuffleOn");
            var saved = shuffleSavedSorts;
            shuffleSorter = null;
            shuffleSavedSorts = null;
            try
            {
                var view = HomeListView();
                if (view == null) return;
                var now = view.CustomSort;
                if (now is ShuffleSorter)
                {
                    view.CustomSort = null;
                    if (saved != null && view.SortDescriptions.Count == 0)
                        using (view.DeferRefresh()) { foreach (var sd in saved) view.SortDescriptions.Add(sd); }
                }
                else if (now is SlotSorter && slotSavedCustom is ShuffleSorter)
                {
                    slotSavedCustom = null;
                    if (saved != null) slotSavedSorts = saved;
                    slotSig = null;
                    ApplySlotPins(true);
                }
                else if (now is FolderSorter && folderSortSavedCustom is ShuffleSorter)
                {
                    folderSortSavedCustom = null;
                    if (saved != null) folderSortSaved = saved;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: shuffle off: " + ex.Message); }
        }
    }
}
