using System;
using System.Net;
using System.Threading.Tasks;
using EmbedIO;
using static CouchPilot.Lang;

namespace CouchPilot.Server
{
    // Stands in front of /api while pairing is on: a request without a known device's cookie is
    // answered 401 with code "unpaired", and the phone shows the pairing screen. The pairing
    // routes themselves are open, or no phone could ever get in. The app's own files stay
    // open too: the pairing screen is part of them.
    //
    // The cookie is set here on a known device too, now and then: browsers cap a cookie's life
    // at about a year, and a phone that keeps coming back shouldn't have to pair again.
    public class PairingGate : WebModuleBase
    {
        public const string DeviceItem = "couchpilot.device";
        private static readonly TimeSpan RefreshEvery = TimeSpan.FromDays(1);
        private static readonly TimeSpan CookieLife = TimeSpan.FromDays(400);

        private readonly PairingService pairing;
        private readonly bool secure;
        private readonly Func<bool> locked;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> refreshed = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();

        public PairingGate(PairingService pairing, bool secure, Func<bool> locked) : base("/")
        {
            this.pairing = pairing;
            this.secure = secure;
            this.locked = locked;
        }

        public override bool IsFinalHandler => false;

        protected override async Task OnRequestAsync(IHttpContext context)
        {
            var path = context.Request.Url.AbsolutePath;
            if (!path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/api/pair", StringComparison.Ordinal)) return;
            if (context.Request.HttpVerb == HttpVerbs.Options) return;
            UseRequest(context.Request.Headers["Accept-Language"]);

            var device = pairing.Required ? pairing.Authenticate(context.Request.Cookies[PairingService.CookieName]?.Value) : null;
            if (pairing.Required && device == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await RemoteServer.SerializeJson(context, new { error = Tr("This phone isn't paired with the PC.", "Questo telefono non è abbinato al PC."), code = "unpaired" });
                context.SetHandled();
                return;
            }
            if (device != null)
            {
                context.Items[DeviceItem] = device;
                var now = DateTime.UtcNow;
                if (!refreshed.TryGetValue(device.Id, out var last) || now - last > RefreshEvery)
                {
                    refreshed[device.Id] = now;
                    SetCookie(context, context.Request.Cookies[PairingService.CookieName].Value);
                }
            }

            // While the PC is on the lock screen the phone may look but not act: anything but a
            // read is refused, save unlocking itself (pairing never reaches here).
            var verb = context.Request.HttpVerb;
            if (verb != HttpVerbs.Get && verb != HttpVerbs.Head && path != "/api/unlock" && locked())
            {
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await RemoteServer.SerializeJson(context, new { error = Tr("The PC is locked: unlock it first.", "Il PC è bloccato: prima sbloccalo."), code = "locked" });
                context.SetHandled();
            }
        }

        // The device's token, for a year and a bit; HttpOnly so no script on the page can read
        // it, SameSite so no other site's page can send it.
        public void SetCookie(IHttpContext context, string token)
        {
            context.Response.Headers.Add("Set-Cookie", CookieHeader(token, CookieLife));
        }

        public void ClearCookie(IHttpContext context)
        {
            context.Response.Headers.Add("Set-Cookie", CookieHeader("", TimeSpan.Zero));
        }

        private string CookieHeader(string token, TimeSpan life) =>
            $"{PairingService.CookieName}={token}; Path=/; Max-Age={(long)life.TotalSeconds}; HttpOnly; SameSite=Lax{(secure ? "; Secure" : "")}";

        // The device a request came from, once the gate let it through; null while pairing is off.
        public static PairedDevice DeviceOf(IHttpContext context) =>
            context.Items.TryGetValue(DeviceItem, out var device) ? device as PairedDevice : null;
    }
}
