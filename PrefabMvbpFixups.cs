#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HarnessPrefabs;

internal static class PrefabMvbpFixups
{
    private const float SnapPointTolerance = 0.001f;

    public static void Apply(GameObject prefab)
    {
        if (!prefab)
        {
            return;
        }

        bool hasMarker = prefab.GetComponent<HarnessPrefabsMvbpFixupMarker>();
        if (prefab.name.Equals("Trailership", StringComparison.Ordinal))
        {
            bool staticFixupsComplete = ApplyTrailershipFixup(prefab, applyStaticFixups: !hasMarker);
            if (staticFixupsComplete && !hasMarker)
            {
                prefab.AddComponent<HarnessPrefabsMvbpFixupMarker>();
            }

            return;
        }

        if (hasMarker)
        {
            return;
        }

        bool applied = ApplyByName(prefab);
        if (applied && !hasMarker)
        {
            prefab.AddComponent<HarnessPrefabsMvbpFixupMarker>();
        }
    }

    private static bool ApplyByName(GameObject prefab)
    {
        List<Vector3> points = new();
        switch (prefab.name)
        {
            case "ArmorStand_Male":
            case "ArmorStand_Female":
                return AddSnapPoint(prefab, Vector3.zero, "Origin");

            case "TreasureChest_mountaincave":
            case "TreasureChest_trollcave":
                FixPieceLayers(prefab);
                return AddSnapPointsToMeshCorners(prefab, "stonechest");

            case "TreasureChest_dvergrtower":
            case "TreasureChest_dvergrtown":
                FixPieceLayers(prefab);
                return AddSnapPoints(prefab,
                    Vector3.zero,
                    new Vector3(0.66f, 0f, 0.33f),
                    new Vector3(0.66f, 0f, -0.33f),
                    new Vector3(-0.66f, 0f, 0.33f),
                    new Vector3(-0.66f, 0f, -0.33f),
                    new Vector3(0.6f, 0.7f, 0.33f),
                    new Vector3(0.6f, 0.7f, -0.33f),
                    new Vector3(-0.6f, 0.7f, 0.33f),
                    new Vector3(-0.6f, 0.7f, -0.33f));

            case "TreasureChest_plains_stone":
            case "TreasureChest_sunkencrypt":
            case "TreasureChest_fCrypt":
                FixPieceLayers(prefab);
                return AddSnapPoints(prefab,
                    new Vector3(0f, -0.01f, 0f),
                    new Vector3(1f, -0.01f, 0.37f),
                    new Vector3(1f, -0.01f, -0.37f),
                    new Vector3(-1f, -0.01f, 0.37f),
                    new Vector3(-1f, -0.01f, -0.37f),
                    new Vector3(0.65f, 0.8f, 0.35f),
                    new Vector3(0.65f, 0.8f, -0.35f),
                    new Vector3(-0.65f, 0.8f, 0.35f),
                    new Vector3(-0.65f, 0.8f, -0.35f));

            case "blackmarble_column_3":
                for (float y = -4f; y <= 4f; y += 2f)
                {
                    points.AddRange(BoxCornerPoints(1f, y, 1f));
                }

                return AddSnapPoints(prefab, points);

            case "blackmarble_creep_4x1x1":
                RemoveRandomPieceRotation(prefab, "new");
                return AddSnapPoints(prefab,
                    new Vector3(-0.5f, 2f, -0.5f),
                    new Vector3(0.5f, 2f, 0.5f),
                    new Vector3(-0.5f, 2f, 0.5f),
                    new Vector3(0.5f, 2f, -0.5f),
                    new Vector3(-0.5f, -2f, -0.5f),
                    new Vector3(0.5f, -2f, 0.5f),
                    new Vector3(-0.5f, -2f, 0.5f),
                    new Vector3(0.5f, -2f, -0.5f),
                    new Vector3(0f, -2f, 0f),
                    new Vector3(0f, 2f, 0f));

            case "blackmarble_creep_4x2x1":
                RemoveRandomPieceRotation(prefab, "new");
                return AddSnapPoints(prefab,
                    new Vector3(1f, 2f, -0.5f),
                    new Vector3(1f, 2f, 0.5f),
                    new Vector3(-1f, 2f, -0.5f),
                    new Vector3(-1f, 2f, 0.5f),
                    new Vector3(1f, -2f, -0.5f),
                    new Vector3(1f, -2f, 0.5f),
                    new Vector3(-1f, -2f, -0.5f),
                    new Vector3(-1f, -2f, 0.5f),
                    new Vector3(0f, -2f, 0f),
                    new Vector3(0f, 2f, 0f));

            case "blackmarble_creep_slope_inverted_1x1x2":
                return AddSnapPoints(prefab,
                    new Vector3(-0.5f, 1f, -0.5f),
                    new Vector3(0.5f, 1f, 0.5f),
                    new Vector3(-0.5f, 1f, 0.5f),
                    new Vector3(0.5f, 1f, -0.5f),
                    new Vector3(0.5f, -1f, -0.5f),
                    new Vector3(-0.5f, -1f, -0.5f));

            case "blackmarble_creep_slope_inverted_2x2x1":
                return AddSnapPoints(prefab,
                    new Vector3(-1f, 0.5f, -1f),
                    new Vector3(1f, 0.5f, 1f),
                    new Vector3(-1f, 0.5f, 1f),
                    new Vector3(1f, 0.5f, -1f),
                    new Vector3(-1f, -0.5f, -1f),
                    new Vector3(1f, -0.5f, -1f));

            case "blackmarble_creep_stair":
                return AddSnapPoints(prefab,
                    new Vector3(-1f, 1f, -1f),
                    new Vector3(1f, 1f, -1f),
                    new Vector3(-1f, 0f, -1f),
                    new Vector3(1f, 0f, -1f),
                    new Vector3(-1f, 0f, 1f),
                    new Vector3(1f, 0f, 1f));

            case "blackmarble_floor_large":
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int x = -4; x <= 4; x += 2)
                    {
                        for (int z = -4; z <= 4; z += 2)
                        {
                            points.Add(new Vector3(x, y, z));
                        }
                    }
                }

