namespace TheMercenaryServer.Models;

public sealed class WebUiConfig
{
    public bool enabled { get; set; } = true;

    /// <summary>
    /// Default: localhost only (safer).
    /// Use "0.0.0.0" only if you know what you're doing.
    /// </summary>
    public string host { get; set; } = "127.0.0.1";

    public int port { get; set; } = 6969;

    /// <summary>
    /// If true, allow requests from non-localhost clients.
    /// Keep false by default.
    /// </summary>
    public bool allowRemote { get; set; } = false;
}
