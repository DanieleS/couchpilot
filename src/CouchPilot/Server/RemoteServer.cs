using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.Input;
using CouchPilot.Library;
using EmbedIO;
using EmbedIO.WebApi;
using Swan.Formatters;

namespace CouchPilot.Server
{
    public class RemoteServer : IDisposable
    {
        private readonly WebServer server;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        public RemoteServer(int port, string webRoot, LibraryService library, GameControl games, DisplayService displays, RemoteInput input)
        {
            Swan.Logging.Logger.NoLogging();

            // EmbedIO's own listener: unlike HttpListener it can bind every interface
            // without an admin-only `netsh http add urlacl`.
            server = new WebServer(o => o
                    .WithUrlPrefix($"http://*:{port}/")
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithCors()
                .WithWebApi("/api", SerializeJson, m => m.WithController(() => new ApiController(library, games, displays, input)))
                .WithModule(new InputSocket("/ws/input", input));

            if (Directory.Exists(webRoot))
            {
                server.WithStaticFolder("/", webRoot, false);
            }

            server.HandleHttpException(async (ctx, ex) =>
            {
                ctx.Response.StatusCode = ex.StatusCode;
                await SerializeJson(ctx, new { error = ex.Message ?? ((System.Net.HttpStatusCode)ex.StatusCode).ToString() });
            });
            server.HandleUnhandledException(async (ctx, ex) =>
            {
                ctx.Response.StatusCode = ex is DisplayException ? 409 : 500;
                await SerializeJson(ctx, new { error = ex.Message });
            });
        }

        public void Start()
        {
            // A bind error (port in use) faults the task almost immediately; surface it to
            // the caller instead of failing silently in the background.
            var run = server.RunAsync(cts.Token);
            if (run.Wait(TimeSpan.FromMilliseconds(500))) run.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            cts.Cancel();
            server.Dispose();
        }

        internal static async Task SerializeJson(IHttpContext ctx, object data)
        {
            ctx.Response.ContentType = MimeType.Json;
            using (var writer = ctx.OpenResponseText(new UTF8Encoding(false)))
            {
                await writer.WriteAsync(Json.Serialize(data, JsonSerializerCase.CamelCase));
            }
        }
    }
}
