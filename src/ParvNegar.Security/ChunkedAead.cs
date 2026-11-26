using System;
using System.IO;

namespace ParvNegar.Security
{
    /// <summary>
    /// Chunked AES-256-GCM stream (STORAGE-FORMAT.md §2). One data key protects a set of streams;
    /// each stream has a distinct index. Per chunk:
    ///   record = flags:u8 | ctLen:u32be | ciphertext||tag
    ///   nonce  = streamIndex:u32be | chunkIndex:u64be                (12 bytes, unique per key)
    ///   aad    = headerHash[32] | streamIndex:u32be | chunkIndex:u64be | flags:u8
    /// flags bit0 = final chunk; other bits must be zero. Non-final chunks carry exactly ChunkSize
    /// plaintext bytes; the final chunk carries 0..ChunkSize. Truncation, reordering, duplication,
    /// cross-stream splicing and header substitution all fail authentication or structure checks.
    /// </summary>
    public static class ChunkedAead
    {
        public const int ChunkSize = 64 * 1024;
        public const int HeaderHashLength = 32;
        public const uint MaxStreamIndex = (1u << 20) - 1;
        public const ulong MaxChunksPerStream = 1UL << 31;
        private const byte FlagFinal = 0x01;

        public static long Encrypt(byte[] key, uint streamIndex, byte[] headerHash, Stream input, Stream output)
        {
            Validate(key, streamIndex, headerHash);
            var cur = new byte[ChunkSize];
            var next = new byte[ChunkSize];
            int curLen = ReadFull(input, cur);
            ulong chunk = 0;
            long total = 0;
            while (true)
            {
                int nextLen = curLen == ChunkSize ? ReadFull(input, next) : 0;
                bool final = curLen < ChunkSize || nextLen == 0;
                if (chunk >= MaxChunksPerStream) throw new InvalidOperationException("Stream exceeds the maximum chunk count.");
                byte flags = final ? FlagFinal : (byte)0;
                var pt = new byte[curLen];
                Buffer.BlockCopy(cur, 0, pt, 0, curLen);
                var ct = AesGcm256.Encrypt(key, Nonce(streamIndex, chunk), Aad(headerHash, streamIndex, chunk, flags), pt);
                Bytes.Wipe(pt);
                var hdr = new byte[5];
                hdr[0] = flags;
                Bytes.WriteUInt32BE(hdr, 1, (uint)ct.Length);
                output.Write(hdr, 0, hdr.Length);
                output.Write(ct, 0, ct.Length);
                total += curLen;
                chunk++;
                if (final) break;
                var t = cur; cur = next; next = t; curLen = nextLen;
            }
            Bytes.Wipe(cur); Bytes.Wipe(next);
            return total;
        }

        /// <summary>Decrypts one stream into <paramref name="output"/>. Throws before returning if any check fails.</summary>
        public static long Decrypt(byte[] key, uint streamIndex, byte[] headerHash, Stream input, Stream output, long maxPlaintextBytes)
        {
            Validate(key, streamIndex, headerHash);
            ulong chunk = 0;
            long total = 0;
            var hdr = new byte[5];
            while (true)
            {
                if (ReadFull(input, hdr) != hdr.Length) throw new MalformedDataException("truncated stream (missing final chunk)");
                byte flags = hdr[0];
                if ((flags & ~FlagFinal) != 0) throw new MalformedDataException("unknown chunk flags");
                bool final = (flags & FlagFinal) != 0;
                uint ctLen = Bytes.ReadUInt32BE(hdr, 1);
                if (ctLen < AesGcm256.TagLength || ctLen > ChunkSize + AesGcm256.TagLength) throw new MalformedDataException("chunk length out of range");
                if (!final && ctLen != ChunkSize + AesGcm256.TagLength) throw new MalformedDataException("non-final chunk must be full");
                if (chunk >= MaxChunksPerStream) throw new MalformedDataException("too many chunks");
                long ptLen = ctLen - AesGcm256.TagLength;
                if (total + ptLen > maxPlaintextBytes) throw new MalformedDataException("stream exceeds size limit");
                var ct = new byte[ctLen];
                if (ReadFull(input, ct) != ct.Length) throw new MalformedDataException("truncated chunk");
                var pt = AesGcm256.Decrypt(key, Nonce(streamIndex, chunk), Aad(headerHash, streamIndex, chunk, flags), ct);
                output.Write(pt, 0, pt.Length);
                Bytes.Wipe(pt);
                total += ptLen;
                chunk++;
                if (final) return total;
            }
        }

        internal static byte[] Nonce(uint streamIndex, ulong chunk)
        {
            var n = new byte[AesGcm256.NonceLength];
            Bytes.WriteUInt32BE(n, 0, streamIndex);
            Bytes.WriteUInt64BE(n, 4, chunk);
            return n;
        }

        internal static byte[] Aad(byte[] headerHash, uint streamIndex, ulong chunk, byte flags)
        {
            var a = new byte[HeaderHashLength + 4 + 8 + 1];
            Buffer.BlockCopy(headerHash, 0, a, 0, HeaderHashLength);
            Bytes.WriteUInt32BE(a, 32, streamIndex);
            Bytes.WriteUInt64BE(a, 36, chunk);
            a[44] = flags;
            return a;
        }

        private static void Validate(byte[] key, uint streamIndex, byte[] headerHash)
        {
            if (key == null || key.Length != AesGcm256.KeyLength) throw new ArgumentException("256-bit key required.", nameof(key));
            if (streamIndex > MaxStreamIndex) throw new ArgumentOutOfRangeException(nameof(streamIndex));
            if (headerHash == null || headerHash.Length != HeaderHashLength) throw new ArgumentException("32-byte header hash required.", nameof(headerHash));
        }

        /// <summary>Reads until the buffer is full or the stream ends; returns bytes read.</summary>
        public static int ReadFull(Stream s, byte[] buf)
        {
            int r = 0;
            while (r < buf.Length)
            {
                int k = s.Read(buf, r, buf.Length - r);
                if (k <= 0) break;
                r += k;
            }
            return r;
        }
    }
}
