using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CouchPilot.Audio;
using CouchPilot.Captures;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.Input;
using CouchPilot.Library;
using CouchPilot.Media;
using CouchPilot.Stats;
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
        public RemoteServer(int port, X509Certificate2 certificate, string webRoot, LibraryService library, GameControl games, DisplayService displays, RemoteInput input, AudioService audio, MediaService media, StatsService stats, CaptureService captures, CaptureLibrary captureLibrary, Thumbnails thumbnails, ClipEditor clips, PairingService pairing, Achievements.AchievementsService achievements, Unlock.UnlockService unlock)
        {
            this.certificate = certificate;
            Swan.Logging.Logger.NoLogging();
            var status = new StatusSource(library, displays, input, captures, clips, pairing, achievements, unlock);
            live = new LiveSocket("/ws", input, audio, status, pairing);
            // In front of everything: while pairing is on, /api answers only phones that typed the PIN.
            var gate = new PairingGate(pairing, certificate != null);

            // EmbedIO's own listener: unlike HttpListener it can bind every interface
            // without an admin-only `netsh http add urlacl`.
            server = new WebServer(o =>
                {
                    o.WithUrlPrefix($"{(certificate != null ? "https" : "http")}://*:{port}/")
                        .WithMode(HttpListenerMode.EmbedIO);
                    if (certificate != null) o.WithCertificate(certificate);
                })
                .WithModule(gate)
                .WithCors()
                .WithWebApi("/api", SerializeJson, m => m.WithController(() => new ApiController(library, games, displays, audio, media, stats, status, live, captures, captureLibrary, thumbnails, clips, pairing, gate, achievements, unlock)))
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
                if (ex is CaptureException capture)
                {
                    // The code tells the phone what to show: how to turn the replay on, say.
                    ctx.Response.StatusCode = 409;
                    await SerializeJson(ctx, new { error = ex.Message, code = capture.Code });
                    return;
                }
                if (ex is PairingException pairing)
                {
                    // wrong-pin, expired, too-soon: the pairing screen says what to do next.
                    ctx.Response.StatusCode = pairing.Code == "wrong-pin" ? 403 : 409;
                    await SerializeJson(ctx, new { error = ex.Message, code = pairing.Code, attemptsLeft = pairing.AttemptsLeft });
                    return;
                }
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

        public void MediaChanged() => live.MediaChanged();

        public void CapturesChanged() => live.CapturesChanged();

        public void AchievementsChanged() => live.AchievementsChanged();

        public void StatsChanged() => live.StatsChanged();

        // Pairing was just switched on: phones connected without a token are let go.
        public void DropUnpaired() => live.DropUnpaired();

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
