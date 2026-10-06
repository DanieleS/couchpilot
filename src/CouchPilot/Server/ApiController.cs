using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using CouchPilot.Audio;
using CouchPilot.Captures;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.Library;
using CouchPilot.Media;
using CouchPilot.Stats;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Swan.Formatters;

namespace CouchPilot.Server
{
    public class ApiController : WebApiController
    {
        private readonly LibraryService library;
        private readonly GameControl games;
        private readonly DisplayService displays;
        private readonly AudioService audio;
        private readonly MediaService media;
        private readonly StatsService stats;
        private readonly StatusSource status;
        private readonly LiveSocket live;
        private readonly CaptureService captures;
        private readonly CaptureLibrary captureLibrary;
        private readonly Thumbnails thumbnails;

        public ApiController(LibraryService library, GameControl games, DisplayService displays, AudioService audio, MediaService media, StatsService stats, StatusSource status, LiveSocket live, CaptureService captures, CaptureLibrary captureLibrary, Thumbnails thumbnails)
        {
            this.captures = captures;
            this.captureLibrary = captureLibrary;
            this.thumbnails = thumbnails;
            this.library = library;
            this.games = games;
            this.displays = displays;
            this.audio = audio;
            this.media = media;
            this.stats = stats;
            this.status = status;
            this.live = live;
        }

        // The phone fetches it again whenever the live socket says it changed.
        [Route(HttpVerbs.Get, "/status")]
        public object Status() => status.Snapshot();

        [Route(HttpVerbs.Get, "/games")]
        public object Games() => library.All();

        [Route(HttpVerbs.Get, "/games/{id}")]
        public object Game(string id) => library.Get(ParseId(id)) ?? throw HttpException.NotFound("Gioco non trovato.");

        // Playnite's totals for the game, and what CouchPilot logged of its sessions.
        [Route(HttpVerbs.Get, "/games/{id}/stats")]
        public object GameStats(string id) => stats.Game(ParseId(id)) ?? throw HttpException.NotFound("Gioco non trovato.");

        // ?range=week|month|year&offset=0 (−1 the one before, and so on).
        [Route(HttpVerbs.Get, "/stats")]
        public object Stats([QueryField] string range, [QueryField] int offset) => stats.Range(range, Math.Max(-500, Math.Min(0, offset)));

        [Route(HttpVerbs.Get, "/games/{id}/cover")]
        public Task Cover(string id) => SendImage(id, ImageKind.Cover);

        [Route(HttpVerbs.Get, "/games/{id}/background")]
        public Task Background(string id) => SendImage(id, ImageKind.Background);

        [Route(HttpVerbs.Get, "/games/{id}/icon")]
        public Task Icon(string id) => SendImage(id, ImageKind.Icon);

