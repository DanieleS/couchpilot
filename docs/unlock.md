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
- **holds the pipe's shared secret** (its own copy, DPAPI-protected the same way) so it can prove
  to the provider that an unlock request really came from CouchPilot and not from some other
  process that found the pipe;
- **answers `POST /api/unlock`**: when a paired, allowed phone asks and the PC is locked, it
  decrypts the password, connects to the provider's pipe, sends the secret + username + password,
  and reports back whether the provider accepted the handoff (not whether the unlock itself
  succeeded — that shows on the PC's screen a moment later).

The password is only ever in CouchPilot's memory for the few milliseconds of a handoff, and is
wiped after. It never reaches the phone, and the phone never sends a password — it sends only
"please unlock," authenticated by the pairing it already has.

## The pipe protocol

- **Name:** `\\.\pipe\CouchPilotUnlock`. Message-mode, duplex, one client at a time.
- **Server:** the credential provider. It creates the pipe when a logon/unlock scenario begins
  and closes it when the scenario ends, so the pipe exists exactly while the lock screen is up —
  which is exactly when the phone would ask. CouchPilot connecting at any other time simply finds
  no pipe, and reports the PC isn't on the lock screen.
- **DACL:** the pipe grants access only to SYSTEM and to the enrolled user's SID (captured at
  `register` time). A process running as anyone else can't even open it.
- **Client:** CouchPilot, in your session.

On top of the DACL, every request carries the shared secret, and the server also checks the
client process is the enrolled user. Two independent gates: the OS won't let a stranger open the
pipe, and even a same-user process can't drive it without the secret, which lives DPAPI-sealed in
CouchPilot and as an LSA secret in the provider.

### Request (phone side → provider)

One message, UTF-8 JSON:

```json
{ "v": 1, "secret": "<base64, 32 bytes>", "user": "DOMAIN\\name", "password": "…" }
```

- `v` — protocol version, `1`. A provider that doesn't recognise it answers `bad-version`.
- `secret` — the shared secret established at enrollment, base64. Wrong or missing → `denied`.
- `user` — the account to sign in. `.` or an empty domain means the local machine.
- `password` — the account password.

### Response (provider → phone side)

One message, UTF-8 JSON:

```json
{ "ok": true }
```

or

```json
{ "ok": false, "code": "denied" }
```

| `code` | Meaning |
|---|---|
| `denied` | the secret didn't match, or the client isn't the enrolled user |
| `bad-version` | the provider doesn't speak this `v` |
| `bad-request` | the message didn't parse |
| `busy` | an unlock is already in flight |

`ok: true` means the provider has accepted the credentials and asked LogonUI to submit them. It
does **not** mean the password was right: a wrong password fails on the PC's screen like any
other, and the PC stays locked. CouchPilot reports the handoff, not the verdict.

The password field is overwritten in both processes as soon as it's been used.

## `POST /api/unlock`

Behind the pairing gate like every other `/api` route. Gated three more ways:

1. unlocking must be enabled in the settings (there's a password on file and the provider is
   registered);
2. the asking phone must have the **unlock** permission (per-device, alongside *mouse and
   keyboard* and *captures* — off by default, you grant it in the settings);
3. the PC must actually be locked.

No body. Responses mirror the other actions' shape:

- `200 { "ok": true }` — the provider accepted the handoff; the PC is unlocking.
- `409 { code: "not-locked" }` — the PC isn't on the lock screen (so there's nothing to do, or
  the provider's pipe isn't up).
- `409 { code: "unavailable" }` — unlocking isn't set up, or the provider didn't answer.
- `403 { code: "forbidden" }` — this phone may not unlock.

The status (`GET /api/status`, and so the live socket's CHANGED) gains:

- `locked` — the PC is on the lock screen right now;
- `unlock` — this phone may unlock (enabled, registered, and permitted for this device).

The button shows only when both are true.

## Security

What's stored, and where:

| Secret | Where | Protected by | Readable by |
|---|---|---|---|
| Account password | `unlock.bin`, plugin data folder | DPAPI, CurrentUser | you, on this PC, only |
| Shared secret (CouchPilot's copy) | plugin data folder | DPAPI, CurrentUser | you, on this PC, only |
| Shared secret (provider's copy) | LSA secret | LSA | SYSTEM only |

The threat this guards against is another device on your network, not someone sitting at your
PC — if they're at the keyboard they can type the password themselves. So:

- the phone is already gated by the PIN pairing; the unlock permission is per-device and off by
  default, the same model as mouse-and-keyboard;
- a password at rest is DPAPI-sealed, not plaintext (unlike, for now, the MQTT and certificate
  passwords — those don't unlock a PC);
- the pipe is reachable only by you and SYSTEM, and needs the shared secret on top;
- the provider fails safe: anything wrong and the normal sign-in methods are still right there,
  so a broken or half-registered install never locks you out of your own PC.

Keep the port off the internet, as the README already says. Unlock makes that matter more, not
less: behind the PIN it's still one more thing a phone that got in could reach.

## Building

The DLL and the setup helper are MSVC/C++, built on the same `windows-latest` runner that
already builds the extension, with the Visual Studio toolchain that image ships. Both land in the
`.pext` next to `CouchPilot.dll`; CouchPilot finds `CouchPilotUnlockSetup.exe` beside its own
assembly and runs it on demand. A plain checkout without the C++ build still produces a working
extension — unlocking just reports itself unavailable, the same way the gallery falls back when
`ffmpeg.exe` isn't there.
