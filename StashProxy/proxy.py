#!/usr/bin/env python3
"""
StashProxy — streams Stash Group scenes as HLS with full seeking support.

Reads chapter-metadata.xml files written by the StashSync Jellyfin plugin,
segments all scenes into HLS .ts files on first request, and serves a
complete VOD m3u8 playlist once all scenes are fully segmented.

Endpoints:
  GET /group/<id>/stream           — redirects to /group/<id>/playlist.m3u8
  GET /group/<id>/playlist.m3u8   — HLS VOD playlist (served after full segmentation)
  GET /group/<id>/segments/<file>  — individual .ts segment files
  GET /health                      — health check
"""

import os
import glob
import subprocess
import threading
import time
import shutil
import xml.etree.ElementTree as ET
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

# ── Configuration ─────────────────────────────────────────────────────────────
STASH_GROUPS_PATH = os.environ.get("STASH_GROUPS_PATH", "/stash-groups")
STASH_API_KEY     = os.environ.get("STASH_API_KEY", "")
PROXY_PORT        = int(os.environ.get("PROXY_PORT", "5678"))
FFMPEG_PATH       = os.environ.get("FFMPEG_PATH", "ffmpeg")
SEGMENT_DIR       = os.environ.get("SEGMENT_DIR", "/tmp/stashproxy-segments")
SEGMENT_DURATION  = int(os.environ.get("SEGMENT_DURATION", "6"))
SEGMENT_TTL       = int(os.environ.get("SEGMENT_TTL", "3600"))
CLEANUP_INTERVAL  = 300
# ─────────────────────────────────────────────────────────────────────────────

# { group_id: { "state": "running"|"done"|"error", "last_access": float } }
_jobs: dict[str, dict] = {}
_jobs_lock = threading.Lock()


class ThreadedHTTPServer(ThreadingMixIn, HTTPServer):
    """Handle each request in its own thread."""
    daemon_threads = True


# ── Helpers ───────────────────────────────────────────────────────────────────

def find_group_folder(group_id: str) -> str | None:
    pattern = os.path.join(STASH_GROUPS_PATH, f"*(StashGroup-{group_id})")
    matches = glob.glob(pattern)
    return matches[0] if matches else None


def parse_chapter_metadata(folder: str) -> list[dict]:
    xml_path = os.path.join(folder, "chapter-metadata.xml")
    if not os.path.exists(xml_path):
        return []
    tree = ET.parse(xml_path)
    root = tree.getroot()
    chapters = []
    for ch in root.findall("chapter"):
        chapters.append({
            "index":       int(ch.attrib["index"]),
            "scene_id":    ch.attrib["scene_id"],
            "title":       ch.attrib.get("title", ""),
            "start_ms":    int(ch.attrib["start_ms"]),
            "duration_ms": int(ch.attrib["duration_ms"]),
            "stream_url":  ch.attrib["stream_url"],
        })
    return sorted(chapters, key=lambda c: c["index"])


def segment_dir_for(group_id: str) -> str:
    return os.path.join(SEGMENT_DIR, f"group_{group_id}")


def add_api_key(url: str) -> str:
    if not STASH_API_KEY:
        return url
    sep = "&" if "?" in url else "?"
    return f"{url}{sep}apikey={STASH_API_KEY}"


def playlist_path_for(group_id: str) -> str:
    return os.path.join(segment_dir_for(group_id), "playlist.m3u8")


# ── Segmentation ──────────────────────────────────────────────────────────────

def ensure_segmented(group_id: str, chapters: list[dict]):
    """
    Start segmentation if not already running or done.
    Returns immediately — caller must wait on playlist_path_for() to appear.
    """
    with _jobs_lock:
        state = _jobs.get(group_id, {}).get("state")
        if state in ("running", "done"):
            _jobs[group_id]["last_access"] = time.time()
            return
        _jobs[group_id] = {"state": "running", "last_access": time.time()}

    thread = threading.Thread(
        target=_segmentation_worker,
        args=(group_id, chapters),
        daemon=True,
    )
    thread.start()


