using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace KippixHelper
{
    /// <summary>
    /// The top left: your name and picture (from Windows). It opens a side menu of shortcuts: Profile (coming soon),
    /// Achievements, Store, Gallery and Media (Netflix, YouTube, Prime Video, Crunchyroll).
    /// </summary>
    public partial class ThemeApi
    {
        public ObservableCollection<PickRow> ProfileRows { get; private set; }
        public ICommand ToggleProfileCommand { get; private set; }
        public bool IsProfileOpen { get { return openPanel == "profile"; } }
        public bool IsProfileMedia { get { return profilePage != ""; } }   // any page under the menu (Media, Your name and picture, Power)
        public bool IsProfileMediaPage { get { return profilePage == "media"; } }   // the note about how the streaming services open: Media only
        public string ProfileMenuTitle
        {
            get
            {
                switch (profilePage)
                {
                    case "media": return Loc("LOC_KX_Media", "Media");
                    case "me": return Loc("LOC_KX_ProfileSettings", "Your name and picture");
                    case "power": return Loc("LOC_KX_Power", "Power");
                    case "pages": return Loc("LOC_KX_MorePages", "More pages");
                }
                if (profileReorder) return Loc("LOC_KX_ReorderMenu", "Reorder the menu");
                return ProfileName;
            }
        }
        private string profilePage = "";

        private string profileName;
        public string ProfileName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(settings.ProfileNameOverride)) return settings.ProfileNameOverride.Trim();
                if (profileName == null) profileName = WindowsDisplayName();
                return profileName;
            }
        }
        public string ProfileInitial { get { string n = ProfileName; return string.IsNullOrEmpty(n) ? "?" : n.Substring(0, 1).ToUpperInvariant(); } }
        private ImageSource profilePicture;
        private bool profilePictureTried;
        public ImageSource ProfilePicture
        {
            get
            {
                if (!profilePictureTried)
                {
                    profilePictureTried = true;
                    string own = settings.ProfilePicturePath ?? "";
                    // (0.89) the file gone (an older copy of the profile's settings pointed at it): the profile's own picture
                    string alt = null;
                    try { var cp = ProfilesOn ? CurrentProfile : null; if (cp != null && !string.IsNullOrEmpty(cp.PicturePath) && cp.PicturePath != own) alt = cp.PicturePath; } catch (Exception) { }
                    // (r15: no picture of your own: the first letter of your name, not the Windows account picture)
                    if (own.Length > 0 || alt != null) KippixShared.SafeWork.Queue(_ =>
                    {
                        string used = own;
                        ImageSource img = null;
                        try { img = own.Length > 0 && File.Exists(own) ? Images.LoadUncached(own, 160) : null; } catch (Exception) { }
                        if (img == null && alt != null) { try { if (File.Exists(alt)) { img = Images.LoadUncached(alt, 160); used = alt; } } catch (Exception) { } }
                        // The theme can ask for the picture before Start() has set "ui"; OnUi() then drops the result without a word
                        // and, with profilePictureTried already set, the picture never shows until it's picked again. Use the
                        // application's dispatcher in that case, and let the next read try again if there's none at all.
                        var back = ui ?? (System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null);
                        if (back == null) { profilePictureTried = false; return; }
                        back.BeginInvoke((Action)(() =>
                        {
                            if ((settings.ProfilePicturePath ?? "") != own) { profilePictureTried = false; Notify("ProfilePicture"); return; }   // (a profile switch or a new picture meanwhile: this one is out of date, read again)
                            if (img == null) { logger.Info("Kippix: profile picture: couldn't read " + (own.Length > 0 ? own : "(none)") + (alt != null ? " or " + alt : "")); return; }
                            if (used != own) { settings.ProfilePicturePath = used; SaveSettings(); }
                            profilePicture = img;
                            Notify("ProfilePicture");
                            Notify("HasProfilePicture");
                            // the tour's "Choose a picture" row was built while this was still loading: it shows the picture now
                            if (openPanel == "welcome") { var row = WelcomeRows.FirstOrDefault(r => r.Key == "pic"); if (row != null) row.Picture = img; }
                        }));
                    });
                }
                return profilePicture;
            }
        }
        public bool HasProfilePicture { get { return profilePicture != null; } }

        private void InitProfile()
        {
            if (ProfileRows != null) return;
            ProfileRows = new ObservableCollection<PickRow>();
            ToggleProfileCommand = new RelayCommand(_ => ToggleProfile());
        }

        private void ToggleProfile()
        {
            if (openPanel == "profile") { ClosePanels(); return; }
            profilePage = "";
            profileReorder = false;
            profileMovingKey = null;
            BuildProfileRows();
            TogglePanel("profile");
            NotifyProfileHead();
            Notify("IsProfileMedia"); Notify("IsProfileMediaPage");
            Notify("ProfileMenuTitle");
            // the highlight starts on Profile (Refresh game library sits above it, one press up)
            if (openPanel == "profile")
                ui.BeginInvoke(new Action(() =>
                {
                    var row = openPanel == "profile" && profilePage == "" ? ProfileRows.FirstOrDefault(r => r.Key == "profile") : null;
                    if (row != null) UiTools.FocusItem("KX_ProfileList", row);
                }), DispatcherPriority.ContextIdle);
        }

        private void BuildProfileRows()
        {
            ProfileRows.Clear();
            if (profilePage == "media") { BuildMediaRows(); return; }
            if (profilePage == "me") { BuildMeRows(); return; }
            if (profilePage == "power") { BuildPowerRows(); return; }
            if (profilePage == "pages") { BuildPageMenuRows(); return; }
            // (0.99.11) Refresh game library is the round button at the top right now, beside the edit button
            ProfileRows.Add(new PickRow { Text = Loc("LOC_KX_Profile", "Profile"), Sub = Loc("LOC_KX_ProfileHubSub", "Overview, your library, up next, friends, wishlist, collections, your year"), Icon = "profile", Key = "profile", Command = new RelayCommand(_ => OpenProfilePage()) });
            if (ProfilesOn)
                ProfileRows.Add(new PickRow { Text = Loc("LOC_KX_SwitchProfile", "Switch profile"), Sub = ProfilesSummary, Icon = "friends", Key = "switchprofile", Command = new RelayCommand(_ => { PlaySound("select"); ClosePanels(); ui.BeginInvoke(new Action(OpenWho), DispatcherPriority.Loaded); }) });
            ProfileRows.Add(new PickRow { Text = Loc("LOC_KX_Achievements", "Achievements"), Sub = Loc("LOC_KX_AchMenuSub", "Every game's achievements and your progress"), Icon = "trophy", Key = "achievements", Command = new RelayCommand(_ => { ClosePanels(); OpenAchievementsView(); }) });
            ProfileRows.Add(new PickRow { Text = Loc("LOC_KX_Store", "Store"), Sub = Loc("LOC_KX_StoreMenuSub", "Deals, new games and your wishlist"), Icon = "store", Key = "store", Command = new RelayCommand(_ => { ClosePanels(); OpenStore(); }) });
            ProfileRows.Add(new PickRow { Text = Loc("LOC_KX_Gallery", "Gallery"), Sub = Loc("LOC_KX_GalleryMenuSub", "Your screenshots and clips"), Icon = "gallery", Key = "gallery", Command = new RelayCommand(_ => { ClosePanels(); OpenGallery(); }) });
            ProfileRows.Add(new PickRow { Text = Loc("LOC_KX_Media", "Media"), Sub = Loc("LOC_KX_MediaMenuSub", "Your shows & movies, Netflix, YouTube, Prime Video, Crunchyroll"), Icon = "media", Key = "media", Command = new RelayCommand(_ => SetProfilePage("media")) });
            AddProfileExtraRows();
            ApplyProfileOrder();   // (0.99.11) your order (and order mode)
        }

        private void SetProfilePage(string page) { SetProfilePage(page, null); }

        private void SetProfilePage(string page, string backFrom)
        {
            if (backFrom == null) PlaySound("select");
            string from = backFrom ?? profilePage;
            hubMeReturn = false;   // (the profile's Name and picture button sets it again, right after)
            profilePage = page;
            BuildProfileRows();
            NotifyProfileHead();
            Notify("IsProfileMedia"); Notify("IsProfileMediaPage");
            Notify("ProfileMenuTitle");
            ui.BeginInvoke(new Action(() =>
            {
                var back = page == "" ? ProfileRows.FirstOrDefault(r => r.Key == from) : null;
                if (back != null) UiTools.FocusItem("KX_ProfileList", back);
                else UiTools.FocusInside("KX_ProfilePanel");
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>B in the profile menu: Media back to the menu, then close.</summary>
        private bool ProfileBack()
        {
            if (ProfileReorderBack()) return true;   // (0.99.11)
            if (openPanel != "profile" || profilePage == "") return false;
            if (BackFromMeToHub()) return true;   // Name and picture from the profile's Overview: back there
            SetProfilePage("", profilePage);
            return true;
        }

        // ---------------- Media ----------------

        /// <summary>A streaming service: its own Windows app when installed, or its website.</summary>
        private class MediaService
        {
            public string Key, Name, Package, Web;
            public bool InTheme;   // plays in the theme's own browser (no copy protection needed)
        }

        private static readonly MediaService[] MediaServices =
        {
            new MediaService { Key = "netflix", Name = "Netflix", Package = "Netflix", Web = "https://www.netflix.com/" },
            new MediaService { Key = "youtube", Name = "YouTube", Package = null, Web = "https://www.youtube.com/", InTheme = true },
            new MediaService { Key = "prime", Name = "Prime Video", Package = "PrimeVideo", Web = "https://www.primevideo.com/" },
            new MediaService { Key = "crunchyroll", Name = "Crunchyroll", Package = "Crunchyroll", Web = "https://www.crunchyroll.com/" }
        };

        private void BuildMediaRows()
        {
            // (1.0.1) your own videos first (ThemeApi.LocalMedia.cs)
            int n = LocalFolders().Count;
            ProfileRows.Add(new PickRow
            {
                Text = Loc("LOC_KX_LocalMedia", "Shows & movies"), Icon = "localmedia", Key = "localmedia",
                Sub = n == 0 ? Loc("LOC_KX_LocalSub", "Your own videos, from folders on this PC") : n == 1 ? Loc("LOC_KX_LocalOneFolder", "Your own videos, from 1 folder") : string.Format(Loc("LOC_KX_LocalNFolders", "Your own videos, from {0} folders"), n),
                Command = new RelayCommand(_ => { PlaySound("select"); OpenLocalMedia(true); })
            });
            ProfileRows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecStreaming", "STREAMING") });
            foreach (var m in MediaServices)
            {
                var svc = m;
                string app = svc.Package != null ? FindStoreApp(svc.Package) : null;
                // no line under each one: the note at the bottom of the menu says how they open
                var row = new PickRow { Text = svc.Name, Icon = svc.Key, Key = svc.Key, Command = new RelayCommand(_ => OpenMedia(svc, app)) };
                ProfileRows.Add(row);
                LoadMediaIcon(svc, row);
            }
        }

        /// <summary>
        /// Each service's own icon: from its app when it's installed (the logo in the app's package), otherwise the icon its
        /// website gives browsers. Kept in the add-on's folder after the first time. Our line icon shows until (or if) there's none.
        /// </summary>
        private void LoadMediaIcon(MediaService svc, PickRow row)
        {
            LoadMediaIcon(svc, img => row.Picture = img);
        }

        private void LoadMediaIcon(MediaService svc, Action<ImageSource> show)
        {
            string dir = Path.Combine(plugin.GetPluginUserDataPath(), "MediaIcons");
            string cached = Path.Combine(dir, svc.Key + ".png");
            KippixShared.SafeWork.Queue(_ =>
            {
                try
                {
                    string file = null;
                    if (svc.Package != null) file = StoreAppLogo(svc.Package);
                    if (file == null && File.Exists(cached)) file = cached;
                    if (file == null)
                    {
                        Directory.CreateDirectory(dir);
                        string host = new Uri(svc.Web).Host;
                        using (var wc = new System.Net.WebClient())
                        {
                            wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                            wc.DownloadFile("https://www.google.com/s2/favicons?domain=" + Uri.EscapeDataString(host) + "&sz=128", cached);
                        }
                        file = cached;
                    }
                    var img = Images.Load(file, 64);
                    if (img != null) OnUi(() => show(img));
                }
                catch (Exception ex) { logger.Info("Kippix: media icon " + svc.Key + ": " + ex.Message); }
            });
        }

        /// <summary>The square logo inside an installed Microsoft Store app's package (the biggest size it has), or null.</summary>
        private static string StoreAppLogo(string part)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (root == null) return null;
                    foreach (string full in root.GetSubKeyNames())
                    {
                        if (full.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string[] bits = full.Split('_');
                        if (bits.Length < 5 || bits[3].Length > 0) continue;
                        using (var k = root.OpenSubKey(full))
                        {
                            string folder = k != null ? k.GetValue("PackageRootFolder") as string : null;
                            if (string.IsNullOrEmpty(folder)) continue;
                            string manifest = Path.Combine(folder, "AppxManifest.xml");
                            if (!File.Exists(manifest)) continue;
                            string text = File.ReadAllText(manifest);
                            var m = Regex.Match(text, "Square150x150Logo=\"([^\"]+)\"");
                            if (!m.Success) m = Regex.Match(text, "Square44x44Logo=\"([^\"]+)\"");
                            if (!m.Success) continue;
                            string rel = m.Groups[1].Value.Replace('/', '\\');
                            string exact = Path.Combine(folder, rel);
                            string dir = Path.GetDirectoryName(exact), stem = Path.GetFileNameWithoutExtension(exact), ext = Path.GetExtension(exact);
                            if (!Directory.Exists(dir)) continue;
                            // Logo.png is often only there as Logo.scale-200.png / Logo.targetsize-256.png: take the biggest file
                            var best = Directory.GetFiles(dir, stem + "*" + ext).Where(f => f.IndexOf("contrast", StringComparison.OrdinalIgnoreCase) < 0)
                                                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                            if (best != null) return best;
                            if (File.Exists(exact)) return exact;
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>(1.0.1) A top bar / quick bar shortcut to one service: whether its app is installed is looked up as it's pressed.</summary>
        private void OpenMediaByKey(string key)
        {
            var svc = MediaServices.FirstOrDefault(m => m.Key == key);
            if (svc == null) return;
            OpenMedia(svc, svc.Package != null ? FindStoreApp(svc.Package) : null);
        }

        /// <summary>(1.0.1) The Media shortcut: the profile menu, open on Media.</summary>
        private void OpenMediaMenu()
        {
            if (openPanel != "profile") { if (openPanel != null) ClosePanels(); ToggleProfile(); }
            if (openPanel == "profile") SetProfilePage("media");
        }

        private void OpenMedia(MediaService svc, string app)
        {
            PlaySound("select");
            if (svc.InTheme) { ClosePanels(); OpenBrowser(svc.Web); return; }
            ClosePanels();
            try
            {
                if (app != null) Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(svc.Web) { UseShellExecute = true });
            }
            catch (Exception ex) { logger.Warn("Kippix: media: " + ex.Message); }
        }

        /// <summary>An installed Microsoft Store app whose package name contains <paramref name="part"/>: "family!appid" for shell:AppsFolder, or null.</summary>
        private static string FindStoreApp(string part)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                {
                    if (root == null) return null;
                    foreach (string full in root.GetSubKeyNames())
                    {
                        if (full.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        using (var k = root.OpenSubKey(full))
                        {
                            string folder = k != null ? k.GetValue("PackageRootFolder") as string : null;
                            // full name: Name_Version_Arch_ResourceId_PublisherId -> family: Name_PublisherId
                            string[] bits = full.Split('_');
                            if (bits.Length < 5 || bits[3].Length > 0) continue;   // resource packages (…_split.scale-125_…) aren't the app
                            string family = bits[0] + "_" + bits[bits.Length - 1];
                            string appId = null;
                            if (!string.IsNullOrEmpty(folder))
                            {
                                string manifest = Path.Combine(folder, "AppxManifest.xml");
                                if (File.Exists(manifest))
                                {
                                    var m = Regex.Match(File.ReadAllText(manifest), "<Application\\s[^>]*Id=\"([^\"]+)\"");
                                    if (m.Success) appId = m.Groups[1].Value;
                                }
                            }
                            if (appId == null) continue;   // no app in this package
                            return family + "!" + appId;
                        }
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        // ---------------- your name and picture ----------------

        [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserNameEx(int format, StringBuilder name, ref uint size);

        private static string WindowsDisplayName()
        {
            try
            {
                var sb = new StringBuilder(256);
                uint size = 256;
                if (GetUserNameEx(3, sb, ref size) && sb.Length > 0) return sb.ToString();   // NameDisplay (e.g. "Alex")
            }
            catch (Exception) { }
            return Environment.UserName;
        }
        // (r15: the Windows account picture isn't used any more: no picture of your own shows your initial)
    }
}
