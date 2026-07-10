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
            .ToList();
    }

    public static List<PrefabRequirement> Clone(IEnumerable<PrefabRequirement>? requirements)
    {
        List<PrefabRequirement> cloned = new();
        if (requirements == null)
        {
            return cloned;
        }

        foreach (PrefabRequirement requirement in requirements)
        {
            string item = requirement?.Item?.Trim() ?? "";
            if (item.Length == 0)
            {
                throw new FormatException("Prefab requirement item names cannot be empty.");
            }

            if (requirement!.Amount < 1)
            {
                throw new FormatException($"Prefab requirement '{item}' must have a positive integer amount.");
            }

            cloned.Add(new PrefabRequirement
            {
                Item = item,
                Amount = requirement.Amount
            });
        }

        return cloned;
    }

    private static PrefabRequirement ParseOne(string entry)
    {
        int separator = entry.IndexOfAny(new[] { ',', ':', '=' });
        if (separator < 0)
        {
            string singleItem = entry.Trim();
            if (singleItem.Length == 0)
            {
                throw new FormatException("Prefab requirement item names cannot be empty.");
            }

            return new PrefabRequirement
            {
                Item = singleItem,
                Amount = 1
            };
        }

        string item = entry.Substring(0, separator).Trim();
        if (item.Length == 0)
        {
            throw new FormatException("Prefab requirement item names cannot be empty.");
        }

        if (!int.TryParse(entry.Substring(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 1)
        {
            throw new FormatException($"Prefab requirement '{item}' must have a positive integer amount.");
        }

        return new PrefabRequirement
        {
            Item = item,
            Amount = amount
        };
    }
}
