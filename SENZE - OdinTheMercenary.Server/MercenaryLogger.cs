using SPTarkov.DI.Annotations;
using SPTarkov.Common.Models.Logging;
using TheMercenaryServer.Controllers;

namespace TheMercenaryServer;

[Injectable(InjectionType.Singleton)]
public sealed class MercenaryLogger
{
    private readonly ISptLogger<MercenaryLogger> _logger;
    private readonly SpawnConfigController _config;

    public MercenaryLogger(ISptLogger<MercenaryLogger> logger, SpawnConfigController configController)
    {
        _logger = logger;
        _config = configController;
    }

    private bool EnableLogsSafe => _config?.Config?.enableLogs ?? false;

    public void Info(string message)
    {
        if (EnableLogsSafe)
            _logger.Info($"[OdinTheMercenary] {message}");
    }

    public void Warn(string message)
    {
        _logger.Warning($"[OdinTheMercenary] WARNING: {message}");
    }

    public void Error(string message)
    {
        _logger.Error($"[OdinTheMercenary] ERROR: {message}");
    }

    public void Debug(string message)
    {
        _logger.Debug($"[OdinTheMercenary] {message}");
    }
}
