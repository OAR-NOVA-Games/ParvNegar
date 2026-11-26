# ParvNegar Phase 0 — Source Field Map (sanitized)

Date: 2026-11-26. Status: draft for owner verification. SRS: v1.0.

## 1. How this map was produced

- Source: the owner-supplied archive `شاهد.rar`, found locally in the workstation Downloads folder. It was extracted only into the session scratch directory. It was never copied into this repository.
- The archive holds 1 PPTX prototype (7 slides), 4 student DOCX files, 1 talent form (`.doc`, Word 97 binary), 2 PNG school logos, 1 ICO and 2 Office lock files (162 bytes, ignored).
- A local script read the DOCX table XML. It printed only text that is **identical in all four student files** and contains no digits. Values that differ between students (names, national IDs, phone numbers, ratings) were never printed. Cells that hold values are shown below as `<value>`.
- One staff name appears in an identical footer in all four files. It is personal data and is omitted here.
- The PPTX was inspected for structure only (slide count, shape/picture counts). Slide text was not extracted, because slides may contain sample student data. Slide semantics come from SRS §1.
- The talent `.doc` was not parsed (binary format, no local parser). The talent catalog below is the SRS §6 normalized transcription. It is pending owner verification against the paper form.
- No source value is used in fixtures, tests or screenshots. All spike data is synthetic.

## 2. Source documents

| Source ref | Type | Structure observed | Status |
|---|---|---|---|
| SRC-PPT | PPTX, 7 slides | Slide shapes/pictures: S1 7/1, S2 11/1, S3 11/1, S4 6/1, S5 12/2, S6 5/2, S7 5/2; no native tables | Structure only; semantics per SRS §1 |
| SRC-DOCX-1..4 | DOCX student files | Identical layout: 2 tables, 190 cells, 2 footer paragraphs, 2 embedded images (logo and photo cell) | Labels extracted, values withheld |
| SRC-TAL | DOC (Word 97) | Not parsed | Use SRS §6 transcription; owner to verify |
| SRC-LOGO | PNG ×2, ICO ×1 | School logo assets | Authorized configuration assets; not committed in Phase 0 |

## 3. Form A — educational assessment (DOCX table T0)

Grid: 18 rows × up to 11 columns. Columns C2–C9 are two assessment periods × four qualitative levels. Column C10 is remarks.

| Field ID | Source label (verbatim) | Source position | Type | Required | Scope | Print placement |
|---|---|---|---|---|---|---|
| EDU-HDR | `<value>` (form header; varies per student) | T0 R0 C0 | Text (generated) | n/a | Annual | Top header |
| EDU-ROWNO | ردیف | T0 R2 C0 | Row number | n/a | Catalog | Column 1 |
| EDU-SUBJECT | مواد درسی | T0 R2 C1 | Catalog ref | Yes (catalog) | Year/grade catalog | Column 2 |
| EDU-P1 | وضعیت تحصیلی سه ماهه اول (مهر ، آبان و آذر) | T0 R2 C2 | Period group | Unknown | Annual | Columns 3–6 |
| EDU-P2 | وضعیت تحصیلی سه ماهه دوم (دی ، بهمن واسفند) | T0 R2 C3 | Period group | Unknown | Annual | Columns 7–10 |
| EDU-REMARK | ملاحظات | T0 R2 C4 / C10 | Free text per subject | Unknown | Annual | Last column |
| EDU-LEVEL | خیلی خوب / خوب / قابل قبول / نیاز به تلاش بیشتر | T0 R3 C2–C5 (repeated per period) | Enum (4) + unrecorded | Unknown | Annual, per subject × period | Check mark in level column |
| EDU-BEHAV | وضعیت تربیتی | T0 R15 C1 | Text or level (unknown; cells empty in all samples) | Unknown | Annual | Row after subjects |
| EDU-ANALYSIS | تحلیل عملکرد(ارتقاء ،افت) | T0 R16 C1 | Free text | Unknown | Annual | Row after behavior |
| EDU-T0-R17 | `<empty in all samples>` | T0 R17 C0 | Unknown | Unknown | Unknown | Bottom row; purpose to confirm |
| SIG-TEACHER | نام و نام¬خانوادگی معلم کلاس: … امضاء | Footer paragraph | Name text + signature placeholder | Unknown | Annual snapshot | Footer |
| SIG-DIRECTOR | نام و نام خانوادگی مدیر آموزشگاه : … مهر و امضاء | Footer paragraph | Name text (staff name present in source; withheld) + stamp/signature placeholder | Unknown | Annual snapshot | Footer |

