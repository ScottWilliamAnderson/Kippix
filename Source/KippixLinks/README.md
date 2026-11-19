# Links

A small Playnite add-on that keeps a list of web pages and opens them full screen in your browser from Playnite's
fullscreen mode.

It works alongside the Kippix theme: it adds a **Links** button to Kippix's top bar. Without Kippix, open it from
Playnite's main menu (Extensions › Links).

## Using it

1. In your browser, copy the address of the page you want.
2. In Playnite, press the Links button: with a new address copied it shows the list, ready on **Add copied link**.
3. From then on the button opens that page straight away, like one of Kippix's own streaming shortcuts. It always
   opens the latest link: the one you added or opened last.

When the button opens a link it first shows an "Opening" card for a second: press A then for the full list (to pick
an earlier link, rename or forget one), or B to cancel. "Top bar button" at the end of the list switches the button
to always show the list instead.

The button's icon follows the latest link's site: Kippix's own white icon where its theme has one, a letter badge
otherwise. "Button icon" at the end of the list switches it back to the plain link icon.

In the list: A opens, X forgets a link (at once: there is no undo), Y renames it, B goes back, Guide closes the
panel. Keyboard: Enter, Delete, F2, Esc.

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
