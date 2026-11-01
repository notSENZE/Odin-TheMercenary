#nullable enable
using BepInEx;

namespace TheMercenary.Prepatch;

[BepInDependency("com.morebotsapiprepatch.tacticaltoaster", "2.1.1")]
[BepInPlugin(TheMercenary.ClientInfo.PreLoadGUID, TheMercenary.ClientInfo.PreLoadName, TheMercenary.ClientInfo.Version)]
public sealed class OdinTheMercenaryPrepatchPlugin : BaseUnityPlugin
{
    public static OdinTheMercenaryPrepatchPlugin? Instance;

    private void Awake()
    {
        Instance = this;
    }
}
