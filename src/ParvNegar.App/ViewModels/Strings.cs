using System;
using ParvNegar.Application.Security;
using ParvNegar.Domain;
using ParvNegar.Licensing;
using ParvNegar.Security;

namespace ParvNegar.App.ViewModels
{
    /// <summary>
    /// Every customer-facing Persian string, in one place for review (UI-UX-REPORT.md §3 terminology).
    /// Technical exceptions are never shown: results are mapped to these messages.
    /// </summary>
    public static class Strings
    {
        // Shell
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
        public const string PlaceholderBody = "در این نسخه توسعه، پرونده دانش‌آموزان هنوز فعال نیست.";
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
        public const string AboutLicense = "مجوز";
        public const string AlreadyRunning = "پرونگار هم‌اکنون روی این رایانه باز است. لطفاً از همان پنجره استفاده کنید.";

        // License
        public const string LicenseTitle = "مجوز نرم‌افزار";
        public const string LicenseIntro = "برای استفاده از پرونگار، این رایانه باید مجوز معتبر داشته باشد. فعال‌سازی کاملاً بدون اینترنت انجام می‌شود.";
        public const string LicenseStep1 = "۱. فایل درخواست فعال‌سازی را بسازید و برای OAR NOVA بفرستید.";
        public const string LicenseStep2 = "۲. فایل مجوزی را که دریافت کرده‌اید وارد کنید.";
        public const string CreateRequest = "ساخت فایل درخواست";
        public const string ImportLicense = "وارد کردن فایل مجوز";
        public const string RequestSaved = "فایل درخواست ذخیره شد. کد تأیید آن را هنگام تماس با پشتیبانی اعلام کنید:";
        public const string RequestNoSecrets = "این فایل هیچ رمز عبور، کلید یا اطلاعات دانش‌آموزی ندارد.";
        public const string LicenseValid = "مجوز این رایانه معتبر است.";
        public const string LicenseNone = "هنوز مجوزی روی این رایانه نصب نشده است.";
        public const string LicensePerpetual = "دائمی";
        public const string LicenseCustomer = "مشتری";
        public const string LicenseId = "شناسه مجوز";
        public const string LicenseType = "نوع مجوز";
        public const string Continue = "ادامه";
        public const string RequestFileFilter = "درخواست فعال‌سازی پرونگار (*.pnreq)|*.pnreq";
        public const string LicenseFileFilter = "مجوز پرونگار (*.pnlic)|*.pnlic";
        public const string ProvisioningFileFilter = "فایل راه‌اندازی مالک (*.pnprov)|*.pnprov";
        public const string FileTooLarge = "این فایل برای مجوز یا راه‌اندازی بیش از حد بزرگ است.";
        public const string InsufficientIdentity = "شناسه سخت‌افزاری کافی برای فعال‌سازی روی این رایانه خوانده نشد. با پشتیبانی OAR NOVA تماس بگیرید.";
        public const string FileUnreadable = "فایل خوانده نشد. دسترسی به فایل را بررسی کنید.";

        // Provisioning and setup
        public const string ProvisioningTitle = "راه‌اندازی اولیه";
        public const string ProvisioningIntro = "OAR NOVA یک فایل راه‌اندازی و، جداگانه، یک کد یک‌بارمصرف برای مالک این نصب ارسال می‌کند. ابتدا فایل راه‌اندازی را وارد کنید.";
        public const string ImportProvisioning = "وارد کردن فایل راه‌اندازی";
        public const string SetupTitle = "تعیین رمز عبور مالک";
        public const string SetupIntro = "کد یک‌بارمصرفی را که جداگانه دریافت کرده‌اید وارد کنید و یک رمز عبور تازه برای خودتان انتخاب کنید.";
        public const string OwnerUsername = "نام کاربری مالک";
        public const string BootstrapCode = "کد یک‌بارمصرف";
        public const string NewPassword = "رمز عبور جدید";
        public const string RepeatPassword = "تکرار رمز عبور جدید";
        public const string CompleteSetup = "پایان راه‌اندازی";
        public const string PasswordHint = "دست‌کم ۱۵ نویسه. می‌توانید از جمله‌ای فارسی یا انگلیسی با فاصله استفاده کنید؛ ترکیب اجباری حروف و عدد لازم نیست. چسباندن متن مجاز است.";
        public const string PasswordsDiffer = "رمز عبور و تکرار آن یکسان نیستند.";

