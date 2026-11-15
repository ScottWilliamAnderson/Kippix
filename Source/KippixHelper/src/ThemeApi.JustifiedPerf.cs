using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;

namespace KippixHelper
{
    /// <summary>
    /// The mosaic's stopwatch, for finding what makes moving in it slow. Off unless JustifiedPanel.Perf is set (the
    /// KIPPIX_MOSAIC_PERF environment variable at start, or by hand): then every move's parts and every frame drawn while
    /// the mosaic is up are timed, and a summary goes to the extensions log ("Kippix: mosaic: perf") when the library view
    /// is closed or JustifiedPanel.PerfDump is called. While it's on WPF draws every frame, so it's never left on.
    /// </summary>
    public partial class ThemeApi
    {
        private sealed class PerfSeries
        {
            private readonly List<double> v = new List<double>();
            public void Add(double ms) { if (v.Count < 20000) v.Add(ms); }
            public int Count { get { return v.Count; } }
            public void Clear() { v.Clear(); }
            public int Over(double ms) { int n = 0; foreach (double x in v) if (x > ms) n++; return n; }
            public override string ToString()
            {
                if (v.Count == 0) return "none";
                var s = new List<double>(v);
                s.Sort();
                double sum = 0;
                foreach (double x in s) sum += x;
                return string.Format(CultureInfo.InvariantCulture, "avg {0:0.0} / median {1:0.0} / p95 {2:0.0} / worst {3:0.0} ms", sum / s.Count, s[s.Count / 2], s[Math.Min(s.Count - 1, (int)(s.Count * 0.95))], s[s.Count - 1]);
            }
        }

        private readonly PerfSeries pfSelect = new PerfSeries(), pfScroll = new PerfSeries(), pfLayout = new PerfSeries(), pfFocus = new PerfSeries(),
                                    pfMove = new PerfSeries(), pfSettle = new PerfSeries(), pfFrames = new PerfSeries(), pfSelected = new PerfSeries();
        private bool pfHooked;
        private TimeSpan pfLastRender;
        private long pfLastStamp;
        private Stopwatch pfSettleWatch;

        private static double PerfMs(long from)
        {
            return (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
        }

        private void InitJustPerf()
        {
            try { if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KIPPIX_MOSAIC_PERF"))) JustifiedPanel.Perf = true; } catch (Exception) { }
            JustifiedPanel.PerfDump = () => JustPerfSummary("asked for");
        }

        /// <summary>Frames are timed while the mosaic is up (and the stopwatch is on).</summary>
        private void JustPerfFrames(bool on)
        {
            on = on && JustifiedPanel.Perf;
            if (on == pfHooked) return;
            pfHooked = on;
            pfLastStamp = 0;
            if (on) CompositionTarget.Rendering += JustPerfFrame;
            else CompositionTarget.Rendering -= JustPerfFrame;
        }

        private void JustPerfFrame(object sender, EventArgs e)
        {
            var r = e as RenderingEventArgs;
            if (r != null) { if (r.RenderingTime == pfLastRender) return; pfLastRender = r.RenderingTime; }
            long now = Stopwatch.GetTimestamp();
            if (pfLastStamp != 0) pfFrames.Add((now - pfLastStamp) * 1000.0 / Stopwatch.Frequency);
            pfLastStamp = now;
        }

        /// <summary>After a move: how long until everything it set off is done (the screen's thread idle again).</summary>
        private void JustPerfSettle()
        {
            if (!JustifiedPanel.Perf || ui == null || pfSettleWatch != null) return;
            pfSettleWatch = Stopwatch.StartNew();
            ui.BeginInvoke(new Action(() =>
            {
                if (pfSettleWatch != null) pfSettle.Add(pfSettleWatch.Elapsed.TotalMilliseconds);
                pfSettleWatch = null;
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void JustPerfSummary(string why)
        {
            try
            {
                if (pfMove.Count == 0 && pfFrames.Count == 0) return;
                logger.Info("Kippix: mosaic: perf (" + why + "): " + pfMove.Count + " moves: " + pfMove
                    + "; of that selecting " + pfSelect + " (the helper's own share " + pfSelected + "); scrolling " + pfScroll + "; layout " + pfLayout + "; highlight " + pfFocus
                    + "; settled after " + pfSettle);
                logger.Info("Kippix: mosaic: perf (" + why + "): " + pfFrames.Count + " frames: " + pfFrames + "; over 25 ms: " + pfFrames.Over(25) + ", over 50 ms: " + pfFrames.Over(50) + ", over 100 ms: " + pfFrames.Over(100)
                    + "; panel: " + JustifiedPanel.PerfMeasures + " measures, " + JustifiedPanel.PerfMeasureMs.ToString("0", CultureInfo.InvariantCulture) + " ms in all, worst " + JustifiedPanel.PerfMeasureWorst.ToString("0.0", CultureInfo.InvariantCulture)
                    + " ms, " + JustifiedPanel.PerfMade + " tiles made, " + JustifiedPanel.PerfModels + " row models; " + JustifiedPanel.PerfArranges + " arranges, " + JustifiedPanel.PerfArrangeMs.ToString("0", CultureInfo.InvariantCulture) + " ms");
                foreach (var s in new[] { pfSelect, pfScroll, pfLayout, pfFocus, pfMove, pfSettle, pfFrames, pfSelected }) s.Clear();
                JustifiedPanel.PerfMeasures = JustifiedPanel.PerfMade = JustifiedPanel.PerfModels = JustifiedPanel.PerfArranges = 0;
                JustifiedPanel.PerfMeasureMs = JustifiedPanel.PerfMeasureWorst = JustifiedPanel.PerfArrangeMs = 0;
                pfLastStamp = 0;
            }
            catch (Exception) { }
        }
    }
}
