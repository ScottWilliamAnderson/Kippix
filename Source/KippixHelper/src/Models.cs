using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;

namespace KippixHelper
{
    /// <summary>Saved per user in the add-on's data folder (settings.json).</summary>
    public class HelperSettings
    {
        /// <summary>(0.99.62) Kippix as it comes: Kipping0's home screen over the defaults below (KippixDefaults).</summary>
        public HelperSettings() { KippixDefaults.Apply(this); }

        // Top bar shortcuts
        public bool PinSearch = true;
        public bool PinAchievements = true;
        public bool PinDiscord = true;
        public bool PinAudio = true;
        public bool PinHibernate = true;
        public bool PinDownloads = true;
        public bool PinStore = true;
        public bool PinBattery = true;
        public bool PinSleep = false;
        public bool PinRestart = false;
        public bool PinShutdown = false;
        public List<string> ShortcutOrder;
        public bool ShortcutsVertical = false;     // the shortcut bar stands down the right side instead of across the top
        public int ProfileShow = 0;                // the profile button: 0 name and picture, 1 name only, 2 picture only
        public bool SettingsInProfileMenu = false; // Settings & Customisation at the bottom of the profile menu
        public bool HideTopOptions = false;        // hide the ⋯ (only allowed with the profile menu entry on)
        public string ProfileNameOverride = "";    // your own name (empty: Windows' name)
        public string ProfilePicturePath = "";     // your own picture (a copy in the add-on's folder; empty: Windows' picture)

        // Home settings. Positions: 0 left, 1 centre, 2 right.
        public int SettingsVersion = 0;
        public bool ShowButtonHints = true;
        public int HintsPosition = 0;
        public bool ShowLogo = true;
        public int LogoAnchor = 0;                 // (0.87) a moved logo lines up by its 0 left edge / 1 centre / 2 right edge
        public int LogoPosition = 0;
        public bool ShowGameInfo = true;
        public int InfoPosition = 0;
        public int GameInfoPosition = 0;      // v0.2 only (0 left, 1 right, 2 hidden); migrated on load
        public bool ShowNotificationIcon = true;
        public bool Clock24h = true;
        public bool ShowBatteryPercent = true;
        // Sizes: 0 small, 1 medium, 2 large, 3 extra large
        public int HintsSize = 1;
        public int LogoSize = 1;
        public int InfoSize = 1;
        // Game cards: pixels up (+) or down (-) from the default spot
        public int CardOffset = 0;
        // Last selected game's background, shown straight away while Playnite loads the library
        public string LastBackground;

        // Achievements screen
        public bool AchSolidBackground = false;    // before 0.70 (became AchBgMode 2); kept in step with AchBgMode for older layout files
        public int AchBgMode = -1;                 // behind the screen: 0 the highlighted game's background (the default), 1 see-through, 2 solid (-1: not set, from AchSolidBackground)
        public int AchTallyMode = 1;   // next to the rarity tally: 0 score, 1 level (the default), 2 hidden
        public bool AchDefaults070 = false;        // 0.70: centred, game background and level applied once (asked for)
        public int AchSort = 0;                    // 0 recently played, 1 name, 2 most complete, 3 least complete
        public int AchFilter = 0;                  // 0 all, 1 in progress, 2 completed, 3 not started
        public int AchItemFilter = 0;              // one game's list: 0 all, 1 unlocked, 2 locked
        public bool AchListLayout = false;         // before 0.15 (became AchLayout 1)
        public int AchLayout = 2;                  // achievements: 0 grid, 1 list, 2 centred (the default)
        public int AchRedesign = 0;                // (0.89) achievements, all-games page: 0 Classic (AchLayout), 1 Showcase, 2 Trophy rings, 3 Timeline, 4 Compact
        public int AchItemRedesign = -1;           // (0.90) a game's achievements page (and one achievement's page): the same choices; -1 = as the all-games page
        public int AchItemLayout = -1;             // (0.90) a game's page in Classic: 0 grid, 1 list, 2 centred; -1 = as AchLayout
        public bool MarqueeText = true;            // (0.90) words that don't fit on a row scroll slowly while it has the highlight
        // (0.91) Presets: for each layout (row0 / row1 / row2 / l1 / l2 / l3 / l4) and the game page ("game"), which one is on and
        // your own (Default) kept to come back to. PresetBase: your own home screen settings while a preset layout is showing.
        public Dictionary<string, PresetSlot> Presets = new Dictionary<string, PresetSlot>();
        public string PresetBase;
        public string PresetOnScreen = "";       // (0.91) whose preset settings are on screen now ("" your own)
        public PresetSlot GamePreset;              // (0.91) the game page's presets (kept with the game page's settings)

        // Top bar
        public bool AutoHideTopBar = false;
        public bool TopBarKeepClock = true;
        public bool OptionsInBar035 = false;         // 0.35: the ⋯ is a top bar item too (the clock then sits after it, by the notifications)
        public bool PinClock = true;                 // the clock: one of the top bar's items since 0.34 (moved like the shortcuts)
        public List<string> TopBarStayVisible;     // shortcut keys that stay visible while the bar is hidden

        // Game page (details)
        public bool DetShowLogo = true;
        public bool DetShowInfo = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)            // developer / publisher / release / genre / platform / source
        public bool DetShowDescription = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public int DetDescPosition = 0;            // 0 left, 1 centre, 2 right
        public int DetLogoPosition = 0;
        public int DetOffset = 0;                  // pixels down (+) or up (-) from the default spot (320 px below the top)
        public bool DetShowAchButton = true;
        public bool DetShowDate = false;
        public int DetArt = 0;                     // 0 cover, 1 trailer (falls back to the cover), 2 nothing
        public int DetMediaSize = 1;               // 0 small .. 3 extra large
        public int DetMediaOffset = 0;             // pixels up (+) or down (-)
        public bool DetTrailerAutoplay = true;
        public bool DetTrailerSound = true;
        public bool DetTrailerLoop = true;
        public bool DetTrailerStream = true;       // stream the Steam store trailer when none is downloaded

        // Home: the active filter preset's name
        public bool ShowFilterLabel = true;
        public int FilterPosition = 0;             // no longer used (kept so older settings files read the same): the filter name is placed with Free move

        // General
        public bool ShowTooltips = true;

        // Audio
        public bool SoundEffects = true;
        public string SoundPack = "Classic";
        public int SoundVolume = 50;               // percent (0..100)
        public int MusicMode = 0;                  // 0 off, 1 Playnite's music, 2 my music folder, 3 UniPlaySong (0.98)
        public bool UpsMet = false;                // (0.98) UniPlaySong has been found once (it became the music then)
        public bool UpsToast = false;              // (0.98) Audio › UniPlaySong › Show the song when it changes
        public bool UseBcBackgrounds = false;      // (0.98) Background › BackgroundChanger › Its backgrounds (when it's installed; 0.99.1: off to start with)
        public bool UseBcCovers = false;           // (0.98) ... › Its covers on the game page (0.99.1: off to start with)
        public bool BcOffOnce = false;             // (0.99.1) both put off once (they started on in 0.98-0.99)
        public int BcBgModeWas = -1;               // (0.99) the background choice before BackgroundChanger took the background (-1: none)
        public int MusicVolume = 50;               // percent (0..100)
        public bool DetBlackBackground = false;

