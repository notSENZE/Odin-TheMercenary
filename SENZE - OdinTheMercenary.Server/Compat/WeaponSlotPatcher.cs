using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace TheMercenaryServer.Compat;

[Injectable(InjectionType.Singleton)]
public sealed class WeaponSlotPatcher
{
    public bool Apply(
        Dictionary<MongoId, TemplateItem> items,
        CompatRule rule,
        CompatReport report,
        out string reason)
    {
        reason = string.Empty;

        if (!items.TryGetValue(new MongoId(rule.TargetTpl), out var item) || item?.Properties?.Slots is null)
        {
            reason = $"Weapon or attachment '{rule.TargetTpl}' was not found or has no slots.";
            return false;
        }

        var wantedSlots = new HashSet<string>(rule.SlotNames!, StringComparer.OrdinalIgnoreCase);
        var matchingSlots = item.Properties.Slots
            .Where(slot => slot?.Name is not null && wantedSlots.Contains(slot.Name))
            .ToArray();

        if (matchingSlots.Length == 0)
        {
            reason = $"Target '{rule.TargetTpl}' has none of these slots: {string.Join(", ", wantedSlots)}.";
            return false;
        }

        report.TargetsFound++;
        foreach (var slot in matchingSlots)
        {
            var filters = slot?.Properties?.Filters;
            if (filters is null)
            {
                continue;
            }

            foreach (var filter in filters)
            {
                if (filter?.Filter is not null)
                {
                    PatchFilter(filter.Filter, rule, report);
                }
            }
        }

        return true;
    }

    private static void PatchFilter(HashSet<MongoId> filter, CompatRule rule, CompatReport report)
    {
        report.FiltersTouched++;

        if (rule.Replace)
        {
            filter.Clear();
            foreach (var tpl in rule.AllowedTpls)
            {
                filter.Add(new MongoId(tpl));
            }

            report.Replaced += rule.AllowedTpls.Count;
            return;
        }

        foreach (var tpl in rule.AllowedTpls)
        {
            if (filter.Add(new MongoId(tpl)))
            {
                report.Added++;
            }
        }
    }
}
