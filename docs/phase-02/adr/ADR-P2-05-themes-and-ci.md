# ADR-P2-05: Runtime Light/Dark themes; selective CI

Status: Accepted (owner-approved scope, engineering design). Date: 2026-11-27.

## Themes

- **Tokens:** `Themes/Light.xaml` and `Themes/Dark.xaml` define the same semantic tokens: background, surface, text, button, input, error, success, warning, header and footer. Views use `DynamicResource` only, so `ThemeManager.Apply` switches themes at runtime without a restart, on both .NET Framework 4.8 and .NET 10.
- **Preference:** only the theme preference is persisted.
- **System theme:** detection is not implemented, so the result is reliable on Windows 7 and 8.1. The default is Light; the user chooses in Settings.
- **Dark title bar:** uses `DWMWA_USE_IMMERSIVE_DARK_MODE` where Windows supports it.
- **Logos are never recolored:**
  - the school logo switches to the owner-supplied white-lettering variant;
  - the Dark header uses the approved app icon and text, because the brand kit has no dark product logo.

## CI

- **One workflow:** a path classifier with in-repo tests decides between the documentation hygiene gate and the full gate. Unknown paths always run the full gate.
- **Required check:** a single aggregate `ci-result` check.
- **Cost control:** superseded PR runs are cancelled; standard runners only; explicit timeouts; NuGet cache keyed by the lock files.
- **Not in CI:** the full UI evidence flow and the compatibility kit. They need an interactive, uncovered desktop and real older-Windows machines.
