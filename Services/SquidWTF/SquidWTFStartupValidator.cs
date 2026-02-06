using Microsoft.Extensions.Options;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Validation;

namespace octo_fiesta.Services.SquidWTF;

/// <summary>
/// Validates Monochrome API connectivity at startup
/// Uses the first available API instance to test connectivity
/// </summary>
public class SquidWTFStartupValidator : BaseStartupValidator
{
    private readonly SquidWTFSettings _settings;

    // Required headers for the Monochrome/Tidal API
    private const string ClientHeader = "x-client";
    private const string ClientValue = "BiniLossless/v3.4";

    public override string ServiceName => "Monochrome";

    public SquidWTFStartupValidator(IOptions<SquidWTFSettings> settings, HttpClient httpClient)
        : base(httpClient)
    {
        _settings = settings.Value;
    }

    public override async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var quality = _settings.Quality;
        var instances = _settings.GetApiInstances();

        WriteStatus("Monochrome API", $"{instances.Count} instances configured", ConsoleColor.Cyan);

        var qualityDisplay = string.IsNullOrWhiteSpace(quality)
            ? "HI_RES_LOSSLESS (default)"
            : quality;
        WriteStatus("Audio Quality", qualityDisplay, ConsoleColor.Cyan);

        // Test connectivity to first available instance
        try
        {
            await ValidateApiAsync(instances, cancellationToken);
            return ValidationResult.Success("Monochrome API validation completed");
        }
        catch (TaskCanceledException)
        {
            WriteStatus("Monochrome API", "TIMEOUT", ConsoleColor.Yellow);
            WriteDetail("Could not reach any API instance within timeout period");
            return ValidationResult.Failure("TIMEOUT", "Service unreachable", ConsoleColor.Yellow);
        }
        catch (HttpRequestException ex)
        {
            WriteStatus("Monochrome API", "UNREACHABLE", ConsoleColor.Yellow);
            WriteDetail(ex.Message);
            return ValidationResult.Failure("UNREACHABLE", ex.Message, ConsoleColor.Yellow);
        }
        catch (Exception ex)
        {
            WriteStatus("Monochrome API", "ERROR", ConsoleColor.Red);
            WriteDetail(ex.Message);
            return ValidationResult.Failure("ERROR", ex.Message, ConsoleColor.Red);
        }
    }

    private async Task ValidateApiAsync(IReadOnlyList<string> instances, CancellationToken cancellationToken)
    {
        // Try each instance until one works
        foreach (var baseUrl in instances)
        {
            try
            {
                var url = $"{baseUrl.TrimEnd('/')}/search/?s=test";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add(ClientHeader, ClientValue);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    WriteStatus("Monochrome API", "AVAILABLE", ConsoleColor.Green);
                    WriteDetail($"Connected to: {baseUrl}");
                    return;
                }
            }
            catch
            {
                // Try next instance
                continue;
            }
        }

        throw new HttpRequestException("All API instances are unreachable");
    }
}
