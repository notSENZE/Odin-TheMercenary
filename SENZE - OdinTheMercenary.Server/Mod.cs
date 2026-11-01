using MoreBotsServer.Models;
using MoreBotsServer.Interop;
using MoreBotsServer.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using System.Reflection;
using TheMercenaryServer.Compat;
namespace TheMercenaryServer;

public sealed class ModMetadata : IModMetadata
{
    public const string VersionNumber = "1.0.0";

    public string ModGuid { get; init; } = "com.senze.odinthemercenary";
    public string Name { get; init; } = "Odin - The Mercenary";
    public string Author { get; init; } = "Senze";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(VersionNumber);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        { "com.morebotsapi.tacticaltoaster", new SemanticVersioning.Range(">=2.1.1 <3.0.0") },
        { "com.wtt.commonlib", new SemanticVersioning.Range(">=3.0.6 <4.0.0") },
        { "com.wtt.contentbackport", new SemanticVersioning.Range(">=2.0.5 <3.0.0") }
    };
    public string? Url { get; init; } = "https://github.com/notSENZE/Odin-TheMercenary";
    public string License { get; init; } = "All Rights Reserved";
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadBots)]
public sealed class OdinTheMercenaryServer(
    MoreBotsServer.MoreBotsAPI moreBotsApi,
    MoreBotsCustomBotTypeService customBotTypeService,
    SainInteropRegistration sainInteropRegistration,
    FactionService factionService,
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    OdinCustomItemService odinCustomItemService,
    CompatService compatService
) : IOnLoad
{
    private const int WildSpawnTypeValue = 836500;
    private const string BotTypeName = "mercenary";

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = Assembly.GetExecutingAssembly();

        var botTypes = new List<string> { BotTypeName };

        var typeNames = new Dictionary<int, string>
        {
            { WildSpawnTypeValue, BotTypeName }
        };

        await moreBotsApi.LoadBotsShared(assembly, BotTypeName, botTypes);

        var customItems = await odinCustomItemService.RegisterItems(assembly, wttCommon);
        await wttCommon.CustomBotLoadoutService.CreateCustomBotLoadouts(assembly);
        odinCustomItemService.AddWeaponsToOdin(customItems);
        await compatService.LoadAsync(assembly);
        await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);
        customBotTypeService.AddCustomWildSpawnTypeNames(typeNames);
        sainInteropRegistration.RegisterBotType(new MoreBotsSainBotTypeRegistration
        {
            Name = "ODIN",
            WildSpawnType = WildSpawnTypeValue,
            BotDbKey = BotTypeName,
            Section = "Odin - The Mercenary",
            Description = "A relentless lone mercenary who hunts players aggressively.",
            DifficultyModifier = 1.0f,
            BaseBrain = "PMC",
            BrainsToApply = ["Raider", "ExUsec"],
            LayersToRemove = ["Request", "KnightFight", "PmcBear", "PmcUsec", "ExURequest", "StationaryWS"]
        });

        // Make Mercenary neutral to bots by default:
        // Instead of defining enemies + revenge, define friendlies for both sides.
        factionService.AddFriendlyByFaction(botTypes, "savage");
        factionService.AddFriendlyByFaction(botTypes, "rogues");
        factionService.AddFriendlyByFaction(botTypes, "usec");
        factionService.AddFriendlyByFaction(botTypes, "bear");
        factionService.AddFriendlyByFaction(botTypes, "infected");

        factionService.AddFriendlyByFaction("savage", BotTypeName);
        factionService.AddFriendlyByFaction("rogues", BotTypeName);
        factionService.AddFriendlyByFaction("usec", BotTypeName);
        factionService.AddFriendlyByFaction("bear", BotTypeName);
        factionService.AddFriendlyByFaction("infected", BotTypeName);

    }
}

[Injectable(InjectionType = InjectionType.Singleton, TypePriority = MoreBotsServer.MoreBotsLoadOrder.LoadFactions)]
public sealed class OdinTheMercenaryFaction(FactionService factionService) : IOnLoad
{
    private const int WildSpawnTypeValue = 836500;
    private const string FactionName = "mercenary";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!factionService.Factions.ContainsKey(FactionName))
        {
            factionService.Factions.Add(FactionName, new Faction()
            {
                Name = FactionName,
                BotTypes = { (WildSpawnType)WildSpawnTypeValue }
            });
        }

        return Task.CompletedTask;
    }
}
