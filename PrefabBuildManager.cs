#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Requirement = Piece.Requirement;

namespace HarnessPrefabs;

internal static class PrefabBuildManager
{
    private static readonly HashSet<GameObject> AddedPrefabs = new();
    private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabs = AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");
    private static readonly Action<Player> UpdateAvailablePieces = AccessTools.MethodDelegate<Action<Player>>(AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList"));
    private static bool _refreshing;
    private static bool? _lastHarnessHammerTabsEnabled;
    private static bool _hasCompletedFullRefresh;
    private static string _lastFullRefreshSignature = "";
    private static ZNetScene _materializationScene;

    public static void BeginPrefabEpoch(ZNetScene scene)
    {
        if (!scene || ReferenceEquals(_materializationScene, scene))
        {
            return;
        }

        RemoveAddedPieces(ObjectDB.instance ? GetHammerPieceTable() : null);
        PrefabPlacementPatchRegistry.Clear();
        HarnessPrefabsPlacedPiecePatches.ResetMaterializedPrefabNames();
        _lastHarnessHammerTabsEnabled = null;
        _hasCompletedFullRefresh = false;
        _lastFullRefreshSignature = "";
        _materializationScene = scene;
    }

    public static void EndPrefabEpoch(ZNetScene scene)
    {
        if (!ReferenceEquals(_materializationScene, scene)) return;
        RemoveAddedPieces(ObjectDB.instance ? GetHammerPieceTable() : null);
        PrefabCategoryRegistry.Clear();
        PrefabPlacementPatchRegistry.Clear();
        PrefabIconRenderer.Release();
        PrefabAssetResolver.Release();
        HarnessPrefabsSfxManager.Reset();
        _materializationScene = null;
        _hasCompletedFullRefresh = false;
        _lastHarnessHammerTabsEnabled = null;
    }

    public static void RefreshIfHarnessHammerVisibilityChanged()
    {
        bool harnessEnabled = HarnessPrefabsPlugin.HarnessHammerTabsEnabled;
        if (_lastHarnessHammerTabsEnabled.HasValue &&
            _lastHarnessHammerTabsEnabled.Value == harnessEnabled)
        {
            return;
        }

        RefreshFromCachedRules("harness hammer visibility changed");
    }

    public static void Refresh(string reason)
    {
        if (_refreshing)
        {
            return;
        }

        if (!ZNetScene.instance || !ObjectDB.instance)
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
        long afterExisting = 0;
        long afterDiscover = 0;
        long afterRules = 0;
        int existingBuildableCount = 0;
        int discoveryCount = 0;
        try
        {
            HarnessPrefabsPlugin.EnsureSourceOfTruthFileMode();
            afterSetup = total.ElapsedMilliseconds;
            HashSet<string> existingBuildables = CollectExistingBuildables();
            existingBuildableCount = existingBuildables.Count;
            afterExisting = total.ElapsedMilliseconds;
            List<PrefabDiscovery> discoveries = PrefabClassifier.Discover(CollectScenePrefabs(), existingBuildables);
            discoveryCount = discoveries.Count;
            afterDiscover = total.ElapsedMilliseconds;
            PrefabRuleStore.LoadRulesForCurrentAuthority(discoveries);
            afterRules = total.ElapsedMilliseconds;
            ApplyActiveRulesToHammer(hammer, discoveries);
            _lastFullRefreshSignature = BuildFullRefreshSignature(hammer);
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
                afterExisting - afterSetup,
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
        if (_refreshing)
        {
            return;
        }

        if (!_hasCompletedFullRefresh || !PrefabRuleStore.HasCachedDiscoveries)
        {
            Refresh($"{reason}; full discovery fallback");
            return;
        }

        if (!ZNetScene.instance || !ObjectDB.instance)
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
            ApplyActiveRulesToHammer(hammer, PrefabRuleStore.LastDiscoveries);
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
            return;
        }


        if (knownRecipes == null)
        {
            return;
        }

    }

    private static void ApplyActiveRulesToHammer(PieceTable hammer, IReadOnlyCollection<PrefabDiscovery> discoveries)
    {
        _lastHarnessHammerTabsEnabled = HarnessPrefabsPlugin.HarnessHammerTabsEnabled;
        RemoveAddedPieces(hammer);
        PrefabCategoryRegistry.Clear();
        PrefabPlacementPatchRegistry.Clear();

        foreach (PrefabDiscovery discovery in PrefabSortPolicy.SortForHammer(discoveries))
        {
            if (!PrefabRuleStore.TryGetRule(discovery.Name, out PrefabRule rule))
            {
                continue;
            }

            if (rule.Access == PrefabAccess.Hidden)
            {
                continue;
            }

            PrefabPlacementPatchRegistry.Set(
                discovery.Name,
                MvbpPrefabDefaults.NeedsPlacementPatch(discovery.Name),
                MvbpCompatibilityDefaults.GetPlacementOffset(discovery.Name));

            if (!discovery.Prefab)
            {
                continue;
            }

            try
            {
                if (TryAddPrefabToHammer(discovery.Prefab, rule, hammer))
                {
                    AddedPrefabs.Add(discovery.Prefab);
                }
            }
            catch (Exception ex)
            {
                HarnessPrefabsPlugin.Log.LogError(
                    $"Prefab '{discovery.Name}' materialization failed and will be skipped: {ex}");
            }
        }


        if (Player.m_localPlayer)
        {
            UpdateAvailablePieces(Player.m_localPlayer);
            PrefabCategoryRegistry.RefreshUi();
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

        if (!RegisterScenePrefab(prefab)) return false;
        Piece piece = EnsurePiece(prefab, rule, hammer, resources, craftingStation);
        if (!piece)
        {
            return false;
        }

        // Materialize on every peer, including headless servers. Admin pieces stay
        // outside native lists and are selected only through HarnessPieceList.
        if (rule.Access == PrefabAccess.Admin)
        {
            if (HarnessPrefabsPlugin.HarnessHammerTabsEnabled) PrefabIconRenderer.QueueIcon(piece);
            return false;
        }
        hammer.m_pieces.Add(prefab);
        PrefabIconRenderer.QueueIcon(piece);

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
            piece.m_canBeRemoved = false;
        }

        HarnessPrefabsPlacedPiecePatches.RegisterMaterializedPrefab(prefab, piece.m_resources);

        piece.m_enabled = true;
        piece.m_name = string.IsNullOrWhiteSpace(rule.DisplayName) ? prefab.name : rule.DisplayName;
        piece.m_description = string.IsNullOrWhiteSpace(rule.Description) ? "" : rule.Description;
        PrefabCategoryRegistry.Assign(piece, rule.Category);
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
        piece.m_allowRotatedOverlap = true;
        piece.m_allowedInDungeons = rule.AllowedInDungeons;
        piece.m_clipEverything = rule.ClipEverything;
        piece.m_clipGround = rule.ClipGround;
        piece.m_repairPiece = false;
        piece.m_craftingStation = craftingStation;
        piece.m_resources = resources;
        PrefabMvbpFixups.Apply(prefab);
        HarnessPrefabsSfxManager.FixPlacementSfx(piece);

        if (!piece.m_icon)
        {
            piece.m_icon = ResolveDefaultIcon();
        }

        return piece;
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
        string internalName = normalizedName switch
        {
            "Workbench" => "piece_workbench",
            "Forge" => "forge",
            "Stonecutter" => "piece_stonecutter",
            "Cauldron" => "piece_cauldron",
            "ArtisanTable" => "piece_artisanstation",
            "BlackForge" => "blackforge",
            "GaldrTable" => "piece_magetable",
            "MeadKetill" => "piece_MeadCauldron",
            "FoodPreparationTable" => "piece_preptable",
            _ => normalizedName
        };

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

    internal static Sprite ResolveDefaultIcon()
    {
        try
        {
            Sprite mvbpDefault = PrefabAssetResolver.GetFallbackIcon();
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
        RemoveAddedPiecesFromTable(hammer);
        AddedPrefabs.Clear();
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
        foreach (string prefabName in MvbpPrefabDefaults.Names)
        {
            if (seen.Contains(prefabName))
            {
                continue;
            }

            GameObject prefab = TryResolveSeededSoftReferencePrefab(prefabName);
            if (!prefab || string.IsNullOrWhiteSpace(prefab.name))
            {
                continue;
            }

            if (!MvbpPrefabDefaults.HasDefault(prefab.name) || !seen.Add(prefab.name))
            {
                continue;
            }

            yield return prefab;
        }
    }

    private static GameObject TryResolveSeededSoftReferencePrefab(string prefabName)
    {
        try
        {
            GameObject prefab = PrefabAssetResolver.Find(prefabName);
            return prefab && RegisterScenePrefab(prefab) ? prefab : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool RegisterScenePrefab(GameObject prefab)
    {
        ZNetScene scene = ZNetScene.instance;
        if (!scene || !prefab) return false;
        int hash = prefab.name.GetStableHashCode();
        if (NamedPrefabs(scene).TryGetValue(hash, out GameObject existing))
        {
            if (ReferenceEquals(existing, prefab)) return true;
            HarnessPrefabsPlugin.Log.LogError($"Prefab '{prefab.name}' conflicts with registered '{existing?.name}'; registration skipped.");
            return false;
        }
        List<GameObject> list = prefab.GetComponent<ZNetView>() ? scene.m_prefabs : scene.m_nonNetViewPrefabs;
        if (!list.Contains(prefab)) list.Add(prefab);
        NamedPrefabs(scene).Add(hash, prefab);
        return true;
    }

    private static PieceTable GetHammerPieceTable()
    {
        GameObject hammer = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
        ItemDrop itemDrop = hammer ? hammer.GetComponent<ItemDrop>() : null;
        return itemDrop?.m_itemData?.m_shared?.m_buildPieces;
    }

    internal static bool IsHammerPieceTable(PieceTable table)
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
        return builder.ToString();
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
        long existingMs,
        long discoverMs,
        long rulesMs,
        long applyMs,
        int existingBuildableCount,
        int discoveryCount)
    {
        if (totalMs < 1000)
        {
            return;
        }

        HarnessPrefabsPlugin.Log.LogInfo(
            $"Refresh profile ({reason}): total={totalMs} ms, setup={setupMs} ms, existingBuildables={existingMs} ms/{existingBuildableCount}, discover={discoverMs} ms/{discoveryCount}, rules={rulesMs} ms/{PrefabRuleStore.ActiveRuleCount}, apply={applyMs} ms, visible={AddedPrefabs.Count}.");
    }

    private static void LogCachedRefreshProfile(string reason, long totalMs, int discoveryCount)
    {
        if (totalMs < 1000)
        {
            return;
        }

        HarnessPrefabsPlugin.Log.LogInfo(
            $"Cached refresh profile ({reason}): total={totalMs} ms, cachedDiscoveries={discoveryCount}, activeRules={PrefabRuleStore.ActiveRuleCount}, visible={AddedPrefabs.Count}.");
    }
}
