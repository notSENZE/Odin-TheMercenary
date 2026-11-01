namespace TheMercenaryServer.Models;

public sealed class BountyProfileState
{
    public int heat { get; set; }
    public bool contractActive { get; set; }
    public int cleanRaidStreak { get; set; }
    public int respiteRaidsRemaining { get; set; }
    public string lastProcessedRaid { get; set; } = string.Empty;
}

public sealed class BountyRaidStatus
{
    public bool enabled { get; set; }
    public bool hasProfile { get; set; }
    public bool isPmcRaid { get; set; }
    public bool huntActive { get; set; }
    public bool forceOdinSpawn { get; set; }
    public bool contractActive { get; set; }
    public int heat { get; set; }
    public int maxHeat { get; set; }
    public int activationThreshold { get; set; }
    public int releaseThreshold { get; set; }
    public int cleanRaidStreak { get; set; }
    public int respiteRaidsRemaining { get; set; }
    public string location { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
    public string reason { get; set; } = string.Empty;
}

public sealed class BountyHeatRequest
{
    public int heat { get; set; }
}
