using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (0.91) Presets: every layout (Row, Row › Tall, Row › Full Screen, Grid, List, Board, Manual board) and the game page has
    /// three ready-made designs besides your own. Settings › Game cards and layout › Preset (Game page › Layout › Preset for the
    /// game page) lists them: Default (your own, as it was) and the three designs (ThemeApi.PresetDesigns.cs). Picking one
    /// changes the screen straight away; the list stays open. (0.92) Changes you make to a design are kept with that design
    /// (PresetSlot.Saved): picking it again brings them back; "Put it back" gives Kippix's version again.
    ///
    /// A layout with a preset on keeps its own settings: going to another layout puts them away (PresetSlot.Cur) and brings that
    /// layout's out (its preset's, or your own: PresetBase). Your own layouts share your settings, as before. The parts' places
    /// are kept per layout already (ThemeApi.BgPerLayout.cs).
    /// </summary>
    public partial class ThemeApi
    {
        // what a home layout's preset carries: the home screen's settings, the widgets, a few top bar ones; not the layout choice
        // itself, the per-layout places (kept elsewhere), the folders, your own picture or your tile presets
        private static readonly HashSet<string> PresetSkip = new HashSet<string>
        {
            "HomeLayoutMode", "RowDesign", "BgPerLayout", "BgLayoutKey", "HomePlacePerLayout", "HomeBgOffsetX", "HomeBgOffsetY", "HomeFadeDir", "HomeFadeReach",
            "FolderGameDesign", "FolderDuplicates", "FolderInstalled", "FolderSort", "FolderTileCount", "Presets", "PresetBase", "PresetOnScreen", "BoardPresets",
            "Look", "LookPack",   // (0.96) the menu theme is one for every layout, not part of a preset
            "ProfileShow"         // (0.97) and the profile button (its picture / name): yours everywhere
        };
        private static readonly string[] PresetExtra = { "Widgets", "WidgetsOn", "AutoHideTopBar", "ShortcutsVertical" };
        // (0.93) and everything else on the home screen: the top bar (shortcuts, profile button, clock...), your own picture, the
        // animated cards, the weather and seasonal touches, card previews. A Kippix design starts from yours for these.
        private static readonly string[] PresetMore =
        {
            "HomeBgImage", "AnimArtMode", "AnimArtAllCards", "AnimArtHero", "AnimArtNoArt", "ListStretch", "SlotPinBadge", "SlotPinsWhenFiltered",
            "PreviewMode", "PreviewDelay", "BgChangeDelay", "PreviewSound", "SeasonalOn", "SeasonalMode", "SeasonSnow",
            "WeatherFxOn", "WeatherFxSync", "WeatherFxKind", "WeatherFxSchedule", "WeatherFxDays", "WeatherFxFrom", "WeatherFxWhen", "WeatherFxLivePick", "WeatherFxPick", "WeatherFxDates"
        };
        private static bool IsPresetMore(string f) { return TopBarFields.Contains(f) || Array.IndexOf(PresetMore, f) >= 0; }

        private static List<FieldInfo> presetHomeFields, presetGameFields;
        private static List<FieldInfo> PresetFields(bool game)
        {
            if (presetHomeFields == null)
            {
                var all = typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
                presetHomeFields = all.Where(f => !PresetSkip.Contains(f.Name) && (HomeLayoutFields.Contains(f.Name) || PresetExtra.Contains(f.Name) || IsPresetMore(f.Name))).ToList();
                presetGameFields = all.Where(f => GameLayoutFields.Contains(f.Name) && f.Name != "GamePreset").ToList();
            }
            return game ? presetGameFields : presetHomeFields;
        }

        private static readonly JavaScriptSerializer PresetJs = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static readonly string[] PresetSlots = { "row0", "row1", "row2", "l1", "l2", "l3", "l4", "game" };
        private static bool IsPresetSlot(string key) { return key != null && Array.IndexOf(PresetSlots, key) >= 0; }

        private PresetSlot SlotState(string key, bool make)
        {
            if (!IsPresetSlot(key)) return null;
            if (key == "game")
            {
                // (the game page's own field, with the game page's settings: a home screen reset or layout leaves it alone)
                if (settings.GamePreset == null) { if (!make) return null; settings.GamePreset = new PresetSlot(); }
                if (settings.GamePreset.Chosen == null) settings.GamePreset.Chosen = "";
                return settings.GamePreset;
            }
            if (settings.Presets == null) settings.Presets = new Dictionary<string, PresetSlot>();
            PresetSlot st;
            if (!settings.Presets.TryGetValue(key, out st) || st == null)
            {
                if (!make) return null;
                st = new PresetSlot();
                settings.Presets[key] = st;
            }
            if (st.Chosen == null) st.Chosen = "";
            MigrateCustom(st);
            return st;
        }

        /// <summary>(0.92) 0.91's Custom: now the changes kept with the design it came from.</summary>
        private static void MigrateCustom(PresetSlot st)
        {
            if (st == null) return;
            bool fromOk = PresetNumberOf(st.CustomFrom) > 0;
            if (!string.IsNullOrEmpty(st.Custom) && fromOk)
            {
                if (st.Saved == null) st.Saved = new Dictionary<string, string>();
                if (!st.Saved.ContainsKey(st.CustomFrom)) st.Saved[st.CustomFrom] = st.Custom;
            }
            if (st.Chosen == "custom") st.Chosen = fromOk ? st.CustomFrom : "kx1";
            st.Custom = null;
            st.CustomFrom = null;
        }

        private static int PresetNumberOf(string chosen)
        {
            int n;
            return chosen != null && chosen.StartsWith("kx", StringComparison.Ordinal) && int.TryParse(chosen.Substring(2), out n) && n >= 1 && n <= 3 ? n : 0;
        }

        private static string SavedOf(PresetSlot st, string key)
        {
            string s;
            return st != null && st.Saved != null && key != null && st.Saved.TryGetValue(key, out s) && !string.IsNullOrEmpty(s) ? s : null;
        }

        // ---------------- snapshots ----------------

        private HelperSettings CaptureFields(bool game)
        {
            var s = new HelperSettings();
            foreach (var f in PresetFields(game)) f.SetValue(s, f.GetValue(settings));
            return s;
        }

        private static bool PlaceKeyFor(string k, bool game) { return k != null && k.StartsWith(game ? "game." : "home.", StringComparison.Ordinal); }

        private HomePlaceSet CapturePlaces(bool game)
        {
            // (in key order, so the same places always give the same fingerprint)
            var p = new HomePlaceSet();
            CopySorted(settings.MasterOffsets, p.Offsets, game);
            CopySorted(settings.MasterSizes, p.Sizes, game);
            CopySorted(settings.MasterRotations, p.Rotations, game);
            CopySorted(settings.MasterLayers, p.Layers, game);
            CopySorted(settings.MasterOpacity, p.Opacity, game);
            return p;
        }

        private static void CopySorted<T>(Dictionary<string, T> from, Dictionary<string, T> into, bool game)
        {
            if (from == null) return;
            foreach (var k in from.Keys.Where(k => PlaceKeyFor(k, game)).OrderBy(k => k, StringComparer.Ordinal).ToList()) into[k] = from[k];
        }

        /// <summary>The fields only (what goes with a layout as it's switched; the places are kept per layout already).</summary>
        private string CaptureFieldsJson(bool game) { return PresetJs.Serialize(new PresetSnap { F = CaptureFields(game), K = PresetFields(game).Select(f => f.Name).ToArray() }); }

        /// <summary>A snapshot's settings, and which ones it holds (null: one from before 0.93). Takes both kinds: a PresetSnap,
        /// or (from before 0.93) the settings on their own.</summary>
        private static HelperSettings ParseFields(string json, out string[] keys)
        {
            keys = null;
            if (string.IsNullOrEmpty(json)) return null;
            if (json.TrimStart().StartsWith("{\"F\"", StringComparison.Ordinal))
            {
                var snap = PresetJs.Deserialize<PresetSnap>(json);
                if (snap == null) return null;
                keys = snap.K;
                return snap.F;
            }
            return PresetJs.Deserialize<HelperSettings>(json);
        }

        private string CaptureSnapJson(bool game)
        {
            var snap = new PresetSnap { F = CaptureFields(game), Places = CapturePlaces(game), K = PresetFields(game).Select(f => f.Name).ToArray() };
            if (!game) snap.Bg = new[] { settings.HomeBgOffsetX, settings.HomeBgOffsetY, settings.HomeFadeDir, settings.HomeFadeReach };
            return PresetJs.Serialize(snap);
        }

        /// <summary>keys: the settings the snapshot holds (null: an older one; only those it held then are taken, so the
        /// newer ones aren't put back to how they come).</summary>
        private void LoadFields(HelperSettings from, bool game, string[] keys)
        {
            if (from == null) return;
            var has = keys != null ? new HashSet<string>(keys) : null;
            foreach (var f in PresetFields(game))
            {
                if (has != null ? !has.Contains(f.Name) : (!game && IsPresetMore(f.Name))) continue;
                f.SetValue(settings, f.GetValue(from));
            }
            if (settings.Widgets == null) settings.Widgets = new List<WidgetConfig>();
        }

        private void LoadPlaces(HomePlaceSet p, double[] bg, bool game)
        {
            if (settings.MasterOffsets == null) settings.MasterOffsets = new Dictionary<string, double[]>();
            if (settings.MasterSizes == null) settings.MasterSizes = new Dictionary<string, double[]>();
            if (settings.MasterRotations == null) settings.MasterRotations = new Dictionary<string, double>();
            if (settings.MasterLayers == null) settings.MasterLayers = new Dictionary<string, int>();
            if (settings.MasterOpacity == null) settings.MasterOpacity = new Dictionary<string, double>();
            ClearPlaceKeys(settings.MasterOffsets, game);
            ClearPlaceKeys(settings.MasterSizes, game);
            ClearPlaceKeys(settings.MasterRotations, game);
            ClearPlaceKeys(settings.MasterLayers, game);
            ClearPlaceKeys(settings.MasterOpacity, game);
            if (p != null)
            {
                if (p.Offsets != null) foreach (var kv in p.Offsets) settings.MasterOffsets[kv.Key] = kv.Value;
                if (p.Sizes != null) foreach (var kv in p.Sizes) settings.MasterSizes[kv.Key] = kv.Value;
                if (p.Rotations != null) foreach (var kv in p.Rotations) settings.MasterRotations[kv.Key] = kv.Value;
                if (p.Layers != null) foreach (var kv in p.Layers) settings.MasterLayers[kv.Key] = kv.Value;
                if (p.Opacity != null) foreach (var kv in p.Opacity) settings.MasterOpacity[kv.Key] = kv.Value;
            }
            if (!game && bg != null && bg.Length >= 4)
            {
                settings.HomeBgOffsetX = Math.Max(-1, Math.Min(1, bg[0]));
                settings.HomeBgOffsetY = Math.Max(-1, Math.Min(1, bg[1]));
                settings.HomeFadeDir = Math.Max(0, Math.Min(4, (int)bg[2]));
                settings.HomeFadeReach = Math.Max(5, Math.Min(95, (int)bg[3]));
            }
        }

        private static void ClearPlaceKeys<T>(Dictionary<string, T> d, bool game)
        {
            foreach (var k in d.Keys.Where(k => PlaceKeyFor(k, game)).ToList()) d.Remove(k);
        }

        private void LoadSnapJson(string json, bool game, bool fieldsFromBase)
        {
            var snap = string.IsNullOrEmpty(json) ? null : PresetJs.Deserialize<PresetSnap>(json);
            HelperSettings f = snap != null ? snap.F : null;
            string[] keys = snap != null ? snap.K : null;
            if (fieldsFromBase && !game && !string.IsNullOrEmpty(settings.PresetBase))
            {
                try
                {
                    string[] bk;
                    var b = ParseFields(settings.PresetBase, out bk);
                    if (b != null) { f = b; keys = bk; }
                }
                catch (Exception) { }
            }
            LoadFields(f, game, keys);
            if (snap != null && snap.Places != null) LoadPlaces(snap.Places, snap.Bg, game);
        }

        private string Fingerprint(bool game)
        {
            string s = CaptureSnapJson(game);
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "");
        }

        /// <summary>(0.94) Each home preset setting as text, to see which ones a layout switch changes.</summary>
        private Dictionary<string, string> FieldTexts() { return FieldTexts(false); }

        private Dictionary<string, string> FieldTexts(bool game)
        {
            var d = new Dictionary<string, string>();
            foreach (var f in PresetFields(game))
            {
                try { d[f.Name] = PresetJs.Serialize(f.GetValue(settings)); } catch (Exception) { d[f.Name] = Guid.NewGuid().ToString(); }
            }
            return d;
        }

        private bool PresetChanged(PresetSlot st, bool game)
        {
            if (st == null || st.Chosen == "") return false;
            if (string.IsNullOrEmpty(st.Fp)) return false;   // (0.99.62: a design as Kippix comes on it, not seen yet: nothing changed)
            try { return Fingerprint(game) != st.Fp; } catch (Exception) { return false; }
        }

        // ---------------- going between layouts ----------------

        private string presetLeaving;   // the fields of the layout being left, taken before its places are put away

        /// <summary>From BgFollowLayout, before anything moves: the layout being left's settings.</summary>
        private void PresetLeave(string from)
        {
            presetLeaving = null;
            try { presetLeaving = CaptureFieldsJson(false); } catch (Exception ex) { logger.Warn("Kippix: presets (leaving): " + ex.Message); }
        }

        /// <summary>From BgFollowLayout, after: a layout with a preset brings its own settings out; your own layouts share yours.
        /// (Folders always show your own: inside a folder nothing is swapped.)</summary>
        private void PresetEnter(string from, string to)
        {
            try
            {
                if (presetLeaving == null) return;   // (couldn't take what was on screen: nothing is swapped, nothing lost)
                string shown = settings.PresetOnScreen ?? "";
                var b = SlotState(to, false);
                string want = b != null && b.Chosen != "" && settings.HomeLayoutMode != 5 ? to : "";
                if (shown == "" && want == "") { if (settings.PresetBase != null) settings.PresetBase = presetLeaving; return; }
                if (shown == want) return;
                // what was on screen goes back where it belongs: the preset's own, or yours
                if (shown != "")
                {
                    var a = SlotState(shown, false);
                    if (a != null && shown != to) a.Cur = presetLeaving;   // (a preset no longer there: dropped, never written over yours)
                }
                else settings.PresetBase = presetLeaving;
                string load = want != "" ? b.Cur : settings.PresetBase;
                if (string.IsNullOrEmpty(load)) return;
                string[] lk;
                var lf = ParseFields(load, out lk);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var before = FieldTexts();
                LoadFields(lf, false, lk);
                settings.PresetOnScreen = want;
                PresetWidgetPlaces(to);
                if (want != "" && string.IsNullOrEmpty(b.Fp))
                {
                    // (0.99.62) a layout that comes on a design: what it is, once it's on screen, so only your changes count as changes
                    var bs = b;
                    ui.BeginInvoke(new Action(() =>
                    {
                        try { if (HomeSlotNow == want && string.IsNullOrEmpty(bs.Fp) && bs.Chosen != "") { bs.Fp = Fingerprint(false); SaveSettings(); } }
                        catch (Exception) { }
                    }), DispatcherPriority.ApplicationIdle);
                }
                var after = FieldTexts();
                var changed = new HashSet<string>(after.Where(kv => { string was; return !before.TryGetValue(kv.Key, out was) || was != kv.Value; }).Select(kv => kv.Key));
                Dbg("presets: " + from + " → " + to + (want != "" ? " (" + b.Chosen + ")" : " (your own)"));
                ApplyAllSettings(changed);   // (it saves, once the shortcuts and info items are made from the new settings: saving first would write the old ones back)
                if (sw.ElapsedMilliseconds >= 40) PerfLog("presets: " + from + " → " + to + " took " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex) { logger.Warn("Kippix: presets (layout): " + ex.Message); }
            finally { presetLeaving = null; }
        }

        /// <summary>The widgets where this layout keeps them (its own places: ThemeApi.BgPerLayout.cs), by id.</summary>
        private void PresetWidgetPlaces(string layout)
        {
            HomePlaceSet ps;
            if (settings.HomePlacePerLayout == null || !settings.HomePlacePerLayout.TryGetValue(layout, out ps) || ps == null || ps.Widgets == null) return;
            foreach (var c in settings.Widgets)
            {
                double[] v;
                if (c == null || c.Id == null || !ps.Widgets.TryGetValue(c.Id, out v) || v == null || v.Length < 8) continue;
                c.X = v[0]; c.Y = v[1]; c.Scale = v[2]; c.WidthMul = v[3]; c.HeightMul = v[4]; c.Rotation = v[5]; c.Layer = (int)v[6]; c.Opacity = v[7];
            }
        }

        /// <summary>(From BgFollowLayout when the layout key stays the same.) Leaving Folder mode from inside a folder can bring back a
        /// layout with a preset under the same key: its own settings come out.</summary>
        private void PresetSync(string key)
        {
            if (!IsPresetSlot(key) || key == "game") return;
            var b = SlotState(key, false);
            string want = b != null && b.Chosen != "" && settings.HomeLayoutMode != 5 ? key : "";
            if (want == (settings.PresetOnScreen ?? "")) return;
            PresetLeave(key);
            PresetEnter(key, key);
        }

        // ---------------- the list ----------------

        private string presetSlot;   // the page shows this layout's presets (or the game page's: "game")
        public ObservableCollection<PickRow> PresetRows { get; private set; }
        public bool IsOptionsPresets { get { return optionsPage == "presets"; } }

        private string HomeSlotNow { get { return FolderTilesShown || settings.HomeLayoutMode == 5 || LayoutNow == 6 ? null : BgLayoutKeyNow; } }   // (folders: your own; Mosaic and the library view: no designs of their own, and the row read "Preset: l6")

        private string SlotName(string slot)
        {
            switch (slot)
            {
                case "row0": return Loc("LOC_KX_PsRow", "Row");
                case "row1": return Loc("LOC_KX_PsTall", "Row › Tall");
                case "row2": return Loc("LOC_KX_PsFull", "Row › Full Screen");
                case "l1": return Loc("LOC_KX_PsGrid", "Grid");
                case "l2": return Loc("LOC_KX_PsList", "List");
                case "l3": return Loc("LOC_KX_PsBoard", "Board");
                case "l4": return Loc("LOC_KX_PsManual", "Manual board");
                case "game": return Loc("LOC_KX_PsGame", "Game page");
            }
            return slot ?? "";
        }

        private string PresetStateText(string slot)
        {
            var st = SlotState(slot, false);
            if (st == null || st.Chosen == "") return Loc("LOC_KX_PsDefault", "Default");
            return PresetName(slot, st.Chosen);
        }

        private int PresetNumber(string chosen) { return PresetNumberOf(chosen); }

        private string PresetName(string slot, string chosen)
        {
            int n = PresetNumber(chosen);
            if (n < 1) return Loc("LOC_KX_PsDefault", "Default");
            string name, sub;
            PresetInfo(slot, n, out name, out sub);
            return name;
        }

        /// <summary>Settings › Game cards and layout › Preset: this layout's.</summary>
        public string HomePresetText { get { string s = HomeSlotNow; return s == null ? "" : PresetStateText(s); } }
        public string HomePresetLabel { get { string s = HomeSlotNow; return s == null ? Loc("LOC_KX_Preset", "Preset") : string.Format(Loc("LOC_KX_PresetFor", "Preset: {0}"), SlotName(s)); } }
        public bool HomePresetShown { get { return HomeSlotNow != null; } }
        public string GamePresetText { get { return PresetStateText("game"); } }

        private void NotifyPresets()
        {
            foreach (string n in new[] { "HomePresetText", "HomePresetLabel", "HomePresetShown", "GamePresetText" }) Notify(n);
        }

        private ICommand openPresetsCmd;
        /// <summary>CommandParameter "presets" (the home screen's, from Game cards and layout) or "gamepresets" (Game page › Layout).</summary>
        public ICommand OpenPresetsCommand
        {
            get
            {
                return openPresetsCmd ?? (openPresetsCmd = new RelayCommand(p =>
                {
                    string slot = p as string == "gamepresets" ? "game" : HomeSlotNow;
                    if (slot == null) { PlaySound("back"); return; }
                    PlaySound("select");
                    if (slot != "game") BgFollowLayout();   // (the layout's own settings are the ones on screen)
                    presetSlot = slot;
                    BuildPresetRows(null);
                    SetOptionsPage("presets");
                }));
            }
        }

        private void BuildPresetRows(string focusKey)
        {
            if (PresetRows == null) PresetRows = new ObservableCollection<PickRow>();
            PresetRows.Clear();
            string slot = presetSlot;
            if (slot == null) return;
            bool game = slot == "game";
            var st = SlotState(slot, false);
            string chosen = st != null ? st.Chosen : "";
            PresetRows.Add(new PickRow { IsHeader = true, Text = string.Format(Loc("LOC_KX_PsHeader", "PRESETS: {0}"), SlotName(slot).ToUpperInvariant()) });
            PresetRows.Add(new PickRow
            {
                Key = "default", Icon = "profile", Text = Loc("LOC_KX_PsDefault", "Default"), Sub = Loc("LOC_KX_PsDefaultSub", "Your own layout, as you had it"),
                IsSelected = chosen == "", Command = new RelayCommand(_ => UsePreset(slot, 0))
            });
            PresetRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_PsDesigns", "KIPPIX DESIGNS") });
            foreach (int i in PresetDesignNumbers(slot))
            {
                int n = i;
                string name, sub;
                PresetInfo(slot, n, out name, out sub);
                bool edited = SavedOf(st, "kx" + n) != null || (chosen == "kx" + n && PresetChanged(st, game));
                PresetRows.Add(new PickRow
                {
                    Key = "kx" + n, Icon = "star", Text = name, Sub = edited ? Loc("LOC_KX_PsEditedSub", "With your changes") : sub,
                    IsSelected = chosen == "kx" + n, Command = new RelayCommand(_ => UsePreset(slot, n))
                });
            }
            int on = PresetNumberOf(chosen);
            if (on > 0 && (SavedOf(st, chosen) != null || PresetChanged(st, game)))
            {
                string onName = PresetName(slot, chosen);
                PresetRows.Add(new PickRow
                {
                    Key = "putback", Icon = "refresh", Text = string.Format(Loc("LOC_KX_PsPutBack", "Put {0} back to how Kippix made it"), onName),
                    Command = new RelayCommand(_ => Confirm(string.Format(Loc("LOC_KX_PsPutBackTitle", "Put {0} back?"), onName),
                                                            Loc("LOC_KX_PsPutBackText", "Your changes to this design go."), Loc("LOC_KX_PsPutBackYes", "Put it back"),
                                                            () => PresetPutBack(slot)))
                });
            }
            PresetRows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "note", Text = game ? Loc("LOC_KX_PsNoteGame", "Changes the game page only. Default puts yours back.") : Loc("LOC_KX_PsNote", "Only this layout changes. Default puts yours back.") });
            Notify("PresetRows");
            string key = focusKey;
            if (key != null)
                ui.BeginInvoke(new Action(() =>
                {
                    var row = PresetRows.FirstOrDefault(r => r.Key == key);
                    if (row != null && optionsPage == "presets") UiTools.FocusItem("KX_PresetList", row);
                }), DispatcherPriority.ContextIdle);
        }

        /// <summary>0 your own (Default), 1-3 a design (with your changes to it, if you made any). fresh: Kippix's version.</summary>
        private void UsePreset(string slot, int n) { UsePreset(slot, n, false); }

        private void PresetPutBack(string slot)
        {
            var st = SlotState(slot, false);
            int n = st != null ? PresetNumberOf(st.Chosen) : 0;
            if (n < 1) { PlaySound("back"); return; }
            if (st.Saved != null) st.Saved.Remove(st.Chosen);
            UsePreset(slot, n, true);
        }

        private void UsePreset(string slot, int n, bool fresh)
        {
            try
            {
                bool game = slot == "game";
                if (!game && slot != HomeSlotNow) { PlaySound("back"); return; }   // (the layout changed under the list)
                var st = SlotState(slot, true);
                string chosen = st.Chosen;
                if (!fresh && chosen == (n == 0 ? "" : "kx" + n)) { PlaySound("back"); return; }   // (already on)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var before = FieldTexts(game);
                // what's on now is kept first: your own the first time, your changes to a design with that design
                if (chosen == "")
                {
                    if (n == 0) { PlaySound("back"); return; }
                    st.Own = CaptureSnapJson(game);
                    if (!game) settings.PresetBase = CaptureFieldsJson(false);
                }
                else if (!fresh && PresetChanged(st, game))
                {
                    if (st.Saved == null) st.Saved = new Dictionary<string, string>();
                    st.Saved[chosen] = CaptureSnapJson(game);
                }
                if (n == 0)
                {
                    LoadSnapJson(st.Own, game, true);
                    if (!game) OwnWidgetPlaces(st.Own);   // (your widgets where this layout had them, not where another of yours did)
                    st.Chosen = "";
                    st.Cur = null;
                }
                else if (!fresh && SavedOf(st, "kx" + n) != null)
                {
                    // your changes to it
                    LoadSnapJson(SavedOf(st, "kx" + n), game, false);
                    st.Chosen = "kx" + n;
                }
                else
                {
                    var s = PresetStart(game);
                    if (!game) foreach (var f in PresetFields(false)) if (IsPresetMore(f.Name)) f.SetValue(s, f.GetValue(settings));   // (0.93: yours, unless the design sets them)
                    var places = new HomePlaceSet();
                    double[] bg = { 0, 0, 0, 44 };
                    string[] keys = PresetFields(game).Select(f => f.Name).ToArray();
                    if (!BakedPreset(slot, n, s, ref places, ref bg)) BuildPreset(slot, n, s, places, ref bg);   // (0.96: the design as you set it up, when it's been put in)
                    LoadFields(s, game, keys);
                    LoadPlaces(places, bg, game);
                    st.Chosen = "kx" + n;
                }
                if (!game) settings.PresetOnScreen = st.Chosen == "" ? "" : slot;
                PlaySound("select");
                // (0.94) only what changed is made again
                var after = FieldTexts(game);
                var changed = new HashSet<string>(after.Where(kv => { string was; return !before.TryGetValue(kv.Key, out was) || was != kv.Value; }).Select(kv => kv.Key));
                if (!game) changed.Add("HomeLayoutMode");   // (the home screen's places were put back too: lay it out again)
                ApplyAllSettings(changed);   // (saves too, after the shortcuts / info items are made from the new settings)
                if (sw.ElapsedMilliseconds >= 60) PerfLog("design " + slot + " → " + (n == 0 ? "default" : "kx" + n) + " took " + sw.ElapsedMilliseconds + " ms");
                st.Fp = Fingerprint(game);
                SaveSettings();
                // once the screen has settled (the board's tiles, the widgets): what the preset is now, so only your own changes make it Custom
                string nowChosen = st.Chosen;
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (st.Chosen != nowChosen || (!game && slot != HomeSlotNow)) return;
                        st.Fp = Fingerprint(game);
                        SaveSettings();
                        NotifyPresets();
                    }
                    catch (Exception) { }
                }), DispatcherPriority.ApplicationIdle);
                Dbg("presets: " + slot + " → " + (st.Chosen == "" ? "your own" : st.Chosen));
                BuildPresetRows(n == 0 ? "default" : "kx" + n);
                NotifyPresets();
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: preset failed"); PlaySound("back"); }
        }

        private void OwnWidgetPlaces(string ownJson)
        {
            try
            {
                var snap = string.IsNullOrEmpty(ownJson) ? null : PresetJs.Deserialize<PresetSnap>(ownJson);
                if (snap == null || snap.F == null || snap.F.Widgets == null) return;
                foreach (var c in settings.Widgets)
                {
                    var o = c == null ? null : snap.F.Widgets.FirstOrDefault(x => x != null && x.Id == c.Id);
                    if (o == null) continue;
                    c.X = o.X; c.Y = o.Y; c.Scale = o.Scale; c.WidthMul = o.WidthMul; c.HeightMul = o.HeightMul; c.Rotation = o.Rotation; c.Layer = o.Layer; c.Opacity = o.Opacity;
                }
            }
            catch (Exception) { }
        }

        /// <summary>Where a design starts: Kippix's own settings, with your own picture, tile presets and Steam / weather setup kept.</summary>
        private HelperSettings PresetStart(bool game)
        {
            var s = new HelperSettings();
            s.Widgets = new List<WidgetConfig>();
            s.BoardPresets = settings.BoardPresets;
            s.HomeBgImage = settings.HomeBgImage;
            s.BoardSeeded = true;
            if (game)
            {
                // the game page's lower page: what's on it stays yours (the designs only change how it looks)
                s.DetLower = settings.DetLower == null ? null : new List<string>(settings.DetLower);
                s.DetInfoOrder = settings.DetInfoOrder == null ? null : new List<string>(settings.DetInfoOrder);
            }
            else
            {
                s.ExtraRowKeys = settings.ExtraRowKeys == null ? null : new List<string>(settings.ExtraRowKeys);
                s.InfoOrder = settings.InfoOrder == null ? null : new List<string>(settings.InfoOrder);
            }
            return s;
        }
    }
}
