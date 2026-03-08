namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the SquidWTF music provider
/// SquidWTF is a music downloader service that supports Qobuz and Tidal backends
/// </summary>
public class SquidWTFSettings
{
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
    /// Only applies to Tidal source and Monochrome Qobuz backend. Defaults to 5 seconds if not specified.
    /// </summary>
    public int InstanceTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Qobuz backend API to use when Source is "Qobuz"
    /// "squidwtf" (default) = qobuz.squid.wtf (fixed URL, no failover)
    /// "monochrome" = monochrome.tf instances (dynamic URL list, with automatic failover)
    /// Environment variable: SQUIDWTF_QOBUZ_BACKEND
    /// </summary>
    public string QobuzBackend { get; set; } = "squidwtf";
}
