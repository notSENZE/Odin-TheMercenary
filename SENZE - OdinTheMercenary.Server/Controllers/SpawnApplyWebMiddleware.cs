using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using System.Text.Json;

namespace TheMercenaryServer.Controllers;

/// <summary>
/// Re-applies Odin's spawn data before raid start and exposes client hunt settings.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 95000)]
public sealed class SpawnApplyWebMiddleware : StaticRouter
{
    public SpawnApplyWebMiddleware(
        JsonUtil jsonUtil,
        SpawnController spawnController,
        SpawnConfigController spawnConfig,
        MercenaryLogger logger
    ) : base(jsonUtil, GetRoutes(spawnController, spawnConfig, logger))
    {
        logger.Info("[Spawn] Raid middleware active for spawn compatibility and hunt settings.");
    }

    private static List<RouteAction> GetRoutes(
        SpawnController spawnController,
        SpawnConfigController spawnConfig,
        MercenaryLogger logger
    )
    {
        return
        [
            new RouteAction(
                "/client/match/local/start",
                async (url, info, sessionId, output, cancellationToken) =>
                {
                    try
                    {
                        spawnController.ApplySpawnConfig(force: true);
                        logger.Info("[Spawn] Re-applied spawn data on raid start.");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"[Spawn] Forced re-apply on raid start failed: {ex}");
                    }

                    await Task.CompletedTask.ConfigureAwait(false);
                    return output ?? string.Empty;
                },
                typeof(StartLocalRaidRequestData)
            ),
            new RouteAction(
                "/odin-the-mercenary/hunt/settings",
                async (url, info, sessionId, output, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.CompletedTask.ConfigureAwait(false);
                    return JsonSerializer.Serialize(new
                    {
                        huntChance = Math.Clamp(spawnConfig.Config.huntChance, 0, 100)
                    });
                },
                typeof(EmptyRequestData)
            )
        ];
    }
}
