using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Playnite.SDK.Events;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>One card on the Board (or, while editing, an empty square you can add a card to).</summary>
    public class BoardTile : Observable
    {
        internal BoardCard Card;          // null for an empty square
        internal int SlotX, SlotY;
        internal Guid GameId;
        internal string ArtFile, LogoFile;
        internal int PlacedShape = -1;

        public bool IsSlot { get; set; }
        public bool IsCard { get { return !IsSlot; } }

        private double left, top, width = 100, height = 100, opacity = 1;
        private int z;
        public double Left { get { return left; } set { Set(ref left, value, "Left"); } }
        public double Top { get { return top; } set { Set(ref top, value, "Top"); } }
        public double Width { get { return width; } set { Set(ref width, value, "Width"); } }
        public double Height { get { return height; } set { Set(ref height, value, "Height"); } }
        public int ZIndex { get { return z; } set { Set(ref z, value, "ZIndex"); } }
        public double TileOpacity { get { return opacity; } set { Set(ref opacity, value, "TileOpacity"); } }
        private double rotation;
        /// <summary>How far the tile is turned (degrees, clockwise, about its middle; LT / RT while it's picked up).</summary>
        public double Rotation { get { return rotation; } set { Set(ref rotation, value, "Rotation"); } }
        private double cardOpacity = 1;
        /// <summary>Its own opacity (Menu › Opacity while it's picked up), on top of TileOpacity (which the board sets itself).</summary>
        public double CardOpacity { get { return cardOpacity; } set { Set(ref cardOpacity, value, "CardOpacity"); } }
        private Thickness fullMargin;
        /// <summary>Cropped (Manual board): where the whole tile is round the part that's left (negative margins), for the dimmed
        /// part and the dashed line while cropping. Its picture fills the part that's left (0.80; it used to stay at the whole tile's size).</summary>
        public Thickness FullMargin { get { return fullMargin; } set { Set(ref fullMargin, value, "FullMargin"); } }
        private Thickness artMargin;
        /// <summary>Cropped and cut (0.81, the tile's Crop › Picture): the picture laid out at the whole tile (= FullMargin), so what's
        /// trimmed is gone from it; 0 when it fills the part that's left.</summary>
        public Thickness ArtMargin { get { return artMargin; } set { Set(ref artMargin, value, "ArtMargin"); } }
        internal string CropKey = "";      // the crop its outline was made for
        internal double FullW, FullH;      // the whole tile's size on screen (cropped: bigger than Width / Height)
        private bool isCropping;
        /// <summary>Being cropped: the part cropped away shows dimmed, a dashed line round the whole tile, a D-pad badge on each edge.</summary>
        public bool IsCropping { get { return isCropping; } set { Set(ref isCropping, value, "IsCropping"); } }
        private Geometry fullClip;
        /// <summary>The whole tile's shape at its whole size (0, 0 at its top left), for the dimmed part while cropping.</summary>
        public Geometry FullClip { get { return fullClip; } set { Set(ref fullClip, value, "FullClip"); } }

        private string name, title, infoText, centerName, pickText;
        public string Name { get { return name; } set { Set(ref name, value, "Name"); } }
        public string Title { get { return title; } set { if (Set(ref title, value, "Title")) { Notify("HasTitle"); Notify("HasInfo"); } } }
        public bool HasTitle { get { return !string.IsNullOrEmpty(title); } }
        public string InfoText { get { return infoText; } set { if (Set(ref infoText, value, "InfoText")) { Notify("HasInfoText"); Notify("HasInfo"); } } }
        public bool HasInfoText { get { return !string.IsNullOrEmpty(infoText); } }
        public bool HasInfo { get { return HasTitle || HasInfoText; } }
        /// <summary>The name over the picture (a "logo on its background" card for a game without a logo).</summary>
        public string CenterName { get { return centerName; } set { if (Set(ref centerName, value, "CenterName")) Notify("HasCenterName"); } }
        public bool HasCenterName { get { return !string.IsNullOrEmpty(centerName); } }

        private ImageSource art, logo;
        public ImageSource Art { get { return art; } set { if (Set(ref art, value, "Art")) Notify("HasArt"); } }
        public bool HasArt { get { return art != null; } }
        public ImageSource Logo { get { return logo; } set { if (Set(ref logo, value, "Logo")) Notify("HasLogo"); } }
        public bool HasLogo { get { return logo != null; } }

        // its shape: made here in code and set as plain values (a Geometry never comes from PluginSettings)
        private bool isCut;
        public bool IsCut { get { return isCut; } set { if (Set(ref isCut, value, "IsCut")) Notify("IsRounded"); } }
        public bool IsRounded { get { return !isCut; } }
        private Geometry clip, outline;
        public Geometry Clip { get { return clip; } set { Set(ref clip, value, "Clip"); } }
        public Geometry Outline { get { return outline; } set { Set(ref outline, value, "Outline"); } }

        private AlignmentX alignX = AlignmentX.Center;
        private AlignmentY alignY = AlignmentY.Center;
        public AlignmentX AlignX { get { return alignX; } set { Set(ref alignX, value, "AlignX"); } }
        public AlignmentY AlignY { get { return alignY; } set { Set(ref alignY, value, "AlignY"); } }

        private double logoMaxW = 200, logoMaxH = 80;
        private VerticalAlignment logoVAlign = VerticalAlignment.Center;
        private Thickness logoMargin = new Thickness(12);
        public double LogoMaxW { get { return logoMaxW; } set { Set(ref logoMaxW, value, "LogoMaxW"); } }
        public double LogoMaxH { get { return logoMaxH; } set { Set(ref logoMaxH, value, "LogoMaxH"); } }
        public VerticalAlignment LogoVAlign { get { return logoVAlign; } set { Set(ref logoVAlign, value, "LogoVAlign"); } }
        public Thickness LogoMargin { get { return logoMargin; } set { Set(ref logoMargin, value, "LogoMargin"); } }

        private bool isPicked, showFrame;
        public bool IsPicked { get { return isPicked; } set { Set(ref isPicked, value, "IsPicked"); } }
        /// <summary>Editing: a dashed frame round each card.</summary>
        public bool ShowFrame { get { return showFrame; } set { Set(ref showFrame, value, "ShowFrame"); } }
        public string PickText { get { return pickText; } set { if (Set(ref pickText, value, "PickText")) Notify("HasPickText"); } }
        public bool HasPickText { get { return !string.IsNullOrEmpty(pickText); } }

        public ICommand Command { get; set; }
        internal bool HiddenByFilter;          // its game is one Playnite's filter hides

        // what kind of tile: a game, a Store game, a shortcut, or a "piece" (logo / info / HLTB / widget / empty: no card of its own)
        internal Action Activate;              // a Store game / shortcut: what A does
        private bool isPiece;
        public bool IsPiece { get { return isPiece; } set { if (Set(ref isPiece, value, "IsPiece")) Notify("IsSolid"); } }
        public bool IsSolid { get { return !isPiece; } }
        private string icon, editLabel, pieceTitle, pieceText;
        /// <summary>A shortcut's icon: store, trophy, gallery, search, shelf, dice.</summary>
        public string Icon { get { return icon; } set { if (Set(ref icon, value, "Icon")) Notify("HasIcon"); } }
        public bool HasIcon { get { return !string.IsNullOrEmpty(icon); } }
        /// <summary>Editing: what a piece is ("Logo", "Widget: Clock"...).</summary>
        public string EditLabel { get { return editLabel; } set { if (Set(ref editLabel, value, "EditLabel")) Notify("HasEditLabel"); } }
        public bool HasEditLabel { get { return !string.IsNullOrEmpty(editLabel); } }
        /// <summary>A game info / HLTB piece: a heading and lines (over the background, no card).</summary>
        public string PieceTitle { get { return pieceTitle; } set { if (Set(ref pieceTitle, value, "PieceTitle")) Notify("HasPieceTitle"); } }
        public bool HasPieceTitle { get { return !string.IsNullOrEmpty(pieceTitle); } }
        public string PieceText { get { return pieceText; } set { if (Set(ref pieceText, value, "PieceText")) Notify("HasPieceText"); } }
        public bool HasPieceText { get { return !string.IsNullOrEmpty(pieceText); } }
        private bool showBack = true, showLabel = true, frameRect, frameCut, pickRect, pickCut;
        private Brush backBrush;
        public bool ShowBack { get { return showBack; } set { Set(ref showBack, value, "ShowBack"); } }
        public Brush BackBrush { get { return backBrush; } set { Set(ref backBrush, value, "BackBrush"); } }
        /// <summary>The icon and name in the middle (shortcuts, libraries; a game's name under its picture).</summary>
        public bool ShowLabel { get { return showLabel; } set { Set(ref showLabel, value, "ShowLabel"); } }
        // editing: the dashed frame follows the tile's shape (a rectangle, or the circle / hexagon / triangle outline)
        public bool FrameRect { get { return frameRect; } set { Set(ref frameRect, value, "FrameRect"); } }
        public bool FrameCut { get { return frameCut; } set { Set(ref frameCut, value, "FrameCut"); } }
        public bool PickRect { get { return pickRect; } set { Set(ref pickRect, value, "PickRect"); } }
        public bool PickCut { get { return pickCut; } set { Set(ref pickCut, value, "PickCut"); } }
        private bool showEditLabel;
        public bool ShowEditLabel { get { return showEditLabel; } set { Set(ref showEditLabel, value, "ShowEditLabel"); } }
        private Visibility tileVisibility = Visibility.Visible;
        public Visibility TileVisibility { get { return tileVisibility; } set { Set(ref tileVisibility, value, "TileVisibility"); } }
        private bool canFocus = true;
        public bool CanFocus { get { return canFocus; } set { Set(ref canFocus, value, "CanFocus"); } }
        // (0.91) Manual board groups, while editing: the group's mark and name
        private bool groupMark;
        public bool GroupMark { get { return groupMark; } set { Set(ref groupMark, value, "GroupMark"); } }
        private string groupText;
        public string GroupText { get { return groupText; } set { Set(ref groupText, value, "GroupText"); } }
        // (0.93) a carousel group's Placement order: the tile's number, big in its middle while editing
        private string orderBadge;
        public string OrderBadge { get { return orderBadge; } set { Set(ref orderBadge, value, "OrderBadge"); Notify("HasOrderBadge"); } }
        public bool HasOrderBadge { get { return !string.IsNullOrEmpty(orderBadge); } }
    }

    /// <summary>One dot of the page indicator.</summary>
    public class BoardDot
    {
        public double DotWidth { get; set; }
        public double DotHeight { get; set; }
        public double DotOpacity { get; set; }
    }

    /// <summary>Which game each card shows on each page (worked out away from the screen; no repeats on a page).</summary>
    public static class BoardPlan
    {
        public static Guid GameOf(BoardCard c)
        {
            Guid g;
            return c != null && !string.IsNullOrEmpty(c.GameId) && Guid.TryParse(c.GameId, out g) ? g : Guid.Empty;
        }

        /// <summary>
        /// Cards are filled in the order they were added (so moving one never changes its game). A pinned card always shows its game (on every page, or
        /// only the first). Cards on the same source take its games in turn, so the next card shows the next game, and the next
        /// page carries on where this one stopped. A game already on the page is skipped. Pages: until every source has run out.
        /// repeat (the Board, 0.85): every page keeps the whole designed layout. A source that runs out before the last page starts
        /// again from its first game (still no repeats on a page), and a first-page-only chosen game's tile shows the next of all
        /// games (Playnite's order, pools["all"]) on the later pages. Manual (repeat false) stays as it was.
        /// </summary>
        public static Dictionary<string, Guid[]> Assign(List<BoardCard> cards, Dictionary<string, List<Guid>> pools, int maxPages, out int pages)
        {
            return Assign(cards, pools, maxPages, false, out pages);
        }

        public static Dictionary<string, Guid[]> Assign(List<BoardCard> cards, Dictionary<string, List<Guid>> pools, int maxPages, bool repeat, out int pages)
        {
            return Assign(cards, pools, maxPages, repeat, false, out pages);
        }

        /// <summary>
        /// (1.0.2) slide: past the edge every tile moves along one game, not a whole page. Each source's tiles (in reading order:
        /// top to bottom, left to right) show a window of its games that moves on by one each step, so a tile takes the game the
        /// tile after it had and one new game comes in at the end. Steps until the last game reaches the last tile.
        /// Paging (not slide, the Board): a game already shown on an earlier page is passed over while its source still has ones
        /// not shown yet, so the next page is all new games (sources overlap: "played" and "installed" games are in "all" too).
        /// </summary>
        public static Dictionary<string, Guid[]> Assign(List<BoardCard> cards, Dictionary<string, List<Guid>> pools, int maxPages, bool repeat, bool slide, out int pages)
        {
            if (slide && repeat) return AssignSlide(cards, pools, maxPages, out pages);
            var ordered = cards.Where(c => c.Kind == "pinned" || c.Kind == "filter").ToList();
            var pinned = ordered.Where(c => c.Kind == "pinned").ToList();
            var fill = ordered.Where(c => c.Kind != "pinned").ToList();
            var pinnedIds = new HashSet<Guid>(pinned.Select(GameOf).Where(g => g != Guid.Empty));
            var queues = new Dictionary<string, List<Guid>>();
            pages = 1;
            foreach (var grp in fill.GroupBy(c => c.Source ?? "all"))
            {
                List<Guid> pool;
                if (!pools.TryGetValue(grp.Key, out pool) || pool == null) pool = new List<Guid>();
                var q = pool.Where(id => !pinnedIds.Contains(id)).Distinct().ToList();
                queues[grp.Key] = q;
                int n = grp.Count();
                pages = Math.Max(pages, (q.Count + n - 1) / n);
            }
            pages = Math.Max(1, Math.Min(pages, Math.Max(1, maxPages)));
            var result = new Dictionary<string, Guid[]>();
            foreach (var c in ordered) result[c.Id] = new Guid[pages];
            var cursor = queues.Keys.ToDictionary(k => k, k => 0);
            int lastFilled = 0;
            // (repeat) what a first-page-only chosen game's spot shows on the later pages: all games, in Playnite's order
            List<Guid> restPool;
            var restQ = repeat && pinned.Any(c => !c.EveryPage) && pools.TryGetValue("all", out restPool) && restPool != null
                        ? restPool.Where(id => !pinnedIds.Contains(id)).Distinct().ToList() : new List<Guid>();
            int restAt = 0;
            var seen = new HashSet<Guid>();   // (1.0.2) on an earlier page
            for (int p = 0; p < pages; p++)
            {
                var used = new HashSet<Guid>();
                foreach (var c in pinned)
                {
                    if (p > 0 && !c.EveryPage) continue;
                    var g = GameOf(c);
                    if (g == Guid.Empty) continue;
                    result[c.Id][p] = g;
                    used.Add(g);
                }
                foreach (var c in fill)
                {
                    string src = c.Source ?? "all";
                    var q = queues[src];
                    int i = cursor[src];
                    if (repeat)
                    {
                        // round again from its first game once it has run out (the layout stays whole on every page)
                        Guid got = TakeFresh(q, ref i, used, seen);
                        if (got != Guid.Empty) { result[c.Id][p] = got; lastFilled = p; }
                        cursor[src] = i;
                        continue;
                    }
                    while (i < q.Count && used.Contains(q[i])) i++;
                    if (i < q.Count) { result[c.Id][p] = q[i]; used.Add(q[i]); i++; lastFilled = p; }
                    cursor[src] = i;
                }
                if (repeat && p > 0 && restQ.Count > 0)
                    foreach (var c in pinned)
                    {
                        if (c.EveryPage || GameOf(c) == Guid.Empty) continue;
                        Guid got = TakeFresh(restQ, ref restAt, used, seen);   // its spot shows the next of all games after the first page
                        if (got != Guid.Empty) result[c.Id][p] = got;
                    }
                foreach (var g in used) seen.Add(g);
            }
            // no empty pages at the end (games skipped as already shown can leave one)
            if (fill.Count > 0 && lastFilled + 1 < pages)
            {
                pages = lastFilled + 1;
                foreach (var k in result.Keys.ToList()) result[k] = result[k].Take(pages).ToArray();
            }
            return result;
        }

        /// <summary>(1.0.2) The next game in q not on this page nor an earlier one; when every game left has been shown, the next not on this page.</summary>
        private static Guid TakeFresh(List<Guid> q, ref int at, HashSet<Guid> used, HashSet<Guid> seen)
        {
            if (q == null || q.Count == 0) return Guid.Empty;
            int start = at < 0 || at >= q.Count ? 0 : at;
            for (int k = start; k < q.Count; k++)
            {
                var g = q[k];
                if (used.Contains(g) || seen.Contains(g)) continue;
                at = k + 1;
                used.Add(g);
                return g;
            }
            return TakeNext(q, ref at, used);   // (all shown already: round again)
        }

        /// <summary>(1.0.2) Slide one at a time: see Assign.</summary>
        private static Dictionary<string, Guid[]> AssignSlide(List<BoardCard> cards, Dictionary<string, List<Guid>> pools, int maxPages, out int pages)
        {
            var ordered = cards.Where(c => c.Kind == "pinned" || c.Kind == "filter").ToList();
            var pinned = ordered.Where(c => c.Kind == "pinned").ToList();
            var fill = ordered.Where(c => c.Kind != "pinned").OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            var pinnedIds = new HashSet<Guid>(pinned.Select(GameOf).Where(g => g != Guid.Empty));
            var groups = new List<Tuple<List<BoardCard>, List<Guid>>>();
            pages = 1;
            foreach (var grp in fill.GroupBy(c => c.Source ?? "all"))
            {
                List<Guid> pool;
                if (!pools.TryGetValue(grp.Key, out pool) || pool == null) pool = new List<Guid>();
                var q = pool.Where(id => !pinnedIds.Contains(id)).Distinct().ToList();
                var list = grp.ToList();
                groups.Add(Tuple.Create(list, q));
                if (q.Count > list.Count) pages = Math.Max(pages, q.Count - list.Count + 1);
            }
            pages = Math.Max(1, Math.Min(pages, Math.Max(1, maxPages)));
            // the source with the most tiles first: its games slide cleanly, and the others pass over what it shows
            groups = groups.OrderByDescending(g => g.Item1.Count).ToList();
            var result = new Dictionary<string, Guid[]>();
            foreach (var c in ordered) result[c.Id] = new Guid[pages];
            for (int p = 0; p < pages; p++)
            {
                var used = new HashSet<Guid>();
                foreach (var c in pinned)
                {
                    if (p > 0 && !c.EveryPage) continue;
                    var g = GameOf(c);
                    if (g == Guid.Empty) continue;
                    result[c.Id][p] = g;
                    used.Add(g);
                }
                foreach (var gr in groups)
                {
                    var q = gr.Item2;
                    if (q.Count == 0) continue;
                    // a source with no more games than tiles stays put; a longer one moves on by one each step (round again at its end)
                    int at = q.Count > gr.Item1.Count ? p % q.Count : 0;
                    foreach (var c in gr.Item1)
                    {
                        Guid got = TakeNext(q, ref at, used);
                        if (got != Guid.Empty) result[c.Id][p] = got;
                    }
                }
            }
            return result;
        }

        /// <summary>The next game in q not already on the page, going round to its start once it has run out (Empty: none left for this page).</summary>
        private static Guid TakeNext(List<Guid> q, ref int at, HashSet<Guid> used)
        {
            if (q == null || q.Count == 0) return Guid.Empty;
            for (int tries = 0; tries < q.Count; tries++)
            {
                if (at >= q.Count || at < 0) at = 0;
                var g = q[at];
                at++;
                if (used.Contains(g)) continue;
                used.Add(g);
                return g;
            }
            return Guid.Empty;
        }
    }


    /// <summary>
    /// Home layouts 4 and 5: the Board and Manual. Tiles placed on a grid, each its own shape, size and picture. A tile is a game
    /// (from a source, taking turns so tiles on the same source show the next games, or one chosen game), a Store top seller, a
    /// shortcut, a widget, or a piece that follows the highlighted game (logo, info, How long to beat); an empty tile keeps a space
    /// clear. More games than tiles: past the edge is the next page. Manual is the same with its own tiles, put down one by one.
    /// Editing: A on an empty square adds a tile (from a list), A on a tile picks it up (D-pad moves it, Y resizes), X its settings,
    /// Y removes it, B done.
    /// </summary>
    public partial class ThemeApi
    {
        private ObservableCollection<BoardTile> boardTiles;
        private ObservableCollection<BoardDot> boardDots;
        private ObservableCollection<PickRow> boardRows, boardCardRows, boardSourceRows, boardAddRows, boardPresetRows;
        public ObservableCollection<BoardTile> BoardTiles { get { return boardTiles ?? (boardTiles = new ObservableCollection<BoardTile>()); } }
        public ObservableCollection<BoardDot> BoardDots { get { return boardDots ?? (boardDots = new ObservableCollection<BoardDot>()); } }
        public ObservableCollection<PickRow> BoardRows { get { return boardRows ?? (boardRows = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> BoardCardRows { get { return boardCardRows ?? (boardCardRows = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> BoardSourceRows { get { return boardSourceRows ?? (boardSourceRows = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> BoardAddRows { get { return boardAddRows ?? (boardAddRows = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> BoardPresetRows { get { return boardPresetRows ?? (boardPresetRows = new ObservableCollection<PickRow>()); } }

        /// <summary>The gap between tiles: Card spacing (Game card settings), a little one for free placement.</summary>
        private double BoardGap { get { return IsFreeMode ? 6 : settings.CardSpacing >= 0 ? settings.CardSpacing : 16; } }
        private const int FreeCols = 96, FreeRows = 49;   // free placement: fine squares (about 18 px)
        private Dictionary<string, Guid[]> boardGames = new Dictionary<string, Guid[]>();
        private int boardPage, boardPages = 1, boardBuildGen;
        private double boardW, boardH;
        private bool boardEditing, boardResizing;
        private int boardUnder;                      // a picked-up tile: how many tiles it would take the place of
        private BoardTile boardPicked;
        private double[] boardPickStart;
        private double boardPickRot;                 // the picked-up tile's turn before (B puts it back)
        private double[] boardPickCrop;              // and its crop (0.81: Start crops it while it's picked up)
        private BoardCard boardClip;                 // copied settings
        private bool boardClipFilter;                // copy what it shows too
        private bool boardPasting;
        private BoardCard boardEditCard;             // the tile whose settings are open
        private bool boardCardFromBoard;
        private BoardCard boardChangeKind;           // "Tile type" from a tile's settings: the list changes it instead of adding one
        private Point boardAddAt;                    // the empty square a new tile goes on
        private List<BoardCard> boardUndo;
        private int boardUndoCols, boardUndoRows;
        private string boardFocusId;                 // the tile that last had the highlight
        private FrameworkElement boardHooked;
        private bool widgetsHooked;
        private object boardFilterHooked;
        private int boardLayoutWas = -1;
        private DispatcherTimer boardRefreshTimer, boardRefillTimer, boardSelectTimer, boardLiveTimer, boardLaterTimer;
        private Guid boardSelectId;
        private HashSet<Guid> boardShown;            // what Playnite's filter shows (only those can be selected)
        private readonly Dictionary<string, int> boardLiveStep = new Dictionary<string, int>();
        private readonly Dictionary<string, ImageSource> boardImages = new Dictionary<string, ImageSource>();
        private static readonly int boardSessionSeed = Environment.TickCount;   // "Random" order: new each time Playnite starts
        private List<StoreItem> boardStore;          // the Store's top sellers (read once a session)
        private bool boardStoreLoading;
        private DateTime boardStoreFailedAt = DateTime.MinValue;

        // ---- which layout: the Board (4th) or Manual (5th), each with its own tiles and grid ----
        /// <summary>The Board (or Manual Board) is on the home screen now (Folder: an opened folder shown as one).</summary>
        public bool IsBoardMode { get { int l = LayoutNow; return l == 3 || l == 4; } }
        /// <summary>
        /// Which board is chosen (3 Board, 4 Manual Board, 0 none): Games shown as, or Folder's "Games in a folder". Its tiles, grid
        /// and settings pages follow this even while the folder tiles show.
        /// </summary>
        private int BoardFlavour
        {
            get
            {
                int m = settings.HomeLayoutMode;
                if (m == 5) m = settings.FolderGameDesign;
                return m == 3 || m == 4 ? m : 0;
            }
        }
        /// <summary>Game cards and layout: the BOARD section (a board is chosen, for the home screen or for a folder's games).</summary>
        public bool BoardSettingsShown { get { return BoardFlavour != 0; } }
        public bool IsBoardOnly { get { return BoardFlavour == 3; } }
        public bool IsManualMode { get { return BoardFlavour == 4; } }
        /// <summary>Manual Board › Free placement: tiles anywhere, any size (no grid, they can overlap).</summary>
        public bool IsFreeMode { get { return IsManualMode && settings.ManualPlacement == 1; } }
        public bool IsManualGrid { get { return IsManualMode && settings.ManualPlacement == 0; } }
        private List<BoardCard> BCards
        {
            get
            {
                if (IsFreeMode) return settings.FreeCards ?? (settings.FreeCards = new List<BoardCard>());
                if (IsManualMode) return settings.ManualCards ?? (settings.ManualCards = new List<BoardCard>());
                return settings.BoardCards ?? (settings.BoardCards = new List<BoardCard>());
            }
            set { if (IsFreeMode) settings.FreeCards = value; else if (IsManualMode) settings.ManualCards = value; else settings.BoardCards = value; }
        }
        private int BCols { get { return IsFreeMode ? FreeCols : Math.Max(1, IsManualMode ? settings.ManualCols : settings.BoardCols); } set { if (IsFreeMode) return; if (IsManualMode) settings.ManualCols = value; else settings.BoardCols = value; } }
        private int BRows { get { return IsFreeMode ? FreeRows : Math.Max(1, IsManualMode ? settings.ManualRows : settings.BoardRows); } set { if (IsFreeMode) return; if (IsManualMode) settings.ManualRows = value; else settings.BoardRows = value; } }
        // (0.99.2) a tile picked up in precise edit moves freely: no squares, steps of the edit grid (Layout › Grid) or 1 px
        private bool boardPrecise, boardPreciseFromBoard;
        private bool BoardPreciseMove { get { return boardPrecise && boardPicked != null; } }
        /// <summary>(0.99.3) The edit grid behind a tile picked up in precise edit (as in precise edit itself).</summary>
        public bool BoardGridShown { get { return boardEditing && BoardPreciseMove; } }
        private double PrecisePx { get { return settings.MasterSnap ? Math.Max(1, settings.MasterGrid) : 1; } }
        private bool BSnap { get { return BoardPreciseMove ? false : IsFreeMode ? true : IsManualMode ? settings.ManualSnap : settings.BoardSnap; } set { if (IsFreeMode) return; if (IsManualMode) settings.ManualSnap = value; else settings.BoardSnap = value; } }
        /// <summary>One step when moving / sizing across (grid squares): a square (snap), a quarter square, one fine square in free placement, or 1 px.</summary>
        private double BStepX { get { if (BoardPreciseMove) return PrecisePx * BCols / BoardAreaW; double s = BoardStepNow; return s > 0 ? s : BCols / BoardAreaW; } }
        /// <summary>The same up / down (1 px is a little less or more of a square that way).</summary>
        private double BStepY { get { if (BoardPreciseMove) return PrecisePx * BRows / BoardAreaH; double s = BoardStepNow; return s > 0 ? s : BRows / BoardAreaH; } }
        /// <summary>Tiles sit on whole grid squares (a grid with Snap on); free placement and Snap off go anywhere.</summary>
        private bool BoardOnSquares { get { return !IsFreeMode && BSnap; } }

        // ---- the step for moving / sizing a picked-up tile: LB / RB while editing (like Edits' grid size) ----
        /// <summary>The "1 px" step (free placement, or a grid with Snap off): one pixel on screen each way.</summary>
        private const double StepOnePx = 0;
        private int boardStepIdx = -1;   // -1: as it comes (a square, or a quarter with Snap off; free placement one fine square)
        private double[] StepChoices
        {
            get
            {
                if (IsFreeMode) return new[] { StepOnePx, 0.25, 0.5, 1, 2, 4 };
                return BSnap ? new[] { 0.25, 0.5, 1 } : new[] { StepOnePx, 0.25, 0.5, 1 };
            }
        }
        /// <summary>The step chosen, in grid squares (StepOnePx: 1 px, see BStepX / BStepY).</summary>
        private double BoardStepNow
        {
            get
            {
                var ch = StepChoices;
                if (boardStepIdx >= 0) return ch[Math.Min(boardStepIdx, ch.Length - 1)];
                return IsFreeMode ? 1 : BSnap ? 1 : 0.25;
            }
        }
        private void BoardChangeStep(int dir)
        {
            if (BoardPreciseMove) { MasterStepSize(dir); if (!settings.MasterSnap) { MasterSnap = true; } Notify("BoardHintText"); return; }   // (0.99.2: the edit grid's size)
            var ch = StepChoices;
            int i = Array.IndexOf(ch, BoardStepNow);
            if (i < 0) i = 0;
            int ni = Math.Max(0, Math.Min(ch.Length - 1, i + dir));
            if (ni == i) { PlaySound("back"); return; }
            boardStepIdx = ni;
            PlaySound("move");
            Notify("BoardHintText");
        }
        private string BoardStepText
        {
            get
            {
                if (BoardPreciseMove) return string.Format(Loc("LOC_KX_StepPxIs", "step {0} px"), PrecisePx);
                double s = BoardStepNow;
                double px = s > 0 ? s * BoardAreaW / BCols : 1;
                return string.Format(Loc("LOC_KX_StepPxIs", "step {0} px"), Math.Max(1, Math.Round(px)));
            }
        }

        // ---- Manual Board pages ----
        private string boardPageId = "home";
        private List<BoardPage> BPages
        {
            get
            {
                if (!IsManualMode) return null;
                var list = IsFreeMode ? (settings.FreePages ?? (settings.FreePages = new List<BoardPage>())) : (settings.ManualPages ?? (settings.ManualPages = new List<BoardPage>()));
                if (!list.Any(p => p.Id == "home")) list.Insert(0, new BoardPage { Id = "home", Name = Loc("LOC_KX_PageHome", "Home"), X = 0, Y = 0 });
                return list;
            }
        }
        private BoardPage CurrentPage
        {
            get
            {
                var pages = BPages;
                if (pages == null) return null;
                var p = pages.FirstOrDefault(x => x.Id == boardPageId);
                if (p == null) { boardPageId = "home"; p = pages.First(x => x.Id == "home"); }
                return p;
            }
        }
        private static string PageOf(BoardCard c) { return string.IsNullOrEmpty(c.Page) ? "home" : c.Page; }
        /// <summary>The tiles inside the grid (a smaller grid hides the ones past its edge; they come back when it grows).</summary>
        private List<BoardCard> ShownCards
        {
            get
            {
                int cols = BCols, rows = BRows;
                string page = IsManualMode && CurrentPage != null ? CurrentPage.Id : null;
                double mx = IsManualMode ? BMaxX : cols, my = IsManualMode ? BMaxY : rows;
                return BCards.Where(c => c.X < mx - 0.01 && c.Y < my - 0.01 && (page == null || PageOf(c) == page)).ToList();
            }
        }

        public bool BoardEditing { get { return boardEditing && IsBoardMode; } }
        private bool ManualPaged { get { return IsManualMode && BPages != null && BPages.Count > 1; } }
        public bool BoardShowPages { get { return IsBoardMode && (boardPages > 1 || ManualPaged) && !boardEditing && settings.BoardIndicator != 2; } }   // (2: Hidden)
        public bool BoardPageDots { get { return !ManualPaged && settings.BoardIndicator == 0 && boardPages <= 24; } }
        public bool BoardPageNumber { get { return ManualPaged || settings.BoardIndicator == 1 || boardPages > 24; } }
        public bool BoardDotsAcross { get { return BoardPageDots && settings.BoardPageAxis != 1; } }
        public bool BoardDotsDown { get { return BoardPageDots && settings.BoardPageAxis == 1; } }
        public string BoardPageText
        {
            get
            {
                if (ManualPaged)
                {
                    var p = CurrentPage;
                    string games = boardPages > 1 ? "   ·   " + string.Format("{0} / {1}", boardPage + 1, boardPages) : "";
                    return (p != null ? p.Name : "") + games;
                }
                return string.Format("{0} / {1}", boardPage + 1, boardPages);
            }
        }
        public HorizontalAlignment BoardIndicatorHAlign { get { return settings.BoardPageAxis == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center; } }
        public VerticalAlignment BoardIndicatorVAlign { get { return settings.BoardPageAxis == 1 ? VerticalAlignment.Center : VerticalAlignment.Bottom; } }
        public Thickness BoardIndicatorMargin { get { return settings.BoardPageAxis == 1 ? new Thickness(0, 0, -50, 0) : new Thickness(0, 0, 0, -46); } }
        public bool IsOptionsBoard { get { return optionsPage == "board"; } }
        public bool IsOptionsBoardCard { get { return optionsPage == "boardcard"; } }
        public bool IsOptionsBoardSource { get { return optionsPage == "boardsource"; } }
        public bool IsOptionsBoardAdd { get { return optionsPage == "boardadd"; } }
        public bool IsOptionsBoardPresets { get { return optionsPage == "boardpresets"; } }
        public bool IsOptionsBoardPages { get { return optionsPage == "boardpages"; } }
        public bool IsOptionsBoardLibrary { get { return optionsPage == "boardlibrary"; } }
        private ObservableCollection<PickRow> boardLibraryRows;
        public ObservableCollection<PickRow> BoardLibraryRows { get { return boardLibraryRows ?? (boardLibraryRows = new ObservableCollection<PickRow>()); } }
        private string boardAddCat;                  // Add a tile: the category open (null: the list of categories)
        private BoardCard boardSizeCard;             // a tile sized with its page's sliders...
        private double[] boardSizeBefore;            // ...and where / how big it was before (B puts it back)
        public bool IsOptionsBoardPage { get { return optionsPage == "boardpage"; } }
        private ObservableCollection<PickRow> boardPagesRows, boardPageRows;
        public ObservableCollection<PickRow> BoardPagesRows { get { return boardPagesRows ?? (boardPagesRows = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> BoardPageRows { get { return boardPageRows ?? (boardPageRows = new ObservableCollection<PickRow>()); } }
        private string boardEditPageId;   // the page whose settings are open
        public string BoardSummary { get { return string.Format(Loc("LOC_KX_BoardSummary", "{0} tiles on a {1} × {2} grid"), BCards.Count, BCols, BRows); } }
        public string BoardSectionTitle { get { return IsManualMode ? Loc("LOC_KX_SecManualBoard", "MANUAL BOARD") : Loc("LOC_KX_SecBoard", "BOARD"); } }
        public string BoardSettingsText { get { return IsManualMode ? Loc("LOC_KX_ManualBoardSettingsRow", "Manual board settings") : Loc("LOC_KX_BoardSettingsRow", "Board settings"); } }
        public string BoardHelpText
        {
            get
            {
                if (IsFreeMode) return Loc("LOC_KX_FreeHelp", "Manual board, free placement: tiles go anywhere, any size. Edit: A on \"+ Add a tile\" chooses what it is (a game, a widget, a shortcut, the logo…); it then sits picked up: the D-pad moves it, Y switches to sizing, A puts it down. X on a tile: its settings (width and height sliders); Y removes it; B done.");
                return IsManualMode
                    ? Loc("LOC_KX_ManualHelp2", "Manual board, on a grid: put tiles down yourself and choose the game (or widget, shortcut, logo…) for each. Edit: A on an empty square adds a tile, A on a tile picks it up (D-pad moves it, Y resizes), X its settings, Y removes it, B done.")
                    : Loc("LOC_KX_BoardHelp3", "The Board puts your games on tiles you place yourself: any size and shape, each showing games from a list (Recently played, a collection, a saved filter…), a game you choose, a widget, a shortcut, or the highlighted game's logo or info. More games than tiles: go past the edge for the next page. Edit: A on an empty square adds a tile, A on a tile picks it up (D-pad moves it, Y resizes), X its settings, Y removes it, B done.");
            }
        }

        public string BoardHintText
        {
            get
            {
                // (the line over the hints: what's going on; the buttons are the hints themselves, BoardHints in ThemeApi.EditHints.cs)
                if (!boardEditing) return "";
                if (cropTile != null) return CropStatusText();
                if (boardPicked != null && PickModeActive) return PickModeText();   // Menu: Layer / Opacity
                if (boardPicked != null)
                    return (boardResizing ? Loc("LOC_KX_BoardResizingTag", "Resizing") : Loc("LOC_KX_BoardMovingTag", "Moving")) + "   ·   " + BoardStepText
                           + (boardPicked.Card != null ? TurnSuffix(boardPicked.Card.Rotation) : "");
                if (layerNote != null && layerNoteTarget != null && boardGrouping == null && !boardPasting)
                {
                    var lt = FocusedBoardTile();
                    if (lt != null && lt.Card != null && "b:" + lt.Card.Id == layerNoteTarget) return layerNote;   // (0.99.2)
                }
                if (boardPasting) return Loc("LOC_KX_BoardStatusPaste", "Paste to…   ·   A on each tile that takes the copied settings");
                if (boardGrouping != null && boardOrdering) return string.Format(Loc("LOC_KX_BoardStatusOrder", "{0}   ·   A on the tiles in the order new games come in"), GroupName(MGroups.FirstOrDefault(g => g != null && g.Id == boardGrouping)));
                if (boardGrouping != null) return string.Format(Loc("LOC_KX_BoardStatusGroup", "{0}   ·   A puts a tile in or takes it out"), GroupName(MGroups.FirstOrDefault(g => g != null && g.Id == boardGrouping)));
                string note = HistoryNoteNow;
                if (note != null) return note;
                return IsManualMode ? Loc("LOC_KX_BoardStatusManual", "Editing the Manual board") : Loc("LOC_KX_BoardStatusEdit", "Editing the board");
            }
        }

        /// <summary>Moving a tile over others: the warning after the hint (bold, red, capitals).</summary>
        public string BoardHintWarn
        {
            get
            {
                if (!boardEditing || boardPicked == null || boardUnder <= 0) return "";
                string n = boardUnder == 1 ? Loc("LOC_KX_OneTile", "1 tile") : string.Format(Loc("LOC_KX_NTiles", "{0} tiles"), boardUnder);
                return "(" + string.Format(Loc("LOC_KX_BoardUnderWarn", "takes the place of {0}"), n).ToUpperInvariant() + ")";
            }
        }
        public bool HasBoardHintWarn { get { return BoardHintWarn.Length > 0; } }

        private ICommand openBoardCmd, editBoardCmd;
        public ICommand OpenBoardSettingsCommand
        {
            get { return openBoardCmd ?? (openBoardCmd = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("board"); })); }
        }
        public ICommand EditBoardCommand
        {
            get
            {
                return editBoardCmd ?? (editBoardCmd = new RelayCommand(_ =>
                {
                    if (!IsBoardMode) return;
                    PlaySound("select");
                    FolderPreviewKeep();   // (Folder: the board of the folder showing behind the settings is the one edited)
                    ClosePanels();
                    ui.BeginInvoke(new Action(() => BoardStartEdit(null)), DispatcherPriority.ApplicationIdle);
                }));
            }
        }

        private void NotifyBoard()
        {
            foreach (string n in new[] { "IsBoardMode", "BoardSettingsShown", "IsBoardOnly", "IsManualMode", "BoardEditing", "BoardShowPages", "BoardPageDots", "BoardPageNumber", "BoardPageText", "BoardDotsAcross", "BoardDotsDown",
                                         "BoardIndicatorHAlign", "BoardIndicatorVAlign", "BoardIndicatorMargin", "BoardHintText", "BoardHintWarn", "HasBoardHintWarn", "BoardSummary", "BoardSectionTitle", "BoardSettingsText",
                                         "BoardHelpText", "FilterAbove", "BoardGridShown" })
                Notify(n);
        }

        private static bool IsGameKind(BoardCard c) { return c != null && (c.Kind == "filter" || c.Kind == "pinned"); }
        private static bool IsPieceKind(BoardCard c) { return c != null && (c.Kind == "logo" || c.Kind == "info" || c.Kind == "hltb" || c.Kind == "widget" || c.Kind == "empty"); }

        // =====================================================================
        // Switching to / from the Board, and keeping it up to date
        // =====================================================================

        /// <summary>The Board takes the place of the cover row (and the logo / info above it, and the tiles before the games).</summary>
        private void BoardApplyMode()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                if (win == null || !IsFullscreen) return;
                bool on = IsBoardMode;
                foreach (string n in new[] { "PART_ListGameItems", "KX_GameInfo", "KX_HomeTilesCanvas" })
                {
                    var el = MasterFind(win, n);
                    if (el == null) continue;
                    // (Folder: the folder tiles aren't games, so no game info / button hints under them; Row › Tall / Full Screen:
                    // the cards have the screen unless Row's "Show the logo, game info and hints" is on)
                    bool hide = on || (n == "KX_GameInfo" && (CarHidesInfo || LibraryOpen));   // (the library view has its own one-line header)
                    if (hide) { if (el.Visibility != Visibility.Collapsed) { el.Visibility = Visibility.Collapsed; if (on) ui.BeginInvoke(new Action(BoardFocusIfLost), DispatcherPriority.ContextIdle); } }
                    else if (el.ReadLocalValue(UIElement.VisibilityProperty) != DependencyProperty.UnsetValue) el.ClearValue(UIElement.VisibilityProperty);
                }
                var board = MasterFind(win, "KX_Board");
                if (board != null && !ReferenceEquals(board, boardHooked))
                {
                    boardHooked = board;
                    board.SizeChanged += (s, e) => { boardW = board.ActualWidth; boardH = board.ActualHeight; BoardMeasureEdges(board); BoardPlaceAll(); BoardRefillSoon(); };
                    board.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) => BoardFocusChanged(e.NewFocus as FrameworkElement)), true);
                    boardW = board.ActualWidth;
                    boardH = board.ActualHeight;
                    BoardMeasureEdges(board);
                }
                // Playnite's filter changed (the games on screen): the pages again, without waiting for a page turn
                var filter = PlayniteFilterSettings() as System.ComponentModel.INotifyPropertyChanged;
                if (filter != null && !ReferenceEquals(filter, boardFilterHooked))
                {
                    boardFilterHooked = filter;
                    filter.PropertyChanged += (s, e) => { if (IsBoardMode) BoardRefreshLater(); if (e.PropertyName == "Name") OnUi(HomeSearchChanged); };
                    HomeSearchChanged();
                }
                if (!widgetsHooked && Widgets != null)
                {
                    widgetsHooked = true;
                    Widgets.CollectionChanged += (s, e) => BoardPlaceWidgetsSoon();   // a widget added / removed
                }
                if (on && BoardFlavour != boardLayoutWas && boardLayoutWas != -1)
                {
                    // Board ↔ Manual: the other set of tiles, from its first page
                    if (boardEditing) BoardEndEdit(false);
                    boardPage = 0;
                    boardPageId = "home";
                    boardGames = new Dictionary<string, Guid[]>();
                    boardFocusId = null;
                    if (BoardTiles.Count > 0) BoardTiles.Clear();
                }
                boardLayoutWas = on ? BoardFlavour : -1;
                if (!on)
                {
                    if (boardEditing) BoardEndEdit(false);
                    if (BoardTiles.Count > 0) BoardTiles.Clear();
                    if (boardLiveTimer != null) boardLiveTimer.Stop();
                    BoardPlaceWidgets();
                    return;
                }
                if (BoardTiles.Count == 0) BoardRefreshSoon();
            }
            catch (Exception ex) { logger.Warn("Kippix: board: " + ex.Message); }
        }

        /// <summary>Works out the pages again (tiles / sources / the library changed), shortly.</summary>
        private void BoardRefreshSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (boardRefreshTimer == null)
                {
                    boardRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
                    boardRefreshTimer.Tick += (s, e) => { boardRefreshTimer.Stop(); BoardRebuild(); };
                }
                boardRefreshTimer.Stop();
                boardRefreshTimer.Start();
            }));
        }

        /// <summary>Playnite's list changed (filter, sort, a game added): the pages again once it has settled.</summary>
        private void BoardRefreshLater()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (boardLaterTimer == null)
                {
                    boardLaterTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(0.9) };
                    boardLaterTimer.Tick += (s, e) => { boardLaterTimer.Stop(); if (!IsBoardMode) return; if (boardPicked != null) { boardLaterTimer.Start(); return; } BoardRebuild(); };
                }
                boardLaterTimer.Stop();
                boardLaterTimer.Start();
            }));
        }

        /// <summary>Fills the tiles on this page again (art / info changed), shortly.</summary>
        private void BoardRefillSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (boardRefillTimer == null)
                {
                    boardRefillTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(400) };
                    boardRefillTimer.Tick += (s, e) => { boardRefillTimer.Stop(); if (IsBoardMode) BoardFillPage(); };
                }
                boardRefillTimer.Stop();
                boardRefillTimer.Start();
            }));
        }

        /// <summary>The Board's starting tiles (once: a board you empty stays empty). Manual starts empty.</summary>
        private void EnsureDefaultBoard()
        {
            var list = BCards;
            list.RemoveAll(c => c == null);
            var ids = new HashSet<string>();
            bool changed = false;
            foreach (var c in list)
                if (string.IsNullOrEmpty(c.Id) || !ids.Add(c.Id)) { c.Id = Guid.NewGuid().ToString("N"); ids.Add(c.Id); changed = true; }   // every tile its own id
            if (!IsManualMode && list.Count == 0 && !settings.BoardSeeded)
            {
                settings.BoardCols = 5;
                settings.BoardRows = 3;
                list.Add(new BoardCard { Source = "played", Shape = CardShapes.Square, X = 0, Y = 0, W = 2, H = 2, Info = new List<string> { "name", "playtime", "lastplayed" } });
                for (int x = 2; x < 5; x++) list.Add(new BoardCard { Source = "played", Shape = CardShapes.Square, X = x, Y = 0 });
                for (int x = 2; x < 5; x++) list.Add(new BoardCard { Source = "favourites", Shape = CardShapes.Square, X = x, Y = 1 });
                list.Add(new BoardCard { Source = "recent", Shape = CardShapes.Wide, X = 0, Y = 2, W = 2, H = 1, Info = new List<string> { "name" } });
                for (int x = 2; x < 5; x++) list.Add(new BoardCard { Source = "installed", Shape = CardShapes.Square, X = x, Y = 2 });
                changed = true;
            }
            if (!IsManualMode && !settings.BoardSeeded) { settings.BoardSeeded = true; changed = true; }
            if (changed) SaveSettings();
        }

        private void BoardRebuild()
        {
            try { BoardRebuildNow(); }
            catch (Exception ex) { logger.Warn("Kippix: board: " + ex.Message); }
        }

        /// <summary>Which games each tile shows on each page (the library is read on a worker thread).</summary>
        private void BoardRebuildNow()
        {
            if (!IsFullscreen || ui == null) return;
            if (!IsBoardMode) { if (BoardTiles.Count > 0) BoardTiles.Clear(); return; }
            EnsureDefaultBoard();
            var cards = ShownCards.Select(c => c.Snapshot()).ToList();
            var keys = cards.Where(c => c.Kind == "filter").Select(c => c.Source ?? "all").Distinct().ToList();
            foreach (string gk in GroupSources()) if (!keys.Contains(gk)) keys.Add(gk);   // (0.91) the carousel groups' lists
            var planCards = cards.Where(c => !IsCarouselCard(c)).ToList();               // (their tiles take games from the group, not in turn)
            bool repeat = !IsManualMode;   // the Board: its layout on every page (Manual: as it was)
            if (repeat && !keys.Contains("all") && cards.Any(c => c.Kind == "pinned" && !c.EveryPage)) keys.Add("all");   // (a first-page-only game's spot after the first page)
            var shown = GamesInView();
            boardShown = shown;
            List<Guid> order = null;
            try
            {
                var list = UiTools.FindByName(Application.Current.MainWindow, "PART_ListGameItems") as ItemsControl;
                if (list != null) { order = new List<Guid>(); foreach (var it in list.Items) { Guid id = EntryId(it); if (id != Guid.Empty) order.Add(id); } }
            }
            catch (Exception) { order = null; }
            var shelves = new Dictionary<string, GameShelf>();
            foreach (string k in keys.Where(k => k.StartsWith("shelf:", StringComparison.Ordinal)))
            {
                var found = ShelfById(k.Substring(6));
                if (found != null) shelves[k] = new GameShelf { Id = found.Id, Name = found.Name, Games = found.Games.ToList() };
            }
            if (cards.Any(c => c.Kind == "store")) BoardLoadStore();
            BoardSyncTiles();   // the tiles are there at once (their games follow)
            int gen = ++boardBuildGen;
            KippixShared.SafeWork.Queue(_ =>
            {
                Dictionary<string, Guid[]> plan = null;
                Dictionary<string, List<Guid>> pools = null;
                int pages = 1;
                try
                {
                    var all = api.Database.Games.Where(g => g != null && !g.Hidden && ProfileAllowsGame(g) && !CopyBehind(g.Id)).ToList();
                    pools = new Dictionary<string, List<Guid>>();
                    foreach (string k in keys) pools[k] = BoardSourceGames(k, all, shown, order, shelves);
                    bool slide = repeat && settings.BoardSlideOne;   // (1.0.2) one game at a time
                    plan = BoardPlan.Assign(planCards, pools, slide ? 3000 : 300, repeat, slide, out pages);
                }
                catch (Exception ex) { logger.Warn("Kippix: board games: " + ex.Message); }
                OnUi(() =>
                {
                    if (gen != boardBuildGen || plan == null) return;
                    boardGames = plan;
                    groupGames = GroupGamesFrom(pools);
                    ClampGroupOffsets();   // (fewer games now: the carousel comes back to where there are some)
                    boardPages = pages;
                    if (boardPage >= boardPages) boardPage = boardPages - 1;
                    if (boardPage < 0) boardPage = 0;
                    BoardSyncTiles();
                    BoardFillPage();
                    BoardMakeDots();
                    NotifyBoard();
                    BoardLiveTimer();
                    if (boardHooked == null) BoardApplyMode();
                    ui.BeginInvoke(new Action(BoardFocusIfLost), DispatcherPriority.ContextIdle);
                });
            });
        }

        /// <summary>A place in "Random" order: shuffled once each time Playnite starts (the same all session).</summary>
        private static int SessionRank(Guid id) { return (id.GetHashCode() ^ boardSessionSeed) * 16777619; }

        private List<Guid> BoardSourceGames(string key, List<Game> all, HashSet<Guid> shown, List<Guid> order, Dictionary<string, GameShelf> shelves)
        {
            IEnumerable<Game> games;
            switch (key)
            {
                case "all":
                    if (order != null && order.Count > 0) return order.ToList();   // Playnite's own order (its sort and filter)
                    games = all.OrderBy(g => g.SortingName ?? g.Name);
                    break;
                case "random": games = all.OrderBy(g => SessionRank(g.Id)); break;
                case "played": games = all.Where(g => LastPlayedNow(g).HasValue).OrderByDescending(g => LastPlayedNow(g) ?? DateTime.MinValue); break;   // (the profile can change between the two reads)
                case "favourites": games = all.Where(g => IsFavouriteNow(g)).OrderByDescending(g => LastPlayedNow(g) ?? DateTime.MinValue); break;
                case "recent": games = all.Where(g => g.Added.HasValue).OrderByDescending(g => g.Added.Value); break;
                case "installed": games = all.Where(g => g.IsInstalled).OrderByDescending(g => g.LastActivity ?? DateTime.MinValue); break;
                case "never": games = all.Where(g => g.Playtime == 0).OrderByDescending(g => g.Added ?? DateTime.MinValue); break;
                default:
                    {
                        games = Enumerable.Empty<Game>();
                        if (key.StartsWith("shelf:", StringComparison.Ordinal))
                        {
                            GameShelf s;
                            if (shelves.TryGetValue(key, out s)) games = ShelfGames(s);
                        }
                        else if (key.StartsWith("filter:", StringComparison.Ordinal))
                        {
                            Guid id;
                            if (Guid.TryParse(key.Substring(7), out id))
                            {
                                var p = api.Database.FilterPresets.Get(id);
                                if (p != null && p.Settings != null) games = api.Database.GetFilteredGames(p.Settings).Where(g => !g.Hidden).OrderBy(g => g.SortingName ?? g.Name).ToList();
                            }
                        }
                        break;
                    }
            }
            var ids = games.Select(g => g.Id);
            if (shown != null) ids = ids.Where(shown.Contains);   // Playnite can only select what its filter shows
            return ids.ToList();
        }

        private string BoardSourceName(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "all") return Loc("LOC_KX_SrcAll", "All games (Playnite's order)");
            if (key == "random") return Loc("LOC_KX_SrcRandom", "Random (new order each time Playnite starts)");
            if (key == "played") return Loc("LOC_KX_SrcPlayed", "Recently played");
            return ExtraRowName(key) ?? Loc("LOC_KX_SrcGone", "(not there any more)");
        }

        /// <summary>The Store's top sellers, read once a session (for "Popular in the store" tiles).</summary>
        private void BoardLoadStore()
        {
            if (boardStore != null || boardStoreLoading || (DateTime.Now - boardStoreFailedAt).TotalSeconds < 60) return;
            boardStoreLoading = true;
            KippixShared.SafeWork.Queue(_ =>
            {
                List<StoreItem> items = null;
                try { items = SteamBrowse(new StoreQuery { Sort = "topsellers" }, 0).Items.Where(i => i != null && !string.IsNullOrEmpty(i.Name)).Take(60).ToList(); }
                catch (Exception ex) { logger.Warn("Kippix: board store: " + ex.Message); }
                OnUi(() =>
                {
                    boardStoreLoading = false;
                    boardStore = items;   // (null: tried again in a minute)
                    if (items == null) boardStoreFailedAt = DateTime.Now;
                    if (IsBoardMode) BoardFillPage();
                });
            });
        }

        // =====================================================================
        // Tiles: one per card (and the empty squares while editing), placed on the grid
        // =====================================================================

        private void BoardSyncTiles()
        {
            var cards = ShownCards;
            var want = new List<BoardTile>();
            var byId = new Dictionary<string, BoardTile>();
            var slots = new Dictionary<string, BoardTile>();
            foreach (var t in BoardTiles)
            {
                if (t.Card != null) byId[t.Card.Id] = t;
                else slots[t.SlotX + "," + t.SlotY] = t;
            }
            if (!boardEditing && IsManualMode && cards.Count == 0)
            {
                // an empty Manual Board page: one square to land on (A: start filling it)
                BoardTile empty;
                if (!slots.TryGetValue("-2,-2", out empty))
                {
                    empty = new BoardTile { IsSlot = true, SlotX = -2, SlotY = -2, Name = "+" };
                    empty.EditLabel = Loc("LOC_KX_EmptyPage", "An empty page");
                    var et = empty;
                    empty.Command = new RelayCommand(_ => BoardTileCommand(et));
                }
                want.Add(empty);
            }
            if (boardEditing && IsFreeMode)
            {
                // free placement: no squares, one "+ Add a tile" in the bottom-right corner
                BoardTile add;
                if (!slots.TryGetValue("-1,-1", out add))
                {
                    add = new BoardTile { IsSlot = true, SlotX = -1, SlotY = -1, Name = "+" };
                    add.EditLabel = Loc("LOC_KX_AddTileTitle", "Add a tile");
                    var at = add;
                    add.Command = new RelayCommand(_ => BoardTileCommand(at));
                }
                want.Add(add);
            }
            else if (boardEditing)   // (the empty squares only show while editing)
            {
                for (int y = 0; y < BRows; y++)
                    for (int x = 0; x < BCols; x++)
                    {
                        var cell = new Rect(x, y, 1, 1);
                        if (cards.Any(c => Overlaps(cell, BoardCells(c)))) continue;
                        BoardTile s;
                        if (!slots.TryGetValue(x + "," + y, out s))
                        {
                            s = new BoardTile { IsSlot = true, SlotX = x, SlotY = y, Name = "+" };
                            var st = s;
                            s.Command = new RelayCommand(_ => BoardTileCommand(st));
                        }
                        want.Add(s);
                    }
            }
            foreach (var c in cards)
            {
                BoardTile t;
                if (!byId.TryGetValue(c.Id, out t))
                {
                    t = new BoardTile { Card = c };
                    var tt = t;
                    t.Command = new RelayCommand(_ => BoardTileCommand(tt));
                }
                t.Card = c;   // (a profile loaded: a new card object with the same id)
                want.Add(t);
            }
            // keep the tiles that stay (their highlight stays with them), take away the rest, add the new ones in order
            for (int i = BoardTiles.Count - 1; i >= 0; i--) if (!want.Contains(BoardTiles[i])) BoardTiles.RemoveAt(i);
            for (int i = 0; i < want.Count; i++)
            {
                int at = BoardTiles.IndexOf(want[i]);
                if (at == i) continue;
                if (at >= 0) BoardTiles.Move(at, i); else BoardTiles.Insert(i, want[i]);
            }
            if (boardPicked != null && !BoardTiles.Contains(boardPicked)) boardPicked = null;
            BoardPlaceAll();
        }

        private static bool Overlaps(Rect a, Rect b)
        {
            double w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
            double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            return w > 0.02 && h > 0.02;
        }

        // ---- 0.82: the Manual board's tiles go right up to the screen's edges (past the board's own margins and under the top
        // bar); the grid (its squares, the empty squares, Snap) stays where it was, so nothing already placed moves ----
        private double edgeL, edgeT, edgeR, edgeB;   // the board's area to the window's edges, in the board's own pixels

        private void BoardMeasureEdges(FrameworkElement board)
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var root = win != null ? win.Content as FrameworkElement : null;
                if (root == null || board == null || board.ActualWidth < 50 || !root.IsAncestorOf(board)) return;
                var tl = root.TranslatePoint(new Point(0, 0), board);
                var br = root.TranslatePoint(new Point(root.ActualWidth, root.ActualHeight), board);
                edgeL = Math.Max(0, -tl.X); edgeT = Math.Max(0, -tl.Y);
                edgeR = Math.Max(0, br.X - board.ActualWidth); edgeB = Math.Max(0, br.Y - board.ActualHeight);
            }
            catch (Exception) { }
        }

        /// <summary>How far tiles can go (grid squares): the Manual board to the screen's edges (the gap between tiles taken off, so a
        /// tile's edge can touch the screen's), the others the grid.</summary>
        private double BMinX { get { return IsManualMode ? -(edgeL + BoardGap / 2) / (BoardAreaW / BCols) : 0; } }
        private double BMinY { get { return IsManualMode ? -(edgeT + BoardGap / 2) / (BoardAreaH / BRows) : 0; } }
        private double BMaxX { get { return IsManualMode ? BCols + (edgeR + BoardGap / 2) / (BoardAreaW / BCols) : BCols; } }
        private double BMaxY { get { return IsManualMode ? BRows + (edgeB + BoardGap / 2) / (BoardAreaH / BRows) : BRows; } }

        private double BoardAreaW { get { return boardW > 50 ? boardW : 1776; } }
        private double BoardAreaH { get { return boardH > 50 ? boardH : 900; } }

        /// <summary>A tile's box on screen (grid squares to pixels, with a gap between tiles).</summary>
        private Rect BoardRect(double x, double y, double w, double h)
        {
            double cw = BoardAreaW / BCols, ch = BoardAreaH / BRows;
            return new Rect(x * cw + BoardGap / 2, y * ch + BoardGap / 2, Math.Max(16, w * cw - BoardGap), Math.Max(16, h * ch - BoardGap));
        }

        /// <summary>Where a tile is in grid squares, kept inside the grid (a smaller grid doesn't change the tile itself).</summary>
        private Rect BoardCells(BoardCard c)
        {
            double x0 = BMinX, y0 = BMinY, x1 = BMaxX, y1 = BMaxY;
            double w = Math.Max(0.25, Math.Min(c.W, x1 - x0)), h = Math.Max(0.25, Math.Min(c.H, y1 - y0));
            double x = Math.Max(x0, Math.Min(c.X, x1 - w)), y = Math.Max(y0, Math.Min(c.Y, y1 - h));
            return new Rect(x, y, w, h);
        }

        /// <summary>The cut shapes keep their own proportions, centred in the tile's box (pieces use the whole box).</summary>
        private Rect BoardShapeRect(BoardCard c)
        {
            return BoardShapeRectOf(BoardCells(c), c);   // (ThemeApi.BoardCrop.cs)
        }

        private void BoardPlaceAll()
        {
            foreach (var t in BoardTiles) BoardPlace(t);
            BoardPlaceWidgetsSoon();
        }

        private void BoardPlace(BoardTile t)
        {
            Rect r;
            int s;
            bool piece = !t.IsSlot && IsPieceKind(t.Card);
            if (t.IsSlot && t.SlotX == -2) { r = IsFreeMode ? BoardRect((FreeCols - 12) / 2.0, (FreeRows - 12) / 2.0, 12, 12) : BoardRect((BCols - 1) / 2.0, (BRows - 1) / 2.0, 1, 1); s = CardShapes.Square; }   // an empty page
            else if (t.IsSlot && t.SlotX < 0) { r = BoardRect(FreeCols - 8, FreeRows - 8, 8, 8); s = CardShapes.Square; }   // free placement's "+ Add a tile"
            else if (t.IsSlot) { r = BoardRect(t.SlotX, t.SlotY, 1, 1); s = CardShapes.Square; }
            else { r = BoardShapeRect(t.Card); s = piece || t.Card.Kind == "shortcut" || t.Card.Kind == "library" ? CardShapes.Square : t.Card.Shape; }
            // Manual board: cropped, the tile is the part of it that's left (X / Y / W / H); its picture fills that part (centred
            // on it, uniform to fill), and the shape is the whole shape cut by the crop (ThemeApi.BoardCrop.cs)
            Rect full = r, vis = r;
            bool cropped = !t.IsSlot && t.Card != null && BoardCropRects(t.Card, out full, out vis);
            if (cropped) r = vis; else full = r;
            t.Left = Math.Round(r.X);
            t.Top = Math.Round(r.Y);
            double w = Math.Round(r.Width), h = Math.Round(r.Height);
            double fullW = cropped ? Math.Max(w, Math.Round(full.Width)) : w, fullH = cropped ? Math.Max(h, Math.Round(full.Height)) : h;
            double offL = cropped ? Math.Max(0, t.Left - Math.Round(full.X)) : 0, offT = cropped ? Math.Max(0, t.Top - Math.Round(full.Y)) : 0;
            t.FullW = fullW;
            t.FullH = fullH;
            string cropKey = cropped ? offL + "," + offT + "," + fullW + "," + fullH : "";
            bool resized = Math.Abs(t.Width - w) > 0.5 || Math.Abs(t.Height - h) > 0.5 || t.Clip == null || t.CropKey != cropKey;
            t.Width = w;
            t.Height = h;
            t.Rotation = t.IsSlot || t.Card == null ? 0 : NormAngle(t.Card.Rotation);
            t.CardOpacity = t.IsSlot || t.Card == null ? 1 : CleanOpacity(t.Card.Opacity);
            bool cut = CardShapes.IsCut(s) || cropped;
            if (resized || t.PlacedShape != s)
            {
                t.PlacedShape = s;
                t.CropKey = cropKey;
                t.IsCut = cut;
                if (cropped)
                {
                    var g = BoardCroppedShape(s, fullW, fullH, offL, offT, w, h);
                    t.Clip = g;
                    t.Outline = g;
                    t.FullClip = BoardFullShape(s, fullW, fullH);
                }
                else
                {
                    t.Clip = cut ? CardShapes.Sized(s, w, h) : CardShapes.Rounded(w, h, 10);
                    t.Outline = cut ? CardShapes.Unit(s) : null;
                    t.FullClip = null;
                }
            }
            t.FullMargin = cropped ? new Thickness(-offL, -offT, -Math.Max(0, fullW - offL - w), -Math.Max(0, fullH - offT - h)) : new Thickness(0);
            t.ArtMargin = cropped && t.Card.CropCut ? t.FullMargin : new Thickness(0);
            bool cropping = !t.IsSlot && ReferenceEquals(t, cropTile);
            if (cropping && t.FullClip == null) t.FullClip = BoardFullShape(s, fullW, fullH);
            t.IsCropping = cropping;
            t.IsPiece = piece;
            if (piece && t.Card.Kind == "logo")
            {
                t.LogoVAlign = VerticalAlignment.Center;
                t.LogoMargin = new Thickness(4);
                t.LogoMaxW = Math.Max(40, w - 8);
                t.LogoMaxH = Math.Max(20, h - 8);
            }
            else if (CardShapes.IsTriangle(s))
            {
                // the logo where the triangle is widest
                t.LogoVAlign = s == CardShapes.TriUp ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                t.LogoMargin = s == CardShapes.TriUp ? new Thickness(0, 0, 0, h * 0.1) : new Thickness(0, h * 0.1, 0, 0);
                t.LogoMaxW = Math.Max(40, w * 0.5);
                t.LogoMaxH = Math.Max(20, h * 0.28);
            }
            else
            {
                t.LogoVAlign = VerticalAlignment.Center;
                t.LogoMargin = new Thickness(w * 0.1);
                t.LogoMaxW = Math.Max(40, w * (cut ? 0.6 : 0.75));
                t.LogoMaxH = Math.Max(20, h * 0.4);
            }
            t.ShowFrame = boardEditing && !t.IsSlot;
            t.IsPicked = ReferenceEquals(t, boardPicked);
            t.FrameRect = t.ShowFrame && !t.IsCut && !t.IsPicked && !cropping;
            t.FrameCut = t.ShowFrame && t.IsCut && !t.IsPicked && !cropping;
            t.PickRect = (t.IsPicked || cropping) && !t.IsCut;
            t.PickCut = (t.IsPicked || cropping) && t.IsCut;
            t.PickText = cropping ? Loc("LOC_KX_CropTag2", "Crop") : t.IsPicked ? (boardResizing ? Loc("LOC_KX_BoardResizingTag", "Resizing") : Loc("LOC_KX_BoardMovingTag", "Moving")) : null;
            t.ZIndex = BoardZ(t);   // (its layer: X › Layer while it's picked up)
            BoardShowState(t);
        }

        /// <summary>Whether a tile shows and takes the highlight (empty tiles and pieces only show their outline while editing).</summary>
        private void BoardShowState(BoardTile t)
        {
            // (0.91) editing: grouped tiles are marked (grouping: the group being set up)
            var tg = t.Card != null ? GroupOf(t.Card) : null;
            t.GroupMark = boardEditing && tg != null && (boardGrouping == null || boardGrouping == tg.Id);
            t.GroupText = t.GroupMark ? (GroupName(tg) + (tg.Carousel ? "   ·   " + Loc("LOC_KX_GroupCarousel", "Carousel") : "")).ToUpperInvariant() : null;
            // (0.93) the tile's place in the Placement order: a big number in its middle
            t.OrderBadge = t.GroupMark && tg.Carousel && t.Card.GroupOrder > 0 ? t.Card.GroupOrder.ToString(System.Globalization.CultureInfo.CurrentCulture) : null;
            t.ShowEditLabel = boardEditing && t.HasEditLabel;
            if (t.IsSlot) { t.ShowEditLabel = t.HasEditLabel && (boardEditing || t.SlotX == -2); t.TileVisibility = Visibility.Visible; t.CanFocus = true; t.ShowBack = false; t.ShowLabel = false; return; }
            var c = t.Card;
            // its background and text (Tile settings › Look)
            bool solidKind = !IsPieceKind(c);
            int back = c.Back >= 1 && c.Back <= 3 ? c.Back : (solidKind ? 1 : 3);
            if (c.Kind == "widget" || c.Kind == "empty") back = 3;
            t.ShowBack = back != 3;
            t.BackBrush = back == 2 ? SeeThroughCard : SolidCard;
            t.ShowLabel = solidKind && !c.HideText;
            bool usable = t.GameId != Guid.Empty || t.Activate != null;
            bool visible = boardEditing || c.Kind == "logo" || c.Kind == "info" || c.Kind == "hltb" || usable
                           || (c.Kind == "pinned" && !string.IsNullOrEmpty(c.GameId) && !string.IsNullOrEmpty(t.Name));   // (a chosen game Playnite's filter hides: says so)
            if (c.Kind == "widget" && !boardEditing) visible = false;   // the widget itself shows there (its buttons take clicks)
            t.TileVisibility = visible ? Visibility.Visible : Visibility.Collapsed;   // a source that ran out: no empty box
            t.CanFocus = boardEditing || usable;
        }

        private static readonly Brush SolidCard = FrozenBrush(0xFF, 0x1E, 0x22, 0x29), SeeThroughCard = FrozenBrush(0x8C, 0x10, 0x12, 0x16);
        private static Brush FrozenBrush(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }

        /// <summary>How many lines of info a tile has room for: 0 for small tiles and the cut shapes, 1 for tall ones.</summary>
        private int BoardInfoRoom(BoardCard c)
        {
            if (c == null || CardShapes.IsCut(c.Shape)) return 0;
            var q = BoardCells(c);
            var r = BoardRect(q.X, q.Y, q.Width, q.Height);
            if (Math.Min(r.Width, r.Height) < 150) return 0;
            if (r.Height > r.Width * 1.25) return 1;
            return Math.Max(1, Math.Min(4, (int)((r.Height - 70) / 42)));
        }

        /// <summary>The picture a tile uses: its choice, or (automatic) what suits its shape.</summary>
        private int BoardArtKind(BoardCard c, double w, double h)
        {
            if (c.Art >= 1 && c.Art <= 4) return c.Art;
            if (CardShapes.IsTriangle(c.Shape)) return 3;
            if (CardShapes.IsCut(c.Shape)) return 1;
            return w >= h * 1.2 ? 2 : 1;
        }

        private string FilePathOf(string dbPath)
        {
            if (string.IsNullOrEmpty(dbPath) || dbPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            string f = SafeFullPath(dbPath);
            return !string.IsNullOrEmpty(f) && File.Exists(f) ? f : null;
        }

        private void BoardFillPage()
        {
            var focusedNow = FocusedBoardTile();
            var before = focusedNow != null ? focusedNow.GameId : Guid.Empty;
            BoardFillAll();
            if (!boardEditing && BoardTiles.Count > 0 && !BoardTiles.Any(t => t.CanFocus))
                foreach (var t in BoardTiles) { t.CanFocus = true; t.TileVisibility = Visibility.Visible; }   // nothing to go to (offline, no games): the tiles still take the highlight, so hold A can edit
            if (focusedNow != null && focusedNow.GameId != before && focusedNow.GameId != Guid.Empty) SelectBoardGameSoon(focusedNow.GameId);
            BoardFillPieces(BoardHighlightGame());
        }

        private void BoardFillAll()
        {
            var storeCards = BoardTiles.Where(t => t.Card != null && t.Card.Kind == "store").ToList();
            foreach (var t in BoardTiles.ToList())
            {
                if (t.IsSlot) continue;
                var c = t.Card;
                switch (c.Kind)
                {
                    case "filter":
                    case "pinned":
                        {
                            if (IsCarouselCard(c)) { var gid = GroupGameFor(t); BoardFill(t, gid); if (gid == Guid.Empty) t.Name = ""; break; }   // (0.91) a carousel group's tile (none left: hidden)
                            Guid[] games;
                            Guid id = boardGames.TryGetValue(c.Id, out games) && boardPage < games.Length ? games[boardPage] : Guid.Empty;
                            if (c.Kind == "pinned" && id == Guid.Empty && (boardPage == 0 || c.EveryPage)) id = BoardPlan.GameOf(c);   // (just chosen: before the pages are worked out again)
                            if (c.Live && c.Kind != "pinned")
                            {
                                int step;
                                if (boardLiveStep.TryGetValue(c.Id, out step) && step > 0 && games != null && games.Length > 1)
                                {
                                    var other = games[(boardPage + step) % games.Length];
                                    if (other != Guid.Empty) id = other;
                                }
                            }
                            BoardFill(t, id);
                            break;
                        }
                    case "store":
                        {
                            // each Store tile its own top seller, the next ones on the next page
                            int k = storeCards.IndexOf(t);
                            StoreItem item = null;
                            if (boardStore != null && boardStore.Count > 0) item = boardStore[(boardPage * storeCards.Count + k) % boardStore.Count];
                            else BoardLoadStore();
                            BoardFillStore(t, item);
                            break;
                        }
                    case "shortcut": BoardFillShortcut(t); break;
                    case "library": BoardFillLibrary(t); break;
                    default: BoardFillPiece(t); break;
                }
                BoardShowState(t);
            }
        }

        private void BoardClearTile(BoardTile t)
        {
            t.GameId = Guid.Empty;
            t.Activate = null;
            t.Title = null;
            t.InfoText = null;
            t.CenterName = null;
            t.Icon = null;
            t.PieceTitle = null;
            t.PieceText = null;
            t.EditLabel = null;
            t.TileOpacity = 1;
            BoardLoadImage(t, null, 0, false);
            BoardLoadImage(t, null, 0, true);
        }

        /// <summary>A tile shows a game: its picture, logo, name and info lines.</summary>
        private void BoardFill(BoardTile t, Guid id)
        {
            var c = t.Card;
            Game g = null;
            if (id != Guid.Empty) { try { g = api.Database.Games.Get(id); } catch (Exception) { } }
            bool pinnedOff = c.Kind == "pinned" && boardPage > 0 && !c.EveryPage;   // a first-page-only chosen game: nothing after that
            bool hidden = g != null && boardShown != null && !boardShown.Contains(g.Id);   // Playnite's filter hides it: it can't be selected
            t.Activate = null;
            t.Icon = null;
            t.PieceTitle = null;
            t.PieceText = null;
            t.GameId = g != null ? g.Id : Guid.Empty;
            t.HiddenByFilter = hidden;   // shown as normal (A clears Playnite's filter to open it)
            hidden = false;
            t.TileOpacity = g != null ? 1 : 0.7;
            t.Name = g != null ? g.Name : pinnedOff ? "" : c.Kind == "pinned" ? (string.IsNullOrEmpty(c.GameId) ? Loc("LOC_KX_BoardChooseGame", "Choose a game (X: tile settings)") : Loc("LOC_KX_BoardPinnedGone", "Game not found"))
                   : BoardSourceName(c.Source);
            t.EditLabel = null;
            if (hidden) g = null;

            // info lines (only what the tile has room for)
            int room = g != null ? BoardInfoRoom(c) : 0;
            var info = c.Info ?? new List<string>();
            string title = null;
            var lines = new List<string>();
            if (room > 0)
            {
                if (info.Contains("name")) title = g.Name;
                foreach (string k in BoardInfoKeys)
                {
                    if (k == "name" || !info.Contains(k)) continue;
                    string line = BoardInfoLine(g, k);
                    if (!string.IsNullOrEmpty(line)) lines.Add(line);
                }
                int left = room - (title != null ? 1 : 0);
                if (left <= 0) lines.Clear();
                else if (lines.Count > left) lines = lines.Take(left).ToList();
            }
            if (c.HideText) { title = null; lines.Clear(); }
            t.Title = title;
            t.InfoText = lines.Count > 0 ? string.Join("\n", lines) : null;
            BoardNudge(t, c);

            // the picture
            string artFile = null, logoFile = null;
            bool wideArt = false;   // a background / wide picture (not the cover): wider than it's tall
            if (g != null)
            {
                int kind = BoardArtKind(c, t.Width, t.Height);   // (cropped: for the part that's left, which its picture fills)
                string cover = ShuffledCover(g, FilePathOf(g.CoverImage)), bg = FilePathOf(g.BackgroundImage);
                string wide = null;
                try { wide = WideArtPath(g); } catch (Exception) { }
                switch (kind)
                {
                    case 2: artFile = wide ?? bg ?? cover; break;
                    case 3: artFile = bg ?? wide ?? cover; logoFile = LogoFileOf(g.Id); break;
                    case 4: artFile = bg ?? wide ?? cover; break;
                    default: artFile = cover ?? bg ?? wide; break;
                }
                t.CenterName = kind == 3 && logoFile == null && !c.HideText ? g.Name : null;
                wideArt = artFile != null && artFile != cover;
            }
            else t.CenterName = null;
            BoardLoadImage(t, artFile, BoardArtPx(t, wideArt), false);
            BoardLoadImage(t, logoFile, BoardPx(t.LogoMaxW * 1.4), true);
        }

        private string LogoFileOf(Guid id)
        {
            try
            {
                string lf = Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", id.ToString(), "Logo.png");
                return File.Exists(lf) ? lf : null;
            }
            catch (Exception) { return null; }
        }

        private static void BoardNudge(BoardTile t, BoardCard c)
        {
            switch (c.Nudge)
            {
                case 1: t.AlignX = AlignmentX.Center; t.AlignY = AlignmentY.Top; break;
                case 2: t.AlignX = AlignmentX.Center; t.AlignY = AlignmentY.Bottom; break;
                case 3: t.AlignX = AlignmentX.Left; t.AlignY = AlignmentY.Center; break;
                case 4: t.AlignX = AlignmentX.Right; t.AlignY = AlignmentY.Center; break;
                default: t.AlignX = AlignmentX.Center; t.AlignY = AlignmentY.Center; break;
            }
        }

        /// <summary>A Store top seller: its picture and name; A opens its Store page.</summary>
        private void BoardFillStore(BoardTile t, StoreItem item)
        {
            BoardClearTile(t);
            BoardNudge(t, t.Card);
            if (item == null) { t.Name = Loc("LOC_KX_BoardStoreLoading", "Popular in the store…"); t.TileOpacity = 0.7; return; }
            t.Name = item.Name;
            var it = item;
            t.Activate = () => OpenStoreFromWidget(it);
            int room = t.Card.HideText ? 0 : BoardInfoRoom(t.Card);
            if (room > 0)
            {
                t.Title = item.Name;
                if (room > 1 && !string.IsNullOrEmpty(item.PriceText)) t.InfoText = item.PriceText + (item.HasDiscount ? "   " + item.DiscountText : "");
            }
            BoardLoadImage(t, item.Image, BoardArtPx(t, true), false);
        }

        private static readonly string[] ShortcutKeys = { "store", "achievements", "gallery", "search", "collections", "random" };

        private string BoardShortcutName(string key)
        {
            switch (key)
            {
                case "store": return Loc("LOC_KX_Store", "Store");
                case "achievements": return Loc("LOC_KX_Achievements", "Achievements");
                case "gallery": return Loc("LOC_KX_GalleryName", "Gallery");
                case "search": return Loc("LOC_KX_SearchName", "Search");
                case "collections": return Loc("LOC_KX_PageCollections", "Collections");
                case "random": return Loc("LOC_KX_RandomGame", "Random game");
            }
            if (key != null && key.StartsWith("shelf:", StringComparison.Ordinal)) { var s = ShelfById(key.Substring(6)); return s != null ? s.Name : Loc("LOC_KX_SrcGone", "(not there any more)"); }
            return key ?? "";
        }

        private static string BoardShortcutIcon(string key)
        {
            switch (key)
            {
                case "store": return "store";
                case "achievements": return "trophy";
                case "gallery": return "gallery";
                case "search": return "search";
                case "random": return "dice";
            }
            return "shelf";
        }

        /// <summary>A shortcut: its icon and name; A opens it.</summary>
        private void BoardFillShortcut(BoardTile t)
        {
            BoardClearTile(t);
            string key = t.Card.Target ?? "store";
            t.Name = BoardShortcutName(key);
            t.Icon = BoardShortcutIcon(key);
            t.Activate = () => RunBoardShortcut(key);
        }

        private void RunBoardShortcut(string key)
        {
            PlaySound("select");
            switch (key)
            {
                case "store": OpenStore(); return;
                case "achievements": OpenAchievementsView(); return;
                case "gallery": OpenGallery(); return;
                case "search": OpenSearch(); return;
                case "collections": OpenPageFromShortcut("collections"); return;
                case "random":
                    {
                        // a random game from the ones Playnite's filter shows: its page
                        var ids = boardShown != null ? boardShown.ToList() : new List<Guid>();
                        if (ids.Count == 0) return;
                        var id = ids[Rand(ids.Count)];
                        try { api.MainView.SelectGame(id); } catch (Exception) { }
                        if (settings.SkipGamePage) return;
                        ui.BeginInvoke(new Action(() => { if (SelectedGameId() == id) RunMainCommand("ToggleGameDetailsCommand"); }), DispatcherPriority.Loaded);
                        return;
                    }
            }
            if (key.StartsWith("shelf:", StringComparison.Ordinal)) { pageStack.Clear(); OpenPage("shelf", key.Substring(6), false); }
        }

        // ---- a Library tile: the games of the lists you pick, A opens them as a page ----

        private string LibraryName(BoardCard c)
        {
            if (!string.IsNullOrEmpty(c.Name)) return c.Name;
            var src = c.Sources != null && c.Sources.Count > 0 ? c.Sources : new List<string> { "all" };
            if (src.Count == 1 && src[0] == "all") return Loc("LOC_KX_LibraryAll", "Library");
            return string.Join(" + ", src.Select(BoardSourceName).Take(3).ToArray());
        }

        private void BoardFillLibrary(BoardTile t)
        {
            BoardClearTile(t);
            var c = t.Card;
            t.Name = LibraryName(c);
            t.Icon = "shelf";
            string id = c.Id;
            t.Activate = () => { pageStack.Clear(); OpenPage("library", id, false); };
        }

        private BoardCard BoardCardById(string id)
        {
            foreach (var list in new[] { settings.BoardCards, settings.ManualCards, settings.FreeCards })
                if (list != null) { var c = list.FirstOrDefault(x => x != null && x.Id == id); if (c != null) return c; }
            return null;
        }

        /// <summary>The page a Library tile opens: every game in its lists (one of each), A opens a game.</summary>
        private void BuildBoardLibraryPage(string id)
        {
            var c = BoardCardById(id);
            if (c == null) { PageTitle = Loc("LOC_KX_LibraryAll", "Library"); SetPageStatus(Loc("LOC_KX_BoardNoCard2", "That tile isn't on the board any more")); return; }
            PageTitle = LibraryName(c);
            var src = c.Sources != null && c.Sources.Count > 0 ? c.Sources : new List<string> { "all" };
            var games = new List<Game>();
            try
            {
                var all = api.Database.Games.Where(g => g != null && !g.Hidden && ProfileAllowsGame(g) && !CopyBehind(g.Id)).ToList();
                var byId = all.ToDictionary(g => g.Id, g => g);
                var shelves = new Dictionary<string, GameShelf>();
                foreach (string k in src.Where(k => k.StartsWith("shelf:", StringComparison.Ordinal)))
                {
                    var found = ShelfById(k.Substring(6));
                    if (found != null) shelves[k] = found;
                }
                var seen = new HashSet<Guid>();
                foreach (string k in src)
                    foreach (var gid in BoardSourceGames(k, all, null, null, shelves))   // (not Playnite's filter: the lists themselves)
                    {
                        Game g;
                        if (seen.Add(gid) && byId.TryGetValue(gid, out g)) games.Add(g);
                    }
            }
            catch (Exception ex) { logger.Warn("Kippix: library page: " + ex.Message); }
            PageSub = games.Count == 1 ? Loc("LOC_KX_OneGame", "1 game") : string.Format(Loc("LOC_KX_NGames", "{0} games"), games.Count);
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
            SetPageStatus(games.Count == 0 ? Loc("LOC_KX_LibraryEmpty", "No games in these lists") : "");
            LoadPageImages();
        }

        /// <summary>A piece: the highlighted game's logo / info / How long to beat, a widget's place, or a space kept clear.</summary>
        private void BoardFillPiece(BoardTile t)
        {
            var c = t.Card;
            BoardClearTile(t);
            switch (c.Kind)
            {
                case "logo": t.EditLabel = Loc("LOC_KX_TileLogo", "Game logo"); break;
                case "info": t.EditLabel = Loc("LOC_KX_TileInfo", "Game info"); break;
                case "hltb": t.EditLabel = Loc("LOC_KX_TileHltb", "How long to beat"); break;
                case "widget":
                    {
                        var w = Widgets != null ? Widgets.FirstOrDefault(x => x.Config != null && x.Config.Id == c.Target) : null;
                        t.EditLabel = w != null ? string.Format(Loc("LOC_KX_TileWidgetN", "Widget: {0}"), w.Name) : Loc("LOC_KX_TileWidgetGone", "Widget (removed)");
                        if (!settings.WidgetsOn) t.PieceText = Loc("LOC_KX_TileWidgetsOff2", "Widgets are off (Settings & Customisation › Home settings › Widgets)");
                        break;
                    }
                default: t.EditLabel = Loc("LOC_KX_TileEmpty", "Empty (kept clear)"); break;
            }
            t.Name = "";
        }

        private int boardPieceGen;

        /// <summary>The logo / info / How long to beat pieces show the highlighted game.</summary>
        private void BoardFillPieces(Guid id)
        {
            var pieces = BoardTiles.Where(t => t.Card != null && (t.Card.Kind == "logo" || t.Card.Kind == "info" || t.Card.Kind == "hltb")).ToList();
            if (pieces.Count == 0) return;
            Game g = null;
            if (id != Guid.Empty) { try { g = api.Database.Games.Get(id); } catch (Exception) { } }
            int gen = ++boardPieceGen;
            foreach (var t in pieces)
            {
                var c = t.Card;
                if (c.Kind == "logo")
                {
                    string lf = g != null ? LogoFileOf(g.Id) : null;
                    t.CenterName = g != null && lf == null && !c.HideText ? g.Name : null;
                    BoardLoadImage(t, lf, BoardPx(t.LogoMaxW * 1.2), true);
                }
                else if (c.Kind == "info")
                {
                    var info = c.Info != null && c.Info.Count > 0 ? c.Info : new List<string> { "name", "playtime", "lastplayed", "ach" };
                    t.PieceTitle = g != null && info.Contains("name") && !c.HideText ? g.Name : null;
                    var lines = g == null ? new List<string>() : BoardPieceKeys.Where(k => k != "name" && info.Contains(k)).Select(k => BoardInfoLine(g, k)).Where(x => !string.IsNullOrEmpty(x)).ToList();
                    t.PieceText = lines.Count > 0 ? string.Join("\n", lines) : null;
                }
                else
                {
                    t.PieceTitle = c.HideText ? null : Loc("LOC_KX_TileHltb", "How long to beat");
                    t.PieceText = g == null ? null : Loc("LOC_KX_HltbLoading", "…");
                    if (g == null) continue;
                    var tile = t;
                    string ext = api.Paths.ExtensionsDataPath;
                    Guid gid = g.Id;
                    KippixShared.SafeWork.Queue(_ =>
                    {
                        string text;
                        try
                        {
                            var h = KippixOverlay.GameData.LoadHltb(ext, gid);
                            var parts = new List<string>();
                            if (h != null && h.Story > 0) parts.Add(string.Format(Loc("LOC_KX_HltbStory", "Main story   {0}"), HltbHours(h.Story)));
                            if (h != null && h.Extras > 0) parts.Add(string.Format(Loc("LOC_KX_HltbExtras", "Main + extras   {0}"), HltbHours(h.Extras)));
                            if (h != null && h.Completionist > 0) parts.Add(string.Format(Loc("LOC_KX_HltbComplete", "Completionist   {0}"), HltbHours(h.Completionist)));
                            text = parts.Count > 0 ? string.Join("\n", parts) : Loc("LOC_KX_HltbNone", "Not known (HowLongToBeat add-on)");
                        }
                        catch (Exception) { text = Loc("LOC_KX_HltbNone", "Not known (HowLongToBeat add-on)"); }
                        OnUi(() => { if (gen == boardPieceGen) tile.PieceText = text; });
                    });
                }
            }
        }

        private static string HltbHours(double h)
        {
            return (h < 1 ? "<1" : Math.Round(h).ToString(System.Globalization.CultureInfo.InvariantCulture)) + " h";
        }

        /// <summary>The game the pieces follow: the highlighted tile's, else Playnite's selected game.</summary>
        private Guid BoardHighlightGame()
        {
            var t = FocusedBoardTile();
            if (t != null && t.GameId != Guid.Empty) return t.GameId;
            return SelectedGameId();
        }

        /// <summary>A few fixed picture sizes (not one per tile size), so the same picture is decoded once.</summary>
        private static int BoardPx(double want)
        {
            foreach (int s in new[] { 300, 450, 700, 1000 }) if (want <= s) return s;
            return 1400;
        }

        /// <summary>
        /// How wide a tile's picture is decoded: what it fills (the tile; cropped, the part that's left), uniform to fill, at the
        /// screen's real pixels (CarScreenScale: 1080p units to device pixels), a few fixed sizes so a picture is decoded once.
        /// </summary>
        private int BoardArtPx(BoardTile t, bool wideArt)
        {
            double w = Math.Max(16, t.Width), h = Math.Max(16, t.Height);
            if (t.ArtMargin.Left < 0 || t.ArtMargin.Top < 0 || t.ArtMargin.Right < 0 || t.ArtMargin.Bottom < 0) { w = Math.Max(w, t.FullW); h = Math.Max(h, t.FullH); }   // (cut: laid out at the whole tile)
            double want = Math.Max(w, h * (wideArt ? 1.78 : 0.75)) * 1.1 * CarScreenScale();
            foreach (int s in new[] { 300, 450, 700, 1000, 1400 }) if (want <= s) return s;
            return want <= 2000 ? 2000 : 2600;
        }

        private void BoardLoadImage(BoardTile t, string file, int px, bool logo)
        {
            if (logo) t.LogoFile = file; else t.ArtFile = file;
            if (string.IsNullOrEmpty(file)) { if (logo) t.Logo = null; else t.Art = null; return; }
            string key = file + "|" + px;
            ImageSource cached;
            lock (boardImages) boardImages.TryGetValue(key, out cached);
            if (cached != null) { if (logo) t.Logo = cached; else t.Art = cached; return; }
            KippixShared.SafeWork.Queue(_ =>
            {
                ImageSource img = null;
                try { img = Images.LoadUncached(file, px); } catch (Exception) { }
                if (img == null)
                {
                    OnUi(() => { if (logo) { if (t.LogoFile == file) t.Logo = null; } else if (t.ArtFile == file) t.Art = null; });   // not the last game's picture
                    return;
                }
                lock (boardImages) { if (boardImages.Count > 160) boardImages.Clear(); boardImages[key] = img; }
                OnUi(() =>
                {
                    if (logo) { if (t.LogoFile == file) t.Logo = img; }
                    else if (t.ArtFile == file) t.Art = img;
                });
            });
        }

        private static readonly string[] BoardInfoKeys = { "name", "playtime", "lastplayed", "hltb", "ach", "installed" };
        private static readonly string[] BoardPieceKeys = { "name", "playtime", "lastplayed", "hltb", "ach", "installed", "status", "platform", "source", "year", "genre" };

        private string BoardInfoLabel(string k)
        {
            switch (k)
            {
                case "name": return Loc("LOC_KX_LiName", "Game name");
                case "playtime": return Loc("LOC_KX_LiPlaytime", "Play time");
                case "lastplayed": return Loc("LOC_KX_LiLastPlayed", "Last played");
                case "hltb": return Loc("LOC_KX_LiHltb", "How long to beat");
                case "ach": return Loc("LOC_KX_BoardAch", "Achievements %");
                case "installed": return Loc("LOC_KX_BoardInstalledRow", "Installed or not");
                case "status": return Loc("LOC_KX_LiStatus", "Completion status");
                case "platform": return Loc("LOC_KX_LiPlatform", "Platform");
                case "source": return Loc("LOC_KX_LiSource", "Store / library");
                case "year": return Loc("LOC_KX_LiYear", "Release year");
                case "genre": return Loc("LOC_KX_LiGenre", "Genre");
            }
            return k;
        }

        private string BoardInfoLine(Game g, string k)
        {
            try
            {
                switch (k)
                {
                    case "playtime": return g.Playtime > 0 ? string.Format(Loc("LOC_KX_ListPlayed", "{0} played"), HoursShort(g.Playtime / 3600.0)) : Loc("LOC_KX_ListNeverPlayed", "Not played yet");
                    case "lastplayed": return g.LastActivity.HasValue ? string.Format(Loc("LOC_KX_ListLast", "Last played {0}"), AgoText(g.LastActivity.Value)) : null;
                    case "hltb": { string h = HltbBadgeText(g.Id, true); return string.IsNullOrEmpty(h) ? null : string.Format(Loc("LOC_KX_LiHltbLine", "{0} to beat"), h); }
                    case "ach": { string a = AchBadgeText(g.Id); return string.IsNullOrEmpty(a) ? null : string.Format(Loc("LOC_KX_LiAchLine", "{0} achievements"), a); }
                    case "installed": return g.IsInstalled ? Loc("LOC_KX_BoardInstalled", "Installed") : Loc("LOC_KX_NotInstalled", "Not installed");
                    case "status": return g.CompletionStatus != null ? g.CompletionStatus.Name : null;
                    case "platform": return g.Platforms != null && g.Platforms.Count > 0 ? g.Platforms[0].Name : null;
                    case "source": return g.Source != null ? g.Source.Name : null;
                    case "year": return g.ReleaseYear.HasValue ? g.ReleaseYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                    case "genre": return g.Genres != null && g.Genres.Count > 0 ? string.Join(", ", g.Genres.Take(2).Select(x => x.Name).ToArray()) : null;
                }
            }
            catch (Exception) { }
            return null;
        }

        // =====================================================================
        // Widgets in tiles (in the Board / Manual, a widget shows only in the tile you give it)
        // =====================================================================

        private void BoardPlaceWidgetsSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(BoardPlaceWidgets), DispatcherPriority.Loaded);
        }

        private void BoardPlaceWidgets()
        {
            try
            {
                if (Widgets == null) return;
                if (!IsBoardMode)
                {
                    foreach (var w in Widgets) if (w.BoardRect.HasValue || w.ItemVisibility != Visibility.Visible) w.SetBoard(null, false);
                    return;
                }
                var win = Application.Current.MainWindow;
                var layer = UiTools.FindByName(win, "KX_Widgets");
                var host = UiTools.FindByName(win, "KX_BoardHost");
                foreach (var w in Widgets)
                {
                    var t = BoardTiles.FirstOrDefault(x => x.Card != null && x.Card.Kind == "widget" && w.Config != null && x.Card.Target == w.Config.Id);
                    bool elsewhere = t == null && w.Config != null && BCards.Any(c => c.Kind == "widget" && c.Target == w.Config.Id);   // in a tile on another page
                    if (t == null || layer == null || host == null) { w.SetBoard(null, elsewhere); continue; }   // not in a tile: where you put it
                    Point p;
                    try { p = host.TranslatePoint(new Point(t.Left, t.Top), layer); } catch (Exception) { p = new Point(72 + t.Left, 112 + t.Top); }
                    w.SetBoard(new Rect(p.X, p.Y, t.Width, t.Height), false);
                    w.SetBoardAngle(t.Rotation);   // turned with its tile
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: board widgets: " + ex.Message); }
        }

        /// <summary>The widget the highlight is in (and its place on the board), if it's one of the board's.</summary>
        private BoardTile BoardWidgetTileOfFocus(out FrameworkElement container)
        {
            container = null;
            var f = Keyboard.FocusedElement as DependencyObject;
            var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets") as ItemsControl;
            if (f == null || layer == null || !layer.IsAncestorOf(f)) return null;
            for (var d = f; d != null && !ReferenceEquals(d, layer); d = VisualTreeHelper.GetParent(d))
            {
                var fe = d as FrameworkElement;
                var w = fe != null ? fe.DataContext as WidgetItem : null;
                if (w == null) continue;
                container = layer.ItemContainerGenerator.ContainerFromItem(w) as FrameworkElement;
                return BoardTiles.FirstOrDefault(x => x.Card != null && x.Card.Kind == "widget" && w.Config != null && x.Card.Target == w.Config.Id);
            }
            return null;
        }

        private List<FrameworkElement> BoardWidgetButtons(BoardTile t)
        {
            var list = new List<FrameworkElement>();
            if (t == null || t.Card == null || t.Card.Kind != "widget" || Widgets == null) return list;
            var w = Widgets.FirstOrDefault(x => x.Config != null && x.Config.Id == t.Card.Target);
            var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets") as ItemsControl;
            var c = w != null && layer != null && layer.IsVisible ? layer.ItemContainerGenerator.ContainerFromItem(w) as DependencyObject : null;
            if (c != null) Collect(c, list);
            return list;
        }

        // =====================================================================
        // Pages
        // =====================================================================

        private void BoardMakeDots()
        {
            BoardDots.Clear();
            if (boardPages <= 1 || boardPages > 24) return;   // a lot of pages: the "2 / 30" number reads better (shown instead)
            bool vertical = settings.BoardPageAxis == 1;
            for (int i = 0; i < boardPages; i++)
            {
                bool cur = i == boardPage;
                BoardDots.Add(new BoardDot { DotWidth = vertical ? 10 : cur ? 28 : 10, DotHeight = vertical ? (cur ? 28 : 10) : 10, DotOpacity = cur ? 1 : 0.4 });
            }
        }

        /// <summary>Next / previous page (a page at a time), the highlight on the tile nearest where you came in.</summary>
        /// <summary>To another Manual Board page (its own tiles), sliding in from that side.</summary>
        private void BoardGoPage(string id, Key dir, Rect from)
        {
            if (id == boardPageId) return;
            boardPageId = id;
            boardPage = 0;
            boardGames = new Dictionary<string, Guid[]>();
            boardFocusId = null;
            BoardSyncTiles();
            BoardFillPage();
            NotifyBoard();
            BoardRefreshSoon();
            BoardPlaceWidgetsSoon();
            BoardSlideIn(dir, from);
        }

        private void BoardFlip(int page, Key dir, Rect from)
        {
            if (page < 0 || page >= boardPages || page == boardPage) return;
            boardPage = page;
            boardLiveStep.Clear();
            BoardFillPage();
            BoardMakeDots();
            Notify("BoardPageText");
            if (BoardSlidesOne) { BoardNudge(dir); return; }   // (1.0.2) one game along: the highlight stays on the tile at the edge
            BoardSlideIn(dir, from);
        }

        private bool BoardSlidesOne { get { return settings.BoardSlideOne && !IsManualMode; } }

        /// <summary>(1.0.2) One game along: the games give a little push that way (the highlight stays where it is).</summary>
        private void BoardNudge(Key dir)
        {
            PlaySound("move");
            var board = boardHooked;
            if (board == null || settings.ReduceMotion) return;
            double dx = dir == Key.Right ? 28 : dir == Key.Left ? -28 : 0, dy = dir == Key.Down ? 28 : dir == Key.Up ? -28 : 0;
            var t = board.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen) { t = new TranslateTransform(); board.RenderTransform = t; }
            var d = Motion(160);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, d) { EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, d) { EasingFunction = ease });
        }

        /// <summary>A new page slides in from the side you went to; the highlight goes to the tile nearest where you came in.</summary>
        private void BoardSlideIn(Key dir, Rect from)
        {
            PlaySound("move");
            var board = boardHooked;
            if (board != null)
            {
                double dx = dir == Key.Right ? 90 : dir == Key.Left ? -90 : 0, dy = dir == Key.Down ? 90 : dir == Key.Up ? -90 : 0;
                var t = board.RenderTransform as TranslateTransform;
                if (t == null || t.IsFrozen) { t = new TranslateTransform(); board.RenderTransform = t; }
                var d = Motion(240);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, d) { EasingFunction = ease });
                t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, d) { EasingFunction = ease });
                board.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, d));
            }
            var cands = BoardTiles.Where(x => BoardUsable(x)).ToList();
            if (cands.Count == 0) { FocusBoard(); return; }
            Point c0 = new Point(from.X + from.Width / 2, from.Y + from.Height / 2);
            if (settings.BoardKeepSpot && !IsManualMode)
            {
                // (1.0.3) the same tile on the new page (the tiles stay where they are; only their games change)
                var same = cands.OrderBy(x => Math.Abs(x.Left + x.Width / 2 - c0.X) + Math.Abs(x.Top + x.Height / 2 - c0.Y)).First();
                FocusBoardTile(same);
                return;
            }
            Func<BoardTile, double> score = x =>
            {
                double cx = x.Left + x.Width / 2, cy = x.Top + x.Height / 2;
                switch (dir)
                {
                    case Key.Right: return x.Left + Math.Abs(cy - c0.Y) * 2;
                    case Key.Left: return (BoardAreaW - (x.Left + x.Width)) + Math.Abs(cy - c0.Y) * 2;
                    case Key.Down: return x.Top + Math.Abs(cx - c0.X) * 2;
                    default: return (BoardAreaH - (x.Top + x.Height)) + Math.Abs(cx - c0.X) * 2;
                }
            };
            FocusBoardTile(cands.OrderBy(score).First());
        }

        // =====================================================================
        // Live tiles: a tile on a source changes game every few seconds
        // =====================================================================

        private void BoardLiveTimer()
        {
            bool any = IsBoardMode && ShownCards.Any(c => c.Live && c.Kind == "filter");
            if (!any) { if (boardLiveTimer != null) boardLiveTimer.Stop(); return; }
            if (boardLiveTimer == null)
            {
                boardLiveTimer = new DispatcherTimer(DispatcherPriority.Background, ui);
                boardLiveTimer.Tick += (s, e) => BoardLiveTick();
            }
            boardLiveTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, settings.BoardLiveSeconds));
            if (!boardLiveTimer.IsEnabled) boardLiveTimer.Start();
        }

        private void BoardLiveTick()
        {
            if (!IsBoardMode || boardEditing || openPanel != null || detailsOpen || gameRunningNow || boardPages <= 1) return;
            var focused = FocusedBoardTile();
            foreach (var t in BoardTiles.ToList())
            {
                if (t.IsSlot || !t.Card.Live || t.Card.Kind != "filter" || ReferenceEquals(t, focused)) continue;   // never under the highlight
                Guid[] games;
                if (!boardGames.TryGetValue(t.Card.Id, out games) || games.Length <= 1) continue;
                int step;
                boardLiveStep.TryGetValue(t.Card.Id, out step);
                for (int tries = 0; tries < games.Length; tries++)
                {
                    step = (step + 1) % games.Length;
                    if (games[(boardPage + step) % games.Length] != Guid.Empty) break;
                }
                boardLiveStep[t.Card.Id] = step;
                BoardFill(t, games[(boardPage + step) % games.Length]);
                BoardShowState(t);
            }
        }

        // =====================================================================
        // The highlight, A, moving about
        // =====================================================================

        private bool BoardHasFocus()
        {
            var b = boardHooked;
            return IsBoardMode && b != null && b.IsKeyboardFocusWithin;
        }

        private BoardTile FocusedBoardTile()
        {
            if (!BoardHasFocus()) return null;
            var f = Keyboard.FocusedElement as FrameworkElement;
            return f != null ? f.DataContext as BoardTile : null;
        }

        private void BoardFocusChanged(FrameworkElement f)
        {
            var t = f != null ? f.DataContext as BoardTile : null;
            if (t == null) return;
            if (t.Card != null && t.Card.Id != boardFocusId)
            {
                boardFocusId = t.Card.Id;
                foreach (var x in BoardTiles) if (!x.IsPicked) x.ZIndex = BoardZ(x);
            }
            if (t.GameId != Guid.Empty) { SelectBoardGameSoon(t.GameId); BoardFillPieces(t.GameId); BoardAccentNow(t.GameId); }
        }

        /// <summary>
        /// 0.82: Accent from the game follows the highlighted tile at once (Playnite's selected game follows 140 ms later, so the
        /// glow used to change after the highlight had moved); the colours of the page's other tiles are worked out ahead.
        /// </summary>
        private void BoardAccentNow(Guid id)
        {
            if (!settings.AccentFromGame || FolderBgBlack) return;
            try
            {
                var g = api.Database.Games.Get(id);
                if (g == null) return;
                string path = AccentPathOf(g);
                lastAccentPath = path;
                ApplyAccentFor(path, true);
                var others = new List<string>();
                foreach (var x in BoardTiles)
                {
                    if (x.GameId == Guid.Empty || x.GameId == id) continue;
                    var og = api.Database.Games.Get(x.GameId);
                    string p = og != null ? AccentPathOf(og) : null;
                    Color? known;
                    if (p != null && !AccentCached(p, out known) && !others.Contains(p)) others.Add(p);
                }
                AccentWarm(others);
            }
            catch (Exception) { }
        }

        // 0.82: closing a game page put the highlight on the profile picture for a moment (Playnite gives it to the first thing it
        // finds, its own list being hidden on the Board), then the Board took it back. For a moment after the page closes the
        // highlight can only go onto the Board; anything else is turned away and the Board's last tile gets it.
        private DateTime boardRefocusUntil = DateTime.MinValue;
        private bool boardRefocusHooked;

        private void BoardHoldFocusAfterDetails()
        {
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null || ui == null) return;
            boardRefocusUntil = DateTime.Now.AddMilliseconds(600);
            if (!boardRefocusHooked)
            {
                boardRefocusHooked = true;
                win.AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(BoardRefocusGuard), true);
            }
            if (!BoardHasFocus()) FocusBoard();
            ui.BeginInvoke(new Action(() => { if (!detailsOpen && openPanel == null && IsBoardMode && !BoardHasFocus()) FocusBoard(); }), DispatcherPriority.Loaded);
        }

        private void BoardRefocusGuard(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (DateTime.Now > boardRefocusUntil) return;
                if (!IsBoardMode || detailsOpen || openPanel != null || confirmOpen || kbOpen || boardHooked == null) { boardRefocusUntil = DateTime.MinValue; return; }
                var to = e.NewFocus as DependencyObject;
                if (to == null) return;
                if (boardHooked.IsAncestorOf(to)) { boardRefocusUntil = DateTime.MinValue; return; }   // on the Board: done
                e.Handled = true;   // (not the top bar on the way)
                ui.BeginInvoke(new Action(() => { if (!BoardHasFocus()) FocusBoard(); }), DispatcherPriority.Input);
            }
            catch (Exception) { }
        }

        /// <summary>The highlighted tile's game becomes Playnite's selected game (background, music and X follow it).</summary>
        private void SelectBoardGameSoon(Guid id)
        {
            boardSelectId = id;
            if (boardSelectTimer == null)
            {
                boardSelectTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(140) };
                boardSelectTimer.Tick += (s, e) => { boardSelectTimer.Stop(); BoardSelectNow(); };
            }
            boardSelectTimer.Stop();
            boardSelectTimer.Start();
        }

        private void BoardSelectNow()
        {
            if (boardSelectTimer != null) boardSelectTimer.Stop();
            if (detailsOpen || openPanel != null || !IsBoardMode) return;   // a game page / panel open: never change its game
            var cur = FocusedBoardTile();
            var id = cur != null ? cur.GameId : boardSelectId;
            if (id == Guid.Empty || SelectedGameId() == id) return;
            try { api.MainView.SelectGame(id); } catch (Exception) { }
        }

        /// <summary>The highlight onto a tile (a widget's tile: onto the widget's first button).</summary>
        private void FocusBoardTile(BoardTile t)
        {
            if (t == null || boardHooked == null) return;
            if (t.Card != null) boardFocusId = t.Card.Id;
            if (!boardEditing && t.Card != null && t.Card.Kind == "widget")
            {
                var btns = BoardWidgetButtons(t);
                if (btns.Count > 0) Keyboard.Focus(btns[0]);
                return;
            }
            int tries = 0;
            Action go = null;
            go = () =>
            {
                var ic = boardHooked as ItemsControl;
                var c = ic != null ? ic.ItemContainerGenerator.ContainerFromItem(t) as FrameworkElement : null;
                if (c != null && !c.IsLoaded) { try { c.UpdateLayout(); } catch (Exception) { } }
                var btn = c != null ? UiTools.FindChild<ButtonBase>(c) as UIElement : null;
                if (btn != null && btn.Focusable) { Keyboard.Focus(btn); return; }
                if (++tries < 4) ui.BeginInvoke(go, DispatcherPriority.Loaded);   // its button isn't made yet: once it is
            };
            go();
        }

        /// <summary>Can the highlight go here (not editing: a game, a Store game, a shortcut, or a widget with buttons).</summary>
        private bool BoardUsable(BoardTile t)
        {
            if (boardEditing) return true;
            if (t.IsSlot) return BoardTiles.All(x => x.IsSlot);   // an empty board: its squares
            if (t.CanFocus && !BoardTiles.Any(x => x.GameId != Guid.Empty || x.Activate != null)) return true;   // nothing usable at all: still somewhere to be
            if (t.GameId != Guid.Empty || t.Activate != null) return true;
            return t.Card.Kind == "widget" && BoardWidgetButtons(t).Count > 0;
        }

        /// <summary>The highlight onto the Board: the selected game's tile, else the tile it was last on, else the first.</summary>
        private bool FocusBoard()
        {
            if (!IsBoardMode || BoardTiles.Count == 0) return false;
            var cards = BoardTiles.Where(t => t.IsCard && BoardUsable(t) && t.Card.Kind != "widget").ToList();
            var sel = SelectedGameId();
            var pick = cards.FirstOrDefault(t => sel != Guid.Empty && t.GameId == sel && t.Card.Id == boardFocusId);
            if (pick == null) pick = cards.FirstOrDefault(t => sel != Guid.Empty && t.GameId == sel);
            if (pick == null) pick = cards.FirstOrDefault(t => t.Card.Id == boardFocusId);
            if (pick == null) pick = cards.OrderBy(t => t.Top).ThenBy(t => t.Left).FirstOrDefault();
            if (pick == null) pick = BoardTiles.Where(BoardUsable).OrderBy(t => t.Top).ThenBy(t => t.Left).FirstOrDefault();
            if (pick == null) pick = BoardTiles.Where(t => t.CanFocus && t.TileVisibility == Visibility.Visible).OrderBy(t => t.Top).ThenBy(t => t.Left).FirstOrDefault();
            if (pick == null) return false;
            FocusBoardTile(pick);
            return true;
        }

        /// <summary>Nothing has the highlight (Playnite put it on the hidden cover row as it started): onto the Board.</summary>
        private void BoardFocusIfLost()
        {
            if (!IsBoardMode || openPanel != null || detailsOpen || confirmOpen || kbOpen) return;
            var f = Keyboard.FocusedElement as UIElement;
            if (f == null || f is Window || !f.IsVisible) FocusBoard();
        }

        private void BoardTileCommand(BoardTile t)
        {
            if (t == null || !IsBoardMode) return;
            if (cropTile != null) { if (!CropJustStarted("the tile's button")) CropEnd(true); return; }   // cropping: A keeps it
            if (boardGrouping != null && boardEditing) { BoardGroupToggle(t); return; }   // (0.91) grouping: into the group / out of it
            if (boardPasting) { BoardPasteTo(t); return; }
            if (t.IsSlot && !boardEditing) { BoardStartEdit(null); if (t.SlotX >= 0) BoardEditA(t); return; }   // an empty page: A starts filling it
            if (boardEditing) { BoardEditA(t); return; }
            if (t.GameId != Guid.Empty) { OpenBoardTile(t); return; }
            if (t.Activate != null) { t.Activate(); return; }
            PlaySound("back");
        }

        private void OpenBoardTile(BoardTile t)
        {
            PlaySound("select");
            var id = t.GameId;
            boardFocusId = t.Card.Id;
            if (t.HiddenByFilter || (boardShown != null && !boardShown.Contains(id)))
            {
                // Playnite's filter hides this game (e.g. "Installed" and it isn't): the filter is cleared so its page can open
                ClearPlayniteFilter();
                try { ShowNotice(Loc("LOC_KX_FilterClearedHead", "Filter cleared"), Loc("LOC_KX_FilterCleared", "Playnite's filter was hiding this game"), null, null, null); } catch (Exception) { }
                ui.BeginInvoke(new Action(() =>
                {
                    try { api.MainView.SelectGame(id); } catch (Exception) { }
                    if (settings.SkipGamePage) { if (TrySwitchInsteadOfPlay(id)) return; launchedDirectlyAt = DateTime.Now; try { api.StartGame(id); } catch (Exception) { } return; }
                    ui.BeginInvoke(new Action(() => { if (SelectedGameId() == id) RunMainCommand("ToggleGameDetailsCommand"); }), DispatcherPriority.Loaded);
                }), DispatcherPriority.ContextIdle);
                return;
            }
            try { api.MainView.SelectGame(id); } catch (Exception) { }
            if (settings.SkipGamePage) { if (TrySwitchInsteadOfPlay(id)) return; launchedDirectlyAt = DateTime.Now; try { api.StartGame(id); } catch (Exception) { } return; }
            ui.BeginInvoke(new Action(() =>
            {
                if (SelectedGameId() != id) return;   // Playnite's filter hides it: don't open another game's page
                RunMainCommand("ToggleGameDetailsCommand");
            }), DispatcherPriority.Loaded);
        }

        /// <summary>The Board's arrow keys (on the way down): between tiles; past the edge, the next page.</summary>
        private bool BoardKey(KeyEventArgs e)
        {
            if (!IsBoardMode || confirmOpen) return false;
            bool arrow = e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right;
            bool pad = e.GetType().Name == "GameControllerInputEventArgs";
            FrameworkElement widgetBox;
            var inWidget = BoardWidgetTileOfFocus(out widgetBox);   // the highlight is in a widget on the board
            if (IsPadRelease(e)) return arrow && (BoardHasFocus() || inWidget != null);
            if (cropTile != null && !pad) return CropKey(e);   // cropping a tile: the D-pad trims its sides
            if (boardEditing && !pad && CropEnterEcho(e)) return true;   // (the Enter of the A that just ended cropping)
            if (boardEditing && !pad && !e.IsRepeat && (e.Key == Key.Z || e.Key == Key.Y) && boardPicked == null && HistoryKeyboardKey(e.Key)) return true;   // Ctrl+Z / Ctrl+Y
            if (boardEditing && !pad && boardPicked != null && PickExtrasKey(null, e.Key)) return true;   // M: Move / Layer / Opacity (and the arrows while Layer / Opacity is on)
            var cur = FocusedBoardTile() ?? inWidget;
            if (boardEditing && !pad && (e.Key == Key.Escape || e.Key == Key.Back)) { BoardBack(); return true; }
            if (boardEditing && boardGrouping != null && !pad && !arrow && e.Key != Key.Enter && e.Key != Key.Space) return true;   // (0.91) grouping: only A and B
            if (boardEditing && !pad && e.Key == Key.Delete && !e.IsRepeat && (boardPicked != null || (cur != null && cur.IsCard))) { BoardDeletePickedOrFocused(); return true; }
            if (boardEditing && !pad && boardPicked == null && !boardPasting && boardGrouping == null && IsManualMode && (e.Key == Key.PageUp || e.Key == Key.PageDown))
            {
                if (!e.IsRepeat && cur != null && cur.IsCard) { LayerStep("b:" + cur.Card.Id, e.Key == Key.PageDown ? 1 : -1); Notify("BoardHintText"); }   // (0.99.2) layer back / forward
                return true;
            }
            if (boardEditing && !pad && (e.Key == Key.PageUp || e.Key == Key.PageDown))
            {
                // (Playnite also sends LT / RT as PgUp / PgDn: with a tile picked up those turn it, they don't change the step)
                if (!(boardPicked != null && RotateActive && TriggerRecent())) BoardChangeStep(e.Key == Key.PageDown ? 1 : -1);
                return true;
            }
            if (boardEditing && !pad && boardPicked != null && (e.Key == Key.Q || e.Key == Key.E)) { RotateTap(e.Key == Key.E ? 1 : -1); return true; }   // Q / E: turn it (the keyboard's LT / RT)
            // the keyboard's Y / X (as the hints show them): Z switches moving / resizing what's picked up, S opens the tile's settings
            if (boardEditing && !pad && !e.IsRepeat && boardPicked != null && e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                boardResizing = !boardResizing;
                PlaySound("move");
                BoardPlace(boardPicked);
                Notify("BoardHintText"); Notify("BoardHintWarn"); Notify("HasBoardHintWarn"); Notify("BoardGridShown");
                return true;
            }
            if (boardEditing && !pad && !e.IsRepeat && boardPicked == null && !boardPasting && boardGrouping == null && (e.Key == Key.S || e.Key == Key.Apps) && cur != null && cur.IsCard) { OpenBoardCardSettings(cur.Card); return true; }
            if (boardEditing && !pad && !e.IsRepeat && boardPicked == null && !boardPasting && boardGrouping == null && e.Key == Key.Tab) { BoardToPrecise(); return true; }   // (0.99.1: the keyboard's Start)
            // C: crop it (the keyboard's Start, Manual board)
            if (boardEditing && !pad && !e.IsRepeat && IsManualMode && boardPicked != null && e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == 0) { BoardCropShortcut(); return true; }
            if (!arrow) return false;
            if (cur == null)
            {
                var f = Keyboard.FocusedElement as UIElement;
                if (boardEditing && (f == null || f is Window || !f.IsVisible)) { FocusBoard(); return true; }   // lost: back to the board
                return false;
            }
            if (boardPicked != null) { BoardMovePicked(e.Key); return true; }
            // inside a widget with more than one button: between its buttons first
            if (inWidget != null && widgetBox != null)
            {
                var focused = Keyboard.FocusedElement as FrameworkElement;
                var inside = new List<FrameworkElement>();
                Collect(widgetBox, inside);
                var win = Application.Current.MainWindow;
                var others = inside.Where(b => !ReferenceEquals(b, focused)).Select(b => Tuple.Create(b, BoundsIn(b, win))).ToList();
                var nextIn = focused != null && others.Count > 0 ? Nearest(BoundsIn(focused, win), others, e.Key) : null;
                if (nextIn != null) { PlaySound("move"); Keyboard.Focus(nextIn); return true; }
            }
            if (!boardEditing && GroupCarouselKey(cur, e.Key)) return true;   // (0.91) a carousel group: its games move on
            var cands = BoardTiles.Where(t => !ReferenceEquals(t, cur) && BoardUsable(t)).ToList();
            var next = BoardNearest(cur, e.Key, cands);
            if (next != null) { PlaySound("move"); FocusBoardTile(next); return true; }
            if (!boardEditing && ManualPaged)
            {
                // the Manual Board's pages: the one that way, if there is one
                var cp = CurrentPage;
                int pdx = e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0, pdy = e.Key == Key.Down ? 1 : e.Key == Key.Up ? -1 : 0;
                var np2 = BPages.FirstOrDefault(p => p.X == cp.X + pdx && p.Y == cp.Y + pdy)
                          // nothing right next to it (a page was moved away): the nearest page that way
                          ?? BPages.Where(p => pdx != 0 ? Math.Sign(p.X - cp.X) == pdx && Math.Abs(p.X - cp.X) >= Math.Abs(p.Y - cp.Y)
                                                        : Math.Sign(p.Y - cp.Y) == pdy && Math.Abs(p.Y - cp.Y) >= Math.Abs(p.X - cp.X))
                                   .OrderBy(p => Math.Abs(p.X - cp.X) + Math.Abs(p.Y - cp.Y)).FirstOrDefault();
                if (np2 != null && (pdx != 0 || pdy != 0) && np2.Id != cp.Id) { BoardGoPage(np2.Id, e.Key, new Rect(cur.Left, cur.Top, cur.Width, cur.Height)); return true; }
            }
            if (!boardEditing)
            {
                bool along = settings.BoardPageAxis == 0 ? (e.Key == Key.Left || e.Key == Key.Right) : (e.Key == Key.Up || e.Key == Key.Down);
                if (along)
                {
                    int np = boardPage + (e.Key == Key.Right || e.Key == Key.Down ? 1 : -1);
                    if (np >= 0 && np < boardPages) { BoardFlip(np, e.Key, new Rect(cur.Left, cur.Top, cur.Width, cur.Height)); return true; }
                }
                if (e.Key == Key.Up) return false;   // past the top: on to the top bar
            }
            return true;   // nothing that way: stay
        }

        /// <summary>The nearest tile in a direction (by the gap between the edges, then how far off to the side).</summary>
        private static BoardTile BoardNearest(BoardTile from, Key dir, List<BoardTile> cands)
        {
            Rect a = new Rect(from.Left, from.Top, from.Width, from.Height);
            BoardTile best = null;
            double bestScore = double.MaxValue;
            foreach (var t in cands)
            {
                Rect b = new Rect(t.Left, t.Top, t.Width, t.Height);
                double gap, side;
                double acx = a.X + a.Width / 2, acy = a.Y + a.Height / 2, bcx = b.X + b.Width / 2, bcy = b.Y + b.Height / 2;
                switch (dir)
                {
                    case Key.Right:
                        if (bcx <= acx + 2 || b.Right <= a.Right - 2 || bcx < a.Right - a.Width * 0.25) continue;   // (0.94: mostly that way, not just a little)
                        gap = Math.Max(0, b.Left - a.Right); side = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom)) + Math.Abs(bcy - acy) * 0.25; break;
                    case Key.Left:
                        if (bcx >= acx - 2 || b.Left >= a.Left + 2 || bcx > a.Left + a.Width * 0.25) continue;
                        gap = Math.Max(0, a.Left - b.Right); side = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom)) + Math.Abs(bcy - acy) * 0.25; break;
                    case Key.Down:
                        if (bcy <= acy + 2 || b.Bottom <= a.Bottom - 2 || bcy < a.Bottom - a.Height * 0.25) continue;
                        gap = Math.Max(0, b.Top - a.Bottom); side = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right)) + Math.Abs(bcx - acx) * 0.25; break;
                    default:
                        if (bcy >= acy - 2 || b.Top >= a.Top + 2 || bcy > a.Top + a.Height * 0.25) continue;   // (0.94: a tile beside it a little higher isn't "up": Up goes on to the top bar)
                        gap = Math.Max(0, a.Top - b.Bottom); side = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right)) + Math.Abs(bcx - acx) * 0.25; break;
                }
                double score = gap + side * 2;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        /// <summary>
        /// The controller's X / Y / B on the Board (caught early, before Playnite's own). Editing: X a tile's settings (picked up:
        /// Move / Layer / Opacity), Y removes it (or, while one is picked up, switches between moving and resizing), Start crops it
        /// (Manual board), B puts it back / ends editing. Not editing: X keeps
        /// Playnite's selected game in step first (its own X then works on the highlighted tile's game).
        /// </summary>
        private bool BoardPadKey(ControllerInput button, KeyEventArgs e)
        {
            if (!IsBoardMode || openPanel != null || detailsOpen || confirmOpen) return false;
            if (cropTile != null) return CropPadKey(button, e);   // cropping a tile
            if (boardEditing && boardGrouping != null && BoardHasFocus()
                && (button == ControllerInput.LeftShoulder || button == ControllerInput.RightShoulder || button == ControllerInput.Start || button == ControllerInput.Back
                    || button == ControllerInput.X || button == ControllerInput.Y)) return true;   // (0.91) grouping: only A and B
            if ((button == ControllerInput.LeftShoulder || button == ControllerInput.RightShoulder) && boardEditing && BoardHasFocus())
            {
                // a tile picked up: the step; nothing picked up: undo / redo
                if (!IsPadRelease(e))
                {
                    if (boardPicked != null) BoardChangeStep(button == ControllerInput.RightShoulder ? 1 : -1);
                    else if (!boardPasting) EditUndo(button == ControllerInput.RightShoulder);
                }
                return true;
            }
            // Start (Menu) while editing the Manual board: crop the highlighted tile / the one picked up (0.80; Layer / Opacity
            // moved to X while a tile is picked up). The Board (no crop): Start still does Layer / Opacity while one is picked up.
            if (button == ControllerInput.Start && boardEditing && BoardHasFocus() && boardPicked == null && !boardPasting && boardGrouping == null && cropTile == null)
            {
                if (IsPadRelease(e)) { startLatched = false; return true; }
                if (StartLatched) return true;   // (0.99.50: the same press seen again: it already switched)
                LatchStart();
                BoardToPrecise();   // (0.99.1) precise edit: the pointer
                return true;
            }
            if (button == ControllerInput.Start && boardEditing && BoardHasFocus() && boardPicked != null)
            {
                if (IsPadRelease(e)) return true;
                if (IsManualMode) BoardCropShortcut();
                else PickExtrasKey("Start", Key.None);
                return true;
            }
            // View while editing: deletes the tile picked up (or the one highlighted), no question (Add a tile brings one back)
            if (button == ControllerInput.Back && boardEditing && BoardHasFocus())
            {
                if (IsPadRelease(e)) return true;
                BoardDeletePickedOrFocused();
                return true;
            }
            if (button != ControllerInput.X && button != ControllerInput.Y && button != ControllerInput.B) return false;
            if (!boardEditing)
            {
                if (button == ControllerInput.X && BoardHasFocus())
                {
                    BoardSelectNow();
                    var cur0 = FocusedBoardTile();
                    if (IsPadRelease(e)) return false;
                    if (cur0 == null || cur0.GameId == Guid.Empty || SelectedGameId() != cur0.GameId) { PlaySound("back"); return true; }   // not that tile's game: don't open another game's menu
                }
                return false;
            }
            if (!BoardHasFocus()) return false;   // something else has the highlight (a Playnite panel): its buttons
            if (IsPadRelease(e)) return true;
            var cur = FocusedBoardTile();
            if (button == ControllerInput.B) { BoardBack(); return true; }
            if (boardGrouping != null) return true;   // (0.91) grouping: only A and B
            if (button == ControllerInput.Y)
            {
                if (boardPicked != null)
                {
                    boardResizing = !boardResizing;
                    PlaySound("move");
                    BoardPlace(boardPicked);
                    Notify("BoardHintText"); Notify("BoardHintWarn"); Notify("HasBoardHintWarn"); Notify("BoardGridShown");
                    return true;
                }
                if (cur != null && cur.IsCard && !boardPasting) BoardRemove(cur);
                return true;
            }
            // X: a tile picked up: Move → Layer → Opacity; nothing picked up: the highlighted tile's settings
            if (boardPicked != null) { PickExtrasKey("Menu", Key.None); return true; }
            if (!boardPasting && cur != null && cur.IsCard) OpenBoardCardSettings(cur.Card);
            return true;
        }

        // =====================================================================
        // Editing
        // =====================================================================

        private void BoardStartEdit(BoardTile pick)
        {
            if (!IsBoardMode) return;
            EditBegin();   // (undo / redo: the board as it is now)
            boardEditing = true;
            boardPasting = false;
            boardUnder = 0;
            boardLiveStep.Clear();
            BoardSyncTiles();
            BoardFillPage();
            NotifyBoard();
            if (pick != null && pick.IsCard) BoardPick(pick);
            else FocusBoard();
        }

        private void BoardEndEdit(bool focus)
        {
            if (boardPicked != null) BoardCancelPick();
            if (cropTile != null) CropEnd(false);
            EditEnd();   // (the last change is an undo step)
            boardPrecise = false;
            boardEditing = false;
            boardPasting = false;
            if (boardGrouping != null) { boardGrouping = null; boardOrdering = false; CleanGroups(); }
            boardResizing = false;
            boardUnder = 0;
            SaveSettings();
            BoardSyncTiles();
            BoardFillPage();
            NotifyBoard();
            BoardRefreshSoon();
            UpdateRotTimer();
            if (focus) ui.BeginInvoke(new Action(() => FocusBoard()), DispatcherPriority.Loaded);
        }

        /// <summary>B: put the tile back where it was / stop pasting / done editing.</summary>
        private void BoardBack()
        {
            if (boardGrouping != null && boardPicked == null) { EndGrouping(); return; }   // (0.91) done grouping
            PlaySound("back");
            if (boardPicked != null) { BoardCancelPick(); BoardPreciseBack(); return; }
            if (boardPasting) { boardPasting = false; NotifyBoard(); return; }
            BoardEndEdit(true);
        }

        private void BoardEditA(BoardTile t)
        {
            if (t.IsSlot) { OpenBoardAdd(t.SlotX, t.SlotY); return; }   // (free placement's "+ Add a tile": -1, -1)
            if (boardPicked != null) { BoardDrop(); return; }
            BoardPick(t);
        }

        private void BoardPick(BoardTile t)
        {
            PlaySound("select");
            boardPicked = t;
            boardResizing = false;
            boardPickStart = new[] { t.Card.X, t.Card.Y, t.Card.W, t.Card.H };
            boardPickRot = t.Card.Rotation;
            boardPickCrop = t.Card.CropBox;   // (Start crops it while it's picked up: B puts that back too)
            var q = BoardCells(t.Card);
            t.Card.X = q.X; t.Card.Y = q.Y; t.Card.W = q.Width; t.Card.H = q.Height;
            BoardCheckUnder();
            BoardPlace(t);
            Notify("BoardHintText"); Notify("BoardHintWarn"); Notify("HasBoardHintWarn"); Notify("BoardGridShown");
            FocusBoardTile(t);
            UpdateRotTimer();   // the triggers turn it while it's picked up
        }

        /// <summary>A: put it down. It takes the place of any tiles under it (they're removed); its game stays the same.</summary>
        private void BoardDrop()
        {
            if (boardPicked == null) return;
            PlaySound("select");
            var t = boardPicked;
            var c = t.Card;
            var under = UnderOf(c);
            foreach (var o in under) BCards.Remove(o);
            EndPickExtras(true);
            boardPicked = null;
            boardResizing = false;
            boardUnder = 0;
            SaveSettings();
            BoardSyncTiles();   // the empty squares around it again
            BoardFillPage();
            Notify("BoardHintText"); Notify("BoardHintWarn"); Notify("HasBoardHintWarn"); Notify("BoardGridShown");
            NotifyBoard();
            FocusBoardTile(t);
            UpdateRotTimer();
            if (under.Any(IsGameKind)) BoardRefreshSoon();   // fewer tiles on a source: the pages again (this one keeps its game)
            BoardPreciseBack();
        }

        /// <summary>(0.99.2) A tile picked up in precise edit is down (or back): the pointer again, on it.</summary>
        private void BoardPreciseBack()
        {
            if (!boardPrecise) return;
            boardPrecise = false;
            bool fromBoard = boardPreciseFromBoard;
            ui.BeginInvoke(new Action(() => { if (boardEditing && boardPicked == null) { BoardToPrecise(); if (!fromBoard) freeFromBoardEdit = false; } }), DispatcherPriority.Loaded);
        }

        private List<BoardCard> UnderOf(BoardCard c)
        {
            if (IsFreeMode) return new List<BoardCard>();   // free placement: tiles can overlap
            var r = BoardCells(c);
            return ShownCards.Where(o => !ReferenceEquals(o, c) && Overlaps(r, BoardCells(o)) && ShapesOverlap(c, o)).ToList();
        }

        /// <summary>A tile's real outline on screen (a triangle's triangle, a circle's circle...), for "is it over another tile?".</summary>
        private Geometry TileGeometry(BoardCard c)
        {
            int s = IsPieceKind(c) || c.Kind == "shortcut" || c.Kind == "library" ? CardShapes.Square : c.Shape;
            Rect full, vis;
            if (BoardCropRects(c, out full, out vis))
            {
                // cropped: only the part that's left counts (the whole shape cut by the crop)
                var whole = BoardFullShape(s, full.Width, full.Height).Clone();
                whole.Transform = new TranslateTransform(full.X, full.Y);
                return Geometry.Combine(whole, new RectangleGeometry(vis), GeometryCombineMode.Intersect, null);
            }
            var r = BoardShapeRect(c);
            Geometry g = CardShapes.IsCut(s) ? CardShapes.Sized(s, r.Width, r.Height) : new RectangleGeometry(new Rect(0, 0, r.Width, r.Height), 10, 10);
            if (g == null) return null;
            g = g.Clone();
            g.Transform = new TranslateTransform(r.X, r.Y);
            return g;
        }

        /// <summary>Two tiles really overlap (their shapes, not just their boxes: an up and a down triangle side by side don't).</summary>
        private bool ShapesOverlap(BoardCard a, BoardCard b)
        {
            try
            {
                var ga = TileGeometry(a);
                var gb = TileGeometry(b);
                if (ga == null || gb == null) return true;
                return Geometry.Combine(ga, gb, GeometryCombineMode.Intersect, null).GetArea() > 40;
            }
            catch (Exception) { return true; }
        }

        private void BoardCancelPick()
        {
            var t = boardPicked;
            EndPickExtras(false);   // (its layer and opacity back too)
            boardPicked = null;
            boardResizing = false;
            boardUnder = 0;
            if (t != null && boardPickStart != null)
            {
                t.Card.X = boardPickStart[0]; t.Card.Y = boardPickStart[1]; t.Card.W = boardPickStart[2]; t.Card.H = boardPickStart[3];
                t.Card.Rotation = boardPickRot;   // (and its turn)
                t.Card.CropBox = boardPickCrop;   // (and its crop)
                BoardPlace(t);
                BoardFillPage();   // its picture / info for the size it's back to
                BoardPlaceWidgetsSoon();
                FocusBoardTile(t);
            }
            Notify("BoardHintText"); Notify("BoardHintWarn"); Notify("HasBoardHintWarn"); Notify("BoardGridShown");
            UpdateRotTimer();
        }

        /// <summary>
        /// D-pad while a tile is picked up, by the step (LB / RB). Moving: only that way (a tile off the step lands on its next
        /// line), its size never changes. Sizing: its top left stays put, Right / Down make it bigger and Left / Up smaller (the
        /// right / bottom edge lands on the step); a circle, hexagon or triangle grows / shrinks both ways at once, keeping its
        /// shape (it's drawn centred in its box: a box only getting wider would just slide it along). Turned, off the squares:
        /// the corner that's its own top left stays where it is on screen (it turns about its middle).
        /// (0.80: sizing used to put X / Y on the step as well, and moving W / H, which moved or resized a tile that was off it.)
        /// </summary>
        private void BoardMovePicked(Key k)
        {
            var t = boardPicked;
            var c = t.Card;
            double sx = BStepX, sy = BStepY;
            double cols = BMaxX, rows = BMaxY, x0 = BMinX, y0 = BMinY;   // (0.82, Manual: up to the screen's edges)
            int dx = k == Key.Right ? 1 : k == Key.Left ? -1 : 0, dy = k == Key.Down ? 1 : k == Key.Up ? -1 : 0;
            if (dx == 0 && dy == 0) return;
            var q = BoardCells(c);   // (where it really is on the grid)
            double x = q.X, y = q.Y, w = q.Width, h = q.Height;
            double minW = IsFreeMode ? 3 : Math.Max(0.25, sx), minH = IsFreeMode ? 3 : Math.Max(0.25, sy);
            if (!boardResizing)
            {
                if (dx != 0) x = Math.Max(x0, Math.Min(cols - w, StepLine(x, sx, dx)));
                if (dy != 0) y = Math.Max(y0, Math.Min(rows - h, StepLine(y, sy, dy)));
            }
            else if (!CardShapes.IsCut(EffShape(c)))
            {
                if (dx != 0) w = Math.Max(minW, Math.Min(cols - x, StepLine(x + w, sx, dx) - x));
                if (dy != 0) h = Math.Max(minH, Math.Min(rows - y, StepLine(y + h, sy, dy) - y));
            }
            else
            {
                Rect nr;
                if (!BoardShapeSize(c, q, dx, dy, sx, sy, minW, minH, out nr)) { PlaySound("back"); return; }
                x = nr.X; y = nr.Y; w = nr.Width; h = nr.Height;
            }
            const double same = 1e-6;
            if (Math.Abs(x - c.X) < same && Math.Abs(y - c.Y) < same && Math.Abs(w - c.W) < same && Math.Abs(h - c.H) < same) { PlaySound("back"); return; }
            PlaySound("move");
            var before = BoardTileRectOf(c);
            c.X = x; c.Y = y; c.W = w; c.H = h;
            if (boardResizing && !BoardOnSquares) BoardKeepTurnedCorner(c, before);
            int was = boardUnder;
            BoardCheckUnder();
            BoardPlace(boardPicked);
            if (boardResizing) BoardFillPage();   // its info / picture fit the new size (the same game)
            BoardPlaceWidgetsSoon();
            if (was != boardUnder) Notify("BoardHintText"); Notify("BoardHintWarn"); Notify("HasBoardHintWarn"); Notify("BoardGridShown");
        }

        private void BoardCheckUnder()
        {
            boardUnder = boardPicked != null ? UnderOf(boardPicked.Card).Count : 0;
        }

        /// <summary>The next line of the step after <paramref name="v"/> that way (dir 1 right / down, -1 left / up): one off the step lands on it.</summary>
        private static double StepLine(double v, double s, int dir)
        {
            const double eps = 1e-6;
            if (!(s > 0)) return v + dir;
            return dir > 0 ? (Math.Floor(v / s + eps) + 1) * s : (Math.Ceiling(v / s - eps) - 1) * s;
        }

        /// <summary>
        /// Sizing a circle / hexagon / triangle: both ways at once from its top left, so it keeps its shape. On whole squares a
        /// step each way (1 × 1 to 2 × 2); otherwise its box first becomes the shape's own (nothing moves on screen) and the
        /// other side follows the shape's proportions; cropped, the part that's left (and the whole tile with it) grows / shrinks
        /// in proportion. False when it can't go that way (the edge of the board, or as small as it goes).
        /// </summary>
        private bool BoardShapeSize(BoardCard c, Rect q, int dx, int dy, double sx, double sy, double minW, double minH, out Rect res)
        {
            res = q;
            const double eps = 1e-6;
            double cols = BMaxX, rows = BMaxY;
            double x = q.X, y = q.Y, w = q.Width, h = q.Height, nw, nh;
            if (BoardOnSquares)
            {
                int dir = dx != 0 ? dx : dy;
                nw = StepLine(x + w, sx, dir) - x;
                nh = StepLine(y + h, sy, dir) - y;
                if (dir > 0 && (nw > cols - x + eps || nh > rows - y + eps)) return false;   // no room for both
                nw = Math.Max(minW, nw);
                nh = Math.Max(minH, nh);
                if (Math.Abs(nw - w) < eps && Math.Abs(nh - h) < eps) return false;
                res = new Rect(x, y, nw, nh);
                return true;
            }
            if (CardCrop(c) == null)
            {
                int aw, ah;
                CardShapes.Aspect(EffShape(c), out aw, out ah);
                if (aw <= 0 || ah <= 0) return false;
                double cw = BoardAreaW / BCols, ch = BoardAreaH / BRows, gap = BoardGap;
                var px = BoardShapeRectOf(q, c);
                x = Math.Max(BMinX, (px.X - gap / 2) / cw); y = Math.Max(BMinY, (px.Y - gap / 2) / ch);
                w = (px.Width + gap) / cw; h = (px.Height + gap) / ch;
                double sw, sh;
                if (dx != 0) { sw = (StepLine(x + w, sx, dx) - x) * cw - gap; sh = sw * ah / aw; }
                else { sh = (StepLine(y + h, sy, dy) - y) * ch - gap; sw = sh * aw / ah; }
                nw = (sw + gap) / cw;
                nh = (sh + gap) / ch;
            }
            else
            {
                double k = dx != 0 ? (StepLine(x + w, sx, dx) - x) / w : (StepLine(y + h, sy, dy) - y) / h;
                nw = w * k;
                nh = h * k;
            }
            if (nw < minW - eps || nh < minH - eps) return false;               // as small as it goes
            if (x + nw > cols + eps || y + nh > rows + eps) return false;       // the edge of the board
            res = new Rect(x, y, Math.Min(nw, cols - x), Math.Min(nh, rows - y));
            return true;
        }

        /// <summary>Where a tile is drawn on the board, not rounded (its shape's box; cropped: the part that's left).</summary>
        private Rect BoardTileRectOf(BoardCard c)
        {
            Rect full, vis;
            return BoardCropRects(c, out full, out vis) ? vis : BoardShapeRect(c);
        }

        /// <summary>A turned tile was sized: it turns about its middle, so it's moved to keep the corner that's its own top left
        /// where it was on screen (otherwise it would drift as it grows).</summary>
        private void BoardKeepTurnedCorner(BoardCard c, Rect before)
        {
            double a = NormAngle(c.Rotation);
            if (Math.Abs(a) < 0.5) return;
            double rad = a * Math.PI / 180, cs = Math.Cos(rad), sn = Math.Sin(rad);
            var now = BoardTileRectOf(c);
            // the corner: the middle plus its top left turned; the new middle: that corner minus the new top left turned
            double cornerX = before.X + before.Width / 2 + (-before.Width / 2 * cs + before.Height / 2 * sn);
            double cornerY = before.Y + before.Height / 2 + (-before.Width / 2 * sn - before.Height / 2 * cs);
            double midX = cornerX + (now.Width / 2 * cs - now.Height / 2 * sn), midY = cornerY + (now.Width / 2 * sn + now.Height / 2 * cs);
            double shiftX = midX - now.Width / 2 - now.X, shiftY = midY - now.Height / 2 - now.Y;
            double cw = BoardAreaW / BCols, ch = BoardAreaH / BRows;
            c.X = Math.Max(BMinX, Math.Min(BMaxX - c.W, c.X + shiftX / cw));
            c.Y = Math.Max(BMinY, Math.Min(BMaxY - c.H, c.Y + shiftY / ch));
        }

        /// <summary>View / Delete while editing: the tile picked up goes (from where it was picked up), else the highlighted one.</summary>
        private void BoardDeletePickedOrFocused()
        {
            if (boardPasting) { PlaySound("back"); return; }
            var t = boardPicked;
            if (t != null)
            {
                BoardCancelPick();   // (back to where it was first, so the tiles it was over keep their place)
                if (t.Card == null || !BCards.Contains(t.Card)) { PlaySound("back"); return; }
                if (t.Card.Kind == "widget" && t.Card.Target != null)
                {
                    var w = Widgets.FirstOrDefault(x => x.Config.Id == t.Card.Target);
                    if (w != null) w.SetBoard(null, false);   // the widget itself stays
                }
                BoardRemove(t);
                return;
            }
            var cur = FocusedBoardTile();
            if (cur == null || !cur.IsCard) { PlaySound("back"); return; }
            BoardRemove(cur);
        }

        private void BoardRemove(BoardTile t)
        {
            PlaySound("back");
            var r = new Rect(t.Left, t.Top, t.Width, t.Height);
            bool game = IsGameKind(t.Card);
            BCards.Remove(t.Card);
            CleanGroups();   // (0.91) a group left with no tiles goes
            SaveSettings();
            BoardSyncTiles();
            NotifyBoard();
            if (game) BoardRefreshSoon();
            BoardPlaceWidgetsSoon();
            var at = BoardTiles.OrderBy(b => Math.Abs(b.Left - r.Left) + Math.Abs(b.Top - r.Top)).FirstOrDefault();
            if (at != null) FocusBoardTile(at);
        }

        // =====================================================================
        // Adding a tile: what goes on the empty square
        // =====================================================================

        private ICommand addElementCmd;
        /// <summary>Settings › Add element: the list of tiles; the new one goes on the first empty square (free placement: the middle).</summary>
        public ICommand AddElementCommand
        {
            get
            {
                return addElementCmd ?? (addElementCmd = new RelayCommand(_ =>
                {
                    if (!IsBoardMode) return;
                    int fx = -1, fy = -1;
                    if (!IsFreeMode)
                    {
                        var shown = ShownCards;
                        for (int y = 0; y < BRows && fx < 0; y++)
                            for (int x = 0; x < BCols && fx < 0; x++)
                                if (!shown.Any(c => Overlaps(new Rect(x, y, 1, 1), BoardCells(c)))) { fx = x; fy = y; }
                        if (fx < 0) { PlaySound("back"); ShowNotice(Loc("LOC_KX_NoRoomHead", "No room"), Loc("LOC_KX_NoRoom", "Every square has a tile: make the grid bigger, or remove one"), null, null, null); return; }
                    }
                    boardAddAt = new Point(fx, fy);
                    boardChangeKind = null;
                    PlaySound("select");
                    if (openPanel != "options") TogglePanel("options");
                    SetOptionsPage("boardadd");
                    boardAddFromSettings = true;
                    boardCardFromBoard = true;   // (once chosen, the board shows it)
                }));
            }
        }

        private void OpenBoardAdd(int x, int y)
        {
            boardAddFromSettings = false;
            boardAddAt = new Point(x, y);
            boardChangeKind = null;
            PlaySound("select");
            if (openPanel != "options") TogglePanel("options");
            SetOptionsPage("boardadd");
            boardCardFromBoard = true;
        }

        private void BuildBoardAddRows()
        {
            var rows = BoardAddRows;
            rows.Clear();
            bool change = boardChangeKind != null;
            var presetList = settings.BoardPresets ?? new List<BoardCard>();
            string cat = boardAddCat;
            if (cat == null)
            {
                // the categories: A opens one (B comes back here)
                Action<string, string, string, string> catRow = (id, icon, text, sub) =>
                    rows.Add(new PickRow
                    {
                        Key = "cat:" + id, Icon = icon, Text = text, Sub = sub,
                        Command = new RelayCommand(_ => { boardAddCat = id; PlaySound("select"); Notify("OptionsTitle"); BoardRowsAgain(BuildBoardAddRows, null); })
                    });
                catRow("games", "play", BoardAddCatName("games"), Loc("LOC_KX_CatGamesSub", "A game you choose, games from a list, a random game, a Library, the Store's popular games"));
                catRow("highlighted", "info", BoardAddCatName("highlighted"), Loc("LOC_KX_CatHighlightedSub", "Its logo, its info, how long to beat (they follow the highlight)"));
                catRow("shortcuts", "link", BoardAddCatName("shortcuts"), Loc("LOC_KX_AddCatShortcutsSub", "Store, achievements, gallery, search, your collections…"));
                catRow("widgets", "layout", BoardAddCatName("widgets"), Loc("LOC_KX_CatWidgetsSub", "One of your widgets, or a new one made in the tile"));
                catRow("other", "hide", BoardAddCatName("other"), Loc("LOC_KX_CatOtherSub", "Empty (keeps a space clear)"));
                if (presetList.Count > 0 && !change)
                    catRow("presets", "star", BoardAddCatName("presets"), presetList.Count == 1 ? Loc("LOC_KX_OnePreset", "1 saved") : string.Format(Loc("LOC_KX_UsePresetSub", "{0} saved"), presetList.Count));
                return;
            }
            Action<string, string, string, Func<BoardCard>> add = (key, text, sub, make) =>
            {
                rows.Add(new PickRow { Key = key, Icon = "add", Text = text, Sub = sub, Command = new RelayCommand(_ => BoardChoose(make())) });
            };
            if (cat == "games")
            {
            if (!change || IsGameKind(boardChangeKind) || boardChangeKind.Kind == "store")
                rows.Add(new PickRow
                {
                    Key = "newshape", Icon = "layout", Text = string.Format(Loc("LOC_KX_NewShapeRow", "Shape for game tiles: {0}"), ShapeName(boardNewShape)),
                    Sub = Loc("LOC_KX_NewShapeSub", "The shape of the game tile you add next"),
                    Command = new RelayCommand(_ => { boardNewShape = (boardNewShape + 1) % CardShapes.Count; PlaySound("move"); BoardRowsAgain(BuildBoardAddRows, "newshape"); })
                });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddGames", "GAMES") });
            Func<BoardCard> pinned = () => new BoardCard { Kind = "pinned", Shape = CardShapes.Square };
            Func<BoardCard> fromList = () => new BoardCard { Kind = "filter", Source = "all", Shape = CardShapes.Square };
            if (IsManualMode) add("addpinned", Loc("LOC_KX_AddPinned", "A game you choose"), Loc("LOC_KX_AddPinnedSub", "Pick it from your library"), pinned);
            add("addfilter", Loc("LOC_KX_AddFilter", "Games from a list"), Loc("LOC_KX_AddFilterSub", "Recently played, favourites, a collection, a saved filter… (tiles on the same list show the next games)"), fromList);
            if (!IsManualMode) add("addpinned", Loc("LOC_KX_AddPinned", "A game you choose"), Loc("LOC_KX_AddPinnedSub", "Pick it from your library"), pinned);
            add("addrandom", Loc("LOC_KX_AddRandom", "A random game"), Loc("LOC_KX_AddRandomSub", "A different one each time Playnite starts"), () => new BoardCard { Kind = "filter", Source = "random", Shape = CardShapes.Square });
            add("addlibrary", Loc("LOC_KX_AddLibrary", "Library"), Loc("LOC_KX_AddLibrarySub", "A tile holding the lists you choose (filters, collections…), opened as a page of games"), () => new BoardCard { Kind = "library", Sources = new List<string> { "all" } });
            add("addstore", Loc("LOC_KX_AddStore", "Popular in the store"), Loc("LOC_KX_AddStoreSub", "The Store's top sellers, a different one on each tile"), () => new BoardCard { Kind = "store", Shape = CardShapes.Square });
            }

            if (cat == "highlighted")
            {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddHighlighted", "THE HIGHLIGHTED GAME") });
            add("addlogo", Loc("LOC_KX_TileLogo", "Game logo"), Loc("LOC_KX_AddLogoSub", "Its logo (or name), following the highlight"), () => new BoardCard { Kind = "logo" });
            add("addinfo", Loc("LOC_KX_TileInfo", "Game info"), Loc("LOC_KX_AddInfoSub", "Play time, last played, achievements… (choose the lines in its settings)"), () => new BoardCard { Kind = "info", Info = new List<string> { "name", "playtime", "lastplayed", "ach" } });
            add("addhltb", Loc("LOC_KX_TileHltb", "How long to beat"), Loc("LOC_KX_AddHltbSub", "Main story, extras, completionist (HowLongToBeat add-on)"), () => new BoardCard { Kind = "hltb" });
            }

            if (cat == "shortcuts")
            {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddShortcuts", "SHORTCUTS") });
            foreach (string k in ShortcutKeys)
            {
                string key = k;
                add("addshortcut:" + k, BoardShortcutName(k), Loc("LOC_KX_AddShortcutSub", ""), () => new BoardCard { Kind = "shortcut", Target = key });
            }
            if (settings.Collections.Count > 0) rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LibGroupCollections", "COLLECTIONS") });
            foreach (var s in settings.Collections)
            {
                string key = "shelf:" + s.Id;
                add("addshortcut:" + key, string.Format(Loc("LOC_KX_AddCollectionN", "Collection: {0}"), s.Name), Loc("LOC_KX_AddShortcutSub", ""), () => new BoardCard { Kind = "shortcut", Target = key });
            }
            }

            if (cat == "widgets")
            {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddYourWidgets", "YOUR WIDGETS") });
            var used = new HashSet<string>(BCards.Where(c => c.Kind == "widget" && c.Target != null).Select(c => c.Target));
            int widgets = 0;
            if (Widgets != null)
                foreach (var w in Widgets.Where(w => w.Config != null))
                {
                    string id = w.Config.Id;
                    bool there = used.Contains(id) && !(change && boardChangeKind.Target == id);
                    widgets++;
                    rows.Add(new PickRow
                    {
                        Key = "addwidget:" + id, Icon = "layout", Text = w.Name, IsInfo = there,
                        Sub = there ? Loc("LOC_KX_AddWidgetUsed", "Already on the board") : Loc("LOC_KX_AddWidgetSub", "Shown in this tile (only here, in this layout)"),
                        Command = new RelayCommand(_ => { if (there) { PlaySound("back"); return; } BoardChoose(new BoardCard { Kind = "widget", Target = id }); })
                    });
                }
            if (widgets == 0) rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "nowidgets", Text = Loc("LOC_KX_NoWidgetsYet", "None yet: make one below") });
            // a new widget made straight in this tile
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddNewWidgets", "A NEW WIDGET") });
            foreach (string wt in WidgetTypeKeys.Concat(BoardCustomTypes()))   // (your own tiles and the community widgets too)
            {
                string type = wt;
                rows.Add(new PickRow
                {
                    Key = "addnewwidget:" + wt, Icon = "add", Text = string.Format(Loc("LOC_KX_AddNewWidget", "New widget: {0}"), WidgetTypeName(wt)),
                    Sub = Loc("LOC_KX_AddNewWidgetSub2", "Made in this tile (its look: Settings & Customisation › Home settings › Widgets)"),
                    Command = new RelayCommand(_ =>
                    {
                        string id = CreateWidgetForBoard(type);
                        BoardChoose(new BoardCard { Kind = "widget", Target = id });
                    })
                });
            }

            }

            if (cat == "other")
            {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddOther", "OTHER") });
            add("addempty", Loc("LOC_KX_TileEmpty", "Empty (kept clear)"), Loc("LOC_KX_AddEmptySub", "Keeps this space free of tiles (the background shows)"), () => new BoardCard { Kind = "empty" });
            }

            var presets = presetList;
            if (cat == "presets" && presets.Count > 0 && !change)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_YourPresets", "YOUR TILE PRESETS") });
                foreach (var p in presets)
                {
                    var preset = p;
                    rows.Add(new PickRow
                    {
                        Key = "addpreset:" + p.Id, Icon = "star", Text = p.Name ?? "?", Sub = PresetSub(p),
                        Command = new RelayCommand(_ =>
                        {
                            var made = preset.Copy();
                            made.Sources = new List<string>(preset.Sources ?? new List<string>());   // its own lists, not the preset's
                            BoardChoose(made);
                        })
                    });
                }
            }
        }

        /// <summary>A Library tile's lists: its own page, in groups.</summary>
        private void BuildBoardLibraryRows()
        {
            var rows = BoardLibraryRows;
            rows.Clear();
            var c = boardEditCard;
            if (c == null || !BCards.Contains(c) || c.Kind != "library") { rows.Add(new PickRow { IsInfo = true, Text = Loc("LOC_KX_BoardNoCard2", "That tile isn't on the board any more") }); return; }
            if (c.Sources == null) c.Sources = new List<string>();
            Action again = () => BuildBoardLibraryRows();
            Action<string, List<string>> group = (head, keys) =>
            {
                if (keys.Count == 0) return;
                rows.Add(new PickRow { IsHeader = true, Text = head });
                foreach (string k in keys)
                {
                    string key = k;
                    rows.Add(BoardSwitch(k.StartsWith("filter:", StringComparison.Ordinal) ? "filter" : "shelf", "lib:" + k, BoardSourceName(k), c.Sources.Contains(k), Loc("LOC_KX_LibIn", "In this library"), Loc("LOC_KX_Off", "Off"), () =>
                    {
                        if (c.Sources.Contains(key)) c.Sources.Remove(key); else c.Sources.Add(key);
                        PlaySound("move");
                        BoardChanged(false);
                        BoardRowsAgain(again, "lib:" + key);
                    }));
                }
            };
            group(Loc("LOC_KX_LibGroupGames", "YOUR GAMES"), new List<string> { "all", "played", "favourites", "recent", "installed", "never" });
            var filters = new List<string>();
            try { foreach (var p in api.Database.FilterPresets.OrderBy(p => p.Name)) filters.Add("filter:" + p.Id); } catch (Exception) { }
            group(Loc("LOC_KX_LibGroupFilters", "SAVED FILTERS"), filters);
            group(Loc("LOC_KX_LibGroupCollections", "COLLECTIONS"), settings.Collections.Select(sh => "shelf:" + sh.Id).ToList());
        }

        private string BoardAddCatName(string cat)
        {
            switch (cat)
            {
                case "games": return Loc("LOC_KX_CatGames", "Games");
                case "highlighted": return Loc("LOC_KX_CatHighlighted", "The highlighted game");
                case "shortcuts": return Loc("LOC_KX_CatShortcuts", "Shortcuts");
                case "widgets": return Loc("LOC_KX_CatWidgets", "Widgets");
                case "presets": return Loc("LOC_KX_CatPresets", "Your tile presets");
                default: return Loc("LOC_KX_CatOther", "Other");
            }
        }

        private string KindName(BoardCard c)
        {
            switch (c.Kind)
            {
                case "pinned": return Loc("LOC_KX_AddPinned", "A game you choose");
                case "store": return Loc("LOC_KX_AddStore", "Popular in the store");
                case "logo": return Loc("LOC_KX_TileLogo", "Game logo");
                case "info": return Loc("LOC_KX_TileInfo", "Game info");
                case "hltb": return Loc("LOC_KX_TileHltb", "How long to beat");
                case "empty": return Loc("LOC_KX_TileEmpty", "Empty (kept clear)");
                case "widget":
                    {
                        var w = Widgets != null ? Widgets.FirstOrDefault(x => x.Config != null && x.Config.Id == c.Target) : null;
                        return w != null ? string.Format(Loc("LOC_KX_TileWidgetN", "Widget: {0}"), w.Name) : Loc("LOC_KX_TileWidgetGone", "Widget (removed)");
                    }
                case "shortcut": return string.Format(Loc("LOC_KX_ShortcutN", "Shortcut: {0}"), BoardShortcutName(c.Target));
                case "library": return string.Format(Loc("LOC_KX_LibraryN", "Library: {0}"), LibraryName(c));
            }
            return c.Source == "random" ? Loc("LOC_KX_AddRandom", "A random game") : Loc("LOC_KX_AddFilter", "Games from a list");
        }

        private string PresetSub(BoardCard p)
        {
            return KindName(p) + "   ·   " + (IsPieceKind(p) ? "" : ShapeName(p.Shape) + "   ·   ") + string.Format("{0} × {1}", Squares(p.W), Squares(p.H));
        }

        /// <summary>A tile type was chosen: a new tile on the empty square (or, from a tile's settings, that tile changes type).</summary>
        private void BoardChoose(BoardCard made)
        {
            PlaySound("select");
            BoardCard c;
            if (boardChangeKind != null)
            {
                c = boardChangeKind;
                boardChangeKind = null;
                c.Kind = made.Kind;
                c.Target = made.Target;
                if (made.Kind == "filter") c.Source = made.Source ?? "all";
                if (made.Kind == "info") c.Info = made.Info;
                if (made.Kind == "pinned" && string.IsNullOrEmpty(c.GameId)) { var t0 = BoardTiles.FirstOrDefault(b => ReferenceEquals(b.Card, c)); if (t0 != null && t0.GameId != Guid.Empty) c.GameId = t0.GameId.ToString(); }
                boardEditCard = c;
                BoardChanged(true);
                BoardPlaceWidgetsSoon();
                if (c.Kind == "pinned" && string.IsNullOrEmpty(c.GameId)) { BoardPickGame(c); return; }
                GoBackTo("boardcard", "kind");
                return;
            }
            c = made;
            c.Id = Guid.NewGuid().ToString("N");
            c.Page = IsManualMode && CurrentPage != null && CurrentPage.Id != "home" ? CurrentPage.Id : null;
            c.X = boardAddAt.X;
            c.Y = boardAddAt.Y;
            BoardOneWidget(c);
            if (made.Name == null && (IsGameKind(c) || c.Kind == "store"))
            {
                // the chosen shape, at its natural size (tall: twice as high, wide: twice as wide) where there's room
                c.Shape = boardNewShape;
                if (!IsFreeMode) { c.W = c.Shape == CardShapes.Wide ? 2 : 1; c.H = c.Shape == CardShapes.Tall ? 2 : 1; }
            }
            bool freeNew = IsFreeMode;
            if (freeNew)
            {
                // free placement: a sensible size for what it is (a preset keeps its own), in the middle, then picked up to place
                if (made.Name == null) { var sz = FreeSize(c); c.W = sz.Width; c.H = sz.Height; }
                c.W = Math.Min(c.W, FreeCols); c.H = Math.Min(c.H, FreeRows);
                c.X = Math.Round((FreeCols - c.W) / 2);
                c.Y = Math.Round((FreeRows - c.H) / 2);
                made = c;
            }
            // a preset's size, as far as there's room (never over another tile)
            double w = Math.Max(1, Math.Min(made.W, BCols - c.X)), h = Math.Max(1, Math.Min(made.H, BRows - c.Y));
            if (freeNew) { w = c.W; h = c.H; }
            c.W = w; c.H = h;
            while ((c.W > 1 || c.H > 1) && UnderOf(c).Count > 0) { if (c.W >= c.H && c.W > 1) c.W = Math.Max(1, c.W - 1); else c.H = Math.Max(1, c.H - 1); }
            if (UnderOf(c).Count > 0) { c.W = 1; c.H = 1; }
            BCards.Add(c);
            boardFocusId = c.Id;
            boardCardFromBoard = false;
            if (!boardEditing) boardEditing = true;
            SaveSettings();
            focusBeforePanel = null;   // (the empty square it was opened from is gone)
            ClosePanels();
            BoardSyncTiles();
            BoardFillPage();
            NotifyBoard();
            BoardRefreshSoon();
            BoardPlaceWidgetsSoon();
            if (freeNew) boardPlaceAfter = c;   // free placement: then it's picked up, to move into place
            if (c.Kind == "pinned" && string.IsNullOrEmpty(c.GameId)) { BoardPickGame(c); return; }
            ui.BeginInvoke(new Action(() => BoardFocusNew(c)), DispatcherPriority.ApplicationIdle);
        }

        private BoardCard boardPlaceAfter;
        private int boardNewShape = CardShapes.Square;   // Add a tile: the shape new game tiles get
        private bool boardAddFromSettings;
        private string lastSettingsPage;               // Add element (from the settings): Back returns to the settings page

        /// <summary>The highlight onto a tile just added (free placement: picked up, ready to move).</summary>
        private void BoardFocusNew(BoardCard c)
        {
            var t = BoardTiles.FirstOrDefault(b => ReferenceEquals(b.Card, c));
            if (t == null) { FocusBoard(); return; }
            if (ReferenceEquals(boardPlaceAfter, c) && boardEditing) { boardPlaceAfter = null; BoardPick(t); return; }
            boardPlaceAfter = null;
            FocusBoardTile(t);
        }

        /// <summary>Choose a game for a tile: the search (type a few letters, A on the game). It's then always that game.</summary>
        /// <summary>Free placement: how big a new tile starts (in fine squares).</summary>
        private static Size FreeSize(BoardCard c)
        {
            switch (c.Kind)
            {
                case "logo": return new Size(26, 10);
                case "info": return new Size(24, 12);
                case "hltb": return new Size(22, 10);
                case "widget": return new Size(26, 15);
                case "shortcut": return new Size(10, 10);
                case "library": return new Size(12, 12);
                case "empty": return new Size(10, 10);
            }
            if (c.Shape == CardShapes.Tall) return new Size(10, 15);
            if (c.Shape == CardShapes.Wide) return new Size(20, 11);
            return new Size(12, 12);
        }

        private void BoardPickGame(BoardCard c)
        {
            OpenSearchToPick(id =>
            {
                if (!BCards.Contains(c)) return;
                c.Kind = "pinned";
                c.GameId = id.ToString();
                boardFocusId = c.Id;
                BoardChanged(true);
                ui.BeginInvoke(new Action(() => BoardFocusNew(c)), DispatcherPriority.ApplicationIdle);
            });
        }

        // =====================================================================
        // Settings: the Board page, a tile's page, its source, tile types, presets
        // =====================================================================

        /// <summary>Back from the Board's settings pages (a tile's page opened from the Board goes back to the Board).</summary>
        private bool BoardSettingsBack()
        {
            if (openPanel != "options") return false;
            if (optionsPage == "boardsource" && boardSourceGroup != null) { boardSourceGroup = null; GoBackTo("boardgroup", "gsource"); BuildBoardGroupRows("gsource"); return true; }
            if (optionsPage == "boardsource") { GoBackTo("boardcard", "source"); return true; }
            if (optionsPage == "boardgroup") { GoBackTo("boardcard", "groupsettings"); return true; }
            if (optionsPage == "boardlibrary") { GoBackTo("boardcard", "libholds"); return true; }
            if (optionsPage == "boardadd" && boardAddCat != null)
            {
                string cat = boardAddCat;
                boardAddCat = null;
                PlaySound("back");
                Notify("OptionsTitle");
                BoardRowsAgain(BuildBoardAddRows, "cat:" + cat);
                return true;
            }
            if (optionsPage == "boardcard" && BoardSizeClash())
            {
                // it was made bigger over other tiles: move it, or A to let it take their place (B: its old size)
                ClosePanels();
                BoardResolveSizeClash();
                return true;
            }
            if (optionsPage == "boardpresets") { GoBackTo("boardcard", "usepreset"); return true; }
            if (optionsPage == "boardpage") { GoBackTo("boardpages", "page:" + boardEditPageId); return true; }
            if (optionsPage == "boardpages") { GoBackTo("board", "pages"); return true; }
            if (optionsPage == "boardadd" && boardChangeKind != null) { boardChangeKind = null; GoBackTo("boardcard", "kind"); return true; }
            if (optionsPage == "boardadd" && boardAddFromSettings) { boardAddFromSettings = false; boardCardFromBoard = false; GoBackTo(lastSettingsPage ?? "board", "addelement"); return true; }
            if ((optionsPage == "boardcard" || optionsPage == "boardadd") && boardCardFromBoard)
            {
                boardCardFromBoard = false;
                string id = optionsPage == "boardcard" && boardEditCard != null ? boardEditCard.Id : null;
                bool fromAdd = optionsPage == "boardadd";
                var at = boardAddAt;
                ClosePanels();
                ui.BeginInvoke(new Action(() =>
                {
                    var t = id != null ? BoardTiles.FirstOrDefault(b => b.Card != null && b.Card.Id == id)
                          : fromAdd ? BoardTiles.FirstOrDefault(b => b.IsSlot && b.SlotX == (int)at.X && b.SlotY == (int)at.Y) : null;
                    if (t != null) FocusBoardTile(t); else FocusBoard();
                }), DispatcherPriority.ApplicationIdle);
                return true;
            }
            return false;
        }

        private void BoardOptionsOpened(string page)
        {
            foreach (string n in new[] { "IsOptionsBoard", "IsOptionsBoardCard", "IsOptionsBoardSource", "IsOptionsBoardAdd", "IsOptionsBoardPresets", "IsOptionsBoardPages", "IsOptionsBoardPage", "IsOptionsBoardLibrary", "IsOptionsBoardGroup" }) Notify(n);
            if (page != "boardsource") boardSourceGroup = null;
            if (page == "boardlibrary") BuildBoardLibraryRows();
            if (page == "widgetadd") BuildWidgetAddMoreRows();   // (your own tiles, community widgets)
            if (page == "boardadd") boardAddCat = null;
            if (page != "boardcard" && page != "boardsource" && page != "boardadd" && page != "boardpresets" && page != "boardlibrary" && page != "boardgroup" && BoardSizeClash())
            {
                // left the tile's pages some other way: sort out the tiles it covers on the board
                ClosePanels();
                BoardResolveSizeClash();
                return;
            }
            if (page != "boardcard" && page != "boardsource" && page != "boardadd" && page != "boardpresets" && page != "boardlibrary" && page != "boardgroup") { boardSizeCard = null; boardSizeBefore = null; }
            if (page == "boardpages") BuildBoardPagesRows();
            if (page == "boardpage") BuildBoardPageRows();
            if (page == "board" || page == "cards") lastSettingsPage = page;
            if (page == "board") BuildBoardRows();
            if (page == "boardcard") BuildBoardCardRows();
            if (page == "boardsource") BuildBoardSourceRows();
            if (page == "boardadd") BuildBoardAddRows();
            if (page == "boardpresets") BuildBoardPresetRows();
            if (page != "boardcard" && page != "boardsource" && page != "boardadd" && page != "boardpresets" && page != "boardlibrary" && page != "boardgroup") { boardCardFromBoard = false; boardChangeKind = null; }
        }

        private void OpenBoardCardSettings(BoardCard c)
        {
            if (c == null) return;
            PlaySound("select");
            boardEditCard = c;
            if (openPanel != "options") TogglePanel("options");
            SetOptionsPage("boardcard");
            boardCardFromBoard = true;
        }

        /// <summary>An on / off row: drawn with the switch track (IsSelected is on), with its own icon on the left.</summary>
        private static PickRow BoardSwitch(string icon, string key, string text, bool on, string onSub, string offSub, Action toggle)
        {
            return new PickRow { Key = key, Text = text, IsSelected = on, IsSwitch = true, Sub = on ? onSub : offSub, Icon = icon, Command = new RelayCommand(_ => toggle()) };
        }

        /// <summary>The line icon for a line of game info (Board tiles, Play info shows).</summary>
        private static string InfoIcon(string k)
        {
            switch (k)
            {
                case "name": return "note";
                case "playtime": return "clock";
                case "lastplayed": return "calendar";
                case "hltb": return "hourglass";
                case "ach": return "trophy";
                case "installed": return "download";
                case "status": return "flag";
                case "platform": return "controller";
                case "source": return "store";
                case "year": return "calendar";
                case "genre": return "tag";
            }
            return "info";
        }

        /// <summary>Builds a page's rows again and puts the highlight back on a row (rows come and go as settings change).</summary>
        private void BoardRowsAgain(Action build, string key)
        {
            build();
            ui.BeginInvoke(new Action(() => { if (key == null || !UiTools.FocusCommandParameter("KX_OptionsPanel", key)) UiTools.FocusInside("KX_OptionsPanel"); }), DispatcherPriority.ContextIdle);
        }

        private void BoardChanged(bool regames)
        {
            SaveSettings();
            BoardSyncTiles();
            BoardFillPage();
            NotifyBoard();
            BoardLiveTimer();
            BoardPlaceWidgetsSoon();
            if (regames) BoardRefreshSoon();
        }

        // ---- the Board / Manual page ----

        private void BuildBoardRows()
        {
            var rows = BoardRows;
            rows.Clear();
            rows.Add(new PickRow
            {
                Key = "edit", Icon = "cursor", Text = Loc("LOC_KX_BoardEdit", "Edit the board"),
                Sub = Loc("LOC_KX_BoardEditSub2", "Add, move, resize and remove tiles (or hold A on a tile)"),
                Command = EditBoardCommand
            });
            rows.Add(new PickRow
            {
                Key = "addelement", Icon = "add", Text = Loc("LOC_KX_AddElement", "Add element"),
                Sub = Loc("LOC_KX_AddElementSub", "A game tile, library, logo, game info, How long to beat, widget, shortcut…"),
                Command = AddElementCommand
            });
            if (IsManualMode)
                rows.Add(new PickRow
                {
                    Key = "pages", Icon = "layout", Text = Loc("LOC_KX_Pages", "Pages"),
                    Sub = string.Format(Loc("LOC_KX_PagesSub", "{0} · add pages to the right, left or below, and arrange them"), BPages.Count == 1 ? Loc("LOC_KX_OnePage", "1 page") : string.Format(Loc("LOC_KX_NPages", "{0} pages"), BPages.Count)),
                    Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("boardpages"); })
                });
            if (IsManualMode)
            {
                // (0.86) straight from here too: an empty page to the right, or a copy of Home
                rows.Add(new PickRow { Key = "quickaddpage", Icon = "add", Text = Loc("LOC_KX_AddPageRight", "Add a page to the right"),
                                       Sub = Loc("LOC_KX_QuickAddPageSub", "An empty page: scroll right to it and fill it"),
                                       Command = new RelayCommand(_ => { BoardAddPage(1, 0); BoardRowsAgain(BuildBoardRows, "quickaddpage"); }) });
                rows.Add(new PickRow { Key = "quickaddcopy", Icon = "layout", Text = Loc("LOC_KX_AddHomeCopy", "Add a copy of Home"),
                                       Sub = Loc("LOC_KX_AddHomeCopySub", "A new page to the right with Home's tiles on it, to change from there"),
                                       Command = new RelayCommand(_ => { BoardAddHomeCopy(); BoardRowsAgain(BuildBoardRows, "quickaddcopy"); }) });
            }
            if (!IsManualMode)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BoardQuick", "QUICK SETUP") });
                foreach (int n in new[] { 3, 4, 5 })
                {
                    int size = n;
                    rows.Add(new PickRow
                    {
                        Key = "q" + n, Icon = "grid", Text = string.Format(Loc("LOC_KX_BoardQuickN", "{0} × {0} grid"), n),
                        Sub = Loc("LOC_KX_BoardQuickSub2", "Replaces the tiles with an even grid of games"),
                        Command = new RelayCommand(_ => BoardQuickSetup(size))
                    });
                }
                if (boardUndo != null)
                    rows.Add(new PickRow
                    {
                        Key = "undo", Icon = "refresh", Text = Loc("LOC_KX_BoardUndo", "Undo quick setup"), Sub = Loc("LOC_KX_BoardUndoSub2", "The tiles you had before"),
                        Command = new RelayCommand(_ => BoardUndoQuick())
                    });
            }
            if (IsManualMode)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_Placement", "PLACEMENT") });
                rows.Add(new PickRow
                {
                    Key = "placement", Icon = "move", Text = settings.ManualPlacement == 1 ? Loc("LOC_KX_PlaceFree", "Free placement") : Loc("LOC_KX_PlaceGrid", "Grid"),
                    Sub = settings.ManualPlacement == 1 ? Loc("LOC_KX_PlaceFreeSub", "Tiles anywhere on the screen, any size") : Loc("LOC_KX_PlaceGridSub", "Tiles on a grid of squares"),
                    Command = new RelayCommand(_ =>
                    {
                        if (boardEditing) BoardEndEdit(false);
                        settings.ManualPlacement = 1 - settings.ManualPlacement;   // each keeps its own tiles (and pages)
                        boardPage = 0;
                        boardPageId = "home";
                        boardFocusId = null;
                        boardGames = new Dictionary<string, Guid[]>();
                        BoardTiles.Clear();
                        PlaySound("move");
                        BoardChanged(true);
                        Notify("OptionsTitle");
                        BoardRowsAgain(BuildBoardRows, "placement");
                    })
                });
            }
            if (IsFreeMode) goto pages;
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BoardGrid", "GRID") });
            string growSub = IsManualMode ? Loc("LOC_KX_ManualGridSub", "The squares tiles snap to") : Loc("LOC_KX_BoardGridSub", "New squares get a game tile; tiles past a smaller grid's edge come back when it grows");
            rows.Add(new PickRow
            {
                Key = "cols", Icon = "grid", Text = string.Format(Loc("LOC_KX_BoardColsN", "Columns: {0}"), BCols), Sub = growSub,
                Command = new RelayCommand(_ => { int max = IsManualMode ? 16 : 8; BoardSetGrid(BCols >= max ? 2 : BCols + 1, BRows); BoardRowsAgain(BuildBoardRows, "cols"); PlaySound("move"); })
            });
            rows.Add(new PickRow
            {
                Key = "rows", Icon = "grid", Text = string.Format(Loc("LOC_KX_BoardRowsN", "Rows: {0}"), BRows), Sub = growSub,
                Command = new RelayCommand(_ => { int max = IsManualMode ? 9 : 6; BoardSetGrid(BCols, BRows >= max ? 1 : BRows + 1); BoardRowsAgain(BuildBoardRows, "rows"); PlaySound("move"); })
            });
            rows.Add(BoardSwitch("move", "snap", Loc("LOC_KX_BoardSnap", "Snap to the grid"), BSnap,
                                 Loc("LOC_KX_BoardSnapOn2", "On: tiles move and size a whole square at a time"), Loc("LOC_KX_BoardSnapOff2", "Off: tiles move and size in small steps"),
                                 () => { BSnap = !BSnap; SaveSettings(); PlaySound("move"); BoardRowsAgain(BuildBoardRows, "snap"); }));
            pages:
            if (BCards.Any(c => c.Kind == "filter" || c.Kind == "store"))
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BoardPages", "PAGES") });
                rows.Add(new PickRow
                {
                    Key = "axis", Icon = "forward", Text = Loc("LOC_KX_BoardAxis", "Next page of games"),
                    Sub = settings.BoardPageAxis == 1 ? Loc("LOC_KX_BoardAxisUD", "Up / Down past the edge") : Loc("LOC_KX_BoardAxisLR", "Left / Right past the edge"),
                    Command = new RelayCommand(_ => { settings.BoardPageAxis = 1 - settings.BoardPageAxis; BoardMakeDots(); BoardChanged(false); BoardRowsAgain(BuildBoardRows, "axis"); PlaySound("move"); })
                });
                // (1.0.3) a page turn: the highlight stays on its tile (hidden with one game at a time, which always keeps it)
                if (!settings.BoardSlideOne)
                    rows.Add(BoardSwitch("cursor", "keepspot", Loc("LOC_KX_BoardKeepSpot", "Stay on the same tile"), settings.BoardKeepSpot,
                                         Loc("LOC_KX_BoardKeepSpotOn", "After a page turn the highlight stays where it was"),
                                         Loc("LOC_KX_BoardKeepSpotOff", "Off: after a page turn the highlight goes to the side the new page came in from"),
                                         () => { settings.BoardKeepSpot = !settings.BoardKeepSpot; SaveSettings(); BoardRowsAgain(BuildBoardRows, "keepspot"); PlaySound("move"); }));
                // (1.0.2) a whole page, or one game at a time
                rows.Add(BoardSwitch("forward", "slideone", Loc("LOC_KX_BoardSlideOne", "One game at a time"), settings.BoardSlideOne,
                                     Loc("LOC_KX_BoardSlideOneOn", "Past the edge, every tile moves along one game and one new game comes in"),
                                     Loc("LOC_KX_BoardSlideOneOff", "Off: past the edge, a whole page of new games"),
                                     () => { settings.BoardSlideOne = !settings.BoardSlideOne; boardPage = 0; BoardChanged(true); BoardRowsAgain(BuildBoardRows, "slideone"); PlaySound("move"); }));
                rows.Add(new PickRow
                {
                    Key = "indicator", Icon = "dots", Text = Loc("LOC_KX_BoardIndicator", "Page indicator"),
                    Sub = settings.BoardIndicator == 2 ? Loc("LOC_KX_BoardIndicatorHidden", "Hidden") : settings.BoardIndicator == 1 ? Loc("LOC_KX_BoardIndicatorNum", "Page number (2 / 5)") : Loc("LOC_KX_BoardIndicatorDots", "Dots"),
                    Command = new RelayCommand(_ => { settings.BoardIndicator = (settings.BoardIndicator + 1) % 3; BoardChanged(false); BoardRowsAgain(BuildBoardRows, "indicator"); PlaySound("move"); })   // Dots › Page number › Hidden
                });
            }
            if (BCards.Any(c => c.Live && c.Kind == "filter"))
                rows.Add(new PickRow
                {
                    Key = "livesec", Icon = "clock", Text = Loc("LOC_KX_BoardLiveEvery2", "Live tiles change every"),
                    Sub = string.Format(Loc("LOC_KX_BoardSeconds", "{0} seconds"), settings.BoardLiveSeconds),
                    Command = new RelayCommand(_ =>
                    {
                        int[] steps = { 4, 6, 8, 10, 15, 20, 30 };
                        int i = Array.IndexOf(steps, settings.BoardLiveSeconds);
                        settings.BoardLiveSeconds = steps[(i + 1) % steps.Length];
                        BoardChanged(false);
                        BoardRowsAgain(BuildBoardRows, "livesec");
                        PlaySound("move");
                    })
                });
        }

        /// <summary>
        /// A new grid size. Bigger (the Board): every new empty square gets a game tile like its neighbour, so a 4 × 4 board
        /// set to 6 × 6 shows 36 games. Smaller: tiles past the edge are hidden (kept), and come back when it grows again.
        /// </summary>
        private void BoardSetGrid(int cols, int rows)
        {
            int oldCols = BCols, oldRows = BRows;
            BCols = cols;
            BRows = rows;
            if (!IsManualMode && (cols > oldCols || rows > oldRows))
            {
                var shown = ShownCards;
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        if (x < oldCols && y < oldRows) continue;   // only the new squares
                        var cell = new Rect(x, y, 1, 1);
                        if (shown.Any(c => Overlaps(cell, BoardCells(c)))) continue;
                        // like the tile to its left (or above) when that's a one-square game tile
                        var near = shown.FirstOrDefault(c => c.Kind == "filter" && Math.Abs(c.W - 1) < 0.01 && Math.Abs(c.H - 1) < 0.01 && Math.Abs(c.Y - y) < 0.01 && Math.Abs(c.X - (x - 1)) < 0.01)
                                ?? shown.FirstOrDefault(c => c.Kind == "filter" && Math.Abs(c.W - 1) < 0.01 && Math.Abs(c.H - 1) < 0.01 && Math.Abs(c.X - x) < 0.01 && Math.Abs(c.Y - (y - 1)) < 0.01);
                        var n = near != null ? near.Copy() : new BoardCard { Kind = "filter", Source = "all", Shape = CardShapes.Square };
                        n.X = x; n.Y = y; n.W = 1; n.H = 1;
                        BCards.Add(n);
                        shown.Add(n);
                    }
            }
            BoardChanged(true);
        }

        /// <summary>Quick setup: an even n × n grid of games (what you had can be put back).</summary>
        private void BoardQuickSetup(int n)
        {
            PlaySound("select");
            boardUndo = BCards.Select(c => c.Snapshot()).ToList();
            boardUndoCols = BCols;
            boardUndoRows = BRows;
            BCols = n;
            BRows = n;
            var cards = new List<BoardCard>();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    cards.Add(new BoardCard { X = x, Y = y, Shape = CardShapes.Square, Source = "all" });
            BCards = cards;
            boardPage = 0;
            BoardChanged(true);
            BoardRowsAgain(BuildBoardRows, "q" + n);
        }

        private void BoardUndoQuick()
        {
            if (boardUndo == null) return;
            PlaySound("select");
            BCards = boardUndo;
            BCols = boardUndoCols;
            BRows = boardUndoRows;
            boardUndo = null;
            boardPage = 0;
            BoardChanged(true);
            BoardRowsAgain(BuildBoardRows, "q3");
        }

        // ---- Manual Board pages: the list, adding, arranging ----

        private string PagePlace(BoardPage p)
        {
            if (p.Id == "home") return Loc("LOC_KX_PageIsHome", "The page you start on");
            var parts = new List<string>();
            if (p.X > 0) parts.Add(string.Format(Loc("LOC_KX_PageRight", "{0} right"), p.X));
            if (p.X < 0) parts.Add(string.Format(Loc("LOC_KX_PageLeft", "{0} left"), -p.X));
            if (p.Y > 0) parts.Add(string.Format(Loc("LOC_KX_PageDown", "{0} down"), p.Y));
            if (p.Y < 0) parts.Add(string.Format(Loc("LOC_KX_PageUp", "{0} up"), -p.Y));
            var home = BPages.FirstOrDefault(x => x.Id == "home");
            if (home != null && (home.X != 0 || home.Y != 0))
            {
                parts.Clear();
                int dx = p.X - home.X, dy = p.Y - home.Y;
                if (dx > 0) parts.Add(string.Format(Loc("LOC_KX_PageRight", "{0} right"), dx));
                if (dx < 0) parts.Add(string.Format(Loc("LOC_KX_PageLeft", "{0} left"), -dx));
                if (dy > 0) parts.Add(string.Format(Loc("LOC_KX_PageDown", "{0} down"), dy));
                if (dy < 0) parts.Add(string.Format(Loc("LOC_KX_PageUp", "{0} up"), -dy));
            }
            return string.Format(Loc("LOC_KX_PageFromHome", "{0} from Home"), string.Join(", ", parts.ToArray()));
        }

        private int BoardPageTileCount(string id) { return BCards.Count(c => PageOf(c) == id); }

        private void BuildBoardPagesRows()
        {
            var rows = BoardPagesRows;
            rows.Clear();
            var cur = CurrentPage;
            if (cur == null) return;
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecAddPage", "ADD A PAGE") });
            string nextTo = string.Format(Loc("LOC_KX_AddPageNextTo", "Next to {0}"), cur.Name ?? "");
            foreach (var d in new[] { new[] { "right", "1", "0" }, new[] { "left", "-1", "0" }, new[] { "below", "0", "1" } })
            {
                int dx = int.Parse(d[1]), dy = int.Parse(d[2]);
                string key = "add" + d[0];
                rows.Add(new PickRow
                {
                    Key = key, Icon = "add", Sub = nextTo,
                    Text = d[0] == "right" ? Loc("LOC_KX_AddPageRight", "Add a page to the right") : d[0] == "left" ? Loc("LOC_KX_AddPageLeft", "Add a page to the left") : Loc("LOC_KX_AddPageBelow", "Add a page below"),
                    Command = new RelayCommand(_ => BoardAddPage(dx, dy))
                });
            }
            rows.Add(new PickRow
            {
                Key = "addcopy", Icon = "layout", Text = Loc("LOC_KX_AddHomeCopy", "Add a copy of Home"),
                Sub = Loc("LOC_KX_AddHomeCopySub", "A new page to the right with Home's tiles on it, to change from there"),
                Command = new RelayCommand(_ => BoardAddHomeCopy())
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecYourPages", "YOUR PAGES") });
            foreach (var p in BPages.OrderBy(x => x.Y).ThenBy(x => x.X))
            {
                string id = p.Id;
                int n = BoardPageTileCount(id);
                rows.Add(new PickRow
                {
                    Key = "page:" + id, Icon = id == "home" ? "star" : "layout", Text = p.Name, IsSelected = id == boardPageId,
                    Sub = PagePlace(p) + "   ·   " + (n == 1 ? Loc("LOC_KX_OneTile", "1 tile") : string.Format(Loc("LOC_KX_NTiles", "{0} tiles"), n)),
                    Command = new RelayCommand(_ => { boardEditPageId = id; PlaySound("select"); SetOptionsPage("boardpage"); })
                });
            }
        }

        /// <summary>A new page next to this one (pages already there move along to make room); you're taken to it.</summary>
        private void BoardAddPage(int dx, int dy)
        {
            var pages = BPages;
            var cur = CurrentPage;
            int tx = cur.X + dx, ty = cur.Y + dy;
            if (pages.Any(p => p.X == tx && p.Y == ty))
            {
                foreach (var p in pages)
                {
                    if (dx != 0 && p.Y == ty && (dx > 0 ? p.X >= tx : p.X <= tx)) p.X += dx;
                    if (dy != 0 && p.X == tx && (dy > 0 ? p.Y >= ty : p.Y <= ty)) p.Y += dy;
                }
            }
            int n = 2;
            while (pages.Any(p => p.Name == string.Format(Loc("LOC_KX_PageN", "Page {0}"), n))) n++;
            var page = new BoardPage { Id = Guid.NewGuid().ToString("N"), Name = string.Format(Loc("LOC_KX_PageN", "Page {0}"), n), X = tx, Y = ty };
            pages.Add(page);
            SaveSettings();
            PlaySound("select");
            boardPageId = page.Id;
            boardPage = 0;
            boardGames = new Dictionary<string, Guid[]>();
            BoardChanged(true);
            BoardRowsAgain(BuildBoardPagesRows, "page:" + page.Id);
        }

        private void BuildBoardPageRows()
        {
            var rows = BoardPageRows;
            rows.Clear();
            var p = BPages.FirstOrDefault(x => x.Id == boardEditPageId);
            if (p == null) return;
            string id = p.Id;
            Action again = () => BuildBoardPageRows();
            rows.Add(new PickRow
            {
                Key = "goto", Icon = "play", Text = Loc("LOC_KX_PageGoTo", "Show this page"), Sub = p.Id == boardPageId ? Loc("LOC_KX_PageShowing", "It's showing now") : Loc("LOC_KX_PageGoToSub", "Go to it on the home screen"),
                Command = new RelayCommand(_ =>
                {
                    if (boardEditing) BoardEndEdit(false);
                    boardPageId = id; boardPage = 0; boardGames = new Dictionary<string, Guid[]>(); boardFocusId = null;
                    PlaySound("select"); BoardChanged(true); BoardRowsAgain(again, "goto");
                })
            });
            rows.Add(new PickRow
            {
                Key = "rename", Icon = "note", Text = Loc("LOC_KX_Rename", "Rename"), Sub = p.Name,
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_PageNamePrompt", "A name for this page"), p.Name, typed =>
                {
                    if (string.IsNullOrWhiteSpace(typed)) return;
                    p.Name = typed.Trim();
                    SaveSettings();
                    NotifyBoard();
                    BoardRowsAgain(again, "rename");
                }, null, null, 30))
            });
            if (id != "home")
                rows.Add(new PickRow
                {
                    Key = "copyhome", Icon = "home", Text = Loc("LOC_KX_PageCopyHome", "Copy Home onto this page"),
                    Sub = BoardPageTileCount(id) == 0 ? Loc("LOC_KX_PageCopyHomeSubEmpty", "Home's tiles, to change from there")
                                                      : Loc("LOC_KX_PageCopyHomeSub", "Its tiles are replaced by Home's (Undo while editing the board puts them back)"),
                    Command = new RelayCommand(_ =>
                    {
                        int n = BoardCopyHomeTo(id);
                        PlaySound(n > 0 ? "select" : "back");
                        SaveSettings();
                        BoardChanged(true);
                        BoardRowsAgain(again, "copyhome");
                    })
                });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecMoveIt", "MOVE IT") });
            foreach (var d in new[] { new[] { "left", "-1", "0" }, new[] { "right", "1", "0" }, new[] { "up", "0", "-1" }, new[] { "down", "0", "1" } })
            {
                int dx = int.Parse(d[1]), dy = int.Parse(d[2]);
                string key = "move" + d[0];
                rows.Add(new PickRow
                {
                    Key = key, Icon = "move", Sub = Loc("LOC_KX_PageMoveSub", "It swaps places with a page that's there"),
                    Text = d[0] == "left" ? Loc("LOC_KX_PageMoveLeft", "Move left") : d[0] == "right" ? Loc("LOC_KX_PageMoveRight", "Move right") : d[0] == "up" ? Loc("LOC_KX_PageMoveUp", "Move up") : Loc("LOC_KX_PageMoveDown", "Move down"),
                    Command = new RelayCommand(_ =>
                    {
                        var other = BPages.FirstOrDefault(x => x.X == p.X + dx && x.Y == p.Y + dy);
                        if (other != null) { other.X = p.X; other.Y = p.Y; }
                        p.X += dx; p.Y += dy;
                        SaveSettings();
                        PlaySound("move");
                        NotifyBoard();
                        BoardRowsAgain(again, key);
                    })
                });
            }
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecDelete", "DELETE") });
            if (id == "home")
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "nodelete", Text = Loc("LOC_KX_PageHomeStays", "Home can't be deleted (it's where the board starts)") });
            else
                rows.Add(new PickRow
                {
                    Key = "delete", Icon = "trash", Text = Loc("LOC_KX_PageDelete", "Delete this page"),
                    Sub = string.Format(Loc("LOC_KX_PageDeleteSub", "Its {0} go too"), BoardPageTileCount(id) == 1 ? Loc("LOC_KX_OneTile", "1 tile") : string.Format(Loc("LOC_KX_NTiles", "{0} tiles"), BoardPageTileCount(id))),
                    Command = new RelayCommand(_ =>
                    {
                        BCards.RemoveAll(c => PageOf(c) == id);
                        BPages.Remove(p);
                        if (boardPageId == id) { boardPageId = "home"; boardPage = 0; boardGames = new Dictionary<string, Guid[]>(); }
                        PlaySound("back");
                        BoardChanged(true);
                        GoBackTo("boardpages", "addright");
                    })
                });
        }

        // ---- a tile's page ----

        private static string BoardArtName(int a)
        {
            switch (a)
            {
                case 1: return Loc("LOC_KX_BoardArtCover", "Cover");
                case 2: return Loc("LOC_KX_BoardArtWide", "Wide picture");
                case 3: return Loc("LOC_KX_BoardArtLogo", "Logo on its background");
                case 4: return Loc("LOC_KX_BoardArtBg", "Background");
            }
            return Loc("LOC_KX_BoardArtAuto", "Automatic (what suits the shape)");
        }

        private static string BoardNudgeName(int n)
        {
            switch (n)
            {
                case 1: return Loc("LOC_KX_NudgeUp", "Higher");
                case 2: return Loc("LOC_KX_NudgeDown", "Lower");
                case 3: return Loc("LOC_KX_NudgeLeft", "Further left");
                case 4: return Loc("LOC_KX_NudgeRight", "Further right");
            }
            return Loc("LOC_KX_NudgeCentre", "Centred");
        }

        private static string Squares(double v)
        {
            string n = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return Math.Abs(v - 1) < 0.001 ? Loc("LOC_KX_OneSquare", "1 square") : string.Format(Loc("LOC_KX_NSquares", "{0} squares"), n);
        }

        private string BoardShowingName(BoardCard c)
        {
            var t = BoardTiles.FirstOrDefault(b => ReferenceEquals(b.Card, c));
            if (t == null || t.GameId == Guid.Empty) return null;
            try { var g = api.Database.Games.Get(t.GameId); return g != null ? g.Name : null; } catch (Exception) { return null; }
        }

        // ---- a tile's width / height sliders (at the top of its page; Left / Right) ----
        private SliderModel boardWSlider, boardHSlider;
        public SliderModel BoardWSlider { get { return boardWSlider ?? (boardWSlider = new SliderModel()); } }
        public SliderModel BoardHSlider { get { return boardHSlider ?? (boardHSlider = new SliderModel()); } }
        public string BoardSizeHint { get { return IsFreeMode ? Loc("LOC_KX_SizeHintFree", "Left / Right: smaller / bigger (it can overlap other tiles)") : Loc("LOC_KX_SizeHintGrid2", "Left / Right: smaller / bigger"); } }
        /// <summary>Grid: the tile now covers others (nothing is removed until you say so).</summary>
        public string BoardSizeWarn
        {
            get
            {
                var c = boardEditCard;
                if (c == null || IsFreeMode || !BCards.Contains(c)) return "";
                int n = UnderOf(c).Count;
                if (n == 0) return "";
                string what = n == 1 ? Loc("LOC_KX_OneTile", "1 tile") : string.Format(Loc("LOC_KX_NTiles", "{0} tiles"), n);
                return string.Format(Loc("LOC_KX_SizeCovers", "It now covers {0}: when you leave this page, move it, or press A to let it take their place (B puts its old size back)"), what).ToUpperInvariant();
            }
        }
        public bool HasBoardSizeWarn { get { return BoardSizeWarn.Length > 0; } }

        private void BoardUpdateSliders()
        {
            var c = boardEditCard;
            if (c == null) return;
            double unitW = IsFreeMode ? BoardAreaW / FreeCols : 0, unitH = IsFreeMode ? BoardAreaH / FreeRows : 0;
            BoardWSlider.Text = IsFreeMode ? Math.Round(c.W * unitW) + " px" : Squares(c.W);
            BoardHSlider.Text = IsFreeMode ? Math.Round(c.H * unitH) + " px" : Squares(c.H);
            BoardWSlider.SetFill(c.W / BCols);
            BoardHSlider.SetFill(c.H / BRows);
            Notify("BoardSizeHint");
            Notify("BoardSizeWarn");
            Notify("HasBoardSizeWarn");
        }

        /// <summary>Left / Right on a tile's width or height slider. Grid: a tile it grows over is removed (free placement: they overlap).</summary>
        private bool BoardNudgeSize(bool width, int dir)
        {
            var c = boardEditCard;
            if (c == null || !BCards.Contains(c)) return false;
            double step = IsFreeMode ? 1 : BSnap ? 1 : 0.25, min = IsFreeMode ? 3 : step;
            double max = width ? BMaxX - BoardCells(c).X : BMaxY - BoardCells(c).Y;
            double v = (width ? c.W : c.H) + dir * step;
            v = Math.Max(min, Math.Min(max, v));
            if (Math.Abs(v - (width ? c.W : c.H)) < 0.001) return false;
            if (!ReferenceEquals(boardSizeCard, c) || boardSizeBefore == null) { boardSizeCard = c; boardSizeBefore = new[] { c.X, c.Y, c.W, c.H }; }
            if (width) c.W = v; else c.H = v;
            // nothing it grows over is removed here: leaving the page asks (move it, or A to let it take their place)
            BoardChanged(false);
            BoardUpdateSliders();
            return true;
        }

        /// <summary>The tile sized on its page now covers other tiles (grid placement).</summary>
        private bool BoardSizeClash()
        {
            var c = boardSizeCard;
            return c != null && boardSizeBefore != null && BCards.Contains(c) && !IsFreeMode && UnderOf(c).Count > 0;
        }

        /// <summary>The panel has closed: the tile sits picked up on the board, covering those tiles. Move it / size it,
        /// A: it takes their place (they're removed), B: back to the size it was.</summary>
        private void BoardResolveSizeClash()
        {
            var c = boardSizeCard;
            var before = boardSizeBefore;
            boardSizeCard = null;
            boardSizeBefore = null;
            boardCardFromBoard = false;
            boardChangeKind = null;
            if (c == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                if (!BCards.Contains(c) || !IsBoardMode) return;
                if (!boardEditing)
                {
                    boardEditing = true;
                    boardPasting = false;
                    boardUnder = 0;
                    boardLiveStep.Clear();
                }
                BoardSyncTiles();
                BoardFillPage();
                NotifyBoard();
                var t = BoardTiles.FirstOrDefault(b => ReferenceEquals(b.Card, c));
                if (t == null) { FocusBoard(); return; }
                BoardPick(t);
                if (before != null) boardPickStart = before;   // B: its old size, nothing removed
            }), DispatcherPriority.ApplicationIdle);
        }

        private void BuildBoardCardRows()
        {
            var rows = BoardCardRows;
            rows.Clear();
            BoardUpdateSliders();
            var c = boardEditCard;
            if (c == null || !BCards.Contains(c)) { rows.Add(new PickRow { IsInfo = true, Text = Loc("LOC_KX_BoardNoCard2", "That tile isn't on the board any more") }); return; }
            Action again = null;
            again = () => BuildBoardCardRows();
            bool game = IsGameKind(c), piece = IsPieceKind(c);

            // what it is
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BoardShows", "WHAT IT SHOWS") });
            rows.Add(new PickRow
            {
                Key = "kind", Icon = "tag", Text = Loc("LOC_KX_TileType", "Tile type"), Sub = KindName(c),
                Command = new RelayCommand(_ => { boardChangeKind = c; PlaySound("select"); SetOptionsPage("boardadd"); })
            });
            if (IsCarouselCard(c))
            {
                // (0.91) its games come from its group's carousel: its own game settings are hidden
                var cg = GroupOf(c);
                rows.Add(new PickRow { IsInfo = true, Icon = "refresh", Key = "carouselinfo",
                                       Text = string.Format(Loc("LOC_KX_GroupTileInfo", "Its games come from {0}'s carousel: {1}"), GroupName(cg), BoardSourceName(cg.Source ?? "all")) });
            }
            else if (c.Kind == "pinned")
            {
                string name = null;
                try { var g = api.Database.Games.Get(BoardPlan.GameOf(c)); name = g != null ? g.Name : null; } catch (Exception) { }
                rows.Add(new PickRow
                {
                    Key = "choose", Icon = "search", Text = Loc("LOC_KX_BoardGame", "Game"), Sub = name ?? Loc("LOC_KX_BoardChooseNone", "None yet: A to choose"),
                    Command = new RelayCommand(_ => { PlaySound("select"); BoardPickGame(c); })
                });
                rows.Add(new PickRow
                {
                    Key = "unpin", Icon = "shelf", Text = Loc("LOC_KX_BoardUnpin", "Show games from a list instead"),
                    Sub = string.Format(Loc("LOC_KX_BoardUnpinSub2", "Games from {0}"), BoardSourceName(c.Source)),
                    Command = new RelayCommand(_ => { c.Kind = "filter"; PlaySound("move"); BoardChanged(true); BoardRowsAgain(again, "source"); })
                });
                rows.Add(BoardSwitch("pin", "everypage", Loc("LOC_KX_BoardEveryPage", "On every page"), c.EveryPage,
                                     Loc("LOC_KX_BoardEveryPageOn", "It stays put as you go through the pages"),
                                     IsManualMode ? Loc("LOC_KX_BoardEveryPageOff", "Only on the first page") : Loc("LOC_KX_BoardEveryPageOffNext", "Only on the first page; other games take its place on the next pages"),
                                     () => { c.EveryPage = !c.EveryPage; PlaySound("move"); BoardChanged(true); BoardRowsAgain(again, "everypage"); }));
            }
            else if (c.Kind == "filter")
            {
                rows.Add(new PickRow
                {
                    Key = "source", Icon = "shelf", Text = Loc("LOC_KX_BoardSource", "Games from"), Sub = BoardSourceName(c.Source),
                    Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("boardsource"); })
                });
                rows.Add(new PickRow
                {
                    Key = "choose", Icon = "search", Text = Loc("LOC_KX_BoardChoose", "Choose the game…"), Sub = Loc("LOC_KX_BoardChooseSub", "Pick one from your library: it's then always this game"),
                    Command = new RelayCommand(_ => { PlaySound("select"); BoardPickGame(c); })
                });
                string showing = BoardShowingName(c);
                rows.Add(new PickRow
                {
                    Key = "pin", Icon = "pin", Text = Loc("LOC_KX_BoardPin", "Pin the game it's showing"),
                    Sub = showing != null ? string.Format(Loc("LOC_KX_BoardPinSub", "Always show {0} here"), showing) : Loc("LOC_KX_BoardPinNone", "It isn't showing a game"),
                    IsInfo = showing == null,
                    Command = new RelayCommand(_ =>
                    {
                        var t = BoardTiles.FirstOrDefault(b => ReferenceEquals(b.Card, c));
                        if (t == null || t.GameId == Guid.Empty) { PlaySound("back"); return; }
                        c.GameId = t.GameId.ToString();
                        c.Kind = "pinned";
                        PlaySound("select");
                        BoardChanged(true);
                        BoardRowsAgain(again, "choose");
                    })
                });
                rows.Add(BoardSwitch("clock", "live", Loc("LOC_KX_BoardLive2", "Live tile"), c.Live,
                                     string.Format(Loc("LOC_KX_BoardLiveOn", "Shows another of its games every {0} seconds"), settings.BoardLiveSeconds), Loc("LOC_KX_BoardLiveOff", "Off"),
                                     () => { c.Live = !c.Live; boardLiveStep.Remove(c.Id); PlaySound("move"); BoardChanged(false); BoardRowsAgain(again, "live"); }));
            }
            else if (c.Kind == "info")
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_InfoLines", "LINES IT SHOWS") });
                if (c.Info == null || c.Info.Count == 0) c.Info = new List<string> { "name", "playtime", "lastplayed", "ach" };
                foreach (string k in BoardPieceKeys)
                {
                    string key = k;
                    rows.Add(BoardSwitch(InfoIcon(k), "info:" + k, BoardInfoLabel(k), c.Info.Contains(k), Loc("LOC_KX_On", "On"), Loc("LOC_KX_Off", "Off"), () =>
                    {
                        if (c.Info.Contains(key)) c.Info.Remove(key); else c.Info.Add(key);
                        PlaySound("move");
                        BoardChanged(false);
                        BoardRowsAgain(again, "info:" + key);
                    }));
                }
            }
            else if (c.Kind == "widget")
            {
                rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "widgetinfo", Text = Loc("LOC_KX_TileWidgetInfo2", "Its look and what it shows: Settings & Customisation › Home settings › Widgets") });
                var tw = Widgets != null ? Widgets.FirstOrDefault(x => x.Config != null && x.Config.Id == c.Target) : null;
                if (tw != null)
                    rows.Add(new PickRow
                    {
                        Key = "editwidget", Icon = "options", Text = Loc("LOC_KX_TileEditWidget", "Edit this widget"), Sub = tw.Name,
                        Command = new RelayCommand(_ => { PlaySound("select"); editingWidget = tw; SetOptionsPage("widgetedit"); NotifyWidgetEdit(); })
                    });
            }
            else if (c.Kind == "library")
            {
                rows.Add(new PickRow
                {
                    Key = "libname", Icon = "note", Text = Loc("LOC_KX_LibraryNameRow", "Name"), Sub = LibraryName(c),
                    Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_LibraryNamePrompt", "A name for this library"), c.Name ?? "", typed =>
                    {
                        c.Name = string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
                        BoardChanged(false);
                        BoardRowsAgain(again, "libname");
                    }, null, null, 40))
                });
                if (c.Sources == null) c.Sources = new List<string>();
                var held = c.Sources.Select(BoardSourceName).ToList();
                rows.Add(new PickRow
                {
                    Key = "libholds", Icon = "shelf", Text = Loc("LOC_KX_LibraryHoldsRow", "The lists it holds"),
                    Sub = held.Count == 0 ? Loc("LOC_KX_LibraryHoldsNone", "None yet: A to choose")
                        : (held.Count == 1 ? Loc("LOC_KX_OneList", "1 list") : string.Format(Loc("LOC_KX_NLists", "{0} lists"), held.Count)) + ": " + string.Join(", ", held.Take(4).ToArray()) + (held.Count > 4 ? "…" : ""),
                    Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("boardlibrary"); })
                });
            }

            // shape and size
            if (!piece && c.Kind != "shortcut" && c.Kind != "library") rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BoardShapeHeader", "SHAPE") });
            if (!piece && c.Kind != "shortcut" && c.Kind != "library")
                rows.Add(new PickRow
                {
                    Key = "shape", Icon = "layout", Text = Loc("LOC_KX_CardShape", "Card shape"), Sub = ShapeName(c.Shape),
                    Command = new RelayCommand(_ => { BoardCycleShape(c); BoardFitFree(c); PlaySound("move"); BoardChanged(false); BoardRowsAgain(again, "shape"); })
                });
            AddCropRows(rows, c, again);   // Manual board: crop, copy / paste the crop

            // picture (games and Store games)
            if (game || c.Kind == "store")
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_BoardPicture", "PICTURE") });
                if (game)
                    rows.Add(new PickRow
                    {
                        Key = "art", Icon = "gallery", Text = Loc("LOC_KX_BoardArt", "Picture"), Sub = BoardArtName(c.Art),
                        Command = new RelayCommand(_ => { c.Art = (c.Art + 1) % 5; PlaySound("move"); BoardChanged(false); BoardRowsAgain(again, "art"); })
                    });
                rows.Add(new PickRow
                {
                    Key = "nudge", Icon = "move", Text = Loc("LOC_KX_BoardNudge", "Picture position"), Sub = BoardNudgeName(c.Nudge),
                    Command = new RelayCommand(_ => { c.Nudge = (c.Nudge + 1) % 5; PlaySound("move"); BoardChanged(false); BoardRowsAgain(again, "nudge"); })
                });
            }

            // info on a game tile: only what fits its shape and size
            int room = game ? BoardInfoRoom(c) : 0;
            if (room > 0)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecCardInfo", "CARD INFO") });
                rows.Add(new PickRow
                {
                    IsInfo = true, Key = "room", Icon = "info",
                    Text = room == 1 ? Loc("LOC_KX_BoardRoomOne", "Room for one line (the first one that's on)") : string.Format(Loc("LOC_KX_BoardRoomN", "Room for {0} lines"), room)
                });
                if (c.Info == null) c.Info = new List<string>();
                foreach (string k in BoardInfoKeys)
                {
                    string key = k;
                    rows.Add(BoardSwitch(InfoIcon(k), "info:" + k, BoardInfoLabel(k), c.Info.Contains(k), Loc("LOC_KX_On", "On"), Loc("LOC_KX_Off", "Off"), () =>
                    {
                        if (c.Info.Contains(key)) c.Info.Remove(key); else c.Info.Add(key);
                        PlaySound("move");
                        BoardChanged(false);
                        BoardRowsAgain(again, "info:" + key);
                    }));
                }
            }

            // look: background and text (a widget: its own look)
            if (c.Kind == "widget")
            {
                var w = Widgets != null ? Widgets.FirstOrDefault(x => x.Config != null && x.Config.Id == c.Target) : null;
                if (w != null)
                {
                    var wc = w.Config;
                    rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_TileLook", "LOOK") });
                    rows.Add(new PickRow
                    {
                        Key = "wback", Icon = "palette", Text = Loc("LOC_KX_TileBack", "Background"),
                        Sub = wc.Back == 1 ? Loc("LOC_KX_BackSolid", "Solid") : wc.Back == 2 ? Loc("LOC_KX_BackNone", "None") : Loc("LOC_KX_BackSeeThrough", "See-through"),
                        Command = new RelayCommand(_ => { wc.Back = (wc.Back + 1) % 3; ApplyWidgetLook(w); SaveSettings(); PlaySound("move"); BoardRowsAgain(again, "wback"); })
                    });
                    rows.Add(BoardSwitch("tag", "wtitle", Loc("LOC_KX_TileShowTitle", "Show its title"), !wc.HideTitle, Loc("LOC_KX_On", "On"), Loc("LOC_KX_Off", "Off"),
                                         () => { wc.HideTitle = !wc.HideTitle; ApplyWidgetLook(w); SaveSettings(); PlaySound("move"); BoardRowsAgain(again, "wtitle"); }));
                }
            }
            else if (c.Kind != "empty")
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_TileLook", "LOOK") });
                rows.Add(new PickRow
                {
                    Key = "back", Icon = "palette", Text = Loc("LOC_KX_TileBack", "Background"),
                    Sub = c.Back == 1 ? Loc("LOC_KX_BackCard", "A card") : c.Back == 2 ? Loc("LOC_KX_BackSeeThrough", "See-through") : c.Back == 3 ? Loc("LOC_KX_BackNone", "None")
                        : (IsPieceKind(c) ? Loc("LOC_KX_BackAutoNone", "Automatic (none)") : Loc("LOC_KX_BackAutoCard", "Automatic (a card)")),
                    Command = new RelayCommand(_ => { c.Back = (c.Back + 1) % 4; PlaySound("move"); BoardChanged(false); BoardRowsAgain(again, "back"); })
                });
                rows.Add(BoardSwitch("tag", "text", c.Kind == "info" || c.Kind == "hltb" ? Loc("LOC_KX_TileShowHeading", "Show its heading") : Loc("LOC_KX_TileShowText", "Show its name"), !c.HideText,
                                     Loc("LOC_KX_On", "On"), Loc("LOC_KX_TileTextOff", "Off: just the picture / icon"),
                                     () => { c.HideText = !c.HideText; PlaySound("move"); BoardChanged(false); BoardRowsAgain(again, "text"); }));
            }

            // copy, presets, remove
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecCopy", "COPY") });
            rows.Add(new PickRow
            {
                Key = "copy", Icon = "file", Text = Loc("LOC_KX_BoardCopy2", "Copy this tile's settings"),
                Sub = IsManualMode
                      ? (boardClip != null && boardClip.Id == c.Id ? Loc("LOC_KX_BoardCopiedCrop", "Copied ✓  Shape, size, info, picture, look (and the crop, for tiles of the same shape)") : Loc("LOC_KX_BoardCopySubCrop", "Shape, size, info, picture and look; the crop too on tiles of the same shape (not where it is)"))
                      : (boardClip != null && boardClip.Id == c.Id ? Loc("LOC_KX_BoardCopied", "Copied ✓  Shape, size, info, picture and look") : Loc("LOC_KX_BoardCopySub", "Shape, size, info, picture and look (not where it is)")),
                Command = new RelayCommand(_ => { boardClip = c.Snapshot(); PlaySound("select"); BoardRowsAgain(again, "paste"); })
            });
            rows.Add(BoardSwitch("filter", "copyfilter", Loc("LOC_KX_BoardCopyFilter", "Copy what it shows too"), boardClipFilter,
                                 Loc("LOC_KX_BoardCopyFilterOn2", "Its type and games (or chosen game) go with the settings"), Loc("LOC_KX_BoardCopyFilterOff2", "Off: each tile keeps showing what it shows"),
                                 () => { boardClipFilter = !boardClipFilter; PlaySound("move"); BoardRowsAgain(again, "copyfilter"); }));
            if (boardClip != null)
            {
                rows.Add(new PickRow
                {
                    Key = "paste", Icon = "download", Text = Loc("LOC_KX_BoardPasteTo", "Paste to…"), Sub = Loc("LOC_KX_BoardPasteToSub2", "Pick the tiles on the board to paste to"),
                    Command = new RelayCommand(_ => BoardStartPaste())
                });
                if (boardClip.Id != c.Id)
                    rows.Add(new PickRow
                    {
                        Key = "pastehere", Icon = "use", Text = Loc("LOC_KX_BoardPasteHere", "Paste the copied settings here"), Sub = Loc("LOC_KX_BoardPasteHereSub", "This tile takes the copied settings"),
                        Command = new RelayCommand(_ => { EditBegin(); BoardApplyLook(boardClip, c, boardClipFilter); PlaySound("select"); BoardChanged(true); EditCheckpointAfterPick(); BoardRowsAgain(again, "pastehere"); })   // (an undo step)
                    });
            }
            int same = BCards.Count(o => o.Shape == c.Shape && o.Kind == c.Kind && !ReferenceEquals(o, c));
            if (same > 0 && !piece)
                rows.Add(new PickRow
                {
                    Key = "applyshape", Icon = "layout", Text = string.Format(Loc("LOC_KX_BoardApplyShape", "Apply to all cards of this shape ({0})"), ShapeName(c.Shape)),
                    Sub = string.Format(Loc("LOC_KX_BoardApplyShapeSub2", "{0} more tiles get this tile's settings (not where they are)"), same),
                    Command = new RelayCommand(_ =>
                    {
                        EditBegin();   // (an undo step)
                        foreach (var o in BCards.Where(o => o.Shape == c.Shape && o.Kind == c.Kind && !ReferenceEquals(o, c)).ToList()) BoardApplyLook(c, o, boardClipFilter);
                        PlaySound("select");
                        BoardChanged(true);
                        EditCheckpointAfterPick();
                        BoardRowsAgain(again, "applyshape");
                    })
                });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecPresets", "PRESETS") });
            rows.Add(new PickRow
            {
                Key = "savepreset", Icon = "star", Text = Loc("LOC_KX_SavePreset", "Save as a tile preset"),
                Sub = Loc("LOC_KX_SavePresetSub", "Then add tiles like this from the list of tiles"),
                Command = new RelayCommand(_ => { BoardSavePreset(c); PlaySound("select"); BoardRowsAgain(again, "savepreset"); })
            });
            if (settings.BoardPresets != null && settings.BoardPresets.Count > 0)
                rows.Add(new PickRow
                {
                    Key = "usepreset", Icon = "use", Text = Loc("LOC_KX_UsePreset", "Use a tile preset…"), Sub = string.Format(Loc("LOC_KX_UsePresetSub", "{0} saved"), settings.BoardPresets.Count),
                    Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("boardpresets"); })
                });

            AddGroupRows(rows, c, again);   // (0.91) Manual board: groups

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecRemove", "REMOVE") });
            rows.Add(new PickRow
            {
                Key = "remove", Icon = "trash", Text = Loc("LOC_KX_BoardRemove2", "Remove this tile"), Sub = Loc("LOC_KX_BoardRemoveSub", "Takes it off the board"),
                Command = new RelayCommand(_ =>
                {
                    BCards.Remove(c);
                    boardEditCard = null;
                    PlaySound("back");
                    boardCardFromBoard = false;
                    ClosePanels();
                    BoardChanged(true);
                    ui.BeginInvoke(new Action(() => FocusBoard()), DispatcherPriority.ApplicationIdle);
                })
            });
        }

        /// <summary>A tile's settings saved under a name (its type, shape, size, picture, info, look; with "copy what it shows" its games too).</summary>
        private void BoardSavePreset(BoardCard c)
        {
            if (settings.BoardPresets == null) settings.BoardPresets = new List<BoardCard>();
            var p = c.Copy();
            p.Sources = new List<string>(c.Sources ?? new List<string>());   // (Copy shares the list: the preset gets its own)
            if (!boardClipFilter && IsGameKind(p)) { p.Kind = "filter"; p.GameId = null; }   // the look, not the game
            p.KeepsWhat = boardClipFilter || !IsGameKind(c);
            string baseName = KindName(p) + (IsPieceKind(p) ? "" : " · " + ShapeName(p.Shape)) + " " + string.Format("{0}×{1}", p.W.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), p.H.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            string name = baseName;
            for (int i = 2; settings.BoardPresets.Any(x => x.Name == name); i++) name = baseName + " (" + i + ")";
            p.Name = name;
            settings.BoardPresets.Add(p);
            SaveSettings();
        }

        private void BuildBoardPresetRows()
        {
            var rows = BoardPresetRows;
            rows.Clear();
            var c = boardEditCard;
            var presets = settings.BoardPresets ?? new List<BoardCard>();
            if (c != null)
            {
                rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecUsePreset", "USE A PRESET") });
                foreach (var p in presets)
                {
                    var preset = p;
                    rows.Add(new PickRow
                    {
                        Key = "use:" + p.Id, Icon = "use", Text = p.Name ?? "?", Sub = PresetSub(p),
                        Command = new RelayCommand(_ =>
                        {
                            BoardApplyLook(preset, c, preset.KeepsWhat);
                            PlaySound("select");
                            BoardChanged(true);
                            GoBackTo("boardcard", "usepreset");
                        })
                    });
                }
            }
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_DeletePresetHeader", "DELETE A PRESET") });
            foreach (var p in presets)
            {
                var preset = p;
                rows.Add(new PickRow
                {
                    Key = "del:" + p.Id, Icon = "trash", Text = p.Name ?? "?", Sub = PresetSub(p),
                    Command = new RelayCommand(_ =>
                    {
                        settings.BoardPresets.Remove(preset);
                        SaveSettings();
                        PlaySound("back");
                        if (settings.BoardPresets.Count == 0) { GoBackTo("boardcard", "savepreset"); return; }
                        BoardRowsAgain(BuildBoardPresetRows, null);
                    })
                });
            }
        }

        /// <summary>The next shape; the tile's size follows it (tall twice as high as wide, wide twice as wide, the rest square).</summary>
        private void BoardCycleShape(BoardCard c)
        {
            c.Shape = (c.Shape + 1) % CardShapes.Count;
            double m = Math.Min(c.W, c.H);
            if (c.Shape == CardShapes.Tall) { c.W = m; c.H = m * 2; }
            else if (c.Shape == CardShapes.Wide) { c.W = m * 2; c.H = m; }
            else { c.W = m; c.H = m; }
            BoardClamp(c);
        }

        private void BoardCycleSize(BoardCard c, bool width)
        {
            double step = IsFreeMode ? 4 : BSnap ? 1 : 0.5;
            double max = width ? BMaxX - c.X : BMaxY - c.Y;
            double v = width ? c.W : c.H;
            v = v + step > max + 0.001 ? step : v + step;
            if (width) c.W = v; else c.H = v;
            BoardClamp(c);
        }

        private void BoardClamp(BoardCard c)
        {
            double cols = BMaxX, rows = BMaxY;
            c.W = Math.Max(0.25, Math.Min(c.W, cols - BMinX));
            c.H = Math.Max(0.25, Math.Min(c.H, rows - BMinY));
            if (c.X + c.W > cols) c.X = Math.Max(BMinX, cols - c.W);
            if (c.Y + c.H > rows) c.Y = Math.Max(BMinY, rows - c.H);
        }

        /// <summary>Settings from one tile to another: shape, size, info, picture and look; with "what it shows" its type and games too. Never where it is.</summary>
        private void BoardApplyLook(BoardCard from, BoardCard to, bool withFilter)
        {
            if (from == null || to == null) return;
            bool sameShape = EffShape(from) == EffShape(to);   // (before the paste: the crop only goes to a tile of the same shape)
            to.Shape = from.Shape;
            to.W = from.W;
            to.H = from.H;
            if (sameShape && IsManualMode) to.CropBox = from.CropBox != null ? (double[])from.CropBox.Clone() : null;   // the crop too (W / H are the part that's left)
            to.Info = new List<string>(from.Info ?? new List<string>());
            to.Art = from.Art;
            to.Nudge = from.Nudge;
            to.Back = from.Back;
            to.HideText = from.HideText;
            if (withFilter)
            {
                to.Kind = from.Kind;
                to.Source = from.Source;
                to.GameId = from.GameId;
                to.Target = from.Target;
                to.EveryPage = from.EveryPage;
                to.Live = from.Live;
                to.Sources = new List<string>(from.Sources ?? new List<string>());   // a Library tile's lists (its own copy)
            }
            BoardOneWidget(to);
            BoardClamp(to);
            BoardFitFree(to);
        }

        /// <summary>A widget shows in one tile only: a second tile for the same widget becomes an empty one.</summary>
        private void BoardOneWidget(BoardCard c)
        {
            if (c == null || c.Kind != "widget") return;
            if (BCards.Any(o => !ReferenceEquals(o, c) && o.Kind == "widget" && o.Target == c.Target)) { c.Kind = "empty"; c.Target = null; }
        }

        /// <summary>A tile made bigger (or a new shape) shrinks back until it doesn't cover another tile.</summary>
        private void BoardFitFree(BoardCard c)
        {
            if (c == null || !BCards.Contains(c)) return;
            double step = Math.Max(0.25, BoardStepNow);   // (never the 1 px step: 64 of those wouldn't get far)
            for (int i = 0; i < 64 && UnderOf(c).Count > 0 && (c.W > step || c.H > step); i++)
            {
                if (c.W >= c.H && c.W > step) c.W = Math.Max(step, c.W - step); else c.H = Math.Max(step, c.H - step);
            }
        }

        private void BoardStartPaste()
        {
            if (boardClip == null) return;
            PlaySound("select");
            boardCardFromBoard = false;
            FolderPreviewKeep();   // (Folder: pasting goes on in the folder showing behind the settings)
            ClosePanels();
            ui.BeginInvoke(new Action(() =>
            {
                if (!boardEditing) { boardEditing = true; BoardSyncTiles(); BoardFillPage(); }
                boardPasting = true;
                NotifyBoard();
                FocusBoard();
            }), DispatcherPriority.ApplicationIdle);
        }

        private void BoardPasteTo(BoardTile t)
        {
            if (t.IsSlot || boardClip == null) { PlaySound("back"); return; }
            BoardApplyLook(boardClip, t.Card, boardClipFilter);
            PlaySound("select");
            BoardChanged(boardClipFilter);
            FocusBoardTile(t);
        }

        // ---- a tile's source ----

        private void BuildBoardSourceRows()
        {
            var rows = BoardSourceRows;
            rows.Clear();
            var c = boardEditCard;
            var grp = boardSourceGroup;
            if (c == null && grp == null) return;
            string now = grp != null ? (grp.Source ?? "all") : (c.Source ?? "all");
            var keys = new List<string> { "all", "random", "played", "favourites", "recent", "installed", "never" };
            try { foreach (var p in api.Database.FilterPresets.OrderBy(p => p.Name)) keys.Add("filter:" + p.Id); } catch (Exception) { }
            foreach (var s in settings.Collections) keys.Add("shelf:" + s.Id);
            bool fHeader = false, sHeader = false;
            foreach (string k in keys)
            {
                string name = BoardSourceName(k);
                if (rows.Count == 0) rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LibGroupGames", "YOUR GAMES") });
                if (k.StartsWith("filter:", StringComparison.Ordinal) && !fHeader) { rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LibGroupFilters", "SAVED FILTERS") }); fHeader = true; }
                if (k.StartsWith("shelf:", StringComparison.Ordinal) && !sHeader) { rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LibGroupCollections", "COLLECTIONS") }); sHeader = true; }
                string key = k;
                rows.Add(new PickRow
                {
                    Key = k, Icon = k.StartsWith("filter:", StringComparison.Ordinal) ? "filter" : "shelf", Text = name, IsSelected = now == k,
                    Command = new RelayCommand(_ =>
                    {
                        if (grp != null)
                        {
                            // (0.91) a carousel group's list
                            grp.Source = key;
                            boardSourceGroup = null;
                            groupOffset.Remove(grp.Id);
                            PlaySound("select");
                            BoardChanged(true);
                            GoBackTo("boardgroup", "gsource");
                            BuildBoardGroupRows("gsource");
                            return;
                        }
                        c.Source = key;
                        PlaySound("select");
                        boardLiveStep.Remove(c.Id);
                        BoardChanged(true);
                        GoBackTo("boardcard", "source");
                    })
                });
            }
        }
    }
}
