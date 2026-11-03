#nullable enable
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace TheMercenary;

[BepInDependency("com.morebotsapi.tacticaltoaster", "2.1.1")]
[BepInDependency("com.wtt.commonlib", "3.0.6")]
[BepInDependency("com.wtt.contentbackport", "2.0.5")]
[BepInDependency("xyz.drakia.bigbrain", "1.5.0")]
[BepInDependency("xyz.drakia.waypoints", "1.9.0")]
[BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(ClientInfo.GUID, ClientInfo.PluginName, ClientInfo.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public static ManualLogSource? LogSource;

    internal static ConfigEntry<bool> UsePropitalOnPlayerHit { get; private set; } = null!;

    private void Awake()
    {
        LogSource = Logger;

        UsePropitalOnPlayerHit = Config.Bind(
            "Odin",
            "UsePropitalOnPlayerHit",
            true,
            "Use Propital once after Odin first hits a human player. Hits on AI do not trigger it."
        );

        // Only enable patches.
        PatchBootstrap.Enable();
    }
}
