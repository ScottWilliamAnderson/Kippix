using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// One look (Look and feel › Look packs, designed in Theme Studio): colours, fonts, text size, cards, panels, the top bar
    /// and the buttons. Kept in the settings (Look) and in a saved layout; a pack is one of these in a file
    /// (Documents › Kippix Layouts › Looks › name.json). Colours are #RRGGBB. Every value here starts as the theme's own.
    /// </summary>
    public class LookStyle
    {
        public string Name = "Kippix";
        // colours
        public string Accent = "#FFFFFF";          // under "Accent from the game" (shown when that's off, or a game has no colour)
        public string Text = "#F4F6FA";
        public string SubText = "#E4E8EF";         // the second line of rows (and, fainter, the dim text)
        public string Panel = "#1B1E24";           // the side panels (and the plate behind the top bar)
        public int PanelOpacity = 94;              // %
        public int PanelCorner = 22;
        public string BgTint = "#000000";          // a colour laid over the home screen's background
        public int BgTintAmount = 0;               // % (0 = none)
        // fonts and text
        public string TitleFont = "";              // "" Titillium Web Bold (the theme's), "tw:regular", "tw:light", or a Windows font's name
        public string BodyFont = "";               // "" Titillium Web (the theme's), "tw:light", or a Windows font's name
        public int TextScale = 100;                // % (on top of Accessibility › Text size)
        // the game cards
        public int CardCorner = 10;
        public int CardBorder = 23;                // the highlight edge, in tenths of a pixel (2.3)
        public string CardBorderColor = "";        // "" = the accent
        public int GlowStyle = 1;                  // 0 none, 1 soft, 2 neon, 3 shadow
        public int GlowSize = 14;
        public string GlowColor = "#FFFFFF";       // "" = the accent
        public int FocusScale = 106;               // % the highlighted card grows to
        public int FocusSpeed = 1;                 // 0 slow, 1 normal, 2 fast, 3 instant
        // the top bar
        public int TopBarPlate = 0;                // % of a band in the panel colour behind it
        public int TopBarShade = 60;               // % of the soft dark fade from the top edge
        public int TopBarSize = 100;               // %
        // buttons and rows
        public int Highlight = 0;                  // 0 soft, 1 accent, 2 bold, 3 minimal
        public int ButtonShape = 0;                // 0 Kippix, 1 round, 2 soft square, 3 square

        public LookStyle Copy() { return (LookStyle)MemberwiseClone(); }
    }

    /// <summary>
    /// Look packs: one-press looks (built-in ones and your own), saved as files you can share, and how a look is put on screen:
    /// the theme's DynamicResource keys in Application.Current.Resources (never bindings inside brushes / effects), the top
    /// bar's shade and size, and the highlighted card's zoom. "Accent from the game" sits on top of the pack's accent.
    /// </summary>
    public partial class ThemeApi
    {
        private LookStyle LookNow { get { if (settings.Look == null) settings.Look = new LookStyle(); return settings.Look; } }

        // ---------------- the built-in packs ----------------

        private static readonly string[] LookPackIds = { "kippix", "midnight", "arcade", "pastel", "oled", "crt", "sunset" };

        private static LookStyle BuiltInLook(string id)
        {
            var l = new LookStyle();
            switch (id)
            {
                case "midnight":
                    l.Name = "Midnight"; l.Accent = "#9DB8FF"; l.Text = "#EEF2FF"; l.SubText = "#C3CEF5"; l.Panel = "#121A33"; l.PanelOpacity = 93; l.PanelCorner = 26;
                    l.BgTint = "#0B1638"; l.BgTintAmount = 30; l.TitleFont = "Segoe UI Semibold"; l.BodyFont = "Segoe UI";
                    l.CardCorner = 14; l.CardBorder = 20; l.GlowStyle = 1; l.GlowSize = 22; l.GlowColor = ""; l.FocusScale = 105; l.FocusSpeed = 0;
                    l.TopBarShade = 70; l.Highlight = 1; l.ButtonShape = 1;
                    break;
                case "arcade":
                    l.Name = "Arcade"; l.Accent = "#FF4FB3"; l.Text = "#FFFFFF"; l.SubText = "#FFD3EE"; l.Panel = "#1A0A26"; l.PanelOpacity = 90; l.PanelCorner = 6;
                    l.BgTint = "#2B0047"; l.BgTintAmount = 22; l.TitleFont = "Bahnschrift SemiBold"; l.BodyFont = "Bahnschrift";
                    l.CardCorner = 4; l.CardBorder = 30; l.CardBorderColor = "#2DF2FF"; l.GlowStyle = 2; l.GlowSize = 26; l.GlowColor = "#2DF2FF";
                    l.FocusScale = 110; l.FocusSpeed = 2; l.TopBarShade = 75; l.Highlight = 1; l.ButtonShape = 3;
                    break;
                case "pastel":
                    l.Name = "Pastel"; l.Accent = "#F6C1D9"; l.Text = "#FFF9F3"; l.SubText = "#EADCF2"; l.Panel = "#2A2433"; l.PanelOpacity = 90; l.PanelCorner = 30;
                    l.BgTint = "#4A3560"; l.BgTintAmount = 18; l.TitleFont = "tw:light";
                    l.CardCorner = 20; l.CardBorder = 20; l.CardBorderColor = "#BDE6D8"; l.GlowStyle = 1; l.GlowSize = 20; l.GlowColor = "#F6C1D9";
                    l.FocusScale = 105; l.TopBarShade = 50; l.Highlight = 1; l.ButtonShape = 1;
                    break;
                case "oled":
                    l.Name = "OLED black"; l.Accent = "#FFFFFF"; l.Text = "#FFFFFF"; l.SubText = "#B8B8B8"; l.Panel = "#000000"; l.PanelOpacity = 100; l.PanelCorner = 16;
                    l.BgTint = "#000000"; l.BgTintAmount = 45;
                    l.CardCorner = 8; l.CardBorder = 20; l.GlowStyle = 0; l.FocusSpeed = 2;
                    l.TopBarPlate = 85; l.TopBarShade = 90; l.Highlight = 2;
                    break;
                case "crt":
                    l.Name = "Retro CRT"; l.Accent = "#7DFF9B"; l.Text = "#C9FFD6"; l.SubText = "#8EE6A6"; l.Panel = "#031208"; l.PanelOpacity = 95; l.PanelCorner = 2;
                    l.BgTint = "#00381A"; l.BgTintAmount = 34; l.TitleFont = "Consolas"; l.BodyFont = "Consolas"; l.TextScale = 95;
                    l.CardCorner = 0; l.CardBorder = 20; l.GlowStyle = 2; l.GlowSize = 18; l.GlowColor = "";
                    l.FocusScale = 103; l.FocusSpeed = 3; l.TopBarShade = 80; l.Highlight = 1; l.ButtonShape = 3;
                    break;
                case "sunset":
                    l.Name = "Sunset"; l.Accent = "#FFB37A"; l.Text = "#FFF4EA"; l.SubText = "#F2D3BF"; l.Panel = "#2A1712"; l.PanelOpacity = 92;
                    l.BgTint = "#5A2410"; l.BgTintAmount = 20; l.TitleFont = "Georgia"; l.BodyFont = "Segoe UI";
                    l.CardCorner = 12; l.GlowStyle = 1; l.GlowSize = 18; l.GlowColor = ""; l.Highlight = 1; l.ButtonShape = 1;
                    break;
            }
            return l;
        }

        private string LookPackDescription(string id)
        {
            switch (id)
            {
                case "midnight": return Loc("LOC_KX_PackMidnightSub", "Deep blue, soft blue light, calm and rounded");
                case "arcade": return Loc("LOC_KX_PackArcadeSub", "Neon pink and cyan, sharp corners, quick and bright");
                case "pastel": return Loc("LOC_KX_PackPastelSub", "Soft rose and mint, light titles, very round");
                case "oled": return Loc("LOC_KX_PackOledSub", "True black panels, no glow, strong highlights");
                case "crt": return Loc("LOC_KX_PackCrtSub", "Green phosphor, a typewriter font, square corners");
                case "sunset": return Loc("LOC_KX_PackSunsetSub", "Warm amber and orange, a classic serif for titles");
            }
            return Loc("LOC_KX_PackKippixSub", "The theme's own: clean white on dark");
        }

        // ---------------- values kept in range ----------------

        private static int LookClamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        private static void NormalizeLookStyle(LookStyle l)
        {
            if (l == null) return;
            var d = new LookStyle();
            if (string.IsNullOrEmpty(l.Name)) l.Name = "Kippix";
            if (ParseHex(l.Accent) == null) l.Accent = d.Accent;
            if (ParseHex(l.Text) == null) l.Text = d.Text;
            if (ParseHex(l.SubText) == null) l.SubText = d.SubText;
            if (ParseHex(l.Panel) == null) l.Panel = d.Panel;
            if (ParseHex(l.BgTint) == null) l.BgTint = d.BgTint;
            if (!string.IsNullOrEmpty(l.CardBorderColor) && ParseHex(l.CardBorderColor) == null) l.CardBorderColor = "";
            if (!string.IsNullOrEmpty(l.GlowColor) && ParseHex(l.GlowColor) == null) l.GlowColor = "";
            l.TitleFont = l.TitleFont ?? "";
            l.BodyFont = l.BodyFont ?? "";
            l.PanelOpacity = LookClamp(l.PanelOpacity, 40, 100);
            l.PanelCorner = LookClamp(l.PanelCorner, 0, 40);
            l.BgTintAmount = LookClamp(l.BgTintAmount, 0, 80);
            l.TextScale = LookClamp(l.TextScale, 85, 130);
            l.CardCorner = LookClamp(l.CardCorner, 0, 32);
            l.CardBorder = LookClamp(l.CardBorder, 0, 60);
            l.GlowStyle = LookClamp(l.GlowStyle, 0, 3);
            l.GlowSize = LookClamp(l.GlowSize, 0, 48);
            l.FocusScale = LookClamp(l.FocusScale, 100, 115);
            l.FocusSpeed = LookClamp(l.FocusSpeed, 0, 3);
            l.TopBarPlate = LookClamp(l.TopBarPlate, 0, 100);
            l.TopBarShade = LookClamp(l.TopBarShade, 0, 100);
            l.TopBarSize = LookClamp(l.TopBarSize, 80, 130);
            l.Highlight = LookClamp(l.Highlight, 0, 3);
            l.ButtonShape = LookClamp(l.ButtonShape, 0, 3);
        }

        /// <summary>From NormalizeSettings: the look's values in range, and the pack it came from named.</summary>
        private void NormalizeLook()
        {
            if (settings.Look != null) NormalizeLookStyle(settings.Look);
            if (string.IsNullOrEmpty(settings.LookPack) || settings.LookPack == "builtin:" + "kip" + "screen") settings.LookPack = "builtin:kippix";   // (0.85: the rename)
        }

        // ---------------- colours ----------------

        private static Color? ParseHex(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim().TrimStart('#');
            uint v;
            if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return null;
            if (s.Length == 6) return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        private static Color HexOr(string s, Color fallback)
        {
            var c = ParseHex(s);
            return c.HasValue ? c.Value : fallback;
        }

        internal static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        private static Color Alpha(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))), c.R, c.G, c.B); }

        private static Color Darker(Color c, double k) { return Color.FromArgb(c.A, (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)); }

        private static SolidColorBrush FrozenBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        /// <summary>The accent under "Accent from the game": the pack's own.</summary>
        private Color LookBaseAccent() { return HexOr(LookNow.Accent, Colors.White); }

        // ---------------- putting a look on screen ----------------

        private readonly Dictionary<string, string> lookSigs = new Dictionary<string, string>();
        private bool contrastOn;   // High contrast's keys are in place (ApplyContrast)

        /// <summary>One of the theme's resources: put (or taken back to the theme's own with null), only when it changed.
        /// High contrast's own keys are left to it while it's on.</summary>
        // 0.82: the colours that follow the accent (and so change with every game when Accent from the game is on) are brushes
        // made once and then only recoloured. Swapping a resource makes WPF look the change up through the whole screen, and
        // with Accent from the game that happened several times on every move: the glow lagged and moving between cards stuttered.
        private readonly Dictionary<string, SolidColorBrush> liveBrushes = new Dictionary<string, SolidColorBrush>();

        /// <summary>A brush resource that follows a colour: recoloured in place when it's still ours, else put in (once).</summary>
        private void LivePut(string key, Color c, string sig)
        {
            if (settings.HighContrast && Array.IndexOf(ContrastKeys, key) >= 0) return;
            var app = Application.Current;
            if (app == null) return;
            SolidColorBrush b;
            try
            {
                if (liveBrushes.TryGetValue(key, out b) && !b.IsFrozen && app.Resources.Contains(key) && ReferenceEquals(app.Resources[key], b))
                {
                    if (b.Color != c) b.Color = c;
                    lookSigs[key] = sig;
                    return;
                }
            }
            catch (Exception) { }
            b = new SolidColorBrush(c);
            liveBrushes[key] = b;
            lookSigs[key] = sig;
            app.Resources[key] = b;
        }

        /// <summary>The card glow's colour (0.82): KX_CardGlowBrush in Constants.xaml, which the glows bind to, recoloured in place.</summary>
        private void PutGlowColour(Color c)
        {
            try
            {
                var app = Application.Current;
                var b = app != null ? app.TryFindResource("KX_CardGlowBrush") as SolidColorBrush : null;
                if (b != null && !b.IsFrozen) { if (b.Color != c) b.Color = c; return; }
                if (b != null && !glowFrozenLogged) { glowFrozenLogged = true; logger.Warn("Kippix: the card glow's brush is frozen: its colour can't follow the accent"); }
            }
            catch (Exception ex) { logger.Warn("Kippix: card glow colour: " + ex.Message); }
        }
        private bool glowFrozenLogged;

        private void LookPut(string key, object value, string sig)
        {
            if (settings.HighContrast && Array.IndexOf(ContrastKeys, key) >= 0) return;
            string old;
            if (lookSigs.TryGetValue(key, out old) && old == sig) return;
            var app = Application.Current;
            if (app == null) return;
            lookSigs[key] = sig;
            if (value == null) app.Resources.Remove(key);
            else app.Resources[key] = value;
        }

        /// <summary>After any change to the look: every part of it again (unchanged ones cost nothing).</summary>
        private void ApplyLook()
        {
            if (!IsFullscreen) return;
            try
            {
                ApplyTextSize();
                ApplyContrast();   // (puts the look's resources first: LookResources)
                LookExtras();
                HookLookFocus();
            }
            catch (Exception ex) { logger.Warn("Kippix: look: " + ex.Message); }
        }

        /// <summary>From ApplyAllSettings (a layout, a profile, a reset, Undo): the look in the new settings, all of it again.</summary>
        private void LookSettingsSwapped() { LookSettingsSwapped(true); }

        /// <summary>force false: (0.94) only the resource keys whose values differ from what's set now are changed.</summary>
        private void LookSettingsSwapped(bool force)
        {
            lookBase = null;
            if (!IsFullscreen) return;
            if (force) lookSigs.Clear();
            ApplyLook();
            if (optionsPage == "studio" && openPanel == "options") StudioRebuild(null);
        }

        /// <summary>The game cards' corners now (Row's carousel draws its own cards with it).</summary>
        private double LookCardCorner { get { return settings.Look != null ? settings.Look.CardCorner : 10; } }

        /// <summary>Accessibility › Text size times the look's own text size.</summary>
        private double LookTextScale() { return IsFullscreen ? LookNow.TextScale / 100.0 : 1.0; }

        /// <summary>The look's colours, fonts, corners and glows (from ApplyContrast, before High contrast goes over its own keys).
        /// force: every key again (High contrast was just switched, or settings were swapped).</summary>
        private void LookResources(bool force)
        {
            if (!IsFullscreen || Application.Current == null) return;
            if (force) lookSigs.Clear();
            var L = LookNow;
            var D = new LookStyle();

            // text
            Color text = HexOr(L.Text, Colors.White), sub = HexOr(L.SubText, Colors.White);
            bool textOwn = string.Equals(L.Text, D.Text, StringComparison.OrdinalIgnoreCase);
            bool subOwn = string.Equals(L.SubText, D.SubText, StringComparison.OrdinalIgnoreCase);
            LookPut("KX_TextBrush", textOwn ? null : FrozenBrush(text), textOwn ? "" : L.Text);
            LookPut("TextBrush", textOwn ? null : FrozenBrush(text), textOwn ? "" : L.Text);
            LookPut("KX_TextSubBrush", subOwn ? null : FrozenBrush(Alpha(sub, 0.8)), subOwn ? "" : L.SubText);
            LookPut("KX_TextDimBrush", subOwn ? null : FrozenBrush(Alpha(sub, 0.55)), subOwn ? "" : L.SubText);

            // panels
            bool panelOwn = string.Equals(L.Panel, D.Panel, StringComparison.OrdinalIgnoreCase) && L.PanelOpacity == D.PanelOpacity;
            Color panel = HexOr(L.Panel, Color.FromRgb(0x1B, 0x1E, 0x24));
            if (panelOwn) LookPut("KX_PanelBrush", null, "");
            else
            {
                double a = L.PanelOpacity / 100.0;
                var g = new LinearGradientBrush(Alpha(panel, a), Alpha(Darker(panel, 0.48), a), new Point(0, 0), new Point(0, 1));
                g.Freeze();
                LookPut("KX_PanelBrush", g, L.Panel + "|" + L.PanelOpacity);
            }
            LookPut("KX_PanelCorner", L.PanelCorner == D.PanelCorner ? (object)null : new CornerRadius(L.PanelCorner), L.PanelCorner.ToString(CultureInfo.InvariantCulture));

            // the background's tint
            LookPut("KX_BgTintBrush", L.BgTintAmount <= 0 ? null : FrozenBrush(Alpha(HexOr(L.BgTint, Colors.Black), L.BgTintAmount / 100.0)), L.BgTintAmount <= 0 ? "" : L.BgTint + "|" + L.BgTintAmount);

            // fonts
            LookPut("FontTitilliumWebBold", LookFont(L.TitleFont, true), L.TitleFont ?? "");
            LookPut("FontTitilliumWeb", LookFont(L.BodyFont, false), L.BodyFont ?? "");

            // cards: corners, the highlight edge and its glow
            LookPut("KX_CardCorner", L.CardCorner == D.CardCorner ? (object)null : new CornerRadius(L.CardCorner), L.CardCorner.ToString(CultureInfo.InvariantCulture));
            LookPut("KX_CardRingCorner", L.CardCorner == D.CardCorner ? (object)null : new CornerRadius(L.CardCorner + 1), L.CardCorner.ToString(CultureInfo.InvariantCulture));
            double edge = L.CardBorder / 10.0;
            LookPut("KX_CardRingThickness", L.CardBorder == D.CardBorder ? (object)null : new Thickness(edge), L.CardBorder.ToString(CultureInfo.InvariantCulture));
            LookPut("KX_CardRingWidth", L.CardBorder == D.CardBorder ? (object)null : (object)(edge + 0.3), L.CardBorder.ToString(CultureInfo.InvariantCulture));
            double glowSize = L.GlowStyle == 2 ? L.GlowSize * 1.4 : L.GlowSize;
            double glowOpacity = L.GlowStyle == 0 ? 0 : L.GlowStyle == 2 ? 0.95 : L.GlowStyle == 3 ? 0.75 : 0.5;
            bool glowOwn = L.GlowStyle == D.GlowStyle && L.GlowSize == D.GlowSize;
            LookPut("KX_CardGlowSize", glowOwn ? (object)null : (object)glowSize, glowOwn ? "" : glowSize.ToString(CultureInfo.InvariantCulture));
            LookPut("KX_CardGlowOpacity", glowOwn ? (object)null : (object)glowOpacity, glowOwn ? "" : glowOpacity.ToString(CultureInfo.InvariantCulture));

            // rows and pills: their corners
            int row = L.ButtonShape == 1 ? 32 : L.ButtonShape == 2 ? 8 : L.ButtonShape == 3 ? 2 : 14;
            int pill = L.ButtonShape == 2 ? 12 : L.ButtonShape == 3 ? 3 : 31;
            LookPut("KX_RowCorner", L.ButtonShape == 0 ? (object)null : new CornerRadius(row), row.ToString(CultureInfo.InvariantCulture));
            LookPut("KX_PillCorner", L.ButtonShape == 0 ? (object)null : new CornerRadius(pill), pill.ToString(CultureInfo.InvariantCulture));

            // the accent (Accent from the game, when it's on, has the say: its fallback is this one)
            if (!settings.AccentFromGame)
            {
                var a = LookBaseAccent();
                if (a != accentNow) { if (ui != null && !ReduceMotionNow) SetAccent(a); else PutAccent(a); }
            }
            LookAccentFollow(true);
            // once the window is on its way (Start): the top bar's look, and the highlighted card's zoom
            if (ui != null && !lookStarted) { lookStarted = true; HookLookFocus(); LookExtras(); HookBareWindows(); }
        }

        private bool lookStarted;

        /// <summary>
        /// What follows the accent (the card's edge and glow when they're set to it, the Accent highlight): put again after the
        /// accent changes. The accent fades in a few steps, so this waits for the last one.
        /// </summary>
        private DispatcherTimer lookFollowTimer;

        private void LookAccentFollow(bool now)
        {
            if (!IsFullscreen) return;
            if (!now && ui != null)
            {
                if (lookFollowTimer == null)
                {
                    lookFollowTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(90) };
                    lookFollowTimer.Tick += (s, e) => { lookFollowTimer.Stop(); LookAccentFollow(true); };
                }
                lookFollowTimer.Stop();
                lookFollowTimer.Start();
                return;
            }
            try
            {
                var L = LookNow;
                var D = new LookStyle();
                Color accent = accentNow;
                string ah = ToHex(accent);
                Color ring = string.IsNullOrEmpty(L.CardBorderColor) ? accent : HexOr(L.CardBorderColor, accent);
                LivePut("KX_CardRingBrush", ring, ToHex(ring));
                Color glow = L.GlowStyle == 3 ? Colors.Black : string.IsNullOrEmpty(L.GlowColor) ? accent : HexOr(L.GlowColor, Colors.White);
                PutGlowColour(glow);
                // the highlight on rows and buttons
                switch (L.Highlight)
                {
                    case 1:
                        LivePut("KX_FocusFillBrush", Alpha(accent, 0.22), "1|" + ah);
                        LivePut("KX_FocusRingBrush", Alpha(accent, 0.94), "1|" + ah);
                        LookPut("KX_FocusRingThickness", new Thickness(2), "2");
                        // (a dark accent: the pills stay white, so their dark text can still be read)
                        bool light = 0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B >= 110;
                        if (light) LivePut("KX_PillFocusBrush", accent, "1|" + ah); else LookPut("KX_PillFocusBrush", null, "");
                        break;
                    case 2:
                        LookPut("KX_FocusFillBrush", FrozenBrush(Color.FromArgb(0x4D, 255, 255, 255)), "2");
                        LookPut("KX_FocusRingBrush", FrozenBrush(Colors.White), "2");
                        LookPut("KX_FocusRingThickness", new Thickness(3), "3");
                        LookPut("KX_PillFocusBrush", null, "");
                        break;
                    case 3:
                        LookPut("KX_FocusFillBrush", FrozenBrush(Color.FromArgb(0x29, 255, 255, 255)), "3");
                        LookPut("KX_FocusRingBrush", FrozenBrush(Colors.Transparent), "3");
                        LookPut("KX_FocusRingThickness", new Thickness(0), "0");
                        LookPut("KX_PillFocusBrush", null, "");
                        break;
                    default:
                        LookPut("KX_FocusFillBrush", null, "");
                        LookPut("KX_FocusRingBrush", null, "");
                        LookPut("KX_FocusRingThickness", null, "");
                        LookPut("KX_PillFocusBrush", null, "");
                        break;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: look accent: " + ex.Message); }
        }

        // ---------------- fonts ----------------

        private static HashSet<string> systemFonts;

        /// <summary>The fonts Windows has (by name), read once.</summary>
        private static HashSet<string> SystemFontNames()
        {
            if (systemFonts != null) return systemFonts;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var f in Fonts.SystemFontFamilies) if (f != null && f.Source != null) set.Add(f.Source); }
            catch (Exception) { }
            systemFonts = set;
            return set;
        }

        /// <summary>Titillium Web as Playnite ships it (the regular, light or bold file), whatever the look put over it.</summary>
        private static object PlayniteFont(string key)
        {
            var app = Application.Current;
            if (app == null) return null;
            var md = app.Resources.MergedDictionaries;
            for (int i = md.Count - 1; i >= 0; i--)
            {
                var v = FindInDict(md[i], key);
                if (v != null) return v;
            }
            return null;
        }

        private static object FindInDict(ResourceDictionary d, string key)
        {
            if (d == null) return null;
            try
            {
                if (d.Contains(key)) return d[key];
                for (int i = d.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var v = FindInDict(d.MergedDictionaries[i], key);
                    if (v != null) return v;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The font for a look's title / body choice (null: the theme's own).</summary>
        private static object LookFont(string id, bool title)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id == "tw:regular") return title ? PlayniteFont("FontTitilliumWeb") : null;
            if (id == "tw:light") return PlayniteFont("FontTitilliumWebLight");
            if (id == "tw:bold") return title ? null : PlayniteFont("FontTitilliumWebBold");
            try { return new FontFamily(id); } catch (Exception) { return null; }
        }

        /// <summary>The fonts offered: Titillium Web (it comes with Playnite) first, then the Windows ones this PC has.</summary>
        private List<KeyValuePair<string, string>> LookFontChoices(bool title)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (title)
            {
                list.Add(new KeyValuePair<string, string>("", "Titillium Web Bold (Kippix)"));
                list.Add(new KeyValuePair<string, string>("tw:regular", "Titillium Web"));
                list.Add(new KeyValuePair<string, string>("tw:light", "Titillium Web Light"));
            }
            else
            {
                list.Add(new KeyValuePair<string, string>("", "Titillium Web (Kippix)"));
                list.Add(new KeyValuePair<string, string>("tw:light", "Titillium Web Light"));
                list.Add(new KeyValuePair<string, string>("tw:bold", "Titillium Web Bold"));
            }
            string[] windows = title
                ? new[] { "Segoe UI Black", "Segoe UI Semibold", "Segoe UI", "Bahnschrift SemiBold", "Bahnschrift", "Arial Black", "Impact", "Franklin Gothic Medium", "Georgia", "Trebuchet MS", "Verdana", "Corbel", "Consolas", "Lucida Console" }
                : new[] { "Segoe UI", "Segoe UI Semilight", "Bahnschrift", "Calibri", "Candara", "Corbel", "Trebuchet MS", "Verdana", "Tahoma", "Arial", "Georgia", "Consolas", "Lucida Console" };
            var have = SystemFontNames();
            foreach (string f in windows)
                if (have.Count == 0 || have.Contains(f)) list.Add(new KeyValuePair<string, string>(f, f));
            return list;
        }

        private string LookFontName(string id, bool title)
        {
            foreach (var p in LookFontChoices(title)) if (p.Key == (id ?? "")) return p.Value;
            return string.IsNullOrEmpty(id) ? "" : id;
        }

        // ---------------- the top bar, and the highlighted card's zoom ----------------

        private ScaleTransform lookBarScale;
        private int lookExtrasTries;

        /// <summary>The top bar: its plate and shade (the theme's KX_TopShade) and its size.</summary>
        private void LookExtras()
        {
            if (!IsFullscreen || Application.Current == null) return;
            var L = LookNow;
            bool own = L.TopBarPlate == 0 && L.TopBarShade == 60 && L.TopBarSize == 100;
            var win = Application.Current.MainWindow;
            var shade = win != null ? UiTools.FindByName(win, "KX_TopShade") as Border : null;
            var bar = win != null ? UiTools.FindByName(win, "KX_TopBar") : null;
            if (shade == null || bar == null)
            {
                // not on screen yet: once more in a moment (only when there's something to change)
                if (!own && ui != null && lookExtrasTries++ < 30)
                {
                    var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
                    t.Tick += (s, e) => { t.Stop(); LookExtras(); };
                    t.Start();
                }
                return;
            }
            lookExtrasTries = 0;
            double k = L.TopBarSize / 100.0;
            if (L.TopBarPlate == 0 && L.TopBarShade == 60) { shade.ClearValue(Border.BackgroundProperty); shade.ClearValue(FrameworkElement.HeightProperty); }
            else
            {
                Color panel = HexOr(L.Panel, Color.FromRgb(0x1B, 0x1E, 0x24));
                double plate = L.TopBarPlate / 100.0, dark = L.TopBarShade / 100.0 * 0.6;
                double h = 220 * k, band = Math.Min(0.9, 104 * k / h);
                // the plate (the panel colour) over the shade (black), as one colour at each stop
                Func<double, double, Color> over = (p, s) =>
                {
                    double a = p + s * (1 - p);
                    if (a <= 0) return Colors.Transparent;
                    return Color.FromArgb((byte)Math.Round(255 * a), (byte)Math.Round(panel.R * p / a), (byte)Math.Round(panel.G * p / a), (byte)Math.Round(panel.B * p / a));
                };
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                g.GradientStops.Add(new GradientStop(over(plate, dark), 0));
                g.GradientStops.Add(new GradientStop(over(plate, dark * 0.55), Math.Max(0.05, band - 0.06)));
                g.GradientStops.Add(new GradientStop(over(plate * 0.35, dark * 0.4), band + 0.04));
                g.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                g.Freeze();
                shade.Background = g;
                shade.Height = h;
            }
            if (L.TopBarSize == 100)
            {
                if (lookBarScale != null && ReferenceEquals(bar.LayoutTransform, lookBarScale)) bar.ClearValue(FrameworkElement.LayoutTransformProperty);
                lookBarScale = null;
            }
            else
            {
                if (lookBarScale == null || !ReferenceEquals(bar.LayoutTransform, lookBarScale)) { lookBarScale = new ScaleTransform(k, k); bar.LayoutTransform = lookBarScale; }
                else { lookBarScale.ScaleX = k; lookBarScale.ScaleY = k; }
            }
        }

        private static bool lookFocusHooked;
        private static readonly int[] FocusSpeedMs = { 380, 220, 120, 0 };

        /// <summary>The highlighted card's zoom (size and speed) when the look changes it: the card's own zoom (in the theme's
        /// card style) is taken over as it starts. Nothing is done with the theme's own values, or with Reduce motion on.</summary>
        private void HookLookFocus()
        {
            if (lookFocusHooked) return;
            lookFocusHooked = true;
            EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) =>
            {
                if (ReferenceEquals(s, e.NewFocus)) LookCardZoom(s as ListBoxItem, true);
            }), true);
            EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((s, e) =>
            {
                if (ReferenceEquals(s, e.OldFocus)) LookCardZoom(s as ListBoxItem, false);
            }), true);
            // (0.93) the mouse (or a Steam Controller's trackpad) over a game card: it grows as the highlighted one does
            EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.MouseEnterEvent, new MouseEventHandler((s, e) => CardHoverZoom(s as ListBoxItem, true)), true);
            EventManager.RegisterClassHandler(typeof(ListBoxItem), UIElement.MouseLeaveEvent, new MouseEventHandler((s, e) => CardHoverZoom(s as ListBoxItem, false)), true);
        }

        private void CardHoverZoom(ListBoxItem item, bool on)
        {
            try
            {
                if (item == null || !IsFullscreen || item.Template == null) return;
                var list = ItemsControl.ItemsControlFromItemContainer(item) as FrameworkElement;
                if (list == null || list.Name != "PART_ListGameItems") return;
                var st = item.Template.FindName("Scale", item) as ScaleTransform;
                if (st == null || st.IsFrozen) return;
                if (on) Panel.SetZIndex(item, 80); else item.ClearValue(Panel.ZIndexProperty);
                if (item.IsKeyboardFocused) return;   // (the highlighted card keeps its own zoom)
                double to = 1.0;
                if (on) to = settings.Look != null && settings.Look.FocusScale > 100 ? settings.Look.FocusScale / 100.0 : 1.06;
                var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ReduceMotionNow ? 0 : on ? 200 : 180))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            catch (Exception) { }
        }

        private void LookCardZoom(ListBoxItem item, bool on)
        {
            try
            {
                if (item == null || ReduceMotionNow || settings.Look == null) return;
                var L = settings.Look;
                if (L.FocusScale == 106 && L.FocusSpeed == 1) return;
                double to = on ? L.FocusScale / 100.0 : 1.0;
                int ms = FocusSpeedMs[LookClamp(L.FocusSpeed, 0, 3)];
                item.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (item.Template == null) return;
                        var st = item.Template.FindName("Scale", item) as ScaleTransform;
                        if (st == null || st.IsFrozen) return;
                        if (on != item.IsKeyboardFocused) return;   // (moved on already)
                        var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    }
                    catch (Exception) { }
                }), DispatcherPriority.Render);
            }
            catch (Exception) { }
        }

        // ---------------- pack files ----------------

        private static string LooksFolder { get { return Path.Combine(LayoutsFolder, "Looks"); } }

        private class LookPackFile
        {
            public string Id;       // user:<file name>
            public string File;
            public LookStyle Style;
            public DateTime When;
        }

        private List<LookPackFile> ReadUserPacks()
        {
            var list = new List<LookPackFile>();
            try
            {
                if (!Directory.Exists(LooksFolder)) return list;
                var js = new JavaScriptSerializer();
                foreach (var f in new DirectoryInfo(LooksFolder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime))
                {
                    try
                    {
                        var s = ReadLookFile(js, f.FullName);
                        if (s == null) continue;
                        string name = Path.GetFileNameWithoutExtension(f.Name);
                        if (string.IsNullOrEmpty(s.Name)) s.Name = name;
                        list.Add(new LookPackFile { Id = "user:" + name, File = f.FullName, Style = s, When = f.LastWriteTime });
                    }
                    catch (Exception ex) { logger.Warn("Kippix: look pack " + f.Name + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: look packs: " + ex.Message); }
            return list;
        }

        /// <summary>A look pack file (or null when it isn't one).</summary>
        private static LookStyle ReadLookFile(JavaScriptSerializer js, string file)
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 32 * 1024 * 1024) return null;   // (a saved layout can be big: picture tiles are carried in it)
            return LookFromText(js, File.ReadAllText(file, Encoding.UTF8));
        }

        /// <summary>A look pack's text (or a saved layout's look), checked and in range; null when it isn't one.</summary>
        private static LookStyle LookFromText(JavaScriptSerializer js, string text)
        {
            js.MaxJsonLength = int.MaxValue;
            if (string.IsNullOrEmpty(text) || text.IndexOf("\"Accent\"", StringComparison.Ordinal) < 0) return null;
            text = text.TrimStart('\uFEFF');
            var top = js.DeserializeObject(text) as Dictionary<string, object>;
            if (top == null) return null;
            LookStyle s;
            if (top.ContainsKey("Accent")) s = js.ConvertToType<LookStyle>(top);
            else if (top.ContainsKey("Look") && top["Look"] is Dictionary<string, object>) s = js.ConvertToType<LookStyle>(top["Look"]);   // a saved layout: its look
            else return null;
            if (s == null) return null;
            NormalizeLookStyle(s);
            return s;
        }

        private static string SafeFileName(string typed)
        {
            string name = string.Join("_", (typed ?? "").Trim().Split(Path.GetInvalidFileNameChars()));
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        private bool WriteLookPack(string name, LookStyle s)
        {
            try
            {
                Directory.CreateDirectory(LooksFolder);
                var copy = s.Copy();
                copy.Name = name;
                File.WriteAllText(Path.Combine(LooksFolder, name + ".json"), new JavaScriptSerializer().Serialize(copy), Encoding.UTF8);
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: save look pack: " + ex.Message); return false; }
        }

        // ---------------- using a pack ----------------

        /// <summary>Theme Studio's starting point: the pack as it was put on (X in Theme Studio puts a value back to it).</summary>
        private LookStyle lookBase;

        private LookStyle LookBase
        {
            get
            {
                if (lookBase != null) return lookBase;
                var p = FindPack(settings.LookPack);
                lookBase = p != null ? p.Copy() : new LookStyle();
                return lookBase;
            }
        }

        private LookStyle FindPack(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("builtin:", StringComparison.Ordinal))
            {
                string b = id.Substring(8);
                return LookPackIds.Contains(b) ? BuiltInLook(b) : null;
            }
            if (id.StartsWith("user:", StringComparison.Ordinal))
            {
                var f = ReadUserPacks().FirstOrDefault(p => p.Id == id);
                return f != null ? f.Style : null;
            }
            return null;
        }

        private void UseLookPack(LookStyle s, string id)
        {
            if (s == null) return;
            settings.Look = s.Copy();
            settings.LookPack = id;
            NormalizeLook();
            lookBase = settings.Look.Copy();
            SaveSettings();
            PlaySound("select");
            ApplyLook();
            Notify("LookPackText");
            if (optionsPage == "lookpacks") BoardRowsAgain(BuildLookPackRows, "pack:" + id);
            if (optionsPage == "studio") StudioRebuild(null);
        }

        /// <summary>The pack's name, with "(changed)" when Theme Studio has changed it since.</summary>
        public string LookPackText
        {
            get
            {
                var L = LookNow;
                var js = new JavaScriptSerializer();
                var b = LookBase.Copy();
                b.Name = L.Name;
                bool changed = js.Serialize(b) != js.Serialize(L);
                string name = string.IsNullOrEmpty(L.Name) ? "Kippix" : L.Name;
                return changed ? string.Format(Loc("LOC_KX_LookChanged", "{0} (changed in Theme Studio)"), name) : name;
            }
        }

        /// <summary>A little picture of a look: its panel, accent and text colours side by side.</summary>
        private static ImageSource LookSwatch(LookStyle s)
        {
            try
            {
                var g = new DrawingGroup();
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(HexOr(s.Panel, Colors.Black)), null, new RectangleGeometry(new Rect(0, 0, 30, 30))));
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(HexOr(s.Accent, Colors.White)), null, new RectangleGeometry(new Rect(4, 4, 12, 22), 3, 3)));
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(HexOr(s.Text, Colors.White)), null, new RectangleGeometry(new Rect(19, 4, 7, 10), 2, 2)));
                g.Children.Add(new GeometryDrawing(new SolidColorBrush(HexOr(s.SubText, Colors.Gray)), null, new RectangleGeometry(new Rect(19, 16, 7, 10), 2, 2)));
                var img = new DrawingImage(g);
                img.Freeze();
                return img;
            }
            catch (Exception) { return null; }
        }

        // ---------------- the pages ----------------

        public bool IsOptionsLookPacks { get { return optionsPage == "lookpacks"; } }
        public bool IsOptionsLookPack { get { return optionsPage == "lookpack"; } }
        private ObservableCollection<PickRow> lookPackRows, lookPackEditRows;
        public ObservableCollection<PickRow> LookPackRows { get { return lookPackRows ?? (lookPackRows = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> LookPackEditRows { get { return lookPackEditRows ?? (lookPackEditRows = new ObservableCollection<PickRow>()); } }
        private string lookStatus;
        private LookPackFile lookEditing;
        public string LookPackEditTitle { get { return lookEditing != null ? lookEditing.Style.Name : Loc("LOC_KX_LookPacks", "Look packs"); } }

        private ICommand openLookPacksCmd, openStudioCmd;
        public ICommand OpenLookPacksCommand { get { return openLookPacksCmd ?? (openLookPacksCmd = new RelayCommand(_ => { PlaySound("select"); lookStatus = null; SetOptionsPage("lookpacks"); })); } }
        public ICommand OpenStudioCommand { get { return openStudioCmd ?? (openStudioCmd = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("studio"); })); } }

        private void BuildLookPackRows()
        {
            var rows = LookPackRows;
            rows.Clear();
            string cur = settings.LookPack ?? "";
            if (!string.IsNullOrEmpty(lookStatus)) rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "lpstatus", Text = lookStatus });
            rows.Add(new PickRow
            {
                Key = "lpstudio", Icon = "sliders", Text = Loc("LOC_KX_ThemeStudio", "Theme Studio"),
                Sub = Loc("LOC_KX_ThemeStudioSub2", "Change any part of the look you're using"),
                Command = new RelayCommand(_ => { PlaySound("select"); SetOptionsPage("studio"); })
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecBuiltIn", "BUILT IN") });
            foreach (string id in LookPackIds)
            {
                string pid = "builtin:" + id;
                var s = BuiltInLook(id);
                rows.Add(new PickRow
                {
                    Key = "pack:" + pid, Text = id == "kippix" ? "Kippix" : s.Name, Sub = LookPackDescription(id), IsSelected = cur == pid,
                    Picture = LookSwatch(s), Command = new RelayCommand(_ => UseLookPack(BuiltInLook(id), pid))
                });
            }
            var mine = ReadUserPacks();
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecYourPacks", "YOUR LOOK PACKS") });
            foreach (var p in mine)
            {
                var pack = p;
                rows.Add(new PickRow
                {
                    Key = "pack:" + p.Id, Text = p.Style.Name, IsSelected = cur == p.Id, Picture = LookSwatch(p.Style),
                    Sub = string.Format(Loc("LOC_KX_LookPackSub", "Saved {0} · X: share, update or delete"), p.When.ToString("d MMM yyyy", CultureInfo.CurrentCulture)),
                    Command = new RelayCommand(_ => UseLookPack(pack.Style, pack.Id))
                });
            }
            rows.Add(new PickRow
            {
                Key = "lpsave", Icon = "add", Text = Loc("LOC_KX_LookSaveAs", "Save this look as a pack"),
                Sub = string.Format(Loc("LOC_KX_LookSaveAsSub", "Now: {0}"), LookPackText), Command = new RelayCommand(_ => SaveLookAsPack())
            });
            rows.Add(new PickRow
            {
                // (r15: Upload, like Community › Yours › Upload your look; adding one from a file is on Community › Yours)
                Key = "lpupload", Icon = "upload", Text = Loc("LOC_KX_LookUploadPack", "Upload look pack"),
                Sub = string.Format(Loc("LOC_KX_ShareLookSub", "The look you're using, with a picture of it. Now: {0}"), LookPackText), Command = new RelayCommand(_ => ShareToCommunity("looks", null))
            });
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "lpwhere", Text = string.Format(Loc("LOC_KX_LookFolder", "Your packs are kept in {0}. A saved layout (and a community layout) has its look in it too."), LooksFolder) });
            rows.Add(CommunityNoteRow("looks", Loc("LOC_KX_CommunityNoteLooks", "Look packs other people have made, and sharing yours")));
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecReset", "RESET") });
            rows.Add(new PickRow
            {
                Key = "lpreset", Icon = "refresh", Text = Loc("LOC_KX_ResetThisPage", "Reset this page"), Sub = Loc("LOC_KX_ResetThisPageSub", "Resets this page to default"),
                Command = new RelayCommand(_ => AskLookReset())
            });
        }

        private void BuildLookPackEditRows()
        {
            var rows = LookPackEditRows;
            rows.Clear();
            var p = lookEditing;
            if (p == null) return;
            rows.Add(new PickRow { Key = "lpuse", Icon = "use", Text = Loc("LOC_KX_LookUse", "Use this look"), Picture = LookSwatch(p.Style), Command = new RelayCommand(_ => { UseLookPack(p.Style, p.Id); GoBackTo("lookpacks", "pack:" + p.Id); }) });
            rows.Add(new PickRow
            {
                Key = "lpupdate", Icon = "refresh", Text = Loc("LOC_KX_LookUpdate", "Save the look you have now over it"),
                Sub = string.Format(Loc("LOC_KX_LookSaveAsSub", "Now: {0}"), LookPackText),
                Command = new RelayCommand(_ =>
                {
                    string name = Path.GetFileNameWithoutExtension(p.File);
                    if (WriteLookPack(name, LookNow)) { settings.LookPack = p.Id; LookNow.Name = name; lookBase = LookNow.Copy(); SaveSettings(); lookStatus = string.Format(Loc("LOC_KX_LookSaved", "Saved as \"{0}\"."), name); PlaySound("select"); }
                    Notify("LookPackText");
                    GoBackTo("lookpacks", "pack:" + p.Id);
                })
            });
            rows.Add(new PickRow { Key = "lpshare", Icon = "download", Text = Loc("LOC_KX_LookShare", "Save a copy to..."), Sub = Loc("LOC_KX_LookShareSub", "To send it to someone (a USB stick, Downloads...)"), Command = new RelayCommand(_ => ShareLookPack(p)) });
            rows.Add(new PickRow
            {
                Key = "lpdelete", Icon = "trash", Text = Loc("LOC_KX_LookDelete", "Delete this pack"), Sub = Loc("LOC_KX_LookDeleteSub", "The look you're using stays as it is"),
                Command = new RelayCommand(_ =>
                {
                    try { File.Delete(p.File); } catch (Exception ex) { logger.Warn("Kippix: delete look pack: " + ex.Message); }
                    if (settings.LookPack == p.Id) { settings.LookPack = "builtin:kippix"; lookBase = null; SaveSettings(); }
                    PlaySound("back");
                    lookEditing = null;
                    lookStatus = string.Format(Loc("LOC_KX_LookDeleted", "Deleted \"{0}\"."), p.Style.Name);
                    Notify("LookPackText");
                    GoBackTo("lookpacks", "lpsave");
                })
            });
        }

        private void SaveLookAsPack()
        {
            OpenKeyboard(Loc("LOC_KX_LookName", "Name for this look"), "", typed =>
            {
                string name = SafeFileName(typed);
                if (name.Length == 0) return;
                if (WriteLookPack(name, LookNow))
                {
                    LookNow.Name = name;
                    settings.LookPack = "user:" + name;
                    lookBase = LookNow.Copy();
                    SaveSettings();
                    PlaySound("select");
                    lookStatus = string.Format(Loc("LOC_KX_LookSaved", "Saved as \"{0}\"."), name);
                }
                else lookStatus = Loc("LOC_KX_LookSaveFailed", "Couldn't save the look pack.");
                Notify("LookPackText");
                if (optionsPage == "lookpacks") BoardRowsAgain(BuildLookPackRows, "pack:user:" + name);
                else if (optionsPage == "studio") StudioRebuild("studio:save");
            }, null, null, 60);
        }

        private void ImportLookPack()
        {
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string back = optionsPage == "community" ? "community" : "lookpacks";   // (Settings › Community › Look packs: back there)
            OpenPicker(Loc("LOC_KX_LookImport", "Add a look pack you were sent"), false, new[] { ".json" }, Directory.Exists(start) ? start : "", back, file =>
            {
                string added = null;
                try
                {
                    var s = ReadLookFile(new JavaScriptSerializer(), file);
                    if (s == null) throw new InvalidDataException("not a look pack");
                    string name = SafeFileName(Path.GetFileNameWithoutExtension(file));
                    if (name.Length == 0) name = "Look";
                    string target = Path.Combine(LooksFolder, name + ".json");
                    bool same = string.Equals(Path.GetFullPath(target), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase);
                    for (int i = 2; !same && File.Exists(target); i++) { name = SafeFileName(Path.GetFileNameWithoutExtension(file)) + " (" + i + ")"; target = Path.Combine(LooksFolder, name + ".json"); }
                    if (!same) { Directory.CreateDirectory(LooksFolder); s.Name = name; File.WriteAllText(target, new JavaScriptSerializer().Serialize(s), Encoding.UTF8); }
                    added = name;
                    PlaySound("select");
                    lookStatus = string.Format(Loc("LOC_KX_LookAdded", "Added \"{0}\". Press it to use it."), name);
                }
                catch (Exception ex)
                {
                    lookStatus = Loc("LOC_KX_LookBad", "That file isn't a look pack.");
                    logger.Warn("Kippix: import look pack: " + ex.Message);
                }
                if (back == "community") { communityStatus = lookStatus; lookStatus = null; GoBackTo("community", "ck:import"); }
                else GoBackTo("lookpacks", added != null ? "pack:user:" + added : "lpimport");
            });
        }

        private void ShareLookPack(LookPackFile p)
        {
            if (p == null) return;
            OpenPickerFolder(Loc("LOC_KX_LayoutShareWhere", "Save a copy to"), "lookpack", dir =>
            {
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(p.File);
                    string target = Path.Combine(dir, baseName + ".json");
                    for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, baseName + " (" + i + ").json");
                    File.Copy(p.File, target);
                    PlaySound("select");
                    lookStatus = string.Format(Loc("LOC_KX_LayoutShared", "Saved a copy as {0}"), target);
                }
                catch (Exception ex) { lookStatus = Loc("LOC_KX_LayoutShareFailed", "Couldn't save a copy there."); logger.Warn("Kippix: share look pack: " + ex.Message); }
                GoBackTo("lookpacks", "pack:" + p.Id);
            });
        }

        /// <summary>Reset (Look packs, Theme Studio): back to the Kippix look. It asks first.</summary>
        private void AskLookReset()
        {
            Confirm(Loc("LOC_KX_LookResetTitle", "Back to the Kippix look?"),
                    Loc("LOC_KX_LookResetText", "Colours, fonts, cards, panels, the top bar and buttons go back to the theme's own. Your saved look packs stay."),
                    Loc("LOC_KX_Reset", "Reset"),
                    () =>
                    {
                        UseLookPack(BuiltInLook("kippix"), "builtin:kippix");
                        if (optionsPage == "studio") StudioRebuild("studio:reset");
                    });
        }

        /// <summary>From SetOptionsPage.</summary>
        private void LookOptionsOpened(string page)
        {
            Notify("IsOptionsLookPacks");
            Notify("IsOptionsLookPack");
            Notify("IsOptionsStudio");
            if (page != "studio") { StudioFlushSave(); studioColorKey = null; }
            if (page == "lookpacks") { BuildLookPackRows(); Notify("LookPackText"); }
            if (page == "lookpack") BuildLookPackEditRows();
            if (page == "look")
            {
                Notify("LookPackText");
                if (systemFonts == null) KippixShared.SafeWork.Queue(_ => SystemFontNames());   // (Theme Studio's font lists: read before they're needed)
            }
            if (page == "studio") StudioRebuild(null);
        }

        /// <summary>Buttons on these pages (from HandlePanelKey): X on one of your packs opens it; Theme Studio's own.</summary>
        private bool LookPanelKey(KeyEventArgs e, int step, int pageJump, bool x, bool y, bool back)
        {
            if (optionsPage == "studio") return StudioPanelKey(e, step, pageJump, x, y, back);
            if (optionsPage == "lookpacks" && x)
            {
                var b = Keyboard.FocusedElement as System.Windows.Controls.Primitives.ButtonBase;
                string key = b != null ? b.CommandParameter as string : null;
                if (key == null || !key.StartsWith("pack:user:", StringComparison.Ordinal)) return false;
                string id = key.Substring(5);
                lookEditing = ReadUserPacks().FirstOrDefault(p => p.Id == id);
                if (lookEditing == null) return false;
                PlaySound("select");
                SetOptionsPage("lookpack");
                Notify("LookPackEditTitle");
                return true;
            }
            return false;
        }
    }
}
