using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace KippixOverlay
{
    public class OverlayConfig
    {
        // 0 = Guide / PS button, 1 = View + Menu (Create + Options), 2 = either
        public int TriggerMode { get; set; }
        public string OverlayHotkey { get; set; }
        public bool SteamButtonOpens { get; set; }  // the Steam Controller's Steam button (read through Playnite) opens it too   // also opens it (e.g. a Steam Controller button bound to it in Steam Input)
        public bool OnlyInFullscreen { get; set; }
        public bool ConfirmClose { get; set; }
        public bool OpenVibration { get; set; }     // (0.99.30) a short buzz on the controller when the overlay opens
        public bool HoldGuidePower { get; set; }    // (0.99.49) hold the Guide button in a game: Power off / Hibernate (Overlay.PowerMenu.cs)
        public int VibrationStrength { get; set; }  // (0.99.35) 0 soft (the default), 1 medium, 2 strong
        public static readonly string[] VibrationNames = { "Soft", "Medium", "Strong" };
        public bool OpenSound { get; set; }         // (0.99.37) a light sound when the overlay opens and closes

        // (0.99.37) Kippix's own achievement pop-ups (Playnite Achievements' unlocks, PS5 style: Overlay.AchievementToasts.cs)
        public bool AchToasts { get; set; }          // on / off
        public bool AchHideOther { get; set; }       // Playnite Achievements' own pop-ups off while these are on
        public int AchToastPosition { get; set; }    // AchToastWindow.PositionNames: 0 top right (as on a PS5) ...
        public int AchToastSeconds { get; set; }     // how long one stays (3-12 s)
        public bool AchToastProgress { get; set; }   // progress updates too (5 / 10 kills...)
        public int AchToastStyle { get; set; }       // (0.99.38) AchToastWindow.StyleNames: 0 Classic ... 6 Kippix
        public string AchSoundFile { get; set; }     // (0.99.38) your own unlock sound (a copy in the overlay's data folder); "" = none
        public int AchSoundVolume { get; set; }      // (0.99.38) 0-100
        public string AchSoundName { get; set; }     // (0.99.38) the chosen file's own name (shown in the settings)
        public bool ScreenshotSound { get; set; }   // (0.99.30) a soft chime when a screenshot is saved
        // Show the overlay without taking focus from the game (stops fullscreen games dropping out with a black flicker).
        // Since r11b only follows WhenOpened (== 1), kept so older files carry over.
        public bool KeepGameFocused { get; set; }

        // What happens to the game when the overlay opens (r11b). The overlay never suspends, injects into or reads the game:
        //  0 Pause the game: the overlay takes the focus (the old default), so a game that pauses when you switch away pauses;
        //  1 Let it keep running: the game keeps the focus and nothing is done to it (the overlay only shows on top);
        //  2 Automatic: 1 for a game Playnite lists as online multiplayer / MMO, else 0.
        public int WhenOpened { get; set; }
        public static readonly string[] WhenOpenedNames = { "Pause the game", "Let it keep running", "Automatic (online games keep running)" };

        // 0 built-in, 1 Xbox Game Bar, 2 Steam, 3 NVIDIA, 4 custom
        public int ScreenshotMethod { get; set; }
        public string CustomScreenshotHotkey { get; set; }
        public string ScreenshotFolder { get; set; }

        // Save recent gameplay: 0 NVIDIA Instant Replay, 1 Xbox Game Bar, 2 custom hotkey, 3 off
        public int ReplayMethod { get; set; }
        public string CustomReplayHotkey { get; set; }

        public string DiscordMuteHotkey { get; set; }
        public string DiscordDeafenHotkey { get; set; }
        public bool MicButtonMutesDiscord { get; set; }
        public bool MaximizeDiscord { get; set; }

        public int Theme { get; set; }
        // 1: the colour list starts with the theme's own colours (0.29; shown as "Kippix", stored as index 0). Older files are moved onto it once.
        public int ThemeVersion { get; set; }

        // Discord in the overlay (your own app from discord.com/developers)
        public string DiscordClientId { get; set; }
        public string DiscordClientSecret { get; set; }
        public List<string> QuickReplies { get; set; }
        public bool ShowDiscordMessages { get; set; }
        public bool ShowHowLongToBeat { get; set; }
        public bool ShowStoryProgress { get; set; }   // (0.99.48) "Story ~60%" under HowLongToBeat on the Now playing tile

        // Achievement guides (AI)
        public int GuideProvider { get; set; }          // 0 off, 1 OpenAI, 2 Gemini, 3 Claude Code
        public string OpenAiKey { get; set; }
        public string OpenAiModel { get; set; }
        public bool OpenAiWebSearch { get; set; }
        public string GeminiKey { get; set; }
        public string GeminiModel { get; set; }
        public bool GeminiWebSearch { get; set; }
        public string ClaudeCodePath { get; set; }      // claude.exe; empty: looked for (PATH and its usual places)
        public string ClaudeCodeModel { get; set; }
        public bool ClaudeCodeWebSearch { get; set; }
        public bool AutoDownloadGuides { get; set; }
        public bool UpdateGuidesOnStartup { get; set; }     // look for new games when Playnite opens
        public bool GuideUpdateInstalledOnly { get; set; }  // that update (and the overlay's) only covers installed games
        public double GuideSpendCapUsd { get; set; }    // per download run, 0 = no cap
        public double GuideSpentUsd { get; set; }       // running total (estimate)
        public long GuideAutoPausedUntil { get; set; }  // (0.99.40) UTC ticks: the AI's limit was hit, so the automatic guide updates wait until then
        public double OpenAiInputPricePerM { get; set; }
        public double OpenAiOutputPricePerM { get; set; }
        public double OpenAiSearchPricePerCall { get; set; }

        public OverlayConfig()
        {
            TriggerMode = 2;
            OverlayHotkey = "Ctrl+Shift+O";
            SteamButtonOpens = true;
            OnlyInFullscreen = false;
            ConfirmClose = true;
            OpenVibration = true;
            HoldGuidePower = true;
            ScreenshotSound = true;
            OpenSound = true;
            AchToasts = true;
            AchHideOther = true;
            AchToastPosition = 0;
            AchToastSeconds = 6;
            AchToastProgress = false;
            AchToastStyle = 0;
            AchSoundFile = "";
            AchSoundVolume = 70;
            AchSoundName = "";
            ScreenshotMethod = 0;
            CustomScreenshotHotkey = "F12";
            ScreenshotFolder = DefaultScreenshotFolder;
            ReplayMethod = 0;
            CustomReplayHotkey = "Alt+F10";
            DiscordMuteHotkey = "Ctrl+Shift+M";
            DiscordDeafenHotkey = "Ctrl+Shift+D";
            MicButtonMutesDiscord = true;
            MaximizeDiscord = true;
            Theme = 0;
            ThemeVersion = 1;
            DiscordClientId = "";
            DiscordClientSecret = "";
            QuickReplies = new List<string> { "brb", "On my way!", "In a game, I'll reply after", "GG!", "Want to join my call?" };
            ShowDiscordMessages = true;
            ShowHowLongToBeat = true;
            ShowStoryProgress = true;
            GuideProvider = 0;
            OpenAiKey = "";
            OpenAiModel = "gpt-5-mini";
            OpenAiWebSearch = true;
            GeminiKey = "";
            GeminiModel = "gemini-flash-latest";
            GeminiWebSearch = true;
            ClaudeCodePath = "";
            ClaudeCodeModel = "sonnet";
            ClaudeCodeWebSearch = true;
            AutoDownloadGuides = true;
            UpdateGuidesOnStartup = true;
            GuideUpdateInstalledOnly = true;
            GuideSpendCapUsd = 2.0;
            GuideSpentUsd = 0;
            OpenAiInputPricePerM = 0.25;
            OpenAiOutputPricePerM = 2.00;
            OpenAiSearchPricePerCall = 0.01;
        }

        public static readonly string[] GuideProviderNames = { "Off", "OpenAI", "Gemini (free)", "Claude Code (your Claude plan)" };

        public bool GuidesReady
        {
            get
            {
                if (GuideProvider == 1) return !string.IsNullOrWhiteSpace(OpenAiKey);
                if (GuideProvider == 2) return !string.IsNullOrWhiteSpace(GeminiKey);
                if (GuideProvider == 3) return ClaudeCode.Find(ClaudeCodePath) != null;
                return false;
            }
        }

        public static string DefaultScreenshotFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PlayniteGame"); }
        }

        public static readonly string[] TriggerNames = { "Home button", "View + Menu", "Home or View + Menu" };
        public static readonly string[] ScreenshotNames = { "Built-in", "Xbox Game Bar", "Steam", "NVIDIA App", "Custom hotkey" };

        public static readonly string[] ReplayNames = { "NVIDIA Instant Replay", "Xbox Game Bar", "Custom hotkey", "Off" };

        /// <summary>The "save the last few minutes" hotkey, or null when turned off.</summary>
        public string ReplayHotkey()
        {
            switch (ReplayMethod)
            {
                case 0: return "Alt+F10";
                case 1: return "Win+Alt+G";
                case 2: return string.IsNullOrWhiteSpace(CustomReplayHotkey) ? null : CustomReplayHotkey;
                default: return null;
            }
        }

        /// <summary>Null means "use the built-in capture".</summary>
        public string ScreenshotHotkey()
        {
            switch (ScreenshotMethod)
            {
                case 1: return "Win+Alt+PrintScreen";
                case 2: return "F12";
                case 3: return "Alt+F1";
                case 4: return CustomScreenshotHotkey;
                default: return null;
            }
        }

        public void Clamp()
        {
            TriggerMode = Wrap(TriggerMode, TriggerNames.Length);
            WhenOpened = Wrap(WhenOpened, WhenOpenedNames.Length);
            KeepGameFocused = WhenOpened == 1;
            ScreenshotMethod = Wrap(ScreenshotMethod, ScreenshotNames.Length);
            ReplayMethod = Wrap(ReplayMethod, ReplayNames.Length);
            if (CustomReplayHotkey == null) CustomReplayHotkey = "Alt+F10";
            Theme = Wrap(Theme, OverlayTheme.All.Length);
            AchToastStyle = Wrap(AchToastStyle, AchToastWindow.StyleNames.Length);
            AchToastPosition = Wrap(AchToastPosition, AchToastWindow.PositionNames.Length);
            if (AchSoundFile == null) AchSoundFile = "";
            if (AchSoundName == null) AchSoundName = "";
            AchSoundVolume = Math.Max(0, Math.Min(100, AchSoundVolume));
            if (string.IsNullOrWhiteSpace(ScreenshotFolder)) ScreenshotFolder = DefaultScreenshotFolder;
            if (QuickReplies == null) QuickReplies = new List<string>();
            QuickReplies.RemoveAll(string.IsNullOrWhiteSpace);
            if (DiscordClientId == null) DiscordClientId = "";
            if (DiscordClientSecret == null) DiscordClientSecret = "";
            if (GuideProvider < 0 || GuideProvider > 3) GuideProvider = 0;
            if (OpenAiKey == null) OpenAiKey = "";
            if (GeminiKey == null) GeminiKey = "";
            if (ClaudeCodePath == null) ClaudeCodePath = "";
            if (string.IsNullOrWhiteSpace(ClaudeCodeModel)) ClaudeCodeModel = "sonnet";
            if (string.IsNullOrWhiteSpace(OpenAiModel)) OpenAiModel = "gpt-5-mini";
            if (string.IsNullOrWhiteSpace(GeminiModel)) GeminiModel = "gemini-flash-latest";
            if (OpenAiInputPricePerM <= 0) OpenAiInputPricePerM = 0.25;
            if (OpenAiOutputPricePerM <= 0) OpenAiOutputPricePerM = 2.00;
            if (OpenAiSearchPricePerCall < 0) OpenAiSearchPricePerCall = 0.01;
            if (GuideSpendCapUsd < 0) GuideSpendCapUsd = 0;
        }

        public static int Wrap(int v, int count)
        {
            return ((v % count) + count) % count;
        }

        /// <summary>Like Load, but null when the file exists and couldn't be read (tries a few times: it may be being written).</summary>
        public static OverlayConfig TryLoad(string path)
        {
            if (!File.Exists(path)) return new OverlayConfig();
            for (int i = 0; i < 4; i++)
            {
                var c = LoadOrNull(path);
                if (c != null) return c;
                System.Threading.Thread.Sleep(60);
            }
            return null;
        }

        public static OverlayConfig Load(string path)
        {
            return LoadOrNull(path) ?? new OverlayConfig();
        }

        private static OverlayConfig LoadOrNull(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    var js = new JavaScriptSerializer();
                    var c = js.Deserialize<OverlayConfig>(text);
                    if (c != null)
                    {
                        // from before the theme's own colours: use them (the fullscreen theme's look) by default
                        var raw = js.DeserializeObject(text) as Dictionary<string, object>;
                        object tv;
                        int v = 0;
                        if (raw != null && raw.TryGetValue("ThemeVersion", out tv) && tv != null) { try { v = Convert.ToInt32(tv); } catch (Exception) { } }
                        if (v < 1) c.Theme = 0;
                        // from before WhenOpened: "Keep the game focused" on is Let it keep running
                        if (raw != null && !raw.ContainsKey("WhenOpened") && c.KeepGameFocused) c.WhenOpened = 1;
                        c.ThemeVersion = 1;
                        c.Clamp();
                        return c;
                    }
                }
                else return new OverlayConfig();
            }
            catch (Exception) { }
            return null;
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, PrettyJson(new JavaScriptSerializer().Serialize(this)));
            }
            catch (Exception) { }
        }

        // JavaScriptSerializer writes one long line; make the file friendly to hand-edit.
        internal static string PrettyJson(string json)
        {
            var sb = new StringBuilder();
            bool inString = false, escaped = false;
            int indent = 0;
            foreach (char c in json)
            {
                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; sb.Append(c); }
                else if (c == '{') { indent++; sb.Append("{\r\n").Append(new string(' ', indent * 2)); }
                else if (c == '}') { indent--; sb.Append("\r\n").Append(new string(' ', indent * 2)).Append('}'); }
                else if (c == '[') { indent++; sb.Append("[\r\n").Append(new string(' ', indent * 2)); }
                else if (c == ']') { indent--; sb.Append("\r\n").Append(new string(' ', indent * 2)).Append(']'); }
                else if (c == ',') sb.Append(",\r\n").Append(new string(' ', indent * 2));
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
