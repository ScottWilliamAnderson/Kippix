using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KippixHelper
{
    /// <summary>
    /// Home settings › BACKGROUND: the game's art, its trailer (muted), a slideshow, the trailer preview, a plain colour or your
    /// own picture, and the fade to black over it (from which edge, and how far it reaches, or none). The page (r14 order):
    /// BACKGROUND (the choice and what belongs to it), FADE TO BLACK, MOTION (Background zoom, Depth), ANIMATED ART and RESET
    /// (those two built in code: BgAnimRows, ThemeApi.AnimatedArt.cs). Also the colour choice the game page cards use for Solid.
    /// </summary>
    public partial class ThemeApi
    {

        public ICommand CycleHomeBgCommand { get; private set; }
        public ICommand ChooseHomeBgColorCommand { get; private set; }
        public ICommand CycleHomeFadeDirCommand { get; private set; }
        public ICommand CycleHomeFadeReachCommand { get; private set; }
        public ICommand ChooseHomeBgImageCommand { get; private set; }

        private void InitHomeBackground()
        {
            PlaceRows = new System.Collections.ObjectModel.ObservableCollection<PickRow>();
            CycleHomeBgCommand = new RelayCommand(_ => { SetHomeBgMode(NextHomeBgMode(settings.HomeBgMode)); PlaySound("move"); });
            ChooseHomeBgImageCommand = new RelayCommand(_ => ChooseHomeBgImage());
            ChooseHomeBgColorCommand = new RelayCommand(_ => OpenThemeColor("homebg", settings.HomeBgColor, "background", "homebgcolor"));
            CycleHomeFadeDirCommand = new RelayCommand(_ => { settings.HomeFadeDir = (settings.HomeFadeDir + 1) % 5; SaveSettings(); PlaySound("move"); NotifyHomeBackground(); });
            CycleHomeFadeReachCommand = new RelayCommand(_ => { settings.HomeFadeReach = 44; SaveSettings(); PlaySound("move"); NotifyHomeBackground(); });   // A on the slider: back to 44 %
            MoveHomeBgCommand = new RelayCommand(_ => BgMoveStart());   // (r15: Move the picture, ThemeApi.BgMove.cs)
        }

        private int previewBeforeBg;   // the trailer preview before Background › The game's trailer took it over
        private int bgBeforePreview;   // (r15) the background before Trailer preview › In the background took it over (it comes back after)

        /// <summary>
        /// Trailer preview › Off / In the card / In the background (r15). "In the background" IS Background › Trailer preview, so
        /// it goes through SetHomeBgMode both ways: the Background page shows it chosen, the live background stops / starts, and
        /// leaving it gives the background you had back (and the other way: leaving it on the Background page gives the preview
        /// its own place back). The delay, sound and streaming rows on both pages are the same settings.
        /// </summary>
        private void SetPreviewMode(int mode)
        {
            mode = Math.Max(0, Math.Min(2, mode));
            if (mode == 2 && settings.HomeBgMode != 2) { SetHomeBgMode(2); return; }   // (sets PreviewMode 2, remembers the background)
            if (mode != 2 && settings.HomeBgMode == 2)
            {
                previewBeforeBg = mode;   // SetHomeBgMode puts this back as the preview's place
                int back = bgBeforePreview != 2 && bgBeforePreview >= 0 && bgBeforePreview <= 6 ? bgBeforePreview : 0;
                if (back == 3 && string.IsNullOrEmpty(settings.HomeBgImage)) back = 0;
                SetHomeBgMode(back);
                return;
            }
            StopPreview();
            settings.PreviewMode = mode;
            SaveSettings();
            NotifyHomeBackground();
            NotifyPreview();
            previewGame = Guid.Empty;
            EnsurePreviewTimer();
        }

        /// <summary>A on Background: the art, the trailer (muted, live), the slideshow, the trailer preview's own trailer, a colour, your picture.</summary>
        private static readonly int[] HomeBgOrder = { 0, 6, 4, 5, 2, 1, 3 };   // (0.86: 6 the game's tile, after its art)
        private static int NextHomeBgMode(int now)
        {
            int i = Array.IndexOf(HomeBgOrder, now);
            return HomeBgOrder[(i + 1) % HomeBgOrder.Length];
        }

        /// <summary>The game's art / a plain colour / the game's trailer (the trailer preview "In the background", over the art).</summary>
        private void SetHomeBgMode(int mode)
        {
            int was = settings.HomeBgMode;
            settings.HomeBgMode = mode;
            if (mode == 2 && was != 2) bgBeforePreview = was;   // (r15: Trailer preview › Off / In the card later gives this back)
            if (mode == 2 || was == 2)
            {
                StopPreview();
                if (mode == 2) { if (settings.PreviewMode != 2) previewBeforeBg = settings.PreviewMode; settings.PreviewMode = 2; }
                else if (settings.PreviewMode == 2) settings.PreviewMode = previewBeforeBg;   // "in the card" comes back if it was that
                previewGame = Guid.Empty;
                EnsurePreviewTimer();
                NotifyPreview();
            }
            SaveSettings();
            NotifyHomeBackground();
            try { TileBgSelected(SelectedGameNow()); } catch (Exception) { }   // (0.88) The game's tile: the selected game's cover now
            // Trailer (muted) / Slideshow: start (or stop) the live background (ThemeApi.LiveBackground.cs)
            try { StopLiveMedia(true); liveGame = Guid.Empty; EnsureLiveTimer(); } catch (Exception) { }
            if (openPanel == "options" && optionsPage == "background") BuildBgAnimRows();   // ("Animate the background too" only shows over the art)
        }

        /// <summary>Left / Right on the Background page's rows: the background, the fade's edge, and the animated art rows. True if it was one.</summary>
        private bool BgPageNudge(string key, int dir)
        {
            if (key == "homebg")
            {
                int i = Array.IndexOf(HomeBgOrder, settings.HomeBgMode);
                SetHomeBgMode(HomeBgOrder[(i + dir + HomeBgOrder.Length) % HomeBgOrder.Length]);
                PlaySound("move");
                return true;
            }
            if (key == "homebgtilesize") { NudgeTileBgSize(dir); return true; }   // (0.87)
            if (key == "homefade")
            {
                settings.HomeFadeDir = (settings.HomeFadeDir + dir + 5) % 5;
                SaveSettings();
                PlaySound("move");
                NotifyHomeBackground();
                return true;
            }
            return AnimArtNudge(key, dir);
        }

        private void NotifyHomeBackground()
        {
            foreach (string n in new[] { "HomeArtShown", "BcBgOnHome", "HomeTileShown", "IsHomeBgTile", "HomeTileBgHeight", "HomeTileBgScaleText", "HomeSolidShown", "HomeCustomShown", "HomeCustomBg", "IsHomeBgCustom", "HomeBgImageText", "HomeSolidBrush", "HomeFadeBrush", "HomeBgText", "HomeBgColorText", "HomeBgColorSwatch",
                                         "HomeFadeDirText", "HomeFadeReachText", "IsHomeBgColor", "IsHomeFadeOn", "HomeBgSummary", "IsHomeBgLive", "IsHomeBgLiveTrailer", "IsHomeBgSlides", "LiveSlideWeb", "BgZoomShown",
                                         "BgMoveShown", "HomeBgOffsetText", "PreviewModeText", "IsPreviewBackground", "PreviewEnabled", "HomeBgSettingsShown", "HomeBgOffNoteShown",
                                         "HomeVideoRowShown", "HomeVideoDownloadShown" })
                Notify(n);
            UpdateSliders();
            try { ApplyHomeBgOffset(); } catch (Exception) { }   // (r15: Move the picture: none over a plain colour)
        }

        /// <summary>Folder's tiles: plain black behind them (no game's art, colour or picture); a folder's games have the background as set.</summary>
        // (0.90) Row › Full Screen covers the whole screen too: no picture or trailer behind it, just black
        private bool HomeBgBlack { get { return (FolderTilesShown || RowFullActive) && IsFullscreen; } }
        /// <summary>(0.90) Home settings › Background: hidden with Row › Full Screen (nothing shows behind it), a note instead.</summary>
        /// <summary>Folder's tiles: black, and the accent isn't the hidden game's (Full Screen's games are on show, so it keeps theirs).</summary>
        private bool FolderBgBlack { get { return FolderTilesShown && IsFullscreen; } }
        public bool HomeBgSettingsShown { get { return !RowFullActive; } }
        public bool HomeBgOffNoteShown { get { return RowFullActive; } }
        public bool HomeArtShown { get { return settings.HomeBgMode != 1 && settings.HomeBgMode != 6 && !HomeCustomShown && !HomeBgBlack; } }
        /// <summary>(0.86) Background › The game's tile: its cover fills the background, softened (a cover is small for a whole screen).</summary>
        public bool HomeTileShown { get { return settings.HomeBgMode == 6 && !HomeBgBlack; } }
        public bool HomeSolidShown { get { return settings.HomeBgMode == 1 || settings.HomeBgMode == 6 || HomeBgBlack; } }   // (the tile sits on the colour)
        public bool IsHomeBgColor { get { return settings.HomeBgMode == 1 || settings.HomeBgMode == 6; } }
        // (0.87) The game's tile: the cover as it is (fitted, not zoomed), on the plain colour; Size and Move the picture place it
        public bool IsHomeBgTile { get { return settings.HomeBgMode == 6; } }
        public double HomeTileBgHeight { get { return Math.Round(1080 * Math.Max(0.3, Math.Min(1.0, settings.HomeTileBgScale))); } }
        public string HomeTileBgScaleText { get { return string.Format(Loc("LOC_KX_TileBgSize", "{0}% of the screen's height"), (int)Math.Round(Math.Max(0.3, Math.Min(1.0, settings.HomeTileBgScale)) * 100)); } }
        private static readonly double[] TileBgSizes = { 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
        private void NudgeTileBgSize(int dir)
        {
            int i = 0;
            for (int k = 0; k < TileBgSizes.Length; k++) if (Math.Abs(TileBgSizes[k] - settings.HomeTileBgScale) < Math.Abs(TileBgSizes[i] - settings.HomeTileBgScale)) i = k;
            i = dir == 0 ? (i + 1) % TileBgSizes.Length : Math.Max(0, Math.Min(TileBgSizes.Length - 1, i + dir));
            settings.HomeTileBgScale = TileBgSizes[i];
            SaveSettings();
            PlaySound("move");
            Notify("HomeTileBgHeight");
            Notify("HomeTileBgScaleText");
        }
        // (0.88) the cover for The game's tile comes from here (Playnite's own SelectedGame binding gave nothing there)
        private ImageSource homeTileBgImage;
        private int tileBgGen;
        public ImageSource HomeTileBgImage { get { return homeTileBgImage; } }

        private void TileBgSelected(Playnite.SDK.Models.Game game)
        {
            if (settings.HomeBgMode != 6) { if (homeTileBgImage != null) { homeTileBgImage = null; Notify("HomeTileBgImage"); } return; }
            if (game == null) return;
            string path = null;
            try { if (!string.IsNullOrEmpty(game.CoverImage)) path = game.CoverImage.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? game.CoverImage : api.Database.GetFullFilePath(game.CoverImage); } catch (Exception) { }
            if (string.IsNullOrEmpty(path)) { homeTileBgImage = null; Notify("HomeTileBgImage"); return; }
            int gen = ++tileBgGen;
            int px = (int)Math.Min(2000, Math.Max(600, HomeTileBgHeight * 0.75 * 1.5));
            KippixShared.SafeWork.Queue(_ =>
            {
                ImageSource img = null;
                try { img = Images.Load(path, px); } catch (Exception) { }
                OnUi(() => { if (gen != tileBgGen) return; homeTileBgImage = img; Notify("HomeTileBgImage"); });
            });
        }

        private System.Windows.Input.ICommand cycTileBgSize;
        public System.Windows.Input.ICommand CycleTileBgSizeCommand { get { return cycTileBgSize ?? (cycTileBgSize = new RelayCommand(_ => NudgeTileBgSize(0))); } }

        // ---------------- your own picture ----------------

        private ImageSource homeCustomBg;
        private string homeCustomBgFor;

        public bool IsHomeBgCustom { get { return settings.HomeBgMode == 3; } }
        /// <summary>Your own picture, when it's chosen and can be read (otherwise the game's art shows, as before).</summary>
        public bool HomeCustomShown { get { return settings.HomeBgMode == 3 && !HomeBgBlack && HomeCustomBg != null; } }
        public ImageSource HomeCustomBg
        {
            get
            {
                string path = settings.HomeBgImage ?? "";
                if (path != homeCustomBgFor)
                {
                    homeCustomBgFor = path;
                    homeCustomBg = null;
                    try
                    {
                        if (path.Length > 0 && File.Exists(path))
                            homeCustomBg = Images.LoadUncached(path, (int)Math.Min(3840, Math.Max(1920, SystemParameters.PrimaryScreenWidth)));
                    }
                    catch (Exception ex) { logger.Warn("Kippix: own background: " + ex.Message); }
                }
                return homeCustomBg;
            }
        }
        public string HomeBgImageText
        {
            get
            {
                string p = settings.HomeBgImage ?? "";
                if (p.Length == 0) return Loc("LOC_KX_BgImageNone", "None yet");
                if (!File.Exists(p)) return Loc("LOC_KX_BgImageMissing", "The picture can't be found");
                return Path.GetFileName(p);
            }
        }

        private string HomeBgFolder { get { return Path.Combine(plugin.GetPluginUserDataPath(), "backgrounds"); } }

        /// <summary>Background › Your own picture › Picture: pick one from your PC; a copy is kept in the add-on's folder, so moving or deleting the original doesn't lose it.</summary>
        private void ChooseHomeBgImage()
        {
            string start = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            try { string cur = settings.HomeBgImage ?? ""; if (cur.Length > 0 && !cur.StartsWith(HomeBgFolder, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.GetDirectoryName(cur))) start = Path.GetDirectoryName(cur); } catch (Exception) { }
            PlaySound("select");
            OpenPicker(Loc("LOC_KX_BgImageChoose", "Choose a background picture"), false, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }, start, "background", file =>
            {
                try
                {
                    Directory.CreateDirectory(HomeBgFolder);
                    string dest = Path.Combine(HomeBgFolder, "background-" + DateTime.Now.Ticks + Path.GetExtension(file).ToLowerInvariant());
                    File.Copy(file, dest, true);
                    string old = settings.HomeBgImage ?? "";
                    settings.HomeBgImage = dest;
                    if (settings.HomeBgMode != 3) SetHomeBgMode(3); else SaveSettings();
                    // the previous copy goes, unless a saved layout or profile still uses it
                    try { if (old.Length > 0 && old.StartsWith(HomeBgFolder, StringComparison.OrdinalIgnoreCase) && !BgImageInUseElsewhere(old)) File.Delete(old); } catch (Exception) { }
                }
                catch (Exception ex)
                {
                    logger.Warn("Kippix: own background: " + ex.Message);
                    try { ShowNotice(Loc("LOC_KX_BgImageFailedTitle", "BACKGROUND"), Loc("LOC_KX_BgImageFailed", "That picture couldn't be used"), Path.GetFileName(file), "", null); } catch (Exception) { }
                }
                NotifyHomeBackground();
                GoBackTo("background", "homebgimage");
            });
        }

        /// <summary>A saved layout or profile file mentions this picture (so it's kept).</summary>
        private bool BgImageInUseElsewhere(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                foreach (string dir in new[] { Path.Combine(plugin.GetPluginUserDataPath(), "profiles"), LayoutsFolder })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                        if (File.ReadAllText(f).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch (Exception) { return true; }
            return false;
        }
        public bool IsOptionsBackground { get { return optionsPage == "background"; } }
        /// <summary>The Background row on Home settings: what it is now.</summary>
        public string HomeBgSummary
        {
            get
            {
                string t = HomeBgText + "  ·  " + (settings.HomeFadeDir == 4 ? Loc("LOC_KX_NoFade", "no fade") : HomeFadeDirText.ToLowerInvariant() + ", " + settings.HomeFadeReach + "%");
                // r14: animated art lives on this page now
                t += "  ·  " + string.Format(Loc("LOC_KX_BgSumAnim", "animated art: {0}"), AnimArtSummary.ToLowerInvariant());
                return t;
            }
        }
        public bool IsHomeFadeOn { get { return settings.HomeFadeDir != 4; } }

        // ---------------- the game's trailer behind the home screen (the trailer preview, "In the background") ----------------

        public bool IsPreviewBackground { get { return settings.PreviewMode == 2; } }
        public string PreviewSoundText { get { return settings.PreviewSound ? Loc("LOC_KX_WithSound", "With sound") : Loc("LOC_KX_Muted", "Muted"); } }
        public Brush HomeSolidBrush { get { return HomeBgBlack ? Brushes.Black : HomeBgColorSwatch; } }
        /// <summary>The plain colour you chose (the settings' swatch: never Folder's black).</summary>
        public Brush HomeBgColorSwatch { get { return Frozen(ParseColor(settings.HomeBgColor) ?? Color.FromRgb(0x0E, 0x10, 0x14)); } }
        public string HomeBgText
        {
            get
            {
                if (settings.HomeBgMode == 4) return Loc("LOC_KX_BgLiveTrailer", "Trailer (muted)");
                if (settings.HomeBgMode == 5) return Loc("LOC_KX_BgSlideshow", "Slideshow");
                if (settings.HomeBgMode == 6) return Loc("LOC_KX_BgTile", "The game's tile");
                return settings.HomeBgMode == 3 ? Loc("LOC_KX_BgOwnPicture", "Your own picture")
                     : settings.HomeBgMode == 1 ? Loc("LOC_KX_BgColour", "A plain colour")
                     : settings.HomeBgMode == 2 ? Loc("LOC_KX_BgTrailerPreview", "Trailer preview")
                     : Loc("LOC_KX_BgArt", "The game's art");
            }
        }
        public string HomeBgColorText { get { return ColorName(settings.HomeBgColor); } }
        public string HomeFadeDirText
        {
            get
            {
                switch (settings.HomeFadeDir)
                {
                    case 1: return Loc("LOC_KX_FadeFromTop", "From the top");
                    case 2: return Loc("LOC_KX_FadeFromLeft", "From the left");
                    case 3: return Loc("LOC_KX_FadeFromRight", "From the right");
                    case 4: return Loc("LOC_KX_FadeNone", "None");
                }
                return Loc("LOC_KX_FadeFromBottom", "From the bottom");
            }
        }
        public string HomeFadeReachText { get { return string.Format(Loc("LOC_KX_FadeReach", "{0}% of the screen"), settings.HomeFadeReach); } }

        /// <summary>
        /// The fade to black, as one brush over the whole screen. As it comes (from the bottom, 44%): clear down to 56% of
        /// the screen, black from 76%; the reach moves both, the edge turns it.
        /// </summary>
        public Brush HomeFadeBrush
        {
            get
            {
                if (settings.HomeFadeDir == 4) return Brushes.Transparent;
                double r = Math.Max(0.05, Math.Min(1, settings.HomeFadeReach / 100.0));
                double s = 1 - r, len = 0.20 * (r / 0.44);
                if (s + len > 1) len = 1 - s;
                Point a, b;
                switch (settings.HomeFadeDir)
                {
                    case 1: a = new Point(0, 1); b = new Point(0, 0); break;   // black at the top
                    case 2: a = new Point(1, 0); b = new Point(0, 0); break;   // black on the left
                    case 3: a = new Point(0, 0); b = new Point(1, 0); break;   // black on the right
                    default: a = new Point(0, 0); b = new Point(0, 1); break;  // black at the bottom
                }
                var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), s));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0, 0, 0), s + len * 0.3));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0xA8, 0, 0, 0), s + len * 0.65));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), s + len));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 1));
                g.Freeze();
                return g;
            }
        }

        private string ColorName(string hex)
        {
            var c = ParseColor(hex);
            if (!c.HasValue) return "";
            string h = ColorHex(c.Value);
            var p = WidgetPalette.FirstOrDefault(x => x[0] == h);
            return p != null ? Loc("LOC_KX_Col" + p[1], p[1]) : h;
        }

        // ---------------- choosing a colour (the widgets' colour page, for the theme's own things) ----------------

        private string colorTarget;          // null: a widget's colour; "homebg"; "card:<part>"
        private string colorBackPage, colorBackKey;

        private void OpenThemeColor(string target, string current, string backPage, string backKey)
        {
            colorTarget = target;
            colorBackPage = backPage;
            colorBackKey = backKey;
            var cur = ParseColor(current);
            WidgetChoices.Clear();
            foreach (var p in WidgetPalette)
            {
                string hex = p[0];
                WidgetChoices.Add(new ChoiceRow
                {
                    Text = Loc("LOC_KX_Col" + p[1], p[1]), Swatch = Frozen(ParseColor(hex).Value),
                    IsSelected = cur.HasValue && ColorHex(cur.Value) == hex,
                    Command = new RelayCommand(_ => ChooseThemeColor(hex))
                });
            }
            if (cur.HasValue && !WidgetPalette.Any(p => p[0] == ColorHex(cur.Value)))
            {
                string hex = ColorHex(cur.Value);
                WidgetChoices.Insert(0, new ChoiceRow { Text = hex, Swatch = Frozen(cur.Value), IsSelected = true, Command = new RelayCommand(_ => ChooseThemeColor(hex)) });
            }
            OpenChoicePage("back");
        }

        private void ChooseThemeColor(string hex)
        {
            PlaySound("select");
            string target = colorTarget;
            if (target == "homebg")
            {
                settings.HomeBgColor = hex;
                // through SetHomeBgMode: leaving The game's trailer gives the trailer preview back its own place
                if (settings.HomeBgMode != 1) SetHomeBgMode(1); else NotifyHomeBackground();
            }
            else if (target != null && target.StartsWith("card:", StringComparison.Ordinal))
            {
                string part = target.Substring(5);
                if (settings.DetCardColors == null) settings.DetCardColors = new Dictionary<string, string>();
                settings.DetCardColors[part] = hex;
                if (settings.DetCardLooks == null) settings.DetCardLooks = new Dictionary<string, int>();
                settings.DetCardLooks[part] = 1;   // picked a colour: it's solid
                ApplyDetCardLooks();
            }
            SaveSettings();
            string page = colorBackPage, key = colorBackKey;
            colorTarget = null;
            if (page == "detpart") BuildDetPartRows();
            GoBackTo(page ?? "", key);
        }

        /// <summary>Typed colour code, for the theme's own colours.</summary>
        private bool TypeThemeColor()
        {
            if (colorTarget == null) return false;
            string now = colorTarget == "homebg" ? settings.HomeBgColor : DetCardColor(colorTarget.Substring(5));
            OpenKeyboard(Loc("LOC_KX_WgTypeColorHint", "A colour code like #3D8BFD (6 digits), or a colour name like teal"), now ?? "", typed =>
            {
                var c = ParseColor(typed);
                if (!c.HasValue) { PlaySound("back"); return; }
                ChooseThemeColor(ColorHex(c.Value));
            }, null, null, 30);
            return true;
        }

        private string DetCardColor(string part)
        {
            string v;
            return settings.DetCardColors != null && settings.DetCardColors.TryGetValue(part, out v) ? v : "#15171C";
        }
    }
}
