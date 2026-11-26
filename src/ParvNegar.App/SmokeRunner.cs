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
                        report.AppendLine("startup_to_first_render_ms\t" + startupMs.ToString("F0", CultureInfo.InvariantCulture));
                        report.AppendLine("process\t" + (Environment.Is64BitProcess ? "x64" : "x86") + "\t" + RuntimeInformation.FrameworkDescription);
                        report.AppendLine("os\t" + Environment.OSVersion.VersionString);
                        report.AppendLine("dpi_scale\t" + VisualTreeHelper.GetDpi(main).DpiScaleX.ToString(CultureInfo.InvariantCulture));
                        report.AppendLine("flow_direction\t" + main.FlowDirection);
                        report.AppendLine("profile_loaded\t" + vm.HasProfile);
                        report.AppendLine("home_title_utf8_sha256\t" + Sha256Hex(vm.HomeTitle));
                        report.AppendLine("icon_set\t" + (main.Icon != null));

                        foreach (var size in new[] { new Size(1024, 768), new Size(1366, 768), new Size(800, 600) })
                        {
                            main.WindowState = WindowState.Normal;
                            main.Width = size.Width; main.Height = size.Height;
                            main.UpdateLayout();
                            Capture(main, Path.Combine(dir, "home-" + size.Width + "x" + size.Height + ".png"));
                        }
                        main.Width = 1024; main.Height = 768;
                        vm.StartCommand.Execute(null);
                        main.UpdateLayout();
                        Capture(main, Path.Combine(dir, "start-placeholder-1024x768.png"));
                        vm.BackCommand.Execute(null);
                        main.UpdateLayout();

                        var about = new AboutWindow(vm) { Owner = main };
                        about.Show();
                        about.UpdateLayout();
                        Capture(about, Path.Combine(dir, "about.png"));
                        about.Close();

                        report.AppendLine("peak_private_bytes\t" + PeakPrivateBytes().ToString(CultureInfo.InvariantCulture));
                        report.AppendLine("result\tPASS");
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

        /// <summary>
        /// Captures the real window (frame, title bar, icon and client) from the screen, so the image shows
        /// what the user sees. RenderTargetBitmap is not used: it renders RTL content mirrored.
        /// </summary>
        private static void Capture(Window w, string path)
        {
            w.Topmost = true;
            w.Activate();
            w.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            System.Threading.Thread.Sleep(400); // let DWM present the frame
            w.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            // Visible frame without the invisible resize borders (DWM, Vista+); fall back to the window rect.
            if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var r, Marshal.SizeOf(typeof(Rect32))) != 0)
                GetWindowRect(hwnd, out r);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
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
