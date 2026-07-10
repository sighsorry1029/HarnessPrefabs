#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;
using Requirement = Piece.Requirement;

namespace HarnessPrefabs;

internal static class PrefabBuildManager
{
    private static readonly HashSet<GameObject> AddedPrefabs = new();
    private static readonly HashSet<string> AddedAdminPieceNames = new(StringComparer.Ordinal);
    private static bool _initialized;
    private static bool _jotunnPiecesRegistered;
    private static bool _refreshing;
    private static bool? _lastHarnessHammerTabsEnabled;
    private static bool _hasCompletedFullRefresh;
    private static string _lastFullRefreshSignature = "";

    public static void Initialize()
    {
        _initialized = true;
    }

    public static void MarkJotunnPiecesRegistered()
    {
        _jotunnPiecesRegistered = true;
    }

    public static void RefreshIfHarnessHammerVisibilityChanged()
    {
        if (!_initialized)
        {
            return;
        }

        bool harnessEnabled = HarnessPrefabsPlugin.HarnessHammerTabsEnabled;
        if (_lastHarnessHammerTabsEnabled.HasValue &&
            _lastHarnessHammerTabsEnabled.Value == harnessEnabled)
        {
            return;
        }

        _lastHarnessHammerTabsEnabled = harnessEnabled;
        RefreshFromCachedRules("harness hammer visibility changed");
    }

    public static void Refresh(string reason)
    {
        if (!_initialized || _refreshing)
        {
            return;
        }

        if (!ZNetScene.instance || !ObjectDB.instance)
        {
            return;
        }

        if (!_jotunnPiecesRegistered && reason != "Jotunn.OnPiecesRegistered")
        {
            return;
        }

        PieceTable hammer = GetHammerPieceTable();
        if (!hammer)
        {
            return;
        }

        string fullRefreshSignature = BuildFullRefreshSignature(hammer);
        if (_hasCompletedFullRefresh &&
            PrefabRuleStore.HasCachedDiscoveries &&
            string.Equals(_lastFullRefreshSignature, fullRefreshSignature, StringComparison.Ordinal))
        {
            RefreshFromCachedRules($"{reason}; full discovery skipped");
            return;
        }

        _refreshing = true;
        Stopwatch total = Stopwatch.StartNew();
        long afterSetup = 0;
        long afterRemove = 0;
        long afterExisting = 0;
        long afterDiscover = 0;
        long afterRules = 0;
        int existingBuildableCount = 0;
        int discoveryCount = 0;
        try
        {
            HarnessPrefabsPlugin.EnsureSourceOfTruthFileMode();
            afterSetup = total.ElapsedMilliseconds;
            RemoveAddedPieces(hammer);
            RemoveAdminCategoryTabs(hammer);
            afterRemove = total.ElapsedMilliseconds;
            HashSet<string> existingBuildables = CollectExistingBuildables();
            existingBuildableCount = existingBuildables.Count;
            afterExisting = total.ElapsedMilliseconds;
            List<PrefabDiscovery> discoveries = PrefabClassifier.Discover(CollectScenePrefabs(), existingBuildables);
            discoveryCount = discoveries.Count;
            afterDiscover = total.ElapsedMilliseconds;
            PrefabRuleStore.LoadRulesForCurrentAuthority(discoveries);
            afterRules = total.ElapsedMilliseconds;
            ApplyActiveRulesToHammer(hammer, discoveries, reason);
            _lastFullRefreshSignature = fullRefreshSignature;
            _hasCompletedFullRefresh = true;
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogError($"Prefab refresh failed ({reason}): {ex}");
        }
        finally
        {
            total.Stop();
            LogRefreshProfile(
                reason,
                total.ElapsedMilliseconds,
                afterSetup,
                afterRemove - afterSetup,
                afterExisting - afterRemove,
                afterDiscover - afterExisting,
                afterRules - afterDiscover,
                total.ElapsedMilliseconds - afterRules,
                existingBuildableCount,
                discoveryCount);
            _refreshing = false;
        }
    }

