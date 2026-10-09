using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using CouchPilot.Achievements;
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
using static CouchPilot.Lang;

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
        private readonly ClipEditor clips;
        private readonly PairingService pairing;
        private readonly PairingGate gate;
        private readonly AchievementsService achievements;
        private readonly Unlock.UnlockService unlock;

        public ApiController(LibraryService library, GameControl games, DisplayService displays, AudioService audio, MediaService media, StatsService stats, StatusSource status, LiveSocket live, CaptureService captures, CaptureLibrary captureLibrary, Thumbnails thumbnails, ClipEditor clips, PairingService pairing, PairingGate gate, AchievementsService achievements, Unlock.UnlockService unlock)
        {
            this.unlock = unlock;
            this.captures = captures;
            this.captureLibrary = captureLibrary;
            this.thumbnails = thumbnails;
            this.clips = clips;
            this.pairing = pairing;
            this.gate = gate;
            this.achievements = achievements;
            this.library = library;
            this.games = games;
            this.displays = displays;
            this.audio = audio;
            this.media = media;
            this.stats = stats;
            this.status = status;
            this.live = live;
        }

        // Errors go back in the phone's language.
        protected override void OnBeforeHandler()
        {
            base.OnBeforeHandler();
            UseRequest(Request.Headers["Accept-Language"]);
        }

        // The paired phone this request came from, or null while pairing is off.
        private PairedDevice Device => PairingGate.DeviceOf(HttpContext);

        // The phone fetches it again whenever the live socket says it changed.
        [Route(HttpVerbs.Get, "/status")]
        public object Status() => status.Snapshot(Device);

        // The PC is on the lock screen and this phone may unlock it: hand the credentials to the
        // lock screen's credential provider. Reports the handoff, not whether the password was
        // right — that shows on the PC a moment later. See docs/unlock.md.
        [Route(HttpVerbs.Post, "/unlock")]
        public object Unlock()
        {
            // A specific paired phone with the Unlock permission, and nothing else. With pairing
            // off Device is null — so unlocking is simply unavailable without pairing, the same
            // way enabling it requires pairing to be on.
            var device = Device;
            if (device == null || !device.AllowUnlock)
            {
                Response.StatusCode = (int)HttpStatusCode.Forbidden;
                return new { error = Tr("This phone may not unlock the PC: pair it and allow Unlock in CouchPilot's settings.", "Questo telefono non può sbloccare il PC: abbinalo e consenti lo Sblocco nelle impostazioni di CouchPilot."), code = "forbidden" };
            }
            // Fully qualified: inside this method, the name "Unlock" binds to the method, not the
            // namespace.
            switch (unlock.TryUnlock())
            {
                case CouchPilot.Unlock.UnlockResult.Ok:
                    return new { ok = true };
                case CouchPilot.Unlock.UnlockResult.NotLocked:
                    Response.StatusCode = (int)HttpStatusCode.Conflict;
                    return new { error = Tr("The PC isn't locked.", "Il PC non è bloccato."), code = "not-locked" };
                default:
                    Response.StatusCode = (int)HttpStatusCode.Conflict;
                    return new { error = Tr("Unlocking isn't set up, or the lock screen didn't answer.", "Lo sblocco non è configurato, o la schermata di blocco non ha risposto."), code = "unavailable" };
            }
        }

        // Pairing. These are open to every phone: they're how a phone gets in.
        // A paired phone also gets its token back, to keep a copy of its own: see Resume.
        [Route(HttpVerbs.Get, "/pair")]
        public object Pairing()
        {
            var token = Request.Cookies[PairingService.CookieName]?.Value;
            var device = pairing.Required ? pairing.Authenticate(token) : null;
            return new
            {
                required = pairing.Required,
                device = PairingService.ToDto(device),
                token = device != null ? token : null,
            };
        }

        // { "token": "…" }: a paired phone whose browser dropped the cookie shows the copy it
        // kept, and gets the cookie again instead of a PIN. Browsers drop cookies on their own
        // (storage cleanup, a home-screen app with its own cookie jar), and having to read the
        // TV again each time would undo the point of pairing.
        [Route(HttpVerbs.Post, "/pair/resume")]
        public async Task<object> Resume()
        {
            if (!pairing.Required) throw new HttpException(HttpStatusCode.Conflict, Tr("Pairing isn't required: the phone is already in.", "L'abbinamento non è richiesto: il telefono è già dentro."));
            var body = await ReadBody<PairBody>();
            var device = pairing.Authenticate(body?.Token);
            if (device == null) throw new PairingException("forgotten", Tr("The PC doesn't know this phone any more: pair it again.", "Il PC non conosce più questo telefono: abbinalo di nuovo."));
            gate.SetCookie(HttpContext, body.Token);
            live.Nudge();
            return PairingService.ToDto(device);
        }

        // { "name": "Pixel 8 · Chrome" }: a PIN goes up on the PC's screen for two minutes.
        [Route(HttpVerbs.Post, "/pair/request")]
        public async Task<object> RequestPin()
        {
            if (!pairing.Required) throw new HttpException(HttpStatusCode.Conflict, Tr("Pairing isn't required: the phone is already in.", "L'abbinamento non è richiesto: il telefono è già dentro."));
            var body = await ReadBody<PairBody>();
            var request = pairing.RequestPin(body?.Name);
            return new { expiresAt = request.ExpiresAt.ToString("o"), seconds = (int)(request.ExpiresAt - DateTime.UtcNow).TotalSeconds };
        }

        // { "pin": "482913", "name": "…" }: right, and the phone gets its cookie and is a device.
        [Route(HttpVerbs.Post, "/pair")]
        public async Task<object> CompletePairing()
        {
            if (!pairing.Required) throw new HttpException(HttpStatusCode.Conflict, Tr("Pairing isn't required: the phone is already in.", "L'abbinamento non è richiesto: il telefono è già dentro."));
            var body = await ReadBody<PairBody>();
            var device = pairing.Complete(body?.Pin, body?.Name, out var token);
            gate.SetCookie(HttpContext, token);
            live.Nudge();
            return new { device.Id, device.Name, Input = device.AllowInput, Captures = device.AllowCaptures, Unlock = device.AllowUnlock, Token = token };
        }

        // This phone forgets the PC, and the PC forgets it.
        [Route(HttpVerbs.Delete, "/pair")]
        public void Unpair()
        {
            var device = pairing.Authenticate(Request.Cookies[PairingService.CookieName]?.Value);
            if (device != null) pairing.Forget(device.Id);
            gate.ClearCookie(HttpContext);
        }

        [Route(HttpVerbs.Get, "/games")]
        public object Games() => library.All();

        [Route(HttpVerbs.Get, "/games/{id}")]
        public object Game(string id) => library.Get(ParseId(id)) ?? throw HttpException.NotFound(Tr("Game not found.", "Gioco non trovato."));

        // Playnite's totals for the game, and what CouchPilot logged of its sessions.
        [Route(HttpVerbs.Get, "/games/{id}/stats")]
        public object GameStats(string id) => stats.Game(ParseId(id)) ?? throw HttpException.NotFound(Tr("Game not found.", "Gioco non trovato."));

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
            var game = library.Get(ParseId(id)) ?? throw HttpException.NotFound(Tr("Game not found.", "Gioco non trovato."));
            if (!game.Installed) throw new HttpException(HttpStatusCode.Conflict, Tr("The game isn't installed.", "Il gioco non è installato."));
            if (game.Running) throw new HttpException(HttpStatusCode.Conflict, Tr("The game is already running.", "Il gioco è già in esecuzione."));
            games.Start(ParseId(id));
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/games/{id}/install")]
        public void Install(string id)
        {
            var game = library.Get(ParseId(id)) ?? throw HttpException.NotFound(Tr("Game not found.", "Gioco non trovato."));
            if (game.Installed) throw new HttpException(HttpStatusCode.Conflict, Tr("The game is already installed.", "Il gioco è già installato."));
            if (game.Installing || game.Uninstalling) throw new HttpException(HttpStatusCode.Conflict, Tr("Playnite is already working on it.", "Playnite ci sta già lavorando."));
            games.Install(ParseId(id));
        }

        [Route(HttpVerbs.Post, "/games/{id}/uninstall")]
        public void Uninstall(string id)
        {
            var game = library.Get(ParseId(id)) ?? throw HttpException.NotFound(Tr("Game not found.", "Gioco non trovato."));
            if (!game.Installed) throw new HttpException(HttpStatusCode.Conflict, Tr("The game isn't installed.", "Il gioco non è installato."));
            if (game.Running) throw new HttpException(HttpStatusCode.Conflict, Tr("Close the game before uninstalling it.", "Chiudi il gioco prima di disinstallarlo."));
            if (game.Installing || game.Uninstalling) throw new HttpException(HttpStatusCode.Conflict, Tr("Playnite is already working on it.", "Playnite ci sta già lavorando."));
            games.Uninstall(ParseId(id));
        }

        // Stops waiting on an install or uninstall that the PC never finished, see GameControl.
        [Route(HttpVerbs.Post, "/games/{id}/stop-waiting")]
        public void StopWaiting(string id)
        {
            if (library.Get(ParseId(id)) == null) throw HttpException.NotFound(Tr("Game not found.", "Gioco non trovato."));
            if (!games.StopWaiting(ParseId(id))) throw new HttpException(HttpStatusCode.Conflict, Tr("Playnite isn't installing or uninstalling it.", "Playnite non lo sta installando né disinstallando."));
        }

        [Route(HttpVerbs.Post, "/games/{id}/stop")]
        public void Stop(string id)
        {
            if (!games.Stop(ParseId(id))) throw new HttpException(HttpStatusCode.Conflict, Tr("Can't find any process of this game.", "Non trovo processi di questo gioco."));
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/games/{id}/focus")]
        public void Focus(string id)
        {
            if (!games.Focus(ParseId(id))) throw new HttpException(HttpStatusCode.Conflict, Tr("Can't find the game's window.", "Non trovo la finestra del gioco."));
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
            if (!displays.Rename(id, body?.Name)) throw HttpException.NotFound(Tr("Preset not found.", "Preset non trovato."));
            live.Nudge();
        }

        // { "outputId": "…", "outputName": "…" } to switch audio with this preset, or
        // { "outputId": null } to leave the audio alone.
        [Route(HttpVerbs.Put, "/displays/presets/{id}/audio")]
        public async Task SetPresetAudio(string id)
        {
            var body = await ReadBody<PresetAudioBody>();
            if (!displays.SetAudioOutput(id, body?.OutputId, body?.OutputName)) throw HttpException.NotFound(Tr("Preset not found.", "Preset non trovato."));
            live.Nudge();
        }

        [Route(HttpVerbs.Delete, "/displays/presets/{id}")]
        public void DeletePreset(string id)
        {
            if (!displays.Delete(id)) throw HttpException.NotFound(Tr("Preset not found.", "Preset non trovato."));
            live.Nudge();
        }

        [Route(HttpVerbs.Post, "/displays/presets/{id}/apply")]
        public void ApplyPreset(string id)
        {
            if (!displays.Apply(id)) throw HttpException.NotFound(Tr("Preset not found.", "Preset non trovato."));
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
            if (!media.PlayPause()) throw new HttpException(HttpStatusCode.Conflict, Tr("The app doesn't accept the command.", "L'app non accetta il comando."));
        }

        [Route(HttpVerbs.Post, "/media/next")]
        public void MediaNext()
        {
            if (!media.Next()) throw new HttpException(HttpStatusCode.Conflict, Tr("The app doesn't accept the command.", "L'app non accetta il comando."));
        }

        [Route(HttpVerbs.Post, "/media/previous")]
        public void MediaPrevious()
        {
            if (!media.Previous()) throw new HttpException(HttpStatusCode.Conflict, Tr("The app doesn't accept the command.", "L'app non accetta il comando."));
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
        public Task<CaptureDto> Screenshot()
        {
            RequireCaptures();
            return captures.Screenshot();
        }

        [Route(HttpVerbs.Post, "/captures/replay")]
        public Task<CaptureDto> Replay()
        {
            RequireCaptures();
            return captures.Replay();
        }

        [Route(HttpVerbs.Post, "/captures/record/stop")]
        public Task<CaptureDto> StopRecording()
        {
            RequireCaptures();
            return captures.StopRecording();
        }

        [Route(HttpVerbs.Post, "/captures/record/start")]
        public Task StartRecording()
        {
            RequireCaptures();
            return captures.StartRecording();
        }

        // { "from": 12.4, "to": 41.0, "format": "mp4" | "gif", "audio": true }: a piece of the clip,
        // written next to it. Answers once ffmpeg is done, with the new capture.
        [Route(HttpVerbs.Post, "/captures/{id}/trim")]
        public async Task<CaptureDto> Trim(string id)
        {
            RequireCaptures();
            var body = await ReadBody<TrimBody>() ?? throw HttpException.BadRequest(Tr("Say from where to where.", "Dimmi da dove a dove."));
            return await clips.Trim(id, body);
        }

        // ?v=<version>: a strip of frames across the clip, for the trimmer. 404 when the PC can't make one.
        [Route(HttpVerbs.Get, "/captures/{id}/strip")]
        public async Task CaptureStrip(string id)
        {
            RequireCaptures();
            var source = captureLibrary.ThumbnailOf(id) ?? throw HttpException.NotFound();
            if (!source.Video) throw HttpException.NotFound();
            var jpeg = await thumbnails.Strip(id, source.Version, source.Seconds, source.Path) ?? throw HttpException.NotFound();
            Response.ContentType = "image/jpeg";
            Response.Headers.Set("Cache-Control", "public, max-age=31536000, immutable");
            await Response.OutputStream.WriteAsync(jpeg, 0, jpeg.Length);
        }

        // Achievements, from SuccessStory's data. 404 for a game it has nothing on.
        [Route(HttpVerbs.Get, "/games/{id}/achievements")]
        public object GameAchievements(string id) =>
            achievements.Game(ParseId(id)) ?? throw new HttpException(HttpStatusCode.NotFound, Tr("No achievements for this game.", "Nessun obiettivo per questo gioco."));

        // The icon of an achievement SuccessStory keeps on disk (web ones are linked directly). ?locked=1 for the locked one.
        [Route(HttpVerbs.Get, "/games/{id}/achievements/{index}/icon")]
        public async Task AchievementIcon(string id, int index, [QueryField] string locked)
        {
            var path = achievements.IconPath(ParseId(id), index, locked == "1") ?? throw HttpException.NotFound();
            var ext = Path.GetExtension(path);
            Response.ContentType = MimeType.Associations.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
            Response.Headers.Set("Cache-Control", "public, max-age=86400");
            using (var file = File.OpenRead(path))
            {
                await file.CopyToAsync(Response.OutputStream);
            }
        }

        // The latest unlocked achievements, whatever the game, newest first. The live socket says when to fetch again.
        [Route(HttpVerbs.Get, "/achievements/recent")]
        public object RecentAchievements() => achievements.Recent(40);

        // To the PC's Recycle Bin.
        [Route(HttpVerbs.Delete, "/captures/{id}")]
        public void DeleteCapture(string id)
        {
            RequireCaptures();
            if (!captureLibrary.Delete(id)) throw HttpException.NotFound(Tr("Capture not found.", "Cattura non trovata."));
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

        // ?v=<version>: a screenshot sized for the phone's screen, a JPEG of a few hundred kB where the
        // PNG can weigh tens of MB. 404 for clips and GIFs, and for a file the PC can't read: the
        // viewer then shows the file itself.
        [Route(HttpVerbs.Get, "/captures/{id}/preview")]
        public async Task CapturePreview(string id)
        {
            RequireCaptures();
            var source = captureLibrary.ThumbnailOf(id) ?? throw HttpException.NotFound();
            if (!Thumbnails.HasPreview(source)) throw HttpException.NotFound();
            var jpeg = await thumbnails.Preview(id, source.Version, source.Path) ?? throw HttpException.NotFound();
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
            var path = captureLibrary.PathOf(id) ?? throw HttpException.NotFound(Tr("Capture not found.", "Cattura non trovata."));
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

        // Screenshots may show more than the game: they stay private unless turned on, and
        // unless this phone is allowed to see them.
        private void RequireCaptures()
        {
            if (!captures.Enabled) throw new HttpException(HttpStatusCode.Forbidden, Tr("Captures are off in CouchPilot's settings.", "Le catture sono spente nelle impostazioni di CouchPilot."));
            if (Device != null && !Device.AllowCaptures) throw new HttpException(HttpStatusCode.Forbidden, Tr("This phone may not see the captures: change it in CouchPilot's settings.", "Questo telefono non può vedere le catture: cambialo nelle impostazioni di CouchPilot."));
        }

        public class NameBody
        {
            public string Name { get; set; }
        }

        public class PairBody
        {
            public string Pin { get; set; }
            public string Name { get; set; }
            public string Token { get; set; }
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
            Guid.TryParse(id, out var guid) ? guid : throw HttpException.BadRequest(Tr("Invalid id.", "Id non valido."));
    }
}
