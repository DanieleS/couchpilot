using System;
using System.Collections.Generic;
using System.Linq;
using CouchPilot.Games;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace CouchPilot.Library
{
    public class GameDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public bool Installed { get; set; }
        // Playnite is installing or uninstalling it right now.
        public bool Installing { get; set; }
        public bool Uninstalling { get; set; }
        public bool Favorite { get; set; }
        public bool Hidden { get; set; }
        public bool Running { get; set; }
        public int PlaytimeMinutes { get; set; }
        public string LastActivity { get; set; }
        public string Added { get; set; }
        public List<string> Genres { get; set; }
        public int? ReleaseYear { get; set; }
        public bool HasCover { get; set; }
        public bool HasBackground { get; set; }
    }

    public class GameDetailDto : GameDto
    {
        public string Description { get; set; }
        public List<string> Developers { get; set; }
    }

    public class RunningDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string StartedAt { get; set; }
        public bool HasCover { get; set; }
        public bool HasBackground { get; set; }
    }

    public enum ImageKind { Cover, Background, Icon }

    public class LibraryService
    {
        private readonly IPlayniteAPI api;
        private readonly GameSessions sessions;

        public LibraryService(IPlayniteAPI api, GameSessions sessions)
        {
            this.api = api;
            this.sessions = sessions;
        }

        public List<GameDto> All()
        {
            return api.Database.Games
                .Where(g => !g.Hidden)
                .OrderBy(g => g.SortingName ?? g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => Fill(new GameDto(), g))
                .ToList();
        }

        public GameDetailDto Get(Guid id)
        {
            var game = api.Database.Games.Get(id);
            if (game == null) return null;
            var dto = Fill(new GameDetailDto(), game);
            dto.Description = game.Description;
            dto.Developers = game.Developers?.Select(d => d.Name).ToList() ?? new List<string>();
            return dto;
        }

        public List<RunningDto> Running()
        {
            var result = new List<RunningDto>();
            foreach (var session in sessions.All)
            {
                var game = api.Database.Games.Get(session.GameId);
                if (game == null) continue;
                result.Add(new RunningDto
                {
                    Id = game.Id.ToString(),
                    Name = game.Name,
                    Source = SourceName(game),
                    StartedAt = session.StartedAt?.ToString("o"),
                    HasCover = !string.IsNullOrEmpty(game.CoverImage),
                    HasBackground = !string.IsNullOrEmpty(game.BackgroundImage),
                });
            }
            return result;
        }

        // A game by its Playnite id, or by its exact name (case-insensitive).
        public Guid? Resolve(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;
            if (Guid.TryParse(idOrName.Trim(), out var id) && api.Database.Games.Get(id) != null) return id;
            var match = api.Database.Games.FirstOrDefault(g =>
                string.Equals(g.Name, idOrName.Trim(), StringComparison.CurrentCultureIgnoreCase));
            return match?.Id;
        }

        // A local file path, or an http(s) URL when Playnite hasn't downloaded the image.
        public string ImageLocation(Guid id, ImageKind kind)
        {
            var game = api.Database.Games.Get(id);
            if (game == null) return null;
            var stored = kind == ImageKind.Cover ? game.CoverImage
                : kind == ImageKind.Background ? game.BackgroundImage
                : game.Icon;
            if (string.IsNullOrEmpty(stored)) return null;
            if (stored.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return stored;
            return api.Database.GetFullFilePath(stored);
        }

        private T Fill<T>(T dto, Game g) where T : GameDto
        {
            dto.Id = g.Id.ToString();
            dto.Name = g.Name;
            dto.Source = SourceName(g);
            dto.Installed = g.IsInstalled;
            dto.Installing = g.IsInstalling;
            dto.Uninstalling = g.IsUninstalling;
            dto.Favorite = g.Favorite;
            dto.Hidden = g.Hidden;
            dto.Running = sessions.IsRunning(g.Id);
            dto.PlaytimeMinutes = (int)(g.Playtime / 60);
            dto.LastActivity = g.LastActivity?.ToString("o");
            dto.Added = g.Added?.ToString("o");
            dto.Genres = g.Genres?.Select(x => x.Name).ToList() ?? new List<string>();
            dto.ReleaseYear = g.ReleaseYear;
            dto.HasCover = !string.IsNullOrEmpty(g.CoverImage);
            dto.HasBackground = !string.IsNullOrEmpty(g.BackgroundImage);
            return dto;
        }

        private static string SourceName(Game g) => g.Source?.Name ?? "Playnite";
    }
}