        // Game card settings
        public int CardSize = -1;                  // 1 (small) .. 7 (big); -1 = Playnite's own number of columns
        public int RowDesign = 0;                  // Row layout: 0 Lower (the row near the bottom), 1 Tall (portrait panels across the screen), 2 Full Screen (a big card in the middle)
        public bool RowLoop = false;               // Row layout: going past the last game comes round to the first (Tall always does)
        public int RowFullCount = 6;               // Row › Full Screen: panels on screen at once (1-10 since 0.89; more makes them narrower)
        public int RowTallCount = 3;               // (0.89) Row › Tall: whole covers across (1-10; the next ones cut off at the edges)
        public double CarTallW = 1, CarTallH = 1;  // (0.89) Row › Tall: Free move › Y sizes the covers, width and height on their own (0.4-2)
        public double CarTallFill = 0;            // (0.90) Row › Tall: the covers' height as part of the room (0: 2:3, as tall as fits); the count (zoom) sets their width
        public double CarFullH = 1;                // (0.89) Row › Full Screen: the panels' height from the top (0.3-1)
        public bool RowShowInfo = false;           // Row › Tall / Full Screen: the logo, game info and button hints show (over the cards)
        public int CardSpacing = -1;               // the gap between cards (row, grid, list, extra rows, Board), px; -1 = as it comes
        public int CardShape = 0;                  // 0 rectangle (Playnite's cover shape), 1 square, 2 wide (landscape art), 3 circle, 4 hexagon, 5 triangle up, 6 triangle down
        public bool TriAlternate = true;           // (before 0.52: triangles taking turns; now TriArrange)
        public int TriSpacing = 4;                 // triangles: the gap between them, % of a triangle's width (they're pushed together, not resized)
        public int TriArrange = 0;                 // the Triangle card shape: 0 take turns up / down (and fit together), 1 all point up, 2 all point down
        // Board home layout (HomeLayoutMode 3): cards you place on a grid
        public List<BoardCard> BoardCards = new List<BoardCard>();
        public bool BoardSeeded;                   // the starting board was made once (a board you empty stays empty)
        // Manual home layout (HomeLayoutMode 4): tiles you put down yourself (same cards as the Board, its own list and grid)
        public List<BoardCard> ManualCards = new List<BoardCard>();
        public int ManualPlacement = 0;            // Manual Board: 0 on a grid, 1 free placement (anywhere, any size)
        public List<BoardCard> FreeCards = new List<BoardCard>();
        public List<BoardPage> ManualPages = new List<BoardPage>(), FreePages = new List<BoardPage>();   // Manual Board pages (grid / free placement)   // free placement's tiles (in 1/96 of the width, 1/49 of the height)
        public int ManualCols = 8, ManualRows = 4;
        public bool ManualSnap = false;
        public List<BoardCard> BoardPresets = new List<BoardCard>();   // tile presets (Board and Manual): a card's settings with a name
        public List<BoardGroup> ManualGroups = new List<BoardGroup>();   // (0.91) Manual board: groups of tiles (BoardCard.Group)
        public int BoardCols = 5, BoardRows = 3;   // the grid the cards snap to
        public bool BoardSnap = true;              // moving / sizing in whole grid squares (off: small steps)
        public int BoardPageAxis = 0;              // past the edge to the next page of games: 0 Left / Right, 1 Up / Down
        public bool BoardSlideOne = false;         // (1.0.2) past the edge: every tile moves along one game (one new game comes in), not a whole new page
        public int BoardIndicator = 0;             // 0 dots, 1 "2 / 5", 2 hidden
        public int BoardLiveSeconds = 8;           // live tiles change this often
        public int PreviewMode = 0;                // trailer when a card stays selected: 0 off, 1 in the card, 2 in the background
        public int PreviewDelay = 5;               // seconds before it starts
        public List<string> ProfileMenuOrder = new List<string>();   // (0.99.11) the profile menu's rows in your order (their keys)
        public int BgChangeDelay = 150;            // (0.99.7) ms after moving to another game before the home background changes (0–400)
        public bool PreviewSound = false;
        public bool SkipGamePage = false;          // A plays the game instead of opening the game page

        // Game page extras
        public bool DetShowOptionsButton = true;
        public bool DetShowTrailerControls = true; // rewind / play-pause / forward / sound under the trailer

        // Master edit: elements moved by hand (offsets in 1080p pixels), per screen
        public bool MasterHome = false;   // before 0.27: Master edit on for the home screen (set spots were switched off)
        public bool MasterGame = false;
        public bool EditsMerged = false;
        public bool LayoutHasViews = false;        // a layout file with the gallery's and achievements' sort / filter / tab (0.71+)
        public bool DebugLogging = false;   // Help › Detailed logging
        public bool OverlayEnabled = true;
        public int StoreImageQuality = 2;   // Store pictures: 0 low (slow connections), 1 medium, 2 high  // the in-game overlay (built in since 0.29); applies when Playnite starts  // 0.27 joined Quick and Master Edits: fine placement now sits on top of the set spots
        public Dictionary<string, double[]> MasterOffsets = CopyOf(GameDefaultOffsets);   // (0.99.61: the game page's parts come placed as Kipping0 set it up)
        public Dictionary<string, double[]> DetRooms = new Dictionary<string, double[]>();      // (0.99.51) the game page's lower parts: the room each took last time it had something (width, height), kept for the games it has nothing for
        public Dictionary<string, double[]> MasterSizes = CopyOf(GameDefaultSizes);   // Free move › Y: width, height per part (0 = its own); "#gap": a deleted part's kept room

        /// <summary>
        /// (0.99.61) The game page as it comes (top and lower page): Kipping0's own layout. The lower page has How long to beat,
        /// Save backups, Your captures and More like this, moved up into the room of the parts that are off (their room is kept:
        /// the "#gap" sizes); the top page has the logo, the buttons and the hints moved, no description or game info.
        /// Reset (Layouts &amp; reset, or Reset this page) comes back to this.
        /// </summary>
        internal static readonly Dictionary<string, double[]> GameDefaultOffsets = new Dictionary<string, double[]>
        {
            { "game.buttons", new double[] { -30, 140 } }, { "game.logo", new double[] { -25, 220 } }, { "game.hints", new double[] { -20, 0 } },
            { "game.sec.saves", new double[] { -15, -430 } }, { "game.sec.hltb", new double[] { -583, -10 } },
            { "game.sec.similar", new double[] { -20, -520 } }, { "game.sec.captures", new double[] { -15, -510 } }
        };
        internal static readonly Dictionary<string, double[]> GameDefaultSizes = new Dictionary<string, double[]>
        {
            { "game.stats#gap", new double[] { 724.5, 93.2, 0, 0 } }, { "game.facts#gap", new double[] { 1221.8, 129.3, 0, 0 } }, { "game.desc#gap", new double[] { 1000, 101.5, 0, 0 } },
            { "game.sec.ach#gap", new double[] { 568, 463.3, 0, 0 } }, { "game.sec.scores#gap", new double[] { 568, 212.9, 0, 0 } }, { "game.sec.notes#gap", new double[] { 568, 116.7, 0, 0 } },
            { "game.sec.mods#gap", new double[] { 568, 229.3, 0, 0 } }, { "game.sec.status#gap", new double[] { 568, 187.9, 0, 0 } }, { "game.sec.history#gap", new double[] { 568, 315.3, 0, 0 } },
            { "game.sec.captures", new double[] { 1748, 182, 0, 0 } }
        };

