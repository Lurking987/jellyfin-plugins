# Jellyfin Plugins

A collection of custom plugins and tools for [Jellyfin](https://jellyfin.org) media server.

---

## Plugins & Tools

### [StashSync](./StashSync)

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.9.0+-blue?logo=jellyfin)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green)

Syncs [Stash App](https://stashapp.cc) Groups to Jellyfin as Movies. Each Group becomes a Movie entry with its scenes mapped as chapter markers. Supports automatic TheMovieDB metadata and image fetching.

**[→ View plugin README](./StashSync/README.md)**

---

### [StashProxy](./StashProxy)

![Docker](https://img.shields.io/badge/Docker-required-blue?logo=docker)
![Python](https://img.shields.io/badge/Python-3.12-blue?logo=python)
![License](https://img.shields.io/badge/license-MIT-green)

A companion proxy service for StashSync. Streams all scenes in a Stash Group as one continuous video using FFmpeg — no re-encoding, no extra disk space. Required for multi-scene chapter playback to work correctly in Jellyfin.

**[→ View StashProxy README](./StashProxy/README.md)**

---

## Installation

### StashSync Plugin

Each plugin is installed manually as a DLL:

1. Download the DLL from the [Releases](../../releases) page
2. Create a folder in your Jellyfin plugins directory named `StashSync_1.0.0.0`
3. Copy the DLL into that folder
4. Restart Jellyfin
5. Confirm it loaded under **Dashboard → Plugins → My Plugins**

See the [StashSync README](./StashSync/README.md) for full setup instructions.

### StashProxy

StashProxy runs as a Docker container alongside Jellyfin:

```bash
cd StashProxy
docker-compose up -d
```

See the [StashProxy README](./StashProxy/README.md) for full setup instructions including TrueNAS Scale.

---

## License

MIT
