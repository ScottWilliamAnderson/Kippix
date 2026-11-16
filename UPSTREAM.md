# Changes in this fork, ready to propose upstream

Every change is one branch from `upstream/main` (Kipping0/Kippix at `a1f2326`), so each can be opened as its own pull request. `scott` is the integration branch (all of them merged) and is what runs on the device. Nothing has been proposed upstream yet.

"Verified" means it was run on a ROG Xbox Ally X (1920x1080, Playnite 10.64 fullscreen) and checked on screen. "Compiled" means it builds and is merged, and was not looked at on screen after its last commit.

## Suggested order

Start with the small bug fixes: they are short, self-contained and easy to review. Features next, smallest first. Stacked branches go after their base.

### 1. Bug fixes in upstream code

| Branch | What it fixes | State |
|---|---|---|
| `fix/fullscreen-web-dialogs-blank` | Add-on web dialogs (sign-in windows) come up black in fullscreen: `WindowBase` has no template outside the desktop themes. Also gives an open dialog a pointer and clicks. | Verified (HowLongToBeat sign-in done through it) |
| `fix/mouse-pointer-in-dialogs` | With the pointer parked off screen it stayed hidden in every other Playnite window. | Verified |
| `fix/top-bar-band-haze` | The band behind the top bar faded to transparent white, leaving a pale strip under the bar. | Verified (measured before and after) |
| `fix/wishlist-prices-unmatched` | Wishlist prices stuck on "getting them" beyond the first 25 games: bulk IsThereAnyDeal lookup. | Verified (145 of 155 priced) |
| `fix/profile-picture-after-restart` | Profile picture gone after restarting Playnite. | Verified |
| `fix/music-off-silences-playnite` | Music set to Off still let Playnite's own music play. | Verified |
| `fix/search-filter-indicator-and-clear` | "Show in my games" left a hidden name filter; B clears it, the preset keeps its name and is chosen again. | Verified |
| `fix/accent-after-rest` | Accent recolouring ran on every move (about 43 ms each); now when the highlight rests. | Verified (timed) |
| `fix/tour-stores-list-scroll` | Tour lists longer than the screen scrolled to the bottom on every move. | Verified |
| `fix/tour-last-preset-dpad` | Tour: D-pad could not reach Next from the presets. | Verified |
| `fix/tour-grid-board-preset-pictures` | Tour presets kept the other layout's pictures after Grid <-> Board. | Verified |
| `fix/tour-picture-row-refresh` | Tour "Choose a picture" row did not show the picture just chosen. | Verified (a newly chosen picture showed on the row at once) |
| `fix/tour-steam-signin-row-refresh` | Tour stores step did not follow a Steam store sign-in. | Not verified (the row shows "Signed in" correctly; the refresh right after signing in needs a sign-out and sign-in) |
| `fix/tour-store-no-games-wording` | "0 games · couldn't tell" for an add-on with no games. | Verified |
| `fix/browser-close-focus` | Closing a page opened from a menu put the highlight on a hidden button. | Verified |
| `fix/back-highlight-far-card` | Back from the game page after LB / RB: no card highlighted when the game was far along the row. | Verified |
| `fix/browser-one-name` | Two names for the built-in browser. | Verified |
| `fix/keyboard-keycaps-legible` | Keyboard key caps in the hints were squeezed and unreadable. | Verified |
| `fix/top-bar-kept-items-legible` | The clock that stays when the bar hides was unreadable over bright art. | Verified |
| `fix/more-below-hint-late` | "More below" hint missing when the lower page fills in late. | Partly verified (the hint matched the lower page on 7 game pages; the late-filling case itself was not isolated) |
| `fix/row-recently-added-name` | The recently added row was listed as "Recently viewed". | Verified |
| `fix/game-part-names-case` | Lower-page part names listed in capitals. | Verified |
| `fix/touch-found-space` | Missing space in "A touch screen was found.Tap to choose". | Verified |
| `fix/story-progress-hide-empty` | "Story ~?%" shown for apps and games with nothing to go by. | Verified |
| `fix/story-progress-relevance` (on `fix/story-progress-hide-empty`) | "Story ~99%" from play time on multiplayer-only games and on games played past their story length. | Verified |
| `fix/game-page-info-layout` | Game page info lines overlapped the logo and buttons. | Verified |
| `fix/game-page-lower-gap` (on `fix/game-page-info-layout`) | Lower-page cards touched at larger text sizes. | Verified (two-card pages) |
| `fix/game-page-long-name-hidden` (on `fix/game-page-info-layout`) | A long name without a logo ran behind the cover / trailer and was cut off; it wraps to two lines before it. | Verified |

