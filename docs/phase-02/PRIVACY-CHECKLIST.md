# Phase 2 — Privacy checklist

| # | Item | Status |
|---|---|---|
| 1 | No real student records, photos, national IDs, contacts, source archives or school database copies in Git, CI, screenshots, fixtures or reports | Met. Hygiene gate passes. All fixtures are synthetic; national-ID-like numbers are blocked by the gate |
| 2 | No credentials, private keys or recovery material in Git | Met. Isolation gate checks key-like files and private key blocks. The development key is only in ignored `.local/`; test keys are ephemeral |
| 3 | No compiled credentials in the product | Met after fix: smoke passwords are random per run (TEST-EVIDENCE §5 #8) |
| 4 | Activation requests and licenses contain no school data, secrets or raw hardware IDs | Met (tested) |
| 5 | Audit and journal contain no passwords, keys, codes, raw hardware IDs or student data | Met (tested) |
| 6 | Screenshots are synthetic: "owner.synthetic", "deputy.one", "معاون آموزشی (نمونه)", synthetic license `ui-smoke-synthetic` | Met. The recovery-key and license-ID values shown belong to a throw-away synthetic installation |
| 7 | Screen captures cannot record other applications | Met. Capture is refused unless a 9×9 grid inside the window belongs to ParvNegar and the window is fully on its monitor; 2-px edge inset |
| 8 | Approved school and publisher logos only | Met. Owner originals, SHA-256 verified against the supplied zip, not recolored |
| 9 | The original source archive was not opened, modified or quoted in Phase 2 | Met |
| 10 | User settings store only the theme and an optional username | Met |
| 11 | Data folder protected from other standard users | Met for newly created folders (protected ACL). The group policy for shared PCs is O-ACL |
| 12 | Repository visibility | **Public** (owner decision 2026-11-27). Everything committed must be fit for public view; the gates enforce no secrets and no data. The owner may make it private at any time |
| 13 | Pre-push history scan | Hygiene and isolation gates run on the working tree. Key-like files and private key blocks are searched over the tracked tree with `git grep`; history was scanned in Phase 1. Phase 2 commits passed the gates before each commit |
