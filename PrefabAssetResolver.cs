#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;

namespace HarnessPrefabs;

// Owns only the vanilla soft references acquired by this mod, for the current prefab epoch.
internal static class PrefabAssetResolver
{
    private static readonly FieldInfo Loader = AccessTools.Field(typeof(Runtime), "s_assetLoader");
    private static readonly Type LoaderType = typeof(Runtime).Assembly.GetType("SoftReferenceableAssets.AssetBundleLoader", true);
    private static readonly FieldInfo AssetLoaders = AccessTools.Field(LoaderType, "m_assetLoaders");
    private static readonly Type AssetLoaderType = typeof(Runtime).Assembly.GetType("SoftReferenceableAssets.AssetLoader", true);
    private static readonly FieldInfo AssetPath = AccessTools.Field(AssetLoaderType, "m_assetPathInBundle");
    private static readonly FieldInfo AssetId = AccessTools.Field(AssetLoaderType, "m_assetID");
    private static Dictionary<string, AssetID> _prefabIds;
    private static readonly Dictionary<string, SoftReference<GameObject>> Held = new(StringComparer.Ordinal);
    private static Sprite _fallbackIcon;
    private static bool _iconSearched;

    public static void Prepare()
    {
        if (Loader.GetValue(null) == null) Runtime.MakeAllAssetsLoadable();
    }

    public static GameObject Find(string name)
    {
        GameObject scenePrefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(name) : null;
        if (scenePrefab) return scenePrefab;
        if (Held.TryGetValue(name, out SoftReference<GameObject> held)) return held.Asset;
        EnsureIndex();
        if (!_prefabIds.TryGetValue(name, out AssetID id)) return null;
        SoftReference<GameObject> reference = new(id);
        if (!reference.IsValid) return null;
        bool keep = false;
        try
        {
            if (reference.Load() != LoadResult.Succeeded || !reference.Asset) return null;
            Held.Add(name, reference);
            keep = true;
            return reference.Asset;
        }
        finally
        {
            if (!keep) reference.Release();
        }
    }

    private static void EnsureIndex()
    {
        if (_prefabIds != null) return;
        IEnumerable<KeyValuePair<string, AssetID>> entries;
        try { entries = Runtime.GetAllAssetPathsInBundleMappedToAssetID(); }
        catch (ArgumentException)
        {
            // The public dictionary builder throws on duplicate paths. Read its original
            // metadata once without patching the global dictionary insertion policy.
            entries = ReadDuplicatePaths();
        }
        Dictionary<string, AssetID> ids = new(StringComparer.Ordinal);
        Dictionary<string, string> chosen = new(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.Key.IndexOf("/DeepNorth/", StringComparison.OrdinalIgnoreCase) >= 0)
                     .ThenBy(e => e.Key.StartsWith("Assets/world/Locations", StringComparison.OrdinalIgnoreCase))
                     .ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(entry.Key);
            if (ids.ContainsKey(name))
            {
                if (MvbpPrefabDefaults.HasDefault(name) && chosen[name] != entry.Key)
                    HarnessPrefabsPlugin.Log.LogInfo($"Seed '{name}' uses '{chosen[name]}' (also found '{entry.Key}').");
                continue;
            }
            ids.Add(name, entry.Value);
            chosen.Add(name, entry.Key);
        }
        _prefabIds = ids;
    }

    private static IEnumerable<KeyValuePair<string, AssetID>> ReadDuplicatePaths()
    {
        foreach (object asset in (IEnumerable)AssetLoaders.GetValue(Loader.GetValue(null)))
            yield return new KeyValuePair<string, AssetID>((string)AssetPath.GetValue(asset), (AssetID)AssetId.GetValue(asset));
    }

    public static Sprite GetFallbackIcon()
    {
        if (!_iconSearched)
        {
            _iconSearched = true;
            _fallbackIcon = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s && s.name == "mapicon_hildir1");
        }
        return _fallbackIcon;
    }

    public static void Release()
    {
        foreach (SoftReference<GameObject> reference in Held.Values) reference.Release();
        Held.Clear();
        _prefabIds = null;
        _fallbackIcon = null;
        _iconSearched = false;
    }
}
