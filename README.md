# CouchPilot

Control your Windows gaming PC from your phone: browse and launch your [Playnite](https://playnite.link) library, close the game that's running, and switch display layouts — all from the couch.

CouchPilot is a Playnite extension that serves a small web app on your local network. Open it in the phone's browser and add it to the home screen.

## What it does

- **Library**: your Playnite games with covers, search, and filters (installed, recent, favourites).
- **Launch and close**: start a game; close it gracefully (every window is asked to close first, forced only after 10 s); bring it back to the foreground.
- **Display presets**: save the current monitor layout under a name, apply any saved layout with one tap. If you don't confirm within 15 s the previous layout comes back by itself, so a preset that leaves you with a black screen can't strand you.
- **Coming next**: touchpad and keyboard.

## Install

1. Download the latest `CouchPilot_x.y.z.pext` from [Releases](../../releases) (or from the artifacts of the latest [build](../../actions)).
2. Double-click it, or drag it onto Playnite, and restart Playnite.
3. Open **Add-ons → Extension settings → CouchPilot**: it lists the address to open on the phone (for example `http://192.168.1.20:8642/`).
4. The first time, Windows Firewall asks whether Playnite may accept connections: allow it on private networks.

The PC and the phone must be on the same network. There is no authentication: don't expose the port to the internet (use a VPN to reach it from outside).

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
