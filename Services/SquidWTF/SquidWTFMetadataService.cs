using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.Search;
using octo_fiesta.Models.Subsonic;
using octo_fiesta.Models.SquidWTF;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Services.SquidWTF;

/// <summary>
/// Metadata service implementation using Monochrome API (Tidal backend)
/// Features automatic failover across multiple API instances
/// No authentication required - works without login!
/// </summary>
public class SquidWTFMetadataService : IMusicMetadataService
{
    private readonly MonochromeApiClient _apiClient;
    private readonly SubsonicSettings _subsonicSettings;
    private readonly ILogger<SquidWTFMetadataService> _logger;

    public SquidWTFMetadataService(
        MonochromeApiClient apiClient,
        IOptions<SubsonicSettings> subsonicSettings,
        ILogger<SquidWTFMetadataService> logger)
    {
        _apiClient = apiClient;
        _subsonicSettings = subsonicSettings.Value;
        _logger = logger;
    }

    #region IMusicMetadataService Implementation

    public async Task<List<Song>> SearchSongsAsync(string query, int limit = 20)
    {
        try
        {
            var path = $"/search/?s={Uri.EscapeDataString(query)}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return new List<Song>();

            var dataResponse = JsonSerializer.Deserialize<TidalDataResponse<TidalTrack>>(response);
            if (dataResponse?.Data?.Items == null) return new List<Song>();

            var songs = new List<Song>();
            foreach (var track in dataResponse.Data.Items.Take(limit))
            {
                var song = MapTidalTrackToSong(track);
                if (ShouldIncludeSong(song))
                {
                    songs.Add(song);
                }
            }

            return songs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search songs for query: {Query}", query);
            return new List<Song>();
        }
    }

    public async Task<List<Album>> SearchAlbumsAsync(string query, int limit = 20)
    {
        try
        {
            var path = $"/search/?al={Uri.EscapeDataString(query)}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return new List<Album>();

            var dataResponse = JsonSerializer.Deserialize<TidalNestedSearchResponse>(response);
            if (dataResponse?.Data?.Albums?.Items == null) return new List<Album>();

            return dataResponse.Data.Albums.Items
                .Take(limit)
                .Select(MapTidalAlbumToAlbum)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search albums for query: {Query}", query);
            return new List<Album>();
        }
    }

    public async Task<List<Artist>> SearchArtistsAsync(string query, int limit = 20)
    {
        try
        {
            var path = $"/search/?a={Uri.EscapeDataString(query)}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return new List<Artist>();

            var dataResponse = JsonSerializer.Deserialize<TidalNestedSearchResponse>(response);
            if (dataResponse?.Data?.Artists?.Items == null) return new List<Artist>();

            return dataResponse.Data.Artists.Items
                .Take(limit)
                .Select(MapTidalArtistToArtist)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search artists for query: {Query}", query);
            return new List<Artist>();
        }
    }

    public async Task<SearchResult> SearchAllAsync(string query, int songLimit = 20, int albumLimit = 20, int artistLimit = 20)
    {
        var songsTask = SearchSongsAsync(query, songLimit);
        var albumsTask = SearchAlbumsAsync(query, albumLimit);
        var artistsTask = SearchArtistsAsync(query, artistLimit);

        await Task.WhenAll(songsTask, albumsTask, artistsTask);

        return new SearchResult
        {
            Songs = await songsTask,
            Albums = await albumsTask,
            Artists = await artistsTask
        };
    }

