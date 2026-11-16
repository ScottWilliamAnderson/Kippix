using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>A tile on the Who's playing? screen.</summary>
    public class WhoTile : Observable
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Sub { get; set; }
        public string Initial { get { return string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant(); } }
        public bool IsCurrent { get; set; }
        public bool IsLocked { get; set; }
        private ImageSource picture;
        public ImageSource Picture { get { return picture; } set { if (Set(ref picture, value, "Picture")) Notify("HasPicture"); } }
        public bool HasPicture { get { return picture != null; } }
        public ICommand Command { get; set; }
        internal string PicturePath;
    }

    /// <summary>
    /// Profiles (testing: switched on in the helper's desktop settings, off by default). Each person who plays on this PC
    /// has their own layout (unless Sync layouts), favourites, wishlist, collections, kept-in-place games, recent games and
    /// play time (the main profile keeps using Playnite's own), Store settings, and can be linked to one of the Steam accounts
    /// Steam remembers on this PC. Kids hides games above an age rating (and chosen games); Guest changes nothing for good.
    /// The Store and the settings can be hidden, or need the grown-ups' PIN, per profile.
    /// Launching a Steam game signs Steam into that profile's account first if needed (Steam restarts: about 10–30 s).
    /// </summary>
    public partial class ThemeApi
    {
        private const string GuestId = "guest";
        private static readonly PlayerProfile GuestProfile = new PlayerProfile { Id = GuestId, Name = "Guest", Kind = "guest", StoreAccess = 1, SettingsAccess = 1, OnlyOwnSteamGames = false };

        internal bool ProfilesOn { get { return settings.ProfilesEnabled && settings.Profiles != null && settings.Profiles.Count > 0; } }
        private bool GuestActive { get { return ProfilesOn && settings.CurrentProfile == GuestId; } }
        private PlayerProfile OwnerProfile { get { return ProfilesOn ? settings.Profiles[0] : null; } }

        /// <summary>Who's playing now (null with profiles off).</summary>
        private PlayerProfile CurrentProfile
        {
            get
            {
                if (!ProfilesOn) return null;
                if (settings.CurrentProfile == GuestId) return GuestProfile;
                return settings.Profiles.FirstOrDefault(p => p.Id == settings.CurrentProfile) ?? settings.Profiles[0];
            }
        }
        private string CurrentProfileId { get { var p = CurrentProfile; return p != null ? p.Id : ""; } }
        /// <summary>The first profile is the PC's main one: its favourites, recent games and play time are Playnite's own.</summary>
        private bool IsOwnerProfile { get { return !ProfilesOn || CurrentProfile == OwnerProfile; } }

        private string ProfilesDir { get { return Path.Combine(plugin.GetPluginUserDataPath(), "profiles"); } }
        private PlayerProfile profileNow;          // a copy of CurrentProfile for other threads (the library filter)
        private HashSet<string> ownedNow;           // the profile's Steam app ids (null: not known, nothing hidden)
        private Dictionary<string, DateTime> profileLastPlayed = new Dictionary<string, DateTime>();
        private readonly HashSet<string> unlockedNow = new HashSet<string>();   // the PIN was given for these this time (store, settings)

        // fields that belong to a person (always), and the layout (unless every profile shares one)
        private static readonly HashSet<string> PersonalFields = new HashSet<string>
        {
            "StoreWishlist", "WishReleaseNotices", "StoreRecent", "WishImportAt", "WishDismissed", "WishRemoveFromSteam", "PinnedGames", "Collections", "RecentSearches", "SlotPins", "Folders",
            "ProfileNameOverride", "ProfilePicturePath", "ImportSteamWishlist", "ImportGogWishlist", "SyncWishToSteam", "CurrentLayout",
            "UpNext"
        };

        private bool PerProfileField(string f)
        {
            if (PersonalFields.Contains(f)) return true;
            if (f.StartsWith("Store", StringComparison.Ordinal)) return true;   // the Store's own settings (stores shown, rows, prices...)
            return !settings.SyncLayouts && InProfile(FieldGroup(f));
        }

        // =====================================================================
        // Turning profiles on, starting up
        // =====================================================================

        /// <summary>From the helper's desktop settings (testing).</summary>
        internal bool ProfilesEnabled
        {
            get { return settings.ProfilesEnabled; }
            set
            {
                if (settings.ProfilesEnabled == value) return;
                if (!value && ProfilesOn && settings.CurrentProfile != OwnerProfile.Id)
                {
                    // switched off while someone else (or Guest) was playing: the main profile's own settings come back
                    var owner = OwnerProfile;
                    if (!GuestActive) SaveProfileSnapshot(settings.CurrentProfile);
                    settings.CurrentProfile = owner.Id;
                    LoadProfileSnapshot(owner, false);
                    settings.ProfileNameOverride = owner.Name;
                    settings.ProfilePicturePath = owner.PicturePath ?? "";
                    NormalizeSettings();
                    profileNow = null;
                }
                settings.ProfilesEnabled = value;
                if (value)
                {
                    EnsureProfiles();
                    // switched back on: the filter and the profile's Steam games follow whoever is playing again
                    if (ProfilesOn) { profileNow = CurrentProfile; LoadProfileExtras(profileNow); }
                }
                SaveSettings();
                OnUi(() => { NotifyProfiles(); ApplyListFilter(true); });
            }
        }

        internal bool WelcomeOnBoot { get { return settings.WelcomeOnBoot; } set { settings.WelcomeOnBoot = value; SaveSettings(); } }
        internal bool WelcomeSkippable { get { return settings.WelcomeSkippable; } set { settings.WelcomeSkippable = value; SaveSettings(); NotifyTour(); } }
        internal string CommunityRepoSetting { get { return settings.CommunityRepo ?? ""; } set { settings.CommunityRepo = (value ?? "").Trim(); SaveSettings(); communityLists = null; } }

        /// <summary>The first time: you (from your name and picture now) and a Kids profile.</summary>
        private void EnsureProfiles()
        {
            if (settings.Profiles == null) settings.Profiles = new List<PlayerProfile>();
            if (settings.Profiles.Count > 0) return;
            var me = new PlayerProfile { Id = NewId(), Name = ProfileName, PicturePath = settings.ProfilePicturePath };
            settings.Profiles.Add(me);
            settings.Profiles.Add(new PlayerProfile { Id = NewId(), Name = Loc("LOC_KX_KidsName", "Kids"), Kind = "kids", MaxAge = 12, StoreAccess = 2, SettingsAccess = 1, HideUnrated = false });
            settings.CurrentProfile = me.Id;
            profileNow = me;
            SaveProfileSnapshot(me.Id);
        }

        private static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        /// <summary>When Playnite starts: Guest never lasts (back to the main profile), and the profile's bits are read.</summary>
        private void StartProfiles()
        {
            if (!ProfilesOn) { profileNow = null; return; }
            if (settings.CurrentProfile == GuestId || !settings.Profiles.Any(p => p.Id == settings.CurrentProfile))
            {
                var owner = OwnerProfile;
                settings.CurrentProfile = owner.Id;
                LoadProfileSnapshot(owner, false);
                settings.ProfileNameOverride = owner.Name;
                settings.ProfilePicturePath = owner.PicturePath ?? "";
                NormalizeSettings();
                SaveSettings();
            }
            profileNow = CurrentProfile;
            LoadProfileExtras(profileNow);
        }

        // =====================================================================
        // Switching
        // =====================================================================

        /// <summary>False when it couldn't be written (then switching away would lose this profile's changes).</summary>
        private bool SaveProfileSnapshot(string id)
        {
            if (string.IsNullOrEmpty(id) || id == GuestId) return true;
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string file = Path.Combine(ProfilesDir, id + ".json"), tmp = file + ".tmp";
                // a list changed by a background job while it's written out fails once: try again
                string text = null;
                for (int i = 0; text == null; i++)
                {
                    try { text = js.Serialize(settings); }
                    catch (InvalidOperationException) { if (i >= 2) throw; Thread.Sleep(50); }
                }
                File.WriteAllText(tmp, text, Encoding.UTF8);
                if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: profile save: " + ex.Message); return false; }
        }

        /// <summary>Puts a profile's own settings in place. A new profile keeps the layout showing now and starts with no data of its own;
        /// Guest borrows the main profile's layout, with nothing of anyone's.</summary>
        private void LoadProfileSnapshot(PlayerProfile p, bool guest)
        {
            HelperSettings src = null;
            try
            {
                string id = guest ? OwnerProfile.Id : p.Id;
                string file = Path.Combine(ProfilesDir, id + ".json");
                if (File.Exists(file)) src = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<HelperSettings>(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (Exception ex) { logger.Warn("Kippix: profile load: " + ex.Message); }
            // the main profile with no snapshot (or one that can't be read): what's in place now is its own, so it stays
            if (src == null && !guest && p == OwnerProfile) return;
            var blank = new HelperSettings();
            foreach (var f in typeof(HelperSettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!PerProfileField(f.Name)) continue;
                // a person's own data starts empty; the layout and the Store's settings carry over to a brand new profile
                bool data = PersonalFields.Contains(f.Name) || f.Name == "StoreWishlist" || f.Name == "StoreRecent";
                if (guest && data) f.SetValue(settings, f.GetValue(blank));
                else if (src != null) f.SetValue(settings, f.GetValue(src));
                else if (data) f.SetValue(settings, f.GetValue(blank));
            }
        }

        private void SwitchProfile(PlayerProfile p)
        {
            if (p == null || !ProfilesOn) return;
            if (p.Id == settings.CurrentProfile) { CloseWho(); return; }
            try
            {
                SaveSettings();
                if (!GuestActive && !SaveProfileSnapshot(settings.CurrentProfile))
                {
                    // not saved: staying put keeps this profile's changes
                    PlaySound("back");
                    try { ShowNotice(Loc("LOC_KX_ProfilesHeader", "PROFILES"), "", Loc("LOC_KX_ProfileSaveFailed", "Couldn't save this profile, so it wasn't switched. Try again."), "", null); } catch (Exception) { }
                    CloseWho();
                    return;
                }
                bool guest = p.Id == GuestId;
                settings.CurrentProfile = p.Id;
                LoadProfileSnapshot(p, guest);
                settings.ProfileNameOverride = guest ? Loc("LOC_KX_Guest", "Guest") : p.Name;
                settings.ProfilePicturePath = guest ? "" : (p.PicturePath ?? "");
                NormalizeSettings();
                unlockedNow.Clear();
                try { if (File.Exists(UndoFile)) File.Delete(UndoFile); } catch (Exception) { }   // Undo reset was the last person's
                ownedNow = null;   // (the last person's Steam games and dates never filter the new one, even for a moment)
                profileLastPlayed = new Dictionary<string, DateTime>();
                profileNow = p;
                profilePicture = null;
                profilePictureTried = false;
                LoadProfileExtras(p);
                ApplyAllSettings();
                NotifyProfile();
                NotifyProfiles();
                ApplyListFilter(true);
                try { RebuildExtraRowsSoon(); } catch (Exception) { }
                try { if (IsBoardMode) BoardRefreshSoon(); } catch (Exception) { }
                storeRowsAt = DateTime.MinValue;
                PlaySound("select");
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: switching profile"); }
            CloseWho();
        }

        /// <summary>A profile's Steam games and recent games, read when it's chosen.</summary>
        private void LoadProfileExtras(PlayerProfile p)
        {
            ownedNow = null;
            profileLastPlayed = new Dictionary<string, DateTime>();
            if (p == null) return;
            if (!string.IsNullOrEmpty(p.SteamId64) && p.OnlyOwnSteamGames) RefreshOwnedSteam(p);
            if (p != OwnerProfile && p.Id != GuestId)
            {
                string id = p.Id;
                KippixShared.SafeWork.Queue(_ =>
                {
                    var map = new Dictionary<string, DateTime>();
                    try
                    {
                        foreach (var s in OwnSessions(3650).Where(x => x.Profile == id && !string.IsNullOrEmpty(x.GameId)))
                        {
                            DateTime end = s.Start.AddSeconds(s.Seconds), was;
                            if (!map.TryGetValue(s.GameId, out was) || end > was) map[s.GameId] = end;
                        }
                    }
                    catch (Exception) { }
                    OnUi(() => { if (CurrentProfileId == id) { profileLastPlayed = map; try { RebuildExtraRowsSoon(); } catch (Exception) { } } });
                });
            }
        }

        // =====================================================================
        // What a profile sees
        // =====================================================================

        /// <summary>Is this game shown for whoever's playing? (safe from any thread)</summary>
        private bool ProfileAllowsGame(Game g)
        {
            var p = profileNow;
            if (p == null || g == null || !settings.ProfilesEnabled) return true;
            string id = g.Id.ToString();
            if (p.HiddenGames != null && p.HiddenGames.Contains(id)) return false;
            if (p.MaxAge > 0)
            {
                int age = GameAge(g);
                if (age < 0 ? p.HideUnrated : age > p.MaxAge) return false;
            }
            bool steam = g.PluginId == SteamLibId;
            if (steam)
            {
                var owned = ownedNow;
                if (owned != null && p.OnlyOwnSteamGames && !string.IsNullOrEmpty(p.SteamId64))
                {
                    bool family = g.Source != null && g.Source.Name != null && g.Source.Name.IndexOf("Family", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!family && !owned.Contains(g.GameId ?? "")) return false;
                }
                return true;
            }
            return !p.HideNonSteam;
        }

        private bool ProfileAllowsId(Guid id)
        {
            if (profileNow == null || id == Guid.Empty) return true;
            Game g = null;
            try { g = api.Database.Games.Get(id); } catch (Exception) { }
            return g == null || ProfileAllowsGame(g);
        }

        /// <summary>This profile's settings need the PIN (or aren't open to it): no editing by holding A either.</summary>
        private bool SettingsLockedNow
        {
            get { var p = profileNow; return p != null && settings.ProfilesEnabled && p.SettingsAccess != 0 && !unlockedNow.Contains("settings") && !string.IsNullOrEmpty(settings.ProfilesPin); }
        }

        private bool ProfileFilterActive
        {
            get
            {
                var p = profileNow;
                if (p == null || !settings.ProfilesEnabled) return false;
                return (p.HiddenGames != null && p.HiddenGames.Count > 0) || p.MaxAge > 0 || p.HideNonSteam || (p.OnlyOwnSteamGames && !string.IsNullOrEmpty(p.SteamId64) && ownedNow != null);
            }
        }

        /// <summary>The lowest age a game is rated for (PEGI / ESRB / USK / CERO names), -1 if it has none.</summary>
        private static int GameAge(Game g)
        {
            int best = -1;
            try
            {
                if (g.AgeRatings == null) return -1;
                foreach (var r in g.AgeRatings)
                {
                    if (r == null || string.IsNullOrEmpty(r.Name)) continue;
                    int a = RatingAge(r.Name);
                    if (a > best) best = a;
                }
            }
            catch (Exception) { }
            return best;
        }

        private static int RatingAge(string name)
        {
            string n = name.Trim().ToUpperInvariant();
            var m = Regex.Match(n, @"\b(\d{1,2})\+?\b");
            if (m.Success && !n.StartsWith("ESRB")) return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            // (the -teens first: "SEVENTEEN" has "SEVEN" in it)
            if (n.Contains("THIRTEEN")) return 13;
            if (n.Contains("SEVENTEEN")) return 17;
            if (n.Contains("THREE")) return 3;
            if (n.Contains("SEVEN")) return 7;
            if (n.Contains("TWELVE")) return 12;
            if (n.Contains("SIXTEEN")) return 16;
            if (n.Contains("EIGHTEEN")) return 18;
            if (n.StartsWith("ESRB"))
            {
                string t = n.Substring(4).Trim().Replace("+", "");
                switch (t)
                {
                    case "EC": return 3;
                    case "E": case "EVERYONE": return 6;
                    case "E10": return 10;
                    case "T": case "TEEN": return 13;
                    case "M": case "MATURE": return 17;
                    case "AO": return 18;
                }
                // "ESRB Mature 17+", "ESRB Everyone 10+"...: the age in it
                var me = Regex.Match(t, @"\b(\d{1,2})\b");
                if (me.Success) return int.Parse(me.Groups[1].Value, CultureInfo.InvariantCulture);
                if (t.StartsWith("MATURE")) return 17;
                if (t.StartsWith("TEEN")) return 13;
                if (t.StartsWith("ADULT")) return 18;
            }
            if (n.StartsWith("CERO"))
            {
                string t = n.Substring(4).Trim();
                switch (t) { case "A": return 0; case "B": return 12; case "C": return 15; case "D": return 17; case "Z": return 18; }
            }
            return -1;
        }

        /// <summary>Favourites: Playnite's for the main profile, the profile's own list for everyone else.</summary>
        private bool IsFavouriteNow(Game g)
        {
            if (g == null) return false;
            var p = profileNow;
            if (p == null || !settings.ProfilesEnabled || p == OwnerProfile) return g.Favorite;
            return p.Favourites != null && p.Favourites.Contains(g.Id.ToString());
        }

        /// <summary>When the game was last played: Playnite's date for the main profile, the theme's own records for everyone else.</summary>
        private DateTime? LastPlayedNow(Game g)
        {
            if (g == null) return null;
            var p = profileNow;
            if (p == null || !settings.ProfilesEnabled || p == OwnerProfile) return g.LastActivity;
            DateTime d;
            return profileLastPlayed.TryGetValue(g.Id.ToString(), out d) ? (DateTime?)d : null;
        }

        /// <summary>A game was just played (OnGameStopped): it's at the front of this profile's recent games straight away,
        /// as it is for the main profile (its records are only read again when the profile is chosen).</summary>
        private void NoteProfilePlayed(Game g, ulong seconds)
        {
            if (g == null || !ProfilesOn || IsOwnerProfile || !settings.RecordSessions || seconds < 60) return;   // (what RecordSession keeps)
            // a new dictionary: the library filter and row builders read it from other threads
            var map = new Dictionary<string, DateTime>(profileLastPlayed);
            map[g.Id.ToString()] = DateTime.Now;
            profileLastPlayed = map;
            try { RebuildExtraRowsSoon(); if (IsBoardMode) BoardRefreshSoon(); } catch (Exception) { }
        }

        /// <summary>The More menu: a favourite for this profile (not the main one, which uses Playnite's own star).</summary>
        private void AddProfileFavouriteRow(Game g)
        {
            AddUpNextRow(g);   // Up next (UpNext.cs): for every profile, the main one too
            var p = CurrentProfile;
            if (g == null || p == null || p == OwnerProfile || p.Id == GuestId) return;
            var game = g;
            bool fav = p.Favourites != null && p.Favourites.Contains(g.Id.ToString());
            GameMoreRows.Add(new PickRow
            {
                Text = fav ? string.Format(Loc("LOC_KX_ProfUnfav", "Take off {0}'s favourites"), p.Name) : string.Format(Loc("LOC_KX_ProfFav", "Add to {0}'s favourites"), p.Name),
                Sub = Loc("LOC_KX_ProfFavSub", "Their own favourites, for this profile only"),
                Icon = "heart", Key = "proffav",
                Command = new RelayCommand(_ =>
                {
                    if (p.Favourites == null) p.Favourites = new List<string>();
                    string id = game.Id.ToString();
                    if (p.Favourites.Contains(id)) p.Favourites.Remove(id); else p.Favourites.Add(id);
                    SaveSettings();
                    PlaySound(fav ? "back" : "select");
                    ClosePanels();
                    try { RebuildExtraRowsSoon(); if (IsBoardMode) BoardRefreshSoon(); } catch (Exception) { }
                })
            });
            GameMoreRows.Add(new PickRow
            {
                Text = string.Format(Loc("LOC_KX_ProfHide", "Hide it from {0}"), p.Name), Sub = Loc("LOC_KX_ProfHideSub", "Show it again from Settings & Customisation › Profiles"), Icon = "hide", Key = "profhide",
                Command = new RelayCommand(_ =>
                {
                    if (p.HiddenGames == null) p.HiddenGames = new List<string>();
                    if (!p.HiddenGames.Contains(game.Id.ToString())) p.HiddenGames.Add(game.Id.ToString());
                    SaveSettings();
                    PlaySound("back");
                    ClosePanels();
                    CloseGameDetails(true);
                    ApplyListFilter(true);
                })
            });
        }

        // ---------------- Playnite's list: the profile's games only ----------------

        private ListCollectionView listFilterView;
        private Predicate<object> listOrigFilter, listWrapFilter;
        private bool slotsAlwaysShow;   // kept-in-place games pass any filter (Games kept in place › While a filter is on: keep them)

        /// <summary>
        /// Playnite's list filter, with the profile's hidden games taken out, kept-in-place games let through when asked, and
        /// (Folder) only the open folder's games, in place of Playnite's filter (not on top of it).
        /// </summary>
        private void ApplyListFilter(bool refresh)
        {
            if (refresh && FolderMode) FolderRefreshSoon();   // (what's in each folder can change with the profile)
            try
            {
                var view = HomeListView();
                if (view == null) return;
                bool want = ProfileFilterActive || slotsAlwaysShow || folderFilter != null || settings.InstalledOnly;
                if (!ReferenceEquals(view, listFilterView)) { listFilterView = view; listWrapFilter = null; listOrigFilter = null; }
                bool ours = listWrapFilter != null && ReferenceEquals(view.Filter, listWrapFilter);
                if (!want)
                {
                    if (ours) { view.Filter = listOrigFilter; listWrapFilter = null; listOrigFilter = null; }
                    return;
                }
                if (!ours)
                {
                    listOrigFilter = view.Filter;
                    var orig = listOrigFilter;
                    listWrapFilter = o =>
                    {
                        // runs inside Playnite's own list updates: must never throw
                        Guid id = Guid.Empty;
                        try
                        {
                            id = EntryId(o);
                            if (profileNow != null && !ProfileAllowsId(id)) return false;
                            // Folder: exactly the open folder's games, whatever Playnite's filter, saved filter or search says (Playnite's
                            // hidden games, the profile's limits and the folder's Installed choice are already left out of them). Playnite's
                            // own filter isn't touched: it applies again as it was once Folder is no longer chosen.
                            var inFolder = folderFilter;
                            if (inFolder != null) return inFolder.Contains(id);
                            // Installed only (Sort and show): on top of Playnite's filter, whichever saved filter is on
                            if (settings.InstalledOnly) { var game = FolderEntryGame(o); if (game != null && !game.IsInstalled) return false; }
                            if (slotsAlwaysShow && SlotOf(id) > 0) return true;
                        }
                        catch (Exception) { }
                        return orig == null || orig(o);
                    };
                    view.Filter = listWrapFilter;   // (setting it refreshes the list)
                    return;
                }
                if (refresh) view.Refresh();
            }
            catch (Exception ex) { logger.Warn("Kippix: profile filter: " + ex.Message); }
        }

        // =====================================================================
        // The Store and the settings: hidden, or behind the PIN
        // =====================================================================

        private static string PinHash(string pin)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("Kippix|" + (pin ?? "").Trim()))).Replace("-", "");
        }

        /// <summary>True when it stops here (hidden, or the PIN is being asked for: <paramref name="then"/> runs once it's right).</summary>
        private bool ProfileGate(string what, Action then)
        {
            var p = profileNow;
            if (p == null || !settings.ProfilesEnabled || unlockedNow.Contains(what)) return false;
            int access = what == "store" ? p.StoreAccess : p.SettingsAccess;
            if (access == 0) return false;
            if (access == 2 || string.IsNullOrEmpty(settings.ProfilesPin))
            {
                if (access == 1) return false;   // no PIN set yet: nothing to ask for
                PlaySound("back");
                try { ShowNotice(Loc("LOC_KX_NotHere", "NOT IN THIS PROFILE"), what == "store" ? Loc("LOC_KX_Store", "Store") : Loc("LOC_KX_Options", "Settings & Customisation"), string.Format(Loc("LOC_KX_NotHereLine", "{0} can't open this"), p.Name), "", null); } catch (Exception) { }
                return true;
            }
            AskPin(string.Format(Loc("LOC_KX_PinFor", "The PIN, to open {0}"), what == "store" ? Loc("LOC_KX_Store", "Store") : Loc("LOC_KX_Options", "Settings & Customisation")), () => { unlockedNow.Add(what); then(); });
            return true;
        }

        private void AskPin(string title, Action ok)
        {
            OpenKeyboard(title, "", typed =>
            {
                if (PinHash(typed) == settings.ProfilesPin) { PlaySound("select"); ui.BeginInvoke(new Action(() => { try { ok(); } catch (Exception ex) { logger.Error(ex, "Kippix: after the PIN"); } }), DispatcherPriority.Loaded); }
                else { PlaySound("back"); try { ShowNotice(Loc("LOC_KX_PinWrong", "WRONG PIN"), "", Loc("LOC_KX_PinWrongLine", "That isn't the PIN"), "", null); } catch (Exception) { } }
            }, null, null, 12);
        }

        // =====================================================================
        // Who's playing?
        // =====================================================================

        public ObservableCollection<WhoTile> WhoTiles { get; private set; }
        public bool IsWhoOpen { get { return openPanel == "who"; } }
        public string WhoHint { get { var p = CurrentProfile; return p != null ? string.Format(Loc("LOC_KX_WhoHint", "B  Carry on as {0}"), p.Id == GuestId ? Loc("LOC_KX_Guest", "Guest") : p.Name) : ""; } }
        private bool whoAtStartDone;

        private void OpenWho()
        {
            if (!ProfilesOn) return;
            if (WhoTiles == null) WhoTiles = new ObservableCollection<WhoTile>();
            WhoTiles.Clear();
            var list = settings.Profiles.ToList();
            list.Add(GuestProfile);
            foreach (var p in list)
            {
                var prof = p;
                var t = new WhoTile
                {
                    Id = p.Id, Name = p.Id == GuestId ? Loc("LOC_KX_Guest", "Guest") : p.Name, IsCurrent = p.Id == settings.CurrentProfile,
                    IsLocked = (p.NeedsPin || (p.Id == GuestId && AnyTimeLimits)) && !string.IsNullOrEmpty(settings.ProfilesPin),
                    Sub = p.Id == GuestId ? Loc("LOC_KX_GuestSub", "Nothing is kept") : (LimitLeftText(p) ?? (p.Kind == "kids" ? Loc("LOC_KX_KidsSub", "Kids") : (p.SteamPersona ?? ""))),
                    PicturePath = p.PicturePath,
                    Command = new RelayCommand(_ => ChooseProfile(prof))
                };
                WhoTiles.Add(t);
                if (!string.IsNullOrEmpty(t.PicturePath) && File.Exists(t.PicturePath))
                {
                    var tt = t;
                    KippixShared.SafeWork.Queue(_ => { var img = Images.LoadUncached(tt.PicturePath, 240); if (img != null) OnUi(() => tt.Picture = img); });
                }
            }
            Notify("WhoTiles");
            Notify("WhoHint");
            if (openPanel != "who") TogglePanel("who");
            var cur = WhoTiles.FirstOrDefault(t => t.IsCurrent) ?? WhoTiles.FirstOrDefault();
            ui.BeginInvoke(new Action(() => { if (cur != null) UiTools.FocusItem("KX_WhoList", cur); else UiTools.FocusInside("KX_WhoView"); }), DispatcherPriority.ContextIdle);
        }

        private void ChooseProfile(PlayerProfile p)
        {
            // (Guest would be a way round a daily time limit: while any profile has one, it needs the PIN too)
            bool guestLocked = p.Id == GuestId && AnyTimeLimits;
            if ((p.NeedsPin || guestLocked) && !string.IsNullOrEmpty(settings.ProfilesPin) && p.Id != settings.CurrentProfile)
            {
                AskPin(string.Format(Loc("LOC_KX_PinForProfile", "The PIN, for {0}"), p.Name), () => SwitchProfile(p));
                return;
            }
            SwitchProfile(p);
        }

        private void CloseWho()
        {
            if (openPanel == "who") ClosePanels();
        }

        /// <summary>When Playnite has started (the home screen is showing): the welcome first, then Who's playing?.</summary>
        private void StartupScreens()
        {
            if (whoAtStartDone) return;
            whoAtStartDone = true;
            if (!IsFullscreen) return;
            if (WelcomeShouldShow()) { OpenWelcome(); return; }
            if (ProfilesOn && settings.AskWhoAtStart) OpenWho();
        }

        // =====================================================================
        // Steam accounts
        // =====================================================================

        private class SteamAccountInfo
        {
            public string SteamId64, Account, Persona;
            public bool Remembered;
        }

        private static string SteamDir()
        {
            try
            {
                string p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                return string.IsNullOrEmpty(p) ? null : p.Replace('/', '\\');
            }
            catch (Exception) { return null; }
        }

        /// <summary>The accounts Steam remembers on this PC (config\loginusers.vdf). Nothing is changed in it.</summary>
        private static List<SteamAccountInfo> SteamAccounts()
        {
            var list = new List<SteamAccountInfo>();
            try
            {
                string dir = SteamDir();
                if (dir == null) return list;
                string file = Path.Combine(dir, "config", "loginusers.vdf");
                if (!File.Exists(file)) return list;
                string text;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();
                foreach (Match m in Regex.Matches(text, "\"(7656\\d{13})\"\\s*\\{([^}]*)\\}"))
                {
                    string body = m.Groups[2].Value;
                    Func<string, string> field = k =>
                    {
                        var f = Regex.Match(body, "\"" + k + "\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
                        return f.Success ? f.Groups[1].Value.Replace("\\\"", "\"") : "";
                    };
                    list.Add(new SteamAccountInfo { SteamId64 = m.Groups[1].Value, Account = field("AccountName"), Persona = field("PersonaName"), Remembered = field("RememberPassword") == "1" && field("AllowAutoLogin") != "0" });
                }
            }
            catch (Exception) { }
            return list;
        }

        private static long AccountIdOf(string steamId64)
        {
            long id;
            return long.TryParse(steamId64, out id) ? id - 76561197960265728L : 0;
        }

        /// <summary>The signed-in account id (a DWORD: read unsigned, so ids above 2^31 still compare with AccountIdOf).</summary>
        private static long SteamActiveUser()
        {
            try { object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0); return v is int ? (long)unchecked((uint)(int)v) : 0; }
            catch (Exception) { return 0; }
        }

        private static bool SteamGameRunning()
        {
            try { object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "RunningAppID", 0); return v is int && (int)v != 0; }
            catch (Exception) { return false; }
        }

        private static bool SteamRunning()
        {
            try
            {
                var all = Process.GetProcessesByName("steam");
                foreach (var p in all) p.Dispose();   // (asked twice a second while switching: the handles go straight back)
                return all.Length > 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Is Steam downloading or updating something? (the app manifests in each library)</summary>
        private static bool SteamDownloading()
        {
            try
            {
                string dir = SteamDir();
                if (dir == null) return false;
                var libs = new List<string> { dir };
                string lf = Path.Combine(dir, "steamapps", "libraryfolders.vdf");
                if (File.Exists(lf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(lf), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                foreach (string lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string apps = Path.Combine(lib, "steamapps");
                    if (!Directory.Exists(apps)) continue;
                    foreach (string acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                    {
                        var m = Regex.Match(File.ReadAllText(acf), "\"StateFlags\"\\s*\"(\\d+)\"");
                        long flags;
                        if (m.Success && long.TryParse(m.Groups[1].Value, out flags) && (flags & (256 | 1024 | 1048576 | 2097152 | 4194304 | 524288 | 262144 | 131072)) != 0) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        // ---------------- the profile's Steam games (Steam Web API) ----------------

        private void RefreshOwnedSteam(PlayerProfile p)
        {
            string sid = p.SteamId64;
            string cache = Path.Combine(ProfilesDir, "steam-" + sid + ".json");
            try
            {
                if (File.Exists(cache))
                {
                    var ids = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<string>>(File.ReadAllText(cache));
                    if (ids != null && ids.Count > 0) ownedNow = new HashSet<string>(ids);
                    if ((DateTime.Now - File.GetLastWriteTime(cache)).TotalHours < 12) return;
                }
            }
            catch (Exception) { }
            string key = SteamKey;
            if (string.IsNullOrEmpty(key)) return;
            string pid = p.Id;
            KippixShared.SafeWork.Queue(_ =>
            {
                try
                {
                    var root = GetJson(SteamApi + "IPlayerService/GetOwnedGames/v1/?include_played_free_games=1&key=" + Uri.EscapeDataString(key) + "&steamid=" + sid);
                    var resp = root != null && root.ContainsKey("response") ? root["response"] as Dictionary<string, object> : null;
                    var games = resp != null && resp.ContainsKey("games") ? resp["games"] as object[] : null;
                    if (games == null) return;   // a private profile: nothing hidden
                    var ids = games.OfType<Dictionary<string, object>>().Select(d => FStr(d, "appid")).Where(x => x.Length > 0).ToList();
                    Directory.CreateDirectory(ProfilesDir);
                    File.WriteAllText(cache, new JavaScriptSerializer().Serialize(ids));
                    // (still this profile, and still linked to the same account)
                    OnUi(() => { var cp = CurrentProfile; if (cp != null && cp.Id == pid && cp.SteamId64 == sid) { ownedNow = new HashSet<string>(ids); ApplyListFilter(true); try { RebuildExtraRowsSoon(); } catch (Exception) { } } });
                }
                catch (Exception ex) { logger.Warn("Kippix: profile Steam games: " + ex.Message); }
            });
        }

        // ---------------- launching a Steam game as the profile's account ----------------

        public bool IsSwitchingSteam { get; private set; }
        public string SwitchingText { get; private set; }
        private Guid steamSwitchBypass;

        /// <summary>From OnGameStarting: true when Steam has to sign into this profile's account first (the launch is held, then started again).</summary>
        public bool SteamSwitchFirst(Game g)
        {
            try
            {
                if (g == null || !ProfilesOn || g.PluginId != SteamLibId || !IsFullscreen) return false;   // (profiles are the fullscreen theme's)
                if (steamSwitchBypass == g.Id) { steamSwitchBypass = Guid.Empty; return false; }
                if (IsSwitchingSteam) return true;   // Steam is restarting for a launch already: not a second one meanwhile
                var p = CurrentProfile;
                if (p == null || string.IsNullOrEmpty(p.SteamAccount) || string.IsNullOrEmpty(p.SteamId64)) return false;
                long want = AccountIdOf(p.SteamId64);
                long active = SteamActiveUser();
                if (active != 0 && active == want) return false;
                if (!SteamRunning())
                {
                    // Steam isn't open: it starts with this account
                    try { Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", p.SteamAccount, RegistryValueKind.String); } catch (Exception) { }
                    return false;
                }
                if (gameRunningNow || SteamGameRunning())
                {
                    OnUi(() => { try { ShowNotice(Loc("LOC_KX_SteamSwitch", "STEAM"), p.SteamPersona ?? p.SteamAccount, Loc("LOC_KX_SwitchGameRunning", "A game is running: Steam can't switch accounts until it's closed"), "", null); } catch (Exception) { } });
                    return true;
                }
                var game = g;
                OnUi(() =>
                {
                    if (SteamDownloading())
                        Confirm(Loc("LOC_KX_SwitchDownloadTitle", "Steam is downloading"),
                                string.Format(Loc("LOC_KX_SwitchDownloadText", "Switching to {0} closes Steam, so the download pauses until Steam is back. Switch now?"), p.SteamPersona ?? p.SteamAccount),
                                Loc("LOC_KX_Switch", "Switch"), () => StartSteamSwitch(p, game));
                    else StartSteamSwitch(p, game);
                });
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: Steam switch: " + ex.Message); return false; }
        }

        private void StartSteamSwitch(PlayerProfile p, Game g)
        {
            if (IsSwitchingSteam) return;
            IsSwitchingSteam = true;
            SwitchingText = string.Format(Loc("LOC_KX_SwitchingTo", "Switching Steam to {0}…"), string.IsNullOrEmpty(p.SteamPersona) ? p.SteamAccount : p.SteamPersona);
            Notify("IsSwitchingSteam");
            Notify("SwitchingText");
            string account = p.SteamAccount;
            long want = AccountIdOf(p.SteamId64);
            Guid gameId = g.Id;
            KippixShared.SafeWork.Queue(_ =>
            {
                string problem = null;
                try
                {
                    string dir = SteamDir();
                    string exe = dir != null ? Path.Combine(dir, "steam.exe") : null;
                    if (exe == null || !File.Exists(exe)) problem = Loc("LOC_KX_SwitchNoSteam", "Steam wasn't found");
                    else
                    {
                        using (Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false })) { }
                        var until = DateTime.Now.AddSeconds(30);
                        while (SteamRunning() && DateTime.Now < until) Thread.Sleep(500);
                        if (SteamRunning()) problem = Loc("LOC_KX_SwitchNoClose", "Steam didn't close");
                        else
                        {
                            Thread.Sleep(800);
                            // the one change: which account Steam signs into by itself (no passwords, nothing else)
                            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", account, RegistryValueKind.String);
                            using (Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir })) { }
                            until = DateTime.Now.AddSeconds(75);
                            while (SteamActiveUser() != want && DateTime.Now < until) Thread.Sleep(700);
                            if (SteamActiveUser() != want) problem = Loc("LOC_KX_SwitchNoSignIn", "Steam didn't sign in by itself: in Steam, sign into that account once with Remember me ticked");
                            else Thread.Sleep(2500);   // the library finishes loading
                        }
                    }
                }
                catch (Exception ex) { problem = ex.Message; logger.Warn("Kippix: Steam switch: " + ex.Message); }
                OnUi(() =>
                {
                    IsSwitchingSteam = false;
                    Notify("IsSwitchingSteam");
                    if (problem != null)
                    {
                        try { ShowNotice(Loc("LOC_KX_SteamSwitch", "STEAM"), p.SteamPersona ?? account, problem, "", null); } catch (Exception) { }
                        return;
                    }
                    steamSwitchBypass = gameId;
                    try { api.StartGame(gameId); } catch (Exception ex) { steamSwitchBypass = Guid.Empty; logger.Warn("Kippix: start after switch: " + ex.Message); }
                });
            });
        }

        // =====================================================================
        // Settings › Profiles
        // =====================================================================

        public bool IsOptionsProfiles { get { return optionsPage == "profiles" || optionsPage == "profileedit" || optionsPage == "profilesteam" || optionsPage == "profiletime"; } }
        public bool ShowProfilesRow { get { return settings.ProfilesEnabled; } }
        private ObservableCollection<PickRow> profileRows;
        public ObservableCollection<PickRow> ProfileOptionRows { get { return profileRows ?? (profileRows = new ObservableCollection<PickRow>()); } }
        private string editingProfileId;
        public string ProfilesSummary
        {
            get
            {
                var p = CurrentProfile;
                return p == null ? "" : string.Format(Loc("LOC_KX_ProfilesSummary", "Playing: {0} · {1} profiles"), p.Id == GuestId ? Loc("LOC_KX_Guest", "Guest") : p.Name, settings.Profiles.Count);
            }
        }

        private void NotifyProfiles()
        {
            foreach (string n in new[] { "ShowProfilesRow", "ProfilesSummary", "WhoHint", "IsOptionsProfiles" }) Notify(n);
        }

        private PlayerProfile EditingProfile { get { return settings.Profiles != null ? settings.Profiles.FirstOrDefault(p => p.Id == editingProfileId) : null; } }

        private void BuildProfileOptionRows()
        {
            var rows = ProfileOptionRows;
            rows.Clear();
            if (!settings.ProfilesEnabled) { rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "off", Text = Loc("LOC_KX_ProfilesOff", "Profiles are switched off (the helper's settings in desktop mode)") }); return; }
            EnsureProfiles();
            if (optionsPage == "profileedit") { BuildProfileEditRows(); return; }
            if (optionsPage == "profilesteam") { BuildProfileSteamRows(); return; }
            if (optionsPage == "profiletime") { BuildProfileTimeRows(); return; }   // (TimeLimits.cs)
            Action again = () => BuildProfileOptionRows();
            rows.Add(new PickRow { Key = "who", Icon = "friends", Text = Loc("LOC_KX_WhosPlaying", "Who's playing?"), Sub = ProfilesSummary, Command = new RelayCommand(_ => { PlaySound("select"); ClosePanels(); ui.BeginInvoke(new Action(OpenWho), DispatcherPriority.Loaded); }) });
            rows.Add(BoardSwitch("play", "atstart", Loc("LOC_KX_WhoAtStart", "Ask who's playing when Playnite starts"), settings.AskWhoAtStart, Loc("LOC_KX_WhoAtStartOn", "The Who's playing? screen comes up first"), Loc("LOC_KX_WhoAtStartOff", "Off: the last profile carries on (change it from the profile menu)"),
                                 () => { settings.AskWhoAtStart = !settings.AskWhoAtStart; SaveSettings(); PlaySound("move"); BoardRowsAgain(again, "atstart"); }));
            rows.Add(BoardSwitch("layout", "sync", Loc("LOC_KX_SyncLayouts", "Same layout for everyone"), settings.SyncLayouts, Loc("LOC_KX_SyncLayoutsOn", "Changing the layout changes it for every profile"), Loc("LOC_KX_SyncLayoutsOff", "Off: each profile has its own layout"),
                                 () => { settings.SyncLayouts = !settings.SyncLayouts; SaveSettings(); PlaySound("move"); BoardRowsAgain(again, "sync"); }));
            rows.Add(new PickRow
            {
                Key = "pin", Icon = "lock", Text = string.IsNullOrEmpty(settings.ProfilesPin) ? Loc("LOC_KX_SetPin", "Set the grown-ups' PIN") : Loc("LOC_KX_ChangePin", "Change the grown-ups' PIN"),
                Sub = Loc("LOC_KX_ProfilePinSub", "For locked profiles, and the Store or settings where a profile needs it"),
                Command = new RelayCommand(_ => SetGrownUpsPin(() => BoardRowsAgain(again, "pin")))
            });
            AddProfilesLimitRows(rows);   // DAILY TIME LIMITS: each profile's screen time, A opens it (TimeLimits.cs)
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_ProfilesHeader", "PROFILES") });
            for (int i = 0; i < settings.Profiles.Count; i++)
            {
                var p = settings.Profiles[i];
                string id = p.Id;
                var bits = new List<string>();
                if (i == 0) bits.Add(Loc("LOC_KX_MainProfile", "Main profile"));
                if (p.Kind == "kids") bits.Add(p.MaxAge > 0 ? string.Format(Loc("LOC_KX_KidsUpTo", "Kids, up to age {0}"), p.MaxAge) : Loc("LOC_KX_KidsSub", "Kids"));
                if (!string.IsNullOrEmpty(p.SteamPersona)) bits.Add("Steam: " + p.SteamPersona);
                if (p.Id == settings.CurrentProfile) bits.Add(Loc("LOC_KX_PlayingNow2", "playing now"));
                if (bits.Count == 0) bits.Add(Loc("LOC_KX_ProfileRowSub", "Their name, picture, Steam account and what they can open"));
                rows.Add(new PickRow { Key = "prof:" + id, Icon = p.Kind == "kids" ? "star" : "profile", Text = p.Name, Sub = string.Join(" · ", bits.ToArray()), IsSelected = p.Id == settings.CurrentProfile,
                                       Command = new RelayCommand(_ => { editingProfileId = id; PlaySound("select"); SetOptionsPage("profileedit"); }) });
            }
            rows.Add(new PickRow { Key = "add", Icon = "add", Text = Loc("LOC_KX_AddProfile", "Add a profile"), Sub = Loc("LOC_KX_AddProfileSub", "Someone with their own layout, picture and games"), Command = new RelayCommand(_ => AddProfile("person")) });
            rows.Add(new PickRow { Key = "addkids", Icon = "add", Text = Loc("LOC_KX_AddKids", "Add a Kids profile"), Sub = Loc("LOC_KX_AddKidsSub2", "Games above an age rating are hidden; no Store; then set a daily time limit"), Command = new RelayCommand(_ => AddProfile("kids")) });
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "guestinfo", Text = Loc("LOC_KX_GuestInfo", "Guest is always on the Who's playing? screen: it uses the main layout and keeps nothing") });
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "families", Text = Loc("LOC_KX_FamiliesTip", "Tip: Steam Families lets everyone play each other's Steam games with their own saves and achievements, so Steam needs switching less often") });
        }

        /// <summary>Set (or change, after the PIN now) the grown-ups' PIN; <paramref name="after"/> once it's set.</summary>
        private void SetGrownUpsPin(Action after)
        {
            Action set = () => OpenKeyboard(Loc("LOC_KX_NewPin", "A new PIN (empty: no PIN)"), "", typed =>
            {
                bool first = string.IsNullOrEmpty(settings.ProfilesPin);
                settings.ProfilesPin = string.IsNullOrWhiteSpace(typed) ? "" : PinHash(typed);
                if (first && settings.ProfilesPin.Length > 0 && OwnerProfile != null) OwnerProfile.NeedsPin = true;   // the main profile is locked too (it can be turned off)
                SaveSettings();
                PlaySound("select");
                if (after != null) after();
            }, null, null, 12);
            if (string.IsNullOrEmpty(settings.ProfilesPin)) set(); else AskPin(Loc("LOC_KX_OldPin", "The PIN now"), set);
        }

        /// <summary>(0.88) A Kids profile or not, from its own settings: on gives the Kids starting rules (games up to 12, no Store,
        /// settings behind the PIN); off takes them away.</summary>
        private void SetProfileKids(PlayerProfile p, bool kids)
        {
            p.Kind = kids ? "kids" : "person";
            if (kids) { if (p.MaxAge == 0) p.MaxAge = 12; p.StoreAccess = 2; p.SettingsAccess = 1; }
            else { p.MaxAge = 0; p.HideUnrated = false; p.StoreAccess = 0; p.SettingsAccess = 0; }
        }

        private bool profileEditFromTour;   // (0.88) a profile's settings opened from Getting started: B goes back there

        /// <summary>(0.88) Getting started › Who plays: a profile's own settings (PIN, Kids, picture, Steam…) over the tour; B comes back.</summary>
        private void OpenProfileFromTour(string id)
        {
            editingProfileId = id;
            profileEditFromTour = true;
            welcomeResume = "profiles";
            ClosePanels();
            TogglePanel("options");
            SetOptionsPage("profileedit");
        }

        private void AddProfile(string kind)
        {
            OpenKeyboard(Loc("LOC_KX_ProfileNameAsk", "Their name"), "", typed =>
            {
                string name = (typed ?? "").Trim();
                if (name.Length == 0) return;
                if (name.Length > 30) name = name.Substring(0, 30);
                var p = new PlayerProfile { Id = NewId(), Name = name, Kind = kind };
                if (kind == "kids") { p.MaxAge = 12; p.StoreAccess = 2; p.SettingsAccess = 1; }
                settings.Profiles.Add(p);
                SaveSettings();
                PlaySound("select");
                editingProfileId = p.Id;
                SetOptionsPage("profileedit", kind != "kids");
                // a Kids profile: the highlight goes on its daily time limit (with a note about it above)
                if (kind == "kids")
                    ui.BeginInvoke(new Action(() => { if (optionsPage == "profileedit" && !UiTools.FocusCommandParameter("KX_OptionsPanel", "profiletime")) UiTools.FocusInside("KX_OptionsPanel"); }), DispatcherPriority.ContextIdle);
            }, null, null, 30);
        }

        private static string AccessText(int a)
        {
            return a == 1 ? Loc("LOC_KX_AccessPin", "Needs the PIN") : a == 2 ? Loc("LOC_KX_AccessHidden", "Not available") : Loc("LOC_KX_AccessOpen", "Open");
        }

        private void BuildProfileEditRows()
        {
            var rows = ProfileOptionRows;
            var p = EditingProfile;
            if (p == null) { rows.Add(new PickRow { IsInfo = true, Key = "gone", Text = Loc("LOC_KX_ProfileGone", "That profile isn't there any more") }); return; }
            if (p.HiddenGames == null) p.HiddenGames = new List<string>();
            bool owner = p == OwnerProfile;
            Action again = () => BuildProfileOptionRows();
            Action changed = () => { SaveSettings(); PlaySound("move"); if (p.Id == settings.CurrentProfile) { profileNow = p; ApplyListFilter(true); } };
            rows.Add(new PickRow { Key = "use", Icon = "play", Text = string.Format(Loc("LOC_KX_PlayAs", "Play as {0}"), p.Name), Sub = p.Id == settings.CurrentProfile ? Loc("LOC_KX_PlayingNow2", "playing now") : Loc("LOC_KX_PlayAsSub", "Switch to this profile now"),
                                   Command = new RelayCommand(_ => { ClosePanels(); ChooseProfile(p); }) });
            rows.Add(new PickRow
            {
                Key = "name", Icon = "note", Text = Loc("LOC_KX_YourName", "Name"), Sub = p.Name,
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_ProfileNameAsk", "Their name"), p.Name, typed =>
                {
                    if (string.IsNullOrWhiteSpace(typed)) return;
                    p.Name = typed.Trim();
                    if (p.Id == settings.CurrentProfile) { settings.ProfileNameOverride = p.Name; NotifyProfile(); }
                    SaveSettings();
                    BoardRowsAgain(again, "name");
                }, null, null, 30))
            });
            rows.Add(new PickRow
            {
                Key = "picture", Icon = "gallery", Text = Loc("LOC_KX_Picture", "Picture"), Sub = string.IsNullOrEmpty(p.PicturePath) ? Loc("LOC_KX_PictureNone", "None: their initial") : Path.GetFileName(p.PicturePath),
                Command = new RelayCommand(_ =>
                {
                    string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    string back = optionsPage;
                    OpenPicker(Loc("LOC_KX_PicFromFile", "Choose a picture file"), false, ProfilePicExts, AvatarFiles().Count > 0 ? AvatarsDir : pics, back, file =>
                    {
                        try
                        {
                            Directory.CreateDirectory(ProfilesDir);
                            string dest = CopyPicture(file, Path.Combine(ProfilesDir, p.Id + "-" + DateTime.Now.Ticks));
                            if (dest == null) return;
                            p.PicturePath = dest;
                            if (p.Id == settings.CurrentProfile) { settings.ProfilePicturePath = dest; profilePicture = null; profilePictureTried = false; NotifyProfile(); }
                            SaveSettings();
                        }
                        catch (Exception ex) { logger.Warn("Kippix: profile picture: " + ex.Message); }
                        SetOptionsPage("profileedit");
                    });
                })
            });
            if (!owner)   // (0.88) Kids or not, here as well as when it's made
                rows.Add(BoardSwitch("star", "kids", Loc("LOC_KX_KidsProfile", "Kids profile"), p.Kind == "kids", Loc("LOC_KX_KidsProfileOn", "Games above an age rating hidden, no Store, settings behind the PIN"), Loc("LOC_KX_Off", "Off"),
                                     () => { SetProfileKids(p, p.Kind != "kids"); changed(); BoardRowsAgain(again, "kids"); }));
            rows.Add(BoardSwitch("lock", "needspin", Loc("LOC_KX_NeedsPin", "Needs the PIN to choose"), p.NeedsPin, string.IsNullOrEmpty(settings.ProfilesPin) ? Loc("LOC_KX_SetPinBelow", "On (set the PIN just below)") : Loc("LOC_KX_On", "On"), Loc("LOC_KX_Off", "Off"),
                                 () => { p.NeedsPin = !p.NeedsPin; changed(); BoardRowsAgain(again, "needspin"); }));
            if (p.NeedsPin && string.IsNullOrEmpty(settings.ProfilesPin))
                rows.Add(new PickRow { Key = "setpin", Icon = "lock", Text = Loc("LOC_KX_SetPin", "Set the grown-ups' PIN"), Sub = Loc("LOC_KX_SetPinHereSub", "The one PIN for every locked profile, the Store and settings"),
                                       Command = new RelayCommand(_ => SetGrownUpsPin(() => BoardRowsAgain(again, "needspin"))) });
            if (p.Kind == "kids" && !p.LimitOn && !owner)
                rows.Add(new PickRow { IsInfo = true, Icon = "hourglass", Key = "limittip", Text = string.Format(Loc("LOC_KX_LimitKidsTip", "Screen time: give {0} a daily time limit (just below, behind the grown-ups' PIN). All limits: Settings › Profiles › Daily time limits"), p.Name) });
            rows.Add(ProfileTimeRow(p));   // Daily time limit › (TimeLimits.cs; behind the PIN)

            rows.Add(new PickRow { IsHeader = true, Text = "STEAM" });
            rows.Add(new PickRow { Key = "steam", Icon = "controller", Text = Loc("LOC_KX_SteamAccount", "Steam account"), Sub = string.IsNullOrEmpty(p.SteamAccount) ? Loc("LOC_KX_SteamNone", "None linked") : (p.SteamPersona + " (" + p.SteamAccount + ")"),
                                   Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("profilesteam"); }) });
            if (!string.IsNullOrEmpty(p.SteamAccount))
                rows.Add(BoardSwitch("controller", "ownsteam", Loc("LOC_KX_OwnSteam", "Only this account's Steam games"), p.OnlyOwnSteamGames,
                                     HasSteamKey ? Loc("LOC_KX_OwnSteamOn", "Other accounts' Steam games are hidden (Family Sharing games stay)") : Loc("LOC_KX_OwnSteamNoKey", "Needs your Steam Web API key (desktop settings) to know which games are theirs"),
                                     Loc("LOC_KX_OwnSteamOff", "Off: every Steam game shows"),
                                     () => { p.OnlyOwnSteamGames = !p.OnlyOwnSteamGames; if (p.Id == settings.CurrentProfile) LoadProfileExtras(p); changed(); BoardRowsAgain(again, "ownsteam"); }));   // (the games first, then the filter)
            rows.Add(BoardSwitch("shelf", "nonsteam", Loc("LOC_KX_NonSteam", "Other games (GOG, Epic, emulators…)"), !p.HideNonSteam, Loc("LOC_KX_NonSteamShared", "Shared: they show for this profile"), Loc("LOC_KX_NonSteamHidden", "Hidden for this profile"),
                                 () => { p.HideNonSteam = !p.HideNonSteam; changed(); BoardRowsAgain(again, "nonsteam"); }));

            if (!owner)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_WhatTheyCanDo", "WHAT THEY CAN OPEN") });
                rows.Add(new PickRow { Key = "store", Icon = "store", Text = Loc("LOC_KX_Store", "Store"), Sub = AccessText(p.StoreAccess) + Loc("LOC_KX_AccessNext", ""),
                                       Command = new RelayCommand(_ => { p.StoreAccess = (p.StoreAccess + 1) % 3; changed(); BoardRowsAgain(again, "store"); }) });
                rows.Add(new PickRow { Key = "settingsaccess", Icon = "gear", Text = Loc("LOC_KX_Options", "Settings & Customisation"), Sub = AccessText(p.SettingsAccess == 2 ? 1 : p.SettingsAccess) + Loc("LOC_KX_AccessNext", ""),
                                       Command = new RelayCommand(_ => { p.SettingsAccess = p.SettingsAccess == 0 ? 1 : 0; changed(); BoardRowsAgain(again, "settingsaccess"); }) });
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AgeHeader", "AGE RATINGS") });
                rows.Add(new PickRow { Key = "age", Icon = "flag", Text = p.MaxAge > 0 ? string.Format(Loc("LOC_KX_MaxAge", "Games rated up to age {0}"), p.MaxAge) : Loc("LOC_KX_MaxAgeNone", "Any age rating"),
                                       Sub = Loc("LOC_KX_MaxAgeSub", "Any, 3, 7, 12, 16, 18. From the games' PEGI / ESRB ratings in Playnite"),
                                       Command = new RelayCommand(_ => { int[] steps = { 0, 3, 7, 12, 16, 18 }; int i = Array.IndexOf(steps, p.MaxAge); p.MaxAge = steps[(i + 1) % steps.Length]; changed(); BoardRowsAgain(again, "age"); }) });
                if (p.MaxAge > 0)
                    rows.Add(BoardSwitch("hide", "unrated", Loc("LOC_KX_HideUnrated", "Hide games with no age rating"), p.HideUnrated, Loc("LOC_KX_On", "On"), Loc("LOC_KX_HideUnratedOff", "Off: unrated games show"),
                                         () => { p.HideUnrated = !p.HideUnrated; changed(); BoardRowsAgain(again, "unrated"); }));
            }
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_HiddenGamesHeader", "HIDDEN GAMES") });
            rows.Add(new PickRow
            {
                Key = "hideone", Icon = "hide", Text = Loc("LOC_KX_HideAGame", "Hide a game from them…"), Sub = p.HiddenGames.Count == 0 ? Loc("LOC_KX_NoneHidden", "None hidden") : string.Format(Loc("LOC_KX_NHidden", "{0} hidden"), p.HiddenGames.Count),
                Command = new RelayCommand(_ => OpenSearchToPick(id =>
                {
                    string s = id.ToString();
                    if (!p.HiddenGames.Contains(s)) p.HiddenGames.Add(s);
                    changed();
                    ui.BeginInvoke(new Action(() => { if (openPanel != "options") TogglePanel("options"); SetOptionsPage("profileedit"); }), DispatcherPriority.Loaded);
                }))
            });
            foreach (string hid in p.HiddenGames.ToList())
            {
                Guid gid;
                Game g = null;
                if (Guid.TryParse(hid, out gid)) try { g = api.Database.Games.Get(gid); } catch (Exception) { }
                string h = hid;
                rows.Add(new PickRow { Key = "hid:" + hid, Icon = "remove", Text = g != null ? g.Name : "?", Sub = Loc("LOC_KX_ShowAgain", "Show it again"),
                                       Command = new RelayCommand(_ => { p.HiddenGames.Remove(h); changed(); BoardRowsAgain(again, "hideone"); }) });
            }
            if (!owner)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecDelete", "DELETE") });
                rows.Add(new PickRow
                {
                    Key = "delete", Icon = "trash", Text = Loc("LOC_KX_DeleteProfile", "Delete this profile"), Sub = Loc("LOC_KX_DeleteProfileSub", "Its layout and data are kept in the profiles folder (Deleted), in case"),
                    Command = new RelayCommand(_ =>
                    {
                        if (!DeleteProfileKeepData(p)) return;
                        PlaySound("back");
                        if (profileEditFromTour) { profileEditFromTour = false; ClosePanels(); return; }   // (back to Getting started)
                        GoBackTo("profiles", "add");
                    })
                });
            }
        }

        /// <summary>A profile goes (never the first one, yours): off it first (that saves its snapshot), then its snapshot is
        /// kept in the profiles folder's Deleted folder, in case. False if it couldn't be switched off.</summary>
        private bool DeleteProfileKeepData(PlayerProfile p)
        {
            if (p == null || settings.Profiles.Count == 0 || ReferenceEquals(p, settings.Profiles[0]) || p.Id == GuestId) return false;
            if (p.Id == settings.CurrentProfile) SwitchProfile(OwnerProfile);
            if (p.Id == settings.CurrentProfile) return false;   // couldn't switch off it: it stays
            try
            {
                string file = Path.Combine(ProfilesDir, p.Id + ".json");
                string safeName = string.Join("_", (p.Name ?? "").Split(Path.GetInvalidFileNameChars()));
                if (File.Exists(file)) { Directory.CreateDirectory(Path.Combine(ProfilesDir, "Deleted")); File.Copy(file, Path.Combine(ProfilesDir, "Deleted", safeName + "-" + p.Id + ".json"), true); File.Delete(file); }
            }
            catch (Exception) { }
            settings.Profiles.Remove(p);
            SaveSettings();
            logger.Info("Kippix: profile deleted: " + p.Name);
            return true;
        }

        private void BuildProfileSteamRows()
        {
            var rows = ProfileOptionRows;
            var p = EditingProfile;
            if (p == null) return;
            var accounts = SteamAccounts();
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "steaminfo", Text = Loc("LOC_KX_SteamAccountsInfo", "The accounts Steam remembers on this PC. Not listed? Sign into it once in Steam with Remember me ticked.") });
            rows.Add(new PickRow { Key = "steam:", Icon = "remove", Text = Loc("LOC_KX_SteamNoneRow", "No Steam account"), Sub = Loc("LOC_KX_SteamNoneRowSub", "Not linked to a Steam account"), IsSelected = string.IsNullOrEmpty(p.SteamAccount),
                                   Command = new RelayCommand(_ => { p.SteamAccount = p.SteamId64 = p.SteamPersona = null; SaveSettings(); PlaySound("select"); if (p.Id == settings.CurrentProfile) { LoadProfileExtras(p); ApplyListFilter(true); } GoBackTo("profileedit", "steam"); }) });
            foreach (var a in accounts)
            {
                var acc = a;
                var used = settings.Profiles.FirstOrDefault(x => x != p && x.SteamId64 == a.SteamId64);
                rows.Add(new PickRow
                {
                    Key = "steam:" + a.SteamId64, Icon = "controller", Text = string.IsNullOrEmpty(a.Persona) ? a.Account : a.Persona, IsSelected = p.SteamId64 == a.SteamId64,
                    Sub = a.Account + (a.Remembered ? "" : Loc("LOC_KX_NotRemembered", "  ·  sign in once with Remember me to switch to it")) + (used != null ? "  ·  " + string.Format(Loc("LOC_KX_UsedBy", "also {0}'s"), used.Name) : ""),
                    Command = new RelayCommand(_ =>
                    {
                        p.SteamAccount = acc.Account;
                        p.SteamId64 = acc.SteamId64;
                        p.SteamPersona = acc.Persona;
                        SaveSettings();
                        PlaySound("select");
                        if (p.Id == settings.CurrentProfile) { LoadProfileExtras(p); ApplyListFilter(true); }
                        GoBackTo("profileedit", "steam");
                    })
                });
            }
            if (accounts.Count == 0) rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "nosteam", Text = Loc("LOC_KX_SteamNoAccounts", "Steam doesn't remember any accounts on this PC yet") });
        }

        private void ProfilesOptionsOpened(string page)
        {
            Notify("IsOptionsProfiles");
            if (page == "profiles" || page == "profileedit" || page == "profilesteam" || page == "profiletime") BuildProfileOptionRows();
            if (page == "") Notify("ProfilesSummary");
        }

        // ---------------- own play sessions (who played) ----------------

        private List<PlaySession> OwnSessions(int days)
        {
            try
            {
                lock (sessionLock)
                {
                    if (!File.Exists(SessionsFile)) return new List<PlaySession>();
                    var list = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<PlaySession>>(File.ReadAllText(SessionsFile)) ?? new List<PlaySession>();
                    foreach (var s in list) if (s != null && s.Start.Kind == DateTimeKind.Utc) s.Start = s.Start.ToLocalTime();
                    var cutoff = DateTime.Now.AddDays(-days);
                    return list.Where(s => s != null && s.Start >= cutoff).ToList();
                }
            }
            catch (Exception) { return new List<PlaySession>(); }
        }
    }
}
