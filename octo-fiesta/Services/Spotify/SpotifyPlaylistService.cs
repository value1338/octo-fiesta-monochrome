using Microsoft.Extensions.DependencyInjection;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Subsonic;
using octo_fiesta.Services.Common;

namespace octo_fiesta.Services.Spotify;

/// <summary>
/// Spotify playlist service - searches and fetches playlists via reverse-engineered API.
/// Tracks are mapped to Tidal via SongLink for playback.
/// Uses IServiceProvider to resolve IMusicMetadataService lazily (avoids circular dep with SquidWTFMetadataService).
/// </summary>
public class SpotifyPlaylistService : ISpotifyPlaylistService
{
    private const string SearchOperation = "searchDesktop";
    private const string SearchHash = "fcad5a3e0d5af727fb76966f06971c19cfa2275e6ff7671196753e008611873c";
    private const string FetchPlaylistOperation = "fetchPlaylist";
    private const string FetchPlaylistHash = "bb67e0af06e8d6f52b531f97468ee4acd44cd0f82b988e15c2ea47b1148efc77";

    private readonly SpotifyClient _spotifyClient;
    private readonly SongLinkService _songLinkService;
    private readonly IServiceProvider _services;
    private readonly ILogger<SpotifyPlaylistService> _logger;

    public SpotifyPlaylistService(
        SpotifyClient spotifyClient,
        SongLinkService songLinkService,
        IServiceProvider services,
        ILogger<SpotifyPlaylistService> logger)
    {
        _spotifyClient = spotifyClient;
        _songLinkService = songLinkService;
        _services = services;
        _logger = logger;
    }

    private IMusicMetadataService MetadataService => _services.GetRequiredService<IMusicMetadataService>();

    public async Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<ExternalPlaylist>();

        try
        {
            var payload = new
            {
                variables = new
                {
                    searchTerm = query,
                    offset = 0,
                    limit = Math.Min(limit, 50),
                    numberOfTopResults = 5,
                    includeAudiobooks = true,
                    includeArtistHasConcertsField = false,
                    includePreReleases = true,
                    includeAuthors = false
                },
                operationName = SearchOperation,
                extensions = new
                {
                    persistedQuery = new
                    {
                        version = 1,
                        sha256Hash = SearchHash
                    }
                }
            };

            using var doc = await _spotifyClient.QueryAsync(payload, cancellationToken);
            if (doc == null) return new List<ExternalPlaylist>();

            var results = SpotifyResponseParser.ParseSearchPlaylists(doc.RootElement);
            return results
                .Take(limit)
                .Select(r => new ExternalPlaylist
                {
                    Id = CreatePlaylistId(r.Id),
                    Name = r.Name,
                    Provider = "spotify",
                    ExternalId = r.Id,
                    TrackCount = 0,
                    CoverUrl = r.CoverUrl,
                    CuratorName = r.Owner
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify playlist search failed for query: {Query}", query);
            return new List<ExternalPlaylist>();
        }
    }

    public async Task<ExternalPlaylist?> GetPlaylistAsync(string spotifyPlaylistId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId)) return null;

        try
        {
            var detail = await FetchPlaylistWithTracksAsync(spotifyPlaylistId, 0, 1, cancellationToken);
            if (detail == null) return null;

            return new ExternalPlaylist
            {
                Id = CreatePlaylistId(detail.Id),
                Name = detail.Name,
                Description = detail.Description,
                Provider = "spotify",
                ExternalId = detail.Id,
                TrackCount = detail.TrackCount,
                Duration = detail.Tracks.Sum(t => t.DurationSeconds),
                CoverUrl = detail.CoverUrl,
                CuratorName = detail.OwnerName
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify playlist fetch failed for id: {Id}", spotifyPlaylistId);
            return null;
        }
    }

    public async Task<List<Song>> GetPlaylistTracksAsync(string spotifyPlaylistId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId)) return new List<Song>();

        try
        {
            var tracks = await FetchAllPlaylistTracksAsync(spotifyPlaylistId, cancellationToken);
            if (tracks.Count == 0) return new List<Song>();

            var songs = new List<Song>();
            foreach (var track in tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tidalId = await _songLinkService.GetTidalTrackIdFromSpotifyAsync(track.SpotifyId, cancellationToken);
                if (string.IsNullOrEmpty(tidalId))
                {
                    _logger.LogDebug("SongLink: no Tidal mapping for Spotify track {SpotifyId}", track.SpotifyId);
                    continue;
                }

                var song = await MetadataService.GetSongAsync("squidwtf", tidalId);
                if (song != null)
                    songs.Add(song);
            }

            return songs;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Spotify playlist tracks fetch failed for id: {Id}", spotifyPlaylistId);
            return new List<Song>();
        }
    }

    private async Task<List<SpotifyPlaylistTrack>> FetchAllPlaylistTracksAsync(string playlistId, CancellationToken cancellationToken)
    {
        var all = new List<SpotifyPlaylistTrack>();
        var offset = 0;
        const int limit = 1000;

        while (true)
        {
            var detail = await FetchPlaylistWithTracksAsync(playlistId, offset, limit, cancellationToken);
            if (detail == null || detail.Tracks.Count == 0) break;

            all.AddRange(detail.Tracks);
            if (detail.Tracks.Count < limit || all.Count >= detail.TrackCount) break;
            offset += limit;
        }

        return all;
    }

    private async Task<SpotifyPlaylistDetail?> FetchPlaylistWithTracksAsync(string playlistId, int offset, int limit, CancellationToken cancellationToken)
    {
        var payload = new
        {
            variables = new
            {
                uri = $"spotify:playlist:{playlistId}",
                offset,
                limit,
                enableWatchFeedEntrypoint = false
            },
            operationName = FetchPlaylistOperation,
            extensions = new
            {
                persistedQuery = new
                {
                    version = 1,
                    sha256Hash = FetchPlaylistHash
                }
            }
        };

        using var doc = await _spotifyClient.QueryAsync(payload, cancellationToken);
        if (doc == null) return null;

        return SpotifyResponseParser.ParsePlaylist(doc.RootElement, playlistId);
    }

    public static string CreatePlaylistId(string spotifyPlaylistId) =>
        PlaylistIdHelper.CreatePlaylistId("spotify", spotifyPlaylistId);
}
