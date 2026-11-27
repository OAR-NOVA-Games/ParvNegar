using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ParvNegar.App.Services;
using ParvNegar.App.ViewModels;
using ParvNegar.App.Views;
using ParvNegar.Application.Security;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.App
{
    public partial class App : System.Windows.Application
    {
        private InstanceGuard _instance;
        private SessionMonitor _monitor;
        private SessionManager _security;
        private string[] _args = new string[0];

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var smokeDir = SmokeRunner.ParseSmokeArgument(e.Args);
            _args = e.Args;

            // One running instance per PC (two processes must never initialise or modify the same data).
            // Smoke runs use their own synthetic data root and their own instance name.
            var paths = smokeDir != null ? SmokeRunner.SyntheticRoot(smokeDir) : InstallationPaths.Default();
            _instance = new InstanceGuard();
            if (!_instance.Acquire("ParvNegar-" + Bytes.ToHex(Bytes.Sha256(System.Text.Encoding.UTF8.GetBytes(paths.Root.ToLowerInvariant()))).Substring(0, 16)))
            {
                MessageBox.Show(Strings.AlreadyRunning, Strings.WindowTitle, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                Shutdown(0);
                return;
            }

            var settings = smokeDir != null ? new UserSettings(Path.Combine(smokeDir, "settings.json")) : UserSettings.Default();
            ThemeManager.Apply(settings.Theme);

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
                    ShowMainAfterSplash(splash, smokeDir, clock, visibleSince, paths, settings)));
            };
            splash.Show();
        }

        private void ShowMainAfterSplash(SplashWindow splash, string smokeDir, Stopwatch clock, TimeSpan visibleSince, InstallationPaths paths, UserSettings settings)
        {
            DataFolderAcl.EnsureCreated(paths.Root, out _); // before anything is written into the data root
            _security = new SessionManager(paths, BuildTrust.Load(), new WindowsDeviceSignals(), new DpapiLocalMachineProtector(), new SystemClock(),
                new SessionOptions { Edition = Edition, Architecture = Environment.Is64BitProcess ? "x64" : "x86", AppVersion = MainViewModel.VersionString() });
            MainWindow main = null;
            var shell = new Shell(_security, settings, new WpfFileDialogs(), Dispatcher);
            var vm = new MainViewModel(XmlSchoolProfileSource.Default(),
                showAbout: () => new AboutWindow((MainViewModel)main.DataContext) { Owner = main }.ShowDialog(),
                exit: () => main.Close(), shell: shell);
            main = new MainWindow(vm);
            MainWindow = main; // ShutdownMode=OnMainWindowClose: from here on, closing the splash never ends the app.
            _monitor = new SessionMonitor(_security, Dispatcher);
            if (smokeDir != null && SmokeFlow.IsPrepare(_args)) { splash.Close(); Shutdown(SmokeFlow.Prepare(_security, smokeDir)); return; }
            if (smokeDir != null && SmokeFlow.IsFlow(_args)) SmokeFlow.Run(this, main, vm, smokeDir);
            else if (smokeDir != null) SmokeRunner.Attach(this, main, vm, smokeDir);

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

        protected override void OnExit(ExitEventArgs e)
        {
            _monitor?.Dispose();
            _security?.Dispose(); // closes the database and wipes the LMK
            _instance?.Dispose();
            base.OnExit(e);
        }

        /// <summary>"modern" for the .NET 10 build, "legacy" for the .NET Framework 4.8 build.</summary>
        public static string Edition =>
#if NETFRAMEWORK
            "legacy";
#else
            "modern";
#endif
    }

    internal sealed class WpfFileDialogs : IFileDialogs
    {
        public string OpenFile(string filter)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = false };
            return d.ShowDialog() == true ? d.FileName : null;
        }

        public string SaveFile(string filter, string suggestedName)
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = suggestedName, OverwritePrompt = true };
            return d.ShowDialog() == true ? d.FileName : null;
        }
    }
}