        // Recovery key
        public const string RecoveryKeyTitle = "کلید بازیابی مالک";
        public const string RecoveryKeyIntro = "این کلید تنها راه بازیابی دسترسی مالک در صورت فراموش کردن رمز عبور یا نصب دوباره ویندوز است. فقط همین یک بار نمایش داده می‌شود و در هیچ جای رایانه ذخیره نمی‌شود.";
        public const string RecoveryKeyWarning = "اگر رمز عبور و این کلید هر دو از دست بروند، اطلاعات این نصب قابل بازیابی نیست؛ OAR NOVA نیز هیچ کلید یا رمز جایگزینی ندارد.";
        public const string RecoveryKeyStore = "آن را روی کاغذ بنویسید یا چاپ کنید و در جای امن، جدا از این رایانه، نگه دارید.";
        public const string RecoveryKeyConfirmPrompt = "برای تأیید، بخش آخر کلید (پنج نویسه آخر) را وارد کنید:";
        public const string RecoveryKeyConfirm = "کلید را در جای امن نگه داشته‌ام";
        public const string RecoveryKeyConfirmWrong = "بخش واردشده با کلید یکسان نیست. دوباره بررسی کنید.";
        public const string RecoveryKeyLost = "کلید نمایش‌داده‌شده در دسترس نیست. برای امنیت، یک کلید بازیابی تازه بسازید.";
        public const string RecoveryKeyNew = "ساخت کلید بازیابی تازه";
        public const string CopyToClipboardWarning = "کلید را کپی نکنید؛ کلیپ‌بورد ویندوز برای نگهداری کلید امن نیست.";

        // Login and lock
        public const string LoginTitle = "ورود";
        public const string LockedTitle = "نشست قفل شد";
        public const string LockedBody = "برای ادامه دوباره وارد شوید.";
        public const string Username = "نام کاربری";
        public const string Password = "رمز عبور";
        public const string RememberUsername = "نام کاربری را به خاطر بسپار (رمز عبور هرگز ذخیره نمی‌شود)";
        public const string SignIn = "ورود";
        public const string ForgotOwnerPassword = "بازیابی دسترسی مالک";
        public const string LockReasonInactivity = "به دلیل ۱۵ دقیقه عدم فعالیت، نشست قفل شد.";
        public const string LockReasonWindows = "به دلیل قفل شدن ویندوز، نشست قفل شد.";
        public const string LockReasonSuspend = "به دلیل خواب یا خاموشی موقت رایانه، نشست قفل شد.";
        public const string LockReasonAccount = "وضعیت حساب شما تغییر کرد؛ لطفاً دوباره وارد شوید.";

        // Password change
        public const string ChangePasswordTitle = "تغییر رمز عبور";
        public const string ChangePasswordRequired = "پیش از ادامه باید رمز عبور موقت را به رمزی که فقط خودتان می‌دانید تغییر دهید.";
        public const string CurrentPassword = "رمز عبور فعلی";
        public const string ChangePassword = "تغییر رمز عبور";
        public const string PasswordChanged = "رمز عبور با موفقیت تغییر کرد.";

        // Recovery
        public const string RecoveryTitle = "بازیابی دسترسی مالک";
        public const string RecoveryIntro = "با نام کاربری مالک و کلید بازیابی، رمز عبور تازه تعیین کنید. پس از بازیابی، یک کلید بازیابی جدید ساخته می‌شود و کلید قبلی از کار می‌افتد.";
        public const string RecoveryDeviceIntro = "کلید امنیتی این رایانه در دسترس نیست (برای نمونه پس از نصب دوباره ویندوز). مالک می‌تواند با کلید بازیابی دسترسی را برگرداند؛ پس از آن، رمز عبور سایر کاربران باید توسط مالک از نو تعیین شود.";
        public const string RecoveryKey = "کلید بازیابی";
        public const string Recover = "بازیابی";

