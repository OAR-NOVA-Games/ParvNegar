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
| Responsive desktop layout | PASS for the 12-profile matrix (follow-up 2026-11-27) | 1024×768, 1366×768, 1920×1080 at 100/125/150/200% as DIP work areas: every key control inside the window, reachable by scrolling, header and footer parts not overlapping (`LayoutProbe`, automated test and shell smoke). Windows clamp to the work area; buttons wrap; header notice and footer school name wrap |
| Keyboard navigation | PASS (automated) | Tab order Start → About → Exit (cycle); Start is the default button and gets focus on load; Escape returns from Start; About: Escape/Enter close it, Close button gets focus; visible teal focus ring. Manual walkthrough NOT RUN |
| High DPI | Rendering PASS on host at 125%; layout PASS at 100/125/150/200% (DIP simulation) | Manifest PerMonitorV2 with PerMonitor fallback; screenshots are physical pixels at 1.25 scale. Real rendering at 100%/150%/200% and on real 1024×768 / 1366×768 displays NOT RUN |
| Minimize / maximize / restore / close | PASS | Standard window chrome, `ResizeMode=CanResize`, min size 800×600 |
| Start, About, Exit actions | PASS | Commands in `MainViewModel`; tests for Start/Back |
| App icon in executable and window | PASS | `ApplicationIcon` = brand ICO (7 sizes, 32 bpp, transparent corners verified); window and About icons from the same ICO; visible in the title bar in screenshots |
| Navy rounded-square icon with transparent outer corners | PASS | Pixel check: corners alpha 0, center navy #011C46 region |
| Separate publisher, product and school identities | PASS | `ProductIdentity` constants vs `SchoolProfile`; footer and About keep them apart |
| Title from SchoolProfile, not XAML | PASS | `Title_comes_from_profile_not_from_xaml` test; title text bound one-time to the view model |
| 1024×768 provisional profile | PASS (layout, simulated work area) | 1024×768 at 125% leaves 819×566 DIP; screenshot `home-1024x768-at125.png` is that window size |
| Startup splash (owner request 2026-11-27) | PASS | OAR NOVA mark on brand navy, visible ≥ 2 s (measured 2,083–2,145 ms on all four builds); `splash.png` |
| No real data before authentication | PASS | The shell references neither Storage nor Security; no data access exists |

## 3. Screenshots (synthetic configuration only)

`docs/phase-01/screenshots/` (replaced in the 2026-11-27 follow-up). These are screen captures of the real window on the Windows 11 host: 1920×1080 physical pixels at 125% DPI. Each `…-<W>x<H>-at125` image is the window sized to the work area a maximized window would get on a W×H display at 125% (W/1.25 × H/1.25 − 48 DIP taskbar). Images are physical pixels, so `home-1024x768-at125.png` is 1008×700 px. All four builds produced byte-identical images, except `home-1920x1080-at125.png`, which differed only by the mouse hover on the close button; the Legacy x64 copy is committed. The red close button in some images is that hover state.

| File | View |
|---|---|
| `splash.png` | Startup splash, OAR NOVA mark (420×420 DIP) |
| `home-1024x768-at125.png` | Home, 819×566 DIP (1024×768 display at 125%) |
| `home-1366x768-at125.png` | Home, 1092×566 DIP |
| `home-1920x1080-at125.png` | Home, 1536×816 DIP (this host's full work area) |
| `start-1024x768-at125.png` | After «شروع», 819×566 DIP |
| `about.png` | About dialog (520×586 DIP) |

Phase 1 images named `home-1024x768.png` and similar showed a 1024×768 **DIP** window. At 125% that is 1280×960 physical pixels, larger than a 1024×768 display, so the names overstated what they proved. The default 1100×720 DIP window did not fit a 1024×768 display even at 100%. Both issues are fixed in the follow-up (PHASE-01-REVIEW-FOLLOWUP.md §3.3).

## 4. Defects found and fixed

| Defect | Fix |
|---|---|
| Logo and school name docked at the visual end in RTL | Dock sides swapped (in RTL, Dock=Left is the visual right) |
| About: "1.1" and copyright parts re-ordered by bidi | Persian wording restructured so numbers and Latin names keep a stable order |
| Screenshots mirrored (capture method) | Screen-pixel capture (TEST-EVIDENCE §6) |
| Window larger than small displays; minimum size larger than the work area at 125% (follow-up) | `WindowFit` clamps size, minimum and maximum to the work area; placeholder and About scroll; buttons wrap |
| Screen capture could record another application's window (follow-up, privacy) | Capture refused unless a 9×9 point grid inside the rectangle belongs to the ParvNegar window; capture refused when the window extends beyond its monitor |

## 5. Open UI items (owner)

- Official Persian school name (currently taken from the approved title wording) and whether the school logo appears on Home.
- The OAR NOVA publisher mark was supplied on 2026-11-26 and is used on the startup splash (`assets/publisher/`). Placement in About and the footer is Phase 2D work.
- School logos were supplied in the same zip; inclusion awaits owner confirmation of the formal school display name.
- Dark theme (not approved), window policy (normal window chosen), accessibility targets (NFR-04).
