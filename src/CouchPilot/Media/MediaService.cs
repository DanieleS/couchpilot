using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace CouchPilot.Media
{
    public class NowPlayingDto
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        // The app playing it, as Windows names it: "Spotify.exe", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"…
        public string App { get; set; }
        public bool Playing { get; set; }
        public bool CanPlayPause { get; set; }
        public bool CanNext { get; set; }
        public bool CanPrevious { get; set; }
        // Where the track was when this was read, so the phone can move the bar on its own.
        // Null when the app doesn't say.
        public double? PositionSeconds { get; set; }
        public double? DurationSeconds { get; set; }
        public string ReadAt { get; set; }
        // Changes with the artwork: the phone fetches /api/media/art?v=… only when it does.
        public string ArtVersion { get; set; }
    }

    // What Windows' media flyout shows: the current media session of whatever app plays (Spotify,
    // a browser tab, the Media Player…), with play/pause and skip. Windows 10 1809 and later.
    public class MediaService : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly object gate = new object();
        private GlobalSystemMediaTransportControlsSessionManager manager;
        private GlobalSystemMediaTransportControlsSession session;
        private byte[] art;
        private string artType;
        private string artVersion;
        private int artCounter;
        private bool disposed;

        // Something the phone shows changed: the track, play/pause, the app. Not raised for the
        // position moving on, which the phone works out by itself.
        public event Action Changed;

        public void Start()
        {
            Task.Run(async () =>
            {
                try
                {
                    var m = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    lock (gate)
                    {
                        if (disposed) return;
                        manager = m;
                        manager.CurrentSessionChanged += (_, __) => Follow();
                    }
                    Follow();
                }
                catch (Exception e)
                {
                    // Older Windows, or no media service: the feature just stays empty.
                    logger.Info($"Media controls unavailable: {e.Message}");
                }
            });
        }

        public NowPlayingDto Snapshot()
        {
            GlobalSystemMediaTransportControlsSession s;
            string version;
            lock (gate)
            {
                s = session;
                version = artVersion;
            }
            if (s == null) return null;
            try
            {
                var props = Wait(s.TryGetMediaPropertiesAsync().AsTask());
                var playback = s.GetPlaybackInfo();
                var timeline = s.GetTimelineProperties();
                if (props == null || (string.IsNullOrWhiteSpace(props.Title) && string.IsNullOrWhiteSpace(props.Artist))) return null;
                var duration = timeline.EndTime - timeline.StartTime;
                var known = duration > TimeSpan.Zero;
                // Windows' position is as of its last update; carry it to now while playing.
                var position = timeline.Position - timeline.StartTime;
                if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && timeline.LastUpdatedTime.Year > 2000)
                {
                    position += DateTimeOffset.Now - timeline.LastUpdatedTime;
                }
                return new NowPlayingDto
                {
                    Title = props.Title,
                    Artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist,
                    Album = props.AlbumTitle,
                    App = s.SourceAppUserModelId,
                    Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanPlayPause = playback.Controls.IsPlayPauseToggleEnabled || playback.Controls.IsPlayEnabled || playback.Controls.IsPauseEnabled,
                    CanNext = playback.Controls.IsNextEnabled,
                    CanPrevious = playback.Controls.IsPreviousEnabled,
                    PositionSeconds = known ? Math.Max(0, Math.Min(duration.TotalSeconds, position.TotalSeconds)) : (double?)null,
                    DurationSeconds = known ? duration.TotalSeconds : (double?)null,
                    ReadAt = DateTime.UtcNow.ToString("o"),
                    ArtVersion = version,
                };
            }
            catch (Exception e)
            {
                logger.Warn(e, "Reading the media session failed");
                return null;
            }
        }

        public (byte[] bytes, string type) Art()
        {
            lock (gate) return (art, artType);
        }

        // False when nothing plays, or the app doesn't allow it.
        public bool PlayPause() => Do(s => s.TryTogglePlayPauseAsync().AsTask());
        public bool Next() => Do(s => s.TrySkipNextAsync().AsTask());
        public bool Previous() => Do(s => s.TrySkipPreviousAsync().AsTask());

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                Unhook();
                session = null;
                manager = null;
            }
        }

        private bool Do(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
        {
            GlobalSystemMediaTransportControlsSession s;
            lock (gate) s = session;
            if (s == null) return false;
            try { return Wait(action(s)); } catch { return false; }
        }

        // Moves our event handlers to whatever session Windows now calls current.
        private void Follow()
        {
            lock (gate)
            {
                if (disposed || manager == null) return;
                Unhook();
                session = manager.GetCurrentSession();
                if (session != null)
                {
                    session.MediaPropertiesChanged += OnPropertiesChanged;
                    session.PlaybackInfoChanged += OnPlaybackChanged;
                }
            }
            LoadArt();
            Raise();
        }

        private void Unhook()
        {
            if (session == null) return;
            session.MediaPropertiesChanged -= OnPropertiesChanged;
            session.PlaybackInfoChanged -= OnPlaybackChanged;
        }

        private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
        {
            LoadArt();
            Raise();
        }

        private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => Raise();

        private void LoadArt()
        {
            GlobalSystemMediaTransportControlsSession s;
            lock (gate) s = session;
            byte[] bytes = null;
            string type = null;
            try
            {
                var thumbnail = s == null ? null : Wait(s.TryGetMediaPropertiesAsync().AsTask())?.Thumbnail;
                if (thumbnail != null)
                {
                    using (var stream = Wait(thumbnail.OpenReadAsync().AsTask()))
                    using (var read = stream.AsStreamForRead())
                    using (var copy = new MemoryStream())
                    {
                        read.CopyTo(copy);
                        bytes = copy.ToArray();
                        type = string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType;
                    }
                }
            }
            catch (Exception e)
            {
                logger.Debug($"No artwork: {e.Message}");
            }
            lock (gate)
            {
                art = bytes;
                artType = type;
                artVersion = bytes == null ? null : (++artCounter).ToString();
            }
        }

        private void Raise()
        {
            try { Changed?.Invoke(); } catch (Exception e) { logger.Error(e, "A media listener failed"); }
        }

        // WinRT calls answer in milliseconds; a hung media app must not hang a request.
        private static T Wait<T>(Task<T> task) =>
            task.Wait(TimeSpan.FromSeconds(2)) ? task.Result : throw new TimeoutException("The media app didn't answer");
    }
}
