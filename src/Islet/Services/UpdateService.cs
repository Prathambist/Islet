using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Islet
{
    /// <summary>
    /// Checks GitHub Releases for a newer build and installs it in place.
    /// </summary>
    public static class UpdateService
    {
        const string Repo = "Prathambist/Islet";

        public sealed record Release(Version Version, string Tag, string ZipUrl, string PageUrl, string SignatureUrl)
        {
            /// <summary>What changed, a line each: the release's own notes, or else its commits' titles.</summary>
            public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
        }

        /// <summary>At most this many lines of notes are shown.</summary>
        public const int NotesShown = 8;

        static readonly HttpClient http = CreateClient();

        static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Islet-Updater");
            return client;
        }

        public static Version CurrentVersion => Normalize(
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

        public static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Islet");

        /// <summary>
        /// Only the copy set up by install.ps1 replaces itself; dev builds just
        /// open the release page.
        /// </summary>
        public static bool CanSelfUpdate =>
            string.Equals(
                Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),
                Path.GetFullPath(InstallDir).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

        public static async Task<Release?> CheckAsync()
        {
            try
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(
                    $"https://api.github.com/repos/{Repo}/releases/latest"));

                var release = ParseRelease(doc.RootElement, CurrentVersion);

                // Releases here usually only link to the full changelog; the commits say what changed
                if (release != null && release.Notes.Count == 0)
                    release = release with { Notes = await CommitNotesAsync(release.Tag) };

                return release;
            }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                return null;
            }
        }

        static async Task<IReadOnlyList<string>> CommitNotesAsync(string tag)
        {
            try
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(
                    $"https://api.github.com/repos/{Repo}/compare/v{CurrentVersion}...{Uri.EscapeDataString(tag)}"));

                return NotesFromCommits(doc.RootElement);
            }
            catch (Exception ex)
            {
                // A build that isn't a release has no tag to compare from; the notes are just left out
                Log.Info($"No commit notes for {tag}: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>The first line of each commit in a GitHub compare response, newest first, merges left out.</summary>
        internal static IReadOnlyList<string> NotesFromCommits(JsonElement compare)
        {
            if (!compare.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return commits.EnumerateArray()
                .Select(c => c.GetProperty("commit").GetProperty("message").GetString() ?? "")
                .Select(m => m.Split('\n')[0].Trim())
                .Where(m => m.Length > 0 && !m.StartsWith("Merge ", StringComparison.Ordinal))
                .Reverse()
                .Distinct()
                .Take(NotesShown)
                .ToList();
        }

        /// <summary>
        /// A release's own notes as plain lines: headings, bullets, bold and links stripped, and the
        /// "Full Changelog" link GitHub adds left out.
        /// </summary>
        internal static IReadOnlyList<string> NotesFromMarkdown(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return Array.Empty<string>();

            var lines = new List<string>();

            foreach (var raw in body.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line.StartsWith('#') || line.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase))
                    continue;

                line = Regex.Replace(line, @"^([-*+]|\d+\.)\s+", "");
                line = Regex.Replace(line, @"\[([^\]]*)\]\([^)]*\)", "$1");
                line = line.Replace("**", "").Replace("__", "").Replace("`", "");

                // GitHub's generated notes: "Fix something by @someone in https://…"
                line = Regex.Replace(line, @"\s+by @\S+ in https?://\S+$", "");

                if (line.Length > 0)
                    lines.Add(line);
            }

            return lines.Take(NotesShown).ToList();
        }

        /// <summary>
        /// A GitHub "latest release" response, if it is newer than <paramref name="current"/>
        /// and has a .zip to install with its signature beside it. Unsigned releases aren't
        /// offered at all, so a tampered or half-published one can't be installed.
        /// </summary>
        internal static Release? ParseRelease(JsonElement root, Version current)
        {
            string tag = root.GetProperty("tag_name").GetString() ?? "";

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                return null;

            string? zipName = null, zip = null;
            var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string? url = asset.GetProperty("browser_download_url").GetString();

                if (url == null)
                    continue;

                urls[name] = url;

                if (zip == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipName = name;
                    zip = url;
                }
            }

            if (zip == null || Normalize(version) <= Normalize(current))
                return null;

            if (!urls.TryGetValue(zipName + ".sig", out var signature))
            {
                Log.Info($"Release {tag} has no signature, so it isn't offered");
                return null;
            }

            return new Release(Normalize(version), tag, zip,
                root.TryGetProperty("html_url", out var page) && page.GetString() is string pageUrl
                    ? pageUrl
                    : $"https://github.com/{Repo}/releases/latest",
                signature)
            {
                Notes = NotesFromMarkdown(root.TryGetProperty("body", out var body) ? body.GetString() : null)
            };
        }

        /// <summary>
        /// Downloads and unpacks the release, then hands over to a small script that
        /// waits for this process to exit, copies the files and restarts the app.
        /// The caller should shut the app down right after this returns.
        /// </summary>
        public static async Task InstallAsync(Release release)
        {
            if (!CanSelfUpdate)
            {
                Process.Start(new ProcessStartInfo { FileName = release.PageUrl, UseShellExecute = true });
                return;
            }

            string work = Path.Combine(Path.GetTempPath(), "Islet-update");

            if (Directory.Exists(work))
                Directory.Delete(work, true);

            string unpacked = Path.Combine(work, "files");
            Directory.CreateDirectory(unpacked);

            string zipPath = Path.Combine(work, "update.zip");

            await using (var download = await http.GetStreamAsync(release.ZipUrl))
            await using (var file = File.Create(zipPath))
                await download.CopyToAsync(file);

            // Nothing is unpacked, let alone installed, unless it was signed with the release key
            string signature = await http.GetStringAsync(release.SignatureUrl);

            if (!ReleaseSignature.Verify(await File.ReadAllBytesAsync(zipPath), signature))
            {
                Directory.Delete(work, true);
                throw new InvalidOperationException($"Update {release.Tag} isn't signed with Islet's release key, so it wasn't installed.");
            }

            ZipFile.ExtractToDirectory(zipPath, unpacked);

            if (!File.Exists(Path.Combine(unpacked, "Islet.exe")))
                throw new InvalidOperationException("The update package doesn't contain Islet.exe.");

            int pid = Environment.ProcessId;
            string script = Path.Combine(work, "update.cmd");

            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"robocopy \"{unpacked}\" \"{InstallDir}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS >nul\r\n" +
                $"start \"\" \"{Path.Combine(InstallDir, "Islet.exe")}\"\r\n");

            Log.Info($"Installing update {release.Tag}");

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }
}
