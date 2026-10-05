using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CouchPilot.Native
{
    // Xbox-style controllers through XInput: which of the four slots are taken, and their
    // battery. PlayStation and Switch pads don't speak XInput (unless Steam Input wraps them),
    // so they don't show up here.
    internal static class XInput
    {
        public struct Pad
        {
            public int Slot;
            public bool Wired;
            // 0 empty, 1 low, 2 medium, 3 full; null when the pad doesn't report it.
            public int? Level;
        }

        private const uint ERROR_SUCCESS = 0;
        private const byte BATTERY_DEVTYPE_GAMEPAD = 0;
        private const byte BATTERY_TYPE_DISCONNECTED = 0;
        private const byte BATTERY_TYPE_WIRED = 1;
        private const byte BATTERY_TYPE_UNKNOWN = 0xFF;

        public static List<Pad> Pads()
        {
            var pads = new List<Pad>();
            for (var slot = 0; slot < 4; slot++)
            {
                try
                {
                    if (XInputGetState((uint)slot, out _) != ERROR_SUCCESS) continue;
                    if (XInputGetBatteryInformation((uint)slot, BATTERY_DEVTYPE_GAMEPAD, out var battery) != ERROR_SUCCESS
                        || battery.BatteryType == BATTERY_TYPE_DISCONNECTED)
                    {
                        pads.Add(new Pad { Slot = slot });
                        continue;
                    }
                    var wired = battery.BatteryType == BATTERY_TYPE_WIRED;
                    pads.Add(new Pad
                    {
                        Slot = slot,
                        Wired = wired,
                        Level = wired || battery.BatteryType == BATTERY_TYPE_UNKNOWN ? (int?)null : battery.BatteryLevel,
                    });
                }
                catch (DllNotFoundException)
                {
                    break;
                }
            }
            return pads;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        // xinput1_4 ships with Windows 8 and later.
        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);
    }
}
