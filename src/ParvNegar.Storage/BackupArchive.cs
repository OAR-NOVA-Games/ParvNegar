using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ParvNegar.Security;

namespace ParvNegar.Storage
{
    public sealed class UnsupportedBackupException : Exception
    {
        public UnsupportedBackupException(string message) : base(message) { }
    }

    public sealed class BackupEntrySource
    {
        public BackupEntrySource(string name, string filePath)
        {
            BackupArchive.ValidateName(name);
            Name = name;
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public string Name { get; }
        public string FilePath { get; }
    }

    public sealed class BackupManifestEntry
    {
        public BackupManifestEntry(string name, long size, byte[] sha256) { Name = name; Size = size; Sha256 = sha256; }
        public string Name { get; }
        public long Size { get; }
        public byte[] Sha256 { get; }
    }

    public sealed class BackupInfo
    {
        public int FormatVersion { get; internal set; }
        public int SchemaVersion { get; internal set; }
        public DateTime CreatedUtc { get; internal set; }
        public IReadOnlyList<BackupManifestEntry> Entries { get; internal set; }
    }

    public sealed class BackupLimits
    {
        public int MaxSchemaVersion { get; set; } = int.MaxValue;
        public int MaxEntries { get; set; } = 100000;
        public long MaxEntryBytes { get; set; } = 2L * 1024 * 1024 * 1024;
        public long MaxTotalBytes { get; set; } = 64L * 1024 * 1024 * 1024;
    }

    /// <summary>
    /// Portable encrypted backup, format PNBK v1 (STORAGE-FORMAT.md §4). The archive key (BAK) is
    /// random per backup and wrapped by Argon2id(passphrase). Everything after the header,
    /// including file names, is encrypted. Device licenses and device secrets are never entries.
    /// </summary>
    public static class BackupArchive
    {
        public const int FormatVersion = 1;
        public const int HeaderLength = 113;
        private const int WrapAadLength = 49;
        private const byte KdfArgon2id13 = 1;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PNBK");
        private static readonly byte[] ManifestMagic = Encoding.ASCII.GetBytes("PNMF");
        private static readonly string[] ForbiddenPrefixes = { "license", "device", "secrets" };

        public static void Create(string archivePath, IReadOnlyList<BackupEntrySource> entries, byte[] passphraseUtf8, int schemaVersion, Argon2idParameters p)
        {
            if (passphraseUtf8 == null || passphraseUtf8.Length == 0) throw new ArgumentException("Passphrase required.", nameof(passphraseUtf8));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count) throw new ArgumentException("Duplicate entry names.");
            if (File.Exists(archivePath)) throw new IOException("Backup file already exists; refusing to overwrite.");
            p = p ?? Argon2idParameters.Default;

