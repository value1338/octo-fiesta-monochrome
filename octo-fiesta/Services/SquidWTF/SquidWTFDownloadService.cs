using System.Diagnostics.CodeAnalysis;
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
/// Download service implementation using SquidWTF API
/// Supports both Qobuz and Tidal backends with automatic instance failover for Tidal
/// No decryption needed - SquidWTF returns direct streaming URLs
/// </summary>
public class SquidWTFDownloadService : BaseDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly SquidWTFSettings _squidWTFSettings;
    private readonly SquidWTFInstanceManager _instanceManager;
    
    // Static Qobuz API endpoint
    private const string QobuzBaseUrl = "https://qobuz.squid.wtf";
    
    // Required headers
    private const string QobuzCountryHeader = "Token-Country";
    private const string QobuzCountryValue = "US";
    private const string TidalClientHeader = "x-client";
    private const string TidalClientValue = "BiniLossless/v3.4";
    
    // Quality mappings
    // Qobuz: 27 = FLAC 24-bit/192kHz, 7 = FLAC 24-bit/96kHz, 6 = FLAC 16-bit/44kHz, 5 = MP3 320kbps
    // Tidal: HI_RES_LOSSLESS (FLAC 24-bit), LOSSLESS (FLAC 16-bit), HIGH (320kbps AAC), LOW (96kbps AAC)
    
    private bool IsQobuzSource => _squidWTFSettings.Source.Equals("Qobuz", StringComparison.OrdinalIgnoreCase);

    protected override string ProviderName => "squidwtf";

    public SquidWTFDownloadService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILocalLibraryService localLibraryService,
        IMusicMetadataService metadataService,
        IOptions<SubsonicSettings> subsonicSettings,
        IOptions<SquidWTFSettings> squidWTFSettings,
        SquidWTFInstanceManager instanceManager,
        IServiceProvider serviceProvider,
        ILogger<SquidWTFDownloadService> logger)
        : base(httpClientFactory, configuration, localLibraryService, metadataService, subsonicSettings.Value, serviceProvider, logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _squidWTFSettings = squidWTFSettings.Value;
        _instanceManager = instanceManager;
    }

    #region BaseDownloadService Implementation

    public override async Task<bool> IsAvailableAsync()
    {
        try
        {
            // Test connectivity to the appropriate backend
            if (IsQobuzSource)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{QobuzBaseUrl}/api/get-music?q=test&offset=0");
                request.Headers.Add(QobuzCountryHeader, QobuzCountryValue);
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            else
            {
                // Test Tidal with instance manager
                var response = await _instanceManager.SendWithFailoverAsync(baseUrl =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/search/?s=test");
                    request.Headers.Add(TidalClientHeader, TidalClientValue);
                    return request;
                });
                return response.IsSuccessStatusCode;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "SquidWTF service not available");
            return false;
        }
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
        
        // Default to highest quality
        return IsQobuzSource ? "27" : "HI_RES_LOSSLESS";
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        if (IsQobuzSource)
        {
            return await DownloadTrackQobuzAsync(trackId, song, cancellationToken);
        }
        else
        {
            return await DownloadTrackTidalAsync(trackId, song, cancellationToken);
        }
    }

    #endregion

    #region Qobuz Download

    private async Task<DownloadResult> DownloadTrackQobuzAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        // Get download URL
        var quality = GetQobuzQuality();
        var path = $"/api/download-music?track_id={trackId}&quality={quality}";

        var req = new HttpRequestMessage(HttpMethod.Get, $"{QobuzBaseUrl}{path}");
        req.Headers.Add(QobuzCountryHeader, QobuzCountryValue);
        var response = await _httpClient.SendAsync(req, cancellationToken);

        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var downloadResponse = JsonSerializer.Deserialize<QobuzDownloadResponse>(json);
        
        if (downloadResponse?.Success != true || string.IsNullOrEmpty(downloadResponse.Data?.Url))
        {
            throw new Exception("Failed to get download URL from SquidWTF Qobuz");
        }
        
        var downloadUrl = downloadResponse.Data.Url;
        Logger.LogInformation("Got download URL for track {TrackId}: {Title}", trackId, song.Title);
        
        // Determine file extension based on quality
        // Qobuz: 27/7/6 = FLAC, 5 = MP3
        var extension = quality == "5" ? ".mp3" : ".flac";
        var downloadedQuality = quality switch
        {
            "27" => "FLAC_24_192",
            "7" => "FLAC_24_96",
            "6" => "FLAC_16",
            "5" => "MP3_320",
            _ => "FLAC"
        };
        
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

    private string GetQobuzQuality()
    {
        var quality = _squidWTFSettings.Quality;
        
        if (string.IsNullOrEmpty(quality))
        {
            return "27"; // Default to highest quality FLAC (24-bit/192kHz)
        }
        
        // Map common quality names to Qobuz quality codes
        // 27 = FLAC 24-bit/192kHz, 7 = FLAC 24-bit/96kHz, 6 = FLAC 16-bit/44kHz, 5 = MP3 320kbps
        return quality.ToUpperInvariant() switch
        {
            "FLAC_24_192" or "FLAC_24" or "27" => "27",
            "FLAC_24_96" or "7" => "7",
            "FLAC_16" or "FLAC" or "6" => "6",
            "MP3_320" or "MP3" or "5" => "5",
            _ => "27"
        };
    }

    #endregion

    #region Tidal Download

    private async Task<DownloadResult> DownloadTrackTidalAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        var requestedQuality = GetTidalQuality();
        var (manifest, actualQuality) = await GetTidalManifestAsync(trackId, requestedQuality, cancellationToken);
        
        if (manifest?.Urls == null || manifest.Urls.Count == 0)
        {
            throw new Exception("No download URLs in Tidal manifest");
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

    /// <summary>
    /// Tidal manifest: prefer <c>/trackManifests/</c> like Monochrome <c>LosslessAPI.getTrack</c>; fallback <c>/track/</c>.
    /// </summary>
    private async Task<(TidalManifest? manifest, string quality)> GetTidalManifestAsync(
        string trackId, string quality, CancellationToken cancellationToken)
    {
        try
        {
            return await GetTidalManifestViaTrackManifestsAsync(trackId, quality, allowHiResDashFallback: true, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "trackManifests failed for track {TrackId}, trying legacy /track/", trackId);
        }

        return await GetTidalManifestLegacyPlaybackInfoAsync(trackId, quality, cancellationToken);
    }

    /// <summary>
    /// <c>/trackManifests/?id=&amp;quality=&amp;adaptive=false&amp;formats=…</c>, then GET signed <c>attributes.uri</c>.
    /// </summary>
    private async Task<(TidalManifest? manifest, string quality)> GetTidalManifestViaTrackManifestsAsync(
        string trackId,
        string quality,
        bool allowHiResDashFallback,
        CancellationToken cancellationToken)
    {
        var path = BuildTrackManifestsQueryPath(trackId, quality);
        var response = await _instanceManager.SendWithFailoverAsync(baseUrl =>
        {
            var root = baseUrl.TrimEnd('/');
            var request = new HttpRequestMessage(HttpMethod.Get, $"{root}{path}");
            request.Headers.Add(TidalClientHeader, TidalClientValue);
            return request;
        }, cancellationToken);

        response.EnsureSuccessStatusCode();

        var envelopeJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!TryExtractSignedManifestUri(envelopeJson, out var signedUri) || string.IsNullOrEmpty(signedUri))
        {
            throw new InvalidOperationException("trackManifests response did not contain a signed manifest URI");
        }

        using var manifestHttpResponse = await _httpClient.GetAsync(signedUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        manifestHttpResponse.EnsureSuccessStatusCode();

        var manifestText = await manifestHttpResponse.Content.ReadAsStringAsync(cancellationToken);
        var contentType = manifestHttpResponse.Content.Headers.ContentType?.MediaType ?? "";
        var looksLikeDash = contentType.Contains("dash", StringComparison.OrdinalIgnoreCase)
            || (manifestText.TrimStart().StartsWith('<') && manifestText.Contains("<MPD", StringComparison.Ordinal));

        if (looksLikeDash)
        {
            if (allowHiResDashFallback && quality == "HI_RES_LOSSLESS")
            {
                Logger.LogWarning(
                    "HI_RES_LOSSLESS returned DASH for track {TrackId} via trackManifests, falling back to LOSSLESS",
                    trackId);
                return await GetTidalManifestViaTrackManifestsAsync(trackId, "LOSSLESS", allowHiResDashFallback: false, cancellationToken);
            }

            throw new Exception($"Unsupported manifest format (DASH): {contentType}");
        }

        var manifest = DeserializeTidalManifestFromManifestBody(manifestText);
        if (manifest?.Urls == null || manifest.Urls.Count == 0)
        {
            throw new InvalidOperationException("Manifest had no stream URLs after trackManifests flow");
        }

        return (manifest, quality);
    }

    /// <summary>Legacy: base64 manifest from <c>/track/?id=</c>.</summary>
    private async Task<(TidalManifest? manifest, string quality)> GetTidalManifestLegacyPlaybackInfoAsync(
        string trackId, string quality, CancellationToken cancellationToken)
    {
        var response = await _instanceManager.SendWithFailoverAsync(baseUrl =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/track/?id={trackId}&quality={quality}");
            request.Headers.Add(TidalClientHeader, TidalClientValue);
            return request;
        }, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var wrapper = JsonSerializer.Deserialize<TidalTrackDownloadResponseWrapper>(json);
        var trackResponse = wrapper?.Data;

        if (string.IsNullOrEmpty(trackResponse?.Manifest))
        {
            throw new Exception("Failed to get manifest from SquidWTF Tidal (legacy /track/)");
        }

        var manifestMimeType = trackResponse.ManifestMimeType ?? "";
        if (manifestMimeType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase)
            || manifestMimeType.Contains("application/dash", StringComparison.OrdinalIgnoreCase))
        {
            if (quality == "HI_RES_LOSSLESS")
            {
                Logger.LogWarning(
                    "HI_RES_LOSSLESS returned DASH for track {TrackId} (legacy /track/), falling back to LOSSLESS",
                    trackId);
                return await GetTidalManifestLegacyPlaybackInfoAsync(trackId, "LOSSLESS", cancellationToken);
            }

            throw new Exception($"Unsupported manifest format: {manifestMimeType}");
        }

        var manifestJson = Encoding.UTF8.GetString(Convert.FromBase64String(trackResponse.Manifest));
        var manifest = JsonSerializer.Deserialize<TidalManifest>(manifestJson);

        return (manifest, quality);
    }

    private static string BuildTrackManifestsQueryPath(string trackId, string quality)
    {
        var formats = GetTrackManifestFormatsForQuality(quality);
        var parts = new List<string>
        {
            $"id={Uri.EscapeDataString(trackId)}",
            $"quality={Uri.EscapeDataString(quality)}",
            "adaptive=false",
        };
        foreach (var f in formats)
        {
            parts.Add($"formats={Uri.EscapeDataString(f)}");
        }

        return "/trackManifests/?" + string.Join("&", parts);
    }

    private static IReadOnlyList<string> GetTrackManifestFormatsForQuality(string quality)
    {
        return quality.ToUpperInvariant() switch
        {
            "DOLBY_ATMOS" => new[] { "EAC3_JOC" },
            "HI_RES_LOSSLESS" or "HI_RES" => new[] { "FLAC_HIRES" },
            "LOSSLESS" or "FLAC" or "FLAC_16" => new[] { "FLAC" },
            "HIGH" or "AAC_320" or "AAC_HIGH" => new[] { "AACLC" },
            "LOW" or "AAC_96" or "AAC_LOW" => new[] { "HEAACV1" },
            _ => new[] { "FLAC" },
        };
    }

    private static bool TryExtractSignedManifestUri(string json, [NotNullWhen(true)] out string? uri)
    {
        uri = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryExtractSignedManifestUri(doc.RootElement, out uri);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractSignedManifestUri(JsonElement root, [NotNullWhen(true)] out string? uri)
    {
        uri = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data))
        {
            return false;
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (data.TryGetProperty("data", out var resource)
            && resource.ValueKind == JsonValueKind.Object
            && resource.TryGetProperty("attributes", out var attrA)
            && attrA.TryGetProperty("uri", out var uriA))
        {
            uri = uriA.GetString();
            return !string.IsNullOrEmpty(uri);
        }

        if (data.TryGetProperty("attributes", out var attrB) && attrB.TryGetProperty("uri", out var uriB))
        {
            uri = uriB.GetString();
            return !string.IsNullOrEmpty(uri);
        }

        return false;
    }

    private static TidalManifest? DeserializeTidalManifestFromManifestBody(string manifestText)
    {
        try
        {
            return JsonSerializer.Deserialize<TidalManifest>(manifestText);
        }
        catch (JsonException)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                manifestText,
                @"https?://[^\s""'<>]+",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1));
            return match.Success
                ? new TidalManifest { Urls = new List<string> { match.Value } }
                : null;
        }
    }

    private string GetTidalQuality()
    {
        var quality = _squidWTFSettings.Quality;
        
        if (string.IsNullOrEmpty(quality))
        {
            return "HI_RES_LOSSLESS"; // Default to highest quality
        }
        
        // Map common quality names to Tidal quality codes
        return quality.ToUpperInvariant() switch
        {
            "HI_RES_LOSSLESS" or "HI_RES" or "FLAC_24" => "HI_RES_LOSSLESS",
            "LOSSLESS" or "FLAC" or "FLAC_16" => "LOSSLESS",
            "HIGH" or "AAC_320" or "AAC_HIGH" => "HIGH",
            "LOW" or "AAC_96" or "AAC_LOW" => "LOW",
            _ => "HI_RES_LOSSLESS"
        };
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Determines file extension based on the manifest's mime type
    /// </summary>
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

    /// <summary>
    /// Determines the quality string for the downloaded file
    /// </summary>
    private static string GetDownloadedQuality(string requestedQuality, string? mimeType)
    {
        if (mimeType?.Contains("flac", StringComparison.OrdinalIgnoreCase) == true)
        {
            return requestedQuality == "HI_RES_LOSSLESS" ? "FLAC_24" : "FLAC_16";
        }
        
        // AAC/M4A from Tidal - determine bitrate based on requested quality
        if (mimeType?.Contains("mp4", StringComparison.OrdinalIgnoreCase) == true ||
            mimeType?.Contains("aac", StringComparison.OrdinalIgnoreCase) == true)
        {
            return requestedQuality switch
            {
                "HIGH" => "AAC_320",
                "LOW" => "AAC_96",
                _ => "AAC_320"  // Default if we got AAC but didn't specifically request it
            };
        }
        
        return "MP3_320";
    }

    private async Task DownloadFileAsync(string url, string outputPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");
        request.Headers.Add("Accept", "*/*");
        
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var outputFile = IOFile.Create(outputPath);
        
        await responseStream.CopyToAsync(outputFile, cancellationToken);
        
        Logger.LogInformation("Downloaded file to: {Path}", outputPath);
    }

    #endregion
}
