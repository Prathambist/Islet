using System.IO;

namespace Islet.Tests
{
    public sealed class LegacyMigrationTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "IsletMigrationTests", Guid.NewGuid().ToString("N"));

        public LegacyMigrationTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        [Fact]
        public void SettingsMoveToTheNewFolder()
        {
            string old = Path.Combine(root, "MusicIsland"), next = Path.Combine(root, "Islet");
            Directory.CreateDirectory(Path.Combine(old, "Shelf"));
            File.WriteAllText(Path.Combine(old, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(old, "Shelf", "note.txt"), "hi");

            Assert.True(LegacyMigration.MoveFolder(old, next));
            Assert.True(File.Exists(Path.Combine(next, "settings.json")));
            Assert.True(File.Exists(Path.Combine(next, "Shelf", "note.txt")));
        }

        [Fact]
        public void FillsInANewFolderThatHasNoSettingsYet()
        {
            string old = Path.Combine(root, "MusicIsland"), next = Path.Combine(root, "Islet");
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(old, "settings.json"), "old");
            File.WriteAllText(Path.Combine(old, "log.txt"), "old log");
            File.WriteAllText(Path.Combine(next, "log.txt"), "new log");

            Assert.True(LegacyMigration.MoveFolder(old, next));
            Assert.Equal("old", File.ReadAllText(Path.Combine(next, "settings.json")));
            Assert.Equal("new log", File.ReadAllText(Path.Combine(next, "log.txt")));
            Assert.False(Directory.Exists(old));
        }

        [Fact]
        public void NeverOverwritesSettingsAlreadyMoved()
        {
            string old = Path.Combine(root, "MusicIsland"), next = Path.Combine(root, "Islet");
            Directory.CreateDirectory(old);
            Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(old, "settings.json"), "old");
            File.WriteAllText(Path.Combine(next, "settings.json"), "new");

            Assert.False(LegacyMigration.MoveFolder(old, next));
            Assert.Equal("new", File.ReadAllText(Path.Combine(next, "settings.json")));
            Assert.False(LegacyMigration.MoveFolder(Path.Combine(root, "missing"), Path.Combine(root, "other")));
        }

        [Theory]
        [InlineData(@"C:\Users\a\AppData\Local\MusicIsland\", @"C:\Users\a\AppData\Local\MusicIsland", true)]
        [InlineData(@"C:\Users\a\AppData\Local\musicisland", @"C:\Users\a\AppData\Local\MusicIsland", true)]
        [InlineData(@"C:\Users\a\AppData\Local\Islet\", @"C:\Users\a\AppData\Local\MusicIsland", false)]
        [InlineData(@"C:\dev\Islet\bin\Debug\", @"C:\Users\a\AppData\Local\MusicIsland", false)]
        public void OnlyTheOldInstallFolderCounts(string baseDirectory, string oldInstall, bool expected) =>
            Assert.Equal(expected, LegacyMigration.IsOldInstall(baseDirectory, oldInstall));
    }
}
