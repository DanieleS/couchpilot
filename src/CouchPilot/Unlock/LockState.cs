using System;
using Microsoft.Win32;

namespace CouchPilot.Unlock
{
    // Whether the PC is on the lock screen right now. CouchPilot keeps running while locked, so it
    // can watch Windows' own session-switch notifications and tell the phone when there's an
    // unlock to offer. Lock-only: a signed-out or just-booted PC isn't running CouchPilot at all.
    public class LockState : IDisposable
    {
        private volatile bool locked;

        public bool Locked => locked;

        // Raised on lock and on unlock, so the server can nudge the phones.
        public event Action Changed;

        public LockState()
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            bool now;
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    now = true;
                    break;
                case SessionSwitchReason.SessionUnlock:
                    now = false;
                    break;
                default:
                    return;
            }
            if (now == locked) return;
            locked = now;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
    }
}
