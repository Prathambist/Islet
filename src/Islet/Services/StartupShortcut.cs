using System.IO;

namespace Islet
{
    /// <summary>
    /// "Launch at login" via the same Startup-folder shortcut install.ps1 creates.
    /// </summary>
    public static class StartupShortcut
    {
        static string ShortcutPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "Islet.lnk");

        public static bool IsEnabled => File.Exists(ShortcutPath);

        public static void Set(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    File.Delete(ShortcutPath);
                    return;
                }

                Create(ShortcutPath, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Islet.exe"), "Islet Startup");
            }
            catch (Exception ex)
            {
                Log.Error("Could not update the startup shortcut", ex);
            }
        }

        /// <summary>A .lnk at <paramref name="path"/> that starts <paramref name="exe"/>.</summary>
        public static void Create(string path, string exe, string description)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.Description = description;
            shortcut.Save();
        }
    }
}
