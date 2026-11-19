using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KippixLinks
{
    /// <summary>
    /// Web addresses: what counts as one, the site name shown for it, and a page title cleaned of the browser's name.
    /// Pure functions (no I/O), so the offline tests can check them.
    /// </summary>
    public static class LinkUrl
    {
        public const int MaxLength = 2048;

        // "scheme:" at the start (javascript:, mailto:, file:...), but not "host:8080"
        private static readonly Regex SchemeLike = new Regex(@"^[A-Za-z][A-Za-z0-9+.\-]*:(?!\d)", RegexOptions.CultureInvariant);

        /// <summary>A copied address made safe to keep and open: http or https only, no user name or password in it.</summary>
        public static bool TryParse(string text, out string url)
        {
            url = null;
            if (text == null) return false;
            string s = text.Trim();
            if (s.Length == 0 || s.Length > MaxLength) return false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '"' || c == '<' || c == '>' || c == '`') return false;
                // invisible format characters (zero-width space, right-to-left override...): a host could look like another
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) return false;
            }
            // a bare "example.com/show" (as some browsers copy it): https
            if (s.IndexOf("://", StringComparison.Ordinal) < 0 && s.IndexOf('.') >= 0 && !SchemeLike.IsMatch(s)) s = "https://" + s;
            Uri uri;
            if (!Uri.TryCreate(s, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrEmpty(uri.Host)) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
            // an international host is kept in its ASCII (punycode) form, so the redirect header is plain ASCII
            if (!IsPlainAscii(uri.Host))
            {
                try
                {
                    string idn = uri.IdnHost;
                    if (string.IsNullOrEmpty(idn) || !IsPlainAscii(idn)) return false;
                    var ub = new UriBuilder(uri) { Host = idn };
                    uri = ub.Uri;
                }
                catch (Exception) { return false; }
                if (!IsPlainAscii(uri.Host)) return false;
            }
            string abs = uri.AbsoluteUri;
            // measured after normalising (so parsing the result again gives the same answer), printable ASCII only
            if (abs.Length > MaxLength || !IsPlainAscii(abs)) return false;
            url = abs;
            return true;
        }

        /// <summary>Printable ASCII only (no space, no CR / LF, nothing above 0x7E).</summary>
        private static bool IsPlainAscii(string s)
        {
            foreach (char c in s)
                if (c < (char)0x21 || c > (char)0x7E) return false;
            return true;
        }

        private static bool HostIs(string host, string domain)
        {
            return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
        }

        /// <summary>The site's name for the panel (from the host only): "Netflix", "BBC iPlayer"... else the host without www.</summary>
        public static string Site(string url)
        {
            Uri uri;
            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host)) return "Link";
            string host = uri.Host.ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
            string path = uri.AbsolutePath ?? "";
            if (HostIs(host, "netflix.com")) return "Netflix";
            if (HostIs(host, "primevideo.com")) return "Prime Video";
            // amazon.com, amazon.co.uk, amazon.de...: only its video pages are Prime Video
            if ((host.StartsWith("amazon.", StringComparison.Ordinal) || host.Contains(".amazon."))
                && (path.StartsWith("/gp/video", StringComparison.OrdinalIgnoreCase) || path.IndexOf("/Amazon-Video", StringComparison.OrdinalIgnoreCase) >= 0))
                return "Prime Video";
            if (HostIs(host, "disneyplus.com")) return "Disney+";
            if ((HostIs(host, "bbc.co.uk") || HostIs(host, "bbc.com")) && path.StartsWith("/iplayer", StringComparison.OrdinalIgnoreCase)) return "BBC iPlayer";
            if (HostIs(host, "itv.com")) return "ITVX";
            if (HostIs(host, "channel4.com")) return "Channel 4";
            if (HostIs(host, "channel5.com") || HostIs(host, "my5.tv")) return "5";
            if (HostIs(host, "youtube.com") || HostIs(host, "youtu.be")) return "YouTube";
            if (HostIs(host, "crunchyroll.com")) return "Crunchyroll";
            if (HostIs(host, "tv.apple.com")) return "Apple TV";
            if (HostIs(host, "paramountplus.com")) return "Paramount+";
            if (HostIs(host, "max.com") || HostIs(host, "hbomax.com")) return "Max";
            if (HostIs(host, "nowtv.com")) return "NOW";
            if (HostIs(host, "discoveryplus.com")) return "discovery+";
            if (HostIs(host, "twitch.tv")) return "Twitch";
            if (HostIs(host, "plex.tv")) return "Plex";
            return host;
        }

        // " - Mozilla Firefox", " — Google Chrome", " - Personal - Microsoft​ Edge" (Edge puts a zero-width space in its name)...
        private static readonly Regex BrowserSuffix = new Regex(
            "\\s(?:-|\u2014|\u2013)\\s(?:Mozilla Firefox Private Browsing|Mozilla Firefox|Google Chrome|Microsoft\u200B? Edge|Brave|Vivaldi|Opera|Chromium|Personal|Work|Profile \\d+)\\s*$",
            RegexOptions.CultureInvariant);

        private static readonly string[] NotTitles = { "New Tab", "New tab", "Untitled", "Loading\u2026", "Loading..." };

        // a window that only says its browser's name (nothing loaded yet): never a page title
        private static readonly Regex BareBrowser = new Regex(
            "^(?:Mozilla Firefox(?: Private Browsing)?|Firefox|Google Chrome|Chrome|Microsoft\u200b? Edge|Edge|Brave|Vivaldi|Opera|Chromium|Thorium|LibreWolf|Waterfox|Floorp|Zen(?: Browser)?)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>A browser window's title as a page title: the browser's own name taken off; null when it isn't a real title yet.</summary>
        public static string CleanTitle(string windowTitle)
        {
            if (windowTitle == null) return null;
            string t = windowTitle.Trim();
            for (int i = 0; i < 8; i++)
            {
                string next = BrowserSuffix.Replace(t, "").Trim();
                if (next == t) break;
                t = next;
            }
            if (t.Length < 2) return null;
            if (t.StartsWith("127.0.0.1", StringComparison.Ordinal) || t.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("https:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (string n in NotTitles)
                if (string.Equals(t, n, StringComparison.OrdinalIgnoreCase)) return null;
            if (BareBrowser.IsMatch(t)) return null;
            if (t.Length > 80) t = t.Substring(0, 80).TrimEnd();
            return t;
        }
    }
}
