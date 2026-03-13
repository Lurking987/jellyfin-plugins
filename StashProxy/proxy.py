#!/usr/bin/env python3
"""
StashProxy — streams Stash Group scenes as a single continuous video.

Reads chapter-metadata.xml files written by the StashSync Jellyfin plugin,
then uses FFmpeg to concatenate the scene streams on the fly.

Endpoints:
  GET /group/<group_id>/stream   — stream all scenes in order
  GET /health                    — health check
"""

import os
import glob
import subprocess
import threading
import xml.etree.ElementTree as ET
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.request import Request, urlopen
from urllib.error import URLError

# ── Configuration (overridden by environment variables) ──────────────────────
STASH_GROUPS_PATH = os.environ.get("STASH_GROUPS_PATH", "/stash-groups")
STASH_API_KEY     = os.environ.get("STASH_API_KEY", "")
PROXY_PORT        = int(os.environ.get("PROXY_PORT", "5678"))
FFMPEG_PATH       = os.environ.get("FFMPEG_PATH", "ffmpeg")
# ─────────────────────────────────────────────────────────────────────────────


def find_group_folder(group_id: str) -> str | None:
    """Find the stash-groups subfolder for a given group ID."""
    pattern = os.path.join(STASH_GROUPS_PATH, f"*(StashGroup-{group_id})")
    matches = glob.glob(pattern)
    return matches[0] if matches else None


def parse_chapter_metadata(folder: str) -> list[dict]:
    """
    Parse chapter-metadata.xml and return chapters sorted by index.
    Each chapter dict has: index, scene_id, title, start_ms, duration_ms, stream_url
    """
    xml_path = os.path.join(folder, "chapter-metadata.xml")
    if not os.path.exists(xml_path):
        return []

    tree = ET.parse(xml_path)
    root = tree.getroot()

    chapters = []
    for ch in root.findall("chapter"):
        chapters.append({
            "index":      int(ch.attrib["index"]),
            "scene_id":   ch.attrib["scene_id"],
            "title":      ch.attrib.get("title", ""),
            "start_ms":   int(ch.attrib["start_ms"]),
            "duration_ms":int(ch.attrib["duration_ms"]),
            "stream_url": ch.attrib["stream_url"],
        })

    return sorted(chapters, key=lambda c: c["index"])


def build_ffmpeg_concat_command(stream_urls: list[str]) -> list[str]:
    """
    Build an FFmpeg command that concatenates multiple HTTP stream URLs
    and outputs to stdout as MKV (copy, no re-encoding).
    """
    cmd = [FFMPEG_PATH, "-y"]

    # Add each stream URL as an input
    for url in stream_urls:
        if STASH_API_KEY:
            separator = "&" if "?" in url else "?"
            url = f"{url}{separator}apikey={STASH_API_KEY}"
        cmd += ["-i", url]

    # Build the filter_complex concat string
    n = len(stream_urls)
    # Use concat filter: [0:v][0:a][1:v][1:a]...concat=n=N:v=1:a=1
    inputs = "".join(f"[{i}:v][{i}:a]" for i in range(n))
    filter_complex = f"{inputs}concat=n={n}:v=1:a=1[outv][outa]"

    cmd += [
        "-filter_complex", filter_complex,
        "-map", "[outv]",
        "-map", "[outa]",
        "-c:v", "copy",
        "-c:a", "copy",
        "-f", "mpegts",     # MPEG-TS — designed for streaming, no seeking required
        "pipe:1",           # output to stdout
    ]

    return cmd


class ProxyHandler(BaseHTTPRequestHandler):

    def log_message(self, format, *args):
        print(f"[StashProxy] {self.address_string()} - {format % args}")

    def do_GET(self):
        # Health check
        if self.path == "/health":
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"OK")
            return

        # Group stream: /group/<group_id>/stream
        parts = self.path.strip("/").split("/")
        if len(parts) == 3 and parts[0] == "group" and parts[2] == "stream":
            self.handle_group_stream(parts[1])
            return

        self.send_response(404)
        self.end_headers()
        self.wfile.write(b"Not found")

    def handle_group_stream(self, group_id: str):
        # Find group folder
        folder = find_group_folder(group_id)
        if not folder:
            print(f"[StashProxy] Group {group_id} not found in {STASH_GROUPS_PATH}")
            self.send_response(404)
            self.end_headers()
            self.wfile.write(f"Group {group_id} not found".encode())
            return

        # Parse chapters
        chapters = parse_chapter_metadata(folder)
        if not chapters:
            print(f"[StashProxy] No chapters found for group {group_id}")
            self.send_response(404)
            self.end_headers()
            self.wfile.write(b"No chapters found")
            return

        stream_urls = [ch["stream_url"] for ch in chapters]
        print(f"[StashProxy] Streaming group {group_id} — {len(stream_urls)} scene(s)")

        # Build and run FFmpeg
        cmd = build_ffmpeg_concat_command(stream_urls)
        print(f"[StashProxy] FFmpeg cmd: {' '.join(cmd)}")

        try:
            process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
        except FileNotFoundError:
            print(f"[StashProxy] FFmpeg not found at '{FFMPEG_PATH}'")
            self.send_response(500)
            self.end_headers()
            self.wfile.write(b"FFmpeg not found")
            return

        self.send_response(200)
        self.send_header("Content-Type", "video/mp2t")
        self.send_header("Transfer-Encoding", "chunked")
        self.end_headers()

        # Stream FFmpeg stdout to client in chunks
        try:
            while True:
                chunk = process.stdout.read(65536)  # 64KB chunks
                if not chunk:
                    break
                self.wfile.write(chunk)
            process.wait()
        except (BrokenPipeError, ConnectionResetError):
            # Client disconnected — kill FFmpeg
            print(f"[StashProxy] Client disconnected for group {group_id}, killing FFmpeg")
            process.kill()
        except Exception as e:
            print(f"[StashProxy] Stream error for group {group_id}: {e}")
            process.kill()


def main():
    print(f"[StashProxy] Starting on port {PROXY_PORT}")
    print(f"[StashProxy] Stash groups path: {STASH_GROUPS_PATH}")
    print(f"[StashProxy] FFmpeg: {FFMPEG_PATH}")
    print(f"[StashProxy] API key: {'set' if STASH_API_KEY else 'not set'}")

    server = HTTPServer(("0.0.0.0", PROXY_PORT), ProxyHandler)
    print(f"[StashProxy] Listening on http://0.0.0.0:{PROXY_PORT}")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("[StashProxy] Shutting down")
        server.shutdown()


if __name__ == "__main__":
    main()
