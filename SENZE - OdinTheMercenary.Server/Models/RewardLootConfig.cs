namespace TheMercenaryServer.Models;

public sealed class RewardLootConfig
{
    public bool enabled { get; set; } = true;

    public double rewardChancePercent { get; set; } = 20;

    public Dictionary<string, double> itemWeights { get; set; } = new();
}
