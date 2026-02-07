using System.Net;
using Microsoft.Extensions.Options;
using octo_fiesta.Models.Settings;

namespace octo_fiesta.Services.SquidWTF;

/// <summary>
/// HTTP client with automatic failover across multiple Monochrome API instances.
/// Implements retry logic similar to Monochrome's api.js for resilient API access.
/// No authentication required - works without login!
/// </summary>
public class MonochromeApiClient
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _downloadClient;
    private readonly SquidWTFSettings _settings;
    private readonly ILogger<MonochromeApiClient> _logger;

    // Required headers for the Monochrome/Tidal API
    private const string ClientHeader = "x-client";
    private const string ClientValue = "BiniLossless/v3.4";

    // Timeout for API requests (same as dev version)
    private const int DefaultTimeoutSeconds = 5;

    public MonochromeApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SquidWTFSettings> settings,
        ILogger<MonochromeApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        // Set timeout for API requests (5 seconds per request like dev version)
        _httpClient.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

        // Separate client for downloads with longer timeout (10 minutes for large FLAC files)
        _downloadClient = httpClientFactory.CreateClient();
        _downloadClient.Timeout = TimeSpan.FromMinutes(10);

        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends a request with automatic failover across multiple API instances.
    /// </summary>
    /// <param name="relativePath">API path (e.g., "/search/?s=test")</param>
    /// <param name="instanceType">Type of instance to use: "api" or "streaming"</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response from the first successful instance</returns>
    public async Task<HttpResponseMessage> SendWithFailoverAsync(
        string relativePath,
        string instanceType = "api",
        CancellationToken cancellationToken = default)
    {
        return await SendRequestWithFailoverAsync(relativePath, instanceType, cancellationToken);
    }

    /// <summary>
    /// Sends a request to a specific URL (for streaming/download)
    /// Uses a dedicated client with longer timeout for large files
    /// </summary>
    public async Task<HttpResponseMessage> SendDirectAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");
        request.Headers.Add("Accept", "*/*");

        // Use download client with 10-minute timeout for large FLAC files
        return await _downloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>
    /// Gets the content as string from a request with failover
    /// </summary>
    public async Task<string?> GetStringAsync(
        string relativePath,
        string instanceType = "api",
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await SendWithFailoverAsync(relativePath, instanceType, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            _logger.LogWarning("API request failed with status {StatusCode} for path: {Path}",
                response.StatusCode, relativePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get content for path: {Path}", relativePath);
            return null;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendRequestWithFailoverAsync(
        string relativePath,
        string instanceType,
        CancellationToken cancellationToken)
    {
        var instances = instanceType == "streaming"
            ? _settings.GetStreamingInstances()
            : _settings.GetApiInstances();

        if (instances.Count == 0)
        {
            throw new InvalidOperationException($"No API instances configured for type: {instanceType}");
        }

        // Allow some retries across instances
        var maxTotalAttempts = Math.Min(instances.Count * 2, 10);
        Exception? lastError = null;
        var instanceIndex = 0;

        for (var attempt = 1; attempt <= maxTotalAttempts; attempt++)
        {
            var baseUrl = instances[instanceIndex % instances.Count];
            var url = BuildUrl(baseUrl, relativePath);
            HttpResponseMessage? response = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add(ClientHeader, ClientValue);

                response = await _httpClient.SendAsync(request, cancellationToken);

                // Rate limited - try next instance
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Rate limit hit on {BaseUrl}. Trying next instance...", baseUrl);
                    response.Dispose();
                    instanceIndex++;
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                // Success
                if (response.IsSuccessStatusCode)
                {
                    if (attempt > 1)
                    {
                        _logger.LogInformation("Request succeeded on instance {BaseUrl} after {Attempts} attempts",
                            baseUrl, attempt);
                    }
                    return response;
                }

                // Auth failed - try next instance
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Auth failed on {BaseUrl}. Trying next instance...", baseUrl);
                    response.Dispose();
                    instanceIndex++;
                    continue;
                }

                // Server error - try next instance
                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("Server error {StatusCode} on {BaseUrl}. Trying next instance...",
                        response.StatusCode, baseUrl);
                    response.Dispose();
                    instanceIndex++;
                    continue;
                }

                // Other client errors (404, 400) - likely a permanent error, don't retry
                lastError = new HttpRequestException($"Request failed with status {response.StatusCode}");
                response.Dispose();
                instanceIndex++;
            }
            catch (OperationCanceledException)
            {
                response?.Dispose();
                throw; // Don't retry on cancellation
            }
            catch (Exception ex)
            {
                response?.Dispose();
                lastError = ex;
                _logger.LogWarning("Network error on {BaseUrl}: {Error}. Trying next instance...",
                    baseUrl, ex.Message);
                instanceIndex++;
                await Task.Delay(200, cancellationToken);
            }
        }

        throw lastError ?? new HttpRequestException($"All API instances failed for: {relativePath}");
    }

    private static string BuildUrl(string baseUrl, string relativePath)
    {
        // Normalize the base URL and relative path
        var normalizedBase = baseUrl.TrimEnd('/');
        var normalizedPath = relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";

        return $"{normalizedBase}{normalizedPath}";
    }

    /// <summary>
    /// Tests connectivity to the API
    /// </summary>
    public async Task<bool> TestConnectivityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendWithFailoverAsync("/search/?s=test", "api", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API connectivity test failed");
            return false;
        }
    }
}
