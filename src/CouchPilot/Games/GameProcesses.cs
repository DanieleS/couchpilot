using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CouchPilot.Native;

namespace CouchPilot.Games
{
    // Finds a game's processes and closes them the way Vibepollo's playnite_launcher does:
    // ask every window to close, wait, and only then terminate what is left.
    public static class GameProcesses
    {
        public static List<int> Find(string installDir, int rootPid)
        {
            var self = Process.GetCurrentProcess().Id;
            var snapshot = Win32.ProcessSnapshot();
            var result = new HashSet<int>();

            var dir = SafeInstallDir(installDir);
            if (dir != null)
            {
                foreach (var p in snapshot)
                {
                    var path = Win32.ImagePath(p.Pid);
                    if (path != null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(p.Pid);
                    }
                }
            }

            if (rootPid > 0 && snapshot.Any(p => p.Pid == rootPid))
            {
                result.Add(rootPid);
                AddDescendants(rootPid, snapshot, result);
            }

            result.RemoveWhere(pid => pid <= 4 || pid == self || IsProtected(snapshot, pid));
            return result.ToList();
        }

        public static async Task CloseGracefullyThenForce(string installDir, int rootPid, TimeSpan grace)
        {
            var pids = Find(installDir, rootPid);
            if (pids.Count == 0) return;

            foreach (var hwnd in Win32.TopLevelWindows(pids))
            {
                Win32.SendMessageTimeout(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            var deadline = DateTime.UtcNow + grace;
            var endSessionSent = false;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                pids = Find(installDir, rootPid);
                if (pids.Count == 0) return;

                // Halfway through, try the shutdown messages some games only answer to.
                if (!endSessionSent && deadline - DateTime.UtcNow < TimeSpan.FromTicks(grace.Ticks / 2))
                {
                    foreach (var hwnd in Win32.TopLevelWindows(pids))
                    {
                        Win32.SendMessageTimeout(hwnd, Win32.WM_QUERYENDSESSION, new IntPtr(1), new IntPtr(Win32.ENDSESSION_CLOSEAPP));
                        Win32.SendMessageTimeout(hwnd, Win32.WM_ENDSESSION, new IntPtr(1), IntPtr.Zero);
                    }
                    endSessionSent = true;
                }
            }

            foreach (var pid in Find(installDir, rootPid))
            {
                try { Process.GetProcessById(pid).Kill(); } catch { }
            }
        }

        public static bool Focus(string installDir, int rootPid)
        {
            var pids = Find(installDir, rootPid);
            var hwnd = Win32.TopLevelWindows(pids)
                .Where(Win32.IsMainWindowCandidate)
                .OrderByDescending(Win32.WindowArea)
                .FirstOrDefault();
            if (hwnd == IntPtr.Zero) return false;
            return Win32.ForceForeground(hwnd);
        }

        // An install directory we can safely match process paths against. A game with
        // InstallDirectory "C:\" or "C:\Program Files" must not take the whole machine down.
        private static string SafeInstallDir(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir)) return null;
            string full;
            try { full = Path.GetFullPath(installDir).TrimEnd('\\', '/'); } catch { return null; }
            if (!Directory.Exists(full)) return null;

            var depth = full.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth < 2) return null;

            var forbidden = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            }.Where(f => !string.IsNullOrEmpty(f)).Select(f => f.TrimEnd('\\'));
            if (forbidden.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase))) return null;

            return full + "\\";
        }

        private static void AddDescendants(int pid, List<Win32.ProcessEntry> snapshot, HashSet<int> into)
        {
            foreach (var child in snapshot.Where(p => p.ParentPid == pid && p.Pid != pid))
            {
                if (into.Add(child.Pid))
                {
                    AddDescendants(child.Pid, snapshot, into);
                }
            }
        }

        private static readonly HashSet<string> ProtectedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe", "playnite.desktopapp.exe", "playnite.fullscreenapp.exe", "steam.exe",
            "steamwebhelper.exe", "epicgameslauncher.exe", "galaxyclient.exe", "sunshine.exe",
            "conhost.exe", "csrss.exe", "dwm.exe", "svchost.exe",
        };

        private static bool IsProtected(List<Win32.ProcessEntry> snapshot, int pid)
        {
            var entry = snapshot.FirstOrDefault(p => p.Pid == pid);
            return entry.ExeName != null && ProtectedExes.Contains(entry.ExeName);
        }
    }
}
