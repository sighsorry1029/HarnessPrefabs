#nullable disable

using System;
using UnityEngine;

namespace HarnessPrefabs;

internal static class HarnessPrefabsRuntime
{
    public static bool IsMaterializedPrefab(Component component)
    {
        return TryGetPiecePrefabName(component, out string prefabName) &&
               HarnessPrefabsPlacedPiecePatches.IsMaterializedPrefab(prefabName);
    }

    public static bool WasMaterializedPrefab(Component component)
    {
        return TryGetPiecePrefabName(component, out string prefabName) &&
               HarnessPrefabsPlacedPiecePatches.WasMaterializedPrefab(prefabName);
    }

    public static bool TryGetRuntimeRule(Component component, out PrefabRule rule)
    {
        rule = null;
        if (!TryGetPiecePrefabName(component, out string prefabName) ||
            !PrefabRuleStore.TryGetRule(prefabName, out rule))
        {
            return false;
        }

        return rule.Access == PrefabAccess.Admin ||
               HarnessPrefabsPlacedPiecePatches.WasMaterializedPrefab(prefabName);
    }

    public static bool TryGetKnownManagedPlacedPiece(Component component, out Piece piece)
    {
        piece = component ? component.GetComponentInParent<Piece>() : null;
        if (!piece || !piece.IsPlacedByPlayer())
        {
            return false;
        }

        return TryGetRuntimeRule(piece, out _);
    }

    private static bool TryGetPiecePrefabName(Component component, out string prefabName)
    {
        prefabName = "";
        if (!component)
        {
            return false;
        }

        Piece piece = component.GetComponentInParent<Piece>();
        prefabName = NormalizePrefabName(piece ? piece.gameObject.name : "");
        return prefabName.Length > 0;
    }

    public static string NormalizePrefabName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        name = name.Trim();
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - cloneSuffix.Length).Trim();
        }

        return name;
    }

}
