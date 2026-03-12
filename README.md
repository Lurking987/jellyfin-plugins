# Jellyfin Plugins

A collection of custom plugins for [Jellyfin](https://jellyfin.org) media server.

---

## Plugins

### [StashSync](./StashSync)

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.9.0+-blue?logo=jellyfin)
![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green)

Syncs [Stash App](https://stashapp.cc) Groups to Jellyfin as Movies. Each Group becomes a Movie entry with its scenes mapped as chapter markers. Supports automatic TheMovieDB metadata and image fetching.

**[→ View plugin README](./StashSync/README.md)**

---

## Installation

Each plugin is installed manually as a DLL. General steps:

1. Download the DLL from the [Releases](../../releases) page
2. Create a folder in your Jellyfin plugins directory named `PluginName_1.0.0.0`
3. Copy the DLL into that folder
4. Restart Jellyfin
5. Confirm it loaded under **Dashboard → Plugins → My Plugins**

See each plugin's individual README for specific setup instructions.

---

## License

MIT

