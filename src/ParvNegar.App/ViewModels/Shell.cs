using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ParvNegar.App.Services;
using ParvNegar.Application.Security;

namespace ParvNegar.App.ViewModels
{
    /// <summary>
    /// Navigation driven by the security state. Protected pages exist only while a session exists: on lock
    /// every page is cleared and replaced by the lock screen, and other windows (About, dialogs) are closed.
    /// The branded home screen shows no data and is the only screen before the license and login gates.
    /// </summary>
    public sealed class Shell : ObservableObject
    {
        private PageViewModel _page;
        private readonly Dispatcher _dispatcher;

        public Shell(SessionManager security, UserSettings settings, IFileDialogs dialogs, Dispatcher dispatcher)
        {
            Security = security; Settings = settings; Dialogs = dialogs; _dispatcher = dispatcher;
            if (security != null) security.Locked += (s, reason) => OnUi(() => ShowLockScreen(reason));
        }

        public SessionManager Security { get; }
        public UserSettings Settings { get; }
        public IFileDialogs Dialogs { get; }

        /// <summary>Null while the home screen is shown.</summary>
        public PageViewModel Page
        {
            get => _page;
            private set
            {
                if (ReferenceEquals(_page, value)) return;
                _page?.Clear();
                SetProperty(ref _page, value);
                OnPropertyChanged(nameof(HasPage));
            }
        }

        public bool HasPage => _page != null;

        public void Home() => Page = null;

        public void Show(PageViewModel page) => Page = page;

        public void ShowRecoveryKey(string code) => Page = new RecoveryKeyPageViewModel(this, code);

        public void NavigateForState()
        {
            Security.Refresh();
            switch (Security.State)
            {
                case AppState.Unlicensed: Page = new LicensePageViewModel(this); break;
                case AppState.LicensedNotInitialized: Page = new ProvisioningPageViewModel(this); break;
                case AppState.ProvisioningRequired: Page = new SetupPageViewModel(this); break;
                case AppState.RecoveryKeyConfirmationRequired: Page = Page as RecoveryKeyPageViewModel ?? new RecoveryKeyPageViewModel(this, null); break;
                case AppState.Locked: Page = new LoginPageViewModel(this, null); break;
                case AppState.PasswordChangeRequired: Page = new ChangePasswordPageViewModel(this, required: true); break;
                case AppState.Authenticated: Page = new WorkspacePageViewModel(this); break;
                case AppState.RecoveryRequired: Page = new RecoveryPageViewModel(this, deviceLost: true); break;
                default: Page = new InvalidInstallationPageViewModel(this); break;
            }
        }

        private void ShowLockScreen(LockReason reason)
        {
            var app = System.Windows.Application.Current;
            if (app != null)
                foreach (var w in app.Windows.OfType<Window>().Where(w => w != app.MainWindow && !(w is Views.SplashWindow)).ToList())
                    w.Close();
            Page = null; // clears the protected page and its data first
            if (reason == LockReason.Logout) { Page = new LoginPageViewModel(this, null); return; }
            Page = new LoginPageViewModel(this, reason);
        }

        private void OnUi(Action a)
        {
            if (_dispatcher == null || _dispatcher.CheckAccess()) a();
            else _dispatcher.BeginInvoke(a);
        }
    }
}
