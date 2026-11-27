# ParvNegar | پرونگار
## Software Requirements Specification (SRS)

**Version:** 1.0 — Owner-approved product baseline  
**Date:** 2026-11-25  
**Publisher:** OAR NOVA Games Studio — https://oarnova.games  
**Distribution channel:** https://yarizan.oarnova.games  
**Initial deployment:** Shahid Shahideh Shahnaz Karimi Girls' Elementary School, Marivan  
**Product language:** Persian (fa-IR), RTL  
**Status:** Product baseline approved by owner on 2026-11-25; Phase 0 engineering discovery authorized as the next step. Implementation, release and publishing remain separately gated. Specific Open items are not implicitly resolved.

## 1. Purpose, sources and scope

ParvNegar is a completely offline Windows application for maintaining structured educational and pastoral records and protected documents for students in a school-defined target population. It must also support registering any student; eligibility is not hard-coded. The first edition serves one girls' elementary school; the product architecture must permit later distribution to other schools without a code fork.

Source baseline: owner decisions in the design discussion; `شاهد.rar`, including `نرم افزار.pptx` (slides 1–7), four sample student DOCX files, `فرم استعداد .doc`, and school logos. Slide 1 provides home navigation; slides 2–4 grade navigation and student management; slide 5 student profile and sections; slides 6–7 assessment and talent forms. The student DOCX files define the two existing form layouts and the talent form defines six activity categories. Owner decisions supersede prototype details where explicitly stated. Existing forms are digitized, not embedded as Word editors. Real student data in the source archive is confidential; do not distribute, seed, log, commit, or use it in screenshots or automated tests.

The exact owner-approved home-page title for this school is:

> ثبت شاخص های آموزشی و پرورشی دانش آموزان جامعه هدف مدرسه شاهد شهیده شهناز کریمی

Store this exact title as school-specific configuration, distinct from the product name `پرونگار` / `ParvNegar`. Do not change the spelling or insert additional words into the title without approval. School profile separately identifies the girls' school. The publisher and product identity remain independent of school identity.

Requirement status: **Confirmed** = owner decision or explicit source requirement; **Proposed** = design/default requiring review; **Open** = must be resolved before affected work. Owner approved the SRS baseline and accepted the product name for commercial purposes on 2026-11-25. This does not establish registered trademark clearance or resolve unspecified policies. Proposed extensions must not be treated as approved business policy. The owner explicitly requires Windows 7 onward and both x86 and x64, with Windows 10/11 primary and older systems a mandatory compatibility target.

### 1.1 Scope boundaries
- In scope: local authentication, device licensing, school branding, academic years, grades/classes, student records, educational assessments, pastoral/talent forms, attachments, search, printing/PDF, backup/restore, account administration, deployment.
- Out of initial scope: online synchronization, public student/parent portal, cloud database, multi-school shared installation, attendance, tuition, payroll, messaging, timetable, government integrations, automated educational judgments, and changes to YariZan production systems.
- No internet is required at installation/use, activation, login, printing, backup, recovery or normal operation. Distribution may occur via the website on another connected machine. Website integration is a distinct future release task.

## 2. Actors and authorization

| Actor | Responsibility |
|---|---|
| Publisher/license issuer | Issue a signed device license and initial-owner provisioning artifact offline; never embed signing private keys in distributed binaries. |
| School owner | Initial unique buyer account; manages additional administrators and protected records. |
| Delegated administrator | School-authorized user on the licensed device. |
| Windows user | OS identity; not automatically a ParvNegar administrator. |

**AUTH-01 (Confirmed):** Before exposing any student data, require both a valid device license and successful application login. The publisher preprovisions a unique initial owner account per sale, without a shared default password or hidden master password.

**AUTH-02 (Confirmed):** Owner can create additional local administrators, e.g. deputies. All accounts are confined to the licensed installation. No online authentication dependency.

**AUTH-03 (Proposed):** Owner can activate/deactivate accounts, reset delegated passwords, change own password and manage explicit permissions: read/edit records, attachments, export, delete/restore, academic-year closure/promotion, backup/restore, account management. Only Owner manages accounts and license/recovery settings by default. The last active Owner cannot be disabled or removed. No anonymous route or alternate UI bypass.

