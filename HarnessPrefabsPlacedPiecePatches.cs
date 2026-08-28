#nullable disable

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HarnessPrefabs;

[HarmonyPatch]
internal static class HarnessPrefabsPlacedPiecePatches
{
    private static readonly Dictionary<string, Piece.Requirement[]> DefaultResources = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MaterializedPrefabNames = new(StringComparer.Ordinal);
    private static readonly int PieceLayer = LayerMask.NameToLayer("piece");
    private static readonly int CharacterTriggerLayer = LayerMask.NameToLayer("character_trigger");
    private const float FermenterLodSize = 6.7675858f;
    private const string ZdoHasFields = "HasFields";
    private const string ZdoHasFieldsContainer = "HasFieldsContainer";
    private const string ZdoHasFieldsInventory = "HasFieldsInventory";
    private const string ZdoHasFieldsTimedDestruction = "HasFieldsTimedDestruction";
    private const string ZdoHasFieldsDestructible = "HasFieldsDestructible";
    private const string ZdoHasFieldsDoor = "HasFieldsDoor";
    private const string ZdoContainerCheckGuardStone = "Container.m_checkGuardStone";
    private const string ZdoContainerWidth = "Container.m_width";
    private const string ZdoContainerHeight = "Container.m_height";
    private const string ZdoInventoryWidth = "Inventory.m_width";
    private const string ZdoInventoryHeight = "Inventory.m_height";
    private const string ZdoTimedDestructionTimeout = "TimedDestruction.m_timeout";
    private const string ZdoDestructibleSpawnWhenDestroyed = "Destructible.m_spawnWhenDestroyed";
    private const string ZdoDoorCanNotBeClosed = "Door.m_canNotBeClosed";
    private const string ZdoDoorCheckGuardStone = "Door.m_checkGuardStone";

    internal static void RegisterMaterializedPrefab(GameObject prefab, Piece.Requirement[] resources)
    {
        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(prefab ? prefab.name : "");
        if (prefabName.Length == 0)
        {
            return;
        }

        MaterializedPrefabNames.Add(prefabName);
        if (!DefaultResources.ContainsKey(prefabName))
        {
            DefaultResources[prefabName] = CloneRequirements(resources);
        }
    }

    internal static bool IsMaterializedPrefab(string prefabName)
    {
        string normalizedName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        return normalizedName.Length > 0 && MaterializedPrefabNames.Contains(normalizedName);
    }

    internal static bool WasMaterializedPrefab(string prefabName)
    {
        string normalizedName = HarnessPrefabsRuntime.NormalizePrefabName(prefabName);
        return normalizedName.Length > 0 && DefaultResources.ContainsKey(normalizedName);
    }

