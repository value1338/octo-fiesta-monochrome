namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the SquidWTF music provider
/// SquidWTF is a music downloader service that supports Qobuz and Tidal backends
/// </summary>
public class SquidWTFSettings
{
    /// <summary>
    /// Tidal API URLs from instances.json that are tried <b>after</b> all others (403/timeouts from many servers).
    /// <c>null</c>: use <see cref="DefaultDeprioritizedTidalApiInstances"/>; empty: no reordering.
    /// </summary>
    public List<string>? DeprioritizedTidalApiInstances { get; set; }

    public static readonly string[] DefaultDeprioritizedTidalApiInstances =
    [
        "https://eu-central.monochrome.tf",
        "https://us-west.monochrome.tf",
        "https://arran.monochrome.tf",
        "https://api.monochrome.tf",
        "https://monochrome-api.samidy.com",
        "https://triton.squid.wtf",
        "https://wolf.qqdl.site",
        "https://maus.qqdl.site",
        "https://vogel.qqdl.site",
    ];

    /// <summary>
    /// Tidal API URLs to drop entirely after loading instances.json (optional).
    /// <c>null</c> or empty: do not remove any URL beyond deprioritization.
    /// </summary>
    public List<string>? BlockedTidalApiInstances { get; set; }

    /// <summary>
    /// The backend source to use: "Qobuz" or "Tidal"
    /// Defaults to "Qobuz" if not specified
    /// </summary>
    public string Source { get; set; } = "Qobuz";
    
    /// <summary>
    /// Preferred audio quality
    /// For Qobuz: 27 (FLAC 24-bit/192kHz), 7 (FLAC 24-bit/96kHz), 6 (FLAC 16-bit), 5 (MP3 320kbps)
    /// For Tidal: HI_RES_LOSSLESS (FLAC 24-bit), LOSSLESS (FLAC 16-bit), HIGH (320kbps AAC), LOW (96kbps AAC)
    /// If not specified, highest quality will be used
    /// </summary>
    public string? Quality { get; set; }
    
    /// <summary>
    /// Timeout in seconds for API instance requests before switching to next instance
    /// Only applies to Tidal source (monochrome.tf instances). Defaults to 5 seconds if not specified.
    /// </summary>
    public int InstanceTimeoutSeconds { get; set; } = 5;
}