        internal static Dictionary<string, double[]> CopyOf(Dictionary<string, double[]> d)
        {
            var r = new Dictionary<string, double[]>();
            foreach (var kv in d) r[kv.Key] = (double[])kv.Value.Clone();
            return r;
        }
        public Dictionary<string, double> MasterRotations = new Dictionary<string, double>();   // Free move › LT / RT: how far each part is turned (degrees, clockwise, -180..180)
        public Dictionary<string, int> MasterLayers = new Dictionary<string, int>();            // Free move › Menu › Layer: each part's place in front of / behind its neighbours (added to its own ZIndex)
        public Dictionary<string, double> MasterOpacity = new Dictionary<string, double>();     // Free move › Menu › Opacity: 0.1..1 (no entry = 1)
        public bool CommunityWidgetsOn = false;    // Settings › Community › Widgets › Community widgets (web snippets, news feeds, data widgets): off until you turn them on
        public int ListStretch = 0;                  // (0.35 only, not used now: the list's own height is in MasterSizes)
        public double ListTileH = 0;                 // list: each game's height / width in px (0: worked out from the screen)
        public double ListTileW = 0;
        public int ListInfoShare = 50;               // list: % of each game the play info takes (the rest is its art)
        public List<string> ListInfoItems = null;    // list: what the play info shows (null: play time, not installed, last played)
        public int HomeBgMode = 0;                   // home background: 0 the game's art, 1 a plain colour, 2 the game's trailer (over its art; = PreviewMode 2), 3 your own picture, 4 trailer muted (live), (0.86) 6 the game's tile (its cover, softened), 5 slideshow
        public string HomeBgImage = "";              // Background › Your own picture: the picture (a copy in the add-on's data folder)
        // r15 (B): Background › Move the picture: where the home background's centre sits, as a share of the screen (0 centred; -1..1, right / down positive)
        public double HomeBgOffsetX = 0;
        public double HomeBgOffsetY = 0;
        // (0.87) Move the picture and the fade, per layout: key → [offset X, offset Y, fade dir, fade reach]; BgLayoutKey = whose the values above are
        public Dictionary<string, double[]> BgPerLayout = new Dictionary<string, double[]>();
        public string BgLayoutKey;
        // (0.88) Free move on the home screen, per layout: key (as BgLayoutKey) → that layout's home.* places, sizes, turns, layers, opacity
        public Dictionary<string, HomePlaceSet> HomePlacePerLayout = new Dictionary<string, HomePlaceSet>();
        public double HomeTileBgScale = 0.8;       // (0.87) Background › The game's tile: its height, as a share of the screen's
        public bool BgTrailer041 = false;            // 0.41: a trailer preview set to "In the background" became Background › The game's trailer
        public string HomeBgColor = "#0E1014";
        // ---- round 12 (agent C): HomeBgMode 4 = the game's trailer, muted (live background), 5 = slideshow of the game's pictures ----
        public bool LiveSlideWeb = true;             // Slideshow: also the store's screenshots (Steam games)
        // Look and feel › Intro and outro: 0 none, 2 your own video (IntroVideo / OutroVideo: a copy in ExtensionsData\intro\), (0.99.66) 1 (the old animated logo) and 3 (Kippix's own video) are gone: read as 0
        public int IntroMode = 0;                    // (0.99.66: none unless you choose a video of your own)
        public string IntroVideo = "";
        public int OutroMode = 0;
        public string OutroVideo = "";
        public bool IntroSound = true;               // the intro / outro video's own sound
        // Look and feel › Depth: the background shifts a little against your moves (0 off, 1 low, 2 medium, 3 high)
        public int DepthMode = 0;
        // Home settings › Background › Animated art (Look and feel until r14): 0 off, 1 stream (memory only), 2 download (ExtensionsData\animated\)
        public int AnimArtMode = 1;                  // Animated covers (0.81): 0 off, 1 the highlighted cover zooms in and out slowly (was 1 stream / 2 download)
        public bool AnimArtAllCards = false;         // every card on screen, not just the highlighted one
        public bool AnimArtHero = true;              // an animated hero behind the home screen too
        public int AnimArtNoArt = 0;                 // when a game has no animated art: 0 a living picture of its own art (r14; was its trailer in r13), 1 the picture stays
        public string SgdbApiKey = "";               // SteamGridDB API key, encrypted for this Windows user (DPAPI), base64 (desktop settings)
        // Look and feel › Night mode: 0 off, 1 automatic (NightFrom..NightTo, minutes after midnight), 2 always
        public int NightMode = 0;
        public int NightFrom = 22 * 60;
        public int NightTo = 7 * 60;
        public int NightDim = 30;                    // % darker
        public int NightWarm = 40;                   // % warmer tint
        public int NightQuiet = 50;                  // % quieter menu sounds and music
        public bool NightNoAutoplay = false;         // no trailers start by themselves at night
        public int HomeFadeDir = 0;                  // fade to black: 0 from the bottom, 1 top, 2 left, 3 right, 4 none
        public int HomeFadeReach = 44;               // how far it reaches, % of the screen
        public Dictionary<string, string> DetCardColors = new Dictionary<string, string>();   // game page cards set to Solid: their colour                  // list mode: half games added to (or taken off) the list's height
        public Dictionary<string, int> DetCardLooks = new Dictionary<string, int>();         // game page cards: 1 solid, 2 no background
        public bool MasterSnap = true;
        public int MasterGrid = 20;
        public bool DetShowFriends = true;         // Steam friends who play / played the game
        public bool DetFlip = false;               // 0.13 only (became DetLayout 1)
        public int DetLayout = 0;                  // game page: 0 info left / media right, 1 info right / media left, 2 media top centre, info centred below
        public string SteamApiKey = "";            // encrypted for this Windows user (DPAPI), base64

        // Store
        public string StoreTiles = "steam,epic,gog";   // stores shown as tiles (All stores is always there)
        public string StoreCountry = "";           // prices for this country ("" = Windows' region)
        public int StoreOpenIn = 0;                // store pages: 0 the theme's browser, 1 the store's app / Windows' browser
        public int StoreTrailers = 1;              // 0 off, 1 with the pictures, 2 also play at the top of the page
        public bool StoreTrailerSound = true;
        public string ItadApiKey = "";             // IsThereAnyDeal key pasted in desktop settings, DPAPI encrypted
        public int BrowserCursorSpeed = 3;         // 1 slowest .. 5 fastest
        public double BrowserZoom = 0;             // Chromium zoom level (0 = 100%, each 1 = x1.2)
        public bool StoreResellers = false;
        public bool StoreHideOwned = false;        // hide games already in the Playnite library from store lists
        public bool StoreHideFree = false;         // hide free games from store lists (not the wishlist or "Free games")
        public List<WishItem> StoreWishlist = new List<WishItem>();
        public List<WishItem> StoreRecent = new List<WishItem>();   // games looked at in the Store, newest first (max 20)
        // rows on the Store's first page
        public bool StoreRowWish = true;
        public bool StoreRowRecent = true;
        public bool StoreRowFree = true;
        public bool StoreRowPopular = true;
        public bool StoreRowSoon = true;
        public bool StoreRowDeals = true;
        // trailer preview on store tiles
        public bool StorePreview = true;
        public int StorePreviewDelay = 3;          // seconds
        public bool StorePreviewSound = false;

        // Home: tiles to the left of the games (Store, Achievements, Random game)
        public int HomeTilesMode = 1;              // 0 hidden (off), 1 auto show (appears when you go left past the first game), 2 always on screen
        public bool HomeTileStore = true;
        public bool HomeTileLibrary = true;        // (0.99.17) a Library tile before the games (with the other tiles: Auto show = going left past the first game, Always show = at the start)
        public bool HomeTileAch = false;
        public bool HomeTileRandom = false;
        public bool HomeTileUpNext = false;        // an Up next tile (opens the Up next page)

        // Home: widgets, placed by hand (1080p units)
        public bool WidgetsOn = true;
        public List<WidgetConfig> Widgets = new List<WidgetConfig>();
        public string WeatherTown = "";
        public double WeatherLat, WeatherLon;
        public bool WeatherFahrenheit = false;
        public bool RecordSessions = true;         // keep our own play sessions (for Spotlight: play time) when GameActivity isn't installed

