#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarnessPrefabs;

internal sealed class PrefabIconRenderer : MonoBehaviour
{
    private static readonly HashSet<string> DoNotUseJotunnCache = new(StringComparer.Ordinal)
    {
        "portal",
        "dvergrprops_wood_floor",
        "dvergrprops_wood_stair"
    };

    private static PrefabIconRenderer _instance;
    private static readonly HashSet<string> RenderedPrefabNames = new(StringComparer.Ordinal);

    private readonly Queue<Piece> _queue = new();
    private readonly HashSet<int> _queued = new();
    private readonly HashSet<string> _queuedPrefabNames = new(StringComparer.Ordinal);
    private bool _processing;

    public static void QueueIcon(Piece piece)
    {
        if (!piece || Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return;
        }

        EnsureInstance().Enqueue(piece);
    }

    private static PrefabIconRenderer EnsureInstance()
    {
        if (_instance)
        {
            return _instance;
        }

        GameObject root = new("HarnessPrefabs_IconRenderer");
        DontDestroyOnLoad(root);
        _instance = root.AddComponent<PrefabIconRenderer>();
        return _instance;
    }

    private void Enqueue(Piece piece)
    {
        string prefabName = piece.gameObject ? piece.gameObject.name : "";
        if (prefabName.Length > 0 && RenderedPrefabNames.Contains(prefabName) && piece.m_icon)
        {
            return;
        }

        if (prefabName.Length > 0 && !_queuedPrefabNames.Add(prefabName))
        {
            return;
        }

        int id = piece.GetInstanceID();
        if (!_queued.Add(id))
        {
            return;
        }

        _queue.Enqueue(piece);
        if (!_processing)
        {
            StartCoroutine(ProcessQueue());
        }
    }

    private IEnumerator ProcessQueue()
    {
        _processing = true;
        yield return new WaitForEndOfFrame();

        int rendered = 0;
        int failed = 0;
        while (_queue.Count > 0)
        {
            Piece piece = _queue.Dequeue();
            if (!piece)
            {
                continue;
            }

            GameObject prefab = piece.gameObject;
            Sprite sprite = TryRenderWithJotunn(prefab);

            if (sprite)
            {
                piece.m_icon = sprite;
                if (prefab.name.Length > 0)
                {
                    RenderedPrefabNames.Add(prefab.name);
                }

                rendered++;
            }
            else
            {
                failed++;
            }

            if ((rendered + failed) % 4 == 0)
            {
                yield return null;
            }
        }

        _queued.Clear();
        _queuedPrefabNames.Clear();
        _processing = false;
        if (HarnessPrefabsPlugin.Verbose && (rendered > 0 || failed > 0))
        {
            HarnessPrefabsPlugin.Log.LogInfo($"Icon render queue complete: rendered={rendered}, failed={failed}.");
        }
    }

    private static Sprite TryRenderWithJotunn(GameObject prefab)
    {
        if (!prefab || IsEffectLike(prefab))
        {
            return null;
        }

        try
        {
            RenderManager.RenderRequest request = new(prefab)
            {
                Rotation = RenderManager.IsometricRotation,
                UseCache = !DoNotUseJotunnCache.Contains(prefab.name)
            };
            Sprite sprite = RenderManager.Instance.Render(request);
            if (!sprite)
            {
                sprite = TryRenderPickableItemFallback(prefab);
            }

            return sprite;
        }
        catch (Exception ex)
        {
            if (HarnessPrefabsPlugin.Verbose)
            {
                HarnessPrefabsPlugin.Log.LogWarning($"Jotunn icon render failed for {prefab.name}: {ex.Message}");
            }

            return null;
        }
    }

    private static Sprite TryRenderPickableItemFallback(GameObject prefab)
    {
        PickableItem.RandomItem[] randomItems = prefab.GetComponent<PickableItem>()?.m_randomItemPrefabs;
        if (randomItems == null || randomItems.Length == 0)
        {
            return null;
        }

        ItemDrop itemPrefab = randomItems[0].m_itemPrefab;
        GameObject itemObject = itemPrefab ? itemPrefab.gameObject : null;
        if (!itemObject)
        {
            return null;
        }

        RenderManager.RenderRequest request = new(itemObject)
        {
            Rotation = RenderManager.IsometricRotation,
            UseCache = true
        };
        return RenderManager.Instance.Render(request);
    }

    private static bool IsEffectLike(GameObject prefab)
    {
        return prefab &&
               (prefab.name.StartsWith("fx_", StringComparison.Ordinal) ||
                prefab.name.StartsWith("vfx_", StringComparison.Ordinal) ||
                prefab.name.StartsWith("sfx_", StringComparison.Ordinal) ||
               prefab.GetComponent<ParticleSystem>());
    }
}
