using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParvNegar.App;
using ParvNegar.App.ViewModels;
using ParvNegar.App.Views;
using ParvNegar.Application;
using ParvNegar.Domain;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.App.Tests
{
    /// <summary>WPF shell smoke tests on a dedicated STA thread. Synthetic configuration only.</summary>
    public class ShellTests
    {
        private const string ApprovedTitle = "ثبت شاخص های آموزشی و پرورشی دانش آموزان جامعه هدف مدرسه شاهد شهیده شهناز کریمی";

        public ShellTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private sealed class FixedSource : ISchoolProfileSource
        {
            private readonly SchoolProfile _p;
            public FixedSource(SchoolProfile p) { _p = p; }
            public SchoolProfile Load() => _p;
        }

        private static void RunSta(Action body)
        {
            Exception error = null;
            var t = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        var app = new ParvNegar.App.App();
                        app.InitializeComponent();
                    }
                    body();
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Exception("STA body failed: " + error.GetType().Name + ": " + error.Message, error);
        }

        private static MainWindow Build(SchoolProfile p, out MainViewModel vm)
        {
            vm = new MainViewModel(new FixedSource(p), null, null);
            var w = new MainWindow(vm) { Width = 1024, Height = 768, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false };
            w.Show();
            w.UpdateLayout();
            // Let deferred binding and layout work run on this STA dispatcher before asserting.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
            return w;
        }

        [Fact]
        public void Home_shows_exact_configured_title_rtl_with_three_actions()
        {
            RunSta(() =>
            {
                var w = Build(new SchoolProfile(Guid.NewGuid(), "مدرسه نمونه", ApprovedTitle, null), out var vm);
                try
                {
                    Assert.Equal(FlowDirection.RightToLeft, w.FlowDirection);
                    var title = (TextBlock)w.FindName("HomeTitleText");
                    Assert.Equal(ApprovedTitle, title.Text);
                    var buttons = Descendants<Button>(w).Where(b => b.IsVisible).Select(b => b.Content as string).ToList();
                    Assert.Contains("شروع", buttons);
                    Assert.Contains("درباره", buttons);
                    Assert.Contains("خروج", buttons);
                    Assert.NotNull(w.Icon);
                    Assert.Contains("Vazirmatn", title.FontFamily.Source);
                    Assert.True(title.ActualHeight > 0 && title.ActualWidth <= 1024);
                }
                finally { w.Close(); }
            });
        }

        [Fact]
        public void Title_comes_from_profile_not_from_xaml()
        {
            RunSta(() =>
            {
                var w = Build(new SchoolProfile(Guid.NewGuid(), "مدرسه آزمایشی", "عنوان آزمایشی دیگر", null), out _);
                try { Assert.Equal("عنوان آزمایشی دیگر", ((TextBlock)w.FindName("HomeTitleText")).Text); }
                finally { w.Close(); }
            });
        }

        [Fact]
        public void Missing_profile_shows_neutral_message_and_does_not_crash()
        {
            RunSta(() =>
            {
                var vm = new MainViewModel(new FixedSource(null), null, null);
                Assert.False(vm.HasProfile);
                Assert.Equal(Strings.ProfileMissing, vm.HomeTitle);
            });
        }

        [Fact]
        public void Start_and_back_navigate_and_escape_returns_home()
        {
            RunSta(() =>
            {
                var w = Build(new SchoolProfile(Guid.NewGuid(), "x", ApprovedTitle, null), out var vm);
                try
                {
                    vm.StartCommand.Execute(null);
                    Assert.True(vm.IsPlaceholder);
                    vm.BackCommand.Execute(null);
                    Assert.True(vm.IsHome);
                    Assert.Contains(w.InputBindings.OfType<System.Windows.Input.KeyBinding>(), k => k.Key == System.Windows.Input.Key.Escape);
                }
                finally { w.Close(); }
            });
        }

        [Fact]
        public void Layout_fits_the_1024x768_low_resource_profile()
        {
            RunSta(() =>
            {
                var w = Build(new SchoolProfile(Guid.NewGuid(), "x", ApprovedTitle, null), out _);
                try
                {
                    var root = (FrameworkElement)w.Content;
                    foreach (var b in Descendants<Button>(w).Where(b => b.IsVisible))
                    {
                        var p = b.TransformToAncestor(root).Transform(new Point(0, 0));
                        Assert.InRange(p.Y + b.ActualHeight, 0, root.ActualHeight);
                    }
                }
                finally { w.Close(); }
            });
        }

        [Fact]
        public void Xml_profile_source_preserves_title_and_rejects_dtd()
        {
            using (var d = new TempDir("profile"))
            {
                var good = d.File("good.xml");
                File.WriteAllText(good, "<?xml version=\"1.0\" encoding=\"utf-8\"?><SchoolProfile Id=\"" + Guid.NewGuid() + "\"><SchoolName>مدرسه نمونه</SchoolName><HomeTitle>" + ApprovedTitle + "</HomeTitle></SchoolProfile>", new System.Text.UTF8Encoding(false));
                Assert.Equal(ApprovedTitle, new XmlSchoolProfileSource(good).Load().HomeTitle);
                var evil = d.File("evil.xml");
                File.WriteAllText(evil, "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><SchoolProfile Id=\"" + Guid.NewGuid() + "\"><SchoolName>&e;</SchoolName><HomeTitle>t</HomeTitle></SchoolProfile>");
                Assert.Null(new XmlSchoolProfileSource(evil).Load());
                Assert.Null(new XmlSchoolProfileSource(d.File("missing.xml")).Load());
            }
        }

        [Fact]
        public void Shipped_profile_file_contains_the_exact_title()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "school-profile.xml");
            Assert.True(File.Exists(path), "Config/school-profile.xml must be copied next to the app");
            Assert.Equal(ApprovedTitle, new XmlSchoolProfileSource(path).Load().HomeTitle);
        }

        [Fact]
        public void Report_and_preview_ports_default_to_unavailable_without_native_dependencies()
        {
            Assert.False(new UnavailableReportRenderer().IsAvailable);
            Assert.False(new UnavailableDocumentPreviewer().CanPreview("application/pdf"));
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToList();
            Assert.DoesNotContain(loaded, n => n.IndexOf("Skia", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("QuestPDF", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("PDFium", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Every_display_profile_keeps_all_controls_reachable_without_overlap()
        {
            // 1024x768, 1366x768, 1920x1080 at 100/125/150/200% as DIP work areas (WPF lays out in DIP).
            RunSta(() =>
            {
                var w = Build(new SchoolProfile(Guid.NewGuid(), "مدرسه آزمایشی با نام نسبتاً طولانی برای بررسی شکستن خط", ApprovedTitle, null), out var vm);
                try
                {
                    var failures = new System.Collections.Generic.List<string>();
                    foreach (var profile in DisplayProfile.Matrix())
                    {
                        var sim = profile.WorkAreaDip;
                        w.MaxWidth = double.PositiveInfinity; w.MaxHeight = double.PositiveInfinity; w.MinWidth = 800; w.MinHeight = 600;
                        WindowFit.ClampTo(w, sim);
                        w.Width = sim.Width; w.Height = sim.Height;
                        Assert.True(w.MinWidth <= sim.Width && w.MinHeight <= sim.Height, "minimum size exceeds the work area: " + profile.Name);
                        foreach (var home in new[] { true, false })
                        {
                            if (home) vm.BackCommand.Execute(null); else vm.StartCommand.Execute(null);
                            w.UpdateLayout();
                            foreach (var p in LayoutProbe.CheckMain(w)) failures.Add(profile.Name + (home ? " home: " : " start: ") + p);
                        }
                    }
                    Assert.Empty(failures);
                }
                finally { w.Close(); }
            });
        }

        [Fact]
        public void Layout_probe_detects_cut_off_controls()
        {
            RunSta(() =>
            {
                var w = Build(new SchoolProfile(Guid.NewGuid(), "x", ApprovedTitle, null), out _);
                try
                {
                    w.MinWidth = 0; w.Width = 600;
                    w.StartButton.MinWidth = 2000;
                    w.UpdateLayout();
                    Assert.Contains(LayoutProbe.CheckMain(w), p => p.StartsWith("Start cut horizontally", StringComparison.Ordinal));
                }
                finally { w.Close(); }
            });
        }

        [Fact]
        public void Window_fit_never_exceeds_the_work_area()
        {
            RunSta(() =>
            {
                var w = new Window { Width = 1100, Height = 720, MinWidth = 800, MinHeight = 600 };
                WindowFit.ClampTo(w, new Rect(0, 0, 819, 566));
                Assert.Equal(819, w.Width); Assert.Equal(566, w.Height);
                Assert.Equal(819, w.MaxWidth); Assert.Equal(566, w.MaxHeight);
                Assert.Equal(800, w.MinWidth); Assert.Equal(566, w.MinHeight);
                var small = new Window { Width = 400, Height = 300 };
                WindowFit.ClampTo(small, new Rect(0, 0, 1536, 816));
                Assert.Equal(400, small.Width); Assert.Equal(300, small.Height);
            });
        }

        [Fact]
        public void Splash_shows_the_unmodified_publisher_mark_for_two_seconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(2), SplashWindow.MinimumDuration);
            RunSta(() =>
            {
                var s = new SplashWindow();
                Assert.Equal(WindowStyle.None, s.WindowStyle);
                Assert.Equal(FlowDirection.RightToLeft, s.FlowDirection);
                var img = (System.Windows.Controls.Image)s.FindName("PublisherMark");
                Assert.NotNull(img.Source);
                Assert.Equal("OAR NOVA Games Studio", System.Windows.Automation.AutomationProperties.GetName(img));
                s.Close();

                // The embedded resource is byte-identical to assets/publisher (no recolor or redraw in the build).
                var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/ParvNegar;component/Assets/Publisher/OAR-NOVA-Games-Logo-White.png"));
                Assert.NotNull(info);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var st = info.Stream)
                    Assert.Equal("75b843bf8d0d2a4f14b142e34aa5b12ba50e736f041ad06f659e4834eddcc8bb",
                                 string.Concat(sha.ComputeHash(st).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))));
            });
        }

        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var x in Descendants<T>(c)) yield return x;
            }
        }
    }
}
