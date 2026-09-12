using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

internal static class Program
{
    private static Assembly Mod;
    private static string Managed;
    private static string Core;
    private static int Passed, Failed;
    private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    private static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("CompatibilityTests <mod.dll> <original Managed> <BepInEx core>");
        Managed = Path.GetFullPath(args[1]); Core = Path.GetFullPath(args[2]);
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { Managed, Core })
                if (File.Exists(Path.Combine(dir, name))) return Assembly.LoadFrom(Path.Combine(dir, name));
            return null;
        };
        Mod = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Run();
        System.Console.WriteLine($"{Passed} passed; {Failed} failed. Isolated CLR tests; no Unity/native/network execution.");
        return Failed == 0 ? 0 : 1;
    }

    private static void Run()
    {
        Check("final assembly has no Jotunn dependency", () => Assert(!Mod.GetReferencedAssemblies().Any(a => a.Name == "Jotunn")));
        Check("icons with rendered RGB but zero alpha are rejected", () =>
        {
            var pixels = Enumerable.Repeat(new Color32(100, 80, 60, 0), 128 * 128).ToArray();
            Assert(!HasVisibleIconPixels(pixels));
        });
        Check("a faint visible icon pixel is retained without making its background opaque", () =>
        {
            var pixels = new Color32[128 * 128];
            pixels[pixels.Length - 1] = new Color32(10, 20, 30, 1);
            Assert(HasVisibleIconPixels(pixels));
            Assert(pixels[0].a == 0 && pixels[pixels.Length - 1].a == 1);
        });
        Check("durability readback uses native hundredths", () =>
        {
            foreach (float durability in new[] { 1.239f, 13.579f, 127.4567f, 0.009f, 200f })
            {
                var expected = Item(); expected.m_durability = durability;
                var actual = Item(); actual.m_durability = (int)(durability * 100f) * 0.01f;
                Assert(Matches(actual, expected));
                actual.m_durability += 0.01f;
                Assert(!Matches(actual, expected));
            }
        });
        Check("quality, variant, cheat flag and custom data cannot silently change", () =>
        {
            var expected = Item(); var actual = Item();
            actual.m_quality++; Assert(!Matches(actual, expected)); actual.m_quality--;
            actual.m_variant++; Assert(!Matches(actual, expected)); actual.m_variant--;
            actual.m_cheated = true; Assert(!Matches(actual, expected)); actual.m_cheated = false;
            actual.m_customData["key"] = "different"; Assert(!Matches(actual, expected));
        });
        Check("current native ItemData payload is read with identity intact", () =>
        {
            byte[] data = Payload(1234); var item = Item();
            Assert(Read(data, 1234, item));
            Assert(item.m_quality == 3 && item.m_variant == 4 && item.m_cheated && item.m_customData["key"] == "value");
        });
        Check("mismatched item hash is rejected", () => Assert(!Read(Payload(1234), 4321, Item())));
        Check("truncated item payload is rejected", () => Assert(!Read(Payload(1234).Take(12).ToArray(), 1234, Item())));
        Check("unknown version is rejected", () =>
        {
            byte[] data = Payload(1234); data[0] = 255; Assert(!Read(data, 1234, Item()));
        });
        Check("trailing item payload is rejected", () => Assert(!Read(Payload(1234).Concat(new byte[] { 0 }).ToArray(), 1234, Item())));
    }

    private static bool HasVisibleIconPixels(Color32[] pixels) => (bool)Mod.GetType("HarnessPrefabs.PrefabIconRenderer", true)
        .GetMethod("HasVisiblePixels", StaticPrivate).Invoke(null, new object[] { pixels });

    private static ItemDrop.ItemData Item()
    {
        var item = (ItemDrop.ItemData)RuntimeHelpers.GetUninitializedObject(typeof(ItemDrop.ItemData));
        item.m_stack = 1; item.m_quality = 3; item.m_durability = 100f; item.m_variant = 4;
        item.m_crafterID = 42; item.m_crafterName = "Maker";
        item.m_customData = new Dictionary<string, string> { ["key"] = "value" };
        return item;
    }

    private static bool Matches(ItemDrop.ItemData actual, ItemDrop.ItemData expected) => (bool)Mod.GetType("HarnessPrefabs.HarnessPrefabsArmorStandSwap+StandItemData", true)
        .GetMethod("Matches", StaticPrivate).Invoke(null, new object[] { actual, expected });

    private static bool Read(byte[] data, int hash, ItemDrop.ItemData item)
    {
        object[] args = { data, hash, item, null };
        return (bool)Mod.GetType("HarnessPrefabs.HarnessPrefabsArmorStandSwap+StandItemData", true).GetMethod("TryRead", StaticPrivate).Invoke(null, args);
    }

    private static byte[] Payload(int hash)
    {
        // Fixed wire fixture of the ORIGINAL 1.0.7 ItemData format. Uses the real
        // ZPackage writer and real ItemData.Load; no modified game assembly.
        var p = new ZPackage();
        p.Write((byte)109); p.Write(12345); p.Write((byte)1); p.Write((byte)2); p.Write((byte)0);
        p.Write((byte)(4 | 16 | 32 | 64 | 128));
        p.Write((ushort)3); p.Write(4); p.Write((long)42); p.Write("Maker"); p.Write(hash);
        p.WriteNumItems(1); p.Write("key"); p.Write("value"); p.Write((byte)1);
        return p.GetArray();
    }

    private static void Check(string name, Action body)
    {
        try { body(); Passed++; System.Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Failed++; System.Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException()); }
    }
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Assertion failed"); }
}
