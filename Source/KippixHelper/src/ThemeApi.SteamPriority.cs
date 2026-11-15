using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KippixHelper
{
    /// <summary>
    /// (0.99.41) The profile menu's top row, next to Refresh game library: "Update Steam games straight away". The user's
    /// Steam-HighPriority.ps1 built in (the user asked for it, so it's the one exception to "don't change Steam's files"):
    /// every installed Steam game's appmanifest_&lt;id&gt;.acf gets AutoUpdateBehavior "2" (High Priority: "Always auto-update this
    /// game before others") and ScheduledAutoUpdate "0" (no update pencilled in for days later). Steam must be closed while
    /// they're edited (it writes them back), so: a pop-up says what's happening, Steam is closed (never while a game runs),
    /// the files are changed, Steam is started again in the tray (-silent) and checked to be running, and the pop-up says
    /// it's done and closes. With nothing to change it says so, and Steam is left alone.
    /// The script's Steam not coming back: it started Steam while Steam's helper processes were still closing; now it waits
    /// for steam.exe and steamwebhelper.exe to be gone, starts it from its own folder, and tries again if it isn't up in 25 s.
    /// </summary>
    public partial class ThemeApi
    {
        private bool steamPriorityBusy;

        private ICommand steamPriorityCmd;
        public ICommand SteamPriorityCommand { get { return steamPriorityCmd ?? (steamPriorityCmd = new RelayCommand(_ => StartSteamPriority())); } }

        private static readonly Regex SpBehavior = new Regex("(\"AutoUpdateBehavior\"\\s+)\"(\\d+)\"");
        private static readonly Regex SpSchedule = new Regex("(\"ScheduledAutoUpdate\"\\s+)\"(\\d+)\"");
        private static readonly Regex SpName = new Regex("\"name\"\\s+\"([^\"]*)\"");
        private static readonly Regex SpAppId = new Regex("(\"appid\"[ \\t]+\"\\d+\")");

        private void StartSteamPriority() { StartSteamPriority(false); }

        /// <summary>auto: from a library update adding Steam games (no sound, panels left as they are).</summary>
        private void StartSteamPriority(bool auto)
        {
            if (steamPriorityBusy) return;
            if (!auto) { PlaySound("select"); ClosePanels(); }
            var pop = new SteamPriorityPopup(Application.Current != null ? Application.Current.MainWindow : null);
            pop.Set(Loc("LOC_KX_SpTitle", "Steam updates"), Loc("LOC_KX_SpLooking", "Looking at your Steam games…"), true);
            pop.Show();
            steamPriorityBusy = true;
            KippixShared.SafeWork.Queue(_ =>
            {
                string end = null;
                bool ok = true;
                try { end = SteamPriorityWork(pop, out ok); }
                catch (Exception ex) { ok = false; end = Loc("LOC_KX_SpFailed", "Something went wrong: ") + ex.Message; logger.Error(ex, "Kippix: Steam high priority"); }
                OnUi(() =>
                {
                    steamPriorityBusy = false;
                    pop.Set(Loc("LOC_KX_SpTitle", "Steam updates"), end ?? "", false);
                    pop.CloseAfter(ok ? 4 : 7);
                });
            });
        }

        /// <summary>The work (not on the UI thread). Returns the pop-up's last line.</summary>
        private string SteamPriorityWork(SteamPriorityPopup pop, out bool ok)
        {
            ok = true;
            string steamPath, steamExe;
            List<string> libs;
            bool downloading;
            var toFix = SpScan(out steamPath, out steamExe, out libs, out downloading);
            if (toFix == null) { ok = false; return Loc("LOC_KX_SpNoSteam", "Steam isn't installed on this PC."); }
            if (toFix.Count == 0)
            {
                logger.Info("Kippix: Steam high priority: every game already is (" + libs.Count + " library folders)");
                return Loc("LOC_KX_SpNothing", "Nothing to change: every Steam game already updates straight away.");
            }
            logger.Info("Kippix: Steam high priority: " + toFix.Count + " to change: " + string.Join(", ", toFix.Select(t => t.Item2).ToArray()));

            // never in the middle of a game
            if (AnyRunning || SteamRunningApp() != 0)
            {
                ok = false;
                return Loc("LOC_KX_SpGameRunning", "A game is running, so Steam wasn't closed. Try again once you've finished playing.");
            }

            bool wasRunning = SteamUp();
            if (wasRunning)
            {
                pop.SetSoon(string.Format(Loc("LOC_KX_SpClosing", "Closing Steam to set {0} to update straight away…"), toFix.Count == 1 ? toFix[0].Item2 : string.Format(Loc("LOC_KX_SpNGames", "{0} games"), toFix.Count)));
                try { Process.Start(new ProcessStartInfo(steamExe, "-shutdown") { UseShellExecute = true, WorkingDirectory = steamPath }); } catch (Exception) { }
                var deadline = DateTime.Now.AddSeconds(90);
                while (SteamUp() && DateTime.Now < deadline) Thread.Sleep(1000);
                if (SteamUp()) { ok = false; return Loc("LOC_KX_SpWouldntClose", "Steam didn't close within 90 seconds, so nothing was changed."); }
                // its web helpers close a moment later; starting Steam again before they've gone is what left it closed
                var helpers = DateTime.Now.AddSeconds(20);
                while (Process.GetProcessesByName("steamwebhelper").Length > 0 && DateTime.Now < helpers) Thread.Sleep(500);
                Thread.Sleep(2500);   // (it finishes writing its files)
            }

            pop.SetSoon(string.Format(Loc("LOC_KX_SpChanging", "Setting {0} to update straight away…"), toFix.Count == 1 ? toFix[0].Item2 : string.Format(Loc("LOC_KX_SpNGames", "{0} games"), toFix.Count)));
            int changed = 0;
            var noBom = new UTF8Encoding(false);
            foreach (var g in toFix)
            {
                try
                {
                    string text = File.ReadAllText(g.Item1);
                    string next = SpBehavior.IsMatch(text) ? SpBehavior.Replace(text, "${1}\"2\"") : SpAppId.Replace(text, "$1\n\t\"AutoUpdateBehavior\"\t\t\"2\"", 1);
                    next = SpSchedule.Replace(next, "${1}\"0\"");
                    if (next != text) { File.WriteAllText(g.Item1, next, noBom); changed++; }
                }
                catch (Exception ex) { logger.Warn("Kippix: Steam high priority: couldn't change " + g.Item2 + ": " + ex.Message); }
            }
            logger.Info("Kippix: Steam high priority: changed " + changed + " of " + toFix.Count);

            // Steam back, in the tray
            pop.SetSoon(Loc("LOC_KX_SpStarting", "Opening Steam in the tray…"));
            bool up = StartSteamTray(steamExe, steamPath) || StartSteamTray(steamExe, steamPath);
            if (!up)
            {
                ok = false;
                logger.Warn("Kippix: Steam high priority: Steam didn't start again");
                return string.Format(Loc("LOC_KX_SpDoneNoSteam", "Done: {0} changed, but Steam didn't open again. Open it yourself and the updates will start."), toFix.Count == 1 ? toFix[0].Item2 : string.Format(Loc("LOC_KX_SpNGames", "{0} games"), changed));
            }
            logger.Info("Kippix: Steam high priority: Steam is running again");
            return string.Format(Loc("LOC_KX_SpDone", "Done: {0} will now update straight away. Steam is in the tray."), toFix.Count == 1 ? toFix[0].Item2 : string.Format(Loc("LOC_KX_SpNGames", "{0} games"), changed));
        }

        /// <summary>(0.99.42) Steam's folder and program, its libraries, the games to change (null: no Steam), and whether it's downloading / installing anything now.</summary>
        private static List<Tuple<string, string>> SpScan(out string steamPath, out string steamExe, out List<string> libs, out bool downloading)
        {
            steamPath = SteamPaths.SteamPath();
            steamExe = null; libs = new List<string>(); downloading = false;
            if (string.IsNullOrEmpty(steamPath)) return null;
            steamExe = Path.Combine(steamPath, "steam.exe");
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string e = k != null ? k.GetValue("SteamExe") as string : null;
                    if (!string.IsNullOrEmpty(e) && File.Exists(e.Replace('/', '\\'))) steamExe = e.Replace('/', '\\');
                }
            }
            catch (Exception) { }

            // every library folder
            libs.Add(Path.Combine(steamPath, "steamapps"));
            try
            {
                string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
            }
            catch (Exception) { }
            libs = libs.Where(Directory.Exists).Select(l => l.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // the games not on High Priority yet, or with an update still pencilled in for later
            var toFix = new List<Tuple<string, string>>();   // path, name
            foreach (var lib in libs)
            {
                string[] files;
                try { files = Directory.GetFiles(lib, "appmanifest_*.acf"); } catch (Exception) { continue; }
                foreach (var f in files)
                {
                    try
                    {
                        string text = File.ReadAllText(f);
                        var fl = Regex.Match(text, "\"StateFlags\"\\s+\"(\\d+)\"");
                        long flags;
                        if (fl.Success && long.TryParse(fl.Groups[1].Value, out flags) && (flags & (256 | 262144 | 524288 | 1048576 | 2097152 | 4194304)) != 0) downloading = true;
                        var b = SpBehavior.Match(text);
                        var s = SpSchedule.Match(text);
                        if (!b.Success || b.Groups[2].Value != "2" || (s.Success && s.Groups[2].Value != "0"))
                        {
                            var n = SpName.Match(text);
                            toFix.Add(Tuple.Create(f, n.Success ? n.Groups[1].Value : Path.GetFileName(f)));
                        }
                    }
                    catch (Exception) { }
                }
            }
            return toFix;
        }

        private static bool SteamUp()
        {
            try { return Process.GetProcessesByName("steam").Length > 0; } catch (Exception) { return false; }
        }

        private static int SteamRunningApp()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    object v = k != null ? k.GetValue("RunningAppID") : null;
                    return v is int ? (int)v : 0;
                }
            }
            catch (Exception) { return 0; }
        }

        /// <summary>Starts Steam minimised to the tray and waits up to 25 s for it to be running.</summary>
        private static bool StartSteamTray(string steamExe, string steamPath)
        {
            if (SteamUp()) return true;
            try { Process.Start(new ProcessStartInfo(steamExe, "-silent") { UseShellExecute = true, WorkingDirectory = steamPath }); }
            catch (Exception ex) { LogManagerWarn("Kippix: Steam high priority: starting Steam: " + ex.Message); return false; }
            var deadline = DateTime.Now.AddSeconds(25);
            bool up = false;
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(1000);
                if (SteamUp()) { Thread.Sleep(3000); if (SteamUp()) { up = true; break; } }   // (still there a moment later: not the bootstrapper handing over and quitting)
            }
            if (!up) return SteamUp();
            WaitSteamReady(120);
            return SteamUp();
        }

        /// <summary>
        /// (0.99.42) steam.exe running isn't Steam in the tray: its icon comes once Steam has signed in and its interface
        /// (steamwebhelper) is up, about 20 s later. Waits for: signed in (HKCU Valve\Steam\ActiveProcess ActiveUser not 0), three
        /// or more steamwebhelper processes, and Steam's own "Steam" window (it's there, hidden, once the interface has loaded), or
        /// 25 s after signing in without it; then 2 s more. Each signal's time is logged, to check against when the icon shows.
        /// </summary>
        private static void WaitSteamReady(int seconds)
        {
            var start = DateTime.Now;
            var deadline = start.AddSeconds(seconds);
            DateTime? loggedAt = null, helpersAt = null, windowAt = null;
            string lastWins = null;
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(1000);
                if (!SteamUp()) continue;
                int helpers = 0;
                try { helpers = Process.GetProcessesByName("steamwebhelper").Length; } catch (Exception) { }
                bool logged = SteamActiveUser() != 0;
                bool hasWindow;
                string wins = SteamWindows(out hasWindow);
                double at = (DateTime.Now - start).TotalSeconds;
                if (logged && loggedAt == null) { loggedAt = DateTime.Now; LogManagerInfo("Kippix: Steam high priority: Steam signed in after " + at.ToString("0") + " s"); }
                if (helpers >= 3 && helpersAt == null) { helpersAt = DateTime.Now; LogManagerInfo("Kippix: Steam high priority: " + helpers + " Steam web helpers after " + at.ToString("0") + " s"); }
                if (hasWindow && windowAt == null) { windowAt = DateTime.Now; LogManagerInfo("Kippix: Steam high priority: Steam's window after " + at.ToString("0") + " s"); }
                if (wins != lastWins) { lastWins = wins; LogManagerInfo("Kippix: Steam high priority: Steam's windows at " + at.ToString("0") + " s: " + (wins.Length > 400 ? wins.Substring(0, 400) : wins)); }
                bool ready = loggedAt != null && helpersAt != null && (windowAt != null || (DateTime.Now - loggedAt.Value).TotalSeconds >= 25);
                if (ready)
                {
                    Thread.Sleep(2000);
                    LogManagerInfo("Kippix: Steam high priority: Steam counted as in the tray after " + (DateTime.Now - start).TotalSeconds.ToString("0") + " s");
                    return;
                }
            }
            LogManagerInfo("Kippix: Steam high priority: stopped waiting for Steam's tray icon after " + seconds + " s");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
        private delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);

        /// <summary>Steam's named top-level windows ("class 'title' (shown)"), and whether its own "Steam" one is among them.</summary>
        private static string SteamWindows(out bool hasSteamWindow)
        {
            var pids = new HashSet<uint>();
            try
            {
                foreach (var p in Process.GetProcessesByName("steam")) pids.Add((uint)p.Id);
                foreach (var p in Process.GetProcessesByName("steamwebhelper")) pids.Add((uint)p.Id);
            }
            catch (Exception) { }
            var list = new List<string>();
            bool found = false;
            try
            {
                EnumWindows((h, x) =>
                {
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (!pids.Contains(pid)) return true;
                    var title = new StringBuilder(128);
                    GetWindowText(h, title, 128);
                    if (title.Length == 0) return true;
                    var cls = new StringBuilder(128);
                    GetClassName(h, cls, 128);
                    string s = title.ToString();
                    if (s == "Steam") found = true;
                    list.Add(cls + " '" + s + "'" + (IsWindowVisible(h) ? " (shown)" : ""));
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            hasSteamWindow = found;
            list.Sort(StringComparer.Ordinal);
            return string.Join(", ", list.ToArray());
        }

        private static void LogManagerInfo(string s) { try { Playnite.SDK.LogManager.GetLogger().Info(s); } catch (Exception) { } }

        // ---------------- (0.99.42) on its own: a library update added Steam games ----------------

        private DispatcherTimer steamAutoTimer;
        private bool steamAutoHooked;

        public bool SteamPriorityAuto
        {
            get { return settings.SteamPriorityAuto; }
            set { if (settings.SteamPriorityAuto == value) return; settings.SteamPriorityAuto = value; SaveSettings(); Notify("SteamPriorityAuto"); }
        }

        /// <summary>Called once at start: watches the library for installed Steam games being added (or becoming installed).</summary>
        private void HookSteamPriorityAuto()
        {
            if (steamAutoHooked) return;
            steamAutoHooked = true;
            try
            {
                api.Database.Games.ItemCollectionChanged += (s, e) =>
                {
                    try { if (e.AddedItems != null && e.AddedItems.Any(g => g != null && g.PluginId == SteamLibIdSp && g.IsInstalled)) SteamAutoSoon(); } catch (Exception) { }
                };
                api.Database.Games.ItemUpdated += (s, e) =>
                {
                    try { if (e.UpdatedItems != null && e.UpdatedItems.Any(u => u.NewData != null && u.NewData.PluginId == SteamLibIdSp && u.NewData.IsInstalled && u.OldData != null && !u.OldData.IsInstalled)) SteamAutoSoon(); } catch (Exception) { }
                };
            }
            catch (Exception ex) { logger.Warn("Kippix: Steam high priority: couldn't watch the library: " + ex.Message); }
        }

        private static readonly Guid SteamLibIdSp = new Guid("cb91dfc9-b977-43bf-8e70-55f46e410fab");

        /// <summary>A library update adds games one after another: once it's been quiet for 8 s.</summary>
        private void SteamAutoSoon()
        {
            OnUi(() =>
            {
                if (!settings.SteamPriorityAuto) return;
                if (steamAutoTimer == null)
                {
                    steamAutoTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(8) };
                    steamAutoTimer.Tick += (s, e) => { steamAutoTimer.Stop(); SteamAutoCheck(); };
                }
                steamAutoTimer.Stop();
                steamAutoTimer.Start();
            });
        }

        private void SteamAutoCheck()
        {
            if (!settings.SteamPriorityAuto || steamPriorityBusy || AnyRunning) return;
            KippixShared.SafeWork.Queue(_ =>
            {
                string steamPath, steamExe;
                List<string> libs;
                bool downloading;
                List<Tuple<string, string>> toFix;
                try { toFix = SpScan(out steamPath, out steamExe, out libs, out downloading); } catch (Exception) { return; }
                if (toFix == null || toFix.Count == 0) return;
                if (downloading) { logger.Info("Kippix: Steam high priority (on its own): " + toFix.Count + " to change, but Steam is downloading or installing: left for the next library update"); return; }
                if (SteamRunningApp() != 0) { logger.Info("Kippix: Steam high priority (on its own): a game is running: left for later"); return; }
                logger.Info("Kippix: Steam high priority (on its own): new Steam games in the library: " + string.Join(", ", toFix.Select(x => x.Item2).ToArray()));
                OnUi(() => { if (!steamPriorityBusy && !AnyRunning) StartSteamPriority(true); });
            });
        }

        // ---------------- (0.99.43) the button's picture: the user's own icon (Images\steam-refresh.png, white on clear) ----------------
        // Put in Application resources as KX_SteamRefreshImage, used as the mask of a shape filled with KX_TextBrush, so it takes
        // the look's text colour like the other top-bar icons. Without the file: the line icon.

        private bool steamIconTried;
        private bool steamIconOk;

        public bool HasSteamPriorityIcon { get { LoadSteamPriorityIcon(); return steamIconOk; } }
        private bool libIconOk;
        public bool HasLibraryRefreshIcon { get { LoadSteamPriorityIcon(); return libIconOk; } }
        public bool NoLibraryRefreshIcon { get { LoadSteamPriorityIcon(); return !libIconOk; } }
        public bool NoSteamPriorityIcon { get { LoadSteamPriorityIcon(); return !steamIconOk; } }

        private void LoadSteamPriorityIcon()
        {
            if (steamIconTried) return;
            steamIconTried = true;
            try
            {
                string f = Path.Combine(ThemeDir, "Images", "steam-refresh.png");
                var img = File.Exists(f) ? Images.LoadUncached(f, 128) : null;
                if (img != null && Application.Current != null)
                {
                    Application.Current.Resources["KX_SteamRefreshImage"] = img;
                    steamIconOk = true;
                }
                // (0.99.44) the Refresh game library button beside it: the user's books-with-refresh icon
                string lf = Path.Combine(ThemeDir, "Images", "library-refresh.png");
                var limg = File.Exists(lf) ? Images.LoadUncached(lf, 128) : null;
                if (limg != null && Application.Current != null)
                {
                    Application.Current.Resources["KX_LibraryRefreshImage"] = limg;
                    libIconOk = true;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: Steam update button picture: " + ex.Message); }
        }


        private static void LogManagerWarn(string s) { try { Playnite.SDK.LogManager.GetLogger().Warn(s); } catch (Exception) { } }
    }

    /// <summary>(0.99.41) The small pop-up in the middle of the screen while Steam's games are changed (no buttons; it closes itself).</summary>
    internal class SteamPriorityPopup : Window
    {
        private readonly TextBlock title, line;
        private readonly ProgressBar bar;

        public SteamPriorityPopup(Window owner)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = owner.FontFamily; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var card = new Border
            {
                Width = 560,
                CornerRadius = new CornerRadius(20),
                Background = new LinearGradientBrush(Color.FromArgb(0xF6, 0x22, 0x25, 0x2D), Color.FromArgb(0xF6, 0x13, 0x15, 0x1A), 90),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(28, 24, 28, 26),
                Margin = new Thickness(30),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 36, ShadowDepth = 8, Opacity = 0.6, Color = Colors.Black }
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var iconBg = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Top };
            var geo = Application.Current != null ? Application.Current.TryFindResource("KX_IconDownload") as Geometry : null;
            if (geo != null)
                iconBg.Child = new System.Windows.Shapes.Path { Data = geo, Stroke = Brushes.White, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 28, Height = 28, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(iconBg);
            var sp = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sp, 1);
            title = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            line = new TextBlock { FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            bar = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 16, 0, 0), Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(0) };
            sp.Children.Add(title);
            sp.Children.Add(line);
            sp.Children.Add(bar);
            g.Children.Add(sp);
            card.Child = g;
            Content = card;
            Opacity = 0;
            Loaded += (s, e) => BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        }

        public void Set(string head, string text, bool working)
        {
            title.Text = head ?? "";
            line.Text = text ?? "";
            bar.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>From another thread: the next step.</summary>
        public void SetSoon(string text)
        {
            try { Dispatcher.BeginInvoke(new Action(() => { line.Text = text ?? ""; bar.Visibility = Visibility.Visible; })); } catch (Exception) { }
        }

        public void CloseAfter(int seconds)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (s2, e2) => { try { Close(); } catch (Exception) { } };
                BeginAnimation(OpacityProperty, fade);
            };
            t.Start();
        }
    }
}