        // What the game info line shows
        public bool InfoPlaytime = true;
        public bool InfoLastPlayed = true;
        public bool InfoAchievements = true;
        public bool InfoPlatform = true;
        public bool InfoSource = false;
        public bool InfoCompletion = false;
        public bool InfoReleaseYear = false;
        public bool InfoPlayers = false;           // (r13) players online now (Steam), looked up for the highlighted game once it rests
        public bool InfoFriends = false;           // (r13) "2 friends playing now" / "3 friends play this" (Steam key / PlayerActivities)
        public bool HomeHltb = false;              // (0.96) home screen: How long to beat (main story) for the highlighted game, its own part
        public List<string> InfoOrder;

        // ---- 0.31 ----
        public string CurrentLayout = "";          // Layouts › Layout: "" Kippix default (default), "saved:Name" or "uploaded:Name"
        public bool PinGallery = true;

        // Gallery (screenshots and clips)
        public List<string> GalleryFolders = new List<string>();      // folders you added (the overlay's folders are always in)
        public List<string> GalleryFavourites = new List<string>();   // full paths
        public List<ArtSwap> ArtSwaps = new List<ArtSwap>();          // gallery photos used as a game's art, so the same button can undo it
        public bool GallerySteam = true;           // Steam screenshots too
        public int GallerySort = 0;                // 0 newest first, 1 oldest first
        public string GalleryView = "games";       // the tab it opens on: all, games (By game, the default), dates, favs
        public int GalleryShow = 0;                // 0 photos and videos, 1 photos, 2 videos
        public int GalleryCleanKind = 1;           // clean-up: 0 photos and videos, 1 videos, 2 photos
        public int GalleryCleanDays = 90;
        public int GallerySlideSeconds = 6;

        // Home: one row or a grid, extra rows, tile badges
        public int HomeLayoutMode = 0;             // 0 row, 1 grid, 2 a list down one side, 3 board, 4 manual board, 5 folders, 6 mosaic
        public bool LibraryDownOpens = true;       // Row: Down from the games (or from the last row under them) opens the library view: every game as a mosaic, B goes back
        public int JustRows = 3;                   // mosaic: rows on screen (2-5): the height its rows aim for (each row ends up a little over or under, to fill the width)
        // Folders (HomeLayoutMode 5): folder tiles on the home screen, each opening its own games
        public List<HomeFolder> Folders = new List<HomeFolder>();   // All games (Id "all") first, then yours in the order they were made
        public int FolderGameDesign = 0;           // an opened folder's games: 0 Row (the Row design and Loop), 1 Grid, 2 List, 3 Board, 4 Manual Board
        public int FolderTileCount = 6;            // the folder tiles (always drawn like Row › Full Screen): how many on screen (6-10)
        public bool FolderDuplicates = false;      // a game in more than one of a folder's lists shows once per list (the theme's own row designs only)
        public int FolderInstalled = 0;            // before 0.74 one choice for every folder (0 all, 1 installed only, 2 not installed); now each folder has its own (HomeFolder.Installed): this only seeds folders without one
        public int FolderSort = 0;                 // an opened folder's order: 0 name, 1 recently played, 2 most played, 3 recently added, 4 as Playnite sorts
        public int ListRows = 5;                   // list: games on screen at once (1-10; the games keep their size, the list grows)
        public bool ListShowInfo = true;           // list: the play info beside each game's art (off: the art takes the whole line)
        public bool ListShowName = true;           // list: the game's name at the top of its play info
        public bool ListCount040 = false;          // 0.40: the list's height is games x their height (its own height in MasterSizes dropped once)
        public int ListSide = 0;                   // list: 0 left, 1 right
        public bool ListSideMoved033;
        public int ReleaseMonths = 12;             // release calendar: how far ahead (1-12 months, 24, 36)
        public bool HoldAFreeMove = true;          // hold A on the games or the game page: Free move
        public bool GuideGoesHome = true;
        public bool GuideBar = true;
        public bool HomeProgress = true;             // (0.99.48) story progress under the home screen's game info (ThemeApi.StoryProgress.cs)                 // (0.99.46) the Guide button brings up the quick bar (ThemeApi.GuideBar.cs)
        public List<string> GuideBarItems;           // (0.99.46) its items, in order (null: GuideBarDefault)
        public int HomeButton = 0;                 // which goes home: 0 the Guide button, 1 Select (View) + Start together          // the Guide (home) button goes to the home screen (not while a game runs)              // 0.33 made the left the default: moved once for settings saved before
        public List<string> PinnedGames = new List<string>();   // game ids shown first, before the games (newest pin last)
        public List<string> RecentSearches = new List<string>();
        public bool ImportSteamWishlist = false;  // bring your Steam / GOG wishlist into the Store's (when the Store opens)
        public bool ImportGogWishlist = false;
        public bool SyncWishToSteam = false;     // a game wishlisted here goes on your Steam wishlist too (Steam games only)
        public System.DateTime WishImportAt;
        public List<string> WishDismissed = new List<string>();
        public List<string> WishRemoveFromSteam = new List<string>();   // Steam app ids taken off the wishlist here, still to take off Steam's   // "steam:123" / "gog:456" taken off here: not brought back
        public List<string> ExtraPins = new List<string> { "quick" };   // top bar shortcuts added in 0.32 that are pinned
        public List<GameShelf> Collections = new List<GameShelf>();
        public List<string> UpNext = new List<string>();   // Up next: game ids you mean to play, in order (each profile has its own)
        public int GridRows = 2;
        public int GridColumns = 6;
        public bool ExtraRows = false;             // rows under the games (one row only)
        public List<string> ExtraRowKeys;          // which rows, in order: favourites, recent, installed, never, filter:<id>
        public bool BadgeInstalled = false;
        public bool BadgeNew = false;
        public bool BadgeAch = false;
        public bool BadgeHltb = false;

        // Look and feel
        public bool AccentFromGame = false;
        public int AccentStrength = 1;             // 0 subtle, 1 normal, 2 strong
        public bool BackgroundMotion = false;
        public int MotionSpeed = 1;                // background zoom: 0 slow, 1 normal, 2 quicker, 3 very slow (0.96)
        public int MotionAmount = 0;               // background zoom: 0 subtle, 1 medium, 2 strong
        public bool MotionDefaults035 = false;     // 0.35: new defaults (normal speed, subtle) applied once
        public bool AmbientMode = false;
        public int AmbientMinutes = 5;
        public int AmbientSeconds = 12;            // (0.86) each picture shows this long
        public int AmbientPos = 8;                 // (0.86) where the time and logo sit: 0 top left … 8 bottom right (rows of three)
        public bool AmbientWakeText = true;          // (0.86) "Press any button to come back" under them
        public int AmbientBg = 0;                    // (0.99.51) 0 the games' pictures, else a plain colour (ThemeApi.ScreensaverLook.cs AmbientBgColors)
        public bool SessionReminder = false;
        public bool SessionSummary = true;          // coming back from a game: what you played, this session, altogether
        // Settings › Notifications: the card's look and what it says
        public int NotifyPos = 0;                    // 0 top right, 1 top left, 2 top centre, 3 bottom right, 4 bottom left, 5 bottom centre
        public int NotifyStyle = 0;                  // 0 dark, 1 accent colour, 2 see-through, 3 light
        public int NotifySize = 1;                   // 0 small, 1 normal, 2 large, 3 extra large
        public int NotifySeconds = 8;
        public bool NotifyCover = true, NotifySessionLine = true, NotifyTotalLine = true;
        public List<string> WishReleaseNotices;      // wishlisted games already told "Out today" about (0.85): store id | release date
        public bool NotifyOther = true;              // Steam sign-in / wishlist cards
        public bool SteamPriorityAuto = false;       // (1.0.0: off until you turn it on; it closes Steam) (0.99.42) new installed Steam games from a library update: set to High Priority (closes / reopens Steam)
        public bool GameUpdateNotify = true;         // (0.99.39) a card when an installed game has an update waiting (ThemeApi.GameUpdates.cs)
        public int GameUpdateWhen = 0;               // 0 at start-up and every time its page opens, 1 only at start-up, 2 once on its page (per update)
        public List<string> GameUpdateSeen;          // (GameUpdateWhen 2) the updates already told about: store | id | build
        public int SessionReminderMinutes = 120;
        public int ButtonIcons = 0;                // 0 automatic, 1 Xbox, 2 PlayStation, 3 Switch, 4 keyboard
        // Look and feel › Look packs / Theme Studio (ThemeApi.LookPacks.cs): colours, fonts, cards, panels, top bar, buttons
        public LookStyle Look;                     // null = Kippix's own look (a layout from before 0.78 has none: yours is kept)
        public string LookPack = "builtin:kippix";   // the pack it started from: builtin:<id> or user:<file name>

