using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Utils;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 94000)]
public sealed class OdinBotInventoryMiddleware : StaticRouter
{
    private const string OdinRole = "mercenary";
    private const string ReckoningAmmoTpl = "6ae7492cc52f2678713f2170";
    private const int SecureAmmoStackSize = 300;
    private const int SecureAmmoStackCount = 2;

    public OdinBotInventoryMiddleware(
        JsonUtil jsonUtil,
        MercenaryLogger logger,
        RewardLootConfigController rewardLootConfigController,
        RandomUtil randomUtil,
        WeightedRandomHelper weightedRandomHelper
    ) : base(jsonUtil, GetRoutes(logger, rewardLootConfigController, randomUtil, weightedRandomHelper))
    {
    }

    private static List<RouteAction> GetRoutes(
        MercenaryLogger logger,
        RewardLootConfigController rewardLootConfigController,
        RandomUtil randomUtil,
        WeightedRandomHelper weightedRandomHelper
    )
    {
        return
        [
            new RouteAction(
                "/client/game/bot/generate",
                (url, info, sessionId, output, cancellationToken) =>
                {
                    if (info is not GenerateBotsRequestData request ||
                        request.Conditions?.Any(condition =>
                            string.Equals(condition.Role, OdinRole, StringComparison.OrdinalIgnoreCase)) != true)
                    {
                        return new ValueTask<object>(output ?? string.Empty);
                    }

                    var originalOutput = output?.ToString() ?? string.Empty;

                    try
                    {
                        var root = JsonNode.Parse(originalOutput);
                        var bots = root?["data"] as JsonArray;

                        if (bots == null)
                            return new ValueTask<object>(output ?? string.Empty);

                        foreach (var botNode in bots)
                        {
                            if (botNode is not JsonObject bot ||
                                !string.Equals(
                                    bot["Info"]?["Settings"]?["Role"]?.GetValue<string>(),
                                    OdinRole,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var secureContainer = LimitSecureContainerAmmo(bot, logger);
                            AddRewardLoot(
                                bot,
                                secureContainer,
                                rewardLootConfigController,
                                randomUtil,
                                weightedRandomHelper,
                                logger
                            );
                        }

                        return new ValueTask<object>(root!.ToJsonString());
                    }
                    catch (JsonException ex)
                    {
                        logger.Error($"[Inventory] Could not process Odin's generated inventory: {ex.Message}");
                        return new ValueTask<object>(output ?? string.Empty);
                    }
                },
                typeof(GenerateBotsRequestData)
            )
        ];
    }

    private static (string? Id, List<JsonNode> FreeLocations) LimitSecureContainerAmmo(
        JsonObject bot,
        MercenaryLogger logger
    )
    {
        if (bot["Inventory"]?["items"] is not JsonArray items)
            return (null, []);

        var secureContainerId = items
            .OfType<JsonObject>()
            .FirstOrDefault(item => item["slotId"]?.GetValue<string>() == "SecuredContainer")?["_id"]
            ?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(secureContainerId))
            return (null, []);

        var ammoIndexes = new List<int>();

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is JsonObject item &&
                item["_tpl"]?.GetValue<string>() == ReckoningAmmoTpl &&
                item["parentId"]?.GetValue<string>() == secureContainerId)
            {
                ammoIndexes.Add(index);
            }
        }

        if (ammoIndexes.Count < SecureAmmoStackCount)
        {
            logger.Warn($"[Inventory] Odin generated with only {ammoIndexes.Count} secure RECKONING ammo stack(s).");
            return (secureContainerId, []);
        }

        foreach (var index in ammoIndexes.Take(SecureAmmoStackCount))
        {
            var item = (JsonObject)items[index]!;
            var update = item["upd"] as JsonObject ?? new JsonObject();
            update["StackObjectsCount"] = SecureAmmoStackSize;
            item["upd"] = update;
        }

        var freeLocations = ammoIndexes
            .Skip(SecureAmmoStackCount)
            .Select(index => (items[index] as JsonObject)?["location"]?.DeepClone())
            .OfType<JsonNode>()
            .ToList();

        foreach (var index in ammoIndexes.Skip(SecureAmmoStackCount).Reverse())
            items.RemoveAt(index);

        return (secureContainerId, freeLocations);
    }

    private static void AddRewardLoot(
        JsonObject bot,
        (string? Id, List<JsonNode> FreeLocations) secureContainer,
        RewardLootConfigController configController,
        RandomUtil randomUtil,
        WeightedRandomHelper weightedRandomHelper,
        MercenaryLogger logger
    )
    {
        var config = configController.Config;
        if (!config.enabled || !randomUtil.GetChance100(config.rewardChancePercent))
            return;

        if (bot["Inventory"]?["items"] is not JsonArray items ||
            string.IsNullOrWhiteSpace(secureContainer.Id) ||
            secureContainer.FreeLocations.Count == 0)
        {
            logger.Warn("[Rewards] No free secure-container location was available for Odin's reward.");
            return;
        }

        var rewardPool = config.itemWeights
            .Where(entry => entry.Value > 0 && MongoId.IsValidMongoId(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        if (rewardPool.Count == 0)
        {
            logger.Warn("[Rewards] No valid reward items with positive weights are configured.");
            return;
        }

        var rewardTpl = weightedRandomHelper.GetWeightedValue(rewardPool);
        items.Add(new JsonObject
        {
            ["_id"] = new MongoId().ToString(),
            ["_tpl"] = rewardTpl,
            ["parentId"] = secureContainer.Id,
            ["slotId"] = "main",
            ["location"] = secureContainer.FreeLocations[0].DeepClone(),
            ["upd"] = new JsonObject
            {
                ["StackObjectsCount"] = 1,
                ["SpawnedInSession"] = true
            }
        });
    }
}
