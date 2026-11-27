using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace ParvNegar.Tests.Shared
{
    /// <summary>Synthetic-only helpers shared by all test projects.</summary>
    public static class TestEnvironment
    {
        /// <summary>
        /// Fails when the test process architecture differs from PARVNEGAR_EXPECTED_ARCH (x86/x64),
        /// so a mis-configured run cannot report x86 results from an x64 process.
        /// </summary>
        public static void AssertExpectedArchitecture()
        {
            var expected = Environment.GetEnvironmentVariable("PARVNEGAR_EXPECTED_ARCH");
            var actual = Environment.Is64BitProcess ? "x64" : "x86";
            if (!string.IsNullOrEmpty(expected)) Assert.Equal(expected, actual);
        }

        public static string Runtime => RuntimeInformation.FrameworkDescription + " " + (Environment.Is64BitProcess ? "x64" : "x86");

        public static string NewTempDir(string prefix)
        {
            var d = Path.Combine(Path.GetTempPath(), "parvnegar-tests", prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public sealed class TempDir : IDisposable
    {
        public TempDir(string prefix) { Path = TestEnvironment.NewTempDir(prefix); }
        public string Path { get; }
        public string File(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() => TestEnvironment.TryDelete(Path);
    }
}
