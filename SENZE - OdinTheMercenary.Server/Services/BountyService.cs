using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Modding;
using System.Globalization;
using TheMercenaryServer.Controllers;
using TheMercenaryServer.Models;

namespace TheMercenaryServer.Services;

[Injectable(InjectionType.Singleton)]
public sealed class BountyService(
    BountyConfigController configController,
    ProfileDataService profileDataService,
    SaveServer saveServer,
    MercenaryLogger logger
)
{
    private const string ProfileDataKey = "com.senze.odinthemercenary.bounty";
    private const string MercenaryRole = "mercenary";

    private readonly SemaphoreSlim _sync = new(1, 1);
    private string _latestProfileId = string.Empty;
    private BountyRaidStatus _currentStatus = CreateEmptyStatus(configController.Config);

    public async Task<BountyRaidStatus> PrepareRaidAsync(
        MongoId sessionId,
        StartLocalRaidRequestData request,
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var profileId = sessionId.ToString();
            _latestProfileId = profileId;

            var config = configController.Config;
            var state = await LoadStateAsync(profileId, cancellationToken);
            var changed = ReconcileContract(state, config);

            if (changed)
                await SaveStateAsync(profileId, state, cancellationToken);

            var isPmcRaid = IsPmcRaid(request.PlayerSide);
            var huntActive = config.enabled
                && isPmcRaid
                && state.contractActive
                && state.respiteRaidsRemaining == 0;

            _currentStatus = CreateStatus(
                state,
                config,
                hasProfile: true,
                isPmcRaid,
                huntActive,
                request.Location ?? string.Empty,
                huntActive ? "Bounty contract active for this raid." : "No bounty hunt for this raid."
            );

            if (huntActive)
                logger.Warn($"Bounty active at {state.heat}/{config.maxHeat} Heat. Odin spawn forced for this PMC raid.");

            return CloneStatus(_currentStatus);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<BountyRaidStatus> ProcessRaidEndAsync(
        MongoId sessionId,
        EndLocalRaidRequestData request,
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var profileId = sessionId.ToString();
            _latestProfileId = profileId;

            var config = configController.Config;
            var profile = request.Results?.Profile;
            var faction = profile?.Info?.Side ?? string.Empty;

            if (!IsPmcFaction(faction))
            {
                _currentStatus = await BuildLatestStatusAsync(profileId, "Scav raid ignored.", cancellationToken);
                return CloneStatus(_currentStatus);
            }

            var state = await LoadStateAsync(profileId, cancellationToken);
            var victims = profile?.Stats?.Eft?.Victims?.Where(victim => victim != null).ToList() ?? [];
            var fingerprint = CreateRaidFingerprint(request, victims.Count);

            if (string.Equals(state.lastProcessedRaid, fingerprint, StringComparison.Ordinal))
            {
                logger.Info("Bounty result ignored because this raid was already processed.");
                _currentStatus = CreateStatus(state, config, true, true, false, string.Empty, "Raid already processed.");
                return CloneStatus(_currentStatus);
            }

            var previousHeat = state.heat;
            var previousRespite = state.respiteRaidsRemaining;
            if (state.respiteRaidsRemaining > 0)
                state.respiteRaidsRemaining--;

            var friendlyKills = victims.Count(victim => IsSameFactionPmc(victim, faction));
            var playerKilledOdin = victims.Any(IsOdin);
            var odinKilledPlayer = IsOdin(profile?.Stats?.Eft?.Aggressor);

            if (friendlyKills > 0)
            {
                var gain = config.firstFriendlyKillHeat
                    + Math.Max(0, friendlyKills - 1) * config.additionalFriendlyKillHeat;
                gain = Math.Min(gain, config.maxHeatGainPerRaid);
                state.heat = Math.Min(config.maxHeat, state.heat + gain);
                state.cleanRaidStreak = 0;
            }
            else if (request.Results?.Result == ExitStatus.SURVIVED)
            {
                state.cleanRaidStreak++;
                state.heat = Math.Max(0, state.heat - config.cleanRaidReduction);

                if (state.cleanRaidStreak >= config.cleanRaidStreakLength)
                {
                    state.heat = Math.Max(0, state.heat - config.cleanRaidStreakBonus);
                    state.cleanRaidStreak = 0;
                }
            }
            else
            {
                state.cleanRaidStreak = 0;
            }

            if (odinKilledPlayer)
            {
                state.heat = Math.Max(0, state.heat - config.odinKillsPlayerReduction);
                state.contractActive = false;

                // If the player is still above the activation threshold, allow one raid of breathing room.
                if (state.heat >= config.activationThreshold)
                    state.respiteRaidsRemaining = Math.Max(state.respiteRaidsRemaining, 1);
            }

            if (playerKilledOdin)
            {
                state.contractActive = false;
                state.respiteRaidsRemaining = Math.Max(
                    state.respiteRaidsRemaining,
                    config.odinKilledRespiteRaids
                );
            }

            ReconcileContract(state, config);
            state.lastProcessedRaid = fingerprint;
            await SaveStateAsync(profileId, state, cancellationToken);

            var reason = BuildRaidEndReason(
                friendlyKills,
                playerKilledOdin,
                odinKilledPlayer,
                previousHeat,
                state.heat,
                previousRespite,
                state.respiteRaidsRemaining
            );

            _currentStatus = CreateStatus(state, config, true, true, false, string.Empty, reason);
            logger.Info($"[Bounty] {reason}");
            return CloneStatus(_currentStatus);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<BountyRaidStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_currentStatus.hasProfile)
                return CloneStatus(_currentStatus);

            var profileId = ResolveLatestProfileId();
            if (string.IsNullOrWhiteSpace(profileId))
                return CloneStatus(_currentStatus);

            _latestProfileId = profileId;
            _currentStatus = await BuildLatestStatusAsync(profileId, "Profile state loaded.", cancellationToken);
            return CloneStatus(_currentStatus);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<BountyRaidStatus> GetStatusAsync(
        MongoId sessionId,
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var profileId = sessionId.ToString();
            if (!sessionId.IsEmpty
                && (!string.Equals(_latestProfileId, profileId, StringComparison.Ordinal)
                    || !_currentStatus.hasProfile))
            {
                _latestProfileId = profileId;
                _currentStatus = await BuildLatestStatusAsync(
                    profileId,
                    "Profile state loaded from the game session.",
                    cancellationToken);
            }

            return CloneStatus(_currentStatus);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<(bool Success, BountyRaidStatus Status)> TrySetLatestHeatAsync(
        int requestedHeat,
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var profileId = ResolveLatestProfileId();
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return (false, CloneStatus(_currentStatus));
            }

            _latestProfileId = profileId;
            var config = configController.Config;
            var state = await LoadStateAsync(profileId, cancellationToken);
            state.heat = Math.Clamp(requestedHeat, 0, config.maxHeat);
            state.respiteRaidsRemaining = 0;

            if (state.heat < config.releaseThreshold)
                state.contractActive = false;
            else if (state.heat >= config.activationThreshold)
                state.contractActive = true;

            await SaveStateAsync(profileId, state, cancellationToken);
            _currentStatus = CreateStatus(
                state,
                config,
                true,
                false,
                false,
                string.Empty,
                "Heat changed in WebUI and respite cleared. Applies next raid."
            );
            return (true, CloneStatus(_currentStatus));
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<(bool Success, BountyRaidStatus Status)> TryResetLatestStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var profileId = ResolveLatestProfileId();
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return (false, CloneStatus(_currentStatus));
            }

            _latestProfileId = profileId;
            var state = new BountyProfileState();
            await SaveStateAsync(profileId, state, cancellationToken);
            _currentStatus = CreateStatus(state, configController.Config, true, false, false, string.Empty, "Bounty state reset. Applies next raid.");
            return (true, CloneStatus(_currentStatus));
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<BountyRaidStatus> BuildLatestStatusAsync(
        string profileId,
        string reason,
        CancellationToken cancellationToken)
    {
        var config = configController.Config;
        var state = await LoadStateAsync(profileId, cancellationToken);
        ReconcileContract(state, config);
        return CreateStatus(state, config, true, false, false, string.Empty, reason);
    }

    private async Task<BountyProfileState> LoadStateAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!profileDataService.ProfileDataExists(profileId, ProfileDataKey))
                return new BountyProfileState();

            var state = await profileDataService.GetProfileDataAsync<BountyProfileState>(
                    new MongoId(profileId),
                    ProfileDataKey,
                    cancellationToken)
                ?? new BountyProfileState();
            NormalizeState(state, configController.Config);
            return state;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error($"Could not load bounty state for profile '{profileId}': {ex.Message}");
            return new BountyProfileState();
        }
    }

    private Task SaveStateAsync(
        string profileId,
        BountyProfileState state,
        CancellationToken cancellationToken)
    {
        NormalizeState(state, configController.Config);
        return profileDataService.SaveProfileDataAsync(
            new MongoId(profileId),
            ProfileDataKey,
            state,
            cancellationToken);
    }

    private string ResolveLatestProfileId()
    {
        if (!string.IsNullOrWhiteSpace(_latestProfileId))
            return _latestProfileId;

        try
        {
            var profiles = saveServer.GetProfiles();
            if (profiles.Count == 1)
                return profiles.Keys.First().ToString();
        }
        catch (Exception ex)
        {
            logger.Error($"Could not resolve the current profile for the bounty WebUI: {ex.Message}");
        }

        return string.Empty;
    }

    private static bool ReconcileContract(BountyProfileState state, BountyConfig config)
    {
        var oldValue = state.contractActive;

        if (!config.enabled || state.heat < config.releaseThreshold || state.respiteRaidsRemaining > 0)
            state.contractActive = false;
        else if (!state.contractActive && state.heat >= config.activationThreshold)
            state.contractActive = true;

        return oldValue != state.contractActive;
    }

    private static void NormalizeState(BountyProfileState state, BountyConfig config)
    {
        state.heat = Math.Clamp(state.heat, 0, config.maxHeat);
        state.cleanRaidStreak = Math.Max(0, state.cleanRaidStreak);
        state.respiteRaidsRemaining = Math.Max(0, state.respiteRaidsRemaining);
        state.lastProcessedRaid ??= string.Empty;
    }

    private static bool IsPmcRaid(string? playerSide)
        => string.Equals(playerSide, "pmc", StringComparison.OrdinalIgnoreCase)
            || IsPmcFaction(playerSide);

    private static bool IsPmcFaction(string? faction)
        => string.Equals(faction, "Usec", StringComparison.OrdinalIgnoreCase)
            || string.Equals(faction, "Bear", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameFactionPmc(Victim victim, string faction)
    {
        var expectedRole = string.Equals(faction, "Usec", StringComparison.OrdinalIgnoreCase)
            ? "pmcUSEC"
            : "pmcBEAR";

        return string.Equals(victim.Role, expectedRole, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOdin(Victim victim)
        => IsOdin(victim.Role, victim.Name);

    private static bool IsOdin(Aggressor? aggressor)
        => aggressor != null && IsOdin(aggressor.Role, aggressor.Name);

    private static bool IsOdin(string? role, string? name)
        => string.Equals(role, MercenaryRole, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Odin", StringComparison.OrdinalIgnoreCase);

    private static string CreateRaidFingerprint(EndLocalRaidRequestData request, int victimCount)
    {
        var eft = request.Results?.Profile?.Stats?.Eft;
        return string.Join("|",
            request.ServerId ?? string.Empty,
            eft?.LastSessionDate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            request.Results?.PlayTime?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            request.Results?.Result?.ToString() ?? string.Empty,
            victimCount.ToString(CultureInfo.InvariantCulture)
        );
    }

    private static string BuildRaidEndReason(
        int friendlyKills,
        bool playerKilledOdin,
        bool odinKilledPlayer,
        int previousHeat,
        int currentHeat,
        int previousRespite,
        int currentRespite
    )
    {
        var events = new List<string>();

        if (friendlyKills > 0)
            events.Add($"{friendlyKills} same-faction PMC kill(s)");
        if (playerKilledOdin)
            events.Add("Odin killed by player");
        if (odinKilledPlayer)
            events.Add("player killed by Odin");
        if (events.Count == 0)
            events.Add("no same-faction PMC kills");

        return $"{string.Join(", ", events)}. Heat {previousHeat} -> {currentHeat}; respite {previousRespite} -> {currentRespite}.";
    }

    private static BountyRaidStatus CreateEmptyStatus(BountyConfig config)
        => CreateStatus(new BountyProfileState(), config, false, false, false, string.Empty, "No profile selected yet.");

    private static BountyRaidStatus CreateStatus(
        BountyProfileState state,
        BountyConfig config,
        bool hasProfile,
        bool isPmcRaid,
        bool huntActive,
        string location,
        string reason
    ) => new()
    {
        enabled = config.enabled,
        hasProfile = hasProfile,
        isPmcRaid = isPmcRaid,
        huntActive = huntActive,
        forceOdinSpawn = huntActive,
        contractActive = state.contractActive,
        heat = state.heat,
        maxHeat = config.maxHeat,
        activationThreshold = config.activationThreshold,
        releaseThreshold = config.releaseThreshold,
        cleanRaidStreak = state.cleanRaidStreak,
        respiteRaidsRemaining = state.respiteRaidsRemaining,
        location = location,
        message = config.huntMessage,
        reason = reason
    };

    private static BountyRaidStatus CloneStatus(BountyRaidStatus source) => new()
    {
        enabled = source.enabled,
        hasProfile = source.hasProfile,
        isPmcRaid = source.isPmcRaid,
        huntActive = source.huntActive,
        forceOdinSpawn = source.forceOdinSpawn,
        contractActive = source.contractActive,
        heat = source.heat,
        maxHeat = source.maxHeat,
        activationThreshold = source.activationThreshold,
        releaseThreshold = source.releaseThreshold,
        cleanRaidStreak = source.cleanRaidStreak,
        respiteRaidsRemaining = source.respiteRaidsRemaining,
        location = source.location,
        message = source.message,
        reason = source.reason
    };
}
