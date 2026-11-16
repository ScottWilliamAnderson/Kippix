using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Balanced logo sizes (LogoBalance, on): every game's logo takes about the same room on screen.
    ///
    /// The logo (Extra Metadata Loader's control) sat in one fixed box, 560 x 150 on the home screen: a wide wordmark filled
    /// it, a square or tall logo (a crest, a stacked title) was held by the box's height and came out at a quarter of the
    /// size, and a logo with empty margins in its file smaller still. Here each logo file is measured once (its size and the
    /// part of it that isn't see-through) and the box is made for that logo: the drawn part gets about the same area whatever
    /// its shape, no wider than the old box and up to about 1.65 times its height; a logo's empty margins no longer push it
    /// away from the edge it's lined up with. The wide ones stay much as they were.
    ///
    /// Display only: nothing is written, and the logo is still Extra Metadata Loader's own control (its picture's own
    /// size limit is lifted to the box worked out here).
    /// </summary>
    public partial class ThemeApi
    {
        private class LogoFit
        {
            public long Stamp;            // the file's time and length (measured again when it changes)
            public double W, H;           // the file's size in pixels
            public double L, T, R, B;     // the drawn part, as fractions of the file (0..1)
        }

        private const string LogoControlName = "ExtraMetadataLoader_LogoLoaderControl";
        private static readonly DependencyProperty LogoBoxProperty = DependencyProperty.RegisterAttached("KXLogoBox", typeof(Size), typeof(ThemeApi), new PropertyMetadata(Size.Empty));
        private static readonly DependencyProperty LogoDoneProperty = DependencyProperty.RegisterAttached("KXLogoDone", typeof(string), typeof(ThemeApi), new PropertyMetadata(null));

        private readonly object logoGate = new object();
        private readonly Dictionary<Guid, LogoFit> logoFits = new Dictionary<Guid, LogoFit>();   // (null: no logo, or unreadable)
        private bool logoWarmed, logoWasOn;

        /// <summary>A logo file's size and drawn part. On a worker (the file is read, at a small size).</summary>
        private static LogoFit LogoMeasure(string file)
        {
            try
            {
                if (file == null) return null;
                var fi = new FileInfo(file);
                if (!fi.Exists) return null;
                var fit = new LogoFit { Stamp = fi.LastWriteTimeUtc.Ticks ^ fi.Length };
                var bmp = new BitmapImage();
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    fit.W = frame.PixelWidth; fit.H = frame.PixelHeight;
                    if (fit.W < 2 || fit.H < 2) return null;
                    fs.Position = 0;
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    if (fit.W >= fit.H) bmp.DecodePixelWidth = (int)Math.Min(fit.W, 192); else bmp.DecodePixelHeight = (int)Math.Min(fit.H, 192);
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }
                var px = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                int w = px.PixelWidth, h = px.PixelHeight;
                var data = new byte[w * h * 4];
                px.CopyPixels(data, w * 4, 0);
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (data[(y * w + x) * 4 + 3] > 24)
                        {
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                        }
                if (x1 < x0 || y1 < y0) return null;   // (nothing drawn in it)
                fit.L = (double)x0 / w; fit.R = (double)(x1 + 1) / w;
                fit.T = (double)y0 / h; fit.B = (double)(y1 + 1) / h;
                return fit;
            }
            catch (Exception) { return null; }
        }

        private LogoFit LogoFitOf(Guid id, bool measure)
        {
            lock (logoGate)
            {
                LogoFit hit;
                if (logoFits.TryGetValue(id, out hit)) return hit;
            }
            if (!measure) return null;
            var fit = LogoMeasure(LogoFileOf(id));
            lock (logoGate) logoFits[id] = fit;
            return fit;
        }

        /// <summary>
        /// The box for a logo, from the box every logo had (boxW x boxH): the drawn part at about 78% of that box's area
        /// (what a wide logo, which fills it, has now), no wider than the box and up to 1.65 times as high. Returned: the
        /// whole picture's size at that scale, and how much of it is empty at the left, right and bottom.
        /// </summary>
        private static Size LogoBoxFor(LogoFit fit, double boxW, double boxH, out double padL, out double padR, out double padB)
        {
            double iw = Math.Max(1, (fit.R - fit.L) * fit.W), ih = Math.Max(1, (fit.B - fit.T) * fit.H);
            double r = iw / ih;
            double h = Math.Sqrt(boxW * boxH * 0.78 / r), w = r * h;
            double s = Math.Min(1, Math.Min(boxH * 1.65 / h, boxW / w));
            double scale = w * s / iw;   // screen units per pixel of the file
            padL = fit.L * fit.W * scale;
            padR = (1 - fit.R) * fit.W * scale;
            padB = (1 - fit.B) * fit.H * scale;
            return new Size(Math.Round(fit.W * scale), Math.Round(fit.H * scale));
        }

        private static void LogoControls(DependencyObject root, List<ContentControl> found)
        {
            if (root == null) return;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var cc = child as ContentControl;
                if (cc != null && cc.Name == LogoControlName) { found.Add(cc); continue; }
                var fe = child as FrameworkElement;
                if (fe != null && fe.Visibility == Visibility.Collapsed) continue;   // (screens that aren't up)
                LogoControls(child, found);
            }
        }

        /// <summary>From the tick, and at once when another game is selected: the logo controls on screen get this game's box.</summary>
        private void LogoBalanceApply()
        {
            if (!IsFullscreen || (!settings.LogoBalance && !logoWasOn)) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return;
            if (!logoWarmed && settings.LogoBalance) { logoWarmed = true; LogoWarm(); }
            Game game = null;
            try { game = SelectedGameNow(); } catch (Exception) { }
            var found = new List<ContentControl>();
            LogoControls(win, found);
            if (found.Count == 0) return;
            Guid id = game != null ? game.Id : Guid.Empty;
            LogoFit fit = settings.LogoBalance && id != Guid.Empty ? LogoFitOf(id, false) : null;
            bool known;
            lock (logoGate) known = logoFits.ContainsKey(id);
            if (settings.LogoBalance && id != Guid.Empty && !known)
            {
                // not measured yet: on a worker, then again (the old box until then)
                KippixShared.SafeWork.Queue(_ => { LogoFitOf(id, true); OnUi(LogoBalanceApply); });
                return;
            }
            foreach (var cc in found)
            {
                try { LogoBoxApply(cc, fit, id); }
                catch (Exception ex) { logger.Warn("Kippix: logo size: " + ex.Message); }
            }
            if (settings.LogoBalance) logoWasOn = true;
        }

        private void LogoBoxApply(ContentControl cc, LogoFit fit, Guid id)
        {
            // the box the theme gave it, kept the first time (what it goes back to)
            var first = (Size)cc.GetValue(LogoBoxProperty);
            if (first.IsEmpty)
            {
                if (double.IsInfinity(cc.MaxWidth) || double.IsInfinity(cc.MaxHeight)) return;
                first = new Size(cc.MaxWidth, cc.MaxHeight);
                cc.SetValue(LogoBoxProperty, first);
            }
            string done = (fit != null ? id.ToString() + fit.Stamp : "none") + "|" + cc.HorizontalAlignment;
            if ((string)cc.GetValue(LogoDoneProperty) == done) return;
            cc.SetValue(LogoDoneProperty, done);
            var area = VisualTreeHelper.GetParent(cc) as FrameworkElement;
            bool ourArea = area != null && (area.Name == "KX_LogoArea" || area.Name == "DetLogo");   // (the home screen's and the game page's: as high as the old box)
            if (fit == null)
            {
                cc.MaxWidth = first.Width; cc.MaxHeight = first.Height;
                cc.Margin = new Thickness(0);
                if (ourArea) area.MaxHeight = first.Height;
                return;
            }
            double padL, padR, padB;
            Size box = LogoBoxFor(fit, first.Width, first.Height, out padL, out padR, out padB);
            cc.MaxWidth = box.Width; cc.MaxHeight = box.Height;
            // (its empty margins hang over the edge it's lined up with, and under the line it stands on)
            cc.Margin = new Thickness(cc.HorizontalAlignment == HorizontalAlignment.Left ? -padL : 0, 0, cc.HorizontalAlignment == HorizontalAlignment.Right ? -padR : 0, -padB);
            if (ourArea) area.MaxHeight = Math.Max(first.Height, box.Height);
            // Extra Metadata Loader's picture has a size limit of its own (its settings): lifted to this box
            var img = UiTools.FindByName(cc, "LogoImage") as Image;
            if (img != null)
            {
                if (img.MaxWidth < box.Width) img.MaxWidth = box.Width;
                if (img.MaxHeight < box.Height) img.MaxHeight = box.Height;
            }
            else cc.SetValue(LogoDoneProperty, null);   // (its picture isn't made yet: at the next tick)
        }

        /// <summary>Once, on a worker: every logo measured, so moving along the row never waits for one.</summary>
        private void LogoWarm()
        {
            List<Guid> ids;
            try { ids = new List<Guid>(); foreach (var g in api.Database.Games) if (g != null) ids.Add(g.Id); }
            catch (Exception) { return; }
            KippixShared.SafeWork.Queue(_ =>
            {
                int n = 0;
                foreach (var id in ids) { try { if (LogoFitOf(id, true) != null) n++; } catch (Exception) { } }
                logger.Info("Kippix: logo sizes: " + n + " logos measured");
            });
        }
    }
}
