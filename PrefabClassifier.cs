using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HarnessPrefabs;

internal static class PrefabClassifier
{
    private static readonly ComponentProbe[] ComponentProbes =
    {
        Root<Piece>(),
        Root<WearNTear>(),
        Root<Door>(),
        Root<Destructible>(),
        Root<PrivateArea>(),
        Root<Ship>(),
        Root<Vagon>(),
        Root<TeleportWorld>(),
        Root<Bed>(),
        Root<Chair>(),
        Root<ArmorStand>(),
        Root<Container>(),
        Root<CraftingStation>(),
        Root<StationExtension>(),
        Root<CookingStation>(),
        Root<Smelter>(),
        Root<Fireplace>(),
        Root<Pickable>(),
        Root<Plant>(),
        Root<MineRock>(),
        Root<MineRock5>(),
        Child<TreeBase>(),
        Child<TreeLog>(),
        Child<InstanceRenderer>(),
        Child<ParticleSystem>(),
        Child<TimedDestruction>(),
        Child<Projectile>(),
        Child<Ragdoll>(),
        Child<Aoe>(),
        Child<CamShaker>()
    };

    private static readonly Type[] AlwaysExcludedChildTypes =
    {
        typeof(ItemDrop),
        typeof(Humanoid),
        typeof(Character),
        typeof(AnimalAI),
        typeof(CreatureSpawner),
        typeof(SpawnArea),
        typeof(TriggerSpawner),
        typeof(DungeonGenerator),
        typeof(TerrainModifier),
        typeof(EventZone),
        typeof(LocationProxy),
        typeof(LootSpawner),
        typeof(Mister),
        typeof(TombStone),
        typeof(LiquidVolume),
        typeof(Gibber),
        typeof(ShipConstructor),
        typeof(TeleportAbility),
        typeof(Trader),
        typeof(Fish),
        typeof(RandomFlyingBird),
        typeof(MusicLocation)
    };

    private static readonly Type[] RuntimeTypes =
    {
        typeof(Projectile),
        typeof(Aoe),
        typeof(CamShaker),
        typeof(Ragdoll)
    };

    private static readonly string[] NatureNameTokens =
    {
        "ashlands_rock", "rock", "cliff", "stoneformation", "tree", "treelog", "stump",
        "bush", "branch", "root", "vine", "fern", "flora", "grass", "reed", "moss",
        "sapling", "shrub", "leviathan"
    };

    private static readonly string[] InteractiveComponentNames =
    {
        "Door", "PrivateArea", "Ship", "Vagon", "TeleportWorld", "Bed", "Container", "CraftingStation",
        "StationExtension", "CookingStation", "Smelter", "Fireplace", "ArmorStand"
    };

    private static readonly HashSet<string> MvbpIgnoredPrefabs = new(StringComparer.Ordinal)
    {
        "CapeOdin",
        "CargoCrate",
        "CastleKit_brazier",
        "CastleKit_groundtorch_unlit",
        "CastleKit_metal_groundtorch_unlit",
        "CastleKit_pot03",
        "Charredfortress_LOD",
        "Circle_section",
        "FishingRodFloat",
        "Flies",
        "HelmetOdin",
        "Pickable_Barley_Wild",
        "Pickable_DolmenTreasure",
        "Pickable_DvergerThing",
        "Pickable_Flax_Wild",
        "Pickable_Item",
        "Pickable_RandomFood",
        "PlaceMarker",
        "Player",
        "Ravens",
        "SunkenKit_int_towerwall_LOD",
        "TERRAIN_TEST",
        "Trailership",
        "TreasureChest_blackforest",
        "TreasureChest_forestcrypt",
        "TreasureChest_forestcrypt_hildir",
        "TreasureChest_heath",
        "TreasureChest_heath_hildir",
        "TreasureChest_meadows",
        "TreasureChest_meadows_buried",
        "TreasureChest_mountaincave_hildir",
        "TreasureChest_mountains",
        "TreasureChest_plains_stone",
        "TreasureChest_plainsfortress_hildir",
        "TreasureChest_swamp",
        "Valkyrie",
        "demister_ball",
        "dragoneggcup",
        "dvergrprops_crate_ashlands",
        "dvergrprops_lantern",
        "dvergrprops_wood_stake",
        "fenrirhide_hanging_door",
        "fuling_turret",
        "guard_stone_test",
        "horizontal_web",
        "loot_chest_wood",
        "odin",
        "shipwreck_karve_chest",
        "stonechest",
        "tolroko_flyer",
        "turf_roof_wall"
    };