    public static void RefreshFromCachedRules(string reason)
    {
        if (!_initialized || _refreshing)
        {
            return;
        }

        if (!PrefabRuleStore.HasCachedDiscoveries)
        {
            Refresh($"{reason}; full discovery fallback");
            return;
        }

        if (!ZNetScene.instance || !ObjectDB.instance)
        {
            return;
        }

        if (!_jotunnPiecesRegistered)
        {
            return;
        }

        PieceTable hammer = GetHammerPieceTable();
        if (!hammer)
        {
            return;
        }

        _refreshing = true;
        Stopwatch total = Stopwatch.StartNew();
        try
        {
            ApplyActiveRulesToHammer(hammer, PrefabRuleStore.LastDiscoveries, reason);
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogError($"Prefab cached-rule refresh failed ({reason}): {ex}");
        }
        finally
        {
            total.Stop();
            LogCachedRefreshProfile(reason, total.ElapsedMilliseconds, PrefabRuleStore.LastDiscoveries.Count);
            _refreshing = false;
        }
    }

    public static void PreparePieceTableForUpdate(PieceTable table, HashSet<string> knownRecipes)
    {
        if (!table)
        {
            return;
        }

        if (!IsHammerPieceTable(table))
        {
            RemoveAddedPiecesFromTable(table);
            RemoveAdminCategoryTabs(table);
            PrefabCategoryRegistry.EnsureGrown(table);
            return;
        }

        PrefabCategoryRegistry.EnsureGrown(table);
        if (!HarnessPrefabsPlugin.HarnessHammerTabsEnabled)
        {
            RemoveAdminCategoryTabs(table);
        }

        if (knownRecipes == null)
        {
            return;
        }

        foreach (string pieceName in AddedAdminPieceNames)
        {
            knownRecipes.Add(pieceName);
        }
    }

    private static void ApplyActiveRulesToHammer(PieceTable hammer, IReadOnlyCollection<PrefabDiscovery> discoveries, string reason)
    {
        _lastHarnessHammerTabsEnabled = HarnessPrefabsPlugin.HarnessHammerTabsEnabled;
        RemoveAddedPieces(hammer);
        RemoveAdminCategoryTabs(hammer);
        PrefabPlacementPatchRegistry.Clear();

        int publicCount = 0;
        int adminCount = 0;
        int hiddenCount = 0;

        foreach (PrefabDiscovery discovery in PrefabSortPolicy.SortForHammer(discoveries))
        {
            if (!PrefabRuleStore.TryGetRule(discovery.Name, out PrefabRule rule))
            {
                continue;
            }

            switch (rule.Access)
            {
                case PrefabAccess.Hidden:
                    hiddenCount++;
                    continue;
                case PrefabAccess.Admin when !HarnessPrefabsPlugin.HarnessHammerTabsEnabled:
                    adminCount++;
                    continue;
                case PrefabAccess.Admin:
                    adminCount++;
                    break;
                case PrefabAccess.Public:
                    publicCount++;
                    break;
            }

            PrefabPlacementPatchRegistry.Set(
                discovery.Name,
                MvbpPrefabDefaults.NeedsPlacementPatch(discovery.Name),
                MvbpCompatibilityDefaults.GetPlacementOffset(discovery.Name));
            if (discovery.Prefab && TryAddPrefabToHammer(discovery.Prefab, rule, hammer))
            {
                AddedPrefabs.Add(discovery.Prefab);
            }
        }

        if (!HarnessPrefabsPlugin.HarnessHammerTabsEnabled)
        {
            RemoveAdminCategoryTabs(hammer);
        }

        PrefabCategoryRegistry.EnsureGrown(hammer);
        if (Player.m_localPlayer)
        {
            Player.m_localPlayer.UpdateAvailablePiecesList();
        }

        if (HarnessPrefabsPlugin.Verbose)
        {
            HarnessPrefabsPlugin.Log.LogInfo($"Refresh complete ({reason}). public={publicCount}, admin={adminCount}, hidden={hiddenCount}, visibleAdded={AddedPrefabs.Count}, isAdmin={HarnessPrefabsPlugin.IsAdmin}, debugMode={HarnessPrefabsPlugin.IsDebugMode}");
        }
    }

