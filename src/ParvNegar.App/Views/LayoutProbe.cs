using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ParvNegar.App.Views
{
    /// <summary>
    /// A display profile: physical resolution and Windows scale factor, turned into the work area a
    /// maximized window would get, in WPF device-independent units (DIP). WPF lays out in DIP, so
    /// checking a window at the simulated DIP size is a valid layout check for that profile even when
    /// the test display has a different resolution or DPI (only pixel snapping differs).
    /// </summary>
    public sealed class DisplayProfile
    {
        /// <summary>Taskbar height assumed per profile, in DIP (Windows 11 uses 48; Windows 7-10 use 40).</summary>
        public const double TaskbarDip = 48;

        public DisplayProfile(int physicalWidth, int physicalHeight, int scalePercent)
        {
            PhysicalWidth = physicalWidth; PhysicalHeight = physicalHeight; ScalePercent = scalePercent;
        }

        public int PhysicalWidth { get; }
        public int PhysicalHeight { get; }
        public int ScalePercent { get; }
        public double Scale => ScalePercent / 100.0;
        public Rect WorkAreaDip => new Rect(0, 0, Math.Floor(PhysicalWidth / Scale), Math.Floor(PhysicalHeight / Scale - TaskbarDip));
        public string Name => PhysicalWidth.ToString(CultureInfo.InvariantCulture) + "x" + PhysicalHeight.ToString(CultureInfo.InvariantCulture) + "-at" + ScalePercent.ToString(CultureInfo.InvariantCulture);

        /// <summary>Phase 2 prompt §23 matrix: 1024×768, 1366×768, 1920×1080 at 100/125/150/200%.</summary>
        public static IEnumerable<DisplayProfile> Matrix()
        {
            foreach (var r in new[] { new[] { 1024, 768 }, new[] { 1366, 768 }, new[] { 1920, 1080 } })
                foreach (var s in new[] { 100, 125, 150, 200 })
                    yield return new DisplayProfile(r[0], r[1], s);
        }
    }

    /// <summary>
    /// Usability check, not only "it rendered": every key element must lie fully inside the window
    /// horizontally, be reachable vertically (directly or by scrolling its ScrollViewer), and the
    /// header and footer parts must not overlap each other.
    /// </summary>
    internal static class LayoutProbe
    {
        public static IList<string> CheckMain(MainWindow w)
        {
            var problems = new List<string>();
            var root = (FrameworkElement)w.Content;
            w.UpdateLayout();
            var fixedParts = new Dictionary<string, FrameworkElement>
            {
                ["ProductLogo"] = w.ProductLogo, ["DevNotice"] = w.DevNoticeText,
                ["FooterSchool"] = w.FooterSchool, ["FooterPublisher"] = w.FooterPublisher,
            };
            foreach (var kv in fixedParts) Inside(problems, kv.Key, kv.Value, root, true);
            NoOverlap(problems, "ProductLogo/DevNotice", w.ProductLogo, w.DevNoticeText, root);
            NoOverlap(problems, "FooterSchool/FooterPublisher", w.FooterSchool, w.FooterPublisher, root);

            var scrolled = new Dictionary<string, FrameworkElement>
            {
                ["HomeTitle"] = w.HomeTitleText, ["Start"] = w.StartButton, ["About"] = w.AboutButton,
                ["Exit"] = w.ExitButton, ["Back"] = w.BackButton,
            };
            foreach (var kv in scrolled)
            {
                if (!kv.Value.IsVisible) continue;
                kv.Value.BringIntoView();
                w.UpdateLayout();
                var viewer = Ancestor<ScrollViewer>(kv.Value);
                Inside(problems, kv.Key, kv.Value, (FrameworkElement)viewer ?? root, true);
            }
            return problems;
        }

        private static void Inside(List<string> problems, string name, FrameworkElement e, FrameworkElement container, bool vertical)
        {
            if (!e.IsVisible) return;
            var b = Bounds(e, container);
            const double tol = 0.5;
            if (b.Left < -tol || b.Right > container.ActualWidth + tol)
                problems.Add(name + " cut horizontally (" + Fmt(b) + " in width " + container.ActualWidth.ToString("F0", CultureInfo.InvariantCulture) + ")");
            if (vertical && (b.Top < -tol || b.Bottom > container.ActualHeight + tol))
                problems.Add(name + " not reachable vertically (" + Fmt(b) + " in height " + container.ActualHeight.ToString("F0", CultureInfo.InvariantCulture) + ")");
            if (e.ActualWidth < 1 || e.ActualHeight < 1) problems.Add(name + " has no visible size");
        }

        private static void NoOverlap(List<string> problems, string name, FrameworkElement a, FrameworkElement b, FrameworkElement root)
        {
            if (!a.IsVisible || !b.IsVisible) return;
            var r = Rect.Intersect(Bounds(a, root), Bounds(b, root));
            if (!r.IsEmpty && r.Width > 0.5 && r.Height > 0.5) problems.Add(name + " overlap");
        }

        private static Rect Bounds(FrameworkElement e, FrameworkElement container)
            => e.TransformToAncestor(container).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

        private static T Ancestor<T>(DependencyObject d) where T : DependencyObject
        {
            for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is T t) return t;
            return null;
        }

        private static string Fmt(Rect r) => string.Format(CultureInfo.InvariantCulture, "{0:F0},{1:F0} {2:F0}x{3:F0}", r.Left, r.Top, r.Width, r.Height);
    }
}
