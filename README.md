# CouchPilot

Control your Windows gaming PC from your phone: browse and launch your [Playnite](https://playnite.link) library, close the game that's running, and switch display layouts — all from the couch.

CouchPilot is a Playnite extension that serves a small web app on your local network. Open it in the phone's browser and add it to the home screen.

## What it does

- **Library**: your Playnite games with covers, search, and filters (installed, recent, favourites).
- **Launch and close**: start a game; close it gracefully (every window is asked to close first, forced only after 10 s); bring it back to the foreground.
- **Display presets**: save the current monitor layout under a name, apply any saved layout with one tap. If you don't confirm within 15 s the previous layout comes back by itself, so a preset that leaves you with a black screen can't strand you.
- **Audio**: pick the PC's output and input device, set the overall volume and each app's volume, like Windows' volume mixer. The running game sits at the top of the mixer, highlighted, with a live level meter on every app.
- **Vibepollo-aware**: while Vibepollo streams, its virtual display owns the screens, so presets are locked and the automatic rollback stands down.
- **Home Assistant (optional)**: publishes the running game, the active display preset and whether a Vibepollo stream is on to an MQTT broker, with discovery, so the PC shows up as a device; optionally also takes commands (switch preset, close, focus or start a game).
- **Touchpad and keyboard (optional)**: drive the PC's mouse and keyboard from the phone. One finger moves, a tap clicks, two fingers scroll or right-click, tap and hold drags. The phone's own keyboard types on the PC, with a Termux-style bar of extra keys above it (Esc, Tab, Ctrl/Alt/Win/Shift, arrows, F1–F12), plus shortcuts and volume keys.

## Install

1. Download the latest `CouchPilot_x.y.z.pext` from [Releases](../../releases) (or from the artifacts of the latest [build](../../actions)).
2. Double-click it, or drag it onto Playnite, and restart Playnite.
3. Open **Add-ons → Extension settings → CouchPilot**: it lists the address to open on the phone (for example `http://192.168.1.20:8642/`).
4. The first time, Windows Firewall asks whether Playnite may accept connections: allow it on private networks.

The PC and the phone must be on the same network. There is no authentication: don't expose the port to the internet (use a VPN to reach it from outside).

## Audio

The **Audio** tab shows the default output (tap **Cambia** to switch: speakers, headphones, the TV over HDMI…), its volume, and below it one slider per app that has sound open on that output, as in Windows' mixer. Apps are grouped by executable, so a browser's many tabs are one slider; the game Playnite launched comes first, in yellow. At the bottom, the default microphone with its volume and mute.

- Switching device sets the Windows default for every role, the way the taskbar's sound flyout does: apps that follow the default move over by themselves, apps pinned to a device in Windows' settings stay where they are.
- Nothing to turn on: changing the volume is no riskier than starting a game.
- Choosing a different output per app isn't supported: Windows only exposes that through internal interfaces that change between versions.

## Touchpad and keyboard

Off by default: whoever reaches the port could type on the PC. Turn it on in the extension settings with **Permetti al telefono di usare mouse e tastiera del PC**; the Control tab connects by itself.

- Modifiers in the bar work like Termux: a tap holds them for the next key (or the next letter typed: Ctrl, then `c`), a double tap locks them until you tap again. **Fn** swaps in F1–F12.
- Text arrives as Unicode, so accents and emoji come through whatever the PC's keyboard layout; keys from the bar are sent with their scan code too, for games that read raw input.
- Windows ignores synthetic input aimed at windows running as administrator, and at the UAC prompt or the lock screen: those need a real mouse.

Input travels over a WebSocket on `/ws/input`, a few bytes per command (the format is in `Input/RemoteInput.cs`). Connections from pages on other sites are refused, and a button held down is let go if the phone drops.

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
COUCHPILOT_HOST=http://<pc-address>:8642 pnpm dev
```

The extension builds on Windows (`pnpm build` in `web/` first, then `dotnet build src/CouchPilot`); CI does both and packages the `.pext` on every push. Tag `vX.Y.Z` to publish a release.

## Credits

Game closing follows the approach of [Vibepollo](https://github.com/Nonary/Vibepollo)'s Playnite launcher. Licensed under the GPL-3.0.
