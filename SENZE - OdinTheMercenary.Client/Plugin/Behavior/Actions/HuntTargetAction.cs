#nullable enable
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using System.Reflection;
using TheMercenary.Components;
using UnityEngine;

namespace TheMercenary.Behavior.Actions;

internal sealed class HuntTargetAction : CustomLogic
{
    private BotHuntManager huntManager = null!;
    private float nextUpdate;

    private static readonly FieldInfo BotZoneField = AccessTools.Field(typeof(BotsGroup), "<BotZone>k__BackingField");
    private readonly LookAround steeringLogic;

    public HuntTargetAction(BotOwner botOwner) : base(botOwner)
    {
        steeringLogic = new LookAround();
    }

    public override void Start()
    {
        base.Start();

        huntManager = BotOwner.GetComponent<BotHuntManager>();

        if (!huntManager.layerActivationLogged)
        {
            huntManager.layerActivationLogged = true;
            TheMercenary.Plugin.LogSource?.LogInfo("Odin hunt layer is active and moving toward the player.");
        }

        BotOwner.Mover.Stop();
        BotOwner.PatrollingData.Pause();
        BotOwner.AimingManager.CurrentAiming.LoseTarget();
    }

    public override void Stop()
    {
        base.Stop();
        BotOwner.PatrollingData.Unpause();
    }

    public override void Update(CustomLayer.ActionData data)
    {
        BotOwner.SetPose(1f);
        BotOwner.SetTargetMoveSpeed(1f);

        BotOwner.BewarePlantedMine.Update();
        BotOwner.DoorOpener.UpdateDoorInteractionStatus();

        BotOwner.Steering.LookToMovingDirection();
        steeringLogic.Update(BotOwner);

        if (Time.time < nextUpdate)
            return;

        nextUpdate = Time.time + 2.5f;

        UpdateBotZone();
        BotOwner.GoToPoint(huntManager.knownLocation, mustHaveWay: false);
    }

    private void UpdateBotZone()
    {
        var botSpawner = Singleton<IBotGame>.Instance.BotsController.BotSpawner;
        var zone = botSpawner.GetClosestZone(BotOwner.Position, out _);

        if (BotOwner.BotsGroup.BotZone == zone)
            return;

        BotZoneField.SetValue(BotOwner.BotsGroup, zone);
        BotOwner.PatrollingData.PointChooser.ShallChangeWay(true);
    }
}
