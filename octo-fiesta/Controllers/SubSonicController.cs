using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Text.Json;

namespace octo_fiesta.Controllers;

[ApiController]
[Route("")]
public class SubsonicController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly string _subsonicUrl;
    private readonly string _defaultUser;
    private readonly string _defaultPassword;
    private readonly string _apiVersion;
    private readonly string _client;

    public SubsonicController(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
        _subsonicUrl = Environment.GetEnvironmentVariable("SUBSONIC_URL") ?? string.Empty;
        _defaultUser = Environment.GetEnvironmentVariable("SUBSONIC_USER") ?? string.Empty;
        _defaultPassword = Environment.GetEnvironmentVariable("SUBSONIC_PASSWORD") ?? string.Empty;
        _apiVersion = Environment.GetEnvironmentVariable("SUBSONIC_API_VERSION") ?? string.Empty;
        _client = Environment.GetEnvironmentVariable("SUBSONIC_CLIENT") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_subsonicUrl))
            Console.WriteLine("Warning: Environment variable SUBSONIC_URL is not set.");
        if (string.IsNullOrWhiteSpace(_defaultUser))
            Console.WriteLine("Warning: Environment variable SUBSONIC_USER is not set.");
        if (string.IsNullOrWhiteSpace(_defaultPassword))
            Console.WriteLine("Warning: Environment variable SUBSONIC_PASSWORD is not set.");
        if (string.IsNullOrWhiteSpace(_apiVersion))
            Console.WriteLine("Warning: Environment variable SUBSONIC_API_VERSION is not set.");
        if (string.IsNullOrWhiteSpace(_client))
            Console.WriteLine("Warning: Environment variable SUBSONIC_CLIENT is not set.");
    }

    // Extract all parameters (query + body)
    private async Task<Dictionary<string, string>> ExtractAllParameters()
    {
        var parameters = new Dictionary<string, string>();

        // Default parameters
        parameters["u"] = _defaultUser;
        parameters["p"] = _defaultPassword;

        // Get query parameters
        foreach (var query in Request.Query)
        {
            parameters[query.Key] = query.Value.ToString();
        }

        // Get body parameters (JSON)
        if (Request.ContentLength > 0 && Request.ContentType?.Contains("application/json") == true)
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var bodyParams = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                    foreach (var param in bodyParams)
                    {
                        parameters[param.Key] = param.Value?.ToString() ?? "";
                    }
                }
                catch (JsonException)
                {
                    
                }
            }
        }

        return parameters;
    }

    private async Task<string> RelayToSubsonic(string endpoint, Dictionary<string, string> parameters)
    {
        var query = string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var url = $"{_subsonicUrl}/{endpoint}.view?{query}&v={_apiVersion}&c={_client}&f=xml";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [HttpGet, HttpPost]
    [Route("ping")]
    public async Task<IActionResult> Ping()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("ping", parameters);
        var doc = XDocument.Parse(xml);
        var status = doc.Root?.Attribute("status")?.Value;
        return Ok(new { status });
    }

    [HttpGet, HttpPost]
    [Route("getLicense")]
    public async Task<IActionResult> GetLicense()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getLicense", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getMusicFolders")]
    public async Task<IActionResult> GetMusicFolders()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getMusicFolders", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getIndexes")]
    public async Task<IActionResult> GetIndexes()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getIndexes", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getMusicDirectory")]
    public async Task<IActionResult> GetMusicDirectory()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getMusicDirectory", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getGenres")]
    public async Task<IActionResult> GetGenres()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getGenres", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getArtists")]
    public async Task<IActionResult> GetArtists()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getArtists", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getArtist")]
    public async Task<IActionResult> GetArtist()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getArtist", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getAlbum")]
    public async Task<IActionResult> GetAlbum()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getAlbum", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getSong")]
    public async Task<IActionResult> GetSong()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getSong", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("search3")]
    public async Task<IActionResult> Search3()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("search3", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getPlaylists")]
    public async Task<IActionResult> GetPlaylists()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getPlaylists", parameters);
        return Content(xml, "application/xml");
    }

    [HttpGet, HttpPost]
    [Route("getPlaylist")]
    public async Task<IActionResult> GetPlaylist()
    {
        var parameters = await ExtractAllParameters();
        var xml = await RelayToSubsonic("getPlaylist", parameters);
        return Content(xml, "application/xml");
    }

    // Generic endpoint to handle all subsonic API calls
    [HttpGet, HttpPost]
    [Route("{endpoint}")]
    public async Task<IActionResult> GenericEndpoint(string endpoint)
    {
        var parameters = await ExtractAllParameters();
        try
        {
            var xml = await RelayToSubsonic(endpoint, parameters);
            return Content(xml, "application/xml");
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { error = $"Error while calling Subsonic: {ex.Message}" });
        }
    }
}