    public static List<PrefabDiscovery> Discover(IEnumerable<GameObject> prefabs, HashSet<string> existingBuildables)
    {
        List<PrefabDiscovery> discoveries = new();
        foreach (GameObject prefab in prefabs)
        {
            PrefabDiscovery? discovery = TryClassify(prefab, existingBuildables);
            if (discovery != null)
            {
                discoveries.Add(discovery);
            }
        }

        return discoveries;
    }

    private static PrefabDiscovery? TryClassify(GameObject prefab, HashSet<string> existingBuildables)
    {
        if (!prefab)
        {
            return null;
        }

        string name = prefab.name;
        bool hasMvbpDefault = MvbpPrefabDefaults.HasDefault(name);
        bool isExistingBuildable = existingBuildables.Contains(name);
        if (string.IsNullOrWhiteSpace(name) ||
            HasExcludedName(name) ||
            (prefab.transform.parent && !hasMvbpDefault) ||
            isExistingBuildable)
        {
            return null;
        }

        ComponentSnapshot snapshot = new(prefab);
        if (HasExcludedComponent(snapshot, hasMvbpDefault))
        {
            return null;
        }

        List<string> components = CollectComponents(snapshot);
        if (IsRuntimePrefab(name, components, hasMvbpDefault))
        {
            return null;
        }

        bool hasRenderable = HasRenderable(snapshot);
        if (components.Count == 0 && !hasRenderable)
        {
            return null;
        }

        bool hasNetworkView = snapshot.Has(typeof(ZNetView), includeChildren: true);
        if (!hasMvbpDefault && !hasNetworkView)
        {
            return null;
        }

        PrefabAccess access;
        string category;
        bool isNature = IsNature(name, components);
        bool clipEverything = ShouldClipEverything(name, components);
        bool clipGround = isNature || name.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("cliff", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isNature)
        {
            access = PrefabAccess.Admin;
            category = BuildCategories.HarnessNature;
        }
        else if (IsInteractive(components))
        {
            access = PrefabAccess.Admin;
            category = BuildCategories.HarnessStructures;
        }
        else if (components.Contains("Piece") || components.Contains("WearNTear"))
        {
            access = PrefabAccess.Admin;
            category = GuessPublicCategory(name, components) == BuildCategories.Furniture
                ? BuildCategories.HarnessProps
                : BuildCategories.HarnessStructures;
        }
        else if (IsDestructibleOnly(components))
        {
            access = PrefabAccess.Admin;
            category = BuildCategories.HarnessStructures;
        }
        else
        {
            access = PrefabAccess.Admin;
            category = BuildCategories.HarnessProps;
        }

        return new PrefabDiscovery
        {
            Name = name,
            Access = access,
            Category = category,
            ClipEverything = clipEverything,
            ClipGround = clipGround,
            AllowedInDungeons = false,
            Components = new ComponentList(components),
            Prefab = prefab
        };
    }

    private static List<string> CollectComponents(ComponentSnapshot snapshot)
    {
        SortedSet<string> components = new(StringComparer.Ordinal);
        foreach (ComponentProbe probe in ComponentProbes)
        {
            if (snapshot.Has(probe.Type, probe.IncludeChildren))
            {
                components.Add(probe.Name);
            }
        }

        return components.ToList();
    }

    private static bool HasExcludedComponent(ComponentSnapshot snapshot, bool hasMvbpDefault)
    {
        return snapshot.HasAny(AlwaysExcludedChildTypes, includeChildren: true)
               || snapshot.HasAny(RuntimeTypes, includeChildren: !hasMvbpDefault);
    }

    private static bool HasExcludedName(string name)
    {
        return MvbpIgnoredPrefabs.Contains(name)
               || name.StartsWith("_", StringComparison.Ordinal)
               || name.StartsWith("BBH_", StringComparison.Ordinal)
               || name.StartsWith("rrr_", StringComparison.Ordinal)
               || name.StartsWith("CLLC_", StringComparison.Ordinal)
               || name.StartsWith("OLD_", StringComparison.Ordinal)
               || name.EndsWith("OLD", StringComparison.Ordinal)
               || name.EndsWith("_old", StringComparison.Ordinal)
               || name.EndsWith("_test", StringComparison.Ordinal)
               || name.IndexOf("Random", StringComparison.Ordinal) >= 0
               || name.IndexOf("random", StringComparison.Ordinal) >= 0;
    }

