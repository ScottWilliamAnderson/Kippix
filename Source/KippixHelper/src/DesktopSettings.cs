using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Playnite.SDK;

namespace KippixHelper
{
    /// <summary>
    /// The add-on's page in Playnite's desktop settings (Add-ons > Extension settings > Kippix Helper).
    /// For the things that are easier with a keyboard and mouse, like pasting the Steam Web API key.
    /// Everything else is in the fullscreen theme's own Options.
    /// </summary>
    internal class DesktopSettings : ISettings
    {
        private readonly ThemeApi theme;
        internal readonly HelperPlugin Plugin;
        internal readonly OverlayDesktopSettings Overlay;
        internal ISettings Guides;   // the overlay's AI guide settings (its own page, shown in the Overlay tab)
        public string NewKey = "";
        public bool RemoveKey;
        public string NewSgdbKey = "";   // Look and feel › Animated art: the SteamGridDB key (ThemeApi.AnimatedArt.cs)
        public bool RemoveSgdbKey;
        public bool ShowFriends;
        private bool showFriendsAtStart;
        // testing
        public bool ProfilesOn, WelcomeOnBoot, WelcomeSkippable;
        public string CommunityRepo = "";
        // Kippix Doctor
        public bool SafeMode, SafeModeOffer;
        // Forgotten your PIN? (r15: ThemeApi.PinReset.cs): taken away when you save
        public bool RemovePin;

        public DesktopSettings(ThemeApi theme, HelperPlugin plugin)
        {
            this.theme = theme;
            Plugin = plugin;
            ShowFriends = theme.DetShowFriends;
            Overlay = new OverlayDesktopSettings(theme, plugin.Overlay);
            try { Guides = plugin.Overlay.GetSettings(false); } catch (Exception) { Guides = null; }
        }

        public bool HasKey { get { return theme.HasSteamKey; } }
        internal ThemeApi Theme { get { return theme; } }

        public void BeginEdit()
        {
            NewKey = "";
            RemoveKey = false;
            NewSgdbKey = "";
            RemoveSgdbKey = false;
            ShowFriends = theme.DetShowFriends;
            showFriendsAtStart = ShowFriends;
            ProfilesOn = theme.ProfilesEnabled;
            WelcomeOnBoot = theme.WelcomeOnBoot;
            WelcomeSkippable = theme.WelcomeSkippable;
            CommunityRepo = theme.CommunityRepoSetting;
            SafeMode = theme.SafeModeOn;
            SafeModeOffer = theme.SafeModeOffer;
            RemovePin = false;
            Overlay.BeginEdit();
            if (Guides != null) Guides.BeginEdit();
        }

        public void CancelEdit()
        {
            if (Guides != null) Guides.CancelEdit();
        }

        public void EndEdit()
        {
            if (!string.IsNullOrWhiteSpace(NewKey)) theme.SetSteamKey(NewKey);
            else if (RemoveKey) theme.SetSteamKey("");
            if (!string.IsNullOrWhiteSpace(NewSgdbKey)) theme.SetSgdbKey(NewSgdbKey);
            else if (RemoveSgdbKey) theme.SetSgdbKey("");
            if (ShowFriends != showFriendsAtStart) theme.DetShowFriends = ShowFriends;   // only when you changed it (a reset meanwhile stays)
            if (ProfilesOn != theme.ProfilesEnabled) theme.ProfilesEnabled = ProfilesOn;
            if (WelcomeOnBoot != theme.WelcomeOnBoot) theme.WelcomeOnBoot = WelcomeOnBoot;
            if (WelcomeSkippable != theme.WelcomeSkippable) theme.WelcomeSkippable = WelcomeSkippable;
            if ((CommunityRepo ?? "").Trim() != theme.CommunityRepoSetting) theme.CommunityRepoSetting = CommunityRepo;
            if (SafeModeOffer != theme.SafeModeOffer) theme.SafeModeOffer = SafeModeOffer;
            if (SafeMode != theme.SafeModeOn) theme.SetSafeMode(SafeMode);
            if (RemovePin) { theme.RemoveProfilesPin(); RemovePin = false; }
            if (!Plugin.OldOverlayInstalled)   // the old add-on is using that file and its own colour numbering: leave it alone
            {
                Overlay.EndEdit();
                if (Guides != null) Guides.EndEdit();   // after ours: both read the overlay's file, change their part and save it
            }
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            string k = (NewKey ?? "").Trim();
            if (k.Length > 0 && !Regex.IsMatch(k, "^[0-9A-Fa-f]{32}$"))
                errors.Add("That doesn't look like a Steam Web API key (it's 32 letters and numbers, from steamcommunity.com/dev/apikey).");
            string g = (NewSgdbKey ?? "").Trim();
            if (g.Length > 0 && !Regex.IsMatch(g, "^[0-9A-Za-z]{16,64}$"))
                errors.Add("That doesn't look like a SteamGridDB API key (letters and numbers, from steamgriddb.com › Preferences › API).");
            List<string> more;
            if (Guides != null && !Plugin.OldOverlayInstalled && !Guides.VerifySettings(out more) && more != null) errors.AddRange(more.Select(m => "Overlay: " + m));
            return errors.Count == 0;
        }
    }

    internal static class DesktopSettingsView
    {
        public static UserControl Create(DesktopSettings s)
        {
            var tabs = new TabControl { Margin = new Thickness(0) };
            tabs.Items.Add(new TabItem { Header = "Kippix", Content = ThemeTab(s) });
            tabs.Items.Add(new TabItem { Header = "In-game overlay", Content = OverlayTab(s) });
            tabs.Items.Add(new TabItem { Header = "Achievement guides", Content = GuidesTab(s) });
            tabs.Items.Add(new TabItem { Header = "Doctor", Content = DoctorTab(s) });
            return new UserControl { Content = tabs };
        }