        // Workspace
        public const string WorkspaceTitle = "پیشخوان";
        public const string WorkspaceWelcome = "خوش آمدید";
        public const string WorkspaceEmpty = "بخش پرونده دانش‌آموزان در مرحله بعدی توسعه فعال می‌شود. در این نسخه، مدیریت حساب‌ها، مجوز و امنیت در دسترس است.";
        public const string NavHome = "پیشخوان";
        public const string NavAccounts = "حساب‌ها و دسترسی‌ها";
        public const string NavSecurity = "امنیت";
        public const string NavSettings = "تنظیمات";
        public const string NavLicense = "مجوز";
        public const string NavAudit = "رویدادهای امنیتی";
        public const string Lock = "قفل";
        public const string Logout = "خروج از حساب";
        public const string RoleOwner = "مالک";
        public const string RoleDelegate = "مدیر واگذارشده";

        // Accounts
        public const string AccountsTitle = "حساب‌ها و دسترسی‌ها";
        public const string AccountsIntro = "مدیران واگذارشده در ابتدا هیچ دسترسی ندارند؛ هر دسترسی را جداگانه به آن‌ها بدهید.";
        public const string NewDelegate = "مدیر واگذارشده جدید";
        public const string DisplayName = "نام نمایشی";
        public const string TemporaryPassword = "رمز عبور موقت";
        public const string TemporaryPasswordHint = "کاربر باید در نخستین ورود این رمز را تغییر دهد. آن را حضوری به او بدهید.";
        public const string CreateAccount = "ساخت حساب";
        public const string SavePermissions = "ذخیره دسترسی‌ها";
        public const string Disable = "غیرفعال کردن";
        public const string Enable = "فعال کردن";
        public const string Delete = "حذف حساب";
        public const string ResetPassword = "تعیین رمز موقت جدید";
        public const string StatusActive = "فعال";
        public const string StatusDisabled = "غیرفعال";
        public const string StatusNeedsReset = "نیازمند رمز موقت جدید";
        public const string StatusMustChange = "باید رمز را تغییر دهد";
        public const string Saved = "ذخیره شد.";
        public const string AccountCreated = "حساب ساخته شد.";
        public const string LastOwner = "آخرین مالک فعال را نمی‌توان غیرفعال، حذف یا به نقش دیگری منتقل کرد.";
        public const string NotAllowed = "شما اجازه انجام این کار را ندارید.";
        public const string UsernameRule = "نام کاربری: ۳ تا ۳۲ نویسه از حروف کوچک انگلیسی، رقم، نقطه، خط تیره یا زیرخط و آغاز با حرف.";
        public const string UsernameTaken = "این نام کاربری قبلاً استفاده شده است.";
        public const string SelectAccount = "یک حساب را از فهرست انتخاب کنید.";
        public const string DelegateCannotSeeAccounts = "مدیریت حساب‌ها فقط برای مالک در دسترس است.";
        public const string ConfirmDelete = "برای حذف، دوباره روی «حذف حساب» بزنید.";

        // Security page
        public const string SecurityTitle = "امنیت";
        public const string RotateRecoveryIntro = "اگر گمان می‌کنید کلید بازیابی دیده شده یا گم شده است، کلید تازه بسازید. کلید قبلی بی‌درنگ از کار می‌افتد.";
        public const string AuditTitle = "رویدادهای امنیتی";
        public const string AuditIntact = "زنجیره رویدادها سالم است.";
        public const string AuditBroken = "زنجیره رویدادها دست‌کاری شده یا ناقص است.";
        public const string AuditLimit = "این فهرست تغییر در رویدادهای ثبت‌شده را آشکار می‌کند، اما در برابر جایگزینی کامل پایگاه داده یا حذف کل آن مقاوم نیست.";

        // Settings
        public const string SettingsTitle = "تنظیمات";
        public const string Theme = "پوسته نمایش";
        public const string ThemeLight = "روشن";
        public const string ThemeDark = "تیره";
        public const string ThemeSaved = "پوسته نمایش تغییر کرد.";

