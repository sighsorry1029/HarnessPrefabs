#nullable disable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarnessPrefabs;

internal static class MvbpCompatibilityDefaults
{
    private sealed class PatchDefaults
    {
        public Vector3? PlacementOffset { get; set; }
        public bool PlayerBasePatch { get; set; }
        public string SpawnOnDestroyed { get; set; } = "";
        public int? ContainerWidth { get; set; }
        public int? ContainerHeight { get; set; }
    }

    private static readonly Dictionary<string, PatchDefaults> Defaults = new(StringComparer.Ordinal)
    {
        ["AshlandsTree1"] = new() { PlacementOffset = new Vector3(0f, 1f, 0f) },
        ["AshlandsTree3"] = new() { PlacementOffset = new Vector3(0f, 1f, 0f) },
        ["AshlandsTree5"] = new() { PlacementOffset = new Vector3(0f, 0.5f, 0f) },
        ["Ashlands_Fortress_Wall_PillarTopStone_frac"] = new() { PlacementOffset = new Vector3(0f, -0.1f, 0f) },
        ["BogWitch_Fire_Pit"] = new() { PlacementOffset = new Vector3(0f, -1f, 0f) },
        ["dvergrprops_pickaxe"] = new() { PlacementOffset = new Vector3(-1f, 0f, 0f) },
        ["shipwreck_karve_dragonhead"] = new() { PlacementOffset = new Vector3(0f, -1.5f, 6f) },

        ["CastleKit_groundtorch"] = new() { PlayerBasePatch = true },
        ["CastleKit_groundtorch_blue"] = new() { PlayerBasePatch = true },
        ["CastleKit_groundtorch_green"] = new() { PlayerBasePatch = true },
        ["MountainKit_brazier"] = new() { PlayerBasePatch = true },
        ["MountainKit_brazier_blue"] = new() { PlayerBasePatch = true },
        ["dvergrprops_bed"] = new() { PlayerBasePatch = true },
        ["goblin_bed"] = new() { PlayerBasePatch = true },

        ["TreasureChest_ashland_stone"] = new() { ContainerWidth = 5, ContainerHeight = 2 },
        ["TreasureChest_charredfortress"] = new() { ContainerWidth = 8, ContainerHeight = 4 },
        ["TreasureChest_dvergr_loose_stone"] = new() { ContainerWidth = 8, ContainerHeight = 4 },
        ["TreasureChest_dvergrtower"] = new() { ContainerWidth = 7, ContainerHeight = 4 },
        ["TreasureChest_dvergrtown"] = new() { ContainerWidth = 8, ContainerHeight = 4 },
        ["TreasureChest_fCrypt"] = new() { ContainerWidth = 5, ContainerHeight = 2 },
        ["TreasureChest_mountaincave"] = new() { ContainerWidth = 6, ContainerHeight = 3 },
        ["TreasureChest_sunkencrypt"] = new() { ContainerWidth = 5, ContainerHeight = 2 },
        ["TreasureChest_trollcave"] = new() { ContainerWidth = 6, ContainerHeight = 3 },
        ["loot_chest_stone"] = new() { ContainerWidth = 5, ContainerHeight = 2 },
        ["ancient_skull"] = new() { SpawnOnDestroyed = "sfx_rock_destroyed" },
        ["flying_core"] = new() { SpawnOnDestroyed = "fx_crystal_destruction" },
        ["rock_mistlands2"] = new() { SpawnOnDestroyed = "sfx_rock_destroyed" }
    };

    public static Vector3? GetPlacementOffset(string prefabName)
    {
        return TryGetDefaults(prefabName, out PatchDefaults defaults) ? defaults.PlacementOffset : null;
    }

    public static bool HasPlayerBasePatch(string prefabName)
    {
        return TryGetDefaults(prefabName, out PatchDefaults defaults) && defaults.PlayerBasePatch;
    }

    public static bool TryGetSpawnOnDestroyed(string prefabName, out string spawnOnDestroyed)
    {
        spawnOnDestroyed = "";
        if (!TryGetDefaults(prefabName, out PatchDefaults defaults) || string.IsNullOrWhiteSpace(defaults.SpawnOnDestroyed))
        {
            return false;
        }

        spawnOnDestroyed = defaults.SpawnOnDestroyed;
        return true;
    }

    public static bool TryGetContainerSize(string prefabName, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!TryGetDefaults(prefabName, out PatchDefaults defaults) || !defaults.ContainerWidth.HasValue || !defaults.ContainerHeight.HasValue)
        {
            return false;
        }

        width = defaults.ContainerWidth.Value;
        height = defaults.ContainerHeight.Value;
        return true;
    }

    private static bool TryGetDefaults(string prefabName, out PatchDefaults defaults)
    {
        string normalizedName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        if (normalizedName.Length == 0)
        {
            defaults = null;
            return false;
        }

        return Defaults.TryGetValue(normalizedName, out defaults);
    }
}
