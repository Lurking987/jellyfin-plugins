# StashProxy

A companion proxy service for the [StashSync](../StashSync) Jellyfin plugin. Segments all scenes in a Stash Group into a complete HLS VOD stream, enabling full random-access seeking on all Jellyfin clients.

---

## How It Works

```
Jellyfin → GET /group/42/stream
              ↓ (302 redirect)
         GET /group/42/playlist.m3u8
              ↓
         StashProxy reads chapter-metadata.xml
         Spawns FFmpeg per scene → writes .ts segments to /tmp
         Serves a complete HLS VOD master playlist
              ↓
         Jellyfin fetches segments on demand
         Full seeking across the entire movie
```

- Each scene is segmented via FFmpeg stream copy (no re-encoding, lossless)
- Segments are written to a temp directory inside the container
- A complete VOD playlist is served only after all scenes are fully segmented
- Full random-access seeking works on all clients including desktop and browser
- Segments are cleaned up automatically after 1 hour of inactivity
- Each request is handled in its own thread — multiple clients supported simultaneously

---

## Quick Start (Docker Compose)

No need to clone the repo. Create a `docker-compose.yml` file anywhere on your server:

```yaml
version: "3.8"

services:
  stashproxy:
    image: lurking987/stashproxy:latest
    container_name: stashproxy
    restart: unless-stopped
    ports:
      - "5678:5678"
    volumes:
      # Change this to the path of your stash-groups folder on the host
      - /your/path/to/stash-groups:/stash-groups:ro
    environment:
      - STASH_GROUPS_PATH=/stash-groups
      - STASH_API_KEY=        # Set this if Stash requires authentication
      - PROXY_PORT=5678
      - SEGMENT_DURATION=6   # seconds per HLS segment
      - SEGMENT_TTL=3600     # seconds before unused segments are cleaned up
```

Then start it:

```bash
docker-compose up -d
```

Verify it's running:

```bash
curl http://localhost:5678/health
# should return: OK
```

No build step required — Docker pulls the pre-built image from Docker Hub automatically.

---

## Connecting to Jellyfin

In Jellyfin → **Dashboard → Plugins → StashSync → Settings**, set the **Proxy URL** to:

```
http://<your-server-ip>:5678
```

Then re-run the StashSync task. The `.strm` files will point at the proxy's HLS playlist endpoint.

---

## TrueNAS Scale Setup

1. Go to **Apps → Discover Apps → Custom App**
2. Set the image to `lurking987/stashproxy:latest`
3. Set the port mapping: `5678 → 5678`
4. Set the volume mount: your stash-groups path on the host → `/stash-groups` (read-only)
5. Set environment variables as needed (see table below)
6. Deploy the app

---

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `STASH_GROUPS_PATH` | `/stash-groups` | Path to the stash-groups folder inside the container |
| `STASH_API_KEY` | *(empty)* | Stash API key, if authentication is enabled |
| `PROXY_PORT` | `5678` | Port the proxy listens on |
| `FFMPEG_PATH` | `ffmpeg` | Path to FFmpeg binary |
| `SEGMENT_DIR` | `/tmp/stashproxy-segments` | Where HLS segments are written inside the container |
| `SEGMENT_DURATION` | `6` | Target duration in seconds per `.ts` segment |
| `SEGMENT_TTL` | `3600` | Seconds of inactivity before segments are deleted |

---

## Endpoints

| Endpoint | Description |
|---|---|
| `GET /group/<id>/stream` | Redirects to `/group/<id>/playlist.m3u8` |
| `GET /group/<id>/playlist.m3u8` | HLS master playlist — triggers segmentation on first request |
| `GET /group/<id>/media.m3u8` | HLS media playlist with all segments listed |
| `GET /group/<id>/segments/<file>` | Individual `.ts` segment files |
| `GET /health` | Health check — returns `OK` |

---

## Segment Cache

Segments are stored in `/tmp/stashproxy-segments` inside the container by default. This is ephemeral — segments are wiped when the container restarts. This is fine for most use cases since re-segmentation happens automatically on the next play request.

To persist segments across restarts, add a volume mount:

```yaml
volumes:
  - /your/path/to/stash-groups:/stash-groups:ro
  - /your/path/to/cache:/tmp/stashproxy-segments
```

Note that 4K content at ~15Mbps produces roughly 10-15GB of segments per 2-hour movie.

---

## Seeking & Client Compatibility

| Client | Seeking | Quality |
|---|---|---|
| Jellyfin Desktop | Full random-access | Direct play — original 4K quality |
| Jellyfin Mobile | Full random-access | Direct play or hardware transcode |
| Web browser (Edge/Chrome/Firefox) | Full random-access | Transcoded — browsers can't direct play MPEG-TS |

Browser clients require Jellyfin to transcode from MPEG-TS to fMP4 HLS. Seeking still works fully, but quality depends on Jellyfin's transcode settings. The desktop and mobile apps direct play at full original quality.

---

## First Play Delay

On the first play of a movie, StashProxy must segment all scenes before serving the playlist. For a 2-hour 4K movie this typically takes 2-5 minutes depending on your server. Jellyfin will show a loading spinner during this time.

Subsequent plays of the same movie are instant since segments are cached.

---

## Troubleshooting

**Proxy returns 404 for a group**
- Make sure the stash-groups volume is mounted to `/stash-groups` inside the container
- Confirm `chapter-metadata.xml` exists in the group folder
- Re-run the StashSync sync task to regenerate chapter files

**Movie loads but quality is poor in the browser**
- This is expected — browsers can't direct play MPEG-TS and require transcoding
- Use the Jellyfin desktop or mobile app for full 4K direct play

**Jellyfin shows a loading spinner for a long time on first play**
- Normal — segmentation is in progress. Check proxy logs for progress:
  ```bash
  docker logs stashproxy -f
  ```

**Proxy crashes or segments aren't found**
- Pull the latest image and redeploy:
  ```bash
  docker pull lurking987/stashproxy:latest
  docker-compose up -d --force-recreate
  ```

**Container won't start**
- Check logs: `docker-compose logs stashproxy`
- Verify the stash-groups path exists and is readable

---

## License

MIT
