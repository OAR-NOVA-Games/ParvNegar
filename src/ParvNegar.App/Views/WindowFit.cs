using System;
using System.Windows;

namespace ParvNegar.App.Views
{
    /// <summary>
    /// Keeps windows inside the available Windows work area (screen minus taskbar), in WPF
    /// device-independent units. A 1024×768 display at 125% DPI offers only about 819×584 DIP,
    /// so fixed sizes or minimums larger than that would push the footer or buttons off-screen.
    /// Content that does not fit scrolls instead (ScrollViewer in each view).
    /// </summary>
    public static class WindowFit
    {
        /// <summary>Clamps size, minimum size and maximum size to <paramref name="workArea"/>.</summary>
        public static void ClampTo(Window w, Rect workArea)
        {
            if (workArea.Width <= 0 || workArea.Height <= 0) return;
            w.MaxWidth = Math.Min(w.MaxWidth, workArea.Width);
            w.MaxHeight = Math.Min(w.MaxHeight, workArea.Height);
            w.MinWidth = Math.Min(w.MinWidth, workArea.Width);
            w.MinHeight = Math.Min(w.MinHeight, workArea.Height);
            if (!double.IsNaN(w.Width) && w.Width > workArea.Width) w.Width = workArea.Width;
            if (!double.IsNaN(w.Height) && w.Height > workArea.Height) w.Height = workArea.Height;
        }

        /// <summary>Primary-monitor work area (the monitor CenterScreen uses).</summary>
        public static void ClampToWorkArea(Window w) => ClampTo(w, SystemParameters.WorkArea);
    }
}
