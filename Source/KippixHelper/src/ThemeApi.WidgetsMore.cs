using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace KippixHelper
{
    /// <summary>The 0.31 widgets: Friends online, PC stats and Latest capture.</summary>
    public partial class ThemeApi
    {
        // ---------------- PC stats ----------------

        private KippixOverlay.SystemStats pcStats;
        private DateTime pcStatsUsedAt = DateTime.MinValue;

        /// <summary>CPU, graphics card, memory, free space and the controller's battery. Measured only while the widget is on screen.</summary>
        private void FillPcStats(WidgetItem w)
        {
            pcStatsUsedAt = DateTime.Now;
            if (pcStats == null) pcStats = new KippixOverlay.SystemStats();
            pcStats.Start();
            var s = pcStats;
            w.Big = s.Cpu >= 0 ? s.Cpu + "%" : "…";
            w.Sub = Loc("LOC_KX_PcCpu", "Processor in use");
            var lines = new List<WidgetLine>();
            if (s.GpuLoad >= 0 || s.GpuTemp >= 0)
            {
                var gpu = new List<string>();
                if (s.GpuLoad >= 0) gpu.Add(s.GpuLoad + "%");
                if (s.GpuTemp >= 0) gpu.Add(s.GpuTemp + "°C");
                lines.Add(new WidgetLine { Label = Loc("LOC_KX_PcGpu", "Graphics"), Value = string.Join("  ·  ", gpu) });
            }
            if (s.MemTotalGb > 0) lines.Add(new WidgetLine { Label = Loc("LOC_KX_PcMemory", "Memory"), Value = s.MemUsedGb.ToString("0.0", CultureInfo.CurrentCulture) + " / " + Math.Round(s.MemTotalGb) + " GB" });
            try
            {
                string sys = Path.GetPathRoot(Environment.SystemDirectory);
                var d = new DriveInfo(sys);
                if (d.IsReady) lines.Add(new WidgetLine { Label = string.Format(Loc("LOC_KX_PcFree", "Free on {0}"), sys.TrimEnd('\\')), Value = KippixOverlay.Capture.DriveSize(d.AvailableFreeSpace) });
            }
            catch (Exception) { }
            if (!string.IsNullOrEmpty(BatteryText)) lines.Add(new WidgetLine { Label = batteryIsDevice ? Loc("LOC_KX_PcBattery", "Battery") : Loc("LOC_KX_PcController", "Controller"), Value = BatteryText });
            w.Lines = lines;
            w.Status = "";
        }

        /// <summary>From the 2 second timer: the stats stop being measured once the widget isn't being shown.</summary>
        private void PcStatsTick()
        {
            // the widget timer only runs every 20 s: live widgets are refreshed from here (every 2 s) while the home screen shows
            if (Widgets != null && settings.WidgetsOn && HomeShowing)
            {
                if (Widgets.Any(w => w.Type == "pcstats")) RefreshWidgetsOfType("pcstats");
                if (Widgets.Any(w => w.Type == "downloads")) RefreshWidgetsOfType("downloads");
            }
            if (pcStats != null && (DateTime.Now - pcStatsUsedAt).TotalSeconds > 25) { pcStats.Stop(); pcStats = null; }
        }

        // ---------------- Friends online ----------------

        private WidgetData FriendsOnlineData()
        {
            var d = new WidgetData { Lines = new List<WidgetLine>() };
            string key = SteamKey;
            if (string.IsNullOrEmpty(key))
            {
                d.Status = Loc("LOC_KX_WgFriendsKey", "Needs a Steam Web API key in the add-on's desktop settings.");
                return d;
            }
            string me = MySteamId();
            if (me != null) OnUi(() => RefreshFriendsAsync(key, me));
            List<FriendInfo> all;
            lock (friendLock) all = friendInfo.Values.ToList();
            var online = all.Where(f => f.State > 0 || !string.IsNullOrEmpty(f.PlayingAppId))
                            .OrderBy(f => string.IsNullOrEmpty(f.PlayingAppId) ? 1 : 0).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var f in online.Take(5))
            {
                string what;
                if (!string.IsNullOrEmpty(f.PlayingAppId))
                {
                    string name = f.PlayingName;
                    if (string.IsNullOrEmpty(name))
                    {
                        var g = api.Database.Games.FirstOrDefault(x => x.PluginId == SteamPluginId && x.GameId == f.PlayingAppId);
                        name = g != null ? g.Name : Loc("LOC_KX_SpAGame", "a game");
                    }
                    what = string.Format(Loc("LOC_KX_SpPlayingNow", "Playing {0}"), name);
                }
                else what = f.State == 3 || f.State == 4 ? Loc("LOC_KX_Away", "Away") : f.State == 2 ? Loc("LOC_KX_Busy", "Busy") : Loc("LOC_KX_Online", "Online");
                d.Lines.Add(new WidgetLine { Label = f.Name, Value = what, IsOnline = true });
            }
            if (online.Count > 5) d.Lines.Add(new WidgetLine { Label = string.Format(Loc("LOC_KX_FriendsMore", "+{0} more"), online.Count - 5), Value = "" });
            if (d.Lines.Count == 0) d.Status = all.Count == 0 ? Loc("LOC_KX_WgFriendsLoading", "Looking for your Steam friends…") : Loc("LOC_KX_WgNoneOnline", "No friends online right now.");
            return d;
        }

        // ---------------- Latest capture ----------------

        /// <summary>Your newest screenshot or clip from the gallery's folders.</summary>
        private void FillLatestCapture(WidgetItem w, int gen)
        {
            Action show = () =>
            {
                var newest = galleryFiles.OrderByDescending(f => f.Date).FirstOrDefault();
                if (newest == null) { w.Status = Loc("LOC_KX_WgNoCapture", "No screenshots or clips yet."); return; }
                string path = newest.Path;
                bool video = newest.IsVideo;
                string caption = (newest.GameId == Guid.Empty ? Loc("LOC_KX_OtherCaptures", "Other") : newest.GameName) + "  ·  " + AgoText(newest.Date) + (video ? "  ·  " + Loc("LOC_KX_Clip", "clip") : "");
                ShellMedia.Run(() =>
                {
                    ImageSource img = ShellMedia.Thumbnail(path, 900);
                    if (img == null && !video) img = Images.LoadUncached(path, 900);
                    OnUi(() => { if (gen != w.Gen) return; w.Photo = img; w.PhotoCaption = caption; w.Status = img == null ? Loc("LOC_KX_WgNoPreview", "No preview for this one.") : ""; });
                });
            };
            if (galleryFiles.Count > 0 && (DateTime.Now - galleryScannedAt).TotalMinutes < 5) { show(); return; }
            KippixShared.SafeWork.Queue(_ =>
            {
                List<GalleryFile> found = null;
                try { found = FindGalleryFiles(); } catch (Exception) { }
                OnUi(() => { if (found != null && !galleryScanning) { galleryFiles = found; galleryScannedAt = DateTime.Now; } if (gen == w.Gen) show(); });
            });
        }
    }
}
