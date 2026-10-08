using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
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
        Check("cloth filtering preserves valid references, nulls, order and duplicates without changing its input", () =>
        {
            object valid = UninitializedGameObject("MagicaClothV2", "MagicaCloth2.MagicaCapsuleCollider");
            object invalid = UninitializedGameObject("UnityEngine.PhysicsModule", "UnityEngine.CapsuleCollider");
            var source = new ArrayList { valid, null, invalid, valid, invalid };
            IList filtered = FilterClothColliders(source);
            Assert(filtered != null && filtered.Count == 3);
            Assert(ReferenceEquals(filtered[0], valid) && filtered[1] == null && ReferenceEquals(filtered[2], valid));
            Assert(source.Count == 5 && ReferenceEquals(source[2], invalid) && ReferenceEquals(source[4], invalid));
            Assert(FilterClothColliders(filtered) == null);
        });
        Check("cloth filtering removes an entirely incompatible list including its first entry", () =>
        {
            object invalid = UninitializedGameObject("UnityEngine.PhysicsModule", "UnityEngine.CapsuleCollider");
            var source = new ArrayList { invalid, invalid };
            IList filtered = FilterClothColliders(source);
            Assert(filtered != null && filtered.Count == 0 && source.Count == 2);
            Assert(FilterClothColliders(filtered) == null);
        });
        Check("valid and empty cloth lists need no replacement", () =>
        {
            object valid = UninitializedGameObject("MagicaClothV2", "MagicaCloth2.MagicaSphereCollider");
            Assert(FilterClothColliders(new ArrayList { null, valid, valid, null }) == null);
            Assert(FilterClothColliders(new ArrayList()) == null);
        });
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
        RunHandEquipmentChecks();
    }

    private static HandFixture ActiveHandFixture;

    private static void RunHandEquipmentChecks()
    {
        var harmony = new Harmony("HarnessPrefabs.Tests.HandEquipment");
        try
        {
            // Character static initializers hash animator names through Unity; the
            // hand-state checks never use those hashes or an Animator instance.
            harmony.Patch(AccessTools.Method(typeof(ZSyncAnimation), nameof(ZSyncAnimation.GetHash), new[] { typeof(string) }),
                transpiler: new HarmonyMethod(typeof(Program), nameof(MockAnimatorHash)));
            harmony.Patch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.EquipItem), new[] { typeof(ItemDrop.ItemData), typeof(bool) }),
                transpiler: new HarmonyMethod(typeof(Program), nameof(MockEquipItemBody)));
            Check("hand set equips normally in right then left order", () =>
            {
                var f = new HandFixture();
                Assert(f.Run(out _) && f.Calls.SequenceEqual(new[] { f.Right, f.Left }));
            });
            Check("auto-equipped incoming shield succeeds without a duplicate equip call", () =>
            {
                var f = new HandFixture(); f.AutoShield = f.Left;
                Assert(f.Run(out _) && f.Calls.SequenceEqual(new[] { f.Right }));
            });
            Check("a genuinely refused left-hand equip still fails", () =>
            {
                var f = new HandFixture { RefuseLeft = true };
                Assert(!f.Run(out string error) && error.Contains("left-hand"));
            });
            Check("a different auto-equipped shield does not satisfy the expected item", () =>
            {
                var f = new HandFixture { AutoShield = Item(), RefuseLeft = true };
                Assert(!f.Run(out _) && f.Calls.Contains(f.Left));
            });
            Check("matching hand references with a cleared equipped flag are rejected", () =>
            {
                var f = new HandFixture(); f.AutoShield = f.Left;
                f.AfterRight = () => f.Left.m_equipped = false;
                Assert(!f.Run(out _));
            });
            Check("matching hand references outside the inventory are rejected", () =>
            {
                var f = new HandFixture(); f.AutoShield = f.Left;
                f.AfterRight = () => f.Items.Remove(f.Left);
                Assert(!f.Run(out _));
            });
            Check("unexpected hidden equipment after auto-equip is rejected", () =>
            {
                var f = new HandFixture(); f.AutoShield = f.Left;
                f.AfterRight = () => f.SetHand("m_hiddenLeftItem", f.Left);
                Assert(!f.Run(out _));
            });
        }
        catch (Exception ex)
        {
            Check("hand equipment fixture setup", () => throw new InvalidOperationException("Could not isolate EquipItem", ex));
        }
        finally
        {
            ActiveHandFixture = null;
            harmony.UnpatchSelf();
        }
    }

    private static IEnumerable<CodeInstruction> MockAnimatorHash() => new[]
    {
        new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Ret)
    };

    private static IEnumerable<CodeInstruction> MockEquipItemBody() => new[]
    {
        new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Program), nameof(MockEquipItem))),
        new CodeInstruction(OpCodes.Ret)
    };

    private static bool MockEquipItem(Humanoid player, ItemDrop.ItemData item)
    {
        if (ActiveHandFixture == null || !ReferenceEquals(player, ActiveHandFixture.Player))
            throw new InvalidOperationException("Unexpected native EquipItem call in isolated test");
        return ActiveHandFixture.Equip(item);
    }

    private sealed class HandFixture
    {
        internal readonly Player Player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        internal readonly ItemDrop.ItemData Left = Item(), Right = Item();
        internal readonly List<ItemDrop.ItemData> Items = new(), Calls = new();
        internal ItemDrop.ItemData AutoShield;
        internal bool RefuseLeft;
        internal Action AfterRight;

        internal HandFixture()
        {
            var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
            Items.Add(Left); Items.Add(Right);
            AccessTools.Field(typeof(Inventory), "m_inventory").SetValue(inventory, Items);
            AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(Player, inventory);
        }

        internal void SetHand(string field, ItemDrop.ItemData item) => AccessTools.Field(typeof(Humanoid), field).SetValue(Player, item);

        internal bool Equip(ItemDrop.ItemData item)
        {
            Calls.Add(item);
            // Model the original EquipItem's already-equipped false result and
            // SecondaryAttacks' synchronous shield equip during the right-hand call.
            if (ReferenceEquals(AccessTools.Field(typeof(Humanoid), "m_leftItem").GetValue(Player), item) ||
                ReferenceEquals(AccessTools.Field(typeof(Humanoid), "m_rightItem").GetValue(Player), item)) return false;
            if (ReferenceEquals(item, Left))
            {
                if (RefuseLeft) return false;
                SetHand("m_leftItem", item); item.m_equipped = true;
            }
            else if (ReferenceEquals(item, Right))
            {
                SetHand("m_rightItem", item); item.m_equipped = true;
                if (AutoShield != null) { SetHand("m_leftItem", AutoShield); AutoShield.m_equipped = true; }
                AfterRight?.Invoke();
            }
            else throw new InvalidOperationException("Unexpected item");
            return true;
        }

        internal bool Run(out string error)
        {
            ActiveHandFixture = this;
            try
            {
                object[] args = { Player, Left, Right, null };
                bool result = (bool)Mod.GetType("HarnessPrefabs.HarnessPrefabsArmorStandSwap", true)
                    .GetMethod("TryEquipHandState", StaticPrivate).Invoke(null, args);
                error = (string)args[3];
                return result;
            }
            finally { ActiveHandFixture = null; }
        }
    }

    private static bool HasVisibleIconPixels(Color32[] pixels) => (bool)Mod.GetType("HarnessPrefabs.PrefabIconRenderer", true)
        .GetMethod("HasVisiblePixels", StaticPrivate).Invoke(null, new object[] { pixels });

    private static IList FilterClothColliders(IList colliders) => (IList)Mod.GetType("HarnessPrefabs.ArmorStandSetupClothPatch", true)
        .GetMethod("FilterClothColliders", StaticPrivate).Invoke(null, new object[] { colliders });

    // Managed type identity only: these objects have no native Unity instance.
    // ArrayList models malformed serialized references without corrupting a CLR typed array.
    private static object UninitializedGameObject(string assembly, string type) => RuntimeHelpers.GetUninitializedObject(
        Assembly.LoadFrom(Path.Combine(Managed, assembly + ".dll")).GetType(type, true));

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