    private static bool TryAddPrefabToHammer(GameObject prefab, PrefabRule rule, PieceTable hammer)
    {
        if (!prefab || !hammer || hammer.m_pieces.Contains(prefab))
        {
            return false;
        }

        if (!TryResolveRequirements(prefab.name, rule.Requirements, out Requirement[] resources) ||
            !TryResolveCraftingStation(prefab.name, rule.CraftingStation, out CraftingStation craftingStation))
        {
            return false;
        }

        Piece piece = EnsurePiece(prefab, rule, hammer, resources, craftingStation);
        if (!piece)
        {
            return false;
        }

        try
        {
            PieceManager.Instance.RegisterPieceInPieceTable(prefab, PieceTables.Hammer);
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogWarning($"Jotunn failed to register prefab '{prefab.name}' in Hammer table: {ex.Message}");
            return false;
        }

        if (!hammer.m_pieces.Contains(prefab))
        {
            return false;
        }

        if (rule.Access == PrefabAccess.Admin)
        {
            AddedAdminPieceNames.Add(piece.m_name);
        }

        return true;
    }

    private static Piece EnsurePiece(
        GameObject prefab,
        PrefabRule rule,
        PieceTable hammer,
        Requirement[] resources,
        CraftingStation craftingStation)
    {
        Piece piece = prefab.GetComponent<Piece>();
        if (!piece)
        {
            piece = prefab.AddComponent<Piece>();
        }

        piece.m_enabled = true;
        piece.m_name = string.IsNullOrWhiteSpace(rule.DisplayName) ? prefab.name : rule.DisplayName;
        piece.m_description = string.IsNullOrWhiteSpace(rule.Description) ? "" : rule.Description;
        piece.m_category = PrefabCategoryRegistry.GetOrAdd(hammer, rule.Category);
        piece.m_groundOnly = false;
        piece.m_groundPiece = false;
        piece.m_cultivatedGroundOnly = false;
        piece.m_waterPiece = false;
        piece.m_noInWater = false;
        piece.m_notOnWood = false;
        piece.m_notOnTiltingSurface = false;
        piece.m_inCeilingOnly = false;
        piece.m_notOnFloor = false;
        piece.m_onlyInTeleportArea = false;
        ApplyPrefabPlacementProfile(prefab, piece);
        piece.m_allowedInDungeons = rule.AllowedInDungeons;
        piece.m_clipEverything = rule.ClipEverything;
        piece.m_clipGround = rule.ClipGround;
        piece.m_repairPiece = false;
        piece.m_canBeRemoved = rule.CanBeRemoved;
        piece.m_craftingStation = craftingStation;
        HarnessPrefabsPlacedPiecePatches.RegisterDefaultResources(prefab, piece.m_resources);
        piece.m_resources = resources;
        ApplyPrefabContainerDefaults(prefab);
        PrefabMvbpFixups.Apply(prefab);
        HarnessPrefabsSfxManager.FixPlacementSfx(piece);

        if (!piece.m_icon)
        {
            piece.m_icon = ResolveDefaultIcon();
        }

        PrefabIconRenderer.QueueIcon(piece);

        return piece;
    }

    private static void ApplyPrefabContainerDefaults(GameObject prefab)
    {
        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(prefab ? prefab.name : "");
        if (!MvbpCompatibilityDefaults.TryGetContainerSize(prefabName, out int width, out int height))
        {
            return;
        }

        foreach (Container container in prefab.GetComponentsInChildren<Container>(true))
        {
            if (!container)
            {
                continue;
            }

            container.m_width = width;
            container.m_height = height;
        }
    }

