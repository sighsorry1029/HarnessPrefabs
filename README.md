# HarnessPrefabs

HarnessPrefabs adds existing objects from Valheim and your installed mods to the Hammer build menu, including Dvergr barrels and black marble chests. Admins choose which pieces everyone can build and which stay in a separate debug tab.

![A player swapping equipment with an armor stand](https://i.ibb.co/21PTPBgw/equipmentswap.gif)

*Hold Alt and press Use (E by default) at a player-built armor stand to swap armor and weapons.*

![A Dvergr barrel used for fermentation](https://i.ibb.co/XxXky8Kb/Screenshot-2026-06-24-230139.png)

*Build a Dvergr barrel from the Crafting category to use it as a fermenter.*

## Getting started

Install HarnessPrefabs and BepInExPack Valheim with your mod manager. For multiplayer, install the same mod version on the host or dedicated server and on every player's client. The server shares its prefab rules and translations with clients.

Equip the Hammer and open the build menu. Public pieces appear in the usual categories. As an admin, enable `debugmode` to see the **HarnessPrefabs** tab beside **Favorites**. It groups pieces under **Harness Nature**, **Harness Structures**, and **Harness Props**. Turn off `Show Harness Tabs` to hide the tab on your client.

## Choose what players can build

After loading a world, open `BepInEx/config/HarnessPrefabs/` on the host or server. Find a piece in the generated `prefabs.reference.yml` and copy its entry into `prefabs.yml`. For example, this puts the decorative barrel in Furniture and sets its cost to four Fine Wood.

```yaml
- prefab: barrell
  enabled: true
  category: Furniture
  requirements:
    - FineWood: 4
```

Use a Harness category to reserve a piece for admins, or set `enabled: false` to hide it. For a list of pieces with their current settings, run `harnessprefabs:full` in the in-game console to generate `prefabs.full.yml`.

## Equipment and crafting

These features are enabled by default. Their settings are in `BepInEx/config/sighsorry.valheim.harnessprefabs.cfg`.

- Equipment swapping works on player-built standard, male, and female armor stands. Drawn and sheathed weapons keep their state.
- Player-built Dvergr barrels ferment in 70% of the usual time. Set `Fermenter Patch Duration Percent` to `0` to disable fermentation.
- Supported beds that players build can serve as spawn points. Before removing the mod, claim a vanilla bed and empty any fermenting barrels.

## Compatibility

HarnessPrefabs runs without Jötunn. MoreVanillaBuildPrefabs, the source of many of the default build rules, is incompatible with HarnessPrefabs. If another mod handles armor stand interactions, turn off `Enable Armor Stand Equipment Swap` in HarnessPrefabs.

[MoreVanillaBuildPrefabs by Searica](https://github.com/searica/MoreVanillaBuildPrefabs) · [HarnessPrefabs on GitHub](https://github.com/sighsorry1029/HarnessPrefabs)
