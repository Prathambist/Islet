using System.IO;
using System.Runtime.CompilerServices;

namespace Islet.Tests
{
    static class TestData
    {
        /// <summary>
        /// Before any test runs: Islet's data folder (settings, the log) points at a scratch one,
        /// so tests never read the real settings or write into the real log.
        /// </summary>
        [ModuleInitializer]
        internal static void UseScratchFolder() =>
            Environment.SetEnvironmentVariable("ISLET_DATA",
                Path.Combine(Path.GetTempPath(), "IsletTests", Environment.ProcessId.ToString()));
    }
}
