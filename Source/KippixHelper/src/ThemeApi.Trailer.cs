using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Playnite.SDK.Models;

namespace KippixHelper
{
    /// <summary>
    /// Trailer on the game page, with our own player so autoplay / sound / loop can be set.
    /// 1. A trailer Extra Metadata Loader downloaded (one .mp4 with sound).
    /// 2. Otherwise, for Steam games, the store trailer. Steam now only offers DASH streams (separate video and
    ///    audio in 3-second pieces), which Windows' player can't stream, so the pieces are downloaded once and
    ///    joined into a video file and an audio file in the add-on's data folder, then played together.
    /// Plays only while the game page is open.
    /// </summary>
    public partial class ThemeApi
    {
        private static readonly Guid SteamPluginId = new Guid("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        private const int TrailerCacheKeep = 25;   // trailers kept on disk

        private class TrailerFiles
        {
            public string Video;
            public string Audio;   // null when the video has its own sound
            public double Seconds; // length from the Steam manifest (joined DASH files don't carry it), 0 if unknown
        }

        private static readonly Dictionary<string, TrailerFiles> steamTrailers = new Dictionary<string, TrailerFiles>();
        private static readonly Dictionary<string, List<Action<TrailerFiles>>> trailerWaiters = new Dictionary<string, List<Action<TrailerFiles>>>();   // downloads in progress

        private Uri trailerSource;
        private Uri trailerAudioSource;
        private bool trailerPlaying;
        private bool detailsHooked;
        private bool detailsOpen;
        private int trailerRequest;
        private MediaElement trailerElement;
        private MediaPlayer trailerAudio;
        private string trailerStatus = "";
        private double trailerSeconds;

        public Uri DetTrailerSource { get { return trailerSource; } }
        /// <summary>The video element is muted when sound is off, or when the sound comes from the separate audio file.</summary>
        public bool DetTrailerMuted { get { return !settings.DetTrailerSound || trailerAudioSource != null; } }
        public bool DetTrailerPlaying { get { return trailerPlaying; } }
        public bool DetShowVideo { get { return settings.DetArt == 1 && trailerSource != null; } }
        public bool DetShowCover { get { return settings.DetArt == 0 || (settings.DetArt == 1 && trailerSource == null); } }
        public string DetTrailerStatus { get { return trailerStatus; } }
        public bool HasDetTrailerStatus { get { return settings.DetArt == 1 && trailerSource == null && trailerStatus.Length > 0; } }

        public bool DetTrailerAutoplay { get { return settings.DetTrailerAutoplay; } set { if (SetDet(ref settings.DetTrailerAutoplay, value)) NotifyTrailer(); } }
        public bool DetTrailerSound
        {
            get { return settings.DetTrailerSound; }
            set { if (SetDet(ref settings.DetTrailerSound, value)) { NotifyTrailer(); ApplyTrailerVolume(); UpdateMusicState(); } }
        }
        public bool DetTrailerLoop { get { return settings.DetTrailerLoop; } set { SetDet(ref settings.DetTrailerLoop, value); } }
        public bool DetTrailerStream
        {
            get { return settings.DetTrailerStream; }
            set { if (SetDet(ref settings.DetTrailerStream, value)) UpdateTrailer(); }
        }

        public ICommand DetToggleTrailerCommand { get; private set; }

        private void InitTrailerCommands()
        {
            DetToggleTrailerCommand = new RelayCommand(_ =>
            {
                // your own pause stays until you leave the game page (panels, the browser, the lower page don't undo it)
                trailerHeld = false;
                if (trailerPlaying) { trailerUserPaused = true; PauseTrailer(); }
                else { trailerUserPaused = false; PlayTrailer(); }
                if (openPanel == "trailer") ShowTrailerControls();
            });
            InitTrailerFullCommands();
            PropertyChanged += (s, e) =>
            {
                string n = e.PropertyName;
                if (n == "IsAnyPanelOpen" || n == "IsDescReaderOpen" || n == "DetHintBackUp") TrailerViewSoon();
                else if (n == "IsBrowserOpen") { TrailerViewSoon(); DetailsBrowserCheckSoon(); }
            };
        }

        private void NotifyTrailer()
        {
            foreach (string n in new[] { "DetTrailerSource", "DetTrailerMuted", "DetTrailerPlaying", "DetShowVideo", "DetShowCover",
                                         "DetTrailerStatus", "HasDetTrailerStatus",
                                         "DetTrailerAutoplay", "DetTrailerSound", "DetTrailerLoop", "DetTrailerStream" })
                Notify(n);
        }

        private void SetTrailerStatus(string text)
        {
            trailerStatus = text ?? "";
            Notify("DetTrailerStatus");
            Notify("HasDetTrailerStatus");
        }

        /// <summary>Watches the game page opening and closing (called from the timer until it's found).</summary>
        private void HookDetails()
        {
            if (detailsHooked) return;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
            if (details == null) return;
            detailsHooked = true;
            detailsOpen = details.IsVisible;
            details.IsVisibleChanged += (s, e) =>
            {
                bool visible = (bool)e.NewValue;
                // a WPF event: a mistake in here must not reach Playnite
                try
                {
                    // The browser hides what it covers (ThemeApi.Browser.cs HideBehindBrowser), the game page too: that isn't the
                    // game page closing. Left as it was (lower page, trailer position, your pause), just the trailer held still.
                    if (!visible && detailsOpen && openPanel == "browser") { detailsUnderBrowser = true; TrailerViewSoon(); return; }
                    if (visible && detailsUnderBrowser) { detailsUnderBrowser = false; detailsOpen = true; TrailerViewSoon(); return; }
                    detailsUnderBrowser = false;
                    detailsOpen = visible;
                    if (detailsOpen) DetailsOpened(details); else DetailsClosed();
                }
                catch (Exception ex) { logger.Error(ex, "Kippix: game page opening / closing"); }
            };
        }

        private void DetailsOpened(FrameworkElement details)
        {
            try { DetEntranceStart(details); } catch (Exception) { DetailsFadeDone(details); }   // (0.99.69) waits until the page is drawn, then plays
            try { ShufflePageCover(SelectedGameNow()); } catch (Exception) { }   // Shuffle covers: the cover picked for now
            try { DetCoverHold(details); } catch (Exception) { }   // (0.99.66) the cover fades in once it's the new game's
            detOriginId = Guid.Empty; trailerUserPaused = false; trailerHeld = false; trailerGameId = Guid.Empty;
            StopPreview(); if (masterTransforms.Count > 0 || settings.MasterOffsets.Count > 0 || (settings.MasterSizes != null && settings.MasterSizes.Count > 0) || HasMasterTurns) ApplyMasterOffsets(); detGameId = Guid.Empty; DetailsScrollTo(false, false); ApplyDetPlacement();
            // 0.81: what fills the page (trailer, friends, play history, news, mods, badges, More like this...) is put in once the
            // page has been drawn, so it opens, and its animation starts, the moment the card is pressed
            int gen = ++detOpenGen;
            detFadeOpenedAt = DateTime.UtcNow;
            detFadeSeen.Clear();
            // (0.99.63) the page's parts are made as it's laid out, after this: the ones not there yet only got their Free move
            // places at the next 2-second tick, so for a moment they stood where they'd be unmoved. Again once it's laid out
            // (before it's drawn, under the fade-in) and once it's filled in.
            ui.BeginInvoke(new Action(() =>
            {
                if (gen != detOpenGen || !detailsOpen) return;
                try { ApplyMasterOffsets(); } catch (Exception) { }   // (0.99.65: places only; the parts aren't moved between the pages again, which made them flash)
                try { HookDetFades(details); } catch (Exception) { }   // (0.99.64) what comes in later fades in
                try { DescTextNoFocus(details); } catch (Exception) { }   // the description's text never takes the highlight
            }), DispatcherPriority.Loaded);
            EventHandler once = null;
            once = (s, e) =>
            {
                CompositionTarget.Rendering -= once;
                ui.BeginInvoke(new Action(() =>
                {
                    if (gen != detOpenGen || !detailsOpen) return;
                    try { UpdateTrailer(); UpdateDetFriends(); UpdateGamePageMore(); }
                    catch (Exception ex) { logger.Warn("Kippix: filling the game page: " + ex.Message); }
                    try { ApplyMasterOffsets(); } catch (Exception) { }   // (0.99.63: what was filled in, in its place too)
                    try { HookDetFades(details); } catch (Exception) { }
                    try { DescTextNoFocus(details); } catch (Exception) { }   // (again: a part made after the first layout)
                    try { GamePageUpdateCheck(SelectedGameNow()); } catch (Exception) { }
                    try { DetProgressRefresh(); } catch (Exception) { }   // (0.99.48) story progress   // (0.99.39) an update waiting: a card
                    detFilledGen = gen;   // (0.99.69) filled in: the entrance can start once it's drawn
                }), DispatcherPriority.Background);
            };
            CompositionTarget.Rendering += once;
        }
        private int detOpenGen;

        /// <summary>
        /// 0.82: the whole game page fades in (a third of a second, easing out) over its own entrance animation, so what's filled
        /// in a moment later (the trailer, play history, How long to beat, badges, More like this...) comes in with the fade
        /// instead of popping in after it.
        /// </summary>
        private void DetailsFadeIn(FrameworkElement details)
        {
            try
            {
                // (0.82: it starts at 0 now, before it's drawn: the animation only takes over on the next frame, so the first frame
                // was drawn whole and the page flashed, then faded)
                details.BeginAnimation(UIElement.OpacityProperty, null);
                details.Opacity = 0;
                // (0.99.68) the one fade the whole page has (the template's own fade of its root is gone: two at once looked uneven)
                var a = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                DetEntranceCache(details);
                a.Completed += (s, e) => DetailsFadeDone(details);
                details.BeginAnimation(UIElement.OpacityProperty, a);
            }
            catch (Exception) { DetailsFadeDone(details); }
        }

        /// <summary>The fade's over (or the page closed meanwhile): fully shown, nothing left on its opacity.</summary>
        private static void DetailsFadeDone(FrameworkElement details)
        {
            try { details.BeginAnimation(UIElement.OpacityProperty, null); details.ClearValue(UIElement.OpacityProperty); } catch (Exception) { }
        }


        private void DetailsClosed()
        {
            try { DetCoverRelease(); detEntranceGen++; DetEntranceUnhook(); } catch (Exception) { }
            try { var dw = Application.Current.MainWindow; var dd = dw != null ? UiTools.FindByName(dw, "PART_ElemGameDetails") as FrameworkElement : null; if (dd != null) DetailsFadeDone(dd); } catch (Exception) { }
            trailerUserPaused = false; trailerHeld = false; trailerGameId = Guid.Empty;   // back home: the next visit starts afresh
            GamePageUpdateClosed();
            StopTrailer(); SetTrailer(null, null); detMoreDown = false; if (descReaderOpen) { descReaderOpen = false; Notify("IsDescReaderOpen"); } bool fromRows = extraOpenedDetails; DetailsClosedForRows(); if (!fromRows) { BackToOriginGame(); HighlightAfterDetails(); }
            detOpenGen++;
            // (0.81: emptied now it's hidden, so the next game's page never shows this one's for a moment while it fills)
            ui.BeginInvoke(new Action(() => { try { if (!detailsOpen) UpdateGamePageMore(); } catch (Exception) { } }), DispatcherPriority.Background);
        }

        // ---------------- the trailer only plays while you can see it ----------------

        private bool detailsUnderBrowser;   // the game page hidden behind the browser (still open)
        private bool trailerUserPaused;     // you paused it (this visit to this game's page)
        private bool trailerHeld;           // paused by us because it's out of sight: plays on when it's back (unless you paused it)
        private Guid trailerGameId;
        private bool trailerViewPending;

        private void TrailerViewSoon()
        {
            if (trailerViewPending || ui == null) return;
            trailerViewPending = true;
            ui.BeginInvoke(new Action(() =>
            {
                trailerViewPending = false;
                try { TrailerViewCheck(); } catch (Exception ex) { logger.Warn("Kippix: trailer in view check: " + ex.Message); }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Pauses the game page trailer (picture and sound) when it's out of sight; plays it on when it's back, unless you paused it.</summary>
        private void TrailerViewCheck()
        {
            if (!detailsOpen || trailerSource == null || openPanel == "trailer") return;   // (the full screen player looks after itself)
            if (TrailerOutOfView())
            {
                if (trailerPlaying) { PauseTrailer(); trailerHeld = true; }
            }
            else if (trailerHeld)
            {
                trailerHeld = false;
                if (!trailerUserPaused) PlayTrailer();
            }
        }

        private static readonly HashSet<string> CoveringPanels = new HashSet<string> { "achievements", "browser", "store", "gallery", "search", "page", "who", "welcome" };   // (not Free move: you're placing things on the game page you can see)

        /// <summary>The game page trailer can't be seen: something full screen over it, a side panel over most of it, or the other page showing.</summary>
        private bool TrailerOutOfView()
        {
            if (!detailsOpen || detailsUnderBrowser || descReaderOpen) return true;
            if (openPanel != null && CoveringPanels.Contains(openPanel)) return true;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            if (win == null) return false;
            var box = UiTools.FindByName(win, "VideoBox") as FrameworkElement;
            if (box == null || !box.IsVisible || box.ActualWidth < 1 || box.ActualHeight < 1) return true;
            // which page it's on (it can be sent to the lower page)
            var more = UiTools.FindByName(win, "KX_DetMore");
            bool onLower = more != null && more.IsAncestorOf(box);
            if (onLower != detMoreDown) return true;
            if (openPanel == null) return false;
            // a side panel (settings, downloads...): out of sight when it covers more than half of it
            var panel = UiTools.FindByName(win, PanelElement(openPanel)) as FrameworkElement;
            if (panel == null || !panel.IsVisible) return false;
            try
            {
                Rect video = box.TransformToAncestor(win).TransformBounds(new Rect(0, 0, box.ActualWidth, box.ActualHeight));
                var parent = VisualTreeHelper.GetParent(panel) as Visual;
                if (parent == null) return false;
                Vector off = VisualTreeHelper.GetOffset(panel);   // where it ends up (not where its slide-in is right now)
                Rect cover = parent.TransformToAncestor(win).TransformBounds(new Rect(off.X, off.Y, panel.ActualWidth, panel.ActualHeight));
                Rect both = Rect.Intersect(video, cover);
                return !both.IsEmpty && both.Width * both.Height > 0.5 * video.Width * video.Height;
            }
            catch (Exception) { return false; }
        }

        /// <summary>The browser closed but the game page didn't come back (it was closed meanwhile): finish closing it.</summary>
        private void DetailsBrowserCheckSoon()
        {
            if (!detailsUnderBrowser || ui == null) return;
            ui.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!detailsUnderBrowser || openPanel == "browser") return;
                    var win = Application.Current != null ? Application.Current.MainWindow : null;
                    var details = win != null ? UiTools.FindByName(win, "PART_ElemGameDetails") : null;
                    if (details == null || details.IsVisible) { detailsUnderBrowser = false; return; }
                    detailsUnderBrowser = false;
                    detailsOpen = false;
                    DetailsClosed();
                }
                catch (Exception ex) { logger.Warn("Kippix: game page after the browser: " + ex.Message); }
            }), DispatcherPriority.ContextIdle);
        }

