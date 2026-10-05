using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CouchPilot.Audio
{
    // The audio messages of the live WebSocket: encoding PC → phone, opcodes both ways.
    // docs/protocol.md is the specification, byte by byte; keep the two in step.
    internal static class AudioProtocol
    {
        // PC → phone
        public const byte OpState = 0x82;
        public const byte OpVolumes = 0x83;
        public const byte OpLevels = 0x84;
        // Phone → PC
        public const byte OpSetVolume = 0x10;
        public const byte OpSetMute = 0x11;
        public const byte OpSetDefault = 0x12;

        public const byte RoleOutput = 0;
        public const byte RoleInput = 1;
        public const byte RoleApp = 2;
        public const byte RoleSystem = 3;
        public const byte RoleGame = 4;

        public const byte KindSpeakers = 0;
        public const byte KindHeadphones = 1;
        public const byte KindDisplay = 2;
        public const byte KindMic = 3;

        public const byte FlowOutput = 0;
        public const byte FlowInput = 1;

        private const byte FlagDefault = 1;
        private const byte FlagMuted = 1;
        private const byte FlagIcon = 2;

        // Lists longer than a byte can count are cut: 255 apps playing at once won't happen.
        public const int MaxItems = 255;

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static byte[] State(AudioModel model)
        {
            using (var stream = new MemoryStream(256))
            {
                stream.WriteByte(OpState);
                stream.WriteByte(model.Generation);
                WriteDevices(stream, model.Outputs);
                WriteDevices(stream, model.Inputs);
                stream.WriteByte((byte)model.Channels.Count);
                foreach (var c in model.Channels)
                {
                    stream.WriteByte(c.Role);
                    stream.WriteByte(c.Volume);
                    stream.WriteByte((byte)((c.Muted ? FlagMuted : 0) | (c.HasIcon ? FlagIcon : 0)));
                    WriteString(stream, c.Name);
                    if (c.Role >= RoleApp) WriteString(stream, c.Key);
                    if (c.Role == RoleGame) stream.Write(GuidBytes(c.GameId), 0, 16);
                }
                return stream.ToArray();
            }
        }

        public static byte[] Volumes(AudioModel model)
        {
            var bytes = new byte[2 + model.Channels.Count * 2];
            bytes[0] = OpVolumes;
            bytes[1] = model.Generation;
            for (var i = 0; i < model.Channels.Count; i++)
            {
                bytes[2 + i * 2] = model.Channels[i].Volume;
                bytes[3 + i * 2] = model.Channels[i].Muted ? FlagMuted : (byte)0;
            }
            return bytes;
        }

        public static byte[] Levels(AudioModel model)
        {
            var bytes = new byte[2 + model.Channels.Count];
            bytes[0] = OpLevels;
            bytes[1] = model.Generation;
            for (var i = 0; i < model.Channels.Count; i++)
            {
                var c = model.Channels[i];
                bytes[2 + i] = c.Muted ? (byte)0 : (byte)Math.Round(Math.Sqrt(Math.Max(0, Math.Min(1, c.Peak))) * 255);
            }
            return bytes;
        }

        private static void WriteDevices(Stream stream, List<AudioModel.Device> devices)
        {
            stream.WriteByte((byte)devices.Count);
            foreach (var d in devices)
            {
                stream.WriteByte(d.Kind);
                stream.WriteByte(d.Default ? FlagDefault : (byte)0);
                WriteString(stream, d.Name);
                WriteString(stream, d.Detail);
            }
        }

        // At most 255 bytes, cut on a character boundary.
        private static void WriteString(Stream stream, string text)
        {
            text = text ?? "";
            var bytes = Utf8.GetBytes(text);
            if (bytes.Length > 255)
            {
                var info = new StringInfo(text);
                var length = info.LengthInTextElements;
                do bytes = Utf8.GetBytes(info.SubstringByTextElements(0, --length));
                while (bytes.Length > 255);
            }
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static byte[] GuidBytes(Guid id)
        {
            var hex = id.ToString("N");
            var bytes = new byte[16];
            for (var i = 0; i < 16; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
