using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Edits (0.27: Quick Edits and Master Edits in one): each part of a screen sits at its set spot (left / centre / right,
    /// the height bars) plus a fine placement on top, moved with the D-pad or dragged with the mouse, on a grid or freehand.
    /// Fine placement is an extra TranslateTransform (next to any transform the theme animates, so the slide-ins still work).
    /// </summary>
    public partial class ThemeApi
    {
        private class MasterElement
        {
            public string Key, Screen, LocKey, Fallback;
            public string[] Names;   // elements in the theme moved together (e.g. the three spots the filter name can use)
        }

        private static readonly MasterElement[] MasterElements =
        {
            new MasterElement { Key = "home.cards",  Screen = "home", LocKey = "LOC_KX_MeCards",   Fallback = "Game cards",   Names = new[] { "PART_ListGameItems", "KX_CarouselHost" } },   // (Row's carousel moves with it)
            new MasterElement { Key = "home.profile", Screen = "home", LocKey = "LOC_KX_MeProfile", Fallback = "Profile button", Names = new[] { "KX_TopLeft" } },
            new MasterElement { Key = "home.shortcuts", Screen = "home", LocKey = "LOC_KX_MeShortcuts", Fallback = "Shortcut bar", Names = new[] { "KX_TopRight" } },
            new MasterElement { Key = "home.logo",   Screen = "home", LocKey = "LOC_KX_MeLogo",    Fallback = "Game logo",    Names = new[] { "KX_LogoArea" } },
            new MasterElement { Key = "home.info",   Screen = "home", LocKey = "LOC_KX_MeInfo",    Fallback = "Game info",    Names = new[] { "KX_InfoLine" } },
            new MasterElement { Key = "home.hints",  Screen = "home", LocKey = "LOC_KX_MeHints",   Fallback = "Button hints", Names = new[] { "KX_HintsRow" } },
            new MasterElement { Key = "home.filter", Screen = "home", LocKey = "LOC_KX_MeFilter",  Fallback = "Filter name",  Names = new[] { "KX_FilterAbove" } },
            // (0.96) under the game info, each its own part
            new MasterElement { Key = "home.hltb",    Screen = "home", LocKey = "LOC_KX_MeHomeHltb",    Fallback = "How long to beat", Names = new[] { "KX_HomeHltb" } },
            new MasterElement { Key = "home.players", Screen = "home", LocKey = "LOC_KX_MeHomePlayers", Fallback = "Playing now",      Names = new[] { "KX_HomePlayers" } },
            new MasterElement { Key = "home.friends", Screen = "home", LocKey = "LOC_KX_MeHomeFriends", Fallback = "Friends",          Names = new[] { "KX_HomeFriends" } },
            new MasterElement { Key = "home.progress", Screen = "home", LocKey = "LOC_KX_MeHomeProgress", Fallback = "Story progress", Names = new[] { "KX_HomeProgress" } },   // (0.99.50)
            new MasterElement { Key = "game.logo",   Screen = "game", LocKey = "LOC_KX_MeDetLogo", Fallback = "Logo",         Names = new[] { "DetLogo" } },
            // the info column's parts, each on its own (0.34; before, they moved as one "game.info")
            new MasterElement { Key = "game.stats",   Screen = "game", LocKey = "LOC_KX_MeDetStats",   Fallback = "Play time and stats", Names = new[] { "KX_DetStats" } },
            new MasterElement { Key = "game.buttons", Screen = "game", LocKey = "LOC_KX_MeDetButtons", Fallback = "Buttons",            Names = new[] { "DetButtons", "KX_DetDlc" } },
            new MasterElement { Key = "game.facts",   Screen = "game", LocKey = "LOC_KX_MeDetFacts",   Fallback = "Game details",       Names = new[] { "KX_DetFacts" } },
            new MasterElement { Key = "game.friends", Screen = "game", LocKey = "LOC_KX_MeDetFriends", Fallback = "Friends who play it", Names = new[] { "KX_DetFriends" } },
            new MasterElement { Key = "game.desc",    Screen = "game", LocKey = "LOC_KX_MeDetDesc",    Fallback = "Description",        Names = new[] { "KX_DetDescButton" } },
            // the lower page's cards (wherever they are: down there, or sent up to the top page)
            new MasterElement { Key = "game.sec.history",  Screen = "game", LocKey = "LOC_KX_SecPlayHistory",  Fallback = "Play history",     Names = new[] { "KX_SecHistory" } },
            new MasterElement { Key = "game.sec.hltb",     Screen = "game", LocKey = "LOC_KX_SecHltb",     Fallback = "How long to beat", Names = new[] { "KX_SecHltb" } },
            new MasterElement { Key = "game.sec.ach",      Screen = "game", LocKey = "LOC_KX_SecAchHighlights",    Fallback = "Achievements",     Names = new[] { "KX_SecAch" } },
            new MasterElement { Key = "game.sec.status",   Screen = "game", LocKey = "LOC_KX_SecStatusRating",  Fallback = "Status",           Names = new[] { "KX_SecStatus" } },
            new MasterElement { Key = "game.sec.notes",    Screen = "game", LocKey = "LOC_KX_SecNotes",    Fallback = "Notes",            Names = new[] { "KX_SecNotes" } },
            new MasterElement { Key = "game.sec.scores",   Screen = "game", LocKey = "LOC_KX_SecScores",   Fallback = "Scores",           Names = new[] { "KX_SecScores" } },
            new MasterElement { Key = "game.sec.saves",    Screen = "game", LocKey = "LOC_KX_SecSaves",    Fallback = "Save backups",     Names = new[] { "KX_SecSaves" } },   // (0.88)
            new MasterElement { Key = "game.sec.mods",     Screen = "game", LocKey = "LOC_KX_PartMods",    Fallback = "Mods",             Names = new[] { "KX_SecMods" } },
            new MasterElement { Key = "game.sec.news",     Screen = "game", LocKey = "LOC_KX_PartNews",    Fallback = "News",             Names = new[] { "KX_SecNews" } },
            new MasterElement { Key = "game.sec.captures", Screen = "game", LocKey = "LOC_KX_SecYourCaptures", Fallback = "Your captures",    Names = new[] { "KX_SecCaptures" } },
            new MasterElement { Key = "game.sec.similar",  Screen = "game", LocKey = "LOC_KX_SecMoreLikeThis", Fallback = "More like this",   Names = new[] { "KX_SecSimilar" } },
            new MasterElement { Key = "game.media",  Screen = "game", LocKey = "LOC_KX_MeDetMedia", Fallback = "Cover / trailer", Names = new[] { "CoverColumn" } },
            new MasterElement { Key = "game.clock",  Screen = "game", LocKey = "LOC_KX_MeDetClock", Fallback = "Clock",       Names = new[] { "DetClock" } },
            new MasterElement { Key = "game.hints",  Screen = "game", LocKey = "LOC_KX_MeHints",    Fallback = "Button hints", Names = new[] { "KX_DetHints" } },   // (0.75: so Free move can delete them)
        };

        private static readonly int[] MasterGrids = { 1, 5, 10, 20, 40, 80 };   // (0.99.1: 1 and 5 px too)
        private static readonly int[] MasterSteps = { 1, 5, 20, 60 };   // free movement step sizes (LB / RB)

        public ICommand CycleMasterGridCommand { get; private set; }

        private void InitMasterCommands()
        {
            CycleMasterGridCommand = new RelayCommand(_ =>
            {
                int i = Array.IndexOf(MasterGrids, settings.MasterGrid);
                settings.MasterGrid = MasterGrids[(i + 1) % MasterGrids.Length];
                SaveSettings();
                NotifyMaster();
            });
            InitFreeMoveCommands();
            RefreshMasterItems();
        }

        // ---------------- settings ----------------

        public bool MasterSnap
        {
            get { return settings.MasterSnap; }
            set { if (settings.MasterSnap != value) { settings.MasterSnap = value; SaveSettings(); NotifyMaster(); } }
        }

        public string MasterGridText { get { return string.Format(Loc("LOC_KX_MePx", "{0} px"), settings.MasterGrid); } }

        // greyed-out states for the set spots (only when the part itself is hidden)
        public bool HomePlacementEnabled { get { return true; } }

        private void NotifyMaster()
        {
            foreach (string n in new[] { "MasterSnap", "MasterGridText", "EditGridBrush", "HomePlacementEnabled",
                                         "DetPlacementEnabled", "DetMediaPlacementEnabled", "DetLogoPositionEnabled" })
                Notify(n);
            RefreshMasterItems();
        }

        /// <summary>Fine placement is always on now (it sits on top of the set spots).</summary>
        private bool MasterOn(string screen) { return true; }

        /// <summary>
        /// Settings from before 0.27: Master edit switched a screen's set spots off while it was on. Keep what you saw:
        /// a screen that had it on goes back to the default spots (so its fine placement lands in the same place),
        /// a screen that had it off drops the fine placement it wasn't using.
        /// </summary>
        private static void MergeEdits(HelperSettings s)
        {
            var blank = new HelperSettings();
            if (s.MasterOffsets == null) s.MasterOffsets = new Dictionary<string, double[]>();
            if (s.MasterHome)
            {
                s.CardOffset = blank.CardOffset;
                s.FilterPosition = blank.FilterPosition;
                s.HintsPosition = blank.HintsPosition;
                s.LogoPosition = blank.LogoPosition;
                s.InfoPosition = blank.InfoPosition;
            }
            else foreach (var k in s.MasterOffsets.Keys.Where(k => k.StartsWith("home.", StringComparison.Ordinal)).ToList()) s.MasterOffsets.Remove(k);
            if (s.MasterGame)
            {
                s.DetLogoPosition = blank.DetLogoPosition;
                s.DetMediaOffset = blank.DetMediaOffset;
                s.DetOffset = blank.DetOffset;
            }
            else foreach (var k in s.MasterOffsets.Keys.Where(k => k.StartsWith("game.", StringComparison.Ordinal)).ToList()) s.MasterOffsets.Remove(k);
            s.MasterHome = s.MasterGame = false;
            s.EditsMerged = true;
        }

        private double[] MasterOffset(string key)
        {
            double[] o;
            if (settings.MasterOffsets != null && settings.MasterOffsets.TryGetValue(key, out o) && o != null && o.Length == 2) return o;
            return new double[] { 0, 0 };
        }

        /// <summary>How far Master edit has moved an element down (0 when Master edit is off for its screen).</summary>
        private double MasterOffsetY(string key)
        {
            var el = MasterElements.FirstOrDefault(m => m.Key == key);
            return el != null && MasterOn(el.Screen) ? MasterOffset(key)[1] : 0;
        }

        private void SetMasterOffset(string key, double x, double y)
        {
            if (settings.MasterOffsets == null) settings.MasterOffsets = new Dictionary<string, double[]>();
            if (Math.Abs(x) < 0.5 && Math.Abs(y) < 0.5) settings.MasterOffsets.Remove(key);
            else settings.MasterOffsets[key] = new[] { Math.Round(x), Math.Round(y) };
        }

        private void RefreshMasterItems()
        {
            NotifyMoved();
        }

        // ---------------- applying the offsets ----------------

        private readonly Dictionary<FrameworkElement, TranslateTransform> masterTransforms = new Dictionary<FrameworkElement, TranslateTransform>();

        private void ApplyMasterOffsets()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null) return;
                HelperSettings.DropFixedGamePlaces(settings.MasterOffsets, settings.MasterSizes);   // (a built-in look or a saved one from before the game page's left column was one stack)
                var lower = DetLowerPushes(win);
                foreach (var el in MasterElements)
                {
                    if (MasterAway(el)) continue;
                    bool on = MasterOn(el.Screen);
                    var o = on ? MasterOffset(el.Key) : new double[] { 0, 0 };
                    double more;
                    if (on && lower.TryGetValue(el.Key, out more)) o = new[] { o[0], o[1] + more };
                    foreach (string name in el.Names)
                    {
                        var fe = MasterFind(win, name);
                        if (fe != null) SetTranslate(fe, o[0], o[1]);
                    }
                }
                ApplyMasterSizes();
                SyncFreeGaps();   // parts deleted in Free move keep their room
                ApplyMasterExtras();   // layers and opacity (Free move › Menu)
            }
            catch (Exception ex) { logger.Warn(ex, "Kippix: master edit offsets failed"); }
        }

        private class LowerPart { public string Key; public double X, Y, W, H, Gap; public bool AsItCame; }

        /// <summary>
        /// The game page's lower parts come placed by fixed moves (GameDefaultOffsets: Save backups under How long to beat, Your
        /// captures and More like this under those), measured at the Standard text size. With larger text (or a card with one
        /// more line) the part above is taller, and the one under it, moved by the same amount as ever, touched it or went over
        /// it. So a part that still has the move it came with goes further down when it would be closer to a part above it than
        /// that part's own gap (its bottom margin: the cards' 20). A part you placed yourself is left where you put it.
        /// Key -> how much further down (the settings keep the move as it was).
        /// </summary>
        private Dictionary<string, double> DetLowerPushes(Window win)
        {
            var push = new Dictionary<string, double>();
            try
            {
                if (!detailsOpen) return push;   // (only the game page's parts are pushed)
                var cards = MasterFind(win, "KX_SecCards");
                var page = cards != null ? VisualTreeHelper.GetParent(cards) as FrameworkElement : null;
                if (page == null || !page.IsVisible) return push;
                var parts = new List<LowerPart>();
                foreach (var el in MasterElements)
                {
                    if (!el.Key.StartsWith("game.sec.", StringComparison.Ordinal) || !MasterOn(el.Screen)) continue;
                    var fe = MasterFind(win, el.Names[0]);
                    if (fe == null || !fe.IsVisible || fe.ActualWidth < 1 || fe.ActualHeight < 1 || !page.IsAncestorOf(fe)) continue;
                    // where the page lays it out (without any transform: our move, the theme's slide-in)
                    double x = 0, y = 0;
                    for (DependencyObject v = fe; v != null && !ReferenceEquals(v, page); v = VisualTreeHelper.GetParent(v))
                    {
                        var at = VisualTreeHelper.GetOffset((Visual)v);
                        x += at.X;
                        y += at.Y;
                    }
                    var o = MasterOffset(el.Key);
                    double[] d;
                    bool asItCame = HelperSettings.GameDefaultOffsets.TryGetValue(el.Key, out d) && Math.Abs(d[0] - o[0]) < 0.5 && Math.Abs(d[1] - o[1]) < 0.5;
                    parts.Add(new LowerPart { Key = el.Key, X = x + o[0], Y = y + o[1], W = fe.ActualWidth, H = fe.ActualHeight, Gap = Math.Max(0, fe.Margin.Bottom), AsItCame = asItCame });
                }
                var placed = new List<LowerPart>();
                foreach (var b in parts.OrderBy(p => p.Y))
                {
                    if (b.AsItCame)
                    {
                        double need = 0;
                        foreach (var a in placed)
                        {
                            if (a.X + a.W <= b.X + 1 || b.X + b.W <= a.X + 1) continue;   // (beside it, not above it)
                            need = Math.Max(need, a.Y + a.H + a.Gap - b.Y);
                        }
                        if (need > 0.5) { b.Y += need; push[b.Key] = Math.Round(need); }
                    }
                    placed.Add(b);
                }
            }
            catch (Exception) { }
            return push;
        }

        // (1.0.0) The parts' places are put on every 2 seconds: each part was looked for through the whole window each time
        // (~50 ms on a 4K TV, a stutter every 2 s, worse on a slower PC). Found parts are kept while they're still on screen;
        // one that isn't there is looked for again only when the screen changes (or after a few seconds).
        // The ones that aren't there are looked for together, in one walk of the window (MasterSweep), not one walk each.
        private readonly Dictionary<string, FrameworkElement> masterFound = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, DateTime> masterMissed = new Dictionary<string, DateTime>();
        private readonly HashSet<string> masterWanted = new HashSet<string>();   // every name asked for (the walk looks for all of them)
        private HashSet<string> masterSweptNow;   // what the last walk didn't find, until the piece of work it was part of is done
        private string masterFindScreen;
        private DateTime masterFindScreenAt = DateTime.MinValue;

        /// <summary>A game page part while the game page is closed: it can't be seen, and it's placed as the page opens (DetailsOpened).</summary>
        private bool MasterAway(MasterElement el) { return el.Screen == "game" && !detailsOpen; }

        private FrameworkElement MasterFind(Window win, string name)
        {
            string screen = (openPanel ?? "") + "|" + detailsOpen + "|" + LayoutNow + "|" + freeMove;
            if (screen != masterFindScreen) { masterFindScreen = screen; masterFindScreenAt = DateTime.Now; masterMissed.Clear(); }
            FrameworkElement fe;
            if (masterFound.TryGetValue(name, out fe) && fe != null)
            {
                try { if (fe.Name == name && PresentationSource.FromVisual(fe) != null && win.IsAncestorOf(fe)) return fe; } catch (Exception) { }
                masterFound.Remove(name);
            }
            DateTime missed;
            if (masterMissed.TryGetValue(name, out missed) && (DateTime.Now - missed).TotalSeconds < 6) return null;
            if (masterSweptNow != null && masterSweptNow.Contains(name)) return null;   // (not there a moment ago, in this same piece of work)
            masterWanted.Add(name);
            MasterSweep(win);
            masterFound.TryGetValue(name, out fe);
            return fe;
        }

        /// <summary>One walk through the window for every name asked for (the first of each, as UiTools.FindByName finds it).</summary>
        private void MasterSweep(Window win)
        {
            var found = new Dictionary<string, FrameworkElement>();
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(win);
            while (queue.Count > 0 && found.Count < masterWanted.Count)
            {
                var d = queue.Dequeue();
                var fe = d as FrameworkElement;
                string n = fe != null ? fe.Name : null;
                if (!string.IsNullOrEmpty(n) && masterWanted.Contains(n) && !found.ContainsKey(n)) found[n] = fe;
                int count = 0;
                try { count = VisualTreeHelper.GetChildrenCount(d); } catch (Exception) { }
                for (int i = 0; i < count; i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
            bool settled = (DateTime.Now - masterFindScreenAt).TotalSeconds > 5;   // (a screen just opened may still be drawing its parts)
            var missing = new HashSet<string>();
            foreach (string n in masterWanted)
            {
                FrameworkElement fe;
                if (found.TryGetValue(n, out fe)) { masterFound[n] = fe; masterMissed.Remove(n); continue; }
                masterFound.Remove(n);
                missing.Add(n);
                if (settled) masterMissed[n] = DateTime.Now;
            }
            // (the rest of this piece of work doesn't walk again for them; the next one does, while the screen is new)
            if (ui == null) return;
            masterSweptNow = missing;
            ui.BeginInvoke(new Action(() => masterSweptNow = null), DispatcherPriority.Send);
        }

        /// <summary>Our own translate on the element, added beside any transform it already has (that one may be animated by the theme).</summary>
        private void SetTranslate(FrameworkElement fe, double x, double y)
        {
            TranslateTransform t;
            masterTransforms.TryGetValue(fe, out t);
            bool attached = t != null && (ReferenceEquals(fe.RenderTransform, t)
                            || (fe.RenderTransform is TransformGroup && GroupHas((TransformGroup)fe.RenderTransform, t)));   // (it may sit inside our scale's group)
            if (!attached)
            {
                if (Math.Abs(x) < 0.01 && Math.Abs(y) < 0.01) return;   // nothing to move, leave the element alone
                t = new TranslateTransform();
                var current = fe.RenderTransform;
                if (current == null || ReferenceEquals(current, Transform.Identity) || current.IsFrozen && current.Value.IsIdentity)
                    fe.RenderTransform = t;
                else
                {
                    var group = new TransformGroup();
                    group.Children.Add(current);
                    group.Children.Add(t);
                    fe.RenderTransform = group;
                }
                masterTransforms[fe] = t;
            }
            if (Math.Abs(t.X - x) > 0.01) t.X = x;
            if (Math.Abs(t.Y - y) > 0.01) t.Y = y;
        }

        // ---------------- moving one element by hand ----------------

        private MasterElement masterMoving;
        private double moveStartX, moveStartY, moveStartRot;
        private int masterStep = 1;
        private string masterReturnPage;
        private bool masterOpenedDetails;
        private bool masterKeysHooked;

        private IInputElement masterReturnFocus;

        public bool IsMasterMoving { get { return masterMoving != null || widgetMoving != null || freeMove; } }
        public string MasterMoveName
        {
            get
            {
                if (widgetMoving != null) return widgetMoving.Name;
                if (masterMoving != null) return Loc(masterMoving.LocKey, masterMoving.Fallback);
                if (!freeMove) return "";
                if (freePrecise) return Loc("LOC_KX_PreciseEdit", "Precise edit");
                return GameDetailsVisible() ? Loc("LOC_KX_EditingGamePage", "Editing the game page") : Loc("LOC_KX_EditingHome", "Editing the home screen");
            }
        }
        /// <summary>What LB / RB do: the step size (or grid) for parts of the screen, the size for widgets.</summary>
        public string MasterStepLabel { get { return widgetMoving != null ? Loc("LOC_KX_MeSize", "Size") : Loc("LOC_KX_MeStep", "Step"); } }
        public string MasterMoveText
        {
            get
            {
                if (PickModeActive) return PickModeText();   // Menu: Layer / Opacity
                if (widgetMoving != null) return WidgetMoveText();
                if (masterMoving == null)
                {
                    if (freeMove && sizeRefusedFor != null && sizeRefusedFor == freeTarget)
                        return sizeRefusedFor == "home.cards" ? Loc("LOC_KX_SizeListOnly", "The games can be sized in list mode") : Loc("LOC_KX_SizeNot", "This one can't be sized (only moved)");
                    return freeMove ? FreeMoveText() : "";
                }
                if (sizing) return SizeText() + TurnSuffix(MasterRotation(masterMoving.Key));
                var o = MasterOffset(masterMoving.Key);
                string step = settings.MasterSnap ? string.Format(Loc("LOC_KX_MeGridStep", "grid {0} px"), settings.MasterGrid)
                                                  : string.Format(Loc("LOC_KX_MeFreeStep", "free, {0} px steps"), MasterSteps[masterStep]);
                return string.Format(Loc("LOC_KX_MeOffset", "x {0:+0;-0;0}, y {1:+0;-0;0}"), o[0], o[1]) + "   ·   " + step + TurnSuffix(MasterRotation(masterMoving.Key));
            }
        }

        private double[] moveStartSize;
        private double moveStartTileH, moveStartTileW;
        private int moveStartCardSize, moveStartGridCols;

        /// <summary>A move cancelled: a size Y took back (or sizing changed) comes back too. Only when it did change.</summary>
        private void RestoreMoveStartSize(MasterElement el)
        {
            if (moveStartSize == null || el == null || !CanSize(el.Key)) return;
            var now = MasterSize(el.Key);
            bool sizeChanged = false;
            for (int i = 0; i < 4; i++) if (Math.Abs(now[i] - moveStartSize[i]) > 0.01) sizeChanged = true;
            bool list = el.Key == "home.cards";
            bool tileChanged = list && (settings.ListTileH != moveStartTileH || settings.ListTileW != moveStartTileW);
            if (list && (settings.CardSize != moveStartCardSize || settings.GridColumns != moveStartGridCols))
            {
                settings.CardSize = moveStartCardSize;
                settings.GridColumns = moveStartGridCols;
                NotifyCardSettings();
                ApplyCardLayoutSoon();
                HomeModeChanged();
            }
            if (!sizeChanged && !tileChanged) return;
            StoreMasterSize(el.Key, moveStartSize);
            if (list) { settings.ListTileH = moveStartTileH; settings.ListTileW = moveStartTileW; UpdateSliders(); HomeModeChanged(); }
            else ApplyMasterSizes();
        }

        private void StartMasterMove(MasterElement el)
        {
            EditBegin();   // (undo / redo: as it is before it's picked up)
            if (!freeMove)
            {
                masterReturnPage = optionsPage;
                masterReturnFocus = Keyboard.FocusedElement;
            }
            masterOpenedDetails = false;
            sizing = false;
            ModeChanged();
            if (el.Screen == "game" && !GameDetailsVisible()) masterOpenedDetails = EnsureGameDetailsOpen();
            var o = MasterOffset(el.Key);
            moveStartX = o[0];
            moveStartY = o[1];
            moveStartRot = MasterRotation(el.Key);
            moveStartSize = MasterSize(el.Key);
            moveStartTileH = settings.ListTileH;
            moveStartTileW = settings.ListTileW;
            moveStartCardSize = settings.CardSize;
            moveStartGridCols = settings.GridColumns;
            masterMoving = el;
            // the options step aside while you move it; "master" counts as an open panel so nothing else reacts to the keys
            openPanel = "master";
            NotifyPanels();
            NotifyMove();
            UpdateTopBarFade();
            ui.BeginInvoke(new Action(() =>
            {
                if (masterMoving != el) return;
                ApplyMasterOffsets();
                UpdateMasterOutline();
                if (!TakeMasterKeys()) EndMasterMove(false);   // couldn't take the keys: don't leave you stuck
            }), DispatcherPriority.Loaded);
        }

        private void NotifyMove()
        {
            UpdateDetBlankMarks();   // (0.99.51)
            Notify("IsMasterMoving");
            Notify("MasterMoveName");
            Notify("MasterMoveText");
            Notify("MasterStepLabel");
            Notify("IsFreeCursor");
            Notify("FreeCursorShown");
            Notify("FreeGridShown");
            Notify("IsPlacing");
            Notify("IsSizing");
            Notify("IsMovingNotSizing");
            Notify("PickedCanDelete");
            Notify("FreeCanDelete");
            UpdateRotTimer();   // the triggers turn what's picked up (read only while something is)
        }

        /// <summary>Keys while moving: D-pad / arrows move, LB / RB change the step (or grid), Y resets, X switches grid / free, A keeps, B cancels, View deletes (what can be).</summary>
        private void OnMasterKeyDown(object sender, KeyEventArgs e)
        {
            if (masterMoving == null && widgetMoving == null && !freeMove) return;
            lastPanelKey = e;   // the window's own handler sees this event afterwards (handledEventsToo): don't let it act on it again
            string button = null;
            bool pressed = true;
            if (e.GetType().Name == "GameControllerInputEventArgs")
            {
                PropertyInfo b = e.GetType().GetProperty("Button"), st = e.GetType().GetProperty("ButtonState");
                object bv = b != null ? b.GetValue(e, null) : null, sv = st != null ? st.GetValue(e, null) : null;
                button = bv != null ? bv.ToString() : "";
                pressed = sv == null || sv.ToString() == "Pressed";
            }
            e.Handled = true;   // nothing else gets keys while moving
            if (button == "TriggerLeft" || button == "TriggerRight") { NoteTrigger(); return; }   // (turning reads the triggers itself)
            if (button == "Start" && !pressed) startLatched = false;   // (0.99.50)
            if (!pressed) return;
            // Playnite also sends the triggers as PgUp / PgDn: while something is picked up those are the turn, not the step size
            if (button == null && (e.Key == Key.PageUp || e.Key == Key.PageDown) && !IsFreeCursor && RotateActive && TriggerRecent()) return;
            if (IsFreeCursor) { FreeMoveKey(button, e.Key, button == null && e.IsRepeat); return; }
            if (PickExtrasKey(button, e.Key)) return;   // Menu: Move / Layer / Opacity (and the D-pad while Layer / Opacity is on)
            // Q / E: turn it (the keyboard's LT / RT)
            if (button == null && (e.Key == Key.Q || e.Key == Key.E)) { RotateTap(e.Key == Key.E ? 1 : -1); return; }
            // the press that started this move (seen again as Enter / A): not the one that ends it
            bool ending = button == "A" || button == "B" || (button == null && (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Escape || e.Key == Key.Back));
            if (ending && (JustChanged || (button == null && e.IsRepeat))) return;
            // View (Delete on the keyboard): delete what's picked up, when it can be (Delete on something that can't: puts it back, as before)
            if ((button == "Back" || (button == null && e.Key == Key.Delete && !e.IsRepeat)) && PickedCanDelete) { if (!JustChanged) PickedDelete(); return; }
            if (widgetMoving != null) { WidgetMoveKey(button, e.Key); return; }
            if (sizing) { SizeKey(button, e.Key); return; }

            int dx = 0, dy = 0;
            if (button == null)
            {
                switch (e.Key)
                {
                    case Key.Left: dx = -1; break;
                    case Key.Right: dx = 1; break;
                    case Key.Up: dy = -1; break;
                    case Key.Down: dy = 1; break;
                    case Key.Enter: case Key.Space: EndMasterMove(true); return;
                    case Key.Escape: case Key.Back: EndMasterMove(false); return;
                    case Key.R: case Key.Delete: MasterNudge(0, 0, true); return;
                    case Key.G: MasterSnap = !settings.MasterSnap; NotifyMove(); return;
                    case Key.PageUp: MasterStepSize(-1); return;
                    case Key.PageDown: MasterStepSize(1); return;
                    default: return;
                }
            }
            else
            {
                switch (button)
                {
                    case "A": EndMasterMove(true); return;
                    case "B": EndMasterMove(false); return;
                    case "Y": if (!JustChanged) MasterNudge(0, 0, true); return;
                    case "X": MasterSnap = !settings.MasterSnap; NotifyMove(); return;
                    case "LeftShoulder": MasterStepSize(-1); return;
                    case "RightShoulder": MasterStepSize(1); return;
                    default: return;   // the D-pad also arrives as arrow keys, handled above
                }
            }
            MasterNudge(dx, dy, false);
        }

        private void MasterStepSize(int dir)
        {
            if (settings.MasterSnap)
            {
                int i = Math.Max(0, Math.Min(MasterGrids.Length - 1, Array.IndexOf(MasterGrids, settings.MasterGrid) + dir));
                settings.MasterGrid = MasterGrids[i];
                SaveSettings();
                Notify("MasterGridText");
                Notify("EditGridBrush");
            }
            else masterStep = Math.Max(0, Math.Min(MasterSteps.Length - 1, masterStep + dir));
            Notify("MasterMoveText");
        }

        private void MasterNudge(int dx, int dy, bool reset)
        {
            var el = masterMoving;
            if (el == null) return;
            var o = MasterOffset(el.Key);
            double x = o[0], y = o[1];
            if (reset)
            {
                // Y while it's picked up: back to where AND how big it was to begin with (and straight)
                x = 0; y = 0;
                SetMasterRotation(el.Key, 0);
                rotRaw = 0;
                if (CanSize(el.Key))
                {
                    if (settings.MasterSizes != null) settings.MasterSizes.Remove(el.Key);
                    if (el.Key == "home.cards" && LayoutNow != 2) ResetCardsSize();
                    else if (el.Key == "home.cards") { settings.ListTileH = 0; settings.ListTileW = 0; UpdateSliders(); HomeModeChanged(); }
                    else ApplyMasterSizes();
                }
                PlaySound("move");
            }
            else if (settings.MasterSnap)
            {
                double g = settings.MasterGrid;
                // first press lines it up with the grid, then it jumps a whole square
                x = dx == 0 ? x : (dx > 0 ? Math.Floor(x / g) * g + g : Math.Ceiling(x / g) * g - g);
                y = dy == 0 ? y : (dy > 0 ? Math.Floor(y / g) * g + g : Math.Ceiling(y / g) * g - g);
            }
            else
            {
                x += dx * MasterSteps[masterStep];
                y += dy * MasterSteps[masterStep];
            }
            x = Math.Max(-1900, Math.Min(1900, x));
            y = Math.Max(-1060, Math.Min(1060, y));
            SetMasterOffset(el.Key, x, y);
            ApplyMasterOffsets();
            Notify("MasterMoveText");
            ui.BeginInvoke(new Action(UpdateMasterOutline), DispatcherPriority.Render);
            if (el.Key == "home.cards") ApplyHomeLayoutSoon();
        }

        private void EndMasterMove(bool keep)
        {
            var el = masterMoving;
            if (el == null) return;
            sizing = false;
            if (!keep)
            {
                SetMasterOffset(el.Key, moveStartX, moveStartY);
                SetMasterRotation(el.Key, moveStartRot);
                RestoreMoveStartSize(el);
            }
            EndPickExtras(keep);   // (B: its layer and opacity back too)
            SaveSettings();
            masterMoving = null;
            EditCheckpointAfterPick();   // put down: an undo step
            StopDrag();
            ApplyMasterOffsets();
            RefreshMasterItems();
            if (el.Key == "home.cards") ApplyHomeLayoutSoon();
            if (freeMove) { BackToFreeCursor(); return; }
            ModeChanged();
            NotifyMove();
            if (masterOpenedDetails) CloseGameDetails(false);
            masterOpenedDetails = false;
            // back to the options, on the row you were moving
            openPanel = "options";
            NotifyPanels();
            SetOptionsPage(masterReturnPage ?? (detailsOpen ? "gamepage" : "home"), false);
            UpdateTopBarFade();
            string key = el.Key;
            var back = masterReturnFocus as UIElement;
            masterReturnFocus = null;
            ui.BeginInvoke(new Action(() =>
            {
                // the row you started from
                if (back != null && back.IsVisible && ReferenceEquals(Keyboard.Focus(back), back)) return;
                var panel = UiTools.FindByName(Application.Current.MainWindow, "KX_OptionsPanel");
                if (panel != null && !panel.IsKeyboardFocusWithin) UiTools.FocusInside("KX_OptionsPanel");
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>Puts the dashed box around the element being moved (all of its visible parts; turned with it when it's one part).</summary>
        private void UpdateMasterOutline()
        {
            try
            {
                var win = Application.Current.MainWindow;
                var canvas = UiTools.FindByName(win, "KX_MasterCanvas");
                var outline = UiTools.FindByName(win, "KX_MasterOutline");
                if ((masterMoving == null && widgetMoving == null) || canvas == null || outline == null) { ShowCentreGuides(canvas as FrameworkElement, null); return; }
                outline.Opacity = 1;
                var frame = widgetMoving != null ? WidgetFrame(widgetMoving, canvas) : MasterFrame(masterMoving, canvas);
                if (frame == null)
                {
                    // the part isn't showing (turned off, or not laid out yet): keep the outline focusable, just invisible
                    outline.Opacity = 0;
                    ShowCentreGuides(canvas as FrameworkElement, null);
                    return;
                }
                outline.Opacity = 1;
                if (widgetMoving == null && masterMoving != null && masterMoving.Key == "home.logo" && !sizing) frame = LogoLineFrame(frame);   // (0.88)
                PlaceOutline(outline, frame);
                ShowCentreGuides(canvas as FrameworkElement, frame);
                UpdateDetBlankMarks();
            }
            catch (Exception) { }
        }

        private const double GuideNear = 60;   // (0.90) how close the middle of what's moving has to be for a centre line to show

        /// <summary>(0.90) The screen's centre lines (up the middle, and across it) show only while what's moving is near one,
        /// brighter when it's right on it.</summary>
        private void ShowCentreGuides(FrameworkElement canvas, TurnFrame frame)
        {
            if (canvas == null) return;
            var v = UiTools.FindByName(canvas, "KX_GuideV") as FrameworkElement;
            var h = UiTools.FindByName(canvas, "KX_GuideH") as FrameworkElement;
            if (v == null || h == null) return;
            double cw = canvas.ActualWidth, ch = canvas.ActualHeight;
            bool moving = frame != null && !sizing && cw > 0 && ch > 0;
            double dx = moving ? Math.Abs(frame.C.X - cw / 2) : double.MaxValue;
            double dy = moving ? Math.Abs(frame.C.Y - ch / 2) : double.MaxValue;
            PlaceGuide(v, dx < GuideNear, dx < 1.5, cw / 2 - 1, 0, 2, ch);
            PlaceGuide(h, dy < GuideNear, dy < 1.5, 0, ch / 2 - 1, cw, 2);
            try { ShowEdgeGuides(canvas, moving ? frame : null); } catch (Exception) { }
        }

        // ---------------- (0.96) lining up with the other parts ----------------

        private const double EdgeNear = 6;   // how close an edge / middle has to be to another part's to show a line
        private readonly List<System.Windows.Shapes.Rectangle> edgeGuides = new List<System.Windows.Shapes.Rectangle>();
        private List<Rect> edgeOthers;
        private string edgeOthersFor;
        private DateTime edgeOthersAt;

        /// <summary>While something is moved: a line where its left / middle / right (or top / middle / bottom) lines up with another
        /// part's on the screen, from one to the other.</summary>
        private void ShowEdgeGuides(FrameworkElement canvas, TurnFrame frame)
        {
            var host = canvas as Panel;
            var lines = new List<Rect>();
            if (frame != null && host != null)
            {
                string me = masterMoving != null ? masterMoving.Key : widgetMoving != null ? "w:" + widgetMoving.Config.Id : null;
                if (me != edgeOthersFor || edgeOthers == null || (DateTime.Now - edgeOthersAt).TotalSeconds > 2)
                {
                    // the others' places (they don't move while this one does): found once, not on every step
                    edgeOthersFor = me;
                    edgeOthersAt = DateTime.Now;
                    edgeOthers = FreeTargets().Where(t => t.Item1 != me && t.Item1 != "board").Select(t => t.Item3.Bounds()).Where(r => !r.IsEmpty && r.Width > 4 && r.Height > 4).ToList();
                }
                Rect b = frame.Bounds();
                double[] mx = { b.Left, b.Left + b.Width / 2, b.Right }, my = { b.Top, b.Top + b.Height / 2, b.Bottom };
                double bestX = EdgeNear, bestY = EdgeNear;
                Rect? lx = null, ly = null;
                foreach (var o in edgeOthers)
                {
                    double[] ox = { o.Left, o.Left + o.Width / 2, o.Right }, oy = { o.Top, o.Top + o.Height / 2, o.Bottom };
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            if ((i == 1) != (j == 1)) continue;   // middles with middles, edges with edges
                            double d = Math.Abs(mx[i] - ox[j]);
                            if (d < bestX)
                            {
                                bestX = d;
                                double top = Math.Min(b.Top, o.Top), bottom = Math.Max(b.Bottom, o.Bottom);
                                lx = new Rect(ox[j] - 1, top, 2, bottom - top);
                            }
                            d = Math.Abs(my[i] - oy[j]);
                            if (d < bestY)
                            {
                                bestY = d;
                                double left = Math.Min(b.Left, o.Left), right = Math.Max(b.Right, o.Right);
                                ly = new Rect(left, oy[j] - 1, right - left, 2);
                            }
                        }
                }
                if (lx.HasValue) lines.Add(lx.Value);
                if (ly.HasValue) lines.Add(ly.Value);
            }
            else edgeOthers = null;
            if (host == null) return;
            while (edgeGuides.Count < lines.Count)
            {
                var r = new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x4F, 0xD8)), IsHitTestVisible = false, Opacity = 0.9 };
                Panel.SetZIndex(r, 1000);
                host.Children.Add(r);
                edgeGuides.Add(r);
            }
            for (int i = 0; i < edgeGuides.Count; i++)
            {
                var g = edgeGuides[i];
                if (i >= lines.Count) { g.Visibility = Visibility.Collapsed; continue; }
                var l = lines[i];
                g.Visibility = Visibility.Visible;
                Canvas.SetLeft(g, l.X);
                Canvas.SetTop(g, l.Y);
                g.Width = Math.Max(2, l.Width);
                g.Height = Math.Max(2, l.Height);
            }
        }

        private static void PlaceGuide(FrameworkElement g, bool show, bool on, double x, double y, double w, double hgt)
        {
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            if (g.Visibility != vis) g.Visibility = vis;
            if (!show) return;
            Canvas.SetLeft(g, x);
            Canvas.SetTop(g, y);
            g.Width = w;
            g.Height = hgt;
            g.Opacity = on ? 1 : 0.55;
        }
    }
}
