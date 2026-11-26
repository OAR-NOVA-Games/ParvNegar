# ADR-P1-05 — WPF shell, font, theme resources

Status: Accepted (2026-11-26). Owner decision: Light theme only for now.

Decision:
- Standard Windows window chrome (min/max/restore/close); `FlowDirection=RightToLeft` at window level.
- Embedded Vazirmatn v33.003 (SIL OFL 1.1, license shipped as `licenses/Vazirmatn-OFL.txt`).
- Brand palette from the approved identity board: navy #14233D, teal #13A9A6, gold #D5A857, ivory #F6F7F4.
- All colors are `DynamicResource` keys in `Themes/Light.xaml`, so a `Dark.xaml` with the same keys can be swapped without structural changes.
- The home title is read from `SchoolProfile` (XML config in Phase 1; database later), never from XAML.
- Persian strings live in one `Strings` class.
- Smoke mode `--smoke <dir>` records startup time, peak private bytes and real screen captures.

Note: the official OAR NOVA publisher mark is not in the corrected brand kit, so the publisher is shown as text only. The school logo comes from the source archive and is not committed; adding it is an owner decision (it is a school configuration asset).