        /// <summary>Autoplay when the trailer is ready: not if you paused this game's trailer, and held until it's in sight.</summary>
        private void AutoplayTrailer()
        {
            if (trailerSource == null || !detailsOpen || trailerUserPaused) return;
            if (TrailerOutOfView()) { trailerHeld = true; return; }
            PlayTrailer();
        }

        /// <summary>Finds the trailer for the selected game (only while the game page is open and Trailer is chosen).</summary>
        private void UpdateTrailer()
        {
            int request = ++trailerRequest;
            StopTrailer();
            SetTrailerStatus("");
            if (!detailsOpen || settings.DetArt != 1) { SetTrailer(null, null); return; }
            Game game = null;
            try { game = api.MainView.SelectedGames != null ? api.MainView.SelectedGames.FirstOrDefault() : null; } catch (Exception) { }
            if (game == null) { SetTrailer(null, null); return; }
            if (game.Id != trailerGameId) { trailerGameId = game.Id; trailerUserPaused = false; trailerHeld = false; }   // another game: its own trailer, autoplay as set

            bool waited = false;
            ResolveTrailer(game, files =>
            {
                if (request != trailerRequest || !detailsOpen) return;
                if (waited) SetTrailerStatus(files == null ? Loc("LOC_KX_TrailerNone", "No trailer for this game") : "");
                if (files != null) { trailerSeconds = files.Seconds; SetTrailer(new Uri(files.Video), files.Audio != null ? new Uri(files.Audio) : null); }
                else SetTrailer(null, null);
            }, () =>
            {
                waited = true;
                SetTrailer(null, null);
                SetTrailerStatus(Loc("LOC_KX_TrailerLoading", "Loading the trailer…"));
                return true;
            });
        }

