using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KippixLinks
{
    /// <summary>
    /// The top bar button's picture when it follows the latest link: Kippix's own white icon for the site when its theme has
    /// one (Images\Streaming\netflix.png and the like: a file on this PC, nothing is fetched), else a badge with the site's
    /// first letter. It shows which site the button goes to, never which page; "Button icon" in the list turns it off.
    /// </summary>
    internal static class SiteIcon
    {
        /// <summary>The picture for this address, or null (then the plain link icon is used).</summary>
        public static ImageSource For(string url, string kippixThemeDir)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                string key = LinkUrl.IconKey(url);
                if (key != null && !string.IsNullOrEmpty(kippixThemeDir))
                {
                    string file = Path.Combine(kippixThemeDir, "Images", "Streaming", key + ".png");
                    if (File.Exists(file))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;   // (read now: the theme's file isn't kept open)
                        bmp.DecodePixelWidth = 64;
                        bmp.UriSource = new Uri(file, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        return bmp;
                    }
                }
                return Badge(LinkUrl.Initial(url));
            }
            catch (Exception) { return null; }   // (nothing to say about it: the plain icon shows)
        }

        /// <summary>A rounded square with one letter in it, drawn like the top bar's line icons (white, about 1.8 px lines at 26 px).</summary>
        public static ImageSource Badge(char letter)
        {
            var group = new DrawingGroup();
            // the full 24 x 24 box, so the badge is the same size whatever the letter
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            var pen = new Pen(Brushes.White, 1.66) { LineJoin = PenLineJoin.Round };
            group.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(1.4, 1.4, 21.2, 21.2), 6, 6)));

            FontFamily font = null;
            try { font = Application.Current != null ? Application.Current.TryFindResource("FontTitilliumWeb") as FontFamily : null; }
            catch (Exception) { font = null; }
            if (font == null) font = new FontFamily("Segoe UI");
            var face = new Typeface(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
#pragma warning disable 618   // (the constructor without pixelsPerDip: the only one on .NET Framework 4.6.2; the text becomes a shape anyway)
            var text = new FormattedText(letter.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 15.5, Brushes.White);
#pragma warning restore 618
            Geometry glyph = text.BuildGeometry(new Point(0, 0));
            Rect b = glyph.Bounds;
            if (!b.IsEmpty && b.Width > 0 && b.Height > 0)
            {
                // centred on the ink, not on the line box (capitals sit high in it)
                var g = glyph.Clone();
                g.Transform = new TranslateTransform(12 - (b.X + b.Width / 2), 12 - (b.Y + b.Height / 2));
                group.Children.Add(new GeometryDrawing(Brushes.White, null, g));
            }
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
    }
}
