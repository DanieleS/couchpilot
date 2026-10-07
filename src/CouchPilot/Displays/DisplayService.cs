using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CouchPilot.Native;
using Playnite.SDK;
using Playnite.SDK.Data;
using static CouchPilot.Native.Ccd;
using static CouchPilot.Lang;

namespace CouchPilot.Displays
{
    public class MonitorInfo
    {
        public string Name { get; set; }
        public string DevicePath { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshHz { get; set; }
        public bool Primary { get; set; }
        // Vibepollo's virtual display: it exists only while a stream is running.
        public bool Virtual { get; set; }
    }

    // One saved path's monitor, so the preset can be re-pointed at the right adapter after a
    // reboot: adapter LUIDs are not stable, monitor device paths are.
    public class PresetTarget
    {
        public string DevicePath { get; set; }
        public long AdapterId { get; set; }
    }

    public class DisplayPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<MonitorInfo> Monitors { get; set; }
        public List<PresetTarget> Targets { get; set; }
        public string Paths { get; set; }
        public string Modes { get; set; }
        // Optional: the audio output to switch to with this preset. The name is kept for showing
        // it while the device is unplugged.
        public string AudioOutputId { get; set; }
        public string AudioOutputName { get; set; }
    }

    public class PendingChange
    {
        public string PresetId { get; set; }
        // The preset that was showing before, if the layout matched one: what we roll back to.
        public string PreviousPresetId { get; set; }
        public int SecondsLeft { get; set; }
    }

    public class DisplaysDto
    {
        public List<MonitorInfo> Monitors { get; set; }
        public List<PresetDto> Presets { get; set; }
        public string ActivePresetId { get; set; }
        public PendingChange Pending { get; set; }
        // A Vibepollo stream owns the displays right now: presets are off limits until it ends.
        public bool StreamActive { get; set; }
    }

