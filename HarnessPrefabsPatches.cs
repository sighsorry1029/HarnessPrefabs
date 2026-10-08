using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MagicaCloth2;
using UnityEngine;

namespace HarnessPrefabs;

[HarmonyPatch(typeof(VisEquipment), "SetupCloth", typeof(GameObject))]
internal static class ArmorStandSetupClothPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(VisEquipment __instance)
    {
        if (!__instance.m_isArmorStand || __instance.m_clothColliders == null || __instance.m_clothColliders.Count == 0)
            return;

        ArmorStand stand = __instance.GetComponentInParent<ArmorStand>(true);
        if (!stand || stand.m_visEquipment != __instance)
            return;

        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(stand.gameObject.name);
        if (prefabName != "ArmorStand_Male" && prefabName != "ArmorStand_Female")
            return;

        List<ColliderComponent>? filtered = FilterClothColliders(__instance.m_clothColliders);
        if (filtered == null)
            return;

        int removed = __instance.m_clothColliders.Count - filtered.Count;
        __instance.m_clothColliders = filtered;
        HarnessPrefabsPlugin.Log.LogWarning($"Ignored {removed} incompatible cloth collider reference(s) on {prefabName} before cloth initialization.");
    }

    // The 1.0.17 male/female assets serialize Unity CapsuleColliders into a
    // List<MagicaCloth2.ColliderComponent>. Read as object so the CLR checks the
    // actual type before MagicaCloth can dispatch a method on an invalid object.
    // Nulls are accepted by MagicaCloth. Leave them and all valid entries intact.
    // Return null when unchanged; never mutate a list already shared with a cloth.
    private static List<ColliderComponent>? FilterClothColliders(IList colliders)
    {
        List<ColliderComponent>? filtered = null;
        for (int i = 0; i < colliders.Count; i++)
        {
            object? candidate = colliders[i];
            if (candidate != null && candidate is not ColliderComponent)
            {
                if (filtered == null)
                {
                    filtered = new List<ColliderComponent>(colliders.Count - 1);
                    for (int j = 0; j < i; j++)
                        filtered.Add((ColliderComponent)colliders[j]!);
                }
            }
            else
            {
                filtered?.Add((ColliderComponent)candidate!);
            }
        }
        return filtered;
    }
}

[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class ZNetSceneAwakePatch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("com.jotunn.jotunn")]
    private static void Postfix(ZNetScene __instance)
    {
        PrefabBuildManager.BeginPrefabEpoch(__instance);
        PrefabBuildManager.Refresh("ZNetScene.Awake");
    }
}

[HarmonyPatch(typeof(ObjectDB), "Awake")]
internal static class ObjectDbAwakePatch
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("com.jotunn.jotunn")]
    private static void Postfix()
    {
        PrefabBuildManager.Refresh("ObjectDB.Awake");
    }
}

[HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
internal static class ZNetSceneDestroyPatch
{
    private static void Postfix(ZNetScene __instance) => PrefabBuildManager.EndPrefabEpoch(__instance);
}

[HarmonyPatch(typeof(ZoneSystem), "Start")]
internal static class ZoneSystemStartPatch
{
    private static void Postfix()
    {
        PrefabBuildManager.Refresh("ZoneSystem.Start");
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
internal static class PlayerSetLocalPlayerPatch
{
    private static void Postfix()
    {
        PrefabBuildManager.Refresh("local player ready");
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdatePatch
{
    private static void Postfix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            PrefabBuildManager.RefreshIfHarnessHammerVisibilityChanged();
        }
    }
}

[HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
internal static class PlayerSetupPlacementGhostPatch
{
    private static void Postfix(GameObject ___m_placementGhost)
    {
        PrefabPlacementPatchRegistry.PatchGhost(___m_placementGhost);
    }
}

[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class PlayerUpdatePlacementGhostPatch
{
    private static void Postfix(GameObject ___m_placementGhost)
    {
        PrefabPlacementPatchRegistry.ApplyGhostOffset(___m_placementGhost);
    }
}

[HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
internal static class PieceTableUpdateAvailablePatch
{
    private static void Prefix(PieceTable __instance, HashSet<string> knownRecipies)
    {
        PrefabBuildManager.PreparePieceTableForUpdate(__instance, knownRecipies);
    }

    private static void Postfix(PieceTable __instance, Player player, List<List<Piece>> ___m_availablePiecesByCategory)
    {
        if (player && player == Player.m_localPlayer &&
            HarnessPrefabsPlugin.HarnessHammerTabsEnabled && PrefabBuildManager.IsHammerPieceTable(__instance))
            PrefabCategoryRegistry.AddAdminSelectionPieces(___m_availablePiecesByCategory);
    }
}
