using System;
using System.Threading.Tasks;
using CouchPilot.Input;
using EmbedIO.WebSockets;

namespace CouchPilot.Server
{
    // The touchpad streams dozens of small commands a second: one WebSocket instead of a
    // request each keeps the pointer from lagging behind the finger.
    public class InputSocket : WebSocketModule
    {
        private readonly RemoteInput input;

        public InputSocket(string path, RemoteInput input) : base(path, true)
        {
            this.input = input;
            MaxMessageSize = 16 * 1024;
        }

        protected override async Task OnClientConnectedAsync(IWebSocketContext context)
        {
            // WebSockets skip the browser's same-origin checks: without this, any page open on a
            // phone or PC on the network could type on this one.
            if (!input.Enabled || !SameOrigin(context))
            {
                await CloseAsync(context);
            }
        }

        protected override async Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
        {
            if (!input.Enabled)
            {
                await CloseAsync(context);
                return;
            }
            input.Handle(buffer);
        }

        protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
        {
            input.ReleaseAll();
            return Task.CompletedTask;
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
