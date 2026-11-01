using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace TheMercenaryServer.Compat;

[Injectable(InjectionType.Singleton)]
public sealed class CompatService(
    CompatFileReader reader,
    CompatValidator validator,
    WeaponSlotPatcher weaponSlotPatcher,
    TemplateTable templateTable,
    MercenaryLogger logger)
{
    public async Task LoadAsync(Assembly assembly)
    {
        var modRoot = System.IO.Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve the Odin - The Mercenary mod folder.");
        var compatRoot = System.IO.Path.Combine(modRoot, "config", "compat");

        var files = EnumerateJson(System.IO.Path.Combine(compatRoot, "weapons"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            logger.Info("No compat JSON files found.");
            return;
        }

        var configs = new List<(string FilePath, List<CompatRule> Rules)>();
        foreach (var file in files)
        {
            var fileName = System.IO.Path.GetFileName(file);
            var set = await reader.ReadAsync(file);
            if (set.Rules.Count == 0)
            {
                logger.Info($"Compat file '{fileName}' has no rules.");
                continue;
            }

            if (!validator.TryCleanFile(fileName, set, out var rules, out var error))
            {
                throw new InvalidOperationException($"Compat validation failed: {error}");
            }

            if (rules.Count > 0)
            {
                configs.Add((file, rules));
            }
        }

        if (configs.Count == 0)
        {
            logger.Info("No usable compat rules found.");
            return;
        }

        var items = templateTable.Items;
        var report = new CompatReport
        {
            Files = configs.Count,
            Rules = configs.Sum(config => config.Rules.Count)
        };

        foreach (var (filePath, rules) in configs)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            foreach (var rule in rules)
            {
                CountMissingAllowedTemplates(items, rule, report);

                var applied = weaponSlotPatcher.Apply(items, rule, report, out var reason);

                if (applied)
                {
                    continue;
                }

                report.TargetsMissing++;
                logger.Warn($"Compat file '{fileName}' skipped a rule: {reason}");
            }
        }

        logger.Info(
            $"Compat applied: Files={report.Files}, Rules={report.Rules}, " +
            $"TargetsFound={report.TargetsFound}, TargetsMissing={report.TargetsMissing}, " +
            $"FiltersTouched={report.FiltersTouched}, Added={report.Added}, Replaced={report.Replaced}, " +
            $"MissingAllowedTemplates={report.MissingAllowedTemplates}.");
    }

    private static IReadOnlyList<string> EnumerateJson(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory
            .GetFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CountMissingAllowedTemplates(
        Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> items,
        CompatRule rule,
        CompatReport report)
    {
        foreach (var tpl in rule.AllowedTpls)
        {
            if (!items.ContainsKey(new MongoId(tpl)))
            {
                report.MissingAllowedTemplates++;
            }
        }
    }
}
