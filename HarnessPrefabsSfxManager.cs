#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;
using EffectData = EffectList.EffectData;

namespace HarnessPrefabs;

internal static class HarnessPrefabsSfxManager
{
    private static readonly Dictionary<string, EffectData> PlacementSfx = new(StringComparer.Ordinal)
    {
        ["sfx_build_hammer_metal"] = null,
        ["sfx_build_hammer_stone"] = null,
        ["sfx_build_hammer_default"] = null
    };

    private static readonly Dictionary<string, EffectData> RemovalSfx = new(StringComparer.Ordinal)
    {
        ["sfx_rock_destroyed"] = null,
        ["sfx_wood_destroyed"] = null
    };

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        InitializeSfx(PlacementSfx);
        InitializeSfx(RemovalSfx);
    }

    public static void FixPlacementSfx(Piece piece)
    {
        if (!piece)
        {
            return;
        }

        Initialize();
        piece.m_placeEffect ??= new EffectList();
        EffectData[] effects = piece.m_placeEffect.m_effectPrefabs ?? Array.Empty<EffectData>();
        foreach (EffectData effect in effects)
        {
            if (effect?.m_prefab && effect.m_prefab.name.StartsWith("sfx_", StringComparison.Ordinal))
            {
                effect.m_enabled = true;
                return;
            }
        }

        string sfxName = ResolvePlacementSfxName(piece.m_craftingStation);
        if (!PlacementSfx.TryGetValue(sfxName, out EffectData sfx) || sfx == null)
        {
            return;
        }

        List<EffectData> merged = effects.ToList();
        merged.Add(CloneEffectData(sfx));
        piece.m_placeEffect.m_effectPrefabs = merged.ToArray();
    }

    public static bool HasSfx(EffectList effectList)
    {
        EffectData[] effects = effectList?.m_effectPrefabs;
        if (effects == null || effects.Length == 0)
        {
            return false;
        }

        return effects.Any(effect => effect?.m_prefab && effect.m_prefab.name.StartsWith("sfx_", StringComparison.Ordinal));
    }

    public static EffectList FixRemovalSfx(WearNTear wearNTear)
    {
        Piece piece = wearNTear ? wearNTear.GetComponent<Piece>() : null;
        CraftingStation station = piece ? piece.m_craftingStation : null;
        return FixRemovalSfx(wearNTear ? wearNTear.m_destroyedEffect : null, station);
    }

    private static EffectList FixRemovalSfx(EffectList effectList, CraftingStation station)
    {
        Initialize();
        List<EffectData> effects = (effectList?.m_effectPrefabs ?? Array.Empty<EffectData>())
            .Where(effect => effect != null)
            .Select(CloneEffectData)
            .ToList();

        string sfxName = station && station.name == CraftingStations.Stonecutter ? "sfx_rock_destroyed" : "sfx_wood_destroyed";
        if (RemovalSfx.TryGetValue(sfxName, out EffectData sfx) && sfx != null)
        {
            effects.Add(CloneEffectData(sfx));
        }

        return new EffectList
        {
            m_effectPrefabs = effects.ToArray()
        };
    }

    private static string ResolvePlacementSfxName(CraftingStation station)
    {
        if (!station || string.IsNullOrEmpty(station.m_name) || station.name == CraftingStations.Workbench || station.name == CraftingStations.BlackForge)
        {
            return "sfx_build_hammer_default";
        }

        if (station.name == CraftingStations.Stonecutter)
        {
            return "sfx_build_hammer_stone";
        }

        return station.name == CraftingStations.Forge ? "sfx_build_hammer_metal" : "sfx_build_hammer_default";
    }

    private static void InitializeSfx(Dictionary<string, EffectData> sfxDict)
    {
        foreach (string name in sfxDict.Keys.ToList())
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(name);
            if (!prefab && ZNetScene.instance)
            {
                prefab = ZNetScene.instance.GetPrefab(name);
            }

            if (!prefab)
            {
                if (HarnessPrefabsPlugin.Verbose)
                {
                    HarnessPrefabsPlugin.Log.LogWarning($"SFX prefab '{name}' could not be found.");
                }

                continue;
            }

            sfxDict[name] = new EffectData
            {
                m_prefab = prefab,
                m_enabled = true,
                m_variant = -1
            };
        }
    }

    private static EffectData CloneEffectData(EffectData source)
    {
        if (source == null)
        {
            return null;
        }

        return new EffectData
        {
            m_prefab = source.m_prefab,
            m_enabled = source.m_enabled,
            m_variant = source.m_variant,
            m_attach = source.m_attach,
            m_follow = source.m_follow,
            m_inheritParentRotation = source.m_inheritParentRotation,
            m_inheritParentScale = source.m_inheritParentScale,
            m_multiplyParentVisualScale = source.m_multiplyParentVisualScale,
            m_randomRotation = source.m_randomRotation,
            m_scale = source.m_scale,
            m_childTransform = source.m_childTransform
        };
    }
}
