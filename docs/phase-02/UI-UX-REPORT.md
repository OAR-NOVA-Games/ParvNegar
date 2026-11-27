# Phase 2 — UI/UX report

## 1. Screens delivered (Persian, RTL)

| Screen | View | Gate |
|---|---|---|
| Splash (2 s, OAR NOVA mark) | `SplashWindow` | — |
| Home (school logo, exact title, Start/About/Exit) | `MainWindow` | None (no data) |
| License status, activation request, license import | `LicenseView` | — |
| Provisioning import | `ProvisioningView` | Valid license |
| First-run owner setup (bootstrap code and new password) | `SetupView` | License + verified provisioning |
| Recovery key display and confirmation, new key | `RecoveryKeyView` | Session |
| Login / session lock (with reason) | `LoginView` | License + installation |
| Mandatory and voluntary password change | `ChangePasswordView`, `SecurityView` | Session |
| Owner recovery (same device / re-binding) | `RecoveryView` | License |
| Workspace navigation, account name and role, lock, logout | `WorkspaceView`, `DashboardView` | Session |
| Account management, delegate creation, permission assignment, disable/enable/delete, temporary password | `AccountsView` | Owner |
| Security: password, recovery-key rotation, audit log with integrity status | `SecurityView` | Session; audit and rotation for the owner |
| Settings: theme | `SettingsView` | Session |
| License info (inside the workspace) | `LicenseView` | Session |
| About (product, publisher mark, distribution, school, offline note, font licence) | `AboutWindow` | — |
| Invalid installation | `InvalidInstallationView` | — |

Technical exceptions are never shown. Every result maps to a Persian message in `ViewModels/Strings.cs`.

## 2. Terminology (consistent)

| Persian | Meaning |
|---|---|
| مجوز | License |
| فایل درخواست فعال‌سازی | Activation request |
| فایل راه‌اندازی | Provisioning file |
| کد یک‌بارمصرف | Bootstrap code |
| مالک | Owner |
| مدیر واگذارشده | Delegated administrator |
| دسترسی | Permission |
| کلید بازیابی | Recovery key |
| نشست قفل شد | Session locked |
| رمز عبور موقت | Temporary password |
| رویدادهای امنیتی | Security events |

## 3. Screenshots (`screenshots/`)

- **Content:** 40 PNGs, 20 states × Light and Dark, with synthetic data.
- **Source:** Modern x64 at 125% DPI on 1920×1080, window 1100×760 DIP. Captured from the real window by `tools/Run-UiSmoke.ps1`. Legacy x86 produced the same 40-screen set (not committed, to keep the repository small).
- **Naming:** `NN-<state>-<light|dark>.png`.

| # | State |
|---|---|
| 01 | Home |
| 02 | License required |
| 03 | License valid |
| 04 | Provisioning |
| 05 | Setup |
| 06 | Setup with a rejected common password (error message) |
| 07 | Recovery key shown once |
| 08 | Wrong confirmation (error) |
| 09 | Workspace |
| 10 | Accounts with a new delegate and permissions |
| 11 | Security and audit |
| 12 | Settings (theme) |
| 13 | License info |
| 14 | About window |
| 15 | Lock after inactivity (reason banner) |
| 16 | Failed login (generic error) |
| 17 | Delegate forced password change |
| 18 | Delegate workspace (no Accounts entry) |
| 19 | After logout |
| 20 | Owner recovery |

## 4. Themes

- **Coverage:** Light and Dark cover every delivered screen and state: normal, disabled, focus and hover buttons; text and password boxes; error, success and warning banners; check boxes and radio buttons; lists; dialogs; logos.
- **Switching:** at runtime without a restart, on both editions. Verified by the UI flow, which switches theme for every capture on Modern and Legacy.
- **Dark title bar:** on Windows 10 1809+ and 11 only.
- **Logos:**
  - school logo: LightSurface variant on Light, DarkSurface variant on Dark;
  - publisher mark: white, always on navy (footer, About, splash);
  - product header: horizontal logo on Light; on Dark, the app icon and the name as text, because the brand kit has no dark product logo (O-DARKLOGO). No asset was recolored.
- **Contrast:** body text tokens are chosen for at least 4.5:1, for example Light `#4A5568` on white is about 7.5:1, and Dark `#B4BFCE` on `#16233A` is about 8:1. This was not measured with a tool on every state.

## 5. Accessibility, resolution and DPI

- **Layout:** the 12-profile DIP matrix passes for the shell. Page cards use a max width with stretching and scroll vertically. The workspace navigation and the account cards wrap.
- **Keyboard:**
  - every page focuses its first input on load;
  - the primary action is the default (Enter);
  - Escape returns home before a session exists, and never leaves a signed-in workspace;
  - the focus ring uses the theme focus colour on buttons and inputs.
  - Not tested: a full manual keyboard-only walkthrough of the new pages.
- **Screen readers:** `AutomationProperties.Name` and `AutomationId` are set on inputs, buttons, messages and logos. NOT tested with Narrator or NVDA.
- **Known UX items:**
  - The recovery-key text box is selectable, and the user is warned not to use the clipboard.
  - Password fields cannot show the password, a deliberate choice; this may be reconsidered for long phrases.
  - Users who type passwords on different Persian/Arabic keyboard layouts must type the same characters (PASSWORD-POLICY §2).
