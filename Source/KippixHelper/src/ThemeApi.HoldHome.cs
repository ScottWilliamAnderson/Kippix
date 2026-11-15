using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Hold B (or Esc) for a moment on any page: straight back to the home screen, with the selected game highlighted.
    /// A quick press still just goes back one step.
    /// </summary>
    public partial class ThemeApi
    {
        private DispatcherTimer holdBackTimer;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vk);
        private bool holdBackKeyboard;

        private void InitHoldHome()
        {
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnHoldBackDown), true);
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyUpEvent, new KeyEventHandler(OnHoldBackUp), true);
        }

        private void OnHoldBackDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!IsMainWindow(sender) || !lookFullscreen) return;
                bool pad = e.GetType().Name == "GameControllerInputEventArgs";
                if (pad)
                {
                    var b = e.GetType().GetProperty("Button");
                    var st = e.GetType().GetProperty("ButtonState");
                    object v = b != null ? b.GetValue(e, null) : null;
                    object sv = st != null ? st.GetValue(e, null) : null;
                    if (v == null || v.ToString() != HoldHomeButton.ToString()) return;   // (B unless Controller buttons moved it)
                    bool pressed = sv == null || sv.ToString() == "Pressed";
                    if (pressed) StartHoldBack(false); else StopHoldBack();
                    return;
                }
                if (!ReferenceEquals(e, touchKey)) TipsAnyKey();   // (a first-time tip goes with any key)
                if (e.Key == Key.Escape && !e.IsRepeat) StartHoldBack(true);
            }
            catch (Exception) { }
        }

        private void OnHoldBackUp(object sender, KeyEventArgs e)
        {
            try
            {
                if (holdBackKeyboard && e.Key == Key.Escape) StopHoldBack();
                if (descReaderOpen && (e.Key == Key.Escape || e.Key == Key.Back) && (DateTime.Now - descReaderOpenedAt).TotalMilliseconds > 400) CloseDescReader();
            }
            catch (Exception ex) { logger.Warn("Kippix: key up: " + ex.Message); }
        }

        // the XInput pad whose B started the hold (-1: none seen, the controller's let-go event decides alone)
        private int holdBackPad = -1;

        /// <summary>The XInput pad (0–3) showing B down right now, or -1 (none, or no XInput).</summary>
        private int XInputPadHoldingB()
        {
            try
            {
                ushort mask = XInputMask(HoldHomeButton);
                if (mask == 0) return -1;
                for (int i = 0; i < 4; i++)
                {
                    var st = new KippixOverlay.XInput.State();
                    if (KippixOverlay.XInput.GetState(i, ref st) == 0 && (st.Gamepad.wButtons & mask) != 0) return i;
                }
            }
            catch (Exception) { }   // (no xinput1_4.dll)
            return -1;
        }

        private bool XInputPadHolds(int pad)
        {
            try
            {
                var st = new KippixOverlay.XInput.State();
                if (KippixOverlay.XInput.GetState(pad, ref st) != 0) return true;   // (gone: can't tell, the events decide)
                ushort mask = XInputMask(HoldHomeButton);
                return mask == 0 || (st.Gamepad.wButtons & mask) != 0;
            }
            catch (Exception) { return true; }
        }

        /// <summary>The XInput bit for a button (0: not one XInput reports).</summary>
        private static ushort XInputMask(Playnite.SDK.Events.ControllerInput b)
        {
            switch (b)
            {
                case Playnite.SDK.Events.ControllerInput.A: return KippixOverlay.Btn.A;
                case Playnite.SDK.Events.ControllerInput.B: return KippixOverlay.Btn.B;
                case Playnite.SDK.Events.ControllerInput.X: return KippixOverlay.Btn.X;
                case Playnite.SDK.Events.ControllerInput.Y: return KippixOverlay.Btn.Y;
                case Playnite.SDK.Events.ControllerInput.LeftShoulder: return KippixOverlay.Btn.LB;
                case Playnite.SDK.Events.ControllerInput.RightShoulder: return KippixOverlay.Btn.RB;
                case Playnite.SDK.Events.ControllerInput.Back: return KippixOverlay.Btn.Back;
                case Playnite.SDK.Events.ControllerInput.Start: return KippixOverlay.Btn.Start;
                case Playnite.SDK.Events.ControllerInput.LeftStick: return KippixOverlay.Btn.LThumb;
                case Playnite.SDK.Events.ControllerInput.RightStick: return KippixOverlay.Btn.RThumb;
            }
            return 0;
        }

        private void StartHoldBack(bool keyboard)
        {
            if (IsControllerPage) return;   // B is being tested there (only B twice leaves)
            if (TourLocked) return;          // (1.0.1) an unfinished Getting started stays: holding B / Esc doesn't go home
            holdBackKeyboard = keyboard;
            holdBackPad = keyboard ? -1 : XInputPadHoldingB();
            if (holdBackTimer == null)
            {
                holdBackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                holdBackTimer.Tick += (s, e) =>
                {
                    holdBackTimer.Stop();
                    // Esc really still held? (Steam's overlay or the Steam button can take the window away mid-press, and its
                    // let-go never reaches Playnite: that isn't a hold)
                    if (holdBackKeyboard && (GetAsyncKeyState(0x1B) & 0x8000) == 0) return;
                    // the controller's B really still down? (its let-go can be lost: it goes to the highlighted element, and when
                    // the B press itself just took that away (back from Profile tabs while the profile is being drawn) it never
                    // reaches the window: a quick B went back and then home)
                    if (!holdBackKeyboard && holdBackPad >= 0 && !XInputPadHolds(holdBackPad)) { logger.Info("Kippix: hold B: already let go (its release was missed), not going home"); return; }
                    var w = Application.Current != null ? Application.Current.MainWindow : null;
                    if (w != null && !w.IsActive) return;
                    logger.Info("Kippix: hold B / Esc: home");
                    HoldBackHome();
                };
            }
            holdBackTimer.Stop();
            holdBackTimer.Start();
        }

        private void StopHoldBack()
        {
            if (holdBackTimer != null) holdBackTimer.Stop();
        }

        /// <summary>Everything closes (panels, the game page, the extra rows): the home screen with the selected game highlighted.</summary>
        private void HoldBackHome()
        {
            try
            {
                if (TourLocked) return;   // (1.0.1)
                bool any = confirmOpen || openPanel != null || detailsOpen || extraRowAt > 0 || kbOpen || FolderInside || LibraryOpen;
                if (!any) return;
                if (kbOpen) CloseKeyboard(false);
                if (confirmOpen) CloseConfirm(false);
                if (IsMasterMoving) EndMasterMove(false);
                if (freeMove) EndFreeMove(null);
                if (openPanel != null) ClosePanels();
                if (detailsOpen) CloseGameDetails(true);
                LeaveExtraRows(false);
                PlaySound("back");
                if (CloseLibraryView(false)) return;   // the library view: home is the home screen under it, on the same game
                if (FolderGoHome()) return;   // Folder: home is the folder tiles, on the folder you were in
                ui.BeginInvoke(new Action(FocusSelectedCard), DispatcherPriority.ContextIdle);
            }
            catch (Exception ex) { logger.Warn("Kippix: hold B for home: " + ex.Message); }
        }
    }
}
