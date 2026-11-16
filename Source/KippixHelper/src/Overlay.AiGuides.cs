using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace KippixOverlay
{
    public class AchievementGuide
    {
        public string Name { get; set; }
        public string Tip { get; set; }
        public bool Missable { get; set; }
        public string When { get; set; }
        public DateTime Created { get; set; }
    }

    public class GameGuides
    {
        public Guid GameId { get; set; }
        public string GameName { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public DateTime Created { get; set; }
        public Dictionary<string, AchievementGuide> Items { get; set; }
        /// <summary>Achievements the AI was asked about but gave no tip for, so automatic updates don't keep asking.</summary>
        public List<string> Skipped { get; set; }

        public GameGuides() { Items = new Dictionary<string, AchievementGuide>(); Skipped = new List<string>(); }
    }

    internal class GuideQuotaException : Exception
    {
        public GuideQuotaException(string message) : base(message) { }
    }

    /// <summary>Saved achievement guides: one JSON file per game in the add-on's data folder.</summary>
    internal class GuideStore
    {
        private readonly string folder;
        private readonly Dictionary<Guid, GameGuides> cache = new Dictionary<Guid, GameGuides>();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public GuideStore(string folder)
        {
            this.folder = folder;
        }

        private string FileFor(Guid gameId)
        {
            return Path.Combine(folder, gameId.ToString() + ".json");
        }

        public static string Key(string achievementName)
        {
            return Regex.Replace((achievementName ?? "").ToLowerInvariant(), "\\s+", " ").Trim();
        }

        public GameGuides Load(Guid gameId)
        {
            lock (cache)
            {
                GameGuides g;
                if (cache.TryGetValue(gameId, out g)) return g;
            }
            GameGuides loaded = null;
            try
            {
                string f = FileFor(gameId);
                if (File.Exists(f)) loaded = json.Deserialize<GameGuides>(File.ReadAllText(f));
            }
            catch (Exception) { }
            lock (cache) cache[gameId] = loaded;
            return loaded;
        }

        public bool Has(Guid gameId)
        {
            var g = Load(gameId);
            return g != null && g.Items != null && g.Items.Count > 0;
        }

        public AchievementGuide Find(Guid gameId, string achievementName)
        {
            var g = Load(gameId);
            if (g == null || g.Items == null) return null;
            AchievementGuide a;
            return g.Items.TryGetValue(Key(achievementName), out a) ? a : null;
        }

        public void Save(GameGuides g)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(FileFor(g.GameId), json.Serialize(g), Encoding.UTF8);
            lock (cache) cache[g.GameId] = g;
        }

        public int Count()
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Length : 0; }
            catch (Exception) { return 0; }
        }

        public void DeleteAll()
        {
            lock (cache) cache.Clear();
            if (!Directory.Exists(folder)) return;
            foreach (string f in Directory.GetFiles(folder, "*.json"))
            {
                try { File.Delete(f); } catch (Exception) { }
            }
        }

        /// <summary>Deletes one game's saved guides. True if there was a file.</summary>
        public bool Delete(Guid gameId)
        {
            lock (cache) cache.Remove(gameId);
            try
            {
                string f = FileFor(gameId);
                if (!File.Exists(f)) return false;
                File.Delete(f);
                return true;
            }
            catch (Exception) { return false; }
        }

        public void Forget(Guid gameId)
        {
            lock (cache) cache.Remove(gameId);
        }
    }

    internal class AiAnswer
    {
        public string Text;
        public long InputTokens;
        public long OutputTokens;
        public int SearchCalls;
        public double CostUsd;
    }

    /// <summary>Talks to OpenAI (Responses API) or Google Gemini, or hands the question to Claude Code (Overlay.ClaudeCode.cs). Blocking; call off the UI thread.</summary>
    internal static class AiClient
    {
        static AiClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch (Exception) { }
        }

        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        /// <summary>The service is busy or had a hiccup (HTTP 500/502/503/504): worth retrying.</summary>
        private class BusyException : Exception
        {
            public BusyException(string message) : base(message) { }
        }

        private class ModelMissingException : Exception
        {
            public ModelMissingException(string model) : base("Gemini doesn't know the model \"" + model + "\". Check the model name in the settings.") { }
        }

        private static readonly string[] GeminiFallbackModels = { "gemini-flash-lite-latest", "gemini-flash-latest" };

        public static AiAnswer Ask(OverlayConfig c, string prompt)
        {
            if (c.GuideProvider < 1 || c.GuideProvider > 3)
                throw new InvalidOperationException("Choose an AI provider in the add-on settings first.");
            if (c.GuideProvider == 3) return ClaudeCode.Ask(c, prompt);

            // Models to try: the chosen one, then (Gemini only) lighter ones that are often less busy.
            var models = new List<string> { c.GuideProvider == 1 ? c.OpenAiModel.Trim() : c.GeminiModel.Trim() };
            if (c.GuideProvider == 2)
                foreach (string m in GeminiFallbackModels)
                    if (!models.Contains(m, StringComparer.OrdinalIgnoreCase)) models.Add(m);

            int[] waitsMs = { 4000, 12000, 25000 };
            BusyException last = null;
            foreach (string model in models)
            {
                for (int attempt = 0; attempt <= waitsMs.Length; attempt++)
                {
                    try
                    {
                        if (c.GuideProvider == 1) return AskOpenAi(c, model, prompt, c.OpenAiWebSearch, true);
                        return AskGemini(c, model, prompt, c.GeminiWebSearch);
                    }
                    catch (BusyException ex)
                    {
                        last = ex;
                        if (attempt < waitsMs.Length) System.Threading.Thread.Sleep(waitsMs[attempt]);
                    }
                    catch (ModelMissingException ex)
                    {
                        if (model == models[0] && models.Count == 1) throw new InvalidOperationException(ex.Message);
                        break; // try the next model
                    }
                }
            }
            throw new InvalidOperationException((c.GuideProvider == 1 ? "OpenAI" : "Gemini") +
                " is too busy right now (" + (last != null ? last.Message : "no answer") + "). Try again in a few minutes.");
        }

        private static bool IsBusy(int status)
        {
            return status == 500 || status == 502 || status == 503 || status == 504;
        }

        private class HttpFailure : Exception
        {
            public readonly int Status;
            public readonly string Body;
            public HttpFailure(int status, string body, string message) : base(message) { Status = status; Body = body ?? ""; }
        }

        private static string Post(string url, Dictionary<string, string> headers, object body)
        {
            var req = WebRequest.CreateHttp(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 240000;
            req.ReadWriteTimeout = 240000;
            req.UserAgent = "Kippix (Playnite add-on)";
            foreach (var h in headers) req.Headers[h.Key] = h.Value;
            byte[] data = Encoding.UTF8.GetBytes(json.Serialize(body));
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null) throw;
                int status = (int)resp.StatusCode;   // (read before the response is closed below)
                string text = "";
                try { using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = r.ReadToEnd(); } catch (Exception) { }
                finally { resp.Dispose(); }   // (an error response holds its connection until closed)
                throw new HttpFailure(status, text, ErrorMessage(text) ?? ex.Message);
            }
        }

        private static string ErrorMessage(string body)
        {
            try
            {
                var d = json.DeserializeObject(body) as Dictionary<string, object>;
                var e = d != null && d.ContainsKey("error") ? d["error"] as Dictionary<string, object> : null;
                if (e != null && e.ContainsKey("message")) return Convert.ToString(e["message"], CultureInfo.InvariantCulture);
            }
            catch (Exception) { }
            return null;
        }

        // ---------------- OpenAI ----------------

        private static AiAnswer AskOpenAi(OverlayConfig c, string model, string prompt, bool search, bool reasoning)
        {
            var body = new Dictionary<string, object>();
            body["model"] = model;
            body["input"] = prompt;
            if (search) body["tools"] = new object[] { new Dictionary<string, object> { { "type", "web_search" } } };
            bool reasoningModel = reasoning && Regex.IsMatch(model, "^(gpt-5|o\\d)", RegexOptions.IgnoreCase);
            if (reasoningModel) body["reasoning"] = new Dictionary<string, object> { { "effort", "low" } };

            string text;
            try
            {
                text = Post("https://api.openai.com/v1/responses",
                    new Dictionary<string, string> { { "Authorization", "Bearer " + c.OpenAiKey.Trim() } }, body);
            }
            catch (HttpFailure f)
            {
                if (f.Status == 401) throw new InvalidOperationException("OpenAI didn't accept the API key.");
                if (IsBusy(f.Status)) throw new BusyException(f.Message);
                if (f.Status == 429)
                {
                    if (f.Body.Contains("insufficient_quota")) throw new GuideQuotaException("Your OpenAI credit has run out. Add credit at platform.openai.com/billing.");
                    throw new GuideQuotaException("OpenAI says you're sending too much at once. Try again in a minute.");
                }
                if (f.Status == 400 && reasoningModel && f.Body.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AskOpenAi(c, model, prompt, search, false);
                if (f.Status == 400 && search && f.Body.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AskOpenAi(c, model, prompt, false, reasoning);
                throw new InvalidOperationException("OpenAI error: " + f.Message);
            }

            var root = json.DeserializeObject(text) as Dictionary<string, object>;
            var answer = new AiAnswer();
            var sb = new StringBuilder();
            foreach (var item in Items(root, "output"))
            {
                string type = S(item, "type");
                if (type == "web_search_call") answer.SearchCalls++;
                if (type != "message") continue;
                foreach (var part in Items(item, "content"))
                    if (S(part, "type") == "output_text") sb.Append(S(part, "text"));
            }
            answer.Text = sb.ToString();
            var usage = root != null && root.ContainsKey("usage") ? root["usage"] as Dictionary<string, object> : null;
            answer.InputTokens = N(usage, "input_tokens");
            answer.OutputTokens = N(usage, "output_tokens");
            answer.CostUsd = answer.InputTokens / 1e6 * c.OpenAiInputPricePerM
                           + answer.OutputTokens / 1e6 * c.OpenAiOutputPricePerM
                           + answer.SearchCalls * c.OpenAiSearchPricePerCall;
            if (string.IsNullOrWhiteSpace(answer.Text)) throw new InvalidOperationException("OpenAI returned an empty answer.");
            return answer;
        }

        // ---------------- Gemini ----------------

        private static AiAnswer AskGemini(OverlayConfig c, string model, string prompt, bool search)
        {
            var body = new Dictionary<string, object>();
            body["contents"] = new object[]
            {
                new Dictionary<string, object>
                {
                    { "role", "user" },
                    { "parts", new object[] { new Dictionary<string, object> { { "text", prompt } } } }
                }
            };
            if (search) body["tools"] = new object[] { new Dictionary<string, object> { { "google_search", new Dictionary<string, object>() } } };
            else body["generationConfig"] = new Dictionary<string, object> { { "responseMimeType", "application/json" } };

            string text;
            try
            {
                text = Post("https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(model) + ":generateContent",
                    new Dictionary<string, string> { { "x-goog-api-key", c.GeminiKey.Trim() } }, body);
            }
            catch (HttpFailure f)
            {
                if (f.Status == 400 && f.Body.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Gemini didn't accept the API key.");
                if (IsBusy(f.Status)) throw new BusyException(f.Message);
                if (f.Status == 429 && f.Body.IndexOf("PerMinute", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new BusyException("free per-minute limit, waiting");
                if (f.Status == 403 && !search) throw new InvalidOperationException("Gemini refused the request: " + f.Message);
                if (f.Status == 429 && search)
                {
                    // Free search allowance used up (or not offered): carry on without it.
                    try { return AskGemini(c, model, prompt, false); }
                    catch (GuideQuotaException) { throw; }
                }
                if (f.Status == 429) throw new GuideQuotaException("Gemini's free daily limit is used up. It resets at around 8am UK time; run the download again then.");
                if ((f.Status == 400 || f.Status == 403) && search) return AskGemini(c, model, prompt, false);
                if (f.Status == 404) throw new ModelMissingException(model);
                throw new InvalidOperationException("Gemini error: " + f.Message);
            }

            var root = json.DeserializeObject(text) as Dictionary<string, object>;
            var sb = new StringBuilder();
            var answer = new AiAnswer();
            foreach (var cand in Items(root, "candidates").Take(1))
            {
                var content = cand.ContainsKey("content") ? cand["content"] as Dictionary<string, object> : null;
                foreach (var part in Items(content, "parts")) sb.Append(S(part, "text"));
                if (cand.ContainsKey("groundingMetadata")) answer.SearchCalls = 1;
            }
            answer.Text = sb.ToString();
            var usage = root != null && root.ContainsKey("usageMetadata") ? root["usageMetadata"] as Dictionary<string, object> : null;
            answer.InputTokens = N(usage, "promptTokenCount");
            answer.OutputTokens = N(usage, "candidatesTokenCount");
            if (string.IsNullOrWhiteSpace(answer.Text)) throw new InvalidOperationException("Gemini returned an empty answer.");
            return answer;
        }

        // ---------------- JSON helpers ----------------

        private static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null || v is string) yield break;
            var e = v as IEnumerable;
            if (e == null) yield break;
            foreach (object o in e)
            {
                var x = o as Dictionary<string, object>;
                if (x != null) yield return x;
            }
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

        /// <summary>A tiny request to check the key works.</summary>
        public static string Test(OverlayConfig c)
        {
            var copy = new OverlayConfig
            {
                GuideProvider = c.GuideProvider,
                OpenAiKey = c.OpenAiKey,
                OpenAiModel = c.OpenAiModel,
                OpenAiWebSearch = false,
                GeminiKey = c.GeminiKey,
                GeminiModel = c.GeminiModel,
                GeminiWebSearch = false,
                ClaudeCodePath = c.ClaudeCodePath,
                ClaudeCodeModel = c.ClaudeCodeModel,
                ClaudeCodeWebSearch = false
            };
            var a = Ask(copy, "Reply with the single word OK.");
            return (a.Text ?? "").Trim();
        }
    }

    /// <summary>Builds the prompt, asks the AI in batches, and saves tips for every achievement in a game.</summary>
    internal static class GuideWriter
    {
        private const int BatchSize = 30;
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        private static string Prompt(string game, string platform, List<AchievementInfo> batch, bool search)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You write short, accurate video game achievement guides.");
            sb.AppendLine("Game: \"" + game + "\"" + (string.IsNullOrEmpty(platform) ? "" : " (" + platform + ")") + ".");
            sb.AppendLine("For each achievement below, explain how to unlock it.");
            if (search) sb.AppendLine("Use web search to check real guides (TrueAchievements, PowerPyx, PSNProfiles, Steam guides, game wikis) so the tips are correct.");
            sb.AppendLine("Rules:");
            sb.AppendLine("- 1 to 3 sentences, at most 70 words, plain text, no markdown, no links.");
            sb.AppendLine("- Be specific: where, when (chapter, level, in-game date), and what to do.");
            sb.AppendLine("- \"missable\": true only if it can be permanently missed in a playthrough.");
            sb.AppendLine("- \"when\": a very short phrase for when it's usually earned, e.g. \"Story, chapter 3\", \"Post-game\", \"Any time\".");
            sb.AppendLine("- If you are not sure, say so briefly instead of guessing.");
            sb.AppendLine("- Spoilers are fine; the player chooses to reveal them.");
            sb.AppendLine("Reply with ONLY this JSON and nothing else:");
            sb.AppendLine("{\"guides\":[{\"id\":1,\"tip\":\"...\",\"missable\":false,\"when\":\"...\"}]}");
            sb.AppendLine("Include one entry for every id.");
            sb.AppendLine();
            sb.AppendLine("Achievements:");
            for (int i = 0; i < batch.Count; i++)
            {
                var a = batch[i];
                sb.Append(i + 1).Append(". ").Append(a.Name);
                if (!string.IsNullOrEmpty(a.Description)) sb.Append(" \u2014 ").Append(a.Description);
                if (a.Hidden) sb.Append(" (hidden)");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static List<Dictionary<string, object>> ParseGuides(string text)
        {
            string t = (text ?? "").Trim();
            t = Regex.Replace(t, "^```(json)?", "", RegexOptions.IgnoreCase).Trim();
            if (t.EndsWith("```")) t = t.Substring(0, t.Length - 3);
            int start = t.IndexOf('{');
            int end = t.LastIndexOf('}');
            if (start < 0 || end <= start) throw new InvalidOperationException("The AI's answer wasn't in the expected format.");
            string body = t.Substring(start, end - start + 1);
            var result = new List<Dictionary<string, object>>();
            // 1. as it came; 2. with the AI's usual slips put right (a key missing a quote: ,tip": / ,tip:, a comma
            // before the closing bracket); 3. one guide at a time, skipping any that still can't be read
            foreach (string attempt in new[] { body, RepairJson(body) })
            {
                try
                {
                    var root = json.DeserializeObject(attempt) as Dictionary<string, object>;
                    object g;
                    if (root == null || !root.TryGetValue("guides", out g) || !(g is IEnumerable)) return result;
                    foreach (object o in (IEnumerable)g)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d != null) result.Add(d);
                    }
                    return result;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            int skipped = 0;
            foreach (Match m in Regex.Matches(RepairJson(body), "\\{[^{}]*\\}"))
            {
                try
                {
                    var d = json.DeserializeObject(m.Value) as Dictionary<string, object>;
                    if (d != null && d.ContainsKey("id")) result.Add(d); else skipped++;
                }
                catch (Exception) { skipped++; }
            }
            if (result.Count == 0) throw new InvalidOperationException("The AI's answer wasn't in the expected format.");
            if (skipped > 0) Playnite.SDK.LogManager.GetLogger().Warn("Achievement guides: " + skipped + " tip(s) in the AI's answer couldn't be read and were skipped");
            return result;
        }

        /// <summary>The AI's usual JSON slips: our keys missing a quote ({"id":3,tip":… or ,tip:…; only id/tip/missable/when/guides, so a tip's own words are left alone), commas before a closing bracket.</summary>
        private static string RepairJson(string s)
        {
            s = Regex.Replace(s, "([\\{,]\\s*)\"?(id|tip|missable|when|guides)[^\"{}:,\\s]{0,3}\"?\\s*:", "$1\"$2\":");
            s = Regex.Replace(s, ",\\s*([\\]}])", "$1");
            return s;
        }

        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, "\\[([^\\]]+)\\]\\([^)]+\\)", "$1");   // markdown links
            s = Regex.Replace(s, "\\s*(\\[\\d+\\]|\\(\\d+\\))+\\s*$", "");   // trailing citation markers like [1]
            s = s.Replace("**", "").Replace("*", "").Trim();
            return s;
        }

        /// <summary>
        /// Writes tips for the given achievements (all of them, or just one when regenerating) and merges them into the store.
        /// Returns the estimated cost in USD. Stops early (and saves what it has) if cancelled or the cap would be passed.
        /// </summary>
        public static double Write(OverlayConfig c, GuideStore store, Guid gameId, string gameName, string platform,
                                   List<AchievementInfo> achievements, Func<bool> cancelled, Action<string> status, double capLeftUsd)
        {
            var existing = store.Load(gameId);
            var guides = existing ?? new GameGuides { GameId = gameId, GameName = gameName, Created = DateTime.Now };
            if (guides.Items == null) guides.Items = new Dictionary<string, AchievementGuide>();
            guides.GameName = gameName;
            guides.Provider = OverlayConfig.GuideProviderNames[c.GuideProvider];
            guides.Model = c.GuideProvider == 1 ? c.OpenAiModel : c.GuideProvider == 3 ? c.ClaudeCodeModel : c.GeminiModel;

            bool search = c.GuideProvider == 1 ? c.OpenAiWebSearch : c.GuideProvider == 3 ? c.ClaudeCodeWebSearch : c.GeminiWebSearch;
            double spent = 0;
            var list = achievements.Where(a => !string.IsNullOrEmpty(a.Name)).ToList();
            int batches = (list.Count + BatchSize - 1) / BatchSize;
            for (int b = 0; b < batches; b++)
            {
                if (cancelled != null && cancelled()) break;
                if (capLeftUsd > 0 && spent >= capLeftUsd) break;
                var batch = list.Skip(b * BatchSize).Take(BatchSize).ToList();
                if (status != null) status(batches > 1 ? "Writing tips " + (b + 1) + " of " + batches : "Writing tips");

                AiAnswer answer = AiClient.Ask(c, Prompt(gameName, platform, batch, search));
                spent += answer.CostUsd;
                var answered = new HashSet<string>();

                foreach (var d in ParseGuides(answer.Text))
                {
                    int id;
                    if (!int.TryParse(Convert.ToString(d.ContainsKey("id") ? d["id"] : null, CultureInfo.InvariantCulture), out id)) continue;
                    if (id < 1 || id > batch.Count) continue;
                    string tip = Clean(d.ContainsKey("tip") ? Convert.ToString(d["tip"], CultureInfo.InvariantCulture) : "");
                    if (tip.Length == 0) continue;
                    var a = batch[id - 1];
                    answered.Add(GuideStore.Key(a.Name));
                    guides.Items[GuideStore.Key(a.Name)] = new AchievementGuide
                    {
                        Name = a.Name,
                        Tip = tip,
                        Missable = d.ContainsKey("missable") && d["missable"] is bool && (bool)d["missable"],
                        When = Clean(d.ContainsKey("when") ? Convert.ToString(d["when"], CultureInfo.InvariantCulture) : ""),
                        Created = DateTime.Now
                    };
                }
                if (guides.Skipped == null) guides.Skipped = new List<string>();
                foreach (var a in batch)
                {
                    string k = GuideStore.Key(a.Name);
                    if (answered.Contains(k)) guides.Skipped.Remove(k);
                    else if (!guides.Skipped.Contains(k)) guides.Skipped.Add(k);
                }
                store.Save(guides);
            }
            return spent;
        }

        /// <summary>Rough cost of a game's guides with OpenAI (for the confirmation before a library download).</summary>
        public static double EstimateUsd(OverlayConfig c, int achievementCount)
        {
            if (c.GuideProvider != 1) return 0;
            int batches = Math.Max(1, (achievementCount + BatchSize - 1) / BatchSize);
            double input = batches * (achievementCount * 40.0 / batches + 600) + (c.OpenAiWebSearch ? batches * 12000 : 0);
            double output = achievementCount * 110.0 + batches * 600; // tips + reasoning
            int searches = c.OpenAiWebSearch ? batches * 4 : 0;
            return input / 1e6 * c.OpenAiInputPricePerM + output / 1e6 * c.OpenAiOutputPricePerM + searches * c.OpenAiSearchPricePerCall;
        }
    }
}
