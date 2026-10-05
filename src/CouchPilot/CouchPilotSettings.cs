using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot
{
    public class CouchPilotSettings : ObservableObject, ISettings
    {
        private readonly CouchPilotPlugin plugin;
        private int editingPort;

        private int port = 8642;
        public int Port { get => port; set => SetValue(ref port, value); }

        [DontSerialize]
        public IReadOnlyList<string> Addresses => Server.NetworkInfo.LanUrls(Port);

        // Used by the serializer.
        public CouchPilotSettings() { }

        public CouchPilotSettings(CouchPilotPlugin plugin)
        {
            this.plugin = plugin;
            var saved = plugin.LoadPluginSettings<CouchPilotSettings>();
            if (saved != null)
            {
                Port = saved.Port;
            }
        }

        public void BeginEdit()
        {
            editingPort = Port;
        }

        public void CancelEdit()
        {
            Port = editingPort;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(this);
            if (Port != editingPort)
            {
                plugin.RestartServer();
            }
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (Port < 1024 || Port > 65535)
            {
                errors.Add("La porta deve essere tra 1024 e 65535.");
            }
            return errors.Count == 0;
        }
    }
}