        // ---------------- the theme ----------------

        private static UIElement ThemeTab(DesktopSettings s)
        {
            var page = SettingsUi.Page("Kippix",
                "Most settings are in the fullscreen theme itself (the options icon in the top bar). This page has the ones that are easier with a keyboard and mouse.");

            // Friends on the game page + the Steam Web API key
            var friends = SettingsUi.Card("Friends on the game page",
                "Friends from Steam, GOG, Epic and EA come from the PlayerActivities add-on (install it from Add-ons and turn on friends in its settings).");
            friends.Add(SettingsUi.CheckRow("Show which friends are playing, or have played, the game", null, s.ShowFriends, v => s.ShowFriends = v));

            var pill = SettingsUi.Pill("");
            Action refresh = () =>
            {
                SettingsUi.SetPill(pill, s.RemoveKey ? "Will be removed when you save" : s.HasKey ? "Saved" : "Not set",
                                   s.RemoveKey ? SettingsUi.Warn : s.HasKey ? SettingsUi.Good : null);
            };
            refresh();
            friends.Add(SettingsUi.Row("Steam Web API key", null, pill));
            TextBox keyBox;
            var secret = SettingsUi.Secret("", v => { s.NewKey = v; if (v.Length > 0 && s.RemoveKey) { s.RemoveKey = false; refresh(); } }, 340, out keyBox,
                                           s.HasKey ? "Paste a new key to replace the saved one" : "Paste your key here");
            var remove = SettingsUi.MakeButton("Remove saved key", () => { s.RemoveKey = true; s.NewKey = ""; keyBox.Text = ""; refresh(); });
            remove.Margin = new Thickness(8, 0, 0, 0);
            var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
            keyRow.Children.Add(secret);
            keyRow.Children.Add(remove);
            var help = SettingsUi.Help("");
            help.Inlines.Add(new Run("Optional: adds who's playing a game right now, and when Steam friends last played it. Get a free key at "));
            help.Inlines.Add(SettingsUi.MakeLink("steamcommunity.com/dev/apikey", "https://steamcommunity.com/dev/apikey"));
            help.Inlines.Add(new Run(" (any domain name works, e.g. localhost). It's kept encrypted for your Windows account on this PC; the saved key is never shown here."));
            friends.Add(SettingsUi.Row("New key", help, keyRow));
            page.Add(friends.Card);

            // (0.81: no SteamGridDB key any more: Animated covers is only a zoom, nothing is fetched)

            // Layouts: reset, with undo
            var layouts = SettingsUi.Card("Layouts",
                "Put the theme back to how it started. Your wishlist, keys and play history are kept. Undo puts back what you had before the last reset (also in the theme: Settings & Customisation › Layouts & reset).");
            var resetStatus = SettingsUi.Help("");
            Func<string, Action, Button> reset = (text, act) => SettingsUi.MakeButton(text, () =>
            {
                try { act(); resetStatus.Text = "Done: " + text.ToLowerInvariant() + "."; }
                catch (OperationCanceledException) { resetStatus.Text = ""; }
                catch (Exception ex) { resetStatus.Text = ex.Message; }
            });
            layouts.Add(SettingsUi.Buttons(
                reset("Reset home screen layout", () => s.Theme.ResetLayout("home")),
                reset("Reset game page layout", () => s.Theme.ResetLayout("game")),
                reset("Reset all layouts", () => s.Theme.ResetLayout("layout")),
                reset("Undo last reset", () => s.Theme.UndoReset())));
            // (1.0.5) a controller button turned off or moved somewhere you can't undo it from (fullscreen: L3 + R3 held 5 s)
            layouts.Add(SettingsUi.Buttons(reset("Reset controller buttons", () => s.Theme.ResetControllerButtons())));
            var everything = reset("Reset everything…", () =>
            {
                if (MessageBox.Show("Reset everything?\n\nThis puts every layout and option back to how Kippix comes. Undo last reset can bring it back.",
                                    "Kippix", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) throw new OperationCanceledException();
                s.Theme.ResetLayout("everything");
            });
            everything.Foreground = SettingsUi.Warn;
            layouts.Add(SettingsUi.Buttons(everything));
            layouts.Add(resetStatus);
            page.Add(layouts.Card);

            // Forgotten your PIN? (r15): only here, in desktop mode, where the fullscreen PIN isn't needed to get in
            var pinCard = SettingsUi.Card("Forgotten your PIN?",
                "The grown-ups' PIN (Settings & Customisation › Profiles in fullscreen) is the one for locked profiles, the Store and Settings locks, and more time on a daily time limit. " +
                "Removing it opens those up. The locks stay set up: set a new PIN in Settings › Profiles and they ask for it again.");
            var pinPill = SettingsUi.Pill("");
            Button removePin = null;
            Action pinRefresh = () =>
            {
                bool has = s.Theme.HasProfilesPin;
                SettingsUi.SetPill(pinPill, s.RemovePin ? "Will be removed when you save" : has ? "A PIN is set" : "No PIN is set",
                                   s.RemovePin ? SettingsUi.Warn : has ? SettingsUi.Good : null);
                if (removePin != null) removePin.IsEnabled = has && !s.RemovePin;
            };
            removePin = SettingsUi.MakeButton("Remove the PIN…", () =>
            {
                if (MessageBox.Show("Remove the grown-ups' PIN?\n\nLocked profiles, the Store and Settings locks, and more time on a time limit won't ask for a PIN until a new one is set in Settings › Profiles. It's removed when you save these settings.",
                                    "Kippix", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                s.RemovePin = true;
                pinRefresh();
            });
            removePin.Foreground = SettingsUi.Warn;
            pinRefresh();
            pinCard.Add(SettingsUi.Row("PIN", null, pinPill));
            pinCard.Add(SettingsUi.Buttons(removePin));
            page.Add(pinCard.Card);

            // Testing
            var testing = SettingsUi.Card("Testing", "Features that are still being tried out. They're off unless you switch them on here.");
            testing.Add(SettingsUi.CheckRow("Profiles and the \"Who's playing?\" screen", "Settings › Profiles in fullscreen. Each person gets their own layout, favourites, wishlist and Steam account; Kids hides games above an age rating; Guest keeps nothing. " +
                                            "Launching a Steam game signs Steam into that profile's account first (Steam restarts, 10–30 seconds). Only the AutoLoginUser registry value is changed for that, no passwords.",
                                            s.ProfilesOn, v => s.ProfilesOn = v));
            testing.Add(SettingsUi.CheckRow("Show Getting started every time Playnite starts", null, s.WelcomeOnBoot, v => s.WelcomeOnBoot = v));
            testing.Add(SettingsUi.Row("Community gallery from", SettingsUi.Help("Layouts, look packs and widgets in Settings › Community. owner/repo on GitHub. Leave empty for Kippix's own repository; the gallery reads community/index.json from its main branch."),
                                       SettingsUi.TextField(s.CommunityRepo, v => s.CommunityRepo = v, 340, "owner/repo")));
            page.Add(testing.Card);

            return page.Scroller;
        }

        // ---------------- the in-game overlay ----------------

        private static UIElement OverlayTab(DesktopSettings s)
        {
            var o = s.Overlay;
            var page = SettingsUi.Page("In-game overlay",
                "Press the Guide button in a game (started from Playnite) and a panel slides in: achievements and guides, Discord, audio, screenshots, stats and more.");
            if (s.Plugin.OldOverlayInstalled) page.Add(OldOverlayNotice());

            var parts = new List<UIElement>();
            var general = SettingsUi.Card("General", null);
            general.Add(SettingsUi.CheckRow("Use the in-game overlay", "Takes effect when Playnite restarts.", o.Enabled, v => o.Enabled = v));
            general.Add(SettingsUi.CheckRow("Only in fullscreen mode", null, o.OnlyInFullscreen, v => o.OnlyInFullscreen = v));
            general.Add(SettingsUi.CheckRow("Ask before closing a game", null, o.ConfirmClose, v => o.ConfirmClose = v));
            general.Add(SettingsUi.CheckRow("Vibrate the controller when the overlay opens", null, o.OpenVibration, v => o.OpenVibration = v));
            general.Add(SettingsUi.CheckRow("Hold the Guide button in a game for Power off / Hibernate", "Hold it for a second and a half: a small menu comes up; either choice closes the game first.", o.HoldGuidePower, v => o.HoldGuidePower = v));
            general.Add(SettingsUi.CheckRow("Play a sound when a screenshot is saved", null, o.ScreenshotSound, v => o.ScreenshotSound = v));
            general.Add(SettingsUi.Row("When the overlay opens",
                SettingsUi.Help("Pause the game: the overlay takes the focus, so a game that pauses when you switch away pauses while it's open. " +
                                "Online games with anti-cheat may not like this: choose Let it keep running for those (the game keeps the focus and " +
                                "nothing is done to it; it may also see your button presses, and it stops some full-screen games flickering). " +
                                "Automatic lets the games Playnite lists as online or MMO keep running. The overlay never reads a game."),
                SettingsUi.Combo(KippixOverlay.OverlayConfig.WhenOpenedNames, o.WhenOpened, v => o.WhenOpened = v)));
            general.Add(SettingsUi.Row("Freeze the game while it's open",
                SettingsUi.Help("Emulators keep reading the controller even when the overlay is in front, so moving around the overlay also plays the game. " +
                                "This pauses the game's program while the overlay is open and carries on the moment it closes. Emulators only (the default), " +
                                "or every game. Never a game the overlay lets keep running (online games)."),
                SettingsUi.Combo(KippixOverlay.GameFreeze.ModeNames, o.FreezeGame, v => o.FreezeGame = v)));
            general.Add(SettingsUi.CheckRow("Show HowLongToBeat times", null, o.ShowHltb, v => o.ShowHltb = v));
            general.Add(SettingsUi.CheckRow("Show story progress", "On the Now playing tile, under HowLongToBeat: how far through the game you are, from its story achievements or play time.", o.ShowProgress, v => o.ShowProgress = v));
            parts.Add(general.Card);

            // (0.99.37) Kippix's own achievement pop-ups (PS5 style), instead of Playnite Achievements' own
            var achs = SettingsUi.Card("Achievement pop-ups", "When you unlock an achievement while playing, Kippix shows it in its own card, in one of seven designs, with Playnite Achievements' rarity badge. Needs Playnite Achievements (it finds the unlocks).");
            achs.Add(SettingsUi.CheckRow("Show Kippix's own achievement pop-ups", null, o.AchToasts, v => o.AchToasts = v));
            achs.Add(SettingsUi.CheckRow("Hide Playnite Achievements' own pop-ups", "Only while Kippix's are on, and only in memory: its own settings aren't changed.", o.AchHideOther, v => o.AchHideOther = v));
            achs.Add(SettingsUi.Row("Design", SettingsUi.Help("Pick one and press Show a test pop-up, or see them all with Show all 7 designs."),
                SettingsUi.Combo(KippixOverlay.AchToastWindow.StyleNames, o.AchToastStyle, v => o.AchToastStyle = v)));
            achs.Add(SettingsUi.Row("Where they show", null, SettingsUi.Combo(KippixOverlay.AchToastWindow.PositionNames, o.AchToastPosition, v => o.AchToastPosition = v)));
            achs.Add(SettingsUi.Row("How long each stays", null, SettingsUi.Combo(new[] { "3 s", "4 s", "5 s", "6 s", "7 s", "8 s", "10 s", "12 s" },
                Array.IndexOf(new[] { 3, 4, 5, 6, 7, 8, 10, 12 }, o.AchToastSeconds) < 0 ? 3 : Array.IndexOf(new[] { 3, 4, 5, 6, 7, 8, 10, 12 }, o.AchToastSeconds),
                v => o.AchToastSeconds = new[] { 3, 4, 5, 6, 7, 8, 10, 12 }[Math.Max(0, Math.Min(7, v))])));
            achs.Add(SettingsUi.CheckRow("Progress updates too", "Pop-ups for achievements that count up (5 of 10...), not only unlocks.", o.AchToastProgress, v => o.AchToastProgress = v));
            achs.Add(AchSoundUi.Build(o));
            var prev = new StackPanel { Orientation = Orientation.Horizontal };
            prev.Children.Add(SettingsUi.MakeButton("Show a test pop-up", () => { o.SaveAchNow(); KippixOverlay.AchievementToasts.ShowTest(); }));
            var all = SettingsUi.MakeButton("Show all 7 designs", () => { o.SaveAchNow(); KippixOverlay.AchievementToasts.ShowAllDesigns(); });
            all.Margin = new Thickness(8, 0, 0, 0);
            prev.Children.Add(all);
            achs.Add(SettingsUi.Row("Preview", SettingsUi.Help("They show on the screen Playnite is on, over everything."), prev));
            parts.Add(achs.Card);

            var look = SettingsUi.Card("Look and buttons", null);
            look.Add(SettingsUi.Row("Colours", SettingsUi.Help("Kippix matches the fullscreen theme's menus."),
                                    SettingsUi.Combo(KippixOverlay.OverlayTheme.All.Select(t => t.Name).ToArray(), o.Theme, v => o.Theme = v)));
            look.Add(SettingsUi.Row("Open it with", null, SettingsUi.Combo(KippixOverlay.OverlayConfig.TriggerNames, o.Trigger, v => o.Trigger = v)));
            parts.Add(look.Card);

            var shots = SettingsUi.Card("Screenshots and clips", null);
            shots.Add(SettingsUi.Row("Screenshots with", null, SettingsUi.Combo(KippixOverlay.OverlayConfig.ScreenshotNames, o.Screenshot, v => o.Screenshot = v)));
            shots.Add(SettingsUi.Row("Save recent gameplay with", null, SettingsUi.Combo(KippixOverlay.OverlayConfig.ReplayNames, o.Replay, v => o.Replay = v)));
            parts.Add(shots.Card);

            var discord = SettingsUi.Card("Discord",
                "Chats and voice in the overlay need a free Discord app of your own: discord.com/developers/applications → New Application, OAuth2 → Redirects → add http://localhost, " +
                "then paste its Client ID and Client Secret here. Takes effect when Playnite restarts.");
            discord.Add(SettingsUi.CheckRow("Show new Discord messages while playing", null, o.ShowDiscordMessages, v => o.ShowDiscordMessages = v));
            TextBox unused;
            discord.Add(SettingsUi.Row("Client ID", null, SettingsUi.Secret(o.DiscordClientId, v => o.DiscordClientId = v, 340, out unused, "Not set")));
            discord.Add(SettingsUi.Row("Client Secret", null, SettingsUi.Secret(o.DiscordClientSecret, v => o.DiscordClientSecret = v, 340, out unused, "Not set")));
            parts.Add(discord.Card);

            foreach (var p in parts)
            {
                if (s.Plugin.OldOverlayInstalled) p.IsEnabled = false;   // read-only until the old add-on is gone
                page.Add(p);
            }
            return page.Scroller;
        }

        // ---------------- achievement guides (the overlay's AI guide settings, their own view) ----------------

        private static UIElement GuidesTab(DesktopSettings s)
        {
            UIElement guides = null;
            try { guides = s.Plugin.Overlay.GetSettingsView(false); } catch (Exception) { }
            if (guides == null)
            {
                var page = SettingsUi.Page("Achievement guides", "The guide settings couldn't be loaded. Restart Playnite and try again.");
                return page.Scroller;
            }
            if (!s.Plugin.OldOverlayInstalled) return guides;
            guides.IsEnabled = false;   // read-only until the old add-on is gone
            var dock = new DockPanel();
            var notice = OldOverlayNotice();
            notice.Margin = new Thickness(24, 20, 24, 0);
            DockPanel.SetDock(notice, Dock.Top);
            dock.Children.Add(notice);
            dock.Children.Add(guides);
            return dock;
        }

        // ---------------- Kippix Doctor: safe mode, health checks, a bug report ----------------

        private static UIElement DoctorTab(DesktopSettings s)
        {
            var page = SettingsUi.Page("Kippix Doctor",
                "For when something isn't right: switch Kippix's extras off, see what might be wrong, or make a bug report to send. Also in fullscreen: Settings & Customisation › Help › Kippix Doctor.");

            var safe = SettingsUi.Card("Safe mode",
                "Fullscreen starts with Kippix's extras off: widgets, the carousel and folders, live backgrounds, the in-game overlay and animations. Turning it off puts back how you had them. Takes effect when you save (the overlay at the next start).");
            safe.Add(SettingsUi.CheckRow("Safe mode", null, s.SafeMode, v => s.SafeMode = v));
            safe.Add(SettingsUi.CheckRow("Offer safe mode after failed starts", "When fullscreen didn't start properly twice in a row, Kippix asks at the next start (and turns it on by itself if starts keep failing).",
                                         s.SafeModeOffer, v => s.SafeModeOffer = v));
            page.Add(safe.Card);

            var checks = SettingsUi.Card("Health checks", "Old or clashing add-ons are only reported here, never removed.");
            var list = new StackPanel();
            var again = SettingsUi.MakeButton("Check again", () => { });
            Action run = null;
            run = () =>
            {
                list.Children.Clear();
                list.Children.Add(SettingsUi.Help("Checking…"));
                again.IsEnabled = false;
                var job = s.Theme.DoctorCheckJob();
                var disp = list.Dispatcher;
                KippixShared.SafeWork.Queue(_ =>
                {
                    List<DoctorCheck> res = null;
                    string err = null;
                    try { res = job(); } catch (Exception ex) { err = ex.Message; }
                    disp.BeginInvoke(new Action(() =>
                    {
                        again.IsEnabled = true;
                        list.Children.Clear();
                        if (res == null) { list.Children.Add(SettingsUi.Help("The checks couldn't run: " + err)); return; }
                        foreach (var c in res) list.Children.Add(DoctorLine(c));
                    }));
                });
            };
            again.Click += (a, b) => run();
            checks.Add(list);
            checks.Add(SettingsUi.Buttons(again));
            page.Add(checks.Card);
            run();

            var report = SettingsUi.Card("Bug report",
                "Zips Playnite's two logs (their last 2 MB), Kippix's settings with every key, token, password and personal thing taken out, and the versions into Documents › Kippix Logs, then opens the folder.");
            var status = SettingsUi.Help("");
            var make = SettingsUi.MakeButton("Make a bug report", () => s.Theme.MakeBugReport(t => status.Text = t));
            report.Add(SettingsUi.Buttons(make));
            report.Add(status);
            page.Add(report.Card);
            return page.Scroller;
        }

        private static UIElement DoctorLine(DoctorCheck c)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string word = c.Level == "ok" ? "OK" : c.Level == "info" ? "Note" : c.Level == "warn" ? "Check" : "Problem";
            var pill = SettingsUi.Pill(word);
            SettingsUi.SetPill(pill, word, c.Level == "ok" ? SettingsUi.Good : c.Level == "info" ? null : SettingsUi.Warn);
            pill.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(pill);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = c.Title ?? "", TextWrapping = TextWrapping.Wrap, FontWeight = c.Level == "ok" || c.Level == "info" ? FontWeights.Normal : FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(c.Detail)) text.Children.Add(SettingsUi.Help(c.Detail));
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            return g;
        }

        private static FrameworkElement OldOverlayNotice()
        {
            return SettingsUi.Notice("The separate KippixOverlay add-on is still installed, so the built-in overlay is off and these settings are read-only. " +
                                     "Uninstall KippixOverlay (Add-ons → Extensions) and restart Playnite.");
        }
    }

