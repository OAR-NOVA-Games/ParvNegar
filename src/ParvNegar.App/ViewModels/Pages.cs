using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParvNegar.App.Services;
using ParvNegar.Application.Security;
using ParvNegar.Domain;
using ParvNegar.Licensing;

namespace ParvNegar.App.ViewModels
{
    /// <summary>File dialogs are injected so pages stay testable and smoke runs never open a dialog.</summary>
    public interface IFileDialogs
    {
        string OpenFile(string filter);
        string SaveFile(string filter, string suggestedName);
    }

    public abstract class PageViewModel : ObservableObject
    {
        private string _message;
        private bool _isError;
        private bool _isBusy;

        protected PageViewModel(Shell shell) { Shell = shell; }

        public Shell Shell { get; }
        protected SessionManager Sm => Shell.Security;
        public abstract string Title { get; }

        public string Message { get => _message; set { SetProperty(ref _message, value); OnPropertyChanged(nameof(HasMessage)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(HasSuccess)); } }
        public bool IsError { get => _isError; set { SetProperty(ref _isError, value); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(HasSuccess)); } }
        public bool HasMessage => !string.IsNullOrEmpty(Message);
        public bool HasError => HasMessage && IsError;
        public bool HasSuccess => HasMessage && !IsError;
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        protected void Error(string m) { IsError = true; Message = m; }
        protected void Success(string m) { IsError = false; Message = m; }
        protected bool Report(AuthOutcome o) { if (o.Ok) { Message = null; return true; } Error(Strings.For(o)); return false; }

