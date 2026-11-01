#nullable enable
using EFT;
using EFT.Ballistics;
using UnityEngine;

namespace TheMercenary.Components;

public sealed class PropitalOnPlayerHit : MonoBehaviour
{
    private const float RetryDelaySeconds = 0.5f;
    private const float ApplyTimeoutSeconds = 15f;

    private BotOwner? _botOwner;
    private bool _triggered;
    private bool _pending;
    private bool _applyInProgress;
    private float _nextAttemptTime;
    private float _timeoutTime;

    public void Init(BotOwner botOwner)
    {
        if (_botOwner != null)
            _botOwner.BotPersonalStats.OnHitTarget -= OnHitTarget;

        _botOwner = botOwner;
        _botOwner.BotPersonalStats.OnHitTarget -= OnHitTarget;
        _botOwner.BotPersonalStats.OnHitTarget += OnHitTarget;
    }

    private void OnHitTarget(IPlayer target, DamageInfo damageInfo, EBodyPart bodyPart)
    {
        if (_triggered || target == null || target.IsAI)
            return;

        _triggered = true;
        _pending = true;
        _nextAttemptTime = Time.time;
        _timeoutTime = Time.time + ApplyTimeoutSeconds;
    }

    private void Update()
    {
        if (!_pending || _applyInProgress || Time.time < _nextAttemptTime || _botOwner == null)
            return;

        if (!_botOwner.GetPlayer.HealthController.IsAlive)
        {
            _pending = false;
            return;
        }

        if (Time.time >= _timeoutTime)
        {
            _pending = false;
            Plugin.LogSource?.LogWarning("Odin could not use Propital within 15 seconds of hitting the player.");
            return;
        }

        var stimulators = _botOwner.Medecine?.Stimulators;
        if (stimulators == null)
        {
            _nextAttemptTime = Time.time + RetryDelaySeconds;
            return;
        }

        stimulators.Refresh();
        if (!stimulators.HaveSmt)
        {
            _nextAttemptTime = Time.time + RetryDelaySeconds;
            return;
        }

        _applyInProgress = true;
        stimulators.StartApplyToTarget(OnApplyFinished);
    }

    private void OnApplyFinished(bool success)
    {
        _applyInProgress = false;

        if (success)
        {
            _pending = false;
            Plugin.LogSource?.LogInfo("Odin used Propital after hitting the player.");
            return;
        }

        _nextAttemptTime = Time.time + RetryDelaySeconds;
    }

    private void OnDestroy()
    {
        if (_botOwner != null)
            _botOwner.BotPersonalStats.OnHitTarget -= OnHitTarget;
    }
}
