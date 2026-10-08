using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace CouchPilot.Unlock
{
    public enum UnlockResult
    {
        Ok,          // the provider accepted the handoff; the PC is unlocking
        NotLocked,   // nothing to do, or the provider's pipe isn't up
        Unavailable, // not set up, or the provider didn't answer
        Forbidden,   // (decided at the API layer) this phone may not unlock
        Failed,      // the provider refused (bad secret, busy…)
    }

    // The Playnite-side half of unlocking: it keeps the Windows password (DPAPI-sealed), turns the
    // feature on and off through the elevated setup helper, and, when a paired phone asks and the
    // PC is locked, hands the credentials to the credential provider over the pipe. docs/unlock.md
    // is the whole picture; this file owns the password store and the pipe client.
    public class UnlockService
    {
        private const string PipeName = "CouchPilotUnlock";
        private const int ProtocolVersion = 1;
        // The protocol, the CLSID and the LSA/registry names are a contract with the native side.
        // Their source of truth is src/CouchPilotUnlock/Common.h and Guid.h (and docs/unlock.md);
        // these copies must stay in step with them.
        private const string CredentialProviderKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B6F4C3A2-7E51-4A90-9C3D-1F8E2A6B5D04}";

        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CouchPilot.Unlock.v1");

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(string user, string domain, string password, int logonType, int logonProvider, out IntPtr token);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        private const int LOGON32_LOGON_INTERACTIVE = 2;
        private const int LOGON32_LOGON_NETWORK = 3;
        private const int LOGON32_PROVIDER_DEFAULT = 0;
        private const int ERROR_LOGON_FAILURE = 1326;
        private const int ERROR_ACCOUNT_RESTRICTION = 1327;

        private readonly string storePath;      // unlock.bin: the secret and password, DPAPI-sealed
        private readonly string accountPath;    // unlock-account.txt: the account name, in the clear (it's not a secret)
        private readonly string helperPath;     // CouchPilotUnlockSetup.exe, beside the extension
        private readonly LockState lockState;
        private readonly Func<bool> pairingRequired;
        private readonly object gate = new object();

        public UnlockService(string dataDir, string helperPath, LockState lockState, Func<bool> pairingRequired)
        {
            storePath = Path.Combine(dataDir, "unlock.bin");
            accountPath = Path.Combine(dataDir, "unlock-account.txt");
            this.helperPath = helperPath;
            this.lockState = lockState;
            this.pairingRequired = pairingRequired;
        }

        // Raised when the feature is turned on or off, so the settings and the phones refresh.
        public event Action Changed;

        public bool Locked => lockState.Locked;

        // The native setup helper shipped: without it (a plain checkout that skipped the C++
        // build) unlocking can't be offered at all.
        public bool Available => File.Exists(helperPath);

        // Set up and ready: there's a sealed password on file and the provider is registered. If
        // one is missing (an interrupted setup, a manual unregister) the feature is off.
        public bool Enabled => Available && File.Exists(storePath) && ProviderRegistered();

        // The enrolled account, for the settings to show. Read from the plaintext sidecar so
        // showing the account never decrypts the password.
        public string Account
        {
            get
            {
                try { return File.Exists(accountPath) ? File.ReadAllText(accountPath).Trim() : null; }
                catch (Exception e) { logger.Warn(e, "Couldn't read the unlock account"); return null; }
            }
        }

        // The phone may see and press Unlock only when it's set up and the PC is actually locked.
        // (Per-device permission is checked at the API layer.)
        // Checked on every status poll: keep the locked test (a cheap field) first so the registry
        // and file look-ups only happen while the PC is actually locked.
        public bool OfferToPhones => lockState.Locked && Enabled;

        private static bool ProviderRegistered()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(CredentialProviderKey))
                {
                    return key != null;
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Couldn't read the credential provider's registration");
                return false;
            }
        }

        // Turn it on: generate the shared secret, register the provider (one UAC prompt), and seal
        // the account, the secret and the password together. The password is only read here and
        // never leaves the PC. Returns false with a reason the settings can show.
        public bool Enable(string account, string password, out string error)
        {
            error = null;
            if (!Available)
            {
                error = "The unlock component isn't in this build.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(account) || password == null)
            {
                error = "An account and password are needed.";
                return false;
            }
            // Unlocking hands an account over to a phone, so it rides on the PIN pairing: without
            // it, every phone on the network would be allowed. Require pairing to be on first.
            if (pairingRequired != null && !pairingRequired())
            {
                error = "Turn on PIN pairing first (Phones → Ask new phones for a PIN): unlocking is granted per phone.";
                return false;
            }

            account = account.Trim();

            // Check the credentials before storing them: a wrong password here would be sent to
            // LSA on every tap, walking the account toward its lockout threshold.
            if (!ValidatePassword(account, password, out error)) return false;

            var secret = Convert.ToBase64String(RandomBytes(32));
            string sid;
            try
            {
                sid = WindowsIdentity.GetCurrent().User.Value;
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't read the current user's SID");
                error = "Couldn't read the Windows account.";
                return false;
            }

            lock (gate)
            {
                // Hand the secret to the elevated helper in a short-lived, user-only file rather
                // than on its command line.
                var secretFile = Path.Combine(Path.GetTempPath(), "couchpilot-unlock-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    File.WriteAllText(secretFile, secret, new UTF8Encoding(false));
                    if (!RunHelper("register \"" + sid + "\" \"" + secretFile + "\"", out var code) || code != 0)
                    {
                        error = code == 1223 ? "Setup was cancelled." : "Registering the unlock component failed.";
                        return false;
                    }
                }
                finally
                {
                    try { if (File.Exists(secretFile)) File.Delete(secretFile); } catch { }
                }

                // The provider is registered now: if sealing the store fails, roll it back so we
                // don't leave the feature half-on (registered but no credentials).
                try
                {
                    Save(new Store { Secret = secret, Password = password });
                    File.WriteAllText(accountPath, account, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    logger.Error(e, "Couldn't save the unlock store; rolling back the registration");
                    RunHelper("unregister", out _);
                    try { if (File.Exists(storePath)) File.Delete(storePath); } catch { }
                    try { if (File.Exists(accountPath)) File.Delete(accountPath); } catch { }
                    error = "Couldn't save the unlock settings.";
                    return false;
                }
            }
            Changed?.Invoke();
            return true;
        }

        // Turn it off: unregister the provider (one UAC prompt) and delete the sealed password.
        // Only clears the local store once the helper has actually unregistered, so a cancelled
        // UAC prompt leaves the feature as it was rather than silently half-off.
        public bool Disable(out string error)
        {
            error = null;
            lock (gate)
            {
                if (!RunHelper("unregister", out var code) || code != 0)
                {
                    error = code == 1223 ? "Setup was cancelled." : "Removing the unlock component failed.";
                    return false;
                }
                try { if (File.Exists(storePath)) File.Delete(storePath); }
                catch (Exception e) { logger.Error(e, "Couldn't delete the unlock store"); }
                try { if (File.Exists(accountPath)) File.Delete(accountPath); }
                catch (Exception e) { logger.Error(e, "Couldn't delete the unlock account"); }
            }
            Changed?.Invoke();
            return true;
        }

        // True if the account and password really sign in. A network logon is tried first: it
        // touches no session and leaves nothing behind. A Microsoft account may only take an
        // interactive logon, so a refusal is checked that way too before the password is called
        // wrong (the token is closed at once; no session is left). A plain wrong password (or
        // wrong account) is rejected; a less usual failure (an account barred from these logon
        // types, say) is allowed through with a log line rather than blocking a valid setup.
        private bool ValidatePassword(string account, string password, out string error)
        {
            error = null;
            string domain, user;
            var slash = account.IndexOf('\\');
            if (slash >= 0) { domain = account.Substring(0, slash); user = account.Substring(slash + 1); }
            else { domain = "."; user = account; }

            var err = 0;
            foreach (var logonType in new[] { LOGON32_LOGON_NETWORK, LOGON32_LOGON_INTERACTIVE })
            {
                if (LogonUser(user, domain, password, logonType, LOGON32_PROVIDER_DEFAULT, out var token))
                {
                    CloseHandle(token);
                    return true;
                }
                err = Marshal.GetLastWin32Error();
                if (err != ERROR_LOGON_FAILURE && err != ERROR_ACCOUNT_RESTRICTION) break;
            }
            if (err == ERROR_LOGON_FAILURE || err == ERROR_ACCOUNT_RESTRICTION)
            {
                error = "Windows didn't accept that account and password. For a Microsoft account use MicrosoftAccount\\your@email.";
                return false;
            }
            logger.Warn($"Couldn't verify the unlock password (error {err}); accepting it anyway");
            return true;
        }

        // A paired, allowed phone asked to unlock. Make the provider prove it holds the shared
        // secret before sending the account over, then report the handoff (not the unlock's own
        // success, which shows on the PC).
        public UnlockResult TryUnlock()
        {
            if (!lockState.Locked) return UnlockResult.NotLocked;
            if (!Enabled) return UnlockResult.Unavailable;

            var store = TryLoad();
            if (store == null) return UnlockResult.Unavailable;
            var account = Account;
            if (string.IsNullOrEmpty(account)) return UnlockResult.Unavailable;

            try
            {
                // Identification level: a hostile server that squatted the pipe can't impersonate
                // us beyond identifying who we are. Asynchronous, so a read can be given up on
                // (see ReadMessage): a server that never answers mustn't hang this request.
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification))
                {
                    try
                    {
                        pipe.Connect(1500);
                    }
                    catch (TimeoutException)
                    {
                        // The PC is locked (checked above) but no pipe answered: the provider isn't
                        // up (not registered, Hello-only, not loaded). That's "unavailable", not a
                        // claim that the PC isn't locked.
                        return UnlockResult.Unavailable;
                    }
                    pipe.ReadMode = PipeTransmissionMode.Message;

                    // 1. Hello with a fresh nonce.
                    var nonce = Convert.ToBase64String(RandomBytes(32));
                    WriteJson(pipe, "{\"v\":" + ProtocolVersion + ",\"nonce\":\"" + JsonEscape(nonce) + "\"}");

                    // 2. The provider proves it holds the secret over our nonce. If the proof is
                    //    wrong or missing, something other than the real provider answered: stop,
                    //    don't send the password.
                    var challenge = ReadMessage(pipe, ReplyTimeoutMs) ?? "";
                    if (!ProofValid(challenge, store.Secret, nonce)) return UnlockResult.Unavailable;

                    // 3. Now send the credentials.
                    var request = "{\"user\":\"" + JsonEscape(account) + "\",\"password\":\"" + JsonEscape(store.Password) + "\"}";
                    var bytes = Encoding.UTF8.GetBytes(request);
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();
                    Array.Clear(bytes, 0, bytes.Length);

                    var reply = ReadMessage(pipe, ReplyTimeoutMs) ?? "";
                    // { "ok": true } is the only success; everything else is a refusal.
                    return reply.Replace(" ", "").Contains("\"ok\":true") ? UnlockResult.Ok : UnlockResult.Failed;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Unlock over the pipe failed");
                return UnlockResult.Unavailable;
            }
        }

        private static void WriteJson(NamedPipeClientStream pipe, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }

        // The provider's proof is HMAC-SHA256(key = UTF8(secret), data = ASCII(nonce)), base64.
        private static bool ProofValid(string challenge, string secret, string nonce)
        {
            var proof = JsonField(challenge, "proof");
            if (string.IsNullOrEmpty(proof)) return false;
            string expected;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(nonce)));
            }
            return FixedTimeEquals(proof, expected);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // Pulls one string value out of a flat JSON object. Enough for the provider's one-field replies.
        private static string JsonField(string json, string key)
        {
            var needle = "\"" + key + "\"";
            var k = json.IndexOf(needle, StringComparison.Ordinal);
            if (k < 0) return null;
            var c = json.IndexOf(':', k + needle.Length);
            if (c < 0) return null;
            var q = json.IndexOf('"', c + 1);
            if (q < 0) return null;
            var end = json.IndexOf('"', q + 1);
            if (end < 0) return null;
            return json.Substring(q + 1, end - q - 1);
        }

        // How long the provider gets to answer each message. It replies to the hello at once and
        // to the credentials as soon as it has armed the tile, so this only ever fires on a
        // server that isn't the provider (a squatted name) or one that's wedged.
        private const int ReplyTimeoutMs = 5000;

        // Reads one message, giving the server a bounded time to send it. On a timeout the pipe
        // is closed, which cancels the pending read, and the request fails as unavailable rather
        // than sitting on the API thread for good. Needs the pipe opened Asynchronous: a
        // synchronous read holds the handle and can't be abandoned.
        private static string ReadMessage(NamedPipeClientStream pipe, int timeoutMs)
        {
            var read = ReadMessageAsync(pipe);
            if (read.Wait(timeoutMs)) return read.Result;
            // Observe whatever the abandoned read ends with, so it isn't an unobserved fault.
            read.ContinueWith(t => { var _ = t.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            pipe.Dispose();
            throw new TimeoutException("The lock screen didn't answer in time.");
        }

        private static async System.Threading.Tasks.Task<string> ReadMessageAsync(NamedPipeClientStream pipe)
        {
            var buffer = new byte[1024];
            using (var ms = new MemoryStream())
            {
                do
                {
                    int read = await pipe.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;
                    ms.Write(buffer, 0, read);
                } while (!pipe.IsMessageComplete);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private bool RunHelper(string arguments, out int exitCode)
        {
            exitCode = -1;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = arguments,
                    UseShellExecute = true,   // required for the UAC "runas" verb
                    Verb = "runas",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                // The user dismissed the UAC prompt.
                exitCode = 1223;
                return false;
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't run the unlock setup helper");
                return false;
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static byte[] RandomBytes(int n)
        {
            var bytes = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private void Save(Store store)
        {
            var json = Encoding.UTF8.GetBytes(Serialization.ToJson(store));
            var sealed_ = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(json, 0, json.Length);
            File.WriteAllBytes(storePath, sealed_);
        }

        private Store TryLoad()
        {
            try
            {
                if (!File.Exists(storePath)) return null;
                var sealed_ = File.ReadAllBytes(storePath);
                var json = ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.CurrentUser);
                var store = Serialization.FromJson<Store>(Encoding.UTF8.GetString(json));
                Array.Clear(json, 0, json.Length);
                return store;
            }
            catch (Exception e)
            {
                logger.Error(e, "Couldn't read the unlock store");
                return null;
            }
        }

        // Sealed with DPAPI (CurrentUser): only this Windows account on this PC can read it back.
        // The account name lives in a plaintext sidecar instead, so reading it never unseals this.
        private class Store
        {
            public string Secret { get; set; }
            public string Password { get; set; }
        }
    }
}
