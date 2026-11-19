<p align="center">
  <img src="docs/images/kippix-logo.png" alt="Kippix" width="360"><br>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/Kipping0/Kippix/total?label=downloads&color=8a2be2" alt="Total downloads"></a>
</p>

<p align="center">
  <b>A console style Fullscreen theme for <a href="https://playnite.link">Playnite</a>, made to be played from the sofa.</b><br>
  Controller first, highly customisable, with its own in-game overlay.
</p>

> [!CAUTION]
> **This is a WIP Fullscreen, please bear with me while we fix any bugs/issues in this new release**

<p align="center">
  <a href="../../releases/latest"><b>Download</b></a> ·
  <a href="docs/GUIDE.md">User guide</a> ·
  <a href="docs/SETTINGS.md">Settings</a> ·
  <a href="docs/OVERLAY.md">In-game overlay</a> ·
  <a href="docs/FAQ.md">FAQ</a>
</p>

---

<!-- Screenshots: they live in docs/images. -->
<p align="center">
  <img src="docs/images/kippix-layouts.gif" alt="Kippix's home screen layouts and presets" width="98%">
</p>
<p align="center">
  <img src="docs/images/home-row-default.jpg" alt="Home screen, Row layout (Lower, default preset)" width="98%">
</p>
<p align="center">
  <img src="docs/images/home-row.jpg" alt="Home screen, Row layout" width="49%">
  <img src="docs/images/game-page.jpg" alt="A game page" width="49%">
</p>
<p align="center">
  <img src="docs/images/home-collage.jpg" alt="Home screen, Manual board with the Collage preset" width="49%">
  <img src="docs/images/overlay.jpg" alt="The in-game overlay" width="49%">
</p>

## What is Kippix?

Kippix replaces Playnite's fullscreen mode with a TV and handheld friendly launcher for all your games, Steam, Epic, GOG,
Xbox, emulators and anything else Playnite knows about. It comes in two parts that are installed together:

- **Kippix**, the fullscreen theme
- **Kippix Helper**, the add-on that powers it (also includes the in-game overlay).

## Highlights

**Your home screen, your way**
- Free move, you can move anything on the home screen, delete anything, add in extra tiles, rows, reshape tiles etc
- Six default layouts: **Row**, **Grid**, **List**, **Board**, **Manual board** (place
  every tile yourself) and **Folders** (work in progress).
- Ready-made presets for every layout
- Widgets (weather, clock, PC stats, friends online, wishlist deals, free games this week, latest capture and more),
  extra rows (favourites, recently added, installed, never played…) and home tiles.
- Backgrounds from the game's art, a muted trailer, a slideshow, your own picture or a plain colour, with zoom, depth and
  seasonal or live-weather effects.
- Community layouts, download and apply custom layouts created from the community

**Game pages**
- Logo, play time, achievements, *How long to beat* times, an estimate of your story progress, trailers, friends who play
  it, Steam news, game updates, mods, your captures, save backups and "more like this in your library".
- Every part can be moved, resized or switched off. Choose from 10 entrance animations (or none) **(Animations are a work in progress, some are disabled while they are being worked on)**.

