using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>
    /// Controller buttons, caught where they start. Playnite sends A, B, X, Y, LB, RB, Start, View and Guide only as KeyDown / KeyUp
    /// to the highlighted element (never on the way down, like keyboard keys), and its own bindings on the way up (A opens a game,
    /// B closes the game page) would act first. So the few things that must come before those are handled here:
    ///   - hold A (on the games or the game page): Free move for that screen; a short press does what A always does;
    ///   - hold B: home (HoldHome.cs);
    ///   - B on the game page's lower half goes back up; B in the description reader closes it;
    ///   - Guide (option): straight home, unless a game is running.
    /// </summary>
    public partial class ThemeApi
    {
        private KeyEventArgs lastPadEvent;
        private KeyEventArgs padHandled;      // a press dealt with here: the window's own handler leaves it alone
        private KeyEventArgs replayingA;
        private ControllerInput comboFirst = ControllerInput.None;
        private DispatcherTimer comboTimer;
        private IInputElement comboFocus;
        private Type comboArgsType;
        private DispatcherTimer holdATimer;
        private bool holdAWaiting;
        private ControllerInput holdAButton = ControllerInput.A;   // the button being held for Free edit (Controller buttons can move it)
        private IInputElement holdAFocus;
        private bool freeFromHold;            // Free move started by holding A: ending it goes back to the screen, not the settings
        private IInputElement freeHoldFocus;

        public bool HoldAFreeMove
        {
            get { return settings.HoldAFreeMove; }
            set { if (settings.HoldAFreeMove == value) return; settings.HoldAFreeMove = value; SaveSettings(); Notify("HoldAFreeMove"); }
        }
        public bool GuideGoesHome
        {
            get { return settings.GuideGoesHome; }
            set { if (settings.GuideGoesHome == value) return; settings.GuideGoesHome = value; SaveSettings(); Notify("GuideGoesHome"); }
        }
        /// <summary>The switch's name follows the chosen button.</summary>
        public string HomeButtonTitle { get { return settings.HomeButton == 1 ? Loc("LOC_KX_SelectStartHome", "Select + Start go home") : Loc("LOC_KX_GuideHome", "Guide button goes home"); } }
        public string HomeButtonText { get { return settings.HomeButton == 1 ? Loc("LOC_KX_ButtonSelectStart", "Select + Start") : Loc("LOC_KX_ButtonGuide", "Guide"); } }
        public ICommand CycleHomeButtonCommand { get; private set; }

        private void InitPadInput()
        {
            CycleHomeButtonCommand = new RelayCommand(_ =>
            {
                settings.HomeButton = 1 - settings.HomeButton;
                SaveSettings();
                PlaySound("move");
                Notify("HomeButtonTitle");
                Notify("HomeButtonText");
            });
            InitSteamPad();
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.KeyDownEvent, new KeyEventHandler(OnPadDownEarly), true);
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.KeyUpEvent, new KeyEventHandler(OnPadUpEarly), true);
        }

        private static ControllerInput PadButton(KeyEventArgs e)
        {
            if (e.GetType().Name != "GameControllerInputEventArgs") return ControllerInput.None;
            var b = e.GetType().GetProperty("Button");
            object v = b != null ? b.GetValue(e, null) : null;
            return v is ControllerInput ? (ControllerInput)v : ControllerInput.None;
        }

        private static bool InMainWindow(object sender)
        {
            var d = sender as DependencyObject;
            return d != null && Application.Current != null && ReferenceEquals(Window.GetWindow(d), Application.Current.MainWindow);
        }

        private void OnPadDownEarly(object sender, KeyEventArgs e)
        {
            try
            {
                var button = PadButton(e);
                if (button == ControllerInput.None || ReferenceEquals(e, lastPadEvent) || ReferenceEquals(e, replayingA)) return;
                lastPadEvent = e;
                if (padEventType == null) padEventType = e.GetType();
                if (!lookFullscreen || !InMainWindow(sender)) return;
                if (justPending != null) JustCommitUnlessMove(e);   // the mosaic: anything but a move acts on the highlighted game

                // Controller buttons: waiting for "press a button" takes every button
                if (PadCaptureKey(e, button, true)) return;
                if (openPanel == "welcome" && TourPadKey(e, button)) return;   // the tour's controller check: every button is being tested
                if (bgMoveOn) { e.Handled = true; padHandled = e; BgMovePad(button, e); return; }
                if (splashEditOn) { e.Handled = true; padHandled = e; SplashEditPad(button, e); return; }
                if (lpOpen) { e.Handled = true; padHandled = e; LocalPlayerPad(button, e); return; }   // (1.0.1: Shows & movies' player: ThemeApi.LocalPlayer.cs)
                if (guideBarOpen) { e.Handled = true; padHandled = e; GuideBarPad(button, e); return; }   // (0.99.46) the quick bar takes every button (ThemeApi.GuideBar.cs)
                if (galleryEditOpen) { e.Handled = true; padHandled = e; GalleryEditPad(button, e); return; }   // (0.99.59) the gallery's Edit (ThemeApi.GalleryEdit.cs)
                TipsAnyKey();   // a first-time tip goes when anything is pressed (the press still counts)
                bool raised = ReferenceEquals(e, remapRaised);
                bool pressed = !IsPadRelease(e);
                if (button == ControllerInput.Start && !pressed) startLatched = false;   // (0.99.50: Start let go)
                // hold for home: read from the button itself (B unless it was moved), before anything is translated
                if (!raised && button == HoldHomeButton) { if (pressed) StartHoldBack(false); else StopHoldBack(); }
                if (kbOpen) { padHandled = e; KeyboardKey(e); return; }   // A still presses the highlighted key
                // an action moved onto this button (or away from it): sent on as the button it stands for here, or nothing
                if (!raised && RemapDown(e, button)) return;

                // (0.99.15) moving on the home screen: the background change speed goes on again first (another add-on resets it)
                if (pressed && openPanel == null && (button == ControllerInput.DPadUp || button == ControllerInput.DPadDown || button == ControllerInput.DPadLeft || button == ControllerInput.DPadRight
                    || button == ControllerInput.LeftStickUp || button == ControllerInput.LeftStickDown || button == ControllerInput.LeftStickLeft || button == ControllerInput.LeftStickRight)) ApplyBgChangeDelay(false);

                if (openPanel == null && TilesReleaseKey(e)) { e.Handled = true; padHandled = e; return; }   // (0.99.18) the let-go of the Left that opened the tiles

                // (0.99.13) Up / Down in Getting started's lists and the profile menu's reordering, from the controller too
                if (!pressed && !confirmOpen && (openPanel == "welcome" || openPanel == "profile")
                    && (button == ControllerInput.DPadUp || button == ControllerInput.LeftStickUp || button == ControllerInput.DPadDown || button == ControllerInput.LeftStickDown)
                    && (openPanel == "welcome" ? TourNavOurs(Keyboard.FocusedElement as FrameworkElement) : ProfileReorderMoving))
                { e.Handled = true; padHandled = e; return; }   // (0.99.14) the let-go: nothing moves
                if (pressed && !confirmOpen && !ReferenceEquals(e, navDoneEvent) && (openPanel == "welcome" || openPanel == "profile"))
                {
                    int ud = button == ControllerInput.DPadUp || button == ControllerInput.LeftStickUp ? -1
                           : button == ControllerInput.DPadDown || button == ControllerInput.LeftStickDown ? 1 : 0;
                    if (ud != 0)
                    {
                        var nfe = Keyboard.FocusedElement as FrameworkElement;
                        bool ours = openPanel == "welcome" ? TourNavOurs(nfe) : ProfileReorderMoving;
                        if (ours && NavIsEcho(ud)) { e.Handled = true; padHandled = e; navDoneEvent = e; return; }   // (0.99.14) a copy of the press just acted on
                        bool done = openPanel == "welcome" ? (TourListStep(nfe, ud) || TourTileStep(nfe, ud)) : ProfileMoveKey(ud);
                        if (done) { e.Handled = true; padHandled = e; navDoneEvent = e; NavMoved(ud); return; }
                    }
                }

                // the Board: X / Y / B while editing it (and X keeps Playnite's selected game in step)
                if (!freeMove && !IsMasterMoving && BoardPadKey(button, e)) { e.Handled = true; padHandled = e; return; }

                // Folder: B in an opened folder goes back to the folder tiles, wherever the highlight is on the home screen
                // (the controller's B only arrives here, never on the way down where Esc is caught)
                if (button == ControllerInput.B && openPanel == null && !detailsOpen && LibraryBackKey(e)) { e.Handled = true; padHandled = e; return; }   // the library view: B, back to the home screen
                if (button == ControllerInput.B && openPanel == null && !detailsOpen && FolderBackKey(e)) { e.Handled = true; padHandled = e; return; }
                // (0.99.63) B on the home screen otherwise: the first game / the top bar (Settings › Controller buttons)
                if (button == ControllerInput.B && openPanel == null && !detailsOpen && HomeBackKey(e)) { e.Handled = true; padHandled = e; return; }
                // Folder: LB / RB and the sticks' clicks don't switch Playnite's saved filters (a folder shows exactly its own games)
                if (openPanel == null && !detailsOpen && FolderFilterKey(e)) { e.Handled = true; padHandled = e; return; }

                // Y on the home screen: the theme's own search (not Playnite's)
                if (button == ControllerInput.Y && openPanel == null && !detailsOpen && !confirmOpen && !freeMove && !IsMasterMoving)
                {
                    e.Handled = true;
                    padHandled = e;
                    OpenSearch();
                    return;
                }

                // Y in the store / achievements: their search (caught here, before Playnite's own Y)
                if (button == ControllerInput.Y && openPanel != null && !confirmOpen && !freeMove && YSearch())
                {
                    e.Handled = true;
                    padHandled = e;
                    return;
                }

                // Select (View) + Start together: home (when that's the chosen button). Each waits a moment for the other.
                if ((button == ControllerInput.Back || button == ControllerInput.Start) && settings.GuideGoesHome && settings.HomeButton == 1 && !gameRunningNow && openPanel != "browser")
                {
                    e.Handled = true;
                    padHandled = e;
                    if (comboFirst != ControllerInput.None && comboFirst != button)
                    {
                        comboTimer.Stop();
                        comboFirst = ControllerInput.None;
                        HoldBackHome();
                        return;
                    }
                    if (comboFirst == button)
                    {
                        // the same button again while waiting: the first press happens now, this one waits in turn
                        comboTimer.Stop();
                        ReplayButton(comboArgsType, comboFirst, null);
                    }
                    comboFirst = button;
                    comboFocus = Keyboard.FocusedElement;
                    comboArgsType = e.GetType();
                    if (comboTimer == null)
                    {
                        comboTimer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(280) };
                        comboTimer.Tick += (s, a) =>
                        {
                            comboTimer.Stop();
                            var b = comboFirst;
                            comboFirst = ControllerInput.None;
                            if (b != ControllerInput.None) ReplayButton(comboArgsType, b, null);   // only one of them: it does what it always does (where the highlight is now)
                        };
                    }
                    comboTimer.Stop();
                    comboTimer.Start();
                    return;
                }

                // (0.99.46) the Guide button: the quick bar (on the home screen, or anywhere when the Guide button doesn't go home)
                if (button == ControllerInput.Guide && !raised && GuideBarGuide(e)) { e.Handled = true; padHandled = e; return; }
                if (button == ControllerInput.Guide && settings.GuideGoesHome && settings.HomeButton == 0 && !gameRunningNow)
                {
                    e.Handled = true;
                    padHandled = e;
                    HoldBackHome();   // closes everything that's open; already home: stays put
                    return;
                }
                if (descReaderOpen) { padHandled = e; DescReaderKey(e); e.Handled = true; return; }
                if (detailsOpen && openPanel == null && !confirmOpen && GamePageKey(e)) { padHandled = e; e.Handled = true; return; }

                // hold X on a card: kept in that spot (a tap still opens Playnite's game menu)
                if (button == ControllerInput.X && !e.Handled && HoldXDown(e)) return;
                // (already handled lower down, e.g. a game started straight from its card: not held, not played again)
                // (hold for Free edit: A unless it was moved; only a real press is held, not one sent on by touch or a moved button)
                if (button == HoldEditButton && !raised && !e.Handled && HoldAApplies())
                {
                    // wait to see if it's held: a short press is played back when A is let go
                    e.Handled = true;
                    padHandled = e;
                    holdAWaiting = true;
                    holdAButton = button;
                    holdAFocus = Keyboard.FocusedElement;
                    if (holdATimer == null)
                    {
                        holdATimer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(900) };
                        holdATimer.Tick += (s, a) => { holdATimer.Stop(); if (holdAWaiting && HoldAApplies()) { holdAWaiting = false; StartFreeMoveFromHold(); } };
                    }
                    holdATimer.Stop();
                    holdATimer.Start();
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: controller button: " + ex.Message); }
        }

        private void OnPadUpEarly(object sender, KeyEventArgs e)
        {
            try
            {
                var button = PadButton(e);
                if (button == ControllerInput.None || ReferenceEquals(e, lastPadEvent) || ReferenceEquals(e, replayingA)) return;
                lastPadEvent = e;
                if (button == ControllerInput.Start) startLatched = false;   // (0.99.50: Start let go)
                // B let go stops hold-B-for-home wherever the release lands: it goes to the highlighted element, which can be one
                // the press just took away (no longer in the window), and a missed release went home 0.8 s after a quick B
                bool raised = ReferenceEquals(e, remapRaised);
                if (!raised && button == HoldHomeButton && IsPadRelease(e)) StopHoldBack();
                if (!lookFullscreen || !InMainWindow(sender)) return;
                if (PadCaptureKey(e, button, false)) return;
                if (openPanel == "welcome" && TourPadKey(e, button)) return;
                if (bgMoveOn) { e.Handled = true; return; }   // (r15: Background › Move the picture takes every button)
                if (splashEditOn || lpOpen) { e.Handled = true; return; }   // (1.0.1)
                if (guideBarOpen) { if (button == ControllerInput.A) GbAUp(); e.Handled = true; return; }   // (0.99.46) the quick bar
                if (galleryEditOpen) { e.Handled = true; return; }   // (0.99.59)
                if (kbOpen) { e.Handled = true; return; }
                if (!raised && RemapUp(e, button)) return;   // the release of a moved button follows its press
                if ((button == ControllerInput.Back || button == ControllerInput.Start) && settings.GuideGoesHome && settings.HomeButton == 1 && !gameRunningNow && openPanel != "browser") { e.Handled = true; return; }   // played back whole
                if (button == ControllerInput.B && descReaderOpen) { DescReaderKey(e); e.Handled = true; return; }
                if (button == ControllerInput.X && HoldXUp(e)) return;
                if (CropPadUp(button)) { e.Handled = true; return; }   // (0.82: LB / RB let go while cropping)
                if (button == holdAButton && holdAWaiting && !raised)
                {
                    holdAWaiting = false;
                    if (holdATimer != null) holdATimer.Stop();
                    e.Handled = true;
                    ReplayA(e);
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: controller button: " + ex.Message); }
        }

        /// <summary>Hold A works on the games (home screen) and on the game page, with nothing open over them.</summary>
        private bool HoldAApplies()
        {
            if (GalleryHoldApplies()) return true;   // (0.99.54) the gallery: hold A on a photo or video to pick several
            if (openPanel == null && !confirmOpen && !freeMove && !IsMasterMoving && !descReaderOpen && !detailsOpen && HeldPinTile() != null) return true;   // a pinned game: hold A offers Unpin (ThemeApi.PinHold.cs; as its More menu does)
            if (SettingsLockedNow) return false;   // Profiles: a profile whose settings are locked can't edit by holding A
            if (openPanel == null && !confirmOpen && !freeMove && !IsMasterMoving && !descReaderOpen && !detailsOpen && IsBoardMode && BoardHasFocus()) return !boardEditing;   // the Board: hold A on a card to edit the board
            if (!settings.HoldAFreeMove || openPanel != null || confirmOpen || freeMove || IsMasterMoving || descReaderOpen) return false;
            var f = Keyboard.FocusedElement as DependencyObject;
            if (f == null) return false;
            var win = Application.Current.MainWindow;
            if (detailsOpen)
            {
                var details = UiTools.FindByName(win, "PART_ElemGameDetails");
                return details != null && details.IsAncestorOf(f);
            }
            var list = UiTools.FindByName(win, "PART_ListGameItems");
            return list != null && list.IsAncestorOf(f);
        }

        /// <summary>A was only tapped: send the press again so it does what it always does (open the game, press the button).</summary>
        private void ReplayA(KeyEventArgs release)
        {
            if (holdAButton == ControllerInput.A) { ReplayButton(release.GetType(), ControllerInput.A, holdAFocus); return; }
            // hold for Free edit moved to another button: its tap goes through every handler (ours too), as that button
            var fw = holdAFocus as UIElement;
            if (fw != null && !ReferenceEquals(Keyboard.FocusedElement, fw) && fw.IsVisible) Keyboard.Focus(fw);
            RaisePad(release.GetType(), holdAButton, true, true);
            RaisePad(release.GetType(), holdAButton, false, true);
        }

        /// <summary>Sends a button press (and its release) again, to the element that had the highlight; not caught here a second time.</summary>
        private void ReplayButton(Type t, ControllerInput button, IInputElement focusWas)
        {
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    var fw = focusWas as UIElement;
                    if (fw != null && !ReferenceEquals(Keyboard.FocusedElement, fw) && fw.IsVisible) Keyboard.Focus(fw);
                    var press = Activator.CreateInstance(t, Key.None, ControllerInputState.Pressed, button) as KeyEventArgs;
                    var up = Activator.CreateInstance(t, Key.None, ControllerInputState.Released, button) as KeyEventArgs;
                    if (press == null) return;
                    replayingA = press;
                    InputManager.Current.ProcessInput(press);
                    replayingA = up;
                    if (up != null) InputManager.Current.ProcessInput(up);
                }
                catch (Exception ex) { logger.Warn("Kippix: replaying " + button + ": " + ex.Message); }
                finally { replayingA = null; }
            }), DispatcherPriority.Input);
        }

        private void StartFreeMoveFromHold()
        {
            if (!HoldAApplies()) return;
            if (PinHoldMenu(holdAFocus)) { holdAWaiting = false; return; }   // a pinned game: Unpin / Cancel
            if (openPanel == "gallery") { holdAWaiting = false; GalleryHoldSelect(holdAFocus); return; }   // (0.99.54)
            // (0.99.1) the Board and the Manual board alike: editing it, with the held tile picked up (Start there: precise edit, the pointer)
            if (IsBoardMode && BoardHasFocus())
            {
                // the Board: editing it, with the held card picked up
                holdAWaiting = false;
                var held = (holdAFocus as FrameworkElement) != null ? ((FrameworkElement)holdAFocus).DataContext as BoardTile : null;
                BoardStartEdit(held ?? FocusedBoardTile());
                return;
            }
            var focus = holdAFocus;
            PlaySound("select");
            StartFreeMove(detailsOpen, false);
            freeHoldFocus = focus;
            freeFromHold = true;
        }

        /// <summary>Free move that holding A started has ended: back to the screen it was on, highlight where it was.</summary>
        private bool EndHoldFreeMove()
        {
            if (!freeFromHold) return false;
            freeFromHold = false;
            openPanel = null;
            NotifyPanels();
            NotifyMove();
            UpdateTopBarFade();
            var back = freeHoldFocus as UIElement;
            freeHoldFocus = null;
            ui.BeginInvoke(new Action(() =>
            {
                if (back != null && back.IsVisible) Keyboard.Focus(back);
                else if (detailsOpen) { if (!UiTools.FocusByNameIfVisible("PART_ButtonContext")) UiTools.FocusInside("PART_ElemGameDetails"); }
                else FocusSelectedCard();
            }), DispatcherPriority.ContextIdle);
            return true;
        }
    }
}