**AUTH-04 (Proposed):** Use a vetted salted adaptive password KDF, persistent throttling, safe password entry, no password logging, session lock on inactivity and on Windows lock, and explicit logout. Recovery must be offline and authenticated; define owner recovery before shipping. Do not implement an undocumented bypass.

**LIC-01 (Confirmed):** The application runs only on the licensed computer. Authentication and licensing are separate controls. License validity is checked offline; invalid/missing/mismatched licenses fail closed.

**LIC-02 (Proposed):** Signed license artifact contains license ID, installation/device binding, product/edition and authorized policy. Use asymmetric signatures, embedded verification public key, and a separate publisher-only signing tool. A challenge/request file may be transferred to publisher on a different machine. Avoid storing private issuer keys or universal credentials in the customer installation. Bind using a documented hardware/OS strategy tolerant of approved maintenance. Do not assert unbreakable DRM; an offline local administrator can tamper with software.

**LIC-03 (Open):** Perpetual vs term, hardware change tolerance, replacement/reissue policy, owner-password recovery process and commercial fulfillment mechanism. No clock-based expiry without an approved offline time/tamper policy. A restored data backup must not grant a new device a license.

## 3. Navigation and UI

**UI-01 (Confirmed):** Persian RTL, desktop-first native Windows UI. Use final approved ParvNegar brand assets, official OAR NOVA publisher mark, and school logo independently. The configured school title must appear verbatim on home.

**UI-02 (Confirmed):** Home has Start, About, Exit and window controls corresponding to the prototype. Navigation: license/setup as needed → login → home → academic year → grade → student list (class filter) → student profile. Grades 1–6 available. Back navigation retains filters, selection and scroll where practical. No forced full-screen requirement; support maximize, restore, minimize and close.

**UI-03 (Proposed):** Home may show selected year, basic aggregate counts and quick search; no student PII visible before authentication. Main window has consistent breadcrumb, selected year and authenticated user. Forms support keyboard navigation, validation messages in Persian, accessible contrast, DPI scaling and unsaved-change confirmation. Use light/dark themes only if both can meet usability criteria; theme requirement is open. Do not copy the prototype's colors as a mandate.

**UI-04 (Confirmed):** Sections: `آموزشی` (subsections `کارنامه`, `کلاس تقویتی`), `پرورشی`, `مشاوره`, `دیدار و بازدید`, `سایر`. Profile form and structured educational/pastoral forms are native editable screens, not static screenshots.

## 4. School, academic years, grades and classes

**SCH-01 (Confirmed):** Configurable school name, home title, logo and report identity. First school uses the title in section 1 and supplied school logo. Product icon remains unchanged for each school. One school profile per installation initially; additional installations must not share a database by default.

**AY-01 (Confirmed):** Each year spans Mehr through Khordad (nine academic months); display Solar Hijri dates and academic-year labels. Years are independently queryable. No hard-coded 1404–1405; sample year is historical source data only.

**AY-02 (Confirmed):** Maintain grade (1–6 initially), class code/name and teacher per year. Create/edit classes and filter student listings by grade/class. Transfer within a year without destroying prior academic records.

**AY-03 (Confirmed):** Explicit owner/admin operation promotes selected students to the following year/grade; prior-year data remains unchanged. One identity per student and one enrollment per year in a school. New year's class assignment is validated. Promotion must be transactional and idempotent: repeating the operation cannot create duplicate enrollment. Grade 6 completion, repeat-grade and withdrawal must not silently promote to an unsupported grade.

**AY-04 (Proposed):** Draft/active/closed academic-year states. Closed year read-only in normal workflows, with any exceptional correction explicitly authorized and audited. New enrollment copies only approved identity/profile defaults; assessments, documents, notes and talent selections remain associated with their source year unless a separate, explicit copy action is approved. Preserve historical name, class, teacher, guardian and relevant school/print metadata in year-specific snapshots. Provide preview and conflict report before bulk promotion.

## 5. Student registry and profile

**STU-01 (Confirmed):** Add, view, edit, search, remove/recover and assign students to classes. A stable internal ID must not depend on national ID or name. Support selected target-group students and other students; no automated eligibility restriction.

