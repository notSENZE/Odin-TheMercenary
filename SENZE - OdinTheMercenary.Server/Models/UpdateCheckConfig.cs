namespace TheMercenaryServer.Models;

public sealed class UpdateCheckConfig
{
    public bool enabled { get; set; } = true;
    public int timeoutSeconds { get; set; } = 5;
}