    private static void ApplyPrefabPlacementProfile(GameObject prefab, Piece piece)
    {
        piece.m_allowRotatedOverlap = true;

        string prefabName = HarnessPrefabsRuntime.NormalizePrefabName(prefab ? prefab.name : "");
        if (!prefabName.Equals("Trailership", StringComparison.Ordinal))
        {
            return;
        }

        Piece vikingShipPiece = ZNetScene.instance ? ZNetScene.instance.GetPrefab("VikingShip")?.GetComponent<Piece>() : null;
        piece.m_waterPiece = vikingShipPiece ? vikingShipPiece.m_waterPiece : true;
        piece.m_noClipping = vikingShipPiece ? vikingShipPiece.m_noClipping : true;
        piece.m_allowRotatedOverlap = vikingShipPiece ? vikingShipPiece.m_allowRotatedOverlap : false;
    }

    private static bool TryResolveRequirements(
        string prefabName,
        IEnumerable<PrefabRequirement> requirements,
        out Requirement[] resolvedRequirements)
    {
        if (requirements == null)
        {
            resolvedRequirements = Array.Empty<Requirement>();
            return true;
        }

        List<Requirement> parsed = new();
        foreach (PrefabRequirement requirement in requirements)
        {
            if (requirement == null || string.IsNullOrWhiteSpace(requirement.Item))
            {
                resolvedRequirements = Array.Empty<Requirement>();
                HarnessPrefabsPlugin.Log.LogError($"Prefab '{prefabName}' has an empty build requirement and will not be registered.");
                return false;
            }

            string itemName = requirement.Item.Trim();
            int amount = requirement.Amount;
            if (amount < 1)
            {
                resolvedRequirements = Array.Empty<Requirement>();
                HarnessPrefabsPlugin.Log.LogError($"Prefab '{prefabName}' has an invalid amount for requirement '{itemName}' and will not be registered.");
                return false;
            }

            ItemDrop item = ResolveItemDrop(itemName);
            if (!item)
            {
                resolvedRequirements = Array.Empty<Requirement>();
                HarnessPrefabsPlugin.Log.LogError($"Prefab '{prefabName}' requirement item '{itemName}' could not be resolved. The prefab will not be registered.");
                return false;
            }

            parsed.Add(new Requirement
            {
                m_resItem = item,
                m_amount = amount,
                m_amountPerLevel = amount,
                m_recover = true
            });
        }

        resolvedRequirements = parsed.ToArray();
        return true;
    }

    private static ItemDrop ResolveItemDrop(string itemName)
    {
        GameObject itemPrefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(itemName) : null;
        return itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
    }