Subject rows (T0 R4–R14), verbatim source spelling:

| Row | Source label | Note |
|---|---|---|
| R4 | قرآن | |
| R5 | هدیه های آسمان | |
| R6 | فارسی(نوشتاری) | |
| R7 | فارسی(خوانداری) | |
| R8 | مطالعات اجتماعي | Arabic Yeh (U+064A) in source |
| R9 | رياضيات | Arabic Yeh in source |
| R10 | علوم تجربي و بهداشت | Arabic Yeh in source |
| R11 | فناوری | |
| R12 | تفکر وپژوهش | No space before پژوهش in source |
| R13 | هنر | |
| R14 | تربیت بدنی وسلامت | No space before سلامت in source |

Observations:
- All rating cells (R4–R16, C2–C9) are empty in every sample. The samples prove layout, not a filled rating convention.
- Source mixes Arabic and Persian Yeh and has irregular spacing. Store a normalized display label, and keep the source spelling in the catalog record for traceability (owner decides display form, O-07).
- Only two periods exist in the source. The proposed third period (Farvardin–Khordad) is **pending O-02** and is not implemented.

## 4. Form B — student profile (DOCX table T1)

| Field ID | Source label (verbatim) | Source position | Type | Required | Scope | Print placement |
|---|---|---|---|---|---|---|
| PRF-HDR | `<value>` (header; varies per student) | T1 R0 C0 | Generated text | n/a | Annual | Top |
| PRF-PHOTO | `<empty cell, image embedded>` | T1 R1–R3 C0 (image) | Image (encrypted at rest) | Unknown (SRS: not required without approved rule) | Stable, versioned | Left/right photo cell |
| PRF-NAME | نام ونام خانوادگی | T1 R1 C1 (label + value in same cell) | Text | Unknown | Stable identity + annual snapshot | Row 1 |
| PRF-FATHER-NAME-H | نام پدر | T1 R1 C2 | Text | Unknown | Stable + annual snapshot | Row 1 |
| PRF-CLASS | کلاس | T1 R1 C3 | Enrollment ref | Yes (enrollment) | Annual | Row 1 |
| PRF-NATID | شمار ه ملی (source spacing) | T1 R3 C1 | String, leading zeros, digit-normalized | Unknown (absent-ID exception pending O-07) | Stable | Row 3 |
| PRF-ISAR | اولویت ایثارگری | T1 R3 C2 | Text/enum (options unknown) | Unknown | Annual snapshot (to confirm) | Row 3 |
| PRF-TEACHER | نام معلم | T1 R3 C3 | Classroom ref (teacher) | Unknown | Annual | Row 3 |
| PRF-SEASON-H | وضعیت تحصیلی | T1 R1 C4 | Group header | n/a | Annual | Right block |
| PRF-SEASON-FALL | پاییز | T1 R2 C4, value T1 R3 C4 | Text/level (unknown) | Unknown | Annual | Right block |
| PRF-SEASON-WINTER | زمستان | T1 R2 C5, value T1 R3 C5 | Text/level (unknown) | Unknown | Annual | Right block |
| PRF-SEASON-SPRING | بهار | T1 R2 C6, value T1 R3 C6 | Text/level (unknown) | Unknown | Annual | Right block |
| PRF-VISIT-FOLLOWUP | ثبت موارد پیگیری در طرح دیدار | T1 R4 C0, value T1 R4 C1 | Multi-line text | Unknown | Annual | Full-width row |
| PRF-VISIT-LOG | ثبت دیدار و بازدید | T1 R5 C0, value T1 R5 C1 | Multi-line text | Unknown | Annual | Full-width row |
| PRF-FATHER-NAME | نام پدر: | T1 R6 C0 | Text | Unknown | Annual guardian snapshot | Parent row 1 |
| PRF-FATHER-EDU | میزان تحصیلات: | T1 R6 C1 | Text | Unknown | Annual guardian snapshot | Parent row 1 |
| PRF-FATHER-JOB | شغل: | T1 R6 C2 | Text | Unknown | Annual guardian snapshot | Parent row 1 |
| PRF-FATHER-MOBILE | تلفن همراه: | T1 R6 C3 | Phone string (sensitive; never logged) | Unknown | Annual guardian snapshot | Parent row 1 |
| PRF-MOTHER-NAME | نام مادر: | T1 R7 C0 | Text | Unknown | Annual guardian snapshot | Parent row 2 |
| PRF-MOTHER-EDU/JOB/MOBILE | Same labels as father row | T1 R7 C1–C3 | As father | Unknown | Annual guardian snapshot | Parent row 2 |
| PRF-GUARDIAN | ولی پیگیر امور آموزشی وتحصیلی دانش آموز | T1 R8 C0, value T1 R8 C1 | Text or enum (father/mother/other; unknown) | Unknown | Annual | Last row |

