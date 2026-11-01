using System.Runtime.InteropServices;
using System.Management;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MusicIsland
{
    public enum StatusLayout
    {
        Pill,
        Small,
        Ring
    }

    public enum DeviceChange
    {
        Connected,
        Disconnected,
        AudioOutput
    }

    public static class SystemStatus
    {
        /// <summary>Volume and brightness take the shape of whatever the island currently is.</summary>
        public static StatusLayout LayoutFor(bool idle, IdleMode idleMode, MusicLook musicLook)
        {
            if (idle)
            {
                return idleMode switch
                {
                    IdleMode.Circle => StatusLayout.Ring,
                    IdleMode.Notch => StatusLayout.Small,
                    _ => StatusLayout.Pill
                };
            }

            return musicLook switch
            {
                MusicLook.Disc => StatusLayout.Ring,
                MusicLook.Small => StatusLayout.Small,
                _ => StatusLayout.Pill
            };
        }

        public static string VolumeGlyph(float level, bool muted) =>
            muted || level <= 0.001f ? ""
            : level < 0.34f ? ""
            : level < 0.67f ? ""
            : "";

        public static string DeviceText(DeviceChange change) => change switch
        {
            DeviceChange.Connected => "Connected",
            DeviceChange.Disconnected => "Disconnected",
            _ => "Now playing"
        };
    }

    /// <summary>
    /// One device can report several times at once (Bluetooth classic, Bluetooth LE and
    /// the audio output switching to it), which should only show up once.
    /// </summary>
    public sealed class DeviceNoticeFilter
    {
        static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

        DateTime lastTime = DateTime.MinValue;
        string lastName = "";
        DeviceChange lastChange;

        public bool ShouldShow(string name, DeviceChange change, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (now - lastTime < Window && (change == DeviceChange.AudioOutput || (name == lastName && change == lastChange)))
                return false;

            lastTime = now;
            lastName = name;
            lastChange = change;
            return true;
        }
    }

    /// <summary>
    /// Reports volume, brightness and device changes from anywhere in Windows. Events
    /// arrive on background threads. Nothing fires for the state found at startup.
    /// </summary>
    public sealed class SystemStatusWatcher : IMMNotificationClient, IDisposable
    {
        public event Action<float, bool>? VolumeChanged;
        public event Action<int>? BrightnessChanged;
        public event Action<string, DeviceChange>? DeviceChanged;

        readonly object gate = new();
        readonly Dictionary<string, string> devices = new();
        readonly List<DeviceWatcher> deviceWatchers = new();
        readonly DeviceNoticeFilter filter = new();

        MMDeviceEnumerator? enumerator;
        MMDevice? device;
        ManagementEventWatcher? brightness;
        float lastVolume = -1;
        bool lastMuted;
        int lastBrightness = -1;
        bool disposed;

        // On a pool thread: Core Audio callbacks need a free-threaded apartment
        public void Start() => Task.Run(() =>
        {
            StartAudio();
            StartBrightness();
            StartDevices(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
            StartDevices(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
        });

        void StartAudio()
        {
            try
            {
                enumerator = new MMDeviceEnumerator();
                enumerator.RegisterEndpointNotificationCallback(this);
                BindDevice();
            }
            catch (Exception ex)
            {
                Log.Error("Volume watcher failed", ex);
            }
        }

        void BindDevice()
        {
            lock (gate)
            {
                if (disposed || enumerator == null)
                    return;

                if (device != null)
                {
                    device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
                    device.Dispose();
                    device = null;
                }

                try
                {
                    device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    lastVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    lastMuted = device.AudioEndpointVolume.Mute;
                    device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
                }
                catch
                {
                    // No audio output right now; the next default-device change rebinds
                }
            }
        }

        void OnVolume(AudioVolumeNotificationData data)
        {
            if (Math.Abs(data.MasterVolume - lastVolume) < 0.002f && data.Muted == lastMuted)
                return;

            lastVolume = data.MasterVolume;
            lastMuted = data.Muted;
            VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
        }

        void StartBrightness()
        {
            try
            {
                brightness = new ManagementEventWatcher(
                    new ManagementScope(@"root\wmi"),
                    new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));

                brightness.EventArrived += (s, e) =>
                {
                    int level = Convert.ToInt32(e.NewEvent["Brightness"]);

                    if (level == lastBrightness)
                        return;

                    lastBrightness = level;
                    BrightnessChanged?.Invoke(level);
                };

                brightness.Start();
            }
            catch (Exception ex)
            {
                // Normal on desktop PCs: external monitors don't report brightness
                Log.Info("Brightness events unavailable: " + ex.Message);
                brightness = null;
            }
        }

        void StartDevices(string selector)
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector);
                bool ready = false;

                watcher.EnumerationCompleted += (s, e) => ready = true;
                watcher.Updated += (s, e) => { };
                watcher.Added += (s, info) =>
                {
                    lock (devices)
                        devices[info.Id] = info.Name;

                    if (ready)
                        ReportDevice(info.Name, DeviceChange.Connected);
                };
                watcher.Removed += (s, update) =>
                {
                    string? name;

                    lock (devices)
                        devices.Remove(update.Id, out name);

                    if (ready && name != null)
                        ReportDevice(name, DeviceChange.Disconnected);
                };

                watcher.Start();
                deviceWatchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Log.Info("Device watcher unavailable: " + ex.Message);
            }
        }

        void ReportDevice(string name, DeviceChange change)
        {
            bool show;

            lock (filter)
                show = filter.ShouldShow(name, change, DateTime.Now);

            if (show)
                DeviceChanged?.Invoke(name, change);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render || role != Role.Multimedia)
                return;

            // Core Audio forbids device calls inside its own callback
            Task.Run(() =>
            {
                BindDevice();

                string? name = null;

                lock (gate)
                {
                    try { name = device?.FriendlyName; }
                    catch { }
                }

                if (name != null)
                    ReportDevice(name, DeviceChange.AudioOutput);
            });
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;

                try
                {
                    if (device != null)
                        device.AudioEndpointVolume.OnVolumeNotification -= OnVolume;

                    device?.Dispose();
                    enumerator?.UnregisterEndpointNotificationCallback(this);
                    enumerator?.Dispose();
                    brightness?.Stop();
                    brightness?.Dispose();

                    foreach (var watcher in deviceWatchers)
                        watcher.Stop();
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Windows 11 draws its volume pop-up without a window that could be hidden, so the
    /// only way to replace it is to take the volume keys and change the volume ourselves.
    /// The hook lives on its own thread, because a slow hook delays every key press on the PC.
    /// </summary>
    public sealed class VolumeKeyHook : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_QUIT = 0x0012;
        const int VK_VOLUME_MUTE = 0xAD;
        const int VK_VOLUME_DOWN = 0xAE;
        const int VK_VOLUME_UP = 0xAF;

        /// <summary>When false, keys reach Windows as usual and it shows its own pop-up.</summary>
        public volatile bool Active;

        readonly HookProc proc;
        readonly System.Collections.Concurrent.BlockingCollection<int> keys = new();
        Thread? hookThread;
        uint hookThreadId;
        IntPtr hook;

        public VolumeKeyHook() => proc = Callback;

        public void Start()
        {
            hookThread = new Thread(() =>
            {
                hookThreadId = GetCurrentThreadId();
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);

                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                }

                if (hook != IntPtr.Zero)
                    UnhookWindowsHookEx(hook);
            })
            { IsBackground = true, Name = "Volume key hook" };

            hookThread.Start();

            // Applies the keys away from the hook; on a pool thread for Core Audio
            Task.Run(() =>
            {
                using var enumerator = new MMDeviceEnumerator();

                foreach (int key in keys.GetConsumingEnumerable())
                    Apply(enumerator, key);
            });
        }

        IntPtr Callback(int code, IntPtr wParam, ref KeyInfo info)
        {
            if (code >= 0 && Active && info.Key is VK_VOLUME_MUTE or VK_VOLUME_DOWN or VK_VOLUME_UP)
            {
                if ((int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN)
                    keys.Add(info.Key);

                return 1;
            }

            return CallNextHookEx(hook, code, wParam, ref info);
        }

        static void Apply(MMDeviceEnumerator enumerator, int key)
        {
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var volume = device.AudioEndpointVolume;

                if (key == VK_VOLUME_MUTE)
                {
                    volume.Mute = !volume.Mute;
                    return;
                }

                if (volume.Mute)
                    volume.Mute = false;

                if (key == VK_VOLUME_UP)
                    volume.VolumeStepUp();
                else
                    volume.VolumeStepDown();
            }
            catch (Exception ex)
            {
                Log.Error("Volume key failed", ex);
            }
        }

        public void Dispose()
        {
            keys.CompleteAdding();

            if (hookThreadId != 0)
                PostThreadMessage(hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        delegate IntPtr HookProc(int code, IntPtr wParam, ref KeyInfo info);

        [StructLayout(LayoutKind.Sequential)]
        struct KeyInfo
        {
            public int Key;
            public int ScanCode;
            public int Flags;
            public int Time;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Msg
        {
            public IntPtr Window;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, ref KeyInfo info);

        [DllImport("user32.dll")]
        static extern int GetMessage(out Msg msg, IntPtr window, uint min, uint max);

        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint thread, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string? name);
    }
}
