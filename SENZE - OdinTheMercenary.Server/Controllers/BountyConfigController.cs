using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using System.Reflection;
using System.Text.Json;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class BountyConfigController
{
    private readonly ModHelper _modHelper;
    private readonly string _modRoot;
    private readonly string _configPath = Path.Join("config", "bounty.jsonc");

    public BountyConfig Config { get; private set; } = new();

    public BountyConfigController(ModHelper modHelper)
    {
        _modHelper = modHelper;
        _modRoot = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        var absolutePath = Path.Join(_modRoot, _configPath);
        if (!File.Exists(absolutePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, JsonSerializer.Serialize(Config, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }

        Config = _modHelper.GetJsonDataFromFile<BountyConfig>(_modRoot, _configPath);
        Normalize(Config);
    }

    private static void Normalize(BountyConfig config)
    {
        config.maxHeat = Math.Max(1, config.maxHeat);
        config.activationThreshold = Math.Clamp(config.activationThreshold, 1, config.maxHeat);
        config.releaseThreshold = Math.Clamp(config.releaseThreshold, 0, config.activationThreshold - 1);
        config.firstFriendlyKillHeat = Math.Max(0, config.firstFriendlyKillHeat);
        config.additionalFriendlyKillHeat = Math.Max(0, config.additionalFriendlyKillHeat);
        config.maxHeatGainPerRaid = Math.Max(0, config.maxHeatGainPerRaid);
        config.cleanRaidReduction = Math.Max(0, config.cleanRaidReduction);
        config.cleanRaidStreakLength = Math.Max(1, config.cleanRaidStreakLength);
        config.cleanRaidStreakBonus = Math.Max(0, config.cleanRaidStreakBonus);
        config.odinKillsPlayerReduction = Math.Max(0, config.odinKillsPlayerReduction);
        config.odinKilledRespiteRaids = Math.Max(0, config.odinKilledRespiteRaids);

        if (string.IsNullOrWhiteSpace(config.huntMessage))
            config.huntMessage = "BOUNTY ACTIVE - ODIN IS HUNTING YOU.";
    }
}
