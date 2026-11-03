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
    private const int DefaultHuntChance = 50;

    private bool _odinNotifiedThisRaid;
    private int _huntChance;

    // Odin is a single boss-type in this mod -> one raid target is enough (and much cheaper).
    private IPlayer? _raidTarget;

    public readonly Dictionary<BotsGroup, IPlayer> huntTargets = new();

    public void InitRaid()
    {
        _odinNotifiedThisRaid = false;
        _raidTarget = null;
        huntTargets.Clear();
        _huntChance = LoadHuntChance();

        // Robustness: avoid duplicate subscriptions between raids.
        Singleton<IBotGame>.Instance.BotsController.BotSpawner.OnBotCreated -= OnBotCreated;
        Singleton<IBotGame>.Instance.BotsController.BotSpawner.OnBotCreated += OnBotCreated;
    }

    private void OnBotCreated(BotOwner bot)
    {
        if (!WildSpawnTypeExtensions.IsMercenary(bot.Profile.Info.Settings.Role))
            return;

        var roll = Random.Range(0, 100);
        var huntActive = roll < _huntChance;
        TryNotifyOdinSpawn(huntActive);

        TheMercenary.Plugin.LogSource?.LogInfo(
            $"Odin hunt roll: {roll + 1}/100 at {_huntChance}% - {(huntActive ? "active" : "inactive")}."
        );

        if (TheMercenary.Plugin.UsePropitalOnPlayerHit.Value)
        {
            var propitalController = bot.gameObject.GetOrAddComponent<PropitalOnPlayerHit>();
            propitalController.Init(bot);
        }

        if (!huntActive)
            return;

        var huntManager = bot.gameObject.GetOrAddComponent<BotHuntManager>();
        huntManager.Init(bot, this);
        TheMercenary.Plugin.LogSource?.LogInfo("Odin hunt controller initialized.");
        FindTarget(huntManager);
    }

    private void TryNotifyOdinSpawn(bool huntActive)
    {
        if (_odinNotifiedThisRaid)
            return;

        _odinNotifiedThisRaid = true;
        var message = huntActive ? "ODIN IS HUNTING YOU." : "ODIN HAS SPAWNED.";

        NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long);
    }

    public void FindTarget(BotHuntManager hunter)
    {
        if (!hunter.IsEnabled)
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

    private static int LoadHuntChance()
    {
        try
        {
            var json = RequestHandler.GetJson("/odin-the-mercenary/hunt/settings");
            var token = JToken.Parse(json);

            if (token["data"] is JObject wrappedData)
                token = wrappedData;

            return Mathf.Clamp(token.Value<int?>("huntChance") ?? DefaultHuntChance, 0, 100);
        }
        catch (System.Exception ex)
        {
            TheMercenary.Plugin.LogSource?.LogWarning(
                $"Could not read Odin hunt chance; using {DefaultHuntChance}%: {ex.Message}"
            );
            return DefaultHuntChance;
        }
    }
}
