using Microsoft.Data.Sqlite;
using ParvNegar.Storage;
using ParvNegar.Tests.Shared;
using Xunit;

namespace ParvNegar.Storage.Tests
{
    /// <summary>Pins the raw SQLite3MC pragma responses that the storage contract relies on.</summary>
    public class ProbeTests
    {
        [Fact]
        public void Cipher_pragmas_answer_as_the_contract_expects()
        {
            TestEnvironment.AssertExpectedArchitecture();
            EncryptedDatabase.InitializeProvider();
            using (var d = new TempDir("probe"))
            using (var c = new SqliteConnection(EncryptedDatabase.ConnectionStringFor(d.File("p.db"), SqliteOpenMode.ReadWriteCreate)))
            {
                c.Open();
                Assert.Equal("SQLite3 Multiple Ciphers 2.7.1", EncryptedDatabase.Scalar(c, "SELECT sqlite3mc_version();"));
                Assert.Equal("3.54.1", EncryptedDatabase.Scalar(c, "SELECT sqlite_version();"));
                Assert.Equal("chacha20", EncryptedDatabase.Scalar(c, "PRAGMA cipher='chacha20';"));
                Assert.Equal("64007", EncryptedDatabase.Scalar(c, "PRAGMA kdf_iter=64007;"));
                Assert.Equal("0", EncryptedDatabase.Scalar(c, "PRAGMA legacy=0;"));
                Assert.Equal("ok", EncryptedDatabase.Scalar(c, "PRAGMA hexkey='00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff';"));
            }
        }
    }
}
