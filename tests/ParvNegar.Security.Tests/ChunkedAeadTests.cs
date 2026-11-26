using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ParvNegar.Security;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Security.Tests
{
    public class ChunkedAeadTests
    {
        private static readonly byte[] Hh = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        private const int C = ChunkedAead.ChunkSize;

        public ChunkedAeadTests() { TestEnvironment.AssertExpectedArchitecture(); }

        private static byte[] Enc(byte[] key, byte[] data, uint stream = 3)
        {
            var o = new MemoryStream();
            ChunkedAead.Encrypt(key, stream, Hh, new MemoryStream(data), o);
            return o.ToArray();
        }

        private static byte[] Dec(byte[] key, byte[] blob, uint stream = 3, byte[] hh = null, long max = long.MaxValue)
        {
            var o = new MemoryStream();
            var input = new MemoryStream(blob);
            ChunkedAead.Decrypt(key, stream, hh ?? Hh, input, o, max);
            if (input.Position != input.Length) throw new MalformedDataException("trailing");
            return o.ToArray();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(C - 1)]
        [InlineData(C)]
        [InlineData(C + 1)]
        [InlineData(3 * C)]
        [InlineData(3 * C + 17)]
        public void Roundtrip_sizes(int size)
        {
            var key = CryptoRandom.Bytes(32);
            var data = size == 0 ? new byte[0] : CryptoRandom.Bytes(size);
            var blob = Enc(key, data);
            int chunks = size == 0 ? 1 : (size + C - 1) / C;
            Assert.Equal(size + chunks * (5 + 16), blob.Length);
            Assert.Equal(data, Dec(key, blob));
        }

        [Fact]
        public void Nonce_layout_is_stream_u32be_then_chunk_u64be()
        {
            Assert.Equal("010203040a0b0c0d0e0f1011", Bytes.ToHex(ChunkedAead.Nonce(0x01020304, 0x0A0B0C0D0E0F1011UL)));
        }

        [Fact]
        public void Aad_layout_is_hash_stream_chunk_flags()
        {
            var a = ChunkedAead.Aad(Hh, 2, 5, 1);
            Assert.Equal(45, a.Length);
            Assert.Equal(Bytes.ToHex(Hh) + "00000002" + "0000000000000005" + "01", Bytes.ToHex(a));
        }

        [Fact]
        public void Byte_flips_across_the_stream_are_rejected()
        {
            var key = CryptoRandom.Bytes(32);
            var blob = Enc(key, CryptoRandom.Bytes(C + 100));
            foreach (var i in Enumerable.Range(0, blob.Length).Where(i => i < 40 || i % 997 == 0 || i > blob.Length - 40))
            {
                var t = (byte[])blob.Clone(); t[i] ^= 0x80;
                Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Dec(key, t));
            }
        }

        [Fact]
        public void Truncation_at_chunk_boundary_and_inside_chunk_is_rejected()
        {
            var key = CryptoRandom.Bytes(32);
            var blob = Enc(key, CryptoRandom.Bytes(2 * C + 5));
            int r = 5 + C + 16;
            Assert.Throws<MalformedDataException>(() => Dec(key, blob.Take(r).ToArray()));
            Assert.Throws<MalformedDataException>(() => Dec(key, blob.Take(2 * r).ToArray()));
            Assert.Throws<MalformedDataException>(() => Dec(key, blob.Take(blob.Length - 1).ToArray()));
            Assert.Throws<MalformedDataException>(() => Dec(key, new byte[0]));
        }

        [Fact]
        public void Reorder_duplicate_cross_stream_and_header_substitution_are_rejected()
        {
            var key = CryptoRandom.Bytes(32);
            var blob = Enc(key, CryptoRandom.Bytes(3 * C + 10));
            int r = 5 + C + 16;
            var rec = Enumerable.Range(0, 3).Select(i => blob.Skip(i * r).Take(r).ToArray()).ToList();
            var last = blob.Skip(3 * r).ToArray();
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Dec(key, rec[1].Concat(rec[0]).Concat(rec[2]).Concat(last).ToArray()));
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Dec(key, rec[0].Concat(rec[0]).Concat(rec[2]).Concat(last).ToArray()));
            Assert.Throws<AuthenticationFailedException>(() => Dec(key, blob, stream: 4));
            var hh2 = (byte[])Hh.Clone(); hh2[0] ^= 1;
            Assert.Throws<AuthenticationFailedException>(() => Dec(key, blob, hh: hh2));
            Assert.Throws<AuthenticationFailedException>(() => Dec(CryptoRandom.Bytes(32), blob));
        }

        [Fact]
        public void Malformed_lengths_and_flags_are_rejected_before_decryption()
        {
            var key = CryptoRandom.Bytes(32);
            var blob = Enc(key, CryptoRandom.Bytes(10));
            var badFlags = (byte[])blob.Clone(); badFlags[0] = 0x03;
            Assert.Throws<MalformedDataException>(() => Dec(key, badFlags));
            var huge = (byte[])blob.Clone(); Bytes.WriteUInt32BE(huge, 1, 0x7FFFFFFF);
            Assert.Throws<MalformedDataException>(() => Dec(key, huge));
            var tiny = (byte[])blob.Clone(); Bytes.WriteUInt32BE(tiny, 1, 3);
            Assert.Throws<MalformedDataException>(() => Dec(key, tiny));
            Assert.Throws<MalformedDataException>(() => Dec(key, Enc(key, CryptoRandom.Bytes(100)), max: 99));
            // A non-final chunk that is not full is structurally invalid.
            var two = Enc(key, CryptoRandom.Bytes(C + 1));
            var shortNonFinal = (byte[])two.Clone(); Bytes.WriteUInt32BE(shortNonFinal, 1, 100);
            Assert.Throws<MalformedDataException>(() => Dec(key, shortNonFinal));
        }

        [Fact]
        public void Trailing_data_after_final_chunk_is_detectable()
        {
            var key = CryptoRandom.Bytes(32);
            var blob = Enc(key, CryptoRandom.Bytes(10)).Concat(new byte[] { 0 }).ToArray();
            Assert.Throws<MalformedDataException>(() => Dec(key, blob));
        }

        [Fact]
        public void Nonces_are_unique_for_all_stream_chunk_pairs()
        {
            var seen = new HashSet<string>();
            for (uint s = 0; s < 64; s++)
                for (ulong c = 0; c < 256; c++)
                    Assert.True(seen.Add(Bytes.ToHex(ChunkedAead.Nonce(s, c))));
        }

        [Fact]
        public void Out_of_range_stream_index_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Enc(CryptoRandom.Bytes(32), new byte[1], ChunkedAead.MaxStreamIndex + 1));
        }
    }
}
