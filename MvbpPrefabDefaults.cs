using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HarnessPrefabs;

internal readonly struct MvbpPrefabDefault
{
    public readonly bool Enabled;
    public readonly bool AllowedInDungeons;
    public readonly string Category;
    public readonly string CraftingStation;
    public readonly string Requirements;
    public readonly bool ClipEverything;
    public readonly bool ClipGround;
    public readonly bool PlacementPatch;
    public readonly string? DisplayName;
    public readonly string? Description;
    public readonly string PieceGroup;

    public MvbpPrefabDefault(
        bool enabled,
        bool allowedInDungeons,
        string category,
        string craftingStation,
        string requirements,
        bool clipEverything,
        bool clipGround,
        bool placementPatch,
        string? displayName,
        string? description,
        string pieceGroup)
    {
        Enabled = enabled;
        AllowedInDungeons = allowedInDungeons;
        Category = string.IsNullOrWhiteSpace(category) ? "CreatorShop" : category;
        CraftingStation = string.IsNullOrWhiteSpace(craftingStation) ? "None" : craftingStation;
        Requirements = requirements ?? "";
        ClipEverything = clipEverything;
        ClipGround = clipGround;
        PlacementPatch = placementPatch;
        DisplayName = displayName;
        Description = description;
        PieceGroup = string.IsNullOrWhiteSpace(pieceGroup) ? "None" : pieceGroup;
    }
}

internal static partial class MvbpPrefabDefaults
{
    public static IEnumerable<string> Names => Defaults.Keys;

    public static bool HasDefault(string prefabName)
    {
        return Defaults.ContainsKey(prefabName);
    }

    public static bool IsRuntimeEffect(string prefabName)
    {
        string normalizedName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        return normalizedName.Length > 0 &&
               Defaults.TryGetValue(normalizedName, out MvbpPrefabDefault seed) &&
               (seed.Category == "Effect" || seed.PieceGroup == "Effect");
    }

    public static bool NeedsPlacementPatch(string prefabName)
    {
        string normalizedName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        return normalizedName.Length > 0 &&
               Defaults.TryGetValue(normalizedName, out MvbpPrefabDefault seed) &&
               seed.PlacementPatch;
    }

    public static string GetPieceGroup(string prefabName)
    {
        string normalizedName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        return normalizedName.Length > 0 &&
               Defaults.TryGetValue(normalizedName, out MvbpPrefabDefault seed)
            ? seed.PieceGroup
            : "";
    }

    public static bool TryCreateRule(PrefabDiscovery discovery, out PrefabRule rule)
    {
        rule = null!;
        if (!Defaults.TryGetValue(discovery.Name, out MvbpPrefabDefault seed))
        {
            return false;
        }

        PrefabAccess access = ResolveAccess(discovery, seed);
        rule = new PrefabRule
        {
            Access = access,
            Category = ResolveCategory(discovery.Name, seed, access),
            DisplayName = access == PrefabAccess.Public ? ResolvePublicDisplayName(discovery, seed) : "",
            Description = access == PrefabAccess.Public ? ResolvePublicDescription(discovery, seed) : "",
            CraftingStation = seed.CraftingStation,
            Requirements = PrefabRequirementParser.ParseMany(seed.Requirements),
            ClipEverything = seed.ClipEverything,
            ClipGround = seed.ClipGround,
            AllowedInDungeons = seed.AllowedInDungeons,
            CanBeRemoved = ResolveCanBeRemoved(seed),
            Components = NormalizeComponents(discovery.Components)
        };
        return true;
    }

