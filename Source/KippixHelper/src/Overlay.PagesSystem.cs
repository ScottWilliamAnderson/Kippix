using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using IOPath = System.IO.Path;

namespace KippixOverlay
{
    internal partial class OverlayWindow
    {
        // ================= Audio =================

        private List<MenuEntry> AudioEntries()
        {
            var l = new List<MenuEntry>();
            l.Add(Header("Sound plays on"));
            List<AudioDevice> devices = Audio.OutputDevices();
            if (devices.Count == 0)
            {
                l.Add(Note("\uE7F5", "No output devices found"));
                plugin.LogInfo("Kippix overlay: sound: no output devices (" + (Audio.LastError ?? "Windows listed none") + ")");   // (0.99.71)
            }
            foreach (AudioDevice d in devices)
            {
                AudioDevice dev = d;
                string glyph = d.FormFactor == 3 || d.FormFactor == 5 ? "\uE7F6" : (d.FormFactor == 9 ? "\uE7F4" : "\uE7F5");
                l.Add(new MenuEntry
                {
                    Glyph = glyph,
                    Title = d.Name,
                    Dot = () => dev.IsDefault,
                    Sub = () => dev.IsDefault ? "Playing here now" : null,
                    Activate = () =>
                    {
                        if (dev.IsDefault) return;
                        if (Audio.SetDefault(dev.Id)) Rebuild();
                        else plugin.Toast("Couldn't switch output", dev.Name);
                    }
                });
            }

            // (0.99.72) Bluetooth headphones and speakers: connect / disconnect without leaving the game
            l.Add(new MenuEntry { Glyph = "\uE702", Title = "Bluetooth headphones and speakers", Sub = () => BtDevicesLine(), Chevron = true, Activate = OpenBtDevices });
            l.Add(Divider());
            l.Add(Header("Volume"));
            l.Add(VolumeRow("\uE767", "Master volume",
                (out bool m) => Audio.GetMaster(out m),
                v => Audio.SetMaster(v),
                () => Audio.ToggleMasterMute()));
            l.Add(VolumeRow("\uE7FC", "Game",
                (out bool m) => Audio.GetAppVolume(plugin.IsGamePid, out m),
                v => Audio.SetAppVolume(plugin.IsGamePid, v),
                () => Audio.ToggleAppMute(plugin.IsGamePid)));
            l.Add(VolumeRow("\uE8BD", "Discord chat",
                (out bool m) => Audio.GetAppVolume(DiscordHelper.IsDiscordPid, out m),
                v => Audio.SetAppVolume(DiscordHelper.IsDiscordPid, v),
                () => Audio.ToggleAppMute(DiscordHelper.IsDiscordPid)));
            return l;
        }

        private delegate float VolumeGetter(out bool muted);

