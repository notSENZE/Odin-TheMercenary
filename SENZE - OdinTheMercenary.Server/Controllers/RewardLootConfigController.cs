using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class RewardLootConfigController
{
    public RewardLootConfig Config { get; }

    public RewardLootConfigController(ModHelper modHelper)
    {
        var modRoot = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        Config = modHelper.GetJsonDataFromFile<RewardLootConfig>(modRoot, Path.Join("config", "rewards.jsonc"));
    }
}
