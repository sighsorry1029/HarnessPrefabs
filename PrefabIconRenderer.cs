#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarnessPrefabs;

internal sealed class PrefabIconRenderer : MonoBehaviour
{
    private static readonly HashSet<string> DoNotCache = new(StringComparer.Ordinal)
    {
        "portal",
        "dvergrprops_wood_floor",
        "dvergrprops_wood_stair"
    };

    private static PrefabIconRenderer _instance;
    private readonly Dictionary<string, Sprite> _icons = new(StringComparer.Ordinal);
    private readonly HashSet<Sprite> _ownedIcons = new();
    private readonly Dictionary<Piece, (Sprite Original, Sprite Assigned)> _previousIcons = new();
    private PrefabIconSources _sources;
    private IconSnapshot _refit;
    private Camera _camera;
    private Light _light;

    private readonly Queue<Piece> _queue = new();
    private readonly HashSet<int> _queued = new();
    private readonly HashSet<string> _queuedPrefabNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failedPrefabNames = new(StringComparer.Ordinal);
    private bool _processing;

    public static void QueueIcon(Piece piece)
    {
        if (!piece || Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return;
        }

        // These are HarnessPrefabs' added buildables; their original Piece sprites may
        // be shared placeholders. Replace them only after a successful prefab snapshot.
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
        if (_icons.TryGetValue(prefabName, out Sprite cached) && cached)
        {
            Assign(piece, cached);
            return;
        }

        if (_failedPrefabNames.Contains(prefabName)) return;

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
        try
        {
            yield return new WaitForEndOfFrame();
            int processed = 0;
            float batchStart = Time.realtimeSinceStartup;
            while (_queue.Count > 0)
            {
                Piece piece = _queue.Dequeue();
                if (!piece) continue;
                GameObject prefab = piece.gameObject;
                _sources ??= new PrefabIconSources();
                Sprite sprite = null;
                var model = prefab;
                bool first = true, triedFallback = false;
                while (model)
                {
                    if (processed >= 4 || processed > 0 && Time.realtimeSinceStartup - batchStart >= 0.008f)
                    {
                        yield return null;
                        processed = 0;
                        batchStart = Time.realtimeSinceStartup;
                        if (!piece || !model) { CancelRefit(); break; }
                    }
                    processed++;
                    if (first)
                    {
                        first = false;
                        sprite = _sources.ExistingIcon(piece);
                        if (sprite || IsEffectLike(prefab)) break;
                    }
                    sprite = _refit != null ? Advance(_refit) : TryRender(model);
                    if (sprite) { _ownedIcons.Add(sprite); break; }
                    if (_refit != null) continue;
                    if (triedFallback) break;
                    triedFallback = true;
                    model = GetPickableFallback(prefab);
                }
                if (!piece) continue;
                if (sprite)
                {
                    _icons[prefab.name] = sprite;
                    Assign(piece, sprite);
                }
                else
                {
                    // Keep the placeholder and avoid retrying on every Hammer refresh.
                    _failedPrefabNames.Add(prefab.name);
                }
            }
        }
        finally
        {
            CancelRefit();
            _queued.Clear();
            _queuedPrefabNames.Clear();
            _processing = false;
            ClearCamera();
        }
    }

    private Sprite TryRender(GameObject prefab)
    {
        try
        {
            return RenderCached(prefab);
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogDebug($"Icon render failed for '{prefab.name}': {ex}");
            return null;
        }
    }

    private static GameObject GetPickableFallback(GameObject prefab)
    {
        PickableItem.RandomItem[] randomItems = prefab.GetComponent<PickableItem>()?.m_randomItemPrefabs;
        if (randomItems == null || randomItems.Length == 0)
        {
            return null;
        }

        ItemDrop itemPrefab = randomItems[0].m_itemPrefab;
        return itemPrefab ? itemPrefab.gameObject : null;
    }

    private void Assign(Piece piece, Sprite sprite)
    {
        var original = _previousIcons.TryGetValue(piece, out var entry) ? entry.Original : piece.m_icon;
        _previousIcons[piece] = (original, sprite);
        piece.m_icon = sprite;
    }

    private Sprite RenderCached(GameObject prefab)
    {
        string path = null;
        if (!DoNotCache.Contains(prefab.name))
        {
            try
            {
                path = PrefabIconPng.PathFor(_sources.GetOwner(prefab), prefab.name, "Hammer");
                var cached = PrefabIconPng.Read(path);
                if (cached != null) return cached;
            }
            catch (Exception ex) { HarnessPrefabsPlugin.Log.LogDebug("Icon cache path: " + ex.GetBaseException().Message); }
        }
        var job = PrepareCapture(prefab, path);
        return job != null ? Advance(job) : null;
    }

    private Sprite Advance(IconSnapshot job)
    {
        _refit = null;
        try
        {
            // Other icon renderers also use layer 31. Light only our own capture.
            _light.enabled = true;
            if (!job.Step(out var sprite)) _refit = job;
            return sprite;
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogDebug("Icon capture: " + ex.GetBaseException().Message);
            return null;
        }
        finally
        {
            _light.enabled = false;
            if (_refit == null) job.Dispose();
        }
    }

