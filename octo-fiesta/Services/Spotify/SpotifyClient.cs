using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using OtpNet;

namespace octo_fiesta.Services.Spotify;

/// <summary>
/// Reverse-engineered Spotify Web Player API client.
/// Ported from SpotiFLAC backend/spotfetch.go.
/// </summary>
public class SpotifyClient
{
    private static readonly Uri SpotifyBase = new("https://open.spotify.com");
    private const string TOTPSecret = "GM3TMMJTGYZTQNZVGM4DINJZHA4TGOBYGMZTCMRTGEYDSMJRHE4TEOBUG4YTCMRUGQ4DQOJUGQYTAMRRGA2TCMJSHE3TCMBY";
    private const int TOTPVersion = 61;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;
    private readonly ILogger<SpotifyClient> _logger;
    private string _accessToken = "";
    private string _clientToken = "";
    private string _clientId = "";
    private string _deviceId = "";
    private string _clientVersion = "";

    public SpotifyClient(ILogger<SpotifyClient> logger)
    {
        var handler = new HttpClientHandler { UseCookies = true };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await GetSessionInfoAsync(cancellationToken);
        await GetAccessTokenAsync(cancellationToken);
        await GetClientTokenAsync(cancellationToken);
    }

    private (string Code, int Version) GenerateTotp()
    {
        var secretBytes = Base32Encoding.ToBytes(TOTPSecret);
        var totp = new Totp(secretBytes);
        var code = totp.ComputeTotp(DateTime.UtcNow);
        return (code, TOTPVersion);
    }

    private static string? GetCookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var header in values)
        {
            var parts = header.Split(';');
            if (parts.Length > 0 && parts[0].StartsWith(name + "=", StringComparison.Ordinal))
                return parts[0][(name.Length + 1)..].Trim();
        }
        return null;
    }

    private async Task GetSessionInfoAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SpotifyBase);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var match = Regex.Match(body, @"<script id=""appServerConfig"" type=""text/plain"">([^<]+)</script>");
        if (match.Success)
        {
            try
            {
                var decoded = Convert.FromBase64String(match.Groups[1].Value);
                using var json = JsonDocument.Parse(decoded);
                if (json.RootElement.TryGetProperty("clientVersion", out var cv))
                    _clientVersion = cv.GetString() ?? "";
            }
            catch { /* ignore */ }
        }

        _deviceId = GetCookieValue(response, "sp_t") ?? _deviceId;
    }

    private async Task GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var (totpCode, totpVer) = GenerateTotp();
        var url = $"https://open.spotify.com/api/token?reason=init&productType=web-player&totp={totpCode}&totpVer={totpVer}&totpServer={totpCode}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Content-Type", "application/json;charset=UTF-8");
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify access token request failed: HTTP {StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"Spotify access token failed: HTTP {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        _accessToken = root.TryGetProperty("accessToken", out var at) ? at.GetString() ?? "" : "";
        _clientId = root.TryGetProperty("clientId", out var ci) ? ci.GetString() ?? "" : "";
        _deviceId = GetCookieValue(response, "sp_t") ?? _deviceId;
    }

    private async Task GetClientTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_deviceId) || string.IsNullOrEmpty(_clientVersion))
        {
            await GetSessionInfoAsync(cancellationToken);
            await GetAccessTokenAsync(cancellationToken);
        }

        var payload = new
        {
            client_data = new
            {
                client_version = _clientVersion,
                client_id = _clientId,
                js_sdk_data = new
                {
                    device_brand = "unknown",
                    device_model = "unknown",
                    os = "windows",
                    os_version = "NT 10.0",
                    device_id = _deviceId,
                    device_type = "computer"
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://clienttoken.spotify.com/v1/clienttoken") { Content = content };
        request.Headers.Add("Authority", "clienttoken.spotify.com");
        request.Headers.Add("Accept", "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Spotify client token request failed: HTTP {StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"Spotify client token failed: HTTP {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("response_type", out var rt) && rt.GetString() != "RESPONSE_GRANTED_TOKEN_RESPONSE")
            throw new InvalidOperationException("Spotify client token: invalid response type");

        if (root.TryGetProperty("granted_token", out var gt) && gt.TryGetProperty("token", out var t))
            _clientToken = t.GetString() ?? "";
    }

    /// <summary>
    /// Execute a GraphQL query against the Spotify partner API.
    /// </summary>
    public async Task<JsonDocument?> QueryAsync(object payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_clientToken))
            await InitializeAsync(cancellationToken);

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api-partner.spotify.com/pathfinder/v2/query") { Content = content };
        request.Headers.Add("Authorization", "Bearer " + _accessToken);
        request.Headers.Add("Client-Token", _clientToken);
        request.Headers.Add("Spotify-App-Version", _clientVersion);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Spotify GraphQL failed: HTTP {StatusCode} | {Body}", response.StatusCode, body.Length > 200 ? body[..200] : body);
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