        [Route(HttpVerbs.Post, "/games/{id}/start")]
        public void Start(string id)
        {
            var game = library.Get(ParseId(id)) ?? throw HttpException.NotFound("Gioco non trovato.");
            if (!game.Installed) throw new HttpException(HttpStatusCode.Conflict, "Il gioco non è installato.");
            if (game.Running) throw new HttpException(HttpStatusCode.Conflict, "Il gioco è già in esecuzione.");
            games.Start(ParseId(id));
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/games/{id}/install")]
        public void Install(string id)
        {
            var game = library.Get(ParseId(id)) ?? throw HttpException.NotFound("Gioco non trovato.");
            if (game.Installed) throw new HttpException(HttpStatusCode.Conflict, "Il gioco è già installato.");
            if (game.Installing || game.Uninstalling) throw new HttpException(HttpStatusCode.Conflict, "Playnite ci sta già lavorando.");
            games.Install(ParseId(id));
        }

        [Route(HttpVerbs.Post, "/games/{id}/uninstall")]
        public void Uninstall(string id)
        {
            var game = library.Get(ParseId(id)) ?? throw HttpException.NotFound("Gioco non trovato.");
            if (!game.Installed) throw new HttpException(HttpStatusCode.Conflict, "Il gioco non è installato.");
            if (game.Running) throw new HttpException(HttpStatusCode.Conflict, "Chiudi il gioco prima di disinstallarlo.");
            if (game.Installing || game.Uninstalling) throw new HttpException(HttpStatusCode.Conflict, "Playnite ci sta già lavorando.");
            games.Uninstall(ParseId(id));
        }

        [Route(HttpVerbs.Post, "/games/{id}/stop")]
        public void Stop(string id)
        {
            if (!games.Stop(ParseId(id))) throw new HttpException(HttpStatusCode.Conflict, "Non trovo processi di questo gioco.");
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/games/{id}/focus")]
        public void Focus(string id)
        {
            if (!games.Focus(ParseId(id))) throw new HttpException(HttpStatusCode.Conflict, "Non trovo la finestra del gioco.");
        }

        [Route(HttpVerbs.Post, "/displays/presets")]
        public async Task<object> SavePreset()
        {
            var body = await ReadBody<NameBody>();
            var preset = displays.SaveCurrent(body?.Name);
            live.Nudge();
            return preset;
        }

        [Route(HttpVerbs.Patch, "/displays/presets/{id}")]
        public async Task RenamePreset(string id)
        {
            var body = await ReadBody<NameBody>();
            if (!displays.Rename(id, body?.Name)) throw HttpException.NotFound("Preset non trovato.");
            live.Nudge();
        }

        // { "outputId": "…", "outputName": "…" } to switch audio with this preset, or
        // { "outputId": null } to leave the audio alone.
        [Route(HttpVerbs.Put, "/displays/presets/{id}/audio")]
        public async Task SetPresetAudio(string id)
        {
            var body = await ReadBody<PresetAudioBody>();
            if (!displays.SetAudioOutput(id, body?.OutputId, body?.OutputName)) throw HttpException.NotFound("Preset non trovato.");
            live.Nudge();
        }

        [Route(HttpVerbs.Delete, "/displays/presets/{id}")]
        public void DeletePreset(string id)
        {
            if (!displays.Delete(id)) throw HttpException.NotFound("Preset non trovato.");
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/displays/presets/{id}/apply")]
        public void ApplyPreset(string id)
        {
            if (!displays.Apply(id)) throw HttpException.NotFound("Preset non trovato.");
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/displays/confirm")]
        public void Confirm()
        {
            displays.Confirm();
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/displays/revert")]
        public void Revert()
        {
            displays.Revert();
            live.Nudge();
        }

        // What's playing, or null. The live socket says when it changed.
        [Route(HttpVerbs.Get, "/media")]
        public object Media() => media.Snapshot();

        // The artwork of what's playing. The phone asks for it with ?v=<artVersion>, so the
        // URL changes with the art and the browser may keep it for good.
        [Route(HttpVerbs.Get, "/media/art")]
        public async Task MediaArt()
        {
            var (bytes, type) = media.Art();
            if (bytes == null) throw HttpException.NotFound();
            Response.ContentType = type;
            Response.Headers.Set("Cache-Control", "public, max-age=31536000, immutable");
            await Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        [Route(HttpVerbs.Post, "/media/play-pause")]
        public void MediaPlayPause()
        {
            if (!media.PlayPause()) throw new HttpException(HttpStatusCode.Conflict, "L'app non accetta il comando.");
        }

        [Route(HttpVerbs.Post, "/media/next")]
        public void MediaNext()
        {
            if (!media.Next()) throw new HttpException(HttpStatusCode.Conflict, "L'app non accetta il comando.");
        }

        [Route(HttpVerbs.Post, "/media/previous")]
        public void MediaPrevious()
        {
            if (!media.Previous()) throw new HttpException(HttpStatusCode.Conflict, "L'app non accetta il comando.");
        }

        // For picking a display preset's audio output; the live mixer has its own list.
        [Route(HttpVerbs.Get, "/audio/outputs")]
        public object AudioOutputs() => audio.Outputs();

        [Route(HttpVerbs.Get, "/audio/apps/{id}/icon")]
        public async Task AudioAppIcon(string id)
        {
            var png = audio.AppIcon(id) ?? throw HttpException.NotFound();
            Response.ContentType = "image/png";
            Response.Headers.Set("Cache-Control", "public, max-age=86400");
            await Response.OutputStream.WriteAsync(png, 0, png.Length);
        }

        // Every screenshot and clip, newest first. The live socket says when the list changed.
        [Route(HttpVerbs.Get, "/captures")]
        public object Captures()
        {
            RequireCaptures();
            return captureLibrary.List();
        }

        // The next three answer once the file is there, with the new capture, or with an error
        // whose code says what's wrong (see CaptureException).
        [Route(HttpVerbs.Post, "/captures/screenshot")]
        public Task<CaptureDto> Screenshot() => captures.Screenshot();

        [Route(HttpVerbs.Post, "/captures/replay")]
        public Task<CaptureDto> Replay() => captures.Replay();

        [Route(HttpVerbs.Post, "/captures/record/stop")]
        public Task<CaptureDto> StopRecording() => captures.StopRecording();

        [Route(HttpVerbs.Post, "/captures/record/start")]
        public Task StartRecording() => captures.StartRecording();

        // To the PC's Recycle Bin.
        [Route(HttpVerbs.Delete, "/captures/{id}")]
        public void DeleteCapture(string id)
        {
            RequireCaptures();
            if (!captureLibrary.Delete(id)) throw HttpException.NotFound("Cattura non trovata.");
            thumbnails.Forget(id);
        }

        // ?v=<version>: the URL changes with the file, so the phone may keep it for good.
        [Route(HttpVerbs.Get, "/captures/{id}/thumb")]
        public async Task CaptureThumbnail(string id)
        {
            RequireCaptures();
            var source = captureLibrary.ThumbnailOf(id) ?? throw HttpException.NotFound();
            var jpeg = await thumbnails.Get(id, source.Version, source.Video, source.Seconds, source.Path) ?? throw HttpException.NotFound();
            Response.ContentType = "image/jpeg";
            Response.Headers.Set("Cache-Control", "public, max-age=31536000, immutable");
            await Response.OutputStream.WriteAsync(jpeg, 0, jpeg.Length);
        }

        // The file itself. Videos are asked for in ranges: the player seeks by asking for the
        // bytes it needs, so a 5-minute clip starts at once and jumps anywhere.
        [Route(HttpVerbs.Get, "/captures/{id}/file")]
        public async Task CaptureFile(string id)
        {
            RequireCaptures();
            var path = captureLibrary.PathOf(id) ?? throw HttpException.NotFound("Cattura non trovata.");
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true))
            {
                var length = file.Length;
                long from = 0, to = length - 1;
                var range = Request.Headers["Range"];
                if (!string.IsNullOrEmpty(range))
                {
                    if (!ByteRange.TryParse(range, length, out from, out to))
                    {
                        Response.StatusCode = 416;
                        Response.Headers.Set("Content-Range", $"bytes */{length}");
                        return;
                    }
                    Response.StatusCode = 206;
                    Response.Headers.Set("Content-Range", $"bytes {from}-{to}/{length}");
                }
                var ext = Path.GetExtension(path);
                Response.ContentType = MimeType.Associations.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
                Response.Headers.Set("Accept-Ranges", "bytes");
                Response.Headers.Set("Cache-Control", "private, max-age=3600");
                Response.Headers.Set("Content-Disposition", $"inline; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(path))}");
                Response.SendChunked = false;
                Response.ContentLength64 = to - from + 1;

                file.Position = from;
                var buffer = new byte[64 * 1024];
                var left = to - from + 1;
                try
                {
                    while (left > 0)
                    {
                        var read = await file.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (read <= 0) break;
                        await Response.OutputStream.WriteAsync(buffer, 0, read);
                        left -= read;
                    }
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException)
                {
                    // The phone stopped listening: the player seeked elsewhere, or the viewer closed.
                }
            }
        }

        // Screenshots may show more than the game: they stay private unless turned on.
        private void RequireCaptures()
        {
            if (!captures.Enabled) throw new HttpException(HttpStatusCode.Forbidden, "Le catture sono spente nelle impostazioni di CouchPilot.");
        }

        public class NameBody
        {
            public string Name { get; set; }
        }

        public class PresetAudioBody
        {
            public string OutputId { get; set; }
            public string OutputName { get; set; }
        }

        private async Task<T> ReadBody<T>() where T : class
        {
            var text = await HttpContext.GetRequestBodyAsStringAsync();
            return string.IsNullOrWhiteSpace(text) ? null : Json.Deserialize<T>(text, JsonSerializerCase.CamelCase);
        }

        private async Task SendImage(string id, ImageKind kind)
        {
            var location = library.ImageLocation(ParseId(id), kind) ?? throw HttpException.NotFound();
            if (location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                throw HttpException.Redirect(location);
            }
            if (!File.Exists(location)) throw HttpException.NotFound();

            var ext = Path.GetExtension(location);
            Response.ContentType = MimeType.Associations.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
            Response.Headers.Set("Cache-Control", "public, max-age=86400");
            using (var file = File.OpenRead(location))
            {
                await file.CopyToAsync(Response.OutputStream);
            }
        }

        private static Guid ParseId(string id) =>
            Guid.TryParse(id, out var guid) ? guid : throw HttpException.BadRequest("Id non valido.");
    }
}
