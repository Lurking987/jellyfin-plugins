FROM python:3.12-slim

# Install FFmpeg
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY proxy.py .

# Environment variables (override at runtime)
ENV STASH_GROUPS_PATH=/stash-groups
ENV STASH_API_KEY=""
ENV PROXY_PORT=5678
ENV FFMPEG_PATH=ffmpeg

EXPOSE 5678

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD python3 -c "import urllib.request; urllib.request.urlopen('http://localhost:5678/health')"

CMD ["python3", "proxy.py"]
