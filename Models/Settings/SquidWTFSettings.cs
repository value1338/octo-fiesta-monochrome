namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the Monochrome music provider
/// Uses the Monochrome/Hi-Fi API for Tidal music streaming
/// No authentication required - works without login!
/// </summary>
public class SquidWTFSettings
{
    /// <summary>
    /// Preferred audio quality: "HI_RES_LOSSLESS" (24-bit FLAC) or "LOSSLESS" (16-bit FLAC)
    /// If not specified, HI_RES_LOSSLESS will be used
    /// </summary>
    public string Quality { get; set; } = "HI_RES_LOSSLESS";

    /// <summary>
    /// API instances for metadata/search operations
    /// Multiple instances enable automatic failover when one is unavailable or rate-limited
    /// If empty, default Monochrome instances are used
    /// </summary>
    public List<string> ApiInstances { get; set; } = new();

    /// <summary>
    /// API instances for streaming/download operations
    /// Usually same as ApiInstances but may differ for some providers
    /// If empty, ApiInstances are used as fallback
    /// </summary>
    public List<string> StreamingInstances { get; set; } = new();

    /// <summary>
    /// Default Monochrome API instances for metadata operations
    /// Used when ApiInstances is empty
    /// Includes SquidWTF Tidal instances as fallback
    /// </summary>
    public static readonly string[] DefaultApiInstances =
    [
        "https://arran.monochrome.tf",
        "https://api.monochrome.tf",
        "https://triton.squid.wtf",
        "https://monochrome-api.samidy.com",
        "https://wolf.qqdl.site",
        "https://hifi-one.spotisaver.net",
        "https://hifi-two.spotisaver.net",
        "https://tidal.kinoplus.online",
        // SquidWTF Tidal instances (no auth required)
        "https://tidal-api.binimum.org"
    ];

    /// <summary>
    /// Default Monochrome instances for streaming operations
    /// Used when StreamingInstances is empty
    /// Includes SquidWTF Tidal instances as fallback
    /// </summary>
    public static readonly string[] DefaultStreamingInstances =
    [
        "https://arran.monochrome.tf",
        "https://api.monochrome.tf",
        "https://triton.squid.wtf",
        "https://wolf.qqdl.site",
        "https://katze.qqdl.site",
        "https://hund.qqdl.site",
        "https://tidal.kinoplus.online",
        "https://hifi-one.spotisaver.net",
        "https://hifi-two.spotisaver.net",
        // SquidWTF Tidal instances (no auth required)
        "https://tidal-api.binimum.org"
    ];

    /// <summary>
    /// Gets the effective API instances (configured or defaults)
    /// </summary>
    public IReadOnlyList<string> GetApiInstances() =>
        ApiInstances.Count > 0 ? ApiInstances : DefaultApiInstances;

    /// <summary>
    /// Gets the effective streaming instances (configured, fallback to API, or defaults)
    /// </summary>
    public IReadOnlyList<string> GetStreamingInstances() =>
        StreamingInstances.Count > 0 ? StreamingInstances :
        ApiInstances.Count > 0 ? ApiInstances : DefaultStreamingInstances;
}
