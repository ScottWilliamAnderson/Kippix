using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;

namespace KippixHelper
{
    /// <summary>A widget file (Documents\Kippix Layouts\Widgets\*.json): one widget's look and what it shows, to share.</summary>
    public class WidgetFile
    {
        public string Format = "Kippix widget";
        public int Version = 1;
        public WidgetConfig Widget;
    }

    /// <summary>
    /// Community widgets (0.78), made without code, behind one switch (Community › Yours, off until you turn it on):
    ///   - Web snippet: a picture of a web page (Playnite's own Chromium, made off screen, pictured, then closed again; an
    ///     address that is a picture is just downloaded). Taken again every N minutes, only while the widget is on screen.
    ///   - News feed: an RSS or Atom feed's headlines; A on one opens it in Kippix's browser.
    ///   - Data widget: an address that gives JSON, shown through a line of your own with {fields} by dot path.
    /// Nothing runs from what comes back: feeds and JSON are only read (no scripts, no DTDs, no outside entities, at most
    /// 2 MB, 12 s); a web snippet's page runs inside Chromium only, can't open windows and is closed once it's pictured.
    /// Network work happens off the UI thread, and only while the widget is showing. Widgets can be saved as a widget file
    /// and added from one; they also go with layouts (they're widgets).
    /// </summary>
    public partial class ThemeApi
    {
        private const int CommunityMaxBytes = 2 * 1024 * 1024;
        private const int CommunityTimeoutMs = 12000;

        public bool CommunityWidgetsOn
        {
            get { return settings.CommunityWidgetsOn; }
            set
            {
                if (settings.CommunityWidgetsOn == value) return;
                settings.CommunityWidgetsOn = value;
                SaveSettings();
                Notify("CommunityWidgetsOn");
                if (Widgets != null)
                    foreach (var w in Widgets.Where(x => IsCommunityType(x.Type)).ToList()) RefreshWidget(w);
                if (!value) StopSnapshot();
            }
        }

        private static string WidgetFilesFolder { get { return Path.Combine(LayoutsFolder, "Widgets"); } }

        /// <summary>The widget may go online now: community widgets on, and it's on screen (home showing, Playnite in front, no game).</summary>
        private bool CommunityMayFetch(WidgetItem w)
        {
            if (!settings.CommunityWidgetsOn || !settings.WidgetsOn || w == null) return false;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null || !win.IsActive || gameRunningNow || detailsOpen) return false;
            if (openPanel != null && openPanel != "master" && openPanel != "options") return false;
            return w.ItemVisibility == Visibility.Visible && Widgets.Contains(w);
        }

