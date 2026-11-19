using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>
    /// (1.0.5, issue #51) Turning Kippix's controller actions off, and getting out of a mess:
    ///  - Settings › General settings › Controller buttons: Y on an action turns it off (no button does it; X on the home screen
    ///    no longer starts the highlighted game, say), Y again turns it back on. Saved as "None" in PadMap.
    ///  - Failsafe: both sticks held in (L3 + R3) for 5 seconds, anywhere in fullscreen, puts every controller button back as
    ///    Kippix comes (read from the buttons themselves, before anything is moved or turned off, so it always works). Also
    ///    Reset controller buttons in the add-on's own settings in desktop mode.
    /// </summary>
    public partial class ThemeApi
    {
        internal const string PadOff = "None";

        /// <summary>Y on an action row: off, or back on (its own button).</summary>
        private bool PadToggleOff()
        {
            var fe = Keyboard.FocusedElement as FrameworkElement;
            var row = fe != null ? fe.DataContext as PickRow : null;
            if (row == null || row.Key == null || !row.Key.StartsWith("pad:", StringComparison.Ordinal)) return false;
            string id = row.Key.Substring(4);
            if (!PadActions.Any(a => a.Id == id)) return false;
            if (settings.PadMap == null) settings.PadMap = new Dictionary<string, string>();
            string v;
            if (settings.PadMap.TryGetValue(id, out v) && v == PadOff) settings.PadMap.Remove(id);   // back on, on its own button
            else settings.PadMap[id] = PadOff;
            remapHeld.Clear();
            SaveSettings();
            PlaySound("move");
            GRowsAgain(row.Key);
            return true;
        }

        /// <summary>Every controller button as Kippix comes: moved and turned-off actions, the home button, B on home, Menu and View.</summary>
        internal void ResetControllerButtons()
        {
            if (settings.PadMap != null) settings.PadMap.Clear();
            settings.GuideGoesHome = true;
            settings.HomeButton = 0;
            settings.HomeBackTo = 0;
            settings.PadStartDoes = 0;   // (1.0.4)
            settings.PadViewDoes = 0;
            remapHeld.Clear();
            SaveSettings();
            foreach (string n in new[] { "GuideGoesHome", "HomeButtonTitle", "HomeButtonText" }) Notify(n);
            logger.Info("Kippix: controller buttons reset");
        }

        // ---------------- the failsafe: L3 + R3 held for 5 seconds ----------------

        private bool failL3, failR3;
        private DispatcherTimer failTimer;

        /// <summary>From the top of OnPadDownEarly / OnPadUpEarly, before anything is translated: the sticks' clicks as pressed.</summary>
        private void PadFailsafe(ControllerInput button, bool pressed)
        {
            if (button == ControllerInput.LeftStick) failL3 = pressed;
            else if (button == ControllerInput.RightStick) failR3 = pressed;
            else return;
            if (failL3 && failR3)
            {
                if (failTimer == null)
                {
                    failTimer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromSeconds(5) };
                    failTimer.Tick += (s, e) =>
                    {
                        failTimer.Stop();
                        if (!failL3 || !failR3) return;
                        ResetControllerButtons();
                        PlaySound("select");
                        try { ShowCard(Loc("LOC_KX_RmTitle", "Controller buttons").ToUpperInvariant(), Loc("LOC_KX_RmResetDone", "Controller buttons reset"),
                                       Loc("LOC_KX_RmResetDoneLine", "Every button does what it did when Kippix was installed"), "", null); } catch (Exception) { }
                        if (openPanel == "options" && optionsPage == "padbuttons") GRowsAgain("padreset");
                    };
                }
                if (!failTimer.IsEnabled) failTimer.Start();
            }
            else if (failTimer != null) failTimer.Stop();
        }
    }
}
