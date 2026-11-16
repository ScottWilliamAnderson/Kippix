using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK.Models;

namespace KippixOverlay
{
    internal partial class OverlayWindow
    {
        // ================= Home =================

        private UIElement StatRow(string glyph, double progress, string right)
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 20, Foreground = theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var bar = new Bar(theme, 8, 0);
            bar.Set(progress);
            Grid.SetColumn(bar.Root, 1);
            row.Children.Add(bar.Root);
            var text = new TextBlock { Text = right, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = OverlayTheme.TextMain, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 2);
            row.Children.Add(text);
            return row;
        }

        private List<MenuEntry> HomeEntries()
        {
            var l = new List<MenuEntry>();
            Game g = plugin.RunningGame;
            string name = plugin.GameName;
            List<AchievementInfo> ach = plugin.CurrentAchievements;
            HltbTimes hltb = plugin.CurrentHltb;
            int unlocked = ach != null ? ach.Count(a => a.Unlocked) : 0;
            int total = ach != null ? ach.Count : 0;

            // ---- Now playing tile ----
            var now = new MenuEntry
            {
                Kind = EntryKind.Card,
                Label = "Now playing",
                Title = name,
                Image = plugin.CoverPath(g),
                ImageW = 104,
                ImageH = 140,
                Activate = () => plugin.Resume()
            };
            now.Sub = () =>
            {
                double session = Math.Max(0, (DateTime.Now - plugin.GameStartedAt).TotalSeconds);
                ulong played = (g != null ? g.Playtime : 0) + (ulong)session;
                return GameData.FormatPlaytime(played) + " played  \u00B7  Session " + GameData.FormatPlaytime((ulong)session);
            };

            var extra = new StackPanel();
            if (total > 0)
                extra.Children.Add(StatRow("\uE734", (double)unlocked / total, unlocked + " / " + total));

            double playedHours = ((g != null ? g.Playtime : 0) + Math.Max(0, (DateTime.Now - plugin.GameStartedAt).TotalSeconds)) / 3600.0;
            if (!plugin.Config.ShowHowLongToBeat)
            {
                // Hidden in Plugin settings.
            }
            else if (hltb != null)
            {
                double target = hltb.Story > 0 ? hltb.Story : (hltb.Extras > 0 ? hltb.Extras : hltb.Completionist);
                var parts = new List<string>();
                if (hltb.Story > 0) parts.Add("Story ~" + GameData.FormatHours(hltb.Story));
                if (hltb.Extras > 0) parts.Add("Extras ~" + GameData.FormatHours(hltb.Extras));
                if (hltb.Completionist > 0) parts.Add("100% ~" + GameData.FormatHours(hltb.Completionist));
                extra.Children.Add(StatRow("\uE916", target > 0 ? playedHours / target : 0, string.Join("  \u00B7  ", parts)));
            }
            else
            {
                var none = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
                none.Children.Add(new TextBlock { Text = "\uE916", FontFamily = IconFont, FontSize = 20, Foreground = OverlayTheme.TextDim, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
                none.Children.Add(new TextBlock { Text = "No HowLongToBeat data for this game", FontSize = 16, Foreground = OverlayTheme.TextDim, VerticalAlignment = VerticalAlignment.Center });
                extra.Children.Add(none);
            }
            // (0.99.48) how far through it you are, under HowLongToBeat (Overlay settings › Look › Story progress on the game tile)
            if (plugin.Config.ShowStoryProgress)
            {
                try
                {
                    var list = new List<KippixHelper.ProgressAch>();
                    if (ach != null)
                        foreach (var a in ach) list.Add(new KippixHelper.ProgressAch { Name = a.Name, Description = a.Description, Unlocked = a.Unlocked, Rarity = a.Rarity });
                    string status = null;
                    try { if (g != null && g.CompletionStatus != null) status = g.CompletionStatus.Name; } catch (Exception) { }
                    var r = KippixHelper.StoryProgress.Estimate(list, playedHours * 3600, hltb != null ? hltb.Story : 0, hltb != null ? hltb.Extras : 0, hltb != null ? hltb.Completionist : 0, status, null, KippixHelper.StoryProgress.LabelsOf(g));
                    if (r != null) extra.Children.Add(StatRow("\uE7C1", r.Percent / 100.0, r.Short));   // (0.99.50: just "Story ~60%")
                }
                catch (Exception) { }
            }
            now.Extra = extra;
            l.Add(now);

            // ---- Game actions ----
            l.Add(Row("\uE768", "Resume game", name, () => plugin.Resume()));
            l.Add(Row("\uE80F", "Open Playnite", null, () => plugin.OpenPlaynite()));
            l.Add(Row("\uE711", "Close game", null, () =>
            {
                if (plugin.Config.ConfirmClose)
                    PushConfirm("Close " + name, "Any unsaved progress will be lost.", "\uE711", "Close game", true, () => plugin.CloseGame());
                else plugin.CloseGame();
            }));

            l.Add(Divider());

            var achRow = new MenuEntry
            {
                Glyph = "\uE734",
                Title = "Achievements",
                Chevron = true,
                Activate = () => Push("Achievements", AchievementEntries)
            };
            if (total > 0)
            {
                var bar = new Bar(theme, 7, 0);
                bar.Root.Margin = new Thickness(0, 10, 40, 2);
                bar.Set((double)unlocked / total);
                achRow.Extra = bar.Root;
            }
            l.Add(achRow);
            AddNextAchievement(l, g, ach);
            l.Add(Row("\uE765", "Keyboard", "Type into the game", () => plugin.OpenGameKeyboard()));

            l.Add(Row("\uE722", "Take a screenshot", null, () => plugin.TakeScreenshot()));
            if (plugin.Config.ReplayHotkey() != null)
                l.Add(Row("\uE714", "Save recent gameplay", null, () => plugin.SaveRecentGameplay()));
            l.Add(Divider());

            // ---- Switch to ----
            List<Game> recent = plugin.RecentGames(5);
            if (recent.Count > 0)
            {
                l.Add(Header("Switch to"));
                foreach (Game r in recent)
                {
                    Game target = r;
                    var parts = new List<string>();
                    parts.Add("Last played " + GameData.FormatLastPlayed(r.LastActivity));
                    parts.Add(GameData.FormatPlaytime(r.Playtime) + " played");
                    string achText = plugin.AchievementSummary(r);
                    if (achText != null) parts.Add(achText);
                    string sub = string.Join("  \u00B7  ", parts);
                    l.Add(new MenuEntry
                    {
                        Title = r.Name,
                        Sub = () => sub,
                        Image = plugin.CoverPath(r) ?? plugin.IconPath(r),
                        Glyph = "\uE7FC",
                        ImageW = 60,
                        ImageH = 80,
                        Chevron = true,
                        Activate = () => PushConfirm("Switch to " + target.Name,
                            name + " will be closed first. Any unsaved progress will be lost.",
                            "\uE8AB", "Switch game", false, () => plugin.SwitchTo(target))
                    });
                }
            }
            return l;
        }

        // ================= Achievements =================

        private List<MenuEntry> AchievementEntries()
        {
            var l = new List<MenuEntry>();
            List<AchievementInfo> ach = plugin.CurrentAchievements;

            if (ach == null || ach.Count == 0)
            {
                l.Add(InfoCard("\uE734", plugin.GameName, "No achievements found",
                    "The overlay couldn't get achievements for this game. Check SuccessStory or Playnite Achievements has this game, then try again. The details below help if it keeps happening."));
                l.Add(Row("\uE72C", "Check again", null, () =>
                {
                    plugin.ReloadGameData();
                    Rebuild();
                }));
                l.Add(new MenuEntry { Kind = EntryKind.Text, Title = plugin.CurrentAchievementInfo, Selectable = false });
                return l;
            }

            int unlocked = ach.Count(a => a.Unlocked);
            double pct = (double)unlocked / ach.Count;
            var bar = new Bar(theme, 8, 0);
            bar.Root.Margin = new Thickness(0, 10, 0, 2);
            bar.Set(pct);
            l.Add(new MenuEntry
            {
                Kind = EntryKind.Card,
                Glyph = "\uE734",
                Label = plugin.GameName,
                Title = unlocked + " of " + ach.Count + " unlocked",
                Sub = () => Math.Round(pct * 100) + "% complete",
                Extra = bar.Root,
                Selectable = false
            });

            foreach (AchievementInfo a in ach)
            {
                AchievementInfo item = a;
                bool hidden = a.Hidden && !a.Unlocked;
                string sub;
                if (a.Unlocked) sub = a.Date.HasValue ? "Unlocked " + a.Date.Value.ToString("d MMM yyyy") : "Unlocked";
                else if (hidden) sub = "Hidden achievement";
                else sub = a.Description;
                if (a.Rarity.HasValue) sub = (string.IsNullOrEmpty(sub) ? "" : sub + "  \u00B7  ") + a.Rarity.Value.ToString("0.#") + "% of players";
                string s2 = sub;
                l.Add(new MenuEntry
                {
                    Title = hidden ? "Hidden achievement" : a.Name,
                    Sub = () => s2,
                    Glyph = a.Unlocked ? "\uE73E" : "\uE72E",
                    Image = hidden ? null : a.Icon,
                    ImageW = 52,
                    ImageH = 52,
                    Dim = !a.Unlocked,
                    Chevron = true,
                    Right = AchievementMarkers(item),
                    Activate = () => Push(hidden ? "Hidden achievement" : item.Name, () => AchievementDetail(item))
                });
            }
            return l;
        }

        /// <summary>
        /// (0.99.47) The Guide button during an achievement pop-up: that achievement's page (with the game's list under it, so
        /// B goes back to it). Found in the game's achievements by name; from the pop-up itself when it isn't there yet.
        /// </summary>
        internal void ShowAchievementFromToast(AchToastItem it)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var ach = plugin.CurrentAchievements;
                    AchievementInfo a = ach != null ? ach.FirstOrDefault(x => x != null && string.Equals((x.Name ?? "").Trim(), (it.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) : null;
                    if (a == null)
                        a = new AchievementInfo { Name = it.Name, Description = it.Description, Unlocked = true, Date = DateTime.Now, Icon = it.IconPath, Rarity = it.Percent };
                    else if (!a.Unlocked) { a.Unlocked = true; a.Date = DateTime.Now; }   // (just unlocked: the saved list may not know yet)
                    if (string.IsNullOrEmpty(a.Description)) a.Description = it.Description;
                    if (ach != null && ach.Count > 0) Push("Achievements", AchievementEntries);
                    var item = a;
                    Push(item.Name, () => AchievementDetail(item));
                }
                catch (Exception) { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private List<MenuEntry> AchievementDetail(AchievementInfo a)
        {
            var l = new List<MenuEntry>();
            bool hidden = a.Hidden && !a.Unlocked;
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(a.Description)) lines.Add(a.Description);
            lines.Add(a.Unlocked ? (a.Date.HasValue ? "Unlocked " + a.Date.Value.ToString("d MMMM yyyy, HH:mm") : "Unlocked") : "Not unlocked yet");
            if (a.Rarity.HasValue) lines.Add(a.Rarity.Value.ToString("0.#") + "% of players have this");
            string text = string.Join("\n", lines);

            l.Add(new MenuEntry
            {
                Kind = EntryKind.Card,
                Label = a.Unlocked ? "Unlocked" : (hidden ? "Hidden" : "Locked"),
                Title = a.Name,
                Sub = () => text,
                Glyph = a.Unlocked ? "\uE73E" : "\uE72E",
                Image = a.Icon,
                ImageW = 96,
                ImageH = 96,
                Dim = !a.Unlocked,
                Selectable = false
            });
            AddGuideEntries(l, a, hidden);

            // "Set as next achievement" goes straight under the How to unlock tip (or at the end when there's no tip).
            Game game = plugin.RunningGame;
            if (game != null && !a.Unlocked)
            {
                int tipIndex = l.FindIndex(x => x.Label == "How to unlock");
                l.Insert(tipIndex >= 0 ? tipIndex + 1 : l.Count, new MenuEntry
                {
                    Glyph = "\uE7C1",
                    TitleFunc = () => IsNextAchievement(a) ? "Your next achievement" : "Set as next achievement",
                    Sub = () => IsNextAchievement(a) ? "Shown on Home. Select to stop tracking it." : "Shows it with its tip on Home",
                    Activate = () =>
                    {
                        plugin.SetNextAchievement(game.Id, IsNextAchievement(a) ? null : a.Name);
                        RefreshEntries();
                    }
                });
            }
            return l;
        }

        private bool IsNextAchievement(AchievementInfo a)
        {
            Game game = plugin.RunningGame;
            if (game == null) return false;
            string next = plugin.GetNextAchievement(game.Id);
            return next != null && GuideStore.Key(next) == GuideStore.Key(a.Name);
        }

        /// <summary>Icons on the right of an achievement in the list: a flag for your next one, a lightbulb when a tip is saved.</summary>
        private UIElement AchievementMarkers(AchievementInfo a)
        {
            bool next = IsNextAchievement(a), guide = HasGuide(a);
            if (!next && !guide) return null;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (next) sp.Children.Add(new TextBlock { Text = "\uE7C1", FontFamily = IconFont, FontSize = 20, Foreground = theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, guide ? 12 : 0, 0) });
            if (guide) sp.Children.Add(new TextBlock { Text = "\uE897", FontFamily = IconFont, FontSize = 20, Foreground = theme.Accent, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        /// <summary>The "Next achievement" card on Home: the one you picked, with its tip.</summary>
        private void AddNextAchievement(List<MenuEntry> l, Game g, List<AchievementInfo> ach)
        {
            if (g == null || ach == null) return;
            string pinned = plugin.GetNextAchievement(g.Id);
            if (pinned == null) return;
            AchievementInfo a = ach.FirstOrDefault(x => GuideStore.Key(x.Name) == GuideStore.Key(pinned));
            if (a == null) return;

            if (a.Unlocked)
            {
                l.Add(new MenuEntry
                {
                    Kind = EntryKind.Card,
                    Label = "Next achievement",
                    LabelAlert = null,
                    Title = a.Name + " \u2014 unlocked!",
                    Sub = () => "Nice one. Pick your next one from Achievements.",
                    Image = a.Icon,
                    ImageW = 64,
                    ImageH = 64,
                    Glyph = "\uE73E",
                    Chevron = true,
                    Activate = () =>
                    {
                        plugin.SetNextAchievement(g.Id, null);
                        Push("Achievements", AchievementEntries);
                    }
                });
                return;
            }

            bool hidden = a.Hidden && !revealedGuides.Contains(GuideStore.Key(a.Name));
            AchievementGuide guide = plugin.Guides != null ? plugin.Guides.Find(g.Id, a.Name) : null;
            string sub;
            if (hidden) sub = "Hidden achievement: open it to reveal how to unlock";
            else if (guide != null) sub = Shorten(guide.Tip, 160);
            else sub = a.Description;

            l.Add(new MenuEntry
            {
                Kind = EntryKind.Card,
                Label = "Next achievement",
                LabelAlert = guide != null && guide.Missable ? "Missable!" : null,
                Title = a.Name,
                Sub = () => sub,
                Image = a.Icon,
                ImageW = 64,
                ImageH = 64,
                Glyph = "\uE7C1",
                Dim = false,
                Chevron = true,
                Activate = () => Push(a.Name, () => AchievementDetail(a))
            });
        }

        private bool HasGuide(AchievementInfo a)
        {
            Game game = plugin.RunningGame;
            return game != null && plugin.Guides != null && plugin.Guides.Find(game.Id, a.Name) != null;
        }

        private readonly HashSet<string> revealedGuides = new HashSet<string>();

        private void AddGuideEntries(List<MenuEntry> l, AchievementInfo a, bool hidden)
        {
            Game game = plugin.RunningGame;
            AchievementGuide guide = game != null && plugin.Guides != null ? plugin.Guides.Find(game.Id, a.Name) : null;
            bool ready = plugin.Config.GuidesReady;

            if (guide != null)
            {
                string key = GuideStore.Key(a.Name);
                if (hidden && !revealedGuides.Contains(key))
                {
                    l.Add(Row("\uE7B3", "Reveal how to unlock", "Hidden achievement: may contain spoilers", () =>
                    {
                        revealedGuides.Add(key);
                        Rebuild();
                    }));
                }
                else
                {
                    string body = guide.Tip;
                    if (!string.IsNullOrEmpty(guide.When)) body = "When: " + guide.When + "\n\n" + body;
                    l.Add(new MenuEntry
                    {
                        Kind = EntryKind.Card,
                        Glyph = guide.Missable ? "\uE7BA" : "\uE897",
                        Danger = guide.Missable,
                        Label = "How to unlock",
                        LabelAlert = guide.Missable ? "Missable!" : null,
                        Sub = () => body,
                        Selectable = false
                    });
                    if (ready)
                    {
                        bool busy = false;
                        l.Add(new MenuEntry
                        {
                            Glyph = "\uE72C",
                            TitleFunc = () => busy ? "Rewriting this tip\u2026" : "Regenerate this tip",
                            Activate = () =>
                            {
                                if (busy) return;
                                busy = true;
                                plugin.RegenerateGuide(game, a, err =>
                                {
                                    busy = false;
                                    if (err != null) plugin.Toast("Couldn't rewrite the tip", err);
                                    RefreshPage(TopPage);
                                });
                            }
                        });
                    }
                }
            }
            else if (ready && game != null && plugin.Guides.Has(game.Id))
            {
                // The game has guides but not for this one (new achievement, or the AI skipped it).
                bool busy = false;
                l.Add(new MenuEntry
                {
                    Glyph = "\uE896",
                    TitleFunc = () => busy ? "Writing a tip\u2026" : "Get a tip for this achievement",
                    Sub = () => hidden ? "May contain spoilers" : null,
                    Activate = () =>
                    {
                        if (busy) return;
                        busy = true;
                        Page here = TopPage;
                        plugin.RegenerateGuide(game, a, err =>
                        {
                            busy = false;
                            if (err != null) plugin.Toast("Couldn't write a tip", err);
                            RefreshPage(here);
                        });
                    }
                });
            }
            else if (ready && game != null)
            {
                l.Add(new MenuEntry
                {
                    Glyph = "\uE896",
                    TitleFunc = () => plugin.IsDownloadingGuides(game.Id) ? "Getting guides for this game\u2026" : "Get guides for this game",
                    Sub = () => plugin.IsDownloadingGuides(game.Id) ? "This takes about a minute. You can keep playing." : "Downloads a tip for every achievement",
                    Activate = () =>
                    {
                        if (plugin.IsDownloadingGuides(game.Id)) return;
                        Page here = TopPage;
                        plugin.DownloadGuidesBackground(game, false, err =>
                        {
                            if (err != null) plugin.Toast("Couldn't get guides", err);
                            RefreshPage(here);
                        });
                        RefreshEntries();
                    }
                });
            }
            else
            {
                l.Add(Note("\uE946", "Saved guides: set up an AI provider in Playnite (desktop) \u2192 Add-ons \u2192 Extension settings \u2192 Kippix Helper \u2192 Overlay."));
            }
        }
    }
}