**STU-02 (Confirmed source fields):** Full name, national ID, father name, student photograph, class, teacher, `اولویت ایثارگری`, seasonal academic status (fall, winter, spring), father and mother details (name, education, occupation, mobile number), and guardian following educational affairs. Preserve existing structured text sections `ثبت موارد پیگیری در طرح دیدار` and `ثبت دیدار و بازدید` from the supplied profile form. The attachment module is additional to these existing text fields; do not invent a full counseling case-management form.

**STU-03 (Proposed):** Target-group flag or configurable label, active/archived status, optional notes, created/updated timestamps. National ID represented as a string to preserve leading zeroes; normalize Persian/Arabic digits, validate structure and detect duplicate known IDs. Required fields, exceptions for absent IDs and exact target-group options require owner approval. Do not require parent information or photograph when unknown without an approved rule.

**STU-04 (Proposed):** Search Persian names with normalized Yeh/Kaf and digits; support national ID, grade, class and year. Scope access to authorized account. Paginate/virtualize results. Soft deletion with confirmation, recycle bin and restore; distinguish removal from an enrollment vs removing a student identity. Permanent deletion/retention requires explicit policy.

## 6. Educational and pastoral forms

**EDU-01 (Confirmed):** Native editable report-card assessment table, sourced from the Word form and slide 6. Each subject and assessment period has one qualitative level or an unrecorded state; no invented numeric conversion. Labels: `خیلی خوب`، `خوب`، `قابل قبول`، `نیاز به تلاش بیشتر`. Include subject remarks, `وضعیت تربیتی`, `تحلیل عملکرد (ارتقاء، افت)` and teacher/director name and signature/stamp areas in print.

**EDU-02 (Source catalog):** `قرآن`، `هدیه های آسمان`، `فارسی (نوشتاری)`، `فارسی (خوانداری)`، `مطالعات اجتماعی`، `ریاضیات`، `علوم تجربی و بهداشت`، `فناوری`، `تفکر و پژوهش`، `هنر`، `تربیت بدنی و سلامت`. The source is a sample list, not proof that every subject applies to every grade. Proposed: year/grade-specific subject catalogs and non-applicable state. Catalog changes cannot silently alter historical assessment meaning.

**EDU-03 (Open):** Source has two assessment columns: Mehr–Azar and Dey–Esfand. Owner confirms academic year is nine months; profile already has three seasonal fields. Proposed third assessment period Farvardin–Khordad, with configurable periods and compatible print layout. Confirm whether source print must preserve two periods or use a three-period expanded layout, and whether seasonal status and detailed assessments are independently entered or linked. Do not implement automatic aggregation before approval.

**EDU-04 (Confirmed):** Reinforcement class subsection is an attachment category, not a new scheduling system. Report-card subsection supports structured assessment plus related files.

**TAL-01 (Confirmed):** Native checkbox-based talent/activity form, sourced from `فرم استعداد .doc` and slide 7. Preserve independent selections per student/year and six categories. No scoring, ranking or automated inference of ability. Unchecked does not necessarily imply inability; distinguish an uncompleted form from a completed form with no selections.

| Category | Initial source options |
|---|---|
| فرهنگی هنری | تئاتر (بازیگری)، سرود، خطاطی، طراحی، نقاشی، نشریه، کتاب‌داری |
| ورزشی | شنا، والیبال، آمادگی جسمانی، بسکتبال، بدمینتون، شطرنج، برنامه‌ریزی در زمینه ورزش |
| مذهبی | صوت قرآن، برگزاری نماز جماعت، برگزاری جشن‌های مذهبی، صوت اذان، مسابقه احکام، تواشیح، حفظ قرآن |
| علمی | ریاضی، علوم، رباتیک، پژوهشی، کاردستی، مسابقات علمی، مقاله‌نویسی، آموزش کامپیوتر / برنامه‌نویسی |
| تشکیلات دانش‌آموزی | اجرای مراسم‌های صبحگاه، حاملان قرآن، شورای دانش‌آموزی کلاس، سفیر سلامت، اجرای جشنواره‌های مدرسه |
| اردویی | اردوی علمی، اردوی ورزشی، بازدید مراکز پژوهشی و آموزشی، بازدید از کارخانه‌ها و کارگاه‌های حرفه‌ای، بازدید مراکز مذهبی |

