using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK.Events;

namespace KippixHelper
{
    /// <summary>
    /// (0.93) Slowness finder: writes a line to the extensions log (prefix "Kippix: slow") when a button press takes a long
    /// time to settle, when one piece of work on the screen's thread runs long (with what it was), or when the 2-second
    /// upkeep runs long (with its slow parts). Nothing is shown; it costs next to nothing; the log is capped per start.
    /// </summary>
    public partial class ThemeApi
    {
        private DateTime lastNavKeyAt = DateTime.MinValue;   // any key or button on the main window (the upkeep waits while you're moving)
        private Stopwatch pressWatch;
        private string pressKey;
        private int perfLines;
        private DateTime perfLastAt = DateTime.MinValue;
        private readonly Dictionary<DispatcherOperation, long> opStarted = new Dictionary<DispatcherOperation, long>();
        private bool opsHooked;
        private static FieldInfo opMethodField;

        private void PerfLog(string text)
        {
            if (perfLines >= 300) return;
            perfLines++;
            logger.Info("Kippix: slow: " + text);
        }

        private string PerfWhere
        {
            get
            {
                try
                {
                    string where = openPanel ?? (detailsOpen ? "game page" : "home");
                    if (openPanel == null && !detailsOpen) where += " (" + BgLayoutKeyNow + ")";
                    return where;
                }
                catch (Exception) { return "?"; }
            }
        }

        /// <summary>From the window's first look at a key: how long until everything it set off is done.</summary>
        private void PerfKeyDown(KeyEventArgs e)
        {
            try
            {
                lastNavKeyAt = DateTime.Now;
                if (JustifiedPanel.Perf) JustPerfPress(e);   // the mosaic's stopwatch (ThemeApi.JustifiedPerf.cs): a press, to its frame
                if (pressWatch != null || ui == null || e.IsRepeat) return;
                var b = PadButton(e);
                pressKey = b != ControllerInput.None ? b.ToString() : e.Key.ToString();
                pressWatch = Stopwatch.StartNew();
                ui.BeginInvoke(new Action(() =>
                {
                    long ms = pressWatch != null ? pressWatch.ElapsedMilliseconds : 0;
                    pressWatch = null;
                    if (ms >= 180) PerfLog("press " + pressKey + " took " + ms + " ms to settle, " + PerfWhere);
                }), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception) { pressWatch = null; }
        }

        /// <summary>Times each piece of work on the screen's thread; a long one is written down with its name.</summary>
        private void HookSlowOps()
        {
            if (opsHooked || ui == null) return;
            opsHooked = true;
            try
            {
                opMethodField = typeof(DispatcherOperation).GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance);
                ui.Hooks.OperationStarted += (s, e) => { try { opStarted[e.Operation] = Stopwatch.GetTimestamp(); } catch (Exception) { } };
                ui.Hooks.OperationAborted += (s, e) => { try { if (ui.CheckAccess()) opStarted.Remove(e.Operation); } catch (Exception) { } };
                ui.Hooks.OperationCompleted += (s, e) =>
                {
                    try
                    {
                        long t0;
                        if (!opStarted.TryGetValue(e.Operation, out t0)) return;
                        opStarted.Remove(e.Operation);
                        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        if (ms < 70 || perfLines >= 300) return;
                        if ((DateTime.Now - perfLastAt).TotalMilliseconds < 400) return;
                        perfLastAt = DateTime.Now;
                        PerfLog("work " + OpName(e.Operation) + " " + Math.Round(ms) + " ms (" + e.Operation.Priority + "), " + PerfWhere);
                    }
                    catch (Exception) { }
                };
            }
            catch (Exception ex) { logger.Warn("Kippix: slowness finder: " + ex.Message); }
        }

        private static string OpName(DispatcherOperation op)
        {
            try
            {
                var d = opMethodField != null ? opMethodField.GetValue(op) as Delegate : null;
                if (d == null) return "?";
                var m = d.Method;
                return (m.DeclaringType != null ? m.DeclaringType.FullName : "") + "." + m.Name;
            }
            catch (Exception) { return "?"; }
        }

        // ---------------- the upkeep's timing ----------------

        private Stopwatch tickWatch;
        private long tickMark;
        private List<string> tickSlow;

        private void TickTimeStart()
        {
            tickWatch = Stopwatch.StartNew();
            tickMark = 0;
            tickSlow = null;
        }

        private void TickTime(string part)
        {
            if (tickWatch == null) return;
            long t = tickWatch.ElapsedMilliseconds;
            if (t - tickMark >= 10) (tickSlow ?? (tickSlow = new List<string>())).Add(part + " " + (t - tickMark));
            tickMark = t;
        }

        private void TickTimeEnd()
        {
            if (tickWatch == null) return;
            long t = tickWatch.ElapsedMilliseconds;
            tickWatch = null;
            if (t >= 45 && tickSlow != null) PerfLog("upkeep " + t + " ms: " + string.Join(", ", tickSlow) + ", " + PerfWhere);
        }
    }
}
