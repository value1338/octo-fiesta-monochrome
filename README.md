# Octo-Fiesta Monochrome

A Subsonic API proxy that sits between your Subsonic client and Navidrome, adding **Monochrome** (Tidal) as a streaming backend. When a track isn't in your local library, it's fetched via Monochrome, cached, and served to your client.

## Quick Start

### 1. Clone the repository

```bash
git clone https://github.com/value1338/octo-fiesta-monochrome.git
cd octo-fiesta-monochrome
```

### 2. Configure

Copy `.env.example` to `.env` and edit your Navidrome settings:

```bash
cp .env.example .env
```

Edit `.env` and set:

- `Subsonic__Url` – Your Navidrome server URL (e.g. `http://localhost:4533`)
- `Subsonic__Username` – Your Navidrome username
- `Subsonic__Password` – Your Navidrome password
- Update the volume path in `docker-compose.yml` to match your music folder

### 3. Build and run

```bash
docker compose build
docker compose up -d
```

Octo-Fiesta will be available at **http://localhost:4534**. Point your Subsonic client to this URL instead of Navidrome directly.

---

## Alternative: Pre-built Docker Image

Use the pre-built image from GitHub Container Registry (no build required):

```bash
docker run -d \
  --name octo-fiesta \
  --restart unless-stopped \
  -p 4534:8080 \
  -v /path/to/your/music:/music \
  --env-file .env \
  ghcr.io/value1338/octo-fiesta-monochrome:latest
```

Create a `.env` file from `.env.example` and configure your Navidrome URL, username, and password before running.

---

## Unraid Installation

1. Go to **Docker** → **Add Container**.

2. **Repository:** `ghcr.io/value1338/octo-fiesta-monochrome:latest`

3. **Network:** Bridge (default)

4. **Port:** Add port mapping `4534` (host) → `8080` (container)

5. **Volume:** Add path mapping
   - Host path: `/mnt/user/music` (or your music share, must match Navidrome)
   - Container path: `/music`

6. **Environment variables:** Add the following (or use an env file):
   - `Subsonic__Url` = `http://Navidrome-IP:4533`
   - `Subsonic__Username` = your Navidrome username
   - `Subsonic__Password` = your Navidrome password
   - `Subsonic__MusicService` = `SquidWTF`
   - `SquidWTF__Source` = `Tidal`
   - `SquidWTF__Quality` = `LOSSLESS`
   - `Library__DownloadPath` = `/music`

7. Apply and start the container.

**Update notifications:** Unraid checks ghcr.io for new image versions. When an update is available, you'll see an update prompt in the Docker tab. The [docker.versions](https://github.com/phyzical/docker.versions) plugin (via Community Applications) can provide changelogs and improved update detection.

---

## Requirements

- A running [Navidrome](https://www.navidrome.org/) server
- Docker and Docker Compose (or Unraid with Docker)

## License

GPL-3.0
