using System;
using System.Collections.Generic;
using CouchPilot.Displays;
using CouchPilot.Input;
using CouchPilot.Library;
using Swan.Formatters;

namespace CouchPilot.Server
{
    // The PC's state at a glance: what's running, what the screens look like, whether the phone
    // may use mouse and keyboard. Served on GET /api/status and pushed as STATUS on the live socket.
    public class StatusSource
    {
        private readonly LibraryService library;
        private readonly DisplayService displays;
        private readonly RemoteInput input;

        public StatusSource(LibraryService library, DisplayService displays, RemoteInput input)
        {
            this.library = library;
            this.displays = displays;
            this.input = input;
        }

        public StatusDto Snapshot() => new StatusDto
        {
            Pc = Environment.MachineName,
            Running = library.Running(),
            Displays = displays.Snapshot(),
            Input = input.Enabled,
        };

        // The JSON body of STATUS, and the same JSON with the pending display change's countdown
        // left out: what decides whether anything changed. The countdown alone isn't news, the
        // phone counts down by itself.
        public (string json, string shape) Encode()
        {
            var status = Snapshot();
            var json = Serialize(status);
            var pending = status.Displays.Pending;
            if (pending == null) return (json, json);
            var secondsLeft = pending.SecondsLeft;
            pending.SecondsLeft = 0;
            var shape = Serialize(status);
            pending.SecondsLeft = secondsLeft;
            return (json, shape);
        }

        private static string Serialize(StatusDto status) => Json.Serialize(status, JsonSerializerCase.CamelCase);
    }

    public class StatusDto
    {
        public string Pc { get; set; }
        public List<RunningDto> Running { get; set; }
        public DisplaysDto Displays { get; set; }
        // Touchpad and keyboard are allowed in the extension settings.
        public bool Input { get; set; }
    }
}
