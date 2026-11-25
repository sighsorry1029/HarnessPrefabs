using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HarnessPrefabs;

// Only this renderer's 128px RGBA snapshots are stored here.
internal static class PrefabIconPng
{
    private const int Size = 128;
    private static readonly Func<Texture2D, byte[], bool> Decode =
        (Func<Texture2D, byte[], bool>)Delegate.CreateDelegate(typeof(Func<Texture2D, byte[], bool>),
            typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) })!);

    internal static string PathFor(BepInPlugin? owner, string prefab, string framing)
    {
        string version = owner?.Version.ToString() ?? global::Version.GetVersionString();
        string name = FileNamePart(prefab);
        bool lossy = name != prefab;
        if (owner != null)
        {
            string guid = FileNamePart(owner.GUID, allowDots: true);
            lossy |= guid != owner.GUID;
            name += "-" + guid;
        }
        name += $"-{version}-{framing}";
        if (lossy)
        {
            string key = string.Join("\0", prefab, owner?.GUID, version, framing);
            using var sha = SHA256.Create();
            name += "-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key)), 0, 8).Replace("-", "");
        }
        return Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "HarnessPrefabs", "CachedIcons", "r5", name + ".png");
    }

    private static string FileNamePart(string value, bool allowDots = false)
    {
        if (value.Length == 0) return "_";
        var prefix = value.ToCharArray(0, Math.Min(value.Length, 48));
        for (int i = 0; i < prefix.Length; i++)
        {
            char c = prefix[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && !(allowDots && c == '.')) prefix[i] = '_';
        }
        // Both parts, the version, framing and optional hash must fit a single filename even in UTF-8.
        int length = prefix.Length;
        while (Encoding.UTF8.GetByteCount(prefix, 0, length) > 88) length--;
        return new string(prefix, 0, length);
    }

    internal static Sprite? Read(string path)
    {
        Texture2D? texture = null;
        try
        {
            if (!File.Exists(path)) return null;
            byte[] png;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 33 || stream.Length > 128 * 1024) return null;
                png = reader.ReadBytes((int)stream.Length);
            }
            // Check IHDR before passing dimensions from a damaged file to Unity's decoder.
            if (png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 ||
                png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R' ||
                png[16] != 0 || png[17] != 0 || png[18] != 0 || png[19] != Size ||
                png[20] != 0 || png[21] != 0 || png[22] != 0 || png[23] != Size) return null;
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!Decode(texture, png) || texture.width != Size || texture.height != Size ||
                !Array.Exists(texture.GetPixels32(), pixel => pixel.a > 0)) return null;
            // Hammer exposes this texture on Piece.m_icon, including to other mods that read its pixels.
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f));
            if (sprite != null) texture = null;
            return sprite;
        }
        catch (Exception error) { HarnessPrefabsPlugin.Log.LogDebug("Icon cache read: " + error.Message); return null; }
        finally { if (texture != null) Object.Destroy(texture); }
    }

    internal static void Write(string path, Texture2D texture)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            byte[] png = ImageConversion.EncodeToPNG(texture);
            if (png == null || png.Length == 0) return;
            File.WriteAllBytes(temporary, png);
            if (!File.Exists(path))
            {
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { } // Another client published this snapshot.
                return;
            }
            // Multiple clients can discover the same broken PNG. Only one needs to replace it.
            FileStream repair;
            try
            {
                repair = new FileStream(Path.ChangeExtension(path, ".lock"), FileMode.OpenOrCreate,
                    FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) { return; }
            using (repair)
            {
                // The writer which held this lock before us may already have repaired the file.
                var existing = Read(path);
                if (existing != null)
                {
                    Object.Destroy(existing.texture);
                    Object.Destroy(existing);
                }
                else File.Replace(temporary, path, null);
            }
        }
        catch (Exception error) { HarnessPrefabsPlugin.Log.LogDebug("Icon cache write: " + error.Message); }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
