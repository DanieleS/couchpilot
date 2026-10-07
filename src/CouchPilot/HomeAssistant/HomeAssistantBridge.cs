using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.Library;
using MQTTnet;
using MQTTnet.Client;
using Playnite.SDK;
using Swan.Formatters;
using static CouchPilot.Lang;

namespace CouchPilot.HomeAssistant
{
    public class MqttConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string BaseTopic { get; set; }
        public string DiscoveryPrefix { get; set; }
        // Let Home Assistant drive the PC (presets, closing and starting games), not just watch it.
        public bool AcceptCommands { get; set; }
    }

    // Publishes what the PC is doing to an MQTT broker, with Home Assistant discovery so the PC
    // shows up as a device on its own; optionally takes commands back on <base>/<pc>/command/*.
    public class HomeAssistantBridge : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
        // What the display preset reads when the screens match none of the saved ones.
        private static string Custom => Pc("Custom", "Personalizzato");

        private readonly MqttConfig config;
        private readonly LibraryService library;
        private readonly DisplayService displays;
        private readonly GameControl games;
        private readonly string version;
        private readonly string node;
        private readonly string stateTopic;
        private readonly string availabilityTopic;
        private readonly string commandTopic;
        private readonly IMqttClient client;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly SemaphoreSlim publishLock = new SemaphoreSlim(1, 1);
        private Timer poll;
        private string lastState;
        private string lastPresetNames;

        public HomeAssistantBridge(MqttConfig config, LibraryService library, DisplayService displays, GameControl games, string version)
        {
            this.config = config;
            this.library = library;
            this.displays = displays;
            this.games = games;
            this.version = version;
            node = Regex.Replace(Environment.MachineName.ToLowerInvariant(), "[^a-z0-9_]+", "_");
            var baseTopic = string.IsNullOrWhiteSpace(config.BaseTopic) ? "couchpilot" : config.BaseTopic.Trim('/');
            stateTopic = $"{baseTopic}/{node}/state";
            availabilityTopic = $"{baseTopic}/{node}/availability";
            commandTopic = $"{baseTopic}/{node}/command";
            client = new MqttFactory().CreateMqttClient();
            client.ApplicationMessageReceivedAsync += OnCommand;
        }

        public void Start()
        {
            _ = KeepConnectedAsync();
            poll = new Timer(_ => _ = PublishStateAsync(), null, PollInterval, PollInterval);
        }

        // Push right away instead of waiting for the next poll, e.g. when a game starts.
        public void Nudge() => _ = PublishStateAsync();

        public void Dispose()
        {
            cts.Cancel();
            poll?.Dispose();
            try
            {
                if (client.IsConnected)
                {
                    // A clean disconnect skips the last will, so say goodbye ourselves.
                    Publish(availabilityTopic, "offline", true).Wait(TimeSpan.FromSeconds(2));
                    client.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "MQTT: disconnect failed");
            }
            client.Dispose();
        }

        // The broker can be down, restarting, or not there yet when Playnite starts:
        // check every few seconds and (re)connect whenever we're not connected.
        private async Task KeepConnectedAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                if (!client.IsConnected)
                {
                    await ConnectAsync();
                }
                try
                {
                    await Task.Delay(ReconnectDelay, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task ConnectAsync()
        {
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(config.Host, config.Port)
                .WithClientId($"couchpilot-{node}")
                .WithCleanSession()
                .WithWillTopic(availabilityTopic)
                .WithWillPayload("offline")
                .WithWillRetain(true);
            if (!string.IsNullOrEmpty(config.Username))
            {
                builder = builder.WithCredentials(config.Username, config.Password ?? "");
            }

            try
            {
                await client.ConnectAsync(builder.Build(), cts.Token);
                logger.Info($"MQTT: connected to {config.Host}:{config.Port}");
                if (config.AcceptCommands)
                {
                    var subscribe = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic($"{commandTopic}/#"))
                        .Build();
                    await client.SubscribeAsync(subscribe, cts.Token);
                }
                lastPresetNames = null;
                await PublishDiscovery();
                await Publish(availabilityTopic, "online", true);
                lastState = null;
                await PublishStateAsync();
            }
            catch (Exception e) when (!cts.IsCancellationRequested)
            {
                logger.Warn($"MQTT: cannot connect to {config.Host}:{config.Port}: {e.Message}");
            }
        }

        private async Task PublishStateAsync()
        {
            if (!client.IsConnected) return;
            string json;
            try
            {
                json = Json.Serialize(BuildState());
            }
            catch (Exception e)
            {
                logger.Warn(e, "MQTT: could not read the current state");
                return;
            }
            var presetNames = PresetNames();
            if (json == lastState && presetNames == lastPresetNames) return;

            await publishLock.WaitAsync();
            try
            {
                if (presetNames != lastPresetNames)
                {
                    await PublishDiscovery();
                }
                if (json != lastState)
                {
                    await Publish(stateTopic, json, true);
                    lastState = json;
                }
            }
            catch (Exception e)
            {
                logger.Warn($"MQTT: publish failed: {e.Message}");
            }
            finally
            {
                publishLock.Release();
            }
        }

        private string PresetNames() => string.Join("\n", displays.Snapshot().Presets.Select(p => p.Name));

        private async Task OnCommand(MqttApplicationMessageReceivedEventArgs args)
        {
            var topic = args.ApplicationMessage.Topic;
            if (!config.AcceptCommands || !topic.StartsWith(commandTopic + "/")) return;
            var command = topic.Substring(commandTopic.Length + 1);
            var payload = (args.ApplicationMessage.ConvertPayloadToString() ?? "").Trim();
            logger.Info($"MQTT: command {command} '{payload}'");

            try
            {
                var running = library.Running().FirstOrDefault();
                switch (command)
                {
                    case "display_preset":
                        var presetId = displays.FindPresetId(payload);
                        if (presetId == null) logger.Warn($"MQTT: no display preset named '{payload}'");
                        else displays.Apply(presetId, keep: true);
                        break;
                    case "close_game":
                        if (running != null) games.Stop(Guid.Parse(running.Id));
                        break;
                    case "focus_game":
                        if (running != null) games.Focus(Guid.Parse(running.Id));
                        break;
                    case "start_game":
                        var gameId = library.Resolve(payload);
                        if (gameId == null) logger.Warn($"MQTT: no game matches '{payload}'");
                        else if (running == null) games.Start(gameId.Value);
                        break;
                    default:
                        logger.Warn($"MQTT: unknown command '{command}'");
                        break;
                }
            }
            catch (Exception e)
            {
                // A Vibepollo stream refusing a preset lands here too: log it and carry on.
                logger.Warn($"MQTT: command {command} failed: {e.Message}");
            }

            await PublishStateAsync();
        }

        private Dictionary<string, object> BuildState()
        {
            var game = library.Running().FirstOrDefault();
            var screens = displays.Snapshot();
            var preset = screens.Presets.FirstOrDefault(p => p.Id == screens.ActivePresetId);
            return new Dictionary<string, object>
            {
                ["playing"] = game != null,
                ["game"] = game?.Name,
                ["game_id"] = game?.Id,
                ["source"] = game?.Source,
                ["started_at"] = game?.StartedAt,
                ["preset"] = preset?.Name,
                ["monitors"] = screens.Monitors.Count(m => !m.Virtual),
                ["streaming"] = screens.StreamActive,
            };
        }

        private async Task PublishDiscovery()
        {
            var device = new Dictionary<string, object>
            {
                ["identifiers"] = new[] { $"couchpilot_{node}" },
                ["name"] = $"CouchPilot {Environment.MachineName}",
                ["manufacturer"] = "CouchPilot",
                ["model"] = "Playnite extension",
                ["sw_version"] = version,
            };

            Dictionary<string, object> Entity(string id, string name, string icon, string valueTemplate)
            {
                return new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["unique_id"] = $"couchpilot_{node}_{id}",
                    ["object_id"] = $"couchpilot_{node}_{id}",
                    ["icon"] = icon,
                    ["state_topic"] = stateTopic,
                    ["value_template"] = valueTemplate,
                    ["availability_topic"] = availabilityTopic,
                    ["device"] = device,
                };
            }

            var game = Entity("game", Pc("Game", "Gioco"), "mdi:gamepad-variant", $"{{{{ value_json.get('game') or '{Pc("None", "Nessuno")}' }}}}");
            game["json_attributes_topic"] = stateTopic;
            game["json_attributes_template"] =
                "{{ {'game_id': value_json.get('game_id'), 'source': value_json.get('source'), 'started_at': value_json.get('started_at')} | tojson }}";

            var playing = Entity("playing", Pc("Playing", "In gioco"), "mdi:controller", "{{ 'ON' if value_json.get('playing') else 'OFF' }}");
            playing["device_class"] = "running";

            var preset = Entity("display_preset", Pc("Display preset", "Preset schermo"), "mdi:monitor-multiple",
                $"{{{{ value_json.get('preset') or '{Custom}' }}}}");
            preset["json_attributes_topic"] = stateTopic;
            preset["json_attributes_template"] = "{{ {'monitors': value_json.get('monitors')} | tojson }}";

            var streaming = Entity("streaming", Pc("Vibepollo stream", "Stream Vibepollo"), "mdi:cast", "{{ 'ON' if value_json.get('streaming') else 'OFF' }}");

            var prefix = string.IsNullOrWhiteSpace(config.DiscoveryPrefix) ? "homeassistant" : config.DiscoveryPrefix.Trim('/');

            // Commands: a select for the display preset, buttons for the running game.
            // When commands are off, empty retained configs remove these entities from HA.
            var selectTopic = $"{prefix}/select/couchpilot_{node}/display_preset_select/config";
            var closeTopic = $"{prefix}/button/couchpilot_{node}/close_game/config";
            var focusTopic = $"{prefix}/button/couchpilot_{node}/focus_game/config";
            if (config.AcceptCommands)
            {
                var names = displays.Snapshot().Presets.Select(p => p.Name).ToList();
                var select = Entity("display_preset_select", Pc("Switch display", "Cambia schermo"), "mdi:monitor-multiple",
                    $"{{{{ value_json.get('preset') or '{Custom}' }}}}");
                select["command_topic"] = $"{commandTopic}/display_preset";
                select["options"] = names.Concat(new[] { Custom }).Distinct().ToList();

                var close = Entity("close_game", Pc("Close game", "Chiudi gioco"), "mdi:close-box", null);
                close.Remove("state_topic");
                close.Remove("value_template");
                close["command_topic"] = $"{commandTopic}/close_game";

                var focus = Entity("focus_game", Pc("Bring to front", "Primo piano"), "mdi:open-in-app", null);
                focus.Remove("state_topic");
                focus.Remove("value_template");
                focus["command_topic"] = $"{commandTopic}/focus_game";

                await Publish(selectTopic, Json.Serialize(select), true);
                await Publish(closeTopic, Json.Serialize(close), true);
                await Publish(focusTopic, Json.Serialize(focus), true);
                lastPresetNames = string.Join("\n", names);
            }
            else
            {
                await Publish(selectTopic, "", true);
                await Publish(closeTopic, "", true);
                await Publish(focusTopic, "", true);
                lastPresetNames = PresetNames();
            }
            await Publish($"{prefix}/sensor/couchpilot_{node}/game/config", Json.Serialize(game), true);
            await Publish($"{prefix}/binary_sensor/couchpilot_{node}/playing/config", Json.Serialize(playing), true);
            await Publish($"{prefix}/sensor/couchpilot_{node}/display_preset/config", Json.Serialize(preset), true);
            await Publish($"{prefix}/binary_sensor/couchpilot_{node}/streaming/config", Json.Serialize(streaming), true);
        }

        private Task Publish(string topic, string payload, bool retain)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .Build();
            return client.PublishAsync(message, cts.Token);
        }
    }
}
