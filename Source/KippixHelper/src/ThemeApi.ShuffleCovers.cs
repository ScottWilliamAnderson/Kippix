using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Shuffle covers (Settings › Home settings › Background › BackgroundChanger): a game with more than one cover in
    /// BackgroundChanger shows a different one of them each time Playnite starts, or each day, on the home screen's tiles and
    /// on its game page. Display only: BackgroundChanger's data and Playnite's library are read, never written.
    ///
    /// BackgroundChanger keeps one record per game, ExtensionsData\(its id)\BackgroundChanger\(game id).json:
    /// { "Items": [ { "Name": "x.jpg", "FolderName": "(game id)", "IsCover": true, "IsDefault": .., "IsIcon": .. } ], "Id": .. },
    /// the pictures in ExtensionsData\(its id)\Images\(FolderName)\(Name). A game's record is read the first time one of its
    /// tiles asks; all of them are also read once on a worker (which fills the same table and writes the log line).
    ///
    /// The pick needs no memory of its own: the periods are numbered (the day, or Playnite's starts) and each game goes through
    /// its covers in a shuffled order, a new order each time round, so every tile of a game agrees and no cover comes twice running.
    ///
    /// Playnite's own cards (PART_ListGameItems) get the pick in KX_ShuffleCover, an Image over PART_ImageCover in
    /// ListGameItemTemplate.xaml (empty: Playnite's cover shows through); the tiles the helper makes get its file from
    /// ShuffledCover; the game page binds ShuffleCoverPage.
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly string[] ShuffleImageTypes = { ".jpg", ".jpeg", ".png", ".bmp" };

        private readonly object shuffleGate = new object();
        private readonly Dictionary<Guid, string[]> shuffleSets = new Dictionary<Guid, string[]>();   // a game's covers (two or more), or null
        private readonly Dictionary<Guid, string> shufflePicks = new Dictionary<Guid, string>();      // this period's pick (null: its own cover)
        private long shufflePicksPeriod = -1;
        private bool shuffleStarted, shuffleScanned;
        private bool shuffleWasOn;   // a card has shown a pick this session: reused cards are always checked, so none lingers

        /// <summary>The picks changed (switched on or off, a new day): whatever sized or drew itself from them does it again. On the UI thread.</summary>
        internal event Action ShuffleCoversChanged;

        private bool ShuffleOn { get { return settings.ShuffleCovers && BcInstalled; } }

        private string BcDataDir { get { return Path.Combine(api.Paths.ExtensionsDataPath, BcId.ToString()); } }

        /// <summary>
        /// The cover picked for this game for now (a full path), or null when it shows its own cover: Shuffle covers is off,
        /// BackgroundChanger isn't there, or the game has fewer than two cover pictures in it. Safe on any thread.
        /// </summary>
        internal string ShuffleCoverFile(Guid gameId)
        {
            if (gameId == Guid.Empty || !ShuffleOn) return null;
            long period = ShufflePeriodNow();
            lock (shuffleGate)
            {
                if (period != shufflePicksPeriod) { shufflePicks.Clear(); shufflePicksPeriod = period; }
                string hit;
                if (shufflePicks.TryGetValue(gameId, out hit)) return hit;
            }
            string[] set = ShuffleSetOf(gameId);
            string pick = set == null ? null : set[ShuffleIndex(gameId, set.Length, period)];
            lock (shuffleGate) { if (period == shufflePicksPeriod) shufflePicks[gameId] = pick; }
            return pick;
        }

        /// <summary>The cover a tile shows: the pick when the game has one, otherwise its own (what the tile used before).</summary>
        private string ShuffledCover(Game g, string own)
        {
            string pick = g != null ? ShuffleCoverFile(g.Id) : null;
            return pick ?? own;
        }

        /// <summary>Which period it is: the day's number, or how many times Playnite has started with this on.</summary>
        private long ShufflePeriodNow()
        {
            ShuffleStart();
            return settings.ShuffleCoversEvery == 1 ? DateTime.Today.Ticks / TimeSpan.TicksPerDay : settings.ShuffleCoversRun;
        }

        /// <summary>Once a session, the first time it's used: this start is counted, and every record is read on a worker.</summary>
        private void ShuffleStart()
        {
            lock (shuffleGate)
            {
                if (shuffleStarted) return;
                shuffleStarted = true;
                settings.ShuffleCoversRun = settings.ShuffleCoversRun >= int.MaxValue - 1 ? 0 : settings.ShuffleCoversRun + 1;
            }
            backgroundDirty = true;   // (saved at the next tick)
            KippixShared.SafeWork.Queue(_ => ShuffleScan());
        }

        private void ShuffleScan()
        {
            var found = new Dictionary<Guid, string[]>();
            int many = 0;
            try
            {
                string dir = Path.Combine(BcDataDir, "BackgroundChanger"), images = Path.Combine(BcDataDir, "Images");
                if (Directory.Exists(dir))
                    foreach (string file in Directory.GetFiles(dir, "*.json"))
                    {
                        Guid id;
                        if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out id)) continue;
                        string[] set = ShuffleReadSet(file, images);
                        found[id] = set;
                        if (set != null) many++;
                    }
            }
            catch (Exception ex) { logger.Warn("Kippix: shuffle covers: reading BackgroundChanger's records: " + ex.Message); }
            lock (shuffleGate)
            {
                foreach (var kv in found) shuffleSets[kv.Key] = kv.Value;
                shuffleScanned = true;
            }
            logger.Info("Kippix: shuffle covers: " + many + " games have more than one cover (" + found.Count + " BackgroundChanger records read)");
        }

        private string[] ShuffleSetOf(Guid gameId)
        {
            lock (shuffleGate)
            {
                string[] known;
                if (shuffleSets.TryGetValue(gameId, out known)) return known;
                if (shuffleScanned) return null;   // (no record: nothing from BackgroundChanger for this game)
            }
            // asked for before the worker got to it: this game's record alone (a small file)
            string[] set = ShuffleReadSet(Path.Combine(BcDataDir, "BackgroundChanger", gameId + ".json"), Path.Combine(BcDataDir, "Images"));
            lock (shuffleGate) shuffleSets[gameId] = set;
            return set;
        }

        /// <summary>A record's cover pictures that are on disk (videos and animated ones left out), in name order; null unless there are two or more.</summary>
        private static string[] ShuffleReadSet(string recordFile, string imagesDir)
        {
            try
            {
                if (!File.Exists(recordFile)) return null;
                string text = File.ReadAllText(recordFile);
                if (text.IndexOf("\"IsCover\":true", StringComparison.Ordinal) < 0 && text.IndexOf("\"IsCover\": true", StringComparison.Ordinal) < 0) return null;
                var root = MiniJson.Parse(text) as Dictionary<string, object>;
                object raw;
                var items = root != null && root.TryGetValue("Items", out raw) ? raw as object[] : null;
                if (items == null) return null;
                var files = new List<string>();
                foreach (object o in items)
                {
                    var item = o as Dictionary<string, object>;
                    if (item == null) continue;
                    object cover, name, folder;
                    if (!item.TryGetValue("IsCover", out cover) || !(cover is bool) || !(bool)cover) continue;
                    item.TryGetValue("Name", out name);
                    item.TryGetValue("FolderName", out folder);
                    string n = name as string, f = folder as string;
                    if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(f)) continue;
                    if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || f.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                    if (Array.IndexOf(ShuffleImageTypes, Path.GetExtension(n).ToLowerInvariant()) < 0) continue;
                    string path = Path.Combine(imagesDir, f, n);
                    if (File.Exists(path) && !files.Contains(path)) files.Add(path);
                }
                if (files.Count < 2) return null;
                files.Sort(StringComparer.OrdinalIgnoreCase);
                return files.ToArray();
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Which of a game's n covers shows in this period. Two: they take turns. Three or more: n periods go through all of
        /// them in an order shuffled for that round (the game and the round are the seed), and a round never starts with the
        /// cover the round before ended on.
        /// </summary>
        private static int ShuffleIndex(Guid gameId, int n, long period)
        {
            uint h = 2166136261;
            foreach (byte b in gameId.ToByteArray()) h = unchecked((h ^ b) * 16777619u);
            if (period < 0) period = 0;
            if (n == 2) return (int)((h + (ulong)period) % 2);
            long round = period / n;
            int[] order = ShuffleOrder(h, round, n);
            if (round > 0 && order[0] == ShuffleOrder(h, round - 1, n)[n - 1]) { int t = order[0]; order[0] = order[1]; order[1] = t; }
            return order[(int)(period % n)];
        }

        private static int[] ShuffleOrder(uint seed, long round, int n)
        {
            // (the game and the round, well mixed: rounds next to each other must not come out alike)
            uint x = unchecked(seed + (uint)round * 2654435761u);
            x = unchecked((x ^ (x >> 16)) * 0x85EBCA6Bu);
            x = unchecked((x ^ (x >> 13)) * 0xC2B2AE35u);
            var rnd = new Random(unchecked((int)(x ^ (x >> 16))));
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }
            return order;
        }

        // ---------------- Playnite's cards ----------------

        private static readonly DependencyProperty ShuffleHookedProperty = DependencyProperty.RegisterAttached("KXShuffleHooked", typeof(bool), typeof(ThemeApi), new PropertyMetadata(false));
        private bool shuffleCardsRegistered;

        /// <summary>Once, at start-up: every card of Playnite's list gets its pick as it goes on screen, before it's first drawn.</summary>
        private void HookShuffleCards()
        {
            if (shuffleCardsRegistered) return;
            shuffleCardsRegistered = true;
            EventManager.RegisterClassHandler(typeof(ListBoxItem), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                var item = s as ListBoxItem;
                if (item != null && (ShuffleOn || shuffleWasOn)) ShuffleHookCard(item);
            }));
        }

        private void ShuffleHookCard(ListBoxItem item)
        {
            try
            {
                var owner = ItemsControl.ItemsControlFromItemContainer(item);
                if (owner == null || owner.Name != "PART_ListGameItems") return;
                ShuffleCard(item);
                if ((bool)item.GetValue(ShuffleHookedProperty)) return;
                item.SetValue(ShuffleHookedProperty, true);
                // reused for another game: its pick (or none) straight away, never the last game's
                item.DataContextChanged += (s, a) => { try { if (ShuffleOn || shuffleWasOn) ShuffleCard(item); } catch (Exception) { } };
            }
            catch (Exception ex) { logger.Warn("Kippix: shuffle covers (card): " + ex.Message); }
        }

        private void ShuffleCard(FrameworkElement card)
        {
            var img = UiTools.FindByName(card, "KX_ShuffleCover") as Image;
            if (img == null) return;
            Guid id = ShuffleOn ? EntryId(card.DataContext) : Guid.Empty;
            string file = id != Guid.Empty ? ShuffleCoverFile(id) : null;
            if (file == null)
            {
                img.Tag = null;
                if (img.Source != null) img.Source = null;
                return;
            }
            shuffleWasOn = true;
            if (img.Source != null && Equals(img.Tag, file)) return;
            img.Tag = file;
            // decoded at about the card's size (a step of 100, so the cards share what's been decoded)
            int px = (int)Math.Max(300, Math.Min(600, Math.Ceiling(card.ActualWidth * 1.5 / 100) * 100));
            ImageSource pic = Images.Peek(file, px);
            if (pic != null) { img.Source = pic; return; }
            if (img.Source != null) img.Source = null;   // (Playnite's cover shows until the pick is decoded)
            KippixShared.SafeWork.Queue(_ =>
            {
                var loaded = Images.Load(file, px);
                if (loaded != null) OnUi(() => { if (Equals(img.Tag, file)) img.Source = loaded; });
            });
        }

        /// <summary>The cards on screen now, again.</summary>
        private void ShuffleCardsAgain()
        {
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var list = win != null ? UiTools.FindByName(win, "PART_ListGameItems") as ListBox : null;
            if (list == null) return;
            var panel = VisualTreeHelper.GetChildrenCount(list) > 0 ? UiTools.FindChild<VirtualizingPanel>(list) : null;
            if (panel != null)
            {
                foreach (UIElement child in panel.Children)
                {
                    var c = child as ListBoxItem;
                    if (c != null) ShuffleHookCard(c);
                }
            }
            else
                for (int i = 0; i < list.Items.Count; i++)
                {
                    var c = list.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                    if (c != null) ShuffleHookCard(c);
                }
        }

        // ---------------- the game page ----------------

        private ImageSource shuffleCoverPage;
        private string shufflePageFile;

        /// <summary>The game page's cover when the game has a pick (GameDetails.xaml: shown in place of PART_ImageCover).</summary>
        public ImageSource ShuffleCoverPage { get { return shuffleCoverPage; } }
        public bool HasShuffleCoverPage { get { return shuffleCoverPage != null; } }

        /// <summary>From the game page opening, and from another game being picked while it's open.</summary>
        private void ShufflePageCover(Game game)
        {
            string file = game != null && detailsOpen ? ShuffleCoverFile(game.Id) : null;
            if (file == shufflePageFile) return;
            shufflePageFile = file;
            // (one picture, read here: the page is still unseen, so it never shows the game's own cover first)
            shuffleCoverPage = file != null ? Images.Load(file, 900) : null;
            Notify("ShuffleCoverPage");
            Notify("HasShuffleCoverPage");
        }

        // ---------------- changes ----------------

        private bool shuffleTickSeen, shuffleTickOn;
        private long shuffleTickPeriod = -1;

        /// <summary>From the tick: switched on or off, a new day, or another "how often": what's on screen picks again.</summary>
        private void ShuffleTick()
        {
            bool on = ShuffleOn;
            long period = on ? ShufflePeriodNow() : -1;
            bool first = !shuffleTickSeen;
            if (!first && on == shuffleTickOn && period == shuffleTickPeriod) return;
            shuffleTickSeen = true;
            shuffleTickOn = on;
            shuffleTickPeriod = period;
            if (first) return;   // (start-up: the cards and tiles ask as they're made)
            lock (shuffleGate) { shufflePicks.Clear(); shufflePicksPeriod = period; }
            ShuffleCardsAgain();
            ShufflePageCover(SelectedGameNow());
            ui.BeginInvoke(new Action(RebuildExtraRows), DispatcherPriority.Background);
            if (IsBoardMode) BoardRefillSoon();
            var changed = ShuffleCoversChanged;
            if (changed != null) changed();
        }

        // ---------------- Settings › Home settings › Background › BackgroundChanger › Covers ----------------

        private void ShuffleRows(ObservableCollection<PickRow> rows)
        {
            bool on = settings.ShuffleCovers;
            rows.Add(BoardSwitch("refresh", "bcshuffle", Loc("LOC_KX_BcShuffle", "Shuffle covers on the home screen"), on,
                Loc("LOC_KX_BcShuffleOn", "A game with more than one cover in it shows a different one each time, on its tile and its game page"), Loc("LOC_KX_BcShuffleOff", "Off: each game's own cover"),
                () => { settings.ShuffleCovers = !on; SaveSettings(); PlaySound("move"); BcRowsAgain("bcshuffle"); }));
            if (!on) return;
            bool daily = settings.ShuffleCoversEvery == 1;
            rows.Add(new PickRow
            {
                Key = "bcshuffleevery", Icon = "clock", Indent = 42, Text = Loc("LOC_KX_BcShuffleEvery", "A new cover"),
                Sub = daily ? Loc("LOC_KX_BcShuffleDay", "Each day") : Loc("LOC_KX_BcShuffleStart", "Each time Playnite starts"),
                Command = new RelayCommand(_ => { settings.ShuffleCoversEvery = daily ? 0 : 1; SaveSettings(); PlaySound("move"); BcRowsAgain("bcshuffleevery"); })
            });
        }
    }
}
