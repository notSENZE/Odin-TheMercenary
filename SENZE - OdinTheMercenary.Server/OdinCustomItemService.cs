using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace TheMercenaryServer;

[Injectable(InjectionType.Singleton)]
public sealed class OdinCustomItemService(
    BotTable botTable,
    TemplateTable templateTable,
    MercenaryLogger logger
)
{
    private const string BotTypeName = "mercenary";

    private static readonly HashSet<string> DistributionOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "addtoBots",
        "addtoTraders",
        "addtoStaticAmmo",
        "addtoStaticLootContainers",
        "addtostaticlootcontainer"
    };

    public async Task<IReadOnlyList<OdinItemDefinition>> RegisterItems(
        Assembly assembly,
        WTTServerCommonLib.WTTServerCommonLib wttCommon)
    {
        var modRoot = System.IO.Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve the Odin - The Mercenary mod folder.");
        var customItemsPath = System.IO.Path.Combine(modRoot, "db", "CustomItems");

        if (!Directory.Exists(customItemsPath))
        {
            logger.Info("No db/CustomItems folder found. Skipping Odin's custom items.");
            return Array.Empty<OdinItemDefinition>();
        }

        var definitions = ReadDefinitions(customItemsPath);
        if (definitions.Count == 0)
        {
            logger.Info("No custom item JSON files found for Odin.");
            return definitions;
        }

        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
        logger.Info($"Registered {definitions.Count} custom item(s) for Odin.");
        return definitions;
    }

    public void AddWeaponsToOdin(IReadOnlyList<OdinItemDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            return;
        }

        if (!botTable.Types.TryGetValue(BotTypeName, out var odin) || odin is null)
        {
            throw new InvalidOperationException("Odin's bot loadout was not found after loading custom items.");
        }

        if (!odin.BotInventory.Equipment.TryGetValue(EquipmentSlots.FirstPrimaryWeapon, out var primaryWeapons))
        {
            throw new InvalidOperationException("Odin has no FirstPrimaryWeapon pool.");
        }

        var itemDatabase = templateTable.Items;
        var weaponDefinitions = new List<OdinItemDefinition>();

        foreach (var definition in definitions)
        {
            var customId = new MongoId(definition.CustomItemId);
            if (!itemDatabase.TryGetValue(customId, out var item) || item?.Properties is null)
            {
                throw new InvalidOperationException(
                    $"Custom item '{definition.CustomItemId}' from '{definition.SourceFile}' was not registered.");
            }

            if (!string.IsNullOrWhiteSpace(item.Properties.WeapClass))
            {
                weaponDefinitions.Add(definition);
            }
        }

        var added = 0;
        foreach (var group in weaponDefinitions.GroupBy(x => x.BaseItemId, StringComparer.OrdinalIgnoreCase))
        {
            var unequipped = group
                .Where(definition => !primaryWeapons.ContainsKey(new MongoId(definition.CustomItemId)))
                .ToArray();

            if (unequipped.Length == 0)
            {
                logger.Info("Custom weapon is already configured directly in Odin's loadout.");
                continue;
            }

            var baseId = new MongoId(group.Key);
            if (!primaryWeapons.TryGetValue(baseId, out var weight))
            {
                logger.Warn(
                    $"Custom weapon clone(s) of '{group.Key}' were registered, but the base weapon is not in Odin's primary weapon pool. " +
                    "They were not equipped.");
                continue;
            }

            primaryWeapons.Remove(baseId);
            odin.BotInventory.Mods.TryGetValue(baseId, out var baseModPool);

            foreach (var definition in unequipped)
            {
                var customId = new MongoId(definition.CustomItemId);
                primaryWeapons[customId] = weight;

                if (baseModPool is not null)
                {
                    odin.BotInventory.Mods[customId] = CloneModPool(baseModPool);
                }

                added++;
            }
        }

        if (added > 0)
        {
            logger.Info($"Added {added} exclusive custom weapon(s) to Odin's loadout.");
        }
    }

    private static List<OdinItemDefinition> ReadDefinitions(string customItemsPath)
    {
        var definitions = new List<OdinItemDefinition>();
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var files = Directory
            .EnumerateFiles(customItemsPath, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            using var document = ParseFile(file);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Custom item file '{file}' must contain a JSON object.");
            }

            foreach (var itemProperty in document.RootElement.EnumerateObject())
            {
                if (itemProperty.Value.ValueKind != JsonValueKind.Object
                    || !TryGetProperty(itemProperty.Value, "itemTplToClone", out var baseItemProperty))
                {
                    continue;
                }

                var baseItemId = baseItemProperty.GetString();
                if (string.IsNullOrWhiteSpace(baseItemId))
                {
                    throw new InvalidOperationException(
                        $"Custom item '{itemProperty.Name}' in '{file}' has no valid itemTplToClone.");
                }

                if (!knownIds.Add(itemProperty.Name))
                {
                    throw new InvalidOperationException($"Custom item ID '{itemProperty.Name}' is defined more than once.");
                }

                EnsureOdinExclusive(itemProperty.Name, itemProperty.Value, file);
                definitions.Add(new OdinItemDefinition(
                    itemProperty.Name,
                    baseItemId,
                    System.IO.Path.GetFileName(file)));
            }
        }

        return definitions;
    }

    private static JsonDocument ParseFile(string file)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Could not read custom item file '{file}': {exception.Message}", exception);
        }
    }

    private static void EnsureOdinExclusive(string customItemId, JsonElement item, string file)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (DistributionOptions.Contains(property.Name)
                && property.Value.ValueKind == JsonValueKind.True)
            {
                throw new InvalidOperationException(
                    $"Custom item '{customItemId}' in '{file}' enables '{property.Name}'. " +
                    "Odin items must not be distributed to traders, general bots or world loot. Set it to false or remove it.");
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Dictionary<string, HashSet<MongoId>> CloneModPool(
        Dictionary<string, HashSet<MongoId>> source)
    {
        return source.ToDictionary(
            entry => entry.Key,
            entry => new HashSet<MongoId>(entry.Value),
            StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record OdinItemDefinition(
    string CustomItemId,
    string BaseItemId,
    string SourceFile);
