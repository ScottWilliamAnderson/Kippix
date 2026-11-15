using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.13) Getting started › Pick a look, in three columns of picture boxes (each a photo of that look and its name):
    /// LAYOUT (Row, List, Grid, Board, Manual board) on the left; picking one dims the others and shows its DESIGNS next to it
    /// (Row: Lower, Tall, Full Screen); picking a design shows its PRESETS next to that (Default and Kippix's). A layout without
    /// designs shows its presets straight away in the middle. Nothing is picked when the step opens. Each pick is put on as it's
    /// made, so Next after any of them keeps it; B steps back a column.
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly string[] TourPickLayouts = { "row", "list", "grid", "board", "manual" };
        private string tourPickLayout;
        private int tourPickDesign = -1, tourPickPreset = -1;
        private ObservableCollection<PickRow> tourCol1, tourCol2, tourCol3;

        public ObservableCollection<PickRow> TourCol1 { get { return tourCol1 ?? (tourCol1 = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> TourCol2 { get { return tourCol2 ?? (tourCol2 = new ObservableCollection<PickRow>()); } }
        public ObservableCollection<PickRow> TourCol3 { get { return tourCol3 ?? (tourCol3 = new ObservableCollection<PickRow>()); } }
        public bool HasTourCol2 { get { return IsTourTiles && TourCol2.Count > 0; } }
        public bool HasTourCol3 { get { return IsTourTiles && TourCol3.Count > 0; } }
        public string TourCol1Head { get { return Loc("LOC_KX_TourColLayout", "LAYOUT"); } }
        public string TourCol2Head { get { return tourPickLayout == "row" ? Loc("LOC_KX_TourColDesign", "DESIGN") : Loc("LOC_KX_TourColPreset", "PRESET"); } }
        public string TourCol3Head { get { return Loc("LOC_KX_TourColPreset", "PRESET"); } }

        private string tourLastPhoto;   // (the file the last TourStatePhoto came from: the box made next keeps it for X)
        private void TourPickReset() { tourPickLayout = null; tourPickDesign = -1; tourPickPreset = -1; tourZoomOn = false; tourWarn = null; TourBoxPreload(); }

        private void NotifyTourCols()
        {
            foreach (string n in new[] { "HasTourCol2", "HasTourCol3", "TourCol1Head", "TourCol2Head", "TourCol3Head" }) Notify(n);
        }

        private void BuildTourLookColumns()
        {
            // (0.99.17) the boxes are worked out fresh, then the columns only change where something did (the boxes already on
            // screen keep their place and pictures, so a new column coming in doesn't rebuild the others)
            var c1 = new List<PickRow>(); var c2 = new List<PickRow>(); var c3 = new List<PickRow>();
            TourZoomHide();
            if (TourLookMissing() == null) TourWarnHide();
            foreach (string k in TourPickLayouts)
            {
                string key = k;
                c1.Add(TourPickTile("pl:" + key, TourLookName(key), TourLayoutPhoto(key), tourPickLayout == key, tourPickLayout != null && tourPickLayout != key, () => TourPickLayoutDo(key)));
            }
            if (tourPickLayout != null)
            {
                if (tourPickLayout == "row")
                {
                    string[] names = { Loc("LOC_KX_RowLower", "Lower"), Loc("LOC_KX_RowTall", "Tall"), Loc("LOC_KX_RowFull", "Full Screen") };
                    for (int d = 0; d < 3; d++)
                    {
                        int dd = d;
                        c2.Add(TourPickTile("pd:" + d, names[d], TourStatePhoto("row" + d, ""), tourPickDesign == d, tourPickDesign >= 0 && tourPickDesign != d, () => TourPickDesignDo(dd)));
                    }
                    if (tourPickDesign >= 0) TourPresetTiles(c3, "row" + tourPickDesign);
                }
                else TourPresetTiles(c2, "l" + TourLookMode(tourPickLayout));
            }
            TourSyncCol(TourCol1, c1);
            TourSyncCol(TourCol2, c2);
            TourSyncCol(TourCol3, c3);
            NotifyTourCols();
        }

        /// <summary>A column to these boxes: the same boxes in the same order are only updated (picked / dimmed / picture).</summary>
        private static void TourSyncCol(ObservableCollection<PickRow> col, List<PickRow> want)
        {
            bool same = col.Count == want.Count;
            // (Grid and Board both have presets pp:0, pp:1, pp:3: the same key isn't the same box, the name tells them apart)
            for (int i = 0; same && i < want.Count; i++) same = col[i].Key == want[i].Key && col[i].Text == want[i].Text;
            if (!same)
            {
                col.Clear();
                foreach (var r in want) col.Add(r);
                return;
            }
            for (int i = 0; i < want.Count; i++)
            {
                var have = col[i];
                have.IsSelected = want[i].IsSelected;
                have.IsDimmed = want[i].IsDimmed;
                have.Command = want[i].Command;
                // the picture too: "Default" is in every layout, each with its own photo (one not loaded yet is null here, and put on when it's in)
                have.Picture = want[i].Picture;
            }
        }

        private void TourPresetTiles(List<PickRow> col, string slot)
        {
            var nums = new List<int> { 0 };
            nums.AddRange(PresetDesignNumbers(slot));
            foreach (int i in nums)
            {
                int n = i;
                string name = Loc("LOC_KX_PsDefault", "Default"), sub;
                if (n > 0) { try { PresetInfo(slot, n, out name, out sub); } catch (Exception) { name = "Kippix " + n; } }
                col.Add(TourPickTile("pp:" + n, name, TourStatePhoto(slot, n == 0 ? "" : "kx" + n), tourPickPreset == n, tourPickPreset >= 0 && tourPickPreset != n, () => TourPickPresetDo(n)));
            }
        }

        private PickRow TourPickTile(string key, string name, ImageSource pic, bool picked, bool dimmed, Action pick)
        {
            if (tourLastPhoto != null) tourTileFiles[key] = tourLastPhoto;
            return new PickRow { Key = key, Text = name, Picture = pic, IsSelected = picked, IsDimmed = dimmed, Glyph = TourGlyph("layout"), Command = new RelayCommand(_ => pick()) };
        }

        // ---------------- the photos ----------------

        private ImageSource TourLayoutPhoto(string key)
        {
            switch (key)
            {
                case "list": return TourStatePhoto("l2", "");
                case "grid": return TourStatePhoto("l1", "");
                case "board": return TourStatePhoto("l3", "");
                case "manual": return TourStatePhoto("l4", "");
            }
            return TourStatePhoto("row0", "");
        }

        /// <summary>The theme's photo of a slot and preset (Looks\row1_kx2.jpg), or that layout's drawing.</summary>
        private ImageSource TourStatePhoto(string slot, string chosen)
        {
            string dir = Path.Combine(ThemeDir, "Images", "Welcome");
            string photo = Path.Combine(dir, "Looks", slot + "_" + (chosen ?? "") + ".jpg");
            if (File.Exists(photo)) { tourLastPhoto = photo; return TourBoxPicture(photo); }
            string drawing = slot == "row1" ? "look-tall" : slot == "row2" ? "look-full" : slot == "l1" ? "look-grid" : slot == "l2" ? "look-list" : slot == "l3" || slot == "l4" ? "look-board" : "look-row";
            tourLastPhoto = Path.Combine(dir, drawing + ".png");
            return TourBoxPicture(tourLastPhoto);
        }

        // ---------------- (0.99.17) the boxes' pictures: decoded off the UI thread ----------------

        private readonly Dictionary<string, ImageSource> tourBoxPics = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> tourBoxLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int TourBoxPx = 400;

        /// <summary>The picture if it's in; otherwise null now, loaded in the background, and put on its boxes when it's in.</summary>
        private ImageSource TourBoxPicture(string file)
        {
            ImageSource img;
            if (tourBoxPics.TryGetValue(file, out img)) return img;
            TourBoxLoad(new[] { file });
            return null;
        }

        private void TourBoxLoad(IEnumerable<string> files)
        {
            var todo = files.Where(f => !tourBoxPics.ContainsKey(f) && tourBoxLoading.Add(f)).ToList();
            if (todo.Count == 0) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                foreach (string f in todo)
                {
                    ImageSource img = null;
                    try { img = File.Exists(f) ? Images.LoadUncached(f, TourBoxPx) : null; } catch (Exception) { }
                    string file = f;
                    var got = img;
                    OnUi(() =>
                    {
                        tourBoxLoading.Remove(file);
                        tourBoxPics[file] = got;
                        if (got == null) return;
                        foreach (var col in new[] { TourCol1, TourCol2, TourCol3 })
                            foreach (var r in col)
                            {
                                string rf;
                                if (r.Picture == null && r.Key != null && tourTileFiles.TryGetValue(r.Key, out rf) && string.Equals(rf, file, StringComparison.OrdinalIgnoreCase)) r.Picture = got;
                            }
                    });
                }
            });
        }

        /// <summary>Coming to Pick a look: every look's picture starts loading, so the columns have them ready.</summary>
        private void TourBoxPreload()
        {
            try
            {
                string dir = Path.Combine(ThemeDir, "Images", "Welcome");
                var files = new List<string>();
                // the layouts first, then the designs, then the presets
                foreach (string s in new[] { "row0_", "l2_", "l1_", "l3_", "l4_", "row1_", "row2_" }) files.Add(Path.Combine(dir, "Looks", s + ".jpg"));
                if (Directory.Exists(Path.Combine(dir, "Looks"))) files.AddRange(Directory.GetFiles(Path.Combine(dir, "Looks"), "*.jpg"));
                foreach (string d in new[] { "look-row", "look-tall", "look-full", "look-grid", "look-list", "look-board" }) files.Add(Path.Combine(dir, d + ".png"));
                TourBoxLoad(files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception) { }
        }

        // ---------------- picking ----------------

        private void TourPickLayoutDo(string key)
        {
            PlaySound("select");
            if (tourPickLayout != key) { tourPickLayout = key; tourPickDesign = -1; tourPickPreset = -1; }
            int m = TourLookMode(key);
            TourApplyLayout(m, m == 0 ? (settings.HomeLayoutMode == 0 ? settings.RowDesign : 0) : 0);
            tourCommitMode = settings.HomeLayoutMode;
            tourCommitDesign = settings.RowDesign;
            BuildTourLookColumns();
            TourColFocus("KX_TourCol2", TourCol2.FirstOrDefault());
        }

        private void TourPickDesignDo(int d)
        {
            PlaySound("select");
            if (tourPickDesign != d) { tourPickDesign = d; tourPickPreset = -1; }
            TourApplyLayout(0, d);
            tourCommitMode = 0;
            tourCommitDesign = d;
            BuildTourLookColumns();
            TourColFocus("KX_TourCol3", TourCol3.FirstOrDefault());
        }

        private void TourPickPresetDo(int n)
        {
            PlaySound("select");
            tourPickPreset = n;
            string slot = TourSlotNow();
            if (slot != null)
            {
                var st = SlotState(slot, false);
                string chosen = st != null ? st.Chosen : "";
                if (chosen != (n == 0 ? "" : "kx" + n)) UsePreset(slot, n);
                tourPresetWas.Remove(slot);
            }
            tourCommitMode = settings.HomeLayoutMode;
            tourCommitDesign = settings.RowDesign;
            logger.Info("Kippix: getting started: picked " + TourLayoutNow());
            BuildTourLookColumns();
            bool third = tourPickLayout == "row";
            var col = third ? TourCol3 : TourCol2;
            TourColFocus(third ? "KX_TourCol3" : "KX_TourCol2", col.FirstOrDefault(r => r.Key == "pp:" + n));
        }

        private void TourColFocus(string list, PickRow row)
        {
            if (row == null) return;
            ui.BeginInvoke(new Action(() => UiTools.FocusItem(list, row)), DispatcherPriority.ContextIdle);
        }

        /// <summary>B: back a column (the design, then the layout, unpicked). False when nothing's picked (B goes back a step).</summary>
        private bool TourPickBack()
        {
            if (openPanel != "welcome" || WelcomeSteps[welcomeStep] != "look" || tourPickLayout == null) return false;
            PlaySound("back");
            if (tourPickLayout == "row" && tourPickDesign >= 0)
            {
                int d = tourPickDesign;
                tourPickDesign = -1;
                tourPickPreset = -1;
                BuildTourLookColumns();
                TourColFocus("KX_TourCol2", TourCol2.FirstOrDefault(r => r.Key == "pd:" + d));
                return true;
            }
            string was = tourPickLayout;
            TourPickReset();
            BuildTourLookColumns();
            TourColFocus("KX_TourCol1", TourCol1.FirstOrDefault(r => r.Key == "pl:" + was));
            return true;
        }
    }
}