    private static bool TryResolveCraftingStation(
        string prefabName,
        string stationName,
        out CraftingStation craftingStation)
    {
        craftingStation = null;
        if (string.IsNullOrWhiteSpace(stationName) || stationName.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string normalizedName = stationName.Trim();
        string internalName;
        try
        {
            internalName = CraftingStations.GetInternalName(normalizedName);
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogError($"Prefab '{prefabName}' crafting station '{normalizedName}' is invalid and the prefab will not be registered: {ex.Message}");
            return false;
        }

        GameObject station = ZNetScene.instance
            ? ZNetScene.instance.GetPrefab(internalName) ?? ZNetScene.instance.GetPrefab(normalizedName)
            : null;
        if (!station && ObjectDB.instance)
        {
            station = ObjectDB.instance.GetItemPrefab(internalName) ?? ObjectDB.instance.GetItemPrefab(normalizedName);
        }

        craftingStation = station ? station.GetComponent<CraftingStation>() : null;
        if (craftingStation)
        {
            return true;
        }

        HarnessPrefabsPlugin.Log.LogError($"Prefab '{prefabName}' crafting station '{normalizedName}' could not be resolved. The prefab will not be registered.");
        return false;
    }

    private static Sprite ResolveDefaultIcon()
    {
        try
        {
            Sprite mvbpDefault = PrefabManager.Cache.GetPrefab<Sprite>("mapicon_hildir1");
            if (mvbpDefault)
            {
                return mvbpDefault;
            }
        }
        catch
        {
        }

        GameObject hammer = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
        ItemDrop itemDrop = hammer ? hammer.GetComponent<ItemDrop>() : null;
        if (itemDrop?.m_itemData?.m_shared?.m_icons != null && itemDrop.m_itemData.m_shared.m_icons.Length > 0)
        {
            return itemDrop.m_itemData.m_shared.m_icons[0];
        }

        return null;
    }

    private static void RemoveAddedPieces(PieceTable hammer)
    {
        if (!hammer || hammer.m_pieces == null || AddedPrefabs.Count == 0)
        {
            AddedPrefabs.Clear();
            AddedAdminPieceNames.Clear();
            return;
        }

        for (int i = hammer.m_pieces.Count - 1; i >= 0; i--)
        {
            GameObject piece = hammer.m_pieces[i];
            if (piece && AddedPrefabs.Contains(piece))
            {
                hammer.m_pieces.RemoveAt(i);
            }
        }

        AddedPrefabs.Clear();
        AddedAdminPieceNames.Clear();
    }

    private static void RemoveAddedPiecesFromTable(PieceTable table)
    {
        if (!table || table.m_pieces == null || AddedPrefabs.Count == 0)
        {
            return;
        }

        for (int i = table.m_pieces.Count - 1; i >= 0; i--)
        {
            GameObject piece = table.m_pieces[i];
            if (piece && AddedPrefabs.Contains(piece))
            {
                table.m_pieces.RemoveAt(i);
            }
        }
    }

    private static void RemoveAdminCategoryTabs(PieceTable hammer)
    {
        if (!hammer || hammer.m_categories == null || hammer.m_categoryLabels == null)
        {
            return;
        }

        for (int i = hammer.m_categoryLabels.Count - 1; i >= 0; i--)
        {
            if (i >= hammer.m_categories.Count)
            {
                hammer.m_categoryLabels.RemoveAt(i);
                continue;
            }

            if (BuildCategories.IsAdminCategory(hammer.m_categoryLabels[i]))
            {
                hammer.m_categoryLabels.RemoveAt(i);
                hammer.m_categories.RemoveAt(i);
            }
        }
    }

    private static HashSet<string> CollectExistingBuildables()
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (PieceTable table in Resources.FindObjectsOfTypeAll<PieceTable>())
        {
            if (!table || table.m_pieces == null)
            {
                continue;
            }

            foreach (GameObject piece in table.m_pieces)
            {
                if (piece && !string.IsNullOrWhiteSpace(piece.name) && !AddedPrefabs.Contains(piece))
                {
                    names.Add(piece.name);
                }
            }
        }

        return names;
    }

    private static IEnumerable<GameObject> CollectScenePrefabs()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (GameObject prefab in ZNetScene.instance.m_prefabs.Concat(ZNetScene.instance.m_nonNetViewPrefabs))
        {
            if (!prefab || string.IsNullOrWhiteSpace(prefab.name) || !seen.Add(prefab.name))
            {
                continue;
            }

            yield return prefab;
        }

