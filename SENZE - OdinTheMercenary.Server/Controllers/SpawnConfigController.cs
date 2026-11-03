using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using System.Reflection;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class SpawnConfigController
{
    public SpawnConfig Config { get; private set; } = new();

    private readonly string _modRoot;
    private readonly string _configPath;

    private readonly ModHelper _modHelper;

    public SpawnConfigController(ModHelper modHelper)
    {
        _modHelper = modHelper;

        _modRoot = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _configPath = Path.Join("config", "spawn.jsonc");
        Config = _modHelper.GetJsonDataFromFile<SpawnConfig>(_modRoot, _configPath);
        Normalize(Config);
    }


    public string GetConfigAbsolutePath() => Path.Join(_modRoot, _configPath);

    public void ReloadFromDisk()
    {
        Config = _modHelper.GetJsonDataFromFile<SpawnConfig>(_modRoot, _configPath);
        Normalize(Config);
    }

    public void SaveToDisk(SpawnConfig newConfig)
    {
        Normalize(newConfig);

        // Keep it simple: write JSON (no comments). JSONC parser accepts plain JSON.
        var absPath = GetConfigAbsolutePath();
        var dir = Path.GetDirectoryName(absPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = System.Text.Json.JsonSerializer.Serialize(newConfig, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(absPath, json);
        Config = newConfig;
    }

    private static void Normalize(SpawnConfig config)
    {
        config.huntChance = Math.Clamp(config.huntChance, 0, 100);
        config.maps ??= new Dictionary<string, MapSpawnConfig>();
    }
}
