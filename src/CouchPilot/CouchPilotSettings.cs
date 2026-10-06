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

        // A .pfx turns the server to HTTPS on the same port; empty keeps plain HTTP.
        private string certificatePath = "";
        public string CertificatePath { get => certificatePath; set => SetValue(ref certificatePath, value); }

        // Stored in plain text like the MQTT password.
        private string certificatePassword = "";
        public string CertificatePassword { get => certificatePassword; set => SetValue(ref certificatePassword, value); }

        // Phones have to type a PIN shown on the PC before they're let in; off, every phone on
        // the network is, as it always was.
        private bool requirePairing;
        public bool RequirePairing { get => requirePairing; set => SetValue(ref requirePairing, value); }

        // Touchpad and keyboard from the phone: off unless you turn it on.
        private bool remoteInput;
        public bool RemoteInput { get => remoteInput; set => SetValue(ref remoteInput, value); }

        // Screenshots and clips from the phone, and the gallery: off unless you turn it on, since
        // whoever reaches the port could see them.
        private bool captures;
        public bool Captures { get => captures; set => SetValue(ref captures, value); }

        private Captures.CaptureTool captureTool = CouchPilot.Captures.CaptureTool.GameBar;
        public Captures.CaptureTool CaptureTool { get => captureTool; set => SetValue(ref captureTool, value); }

        // A folder of your own shown in the gallery too, besides Game Bar's and NVIDIA's.
        private string captureFolder = "";
        public string CaptureFolder { get => captureFolder; set => SetValue(ref captureFolder, value); }

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
        public IReadOnlyList<string> Addresses => plugin?.Addresses ?? Server.NetworkInfo.LanUrls(Port);

        // The paired phones, for the settings view. Changes to them are saved at once, not with the settings.
        [DontSerialize]
        public Server.PairingService Pairing => plugin?.Pairing;

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
            if (Port != editing.Port || CertificatePath != editing.CertificatePath || CertificatePassword != editing.CertificatePassword)
            {
                plugin.RestartServer();
            }
            plugin.ApplyInputSetting();
            plugin.ApplyCaptureSettings();
            plugin.ApplyPairingSetting();
            if (MqttChanged(editing))
            {
                plugin.RestartHomeAssistant();
            }
        }

        internal void PickCertificate() => plugin?.PickCertificate();

        internal void PickCaptureFolder() => plugin?.PickCaptureFolder();

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (Port < 1024 || Port > 65535)
            {
                errors.Add("La porta deve essere tra 1024 e 65535.");
            }
            if (!string.IsNullOrWhiteSpace(CertificatePath))
            {
                try
                {
                    Server.ServerCertificate.Load(CertificatePath.Trim(), CertificatePassword).Dispose();
                }
                catch (System.InvalidOperationException e)
                {
                    errors.Add(e.Message);
                }
            }
            if (!string.IsNullOrWhiteSpace(CaptureFolder) && !System.IO.Directory.Exists(CaptureFolder.Trim()))
            {
                errors.Add("La cartella delle catture non esiste.");
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
            CertificatePath = other.CertificatePath;
            CertificatePassword = other.CertificatePassword;
            RequirePairing = other.RequirePairing;
            RemoteInput = other.RemoteInput;
            Captures = other.Captures;
            CaptureTool = other.CaptureTool;
            CaptureFolder = other.CaptureFolder;
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