        /// <summary>
        /// The game's trailer: one Extra Metadata Loader downloaded, otherwise the Steam store trailer (downloaded once).
        /// <paramref name="done"/> runs on the UI thread, straight away or after the download (null when there's none);
        /// <paramref name="downloading"/> runs first when it has to wait for a download (returning false gives up instead).
        /// </summary>
        private void ResolveTrailer(Game game, Action<TrailerFiles> done, Func<bool> downloading)
        {
            // 1. downloaded by Extra Metadata Loader
            string dir = Path.Combine(api.Paths.ConfigurationPath, "ExtraMetadata", "games", game.Id.ToString());
            foreach (string name in new[] { "VideoTrailer.mp4", "VideoMicrotrailer.mp4" })
            {
                string f = Path.Combine(dir, name);
                if (File.Exists(f)) { done(new TrailerFiles { Video = f }); return; }
            }

            // 2. the Steam store trailer
            if (!settings.DetTrailerStream) { done(null); return; }
            ResolveSteamTrailer(game, done, downloading);
        }

        /// <summary>
        /// The game's Steam store trailer alone (downloaded once, then reused), whatever DetTrailerStream says: the caller asks
        /// its own setting. (issue #13: split from ResolveTrailer so the home video can use the same download and cache.)
        /// </summary>
        private void ResolveSteamTrailer(Game game, Action<TrailerFiles> done, Func<bool> downloading)
        {
            if (game.PluginId != SteamPluginId || string.IsNullOrEmpty(game.GameId)) { done(null); return; }
            string appId = game.GameId;
            TrailerFiles cached;
            lock (steamTrailers)
            {
                if (steamTrailers.TryGetValue(appId, out cached))
                {
                    if (cached == null || File.Exists(cached.Video)) { done(cached); return; }
                    steamTrailers.Remove(appId);   // trimmed from the cache since (e.g. by Store trailers): download it again
                }
            }
            if (downloading != null && !downloading()) return;
            lock (trailerWaiters)
            {
                List<Action<TrailerFiles>> waiting;
                if (trailerWaiters.TryGetValue(appId, out waiting)) { waiting.Add(done); return; }   // already downloading
                trailerWaiters[appId] = new List<Action<TrailerFiles>> { done };
            }
            string cacheDir = Path.Combine(plugin.GetPluginUserDataPath(), "Trailers");
            KippixShared.SafeWork.Queue(_ =>
            {
                TrailerFiles files = null;
                try { files = GetSteamTrailer(appId, cacheDir); }
                catch (Exception ex) { logger.Warn("Kippix: Steam trailer for " + appId + " failed: " + ex.Message); }
                lock (steamTrailers) steamTrailers[appId] = files;
                List<Action<TrailerFiles>> callbacks;
                lock (trailerWaiters)
                {
                    trailerWaiters.TryGetValue(appId, out callbacks);
                    trailerWaiters.Remove(appId);
                }
                OnUi(() =>
                {
                    if (callbacks == null) return;
                    foreach (var cb in callbacks)
                        try { cb(files); } catch (Exception ex) { logger.Warn(ex, "Kippix: trailer callback failed"); }
                });
            });
        }

