using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KippixOverlay
{
    /// <summary>
    /// Achievement guides written by Claude Code (GuideProvider 3): the question goes to the "claude" program already
    /// installed and signed in on this PC, so the tips come out of the user's own Claude plan and no API key is kept here.
    /// One run per question: claude -p (print and exit), the prompt on its standard input, the answer as JSON on its output.
    /// It gets no tools but web search / web fetch (none at all with search off), none of the user's own Claude Code set-up
    /// (--safe-mode: no CLAUDE.md, hooks, plug-ins or MCP servers), an empty folder to run in, and its session isn't saved.
    /// </summary>
    internal static class ClaudeCode
    {
        private const int TimeoutMs = 8 * 60 * 1000;
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        /// <summary>Where the claude program is: the path given, else on PATH, else its usual places. Null: not found.</summary>
        private static string foundFor, found;
        private static DateTime foundAt;

        public static string Find(string setting)
        {
            // (asked for whenever an achievement opens: looked up at most once a minute)
            lock (json)
            {
                if (foundFor == (setting ?? "") && (DateTime.UtcNow - foundAt).TotalSeconds < 60) return found;
            }
            string hit = FindNow(setting);
            lock (json) { foundFor = setting ?? ""; found = hit; foundAt = DateTime.UtcNow; }
            return hit;
        }

        private static string FindNow(string setting)
        {
            try
            {
                string given = (setting ?? "").Trim().Trim('"');
                if (given.Length > 0) return File.Exists(given) ? given : null;
                string[] names = { "claude.exe", "claude.cmd" };
                foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                {
                    string d = dir.Trim().Trim('"');
                    if (d.Length == 0) continue;
                    foreach (string n in names)
                    {
                        try { string f = Path.Combine(d, n); if (File.Exists(f)) return f; } catch (Exception) { }
                    }
                }
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                foreach (string f in new[] { Path.Combine(home, ".local", "bin", "claude.exe"), Path.Combine(roaming, "npm", "claude.cmd") })
                    if (File.Exists(f)) return f;
            }
            catch (Exception) { }
            return null;
        }

        public static AiAnswer Ask(OverlayConfig c, string prompt)
        {
            string exe = Find(c.ClaudeCodePath);
            if (exe == null)
                throw new InvalidOperationException("Claude Code wasn't found on this PC. Install it (claude.com/claude-code) and sign in, or give its path in the settings.");
            string model = (c.ClaudeCodeModel ?? "").Trim();
            if (!Regex.IsMatch(model, "^[A-Za-z0-9._\\-\\[\\]]{1,80}$"))
                throw new InvalidOperationException("The Claude model name in the settings isn't usable. Try: sonnet");

            string tools = c.ClaudeCodeWebSearch ? "WebSearch,WebFetch" : "";
            string args = "-p --safe-mode --output-format json --no-session-persistence --model " + model +
                          " --tools \"" + tools + "\"" + (tools.Length > 0 ? " --allowedTools \"" + tools + "\"" : "") +
                          " --system-prompt \"You answer with exactly what is asked for and nothing else.\"";

            string folder = Path.Combine(Path.GetTempPath(), "KippixClaudeCode");
            Directory.CreateDirectory(folder);

            var info = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = folder
            };
            string ext = Path.GetExtension(exe).ToLowerInvariant();
            if (ext == ".cmd" || ext == ".bat")
            {
                info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                info.Arguments = "/d /s /c \"\"" + exe + "\" " + args + "\"";
            }
            else
            {
                info.FileName = exe;
                info.Arguments = args;
            }
            // (Playnite started from inside a Claude Code session would pass these on, and the new one would think it's nested)
            foreach (string v in new[] { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT" })
                try { info.EnvironmentVariables.Remove(v); } catch (Exception) { }

            string output, errors;
            using (var p = new Process { StartInfo = info })
            {
                try { p.Start(); }
                catch (Exception ex) { throw new InvalidOperationException("Claude Code couldn't be started: " + ex.Message); }
                Task<string> readOut = p.StandardOutput.ReadToEndAsync();
                Task<string> readErr = p.StandardError.ReadToEndAsync();
                try
                {
                    byte[] data = new UTF8Encoding(false).GetBytes(prompt ?? "");
                    p.StandardInput.BaseStream.Write(data, 0, data.Length);
                    p.StandardInput.BaseStream.Flush();
                    p.StandardInput.Close();
                }
                catch (IOException) { }   // (it ended before reading: what it printed says why)
                if (!p.WaitForExit(TimeoutMs))
                {
                    try { p.Kill(); } catch (Exception) { }
                    throw new InvalidOperationException("Claude Code took too long to answer. Try again.");
                }
                p.WaitForExit();   // (the rest of its output)
                output = readOut.Wait(15000) ? readOut.Result : "";
                errors = readErr.Wait(15000) ? readErr.Result : "";
            }

            Dictionary<string, object> root = null;
            try
            {
                int start = (output ?? "").IndexOf('{');
                if (start >= 0) root = json.DeserializeObject(output.Substring(start)) as Dictionary<string, object>;
            }
            catch (Exception) { }
            string text = root != null ? S(root, "result") : null;
            bool failed = root == null || (root.ContainsKey("is_error") && root["is_error"] is bool && (bool)root["is_error"]);
            if (failed || string.IsNullOrWhiteSpace(text))
            {
                string why = (text ?? "").Trim();
                if (why.Length == 0) why = (errors ?? "").Trim();
                if (why.Length == 0) why = (output ?? "").Trim();
                if (why.Length > 300) why = why.Substring(0, 300);
                string low = why.ToLowerInvariant();
                if (low.Contains("/login") || low.Contains("api key") || low.Contains("not logged in") || low.Contains("authenticat"))
                    throw new InvalidOperationException("Claude Code isn't signed in on this PC. Open a terminal, run \"claude\" and sign in, then try again.");
                if (low.Contains("limit") || low.Contains("quota") || low.Contains("overloaded"))
                    throw new GuideQuotaException("Your Claude plan's usage limit is reached for now (" + why + "). Run the download again later; everything so far is kept.");
                throw new InvalidOperationException("Claude Code error: " + (why.Length > 0 ? why : "no answer"));
            }

            var answer = new AiAnswer { Text = text };
            var usage = root.ContainsKey("usage") ? root["usage"] as Dictionary<string, object> : null;
            answer.InputTokens = N(usage, "input_tokens");
            answer.OutputTokens = N(usage, "output_tokens");
            var served = usage != null && usage.ContainsKey("server_tool_use") ? usage["server_tool_use"] as Dictionary<string, object> : null;
            answer.SearchCalls = (int)N(served, "web_search_requests");
            // (CostUsd stays 0: it's the plan's allowance, not credit; the spending cap is OpenAI's)
            Playnite.SDK.LogManager.GetLogger().Info("Achievement guides: Claude Code (" + model + ") answered: " + text.Length + " characters, " + answer.SearchCalls + " web searches, stopped for: " + (S(root, "stop_reason") ?? "?"));
            return answer;
        }

        private static string S(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        private static long N(Dictionary<string, object> d, string key)
        {
            long n;
            return long.TryParse(S(d, key), out n) ? n : 0;
        }
    }
}
