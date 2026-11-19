using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixLinks
{
    /// <summary>An ICommand that runs on the UI thread and never lets an exception out (P2).</summary>
    internal class SafeCommand : ICommand
    {
        private readonly Action run;
        private readonly string what;

        public SafeCommand(string what, Action run) { this.what = what; this.run = run; }

        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) { return true; }

        public void Execute(object parameter)
        {
            try { run(); }
            catch (Exception ex) { SafeLog.Warn(what, ex); }
        }
    }

    /// <summary>
    /// Our "Links" shortcut in Kippix's top bar. Kippix rebuilds its shortcut list (Shortcuts.Clear() and its own keys again)
    /// at start-up, on a language change and whenever a layout or profile is applied, so ours is put back each time, in
    /// front of the shortcut it was last seen before. Pin and "stay visible" come from Kippix's own Top bar settings page
    /// (the same list drives it) and are kept in our settings.json, not Kippix's.
    /// </summary>
    internal class TopBarButton
    {
        public const string Key = "x-links";
        private const string FallbackIcon = "M10,14 L14,10 M8.5,11.5 L6.5,13.5 C5,15 5,17.5 6.5,19 C8,20.5 10.5,20.5 12,19 L14,17 M15.5,12.5 L17.5,10.5 C19,9 19,6.5 17.5,5 C16,3.5 13.5,3.5 12,5 L10,7";

        private readonly KippixSeam seam;
        private readonly PanelSettings settings;
        private readonly Action openPanel;

        private IList shortcuts;
        private object item;
        private bool inserting;
        private bool ensurePending;
        private DispatcherTimer retry;
        private int retries;
        private DispatcherTimer orderSave;
        private bool stopped;
        private ImageSource picture;   // the site's icon when the button follows the latest link (null: the plain link icon)

        /// <summary>The button's picture: the given one, or the plain link icon for null (UI thread).</summary>
        public void SetIcon(ImageSource p)
        {
            picture = p;
            try { if (item != null) KippixSeam.SetProp(item, "Picture", p ?? MakeIcon()); }
            catch (Exception ex) { SafeLog.Warn("top bar: icon", ex); }
        }

        public TopBarButton(KippixSeam seam, PanelSettings settings, Action openPanel)
        {
            this.seam = seam;
            this.settings = settings;
            this.openPanel = openPanel;
        }

        /// <summary>Puts the shortcut in, or keeps trying every 2 s (15 times) while Kippix isn't ready.</summary>
        public void Start()
        {
            try
            {
                stopped = false;
                if (TryAttach()) return;
                retries = 0;
                retry = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
                retry.Tick += (s, e) =>
                {
                    try
                    {
                        retries++;
                        if (stopped || TryAttach() || retries >= 15)
                        {
                            retry.Stop();
                            if (item == null && !stopped) SafeLog.Info("top bar: Kippix not found, the main menu entry still works");
                        }
                    }
                    catch (Exception ex) { retry.Stop(); SafeLog.Warn("top bar: retry", ex); }
                };
                retry.Start();
            }
            catch (Exception ex) { SafeLog.Warn("top bar: start", ex); }
        }

        private bool TryAttach()
        {
            if (item != null) return true;
            var list = seam.Shortcuts;
            if (list == null) return false;
            var cmd = new SafeCommand("top bar: open failed", openPanel);
            object it = null;
            var move = new SafeCommand("top bar: move failed", () => { if (item != null) seam.ToggleMove(item); });
            it = seam.NewShortcut(Key, "Links", cmd, move, picture ?? MakeIcon());
            if (it == null) return false;
            KippixSeam.SetProp(it, "IsPinned", settings.Pinned);
            KippixSeam.SetProp(it, "IsShown", settings.Pinned);
            KippixSeam.SetProp(it, "StayVisible", settings.Stay);
            var npc = it as INotifyPropertyChanged;
            if (npc != null) npc.PropertyChanged += OnItemChanged;
            item = it;
            shortcuts = list;
            ((INotifyCollectionChanged)list).CollectionChanged += OnShortcutsChanged;
            Ensure();
            SafeLog.Info("top bar: shortcut added");
            return true;
        }

        public void Stop()
        {
            try
            {
                stopped = true;
                if (retry != null) retry.Stop();
                if (orderSave != null) orderSave.Stop();
                if (shortcuts != null) ((INotifyCollectionChanged)shortcuts).CollectionChanged -= OnShortcutsChanged;
                var npc = item as INotifyPropertyChanged;
                if (npc != null) npc.PropertyChanged -= OnItemChanged;
                if (shortcuts != null && item != null && shortcuts.Contains(item))
                {
                    inserting = true;
                    try { shortcuts.Remove(item); } finally { inserting = false; }
                }
            }
            catch (Exception ex) { SafeLog.Warn("top bar: stop", ex); }
        }

        /// <summary>
        /// Our shortcut back in the list (when it isn't): before the one it was last before, else before ⋯, else last; and
        /// last when it was last (Before = "").
        /// </summary>
        private void Ensure()
        {
            ensurePending = false;
            if (stopped || shortcuts == null || item == null || shortcuts.Contains(item)) return;
            int at;
            if (settings.Before != null && settings.Before.Length == 0) at = shortcuts.Count;   // "" = it was moved to the very end
            else
            {
                at = IndexOfKey(settings.Before);
                if (at < 0) at = IndexOfKey("options");
                if (at < 0) at = shortcuts.Count;
            }
            inserting = true;
            try { shortcuts.Insert(at, item); }
            finally { inserting = false; }
            KippixSeam.SetProp(item, "IsShown", KippixSeam.GetBool(item, "IsPinned"));
        }

        private int IndexOfKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < shortcuts.Count; i++)
                if (KippixSeam.GetProp(shortcuts[i], "Key") as string == key) return i;
            return -1;
        }

        private void OnShortcutsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (inserting || stopped) return;
                if (e.Action == NotifyCollectionChangedAction.Reset || !shortcuts.Contains(item))
                {
                    // Kippix rebuilt the list: back in once it has finished (the list can't change inside its own event)
                    if (ensurePending) return;
                    ensurePending = true;
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        try { Ensure(); }
                        catch (Exception ex) { ensurePending = false; SafeLog.Warn("top bar: put back", ex); }
                    }), DispatcherPriority.Background);
                    return;
                }
                // moved (Settings › Top bar › Shortcuts) or the list around it changed: remember where it sits
                if (orderSave == null)
                {
                    orderSave = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
                    orderSave.Tick += (s, a) =>
                    {
                        try
                        {
                            orderSave.Stop();
                            if (stopped || shortcuts == null) return;
                            int i = shortcuts.IndexOf(item);
                            if (i < 0) return;
                            string next = i + 1 < shortcuts.Count ? KippixSeam.GetProp(shortcuts[i + 1], "Key") as string : "";
                            if ((next ?? "") == (settings.Before ?? "")) return;
                            settings.Before = next ?? "";
                            settings.Save();
                        }
                        catch (Exception ex) { SafeLog.Warn("top bar: order save", ex); }
                    };
                }
                orderSave.Stop();
                orderSave.Start();
            }
            catch (Exception ex) { SafeLog.Warn("top bar: list changed", ex); }
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == "IsPinned")
                {
                    bool pinned = KippixSeam.GetBool(item, "IsPinned");
                    KippixSeam.SetProp(item, "IsShown", pinned);
                    if (settings.Pinned != pinned) { settings.Pinned = pinned; settings.Save(); }
                }
                else if (e.PropertyName == "StayVisible")
                {
                    bool stay = KippixSeam.GetBool(item, "StayVisible");
                    if (settings.Stay != stay) { settings.Stay = stay; settings.Save(); }
                }
            }
            catch (Exception ex) { SafeLog.Warn("top bar: item changed", ex); }
        }

        /// <summary>
        /// The white chain-link glyph. Kippix shows Picture in a 26 x 26 Image (Uniform) next to its stroke-only icons
        /// (StrokeThickness 1.8), so the pen is sized in the geometry's own units to come out about 1.8 px.
        /// </summary>
        private static ImageSource MakeIcon()
        {
            try
            {
                Geometry g = null;
                try { g = Application.Current != null ? Application.Current.TryFindResource("KX_IconLink") as Geometry : null; }
                catch (Exception) { g = null; }
                if (g == null) g = Geometry.Parse(FallbackIcon);
                var b = g.Bounds;
                double m = Math.Max(b.Width, b.Height);
                if (double.IsNaN(m) || double.IsInfinity(m) || m <= 0) { g = Geometry.Parse(FallbackIcon); m = Math.Max(g.Bounds.Width, g.Bounds.Height); }
                double s = (26 - 1.8) / m;
                var pen = new Pen(Brushes.White, 1.8 / s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                var drawing = new GeometryDrawing(null, pen, g);
                if (drawing.CanFreeze) drawing.Freeze();
                var img = new DrawingImage(drawing);
                if (img.CanFreeze) img.Freeze();
                return img;
            }
            catch (Exception ex) { SafeLog.Warn("top bar: icon", ex); return null; }
        }
    }
}