Some source numbering/line breaks are irregular (especially scientific items and student organizations); the above is a normalized transcription for owner verification, not a newly approved catalog. Proposed: versioned, configurable activity options, optional note per form, no forced extra categories.

## 7. Protected document management

**DOC-01 (Confirmed):** Attach documents by student, academic year and category: educational/report card, reinforcement class, pastoral, counseling, visits, other. Authorized users can import from Windows file picker, list, open/preview, rename display name, and delete/recover. Files are copied into managed storage, not linked to mutable external paths.

**DOC-02 (Confirmed, supersedes initial Explorer request):** Readable attachments accessible only after successful application authorization. Do not expose plaintext storage paths, provide an Explorer shortcut to decrypted files, or persist ordinary readable files in public folders. Category folders become logical folders within the application. If an Explorer shortcut is later required, it may expose only encrypted blobs and must not be represented as readable document access.

**DOC-03 (Proposed):** Encrypt attachments and student images with authenticated encryption and unique nonces; use opaque filenames, protected metadata and a key-management design with documented backup recovery. A plain SQLite database is insufficient for sensitive metadata. Encrypt database at rest with a vetted compatible provider or an equally reviewed equivalent. Protect keys through OS-backed mechanisms and authenticated application authorization; DPAPI by itself does not enforce isolation from other processes running as the same Windows identity.

**DOC-04 (Proposed):** In-app rendering for approved PDF/image/text formats and safe read-only rendering of supported Office documents without executing macros. No implicit shell execution, executable attachments or unrestricted external viewers. If a format cannot be safely previewed, explain limitations. Explicit export, if approved, is separate from preview and produces a user-controlled plaintext copy. Define file type/size limits and Office fidelity before implementation. Validate actual file type and file names; defend against traversal, malformed/oversize content, decompression bombs, disk exhaustion and interrupted imports. Avoid temporary plaintext on disk wherever feasible.

**DOC-05 (Proposed):** Store file metadata (original name, display name, content type, size, checksum, category, enrollment ID, author, import time and deletion state); use consistent database/file transactions or a recoverable staged-commit journal. Backup and restore cover both metadata and encrypted blobs. Never silently overwrite an existing file.

## 8. Printing and reports

**REP-01 (Confirmed):** Print preview, direct printing and PDF export of the student profile, educational assessment and pastoral/talent forms. Generated from structured records, not dependent on Word installed or internet. Preserve meaningful fields and school-specific headers, year, teacher/director and signature/stamp placeholders from source. Use Persian RTL and embedded or reliably available Persian font with proper license. Proposed A4 layout; verify against physical print and Persian text extraction. No automatic real signature/stamp insertion without approval.

**REP-02 (Proposed):** Provide per-form and combined student packet exports, grade/class student list and summary counts. Any aggregate reports, Excel exports, printable blank forms and exact source-layout fidelity are open business requirements. No PDF export of raw encrypted attachments by default. Warn that printed/PDF output is sensitive and outside app access control; audit successful exports without logging PII.

## 9. Backup and restore

**BKP-01 (Confirmed):** Manual complete backup and restore of data, attachments, school settings and account-recovery-related state. Offline, user-selected destination. Device license and issuer secrets are excluded from portable backups. Restore on replacement hardware requires a valid newly issued license.

**BKP-02 (Proposed):** Encrypted authenticated backup archive with versioned manifest, schema version and integrity checks. Create a consistent database snapshot and corresponding attachments, safely finalize via temporary archive then rename. No plaintext staging archive; use user-held recovery passphrase or equivalent approved portable key envelope. Losing recovery material must be clearly explained; no hidden master key.

**BKP-03 (Proposed):** Verify authentication, archive size/path safety, schema compatibility, hashes and referential consistency before modifying live data. Show summary, warn about overwrite and require Owner authorization. Make a pre-restore backup and stage restore before atomic switch or recoverable rollback. A failed/cancelled restore leaves live records unchanged. Never restore invalid licenses or silently reinstate a disabled user without a defined owner recovery policy.

**BKP-04 (Open):** Automatic backup schedule, retention, encrypted destination, maximum backup size, portable passphrase recovery and account behavior on restore. Recommend optional local scheduling plus removable-drive/off-device backup; no cloud integration by default. Test restoration on a clean licensed installation before release.

## 10. Data model (logical proposal)

