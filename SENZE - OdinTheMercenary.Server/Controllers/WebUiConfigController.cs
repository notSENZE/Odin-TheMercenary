using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using System.Reflection;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Controllers;

[Injectable(InjectionType.Singleton)]
public sealed class WebUiConfigController
{
    public WebUiConfig Config { get; private set; } = new();

    private readonly ModHelper _modHelper;
    private readonly string _modRoot;
    private readonly string _configPath;

    public WebUiConfigController(ModHelper modHelper)
    {
        _modHelper = modHelper;
        _modRoot = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _configPath = Path.Join("config", "webui.jsonc");

        // If missing, ModHelper should throw; so we create a default file on first run.
        var absPath = GetConfigAbsolutePath();
        if (!File.Exists(absPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(Config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(absPath, json);
        }

        Config = _modHelper.GetJsonDataFromFile<WebUiConfig>(_modRoot, _configPath);
    }

    public string GetConfigAbsolutePath() => Path.Join(_modRoot, _configPath);
    public string GetModRoot() => _modRoot;
}
