using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Islet
{
    /// <summary>Which apps are using the microphone and the camera right now.</summary>
    public sealed record PrivacyUse(IReadOnlyList<string> Microphone, IReadOnlyList<string> Camera)
    {
        public static readonly PrivacyUse None = new(Array.Empty<string>(), Array.Empty<string>());

        public bool Any => Microphone.Count > 0 || Camera.Count > 0;

        /// <summary>Like iPhone, one dot: green for the camera (which usually brings the mic along), else orange.</summary>
        public bool CameraDot => Camera.Count > 0;

        /// <summary>What hovering the dot says, e.g. "Camera: Zoom · Mic: Zoom, Discord".</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (Camera.Count > 0) parts.Add("Camera: " + string.Join(", ", Camera));
            if (Microphone.Count > 0) parts.Add("Mic: " + string.Join(", ", Microphone));

            return string.Join(" · ", parts);
        }

        public bool SameAs(PrivacyUse other) =>
            Microphone.SequenceEqual(other.Microphone) && Camera.SequenceEqual(other.Camera);
    }

    /// <summary>
    /// Finds which apps are using the mic and the camera, without needing any permission.
    ///
    /// Mic: the apps with an active recording session on any microphone, as the
    /// volume mixer sees them. This is live on every Windows version.
    ///
    /// Camera (and older Windows' mic record): the registry behind Settings > Privacy,
    /// where an app in use has a start time but no stop time yet. Windows 11 24H2 and
    /// later only write it once the app stops, keeping the live state where only
    /// Windows can read it, so there the camera dot can't appear.
    /// </summary>
    public sealed class PrivacyWatcher
    {
        const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
        readonly Dictionary<string, string> descriptions = new(StringComparer.OrdinalIgnoreCase);
        readonly int ownProcess = Environment.ProcessId;
        bool loggedFailure;
        bool polling;

        public PrivacyUse Current { get; private set; } = PrivacyUse.None;

        public event Action? Changed;

        public PrivacyWatcher()
        {
            timer.Tick += (s, e) => Poll();
        }

        public void Start()
        {
            if (timer.IsEnabled)
                return;

            timer.Start();
            Poll();
        }

        public void Stop()
        {
            timer.Stop();
            Update(PrivacyUse.None);
        }

        // A check takes a few milliseconds of audio-device calls, so it runs off the UI
        // thread to keep animations smooth; the result comes back here
        async void Poll()
        {
            if (polling)
                return;

            polling = true;

            try
            {
                var next = await Task.Run(Read);

                if (timer.IsEnabled)
                    Update(next);
            }
            catch (Exception ex)
            {
                // A device unplugged mid-check can throw; try again next time, but log it once
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Log.Error("Reading mic and camera use failed", ex);
                }
            }
            finally
            {
                polling = false;
            }
        }

        PrivacyUse Read()
        {
            var microphone = Recorded("microphone");
            microphone.UnionWith(RecordingApps());

            return new PrivacyUse(microphone.ToList(), Recorded("webcam").ToList());
        }

        void Update(PrivacyUse next)
        {
            if (next.SameAs(Current))
                return;

            Current = next;
            Changed?.Invoke();
        }

        // Apps with a live recording session on any microphone
        List<string> RecordingApps()
        {
            var names = new List<string>();
            using var devices = new MMDeviceEnumerator();

            foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];

                        if (session.State != AudioSessionState.AudioSessionStateActive || session.IsSystemSoundsSession)
                            continue;

                        int pid = (int)session.GetProcessID;

                        if (pid != 0 && pid != ownProcess && ProcessPath(pid) is { } path)
                            names.Add(Describe(path));
                    }
                }
            }

            return names;
        }

        static string? ProcessPath(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                try
                {
                    return process.MainModule?.FileName ?? process.ProcessName;
                }
                catch
                {
                    // Elevated or protected processes can't be inspected; the name still says enough
                    return process.ProcessName;
                }
            }
            catch
            {
                // It ended in the meantime
                return null;
            }
        }

        SortedSet<string> Recorded(string capability)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using var root = Registry.CurrentUser.OpenSubKey(Store + capability);

            if (root == null)
                return names;

            // Store apps are listed directly; desktop apps sit under NonPackaged, keyed by their path
            foreach (var name in root.GetSubKeyNames())
            {
                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var key = root.OpenSubKey(name);

                if (key != null && IsInUse(key))
                    names.Add(PackagedAppName(name));
            }

            using var desktop = root.OpenSubKey("NonPackaged");

            if (desktop != null)
            {
                foreach (var name in desktop.GetSubKeyNames())
                {
                    using var key = desktop.OpenSubKey(name);

                    if (key == null || !IsInUse(key))
                        continue;

                    string path = ExePath(name);

                    // An app that crashed mid-call never writes its stop time; don't show a dot for it forever
                    if (!IsRunning(path))
                        continue;

                    names.Add(Describe(path));
                }
            }

            return names;
        }

        static bool IsInUse(RegistryKey key) =>
            IsInUse(ToLong(key.GetValue("LastUsedTimeStart")), ToLong(key.GetValue("LastUsedTimeStop")));

        static long ToLong(object? value) => value switch
        {
            long l => l,
            int i => i,
            _ => 0
        };

        static bool IsRunning(string path)
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
            bool running = processes.Length > 0;

            foreach (var process in processes)
                process.Dispose();

            return running;
        }

        // "Discord.exe" → "Discord", "brave.exe" → "Brave Browser", from the file's own description
        string Describe(string path)
        {
            if (descriptions.TryGetValue(path, out var cached))
                return cached;

            string name = DesktopAppName(path);

            try
            {
                if (File.Exists(path))
                {
                    var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();

                    if (!string.IsNullOrEmpty(description) && description.Length <= 32)
                        name = description;
                }
            }
            catch
            {
                // Unreadable file: keep the file name
            }

            descriptions[path] = name;
            return name;
        }

        public static bool IsInUse(long start, long stop) => start > 0 && stop == 0;

        /// <summary>Desktop apps are keyed by their path with '#' for each '\'.</summary>
        public static string ExePath(string keyName) => keyName.Replace('#', '\\');

        public static string DesktopAppName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : path;
        }

        static readonly Dictionary<string, string> KnownPackages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["WindowsCamera"] = "Camera",
            ["WhatsAppDesktop"] = "WhatsApp",
            ["XboxGamingOverlay"] = "Xbox Game Bar",
            ["immersivecontrolpanel"] = "Settings",
            ["WindowsSoundRecorder"] = "Sound Recorder",
            ["MSTeams"] = "Microsoft Teams",
            ["Client.CBS"] = "Windows"
        };

        /// <summary>"5319275A.WhatsAppDesktop_cv1g1gvanyjgm" → "WhatsApp", "Microsoft.ZuneVideo_8wekyb3d8bbwe" → "Zune Video".</summary>
        public static string PackagedAppName(string keyName)
        {
            // Drop the publisher hash after the last underscore
            int underscore = keyName.LastIndexOf('_');
            string name = underscore > 0 ? keyName[..underscore] : keyName;

            foreach (var (known, friendly) in KnownPackages)
            {
                if (name.EndsWith(known, StringComparison.OrdinalIgnoreCase))
                    return friendly;
            }

            // The part after the publisher's prefix, with spaces between words
            int dot = name.LastIndexOf('.');
            string last = dot >= 0 ? name[(dot + 1)..] : name;

            return Regex.Replace(last, "(?<=[a-z])(?=[A-Z])", " ");
        }
    }
}
