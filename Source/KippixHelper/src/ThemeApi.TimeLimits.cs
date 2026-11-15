using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Daily time limits (Profiles on; any profile but Guest, set on the profile's own page: Settings › Profiles › name ›
    /// Daily time limit, behind the grown-ups' PIN). A profile gets minutes a day (Monday to Friday and the weekend apart) and
    /// optionally hours of the day games may be played. Only games started from Playnite while that profile is on count: the
    /// time is added up while the game runs (a timer that runs only then), kept on the profile, and starts again each day.
    /// Messages at 10 and 2 minutes left (in the game: the overlay's card; in Playnite: the theme's card). At zero a game can't
    /// be started (the "Time's up for today" page; a grown-up's PIN gives +15 / +30 / +60 minutes for today), and a game still
    /// running is told time's up; with "Close the game when time runs out" it is asked to close as its close button would
    /// (WM_CLOSE / CloseMainWindow, never killed) after a 2-minute warning.
    /// </summary>
    public partial class ThemeApi
    {
        private DispatcherTimer limitTimer;
        private DateTime limitTickAt, limitSavedAt;
        private string limitProfileId;          // who the running game counts for
        private Guid limitGameId;
        private string limitGameName;
        private bool limitWarn10, limitWarn2, limitWarn0, limitCloseAsked;
        private DateTime limitNagAt, limitCloseAt;
        private DispatcherTimer limitPageWait;
        private DateTime limitPageWaitSince;
        private Guid limitBlockedGame;          // the game that couldn't start (started after a grown-up gives more time)

        private static readonly int[] LimitDaySteps = { 0, 30, 45, 60, 90, 120, 150, 180, 240, 300, 360 };
        private static readonly int[] LimitFromSteps = { 6 * 60, 6 * 60 + 30, 7 * 60, 7 * 60 + 30, 8 * 60, 8 * 60 + 30, 9 * 60, 10 * 60, 11 * 60, 12 * 60, 14 * 60, 16 * 60 };
        private static readonly int[] LimitUntilSteps = { 16 * 60, 17 * 60, 18 * 60, 18 * 60 + 30, 19 * 60, 19 * 60 + 30, 20 * 60, 20 * 60 + 30, 21 * 60, 21 * 60 + 30, 22 * 60, 23 * 60, 24 * 60 };

        // =====================================================================
        // Working out what's left
        // =====================================================================

        private static string LimitDayKey(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        /// <summary>From NormalizeSettings: minutes 0..6 h, hours inside the day with From before Until.</summary>
        private void NormalizeTimeLimits()
        {
            if (settings.Profiles == null) return;
            foreach (var p in settings.Profiles)
            {
                if (p == null) continue;
                p.LimitWeekdayMin = Math.Max(0, Math.Min(360, p.LimitWeekdayMin));
                p.LimitWeekendMin = Math.Max(0, Math.Min(360, p.LimitWeekendMin));
                p.LimitUntil = Math.Max(60, Math.Min(24 * 60, p.LimitUntil));
                p.LimitFrom = Math.Max(0, Math.Min(p.LimitUntil - 30, p.LimitFrom));
                p.LimitExtraMin = Math.Max(0, Math.Min(24 * 60, p.LimitExtraMin));
                if (p.LimitUsedSec < 0) p.LimitUsedSec = 0;
            }
        }

        private PlayerProfile LimitProfileById(string id)
        {
            if (string.IsNullOrEmpty(id) || id == GuestId || settings.Profiles == null) return null;
            return settings.Profiles.FirstOrDefault(p => p != null && p.Id == id);
        }

        private bool LimitApplies(PlayerProfile p)
        {
            return p != null && ProfilesOn && p.Id != GuestId && p.LimitOn;
        }

        /// <summary>Any profile has a daily time limit (then Guest needs the PIN: it would be a way round it).</summary>
        private bool AnyTimeLimits
        {
            get { return ProfilesOn && settings.Profiles.Any(p => p != null && p.LimitOn); }
        }

        private static bool IsWeekend(DateTime d) { return d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday; }

        private static int LimitAllowance(PlayerProfile p, DateTime now) { return Math.Max(0, IsWeekend(now) ? p.LimitWeekendMin : p.LimitWeekdayMin); }

        private static long LimitUsedToday(PlayerProfile p, DateTime now) { return p.LimitDay == LimitDayKey(now) ? Math.Max(0, p.LimitUsedSec) : 0; }

        private static int LimitExtraToday(PlayerProfile p, DateTime now) { return p.LimitExtraDay == LimitDayKey(now) ? Math.Max(0, p.LimitExtraMin) : 0; }

        /// <summary>The counters belong to today (a new day starts them again).</summary>
        private static void LimitRollDay(PlayerProfile p, DateTime now)
        {
            string d = LimitDayKey(now);
            if (p.LimitDay != d) { p.LimitDay = d; p.LimitUsedSec = 0; }
            if (p.LimitExtraDay != d) { p.LimitExtraDay = d; p.LimitExtraMin = 0; }
        }

        /// <summary>
        /// Minutes of play left now (double.MaxValue: no limit). reason: 1 the day's time, 2 too early, 3 too late (the allowed hours).
        /// Doesn't change the profile (safe to ask from any thread).
        /// </summary>
        private static double LimitMinutesLeft(PlayerProfile p, DateTime now, out int reason)
        {
            reason = 0;
            double left = double.MaxValue;
            if (p == null || !p.LimitOn) return left;
            int allow = LimitAllowance(p, now);
            if (allow > 0)
            {
                left = allow + LimitExtraToday(p, now) - LimitUsedToday(p, now) / 60.0;
                reason = 1;
            }
            if (p.LimitHoursOn)
            {
                double t = now.TimeOfDay.TotalMinutes;
                double windowLeft = -1;
                if (t >= p.LimitFrom && t < p.LimitUntil) windowLeft = p.LimitUntil - t;
                // extra time from a grown-up opens the hours too, from when it was given
                if (p.LimitPassDay == LimitDayKey(now) && t >= p.LimitPassFrom && t < p.LimitPassUntil) windowLeft = Math.Max(windowLeft, p.LimitPassUntil - t);
                if (windowLeft < 0) { reason = t < p.LimitFrom ? 2 : 3; return 0; }
                if (windowLeft < left) { left = windowLeft; reason = t < p.LimitFrom ? 2 : 3; }
            }
            return left;
        }

        private static string LimitClock(int minutes)
        {
            if (minutes >= 24 * 60) return Loc("LOC_KX_LimitMidnight", "midnight");
            return new DateTime(2000, 1, 1).AddMinutes(minutes).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private string LimitDayText(int minutes)
        {
            if (minutes <= 0) return Loc("LOC_KX_LimitNoLimit", "No limit");
            return Duration((ulong)minutes * 60);
        }

        /// <summary>"35 min left today" (null: no limit).</summary>
        private string LimitLeftText(PlayerProfile p)
        {
            if (!LimitApplies(p)) return null;
            int reason;
            double left = LimitMinutesLeft(p, DateTime.Now, out reason);
            if (left == double.MaxValue) return null;
            if (left <= 0) return reason == 2 ? string.Format(Loc("LOC_KX_LimitFromAt", "Games from {0}"), LimitClock(p.LimitFrom)) : Loc("LOC_KX_LimitNoneLeft", "Time's up for today");
            return string.Format(Loc("LOC_KX_LimitLeft", "{0} left today"), Duration((ulong)Math.Ceiling(left) * 60));
        }

        // =====================================================================
        // A game starting, running, stopping (from HelperPlugin)
        // =====================================================================

        /// <summary>OnGameStarting: true when this profile has no time left (the start is cancelled and the Time's up page shows).</summary>
        public bool TimeLimitBlocksStart(Game g)
        {
            try
            {
                var p = profileNow;
                if (!LimitApplies(p)) return false;
                int reason;
                double left = LimitMinutesLeft(p, DateTime.Now, out reason);
                if (left > 0) return false;
                logger.Info("Kippix: time limit: " + (g != null ? g.Name : "a game") + " not started, " + p.Name + " has no time left (" + reason + ")");
                Guid id = g != null ? g.Id : Guid.Empty;
                OnUi(() =>
                {
                    limitBlockedGame = id;
                    if (IsFullscreen) ShowTimesUpPage();
                    else try { api.Notifications.Add(new Playnite.SDK.NotificationMessage("kippix-timesup", string.Format(Loc("LOC_KX_LimitBlockedNote", "Time's up for today for {0}: games can't be started."), p.Name), Playnite.SDK.NotificationType.Info)); } catch (Exception) { }
                });
                return true;
            }
            catch (Exception ex) { logger.Warn("Kippix: time limit check: " + ex.Message); return false; }
        }

        /// <summary>OnGameStarted: the time starts counting for whoever is playing.</summary>
        public void TimeLimitGameStarted(Game g)
        {
            OnUi(() =>
            {
                var p = profileNow;
                if (!LimitApplies(p)) { StopLimitTimer(); return; }
                // (issue #11) something else is running and being counted already: the count carries on as it is (its warnings
                // aren't forgotten), the messages name this game now
                if (limitTimer != null && limitTimer.IsEnabled && limitProfileId == p.Id && Running.Count > 1)
                {
                    limitGameId = g != null ? g.Id : Guid.Empty;
                    limitGameName = g != null ? g.Name : "";
                    return;
                }
                limitProfileId = p.Id;
                limitGameId = g != null ? g.Id : Guid.Empty;
                limitGameName = g != null ? g.Name : "";
                limitWarn10 = limitWarn2 = limitWarn0 = limitCloseAsked = false;
                limitNagAt = limitCloseAt = DateTime.MinValue;
                limitTickAt = limitSavedAt = DateTime.Now;
                if (limitTimer == null)
                {
                    limitTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(15) };
                    limitTimer.Tick += (s, e) => LimitTick(false);
                }
                limitTimer.Start();
                int reason;
                double left = LimitMinutesLeft(p, DateTime.Now, out reason);
                logger.Info("Kippix: time limit: counting for " + p.Name + (left == double.MaxValue ? " (no limit today)" : ", " + Math.Round(left) + " min left"));
            });
        }

        /// <summary>OnGameStopped: the last bit is added and saved. With no time left, the Time's up page shows when Playnite is back.</summary>
        public void TimeLimitGameStopped(Game g)
        {
            OnUi(() =>
            {
                if (limitTimer == null || !limitTimer.IsEnabled) return;
                if (g != null && limitGameId != Guid.Empty && g.Id != limitGameId) return;   // (another game: this one carries on)
                // (issue #11) others are still running: the time keeps counting, for the one started last (asked to close too, if time's up)
                var still = Running.Newest();
                if (still != null && (g == null || still.Id != g.Id))
                {
                    limitGameId = still.Id;
                    limitGameName = still.Name;
                    limitCloseAsked = false;
                    return;
                }
                LimitTick(true);
                StopLimitTimer();
                var p = LimitProfileById(limitProfileId);
                if (LimitApplies(p) && IsFullscreen)
                {
                    int reason;
                    if (LimitMinutesLeft(p, DateTime.Now, out reason) <= 0) ShowTimesUpWhenBack();
                }
            });
        }

        private void StopLimitTimer()
        {
            if (limitTimer != null) limitTimer.Stop();
        }

        private void LimitTick(bool final)
        {
            try
            {
                var p = LimitProfileById(limitProfileId);
                if (!LimitApplies(p)) { StopLimitTimer(); return; }
                var now = DateTime.Now;
                double sec = (now - limitTickAt).TotalSeconds;
                limitTickAt = now;
                if (sec < 0 || sec > 180) sec = 0;   // the PC slept or the clock changed: that isn't play
                if (now.Date != now.AddSeconds(-sec).Date) sec = now.TimeOfDay.TotalSeconds;   // past midnight: only today's part counts today
                LimitRollDay(p, now);
                p.LimitUsedSec += (long)Math.Round(sec);
                if (final || (now - limitSavedAt).TotalMinutes >= 3) { limitSavedAt = now; SaveSettings(); }
                if (final) return;

                int reason;
                double left = LimitMinutesLeft(p, now, out reason);
                if (left == double.MaxValue) return;
                string game = string.IsNullOrEmpty(limitGameName) ? Loc("LOC_KX_LimitTheGame", "The game") : limitGameName;
                if (left > 0 && left <= 10.05 && !limitWarn10 && !(left <= 2.05))
                {
                    limitWarn10 = true;
                    LimitMessage(string.Format(Loc("LOC_KX_LimitNMinLeft", "{0} minutes left today"), Math.Ceiling(left)),
                                 reason >= 2 ? string.Format(Loc("LOC_KX_LimitHoursEnd", "Games are until {0} today."), LimitClock(p.LimitUntil)) : Loc("LOC_KX_LimitSaveSoon", "A good moment to find a save point soon."));
                }
                else if (left > 0 && left <= 2.05 && !limitWarn2)
                {
                    limitWarn2 = limitWarn10 = true;
                    LimitMessage(string.Format(Loc("LOC_KX_LimitNMinLeft", "{0} minutes left today"), Math.Max(1, Math.Ceiling(left))),
                                 p.LimitCloseGame ? string.Format(Loc("LOC_KX_LimitClosesSoon", "{0} closes then. Save your game now."), game) : Loc("LOC_KX_LimitSaveNow", "Time to save your game."));
                }
                else if (left <= 0)
                {
                    if (!limitWarn0 || (now - limitNagAt).TotalMinutes >= 5)
                    {
                        bool first = !limitWarn0;
                        limitWarn0 = limitWarn2 = limitWarn10 = true;
                        limitNagAt = now;
                        if (!p.LimitCloseGame || !first)
                            LimitMessage(Loc("LOC_KX_LimitTimesUp", "Time's up for today"),
                                         p.LimitCloseGame ? string.Format(Loc("LOC_KX_LimitStillOpen", "{0} is still open. Please save and close it."), game) : Loc("LOC_KX_LimitPleaseStop", "Please save your game and stop playing."));
                    }
                    if (p.LimitCloseGame && !limitCloseAsked)
                    {
                        limitCloseAsked = true;
                        limitCloseAt = now;
                        LimitMessage(Loc("LOC_KX_LimitTimesUp", "Time's up for today"), string.Format(Loc("LOC_KX_LimitClosing", "Closing {0} now."), game));
                        CloseRunningGameGently(limitGameId);
                    }
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: time limit: " + ex.Message); }
        }

        /// <summary>A message where it can be seen: the theme's card when Playnite is in front, else a card over the game.</summary>
        private void LimitMessage(string title, string text)
        {
            logger.Info("Kippix: time limit: " + title + " (" + text + ")");
            try
            {
                var w = Application.Current != null ? Application.Current.MainWindow : null;
                if (w != null && w.IsActive && !gameRunningNow && IsFullscreen) { ShowCard(Loc("LOC_KX_LimitHeader", "DAILY TIME"), title, text, "", null); return; }
                if (plugin.OverlayRunning) { plugin.Overlay.Toast(title, text); return; }
                IntPtr anchor = GetForegroundWindow();
                KippixOverlay.ToastWindow.ShowToast(title, text, anchor, KippixOverlay.OverlayTheme.Get(0));
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: time limit message: " + ex.Message);
                try { api.Notifications.Add(new Playnite.SDK.NotificationMessage("kippix-limit-" + DateTime.Now.Ticks, title + ". " + text, Playnite.SDK.NotificationType.Info)); } catch (Exception) { }
            }
        }

        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        private const uint WM_CLOSE = 0x0010;

        /// <summary>
        /// Asks the running game to close the way its own close button would (its window gets WM_CLOSE; programs from its install
        /// folder are asked with CloseMainWindow). Nothing is ever killed: a game that asks "are you sure?" or ignores it stays
        /// open, and the message keeps coming back every few minutes.
        /// </summary>
        private void CloseRunningGameGently(Guid gameId)
        {
            IntPtr hwnd = IntPtr.Zero;
            // (issue #11) this game's own window when it's known; the overlay's (it knows one game) only when nothing else runs
            try
            {
                var entry = Running.Get(gameId);
                if (entry != null) hwnd = entry.Window;
                if (hwnd == IntPtr.Zero && Running.Count <= 1 && plugin.Overlay != null) hwnd = plugin.Overlay.GameWindow;
            }
            catch (Exception) { }
            string dir = null;
            try
            {
                var g = gameId != Guid.Empty ? api.Database.Games.Get(gameId) : null;
                if (g != null && !string.IsNullOrEmpty(g.InstallDirectory))
                {
                    dir = g.InstallDirectory;
                    try { dir = api.ExpandGameVariables(g, dir); } catch (Exception) { }
                    dir = Directory.Exists(dir) ? Path.GetFullPath(dir).TrimEnd('\\') + "\\" : null;
                }
            }
            catch (Exception) { dir = null; }
            int own = Process.GetCurrentProcess().Id;
            KippixShared.SafeWork.Queue(_ =>
            {
                int asked = 0;
                try
                {
                    if (hwnd != IntPtr.Zero && IsWindow(hwnd))
                    {
                        uint pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        if (pid != 0 && pid != (uint)own && PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) asked++;
                    }
                    // a short install folder ("C:\") would take in every program: only a real game folder
                    if (dir != null && dir.Length > 8)
                    {
                        foreach (var proc in Process.GetProcesses())
                        {
                            try
                            {
                                if (proc.Id == own || proc.Id <= 4) continue;
                                string path = Audio.ProcessPath((uint)proc.Id);
                                if (string.IsNullOrEmpty(path) || !path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;
                                if (proc.MainWindowHandle != IntPtr.Zero && proc.CloseMainWindow()) asked++;
                            }
                            catch (Exception) { }
                            finally { proc.Dispose(); }
                        }
                    }
                }
                catch (Exception ex) { logger.Warn("Kippix: time limit: closing the game: " + ex.Message); }
                logger.Info("Kippix: time limit: asked the game to close (" + asked + " window(s))");
                if (asked == 0) OnUi(() => LimitMessage(Loc("LOC_KX_LimitTimesUp", "Time's up for today"), Loc("LOC_KX_LimitCantClose", "The game couldn't be closed by itself. Please save and close it now.")));
            });
        }

        // =====================================================================
        // Time's up for today (a page over everything, fullscreen)
        // =====================================================================

        /// <summary>Once Playnite is in front again after the game closed (at most 3 minutes of waiting).</summary>
        private void ShowTimesUpWhenBack()
        {
            limitPageWaitSince = DateTime.Now;
            if (limitPageWait == null)
            {
                limitPageWait = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
                limitPageWait.Tick += (s, e) =>
                {
                    if ((DateTime.Now - limitPageWaitSince).TotalMinutes > 3) { limitPageWait.Stop(); return; }
                    var w = Application.Current != null ? Application.Current.MainWindow : null;
                    if (w == null || !w.IsActive || gameRunningNow || playniteDialogsOpen > 0 || kbOpen || confirmOpen) return;
                    limitPageWait.Stop();
                    limitBlockedGame = Guid.Empty;
                    ShowTimesUpPage();
                };
            }
            limitPageWait.Start();
        }

        private void ShowTimesUpPage()
        {
            if (!IsFullscreen) return;
            if (openPanel == "page" && pageKey == "timesup") { FocusPageSoon(null); return; }
            if (openPanel != null) ClosePanels();
            pageStack.Clear();
            OpenPage("timesup", "", false);
        }

        private void BuildTimesUpPage()
        {
            var p = profileNow;
            string name = p != null ? p.Name : "";
            PageTitle = Loc("LOC_KX_LimitTimesUp", "Time's up for today");
            if (!LimitApplies(p))
            {
                PageSub = Loc("LOC_KX_LimitNoLimitNow", "There's no time limit now.");
                PageRows.Add(new PageRow { Text = Loc("LOC_KX_LimitOk", "OK"), Icon = "check", Key = "ok", Command = new RelayCommand(_ => ClosePanels()) });
                return;
            }
            var now = DateTime.Now;
            int reason;
            double left = LimitMinutesLeft(p, now, out reason);
            if (left > 0)
            {
                PageTitle = string.Format(Loc("LOC_KX_LimitLeft", "{0} left today"), Duration((ulong)Math.Ceiling(left) * 60));
                PageSub = string.Format(Loc("LOC_KX_LimitHaveFun", "Have fun, {0}!"), name);
            }
            else if (reason == 2)
                PageSub = string.Format(Loc("LOC_KX_LimitTooEarly", "Morning, {0}! Games can start from {1}."), name, LimitClock(p.LimitFrom));
            else
                PageSub = string.Format(Loc("LOC_KX_LimitSeeTomorrow", "Great playing today, {0}! Games can start again tomorrow{1}."), name,
                                        p.LimitHoursOn ? string.Format(Loc("LOC_KX_LimitFromSuffix", " from {0}"), LimitClock(p.LimitFrom)) : "");
            long used = LimitUsedToday(p, now);
            int allow = LimitAllowance(p, now);
            PageStats = new List<PageStat>
            {
                new PageStat { Value = used >= 60 ? Duration((ulong)used) : Loc("LOC_KX_LimitNone0", "0 min"), Label = Loc("LOC_KX_LimitPlayedToday", "played today") }
            };
            if (allow > 0) PageStats.Add(new PageStat { Value = Duration((ulong)(allow + LimitExtraToday(p, now)) * 60), Label = Loc("LOC_KX_LimitTodaysTime", "today's time") });
            if (p.LimitHoursOn) PageStats.Add(new PageStat { Value = LimitClock(p.LimitFrom) + "–" + LimitClock(p.LimitUntil), Label = Loc("LOC_KX_LimitGameHours", "game hours") });

            if (left <= 0)
            {
                AddPageHeader(Loc("LOC_KX_LimitMoreTime", "MORE TIME (A GROWN-UP'S PIN)"));
                if (string.IsNullOrEmpty(settings.ProfilesPin))
                    PageRows.Add(new PageRow { Text = Loc("LOC_KX_LimitNoPin", "No grown-ups' PIN is set"), Sub = Loc("LOC_KX_LimitNoPinSub", "Set one in Settings & Customisation › Profiles to give more time from here"), Icon = "lock", Key = "nopin" });
                else
                    foreach (int m in new[] { 15, 30, 60 })
                    {
                        int mins = m;
                        PageRows.Add(new PageRow
                        {
                            Text = string.Format(Loc("LOC_KX_LimitPlusN", "+{0} more"), Duration((ulong)mins * 60)), Icon = "add", Key = "extra" + mins,
                            Sub = limitBlockedGame != Guid.Empty ? Loc("LOC_KX_LimitPlusSubGame", "Asks for the PIN, then the game starts") : Loc("LOC_KX_LimitPlusSub", "Asks for the PIN"),
                            Command = new RelayCommand(_ => AskPin(Loc("LOC_KX_LimitPinTitle", "A grown-up's PIN, for more time today"), () => GiveExtraTime(p, mins, true)))
                        });
                    }
            }
            AddPageHeader(Loc("LOC_KX_LimitOther", "OR"));
            if (ProfilesOn)
                PageRows.Add(new PageRow { Text = Loc("LOC_KX_WhosPlaying", "Who's playing?"), Sub = Loc("LOC_KX_LimitSwitchSub", "Someone else's turn"), Icon = "friends", Key = "who",
                                           Command = new RelayCommand(_ => { ClosePanels(); ui.BeginInvoke(new Action(OpenWho), DispatcherPriority.Loaded); }) });
            PageRows.Add(new PageRow { Text = Loc("LOC_KX_LimitOk", "OK"), Sub = Loc("LOC_KX_LimitOkSub", "Back to the games (they can't start until then)"), Icon = "check", Key = "ok", Command = new RelayCommand(_ => ClosePanels()) });
            PageHint = Loc("LOC_KX_PageHintBack", "");
        }

        /// <summary>More time today: added to the day's time and, with allowed hours, opens the hours from now for as long.</summary>
        private void GiveExtraTime(PlayerProfile p, int minutes, bool startGame)
        {
            if (p == null) return;
            var now = DateTime.Now;
            LimitRollDay(p, now);
            if (LimitAllowance(p, now) > 0)
            {
                // the day's time is what ran out: count from what's been played (time already over doesn't eat into the extra)
                double over = LimitUsedToday(p, now) / 60.0 - (LimitAllowance(p, now) + p.LimitExtraMin);
                p.LimitExtraMin += minutes + (over > 0 ? (int)Math.Ceiling(over) : 0);
            }
            if (p.LimitHoursOn)
            {
                int t = (int)now.TimeOfDay.TotalMinutes;
                bool passToday = p.LimitPassDay == LimitDayKey(now) && t < p.LimitPassUntil;
                p.LimitPassDay = LimitDayKey(now);
                p.LimitPassFrom = passToday ? Math.Min(p.LimitPassFrom, t) : t;
                int from = Math.Max(t, passToday ? p.LimitPassUntil : (t >= p.LimitFrom && t < p.LimitUntil ? p.LimitUntil : t));
                p.LimitPassUntil = Math.Min(24 * 60, from + minutes);
            }
            SaveSettings();
            PlaySound("select");
            limitWarn10 = limitWarn2 = limitWarn0 = limitCloseAsked = false;
            logger.Info("Kippix: time limit: +" + minutes + " min for " + p.Name);
            Guid game = limitBlockedGame;
            limitBlockedGame = Guid.Empty;
            if (openPanel == "page" && pageKey == "timesup") ClosePanels();
            if (openPanel == "options" && optionsPage == "profiletime") BuildProfileOptionRows();
            ShowCard(Loc("LOC_KX_LimitHeader", "DAILY TIME"), string.Format(Loc("LOC_KX_LimitAdded", "+{0} for {1}"), Duration((ulong)minutes * 60), p.Name), LimitLeftText(p) ?? "", "", null);
            if (startGame && game != Guid.Empty && p == profileNow)
                ui.BeginInvoke(new Action(() => { try { api.StartGame(game); } catch (Exception ex) { logger.Warn("Kippix: time limit: starting: " + ex.Message); } }), DispatcherPriority.ContextIdle);
        }

        // =====================================================================
        // Settings › Profiles › name › Daily time limit (behind the PIN)
        // =====================================================================

        /// <summary>A profile's limit in a line: off, or school days / weekends (and the hours).</summary>
        private string LimitSummary(PlayerProfile p)
        {
            if (!p.LimitOn) return Loc("LOC_KX_LimitOffSub", "Off: no limit");
            var bits = new List<string>();
            bits.Add(string.Format(Loc("LOC_KX_LimitWeekShort", "School days {0}"), LimitDayText(p.LimitWeekdayMin)));
            bits.Add(string.Format(Loc("LOC_KX_LimitWeekendShort", "weekends {0}"), LimitDayText(p.LimitWeekendMin)));
            if (p.LimitHoursOn) bits.Add(LimitClock(p.LimitFrom) + "–" + LimitClock(p.LimitUntil));
            return string.Join(" · ", bits.ToArray());
        }

        /// <summary>The limit page was opened from Settings › Profiles' own DAILY TIME LIMITS (B goes back there), not from the profile's page.</summary>
        private bool limitFromProfilesRoot;

        /// <summary>Opens a profile's limit page (after the PIN once per session).</summary>
        private void OpenProfileTime(PlayerProfile p, bool fromRoot)
        {
            Action go = () =>
            {
                editingProfileId = p.Id;
                limitFromProfilesRoot = fromRoot;
                if (openPanel != "options") TogglePanel("options");
                SetOptionsPage("profiletime");
            };
            if (!string.IsNullOrEmpty(settings.ProfilesPin) && !unlockedNow.Contains("limits"))
                AskPin(Loc("LOC_KX_LimitPinSettings", "The PIN, to change the time limit"), () => { unlockedNow.Add("limits"); go(); });
            else { PlaySound("select"); go(); }
        }

        /// <summary>The profile page's row (opens the limit page, after the PIN once per profile session).</summary>
        private PickRow ProfileTimeRow(PlayerProfile p)
        {
            return new PickRow
            {
                Key = "profiletime", Icon = "hourglass", Text = Loc("LOC_KX_LimitTitle", "Daily time limit"), Sub = LimitSummary(p),
                Command = new RelayCommand(_ => OpenProfileTime(p, false))
            };
        }

        /// <summary>Settings › Profiles: DAILY TIME LIMITS, a row per profile (Guest has none) with its limit; A opens its limit page.</summary>
        private void AddProfilesLimitRows(System.Collections.ObjectModel.ObservableCollection<PickRow> rows)
        {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LimitsHeader", "DAILY TIME LIMITS") });
            foreach (var prof in settings.Profiles.Where(x => x != null && x.Id != GuestId).ToList())
            {
                var p = prof;
                string left = p.LimitOn ? LimitLeftText(p) : null;
                rows.Add(new PickRow
                {
                    Key = "limit:" + p.Id, Icon = "hourglass", Text = p.Name, IsSelected = false,
                    Sub = LimitSummary(p) + (left != null ? "  ·  " + left : ""),
                    Command = new RelayCommand(_ => OpenProfileTime(p, true))
                });
            }
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "limitsinfo", Text = Loc("LOC_KX_LimitsInfo", "Screen time for each profile: minutes a day (school days and weekends apart) and the hours games may be played. Behind the grown-ups' PIN.") });
        }

        private static int LimitNextStep(int[] steps, int now)
        {
            int i = Array.IndexOf(steps, now);
            if (i < 0) { i = 0; while (i < steps.Length && steps[i] <= now) i++; return steps[i % steps.Length]; }
            return steps[(i + 1) % steps.Length];
        }

        private void BuildProfileTimeRows()
        {
            var rows = ProfileOptionRows;
            var p = EditingProfile;
            if (p == null) { rows.Add(new PickRow { IsInfo = true, Key = "gone", Text = Loc("LOC_KX_ProfileGone", "That profile isn't there any more") }); return; }
            Action again = () => BuildProfileOptionRows();
            Action changed = () =>
            {
                SaveSettings();
                PlaySound("move");
                // (a game running now counts against the new limit from its next tick)
                if (p.Id == limitProfileId && limitTimer != null && limitTimer.IsEnabled) { limitWarn10 = limitWarn2 = limitWarn0 = false; }
            };
            if (string.IsNullOrEmpty(settings.ProfilesPin))
                rows.Add(new PickRow { IsInfo = true, Icon = "lock", Key = "limitpin", Text = Loc("LOC_KX_LimitSetPinFirst", "Set the grown-ups' PIN on the Profiles page: then only a grown-up can change this or give more time") });
            var now = DateTime.Now;
            rows.Add(BoardSwitch("hourglass", "limiton", Loc("LOC_KX_LimitTitle", "Daily time limit"), p.LimitOn,
                                 string.Format(Loc("LOC_KX_LimitOnSub", "Games started from Playnite as {0} count"), p.Name), Loc("LOC_KX_LimitOffSub", "Off: no limit"),
                                 () => { p.LimitOn = !p.LimitOn; changed(); BoardRowsAgain(again, "limiton"); }));
            if (!p.LimitOn) return;

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LimitHowLong", "HOW LONG A DAY") });
            rows.Add(new PickRow { Key = "limitweek", Icon = "calendar", Text = Loc("LOC_KX_LimitWeekdays", "Monday to Friday"), Sub = LimitDayText(p.LimitWeekdayMin) + Loc("LOC_KX_AccessNext", ""),
                                   Command = new RelayCommand(_ => { p.LimitWeekdayMin = LimitNextStep(LimitDaySteps, p.LimitWeekdayMin); changed(); BoardRowsAgain(again, "limitweek"); }) });
            rows.Add(new PickRow { Key = "limitweekend", Icon = "calendar", Text = Loc("LOC_KX_LimitWeekend", "Saturday and Sunday"), Sub = LimitDayText(p.LimitWeekendMin) + Loc("LOC_KX_AccessNext", ""),
                                   Command = new RelayCommand(_ => { p.LimitWeekendMin = LimitNextStep(LimitDaySteps, p.LimitWeekendMin); changed(); BoardRowsAgain(again, "limitweekend"); }) });

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LimitWhen", "WHEN") });
            rows.Add(BoardSwitch("clock", "limithours", Loc("LOC_KX_LimitHours", "Only at certain times of day"), p.LimitHoursOn,
                                 string.Format(Loc("LOC_KX_LimitHoursOnSub", "From {0} until {1}"), LimitClock(p.LimitFrom), LimitClock(p.LimitUntil)), Loc("LOC_KX_LimitHoursOffSub", "Off: any time of day"),
                                 () => { p.LimitHoursOn = !p.LimitHoursOn; changed(); BoardRowsAgain(again, "limithours"); }));
            if (p.LimitHoursOn)
            {
                rows.Add(new PickRow { Key = "limitfrom", Icon = "sun", Text = Loc("LOC_KX_LimitFrom", "From"), Sub = LimitClock(p.LimitFrom) + Loc("LOC_KX_AccessNext", ""),
                                       Command = new RelayCommand(_ =>
                                       {
                                           int v = LimitNextStep(LimitFromSteps, p.LimitFrom);
                                           if (v >= p.LimitUntil) v = LimitFromSteps[0];   // (always before Until)
                                           p.LimitFrom = v; changed(); BoardRowsAgain(again, "limitfrom");
                                       }) });
                rows.Add(new PickRow { Key = "limituntil", Icon = "moon", Text = Loc("LOC_KX_LimitUntil", "Until"), Sub = LimitClock(p.LimitUntil) + Loc("LOC_KX_AccessNext", ""),
                                       Command = new RelayCommand(_ =>
                                       {
                                           int v = LimitNextStep(LimitUntilSteps, p.LimitUntil);
                                           int guard = 0;
                                           while (v <= p.LimitFrom && guard++ < LimitUntilSteps.Length) v = LimitNextStep(LimitUntilSteps, v);
                                           p.LimitUntil = v; changed(); BoardRowsAgain(again, "limituntil");
                                       }) });
            }

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LimitRunsOut", "WHEN TIME RUNS OUT") });
            rows.Add(BoardSwitch("close", "limitclose", Loc("LOC_KX_LimitCloseGame", "Close the game when time runs out"), p.LimitCloseGame,
                                 Loc("LOC_KX_LimitCloseOnSub", "A 2-minute warning, then Kippix asks the game to close, as its own close button would. Unsaved progress can be lost. It's never forced: a game that asks first, or doesn't listen, stays open"),
                                 Loc("LOC_KX_LimitCloseOffSub", "Off: a message says time's up (every few minutes) and the game keeps running"),
                                 () => { p.LimitCloseGame = !p.LimitCloseGame; changed(); BoardRowsAgain(again, "limitclose"); }));
            rows.Add(new PickRow { IsInfo = true, Icon = "info", Key = "limitstart", Text = Loc("LOC_KX_LimitStartInfo", "With no time left, games can't be started: the Time's up page shows, where a grown-up's PIN gives more time. Guest needs the PIN while any profile has a limit") });

            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_LimitToday", "TODAY") });
            long used = LimitUsedToday(p, now);
            string leftText = LimitLeftText(p);
            rows.Add(new PickRow { IsInfo = true, Icon = "clock", Key = "limitused",
                                   Text = string.Format(Loc("LOC_KX_LimitPlayedTodayRow", "Played today: {0}"), used >= 60 ? Duration((ulong)used) : Loc("LOC_KX_LimitNone0", "0 min")) + (leftText != null ? "  ·  " + leftText : "") });
            foreach (int m in new[] { 15, 30, 60 })
            {
                int mins = m;
                rows.Add(new PickRow { Key = "limitplus" + mins, Icon = "add", Text = string.Format(Loc("LOC_KX_LimitGiveN", "Give {0} more today"), Duration((ulong)mins * 60)), Sub = Loc("LOC_KX_LimitGiveSub", "Only for today"),
                                       Command = new RelayCommand(_ => { GiveExtraTime(p, mins, false); BoardRowsAgain(again, "limitplus" + mins); }) });
            }
            if (used > 0 || LimitExtraToday(p, now) > 0)
                rows.Add(new PickRow { Key = "limitclear", Icon = "refresh", Text = Loc("LOC_KX_LimitStartAgain", "Start today again"), Sub = Loc("LOC_KX_LimitStartAgainSub", "Today's played time and extra time go back to nothing"),
                                       Command = new RelayCommand(_ => { p.LimitUsedSec = 0; p.LimitExtraMin = 0; p.LimitPassDay = null; p.LimitDay = LimitDayKey(DateTime.Now); limitWarn10 = limitWarn2 = limitWarn0 = false; changed(); BoardRowsAgain(again, "limiton"); }) });
        }
    }
}
