using System;
using System.Collections.Generic;
using CouchPilot.Displays;
using CouchPilot.Input;
using CouchPilot.Library;
using Swan.Formatters;

namespace CouchPilot.Server
{
    // The PC's state at a glance: what's running, what the screens look like, whether the phone
    // may use mouse and keyboard. Served on GET /api/status; the live socket says when it changed.
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

        // What decides whether phones are told the status changed: the status as JSON, with the
        // pending display change's countdown left out. The countdown alone isn't news, the phone
        // counts down by itself.
        public string Shape()
        {
            var status = Snapshot();
            if (status.Displays.Pending != null) status.Displays.Pending.SecondsLeft = 0;
            return Serialize(status);
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