    /// <summary>
    /// Small building blocks for the code-built desktop settings pages (no XAML in the add-on): a page with a title, cards with a
    /// heading and help, label / control rows lined up in two columns, and secret fields that stay blurred until the eye is pressed.
    /// Colours come from Playnite's desktop theme (TextBrush, GlyphBrush) where it has them, so light and dark themes both work.
    /// </summary>
    internal static class SettingsUi
    {
        public const double LabelWidth = 210;

        public static readonly Brush Good = Frozen(Color.FromRgb(0x3C, 0xB3, 0x71));
        public static readonly Brush Warn = Frozen(Color.FromRgb(0xE8, 0x8A, 0x2E));

        // Material Design "eye" / "eye-off" shapes (24 x 24)
        private const string EyeData = "M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9Z";
        private const string EyeOffData = "M11.83,9L15,12.16C15,12.11 15,12.05 15,12A3,3 0 0,0 12,9C11.94,9 11.89,9 11.83,9M7.53,9.8L9.08,11.35C9.03,11.56 9,11.77 9,12A3,3 0 0,0 12,15C12.22,15 12.44,14.97 12.65,14.92L14.2,16.47C13.53,16.8 12.79,17 12,17A5,5 0 0,1 7,12C7,11.21 7.2,10.47 7.53,9.8M2,4.27L4.28,6.55L4.73,7C3.08,8.3 1.78,10 1,12C2.73,16.39 7,19.5 12,19.5C13.55,19.5 15.03,19.2 16.38,18.66L16.81,19.08L19.73,22L21,20.73L3.27,3M12,7A5,5 0 0,1 17,12C17,12.64 16.87,13.26 16.64,13.82L19.57,16.75C21.07,15.5 22.27,13.86 23,12C21.27,7.61 17,4.5 12,4.5C10.6,4.5 9.26,4.75 8,5.2L10.17,7.35C10.74,7.13 11.35,7 12,7Z";

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static object Res(string key)
        {
            try { return Application.Current != null ? Application.Current.TryFindResource(key) : null; } catch (Exception) { return null; }
        }

