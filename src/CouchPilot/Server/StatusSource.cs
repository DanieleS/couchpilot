using System;
using System.Collections.Generic;
using System.Linq;
using CouchPilot.Captures;
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
        private readonly CaptureService captures;

        public StatusSource(LibraryService library, DisplayService displays, RemoteInput input, CaptureService captures)
        {
            this.library = library;
            this.displays = displays;
            this.input = input;
            this.captures = captures;
        }

        public StatusDto Snapshot() => new StatusDto
        {
            Pc = Environment.MachineName,
            Running = library.Running(),
            Displays = displays.Snapshot(),
            Input = input.Enabled,
            Capture = captures.Status(),
            Controllers = Native.XInput.Pads().Select(p => new ControllerDto
            {
                Slot = p.Slot + 1,
                Wired = p.Wired,
                Battery = p.Level == null ? null : Levels[Math.Min(p.Level.Value, Levels.Length - 1)],
            }).ToList(),
        };

        private static readonly string[] Levels = { "empty", "low", "medium", "full" };

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
        // Screenshots and clips from the phone, or null when they're off in the settings.
        public CaptureStatusDto Capture { get; set; }
        public List<ControllerDto> Controllers { get; set; }
    }

    // An Xbox-style controller connected to the PC.
    public class ControllerDto
    {
        // 1–4, like the light on the controller.
        public int Slot { get; set; }
        public bool Wired { get; set; }
        // empty, low, medium or full; null when wired or when the pad doesn't say.
        public string Battery { get; set; }
    }
}
