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
        private readonly Captures.ClipEditor clips;
        private readonly PairingService pairing;
        private readonly Achievements.AchievementsService achievements;
        private readonly Unlock.UnlockService unlock;
        private readonly HomeAssistant.CoverLights coverLights;

        public StatusSource(LibraryService library, DisplayService displays, RemoteInput input, CaptureService captures, Captures.ClipEditor clips, PairingService pairing, Achievements.AchievementsService achievements, Unlock.UnlockService unlock, HomeAssistant.CoverLights coverLights)
        {
            this.coverLights = coverLights;
            this.library = library;
            this.displays = displays;
            this.input = input;
            this.captures = captures;
            this.clips = clips;
            this.pairing = pairing;
            this.achievements = achievements;
            this.unlock = unlock;
        }

        // The status as this phone sees it: what the settings allow, narrowed to what this
        // device is allowed. `device` is null while pairing is off.
        public StatusDto Snapshot(PairedDevice device) => new StatusDto
        {
            Pc = Environment.MachineName,
            Running = library.Running(),
            Displays = displays.Snapshot(),
            Input = input.Enabled && (device == null || device.AllowInput),
            Capture = device == null || device.AllowCaptures ? WithEditing(captures.Status()) : null,
            Pairing = pairing.Required,
            Device = PairingService.ToDto(device),
            Achievements = achievements.Available,
            Locked = unlock.Locked,
            // Only a paired phone with the Unlock permission; with pairing off (device null) the
            // button never shows, matching the API, which refuses it.
            Unlock = unlock.OfferToPhones && device != null && device.AllowUnlock,
            CoverLights = coverLights.Available ? coverLights.Enabled : (bool?)null,
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
            var status = Snapshot(null);
            if (status.Displays.Pending != null) status.Displays.Pending.SecondsLeft = 0;
            // A device's permissions changing is news to that device, whatever the status looks like from here.
            return Serialize(status) + pairing.Version;
        }

        private CaptureStatusDto WithEditing(CaptureStatusDto status)
        {
            if (status != null) status.Editing = clips.Available;
            return status;
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
        // Phones have to type a PIN before they're let in.
        public bool Pairing { get; set; }
        // The paired phone this status is for, or null while pairing is off.
        public PairedDeviceDto Device { get; set; }
        // SuccessStory is installed and has data: games can show their achievements.
        public bool Achievements { get; set; }
        // The PC is on the lock screen right now.
        public bool Locked { get; set; }
        // This phone may unlock the PC: set up, locked, and permitted for this device.
        public bool Unlock { get; set; }
        // Games started from CouchPilot hand their cover's colours to Home Assistant; null while
        // Home Assistant is off in the settings, and the phone shows no switch.
        public bool? CoverLights { get; set; }
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
