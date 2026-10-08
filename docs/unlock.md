# Unlocking the PC from the phone

CouchPilot already runs while Windows is locked: Playnite stays open, the server keeps
answering, and the phone still reaches it. What it can't do from there is get past the lock
screen, because the keyboard and the touchpad it drives (`SendInput`, from Playnite's own
session) can't touch Windows' secure desktop — the one Winlogon shows while the PC is locked.
Only a **credential provider**, a small native DLL that Windows itself loads into the lock
screen, may hand Windows the credentials that unlock it.

So unlocking is split across two processes that live on opposite sides of that fence:

| Side | Runs as | Lives in | Job |
|---|---|---|---|
| CouchPilot (the extension) | you | your Playnite session | knows the phone, holds the password, asks for the unlock |
| `CouchPilotUnlock.dll` (the credential provider) | SYSTEM | Winlogon's lock screen | hands the password to Windows |

They can't share memory and they sit on different desktops, but a **named pipe** crosses
between them: the credential provider opens it, CouchPilot connects to it, and the password
travels down it only at the moment of an unlock.

This document is the specification for that handoff. The implementations must follow it:

| Part | Files | Covers |
|---|---|---|
| Credential provider | `src/CouchPilotUnlock/**` | the DLL Windows loads on the lock screen |
| Setup helper | `src/CouchPilotUnlockSetup/**` | registering the DLL, the shared secret (needs admin) |
| Extension | `src/CouchPilot/Unlock/UnlockService.cs`, `LockState.cs` | the password store, the pipe client, lock detection |
| Extension | `src/CouchPilot/Server/ApiController.cs` | `POST /api/unlock` |
| Phone | `web/src/*`, `web/src/components/ControlView.vue` | the Unlock button and its states |

Change the wire format on one side, change this document and the other side in the same commit.

## What it does, and what it can't

**It unlocks a PC that's already been logged into and then locked** — the couch case: the PC
went to sleep, or you pressed Win+L, and you want it back without reaching for a keyboard. That
is the whole of it.

It does **not** get you past the sign-in screen after a reboot or a sign-out, and it can't,
because before anyone signs in your session doesn't exist: Playnite isn't running, CouchPilot
isn't listening, and the phone has nothing to talk to. A PC that reboots into the lock screen
needs Windows' own automatic sign-in for the phone to be able to reach it at all; that's a
Windows setting, outside CouchPilot, and the README points at it.

Two more limits worth knowing before you turn this on:

- **It uses your account password, not your Windows Hello PIN.** Windows only lets an outside
  credential provider sign in with the account password (for a Microsoft account, that's the
  Microsoft-account password). The Hello PIN is sealed to the TPM and the on-screen keypad and
  can't be replayed by anyone else, which is the whole point of it — so CouchPilot can't use it.
- **"For improved security, only allow Windows Hello sign-in" must be off** (Settings → Accounts
  → Sign-in options). While it's on, Windows hides every password-based provider, this one
  included, and the Unlock button will do nothing.

If you're not comfortable with a stored account password (see [Security](#security) for exactly
how it's kept), don't enable this. The feature is off until you turn it on, and turning it off
removes the password and unregisters the DLL.

## The pieces

### The credential provider — `CouchPilotUnlock.dll`

A standard V2 credential provider (`ICredentialProvider` + `ICredentialProviderCredential2`),
written in C++ because a managed DLL has no business being loaded into Winlogon: if it threw and
took LogonUI down with it, you'd be staring at a lock screen that can't accept any sign-in. The
native DLL does the least it can and never blocks the UI thread.

It takes part only in the two scenarios where unlocking makes sense — `CPUS_UNLOCK_WORKSTATION`
and `CPUS_LOGON` — and stays out of `CPUS_CREDUI` and `CPUS_CHANGE_PASSWORD` entirely. In those
two scenarios it contributes a single credential tile that stays hidden (`cpgsNone`): the user
never sees or clicks anything; the existing password and PIN tiles are untouched. The provider
exists only to be ready to submit when CouchPilot says to.

While a scenario is active the provider runs a pipe server (below) on its own thread. When a
message arrives with a username and password:

1. it builds a `KERB_INTERACTIVE_UNLOCK_LOGON` for that account (domain\user or
   `.`\user for a local account, `MicrosoftAccount\…` for an MSA), packs it the way LSA wants,
   and holds it as this credential's serialization;
2. it calls `ICredentialProviderEvents::CredentialsChanged`, which makes LogonUI re-read the
   tile;
3. on the next `GetSerialization` it returns `CPGSR_RETURN_CREDENTIAL_FINISHED`, and Windows
   does the actual unlock with those credentials;
4. whatever the result, it zeroes the password bytes it held.