Observations:
- `نام پدر` appears twice: in the identity header (R1 C2) and in the parent block (R6 C0). Proposal: one stored father name, printed in both places. Owner to confirm (O-07).
- Seasonal status (پاییز/زمستان/بهار) and the two assessment periods are separate in the source. Whether they are linked is **pending O-02**. No aggregation is implemented.
- T1 R2 C0–C3 are empty in all samples (spacer or photo span). Exact print fidelity is O-09.

## 5. Form C — talent and activity form (SRC-TAL)

Source not parsed. SRS §6 transcription (normalized, pending owner verification):

| Category ID | Category | Options (count) |
|---|---|---|
| TAL-ART | فرهنگی هنری | 7 |
| TAL-SPORT | ورزشی | 7 |
| TAL-REL | مذهبی | 7 |
| TAL-SCI | علمی | 8 |
| TAL-ORG | تشکیلات دانش‌آموزی | 5 |
| TAL-CAMP | اردویی | 5 |

Field rules (SRS TAL-01): per student per year; boolean per option; form state `NotStarted` / `Completed` (a completed form may have zero selections); optional note (proposed). No scoring or inference.

## 6. Logical folder (attachment) categories

| Category ID | Persian label | Source |
|---|---|---|
| ATT-EDU-REPORT | آموزشی › کارنامه | SRS UI-04, DOC-01 |
| ATT-EDU-REINF | آموزشی › کلاس تقویتی | SRS UI-04, EDU-04 |
| ATT-PASTORAL | پرورشی | SRS UI-04 |
| ATT-COUNSEL | مشاوره | SRS UI-04 |
| ATT-VISIT | دیدار و بازدید | SRS UI-04 |
| ATT-OTHER | سایر | SRS UI-04 |

## 7. Home title (school configuration)

Stored verbatim as `SchoolProfile.HomeTitle` (SRS §1), separate from product name `پرونگار`:

> ثبت شاخص های آموزشی و پرورشی دانش آموزان جامعه هدف مدرسه شاهد شهیده شهناز کریمی

Note: the title uses a plain space in `شاخص های` and `دانش آموزان` (no ZWNJ). Keep it exactly; never auto-normalize configuration titles.

## 8. Grade promotion invariants (proposed, for AY-03/AY-04)

1. `Student.Id` is a stable surrogate key (GUID/ULID). It never derives from national ID or name.
2. `Enrollment` is unique on (StudentId, SchoolId, AcademicYearId).
3. Promotion is one transaction per batch. Re-running it with the same source/target creates no second enrollment (idempotent by the uniqueness constraint plus an operation ID).
4. Target grade = source grade + 1 only for grades 1–5. Grade 6 maps to `Completed`, never to grade 7. Repeat-grade and withdrawal are explicit outcomes, never silent defaults.
5. The target class must exist in the target year and match the target grade.
6. Source-year rows (assessments, talent selections, profile snapshot, attachments, guardian snapshot) are never updated by promotion. A hash of the source-year export before and after promotion must be equal (AC-06).
7. The new enrollment copies only approved identity/profile defaults (O-08). Annual data starts empty.
8. A closed year is read-only. Promotion out of a closed year is allowed. Promotion into a closed year is refused.
9. A preview and conflict report (missing class, duplicate enrollment, grade 6, withdrawn) is produced before commit.

History snapshots (per enrollment): student display name, class name, teacher name, guardian block, school name/title/logo version, subject catalog version, period definition version.
