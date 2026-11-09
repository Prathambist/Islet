using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows Script Host isn't available.");
            object shell = Activator.CreateInstance(shellType)!;
            object? shortcut = null;

            try
            {
                // Late-bound calls through the COM object's own dispatch interface
                shortcut = Call(shell, "CreateShortcut", BindingFlags.InvokeMethod, path)!;
                Call(shortcut, "TargetPath", BindingFlags.SetProperty, exe);
                Call(shortcut, "WorkingDirectory", BindingFlags.SetProperty, Path.GetDirectoryName(exe) ?? "");
                Call(shortcut, "Description", BindingFlags.SetProperty, description);
                Call(shortcut, "Save", BindingFlags.InvokeMethod);
            }
            finally
            {
                if (shortcut != null)
                    Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        static object? Call(object target, string name, BindingFlags kind, params object[] args) =>
            target.GetType().InvokeMember(name, kind, null, target, args);
    }
}
