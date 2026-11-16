using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Add an app or game (the profile menu's More pages, and the bottom of Edit this game): a Playnite entry of your own
    /// without desktop mode. It uses the More menu's list, by itself.
    ///   1. What: one of the Start menu's shortcuts (everyone's and yours), or a file you browse for (.exe, .lnk, .bat, .cmd, .url).
    ///   2. Its name (from the shortcut; the keyboard changes it) and whether it's an app or a game. An app gets the category
    ///      "Apps" (made if it isn't there) and, when the library has it, the tag "[Ludusavi] Game: no backup".
    ///   3. Add it: a Playnite game with one play action (the file, its arguments and folder), marked installed, its icon taken
    ///      from the file. Then its Edit this game rows, where Download what's missing can look for a cover.
    /// </summary>
    public partial class ThemeApi
    {
        private const string AddAppsCategory = "Apps";
        private const string AddNoBackupTag = "[Ludusavi] Game: no backup";
        private static readonly string[] AddFileTypes = { ".exe", ".lnk", ".bat", ".cmd", ".url" };

        /// <summary>What's being added.</summary>
        private class AddTarget
        {
            public string Name, Path, Arguments, WorkingDir, IconFile, Url;
            public int IconIndex;
            public bool IsApp = true;
        }

        private string gameAddLevel;      // null: not in it; "source": choosing what; "details": its name and kind
        private AddTarget gameAdd;
        private int gameAddGen;

        // ---------------- where it opens from ----------------

        private PickRow GameAddMenuRow(bool fromProfileMenu)
        {
            return new PickRow
            {
                Key = "addgame", Icon = "add", Text = Loc("LOC_KX_AddGame", "Add an app or game"), Sub = Loc("LOC_KX_AddGameSub", "From the Start menu, or a file you choose"),
                Command = new RelayCommand(_ => { if (fromProfileMenu) ClosePanels(); OpenGameAdd(); })
            };
        }

        private void OpenGameAdd()
        {
            if (openPanel != "options") TogglePanel("options");
            if (openPanel != "options") return;   // (the profile's PIN was asked for instead)
            PlaySound("select");
            if (collectionsShown) { collectionsShown = false; Notify("GameCollectionsShown"); Notify("GameMoreListShown"); }
            moreSnapshot = null;
            gameEditLevel = null;
            gameAdd = null;
            GameMoreTitle = Loc("LOC_KX_AddGame", "Add an app or game");
            Notify("GameMoreTitle");
            ShowGameAddSources();
            SetOptionsPage("gamemore");
        }

        /// <summary>B: from the name and kind back to the list of what to add (from there, B closes it like the More menu).</summary>
        private bool GameAddBack()
        {
            if (openPanel != "options" || optionsPage != "gamemore" || gameAddLevel == null) return false;
            if (gameAddLevel != "details") { gameAddLevel = null; gameAddGen++; return false; }
            PlaySound("back");
            ShowGameAddSources();
            return true;
        }

        // ---------------- 1: what to add ----------------

        private void ShowGameAddSources()
        {
            gameAddLevel = "source";
            int gen = ++gameAddGen;
            var fresh = new List<PickRow>();
            fresh.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddHeader", "WHAT TO ADD") });
            var browse = new PickRow
            {
                Key = "browse", Icon = "folder", Text = Loc("LOC_KX_AddBrowse", "Browse for a file…"), Sub = string.Join("  ", AddFileTypes),
                Command = new RelayCommand(_ => OpenPicker(Loc("LOC_KX_AddBrowseTitle", "The program to add"), false, AddFileTypes, "", "gamemore", path =>
                {
                    SetOptionsPage("gamemore", false);
                    var t = ResolveAddTarget(path);
                    if (t != null) ShowGameAddDetails(t);
                    else FocusListItemSure("KX_GameMoreList", GameEditRow("browse"));
                }, () => { SetOptionsPage("gamemore", false); FocusListItemSure("KX_GameMoreList", GameEditRow("browse")); }))
            };
            fresh.Add(browse);
            fresh.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddStartMenu", "FROM THE START MENU") });
            fresh.Add(new PickRow { Key = "looking", IsInfo = true, Icon = "info", Text = Loc("LOC_KX_AddLooking", "Looking…") });
            MorphMoreRows(fresh, browse);

            // the shortcuts are read on a thread of their own (the shell's shortcut reader wants one)
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var g in api.Database.Games) if (g != null && g.Name != null) names.Add(g.Name); } catch (Exception) { }
            var th = new Thread(() =>
            {
                List<AddTarget> found;
                try { found = StartMenuTargets(); }
                catch (Exception ex) { logger.Warn("Kippix: add: Start menu: " + ex.Message); found = new List<AddTarget>(); }
                OnUi(() =>
                {
                    if (gen != gameAddGen || gameAddLevel != "source") return;
                    var looking = GameEditRow("looking");
                    if (looking == null) return;
                    if (found.Count == 0) { looking.SetText(Loc("LOC_KX_AddNoneFound", "No shortcuts were found")); return; }
                    GameMoreRows.Remove(looking);   // (a note: never the highlighted row)
                    foreach (var t in found)
                    {
                        var target = t;
                        GameMoreRows.Add(new PickRow
                        {
                            Key = "lnk:" + t.Name, Icon = "file", Text = t.Name,
                            Sub = names.Contains(t.Name) ? Loc("LOC_KX_AddAlready", "Your library already has one with this name") : t.Path,
                            Command = new RelayCommand(_ => ShowGameAddDetails(target))
                        });
                    }
                });
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        /// <summary>The Start menu's program shortcuts (everyone's, then yours), by name; no uninstallers, help files or links to websites.</summary>
        private List<AddTarget> StartMenuTargets()
        {
            var byName = new Dictionary<string, AddTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                IEnumerable<string> files;
                try { files = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories); } catch (Exception) { continue; }
                foreach (string f in files)
                {
                    var t = ResolveShortcut(f);
                    if (t == null || string.IsNullOrEmpty(t.Path) || !t.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(t.Path)) continue;
                    string n = t.Name.ToLowerInvariant();
                    if (n.Contains("uninstall") || n.Contains("readme") || n.StartsWith("remove ")) continue;
                    byName[t.Name] = t;
                }
            }
            return byName.Values.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>A chosen file as something to add (a shortcut is followed to what it starts). Null: it can't be read.</summary>
        private AddTarget ResolveAddTarget(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".lnk")
                {
                    AddTarget t = null;
                    var th = new Thread(() => t = ResolveShortcut(path));
                    th.SetApartmentState(ApartmentState.STA);
                    th.Start();
                    th.Join(5000);
                    if (t != null && !string.IsNullOrEmpty(t.Path)) return t;
                    return null;
                }
                var plain = new AddTarget { Name = Path.GetFileNameWithoutExtension(path), Path = path, WorkingDir = Path.GetDirectoryName(path), IconFile = path };
                if (ext == ".url")
                {
                    string url = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)).Select(l => l.Substring(4)).FirstOrDefault();
                    if (string.IsNullOrEmpty(url)) return null;
                    plain.Url = url;
                    plain.IconFile = null;
                }
                return plain;
            }
            catch (Exception ex) { logger.Warn("Kippix: add: " + ex.Message); return null; }
        }

        /// <summary>Reads a .lnk through the shell (on an STA thread): what it starts, with what, where, and its icon.</summary>
        private static AddTarget ResolveShortcut(string lnk)
        {
            object shell = null, sc = null;
            try
            {
                var st = Type.GetTypeFromProgID("WScript.Shell");
                if (st == null) return null;
                shell = Activator.CreateInstance(st);
                sc = st.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                if (sc == null) return null;
                Func<string, string> get = name => Convert.ToString(sc.GetType().InvokeMember(name, BindingFlags.GetProperty, null, sc, null)) ?? "";
                var t = new AddTarget { Name = Path.GetFileNameWithoutExtension(lnk), Path = Environment.ExpandEnvironmentVariables(get("TargetPath")), Arguments = get("Arguments"), WorkingDir = Environment.ExpandEnvironmentVariables(get("WorkingDirectory")) };
                string icon = get("IconLocation");   // "C:\...\app.exe,0" (",0" alone: the target's own)
                int comma = icon.LastIndexOf(',');
                string iconFile = Environment.ExpandEnvironmentVariables(comma > 0 ? icon.Substring(0, comma) : comma == 0 ? "" : icon).Trim();
                int index = 0;
                if (comma >= 0) int.TryParse(icon.Substring(comma + 1).Trim(), out index);
                t.IconFile = iconFile.Length > 0 && File.Exists(iconFile) ? iconFile : t.Path;
                t.IconIndex = iconFile.Length > 0 ? index : 0;
                if (string.IsNullOrEmpty(t.WorkingDir) && !string.IsNullOrEmpty(t.Path)) t.WorkingDir = Path.GetDirectoryName(t.Path);
                return t;
            }
            catch (Exception) { return null; }
            finally
            {
                try { if (sc != null) Marshal.ReleaseComObject(sc); } catch (Exception) { }
                try { if (shell != null) Marshal.ReleaseComObject(shell); } catch (Exception) { }
            }
        }

        // ---------------- 2: its name and kind; 3: add it ----------------

        private string GameAddKindText() { return gameAdd != null && gameAdd.IsApp ? Loc("LOC_KX_AddKindApp", "App") : Loc("LOC_KX_AddKindGame", "Game"); }

        private string GameAddKindSub()
        {
            return gameAdd != null && gameAdd.IsApp ? string.Format(Loc("LOC_KX_AddKindAppSub", "Gets the category “{0}”"), AddAppsCategory) : Loc("LOC_KX_AddKindGameSub", "No category: it sits with your games");
        }

        private void ShowGameAddDetails(AddTarget t)
        {
            gameAdd = t;
            gameAddLevel = "details";
            gameAddGen++;
            var fresh = new List<PickRow>();
            fresh.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_AddDetailsHeader", "ADD THIS") });
            fresh.Add(new PickRow
            {
                Key = "addname", Icon = "keyboard", Text = Loc("LOC_KX_EditName", "Name"), Sub = t.Name,
                Command = new RelayCommand(_ => OpenKeyboard(Loc("LOC_KX_AddNameAsk", "Its name in your library"), gameAdd != null ? gameAdd.Name : "", text =>
                {
                    text = (text ?? "").Trim();
                    if (gameAdd == null || text.Length == 0) return;
                    gameAdd.Name = text;
                    GameEditSub("addname", text);
                }, null, null, 200))
            });
            fresh.Add(new PickRow
            {
                Key = "addkind", Icon = "tag", Text = string.Format(Loc("LOC_KX_AddKind", "Add as: {0}"), GameAddKindText()), Sub = GameAddKindSub(),
                Command = new RelayCommand(_ =>
                {
                    if (gameAdd == null || !GameEditPressOnce()) return;
                    gameAdd.IsApp = !gameAdd.IsApp;
                    PlaySound("move");
                    var r = GameEditRow("addkind");
                    if (r != null) { r.SetText(string.Format(Loc("LOC_KX_AddKind", "Add as: {0}"), GameAddKindText())); r.SetSub(GameAddKindSub()); }
                })
            });
            fresh.Add(new PickRow { IsInfo = true, Icon = "file", Text = t.Url ?? t.Path, Sub = string.IsNullOrEmpty(t.Arguments) ? null : t.Arguments });
            var add = new PickRow { Key = "addnow", Icon = "check", Text = Loc("LOC_KX_AddNow", "Add it to the library"), Command = new RelayCommand(_ => GameAddNow()) };
            fresh.Add(add);
            MorphMoreRows(fresh, add);
        }

        private void GameAddNow()
        {
            var t = gameAdd;
            if (t == null || !GameEditPressOnce()) return;
            Game g;
            try
            {
                string dir = "";
                try { if (t.Url == null) dir = Path.GetDirectoryName(t.Path) ?? ""; } catch (Exception) { }
                var action = t.Url != null ? new GameAction { Type = GameActionType.URL, Path = t.Url, Name = t.Name, IsPlayAction = true }
                                           : new GameAction { Type = GameActionType.File, Path = t.Path, Arguments = t.Arguments ?? "", WorkingDir = string.IsNullOrEmpty(t.WorkingDir) ? dir : t.WorkingDir, Name = t.Name, IsPlayAction = true };
                g = new Game(t.Name) { IsInstalled = true, InstallDirectory = dir, Added = DateTime.Now, GameActions = new ObservableCollection<GameAction> { action } };
                if (t.IsApp)
                {
                    var cat = api.Database.Categories.Add(AddAppsCategory);   // (the one that's there, or a new one)
                    if (cat != null) g.CategoryIds = new List<Guid> { cat.Id };
                    var tag = api.Database.Tags.FirstOrDefault(x => x != null && string.Equals(x.Name, AddNoBackupTag, StringComparison.OrdinalIgnoreCase));
                    if (tag != null) g.TagIds = new List<Guid> { tag.Id };
                }
                api.Database.Games.Add(g);
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: add: " + ex);
                GameEditSub("addnow", string.Format(Loc("LOC_KX_EditMetaFailed", "It didn't work: {0}"), ex.Message));
                return;
            }
            // its icon, from the file (a failure here leaves it without one)
            string png = null;
            try
            {
                png = SaveFileIcon(t.IconFile, t.IconIndex);
                string id = png != null ? api.Database.AddFile(png, g.Id) : null;
                if (!string.IsNullOrEmpty(id)) { g.Icon = id; api.Database.Games.Update(g); }
            }
            catch (Exception ex) { logger.Warn("Kippix: add: icon: " + ex.Message); }
            finally { try { if (png != null) File.Delete(png); } catch (Exception) { } }
            logger.Info("Kippix: added " + g.Name + " (" + (t.Url ?? t.Path) + ")");
            PlaySound("select");
            // on to its edit rows (B closes): the name, lists and Download what's missing are all there
            gameAdd = null;
            gameAddLevel = null;
            GameMoreTitle = g.Name;
            Notify("GameMoreTitle");
            ShowGameEdit(g, false, "metafill");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, int flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        /// <summary>The file's icon as a PNG in the temp folder (256 pixels when it has one that big). Null: none.</summary>
        private static string SaveFileIcon(string file, int index)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            string png = Path.Combine(Path.GetTempPath(), "kippix_" + Guid.NewGuid().ToString("N") + ".png");
            var handles = new IntPtr[1];
            try
            {
                int n = PrivateExtractIcons(file, index, 256, 256, handles, new int[1], 1, 0);
                if (n > 0 && handles[0] != IntPtr.Zero)
                {
                    using (var icon = System.Drawing.Icon.FromHandle(handles[0]))
                    using (var bmp = icon.ToBitmap())
                        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    return png;
                }
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(file))
                {
                    if (icon == null) return null;
                    using (var bmp = icon.ToBitmap()) bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                }
                return png;
            }
            finally { try { if (handles[0] != IntPtr.Zero) DestroyIcon(handles[0]); } catch (Exception) { } }
        }
    }
}
