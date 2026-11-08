using System.Runtime.InteropServices;

namespace Islet
{
    /// <summary>
    /// Whether Windows' own Do Not Disturb is on. Windows has no public way to ask, so this
    /// reads the state its shell publishes for the notification centre (the same one Settings
    /// and the bell button use): 0 when it's off, and a quiet profile number when it's on.
    /// </summary>
    public static class DoNotDisturb
    {
        // WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED
        const ulong QuietHoursState = 0x0D83063EA3BF1C75;

        /// <summary>On, off, or null when this Windows doesn't say.</summary>
        public static bool? IsOn()
        {
            try
            {
                var buffer = new byte[16];
                uint size = (uint)buffer.Length;
                ulong state = QuietHoursState;

                if (NtQueryWnfStateData(ref state, IntPtr.Zero, IntPtr.Zero, out _, buffer, ref size) != 0 || size < 4)
                    return null;

                return IsQuietProfile(BitConverter.ToInt32(buffer, 0));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Any profile but "off" (priority only, alarms only) keeps things quiet.</summary>
        public static bool IsQuietProfile(int profile) => profile != 0;

        [DllImport("ntdll.dll")]
        static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope,
            out uint changeStamp, byte[] buffer, ref uint bufferSize);
    }
}
