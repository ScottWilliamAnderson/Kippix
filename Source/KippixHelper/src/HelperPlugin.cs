using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// KippixHelper: the live parts of the Kippix fullscreen theme.
    /// The theme binds to it with {PluginSettings Plugin=Kippix, Path=...}.
    /// </summary>
    public partial class HelperPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly Guid PluginId = new Guid("3f6c2d1e-8a47-4b9e-b1d5-7e2a9c4f0b18");

        public override Guid Id { get { return PluginId; } }

        /// <summary>Root object for theme bindings ("Theme" settings root).</summary>
        public ThemeApi Theme { get; private set; }

        /// <summary>The in-game overlay (used to be the KippixOverlay add-on). Only started when it's on and the old add-on isn't installed.</summary>
        internal KippixOverlay.OverlayPlugin Overlay { get; private set; }
        internal bool OverlayRunning { get { return Overlay != null && Overlay.Started; } }
        /// <summary>The old, separate KippixOverlay add-on is still installed (then ours stays off, so there aren't two).</summary>
        internal bool OldOverlayInstalled { get; private set; }

        public HelperPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };   // the desktop settings page (Steam key etc.)
            Theme = new ThemeApi(this, api);
            Overlay = new KippixOverlay.OverlayPlugin(api);
            KippixOverlay.OverlayPlugin.MoreAchievements = id => Theme.AchievementsForOverlay(id);
            KippixOverlay.OverlayPlugin.CaptureSaved = (file, game) => Theme.RecordCapture(file, game);
            AddSettingsSupport(new AddSettingsSupportArgs { SourceName = "Kippix", SettingsRoot = "Theme" });
        }

        private DesktopSettings desktopSettings;

        public override ISettings GetSettings(bool firstRunSettings)
        {
            desktopSettings = new DesktopSettings(Theme, this);
            return desktopSettings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return DesktopSettingsView.Create(desktopSettings ?? (desktopSettings = new DesktopSettings(Theme, this)));
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                try { Theme.DoctorStarting(); } catch (Exception ex) { logger.Warn("Kippix: start marker: " + ex.Message); }   // (Kippix Doctor: starts that never finished)
                try
                {
                    OldOverlayInstalled = PlayniteApi.Addons.Plugins.Any(p => p.Id == KippixOverlay.OverlayPlugin.PluginId);
                    if (OldOverlayInstalled)
                    {
                        logger.Warn("Kippix: the separate KippixOverlay add-on is still installed, so the built-in overlay stays off.");
                        PlayniteApi.Notifications.Add(new NotificationMessage("Kippix-old-overlay",
                            "The overlay is now part of Kippix Helper. Uninstall the separate KippixOverlay add-on (Add-ons \u2192 Extensions) and restart Playnite.",
                            NotificationType.Info));
                    }
                    else if (Theme.OverlayEnabled && !Theme.SafeModeOn) Overlay.OnApplicationStarted(args);
                    else if (Theme.SafeModeOn) logger.Info("Kippix: safe mode: the in-game overlay stays off");
                }
                catch (Exception ex) { logger.Error(ex, "Kippix: the overlay failed to start"); }
                // (0.99.37) Kippix's own achievement pop-ups (whether or not the overlay itself is on)
                try { if (!Theme.SafeModeOn) KippixOverlay.AchievementToasts.Start(PlayniteApi, () => Overlay.Config ?? Overlay.ReloadConfig(), () => Overlay.GameWindow); }
                catch (Exception ex) { logger.Warn("Kippix: achievement pop-ups: " + ex.Message); }
                try { ThemeApi.EnsureLayoutsFolder(); } catch (Exception) { }   // Documents › Kippix Layouts → Kippix Layouts (copied once)
                Theme.Start();
                try { Theme.LudusaviOwnAfterPlayOff("start-up"); } catch (Exception) { }
                try { Theme.ApplyPendingProfile(); } catch (Exception) { }   // (0.88: a one-off name / picture change left for it)   // (0.86: one backup after playing, no question)
                logger.Info("Kippix Helper started (" + PlayniteApi.ApplicationInfo.Mode + ").");
            }
            catch (Exception ex) { logger.Error(ex, "Kippix Helper failed to start"); }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            try { if (hoGame != null) HandoverFinish(); } catch (Exception) { }
            try { Theme.Stop(); } catch (Exception) { }
            try { Theme.DoctorStopped(); } catch (Exception) { }   // closed properly: this start counts as finished
            try { Overlay.OnApplicationStopped(args); } catch (Exception) { }
        }

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            try
            {
                if (args != null && args.NewValue != null && args.NewValue.Count > 0) Theme.GameSelected(args.NewValue[0]);
            }
            catch (Exception) { }
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            try { if (hoGame != null) HandoverFinish(); } catch (Exception) { }   // (0.99.54) another game: the held-back one ends first
            try { if (args != null && Theme.TimeLimitBlocksStart(args.Game)) { args.CancelStartup = true; return; } } catch (Exception) { }   // Profiles: no time left today
            try { if (args != null && Theme.SteamSwitchFirst(args.Game)) { args.CancelStartup = true; return; } } catch (Exception) { }   // Steam signs into the profile's account first
            try { if (args != null) Theme.GameStarting(args.Game); } catch (Exception) { }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            try { Theme.RunningStarted(args); } catch (Exception ex) { logger.Error(ex, "Kippix: running list (game started)"); }   // (issue #11) what's running, one entry per game
            try { Theme.GameRunningChanged(true); } catch (Exception ex) { logger.Error(ex, "Kippix: game started"); }
            try { Overlay.OnGameStarted(args); } catch (Exception ex) { logger.Error(ex, "Kippix: overlay (game started)"); }
            try { KippixOverlay.AchievementToasts.Apply(); } catch (Exception) { }   // (0.99.37) Playnite Achievements' own pop-ups stay off while ours are on
            try { if (args != null) Theme.TimeLimitGameStarted(args.Game); } catch (Exception ex) { logger.Error(ex, "Kippix: time limit (game started)"); }
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (!OverlayRunning) return new List<GameMenuItem>();
            return Overlay.GetGameMenuItems(args);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            // (0.99.54) stopped in its first minute: a launcher may be handing over to the game (HelperPlugin.Handover.cs)
            try { if (HandoverStart(args)) return; } catch (Exception ex) { logger.Warn("Kippix: launcher hand-over: " + ex.Message); }
            FinishStopped(args != null ? args.Game : null, args != null ? args.ElapsedSeconds : 0);
        }

        /// <summary>Everything a stopped game does (straight away, or once a launcher's game has closed too).</summary>
        private void FinishStopped(Game game, ulong elapsed)
        {
            try { Theme.RunningStopped(game); } catch (Exception ex) { logger.Error(ex, "Kippix: running list (game stopped)"); }
            try { Theme.GameRunningChanged(false); } catch (Exception ex) { logger.Error(ex, "Kippix: game stopped"); }
            try { Overlay.OnGameStopped(game); } catch (Exception ex) { logger.Error(ex, "Kippix: overlay (game stopped)"); }
            try { Theme.RecordSession(game, elapsed); } catch (Exception) { }
            try { Theme.TimeLimitGameStopped(game); } catch (Exception ex) { logger.Error(ex, "Kippix: time limit (game stopped)"); }
            try { Theme.GameSessionEnded(game, elapsed); } catch (Exception ex) { logger.Error(ex, "Kippix: session card"); }
            try { Theme.GameStoppedForSaves(game); } catch (Exception ex) { logger.Error(ex, "Kippix: save backup"); }
        }
    }
}
