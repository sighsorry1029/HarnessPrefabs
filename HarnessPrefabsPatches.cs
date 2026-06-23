using System.Collections.Generic;
using HarmonyLib;

namespace HarnessPrefabs;

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
internal static class ZNetSceneAwakePatch
{
    private static void Postfix()
    {
        PrefabBuildManager.Refresh("ZNetScene.Awake");
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class ObjectDbAwakePatch
{
    private static void Postfix()
    {
        PrefabBuildManager.Refresh("ObjectDB.Awake");
    }
}

[HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
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

[HarmonyPatch(typeof(Player), nameof(Player.Update))]
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
    private static void Postfix(Player __instance)
    {
        PrefabPlacementPatchRegistry.PatchGhost(__instance.m_placementGhost);
    }
}

[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class PlayerUpdatePlacementGhostPatch
{
    private static void Postfix(Player __instance)
    {
        PrefabPlacementPatchRegistry.ApplyGhostOffset(__instance.m_placementGhost);
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