        /// <summary>Clears anything sensitive the page holds (called on lock and navigation).</summary>
        public virtual void Clear() { Message = null; }
    }

    // ---------------------------------------------------------------- license

    public sealed class LicensePageViewModel : PageViewModel
    {
        private string _confirmation;

        public LicensePageViewModel(Shell shell) : base(shell)
        {
            CreateRequestCommand = new RelayCommand(CreateRequest);
            ImportCommand = new RelayCommand(Import);
            ContinueCommand = new RelayCommand(() => Shell.NavigateForState(), () => Sm.License != null && Sm.License.IsValid);
            Refresh();
        }

        public override string Title => Strings.LicenseTitle;
        public string Status => Strings.For(Sm.License);
        public bool IsValid => Sm.License != null && Sm.License.IsValid;
        public bool IsInvalid => !IsValid;
        public string CustomerRef => Sm.License?.Claims?.CustomerRef;
        public string LicenseIdText => Sm.License?.Claims?.LicenseId;
        public string Confirmation { get => _confirmation; set { SetProperty(ref _confirmation, value); OnPropertyChanged(nameof(HasConfirmation)); } }
        public bool HasConfirmation => !string.IsNullOrEmpty(Confirmation);
        public ICommand CreateRequestCommand { get; }
        public ICommand ImportCommand { get; }
        public RelayCommand ContinueCommand { get; }

        private void Refresh()
        {
            OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(IsValid)); OnPropertyChanged(nameof(IsInvalid));
            OnPropertyChanged(nameof(CustomerRef)); OnPropertyChanged(nameof(LicenseIdText));
            ContinueCommand.NotifyCanExecuteChanged();
        }

        private void CreateRequest()
        {
            try
            {
                var bytes = Sm.GenerateActivationRequest(out var req);
                var path = Shell.Dialogs.SaveFile(Strings.RequestFileFilter, "ParvNegar-activation-" + req.RequestId.Substring(0, 8) + ".pnreq");
                if (path != null) File.WriteAllBytes(path, bytes);
                Confirmation = req.ConfirmationCode();
                Success(Strings.RequestSaved);
            }
            catch (InsufficientDeviceIdentityException) { Error(Strings.InsufficientIdentity); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Error(Strings.FileUnreadable); }
        }

        public void ImportBytes(byte[] bytes)
        {
            var r = Sm.ImportLicense(bytes);
            Refresh();
            if (r.IsValid) Success(Strings.LicenseValid); else Error(Strings.For(r));
        }

        private void Import()
        {
            var path = Shell.Dialogs.OpenFile(Strings.LicenseFileFilter);
            if (path == null) return;
            try { ImportBytes(FileLimits.ReadSmall(path)); }
            catch (InvalidDataException) { Error(Strings.FileTooLarge); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Error(Strings.FileUnreadable); }
        }
    }

    // ---------------------------------------------------------------- provisioning and setup

    public sealed class ProvisioningPageViewModel : PageViewModel
    {
        public ProvisioningPageViewModel(Shell shell) : base(shell) { ImportCommand = new RelayCommand(Import); }
        public override string Title => Strings.ProvisioningTitle;
        public ICommand ImportCommand { get; }

        public void ImportBytes(byte[] bytes)
        {
            if (Report(Sm.LoadProvisioning(bytes))) Shell.NavigateForState();
        }

        private void Import()
        {
            var path = Shell.Dialogs.OpenFile(Strings.ProvisioningFileFilter);
            if (path == null) return;
            try { ImportBytes(FileLimits.ReadSmall(path)); }
            catch (InvalidDataException) { Error(Strings.FileTooLarge); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Error(Strings.FileUnreadable); }
        }
    }

    public sealed class SetupPageViewModel : PageViewModel
    {
        public SetupPageViewModel(Shell shell) : base(shell) { }
        public override string Title => Strings.SetupTitle;
        public string OwnerUsername => Sm.PendingProvisioning?.OwnerUsername;

        public void Submit(string bootstrapCode, string password, string repeat)
        {
            if (!string.Equals(password, repeat, StringComparison.Ordinal)) { Error(Strings.PasswordsDiffer); return; }
            var r = Sm.CompleteSetup(bootstrapCode, password);
            if (Report(r)) Shell.ShowRecoveryKey(r.RecoveryKeyCode);
        }
    }

    public sealed class RecoveryKeyPageViewModel : PageViewModel
    {
        private string _code;

        public RecoveryKeyPageViewModel(Shell shell, string code) : base(shell)
        {
            _code = code;
            NewKeyCommand = new RelayCommand(NewKey);
        }

        public override string Title => Strings.RecoveryKeyTitle;
        public string Code => _code;
        public bool HasCode => _code != null;
        public bool LacksCode => _code == null;
        public bool CanRotate => Sm.CurrentAccount?.Role == AccountRole.Owner;
        public ICommand NewKeyCommand { get; }

        public void Confirm(string lastGroup)
        {
            if (Sm.ConfirmRecoveryKeySaved(lastGroup, _code)) { Clear(); Shell.NavigateForState(); }
            else Error(Strings.RecoveryKeyConfirmWrong);
        }

        private void NewKey()
        {
            try
            {
                var r = Sm.RotateRecoveryKey(Sm.Token);
                _code = r.RecoveryKeyCode;
                OnPropertyChanged(nameof(Code)); OnPropertyChanged(nameof(HasCode)); OnPropertyChanged(nameof(LacksCode));
                Message = null;
            }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        public override void Clear() { _code = null; OnPropertyChanged(nameof(Code)); base.Clear(); }
    }

    // ---------------------------------------------------------------- login, lock, password change, recovery

    public sealed class LoginPageViewModel : PageViewModel
    {
        private string _username;
        private bool _remember;

        public LoginPageViewModel(Shell shell, LockReason? reason) : base(shell)
        {
            _username = shell.Settings.RememberedUsername ?? "";
            _remember = shell.Settings.RememberedUsername != null;
            LockMessage = reason == null ? null
                : reason == LockReason.Inactivity ? Strings.LockReasonInactivity
                : reason == LockReason.WindowsLock || reason == LockReason.SessionSwitch ? Strings.LockReasonWindows
                : reason == LockReason.Suspend ? Strings.LockReasonSuspend
                : reason == LockReason.AccountChanged ? Strings.LockReasonAccount : null;
            RecoverCommand = new RelayCommand(() => Shell.Show(new RecoveryPageViewModel(Shell, deviceLost: false)));
        }

        public override string Title => LockMessage != null ? Strings.LockedTitle : Strings.LoginTitle;
        public string LockMessage { get; }
        public bool IsLockScreen => LockMessage != null;
        public string Username { get => _username; set => SetProperty(ref _username, value); }
        public bool Remember { get => _remember; set => SetProperty(ref _remember, value); }
        public ICommand RecoverCommand { get; }

        public void Submit(string password)
        {
            var r = Sm.Login(Username, password);
            if (!Report(r)) return;
            Shell.Settings.RememberedUsername = Remember ? Usernames.Canonicalize(Username) : null;
            Shell.Settings.Save();
            Shell.NavigateForState();
        }
    }

    public sealed class ChangePasswordPageViewModel : PageViewModel
    {
        public ChangePasswordPageViewModel(Shell shell, bool required) : base(shell) { Required = required; }
        public override string Title => Strings.ChangePasswordTitle;
        public bool Required { get; }

        public void Submit(string current, string password, string repeat)
        {
            if (!string.Equals(password, repeat, StringComparison.Ordinal)) { Error(Strings.PasswordsDiffer); return; }
            if (!Report(Sm.ChangePassword(current, password))) return;
            if (Required) Shell.NavigateForState(); else Success(Strings.PasswordChanged);
        }
    }

    public sealed class RecoveryPageViewModel : PageViewModel
    {
        private string _username;
        public RecoveryPageViewModel(Shell shell, bool deviceLost) : base(shell)
        {
            DeviceLost = deviceLost;
            _username = shell.Settings.RememberedUsername ?? "";
            BackCommand = new RelayCommand(() => Shell.NavigateForState(), () => !deviceLost);
        }

        public override string Title => Strings.RecoveryTitle;
        public bool DeviceLost { get; }
        public string Intro => DeviceLost ? Strings.RecoveryDeviceIntro : Strings.RecoveryIntro;
        public string Username { get => _username; set => SetProperty(ref _username, value); }
        public ICommand BackCommand { get; }

        public void Submit(string recoveryKey, string password, string repeat)
        {
            if (!string.Equals(password, repeat, StringComparison.Ordinal)) { Error(Strings.PasswordsDiffer); return; }
            var r = Sm.RecoverOwner(Username, recoveryKey, password);
            if (Report(r)) Shell.ShowRecoveryKey(r.RecoveryKeyCode);
        }
    }

    public sealed class InvalidInstallationPageViewModel : PageViewModel
    {
        public InvalidInstallationPageViewModel(Shell shell) : base(shell) { }
        public override string Title => Strings.InvalidTitle;
    }

    // ---------------------------------------------------------------- workspace

    public sealed class WorkspacePageViewModel : PageViewModel
    {
        private PageViewModel _section;

        public WorkspacePageViewModel(Shell shell) : base(shell)
        {
            var acc = Sm.CurrentAccount;
            IsOwner = acc != null && acc.Role == AccountRole.Owner;
            AccountName = acc?.DisplayName;
            RoleText = acc == null ? "" : Strings.RoleName(acc.Role);
            Permissions = new ObservableCollection<string>(Sm.EffectivePermissions().Where(ParvNegar.Application.Security.Permissions.IsGrantable).Select(Strings.PermissionName));
            HomeCommand = new RelayCommand(() => Section = new DashboardViewModel(Shell, this));
            AccountsCommand = new RelayCommand(() => Section = new AccountsViewModel(Shell), () => IsOwner);
            SecurityCommand = new RelayCommand(() => Section = new SecurityViewModel(Shell));
            SettingsCommand = new RelayCommand(() => Section = new SettingsViewModel(Shell));
            LicenseCommand = new RelayCommand(() => Section = new LicensePageViewModel(Shell));
            LockCommand = new RelayCommand(() => Sm.Lock(LockReason.Explicit));
            LogoutCommand = new RelayCommand(() => Sm.Lock(LockReason.Logout));
            _section = new DashboardViewModel(Shell, this);
        }

        public override string Title => Strings.WorkspaceTitle;
        public bool IsOwner { get; }
        public string AccountName { get; }
        public string RoleText { get; }
        public ObservableCollection<string> Permissions { get; }
        public PageViewModel Section { get => _section; set { _section?.Clear(); SetProperty(ref _section, value); } }
        public ICommand HomeCommand { get; }
        public RelayCommand AccountsCommand { get; }
        public ICommand SecurityCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand LicenseCommand { get; }
        public ICommand LockCommand { get; }
        public ICommand LogoutCommand { get; }

        public override void Clear() { _section?.Clear(); Permissions.Clear(); base.Clear(); }
    }

    public sealed class DashboardViewModel : PageViewModel
    {
        public DashboardViewModel(Shell shell, WorkspacePageViewModel ws) : base(shell) { Workspace = ws; }
        public override string Title => Strings.WorkspaceTitle;
        public WorkspacePageViewModel Workspace { get; }
        public string Welcome => Strings.WorkspaceWelcome + "، " + Workspace.AccountName;
    }

    public sealed class PermissionItem : ObservableObject
    {
        private bool _granted;
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Granted { get => _granted; set => SetProperty(ref _granted, value); }
    }

    public sealed class AccountItem
    {
        public AccountSummary Summary { get; set; }
        public string Username => Summary.Username;
        public string DisplayName => Summary.DisplayName;
        public string Role => Strings.RoleName(Summary.Role);
        public string Status => Summary.Status == AccountStatus.Disabled ? Strings.StatusDisabled
            : !Summary.HasPassword ? Strings.StatusNeedsReset : Summary.MustChangePassword ? Strings.StatusMustChange : Strings.StatusActive;
        public bool IsDelegate => Summary.Role == AccountRole.Delegate;
    }

    public sealed class AccountsViewModel : PageViewModel
    {
        private AccountItem _selected;
        private string _newUsername = "", _newDisplayName = "";
        private bool _deleteArmed;

        public AccountsViewModel(Shell shell) : base(shell)
        {
            SavePermissionsCommand = new RelayCommand(SavePermissions, () => Selected?.IsDelegate == true);
            DisableCommand = new RelayCommand(() => SetStatus(AccountStatus.Disabled), () => Selected != null && Selected.Summary.Status == AccountStatus.Active);
            EnableCommand = new RelayCommand(() => SetStatus(AccountStatus.Active), () => Selected != null && Selected.Summary.Status == AccountStatus.Disabled);
            DeleteCommand = new RelayCommand(Delete, () => Selected != null);
            Reload();
        }

        public override string Title => Strings.AccountsTitle;
        public ObservableCollection<AccountItem> Accounts { get; } = new ObservableCollection<AccountItem>();
        public ObservableCollection<PermissionItem> PermissionItems { get; } = new ObservableCollection<PermissionItem>();
        public string NewUsername { get => _newUsername; set => SetProperty(ref _newUsername, value); }
        public string NewDisplayName { get => _newDisplayName; set => SetProperty(ref _newDisplayName, value); }
        public RelayCommand SavePermissionsCommand { get; }
        public RelayCommand DisableCommand { get; }
        public RelayCommand EnableCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public bool HasSelection => Selected != null;
        public bool SelectedIsDelegate => Selected?.IsDelegate == true;

        public AccountItem Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value)) return;
                _deleteArmed = false;
                PermissionItems.Clear();
                if (value != null && value.IsDelegate)
                    foreach (var p in ParvNegar.Application.Security.Permissions.Grantable)
                        PermissionItems.Add(new PermissionItem { Id = p, Name = Strings.PermissionName(p), Granted = value.Summary.Permissions.Contains(p) });
                OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(SelectedIsDelegate));
                SavePermissionsCommand.NotifyCanExecuteChanged(); DisableCommand.NotifyCanExecuteChanged(); EnableCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
            }
        }

        public void Reload(Guid? select = null)
        {
            try
            {
                var keep = select ?? Selected?.Summary.Id;
                Accounts.Clear();
                foreach (var a in Sm.ListAccounts(Sm.Token)) Accounts.Add(new AccountItem { Summary = a });
                Selected = Accounts.FirstOrDefault(a => a.Summary.Id == keep);
            }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.DelegateCannotSeeAccounts); }
        }

        public void Create(string tempPassword, string repeat)
        {
            if (Usernames.Canonicalize(NewUsername) == null) { Error(Strings.UsernameRule); return; }
            if (!string.Equals(tempPassword, repeat, StringComparison.Ordinal)) { Error(Strings.PasswordsDiffer); return; }
            try
            {
                var r = Sm.CreateDelegate(Sm.Token, NewUsername, NewDisplayName, tempPassword, out var id);
                if (r.Result == AuthResult.Failed) { Error(Strings.UsernameTaken); return; }
                if (!Report(r)) return;
                NewUsername = ""; NewDisplayName = "";
                Reload(id);
                Success(Strings.AccountCreated);
            }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        public void ResetPassword(string tempPassword, string repeat)
        {
            if (Selected == null) { Error(Strings.SelectAccount); return; }
            if (!string.Equals(tempPassword, repeat, StringComparison.Ordinal)) { Error(Strings.PasswordsDiffer); return; }
            try { if (Report(Sm.ResetDelegatePassword(Sm.Token, Selected.Summary.Id, tempPassword))) { Reload(); Success(Strings.Saved); } }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        private void SavePermissions()
        {
            try
            {
                Sm.SetPermissions(Sm.Token, Selected.Summary.Id, PermissionItems.Where(p => p.Granted).Select(p => p.Id));
                Reload(); Success(Strings.Saved);
            }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        private void SetStatus(AccountStatus s)
        {
            try { Sm.SetAccountStatus(Sm.Token, Selected.Summary.Id, s); Reload(); Success(Strings.Saved); }
            catch (LastOwnerException) { Error(Strings.LastOwner); }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        private void Delete()
        {
            if (!_deleteArmed) { _deleteArmed = true; Error(Strings.ConfirmDelete); return; }
            _deleteArmed = false;
            SetStatus(AccountStatus.Deleted);
        }

        public override void Clear() { Accounts.Clear(); PermissionItems.Clear(); base.Clear(); }
    }

    public sealed class AuditItem
    {
        public string When { get; set; }
        public string Kind { get; set; }
        public string Outcome { get; set; }
        public string Source { get; set; }
    }

    public sealed class SecurityViewModel : PageViewModel
    {
        public SecurityViewModel(Shell shell) : base(shell)
        {
            IsOwner = Sm.CurrentAccount?.Role == AccountRole.Owner;
            RotateCommand = new RelayCommand(Rotate, () => IsOwner);
            if (IsOwner) LoadAudit();
        }

        public override string Title => Strings.SecurityTitle;
        public bool IsOwner { get; }
        public ICommand RotateCommand { get; }
        public ObservableCollection<AuditItem> Audit { get; } = new ObservableCollection<AuditItem>();
        public string AuditStatus { get; private set; }
        public bool AuditIntact { get; private set; }

        private void LoadAudit()
        {
            try
            {
                var v = Sm.VerifyAudit(Sm.Token);
                AuditIntact = v.Intact;
                AuditStatus = v.Intact ? Strings.AuditIntact : Strings.AuditBroken;
                foreach (var e in Sm.ReadAudit(Sm.Token, 200))
                    Audit.Add(new AuditItem
                    {
                        When = PersianText.ToPersianDigits(e.Utc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)),
                        Kind = Strings.AuditKindName(e.Kind), Outcome = e.Outcome, Source = e.Source == "pre-unlock" ? "پیش از ورود" : "",
                    });
            }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        public void ChangePassword(string current, string password, string repeat)
        {
            if (!string.Equals(password, repeat, StringComparison.Ordinal)) { Error(Strings.PasswordsDiffer); return; }
            if (Report(Sm.ChangePassword(current, password))) Success(Strings.PasswordChanged);
        }

        private void Rotate()
        {
            try { var r = Sm.RotateRecoveryKey(Sm.Token); Shell.ShowRecoveryKey(r.RecoveryKeyCode); }
            catch (Exception ex) when (ex is UnauthorizedOperationException || ex is SessionExpiredException) { Error(Strings.NotAllowed); }
        }

        public override void Clear() { Audit.Clear(); base.Clear(); }
    }

    public sealed class SettingsViewModel : PageViewModel
    {
        public SettingsViewModel(Shell shell) : base(shell) { }
        public override string Title => Strings.SettingsTitle;

        public bool IsDark
        {
            get => ThemeManager.IsDark;
            set
            {
                ThemeManager.Apply(value ? "dark" : "light");
                Shell.Settings.Theme = ThemeManager.Current;
                Shell.Settings.Save();
                OnPropertyChanged(); OnPropertyChanged(nameof(IsLight));
                Success(Strings.ThemeSaved);
            }
        }

        public bool IsLight { get => !IsDark; set => IsDark = !value; }
    }
}
