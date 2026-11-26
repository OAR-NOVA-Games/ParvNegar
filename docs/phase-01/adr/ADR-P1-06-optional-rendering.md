# ADR-P1-06 — PDF and preview behind optional ports

Status: Accepted (2026-11-26). Engine selection Deferred by owner decision (PDF engine deferred beyond Phase 1).

Decision: `IReportRenderer` and `IDocumentPreviewer` ports with `Unavailable*` defaults. No PDF or preview native package in Phase 1. Engines will be loaded lazily; a load failure disables only that feature. No outdated PDFium/Skia build is pinned just to reach Windows 7.

Open risks carried to D-06:
- Windows 7 native loading (Skia 4.x and PDFium 160 fail static checks).
- PDFium on Windows 8.1 (subsystem 10.0).
- Persian bidi ordering (raw HarfBuzz reversed digits in Phase 0).
- Correct glyph extraction (`پ` lost in Phase 0) and searchable PDF.
- QuestPDF commercial eligibility.
- Physical A4 printing.
- Plaintext temporary files during preview/print.

A reduced-feature Legacy edition needs explicit owner approval.
