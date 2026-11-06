using System.IO;

namespace MusicIsland.Tests
{
    public sealed class FileShelfTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "MusicIslandShelfTests", Guid.NewGuid().ToString("N"));

        public FileShelfTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        string NewFile(string name)
        {
            string path = Path.Combine(root, name);
            File.WriteAllText(path, name);
            return path;
        }

        [Fact]
        public void AddsOnlyNewFilesThatExist()
        {
            var shelf = new FileShelf(Path.Combine(root, "app"));
            string a = NewFile("a.txt");

            var added = shelf.Add(new[] { a, a.ToUpperInvariant(), Path.Combine(root, "missing.txt") });

            Assert.Single(added);
            Assert.Single(shelf.Items);
            Assert.Empty(shelf.Add(new[] { a }));
        }

        [Fact]
        public void KeepsTheNewestWhenFull()
        {
            var shelf = new FileShelf(Path.Combine(root, "app"));
            var files = Enumerable.Range(0, FileShelf.MaxItems + 3).Select(i => NewFile($"{i}.txt")).ToList();

            shelf.Add(files);

            Assert.Equal(FileShelf.MaxItems, shelf.Items.Count);
            Assert.Equal(files[^1], shelf.Items[^1]);
            Assert.DoesNotContain(files[0], shelf.Items);
        }

        [Fact]
        public void RemembersBetweenRunsAndForgetsMovedFiles()
        {
            string app = Path.Combine(root, "app");
            string a = NewFile("a.txt"), b = NewFile("b.txt");

            new FileShelf(app).Add(new[] { a, b });
            File.Delete(a);

            var reopened = new FileShelf(app);

            Assert.Equal(new[] { b }, reopened.Items);
        }

        [Fact]
        public void RemovingNeverDeletesYourFiles()
        {
            var shelf = new FileShelf(Path.Combine(root, "app"));
            string a = NewFile("a.txt");

            shelf.Add(new[] { a });
            shelf.Remove(a);

            Assert.Empty(shelf.Items);
            Assert.True(File.Exists(a));
        }

        [Fact]
        public void ItsOwnCopiesGoWhenTheyLeave()
        {
            var shelf = new FileShelf(Path.Combine(root, "app"));
            string saved = shelf.SaveOwn("link.url", s => s.WriteByte(1));

            Assert.True(shelf.IsOwn(saved));
            shelf.Add(new[] { saved });
            shelf.Clear();

            Assert.False(File.Exists(saved));
        }

        [Fact]
        public void SameNamesGetNumbered()
        {
            NewFile("photo.png");

            Assert.Equal(Path.Combine(root, "photo (2).png"), FileShelf.UniquePath(root, "photo.png"));
            Assert.Equal(Path.Combine(root, "a_b.txt"), FileShelf.UniquePath(root, "a/b.txt"));
        }
    }
}
