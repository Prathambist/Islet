using System.IO;

namespace Islet.Tests
{
    public sealed class ClipboardHistoryTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "IsletClipboardTests", Guid.NewGuid().ToString("N"));
        DateTime clock = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        public ClipboardHistoryTests() => Directory.CreateDirectory(root);

        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        ClipboardHistory New(TimeSpan? retention = null) =>
            new(root, () => clock) { Retention = retention ?? TimeSpan.FromDays(7) };

        [Fact]
        public void KeepsCopiedTextNewestFirst()
        {
            var history = New();

            Assert.True(history.Add("first"));
            clock = clock.AddMinutes(1);
            Assert.True(history.Add("second"));

            Assert.Equal(new[] { "second", "first" }, history.Items.Select(e => e.Text));
        }

        [Fact]
        public void IgnoresEmptyAndHugeCopies()
        {
            var history = New();

            Assert.False(history.Add(null));
            Assert.False(history.Add("   \r\n"));
            Assert.False(history.Add(new string('x', ClipboardHistory.MaxLength + 1)));
            Assert.Empty(history.Items);
        }

        [Fact]
        public void CopyingAgainMovesItUpWithoutADuplicate()
        {
            var history = New();
            history.Add("a");
            clock = clock.AddMinutes(1);
            history.Add("b");
            clock = clock.AddMinutes(1);
            history.Add("a");

            Assert.Equal(new[] { "a", "b" }, history.Items.Select(e => e.Text));
            Assert.Equal(clock, history.Items[0].CopiedAt);
        }

        [Fact]
        public void EntriesExpireAfterTheRetentionPeriod()
        {
            var history = New(TimeSpan.FromDays(7));
            history.Add("old");
            clock = clock.AddDays(3);
            history.Add("newer");

            clock = clock.AddDays(4).AddMinutes(1);
            Assert.True(history.Prune());

            Assert.Equal(new[] { "newer" }, history.Items.Select(e => e.Text));
            Assert.False(history.Prune());
        }

        [Fact]
        public void ShorteningTheRetentionDropsOlderEntries()
        {
            var history = New(TimeSpan.FromDays(30));
            history.Add("ten days old");
            clock = clock.AddDays(10);
            history.Add("today");

            history.Retention = TimeSpan.FromHours(24);
            history.Prune();

            Assert.Equal(new[] { "today" }, history.Items.Select(e => e.Text));
        }

        [Fact]
        public void PinnedEntriesNeverExpireAndSurviveClear()
        {
            var history = New(TimeSpan.FromHours(24));
            history.Add("keep me");
            history.Add("let me go");
            history.SetPinned(history.Items.Single(e => e.Text == "keep me").Id, true);

            clock = clock.AddDays(40);
            history.Prune();
            Assert.Equal(new[] { "keep me" }, history.Items.Select(e => e.Text));

            history.Add("another");
            history.Clear();
            Assert.Equal(new[] { "keep me" }, history.Items.Select(e => e.Text));

            history.Clear(includePinned: true);
            Assert.Empty(history.Items);
        }

        [Fact]
        public void UnpinningAnOldEntryLetsItExpire()
        {
            var history = New(TimeSpan.FromHours(24));
            history.Add("pinned");
            string id = history.Items[0].Id;
            history.SetPinned(id, true);

            clock = clock.AddDays(2);
            history.SetPinned(id, false);

            Assert.Empty(history.Items);
        }

        [Fact]
        public void SurvivesARestartEncrypted()
        {
            var history = New();
            history.Add("https://example.com/secret-link");
            history.SetPinned(history.Items[0].Id, true);

            string file = Path.Combine(root, "clipboard.dat");
            Assert.True(File.Exists(file));
            Assert.DoesNotContain("secret-link", File.ReadAllText(file));

            var reopened = New();
            var entry = Assert.Single(reopened.Items);
            Assert.Equal("https://example.com/secret-link", entry.Text);
            Assert.True(entry.Pinned);
        }

        [Fact]
        public void ADamagedFileStartsAfresh()
        {
            File.WriteAllText(Path.Combine(root, "clipboard.dat"), "not encrypted json");

            var history = New();

            Assert.Empty(history.Items);
            Assert.True(history.Add("fine"));
        }

        [Fact]
        public void SearchMatchesEveryWordInAnyCaseWithPinnedFirst()
        {
            var history = New();
            history.Add("git push origin main");
            clock = clock.AddMinutes(1);
            history.Add("Git Pull Origin main");
            clock = clock.AddMinutes(1);
            history.Add("npm install");
            history.SetPinned(history.Items.Single(e => e.Text == "git push origin main").Id, true);

            var found = history.Search("GIT  main");

            Assert.Equal(new[] { "git push origin main", "Git Pull Origin main" }, found.Select(e => e.Text));
            Assert.Equal(3, history.Search("").Count);
            Assert.Empty(history.Search("docker"));
        }

        [Fact]
        public void RemovesOneEntry()
        {
            var history = New();
            history.Add("a");
            history.Add("b");

            history.Remove(history.Items.Single(e => e.Text == "a").Id);

            Assert.Equal(new[] { "b" }, history.Items.Select(e => e.Text));
        }

        [Fact]
        public void KeepsTheNewestWhenFullButNeverDropsPinned()
        {
            var history = New(TimeSpan.Zero);
            history.Add("pinned oldest");
            history.SetPinned(history.Items[0].Id, true);

            for (int i = 0; i < ClipboardHistory.MaxItems + 5; i++)
            {
                clock = clock.AddSeconds(1);
                history.Add($"item {i}");
            }

            Assert.Equal(ClipboardHistory.MaxItems + 1, history.Items.Count);
            Assert.Contains(history.Items, e => e.Text == "pinned oldest");
            Assert.DoesNotContain(history.Items, e => e.Text == "item 0");
            Assert.Equal($"item {ClipboardHistory.MaxItems + 4}", history.Items[0].Text);
        }

        [Fact]
        public void NewSettingsKeepAWeekByDefault()
        {
            var settings = new AppSettings();

            Assert.True(settings.ClipboardHistoryEnabled);
            Assert.Equal(168, settings.ClipboardRetentionHours);
            Assert.Contains(settings.ClipboardRetentionHours, SettingsWindow.ClipboardRetentionChoices);
        }

        [Theory]
        [InlineData(0.5, "Just now")]
        [InlineData(5, "5 min ago")]
        [InlineData(180, "3 h ago")]
        [InlineData(60 * 30, "Yesterday")]
        [InlineData(60 * 24 * 5, "5 days ago")]
        public void SaysHowLongAgo(double minutes, string expected) =>
            Assert.Equal(expected, ClipboardHistoryWindow.Ago(TimeSpan.FromMinutes(minutes)));

        [Fact]
        public void PreviewsKeepToOneLine() =>
            Assert.Equal("a ⏎ b c", ClipboardHistoryWindow.Preview("  a\r\nb\nc  "));
    }
}
