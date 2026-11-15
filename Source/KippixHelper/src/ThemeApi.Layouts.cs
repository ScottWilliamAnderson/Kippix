using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>A saved layout (Documents › Kippix Layouts › name.json).</summary>
    public class LayoutProfile
    {
        public string Name { get; set; }
        public string SavedText { get; set; }
        public ICommand Command { get; set; }
        internal string File;
        /// <summary>Added with Upload layout (kept in the Uploaded folder), not saved here.</summary>
        public bool IsUploaded { get; set; }
        internal string Id { get { return (IsUploaded ? "uploaded:" : "saved:") + Name; } }
    }

    /// <summary>
    /// Layout profiles (save / load / share) and resets (home, game page, all layouts, everything) with undo.
    /// Settings are grouped by field name; personal data (wishlist, keys, history) is never reset or put in a profile.
    /// </summary>
    public partial class ThemeApi
    {
        // ---------------- Community layouts (ThemeApi.Community.cs) ----------------

        private System.Windows.Input.ICommand communityCmd;
        /// <summary>Layouts & reset › COMMUNITY note: Settings › Community on its LAYOUTS section.</summary>
        public System.Windows.Input.ICommand CommunityLayoutsCommand
        {
            get { return communityCmd ?? (communityCmd = new RelayCommand(_ => OpenCommunityAt(null, "cl:browse", false))); }   // (Settings › Community, on LAYOUTS)
        }
        public string CommunityLayoutsText
        {
            get { return Loc("LOC_KX_CommunityNoteLayouts", "Layouts other people have made, and sharing yours"); }
        }

        public ObservableCollection<LayoutProfile> LayoutProfiles { get; private set; }
        public ICommand SaveLayoutCommand { get; private set; }
        public ICommand ResetLayoutCommand { get; private set; }
        public ICommand UndoResetCommand { get; private set; }
        public ICommand LayoutLoadCommand { get; private set; }
        public ICommand LayoutOverwriteCommand { get; private set; }
        public ICommand LayoutDeleteCommand { get; private set; }
        public ICommand ResetMasterElementCommand { get; private set; }
        private LayoutProfile editingLayout;

        // never reset, never in a profile: your data, keys, history and where you live
        private static readonly HashSet<string> DataFields = new HashSet<string>
        {
            "SettingsVersion", "LastBackground", "SteamApiKey", "GitHubToken", "GitHubLogin", "CommunitySent", "SgdbApiKey", "ItadApiKey", "StoreWishlist", "WishReleaseNotices", "GameUpdateSeen", "StoreRecent", "GameInfoPosition", "DetFlip", "AchListLayout",
            "WeatherTown", "WeatherLat", "WeatherLon", "WeatherFahrenheit", "RecordSessions", "EditsMerged", "MasterHome", "MasterGame",
            "CurrentLayout", "GalleryFolders", "GalleryFavourites", "ArtSwaps", "PinnedGames", "ProfileNameOverride", "ProfilePicturePath", "RecentSearches", "WishImportAt", "WishDismissed", "Collections",
            "GameReset094", "LookReset096", "Profiles", "CurrentProfile", "ProfilesEnabled", "AskWhoAtStart", "SyncLayouts", "ProfilesPin", "WelcomeDone", "WelcomeOnBoot", "WelcomeSkippable", "UpsMet", "BcBgModeWas", "BcOffOnce", "CommunityRepo", "CommunityBranch", "SlotPins",
            "LayoutHasViews", "CompletionBeaten", "Completion100", "CompletionDropped",
            "TipsSeen", "SafeMode", "SafeModeSaved", "HandheldSaved",   // (r12 G: state, not a layout)
            "Folders",  // your folders are your own things, like collections: a reset or a layout never takes them away
            "UpNext",   // the Up next queue (each profile's own)
            "LocalMediaFolders", "LocalMediaKinds", "LocalMediaSort"   // (1.0.1) Shows & movies: your folders
        };

        // what "layout" means, screen by screen (placement, size and on / off of what's shown)
        private static readonly HashSet<string> HomeLayoutFields = new HashSet<string>
        {
            "ShowButtonHints", "HintsPosition", "ShowLogo", "LogoAnchor", "LogoPosition", "ShowGameInfo", "InfoPosition", "HintsSize", "LogoSize", "InfoSize",
            "CardOffset", "CardSize", "RowDesign", "RowLoop", "RowFullCount", "RowTallCount", "CarTallW", "CarTallH", "CarTallFill", "CarFullH", "RowShowInfo", "FolderGameDesign", "FolderDuplicates", "FolderInstalled", "FolderSort", "FolderTileCount", "CardShape", "ShowFilterLabel", "FilterPosition",
            "InfoPlaytime", "InfoLastPlayed", "InfoAchievements", "InfoPlatform", "InfoSource", "InfoCompletion", "InfoReleaseYear", "InfoPlayers", "InfoFriends", "HomeHltb", "InfoOrder",
            "HomeTilesMode", "HomeTileStore", "HomeTileAch", "HomeTileRandom", "HomeTileUpNext", "HomeTileLibrary",
            "HomeLayoutMode", "GridRows", "GridColumns", "JustRows", "ListRows", "ListTileH", "ListTileW", "ListInfoShare", "ListInfoItems", "ListShowInfo", "ListShowName", "HomeBgMode", "HomeBgColor", "HomeBgImage", "BgPerLayout", "BgLayoutKey", "HomePlacePerLayout", "HomeTileBgScale", "HomeBgOffsetX", "HomeBgOffsetY", "HomeFadeDir", "HomeFadeReach", "ListSide", "ExtraRows", "ExtraRowKeys", "BadgeInstalled", "BadgeNew", "BadgeAch", "BadgeHltb", "BadgeFeatures", "BadgeFeatHide", "BadgeStore",
            "AccentFromGame", "AccentStrength", "BackgroundMotion", "MotionSpeed", "MotionAmount", "DepthMode", "LiveSlideWeb",
            "Look", "LookPack",   // the look (Look packs / Theme Studio): in saved, community and profile layouts
            "TriArrange", "TriSpacing", "TriAlternate", "BoardSeeded", "ManualCards", "ManualGroups", "ManualPlacement", "FreeCards", "ManualPages", "FreePages", "CardSpacing", "ManualCols", "ManualRows", "ManualSnap", "BoardPresets", "BoardCards", "BoardCols", "BoardRows", "BoardSnap", "BoardPageAxis", "BoardSlideOne", "BoardIndicator", "BoardLiveSeconds", "Presets", "PresetBase", "PresetOnScreen"
        };
        private static readonly HashSet<string> GameLayoutFields = new HashSet<string>
        {
            "DetShowLogo", "DetShowInfo", "DetShowDescription", "DetDescPosition", "DetLogoPosition", "DetOffset", "DetShowAchButton", "DetShowDate",
            "DetArt", "DetMediaSize", "DetMediaOffset", "DetLayout", "DetShowOptionsButton", "DetShowTrailerControls",
            "DetMoreSections", "DetSecHistory", "DetSecHltb", "DetSecCaptures", "DetSecAch", "DetSecStatus", "DetSecNotes", "DetSecScores", "DetSecSimilar",
            "DetShowMoreButton", "DetShowDlcWarning", "DetLower", "DetNoInteract", "DetDescSource", "DetCardLooks", "DetCardColors", "DetInfoOff",
            "DetSecSaves", "DetShowFriends", "DetBlackBackground", "DetInfoOrder", "DetShowHints", "DetSimilarShape", "DetSecNews", "DetSecMods", "DetNewsButton",
            "GamePreset", "ShowNexusLink"   // (r15) the game page's Nexus Mods button (its switch moved from Add-ons to Game page › Buttons and extras)
        };
        private static readonly HashSet<string> TopBarFields = new HashSet<string>
        {
            "PinSearch", "PinAchievements", "PinDiscord", "PinAudio", "PinHibernate", "PinDownloads", "PinStore", "PinBattery", "PinSleep", "PinRestart",
            "PinShutdown", "PinGallery", "ShortcutOrder", "PinClock", "ShortcutsVertical", "ProfileShow", "SettingsInProfileMenu", "HideTopOptions", "ExtraPins", "AutoHideTopBar", "TopBarKeepClock", "TopBarStayVisible", "ShowNotificationIcon", "Clock24h", "ShowBatteryPercent"
        };

        private static string FieldGroup(string f)
        {
            if (DataFields.Contains(f)) return "data";
            if (HomeLayoutFields.Contains(f)) return "home";
            if (GameLayoutFields.Contains(f)) return "game";
            if (TopBarFields.Contains(f)) return "topbar";
            if (f == "MasterOffsets" || f == "MasterSizes" || f == "MasterRotations" || f == "MasterLayers" || f == "MasterOpacity" || f == "MasterSnap" || f == "MasterGrid") return "master";
            if (f == "AchSolidBackground" || f == "AchBgMode" || f == "AchLayout" || f == "AchRedesign" || f == "AchItemRedesign" || f == "AchItemLayout" || f == "AchTallyMode" || f == "AchNextUp" || ViewFields.Contains(f)) return "ach";
            if (f == "Widgets" || f == "WidgetsOn") return "widgets";
            return "other";   // behaviour and the rest (sounds, music, store, trailers, previews...): only "everything" resets them
        }

        // how the achievements and gallery pages are sorted, filtered and which tab they open on: kept with the achievements view (0.71).
        // A layout file from before 0.71 has them blank (LayoutHasViews false), so loading one leaves yours as they are.
        private static readonly HashSet<string> ViewFields = new HashSet<string>
        {
            "AchSort", "AchFilter", "AchItemFilter", "GalleryView", "GallerySort", "GalleryShow"
        };

        /// <summary>In a saved layout: the screens' layouts and your widgets. Reset layout leaves the widgets alone (that's what they show, not a layout).</summary>
        private static bool InLayout(string group) { return group == "home" || group == "game" || group == "topbar" || group == "master" || group == "ach"; }
        private static bool InProfile(string group) { return InLayout(group) || group == "widgets"; }

        private void LayoutSound(string kind) { if (IsFullscreen) PlaySound(kind); }

        private void InitLayouts()
        {
            LayoutProfiles = new ObservableCollection<LayoutProfile>();
            LayoutChoices = new ObservableCollection<PickRow>();
            UploadLayoutCommand = new RelayCommand(_ => UploadLayout());
            LayoutShareCommand = new RelayCommand(_ => ShareLayout());
            SaveLayoutCommand = new RelayCommand(_ => SaveLayoutAs());
            ResetLayoutCommand = new RelayCommand(p => AskReset(p as string));
            UndoResetCommand = new RelayCommand(_ => UndoReset());
            LayoutLoadCommand = new RelayCommand(_ => { if (editingLayout != null) LoadLayout(editingLayout); });
            LayoutOverwriteCommand = new RelayCommand(_ => { if (editingLayout != null) WriteLayout(editingLayout.Name); });
            LayoutDeleteCommand = new RelayCommand(_ => DeleteLayout());
            ResetMasterElementCommand = new RelayCommand(p => ResetMasterElement(p as string));
        }

        public bool IsOptionsLayouts { get { return optionsPage == "layouts"; } }
        public bool IsOptionsLayoutEdit { get { return optionsPage == "layoutedit"; } }
        public string EditLayoutName { get { return editingLayout != null ? editingLayout.Name : ""; } }
        public string LayoutsFolderText { get { return string.Format(Loc("LOC_KX_LayoutsFolder2", "Layouts are kept in {0}. To share one, send its .json file (or use Save a copy on it); Upload layout adds one you were sent."), LayoutsFolder); } }
        public ICommand UploadLayoutCommand { get; private set; }
        public ICommand LayoutShareCommand { get; private set; }
        public bool IsOptionsLayoutPick { get { return optionsPage == "layoutpick"; } }
        /// <summary>Layouts › Layout: Kippix default, then your saved layouts, then uploaded ones.</summary>
        public ObservableCollection<PickRow> LayoutChoices { get; private set; }
        public string CurrentLayoutText
        {
            get
            {
                string c = settings.CurrentLayout ?? "";
                if (c.StartsWith("saved:", StringComparison.Ordinal)) return c.Substring(6);
                if (c.StartsWith("uploaded:", StringComparison.Ordinal)) return c.Substring(9);
                return "Kippix default";
            }
        }
        public bool HasUndoReset { get { return File.Exists(UndoFile); } }
        public string LayoutStatus { get; private set; }
        public bool HasLayoutStatus { get { return !string.IsNullOrEmpty(LayoutStatus); } }

        private void SetLayoutStatus(string text)
        {
            LayoutStatus = text;
            Notify("LayoutStatus");
            Notify("HasLayoutStatus");
            Notify("HasUndoReset");
        }

        private static string LayoutsFolder
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kippix Layouts");
                if (!layoutsMigrated) { layoutsMigrated = true; MigrateLayoutsFolder(dir); }
                return dir;
            }
        }

        private static bool layoutsMigrated;

        /// <summary>Run at start-up so the old layouts folder is copied across before anything reads it.</summary>
        internal static void EnsureLayoutsFolder() { LayoutsFolder.ToString(); }

        /// <summary>Renames (0.85 Kippix; 0.76 KipScreen): the layouts folder from before — Documents › KipScreen Layouts, else
        /// Documents › BananaBreadFS Layouts — is copied (not moved: the old one stays as a backup) to Documents › Kippix Layouts
        /// once, when the new folder doesn't exist yet. Copied into a temporary folder first and renamed at the end, so a copy that
        /// stops half way is tried again next time.</summary>
        internal static void MigrateLayoutsFolder(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) return;
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string old = new[] { Path.Combine(docs, "Kip" + "Screen Layouts"), Path.Combine(docs, "BananaBreadFS Layouts") }.FirstOrDefault(Directory.Exists);
                if (old == null) return;
                string temp = dir + " (copying)";
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                CopyFolder(old, temp);
                Directory.Move(temp, dir);
                logger.Info("Kippix: layouts copied from " + old + " to " + dir + " (the old folder is kept)");
            }
            catch (Exception ex) { logger.Warn("Kippix: couldn't copy the old layouts folder: " + ex.Message); }
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from)) CopyFolder(d, Path.Combine(to, Path.GetFileName(d)));
        }

        private string UndoFile { get { return Path.Combine(plugin.GetPluginUserDataPath(), "settings.before-reset.json"); } }
        private static string UploadedFolder { get { return Path.Combine(LayoutsFolder, "Uploaded"); } }

        // ---------------- profiles ----------------

        private void RefreshLayoutProfiles()
        {
            LayoutProfiles.Clear();
            try
            {
                foreach (var pair in new[] { Tuple.Create(LayoutsFolder, false), Tuple.Create(UploadedFolder, true) })
                {
                    if (!Directory.Exists(pair.Item1)) continue;
                    foreach (var f in new DirectoryInfo(pair.Item1).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime))
                    {
                        bool up = pair.Item2;
                        var p = new LayoutProfile
                        {
                            Name = Path.GetFileNameWithoutExtension(f.Name),
                            SavedText = string.Format(up ? Loc("LOC_KX_LayoutUploaded", "Uploaded {0}") : Loc("LOC_KX_LayoutSaved", "Saved {0}"),
                                                      f.LastWriteTime.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)),
                            File = f.FullName,
                            IsUploaded = up
                        };
                        var prof = p;
                        p.Command = new RelayCommand(_ => { editingLayout = prof; SetOptionsPage("layoutedit"); Notify("EditLayoutName"); });
                        LayoutProfiles.Add(p);
                    }
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: layouts: " + ex.Message); }
            BuildLayoutChoices();
            Notify("CurrentLayoutText");
        }

        /// <summary>The Layout dropdown: Kippix default first, then saved, then uploaded (a tick on the one in use).</summary>
        private void BuildLayoutChoices()
        {
            LayoutChoices.Clear();
            string cur = settings.CurrentLayout ?? "";
            LayoutChoices.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecBuiltIn", "BUILT IN") });
            LayoutChoices.Add(new PickRow
            {
                Text = "Kippix default", Sub = Loc("LOC_KX_LayoutDefaultSub", "The theme's own layout"), Icon = "layout", IsSelected = cur == "",
                Command = new RelayCommand(_ => UseBananaTheme())
            });
            var saved = LayoutProfiles.Where(p => !p.IsUploaded).ToList();
            var uploaded = LayoutProfiles.Where(p => p.IsUploaded).ToList();
            if (saved.Count > 0) LayoutChoices.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LayoutsSaved", "SAVED") });
            foreach (var p in saved) LayoutChoices.Add(ChoiceFor(p, cur));
            if (uploaded.Count > 0) LayoutChoices.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LayoutsUploaded", "UPLOADED") });
            foreach (var p in uploaded) LayoutChoices.Add(ChoiceFor(p, cur));
        }

        private PickRow ChoiceFor(LayoutProfile p, string cur)
        {
            var prof = p;
            return new PickRow { Text = p.Name, Sub = p.SavedText, Icon = p.IsUploaded ? "upload" : "layout", IsSelected = cur == p.Id, Command = new RelayCommand(_ => { LoadLayout(prof); }) };
        }

        /// <summary>The theme's own layout: every screen, the top bar and the widgets as they come (your data stays). Undo puts back what you had.</summary>
        private void UseBananaTheme()
        {
            BackupForUndo();
            var blank = new HelperSettings();
            foreach (var f in typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (InProfile(FieldGroup(f.Name))) f.SetValue(settings, f.GetValue(blank));
            settings.MasterOffsets = new Dictionary<string, double[]>();
            settings.MasterSizes = new Dictionary<string, double[]>();
            settings.MasterRotations = new Dictionary<string, double>();
            settings.Widgets = new List<WidgetConfig>();
            settings.CurrentLayout = "";
            NormalizeSettings();
            ApplyAllSettings();
            LayoutSound("select");
            SetLayoutStatus(Loc("LOC_KX_LayoutDefaultDone", "Using the Kippix default layout. Undo last reset puts back what you had."));
            RefreshLayoutProfiles();
            if (optionsPage == "layoutpick") GoBackTo("layouts", "layoutpick");
        }

        /// <summary>Upload layout: pick a layout file someone sent you; it's copied into the Uploaded folder and shows in the Layout list.</summary>
        private void UploadLayout()
        {
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            OpenPicker(Loc("LOC_KX_UploadLayout", "Upload layout"), false, new[] { ".json" }, Directory.Exists(start) ? start : "", "layouts", file =>
            {
                try
                {
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var test = js.Deserialize<HelperSettings>(File.ReadAllText(file));
                    if (test == null) throw new InvalidDataException();
                    Directory.CreateDirectory(UploadedFolder);
                    string name = Path.GetFileNameWithoutExtension(file);
                    string target = Path.Combine(UploadedFolder, name + ".json");
                    for (int i = 2; File.Exists(target) && !string.Equals(Path.GetFullPath(target), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase); i++)
                        target = Path.Combine(UploadedFolder, name + " (" + i + ").json");
                    if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) File.Copy(file, target);
                    LayoutSound("select");
                    RefreshLayoutProfiles();
                    SetLayoutStatus(string.Format(Loc("LOC_KX_LayoutUploadedDone", "Added \"{0}\". Choose it under Layout to use it."), Path.GetFileNameWithoutExtension(target)));
                }
                catch (Exception ex)
                {
                    SetLayoutStatus(Loc("LOC_KX_LayoutBad", "That layout file couldn't be read."));
                    logger.Warn("Kippix: upload layout: " + ex.Message);
                }
                GoBackTo("layouts", "upload");
            });
        }

        /// <summary>Save a copy of a layout somewhere (a USB stick, Downloads...) to send to someone.</summary>
        private void ShareLayout()
        {
            var p = editingLayout;
            if (p == null) return;
            OpenPickerFolder(Loc("LOC_KX_LayoutShareWhere", "Save a copy to"), "layoutedit", dir =>
            {
                try
                {
                    string target = Path.Combine(dir, p.Name + ".json");
                    for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, p.Name + " (" + i + ").json");
                    File.Copy(p.File, target);
                    LayoutSound("select");
                    SetLayoutStatus(string.Format(Loc("LOC_KX_LayoutShared", "Saved a copy as {0}"), target));
                }
                catch (Exception ex) { SetLayoutStatus(Loc("LOC_KX_LayoutShareFailed", "Couldn't save a copy there.")); logger.Warn("Kippix: share layout: " + ex.Message); }
                GoBackTo("layouts", "layoutedit");
            });
        }

        private void SaveLayoutAs()
        {
            OpenKeyboard(Loc("LOC_KX_LayoutName", "Name for this layout"), "", typed =>
            {
                string name = string.Join("_", (typed ?? "").Trim().Split(Path.GetInvalidFileNameChars()));
                if (name.Length == 0) return;
                WriteLayout(name);
            }, null, null, 60);
        }

        /// <summary>Only the layout parts of the settings go in the file (no wishlist, keys or history), so it can be shared.</summary>
        private void WriteLayout(string name)
        {
            try
            {
                SaveSettings();
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var copy = js.Deserialize<HelperSettings>(js.Serialize(settings));
                var blank = new HelperSettings();
                foreach (var f in typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!InProfile(FieldGroup(f.Name))) f.SetValue(copy, f.GetValue(blank));
                copy.EditsMerged = true;   // marks a 0.27+ layout (older ones are converted when loaded)
            copy.LayoutHasViews = true;   // has the gallery's and achievements' sort / filter / tab (0.71+)
                EmbedTileImages(copy);   // picture tiles: the pictures go inside the file (so a copy of it works on another PC)
                Directory.CreateDirectory(LayoutsFolder);
                // Overwrite on an uploaded layout writes over that one (not a new copy in the saved folder)
                bool overUploaded = optionsPage == "layoutedit" && editingLayout != null && editingLayout.IsUploaded && editingLayout.Name == name;
                File.WriteAllText(overUploaded ? editingLayout.File : Path.Combine(LayoutsFolder, name + ".json"), js.Serialize(copy));
                settings.CurrentLayout = (overUploaded ? "uploaded:" : "saved:") + name;
                SaveSettings();
                LayoutSound("select");
                RefreshLayoutProfiles();
                SetLayoutStatus(string.Format(Loc("LOC_KX_LayoutSavedAs", "Saved as \"{0}\"."), name));
                if (optionsPage == "layoutedit") GoBackTo("layouts", "layoutedit");
            }
            catch (Exception ex) { SetLayoutStatus(Loc("LOC_KX_LayoutFailed", "Couldn't save the layout.")); logger.Warn("Kippix: save layout: " + ex.Message); }
        }

        /// <summary>Puts a layout file's layout in place (with Undo reset ready). False if the file can't be read.</summary>
        private bool ApplyLayoutFile(string file, string id)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var loaded = js.Deserialize<HelperSettings>(File.ReadAllText(file));
            if (loaded == null) return false;
            if (!loaded.EditsMerged) MergeEdits(loaded);   // a layout saved before Quick and Master Edits were joined
            BackupForUndo();
            bool trailerWas = settings.HomeBgMode == 2;
            foreach (var f in typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (InProfile(FieldGroup(f.Name)) && (loaded.LayoutHasViews || !ViewFields.Contains(f.Name))
                    && !((f.Name == "Look" || f.Name == "LookPack") && loaded.Look == null)) f.SetValue(settings, f.GetValue(loaded));   // (a layout from before look packs keeps your look)
            // a layout with the game's art (all from before 0.41) keeps the trailer you had playing over it
            if (trailerWas && settings.HomeBgMode == 0) settings.HomeBgMode = 2;
            RestoreTileImages();   // picture tiles carried in the file: written back to the tiles folder
            NormalizeSettings();   // a shared file may have values this version doesn't take
            settings.CurrentLayout = id;
            ApplyAllSettings();
            return true;
        }

        private void LoadLayout(LayoutProfile p)
        {
            try
            {
                if (!ApplyLayoutFile(p.File, p.Id)) return;
                LayoutSound("select");
                SetLayoutStatus(string.Format(Loc("LOC_KX_LayoutLoaded", "Using \"{0}\". Undo last reset puts back what you had."), p.Name));
                RefreshLayoutProfiles();
                GoBackTo("layouts", optionsPage == "layoutpick" ? "layoutpick" : "layoutedit");
            }
            catch (Exception ex) { SetLayoutStatus(Loc("LOC_KX_LayoutBad", "That layout file couldn't be read.")); logger.Warn("Kippix: load layout: " + ex.Message); }
        }

        private void DeleteLayout()
        {
            if (editingLayout == null) return;
            try { File.Delete(editingLayout.File); } catch (Exception) { }
            if (settings.CurrentLayout == editingLayout.Id) { settings.CurrentLayout = ""; SaveSettings(); }
            LayoutSound("back");
            editingLayout = null;
            RefreshLayoutProfiles();
            GoBackTo("layouts", "layoutedit");
        }

        // ---------------- resets (with undo) ----------------

        /// <summary>scope: home, game, layout (both + top bar + Master Edit + achievements screen + the gallery's tab / sort / show), everything (all but your data).</summary>
        /// <summary>The reset buttons ask first (what's reset, and that Undo reset can bring it back).</summary>
        private void AskReset(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return;
            string what = ResetScopeName(scope);
            if (scope == "everything")
                Confirm(Loc("LOC_KX_ResetEverythingAsk", "Reset everything?"),
                        Loc("LOC_KX_ResetEverythingAskText", "This puts every layout and option back to how Kippix comes. You can undo it straight after with Undo reset."),
                        Loc("LOC_KX_ResetEverythingYes", "Reset everything"), () => ResetLayout(scope));
            else
                Confirm(string.Format(Loc("LOC_KX_ResetSectionAsk", "Reset {0}?"), what),
                        string.Format(Loc("LOC_KX_ResetSectionAskText", "This puts {0} back to how Kippix comes. Everything else stays as it is, and Undo reset can bring it back."), what),
                        Loc("LOC_KX_Reset", "Reset"), () => ResetLayout(scope));
        }

        private string ResetScopeName(string scope)
        {
            switch (scope)
            {
                case "home": return Loc("LOC_KX_ResetNameHome", "the home screen");
                case "game": return Loc("LOC_KX_ResetNameGame", "the game page");
                case "layout": return Loc("LOC_KX_ResetNameLayout", "the layout");
                case "topbar": return Loc("LOC_KX_ResetNameTopBar", "the top bar");
                case "widgets": return Loc("LOC_KX_ResetNameWidgets", "the widgets");
                default: return scope;
            }
        }

        public void ResetLayout(string scope)
        {
            if (string.IsNullOrEmpty(scope)) return;
            BackupForUndo();
            var blank = new HelperSettings();
            foreach (var f in typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string g = FieldGroup(f.Name);
                if (g == "data" || f.Name == "MasterOffsets" || f.Name == "MasterSizes" || f.Name == "MasterRotations" || f.Name == "MasterLayers" || f.Name == "MasterOpacity") continue;
                bool hit = scope == "everything" || (scope == "layout" && InLayout(g)) || (scope != "widgets" && scope == g);
                if (hit) f.SetValue(settings, f.GetValue(blank));
            }
            if (settings.MasterOffsets != null)
                foreach (var k in settings.MasterOffsets.Keys.ToList())
                    if (scope == "everything" || scope == "layout" || k.StartsWith(scope + ".", StringComparison.Ordinal)
                        || (scope == "topbar" && (k == "home.topbar" || k == "home.profile" || k == "home.shortcuts"))) settings.MasterOffsets.Remove(k);
            if (settings.MasterSizes != null)
                foreach (var k in settings.MasterSizes.Keys.ToList())
                    if (scope == "everything" || scope == "layout" || k.StartsWith(scope + ".", StringComparison.Ordinal)
                        || (scope == "topbar" && (k == "home.topbar" || k == "home.profile" || k == "home.shortcuts"))) settings.MasterSizes.Remove(k);   // (as the offsets above)
            if (settings.MasterRotations != null)
                foreach (var k in settings.MasterRotations.Keys.ToList())
                    if (scope == "everything" || scope == "layout" || k.StartsWith(scope + ".", StringComparison.Ordinal)
                        || (scope == "topbar" && (k == "home.topbar" || k == "home.profile" || k == "home.shortcuts"))) settings.MasterRotations.Remove(k);   // (and the turns)
            ResetMasterExtras(scope);   // (and the layers and opacity, the same way)
            // (0.99.61) the game page as it comes isn't "nothing moved": its parts' own places and kept rooms go back;
            // (0.99.62) and the home screen's, as it comes (the layout it starts on; the others' are in HomePlacePerLayout)
            {
                bool all = scope == "everything" || scope == "layout";
                Func<string, bool> back = k => all || k.StartsWith(scope + ".", StringComparison.Ordinal)
                                               || (scope == "topbar" && (k == "home.topbar" || k == "home.profile" || k == "home.shortcuts"));
                if (settings.MasterOffsets == null) settings.MasterOffsets = new Dictionary<string, double[]>();
                if (settings.MasterSizes == null) settings.MasterSizes = new Dictionary<string, double[]>();
                if (settings.MasterRotations == null) settings.MasterRotations = new Dictionary<string, double>();
                if (settings.MasterLayers == null) settings.MasterLayers = new Dictionary<string, int>();
                if (settings.MasterOpacity == null) settings.MasterOpacity = new Dictionary<string, double>();
                if (blank.MasterOffsets != null) foreach (var kv in blank.MasterOffsets) if (back(kv.Key)) settings.MasterOffsets[kv.Key] = kv.Value;
                if (blank.MasterSizes != null) foreach (var kv in blank.MasterSizes) if (back(kv.Key)) settings.MasterSizes[kv.Key] = kv.Value;
                if (blank.MasterRotations != null) foreach (var kv in blank.MasterRotations) if (back(kv.Key)) settings.MasterRotations[kv.Key] = kv.Value;
                if (blank.MasterLayers != null) foreach (var kv in blank.MasterLayers) if (back(kv.Key)) settings.MasterLayers[kv.Key] = kv.Value;
                if (blank.MasterOpacity != null) foreach (var kv in blank.MasterOpacity) if (back(kv.Key)) settings.MasterOpacity[kv.Key] = kv.Value;
            }
            if (scope == "everything" || scope == "layout") { settings.MasterSnap = blank.MasterSnap; settings.MasterGrid = blank.MasterGrid; }
            // the home screen as it comes has no widgets: they go too (Undo last reset brings them back)
            if (scope == "home" || scope == "everything" || scope == "layout" || scope == "widgets") settings.Widgets = new List<WidgetConfig>();
            if (scope == "everything" || scope == "layout") settings.CurrentLayout = "";
            if (scope == "game") ResetGameTrailerOptions(blank);   // "Reset this whole page": the trailer's options are on the game page's settings too
            NormalizeSettings();
            ApplyAllSettings();
            LayoutSound("select");
            SetLayoutStatus(Loc("LOC_KX_ResetDone", "Done. Undo last reset puts it back."));
            RefreshLayoutProfiles();
        }

        private void BackupForUndo()
        {
            try
            {
                SaveSettings();
                FlushSettings(true);   // (0.93: written now, so the copy is of what's set)
                lock (saveLock) { }
                Directory.CreateDirectory(Path.GetDirectoryName(UndoFile));
                File.Copy(SettingsPath, UndoFile, true);
            }
            catch (Exception ex) { logger.Warn("Kippix: backup before reset: " + ex.Message); }
        }

        public void UndoReset()
        {
            try
            {
                if (!File.Exists(UndoFile)) return;
                var s = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<HelperSettings>(File.ReadAllText(UndoFile));
                if (s == null) return;
                if (!s.EditsMerged) MergeEdits(s);
                // your data stays as it is now (a wishlist change since the reset isn't undone)
                foreach (var f in typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (FieldGroup(f.Name) != "data") f.SetValue(settings, f.GetValue(s));
                settings.CurrentLayout = s.CurrentLayout ?? "";   // the layout you had (it's kept with your data otherwise)
                NormalizeSettings();
                File.Delete(UndoFile);
                RefreshLayoutProfiles();
                ApplyAllSettings();
                LayoutSound("select");
                SetLayoutStatus(Loc("LOC_KX_UndoDone", "Put back how it was."));
                FocusOptionsSoon();   // the Undo row has gone
            }
            catch (Exception ex) { logger.Warn("Kippix: undo reset: " + ex.Message); }
        }

        /// <summary>After many settings changed at once: rebuild what's built from them and refresh every binding.</summary>
        private void ApplyAllSettings() { ApplyAllSettings(null); }

        /// <summary>changed: (0.94) only these settings changed (a layout's preset coming out): the parts they don't touch aren't
        /// made again (the look's colours, the widgets, the shortcuts, the home tiles were most of a slow layout switch). Null: all.</summary>
        private void ApplyAllSettings(HashSet<string> changed)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var parts = new List<string>();
            long mark = 0;
            Action<string> took = n => { long t = sw.ElapsedMilliseconds; if (t - mark >= 15) parts.Add(n + " " + (t - mark)); mark = t; };
            Func<Func<string, bool>, bool> any = test => changed == null || changed.Any(test);
            bool gamePart = any(f => GameLayoutFields.Contains(f)), homePart = any(f => !GameLayoutFields.Contains(f));
            try
            {
                // the Board: "Undo quick setup" and the page you were on belong to the tiles that were there before
                EditHistoryClear();   // Free edit's undo steps belong to the layout that was there before
                if (homePart) { boardUndo = null; boardPageId = "home"; boardPage = 0; }
                if (Shortcuts != null && any(f => TopBarFields.Contains(f))) { BuildShortcuts(); UpdateShortcutStatus(); }
                if (InfoItems != null && any(f => f.StartsWith("Info", StringComparison.Ordinal))) BuildInfoItems();
                SaveSettings();
                if (changed == null) { try { LoadSoundPack(); UpdateMusicState(); } catch (Exception) { } }
                took("shortcuts");
                if (Widgets != null && IsFullscreen && any(f => f == "Widgets" || f == "WidgetsOn"))
                {
                    FixWidgetConfigs();
                    Widgets.Clear();
                    foreach (var c in settings.Widgets) Widgets.Add(MakeWidget(c));
                    if (Widgets.Count > 0 && widgetTimer != null && !widgetTimer.IsEnabled) widgetTimer.Start();
                    RefreshWidgets(true);
                }
                took("widgets");
                if (HomeTiles != null && IsFullscreen && any(f => f.StartsWith("HomeTile", StringComparison.Ordinal))) { BuildHomeTiles(); homeTilesShown = false; SetCardsLeft(0); ApplyHomeTiles(); }
                took("home tiles");
                if (changed == null) LookSettingsSwapped();   // the look (Look packs / Theme Studio) from the new settings
                else if (changed.Contains("Look") || changed.Contains("LookPack")) LookSettingsSwapped(false);   // (only the colours / fonts that differ)
                took("look");
                Notify("");   // every binding reads its value again
                took("bindings");
                RefreshMasterItems();
                if (ui != null)
                    ui.BeginInvoke(new Action(() =>
                    {
                        ApplyMasterOffsets();
                        if (gamePart) ApplyDetPlacement();   // parts sent to the lower page come back up (and the other way)
                        if (homePart)
                        {
                            ApplyBackgroundMotion();
                            ApplyHomeLayout();
                            if (IsFullscreen) HomeModeChanged(); else ApplyCardLayoutSoon();   // row / grid / list, the card art, the tiles: all of it again
                        }
                        UpdateTopBarFade();
                    }), DispatcherPriority.Loaded);
                took("rest");
                if (sw.ElapsedMilliseconds >= 60) PerfLog("settings applied in " + sw.ElapsedMilliseconds + " ms: " + string.Join(", ", parts) + (changed != null ? " (" + changed.Count + " changed)" : ""));
            }
            catch (Exception ex) { logger.Warn("Kippix: applying settings: " + ex.Message); }
        }

        private void FocusOptionsSoon()
        {
            if (ui != null && openPanel == "options") ui.BeginInvoke(new Action(() => UiTools.FocusInside("KX_OptionsPanel")), DispatcherPriority.ContextIdle);
        }

        public bool IsFullscreen
        {
            get { try { return api.ApplicationInfo.Mode == Playnite.SDK.ApplicationMode.Fullscreen; } catch (Exception) { return false; } }
        }

        // ---------------- Quick Edits: "moved in Master Edit" per element, with its own reset ----------------

        private bool Moved(string key)
        {
            var o = MasterOffset(key);
            var el = MasterElements.FirstOrDefault(m => m.Key == key);
            return el != null && (Math.Abs(o[0]) >= 0.5 || Math.Abs(o[1]) >= 0.5);
        }

        public bool MovedHomeCards { get { return Moved("home.cards"); } }
        public bool MovedHomeLogo { get { return Moved("home.logo"); } }
        public bool MovedHomeInfo { get { return Moved("home.info"); } }
        public bool MovedHomeHints { get { return Moved("home.hints"); } }
        public bool MovedHomeFilter { get { return Moved("home.filter"); } }
        public bool MovedGameLogo { get { return Moved("game.logo"); } }
        public bool MovedGameInfo { get { return Moved("game.stats") || Moved("game.buttons") || Moved("game.facts") || Moved("game.friends") || Moved("game.desc"); } }
        public bool MovedGameMedia { get { return Moved("game.media"); } }

        private void NotifyMoved()
        {
            foreach (string n in new[] { "MovedHomeCards", "MovedHomeLogo", "MovedHomeInfo", "MovedHomeHints", "MovedHomeFilter", "MovedGameLogo", "MovedGameInfo", "MovedGameMedia" }) Notify(n);
        }

        private void ResetMasterElement(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            PlaySound("select");
            // its place, its size (Free move › Y) and its turn (LT / RT) all go back to how they come
            var keys = key == "game.info" ? new[] { "game.stats", "game.buttons", "game.facts", "game.friends", "game.desc" } : new[] { key };   // the old single block is now five parts
            foreach (var k in keys)
            {
                SetMasterOffset(k, 0, 0);
                SetMasterRotation(k, 0);
                ClearMasterExtras(k);   // (its layer and opacity)
                if (settings.MasterSizes != null) settings.MasterSizes.Remove(k);
            }
            if (key == "home.cards") { settings.ListTileH = 0; settings.ListTileW = 0; HomeModeChanged(); UpdateSliders(); }
            SaveSettings();
            RefreshMasterItems();
            ApplyMasterOffsets();
            ApplyHomeLayoutSoon();
            NotifyMoved();
            FocusOptionsSoon();   // the row you pressed has gone
        }
    }
}
