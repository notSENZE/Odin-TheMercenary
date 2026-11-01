#nullable enable
using EFT;
using SPT.Reflection.Patching;
using System.Reflection;
using TheMercenary.Components;
using UnityEngine;

namespace TheMercenary.Patches;

internal sealed class BotsControllerInitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(BotsController).GetMethod(
            nameof(BotsController.Init),
            BindingFlags.Public | BindingFlags.Instance
        );
    }

    [PatchPostfix]
    private static void PatchPostfix()
    {
        var instance = MonoBehaviourSingleton<HuntManager>.Instance;

        if (instance == null)
        {
            var go = new GameObject("OdinTheMercenary.HuntManager");
            Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<HuntManager>();
        }

        instance.InitRaid();
    }
}
