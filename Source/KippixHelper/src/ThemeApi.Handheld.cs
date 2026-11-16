using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace KippixHelper
{
    /// <summary>
    /// Handheld mode (Settings › General › Handheld, touch and mouse): Automatic (on for a screen up to 10 inches across, from the
    /// monitor's own EDID size; without one, a screen up to 1920 × 1200 with a battery), On or Off. While it's on:
    ///   - the compact layout: bigger text, fewer rows, bigger cards, the battery in the top bar (what it changed is kept and put
    ///     back when handheld mode goes off, unless you changed that setting yourself meanwhile);
    ///   - battery saver: on battery, IsBatterySaving is true and the costly extras stand down (live backgrounds, animated art,
    ///     seasonal touches, ambient mode read it). Power changes come from Windows (no polling).
    /// Also the settings "overrides" safe mode uses (Kippix Doctor).
    /// </summary>
    public partial class ThemeApi
    {
        private bool handheldNow, handheldStarted, batterySaving, onBattery, hasBattery;
        private bool? smallScreen;          // the screen check's answer (null: not known yet)
        private double screenInches;        // 0: not known
        private string screenPixels = "";
        private int batteryPercent = -1;

        /// <summary>Handheld mode is on now (set, or Automatic on a small screen).</summary>
        public bool IsHandheld { get { return handheldNow; } }

        /// <summary>
        /// True while the costly extras should stand down: handheld mode on battery (with Battery saver on), or safe mode.
        /// Live backgrounds, animated art, seasonal touches and ambient mode read it (PropertyChanged "IsBatterySaving").
        /// </summary>
        public bool IsBatterySaving { get { return batterySaving; } }

        /// <summary>Another part can add this to hear when IsBatterySaving changes (no need: PropertyChanged says so too).</summary>
        partial void OnBatterySavingChanged();

        /// <summary>From the tick, once (fullscreen): Windows' power events, then the screen check off the UI thread.</summary>
        private void HandheldStart()
        {
            if (handheldStarted) return;
            handheldStarted = true;
            try { SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.StatusChange) OnUi(UpdatePowerState); }; } catch (Exception) { }
            UpdatePowerState();
            IntPtr hwnd = IntPtr.Zero;
            try { var w = Application.Current != null ? Application.Current.MainWindow : null; if (w != null) hwnd = new WindowInteropHelper(w).Handle; } catch (Exception) { }
            KippixShared.SafeWork.Queue(_ =>
            {
                double inches = 0;
                string px = "";
                bool? small = null;
                try { small = DetectSmallScreen(hwnd, out inches, out px); }
                catch (Exception ex) { logger.Warn("Kippix: handheld: screen check: " + ex.Message); }
                OnUi(() =>
                {
                    smallScreen = small;
                    screenInches = inches;
                    screenPixels = px;
                    logger.Info("Kippix: handheld: screen " + (inches > 0 ? inches.ToString("0.0", CultureInfo.InvariantCulture) + " in" : "size unknown") + ", " + px + (hasBattery ? ", battery" : "") + " -> " + (small == true ? "small" : "not small"));
                    ApplyHandheld();
                });
            });
        }

        private void UpdatePowerState()
        {
            try
            {
                var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                hasBattery = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
                onBattery = hasBattery && ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
                batteryPercent = hasBattery ? (int)Math.Round(ps.BatteryLifePercent * 100) : -1;
            }
            catch (Exception) { }
            UpdateBatterySaving();
            if (optionsPage == "handheld" && openPanel == "options") GRowsAgain(null);
        }

        /// <summary>Handheld on or off, the compact layout with it.</summary>
        private void ApplyHandheld()
        {
            bool want = settings.HandheldMode == 1 || (settings.HandheldMode == 0 && smallScreen == true);
            if (want != handheldNow)
            {
                handheldNow = want;
                logger.Info("Kippix: handheld mode " + (want ? "on" : "off"));
                Notify("IsHandheld");
            }
            bool compact = want && settings.HandheldCompact;
            if (settings.HandheldSaved == null) settings.HandheldSaved = new Dictionary<string, string>();
            bool changed = false;
            if (compact && settings.HandheldSaved.Count == 0) { OverrideFields(HandheldPreset(), settings.HandheldSaved); changed = true; }
            else if (!compact && settings.HandheldSaved.Count > 0) { RestoreFields(settings.HandheldSaved); changed = true; }
            if (changed)
            {
                SaveSettings();
                if (IsFullscreen) { ApplyAllSettings(); try { ApplyAccessibility(); } catch (Exception) { } }
            }
            UpdateBatterySaving();
            Notify("HandheldSummary");
        }

        /// <summary>The compact layout: bigger text, fewer rows, bigger cards, the battery in the top bar.</summary>
        private Dictionary<string, object> HandheldPreset()
        {
            var d = new Dictionary<string, object>();
            d["TextSize"] = Math.Max(1, settings.TextSize);
            d["GridRows"] = Math.Min(2, settings.GridRows);
            d["GridColumns"] = Math.Min(5, settings.GridColumns);
            d["ListRows"] = Math.Min(3, settings.ListRows);
            d["CardSize"] = settings.CardSize < 0 ? 5 : Math.Max(5, settings.CardSize);
            d["RowFullCount"] = 6;
            d["FolderTileCount"] = 6;
            d["PinBattery"] = true;
            d["ShowBatteryPercent"] = true;
            return d;
        }

        private void UpdateBatterySaving()
        {
            bool saving = settings.SafeMode || (handheldNow && settings.HandheldBatterySaver && onBattery);
            if (saving == batterySaving) return;
            batterySaving = saving;
            logger.Info("Kippix: battery saving " + (saving ? "on" : "off"));
            Notify("IsBatterySaving");
            try { OnBatterySavingChanged(); } catch (Exception ex) { logger.Warn("Kippix: battery saving: " + ex.Message); }
            try { if (ui != null && IsFullscreen) ApplyDepth(); } catch (Exception) { }   // the depth background stands down too (DepthMode itself kept)
        }

        // ---------------- the screen ----------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, int dwFlags);

        /// <summary>
        /// Is the screen Kippix is on a small one? Its size from the monitor's EDID (the registry copy Windows keeps), up to 10
        /// inches across; without a size: up to 1920 × 1200 pixels with a battery. Null: couldn't tell. Worker thread.
        /// </summary>
        private bool? DetectSmallScreen(IntPtr hwnd, out double inches, out string pixels)
        {
            inches = 0;
            pixels = "";
            var screen = hwnd != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(hwnd) : System.Windows.Forms.Screen.PrimaryScreen;
            if (screen == null) return null;
            int w = screen.Bounds.Width, h = screen.Bounds.Height;
            pixels = w + " × " + h;
            double mmW, mmH;
            if (EdidSize(screen.DeviceName, out mmW, out mmH))
            {
                inches = Math.Sqrt(mmW * mmW + mmH * mmH) / 25.4;
                if (inches > 3 && inches < 120) return inches <= 10.2;
                inches = 0;
            }
            bool modest = Math.Max(w, h) <= 1920 && Math.Min(w, h) <= 1200;
            return modest && hasBattery;
        }

        /// <summary>The physical size (mm) in the EDID of the monitor on that display (\\.\DISPLAY1).</summary>
        private static bool EdidSize(string displayName, out double mmW, out double mmH)
        {
            mmW = mmH = 0;
            var dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            // the monitor on that display, by its device interface: \\?\DISPLAY#SDC4161#5&2b8a...&UID265988#{e6f07b5f-...}
            if (!EnumDisplayDevices(displayName, 0, ref dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */) || string.IsNullOrEmpty(dd.DeviceID)) return false;
            string id = dd.DeviceID;
            int start = id.IndexOf("DISPLAY#", StringComparison.OrdinalIgnoreCase);
            int end = id.LastIndexOf("#{", StringComparison.Ordinal);
            if (start < 0 || end <= start) return false;
            string instance = id.Substring(start, end - start).Replace('#', '\\');   // DISPLAY\SDC4161\5&2b8a...&UID265988
            byte[] edid;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + instance + @"\Device Parameters"))
                edid = key != null ? key.GetValue("EDID") as byte[] : null;
            if (edid == null || edid.Length < 128) return false;
            // the first detailed timing: its image size in mm (12 bits each)
            int w = edid[66] | ((edid[68] & 0xF0) << 4);
            int h = edid[67] | ((edid[68] & 0x0F) << 8);
            if (w > 0 && h > 0 && (edid[54] != 0 || edid[55] != 0)) { mmW = w; mmH = h; return true; }
            // else the basic size in cm (both set: a size, not an aspect ratio)
            if (edid[21] > 0 && edid[22] > 0) { mmW = edid[21] * 10.0; mmH = edid[22] * 10.0; return true; }
            return false;
        }

        // ---------------- settings changed for a while (handheld's compact layout, safe mode) ----------------

        private static FieldInfo SettingField(string name)
        {
            var f = typeof(HelperSettings).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null && (f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum) ? f : null;
        }

        private static string FieldText(object v)
        {
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static object FieldValue(string text, Type t)
        {
            return Convert.ChangeType(text, t, CultureInfo.InvariantCulture);
        }

        /// <summary>Sets those settings, keeping what each was (and what it was set to) in <paramref name="saved"/>.</summary>
        private void OverrideFields(Dictionary<string, object> want, Dictionary<string, string> saved)
        {
            foreach (var kv in want)
            {
                var f = SettingField(kv.Key);
                if (f == null) continue;
                try
                {
                    object now = f.GetValue(settings);
                    object to = Convert.ChangeType(kv.Value, f.FieldType, CultureInfo.InvariantCulture);
                    if (Equals(now, to)) continue;   // already so: nothing to put back later
                    if (!saved.ContainsKey(kv.Key)) saved[kv.Key] = FieldText(now) + "\u0001" + FieldText(to);
                    f.SetValue(settings, to);
                }
                catch (Exception ex) { logger.Warn("Kippix: setting " + kv.Key + ": " + ex.Message); }
            }
            if (saved.Count == 0) saved["(none)"] = "";   // (applied, with nothing to change)
        }

        /// <summary>Puts those settings back, except the ones you changed yourself meanwhile.</summary>
        private void RestoreFields(Dictionary<string, string> saved)
        {
            foreach (var kv in saved)
            {
                var f = SettingField(kv.Key);
                if (f == null || kv.Value == null) continue;
                try
                {
                    string[] parts = kv.Value.Split('\u0001');
                    if (parts.Length == 2 && FieldText(f.GetValue(settings)) != parts[1]) continue;   // changed since: yours stays
                    f.SetValue(settings, FieldValue(parts[0], f.FieldType));
                }
                catch (Exception ex) { logger.Warn("Kippix: putting back " + kv.Key + ": " + ex.Message); }
            }
            saved.Clear();
        }

        // ---------------- the page: Settings › General › Handheld, touch and mouse ----------------

        public string HandheldSummary
        {
            get
            {
                string mode = settings.HandheldMode == 1 ? Loc("LOC_KX_On", "On") : settings.HandheldMode == 2 ? Loc("LOC_KX_Off", "Off") : Loc("LOC_KX_Automatic", "Automatic");
                return mode + "   ·   " + (handheldNow ? Loc("LOC_KX_HhNowOn", "handheld now") : Loc("LOC_KX_HhNowOff", "not handheld now"));
            }
        }

        private string HandheldIntro()
        {
            string s = screenInches > 0 ? string.Format(Loc("LOC_KX_HhScreen", "This screen: {0} inches, {1}"), screenInches.ToString("0.0", CultureInfo.CurrentCulture), screenPixels)
                     : !string.IsNullOrEmpty(screenPixels) ? string.Format(Loc("LOC_KX_HhScreenPx", "This screen: {0} (its size isn't reported)"), screenPixels) : "";
            if (hasBattery) s += (s.Length > 0 ? "   ·   " : "") + string.Format(onBattery ? Loc("LOC_KX_HhOnBattery", "on battery, {0}%") : Loc("LOC_KX_HhPluggedIn", "plugged in, {0}%"), Math.Max(0, batteryPercent));
            if (batterySaving) s += "   ·   " + Loc("LOC_KX_HhSaving", "saving battery now");
            return s;
        }

        private void BuildHandheldRows(List<PickRow> rows)
        {
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_HhHeader", "HANDHELD MODE") });
            string mode = settings.HandheldMode == 1 ? Loc("LOC_KX_On", "On") : settings.HandheldMode == 2 ? Loc("LOC_KX_Off", "Off") : Loc("LOC_KX_Automatic", "Automatic");
            rows.Add(new PickRow
            {
                Key = "hhmode", Icon = "controller", Text = string.Format(Loc("LOC_KX_HhMode", "Handheld mode: {0}"), mode),
                Sub = settings.HandheldMode == 0 ? Loc("LOC_KX_HhModeAutoSub", "On for screens up to 10 inches across (or a small screen with a battery)") : Loc("LOC_KX_HhModeSub", "Automatic, On or Off"),
                Command = new RelayCommand(_ => { settings.HandheldMode = (settings.HandheldMode + 1) % 3; SaveSettings(); PlaySound("move"); ApplyHandheld(); GRowsAgain("hhmode"); })
            });
            if (handheldNow || settings.HandheldMode == 1)
            {
                rows.Add(new PickRow
                {
                    Key = "hhcompact", Icon = "zoomin", IsSwitch = true, IsSelected = settings.HandheldCompact, Text = Loc("LOC_KX_HhCompact", "Compact layout"),
                    Sub = Loc("LOC_KX_HhCompactSub", "Bigger text, fewer rows, bigger cards, the battery in the top bar (put back when handheld mode goes off)"),
                    Command = new RelayCommand(_ => { settings.HandheldCompact = !settings.HandheldCompact; SaveSettings(); PlaySound("move"); ApplyHandheld(); GRowsAgain("hhcompact"); })
                });
                rows.Add(new PickRow
                {
                    Key = "hhsaver", Icon = "battery", IsSwitch = true, IsSelected = settings.HandheldBatterySaver, Text = Loc("LOC_KX_HhSaver", "Battery saver"),
                    Sub = Loc("LOC_KX_HhSaverSub", "On battery: no live backgrounds, animated art, seasonal touches or ambient mode"),
                    Command = new RelayCommand(_ => { settings.HandheldBatterySaver = !settings.HandheldBatterySaver; SaveSettings(); PlaySound("move"); UpdateBatterySaving(); GRowsAgain("hhsaver"); })
                });
            }
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_TouchHeader", "TOUCH AND MOUSE") });
            string touch = settings.TouchMode == 1 ? Loc("LOC_KX_On", "On") : settings.TouchMode == 2 ? Loc("LOC_KX_Off", "Off") : Loc("LOC_KX_Automatic", "Automatic");
            rows.Add(new PickRow
            {
                Key = "touch", Icon = "cursor", Text = string.Format(Loc("LOC_KX_TouchMode", "Touch: {0}"), touch),
                // (the language file's text comes without its last space: "A touch screen was found.Tap to choose")
                Sub = (HasTouchScreen ? Loc("LOC_KX_TouchFound", "A touch screen was found. ") : Loc("LOC_KX_TouchNone", "No touch screen found. ")).TrimEnd() + " "
                      + Loc("LOC_KX_TouchHow", "Tap to choose, swipe through rows and lists, swipe in from the left edge to go back, hold for options"),
                Command = new RelayCommand(_ => { settings.TouchMode = (settings.TouchMode + 1) % 3; SaveSettings(); PlaySound("move"); EnsureTouchInput(); GRowsAgain("touch"); })
            });
            rows.Add(new PickRow
            {
                Key = "rclick", Icon = "reply", IsSwitch = true, IsSelected = settings.MouseRightBack, Text = Loc("LOC_KX_MouseRightBack", "Right-click goes back"),
                Command = new RelayCommand(_ => { settings.MouseRightBack = !settings.MouseRightBack; SaveSettings(); PlaySound("move"); GRowsAgain("rclick"); })
            });
            rows.Add(new PickRow
            {
                Key = "wheel", Icon = "sliders", IsSwitch = true, IsSelected = settings.MouseWheelRow, Text = Loc("LOC_KX_MouseWheelRow", "The mouse wheel steps through the home row"),
                Command = new RelayCommand(_ => { settings.MouseWheelRow = !settings.MouseWheelRow; SaveSettings(); PlaySound("move"); GRowsAgain("wheel"); })
            });
            rows.Add(new PickRow
            {
                Key = "mousepark", Icon = "cursor", Text = string.Format(Loc("LOC_KX_MousePark", "Mouse pointer: {0}"), MouseParkText),
                Sub = Loc("LOC_KX_MouseParkSub", "Where it goes when Playnite starts or comes back from a game: off screen (hidden until you move the mouse), where it was, or the middle of the screen"),
                Command = new RelayCommand(_ => { CycleMousePark(); GRowsAgain("mousepark"); })
            });
            rows.Add(new PickRow { IsHeader = true, Text = Loc("LOC_KX_SecReset", "RESET") });
            rows.Add(new PickRow
            {
                Key = "hhreset", Icon = "refresh", Text = Loc("LOC_KX_ResetThisPage", "Reset this page"), Sub = Loc("LOC_KX_ResetThisPageSub", "Resets this page to default"),
                Command = new RelayCommand(_ =>
                {
                    var blank = new HelperSettings();
                    settings.HandheldMode = blank.HandheldMode;
                    settings.HandheldCompact = blank.HandheldCompact;
                    settings.HandheldBatterySaver = blank.HandheldBatterySaver;
                    settings.TouchMode = blank.TouchMode;
                    settings.MouseRightBack = blank.MouseRightBack;
                    settings.MouseWheelRow = blank.MouseWheelRow;
                    settings.MouseParkMode = blank.MouseParkMode;
                    SaveSettings();
                    PlaySound("select");
                    ApplyHandheld();
                    GRowsAgain("hhreset");
                })
            });
        }
    }
}
