# Jellyfin StashMovies Sync Plugin

A Jellyfin plugin that automatically syncs movie groups from [Stash](https://stashapp.cc) into your Jellyfin library using symbolic links — no copying or moving files required.

---

## How It Works

Stash organises related scenes into **Groups**. This plugin treats each Group as a single Movie in Jellyfin:

1. Queries your Stash instance for all Groups and their associated scenes via the GraphQL API.
2. Creates a dedicated folder for each Group inside your configured library path.
3. Writes a `movie.nfo` file so Jellyfin picks up the group's title and synopsis.
4. Creates a symbolic link for each scene file inside that folder, named according to your chosen **Sync Mode**.
5. Runs on a configurable schedule (default: every 24 hours) and overwrites any broken links automatically.

```
/your-library-path/
└── My Movie Group/
    ├── movie.nfo
    ├── My Movie Group - pt1.mkv  ← symlink → actual file on disk
    └── My Movie Group - pt2.mkv  ← symlink → actual file on disk
```

---

## Requirements

- Jellyfin 10.11 or later
- .NET 9
- A running [Stash](https://stashapp.cc) instance with the GraphQL API accessible from your Jellyfin server
- Jellyfin must have read/write access to the symlink output directory
- The Jellyfin process must have permission to create symbolic links on your OS

---

## Installation

### Manual

1. Download the latest `StashMoviesSync.zip` from the [Releases](../../releases) page and extract it.
2. Copy `Jellyfin.Plugin.StashMovies.dll` into your Jellyfin plugins directory (e.g. `/config/plugins/StashMovies/`).
3. Restart Jellyfin.

### Via Jellyfin Plugin Repository

1. In Jellyfin go to **Dashboard → Plugins → Repositories → +**
2. Add this URL:
   ```
   https://lurking987.github.io/jellyfin-plugins/manifest.json
   ```
3. Go to **Catalogue**, find **Stash Movies Sync**, and install.

### Build from source

```bash
git clone https://github.com/Lurking987/jellyfin-plugins.git
cd jellyfin-plugins
dotnet build StashMovies/Jellyfin_Plugin_StashMovies.csproj --configuration Release
```

Copy the output `.dll` from `bin/Release/net9.0/` to your Jellyfin plugins directory and restart Jellyfin.

---

## Configuration

Navigate to **Dashboard → Plugins → Stash Movies Sync → Settings**.

| Setting | Description |
|---|---|
| **Stash GraphQL URL** | Full URL to your Stash GraphQL endpoint, e.g. `http://192.168.1.x:9999/graphql` |
| **Stash Movies Library Path** | Absolute path where symlink folders will be created, e.g. `/mnt/jellyfin/stash-movies` |
| **Minimum Scene Count** | Groups with fewer scenes than this number are skipped entirely. Set to `0` to include all groups. |
| **Sync Mode** | Controls how scene files are named inside each movie folder (see below). |

### Sync Mode

Both modes create **one folder and one `movie.nfo` per Group**. The only difference is how the individual scene symlinks are named.

| Mode | File naming | Jellyfin behaviour |
|---|---|---|
| **Parts** | `Movie Name - pt1.mkv`, `Movie Name - pt2.mkv` … | Jellyfin recognises the `pt#` suffix and plays all parts back-to-back automatically |
| **Scenes** | `Movie Name - Scene Title.mkv` … | Jellyfin lists each scene as an individually selectable section; playback is not automatic |

---

## Path Translation

If your Stash and Jellyfin instances run in separate containers or mount paths differently, you can configure path translation directly in the plugin settings.

| Setting | Example | Description |
|---|---|---|
| **Stash Path Prefix** | `/data/Studios` | The path prefix as Stash sees the files |
| **Jellyfin Path Prefix** | `/mnt/stsh` | The same location as Jellyfin sees it |

Leave both fields blank if Stash and Jellyfin share identical mount paths — no translation will be applied.

---

## Scheduled Task

The sync runs automatically every **24 hours**. You can also trigger it manually at any time from **Dashboard → Scheduled Tasks → Sync Stash Movies**.

---

## Project Structure

```
jellyfin-plugins/
├── .github/
│   └── workflows/
│       └── release.yml               # Auto-build and publish on version tags
├── StashMovies/
│   ├── Configuration/
│   │   └── config.html               # Plugin settings page
│   ├── Jellyfin_Plugin_StashMovies.csproj
│   ├── Plugin.cs                     # Plugin entry point & web page registration
│   ├── PluginConfiguration.cs        # Settings model
│   └── ScheduledTask.cs              # Sync logic, GraphQL client, symlink creation
├── manifest.json                     # Jellyfin plugin repository manifest
└── README.md
```

1. Push a version tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
2. GitHub Actions will build the `.dll`, zip it, and attach it to a GitHub Release automatically.
3. Update `manifest.json` with the new version entry and the MD5 checksum printed in the release notes.

---

## Releasing a New Version

1. Push a version tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
2. GitHub Actions will build the `.dll`, zip it, and attach it to a GitHub Release automatically.
3. Update `manifest.json` with the new version entry and the MD5 checksum printed in the release notes.

---

## Contributing

Pull requests are welcome. Please open an issue first to discuss any significant changes.

---

## License

This project is provided as-is with no warranty. See [LICENSE](LICENSE) for details.
