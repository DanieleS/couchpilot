using System;
using System.Collections.Generic;
using System.Text;
using Playnite.SDK;
using Button = CouchPilot.Native.Input.Button;

namespace CouchPilot.Input
{
    // The phone's touchpad and keyboard: opcodes 0x01–0x0F of the live WebSocket, one message per
    // command, an opcode byte followed by its fields (integers little-endian). docs/protocol.md is
    // the full specification; in short:
    //   0x01 move    int16 dx, int16 dy       pixels
    //   0x02 wheel   int16 vertical, int16 h  120 is one notch, positive is up / right
    //   0x03 down    u8 button                0 left, 1 right, 2 middle
    //   0x04 up      u8 button
    //   0x05 click   u8 button
    //   0x06 key     u8 mods, u8 vk           a Windows virtual key, tapped with the modifiers
    //   0x07 char    u8 mods, UTF-8 char      the key that types this character, as a shortcut
    //   0x08 text    UTF-8 text               typed as Unicode, whatever the PC's layout
    // Modifier bits: 1 Ctrl, 2 Alt, 4 Shift, 8 Win.
    public class RemoteInput
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        private const byte OpMove = 0x01;
        private const byte OpWheel = 0x02;
        private const byte OpDown = 0x03;
        private const byte OpUp = 0x04;
        private const byte OpClick = 0x05;
        private const byte OpKey = 0x06;
        private const byte OpChar = 0x07;
        private const byte OpText = 0x08;

        private static readonly ushort[] ModifierKeys =
        {
            Native.Input.VK_LCONTROL, Native.Input.VK_LMENU, Native.Input.VK_LSHIFT, Native.Input.VK_LWIN,
        };

        private readonly object gate = new object();
        private readonly HashSet<Button> held = new HashSet<Button>();
        private volatile bool enabled;

        // Off unless turned on in the settings: whoever reaches the port gets the PC's keyboard.
        public bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                if (!value) ReleaseAll();
            }
        }

        // A phone that drops mid-drag must not leave the button stuck down.
        public void ReleaseAll()
        {
            lock (gate)
            {
                foreach (var b in held) Native.Input.ButtonEvent(b, false);
                held.Clear();
            }
        }

        public void Handle(byte[] message)
        {
            if (!enabled || message == null || message.Length == 0) return;
            try
            {
                lock (gate)
                {
                    Dispatch(message);
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Ignoring input command {BitConverter.ToString(message, 0, Math.Min(message.Length, 16))}");
            }
        }

        private void Dispatch(byte[] m)
        {
            switch (m[0])
            {
                case OpMove:
                    Need(m, 5);
                    Native.Input.Move(Int16(m, 1), Int16(m, 3));
                    break;
                case OpWheel:
                    Need(m, 5);
                    Native.Input.Wheel(Int16(m, 1), Int16(m, 3));
                    break;
                case OpDown:
                {
                    var b = ButtonAt(m);
                    if (held.Add(b)) Native.Input.ButtonEvent(b, true);
                    break;
                }
                case OpUp:
                {
                    var b = ButtonAt(m);
                    if (held.Remove(b)) Native.Input.ButtonEvent(b, false);
                    break;
                }
                case OpClick:
                {
                    var b = ButtonAt(m);
                    held.Remove(b);
                    Native.Input.ButtonEvent(b, true);
                    Native.Input.ButtonEvent(b, false);
                    break;
                }
                case OpKey:
                    Need(m, 3);
                    if (m[2] != 0) Native.Input.Press(m[2], Mods(m[1]));
                    break;
                case OpChar:
                    Need(m, 3);
                    PressChar(Mods(m[1]), Utf8.GetString(m, 2, m.Length - 2));
                    break;
                case OpText:
                    Native.Input.Type(Utf8.GetString(m, 1, m.Length - 1));
                    break;
                default:
                    throw new ArgumentException($"Unknown opcode {m[0]}");
            }
        }

        // With modifiers a character becomes a shortcut (Ctrl+C), which needs the key that
        // produces it on this layout; one no key can type is sent as plain text instead.
        private static void PressChar(List<ushort> mods, string text)
        {
            if (text.Length != 1 || mods.Count == 0 || !Native.Input.KeyForChar(text[0], out var vk, out var shift))
            {
                Native.Input.Type(text);
                return;
            }
            if (shift && !mods.Contains(Native.Input.VK_LSHIFT)) mods.Add(Native.Input.VK_LSHIFT);
            Native.Input.Press(vk, mods);
        }

        private static List<ushort> Mods(byte bits)
        {
            var mods = new List<ushort>(4);
            for (var i = 0; i < ModifierKeys.Length; i++)
            {
                if ((bits & (1 << i)) != 0) mods.Add(ModifierKeys[i]);
            }
            return mods;
        }

        private static Button ButtonAt(byte[] m)
        {
            Need(m, 2);
            switch (m[1])
            {
                case 1: return Button.Right;
                case 2: return Button.Middle;
                default: return Button.Left;
            }
        }

        private static short Int16(byte[] m, int at) => (short)(m[at] | (m[at + 1] << 8));

        private static void Need(byte[] m, int length)
        {
            if (m.Length < length) throw new ArgumentException($"Command 0x{m[0]:X2} needs {length} bytes, got {m.Length}");
        }
    }
}
