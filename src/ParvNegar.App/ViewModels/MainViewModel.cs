using System;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParvNegar.Application;
using ParvNegar.Domain;

namespace ParvNegar.App.ViewModels
{
    /// <summary>Home shell view model. Shows no student data: authentication and licensing do not exist yet.</summary>
    public sealed class MainViewModel : ObservableObject
    {
        private bool _isHome = true;

        public MainViewModel(ISchoolProfileSource profileSource, Action showAbout, Action exit)
        {
            if (profileSource == null) throw new ArgumentNullException(nameof(profileSource));
            Profile = profileSource.Load();
            StartCommand = new RelayCommand(() => IsHome = false);
            BackCommand = new RelayCommand(() => IsHome = true);
            AboutCommand = new RelayCommand(() => showAbout?.Invoke());
            ExitCommand = new RelayCommand(() => exit?.Invoke());
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
            get => _isHome;
            set { if (SetProperty(ref _isHome, value)) OnPropertyChanged(nameof(IsPlaceholder)); }
        }

        public bool IsPlaceholder => !_isHome;

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

    /// <summary>Customer-facing Persian strings (single place for review and future localization).</summary>
    public static class Strings
    {
        public const string WindowTitle = "پرونگار";
        public const string Tagline = "سامانه مدیریت پرونده‌های دانش‌آموزی";
        public const string Start = "شروع";
        public const string About = "درباره";
        public const string Exit = "خروج";
        public const string Back = "بازگشت";
        public const string Close = "بستن";
        public const string ProfileMissing = "پیکربندی مدرسه یافت نشد.";
        public const string DevNotice = "نسخه توسعه؛ فقط برای آزمایش و بدون داده واقعی دانش‌آموزان";
        public const string PlaceholderTitle = "بخش‌های برنامه در حال آماده‌سازی است";
        public const string PlaceholderBody = "در این نسخه توسعه، ورود کاربران، مجوز نرم‌افزار و پرونده دانش‌آموزان هنوز فعال نیست.";
        public const string PublisherPrefix = "محصولی از";
        public const string VersionPrefix = "نسخه";
        public const string DevSuffix = "(توسعه)";
        public const string AboutTitle = "درباره پرونگار";
        public const string AboutPublisher = "ناشر";
        public const string AboutDistribution = "توزیع";
        public const string AboutSchool = "مدرسه";
        public const string AboutCopyright = "© ۲۰۲۶؛ تمامی حقوق برای OAR NOVA Games Studio محفوظ است.";
        public const string AboutFont = "قلم وزیرمتن با مجوز آزاد SIL Open Font License (نسخه ۱٫۱)";
        public const string AboutOffline = "این نرم‌افزار بدون نیاز به اینترنت کار می‌کند.";
    }
}
