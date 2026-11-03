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
    private const string GungnirWeaponTpl = "6895bb82c4519957df062f82";
    private const string GungnirMagazineTpl = "59c1383d86f774290a37e0ca";
    private const string ReckoningAmmoTpl = "6ae7492cc52f2678713f2170";
    private const string AllvaterWeaponTpl = "6aea113c1edb080a9b5d5a9f";
    private const string AllvaterMagazineTpl = "5ac66c5d5acfc4001718d314";
    private const string CbjAmmoTpl = "64b8725c4b75259c590fa899";

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

                            var primaryWeapon = FindPrimaryWeapon(bot);
                            if (primaryWeapon == null)
                            {
                                logger.Warn("[Inventory] Odin generated without a primary weapon.");
                                continue;
                            }

                            var weaponTpl = primaryWeapon["_tpl"]?.GetValue<string>();
                            var weaponId = primaryWeapon["_id"]?.GetValue<string>();

                            var loadout = weaponTpl switch
                            {
                                GungnirWeaponTpl => (
                                    MagazineTpl: GungnirMagazineTpl,
                                    SpareMagazineCount: 3,
                                    AmmoTpl: ReckoningAmmoTpl,
                                    AmmoStackSize: 300,
                                    AmmoStackCount: 2),
                                AllvaterWeaponTpl => (
                                    MagazineTpl: AllvaterMagazineTpl,
                                    SpareMagazineCount: 4,
                                    AmmoTpl: CbjAmmoTpl,
                                    AmmoStackSize: 60,
                                    AmmoStackCount: 10),
                                _ => default
                            };

                            if (string.IsNullOrWhiteSpace(loadout.MagazineTpl) || string.IsNullOrWhiteSpace(weaponId))
                            {
                                logger.Warn($"[Inventory] Odin generated with unsupported primary weapon '{weaponTpl}'.");
                                continue;
                            }

                            LimitSpareMagazines(
                                bot,
                                weaponId,
                                loadout.MagazineTpl,
                                loadout.SpareMagazineCount,
                                logger
                            );

                            var secureContainer = LimitSecureContainerAmmo(
                                bot,
                                loadout.AmmoTpl,
                                loadout.AmmoStackSize,
                                loadout.AmmoStackCount,
                                logger
                            );
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

    private static JsonObject? FindPrimaryWeapon(JsonObject bot)
    {
        if (bot["Inventory"]?["items"] is not JsonArray items)
            return null;

        return items
            .OfType<JsonObject>()
            .FirstOrDefault(item => item["slotId"]?.GetValue<string>() == "FirstPrimaryWeapon");
    }

    private static void LimitSpareMagazines(
        JsonObject bot,
        string weaponId,
        string magazineTpl,
        int desiredCount,
        MercenaryLogger logger
    )
    {
        if (bot["Inventory"]?["items"] is not JsonArray items)
            return;

        var spareMagazineIds = items
            .OfType<JsonObject>()
            .Where(item =>
                item["_tpl"]?.GetValue<string>() == magazineTpl &&
                item["parentId"]?.GetValue<string>() != weaponId)
            .Select(item => item["_id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();

        if (spareMagazineIds.Count < desiredCount)
        {
            logger.Warn(
                $"[Inventory] Odin generated with only {spareMagazineIds.Count}/{desiredCount} spare magazine(s) for '{magazineTpl}'."
            );
            return;
        }

        var itemIdsToRemove = new HashSet<string>(spareMagazineIds.Skip(desiredCount));
        if (itemIdsToRemove.Count == 0)
            return;

        var foundChild = true;
        while (foundChild)
        {
            foundChild = false;
            foreach (var item in items.OfType<JsonObject>())
            {
                var parentId = item["parentId"]?.GetValue<string>();
                var itemId = item["_id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(parentId) || string.IsNullOrWhiteSpace(itemId))
                    continue;

                if (itemIdsToRemove.Contains(parentId) && itemIdsToRemove.Add(itemId))
                    foundChild = true;
            }
        }

        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (items[index] is JsonObject item &&
                item["_id"]?.GetValue<string>() is string itemId &&
                itemIdsToRemove.Contains(itemId))
            {
                items.RemoveAt(index);
            }
        }
    }

    private static (string? Id, List<JsonNode> FreeLocations) LimitSecureContainerAmmo(
        JsonObject bot,
        string ammoTpl,
        int ammoStackSize,
        int ammoStackCount,
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
                item["_tpl"]?.GetValue<string>() == ammoTpl &&
                item["parentId"]?.GetValue<string>() == secureContainerId)
            {
                ammoIndexes.Add(index);
            }
        }

        if (ammoIndexes.Count < ammoStackCount)
        {
            logger.Warn(
                $"[Inventory] Odin generated with only {ammoIndexes.Count}/{ammoStackCount} secure ammo stack(s) for '{ammoTpl}'."
            );
            return (secureContainerId, []);
        }

        foreach (var index in ammoIndexes.Take(ammoStackCount))
        {
            var item = (JsonObject)items[index]!;
            var update = item["upd"] as JsonObject ?? new JsonObject();
            update["StackObjectsCount"] = ammoStackSize;
            item["upd"] = update;
        }

        var freeLocations = ammoIndexes
            .Skip(ammoStackCount)
            .Select(index => (items[index] as JsonObject)?["location"]?.DeepClone())
            .OfType<JsonNode>()
            .ToList();

        foreach (var index in ammoIndexes.Skip(ammoStackCount).Reverse())
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
