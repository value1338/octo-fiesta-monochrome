using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Subsonic;

namespace octo_fiesta.Services.Spotify;

/// <summary>
/// Service for searching and fetching Spotify playlists.
/// Tracks are mapped to Tidal via SongLink for playback.
/// </summary>
public interface ISpotifyPlaylistService
{
    /// <summary>
    /// Searches Spotify for playlists matching the query.
    /// </summary>
    Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets playlist metadata by Spotify playlist ID.
    /// </summary>
    Task<ExternalPlaylist?> GetPlaylistAsync(string spotifyPlaylistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tracks from a Spotify playlist.
    /// Each track is mapped to Tidal via SongLink; only successfully mapped tracks are returned.
    /// </summary>
    Task<List<Song>> GetPlaylistTracksAsync(string spotifyPlaylistId, CancellationToken cancellationToken = default);
}