    public async Task<Song?> GetSongAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "squidwtf") return null;

        try
        {
            var path = $"/info/?id={externalId}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return null;

            var trackInfoWrapper = JsonSerializer.Deserialize<TidalTrackInfoResponseWrapper>(response);
            if (trackInfoWrapper?.Data == null) return null;

            return MapTidalTrackInfoToSong(trackInfoWrapper.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get song: {ExternalId}", externalId);
            return null;
        }
    }

    public async Task<Album?> GetAlbumAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "squidwtf") return null;

        try
        {
            var path = $"/album/?id={externalId}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return null;

            var albumResponse = JsonSerializer.Deserialize<TidalAlbumResponse>(response);
            var albumData = albumResponse?.Data;

            if (albumData == null) return null;

            var album = MapTidalAlbumDataToAlbum(albumData);

            // Add tracks from items
            if (albumData.Items != null)
            {
                foreach (var item in albumData.Items)
                {
                    if (item.Type == "track" && item.Item != null)
                    {
                        var song = MapTidalTrackToSong(item.Item);
                        song.Album = album.Title;
                        song.AlbumId = album.Id;
                        song.AlbumArtist = album.Artist;
                        if (string.IsNullOrEmpty(song.CoverArtUrl))
                        {
                            song.CoverArtUrl = album.CoverArtUrl;
                        }

                        if (ShouldIncludeSong(song))
                        {
                            album.Songs.Add(song);
                        }
                    }
                }
            }

            return album;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get album: {ExternalId}", externalId);
            return null;
        }
    }

    public async Task<Artist?> GetArtistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "squidwtf") return null;

        try
        {
            var path = $"/artist/?id={externalId}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return null;

            var artistResponse = JsonSerializer.Deserialize<TidalArtistResponse>(response);

            if (artistResponse?.Artist == null) return null;

            return MapTidalArtistDataToArtist(artistResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get artist: {ExternalId}", externalId);
            return null;
        }
    }

    public async Task<List<Album>> GetArtistAlbumsAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "squidwtf") return new List<Album>();

        try
        {
            // First get the artist to get their name
            var artist = await GetArtistAsync(externalProvider, externalId);
            if (artist == null) return new List<Album>();

            // Search for albums by artist name
            var path = $"/search/?al={Uri.EscapeDataString(artist.Name)}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null) return new List<Album>();

            var dataResponse = JsonSerializer.Deserialize<TidalNestedSearchResponse>(response);
            if (dataResponse?.Data?.Albums?.Items == null) return new List<Album>();

            // Filter albums that have this artist as main artist
            return dataResponse.Data.Albums.Items
                .Where(a => a.Artists?.Any(ar => ar.Id.ToString() == externalId) == true ||
                           a.Artist?.Id.ToString() == externalId)
                .Select(MapTidalAlbumToAlbum)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get artist albums: {ExternalId}", externalId);
            return new List<Album>();
        }
    }

    public async Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
    {
        try
        {
            var path = $"/search/?p={Uri.EscapeDataString(query)}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null)
            {
                _logger.LogWarning("Playlist search returned null response for query: {Query}", query);
                return new List<ExternalPlaylist>();
            }

            var dataResponse = JsonSerializer.Deserialize<TidalNestedSearchResponse>(response);

            if (dataResponse?.Data?.Playlists?.Items == null)
            {
                return new List<ExternalPlaylist>();
            }

            _logger.LogInformation("Playlist search found {Count} playlists for query: {Query}",
                dataResponse.Data.Playlists.Items.Count, query);

            return dataResponse.Data.Playlists.Items
                .Take(limit)
                .Select(MapTidalPlaylistToExternalPlaylist)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search playlists for query: {Query}", query);
            return new List<ExternalPlaylist>();
        }
    }

    public async Task<ExternalPlaylist?> GetPlaylistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "squidwtf") return null;

        try
        {
            var path = $"/playlist/?id={externalId}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null)
            {
                _logger.LogWarning("Playlist fetch returned null for UUID: {PlaylistUuid}", externalId);
                return null;
            }

            var playlistResponse = JsonSerializer.Deserialize<TidalPlaylistResponse>(response);

            if (playlistResponse?.Playlist == null)
            {
                _logger.LogWarning("Playlist response has no playlist data for UUID: {PlaylistUuid}", externalId);
                return null;
            }

            return MapTidalPlaylistToExternalPlaylist(playlistResponse.Playlist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get playlist: {ExternalId}", externalId);
            return null;
        }
    }

    public async Task<List<Song>> GetPlaylistTracksAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "squidwtf") return new List<Song>();

        try
        {
            var path = $"/playlist/?id={externalId}";
            var response = await _apiClient.GetStringAsync(path);

            if (response == null)
            {
                _logger.LogWarning("Playlist tracks fetch returned null for UUID: {PlaylistUuid}", externalId);
                return new List<Song>();
            }

            var playlistResponse = JsonSerializer.Deserialize<TidalPlaylistResponse>(response);

            if (playlistResponse?.Items == null)
            {
                _logger.LogWarning("Playlist response has no items for UUID: {PlaylistUuid}", externalId);
                return new List<Song>();
            }

            _logger.LogInformation("Playlist has {Count} items for UUID: {PlaylistUuid}",
                playlistResponse.Items.Count, externalId);

            var songs = new List<Song>();
            foreach (var item in playlistResponse.Items)
            {
                if (item.Type == "track" && item.Item != null)
                {
                    var song = MapTidalTrackToSong(item.Item);
                    if (ShouldIncludeSong(song))
                    {
                        songs.Add(song);
                    }
                }
            }

            return songs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get playlist tracks: {ExternalId}", externalId);
            return new List<Song>();
        }
    }

    #endregion

    #region Mapping Methods

    private Song MapTidalTrackToSong(TidalTrack track)
    {
        var externalId = track.Id.ToString();

        // Parse year from album release date
        int? year = null;
        if (!string.IsNullOrEmpty(track.Album?.ReleaseDate) && track.Album.ReleaseDate.Length >= 4)
        {
            if (int.TryParse(track.Album.ReleaseDate.Substring(0, 4), out var y))
            {
                year = y;
            }
        }

        return new Song
        {
            Id = $"ext-squidwtf-song-{externalId}",
            Title = track.Title ?? "",
            Artist = track.Artist?.Name ?? (track.Artists?.FirstOrDefault()?.Name ?? ""),
            ArtistId = track.Artist != null
                ? $"ext-squidwtf-artist-{track.Artist.Id}"
                : (track.Artists?.FirstOrDefault() is { } firstArtist
                    ? $"ext-squidwtf-artist-{firstArtist.Id}"
                    : null),
            Album = track.Album?.Title ?? "",
            AlbumId = track.Album != null ? $"ext-squidwtf-album-{track.Album.Id}" : null,
            Duration = track.Duration,
            Track = track.TrackNumber,
            DiscNumber = track.VolumeNumber,
            Year = year,
            Isrc = track.Isrc,
            Bpm = track.Bpm,
            Copyright = track.Copyright,
            TotalTracks = track.Album?.NumberOfTracks,
            CoverArtUrl = GetTidalCoverUrl(track.Album?.Cover, "320x320"),
            CoverArtUrlLarge = GetTidalCoverUrl(track.Album?.Cover, "1280x1280"),
            IsLocal = false,
            ExternalProvider = "squidwtf",
            ExternalId = externalId,
            ExplicitContentLyrics = track.Explicit ? 1 : 0
        };
    }

    private Song MapTidalTrackInfoToSong(TidalTrackInfoResponse track)
    {
        var externalId = track.Id.ToString();

        // Parse year from album release date
        int? year = null;
        if (!string.IsNullOrEmpty(track.Album?.ReleaseDate) && track.Album.ReleaseDate.Length >= 4)
        {
            if (int.TryParse(track.Album.ReleaseDate.Substring(0, 4), out var y))
            {
                year = y;
            }
        }

        return new Song
        {
            Id = $"ext-squidwtf-song-{externalId}",
            Title = track.Title ?? "",
            Artist = track.Artist?.Name ?? (track.Artists?.FirstOrDefault()?.Name ?? ""),
            ArtistId = track.Artist != null
                ? $"ext-squidwtf-artist-{track.Artist.Id}"
                : (track.Artists?.FirstOrDefault() is { } firstTrackInfoArtist
                    ? $"ext-squidwtf-artist-{firstTrackInfoArtist.Id}"
                    : null),
            Album = track.Album?.Title ?? "",
            AlbumId = track.Album != null ? $"ext-squidwtf-album-{track.Album.Id}" : null,
            Duration = track.Duration,
            Track = track.TrackNumber,
            DiscNumber = track.VolumeNumber,
            Year = year,
            Isrc = track.Isrc,
            TotalTracks = track.Album?.NumberOfTracks,
            CoverArtUrl = GetTidalCoverUrl(track.Album?.Cover, "320x320"),
            CoverArtUrlLarge = GetTidalCoverUrl(track.Album?.Cover, "1280x1280"),
            IsLocal = false,
            ExternalProvider = "squidwtf",
            ExternalId = externalId,
            ExplicitContentLyrics = track.Explicit ? 1 : 0
        };
    }

    private Album MapTidalAlbumToAlbum(TidalAlbum album)
    {
        var externalId = album.Id.ToString();

        int? year = null;
        if (!string.IsNullOrEmpty(album.ReleaseDate) && album.ReleaseDate.Length >= 4)
        {
            if (int.TryParse(album.ReleaseDate.Substring(0, 4), out var y))
            {
                year = y;
            }
        }

        return new Album
        {
            Id = $"ext-squidwtf-album-{externalId}",
            Title = album.Title ?? "",
            Artist = album.Artist?.Name ?? (album.Artists?.FirstOrDefault()?.Name ?? ""),
            ArtistId = album.Artist != null ? $"ext-squidwtf-artist-{album.Artist.Id}" : null,
            Year = year,
            SongCount = album.NumberOfTracks,
            CoverArtUrl = GetTidalCoverUrl(album.Cover, "320x320"),
            IsLocal = false,
            ExternalProvider = "squidwtf",
            ExternalId = externalId
        };
    }

    private Artist MapTidalArtistToArtist(TidalArtist artist)
    {
        var externalId = artist.Id.ToString();

        return new Artist
        {
            Id = $"ext-squidwtf-artist-{externalId}",
            Name = artist.Name ?? "",
            ImageUrl = GetTidalImageUrl(artist.Picture),
            IsLocal = false,
            ExternalProvider = "squidwtf",
            ExternalId = externalId
        };
    }

    private Album MapTidalAlbumDataToAlbum(TidalAlbumData albumData)
    {
        var externalId = albumData.Id.ToString();

        int? year = null;
        if (!string.IsNullOrEmpty(albumData.ReleaseDate) && albumData.ReleaseDate.Length >= 4)
        {
            if (int.TryParse(albumData.ReleaseDate.Substring(0, 4), out var y))
            {
                year = y;
            }
        }

        return new Album
        {
            Id = $"ext-squidwtf-album-{externalId}",
            Title = albumData.Title ?? "",
            Artist = albumData.Artist?.Name ?? (albumData.Artists?.FirstOrDefault()?.Name ?? ""),
            ArtistId = albumData.Artist != null ? $"ext-squidwtf-artist-{albumData.Artist.Id}" : null,
            Year = year,
            SongCount = albumData.NumberOfTracks,
            CoverArtUrl = GetTidalCoverUrl(albumData.Cover, "320x320"),
            IsLocal = false,
            ExternalProvider = "squidwtf",
            ExternalId = externalId
        };
    }

    private Artist MapTidalArtistDataToArtist(TidalArtistResponse artistResponse)
    {
        var artistData = artistResponse.Artist!;
        var externalId = artistData.Id.ToString();

        string? imageUrl = artistResponse.Cover?.Image750;
        if (string.IsNullOrEmpty(imageUrl))
        {
            imageUrl = GetTidalImageUrl(artistData.Picture);
        }

        return new Artist
        {
            Id = $"ext-squidwtf-artist-{externalId}",
            Name = artistData.Name ?? "",
            ImageUrl = imageUrl,
            IsLocal = false,
            ExternalProvider = "squidwtf",
            ExternalId = externalId
        };
    }

    private ExternalPlaylist MapTidalPlaylistToExternalPlaylist(TidalPlaylist playlist)
    {
        return new ExternalPlaylist
        {
            Id = Common.PlaylistIdHelper.CreatePlaylistId("squidwtf", playlist.Uuid ?? ""),
            Name = playlist.Title ?? "",
            CuratorName = playlist.Creator?.Name,
            Provider = "squidwtf",
            ExternalId = playlist.Uuid ?? "",
            TrackCount = playlist.NumberOfTracks,
            Duration = playlist.Duration,
            CoverUrl = GetTidalCoverUrl(playlist.SquareImage ?? playlist.Image, "320x320")
        };
    }

    private static string? GetTidalCoverUrl(string? coverId, string size = "320x320")
    {
        if (string.IsNullOrEmpty(coverId)) return null;

        var formattedId = coverId.Replace("-", "/");
        return $"https://resources.tidal.com/images/{formattedId}/{size}.jpg";
    }

    private static string? GetTidalImageUrl(string? pictureId)
    {
        if (string.IsNullOrEmpty(pictureId)) return null;

        var formattedId = pictureId.Replace("-", "/");
        return $"https://resources.tidal.com/images/{formattedId}/320x320.jpg";
    }

    #endregion

    #region Helpers

    private bool ShouldIncludeSong(Song song)
    {
        if (song.ExplicitContentLyrics == null)
            return true;

        return _subsonicSettings.ExplicitFilter switch
        {
            ExplicitFilter.All => true,
            ExplicitFilter.ExplicitOnly => song.ExplicitContentLyrics != 3,
            ExplicitFilter.CleanOnly => song.ExplicitContentLyrics != 1,
            _ => true
        };
    }

    #endregion
}
