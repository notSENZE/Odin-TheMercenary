namespace TheMercenaryServer.Models;

public sealed class BountyConfig
{
    public bool enabled { get; set; } = true;
    public int maxHeat { get; set; } = 100;
    public int activationThreshold { get; set; } = 60;
    public int releaseThreshold { get; set; } = 40;
    public int firstFriendlyKillHeat { get; set; } = 1;
    public int additionalFriendlyKillHeat { get; set; } = 5;
    public int maxHeatGainPerRaid { get; set; } = 20;
    public int cleanRaidReduction { get; set; } = 4;
    public int cleanRaidStreakLength { get; set; } = 3;
    public int cleanRaidStreakBonus { get; set; } = 5;
    public int odinKillsPlayerReduction { get; set; } = 20;
    public int odinKilledRespiteRaids { get; set; } = 2;
    public string huntMessage { get; set; } = "BOUNTY ACTIVE - ODIN IS HUNTING YOU.";
}