        // Game page: what's further down the page, and the More button
        public bool DetMoreSections = true;
        public bool DetSecHistory = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public bool DetSecHltb = true;
        public bool DetSecCaptures = true;
        public bool DetSecAch = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public bool DetSecStatus = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public bool DetSecNotes = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public bool DetSecScores = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public bool DetSecSimilar = true;
        public int DetSimilarShape = 0;            // More like this: its covers' shape (CardShapes: 0 tall cover, 1 square, 2 wide, 3 circle, 4 hexagon, 5 triangle)
        public List<string> DetLower;             // what's on the lower page (null = the sections, as before): logo, info, desc, media, friends, history, hltb, ach, status, notes, scores, captures, similar
        public List<string> DetNoInteract = new List<string>();   // parts that are just for show (the controller skips them)
        public List<string> DetInfoOff = new List<string> { "hltb", "size" };   // game page "Play time and game info": the lines switched off (HowLongToBeat and the installed size start off)
        public List<string> DetInfoOrder;          // ... and their order (null = as they come; the play info and the game's details are ordered each in their own block)
        public bool DetShowHints = true;           // the button hints at the bottom of the game page (Free move can delete them)
        public int DetDescSource = 0;              // 0 the store's description (Steam first, then GOG), 1 Playnite's
        public bool DetShowMoreButton = true;
        public bool DetShowDlcWarning = true;