        private void SetTrailer(Uri video, Uri audio)
        {
            trailerSource = video;
            trailerAudioSource = audio;
            trailerPlaying = false;
            if (trailerAudio != null) { try { trailerAudio.Close(); } catch (Exception) { } }
            if (audio != null)
            {
                if (trailerAudio == null) trailerAudio = new MediaPlayer();
                trailerAudio.Open(audio);
                ApplyTrailerVolume();
            }
            NotifyTrailer();
            if (video != null && settings.DetTrailerAutoplay && !NightNoAutoplayNow)   // (Night mode can hold autoplay back)
                ui.BeginInvoke(new Action(AutoplayTrailer), DispatcherPriority.Loaded);
        }

        private void ApplyTrailerVolume()
        {
            if (trailerAudio != null) trailerAudio.Volume = settings.DetTrailerSound ? 0.8 : 0;
        }

        private MediaElement TrailerElement()
        {
            if (trailerElement != null) return trailerElement;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            trailerElement = win != null ? UiTools.FindByName(win, "KX_Trailer") as MediaElement : null;
            if (trailerElement != null)
            {
                trailerElement.MediaEnded += (s, e) =>
                {
                    if (settings.DetTrailerLoop)
                    {
                        trailerElement.Position = TimeSpan.Zero;
                        trailerElement.Play();
                        if (trailerAudioSource != null && trailerAudio != null) { trailerAudio.Position = TimeSpan.Zero; trailerAudio.Play(); }
                    }
                    else
                    {
                        trailerPlaying = false;
                        if (trailerAudio != null) trailerAudio.Stop();
                        NotifyTrailer();
                        UpdateMusicState();
                    }
                };
                trailerElement.MediaFailed += (s, e) =>
                {
                    logger.Warn("Kippix: trailer couldn't play: " + (e.ErrorException != null ? e.ErrorException.Message : "unknown"));
                    trailerPlaying = false;
                    trailerSource = null;
                    if (trailerAudio != null) trailerAudio.Stop();
                    NotifyTrailer();
                    UpdateMusicState();
                };
            }
            return trailerElement;
        }