If the credentials are wrong Windows says so exactly as it would for a typed password, and the
tile goes back to waiting. The provider never retries on its own.

### The setup helper — `CouchPilotUnlockSetup.exe`

Registering a credential provider means writing under `HKLM` and dropping the DLL somewhere
SYSTEM can load it — both need administrator rights, which Playnite (and so CouchPilot) doesn't
have. The helper is a tiny console app manifested `requireAdministrator`, so launching it raises
one UAC prompt. CouchPilot shells out to it for three verbs:

- `register` — copy `CouchPilotUnlock.dll` to `%ProgramFiles%\CouchPilot\`, register its CLSID
  (`HKCR\CLSID\{…}\InprocServer32`) and list it under
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{…}`, and
  write the pipe's shared secret as an LSA secret (readable only by SYSTEM). Prints nothing
  secret to stdout.
- `unregister` — undo all of that and delete the DLL and the secret.
- `status` — report whether the provider is currently registered, so the settings screen can
  show the real state rather than what it last did.

The CLSID is fixed and belongs to CouchPilot: `{b6f4c3a2-7e51-4a90-9c3d-1f8e2a6b5d04}`.

### The extension — `UnlockService`

Everything on the Playnite side. It:

- **watches the lock state** via `SystemEvents.SessionSwitch` (`SessionLock` / `SessionUnlock`),
  so the phone's status carries a plain `locked` flag and the Unlock button only shows when it
  can do something;
- **keeps the password**, encrypted with DPAPI (`ProtectedData`, `CurrentUser` scope) in
  `unlock.bin` in the plugin's data folder. DPAPI ties the ciphertext to your Windows account on
  this machine: nothing and no one else — not another user, not the file copied to another PC —
  can turn it back into the password. While the PC is locked you are still signed in, so your
  DPAPI key is available and CouchPilot can decrypt it; this is exactly the window in which it
  needs to;
- **holds the pipe's shared secret** (its own copy, DPAPI-protected the same way) so it can tell
  the real provider from an impostor: it makes the server prove it knows the secret before it ever
  sends the account over (see the handshake below);
- **checks the password** when you first enable the feature, with `LogonUser` (a network logon,
  then an interactive one if that is refused, since a Microsoft account may only take the latter;
  the token is closed at once), so a typo isn't saved to be rejected by LSA on every later tap;
- **answers `POST /api/unlock`**: when a paired, allowed phone asks and the PC is locked, it runs
  the handshake, sends the username and password to the proven provider, and reports whether the
  provider accepted the handoff (not whether the unlock itself succeeded — that shows on the PC's
  screen a moment later).

The password lives in CouchPilot's memory only for the span of a handoff. (It's a managed .NET
string, so it can't be reliably zeroed; what CouchPilot can clear — the request byte buffers — it
does.) It never reaches the phone, and the phone never sends a password — it sends only "please
unlock," authenticated by the pairing it already has. The enrolled account name is kept in the
clear in a sidecar file (it isn't a secret), so showing it in the settings never has to unseal the
password.

## The pipe protocol

- **Name:** `\\.\pipe\CouchPilotUnlock`. Message-mode, duplex, one client at a time.
- **Server:** the credential provider. It creates the pipe when a logon/unlock scenario begins
  and closes it when the scenario ends, so the pipe exists exactly while the lock screen is up —
  which is exactly when the phone would ask. It creates it with `FIRST_PIPE_INSTANCE`, so if some
  other process has squatted the name the provider fails to open it and simply never serves —
  nothing is disclosed, and the normal sign-in methods still work. CouchPilot connecting when no
  pipe is up reports unlocking unavailable.
- **DACL:** the pipe grants access only to SYSTEM and to the enrolled user's SID (captured at
  `register` time). A process running as anyone else can't even open it.
- **Client:** CouchPilot, in your session. It connects at the `Identification` impersonation
  level, so even a hostile server can't impersonate it beyond reading who it is, and it gives the
  server a few seconds to answer each message: one that never does (a squatted name, a wedged
  provider) makes the request fail as unavailable rather than hang.

Two things have to be true before the password crosses the pipe, and neither trusts the pipe name
alone: the server must prove it is the real provider (it holds the LSA secret), and the client
must be the enrolled user (the DACL, plus an impersonation check on the server). So the exchange
is a three-step handshake, not a single message.

### The handshake

1. **Client → server**, UTF-8 JSON: `{ "v": 1, "nonce": "<base64, 32 bytes>" }`. A fresh random
   nonce each time. `v` is the protocol version; a server that doesn't know it answers
   `bad-version`.
