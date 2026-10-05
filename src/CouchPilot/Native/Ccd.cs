using System;
using System.Runtime.InteropServices;

namespace CouchPilot.Native
{
    // Windows Connecting and Configuring Displays (CCD) API: QueryDisplayConfig / SetDisplayConfig.
    internal static class Ccd
    {
        public const uint QDC_ALL_PATHS = 0x1;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

        public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
        public const uint SDC_VALIDATE = 0x40;
        public const uint SDC_APPLY = 0x80;
        public const uint SDC_SAVE_TO_DATABASE = 0x200;
        public const uint SDC_ALLOW_CHANGES = 0x400;

        public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x1;
        public const uint DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
        public const uint DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2;
        public const uint INVALID_MODE_IDX = 0xffffffff;

        private const int ERROR_SUCCESS = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public uint LowPart;
            public int HighPart;

            public long Value => ((long)HighPart << 32) | LowPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_2DREGION
        {
            public uint cx;
            public uint cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_TARGET_MODE
        {
            public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
        }

        // The native struct ends in a union; target mode (48 bytes) is its largest member.
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct DISPLAYCONFIG_MODE_INFO
        {
            [FieldOffset(0)] public uint infoType;
            [FieldOffset(4)] public uint id;
            [FieldOffset(8)] public LUID adapterId;
            [FieldOffset(16)] public DISPLAYCONFIG_TARGET_MODE targetMode;
            [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        public static (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) Query(uint flags)
        {
            while (true)
            {
                Check(GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount), "GetDisplayConfigBufferSizes");
                var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                var result = QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                // The topology can change between the two calls; ask again.
                if (result == ERROR_INSUFFICIENT_BUFFER) continue;
                Check(result, "QueryDisplayConfig");
                Array.Resize(ref paths, (int)pathCount);
                Array.Resize(ref modes, (int)modeCount);
                return (paths, modes);
            }
        }

        public static void Apply(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
        {
            var flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE;
            Check(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags), "SetDisplayConfig");
        }

        public static DISPLAYCONFIG_TARGET_DEVICE_NAME TargetName(LUID adapterId, uint targetId)
        {
            var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            request.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            request.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
            request.header.adapterId = adapterId;
            request.header.id = targetId;
            Check(DisplayConfigGetDeviceInfo(ref request), "DisplayConfigGetDeviceInfo");
            return request;
        }

        public static byte[] ToBytes<T>(T[] items) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var bytes = new byte[size * items.Length];
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                for (var i = 0; i < items.Length; i++)
                {
                    Marshal.StructureToPtr(items[i], ptr, false);
                    Marshal.Copy(ptr, bytes, i * size, size);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return bytes;
        }

        public static T[] FromBytes<T>(byte[] bytes) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var items = new T[bytes.Length / size];
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                for (var i = 0; i < items.Length; i++)
                {
                    Marshal.Copy(bytes, i * size, ptr, size);
                    items[i] = Marshal.PtrToStructure<T>(ptr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return items;
        }

        private static void Check(int result, string call)
        {
            if (result != ERROR_SUCCESS)
            {
                throw new InvalidOperationException($"{call} failed with Win32 error {result}");
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
    }
}
