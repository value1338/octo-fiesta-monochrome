using System.Text;
using System.Text.Json;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.SquidWTF;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Common;
using Microsoft.Extensions.Options;
using IOFile = System.IO.File;

namespace octo_fiesta.Services.SquidWTF;

/// <summary>
/// Download service implementation using Monochrome API (Tidal backend)
/// Uses multiple API instances with automatic failover
/// No decryption needed - API returns direct streaming URLs
/// No authentication required - works without login!
/// </summary>
public class SquidWTFDownloadService : BaseDownloadService
{
    private readonly MonochromeApiClient _apiClient;
    private readonly SquidWTFSettings _squidWTFSettings;

    protected override string ProviderName => "squidwtf";

    public SquidWTFDownloadService(
        MonochromeApiClient apiClient,
        IConfiguration configuration,
        ILocalLibraryService localLibraryService,
        IMusicMetadataService metadataService,
        IOptions<SubsonicSettings> subsonicSettings,
        IOptions<SquidWTFSettings> squidWTFSettings,
        IServiceProvider serviceProvider,
        ILogger<SquidWTFDownloadService> logger)
        : base(configuration, localLibraryService, metadataService, subsonicSettings.Value, serviceProvider, logger)
    {
        _apiClient = apiClient;
        _squidWTFSettings = squidWTFSettings.Value;
    }

    #region BaseDownloadService Implementation

    public override async Task<bool> IsAvailableAsync()
    {
        return await _apiClient.TestConnectivityAsync();
    }

    protected override string? ExtractExternalIdFromAlbumId(string albumId)
    {
        const string prefix = "ext-squidwtf-album-";
        if (albumId.StartsWith(prefix))
        {
            return albumId[prefix.Length..];
        }
        return null;
    }

    protected override string? GetTargetQuality()
    {
        if (!string.IsNullOrEmpty(_squidWTFSettings.Quality))
        {
            return _squidWTFSettings.Quality;
        }

        return "HI_RES_LOSSLESS";
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        var requestedQuality = GetTidalQuality();
        var (manifest, actualQuality) = await GetManifestAsync(trackId, requestedQuality, cancellationToken);

        if (manifest?.Urls == null || manifest.Urls.Count == 0)
        {
            throw new Exception("No download URLs in manifest");
        }

        var downloadUrl = manifest.Urls[0];
        Logger.LogInformation("Got download URL for track {TrackId}: {Title} (quality: {Quality})", trackId, song.Title, actualQuality);

        // Determine file extension based on manifest mime type
        var extension = GetExtensionFromMimeType(manifest.MimeType);
        var downloadedQuality = GetDownloadedQuality(actualQuality, manifest.MimeType);

        // Build output path
        var artistForPath = song.AlbumArtist ?? song.Artist;
        var basePath = SubsonicSettings.StorageMode == StorageMode.Cache ? CachePath : DownloadPath;
        var outputPath = PathHelper.BuildTrackPath(basePath, artistForPath, song.Album, song.Title, song.Track, extension);

        // Create directories
        var albumFolder = Path.GetDirectoryName(outputPath)!;
        EnsureDirectoryExists(albumFolder);

        // Resolve unique path if file already exists
        outputPath = PathHelper.ResolveUniquePath(outputPath);

        // Download the file (no decryption needed)
        await DownloadFileAsync(downloadUrl, outputPath, cancellationToken);

        // Write metadata
        await WriteMetadataAsync(outputPath, song, cancellationToken);

        return new DownloadResult(outputPath, downloadedQuality);
    }

    #endregion

    #region Download Logic

    /// <summary>
    /// Gets the manifest with failover, falling back to LOSSLESS if HI_RES_LOSSLESS returns DASH format
    /// </summary>
    private async Task<(TidalManifest? manifest, string quality)> GetManifestAsync(
        string trackId, string quality, CancellationToken cancellationToken)
    {
        var path = $"/track/?id={trackId}&quality={quality}";

        var response = await _apiClient.SendWithFailoverAsync(path, "streaming", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var wrapper = JsonSerializer.Deserialize<TidalTrackDownloadResponseWrapper>(json);
        var trackResponse = wrapper?.Data;

        if (string.IsNullOrEmpty(trackResponse?.Manifest))
        {
            throw new Exception("Failed to get manifest from Monochrome API");
        }

        // Check if manifest is DASH (XML) format - not supported, need to fallback to LOSSLESS
        var manifestMimeType = trackResponse.ManifestMimeType ?? "";
        if (manifestMimeType.Contains("dash+xml") || manifestMimeType.Contains("application/dash"))
        {
            if (quality == "HI_RES_LOSSLESS")
            {
                Logger.LogWarning("HI_RES_LOSSLESS returned DASH format for track {TrackId}, falling back to LOSSLESS", trackId);
                return await GetManifestAsync(trackId, "LOSSLESS", cancellationToken);
            }
            throw new Exception($"Unsupported manifest format: {manifestMimeType}");
        }

        // Decode the base64 manifest (JSON format)
        var manifestJson = Encoding.UTF8.GetString(Convert.FromBase64String(trackResponse.Manifest));
        var manifest = JsonSerializer.Deserialize<TidalManifest>(manifestJson);

        return (manifest, quality);
    }

    private string GetTidalQuality()
    {
        var quality = _squidWTFSettings.Quality;

        if (string.IsNullOrEmpty(quality))
        {
            return "HI_RES_LOSSLESS";
        }

        // Map common quality names to Tidal quality codes
        return quality.ToUpperInvariant() switch
        {
            "HI_RES_LOSSLESS" or "HI_RES" or "FLAC_24" => "HI_RES_LOSSLESS",
            "LOSSLESS" or "FLAC" or "FLAC_16" => "LOSSLESS",
            _ => "HI_RES_LOSSLESS"
        };
    }

    #endregion

    #region Helpers

    private static string GetExtensionFromMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return ".mp3";

        return mimeType.ToLowerInvariant() switch
        {
            var m when m.Contains("flac") => ".flac",
            var m when m.Contains("mp4") || m.Contains("m4a") || m.Contains("aac") => ".m4a",
            var m when m.Contains("mp3") || m.Contains("mpeg") => ".mp3",
            _ => ".mp3"
        };
    }

    private static string GetDownloadedQuality(string requestedQuality, string? mimeType)
    {
        if (mimeType?.Contains("flac", StringComparison.OrdinalIgnoreCase) == true)
        {
            return requestedQuality == "HI_RES_LOSSLESS" ? "FLAC_24" : "FLAC_16";
        }

        // AAC/M4A from Tidal is typically 256kbps
        if (mimeType?.Contains("mp4", StringComparison.OrdinalIgnoreCase) == true ||
            mimeType?.Contains("aac", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "AAC_256";
        }

        return "MP3_320";
    }

    private async Task DownloadFileAsync(string url, string outputPath, CancellationToken cancellationToken)
    {
        var response = await _apiClient.SendDirectAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var outputFile = IOFile.Create(outputPath);

        await responseStream.CopyToAsync(outputFile, cancellationToken);

        Logger.LogInformation("Downloaded file to: {Path}", outputPath);
    }

    #endregion
}
