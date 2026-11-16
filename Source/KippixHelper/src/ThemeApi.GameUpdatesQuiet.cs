using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Settings › Notifications › Game updates › Let Steam update games in the background (off unless switched on).
    ///
    /// Steam only finds and downloads updates while it is running, and a console-style setup that starts in Playnite never
    /// starts it until a Steam game is played: the update is then waiting just when you want to play. With this on, Kippix
    /// starts Steam once, quietly (steam.exe -silent: in the tray, no window), when no game is running; Steam does the rest
    /// by itself, and by its own default holds its downloads while one of its games is played.
    ///   - Once per Playnite session, and never when Steam is already running: closing Steam yourself is respected.
    ///   - On a device with a battery, only on mains power unless "Also on battery" is on (it waits for the charger).
    ///   - Nothing in Steam is changed, and Steam is never closed.
    /// </summary>
    public partial class ThemeApi
    {
        private bool steamQuietDone;
        private DispatcherTimer steamQuietTimer;

        private void ScheduleSteamQuiet()
        {
            if (!IsFullscreen || steamQuietTimer != null) return;
            // (a minute in: Playnite and its add-ons have settled; then every minute, for the charger or a game that ends)
            steamQuietTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(60) };
            steamQuietTimer.Tick += (s, e) => { try { SteamQuietTick(); } catch (Exception ex) { logger.Warn("Kippix: Steam in the background: " + ex.Message); } };
            steamQuietTimer.Start();
        }

        /// <summary>A game (not an app or a tool kept in the library) is running: Steam isn't started under it.</summary>
        private bool SteamQuietGameRunning()
        {
            try { return api.Database.Games.Any(g => g != null && g.IsRunning); }
            catch (Exception) { return true; }
        }

        private void SteamQuietTick()
        {
            if (!settings.SteamUpdatesQuiet || steamQuietDone) return;
            string dir = SteamPaths.SteamPath();
            string exe = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "steam.exe");
            if (exe == null || !File.Exists(exe)) { steamQuietDone = true; return; }   // (no Steam here)
            bool running = false;
            try
            {
                var ps = Process.GetProcessesByName("steam");
                running = ps.Length > 0;
                foreach (var p in ps) p.Dispose();
            }
            catch (Exception) { }
            if (running) { steamQuietDone = true; return; }   // (it's already at it)
            if (SteamQuietGameRunning()) return;
            if (hasBattery && onBattery && !settings.SteamUpdatesOnBattery) return;
            steamQuietDone = true;
            Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = false, WorkingDirectory = dir });
            logger.Info("Kippix: Steam started in the background (steam.exe -silent) so its game updates download by themselves");
        }

        public bool SteamUpdatesQuiet
        {
            get { return settings.SteamUpdatesQuiet; }
            set { if (settings.SteamUpdatesQuiet == value) return; settings.SteamUpdatesQuiet = value; SaveSettings(); Notify("SteamUpdatesQuiet"); Notify("SteamUpdatesBatteryRow"); }
        }

        public bool SteamUpdatesOnBattery
        {
            get { return settings.SteamUpdatesOnBattery; }
            set { if (settings.SteamUpdatesOnBattery == value) return; settings.SteamUpdatesOnBattery = value; SaveSettings(); Notify("SteamUpdatesOnBattery"); }
        }

        /// <summary>"Also on battery" is only offered where there is one.</summary>
        public bool SteamUpdatesBatteryRow { get { return settings.SteamUpdatesQuiet && hasBattery; } }
    }
}
