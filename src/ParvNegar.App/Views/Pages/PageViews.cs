using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ParvNegar.App.ViewModels;

namespace ParvNegar.App.Views.Pages
{
    // Code-behind only moves secrets from PasswordBox/TextBox straight into one call and clears the
    // controls afterwards; secrets are never stored in view-model properties.

    public partial class LicenseView : UserControl { public LicenseView() { InitializeComponent(); } }
    public partial class ProvisioningView : UserControl { public ProvisioningView() { InitializeComponent(); } }
    public partial class InvalidInstallationView : UserControl { public InvalidInstallationView() { InitializeComponent(); } }
    public partial class WorkspaceView : UserControl { public WorkspaceView() { InitializeComponent(); } }
    public partial class DashboardView : UserControl { public DashboardView() { InitializeComponent(); } }
    public partial class SettingsView : UserControl { public SettingsView() { InitializeComponent(); } }

    public partial class SetupView : UserControl
    {
        public SetupView() { InitializeComponent(); PageHelpers.FocusFirst(this); }

        private void OnSubmit(object sender, RoutedEventArgs e)
        {
            ((SetupPageViewModel)DataContext).Submit(CodeBox.Text, Pw1.Password, Pw2.Password);
            PageHelpers.Clear(Pw1, Pw2);
            CodeBox.Clear();
        }
    }

    public partial class RecoveryKeyView : UserControl
    {
        public RecoveryKeyView() { InitializeComponent(); }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            ((RecoveryKeyPageViewModel)DataContext).Confirm(LastGroup.Text);
            LastGroup.Clear();
        }
    }

    public partial class LoginView : UserControl
    {
        public LoginView()
        {
            InitializeComponent();
            Loaded += (s, e) => { if (string.IsNullOrEmpty(UserBox.Text)) UserBox.Focus(); else PwBox.Focus(); };
        }

        private void OnSignIn(object sender, RoutedEventArgs e)
        {
            ((LoginPageViewModel)DataContext).Submit(PwBox.Password);
            PwBox.Clear();
        }
    }

    public partial class ChangePasswordView : UserControl
    {
        public ChangePasswordView() { InitializeComponent(); PageHelpers.FocusFirst(this); }

        private void OnSubmit(object sender, RoutedEventArgs e)
        {
            ((ChangePasswordPageViewModel)DataContext).Submit(Cur.Password, Pw1.Password, Pw2.Password);
            PageHelpers.Clear(Cur, Pw1, Pw2);
        }
    }

    public partial class RecoveryView : UserControl
    {
        public RecoveryView() { InitializeComponent(); PageHelpers.FocusFirst(this); }

        private void OnSubmit(object sender, RoutedEventArgs e)
        {
            ((RecoveryPageViewModel)DataContext).Submit(KeyBox.Text, Pw1.Password, Pw2.Password);
            PageHelpers.Clear(Pw1, Pw2);
            KeyBox.Clear();
        }
    }

    public partial class AccountsView : UserControl
    {
        public AccountsView() { InitializeComponent(); }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            ((AccountsViewModel)DataContext).Create(NewPw1.Password, NewPw2.Password);
            PageHelpers.Clear(NewPw1, NewPw2);
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            ((AccountsViewModel)DataContext).ResetPassword(ResetPw1.Password, ResetPw2.Password);
            PageHelpers.Clear(ResetPw1, ResetPw2);
        }
    }

    public partial class SecurityView : UserControl
    {
        public SecurityView() { InitializeComponent(); }

        private void OnChangePassword(object sender, RoutedEventArgs e)
        {
            ((SecurityViewModel)DataContext).ChangePassword(Cur.Password, Pw1.Password, Pw2.Password);
            PageHelpers.Clear(Cur, Pw1, Pw2);
        }
    }

    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
