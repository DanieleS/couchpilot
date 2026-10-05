using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CouchPilot.Audio;
using CouchPilot.Input;
using EmbedIO.WebSockets;
using Playnite.SDK;

namespace CouchPilot.Server
{
    // The phone's one WebSocket (/ws): mouse and keyboard up, the PC's state down, audio both ways.
    // The wire format is specified in docs/protocol.md.
    //
    // One loop serves every phone, ticking 25 times a second while any is connected: it pushes
    // STATUS when the PC's state changes, and to phones subscribed to audio the mixer's STATE,
    // VOLUMES and LEVELS. Each phone has its own send queue, so a slow one never holds up the
    // others; levels are skipped for a phone still busy with the previous ones, since a late
    // level is worth nothing.
    public class LiveSocket : WebSocketModule
    {
        public const byte ProtocolVersion = 1;

        // Phone → PC
        private const byte OpInputFirst = 0x01;
        private const byte OpInputLast = 0x0F;
        private const byte OpAudioFirst = 0x10;
        private const byte OpAudioLast = 0x1F;
        private const byte OpSubscribe = 0x20;
        private const byte SubscribeAudio = 1;
        // PC → phone
        private const byte OpHello = 0x80;
        private const byte OpStatus = 0x81;

        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(40);
        private const int StatusEveryTicks = 12;  // ~0.5 s
        private const int RescanEveryTicks = 25;  // 1 s

        private readonly RemoteInput input;
        private readonly AudioService audio;
        private readonly StatusSource status;
        private readonly ConcurrentDictionary<IWebSocketContext, Client> clients = new ConcurrentDictionary<IWebSocketContext, Client>();
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private int looping;
        private volatile bool statusWanted;
        private volatile bool rescanWanted;
        private string lastStatusShape;
        private byte[] lastVolumes;
        private byte[] lastLevels;

        private class Client
        {
            public volatile bool Audio;
            public volatile bool NeedsAudioState;
            public Task Queue = Task.CompletedTask;
        }

        public LiveSocket(string path, RemoteInput input, AudioService audio, StatusSource status) : base(path, true)
        {
            this.input = input;
            this.audio = audio;
            this.status = status;
            MaxMessageSize = 16 * 1024;
        }

        // Something changed on the PC (a game started, a preset applied): push STATUS on the next tick.
        public void Nudge() => statusWanted = true;

        protected override Task OnClientConnectedAsync(IWebSocketContext context)
        {
            // WebSockets skip the browser's same-origin checks: without this, any page open on a
            // phone or PC on the network could type on this one.
            if (!SameOrigin(context)) return CloseAsync(context);

            var client = new Client();
            clients[context] = client;
            Enqueue(context, client, new[] { OpHello, ProtocolVersion });
            Enqueue(context, client, StatusMessage(status.Encode().json));
            if (Interlocked.Exchange(ref looping, 1) == 0) Task.Run(LoopAsync);
            return Task.CompletedTask;
        }

        protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            // The protocol is binary only: a text frame isn't ours, whatever its first character.
            // EmbedIO reports System.Net's WebSocketMessageType here, not the frame's opcode.
            if (result.MessageType == (int)WebSocketMessageType.Text) return Task.CompletedTask;
            if (buffer.Length == 0 || !clients.TryGetValue(context, out var client)) return Task.CompletedTask;
            var op = buffer[0];
            try
            {
                if (op >= OpInputFirst && op <= OpInputLast)
                {
                    // Dropped, not refused, when mouse and keyboard are off: the socket is for audio too.
                    input.Handle(buffer);
                }
                else if (op >= OpAudioFirst && op <= OpAudioLast)
                {
                    if (audio.Handle(buffer)) rescanWanted = true;
                }
                else if (op == OpSubscribe && buffer.Length >= 2)
                {
                    var wantsAudio = (buffer[1] & SubscribeAudio) != 0;
                    if (wantsAudio && !client.Audio) client.NeedsAudioState = true;
                    client.Audio = wantsAudio;
                }
                // Anything else is from a newer phone: ignored, as the protocol promises.
            }
            catch (Exception e)
            {
                logger.Warn(e, $"Ignoring message {BitConverter.ToString(buffer, 0, Math.Min(buffer.Length, 16))}");
            }
            return Task.CompletedTask;
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            clients.TryRemove(context, out _);
            // A phone that drops mid-drag must not leave a mouse button stuck down.
            input.ReleaseAll();
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            stop.Cancel();
            base.Dispose(disposing);
        }

