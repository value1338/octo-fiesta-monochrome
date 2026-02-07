# 🐙 Octo-Fiesta

A Subsonic API proxy that sits between your Subsonic client and Navidrome and adds **Monochrome** as a streaming backend. When a track isn’t in your local library, it’s fetched via Monochrome, cached, and served to your client.

---

## 🏗 Architecture

```
┌─────────────────┐      ┌──────────────────┐      ┌─────────────────┐
│  Subsonic       │─────▶│   Octo-Fiesta    │─────▶│   Navidrome     │
│  Client         │◀─────│   (Proxy)        │◀─────│   Server        │
└─────────────────┘      └────────┬─────────┘      └─────────────────┘
                                  │
                                  ▼
                        ┌─────────────────┐
                        │   Monochrome    │
                        │   (embedded)    │
                        └─────────────────┘
```

**Originals:**

- [V1ck3s/octo-fiesta](https://github.com/V1ck3s/octo-fiesta)
- [monochrome-music/monochrome](https://github.com/monochrome-music/monochrome)

---

## 🎵 Music Provider

This fork integrates **Monochrome** directly as the streaming backend.

---

## 📱 Compatible Clients

Tested with **Feishin** (PC) and **Arpeggi** (iOS). It should work with any client that supports a Navidrome/Subsonic server. If something doesn’t work, please open an issue.

---

## ⚠️ Limitations

- **Playlist search:** Some clients may filter externally sourced playlists.
- **Playlist display:** Due to Subsonic API limits, playlists can appear at the end of the album list.
- **Provider behaviour:** Streaming depends on the underlying Monochrome implementation.

For API endpoints and configuration details, see the project Wiki.

---

## 🚀 Compatible Clients

| Platform    | Clients                                         |
| ----------- | ----------------------------------------------- |
| **Desktop** | Aonsoku, Feishin, Supersonic, Subplayer, Aurial |
| **Android** | Tempus, Substreamer, Yuzic                      |
| **iOS**     | Narjo, BeatsX, Yuzic, Arpeggi                   |

👉 See the **[Compatible Clients](https://github.com/V1ck3s/octo-fiesta/wiki/Compatible-Clients)** wiki page for details and incompatible clients.


## 🚀 Quick Start

### Requirements

- A running **Navidrome** server (or another Subsonic-compatible server)
- **Docker** (recommended)

### Build & Run (Docker)

**1. Build the image**

```bash
cd /path/to/octo-fiesta
docker build -t octo-fiesta:latest .
```

**2. Run the container**

```bash
docker run -d \
  --name octo-fiesta \
  --restart unless-stopped \
  -p 4534:4533 \
  -v /YOUR_MUSIC_FOLDER:/music \
  -e TZ=Europe/Berlin \
  -e Subsonic__Url=http://NAVIDROME_IP:4533 \
  -e Subsonic__Username=YOUR_NAVIDROME_USER \
  -e Subsonic__Password=YOUR_NAVIDROME_PASSWORD \
  -e Library__DownloadPath=/music \
  -e SQUIDWTF__Quality=HI_RES_LOSSLESS \
  -e STORAGE_MODE=Permanent \
  -e Subsonic__EnableExternalPlaylists=true \
  octo-fiesta:latest
```

Replace `NAVIDROME_IP`, `/mnt/YOUR_MUSIC_FOLDER`, and credentials with your values.

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `Subsonic__Url` | URL of your Navidrome server | Required |
| `Subsonic__Username` | Navidrome username (for auto library scan) | Optional |
| `Subsonic__Password` | Navidrome password (for auto library scan) | Optional |
| `Library__DownloadPath` | Container path for downloads (must match volume) | `/music` |
| `SQUIDWTF__Quality` | `HI_RES_LOSSLESS`, `LOSSLESS`, `HIGH`, `LOW` | `HI_RES_LOSSLESS` |
| `STORAGE_MODE` | `Permanent` or `Cache` | `Permanent` |


**Note:** The music folder (`/YOUR_MUSIC_FOLDER`) must be the same folder Navidrome uses, so downloaded files appear in your library after a scan.

Octo-Fiesta will be available at **http://localhost:4534**.  
Point your client to this URL (port 4534), not to Navidrome directly.

---

## 📄 License

GPL-3.0

---

## 🙏 Acknowledgments

- **Navidrome** – Self-hosted music server
- **Monochrome** – Streaming provider implementation
- **Subsonic API** – Protocol specification

This Software does not control or endorse any external APIs or services used by the embedded Monochrome implementation. Users are solely responsible for ensuring compliance with applicable laws and service terms when using this software.
