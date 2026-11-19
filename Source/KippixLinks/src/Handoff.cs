using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace KippixLinks
{
    /// <summary>
    /// Keeps the address off every command line: the browser is started with http://127.0.0.1:port/token, and that one
    /// request is answered with a redirect to the real address. Only this machine can connect (loopback), the token is
    /// random, and the listener goes away 5 s after the browser asked (or after 2 minutes if it never did).
    /// </summary>
    public class Handoff
    {
        public const int WaitMs = 120000;   // waiting for the browser's first request
        public const int GraceMs = 5000;    // after it: browsers sometimes ask twice
        private const int MaxRequest = 8192;
        private const int SocketTimeoutMs = 3000;   // for the whole request head, not per read
        private const int MaxConnections = 16;      // at once (a browser opens a few; more is someone else)

        private readonly object gate = new object();
        private Session current;

        /// <summary>The listener is up (for the tests).</summary>
        public bool Alive
        {
            get { lock (gate) return current != null && current.IsAlive; }
        }

        public const int MaxHoldMs = 15000; // the longest a good request waits for "ready" (see Begin): a browser starting from cold

        private Session last;               // the newest handoff, kept after it ended only to say whether it was served

        /// <summary>The newest handoff has sent the browser on to its address.</summary>
        public bool Served
        {
            get { lock (gate) return last != null && last.WasServed; }
        }

        /// <summary>Starts serving url once and returns the loopback address to open instead. Cancels any earlier one.</summary>
        public string Begin(string url) { return Begin(url, null); }

        /// <summary>A "ready" that turns true ms after it was made (for the tests, and when nothing better can be told).</summary>
        public static Func<bool> After(int ms)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            return () => clock.ElapsedMilliseconds >= ms;
        }

        /// <summary>
        /// As Begin(url), but the browser's request is only answered once ready() says so (asked every 100 ms, off the UI
        /// thread, for at most MaxHoldMs). Until then the browser's new window shows nothing and its title says nothing.
        /// </summary>
        public string Begin(string url, Func<bool> ready)
        {
            if (url == null || url.IndexOf('\r') >= 0 || url.IndexOf('\n') >= 0
                || !(url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal)))
                throw new ArgumentException("Not a web address.");
            foreach (char c in url)
                if (c < (char)0x21 || c > (char)0x7E) throw new ArgumentException("Not a web address.");   // (a header is ASCII)
            Stop();
            var s = new Session(url, ready);
            try { s.Start(); }
            catch (Exception ex)
            {
                s.End();
                SafeLog.Warn("handoff: listener didn't start", ex);
                throw new InvalidOperationException("The link handoff couldn't start.");
            }
            lock (gate) { current = s; last = s; }
            return s.HopUrl;
        }

        public void Stop()
        {
            Session s;
            lock (gate) { s = current; current = null; }
            if (s != null) s.End();
        }

        /// <summary>One handoff: its own listener, token and address, so a newer one can't be answered by an older one.</summary>
        private sealed class Session
        {
            private readonly object sync = new object();
            private string url;
            private readonly string token;
            private TcpListener listener;
            private Timer timer;
            private bool alive;
            private bool served;
            private int open;
            public string HopUrl { get; private set; }

            private readonly Func<bool> ready;

            public Session(string url, Func<bool> ready)
            {
                this.url = url;
                this.ready = ready;
                var b = new byte[16];
                using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
                var sb = new StringBuilder(32);
                foreach (byte x in b) sb.Append(x.ToString("x2"));
                token = sb.ToString();
            }

            public bool IsAlive { get { lock (sync) return alive; } }
            public bool WasServed { get { lock (sync) return served; } }

            public void Start()
            {
                listener = new TcpListener(IPAddress.Loopback, 0);   // the OS picks a free port
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                HopUrl = "http://127.0.0.1:" + port + "/" + token;
                lock (sync) alive = true;
                timer = new Timer(_ => { try { End(); } catch (Exception) { } }, null, WaitMs, Timeout.Infinite);
                var t = new Thread(AcceptLoop) { IsBackground = true, Name = "KippixLinks handoff" };
                t.Start();
            }

            public void End()
            {
                TcpListener l;
                lock (sync)
                {
                    alive = false;
                    url = null;   // the address doesn't stay in memory here
                    l = listener;
                    listener = null;
                    if (timer != null) { try { timer.Dispose(); } catch (Exception) { } timer = null; }
                }
                if (l != null) { try { l.Stop(); } catch (Exception) { } }
            }

            private void AcceptLoop()
            {
                try
                {
                    while (true)
                    {
                        TcpListener l;
                        lock (sync) l = listener;
                        if (l == null) return;
                        TcpClient c;
                        try { c = l.AcceptTcpClient(); }
                        catch (Exception) { return; }   // stopped
                        if (Interlocked.Increment(ref open) > MaxConnections)
                        {
                            Interlocked.Decrement(ref open);
                            try { c.Close(); } catch (Exception) { }
                            continue;
                        }
                        // each connection on its own: a browser's idle pre-connection must not hold up the real request
                        try { ThreadPool.QueueUserWorkItem(_ => { try { Serve(c); } finally { Interlocked.Decrement(ref open); } }); }
                        catch (Exception) { Interlocked.Decrement(ref open); try { c.Close(); } catch (Exception) { } }
                    }
                }
                catch (Exception ex) { SafeLog.Warn("handoff: accept loop", ex); }
            }

            private void Serve(TcpClient c)
            {
                try
                {
                    using (c)
                    {
                        c.ReceiveTimeout = SocketTimeoutMs;
                        c.SendTimeout = SocketTimeoutMs;
                        var stream = c.GetStream();
                        string line = ReadRequestLine(c, stream);
                        string answer = null;
                        if (line != null)
                        {
                            string[] parts = line.Split(' ');
                            if (parts.Length >= 2 && (parts[0] == "GET" || parts[0] == "HEAD") && SameText(parts[1], "/" + token))
                            {
                                Hold();
                                lock (sync)
                                {
                                    if (alive && url != null)
                                    {
                                        answer = "HTTP/1.1 302 Found\r\nLocation: " + url + "\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                                        if (!served)
                                        {
                                            served = true;
                                            if (timer != null) timer.Change(GraceMs, Timeout.Infinite);   // a little longer, then gone
                                        }
                                    }
                                }
                            }
                        }
                        if (answer == null) answer = "HTTP/1.1 404 Not Found\r\nCache-Control: no-store\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                        byte[] bytes = Encoding.UTF8.GetBytes(answer);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                    }
                }
                catch (Exception) { }   // a dropped or slow connection: nothing to tell anyone
            }

            /// <summary>Waits for ready() (at most MaxHoldMs, and no longer than this handoff lives).</summary>
            private void Hold()
            {
                if (ready == null) return;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < MaxHoldMs)
                {
                    bool ok;
                    try { ok = ready(); } catch (Exception) { ok = true; }
                    if (ok) return;
                    lock (sync) if (!alive) return;
                    Thread.Sleep(100);
                }
                SafeLog.Info("handoff: the hold ran out");   // answered anyway: said, so it can be seen that it happened
            }

            /// <summary>Reads the request head (up to 8 KB, ending in a blank line) and returns its first line, or null.</summary>
            private static string ReadRequestLine(TcpClient c, NetworkStream stream)
            {
                var buf = new byte[MaxRequest];
                int n = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (n < buf.Length)
                {
                    // one deadline for the whole head: a client trickling a byte at a time doesn't hold a thread for long
                    long left = SocketTimeoutMs - clock.ElapsedMilliseconds;
                    if (left <= 0) return null;
                    c.ReceiveTimeout = (int)Math.Max(1, left);
                    int r = stream.Read(buf, n, buf.Length - n);
                    if (r <= 0) break;
                    n += r;
                    if (HasBlankLine(buf, n)) break;
                }
                if (!HasBlankLine(buf, n)) return null;
                string head = Encoding.ASCII.GetString(buf, 0, n);
                int eol = head.IndexOf("\r\n", StringComparison.Ordinal);
                return eol > 0 ? head.Substring(0, eol) : null;
            }

            /// <summary>Equal, compared in time that doesn't depend on where they differ.</summary>
            private static bool SameText(string a, string b)
            {
                if (a == null || b == null || a.Length != b.Length) return false;
                int d = 0;
                for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
                return d == 0;
            }

            private static bool HasBlankLine(byte[] b, int n)
            {
                for (int i = 3; i < n; i++)
                    if (b[i - 3] == 13 && b[i - 2] == 10 && b[i - 1] == 13 && b[i] == 10) return true;
                return false;
            }
        }
    }
}