        private void PlayTrailer()
        {
            if (trailerSource == null || !detailsOpen) return;
            var m = ActiveTrailer();
            if (m == null) return;
            try
            {
                m.Play();
                if (trailerAudioSource != null && trailerAudio != null)
                {
                    trailerAudio.Position = m.Position;
                    trailerAudio.Play();
                }
                trailerPlaying = true;
            }
            catch (Exception) { trailerPlaying = false; }
            NotifyTrailer();
            UpdateMusicState();
        }

        private void PauseTrailer()
        {
            var m = ActiveTrailer();
            if (m != null) try { m.Pause(); } catch (Exception) { }
            if (trailerAudio != null) try { trailerAudio.Pause(); } catch (Exception) { }
            trailerPlaying = false;
            NotifyTrailer();
            UpdateMusicState();
        }

        private void StopTrailer()
        {
            var m = trailerElement;
            if (m != null) try { m.Stop(); } catch (Exception) { }
            if (trailerFull != null) try { trailerFull.Stop(); } catch (Exception) { }
            if (trailerAudio != null) try { trailerAudio.Stop(); } catch (Exception) { }
            if (!trailerPlaying) return;
            trailerPlaying = false;
            NotifyTrailer();
            UpdateMusicState();
        }

        // ---------------- full screen player ----------------

        private const double TrailerBarWidth = 1600;   // progress bar width in the theme
        private MediaElement trailerFull;
        private DispatcherTimer trailerTick;
        private DateTime trailerLastInput = DateTime.Now;
        private bool trailerControlsShown = true;

        public bool IsTrailerFullOpen { get { return openPanel == "trailer"; } }
        public string TrailerTimeText { get; private set; }
        public double TrailerProgressWidth { get; private set; }

        public ICommand DetTrailerFullscreenCommand { get; private set; }
        public ICommand TrailerBackCommand { get; private set; }
        public ICommand TrailerForwardCommand { get; private set; }
        public ICommand TrailerSoundCommand { get; private set; }
        public ICommand TrailerCloseCommand { get; private set; }

        private void InitTrailerFullCommands()
        {
            DetTrailerFullscreenCommand = new RelayCommand(_ => OpenTrailerFull());
            TrailerBackCommand = new RelayCommand(_ => SeekTrailer(-10));
            TrailerForwardCommand = new RelayCommand(_ => SeekTrailer(10));
            TrailerSoundCommand = new RelayCommand(_ => { DetTrailerSound = !settings.DetTrailerSound; });
            TrailerCloseCommand = new RelayCommand(_ => CloseTrailerFull());
        }

        /// <summary>The player you're watching: the full screen one while it's open, otherwise the one on the game page.</summary>
        private MediaElement ActiveTrailer()
        {
            if (openPanel == "trailer" && TrailerFullElement() != null) return trailerFull;
            return TrailerElement();
        }

