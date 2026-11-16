using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>One week's bar in the game page's play history.</summary>
    public class HistoryBar
    {
        public double Height { get; set; }
        public string Label { get; set; }
        public string Tip { get; set; }
        public bool IsThisWeek { get; set; }
    }

    /// <summary>
    /// Further down the game page (Down from the buttons): play history, HowLongToBeat, your captures, achievement
    /// highlights, status and rating, notes, scores and more like this. Plus a DLC note under the buttons and the More menu.
    /// </summary>
    public partial class ThemeApi
    {
        public ObservableCollection<HistoryBar> DetHistory { get; private set; }
        public ObservableCollection<GalleryItem> DetCaptures { get; private set; }
        public ObservableCollection<RowTile> DetSimilar { get; private set; }
        public ObservableCollection<PickRow> GameMoreRows { get; private set; }
        public ObservableCollection<PickRow> GameStatusRows { get; private set; }

        private Guid detGameId;
        private int detMoreGen;
        private bool detMoreDown;
        private Guid detOriginId;   // set when a More like this game was opened: the game page closes back to this one

        private void InitGamePageMore()
        {
            DetHistory = new ObservableCollection<HistoryBar>();
            DetCaptures = new ObservableCollection<GalleryItem>();
            DetSimilar = new ObservableCollection<RowTile>();
            GameMoreRows = new ObservableCollection<PickRow>();
            CollectionRows = new ObservableCollection<PickRow>();
            GameStatusRows = new ObservableCollection<PickRow>();
            InitProfile();
            OpenGameMoreCommand = new RelayCommand(_ => OpenGamePanel("gamemore"));
            OpenGameStatusCommand = new RelayCommand(_ => OpenGamePanel("gamestatus"));
            EditNotesCommand = new RelayCommand(_ => EditNotes());
            OpenDetCapturesCommand = new RelayCommand(_ => { var g = SelectedGameNow(); if (g != null) OpenGalleryForGame(g.Id, g.Name, null); });
            DlcOpenSteamCommand = new RelayCommand(_ => { var g = SelectedGameNow(); if (g != null && g.PluginId == SteamPluginId) { try { Process.Start("steam://nav/games/details/" + g.GameId); } catch (Exception) { } } });
        }

        public ICommand OpenGameMoreCommand { get; private set; }
        public ICommand OpenGameStatusCommand { get; private set; }
        public ICommand EditNotesCommand { get; private set; }
        public ICommand OpenDetCapturesCommand { get; private set; }
        public ICommand DlcOpenSteamCommand { get; private set; }

        // ---------------- settings: what's further down ----------------

        public bool IsOptionsGameSections { get { return optionsPage == "gamesections"; } }
        public bool IsOptionsGameMore { get { return optionsPage == "gamemore"; } }
        public bool IsOptionsGameStatus { get { return optionsPage == "gamestatus"; } }
        public bool DetMoreSections
        {
            get { return settings.DetMoreSections; }
            set
            {
                if (!SetSetting(ref settings.DetMoreSections, value)) return;
                NotifyDetMore();
                if (optionsPage == "gamesections") BuildGameSectionRows();   // the LOWER PAGE list comes and goes with it
            }
        }
        public bool DetSecHistory { get { return settings.DetSecHistory; } set { if (SetSetting(ref settings.DetSecHistory, value)) NotifyDetMore(); } }
        public bool DetSecHltb { get { return settings.DetSecHltb; } set { if (SetSetting(ref settings.DetSecHltb, value)) NotifyDetMore(); } }
        public bool DetSecCaptures { get { return settings.DetSecCaptures; } set { if (SetSetting(ref settings.DetSecCaptures, value)) NotifyDetMore(); } }
        public bool DetSecAch { get { return settings.DetSecAch; } set { if (SetSetting(ref settings.DetSecAch, value)) NotifyDetMore(); } }
        public bool DetSecStatus { get { return settings.DetSecStatus; } set { if (SetSetting(ref settings.DetSecStatus, value)) NotifyDetMore(); } }
        public bool DetSecNotes { get { return settings.DetSecNotes; } set { if (SetSetting(ref settings.DetSecNotes, value)) NotifyDetMore(); } }
        public bool DetSecScores { get { return settings.DetSecScores; } set { if (SetSetting(ref settings.DetSecScores, value)) NotifyDetMore(); } }
        public bool DetSecSimilar { get { return settings.DetSecSimilar; } set { if (SetSetting(ref settings.DetSecSimilar, value)) NotifyDetMore(); } }
        public bool DetShowMoreButton { get { return settings.DetShowMoreButton; } set { if (SetSetting(ref settings.DetShowMoreButton, value)) NotifyDetMore(); } }
        public bool DetShowDlcWarning { get { return settings.DetShowDlcWarning; } set { if (SetSetting(ref settings.DetShowDlcWarning, value)) NotifyDetMore(); } }

        // what each section shows (false when it's off or has nothing)
        public bool ShowDetMore { get { return settings.DetMoreSections && DetAllKeys.Any(k => DetIsLower(k) && DetPartShowing(k)); } }
        public bool ShowDetHistory { get { return settings.DetSecHistory && DetHistory.Count > 0; } }
        public bool ShowDetHltb { get { return settings.DetSecHltb && !string.IsNullOrEmpty(DetHltbMain); } }
        public bool ShowDetCaptures { get { return settings.DetSecCaptures && DetCaptures.Count > 0; } }
        public bool ShowDetAch { get { return settings.DetSecAch && !string.IsNullOrEmpty(DetAchSummary); } }
        public bool ShowDetStatus { get { return settings.DetSecStatus; } }
        public bool ShowDetNotesCard { get { return settings.DetSecNotes; } }
        public bool ShowDetScores { get { return settings.DetSecScores; } }
        public bool ShowDetSimilar { get { return settings.DetSecSimilar && DetSimilar.Count > 0; } }
        public bool ShowDetDlc { get { return settings.DetShowDlcWarning && !string.IsNullOrEmpty(DetDlcText); } }

        public string DetHistoryTotal { get; private set; }
        public string DetHistoryLine { get; private set; }
        public string DetHltbMain { get; private set; }
        public string DetHltbExtras { get; private set; }
        public string DetHltbComplete { get; private set; }
        public string DetHltbYou { get; private set; }
        public double DetHltbFill { get; private set; }   // 0..480
        public string DetCapturesText { get; private set; }
        public string DetAchSummary { get; private set; }
        public double DetAchFill { get; private set; }    // 0..480
        public string DetAchRarest { get; private set; }
        public string DetAchRarestSub { get; private set; }
        public bool HasDetAchRarest { get { return !string.IsNullOrEmpty(DetAchRarest); } }
        public string DetAchNext { get; private set; }
        public string DetAchNextSub { get; private set; }
        public bool HasDetAchNext { get { return !string.IsNullOrEmpty(DetAchNext); } }
        public ImageSource DetAchRarestIcon { get; private set; }
        public ImageSource DetAchNextIcon { get; private set; }
        public string DetStatusText { get; private set; }
        public string DetStarsText { get; private set; }
        public string DetNotesText { get; private set; }
        public bool HasDetNotes { get { return !string.IsNullOrEmpty(DetNotesText); } }
        public string DetDlcText { get; private set; }
        public bool DetDlcIsSteam { get; private set; }
        public string GameMoreTitle { get; private set; }

        private void NotifyDetMore()
        {
            foreach (string n in new[] { "DetMoreSections", "DetSecHistory", "DetSecHltb", "DetSecCaptures", "DetSecAch", "DetSecStatus", "DetSecNotes", "DetSecScores", "DetSecSimilar",
                                         "DetShowMoreButton", "DetShowDlcWarning", "ShowDetMore", "ShowDetHistory", "ShowDetHltb", "ShowDetCaptures", "ShowDetAch", "ShowDetStatus",
                                         "ShowDetNotesCard", "ShowDetScores", "ShowDetSimilar", "ShowDetDlc", "DetHistoryTotal", "DetHistoryLine", "DetHltbMain", "DetHltbExtras",
                                         "DetHltbComplete", "DetHltbYou", "DetHltbFill", "DetCapturesText", "DetAchSummary", "DetAchFill", "DetAchRarest", "DetAchRarestSub",
                                         "HasDetAchRarest", "DetAchNext", "DetAchNextSub", "HasDetAchNext", "DetAchRarestIcon", "DetAchNextIcon", "DetStatusText", "DetStarsText",
                                         "DetNotesText", "HasDetNotes", "DetDlcText", "DetDlcIsSteam",
                                         "DetHintMoreBelow" })   // (the hint too: a lower page with only More like this on it is found after the page opens)
                Notify(n);
            NotifyDetInfo();   // the info's How long to beat and size lines follow the same data
            SyncFreeGaps();    // a card deleted in Free move keeps its room for the games it would show on
            QueueDetOffsets(); // (0.99.63) and a card that's just come in stands where you moved it straight away
        }

        private bool detOffsetsQueued;
        private void QueueDetOffsets()
        {
            if (detOffsetsQueued || ui == null || !detailsOpen) return;
            detOffsetsQueued = true;
            ui.BeginInvoke(new Action(() => { detOffsetsQueued = false; try { if (detailsOpen) ApplyMasterOffsets(); } catch (Exception) { } }), DispatcherPriority.Loaded);
        }

        // ---------------- More like this: its covers' size, and as many as fit ----------------

        private readonly List<RowTile> detSimilarAll = new List<RowTile>();   // every game found; DetSimilar is the ones that fit across
        private const double SimilarDefaultH = 225, SimilarMinH = 120, SimilarMaxH = 480, SimilarStepH = 15;
        private double similarShownH = SimilarDefaultH;

        /// <summary>
        /// The covers' height (Free move › Y on More like this; MasterSizes [0, height, moved x, moved y]). A size kept from 0.74,
        /// when the part was sized like a widget (its width first), isn't a cover size: the covers stay as they come.
        /// </summary>
        private double SimilarTileH()
        {
            var s = MasterSize(SimilarKey);
            if (s[0] > 0 || s[1] <= 0) return SimilarDefaultH;
            return Math.Max(SimilarMinH, Math.Min(SimilarMaxH, s[1]));
        }

        public double DetSimilarTileH { get { return SimilarTileH(); } }
        public double DetSimilarTileW { get { return SimilarWidthFor(SimilarShape, SimilarTileH()); } }

        // ---------------- More like this: its covers' shape (the same shapes as the home screen's cards) ----------------
        //
        // Tall cover, Square, Wide, Circle, Hexagon, Triangle (taking turns pointing up and down, so they fit together). The
        // cover size (Free move › Y, or the Cover size row) is the covers' height; the shape gives the width. Wide and the
        // triangles show the game's wide picture (as the home cards do), the others its cover. Free move › Y › X, or the part's
        // Cover shape row, changes it. Kept with the game page's layout (DetSimilarShape).

        private const int SimilarShapeCount = 6;   // (0..5: one Triangle, which takes turns)
        private int similarShownShape = -1;

        private int SimilarShape
        {
            get { int s = settings.DetSimilarShape; return s < 0 || s >= SimilarShapeCount ? CardShapes.Tall : s; }
        }

        /// <summary>A cover's width for the shape at a height (Playnite's own cover shape: 2 : 3).</summary>
        private static double SimilarWidthFor(int shape, double h)
        {
            int aw, ah;
            CardShapes.Aspect(shape, out aw, out ah);
            if (aw <= 0 || ah <= 0) return Math.Round(h * 2 / 3);
            return Math.Round(h * aw / ah);
        }

        /// <summary>From one cover to the next: a cover and a gap; triangles half a triangle on (they fit together).</summary>
        private double SimilarStep
        {
            get
            {
                double w = DetSimilarTileW;
                return CardShapes.IsTriangle(SimilarShape) ? Math.Round(w * 0.5 + 10) : w + 14;
            }
        }

        private string SimilarShapeText()
        {
            return CardShapes.IsTriangle(SimilarShape) ? Loc("LOC_KX_ShapeTriangle", "Triangle") : ShapeName(SimilarShape);
        }

        /// <summary>One cover: its size, outline, clip and the room after it, for the shape (place: its place in the row).</summary>
        private void ShapeSimilarTile(RowTile t, int place)
        {
            int s = SimilarShape;
            if (CardShapes.IsTriangle(s)) s = CardShapes.Alternate(CardShapes.TriUp, place, true);
            double h = SimilarTileH(), w = DetSimilarTileW;
            t.Width = w;
            t.Height = h;
            t.Shape = s;
            bool cut = CardShapes.IsCut(s);
            t.IsCut = cut;
            t.Clip = cut ? CardShapes.Sized(s, w, h) : CardShapes.Rounded(w, h, 10);
            t.Outline = cut ? CardShapes.Unit(s) : null;
            t.TileMargin = new Thickness(0, 0, SimilarStep - w, 0);
        }

        private void ShapeSimilarTiles()
        {
            int i = 0;
            foreach (var t in detSimilarAll) ShapeSimilarTile(t, i++);
        }

        /// <summary>The covers' pictures for the shape: the wide picture for Wide and the triangles (the cover when there's none), else the cover.</summary>
        private int similarArtGen;
        private void LoadSimilarArt()
        {
            int gen = ++similarArtGen;
            int shape = SimilarShape;
            bool wide = shape == CardShapes.Wide || CardShapes.IsTriangle(shape);
            int decode = (int)Math.Max(240, Math.Min(640, Math.Max(DetSimilarTileW, DetSimilarTileH * 2 / 3) * 1.6));   // (sharp at bigger sizes too)
            foreach (var t in detSimilarAll.ToList())
            {
                var tile = t;
                KippixShared.SafeWork.Queue(__ =>
                {
                    try
                    {
                        string f = tile.CoverFile;
                        if (wide)
                        {
                            Game g = null;
                            try { g = api.Database.Games.Get(tile.GameId); } catch (Exception) { }
                            string art = null;
                            try { art = WideArtPath(g); } catch (Exception) { }
                            if (!string.IsNullOrEmpty(art)) f = art;
                        }
                        if (string.IsNullOrEmpty(f)) return;
                        var img = Images.Load(f, decode);
                        if (img != null) OnUi(() => { if (gen == similarArtGen) tile.Cover = img; });
                    }
                    catch (Exception) { }
                });
            }
        }

        /// <summary>The next shape (Free move › Y › X, and the Cover shape row).</summary>
        private void CycleSimilarShape()
        {
            settings.DetSimilarShape = (SimilarShape + 1) % SimilarShapeCount;
            ApplySimilarTileSize();
            PlaySound("move");
        }

        /// <summary>The covers take their size and shape (when they changed), then as many as fit across are shown.</summary>
        private void ApplySimilarTileSize()
        {
            double h = SimilarTileH();
            int shape = SimilarShape;
            bool shapeChanged = shape != similarShownShape;
            if (Math.Abs(h - similarShownH) < 0.5 && !shapeChanged) { FitDetSimilar(); return; }
            similarShownH = h;
            similarShownShape = shape;
            ShapeSimilarTiles();
            Notify("DetSimilarTileW");
            Notify("DetSimilarTileH");
            FitDetSimilar();
            if (shapeChanged && detSimilarAll.Count > 0) LoadSimilarArt();   // (wide picture or cover)
            if (ui != null) ui.BeginInvoke(new Action(() => { try { FitDetSimilar(); SyncFreeGaps(); } catch (Exception) { } }), DispatcherPriority.Loaded);
        }

        /// <summary>Free move › Y on More like this: Up / Right bigger covers, Down / Left smaller. The row keeps its place.</summary>
        private void SimilarResizeBy(int dx, int dy)
        {
            if (dx == 0 && dy == 0) { PlaySound("back"); return; }
            int d = (dy < 0 || dx > 0) ? 1 : -1;
            double now = SimilarTileH();
            double want = Math.Max(SimilarMinH, Math.Min(SimilarMaxH, Math.Round((now + d * SimilarStepH) / SimilarStepH) * SimilarStepH));
            if (Math.Abs(want - now) < 0.5) { PlaySound("back"); return; }
            SetSimilarTileH(want);
            PlaySound("move");
            LaterOutline();
        }

        private void SetSimilarTileH(double h)
        {
            var s = MasterSize(SimilarKey);
            s[0] = 0;   // (a 0.74 widget-style size goes: the covers are what's sized now)
            s[1] = Math.Abs(h - SimilarDefaultH) < 0.5 ? 0 : h;
            StoreMasterSize(SimilarKey, s);
            ApplySimilarTileSize();
        }

        /// <summary>The settings row's sizes: Small, Medium (as it comes), Large, Extra large.</summary>
        private static readonly double[] SimilarPresetsH = { 165, 225, 300, 375 };

        private string SimilarSizeText()
        {
            double h = SimilarTileH();
            string[] names = { Loc("LOC_KX_SizeSmall", "Small"), Loc("LOC_KX_SizeMedium", "Medium"), Loc("LOC_KX_SizeLarge", "Large"), Loc("LOC_KX_SizeXLarge", "Extra large") };
            for (int i = 0; i < SimilarPresetsH.Length; i++)
                if (Math.Abs(SimilarPresetsH[i] - h) < 0.5) return names[i];
            return string.Format(Loc("LOC_KX_SizeCoversOwn", "{0} × {1}"), DetSimilarTileW, Math.Round(h));
        }

        private void CycleSimilarSize()
        {
            double h = SimilarTileH();
            double next = SimilarPresetsH[0];
            foreach (double p in SimilarPresetsH) if (p > h + 0.5) { next = p; break; }
            SetSimilarTileH(next);
            SaveSettings();
            PlaySound("move");
        }

        /// <summary>
        /// As many covers as fit across the page (no cover is cut off at the edge or hidden past it, so the highlight and Free
        /// move's outline are round what you see). The page's own width, less where Free move put the row.
        /// </summary>
        private void FitDetSimilar()
        {
            int n = detSimilarAll.Count;
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                var host = details != null ? UiTools.FindByName(details, "PART_ViewHost") : null;
                double room = host != null && host.ActualWidth > 0 ? host.ActualWidth : 1728;
                room -= 12 + Math.Max(0, MasterOffset(SimilarKey)[0]);   // (the covers' row: 6 px each side for the highlight)
                double each = SimilarStep;                                // (a cover and the gap after it, the last one's too)
                int fit = CardShapes.IsTriangle(SimilarShape)
                    ? (int)Math.Floor((room - DetSimilarTileW) / each) + 1   // (triangles overlap: the last one's whole width)
                    : (int)Math.Floor(room / each);
                n = Math.Min(n, Math.Max(1, fit));
            }
            catch (Exception) { }
            while (DetSimilar.Count > n) DetSimilar.RemoveAt(DetSimilar.Count - 1);
            while (DetSimilar.Count < n && DetSimilar.Count < detSimilarAll.Count) DetSimilar.Add(detSimilarAll[DetSimilar.Count]);
        }

        private Game SelectedGameNow()
        {
            try { return api.MainView.SelectedGames != null ? api.MainView.SelectedGames.FirstOrDefault() : null; } catch (Exception) { return null; }
        }

        // =====================================================================
        // Filling it in (when the page opens, and when it moves to another game)
        // =====================================================================

        private void UpdateGamePageMore()
        {
            var g = SelectedGameNow();
            int gen = ++detMoreGen;
            DetHistory.Clear();
            DetCaptures.Clear();
            DetSimilar.Clear();
            detSimilarAll.Clear();
            DetHltbMain = DetHltbExtras = DetHltbComplete = DetAchSummary = DetDlcText = DetHistoryTotal = DetHistoryLine = "";
            DetAchRarest = DetAchNext = null;
            FillInfoSize(detailsOpen ? g : null);
            if (!detailsOpen || g == null) { NotifyDetMore(); FillAddonBits(null); FillSavesCard(null); FillGameExtras(null, gen); return; }
            FillAddonBits(g);
            FillSavesCard(g);
            FillGameExtras(g, gen);   // (r12) News, Mods, players online now, feature badges
            bool stayDown = false;
            if (g.Id != detGameId)
            {
                // LB / RB (or a game from More like this) on the lower page: the next game opens at its top, on Play
                bool wasDown = detMoreDown && detGameId != Guid.Empty;
                detGameId = g.Id;
                DetailsScrollTo(false, wasDown);
                if (wasDown) ui.BeginInvoke(new Action(() => { if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) UiTools.FocusInside("PART_ElemGameDetails"); }), DispatcherPriority.Loaded);
                LoadDescription(g);
            }
            FillStatusAndNotes(g);
            FillAchHighlights(g);
            FillCaptures(g, gen);
            var game = g;
            var shown = GamesInView();
            KippixShared.SafeWork.Queue(_ =>
            {
                List<HistoryBar> bars = null;
                string total = "", line = "";
                HltbInfo h = null;
                List<Game> similar = null;
                string dlc = null;
                try { bars = BuildHistory(game, out total, out line); } catch (Exception) { }
                try { h = BuildHltb(game); } catch (Exception) { }
                try { similar = MoreLikeThis(game).Where(x => shown == null || shown.Contains(x.Id)).Take(12).ToList(); } catch (Exception) { }
                try { dlc = DlcNote(game); } catch (Exception) { }
                OnUi(() =>
                {
                    if (gen != detMoreGen) return;
                    if (bars != null) foreach (var b in bars) DetHistory.Add(b);
                    DetHistoryTotal = total;
                    DetHistoryLine = line;
                    if (h != null) { DetHltbMain = h.Main; DetHltbExtras = h.Extras; DetHltbComplete = h.Complete; DetHltbYou = h.You; DetHltbFill = h.Fill; }
                    if (similar != null)
                        foreach (var s in similar)
                        {
                            var t = MakeRowTile(s, 150, 225);
                            var id = s.Id;
                            t.Command = new RelayCommand(x =>
                            {
                                PlaySound("select");
                                if (detOriginId == Guid.Empty) detOriginId = detGameId;   // closing the game page goes back to the game you opened first
                                try { api.MainView.SelectGame(id); } catch (Exception) { }
                                DetailsScrollTo(false, true);
                                ui.BeginInvoke(new Action(() => { if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) UiTools.FocusInside("PART_ElemGameDetails"); }), DispatcherPriority.Loaded);
                            });
                            detSimilarAll.Add(t);
                        }
                    similarShownShape = SimilarShape;
                    ShapeSimilarTiles();   // (their shape: More like this's own, not the home cards')
                    FitDetSimilar();   // as many as fit across
                    DetDlcText = dlc;
                    DetDlcIsSteam = game.PluginId == SteamPluginId;
                    NotifyDetMore();
                    if (stayDown && detMoreDown)
                        ui.BeginInvoke(new Action(() =>
                        {
                            if (gen != detMoreGen || !detMoreDown) return;
                            if (!ShowDetMore) { DetailsScrollTo(false, true); if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) UiTools.FocusInside("PART_ElemGameDetails"); return; }
                            var f = Keyboard.FocusedElement as DependencyObject;
                            var more = UiTools.FindByName(Application.Current.MainWindow, "KX_DetMore");
                            if (more == null || f == null || !more.IsAncestorOf(f) || !((UIElement)f).IsVisible) FocusLowerPage();
                        }), DispatcherPriority.Loaded);
                    LoadSimilarArt();   // (the cover, or the wide picture for the wide shapes)
                });
            });
            NotifyDetMore();
        }

        // ---------------- play history ----------------

        private List<HistoryBar> BuildHistory(Game g, out string total, out string line)
        {
            total = line = "";
            var mine = LoadSessionsFor(g.Id);
            var now = DateTime.Now;
            int back = ((int)now.DayOfWeek + 6) % 7;
            var thisWeek = now.Date.AddDays(-back);
            var weeks = new double[12];
            foreach (var s in mine)
            {
                int w = (int)Math.Floor((thisWeek - s.Start.Date.AddDays(-(((int)s.Start.DayOfWeek + 6) % 7))).TotalDays / 7);
                if (w >= 0 && w < 12) weeks[11 - w] += s.Seconds / 3600.0;
            }
            if (g.Playtime == 0 && mine.Count == 0) return null;
            double max = Math.Max(0.5, weeks.Max());
            var bars = new List<HistoryBar>();
            for (int i = 0; i < 12; i++)
            {
                var start = thisWeek.AddDays(-7 * (11 - i));
                bars.Add(new HistoryBar
                {
                    Height = weeks[i] <= 0 ? 3 : Math.Max(6, 110 * weeks[i] / max),
                    Label = i == 11 ? Loc("LOC_KX_Now", "Now") : i % 3 == 2 ? start.ToString("d MMM", CultureInfo.CurrentCulture) : "",
                    Tip = start.ToString("d MMM", CultureInfo.CurrentCulture) + ": " + HoursShort(weeks[i]),
                    IsThisWeek = i == 11
                });
            }
            total = string.Format(Loc("LOC_KX_PlayedInAll", "{0} in all"), HoursShort(g.Playtime / 3600.0));
            var parts = new List<string>();
            int count = mine.Count(s => !s.Estimated);
            if (count > 0) parts.Add(string.Format(count == 1 ? Loc("LOC_KX_OneSession", "1 session") : Loc("LOC_KX_NSessions", "{0} sessions"), count));
            var longest = mine.Where(s => !s.Estimated).OrderByDescending(s => s.Seconds).FirstOrDefault();
            if (longest != null) parts.Add(string.Format(Loc("LOC_KX_Longest", "longest {0}"), HoursShort(longest.Seconds / 3600.0)));
            double last4 = weeks.Skip(8).Sum();
            if (last4 > 0) parts.Add(string.Format(Loc("LOC_KX_Last4Weeks", "{0} in the last 4 weeks"), HoursShort(last4)));
            line = string.Join("  ·  ", parts);
            return bars;
        }

        private string HoursShort(double h)
        {
            if (h <= 0) return Loc("LOC_KX_NoTime", "none");
            if (h < 1) return Math.Max(1, (int)Math.Round(h * 60)) + " min";
            return h < 10 ? Math.Round(h, 1).ToString(CultureInfo.CurrentCulture) + " h" : Math.Round(h) + " h";
        }

        // ---------------- HowLongToBeat ----------------

        private class HltbInfo { public string Main, Extras, Complete, You; public double Fill; }

        private HltbInfo BuildHltb(Game g)
        {
            var h = KippixOverlay.GameData.LoadHltb(api.Paths.ExtensionsDataPath, g.Id);
            if (h == null) return null;
            var r = new HltbInfo
            {
                Main = h.Story > 0 ? HoursShort(h.Story) : "—",
                Extras = h.Extras > 0 ? HoursShort(h.Extras) : "—",
                Complete = h.Completionist > 0 ? HoursShort(h.Completionist) : "—"
            };
            double you = g.Playtime / 3600.0;
            double main = h.Story > 0 ? h.Story : h.Extras;
            if (main > 0)
            {
                double f = Math.Min(1, you / main);
                r.Fill = 480 * f;
                r.You = you <= 0 ? Loc("LOC_KX_HltbNotStarted", "You haven't started it yet")
                      : you >= main ? string.Format(Loc("LOC_KX_HltbPast", "You: {0}, past the main story time"), HoursShort(you))
                      : string.Format(Loc("LOC_KX_HltbYou", "You: {0}, {1}% of the main story time"), HoursShort(you), Math.Round(100 * f));
            }
            return r;
        }

        // ---------------- your captures ----------------

        private void FillCaptures(Game g, int gen)
        {
            Action fill = () =>
            {
                if (gen != detMoreGen) return;
                DetCaptures.Clear();
                CapturesRowShift(0, false);   // (back to the newest)
                HookCapturesRow();
                var mine = galleryFiles.Where(f => f.GameId == g.Id).OrderByDescending(f => f.Date).ToList();
                foreach (var f in mine.Take(24))   // (0.99.54: the row slides sideways; 0.99.55: 24)
                {
                    var file = f;
                    var it = new GalleryItem { File = f, Caption = f.Date.ToString("d MMM yyyy", CultureInfo.CurrentCulture) };
                    it.Command = new RelayCommand(_ => OpenGalleryForGame(g.Id, g.Name, file.Path));
                    DetCaptures.Add(it);
                }
                DetCapturesText = mine.Count == 0 ? "" : CountText(mine);
                LoadGalleryThumbs(DetCaptures.ToList(), galleryGen);
                NotifyDetMore();
            };
            if (galleryFiles.Count > 0 && (DateTime.Now - galleryScannedAt).TotalMinutes < 10) { fill(); return; }
            if (galleryScanning) return;
            galleryScanning = true;
            KippixShared.SafeWork.Queue(_ =>
            {
                List<GalleryFile> found = null;
                try { found = FindGalleryFiles(); } catch (Exception) { }
                OnUi(() =>
                {
                    galleryScanning = false;
                    if (found != null) { galleryFiles = found; galleryScannedAt = DateTime.Now; }
                    fill();
                    // the gallery opened while this ran (its own scan was skipped): show what was found there too
                    bool galleryEmpty = !GalleryItems.Any(i => i.IsTile) && GalleryGames.Count == 0;
                    if ((openPanel == "gallery" || gallerySettingsFromGallery) && !galleryViewerOpen && galleryEmpty) RebuildGallery(true);
                    else NotifyGallery();
                });
            });
        }

        // ---------------- achievement highlights ----------------

        private void FillAchHighlights(Game g)
        {
            DetAchSummary = "";
            DetAchRarest = DetAchNext = null;
            DetAchRarestIcon = DetAchNextIcon = null;
            var data = achData;
            var d = data != null ? data.FirstOrDefault(x => x != null && x.Id == g.Id) : null;
            FillDetNextUp(d);   // NEXT UP on the card (ThemeApi.NextUp.cs; replaces the single "easiest next" line while it's on)
            if (d == null || d.Items == null || d.Items.Count == 0) return;
            int un = d.Items.Count(i => i.Unlocked);
            DetAchSummary = string.Format(Loc("LOC_KX_AchOf", "{0} of {1} unlocked  ·  {2}%"), un, d.Items.Count, Math.Round(100.0 * un / d.Items.Count));
            DetAchFill = 480.0 * un / d.Items.Count;
            var rarest = d.Items.Where(i => i.Unlocked && i.Rarity.HasValue).OrderBy(i => i.Rarity.Value).FirstOrDefault();
            if (rarest != null)
            {
                DetAchRarest = rarest.Name;
                DetAchRarestSub = string.Format(Loc("LOC_KX_AchRarestSub", "Your rarest: {0}% of players have it"), Math.Round(rarest.Rarity.Value, 1));
                LoadAchIcon(rarest.Icon, img => { DetAchRarestIcon = img; Notify("DetAchRarestIcon"); });
            }
            var next = d.Items.Where(i => !i.Unlocked && !i.Hidden && i.Rarity.HasValue).OrderByDescending(i => i.Rarity.Value).FirstOrDefault()
                       ?? d.Items.FirstOrDefault(i => !i.Unlocked && !i.Hidden);
            if (HasDetAchNextUp) next = null;
            if (next != null)
            {
                DetAchNext = next.Name;
                DetAchNextSub = next.Rarity.HasValue ? string.Format(Loc("LOC_KX_AchNextSub", "Easiest to get next: {0}% of players have it"), Math.Round(next.Rarity.Value, 1))
                                                     : Loc("LOC_KX_AchNextSub2", "Still to get");
                LoadAchIcon(next.Icon, img => { DetAchNextIcon = img; Notify("DetAchNextIcon"); });
            }
        }

        private void LoadAchIcon(string icon, Action<ImageSource> done)
        {
            if (string.IsNullOrEmpty(icon)) return;
            int gen = detMoreGen;
            KippixShared.SafeWork.Queue(_ => { var img = Images.Load(icon, 96); OnUi(() => { if (gen == detMoreGen && img != null) done(img); }); });
        }

        // ---------------- status, rating, notes ----------------

        private void FillStatusAndNotes(Game g)
        {
            DetStatusText = g.CompletionStatus != null ? g.CompletionStatus.Name : Loc("LOC_KX_StatusNone", "No status set");
            int stars = g.UserScore.HasValue ? (int)Math.Round(g.UserScore.Value / 20.0) : 0;
            DetStarsText = stars <= 0 ? Loc("LOC_KX_NotRated", "Not rated") : new string('★', stars) + new string('☆', 5 - stars);
            string notes = g.Notes ?? "";
            notes = Regex.Replace(notes, "<[^>]+>", " ").Trim();   // notes can be HTML
            DetNotesText = notes.Length > 700 ? notes.Substring(0, 700).TrimEnd() + "…" : notes;
        }

        private void BuildGameStatusRows(Game g)
        {
            GameStatusRows.Clear();
            if (g == null) return;
            AddCompletionQuickRows(g);   // FINISHED IT? Beaten / 100% / Dropped (ThemeApi.Completion.cs)
            GameStatusRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_StatusHeader", "STATUS") });
            try
            {
                foreach (var cs in api.Database.CompletionStatuses.OrderBy(c => c.Name))
                {
                    var status = cs;
                    GameStatusRows.Add(new PickRow { Text = cs.Name, Icon = "flag", Key = "s:" + cs.Id, IsSelected = g.CompletionStatusId == cs.Id, Command = new RelayCommand(_ => SetGameStatus(status.Id)) });
                }
            }
            catch (Exception) { }
            GameStatusRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_RatingHeader", "YOUR RATING") });
            int now = g.UserScore.HasValue ? (int)Math.Round(g.UserScore.Value / 20.0) : 0;
            for (int i = 5; i >= 1; i--)
            {
                int n = i;
                GameStatusRows.Add(new PickRow { Text = new string('★', n) + new string('☆', 5 - n), Icon = "star", Key = "r:" + n, IsSelected = now == n, Command = new RelayCommand(_ => SetGameRating(n)) });
            }
            GameStatusRows.Add(new PickRow { Text = Loc("LOC_KX_ClearRating", "No rating"), Icon = "remove", Key = "r:0", IsSelected = now == 0, Command = new RelayCommand(_ => SetGameRating(0)) });
        }

        private Game PanelGame() { try { return detGameId != Guid.Empty ? api.Database.Games.Get(detGameId) : SelectedGameNow(); } catch (Exception) { return null; } }

        private void SetGameStatus(Guid statusId)
        {
            var g = PanelGame();
            if (g == null) return;
            try
            {
                g.CompletionStatusId = g.CompletionStatusId == statusId ? Guid.Empty : statusId;   // pressing the one that's set clears it
                api.Database.Games.Update(g);
                PlaySound("select");
            }
            catch (Exception ex) { logger.Warn("Kippix: status: " + ex.Message); }
            RefreshStatusRows("s:" + statusId);
        }

        private void SetGameRating(int stars)
        {
            var g = PanelGame();
            if (g == null) return;
            try
            {
                g.UserScore = stars <= 0 ? (int?)null : stars * 20;
                api.Database.Games.Update(g);
                PlaySound("select");
            }
            catch (Exception ex) { logger.Warn("Kippix: rating: " + ex.Message); }
            RefreshStatusRows("r:" + stars);
        }

        private void RefreshStatusRows(string focusKey)
        {
            var g = PanelGame();
            BuildGameStatusRows(g);
            if (g != null) FillStatusAndNotes(g);
            NotifyDetMore();
            ui.BeginInvoke(new Action(() =>
            {
                var r = GameStatusRows.FirstOrDefault(x => x.Key == focusKey);
                if (r != null) UiTools.FocusItem("KX_GameStatusList", r);
            }), DispatcherPriority.ContextIdle);
        }

        private void EditNotes()
        {
            var g = PanelGame();
            if (g == null) return;
            // the notes card shows what's typed as it's typed; B puts back what was there
            OpenKeyboard(string.Format(Loc("LOC_KX_NotesAskFor", "Your notes for {0}"), g.Name), g.Notes ?? "", text =>
            {
                try
                {
                    g.Notes = text;
                    api.Database.Games.Update(g);
                    FillStatusAndNotes(g);
                    NotifyDetMore();
                }
                catch (Exception ex) { logger.Warn("Kippix: notes: " + ex.Message); }
            }, text =>
            {
                DetNotesText = text + "\u258F";
                Notify("DetNotesText");
                Notify("HasDetNotes");
            }, () =>
            {
                FillStatusAndNotes(g);
                NotifyDetMore();
            }, 2000);
            kbLiveNow = true;
            NotesAboveKeyboard();
            DetNotesText = (g.Notes ?? "") + "\u258F";
            Notify("DetNotesText");
            Notify("HasDetNotes");
        }

        /// <summary>On the lower page, the notes card scrolls up near the top, so the keyboard (over the bottom) doesn't hide it.</summary>
        private void NotesAboveKeyboard()
        {
            try
            {
                if (!detailsOpen || !detMoreDown) return;
                var win = Application.Current.MainWindow;
                var sv = UiTools.FindByName(win, "KX_DetMore") as ScrollViewer;
                var card = UiTools.FindByName(win, "KX_SecNotes") as FrameworkElement;
                if (sv == null || card == null || !sv.IsAncestorOf(card) || !card.IsVisible) return;
                double y = card.TransformToAncestor(sv).Transform(new Point(0, 0)).Y;
                sv.ScrollToVerticalOffset(Math.Max(0, Math.Min(sv.ScrollableHeight, sv.VerticalOffset + y - 30)));
            }
            catch (Exception) { }
        }

        // ---------------- more like this ----------------

        private List<Game> MoreLikeThis(Game g)
        {
            var genres = new HashSet<Guid>(g.GenreIds ?? new List<Guid>());
            var tags = new HashSet<Guid>(g.TagIds ?? new List<Guid>());
            var cats = new HashSet<Guid>(g.CategoryIds ?? new List<Guid>());
            if (genres.Count == 0 && tags.Count == 0) return new List<Game>();
            var scored = new List<Tuple<Game, double>>();
            foreach (var o in api.Database.Games)
            {
                if (o == null || o.Id == g.Id || o.Hidden) continue;
                double s = 0;
                if (o.GenreIds != null) s += o.GenreIds.Count(genres.Contains) * 2;
                if (o.TagIds != null) s += o.TagIds.Count(tags.Contains);
                if (o.CategoryIds != null) s += o.CategoryIds.Count(cats.Contains) * 0.5;
                if (s <= 1) continue;
                if (o.SeriesIds != null && g.SeriesIds != null && o.SeriesIds.Intersect(g.SeriesIds).Any()) s += 3;
                scored.Add(Tuple.Create(o, s));
            }
            return scored.OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item1.LastActivity ?? DateTime.MinValue).Take(40).Select(x => x.Item1).ToList();
        }

        // ---------------- DLC ----------------

        /// <summary>Steam: DLC you own but have switched off (or that's waiting to download). Null when there's nothing to say.</summary>
        private string DlcNote(Game g)
        {
            if (g == null || g.PluginId != SteamPluginId || string.IsNullOrEmpty(g.GameId) || !g.IsInstalled) return null;
            string acf = FindAppManifest(g.GameId);
            if (acf == null) return null;
            string text = File.ReadAllText(acf);
            var off = new HashSet<string>();
            foreach (Match m in Regex.Matches(text, "\"DisabledDLC\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase))
                foreach (string id in m.Groups[1].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) off.Add(id);
            if (off.Count > 0)
                return string.Format(off.Count == 1 ? Loc("LOC_KX_DlcOffOne", "1 DLC you own isn't installed (it's switched off in Steam).")
                                                    : Loc("LOC_KX_DlcOffMany", "{0} DLC you own aren't installed (they're switched off in Steam)."), off.Count);
            var flags = Regex.Match(text, "\"StateFlags\"\\s+\"(\\d+)\"");
            int f;
            if (flags.Success && int.TryParse(flags.Groups[1].Value, out f) && (f & 2) != 0 && text.IndexOf("dlcappid", StringComparison.OrdinalIgnoreCase) >= 0)
                return Loc("LOC_KX_DlcWaiting", "An update or DLC is waiting to download in Steam.");
            return null;
        }

        private static string FindAppManifest(string appId)
        {
            try
            {
                string steam = null;
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    steam = k != null ? Convert.ToString(k.GetValue("SteamPath")) : null;
                if (string.IsNullOrEmpty(steam)) return null;
                steam = steam.Replace('/', '\\');
                var libs = new List<string> { Path.Combine(steam, "steamapps") };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
                foreach (string lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string f = Path.Combine(lib, "appmanifest_" + appId + ".acf");
                    if (File.Exists(f)) return f;
                }
            }
            catch (Exception) { }
            return null;
        }

        // ---------------- the More menu ----------------

        /// <summary>The More menu and Status / rating open over the game page; B closes them back to it.</summary>
        private void OpenGamePanel(string page)
        {
            var g = PanelGame();
            if (g == null) return;
            // a profile whose settings need the PIN: ask first, then come straight back to this page
            if (openPanel != "options" && ProfileGate("settings", () => OpenGamePanel(page))) return;
            PlaySound("select");
            GameMoreTitle = g.Name;
            Notify("GameMoreTitle");
            if (page == "gamemore") BuildGameMoreRows(g);
            else if (page == "dethistory") BuildDetHistoryRows();
            else BuildGameStatusRows(g);
            if (openPanel != "options") TogglePanel("options");
            SetOptionsPage(page);
        }

        private bool GameMoreBack()
        {
            if (openPanel != "options" || (optionsPage != "gamemore" && optionsPage != "gamestatus" && optionsPage != "dethistory")) return false;
            ClosePanels();
            return true;
        }

        private void BuildGameMoreRows(Game g)
        {
            GameMoreRows.Clear();
            if (collectionsShown) { collectionsShown = false; Notify("GameCollectionsShown"); Notify("GameMoreListShown"); }
            GameMoreRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecThisGame", "THIS GAME") });
            AddHomeRows(g);
            AddShareCardRow(g);   // Share card (ThemeApi.ShareCards.cs)
            string store = StorePageFor(g);
            var links = g.Links == null ? new List<Link>()
                      : g.Links.Where(l => l != null && !string.IsNullOrEmpty(l.Url) && l.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                           && !string.Equals(l.Url, store, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
            bool steam = g.PluginId == SteamPluginId && !string.IsNullOrEmpty(g.GameId);
            if (store != null || steam || links.Count > 0) GameMoreRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecLinks", "LINKS") });
            if (store != null) GameMoreRows.Add(new PickRow { Text = Loc("LOC_KX_OpenStorePage", "Store page"), Sub = LinkHostOf(store), Icon = "store", Key = "store", Command = new RelayCommand(_ => { ClosePanels(); OpenBrowser(store); }) });
            int dlcKnown = -1;
            if (steam) lock (dlcCounts) { if (!dlcCounts.TryGetValue(g.GameId, out dlcKnown)) dlcKnown = -1; }
            if (steam && dlcKnown == 0) steam = false;   // (0.86: known to have none: no row at all)
            if (steam && dlcKnown > 0)
            {
                string id = g.GameId;
                GameMoreRows.Add(new PickRow { Text = Loc("LOC_KX_DlcOnStore", "DLC on the store"), Icon = "tag", Key = "dlc",
                                               Sub = string.Format(dlcKnown == 1 ? Loc("LOC_KX_OneDlc", "1 DLC") : Loc("LOC_KX_NDlc", "{0} DLC"), dlcKnown),
                                               Command = new RelayCommand(_ => { ClosePanels(); OpenBrowser("https://store.steampowered.com/dlc/" + id + "/"); }) });
            }
            else if (steam)
            {
                string id = g.GameId;
                var row = new PickRow { Text = Loc("LOC_KX_DlcOnStore", "DLC on the store"), Sub = Loc("LOC_KX_Checking", "Checking…"), Icon = "tag", Key = "dlc",
                                        Command = new RelayCommand(_ => { ClosePanels(); OpenBrowser("https://store.steampowered.com/dlc/" + id + "/"); }) };
                GameMoreRows.Add(row);
                KippixShared.SafeWork.Queue(_ =>
                {
                    int n = SteamDlcCount(id);
                    if (n >= 0) lock (dlcCounts) dlcCounts[id] = n;
                    OnUi(() =>
                    {
                        // the collections may be showing: then the row is waiting in the copy kept for B
                        if (moreSnapshot != null)
                        {
                            var kept = moreSnapshot.FirstOrDefault(r => r.Key == "dlc");
                            if (kept != null)
                            {
                                if (n <= 0) moreSnapshot.Remove(kept);
                                else kept.Sub = string.Format(n == 1 ? Loc("LOC_KX_OneDlc", "1 DLC") : Loc("LOC_KX_NDlc", "{0} DLC"), n);
                            }
                        }
                        int at = GameMoreRows.IndexOf(row);
                        if (at < 0) return;
                        if (n <= 0)
                        {
                            // (0.86) not taken out: the rows under it moving up made the highlight jump up a row while you
                            // moved down. It says so where it is (next time the menu opens it isn't there at all)
                            row.BecomeLike(new PickRow { Text = row.Text, Icon = row.Icon, Key = row.Key, Command = row.Command,
                                                         Sub = n == 0 ? Loc("LOC_KX_NoDlc", "No DLC for this game") : Loc("LOC_KX_DlcUnknown", "Couldn't check: opens its DLC page") });
                            return;
                        }
                        // changed where it is (replacing a highlighted row would lose the highlight to the game page)
                        row.BecomeLike(new PickRow { Text = row.Text, Icon = row.Icon, Key = row.Key, Command = row.Command, Sub = string.Format(n == 1 ? Loc("LOC_KX_OneDlc", "1 DLC") : Loc("LOC_KX_NDlc", "{0} DLC"), n) });
                    });
                });
            }
            foreach (var l in links)
            {
                string url = l.Url;
                GameMoreRows.Add(new PickRow { Text = string.IsNullOrEmpty(l.Name) ? LinkHostOf(url) : l.Name, Sub = LinkHostOf(url), Icon = "link", Command = new RelayCommand(_ => { ClosePanels(); OpenBrowser(url); }) });
            }
            if (g.IsInstalled) GameMoreRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecManage", "MANAGE") });
            if (g.IsInstalled && !string.IsNullOrEmpty(g.InstallDirectory))
            {
                string dir = g.InstallDirectory;
                try { dir = api.ExpandGameVariables(g, dir); } catch (Exception) { }
                // the size goes on its line (not a row of its own that does nothing)
                string sizeText = g.InstallSize.HasValue && g.InstallSize.Value > 0 ? KippixOverlay.Capture.DriveSize((long)g.InstallSize.Value) : null;
                GameMoreRows.Add(new PickRow { Text = Loc("LOC_KX_InstallFolder", "Open the install folder"), Sub = sizeText != null ? sizeText + "  ·  " + dir : dir, Icon = "folder", Key = "folder",
                                               Command = new RelayCommand(_ => { ClosePanels(); try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch (Exception) { } }) });
            }
            if (g.IsInstalled)
            {
                var id = g.Id;
                GameMoreRows.Add(new PickRow { Text = Loc("LOC_KX_Uninstall", "Uninstall"), Sub = Loc("LOC_KX_UninstallSub", "Through the game's own launcher"), Icon = "trash", Key = "uninstall",
                                               Command = new RelayCommand(_ => { ClosePanels(); try { api.UninstallGame(id); } catch (Exception ex) { logger.Warn("Kippix: uninstall: " + ex.Message); } }) });
            }
            if (!GameMoreRows.Any(r => r.IsRow)) GameMoreRows.Add(new PickRow { Text = Loc("LOC_KX_NothingMore", "Nothing more for this game."), Icon = "info", IsInfo = true });
        }

        private string StorePageFor(Game g)
        {
            if (g.PluginId == SteamPluginId && !string.IsNullOrEmpty(g.GameId)) return "https://store.steampowered.com/app/" + g.GameId + "/";
            if (g.Links == null) return null;
            var l = g.Links.FirstOrDefault(x => x != null && !string.IsNullOrEmpty(x.Url) && Regex.IsMatch(x.Name ?? "", "store|steam|gog|epic|xbox|microsoft|ubisoft|ea app|origin|battle", RegexOptions.IgnoreCase));
            return l != null ? l.Url : null;
        }

        private static string LinkHostOf(string url)   // (r12: renamed; ThemeApi.CustomTiles.cs has its own HostOf)
        {
            try { return new Uri(url).Host.Replace("www.", ""); } catch (Exception) { return url; }
        }

        private static readonly Dictionary<string, int> dlcCounts = new Dictionary<string, int>();   // (0.86) Steam app id → DLC count, this session

        private int SteamDlcCount(string appId)
        {
            try
            {
                var root = QuickJson("https://store.steampowered.com/api/appdetails?filters=basic&appids=" + appId, 8000) as Dictionary<string, object>;
                var app = Dig(root, appId) as Dictionary<string, object>;
                var data = Dig(app, "data") as Dictionary<string, object>;
                if (data == null) return -1;   // (0.86: couldn't tell: not remembered as "none")
                var dlc = Dig(data, "dlc") as System.Collections.ArrayList;
                if (dlc != null) return dlc.Count;
                var arr = Dig(data, "dlc") as object[];
                return arr != null ? arr.Length : 0;
            }
            catch (Exception) { return -1; }
        }

        // =====================================================================
        // Scrolling down to the sections and back up
        // =====================================================================

        /// <summary>
        /// Game page keys that come first: B on the lower page goes back up (it doesn't close the game page);
        /// Down from the buttons stops on Open in Steam when the update / DLC note shows, and Up from it goes back to Play.
        /// </summary>
        private bool GamePageKey(KeyEventArgs e)
        {
            if (!detailsOpen || openPanel != null || descReaderOpen || confirmOpen) return false;
            if (IsPadRelease(e)) return false;
            if (IsPadButton(e, "X"))
            {
                // X: the settings of the part that's highlighted (the buttons, the trailer, a card...), straight over the game page
                e.Handled = true;
                OpenPartSettingsFromGame(DetPartAt(Keyboard.FocusedElement as DependencyObject));
                return true;
            }
            if (IsBackKey(e) && detMoreDown)
            {
                PlaySound("back");
                DetailsScrollTo(false, true);
                ui.BeginInvoke(new Action(() => { if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) UiTools.FocusInside("PART_ElemGameDetails"); }), DispatcherPriority.Loaded);
                e.Handled = true;
                return true;
            }
            if (e.Key != Key.Down && e.Key != Key.Up) return false;
            var win = Application.Current.MainWindow;
            var dlc = UiTools.FindByName(win, "KX_DetDlcButton");
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (dlc == null || !dlc.IsVisible || focused == null || detMoreDown) return false;
            if (e.Key == Key.Down && Ancestor(focused, "DetButtons") != null)
            {
                Keyboard.Focus(dlc);
                e.Handled = true;
                return true;
            }
            if (e.Key == Key.Up && ReferenceEquals(focused, dlc))
            {
                if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) return false;
                e.Handled = true;
                return true;
            }
            return false;
        }

        /// <summary>Down from the game page's last row goes to the sections below; Up from the first one goes back.</summary>
        private bool DetailsMoreKey(KeyEventArgs e)
        {
            if (!detailsOpen || !ShowDetMore || IsPadRelease(e)) return false;
            var win = Application.Current.MainWindow;
            var more = UiTools.FindByName(win, "KX_DetMore");
            var focused = Keyboard.FocusedElement as DependencyObject;
            if (more == null || focused == null) return false;
            bool inMore = more.IsAncestorOf(focused);
            if (!inMore && e.Key == Key.Down)
            {
                var next = (focused as UIElement) != null ? ((UIElement)focused).PredictFocus(FocusNavigationDirection.Down) as DependencyObject : null;
                if (next != null && !more.IsAncestorOf(next)) return false;   // there's still something below on the top part
                DetailsScrollTo(true, true);
                ui.BeginInvoke(new Action(FocusLowerPage), DispatcherPriority.Loaded);
                return true;
            }
            if (inMore && e.Key == Key.Down)
            {
                var next = (focused as UIElement) != null ? ((UIElement)focused).PredictFocus(FocusNavigationDirection.Down) as DependencyObject : null;
                if (next != null && more.IsAncestorOf(next) && !IsSink(next)) return false;
                ScrollLowerPage(1);   // nothing more to stop on: read on down
                return true;
            }
            if (inMore && e.Key == Key.Up)
            {
                var next = (focused as UIElement) != null ? ((UIElement)focused).PredictFocus(FocusNavigationDirection.Up) as DependencyObject : null;
                if (next != null && more.IsAncestorOf(next) && !IsSink(next)) return false;
                if (ScrollLowerPage(-1) && IsSink(focused)) return true;   // just for show: scroll back up first
                DetailsScrollTo(false, true);
                ui.BeginInvoke(new Action(() =>
                {
                    if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) UiTools.FocusInside("PART_ElemGameDetails");
                }), DispatcherPriority.Loaded);
                return true;
            }
            return false;
        }

        /// <summary>
        /// A card on the lower page (How long to beat, Play history, Achievements...) opening something over the game page:
        /// closing that comes back to the same card, with the lower page still showing (not the top of the game page).
        /// </summary>
        private UIElement detLowerReturn;

        private void RememberLowerFocus()
        {
            detLowerReturn = null;
            if (!detailsOpen || !detMoreDown) return;
            var f = Keyboard.FocusedElement as UIElement;
            var more = Application.Current != null ? UiTools.FindByName(Application.Current.MainWindow, "KX_DetMore") : null;
            if (f != null && more != null && more.IsAncestorOf(f) && !IsSink(f)) detLowerReturn = f;
        }

        private bool RestoreLowerFocus()
        {
            var el = detLowerReturn;
            detLowerReturn = null;
            if (el == null || !detailsOpen || !ShowDetMore || openPanel != null) return false;
            var more = UiTools.FindByName(Application.Current.MainWindow, "KX_DetMore");
            if (more == null || !more.IsAncestorOf(el) || !el.IsVisible || !el.Focusable) return false;
            if (!detMoreDown) DetailsScrollTo(true, false);
            Keyboard.Focus(el);
            var fe = el as FrameworkElement;
            if (fe != null) fe.BringIntoView();
            return true;
        }

        private static bool IsSink(DependencyObject d)
        {
            var fe = d as FrameworkElement;
            return fe != null && fe.Name == "KX_DetMoreSink";
        }

        private void DetailsScrollTo(bool down, bool animate)
        {
            detMoreDown = down;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var host = win != null ? UiTools.FindByName(win, "KX_DetScroll") : null;
            if (host == null) return;
            FadeTopPage(down, animate);
            var lower = UiTools.FindByName(win, "KX_DetMore");
            if (!down) { var sv = lower as ScrollViewer; if (sv != null) sv.ScrollToTop(); }
            // the lower page waits fully off the bottom of the screen (nothing of it peeks over the top page) and fades in when you go down
            if (lower != null) lower.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(down ? 1 : 0, new Duration(TimeSpan.FromMilliseconds(animate ? (down ? 380 : 200) : 0))));
            var t = host.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen) { t = new TranslateTransform(); host.RenderTransform = t; }
            double to = down ? -1010 : 0;   // the lower page (1100 down) ends up just under the top of the screen
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(animate && !ReduceMotionNow ? 420 : 0))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }
    
        // ---------------- (0.99.55) the captures row slides to keep the highlighted picture in sight ----------------

        private bool capturesRowHooked;

        private void HookCapturesRow()
        {
            if (capturesRowHooked) return;
            var list = UiTools.FindByName(Application.Current.MainWindow, "KX_DetCapturesList") as UIElement;
            if (list == null) return;
            capturesRowHooked = true;
            list.AddHandler(UIElement.GotKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((s, e) =>
            {
                try
                {
                    var fe = e.NewFocus as FrameworkElement;
                    var clip = UiTools.FindByName(Application.Current.MainWindow, "KX_DetCapturesClip") as FrameworkElement;
                    var items = list as FrameworkElement;
                    if (fe == null || clip == null || items == null || clip.ActualWidth <= 0) return;
                    var tt = items.RenderTransform as TranslateTransform;
                    double shift = tt != null ? tt.X : 0;
                    // where it is in the row, without the slide
                    var at = fe.TransformToAncestor(items).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                    double left = 96 + at.Left + shift, right = 96 + at.Right + shift;
                    double room = clip.ActualWidth - 96;   // (the page's right margin: a picture there is fully in sight)
                    double want = shift;
                    if (right > room) want = shift - (right - room);
                    if (left < 96) want = shift + (96 - left);
                    want = Math.Min(0, want);
                    if (Math.Abs(want - shift) > 0.5) CapturesRowShift(want, true);
                }
                catch (Exception) { }
            }), true);
        }

        private void CapturesRowShift(double x, bool animate)
        {
            var items = UiTools.FindByName(Application.Current.MainWindow, "KX_DetCapturesList") as FrameworkElement;
            if (items == null) return;
            var tt = items.RenderTransform as TranslateTransform;
            if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); items.RenderTransform = tt; }
            if (!animate) { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.X = x; return; }
            tt.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(x, new Duration(TimeSpan.FromMilliseconds(160))) { EasingFunction = new System.Windows.Media.Animation.CubicEase() });
        }

        /// <summary>(0.99.56) Back from a picture opened from the game page: the highlight on that picture in the captures row.</summary>
        private void FocusCaptureOnGamePage(string path)
        {
            try
            {
                if (!detailsOpen || openPanel != null) return;
                var it = path != null ? DetCaptures.FirstOrDefault(c => c.File != null && string.Equals(c.File.Path, path, StringComparison.OrdinalIgnoreCase)) : null;
                if (it == null) it = DetCaptures.FirstOrDefault();
                if (it != null) UiTools.FocusItem("KX_DetCapturesList", it);
            }
            catch (Exception) { }
        }
}
}
