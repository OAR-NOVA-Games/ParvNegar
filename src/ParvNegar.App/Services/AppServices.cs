using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ParvNegar.Application.Security;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.App.Services
{
    /// <summary>
    /// Non-sensitive per-user preferences: theme and, optionally, the last username. Never a password,
    /// key, token or student data. %LocalAppData%\OARNOVA\ParvNegar\settings.json.
    /// </summary>
    public sealed class UserSettings
    {
        private readonly string _path;

        public UserSettings(string path) { _path = path; Load(); }

        public static UserSettings Default() => new UserSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OARNOVA", "ParvNegar", "settings.json"));

        public string Theme { get; set; } = "light";
        public string RememberedUsername { get; set; }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                var v = StrictJson.Parse(File.ReadAllBytes(_path));
                foreach (var m in v.Members)
                {
                    if (m.Key == "theme" && m.Value.Kind == JsonKind.String && (m.Value.String == "light" || m.Value.String == "dark")) Theme = m.Value.String;
                    if (m.Key == "remembered_username" && m.Value.Kind == JsonKind.String) RememberedUsername = Usernames.Canonicalize(m.Value.String);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is LicenseFormatException || ex is UnauthorizedAccessException) { }
        }

        public void Save()
        {
            try
            {
                var members = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, JsonValue>>
                {
                    new System.Collections.Generic.KeyValuePair<string, JsonValue>("theme", JsonValue.Str(Theme)),
                };
                if (RememberedUsername != null) members.Add(new System.Collections.Generic.KeyValuePair<string, JsonValue>("remembered_username", JsonValue.Str(RememberedUsername)));
                AtomicFile.Write(_path, StrictJson.Canonical(JsonValue.Obj(members)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }

    /// <summary>Swaps the theme dictionary at runtime; every view uses DynamicResource, so no restart is needed.</summary>
    public static class ThemeManager
    {
        public static event EventHandler ThemeChanged;

        public static string Current { get; private set; } = "light";
        public static bool IsDark => Current == "dark";

        public static void Apply(string theme)
        {
            theme = theme == "dark" ? "dark" : "light";
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            var old = dicts.FirstOrDefault(d => d.Source != null && (d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.Ordinal) || d.Source.OriginalString.EndsWith("Dark.xaml", StringComparison.Ordinal)));
            var fresh = new ResourceDictionary { Source = new Uri("pack://application:,,,/ParvNegar;component/Themes/" + (theme == "dark" ? "Dark" : "Light") + ".xaml") };
            if (old != null) dicts[dicts.IndexOf(old)] = fresh;
            else dicts.Insert(0, fresh);
            Current = theme;
            foreach (Window w in System.Windows.Application.Current.Windows) ApplyChrome(w);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Dark title bar via DWMWA_USE_IMMERSIVE_DARK_MODE (attribute 20; 19 on Windows 10 1809–1909).
        /// Windows 7 and 8.1 have no such attribute: the call fails harmlessly and the title bar stays standard.
        /// </summary>
        public static void ApplyChrome(Window w)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) { w.SourceInitialized -= OnSourceInitialized; w.SourceInitialized += OnSourceInitialized; return; }
            try
            {
                int on = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
        }

        private static void OnSourceInitialized(object sender, EventArgs e) => ApplyChrome((Window)sender);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>One running ParvNegar per Windows installation (all Windows sessions on the PC).</summary>
    public sealed class InstanceGuard : IDisposable
    {
        private Mutex _mutex;

        public bool Acquire(string name)
        {
            foreach (var scope in new[] { "Global\\", "Local\\" })
            {
                try
                {
                    _mutex = new Mutex(true, scope + name, out bool created);
                    if (created) return true;
                    _mutex.Dispose(); _mutex = null;
                    return false;
                }
                catch (UnauthorizedAccessException) { _mutex = null; } // Global namespace refused: fall back to Local
            }
            return true;
        }

        public void Dispose() { try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { } _mutex?.Dispose(); _mutex = null; }
    }

    /// <summary>
    /// Connects Windows events to the session: Windows lock, user switching and remote disconnect lock the
    /// session at once; suspend/hibernate locks before the PC sleeps; inactivity is counted only from input
    /// in ParvNegar windows (keyboard, mouse buttons and wheel; not mouse movement, not other applications).
    /// </summary>
    public sealed class SessionMonitor : IDisposable
    {
        private readonly SessionManager _sm;
        private readonly DispatcherTimer _timer;

        public SessionMonitor(SessionManager sm, Dispatcher dispatcher)
        {
            _sm = sm;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            InputManager.Current.PreProcessInput += OnInput;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (s, e) => _sm.CheckInactivity(), dispatcher);
            _timer.Start();
        }

        internal static bool IsMeaningful(InputEventArgs e) => e is KeyEventArgs || e is MouseButtonEventArgs || e is MouseWheelEventArgs || e is TextCompositionEventArgs || e is TouchEventArgs || e is StylusDownEventArgs;

        private void OnInput(object sender, PreProcessInputEventArgs e)
        {
            if (IsMeaningful(e.StagingItem.Input)) _sm.Touch();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock: _sm.Lock(LockReason.WindowsLock); break;
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                case SessionSwitchReason.SessionLogoff: _sm.Lock(LockReason.SessionSwitch); break;
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) _sm.Lock(LockReason.Suspend);
            else if (e.Mode == PowerModes.Resume) _sm.Lock(LockReason.Suspend); // defensive: nothing stays unlocked after resume
        }

        public void Dispose()
        {
            _timer.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            InputManager.Current.PreProcessInput -= OnInput;
        }
    }

    /// <summary>
    /// Trust anchors of this build, embedded at build time from Config/trust/&lt;profile&gt;.json
    /// (ParvTrustProfile: Development by default; Production only for release builds). A production build
    /// contains only production keys; a development build trusts only development keys.
    /// </summary>
    public static class BuildTrust
    {
        public static TrustEnvironment Environment
        {
            get
            {
                var attr = typeof(BuildTrust).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ParvTrustProfile");
                return attr != null && attr.Value == "Production" ? TrustEnvironment.Production : TrustEnvironment.Development;
            }
        }

        public static TrustStore Load()
        {
            using (var s = typeof(BuildTrust).Assembly.GetManifestResourceStream("ParvNegar.trust-anchors.json"))
            {
                if (s == null) return new TrustStore(Environment, new TrustedKey[0]);
                using (var ms = new MemoryStream()) { s.CopyTo(ms); return TrustStore.Load(ms.ToArray(), Environment); }
            }
        }
    }

    public static class FileLimits
    {
        public const int MaxArtifactBytes = 64 * 1024;

        public static byte[] ReadSmall(string path)
        {
            var fi = new FileInfo(path);
            if (fi.Length > MaxArtifactBytes) throw new InvalidDataException("too large");
            return File.ReadAllBytes(path);
        }
    }
}