2. **Server → client**: `{ "proof": "<base64>" }`, where `proof` is
   `HMAC-SHA256(key = utf8(secret), msg = ascii(nonce))`. Only the real provider, holding the
   secret as an LSA secret, can compute it. The client recomputes it and compares in constant
   time; a mismatch (or a squatter that can't answer) means *stop* — the password is never sent.
3. **Client → server**: `{ "user": "DOMAIN\\name", "password": "…" }`, now that the server is
   proven. `.` or an empty domain means the local machine.

The secret itself never travels the pipe; it's only ever an HMAC key on both ends.

### Response (server → client)

One message, UTF-8 JSON — `{ "ok": true }`, or `{ "ok": false, "code": "…" }`:

| `code` | Meaning |
|---|---|
| `denied` | the client isn't the enrolled user |
| `bad-version` | the provider doesn't speak this `v` |
| `bad-request` | a message didn't parse |
| `busy` | an unlock is already in flight, or there's no events sink to submit through |

`ok: true` means the provider has accepted the credentials and asked LogonUI to submit them. It
does **not** mean the password was right: a wrong password fails on the PC's screen like any
other, and the PC stays locked. CouchPilot reports the handoff, not the verdict. An armed
credential that LogonUI never consumes disarms itself after half a minute, so a request that goes
nowhere doesn't wedge the tile or leave the password in the provider's memory.

## `POST /api/unlock`

Behind the pairing gate like every other `/api` route. Gated three more ways:

1. unlocking must be enabled in the settings (there's a password on file and the provider is
   registered) — and enabling it *requires PIN pairing to be on*, so there is always a specific
   phone to grant;
2. the request must come from a **paired phone that has the unlock permission** (per-device,
   alongside *mouse and keyboard* and *captures* — off by default). With pairing off there's no
   such phone, so unlocking is simply unavailable; a `null` device is refused, not waved through;
3. the PC must actually be locked.

No body. Responses mirror the other actions' shape:

- `200 { "ok": true }` — the provider accepted the handoff; the PC is unlocking.
- `409 { code: "not-locked" }` — the PC isn't on the lock screen.
- `409 { code: "unavailable" }` — unlocking isn't set up, or the provider's pipe didn't answer
  (not registered, Hello-only, not loaded) even though the PC is locked.
- `403 { code: "forbidden" }` — this phone isn't a paired phone allowed to unlock.

The status (`GET /api/status`, and so the live socket's CHANGED) gains:

- `locked` — the PC is on the lock screen right now;
- `unlock` — this phone may unlock (enabled, registered, and permitted for this device).

While `locked` is true the phone says so above every tab; the Unlock button is there only when `unlock` is true too.

## Security

What's stored, and where:

| Secret | Where | Protected by | Readable by |
|---|---|---|---|
| Account password + shared secret | `unlock.bin`, plugin data folder | DPAPI, CurrentUser | you, on this PC, only |
| Shared secret (provider's copy) | LSA secret | LSA | SYSTEM only |
| Account **name** (not a secret) | `unlock-account.txt`, plugin data folder | — | anyone on this PC |

The threat this guards against is another device on your network, and a non-admin user on the PC
reaching for the account password — not someone already sitting at your keyboard, who can type it
themselves. So:

- enabling it requires PIN pairing, and the unlock permission is per-device and off by default,
  the same model as mouse-and-keyboard; a request from an unpaired device is refused;
- the password and the shared secret at rest are DPAPI-sealed, not plaintext (unlike, for now, the
  MQTT and certificate passwords — those don't unlock a PC);
- the pipe is reachable only by you and SYSTEM; on top of that the provider proves it holds the
  secret (HMAC over the client's nonce) *before* CouchPilot sends the account, and it claims the
  name with `FIRST_PIPE_INSTANCE`, so a process that squats the pipe neither learns the password
  nor blocks a loud failure;
- the password is verified once at enable time, so a wrong one isn't replayed to LSA on every tap;
- the provider fails safe: anything wrong and the normal sign-in methods are still right there,
  so a broken or half-registered install never locks you out of your own PC. A failed registration
  rolls itself back rather than leaving a half-configured provider.

Keep the port off the internet, as the README already says. Unlock makes that matter more, not
less: behind the PIN it's still one more thing a phone that got in could reach.

## Building

The DLL and the setup helper are MSVC/C++, built on the same `windows-latest` runner that
already builds the extension, with the Visual Studio toolchain that image ships. Both land in the
`.pext` next to `CouchPilot.dll`; CouchPilot finds `CouchPilotUnlockSetup.exe` beside its own
assembly and runs it on demand. A plain checkout without the C++ build still produces a working
extension — unlocking just reports itself unavailable, the same way the gallery falls back when
`ffmpeg.exe` isn't there.