                return AddSnapPoints(prefab, points);

            case "blackmarble_head_big01":
            case "blackmarble_head_big02":
                return AddSnapPoints(prefab,
                    new Vector3(-1f, 1f, -1f),
                    new Vector3(1f, 1f, 1f),
                    new Vector3(-1f, 1f, 1f),
                    new Vector3(1f, 1f, -1f),
                    new Vector3(1f, -1f, -1f),
                    new Vector3(-1f, -1f, -1f));

            case "blackmarble_out_2":
                RemoveComponent<MeshCollider>(prefab);
                FixPieceLayers(prefab);
                return true;

            case "blackmarble_tile_floor_1x1":
                return MoveSnapPoints(prefab,
                    ("_snappoint", new Vector3(0.5f, 0.1f, 0.5f)),
                    ("_snappoint (1)", new Vector3(0.5f, 0.1f, -0.5f)),
                    ("_snappoint (2)", new Vector3(-0.5f, 0.1f, 0.5f)),
                    ("_snappoint (3)", new Vector3(-0.5f, 0.1f, -0.5f)));

            case "blackmarble_tile_floor_2x2":
                return MoveSnapPoints(prefab,
                    ("_snappoint", new Vector3(1f, 0.1f, 1f)),
                    ("_snappoint (1)", new Vector3(1f, 0.1f, -1f)),
                    ("_snappoint (2)", new Vector3(-1f, 0.1f, 1f)),
                    ("_snappoint (3)", new Vector3(-1f, 0.1f, -1f)));

            case "blackmarble_tile_wall_1x1":
                return MoveSnapPoints(prefab,
                    ("_snappoint", new Vector3(0.5f, -0.05f, 0.1f)),
                    ("_snappoint (1)", new Vector3(0.5f, 0.95f, 0.1f)),
                    ("_snappoint (2)", new Vector3(-0.5f, -0.05f, 0.1f)),
                    ("_snappoint (3)", new Vector3(-0.5f, 0.95f, 0.1f)),
                    ("_snappoint (4)", new Vector3(0f, -0.05f, 0.1f)),
                    ("_snappoint (5)", new Vector3(0.5f, 0.45f, 0.1f)),
                    ("_snappoint (6)", new Vector3(0f, 0.95f, 0.1f)),
                    ("_snappoint (7)", new Vector3(-0.5f, 0.45f, 0.1f)));

