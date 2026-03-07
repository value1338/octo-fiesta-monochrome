using System.Text.Json;

namespace octo_fiesta.Services.Spotify;

/// <summary>
/// Maps Spotify track IDs to Tidal track IDs via the SongLink (Odesli) API.
/// Ported from SpotiFLAC backend/tidal.go and songlink.go.
/// Reference: https://api.song.link/v1-alpha.1/links
/// Rate limit: ~9 requests/minute - use 7+ second delay between calls.
/// </summary>
public class SongLinkService
{
    private const string ApiBase = "https://api.song.link/v1-alpha.1/links";
    private readonly HttpClient _httpClient;
    private readonly ILogger<SongLinkService> _logger;
    private DateTime _lastCallTime;
    private static readonly TimeSpan MinDelayBetweenCalls = TimeSpan.FromSeconds(7);

    public SongLinkService(IHttpClientFactory httpClientFactory, ILogger<SongLinkService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
        _logger = logger;
    }

    /// <summary>
    /// Maps a Spotify track ID to a Tidal track ID.
    /// </summary>
    /// <param name="spotifyTrackId">Spotify track ID (e.g. from open.spotify.com/track/xxx)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tidal track ID as string, or null if not found</returns>
    public async Task<string?> GetTidalTrackIdFromSpotifyAsync(string spotifyTrackId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyTrackId))
            return null;

        await EnforceRateLimitAsync(cancellationToken);

        var spotifyUrl = $"https://open.spotify.com/track/{spotifyTrackId.Trim()}";
        var apiUrl = $"{ApiBase}?url={Uri.EscapeDataString(spotifyUrl)}";

        try
        {
            var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("SongLink API returned {StatusCode} for Spotify track {SpotifyId}", response.StatusCode, spotifyTrackId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("linksByPlatform", out var linksByPlatform))
                return null;

            if (!linksByPlatform.TryGetProperty("tidal", out var tidal) ||
                !tidal.TryGetProperty("url", out var urlElement))
            {
                return null;
            }

            var tidalUrl = urlElement.GetString();
            if (string.IsNullOrEmpty(tidalUrl))
                return null;

            var trackId = ExtractTidalTrackIdFromUrl(tidalUrl);
            if (!string.IsNullOrEmpty(trackId))
                _logger.LogDebug("Mapped Spotify {SpotifyId} → Tidal {TidalId}", spotifyTrackId, trackId);

            return trackId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SongLink API failed for Spotify track {SpotifyId}", spotifyTrackId);
            return null;
        }
    }

    private static string? ExtractTidalTrackIdFromUrl(string tidalUrl)
    {
        // Format: https://tidal.com/browse/track/12345678 or https://listen.tidal.com/track/12345678
        var parts = tidalUrl.Split("/track/", StringSplitOptions.None);
        if (parts.Length < 2)
            return null;

        var idPart = parts[1].Split('?', '#')[0].Trim();
        return long.TryParse(idPart, out _) ? idPart : null;
    }

    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        var elapsed = DateTime.UtcNow - _lastCallTime;
        if (elapsed < MinDelayBetweenCalls)
        {
            var wait = MinDelayBetweenCalls - elapsed;
            await Task.Delay(wait, cancellationToken);
        }
        _lastCallTime = DateTime.UtcNow;
    }
}
