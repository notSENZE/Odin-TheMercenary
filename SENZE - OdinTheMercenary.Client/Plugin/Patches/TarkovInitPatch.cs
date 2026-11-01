#nullable enable
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.AssetsManager;
using EFT.InputSystem;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using TheMercenary.Behavior.Layers;

namespace TheMercenary.Patches;

internal sealed class TarkovInitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(TarkovApplication).GetMethod(
            nameof(TarkovApplication.Init),
            BindingFlags.Public | BindingFlags.Instance
        );
    }

    [PatchPostfix]
    private static void PatchPostfix(IAssetsManager assetsManager, InputTree inputTree)
    {
        var brains = new List<string>
        {
            "PMC",
            "ExUsec",
            "Assault",
            "PmcUsec",
            "PmcBear",
            "PmcUSEC",
            "PmcBEAR"
        };

        var types = new List<WildSpawnType>
        {
            (WildSpawnType)836500
        };

        // SAIN combat/extract layers use priorities around 20-24 and ORBIT uses 19.
        // Hunt must win while travelling, then yield to visible-enemy combat in IsActive().
        BrainManager.AddCustomLayer(typeof(HuntTargetLayer), brains, 25, types);
    }
}
