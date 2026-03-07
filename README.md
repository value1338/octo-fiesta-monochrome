# Octo-Fiesta Monochrome (Beta)

Subsonic-Proxy zwischen Navidrome und deinem Client. Nutzt **Monochrome** (Tidal) als Streaming-Backend für Tracks, die nicht lokal vorhanden sind.

## Beta: Spotify-Playlist-Suche

In dieser Beta-Version kannst du zusätzlich **Spotify-Playlists** durchsuchen. Die Tracks werden über SongLink auf Tidal gemappt und über Monochrome abgespielt.

**Noch nicht fertig:** Die Spotify-Integration ist experimentell. Der reverse-engineerte Spotify-Client kann regional blockiert sein; SongLink hat ein Rate-Limit (~7 s pro Track). Große Playlists laden entsprechend langsam.

## Quick Start

```bash
git clone https://github.com/value1338/octo-fiesta-monochrome.git
cd octo-fiesta-monochrome
git checkout beta

cp .env.example .env
# .env bearbeiten: Navidrome-URL, User, Passwort

docker compose build
docker compose up -d
```

Octo-Fiesta läuft auf **http://localhost:4534**. Subsonic-Client auf diese URL zeigen.

**Voraussetzungen:** Navidrome, SquidWTF mit Tidal-Backend. Für Spotify: `Subsonic__EnableExternalPlaylists` = `true`, `Subsonic__MusicService` = `SquidWTF`, `SquidWTF__Source` = `Tidal`.

## Lizenz

GPL-3.0