        private void RefreshCommunityWidget(WidgetItem w, int gen)
        {
            var c = w.Config;
            var d = c.Custom;
            if (!settings.CommunityWidgetsOn)
            {
                w.Big = ""; w.Lines = new List<WidgetLine>(); w.Photo = null;
                w.Status = Loc("LOC_KX_CommunityOffStatus3", "Community widgets are off (Community › Yours)");
                return;
            }
            string url = SafeUrl(d.Url);
            if (url == null) { w.Status = Loc("LOC_KX_WgNoAddress", "No address yet: set it on its page (Settings › Home settings › Widgets)"); return; }
            if (!CommunityMayFetch(w)) { w.RefreshedAt = DateTime.MinValue; return; }   // (not on screen: tried again when it is)
            if (string.IsNullOrEmpty(w.Big) && w.Lines.Count == 0 && w.Photo == null) w.Status = Loc("LOC_KX_WgLoading", "Loading…");
            switch (c.Type)
            {
                case "feed":
                    {
                        int items = d.Items;
                        KippixShared.SafeWork.Queue(_ =>
                        {
                            List<Tuple<string, string, string>> heads = null;
                            string error = null;
                            try { heads = ReadFeed(url, items); } catch (Exception ex) { error = ex.Message; }
                            OnUi(() =>
                            {
                                if (gen != w.Gen) return;
                                if (heads == null) { w.Status = Loc("LOC_KX_WgFeedFailed", "Couldn't read that feed just now"); logger.Info("Kippix: news feed widget: " + error); return; }
                                if (WidgetHasFocus(w) && w.Lines.Count > 0) { w.RefreshedAt = DateTime.Now - CustomMaxAge(c) + TimeSpan.FromMinutes(1); return; }   // (you're on it: in a minute)
                                w.Lines = heads.Select(h =>
                                {
                                    string link = h.Item3;
                                    return new WidgetLine { Label = h.Item1, Value = h.Item2, Bar = -1, Command = link != null && w.IsInteractive ? new RelayCommand(x => { PlaySound("select"); OpenLink(link); }) : null };
                                }).ToList();
                                w.Status = heads.Count == 0 ? Loc("LOC_KX_WgFeedEmpty", "No headlines in that feed") : "";
                            });
                        });
                        return;
                    }
                case "data":
                    {
                        string template = string.IsNullOrWhiteSpace(d.Template) ? "{value}" : d.Template;
                        KippixShared.SafeWork.Queue(_ =>
                        {
                            string text = null, error = null;
                            try { text = DataText(url, template); } catch (Exception ex) { error = ex.Message; }
                            OnUi(() =>
                            {
                                if (gen != w.Gen) return;
                                if (text == null) { w.Status = Loc("LOC_KX_WgDataFailed", "Couldn't read that address just now (it has to give JSON)"); logger.Info("Kippix: data widget: " + error); return; }
                                w.Big = text;
                                w.Status = "";
                            });
                        });
                        return;
                    }
                case "web":
                    if (LooksLikePicture(url))
                    {
                        int width = (int)Math.Max(320, Math.Min(2400, w.DesignWidth * 1.5));
                        KippixShared.SafeWork.Queue(_ =>
                        {
                            ImageSource img = null;
                            try { img = DecodeBytes(Fetch(url, 12 * 1024 * 1024), width); } catch (Exception) { }
                            OnUi(() => { if (gen != w.Gen) return; if (img != null) w.Photo = img; w.Status = img != null ? "" : Loc("LOC_KX_WgWebFailed", "Couldn't show that page just now"); });
                        });
                        return;
                    }
                    QueueSnapshot(w, gen, url);
                    return;
            }
        }

        private static bool LooksLikePicture(string url)
        {
            try
            {
                string ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".webp";
            }
            catch (Exception) { return false; }
        }

        // ---------------- fetching (off the UI thread) ----------------