            case "blackmarble_tile_wall_2x2":
                return MoveSnapPoints(prefab,
                    ("_snappoint", new Vector3(1f, -0.05f, 0.1f)),
                    ("_snappoint (1)", new Vector3(1f, 1.95f, 0.1f)),
                    ("_snappoint (2)", new Vector3(-1f, -0.05f, 0.1f)),
                    ("_snappoint (3)", new Vector3(-1f, -0.05f, 0.1f)),
                    ("_snappoint (4)", new Vector3(0.5f, -0.05f, 0.1f)),
                    ("_snappoint (5)", new Vector3(0.5f, 0.95f, 0.1f)),
                    ("_snappoint (6)", new Vector3(-0.5f, -0.05f, 0.1f)),
                    ("_snappoint (7)", new Vector3(-0.5f, 0.95f, 0.1f)));

            case "blackmarble_tile_wall_2x4":
                return MoveSnapPoints(prefab,
                    ("_snappoint", new Vector3(1f, -0.05f, 0.1f)),
                    ("_snappoint (1)", new Vector3(1f, 3.95f, 0.1f)),
                    ("_snappoint (2)", new Vector3(-1f, -0.05f, 0.1f)),
                    ("_snappoint (3)", new Vector3(-1f, 3.95f, 0.1f)),
                    ("_snappoint (4)", new Vector3(0.5f, -0.05f, 0.1f)),
                    ("_snappoint (5)", new Vector3(0.5f, 1.95f, 0.1f)),
                    ("_snappoint (6)", new Vector3(-0.5f, -0.05f, 0.1f)),
                    ("_snappoint (7)", new Vector3(-0.5f, 1.95f, 0.1f)));

