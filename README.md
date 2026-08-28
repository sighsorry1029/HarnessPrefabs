# HarnessPrefabs

Scans vanilla Valheim and loaded prefab-adding mods for existing prefabs that are not normally available in the Hammer build table, then lets the server decide which ones become public build pieces and which ones stay admin-only.

It does not add new prefabs. It helps you harness the prefabs that are already there.

![](https://i.ibb.co/21PTPBgw/equipmentswap.gif) <br>
Swap equipment with the vanilla armor stand and the additional armor stands unlocked by HarnessPrefabs.

![](https://i.ibb.co/XxXky8Kb/Screenshot-2026-06-24-230139.png) <br>
Dvergr barrel(enhanced lod) can be used as a fermenter. Fermenting time is configurable.

![](https://i.ibb.co/MkQQPHqH/Screenshot-2026-06-24-230129.png) <br>
Trailership can be used by default. It would be available at plains. It is slightly slower(configurable) then longship but has bigger containersize.

## Why Use It

- Open useful vanilla and modded prefabs without shipping another hard-coded prefab dump.
- Keep normal players on curated public build tabs while giving admins debug-only review tabs.
- Replace MoreVanillaBuildPrefabs-style public unlocks with YAML policy files you can review, copy, and override.
- Use MVBP-informed defaults for categories, requirements, placement flags, snap fixes, and icon behavior where those defaults are known.
- Keep the server as the source of truth through ServerSync, so clients receive the active prefab policy and localization text.
- Avoid Hammer clutter from runtime-only objects such as effects, projectiles, ragdolls, creatures, humanoids, and item drops.

## What It Does

HarnessPrefabs builds a safe review workflow around hidden prefabs:

1. Discover buildable-looking prefabs from Valheim and loaded mods.
2. Ignore prefabs that are unsafe for Hammer placement, such as characters, item drops, spawners, runtime effects, projectiles, ragdolls, and similar controller objects.
3. Seed defaults from reviewed MoreVanillaBuildPrefabs data when a prefab is known there.
4. Classify the rest into public or admin categories based on components and prefab shape.
5. Write reference YAML files for review.
6. Apply only the overrides you choose to write.
7. Sync the active policy from the server to clients.

This makes it useful both as a MoreVanillaBuildPrefabs replacement for curated public pieces and as an admin review tool for larger modpacks.

## Hammer Tabs

Public tabs use existing Valheim categories where possible:

- `Misc`
- `Crafting`
- `Building`
- `BuildingStonecutter`
- `Furniture`

Admin-only tabs are added only for admin clients while Valheim `debugmode` is enabled:

- `Harness Nature`
- `Harness Structures`
- `Harness Props`

The client-side `Show Harness Tabs` config can hide Harness admin tabs even while debugmode is on.

## Default Classification

Known MVBP public pieces default to public access. Known MVBP admin-style nature and prop pieces default to Harness admin tabs.

General classification uses prefab components and names:

- `Harness Nature`: plants, pickables, trees, logs, rocks, ore rocks, crops, and natural static prefabs.
- `Harness Structures`: walls, floors, arches, pillars, stairs, gates, doors, containers, portals, beds, crafting stations, fires, ships, carts, destructibles, and other build-like interactives.
- `Harness Props`: decor, static props, banners, rugs, tables, chairs, statues, treasure, CreatorShop-style props, clutter, fragments, and LOD-like objects.

Runtime objects are intentionally ignored. Prefabs named `fx_*`, `vfx_*`, or `sfx_*`, MVBP effect defaults, and prefabs with components such as `TimedDestruction`, `Aoe`, `CamShaker`, `Projectile`, or `Ragdoll` are not written to reference files and are not added to Hammer tabs.

Prefabs with `ItemDrop`, `Humanoid`, or `Character` anywhere in their hierarchy are ignored.

## Policy Files

HarnessPrefabs stores policy files under:

```text
BepInEx/config/HarnessPrefabs/
```

Editable override files:

```text
prefabs.yml
prefabs_*.yml
```

Generated review files:

```text
prefabs.reference.yml
prefabs.full.yml
```

`prefabs.yml` is created automatically. Extra files such as `prefabs_public.yml` or `prefabs_admin.yml` are loaded after the base file and can override the same prefab entries.

Do not edit `prefabs.reference.yml` or `prefabs.full.yml` directly. Copy entries from them into `prefabs.yml` or `prefabs_*.yml`.

## Minimal Overrides

Override files use a top-level YAML list. You can copy only the fields you want to change:

```yaml
- prefab: blackmarble_1x1
  enabled: true
  category: BuildingStonecutter
  requirements:
    - BlackMarble: 2

- prefab: GoblinTotem
  enabled: false
```

`enabled: true` exposes a prefab. Public categories expose it to normal clients. Harness categories expose it only to admins in debugmode.

Missing fields keep the generated default.

## Full Review Schema

Run this in the in-game console to write a full scaffold:

```text
harnessprefabs:full
```

The full scaffold includes fields that are useful for review:

```yaml
- prefab: barrell
  enabled: true
  category: Furniture
  displayName: Barrel
  description: ""
  craftingStation: Workbench
  requirements:
    - FineWood: 2
    - Iron: 1
  flags: false, false, false, true # clipEverything, clipGround, allowedInDungeons, canBeRemoved
  components: [Piece, WearNTear] # review metadata only
```

`components` is a reference hint from discovery and is ignored as an edit field.

## Localization

Server-synced localization files live under:

```text
BepInEx/config/HarnessPrefabs/localization/
```

`English.yml` is created automatically as a template. Add language files with Valheim language names such as `Korean.yml`, `German.yml`, or `Turkish.yml`.

Use localization tokens in prefab policy entries:

```yaml
- prefab: barrell
  enabled: true
  category: Furniture
  displayName: $ph_piece_barrell
  description: $ph_piece_barrell_desc
```

```yaml
$ph_piece_barrell: "Barrel"
$ph_piece_barrell_desc: "A decorative barrel."
```

English is applied first as fallback, then the client's selected language is layered over it.

## Icons And Placement

HarnessPrefabs uses Jotunn RenderManager to generate missing Hammer icons. The render path follows the MVBP-style isometric snapshot setup and handles known special cases such as `PickableItem` random item previews.

For prefabs with awkward or sparse colliders, HarnessPrefabs applies placement-only ghost helpers based on reviewed MVBP defaults. These helpers affect Hammer preview placement without adding extra helper colliders to the final placed object.

## Prefab Tweaks

The `2 - Prefab Tweaks` config section contains optional synchronized tweaks:

- `Trailership VikingShip Speed Ratio`: scales Trailership movement relative to VikingShip. Default is `0.66`; accepted range is `0.5` to `1.0`.
- `Enable Armor Stand Equipment Swap`: on by default. Left or Right Alt+Use swaps the player's Helmet, Chest, Legs, Shoulder, and drawn or sheathed hand set with `ArmorStand`, `ArmorStand_Female`, or `ArmorStand_Male`; the game's `AltPlace`/Shift input no longer triggers the swap. A Utility item is included only when both sides of that Utility exchange are safe; displayable items such as `BeltStrength` can swap, while incompatible items such as `Wishbone` or a demister stay equipped or attached and the rest of the set still swaps. One-handed pairs and two-handed weapons are validated and moved as one hand-set transaction. A sheathed hand set remains sheathed after the swap; temporary states that mix drawn and hidden hand items must finish first. On the base `ArmorStand`, the shield and weapon back slots continue to hold either hand set. On the female and male stands, drawn equipment swaps with the hand slots and sheathed equipment swaps with the back slots; the unselected pair stays unchanged. When the player has neither drawn nor sheathed hand equipment, the only populated pair is retrieved, empty stands default to the hand slots, and a stand with both pairs populated asks the player to draw or sheathe an item to choose. Female and male selected slots must already match the sides Valheim chooses when equipping the complete set; reversed display-only arrangements are not changed.
- `Enable Bed Patches`: off by default. Adds bed/spawn behavior to supported MVBP-style bed prefabs.
- `Fermenter Patch Duration Percent`: `0` disables the player-built `dvergrprops_barrel` fermenter patch. `1` to `100` enables it and sets fermentation duration as a percentage of the vanilla fermenter. Natural instances without a creator remain ordinary barrels.

Bed and fermenter tweaks are intentionally marked unsafe because disabling the mod later can affect placed-world state such as spawn points or fermenting contents.

Turn `Enable Armor Stand Equipment Swap` off when another mod, such as `ZenItemStands` or `Wardrobe`, should handle ArmorStand interaction instead. HarnessPrefabs does not automatically enable or disable this feature based on installed mods.

## Strengths

- Curated defaults with local control: MVBP knowledge is used as a starting point, but YAML remains the final policy.
- Admin review without player clutter: Harness tabs exist only for admins in debugmode and can be hidden client-side.
- Server-authoritative policy: active overrides and localization can be synchronized to clients.
- Modpack friendly discovery: prefabs are grouped by owner where possible, making large reference files easier to review.
- Safer Hammer scope: runtime effects, creatures, item drops, and spawner/controller objects stay outside the build table.
- Focused compatibility: Jotunn handles category integration and icon rendering instead of private UI hacks.

## Suggested Workflow

1. Start the server or single-player world with HarnessPrefabs installed.
2. Let it generate `prefabs.reference.yml`.
3. Copy wanted entries into `prefabs.yml` or a `prefabs_*.yml` file.
4. Use public categories for normal-player pieces.
5. Use Harness categories for admin-only review/build pieces.
6. Run `harnessprefabs:full` only when you need display names, descriptions, flags, or component metadata for deeper review.

## Github
Original code from
https://github.com/searica/MoreVanillaBuildPrefabs <br>
The mod's repo
https://github.com/sighsorry1029/HarnessPrefabs
