#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HarnessPrefabs;

internal static class HarnessPrefabsArmorStandSwap
{
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidLeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_leftItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidRightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_rightItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidHiddenLeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_hiddenLeftItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidHiddenRightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_hiddenRightItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidHelmetItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_helmetItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidChestItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_chestItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidLegItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_legItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidShoulderItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_shoulderItem");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData?> HumanoidUtilityItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData?>("m_utilityItem");
    private static readonly AccessTools.FieldRef<Humanoid, float> HumanoidUseItemTime = AccessTools.FieldRefAccess<Humanoid, float>("m_useItemTime");
    private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData.SharedData> HumanoidUseItemVisual = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData.SharedData>("m_useItemVisual");
    private static readonly AccessTools.FieldRef<ArmorStand, ItemDrop.ItemData> ArmorStandQueuedItem = AccessTools.FieldRefAccess<ArmorStand, ItemDrop.ItemData>("m_queuedItem");
    private static readonly AccessTools.FieldRef<ArmorStand, ZNetView> ArmorStandNview = AccessTools.FieldRefAccess<ArmorStand, ZNetView>("m_nview");
    private static readonly Func<ArmorStand, ArmorStand.ArmorStandSlot, ItemDrop.ItemData, bool> CanAttach = AccessTools.MethodDelegate<Func<ArmorStand, ArmorStand.ArmorStandSlot, ItemDrop.ItemData, bool>>(AccessTools.Method(typeof(ArmorStand), "CanAttach"));
    private static readonly Action<Humanoid> SetupEquipment = AccessTools.MethodDelegate<Action<Humanoid>>(AccessTools.Method(typeof(Humanoid), "SetupEquipment"));
    private static readonly Action<ArmorStand> UpdateSupports = AccessTools.MethodDelegate<Action<ArmorStand>>(AccessTools.Method(typeof(ArmorStand), "UpdateSupports"));
    private static readonly AccessTools.FieldRef<ArmorStand, Cloth[]> ArmorStandCloths = AccessTools.FieldRefAccess<ArmorStand, Cloth[]>("m_cloths");

    private const float OwnershipTimeoutSeconds = 2f;
    private static readonly HashSet<string> SupportedPrefabNames = new(StringComparer.Ordinal)
    {
        "ArmorStand",
        "ArmorStand_Female",
        "ArmorStand_Male"
    };

    private static readonly SlotDefinition[] ArmorSlotDefinitions =
    {
        new(VisSlot.Helmet, ItemDrop.ItemData.ItemType.Helmet),
        new(VisSlot.Chest, ItemDrop.ItemData.ItemType.Chest),
        new(VisSlot.Legs, ItemDrop.ItemData.ItemType.Legs),
        new(VisSlot.Shoulder, ItemDrop.ItemData.ItemType.Shoulder)
    };
    private static readonly SlotDefinition UtilitySlotDefinition =
        new(VisSlot.Utility, ItemDrop.ItemData.ItemType.Utility);

    private static bool _swapInProgress;

    internal static bool TryGetTarget(Switch switchInstance, out ArmorStand stand)
    {
        stand = null!;
        if (!HarnessPrefabsPlugin.ArmorStandEquipmentSwapEnabled || !switchInstance)
        {
            return false;
        }

        ArmorStand candidate = switchInstance.GetComponentInParent<ArmorStand>();
        if (!candidate || !IsSupportedPlayerBuiltPrefab(candidate))
        {
            return false;
        }

        foreach (ArmorStand.ArmorStandSlot slot in candidate.m_slots)
        {
            if (slot.m_switch == switchInstance)
            {
                stand = candidate;
                return true;
            }
        }

        return false;
    }

    internal static void Begin(ArmorStand stand, Player player)
    {
        if (_swapInProgress)
        {
            ShowMessage(player, "An equipment swap is already in progress.");
            return;
        }

        _swapInProgress = true;
        try
        {
            stand.StartCoroutine(SwapWhenOwned(stand, player));
        }
        catch (Exception ex)
        {
            _swapInProgress = false;
            HarnessPrefabsPlugin.Log.LogError($"Could not start ArmorStand equipment swap: {ex}");
            ShowMessage(player, "Could not start the equipment swap.");
        }
    }

    internal static string GetHoverSuffix()
    {
        return Localization.instance.Localize(
            "\n[<color=yellow><b>Alt + $KEY_Use</b></color>] Swap equipment set");
    }

    internal static bool IsSwapModifierPressed()
    {
        return ZInput.GetKey(KeyCode.LeftAlt, logWarning: false) ||
               ZInput.GetKey(KeyCode.RightAlt, logWarning: false);
    }