def _segmentation_worker(group_id: str, chapters: list[dict]):
    out_dir = segment_dir_for(group_id)
    os.makedirs(out_dir, exist_ok=True)

    segment_index = 0
    all_segments = []

    print(f"[StashProxy] Starting segmentation for group {group_id} "
          f"({len(chapters)} scenes)", flush=True)

    for i, chapter in enumerate(chapters):
        scene_id = chapter["scene_id"]
        offset_s = chapter["start_ms"] / 1000.0
        stream_url = add_api_key(chapter["stream_url"])
        scene_playlist = os.path.join(out_dir, f"scene_{i}.m3u8")

        cmd = [
            FFMPEG_PATH, "-y",
            "-i", stream_url,
            "-c", "copy",
            "-output_ts_offset", str(offset_s),
            "-f", "hls",
            "-hls_time", str(SEGMENT_DURATION),
            "-hls_list_size", "0",
            "-hls_flags", "independent_segments",
            "-hls_segment_type", "mpegts",
            "-hls_segment_filename", os.path.join(out_dir, f"seg_%04d.ts"),
            "-start_number", str(segment_index),
            scene_playlist,
        ]

        print(f"[StashProxy] Segmenting scene {i+1}/{len(chapters)} "
              f"(id={scene_id})", flush=True)

        try:
            proc = subprocess.run(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        except Exception as e:
            print(f"[StashProxy] FFmpeg error on scene {i+1}: {e}", flush=True)
            with _jobs_lock:
                _jobs[group_id]["state"] = "error"
            return

        if proc.returncode != 0:
            print(f"[StashProxy] FFmpeg exited {proc.returncode} on scene {i+1}",
                  flush=True)
            with _jobs_lock:
                _jobs[group_id]["state"] = "error"
            return

        scene_segs = _parse_scene_playlist(scene_playlist)
        all_segments.extend(scene_segs)
        segment_index += len(scene_segs)

        print(f"[StashProxy] Scene {i+1} done — "
              f"{len(scene_segs)} segments, total {segment_index}", flush=True)

    # All scenes done — write the single complete VOD playlist
    _write_vod_playlist(group_id, all_segments)

    with _jobs_lock:
        _jobs[group_id]["state"] = "done"

    print(f"[StashProxy] Segmentation complete for group {group_id} — "
          f"{len(all_segments)} total segments", flush=True)


def _parse_scene_playlist(playlist_path: str) -> list[tuple[str, float]]:
    segments = []
    if not os.path.exists(playlist_path):
        return segments
    with open(playlist_path) as f:
        lines = f.readlines()
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        if line.startswith("#EXTINF:"):
            duration = float(line.split(":")[1].rstrip(","))
            i += 1
            if i < len(lines):
                filename = os.path.basename(lines[i].strip())
                segments.append((filename, duration))
        i += 1
    return segments


def _probe_segment(seg_path: str) -> dict:
    """Use ffprobe to get codec, resolution and bitrate from the first segment."""
    try:
        import json
        result = subprocess.run(
            [
                "ffprobe", "-v", "quiet",
                "-print_format", "json",
                "-show_streams", "-show_format",
                seg_path,
            ],
            capture_output=True, text=True, timeout=15,
        )
        data = json.loads(result.stdout)
        info = {"bandwidth": 15000000, "resolution": "3840x2160", "codecs": "avc1.640028,mp4a.40.2"}
        for stream in data.get("streams", []):
            if stream.get("codec_type") == "video":
                w = stream.get("coded_width") or stream.get("width", 3840)
                h = stream.get("coded_height") or stream.get("height", 2160)
                info["resolution"] = f"{w}x{h}"
                # Parse codec string
                profile = stream.get("profile", "High")
                level = stream.get("level", 52)
                profile_map = {"High": "64", "Main": "4D", "Baseline": "42"}
                p = profile_map.get(profile, "64")
                info["codecs"] = f"avc1.{p}00{level:02x},mp4a.40.2"
        fmt = data.get("format", {})
        br = int(fmt.get("bit_rate", 0))
        if br > 0:
            # Cap at 80Mbps — container bitrate can be inflated vs actual video bitrate
            info["bandwidth"] = min(br, 80_000_000)
        return info
    except Exception:
        return {"bandwidth": 15000000, "resolution": "3840x2160", "codecs": "avc1.640034,mp4a.40.2"}


def _write_vod_playlist(group_id: str, segments: list[tuple[str, float]]):
    """
    Write a two-level HLS structure:
      playlist.m3u8 — master playlist with bandwidth/resolution/codec hints
      media.m3u8    — media playlist with all segments
    """
    out_dir = segment_dir_for(group_id)
    media_path = os.path.join(out_dir, "media.m3u8")
    master_path = playlist_path_for(group_id)

    max_duration = max((d for _, d in segments), default=SEGMENT_DURATION)
    target_duration = int(max_duration) + 1

    # Write media playlist
    media_lines = [
        "#EXTM3U",
        "#EXT-X-VERSION:3",
        f"#EXT-X-TARGETDURATION:{target_duration}",
        "#EXT-X-PLAYLIST-TYPE:VOD",
        "#EXT-X-INDEPENDENT-SEGMENTS",
    ]
    for filename, duration in segments:
        media_lines.append(f"#EXTINF:{duration:.6f},")
        media_lines.append(f"/group/{group_id}/segments/{filename}")
    media_lines.append("#EXT-X-ENDLIST")

    tmp_media = media_path + ".tmp"
    with open(tmp_media, "w") as f:
        f.write("\n".join(media_lines) + "\n")
    os.replace(tmp_media, media_path)

    # Probe first segment for real stream info
    first_seg = os.path.join(out_dir, segments[0][0]) if segments else None
    info = _probe_segment(first_seg) if first_seg and os.path.exists(first_seg) else \
        {"bandwidth": 15000000, "resolution": "3840x2160", "codecs": "avc1.640034,mp4a.40.2"}

    # Write master playlist
    master_lines = [
        "#EXTM3U",
        "#EXT-X-VERSION:3",
        "#EXT-X-INDEPENDENT-SEGMENTS",
        f"#EXT-X-STREAM-INF:BANDWIDTH={info['bandwidth']},"
        f"RESOLUTION={info['resolution']},"
        f"CODECS=\"{info['codecs']}\"",
        f"/group/{group_id}/media.m3u8",
    ]

    tmp_master = master_path + ".tmp"
    with open(tmp_master, "w") as f:
        f.write("\n".join(master_lines) + "\n")
    os.replace(tmp_master, master_path)


# ── Cleanup ───────────────────────────────────────────────────────────────────

def _cleanup_worker():
    while True:
        time.sleep(CLEANUP_INTERVAL)
        now = time.time()
        with _jobs_lock:
            stale = [
                gid for gid, info in _jobs.items()
                if now - info["last_access"] > SEGMENT_TTL
                and info["state"] != "running"
            ]
        for group_id in stale:
            out_dir = segment_dir_for(group_id)
            if os.path.exists(out_dir):
                shutil.rmtree(out_dir, ignore_errors=True)
                print(f"[StashProxy] Cleaned up segments for group {group_id}",
                      flush=True)
            with _jobs_lock:
                _jobs.pop(group_id, None)


# ── HTTP Handler ──────────────────────────────────────────────────────────────

class ProxyHandler(BaseHTTPRequestHandler):

    def log_message(self, format, *args):
        print(f"[StashProxy] {self.address_string()} - {format % args}", flush=True)

    def do_GET(self):
        path = self.path.split("?")[0].strip("/")
        parts = path.split("/")

        if path == "health":
            self._respond(200, "text/plain", b"OK")
            return

        if len(parts) >= 3 and parts[0] == "group":
            group_id = parts[1]

            if parts[2] == "stream":
                self.send_response(302)
                self.send_header("Location", f"/group/{group_id}/playlist.m3u8")
                self.end_headers()
                return

            if parts[2] == "playlist.m3u8":
                self.handle_playlist(group_id)
                return

            if parts[2] == "media.m3u8":
                self.handle_media_playlist(group_id)
                return

            if len(parts) == 4 and parts[2] == "segments":
                self.handle_segment(group_id, parts[3])
                return

        self._respond(404, "text/plain", b"Not found")

    def handle_playlist(self, group_id: str):
        with _jobs_lock:
            if group_id in _jobs:
                _jobs[group_id]["last_access"] = time.time()

        playlist = playlist_path_for(group_id)

        # Kick off segmentation if needed
        if not os.path.exists(playlist):
            folder = find_group_folder(group_id)
            if not folder:
                self._respond(404, "text/plain",
                              f"Group {group_id} not found".encode())
                return

            chapters = parse_chapter_metadata(folder)
            if not chapters:
                self._respond(404, "text/plain", b"No chapters found")
                return

            print(f"[StashProxy] First request for group {group_id} — "
                  f"starting segmentation", flush=True)
            ensure_segmented(group_id, chapters)

            # Wait for complete VOD playlist — no partial playlists served
            print(f"[StashProxy] Waiting for full segmentation of group "
                  f"{group_id}...", flush=True)
            while not os.path.exists(playlist):
                # Check for error
                with _jobs_lock:
                    state = _jobs.get(group_id, {}).get("state")
                if state == "error":
                    self._respond(500, "text/plain", b"Segmentation failed")
                    return
                time.sleep(1)

            print(f"[StashProxy] Segmentation ready, serving playlist for "
                  f"group {group_id}", flush=True)

        with open(playlist, "rb") as f:
            data = f.read()

        self.send_response(200)
        self.send_header("Content-Type", "application/vnd.apple.mpegurl")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        try:
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def handle_media_playlist(self, group_id: str):
        with _jobs_lock:
            if group_id in _jobs:
                _jobs[group_id]["last_access"] = time.time()

        media_path = os.path.join(segment_dir_for(group_id), "media.m3u8")

        if not os.path.exists(media_path):
            self._respond(404, "text/plain", b"Media playlist not ready")
            return

        try:
            with open(media_path, "rb") as f:
                data = f.read()
            self.send_response(200)
            self.send_header("Content-Type", "application/vnd.apple.mpegurl")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-cache")
            self.end_headers()
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def handle_segment(self, group_id: str, filename: str):
        with _jobs_lock:
            if group_id in _jobs:
                _jobs[group_id]["last_access"] = time.time()

        if ".." in filename or "/" in filename:
            self._respond(400, "text/plain", b"Bad request")
            return

        seg_path = os.path.join(segment_dir_for(group_id), filename)

        if not os.path.exists(seg_path):
            self._respond(404, "text/plain", b"Segment not found")
            return

        try:
            with open(seg_path, "rb") as f:
                data = f.read()

            self.send_response(200)
            self.send_header("Content-Type", "video/mp2t")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "max-age=3600")
            self.end_headers()
            self.wfile.write(data)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _respond(self, code: int, content_type: str, body: bytes):
        try:
            self.send_response(code)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    os.makedirs(SEGMENT_DIR, exist_ok=True)

    print(f"[StashProxy] Starting on port {PROXY_PORT}", flush=True)
    print(f"[StashProxy] Stash groups path: {STASH_GROUPS_PATH}", flush=True)
    print(f"[StashProxy] Segment dir: {SEGMENT_DIR}", flush=True)
    print(f"[StashProxy] Segment duration: {SEGMENT_DURATION}s", flush=True)
    print(f"[StashProxy] Segment TTL: {SEGMENT_TTL}s", flush=True)
    print(f"[StashProxy] FFmpeg: {FFMPEG_PATH}", flush=True)
    print(f"[StashProxy] API key: {'set' if STASH_API_KEY else 'not set'}", flush=True)

    cleanup_thread = threading.Thread(target=_cleanup_worker, daemon=True)
    cleanup_thread.start()

    server = ThreadedHTTPServer(("0.0.0.0", PROXY_PORT), ProxyHandler)
    print(f"[StashProxy] Listening on http://0.0.0.0:{PROXY_PORT}", flush=True)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("[StashProxy] Shutting down")
        server.shutdown()


if __name__ == "__main__":
    main()