        // Invalid installation
        public const string InvalidTitle = "نصب ناقص یا آسیب‌دیده";
        public const string InvalidBody = "پوشه اطلاعات پرونگار کامل نیست یا آسیب دیده است. برای حفظ اطلاعات، برنامه هیچ پایگاه داده تازه‌ای نمی‌سازد. لطفاً با پشتیبانی OAR NOVA تماس بگیرید.";

        public static string TimeLeft(TimeSpan t)
        {
            var s = (int)Math.Ceiling(t.TotalSeconds);
            return s >= 60 ? PersianText.ToPersianDigits(((s + 59) / 60).ToString(System.Globalization.CultureInfo.InvariantCulture)) + " دقیقه" : PersianText.ToPersianDigits(s.ToString(System.Globalization.CultureInfo.InvariantCulture)) + " ثانیه";
        }

        public static string For(AuthOutcome o)
        {
            switch (o.Result)
            {
                case AuthResult.Success: return null;
                case AuthResult.InvalidCredentials: return "اطلاعات ورود درست نیست.";
                case AuthResult.Throttled: return "به دلیل تلاش‌های ناموفق، لطفاً " + TimeLeft(o.RetryAfter) + " دیگر دوباره تلاش کنید.";
                case AuthResult.AccountDisabled: return "این حساب غیرفعال است. با مالک نصب تماس بگیرید.";
                case AuthResult.Busy: return "عملیات قبلی هنوز در حال انجام است. چند لحظه صبر کنید.";
                case AuthResult.LicenseInvalid: return "مجوز معتبری روی این رایانه نصب نیست.";
                case AuthResult.NotInitialized: return "راه‌اندازی اولیه هنوز انجام نشده است.";
                case AuthResult.DeviceUnavailable: return "کلید امنیتی این رایانه در دسترس نیست. از بازیابی دسترسی مالک استفاده کنید.";
                case AuthResult.PasswordPolicy: return For(o.PasswordProblem);
                case AuthResult.InvalidCode: return "کد واردشده نادرست تایپ شده است. نویسه‌ها را با دقت دوباره بررسی کنید.";
                case AuthResult.InvalidProvisioning: return "فایل راه‌اندازی معتبر نیست یا آسیب دیده است.";
                case AuthResult.AlreadyInitialized: return "این نصب قبلاً راه‌اندازی شده است؛ فایل راه‌اندازی دوباره پذیرفته نمی‌شود.";
                case AuthResult.StaleArtifact: return "این فایل راه‌اندازی برای درخواست یا مجوز دیگری صادر شده است. آخرین فایل درخواست همین رایانه را به OAR NOVA بفرستید.";
                case AuthResult.WrongState: return "این کار در وضعیت فعلی امکان‌پذیر نیست.";
                default: return "عملیات انجام نشد. اگر تکرار شد، با پشتیبانی تماس بگیرید.";
            }
        }

        public static string For(PasswordProblem p)
        {
            switch (p)
            {
                case PasswordProblem.TooShort: return "رمز عبور باید دست‌کم ۱۵ نویسه باشد.";
                case PasswordProblem.TooLong: return "رمز عبور حداکثر می‌تواند ۱۲۸ نویسه باشد.";
                case PasswordProblem.InvalidCharacters: return "رمز عبور نویسه نامعتبر (مانند Tab یا سطر جدید) دارد.";
                case PasswordProblem.Common: return "این رمز عبور بسیار رایج است. عبارت دیگری انتخاب کنید.";
                case PasswordProblem.ContainsUsername: return "رمز عبور نباید بر پایه نام کاربری باشد.";
                case PasswordProblem.Repetitive: return "رمز عبور بیش از حد تکراری یا پشت‌سرهم است.";
                default: return null;
            }
        }

