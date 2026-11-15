using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace KippixHelper
{
    /// <summary>
    /// Game card settings › Card spacing: the gap between the cards (one row, grid, list, the extra rows and the Board's tiles).
    /// Playnite gives each card a margin from its own settings; a chosen spacing replaces it on each card (in code: SetCurrentValue,
    /// so Playnite's binding stays and "as it comes" puts its value back).
    /// </summary>
    public partial class ThemeApi
    {
        private SliderModel spacingSlider;
        public SliderModel SpacingSlider { get { return spacingSlider ?? (spacingSlider = new SliderModel()); } }
        private bool spacingWasSet;
        private double measuredSpacing = -1;          // Playnite's own gap (both sides of a card)
        private Thickness? playniteCardMargin;

        private int CardSpacingNow
        {
            get { return settings.CardSpacing >= 0 ? settings.CardSpacing : measuredSpacing >= 0 ? (int)Math.Round(measuredSpacing) : 16; }
        }
        private bool SpacingOn { get { return settings.CardSpacing >= 0 || spacingWasSet; } }
        /// <summary>The gap between the extra rows' tiles.</summary>
        private double TileGapX { get { return settings.CardSpacing >= 0 ? settings.CardSpacing : 16; } }

        private ICommand resetSpacingCmd;
        public ICommand ResetSpacingCommand
        {
            get
            {
                return resetSpacingCmd ?? (resetSpacingCmd = new RelayCommand(_ =>
                {
                    settings.CardSpacing = -1;   // A on the slider: as it comes
                    SaveSettings();
                    PlaySound("move");
                    ApplyCardSpacing();
                    UpdateSliders();
                }));
            }
        }

        /// <summary>Every card on screen gets the chosen gap (runs with the per-card pass; cards are reused as you scroll).</summary>
        private void ApplyCardSpacingTo(ListBox list)
        {
            if (list == null) return;
            if (TrianglesPacked) { PackTriangles(list); return; }
            if (trianglesWerePacked) { trianglesWerePacked = false; spacingWasSet = true; }   // (back from triangles: every card's margin again)
            bool on = settings.CardSpacing >= 0 && LayoutNow != 6;   // (the mosaic keeps Playnite's own gap: its rows are worked out from it)
            if (!on && !spacingWasSet)
            {
                // just learn Playnite's own gap (for the slider's starting point)
                if (measuredSpacing < 0)
                    for (int i = 0; i < list.Items.Count && measuredSpacing < 0; i++)
                    {
                        var c0 = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                        var g0 = c0 != null ? UiTools.FindByName(c0, "KX_CardSpacing") as FrameworkElement : null;
                        if (g0 != null && g0.IsLoaded) measuredSpacing = g0.Margin.Left + g0.Margin.Right;
                    }
                return;
            }
            if (on) spacingWasSet = true;
            var want = new Thickness(Math.Max(0, settings.CardSpacing) / 2.0);
            for (int i = 0; i < list.Items.Count; i++)
            {
                var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                var g = c != null ? UiTools.FindByName(c, "KX_CardSpacing") as FrameworkElement : null;
                if (g == null) continue;
                if (on)
                {
                    if (playniteCardMargin == null && g.IsLoaded) { playniteCardMargin = g.Margin; if (measuredSpacing < 0) measuredSpacing = g.Margin.Left + g.Margin.Right; }
                    if (g.Margin != want) g.SetCurrentValue(FrameworkElement.MarginProperty, want);
                }
                else
                {
                    var be = BindingOperations.GetBindingExpressionBase(g, FrameworkElement.MarginProperty);
                    if (be != null) be.UpdateTarget();
                    else if (playniteCardMargin.HasValue && g.Margin != playniteCardMargin.Value) g.SetCurrentValue(FrameworkElement.MarginProperty, playniteCardMargin.Value);
                }
            }
        }

        private bool trianglesWerePacked;

        /// <summary>
        /// Triangles: the card's slot is one step along; the triangle is drawn wider (negative side margins) so neighbours
        /// overlap and fit together. Their size never changes with the spacing, only how far apart they sit.
        /// </summary>
        private void PackTriangles(ListBox list)
        {
            trianglesWerePacked = true;
            double step = TriStep;
            for (int i = 0; i < list.Items.Count; i++)
            {
                var c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                var g = c != null ? UiTools.FindByName(c, "KX_CardSpacing") as FrameworkElement : null;
                if (g == null || c.ActualWidth <= 0) continue;
                if (playniteCardMargin == null && g.IsLoaded && settings.CardSpacing < 0) playniteCardMargin = g.Margin;
                double slot = c.ActualWidth;
                double wide = slot / step;                 // the triangle's own width
                double m = -Math.Round((wide - slot) / 2);
                var want = new Thickness(m, 0, m, 0);
                if (g.Margin != want) g.SetCurrentValue(FrameworkElement.MarginProperty, want);
            }
        }

        /// <summary>The spacing changed: the cards, the extra rows and the Board again.</summary>
        private void ApplyCardSpacing()
        {
            RefreshTileArtSoon();
            RebuildExtraRowsSoon();
            if (IsBoardMode) { BoardPlaceAll(); BoardRefillSoon(); }
        }
    }
}
