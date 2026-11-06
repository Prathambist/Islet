using System.IO;
using System.Text.Json;

namespace MusicIsland
{
    /// <summary>
    /// Files parked on the island to drag somewhere else later.
    ///
    /// The shelf keeps paths, not copies, so parking a file costs nothing and a file
    /// moved or deleted elsewhere simply drops off. Only things that weren't files to
    /// begin with (an image or a link dragged from a web page) are saved, into the
    /// shelf's own folder, and those copies are deleted again when they leave the shelf.
    /// </summary>
    public sealed class FileShelf
    {
        public const int MaxItems = 20;

        readonly string folder;
        readonly List<string> items = new();

        public FileShelf(string folder)
        {
            this.folder = folder;
            Load();
        }

        /// <summary>Where the shelf's own copies go.</summary>
        public string OwnFolder => Path.Combine(folder, "Shelf");

        string ListPath => Path.Combine(folder, "shelf.json");

        /// <summary>Newest last.</summary>
        public IReadOnlyList<string> Items => items;

        public event Action? Changed;

        /// <summary>Adds what exists and isn't already there; the oldest go once it's full.</summary>
        /// <returns>The paths that were added.</returns>
        public IReadOnlyList<string> Add(IEnumerable<string> paths)
        {
            var added = new List<string>();

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                    continue;

                string full = Path.GetFullPath(path);

                if (items.Any(p => SamePath(p, full)))
                    continue;

                items.Add(full);
                added.Add(full);
            }

            while (items.Count > MaxItems)
            {
                Forget(items[0]);
                items.RemoveAt(0);
            }

            if (added.Count > 0)
                SaveAndNotify();

            return added;
        }

        public void Remove(string path)
        {
            int index = items.FindIndex(p => SamePath(p, path));

            if (index < 0)
                return;

            Forget(items[index]);
            items.RemoveAt(index);
            SaveAndNotify();
        }

        public void Clear()
        {
            if (items.Count == 0)
                return;

            foreach (var path in items)
                Forget(path);

            items.Clear();
            SaveAndNotify();
        }

        /// <summary>Drops files that were moved or deleted since they were parked.</summary>
        public bool Prune()
        {
            int removed = items.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));

            if (removed > 0)
                SaveAndNotify();

            return removed > 0;
        }

        /// <summary>Saves something that isn't a file yet (an image, a link) into the shelf's folder.</summary>
        /// <returns>Its new path.</returns>
        public string SaveOwn(string fileName, Action<Stream> write)
        {
            Directory.CreateDirectory(OwnFolder);
            string path = UniquePath(OwnFolder, fileName);

            using (var stream = File.Create(path))
                write(stream);

            return path;
        }

        public bool IsOwn(string path) =>
            Path.GetFullPath(path).StartsWith(Path.GetFullPath(OwnFolder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>"name.png", then "name (2).png", and so on.</summary>
        public static string UniquePath(string directory, string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            string name = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string path = Path.Combine(directory, fileName);

            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(directory, $"{name} ({n}){extension}");

            return path;
        }

        // The shelf's own copies go when they leave it; your files are never touched
        void Forget(string path)
        {
            try
            {
                if (IsOwn(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // In use somewhere; it's only a leftover copy
            }
        }

        static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        void Load()
        {
            try
            {
                if (File.Exists(ListPath))
                    items.AddRange(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(ListPath)) ?? new());
            }
            catch
            {
            }

            items.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        }

        void SaveAndNotify()
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(ListPath, JsonSerializer.Serialize(items));
            }
            catch
            {
            }

            Changed?.Invoke();
        }
    }
}
