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

    /// <summary>Reported when a failed restore could not remove its staging directory.</summary>
    public sealed class BackupStagingCleanupException : IOException
    {
        public BackupStagingCleanupException(string stagingDir, Exception cause)
            : base("Restore failed and the staging directory could not be removed: " + stagingDir, cause) { StagingDirectory = stagingDir; }
        public string StagingDirectory { get; }
    }

    /// <summary>
    /// One backup entry. File entries are read from disk; key entries (<c>keys/...</c>) are supplied
    /// in memory only, so key material never needs a plaintext file. Dispose wipes the in-memory copy.
    /// </summary>
    public sealed class BackupEntrySource : IDisposable
    {
        public BackupEntrySource(string name, string filePath)
        {
            BackupArchive.ValidateName(name);
            if (BackupArchive.IsKeyEntry(name)) throw new ArgumentException("Key entries must be supplied in memory with BackupEntrySource.FromKeyMaterial.", nameof(name));
            Name = name;
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        private BackupEntrySource(string name, byte[] keyMaterial) { Name = name; KeyMaterial = keyMaterial; }

        /// <summary>Creates an in-memory key entry. The bytes are copied; the caller still wipes its own copy.</summary>
        public static BackupEntrySource FromKeyMaterial(string name, byte[] keyMaterial)
        {
            BackupArchive.ValidateName(name);
            if (!BackupArchive.IsKeyEntry(name)) throw new ArgumentException("In-memory entries must be named keys/...", nameof(name));
            if (keyMaterial == null || keyMaterial.Length == 0 || keyMaterial.Length > BackupArchive.MaxKeyEntryBytes) throw new ArgumentException("Invalid key material length.", nameof(keyMaterial));
            return new BackupEntrySource(name, (byte[])keyMaterial.Clone());
        }

        public string Name { get; }
        /// <summary>Source file, or null for a key entry.</summary>
        public string FilePath { get; }
        public bool IsKeyMaterial => FilePath == null;
        internal byte[] KeyMaterial { get; private set; }

        public void Dispose() { Bytes.Wipe(KeyMaterial); KeyMaterial = null; }
    }

    public sealed class BackupManifestEntry
    {
        public BackupManifestEntry(string name, long size, byte[] sha256) { Name = name; Size = size; Sha256 = sha256; }
        public string Name { get; }
        public long Size { get; }
        public byte[] Sha256 { get; }
    }

    /// <summary>
    /// Restore result. Key entries are kept in memory only (never written to staging);
    /// Dispose wipes them.
    /// </summary>
    public sealed class BackupInfo : IDisposable
    {
        private Dictionary<string, byte[]> _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public int FormatVersion { get; internal set; }
        public int SchemaVersion { get; internal set; }
        public DateTime CreatedUtc { get; internal set; }
        public IReadOnlyList<BackupManifestEntry> Entries { get; internal set; }
        public IEnumerable<string> KeyEntryNames => _keys.Keys;

        internal void AddKey(string name, byte[] value) => _keys.Add(name, value);

        /// <summary>Returns a copy of a restored key entry; the caller wipes it after use.</summary>
        public byte[] GetKeyEntry(string name)
        {
            if (_keys == null) throw new ObjectDisposedException(nameof(BackupInfo));
            if (!_keys.TryGetValue(name, out var v)) throw new KeyNotFoundException("Backup has no key entry " + name + ".");
            return (byte[])v.Clone();
        }

        public void Dispose()
        {
            if (_keys == null) return;
            foreach (var v in _keys.Values) Bytes.Wipe(v);
            _keys = null;
        }
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
    /// including file names, is encrypted. Device licenses and device secrets are never entries;
    /// key entries (keys/...) never touch disk in plaintext on either side.
    /// </summary>
    public static class BackupArchive
    {
        public const int FormatVersion = 1;
        public const int HeaderLength = 113;
        private const int WrapAadLength = 49;
        private const byte KdfArgon2id13 = 1;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PNBK");
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly byte[] ManifestMagic = Encoding.ASCII.GetBytes("PNMF");
        public const int MaxKeyEntryBytes = 4096;
        private const string KeyPrefix = "keys/";
        private static readonly string[] ForbiddenPrefixes = { "license", "device", "secrets" };
        private static readonly string[] ForbiddenExtensions = { ".pnlic", ".pnreq", ".pnds", ".pnprov" };
        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "con", "prn", "aux", "nul", "conin$", "conout$",
            "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

        public static void Create(string archivePath, IReadOnlyList<BackupEntrySource> entries, byte[] passphraseUtf8, int schemaVersion, Argon2idParameters p)
        {
            if (passphraseUtf8 == null || passphraseUtf8.Length == 0) throw new ArgumentException("Passphrase required.", nameof(passphraseUtf8));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Any(e => e == null)) throw new ArgumentException("Null entry.", nameof(entries));
            if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (entries.Any(e => e.IsKeyMaterial && e.KeyMaterial == null)) throw new ObjectDisposedException(nameof(BackupEntrySource));
            ValidateNameSet(entries.Select(e => e.Name), msg => new ArgumentException(msg, nameof(entries)));
            archivePath = Path.GetFullPath(archivePath ?? throw new ArgumentNullException(nameof(archivePath)));
            var archiveDir = Path.GetDirectoryName(archivePath);
            if (archiveDir == null || !Directory.Exists(archiveDir)) throw new DirectoryNotFoundException("Backup target folder does not exist.");
            if (File.Exists(archivePath) || Directory.Exists(archivePath)) throw new IOException("Backup file already exists; refusing to overwrite.");
            foreach (var e in entries.Where(x => !x.IsKeyMaterial))
                if (string.Equals(Path.GetFullPath(e.FilePath), archivePath, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The backup file cannot be one of its own sources.", nameof(archivePath));
            p = p ?? Argon2idParameters.Default;

            // Every source stays open with FileShare.Read for the whole operation: other processes
            // cannot write to it, so the hashed bytes and the encrypted bytes are the same bytes.
            // A source that another process holds open for writing (e.g. a live database) is refused.
            var streams = new List<Stream>(entries.Count);
            var bak = CryptoRandom.Bytes(32);
            string partial = null;
            try
            {
                foreach (var e in entries)
                    streams.Add(e.IsKeyMaterial
                        ? (Stream)new MemoryStream(e.KeyMaterial, false)
                        : new FileStream(e.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                var manifest = new List<BackupManifestEntry>(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    var hash = HashStream(streams[i], out long len);
                    streams[i].Position = 0;
                    manifest.Add(new BackupManifestEntry(entries[i].Name, len, hash));
                }

                var salt = CryptoRandom.Bytes(Argon2id.SaltLength);
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

                // Unique partial name: an existing file is never deleted or reused.
                partial = archivePath + "." + Guid.NewGuid().ToString("N") + ".partial";
                using (var fs = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(header, 0, header.Length);
                    using (var ms = new MemoryStream(SerializeManifest(manifest)))
                        ChunkedAead.Encrypt(bak, 0, headerHash, ms, fs);
                    for (int i = 0; i < entries.Count; i++)
                        using (var hs = new HashingReadStream(streams[i]))
                        {
                            var n = ChunkedAead.Encrypt(bak, (uint)(i + 1), headerHash, hs, fs);
                            if (n != manifest[i].Size || !Bytes.FixedTimeEquals(hs.Finish(), manifest[i].Sha256))
                                throw new IOException("Source changed while the backup was written: " + entries[i].Name);
                        }
                    fs.Flush(true);
                }
                File.Move(partial, archivePath);
                partial = null;
            }
            finally
            {
                foreach (var s in streams) s.Dispose();
                Bytes.Wipe(bak);
                if (partial != null) try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Reads only the unauthenticated public header fields (for a pre-restore summary).</summary>
        public static BackupInfo ReadHeaderInfo(string archivePath)
        {
            var h = ReadHeader(archivePath);
            return new BackupInfo { FormatVersion = ReadU16(h, 4), SchemaVersion = (int)Bytes.ReadUInt32BE(h, 8), CreatedUtc = Created(h) };
        }

        /// <summary>
        /// Verifies and extracts the archive into <paramref name="stagingDir"/> (which must not exist;
        /// its parent must). Nothing outside the staging directory is touched. Key entries
        /// (<c>keys/...</c>) are returned in memory through the result and are never written to disk.
        /// On any failure the staging directory is deleted and the exception propagates; if the
        /// deletion fails, <see cref="BackupStagingCleanupException"/> wraps the original failure.
        /// </summary>
        public static BackupInfo RestoreToStaging(string archivePath, byte[] passphraseUtf8, string stagingDir, BackupLimits limits)
        {
            limits = limits ?? new BackupLimits();
            if (archivePath == null) throw new ArgumentNullException(nameof(archivePath));
            stagingDir = Path.GetFullPath(stagingDir ?? throw new ArgumentNullException(nameof(stagingDir))).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(stagingDir);
            if (string.IsNullOrEmpty(parent)) throw new ArgumentException("The staging directory cannot be a volume root.", nameof(stagingDir));
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The staging parent folder does not exist.");
            if (Directory.Exists(stagingDir) || File.Exists(stagingDir)) throw new IOException("Staging directory already exists.");
            if (!File.Exists(archivePath)) throw new FileNotFoundException("Backup file not found.");
            Directory.CreateDirectory(stagingDir);
            var result = new BackupInfo();
            try
            {
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var header = new byte[HeaderLength];
                    if (ChunkedAead.ReadFull(fs, header) != HeaderLength) throw new MalformedDataException("truncated header");
                    CheckHeader(header);
                    int schema = (int)Bytes.ReadUInt32BE(header, 8);
                    if (schema < 1) throw new MalformedDataException("schema version");
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
                            if (IsKeyEntry(e.Name))
                            {
                                if (e.Size < 1 || e.Size > MaxKeyEntryBytes) throw new MalformedDataException("key entry size");
                                var buf = new byte[e.Size];
                                try
                                {
                                    using (var ks = new MemoryStream(buf, true))
                                        if (ChunkedAead.Decrypt(bak, (uint)(i + 1), headerHash, fs, ks, e.Size) != e.Size) throw new MalformedDataException("entry size mismatch");
                                    if (!Bytes.FixedTimeEquals(Bytes.Sha256(buf), e.Sha256)) throw new MalformedDataException("entry hash mismatch");
                                }
                                catch { Bytes.Wipe(buf); throw; }
                                result.AddKey(e.Name, buf);
                                continue;
                            }
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
                        result.FormatVersion = ReadU16(header, 4);
                        result.SchemaVersion = schema;
                        result.CreatedUtc = Created(header);
                        result.Entries = manifest;
                        return result;
                    }
                    finally { Bytes.Wipe(bak); }
                }
            }
            catch (Exception ex)
            {
                result.Dispose();
                if (!TryDeleteDirectory(stagingDir)) throw new BackupStagingCleanupException(stagingDir, ex);
                throw;
            }
        }

        /// <summary>Entries under <c>keys/</c> hold key material: memory-only on create and restore.</summary>
        public static bool IsKeyEntry(string name) => name != null && name.StartsWith(KeyPrefix, StringComparison.Ordinal);

        public static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 200) throw new ArgumentException("Invalid entry name length.");
            foreach (char ch in name)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-' || ch == '/'))
                    throw new ArgumentException("Entry names use [a-z0-9._-/] only.");
            if (name.StartsWith("/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal) || name.Contains("//"))
                throw new ArgumentException("Invalid entry name structure.");
            foreach (var seg in name.Split('/'))
            {
                if (seg == "." || seg == "..") throw new ArgumentException("Relative path segments are not allowed.");
                // Windows strips trailing dots and maps device names (con, nul, com1 ...) regardless of extension.
                if (seg.EndsWith(".", StringComparison.Ordinal)) throw new ArgumentException("Segments must not end with a dot.");
                if (ReservedDeviceNames.Contains(seg.Split('.')[0])) throw new ArgumentException("Windows device names are not allowed.");
            }
            foreach (var f in ForbiddenPrefixes)
                if (name.StartsWith(f, StringComparison.Ordinal)) throw new ArgumentException("Licenses and device secrets must never be backed up.");
            foreach (var x in ForbiddenExtensions)
                if (name.EndsWith(x, StringComparison.Ordinal)) throw new ArgumentException("Licenses, activation files and device secrets must never be backed up.");
        }

        /// <summary>Rejects duplicates and file/folder conflicts such as "a" together with "a/b".</summary>
        private static void ValidateNameSet(IEnumerable<string> names, Func<string, Exception> fail)
        {
            var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
            for (int i = 1; i < sorted.Count; i++)
                if (string.Equals(sorted[i - 1], sorted[i], StringComparison.Ordinal)) throw fail("Duplicate entry names.");
            var set = new HashSet<string>(sorted, StringComparer.Ordinal);
            foreach (var n in sorted)
                for (int k = n.IndexOf('/'); k > 0; k = n.IndexOf('/', k + 1))
                    if (set.Contains(n.Substring(0, k))) throw fail("An entry name is also used as a folder.");
        }

        private static bool TryDeleteDirectory(string dir)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return true; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                System.Threading.Thread.Sleep(100);
            }
            return !Directory.Exists(dir);
        }

        private static byte[] HashStream(Stream s, out long length)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
            {
                var buf = new byte[81920];
                long total = 0;
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) { h.TransformBlock(buf, 0, n, null, 0); total += n; }
                h.TransformFinalBlock(buf, 0, 0);
                Bytes.Wipe(buf);
                length = total;
                return h.Hash;
            }
        }

        /// <summary>Read-only pass-through that hashes exactly the bytes handed to the encryptor.</summary>
        private sealed class HashingReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly System.Security.Cryptography.SHA256 _h = System.Security.Cryptography.SHA256.Create();
            public HashingReadStream(Stream inner) { _inner = inner; }
            public byte[] Finish() { _h.TransformFinalBlock(new byte[0], 0, 0); return _h.Hash; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                if (n > 0) _h.TransformBlock(buffer, offset, n, null, 0);
                return n;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) _h.Dispose(); base.Dispose(disposing); }
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
                string name;
                try { name = StrictUtf8.GetString(take(nl)); } catch (DecoderFallbackException) { throw new MalformedDataException("entry name encoding"); }
                try { ValidateName(name); } catch (ArgumentException) { throw new MalformedDataException("unsafe entry name"); }
                if (!names.Add(name)) throw new MalformedDataException("duplicate entry name");
                ulong size = Bytes.ReadUInt64BE(take(8), 0);
                if (size > long.MaxValue) throw new MalformedDataException("entry size");
                list.Add(new BackupManifestEntry(name, (long)size, take(32)));
            }
            if (o != m.Length) throw new MalformedDataException("manifest trailing bytes");
            ValidateNameSet(list.Select(e => e.Name), msg => new MalformedDataException("conflicting entry names"));
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
