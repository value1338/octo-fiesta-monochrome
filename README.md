# Octo-Fiesta Monochrome (Beta)

A Subsonic API proxy between Navidrome and your client. Uses **Monochrome** (Tidal) as a streaming backend for tracks not in your local library.

## Beta: Spotify Playlist Search

This beta adds **Spotify playlist search**. Playlists appear alongside Tidal results; tracks are mapped to Tidal via SongLink and streamed through Monochrome.

**Not production-ready:** The Spotify integration is experimental. The reverse-engineered Spotify client may be blocked in some regions; SongLink has a rate limit (~7 s per track), so large playlists load slowly.

## Quick Start

```bash
git clone https://github.com/value1338/octo-fiesta-monochrome.git
cd octo-fiesta-monochrome
git checkout beta

cp .env.example .env
# Edit .env: Navidrome URL, username, password

docker compose build
docker compose up -d
```

Octo-Fiesta runs at **http://localhost:4534**. Point your Subsonic client to this URL.

**Requirements:** Navidrome, SquidWTF with Tidal backend. For Spotify: `Subsonic__EnableExternalPlaylists` = `true`, `Subsonic__MusicService` = `SquidWTF`, `SquidWTF__Source` = `Tidal`.

## License

GPL-3.0
