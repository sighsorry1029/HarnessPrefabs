#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarnessPrefabs;

internal sealed class PrefabIconRenderer : MonoBehaviour
{
    // Select the byte[] API explicitly: Unity 6 also exposes Span overloads which
    // cannot be bound by the net48 compiler's framework reference assemblies.
    private static readonly Func<Texture2D, byte[], bool> LoadImage =
        (Func<Texture2D, byte[], bool>)Delegate.CreateDelegate(typeof(Func<Texture2D, byte[], bool>),
            typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }));
    private static readonly HashSet<string> DoNotCache = new(StringComparer.Ordinal)
    {
        "portal",
        "dvergrprops_wood_floor",
        "dvergrprops_wood_stair"
    };

    private static PrefabIconRenderer _instance;
    private readonly Dictionary<string, Sprite> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<Piece, Sprite> _previousIcons = new();
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
                Sprite sprite = TryRender(prefab);
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
                processed++;
                if (processed % 4 == 0 || Time.realtimeSinceStartup - batchStart >= 0.008f)
                {
                    yield return null;
                    batchStart = Time.realtimeSinceStartup;
                }
            }
        }
        finally
        {
            _queued.Clear();
            _queuedPrefabNames.Clear();
            _processing = false;
            ClearCamera();
        }
    }

    private Sprite TryRender(GameObject prefab)
    {
        if (!prefab || IsEffectLike(prefab))
        {
            return null;
        }

        try
        {
            Sprite sprite = RenderCached(prefab);
            if (!sprite)
            {
                sprite = TryRenderPickableItemFallback(prefab);
            }

            return sprite;
        }
        catch (Exception ex)
        {
            HarnessPrefabsPlugin.Log.LogDebug($"Icon render failed for '{prefab.name}': {ex}");
            return null;
        }
    }

    private Sprite TryRenderPickableItemFallback(GameObject prefab)
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

        return RenderCached(itemObject);
    }

    private void Assign(Piece piece, Sprite sprite)
    {
        if (!_previousIcons.ContainsKey(piece)) _previousIcons.Add(piece, piece.m_icon);
        piece.m_icon = sprite;
    }

    private Sprite RenderCached(GameObject prefab)
    {
        // A new renderer revision skips the old transparent PNGs without deleting them.
        string path = Path.Combine(Paths.CachePath, "HarnessPrefabs", "valheim-" + global::Version.GetVersionString() + "-icons-r2",
            prefab.name.GetStableHashCode().ToString("X8") + ".png");
        bool cache = !DoNotCache.Contains(prefab.name);
        if (cache && File.Exists(path))
        {
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (LoadImage(texture, File.ReadAllBytes(path)) && HasVisiblePixels(texture.GetPixels32()))
                    return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
            }
            catch (Exception ex) { HarnessPrefabsPlugin.Log.LogDebug($"Icon cache read failed: {ex.Message}"); }
            Destroy(texture);
        }
        Sprite sprite = RenderPrefab(prefab);
        if (sprite && cache)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(sprite.texture));
            }
            catch (Exception ex) { HarnessPrefabsPlugin.Log.LogDebug($"Icon cache write failed: {ex.Message}"); }
        }
        return sprite;
    }

    private Sprite RenderPrefab(GameObject prefab)
    {
        EnsureCamera();
        GameObject container = new("HarnessPrefabs_IconClone");
        container.SetActive(false);
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = null;
        Texture2D texture = null;
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

            target = RenderTexture.GetTemporary(128, 128);
            _camera.targetTexture = target;
            RenderTexture.active = target;
            clone.SetActive(true);
            container.SetActive(true);
            _camera.Render();
            container.SetActive(false);
            texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            texture.Apply();
            if (!HasVisiblePixels(texture.GetPixels32()))
            {
                HarnessPrefabsPlugin.Log.LogWarning($"Icon render for '{prefab.name}' was fully transparent; keeping the fallback icon.");
                return null;
            }
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 128, 128), Vector2.one * 0.5f);
            texture = null; // The returned sprite owns this texture until Release.
            return sprite;
        }
        finally
        {
            container.SetActive(false);
            DestroyImmediate(container);
            if (texture) Destroy(texture);
            _camera.targetTexture = null;
            RenderTexture.active = previous;
            if (target) RenderTexture.ReleaseTemporary(target);
        }
    }

    private static bool HasVisiblePixels(Color32[] pixels)
    {
        foreach (Color32 pixel in pixels)
            if (pixel.a != 0) return true;
        return false;
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
        HashSet<Sprite> owned = new(_icons.Values);
        foreach (var entry in _previousIcons)
            if (entry.Key && owned.Contains(entry.Key.m_icon)) entry.Key.m_icon = entry.Value;
        foreach (Sprite sprite in owned)
        {
            if (!sprite) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
        _icons.Clear();
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
