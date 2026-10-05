using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CouchPilot.Native
{
    // The bits of the Windows shell the captures need: where Game Bar saves, and the thumbnails
    // Explorer shows for videos.
    internal static class Shell
    {
        // Game Bar's Captures folder, wherever the user moved it. Missing on older Windows.
        public static readonly Guid AppCaptures = new Guid("EDC0FE71-98D8-4F4A-B920-C8DC133CB165");

        public static string KnownFolder(Guid id)
        {
            try
            {
                if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var path) != 0) return null;
                try
                {
                    return Marshal.PtrToStringUni(path);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(path);
                }
            }
            catch
            {
                return null;
            }
        }

        // Explorer's thumbnail of a file, at least `size` pixels on its long side, or null when
        // Windows has none (no codec for the video, say). Call it from an STA thread.
        public static Bitmap Thumbnail(string path, int size)
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item) != 0 || item == null) return null;
            try
            {
                var factory = (IShellItemImageFactory)item;
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY, out var bitmap) != 0) return null;
                try
                {
                    return Image.FromHbitmap(bitmap);
                }
                finally
                {
                    DeleteObject(bitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }

        private const int SIIGBF_BIGGERSIZEOK = 0x01;
        private const int SIIGBF_THUMBNAILONLY = 0x08;

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr bitmap);
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
