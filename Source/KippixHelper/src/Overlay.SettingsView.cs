using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Playnite.SDK;
using Ui = KippixHelper.SettingsUi;

namespace KippixOverlay
{
    /// <summary>Settings shown in Playnite desktop mode (Add-ons \u2192 Extension settings).</summary>
    public class GuideSettings : ISettings
    {
        private readonly OverlayPlugin plugin;

        public int Provider;
        public string OpenAiKey, OpenAiModel, GeminiKey, GeminiModel, ClaudePath, ClaudeModel;
        public bool OpenAiWebSearch, GeminiWebSearch, ClaudeWebSearch, AutoDownload, UpdateOnStartup, InstalledOnly;
        public string Cap;

        public GuideSettings(OverlayPlugin plugin)
        {
            this.plugin = plugin;
            BeginEdit();
        }

        public void BeginEdit()
        {
            var c = plugin.ReloadConfig();
            Provider = c.GuideProvider;
            OpenAiKey = c.OpenAiKey;
            OpenAiModel = c.OpenAiModel;
            OpenAiWebSearch = c.OpenAiWebSearch;
            GeminiKey = c.GeminiKey;
            GeminiModel = c.GeminiModel;
            GeminiWebSearch = c.GeminiWebSearch;
            ClaudePath = c.ClaudeCodePath;
            ClaudeModel = c.ClaudeCodeModel;
            ClaudeWebSearch = c.ClaudeCodeWebSearch;
            AutoDownload = c.AutoDownloadGuides;
            UpdateOnStartup = c.UpdateGuidesOnStartup;
            InstalledOnly = c.GuideUpdateInstalledOnly;
            Cap = c.GuideSpendCapUsd.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public void CancelEdit() { }

        public void EndEdit()
        {
            Apply();
        }

        /// <summary>Copies the edited values into the live config and saves it.</summary>
        public void Apply()
        {
            var c = plugin.ReloadConfig();
            c.GuideProvider = Provider;
            c.OpenAiKey = (OpenAiKey ?? "").Trim();
            c.OpenAiModel = (OpenAiModel ?? "").Trim();
            c.OpenAiWebSearch = OpenAiWebSearch;
            c.GeminiKey = (GeminiKey ?? "").Trim();
            c.GeminiModel = (GeminiModel ?? "").Trim();
            c.GeminiWebSearch = GeminiWebSearch;
            c.ClaudeCodePath = (ClaudePath ?? "").Trim().Trim('"');
            c.ClaudeCodeModel = (ClaudeModel ?? "").Trim();
            c.ClaudeCodeWebSearch = ClaudeWebSearch;
            c.AutoDownloadGuides = AutoDownload;
            c.UpdateGuidesOnStartup = UpdateOnStartup;
            c.GuideUpdateInstalledOnly = InstalledOnly;
            double cap;
            if (double.TryParse((Cap ?? "").Replace("$", "").Replace("\u00A3", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out cap) && cap >= 0)
                c.GuideSpendCapUsd = cap;
            plugin.SaveConfig();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            double cap;
            if (!double.TryParse((Cap ?? "").Replace("$", "").Replace("\u00A3", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out cap) || cap < 0)
                errors.Add("The spending cap must be a number, for example 2 or 2.50 (0 means no cap).");
            if (Provider == 1 && string.IsNullOrWhiteSpace(OpenAiKey)) errors.Add("Add your OpenAI API key, or set the provider to Off.");
            if (Provider == 2 && string.IsNullOrWhiteSpace(GeminiKey)) errors.Add("Add your Gemini API key, or set the provider to Off.");
            if (Provider == 3 && ClaudeCode.Find(ClaudePath) == null) errors.Add("Claude Code wasn't found. Install it and sign in, give the path to claude.exe, or set the provider to Off.");
            return errors.Count == 0;
        }
    }

    /// <summary>The settings page itself, built in code.</summary>
    public class GuideSettingsView : UserControl
    {
        private readonly OverlayPlugin plugin;
        private readonly GuideSettings s;
        private StackPanel openAiPanel, geminiPanel, claudePanel;
        private TextBlock status, testResult, fileNote;

        public GuideSettingsView(OverlayPlugin plugin, GuideSettings settings)
        {
            this.plugin = plugin;
            s = settings;

            var page = Ui.Page("Achievement guides",
                "Kippix can download a short \"how to unlock\" tip for every achievement in your games and save it on this PC. " +
                "The overlay then shows the tip instantly in each achievement. Tips are written by AI, so they can occasionally be wrong; you can regenerate any tip from the overlay.");
            Content = page.Scroller;

            // ---- AI provider ----
            var ai = Ui.Card("AI provider", "Keys are kept in the overlay's settings on this PC and only sent to the provider you pick.");
            var providerBox = Ui.Combo(OverlayConfig.GuideProviderNames, s.Provider, v => { s.Provider = v; UpdatePanels(); });
            ai.Add(Ui.Row("Provider", null, providerBox));

            TextBox unused;
            openAiPanel = new StackPanel();
            openAiPanel.Children.Add(Ui.Help("Paid, best results. Uses prepaid credit on your OpenAI developer account (separate from ChatGPT). A game usually costs a few pence."));
            openAiPanel.Children.Add(new Border { Height = 8 });
            openAiPanel.Children.Add(Ui.LinkLine("Create an API key: platform.openai.com/api-keys", "https://platform.openai.com/api-keys"));
            openAiPanel.Children.Add(Ui.LinkLine("Add credit: platform.openai.com/settings/organization/billing", "https://platform.openai.com/settings/organization/billing/overview"));
            openAiPanel.Children.Add(new Border { Height = 6 });
            openAiPanel.Children.Add(Ui.Row("API key", null, Ui.Secret(s.OpenAiKey, v => s.OpenAiKey = v, 380, out unused, "Paste your OpenAI key")));
            openAiPanel.Children.Add(Ui.Row("Model", null, Ui.TextField(s.OpenAiModel, v => s.OpenAiModel = v, 260, null)));
            openAiPanel.Children.Add(Ui.CheckRow("Let the AI search the web while writing tips", "Much more accurate, small extra cost.", s.OpenAiWebSearch, v => s.OpenAiWebSearch = v));
            ai.Add(openAiPanel);

            geminiPanel = new StackPanel();
            geminiPanel.Children.Add(Ui.Help("Free with a daily limit (it resets each morning; big libraries take a few days). No card needed. Google may use free-tier requests to improve its products."));
            geminiPanel.Children.Add(new Border { Height = 8 });
            geminiPanel.Children.Add(Ui.LinkLine("Create a free key: aistudio.google.com/apikey", "https://aistudio.google.com/apikey"));
            geminiPanel.Children.Add(new Border { Height = 6 });
            geminiPanel.Children.Add(Ui.Row("API key", null, Ui.Secret(s.GeminiKey, v => s.GeminiKey = v, 380, out unused, "Paste your Gemini key")));
            geminiPanel.Children.Add(Ui.Row("Model", null, Ui.TextField(s.GeminiModel, v => s.GeminiModel = v, 260, null)));
            geminiPanel.Children.Add(Ui.CheckRow("Use Google Search while writing tips", "Skipped automatically if your free tier doesn't include it.", s.GeminiWebSearch, v => s.GeminiWebSearch = v));
            ai.Add(geminiPanel);

            claudePanel = new StackPanel();
            claudePanel.Children.Add(Ui.Help("No API key: the tips are written by Claude Code, the program from Anthropic, already installed and signed in on this PC, and count against your Claude plan's usage. It gets web search only, none of your own Claude Code set-up, and nothing else on this PC."));
            claudePanel.Children.Add(new Border { Height = 8 });
            claudePanel.Children.Add(Ui.LinkLine("Get Claude Code: claude.com/claude-code", "https://claude.com/claude-code"));
            claudePanel.Children.Add(new Border { Height = 6 });
            claudePanel.Children.Add(Ui.Row("Model", Ui.Help("sonnet, haiku, opus, or a full model name."), Ui.TextField(s.ClaudeModel, v => s.ClaudeModel = v, 260, null)));
            claudePanel.Children.Add(Ui.Row("Where claude.exe is", Ui.Help("Leave empty to have it found."), Ui.TextField(s.ClaudePath, v => s.ClaudePath = v, 380, null)));
            claudePanel.Children.Add(Ui.CheckRow("Let Claude search the web while writing tips", "Much more accurate; uses more of your plan's allowance.", s.ClaudeWebSearch, v => s.ClaudeWebSearch = v));
            ai.Add(claudePanel);

            var testRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
            var testButton = Ui.MakeButton("Test", TestKey);
            testResult = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
            testRow.Children.Add(testButton);
            testRow.Children.Add(testResult);
            ai.Add(testRow);
            page.Add(ai.Card);

            // ---- Downloading ----
            var dl = Ui.Card("Downloading", "Tip: you can also right-click games in your library and choose Kippix \u2192 Download achievement guides.");
            dl.Add(Ui.CheckRow("Download guides automatically when I start a game that doesn't have them yet", null, s.AutoDownload, v => s.AutoDownload = v));
            dl.Add(Ui.CheckRow("Check for new games when Playnite opens", "New games (or new achievements) get their guides downloaded in the background.", s.UpdateOnStartup, v => s.UpdateOnStartup = v));
            dl.Add(Ui.CheckRow("Only installed games for that check", "Also for \"Update achievement guides\" in the overlay's Settings tab.", s.InstalledOnly, v => s.InstalledOnly = v));
            dl.Add(Ui.Row("Spending cap per run", Ui.Help("US$ per download run; 0 means no cap."), Ui.TextField(s.Cap, v => s.Cap = v, 90, null)));
            dl.Add(Ui.Buttons(
                Ui.MakeButton("Download for all games", () => { s.Apply(); plugin.DownloadGuidesForLibrary(false); RefreshStatus(); }),
                Ui.MakeButton("Download for selected games", () => { s.Apply(); plugin.DownloadGuidesForSelected(false); RefreshStatus(); }),
                Ui.MakeButton("Redownload selected games", () => { s.Apply(); plugin.DownloadGuidesForSelected(true); RefreshStatus(); }),
                Ui.MakeButton("Delete all guides\u2026", () =>
                {
                    if (plugin.Api.Dialogs.ShowMessage("Delete every downloaded achievement guide?", "Kippix", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        plugin.Guides.DeleteAll();
                        RefreshStatus();
                    }
                })));
            status = new TextBlock { Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            dl.Add(status);
            page.Add(dl.Card);

            // ---- Fill in guides yourself ----
            var fill = Ui.Card("Fill in guides yourself (e.g. with ChatGPT)",
                "Export a list of achievements, upload it to ChatGPT (or fill it in yourself), then import the filled-in file. A ready-to-paste ChatGPT message is copied to your clipboard " +
                "when you export. Imported tips replace any saved ones for the same achievements. No AI key is needed for this.");
            var scopeBox = Ui.Combo(new[] { "All games", "Installed games only", "Games selected in the library" }, 1, v => { });
            fill.Add(Ui.Row("Games", null, scopeBox));
            var whichBox = Ui.Combo(new[] { "Only achievements without a tip", "Every achievement" }, 0, v => { });
            fill.Add(Ui.Row("Achievements", null, whichBox));
            fill.Add(Ui.Buttons(
                Ui.MakeButton("Export achievement list\u2026", () => { s.Apply(); plugin.ExportGuideFile(scopeBox.SelectedIndex, whichBox.SelectedIndex == 0); }),
                Ui.MakeButton("Import filled-in file\u2026", () => { plugin.ImportGuideFile(); RefreshStatus(); }),
                Ui.MakeButton("Copy ChatGPT message", () =>
                {
                    try { Clipboard.SetText(GuideFiles.ChatPrompt); fileNote.Text = "Copied. Paste it into ChatGPT along with the exported file."; }
                    catch (Exception) { fileNote.Text = "Couldn't reach the clipboard; try again."; }
                })));
            fileNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 8) };
            fill.Add(fileNote);
            page.Add(fill.Card);

            page.Add(Ui.Help("Everything else for the overlay is on the In-game overlay tab, or in the overlay itself: open it in a game (View + Menu, the Steam button or Ctrl+Shift+O) \u2192 Settings."));

            UpdatePanels();
            RefreshStatus();
        }

        private void UpdatePanels()
        {
            openAiPanel.Visibility = s.Provider == 1 ? Visibility.Visible : Visibility.Collapsed;
            geminiPanel.Visibility = s.Provider == 2 ? Visibility.Visible : Visibility.Collapsed;
            claudePanel.Visibility = s.Provider == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshStatus()
        {
            var c = plugin.ReloadConfig();
            status.Text = "Guides saved for " + plugin.Guides.Count() + " games." +
                (c.GuideSpentUsd > 0 ? "  Estimated OpenAI spend so far: $" + c.GuideSpentUsd.ToString("0.00", CultureInfo.InvariantCulture) + "." : "");
        }

        private void TestKey()
        {
            s.Apply();
            testResult.Text = "Testing\u2026";
            var c = plugin.ReloadConfig();
            new System.Threading.Thread(() =>
            {
                string msg;
                try
                {
                    if (c.GuideProvider == 0) msg = "Choose a provider first.";
                    else { AiClient.Test(c); msg = "\u2714 Works! " + OverlayConfig.GuideProviderNames[c.GuideProvider] + " answered."; }
                }
                catch (Exception ex) { msg = "\u2716 " + ex.Message; }
                Dispatcher.BeginInvoke(new Action(() => testResult.Text = msg));
            }) { IsBackground = true }.Start();
        }
    }
}
