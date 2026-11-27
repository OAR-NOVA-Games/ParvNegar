-- ParvNegar schema 2 (Phase 2): installation identity, accounts, permissions, security audit chain.
-- IDs: lowercase canonical GUID text. Timestamps: ISO-8601 UTC 'yyyy-MM-ddTHH:mm:ss.fffZ'.
-- This database is the AUTHORITATIVE source of accounts, roles, permissions and envelope revisions.
-- Pre-unlock files (envelopes on disk) are only used to locate an envelope; after unlock every
-- decision is checked against these tables (AUTHENTICATION-ARCHITECTURE.md §4).

CREATE TABLE installation_info (
    id                    INTEGER PRIMARY KEY NOT NULL CHECK (id = 1),
    install_id            TEXT NOT NULL,
    license_id            TEXT NOT NULL,
    provisioning_id       TEXT NOT NULL,             -- one-time owner provisioning that created this installation
    created_utc           TEXT NOT NULL,
    recovery_envelope_id  TEXT NOT NULL,
    recovery_revision     INTEGER NOT NULL CHECK (recovery_revision >= 1),
    recovery_confirmed    INTEGER NOT NULL CHECK (recovery_confirmed IN (0, 1)),
    device_generation     INTEGER NOT NULL CHECK (device_generation >= 1)   -- increments on device re-binding
) STRICT;

CREATE TABLE account (
    id                    TEXT PRIMARY KEY NOT NULL,
    username              TEXT NOT NULL UNIQUE,      -- canonical (lower-case a-z 0-9 . _ -)
    display_name          TEXT NOT NULL,
    role                  INTEGER NOT NULL CHECK (role IN (1, 2)),          -- 1 owner, 2 delegated administrator
    status                INTEGER NOT NULL CHECK (status IN (1, 2, 3)),     -- 1 active, 2 disabled, 3 deleted
    envelope_id           TEXT NULL,                 -- NULL: no usable envelope (deleted, or reset pending)
    envelope_revision     INTEGER NOT NULL CHECK (envelope_revision >= 0),
    must_change_password  INTEGER NOT NULL CHECK (must_change_password IN (0, 1)),
    authz_version         INTEGER NOT NULL CHECK (authz_version >= 1),      -- bumps on role/status/permission change
    created_utc           TEXT NOT NULL,
    updated_utc           TEXT NOT NULL,
    created_by            TEXT NULL REFERENCES account(id) ON DELETE RESTRICT
) STRICT;

CREATE INDEX ix_account_status ON account(status, role);

CREATE TABLE account_permission (
    account_id   TEXT NOT NULL REFERENCES account(id) ON DELETE CASCADE,
    permission   TEXT NOT NULL,
    granted_utc  TEXT NOT NULL,
    granted_by   TEXT NOT NULL REFERENCES account(id) ON DELETE RESTRICT,
    PRIMARY KEY (account_id, permission)
) STRICT;

-- Security audit log: ordered HMAC chain under the LMK-derived audit key (AUDIT-LOG-FORMAT.md).
CREATE TABLE audit_event (
    seq        INTEGER PRIMARY KEY NOT NULL,        -- strictly increasing, no gaps after the anchor
    utc        TEXT NOT NULL,
    kind       TEXT NOT NULL,
    actor_id   TEXT NULL,
    subject    TEXT NULL,
    outcome    TEXT NOT NULL,
    detail     TEXT NULL,
    source     TEXT NOT NULL CHECK (source IN ('session', 'pre-unlock')),
    mac        BLOB NOT NULL CHECK (length(mac) = 32)
) STRICT;

CREATE TABLE audit_state (
    id            INTEGER PRIMARY KEY NOT NULL CHECK (id = 1),
    head_seq      INTEGER NOT NULL,                 -- last written seq (0 = empty)
    head_mac      BLOB NOT NULL CHECK (length(head_mac) = 32),
    anchor_seq    INTEGER NOT NULL,                 -- events with seq <= anchor_seq were pruned
    anchor_mac    BLOB NOT NULL CHECK (length(anchor_mac) = 32)
) STRICT;
