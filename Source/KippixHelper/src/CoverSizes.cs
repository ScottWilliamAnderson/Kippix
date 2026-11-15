using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KippixShared;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Each cover's own shape (width / height), for the Mosaic layout: a tall box cover, a wide banner and a square icon each
    /// keep their shape there, so the layout has to know it before the picture is on screen. Only the file's header is read
    /// (never the picture itself), off the UI thread, and only the number is kept: in memory and in cover-sizes.json in the
    /// add-on's data folder, keyed by the cover's database id (a new cover gets a new id, so nothing goes stale).
    /// </summary>
    internal sealed class CoverSizes
    {
        public const float DefaultRatio = 2f / 3f;   // a box cover, until the real shape is known (and for covers that can't be read)

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IPlayniteAPI api;
        private readonly string path;
        private readonly Dispatcher ui;
        private readonly object gate = new object();
        private readonly Dictionary<string, float> ratios = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // asked for this session (queued, read or unreadable)
        private readonly Queue<string> waiting = new Queue<string>();
        private bool working, dirty, loaded, prewarmed;
        private int raiseQueued;

        /// <summary>Some shapes have been learnt since the last call (on the UI thread, at most a few times a second).</summary>
        public event Action Changed;

        public CoverSizes(IPlayniteAPI api, string dataDir, Dispatcher ui)
        {
            this.api = api;
            this.ui = ui;
            path = string.IsNullOrEmpty(dataDir) ? null : Path.Combine(dataDir, "cover-sizes.json");
        }

        public int Count { get { lock (gate) return ratios.Count; } }

        /// <summary>The cover's width / height: the real one when known, a box cover's until then (the real one is then read in the background).</summary>
        public float Get(Game game)
        {
            string id = game != null ? game.CoverImage : null;
            if (string.IsNullOrEmpty(id)) return DefaultRatio;
            bool start = false;
            lock (gate)
            {
                Load();
                float r;
                if (ratios.TryGetValue(id, out r)) return r;
                if (!tried.Add(id)) return DefaultRatio;
                if (IsUrl(id)) return DefaultRatio;   // a cover still on the web: no file to read
                waiting.Enqueue(id);
                if (!working) { working = true; start = true; }
            }
            if (start) SafeWork.Queue(_ => Work());
            return DefaultRatio;
        }

        /// <summary>Once after start: every cover not known yet is read on a worker, and covers no longer in the library are forgotten.</summary>
        public void Prewarm(IEnumerable<Game> games)
        {
            if (games == null) return;
            lock (gate) { if (prewarmed) return; prewarmed = true; }
            // (the ids are taken here, on the caller's thread: the worker never walks Playnite's collection)
            var ids = games.Where(g => g != null && !string.IsNullOrEmpty(g.CoverImage)).Select(g => g.CoverImage).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SafeWork.Queue(_ =>
            {
                var sw = Stopwatch.StartNew();
                int read = 0, failed = 0, dropped = 0;
                var todo = new List<string>();
                lock (gate)
                {
                    Load();
                    foreach (string id in ids)
                        if (!ratios.ContainsKey(id) && !IsUrl(id) && tried.Add(id)) todo.Add(id);
                }
                foreach (string id in todo)
                {
                    float r = Read(id);
                    if (r <= 0) { failed++; continue; }
                    lock (gate) { ratios[id] = r; dirty = true; }
                    read++;
                }
                lock (gate)
                {
                    if (ids.Count > 0)
                    {
                        var keep = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                        foreach (string k in ratios.Keys.Where(k => !keep.Contains(k)).ToList()) { ratios.Remove(k); dropped++; dirty = true; }
                    }
                }
                Save();
                logger.Info("Kippix: mosaic: pre-warm done: " + ids.Count + " covers, " + read + " read, " + failed + " unreadable, " + dropped + " forgotten, " + Count + " known, " + sw.ElapsedMilliseconds + " ms");
                if (read > 0) RaiseSoon();
            });
        }

        private static bool IsUrl(string id)
        {
            return id.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The worker: the covers asked for one at a time; the layout hears about them in batches, and the file is written once the queue is empty.</summary>
        private void Work()
        {
            var since = Stopwatch.StartNew();
            bool news = false;
            try
            {
                while (true)
                {
                    string id;
                    lock (gate)
                    {
                        if (waiting.Count == 0) { working = false; break; }
                        id = waiting.Dequeue();
                    }
                    float r = Read(id);
                    if (r > 0)
                    {
                        lock (gate) { ratios[id] = r; dirty = true; }
                        news = true;
                    }
                    if (news && since.ElapsedMilliseconds >= 400) { RaiseSoon(); news = false; since.Restart(); }
                }
            }
            catch (Exception) { lock (gate) working = false; throw; }
            if (news) RaiseSoon();
            Save();
        }

        /// <summary>Width / height from the file's header; 0 when it can't be read.</summary>
        private float Read(string id)
        {
            try
            {
                string file = api.Database.GetFullFilePath(id);
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return 0;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var f = dec.Frames[0];
                    if (f.PixelWidth <= 0 || f.PixelHeight <= 0) return 0;
                    return (float)f.PixelWidth / f.PixelHeight;
                }
            }
            catch (Exception) { return 0; }
        }

        private void RaiseSoon()
        {
            if (ui == null || System.Threading.Interlocked.Exchange(ref raiseQueued, 1) == 1) return;
            ui.BeginInvoke(new Action(() =>
            {
                raiseQueued = 0;
                var h = Changed;
                if (h == null) return;
                try { h(); } catch (Exception ex) { logger.Warn("Kippix: mosaic: cover sizes: " + ex.Message); }
            }), DispatcherPriority.Background);
        }

        // (call with the gate held)
        private void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (path == null || !File.Exists(path)) return;
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, float>>(File.ReadAllText(path));
                if (d == null) return;
                foreach (var kv in d)
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0 && !float.IsNaN(kv.Value) && !float.IsInfinity(kv.Value)) ratios[kv.Key] = kv.Value;
            }
            catch (Exception ex) { logger.Warn("Kippix: mosaic: couldn't read cover-sizes.json (it's made again): " + ex.Message); }
        }

        private void Save()
        {
            try
            {
                string json;
                lock (gate)
                {
                    if (!dirty || path == null) return;
                    dirty = false;
                    json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, float>(ratios));
                }
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex) { logger.Warn("Kippix: mosaic: couldn't save cover-sizes.json: " + ex.Message); }
        }
    }
}
