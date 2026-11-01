#nullable enable
using TheMercenary.Patches;

namespace TheMercenary;

internal static class PatchBootstrap
{
    private static bool _enabled;

    public static void Enable()
    {
        if (_enabled)
            return;

        _enabled = true;

        new TarkovInitPatch().Enable();
        new BotsControllerInitPatch().Enable();
    }
}
