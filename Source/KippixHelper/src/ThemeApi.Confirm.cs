using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// A "are you sure?" box over everything, for the few things that can't be taken back easily:
    /// Reset everything, resetting a section, and the Power choices. Cancel has the highlight to start with; B cancels.
    /// </summary>
    public partial class ThemeApi
    {
        private bool confirmOpen;
        private Action confirmYes;
        private IInputElement confirmFocus;
        private string confirmTitle = "", confirmText = "", confirmYesText = "";

        public bool IsConfirmOpen { get { return confirmOpen; } }
        public string ConfirmTitle { get { return confirmTitle; } }
        public string ConfirmText { get { return confirmText; } }
        public string ConfirmYesText { get { return confirmYesText; } }
        private bool confirmPlain;   // the Yes button does nothing that loses anything (Download what's missing): not in the warning red
        public bool ConfirmYesDanger { get { return !confirmPlain; } }
        public bool ConfirmYesPlain { get { return confirmPlain; } }
        public ICommand ConfirmYesCommand { get; private set; }
        public ICommand ConfirmNoCommand { get; private set; }

        private void InitConfirm()
        {
            ConfirmYesCommand = new RelayCommand(_ => CloseConfirm(true));
            ConfirmNoCommand = new RelayCommand(_ => CloseConfirm(false));
        }

        /// <summary>Asks first, as Confirm does, for something that takes nothing away: its Yes button looks like any other button.</summary>
        private void ConfirmPlain(string title, string text, string yesText, Action yes)
        {
            Confirm(title, text, yesText, yes);
            confirmPlain = true;
            Notify("ConfirmYesDanger");
            Notify("ConfirmYesPlain");
        }

        private void Confirm(string title, string text, string yesText, Action yes)
        {
            if (confirmPlain) { confirmPlain = false; Notify("ConfirmYesDanger"); Notify("ConfirmYesPlain"); }
            confirmTitle = title ?? "";
            confirmText = text ?? "";
            confirmYesText = string.IsNullOrEmpty(yesText) ? Loc("LOC_KX_Yes", "Yes") : yesText;
            confirmYes = yes;
            confirmFocus = Keyboard.FocusedElement;
            confirmOpen = true;
            PlaySound("select");
            foreach (string n in new[] { "ConfirmTitle", "ConfirmText", "ConfirmYesText", "IsConfirmOpen" }) Notify(n);
            ui.BeginInvoke(new Action(() => UiTools.FocusByName("KX_ConfirmNo")), DispatcherPriority.ContextIdle);
        }

        private void CloseConfirm(bool yes)
        {
            if (!confirmOpen) return;
            confirmOpen = false;
            var act = confirmYes;
            confirmYes = null;
            Notify("IsConfirmOpen");
            var back = confirmFocus as UIElement;
            confirmFocus = null;
            if (!yes) PlaySound("back");
            if (back != null) ui.BeginInvoke(new Action(() => { if (back.IsVisible && back.Focusable) Keyboard.Focus(back); }), DispatcherPriority.ContextIdle);
            if (yes && act != null)
            {
                try { act(); } catch (Exception ex) { logger.Error(ex, "Kippix: confirmed action failed"); }
            }
        }
    }
}
