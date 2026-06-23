using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HarnessPrefabs;

internal static class PrefabRequirementParser
{
    public static List<PrefabRequirement> ParseMany(string requirements)
    {
        if (string.IsNullOrWhiteSpace(requirements) || requirements.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return new List<PrefabRequirement>();
        }

        return requirements
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseOne)
            .Where(requirement => !string.IsNullOrWhiteSpace(requirement.Item))
            .ToList();
    }

    public static List<PrefabRequirement> Clone(IEnumerable<PrefabRequirement>? requirements)
    {
        return requirements?
                   .Where(requirement => requirement != null && !string.IsNullOrWhiteSpace(requirement.Item))
                   .Select(requirement => new PrefabRequirement
                   {
                       Item = requirement.Item.Trim(),
                       Amount = Math.Max(1, requirement.Amount)
                   })
                   .ToList() ??
               new List<PrefabRequirement>();
    }

    private static PrefabRequirement ParseOne(string entry)
    {
        int separator = entry.IndexOfAny(new[] { ',', ':', '=' });
        if (separator < 0)
        {
            return new PrefabRequirement
            {
                Item = entry.Trim(),
                Amount = 1
            };
        }

        int amount = 1;
        int.TryParse(entry.Substring(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount);

        return new PrefabRequirement
        {
            Item = entry.Substring(0, separator).Trim(),
            Amount = Math.Max(1, amount)
        };
    }
}
