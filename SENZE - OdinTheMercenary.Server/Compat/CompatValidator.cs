using System.Text;
using SPTarkov.DI.Annotations;

namespace TheMercenaryServer.Compat;

[Injectable(InjectionType.Singleton)]
public sealed class CompatValidator
{
    public bool TryCleanFile(
        string fileName,
        CompatRuleSet set,
        out List<CompatRule> rules,
        out string error)
    {
        rules = [];
        error = string.Empty;

        if (set.Rules.Count == 0)
        {
            return true;
        }

        var errors = new StringBuilder();
        for (var index = 0; index < set.Rules.Count; index++)
        {
            if (TryCleanRule(fileName, index + 1, set.Rules[index], out var rule, out var ruleError))
            {
                rules.Add(rule);
                continue;
            }

            if (errors.Length > 0)
            {
                errors.Append(" | ");
            }

            errors.Append(ruleError);
        }

        if (errors.Length == 0)
        {
            return true;
        }

        error = errors.ToString();
        rules.Clear();
        return false;
    }

    private static bool TryCleanRule(
        string fileName,
        int ruleNumber,
        CompatRule input,
        out CompatRule rule,
        out string error)
    {
        rule = new CompatRule();
        error = string.Empty;

        var target = input.TargetTpl.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            error = $"Rule #{ruleNumber} in '{fileName}' has an empty TARGET-TPL.";
            return false;
        }

        var allowed = input.AllowedTpls
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allowed.Count == 0)
        {
            error = $"Rule #{ruleNumber} in '{fileName}' has no valid ALLOWED_TPLS.";
            return false;
        }

        var slotNames = input.SlotNames?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (slotNames is not { Count: > 0 })
        {
            error = $"Rule #{ruleNumber} in '{fileName}' needs at least one valid SLOT_NAME.";
            return false;
        }

        rule = new CompatRule
        {
            TargetTpl = target,
            AllowedTpls = allowed,
            SlotNames = slotNames,
            Replace = input.Replace
        };

        return true;
    }
}
