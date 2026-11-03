// Pfad: Server/Controllers/SpawnController.cs

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class SpawnController(
    LocationTable locationTable,
    SpawnConfigController spawnConfigController,
    MercenaryLogger logger
)
{
    private bool _applied;

    public void ApplySpawnConfig(bool force = false)
    {
        if (_applied && !force)
            return;

        _applied = true;

        var cfg = spawnConfigController.Config;
        var locDict = locationTable.GetDictionary();

        foreach (var kvp in cfg.maps)
        {
            var locationKeyFromConfig = kvp.Key;
            var mapCfg = kvp.Value;

            if (!TryFindLocation(locDict, locationKeyFromConfig, out var loc))
            {
                if (mapCfg.enabled)
                    LogNotFoundWithHints(locationKeyFromConfig, locDict);
                continue;
            }

            if (loc?.Base == null)
            {
                if (mapCfg.enabled)
                    logger.Info($"[Spawn] Map key '{locationKeyFromConfig}' found but has no Base in Locations DB. Skipping.");
                continue;
            }

            // ABPS / Spawnmods may null this out -> create it so we can inject our entry.
            loc.Base.BossLocationSpawn ??= new List<BossLocationSpawn>();

            // Remove any old mercenary entries (always)
            loc.Base.BossLocationSpawn.RemoveAll(x =>
                string.Equals(x.BossName, "mercenary", StringComparison.OrdinalIgnoreCase));

            if (!mapCfg.enabled)
                continue;

            var chance = ClampChance(mapCfg.chance);
            if (chance <= 0)
            {
                logger.Info($"[Spawn] '{locationKeyFromConfig}' enabled but chance is 0%. Skipping.");
                continue;
            }

            var enabledZones = GetEnabledZones(locationKeyFromConfig, mapCfg, loc);
            if (enabledZones.Count == 0)
            {
                logger.Info($"[Spawn] '{locationKeyFromConfig}' enabled but has no effective zones (zones - disabledZones is empty). Skipping.");
                continue;
            }

            loc.Base.BossLocationSpawn.Add(new BossLocationSpawn
            {
                BossName = "mercenary",
                BossChance = chance,
                BossDifficulty = "normal",

                // IMPORTANT: cannot be empty string (client Enum.Parse crash)
                BossEscortType = "followerBully",
                BossEscortAmount = "0",
                BossEscortDifficulty = "normal",

                IsBossPlayer = false,

                // BossZone supports comma-separated values
                BossZone = string.Join(",", enabledZones),

                Delay = 0,
                ForceSpawn = false,
                IgnoreMaxBots = true,
                IsRandomTimeSpawn = false,

                SpawnMode = ["regular"],
                Supports = [],

                Time = -1,
                TriggerId = "",
                TriggerName = ""
            });
        }

        logger.Info(force ? "Spawn config applied (forced)." : "Spawn config applied.");
    }

    private static int ClampChance(int v) => v < 0 ? 0 : (v > 100 ? 100 : v);

    private static bool TryFindLocation(
        Dictionary<string, Location> locDict,
        string locationKey,
        out Location? loc
    )
    {
        if (locDict.TryGetValue(locationKey, out loc))
            return loc != null;

        var foundKey = locDict.Keys.FirstOrDefault(k =>
            string.Equals(k, locationKey, StringComparison.OrdinalIgnoreCase));

        if (foundKey != null)
        {
            loc = locDict[foundKey];
            return loc != null;
        }

        loc = null;
        return false;
    }

    private void LogNotFoundWithHints(string requestedKey, Dictionary<string, Location> locDict)
    {
        logger.Info($"[Spawn] Map key '{requestedKey}' not found in Locations DB. Skipping.");

        // Helpful hints without dumping the whole DB
        var token = requestedKey;
        var idx = requestedKey.IndexOf('_');
        if (idx > 0)
            token = requestedKey[..idx];

        var hints = locDict.Keys
            .Where(k => k.Contains(token, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (hints.Count > 0)
            logger.Info($"[Spawn] Similar keys for '{requestedKey}' (contains '{token}'): {string.Join(", ", hints)}");
    }

    private List<string> GetEnabledZones(string locationKeyFromConfig, MapSpawnConfig mapCfg, Location loc)
    {
        // Prefer config zones (authoritative).
        // If empty (broken config from old UI), derive from DB.
        var all = (mapCfg.zones ?? new List<string>())
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Select(z => z.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (all.Count == 0)
        {
            all = DeriveZonesFromDb(loc);
            if (all.Count > 0)
                logger.Info($"[Spawn] '{locationKeyFromConfig}' has empty zones in config - using derived DB zones ({all.Count}).");
        }

        if (all.Count == 0)
            return new List<string>();

        var disabled = new HashSet<string>(
            (mapCfg.disabledZones ?? new List<string>())
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Select(z => z.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return all.Where(z => !disabled.Contains(z)).ToList();
    }

    private static List<string> DeriveZonesFromDb(Location loc)
    {
        var list = new List<string>();
        var spawns = loc.Base?.BossLocationSpawn;
        if (spawns == null || spawns.Count == 0)
            return list;

        foreach (var s in spawns)
        {
            var bz = s?.BossZone;
            if (string.IsNullOrWhiteSpace(bz))
                continue;

            foreach (var part in bz.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    list.Add(part.Trim());
            }
        }

        return list
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