### 2. Features

| Branch | What it adds | State |
|---|---|---|
| `feat/cycle-next-hint` | Settings rows that cycle on A show "current › next" (37 rows). | Verified |
| `feat/tour-preview-hint` | A visible "X: preview full screen" hint in the tour. | Verified |
| `feat/game-page-hints` | Game page hint strip that follows the highlight: "A <what it does>", B, LB/RB, X. | Verified |
| `feat/device-battery` | Top-bar battery can show the device's battery (handhelds), with a charging mark. | Verified |
| `feat/sort-and-show` | A top-bar shortcut: sort order and installed-only, without Playnite's filter panel. | Verified |
| `feat/browser-keyboard-links` | The on-screen keyboard comes up when a text box is picked in the built-in browser; "Open links in" setting. | Verified |
| `feat/shuffle-covers` | Tiles rotate between a game's own cover and its BackgroundChanger covers, per start, day, hour or ten minutes. | Verified (230 games rotating) |
| `feat/home-video-background` | Muted gameplay clip behind the home screen: local trailer, ES-DE clip, or Steam trailer. An option lets it play on battery (handheld mode pauses it otherwise). `HomeVideoSkipSeconds` (no row in the settings yet) starts clips a few seconds in, past the ratings card. | Verified |
| `feat/home-video-pip` (on `feat/home-video-background`) | The clip as a picture-in-picture box; plays on the landing only. | Verified |
| `feat/running-row` | Several apps and games at once: a "Running now" row, switch with A, close with X, RUNNING badge, "Switch to" on the game page. | Verified |
| `feat/running-close-confirm` (on `feat/running-row`) | X asks "Close <name>?" first; apps only by default. | Verified |
| `feat/apps-row` | Apps (a category) in their own row under the games; an app's page says Open and drops game-only parts. | Verified |
| `feat/game-edit` | "Edit this game" in fullscreen (name, favourite, hidden, categories, genres, platforms, tags, metadata download) and "Add an app or game". | Verified |
| `feat/justified-library` | "Mosaic": every cover in its own shape in justified rows; as a home layout and as a library view opened with Down from the Row. Includes the header-overlap fix (`fix/library-header-overlap`). | Verified (press-to-frame 173 ms -> 29 ms after the performance pass) |
| `feat/guides-claude-code` | Achievement guides: Claude Code as a third provider (the user's own Claude plan, no API key), with web search. | Verified (two games written on the device) |
| `feat/one-tile-per-game` | Stacked on `feat/sort-and-show`. A switch (off by default): a game that is in the library more than once (two stores, an old and a new package, a console copy and a PC one) shows as one tile, the installed copy or else the one played last. Play time and last played are those of all copies together; the game's More menu switches copy or gives each its own tile again. Nothing in the library is changed. | Verified (9 groups in a 377-game library; switch copy, choose automatically, split and re-join, a filter that hides the best copy, on and off) |
| `feat/sort-shuffle` | Stacked on `feat/sort-and-show`. "Shuffle" under SORT BY: the games in a random order among whatever the filter shows; a new order when chosen again and each time Playnite starts. Kept-in-place games and a folder's own order still apply. | Verified (on, again, after a restart, and back to the earlier sort) |

## Notes for whoever opens the pull requests

- **Merge conflicts between branches:** almost all are at the end of `Localization/en_US.xaml` (both sides append strings: keep both). `feat/sort-and-show` and `feat/apps-row` also meet in `ThemeApi.Profiles.cs` and `ThemeApi.cs`: merge sort first, keep both conditions. `feat/one-tile-per-game` and `feat/sort-shuffle` both add settings after `InstalledOnly` in `Models.cs` (keep both), and one-tile meets `feat/apps-row` and `feat/game-edit` in the same places as sort-and-show does.
- **Glue that exists only on `scott`:** the Mosaic tiles follow the shuffled cover's shape (`CoverSizes.PickOf`), the home video stops while the library view is open, apps stay listed in the library view, and two cycle hints for rows that come from other branches. Each is a few lines and is needed only when both branches are in.
- **One branch is large:** `feat/justified-library` (15 commits, 28 files). It is split into commits by concern (panel, cover sizes, layout, library view, performance) and could be proposed as a series.
- **Style:** the helper is C# 5 (csc.exe); strings are in `en_US.xaml` only, other languages fall back to English; comments follow the upstream author's style.
- **Not for upstream:** `Setups/` (one person's setup) and this file.
- **Tracker:** issues #1 to #22 in the fork describe the original problems and requests.
- **On screen:** `Showcase/README.md` has a clip of each larger change.
