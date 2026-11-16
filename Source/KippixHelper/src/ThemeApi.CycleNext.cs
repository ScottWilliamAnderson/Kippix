using System;
using System.Collections.Generic;

namespace KippixHelper
{
    /// <summary>
    /// The rows that step through a list of values on A (Design, Games shown as, Background...) say what A changes the value
    /// to: dim, after the value it has now ("Lower  ›  Tall"). A row's value is its ...Text property; what comes next is the
    /// property of the same name with Next on the end, here: one line per row, stepping the setting the way its command does.
    /// The theme shows it with the KX_CycleNext style. Nothing here changes a setting or what the commands do.
    /// </summary>
    public partial class ThemeApi
    {
        // Home settings › Game cards and layout
        public string HomeLayoutTextNext { get { return NextText(settings.HomeLayoutMode, (settings.HomeLayoutMode + 1) % 6, v => settings.HomeLayoutMode = v, () => HomeLayoutText); } }
        public string RowDesignTextNext { get { return NextText(settings.RowDesign, (settings.RowDesign + 1) % 3, v => settings.RowDesign = v, () => RowDesignText); } }
        public string RowFullCountTextNext { get { int n = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.RowFullCount)); return NextText(settings.RowFullCount, n >= FullCountMax ? FullCountMin : n + 1, v => settings.RowFullCount = v, () => RowFullCountText); } }
        public string RowTallCountTextNext { get { int n = Math.Max(1, Math.Min(10, settings.RowTallCount)); return NextText(settings.RowTallCount, n >= 10 ? 1 : n + 1, v => settings.RowTallCount = v, () => RowTallCountText); } }
        public string GridRowsTextNext { get { return NextText(settings.GridRows, settings.GridRows % 4 + 1, v => settings.GridRows = v, () => GridRowsText); } }
        public string GridColumnsTextNext { get { return NextText(settings.GridColumns, settings.GridColumns >= 10 ? 3 : settings.GridColumns + 1, v => settings.GridColumns = v, () => GridColumnsText); } }
        public string HomeTilesModeTextNext { get { return NextText(settings.HomeTilesMode, (settings.HomeTilesMode + 1) % 3, v => settings.HomeTilesMode = v, () => HomeTilesModeText); } }
        public string PreviewModeTextNext { get { return NextText(settings.PreviewMode, (settings.PreviewMode + 1) % 3, v => settings.PreviewMode = v, () => PreviewModeText); } }
        // Folder
        public string FolderTileCountTextNext { get { int n = Math.Max(FullCountMin, Math.Min(FullCountMax, settings.FolderTileCount)); return NextText(settings.FolderTileCount, n >= FullCountMax ? FullCountMin : n + 1, v => settings.FolderTileCount = v, () => FolderTileCountText); } }
        public string FolderDesignTextNext { get { return NextText(settings.FolderGameDesign, (Math.Max(0, Math.Min(4, settings.FolderGameDesign)) + 1) % 5, v => settings.FolderGameDesign = v, () => FolderDesignText); } }
        public string FolderSortTextNext { get { return NextText(settings.FolderSort, (FolderSortNow + 1) % 5, v => settings.FolderSort = v, () => FolderSortText); } }
        // Home settings › Background
        public string HomeBgTextNext { get { return NextText(settings.HomeBgMode, NextHomeBgMode(settings.HomeBgMode), v => settings.HomeBgMode = v, () => HomeBgText); } }
        public string HomeFadeDirTextNext { get { return NextText(settings.HomeFadeDir, (settings.HomeFadeDir + 1) % 5, v => settings.HomeFadeDir = v, () => HomeFadeDirText); } }
        public string DepthTextNext { get { return NextText(settings.DepthMode, (settings.DepthMode + 1) % 4, v => settings.DepthMode = v, () => DepthText); } }
        // Game page, sound, gallery, look, notifications
        public string DetArtTextNext { get { return NextText(settings.DetArt, (settings.DetArt + 1) % 3, v => settings.DetArt = v, () => DetArtText); } }
        public string DetMediaSizeTextNext { get { return NextText(settings.DetMediaSize, (settings.DetMediaSize + 1) % 4, v => settings.DetMediaSize = v, () => DetMediaSizeText); } }
        public string MusicModeTextNext { get { return NextText(settings.MusicMode, NextMusicMode(settings.MusicMode, UpsInstalled), v => settings.MusicMode = v, () => MusicModeText); } }
        public string SlideSecondsTextNext { get { return NextText(settings.GallerySlideSeconds, NextIn(new[] { 3, 6, 10, 15 }, settings.GallerySlideSeconds), v => settings.GallerySlideSeconds = v, () => SlideSecondsText); } }
        public string ButtonIconsTextNext { get { return NextText(settings.ButtonIcons, (settings.ButtonIcons + 1) % 5, v => settings.ButtonIcons = v, () => ButtonIconsText); } }
        public string NotifyPosTextNext { get { return NextText(settings.NotifyPos, (Clamp(settings.NotifyPos, 0, 5) + 1) % 6, v => settings.NotifyPos = v, () => NotifyPosText); } }
        public string NotifyStyleTextNext { get { return NextText(settings.NotifyStyle, (Clamp(settings.NotifyStyle, 0, 3) + 1) % 4, v => settings.NotifyStyle = v, () => NotifyStyleText); } }
        public string NotifySizeTextNext { get { return NextText(settings.NotifySize, (Clamp(settings.NotifySize, 0, 3) + 1) % 4, v => settings.NotifySize = v, () => NotifySizeText); } }
        public string NotifySecondsTextNext { get { return NextText(settings.NotifySeconds, NextIn(new[] { 5, 8, 12, 20, 30 }, settings.NotifySeconds), v => settings.NotifySeconds = v, () => NotifySecondsText); } }
        public string AccentStrengthTextNext { get { return NextText(settings.AccentStrength, (settings.AccentStrength + 1) % 3, v => settings.AccentStrength = v, () => AccentStrengthText); } }
        public string MotionSpeedTextNext { get { int m = settings.MotionSpeed; return NextText(m, m == 3 ? 0 : m == 2 ? 3 : m + 1, v => settings.MotionSpeed = v, () => MotionSpeedText); } }
        public string MotionAmountTextNext { get { return NextText(settings.MotionAmount, (settings.MotionAmount + 1) % 3, v => settings.MotionAmount = v, () => MotionAmountText); } }
        public string ReminderTextNext { get { return NextText(settings.SessionReminderMinutes, NextIn(new[] { 30, 60, 90, 120, 180 }, settings.SessionReminderMinutes), v => settings.SessionReminderMinutes = v, () => ReminderText); } }
        // Ambient mode
        public string AmbientMinutesTextNext { get { return NextText(settings.AmbientMinutes, NextIn(new[] { 2, 5, 10, 15, 30 }, settings.AmbientMinutes), v => settings.AmbientMinutes = v, () => AmbientMinutesText); } }
        public string AmbientSecondsTextNext { get { return NextText(settings.AmbientSeconds, NextIn(new[] { 6, 8, 12, 20, 30, 60 }, settings.AmbientSeconds), v => settings.AmbientSeconds = v, () => AmbientSecondsText); } }
        public string AmbientPosTextNext { get { return NextText(settings.AmbientPos, (AmbientPosNow + 1) % 9, v => settings.AmbientPos = v, () => AmbientPosText); } }
        public string AmbientBgTextNext { get { return NextText(settings.AmbientBg, (AmbientBgNow + 1) % AmbientBgKeys.Length, v => settings.AmbientBg = v, () => AmbientBgText); } }
        // the Store
        public string StoreImageQualityTextNext { get { return NextText(settings.StoreImageQuality, (Math.Max(0, Math.Min(2, settings.StoreImageQuality)) + 1) % 3, v => settings.StoreImageQuality = v, () => StoreImageQualityText); } }
        // Top bar settings (a switch between two, shown as a value)
        public string ClockModeTextNext { get { HookCycleNext(); return "›  " + (settings.Clock24h ? Loc("LOC_KX_Clock12", "12-hour") : Loc("LOC_KX_Clock24", "24-hour")); } }
        public string CursorSpeedTextNext { get { return NextText(settings.BrowserCursorSpeed, settings.BrowserCursorSpeed % 5 + 1, v => settings.BrowserCursorSpeed = v, () => CursorSpeedText); } }

        private bool cycleNextHooked;
        private readonly Dictionary<string, bool> cycleNextHas = new Dictionary<string, bool>();

        /// <summary>
        /// "›  " and what <paramref name="text"/> (a row's ...Text property) says with the setting at <paramref name="next"/>,
        /// or nothing when that reads the same as now. The setting is put there only to read the text, then put straight back:
        /// nothing is saved, applied or notified (the settings are only read and written on the UI thread, as this is).
        /// </summary>
        private string NextText(int now, int next, Action<int> set, Func<string> text)
        {
            HookCycleNext();
            try
            {
                string have = text();
                string then;
                set(next);
                try { then = text(); } finally { set(now); }
                return string.IsNullOrEmpty(then) || then == have ? "" : "›  " + then;
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Whenever a ...Text property is said to have changed, its ...TextNext (if it has one) has changed too.</summary>
        private void HookCycleNext()
        {
            if (cycleNextHooked) return;
            cycleNextHooked = true;
            PropertyChanged += (s, e) =>
            {
                string name = e.PropertyName;
                if (string.IsNullOrEmpty(name) || !name.EndsWith("Text", StringComparison.Ordinal)) return;
                bool has;
                if (!cycleNextHas.TryGetValue(name, out has)) cycleNextHas[name] = has = GetType().GetProperty(name + "Next") != null;
                if (has) Notify(name + "Next");
            };
        }
    }
}
