using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Input;
using Microsoft.Win32;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// (r12) The game page's Mods card: the game on Nexus Mods, with how many mods it has, a button to open its Nexus Mods page in
    /// the theme's browser and, when Vortex or Mod Organizer 2 is installed, a button to open it. The game is found by the Nexus
    /// Mods checker add-on's link ("Nexus Mods"; that add-on only adds it when run from the desktop), else in Nexus Mods' public list
    /// of games (data.nexusmods.com games.json, kept in the helper's ExtensionsData folder for 7 days), matched by name the way the
    /// add-on does (letters and digits only, lower case). Works for games that aren't installed too.
    /// </summary>
    public partial class ThemeApi
    {
        private class NexusGame
        {
            public string Name, Url, Domain;
            public long Mods, Downloads;
        }

        private const string NexusGamesUrl = "https://data.nexusmods.com/file/nexus-data/games.json";
        private static Dictionary<string, NexusGame> nexusByName, nexusByDomain;   // null until read
        private static bool nexusLoading;
        private static Action nexusWaiting;   // what to do when the list has been read
        private static DateTime nexusFailedAt = DateTime.MinValue;
        private static readonly object nexusLock = new object();
        private string NexusCachePath { get { return Path.Combine(plugin.GetPluginUserDataPath(), "nexus-games.json"); } }

        private string detModsUrl;          // the game's Nexus Mods page (null: card hidden)
        private string detModsTitle = "", detModsSub = "";
        private bool detModsFromLink;
        private int modsGen;
        public bool ShowDetMods { get { return settings.DetSecMods && !string.IsNullOrEmpty(detModsUrl); } }
        public bool DetIntMods { get { return DetInteractive("mods") && !DetBlank("mods"); } }
        public string DetModsTitle { get { return detModsTitle; } }
        public string DetModsSub { get { return detModsSub; } }

        private void NotifyMods()
        {
            foreach (string n in new[] { "ShowDetMods", "DetIntMods", "DetModsTitle", "DetModsSub", "ShowModsVortex", "ShowModsMo2", "ShowDetMore", "DetHintMoreBelow", "ShowDetNexus", "ShowModsCardButtons" }) Notify(n);
        }

        private ICommand openModsNexusCommand, openVortexCommand, openMo2Command;
        public ICommand OpenModsNexusCommand
        {
            get { return openModsNexusCommand ?? (openModsNexusCommand = new RelayCommand(_ => { if (string.IsNullOrEmpty(detModsUrl)) return; PlaySound("select"); OpenLink(detModsUrl); })); }
        }
        public ICommand OpenVortexCommand { get { return openVortexCommand ?? (openVortexCommand = new RelayCommand(_ => StartModManager(vortexExe, "Vortex"))); } }
        public ICommand OpenMo2Command { get { return openMo2Command ?? (openMo2Command = new RelayCommand(_ => StartModManager(mo2Exe, "Mod Organizer 2"))); } }

        // ---------------- the card ----------------

        private static readonly Regex NexusDomainRx = new Regex(@"nexusmods\.com/([a-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private void FillGameMods(Game g, int gen)
        {
            int my = ++modsGen;
            detModsUrl = null;
            detModsTitle = detModsSub = "";
            detModsFromLink = false;
            if (g == null || !(settings.DetSecMods || settings.ShowNexusLink)) { NotifyMods(); return; }   // (r15) the Nexus Mods button uses it too
            FindModManagers();
            // the Nexus Mods checker's link (named "Nexus Mods", or any link to a game on nexusmods.com)
            string linkUrl = null;
            try
            {
                var link = g.Links != null ? g.Links.FirstOrDefault(l => l != null && !string.IsNullOrEmpty(l.Url) && l.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                               && (string.Equals((l.Name ?? "").Trim(), "Nexus Mods", StringComparison.OrdinalIgnoreCase) || NexusDomainRx.IsMatch(l.Url))) : null;
                if (link != null) linkUrl = link.Url;
            }
            catch (Exception) { }
            string name = g.Name ?? "";
            if (linkUrl != null)
            {
                detModsUrl = linkUrl;
                detModsFromLink = true;
                detModsTitle = Loc("LOC_KX_ModsOnNexusTitle", "On Nexus Mods");
            }
            if (nexusByName != null) { ApplyNexusMatch(name, linkUrl); NotifyMods(); return; }
            NotifyMods();
            // Nexus Mods' list isn't read yet: read it (from the copy kept here, else downloaded), then match
            var id = g.Id;
            LoadNexusGames(() => OnUi(() =>
            {
                if (my != modsGen || !StillShowing(id) || !(settings.DetSecMods || settings.ShowNexusLink)) return;
                ApplyNexusMatch(name, linkUrl);
                NotifyMods();
                ApplyDetCardLooks();
            }));
        }

        /// <summary>The card's text (and, without the add-on's link, its page) from Nexus Mods' list.</summary>
        private void ApplyNexusMatch(string name, string linkUrl)
        {
            NexusGame n = null;
            if (linkUrl != null)
            {
                var m = NexusDomainRx.Match(linkUrl);
                if (m.Success && nexusByDomain != null) nexusByDomain.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out n);
            }
            if (n == null) n = NexusFind(name);
            if (n == null)
            {
                if (linkUrl == null) return;   // not on Nexus Mods (as far as its list knows): no card
                detModsSub = Loc("LOC_KX_ModsFromChecker", "Found by the Nexus Mods checker add-on");
                return;
            }
            if (detModsUrl == null) detModsUrl = n.Url;
            detModsTitle = n.Mods > 0 ? string.Format(Loc("LOC_KX_ModsCount", "{0} mods on Nexus Mods"), n.Mods.ToString("N0", CultureInfo.CurrentCulture))
                                      : Loc("LOC_KX_ModsOnNexusTitle", "On Nexus Mods");
            var bits = new List<string>();
            if (n.Downloads > 0) bits.Add(string.Format(Loc("LOC_KX_ModsDownloads", "{0} downloads"), BigNumber(n.Downloads)));
            if (!string.IsNullOrEmpty(n.Name) && NormName(n.Name) != NormName(name)) bits.Add(n.Name);
            if (detModsFromLink) bits.Add(Loc("LOC_KX_ModsLinked", "linked by Nexus Mods checker"));
            detModsSub = string.Join("  ·  ", bits);
        }

        private static string BigNumber(long v)
        {
            if (v >= 1000000000) return (v / 1000000000.0).ToString("0.#", CultureInfo.CurrentCulture) + "B";
            if (v >= 1000000) return (v / 1000000.0).ToString("0.#", CultureInfo.CurrentCulture) + "M";
            if (v >= 10000) return (v / 1000.0).ToString("0", CultureInfo.CurrentCulture) + "K";
            return v.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static readonly string[] EditionWords =
        {
            "gameoftheyearedition", "goty", "gotyedition", "definitiveedition", "enhancededition", "completeedition", "remastered", "remaster",
            "directorscut", "ultimateedition", "deluxeedition", "standardedition", "legendaryedition", "anniversaryedition", "thefinalcut", "edition"
        };

        /// <summary>
        /// The game in Nexus Mods' list by name: the whole name (letters and digits, lower case, as the Nexus Mods checker matches),
        /// then without an edition word at the end, without a leading "The", the part before a colon / dash ("The Witcher 3: Wild
        /// Hunt") and the part after it ("The Elder Scrolls V: Skyrim Special Edition"). Only whole-name matches: never a guess.
        /// </summary>
        private static NexusGame NexusFind(string name)
        {
            var byName = nexusByName;
            if (byName == null || string.IsNullOrWhiteSpace(name)) return null;
            var tries = new List<string>();
            Action<string> add = s =>
            {
                string n = NormName(s);
                if (n.Length < 3 || tries.Contains(n)) return;
                tries.Add(n);
                foreach (var w in EditionWords)
                    if (n.EndsWith(w, StringComparison.Ordinal) && n.Length - w.Length >= 3 && !tries.Contains(n.Substring(0, n.Length - w.Length))) tries.Add(n.Substring(0, n.Length - w.Length));
                if (n.StartsWith("the", StringComparison.Ordinal) && n.Length > 6 && !tries.Contains(n.Substring(3))) tries.Add(n.Substring(3));
            };
            string clean = name.Replace("™", "").Replace("®", "").Trim();
            add(clean);
            int cut = clean.IndexOfAny(new[] { ':', '–', '—' });
            if (cut < 0) { int dash = clean.IndexOf(" - ", StringComparison.Ordinal); if (dash > 0) cut = dash; }
            if (cut > 0)
            {
                add(clean.Substring(0, cut));
                add(clean.Substring(cut + 1).TrimStart('-', ' '));
            }
            foreach (var t in tries)
            {
                NexusGame g;
                if (byName.TryGetValue(t, out g)) return g;
            }
            return null;
        }

        // ---------------- Nexus Mods' list of games ----------------

        /// <summary>Reads Nexus Mods' list (the copy kept here when it's under 7 days old, else downloads it) on a worker thread, then <paramref name="done"/>.</summary>
        private void LoadNexusGames(Action done)
        {
            lock (nexusLock)
            {
                if (nexusByName == null)
                {
                    if ((DateTime.UtcNow - nexusFailedAt).TotalMinutes < 10) return;   // (a failed download isn't tried again straight away)
                    nexusWaiting = done;   // (only the newest game page needs it: an older one has gone)
                    if (nexusLoading) return;
                    nexusLoading = true;
                }
            }
            if (nexusByName != null) { done(); return; }
            string path = NexusCachePath;
            KippixShared.SafeWork.Queue(_ =>
            {
                string json = null;
                try
                {
                    bool fresh = File.Exists(path) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalDays < 7;
                    if (fresh) json = File.ReadAllText(path);
                    else
                    {
                        try
                        {
                            using (var web = StoreWeb()) json = web.DownloadString(NexusGamesUrl);
                            string head = (json ?? "").TrimStart();
                            if (head.StartsWith("[", StringComparison.Ordinal) || head.StartsWith("{", StringComparison.Ordinal))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(path));
                                string tmp = path + ".tmp";
                                File.WriteAllText(tmp, json);
                                if (File.Exists(path)) File.Delete(path);
                                File.Move(tmp, path);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Info("Kippix: Nexus Mods' list of games couldn't be downloaded: " + ex.Message);
                            if (File.Exists(path)) json = File.ReadAllText(path);   // (an older copy is better than none)
                        }
                    }
                    if (!string.IsNullOrEmpty(json)) ParseNexusGames(json);
                }
                catch (Exception ex) { logger.Info("Kippix: Nexus Mods' list of games couldn't be read: " + ex.Message); }
                bool ok;
                Action then;
                lock (nexusLock)
                {
                    nexusLoading = false;
                    ok = nexusByName != null;
                    if (!ok) nexusFailedAt = DateTime.UtcNow;
                    then = nexusWaiting;
                    nexusWaiting = null;
                }
                if (ok && then != null) then();
            });
        }

        /// <summary>The list's games: name, page, number of mods and downloads (whatever of those it has).</summary>
        private static void ParseNexusGames(string json)
        {
            object root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
            var list = root as IEnumerable;
            var asDict = root as Dictionary<string, object>;
            if (asDict != null) list = (Dig(asDict, "games") ?? Dig(asDict, "data")) as IEnumerable;
            if (list == null || list is string) return;
            var byName = new Dictionary<string, NexusGame>();
            var byDomain = new Dictionary<string, NexusGame>();
            foreach (var d in list.Cast<object>().OfType<Dictionary<string, object>>())
            {
                string name = FStr(d, "name");
                string domain = FStr(d, "domain_name");
                string url = FStr(d, "nexusmods_url");
                if (url.Length == 0 && domain.Length > 0) url = "https://www.nexusmods.com/" + domain;
                if (domain.Length == 0) { var m = NexusDomainRx.Match(url); if (m.Success) domain = m.Groups[1].Value; }
                if (name.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                long mods = FNum(d, "mods");
                if (mods <= 0) mods = FNum(d, "mod_count");
                if (mods <= 0) mods = FNum(d, "mods_count");
                var g = new NexusGame { Name = name, Url = url, Domain = domain.ToLowerInvariant(), Mods = mods, Downloads = FNum(d, "downloads") };
                string key = NormName(name);
                NexusGame have;
                // two games with the same name: the one with more mods
                if (key.Length > 0 && (!byName.TryGetValue(key, out have) || have.Mods < g.Mods)) byName[key] = g;
                if (g.Domain.Length > 0) byDomain[g.Domain] = g;
            }
            if (byName.Count == 0) return;
            lock (nexusLock)
            {
                nexusByDomain = byDomain;
                nexusByName = byName;
            }
        }

        // ---------------- Vortex and Mod Organizer 2 ----------------

        private static string vortexExe, mo2Exe;
        private static bool modManagersLooked;
        public bool ShowModsVortex { get { return !string.IsNullOrEmpty(vortexExe); } }
        public bool ShowModsMo2 { get { return !string.IsNullOrEmpty(mo2Exe); } }

        /// <summary>Looks for Vortex and Mod Organizer 2 once a session (worker thread): who opens nxm:// links, the uninstall list, the usual folders.</summary>
        private void FindModManagers()
        {
            lock (nexusLock)
            {
                if (modManagersLooked) return;
                modManagersLooked = true;
            }
            KippixShared.SafeWork.Queue(_ =>
            {
                string vortex = null, mo2 = null;
                try
                {
                    // the program that opens Nexus Mods' "Mod manager download" links
                    foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                    {
                        string cmd = RegValue(hive, @"Software\Classes\nxm\shell\open\command", null);
                        string exe = ExeOfCommand(cmd);
                        if (exe == null) continue;
                        string file = Path.GetFileName(exe).ToLowerInvariant();
                        if (file == "vortex.exe" && vortex == null) vortex = exe;
                        else if (file == "nxmhandler.exe" || file == "modorganizer.exe")
                        {
                            string mo = Path.Combine(Path.GetDirectoryName(exe) ?? "", "ModOrganizer.exe");
                            if (mo2 == null && File.Exists(mo)) mo2 = mo;
                        }
                    }
                    // the uninstall list
                    foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                        {
                            try
                            {
                                using (var root = RegistryKey.OpenBaseKey(hive, view))
                                using (var un = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                                {
                                    if (un == null) continue;
                                    foreach (string sub in un.GetSubKeyNames())
                                    {
                                        using (var k = un.OpenSubKey(sub))
                                        {
                                            if (k == null) continue;
                                            string dn = Convert.ToString(k.GetValue("DisplayName") ?? "");
                                            string loc = Convert.ToString(k.GetValue("InstallLocation") ?? "").Trim('"', ' ');
                                            if (vortex == null && dn.StartsWith("Vortex", StringComparison.OrdinalIgnoreCase))
                                            {
                                                string v = loc.Length > 0 ? Path.Combine(loc, "Vortex.exe") : null;
                                                if (v != null && File.Exists(v)) vortex = v;
                                            }
                                            if (mo2 == null && dn.StartsWith("Mod Organizer", StringComparison.OrdinalIgnoreCase))
                                            {
                                                string m = loc.Length > 0 ? Path.Combine(loc, "ModOrganizer.exe") : null;
                                                if (m != null && File.Exists(m)) mo2 = m;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception) { }
                        }
                    // the usual folders
                    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (vortex == null)
                        vortex = FirstFile(Path.Combine(pf, @"Black Tree Gaming Ltd\Vortex\Vortex.exe"), Path.Combine(local, @"Programs\Vortex\Vortex.exe"), @"C:\Program Files\Black Tree Gaming Ltd\Vortex\Vortex.exe");
                    if (mo2 == null)
                        mo2 = FirstFile(Path.Combine(pf, @"Mod Organizer 2\ModOrganizer.exe"), Path.Combine(pf, @"Mod.Organizer-2\ModOrganizer.exe"), @"C:\Modding\MO2\ModOrganizer.exe",
                                        @"C:\MO2\ModOrganizer.exe", @"C:\Mod Organizer 2\ModOrganizer.exe", @"C:\Games\Mod Organizer 2\ModOrganizer.exe", Path.Combine(local, @"Programs\Mod Organizer 2\ModOrganizer.exe"));
                }
                catch (Exception ex) { logger.Info("Kippix: looking for mod managers: " + ex.Message); }
                vortexExe = vortex;
                mo2Exe = mo2;
                if (vortex != null || mo2 != null) OnUi(() => { Notify("ShowModsVortex"); Notify("ShowModsMo2"); });
            });
        }

        private static string FirstFile(params string[] paths)
        {
            foreach (var p in paths) { try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p; } catch (Exception) { } }
            return null;
        }

        private static string RegValue(RegistryHive hive, string key, string name)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var k = root.OpenSubKey(key))
                    {
                        object v = k != null ? k.GetValue(name) : null;
                        if (v != null) return Convert.ToString(v);
                    }
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>The program in a shell command ("C:\x\y.exe" -d "%1" → C:\x\y.exe), if it's there.</summary>
        private static string ExeOfCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = cmd.Trim();
            string exe;
            if (cmd.StartsWith("\"", StringComparison.Ordinal)) { int end = cmd.IndexOf('"', 1); exe = end > 1 ? cmd.Substring(1, end - 1) : null; }
            else { int at = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe = at > 0 ? cmd.Substring(0, at + 4) : null; }
            try { return exe != null && File.Exists(exe) ? exe : null; } catch (Exception) { return null; }
        }

        private void StartModManager(string exe, string name)
        {
            if (string.IsNullOrEmpty(exe)) return;
            PlaySound("select");
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
                try { ShowNotice(Loc("LOC_KX_ModsNotice", "MODS"), string.Format(Loc("LOC_KX_ModsOpening", "Opening {0}"), name), Loc("LOC_KX_ModsOpeningSub", "It opens in its own window"), "", null); } catch (Exception) { }
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: " + name + " couldn't be started: " + ex.Message);
                try { ShowNotice(Loc("LOC_KX_ModsNotice", "MODS"), string.Format(Loc("LOC_KX_ModsFailed", "{0} couldn't be started"), name), ex.Message, "", null); } catch (Exception) { }
            }
        }
    }
}
