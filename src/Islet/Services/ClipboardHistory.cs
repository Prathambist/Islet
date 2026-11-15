using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Islet
{
    public sealed class ClipboardEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = "";

        /// <summary>When it was last copied, in UTC. Copying the same text again moves it up.</summary>
        public DateTime CopiedAt { get; set; }

        /// <summary>Pinned entries never expire and survive Clear.</summary>
        public bool Pinned { get; set; }
    }

    /// <summary>
    /// Text you've copied, newest first, kept for as long as Settings says and then dropped.
    ///
    /// Saved to clipboard.dat in Islet's folder, encrypted for the current Windows user
    /// (DPAPI), so it survives restarts but can't be read from another account.
    /// The clock is passed in, so expiry is easy to test.
    /// </summary>
    public sealed class ClipboardHistory
    {
        /// <summary>Unpinned entries kept at most; the oldest go first.</summary>
        public const int MaxItems = 500;

        /// <summary>Longer copies (a whole file, a log) aren't kept.</summary>
        public const int MaxLength = 100_000;

        readonly string folder;
        readonly Func<DateTime> now;
        readonly List<ClipboardEntry> items = new();

        public ClipboardHistory(string folder, Func<DateTime>? utcNow = null)
        {
            this.folder = folder;
            now = utcNow ?? (() => DateTime.UtcNow);
            Load();
        }

        string FilePath => Path.Combine(folder, "clipboard.dat");

        /// <summary>Newest first.</summary>
        public IReadOnlyList<ClipboardEntry> Items => items;

        public event Action? Changed;

        /// <summary>How long unpinned entries are kept; zero or less keeps them forever.</summary>
        public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

        /// <summary>Adds copied text, or moves it to the top if it's already there.</summary>
        /// <returns>False when it was empty or too long to keep.</returns>
        public bool Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
                return false;

            var existing = items.Find(e => e.Text == text);

            if (existing != null)
            {
                items.Remove(existing);
                existing.CopiedAt = now();
                items.Insert(0, existing);
            }
            else
            {
                items.Insert(0, new ClipboardEntry { Text = text, CopiedAt = now() });
            }

            Trim();
            SaveAndNotify();
            return true;
        }

        public void Remove(string id)
        {
            if (items.RemoveAll(e => e.Id == id) > 0)
                SaveAndNotify();
        }

        public void SetPinned(string id, bool pinned)
        {
            var entry = items.Find(e => e.Id == id);

            if (entry == null || entry.Pinned == pinned)
                return;

            entry.Pinned = pinned;

            // Unpinning something old lets it expire as usual
            Prune(save: false);
            SaveAndNotify();
        }

        /// <summary>Forgets everything except pinned entries, unless those go too.</summary>
        public void Clear(bool includePinned = false)
        {
            if (items.RemoveAll(e => includePinned || !e.Pinned) > 0)
                SaveAndNotify();
        }

        /// <summary>Drops unpinned entries older than the retention period.</summary>
        /// <returns>True if anything was dropped.</returns>
        public bool Prune() => Prune(save: true);

        bool Prune(bool save)
        {
            if (Retention <= TimeSpan.Zero)
                return false;

            var cutoff = now() - Retention;
            int removed = items.RemoveAll(e => !e.Pinned && e.CopiedAt < cutoff);

            if (removed > 0 && save)
                SaveAndNotify();

            return removed > 0;
        }

        /// <summary>Pinned first, then newest; every word of the query has to appear, in any case.</summary>
        public IReadOnlyList<ClipboardEntry> Search(string? query)
        {
            var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            return items
                .Where(e => words.All(w => e.Text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.Pinned)
                .ThenByDescending(e => e.CopiedAt)
                .ToList();
        }

        void Trim()
        {
            // Oldest unpinned past the cap
            while (items.Count(e => !e.Pinned) > MaxItems)
                items.RemoveAt(items.FindLastIndex(e => !e.Pinned));
        }

        void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                    items.AddRange(JsonSerializer.Deserialize<List<ClipboardEntry>>(Encoding.UTF8.GetString(plain)) ?? new());
                    items.Sort((a, b) => b.CopiedAt.CompareTo(a.CopiedAt));
                }
            }
            catch
            {
                // Unreadable (another user's, or damaged): start afresh
                items.Clear();
            }
        }

        void SaveAndNotify()
        {
            try
            {
                Directory.CreateDirectory(folder);

                if (items.Count == 0)
                {
                    File.Delete(FilePath);
                }
                else
                {
                    byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items));
                    File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
                }
            }
            catch
            {
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Clipboard formats password managers (and Windows' own clipboard history rules) use to say
        /// "don't keep this". See "Cloud clipboard and clipboard history formats" in the Windows docs.
        /// </summary>
        public static readonly string[] PrivateFormats =
        {
            "ExcludeClipboardContentFromMonitorProcessing",
            "Clipboard Viewer Ignore"
        };

        /// <summary>The format whose value 0 means "keep out of clipboard history".</summary>
        public const string CanIncludeFormat = "CanIncludeInClipboardHistory";
    }
}
