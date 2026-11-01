// Pfad: Server/Models/SpawnConfig.cs

using System.Collections.Generic;

namespace TheMercenaryServer.Models;

public sealed class SpawnConfig
{
    /// <summary>
    /// Enables MercenaryLogger output (debug/info).
    /// This existed before - keep for backward compatibility.
    /// </summary>
    public bool enableLogs { get; set; } = false;

    public Dictionary<string, MapSpawnConfig> maps { get; set; } = new();
}

public sealed class MapSpawnConfig
{
    public bool enabled { get; set; } = false;

    /// <summary>
    /// 0-100
    /// </summary>
    public int chance { get; set; } = 0;

    /// <summary>
    /// IMPORTANT: This is the COMPLETE list of zones for this map (authoritative list).
    /// The WebUI must never delete from this list when toggling.
    /// </summary>
    public List<string> zones { get; set; } = new();

    /// <summary>
    /// Zones that are currently disabled in the UI (blacklist).
    /// Enabled zones = zones - disabledZones.
    /// Backwards compatible: missing/empty => all zones enabled.
    /// </summary>
    public List<string> disabledZones { get; set; } = new();
}