        private MediaElement TrailerFullElement()
        {
            if (trailerFull != null) return trailerFull;
            var win = Application.Current != null ? Application.Current.MainWindow : null;
            trailerFull = win != null ? UiTools.FindByName(win, "KX_TrailerFull") as MediaElement : null;
            if (trailerFull != null)
            {
                trailerFull.MediaEnded += (s, e) =>
                {
                    if (settings.DetTrailerLoop)
                    {
                        trailerFull.Position = TimeSpan.Zero;
                        trailerFull.Play();
                        if (trailerAudioSource != null && trailerAudio != null) { trailerAudio.Position = TimeSpan.Zero; trailerAudio.Play(); }
                    }
                    else
                    {
                        trailerPlaying = false;
                        if (trailerAudio != null) trailerAudio.Stop();
                        ShowTrailerControls();
                        NotifyTrailer();
                        UpdateMusicState();
                    }
                };
            }
            return trailerFull;
        }

        private void OpenTrailerFull()
        {
            if (trailerSource == null || openPanel == "trailer") return;
            var small = TrailerElement();
            TimeSpan pos = small != null ? small.Position : TimeSpan.Zero;
            bool wasPlaying = trailerPlaying;
            if (small != null) try { small.Pause(); } catch (Exception) { }
            TogglePanel("trailer");
            ui.BeginInvoke(new Action(() =>
            {
                var full = TrailerFullElement();
                if (full == null) return;
                full.Position = pos;
                if (trailerAudio != null && trailerAudioSource != null) trailerAudio.Position = pos;
                if (wasPlaying || (settings.DetTrailerAutoplay && !NightNoAutoplayNow)) PlayTrailer();
                UiTools.FocusByName("KX_TrailerPlayPause");
                ShowTrailerControls();
                if (trailerTick == null)
                {
                    trailerTick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(250) };
                    trailerTick.Tick += (s, e) => TrailerTick();
                }
                trailerTick.Start();
            }), DispatcherPriority.Loaded);
        }

        /// <summary>Back to the game page, carrying on from the same spot.</summary>
        private void CloseTrailerFull()
        {
            if (openPanel != "trailer") return;
            var full = trailerFull;
            TimeSpan pos = full != null ? full.Position : TimeSpan.Zero;
            bool wasPlaying = trailerPlaying;
            if (full != null) try { full.Stop(); } catch (Exception) { }
            if (trailerTick != null) trailerTick.Stop();
            ClosePanels();
            var small = TrailerElement();
            if (small == null) return;
            small.Position = pos;
            if (trailerAudio != null && trailerAudioSource != null) trailerAudio.Position = pos;
            trailerHeld = false;
            trailerUserPaused = !wasPlaying;   // left paused (or finished) in full screen: stays paused here too
            if (wasPlaying) PlayTrailer(); else { trailerPlaying = false; NotifyTrailer(); }
        }

        private void SeekTrailer(int seconds)
        {
            var m = ActiveTrailer();
            if (m == null) return;
            try
            {
                double len = TrailerLength(m);
                double t = m.Position.TotalSeconds + seconds;
                if (len > 0) t = Math.Min(t, len - 0.5);
                var to = TimeSpan.FromSeconds(Math.Max(0, t));
                m.Position = to;
                if (trailerAudio != null && trailerAudioSource != null) trailerAudio.Position = to;
            }
            catch (Exception) { }
            ShowTrailerControls();
            TrailerTick();
        }

        private double TrailerLength(MediaElement m)
        {
            return m != null && m.NaturalDuration.HasTimeSpan ? m.NaturalDuration.TimeSpan.TotalSeconds : trailerSeconds;
        }

        private void TrailerTick()
        {
            if (openPanel != "trailer") { if (trailerTick != null) trailerTick.Stop(); return; }
            var m = trailerFull;
            if (m == null) return;
            double pos = m.Position.TotalSeconds, len = TrailerLength(m);
            TrailerTimeText = Clock(pos) + (len > 0 ? "  /  " + Clock(len) : "");
            TrailerProgressWidth = len > 0 ? Math.Min(1, pos / len) * TrailerBarWidth : 0;
            Notify("TrailerTimeText");
            Notify("TrailerProgressWidth");
            // controls fade away after a few seconds of watching
            if (trailerControlsShown && trailerPlaying && (DateTime.Now - trailerLastInput).TotalSeconds > 3.5)
            {
                trailerControlsShown = false;
                Fade(UiTools.FindByName(Application.Current.MainWindow, "KX_TrailerControls"), 0);
            }
        }

        private void ShowTrailerControls()
        {
            trailerLastInput = DateTime.Now;
            if (trailerControlsShown) return;
            trailerControlsShown = true;
            Fade(UiTools.FindByName(Application.Current.MainWindow, "KX_TrailerControls"), 1);
        }

        private static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        // ---------------- Steam ----------------