            case "dungeon_queen_door":
                return AddSnapPoints(prefab, new Vector3(2.5f, 0f, 0f), new Vector3(-2.5f, 0f, 0f));
            case "dungeon_sunkencrypt_irongate":
                return AddSnapPoints(prefab, new Vector3(1f, -0.4f, 0f), new Vector3(-1f, -0.4f, 0f));
            case "sunken_crypt_gate":
                return AddSnapPoints(prefab, new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f));
            case "dvergrprops_wood_beam":
            case "dvergrtown_wood_beam":
                return AddSnapPoints(prefab, new Vector3(3f, 0f, 0f), new Vector3(-3f, 0f, 0f));
            case "dvergrprops_wood_pole":
                return AddSnapPoints(prefab, new Vector3(0f, 2f, 0f), new Vector3(0f, -2f, 0f));
            case "dvergrprops_wood_wall":
                return AddSnapPoints(prefab,
                    new Vector3(2.2f, 2f, 0f),
                    new Vector3(-2.2f, 2f, 0f),
                    new Vector3(2.2f, -2f, 0f),
                    new Vector3(-2.2f, -2f, 0f));
            case "dvergrtown_arch":
                return AddSnapPoints(prefab, new Vector3(1f, 0.5f, 0f));
            case "dvergrtown_secretdoor":
            case "dvergrtown_slidingdoor":
                return AddSnapPoints(prefab,
                    new Vector3(2f, 0f, 0f),
                    new Vector3(-2f, 0f, 0f),
                    new Vector3(2f, 4f, 0f),
                    new Vector3(-2f, 4f, 0f));
            case "dvergrtown_stair_corner_wood_left":
                return AddSnapPoints(prefab,
                    new Vector3(0.25f, 0f, -0.25f),
                    new Vector3(0.25f, 0f, 0.25f),
                    new Vector3(0.25f, 1.1f, -0.25f),
                    new Vector3(0.25f, 1.1f, 0.25f),
                    new Vector3(0.25f, 0f, 2f),
                    new Vector3(-0.25f, 1.1f, -0.25f),
                    new Vector3(-2f, 1.1f, -0.25f));
            case "dvergrprops_shelf":
            case "dvergrprops_table":
                return SetWearNTearSupports(prefab);
            case "dvergrtown_wood_pole":
                return AddSnapPoints(prefab, new Vector3(0f, -2f, 0f), new Vector3(0f, 2f, 0f));
            case "dvergrtown_wood_stake":
                return AddSnapPoints(prefab, Vector3.zero);
            case "dvergrtown_wood_crane":
                return AddSnapPoints(prefab, new Vector3(0f, -3f, 0f));
            case "dvergrtown_wood_support":
                return AddSnapPoints(prefab, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
            case "dvergrtown_wood_wall01":
                Transform wallCollider = prefab.transform.Find("wallcollider");
                if (wallCollider)
                {
                    wallCollider.localPosition = Vector3.zero;
                }

                return AddSnapPoints(prefab,
                    new Vector3(-3f, -2.7f, 0f),
                    new Vector3(3f, -2.7f, 0f),
                    new Vector3(-3f, 2.7f, 0f),
                    new Vector3(3f, 2.7f, 0f));
            case "dvergrtown_wood_wall02":
                return AddSnapPoints(prefab,
                    new Vector3(-3f, -0.5f, 0f),
                    new Vector3(3f, -0.5f, 0f),
                    new Vector3(-3f, 4.5f, 0f),
                    new Vector3(3f, 4.5f, 0f));
            case "dvergrtown_wood_wall03":
                return AddSnapPoints(prefab,
                    new Vector3(1.1f, 0f, 0f),
                    new Vector3(-1.1f, 0f, 0f),
                    new Vector3(2f, 2f, 0f),
                    new Vector3(-2f, 2f, 0f),
                    new Vector3(1.1f, 4f, 0f),
                    new Vector3(-1.1f, 4f, 0f));

            case "goblin_roof_45d":
                return AddSnapPoints(prefab,
                    new Vector3(1f, 0f, 1f),
                    new Vector3(-1f, 0f, 1f),
                    new Vector3(1f, 2f, -1f),
                    new Vector3(-1f, 2f, -1f));
            case "goblin_roof_45d_corner":
                return AddSnapPoints(prefab, new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, 1f));
            case "goblin_woodwall_1m":
                return AddSnapPoints(prefab,
                    new Vector3(-0.5f, 0f, 0f),
                    new Vector3(0.5f, 0f, 0f),
                    new Vector3(-0.5f, 2f, 0f),
                    new Vector3(0.5f, 2f, 0f));
            case "goblin_woodwall_2m":
                return AddSnapPoints(prefab,
                    new Vector3(-1f, 0f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(-1f, 2f, 0f),
                    new Vector3(1f, 2f, 0f));
            case "Ice_floor":
                return AddSnapPoints(prefab,
                    new Vector3(2f, 1f, 2f),
                    new Vector3(-2f, 1f, -2f),
                    new Vector3(2f, 1f, -2f),
                    new Vector3(-2f, 1f, 2f),
                    new Vector3(2f, -1f, 2f),
                    new Vector3(-2f, -1f, -2f),
                    new Vector3(2f, -1f, -2f),
                    new Vector3(-2f, -1f, 2f));
            case "turf_roof_top":
                return AddSnapPoints(prefab, new Vector3(1f, 0.5f, 0f), new Vector3(-1f, 0.5f, 0f));
            case "metalbar_1x2":
                return AddSnapPoints(prefab,
                    Vector3.zero,
                    new Vector3(1f, -1f, 0f),
                    new Vector3(1f, 0f, 0f),
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(-1f, 0f, 0f));

            case "stone_floor":
                for (float y = -0.5f; y <= 0.5f; y += 1f)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        for (int z = -2; z <= 2; z++)
                        {
                            if (Math.Abs(x) != 2 || Math.Abs(z) != 2)
                            {
                                points.Add(new Vector3(x, y, z));
                            }
                        }
                    }
                }

                return AddSnapPoints(prefab, points);

            case "stoneblock_fracture":
                for (float y = -0.5f; y <= 0.5f; y += 1f)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        for (int z = -1; z <= 1; z++)
                        {
                            points.Add(new Vector3(x, y, z));
                        }
                    }
                }

                FixPieceLayers(prefab);
                AddSnapPoints(prefab, points);
                AddBoxCollider(prefab, Vector3.zero, new Vector3(2f, 1f, 2f));
                return true;

            case "blackmarble_post01":
                FixPieceLayers(prefab);
                AddSnapPoints(prefab,
                    Vector3.zero,
                    new Vector3(1f, 0f, 1f),
                    new Vector3(1f, 0f, -1f),
                    new Vector3(-1f, 0f, -1f),
                    new Vector3(-1f, 0f, 1f),
                    new Vector3(1f, 1f, 1f),
                    new Vector3(1f, 1f, -1f),
                    new Vector3(-1f, 1f, -1f),
                    new Vector3(-1f, 1f, 1f),
                    new Vector3(0f, 3.5f, 0f));
                AdjustNamedBoxCollider(prefab, "collider", new Vector3(0f, 0.5f, 0f), new Vector3(0f, -1f, 0f));
                return true;

            case "wood_ledge":
                return AddSnapPoints(prefab,
                    new Vector3(0.5f, 0f, 0.25f),
                    new Vector3(0f, 0f, 0.25f),
                    new Vector3(-0.5f, 0f, 0.25f),
                    new Vector3(-0.5f, 0f, -0.25f),
                    new Vector3(0.5f, 0f, -0.25f));

            case "dverger_demister":
                RemoveColliders(prefab);
                AddBoxCollider(prefab, new Vector3(0f, -0.2f, 0f), new Vector3(0.2f, 1.1f, 0.2f));
                AddBoxCollider(prefab, Vector3.zero, new Vector3(1f, 0.1f, 1f));
                return AddSnapPoints(prefab, Vector3.zero, new Vector3(0f, -0.65f, 0f));

            case "dverger_demister_large":
                RemoveColliders(prefab);
                AddBoxCollider(prefab, new Vector3(0f, -0.25f, 0f), new Vector3(0.2f, 1.3f, 0.2f));
                AddBoxCollider(prefab, new Vector3(0f, -0.25f, 0f), new Vector3(1f, 0.1f, 1f));
                return AddSnapPoints(prefab, Vector3.zero, new Vector3(0f, -0.9f, 0f));

            case "dvergrprops_hooknchain":
                return AddSnapPoint(prefab, new Vector3(0f, 2.5f, 0f), "$hud_snappoint_top") |
                       AddSnapPoint(prefab, new Vector3(0f, -2f, 0f), "Hook");
            case "barrell":
                return AddSnapPoints(prefab, new Vector3(0f, -1f, 0f));
            case "goblin_strawpile":
                AddBoxCollider(prefab, Vector3.zero, new Vector3(1.5f, 0.02f, 1.5f));
                return true;
            case "mountainkit_chair":
                return AddChair(prefab, Vector3.zero);
            case "dvergrprops_chair":
                return AddChair(prefab, new Vector3(0f, -0.15f, 0f));
            case "dvergrprops_stool":
                return AddChair(prefab, new Vector3(0f, -0.1f, 0f));

            case "Ashland_Stair":
                return AddSnapPoints(prefab,
                    new Vector3(2f, 0f, -2f),
                    new Vector3(-2f, 0f, -2f),
                    new Vector3(-2f, 0f, 2f),
                    new Vector3(2f, 0f, 2f),
                    new Vector3(2f, 2f, -2f),
                    new Vector3(-2f, 2f, -2f));
            case "Ashlands_Fortress_Floor":
                return AddSnapPoints(prefab,
                    new Vector3(1f, -0.5f, -1f),
                    new Vector3(-1f, -0.5f, -1f),
                    new Vector3(-1f, -0.5f, 1f),
                    new Vector3(1f, -0.5f, 1f),
                    new Vector3(1f, 0.5f, -1f),
                    new Vector3(-1f, 0.5f, -1f),
                    new Vector3(-1f, 0.5f, 1f),
                    new Vector3(1f, 0.5f, 1f),
                    Vector3.zero);
            case "Ashlands_Wall_2x2":
            case "Ashlands_Wall_2x2_edge2":
            case "Ashlands_Wall_2x2_top":
            case "Ashlands_Wall_2x2_edge2_top":
                return AddSnapPoints(prefab,
                    new Vector3(-1f, 0f, -0.5f),
                    new Vector3(1f, 0f, -0.5f),
                    new Vector3(-1f, 2f, -0.5f),
                    new Vector3(1f, 2f, -0.5f),
                    new Vector3(-1f, 0f, 0.5f),
                    new Vector3(1f, 0f, 0.5f),
                    new Vector3(-1f, 2f, 0.5f),
                    new Vector3(1f, 2f, 0.5f));
            case "Ashlands_Wall_2x2_edge_top":
            case "Ashlands_Wall_2x2_edge":
                return AddSnapPoints(prefab,
                    new Vector3(1f, 0f, 0.5f),
                    new Vector3(-1f, 0f, 0.5f),
                    new Vector3(1f, 2f, 0.5f),
                    new Vector3(-1f, 2f, 0.5f),
                    new Vector3(1f, 0f, -0.5f),
                    new Vector3(-1f, 0f, -0.5f),
                    new Vector3(1f, 2f, -0.5f),
                    new Vector3(-1f, 2f, -0.5f));
            case "Ashlands_Wall_2x2_cornerR":
            case "Ashlands_Wall_2x2_cornerR_top":
                return AddCornerSnapPoints(prefab, right: true);
            case "Ashlands_Wall_2x2_cornerL":
            case "Ashlands_Wall_2x2_cornerL_top":
                return AddCornerSnapPoints(prefab, right: false);
            case "Ashlands_Fortress_Wall_Spikes":
                return AddSnapPoints(prefab, new Vector3(0f, 0f, -2f), Vector3.zero, new Vector3(0f, 0f, 2f));
        }

        return false;
    }

    private static bool ApplyTrailershipFixup(GameObject prefab, bool applyStaticFixups)
    {
        GameObject vikingShip = ZNetScene.instance ? ZNetScene.instance.GetPrefab("VikingShip") : null;
        bool staticFixupsComplete = !applyStaticFixups;

        if (applyStaticFixups)
        {
            MeshFilter trailerHull = FindDeepChild(prefab.transform, "hull")?.GetComponent<MeshFilter>();
            MeshFilter vikingHull = vikingShip ? FindDeepChild(vikingShip.transform, "hull")?.GetComponent<MeshFilter>() : null;
            if (trailerHull && vikingHull && vikingHull.sharedMesh)
            {
                trailerHull.sharedMesh = vikingHull.sharedMesh;
            }

            GameObject shield = ZNetScene.instance ? ZNetScene.instance.GetPrefab("ShieldBanded") : null;
            staticFixupsComplete = vikingShip && shield;
            MeshRenderer shieldRenderer = shield ? shield.GetComponentInChildren<MeshRenderer>(true) : null;
            Material shieldMaterial = shieldRenderer ? shieldRenderer.sharedMaterial : null;
            Transform storage = prefab.transform.Find("ship/visual/Customize/storage");
            if (shieldMaterial && storage)
            {
                for (int i = 0; i < storage.childCount; i++)
                {
                    Transform child = storage.GetChild(i);
                    MeshRenderer renderer = child && child.name.StartsWith("Shield", StringComparison.Ordinal)
                        ? child.GetComponent<MeshRenderer>()
                        : null;
                    if (renderer)
                    {
                        renderer.sharedMaterial = shieldMaterial;
                    }
                }
            }

            Cloth trailerSail = FindDeepChild(prefab.transform, "sail_full")?.GetComponent<Cloth>();
            Cloth vikingSail = vikingShip ? FindDeepChild(vikingShip.transform, "sail_full")?.GetComponent<Cloth>() : null;
            if (trailerSail && vikingSail)
            {
                trailerSail.coefficients = vikingSail.coefficients;
            }
        }

        Ship ship = prefab.GetComponent<Ship>();
        if (ship)
        {
            Ship vikingShipComponent = vikingShip ? vikingShip.GetComponent<Ship>() : null;
            if (applyStaticFixups)
            {
                Transform controlGui = prefab.transform.Find("ControlGui");
                if (!controlGui)
                {
                    GameObject controlGuiObject = new("ControlGui");
                    controlGuiObject.transform.SetParent(prefab.transform, worldPositionStays: false);
                    controlGui = controlGuiObject.transform;
                }

                controlGui.localPosition = new Vector3(1f, 1.696f, -6.54f);
                ship.m_controlGuiPos = controlGui;
                EnsureTrailershipAshlandsDamageEffects(prefab, ship, vikingShip);
            }

            ApplyTrailershipMovementProfile(ship, vikingShipComponent);
        }

        if (applyStaticFixups)
        {
            ShipControlls controls = FindDeepChild(prefab.transform, "rudder_button")?.GetComponent<ShipControlls>();
            Transform attachPoint = prefab.transform.Find("sit locations/sit_box (4)/attachpoint");
            if (controls && attachPoint)
            {
                controls.m_attachPoint = attachPoint;
            }
        }

        return staticFixupsComplete;
    }

    private static void ApplyTrailershipMovementProfile(Ship ship, Ship vikingShip)
    {
        float speedRatio = HarnessPrefabsPlugin.TrailershipSpeedRatio;
        if (!vikingShip)
        {
            ship.m_damping = 0.05f;
            ship.m_dampingSideway = 0.15f;
            ship.m_dampingForward = 0.001f / speedRatio;
            ship.m_angularDamping = 0.3f;
            ship.m_sailForceOffset = 2f;
            ship.m_sailForceFactor = 0.05f * speedRatio;
            ship.m_rudderSpeed = 1f;
            ship.m_stearForceOffset = -8f;
            ship.m_stearForce = 1f * speedRatio;
            ship.m_stearVelForceFactor = 0.8f;
            ship.m_backwardForce = 0.2f * speedRatio;
            ship.m_rudderRotationMax = 45f;
            return;
        }

        ship.m_damping = vikingShip.m_damping;
        ship.m_dampingSideway = vikingShip.m_dampingSideway;
        ship.m_dampingForward = vikingShip.m_dampingForward / speedRatio;
        ship.m_angularDamping = vikingShip.m_angularDamping;
        ship.m_sailForceOffset = vikingShip.m_sailForceOffset;
        ship.m_sailForceFactor = vikingShip.m_sailForceFactor * speedRatio;
        ship.m_rudderSpeed = vikingShip.m_rudderSpeed;
        ship.m_stearForceOffset = vikingShip.m_stearForceOffset;
        ship.m_stearForce = vikingShip.m_stearForce * speedRatio;
        ship.m_stearVelForceFactor = vikingShip.m_stearVelForceFactor;
        ship.m_backwardForce = vikingShip.m_backwardForce * speedRatio;
        ship.m_rudderRotationMax = vikingShip.m_rudderRotationMax;
    }

    private static bool EnsureTrailershipAshlandsDamageEffects(GameObject prefab, Ship ship, GameObject vikingShip)
    {
        bool applied = false;
        if (!ship.m_ashdamageEffects)
        {
            Transform sourceEffects = vikingShip ? FindDeepChild(vikingShip.transform, "ashdamageeffects") : null;
            GameObject effects = sourceEffects
                ? Object.Instantiate(sourceEffects.gameObject)
                : new GameObject("ashdamageeffects");

            effects.name = "ashdamageeffects";
            effects.transform.SetParent(prefab.transform, worldPositionStays: false);
            if (sourceEffects)
            {
                effects.transform.localPosition = sourceEffects.localPosition;
                effects.transform.localRotation = sourceEffects.localRotation;
                effects.transform.localScale = sourceEffects.localScale;
            }
            else
            {
                effects.transform.localPosition = new Vector3(0f, -0.8f, 0f);
            }

            ship.m_ashdamageEffects = effects;
            applied = true;
        }

        if (ship.m_ashdamageEffects)
        {
            ship.m_ashdamageEffects.SetActive(false);
            ship.m_ashlandsFxAudio = ship.m_ashdamageEffects.GetComponentsInChildren<AudioSource>(true).ToList();
        }

        return applied;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (!root || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child && child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private static bool AddCornerSnapPoints(GameObject prefab, bool right)
    {
        if (right)
        {
            const float offset = -0.25f;
            return AddSnapPoints(prefab,
                new Vector3(offset + 0f, 0f, -1f),
                new Vector3(offset + 0f, 0f, 0f),
                new Vector3(offset + 1f, 0f, 0f),
                new Vector3(offset + 0f, 2f, -1f),
                new Vector3(offset + 0f, 2f, 0f),
                new Vector3(offset + 1f, 2f, 0f),
                new Vector3(offset - 1f, 0f, -1f),
                new Vector3(offset - 1f, 0f, 1f),
                new Vector3(offset + 1f, 0f, 1f),
                new Vector3(offset - 1f, 2f, -1f),
                new Vector3(offset - 1f, 2f, 1f),
                new Vector3(offset + 1f, 2f, 1f));
        }

        return AddSnapPoints(prefab,
            new Vector3(0f, 0f, -1.25f),
            new Vector3(0f, 0f, -0.5f),
            new Vector3(1.25f, 0f, -0.5f),
            new Vector3(0f, 2f, -1.25f),
            new Vector3(0f, 2f, -0.5f),
            new Vector3(1.25f, 2f, -0.5f),
            new Vector3(-1f, 0f, -1.25f),
            new Vector3(-1f, 0f, 0.5f),
            new Vector3(1.25f, 0f, 0.5f),
            new Vector3(-1f, 2f, -1.25f),
            new Vector3(-1f, 2f, 0.5f),
            new Vector3(1.25f, 2f, 0.5f));
    }

    private static IEnumerable<Vector3> BoxCornerPoints(float x, float y, float z)
    {
        yield return new Vector3(-x, y, -z);
        yield return new Vector3(x, y, z);
        yield return new Vector3(-x, y, z);
        yield return new Vector3(x, y, -z);
    }

    private static bool AddSnapPoints(GameObject prefab, IEnumerable<Vector3> points)
    {
        bool any = false;
        foreach (Vector3 point in points)
        {
            any |= AddSnapPoint(prefab, point, "Snappoint");
        }

        return any;
    }

    private static bool AddSnapPoints(GameObject prefab, params Vector3[] points)
    {
        return AddSnapPoints(prefab, (IEnumerable<Vector3>)points);
    }

    private static bool AddSnapPoint(GameObject prefab, Vector3 point, string name)
    {
        if (HasSnapPointAt(prefab, point))
        {
            return true;
        }

        GameObject snapPoint = new(name);
        snapPoint.transform.SetParent(prefab.transform, worldPositionStays: false);
        snapPoint.transform.localPosition = point;
        snapPoint.tag = "snappoint";
        snapPoint.SetActive(false);
        return true;
    }

    private static bool HasSnapPointAt(GameObject prefab, Vector3 point)
    {
        foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.gameObject.tag, "snappoint", StringComparison.Ordinal) &&
                (transform.localPosition - point).sqrMagnitude <= SnapPointTolerance * SnapPointTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MoveSnapPoints(GameObject prefab, params (string name, Vector3 localPosition)[] moves)
    {
        bool any = false;
        foreach ((string name, Vector3 localPosition) in moves)
        {
            Transform snap = prefab.transform.Find(name);
            if (!snap)
            {
                continue;
            }

            snap.localPosition = localPosition;
            snap.tag = "snappoint";
            any = true;
        }

        return any;
    }

    private static bool AddSnapPointsToMeshCorners(GameObject prefab, string meshName)
    {
        MeshFilter meshFilter = prefab.GetComponentsInChildren<MeshFilter>(true)
            .FirstOrDefault(filter => filter && filter.sharedMesh && filter.name == meshName);
        if (!meshFilter)
        {
            return false;
        }

        Bounds bounds = meshFilter.sharedMesh.bounds;
        List<Vector3> points = new();
        foreach (Vector3 corner in GetBoundsCorners(bounds))
        {
            Vector3 world = meshFilter.transform.TransformPoint(corner);
            points.Add(prefab.transform.InverseTransformPoint(world));
        }

        return AddSnapPoints(prefab, points);
    }

    private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    yield return center + Vector3.Scale(extents, new Vector3(x, y, z));
                }
            }
        }
    }

    private static void FixPieceLayers(GameObject gameObject)
    {
        int pieceLayer = LayerMask.NameToLayer("piece");
        foreach (Collider collider in gameObject.GetComponentsInChildren<Collider>(true))
        {
            collider.gameObject.layer = pieceLayer;
        }
    }

    private static void RemoveColliders(GameObject prefab)
    {
        foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }
    }

    private static void AddBoxCollider(GameObject prefab, Vector3 center, Vector3 size)
    {
        BoxCollider collider = prefab.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
    }

    private static void RemoveComponent<T>(GameObject prefab) where T : Component
    {
        T component = prefab.GetComponent<T>();
        if (component)
        {
            Object.DestroyImmediate(component);
        }
    }

    private static void RemoveRandomPieceRotation(GameObject prefab, string childName)
    {
        Transform child = prefab.transform.Find(childName);
        RandomPieceRotation rotation = child ? child.GetComponent<RandomPieceRotation>() : null;
        if (rotation)
        {
            Object.DestroyImmediate(rotation);
        }
    }

    private static bool SetWearNTearSupports(GameObject prefab)
    {
        WearNTear wearNTear = prefab.GetComponent<WearNTear>();
        if (!wearNTear)
        {
            return false;
        }

        wearNTear.m_supports = true;
        return true;
    }

    private static bool AddChair(GameObject prefab, Vector3 attachPointPosition)
    {
        Chair chair = prefab.GetComponent<Chair>() ?? prefab.AddComponent<Chair>();
        Transform attachPoint = prefab.transform.Find("attachPoint");
        if (!attachPoint)
        {
            GameObject attachPointObject = new("attachPoint");
            attachPointObject.transform.SetParent(prefab.transform, worldPositionStays: false);
            attachPoint = attachPointObject.transform;
        }

        attachPoint.localPosition = attachPointPosition;
        chair.m_attachPoint = attachPoint;
        return true;
    }

    private static void AdjustNamedBoxCollider(GameObject prefab, string colliderName, Vector3 centerDelta, Vector3 sizeDelta)
    {
        foreach (BoxCollider collider in prefab.GetComponentsInChildren<BoxCollider>(true))
        {
            if (collider.name != colliderName)
            {
                continue;
            }

            collider.center += centerDelta;
            collider.size += sizeDelta;
        }
    }
}

internal sealed class HarnessPrefabsMvbpFixupMarker : MonoBehaviour
{
}
