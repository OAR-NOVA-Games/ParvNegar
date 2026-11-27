using System;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParvNegar.App.Services;
using ParvNegar.Application;
using ParvNegar.Domain;

namespace ParvNegar.App.ViewModels
{
    /// <summary>
    /// Main window view model: the branded home screen (no data) and, when a security shell is attached,
    /// the current gated page. Without a shell (tests, Phase 1 smoke) Start shows the placeholder.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private bool _isHome = true;
        private readonly string _logoLight, _logoDark;

        public MainViewModel(ISchoolProfileSource profileSource, Action showAbout, Action exit, Shell shell = null)
        {
            if (profileSource == null) throw new ArgumentNullException(nameof(profileSource));
            Profile = profileSource.Load();
            if (profileSource is XmlSchoolProfileSource xml) { _logoLight = xml.LogoLightSurfacePath; _logoDark = xml.LogoDarkSurfacePath; }
            Shell = shell;
            StartCommand = new RelayCommand(Start);
            BackCommand = new RelayCommand(Back);
            AboutCommand = new RelayCommand(() => showAbout?.Invoke());
            ExitCommand = new RelayCommand(() => exit?.Invoke());
            if (shell != null) shell.PropertyChanged += (s, e) => { OnPropertyChanged(nameof(CurrentPage)); OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(IsPlaceholder)); OnPropertyChanged(nameof(HasPage)); };
            ThemeManager.ThemeChanged += (s, e) => { OnPropertyChanged(nameof(SchoolLogo)); OnPropertyChanged(nameof(IsDarkTheme)); OnPropertyChanged(nameof(IsLightTheme)); };
        }

        public Shell Shell { get; }
        public PageViewModel CurrentPage => Shell?.Page;
        public bool HasPage => Shell != null && Shell.HasPage;

        /// <summary>School logo variant for the current theme (owner originals, never recolored).</summary>
        public System.Windows.Media.ImageSource SchoolLogo
        {
            get
            {
                var path = ThemeManager.IsDark ? (_logoDark ?? _logoLight) : (_logoLight ?? _logoDark);
                if (path == null) return null;
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.DecodePixelWidth = 320;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is NotSupportedException || ex is UriFormatException) { return null; }
            }
        }

        public bool IsDarkTheme => ThemeManager.IsDark;
        public bool IsLightTheme => !ThemeManager.IsDark;

        public bool HasSchoolLogo => (_logoLight ?? _logoDark) != null;

        private void Start()
        {
            if (Shell == null) { IsHome = false; return; }
            Shell.NavigateForState();
        }

        private void Back()
        {
            if (Shell == null) { IsHome = true; return; }
            // Escape never leaves a signed-in workspace (it would only hide it, not lock it).
            if (Shell.Security.HasSession) return;
            if (Shell.Page is RecoveryPageViewModel r && !r.DeviceLost) { Shell.NavigateForState(); return; }
            Shell.Home();
        }

        public SchoolProfile Profile { get; }
        public bool HasProfile => Profile != null;

        /// <summary>Exact configured title; never transformed for display.</summary>
        public string HomeTitle => Profile != null ? Profile.HomeTitle : Strings.ProfileMissing;
        public string SchoolName => Profile != null ? Profile.SchoolName : string.Empty;

        public string ProductName => ProductIdentity.ProductNameFa;
        public string Tagline => Strings.Tagline;
        public string PublisherLine => Strings.PublisherPrefix + " " + ProductIdentity.PublisherName;
        public string VersionText => Strings.VersionPrefix + " " + PersianText.ToPersianDigits(VersionString()) + " " + Strings.DevSuffix;
        public string DevNotice => Strings.DevNotice;

        public bool IsHome
        {
            get => Shell == null ? _isHome : !Shell.HasPage;
            set { if (SetProperty(ref _isHome, value)) OnPropertyChanged(nameof(IsPlaceholder)); }
        }

        public bool IsPlaceholder => Shell == null && !_isHome;

        public ICommand StartCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand ExitCommand { get; }

        public static string VersionString()
        {
            var v = typeof(MainViewModel).Assembly.GetName().Version;
            return v.Major + "." + v.Minor + "." + v.Build;
        }
    }
}
