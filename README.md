# CouchPilot

Control your Windows gaming PC from your phone: browse and launch your [Playnite](https://playnite.link) library, close the game that's running, and switch display layouts — all from the couch.

CouchPilot is a Playnite extension that serves a small web app on your local network. Open it in the phone's browser and add it to the home screen.

## What it does

- **Library**: your Playnite games with covers, search, and filters (installed, recent, favourites).
- **Statistics**: behind the chart icon in Giochi, how much you played this week, month or year, day by day, the most played games, the library at large and the installed games you left months ago; each game's sheet shows its own hours, sessions and last 12 weeks. Playnite keeps only totals, so CouchPilot logs every session from the day it's installed (`sessions.jsonl` in the extension's data folder). A day of play runs from 5:00 to 5:00: a session past midnight counts for the evening it started.
- **Install and uninstall**: install a game from the phone, or uninstall it after a confirmation; Playnite hands the job to the game's library (Steam, Epic, GOG…), whose client may open on the PC.
- **Launch and close**: start a game; close it gracefully (every window is asked to close first, forced only after 10 s); bring it back to the foreground.
- **Controllers**: the Xbox-style pads connected to the PC, with their battery, on the running game's card and in the Control tab; a low battery turns red. PlayStation and Switch pads don't speak XInput, so they show up only through Steam Input.
- **Display presets**: save the current monitor layout under a name, apply any saved layout with one tap. If you don't confirm within 15 s the previous layout comes back by itself, so a preset that leaves you with a black screen can't strand you.
- **Audio**: pick the PC's output and input device, set the overall volume and each app's volume, like Windows' volume mixer. The running game gets a card at the top of the mixer with its art and Playnite's metadata, and every app has a live level meter.
- **Now playing**: what plays on the PC (Spotify, a browser tab, the Media Player…) with its artwork, a progress bar and play/pause and skip, from Windows' own media controls (Windows 10 1809 or later).
- **Audio with a preset (optional)**: a display preset can switch the audio output too, say to the TV over HDMI for «Solo TV». If the preset is rolled back, the audio goes back as well. Presets leave the audio alone unless you pick an output for them (the speaker button next to each preset).
- **Vibepollo-aware**: while Vibepollo streams, its virtual display owns the screens, so presets are locked and the automatic rollback stands down.
- **Home Assistant (optional)**: publishes the running game, the active display preset and whether a Vibepollo stream is on to an MQTT broker, with discovery, so the PC shows up as a device; optionally also takes commands (switch preset, close, focus or start a game).
- **Touchpad and keyboard (optional)**: drive the PC's mouse and keyboard from the phone. One finger moves, a tap clicks, two fingers scroll or right-click, tap and hold drags. The phone's own keyboard types on the PC, with a Termux-style bar of extra keys above it (Esc, Tab, Ctrl/Alt/Win/Shift, arrows, F1–F12), plus shortcuts.

## Install

1. Download the latest `CouchPilot_x.y.z.pext` from [Releases](../../releases) (or from the artifacts of the latest [build](../../actions)).
2. Double-click it, or drag it onto Playnite, and restart Playnite.
3. Open **Add-ons → Extension settings → CouchPilot**: it lists the address to open on the phone (for example `http://192.168.1.20:8642/`, or `https://couch.internal:8642/` once [HTTPS](#https-and-installing-the-app) is set up).
4. The first time, Windows Firewall asks whether Playnite may accept connections: allow it on private networks.

The PC and the phone must be on the same network. There is no authentication: don't expose the port to the internet (use a VPN to reach it from outside).

Over plain HTTP the page works, but the phone's browser won't install it as an app: that takes HTTPS, below.

## HTTPS and installing the app

Chrome installs a site as an app (icon on the home screen, no address bar) only over HTTPS. Give CouchPilot a certificate and the same port speaks HTTPS instead of HTTP; nothing to install on the PC.

The certificate comes from a CA of your own: a key pair you create once and tell the phone to trust. This one is limited, through name constraints, to the PC's name and address, so even if its key leaked it couldn't vouch for any other site.

**1. Pick a name and point it at the PC.** `couch.internal` works well: `.internal` is reserved for private networks and will never exist on the internet. Add a DNS rewrite `couch.internal → <PC address>` on the resolver the phone uses (AdGuard Home, Pi-hole, the router). If the phone comes in through a VPN, that's the resolver the VPN hands out. Give the PC a DHCP reservation, since the address goes into the certificate.

**2. Create the CA and the certificate** (any machine with OpenSSL; Git Bash on Windows has it):

```sh
NAME=couch.internal
IP=192.168.1.20        # the PC's address
export MSYS_NO_PATHCONV=1   # Git Bash only: otherwise it mangles "/CN=..." into a Windows path

# The CA: ten years, and it can only sign for $NAME and $IP.
openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.crt -days 3650 \
  -subj "/CN=CouchPilot CA" \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -addext "nameConstraints=critical,permitted;DNS:$NAME,permitted;IP:$IP/255.255.255.255"

# CouchPilot's certificate: five years, valid for the name and for the bare address.
openssl req -newkey rsa:2048 -nodes -keyout couch.key -out couch.csr -subj "/CN=$NAME"
printf "subjectAltName=DNS:$NAME,IP:$IP\nextendedKeyUsage=serverAuth\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n" > couch.ext
openssl x509 -req -in couch.csr -CA ca.crt -CAkey ca.key -CAcreateserial -days 1825 -extfile couch.ext -out couch.crt

# The .pfx for CouchPilot. 3DES rather than OpenSSL 3's default AES, which older
# Windows builds can't open.
openssl pkcs12 -export -in couch.crt -inkey couch.key -certfile ca.crt -out couch.pfx \
  -certpbe PBE-SHA1-3DES -keypbe PBE-SHA1-3DES -macalg sha1
```

RSA rather than an elliptic curve key on purpose: .NET Framework, which Playnite runs on, handles it everywhere.

**3. Trust the CA on the phone.** Copy `ca.crt` (only that one) to the phone and install it from *Settings → Security → Encryption & credentials → Install a certificate → CA certificate* (the path varies a little by brand). Android will keep reminding you that the network "may be monitored": that's how it labels every CA you add yourself.

**4. Give CouchPilot the certificate.** In the extension settings pick `couch.pfx` next to **Certificato HTTPS (.pfx)** and type its password. The server restarts and the settings list `https://couch.internal:8642/` first. Open it on the phone and choose *Install app* from Chrome's menu.

Keep `ca.key` somewhere safe and off the PC (a password manager will do): it's only needed again to sign a new certificate, in five years or if the PC's address changes. `couch.key`, `couch.csr` and `couch.ext` can go once the `.pfx` exists.

Once installed the app lives on the phone: it opens at once even with the PC off (and says so), covers come up from the phone while the library refreshes, and a long press on its icon goes straight to Schermi, Audio or Controllo. After an update of the extension the phone switches to the new version by itself the first time it reaches the PC.

With a certificate set, plain `http://` on that port stops answering, and the installed app lives at the `https://` address. If the certificate can't be loaded (moved, wrong password) the server doesn't start at all and Playnite shows why, rather than quietly going back to HTTP.

## Audio

The **Audio** tab shows the default output (tap **Cambia** to switch: speakers, headphones, the TV over HDMI…), its volume, and below it one slider per app that has sound open on that output, as in Windows' mixer. Apps are grouped by executable, so a browser's many tabs are one slider; the game Playnite launched comes first, on a card with its art, cover, developer, year, genre, how long you've been playing and a mixing-desk meter. At the bottom, the default microphone with its volume and mute.

- Switching device sets the Windows default for every role, the way the taskbar's sound flyout does: apps that follow the default move over by themselves, apps pinned to a device in Windows' settings stay where they are.
- Nothing to turn on: changing the volume is no riskier than starting a game.
- Choosing a different output per app isn't supported: Windows only exposes that through internal interfaces that change between versions.

## How the phone talks to the PC

One WebSocket, `/ws`, open while the app is on screen, carrying small binary messages specified byte by byte in [docs/protocol.md](docs/protocol.md). The touchpad and the mixer send their commands up it; down it come the mixer's volumes and level meters (25 times a second while the Audio tab is open) and a two-byte notice whenever the PC's status or the game library changes. The phone then fetches that data again as JSON from `/api`, where it lives along with cover art and actions such as starting a game. Nothing polls.

## Touchpad and keyboard

Off by default: whoever reaches the port could type on the PC. Turn it on in the extension settings with **Permetti al telefono di usare mouse e tastiera del PC**; the Control tab connects by itself.

- Modifiers in the bar work like Termux: a tap holds them for the next key (or the next letter typed: Ctrl, then `c`), a double tap locks them until you tap again. **Fn** swaps in F1–F12.
- Text arrives as Unicode, so accents and emoji come through whatever the PC's keyboard layout; keys from the bar are sent with their scan code too, for games that read raw input.
- Windows ignores synthetic input aimed at windows running as administrator, and at the UAC prompt or the lock screen: those need a real mouse.

Input travels over the phone's live WebSocket, a few bytes per command. Connections from pages on other sites are refused, and a button held down is let go if the phone drops.

## Home Assistant

Off by default. In the extension settings tick **Pubblica lo stato del PC su Home Assistant** and fill in the MQTT broker (address, port, user, password). With discovery on (the default `homeassistant` prefix) Home Assistant creates a *CouchPilot &lt;PC name&gt;* device with:

| Entity | Type | Value |
|---|---|---|
| Gioco | sensor | running game, or `Nessuno`; attributes `game_id`, `source`, `started_at` |
| In gioco | binary sensor | on while a game runs |
| Preset schermo | sensor | active display preset, or `Personalizzato` |
| Stream Vibepollo | binary sensor | on while Vibepollo streams |

### Commands

A second, separate opt-in: **Permetti a Home Assistant di comandare il PC**. It adds:

| Entity | Type | Does |
|---|---|---|
| Cambia schermo | select | applies a display preset |
| Chiudi gioco | button | closes the running game (gracefully, then forced after 10 s) |
| Primo piano | button | brings the running game to the foreground |

Presets applied from Home Assistant are kept straight away: there is nobody in front of the screen to confirm, so there is no 15 s rollback. While Vibepollo streams they are refused, as from the phone.

To start a game, publish its Playnite id or exact name to `couchpilot/<pc>/command/start_game`:

```yaml
action: mqtt.publish
data:
  topic: couchpilot/pc-gaming/command/start_game
  payload: Elden Ring
```

The other commands live on the same prefix (`display_preset` with the preset name, `close_game`, `focus_game`), if you'd rather call them directly. Anyone who can publish to your broker can use them, which is why they are off unless you turn them on.

Everything is published as one retained JSON document on `couchpilot/<pc>/state`; `couchpilot/<pc>/availability` turns `offline` when Playnite closes or the PC drops off. The password is stored in plain text with the other extension settings.

## Development

```
web/                 Vue 3 + TypeScript PWA (Open Props, Reka UI)
src/CouchPilot/      Playnite generic extension (C#, .NET Framework 4.6.2)
  Server/            Embedded web server (EmbedIO) and the JSON API
  Library/           Game list and images from the Playnite database
  Games/             Running sessions, closing and focusing games
  Displays/          Display presets on top of the Windows CCD API
```

Web app against fake data, no PC needed:

```
cd web && pnpm install && pnpm dev:mock
```

Against a running CouchPilot:

```
COUCHPILOT_HOST=http://<pc-address>:8642 pnpm dev     # or https:// if the PC has a certificate
```

The extension builds on Windows (`pnpm build` in `web/` first, then `dotnet build src/CouchPilot`); CI does both and packages the `.pext` on every push. Tag `vX.Y.Z` to publish a release.

## Credits

Game closing follows the approach of [Vibepollo](https://github.com/Nonary/Vibepollo)'s Playnite launcher. Licensed under the GPL-3.0.