        private async Task LoopAsync()
        {
            for (var tick = 0; !stop.IsCancellationRequested; tick++)
            {
                if (clients.IsEmpty)
                {
                    Interlocked.Exchange(ref looping, 0);
                    // A phone may have connected between the check and the reset: then it's ours to serve.
                    if (clients.IsEmpty || Interlocked.Exchange(ref looping, 1) == 1) return;
                }
                try
                {
                    if (statusWanted || tick % StatusEveryTicks == 0) PushStatus();
                    PushAudio(tick);
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Live socket tick failed");
                }
                try { await Task.Delay(Tick, stop.Token); } catch (TaskCanceledException) { return; }
            }
        }

        private void PushStatus()
        {
            statusWanted = false;
            var (json, shape) = status.Encode();
            if (shape == lastStatusShape) return;
            lastStatusShape = shape;
            var message = StatusMessage(json);
            foreach (var pair in clients) Enqueue(pair.Key, pair.Value, message);
        }

        private void PushAudio(int tick)
        {
            var listeners = clients.Where(c => c.Value.Audio).ToList();
            if (listeners.Count == 0)
            {
                lastVolumes = lastLevels = null;
                return;
            }

            var newcomers = listeners.Where(c => c.Value.NeedsAudioState).ToList();
            var changed = false;
            if (rescanWanted || audio.Stale || newcomers.Count > 0 || tick % RescanEveryTicks == 0)
            {
                rescanWanted = false;
                changed = audio.Rescan();
                var state = audio.State();
                foreach (var pair in changed ? listeners : newcomers)
                {
                    pair.Value.NeedsAudioState = false;
                    Enqueue(pair.Key, pair.Value, state);
                }
                // STATE carries the volumes; levels start over for the new channel list.
                if (changed)
                {
                    lastVolumes = audio.Volumes();
                    lastLevels = null;
                }
            }
            else
            {
                audio.Refresh();
            }

            if (!changed)
            {
                var volumes = audio.Volumes();
                if (lastVolumes == null || !volumes.SequenceEqual(lastVolumes))
                {
                    lastVolumes = volumes;
                    foreach (var pair in listeners) Enqueue(pair.Key, pair.Value, volumes);
                }
            }

            var levels = audio.Levels();
            if (lastLevels != null && levels.SequenceEqual(lastLevels)) return;
            lastLevels = levels;
            foreach (var pair in listeners)
            {
                if (pair.Value.Queue.IsCompleted) Enqueue(pair.Key, pair.Value, levels);
            }
        }

        // Messages to one phone go out in order, one at a time, without the loop waiting on them.
        private void Enqueue(IWebSocketContext context, Client client, byte[] message)
        {
            lock (client)
            {
                client.Queue = client.Queue.ContinueWith(_ => SendAsync(context, message), TaskScheduler.Default).Unwrap();
            }
        }

        private static byte[] StatusMessage(string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var message = new byte[body.Length + 1];
            message[0] = OpStatus;
            Buffer.BlockCopy(body, 0, message, 1, body.Length);
            return message;
        }

        private static bool SameOrigin(IWebSocketContext context)
        {
            var origin = context.Origin;
            if (string.IsNullOrEmpty(origin)) return true;
            var host = context.Headers["Host"];
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);
        }
    }
}
