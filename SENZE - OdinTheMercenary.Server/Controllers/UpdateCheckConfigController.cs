using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using System.Reflection;
using System.Text.Json;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class UpdateCheckConfigController
{
    public UpdateCheckConfig Config { get; }

    public UpdateCheckConfigController(ModHelper modHelper)
    {
        var modRoot = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Join("config", "update.jsonc");
        var absolutePath = Path.Join(modRoot, configPath);

        if (!File.Exists(absolutePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, JsonSerializer.Serialize(new UpdateCheckConfig(), new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }

        Config = modHelper.GetJsonDataFromFile<UpdateCheckConfig>(modRoot, configPath);
        Config.timeoutSeconds = Math.Clamp(Config.timeoutSeconds, 1, 30);
    }
}
