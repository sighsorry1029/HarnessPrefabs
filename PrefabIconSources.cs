using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace HarnessPrefabs;

// Read providers which have already indexed this object. Neither lookup requests a render.
internal sealed class PrefabIconSources
{
    private Func<GameObject, Sprite?>? _vnei;
    private Func<GameObject, BepInPlugin?>? _owner;

    internal PrefabIconSources()
    {
        try
        {
            if (Chainloader.PluginInfos.TryGetValue("com.maxsch.valheim.vnei", out var plugin) && plugin.Instance != null)
            {
                var type = plugin.Instance.GetType();
                var get = type.Assembly.GetType("VNEI.Logic.Indexing")?.GetMethod("GetItem",
                    BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
                var target = get?.ReturnType.GetField("prefab");
                var icon = get?.ReturnType.GetMethod("GetIcon", Type.EmptyTypes);
                var placeholder = type.GetField("noIconSprite");
                if (target?.FieldType == typeof(GameObject) && icon?.ReturnType == typeof(Sprite) &&
                    placeholder?.FieldType == typeof(Sprite))
                    _vnei = prefab =>
                    {
                        var record = get!.Invoke(null, new object[] { prefab.name });
                        if (record == null || (GameObject?)target.GetValue(record) != prefab) return null;
                        var sprite = (Sprite?)icon.Invoke(record, null);
                        return sprite == (Sprite?)placeholder.GetValue(plugin.Instance) ? null : sprite;
                    };
            }
        }
        catch (Exception error) { HarnessPrefabsPlugin.Log.LogDebug("VNEI icon API: " + error.GetBaseException().Message); }
        try
        {
            if (Chainloader.PluginInfos.TryGetValue("com.jotunn.jotunn", out var plugin) && plugin.Instance != null)
            {
                var get = plugin.Instance.GetType().Assembly.GetType("Jotunn.Utils.ModQuery")?.GetMethod("GetPrefab",
                    BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
                if (get != null)
                    _owner = prefab =>
                    {
                        var record = get.Invoke(null, new object[] { prefab.name });
                        if (record?.GetType().GetProperty("Prefab")?.GetValue(record) is GameObject target && target == prefab)
                            return record.GetType().GetProperty("SourceMod")?.GetValue(record) as BepInPlugin;
                        return null;
                    };
            }
        }
        catch (Exception error) { HarnessPrefabsPlugin.Log.LogDebug("Jotunn prefab source API: " + error.GetBaseException().Message); }
    }

    internal Sprite? ExistingIcon(Piece piece)
    {
        var icons = piece.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_icons;
        if (icons is { Length: > 0 } && IsSpecificIcon(icons[0])) return icons[0];
        try
        {
            var sprite = _vnei?.Invoke(piece.gameObject);
            // VNEI can forward the same shared Piece sprite that the Hammer snapshot is meant to replace.
            return sprite != piece.m_icon && IsSpecificIcon(sprite) ? sprite : null;
        }
        catch (Exception error)
        {
            _vnei = null;
            HarnessPrefabsPlugin.Log.LogDebug("VNEI icon lookup: " + error.GetBaseException().Message);
            return null;
        }
    }

    private static bool IsSpecificIcon(Sprite? sprite)
    {
        if (sprite == null || sprite.name == "mapicon_hildir1") return false;
        var hammerIcons = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_icons;
        return hammerIcons is not { Length: > 0 } || sprite != hammerIcons[0];
    }

    internal BepInPlugin? GetOwner(GameObject prefab)
    {
        try
        {
            return _owner?.Invoke(prefab);
        }
        catch (Exception error)
        {
            _owner = null;
            HarnessPrefabsPlugin.Log.LogDebug("Jotunn prefab source: " + error.GetBaseException().Message);
            return null;
        }
    }
}
