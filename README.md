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

A companion proxy service for StashSync. Segments all scenes in a Stash Group into a complete HLS VOD stream on first play, enabling full random-access seeking on all Jellyfin clients. No re-encoding — FFmpeg stream-copies each scene. Segments are cached and cleaned up automatically.

**[→ View StashProxy README](./StashProxy/README.md)**

---

## Quick Setup

### 1. Install the StashSync Plugin

1. Download the DLL from the [Releases](../../releases) page
2. Create a folder in your Jellyfin plugins directory named `StashSync_1.0.0.0`
3. Copy the DLL into that folder
4. Restart Jellyfin
5. Confirm it loaded under **Dashboard → Plugins → My Plugins**

### 2. Deploy StashProxy

Create a `docker-compose.yml` with your stash-groups path and start it:

```bash
cd StashProxy
docker-compose up -d
```

### 3. Configure StashSync

In Jellyfin → **Dashboard → Plugins → StashSync → Settings**:
- Set your Stash URL, API key (if needed), and TMDB API key
- Set the STRM Output Path to a folder Jellyfin can read
- Set the Proxy URL to `http://<your-server-ip>:5678`
- Run the sync task

### 4. Add the library

Add the STRM Output Path as a **Movies** library in Jellyfin with TheMovieDB enabled.

See the individual READMEs for full setup instructions and TrueNAS Scale specifics.

---

## How It All Fits Together

```
Stash App
  └── Groups (movies) + Scenes (chapters)
         ↓ StashSync plugin syncs
Jellyfin Library
  └── .strm files → http://<proxy>/group/<id>/playlist.m3u8
         ↓ on play
StashProxy
  └── Segments scenes via FFmpeg → serves HLS VOD playlist
         ↓
Jellyfin plays with full seeking, chapters, and TMDB metadata
```

---

## License

MIT
