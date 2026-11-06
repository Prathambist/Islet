using System.Diagnostics;
using System.IO;

namespace Islet
{
    /// <summary>
    /// Islet used to be called MusicIsland. This moves an old install, and its settings,
    /// over to the new name, so nobody has to reinstall or set things up again.
    ///
    /// An old install updates itself the way it always has: it unpacks the newest release
    /// over its own folder and starts MusicIsland.exe from it. The release includes a tiny
    /// MusicIsland.exe for that (see Islet.LegacyLauncher), which starts Islet. Islet then
    /// copies itself to its own folder, swaps the shortcuts, and starts again from there;
    /// that copy removes the old folder once the first one has exited.
    /// </summary>
    public static class LegacyMigration
    {
        public const string OldName = "MusicIsland";

        const string AfterArgument = "--after";
        const string RemoveOldArgument = "--remove-old-install";

        static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static string Programs => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        static string Startup => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

        public static string OldInstallDir => Path.Combine(LocalAppData, OldName);

        /// <summary>
        /// Settings, the timer, the shelf and the log, moved the first time Islet starts.
        /// Run before anything reads the settings; it works out the folders itself so it
        /// doesn't load them early.
        /// </summary>
        public static void MoveSettings() =>
            MoveFolder(Path.Combine(RoamingAppData, OldName), Path.Combine(RoamingAppData, "Islet"));

        /// <summary>
        /// Moves the old folder over, unless the new one already has its own settings.
        /// A new folder holding only, say, a log is filled in around what's there.
        /// </summary>
        /// <returns>Whether anything moved.</returns>
        internal static bool MoveFolder(string from, string to)
        {
            if (!Directory.Exists(from) || File.Exists(Path.Combine(to, "settings.json")))
                return false;

            if (!Directory.Exists(to))
            {
                try
                {
                    Directory.Move(from, to);
                    return true;
                }
                catch
                {
                    // Another drive, or a file still open: copy instead
                }
            }

            try
            {
                CopyFolder(from, to, skip: null, overwrite: false);
            }
            catch
            {
                return false;
            }

            try
            {
                Directory.Delete(from, recursive: true);
            }
            catch
            {
                // Something still has a file open; the copy is what counts
            }

            return true;
        }

        static void CopyFolder(string from, string to, string? skip, bool overwrite = true)
        {
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                if (skip != null && string.Equals(Path.GetFileName(file), skip, StringComparison.OrdinalIgnoreCase))
                    continue;

                string target = Path.Combine(to, Path.GetRelativePath(from, file));

                if (!overwrite && File.Exists(target))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite);
            }
        }

        internal static bool IsOldInstall(string baseDirectory, string oldInstallDir) =>
            string.Equals(
                Path.GetFullPath(baseDirectory).TrimEnd('\\'),
                Path.GetFullPath(oldInstallDir).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Run from an old install's folder: copies this app to its new folder, points the
        /// shortcuts there, and starts it.
        /// </summary>
        /// <returns>True when the new copy has been started and this one should exit at once.</returns>
        public static bool MoveInstall()
        {
            string here = AppContext.BaseDirectory;

            if (!IsOldInstall(here, OldInstallDir))
                return false;

            try
            {
                string newDir = UpdateService.InstallDir;
                string exe = Path.Combine(newDir, "Islet.exe");

                // The stand-in is only needed in the old folder
                CopyFolder(here, newDir, skip: OldName + ".exe");
                MoveShortcut(Programs, exe, "Islet");
                MoveShortcut(Startup, exe, "Islet Startup");

                Process.Start(new ProcessStartInfo(exe, $"{AfterArgument} {Environment.ProcessId} {RemoveOldArgument}")
                {
                    UseShellExecute = false,
                    WorkingDirectory = newDir
                });

                Log.Info($"Moved from {here} to {newDir}");
                return true;
            }
            catch (Exception ex)
            {
                // Keeps running from the old folder; it can try again next start
                Log.Error("Moving to the new install folder failed", ex);
                return false;
            }
        }

        // Only shortcuts that were there before: someone who turned off launching at login keeps it off
        static void MoveShortcut(string folder, string exe, string description)
        {
            string old = Path.Combine(folder, OldName + ".lnk");

            if (!File.Exists(old))
                return;

            StartupShortcut.Create(Path.Combine(folder, "Islet.lnk"), exe, description);
            File.Delete(old);
        }

        /// <summary>
        /// The new copy's side of a move: waits for the old copy to exit, so the
        /// single-instance check doesn't mistake it for a second island, then removes
        /// the old folder.
        /// </summary>
        public static void FinishMove(string[] args)
        {
            int at = Array.IndexOf(args, AfterArgument);

            if (at < 0 || at + 1 >= args.Length || !int.TryParse(args[at + 1], out int pid))
                return;

            try
            {
                using var previous = Process.GetProcessById(pid);
                previous.WaitForExit(15_000);
            }
            catch
            {
                // Already gone
            }

            if (!args.Contains(RemoveOldArgument) || !Directory.Exists(OldInstallDir))
                return;

            // Its files can stay locked for a moment after it exits
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    Directory.Delete(OldInstallDir, recursive: true);
                    return;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        }
    }
}