**Built for controllers**
- Xbox, PlayStation and Switch button icons (or matching whatever you're holding), Steam Controller and Steam Deck
  support, remappable buttons, touch and mouse support, and a handheld mode for small screens.

**In-game overlay**
- Press the Guide / PS button in a game: achievements with guides, screenshots and clips, sound output and volume,
  Discord chat and voice, HDR on / off, Bluetooth, PC stats, downloads, and power options. [More about the overlay →](docs/OVERLAY.md)

**And LOTS more**
- A built-in **Store** (Steam, GOG and Epic's free games, with prices across official stores from IsThereAnyDeal), wishlist
  import and price alerts.
- A **Gallery** for your screenshots and clips, with crop, filters and video trimming.
- **Save backups** and restores with Ludusavi, **profiles** with optional daily time limits and a grown-ups' PIN, a
  **Community** gallery for sharing layouts, looks and widgets, and a getting-started tour that sets it all up.
- **Discord** integrated in both the Fullscreen and overlay.

## Requirements

- Windows 10 or 11
- [Playnite](https://playnite.link) **10 or newer** (installed or portable)
- A controller is recommended, but keyboard, mouse and touch all work **(Some Keyboard/Mouse/touch QOL changes are not yet implemented, for full support, please use a controller or try your luck with keyboard and mouse, any issues please open an issue thread)**

Everything else is optional. Kippix works on its own, and picks up extra features from other Playnite add-ons if you have
them (see [Works with](#works-with)).

## Install

1. Download **both** files from the [latest release](../../releases/latest):
   - `Kippix…pthm` (the theme)
   - `KippixHelper…pext` (the add-on)
2. With Playnite open, double-click each file (or drag it onto the Playnite window) and confirm the install.
3. Restart Playnite.
4. Switch to fullscreen mode, open Playnite's menu (Back / View button), go to **Settings › Visuals › Theme**, pick
   **Kippix** and restart when asked.
5. The **Getting started** tour runs the first time and helps you choose your layout, style, game page and stores.

Step-by-step instructions with pictures, updating and uninstalling: [User guide › Install](docs/GUIDE.md#1-install).

> The theme and the add-on always go together. Install both from the **same** release, or parts of Kippix won't work.

## Works with

All optional. Kippix lists the ones you have in **Settings › General settings › Other add-ons**.

| Add-on / program | What it adds in Kippix |
|---|---|
| SuccessStory *or* Playnite Achievements | Achievements on cards, game pages, your profile and the overlay; achievement pop-ups in games |
| HowLongToBeat | *How long to beat* times on game pages, cards and suggestions |
| Ludusavi | Save backups and restores from the game page, automatic backups around play sessions |
| Extra Metadata Loader | Logos and trailers |
| BackgroundChanger | More backgrounds, covers and videos per game |
| PlayerActivities | Friends from Steam, GOG, Epic and EA on game pages |
| UniPlaySong **(WIP)** | Music per game |
| Game Pass Catalog Browser **(WIP)** | A Game Pass row in the Store |
| Nexus Mods checker, Vortex, Mod Organizer 2 | The Mods card and Nexus button on game pages |
| NVIDIA app (Instant Replay) or Xbox Game Bar | "Save recent gameplay" clips from the overlay |

## Privacy

Kippix doesn't have accounts, ads or tracking, and nothing is sent to the author. It talks to public services only for
what you see on screen, store data and prices (Steam, GOG, Epic, IsThereAnyDeal, CheapShark), weather (Open-Meteo), and
this GitHub repository (update checks and the Community gallery). Keys you add (Steam Web API, Discord, AI guides) stay on
your PC and only go to the service they're for. Details: [FAQ › Privacy](docs/FAQ.md#privacy-and-data).

## Help and feedback

- Something not right? Run **Settings › Help › Kippix Doctor**, or see the [FAQ](docs/FAQ.md) and
  [Troubleshooting](docs/GUIDE.md#12-troubleshooting).
- Found a bug? [Open an issue](../../issues/new/choose) and attach a bug report (Settings › Help › Save a log).

## Credits

Made by **Kipping0**. Kippix is a fan project and isn't affiliated with Playnite, Valve, Microsoft, Sony, Nintendo,
Discord, NVIDIA or any store. Prices are from [IsThereAnyDeal](https://isthereanydeal.com); weather data by
[Open-Meteo](https://open-meteo.com) (CC BY 4.0). Game names, art and trademarks belong to their owners.

## Disclosure

Kippix was created with assistance from AI, future updates will also have AI to help, if you do not like AI being involved with creation of this theme or its helper then please do not use Kippix.

## Licence

MIT. See [LICENSE](LICENSE).
