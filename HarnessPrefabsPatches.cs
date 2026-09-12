using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HarnessPrefabs;

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
}
