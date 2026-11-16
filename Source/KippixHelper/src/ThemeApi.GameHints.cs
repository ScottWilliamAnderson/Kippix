using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KippixHelper
{
    /// <summary>
    /// The game page's button hints say what A does where the highlight is: the buttons beside Play are icons only, and
    /// nothing named the one you were on. The first hint is "A" and the highlighted button's name (Play / Install as Playnite
    /// labels it, the icon buttons' and the trailer controls' tooltips, a lower page card's name, a game under More like
    /// this); it goes when the highlight is on nothing A does anything with. The rest of the strip is what the page's other
    /// buttons really do there: B, LB / RB (Playnite's own previous / next game), X (the highlighted part's settings).
    /// Start and View aren't given a job on the game page by Kippix (Playnite's Start, the game menu, is on the list of
    /// games only), so they aren't listed.
    /// </summary>
    public partial class ThemeApi
    {
        private string detHintAText = "";
        public string DetHintAText { get { return detHintAText; } }
        public bool DetHintAShown { get { return detHintAText.Length > 0; } }

        /// <summary>The highlight moved (the window's focus hook): the A hint follows it.</summary>
        private void DetHintFocus(DependencyObject focused)
        {
            string t = "";
            try { if (detailsOpen && openPanel == null) t = DetHintActionOf(focused) ?? ""; } catch (Exception) { }
            if (t == detHintAText) return;
            detHintAText = t;
            Notify("DetHintAText");
            Notify("DetHintAShown");
        }

        private string DetHintActionOf(DependencyObject focused)
        {
            var b = focused as ButtonBase;
            if (b == null || IsSink(b) || Ancestor(b, "PART_ElemGameDetails") == null) return null;
            string name = b.Name ?? "";
            if (b.Command == null && !name.StartsWith("PART_", StringComparison.Ordinal)) return null;   // (a card that's only there to be read)
            string tip = b.ToolTip as string;
            if (!string.IsNullOrWhiteSpace(tip)) return tip.Trim();
            string own = b.Content as string;
            if (string.IsNullOrWhiteSpace(own))
            {
                var text = UiTools.FindChild<TextBlock>(b);
                own = text != null ? text.Text : null;
            }
            own = string.IsNullOrWhiteSpace(own) ? null : own.Trim();
            if (name == "PART_ButtonContext") return own ?? Loc("LOC_KX_Play", "Play");
            // a part that is one button (a lower page card, the description): its name; a button inside a part: its own words
            string part = DetPartAt(b);
            if (part != null && part != "buttons" && part != "media" && Array.IndexOf(DetNames(part), name) >= 0) return DetPartName(part);
            return own ?? (part != null && part != "buttons" && part != "media" ? DetPartName(part) : null);
        }
    }
}