using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HarnessPrefabs;

// A pending second pass owns the first texture and an inactive stage, never a render target.
internal sealed class IconSnapshot : IDisposable
{
    private const int Size = 128;
    private readonly GameObject _stage;
    private readonly Camera _camera;
    private readonly string? _path;
    private Texture2D? _first;
    private Footprint _original;
    internal GameObject Prefab { get; }

    internal IconSnapshot(GameObject prefab, GameObject stage, Camera camera, string? path)
    {
        Prefab = prefab;
        _stage = stage;
        _camera = camera;
        _path = path;
        stage.SetActive(false);
    }

    // False requests exactly one more GPU pass through the renderer's existing queue.
    internal bool Step(out Sprite? sprite)
    {
        sprite = null;
        Texture2D? image = null;
        try
        {
            bool refining = _first != null;
            try
            {
                image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                ReadFrame(image, refining);
                var footprint = Measure(image.GetPixels32());
                if (!refining && footprint.Peak == 0)
                {
                    HarnessPrefabsPlugin.Log.LogWarning($"Icon render for '{Prefab.name}' was fully transparent; keeping the fallback icon.");
                    return true;
                }
                if (!refining || _original.Accepts(footprint))
                {
                    if (_first != null) Object.Destroy(_first);
                    _first = image;
                    image = null;
                }
                if (!refining && footprint.NeedsCorrection)
                {
                    _original = footprint;
                    return false;
                }
            }
            catch (Exception error) when (refining)
            {
                HarnessPrefabsPlugin.Log.LogDebug("Keeping first snapshot of " + Prefab.name + ": " + error.Message);
            }

            if (_path != null) PrefabIconPng.Write(_path, _first!);
            _first!.Apply(false, false);
            sprite = Sprite.Create(_first, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f));
            if (sprite != null) _first = null; // Sprite owner releases the texture.
            return true;
        }
        finally
        {
            if (image != null) Object.Destroy(image);
        }
    }

    private void ReadFrame(Texture2D image, bool refining)
    {
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(Size, Size);
        try
        {
            _camera.targetTexture = target;
            if (refining) _camera.projectionMatrix = _original.Crop * _camera.projectionMatrix;
            _stage.SetActive(true);
            _camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
        }
        finally
        {
            _stage.SetActive(false);
            if (refining) _camera.ResetProjectionMatrix();
            _camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    public void Dispose()
    {
        if (_first != null) Object.Destroy(_first);
        Object.DestroyImmediate(_stage);
    }

    internal struct Footprint
    {
        internal RectInt Bounds;
        internal int Count, Solid;
        internal byte Peak;
        private int Span => Math.Max(Bounds.width, Bounds.height);
        private float Scale => Size * .88f / Span;
        private bool Centered => Math.Abs(Bounds.xMin + Bounds.xMax - Size) <= 8 &&
                                 Math.Abs(Bounds.yMin + Bounds.yMax - Size) <= 8;
        private bool Fits => Span >= Size * .80f && Span <= Size * .94f && Centered &&
                             Bounds.xMin > 0 && Bounds.yMin > 0 && Bounds.xMax < Size && Bounds.yMax < Size;
        internal bool NeedsCorrection => Span >= 6 && Solid >= 8 && Solid * 4 >= Count && !Fits;

        internal Matrix4x4 Crop
        {
            get
            {
                // Clip-space pan and zoom preserve perspective, depth clipping and lighting.
                var crop = Matrix4x4.identity;
                crop.m00 = crop.m11 = Scale;
                crop.m03 = Scale * (Size - Bounds.xMin - Bounds.xMax) / Size;
                crop.m13 = Scale * (Size - Bounds.yMin - Bounds.yMax) / Size;
                return crop;
            }
        }

        internal bool Accepts(Footprint next)
        {
            if (!next.Fits) return false;
            float scale = Scale;
            float width = Bounds.width * scale;
            float height = Bounds.height * scale;
            float expected = Count * scale * scale;
            // Alpha-tested foliage reveals extra leaf edges at higher resolution.
            // One eighth of each axis leaves room for this and one-pixel rounding at both ends.
            return Math.Abs(next.Bounds.width - width) <= Math.Max(6f, width * .125f) &&
                   Math.Abs(next.Bounds.height - height) <= Math.Max(6f, height * .125f) &&
                   next.Count >= expected * .6f && next.Count <= expected * 1.6f && next.Solid >= 8;
        }
    }

    internal static Footprint Measure(Color32[] pixels)
    {
        Footprint result = default;
        int left = Size, bottom = Size, right = -1, top = -1;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            byte alpha = pixels[y * Size + x].a;
            if (alpha > result.Peak) result.Peak = alpha;
            if (alpha <= 12) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            bottom = Math.Min(bottom, y);
            top = y;
            result.Count++;
            if (alpha >= 96) result.Solid++;
        }
        if (result.Count != 0) result.Bounds = new RectInt(left, bottom, right - left + 1, top - bottom + 1);
        return result;
    }
}