    private void CancelRefit()
    {
        _refit?.Dispose();
        _refit = null;
    }

    private IconSnapshot PrepareCapture(GameObject prefab, string path)
    {
        EnsureCamera();
        GameObject container = new("HarnessPrefabs_IconClone");
        container.SetActive(false);
        IconSnapshot job = null;
        try
        {
            // The inactive parent prevents Awake/OnEnable on the clone. All gameplay components
            // must be removed successfully before activation, including ZNetView and ItemDrop.
            GameObject clone = Instantiate(prefab, container.transform);
            if (!StripNonVisualComponents(clone)) return null;
            clone.SetActive(true); // Still inactive in hierarchy under the snapshot container.
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.Euler(23f, 51f, 25.8f);
            foreach (Transform child in clone.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) ||
                    !IsActiveWithinClone(renderer.transform, clone.transform)) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds || bounds.size.sqrMagnitude < 0.000001f) return null;
            clone.transform.position -= bounds.center;
            // Fit even the nearest corners inside the square perspective image.
            float distance = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.1f /
                Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) + bounds.extents.z + 1f;
            _camera.transform.position = new Vector3(0, 0, distance);
            _camera.farClipPlane = distance + bounds.extents.z + 1f;
            foreach (ParticleSystem particles in clone.GetComponentsInChildren<ParticleSystem>(true))
                particles.Simulate(5f, withChildren: false, restart: true);

            return job = new IconSnapshot(prefab, container, _camera, path);
        }
        finally
        {
            if (job == null) DestroyImmediate(container);
        }
    }

    private static bool StripNonVisualComponents(GameObject clone)
    {
        foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
        {
            List<Component> pending = child.GetComponents<Component>().Where(c => c && !IsVisual(c)).ToList();
            while (pending.Count > 0)
            {
                bool removed = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Component candidate = pending[i];
                    if (pending.Any(other => other != candidate && Requires(other.GetType(), candidate.GetType()))) continue;
                    DestroyImmediate(candidate);
                    if (candidate) return false;
                    pending.RemoveAt(i);
                    removed = true;
                }
                if (!removed) return false;
            }
        }
        return clone.GetComponentsInChildren<MonoBehaviour>(true).Length == 0;
    }

    private static bool IsActiveWithinClone(Transform child, Transform root)
    {
        for (Transform current = child; current && current != root; current = current.parent)
            if (!current.gameObject.activeSelf) return false;
        return true;
    }

    private static bool IsVisual(Component c) => c is Transform || c is MeshFilter || c is MeshRenderer ||
        c is SkinnedMeshRenderer || c is ParticleSystem || c is ParticleSystemRenderer;

    private static readonly Dictionary<Type, RequireComponent[]> ComponentRequirements = new();

    private static bool Requires(Type dependent, Type required)
    {
        if (!ComponentRequirements.TryGetValue(dependent, out RequireComponent[] requirements))
        {
            requirements = dependent.GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>().ToArray();
            ComponentRequirements.Add(dependent, requirements);
        }
        foreach (RequireComponent requirement in requirements)
            if (requirement.m_Type0?.IsAssignableFrom(required) == true ||
                requirement.m_Type1?.IsAssignableFrom(required) == true ||
                requirement.m_Type2?.IsAssignableFrom(required) == true) return true;
        return false;
    }

    private void EnsureCamera()
    {
        if (_camera) return;
        _camera = new GameObject("HarnessPrefabs_IconCamera").AddComponent<Camera>();
        _camera.transform.SetParent(transform);
        _camera.enabled = false;
        // Valheim shaders require perspective; match the previous Jotunn renderer.
        _camera.orthographic = false;
        _camera.fieldOfView = 0.5f;
        _camera.aspect = 1f;
        _camera.nearClipPlane = 0.01f;
        _camera.backgroundColor = Color.clear;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.cullingMask = 1 << 31;
        _camera.transform.rotation = Quaternion.Euler(0, 180, 0);
        _light = new GameObject("HarnessPrefabs_IconLight").AddComponent<Light>();
        _light.enabled = false;
        _light.transform.SetParent(transform);
        _light.transform.rotation = Quaternion.Euler(5, 180, 5);
        _light.type = LightType.Directional;
        _light.cullingMask = 1 << 31;
    }

    private void ClearCamera()
    {
        if (_camera) Destroy(_camera.gameObject);
        if (_light) Destroy(_light.gameObject);
        _camera = null;
        _light = null;
    }

    public static void Release()
    {
        if (_instance)
        {
            _instance.Cleanup();
            Destroy(_instance.gameObject);
        }
        _instance = null;
    }

    private void OnDestroy()
    {
        Cleanup();
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private void Cleanup()
    {
        StopAllCoroutines();
        CancelRefit();
        foreach (var entry in _previousIcons)
            if (entry.Key && entry.Key.m_icon == entry.Value.Assigned) entry.Key.m_icon = entry.Value.Original;
        foreach (Sprite sprite in _ownedIcons)
        {
            if (!sprite) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
        _icons.Clear();
        _ownedIcons.Clear();
        _previousIcons.Clear();
        _failedPrefabNames.Clear();
        ClearCamera();
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
