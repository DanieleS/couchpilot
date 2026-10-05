using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using CouchPilot.Displays;
using CouchPilot.Games;
using CouchPilot.Library;
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

        public ApiController(LibraryService library, GameControl games, DisplayService displays)
        {
            this.library = library;
            this.games = games;
            this.displays = displays;
        }

        // One call the phone polls: what's running and what the screens look like.
        [Route(HttpVerbs.Get, "/status")]
        public object Status() => new
        {
            pc = Environment.MachineName,
            running = library.Running(),
            displays = displays.Snapshot(),
        };

        [Route(HttpVerbs.Get, "/games")]
        public object Games() => library.All();

        [Route(HttpVerbs.Get, "/games/{id}")]
        public object Game(string id) => library.Get(ParseId(id)) ?? throw HttpException.NotFound("Gioco non trovato.");

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
        }

        [Route(HttpVerbs.Post, "/games/{id}/stop")]
        public void Stop(string id)
        {
            if (!games.Stop(ParseId(id))) throw new HttpException(HttpStatusCode.Conflict, "Non trovo processi di questo gioco.");
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
            return displays.SaveCurrent(body?.Name);
        }

        [Route(HttpVerbs.Patch, "/displays/presets/{id}")]
        public async Task RenamePreset(string id)
        {
            var body = await ReadBody<NameBody>();
            if (!displays.Rename(id, body?.Name)) throw HttpException.NotFound("Preset non trovato.");
        }

        [Route(HttpVerbs.Delete, "/displays/presets/{id}")]
        public void DeletePreset(string id)
        {
            if (!displays.Delete(id)) throw HttpException.NotFound("Preset non trovato.");
        }

        [Route(HttpVerbs.Post, "/displays/presets/{id}/apply")]
        public void ApplyPreset(string id)
        {
            if (!displays.Apply(id)) throw HttpException.NotFound("Preset non trovato.");
        }

        [Route(HttpVerbs.Post, "/displays/confirm")]
        public void Confirm() => displays.Confirm();

        [Route(HttpVerbs.Post, "/displays/revert")]
        public void Revert() => displays.Revert();

        public class NameBody
        {
            public string Name { get; set; }
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