        // ---- 0.58 ----
        // Accessibility
        public int TextSize = 0;                   // 0 normal, 1 large, 2 larger, 3 largest
        public bool ReduceMotion = false;          // no slides, zooms, background motion or card push-aside
        public bool HighContrast = false;          // solid backgrounds, stronger outlines and focus rings
        // Language: "" follows Playnite's own, or a file name in the theme's Localization folder (en_US...)
        public string LanguageOverride = "";
        // Seasonal touches on the home screen (paused while a trailer plays or a game runs)
        public bool SeasonalOn = false;
        public int SeasonalMode = 0;               // 0 automatic by date, 1 always on (every effect switched on)
        public bool SeasonSnow = true, SeasonHalloween = true, SeasonChristmas = true, SeasonNewYear = true,
                    SeasonValentine = true, SeasonSpring = true, SeasonAutumn = true;
        // Pinned game tiles: a game kept in an exact slot (One row, Grid, List)
        public List<SlotPin> SlotPins = new List<SlotPin>();
        public bool SlotPinBadge = true;
        public bool SlotPinsWhenFiltered = false;  // false: skipped while a filter is active
        // Split screen finder
        public int SplitPlayers = 2;
        public int SplitScreenKind = 0;            // 0 either, 1 split screen, 2 shared screen
        public int SplitMode = 0;                  // 0 either, 1 co-op, 2 versus
        public bool SplitOnline = false;           // false: together on this PC (local players), true: online multiplayer
        public int OnlinePlayers = 0;              // online: at least this many players (0 any number); SplitPlayers 0 = any too
        public bool SplitExact = true;             // (r10c) a chosen number shows games for exactly that many (false: that many or more), both ways of playing
        // Save backups (Ludusavi add-on)
        public bool DetSecSaves = true;
        public bool DetSavesAdded058 = false;      // the card added to the lower page once
        // (r12) game page: News (Steam news / patch notes) and Mods (Nexus Mods) cards, players online now and feature badges
        public bool DetSecNews = false;            // (r13) off at first: the News button next to Achievements opens it
        public bool DetNewsButton = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)          // (r13) game page: News button (Steam games) right of the Achievements button
        public bool DetNewsCardOff079 = false;     // (r13) the News card turned off once for everyone (the button replaces it), and the friends line off
        public bool DetSecMods = false;   // (0.99.61: the game page as Kipping0 set it up is how it comes)
        public bool DetNewsModsAdded = false;      // the two cards added to the lower page once
        public int BadgeFeatHide = 0;              // (0.96) the feature icons the cards leave out (Feat flags in ThemeApi.Badges.cs)
        public bool BadgeFeatures = false;         // home cards: small feature icons (controller, co-op, split screen, online, VR, Steam Deck)
        public bool BadgeStore = false;            // home cards: a small pill with the game's store (Steam, GOG, Epic...)
        public bool SavesBeforeLaunch = false;
        public bool SavesAfterClose = false;
        // Other add-ons (only used when they're installed)
        public bool UseGamePass = true;
        public bool ShowNexusLink = true;
        public bool ShowEngine = true;
        public bool ShowFreestyle = true;
        public bool SplashStandAside = true;

        // ---- 1.0.1 ----
        // Media › Shows & movies: folders of your own videos (ThemeApi.LocalMedia.cs)
        public List<string> LocalMediaFolders = new List<string>();
        public Dictionary<string, string> LocalMediaKinds = new Dictionary<string, string>();   // folder → "movies" / "shows" (missing: worked out)
        public string LocalMediaSort = "name";     // name, added
        public bool LocalMediaInKippix = true;     // play in Kippix's own full screen player (else Windows' player for the file)
        public double LocalMediaVolume = 1.0;
        public bool LocalMediaSubs = true;         // subtitles (a .srt beside the video) shown
        // Game page › Splash screen layout: what the launch screen shows over the game's picture, and where (ThemeApi.SplashLayout.cs).
        // X / Y: moved by a share of the screen from where it sits by default (0 = there); Size: 1 = as it comes.
        public bool SplashShowLogo = false;
        public bool SplashShowCard = false;
        public bool DetBgZoom = true;              // (1.0.2) Game page › Animation: the background zooms in slowly as the page opens
        public bool SplashShowStatus = true;       // Playnite's "Starting…" words
        public double SplashLogoX = 0, SplashLogoY = 0, SplashLogoSize = 1;
        public double SplashCardX = 0, SplashCardY = 0, SplashCardSize = 1;
        public double SplashStatusX = 0, SplashStatusY = 0, SplashStatusSize = 1;

        // ---- 0.59 ----
        // Profiles (testing: switched on in the helper's desktop settings)
        public bool ProfilesEnabled = false;
        public bool AskWhoAtStart = true;          // the "Who's playing?" screen when Playnite starts
        public bool SyncLayouts = false;           // every profile shares one layout
        public string ProfilesPin = "";            // the grown-ups' PIN (hashed): locked profiles, the Store / Settings when a profile needs it
        public List<PlayerProfile> Profiles = new List<PlayerProfile>();
        public string CurrentProfile = "";
        // Welcome (first start)
        public bool WelcomeDone = false;
        public bool GameReset094 = false;
        public bool LookReset096 = false;   // (0.96) the menu theme put back to Kippix's once (it had come from the layouts' presets)   // (0.94) the game page put back once to how Kippix comes (the user asked)
        public bool WelcomeOnBoot = false;         // testing: show it every time Playnite starts
        public bool WelcomeSkippable = false;      // (0.86) testing: Getting started has a Skip button even on a new install
        // Profile page
        public bool ShowStreaks = true;
        // Profile hub (r9c): the tab it opens on, tabs taken off the strip, the Library tab's sort and filter
        public string ProfileHubTab = "overview";
        public bool ProfileHubRemember = true;     // the profile opens on the tab you used last (off: always Overview)
        public List<string> ProfileHubHidden = new List<string>();
        public int ProfileLibSort = 0;             // 0 name, 1 recently played, 2 most played, 3 recently added
        public int ProfileLibShow = 0;             // 0 every game, 1 installed, 2 not installed
        public string ProfileWishView = "wishlist";   // the Wishlist tab: "wishlist" or "releases" (the release calendar)
        // r12 B: sections on the profile's Overview / Your year taken off ("heat" play calendar, "finished" finished games, "closest" closest achievements, "share" share card button)
        public List<string> ProfileHubSectionsHidden = new List<string>();
        public bool ShareOpenFolder = false;       // share cards: open Pictures\Kippix after saving one
        public bool AchNextUp = true;              // NEXT UP (the locked achievements you're closest to) on a game's achievements page and the game page card
        // which Playnite completion status means Beaten / 100% / Dropped (a status id; "" = worked out from the names)
        public string CompletionBeaten = "";
        public string Completion100 = "";
        public string CompletionDropped = "";
        // Community layouts (testing: another repository / branch)
        public string CommunityRepo = "";
        public string CommunityBranch = "";
        public string CommunitySort = "new";       // (1.0.4) the gallery: "new" (newest first) or "downloads" (most downloaded first)
        public string GitHubToken = "";            // (1.0.1) your GitHub sign-in for Community uploads, encrypted for this Windows user (DPAPI)
        public string GitHubLogin = "";            // its GitHub name
        public List<CommunitySentItem> CommunitySent = new List<CommunitySentItem>();   // your uploads (newest first) and what happened to them

        // ---- 0.60 ----
        // Weather touches (Home settings › Seasonal touches): rain, storms, snow, fog or sunshine on the home screen
        public bool WeatherFxOn = false;
        public bool WeatherFxSync = true;          // follows the weather where you are (the weather widget's town); off: set by hand
        public string WeatherFxKind = "rain";      // by hand: rain, storm, snow, fog, sun
        public bool WeatherFxSchedule = false;     // by hand: always, or on set days between set hours
        public int WeatherFxDays = 127;            // Monday = 1, Tuesday = 2 ... Sunday = 64
        public int WeatherFxFrom = 18, WeatherFxTo = 23;
        public int WeatherFxWhen = -1;             // set by hand (0.82): 0 always, 1 each on its own dates (like Seasonal touches), 2 on set days and times; -1: from WeatherFxSchedule
        public int WeatherFxLivePick = 31;         // following the weather where you are (0.85): the kinds that may show (same bits as WeatherFxPick)
        public int WeatherFxPick = -1;             // set by hand (0.82): the effects switched on (rain 1, storm 2, snow 4, fog 8, sunshine 16); -1: from WeatherFxKind
        public List<int> WeatherFxDates;           // set by hand, on their dates (0.82): from / until for each effect (month × 100 + day), rain, storm, snow, fog, sunshine

        // ---- r12 G: tour, tips, Doctor, controller buttons, handheld, touch ----
        public bool TipsOn = true;                 // first-time tips (a small card the first time a screen opens)
        public List<string> TipsSeen = new List<string>();   // the screens whose tip has been shown
        public bool SafeMode = false;              // Kippix Doctor: extras off (widgets, carousel / folders, live backgrounds, overlay, animations)
        public bool SafeModeOffer = true;          // offer safe mode when the last two starts didn't finish
        public Dictionary<string, string> SafeModeSaved = new Dictionary<string, string>();   // what safe mode switched off (put back when it's turned off)
        public Dictionary<string, string> PadMap = new Dictionary<string, string>();          // Controller buttons: action id -> button (only the ones moved)
        public int HandheldMode = 0;               // 0 automatic (a small screen), 1 on, 2 off
        public bool HandheldCompact = true;        // handheld: bigger text, fewer rows, bigger cards, battery in the top bar
        public bool HandheldBatterySaver = true;   // handheld on battery: no live backgrounds, animated art, seasonal touches or ambient mode
        public Dictionary<string, string> HandheldSaved = new Dictionary<string, string>();   // what the compact layout changed (put back when handheld goes off)
        public int TouchMode = 0;                  // touch: 0 automatic (on with a touch screen), 1 on, 2 off
        public bool MouseRightBack = true;         // right-click goes back (B)
        public bool MouseWheelRow = true;          // the mouse wheel steps through the home row
        public int DetEntranceStyle = 1;           // (0.99.70) Game page › Animation: 0 off (straight away), 1 rise, 2 fade, 3 from the sides, 4 drop in, 5 zoom in, 6 settle, 7 cinematic, 8 cascade, 9 pop, 10 focus
        public int HomeBackTo = 0;                 // (0.99.62) B on the home screen: 0 the very first game, 1 the top bar, 2 nothing
        public int MouseParkMode = 0;              // (0.99.57) the pointer when Playnite starts / comes back: 0 off screen (hidden until it moves), 1 where it was, 2 middle of the screen
    }

    /// <summary>A person who plays on this PC (Settings › Profiles). Kids and Guest are kinds of profile.</summary>
    /// <summary>(1.0.1) One upload sent to the Community gallery (a pull request).</summary>
    public class CommunitySentItem
    {
        public string Kind = "layouts";
        public string Name = "";
        public int Number;
        public string Url = "";
        public DateTime At;
        public string State = "open";   // open (waiting), merged (in the gallery), closed (not added)
    }

    public class PlayerProfile
    {
        public string Id;
        public string Name;
        public string Kind = "person";             // person, kids
        public string PicturePath;
        public bool NeedsPin;                      // choosing it asks for the PIN
        // Steam: one of the accounts Steam remembers on this PC
        public string SteamId64, SteamAccount, SteamPersona;
        public bool OnlyOwnSteamGames = true;      // with a Steam account linked: other accounts' Steam games are hidden
        public bool HideNonSteam = false;          // GOG, Epic, emulators...: shared, or hidden for this profile
        public int StoreAccess = 0;                // 0 open, 1 needs the PIN, 2 hidden
        public int SettingsAccess = 0;             // 0 open, 1 needs the PIN, 2 hidden
        public int MaxAge = 0;                     // Kids: games rated above this are hidden (0: no limit)
        public bool HideUnrated = false;           // Kids: games with no age rating are hidden too
        public List<string> HiddenGames = new List<string>();
        public List<string> Favourites = new List<string>();   // this profile's own favourites (not the main profile's: that's Playnite's)
        // Daily time limit (ThemeApi.TimeLimits.cs): minutes a day (0 = no limit that day), allowed hours (minutes after midnight)
        public bool LimitOn = false;
        public int LimitWeekdayMin = 60;           // Monday to Friday
        public int LimitWeekendMin = 120;          // Saturday and Sunday
        public bool LimitHoursOn = false;
        public int LimitFrom = 7 * 60;             // 07:00
        public int LimitUntil = 20 * 60;           // 20:00
        public bool LimitCloseGame = false;        // close the game (gracefully, never forced) when the time runs out
        public string LimitDay;                    // the day LimitUsedSec counts (yyyy-MM-dd)
        public long LimitUsedSec;                  // played that day, games started from Playnite while this profile was on
        public string LimitExtraDay;               // the day LimitExtraMin was given for
        public int LimitExtraMin;                  // extra minutes a grown-up gave for that day
        public string LimitPassDay;                // extra time also opens the allowed hours: from LimitPassFrom to LimitPassUntil that day
        public int LimitPassFrom, LimitPassUntil;  // (minutes after midnight)
    }

    /// <summary>A game kept in one place among the games (slot 1 = the first card).</summary>
    public class SlotPin
    {
        public string GameId;
        public int Slot;
    }

    /// <summary>One widget on the home screen: what it is, what it shows, where and how big (1080p units).</summary>
    /// <summary>A page of the Manual Board (Home is always there); X / Y: where it sits from Home (right / down).</summary>
    /// <summary>(0.88) One layout's Free move changes on the home screen (the home.* entries of the Master dictionaries).</summary>
    public class HomePlaceSet
    {
        public Dictionary<string, double[]> Offsets = new Dictionary<string, double[]>();
        public Dictionary<string, double[]> Sizes = new Dictionary<string, double[]>();
        public Dictionary<string, double> Rotations = new Dictionary<string, double>();
        public Dictionary<string, int> Layers = new Dictionary<string, int>();
        public Dictionary<string, double> Opacity = new Dictionary<string, double>();
        // (0.89) the widgets' places: id → [X, Y, Scale, WidthMul, HeightMul, Rotation, Layer, Opacity]
        public Dictionary<string, double[]> Widgets = new Dictionary<string, double[]>();
    }

    /// <summary>(0.91) One layout's presets: which is on ("" your own / Default, "kx1".."kx3", "custom"), and the snapshots kept.</summary>
    public class PresetSlot
    {
        public string Chosen = "";
        public string Own;          // your own (Default): a PresetSnap (JSON), taken when you first picked a preset here
        public string Cur;          // this layout's preset settings while another layout is showing (JSON of the fields)
        public string Fp;           // the settings just after the preset went on (changed since: Custom)
        public string Custom;       // (0.91 only: moved into Saved) your changes to a preset
        public string CustomFrom;   // (0.91 only) the preset they started from
        public Dictionary<string, string> Saved;   // (0.92) your changes to each Kippix design ("kx1".."kx3" → a PresetSnap, JSON)
    }

    /// <summary>(0.91) A snapshot for presets: the settings that make the layout, and its parts' places.</summary>
    public class PresetSnap
    {
        public HelperSettings F;
        public HomePlaceSet Places;
        public double[] Bg;         // home: Move the picture X / Y, fade direction, fade reach
        public string[] K;          // (0.93) the settings it holds (none: one from before 0.93, which held fewer)
    }

    /// <summary>(0.91) Manual board: tiles grouped together. With Carousel on, the group's game tiles show games from one list in
    /// turn, and going past the group's end (in its direction) brings the next games in, like Row.</summary>
    public class BoardGroup
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public bool Carousel = false;
        public int Dir = 0;                        // the way you move to see more: 0 right, 1 left, 2 down
        public bool Loop = false;                  // after the last game, round to the first
        public string Source = "all";              // the list its games come from (all, played, favourites, recent, installed, never, random, filter:<id>, shelf:<id>)
    }

    public class BoardPage
    {
        public string Id;
        public string Name;
        public int X, Y;
    }

    /// <summary>One card on the Board home layout.</summary>
    public class BoardCard
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Kind = "filter";             // "filter": games from Source, in turn; "pinned": always GameId; "store": the Store's top sellers;
                                                   // "widget": a widget (Target = its id); "shortcut": opens Target (store, achievements, gallery, search,
                                                   // collections, shelf:<id>, random); "logo" / "info" / "hltb": the highlighted game's; "empty": kept clear
        public string Target;
        public string Name;                        // a preset's name
        public bool KeepsWhat;
        public string Page;                        // Manual Board: the page it's on (null = Home)
        public int Back;                           // its background: 0 automatic (a card for games / shortcuts, none for logo / info), 1 card, 2 see-through, 3 none
        public bool HideText;                      // no name / heading on it (just the icon or picture)
        public List<string> Sources = new List<string>();   // a "library" tile: the lists of games it holds (all, played, filter:<id>, shelf:<id>...)                     // a preset: carries what it shows (its type and games), not only its look
        public string Source = "all";              // all, random, played, favourites, recent, installed, never, filter:<id>, shelf:<id>
        public string Group;                       // (0.91) Manual board: the group it's in (ManualGroups), or null
        public int GroupOrder;                     // (0.92) carousel group: its place in the order new games slide in (1 = first in), 0 = not set
        public string GameId;
        public bool EveryPage = true;              // a pinned card: on every page, or only the first
        public bool Live;                          // a filter card: changes game every few seconds
        public int Shape = CardShapes.Square;
        public int Art;                            // 0 what suits the shape, 1 cover, 2 wide art, 3 logo on its background, 4 background
        public int Nudge;                          // where the picture sits: 0 centre, 1 higher, 2 lower, 3 left, 4 right
        public List<string> Info = new List<string>();   // what a wide / large card shows: name, playtime, lastplayed, hltb, ach, installed
        public double X, Y, W = 1, H = 1;          // in grid squares (from the top left)
        public double Rotation;                    // turned (degrees, clockwise, -180..180; LT / RT while it's picked up)
        public int Layer;                          // in front of / behind other tiles (Menu › Layer while it's picked up)
        public double Opacity = 1;                 // 0.1..1 (Menu › Opacity while it's picked up)
        public double[] Crop;                      // 0.78 only (X/Y/W/H were the whole tile): turned into CropBox when settings load
        public double[] CropBox;                   // Manual board (0.79): cropped [left, top, right, bottom], fractions of the whole tile; X/Y/W/H are then the part that's
                                                   // left (what it takes up on the board); null = not cropped; replaced, never changed in place
        public bool CropCut;                       // cropped (0.82): false (the default) = the picture fills the part that's left, centred on it;
                                                   // true = it's cut where you trimmed (it stays at the whole tile's size and place). (0.81's CropFill had cut as the default)

        /// <summary>The same card (same id), for reading on another thread.</summary>
        public BoardCard Snapshot() { var c = (BoardCard)MemberwiseClone(); c.Info = new List<string>(Info ?? new List<string>()); if (Sources != null) c.Sources = new List<string>(Sources); return c; }

        public BoardCard Copy()
        {
            var c = (BoardCard)MemberwiseClone();
            c.Id = Guid.NewGuid().ToString("N");
            c.Group = null;   // (0.91) a copy isn't in the original's group
            c.GroupOrder = 0;
            c.Info = new List<string>(Info ?? new List<string>());
            if (Sources != null) c.Sources = new List<string>(Sources);   // a copy's lists are its own
            return c;
        }
    }

    /// <summary>A folder on the home screen (Games shown as: Folder): its name, its photo and the games in it.</summary>
    public class HomeFolder
    {
        public string Id = Guid.NewGuid().ToString("N");   // "all": All games (always there, always first)
        public string Name = "";                   // empty: its own name ("All games")
        public string Picture = "";                // its photo (a copy in the add-on's data folder, "folders")
        public List<string> Sources = new List<string>();   // whole lists: all, played, favourites, recent, installed, never, filter:<id>, shelf:<id>
        public List<string> Games = new List<string>();     // games picked by hand (ids)
        public bool Removed = false;               // All games only: taken off the home screen (Your folders › Add the All games folder brings it back, as it was)
        public int Installed = -1;                // the games it shows (and counts): 0 all, 1 installed only, 2 not installed (-1: not set yet, takes the old folder-wide choice once)
    }

    /// <summary>A collection: your own shelf of games (like a playlist).</summary>
    public class GameShelf
    {
        public string Id;
        public string Name;
        public List<string> Games = new List<string>();   // game ids, in the order added
    }

    /// <summary>A gallery photo made a game's background or cover: what it replaced, so pressing the button again puts that back.</summary>
    public class ArtSwap
    {
        public string GameId;
        public bool Background;
        public string Photo;
        public string NewId;
        public string OldId;
    }

    public class WidgetConfig
    {
        public string Id;
        public string Type;            // weather, clock, suggest, spotlight, free, wishlist, downloads, release
        public string Subject;         // spotlight: playtime / achievements / photos / friends; suggest: never / old / nearly / like / random
        public double X, Y;
        public double Scale = 1;
        public int Back;               // 0 see-through, 1 solid colour, 2 no background
        public string BackColor = "";  // #RRGGBB ("" = the theme's dark)
        public bool HideTitle;
        public string TextColor = "";  // #RRGGBB ("" = white, or dark on a light background)
        public double WidthMul = 1;    // its own shape: wider / narrower, taller / shorter (the inside rearranges to fit)
        public double HeightMul = 1;
        public double TextScale = 1;   // text inside bigger / smaller (never so far that anything is cut off)
        public bool NotInteractive;    // just for show: the controller skips it, nothing in it can be pressed
        public double Rotation;        // turned (degrees, clockwise, -180..180; LT / RT while it's picked up)
        public int Layer;              // in front of / behind other widgets (Free move › Menu › Layer)
        public double Opacity = 1;     // 0.1..1 (Free move › Menu › Opacity)
        public CustomWidgetDef Custom; // your own tiles (text, picture) and community widgets (web snippet, news feed, data): what they show (ThemeApi.CustomTiles.cs)
    }

    /// <summary>A play session we recorded (for Spotlight: play time when GameActivity isn't installed).</summary>
    public class PlaySession
    {
        public string GameId;
        public string Name;
        public System.DateTime Start;
        public long Seconds;
        public string Profile;   // who played (Profiles on; empty: the main profile / before profiles)
        public bool Estimated;   // filled in afterwards (Steam's last two weeks, or a total that went up), not timed by us
    }

    /// <summary>A game on the Store wishlist (price when added, to show drops).</summary>
    public class WishItem
    {
        public string ItadId;
        public string SteamAppId;
        public string Name;
        public string Image;
        public string Link;
        public decimal AddedPrice = -1;
        public string Currency;
        public System.DateTime Added;
        public string Store;           // Steam / GOG / Epic / "" (recently viewed: to open it the same way)
        public string Id;
        public string PriceText;       // recently viewed: the price when you looked
        public decimal TargetPrice = -1;   // price alert: tell me when it's this or less (-1: any drop below AddedPrice)
        public string TargetCurrency;
        public string ImportedFrom;    // "steam" / "gog": brought in from that store's wishlist (it stays there if you take it off here)
        public bool OnSteam;           // added here and since put on your Steam wishlist too (Add games I wishlist here to my Steam)
        public int SteamTries;         // times Steam said no to it (given up after 3)
        public string GogId;
        public System.DateTime? ReleaseDate;   // when it comes out (for the releases calendar)
        public string ReleaseText;     // "Q1 2027", "Coming soon"... when there's no exact date
        public bool ComingSoon;        // not out yet
        public System.DateTime ReleaseCheckedAt;
    }

    /// <summary>One top bar shortcut. The same list drives the top bar and the Shortcuts options page.</summary>
    public class ShortcutItem : Observable
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public ICommand Command { get; set; }
        public ICommand MoveCommand { get; set; }
        public bool IsButton { get { return Key != "battery" && Key != "clock" && Key != "options"; } }
        public bool IsOptions { get { return Key == "options"; } }
        public bool IsBattery { get { return Key == "battery"; } }
        public bool IsClock { get { return Key == "clock"; } }
        private System.Windows.Media.ImageSource picture;   // (1.0.1) a streaming service's own icon (from its app or website), over the line icon
        public System.Windows.Media.ImageSource Picture { get { return picture; } set { if (Set(ref picture, value, "Picture")) Notify("HasPicture"); } }
        public bool HasPicture { get { return picture != null; } }

        internal System.Action<ShortcutItem> PinChanged;

        private bool isPinned;
        public bool IsPinned
        {
            get { return isPinned; }
            set { if (Set(ref isPinned, value, "IsPinned") && PinChanged != null) PinChanged(this); }
        }
        internal void SetPinnedSilently(bool v) { isPinned = v; Notify("IsPinned"); }

        private bool isShown;
        public bool IsShown { get { return isShown; } set { Set(ref isShown, value, "IsShown"); } }

        private bool isMoving;
        public bool IsMoving { get { return isMoving; } set { Set(ref isMoving, value, "IsMoving"); } }

        /// <summary>Top bar settings page: stays visible while the bar auto-hides.</summary>
        internal System.Action<ShortcutItem> StayChanged;
        private bool stayVisible;
        public bool StayVisible
        {
            get { return stayVisible; }
            set { if (Set(ref stayVisible, value, "StayVisible") && StayChanged != null) StayChanged(this); }
        }
        internal void SetStaySilently(bool v) { stayVisible = v; Notify("StayVisible"); }

        // Little status dot (Discord) and count badge (Discord unread, downloads)
        private bool showDot;
        public bool ShowDot { get { return showDot; } set { Set(ref showDot, value, "ShowDot"); } }

        private Brush dotBrush;
        public Brush DotBrush { get { return dotBrush; } set { Set(ref dotBrush, value, "DotBrush"); } }

        private bool showBadge;
        public bool ShowBadge { get { return showBadge; } set { Set(ref showBadge, value, "ShowBadge"); } }

        private string badgeText;
        public string BadgeText { get { return badgeText; } set { Set(ref badgeText, value, "BadgeText"); } }

        private Brush badgeBrush;
        public Brush BadgeBrush { get { return badgeBrush; } set { Set(ref badgeBrush, value, "BadgeBrush"); } }

        private Brush badgeTextBrush;
        public Brush BadgeTextBrush { get { return badgeTextBrush; } set { Set(ref badgeTextBrush, value, "BadgeTextBrush"); } }
    }

    public class DownloadItem : Observable
    {
        public string Key { get; set; }

        private string name;
        public string Name { get { return name; } set { Set(ref name, value, "Name"); } }

        private string status;
        public string Status { get { return status; } set { Set(ref status, value, "Status"); } }

        private bool hasProgress;
        public bool HasProgress { get { return hasProgress; } set { Set(ref hasProgress, value, "HasProgress"); } }

        private double barWidth;
        public double BarWidth { get { return barWidth; } set { Set(ref barWidth, value, "BarWidth"); } }

        /// <summary>Opens the launcher it's downloading in (Steam's downloads, GOG Galaxy's page for the game...).</summary>
        public ICommand Command { get; set; }
        public string Source { get; set; }
    }

    public class VoiceItem
    {
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public bool Speaking { get; set; }
        public string StateText { get; set; }
    }

    public class AudioItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public bool IsInput { get; set; }   // a microphone (the row shows the mic icon)
        public ICommand SelectCommand { get; set; }
    }

    /// <summary>A Discord chat, server, channel or quick reply row: one shape for every list in the Discord panel.</summary>
    public class DiscordRow
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string IconUrl { get; set; }
        public string Badge { get; set; }
        public bool HasBadge { get { return !string.IsNullOrEmpty(Badge); } }
        public bool HasSubtitle { get { return !string.IsNullOrEmpty(Subtitle); } }
        public bool HasIcon { get { return !string.IsNullOrEmpty(IconUrl); } }
        public bool IsVoice { get; set; }
        public ICommand Command { get; set; }
    }

    public class MessageItem
    {
        public string Author { get; set; }
        public string Content { get; set; }
        public string TimeText { get; set; }
        public string AvatarUrl { get; set; }
        public bool HasAvatar { get { return !string.IsNullOrEmpty(AvatarUrl); } }
    }
}
