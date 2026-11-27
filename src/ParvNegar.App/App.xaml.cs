using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ParvNegar.App.ViewModels;
using ParvNegar.App.Views;

namespace ParvNegar.App
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var smokeDir = SmokeRunner.ParseSmokeArgument(e.Args);

            // Owner request (2026-11-27): the studio mark is visible for 2 seconds at startup.
            // The splash renders first; the main window is built only after that first render (building it
            // earlier blocks the splash paint), and it is shown once the splash has been visible for
            // MinimumDuration, measured with a monotonic Stopwatch, not the wall clock.
            var clock = Stopwatch.StartNew();
            var splash = new SplashWindow();
            if (smokeDir != null) SmokeRunner.AttachSplash(splash, smokeDir, clock);
            bool started = false;
            splash.ContentRendered += (s, a) =>
            {
                if (started) return;
                started = true;
                var visibleSince = clock.Elapsed;
                splash.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    ShowMainAfterSplash(splash, smokeDir, clock, visibleSince)));
            };
            splash.Show();
        }

        private void ShowMainAfterSplash(SplashWindow splash, string smokeDir, Stopwatch clock, TimeSpan visibleSince)
        {
            MainWindow main = null;
            var vm = new MainViewModel(XmlSchoolProfileSource.Default(),
                showAbout: () => new AboutWindow((MainViewModel)main.DataContext) { Owner = main }.ShowDialog(),
                exit: () => main.Close());
            main = new MainWindow(vm);
            MainWindow = main; // ShutdownMode=OnMainWindowClose: from here on, closing the splash never ends the app.
            if (smokeDir != null) SmokeRunner.Attach(this, main, vm, smokeDir);

            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (s, a) =>
            {
                if (clock.Elapsed - visibleSince < SplashWindow.MinimumDuration) { timer.Interval = TimeSpan.FromMilliseconds(15); return; }
                timer.Stop();
                main.Show();
                main.Activate();
                splash.Close();
            };
            var remaining = SplashWindow.MinimumDuration - (clock.Elapsed - visibleSince);
            if (remaining > TimeSpan.FromMilliseconds(15)) timer.Interval = remaining;
            timer.Start();
        }
    }
}