namespace KippixHelper
{
    /// <summary>
    /// (0.99.14) Pick a look: X on a box shows its photo big (most of the screen, stretching in from small), X / B closes it,
    /// and moving the highlight while it's open shows that box's photo. Next can't leave the step until a layout, its design
    /// (Row) and a preset are picked: a warning says what's still to pick.
    /// </summary>
    public partial class ThemeApi
    {
        private readonly System.Collections.Generic.Dictionary<string, string> tourTileFiles = new System.Collections.Generic.Dictionary<string, string>();
        private System.Windows.Media.ImageSource tourZoomImage;
        private bool tourZoomOn;
        private string tourWarn;
        private System.Windows.Threading.DispatcherTimer tourWarnTimer;

        public System.Windows.Media.ImageSource TourZoomImage { get { return tourZoomImage; } }
        public bool IsTourZoom { get { return tourZoomOn && IsTourTiles; } }
        public string TourZoomName { get; private set; }
        public string TourWarn { get { return tourWarn ?? ""; } }
        public bool HasTourWarn { get { return !string.IsNullOrEmpty(tourWarn) && IsTourTiles; } }

        /// <summary>X on Pick a look: the highlighted box's photo, big (again: closes it).</summary>
        private bool TourZoomToggle()
        {
            if (!IsTourTiles) return false;
            if (tourZoomOn) { TourZoomHide(); PlaySound("back"); return true; }
            var row = System.Windows.Input.Keyboard.FocusedElement is System.Windows.FrameworkElement
                ? ((System.Windows.FrameworkElement)System.Windows.Input.Keyboard.FocusedElement).DataContext as PickRow : null;
            if (row == null || row.Picture == null) { PlaySound("back"); return true; }
            PlaySound("select");
            TourZoomShow(row);
            return true;
        }