    private static IEnumerator SwapWhenOwned(ArmorStand stand, Player player)
    {
        try
        {
            if (!IsPlayerReady(player) ||
                !IsSupportedPlayerBuiltPrefab(stand) ||
                !PrivateArea.CheckAccess(stand.transform.position, 0f, flash: true))
            {
                ShowMessage(player, "You cannot use this armor stand right now.");
                yield break;
            }

            ZNetView nview = GetNetView(stand);
            if (!nview || !nview.IsValid())
            {
                ShowMessage(player, "This armor stand is not ready.");
                yield break;
            }

            if (!nview.IsOwner())
            {
                nview.InvokeRPC("RPC_RequestOwn");
                float deadline = Time.realtimeSinceStartup + OwnershipTimeoutSeconds;
                while (nview && nview.IsValid() && !nview.IsOwner() && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }

            if (!nview || !nview.IsValid() || !nview.IsOwner())
            {
                ShowMessage(player, "Could not take ownership of this armor stand.");
                yield break;
            }

            if (!TryCreatePlan(stand, nview, player, out SwapPlan plan, out string error))
            {
                ShowMessage(player, error);
                yield break;
            }

            if (!TryExecute(plan, out error))
            {
                ShowMessage(player, error);
                yield break;
            }

            PlaySwapEffect(plan);
            ShowMessage(
                player,
                plan.UtilitySkipped
                    ? "Equipment set swapped. Utility was left unchanged."
                    : "Equipment set swapped.");
        }
        finally
        {
            _swapInProgress = false;
        }
    }

    private static bool TryCreatePlan(
        ArmorStand stand,
        ZNetView nview,
        Player player,
        out SwapPlan plan,
        out string error)
    {
        plan = null!;
        error = "Could not prepare the equipment swap.";

        if (!IsPlayerReady(player))
        {
            error = "You cannot swap equipment right now.";
            return false;
        }

        if (!IsSupportedPlayerBuiltPrefab(stand) || !PrivateArea.CheckAccess(stand.transform.position, 0f, flash: false))
        {
            error = "You do not have access to this armor stand.";
            return false;
        }

        if (!nview.IsValid() || !nview.IsOwner())
        {
            error = "The armor stand ownership changed. Try again.";
            return false;
        }

        if (ArmorStandQueuedItem(stand) != null)
        {
            error = "This armor stand is already handling another item.";
            return false;
        }

        ZDO zdo = nview.GetZDO();
        Inventory inventory = player.GetInventory();
        if (zdo == null || inventory == null || ObjectDB.instance == null)
        {
            error = "The armor stand or inventory is not ready.";
            return false;
        }

        List<SlotPlan> slots = new(ArmorSlotDefinitions.Length + 3);

        foreach (SlotDefinition definition in ArmorSlotDefinitions)
        {
            if (!TryCreateArmorSlotPlan(
                    stand,
                    zdo,
                    player,
                    inventory,
                    definition,
                    out SlotPlan slot,
                    out error))
            {
                return false;
            }

            slots.Add(slot);
        }

        if (!TryCreateHandSetPlan(stand, zdo, player, inventory, slots, out HandSetPlan handSet, out error))
        {
            return false;
        }

        bool utilitySkipped = !TryCreateArmorSlotPlan(
            stand,
            zdo,
            player,
            inventory,
            UtilitySlotDefinition,
            out SlotPlan utilitySlot,
            out string utilitySkipReason);
        if (utilitySkipped)
        {
            HarnessPrefabsPlugin.Log.LogDebug($"ArmorStand Utility was left unchanged: {utilitySkipReason}");
        }
        else
        {
            slots.Add(utilitySlot);
        }

        int outgoingCount = 0;
        int incomingCount = 0;
        foreach (SlotPlan slot in slots)
        {
            if (slot.Outgoing != null)
            {
                outgoingCount++;
            }

            if (slot.Incoming != null)
            {
                incomingCount++;
            }
        }

        if (incomingCount == 0 && outgoingCount == 0)
        {
            error = utilitySkipped
                ? "There is no compatible equipment to swap. Utility was left unchanged."
                : "There is no equipment to swap.";
            return false;
        }

        if (incomingCount > inventory.GetEmptySlots() + outgoingCount)
        {
            error = "Not enough inventory space to swap this equipment set.";
            return false;
        }

        if (!TryReserveIncomingPositions(inventory, slots, out error))
        {
            return false;
        }

        plan = new SwapPlan(
            stand,
            nview,
            zdo,
            zdo.m_uid,
            zdo.DataRevision,
            player,
            inventory,
            slots,
            handSet,
            utilitySkipped);
        return true;
    }

    private static bool TryCreateArmorSlotPlan(
        ArmorStand stand,
        ZDO zdo,
        Player player,
        Inventory inventory,
        SlotDefinition definition,
        out SlotPlan slot,
        out string error)
    {
        slot = null!;
        if (!TryFindUniqueSlot(stand, definition.VisSlot, out int index, out ArmorStand.ArmorStandSlot standSlot))
        {
            error = $"This armor stand does not have one unique {definition.VisSlot} slot.";
            return false;
        }

        ItemDrop.ItemData? outgoing = GetEquippedItem(player, definition.VisSlot);
        if (outgoing != null &&
            !TryValidatePlayerItem(stand, standSlot, definition, player, inventory, outgoing, out error))
        {
            return false;
        }

        int incomingHash = zdo.GetInt(index + "_item", 0);
        ItemDrop.ItemData? standSnapshot = null;
        ItemDrop.ItemData? incoming = null;
        if (incomingHash != 0)
        {
            if (!TryLoadStandItem(index, incomingHash, zdo, out standSnapshot, out error) ||
                !TryValidateIncomingItem(definition, standSnapshot, out error))
            {
                return false;
            }

            if (!CanAttach(stand, standSlot, standSnapshot) || !HasArmorStandVisual(standSnapshot))
            {
                error = $"The armor stand's {definition.VisSlot} item is incompatible with its current slot.";
                return false;
            }

            incoming = standSnapshot.Clone();
            incoming.m_equipped = false;
        }

        slot = new SlotPlan(
            definition,
            index,
            standSnapshot,
            incoming,
            outgoing,
            CreateStandSnapshot(outgoing));
        error = "";
        return true;
    }

    private static bool TryCreateHandSetPlan(
        ArmorStand stand,
        ZDO zdo,
        Player player,
        Inventory inventory,
        List<SlotPlan> slots,
        out HandSetPlan handSet,
        out string error)
    {
        handSet = null!;
        bool hasActiveHandItem = HumanoidLeftItem(player) != null || HumanoidRightItem(player) != null;
        bool hasSheathedHandItem = HumanoidHiddenLeftItem(player) != null || HumanoidHiddenRightItem(player) != null;
        if (hasActiveHandItem && hasSheathedHandItem)
        {
            error = "Finish using or drawing your hand equipment before swapping this set.";
            return false;
        }

        if (HumanoidUseItemTime(player) > 0f || HumanoidUseItemVisual(player) != null)
        {
            error = "Finish using the current item before swapping this set.";
            return false;
        }

        bool originalSheathed = hasSheathedHandItem;

        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(stand.gameObject.name);
        bool isBaseArmorStand = string.Equals(prefabName, "ArmorStand", StringComparison.Ordinal);

        VisSlot standLeftVisSlot = isBaseArmorStand ? VisSlot.BackLeft : VisSlot.HandLeft;
        VisSlot standRightVisSlot = isBaseArmorStand ? VisSlot.BackRight : VisSlot.HandRight;

        if (!TryFindUniqueSlot(stand, standLeftVisSlot, out int standLeftIndex, out ArmorStand.ArmorStandSlot standLeftSlot) ||
            !TryFindUniqueSlot(stand, standRightVisSlot, out int standRightIndex, out ArmorStand.ArmorStandSlot standRightSlot))
        {
            error = $"This armor stand does not have one unique {standLeftVisSlot}/{standRightVisSlot} hand set.";
            return false;
        }

        ItemDrop.ItemData? originalLeft = originalSheathed ? HumanoidHiddenLeftItem(player) : HumanoidLeftItem(player);
        ItemDrop.ItemData? originalRight = originalSheathed ? HumanoidHiddenRightItem(player) : HumanoidRightItem(player);
        if (!TryValidatePlayerHandItem(player, inventory, originalLeft, "left", originalSheathed, out error) ||
            !TryValidatePlayerHandItem(player, inventory, originalRight, "right", originalSheathed, out error))
        {
            return false;
        }

        if (!TryGetCanonicalHandState(originalLeft, originalRight, out ItemDrop.ItemData? canonicalOriginalLeft,
                out ItemDrop.ItemData? canonicalOriginalRight) ||
            !ReferenceEquals(canonicalOriginalLeft, originalLeft) ||
            !ReferenceEquals(canonicalOriginalRight, originalRight))
        {
            error = "The player's current hand equipment is not a supported vanilla hand set.";
            return false;
        }

        ItemDrop.ItemData? standLeftOutgoing;
        ItemDrop.ItemData? standRightOutgoing;
        if (!TryMapHandsToStandSlots(
                stand,
                standLeftSlot,
                standRightSlot,
                originalLeft,
                originalRight,
                out standLeftOutgoing,
                out standRightOutgoing))
        {
            error = isBaseArmorStand
                ? "The player's hand set cannot fit the base armor stand's shield and weapon slots."
                : "The player's hand set cannot be displayed in this armor stand's hand slots.";
            return false;
        }

        if (!TryLoadStandHandItem(
                stand,
                standLeftSlot,
                standLeftIndex,
                standLeftVisSlot,
                zdo,
                out ItemDrop.ItemData? standLeftSnapshot,
                out ItemDrop.ItemData? incomingFromStandLeft,
                out error) ||
            !TryLoadStandHandItem(
                stand,
                standRightSlot,
                standRightIndex,
                standRightVisSlot,
                zdo,
                out ItemDrop.ItemData? standRightSnapshot,
                out ItemDrop.ItemData? incomingFromStandRight,
                out error))
        {
            return false;
        }

        if (!TryGetCanonicalHandState(
                incomingFromStandLeft,
                incomingFromStandRight,
                out ItemDrop.ItemData? incomingLeft,
                out ItemDrop.ItemData? incomingRight))
        {
            error = "The armor stand's selected hand-set slots do not form a hand set that can be equipped together.";
            return false;
        }

        if (!isBaseArmorStand &&
            (!TryMapHandsToStandSlots(
                 stand,
                 standLeftSlot,
                 standRightSlot,
                 incomingLeft,
                 incomingRight,
                 out ItemDrop.ItemData? mappedStandLeft,
                 out ItemDrop.ItemData? mappedStandRight) ||
             !ReferenceEquals(mappedStandLeft, incomingFromStandLeft) ||
             !ReferenceEquals(mappedStandRight, incomingFromStandRight)))
        {
            error = "The armor stand's hand items are not in their attachment-compatible wearable positions.";
            return false;
        }

        SlotDefinition standLeftDefinition = new(standLeftVisSlot, ItemDrop.ItemData.ItemType.None);
        SlotDefinition standRightDefinition = new(standRightVisSlot, ItemDrop.ItemData.ItemType.None);
        SlotPlan standLeftPlan = new(
            standLeftDefinition,
            standLeftIndex,
            standLeftSnapshot,
            incomingFromStandLeft,
            standLeftOutgoing,
            CreateStandSnapshot(standLeftOutgoing),
            isHand: true);
        SlotPlan standRightPlan = new(
            standRightDefinition,
            standRightIndex,
            standRightSnapshot,
            incomingFromStandRight,
            standRightOutgoing,
            CreateStandSnapshot(standRightOutgoing),
            isHand: true);

        slots.Add(standLeftPlan);
        slots.Add(standRightPlan);
        handSet = new HandSetPlan(
            originalLeft,
            originalRight,
            incomingLeft,
            incomingRight,
            originalSheathed);
        error = "";
        return true;
    }

    private static bool TryLoadStandHandItem(
        ArmorStand stand,
        ArmorStand.ArmorStandSlot standSlot,
        int index,
        VisSlot visSlot,
        ZDO zdo,
        out ItemDrop.ItemData? standSnapshot,
        out ItemDrop.ItemData? incoming,
        out string error)
    {
        standSnapshot = null;
        incoming = null;
        int incomingHash = zdo.GetInt(index + "_item", 0);
        if (incomingHash == 0)
        {
            error = "";
            return true;
        }

        if (!TryLoadStandItem(index, incomingHash, zdo, out ItemDrop.ItemData loaded, out error) ||
            !TryValidateStandHandItem(stand, standSlot, visSlot, loaded, out error))
        {
            return false;
        }

        standSnapshot = loaded;
        incoming = loaded.Clone();
        incoming.m_equipped = false;
        error = "";
        return true;
    }

    private static ItemDrop.ItemData? CreateStandSnapshot(ItemDrop.ItemData? item)
    {
        ItemDrop.ItemData? snapshot = item?.Clone();
        if (snapshot != null)
        {
            snapshot.m_equipped = false;
            snapshot.m_stack = 1;
        }

        return snapshot;
    }

    private static bool TryMapHandsToStandSlots(
        ArmorStand stand,
        ArmorStand.ArmorStandSlot standLeftSlot,
        ArmorStand.ArmorStandSlot standRightSlot,
        ItemDrop.ItemData? playerLeft,
        ItemDrop.ItemData? playerRight,
        out ItemDrop.ItemData? standLeft,
        out ItemDrop.ItemData? standRight)
    {
        // ArmorStand routing uses attachOverride through CanAttach; player hand position does not.
        if (CanStoreHandItem(stand, standLeftSlot, playerLeft) &&
            CanStoreHandItem(stand, standRightSlot, playerRight))
        {
            standLeft = playerLeft;
            standRight = playerRight;
            return true;
        }

        if (CanStoreHandItem(stand, standLeftSlot, playerRight) &&
            CanStoreHandItem(stand, standRightSlot, playerLeft))
        {
            standLeft = playerRight;
            standRight = playerLeft;
            return true;
        }

        standLeft = null;
        standRight = null;
        return false;
    }

    private static bool CanStoreHandItem(
        ArmorStand stand,
        ArmorStand.ArmorStandSlot standSlot,
        ItemDrop.ItemData? item)
    {
        return item == null || CanAttach(stand, standSlot, item);
    }

    private static bool TryExecute(SwapPlan plan, out string error)
    {
        List<SlotPlan> removedOutgoing = new();
        List<SlotPlan> addedIncoming = new();
        bool standTouched = false;

        try
        {
            EnsureTransactionStillValid(plan);

            foreach (SlotPlan slot in plan.Slots)
            {
                if (slot.IsHand || slot.Outgoing == null)
                {
                    continue;
                }

                RemoveOutgoingItem(plan, slot, removedOutgoing);
            }

            RemoveOutgoingHandItem(plan, plan.HandSet.OriginalRight, removedOutgoing);
            RemoveOutgoingHandItem(plan, plan.HandSet.OriginalLeft, removedOutgoing);

            if (!AreHandReferencesClear(plan.Player))
            {
                throw new InvalidOperationException("Could not clear the player's current hand set.");
            }

            foreach (SlotPlan slot in plan.Slots)
            {
                if (slot.Incoming == null)
                {
                    continue;
                }

                addedIncoming.Add(slot);
                if (!plan.Inventory.AddItem(slot.Incoming, slot.IncomingPosition) ||
                    !plan.Inventory.ContainsItem(slot.Incoming))
                {
                    throw new InvalidOperationException($"Could not add the armor stand's {slot.Definition.VisSlot} item to inventory.");
                }

            }

            foreach (SlotPlan slot in addedIncoming)
            {
                if (slot.IsHand)
                {
                    continue;
                }

                if (GetEquippedItem(plan.Player, slot.Definition.VisSlot) != slot.Incoming)
                {
                    plan.Player.EquipItem(slot.Incoming, triggerEquipEffects: false);
                }

                if (GetEquippedItem(plan.Player, slot.Definition.VisSlot) != slot.Incoming)
                {
                    throw new InvalidOperationException($"Could not equip the armor stand's {slot.Definition.VisSlot} item.");
                }
            }

            if (!TryApplyHandState(
                    plan.Player,
                    plan.HandSet.IncomingLeft,
                    plan.HandSet.IncomingRight,
                    plan.HandSet.Sheathed,
                    out string handError))
            {
                throw new InvalidOperationException(handError);
            }

            EnsureTransactionStillValid(plan);

            foreach (SlotPlan slot in plan.Slots)
            {
                standTouched = true;
                WriteStandItem(plan.Zdo, slot.Index, slot.OutgoingSnapshot);
            }

            foreach (SlotPlan slot in plan.Slots)
            {
                if (!StandItemMatches(plan.Zdo, slot.Index, slot.OutgoingSnapshot))
                {
                    throw new InvalidOperationException($"Armor stand {slot.Definition.VisSlot} data did not verify after writing.");
                }
            }

            RefreshVisuals(plan.Stand, plan.Nview, plan.Slots, useOutgoing: true);
            if (Game.instance != null)
            {
                Game.instance.IncrementPlayerStat(PlayerStatType.ArmorStandUses);
            }

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            bool rollbackSucceeded = RollBack(plan, addedIncoming, removedOutgoing, standTouched);
            HarnessPrefabsPlugin.Log.LogError(
                $"ArmorStand equipment swap failed{(rollbackSucceeded ? " and was rolled back" : "; rollback was incomplete")}: {ex}");
            error = rollbackSucceeded
                ? "Equipment swap failed and was safely rolled back."
                : "Equipment swap failed. Some items could not be restored; check the ground and log.";
            return false;
        }
    }

    private static void PlaySwapEffect(SwapPlan plan)
    {
        try
        {
            bool placedItem = false;
            bool removedItem = false;
            foreach (SlotPlan slot in plan.Slots)
            {
                placedItem |= slot.Outgoing != null;
                removedItem |= slot.Incoming != null;
            }

            if (placedItem)
            {
                ResolveSwapEffectList(plan.Stand, removal: false)
                    ?.Create(plan.Stand.transform.position, Quaternion.identity);
                return;
            }

            if (removedItem)
            {
                Vector3 position = plan.Stand.m_dropSpawnPoint
                    ? plan.Stand.m_dropSpawnPoint.position
                    : plan.Stand.transform.position;
                ResolveSwapEffectList(plan.Stand, removal: true)?.Create(position, Quaternion.identity);
            }
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogWarning($"ArmorStand swap effect failed; the swap remains applied: {ex.Message}");
        }
    }

    private static EffectList? ResolveSwapEffectList(ArmorStand stand, bool removal)
    {
        EffectList? effects = removal ? stand.m_destroyEffects : stand.m_effects;
        if (CanCreateSwapEffects(effects))
        {
            return effects;
        }

        if (string.Equals(
                HarnessPrefabsRuntime.NormalizePrefabName(stand.gameObject.name),
                "ArmorStand",
                StringComparison.Ordinal))
        {
            return null;
        }

        GameObject? basePrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab("ArmorStand") : null;
        ArmorStand? baseStand = basePrefab ? basePrefab.GetComponent<ArmorStand>() : null;
        EffectList? fallback = baseStand
            ? removal ? baseStand.m_destroyEffects : baseStand.m_effects
            : null;
        return CanCreateSwapEffects(fallback) ? fallback : null;
    }

    private static bool CanCreateSwapEffects(EffectList? effects)
    {
        EffectList.EffectData[]? entries = effects?.m_effectPrefabs;
        if (entries == null || entries.Length == 0)
        {
            return false;
        }

        foreach (EffectList.EffectData entry in entries)
        {
            if (entry == null || entry.m_enabled && !entry.m_prefab)
            {
                return false;
            }
        }

        return true;
    }

    private static void RemoveOutgoingHandItem(
        SwapPlan plan,
        ItemDrop.ItemData? item,
        List<SlotPlan> removedOutgoing)
    {
        if (item == null)
        {
            return;
        }

        foreach (SlotPlan slot in plan.Slots)
        {
            if (slot.IsHand && ReferenceEquals(slot.Outgoing, item))
            {
                RemoveOutgoingItem(plan, slot, removedOutgoing);
                return;
            }
        }

        throw new InvalidOperationException("Could not map a player hand item to its armor stand slot.");
    }

    private static void RemoveOutgoingItem(
        SwapPlan plan,
        SlotPlan slot,
        List<SlotPlan> removedOutgoing)
    {
        ItemDrop.ItemData item = slot.Outgoing ??
                                 throw new InvalidOperationException("An outgoing equipment slot was empty.");
        removedOutgoing.Add(slot);
        plan.Player.UnequipItem(item, triggerEquipEffects: false);
        if (plan.Player.IsItemEquiped(item))
        {
            throw new InvalidOperationException($"Could not unequip the player's {slot.Definition.VisSlot} item.");
        }

        if (!plan.Inventory.RemoveItem(item))
        {
            throw new InvalidOperationException($"Could not remove the player's {slot.Definition.VisSlot} item from inventory.");
        }
    }

    private static bool RollBack(
        SwapPlan plan,
        List<SlotPlan> addedIncoming,
        List<SlotPlan> removedOutgoing,
        bool standTouched)
    {
        bool succeeded = true;
        bool standRestored = !standTouched;

        if (standTouched)
        {
            try
            {
                if (!plan.Nview.IsValid() || !plan.Nview.IsOwner() || plan.Nview.GetZDO() != plan.Zdo)
                {
                    throw new InvalidOperationException("Armor stand ownership changed during rollback.");
                }

                foreach (SlotPlan slot in plan.Slots)
                {
                    WriteStandItem(plan.Zdo, slot.Index, slot.StandSnapshot);
                }

                foreach (SlotPlan slot in plan.Slots)
                {
                    if (!StandItemMatches(plan.Zdo, slot.Index, slot.StandSnapshot))
                    {
                        throw new InvalidOperationException(
                            $"Armor stand {slot.Definition.VisSlot} data did not verify during rollback.");
                    }
                }

                standRestored = true;
                RefreshVisuals(plan.Stand, plan.Nview, plan.Slots, useOutgoing: false);
            }
            catch (Exception ex)
            {
                succeeded = false;
                HarnessPrefabsPlugin.Log.LogError($"Rollback failed while restoring ArmorStand ZDO data: {ex}");
            }
        }

        foreach (SlotPlan slot in addedIncoming)
        {
            if (slot.Incoming == null)
            {
                continue;
            }

            try
            {
                plan.Player.UnequipItem(slot.Incoming, triggerEquipEffects: false);
            }
            catch (Exception ex)
            {
                succeeded = false;
                HarnessPrefabsPlugin.Log.LogError($"Rollback failed while unequipping incoming {slot.Definition.VisSlot} item: {ex}");
            }

            try
            {
                ForceClearEquippedItem(plan.Player, slot.Incoming);
            }
            catch (Exception ex)
            {
                succeeded = false;
                HarnessPrefabsPlugin.Log.LogError($"Rollback failed while clearing incoming {slot.Definition.VisSlot} equipment state: {ex}");
            }

            if (!standRestored)
            {
                continue;
            }

            try
            {
                if (plan.Inventory.ContainsItem(slot.Incoming) && !plan.Inventory.RemoveItem(slot.Incoming))
                {
                    succeeded = false;
                    HarnessPrefabsPlugin.Log.LogError($"Rollback could not remove incoming {slot.Definition.VisSlot} item.");
                }
            }
            catch (Exception ex)
            {
                succeeded = false;
                HarnessPrefabsPlugin.Log.LogError($"Rollback failed while removing incoming {slot.Definition.VisSlot} item from inventory: {ex}");
            }
        }

        foreach (SlotPlan slot in removedOutgoing)
        {
            try
            {
                if (slot.Outgoing == null)
                {
                    continue;
                }

                bool restored = plan.Inventory.ContainsItem(slot.Outgoing) ||
                                plan.Inventory.AddItem(slot.Outgoing, slot.OriginalPosition) ||
                                plan.Inventory.AddItem(slot.Outgoing);
                if (!restored)
                {
                    ItemDrop.DropItem(
                        slot.Outgoing,
                        1,
                        plan.Player.transform.position + Vector3.up,
                        plan.Player.transform.rotation);
                    succeeded = false;
                    HarnessPrefabsPlugin.Log.LogError(
                        $"Rollback dropped the original {slot.Definition.VisSlot} item because inventory restoration failed.");
                    continue;
                }

                if (slot.IsHand)
                {
                    slot.Outgoing.m_equipped = false;
                    continue;
                }

                if (GetEquippedItem(plan.Player, slot.Definition.VisSlot) == slot.Outgoing)
                {
                    slot.Outgoing.m_equipped = true;
                }
                else
                {
                    slot.Outgoing.m_equipped = false;
                    if (!plan.Player.EquipItem(slot.Outgoing, triggerEquipEffects: false) ||
                        GetEquippedItem(plan.Player, slot.Definition.VisSlot) != slot.Outgoing)
                    {
                        succeeded = false;
                        HarnessPrefabsPlugin.Log.LogError($"Rollback could not re-equip the original {slot.Definition.VisSlot} item.");
                    }
                }
            }
            catch (Exception ex)
            {
                succeeded = false;
                HarnessPrefabsPlugin.Log.LogError($"Rollback failed while restoring outgoing {slot.Definition.VisSlot} item: {ex}");
            }
        }

        try
        {
            if (!TryRestoreOriginalHandState(plan, out string handError))
            {
                succeeded = false;
                HarnessPrefabsPlugin.Log.LogError($"Rollback could not restore the original hand set: {handError}");
            }
        }
        catch (Exception ex)
        {
            succeeded = false;
            HarnessPrefabsPlugin.Log.LogError($"Rollback failed while restoring the original hand set: {ex}");
        }

        if (standTouched && !standRestored)
        {
            HarnessPrefabsPlugin.Log.LogError(
                "Rollback kept the incoming inventory items because ArmorStand ZDO restoration was incomplete. " +
                "This favors item preservation over duplicate removal.");
        }

        return succeeded;
    }

    private static void EnsureTransactionStillValid(SwapPlan plan)
    {
        if (!IsSupportedPlayerBuiltPrefab(plan.Stand) || !plan.Nview || !plan.Nview.IsValid() || !plan.Nview.IsOwner() ||
            plan.Nview.GetZDO() != plan.Zdo || plan.Zdo.m_uid != plan.ZdoId)
        {
            throw new InvalidOperationException("Armor stand ownership or identity changed during the swap.");
        }

        if (plan.Zdo.DataRevision != plan.InitialRevision)
        {
            throw new InvalidOperationException("Armor stand contents changed during the swap.");
        }

        if (!IsPlayerReady(plan.Player) || !PrivateArea.CheckAccess(plan.Stand.transform.position, 0f, flash: false))
        {
            throw new InvalidOperationException("The player can no longer use this armor stand.");
        }
    }

    private static bool TryFindUniqueSlot(
        ArmorStand stand,
        VisSlot visSlot,
        out int index,
        out ArmorStand.ArmorStandSlot foundSlot)
    {
        index = -1;
        foundSlot = null!;
        for (int i = 0; i < stand.m_slots.Count; i++)
        {
            ArmorStand.ArmorStandSlot slot = stand.m_slots[i];
            if (slot.m_slot != visSlot)
            {
                continue;
            }

            if (foundSlot != null)
            {
                index = -1;
                foundSlot = null!;
                return false;
            }

            index = i;
            foundSlot = slot;
        }

        return foundSlot != null;
    }

    private static bool TryValidatePlayerItem(
        ArmorStand stand,
        ArmorStand.ArmorStandSlot standSlot,
        SlotDefinition definition,
        Player player,
        Inventory inventory,
        ItemDrop.ItemData item,
        out string error)
    {
        if (!inventory.ContainsItem(item) || !player.IsItemEquiped(item))
        {
            error = $"The equipped {definition.VisSlot} item is not in the main inventory.";
            return false;
        }

        if (!TryValidateWearableItem(item, definition, out error))
        {
            return false;
        }

        if (!TryValidateEquipAvailability(
                item,
                $"equipped {definition.VisSlot}",
                definition.ItemType == ItemDrop.ItemData.ItemType.Utility,
                out error))
        {
            return false;
        }

        if (!CanAttach(stand, standSlot, item) || !HasArmorStandVisual(item))
        {
            error = $"The equipped {definition.VisSlot} item cannot be displayed on this armor stand.";
            return false;
        }

        Vector2i position = item.m_gridPos;
        if (position.x < 0 || position.y < 0 || position.x >= inventory.GetWidth() || position.y >= inventory.GetHeight() ||
            inventory.GetItemAt(position.x, position.y) != item)
        {
            error = $"The equipped {definition.VisSlot} item has an invalid inventory position.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryValidatePlayerHandItem(
        Player player,
        Inventory inventory,
        ItemDrop.ItemData? item,
        string side,
        bool sheathed,
        out string error)
    {
        if (item == null)
        {
            error = "";
            return true;
        }

        bool stateMatches = sheathed
            ? !item.m_equipped && !player.IsItemEquiped(item)
            : item.m_equipped && player.IsItemEquiped(item);
        if (!inventory.ContainsItem(item) || !stateMatches)
        {
            error = $"The {(sheathed ? "sheathed" : "equipped")} {side}-hand item is not in a valid inventory state.";
            return false;
        }

        if (!TryValidateHandItem(item, $"{side}-hand", out error))
        {
            return false;
        }

        if (!TryValidateEquipAvailability(
                item,
                $"{(sheathed ? "sheathed" : "equipped")} {side}-hand",
                worldLevelRestricted: false,
                out error))
        {
            return false;
        }

        if (!HasArmorStandVisual(item))
        {
            error = $"The equipped {side}-hand item has no armor stand visual.";
            return false;
        }

        Vector2i position = item.m_gridPos;
        if (position.x < 0 || position.y < 0 || position.x >= inventory.GetWidth() || position.y >= inventory.GetHeight() ||
            inventory.GetItemAt(position.x, position.y) != item)
        {
            error = $"The equipped {side}-hand item has an invalid inventory position.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryValidateStandHandItem(
        ArmorStand stand,
        ArmorStand.ArmorStandSlot standSlot,
        VisSlot visSlot,
        ItemDrop.ItemData item,
        out string error)
    {
        if (!TryValidateHandItem(item, visSlot.ToString(), out error))
        {
            return false;
        }

        if (!TryValidateEquipAvailability(
                item,
                $"armor stand's {visSlot}",
                worldLevelRestricted: false,
                out error))
        {
            return false;
        }

        if (!CanAttach(stand, standSlot, item) || !HasArmorStandVisual(item))
        {
            error = $"The armor stand's {visSlot} item is incompatible with its current slot.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryValidateIncomingItem(
        SlotDefinition definition,
        ItemDrop.ItemData item,
        out string error)
    {
        if (!TryValidateWearableItem(item, definition, out error))
        {
            return false;
        }

        return TryValidateEquipAvailability(
            item,
            $"armor stand's {definition.VisSlot}",
            definition.ItemType == ItemDrop.ItemData.ItemType.Utility,
            out error);
    }

    private static bool TryValidateEquipAvailability(
        ItemDrop.ItemData item,
        string label,
        bool worldLevelRestricted,
        out string error)
    {
        if (item.m_shared.m_useDurability && item.m_durability <= 0f)
        {
            error = $"The {label} item is broken.";
            return false;
        }

        if (!string.IsNullOrEmpty(item.m_shared.m_dlc) &&
            (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(item.m_shared.m_dlc)))
        {
            error = $"The {label} item requires unavailable DLC.";
            return false;
        }

        if (worldLevelRestricted && Game.m_worldLevel > 0 && item.m_worldLevel < Game.m_worldLevel)
        {
            error = $"The {label} item cannot be equipped in this world level.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryValidateWearableItem(
        ItemDrop.ItemData item,
        SlotDefinition definition,
        out string error)
    {
        if (item.m_shared == null || item.m_shared.m_itemType != definition.ItemType)
        {
            error = $"The {definition.VisSlot} slot contains an incompatible item type.";
            return false;
        }

        return TryValidateSingleEquipmentItem(item, definition.VisSlot.ToString(), out error);
    }

    private static bool TryValidateHandItem(
        ItemDrop.ItemData item,
        string label,
        out string error)
    {
        if (item.m_shared == null || !IsHandItemType(item.m_shared.m_itemType))
        {
            error = $"The {label} slot contains an incompatible item type.";
            return false;
        }

        return TryValidateSingleEquipmentItem(item, label, out error);
    }

    private static bool TryValidateSingleEquipmentItem(
        ItemDrop.ItemData item,
        string label,
        out string error)
    {

        if (item.m_stack != 1 || item.m_shared.m_maxStackSize != 1)
        {
            error = $"Stackable {label} equipment cannot be safely swapped.";
            return false;
        }

        if (!item.m_dropPrefab || !item.m_dropPrefab.GetComponent<ItemDrop>())
        {
            error = $"The {label} item prefab is missing.";
            return false;
        }

        string prefabName = item.m_dropPrefab.name;
        if (string.IsNullOrEmpty(prefabName) || ObjectDB.instance.GetItemPrefab(prefabName) == null)
        {
            error = $"The {label} item prefab is not registered in ObjectDB.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool IsHandItemType(ItemDrop.ItemData.ItemType itemType)
    {
        return itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
               itemType == ItemDrop.ItemData.ItemType.Tool ||
               itemType == ItemDrop.ItemData.ItemType.Torch ||
               itemType == ItemDrop.ItemData.ItemType.Shield ||
               itemType == ItemDrop.ItemData.ItemType.Bow ||
               itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
               itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
    }

    private static bool TryGetCanonicalHandState(
        ItemDrop.ItemData? first,
        ItemDrop.ItemData? second,
        out ItemDrop.ItemData? left,
        out ItemDrop.ItemData? right)
    {
        // Keep this in step with Humanoid.EquipItem, which uses the actual item type.
        left = null;
        right = null;
        if (first == null && second == null)
        {
            return true;
        }

        if (first != null && ReferenceEquals(first, second))
        {
            return false;
        }

        if (first == null || second == null)
        {
            ItemDrop.ItemData item = first ?? second!;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    left = item;
                    return true;
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                    right = item;
                    return true;
                default:
                    return false;
            }
        }

        ItemDrop.ItemData.ItemType firstType = first.m_shared.m_itemType;
        ItemDrop.ItemData.ItemType secondType = second.m_shared.m_itemType;
        if (firstType == ItemDrop.ItemData.ItemType.Shield &&
            (secondType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
             secondType == ItemDrop.ItemData.ItemType.Torch))
        {
            left = first;
            right = second;
            return true;
        }

        if (secondType == ItemDrop.ItemData.ItemType.Shield &&
            (firstType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
             firstType == ItemDrop.ItemData.ItemType.Torch))
        {
            left = second;
            right = first;
            return true;
        }

        if (firstType == ItemDrop.ItemData.ItemType.Torch &&
            secondType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
        {
            left = first;
            right = second;
            return true;
        }

        if (secondType == ItemDrop.ItemData.ItemType.Torch &&
            firstType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
        {
            left = second;
            right = first;
            return true;
        }

        return false;
    }

    private static bool TryLoadStandItem(
        int index,
        int prefabHash,
        ZDO zdo,
        out ItemDrop.ItemData item,
        out string error)
    {
        item = null!;
        GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabHash);
        ItemDrop? itemDrop = prefab ? prefab.GetComponent<ItemDrop>() : null;
        if (!prefab || !itemDrop)
        {
            error = $"Armor stand item prefab '{prefabHash}' could not be resolved.";
            return false;
        }

        byte[] data = zdo.GetByteArray((index + "_itemData").GetStableHashCode());
        if (data == null || data.Length <= 2)
        {
            error = "Armor stand item data is missing; no items were moved.";
            return false;
        }

        item = itemDrop.m_itemData.Clone();
        item.m_customData.Clear();
        item.m_dropPrefab = prefab;
        item.m_equipped = false;
        if (!StandItemData.TryRead(data, prefabHash, item, out error)) return false;
        error = "";
        return true;
    }


    private static bool TryReserveIncomingPositions(Inventory inventory, List<SlotPlan> slots, out string error)
    {
        HashSet<int> reserved = new();
        int width = inventory.GetWidth();
        int height = inventory.GetHeight();

        foreach (SlotPlan slot in slots)
        {
            if (slot.Incoming == null || slot.Outgoing == null)
            {
                continue;
            }

            slot.IncomingPosition = slot.OriginalPosition;
            reserved.Add(ToPositionKey(slot.IncomingPosition, width));
        }

        foreach (SlotPlan slot in slots)
        {
            if (slot.Incoming == null || slot.Outgoing != null)
            {
                continue;
            }

            bool found = false;
            for (int y = 0; y < height && !found; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int key = y * width + x;
                    if (reserved.Contains(key))
                    {
                        continue;
                    }

                    ItemDrop.ItemData occupying = inventory.GetItemAt(x, y);
                    if (occupying != null && !IsOutgoingItem(slots, occupying))
                    {
                        continue;
                    }

                    slot.IncomingPosition = new Vector2i(x, y);
                    reserved.Add(key);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                error = "Not enough inventory space to swap this equipment set.";
                return false;
            }
        }

        error = "";
        return true;
    }

    private static bool IsOutgoingItem(List<SlotPlan> slots, ItemDrop.ItemData item)
    {
        foreach (SlotPlan slot in slots)
        {
            if (ReferenceEquals(slot.Outgoing, item))
            {
                return true;
            }
        }

        return false;
    }

    private static int ToPositionKey(Vector2i position, int width)
    {
        return position.y * width + position.x;
    }

    private static void WriteStandItem(ZDO zdo, int index, ItemDrop.ItemData? item)
    {
        if (item == null)
        {
            zdo.Set(index + "_item", 0);
            zdo.Set(index + "_variant", 0);
            return;
        }

        ItemDrop.SaveToZDO(item, zdo, index);
        zdo.Set(index + "_item", item.m_dropPrefab.name.GetStableHashCode());
        zdo.Set(index + "_variant", item.m_variant);
    }

    private static bool StandItemMatches(ZDO zdo, int index, ItemDrop.ItemData? expected)
    {
        int itemHash = zdo.GetInt(index + "_item", 0);
        if (expected == null)
        {
            return itemHash == 0;
        }

        if (itemHash != expected.m_dropPrefab.name.GetStableHashCode() ||
            !TryLoadStandItem(index, itemHash, zdo, out ItemDrop.ItemData actual, out _))
        {
            return false;
        }

        return StandItemData.Matches(actual, expected);
    }


    private static void RefreshVisuals(
        ArmorStand stand,
        ZNetView nview,
        List<SlotPlan> slots,
        bool useOutgoing)
    {
        foreach (SlotPlan slot in slots)
        {
            ItemDrop.ItemData? item = useOutgoing ? slot.OutgoingSnapshot : slot.StandSnapshot;
            int itemHash = item?.m_dropPrefab.name.GetStableHashCode() ?? 0;
            nview.InvokeRPC(ZNetView.Everybody, "RPC_SetVisualItem", slot.Index, itemHash, item?.m_variant ?? 0);
        }
        // The game's empty-slot branch returns before refreshing supports and cloths.
        UpdateSupports(stand);
        ArmorStandCloths(stand) = stand.GetComponentsInChildren<Cloth>();
    }

    private static bool HasArmorStandVisual(ItemDrop.ItemData item)
    {
        if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest ||
            item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs)
        {
            return true;
        }

        Transform root = item.m_dropPrefab.transform;
        for (int i = 0; i < root.childCount; i++)
        {
            string childName = root.GetChild(i).name;
            if (childName == "attach" || childName == "attach_skin")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlayerReady(Player player)
    {
        return player &&
               player == Player.m_localPlayer &&
               !player.IsDead() &&
               !player.IsTeleporting() &&
               !player.InAttack() &&
               !player.InDodge() &&
               !player.InMinorAction() &&
               player.GetActionQueueCount() == 0 &&
               (!player.IsSwimming() || player.IsOnGround());
    }

    private static bool IsSupportedPlayerBuiltPrefab(ArmorStand stand)
    {
        if (!stand || !SupportedPrefabNames.Contains(HarnessPrefabsRuntime.NormalizePrefabName(stand.gameObject.name)))
        {
            return false;
        }

        Piece piece = stand.GetComponentInParent<Piece>();
        return piece && piece.IsPlacedByPlayer();
    }

    private static ZNetView GetNetView(ArmorStand stand)
    {
        if (ArmorStandNview(stand))
        {
            return ArmorStandNview(stand);
        }

        return stand.m_netViewOverride ? stand.m_netViewOverride : stand.GetComponent<ZNetView>();
    }

    private static ItemDrop.ItemData? GetEquippedItem(Player player, VisSlot slot)
    {
        return slot switch
        {
            VisSlot.Helmet => HumanoidHelmetItem(player),
            VisSlot.Chest => HumanoidChestItem(player),
            VisSlot.Legs => HumanoidLegItem(player),
            VisSlot.Shoulder => HumanoidShoulderItem(player),
            VisSlot.Utility => HumanoidUtilityItem(player),
            _ => null
        };
    }

    private static bool TryEquipHandState(
        Player player,
        ItemDrop.ItemData? expectedLeft,
        ItemDrop.ItemData? expectedRight,
        out string error)
    {
        if (HumanoidLeftItem(player) != null || HumanoidRightItem(player) != null)
        {
            error = "The player's hands were not empty before equipping the new hand set.";
            return false;
        }

        if (HumanoidHiddenLeftItem(player) != null || HumanoidHiddenRightItem(player) != null)
        {
            error = "The player's sheathed hand equipment changed during the swap.";
            return false;
        }

        if (expectedRight != null)
        {
            if (!ReferenceEquals(HumanoidRightItem(player), expectedRight))
            {
                player.EquipItem(expectedRight, triggerEquipEffects: false);
            }

            if (!ReferenceEquals(HumanoidRightItem(player), expectedRight))
            {
                error = "Could not equip the armor stand's right-hand item.";
                return false;
            }
        }

        if (expectedLeft != null)
        {
            // Equipping a weapon can synchronously auto-equip its shield through
            // another mod. Native EquipItem returns false for an already equipped item.
            if (!ReferenceEquals(HumanoidLeftItem(player), expectedLeft))
            {
                player.EquipItem(expectedLeft, triggerEquipEffects: false);
            }

            if (!ReferenceEquals(HumanoidLeftItem(player), expectedLeft))
            {
                error = "Could not equip the armor stand's left-hand item.";
                return false;
            }
        }

        if (!HandStateMatches(player, expectedLeft, expectedRight))
        {
            error = "The armor stand's hand items could not be equipped together.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryApplyHandState(
        Player player,
        ItemDrop.ItemData? expectedLeft,
        ItemDrop.ItemData? expectedRight,
        bool sheathed,
        out string error)
    {
        if (!sheathed)
        {
            return TryEquipHandState(player, expectedLeft, expectedRight, out error);
        }

        if (!AreHandReferencesClear(player))
        {
            error = "The player's hand state was not empty before storing the new hand set.";
            return false;
        }

        Inventory inventory = player.GetInventory();
        if (inventory == null ||
            (expectedLeft != null && !inventory.ContainsItem(expectedLeft)) ||
            (expectedRight != null && !inventory.ContainsItem(expectedRight)))
        {
            error = "The new hand set is not in the player's main inventory.";
            return false;
        }

        if (expectedLeft != null)
        {
            expectedLeft.m_equipped = false;
        }

        if (expectedRight != null)
        {
            expectedRight.m_equipped = false;
        }

        HumanoidHiddenLeftItem(player) = expectedLeft;
        HumanoidHiddenRightItem(player) = expectedRight;
        SetupEquipment(player);

        if (!HandStateMatches(player, expectedLeft, expectedRight, sheathed: true))
        {
            error = "The armor stand's hand items could not be stored on the player's back.";
            return false;
        }

        error = "";
        return true;
    }

    private static bool TryRestoreOriginalHandState(SwapPlan plan, out string error)
    {
        HandSetPlan hands = plan.HandSet;
        if (HandStateMatches(plan.Player, hands.OriginalLeft, hands.OriginalRight, hands.Sheathed))
        {
            error = "";
            return true;
        }

        if (!IsKnownHandReference(HumanoidLeftItem(plan.Player), hands) ||
            !IsKnownHandReference(HumanoidRightItem(plan.Player), hands) ||
            !IsKnownHandReference(HumanoidHiddenLeftItem(plan.Player), hands) ||
            !IsKnownHandReference(HumanoidHiddenRightItem(plan.Player), hands))
        {
            error = "an unrelated item entered the player's hand state";
            return false;
        }

        ClearKnownHandItem(plan.Player, hands.IncomingRight);
        ClearKnownHandItem(plan.Player, hands.IncomingLeft);
        ClearKnownHandItem(plan.Player, hands.OriginalRight);
        ClearKnownHandItem(plan.Player, hands.OriginalLeft);
        if (!AreHandReferencesClear(plan.Player))
        {
            error = "the temporary hand state could not be cleared";
            return false;
        }

        return TryApplyHandState(
            plan.Player,
            hands.OriginalLeft,
            hands.OriginalRight,
            hands.Sheathed,
            out error);
    }

    private static bool IsKnownHandReference(ItemDrop.ItemData? item, HandSetPlan hands)
    {
        return item == null ||
               ReferenceEquals(item, hands.OriginalLeft) ||
               ReferenceEquals(item, hands.OriginalRight) ||
               ReferenceEquals(item, hands.IncomingLeft) ||
               ReferenceEquals(item, hands.IncomingRight);
    }

    private static void ClearKnownHandItem(Player player, ItemDrop.ItemData? item)
    {
        if (item == null)
        {
            return;
        }

        player.UnequipItem(item, triggerEquipEffects: false);
        ForceClearEquippedItem(player, item);
    }

    private static bool HandStateMatches(
        Player player,
        ItemDrop.ItemData? expectedLeft,
        ItemDrop.ItemData? expectedRight,
        bool sheathed = false)
    {
        Inventory inventory = player.GetInventory();
        if (inventory == null)
        {
            return false;
        }

        bool leftItemMatches = expectedLeft == null ||
                               expectedLeft.m_equipped == !sheathed && inventory.ContainsItem(expectedLeft);
        bool rightItemMatches = expectedRight == null ||
                                expectedRight.m_equipped == !sheathed && inventory.ContainsItem(expectedRight);
        if (!leftItemMatches || !rightItemMatches)
        {
            return false;
        }

        return sheathed
            ? HumanoidLeftItem(player) == null &&
              HumanoidRightItem(player) == null &&
              ReferenceEquals(HumanoidHiddenLeftItem(player), expectedLeft) &&
              ReferenceEquals(HumanoidHiddenRightItem(player), expectedRight)
            : ReferenceEquals(HumanoidLeftItem(player), expectedLeft) &&
              ReferenceEquals(HumanoidRightItem(player), expectedRight) &&
              HumanoidHiddenLeftItem(player) == null &&
              HumanoidHiddenRightItem(player) == null;
    }

    private static bool AreHandReferencesClear(Player player)
    {
        return HumanoidLeftItem(player) == null &&
               HumanoidRightItem(player) == null &&
               HumanoidHiddenLeftItem(player) == null &&
               HumanoidHiddenRightItem(player) == null;
    }

    private static void ForceClearEquippedItem(Player player, ItemDrop.ItemData expected)
    {
        bool changed = false;
        if (ReferenceEquals(HumanoidHelmetItem(player), expected))
        {
            HumanoidHelmetItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidChestItem(player), expected))
        {
            HumanoidChestItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidLegItem(player), expected))
        {
            HumanoidLegItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidShoulderItem(player), expected))
        {
            HumanoidShoulderItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidUtilityItem(player), expected))
        {
            HumanoidUtilityItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidLeftItem(player), expected))
        {
            HumanoidLeftItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidRightItem(player), expected))
        {
            HumanoidRightItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidHiddenLeftItem(player), expected))
        {
            HumanoidHiddenLeftItem(player) = null;
            changed = true;
        }

        if (ReferenceEquals(HumanoidHiddenRightItem(player), expected))
        {
            HumanoidHiddenRightItem(player) = null;
            changed = true;
        }

        if (changed || expected.m_equipped)
        {
            expected.m_equipped = false;
            SetupEquipment(player);
        }
    }

    private static void ShowMessage(Player? player, string message)
    {
        if (player)
        {
            player.Message(MessageHud.MessageType.Center, message);
        }
    }


    // Serializer-only operations share no Unity object lifecycle state.
    internal static class StandItemData
    {
        internal static bool TryRead(byte[] data, int prefabHash, ItemDrop.ItemData item, out string error)
        {
            try
            {
                ZPackage package = new(data);
                Version.Item version = (Version.Item)package.ReadByte();
                if (!Enum.IsDefined(typeof(Version.Item), version))
                {
                    error = "Armor stand item data has an unknown game format.";
                    return false;
                }
                int storedHash = ItemDrop.ItemData.Load(package, item, version);
                if (storedHash != prefabHash || package.GetPos() != data.Length)
                {
                    error = "Armor stand item identity or payload does not match; no items were moved.";
                    return false;
                }
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = "Armor stand item data could not be read: " + ex.Message;
                return false;
            }
        }
        internal static bool Matches(ItemDrop.ItemData actual, ItemDrop.ItemData expected)
        {
            // ItemData.Save stores durability as integer hundredths in 1.0.7. Compare
            // the game's stored value, otherwise a successful write triggers rollback.
            float savedDurability = (int)(expected.m_durability * 100f) * 0.01f;
            if (actual.m_stack != expected.m_stack ||
                actual.m_durability != savedDurability ||
                actual.m_quality != expected.m_quality ||
                actual.m_variant != expected.m_variant ||
                actual.m_crafterID != expected.m_crafterID ||
                !string.Equals(actual.m_crafterName, expected.m_crafterID == 0 ? "" : expected.m_crafterName, StringComparison.Ordinal) ||
                actual.m_worldLevel != expected.m_worldLevel ||
                actual.m_pickedUp != expected.m_pickedUp ||
                actual.m_cheated != expected.m_cheated ||
                actual.m_customData.Count != expected.m_customData.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in expected.m_customData)
            {
                if (!actual.m_customData.TryGetValue(pair.Key, out string value) ||
                    !string.Equals(value, pair.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class SlotDefinition
    {
        internal SlotDefinition(VisSlot visSlot, ItemDrop.ItemData.ItemType itemType)
        {
            VisSlot = visSlot;
            ItemType = itemType;
        }

        internal VisSlot VisSlot { get; }

        internal ItemDrop.ItemData.ItemType ItemType { get; }
    }

    private sealed class SlotPlan
    {
        internal SlotPlan(
            SlotDefinition definition,
            int index,
            ItemDrop.ItemData? standSnapshot,
            ItemDrop.ItemData? incoming,
            ItemDrop.ItemData? outgoing,
            ItemDrop.ItemData? outgoingSnapshot,
            bool isHand = false)
        {
            Definition = definition;
            Index = index;
            StandSnapshot = standSnapshot;
            Incoming = incoming;
            Outgoing = outgoing;
            OutgoingSnapshot = outgoingSnapshot;
            IsHand = isHand;
            OriginalPosition = outgoing?.m_gridPos ?? new Vector2i(-1, -1);
            IncomingPosition = new Vector2i(-1, -1);
        }

        internal SlotDefinition Definition { get; }

        internal int Index { get; }

        internal ItemDrop.ItemData? StandSnapshot { get; }

        internal ItemDrop.ItemData? Incoming { get; }

        internal ItemDrop.ItemData? Outgoing { get; }

        internal ItemDrop.ItemData? OutgoingSnapshot { get; }

        internal bool IsHand { get; }

        internal Vector2i OriginalPosition { get; }

        internal Vector2i IncomingPosition { get; set; }
    }

    private sealed class SwapPlan
    {
        internal SwapPlan(
            ArmorStand stand,
            ZNetView nview,
            ZDO zdo,
            ZDOID zdoId,
            uint initialRevision,
            Player player,
            Inventory inventory,
            List<SlotPlan> slots,
            HandSetPlan handSet,
            bool utilitySkipped)
        {
            Stand = stand;
            Nview = nview;
            Zdo = zdo;
            ZdoId = zdoId;
            InitialRevision = initialRevision;
            Player = player;
            Inventory = inventory;
            Slots = slots;
            HandSet = handSet;
            UtilitySkipped = utilitySkipped;
        }

        internal ArmorStand Stand { get; }

        internal ZNetView Nview { get; }

        internal ZDO Zdo { get; }

        internal ZDOID ZdoId { get; }

        internal uint InitialRevision { get; }

        internal Player Player { get; }

        internal Inventory Inventory { get; }

        internal List<SlotPlan> Slots { get; }

        internal HandSetPlan HandSet { get; }

        internal bool UtilitySkipped { get; }
    }

    private sealed class HandSetPlan
    {
        internal HandSetPlan(
            ItemDrop.ItemData? originalLeft,
            ItemDrop.ItemData? originalRight,
            ItemDrop.ItemData? incomingLeft,
            ItemDrop.ItemData? incomingRight,
            bool sheathed)
        {
            OriginalLeft = originalLeft;
            OriginalRight = originalRight;
            IncomingLeft = incomingLeft;
            IncomingRight = incomingRight;
            Sheathed = sheathed;
        }

        internal ItemDrop.ItemData? OriginalLeft { get; }

        internal ItemDrop.ItemData? OriginalRight { get; }

        internal ItemDrop.ItemData? IncomingLeft { get; }

        internal ItemDrop.ItemData? IncomingRight { get; }

        internal bool Sheathed { get; }
    }
}

[HarmonyPatch(typeof(Switch), nameof(Switch.Interact))]
internal static class HarnessPrefabsArmorStandInteractPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        Switch __instance,
        Humanoid character,
        bool hold,
        ref bool __result)
    {
        if (!HarnessPrefabsArmorStandSwap.IsSwapModifierPressed() ||
            character is not Player player ||
            player != Player.m_localPlayer ||
            !HarnessPrefabsArmorStandSwap.TryGetTarget(__instance, out ArmorStand stand))
        {
            return true;
        }

        __result = !hold;
        if (!hold)
        {
            HarnessPrefabsArmorStandSwap.Begin(stand, player);
        }

        return false;
    }
}

[HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
internal static class HarnessPrefabsArmorStandHoverPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Switch __instance, ref string __result)
    {
        if (HarnessPrefabsArmorStandSwap.TryGetTarget(__instance, out _))
        {
            __result += HarnessPrefabsArmorStandSwap.GetHoverSuffix();
        }
    }
}
