#nullable enable
using Comfort.Common;
using EFT;
using EFT.Communications;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.SinglePlayer.Utils.InRaid;
using System.Collections.Generic;
using UnityEngine;

namespace TheMercenary.Components;

public sealed class HuntManager : MonoBehaviourSingleton<HuntManager>
{
    private const int OdinWildSpawnTypeValue = 836500;

    private bool _odinNotifiedThisRaid;

    // Odin is a single boss-type in this mod -> one raid target is enough (and much cheaper).
    private IPlayer? _raidTarget;

    public readonly Dictionary<BotsGroup, IPlayer> huntTargets = new();

    public void InitRaid()
    {
        _odinNotifiedThisRaid = false;
        _raidTarget = null;
        huntTargets.Clear();
        LoadBountyStatus();

        // Robustness: avoid duplicate subscriptions between raids.
        Singleton<IBotGame>.Instance.BotsController.BotSpawner.OnBotCreated -= OnBotCreated;
        Singleton<IBotGame>.Instance.BotsController.BotSpawner.OnBotCreated += OnBotCreated;
    }

    private void OnBotCreated(BotOwner bot)
    {
        TryNotifyOdinSpawn(bot);

        if (!WildSpawnTypeExtensions.IsMercenary(bot.Profile.Info.Settings.Role))
            return;

        if (TheMercenary.Plugin.UsePropitalOnPlayerHit.Value)
        {
            var propitalController = bot.gameObject.GetOrAddComponent<PropitalOnPlayerHit>();
            propitalController.Init(bot);
        }

        if (!TheMercenary.Plugin.ShouldHunt)
            return;

        var huntManager = bot.gameObject.GetOrAddComponent<BotHuntManager>();
        huntManager.Init(bot, this);
        TheMercenary.Plugin.LogSource?.LogInfo(
            $"Odin hunt controller initialized (bounty: {TheMercenary.Plugin.IsBountyHuntActive})."
        );
        FindTarget(huntManager);
    }

    private void TryNotifyOdinSpawn(BotOwner bot)
    {
        if (_odinNotifiedThisRaid)
            return;

        if ((int)bot.Profile.Info.Settings.Role != OdinWildSpawnTypeValue)
            return;

        _odinNotifiedThisRaid = true;
        var message = TheMercenary.Plugin.IsBountyHuntActive
            ? TheMercenary.Plugin.BountyHuntMessage
            : "ODIN HAS SPAWNED.";

        NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long);
    }

    public void FindTarget(BotHuntManager hunter)
    {
        if (!TheMercenary.Plugin.ShouldHunt)
            return;

        // Fast path: keep using the cached raid target if it's still valid.
        if (IsValidHumanTarget(_raidTarget))
        {
            huntTargets[hunter.botOwner.BotsGroup] = _raidTarget!;
            hunter.huntTarget = _raidTarget!;
            hunter.knownLocation = _raidTarget!.Position;
            LogTargetAssignment(hunter);
            return;
        }

        // Find the first valid human player. No shuffle = no allocations.
        var players = Singleton<GameWorld>.Instance.AllAlivePlayersList;
        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (!IsValidHumanTarget(p))
                continue;

            _raidTarget = p;

            huntTargets[hunter.botOwner.BotsGroup] = p;
            hunter.huntTarget = p;
            hunter.knownLocation = p.Position;
            LogTargetAssignment(hunter);
            return;
        }
    }

    private static void LogTargetAssignment(BotHuntManager hunter)
    {
        if (hunter.targetAssignmentLogged)
            return;

        hunter.targetAssignmentLogged = true;
        TheMercenary.Plugin.LogSource?.LogInfo("Odin received the player as his hunt target.");
    }

    private static bool IsValidHumanTarget(IPlayer? player)
    {
        if (player == null)
            return false;

        if (player.IsAI)
            return false;

        if (!player.HealthController.IsAlive)
            return false;

        return true;
    }

    private static void LoadBountyStatus()
    {
        TheMercenary.Plugin.SetBountyHunt(false);

        try
        {
            var json = RequestHandler.GetJson("/odin-the-mercenary/bounty/status");
            var token = JToken.Parse(json);

            // Keep this tolerant in case another SPT version wraps custom route data.
            if (token["data"] is JObject wrappedData)
                token = wrappedData;

            var active = token.Value<bool?>("huntActive") ?? false;
            var message = token.Value<string>("message");
            TheMercenary.Plugin.SetBountyHunt(active, message);

            if (active)
                TheMercenary.Plugin.LogSource?.LogInfo("Odin bounty hunt is active for this raid.");
        }
        catch (System.Exception ex)
        {
            TheMercenary.Plugin.LogSource?.LogWarning($"Could not read Odin bounty status: {ex.Message}");
        }
    }
}