    private static ComponentProbe Root<T>() where T : Component
    {
        return new ComponentProbe(typeof(T), includeChildren: false);
    }

    private static ComponentProbe Child<T>() where T : Component
    {
        return new ComponentProbe(typeof(T), includeChildren: true);
    }

    private readonly struct ComponentProbe
    {
        public ComponentProbe(Type type, bool includeChildren)
        {
            Type = type;
            IncludeChildren = includeChildren;
            Name = type.Name;
        }

        public Type Type { get; }
        public bool IncludeChildren { get; }
        public string Name { get; }
    }

    private sealed class ComponentSnapshot
    {
        private readonly Type[] _rootTypes;
        private readonly Type[] _allTypes;

        public ComponentSnapshot(GameObject prefab)
        {
            _rootTypes = CollectTypes(prefab.GetComponents<Component>());
            _allTypes = CollectTypes(prefab.GetComponentsInChildren<Component>(true));
        }

        public bool Has(Type componentType, bool includeChildren)
        {
            Type[] types = includeChildren ? _allTypes : _rootTypes;
            return types.Any(componentType.IsAssignableFrom);
        }

        public bool HasAny(IEnumerable<Type> componentTypes, bool includeChildren)
        {
            return componentTypes.Any(type => Has(type, includeChildren));
        }

        private static Type[] CollectTypes(IEnumerable<Component> components)
        {
            return components
                .Where(component => component)
                .Select(component => component.GetType())
                .Distinct()
                .ToArray();
        }
    }

    private static bool HasRenderable(ComponentSnapshot snapshot)
    {
        return snapshot.Has(typeof(Renderer), includeChildren: true) ||
               snapshot.Has(typeof(Collider), includeChildren: true);
    }

    private static bool IsRuntimePrefab(string name, IReadOnlyCollection<string> components, bool hasMvbpDefault)
    {
        if (IsRuntimeName(name) || MvbpPrefabDefaults.IsRuntimeEffect(name))
        {
            return true;
        }

        if (hasMvbpDefault)
        {
            return false;
        }

        return components.Contains("TimedDestruction");
    }

    private static bool IsRuntimeName(string name)
    {
        return name.StartsWith("fx_", StringComparison.Ordinal)
               || name.StartsWith("vfx_", StringComparison.Ordinal)
               || name.StartsWith("sfx_", StringComparison.Ordinal);
    }

    private static bool IsNature(string name, IReadOnlyCollection<string> components)
    {
        return IsNatureComponent(components) || IsNatureName(name);
    }

    private static bool IsNatureComponent(IReadOnlyCollection<string> components)
    {
        return components.Contains("Pickable") || components.Contains("Plant") || components.Contains("MineRock") || components.Contains("MineRock5") || components.Contains("TreeBase") || components.Contains("TreeLog");
    }

    private static bool IsNatureName(string name)
    {
        string lower = name.ToLowerInvariant();
        return NatureNameTokens.Any(lower.Contains);
    }

    private static bool IsInteractive(IReadOnlyCollection<string> components)
    {
        return InteractiveComponentNames.Any(components.Contains);
    }

    private static bool IsDestructibleOnly(IReadOnlyCollection<string> components)
    {
        return components.Count == 1 && components.Contains("Destructible");
    }

    private static bool ShouldClipEverything(string name, IReadOnlyCollection<string> components)
    {
        return IsNature(name, components)
               || components.Contains("InstanceRenderer")
               || name.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("cliff", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("web", StringComparison.OrdinalIgnoreCase) >= 0
               || name.IndexOf("vine", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GuessPublicCategory(string name, IReadOnlyCollection<string> components)
    {
        string lower = name.ToLowerInvariant();
        if (components.Contains("Chair") || lower.Contains("chair") || lower.Contains("bench") || lower.Contains("table") || lower.Contains("rug") || lower.Contains("banner") || lower.Contains("curtain"))
        {
            return BuildCategories.Furniture;
        }

        if (lower.Contains("stone") || lower.Contains("marble") || lower.Contains("grausten"))
        {
            return BuildCategories.Stonecutter;
        }

        if (components.Contains("WearNTear") || lower.Contains("wood") || lower.Contains("wall") || lower.Contains("floor") || lower.Contains("roof") || lower.Contains("beam") || lower.Contains("stair"))
        {
            return BuildCategories.Building;
        }

        return BuildCategories.Misc;
    }
}