| Entity | Responsibility |
|---|---|
| SchoolProfile | Configured title, name, logos, school metadata |
| AcademicYear | Year boundaries, status, school ID |
| Grade / Classroom | Grade catalog, year-specific classroom and teacher assignment |
| Student | Stable identity, minimal current profile |
| Enrollment | Student + school + academic year unique; grade/class, status, historical snapshot |
| GuardianSnapshot / ProfileForm | Year-specific family details, seasonal status, visitation text |
| SubjectCatalog / AssessmentPeriod | Versioned grade/year subject and period definitions |
| Assessment / AssessmentSummary | Unique enrollment/subject/period rating, remarks, behavior and performance text |
| TalentCategory / TalentOption / TalentForm / TalentSelection | Versioned activity definitions and annual student selections |
| Attachment | Enrollment/category, encrypted content reference and metadata |
| LocalAccount / Permission / AccountPermission | Local credential hashes, status and authorization |
| AuditEvent | Actor, timestamp, action, object ID, outcome; minimal sensitive content |
| AppSettings / BackupManifest | Local policy, schema/format versions and backup metadata |

Use foreign keys, uniqueness constraints, transactional writes, migration versioning and explicit deletion policies. License state is distinct from business data. No source student data in schema migrations or fixtures. Storage identifiers are not paths supplied by users. Calendar model preserves unambiguous dates, with Solar Hijri presentation and documented internal representation.

## 11. Technical and non-functional requirements

**NFR-01 (Confirmed):** Windows native desktop, fully offline. Architecture direction: C#, WPF, MVVM, modular application/domain/infrastructure/presentation boundaries and encrypted SQLite-compatible storage. The mandatory legacy compatibility requirement supersedes the earlier universal .NET 10/EF Core suggestion. See section 11.1 for the candidate architecture and proof gates; final runtime, provider and ORM choice is not yet approved. No cloud telemetry, automatic network calls or remote assets in application runtime.

**NFR-02 (Proposed):** Per-installation storage outside binaries, Windows ACLs, least privilege, OS-backed key protection, no secrets in source control, signed build/installer where feasible. Define threat model: unauthorized application user, another Windows user, stolen disk/backup, copied installation, malicious document; privileged OS administrators, memory inspection, screenshots and authorized exports cannot be absolutely prevented. No claim of unbreakable security. Protect data from unintended local access; prefer full-disk encryption as an additional control.

**NFR-03 (Proposed):** Audit authentication, account changes, licensing, data modification/deletion/restore, year close/promotion, export and backup/restore. Minimize audit content; never write passwords, national IDs, guardian phone numbers, document contents or keys to logs. Tamper resistance within offline threat model; avoid promising tamper-proof audit.

**NFR-04 (Proposed):** Responsive UI, background I/O with cancellation, transaction consistency, crash recovery and actionable Persian error messages. Initial measurable acceptance targets (owner review): on agreed reference hardware, test 1,000 synthetic students over six years; filtered search p95 ≤ 500 ms, normal form navigation p95 ≤ 1 s, startup-to-login ≤ 5 s excluding first migration. Large file operations show progress and avoid UI freeze. Confirm data volume, hardware, Windows versions, x64/x86 and accessibility targets.

**NFR-05 (Proposed):** Offline self-contained installer where supported, no unapproved bundled components, upgrades preserve database/attachments/license and back up before migrations, uninstall does not silently remove records. Separate release packaging and signing pipeline. No production site deployment without owner approval. Verify software library licenses and font redistribution rights before commercial shipment.

### 11.1 Platform compatibility amendment (2026-11-25)

**PLAT-01 (Confirmed target, not tested):** Windows 7 SP1 onward, native x86 and x64 builds where the OS supports those architectures; Windows 10/11 are primary. Windows 11 has no x86 OS edition; test x64 OS with the x64 application and x86 application under compatibility execution as appropriate. ARM64 native builds are not in scope. Windows 7 without SP1 is not a candidate baseline; exact prerequisites and Windows 10 build floor must be documented by Phase 0 and reviewed before release.

