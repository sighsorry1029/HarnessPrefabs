using System;
using System.Collections.Generic;
using System.Linq;

namespace HarnessPrefabs;

public enum PrefabAccess
{
    Hidden,
    Public,
    Admin
}

internal static class BuildCategories
{
    private static readonly HashSet<string> AdminCategoryNames = new(StringComparer.Ordinal)
    {
        HarnessProps,
        HarnessNature,
        HarnessStructures
    };

    public const string Misc = "Misc";
    public const string Crafting = "Crafting";
    public const string Building = "Building";
    public const string Stonecutter = "BuildingStonecutter";
    public const string Furniture = "Furniture";
    public const string HarnessProps = "Harness Props";
    public const string HarnessNature = "Harness Nature";
    public const string HarnessStructures = "Harness Structures";

    public static bool IsAdminCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        string trimmed = categoryName.Trim();
        return AdminCategoryNames.Contains(trimmed);
    }

    public static string NormalizeCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return HarnessProps;
        }

        return categoryName.Trim();
    }
}

internal static class PrefabSortPolicy
{
    public static IEnumerable<PrefabDiscovery> SortForHammer(IEnumerable<PrefabDiscovery> discoveries)
    {
        return discoveries
            .OrderBy(GetCategorySortKey)
            .ThenBy(GetCategorySortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetBucketSortKey)
            .ThenBy(GetBucketSortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(GetDisplaySortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(discovery => discovery.Name, StringComparer.Ordinal);
    }

    private static int GetCategorySortKey(PrefabDiscovery discovery)
    {
        string category = GetRuleCategory(discovery);
        return category switch
        {
            BuildCategories.Misc => 10,
            BuildCategories.Crafting => 20,
            BuildCategories.Building => 30,
            BuildCategories.Stonecutter => 40,
            BuildCategories.Furniture => 50,
            BuildCategories.HarnessNature => 110,
            BuildCategories.HarnessStructures => 120,
            BuildCategories.HarnessProps => 130,
            _ => 1000
        };
    }

    private static string GetCategorySortName(PrefabDiscovery discovery)
    {
        return GetRuleCategory(discovery);
    }

    private static int GetBucketSortKey(PrefabDiscovery discovery)
    {
        string category = GetRuleCategory(discovery);
        ComponentList components = GetRuleComponents(discovery);
        string group = MvbpPrefabDefaults.GetPieceGroup(discovery.Name);
        string lower = discovery.Name.ToLowerInvariant();

        return category switch
        {
            BuildCategories.Crafting => GetCraftingBucketSortKey(components, lower),
            BuildCategories.Building or BuildCategories.Stonecutter or BuildCategories.HarnessStructures => GetStructureBucketSortKey(components, group, lower),
            BuildCategories.Furniture or BuildCategories.HarnessProps => GetPropBucketSortKey(components, group, lower),
            BuildCategories.HarnessNature => GetNatureBucketSortKey(components, group, lower),
            _ => GetGeneralBucketSortKey(components, lower)
        };
    }

    private static string GetBucketSortName(PrefabDiscovery discovery)
    {
        string category = GetRuleCategory(discovery);
        ComponentList components = GetRuleComponents(discovery);
        string group = MvbpPrefabDefaults.GetPieceGroup(discovery.Name);
        string lower = discovery.Name.ToLowerInvariant();

        return category switch
        {
            BuildCategories.Crafting => GetCraftingBucketName(components, lower),
            BuildCategories.Building or BuildCategories.Stonecutter or BuildCategories.HarnessStructures => GetStructureBucketName(components, group, lower),
            BuildCategories.Furniture or BuildCategories.HarnessProps => GetPropBucketName(components, group, lower),
            BuildCategories.HarnessNature => GetNatureBucketName(components, group, lower),
            _ => GetGeneralBucketName(components, lower)
        };
    }

    private static string GetDisplaySortName(PrefabDiscovery discovery)
    {
        if (PrefabRuleStore.TryGetRule(discovery.Name, out PrefabRule rule) &&
            !string.IsNullOrWhiteSpace(rule.DisplayName))
        {
            return rule.DisplayName.Trim();
        }

        return discovery.Name;
    }

    private static string GetRuleCategory(PrefabDiscovery discovery)
    {
        string category = PrefabRuleStore.TryGetRule(discovery.Name, out PrefabRule rule)
            ? rule.Category
            : discovery.Category;
        return BuildCategories.NormalizeCategory(category);
    }

    private static ComponentList GetRuleComponents(PrefabDiscovery discovery)
    {
        return PrefabRuleStore.TryGetRule(discovery.Name, out PrefabRule rule) && rule.Components is { Count: > 0 }
            ? rule.Components
            : discovery.Components;
    }

    private static int GetCraftingBucketSortKey(IReadOnlyCollection<string> components, string lower)
    {
        if (components.Contains("CraftingStation")) return 10;
        if (components.Contains("StationExtension")) return 20;
        if (components.Contains("CookingStation")) return 30;
        if (components.Contains("Smelter")) return 40;
        if (components.Contains("Fireplace") || lower.Contains("fire")) return 50;
        return GetGeneralBucketSortKey(components, lower);
    }

    private static string GetCraftingBucketName(IReadOnlyCollection<string> components, string lower)
    {
        if (components.Contains("CraftingStation")) return "CraftingStation";
        if (components.Contains("StationExtension")) return "StationExtension";
        if (components.Contains("CookingStation")) return "CookingStation";
        if (components.Contains("Smelter")) return "Smelter";
        if (components.Contains("Fireplace") || lower.Contains("fire")) return "Fire";
        return GetGeneralBucketName(components, lower);
    }

    private static int GetInteractiveBucketSortKey(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("Container") || group == "Chest" || lower.Contains("chest")) return 10;
        if (components.Contains("CraftingStation") || components.Contains("StationExtension")) return 20;
        if (components.Contains("CookingStation") || components.Contains("Smelter")) return 30;
        if (components.Contains("Fireplace") || group is "Fire" or "Brazier" or "Torch") return 40;
        if (components.Contains("TeleportWorld") || group == "Portal") return 50;
        if (components.Contains("Door") || lower.Contains("door") || lower.Contains("gate")) return 60;
        if (components.Contains("Bed") || group == "Bed") return 70;
        if (components.Contains("Ship") || components.Contains("Vagon") || group == "Ship") return 80;
        if (components.Contains("ArmorStand") || group == "ArmorStand") return 90;
        if (components.Contains("PrivateArea")) return 100;
        return GetGeneralBucketSortKey(components, lower);
    }

    private static string GetInteractiveBucketName(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("Container") || group == "Chest" || lower.Contains("chest")) return "Container";
        if (components.Contains("CraftingStation") || components.Contains("StationExtension")) return "Crafting";
        if (components.Contains("CookingStation") || components.Contains("Smelter")) return "Processing";
        if (components.Contains("Fireplace") || group is "Fire" or "Brazier" or "Torch") return "Fire";
        if (components.Contains("TeleportWorld") || group == "Portal") return "Portal";
        if (components.Contains("Door") || lower.Contains("door") || lower.Contains("gate")) return "Door";
        if (components.Contains("Bed") || group == "Bed") return "Bed";
        if (components.Contains("Ship") || components.Contains("Vagon") || group == "Ship") return "Vehicle";
        if (components.Contains("ArmorStand") || group == "ArmorStand") return "ArmorStand";
        if (components.Contains("PrivateArea")) return "PrivateArea";
        return GetGeneralBucketName(components, lower);
    }

