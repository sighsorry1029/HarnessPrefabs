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
    public const string Misc = "Misc";
    public const string Crafting = "Crafting";
    public const string Building = "Building";
    public const string Stonecutter = "BuildingStonecutter";
    public const string Furniture = "Furniture";
    public const string HarnessProps = "Harness Props";
    public const string HarnessNature = "Harness Nature";
    public const string HarnessStructures = "Harness Structures";

    private static readonly Dictionary<string, string> CanonicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [Misc] = Misc,
        [Crafting] = Crafting,
        [Building] = Building,
        ["BuildingWorkbench"] = Building,
        [Stonecutter] = Stonecutter,
        ["Stonecutter"] = Stonecutter,
        [Furniture] = Furniture,
        [HarnessProps] = HarnessProps,
        [HarnessNature] = HarnessNature,
        [HarnessStructures] = HarnessStructures
    };

    public static bool IsAdminCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        return NormalizeCategory(categoryName) is HarnessProps or HarnessNature or HarnessStructures;
    }

    public static string NormalizeCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return HarnessProps;
        }

        string trimmed = categoryName.Trim();
        return CanonicalNames.TryGetValue(trimmed, out string canonicalName)
            ? canonicalName
            : trimmed;
    }
}

internal static class PrefabSortPolicy
{
    public static IEnumerable<PrefabDiscovery> SortForHammer(IEnumerable<PrefabDiscovery> discoveries)
    {
        return discoveries
            .Select(discovery => (Discovery: discovery, Key: CreateSortKey(discovery)))
            .OrderBy(entry => entry.Key.CategoryOrder)
            .ThenBy(entry => entry.Key.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key.BucketOrder)
            .ThenBy(entry => entry.Key.BucketName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Key.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Discovery.Name, StringComparer.Ordinal)
            .Select(entry => entry.Discovery);
    }

    private static (int CategoryOrder, string CategoryName, int BucketOrder, string BucketName, string DisplayName) CreateSortKey(PrefabDiscovery discovery)
    {
        bool hasRule = PrefabRuleStore.TryGetRule(discovery.Name, out PrefabRule rule);
        string category = BuildCategories.NormalizeCategory(hasRule ? rule.Category : discovery.Category);
        ComponentList components = hasRule && rule.Components is { Count: > 0 }
            ? rule.Components
            : discovery.Components;
        string group = MvbpPrefabDefaults.GetPieceGroup(discovery.Name);
        string lower = discovery.Name.ToLowerInvariant();
        (int Order, string Name) bucket = category switch
        {
            BuildCategories.Crafting => GetCraftingBucket(components, lower),
            BuildCategories.Building or BuildCategories.Stonecutter or BuildCategories.HarnessStructures => GetStructureBucket(components, group, lower),
            BuildCategories.Furniture or BuildCategories.HarnessProps => GetPropBucket(components, group, lower),
            BuildCategories.HarnessNature => GetNatureBucket(components, group, lower),
            _ => GetGeneralBucket(components)
        };
        string displayName = hasRule && !string.IsNullOrWhiteSpace(rule.DisplayName)
            ? rule.DisplayName.Trim()
            : discovery.Name;
        return (GetCategoryOrder(category), category, bucket.Order, bucket.Name, displayName);
    }

    private static int GetCategoryOrder(string category)
    {
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

    private static (int Order, string Name) GetCraftingBucket(IReadOnlyCollection<string> components, string lower)
    {
        if (components.Contains("CraftingStation")) return (10, "CraftingStation");
        if (components.Contains("StationExtension")) return (20, "StationExtension");
        if (components.Contains("CookingStation")) return (30, "CookingStation");
        if (components.Contains("Smelter")) return (40, "Smelter");
        if (components.Contains("Fireplace") || lower.Contains("fire")) return (50, "Fire");
        return GetGeneralBucket(components);
    }

    private static (int Order, string Name) GetInteractiveBucket(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("Container") || group == "Chest" || lower.Contains("chest")) return (10, "Container");
        if (components.Contains("CraftingStation") || components.Contains("StationExtension")) return (20, "Crafting");
        if (components.Contains("CookingStation") || components.Contains("Smelter")) return (30, "Processing");
        if (components.Contains("Fireplace") || group is "Fire" or "Brazier" or "Torch") return (40, "Fire");
        if (components.Contains("TeleportWorld") || group == "Portal") return (50, "Portal");
        if (components.Contains("Door") || lower.Contains("door") || lower.Contains("gate")) return (60, "Door");
        if (components.Contains("Bed") || group == "Bed") return (70, "Bed");
        if (components.Contains("Ship") || components.Contains("Vagon") || group == "Ship") return (80, "Vehicle");
        if (components.Contains("ArmorStand") || group == "ArmorStand") return (90, "ArmorStand");
        if (components.Contains("PrivateArea")) return (100, "PrivateArea");
        return (1000, "");
    }

