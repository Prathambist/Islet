using System.IO;

namespace Islet.Tests
{
    public sealed class FileLocksTests : IDisposable
    {
        readonly string folder = Path.Combine(Path.GetTempPath(), "IsletLockTests", Guid.NewGuid().ToString("N"));

        public FileLocksTests() => Directory.CreateDirectory(folder);

        public void Dispose()
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }

        [Fact]
        public void FindsTheProgramHoldingAFile()
        {
            string path = Path.Combine(folder, "report.txt");

            // This test itself holds the file open, like Excel would
            using (new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var lockers = FileLocks.Find(new[] { path }, includeSelf: true);
                Assert.Contains(lockers, l => l.ProcessId == Environment.ProcessId);

                // And through the folder it's in
                Assert.Contains(FileLocks.Find(new[] { folder }, includeSelf: true), l => l.ProcessId == Environment.ProcessId);
            }

            Assert.Empty(FileLocks.Find(new[] { path }, includeSelf: true));
        }

        [Fact]
        public void NothingToCheckFindsNothing()
        {
            Assert.Empty(FileLocks.Find(new[] { Path.Combine(folder, "missing.txt") }));
            Assert.Empty(FileLocks.Find(Array.Empty<string>()));
        }

        [Theory]
        [InlineData(1, LockerKind.App)]
        [InlineData(2, LockerKind.App)]
        [InlineData(3, LockerKind.Service)]
        [InlineData(4, LockerKind.Explorer)]
        [InlineData(5, LockerKind.Background)]
        [InlineData(1000, LockerKind.Critical)]
        public void SortsOutWhatKindOfProgramItIs(int type, LockerKind kind) =>
            Assert.Equal(kind, FileLocks.KindOf(type));
    }
}