            var manifest = entries.Select(e => { var fi = new FileInfo(e.FilePath); return new BackupManifestEntry(e.Name, fi.Length, HashFile(e.FilePath)); }).ToList();
            var salt = CryptoRandom.Bytes(Argon2id.SaltLength);
            var bak = CryptoRandom.Bytes(32);
            var header = new byte[HeaderLength];
            Buffer.BlockCopy(Magic, 0, header, 0, 4);
            WriteU16(header, 4, FormatVersion);
            WriteU16(header, 6, FormatVersion); // minimum reader version
            Bytes.WriteUInt32BE(header, 8, (uint)schemaVersion);
            Bytes.WriteUInt64BE(header, 12, (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            header[20] = KdfArgon2id13;
            Bytes.WriteUInt32BE(header, 21, (uint)p.MemoryKiB);
            Bytes.WriteUInt32BE(header, 25, (uint)p.Iterations);
            Bytes.WriteUInt32BE(header, 29, (uint)p.Parallelism);
            Buffer.BlockCopy(salt, 0, header, 33, 16);
            var wrapNonce = CryptoRandom.Bytes(12);
            Buffer.BlockCopy(wrapNonce, 0, header, 49, 12);
            var kek = Kek(passphraseUtf8, salt, p);
            try
            {
                var wrapped = AesGcm256.Encrypt(kek, wrapNonce, Slice(header, 0, WrapAadLength), bak);
                Buffer.BlockCopy(wrapped, 0, header, 61, 48);
            }
            finally { Bytes.Wipe(kek); }
            Bytes.WriteUInt32BE(header, 109, (uint)entries.Count);
            var headerHash = Bytes.Sha256(header);

            var partial = archivePath + ".partial";
            if (File.Exists(partial)) File.Delete(partial);
            try
            {
                using (var fs = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(header, 0, header.Length);
                    using (var ms = new MemoryStream(SerializeManifest(manifest)))
                        ChunkedAead.Encrypt(bak, 0, headerHash, ms, fs);
                    for (int i = 0; i < entries.Count; i++)
                        using (var src = File.OpenRead(entries[i].FilePath))
                        {
                            var n = ChunkedAead.Encrypt(bak, (uint)(i + 1), headerHash, src, fs);
                            if (n != manifest[i].Size) throw new IOException("Source file changed while the backup was written: " + entries[i].Name);
                        }
                    fs.Flush(true);
                }
                File.Move(partial, archivePath);
            }
            catch
            {
                if (File.Exists(partial)) File.Delete(partial);
                throw;
            }
            finally { Bytes.Wipe(bak); }
        }

        /// <summary>Reads only the unauthenticated public header fields (for a pre-restore summary).</summary>
        public static BackupInfo ReadHeaderInfo(string archivePath)
        {
            var h = ReadHeader(archivePath);
            return new BackupInfo { FormatVersion = ReadU16(h, 4), SchemaVersion = (int)Bytes.ReadUInt32BE(h, 8), CreatedUtc = Created(h) };
        }

        /// <summary>
        /// Verifies and extracts the whole archive into <paramref name="stagingDir"/> (which must not exist).
        /// Nothing outside the staging directory is touched. On any failure the staging directory is
        /// deleted and the exception propagates; live data is never modified here.
        /// </summary>
        public static BackupInfo RestoreToStaging(string archivePath, byte[] passphraseUtf8, string stagingDir, BackupLimits limits)
        {
            limits = limits ?? new BackupLimits();
            stagingDir = Path.GetFullPath(stagingDir);
            if (Directory.Exists(stagingDir) || File.Exists(stagingDir)) throw new IOException("Staging directory already exists.");
            Directory.CreateDirectory(stagingDir);
            try
            {
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var header = new byte[HeaderLength];
                    if (ChunkedAead.ReadFull(fs, header) != HeaderLength) throw new MalformedDataException("truncated header");
                    CheckHeader(header);
                    int schema = (int)Bytes.ReadUInt32BE(header, 8);
                    if (schema > limits.MaxSchemaVersion) throw new UnsupportedBackupException("Backup schema version " + schema + " is newer than supported (" + limits.MaxSchemaVersion + ").");
                    uint count = Bytes.ReadUInt32BE(header, 109);
                    if (count > limits.MaxEntries) throw new MalformedDataException("too many entries");
                    var p = new Argon2idParameters((int)Bytes.ReadUInt32BE(header, 21), (int)Bytes.ReadUInt32BE(header, 25), (int)Bytes.ReadUInt32BE(header, 29));
                    var kek = Kek(passphraseUtf8 ?? new byte[0], Slice(header, 33, 16), p);
                    byte[] bak;
                    try { bak = AesGcm256.Decrypt(kek, Slice(header, 49, 12), Slice(header, 0, WrapAadLength), Slice(header, 61, 48)); }
                    finally { Bytes.Wipe(kek); }
                    var headerHash = Bytes.Sha256(header);
                    try
                    {
                        List<BackupManifestEntry> manifest;
                        using (var ms = new MemoryStream())
                        {
                            ChunkedAead.Decrypt(bak, 0, headerHash, fs, ms, 16L * 1024 * 1024);
                            manifest = ParseManifest(ms.ToArray(), (int)count);
                        }
                        long total = 0;
                        for (int i = 0; i < manifest.Count; i++)
                        {
                            var e = manifest[i];
                            if (e.Size > limits.MaxEntryBytes) throw new MalformedDataException("entry exceeds size limit");
                            total += e.Size;
                            if (total > limits.MaxTotalBytes) throw new MalformedDataException("backup exceeds total size limit");
                            var target = Path.GetFullPath(Path.Combine(stagingDir, e.Name.Replace('/', Path.DirectorySeparatorChar)));
                            if (!target.StartsWith(stagingDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new MalformedDataException("entry path escapes staging");
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            using (var outFs = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                var n = ChunkedAead.Decrypt(bak, (uint)(i + 1), headerHash, fs, outFs, e.Size);
                                if (n != e.Size) throw new MalformedDataException("entry size mismatch");
                                outFs.Flush(true);
                            }
                            if (!Bytes.FixedTimeEquals(HashFile(target), e.Sha256)) throw new MalformedDataException("entry hash mismatch");
                        }
                        if (fs.Position != fs.Length) throw new MalformedDataException("trailing data after last entry");
                        return new BackupInfo { FormatVersion = ReadU16(header, 4), SchemaVersion = schema, CreatedUtc = Created(header), Entries = manifest };
                    }
                    finally { Bytes.Wipe(bak); }
                }
            }
            catch
            {
                try { Directory.Delete(stagingDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        public static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 200) throw new ArgumentException("Invalid entry name length.");
            foreach (char ch in name)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-' || ch == '/'))
                    throw new ArgumentException("Entry names use [a-z0-9._-/] only.");
            if (name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal) || name.Contains("//"))
                throw new ArgumentException("Invalid entry name structure.");
            foreach (var seg in name.Split('/'))
                if (seg == "." || seg == "..") throw new ArgumentException("Relative path segments are not allowed.");
            foreach (var f in ForbiddenPrefixes)
                if (name.StartsWith(f, StringComparison.Ordinal)) throw new ArgumentException("Licenses and device secrets must never be backed up.");
        }

        private static byte[] ReadHeader(string archivePath)
        {
            var h = new byte[HeaderLength];
            using (var fs = File.OpenRead(archivePath))
                if (ChunkedAead.ReadFull(fs, h) != HeaderLength) throw new MalformedDataException("truncated header");
            CheckHeader(h);
            return h;
        }

        private static void CheckHeader(byte[] h)
        {
            for (int i = 0; i < 4; i++) if (h[i] != Magic[i]) throw new MalformedDataException("not a ParvNegar backup");
            int ver = ReadU16(h, 4), minReader = ReadU16(h, 6);
            if (minReader > FormatVersion || ver < 1) throw new UnsupportedBackupException("Backup format " + ver + " (requires reader " + minReader + ") is not supported by this version.");
            if (h[20] != KdfArgon2id13) throw new UnsupportedBackupException("Unsupported key derivation in backup.");
        }

        private static byte[] Kek(byte[] pass, byte[] salt, Argon2idParameters p)
        {
            var k = Argon2id.Derive(pass, salt, p, 32);
            try { return Hkdf.DeriveKey(k, null, KeyLabels.BackupKek, 32); }
            finally { Bytes.Wipe(k); }
        }

        private static byte[] SerializeManifest(IReadOnlyList<BackupManifestEntry> entries)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(ManifestMagic, 0, 4);
                ms.WriteByte(1);
                var b4 = new byte[4]; Bytes.WriteUInt32BE(b4, 0, (uint)entries.Count); ms.Write(b4, 0, 4);
                foreach (var e in entries)
                {
                    var name = Encoding.UTF8.GetBytes(e.Name);
                    var b2 = new byte[2]; WriteU16(b2, 0, name.Length); ms.Write(b2, 0, 2);
                    ms.Write(name, 0, name.Length);
                    var b8 = new byte[8]; Bytes.WriteUInt64BE(b8, 0, (ulong)e.Size); ms.Write(b8, 0, 8);
                    ms.Write(e.Sha256, 0, 32);
                }
                return ms.ToArray();
            }
        }

        private static List<BackupManifestEntry> ParseManifest(byte[] m, int expectedCount)
        {
            int o = 0;
            Func<int, byte[]> take = n => { if (o + n > m.Length) throw new MalformedDataException("manifest truncated"); var r = Slice(m, o, n); o += n; return r; };
            var magic = take(4);
            for (int i = 0; i < 4; i++) if (magic[i] != ManifestMagic[i]) throw new MalformedDataException("manifest magic");
            if (take(1)[0] != 1) throw new MalformedDataException("manifest version");
            uint count = Bytes.ReadUInt32BE(take(4), 0);
            if (count != expectedCount) throw new MalformedDataException("manifest count mismatch");
            var list = new List<BackupManifestEntry>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                int nl = ReadU16(take(2), 0);
                var name = Encoding.UTF8.GetString(take(nl));
                try { ValidateName(name); } catch (ArgumentException) { throw new MalformedDataException("unsafe entry name"); }
                if (!names.Add(name)) throw new MalformedDataException("duplicate entry name");
                ulong size = Bytes.ReadUInt64BE(take(8), 0);
                if (size > long.MaxValue) throw new MalformedDataException("entry size");
                list.Add(new BackupManifestEntry(name, (long)size, take(32)));
            }
            if (o != m.Length) throw new MalformedDataException("manifest trailing bytes");
            return list;
        }

        private static byte[] HashFile(string path)
        {
            using (var s = System.Security.Cryptography.SHA256.Create())
            using (var f = File.OpenRead(path)) return s.ComputeHash(f);
        }

        private static DateTime Created(byte[] h) => new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Bytes.ReadUInt64BE(h, 12));
        private static byte[] Slice(byte[] b, int o, int n) { var r = new byte[n]; Buffer.BlockCopy(b, o, r, 0, n); return r; }
        private static void WriteU16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
        private static int ReadU16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    }
}
