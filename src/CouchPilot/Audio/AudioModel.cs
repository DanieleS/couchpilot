using System;
using System.Collections.Generic;
using static CouchPilot.Native.CoreAudio;

namespace CouchPilot.Audio
{
    // What the Audio tab shows, as AudioService last read it. Channels keep the Core Audio
    // objects they were read from, so volumes and levels can be re-read many times a second
    // without enumerating devices and sessions again.
    internal class AudioModel
    {
        public byte Generation;
        public List<Device> Outputs = new List<Device>();
        public List<Device> Inputs = new List<Device>();
        public List<Channel> Channels = new List<Channel>();

        public class Device
        {
            public string Id;
            public string Name;
            public string Detail;
            public byte Kind;
            public bool Default;
        }

        public class Channel
        {
            public byte Role;
            public string Name;
            // Apps only: stable across generations, and the name of the app's icon.
            public string Key;
            // Games only.
            public Guid GameId;
            public bool HasIcon;

            public byte Volume;
            public bool Muted;
            public double Peak;

            // Output and input: the endpoint itself.
            public IAudioEndpointVolume Endpoint;
            public IAudioMeterInformation EndpointMeter;
            // Apps: every session of the app on the default output.
            public List<ISimpleAudioVolume> Sessions = new List<ISimpleAudioVolume>();
            public List<IAudioMeterInformation> SessionMeters = new List<IAudioMeterInformation>();
        }
    }
}