    private static bool ResolveCanBeRemoved(MvbpPrefabDefault seed)
    {
        return !seed.PieceGroup.Equals("Ship", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePublicDisplayName(PrefabDiscovery discovery, MvbpPrefabDefault seed)
    {
        return seed.DisplayName ?? discovery.Name;
    }

    private static string ResolvePublicDescription(PrefabDiscovery discovery, MvbpPrefabDefault seed)
    {
        if (seed.Description != null)
        {
            return seed.Description;
        }

        return FindMvbpPieceDescription(discovery.Prefab);
    }

    private static string FindMvbpPieceDescription(GameObject? prefab, HashSet<string>? visited = null)
    {
        if (!prefab)
        {
            return "";
        }

        visited ??= new HashSet<string>(StringComparer.Ordinal);
        if (!visited.Add(prefab.name))
        {
            return "";
        }

        HoverText hoverText = prefab.GetComponent<HoverText>();
        if (hoverText && !string.IsNullOrEmpty(hoverText.m_text))
        {
            return hoverText.m_text;
        }

        ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
        if (itemDrop && itemDrop.m_itemData?.m_shared?.m_name is { Length: > 0 } itemName)
        {
            return itemName;
        }

        Character character = prefab.GetComponent<Character>();
        if (character && !string.IsNullOrEmpty(character.m_name))
        {
            return character.m_name;
        }

        RuneStone runeStone = prefab.GetComponent<RuneStone>();
        if (runeStone && !string.IsNullOrEmpty(runeStone.m_name))
        {
            return runeStone.m_name;
        }

        ItemStand itemStand = prefab.GetComponent<ItemStand>();
        if (itemStand && !string.IsNullOrEmpty(itemStand.m_name))
        {
            return itemStand.m_name;
        }

        MineRock mineRock = prefab.GetComponent<MineRock>();
        if (mineRock && !string.IsNullOrEmpty(mineRock.m_name))
        {
            return mineRock.m_name;
        }

        Pickable pickable = prefab.GetComponent<Pickable>();
        if (pickable && pickable.m_itemPrefab)
        {
            return FindMvbpPieceDescription(pickable.m_itemPrefab, visited);
        }

        CreatureSpawner creatureSpawner = prefab.GetComponent<CreatureSpawner>();
        if (creatureSpawner && creatureSpawner.m_creaturePrefab)
        {
            return FindMvbpPieceDescription(creatureSpawner.m_creaturePrefab, visited);
        }

        SpawnArea spawnArea = prefab.GetComponent<SpawnArea>();
        if (spawnArea && spawnArea.m_prefabs.Count > 0 && spawnArea.m_prefabs[0].m_prefab)
        {
            return FindMvbpPieceDescription(spawnArea.m_prefabs[0].m_prefab, visited);
        }

        return "";
    }

    private static PrefabAccess ResolveAccess(PrefabDiscovery discovery, MvbpPrefabDefault seed)
    {
        if (discovery.Access == PrefabAccess.Hidden)
        {
            return PrefabAccess.Hidden;
        }

        if (IsAdminOnlyCategory(seed.Category))
        {
            return PrefabAccess.Admin;
        }

        return seed.Enabled ? PrefabAccess.Public : PrefabAccess.Admin;
    }

    private static string ResolveCategory(string prefabName, MvbpPrefabDefault seed, PrefabAccess access)
    {
        if (access == PrefabAccess.Public)
        {
            return MapPublicCategory(seed.Category);
        }

        return MapAdminCategory(prefabName, seed);
    }

    private static string MapPublicCategory(string category)
    {
        return category switch
        {
            "BuildingWorkbench" => BuildCategories.Building,
            "BuildingStonecutter" => BuildCategories.Stonecutter,
            "Furniture" => BuildCategories.Furniture,
            "Misc" => BuildCategories.Misc,
            "Crafting" => BuildCategories.Crafting,
            _ => BuildCategories.Misc
        };
    }

    private static string MapAdminCategory(string prefabName, MvbpPrefabDefault seed)
    {
        if (seed.Category == "Effect" || seed.PieceGroup == "Effect")
        {
            return BuildCategories.HarnessProps;
        }

        if (seed.Category == "Nature" || IsNatureGroup(seed.PieceGroup))
        {
            return BuildCategories.HarnessNature;
        }

        if (IsInteractiveGroup(seed.PieceGroup))
        {
            return BuildCategories.HarnessStructures;
        }

        if (IsStructureGroup(seed.PieceGroup) ||
            seed.Category is "BuildingWorkbench" or "BuildingStonecutter" ||
            IsStructureName(prefabName) ||
            IsStructureStation(seed.CraftingStation))
        {
            return BuildCategories.HarnessStructures;
        }

        if (IsPropGroup(seed.PieceGroup) || seed.Category == "CreatorShop" || seed.Category == "Furniture")
        {
            return BuildCategories.HarnessProps;
        }

        return seed.Category switch
        {
            "Misc" or "Crafting" => BuildCategories.HarnessProps,
            _ => BuildCategories.HarnessProps
        };
    }

    private static bool IsAdminOnlyCategory(string category)
    {
        return category == "CreatorShop" || category == "Effect" || category == "Nature";
    }

    private static bool IsNatureGroup(string group)
    {
        return group is "Flora" or "Plant" or "Rock" or "Ore" or "Ice" or "VanillaCrop";
    }

    private static bool IsInteractiveGroup(string group)
    {
        return group is "ArmorStand" or "Bed" or "Brazier" or "Chest" or "Fire" or "Portal" or "Ship" or "Torch";
    }

    private static bool IsStructureGroup(string group)
    {
        return group is "BlackMarble" or "Dvergr" or "Goblin" or "Ice" or "Iron" or "Stone" or "Wood";
    }

    private static bool IsPropGroup(string group)
    {
        return group is "Banner" or "Chair" or "Misc" or "Rug" or "Statue" or "Table" or "Treasure";
    }

    private static bool IsStructureStation(string station)
    {
        return station is "BlackForge" or "Stonecutter";
    }

    private static bool IsStructureName(string prefabName)
    {
        string lower = prefabName.ToLowerInvariant();
        string[] tokens =
        {
            "arch", "beam", "bridge", "column", "corner", "door", "floor", "fortress",
            "foundation", "gate", "pillar", "pole", "railing", "ramp", "roof", "ruin",
            "stair", "steepstair", "wall"
        };
        return tokens.Any(lower.Contains);
    }

    private static ComponentList NormalizeComponents(IEnumerable<string> components)
    {
        return new ComponentList(components
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Select(component => component.Trim())
            .Where(component => !component.StartsWith("MVBP", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(component => component, StringComparer.OrdinalIgnoreCase));
    }

}
