using Mono.Cecil;
using MoreBotsAPI;
using System.Collections.Generic;

namespace TheMercenary.Prepatch;

public static class WildSpawnTypePatch
{
    public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

    public static void Patch(ref AssemblyDefinition assembly)
    {
        var brainsToApply = new List<string> { "Raider", "ExUsec" };
        var layersToRemove = new List<string>
        {
            "Request",
            "KnightFight",
            "PmcBear",
            "PmcUsec",
            "ExURequest",
            "StationaryWS"
        };

        var baseBrainInt = 24;

        var bot = new CustomWildSpawnType(
            value: 836500,
            name: "mercenary",
            scavRole: "Mercenary",
            baseBrain: baseBrainInt,
            isBoss: true,
            isFollower: false,
            isHostileToEverybody: false
        );

        bot.SetCountAsBossForStatistics(true);
        bot.SetShouldUseFenceNoBossAttack(false, false);
        bot.SetExcludedDifficulties(new List<int> { 0, 2, 3 });

        var sain = new SAINSettings(bot.WildSpawnTypeValue)
        {
            Name = "ODIN",
            Description = "A relentless lone mercenary who hunts players aggressively.",
            Section = "Odin - The Mercenary",
            BaseBrain = "PMC",
            BrainsToApply = brainsToApply,
            LayersToRemove = layersToRemove,
            DifficultyModifier = 1.0f
        };

        bot.SetSAINSettings(sain);

        CustomWildSpawnTypeManager.RegisterWildSpawnType(bot, assembly);
    }
}
