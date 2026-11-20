using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KippixHelper
{
    /// <summary>
    /// Audio settings: menu sounds (the theme ships silent copies of Playnite's own, so these replace them),
    /// home screen music, plus General settings (labels on icons) and Exit Playnite.
    /// </summary>
    public partial class ThemeApi
    {
        // "Classic" is Playnite's own sound from its Default theme; the rest ship with Kippix (Audio\Packs).
        private static readonly string[] SoundPacks = { "Classic", "Soft", "Pop", "Retro", "Glass" };
        private static readonly double[] Volumes = { 0.25, 0.5, 0.75, 1.0 };

        public ICommand CycleSoundPackCommand { get; private set; }
        public ICommand CycleSoundVolumeCommand { get; private set; }
        public ICommand CycleMusicModeCommand { get; private set; }
        public ICommand CycleMusicVolumeCommand { get; private set; }
        public ICommand NextMusicCommand { get; private set; }
        public ICommand ExitPlayniteCommand { get; private set; }

        private void InitSoundCommands()
        {
            CycleSoundPackCommand = new RelayCommand(_ =>
            {
                int i = Array.IndexOf(SoundPacks, settings.SoundPack);
                settings.SoundPack = SoundPacks[(i + 1) % SoundPacks.Length];
                SaveSettings();
                LoadSoundPack();
                NotifySound();
                PlaySound("select");   // let you hear the new style
            });
            CycleSoundVolumeCommand = new RelayCommand(_ => PlaySound("select"));
            CycleMusicModeCommand = new RelayCommand(_ => { settings.MusicMode = NextMusicMode(settings.MusicMode, UpsInstalled); SaveSettings(); UpsQuietCheck(); NotifySound(); RestartMusic(); });
            CycleMusicVolumeCommand = new RelayCommand(_ => { });
            NextMusicCommand = new RelayCommand(_ => { if (MusicUps) UpsCommand("skip"); else if (settings.MusicMode != 0) NextTrack(); });
            ExitPlayniteCommand = new RelayCommand(_ => WithOutro("exit", AskExitPlaynite));   // (the outro first, when there is one)
        }

        // ---------------- settings shown in the options ----------------

        public bool SoundEffects
        {
            get { return settings.SoundEffects; }
            set { if (SetSetting(ref settings.SoundEffects, value)) NotifySound(); }
        }
        public bool ShowTooltips
        {
            get { return settings.ShowTooltips; }
            set { SetSetting(ref settings.ShowTooltips, value); Notify("ShowTooltips"); }
        }

        public string SoundPackText
        {
            get
            {
                switch (settings.SoundPack)
                {
                    case "Soft": return Loc("LOC_KX_PackSoft", "Soft");
                    case "Pop": return Loc("LOC_KX_PackPop", "Pop");
                    case "Retro": return Loc("LOC_KX_PackRetro", "Retro");
                    case "Glass": return Loc("LOC_KX_PackGlass", "Glass");
                }
                return Loc("LOC_KX_PackClassic", "Playnite classic");
            }
        }
        public string SoundVolumeText { get { return settings.SoundVolume + "%"; } }
        public string MusicVolumeText { get { return MusicVolumeShown + "%"; } }
        /// <summary>(0.98) With UniPlaySong as the music, the volume bar is its volume.</summary>
        private int MusicVolumeShown { get { return MusicUps ? UpsInt("MusicVolume", 50) : settings.MusicVolume; } }

        // volume bars (480 px wide in the theme)
        public double SoundFillWidth { get { return settings.SoundVolume / 100.0 * 480; } }
        public Thickness SoundKnobMargin { get { return new Thickness(settings.SoundVolume / 100.0 * 480 - 12, 0, 0, 0); } }
        public double MusicFillWidth { get { return MusicVolumeShown / 100.0 * 480; } }
        public Thickness MusicKnobMargin { get { return new Thickness(MusicVolumeShown / 100.0 * 480 - 12, 0, 0, 0); } }

        private void NudgeVolume(bool sound, int delta)
        {
            if (sound)
            {
                int v = Math.Max(0, Math.Min(100, settings.SoundVolume + delta));
                if (v == settings.SoundVolume) return;
                settings.SoundVolume = v;
                PlaySound("move");
            }
            else if (MusicUps)
            {
                int v = Math.Max(0, Math.Min(100, UpsInt("MusicVolume", 50) + delta));
                if (v == UpsInt("MusicVolume", 50)) return;
                UpsSet("MusicVolume", v);
            }
            else
            {
                int v = Math.Max(0, Math.Min(100, settings.MusicVolume + delta));
                if (v == settings.MusicVolume) return;
                settings.MusicVolume = v;
                if (music != null && musicPlaying) music.Volume = musicLevel * MusicVolumeNow / 100.0 * 0.6;   // (MusicVolumeNow: quieter in Night mode, ThemeApi.NightMode.cs)
            }
            SaveSettings();
            NotifySound();
        }
        public string MusicModeText
        {
            get
            {
                return settings.MusicMode == 1 ? Loc("LOC_KX_MusicPlaynite", "Playnite's music")
                     : settings.MusicMode == 2 ? Loc("LOC_KX_MusicMine", "My music")
                     : settings.MusicMode == 3 ? (UpsInstalled ? "UniPlaySong" : Loc("LOC_KX_MusicUpsGone", "UniPlaySong (not installed)"))
                     : Loc("LOC_KX_Off", "Off");
            }
        }
        public bool MusicOn { get { return settings.MusicMode != 0 && (settings.MusicMode != 3 || UpsInstalled); } }
        public bool MusicMine { get { return settings.MusicMode == 2; } }
        public string MusicFolderText { get { return string.Format(Loc("LOC_KX_MusicFolderHelp", "Put .mp3 files in: {0}"), MusicFolder); } }
        public string NowPlayingText { get; private set; }
        public string UpsNowPlayingText { get { string np = MusicUps ? UpsNowPlaying : ""; return !string.IsNullOrEmpty(np) ? np : Loc("LOC_KX_UpsOptionsSub", "What it plays and when"); } }

        private static int Clamp(int i) { return Math.Max(0, Math.Min(Volumes.Length - 1, i)); }

        private void NotifySound()
        {
            foreach (string n in new[] { "SoundEffects", "SoundPackText", "SoundVolumeText", "MusicVolumeText", "MusicModeText", "MusicOn", "MusicMine", "MusicUps", "UpsNowPlayingText", "MusicFolderText", "NowPlayingText",
                                         "SoundFillWidth", "SoundKnobMargin", "MusicFillWidth", "MusicKnobMargin" })
                Notify(n);
        }

        // ---------------- menu sounds ----------------

        private readonly Dictionary<string, MediaPlayer[]> soundPlayers = new Dictionary<string, MediaPlayer[]>();
        private readonly Dictionary<string, WavSound> wavSounds = new Dictionary<string, WavSound>();   // played by Windows (reliable); MediaPlayer only for files it can't take
        private readonly Dictionary<string, int> soundNext = new Dictionary<string, int>();
        private DateTime lastMoveSound = DateTime.MinValue;
        private DateTime lastActionSound = DateTime.MinValue;
        private readonly Dictionary<string, DateTime> soundLastAt = new Dictionary<string, DateTime>();
        private object lastFocused;

        private string themeDirFound;

        /// <summary>The theme's folder: Themes\Fullscreen\Kippix_Kipping0 (its id since 1.0.0).</summary>
        private string ThemeDir
        {
            get
            {
                if (themeDirFound != null) return themeDirFound;
                string root = Path.Combine(api.Paths.ConfigurationPath, "Themes", "Fullscreen");
                string dir = Path.Combine(root, "Kippix_Kipping0");
                try { if (File.Exists(Path.Combine(dir, "theme.yaml"))) themeDirFound = dir; }
                catch (Exception) { }
                return dir;
            }
        }

        private string SoundFile(string kind)
        {
            if (settings.SoundPack == "Classic" || !SoundPacks.Contains(settings.SoundPack))
            {
                // Playnite's own sounds from its Default theme
                string dir = Path.Combine(api.Paths.ApplicationPath, "Themes", "Fullscreen", "Default", "Audio");
                return Path.Combine(dir, kind == "move" ? "navigation.wav" : "activation.wav");
            }
            return Path.Combine(ThemeDir, "Audio", "Packs", settings.SoundPack, kind + ".wav");
        }

        private void LoadSoundPack()
        {
            foreach (var arr in soundPlayers.Values) foreach (var p in arr) try { p.Close(); } catch (Exception) { }
            soundPlayers.Clear();
            foreach (var w in wavSounds.Values) try { w.Dispose(); } catch (Exception) { }
            wavSounds.Clear();
            foreach (string kind in new[] { "move", "select", "back" })
            {
                string file = SoundFile(kind);
                if (!File.Exists(file)) continue;
                var wav = WavSound.Load(file);
                if (wav != null) { wavSounds[kind] = wav; continue; }
                // a few players per sound so quick presses can overlap
                var arr = new MediaPlayer[3];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i] = new MediaPlayer();
                    arr[i].Open(new Uri(file));
                }
                soundPlayers[kind] = arr;
                soundNext[kind] = 0;
            }
        }

        private void PlaySound(string kind)
        {
            try
            {
                if (!settings.SoundEffects || gameRunningNow || muteTourSounds > 0) return;   // (0.97: not for the starting look's previews)
                // one press, one sound: the same sound asked for twice at once (the button's own click sound and the
                // action's, or a press that arrived twice) plays once; a select / back also covers a move right after it
                var now = DateTime.Now;
                DateTime last;
                if (soundLastAt.TryGetValue(kind, out last) && (now - last).TotalMilliseconds < 200) { Dbg("sound " + kind + " (twice at once: once)"); return; }
                if (kind == "move" && (now - lastActionSound).TotalMilliseconds < 150) return;
                soundLastAt[kind] = now;
                if (kind != "move") lastActionSound = now;
                WavSound wav;
                if (wavSounds.TryGetValue(kind, out wav)) { wav.Play(SoundVolumeNow); return; }   // (quieter in Night mode)
                MediaPlayer[] arr;
                if (!soundPlayers.TryGetValue(kind, out arr)) return;
                int i = soundNext[kind];
                soundNext[kind] = (i + 1) % arr.Length;
                var p = arr[i];
                p.Volume = SoundVolumeNow / 100.0;
                p.Stop();   // (rewinding a finished player without stopping it sometimes stayed silent)
                p.Position = TimeSpan.Zero;
                p.Play();
            }
            catch (Exception) { }
        }

        private void StartSounds()
        {
            LoadSoundPack();
            EventManager.RegisterClassHandler(typeof(Window), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnAnyFocusSound), true);
            EventManager.RegisterClassHandler(typeof(Window), ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => PlaySound("select")), true);
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent, new ToolTipEventHandler(OnToolTipOpening), true);
            StartMusic();
        }

        /// <summary>Every focus move in Playnite's windows (ours and its menus) gets the "move" sound.</summary>
        private void OnAnyFocusSound(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus == null || ReferenceEquals(e.NewFocus, lastFocused)) return;
            lastFocused = e.NewFocus;
            if ((DateTime.Now - lastMoveSound).TotalMilliseconds < 45) return;
            // pressing something moves the highlight into what it opened: one sound for that (the press), not two
            if ((DateTime.Now - lastActionSound).TotalMilliseconds < 250) return;
            lastMoveSound = DateTime.Now;
            PlaySound("move");
        }

        private void OnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (!settings.ShowTooltips) e.Handled = true;   // labels on icons switched off in General settings
            else if (ambientOn) e.Handled = true;           // none over the screensaver
        }

        /// <summary>
        /// Takes down any label (tooltip) that's up. Each one is a small window of its own over Playnite's, so the
        /// screensaver, which is drawn in the main window, comes up under it and the label stays on the screen.
        /// </summary>
        private static void CloseLabels()
        {
            var open = new List<ToolTip>();   // (all found first: closing one takes its window away)
            try
            {
                foreach (PresentationSource source in PresentationSource.CurrentSources)
                {
                    if (source == null || !source.CheckAccess()) continue;   // (a window on another thread, the overlay's)
                    var tip = FindLabel(source.RootVisual, 4);
                    if (tip != null && tip.IsOpen) open.Add(tip);
                }
            }
            catch (Exception) { }
            foreach (var tip in open)
                try { tip.IsOpen = false; } catch (Exception) { }
        }

        /// <summary>The label at the top of a popup's window (popup root, its decorators, then the ToolTip).</summary>
        private static ToolTip FindLabel(DependencyObject d, int depth)
        {
            if (d == null) return null;
            var tip = d as ToolTip;
            if (tip != null) return tip;
            if (depth == 0 || !(d is Visual)) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                tip = FindLabel(VisualTreeHelper.GetChild(d, i), depth - 1);
                if (tip != null) return tip;
            }
            return null;
        }

        // ---------------- music ----------------

        private MediaPlayer music;
        private List<string> playlist = new List<string>();
        private int track = -1;
        private bool musicPlaying;
        private readonly Random rng = new Random();
        private DispatcherTimer musicFade;
        private double musicLevel;          // 0..1 multiplier used for fading in and out

        private string MusicFolder { get { return Path.Combine(plugin.GetPluginUserDataPath(), "Music"); } }

        private string PlayniteMusic
        {
            get { return Path.Combine(api.Paths.ApplicationPath, "Themes", "Fullscreen", "Default", "Audio", "background.mp3"); }
        }

        private void StartMusic()
        {
            try { Directory.CreateDirectory(MusicFolder); } catch (Exception) { }
            if (api.ApplicationInfo.Mode != Playnite.SDK.ApplicationMode.Fullscreen) return;
            music = new MediaPlayer();
            music.MediaEnded += (s, e) => NextTrack();
            musicFade = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(60) };
            musicFade.Tick += (s, e) => FadeStep();
            var w = Application.Current.MainWindow;
            if (w != null)
            {
                w.Activated += (s, e) => UpdateMusicState();
                w.Deactivated += (s, e) => UpdateMusicState();
            }
            // Playnite starts its music again by itself (back from a game, its window in front again, its volume changed): looked at every second too
            playniteMusicWatch = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
            playniteMusicWatch.Tick += (s, e) => QuietPlayniteMusic();
            playniteMusicWatch.Start();
            RestartMusic();
        }

        private void RestartMusic()
        {
            if (music == null) return;
            playlist.Clear();
            if (settings.MusicMode == 1 && File.Exists(PlayniteMusic)) playlist.Add(PlayniteMusic);
            if (settings.MusicMode == 2)
            {
                try
                {
                    playlist = Directory.GetFiles(MusicFolder)
                        .Where(f => new[] { ".mp3", ".wma", ".m4a", ".wav" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => rng.Next()).ToList();
                }
                catch (Exception) { }
            }
            track = -1;
            musicPlaying = false;
            music.Stop();
            QuietPlayniteMusic();
            if (playlist.Count == 0)
            {
                NowPlayingText = settings.MusicMode == 2 ? Loc("LOC_KX_MusicEmpty", "No music in the folder yet") : "";
                NotifySound();
                return;
            }
            NextTrack();
        }

        private void NextTrack()
        {
            if (music == null || playlist.Count == 0) return;
            track = (track + 1) % playlist.Count;
            music.Open(new Uri(playlist[track]));
            NowPlayingText = settings.MusicMode == 2 ? Path.GetFileNameWithoutExtension(playlist[track]) : "";
            NotifySound();
            musicPlaying = false;
            UpdateMusicState();
        }

        /// <summary>Music plays in fullscreen except while a game runs, Playnite is in the background, or a trailer with sound plays.</summary>
        private void UpdateMusicState()
        {
            try { UpsQuietCheck(); } catch (Exception) { }   // (0.98) UniPlaySong quietens for the same things
            if (music == null) return;
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            bool should = settings.MusicMode != 0 && playlist.Count > 0 && !gameRunningNow
                          && w != null && w.IsActive && !(trailerPlaying && settings.DetTrailerSound) && !PreviewHasSound && !StoreSoundActive && !GallerySoundActive && !IntroSoundActive && !LocalPlayerActive;
            if (should && !musicPlaying) { music.Volume = 0; musicLevel = 0; music.Play(); musicPlaying = true; }
            musicFade.Start();
            QuietPlayniteMusic();
        }

        private void FadeStep()
        {
            var w = Application.Current != null ? Application.Current.MainWindow : null;
            bool should = settings.MusicMode != 0 && musicPlaying && !gameRunningNow && w != null && w.IsActive
                          && !(trailerPlaying && settings.DetTrailerSound) && !PreviewHasSound && !StoreSoundActive && !GallerySoundActive && !IntroSoundActive && !LocalPlayerActive;
            double target = should ? 1 : 0;
            musicLevel += (target - musicLevel) * 0.25;
            if (Math.Abs(target - musicLevel) < 0.02) musicLevel = target;
            music.Volume = musicLevel * MusicVolumeNow / 100.0 * 0.6;   // (MusicVolumeNow: quieter in Night mode, ThemeApi.NightMode.cs)
            if (musicLevel == target)
            {
                musicFade.Stop();
                if (target == 0 && musicPlaying) { music.Pause(); musicPlaying = false; }
            }
        }

        // (issue #11) "a game is running" used to be one yes / no. anyRunning is that, strictly (something in Running.cs). With
        // "Keep browsing while things run" on, gameRunningNow is only true while Playnite is NOT the window in front, so the music,
        // sounds, guide bar, cards... are as usual while you browse with apps open. Off: the two are the same, as before.
        private volatile bool anyRunning, playniteActive;
        private bool AnyRunning { get { return anyRunning; } }
        private bool gameRunningNow { get { return anyRunning && !(settings.RunKeepBrowsing && playniteActive); } }

        // ---------------- Playnite's own background music ----------------

        private DispatcherTimer playniteMusicWatch;
        private bool playniteAudioLooked;
        private object playniteAudio;
        private MethodInfo pnMusicPlaying, pnMusicPaused, pnMusicPause, pnMusicResume;
        private bool playniteMusicPausedByUs;

        /// <summary>Playnite's audio engine (PlayniteApplication.Current.Audio): not in the SDK, so it's found by name.</summary>
        private bool FindPlayniteAudio()
        {
            if (playniteAudio != null) return true;
            if (playniteAudioLooked) return false;
            playniteAudioLooked = true;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Playnite.PlayniteApplication", false);
                    if (t == null) continue;
                    PropertyInfo cur = t.GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    object app = cur != null ? cur.GetValue(null, null) : null;
                    PropertyInfo ap = app != null ? app.GetType().GetProperty("Audio", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy) : null;
                    object audio = ap != null ? ap.GetValue(app, null) : null;
                    if (audio == null) { playniteAudioLooked = false; return false; }   // Playnite has not made it yet (asked early at start-up): looked for again next time
                    Type at = audio.GetType();
                    pnMusicPlaying = at.GetMethod("GetIsMusicPlaying", Type.EmptyTypes);
                    pnMusicPaused = at.GetMethod("GetIsMusicPaused", Type.EmptyTypes);
                    pnMusicPause = at.GetMethod("PauseMusic", Type.EmptyTypes);
                    pnMusicResume = at.GetMethod("ResumeMusic", Type.EmptyTypes);
                    if (pnMusicPlaying != null && pnMusicPaused != null && pnMusicPause != null && pnMusicResume != null) playniteAudio = audio;
                    break;
                }
            }
            catch (Exception ex) { logger.Warn("Kippix: Playnite's music can't be reached: " + ex.Message); }
            if (playniteAudio == null) logger.Info("Kippix: Playnite's audio engine wasn't found: its own music is left alone");
            return playniteAudio != null;
        }

        /// <summary>
        /// Playnite plays its own background music whatever Music is set to here: with Off it kept playing, and with
        /// "Playnite's music" (the same file, played by our player) or My music it would play on top of ours. So it's paused,
        /// except with UniPlaySong, which deals with Playnite's music itself. Playnite resumes it on its own
        /// (after a game, when its window comes back), so this is asked again each time, not once.
        /// </summary>
        private void QuietPlayniteMusic()
        {
            if (music == null) return;   // fullscreen only
            try
            {
                if (!FindPlayniteAudio()) return;
                if (!MusicUps)
                {
                    if ((bool)pnMusicPlaying.Invoke(playniteAudio, null) && !(bool)pnMusicPaused.Invoke(playniteAudio, null))
                    {
                        pnMusicPause.Invoke(playniteAudio, null);
                        playniteMusicPausedByUs = true;
                    }
                }
                else if (playniteMusicPausedByUs)
                {
                    // UniPlaySong was just picked: Playnite's music goes back to how it was (not under a running game, Playnite keeps it paused there)
                    playniteMusicPausedByUs = false;
                    if (!gameRunningNow && (bool)pnMusicPaused.Invoke(playniteAudio, null)) pnMusicResume.Invoke(playniteAudio, null);
                }
            }
            catch (Exception) { }
        }

        // ---------------- exit ----------------

        private void AskExitPlaynite()
        {
            ClosePanels();   // no "are you sure?": choosing Exit Playnite in the options is the confirmation
            SaveSettings();
            FlushSettings(true);
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("Playnite.PlayniteApplication", false);
                    if (t == null) continue;
                    object app = t.GetProperty("Current", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
                    MethodInfo quit = t.GetMethod("Quit", Type.EmptyTypes);
                    if (app != null && quit != null) { quit.Invoke(app, null); return; }
                }
            }
            catch (Exception ex) { logger.Error(ex, "Kippix: Playnite's Quit failed"); }
            Application.Current.Shutdown();
        }
    }
}
