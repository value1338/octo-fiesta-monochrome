using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using octo_fiesta.Models;

namespace octo_fiesta.Services.Deezer;

/// <summary>
/// Validates Deezer ARL credentials at startup
/// </summary>
public class DeezerStartupValidator
{
    private readonly DeezerSettings _settings;
    private readonly HttpClient _httpClient;

    public DeezerStartupValidator(IOptions<DeezerSettings> settings, HttpClient httpClient)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
    }

    public async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var arl = _settings.Arl;
        var arlFallback = _settings.ArlFallback;
        var quality = _settings.Quality;

        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(arl))
        {
            WriteStatus("Deezer ARL", "NOT CONFIGURED", ConsoleColor.Red);
            WriteDetail("Set the Deezer__Arl environment variable");
            return;
        }

        WriteStatus("Deezer ARL", MaskSecret(arl), ConsoleColor.Cyan);
        
        if (!string.IsNullOrWhiteSpace(arlFallback))
        {
            WriteStatus("Deezer ARL Fallback", MaskSecret(arlFallback), ConsoleColor.Cyan);
        }

        WriteStatus("Deezer Quality", string.IsNullOrWhiteSpace(quality) ? "auto (highest available)" : quality, ConsoleColor.Cyan);

        // Validate ARL by calling Deezer API
        await ValidateArlTokenAsync(arl, "primary", cancellationToken);
        
        if (!string.IsNullOrWhiteSpace(arlFallback))
        {
            await ValidateArlTokenAsync(arlFallback, "fallback", cancellationToken);
        }
    }

    private async Task ValidateArlTokenAsync(string arl, string label, CancellationToken cancellationToken)
    {
        var fieldName = $"Deezer ARL ({label})";
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "https://www.deezer.com/ajax/gw-light.php?method=deezer.getUserData&input=3&api_version=1.0&api_token=null");
            
            request.Headers.Add("Cookie", $"arl={arl}");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                WriteStatus(fieldName, $"HTTP {(int)response.StatusCode}", ConsoleColor.Red);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.TryGetProperty("USER", out var user))
            {
                if (user.TryGetProperty("USER_ID", out var userId))
                {
                    var userIdValue = userId.ValueKind == JsonValueKind.Number 
                        ? userId.GetInt64() 
                        : long.TryParse(userId.GetString(), out var parsed) ? parsed : 0;

                    if (userIdValue > 0)
                    {
                        // BLOG_NAME is the username displayed on Deezer
                        var userName = user.TryGetProperty("BLOG_NAME", out var blogName) && blogName.GetString() is string bn && !string.IsNullOrEmpty(bn)
                            ? bn
                            : user.TryGetProperty("NAME", out var name) && name.GetString() is string n && !string.IsNullOrEmpty(n)
                                ? n
                                : "Unknown";
                        
                        var offerName = GetOfferName(user);
                        
                        WriteStatus(fieldName, "VALID", ConsoleColor.Green);
                        WriteDetail($"Logged in as {userName} ({offerName})");
                        return;
                    }
                }
                
                WriteStatus(fieldName, "INVALID", ConsoleColor.Red);
                WriteDetail("Token is expired or invalid");
            }
            else
            {
                WriteStatus(fieldName, "INVALID", ConsoleColor.Red);
                WriteDetail("Unexpected response from Deezer");
            }
        }
        catch (TaskCanceledException)
        {
            WriteStatus(fieldName, "TIMEOUT", ConsoleColor.Yellow);
            WriteDetail("Could not reach Deezer within 10 seconds");
        }
        catch (HttpRequestException ex)
        {
            WriteStatus(fieldName, "UNREACHABLE", ConsoleColor.Yellow);
            WriteDetail(ex.Message);
        }
        catch (Exception ex)
        {
            WriteStatus(fieldName, "ERROR", ConsoleColor.Red);
            WriteDetail(ex.Message);
        }
    }

    private static string GetOfferName(JsonElement user)
    {
        if (!user.TryGetProperty("OPTIONS", out var options))
        {
            return "Free";
        }

        // Check actual streaming capabilities, not just license_token presence
        var hasLossless = options.TryGetProperty("web_lossless", out var webLossless) && webLossless.GetBoolean();
        var hasHq = options.TryGetProperty("web_hq", out var webHq) && webHq.GetBoolean();

        if (hasLossless)
        {
            return "Premium+ (Lossless)";
        }
        
        if (hasHq)
        {
            return "Premium (HQ)";
        }
        
        return "Free";
    }

    private static void WriteStatus(string label, string value, ConsoleColor valueColor)
    {
        Console.Write($"  {label}: ");
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = valueColor;
        Console.WriteLine(value);
        Console.ForegroundColor = originalColor;
    }

    private static void WriteDetail(string message)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"    -> {message}");
        Console.ForegroundColor = originalColor;
    }

    private static string MaskSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "(empty)";
        }

        const int visibleChars = 4;
        if (secret.Length <= visibleChars)
        {
            return new string('*', secret.Length);
        }

        return secret[..visibleChars] + new string('*', Math.Min(secret.Length - visibleChars, 8));
    }
}