| OS target | App architectures | Candidate route |
|---|---|---|
| Windows 7 SP1 | x86, x64 | Legacy; .NET Framework 4.8 candidate, prerequisites offline |
| Windows 8 (not 8.1) | x86, x64 | Mandatory feasibility gate; .NET Framework 4.6.1 maximum per Microsoft; do not silently exclude |
| Windows 8.1 Update | x86, x64 | Legacy; .NET Framework 4.8 candidate |
| Windows 10 | x86, x64 | Modern on compatible builds; investigate legacy fallback for earlier builds |
| Windows 11 | x64 and x86 process on x64 OS | Modern primary |

**PLAT-02 (Proposed):** One repository, one logical product, shared domain/business rules, Persian views/resources, schema and migrations; modern WPF host targeting .NET 10 LTS and legacy WPF host(s) targeting the proven Framework baseline. Assess a single multi-targeted WPF host first; split only platform adapters or hosts where necessary. Modern EF Core is not a shared legacy ORM (last EF Core version supporting .NET Framework was 3.1, now out of support). Investigate a maintained common ADO.NET-based SQLite-compatible data access approach or equivalent; do not lock a package until native x86/x64, encrypted database, runtime/OS support, and redistribution rights are proven. No unsupported EF Core 3.1 fallback. Windows 8 may require a distinct legacy target or alternative architecture; present viable options or a blocking trade-off for owner decision, not an unapproved downgrade.

**PLAT-03 (Required engineering gate):** Modern/Legacy must preserve the same business semantics and compatible versioned storage/backup format. Test migrations, restore across architectures/editions on appropriately licensed installations, fail-closed license and credential behavior, actual encryption of DB/WAL/journal/temp and attachment contents, key recovery, Persian printing/PDF, safe preview and native dependency loading. No plaintext/security-degraded legacy edition, no unsafe browser or Office dependency, no unapproved network call. Unsupported OS risk cannot be eliminated by application security; distinguish app compatibility from vendor OS support and explicitly warn users. Windows 10 editions/builds must be checked against current .NET and OS lifecycle policies; ordinary Windows 10 support has ended, with specific servicing/ESU exceptions.

**PLAT-04 (Required evidence):** For each OS/build/architecture, record actual environment, prerequisites, build output, installer/runtime, offline smoke tests, UI/DPI, database, licensing, crypto, PDF/print, backup/restore and status PASS/FAIL/BLOCKED/NOT RUN. Compilation or a modern Windows compatibility mode does not establish legacy execution. Use actual Windows VMs or hardware; no fabricated evidence. Establish low-resource x86 test profile (provisional 2 GB RAM, dual-core CPU, 1024×768; measure and refine, not a promised minimum). Document offline prerequisite installation, signed redistributable checksums, and separate x86/x64 installer strategy. Compatibility is not a claim of ongoing Microsoft support.

References for Phase 0 (checked 2026-11-25):
- https://learn.microsoft.com/en-us/dotnet/core/install/windows
- https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements
- https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server
- https://learn.microsoft.com/en-us/ef/core/miscellaneous/platforms
- https://learn.microsoft.com/en-us/lifecycle/products/windows-7
- https://learn.microsoft.com/en-us/lifecycle/products/windows-8
- https://learn.microsoft.com/en-us/lifecycle/announcements/windows-8-1-end-support-january-2023

## 12. Acceptance and release gates

| Test | Observable acceptance |
|---|---|
| AC-01 | Clean licensed PC works with networking disabled: setup, login, forms, attachments, print/PDF and backup. |
| AC-02 | Missing/tampered license and copied license on a different device fail closed; copied credentials alone are insufficient. |
| AC-03 | Unique initial Owner logs in; can create/deactivate delegate; unauthorized action blocked at application service level. |
| AC-04 | Home title exactly matches section 1; product, school and publisher identities remain separate; RTL layout is correct. |
| AC-05 | Create year, grade/class, student and record; duplicate known identity and enrollment conflicts handled without corruption. |
| AC-06 | Promote to next year, repeat promotion safely, verify source-year data and file hashes unchanged; class transfer works. |
| AC-07 | Fill, save, reopen and print all source form fields, qualitative assessment and talent checkboxes; period decision explicitly approved. |
| AC-08 | Import, preview, rename and delete/recover attachment; no readable persistent storage outside authorized workflow; invalid/malicious input rejected. |
| AC-09 | Search Persian variants and leading-zero IDs; soft-delete and restore records without dangling references. |
| AC-10 | PDF/printed forms correctly render Persian, selected year, school identity, all fields and signature placeholders. |
| AC-11 | Encrypted backup restores onto clean licensed installation; wrong passphrase/corrupt archive/interruption fail safely; backup does not clone license. |
| AC-12 | Upgrade from previous test schema preserves data; failed migration has recovery path; no real student data in source, logs or CI. |
| AC-13 | Verify approved Windows matrix, installer/uninstall, resolution/DPI, keyboard navigation and performance budgets. |
| AC-14 | Demonstrate PLAT-01–04 on each claimed Windows version and architecture, including native dependency load, offline prerequisite install, security and cross-edition backup compatibility; unresolved target failures require owner disposition before release. |

