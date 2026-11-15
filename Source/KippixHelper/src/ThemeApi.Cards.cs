using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Game card settings: card size (how many fit across the screen), card shape (Playnite's cover shape or square),
    /// and skipping the game page so A plays the game.
    /// Size and shape are set on the cover row's own panel, so Playnite's settings (and other themes) stay as they are.
    /// </summary>
    public partial class ThemeApi
    {
        // ---------------- size ----------------

        private const int CardSizeMin = 1, CardSizeMax = 7;   // size 1 = 10 cards across ... size 7 = 4 across
        private const double CardSizeTrackWidth = 480;

        private static int ColumnsForSize(int size) { return 11 - size; }

        public ICommand CycleCardShapeCommand { get; private set; }
        public ICommand CardSettingsResetCommand { get; private set; }

        private void InitCardCommands()
        {
            CycleCardShapeCommand = new RelayCommand(_ =>
            {
                if (RowFullActive) settings.CardShape = settings.CardShape == CardShapes.Wide ? CardShapes.Tall : CardShapes.Wide;   // (0.90) Full Screen: Tall or Wide
                else settings.CardShape = (settings.CardShape + 1) % CardShapes.TriDown;   // one Triangle (how they point: its own setting)
                SaveSettings();
                NotifyCardSettings();
                ApplyCardLayoutSoon();
                RefreshTileArtSoon();
                RebuildExtraRowsSoon();
                Notify("IsTriangleShape");
                try { if (carActive) { carGeoSig = null; CarouselUpdate(); CarRedress(); } } catch (Exception) { }   // (0.89: Tall / Full Screen's cards take the shape too; 0.90: Tall's size follows it)
                RefreshCardShapesNow();   // (0.90) Row's own cards: straight away, not on the next move
            });
            CardSettingsResetCommand = new RelayCommand(_ =>
            {
                // (0.91) back to Row first, so the layout being left keeps its own settings (presets) and the reset lands on Row
                if (settings.RowDesign != 0) { settings.RowDesign = 0; try { BgFollowLayout(); } catch (Exception) { } }
                settings.CardOffset = 0;
                settings.CardSize = -1;
                settings.CardShape = 0;
                settings.RowDesign = 0;
                settings.RowLoop = false;
                settings.RowFullCount = 6;
                settings.RowTallCount = 3;
                settings.CarTallW = 1; settings.CarTallH = 1; settings.CarTallFill = 0; settings.CarFullH = 1;
                settings.RowShowInfo = false;
                settings.FolderTileCount = 6;
                settings.BadgeFeatures = false;   // (r12) the cards' feature icons and store pill
                settings.BadgeStore = false;
                Notify("BadgeFeatures");
                Notify("BadgeStore");
                RefreshBadgesSoon();
                Notify("IsTriangleShape");
                Notify("FolderTileCountText");
                // and their size: the list's games and its height, as they come
                settings.ListTileH = 0; settings.ListTileW = 0; settings.ListInfoShare = 50; settings.ListShowInfo = true; Notify("ListShowInfo");
                ResetMasterSize("home.cards");
                SaveSettings();
                NotifyCards();
                NotifyCardSettings();
                ApplyMasterOffsets();
                HomeModeChanged();
                UpdateSliders();
            });
            InitPreviewCommands();
        }

        /// <summary>Playnite's own number of columns (Settings > Layout), used while the size is "Default".</summary>
        private int PlayniteColumns()
        {
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                object vm = w != null ? w.DataContext : null;
                object app = vm != null ? Prop(vm, "AppSettings") : null;
                object fs = app != null ? Prop(app, "Fullscreen") : null;
                object c = fs != null ? Prop(fs, "Columns") : null;
                if (c is int) return (int)c;
            }
            catch (Exception) { }
            return 6;
        }

        private static object Prop(object o, string name)
        {
            PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p != null ? p.GetValue(o, null) : null;
        }

        private int EffectiveCardSize
        {
            get
            {
                if (settings.CardSize > 0) return settings.CardSize;
                return Math.Max(CardSizeMin, Math.Min(CardSizeMax, 11 - PlayniteColumns()));
            }
        }

        public string CardSizeText
        {
            get
            {
                int cols = settings.CardSize > 0 ? ColumnsForSize(settings.CardSize) : PlayniteColumns();
                string across = string.Format(Loc("LOC_KX_CardsAcross", "{0} across"), cols);
                return settings.CardSize > 0 ? across : Loc("LOC_KX_CardsDefault", "Default") + " (" + across + ")";
            }
        }

        private double CardSizeFraction { get { return (double)(EffectiveCardSize - CardSizeMin) / (CardSizeMax - CardSizeMin); } }
        public double CardSizeFillWidth { get { return CardSizeFraction * CardSizeTrackWidth; } }
        public Thickness CardSizeKnobMargin { get { return new Thickness(CardSizeFraction * CardSizeTrackWidth - 12, 0, 0, 0); } }

        public string CardShapeText
        {
            get
            {
                if (RowFullActive) return ShapeName(settings.CardShape == CardShapes.Wide ? CardShapes.Wide : CardShapes.Tall);
                return CardShapes.IsTriangle(settings.CardShape) ? Loc("LOC_KX_ShapeTriangle", "Triangle") : ShapeName(settings.CardShape);
            }
        }

        private void NudgeCardSize(int delta)
        {
            int v = Math.Max(CardSizeMin, Math.Min(CardSizeMax, EffectiveCardSize + delta));
            if (v == settings.CardSize) return;
            settings.CardSize = v;
            SaveSettings();
            NotifyCardSettings();
            ApplyCardLayoutSoon();
        }

        private void NotifyCardSettings()
        {
            foreach (string n in new[] { "CardSizeText", "CardSizeFillWidth", "CardSizeKnobMargin", "CardShapeText", "IsTriangleShape", "IsNotTriangleShape", "TriArrangeText", "ShowCardSpacing", "ShowTriArrange", "ShowTriSpacing" }) Notify(n);
        }

        // ---------------- applying it to the cover row ----------------

        private double cardsHeight = 550;
        /// <summary>Height of the cover row: the cover plus room for the selected one to grow (550 at Playnite's usual 6 across).</summary>
        public double CardsHeight { get { return cardsHeight; } }

        // (0.96) room above the cards inside the list, so the highlighted one (it grows from its bottom edge) and its glow aren't
        // cut off at the top of the list (Row's Cinema, the first game in List...)
        private double cardsTopRoom;
        public Thickness CardsTopRoom { get { return new Thickness(0, cardsTopRoom, 0, 0); } }
        private double TopRoomFor(double itemH)
        {
            double grow = settings.Look != null && settings.Look.FocusScale > 100 ? settings.Look.FocusScale / 100.0 - 1 : 0.06;
            double room = Math.Round(Math.Max(0, itemH) * grow + 16);
            if (Math.Abs(room - cardsTopRoom) >= 1) { cardsTopRoom = room; Notify("CardsTopRoom"); }
            return room;
        }

        private DependencyObject cardPanel;
        private DependencyProperty colsProp, aspectProp;
        private BindingBase colsBinding, aspectBinding;   // Playnite's {Settings ...} bindings, when they're set directly on the panel
        private bool colsFromTemplate, aspectFromTemplate;  // ...or when they come from the panel template (then clearing our value brings them back)
        private bool colsSet, aspectSet;

        /// <summary>(0.90) Row Full Screen in use (Full Screen's panels take Tall or Wide only).</summary>
        private bool RowFullActive { get { return LayoutNow == 0 && CarDesign == 2 && !FolderTilesShown; } }

        /// <summary>(0.90) After the shape changes: Playnite's row lays its cards out again now and they're cut / dressed
        /// straight away (before, only on the next move).</summary>
        private void RefreshCardShapesNow()
        {
            if (ui == null) return;
            Action again = () =>
            {
                try
                {
                    var win = Application.Current != null ? Application.Current.MainWindow : null;
                    var list = win != null ? UiTools.FindByName(win, "PART_ListGameItems") as ListBox : null;
                    if (list != null)
                    {
                        var panel = UiTools.FindChild<VirtualizingPanel>(list);
                        if (panel != null)
                        {
                            panel.InvalidateMeasure();
                            panel.InvalidateArrange();
                            foreach (UIElement child in panel.Children) child.InvalidateMeasure();   // (only the cards Playnite has made)
                        }
                        list.UpdateLayout();
                    }
                    UpdateTileArt();
                }
                catch (Exception ex) { logger.Warn("Kippix: card shapes now: " + ex.Message); }
            };
            ui.BeginInvoke(again, DispatcherPriority.ApplicationIdle);
            var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(450) };
            t.Tick += (o, e) => { t.Stop(); again(); };
            t.Start();
        }

        private void ApplyCardLayoutSoon()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(ApplyCardLayout), DispatcherPriority.Loaded);
            ui.BeginInvoke(new Action(ApplyHomeLayout), DispatcherPriority.ContextIdle);
        }

        private void ApplyCardLayout()
        {
            try
            {
                var win = Application.Current != null ? Application.Current.MainWindow : null;
                var list = win != null ? PreviewList(win) : null;
                if (list == null) return;
                var panel = UiTools.FindChild<VirtualizingPanel>(list);
                if (panel == null || panel.GetType().Name != "FullscreenTilePanel") return;
                Type t = panel.GetType();

                if (!ReferenceEquals(panel, cardPanel))
                {
                    // a new panel (first time, or Playnite rebuilt the row): remember Playnite's own bindings
                    cardPanel = panel;
                    colsProp = StaticProperty(t, "ColumnsProperty");
                    aspectProp = StaticProperty(t, "ItemAspectRatioProperty");
                    colsBinding = colsProp != null ? BindingOperations.GetBindingBase(panel, colsProp) : null;
                    aspectBinding = aspectProp != null ? BindingOperations.GetBindingBase(panel, aspectProp) : null;
                    colsFromTemplate = colsProp != null && DependencyPropertyHelper.GetValueSource(panel, colsProp).BaseValueSource == BaseValueSource.ParentTemplate;
                    aspectFromTemplate = aspectProp != null && DependencyPropertyHelper.GetValueSource(panel, aspectProp).BaseValueSource == BaseValueSource.ParentTemplate;
                    colsSet = aspectSet = false;
                }
                if (colsProp == null || aspectProp == null) return;

                // size: number of cards across the screen
                if (settings.CardSize > 0)
                {
                    int c = ColumnsForSize(settings.CardSize);
                    if (!(panel.GetValue(colsProp) is int) || (int)panel.GetValue(colsProp) != c) panel.SetValue(colsProp, c);
                    colsSet = true;
                }
                else if (colsSet)
                {
                    Restore(panel, colsProp, colsFromTemplate ? null : colsBinding);
                    colsSet = false;
                }

                // shape: square, wide, or Playnite's cover shape (the list down one side has its own long, flat shape)
                int wantW = 0, wantH = 0;
                if (LayoutNow == 2) { aspectSet = true; }   // the list's own size: ApplyGrid sets the shape
                else CardShapes.Aspect(settings.CardShape, out wantW, out wantH);   // 0 x 0: Playnite's own cover shape
                if (wantW > 0 && CardShapes.IsTriangle(settings.CardShape))
                {
                    // triangles: each card's slot is one step along (half a triangle when they take turns, plus the spacing);
                    // the triangle itself is drawn wider than its slot (ApplyCardSpacingTo), so they sit pushed together
                    wantW = (int)Math.Round(1500 * TriStep);
                    wantH = 1300;
                }
                if (LayoutNow == 2) { }
                else if (wantW > 0)
                {
                    int aw, ah;
                    if (!ReadAspect(panel.GetValue(aspectProp), out aw, out ah) || aw * wantH != ah * wantW)
                        panel.SetValue(aspectProp, Activator.CreateInstance(aspectProp.PropertyType, new object[] { wantW, wantH }));
                    aspectSet = true;
                }
                else if (aspectSet)
                {
                    Restore(panel, aspectProp, aspectFromTemplate ? null : aspectBinding);
                    aspectSet = false;
                }

                // the grid (Options › Home settings › Rows and grid) sizes the list itself
                {
                    int gw, gh;
                    ReadAspect(panel.GetValue(aspectProp), out gw, out gh);
                    if (ApplyGrid(panel, t, list, gw, gh)) return;
                }
                // row height follows the cover height, so bigger cards have room and smaller ones stay in one row
                object horizontal = Prop(panel, "UseHorizontalLayout");
                if (horizontal is bool && !(bool)horizontal) return;
                int cols = panel.GetValue(colsProp) is int ? (int)panel.GetValue(colsProp) : 6;
                int w, h;
                if (cols <= 0 || !ReadAspect(panel.GetValue(aspectProp), out w, out h) || w <= 0 || h <= 0) return;
                var scroll = panel as IScrollInfo;
                double vw = scroll != null && scroll.ViewportWidth > 0 ? scroll.ViewportWidth : (list.ActualWidth > 0 ? list.ActualWidth : 1920);
                double cover = vw / (cols + 0.5) * h / w;
                double rowHeight = Math.Round(Math.Min(cover + 156, cover * 2 - 2));
                SetRowTileSize(Math.Round(vw / (cols + 0.5)) - 16, Math.Round(cover) - 16);   // the extra rows match the cards
                rowHeight = Math.Max(260, Math.Min(1000, rowHeight));
                rowHeight = Math.Min(rowHeight, Math.Round(cover * 2 - 2));   // (0.96) never room for a second row (short, wide cards fitted two)
                rowHeight += TopRoomFor(cover);
                if (Math.Abs(rowHeight - cardsHeight) >= 1)
                {
                    cardsHeight = rowHeight;
                    Notify("CardsHeight");
                    ApplyHomeLayoutSoon();
                }
            }
            catch (Exception ex) { logger.Warn(ex, "Kippix: card layout failed"); }
        }

        private static DependencyProperty StaticProperty(Type t, string name)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            return f != null ? f.GetValue(null) as DependencyProperty : null;
        }

        private static void Restore(DependencyObject o, DependencyProperty p, BindingBase b)
        {
            if (b != null) BindingOperations.SetBinding(o, p, b);
            else o.ClearValue(p);
        }

        private static bool ReadAspect(object ratio, out int w, out int h)
        {
            w = h = 0;
            if (ratio == null) return false;
            object ow = Prop(ratio, "Width"), oh = Prop(ratio, "Height");
            if (!(ow is int) || !(oh is int)) return false;
            w = (int)ow;
            h = (int)oh;
            return true;
        }

        // ---------------- skip the game page ----------------

        public bool SkipGamePage
        {
            get { return settings.SkipGamePage; }
            set { if (SetSetting(ref settings.SkipGamePage, value)) { Notify("HintAText"); Notify("ShowXPlayHint"); Notify("GamePageAvailable"); } }
        }

        /// <summary>The game page (and its settings) can be used: false when it's skipped.</summary>
        public bool GamePageAvailable { get { return !settings.SkipGamePage; } }

        /// <summary>What A does on the home screen: opens the game page, or plays the game.</summary>
        public string HintAText { get { return InRunningRow ? Loc("LOC_KX_RunSwitch", "Switch to") : settings.SkipGamePage ? Loc("LOC_KX_Play", "Play") : Loc("LOC_KX_Details", "Details"); } }
        /// <summary>(issue #11) What X does: plays, or closes the app on a Running now tile.</summary>
        public string HintXText { get { return InRunningRow ? Loc("LOC_KX_RunClose", "Close") : Loc("LOC_KX_Play", "Play"); } }
        /// <summary>The separate "X Play" hint, not needed when A already plays.</summary>
        public bool ShowXPlayHint { get { return !settings.SkipGamePage || InRunningRow; } }

        /// <summary>A (or X) on a game card plays it (or installs it) when the game page is skipped. Runs before Playnite's own key bindings on the row.</summary>
        private void OnCardKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!settings.SkipGamePage || e.Handled || openPanel != null || detailsOpen) return;
                var item = sender as ListBoxItem;
                if (item == null || !item.IsKeyboardFocusWithin) return;
                var owner = ItemsControl.ItemsControlFromItemContainer(item);
                if (owner == null || owner.Name != "PART_ListGameItems") return;

                bool play;
                if (e.GetType().Name == "GameControllerInputEventArgs")
                {
                    PropertyInfo button = e.GetType().GetProperty("Button");
                    PropertyInfo state = e.GetType().GetProperty("ButtonState");
                    object b = button != null ? button.GetValue(e, null) : null;
                    object st = state != null ? state.GetValue(e, null) : null;
                    string name = b != null ? b.ToString() : "";
                    play = (st == null || st.ToString() == "Pressed") && (name == "A" || name == "X");
                }
                else play = e.Key == Key.A || e.Key == Key.X;
                if (!play) return;

                e.Handled = true;
                if (TrySwitchInsteadOfPlay(SelectedGameId())) return;   // (issue #11) running already: its window comes to the front
                PausePreviewFor(10);
                RunMainCommand("ActivateSelectedCommand");
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: couldn't start the game"); }
        }
    }
}
