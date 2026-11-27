using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ParvNegar.App.Services;
using ParvNegar.App.ViewModels;
using ParvNegar.App.Views;
using ParvNegar.Application.Security;

namespace ParvNegar.App
{
    /// <summary>
    /// UI evidence flow: "ParvNegar.exe --smoke &lt;dir&gt; --flow". Requires &lt;dir&gt;\license.pnlic,
    /// owner.pnprov and bootstrap.txt prepared by tools/Run-UiSmoke.ps1 for the synthetic data root
    /// (&lt;dir&gt;\synthetic-root, created by "--smoke &lt;dir&gt; --prepare"). It drives the same view models a user
    /// operates, through the real license, provisioning and login gates, and captures every screen in the
    /// Light and Dark themes. Synthetic credentials only; never the real data folder.
    /// </summary>
    internal static class SmokeFlow
    {
        // Random per run: no credential is compiled into the product binary.
        private static readonly string SyntheticOwnerPassword = RandomPhrase();
        private static readonly string SyntheticDelegatePassword = RandomPhrase();
        private static readonly string SyntheticDelegateOwnPassword = RandomPhrase();

        private static string RandomPhrase() => "smoke " + ParvNegar.Security.Bytes.ToHex(ParvNegar.Security.CryptoRandom.Bytes(12));

        public static bool IsFlow(string[] args) => args.Any(a => a == "--flow");
        public static bool IsPrepare(string[] args) => args.Any(a => a == "--prepare");

        /// <summary>Creates the synthetic root's activation request and copies it to &lt;dir&gt;\request.pnreq.</summary>
        public static int Prepare(SessionManager sm, string dir)
        {
            var bytes = sm.GenerateActivationRequest(out _);
            File.WriteAllBytes(Path.Combine(dir, "request.pnreq"), bytes);
            return 0;
        }

        public static void Run(System.Windows.Application app, MainWindow main, MainViewModel vm, string dir)
        {
            var report = new StringBuilder();
            int failures = 0;
            var shell = vm.Shell;
            void Step(string name, Action action, Window target = null)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    action?.Invoke();
                    var actionMs = sw.ElapsedMilliseconds;
                    Pump();
                    foreach (var theme in new[] { "light", "dark" })
                    {
                        ThemeManager.Apply(theme);
                        Pump();
                        SmokeRunner.CaptureWindow(target ?? main, Path.Combine(dir, "flow", name + "-" + theme + ".png"));
                    }
                    ThemeManager.Apply("light");
                    var page = shell.Page;
                    report.AppendLine("step\t" + name + "\tOK\t" + (page == null ? "home" : page.GetType().Name) + "\taction_ms=" + actionMs + (page?.HasError == true ? "\tmessage: error shown" : ""));
                }
                catch (Exception ex)
                {
                    failures++;
                    report.AppendLine("step\t" + name + "\tFAIL\t" + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' '));
                }
            }

            main.ContentRendered += (s, e) => main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                Directory.CreateDirectory(Path.Combine(dir, "flow"));
                main.WindowState = WindowState.Normal;
                main.Width = 1100; main.Height = 760;
                WindowFit.ClampToWorkArea(main);
                main.Left = SystemParameters.WorkArea.Left + 10; main.Top = SystemParameters.WorkArea.Top + 10;

                var sm = shell.Security;
                Step("01-home", null);
                Step("02-license-required", () => vm.StartCommand.Execute(null));
                Step("03-license-valid", () => ((LicensePageViewModel)shell.Page).ImportBytes(File.ReadAllBytes(Path.Combine(dir, "license.pnlic"))));
                Step("04-provisioning", () => ((LicensePageViewModel)shell.Page).ContinueCommand.Execute(null));
                Step("05-setup", () => ((ProvisioningPageViewModel)shell.Page).ImportBytes(File.ReadAllBytes(Path.Combine(dir, "owner.pnprov"))));
                Step("06-setup-weak-password", () => ((SetupPageViewModel)shell.Page).Submit(File.ReadAllText(Path.Combine(dir, "bootstrap.txt")).Trim(), "password12345678", "password12345678"));
                Step("07-recovery-key", () => ((SetupPageViewModel)shell.Page).Submit(File.ReadAllText(Path.Combine(dir, "bootstrap.txt")).Trim(), SyntheticOwnerPassword, SyntheticOwnerPassword));
                Step("08-recovery-key-wrong-confirmation", () => ((RecoveryKeyPageViewModel)shell.Page).Confirm("XXXXX"));
                Step("09-workspace", () => { var p = (RecoveryKeyPageViewModel)shell.Page; p.Confirm(p.Code.Split('-').Last()); });
                Step("10-accounts", () =>
                {
                    var ws = (WorkspacePageViewModel)shell.Page;
                    ws.AccountsCommand.Execute(null);
                    var acc = (AccountsViewModel)ws.Section;
                    acc.NewUsername = "deputy.one"; acc.NewDisplayName = "معاون آموزشی (نمونه)";
                    acc.Create(SyntheticDelegatePassword, SyntheticDelegatePassword);
                    foreach (var p in acc.PermissionItems.Take(3)) p.Granted = true;
                });
                Step("11-security-audit", () => ((WorkspacePageViewModel)shell.Page).SecurityCommand.Execute(null));
                Step("12-settings", () => ((WorkspacePageViewModel)shell.Page).SettingsCommand.Execute(null));
                Step("13-license-info", () => ((WorkspacePageViewModel)shell.Page).LicenseCommand.Execute(null));
                AboutWindow about = null;
                try { about = new AboutWindow(vm) { Owner = main }; about.Show(); Pump(); if (about != null) { SmokeRunner.CaptureWindow(about, Path.Combine(dir, "flow", "14-about-window-light.png")); ThemeManager.Apply("dark"); Pump(); SmokeRunner.CaptureWindow(about, Path.Combine(dir, "flow", "14-about-window-dark.png")); ThemeManager.Apply("light"); about.Close(); } }
                catch (Exception ex) { failures++; report.AppendLine("step\t14-about-window\tFAIL\t" + ex.Message); }
                Step("15-lock-inactivity", () => sm.Lock(LockReason.Inactivity));
                Step("16-login-failed", () => ((LoginPageViewModel)shell.Page).Submit("not the right password at all"));
                Step("17-delegate-must-change-password", () => { var l = (LoginPageViewModel)shell.Page; l.Username = "deputy.one"; l.Submit(SyntheticDelegatePassword); });
                Step("18-delegate-workspace", () => ((ChangePasswordPageViewModel)shell.Page).Submit(SyntheticDelegatePassword, SyntheticDelegateOwnPassword, SyntheticDelegateOwnPassword));
                Step("19-logout-login", () => ((WorkspacePageViewModel)shell.Page).LogoutCommand.Execute(null));
                Step("20-owner-recovery", () => ((LoginPageViewModel)shell.Page).RecoverCommand.Execute(null));

                // Evidence that protected data is gone after lock.
                report.AppendLine("after_lock_session\t" + sm.HasSession);
                report.AppendLine("process\t" + (Environment.Is64BitProcess ? "x64" : "x86") + "\t" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                report.AppendLine("peak_private_bytes\t" + SmokeRunner.PeakPrivateBytes());
                report.AppendLine("result\t" + (failures == 0 ? "PASS" : "FAIL"));
                File.WriteAllText(Path.Combine(dir, "ui-flow.tsv"), report.ToString(), new UTF8Encoding(false));
                app.Shutdown(failures == 0 ? 0 : 3);
            }));
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            var t = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) => { t.Stop(); frame.Continue = false; };
            t.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