        /// <summary>The game's first Steam store trailer as local files (downloaded once, then reused), or null.</summary>
        private static TrailerFiles GetSteamTrailer(string appId, string cacheDir)
        {
            string dir = Path.Combine(cacheDir, appId);
            string video = Path.Combine(dir, "video.mp4");
            string audio = Path.Combine(dir, "audio.m4a");
            string length = Path.Combine(dir, "seconds.txt");
            string combined = Path.Combine(dir, "trailer.mp4");
            if (File.Exists(combined))
                return new TrailerFiles { Video = combined, Seconds = ReadSeconds(length) };
            if (File.Exists(video))   // downloaded by an older version: rewrite it so it can be skipped through
                return Combine(video, audio, combined, ReadSeconds(length));

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json = Download("https://store.steampowered.com/api/appdetails?filters=movies&appids=" + Uri.EscapeDataString(appId));
            var movie = FirstMovie(json, appId);
            if (movie == null) return null;

            Directory.CreateDirectory(dir);
            // older listings still have a plain .mp4 with sound
            object mp4;
            if (movie.TryGetValue("mp4", out mp4) && mp4 is Dictionary<string, object>)
            {
                var m = (Dictionary<string, object>)mp4;
                object url;
                if ((m.TryGetValue("max", out url) || m.TryGetValue("480", out url)) && url is string)
                {
                    DownloadTo((string)url, video);
                    TrimTrailerCache(cacheDir);
                    return new TrailerFiles { Video = video };
                }
            }
            object mpd;
            if (!movie.TryGetValue("dash_h264", out mpd) || !(mpd is string)) return null;
            double seconds;
            if (!DownloadDash((string)mpd, video, audio, out seconds)) return null;
            try { File.WriteAllText(length, seconds.ToString(CultureInfo.InvariantCulture)); } catch (Exception) { }
            TrimTrailerCache(cacheDir);
            return Combine(video, audio, combined, seconds);
        }

        /// <summary>
        /// Rewrites the joined DASH pieces as one ordinary MP4 with picture and sound together, so the player can
        /// seek (rewind / forward) and the sound can't drift. Keeps the two separate files if that fails.
        /// </summary>
        private static TrailerFiles Combine(string video, string audio, string combined, double seconds)
        {
            try
            {
                Mp4Remux.Remux(video, File.Exists(audio) ? audio : null, combined);
                try { File.Delete(video); } catch (Exception) { }
                try { if (File.Exists(audio)) File.Delete(audio); } catch (Exception) { }
                return new TrailerFiles { Video = combined, Seconds = seconds };
            }
            catch (Exception ex)
            {
                logger.Warn("Kippix: couldn't rewrite the trailer (" + ex.Message + "), playing the pieces as they are");
                try { if (File.Exists(combined + ".part")) File.Delete(combined + ".part"); } catch (Exception) { }
                return new TrailerFiles { Video = video, Audio = File.Exists(audio) ? audio : null, Seconds = seconds };
            }
        }

