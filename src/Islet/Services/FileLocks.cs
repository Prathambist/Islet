using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Islet
{
    public enum LockerKind
    {
        /// <summary>An app with a window, which can be asked to close.</summary>
        App,
        /// <summary>File Explorer itself, often its preview pane or a folder window.</summary>
        Explorer,
        /// <summary>A Windows service.</summary>
        Service,
        /// <summary>A program with no window to close, like a console tool or a background helper.</summary>
        Background,
        /// <summary>Part of Windows that can't be closed.</summary>
        Critical
    }

    public sealed record FileLocker(int ProcessId, string Name, string? ExePath, LockerKind Kind);

    /// <summary>
    /// Which programs have a file open, asked the way installers ask: through Windows' Restart
    /// Manager. A folder is checked by the files inside it, since that's what's usually held.
    /// </summary>
    public static class FileLocks
    {
        // A huge folder would take a while to register; this many files covers real cases
        const int MostFiles = 2000;

        public static IReadOnlyList<FileLocker> Find(IEnumerable<string> paths, bool includeSelf = false)
        {
            var files = Expand(paths).Take(MostFiles).ToArray();

            if (files.Length == 0)
                return Array.Empty<FileLocker>();

            var key = new StringBuilder(33);

            if (RmStartSession(out uint session, 0, key) != 0)
                return Array.Empty<FileLocker>();

            try
            {
                if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != 0)
                    return Array.Empty<FileLocker>();

                uint count = 0;
                RM_PROCESS_INFO[]? infos = null;
                int result;

                // Asked twice: once for how many, then for the list
                do
                {
                    uint reasons = 0;
                    infos = count == 0 ? null : new RM_PROCESS_INFO[count];
                    result = RmGetList(session, out uint needed, ref count, infos, ref reasons);

                    if (result == ErrorMoreData)
                        count = needed;
                }
                while (result == ErrorMoreData);

                if (result != 0 || infos == null)
                    return Array.Empty<FileLocker>();

                int self = Environment.ProcessId;

                return infos.Take((int)count)
                    .Where(i => includeSelf || i.Process.dwProcessId != self)
                    .GroupBy(i => i.Process.dwProcessId)
                    .Select(g => Describe(g.First()))
                    .OrderBy(l => l.Kind)
                    .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                RmEndSession(session);
            }
        }

        static IEnumerable<string> Expand(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    yield return path;
                }
                else if (Directory.Exists(path))
                {
                    IEnumerable<string> inside;

                    try
                    {
                        inside = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true
                        });
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var file in inside)
                        yield return file;
                }
            }
        }

        static FileLocker Describe(RM_PROCESS_INFO info)
        {
            int pid = info.Process.dwProcessId;
            string? exe = null;

            try
            {
                using var process = Process.GetProcessById(pid);
                exe = process.MainModule?.FileName;
            }
            catch
            {
                // Elevated or already gone; the name Windows gave still says enough
            }

            return new FileLocker(pid, string.IsNullOrWhiteSpace(info.strAppName) ? $"Process {pid}" : info.strAppName,
                exe, KindOf(info.ApplicationType));
        }

        public static LockerKind KindOf(int applicationType) => applicationType switch
        {
            1 or 2 => LockerKind.App,
            3 => LockerKind.Service,
            4 => LockerKind.Explorer,
            1000 => LockerKind.Critical,
            _ => LockerKind.Background
        };

        const int ErrorMoreData = 234;

        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;

            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmStartSession(out uint session, int flags, StringBuilder key);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint session);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint session, uint fileCount, string[] files,
            uint applicationCount, RM_UNIQUE_PROCESS[]? applications, uint serviceCount, string[]? services);

        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint session, out uint needed, ref uint count,
            [In, Out] RM_PROCESS_INFO[]? infos, ref uint rebootReasons);
    }
}