        private void TourZoomShow(PickRow row)
        {
            string file;
            System.Windows.Media.ImageSource big = null;
            if (row.Key != null && tourTileFiles.TryGetValue(row.Key, out file))
            {
                try { big = Images.LoadUncached(file, 1920); } catch (Exception) { }   // (0.99.16: the photos are 1920 x 1080 now)
            }
            tourZoomImage = big ?? row.Picture;
            TourZoomName = row.Text;
            tourZoomOn = true;
            foreach (string n in new[] { "TourZoomImage", "TourZoomName", "IsTourZoom" }) Notify(n);
        }

        private void TourZoomHide()
        {
            if (!tourZoomOn) return;
            tourZoomOn = false;
            Notify("IsTourZoom");
        }

        /// <summary>The highlight moved to another box while the photo's big: that one's photo.</summary>
        private void TourZoomFollow(PickRow row)
        {
            if (!tourZoomOn || row == null || row.Picture == null) return;
            TourZoomShow(row);
        }

        /// <summary>Next on Pick a look: what's still to pick, or null when it's all picked.</summary>
        private string TourLookMissing()
        {
            if (tourPickLayout == null) return Loc("LOC_KX_TourMissLayout", "Pick a layout, then its design and a preset, to carry on.");
            if (tourPickLayout == "row" && tourPickDesign < 0) return Loc("LOC_KX_TourMissDesign", "Pick a design for Row, then a preset, to carry on.");
            if (tourPickPreset < 0) return Loc("LOC_KX_TourMissPreset", "Pick a preset to carry on.");
            return null;
        }

        private void TourWarnShow(string text)
        {
            tourWarn = text;
            Notify("TourWarn");
            Notify("HasTourWarn");
            if (tourWarnTimer == null)
            {
                tourWarnTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                tourWarnTimer.Tick += (s, e) => { tourWarnTimer.Stop(); TourWarnHide(); };
            }
            tourWarnTimer.Stop();
            tourWarnTimer.Start();
        }

        private void TourWarnHide()
        {
            if (tourWarn == null) return;
            tourWarn = null;
            Notify("TourWarn");
            Notify("HasTourWarn");
        }

        // ---------------- (0.99.14) Up / Down: once per press ----------------

        private DateTime navMovedAt = DateTime.MinValue;
        private int navMovedDir;

        /// <summary>True when this Up / Down is a copy of the one just acted on (it's taken, nothing moves).</summary>
        private bool NavIsEcho(int dir)
        {
            if (dir == navMovedDir && (DateTime.Now - navMovedAt).TotalMilliseconds < 90) return true;
            return false;
        }

        private void NavMoved(int dir) { navMovedDir = dir; navMovedAt = DateTime.Now; }
    }
}