        private static Dictionary<string, object> FirstMovie(string json, string appId)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            object entry, data, movies;
            if (root == null || !root.TryGetValue(appId, out entry)) return null;
            var e = entry as Dictionary<string, object>;
            if (e == null || !e.TryGetValue("data", out data)) return null;
            var d = data as Dictionary<string, object>;
            if (d == null || !d.TryGetValue("movies", out movies)) return null;
            var list = movies as IEnumerable;
            if (list == null) return null;
            Dictionary<string, object> first = null;
            foreach (object o in list)
            {
                var mv = o as Dictionary<string, object>;
                if (mv == null) continue;
                if (first == null) first = mv;
                object h;
                if (mv.TryGetValue("highlight", out h) && h is bool && (bool)h) return mv;   // the store's featured trailer
            }
            return first;
        }

        /// <summary>
        /// Reads a DASH manifest (SegmentTemplate with $Number$) and joins the 720p video and the audio
        /// pieces into two ordinary files that Windows' player can open.
        /// </summary>
        private static double ReadSeconds(string file)
        {
            try
            {
                double d;
                return File.Exists(file) && double.TryParse(File.ReadAllText(file).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
            }
            catch (Exception) { return 0; }
        }

        private static bool DownloadDash(string mpdUrl, string videoOut, string audioOut, out double seconds)
        {
            seconds = 0;
            var doc = new XmlDocument();
            doc.LoadXml(Download(mpdUrl));
            string baseUrl = mpdUrl.Substring(0, mpdUrl.IndexOf('?') > 0 ? mpdUrl.IndexOf('?') : mpdUrl.Length);
            baseUrl = baseUrl.Substring(0, baseUrl.LastIndexOf('/') + 1);
            string query = mpdUrl.IndexOf('?') > 0 ? mpdUrl.Substring(mpdUrl.IndexOf('?')) : "";

            TimeSpan total = TimeSpan.Zero;
            var mpdNode = doc.DocumentElement;
            if (mpdNode.HasAttribute("mediaPresentationDuration"))
                total = XmlConvert.ToTimeSpan(mpdNode.GetAttribute("mediaPresentationDuration"));
            seconds = total.TotalSeconds;

            XmlElement bestVideo = null, audio = null;
            int bestScore = int.MaxValue;
            foreach (XmlElement rep in doc.GetElementsByTagName("*").OfType<XmlElement>().Where(x => x.LocalName == "Representation"))
            {
                string mime = Attr(rep, "mimeType") ?? Attr(rep.ParentNode as XmlElement, "mimeType") ?? "";
                if (mime.StartsWith("video/mp4"))
                {
                    int h;
                    int.TryParse(Attr(rep, "height") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out h);
                    int score = Math.Abs(h - 720);
                    if (score < bestScore) { bestScore = score; bestVideo = rep; }
                }
                else if (mime.StartsWith("audio/mp4") && audio == null) audio = rep;
            }
            if (bestVideo == null) return false;
            // the sound downloads alongside the picture
            Thread soundThread = null;
            if (audio != null)
            {
                var a = audio;
                soundThread = new Thread(() =>
                {
                    try { if (!DownloadRepresentation(a, baseUrl, query, total, audioOut)) File.Delete(audioOut); }
                    catch (Exception) { try { File.Delete(audioOut); } catch (Exception) { } }
                }) { IsBackground = true };
                soundThread.Start();
            }
            bool ok = DownloadRepresentation(bestVideo, baseUrl, query, total, videoOut);
            if (soundThread != null) soundThread.Join();
            return ok;
        }

        private static bool DownloadRepresentation(XmlElement rep, string baseUrl, string query, TimeSpan total, string outFile)
        {
            XmlElement tpl = Child(rep, "SegmentTemplate") ?? Child(rep.ParentNode as XmlElement, "SegmentTemplate");
            if (tpl == null) return false;
            string id = Attr(rep, "id");
            string init = Attr(tpl, "initialization");
            string media = Attr(tpl, "media");
            double timescale = double.Parse(Attr(tpl, "timescale") ?? "1", CultureInfo.InvariantCulture);
            double duration = double.Parse(Attr(tpl, "duration") ?? "0", CultureInfo.InvariantCulture);
            int start = int.Parse(Attr(tpl, "startNumber") ?? "1", CultureInfo.InvariantCulture);
            if (init == null || media == null || duration <= 0 || total <= TimeSpan.Zero) return false;
            int count = (int)Math.Ceiling(total.TotalSeconds / (duration / timescale));

            // every piece's address, then six downloads at a time (one after another took most of the wait)
            var urls = new List<string> { baseUrl + Fill(init, id, 0) + query };
            for (int n = start; n < start + count; n++) urls.Add(baseUrl + Fill(media, id, n) + query);
            var parts = new byte[urls.Count][];
            int next = -1;
            Exception failed = null;
            var workers = new List<Thread>();
            for (int w = 0; w < Math.Min(6, urls.Count); w++)
            {
                var t = new Thread(() =>
                {
                    using (var web = new WebClient())
                    {
                        while (true)
                        {
                            int i = Interlocked.Increment(ref next);
                            if (i >= urls.Count || failed != null) return;
                            try
                            {
                                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 Kippix";
                                parts[i] = web.DownloadData(urls[i]);
                            }
                            catch (Exception ex) { failed = ex; return; }
                        }
                    }
                }) { IsBackground = true };
                t.Start();
                workers.Add(t);
            }
            foreach (var t in workers) t.Join();
            if (failed != null) throw failed;

            string tmp = outFile + ".part";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                foreach (var b in parts) fs.Write(b, 0, b.Length);
            if (File.Exists(outFile)) File.Delete(outFile);
            File.Move(tmp, outFile);
            return true;
        }

        /// <summary>Fills $RepresentationID$ and $Number$ / $Number%05d$ in a DASH segment template.</summary>
        private static string Fill(string template, string id, int number)
        {
            string s = template.Replace("$RepresentationID$", id ?? "");
            int i;
            while ((i = s.IndexOf("$Number", StringComparison.Ordinal)) >= 0)
            {
                int end = s.IndexOf('$', i + 1);
                if (end < 0) break;
                string token = s.Substring(i + 1, end - i - 1);   // Number or Number%05d
                string num = number.ToString(CultureInfo.InvariantCulture);
                int pct = token.IndexOf('%');
                if (pct >= 0)
                {
                    int width;
                    string fmt = token.Substring(pct + 1).TrimEnd('d');
                    if (int.TryParse(fmt, NumberStyles.Integer, CultureInfo.InvariantCulture, out width)) num = number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
                }
                s = s.Substring(0, i) + num + s.Substring(end + 1);
            }
            return s;
        }

        private static string Attr(XmlElement e, string name)
        {
            return e != null && e.HasAttribute(name) ? e.GetAttribute(name) : null;
        }

        private static XmlElement Child(XmlElement e, string localName)
        {
            if (e == null) return null;
            foreach (XmlNode n in e.ChildNodes)
                if (n is XmlElement && n.LocalName == localName) return (XmlElement)n;
            return null;
        }

        private static string Download(string url)
        {
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 Kippix";
                web.Encoding = System.Text.Encoding.UTF8;
                return web.DownloadString(url);
            }
        }

        private static void DownloadTo(string url, string file)
        {
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 Kippix";
                web.DownloadFile(url.Replace("http://", "https://"), file + ".part");
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(file + ".part", file);
        }

        /// <summary>Keeps the newest few trailers on disk and deletes the rest.</summary>
        private static void TrimTrailerCache(string cacheDir)
        {
            try
            {
                var dirs = new DirectoryInfo(cacheDir).GetDirectories().OrderByDescending(d => d.LastWriteTimeUtc).ToList();
                foreach (var d in dirs.Skip(TrailerCacheKeep)) try { d.Delete(true); } catch (Exception) { }
            }
            catch (Exception) { }
        }
    }
}
