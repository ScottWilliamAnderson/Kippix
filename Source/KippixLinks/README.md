# Links

A small Playnite add-on that keeps a list of web pages and opens them full screen in your browser from Playnite's
fullscreen mode.

It works alongside the Kippix theme: it adds a **Links** button to Kippix's top bar. Without Kippix, open it from
Playnite's main menu (Extensions › Links).

## Using it

1. In your browser, copy the address of the page you want.
2. In Playnite, open Links and choose **Add copied link**.
3. Pick a link to open it. The list remembers the links you add, most recently opened first.

Controller: A opens, X forgets a link (at once: there is no undo), Y renames it, B goes back, Guide closes the panel.
Keyboard: Enter, Delete, F2, Esc.

Rename uses a small keyboard inside the Links panel: type on it with the controller, or on a real keyboard (Enter is
Done, Esc cancels).

## Notes

- The panel is a separate window, so while it is open Kippix's music and home video pause, as they do for Playnite's
  own dialogs. It closes when another window is activated.
- Without Kippix (or in Desktop mode) a link opens in the default browser's own window (full screen for Chromium
  browsers). There is no controller control of the page then: close it with the keyboard or mouse.
- The list is stored in the add-on's own data folder, for your Windows user only.

## Building

Needs only Windows (the .NET Framework C# compiler) and an installed Playnite (for `Playnite.SDK.dll`).

    powershell -ExecutionPolicy Bypass -File build.ps1             # build\KippixLinks.dll
    powershell -ExecutionPolicy Bypass -File build.ps1 -Package    # also KippixLinks-<version>.pext
    powershell -ExecutionPolicy Bypass -File build.ps1 -Install    # copy into Playnite (close Playnite first)

Tests (offline):

    powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1

## Installing

Either run `build.ps1 -Install` with Playnite closed, or open the `.pext` file with Playnite. Restart Playnite afterwards.
