using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CouchPilot.Audio;
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
        private readonly LiveSocket live;
        private readonly X509Certificate2 certificate;

        // With a certificate the port speaks HTTPS only: one port can't carry both, and the
        // phone's browser needs HTTPS to install the app.
        public RemoteServer(int port, X509Certificate2 certificate, string webRoot, LibraryService library, GameControl games, DisplayService displays, RemoteInput input, AudioService audio)
        {
            this.certificate = certificate;
            Swan.Logging.Logger.NoLogging();
            var status = new StatusSource(library, displays, input);
            live = new LiveSocket("/ws", input, audio, status);

            // EmbedIO's own listener: unlike HttpListener it can bind every interface
            // without an admin-only `netsh http add urlacl`.
            server = new WebServer(o =>
                {
                    o.WithUrlPrefix($"{(certificate != null ? "https" : "http")}://*:{port}/")
                        .WithMode(HttpListenerMode.EmbedIO);
                    if (certificate != null) o.WithCertificate(certificate);
                })
                .WithCors()
                .WithWebApi("/api", SerializeJson, m => m.WithController(() => new ApiController(library, games, displays, audio, status, live)))
                .WithModule(live);

            if (Directory.Exists(webRoot))
            {
                // EmbedIO doesn't know the manifest's extension and would serve it as a download.
                server.WithStaticFolder("/", webRoot, false, m => m.WithCustomMimeType(".webmanifest", "application/manifest+json"));
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

        // Something changed on the PC: phones hear about it now rather than on the next check.
        public void Nudge() => live.Nudge();

        public void LibraryChanged() => live.LibraryChanged();

        public void Dispose()
        {
            cts.Cancel();
            server.Dispose();
            // Deletes the private key Windows unpacked from the .pfx.
            certificate?.Dispose();
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