        public static string For(LicenseCheck c)
        {
            if (c == null) return LicenseNone;
            switch (c.Status)
            {
                case LicenseStatus.Valid: return LicenseValid;
                case LicenseStatus.Malformed: return "فایل مجوز خوانا یا کامل نیست.";
                case LicenseStatus.UnsupportedFormat: return "قالب این فایل مجوز با این نسخه از پرونگار سازگار نیست.";
                case LicenseStatus.WrongProduct: return "این فایل مجوز برای محصول دیگری است.";
                case LicenseStatus.UnknownKey: return "امضای این مجوز از سوی ناشر شناخته‌شده‌ای نیست.";
                case LicenseStatus.InvalidSignature: return "امضای فایل مجوز معتبر نیست؛ فایل تغییر کرده یا آسیب دیده است.";
                case LicenseStatus.WrongEdition: return "این مجوز برای این نسخه یا معماری از پرونگار صادر نشده است.";
                case LicenseStatus.DeviceMismatch: return "این مجوز برای رایانه دیگری است یا سخت‌افزار این رایانه تغییر زیادی کرده است. برای صدور دوباره با OAR NOVA تماس بگیرید.";
                case LicenseStatus.InsufficientDeviceIdentity: return InsufficientIdentity;
                default: return "مجوز معتبر نیست.";
            }
        }

        public static string RoleName(AccountRole r) => r == AccountRole.Owner ? RoleOwner : RoleDelegate;

        public static string PermissionName(string id)
        {
            switch (id)
            {
                case Permissions.StudentView: return "مشاهده پرونده دانش‌آموزان";
                case Permissions.StudentCreate: return "ثبت دانش‌آموز جدید";
                case Permissions.StudentEdit: return "ویرایش اطلاعات دانش‌آموز";
                case Permissions.StudentDeleteRestore: return "حذف و بازگردانی دانش‌آموز";
                case Permissions.EducationalRecords: return "مدیریت سوابق آموزشی";
                case Permissions.PastoralRecords: return "مدیریت سوابق پرورشی";
                case Permissions.Documents: return "مدیریت مدارک";
                case Permissions.Print: return "چاپ";
                case Permissions.ExportPdf: return "خروجی PDF";
                case Permissions.YearPromote: return "انتقال به سال تحصیلی بعد";
                case Permissions.YearManage: return "مدیریت سال تحصیلی";
                case Permissions.BackupCreate: return "تهیه نسخه پشتیبان";
                default: return id;
            }
        }

        public static string AuditKindName(string kind)
        {
            switch (kind)
            {
                case AuditKinds.Setup: return "راه‌اندازی نصب";
                case AuditKinds.LoginSuccess: return "ورود موفق";
                case AuditKinds.LoginFailure: return "ورود ناموفق";
                case AuditKinds.LoginThrottled: return "تأخیر پس از تلاش‌های ناموفق";
                case AuditKinds.Logout: return "خروج";
                case AuditKinds.SessionLock: return "قفل نشست";
                case AuditKinds.PasswordChange: return "تغییر رمز عبور";
                case AuditKinds.PasswordReset: return "تعیین رمز موقت";
                case AuditKinds.AccountCreate: return "ساخت حساب";
                case AuditKinds.AccountDisable: return "غیرفعال‌سازی حساب";
                case AuditKinds.AccountEnable: return "فعال‌سازی حساب";
                case AuditKinds.AccountDelete: return "حذف حساب";
                case AuditKinds.PermissionChange: return "تغییر دسترسی";
                case AuditKinds.Recovery: return "بازیابی دسترسی مالک";
                case AuditKinds.RecoveryFailure: return "بازیابی ناموفق";
                case AuditKinds.RecoveryKeyRotate: return "تعویض کلید بازیابی";
                case AuditKinds.RecoveryKeyConfirmed: return "تأیید نگهداری کلید بازیابی";
                case AuditKinds.DeviceRebind: return "اتصال دوباره به این رایانه";
                case AuditKinds.LicenseImport: return "ورود مجوز";
                case AuditKinds.SecurityFailure: return "هشدار امنیتی";
                case AuditKinds.BootstrapFailure: return "کد راه‌اندازی نادرست";
                default: return kind;
            }
        }
    }
}
