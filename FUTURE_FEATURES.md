# Future Features – Spotify Playlist Search Integration

This document describes the **in-progress** integration of Spotify playlist search into Octo-Fiesta Monochrome, based on [SpotiFLAC](https://github.com/SpotiFLAC/SpotiFLAC) code. It is intended for continuation by another developer or LLM.

## Goal

When a user searches in Octo-Fiesta (e.g. "Chill Vibes"), **Spotify playlists** should appear in the search results alongside Tidal playlists. When the user selects a Spotify playlist and plays a track, the audio is streamed from Tidal (via Spotify ID → Tidal ID mapping).

## Architecture Overview

```
User Search "chill"
       │
       ▼
┌──────────────────────────────────────┐
│  SquidWTFMetadataService             │
│  SearchPlaylistsAsync()              │
│  - Tidal (existing)                  │
│  - Spotify (NEW – merge results)     │
└──────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────┐
│  SpotifyPlaylistService (NEW)       │
│  - SearchSpotifyPlaylistsAsync()     │
│  - GetSpotifyPlaylistAsync()         │
│  - GetSpotifyPlaylistTracksAsync()   │
└──────────────────────────────────────┘
       │
       ├── Spotify Client (reverse-engineered)
       │   Port from: SpotiFLAC/backend/spotfetch.go
       │
       └── SongLinkService (DONE)
           Maps Spotify Track ID → Tidal Track ID
           Port from: SpotiFLAC/backend/tidal.go, songlink.go
```

## Implementation Status

### ✅ Completed

1. **SongLinkService** (`Services/Spotify/SongLinkService.cs`)
   - Maps Spotify track URL → Tidal URL via `https://api.song.link/v1-alpha.1/links?url=...`
   - Extracts Tidal track ID from URL
   - Rate limiting: ~7 seconds between calls (SongLink limit)
   - Reference: `SpotiFLAC-main/backend/tidal.go` lines 89–134, `songlink.go` lines 47–120

2. **PlaylistIdHelper** – Added `spotify` to known providers

3. **SpotifyClient** (`Services/Spotify/SpotifyClient.cs`)
   - TOTP (Otp.NET), Session (clientVersion from HTML), Access Token, Client Token
   - GraphQL `QueryAsync` → `api-partner.spotify.com/pathfinder/v2/query`
   - Ported from SpotiFLAC `spotfetch.go`

4. **SpotifyPlaylistService** (`Services/Spotify/SpotifyPlaylistService.cs`) – full implementation
   - `SearchPlaylistsAsync()` – searchDesktop GraphQL, parse playlists
   - `GetPlaylistAsync()` – fetchPlaylist metadata
   - `GetPlaylistTracksAsync()` – fetch tracks, map via SongLink → Tidal, fetch via IMusicMetadataService
   - Uses `SpotifyResponseParser` for GraphQL response parsing

5. **SpotifyResponseParser** (`Services/Spotify/SpotifyResponseParser.cs`)
   - ParseSearchPlaylists, ParsePlaylist, ParsePlaylistTracks
   - Ported from SpotiFLAC `spotfetch.go` FilterSearch, FilterPlaylist

6. **SquidWTFMetadataService** – Spotify integration wired
   - `SearchPlaylistsAsync` merges Tidal + Spotify results
   - `GetPlaylistAsync` / `GetPlaylistTracksAsync` delegate to `_spotifyPlaylistService` when `provider == "spotify"`
   - DI: `SpotifyClient`, `SongLinkService`, `ISpotifyPlaylistService` registered in `Program.cs` for SquidWTF

7. **Playlist cache fix** – SubSonicController uses `track.Id` (playback ID) for cache key, not `ext-{provider}-{externalId}`

### 🔲 To Do (Optional / Future)

#### 1. Spotify Client fallback (if blocked)

**Source:** `SpotiFLAC-main/backend/spotfetch.go`

The Spotify client uses a **reverse-engineered** Web Player API:

- **Initialization:** `getSessionInfo()` → `getAccessToken()` (TOTP) → `getClientToken()`
- **TOTP:** Hardcoded secret in `generateTOTP()`, version 61. Uses `otpauth://totp/secret?secret=...`
- **Endpoints:**
  - Token: `GET https://open.spotify.com/api/token?reason=init&productType=web-player&totp=...`
  - Client token: `POST https://clienttoken.spotify.com/v1/clienttoken`
  - GraphQL: `POST https://api-partner.spotify.com/pathfinder/v2/query`

**GraphQL operations (from spotify_metadata.go):**

- **Search:** `operationName: "searchDesktop"`, `sha256Hash: "fcad5a3e0d5af727fb76966f06971c19cfa2275e6ff7671196753e008611873c"`
- **Playlist fetch:** `operationName: "fetchPlaylist"`, `sha256Hash: "bb67e0af06e8d6f52b531f97468ee4acd44cd0f82b988e15c2ea47b1148efc77"`

**C# port tasks:**

- Create `SpotifyClient.cs` with `InitializeAsync()`, `QueryAsync(payload)`
- Implement TOTP (e.g. `OtpNet` NuGet or manual)
- Parse `FilterSearch` response for playlists (see `spotfetch.go` FilterSearch, playlists section ~line 1240)
- Parse `FilterPlaylist` for playlist tracks (see `spotfetch.go` FilterPlaylist ~line 696)

**Note:** Spotify may change these endpoints. SpotiFLAC uses `clientVersion` from the HTML. Consider SpotFetch API as fallback (see `spotfetch_api.go`).

#### 2. SpotifyPlaylistService – Full Implementation

**Search playlists:**

```csharp
// Call SpotifyClient.Query with searchDesktop payload
// Parse response: data.searchV2.playlistsV2.items
// Map to ExternalPlaylist with Id = "pl-spotify-{playlistId}"
```

**Get playlist metadata:**

```csharp
// Call SpotifyClient.Query with fetchPlaylist payload
// Variables: uri = "spotify:playlist:{id}", offset, limit
// Paginate: offset += 1000 until no more items
```

**Get playlist tracks (with Tidal mapping):**

```csharp
// 1. Fetch playlist (above) – get track list with Spotify IDs
// 2. For each track: SongLinkService.GetTidalTrackIdFromSpotify(spotifyTrackId)
// 3. For each successful mapping: fetch Tidal track via existing SquidWTF/Tidal API
// 4. Return List<Song> (Tidal songs)
// 5. Rate limit: SongLink allows ~9 calls/min – add delay between mappings
```

#### 3. Download Service & Playlist Cache

When playing a Spotify playlist track, the track ID will be `ext-squidwtf-{tidalId}` (we map to Tidal first). The existing download flow should work.

**Playlist cache:** In `SubSonicController`, when adding tracks to `PlaylistSyncService.AddTrackToPlaylistCache(trackId, playlistId)`, the code uses `ext-{playlistProvider}-{track.ExternalId}`. For Spotify playlists, `playlistProvider` is `"spotify"`, but the actual track ID for playback is `ext-squidwtf-{tidalId}`. You may need to use the track’s download provider (squidwtf) for the cache key, not the playlist provider, so playback correctly associates the track with the Spotify playlist.

#### 4. Configuration

Add optional settings:

```json
"Spotify": {
  "Enabled": true,
  "SearchLimit": 10
}
```

Disable Spotify search if the client fails (e.g. blocked regions).

## File Reference – SpotiFLAC Source

| Octo-Fiesta (C#)           | SpotiFLAC (Go)                    | Purpose                          |
|---------------------------|-----------------------------------|----------------------------------|
| SongLinkService.cs        | tidal.go:89-134, songlink.go:47-120 | Spotify → Tidal mapping          |
| SpotifyClient.cs (TODO)   | spotfetch.go                      | Spotify API client               |
| SpotifyPlaylistService   | spotify_metadata.go               | Search, fetch playlist, parse    |
| –                         | spotfetch.go FilterSearch         | Parse search response (playlists)|
| –                         | spotfetch.go FilterPlaylist       | Parse playlist + tracks          |

## SongLink API

- **URL:** `https://api.song.link/v1-alpha.1/links?url={spotifyTrackUrl}`
- **Response:** `linksByPlatform.tidal.url` (Tidal track URL)
- **Rate limit:** ~9 requests/minute (SpotiFLAC uses 7s delay)
- **Tidal URL format:** `https://tidal.com/browse/track/{trackId}` – extract numeric ID

## Testing

1. **SongLinkService:** Unit test with a known Spotify track ID (e.g. `3n3Ppam7vgaVa1iaRUc9Lp` for "Blinding Lights")
2. **Spotify Client:** Manual test – may fail in blocked regions; consider SpotFetch API fallback
3. **End-to-end:** Search → select Spotify playlist → play track → verify Tidal stream

## Dependencies

- **SongLink:** None (plain HTTP)
- **Spotify Client:** TOTP library (e.g. `OtpNet` NuGet package)
- **SpotFetch API** (optional fallback): External service – see `spotfetch_api.go`

## License Note

SpotiFLAC is a separate project. Ensure compliance with its license when porting code. This integration uses the same reverse-engineering approach; no official Spotify API is used.
