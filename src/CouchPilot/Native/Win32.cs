using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CouchPilot.Native
{
    internal static class Win32
    {
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_QUERYENDSESSION = 0x0011;
        public const uint WM_ENDSESSION = 0x0016;
        public const int ENDSESSION_CLOSEAPP = 0x1;

        public struct ProcessEntry
        {
            public int Pid;
            public int ParentPid;
            public string ExeName;
        }

        public static List<ProcessEntry> ProcessSnapshot()
        {
            var result = new List<ProcessEntry>();
            var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == INVALID_HANDLE_VALUE) return result;
            try
            {
                var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
                if (!Process32FirstW(snap, ref entry)) return result;
                do
                {
                    result.Add(new ProcessEntry { Pid = (int)entry.th32ProcessID, ParentPid = (int)entry.th32ParentProcessID, ExeName = entry.szExeFile });
                } while (Process32NextW(snap, ref entry));
            }
            finally
            {
                CloseHandle(snap);
            }
            return result;
        }

        // Works across bitness and for most elevated-but-same-user processes, unlike Process.MainModule.
        public static string ImagePath(int pid)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var buffer = new StringBuilder(1024);
                var size = (uint)buffer.Capacity;
                return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static List<IntPtr> TopLevelWindows(ICollection<int> pids)
        {
            var set = new HashSet<int>(pids);
            var result = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (set.Contains((int)pid)) result.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static void SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            SendMessageTimeoutW(hwnd, msg, wParam, lParam, SMTO_ABORTIFHUNG | SMTO_NORMAL, 2000, out _);
        }

        public static bool IsMainWindowCandidate(IntPtr hwnd) =>
            IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero;

        public static long WindowArea(IntPtr hwnd) =>
            GetWindowRect(hwnd, out var r) ? (long)(r.Right - r.Left) * (r.Bottom - r.Top) : 0;

        // Windows only lets the foreground process hand focus away. Playnite isn't it while a
        // game runs, so tap Alt first: that unlocks SetForegroundWindow for the caller.
        public static bool ForceForeground(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            return SetForegroundWindow(hwnd);
        }

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint SMTO_NORMAL = 0x0;
        private const uint SMTO_ABORTIFHUNG = 0x2;
        private const uint GW_OWNER = 4;
        private const int SW_RESTORE = 9;
        private const byte VK_MENU = 0x12;
        private const uint KEYEVENTF_KEYUP = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32W
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
    }
}
