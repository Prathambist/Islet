using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet
{
    /// <summary>
    /// Explorer's own thumbnail for any file or folder: a real preview for images, videos
    /// and documents, otherwise its icon. Made one at a time on a background thread (the
    /// shell needs an STA thread, and a video's first thumbnail can take a moment), so the
    /// island never stutters while they load.
    /// </summary>
    public static class ShellThumbnails
    {
        static readonly BlockingCollection<(string Path, int Size, TaskCompletionSource<BitmapSource?> Done)> queue = new();
        static Thread? worker;

        public static Task<BitmapSource?> GetAsync(string path, int size)
        {
            var done = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add((path, size, done));

            lock (queue)
            {
                if (worker == null)
                {
                    worker = new Thread(Work) { IsBackground = true, Name = "Shelf thumbnails" };
                    worker.SetApartmentState(ApartmentState.STA);
                    worker.Start();
                }
            }

            return done.Task;
        }

        static void Work()
        {
            foreach (var (path, size, done) in queue.GetConsumingEnumerable())
            {
                try
                {
                    done.SetResult(Get(path, size));
                }
                catch
                {
                    done.SetResult(null);
                }
            }
        }

        static BitmapSource? Get(string path, int size)
        {
            var id = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out var factory);

            try
            {
                if (factory.GetImage(new NativeSize { Width = size, Height = size }, 0, out var bitmap) != 0 || bitmap == IntPtr.Zero)
                    return null;

                try
                {
                    return ToBitmapSource(bitmap);
                }
                finally
                {
                    DeleteObject(bitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }

        // Copied pixel by pixel: WPF's own conversion drops the alpha, giving icons black corners
        static BitmapSource? ToBitmapSource(IntPtr handle)
        {
            if (GetObject(handle, Marshal.SizeOf<NativeBitmap>(), out var info) == 0)
                return null;

            int width = info.Width, height = Math.Abs(info.Height), stride = width * 4;
            var rows = new byte[stride * height];

            // Photos and icons come back stored in opposite row orders; asking for the
            // pixels top row first (a negative height) gets both the right way up
            var header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };

            IntPtr screen = GetDC(IntPtr.Zero);

            try
            {
                if (GetDIBits(screen, handle, 0, (uint)height, rows, ref header, 0) == 0)
                    return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }

            // Photo thumbnails come with no alpha at all; those are fully opaque
            bool anyAlpha = false;

            for (int i = 3; i < rows.Length && !anyAlpha; i += 4)
                anyAlpha = rows[i] != 0;

            if (!anyAlpha)
                for (int i = 3; i < rows.Length; i += 4)
                    rows[i] = 255;

            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, rows, stride);
            source.Freeze();
            return source;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeBitmap
        {
            public int Type;
            public int Width;
            public int Height;
            public int WidthBytes;
            public ushort Planes;
            public ushort BitsPixel;
            public IntPtr Bits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, int flags, out IntPtr bitmap);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid id,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [DllImport("gdi32.dll")]
        static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        [DllImport("gdi32.dll")]
        static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr window, IntPtr dc);
    }
}