Automated unit/integration tests cover domain rules, permission boundaries, encryption/backup failure modes, duplicate promotion, migration and formatting. Windows UI/installer/PDF/physical-print checks require a real supported Windows test environment. Release evidence must distinguish tested, untested, blocked and proposed. No fabricated pass results.

## 13. Decisions requiring owner sign-off

| ID | Decision | Recommended baseline (not yet approved) |
|---|---|---|
| O-01 | Windows minimum version, x64/x86, hardware | Target approved: Windows 7 onward, x86/x64, 10/11 primary. Exact OS prerequisites, runtime architecture and hardware thresholds remain Phase 0 validation items. |
| O-02 | Assessment third period and print layout | Three periods; preserve original field semantics; confirm seasonal linkage. |
| O-03 | Permission model, initial owner provisioning, offline recovery | Owner + delegated permissions; signed issuance; authenticated recovery. |
| O-04 | License term, hardware replacement and ownership policy | One device per sale; reissue policy explicitly approved. |
| O-05 | File types/limits, Office preview and export | Strict allowlist and internal preview; no arbitrary shell open. |
| O-06 | Backup key/recovery, scheduling, retention, restore account handling | Encrypted manual backup + optional automatic backup; recovery tested. |
| O-07 | Required fields, target-group classification, grade-specific subjects | Preserve source labels; flexible catalog; no inferred eligibility. |
| O-08 | Year closure, exceptions, promotion copy rules, deletion/retention | Closed read-only, minimal copy, soft deletion by default. |
| O-09 | Reports, combined PDF, historical import | Three native source forms mandatory; agree on exact print layout, import of legacy data, and extras. |
| O-10 | UI theme, window policy, typography, branding legal clearance | Name accepted commercially by owner; use approved assets. Theme/window details remain open; formal trademark verification not independently established. |

## 14. Implementation phases — sequential approval gates

0. Approved product baseline; perform engineering discovery, close applicable decisions, define threat model, platform matrix, source form field map and architecture decision records; run isolated compatibility spikes. Stop for review before production implementation.
1. Repository/bootstrap, solution architecture, test/CI baseline, app shell/RTL and branding, secure storage/provider proof of concept.
2. Offline licensing, issuer/provisioning tool, local account security and authorization tests.
3. School configuration, academic year, grade/class, student registry, enrollment/promotion/archive workflows.
4. Native profile, educational assessment and talent forms, validation and source fidelity tests.
5. Encrypted attachment vault, internal safe viewers and recovery consistency tests.
6. Persian print/PDF, backup/restore, audit and authorized administration.
7. End-to-end offline/security tests, performance and Windows compatibility, installer/upgrade, pilot/UAT with synthetic data then authorized school data locally.
8. Owner-approved commercial release and separate YariZan integration. No production deploy or real-data transfer without approval.

## 15. Agent execution contract

All engineering prompts, commit messages, technical reports and source documentation are English; customer-facing strings are Persian RTL. Work phase-by-phase only after owner approval of each scope. Begin each phase with repository inspection, plan and acceptance mapping. Do not repeat completed work or change unrelated repos, deploy, create real credentials, or add sensitive samples to Git. Stop on unclear business/security rules, failing gates or destructive operations; report blockers with evidence. Use isolated synthetic fixtures. Every phase produces an English report with changes, design decisions, commands/tests, outcomes, known limitations, risks, and next approval gate. Keep SRS IDs traceable to tests and PRs. Codex or Claude is an implementation agent, not the owner of business decisions.
