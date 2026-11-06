using System.Diagnostics;
using System.IO;

namespace Islet.LegacyLauncher
{
    static class Program
    {
        // Started by an old MusicIsland install after it updated: hands over to Islet beside it
        static void Main()
        {
            string folder = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            string islet = Path.Combine(folder, "Islet.exe");

            if (File.Exists(islet))
                Process.Start(new ProcessStartInfo(islet) { UseShellExecute = false, WorkingDirectory = folder });
        }
    }
}
