#nullable enable
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using TheMercenary.Behavior.Actions;
using TheMercenary.Components;

namespace TheMercenary.Behavior.Layers;

internal sealed class HuntTargetLayer : CustomLayer
{
    private readonly BotHuntManager huntManager;

    private Type? lastAction;
    private Type? nextAction;
    private string nextActionReason = "HuntingTarget";

    public HuntTargetLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
    {
        huntManager = botOwner.GetOrAddComponent<BotHuntManager>();
        nextAction = typeof(HuntTargetAction);
        lastAction = nextAction;
    }

    public override string GetName()
    {
        return "HuntTarget";
    }

    public override bool IsActive()
    {
        if (!TheMercenary.Plugin.ShouldHunt)
            return false;

        if (!huntManager.HasTarget())
            return false;

        // Once Odin can actually see an enemy, hand control back to SAIN/EFT combat.
        // Outside visual contact, this layer owns navigation and keeps him tracking the player.
        if (BotOwner.Memory?.GoalEnemy?.IsVisible == true)
            return false;

        nextAction = typeof(HuntTargetAction);
        nextActionReason = "HuntingTarget";
        return true;
    }

    public override Action GetNextAction()
    {
        lastAction = nextAction;
        return new Action(nextAction!, nextActionReason);
    }

    public override bool IsCurrentActionEnding()
    {
        return nextAction != lastAction || (CurrentAction.Type != nextAction && CurrentAction.Type != lastAction);
    }
}