    internal static void ResetMaterializedPrefabNames()
    {
        MaterializedPrefabNames.Clear();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    private static void PieceSetCreatorPrefix(Piece __instance, long uid, out bool __state)
    {
        __state = false;
        if (!__instance ||
            uid == 0L ||
            !HarnessPrefabsRuntime.IsMaterializedPrefab(__instance) ||
            !__instance.m_nview ||
            !__instance.m_nview.IsValid() ||
            !__instance.m_nview.IsOwner() ||
            __instance.GetCreator() != 0L)
        {
            return;
        }

        __state = true;
        ZNetView zNetView = __instance.m_nview;
        if (!zNetView.m_persistent)
        {
            zNetView.m_persistent = true;
            ZSyncTransform syncTransform = __instance.gameObject.GetComponent<ZSyncTransform>();
            if (!syncTransform)
            {
                syncTransform = __instance.gameObject.AddComponent<ZSyncTransform>();
            }

            syncTransform.m_syncPosition = true;
            syncTransform.m_syncRotation = true;
        }

        ZDO zdo = zNetView.GetZDO();
        if (zdo != null)
        {
            zdo.Persistent = true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    private static void PieceSetCreatorPostfix(Piece __instance, bool __state)
    {
        if (!__state ||
            !__instance ||
            !__instance.IsPlacedByPlayer() ||
            !HarnessPrefabsRuntime.TryGetRuntimeRule(__instance, out PrefabRule rule))
        {
            return;
        }

        ClearPlacedContainerInventory(__instance);
        ApplyRuntimePatches(__instance, rule);
    }

    [HarmonyPostfix]
    [HarmonyPriority(700)]
    [HarmonyPatch(typeof(Piece), nameof(Piece.Awake))]
    private static void PieceAwakePostfix(Piece __instance)
    {
        if (!__instance ||
            !__instance.IsPlacedByPlayer() ||
            !HarnessPrefabsRuntime.TryGetRuntimeRule(__instance, out PrefabRule rule))
        {
            return;
        }

        ApplyRuntimePatches(__instance, rule);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
    private static void WearNTearDestroyPrefix(
        WearNTear __instance,
        out (bool Changed, EffectList Original) __state)
    {
        __state = (false, null);
        if (!HarnessPrefabsRuntime.WasMaterializedPrefab(__instance) || HarnessPrefabsSfxManager.HasSfx(__instance.m_destroyedEffect))
        {
            return;
        }

        __state = (true, __instance.m_destroyedEffect);
        __instance.m_destroyedEffect = HarnessPrefabsSfxManager.FixRemovalSfx(__instance);
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
    private static Exception WearNTearDestroyFinalizer(
        WearNTear __instance,
        (bool Changed, EffectList Original) __state,
        Exception __exception)
    {
        if (__state.Changed && __instance)
        {
            __instance.m_destroyedEffect = __state.Original;
        }

        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    private static void PieceDropResourcesPrefix(
        Piece __instance,
        out (bool Changed, Piece.Requirement[] Original) __state)
    {
        __state = (false, null);
        if (!__instance || !HarnessPrefabsRuntime.WasMaterializedPrefab(__instance))
        {
            return;
        }

        Piece.Requirement[] dropResources = GetDropResources(__instance);
        if (dropResources != null && !ReferenceEquals(dropResources, __instance.m_resources))
        {
            __state = (true, __instance.m_resources);
            __instance.m_resources = dropResources;
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    private static Exception PieceDropResourcesFinalizer(
        Piece __instance,
        (bool Changed, Piece.Requirement[] Original) __state,
        Exception __exception)
    {
        if (__state.Changed && __instance)
        {
            __instance.m_resources = __state.Original;
        }

        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DropOnDestroyed), "OnDestroyed")]
    private static bool DropOnDestroyedPrefix(DropOnDestroyed __instance)
    {
        return !HarnessPrefabsRuntime.TryGetKnownManagedPlacedPiece(__instance, out _);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Door), "RPC_UseDoor")]
    private static void DoorUsePrefix(Door __instance, out int? __state)
    {
        __state = null;
        if (!HarnessPrefabsRuntime.TryGetKnownManagedPlacedPiece(__instance, out Piece piece) ||
            HarnessPrefabsRuntime.NormalizePrefabName(piece.gameObject.name) != "dvergrtown_secretdoor")
        {
            return;
        }

        __state = GetDoorState(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Door), "RPC_UseDoor")]
    private static void DoorUsePostfix(Door __instance, int? __state)
    {
        if (!__instance || !__state.HasValue)
        {
            return;
        }

        int? doorState = GetDoorState(__instance);
        ZNetView zNetView = __instance.GetComponent<ZNetView>();
        if (!doorState.HasValue || (__state != -1 && __state != 1) || doorState != 0 || !zNetView || !zNetView.IsValid())
        {
            return;
        }

        if (!zNetView.IsOwner())
        {
            zNetView.ClaimOwnership();
        }

        __instance.m_animator.Rebind();
        __instance.m_animator.Update(0f);
    }

    private static void ApplyRuntimePatches(Piece piece, PrefabRule rule)
    {
        piece.m_canBeRemoved = rule.CanBeRemoved;
        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(piece.name);
        ApplyContainerPatches(piece, prefabName);
        ApplyTimedDestructionPatch(piece);
        ApplySpawnOnDestroyedPatch(piece, prefabName);
        ApplyDoorPatches(piece);

        if (MvbpCompatibilityDefaults.HasPlayerBasePatch(prefabName))
        {
            ApplyPlayerBasePatch(piece.gameObject);
        }

        if (HarnessPrefabsPlugin.UnsafeBedPatchesEnabled)
        {
            ApplyBedPatch(piece.gameObject, prefabName);
        }

        int fermenterPatchDurationPercent = HarnessPrefabsPlugin.FermenterPatchDurationPercent;
        if (fermenterPatchDurationPercent > 0)
        {
            ApplyFermenterPatch(piece.gameObject, prefabName, fermenterPatchDurationPercent);
        }
    }

    private static void ClearPlacedContainerInventory(Piece piece)
    {
        Container container = piece.GetComponentInChildren<Container>();
        Inventory inventory = container ? container.GetInventory() : null;
        if (inventory == null || inventory.NrOfItems() == 0)
        {
            return;
        }

        inventory.RemoveAll();
        container.Save();
    }

    private static void ApplyContainerPatches(Piece piece, string prefabName)
    {
        Container container = piece.GetComponentInChildren<Container>();
        if (!container || !TryGetZdo(piece, out ZDO zdo))
        {
            return;
        }

        MarkZdoFields(zdo, ZdoHasFieldsContainer);
        zdo.Set(ZdoContainerCheckGuardStone, true);
        container.m_checkGuardStone = true;

        if (!MvbpCompatibilityDefaults.TryGetContainerSize(prefabName, out int width, out int height))
        {
            return;
        }

        Inventory inventory = container.GetInventory();
        zdo.Set(ZdoContainerWidth, width);
        zdo.Set(ZdoContainerHeight, height);
        MarkZdoFields(zdo, ZdoHasFieldsInventory);
        zdo.Set(ZdoInventoryWidth, width);
        zdo.Set(ZdoInventoryHeight, height);

        container.m_width = width;
        container.m_height = height;
        if (inventory != null)
        {
            inventory.m_width = width;
            inventory.m_height = height;
        }
    }

    private static void ApplyTimedDestructionPatch(Piece piece)
    {
        TimedDestruction timedDestruction = piece.GetComponent<TimedDestruction>();
        if (!timedDestruction || !TryGetZdo(piece, out ZDO zdo))
        {
            return;
        }

        const float timeout = 1E+30f;
        MarkZdoFields(zdo, ZdoHasFieldsTimedDestruction);
        zdo.Set(ZdoTimedDestructionTimeout, timeout);
        timedDestruction.m_timeout = timeout;
    }

    private static void ApplySpawnOnDestroyedPatch(Piece piece, string prefabName)
    {
        if (!MvbpCompatibilityDefaults.TryGetSpawnOnDestroyed(prefabName, out string spawnOnDestroyed) || !TryGetZdo(piece, out ZDO zdo))
        {
            return;
        }

        Destructible destructible = piece.GetComponent<Destructible>();
        if (!destructible)
        {
            return;
        }

        GameObject spawnPrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(spawnOnDestroyed) : null;
        if (!spawnPrefab)
        {
            return;
        }

        MarkZdoFields(zdo, ZdoHasFieldsDestructible);
        zdo.Set(ZdoDestructibleSpawnWhenDestroyed, spawnOnDestroyed);
        destructible.m_spawnWhenDestroyed = spawnPrefab;
    }

    private static void ApplyDoorPatches(Piece piece)
    {
        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(piece.name);
        if (prefabName != "dvergrtown_slidingdoor" && prefabName != "dvergrtown_secretdoor")
        {
            return;
        }

        Door door = piece.GetComponent<Door>();
        if (!door || !TryGetZdo(piece, out ZDO zdo))
        {
            return;
        }

        door.m_canNotBeClosed = false;
        door.m_checkGuardStone = true;
        MarkZdoFields(zdo, ZdoHasFieldsDoor);
        zdo.Set(ZdoDoorCanNotBeClosed, false);
        zdo.Set(ZdoDoorCheckGuardStone, true);
    }

    private static void ApplyPlayerBasePatch(GameObject gameObject)
    {
        Transform existing = gameObject.transform.Find("PlayerBase");
        if (existing)
        {
            return;
        }

        GameObject playerBase = new("PlayerBase");
        playerBase.transform.SetParent(gameObject.transform, worldPositionStays: false);
        playerBase.transform.localScale = Vector3.one;
        playerBase.transform.localPosition = Vector3.zero;
        playerBase.layer = CharacterTriggerLayer;

        SphereCollider collider = playerBase.AddComponent<SphereCollider>();
        collider.center = Vector3.zero;
        collider.radius = 20f;
        collider.enabled = true;
        collider.isTrigger = true;

        EffectArea area = playerBase.AddComponent<EffectArea>();
        area.enabled = true;
        area.m_type = EffectArea.Type.PlayerBase;
    }

    private static void ApplyBedPatch(GameObject gameObject, string prefabName)
    {
        if (prefabName != "goblin_bed" && prefabName != "dvergrprops_bed")
        {
            return;
        }

        Bed bed = gameObject.GetComponent<Bed>();
        if (bed && bed.m_spawnPoint)
        {
            return;
        }

        Transform spawnPoint = gameObject.transform.Find("spawnpoint");
        if (!spawnPoint)
        {
            GameObject spawnPointObject = new("spawnpoint");
            spawnPointObject.transform.SetParent(gameObject.transform, worldPositionStays: false);
            spawnPoint = spawnPointObject.transform;
        }

        spawnPoint.localPosition = new Vector3(0f, 0.45f, 0f);
        spawnPoint.gameObject.layer = PieceLayer;
        bed = bed ? bed : gameObject.AddComponent<Bed>();
        bed.m_spawnPoint = spawnPoint;
    }

    private static void ApplyFermenterPatch(GameObject gameObject, string prefabName, int durationPercent)
    {
        if (prefabName != "dvergrprops_barrel")
        {
            return;
        }

        Fermenter existingFermenter = gameObject.GetComponent<Fermenter>();
        GameObject fermenterPrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab("fermenter") : null;
        Fermenter sourceFermenter = fermenterPrefab ? fermenterPrefab.GetComponent<Fermenter>() : null;
        if (existingFermenter)
        {
            if (sourceFermenter)
            {
                existingFermenter.m_fermentationDuration =
                    sourceFermenter.m_fermentationDuration * Mathf.Clamp(durationPercent, 1, 100) / 100f;
            }

            EnsureFermenterHoverProxies(gameObject);
            ApplyFermenterLodPatch(gameObject, fermenterPrefab);
            ApplyPlayerBasePatch(gameObject);
            return;
        }

        if (!fermenterPrefab || !sourceFermenter)
        {
            return;
        }

        Transform addButtonSource = fermenterPrefab.transform.Find("add_button");
        Transform tapButtonSource = fermenterPrefab.transform.Find("tap_button");
        Transform roofCheckPointSource = fermenterPrefab.transform.Find("roofcheckpoint");
        Transform outputSource = fermenterPrefab.transform.Find("output");
        Transform readySource = fermenterPrefab.transform.Find("_ready");
        Transform fermentingSource = fermenterPrefab.transform.Find("_fermenting");
        if (!addButtonSource ||
            !tapButtonSource ||
            !roofCheckPointSource ||
            !outputSource ||
            !readySource ||
            !fermentingSource ||
            !addButtonSource.GetComponent<Switch>() ||
            !tapButtonSource.GetComponent<Switch>())
        {
            return;
        }

        List<GameObject> addedObjects = new();
        Fermenter fermenter = null;
        bool activeSelf = gameObject.activeSelf;
        try
        {
            GameObject addButton = CloneFermenterChild(addButtonSource, gameObject.transform);
            GameObject tapButton = CloneFermenterChild(tapButtonSource, gameObject.transform);
            GameObject roofCheckPoint = CloneFermenterChild(roofCheckPointSource, gameObject.transform);
            GameObject output = CloneFermenterChild(outputSource, gameObject.transform);
            GameObject ready = CloneFermenterChild(readySource, gameObject.transform);
            GameObject fermenting = CloneFermenterChild(fermentingSource, gameObject.transform);
            addedObjects.AddRange(new[] { addButton, tapButton, roofCheckPoint, output, ready, fermenting });

            addButton.transform.localScale = Vector3.one;
            addButton.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            tapButton.transform.localPosition = new Vector3(0f, 0.5f, 0.9f);
            output.transform.localPosition = new Vector3(0f, 0.5f, 1.2f);
            roofCheckPoint.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            ready.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            fermenting.transform.localPosition = new Vector3(0f, 0.75f, 0f);

            Transform top = gameObject.transform.Find("_top");
            if (!top)
            {
                GameObject topObject = new("_top");
                topObject.transform.SetParent(gameObject.transform, worldPositionStays: false);
                addedObjects.Add(topObject);
                top = topObject.transform;
            }

            gameObject.SetActive(false);
            fermenter = gameObject.AddComponent<Fermenter>();
            fermenter.m_addSwitch = addButton.GetComponent<Switch>();
            fermenter.m_tapSwitch = tapButton.GetComponent<Switch>();
            fermenter.m_roofCheckPoint = roofCheckPoint.transform;
            fermenter.m_topObject = top.gameObject;
            fermenter.m_readyObject = ready;
            fermenter.m_fermentingObject = fermenting;
            fermenter.m_outputPoint = output.transform;
            fermenter.m_tapDelay = sourceFermenter.m_tapDelay;
            fermenter.m_updateCoverTimer = sourceFermenter.m_updateCoverTimer;
            fermenter.m_fermentationDuration =
                sourceFermenter.m_fermentationDuration * Mathf.Clamp(durationPercent, 1, 100) / 100f;
            fermenter.m_name = sourceFermenter.m_name;
            fermenter.m_addedEffects = sourceFermenter.m_addedEffects;
            fermenter.m_tapEffects = sourceFermenter.m_tapEffects;
            fermenter.m_spawnEffects = sourceFermenter.m_spawnEffects;
            fermenter.m_conversion = sourceFermenter.m_conversion;
        }
        catch (Exception ex)
        {
            if (fermenter)
            {
                UnityEngine.Object.DestroyImmediate(fermenter);
            }

            for (int i = addedObjects.Count - 1; i >= 0; i--)
            {
                if (addedObjects[i])
                {
                    UnityEngine.Object.DestroyImmediate(addedObjects[i]);
                }
            }

            HarnessPrefabsPlugin.Log.LogWarning($"Failed to patch fermenter prefab '{prefabName}': {ex.Message}");
            return;
        }
        finally
        {
            gameObject.SetActive(activeSelf);
        }

        EnsureFermenterHoverProxies(gameObject);
        ApplyFermenterLodPatch(gameObject, fermenterPrefab);
        ApplyPlayerBasePatch(gameObject);
    }

    private static void ApplyFermenterLodPatch(GameObject gameObject, GameObject fermenterPrefab = null)
    {
        float targetSize = FermenterLodSize;
        LODGroup sourceLodGroup = fermenterPrefab ? fermenterPrefab.GetComponent<LODGroup>() : null;
        if (sourceLodGroup && sourceLodGroup.size > targetSize)
        {
            targetSize = sourceLodGroup.size;
        }

        foreach (LODGroup lodGroup in gameObject.GetComponentsInChildren<LODGroup>(true))
        {
            if (!lodGroup)
            {
                continue;
            }

            lodGroup.size = Mathf.Max(lodGroup.size, targetSize);
        }
    }

    private static void EnsureFermenterHoverProxies(GameObject gameObject)
    {
        Fermenter fermenter = gameObject.GetComponent<Fermenter>();
        if (!fermenter)
        {
            return;
        }

        foreach (Collider collider in gameObject.GetComponentsInChildren<Collider>(true))
        {
            if (!collider || collider.GetComponent<Hoverable>() != null)
            {
                continue;
            }

            if (!collider.GetComponent<HarnessPrefabsFermenterHoverProxy>())
            {
                collider.gameObject.AddComponent<HarnessPrefabsFermenterHoverProxy>();
            }
        }
    }

    private static GameObject CloneFermenterChild(Transform source, Transform targetParent)
    {
        bool activeSelf = source.gameObject.activeSelf;
        GameObject clone;
        try
        {
            source.gameObject.SetActive(false);
            clone = UnityEngine.Object.Instantiate(source.gameObject);
        }
        finally
        {
            source.gameObject.SetActive(activeSelf);
        }

        clone.name = source.name;
        clone.transform.SetParent(targetParent, worldPositionStays: false);
        clone.SetActive(activeSelf);
        return clone;
    }

    private static Piece.Requirement[] GetDropResources(Piece piece)
    {
        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(piece.gameObject.name);
        if (!piece.IsPlacedByPlayer() && DefaultResources.TryGetValue(prefabName, out Piece.Requirement[] defaultResources))
        {
            return CloneRequirements(defaultResources);
        }

        DropItemStandItems(piece);
        Piece.Requirement[] resources = CloneRequirements(piece.m_resources);
        Pickable pickable = piece.GetComponent<Pickable>();
        if (pickable)
        {
            bool wasPicked = pickable.m_picked;
            bool pickRequested = false;
            ZNetView zNetView = pickable.m_nview ? pickable.m_nview : pickable.GetComponent<ZNetView>();
            if (!wasPicked && zNetView && zNetView.IsValid())
            {
                zNetView.InvokeRPC(nameof(Pickable.RPC_Pick), 0);
                pickRequested = true;
            }

            resources = RemovePickableFromRequirements(resources, pickable, wasPicked || pickRequested);
        }

        return resources;
    }

    private static void DropItemStandItems(Piece piece)
    {
        foreach (ItemStand itemStand in piece.GetComponentsInChildren<ItemStand>())
        {
            if (!itemStand)
            {
                continue;
            }

            ZNetView zNetView = itemStand.m_nview
                ? itemStand.m_nview
                : itemStand.m_netViewOverride
                    ? itemStand.m_netViewOverride
                    : itemStand.GetComponent<ZNetView>();
            if (!zNetView || !zNetView.IsValid())
            {
                continue;
            }

            bool canBeRemoved = itemStand.m_canBeRemoved;
            try
            {
                itemStand.m_canBeRemoved = true;
                zNetView.InvokeRPC(nameof(ItemStand.RPC_DropItem));
            }
            finally
            {
                itemStand.m_canBeRemoved = canBeRemoved;
            }
        }
    }

    private static Piece.Requirement[] RemovePickableFromRequirements(
        Piece.Requirement[] requirements,
        Pickable pickable,
        bool pickedOrRequested)
    {
        if (requirements == null || !pickable || !pickedOrRequested || !pickable.m_itemPrefab)
        {
            return requirements;
        }

        ItemDrop item = pickable.m_itemPrefab.GetComponent<ItemDrop>();
        if (item?.m_itemData?.m_shared == null)
        {
            return requirements;
        }

        Piece.Requirement[] adjusted = CloneRequirements(requirements);
        int amount = GetScaledPickableDropAmount(pickable);
        string itemName = item.m_itemData.m_shared.m_name;
        foreach (Piece.Requirement requirement in adjusted)
        {
            if (requirement?.m_resItem?.m_itemData?.m_shared?.m_name == itemName)
            {
                requirement.m_amount = Mathf.Clamp(requirement.m_amount - amount, 0, requirement.m_amount);
                break;
            }
        }

        return adjusted;
    }

    private static int GetScaledPickableDropAmount(Pickable pickable)
    {
        if (!Game.instance)
        {
            return pickable.m_amount;
        }

        return pickable.m_dontScale
            ? pickable.m_amount
            : Mathf.Max(pickable.m_minAmountScaled, Game.instance.ScaleDrops(pickable.m_itemPrefab, pickable.m_amount));
    }

    private static Piece.Requirement[] CloneRequirements(Piece.Requirement[] requirements)
    {
        if (requirements == null || requirements.Length == 0)
        {
            return Array.Empty<Piece.Requirement>();
        }

        Piece.Requirement[] clone = new Piece.Requirement[requirements.Length];
        for (int i = 0; i < requirements.Length; i++)
        {
            Piece.Requirement requirement = requirements[i];
            clone[i] = requirement == null
                ? null
                : new Piece.Requirement
                {
                    m_resItem = requirement.m_resItem,
                    m_amount = requirement.m_amount,
                    m_amountPerLevel = requirement.m_amountPerLevel,
                    m_recover = requirement.m_recover
                };
        }

        return clone;
    }

    private static int? GetDoorState(Door door)
    {
        if (!door || !door.m_nview || !door.m_nview.IsValid())
        {
            return null;
        }

        return door.m_nview.GetZDO().GetInt(ZDOVars.s_state, 0);
    }

    private static bool TryGetZdo(Piece piece, out ZDO zdo)
    {
        zdo = null;
        if (!piece || !piece.m_nview || !piece.m_nview.IsValid())
        {
            return false;
        }

        zdo = piece.m_nview.GetZDO();
        return zdo != null;
    }

    private static void MarkZdoFields(ZDO zdo, params string[] fieldFlags)
    {
        zdo.Set(ZdoHasFields, true);
        foreach (string fieldFlag in fieldFlags)
        {
            zdo.Set(fieldFlag, true);
        }
    }
}

internal sealed class HarnessPrefabsFermenterHoverProxy : MonoBehaviour, Hoverable, Interactable
{
    public string GetHoverName()
    {
        Fermenter fermenter = GetComponentInParent<Fermenter>();
        return fermenter ? fermenter.GetHoverName() : "";
    }

    public string GetHoverText()
    {
        Fermenter fermenter = GetComponentInParent<Fermenter>();
        return fermenter ? fermenter.GetHoverText() : "";
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        Fermenter fermenter = GetComponentInParent<Fermenter>();
        return fermenter && fermenter.Interact(user, hold, alt);
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        Fermenter fermenter = GetComponentInParent<Fermenter>();
        return fermenter && fermenter.UseItem(user, item);
    }
}
