# StashProxy

A lightweight proxy server that streams Stash App Group scenes as a single continuous video to Jellyfin, with no re-encoding and no extra disk space required.

Works alongside the [StashSync](../StashSync) Jellyfin plugin. StashSync writes the `chapter-metadata.xml` files that StashProxy reads to know which scenes to stream and in what order.

---

## How It Works

```
Jellyfin → GET /group/42/stream
              ↓
         StashProxy reads chapter-metadata.xml
              ↓
         FFmpeg fetches scene streams from Stash
         and concatenates them on the fly
              ↓
         Single continuous video piped back to Jellyfin
```

- No files written to disk
- No re-encoding — FFmpeg stream-copies (fast, lossless)
- Scenes play sequentially as one continuous video
- Works with chapter markers already in the NFO

---

## Requirements

- Docker and Docker Compose
- FFmpeg (included in the Docker image)
- StashSync plugin installed and synced at least once

---

## Setup

### 1. Update docker-compose.yml

Edit `docker-compose.yml` and set the volume path to match your stash-groups folder:

```yaml
volumes:
  - /your/actual/path/to/stash-groups:/stash-groups:ro
```

If Stash requires an API key, set it:
```yaml
environment:
  - STASH_API_KEY=your_api_key_here
```

### 2. Start the container

```bash
cd StashProxy
docker-compose up -d
```

Docker will automatically pull the pre-built image from Docker Hub — no build step required.

Verify it's running:
```bash
curl http://localhost:5678/health
# should return: OK
```

### 3. Update StashSync plugin settings

In Jellyfin → Dashboard → Plugins → StashSync → Settings, set the **Proxy URL** to:
```
http://<your-server-ip>:5678
```

Re-run the sync task. The `.strm` files will now point to the proxy instead of Stash directly.

---

## TrueNAS Scale Setup

1. Go to **Apps → Discover Apps → Custom App**
2. Set the image to `lurking987/stashproxy:latest`
3. Set the port mapping: `5678 → 5678`
4. Set the volume mount: your stash-groups path → `/stash-groups` (read-only)
5. Set environment variables as needed (see table above)

Alternatively, run it via SSH:
```bash
cd /path/to/StashProxy
docker-compose up -d
```

---

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `STASH_GROUPS_PATH` | `/stash-groups` | Path to the stash-groups folder inside the container |
| `STASH_API_KEY` | *(empty)* | Stash API key, if authentication is enabled |
| `PROXY_PORT` | `5678` | Port the proxy listens on |
| `FFMPEG_PATH` | `ffmpeg` | Path to FFmpeg binary |

---

## Stream URL Format

```
http://<proxy-host>:5678/group/<group_id>/stream
```

Example:
```
http://192.168.0.38:5678/group/42/stream
```

The `group_id` matches the Stash Group ID, which is embedded in the folder name by StashSync (e.g. `My Movie (StashGroup-42)`).

---

## Troubleshooting

**Proxy returns 404 for a group**
- Make sure the stash-groups volume is mounted correctly
- Confirm the `chapter-metadata.xml` file exists in the group folder
- Re-run the StashSync sync task to regenerate chapter files

**Video plays but cuts off early**
- Check that FFmpeg can reach the Stash stream URLs from inside the container
- If Stash requires auth, make sure `STASH_API_KEY` is set

**Container won't start**
- Check logs: `docker-compose logs stashproxy`
- Verify the stash-groups path exists and is readable

---

## License

MIT
