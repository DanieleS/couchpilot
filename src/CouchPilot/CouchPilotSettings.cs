using System.Collections.Generic;
using CouchPilot.HomeAssistant;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot
{
    public class CouchPilotSettings : ObservableObject, ISettings
    {
        private readonly CouchPilotPlugin plugin;
        private CouchPilotSettings editing;

        private int port = 8642;
        public int Port { get => port; set => SetValue(ref port, value); }

        // Home Assistant over MQTT: off unless you turn it on.
        private bool mqttEnabled;
        public bool MqttEnabled { get => mqttEnabled; set => SetValue(ref mqttEnabled, value); }

        private string mqttHost = "";
        public string MqttHost { get => mqttHost; set => SetValue(ref mqttHost, value); }

        private int mqttPort = 1883;
        public int MqttPort { get => mqttPort; set => SetValue(ref mqttPort, value); }

        private string mqttUsername = "";
        public string MqttUsername { get => mqttUsername; set => SetValue(ref mqttUsername, value); }

        // Stored in Playnite's extension data folder in plain text, like any other setting.
        private string mqttPassword = "";
        public string MqttPassword { get => mqttPassword; set => SetValue(ref mqttPassword, value); }

        private bool mqttCommands;
        public bool MqttCommands { get => mqttCommands; set => SetValue(ref mqttCommands, value); }

        private string mqttBaseTopic = "couchpilot";
        public string MqttBaseTopic { get => mqttBaseTopic; set => SetValue(ref mqttBaseTopic, value); }

        private string mqttDiscoveryPrefix = "homeassistant";
        public string MqttDiscoveryPrefix { get => mqttDiscoveryPrefix; set => SetValue(ref mqttDiscoveryPrefix, value); }

        [DontSerialize]
        public IReadOnlyList<string> Addresses => Server.NetworkInfo.LanUrls(Port);

        [DontSerialize]
        public MqttConfig Mqtt => new MqttConfig
        {
            Host = MqttHost?.Trim(),
            Port = MqttPort,
            Username = MqttUsername?.Trim(),
            Password = MqttPassword,
            BaseTopic = MqttBaseTopic,
            DiscoveryPrefix = MqttDiscoveryPrefix,
            AcceptCommands = MqttCommands,
        };

        // Used by the serializer.
        public CouchPilotSettings() { }

        public CouchPilotSettings(CouchPilotPlugin plugin)
        {
            this.plugin = plugin;
            var saved = plugin.LoadPluginSettings<CouchPilotSettings>();
            if (saved != null)
            {
                CopyFrom(saved);
            }
        }

        public void BeginEdit()
        {
            editing = new CouchPilotSettings();
            editing.CopyFrom(this);
        }

        public void CancelEdit()
        {
            CopyFrom(editing);
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
            if (Port != editing.Port)
            {
                plugin.RestartServer();
            }
            if (MqttChanged(editing))
            {
                plugin.RestartHomeAssistant();
            }
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (Port < 1024 || Port > 65535)
            {
                errors.Add("La porta deve essere tra 1024 e 65535.");
            }
            if (MqttEnabled && string.IsNullOrWhiteSpace(MqttHost))
            {
                errors.Add("Per Home Assistant serve l'indirizzo del broker MQTT.");
            }
            if (MqttEnabled && (MqttPort < 1 || MqttPort > 65535))
            {
                errors.Add("La porta del broker MQTT non è valida.");
            }
            return errors.Count == 0;
        }

        private bool MqttChanged(CouchPilotSettings before) =>
            MqttEnabled != before.MqttEnabled || MqttHost != before.MqttHost || MqttPort != before.MqttPort
            || MqttCommands != before.MqttCommands || MqttUsername != before.MqttUsername || MqttPassword != before.MqttPassword
            || MqttBaseTopic != before.MqttBaseTopic || MqttDiscoveryPrefix != before.MqttDiscoveryPrefix;

        private void CopyFrom(CouchPilotSettings other)
        {
            Port = other.Port;
            MqttEnabled = other.MqttEnabled;
            MqttHost = other.MqttHost;
            MqttPort = other.MqttPort;
            MqttUsername = other.MqttUsername;
            MqttPassword = other.MqttPassword;
            MqttCommands = other.MqttCommands;
            MqttBaseTopic = other.MqttBaseTopic;
            MqttDiscoveryPrefix = other.MqttDiscoveryPrefix;
        }
    }
}
