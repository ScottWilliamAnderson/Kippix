using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace KippixLinks
{
    /// <summary>One saved link. Its address, title and name are secret: they never leave this add-on (see SafeLog).</summary>
    public class LinkItem
    {
        internal LinkStore Owner;
        public string Url { get; internal set; }
        /// <summary>The page title, picked up from the browser window after it opened (null until then).</summary>
        public string Title { get; internal set; }
        /// <summary>The name given to it with Rename (null: none).</summary>
        public string Name { get; internal set; }
        public DateTime AddedUtc { get; internal set; }
        /// <summary>DateTime.MinValue: never opened.</summary>
        public DateTime OpenedUtc { get; internal set; }
        public int Count { get; internal set; }

        /// <summary>A short id that is safe to log: keyed with the store's random key, so it can't be matched to an address.</summary>
        public string Id { get { return Owner != null ? Owner.IdOf(Url) : "00000000"; } }

        public string Site { get { return LinkUrl.Site(Url); } }

        public string Display
        {
            get
            {
                if (!string.IsNullOrEmpty(Name)) return Name;
                if (!string.IsNullOrEmpty(Title)) return Title;
                return Site;
            }
        }

        public string Detail
        {
            get
            {
                string site = Site;
                return (Display != site ? site + " · " : "") + When;
            }
        }

        /// <summary>"not opened yet" / "opened today" / "opened yesterday" / "opened N days ago" (nothing secret in it).</summary>
        public string When
        {
            get
            {
                if (OpenedUtc == DateTime.MinValue) return "not opened yet";
                int days = (DateTime.Now.Date - OpenedUtc.ToLocalTime().Date).Days;
                if (days <= 0) return "opened today";
                if (days == 1) return "opened yesterday";
                return "opened " + days + " days ago";
            }
        }

        internal DateTime SortTime { get { return OpenedUtc > AddedUtc ? OpenedUtc : AddedUtc; } }
    }

    /// <summary>
    /// The saved links, in links.bin, encrypted for this Windows user (DPAPI). Every change is saved at once.
    /// All calls on the UI thread.
    /// </summary>
    public class LinkStore
    {
        public const int MaxItems = 40;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KippixLinks.v1");

        private readonly string folder;
        private readonly string file;
        private readonly List<LinkItem> items = new List<LinkItem>();
        private byte[] key;
        private bool unavailable;
        private DateTime lastStamp = DateTime.MinValue;

        public LinkStore(string dataFolder)
        {
            if (string.IsNullOrEmpty(dataFolder)) throw new ArgumentException("No data folder.");
            folder = dataFolder;
            file = Path.Combine(folder, "links.bin");
            try { Directory.CreateDirectory(folder); } catch (Exception ex) { SafeLog.Warn("store: data folder", ex); }
            Load();
        }

        /// <summary>Most recently opened or added first.</summary>
        public IList<LinkItem> Items { get { return new ReadOnlyCollection<LinkItem>(items); } }

        public LinkItem Find(string url)
        {
            return url == null ? null : items.FirstOrDefault(i => i.Url == url);
        }

        public bool Contains(LinkItem item) { return item != null && items.Contains(item); }

        /// <summary>Adds the address, or brings it back to the top when it's already saved.</summary>
        public LinkItem AddOrTouch(string url, out bool existed)
        {
            string clean;
            if (!LinkUrl.TryParse(url, out clean)) throw new ArgumentException("Not a web address.");
            var item = Find(clean);
            existed = item != null;
            if (item == null)
            {
                item = new LinkItem { Owner = this, Url = clean, OpenedUtc = DateTime.MinValue };
                items.Insert(0, item);
            }
            item.AddedUtc = Now();
            Sort();
            while (items.Count > MaxItems) items.RemoveAt(items.Count - 1);
            Save();
            return item;
        }

        public void Remove(LinkItem item)
        {
            if (item == null || !items.Remove(item)) return;
            Save();
        }

        /// <summary>
        /// Forgets everything, and with a new key, so old ids mean nothing. Also removes any older copy of the list this add-on
        /// still has (a set-aside unreadable file, a left-over temp file), so nothing of the old list stays on disk.
        /// </summary>
        public void Clear()
        {
            items.Clear();
            key = NewKey();
            unavailable = false;   // (the user asked for the old list to go: writing over it is what's wanted now)
            Save();
            if (SaveFailed) DeleteQuietly(file, "store: couldn't delete the old links file");
            DeleteQuietly(file + ".unreadable", "store: couldn't delete the unreadable file");
            DeleteQuietly(file + ".tmp", "store: couldn't delete the temp file");
        }

        private static void DeleteQuietly(string path, string literal)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { SafeLog.Warn(literal, ex); }
        }

        /// <summary>The last save didn't reach the disk (the panel says so; the links are still in memory).</summary>
        public bool SaveFailed { get; private set; }

        /// <summary>links.bin was there but couldn't be read just now (busy, no access): it is left as it is and not saved over.</summary>
        public bool Unavailable { get { return unavailable; } }

        public void MarkOpened(LinkItem item)
        {
            if (!Contains(item)) return;
            item.OpenedUtc = Now();
            item.Count++;
            Sort();
            Save();
        }

        public void SetTitle(LinkItem item, string title)
        {
            if (!Contains(item)) return;
            item.Title = Clip(title, 80);
            Save();
        }

        public void SetName(LinkItem item, string name)
        {
            if (!Contains(item)) return;
            item.Name = Clip(name, 60);
            Save();
        }

        private static string Clip(string s, int max)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Length == 0) return null;
            return s.Length > max ? s.Substring(0, max).TrimEnd() : s;
        }

        /// <summary>Now, always later than any time handed out before (so the newest is first even within one clock tick).</summary>
        private DateTime Now()
        {
            var now = DateTime.UtcNow;
            if (now <= lastStamp) now = lastStamp.AddTicks(1);
            lastStamp = now;
            return now;
        }

        private void Sort()
        {
            // stable: equal times keep their order
            var sorted = items.OrderByDescending(i => i.SortTime).ToList();
            items.Clear();
            items.AddRange(sorted);
        }

        internal string IdOf(string url)
        {
            if (url == null || key == null) return "00000000";
            using (var h = new HMACSHA256(key))
            {
                byte[] d = h.ComputeHash(Encoding.UTF8.GetBytes(url));
                var sb = new StringBuilder(8);
                for (int i = 0; i < 4; i++) sb.Append(d[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static byte[] NewKey()
        {
            var k = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(k);
            return k;
        }

        // ---------------- the file ----------------

        private void Load()
        {
            items.Clear();
            key = null;
            unavailable = false;
            if (!File.Exists(file)) { key = NewKey(); return; }
            // reading the file: a lock (antivirus, backup, sync) or an access problem is not damage. Try a few times, and if it
            // still can't be read, leave it exactly as it is and save nothing over it this session.
            byte[] raw = null;
            for (int attempt = 0; attempt < 4 && raw == null; attempt++)
            {
                try { raw = File.ReadAllBytes(file); }
                catch (Exception ex)
                {
                    if (ex is FileNotFoundException) { key = NewKey(); return; }   // (gone in between: same as missing)
                    if (!(ex is IOException || ex is UnauthorizedAccessException) || attempt == 3)
                    {
                        key = NewKey();
                        unavailable = true;
                        SafeLog.Warn("store: links file couldn't be read now, left as it is", ex);
                        return;
                    }
                    System.Threading.Thread.Sleep(150);
                }
            }
            try
            {
                byte[] plain = ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
                Dictionary<string, object> root;
                try { root = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(Encoding.UTF8.GetString(plain)) as Dictionary<string, object>; }
                finally { Array.Clear(plain, 0, plain.Length); }
                if (root == null || !(root.ContainsKey("v") && root["v"] is int && (int)root["v"] == 1)) throw new InvalidDataException("Wrong version.");
                byte[] k = Convert.FromBase64String(root["key"] as string ?? "");
                if (k.Length != 32) throw new InvalidDataException("Bad key.");
                key = k;
                var list = root.ContainsKey("items") ? root["items"] as IEnumerable : null;
                if (list == null) throw new InvalidDataException("No items.");
                foreach (var o in list)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string url;
                    if (!LinkUrl.TryParse(Str(d, "u"), out url) || Find(url) != null) continue;
                    items.Add(new LinkItem
                    {
                        Owner = this, Url = url, Title = Clip(Str(d, "t"), 80), Name = Clip(Str(d, "n"), 60),
                        AddedUtc = Ticks(d, "a"), OpenedUtc = Ticks(d, "o"), Count = (int)Math.Max(0, Math.Min(int.MaxValue, Num(d, "c")))
                    });
                }
                Sort();
                while (items.Count > MaxItems) items.RemoveAt(items.Count - 1);
                foreach (var i in items) { if (i.SortTime > lastStamp) lastStamp = i.SortTime; }
            }
            catch (Exception ex)
            {
                // can't be read (another Windows user's, damaged, a newer format): put it aside and start empty
                items.Clear();
                key = NewKey();
                SafeLog.Warn("store: links file unreadable, started empty", ex);
                try
                {
                    string aside = file + ".unreadable";
                    if (File.Exists(aside)) File.Delete(aside);
                    File.Move(file, aside);
                }
                catch (Exception ex2)
                {
                    // not moved: then it mustn't be saved over either
                    unavailable = true;
                    SafeLog.Warn("store: couldn't set the unreadable file aside", ex2);
                }
            }
        }

        private static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) ? v as string : null;
        }

        private static long Num(Dictionary<string, object> d, string k)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return 0;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is decimal) return (long)(decimal)v;
            if (v is double) return (long)(double)v;
            return 0;
        }

        private static DateTime Ticks(Dictionary<string, object> d, string k)
        {
            long t = Num(d, k);
            if (t <= 0 || t > DateTime.MaxValue.Ticks) return DateTime.MinValue;
            return new DateTime(t, DateTimeKind.Utc);
        }

        private void Save()
        {
            if (unavailable)
            {
                SaveFailed = true;
                SafeLog.Info("store: not saved (the links file couldn't be read at start)");
                return;
            }
            byte[] plain = null;
            SaveFailed = true;
            try
            {
                var list = new List<object>();
                foreach (var i in items)
                {
                    list.Add(new Dictionary<string, object>
                    {
                        { "u", i.Url }, { "t", i.Title }, { "n", i.Name },
                        { "a", i.AddedUtc.Ticks }, { "o", i.OpenedUtc.Ticks }, { "c", i.Count }
                    });
                }
                var root = new Dictionary<string, object> { { "v", 1 }, { "key", Convert.ToBase64String(key) }, { "items", list } };
                plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(root));
                byte[] sealedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
                AtomicFile.Write(file, sealedBytes);
                SaveFailed = false;
            }
            catch (Exception ex) { SafeLog.Warn("store: save failed", ex); }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }
    }

    /// <summary>Writes name.tmp, then puts it in place in one step (the old file is never half-written).</summary>
    internal static class AtomicFile
    {
        public static void Write(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null, true); return; }
                catch (IOException) { }   // (some file systems can't Replace: delete and move instead)
                catch (UnauthorizedAccessException) { }
                File.Delete(path);
            }
            File.Move(tmp, path);
        }
    }

    /// <summary>The panel's plain settings (settings.json). Not secret, and never anything about the links.</summary>
    public class PanelSettings
    {
        private string file;

        public bool Pinned { get; set; }
        public bool Stay { get; set; }
        /// <summary>The Kippix top bar shortcut ours sits in front of.</summary>
        public string Before { get; set; }
        /// <summary>Pick up a page's title from the browser window after opening it.</summary>
        public bool Titles { get; set; }
        /// <summary>The top bar button opens the latest link straight away (off: it shows the list).</summary>
        public bool Direct { get; set; }
        /// <summary>The top bar button's picture follows the latest link's site (off: always the plain link icon).</summary>
        public bool SiteIcon { get; set; }

        public PanelSettings()
        {
            Pinned = true;
            Stay = false;
            Before = "options";
            Titles = true;
            Direct = true;
            SiteIcon = true;
        }

        public static PanelSettings Load(string dataFolder)
        {
            var s = new PanelSettings();
            try
            {
                s.file = Path.Combine(dataFolder, "settings.json");
                if (!File.Exists(s.file)) return s;
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(s.file, Encoding.UTF8)) as Dictionary<string, object>;
                if (d == null) return s;
                object v;
                if (d.TryGetValue("Pinned", out v) && v is bool) s.Pinned = (bool)v;
                if (d.TryGetValue("Stay", out v) && v is bool) s.Stay = (bool)v;
                if (d.TryGetValue("Titles", out v) && v is bool) s.Titles = (bool)v;
                if (d.TryGetValue("Direct", out v) && v is bool) s.Direct = (bool)v;
                if (d.TryGetValue("SiteIcon", out v) && v is bool) s.SiteIcon = (bool)v;
                if (d.TryGetValue("Before", out v) && v is string && ((string)v).Length <= 64) s.Before = (string)v;
            }
            catch (Exception ex) { SafeLog.Warn("settings: unreadable, defaults used", ex); }
            return s;
        }

        public void Save()
        {
            if (file == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var d = new Dictionary<string, object> { { "Pinned", Pinned }, { "Stay", Stay }, { "Before", Before ?? "" }, { "Titles", Titles }, { "Direct", Direct }, { "SiteIcon", SiteIcon } };
                AtomicFile.Write(file, Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(d)));
            }
            catch (Exception ex) { SafeLog.Warn("settings: save failed", ex); }
        }
    }
}
