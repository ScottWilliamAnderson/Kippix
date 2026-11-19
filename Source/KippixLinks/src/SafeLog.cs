using System;
using Playnite.SDK;

namespace KippixLinks
{
    /// <summary>
    /// The only way this add-on writes to Playnite's log. It takes fixed text written in the code, an 8-hex link id and,
    /// for a failure, only the exception's type name and HResult: never its message (that can hold an address or a path).
    /// Callers pass string literals only: no address, title, name, clipboard text, port or token ever goes in.
    /// </summary>
    public static class SafeLog
    {
        private static ILogger logger;
        private static bool loggerFailed;

        private static ILogger Logger
        {
            get
            {
                if (logger == null && !loggerFailed)
                {
                    try { logger = LogManager.GetLogger(); }
                    catch (Exception) { loggerFailed = true; }   // (outside Playnite, e.g. the offline tests)
                }
                return logger;
            }
        }

        public static void Info(string literal) { Write(false, literal, null, null); }
        public static void Info(string literal, string id) { Write(false, literal, id, null); }
        public static void Warn(string literal, Exception ex) { Write(true, literal, null, ex); }
        public static void Warn(string literal, string id, Exception ex) { Write(true, literal, id, ex); }

        /// <summary>"Links: literal[ id=abcd1234][: IOException 0x80070020]"</summary>
        internal static string Format(string literal, string id, Exception ex)
        {
            string s = "Links: " + (literal ?? "");
            if (id != null) s += " id=" + (IsId(id) ? id : "?");   // anything that isn't an id is not written
            if (ex != null)
            {
                // a wrapper from reflection says nothing: the real failure is inside it
                var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                s += ": " + inner.GetType().Name + " 0x" + inner.HResult.ToString("X8");
            }
            return s;
        }

        private static bool IsId(string id)
        {
            if (id.Length != 8) return false;
            foreach (char c in id)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static void Write(bool warn, string literal, string id, Exception ex)
        {
            try
            {
                var l = Logger;
                if (l == null) return;
                string s = Format(literal, id, ex);
                if (warn) l.Warn(s); else l.Info(s);
            }
            catch (Exception) { }   // logging must never be the thing that fails
        }
    }
}
