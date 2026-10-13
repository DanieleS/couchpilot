using System;

namespace CouchPilot.HomeAssistant
{
    // Whether a game started from CouchPilot hands its cover's colours to Home Assistant: a switch
    // on the phone, for when the lights following the cover get old for a while. On unless turned
    // off, and remembered in the settings across restarts.
    public class CoverLights
    {
        private readonly CouchPilotSettings settings;
        private readonly Action save;

        public CoverLights(CouchPilotSettings settings, Action save)
        {
            this.settings = settings;
            this.save = save;
        }

        public event Action Changed;

        public bool Enabled => settings.CoverLights;

        // Only worth a switch on the phone while there's an MQTT broker to hand the colours to.
        public bool Available => settings.MqttEnabled && !string.IsNullOrWhiteSpace(settings.MqttHost);

        public void Set(bool enabled)
        {
            if (settings.CoverLights == enabled) return;
            settings.CoverLights = enabled;
            save();
            Changed?.Invoke();
        }
    }
}
