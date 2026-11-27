using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ParvNegar.App.ViewModels;
using ParvNegar.App.Views;

namespace ParvNegar.App
{
    /// <summary>
    /// Compatibility smoke mode: "ParvNegar.exe --smoke &lt;dir&gt;". Shows the real window, waits for the first
    /// render, records startup time and peak private memory, saves screenshots (synthetic configuration only)
    /// and exits with 0 on success. Used by the compatibility kit on real Windows machines.
    /// </summary>
    internal static class SmokeRunner
    {
        public static string ParseSmokeArgument(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--smoke", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(args[i + 1]);
            return null;
        }

        private static readonly StringBuilder SplashReport = new StringBuilder();

        /// <summary>Records splash timing and a splash screenshot (captured while the splash is visible).</summary>
        public static void AttachSplash(Window splash, string dir, Stopwatch clock)
        {
            Directory.CreateDirectory(dir);
            double shownMs = 0;
            splash.Loaded += (s, e) => SplashReport.AppendLine("splash_loaded_ms\t" + clock.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture));
            splash.ContentRendered += (s, e) =>
            {
                shownMs = clock.Elapsed.TotalMilliseconds;
                SplashReport.AppendLine("splash_first_render_ms\t" + shownMs.ToString("F0", CultureInfo.InvariantCulture));
                splash.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try { Capture(splash, Path.Combine(dir, "splash.png")); SplashReport.AppendLine("splash_capture\tOK"); }
                    catch (Exception ex) { SplashReport.AppendLine("splash_capture\tFAIL\t" + ex.Message); }
                }));
            };
            splash.Closed += (s, e) =>
            {
                var total = clock.Elapsed.TotalMilliseconds;
                SplashReport.AppendLine("splash_closed_ms\t" + total.ToString("F0", CultureInfo.InvariantCulture));
                SplashReport.AppendLine("splash_visible_ms\t" + (total - shownMs).ToString("F0", CultureInfo.InvariantCulture));
                SplashReport.AppendLine("splash_minimum_met\t" + (total - shownMs >= SplashWindow.MinimumDuration.TotalMilliseconds));
            };
        }

        public static void Attach(System.Windows.Application app, MainWindow main, MainViewModel vm, string dir)
        {
            Directory.CreateDirectory(dir);
            var report = new StringBuilder();
            main.ContentRendered += (s, e) =>
            {
                var startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    int code = 0;
                    try
                    {
                        report.Append(SplashReport);
                        report.AppendLine("startup_to_first_render_ms\t" + startupMs.ToString("F0", CultureInfo.InvariantCulture) + "\t(includes the 2 s splash)");
                        report.AppendLine("process\t" + (Environment.Is64BitProcess ? "x64" : "x86") + "\t" + RuntimeInformation.FrameworkDescription);
                        report.AppendLine("os\t" + Environment.OSVersion.VersionString);
                        var dpi = VisualTreeHelper.GetDpi(main);
                        int currentScale = (int)Math.Round(dpi.DpiScaleX * 100);
                        var wa = SystemParameters.WorkArea;
                        report.AppendLine("dpi_scale\t" + dpi.DpiScaleX.ToString(CultureInfo.InvariantCulture));
                        report.AppendLine("primary_screen_physical_px\t" + GetSystemMetrics(0) + "x" + GetSystemMetrics(1));
                        report.AppendLine("work_area_dip\t" + D(wa.Width) + "x" + D(wa.Height));
                        report.AppendLine("main_window_dip\t" + D(main.ActualWidth) + "x" + D(main.ActualHeight) + "\tfits_work_area=" + (main.ActualWidth <= wa.Width + 0.5 && main.ActualHeight <= wa.Height + 0.5));
                        report.AppendLine("flow_direction\t" + main.FlowDirection);
                        report.AppendLine("profile_loaded\t" + vm.HasProfile);
                        report.AppendLine("home_title_utf8_sha256\t" + Sha256Hex(vm.HomeTitle));
                        report.AppendLine("icon_set\t" + (main.Icon != null));

                        // Layout matrix: simulated work areas (DIP) for each resolution and scale factor.
                        // Screenshots only for profiles at this display's own scale that fit this display.
                        int layoutFailures = 0;
                        main.WindowState = WindowState.Normal;
                        foreach (var profile in DisplayProfile.Matrix())
                        {
                            var sim = profile.WorkAreaDip;
                            main.MaxWidth = double.PositiveInfinity; main.MaxHeight = double.PositiveInfinity;
                            main.MinWidth = 800; main.MinHeight = 600;
                            WindowFit.ClampTo(main, sim);
                            main.Width = sim.Width; main.Height = sim.Height;
                            main.Left = wa.Left; main.Top = wa.Top;
                            foreach (var home in new[] { true, false })
                            {
                                if (home) vm.BackCommand.Execute(null); else vm.StartCommand.Execute(null);
                                main.UpdateLayout();
                                var problems = LayoutProbe.CheckMain(main);
                                var view = home ? "home" : "start";
                                report.AppendLine("layout\t" + profile.Name + "\t" + view + "\twindow_dip=" + D(sim.Width) + "x" + D(sim.Height) + "\t" + (problems.Count == 0 ? "OK" : "FAIL\t" + string.Join("; ", problems)));
                                layoutFailures += problems.Count == 0 ? 0 : 1;
                                bool capturable = profile.ScalePercent == currentScale && sim.Width <= wa.Width && sim.Height <= wa.Height;
                                if (capturable && (home || profile.PhysicalWidth == 1024))
                                    Capture(main, Path.Combine(dir, view + "-" + profile.Name + ".png"));
                            }
                        }
                        vm.BackCommand.Execute(null);
                        main.MaxWidth = double.PositiveInfinity; main.MaxHeight = double.PositiveInfinity;
                        main.MinWidth = 800; main.MinHeight = 600; main.Width = 1100; main.Height = 720;
                        WindowFit.ClampToWorkArea(main);
                        main.UpdateLayout();

                        var about = new AboutWindow(vm) { Owner = main };
                        about.Show();
                        about.UpdateLayout();
                        report.AppendLine("about_window_dip\t" + D(about.ActualWidth) + "x" + D(about.ActualHeight));
                        Capture(about, Path.Combine(dir, "about.png"));
                        about.Close();

                        report.AppendLine("layout_failures\t" + layoutFailures);
                        report.AppendLine("peak_private_bytes\t" + PeakPrivateBytes().ToString(CultureInfo.InvariantCulture));
                        if (layoutFailures > 0) { code = 2; report.AppendLine("result\tFAIL\tlayout"); }
                        else report.AppendLine("result\tPASS");
                    }
                    catch (Exception ex)
                    {
                        code = 1;
                        report.AppendLine("result\tFAIL\t" + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' '));
                    }
                    File.WriteAllText(Path.Combine(dir, "shell-smoke.tsv"), report.ToString(), new UTF8Encoding(false));
                    app.Shutdown(code);
                }));
            };
        }

        private static string D(double v) => v.ToString("F0", CultureInfo.InvariantCulture);

        /// <summary>
        /// Captures the real window (frame, title bar, icon and client) from the screen, so the image shows
        /// what the user sees. RenderTargetBitmap is not used: it renders RTL content mirrored.
        /// </summary>
        private static void Capture(Window w, string path)
        {
            w.Topmost = true;
            w.Activate();
            PumpFor(TimeSpan.FromMilliseconds(400)); // keep the dispatcher rendering while DWM presents the frame
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            // Visible frame without the invisible resize borders (DWM, Vista+); fall back to the window rect.
            if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var r, Marshal.SizeOf(typeof(Rect32))) != 0)
                GetWindowRect(hwnd, out r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            // Privacy guard: the capture copies screen pixels, so another application's window on top would be
            // recorded. Every point of a 9x9 grid inside the rectangle must belong to this window, else refuse.
            for (int gy = 0; gy < 9; gy++)
                for (int gx = 0; gx < 9; gx++)
                {
                    var pt = new Point32 { X = r.Left + 2 + (width - 5) * gx / 8, Y = r.Top + 2 + (height - 5) * gy / 8 };
                    var top = GetAncestor(WindowFromPoint(pt), 2 /* GA_ROOT */);
                    if (top != hwnd) throw new InvalidOperationException("another window covers the capture area; capture refused: " + Path.GetFileName(path));
                }
            var mi = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* NEAREST */), ref mi)
                && (r.Left < mi.rcMonitor.Left || r.Top < mi.rcMonitor.Top || r.Right > mi.rcMonitor.Right || r.Bottom > mi.rcMonitor.Bottom))
                throw new InvalidOperationException("window extends beyond its monitor; the capture would be clipped: " + Path.GetFileName(path));
            var screen = GetDC(IntPtr.Zero);
            var mem = CreateCompatibleDC(screen);
            var bmp = CreateCompatibleBitmap(screen, width, height);
            var old = SelectObject(mem, bmp);
            try
            {
                // Copy the screen pixels under the window: exactly what the user sees (PrintWindow mirrors RTL windows).
                BitBlt(mem, 0, 0, width, height, screen, r.Left, r.Top, 0x00CC0020 /* SRCCOPY */);
                SelectObject(mem, old);
                var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using (var fs = File.Create(path)) enc.Save(fs);
            }
            finally
            {
                DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
                w.Topmost = false;
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int cbSize; public Rect32 rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point32 point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        private static void PumpFor(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
            t.Tick += (s, e) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Rect32 value, int size);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

        private static string Sha256Hex(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? string.Empty));
                var sb = new StringBuilder();
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCounters counters, uint size);

        /// <summary>Peak commit charge (private bytes) of this process.</summary>
        public static ulong PeakPrivateBytes()
        {
            var c = new ProcessMemoryCounters { cb = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters)) };
            return GetProcessMemoryInfo(Process.GetCurrentProcess().Handle, out c, c.cb) ? c.PeakPagefileUsage.ToUInt64() : 0;
        }
    }
}
