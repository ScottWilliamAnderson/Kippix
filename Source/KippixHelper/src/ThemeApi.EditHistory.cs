using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Free edit extras (0.78):
    ///   - Undo / redo for every Free edit change (moving, sizing, turning, deleting / hiding, widgets added or removed, the
    ///     board's tiles, layers, opacity, crops). One history per screen (home, game page), at least 50 steps, kept while
    ///     Kippix runs (leaving and coming back to Free edit keeps it). LB / RB with nothing picked up (Free move's pointer,
    ///     the board being edited); Ctrl+Z / Ctrl+Y on a keyboard.
    ///     How it works: when editing starts, the layout's settings are noted (each field as JSON; the per-part dictionaries
    ///     part by part). Each time a change is put down (and the settings are saved), what changed since is one step: what it
    ///     was and what it became. Undo puts back what it was for just those fields / parts, so the home screen's steps and the
    ///     game page's never get in each other's way.
    ///   - Layers and opacity for what's picked up: Menu (Start; M on a keyboard) switches the D-pad between moving, Layer
    ///     (Up / Down: forward / backward, Right / Left: to the front / back) and Opacity (10–100 %). Kept in MasterLayers /
    ///     MasterOpacity (screen parts), WidgetConfig.Layer / Opacity and BoardCard.Layer / Opacity, so they go with layouts.
    ///     B (cancel) puts them back too.
    /// </summary>
    public partial class ThemeApi
    {
        private class EditStep
        {
            public string Screen;
            public Dictionary<string, string> Before, After;
        }

        private const int HistoryMax = 60;
        private const char DictSep = '\u0001';
        private static readonly string[] HistoryDicts = { "MasterOffsets", "MasterSizes", "MasterRotations", "MasterLayers", "MasterOpacity" };
        private readonly Dictionary<string, List<EditStep>> undoSteps = new Dictionary<string, List<EditStep>>();
        private readonly Dictionary<string, List<EditStep>> redoSteps = new Dictionary<string, List<EditStep>>();
        private Dictionary<string, string> editBase;   // the layout as it was at the last step (null: not editing)
        private bool historyApplying;
        private string historyNote;
        private DateTime historyNoteAt = DateTime.MinValue;
        private DispatcherTimer historyNoteTimer;
        private static FieldInfo[] historyFields;

        /// <summary>The fields a step can hold: the home screen's and game page's layout and the widgets (not Snap / grid size).</summary>
        private static FieldInfo[] HistoryFields
        {
            get
            {
                if (historyFields == null)
                    historyFields = typeof(HelperSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Where(f => { string g = FieldGroup(f.Name); return g == "home" || g == "game" || g == "widgets"; })
                        .ToArray();
                return historyFields;
            }
        }

        private static JavaScriptSerializer HistoryJson() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }

        /// <summary>The layout now: field → JSON (the dictionaries one entry each: "MasterOffsets\u0001home.logo").</summary>
        private Dictionary<string, string> EditSnapshot()
        {
            var js = HistoryJson();
            var d = new Dictionary<string, string>();
            foreach (var f in HistoryFields)
            {
                try { d[f.Name] = js.Serialize(f.GetValue(settings)); }
                catch (Exception) { }   // (a list a background job is changing: next time)
            }
            foreach (string name in HistoryDicts)
            {
                var f = typeof(HelperSettings).GetField(name);
                var dict = f != null ? f.GetValue(settings) as IDictionary : null;
                if (dict == null) continue;
                foreach (DictionaryEntry e in dict)
                    d[name + DictSep + e.Key] = js.Serialize(e.Value);
            }
            return d;
        }

        private static string ScreenOfKey(string key)
        {
            int i = key.IndexOf(DictSep);
            if (i > 0) return key.Substring(i + 1).StartsWith("game.", StringComparison.Ordinal) ? "game" : "home";
            return GameLayoutFields.Contains(key) ? "game" : "home";
        }

        private List<EditStep> Steps(Dictionary<string, List<EditStep>> all, string screen)
        {
            List<EditStep> l;
            if (!all.TryGetValue(screen, out l)) { l = new List<EditStep>(); all[screen] = l; }
            return l;
        }

        // ---------------- when steps are made ----------------

        /// <summary>Something can be changed by hand now (Free move, the board being edited, a part or widget picked up): note the layout as it is.</summary>
        private void EditBegin()
        {
            if (historyApplying) return;
            try { if (editBase == null) editBase = EditSnapshot(); }
            catch (Exception ex) { logger.Warn("Kippix: edit history: " + ex.Message); editBase = null; }
            Notify("FreeHistoryText");
        }

        /// <summary>Editing is over: the last change becomes a step, and changes made elsewhere aren't steps.</summary>
        private void EditEnd()
        {
            EditCheckpoint();
            editBase = null;
        }

        /// <summary>A pick put down (or cancelled): its change is a step; if nothing else is being edited, editing is over.</summary>
        private void EditCheckpointAfterPick()
        {
            EditCheckpoint();
            if (!freeMove && !(boardEditing && IsBoardMode)) editBase = null;
        }

        /// <summary>What changed since the last step becomes a step (split by screen).</summary>
        private void EditCheckpoint()
        {
            if (historyApplying || editBase == null) return;
            try
            {
                var now = EditSnapshot();
                var steps = new Dictionary<string, EditStep>();
                foreach (var k in now.Keys.Union(editBase.Keys))
                {
                    string a, b;
                    editBase.TryGetValue(k, out a);
                    now.TryGetValue(k, out b);
                    if (a == b) continue;
                    if (k.IndexOf(DictSep) < 0 && (a == null || b == null)) continue;   // (a field that couldn't be read once)
                    string screen = ScreenOfKey(k);
                    EditStep st;
                    if (!steps.TryGetValue(screen, out st)) { st = new EditStep { Screen = screen, Before = new Dictionary<string, string>(), After = new Dictionary<string, string>() }; steps[screen] = st; }
                    st.Before[k] = a;
                    st.After[k] = b;
                }
                foreach (var st in steps.Values)
                {
                    var list = Steps(undoSteps, st.Screen);
                    list.Add(st);
                    if (list.Count > HistoryMax) list.RemoveRange(0, list.Count - HistoryMax);
                    Steps(redoSteps, st.Screen).Clear();
                }
                editBase = now;
                if (steps.Count > 0) { Notify("FreeHistoryText"); Notify("BoardHintText"); }
            }
            catch (Exception ex) { logger.Warn("Kippix: edit history step: " + ex.Message); }
        }

        /// <summary>From SaveSettings: while editing (and nothing is picked up), every saved change is a step.</summary>
        private void EditHistoryOnSave()
        {
            if (historyApplying || editBase == null || ui == null || !ui.CheckAccess()) return;   // (a save from a worker thread isn't an edit)
            if (!freeMove && !(boardEditing && IsBoardMode)) return;
            if (masterMoving != null || widgetMoving != null || boardPicked != null || cropTile != null) return;   // (put down: the step is made then)
            EditCheckpoint();
        }

        /// <summary>A whole other layout came in (a saved layout, a reset, a profile): the steps were for the one before.</summary>
        private void EditHistoryClear()
        {
            if (historyApplying) return;
            undoSteps.Clear();
            redoSteps.Clear();
            editBase = null;
        }

        private string ScreenNow { get { return GameDetailsVisible() ? "game" : "home"; } }
        private int UndoCount { get { return Steps(undoSteps, ScreenNow).Count; } }
        private int RedoCount { get { return Steps(redoSteps, ScreenNow).Count; } }

        /// <summary>The line of hints: LB / RB undo / redo (and how many there are).</summary>
        public string FreeHistoryText
        {
            get { return string.Format(Loc("LOC_KX_UndoRedoCounts", "Undo ({0}) / redo ({1})"), UndoCount, RedoCount); }
        }

        // ---------------- undo / redo ----------------

        /// <summary>LB (undo) / RB (redo) with nothing picked up.</summary>
        private void EditUndo(bool redo)
        {
            if (masterMoving != null || widgetMoving != null || boardPicked != null || cropTile != null) { PlaySound("back"); return; }
            EditCheckpoint();   // (a change not made into a step yet is one now)
            string screen = ScreenNow;
            var from = Steps(redo ? redoSteps : undoSteps, screen);
            if (from.Count == 0)
            {
                PlaySound("back");
                SetHistoryNote(redo ? Loc("LOC_KX_RedoNone", "Nothing to redo") : Loc("LOC_KX_UndoNone", "Nothing to undo"));
                return;
            }
            var st = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Steps(redo ? undoSteps : redoSteps, screen).Add(st);
            historyApplying = true;
            HashSet<string> keys;
            try
            {
                keys = new HashSet<string>(st.Before.Keys);
                PutHistoryValues(redo ? st.After : st.Before);
                SaveSettings();
            }
            finally { historyApplying = false; }
            try { AfterHistory(keys); } catch (Exception ex) { logger.Warn("Kippix: undo: " + ex.Message); }
            try { editBase = EditSnapshot(); } catch (Exception) { editBase = null; }
            PlaySound("move");
            SetHistoryNote(string.Format(redo ? Loc("LOC_KX_Redone", "Redone   ·   {0} more to redo") : Loc("LOC_KX_Undone", "Undone   ·   {0} more to undo"), from.Count));
        }

        private void PutHistoryValues(Dictionary<string, string> values)
        {
            var js = HistoryJson();
            foreach (var kv in values)
            {
                try
                {
                    int i = kv.Key.IndexOf(DictSep);
                    if (i > 0)
                    {
                        var f = typeof(HelperSettings).GetField(kv.Key.Substring(0, i));
                        if (f == null) continue;
                        var dict = f.GetValue(settings) as IDictionary;
                        if (dict == null) { dict = (IDictionary)Activator.CreateInstance(f.FieldType); f.SetValue(settings, dict); }
                        string k = kv.Key.Substring(i + 1);
                        if (kv.Value == null) dict.Remove(k);
                        else dict[k] = js.Deserialize(kv.Value, f.FieldType.GetGenericArguments()[1]);
                    }
                    else
                    {
                        var f = typeof(HelperSettings).GetField(kv.Key);
                        if (f != null && kv.Value != null) f.SetValue(settings, js.Deserialize(kv.Value, f.FieldType));
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: undo " + kv.Key + ": " + ex.Message); }
            }
        }

        private static readonly HashSet<string> BoardHistoryFields = new HashSet<string> { "BoardCards", "ManualCards", "FreeCards", "ManualPages", "FreePages", "BoardCols", "BoardRows", "ManualCols", "ManualRows", "ManualGroups" };

        /// <summary>The screen shows what the step put back.</summary>
        private void AfterHistory(HashSet<string> keys)
        {
            bool widgets = keys.Contains("Widgets") || keys.Contains("WidgetsOn");
            bool board = keys.Any(k => BoardHistoryFields.Contains(k));
            bool other = keys.Any(k => k.IndexOf(DictSep) < 0 && !BoardHistoryFields.Contains(k) && k != "Widgets" && k != "WidgetsOn");
            NormalizeEditExtras();
            if (widgets) SyncWidgetItems();
            if (other)
            {
                Notify("");
                NotifyCardSettings();
                ApplyCardLayoutSoon();
                ApplyDetPlacement();
                HomeModeChanged();
            }
            ApplyMasterOffsets();   // (sizes, turns, kept room, layers and opacity too)
            NotifyMoved();
            ApplyHomeLayoutSoon();
            if (board && IsBoardMode)
            {
                if (boardEditCard != null && !BCards.Contains(boardEditCard)) boardEditCard = null;
                BoardSyncTiles();
                BoardFillPage();
                NotifyBoard();
                BoardPlaceWidgetsSoon();
                BoardRefreshSoon();
            }
            else if (IsBoardMode) BoardPlaceWidgetsSoon();
            if (freeMove)
                ui.BeginInvoke(new Action(() => { if (IsFreeCursor) { UpdateFreeTarget(); Notify("MasterMoveText"); } }), DispatcherPriority.ContextIdle);
            if (boardEditing && IsBoardMode && board)
                ui.BeginInvoke(new Action(() => { if (boardEditing && !BoardHasFocus()) FocusBoard(); }), DispatcherPriority.ContextIdle);
        }

        /// <summary>The widgets on screen follow settings.Widgets (kept where they're the same, so their pictures and data stay).</summary>
        private void SyncWidgetItems()
        {
            if (Widgets == null) return;
            FixWidgetConfigs();
            var js = HistoryJson();
            var want = settings.Widgets ?? new List<WidgetConfig>();
            for (int i = Widgets.Count - 1; i >= 0; i--)
                if (!want.Any(c => c.Id == Widgets[i].Config.Id)) { if (ReferenceEquals(editingWidget, Widgets[i])) editingWidget = null; Widgets.RemoveAt(i); }
            for (int i = 0; i < want.Count; i++)
            {
                var c = want[i];
                var w = Widgets.FirstOrDefault(x => x.Config.Id == c.Id);
                if (w == null)
                {
                    w = MakeWidget(c);
                    Widgets.Insert(Math.Min(i, Widgets.Count), w);
                    RefreshWidget(w);
                    continue;
                }
                int at = Widgets.IndexOf(w);
                if (at != i && i < Widgets.Count) Widgets.Move(at, i);
                if (ReferenceEquals(w.Config, c)) continue;
                string was = js.Serialize(new object[] { w.Config.Type, w.Config.Subject, w.Config.Custom });
                w.Config = c;
                w.Kind = KindOf(c);
                ApplyWidgetLook(w);
                w.Title = WidgetTitleOf(c);
                w.Name = WidgetFullName(c);
                w.PlaceText = PlaceTextOf(c);
                w.Place();
                w.TurnChanged();
                w.ExtrasChanged();
                if (was != js.Serialize(new object[] { c.Type, c.Subject, c.Custom })) RefreshWidget(w);
            }
            if (Widgets.Count > 0 && widgetTimer != null && !widgetTimer.IsEnabled) widgetTimer.Start();
            Notify("ShowWidgets");
            Notify("WidgetCountText");
        }

        private void SetHistoryNote(string text)
        {
            historyNote = text;
            historyNoteAt = DateTime.Now;
            Notify("MasterMoveText");
            Notify("BoardHintText");
            Notify("FreeHistoryText");
            if (ui == null) return;
            if (historyNoteTimer == null)
            {
                historyNoteTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(2.6) };
                historyNoteTimer.Tick += (s, e) => { historyNoteTimer.Stop(); historyNote = null; Notify("MasterMoveText"); Notify("BoardHintText"); };
            }
            historyNoteTimer.Stop();
            historyNoteTimer.Start();
        }

        /// <summary>"Undone" for a moment after LB / RB (null otherwise).</summary>
        private string HistoryNoteNow { get { return historyNote != null && (DateTime.Now - historyNoteAt).TotalSeconds < 3 ? historyNote : null; } }

        /// <summary>Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) on a keyboard. True if it was one.</summary>
        private bool HistoryKeyboardKey(Key key)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return false;
            if (key == Key.Z) { EditUndo((Keyboard.Modifiers & ModifierKeys.Shift) != 0); return true; }
            if (key == Key.Y) { EditUndo(true); return true; }
            return false;
        }

        // =====================================================================
        // Layers and opacity
        // =====================================================================

        private const int LayerMin = -50, LayerMax = 50;

        private int MasterLayer(string key)
        {
            int v;
            return settings.MasterLayers != null && settings.MasterLayers.TryGetValue(key, out v) ? Math.Max(LayerMin, Math.Min(LayerMax, v)) : 0;
        }

        private void SetMasterLayer(string key, int v)
        {
            if (settings.MasterLayers == null) settings.MasterLayers = new Dictionary<string, int>();
            v = Math.Max(LayerMin, Math.Min(LayerMax, v));
            if (v == 0) settings.MasterLayers.Remove(key); else settings.MasterLayers[key] = v;
        }

        private static double CleanOpacity(double v)
        {
            if (double.IsNaN(v) || v <= 0 || v > 1) return 1;
            return Math.Max(0.1, Math.Round(v, 2));
        }

        private double MasterOpacityOf(string key)
        {
            double v;
            return settings.MasterOpacity != null && settings.MasterOpacity.TryGetValue(key, out v) ? CleanOpacity(v) : 1;
        }

        private void SetMasterOpacity(string key, double v)
        {
            if (settings.MasterOpacity == null) settings.MasterOpacity = new Dictionary<string, double>();
            v = CleanOpacity(v);
            if (v >= 0.999) settings.MasterOpacity.Remove(key); else settings.MasterOpacity[key] = v;
        }

        /// <summary>Put things back (one part): its layer and opacity go too.</summary>
        private void ClearMasterExtras(string key)
        {
            if (settings.MasterLayers != null) settings.MasterLayers.Remove(key);
            if (settings.MasterOpacity != null) settings.MasterOpacity.Remove(key);
        }

        /// <summary>A reset: the layers and opacity of the parts it covers (as the offsets).</summary>
        private void ResetMasterExtras(string scope)
        {
            Func<string, bool> hit = k => scope == "everything" || scope == "layout" || k.StartsWith(scope + ".", StringComparison.Ordinal)
                                          || (scope == "topbar" && (k == "home.topbar" || k == "home.profile" || k == "home.shortcuts"));
            if (settings.MasterLayers != null) foreach (var k in settings.MasterLayers.Keys.ToList()) if (hit(k)) settings.MasterLayers.Remove(k);
            if (settings.MasterOpacity != null) foreach (var k in settings.MasterOpacity.Keys.ToList()) if (hit(k)) settings.MasterOpacity.Remove(k);
        }

        /// <summary>Settings loaded: layers / opacity / crops in range, your own tiles' definitions tidy.</summary>
        private void NormalizeEditExtras()
        {
            if (settings.MasterLayers == null) settings.MasterLayers = new Dictionary<string, int>();
            if (settings.MasterOpacity == null) settings.MasterOpacity = new Dictionary<string, double>();
            foreach (var k in settings.MasterLayers.Keys.ToList())
            {
                int v = Math.Max(LayerMin, Math.Min(LayerMax, settings.MasterLayers[k]));
                if (v == 0) settings.MasterLayers.Remove(k); else settings.MasterLayers[k] = v;
            }
            foreach (var k in settings.MasterOpacity.Keys.ToList())
            {
                double v = CleanOpacity(settings.MasterOpacity[k]);
                if (v >= 0.999) settings.MasterOpacity.Remove(k); else settings.MasterOpacity[k] = v;
            }
            if (settings.Widgets != null)
                foreach (var c in settings.Widgets)
                {
                    if (c == null) continue;
                    c.Opacity = CleanOpacity(c.Opacity);
                    c.Layer = Math.Max(LayerMin, Math.Min(LayerMax, c.Layer));
                    NormalizeCustom(c);
                }
            foreach (var cards in new[] { settings.BoardCards, settings.ManualCards, settings.FreeCards, settings.BoardPresets })
                if (cards != null)
                    foreach (var c in cards)
                    {
                        if (c == null) continue;
                        c.Opacity = CleanOpacity(c.Opacity);
                        c.Layer = Math.Max(LayerMin, Math.Min(LayerMax, c.Layer));
                        MigrateOldCrop(c);   // (0.78's crop: X / Y / W / H become the part that's left)
                    }
        }

        // ---------------- the screen's parts ----------------

        private readonly Dictionary<FrameworkElement, int[]> layerWas = new Dictionary<FrameworkElement, int[]>();   // [its own ZIndex, what we set]
        private readonly Dictionary<FrameworkElement, Brush> opacityMasks = new Dictionary<FrameworkElement, Brush>();

        /// <summary>
        /// Every part's layer (added to its own ZIndex, so it goes in front of / behind the parts beside it in the same panel)
        /// and opacity (an opacity mask, so the theme's own fades and bindings on Opacity are left alone). Called with the offsets.
        /// </summary>
        private void ApplyMasterExtras()
        {
            bool none = (settings.MasterLayers == null || settings.MasterLayers.Count == 0) && (settings.MasterOpacity == null || settings.MasterOpacity.Count == 0);
            if (none && layerWas.Count == 0 && opacityMasks.Count == 0) return;
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                foreach (var el in MasterElements)
                {
                    if (MasterAway(el)) continue;
                    int elLayer = MasterLayer(el.Key);
                    double op = MasterOpacityOf(el.Key);
                    foreach (string name in el.Names)
                    {
                        var fe = MasterFind(win, name);
                        if (fe == null) continue;
                        int layer = elLayer;
                        // the layer
                        if (fe.ReadLocalValue(Panel.ZIndexProperty) is System.Windows.Expression) layer = 0;   // (the theme binds it: left alone)
                        int cur = Panel.GetZIndex(fe);
                        int[] w;
                        if (layerWas.TryGetValue(fe, out w)) { if (cur != w[1]) w[0] = cur; }   // (something else set it since: that's its own now)
                        else if (layer != 0) { w = new[] { cur, cur }; layerWas[fe] = w; }
                        if (w != null)
                        {
                            int want = w[0] + layer;
                            if (cur != want) Panel.SetZIndex(fe, want);
                            w[1] = want;
                            if (layer == 0) layerWas.Remove(fe);
                        }
                        // the opacity
                        Brush mine;
                        opacityMasks.TryGetValue(fe, out mine);
                        object local = fe.ReadLocalValue(UIElement.OpacityMaskProperty);
                        bool ours = mine != null && ReferenceEquals(local, mine);
                        if (op >= 0.999)
                        {
                            if (ours) fe.ClearValue(UIElement.OpacityMaskProperty);
                            opacityMasks.Remove(fe);
                            continue;
                        }
                        if (!ours && local != DependencyProperty.UnsetValue) continue;   // the theme has its own mask on it
                        byte a = (byte)Math.Round(op * 255);
                        var sc = mine as SolidColorBrush;
                        if (ours && sc != null && sc.Color.A == a) continue;
                        var b = new SolidColorBrush(Color.FromArgb(a, 0, 0, 0));
                        b.Freeze();
                        fe.OpacityMask = b;
                        opacityMasks[fe] = b;
                    }
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: layers / opacity: " + ex.Message); }
        }

        /// <summary>
        /// The new place in the stack: <paramref name="action"/> 1 forward, -1 backward, 2 to the front, -2 to the back, among
        /// items drawn in order of (z, order). Null when it's already there.
        /// </summary>
        private static int? StackMove(List<Tuple<object, int, int>> all, object me, int action)
        {
            var sorted = all.OrderBy(t => t.Item2).ThenBy(t => t.Item3).ToList();
            int i = sorted.FindIndex(t => ReferenceEquals(t.Item1, me) || Equals(t.Item1, me));
            if (i < 0 || sorted.Count < 2) return null;
            int z = sorted[i].Item2;
            switch (action)
            {
                case 2: return i == sorted.Count - 1 ? (int?)null : sorted[sorted.Count - 1].Item2 + 1;
                case -2: return i == 0 ? (int?)null : sorted[0].Item2 - 1;
                case 1: return i == sorted.Count - 1 ? (int?)null : Math.Max(z, sorted[i + 1].Item2) + (sorted[i + 1].Item2 >= z ? 1 : 0);
                default: return i == 0 ? (int?)null : Math.Min(z, sorted[i - 1].Item2) - (sorted[i - 1].Item2 <= z ? 1 : 0);
            }
        }

        // ---------------- what's picked up: Move / Layer / Opacity ----------------

        private int pickMode;            // 0 moving (or sizing), 1 layer, 2 opacity
        private object pickModeOwner;    // what it's for (a MasterElement, a WidgetItem, a BoardTile)
        private int pickStartLayer;
        private double pickStartOpacity = 1;
        private bool pickExtrasTouched;

        private object PickedThing { get { return masterMoving != null ? (object)masterMoving : widgetMoving != null ? (object)widgetMoving : boardPicked; } }

        private bool PickModeActive { get { var p = PickedThing; return pickMode != 0 && p != null && ReferenceEquals(p, pickModeOwner); } }

        private void EnsurePickOwner()
        {
            var p = PickedThing;
            if (ReferenceEquals(p, pickModeOwner)) return;
            pickModeOwner = p;
            pickMode = 0;
            pickExtrasTouched = false;
            pickStartLayer = PickedLayer();
            pickStartOpacity = PickedOpacity();
        }

        private int PickedLayer()
        {
            if (masterMoving != null) return MasterLayer(masterMoving.Key);
            if (widgetMoving != null) return widgetMoving.Config.Layer;
            if (boardPicked != null && boardPicked.Card != null) return boardPicked.Card.Layer;
            return 0;
        }

        private double PickedOpacity()
        {
            if (masterMoving != null) return MasterOpacityOf(masterMoving.Key);
            if (widgetMoving != null) return CleanOpacity(widgetMoving.Config.Opacity);
            if (boardPicked != null && boardPicked.Card != null) return CleanOpacity(boardPicked.Card.Opacity);
            return 1;
        }

        private void SetPickedLayer(int v)
        {
            v = Math.Max(LayerMin, Math.Min(LayerMax, v));
            if (masterMoving != null) { SetMasterLayer(masterMoving.Key, v); ApplyMasterExtras(); }
            else if (widgetMoving != null) { widgetMoving.Config.Layer = v; widgetMoving.ExtrasChanged(); }
            else if (boardPicked != null && boardPicked.Card != null) { boardPicked.Card.Layer = v; BoardPlace(boardPicked); }
        }

        private void SetPickedOpacity(double v)
        {
            v = CleanOpacity(v);
            if (masterMoving != null) { SetMasterOpacity(masterMoving.Key, v); ApplyMasterExtras(); }
            else if (widgetMoving != null) { widgetMoving.Config.Opacity = v; widgetMoving.ExtrasChanged(); }
            else if (boardPicked != null && boardPicked.Card != null) { boardPicked.Card.Opacity = v; boardPicked.CardOpacity = v; }
        }

        /// <summary>A pick ended: B puts the layer and opacity back as well. Then back to moving for the next one.</summary>
        private void EndPickExtras(bool keep)
        {
            if (!keep && pickExtrasTouched && pickModeOwner != null && ReferenceEquals(pickModeOwner, PickedThing))
            {
                SetPickedLayer(pickStartLayer);
                SetPickedOpacity(pickStartOpacity);
            }
            pickModeOwner = null;
            pickMode = 0;
            pickExtrasTouched = false;
        }

        /// <summary>Everything that shares a stack with what's picked up: (thing, z, order).</summary>
        private List<Tuple<object, int, int>> PickedStack(out object me, out int zBase)
        {
            return StackOf(masterMoving, widgetMoving, boardPicked, out me, out zBase);
        }

        /// <summary>(0.99.2) The stack a part / widget / board tile is in (picked up or only highlighted).</summary>
        private List<Tuple<object, int, int>> StackOf(MasterElement masterEl, WidgetItem widget, BoardTile tile, out object me, out int zBase)
        {
            me = null;
            zBase = 0;
            var all = new List<Tuple<object, int, int>>();
            if (widget != null)
            {
                me = widget;
                for (int i = 0; i < Widgets.Count; i++) all.Add(Tuple.Create((object)Widgets[i], Widgets[i].Config.Layer, i));
                return all;
            }
            if (tile != null && tile.Card != null)
            {
                me = tile;
                for (int i = 0; i < BoardTiles.Count; i++)
                    if (BoardTiles[i].IsCard && BoardTiles[i].Card != null) all.Add(Tuple.Create((object)BoardTiles[i], BoardTiles[i].Card.Layer, i));
                return all;
            }
            if (masterEl != null)
            {
                var win = Application.Current.MainWindow;
                FrameworkElement fe = null;
                foreach (string n in masterEl.Names) { var f = UiTools.FindByName(win, n); if (f != null && f.IsVisible) { fe = f; break; } }
                var parent = fe != null ? VisualTreeHelper.GetParent(fe) as Panel : null;
                if (parent == null) return all;
                me = fe;
                int[] w;
                zBase = layerWas.TryGetValue(fe, out w) ? w[0] : Panel.GetZIndex(fe) - MasterLayer(masterEl.Key);
                for (int i = 0; i < parent.Children.Count; i++)
                {
                    var c = parent.Children[i];
                    if (c == null || (c.Visibility != Visibility.Visible && !ReferenceEquals(c, fe))) continue;
                    var cfe = c as FrameworkElement;
                    if (cfe != null && Equals(cfe.Tag, "bbgap")) continue;   // (a deleted part's kept room)
                    all.Add(Tuple.Create((object)c, Panel.GetZIndex(c), i));
                }
            }
            return all;
        }

        private void PickLayerMove(int action)
        {
            object me;
            int zBase;
            var all = PickedStack(out me, out zBase);
            int? z = me != null ? StackMove(all, me, action) : null;
            if (!z.HasValue) { PlaySound("back"); return; }
            int layer = z.Value - zBase;
            if (layer < LayerMin || layer > LayerMax) { PlaySound("back"); return; }
            pickExtrasTouched = true;
            SetPickedLayer(layer);
            PlaySound("move");
            Notify("MasterMoveText");
            Notify("BoardHintText");
        }

        // ---------------- (0.99.2) Layer without picking up: LT / RT (PgUp / PgDn) on what's highlighted ----------------

        private string layerNote, layerNoteTarget;   // "Layer: 2 of 4 from the back" after a change, while that one's highlighted

        /// <summary>What's highlighted, as the part / widget / board tile it is (one of the three, or none).</summary>
        private bool LayerTargetOf(string target, out MasterElement el, out WidgetItem w, out BoardTile bt)
        {
            el = null; w = null; bt = null;
            if (target == null) return false;
            if (target.StartsWith("w:", StringComparison.Ordinal)) w = Widgets.FirstOrDefault(x => "w:" + x.Config.Id == target);
            else if (target.StartsWith("b:", StringComparison.Ordinal))
            {
                if (!IsManualMode) return false;   // (the Board: tiles can't sit over each other)
                bt = BoardTiles.FirstOrDefault(x => x.IsCard && x.Card != null && "b:" + x.Card.Id == target);
            }
            else if (target != "board") el = MasterElements.FirstOrDefault(m => m.Key == target);
            return el != null || w != null || bt != null;
        }

        /// <summary>It can go in front of / behind something beside it.</summary>
        private bool CanLayer(string target)
        {
            MasterElement el; WidgetItem w; BoardTile bt;
            if (!LayerTargetOf(target, out el, out w, out bt)) return false;
            object me; int zBase;
            try { return StackOf(el, w, bt, out me, out zBase).Count > 1 && me != null; } catch (Exception) { return false; }
        }

        /// <summary>One layer forward (1) / backward (-1) for <paramref name="target"/> ("w:" widget, "b:" Manual board tile, a part's key).
        /// An undo step of its own.</summary>
        private bool LayerStep(string target, int action)
        {
            MasterElement el; WidgetItem w; BoardTile bt;
            if (!LayerTargetOf(target, out el, out w, out bt)) { PlaySound("back"); return false; }
            object me; int zBase;
            var all = StackOf(el, w, bt, out me, out zBase);
            int? z = me != null ? StackMove(all, me, action) : null;
            if (!z.HasValue) { PlaySound("back"); return false; }
            int layer = Math.Max(LayerMin, Math.Min(LayerMax, z.Value - zBase));
            if (el != null) { SetMasterLayer(el.Key, layer); ApplyMasterExtras(); }
            else if (w != null) { w.Config.Layer = layer; w.ExtrasChanged(); }
            else { bt.Card.Layer = layer; BoardPlace(bt); }
            SaveSettings();
            EditCheckpoint();
            PlaySound("move");
            // where it is now
            all = StackOf(el, w, bt, out me, out zBase);
            var sorted = all.OrderBy(t => t.Item2).ThenBy(t => t.Item3).ToList();
            int i = sorted.FindIndex(t => ReferenceEquals(t.Item1, me));
            string where = i < 0 || sorted.Count < 2 ? "" : i == sorted.Count - 1 ? Loc("LOC_KX_LayerFront", "at the front")
                         : i == 0 ? Loc("LOC_KX_LayerBack", "at the back") : string.Format(Loc("LOC_KX_LayerOf", "{0} of {1} from the back"), i + 1, sorted.Count);
            layerNote = string.Format(Loc("LOC_KX_LayerStatus", "Layer: {0}"), where);
            layerNoteTarget = target;
            Notify("MasterMoveText");
            Notify("BoardHintText");
            return true;
        }

        private void PickOpacityStep(int dir)
        {
            double now = PickedOpacity();
            double v = Math.Max(0.1, Math.Min(1, Math.Round((now + dir * 0.1) * 10) / 10));
            if (Math.Abs(v - now) < 0.001) { PlaySound("back"); return; }
            pickExtrasTouched = true;
            SetPickedOpacity(v);
            PlaySound("move");
            Notify("MasterMoveText");
            Notify("BoardHintText");
        }

        /// <summary>Menu (Start; M on a keyboard; on the board X, since 0.80 Start crops there) while something is picked up:
        /// Move → Layer → Opacity. <paramref name="button"/> "Start" or "Menu" (the button that cycles). True if the key was used here.</summary>
        private bool PickExtrasKey(string button, Key key)
        {
            if (PickedThing == null) return false;
            EnsurePickOwner();
            bool menu = button == "Start" || button == "Menu" || (button == null && key == Key.M);
            if (menu)
            {
                pickMode = (pickMode + 1) % 3;
                PlaySound("move");
                Notify("MasterMoveText");
                Notify("BoardHintText");
                Notify("PickModeHint");
                return true;
            }
            if (pickMode == 0 || button != null) return false;
            int up = key == Key.Up ? 1 : key == Key.Down ? -1 : 0, side = key == Key.Right ? 1 : key == Key.Left ? -1 : 0;
            if (up == 0 && side == 0) return false;
            if (pickMode == 1) PickLayerMove(up != 0 ? up : side * 2);
            else PickOpacityStep(up != 0 ? up : side);
            return true;
        }

        /// <summary>The line of text while Layer / Opacity is on.</summary>
        private string PickModeText()
        {
            if (pickMode == 1)
            {
                object me;
                int zBase;
                var all = PickedStack(out me, out zBase);
                var sorted = all.OrderBy(t => t.Item2).ThenBy(t => t.Item3).ToList();
                int i = sorted.FindIndex(t => ReferenceEquals(t.Item1, me));
                string where = i < 0 || sorted.Count < 2 ? Loc("LOC_KX_LayerAlone", "nothing beside it to go in front of")
                             : i == sorted.Count - 1 ? Loc("LOC_KX_LayerFront", "at the front")
                             : i == 0 ? Loc("LOC_KX_LayerBack", "at the back")
                             : string.Format(Loc("LOC_KX_LayerOf", "{0} of {1} from the back"), i + 1, sorted.Count);
                return string.Format(Loc("LOC_KX_LayerStatus", "Layer: {0}"), where);   // (the buttons: the hints under it, ThemeApi.EditHints.cs)
            }
            return string.Format(Loc("LOC_KX_OpacityStatus", "Opacity {0}%"), Math.Round(PickedOpacity() * 100));
        }

        /// <summary>The board: a tile's place in the stack (picked up: on top of everything while it moves).</summary>
        private int BoardZ(BoardTile t)
        {
            if (t.IsPicked || (cropTile != null && ReferenceEquals(t, cropTile))) return 1000;
            if (t.IsSlot || t.Card == null) return 0;
            return 200 + Math.Max(LayerMin, Math.Min(LayerMax, t.Card.Layer)) * 3 + (t.Card.Id == boardFocusId ? 1 : 0);
        }
    }
}
