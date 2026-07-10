using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarnessPrefabs;

internal static class PrefabPlacementPatchRegistry
{
    private const float MinBoundsSize = 0.001f;
    private static readonly HashSet<string> PrefabNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Vector3> PlacementOffsets = new(StringComparer.Ordinal);

    public static void Clear()
    {
        PrefabNames.Clear();
        PlacementOffsets.Clear();
    }

    public static void Set(string prefabName, bool enabled, Vector3? placementOffset)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return;
        }

        prefabName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        if (enabled)
        {
            PrefabNames.Add(prefabName);
        }
        else
        {
            PrefabNames.Remove(prefabName);
        }

        if (placementOffset.HasValue && placementOffset.Value.sqrMagnitude > MinBoundsSize * MinBoundsSize)
        {
            PlacementOffsets[prefabName] = placementOffset.Value;
        }
        else
        {
            PlacementOffsets.Remove(prefabName);
        }
    }

    public static void PatchGhost(GameObject ghost)
    {
        if (!ghost)
        {
            return;
        }

        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(ghost.name);
        if (!PrefabNames.Contains(prefabName) || ghost.GetComponent<HarnessPrefabsPlacementPatchMarker>())
        {
            return;
        }

        if (!TryGetRendererBounds(ghost, out Bounds bounds))
        {
            return;
        }

        BoxCollider collider = ghost.AddComponent<BoxCollider>();
        collider.center = ghost.transform.InverseTransformPoint(bounds.center);
        collider.size = WorldSizeToLocalSize(ghost.transform, bounds.size);
        collider.isTrigger = false;

        ghost.AddComponent<HarnessPrefabsPlacementPatchMarker>();

        if (HarnessPrefabsPlugin.Verbose)
        {
            HarnessPrefabsPlugin.Log.LogInfo($"Applied placement collider patch to ghost '{prefabName}'.");
        }
    }

    public static void ApplyGhostOffset(GameObject ghost)
    {
        if (!ghost)
        {
            return;
        }

        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(ghost.name);
        if (PlacementOffsets.TryGetValue(prefabName, out Vector3 offset))
        {
            ghost.transform.position += ghost.transform.rotation * offset;
        }
    }

    private static bool TryGetRendererBounds(GameObject prefab, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (!renderer)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return hasBounds && bounds.size.sqrMagnitude > MinBoundsSize * MinBoundsSize;
    }

    private static Vector3 WorldSizeToLocalSize(Transform transform, Vector3 worldSize)
    {
        Vector3 scale = transform.lossyScale;
        return new Vector3(
            DivideByScale(worldSize.x, scale.x),
            DivideByScale(worldSize.y, scale.y),
            DivideByScale(worldSize.z, scale.z));
    }

    private static float DivideByScale(float value, float scale)
    {
        float absScale = Mathf.Abs(scale);
        return absScale < MinBoundsSize ? value : value / absScale;
    }

}

internal sealed class HarnessPrefabsPlacementPatchMarker : MonoBehaviour
{
}