        foreach (GameObject prefab in CollectSeededSoftReferencePrefabs(seen))
        {
            yield return prefab;
        }
    }

    private static IEnumerable<GameObject> CollectSeededSoftReferencePrefabs(HashSet<string> seen)
    {
        int loadedCount = 0;
        int missingCount = 0;
        foreach (string prefabName in MvbpPrefabDefaults.Names)
        {
            if (seen.Contains(prefabName))
            {
                continue;
            }

            GameObject prefab = TryResolveSeededSoftReferencePrefab(prefabName);
            if (!prefab || string.IsNullOrWhiteSpace(prefab.name))
            {
                missingCount++;
                continue;
            }

            if (!MvbpPrefabDefaults.HasDefault(prefab.name) || !seen.Add(prefab.name))
            {
                continue;
            }

            loadedCount++;
            yield return prefab;
        }

        if (HarnessPrefabsPlugin.Verbose && (loadedCount > 0 || missingCount > 0))
        {
            HarnessPrefabsPlugin.Log.LogInfo($"Loaded {loadedCount} MVBP-seeded prefabs from SoftRef fallback. missing={missingCount}");
        }
    }

    private static GameObject TryResolveSeededSoftReferencePrefab(string prefabName)
    {
        try
        {
            return PrefabManager.Cache.GetPrefab<GameObject>(prefabName);
        }
        catch (Exception ex)
        {
            if (HarnessPrefabsPlugin.Verbose)
            {
                HarnessPrefabsPlugin.Log.LogWarning($"SoftRef fallback failed for '{prefabName}': {ex.Message}");
            }

            return null;
        }
    }

    private static PieceTable GetHammerPieceTable()
    {
        PieceTable jotunnHammer = PieceManager.Instance.GetPieceTable(PieceTables.Hammer);
        if (jotunnHammer)
        {
            return jotunnHammer;
        }

        GameObject hammer = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
        ItemDrop itemDrop = hammer ? hammer.GetComponent<ItemDrop>() : null;
        return itemDrop?.m_itemData?.m_shared?.m_buildPieces;
    }

    private static bool IsHammerPieceTable(PieceTable table)
    {
        PieceTable hammer = GetHammerPieceTable();
        return table && hammer && ReferenceEquals(table, hammer);
    }

    private static string BuildFullRefreshSignature(PieceTable hammer)
    {
        StringBuilder builder = new();
        builder.Append("source=").Append(HarnessPrefabsPlugin.IsSourceOfTruth).Append(';');
        builder.AppendLine();

        AppendPrefabNames(builder, "znet", ZNetScene.instance.m_prefabs);
        AppendPrefabNames(builder, "nonnv", ZNetScene.instance.m_nonNetViewPrefabs);
        AppendPieceTableMembership(builder, hammer);
        return HarnessPrefabsReferenceState.ComputeStableHash(builder.ToString());
    }

    private static void AppendPrefabNames(StringBuilder builder, string label, IEnumerable<GameObject> prefabs)
    {
        builder.Append('[').Append(label).AppendLine("]");
        foreach (string name in prefabs
                     .Where(prefab => prefab && !string.IsNullOrWhiteSpace(prefab.name))
                     .Select(prefab => prefab.name)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            builder.AppendLine(name);
        }
    }

    private static void AppendPieceTableMembership(StringBuilder builder, PieceTable hammer)
    {
        builder.AppendLine("[pieceTables]");
        foreach (PieceTable table in Resources.FindObjectsOfTypeAll<PieceTable>()
                     .Where(table => table && table.m_pieces != null)
                     .OrderBy(table => ReferenceEquals(table, hammer) ? "Hammer" : table.name, StringComparer.Ordinal))
        {
            builder.Append(ReferenceEquals(table, hammer) ? "Hammer" : table.name).Append('=');
            foreach (string name in table.m_pieces
                         .Where(piece => piece && !string.IsNullOrWhiteSpace(piece.name) && !AddedPrefabs.Contains(piece))
                         .Select(piece => piece.name)
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                builder.Append(name).Append(',');
            }

            builder.AppendLine();
        }
    }

    private static void LogRefreshProfile(
        string reason,
        long totalMs,
        long setupMs,
        long removeMs,
        long existingMs,
        long discoverMs,
        long rulesMs,
        long applyMs,
        int existingBuildableCount,
        int discoveryCount)
    {
        if (!HarnessPrefabsPlugin.Verbose && totalMs < 1000)
        {
            return;
        }

        HarnessPrefabsPlugin.Log.LogInfo(
            $"Refresh profile ({reason}): total={totalMs} ms, setup={setupMs} ms, cleanup={removeMs} ms, existingBuildables={existingMs} ms/{existingBuildableCount}, discover={discoverMs} ms/{discoveryCount}, rules={rulesMs} ms/{PrefabRuleStore.ActiveRuleCount}, apply={applyMs} ms, visible={AddedPrefabs.Count}.");
    }

    private static void LogCachedRefreshProfile(string reason, long totalMs, int discoveryCount)
    {
        if (!HarnessPrefabsPlugin.Verbose && totalMs < 1000)
        {
            return;
        }

        HarnessPrefabsPlugin.Log.LogInfo(
            $"Cached refresh profile ({reason}): total={totalMs} ms, cachedDiscoveries={discoveryCount}, activeRules={PrefabRuleStore.ActiveRuleCount}, visible={AddedPrefabs.Count}.");
    }
}
