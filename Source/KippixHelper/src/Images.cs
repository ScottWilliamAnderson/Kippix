using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KippixHelper
{
    /// <summary>
    /// Small, frozen thumbnails (covers, achievement icons, store pictures) decoded off the UI thread, so lists of
    /// hundreds of items draw and re-sort instantly instead of decoding full-size images every time.
    /// Pictures from the web are also kept on disk (ImageCache, newest 1500), so the Store shows them straight away next time.
    /// </summary>
    internal static class Images
    {
        private static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private const int MaxCache = 3000;
        private const int MaxDiskFiles = 1500;

        /// <summary>Folder for downloaded pictures (set at start-up); null = don't keep them.</summary>
        public static string DiskCacheDir;
        private static int diskWrites;

        /// <summary>Loads (and caches) a picture at the given pixel width. Safe on any thread. Null if it can't be read.</summary>
        public static ImageSource Load(string path, int width)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = width + "|" + path;
            lock (cache)
            {
                ImageSource hit;
                if (cache.TryGetValue(key, out hit)) return hit;
            }
            ImageSource img = Decode(path, width);
            // a download that failed may work next time (busy CDN, brief drop-out): only remember it when it worked
            if (img == null && IsWeb(path)) return null;
            lock (cache)
            {
                if (cache.Count > MaxCache) cache.Clear();
                cache[key] = img;
            }
            return img;
        }

        /// <summary>The picture if it's already decoded at this width (nothing is read); null otherwise.</summary>
        public static ImageSource Peek(string path, int width)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (cache)
            {
                ImageSource hit;
                return cache.TryGetValue(width + "|" + path, out hit) ? hit : null;
            }
        }

        /// <summary>Same, without keeping it in memory (big backgrounds, full screen pictures).</summary>
        public static ImageSource LoadUncached(string path, int width)
        {
            return string.IsNullOrEmpty(path) ? null : Decode(path, width);
        }

        private static bool IsWeb(string path)
        {
            return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static ImageSource Decode(string path, int width)
        {
            try
            {
                byte[] bytes = IsWeb(path) ? WebBytes(path) : (File.Exists(path) ? File.ReadAllBytes(path) : null);
                if (bytes == null || bytes.Length == 0) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = width;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception)
            {
                // a broken file in the disk cache: drop it so it downloads again next time
                try { string f = DiskFile(path); if (f != null && File.Exists(f)) File.Delete(f); } catch (Exception) { }
                return null;
            }
        }

        private static byte[] WebBytes(string url)
        {
            string file = DiskFile(url);
            if (file != null)
            {
                try { if (File.Exists(file)) { File.SetLastWriteTimeUtc(file, DateTime.UtcNow); return File.ReadAllBytes(file); } } catch (Exception) { }
            }
            byte[] bytes;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 Kippix";
                bytes = web.DownloadData(url);
            }
            if (file != null && bytes != null && bytes.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, bytes);
                    if (Interlocked.Increment(ref diskWrites) % 200 == 1) KippixShared.SafeWork.Queue(_ => TrimDisk());
                }
                catch (Exception) { }
            }
            return bytes;
        }

        private static string DiskFile(string url)
        {
            if (string.IsNullOrEmpty(DiskCacheDir) || !IsWeb(url)) return null;
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                string name = sb.ToString();
                return Path.Combine(DiskCacheDir, name.Substring(0, 2), name + ".img");
            }
        }

        /// <summary>Keeps the newest pictures (the ones used longest ago go first).</summary>
        private static void TrimDisk()
        {
            try
            {
                if (string.IsNullOrEmpty(DiskCacheDir) || !Directory.Exists(DiskCacheDir)) return;
                var files = new DirectoryInfo(DiskCacheDir).GetFiles("*.img", SearchOption.AllDirectories);
                if (files.Length <= MaxDiskFiles) return;
                foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxDiskFiles))
                    try { f.Delete(); } catch (Exception) { }
            }
            catch (Exception) { }
        }
    }
}
