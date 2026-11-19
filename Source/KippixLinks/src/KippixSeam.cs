using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media;
using Playnite.SDK;

namespace KippixLinks
{
    /// <summary>
    /// Everything this add-on uses of Kippix Helper, found by reflection (we never build against Kippix, so a Kippix update
    /// can't break the build, and a missing piece only turns that feature off). Every call is wrapped.
    /// Names checked in the 1.0.5 source and in the 1.0.6 release's metadata: ThemeApi.Shortcuts, ShortcutItem,
    /// ToggleMove, OpenMediaAsSet / OpenFullScreenBrowser + MediaService, PlaySound.
    /// Only the loopback hop address is ever handed to Kippix (never the saved address, its title or name).
    /// </summary>
    internal class KippixSeam
    {
        private static readonly Guid HelperId = new Guid("3f6c2d1e-8a47-4b9e-b1d5-7e2a9c4f0b18");
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly IPlayniteAPI api;
        private object theme;
        private Type themeType;
        private Type shortcutType;
        private readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        private readonly HashSet<string> missingLogged = new HashSet<string>();

        public KippixSeam(IPlayniteAPI api) { this.api = api; }

        /// <summary>Kippix Helper is loaded and its ThemeApi is there.</summary>
        public bool Available { get { return Resolve(); } }

        /// <summary>Finds Kippix Helper's ThemeApi (again until it's there: it may not be at the very start).</summary>
        public bool Resolve()
        {
            if (theme != null) return true;
            try
            {
                var plugin = api.Addons.Plugins.FirstOrDefault(p => p.Id == HelperId);
                if (plugin == null) return false;
                var prop = plugin.GetType().GetProperty("Theme", BindingFlags.Instance | BindingFlags.Public);
                object t = prop != null ? prop.GetValue(plugin, null) : null;
                if (t == null) { Missing("Theme"); return false; }
                themeType = t.GetType();
                shortcutType = themeType.Assembly.GetType("KippixHelper.ShortcutItem");
                if (shortcutType == null) Missing("ShortcutItem");
                theme = t;
                return true;
            }
            catch (Exception ex) { SafeLog.Warn("Kippix seam: resolve failed", ex); return false; }
        }

        private void Missing(string what)
        {
            if (!missingLogged.Add(what)) return;
            // fixed names only (the list below is every literal that can reach the log)
            switch (what)
            {
                case "Theme": SafeLog.Info("Kippix seam missing: Theme"); break;
                case "ShortcutItem": SafeLog.Info("Kippix seam missing: ShortcutItem"); break;
                case "Shortcuts": SafeLog.Info("Kippix seam missing: Shortcuts"); break;
                case "OpenMediaAsSet": SafeLog.Info("Kippix seam missing: OpenMediaAsSet"); break;
                case "OpenFullScreenBrowser": SafeLog.Info("Kippix seam missing: OpenFullScreenBrowser"); break;
                case "MediaService": SafeLog.Info("Kippix seam missing: MediaService"); break;
                case "ToggleMove": SafeLog.Info("Kippix seam missing: ToggleMove"); break;
                case "PlaySound": SafeLog.Info("Kippix seam missing: PlaySound"); break;
                default: SafeLog.Info("Kippix seam missing"); break;
            }
        }

        private MethodInfo Method(string name, Func<MethodInfo, bool> match)
        {
            MethodInfo m;
            if (methods.TryGetValue(name, out m)) return m;
            m = null;
            if (themeType != null)
            {
                try { m = themeType.GetMethods(All).FirstOrDefault(x => x.Name == name && (match == null || match(x))); }
                catch (Exception) { m = null; }
            }
            if (m == null) Missing(name);
            methods[name] = m;
            return m;
        }

        // ---------------- the top bar's shortcuts ----------------

        /// <summary>ThemeApi.Shortcuts (an ObservableCollection of ShortcutItem), or null.</summary>
        public IList Shortcuts
        {
            get
            {
                if (!Resolve()) return null;
                try
                {
                    var p = themeType.GetProperty("Shortcuts", All);
                    var list = p != null ? p.GetValue(theme, null) as IList : null;
                    if (list == null || !(list is INotifyCollectionChanged)) { if (p == null) Missing("Shortcuts"); return null; }
                    return list;
                }
                catch (Exception ex) { SafeLog.Warn("Kippix seam: Shortcuts", ex); return null; }
            }
        }

        /// <summary>A new KippixHelper.ShortcutItem, or null.</summary>
        public object NewShortcut(string key, string name, ICommand command, ICommand moveCommand, ImageSource picture)
        {
            if (!Resolve() || shortcutType == null) return null;
            try
            {
                object item = Activator.CreateInstance(shortcutType);
                SetProp(item, "Key", key);
                SetProp(item, "Name", name);
                SetProp(item, "Command", command);
                SetProp(item, "MoveCommand", moveCommand);
                if (picture != null) SetProp(item, "Picture", picture);
                return item;
            }
            catch (Exception ex) { SafeLog.Warn("Kippix seam: new shortcut", ex); return null; }
        }

        public static object GetProp(object o, string name)
        {
            if (o == null) return null;
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                return p != null && p.CanRead ? p.GetValue(o, null) : null;
            }
            catch (Exception) { return null; }
        }