        /// <summary>The desktop theme's text colour (for tints that suit light and dark themes alike).</summary>
        private static Color TextColor()
        {
            var b = Res("TextBrush") as SolidColorBrush;
            return b != null ? b.Color : SystemColors.ControlTextColor;
        }

        private static Brush Tint(byte alpha)
        {
            var c = TextColor();
            return Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        /// <summary>A theme brush by key (kept live if the theme has it), else <paramref name="fallback"/>.</summary>
        private static void ThemeBrush(FrameworkElement e, DependencyProperty p, string key, Brush fallback)
        {
            if (Res(key) is Brush) e.SetResourceReference(p, key);
            else if (fallback != null) e.SetValue(p, fallback);
        }

        // ---------------- page / card ----------------

        internal class PageParts
        {
            public StackPanel Body;
            public ScrollViewer Scroller;
            public void Add(UIElement e) { Body.Children.Add(e); }
        }

        public static PageParts Page(string title, string intro)
        {
            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            if (!string.IsNullOrEmpty(intro))
                body.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 18) });
            return new PageParts { Body = body, Scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        }

        internal class CardParts
        {
            public Border Card;
            public StackPanel Body;
            public void Add(UIElement e) { Body.Children.Add(e); }
        }

        /// <summary>A section: a rounded card with a heading, a line of help and its rows.</summary>
        public static CardParts Card(string title, string help)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(help) ? 12 : 4) });
            if (!string.IsNullOrEmpty(help))
                body.Children.Add(new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 14) });
            var card = new Border
            {
                Child = body, CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 16, 18, 8), Margin = new Thickness(0, 0, 0, 14),
                Background = Tint(0x0C), BorderBrush = Tint(0x22), BorderThickness = new Thickness(1)
            };
            return new CardParts { Card = card, Body = body };
        }

        public static Border Notice(string text)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 14),
                BorderThickness = new Thickness(1), BorderBrush = Warn, Background = Frozen(Color.FromArgb(0x22, 0xE8, 0x8A, 0x2E)),
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            };
        }

        // ---------------- rows ----------------

        public static TextBlock Help(string text)
        {
            return new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        }

        /// <summary>Label on the left, control on the right, its help under the control.</summary>
        public static UIElement Row(string label, TextBlock help, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
            g.Children.Add(l);
            var fe = control as FrameworkElement;
            if (fe != null && fe.HorizontalAlignment == HorizontalAlignment.Stretch) fe.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            if (help != null)
            {
                Grid.SetColumn(help, 1);
                Grid.SetRow(help, 1);
                g.Children.Add(help);
            }
            return g;
        }

        /// <summary>A switch (check box) across the card, its help under it.</summary>
        public static UIElement CheckRow(string text, string help, bool value, Action<bool> set)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var c = new CheckBox { IsChecked = value, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, VerticalContentAlignment = VerticalAlignment.Center };
            c.Checked += (a, b) => set(true);
            c.Unchecked += (a, b) => set(false);
            sp.Children.Add(c);
            if (!string.IsNullOrEmpty(help))
            {
                var h = Help(help);
                h.Margin = new Thickness(24, 2, 0, 0);
                sp.Children.Add(h);
            }
            return sp;
        }

        public static ComboBox Combo(string[] items, int value, Action<int> set)
        {
            var box = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var i in items) box.Items.Add(i);
            box.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, value));
            box.SelectionChanged += (a, b) => set(box.SelectedIndex);
            return box;
        }

        public static FrameworkElement TextField(string value, Action<string> set, double width, string placeholder)
        {
            var box = new TextBox { Text = value ?? "", Width = width, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 3, 4, 3) };
            box.TextChanged += (a, b) => set(box.Text);
            return WithPlaceholder(box, placeholder);
        }

        /// <summary>A grey hint over an empty box (it doesn't take clicks).</summary>
        private static FrameworkElement WithPlaceholder(TextBox box, string placeholder)
        {
            if (string.IsNullOrEmpty(placeholder)) return box;
            var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            var hint = new TextBlock { Text = placeholder, Opacity = 0.45, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Action update = () => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (a, b) => update();
            update();
            g.Children.Add(box);
            g.Children.Add(hint);
            return g;
        }

        /// <summary>
        /// A key / secret: an ordinary text box whose text is blurred (and can't be copied out) until the eye button is pressed.
        /// Still editable while blurred (paste a new key straight in). The value is only handed to <paramref name="set"/>, never logged.
        /// </summary>
        public static FrameworkElement Secret(string value, Action<string> set, double width, out TextBox textBox, string placeholder)
        {
            var box = new TextBox { Text = value ?? "", Width = width, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 3, 4, 3) };
            box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Secret value");
            textBox = box;
            bool hidden = true;
            var blur = new BlurEffect { Radius = 9, KernelType = KernelType.Gaussian };
            UIElement blurTarget = box;
            Action applyBlur = () =>
            {
                // only the text (the template's content host) is blurred, so the box's border stays sharp
                box.ApplyTemplate();
                var host = box.Template != null ? box.Template.FindName("PART_ContentHost", box) as UIElement : null;
                var target = host ?? (UIElement)box;
                if (target != blurTarget) blurTarget.Effect = null;
                blurTarget = target;
                blurTarget.Effect = hidden ? blur : null;
            };
            box.Loaded += (a, b) => applyBlur();
            box.Effect = blur;   // until it's loaded (and has its template)
            // while hidden: no copying / cutting it out, no dragging the text away
            CommandManager.AddPreviewCanExecuteHandler(box, (a, e) =>
            {
                if (hidden && (e.Command == ApplicationCommands.Copy || e.Command == ApplicationCommands.Cut)) { e.CanExecute = false; e.Handled = true; }
            });
            DataObject.AddCopyingHandler(box, (a, e) => { if (hidden) e.CancelCommand(); });
            box.TextChanged += (a, b) => set(box.Text);

            var icon = new Path { Data = Geometry.Parse(EyeData), Stretch = Stretch.Uniform, Width = 18, Height = 18 };
            if (Res("GlyphBrush") is Brush) icon.SetResourceReference(Shape.FillProperty, "GlyphBrush");
            else ThemeBrush(icon, Shape.FillProperty, "TextBrush", Frozen(SystemColors.ControlTextColor));
            var eye = new Button { Content = icon, Width = 34, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(4), ToolTip = "Show", VerticalAlignment = VerticalAlignment.Stretch };
            eye.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Show or hide");
            eye.Click += (a, b) =>
            {
                hidden = !hidden;
                icon.Data = Geometry.Parse(hidden ? EyeData : EyeOffData);
                eye.ToolTip = hidden ? "Show" : "Hide";
                if (box.IsLoaded) applyBlur(); else box.Effect = hidden ? blur : null;
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(WithPlaceholder(box, placeholder));
            row.Children.Add(eye);
            return row;
        }

        // ---------------- buttons, links, status ----------------

        public static Button MakeButton(string text, Action click)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 5, 14, 5), MinWidth = 90 };
            b.Click += (a, e) =>
            {
                try { click(); }
                catch (Exception ex)
                {
                    LogManager.GetLogger().Error(ex, "Kippix settings: " + text);
                    try { MessageBox.Show(ex.Message, "Kippix"); } catch (Exception) { }
                }
            };
            return b;
        }

        public static WrapPanel Buttons(params Button[] buttons)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var b in buttons)
            {
                b.Margin = new Thickness(0, 0, 8, 8);
                w.Children.Add(b);
            }
            return w;
        }

        public static Hyperlink MakeLink(string text, string url)
        {
            var link = new Hyperlink(new Run(text)) { NavigateUri = new Uri(url) };
            link.RequestNavigate += (o, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception) { } e.Handled = true; };
            return link;
        }

        public static TextBlock LinkLine(string text, string url)
        {
            var tb = new TextBlock { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            tb.Inlines.Add(MakeLink(text, url));
            return tb;
        }

        /// <summary>A small rounded status label ("Saved", "Not set").</summary>
        public static Border Pill(string text)
        {
            var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1),
                                 Child = new TextBlock { Text = text, FontSize = 12 } };
            SetPill(b, text, null);
            return b;
        }

        public static void SetPill(Border pill, string text, Brush colour)
        {
            ((TextBlock)pill.Child).Text = text;
            if (colour != null)
            {
                var c = ((SolidColorBrush)colour).Color;
                pill.BorderBrush = colour;
                pill.Background = Frozen(Color.FromArgb(0x26, c.R, c.G, c.B));
            }
            else
            {
                pill.BorderBrush = Tint(0x40);
                pill.Background = Tint(0x10);
            }
        }
    }

    internal static class AchSoundUi
    {
        /// <summary>
        /// (0.99.38) Your own achievement unlock sound: Choose... copies a wav / mp3 into the overlay's data folder (so it keeps
        /// working if the original moves), Play tries it, Clear goes back to Playnite Achievements' own sound; and its volume.
        /// </summary>
        public static UIElement Build(OverlayDesktopSettings o)
        {
            var panel = new StackPanel();
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 220, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            var volRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            Button play = null, clear = null;
            Action update = () =>
            {
                bool has = !string.IsNullOrWhiteSpace(o.AchSoundFile) && System.IO.File.Exists(o.AchSoundFile);
                name.Text = has ? (string.IsNullOrEmpty(o.AchSoundName) ? System.IO.Path.GetFileName(o.AchSoundFile) : o.AchSoundName) : "Playnite Achievements' own sound";
                name.Opacity = has ? 1 : 0.65;
                if (play != null) play.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                if (clear != null) clear.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                volRow.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            };
            var choose = SettingsUi.MakeButton("Choose...", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Achievement unlock sound", Filter = "Sounds (*.wav;*.mp3;*.wma;*.m4a;*.aac)|*.wav;*.mp3;*.wma;*.m4a;*.aac|All files (*.*)|*.*", CheckFileExists = true };
                if (dlg.ShowDialog() != true) return;
                string dir = o.DataFolder;
                System.IO.Directory.CreateDirectory(dir);
                string dest = System.IO.Path.Combine(dir, "achievement-sound-" + DateTime.Now.Ticks + System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant());
                System.IO.File.Copy(dlg.FileName, dest, true);
                // the earlier copies (not the one still saved, until these settings are saved)
                foreach (var f in System.IO.Directory.GetFiles(dir, "achievement-sound-*"))
                    if (!string.Equals(f, dest, StringComparison.OrdinalIgnoreCase) && !string.Equals(f, o.SavedSoundFile, StringComparison.OrdinalIgnoreCase)) { try { System.IO.File.Delete(f); } catch (Exception) { } }
                o.AchSoundFile = dest;
                o.AchSoundName = System.IO.Path.GetFileName(dlg.FileName);
                update();
                KippixOverlay.AchievementToasts.PlaySound(dest, o.AchSoundVolume);
            });
            play = SettingsUi.MakeButton("Play", () => KippixOverlay.AchievementToasts.PlaySound(o.AchSoundFile, o.AchSoundVolume));
            clear = SettingsUi.MakeButton("Clear", () => { o.AchSoundFile = ""; o.AchSoundName = ""; update(); });
            play.Margin = new Thickness(8, 0, 0, 0); clear.Margin = new Thickness(8, 0, 0, 0);
            line.Children.Add(name);
            line.Children.Add(choose);
            line.Children.Add(play);
            line.Children.Add(clear);
            panel.Children.Add(line);

            int[] vols = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            int vi = Array.IndexOf(vols, (o.AchSoundVolume + 5) / 10 * 10);
            var vol = SettingsUi.Combo(vols.Select(v => v + "%").ToArray(), vi < 0 ? 6 : vi, v => { o.AchSoundVolume = vols[Math.Max(0, Math.Min(9, v))]; });
            vol.Width = 120;
            volRow.Children.Add(new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            volRow.Children.Add(vol);
            panel.Children.Add(volRow);
            update();
            return SettingsUi.Row("Unlock sound", SettingsUi.Help("A wav or mp3 of your own, played with each pop-up (Playnite Achievements' own sound is off meanwhile)."), panel);
        }
    }

    /// <summary>The overlay's general settings (its config file), edited from the Overlay tab.</summary>
    internal class OverlayDesktopSettings
    {
        private readonly ThemeApi theme;
        private readonly KippixOverlay.OverlayPlugin overlay;
        public bool Enabled, OnlyInFullscreen, ConfirmClose, ShowHltb, ShowProgress = true, ShowDiscordMessages, OpenVibration = true, HoldGuidePower = true, ScreenshotSound = true, OpenSound = true, AchToasts = true, AchHideOther = true, AchToastProgress;
        public int AchToastPosition, AchToastSeconds = 6, AchToastStyle, AchSoundVolume = 70;
        public string AchSoundFile = "", AchSoundName = "", SavedSoundFile = "";
        public string DataFolder { get { return System.IO.Path.GetDirectoryName(overlay.ConfigPath); } }
        public int Theme, Trigger, Screenshot, Replay, WhenOpened, FreezeGame;
        public string DiscordClientId, DiscordClientSecret;

        public OverlayDesktopSettings(ThemeApi theme, KippixOverlay.OverlayPlugin overlay)
        {
            this.theme = theme;
            this.overlay = overlay;
            BeginEdit();
        }

        public void BeginEdit()
        {
            Enabled = theme.OverlayEnabled;
            try
            {
                var c = overlay.ReloadConfig();
                Theme = c.Theme; Trigger = c.TriggerMode; Screenshot = c.ScreenshotMethod; Replay = c.ReplayMethod;
                OnlyInFullscreen = c.OnlyInFullscreen; ConfirmClose = c.ConfirmClose; WhenOpened = c.WhenOpened; FreezeGame = c.FreezeGame; OpenVibration = c.OpenVibration; HoldGuidePower = c.HoldGuidePower; ScreenshotSound = c.ScreenshotSound; OpenSound = c.OpenSound; AchToasts = c.AchToasts; AchHideOther = c.AchHideOther; AchToastPosition = c.AchToastPosition; AchToastSeconds = c.AchToastSeconds; AchToastProgress = c.AchToastProgress;
                AchToastStyle = c.AchToastStyle; AchSoundFile = c.AchSoundFile ?? ""; SavedSoundFile = AchSoundFile; AchSoundVolume = c.AchSoundVolume; AchSoundName = c.AchSoundName ?? "";
                ShowHltb = c.ShowHowLongToBeat; ShowProgress = c.ShowStoryProgress; ShowDiscordMessages = c.ShowDiscordMessages;
                DiscordClientId = c.DiscordClientId; DiscordClientSecret = c.DiscordClientSecret;
            }
            catch (Exception) { }
        }

        public void EndEdit()
        {
            theme.OverlayEnabled = Enabled;
            try
            {
                var c = overlay.ReloadConfig();
                c.Theme = Theme; c.TriggerMode = Trigger; c.ScreenshotMethod = Screenshot; c.ReplayMethod = Replay;
                c.OnlyInFullscreen = OnlyInFullscreen; c.ConfirmClose = ConfirmClose; c.WhenOpened = WhenOpened; c.FreezeGame = FreezeGame; c.OpenVibration = OpenVibration; c.HoldGuidePower = HoldGuidePower; c.ScreenshotSound = ScreenshotSound; c.OpenSound = OpenSound; c.AchToasts = AchToasts; c.AchHideOther = AchHideOther; c.AchToastPosition = AchToastPosition; c.AchToastSeconds = AchToastSeconds; c.AchToastProgress = AchToastProgress;
                c.AchToastStyle = AchToastStyle; c.AchSoundFile = AchSoundFile ?? ""; c.AchSoundVolume = AchSoundVolume; c.AchSoundName = AchSoundName ?? ""; SavedSoundFile = c.AchSoundFile;
                c.ShowHowLongToBeat = ShowHltb; c.ShowStoryProgress = ShowProgress; c.ShowDiscordMessages = ShowDiscordMessages;
                c.DiscordClientId = (DiscordClientId ?? "").Trim(); c.DiscordClientSecret = (DiscordClientSecret ?? "").Trim();
                overlay.SaveConfig();
                KippixOverlay.AchievementToasts.Apply();   // (0.99.37) Playnite Achievements' own pop-ups off / back
            }
            catch (Exception) { }
        }

        /// <summary>(0.99.37) The test pop-up: the achievement pop-up choices on this page are saved first, so it shows as chosen.</summary>
        public void SaveAchNow()
        {
            try
            {
                var c = overlay.ReloadConfig();
                c.AchToasts = AchToasts; c.AchHideOther = AchHideOther; c.AchToastPosition = AchToastPosition; c.AchToastSeconds = AchToastSeconds; c.AchToastProgress = AchToastProgress;
                c.AchToastStyle = AchToastStyle; c.AchSoundFile = AchSoundFile ?? ""; c.AchSoundVolume = AchSoundVolume; c.AchSoundName = AchSoundName ?? ""; SavedSoundFile = c.AchSoundFile;
                overlay.SaveConfig();
                KippixOverlay.AchievementToasts.Apply();
            }
            catch (Exception) { }
        }
    }
}