    private static int GetNatureBucketSortKey(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("Plant") || group is "Plant" or "VanillaCrop") return 10;
        if (components.Contains("Pickable")) return 20;
        if (components.Contains("TreeBase") || lower.Contains("tree")) return 30;
        if (components.Contains("TreeLog") || lower.Contains("log")) return 40;
        if (components.Contains("MineRock") || components.Contains("MineRock5") || group is "Rock" or "Ore" or "Ice") return 50;
        if (group == "Flora" || ContainsAny(lower, "bush", "fern", "grass", "reed", "moss", "vine", "root")) return 60;
        if (ContainsAny(lower, "rock", "cliff", "stoneformation")) return 70;
        return GetGeneralBucketSortKey(components, lower);
    }

    private static string GetNatureBucketName(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("Plant") || group is "Plant" or "VanillaCrop") return "Plant";
        if (components.Contains("Pickable")) return "Pickable";
        if (components.Contains("TreeBase") || lower.Contains("tree")) return "Tree";
        if (components.Contains("TreeLog") || lower.Contains("log")) return "TreeLog";
        if (components.Contains("MineRock") || components.Contains("MineRock5") || group is "Rock" or "Ore" or "Ice") return "RockOre";
        if (group == "Flora" || ContainsAny(lower, "bush", "fern", "grass", "reed", "moss", "vine", "root")) return "Flora";
        if (ContainsAny(lower, "rock", "cliff", "stoneformation")) return "RockStatic";
        return GetGeneralBucketName(components, lower);
    }

    private static int GetStructureBucketSortKey(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (ContainsAny(lower, "floor", "foundation")) return 10;
        if (ContainsAny(lower, "wall", "window")) return 20;
        if (ContainsAny(lower, "roof")) return 30;
        if (ContainsAny(lower, "stair", "ramp")) return 40;
        if (ContainsAny(lower, "beam", "pole", "pillar", "column")) return 50;
        if (ContainsAny(lower, "arch", "bridge", "ruin", "fortress")) return 60;
        if (ContainsAny(lower, "gate", "door")) return 70;
        int interactiveSortKey = GetInteractiveBucketSortKey(components, group, lower);
        if (interactiveSortKey < 1000) return 100 + interactiveSortKey;
        if (components.Contains("Destructible")) return 220;
        if (components.Contains("WearNTear")) return 230;
        if (components.Contains("Piece")) return 240;
        if (!string.IsNullOrWhiteSpace(group) && group != "None") return 250;
        return GetGeneralBucketSortKey(components, lower);
    }

    private static string GetStructureBucketName(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (ContainsAny(lower, "floor", "foundation")) return "Floor";
        if (ContainsAny(lower, "wall", "window")) return "Wall";
        if (ContainsAny(lower, "roof")) return "Roof";
        if (ContainsAny(lower, "stair", "ramp")) return "StairRamp";
        if (ContainsAny(lower, "beam", "pole", "pillar", "column")) return "Support";
        if (ContainsAny(lower, "arch", "bridge", "ruin", "fortress")) return "Shell";
        if (ContainsAny(lower, "gate", "door")) return "GateDoor";
        int interactiveSortKey = GetInteractiveBucketSortKey(components, group, lower);
        if (interactiveSortKey < 1000) return "Interactive" + GetInteractiveBucketName(components, group, lower);
        if (components.Contains("Destructible")) return "Destructible";
        if (components.Contains("WearNTear")) return "WearNTear";
        if (components.Contains("Piece")) return "Piece";
        return string.IsNullOrWhiteSpace(group) || group == "None" ? GetGeneralBucketName(components, lower) : group;
    }

    private static int GetPropBucketSortKey(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("InstanceRenderer")) return 5;
        if (components.Contains("Chair") || group == "Chair") return 10;
        if (components.Contains("ArmorStand") || group == "ArmorStand") return 20;
        if (group == "Table" || lower.Contains("table")) return 30;
        if (group == "Banner" || lower.Contains("banner")) return 40;
        if (group == "Rug" || lower.Contains("rug")) return 50;
        if (group == "Statue" || lower.Contains("statue")) return 60;
        if (group == "Treasure" || ContainsAny(lower, "treasure", "coin", "chest")) return 70;
        if (ContainsAny(lower, "barrel", "barrell", "crate", "pot", "jar")) return 80;
        if (!string.IsNullOrWhiteSpace(group) && group != "None") return 90;
        if (lower.Contains("clutter")) return 100;
        if (ContainsAny(lower, "frac", "fragment", "broken")) return 110;
        if (lower.Contains("_lod") || lower.EndsWith("lod", StringComparison.Ordinal)) return 120;
        return GetGeneralBucketSortKey(components, lower);
    }

    private static string GetPropBucketName(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("InstanceRenderer")) return "InstanceRenderer";
        if (components.Contains("Chair") || group == "Chair") return "Chair";
        if (components.Contains("ArmorStand") || group == "ArmorStand") return "ArmorStand";
        if (group == "Table" || lower.Contains("table")) return "Table";
        if (group == "Banner" || lower.Contains("banner")) return "Banner";
        if (group == "Rug" || lower.Contains("rug")) return "Rug";
        if (group == "Statue" || lower.Contains("statue")) return "Statue";
        if (group == "Treasure" || ContainsAny(lower, "treasure", "coin", "chest")) return "Treasure";
        if (ContainsAny(lower, "barrel", "barrell", "crate", "pot", "jar")) return "ContainerProp";
        if (lower.Contains("clutter")) return "Clutter";
        if (ContainsAny(lower, "frac", "fragment", "broken")) return "Fragment";
        if (lower.Contains("_lod") || lower.EndsWith("lod", StringComparison.Ordinal)) return "LOD";
        return string.IsNullOrWhiteSpace(group) || group == "None" ? GetGeneralBucketName(components, lower) : group;
    }

    private static int GetGeneralBucketSortKey(IReadOnlyCollection<string> components, string lower)
    {
        if (components.Contains("Piece")) return 10;
        if (components.Contains("WearNTear")) return 20;
        if (components.Contains("Destructible")) return 30;
        if (components.Contains("ParticleSystem")) return 40;
        return 1000;
    }

    private static string GetGeneralBucketName(IReadOnlyCollection<string> components, string lower)
    {
        return components.Count > 0 ? components.OrderBy(component => component, StringComparer.OrdinalIgnoreCase).First() : "Static";
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(value.Contains);
    }
}
