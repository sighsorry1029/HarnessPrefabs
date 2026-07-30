#nullable disable

using System;
using UnityEngine;

namespace HarnessPrefabs;

internal static class HarnessPrefabsRuntime
{
    public static bool TryGetManagedRule(GameObject gameObject, out PrefabRule rule)
    {
        rule = null;
        if (!gameObject)
        {
            return false;
        }

        return TryGetManagedRule(gameObject.name, out rule);
    }

    public static bool TryGetManagedRule(Component component, out PrefabRule rule)
    {
        return TryGetManagedRule(component ? component.gameObject : null, out rule);
    }

    public static bool TryGetKnownManagedPlacedPiece(Component component, out Piece piece)
    {
        piece = component ? component.GetComponentInParent<Piece>() : null;
        if (!piece || !piece.IsPlacedByPlayer())
        {
            return false;
        }

        string normalizedName = NormalizePrefabName(piece.gameObject.name);
        return normalizedName.Length > 0 && PrefabRuleStore.TryGetRule(normalizedName, out _);
    }

    public static bool TryGetManagedRule(string prefabName, out PrefabRule rule)
    {
        rule = null;
        string normalizedName = NormalizePrefabName(prefabName);
        if (normalizedName.Length == 0)
        {
            return false;
        }

        return PrefabRuleStore.TryGetRule(normalizedName, out rule) && rule.Access != PrefabAccess.Hidden;
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
