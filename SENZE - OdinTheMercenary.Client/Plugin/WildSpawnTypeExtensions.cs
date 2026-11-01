#nullable enable
using EFT;
using System.Collections.Generic;

namespace TheMercenary;

public static class WildSpawnTypeExtensions
{
    public static readonly List<int> MercenaryTypes = new()
    {
        836500
    };

    public static bool IsMercenary(WildSpawnType type)
    {
        return MercenaryTypes.Contains((int)type);
    }
}