        public static bool SetProp(object o, string name, object value)
        {
            if (o == null) return false;
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (p == null || !p.CanWrite || p.GetSetMethod() == null) return false;
                p.SetValue(o, value, null);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool GetBool(object o, string name)
        {
            object v = GetProp(o, name);
            return v is bool && (bool)v;
        }

        /// <summary>Kippix's reorder mode for our shortcut (Settings › Top bar › Shortcuts).</summary>
        public bool ToggleMove(object item)
        {
            if (item == null || !Resolve()) return false;
            var m = Method("ToggleMove", x => x.GetParameters().Length == 3);
            var list = Shortcuts;
            if (m == null || list == null) return false;
            try { m.Invoke(theme, new object[] { item, list, "KX_ShortcutList" }); return true; }
            catch (Exception ex) { SafeLog.Warn("Kippix seam: ToggleMove failed", ex); return false; }
        }

        // ---------------- opening the hop address ----------------

        /// <summary>
        /// Opens the loopback hop address the way Kippix opens a streaming service (Media › Opens in: full screen in the
        /// browser with the controller driving it, or a browser window). Refuses anything but a 127.0.0.1 address.
        /// </summary>
        public bool OpenMedia(string hopUrl)
        {
            if (hopUrl == null || !hopUrl.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) return false;
            if (!Resolve()) return false;
            try
            {
                var svcType = themeType.GetNestedType("MediaService", BindingFlags.NonPublic | BindingFlags.Public);
                if (svcType == null) { Missing("MediaService"); return false; }
                object svc = Activator.CreateInstance(svcType, true);
                SetField(svc, "Key", "link");
                SetField(svc, "Name", "Link");   // (the only thing Kippix logs about it)
                SetField(svc, "Package", null);
                SetField(svc, "Web", hopUrl);
                var asSet = Method("OpenMediaAsSet", x => { var p = x.GetParameters(); return p.Length == 2 && p[0].ParameterType == svcType; });
                if (asSet != null)
                {
                    object r = asSet.Invoke(theme, new object[] { svc, null });
                    if (r is bool && (bool)r) return true;
                }
                var full = Method("OpenFullScreenBrowser", x => { var p = x.GetParameters(); return p.Length == 1 && p[0].ParameterType == svcType; });
                if (full == null) return false;
                full.Invoke(theme, new object[] { svc });
                return true;
            }
            catch (Exception ex) { SafeLog.Warn("Kippix seam: OpenMedia failed", ex); return false; }
        }

        /// <summary>
        /// Kippix is following running games and apps right now: once a second it looks at the window in front and, the first
        /// time it sees one, writes that window's title to Playnite's log (Running.LearnForeground / Remember, 1.0.5 and
        /// 1.0.6). True when that can't be told, false without Kippix.
        /// </summary>
        public bool WatchingWindows()
        {
            if (!Resolve()) return false;
            try
            {
                var t = themeType.Assembly.GetType("KippixHelper.Running");
                if (t != null)
                {
                    const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    var p = t.GetProperty("Any", Statics);
                    if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(null, null);
                }
            }
            catch (Exception) { }
            return true;
        }

        /// <summary>
        /// Kippix has looked at this window: it is a running entry's window, or the one it last set aside as nobody's
        /// (Running.Snapshot() / RunningEntry.Window / nobodysWindow, 1.0.5 and 1.0.6). Null when that can't be told.
        /// Called off the UI thread (Snapshot takes Kippix's own lock).
        /// </summary>
        public bool? HasLookedAt(IntPtr h)
        {
            if (!Resolve()) return null;
            try
            {
                var t = themeType.Assembly.GetType("KippixHelper.Running");
                if (t == null) return null;
                const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var nobodys = t.GetField("nobodysWindow", Statics);
                var snapshot = t.GetMethod("Snapshot", Statics, null, Type.EmptyTypes, null);
                if (nobodys == null || snapshot == null) return null;
                object nw = nobodys.GetValue(null);
                if (nw is IntPtr && (IntPtr)nw == h) return true;
                var list = snapshot.Invoke(null, null) as IEnumerable;
                if (list == null) return null;
                foreach (object e in list)
                {
                    if (e == null) continue;
                    var wf = e.GetType().GetField("Window", All);
                    if (wf == null) return null;
                    object w = wf.GetValue(e);
                    if (w is IntPtr && (IntPtr)w == h) return true;
                }
                return false;
            }
            catch (Exception) { return null; }
        }

        private static void SetField(object o, string name, object value)
        {
            var f = o.GetType().GetField(name, All);
            if (f != null) f.SetValue(o, value);
        }

        // ---------------- keyboard and sounds ----------------

        // (Kippix's OpenKeyboard is deliberately not used: it draws the text in Playnite's main window, which can be
        // captured, and keeps it after closing. Rename has its own keyboard inside the private panel window.)

        /// <summary>Kippix's own sounds: "move", "select", "back" (its sound setting decides if they play).</summary>
        public void Sound(string kind)
        {
            if (!Resolve()) return;
            var m = Method("PlaySound", x => { var p = x.GetParameters(); return p.Length == 1 && p[0].ParameterType == typeof(string); });
            if (m == null) return;
            try { m.Invoke(theme, new object[] { kind }); }
            catch (Exception ex) { SafeLog.Warn("Kippix seam: PlaySound failed", ex); }
        }
    }
}
