#!/usr/bin/env python3
"""
StashProxy — streams Stash Group scenes as a single continuous video.

Reads chapter-metadata.xml files written by the StashSync Jellyfin plugin,
then remuxes each scene from MP4→MPEG-TS via FFmpeg sequentially with
continuous timestamps, piping all output to the client as one stream.

Endpoints:
  GET /group/<group_id>/stream   — stream all scenes in order
  GET /health                    — health check
"""

import os
import glob
import subprocess
import xml.etree.ElementTree as ET
from http.server import BaseHTTPRequestHandler, HTTPServer

# ── Configuration ─────────────────────────────────────────────────────────────
STASH_GROUPS_PATH = os.environ.get("STASH_GROUPS_PATH", "/stash-groups")
STASH_API_KEY     = os.environ.get("STASH_API_KEY", "")
PROXY_PORT        = int(os.environ.get("PROXY_PORT", "5678"))
FFMPEG_PATH       = os.environ.get("FFMPEG_PATH", "ffmpeg")
CHUNK_SIZE        = 65536  # 64KB
# ─────────────────────────────────────────────────────────────────────────────


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


def build_scene_command(stream_url: str, timestamp_offset_ms: int) -> list[str]:
    """
    Remux a single MP4 HTTP stream to MPEG-TS with a timestamp offset
    so scenes chain together with continuous timestamps.
    """
    if STASH_API_KEY:
        sep = "&" if "?" in stream_url else "?"
        stream_url = f"{stream_url}{sep}apikey={STASH_API_KEY}"

    # Convert ms offset to seconds for FFmpeg
    offset_s = timestamp_offset_ms / 1000.0

    return [
        FFMPEG_PATH, "-y",
        "-i", stream_url,
        "-c", "copy",
        "-output_ts_offset", str(offset_s),  # shift all timestamps by offset
        "-f", "mpegts",
        "pipe:1",
    ]


class ProxyHandler(BaseHTTPRequestHandler):

    def log_message(self, format, *args):
        print(f"[StashProxy] {self.address_string()} - {format % args}", flush=True)

    def do_GET(self):
        if self.path == "/health":
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"OK")
            return

        parts = self.path.strip("/").split("/")
        if len(parts) == 3 and parts[0] == "group" and parts[2] == "stream":
            self.handle_group_stream(parts[1])
            return

        self.send_response(404)
        self.end_headers()
        self.wfile.write(b"Not found")

    def handle_group_stream(self, group_id: str):
        folder = find_group_folder(group_id)
        if not folder:
            print(f"[StashProxy] Group {group_id} not found in {STASH_GROUPS_PATH}", flush=True)
            self.send_response(404)
            self.end_headers()
            self.wfile.write(f"Group {group_id} not found".encode())
            return

        chapters = parse_chapter_metadata(folder)
        if not chapters:
            print(f"[StashProxy] No chapters found for group {group_id}", flush=True)
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b"No chapters found")
            return

        total_duration_s = sum(ch["duration_ms"] for ch in chapters) / 1000.0
        print(f"[StashProxy] Streaming group {group_id} — {len(chapters)} scene(s), "
              f"total duration {total_duration_s:.1f}s", flush=True)

        self.send_response(200)
        self.send_header("Content-Type", "video/mp2t")
        self.send_header("X-Content-Duration", str(total_duration_s))
        self.end_headers()

        # Stream each scene with continuous timestamps
        for i, chapter in enumerate(chapters):
            scene_id = chapter["scene_id"]
            stream_url = chapter["stream_url"]
            offset_ms = chapter["start_ms"]  # already cumulative from plugin
            cmd = build_scene_command(stream_url, offset_ms)
            print(f"[StashProxy] Scene {i+1}/{len(chapters)} (id={scene_id}, "
                  f"offset={offset_ms}ms)", flush=True)

            try:
                process = subprocess.Popen(
                    cmd,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.DEVNULL,
                )
            except FileNotFoundError:
                print(f"[StashProxy] FFmpeg not found at '{FFMPEG_PATH}'", flush=True)
                return

            try:
                while True:
                    chunk = process.stdout.read(CHUNK_SIZE)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                process.wait()
                print(f"[StashProxy] Scene {i+1} done (exit {process.returncode})", flush=True)
            except (BrokenPipeError, ConnectionResetError):
                print(f"[StashProxy] Client disconnected during scene {i+1} of group {group_id}", flush=True)
                process.kill()
                return
            except Exception as e:
                print(f"[StashProxy] Error during scene {i+1} of group {group_id}: {e}", flush=True)
                process.kill()
                return

        print(f"[StashProxy] Finished streaming group {group_id}", flush=True)


def main():
    print(f"[StashProxy] Starting on port {PROXY_PORT}", flush=True)
    print(f"[StashProxy] Stash groups path: {STASH_GROUPS_PATH}", flush=True)
    print(f"[StashProxy] FFmpeg: {FFMPEG_PATH}", flush=True)
    print(f"[StashProxy] API key: {'set' if STASH_API_KEY else 'not set'}", flush=True)

    server = HTTPServer(("0.0.0.0", PROXY_PORT), ProxyHandler)
    print(f"[StashProxy] Listening on http://0.0.0.0:{PROXY_PORT}", flush=True)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("[StashProxy] Shutting down")
        server.shutdown()


if __name__ == "__main__":
    main()
