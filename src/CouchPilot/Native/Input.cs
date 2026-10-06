using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CouchPilot.Native
{
    // Synthetic mouse and keyboard through SendInput. Windows drops it silently for elevated
    // windows (UIPI) and for the secure desktop (UAC prompts, lock screen).
    internal static class Input
    {
        public const ushort VK_BACK = 0x08;
        public const ushort VK_TAB = 0x09;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_ESCAPE = 0x1B;
        public const ushort VK_SPACE = 0x20;
        public const ushort VK_SNAPSHOT = 0x2C;
        public const ushort VK_PRIOR = 0x21;
        public const ushort VK_NEXT = 0x22;
        public const ushort VK_END = 0x23;
        public const ushort VK_HOME = 0x24;
        public const ushort VK_LEFT = 0x25;
        public const ushort VK_UP = 0x26;
        public const ushort VK_RIGHT = 0x27;
        public const ushort VK_DOWN = 0x28;
        public const ushort VK_INSERT = 0x2D;
        public const ushort VK_DELETE = 0x2E;
        public const ushort VK_LWIN = 0x5B;
        public const ushort VK_APPS = 0x5D;
        public const ushort VK_F1 = 0x70;
        public const ushort VK_LSHIFT = 0xA0;
        public const ushort VK_LCONTROL = 0xA2;
        public const ushort VK_LMENU = 0xA4;
        public const ushort VK_VOLUME_MUTE = 0xAD;
        public const ushort VK_VOLUME_DOWN = 0xAE;
        public const ushort VK_VOLUME_UP = 0xAF;
        public const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
        public const ushort VK_MEDIA_PREV_TRACK = 0xB1;
        public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

        public enum Button { Left, Right, Middle }

        public static void Move(int dx, int dy) => Send(Mouse(MOUSEEVENTF_MOVE, dx, dy, 0));

        // 120 is one notch. Positive vertical scrolls up, positive horizontal scrolls right.
        public static void Wheel(int vertical, int horizontal)
        {
            var inputs = new List<INPUT>(2);
            if (vertical != 0) inputs.Add(Mouse(MOUSEEVENTF_WHEEL, 0, 0, vertical));
            if (horizontal != 0) inputs.Add(Mouse(MOUSEEVENTF_HWHEEL, 0, 0, horizontal));
            Send(inputs.ToArray());
        }

        public static void ButtonEvent(Button button, bool down)
        {
            uint flags;
            switch (button)
            {
                case Button.Right: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
                case Button.Middle: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
                default: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
            }
            Send(Mouse(flags, 0, 0, 0));
        }

        // Modifiers go down in order, the key is tapped, modifiers come up in reverse: one
        // SendInput call, so nothing typed on the PC in the meantime can land in between.
        public static void Press(ushort vk, IList<ushort> modifiers)
        {
            var inputs = new List<INPUT>(modifiers.Count * 2 + 2);
            foreach (var m in modifiers) inputs.Add(Key(m, false));
            inputs.Add(Key(vk, false));
            inputs.Add(Key(vk, true));
            for (var i = modifiers.Count - 1; i >= 0; i--) inputs.Add(Key(modifiers[i], true));
            Send(inputs.ToArray());
        }

        // A shortcut held for a moment, as a person would press it: overlays that watch the
        // keyboard with a hook (NVIDIA's) can miss a combination that comes and goes at once.
        public static void Chord(ushort vk, IList<ushort> modifiers, int holdMs = 60)
        {
            var down = new List<INPUT>(modifiers.Count + 1);
            foreach (var m in modifiers) down.Add(Key(m, false));
            down.Add(Key(vk, false));
            Send(down.ToArray());
            System.Threading.Thread.Sleep(holdMs);
            var up = new List<INPUT>(modifiers.Count + 1) { Key(vk, true) };
            for (var i = modifiers.Count - 1; i >= 0; i--) up.Add(Key(modifiers[i], true));
            Send(up.ToArray());
        }

        // Text as Unicode characters, independent of the PC's keyboard layout.
        public static void Type(string text)
        {
            var inputs = new List<INPUT>(text.Length * 2);
            foreach (var c in text)
            {
                if (c == '\r') continue;
                if (c == '\n' || c == '\t')
                {
                    var vk = c == '\n' ? VK_RETURN : VK_TAB;
                    inputs.Add(Key(vk, false));
                    inputs.Add(Key(vk, true));
                    continue;
                }
                inputs.Add(Unicode(c, false));
                inputs.Add(Unicode(c, true));
            }
            Send(inputs.ToArray());
        }

        // The key that types this character on the current layout, and whether it needs Shift.
        public static bool KeyForChar(char c, out ushort vk, out bool shift)
        {
            var scan = VkKeyScanW(c);
            vk = (ushort)(scan & 0xFF);
            shift = (scan & 0x100) != 0;
            // Ctrl or Alt in the combination (AltGr characters) can't be pressed as a shortcut.
            return scan != -1 && (scan & 0x600) == 0;
        }

        private static INPUT Mouse(uint flags, int dx, int dy, int data) => new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
        };

        // Virtual key plus its scan code: games that read raw input or DirectInput look at the
        // scan code, everything else at the virtual key.
        private static INPUT Key(ushort vk, bool up)
        {
            var flags = up ? KEYEVENTF_KEYUP : 0;
            if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC), dwFlags = flags } },
            };
        }

        private static INPUT Unicode(char c, bool up) => new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } },
        };

        private static bool IsExtended(ushort vk) =>
            (vk >= VK_PRIOR && vk <= VK_DOWN) || vk == VK_INSERT || vk == VK_DELETE
            || vk == VK_LWIN || vk == VK_APPS || vk == VK_SNAPSHOT || (vk >= VK_VOLUME_MUTE && vk <= VK_MEDIA_PLAY_PAUSE);

        private static void Send(params INPUT[] inputs)
        {
            if (inputs.Length == 0) return;
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x1000;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint MAPVK_VK_TO_VSC = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // MOUSEINPUT is the largest member, so it alone sets the union's size.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKeyW(uint code, uint mapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScanW(char c);
    }
}
