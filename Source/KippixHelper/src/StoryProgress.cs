using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace KippixHelper
{
    /// <summary>One achievement, as the story estimate needs it (from the theme's AchItemData or the overlay's AchievementInfo).</summary>
    internal class ProgressAch
    {
        public string Name, Description;
        public bool Unlocked, Counter;
        public double? Rarity;   // % of players who have it
    }

    /// <summary>How far through a game you are, and what that's based on.</summary>
    internal class ProgressResult
    {
        public int Percent;      // 0-100
        public string Source;    // "finished", "story", "playtime", "achievements"
        public int StoryDone, StoryTotal;
        /// <summary>"Story ~60%" / "Finished" / "Achievements 40%"</summary>
        public string Short;
        /// <summary>what it's based on: "from 4 of 7 story achievements", "estimate from play time"...</summary>
        public string Basis;
        public string Line { get { return string.IsNullOrEmpty(Basis) ? Short : Short + "  ·  " + Basis; } }
    }

    /// <summary>
    /// (0.99.48) How far through a game you are, as close to a console's "x% through" as a PC can get (nothing on Windows or
    /// Steam reports real story progress). In order:
    ///  1. Finished: its completion status is Beaten / Completed (or the ones chosen in Kippix), or its ending achievement is unlocked.
    ///  2. Story achievements: the ones you can't miss along the main path (chapters, acts, "complete the story"), picked out by
    ///     their names and descriptions (counters, difficulty, collect-all, multiplayer... left out). They're put in story order
    ///     (by chapter / act number when they have one, otherwise by how many players have each: fewer players get further),
    ///     and you're placed after the furthest one you have: 4 of 7 = about 57%. Play time against HowLongToBeat's main story
    ///     moves it on between two of them (never past the next one you haven't got).
    ///  3. Play time against HowLongToBeat's main story. Only where that says something: not for a game with no main story
    ///     time (extras / completionist only), nor for one that's multiplayer only by its features, tags and categories (a
    ///     football game with 100 hours isn't "Story ~99%"), nor once the play time has reached the story time (98% at most:
    ///     past that, and all the more at several times the story time, the time played no longer says where you are).
    ///  4. The share of achievements unlocked, labelled as that (it isn't the story).
    /// Null when there's nothing to go on.
    /// </summary>
    internal static class StoryProgress
    {
        private static readonly Regex StoryWords = new Regex(
            @"\b(chapters?|acts?|episodes?|part|prologue|epilogue|finale|ending|endings|credits|story|campaign|main quest|final boss|boss|beat the game|finish|finished|complete the game|reach(ed)?|arriv(e|ed)|escap(e|ed)|defeat(ed)?|surviv(e|ed)|journey|mission)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex NotStory = new Regex(
            @"(\bhard(er|est)?\b|difficult|nightmare|insane|\bexpert\b|without|\bin under\b|\bunder \d|speedrun|no damage|deathless|new game ?\+|\bng\+|\ball\b|\bevery\b|collect|\btimes\b|\b(kill|defeat|destroy|collect|find|get|earn|win|complete|reach|survive) \d+|\d+ (enemies|kills|times|items|players|games|matches|rounds|waves|days|nights|levels|stars)\b|multiplayer|online|co-?op|ranked|\bwin\b|\bperfect|\bsecret|easter egg|photo|craft|upgrade|\bbuy\b|purchase|\bmax(imum)?\b|level \d|\bdlc\b|\bbonus\b|side quest|optional)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Ending = new Regex(
            @"(\bending\b|\bcredits\b|finish(ed)? (the )?(game|story|campaign)|complete(d)? (the )?(game|story|main story|campaign)|beat the game|final boss|\bfinale\b|\bepilogue\b|\bthe end\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Numbered = new Regex(
            @"\b(chapter|act|episode|part|mission|stage)\s+(\d{1,2}|[ivx]{1,5}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

        public static readonly string[] FinishedStatuses = { "beaten", "completed", "finished", "100%", "platinum" };

        // the game's feature / tag / category names that say it's played with others, and the ones that say it has a single-player side
        private static readonly Regex MultiLabel = new Regex(@"(multi-?player|\bonline\b|\bpvp\b|\bmmo|\bco-?op\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SoloLabel = new Regex(@"(single[\s-]?player|campaign|story|^\s*play:\s*solo\s*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The names of the game's features, tags and categories (what Estimate's labels are).</summary>
        public static List<string> LabelsOf(Playnite.SDK.Models.Game g)
        {
            var names = new List<string>();
            if (g == null) return names;
            try { if (g.Features != null) names.AddRange(g.Features.Where(x => x != null && x.Name != null).Select(x => x.Name)); } catch (Exception) { }
            try { if (g.Tags != null) names.AddRange(g.Tags.Where(x => x != null && x.Name != null).Select(x => x.Name)); } catch (Exception) { }
            try { if (g.Categories != null) names.AddRange(g.Categories.Where(x => x != null && x.Name != null).Select(x => x.Name)); } catch (Exception) { }
            return names;
        }

        /// <summary>Multiplayer only: a label says multiplayer / online / PvP / MMO / co-op and none says single player, campaign, story or "Play: Solo".</summary>
        internal static bool MultiplayerOnly(IEnumerable<string> labels)
        {
            if (labels == null) return false;
            bool multi = false;
            foreach (var l in labels)
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                if (SoloLabel.IsMatch(l)) return false;
                if (MultiLabel.IsMatch(l)) multi = true;
            }
            return multi;
        }

        public static ProgressResult Estimate(List<ProgressAch> ach, double playedSeconds, double hltbStoryHours, double hltbExtrasHours, double hltbCompleteHours, string status, IEnumerable<string> extraFinished, IEnumerable<string> labels)
        {
            string st = (status ?? "").Trim().ToLowerInvariant();
            var finishedNames = new HashSet<string>(FinishedStatuses);
            if (extraFinished != null) foreach (var s in extraFinished) if (!string.IsNullOrWhiteSpace(s)) finishedNames.Add(s.Trim().ToLowerInvariant());
            if (st.Length > 0 && finishedNames.Contains(st)) return new ProgressResult { Percent = 100, Source = "finished", Short = L("LOC_KX_StpFinished", "Finished"), Basis = L("LOC_KX_StpBasisStatus", "marked as finished") };

            double hltb = hltbStoryHours > 0 ? hltbStoryHours : hltbExtrasHours > 0 ? hltbExtrasHours : hltbCompleteHours;
            double played = playedSeconds / 3600.0;
            double? byTime = hltb > 0 && played > 0 ? (double?)(played / hltb) : null;

            // ---- story achievements ----
            var story = StoryList(ach);
            if (story != null)
            {
                int n = story.Count;
                int furthest = -1;
                for (int i = 0; i < n; i++) if (story[i].Unlocked) furthest = i;
                int done = story.Count(a => a.Unlocked);
                if (furthest == n - 1 && Ending.IsMatch(Text(story[n - 1])))
                    return new ProgressResult { Percent = 100, Source = "finished", StoryDone = done, StoryTotal = n, Short = L("LOC_KX_StpFinished", "Finished"), Basis = L("LOC_KX_StpBasisEnding", "its ending achievement is unlocked") };
                double from = (furthest + 1) / (double)(n + (Ending.IsMatch(Text(story[n - 1])) ? 0 : 1));
                double to = (furthest + 2) / (double)(n + (Ending.IsMatch(Text(story[n - 1])) ? 0 : 1));
                double p = from;
                if (byTime.HasValue) p = Math.Max(from, Math.Min(to - 0.01, byTime.Value));   // (play time moves it on between two of them)
                int pct = (int)Math.Round(Math.Max(0, Math.Min(0.99, p)) * 100);
                return new ProgressResult
                {
                    Percent = pct, Source = "story", StoryDone = furthest + 1, StoryTotal = n,
                    Short = string.Format(L("LOC_KX_StpStory", "Story ~{0}%"), pct),
                    Basis = string.Format(L("LOC_KX_StpBasisStory", "from {0} of {1} story achievements"), furthest + 1, n)
                };
            }

            // ---- play time against HowLongToBeat ----
            // (only against a main story time, and not for a multiplayer-only game: neither has a story to be part-way through;
            //  and only while the play time is short of that story time: at or past it the time says nothing about where you
            //  are (18 hours of chess against a 2 hour "story" isn't "Story ~99%"), so there's no estimate, not one pinned at 99)
            if (byTime.HasValue && hltbStoryHours > 0 && byTime.Value < 0.985 && !MultiplayerOnly(labels))
            {
                int pct = (int)Math.Round(Math.Max(0, Math.Min(0.99, byTime.Value)) * 100);
                return new ProgressResult { Percent = pct, Source = "playtime", Short = string.Format(L("LOC_KX_StpStory", "Story ~{0}%"), pct), Basis = L("LOC_KX_StpBasisTime", "estimate from play time") };
            }

            // ---- achievements unlocked ----
            if (ach != null && ach.Count > 0)
            {
                int pct = (int)Math.Round(100.0 * ach.Count(a => a.Unlocked) / ach.Count);
                return new ProgressResult { Percent = pct, Source = "achievements", Short = string.Format(L("LOC_KX_StpAch", "Achievements {0}%"), pct) };   // (says what it is: no basis after it)
            }
            return null;
        }

        /// <summary>The story achievements in story order, or null when there aren't enough to go by (three or more).</summary>
        private static List<ProgressAch> StoryList(List<ProgressAch> ach)
        {
            if (ach == null || ach.Count < 3) return null;
            var picked = ach.Where(a => a != null && !a.Counter && !string.IsNullOrWhiteSpace(a.Name))
                            .Where(a => { string t = Text(a); return StoryWords.IsMatch(t) && (!NotStory.IsMatch(t) || Ending.IsMatch(t) && !Regex.IsMatch(t, @"hard|difficult|nightmare|insane|without|under \d|speedrun|deathless|new game", RegexOptions.IgnoreCase)); })
                            .ToList();
            if (picked.Count < 3) return null;

            // numbered ones (Chapter 3, Act II) in their order; the endings after them
            var numbered = picked.Select(a => new { A = a, N = NumberOf(Text(a)) }).Where(x => x.N > 0).ToList();
            if (numbered.Select(x => x.N).Distinct().Count() >= 3)
            {
                var list = numbered.GroupBy(x => x.N).OrderBy(g => g.Key).Select(g => g.First().A).ToList();
                foreach (var e in picked.Where(a => !list.Contains(a) && Ending.IsMatch(Text(a))).OrderByDescending(a => a.Rarity ?? 0)) list.Add(e);
                return list;
            }

            // otherwise by how many players have each (fewer get further); the ones without a figure can't be placed
            var rated = picked.Where(a => a.Rarity.HasValue && a.Rarity.Value > 0).OrderByDescending(a => a.Rarity.Value).ToList();
            if (rated.Count < 3) return null;
            // an ending that more players have than the story before it (it happens): it still goes last
            var endings = rated.Where(a => Ending.IsMatch(Text(a))).ToList();
            if (endings.Count > 0) { foreach (var e in endings) rated.Remove(e); rated.AddRange(endings.OrderByDescending(a => a.Rarity.Value)); }
            return rated;
        }

        private static int NumberOf(string t)
        {
            var m = Numbered.Match(t);
            if (!m.Success) return 0;
            string v = m.Groups[2].Value.ToLowerInvariant();
            int n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            int w = Array.IndexOf(Words, v);
            if (w > 0) return w;
            return Roman(v);
        }

        private static int Roman(string s)
        {
            int total = 0, prev = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                int v = s[i] == 'i' ? 1 : s[i] == 'v' ? 5 : s[i] == 'x' ? 10 : 0;
                if (v == 0) return 0;
                total += v < prev ? -v : v;
                prev = Math.Max(prev, v);
            }
            return total;
        }

        private static string Text(ProgressAch a) { return (a.Name ?? "") + " — " + (a.Description ?? ""); }

        private static string L(string key, string fallback)
        {
            try
            {
                var app = System.Windows.Application.Current;
                var v = app != null ? app.TryFindResource(key) as string : null;
                return string.IsNullOrEmpty(v) ? fallback : v;
            }
            catch (Exception) { return fallback; }
        }
    }
}
