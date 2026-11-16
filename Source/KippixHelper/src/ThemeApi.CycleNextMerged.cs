namespace KippixHelper
{
    /// <summary>
    /// What A changes the value to (ThemeApi.CycleNext.cs), for rows that come from other branches than that one: they can't
    /// be written on either branch alone (the hint needs both), so they live where the branches meet.
    ///   - Top bar settings › Battery shown (feat/device-battery)
    ///   - Home settings › Background › Where the video plays (feat/home-video-pip)
    /// </summary>
    public partial class ThemeApi
    {
        public string BatterySourceTextNext
        {
            get { HookCycleNext(); return "›  " + (settings.BatterySource == 1 ? Loc("LOC_KX_BatterySourceController", "Controller") : Loc("LOC_KX_BatterySourceDevice", "This device")); }
        }

        public string HomeVideoModeTextNext
        {
            get { HookCycleNext(); return "›  " + (HomeVideoMode == 1 ? Loc("LOC_KX_HomeVideoModeFull", "Full screen") : Loc("LOC_KX_HomeVideoModePip", "Picture in picture")); }
        }
    }
}
