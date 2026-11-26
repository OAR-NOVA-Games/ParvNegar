-- ParvNegar schema 1: foundation entities (Phase 1). Synthetic data only; no seed records.
-- Dates: ISO-8601 'yyyy-MM-dd' (Gregorian calendar date). Timestamps: ISO-8601 UTC 'yyyy-MM-ddTHH:mm:ss.fffZ'.
-- IDs: lowercase canonical GUID text.

CREATE TABLE school_profile (
    id              TEXT PRIMARY KEY NOT NULL,
    school_name     TEXT NOT NULL,
    home_title      TEXT NOT NULL,              -- stored exactly as configured, never normalized
    logo_asset_id   TEXT NULL,
    created_utc     TEXT NOT NULL,
    updated_utc     TEXT NOT NULL
) STRICT;

CREATE TABLE academic_year (
    id               TEXT PRIMARY KEY NOT NULL,
    school_id        TEXT NOT NULL REFERENCES school_profile(id) ON DELETE RESTRICT,
    start_solar_year INTEGER NOT NULL CHECK (start_solar_year BETWEEN 1390 AND 1499),
    state            INTEGER NOT NULL CHECK (state IN (0, 1, 2)),   -- 0 draft, 1 active, 2 closed
    start_date       TEXT NOT NULL,
    end_date         TEXT NOT NULL,
    UNIQUE (school_id, start_solar_year)
) STRICT;

CREATE TABLE classroom (
    id               TEXT PRIMARY KEY NOT NULL,
    academic_year_id TEXT NOT NULL REFERENCES academic_year(id) ON DELETE RESTRICT,
    grade            INTEGER NOT NULL CHECK (grade BETWEEN 1 AND 6),
    code             TEXT NOT NULL,
    name             TEXT NOT NULL,
    teacher_name     TEXT NULL,
    UNIQUE (academic_year_id, code)
) STRICT;

CREATE TABLE student (
    id               TEXT PRIMARY KEY NOT NULL,
    full_name        TEXT NOT NULL,
    full_name_key    TEXT NOT NULL,              -- normalized search key (display text unchanged)
    father_name      TEXT NULL,
    national_id      TEXT NULL CHECK (national_id IS NULL OR (length(national_id) = 10 AND national_id NOT GLOB '*[^0-9]*')),
    is_target_group  INTEGER NOT NULL CHECK (is_target_group IN (0, 1)),
    is_archived      INTEGER NOT NULL DEFAULT 0 CHECK (is_archived IN (0, 1)),
    created_utc      TEXT NOT NULL,
    updated_utc      TEXT NOT NULL
) STRICT;

CREATE UNIQUE INDEX ux_student_national_id ON student(national_id) WHERE national_id IS NOT NULL;
CREATE INDEX ix_student_name_key ON student(full_name_key);

CREATE TABLE enrollment (
    id                     TEXT PRIMARY KEY NOT NULL,
    student_id             TEXT NOT NULL REFERENCES student(id) ON DELETE RESTRICT,
    school_id              TEXT NOT NULL REFERENCES school_profile(id) ON DELETE RESTRICT,
    academic_year_id       TEXT NOT NULL REFERENCES academic_year(id) ON DELETE RESTRICT,
    grade                  INTEGER NOT NULL CHECK (grade BETWEEN 1 AND 6),
    classroom_id           TEXT NOT NULL REFERENCES classroom(id) ON DELETE RESTRICT,
    status                 INTEGER NOT NULL CHECK (status IN (0, 1, 2, 3)),
    snapshot_student_name  TEXT NOT NULL,
    snapshot_classroom     TEXT NOT NULL,
    snapshot_teacher       TEXT NULL,
    snapshot_school_title  TEXT NOT NULL,
    promotion_operation_id TEXT NULL,            -- idempotency key of the promotion batch that created it
    created_utc            TEXT NOT NULL,
    UNIQUE (student_id, school_id, academic_year_id)
) STRICT;

CREATE INDEX ix_enrollment_year_class ON enrollment(academic_year_id, classroom_id);
