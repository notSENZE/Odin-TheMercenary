using System.Collections.Generic;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using System.Text.Json;
using TheMercenaryServer.Services;

namespace TheMercenaryServer.Controllers;

/// <summary>
/// ABPS compatibility: re-apply mercenary BossLocationSpawn right before raid start.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostLoad + 95000)]
public sealed class SpawnApplyWebMiddleware : StaticRouter
{
    public SpawnApplyWebMiddleware(
        JsonUtil jsonUtil,
        SpawnController spawnController,
        BountyService bountyService,
        MercenaryLogger logger
    ) : base(jsonUtil, GetRoutes(spawnController, bountyService, logger))
    {
        logger.Info("[Spawn] Raid middleware active for spawn compatibility and Odin's bounty system.");
    }

    private static List<RouteAction> GetRoutes(
        SpawnController spawnController,
        BountyService bountyService,
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
                        var bountyStatus = info is StartLocalRaidRequestData request
                            ? await bountyService.PrepareRaidAsync(sessionId, request, cancellationToken)
                            : await bountyService.GetStatusAsync(cancellationToken);

                        spawnController.ApplySpawnConfig(
                            force: true,
                            forceBountySpawn: bountyStatus.forceOdinSpawn
                        );
                        logger.Info("[Spawn] Re-applied spawn data on raid start.");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (System.Exception ex)
                    {
                        logger.Error($"[Spawn] Forced re-apply on raid start failed: {ex}");
                    }

                    return output ?? string.Empty;
                },
                typeof(StartLocalRaidRequestData)
            ),
            new RouteAction(
                "/client/match/local/end",
                async (url, info, sessionId, output, cancellationToken) =>
                {
                    try
                    {
                        if (info is EndLocalRaidRequestData request)
                            await bountyService.ProcessRaidEndAsync(sessionId, request, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (System.Exception ex)
                    {
                        logger.Error($"[Bounty] Raid result processing failed: {ex}");
                    }

                    return output ?? string.Empty;
                },
                typeof(EndLocalRaidRequestData)
            ),
            new RouteAction(
                "/odin-the-mercenary/bounty/status",
                async (url, info, sessionId, output, cancellationToken) =>
                {
                    var status = await bountyService.GetStatusAsync(sessionId, cancellationToken);
                    return JsonSerializer.Serialize(status);
                },
                typeof(EmptyRequestData)
            )
        ];
    }
}
