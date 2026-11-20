using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace KippixShared
{
    /// <summary>A Steam download's progress as Steam's own content log tells it.</summary>
    internal class SteamLogProgress
    {
        public string AppId;
        public long Downloaded;       // bytes downloaded so far (as Steam's "download" figure)
        public long ToDownload;
        public double BytesPerSecond; // Steam's own "current download rate" (0 when not recent)
        public bool Active;           // still downloading (no stop / cancel since it started)
    }

    /// <summary>
    /// (0.99.72) Live Steam download progress from logs\content_log.txt. Steam only saves progress to the appmanifest when a
    /// download starts or stops, so a long download looked stuck at whatever it had when it started (e.g. 297 MB of a game
    /// already 63 GB in). The log has what's needed: "update started : download X/Y" when it (re)starts, then every ~5.5 minutes
    /// "stats:" lines with the bytes each server sent in that window (an "(Invalid, 0)" line is their total), and every minute
    /// "Current download rate: N Mbps". Progress = X + the windows since + the rate since the last window. Nothing is saved,
    /// so it's right after Playnite restarts and when the download was started outside Playnite.
    /// The log is read once (its last 2 MB) and after that only the lines Steam adds to it. Only lines in the last 2 MB
    /// count (256 KB for the states).
    /// </summary>
    internal static class SteamContentLog
    {
        private static readonly Regex LineRx = new Regex(@"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\] (.*)$", RegexOptions.Compiled);
        private static readonly Regex StartedRx = new Regex(@"^AppID (\d+) update started : download (\d+)/(\d+)", RegexOptions.Compiled);
        private static readonly Regex StatsRx = new Regex(@"^stats: \((\w+), \d+\)[^:]*: (\d+) Bytes, (\d+) sec", RegexOptions.Compiled);
        private static readonly Regex RateRx = new Regex(@"^Current download rate: ([\d.]+) Mbps", RegexOptions.Compiled);
        private static readonly Regex StoppedRx = new Regex(@"^AppID (\d+) (update canceled|App update changed : None|finished update)", RegexOptions.Compiled);

        // download states
        private static readonly Regex LogUpdateChanged = new Regex(@"AppID (\d+) App update changed : (.*)$", RegexOptions.Compiled);
        private static readonly Regex LogCanceled = new Regex(@"AppID (\d+) update canceled : (.*)$", RegexOptions.Compiled);
        private static readonly Regex LogStarted = new Regex(@"AppID (\d+) update started", RegexOptions.Compiled);
        private static readonly Regex LogFinished = new Regex(@"AppID (\d+) (update finished|finished update|state changed : Fully Installed,\s*$)", RegexOptions.Compiled);

        private const long ProgressSpan = 2 * 1024 * 1024;   // how far back Read looks
        private const long StatesSpan = 256 * 1024;          // how far back States looks

        private class AppRun
        {
            public long Base, Total;
            public DateTime Started, LastCounted;
            public double Counted;
            public bool Active;
            public long At;   // where its "update started" line is in the log
        }

        private class LogState
        {
            public string State;
            public long At;   // where the line that said so is in the log
        }

        private static readonly object gate = new object();
        private static string logFile;
        private static DateTime logCreated;
        private static long readTo;            // read up to here: the start of a line Steam hasn't finished writing
        private static long seenLen = -1;      // the log's size and time when last read (-1: no log)
        private static DateTime seenTime;
        private static byte[] buf;

        // progress (Read)
        private static Dictionary<string, AppRun> runs = new Dictionary<string, AppRun>();
        private static AppRun current;
        private static double rate;
        private static DateTime rateAt = DateTime.MinValue;
        // one window's stats lines share a time stamp: their total (the "(Invalid, 0)" line), or the sum when it's missing
        private static DateTime batchAt = DateTime.MinValue;
        private static double batchSum, batchTotal = -1;
        private static int batchSecs, batchTotalSecs;

        // states (States)
        private static Dictionary<string, LogState> states = new Dictionary<string, LogState>();
        private static Dictionary<string, LogState> canceledReason = new Dictionary<string, LogState>();

        /// <summary>The download in progress (or last stopped) per app id. Empty when there's no log.</summary>
        public static Dictionary<string, SteamLogProgress> Read(string logPath)
        {
            var result = new Dictionary<string, SteamLogProgress>();
            lock (gate)
            {
                try { if (!CatchUp(logPath)) return result; }
                catch (Exception) { return result; }

                long from = seenLen - ProgressSpan;
                DateTime now = DateTime.Now;   // (the log's times are local)
                bool rateFresh = (now - rateAt).TotalSeconds < 150;
                foreach (var kv in runs)
                {
                    AppRun r = kv.Value;
                    if (r.At < from) continue;   // started further back than the part of the log that counts
                    double counted = r.Counted;
                    DateTime lastCounted = r.LastCounted;
                    if (r == current && batchAt != DateTime.MinValue) { counted += BatchBytes(); lastCounted = batchAt; }   // the window written last
                    double done = r.Base + counted;
                    double bps = 0;
                    if (r.Active && rateFresh)
                    {
                        bps = rate;
                        double since = (now - (lastCounted > r.Started ? lastCounted : r.Started)).TotalSeconds;
                        if (since > 0) done += bps * Math.Min(since, 360);
                    }
                    result[kv.Key] = new SteamLogProgress
                    {
                        AppId = kv.Key,
                        Downloaded = (long)Math.Min(r.Total, Math.Max(r.Base, done)),
                        ToDownload = r.Total,
                        BytesPerSecond = bps,
                        Active = r.Active
                    };
                }
            }
            return result;
        }

        /// <summary>The latest download state per app id from the end of the log ("downloading", "installing", "verifying",
        /// "suspended", "paused" or "finished"). Empty when there's no log.</summary>
        public static Dictionary<string, string> States(string logPath)
        {
            var result = new Dictionary<string, string>();
            lock (gate)
            {
                try { if (!CatchUp(logPath)) return result; }
                catch (Exception) { return result; }
                long from = seenLen - StatesSpan;
                foreach (var kv in states) if (kv.Value.At >= from) result[kv.Key] = kv.Value.State;
            }
            return result;
        }

        /// <summary>Whether Steam has written to the log since it was last read: one look at its size and time.</summary>
        public static bool Changed(string logPath)
        {
            if (string.IsNullOrEmpty(logPath)) return false;
            lock (gate)
            {
                try
                {
                    var fi = new FileInfo(logPath);
                    if (!fi.Exists) return seenLen != -1;
                    return logPath != logFile || fi.Length != seenLen || fi.LastWriteTimeUtc != seenTime;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Reads the lines Steam has added since last time (the first time, or once Steam has started a new log: its
        /// last 2 MB). False when there's no log.</summary>
        private static bool CatchUp(string logPath)
        {
            var fi = new FileInfo(logPath);
            if (!fi.Exists) { seenLen = -1; return false; }
            long len = fi.Length;
            DateTime created = fi.CreationTimeUtc;
            if (logPath != logFile || len < readTo || created != logCreated)
            {
                logFile = logPath;
                logCreated = created;
                readTo = Math.Max(0, len - ProgressSpan);
                runs = new Dictionary<string, AppRun>();
                current = null;
                rate = 0;
                rateAt = DateTime.MinValue;
                batchAt = DateTime.MinValue; batchSum = 0; batchTotal = -1; batchSecs = 0; batchTotalSecs = 0;
                states = new Dictionary<string, LogState>();
                canceledReason = new Dictionary<string, LogState>();
            }
            seenLen = len;
            seenTime = fi.LastWriteTimeUtc;
            if (readTo >= len) return true;

            if (buf == null) buf = new byte[64 * 1024];
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1))
            {
                fs.Seek(readTo, SeekOrigin.Begin);
                long pos = readTo;   // where buf[0] is in the log
                int have = 0;
                while (pos + have < len)
                {
                    if (have == buf.Length) { pos += have; have = 0; readTo = pos; }   // no line of Steam's is this long: skipped
                    int n = fs.Read(buf, have, (int)Math.Min(buf.Length - have, len - pos - have));
                    if (n <= 0) break;
                    int start = 0;
                    for (int i = have; i < have + n; i++)
                    {
                        if (buf[i] != (byte)'\n') continue;
                        int end = i > start && buf[i - 1] == (byte)'\r' ? i - 1 : i;
                        try { Line(Encoding.UTF8.GetString(buf, start, end - start), pos + start); } catch (Exception) { }   // (one odd line is skipped, not read again)
                        start = i + 1;
                    }
                    have += n;
                    if (start > 0)
                    {
                        // the unfinished line moves to the front (and is read again next time if Steam is still writing it)
                        Buffer.BlockCopy(buf, start, buf, 0, have - start);
                        pos += start;
                        have -= start;
                        readTo = pos;
                    }
                }
            }
            return true;
        }

        private static void Line(string line, long at)
        {
            StateLine(line, at);

            Match lm = LineRx.Match(line);
            if (!lm.Success) return;
            DateTime t;
            if (!DateTime.TryParseExact(lm.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return;
            string body = lm.Groups[2].Value;
            Match m;
            if ((m = StatsRx.Match(body)).Success)
            {
                if (batchAt != t) Flush();
                batchAt = t;
                long n = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int secs = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (m.Groups[1].Value == "Invalid") { batchTotal = n; batchTotalSecs = secs; }
                else { batchSum += n; batchSecs = Math.Max(batchSecs, secs); }
                return;
            }
            Flush();
            if ((m = StartedRx.Match(body)).Success)
            {
                current = new AppRun
                {
                    Base = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    Total = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    Started = t,
                    Active = true,
                    At = at
                };
                runs[m.Groups[1].Value] = current;
            }
            else if ((m = StoppedRx.Match(body)).Success)
            {
                AppRun r;
                if (runs.TryGetValue(m.Groups[1].Value, out r))
                {
                    r.Active = false;
                    if (r == current) current = null;
                }
            }
            else if ((m = RateRx.Match(body)).Success)
            {
                double mbps;
                if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out mbps)) { rate = mbps * 1000000 / 8; rateAt = t; }
            }
        }

        /// <summary>What the stats window being read adds to the download running now.</summary>
        private static double BatchBytes()
        {
            double bytes = batchTotal >= 0 ? batchTotal : batchSum;
            int secs = batchTotal >= 0 ? batchTotalSecs : batchSecs;
            // the first window after a start also holds what came before it (another game, or before a pause): only its share
            double inRun = (batchAt - current.Started).TotalSeconds;
            if (secs > 0 && inRun < secs) bytes *= Math.Max(0, inRun) / secs;
            return bytes;
        }

        private static void Flush()
        {
            if (batchAt == DateTime.MinValue) return;
            if (current != null)
            {
                current.Counted += BatchBytes();
                current.LastCounted = batchAt;
            }
            batchAt = DateTime.MinValue; batchSum = 0; batchTotal = -1; batchSecs = 0; batchTotalSecs = 0;
        }

        private static void StateLine(string line, long at)
        {
            Match m;
            if ((m = LogCanceled.Match(line)).Success)
            {
                string why = m.Groups[2].Value;
                var s = new LogState { State = why.IndexOf("Suspended", StringComparison.OrdinalIgnoreCase) >= 0 ? "suspended" : "paused", At = at };
                canceledReason[m.Groups[1].Value] = s;
                states[m.Groups[1].Value] = s;
            }
            else if ((m = LogUpdateChanged.Match(line)).Success)
            {
                string id = m.Groups[1].Value, what = m.Groups[2].Value;
                if (what.Trim() == "None")
                {
                    LogState why;
                    bool canceled = canceledReason.TryGetValue(id, out why) && at - why.At < StatesSpan;   // (the cancel line still in the part that counts)
                    states[id] = new LogState { State = canceled ? why.State : "finished", At = at };
                }
                else if (what.Contains("Stopping")) { /* winding down; the cancel line before it says why */ }
                else if (what.Contains("Committing")) SetState(id, "installing", at);
                else if (what.Contains("Verifying")) SetState(id, "verifying", at);
                else if (what.Contains("Downloading") || what.Contains("Staging")) { SetState(id, "downloading", at); canceledReason.Remove(id); }
            }
            else if ((m = LogStarted.Match(line)).Success)
            {
                SetState(m.Groups[1].Value, "downloading", at);
                canceledReason.Remove(m.Groups[1].Value);
            }
            else if ((m = LogFinished.Match(line)).Success)
            {
                SetState(m.Groups[1].Value, "finished", at);
                canceledReason.Remove(m.Groups[1].Value);
            }
        }

        private static void SetState(string id, string state, long at)
        {
            states[id] = new LogState { State = state, At = at };
        }
    }
}
