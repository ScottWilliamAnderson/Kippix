using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.46) The quick bar: the Guide button brings up a bar of shortcuts at the bottom of the screen (the PlayStation 5's
    /// control-centre idea, Kippix's own look): it rises with a dim behind it and drops away again. Its own items and order
    /// (settings.GuideBarItems), apart from the top bar's: the top bar's shortcuts plus Home, Profile, Refresh game library and
    /// Update Steam games. Left / Right choose, A opens, B or Guide close. Hold A to customise: the item is picked up, Left /
    /// Right move it, X takes it off, Y adds one (a row of the rest comes up), A or B put it down.
    ///
    /// When it comes up (settings.GuideBar on, never while a game runs): with "Guide button goes home" on, only on the home
    /// screen (anywhere else the Guide button still goes home); with it off (or Select + Start chosen for home), anywhere.
    ///
    /// Built in code into the main screen (PART_MainHost, over the panels, under the confirm box) and takes every button
    /// and key while it's up, like Background › Move the picture.
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly string[] GuideBarDefault = { "home", "profile", "search", "store", "achievements", "gallery", "downloads", "discord", "audio", "options", "shutdown" };
        private static readonly string[] GuideBarExtra = { "home", "profile", "library", "steamupdate" };

        private bool guideBarOpen;
        private bool guideBarClosing;
        private Grid gbLayer;
        private Border gbDock, gbAddDock;
        private StackPanel gbRow, gbAddRow;
        private TextBlock gbTitle, gbClock, gbHint;
        private TranslateTransform gbDockMove;
        private ScaleTransform gbDockScale;
        private List<string> gbItems = new List<string>();
        private List<string> gbAddItems = new List<string>();
        private int gbIndex, gbAddIndex;
        private bool gbEditing, gbAdding;
        private DateTime gbOpenedAt, gbPadAAt, gbPadBAt;
        private DispatcherTimer gbHoldTimer, gbWatch;
        private bool gbAHeld, gbHoldFired;

        public bool GuideBar
        {
            get { return settings.GuideBar; }
            set { if (settings.GuideBar == value) return; settings.GuideBar = value; SaveSettings(); Notify("GuideBar"); }
        }

        // ---------------- the Guide button ----------------

        /// <summary>From OnPadDownEarly: the Guide button. True when the quick bar took it.</summary>
        private bool GuideBarGuide(KeyEventArgs e)
        {
            if (!settings.GuideBar || gameRunningNow || !IsFullscreen) return false;
            if (IsPadRelease(e)) return guideBarOpen || (DateTime.Now - gbOpenedAt).TotalMilliseconds < 1500;   // (the let-go of the press that opened it)
            if (guideBarOpen) { CloseGuideBar(); return true; }
            if (TourLocked) return true;   // (1.0.1) an unfinished Getting started: the Guide button does nothing over it
            bool guideGoesHome = settings.GuideGoesHome && settings.HomeButton == 0;
            bool elsewhere = confirmOpen || openPanel != null || detailsOpen || extraRowAt > 0 || kbOpen || FolderInside;
            if (guideGoesHome && elsewhere) return false;   // the Guide button goes home, as before
            if (confirmOpen || kbOpen || freeMove || IsMasterMoving || bgMoveOn || splashEditOn || lpOpen) return false;
            OpenGuideBar();
            return true;
        }

        // ---------------- open / close ----------------

        private void OpenGuideBar()
        {
            if (guideBarOpen) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var host = win != null ? UiTools.FindByName(win, "PART_MainHost") as Panel : null;
            if (host == null) { logger.Info("Kippix: quick bar: the main screen (PART_MainHost) wasn't found"); return; }
            gbItems = (settings.GuideBarItems != null && settings.GuideBarItems.Count > 0 ? settings.GuideBarItems : GuideBarDefault.ToList())
                .Where(GbKnown).Distinct().ToList();
            if (gbItems.Count == 0) gbItems = GuideBarDefault.ToList();
            GbBuild(host);
            guideBarOpen = true;
            guideBarClosing = false;
            gbEditing = gbAdding = false;
            gbOpenedAt = DateTime.Now;
            gbIndex = Math.Min(gbItems.Count - 1, gbItems.IndexOf("home") >= 0 && (openPanel != null || detailsOpen) ? gbItems.IndexOf("home") : (gbItems[0] == "home" && gbItems.Count > 1 && openPanel == null && !detailsOpen ? 1 : 0));
            GbFillRow(true);
            GbUpdate();
            gbLayer.Visibility = Visibility.Visible;
            PlaySound("select");

            // the rise: the dim fades in, the dock comes up from below, the tiles follow one after another
            gbLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            gbDockMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(140, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
            gbDockScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            gbDockScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            gbDock.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));

            if (gbWatch == null)
            {
                gbWatch = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(300) };
                gbWatch.Tick += (s, e) =>
                {
                    try
                    {
                        if (!guideBarOpen) { gbWatch.Stop(); return; }
                        if (gameRunningNow || confirmOpen || kbOpen) CloseGuideBar(true);
                        else if (gbClock != null) gbClock.Text = DateTime.Now.ToString("t");
                    }
                    catch (Exception) { }
                };
            }
            gbWatch.Start();
        }

        private void CloseGuideBar() { CloseGuideBar(false); }

        private void CloseGuideBar(bool quiet)
        {
            if (!guideBarOpen) return;
            if (gbEditing || gbAdding) GbSave();
            guideBarOpen = false;
            guideBarClosing = true;
            gbEditing = gbAdding = false;
            if (gbHoldTimer != null) gbHoldTimer.Stop();
            if (gbWatch != null) gbWatch.Stop();
            if (!quiet) PlaySound("back");
            var layer = gbLayer;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            gbDockMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(gbDockMove.Y, 110, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            var fade = new DoubleAnimation(layer.Opacity, 0, TimeSpan.FromMilliseconds(200));
            fade.Completed += (s, e) => { if (!guideBarOpen) { layer.Visibility = Visibility.Collapsed; guideBarClosing = false; } };
            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ---------------- the buttons ----------------

        /// <summary>From OnPadDownEarly while it's up: every controller button (the D-pad and sticks come as arrows: GuideBarKey).</summary>
        private void GuideBarPad(ControllerInput button, KeyEventArgs e)
        {
            bool release = IsPadRelease(e);
            if (button == ControllerInput.A)
            {
                gbPadAAt = DateTime.Now;
                if (!release) GbADown(); else GbAUp();
                return;
            }
            if (release) return;
            switch (button)
            {
                case ControllerInput.B: gbPadBAt = DateTime.Now; GbBack(); break;
                case ControllerInput.Guide: CloseGuideBar(); break;
                case ControllerInput.X: if (gbEditing && !gbAdding) GbRemove(); break;
                case ControllerInput.Y: if (gbEditing && !gbAdding) GbStartAdd(); break;
                case ControllerInput.LeftShoulder: GbMove(-1); break;
                case ControllerInput.RightShoulder: GbMove(1); break;
            }
        }

        /// <summary>From the window's key handler while it's up: arrows, and the keyboard's own keys.</summary>
        private void GuideBarKey(KeyEventArgs e)
        {
            if (e.Key == Key.System) return;
            e.Handled = true;
            bool pad = e.GetType().Name == "GameControllerInputEventArgs";
            switch (e.Key)
            {
                case Key.Left: GbMove(-1); break;
                case Key.Right: GbMove(1); break;
                case Key.Up: if (gbAdding) { } break;
                case Key.Down: if (gbAdding) GbBack(); break;
                case Key.Enter:
                    if (pad || e.IsRepeat || (DateTime.Now - gbPadAAt).TotalMilliseconds < 400 || (DateTime.Now - gbOpenedAt).TotalMilliseconds < 300) break;   // (the A, already handled)
                    if (gbAdding) GbPickAdd(); else if (gbEditing) GbEndEdit(); else GbActivate();
                    break;
                case Key.Space: if (!pad && !e.IsRepeat) { if (gbEditing) GbEndEdit(); else GbStartEdit(); } break;
                case Key.Delete: if (!pad && gbEditing && !gbAdding) GbRemove(); break;
                case Key.Insert: if (!pad && gbEditing && !gbAdding) GbStartAdd(); break;
                case Key.Escape: case Key.Back:
                    if (pad || e.IsRepeat || (DateTime.Now - gbPadBAt).TotalMilliseconds < 400) break;
                    GbBack();
                    break;
            }
        }

        private void GbADown()
        {
            if ((DateTime.Now - gbOpenedAt).TotalMilliseconds < 250) return;
            if (gbAdding) { GbPickAdd(); return; }
            if (gbEditing) { GbEndEdit(); return; }
            gbAHeld = true;
            gbHoldFired = false;
            if (gbHoldTimer == null)
            {
                gbHoldTimer = new DispatcherTimer(DispatcherPriority.Input, ui) { Interval = TimeSpan.FromMilliseconds(650) };
                gbHoldTimer.Tick += (s, e) => { gbHoldTimer.Stop(); if (gbAHeld && guideBarOpen && !gbEditing) { gbHoldFired = true; GbStartEdit(); } };
            }
            gbHoldTimer.Stop();
            gbHoldTimer.Start();
        }

        private void GbAUp()
        {
            if (!gbAHeld) return;
            gbAHeld = false;
            if (gbHoldTimer != null) gbHoldTimer.Stop();
            if (!gbHoldFired && !gbEditing) GbActivate();
        }

        private void GbBack()
        {
            if (gbAdding) { gbAdding = false; PlaySound("back"); GbUpdate(); return; }
            if (gbEditing) { GbEndEdit(); return; }
            CloseGuideBar();
        }

        private void GbMove(int d)
        {
            if (gbAdding)
            {
                if (gbAddItems.Count == 0) return;
                int n = Math.Max(0, Math.Min(gbAddItems.Count - 1, gbAddIndex + d));
                if (n == gbAddIndex) return;
                gbAddIndex = n;
                PlaySound("move");
                GbUpdate();
                return;
            }
            int to = Math.Max(0, Math.Min(gbItems.Count - 1, gbIndex + d));
            if (to == gbIndex) return;
            if (gbEditing)
            {
                string k = gbItems[gbIndex];
                gbItems.RemoveAt(gbIndex);
                gbItems.Insert(to, k);
                gbIndex = to;
                GbFillRow(false);
            }
            else gbIndex = to;
            PlaySound("move");
            GbUpdate();
        }

        // ---------------- customising ----------------

        private void GbStartEdit()
        {
            if (gbItems.Count == 0) return;
            gbEditing = true;
            gbAdding = false;
            PlaySound("select");
            GbUpdate();
        }

        private void GbEndEdit()
        {
            gbEditing = false;
            gbAdding = false;
            GbSave();
            PlaySound("select");
            GbUpdate();
        }

        private void GbRemove()
        {
            if (gbItems.Count <= 1) { PlaySound("back"); return; }   // (one always stays, or there's nothing to come back to)
            gbItems.RemoveAt(gbIndex);
            gbIndex = Math.Min(gbIndex, gbItems.Count - 1);
            GbSave();
            GbFillRow(false);
            PlaySound("back");
            GbUpdate();
        }

        private void GbStartAdd()
        {
            gbAddItems = GbAllKeys().Where(k => !gbItems.Contains(k)).ToList();
            if (gbAddItems.Count == 0) { PlaySound("back"); return; }
            gbAdding = true;
            gbAddIndex = 0;
            GbFillAddRow();
            PlaySound("select");
            GbUpdate();
        }

        private void GbPickAdd()
        {
            if (gbAddIndex < 0 || gbAddIndex >= gbAddItems.Count) return;
            string k = gbAddItems[gbAddIndex];
            int at = Math.Min(gbItems.Count, gbIndex + 1);
            gbItems.Insert(at, k);
            gbIndex = at;
            gbAdding = false;
            GbSave();
            GbFillRow(false);
            PlaySound("select");
            GbUpdate();
        }

        private void GbSave()
        {
            settings.GuideBarItems = gbItems.ToList();
            SaveSettings();
        }

        // ---------------- what each one is and does ----------------

        private IEnumerable<string> GbAllKeys()
        {
            var keys = new List<string>(GuideBarExtra);
            foreach (string k in DefaultOrder) if (k != "battery" && k != "clock" && !keys.Contains(k)) keys.Add(k);
            return keys;
        }

        private bool GbKnown(string k)
        {
            return !string.IsNullOrEmpty(k) && (GuideBarExtra.Contains(k) || (DefaultOrder.Contains(k) && k != "battery" && k != "clock"));
        }

        private string GbName(string k)
        {
            switch (k)
            {
                case "home": return Loc("LOC_KX_GbHome", "Home");
                case "profile": { string n = null; try { n = ProfileName; } catch (Exception) { } return string.IsNullOrEmpty(n) ? Loc("LOC_KX_GbProfile", "Profile") : n; }
                case "library": return Loc("LOC_KX_RefreshLibrary", "Refresh game library");
                case "steamupdate": return Loc("LOC_KX_SteamPriorityTip", "Update Steam games straight away");
                case "options": return Loc("LOC_KX_GbSettings", "Settings");
            }
            return ShortcutName(k);
        }

        private void GbActivate()
        {
            if (gbIndex < 0 || gbIndex >= gbItems.Count) return;
            string k = gbItems[gbIndex];
            CloseGuideBar(true);
            PlaySound("select");
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    switch (k)
                    {
                        case "home": HoldBackHome(); return;
                        case "profile": if (openPanel != "profile") ToggleProfile(); return;
                        case "library": RefreshGameLibrary(); return;
                        case "steamupdate": StartSteamPriority(); return;
                    }
                    if (openPanel == k) return;   // (already open)
                    var cmd = ShortcutCommand(k);
                    if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
                }
                catch (Exception ex) { logger.Warn("Kippix: quick bar: " + k + ": " + ex.Message); }
            }), DispatcherPriority.Input);
        }

        /// <summary>The item's picture: the profile picture, the user's own icons, or the theme's line icon for it.</summary>
        private FrameworkElement GbIcon(string k, Brush fg, double size)
        {
            if (k == "profile")
            {
                ImageSource pic = null;
                try { pic = ProfilePicture; } catch (Exception) { }
                if (pic != null)
                    return new Ellipse { Width = size * 1.35, Height = size * 1.35, Fill = new ImageBrush(pic) { Stretch = Stretch.UniformToFill }, Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.5 };
            }
            string imageKey = k == "library" ? "KX_LibraryRefreshImage" : k == "steamupdate" ? "KX_SteamRefreshImage" : null;
            if (imageKey != null)
            {
                try { LoadSteamPriorityIcon(); } catch (Exception) { }
                var img = Application.Current != null ? Application.Current.TryFindResource(imageKey) as ImageSource : null;
                if (img != null) return new Rectangle { Width = size, Height = size, Fill = fg, OpacityMask = new ImageBrush(img) { Stretch = Stretch.Uniform } };
            }
            string geoKey;
            switch (k)
            {
                case "home": geoKey = "KX_IconHome"; break;
                case "profile": geoKey = "KX_IconProfile"; break;
                case "library": geoKey = "KX_IconRefresh"; break;
                case "steamupdate": geoKey = "KX_IconDownload"; break;
                case "search": geoKey = "KX_IconSearch"; break;
                case "achievements": geoKey = "KX_IconTrophy"; break;
                case "gallery": geoKey = "KX_IconGallery"; break;
                case "store": geoKey = "KX_IconStore"; break;
                case "discord": geoKey = "KX_IconDiscord"; break;
                case "audio": geoKey = "KX_IconSpeaker"; break;
                case "hibernate": geoKey = "KX_IconHibernate"; break;
                case "sleep": geoKey = "KX_IconMoon"; break;
                case "restart": geoKey = "KX_IconRefresh"; break;
                case "shutdown": geoKey = "KX_IconPower"; break;
                case "downloads": geoKey = "KX_IconDownload"; break;
                case "quick": geoKey = "KX_IconSliders"; break;
                case "releases": geoKey = "KX_IconCalendar"; break;
                case "wishlist": geoKey = "KX_IconHeart"; break;
                case "news": geoKey = "KX_IconNews"; break;
                case "friends": geoKey = "KX_IconFriends"; break;
                case "collections": geoKey = "KX_IconShelf"; break;
                case "splitscreen": geoKey = "KX_IconController"; break;
                case "pick": geoKey = "KX_IconDice"; break;
                case "storage": geoKey = "KX_IconDrive"; break;
                case "sortshow": geoKey = "KX_IconSort"; break;
                case "controller": geoKey = "KX_IconController"; break;
                case "bluetooth": geoKey = "KX_IconBluetooth"; break;
                case "year": geoKey = "KX_IconChart"; break;
                case "options": geoKey = "KX_IconGear"; break;
                case "media": geoKey = "KX_IconMedia"; break;            // (1.0.1)
                case "localmedia": geoKey = "KX_IconLocalMedia"; break;
                case "netflix": geoKey = "KX_IconTv"; break;
                case "youtube": geoKey = "KX_IconVideo"; break;
                case "prime": geoKey = "KX_IconFilmStrip"; break;
                case "crunchyroll": geoKey = "KX_IconClapper"; break;
                default: geoKey = "KX_IconDots"; break;
            }
            var geo = Application.Current != null ? Application.Current.TryFindResource(geoKey) as Geometry : null;
            if (geo == null) return new TextBlock { Text = "•", Foreground = fg, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center };
            return new Path
            {
                Data = geo, Stretch = Stretch.Uniform, Width = size, Height = size, Stroke = fg, StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Fill = k == "discord" ? fg : Brushes.Transparent
            };
        }

        // ---------------- drawing ----------------

        private static readonly Brush GbTileBg = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        private static readonly Brush GbTileSel = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        private static readonly Brush GbIconFg = Frozen(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
        private static readonly Brush GbIconSelFg = Frozen(Color.FromRgb(0x12, 0x14, 0x19));

        private void GbBuild(Panel host)
        {
            if (gbLayer != null && ReferenceEquals(VisualTreeHelper.GetParent(gbLayer), host)) return;
            if (gbLayer != null) { var old = VisualTreeHelper.GetParent(gbLayer) as Panel; if (old != null) old.Children.Remove(gbLayer); }

            var layer = new Grid { Focusable = false, Visibility = Visibility.Collapsed, Opacity = 0 };
            Panel.SetZIndex(layer, 89);   // over the panels and the game page, under the confirm box (90)
            Grid.SetRowSpan(layer, 99);
            Grid.SetColumnSpan(layer, 99);
            // the dim: the screen darkens toward the bottom, where the bar is
            var dim = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            dim.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0, 0, 0), 0));
            dim.GradientStops.Add(new GradientStop(Color.FromArgb(0x88, 0, 0, 0), 0.55));
            dim.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, 0, 0, 0), 1));
            var shade = new Border { Background = dim };
            shade.MouseLeftButtonUp += (s, e) => { if (guideBarOpen && ReferenceEquals(e.OriginalSource, shade)) CloseGuideBar(); };
            layer.Children.Add(shade);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(60, 0, 60, 54) };

            // the row to add from (customising › Y)
            gbAddRow = new StackPanel { Orientation = Orientation.Horizontal };
            gbAddDock = new Border
            {
                Child = new ScrollViewer { Content = gbAddRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, MaxWidth = 1500 },
                Background = Frozen(Color.FromArgb(0xEE, 0x15, 0x18, 0x1E)), BorderBrush = Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 18), HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            stack.Children.Add(gbAddDock);

            // the name of the highlighted one, and the clock on the right
            var head = new Grid { Margin = new Thickness(8, 0, 8, 14) };
            gbTitle = new TextBlock { FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            gbClock = new TextBlock { FontSize = 26, Foreground = Frozen(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Text = DateTime.Now.ToString("t") };
            head.Children.Add(gbTitle);
            head.Children.Add(gbClock);
            stack.Children.Add(head);

            // the dock
            gbRow = new StackPanel { Orientation = Orientation.Horizontal };
            gbDock = new Border
            {
                Child = gbRow,
                Background = new LinearGradientBrush(Color.FromArgb(0xF0, 0x24, 0x27, 0x30), Color.FromArgb(0xF0, 0x15, 0x17, 0x1C), 90),
                BorderBrush = Frozen(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(30), Padding = new Thickness(16, 16, 16, 16),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.55, Color = Colors.Black },
                RenderTransformOrigin = new Point(0.5, 1)
            };
            gbDockMove = new TranslateTransform();
            gbDockScale = new ScaleTransform(1, 1);
            var tg = new TransformGroup();
            tg.Children.Add(gbDockScale);
            tg.Children.Add(gbDockMove);
            gbDock.RenderTransform = tg;
            stack.Children.Add(gbDock);

            gbHint = new TextBlock { FontSize = 19, Foreground = Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
            stack.Children.Add(gbHint);
            layer.Children.Add(stack);

            host.Children.Add(layer);
            gbLayer = layer;
        }

        /// <summary>The dock's tiles (again after a change; first: they come in one after another).</summary>
        private void GbFillRow(bool entrance)
        {
            gbRow.Children.Clear();
            for (int i = 0; i < gbItems.Count; i++)
            {
                int index = i;
                var tile = GbTile(gbItems[i], false);
                tile.MouseLeftButtonUp += (s, e) => { if (!guideBarOpen || gbEditing || gbAdding) return; gbIndex = index; GbUpdate(); GbActivate(); };
                gbRow.Children.Add(tile);
                if (entrance)
                {
                    var move = new TranslateTransform(0, 26);
                    ((TransformGroup)tile.RenderTransform).Children.Add(move);
                    var delay = TimeSpan.FromMilliseconds(70 + i * 28);
                    tile.Opacity = 0;
                    tile.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });
                    move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(360)) { BeginTime = delay, EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
                }
            }
        }

        private void GbFillAddRow()
        {
            gbAddRow.Children.Clear();
            foreach (string k in gbAddItems) gbAddRow.Children.Add(GbTile(k, true));
        }

        private Border GbTile(string k, bool small)
        {
            double size = small ? 64 : 84;
            var tile = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(small ? 18 : 24), Background = GbTileBg, Margin = new Thickness(small ? 5 : 7, 0, small ? 5 : 7, 0),
                BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Tag = k, Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            var g = new TransformGroup();
            g.Children.Add(new ScaleTransform(1, 1));
            g.Children.Add(new TranslateTransform(0, 0));
            tile.RenderTransform = g;
            var icon = GbIcon(k, GbIconFg, small ? 26 : 34);
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            tile.Child = icon;
            return tile;
        }

        /// <summary>The highlight, the picked-up tile, the title and the hints, for where things are now.</summary>
        private void GbUpdate()
        {
            if (gbRow == null) return;
            var accent = Application.Current != null ? Application.Current.TryFindResource("KX_AccentBrush") as Brush : null;
            for (int i = 0; i < gbRow.Children.Count; i++)
                GbStyleTile(gbRow.Children[i] as Border, i == gbIndex && !gbAdding, gbEditing && i == gbIndex, accent);
            gbAddDock.Visibility = gbAdding ? Visibility.Visible : Visibility.Collapsed;
            if (gbAdding)
                for (int i = 0; i < gbAddRow.Children.Count; i++)
                {
                    var t = gbAddRow.Children[i] as Border;
                    GbStyleTile(t, i == gbAddIndex, false, accent);
                    if (i == gbAddIndex && t != null) t.BringIntoView();
                }

            string key = gbAdding ? (gbAddIndex < gbAddItems.Count ? gbAddItems[gbAddIndex] : null) : (gbIndex < gbItems.Count ? gbItems[gbIndex] : null);
            gbTitle.Text = key != null ? (gbAdding ? string.Format(Loc("LOC_KX_GbAddWhat", "Add {0}"), GbName(key)) : GbName(key)) : "";
            gbDock.BorderBrush = gbEditing ? (accent ?? Brushes.White) : Frozen(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));

            int kind = settings.ButtonIcons == 0 ? promptKind : settings.ButtonIcons;
            bool kb = kind == 4;
            string a = kb ? "Enter" : kind == 2 ? "✕" : "A", b = kb ? "Esc" : kind == 2 ? "○" : "B";
            string x = kb ? "Delete" : kind == 2 ? "□" : "X", y = kb ? "Insert" : kind == 2 ? "△" : "Y";
            string sep = "      ";
            if (gbAdding) gbHint.Text = a + "  " + Loc("LOC_KX_GbAdd", "Add") + sep + b + "  " + Loc("LOC_KX_Back", "Back");
            else if (gbEditing)
                gbHint.Text = (kb ? "← →" : "◀ ▶") + "  " + Loc("LOC_KX_GbMove", "Move") + sep + x + "  " + Loc("LOC_KX_GbRemove", "Remove") + sep
                            + y + "  " + Loc("LOC_KX_GbAddOne", "Add one") + sep + a + "  " + Loc("LOC_KX_Done", "Done");
            else gbHint.Text = a + "  " + Loc("LOC_KX_Open", "Open") + sep + (kb ? "Space" : string.Format(Loc("LOC_KX_GbHold", "Hold {0}"), a)) + "  " + Loc("LOC_KX_GbCustomise", "Customise")
                             + sep + b + "  " + Loc("LOC_KX_Close", "Close");
        }

        private void GbStyleTile(Border t, bool selected, bool lifted, Brush accent)
        {
            if (t == null) return;
            var g = t.RenderTransform as TransformGroup;
            var scale = g != null ? g.Children[0] as ScaleTransform : null;
            var lift = g != null ? g.Children[1] as TranslateTransform : null;
            bool was = t.Background == GbTileSel;
            t.Background = selected ? GbTileSel : GbTileBg;
            t.BorderBrush = lifted ? (accent ?? Brushes.White) : Brushes.Transparent;
            var fg = selected ? GbIconSelFg : GbIconFg;
            var p = t.Child as Path;
            if (p != null) { p.Stroke = fg; if (p.Fill != Brushes.Transparent) p.Fill = fg; }
            var r = t.Child as Rectangle;
            if (r != null) r.Fill = fg;
            var tb = t.Child as TextBlock;
            if (tb != null) tb.Foreground = fg;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double s = selected ? 1.12 : 1.0;
            if (scale != null && (was != selected || scale.ScaleX != s))
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            }
            if (lift != null) lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(lifted ? -16 : 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        }
    }
}
