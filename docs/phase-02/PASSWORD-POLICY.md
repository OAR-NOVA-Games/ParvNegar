# Password policy

Owner-approved. Source: `Security/PasswordPolicy.cs`; tested in `PasswordPolicyTests` and the application flows.

## 1. Rules

| Rule | Value |
|---|---|
| Minimum length | 15 characters |
| Maximum length | 128 characters. Longer input is **rejected, never truncated** |
| Characters | Any Unicode: Persian, English, digits in any script, spaces, symbols, emoji |
| Rejected characters | Control characters (Unicode category Cc, including Tab and newline) and unpaired surrogates |
| Composition rules | None (no forced upper/lower/digit/symbol) |
| Paste | Allowed (standard WPF PasswordBox) |
| Common passwords | Offline blocklist plus rules (§4) |
| Initial password | Must be changed: the bootstrap code at setup; the temporary password of a delegate at first sign-in |
| Periodic change | Not forced |
| Storage | Never stored. Only the Argon2id-derived KEK protects the envelope; nothing is remembered |

## 2. Unicode handling

- **Normalization:** **NFC only**, so composed and decomposed forms of the same text are equal (tested).
- **No other transformation:** no case folding, no trimming, and **no Persian/Arabic letter unification**. «ک» U+06A9 and «ك» U+0643 are different characters in a password (tested). The student-name search normalization (`PersianText.SearchKey`) is never applied to passwords.
- **Counting:** length is counted in Unicode scalar values (code points) after NFC. An emoji counts as 1 character, not 2 UTF-16 units (tested).
- **KDF input:** the UTF-8 bytes of the NFC string.
- **Keyboard layouts:** users typing on different layouts must type the same characters. The setup and password-change screens say this indirectly by allowing a phrase; the UI-UX report records it as a user-education item.

## 3. KDF and memory

- **Argon2id parameters:** unchanged from Phase 1: default m = 64 MiB, t = 3, p = 1; bounds 19–64 MiB, t 2–10, p 1–4.
- **Measured on the Windows 11 host** (i7-12700H), through the compatibility kit:
  - Modern x64: 630 ms;
  - Modern x86: 950 ms;
  - Legacy x64: 334 ms;
  - Legacy x86: 838 ms.
- **Peak private bytes during the check:** 80–95 MB.
- **Low-resource PCs:** at most one Argon2id runs at a time per process, so a 32-bit process on a 2 GB PC never needs more than one 64 MiB block for password hashing. **Not measured** on a 2 GB machine (PLATFORM-MATRIX).

## 4. Common-password protection

- **Blocklist:** `Security/common-passwords.txt` is embedded in the assembly. It holds over 100 curated entries of 15+ characters: generic long passwords, keyboard walks, Finglish phrases, product, publisher and school words. Comparison is case-insensitive, with and without spaces and the separators `- _ .`.
- **Rule checks:**
  - at most 3 distinct characters;
  - a short unit repeated (`abcabcabc…`);
  - a run of consecutive characters (`123456789012345`, `abcdefghijklmno`, keyboard rows);
  - passwords built from the username (fewer than 8 characters left after removing it).
- **Honest limit:** the curated list is small. Before release the owner should approve a larger vetted offline list, such as a public breach-derived list filtered to 15+ characters, subject to its licence (DECISION-REGISTER O-PWLIST).

## 5. Throttling

Failed sign-ins are slowed progressively and persistently. There is never a permanent lockout: SESSION-SECURITY §4.