        private MenuEntry VolumeRow(string glyph, string title, VolumeGetter get, Action<float> set, Action toggleMute)
        {
            var pct = new TextBlock { FontSize = 18, Foreground = OverlayTheme.TextMain, Width = 110, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            var bar = new Bar(theme, 10, 200);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(pct);
            right.Children.Add(bar.Root);

            float last = -1;
            Action refresh = () =>
            {
                bool muted;
                float v = get(out muted);
                last = v;
                if (v < 0)
                {
                    pct.Text = "not playing";
                    bar.Root.Visibility = Visibility.Hidden;
                }
                else
                {
                    pct.Text = muted ? "muted" : Math.Round(v * 100) + "%";
                    bar.Root.Visibility = Visibility.Visible;
                    bar.Root.Opacity = muted ? 0.4 : 1;
                    bar.Set(v);
                }
            };
            refresh();

            return new MenuEntry
            {
                Glyph = glyph,
                Title = title,
                Right = right,
                Refresh = refresh,
                Adjust = d =>
                {
                    if (last < 0) return;
                    set((float)Math.Round(last + d * 0.05, 2));
                    refresh();
                },
                Activate = () => { toggleMute(); refresh(); }
            };
        }

        // ================= Settings =================

        private List<MenuEntry> SettingsEntries()
        {
            var l = new List<MenuEntry>();
            SystemStats st = plugin.Stats;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            var cpu = StatCell(grid, "CPU", 0, 0);
            var gpu = StatCell(grid, "GPU", 0, 1);
            var mem = StatCell(grid, "Memory", 1, 0);
            var up = StatCell(grid, "On for", 1, 1);
            Action refresh = () =>
            {
                cpu.Text = st.Cpu >= 0 ? st.Cpu + "%" : "\u2014";
                var parts = new List<string>();
                if (st.GpuTemp >= 0) parts.Add(st.GpuTemp + "\u00B0C");
                if (st.GpuLoad >= 0) parts.Add(st.GpuLoad + "%");
                if (st.GpuPower >= 0) parts.Add(Math.Round(st.GpuPower) + " W");
                gpu.Text = parts.Count > 0 ? string.Join("  \u00B7  ", parts) : "\u2014";
                mem.Text = st.MemTotalGb > 0 ? st.MemUsedGb.ToString("0.0") + " / " + Math.Round(st.MemTotalGb) + " GB" : "\u2014";
                TimeSpan u = st.Uptime;
                up.Text = u.TotalHours >= 24
                    ? string.Format("{0} d {1} h", (int)u.TotalDays, u.Hours)
                    : string.Format("{0} h {1} min", (int)u.TotalHours, u.Minutes);
            };
            refresh();
            l.Add(new MenuEntry { Kind = EntryKind.Card, Extra = grid, Refresh = refresh, Selectable = false });

            AddDeviceRows(l);   // (0.99.72) HDR and Bluetooth
            l.Add(new MenuEntry
            {
                Glyph = "\uE713",
                Title = "Plugin settings",
                Sub = () => "Buttons, screenshots, theme, Discord",
                Chevron = true,
                Activate = () => Push("Plugin settings", PluginSettingsEntries)
            });
            l.Add(new MenuEntry
            {
                Glyph = "\uE896",
                TitleFunc = () => plugin.GuideUpdateRunning ? "Updating achievement guides\u2026" : "Update achievement guides",
                Sub = () => plugin.GuideUpdateStatus,
                Activate = () => plugin.StartGuideUpdate(true)
            });
            l.Add(new MenuEntry
            {
                Glyph = "\uE7E8",
                Title = "Shut down",
                Chevron = true,
                Activate = () => PushConfirm("Shut down",
                    "Your PC will hibernate. When you turn it back on, everything (even your game) picks up where you left off.",
                    "\uE7E8", "Shut down", false, () => plugin.Hibernate())
            });
            l.Add(new MenuEntry
            {
                Glyph = "\uE72C",
                Title = "Restart PC",
                Chevron = true,
                Activate = () => PushConfirm("Restart PC", "Any unsaved game progress will be lost.", "\uE72C", "Restart now", true, () => plugin.RestartPc())
            });

            l.Add(Divider());
            l.Add(Header("Storage"));
            foreach (DriveInfo d in plugin.Drives())
            {
                double used = d.TotalSize > 0 ? 1.0 - (double)d.TotalFreeSpace / d.TotalSize : 0;
                var bar = new Bar(theme, 8, 0);
                bar.Set(used);
                var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(bar.Root);
                var pctText = new TextBlock { Text = Math.Round(used * 100) + "% used", FontSize = 17, Foreground = OverlayTheme.TextMain, Margin = new Thickness(16, 0, 0, 0) };
                Grid.SetColumn(pctText, 1);
                row.Children.Add(pctText);

                string label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : d.Name.TrimEnd('\\') + "  " + d.VolumeLabel;
                string sub = Capture.DriveSize(d.TotalFreeSpace) + " free of " + Capture.DriveSize(d.TotalSize);
                l.Add(new MenuEntry
                {
                    Glyph = d.DriveType == DriveType.Removable ? "\uE88E" : "\uEDA2",
                    Title = label,
                    Sub = () => sub,
                    Extra = row
                });
            }

            // ---- Downloads (the theme's list: Steam, Epic and GOG with progress where their files tell it; Playnite installs without) ----
            l.Add(Header("Downloads"));
            // What's downloading right now first, then the rest from most to least complete.
            List<KippixHelper.DownloadInfo> downloads = plugin.ShownDownloads()
                .OrderByDescending(x => x.State == "Downloading" || x.State == "Updating" || x.State == "Installing" || x.State == "Verifying")
                .ThenByDescending(x => x.Progress)
                .ThenByDescending(x => x.Done)
                .ToList();
            if (downloads.Count == 0)
            {
                l.Add(new MenuEntry
                {
                    Glyph = "\uE896",
                    TitleFunc = () => plugin.ShownDownloads().Count > 0 ? "A download has started (reopen to see it)" : "Nothing downloading",
                    Dim = true
                });
            }
            foreach (KippixHelper.DownloadInfo d in downloads)
            {
                string key = d.Key;
                KippixHelper.DownloadInfo last = d;
                var dlBar = new Bar(theme, 8, 0);
                dlBar.Root.Margin = new Thickness(0, 10, 0, 2);
                Action update = () =>
                {
                    KippixHelper.DownloadInfo now = plugin.ShownDownloads().FirstOrDefault(x => x.Key == key);
                    if (now != null) last = now;
                    dlBar.Set(now == null ? 1 : Math.Max(0, last.Progress));
                    dlBar.Root.Visibility = last.Progress >= 0 || now == null ? Visibility.Visible : Visibility.Collapsed;
                };
                update();
                l.Add(new MenuEntry
                {
                    Glyph = "\uE896",
                    Title = d.Name,
                    Sub = () =>
                    {
                        KippixHelper.DownloadInfo now = plugin.ShownDownloads().FirstOrDefault(x => x.Key == key);
                        return now != null ? now.Text() : "Finished";
                    },
                    Extra = dlBar.Root,
                    Refresh = update
                });
            }
            return l;
        }

        private TextBlock StatCell(Grid grid, string label, int row, int col)
        {
            var sp = new StackPanel { Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 0) };
            sp.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = theme.Accent });
            var value = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = OverlayTheme.TextMain };
            sp.Children.Add(value);
            Grid.SetRow(sp, row);
            Grid.SetColumn(sp, col);
            grid.Children.Add(sp);
            return value;
        }

        private MenuEntry Option(string glyph, string title, Func<string> value, Action<int> adjust)
        {
            var valueText = new TextBlock { FontSize = 18, Foreground = OverlayTheme.TextMain, VerticalAlignment = VerticalAlignment.Center };
            Action refresh = () => valueText.Text = "\u2039  " + value() + "  \u203A";
            refresh();
            return new MenuEntry
            {
                Glyph = glyph,
                Title = title,
                Right = valueText,
                Refresh = refresh,
                Adjust = d => { adjust(d); plugin.SaveConfig(); refresh(); }
            };
        }

        private List<MenuEntry> PluginSettingsEntries()
        {
            var c = plugin.Config;
            var l = new List<MenuEntry>();
            l.Add(Option("\uE7FC", "Open overlay with", () => OverlayConfig.TriggerNames[c.TriggerMode],
                d => c.TriggerMode = OverlayConfig.Wrap(c.TriggerMode + d, OverlayConfig.TriggerNames.Length)));
            l.Add(Option("\uE7FC", "Steam Controller's Steam button opens it", () => c.SteamButtonOpens ? "On" : "Off", d => c.SteamButtonOpens = !c.SteamButtonOpens));
            l.Add(Note("\uE946", "Steam opens its own overlay on that button too. Or bind any Steam Controller button to " + (string.IsNullOrEmpty(c.OverlayHotkey) ? "the overlay's shortcut" : c.OverlayHotkey) + " in Steam Input."));
            l.Add(Option("\uE740", "Only in fullscreen mode", () => c.OnlyInFullscreen ? "On" : "Off", d => c.OnlyInFullscreen = !c.OnlyInFullscreen));
            l.Add(Option("\uE7BA", "Confirm before closing", () => c.ConfirmClose ? "On" : "Off", d => c.ConfirmClose = !c.ConfirmClose));
            l.Add(Option("\uE7FC", "Vibrate when it opens", () => c.OpenVibration ? "On" : "Off", d => c.OpenVibration = !c.OpenVibration));
            l.Add(Option("\uE7E8", "Hold Guide for Power off / Hibernate", () => c.HoldGuidePower ? "On" : "Off", d => c.HoldGuidePower = !c.HoldGuidePower));
            l.Add(Option("\uE7FC", "Vibration strength", () => OverlayConfig.VibrationNames[OverlayConfig.Wrap(c.VibrationStrength, OverlayConfig.VibrationNames.Length)],
                d => { c.VibrationStrength = OverlayConfig.Wrap(c.VibrationStrength + d, OverlayConfig.VibrationNames.Length); int vs = c.VibrationStrength; if (plugin.Poller != null) plugin.Poller.Pulse(vs == 0 ? 50 : vs == 1 ? 60 : 70, vs == 2 ? 0.12 : 0.0, vs == 0 ? 0.22 : vs == 1 ? 0.4 : 0.6); }));
            l.Add(Option("\uE769", "When the overlay opens", () => OverlayConfig.WhenOpenedNames[OverlayConfig.Wrap(c.WhenOpened, OverlayConfig.WhenOpenedNames.Length)],
                d => c.WhenOpened = OverlayConfig.Wrap(c.WhenOpened + d, OverlayConfig.WhenOpenedNames.Length)));
            l.Add(Option("\uE769", "Freeze the game while it's open", () => GameFreeze.ModeNames[OverlayConfig.Wrap(c.FreezeGame, GameFreeze.ModeNames.Length)],
                d => c.FreezeGame = OverlayConfig.Wrap(c.FreezeGame + d, GameFreeze.ModeNames.Length)));
            l.Add(Note("\uE946", "Pause: the overlay takes the focus, so games that pause when you switch away pause. Online games with anti-cheat may not like that: Let it keep running leaves the game alone (it keeps the focus; it may see your button presses; also stops a black flash in some full-screen games). Automatic does that for online / MMO games. From the next time it opens."));

            l.Add(Header("Achievement pop-ups"));
            l.Add(Option("\uE7C1", "Kippix's own pop-ups", () => c.AchToasts ? "On" : "Off", d => { c.AchToasts = !c.AchToasts; plugin.SaveConfig(); AchievementToasts.Apply(); }));
            l.Add(Option("\uE8BB", "Hide Playnite Achievements' pop-ups", () => c.AchHideOther ? "On" : "Off", d => { c.AchHideOther = !c.AchHideOther; plugin.SaveConfig(); AchievementToasts.Apply(); }));
            l.Add(Option("\uE790", "Design", () => AchToastWindow.StyleNames[OverlayConfig.Wrap(c.AchToastStyle, AchToastWindow.StyleNames.Length)],
                d => c.AchToastStyle = OverlayConfig.Wrap(c.AchToastStyle + d, AchToastWindow.StyleNames.Length)));
            l.Add(Option("\uE8A7", "Where they show", () => AchToastWindow.PositionNames[OverlayConfig.Wrap(c.AchToastPosition, AchToastWindow.PositionNames.Length)],
                d => c.AchToastPosition = OverlayConfig.Wrap(c.AchToastPosition + d, AchToastWindow.PositionNames.Length)));
            l.Add(Option("\uE916", "How long each stays", () => c.AchToastSeconds + " s", d => c.AchToastSeconds = Math.Max(3, Math.Min(12, c.AchToastSeconds + d))));
            l.Add(Option("\uE9D2", "Progress updates too", () => c.AchToastProgress ? "On" : "Off", d => c.AchToastProgress = !c.AchToastProgress));
            if (AchievementToasts.HasSound(c))
                l.Add(Option("\uE767", "Unlock sound volume", () => c.AchSoundVolume + "%",
                    d => { c.AchSoundVolume = Math.Max(10, Math.Min(100, (c.AchSoundVolume + d * 10) / 10 * 10)); AchievementToasts.PlaySound(c.AchSoundFile, c.AchSoundVolume); }));
            l.Add(new MenuEntry
            {
                Glyph = "\uE768",
                Title = "Show a test pop-up",
                Sub = () => "Closes the overlay, then shows one in a couple of seconds",
                Chevron = true,
                Activate = () => plugin.Resume(() => AchievementToasts.ShowTest(), 1800)
            });
            l.Add(new MenuEntry
            {
                Glyph = "\uE8A9",
                Title = "Show all 7 designs",
                Sub = () => "Closes the overlay, then shows each design in turn",
                Chevron = true,
                Activate = () => plugin.Resume(() => AchievementToasts.ShowAllDesigns(), 1800)
            });
            l.Add(Note("\uE946", "They need Playnite Achievements: it finds the unlocks while you play, Kippix shows them. Your own unlock sound is chosen in Playnite's add-on settings (Kippix Helper › Overlay); without one, Playnite Achievements' sound plays."));

            l.Add(Header("Screenshots"));
            l.Add(Option("\uE722", "Screenshot method", () => OverlayConfig.ScreenshotNames[c.ScreenshotMethod],
                d => c.ScreenshotMethod = OverlayConfig.Wrap(c.ScreenshotMethod + d, OverlayConfig.ScreenshotNames.Length)));
            l.Add(Option("\uE767", "Sound when one is saved", () => c.ScreenshotSound ? "On" : "Off", d => { c.ScreenshotSound = !c.ScreenshotSound; if (c.ScreenshotSound) Chime.Play(); }));
            l.Add(new MenuEntry
            {
                Glyph = "\uE838",
                Title = "Screenshot folder",
                Sub = () => c.ScreenshotFolder,
                Chevron = true,
                Activate = () => OpenFolderPicker()
            });

            l.Add(Header("Recent gameplay"));
            l.Add(Option("\uE714", "Save recent gameplay with", () => OverlayConfig.ReplayNames[c.ReplayMethod],
                d => c.ReplayMethod = OverlayConfig.Wrap(c.ReplayMethod + d, OverlayConfig.ReplayNames.Length)));

            l.Add(Header("Look"));
            var themeRow = Option("\uE790", "Theme colour", () => OverlayTheme.Get(c.Theme).Name, d => c.Theme = OverlayConfig.Wrap(c.Theme + d, OverlayTheme.All.Length));
            var baseAdjust = themeRow.Adjust;
            themeRow.Adjust = d =>
            {
                baseAdjust(d);
                theme = OverlayTheme.Get(c.Theme);
                Dispatcher.BeginInvoke(new Action(RestyleAll));
            };
            l.Add(themeRow);

            l.Add(Option("\uE916", "HowLongToBeat on the game tile", () => c.ShowHowLongToBeat ? "On" : "Off", d => c.ShowHowLongToBeat = !c.ShowHowLongToBeat));
            l.Add(Option("\uE7C1", "Story progress on the game tile", () => c.ShowStoryProgress ? "On" : "Off", d => c.ShowStoryProgress = !c.ShowStoryProgress));

            l.Add(Header("Achievement guides"));
            l.Add(Option("\uE896", "Update guides when Playnite opens", () => c.UpdateGuidesOnStartup ? "On" : "Off", d => c.UpdateGuidesOnStartup = !c.UpdateGuidesOnStartup));
            l.Add(Option("\uE7FC", "Games to update", () => c.GuideUpdateInstalledOnly ? "Installed only" : "All games", d => c.GuideUpdateInstalledOnly = !c.GuideUpdateInstalledOnly));

            l.Add(Header("Discord"));
            l.Add(new MenuEntry
            {
                Glyph = "\uE8BD",
                Title = "Discord connection",
                Sub = () => plugin.DiscordConnectionText(),
                Chevron = true,
                Activate = () => Push("Discord connection", DiscordSetupEntries)
            });
            l.Add(Option("\uE8BD", "Show new messages in-game", () => c.ShowDiscordMessages ? "On" : "Off", d => c.ShowDiscordMessages = !c.ShowDiscordMessages));
            l.Add(Option("\uE740", "Maximise Discord window", () => c.MaximizeDiscord ? "On" : "Off", d => c.MaximizeDiscord = !c.MaximizeDiscord));

            l.Add(Divider());
            l.Add(Row("\uE70F", "Edit config file", "Hotkeys, quick replies, Discord app keys", () => plugin.OpenConfigFile()));
            l.Add(Row("\uE777", "Reset to defaults", null, () =>
            {
                var keep = plugin.Config;
                plugin.Config = new OverlayConfig
                {
                    DiscordClientId = keep.DiscordClientId,
                    DiscordClientSecret = keep.DiscordClientSecret,
                    QuickReplies = keep.QuickReplies
                };
                plugin.SaveConfig();
                theme = OverlayTheme.Get(0);
                RestyleAll();
            }));
            return l;
        }

        // ================= Folder picker =================

        private void OpenFolderPicker()
        {
            int returnDepth = stack.Count;
            PushFolder(null, returnDepth);
        }

        private void PushFolder(string path, int returnDepth)
        {
            Push(path == null ? "Choose a folder" : path, () => FolderEntries(path, returnDepth));
        }

        private void UseFolder(string path, int returnDepth)
        {
            try
            {
                Directory.CreateDirectory(path);
                plugin.Config.ScreenshotFolder = path;
                plugin.SaveConfig();
                plugin.Toast("Screenshots will save to", path);
                PopTo(returnDepth);
            }
            catch (Exception ex)
            {
                plugin.Toast("Can't use that folder", ex.Message);
            }
        }

        private List<MenuEntry> FolderEntries(string path, int returnDepth)
        {
            var l = new List<MenuEntry>();
            if (path == null)
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                var quick = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Pictures\\PlayniteGame", OverlayConfig.DefaultScreenshotFolder),
                    new KeyValuePair<string, string>("Pictures\\Screenshots", IOPath.Combine(pics, "Screenshots")),
                    new KeyValuePair<string, string>("Videos\\Captures", IOPath.Combine(vids, "Captures")),
                    new KeyValuePair<string, string>("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
                };
                l.Add(Header("Quick picks"));
                foreach (var q in quick)
                {
                    string p = q.Value;
                    bool current = string.Equals(p.TrimEnd('\\'), plugin.Config.ScreenshotFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    l.Add(new MenuEntry
                    {
                        Glyph = "\uE8B7",
                        Title = q.Key,
                        Dot = () => current,
                        Activate = () => UseFolder(p, returnDepth)
                    });
                }
                l.Add(Header("Browse"));
                foreach (DriveInfo d in plugin.Drives())
                {
                    string root = d.RootDirectory.FullName;
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? root : root + "  " + d.VolumeLabel;
                    l.Add(new MenuEntry { Glyph = "\uEDA2", Title = label, Chevron = true, Activate = () => PushFolder(root, returnDepth) });
                }
                return l;
            }

            l.Add(Row("\uE73E", "Save screenshots here", null, () => UseFolder(path, returnDepth)));
            string parent = null;
            try { var di = Directory.GetParent(path); parent = di != null ? di.FullName : null; } catch (Exception) { }
            l.Add(Row("\uE74A", "Up one level", null, () => PushFolder(parent, returnDepth)));

            l.Add(Header("Folders"));
            List<string> subs = new List<string>();
            try
            {
                foreach (string dir in Directory.GetDirectories(path))
                {
                    try
                    {
                        var attr = File.GetAttributes(dir);
                        if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch (Exception) { continue; }
                    subs.Add(dir);
                }
            }
            catch (Exception ex)
            {
                l.Add(Note("\uE783", "Can't open this folder: " + ex.Message));
                return l;
            }
            subs.Sort(StringComparer.OrdinalIgnoreCase);
            if (subs.Count == 0) l.Add(Note("\uE8B7", "No folders inside"));
            foreach (string dir in subs.Take(300))
            {
                string d2 = dir;
                l.Add(new MenuEntry { Glyph = "\uE8B7", Title = IOPath.GetFileName(dir), Chevron = true, Activate = () => PushFolder(d2, returnDepth) });
            }
            return l;
        }
    }
}
