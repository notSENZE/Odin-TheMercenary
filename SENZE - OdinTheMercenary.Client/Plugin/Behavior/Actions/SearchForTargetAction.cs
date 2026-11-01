#nullable enable
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using TheMercenary.Components;
using UnityEngine;

namespace TheMercenary.Behavior.Actions;

internal sealed class SearchForTargetAction : CustomLogic
{
    private BotHuntManager huntManager = null!;
    private float endTime;
    private float nextGoToTime;

    public SearchForTargetAction(BotOwner botOwner) : base(botOwner)
    {
    }

    public override void Start()
    {
        base.Start();
        huntManager = BotOwner.GetComponent<BotHuntManager>();
        endTime = Time.time + 30f;
        nextGoToTime = 0f;
    }

    public override void Update(CustomLayer.ActionData data)
    {
        if (Time.time > endTime)
            return;

        // Calling GoToPoint every frame is expensive; 1x/sec is plenty for "search".
        if (Time.time < nextGoToTime)
            return;

        nextGoToTime = Time.time + 1f;
        BotOwner.GoToPoint(huntManager.knownLocation, mustHaveWay: false);
    }
}
