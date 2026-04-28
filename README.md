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

### ### [StashMovies](./StashMovies)

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.0+-blue?logo=jellyfin)
![.NET](https://img.shields.io/badge/.NET-9.0-purple?logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green)

Syncs [Stash App](https://stashapp.cc) Groups to Jellyfin as Movies using **symbolic links** — no proxy, no re-encoding, no extra disk space. Each Group becomes a single Movie entry, with its scenes linked as either auto-playing parts or individually selectable sections depending on your preference.

**[→ View plugin README](./StashMovies/README.md)**

---

### [StashProxy](./StashProxy)

![Docker](https://img.shields.io/badge/Docker-required-blue?logo=docker)
![Python](https://img.shields.io/badge/Python-3.12-blue?logo=python)
![License](https://img.shields.io/badge/license-MIT-green)

A companion proxy service for StashSync. Streams all scenes in a Stash Group as one continuous video using FFmpeg — no re-encoding, no extra disk space. Required for multi-scene playback to work correctly in Jellyfin.

**[→ View StashProxy README](./StashProxy/README.md)**

---

## Quick Setup

### StashSync (legacy, proxy-based)

1. Download the DLL from the [Releases](../../releases) page
2. Create a folder in your Jellyfin plugins directory named `StashSync_1.0.0.0`
3. Copy the DLL into that folder
4. Restart Jellyfin
5. Confirm it loaded under **Dashboard → Plugins → My Plugins**

See [StashSync README](./StashSync/README.md) and [StashProxy README](./StashProxy/README.md) for full setup.

---

### StashMovies (symlink-based, no proxy required)

**Option A — Via Jellyfin Plugin Repository (recommended)**

1. In Jellyfin go to **Dashboard → Plugins → Repositories → +**
2. Add this URL:
   ```
   https://lurking987.github.io/jellyfin-plugins/manifest.json
   ```
3. Go to **Catalogue**, find **Stash Movies Sync**, and install.
4. Restart Jellyfin and configure the plugin under **Dashboard → Plugins → Stash Movies Sync → Settings**.

**Option B — Manual**

1. Download `StashMoviesSync.zip` from the [Releases](../../releases) page and extract it.
2. Copy `Jellyfin.Plugin.StashMovies.dll` to your Jellyfin plugins directory (e.g. `/config/plugins/StashMovies/`).
3. Restart Jellyfin.
4. Go to **Dashboard → Plugins → Stash Movies Sync → Settings** and configure your Stash URL and library path.
5. Run the **Sync Stash Movies** scheduled task.

See [StashMovies README](./StashMovies/README.md) for full setup including path translation and sync mode options.

---

## StashSync vs StashMovies

| | StashSync | StashMovies |
|---|---|---|
| Approach | `.strm` files + HTTP proxy | Symbolic links directly to files |
| Requires StashProxy | ✅ Yes | ❌ No |
| Direct play (4K) | Desktop only | ✅ All clients |
| TMDB metadata | ✅ Automatic | Manual / NFO only |
| Setup complexity | Higher | Lower |

---

## License

MIT