    public class PresetDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<MonitorInfo> Monitors { get; set; }
        public string AudioOutputId { get; set; }
        public string AudioOutputName { get; set; }
    }

    public class DisplayException : Exception
    {
        public DisplayException(string message) : base(message) { }
    }

    public class DisplayService : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(15);

        private readonly string storePath;
        private readonly object sync = new object();
        private List<DisplayPreset> presets;

        // Set after applying a preset until the user keeps it; if they don't, we roll back.
        private string pendingPresetId;
        private string previousPresetId;
        private DateTime pendingDeadline;
        private (DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) rollback;
        private Timer rollbackTimer;

        // For whoever acts along with presets (the audio output that follows them). Raised with
        // the lock held: handlers must be quick and must not call back into this service.
        // Applied's flag is true when the change is on trial and may still be rolled back.
        public event Action<DisplayPreset, bool> Applied;
        public event Action Confirmed;
        public event Action RolledBack;

        public DisplayService(string storePath)
        {
            this.storePath = storePath;
            presets = Load();
        }

        public DisplaysDto Snapshot()
        {
            lock (sync)
            {
                var monitors = CurrentMonitors();
                return new DisplaysDto
                {
                    StreamActive = monitors.Any(m => m.Virtual),
                    Monitors = monitors,
                    Presets = presets.Select(p => new PresetDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Monitors = p.Monitors,
                        AudioOutputId = p.AudioOutputId,
                        AudioOutputName = p.AudioOutputName,
                    }).ToList(),
                    ActivePresetId = presets.FirstOrDefault(p => SameLayout(p.Monitors, monitors))?.Id,
                    Pending = pendingPresetId == null ? null : new PendingChange
                    {
                        PresetId = pendingPresetId,
                        PreviousPresetId = previousPresetId,
                        SecondsLeft = Math.Max(0, (int)Math.Ceiling((pendingDeadline - DateTime.UtcNow).TotalSeconds)),
                    },
                };
            }
        }

        public PresetDto SaveCurrent(string name)
        {
            lock (sync)
            {
                var (paths, modes) = Query(QDC_ONLY_ACTIVE_PATHS);
                if (Describe(paths, modes).Any(m => m.Virtual))
                {
                    throw new DisplayException(Tr("A Vibepollo stream is on: save the preset once it's over, without the virtual display.", "C'è uno stream Vibepollo in corso: salva il preset quando è finito, senza lo schermo virtuale."));
                }
                var preset = new DisplayPreset
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = string.IsNullOrWhiteSpace(name) ? $"Preset {presets.Count + 1}" : name.Trim(),
                    Monitors = Describe(paths, modes),
                    Targets = paths.Select(p => new PresetTarget
                    {
                        DevicePath = SafeTargetName(p.targetInfo.adapterId, p.targetInfo.id).monitorDevicePath,
                        AdapterId = p.targetInfo.adapterId.Value,
                    }).ToList(),
                    Paths = Convert.ToBase64String(ToBytes(paths)),
                    Modes = Convert.ToBase64String(ToBytes(modes)),
                };
                presets.Add(preset);
                Save();
                return new PresetDto { Id = preset.Id, Name = preset.Name, Monitors = preset.Monitors };
            }
        }

        public bool Rename(string id, string name)
        {
            lock (sync)
            {
                var preset = presets.FirstOrDefault(p => p.Id == id);
                if (preset == null || string.IsNullOrWhiteSpace(name)) return false;
                preset.Name = name.Trim();
                Save();
                return true;
            }
        }

        // null outputId: the preset leaves the audio alone.
        public bool SetAudioOutput(string id, string outputId, string outputName)
        {
            lock (sync)
            {
                var preset = presets.FirstOrDefault(p => p.Id == id);
                if (preset == null) return false;
                preset.AudioOutputId = string.IsNullOrEmpty(outputId) ? null : outputId;
                preset.AudioOutputName = preset.AudioOutputId == null ? null : outputName?.Trim();
                Save();
                return true;
            }
        }

        public bool Delete(string id)
        {
            lock (sync)
            {
                var removed = presets.RemoveAll(p => p.Id == id) > 0;
                if (removed) Save();
                return removed;
            }
        }

        // keep: apply for good, without the 15 s rollback. For callers with nobody in front of
        // the screen to confirm, like a Home Assistant automation.
        public bool Apply(string id, bool keep = false)
        {
            lock (sync)
            {
                var preset = presets.FirstOrDefault(p => p.Id == id);
                if (preset == null) return false;
                if (StreamActive())
                {
                    throw new DisplayException("C'è uno stream Vibepollo in corso: gli schermi li sta gestendo lui.");
                }

                var paths = FromBytes<DISPLAYCONFIG_PATH_INFO>(Convert.FromBase64String(preset.Paths));
                var modes = FromBytes<DISPLAYCONFIG_MODE_INFO>(Convert.FromBase64String(preset.Modes));
                RemapAdapters(preset, paths, modes);

                if (keep)
                {
                    ClearPending();
                    Ccd.Apply(paths, modes);
                    Raise(() => Applied?.Invoke(preset, false));
                    return true;
                }

                // Only the first apply in a row keeps a rollback point: chaining presets
                // without confirming still rolls back to what worked before.
                if (pendingPresetId == null)
                {
                    rollback = Query(QDC_ONLY_ACTIVE_PATHS);
                    var before = Describe(rollback.paths, rollback.modes);
                    previousPresetId = presets.FirstOrDefault(p => SameLayout(p.Monitors, before))?.Id;
                }

                Ccd.Apply(paths, modes);
                Raise(() => Applied?.Invoke(preset, true));

                pendingPresetId = preset.Id;
                pendingDeadline = DateTime.UtcNow + ConfirmWindow;
                rollbackTimer?.Dispose();
                rollbackTimer = new Timer(_ => RevertUnlessStreaming(), null, ConfirmWindow, Timeout.InfiniteTimeSpan);
                return true;
            }
        }

        public string FindPresetId(string name)
        {
            lock (sync)
            {
                return presets.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase))?.Id;
            }
        }

        public void Confirm()
        {
            lock (sync)
            {
                if (pendingPresetId != null) Raise(() => Confirmed?.Invoke());
                ClearPending();
            }
        }

        public void Revert()
        {
            lock (sync)
            {
                if (pendingPresetId == null) return;
                try
                {
                    Ccd.Apply(rollback.paths, rollback.modes);
                }
                catch (Exception e)
                {
                    logger.Error(e, "Rolling back the display configuration failed");
                }
                Raise(() => RolledBack?.Invoke());
                ClearPending();
            }
        }

        // The automatic rollback must not fight Vibepollo: if a stream started while we were
        // waiting, the displays are its business now, and it restores them when it ends.
        private void RevertUnlessStreaming()
        {
            lock (sync)
            {
                if (StreamActive())
                {
                    // Vibepollo took over: what we applied stays, as if kept.
                    Raise(() => Confirmed?.Invoke());
                    ClearPending();
                    return;
                }
                Revert();
            }
        }

        // A failing listener must not undo a display change that already happened.
        private static void Raise(Action raise)
        {
            try { raise(); } catch (Exception e) { logger.Error(e, "A display preset listener failed"); }
        }

        private static bool StreamActive() => CurrentMonitors().Any(m => m.Virtual);

        public void Dispose()
        {
            lock (sync)
            {
                rollbackTimer?.Dispose();
                rollbackTimer = null;
            }
        }

        private void ClearPending()
        {
            pendingPresetId = null;
            previousPresetId = null;
            rollback = default;
            rollbackTimer?.Dispose();
            rollbackTimer = null;
        }

        private static void RemapAdapters(DisplayPreset preset, DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
        {
            var (allPaths, _) = Query(QDC_ALL_PATHS);
            var connected = new Dictionary<string, DISPLAYCONFIG_PATH_TARGET_INFO>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in allPaths)
            {
                var name = SafeTargetName(p.targetInfo.adapterId, p.targetInfo.id).monitorDevicePath;
                if (!string.IsNullOrEmpty(name) && !connected.ContainsKey(name))
                {
                    connected[name] = p.targetInfo;
                }
            }

            var luidMap = new Dictionary<long, LUID>();
            for (var i = 0; i < paths.Length; i++)
            {
                var target = preset.Targets[i];
                if (!connected.TryGetValue(target.DevicePath ?? "", out var now))
                {
                    var monitor = preset.Monitors.FirstOrDefault(m => m.DevicePath == target.DevicePath);
                    throw new DisplayException(Tr($"The monitor {monitor?.Name ?? target.DevicePath} isn't connected.", $"Il monitor {monitor?.Name ?? target.DevicePath} non è collegato."));
                }
                luidMap[target.AdapterId] = now.adapterId;
                paths[i].targetInfo.id = now.id;
            }

            LUID Map(LUID old) => luidMap.TryGetValue(old.Value, out var n) ? n : old;
            for (var i = 0; i < paths.Length; i++)
            {
                paths[i].sourceInfo.adapterId = Map(paths[i].sourceInfo.adapterId);
                paths[i].targetInfo.adapterId = Map(paths[i].targetInfo.adapterId);
            }
            for (var i = 0; i < modes.Length; i++)
            {
                modes[i].adapterId = Map(modes[i].adapterId);
            }
        }

        private static List<MonitorInfo> CurrentMonitors()
        {
            var (paths, modes) = Query(QDC_ONLY_ACTIVE_PATHS);
            return Describe(paths, modes);
        }

        private static List<MonitorInfo> Describe(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
        {
            var result = new List<MonitorInfo>();
            foreach (var p in paths)
            {
                if ((p.flags & DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;
                var idx = p.sourceInfo.modeInfoIdx;
                if (idx == INVALID_MODE_IDX || idx >= modes.Length) continue;
                var mode = modes[idx];
                if (mode.infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) continue;

                var name = SafeTargetName(p.targetInfo.adapterId, p.targetInfo.id);
                var rate = p.targetInfo.refreshRate;
                result.Add(new MonitorInfo
                {
                    Name = string.IsNullOrEmpty(name.monitorFriendlyDeviceName) ? "Monitor" : name.monitorFriendlyDeviceName,
                    DevicePath = name.monitorDevicePath,
                    X = mode.sourceMode.position.x,
                    Y = mode.sourceMode.position.y,
                    Width = (int)mode.sourceMode.width,
                    Height = (int)mode.sourceMode.height,
                    RefreshHz = rate.Denominator == 0 ? 0 : (int)Math.Round((double)rate.Numerator / rate.Denominator),
                    Primary = mode.sourceMode.position.x == 0 && mode.sourceMode.position.y == 0,
                    Virtual = VirtualDisplay.IsVibepollo(name.monitorDevicePath, name.monitorFriendlyDeviceName),
                });
            }
            return result;
        }

        private static DISPLAYCONFIG_TARGET_DEVICE_NAME SafeTargetName(LUID adapter, uint target)
        {
            try { return TargetName(adapter, target); }
            catch { return new DISPLAYCONFIG_TARGET_DEVICE_NAME(); }
        }

        private static bool SameLayout(List<MonitorInfo> a, List<MonitorInfo> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            string Key(MonitorInfo m) => $"{m.DevicePath}|{m.X}|{m.Y}|{m.Width}|{m.Height}";
            return a.Select(Key).OrderBy(k => k).SequenceEqual(b.Select(Key).OrderBy(k => k));
        }

        private List<DisplayPreset> Load()
        {
            try
            {
                if (File.Exists(storePath))
                {
                    return Serialization.FromJsonFile<List<DisplayPreset>>(storePath) ?? new List<DisplayPreset>();
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Could not read display presets");
            }
            return new List<DisplayPreset>();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath));
            File.WriteAllText(storePath, Serialization.ToJson(presets, true));
        }
    }
}
