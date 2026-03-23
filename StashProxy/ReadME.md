# StashProxy

A companion proxy service for the [StashSync](../StashSync) Jellyfin plugin. Streams all scenes in a Stash Group as a single continuous video to Jellyfin, with no re-encoding and no extra disk space required.

---

## How It Works

```
Jellyfin → GET /group/42/stream
              ↓
         StashProxy reads chapter-metadata.xml
              ↓
         For each scene, spawns FFmpeg to remux MP4→MPEG-TS
         Pipes output directly to Jellyfin as one continuous stream
              ↓
         Single continuous video with all scenes chained together
```

- No files written to disk
- No re-encoding — FFmpeg stream-copies each scene (fast, lossless)
- All scenes play as one continuous video
- Each scene gets its own FFmpeg process so output starts immediately
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

Then re-run the StashSync task. The `.strm` files will be updated to point at the proxy.

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

---

## Endpoints

| Endpoint | Description |
|---|---|
| `GET /group/<id>/stream` | Streams all scenes as continuous MPEG-TS |
| `GET /health` | Health check — returns `OK` |

---

## Seeking

Seeking works within the buffered portion of the video. As Jellyfin buffers more of the stream, the seekable range grows. The stream is a live pipe so you cannot seek ahead of what has already been buffered. The web browser client tends to buffer more aggressively than the desktop app, so seeking range will vary by client.

---

## Client Compatibility

| Client | Playback |
|---|---|
| Jellyfin Desktop | Direct play — full 4K quality |
| Jellyfin Web (browser) | Transcoded — browsers require fMP4, not MPEG-TS |
| Jellyfin Android TV | Not currently supported |

---

## Troubleshooting

**Proxy returns 404 for a group**
- Make sure the stash-groups volume is mounted correctly to `/stash-groups` inside the container
- Confirm `chapter-metadata.xml` exists in the group folder
- Re-run the StashSync sync task to regenerate chapter files

**Video plays but stops after the first scene**
- Check proxy logs: `docker logs stashproxy --tail 50`
- Confirm FFmpeg can reach the Stash stream URLs from inside the container
- If Stash requires auth, make sure `STASH_API_KEY` is set

**Jellyfin reports "invalid data" or FFmpeg exits with code 183**
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
