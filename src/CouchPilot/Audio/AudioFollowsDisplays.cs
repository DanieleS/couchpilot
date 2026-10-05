using System;
using System.Threading.Tasks;
using CouchPilot.Displays;
using Playnite.SDK;

namespace CouchPilot.Audio
{
    // A display preset can name an audio output: applying it switches to that output, and if
    // the preset is rolled back (not kept within 15 s, or reverted) the output goes back too.
    // Presets without one leave the audio alone.
    public class AudioFollowsDisplays
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        // A screen's HDMI or DisplayPort audio appears only once Windows has brought the screen
        // up, a moment after the layout changes: keep trying for a while.
        private static readonly TimeSpan WaitForDevice = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan RetryEvery = TimeSpan.FromMilliseconds(500);

        private readonly AudioService audio;
        private readonly object gate = new object();
        // The output before the first preset of a trial switched it; like the displays' own
        // rollback point, chaining presets keeps the first one.
        private string restoreTo;
        // Bumped by every event: a switch still waiting for its device gives up when a newer
        // event (another preset, a rollback) has come in since.
        private int turn;

        public AudioFollowsDisplays(DisplayService displays, AudioService audio)
        {
            this.audio = audio;
            displays.Applied += OnApplied;
            displays.Confirmed += () =>
            {
                lock (gate) restoreTo = null;
            };
            displays.RolledBack += OnRolledBack;
        }

        // Called with the display service's lock held: only notes what to do, the switching
        // happens off this thread.
        private void OnApplied(DisplayPreset preset, bool onTrial)
        {
            int mine;
            lock (gate)
            {
                mine = ++turn;
                if (string.IsNullOrEmpty(preset.AudioOutputId)) return;
                if (!onTrial) restoreTo = null;
                else if (restoreTo == null) restoreTo = audio.DefaultOutputId();
            }
            SwitchWhenReady(preset.AudioOutputId, preset.Name, mine);
        }

        private void OnRolledBack()
        {
            string target;
            int mine;
            lock (gate)
            {
                mine = ++turn;
                target = restoreTo;
                restoreTo = null;
            }
            if (target != null) SwitchWhenReady(target, "rollback", mine);
        }

        private void SwitchWhenReady(string outputId, string why, int mine)
        {
            Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow + WaitForDevice;
                while (true)
                {
                    lock (gate)
                    {
                        if (turn != mine) return;
                        try
                        {
                            if (audio.DefaultOutputId() == outputId || audio.SetDefaultOutput(outputId)) return;
                        }
                        catch (Exception e)
                        {
                            logger.Warn(e, $"Switching the audio output ({why}) failed");
                            return;
                        }
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        logger.Info($"Audio output for {why} never showed up; leaving the audio as it is");
                        return;
                    }
                    await Task.Delay(RetryEvery);
                }
            });
        }
    }
}