    private static (int Order, string Name) GetNatureBucket(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("Plant") || group is "Plant" or "VanillaCrop") return (10, "Plant");
        if (components.Contains("Pickable")) return (20, "Pickable");
        if (components.Contains("TreeBase") || lower.Contains("tree")) return (30, "Tree");
        if (components.Contains("TreeLog") || lower.Contains("log")) return (40, "TreeLog");
        if (components.Contains("MineRock") || components.Contains("MineRock5") || group is "Rock" or "Ore" or "Ice") return (50, "RockOre");
        if (group == "Flora" || ContainsAny(lower, "bush", "fern", "grass", "reed", "moss", "vine", "root")) return (60, "Flora");
        if (ContainsAny(lower, "rock", "cliff", "stoneformation")) return (70, "RockStatic");
        return GetGeneralBucket(components);
    }

    private static (int Order, string Name) GetStructureBucket(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (ContainsAny(lower, "floor", "foundation")) return (10, "Floor");
        if (ContainsAny(lower, "wall", "window")) return (20, "Wall");
        if (ContainsAny(lower, "roof")) return (30, "Roof");
        if (ContainsAny(lower, "stair", "ramp")) return (40, "StairRamp");
        if (ContainsAny(lower, "beam", "pole", "pillar", "column")) return (50, "Support");
        if (ContainsAny(lower, "arch", "bridge", "ruin", "fortress")) return (60, "Shell");
        if (ContainsAny(lower, "gate", "door")) return (70, "GateDoor");

        (int Order, string Name) interactive = GetInteractiveBucket(components, group, lower);
        if (interactive.Order < 1000) return (100 + interactive.Order, "Interactive" + interactive.Name);
        if (components.Contains("Destructible")) return (220, "Destructible");
        if (components.Contains("WearNTear")) return (230, "WearNTear");
        if (components.Contains("Piece")) return (240, "Piece");
        if (!string.IsNullOrWhiteSpace(group) && group != "None") return (250, group);
        return GetGeneralBucket(components);
    }

    private static (int Order, string Name) GetPropBucket(IReadOnlyCollection<string> components, string group, string lower)
    {
        if (components.Contains("InstanceRenderer")) return (5, "InstanceRenderer");
        if (components.Contains("Chair") || group == "Chair") return (10, "Chair");
        if (components.Contains("ArmorStand") || group == "ArmorStand") return (20, "ArmorStand");
        if (group == "Table" || lower.Contains("table")) return (30, "Table");
        if (group == "Banner" || lower.Contains("banner")) return (40, "Banner");
        if (group == "Rug" || lower.Contains("rug")) return (50, "Rug");
        if (group == "Statue" || lower.Contains("statue")) return (60, "Statue");
        if (group == "Treasure" || ContainsAny(lower, "treasure", "coin", "chest")) return (70, "Treasure");
        if (ContainsAny(lower, "barrel", "barrell", "crate", "pot", "jar")) return (80, "ContainerProp");
        if (!string.IsNullOrWhiteSpace(group) && group != "None")
        {
            string groupedName = lower.Contains("clutter") ? "Clutter"
                : ContainsAny(lower, "frac", "fragment", "broken") ? "Fragment"
                : lower.Contains("_lod") || lower.EndsWith("lod", StringComparison.Ordinal) ? "LOD"
                : group;
            return (90, groupedName);
        }
        if (lower.Contains("clutter")) return (100, "Clutter");
        if (ContainsAny(lower, "frac", "fragment", "broken")) return (110, "Fragment");
        if (lower.Contains("_lod") || lower.EndsWith("lod", StringComparison.Ordinal)) return (120, "LOD");
        return GetGeneralBucket(components);
    }

    private static (int Order, string Name) GetGeneralBucket(IReadOnlyCollection<string> components)
    {
        int order = components.Contains("Piece") ? 10
            : components.Contains("WearNTear") ? 20
            : components.Contains("Destructible") ? 30
            : components.Contains("ParticleSystem") ? 40
            : 1000;
        string name = components.Count > 0
            ? components.OrderBy(component => component, StringComparer.OrdinalIgnoreCase).First()
            : "Static";
        return (order, name);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(value.Contains);
    }
}
