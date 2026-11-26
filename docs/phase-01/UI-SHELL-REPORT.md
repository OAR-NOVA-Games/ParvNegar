# Phase 1 — UI Shell Report

Date: 2026-11-26. Internal development shell only: no login, no license, no student data. SRS: UI-01, UI-02 (partial), UI-03 (partial), SCH-01, AC-04 (partial).

## 1. Screens

| Screen | Content |
|---|---|
| Home | Header: approved horizontal ParvNegar logo at the visual start (right); development notice in gold. Card: teal accent bar; **school home title read from `SchoolProfile`**; product tagline «سامانه مدیریت پرونده‌های دانش‌آموزی» (from the identity board); actions «شروع» (primary), «درباره», «خروج». Footer (navy): school name (start) and version + «محصولی از OAR NOVA Games Studio» (end) |
| Start | Placeholder card: «بخش‌های برنامه در حال آماده‌سازی است» + explanation that login, license and student records are not active; «بازگشت» |
| About (modal) | App icon, product name «پرونگار», tagline, version with Persian digits, publisher, distribution site, school (separate rows), offline statement, Vazirmatn OFL notice, copyright, «بستن» |

## 2. Requirements check

| Requirement | Status | Evidence |
|---|---|---|
| Persian RTL layout | PASS | `FlowDirection=RightToLeft` at window level; caption buttons appear on the left as Windows does for RTL windows (screenshots) |
| Embedded Vazirmatn | PASS | `pack://…/Assets/Fonts/#Vazirmatn`; test asserts the font family; joining and digits correct in screenshots |
| Resource dictionaries | PASS | `Themes/Light.xaml` (colors/brushes), `Themes/Styles.xaml` (text, buttons, card, focus ring) |
| Theme readiness (Light/Dark) | PASS (Light only, per owner) | All colors are `DynamicResource` keys; Dark not implemented |
| Responsive desktop layout | PASS at 800×600, 1024×768, 1366×768 (logical) | Screenshots; the title wraps; the card is centered with a max width; the scroll viewer protects small heights |
| Keyboard navigation | PASS (automated) | Tab order Start → About → Exit (cycle); Start is the default button and gets focus on load; Escape returns from Start; About: Escape/Enter close it, Close button gets focus; visible teal focus ring. Manual walkthrough NOT RUN |
| High DPI | PASS on host at 125% | Manifest PerMonitorV2 with PerMonitor fallback; screenshots are physical pixels at 1.25 scale. 100%/150%/200% NOT RUN |
| Minimize / maximize / restore / close | PASS | Standard window chrome, `ResizeMode=CanResize`, min size 800×600 |
| Start, About, Exit actions | PASS | Commands in `MainViewModel`; tests for Start/Back |
| App icon in executable and window | PASS | `ApplicationIcon` = brand ICO (7 sizes, 32 bpp, transparent corners verified); window and About icons from the same ICO; visible in the title bar in screenshots |
| Navy rounded-square icon with transparent outer corners | PASS | Pixel check: corners alpha 0, center navy #011C46 region |
| Separate publisher, product and school identities | PASS | `ProductIdentity` constants vs `SchoolProfile`; footer and About keep them apart |
| Title from SchoolProfile, not XAML | PASS | `Title_comes_from_profile_not_from_xaml` test; title text bound one-time to the view model |
| 1024×768 provisional profile | PASS (logical layout) | Test checks all buttons fit; screenshot `home-1024x768.png` |
| No real data before authentication | PASS | The shell references neither Storage nor Security; no data access exists |

## 3. Screenshots (synthetic configuration only)

`docs/phase-01/screenshots/`. These are screen captures of the real window on the Windows 11 host at 125% DPI. Modern x64 and Legacy x86 produced byte-identical images (same SHA-256), so one set is committed.

| File | View |
|---|---|
| `home-1024x768.png` | Home at 1024×768 logical |
| `home-1366x768.png` | Home at 1366×768 |
| `home-800x600.png` | Home at the minimum window size |
| `start-placeholder-1024x768.png` | After «شروع» |
| `about.png` | About dialog |

## 4. Defects found and fixed

| Defect | Fix |
|---|---|
| Logo and school name docked at the visual end in RTL | Dock sides swapped (in RTL, Dock=Left is the visual right) |
| About: "1.1" and copyright parts re-ordered by bidi | Persian wording restructured so numbers and Latin names keep a stable order |
| Screenshots mirrored (capture method) | Screen-pixel capture (TEST-EVIDENCE §6) |

## 5. Open UI items (owner)

- Official Persian school name (currently taken from the approved title wording) and whether the school logo appears on Home.
- The OAR NOVA publisher mark is not in the corrected brand kit; the publisher is text only until a mark is supplied.
- Dark theme (not approved), window policy (normal window chosen), accessibility targets (NFR-04).