        /// <summary>A GET with a time limit and a size limit (more than <paramref name="max"/> bytes: an error). Worker threads only.</summary>
        private static byte[] Fetch(string url, int max)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception) { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = CommunityTimeoutMs;
            req.ReadWriteTimeout = CommunityTimeoutMs;
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 5;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.UserAgent = "Kippix (Playnite add-on)";
            req.Accept = "application/json, application/rss+xml, application/atom+xml, application/xml, text/xml, image/*, */*;q=0.5";
            var deadline = DateTime.UtcNow.AddMilliseconds(CommunityTimeoutMs * 1.5);
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.ResponseUri != null && resp.ResponseUri.Scheme != Uri.UriSchemeHttp && resp.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("not a web address");
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[16384];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > max) throw new InvalidDataException("too big");
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("too slow");
                    }
                    return ms.ToArray();
                }
            }
        }

        private static ImageSource DecodeBytes(byte[] bytes, int width)
        {
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

        private static string PlainText(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, "<[^>]*>", " ");   // (titles sometimes carry HTML: only the words are kept)
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > max ? s.Substring(0, max - 1) + "…" : s;
        }

        /// <summary>RSS or Atom: (headline, line under it, link). Read only: no DTDs, no outside entities.</summary>
        private List<Tuple<string, string, string>> ReadFeed(string url, int count)
        {
            byte[] bytes = Fetch(url, CommunityMaxBytes);
            var xs = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024, IgnoreComments = true, IgnoreProcessingInstructions = true };
            var doc = new XmlDocument { XmlResolver = null };
            using (var r = XmlReader.Create(new MemoryStream(bytes), xs)) doc.Load(r);
            string feedTitle = "";
            var ft = doc.SelectSingleNode("/*[local-name()='rss']/*[local-name()='channel']/*[local-name()='title'] | /*[local-name()='feed']/*[local-name()='title'] | /*[local-name()='RDF']/*[local-name()='channel']/*[local-name()='title']");
            if (ft != null) feedTitle = PlainText(ft.InnerText, 40);
            var list = new List<Tuple<string, string, string>>();
            var nodes = doc.SelectNodes("//*[local-name()='item' or local-name()='entry']");
            if (nodes == null) return list;
            foreach (XmlNode n in nodes)
            {
                if (list.Count >= count) break;
                string title = "", link = null, when = "";
                foreach (XmlNode ch in n.ChildNodes)
                {
                    string ln = ch.LocalName;
                    if (ln == "title" && title.Length == 0) title = PlainText(ch.InnerText, 140);
                    else if (ln == "link" && link == null)
                    {
                        var href = ch.Attributes != null ? ch.Attributes["href"] : null;
                        var rel = ch.Attributes != null ? ch.Attributes["rel"] : null;
                        if (href != null && (rel == null || rel.Value == "alternate")) link = href.Value;
                        else if (href == null) link = ch.InnerText.Trim();
                    }
                    else if ((ln == "pubDate" || ln == "updated" || ln == "published" || ln == "date") && when.Length == 0) when = FeedWhen(ch.InnerText);
                }
                if (title.Length == 0) continue;
                string safe = SafeUrl(link);
                if (safe != null && !link.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase)) safe = null;   // (relative / odd links: not opened)
                string line = string.Join("  ·  ", new[] { feedTitle, when }.Where(x => !string.IsNullOrEmpty(x)).ToArray());
                list.Add(Tuple.Create(title, line, safe));
            }
            return list;
        }

        private string FeedWhen(string s)
        {
            DateTime d;
            s = (s ?? "").Trim();
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out d)
                || DateTime.TryParse(Regex.Replace(s, @"\s+[A-Z]{2,4}$", ""), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out d))
                return AgoText(DateTime.SpecifyKind(d, DateTimeKind.Utc));
            return "";
        }

        /// <summary>The JSON at the address through the template: "{a.b.0.c}" picks a value (lists by number). Read only.</summary>
        private static string DataText(string url, string template)
        {
            byte[] bytes = Fetch(url, CommunityMaxBytes);
            string json = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
            object root = new JavaScriptSerializer { MaxJsonLength = CommunityMaxBytes * 2, RecursionLimit = 64 }.DeserializeObject(json);
            string text = Regex.Replace(template, @"\{([^{}]{1,120})\}", m =>
            {
                string path = m.Groups[1].Value.Trim();
                if (path == "value" && !(root is Dictionary<string, object>)) return JsonValueText(root);
                object v = root;
                foreach (string part in path.Split('.'))
                {
                    var dict = v as Dictionary<string, object>;
                    var arr = v as object[] ?? (v as ArrayList != null ? ((ArrayList)v).ToArray() : null);
                    object next;
                    int idx;
                    if (dict != null && dict.TryGetValue(part, out next)) v = next;
                    else if (arr != null && int.TryParse(part, out idx) && idx >= 0 && idx < arr.Length) v = arr[idx];
                    else return "–";
                }
                return JsonValueText(v);
            });
            text = text.Replace("|", "\n");
            return text.Length > 400 ? text.Substring(0, 399) + "…" : text;
        }

        private static string JsonValueText(object v)
        {
            if (v == null) return "–";
            if (v is string) return PlainText((string)v, 200);
            if (v is bool) return (bool)v ? "✓" : "✗";
            if (v is decimal || v is double || v is int || v is long || v is float)
                return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture).ToString("#,0.##", System.Globalization.CultureInfo.CurrentCulture);
            if (v is Dictionary<string, object> || v is object[] || v is ArrayList) return "…";
            return PlainText(Convert.ToString(v, System.Globalization.CultureInfo.CurrentCulture), 200);
        }

        // ---------------- web snippets: a picture of the page ----------------

        private class SnapJob { public WidgetItem W; public int Gen; public string Url; }
        private readonly Queue<SnapJob> snapQueue = new Queue<SnapJob>();
        private SnapJob snapNow;
        private FrameworkElement snapBrowser;
        private Canvas snapHost;
        private DispatcherTimer snapTimer;
        private DateTime snapStarted, snapLoadedAt;
        private int snapW, snapH;

        private void QueueSnapshot(WidgetItem w, int gen, string url)
        {
            if (snapQueue.Any(j => ReferenceEquals(j.W, w)) || (snapNow != null && ReferenceEquals(snapNow.W, w))) return;
            snapQueue.Enqueue(new SnapJob { W = w, Gen = gen, Url = url });
            if (snapNow == null) NextSnapshot();
        }

        private void NextSnapshot()
        {
            snapNow = null;
            while (snapQueue.Count > 0)
            {
                var job = snapQueue.Dequeue();
                if (job.Gen != job.W.Gen || !CommunityMayFetch(job.W)) { job.W.RefreshedAt = DateTime.MinValue; continue; }
                if (StartSnapshot(job)) return;
            }
        }

        /// <summary>Playnite's Chromium, off screen at the page width (its height in the widget's shape), at the address.</summary>
        private bool StartSnapshot(SnapJob job)
        {
            try
            {
                if (browserType == null) browserType = FindBrowserType();
                if (browserType == null)
                {
                    job.W.Status = Loc("LOC_KX_WgWebNoBrowser", "Web snippets need Playnite's built-in browser (an address that's a picture works without it)");
                    return false;
                }
                var layer = UiTools.FindByName(Application.Current.MainWindow, "KX_Widgets");
                var panel = layer != null ? VisualTreeHelper.GetParent(layer) as Panel : null;
                if (panel == null) return false;
                if (snapHost == null || snapHost.Parent == null)
                {
                    // a canvas takes no room in the layout; the page sits far off screen on it (drawn, never seen)
                    snapHost = new Canvas { Width = 0, Height = 0, IsHitTestVisible = false, Focusable = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                    Grid.SetRowSpan(snapHost, 1);
                    panel.Children.Add(snapHost);
                }
                var d = job.W.Config.Custom;
                snapW = Math.Max(640, Math.Min(2560, d.ViewWidth));
                double aspect = job.W.DesignHeight > 0 ? job.W.DesignWidth / job.W.DesignHeight : 16.0 / 9;
                snapH = (int)Math.Max(240, Math.Min(2560, Math.Round(snapW / Math.Max(0.3, aspect))));
                var b = (FrameworkElement)Activator.CreateInstance(browserType);
                b.Width = snapW;
                b.Height = snapH;
                b.Focusable = false;
                b.IsHitTestVisible = false;
                Canvas.SetLeft(b, -40000);
                Canvas.SetTop(b, 0);
                try
                {
                    // no pop-up windows from the page
                    var lsh = browserType.GetProperty("LifeSpanHandler");
                    object handler = lsh != null ? BrowserPopups.Create(lsh.PropertyType) : null;
                    if (handler != null) lsh.SetValue(b, handler, null);
                }
                catch (Exception) { }
                snapHost.Children.Add(b);
                snapBrowser = b;
                snapNow = job;
                snapStarted = DateTime.Now;
                snapLoadedAt = DateTime.MinValue;
                var addr = browserType.GetProperty("Address");
                if (addr != null) addr.SetValue(b, job.Url, null);
                if (snapTimer == null)
                {
                    snapTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(500) };
                    snapTimer.Tick += (s, e) => { try { SnapTick(); } catch (Exception ex) { logger.Warn("Kippix: web snippet: " + ex.Message); StopSnapshot(); NextSnapshot(); } };
                }
                snapTimer.Start();
                return true;
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: web snippet: " + ex.Message);
                DisposeSnapBrowser();
                job.W.Status = Loc("LOC_KX_WgWebFailed", "Couldn't show that page just now");
                return false;
            }
        }

        private void SnapTick()
        {
            var job = snapNow;
            if (job == null || snapBrowser == null) { StopSnapshot(); return; }
            if (job.Gen != job.W.Gen || !CommunityMayFetch(job.W)) { job.W.RefreshedAt = DateTime.MinValue; StopSnapshot(); NextSnapshot(); return; }   // (off screen now: later)
            bool loading = true;
            try { var p = browserType.GetProperty("IsLoading"); loading = p == null || (bool)p.GetValue(snapBrowser, null); } catch (Exception) { }
            var now = DateTime.Now;
            if (!loading && snapLoadedAt == DateTime.MinValue) snapLoadedAt = now;
            if (loading) snapLoadedAt = DateTime.MinValue;
            bool settled = snapLoadedAt != DateTime.MinValue && (now - snapLoadedAt).TotalMilliseconds >= 1800;   // (its pictures and fonts in)
            bool late = (now - snapStarted).TotalSeconds > 25;
            if (!settled && !late) return;
            ImageSource shot = null;
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(snapBrowser) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, snapW, snapH));
                var rtb = new RenderTargetBitmap(snapW, snapH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                shot = rtb;
            }
            catch (Exception ex) { logger.Warn("Kippix: web snippet picture: " + ex.Message); }
            if (shot != null) { job.W.Photo = shot; job.W.Status = ""; }
            else job.W.Status = Loc("LOC_KX_WgWebFailed", "Couldn't show that page just now");
            StopSnapshot();
            NextSnapshot();
        }

        private void StopSnapshot()
        {
            if (snapTimer != null) snapTimer.Stop();
            DisposeSnapBrowser();
            snapNow = null;
        }

        private void DisposeSnapBrowser()
        {
            var b = snapBrowser;
            snapBrowser = null;
            if (b == null) return;
            try
            {
                if (snapHost != null) snapHost.Children.Remove(b);
                var d = b as IDisposable;
                if (d != null) d.Dispose();
            }
            catch (Exception ex) { logger.Warn("Kippix: closing a web snippet: " + ex.Message); }
        }

        // ---------------- adding one: its address ----------------

        private void AskCommunityAddress(WidgetConfig c, Action done)
        {
            OpenKeyboard(Loc("LOC_KX_WgAddressAsk", "The address (https://…)"), "https://", typed =>
            {
                string u = SafeUrl(typed);
                if (u == null) { PlaySound("back"); ShowNoticeSafe(Loc("LOC_KX_WgHead", "WIDGETS"), Loc("LOC_KX_WgAddressBad", "That isn't a web address (http or https)"), typed); return; }
                c.Custom.Url = u;
                if (c.Type != "data") { done(); return; }
                // a data widget: what it shows
                ui.BeginInvoke(new Action(() => OpenKeyboard(Loc("LOC_KX_WgTemplateAsk", "Words and {fields}: e.g. {data.price} – {data.status}"), "{value}", t2 =>
                {
                    c.Custom.Template = Clip(string.IsNullOrWhiteSpace(t2) ? "{value}" : t2, 400);
                    done();
                }, null, null, 400)), DispatcherPriority.ContextIdle);
            }, null, null, 2000);
        }

        // ---------------- widget files ----------------

        /// <summary>A copy of the widget for a widget file: no id, at the top left, not turned, its picture carried inside.</summary>
        private WidgetConfig WidgetForFile(WidgetItem w)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var copy = js.Deserialize<WidgetConfig>(js.Serialize(w.Config));
            copy.Id = null;
            copy.X = 0; copy.Y = 0;
            copy.Rotation = 0;
            copy.Layer = 0;
            EmbedTileImage(copy);
            return copy;
        }

        private void ExportWidgetFile(WidgetItem w)
        {
            if (w == null || w.Config == null) return;
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var copy = WidgetForFile(w);
                string name = string.Join("_", WidgetFullName(w.Config).Split(Path.GetInvalidFileNameChars())).Trim();
                if (name.Length == 0) name = "widget";
                if (name.Length > 60) name = name.Substring(0, 60);
                Directory.CreateDirectory(WidgetFilesFolder);
                string target = Path.Combine(WidgetFilesFolder, name + ".json");
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(WidgetFilesFolder, name + " (" + i + ").json");
                File.WriteAllText(target, js.Serialize(new WidgetFile { Widget = copy }), Encoding.UTF8);
                PlaySound("select");
                ShowNoticeSafe(Loc("LOC_KX_WgHead", "WIDGETS"), Loc("LOC_KX_WidgetExported", "Saved as a widget file"), target);
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: widget file: " + ex.Message);
                PlaySound("back");
                ShowNoticeSafe(Loc("LOC_KX_WgHead", "WIDGETS"), Loc("LOC_KX_WidgetExportFailed", "Couldn't save the widget file"), ex.Message);
            }
        }

        /// <summary>
        /// A widget file's text as a new widget, ready to add (a new id, values in range, addresses checked, a carried picture
        /// written to the tiles folder), or null with the reason. needSwitch: a web snippet / news feed / data widget needs
        /// Community widgets switched on (only checking a file, e.g. to keep it, it doesn't).
        /// </summary>
        private WidgetConfig ReadWidgetText(string text, bool needSwitch, out string problem)
        {
            problem = null;
            WidgetConfig c = null;
            try
            {
                if (text == null || text.Length > 30 * 1024 * 1024) throw new InvalidDataException("too big");
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var wf = js.Deserialize<WidgetFile>(text);
                c = wf != null ? wf.Widget : null;
                if (c == null || string.IsNullOrEmpty(c.Type) || (!IsCustomType(c.Type) && Array.IndexOf(WidgetTypeKeys, c.Type) < 0)) { problem = Loc("LOC_KX_WidgetFileBad", "That isn't a Kippix widget file"); return null; }
                if (needSwitch && IsCommunityType(c.Type) && !settings.CommunityWidgetsOn) { problem = Loc("LOC_KX_WidgetFileNeedsCommunity3", "Switch on Community widgets first (Community › Yours)"); return null; }
            }
            catch (Exception ex) { problem = Loc("LOC_KX_WidgetFileBad", "That isn't a Kippix widget file"); logger.Warn("Kippix: widget file: " + ex.Message); return null; }
            if (!needSwitch) return c;
            c.Id = Guid.NewGuid().ToString("N");
            c.Scale = c.Scale <= 0 || double.IsNaN(c.Scale) ? 1 : Math.Max(0.4, Math.Min(2.5, c.Scale));
            c.Rotation = 0;
            c.Layer = 0;
            c.Opacity = CleanOpacity(c.Opacity);
            c.BackColor = ParseColor(c.BackColor).HasValue ? c.BackColor : "";
            c.TextColor = ParseColor(c.TextColor).HasValue ? c.TextColor : "";
            if (IsCustomType(c.Type)) { if (c.Custom == null) SetupCustomConfig(c); NormalizeCustom(c); }   // (addresses checked, a carried picture written to the tiles folder)
            else c.Custom = null;
            return c;
        }

        /// <summary>Add from a widget file… (Community › Download / Yours, or Add a widget): the picker, then placing it.</summary>
        private void ImportWidgetFile()
        {
            PlaySound("select");
            string start = WidgetFilesFolder;
            try { Directory.CreateDirectory(start); } catch (Exception) { start = ""; }
            string back = optionsPage == "community" ? "community" : optionsPage == "widgetadd" ? "widgetadd" : "widgets";
            OpenPicker(Loc("LOC_KX_WidgetImport", "Add from a widget file…"), false, new[] { ".json" }, start, back, file =>
            {
                WidgetConfig c = null;
                string problem = null;
                try
                {
                    if (new FileInfo(file).Length > 20L * 1024 * 1024) throw new InvalidDataException("too big");
                    c = ReadWidgetText(File.ReadAllText(file), true, out problem);
                }
                catch (Exception ex) { c = null; problem = Loc("LOC_KX_WidgetFileBad", "That isn't a Kippix widget file"); logger.Warn("Kippix: widget file: " + ex.Message); }
                if (c == null)
                {
                    PlaySound("back");
                    ShowNoticeSafe(Loc("LOC_KX_WgHead", "WIDGETS"), problem, Path.GetFileName(file));
                    if (back == "community") GoBackTo("community", "cw:import"); else GoBackTo("widgets", "widgetadd");
                    return;
                }
                if (back == "community") { widgetNewReturn = "community"; communityWidgetAdded = c.Id; communityPlacingId = c.Id; }   // (placed, then back to the Community page: Undo takes it off)
                AddWidgetConfig(c.Type, c.Subject, c);
            });
        }
    }
}
