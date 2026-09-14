# Valheim 1.0 compatibility checks

These checks use the final ILRepack output and unmodified game DLLs. They do not launch Unity or establish a network connection. Run from the repository root with .NET SDK 9 and the mod's .NET Framework 4.8 targeting pack available. Both test runners use .NET 9: .NET Framework cannot resolve Unity 6's netstandard 2.1 Span overloads during reflection. The mod itself still targets .NET Framework 4.8 and runs inside the game's Unity/Mono runtime.

```powershell
$originalManaged = 'C:\path\to\original\valheim_Data\Managed'
$bepinexCore = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\core'
dotnet build HarnessPrefabs.csproj -c Debug -p:DeployToGame=true
dotnet build tests/HarnessPrefabs.CompatibilityTests.csproj -c Debug "-p:OriginalManagedPath=$originalManaged"
dotnet ./tests/bin/Debug/net9.0/HarnessPrefabs.CompatibilityTests.dll ./bin/Debug/HarnessPrefabs.dll $originalManaged $bepinexCore
dotnet build tests/MetadataChecks/MetadataChecks.csproj -c Debug
dotnet ./tests/MetadataChecks/bin/Debug/net9.0/MetadataChecks.dll ./bin/Debug/HarnessPrefabs.dll $originalManaged $bepinexCore ./tests/bin/metadata-client.json
```

Repeat the two executables with the matching dedicated server's original `valheim_server_Data/Managed` directory. `OriginalManagedPath` changes test compilation references; `CorlibPath` changes mod compilation references. Neither applies a publicizer. Debug deployment copies only the final merged mod DLL to the local game's plugins directory.

- CompatibilityTests: ten cases for the native item payload reader, durability quantization, item identity/state comparison, malformed payload rejection, absence of a Jotunn assembly reference, and icon alpha validation (nonzero RGB with zero alpha is rejected; faint visible pixels and transparent backgrounds are retained). The item fixture uses the native game `ItemData.Load`; it is not a complete ArmorStand transaction or a `SaveToZDO` integration test. The icon fixtures invoke the final DLL's pixel validator; they do not render textures or verify GPU shader behavior.
- MetadataChecks: direct game method/field accessibility, declared Harmony target existence/uniqueness and injected argument types, explicit generic FieldRef targets, no stale `ZRoutedRpc.Everybody` field load. Resolves type forwarders when comparing client/server types. It does not execute patches or exhaustively verify dynamic reflection and third-party code.
- The pinned ServerSync source/verification location is recorded in `Libs/ServerSync.md`. Its isolated BufferHarness can take the final merged mod DLL as its candidate. It checks packet buffering with a fake transport, not actual Steam/PlayFab login.

Standalone CLR tests cannot execute Unity native calls or establish behavior in the game's Unity/Mono runtime. Actual UI, soft asset lifecycle, Harmony application and multiplayer tests therefore remain separate; see `VALHEIM-1.0.7.md`.

Hammer tab appearance regression (requires actual Unity execution): enable debugmode while Categories is selected, and again while another tab is selected. The newly attached HarnessPrefabs tab must be clickable and unhighlighted. Select it using mouse and keyboard/gamepad navigation, then switch away: only the active tab should be highlighted. Repeat after toggling `Show Harness Tabs`, reopening the menu, and re-entering the world. Attaching the tab must preserve the current tab and must not invoke another tab's selection callback.

For the icon renderer fix, restart the game and compare Wood box (`CastleKit_braided_box01`), Black Marble Post (01) (`blackmarble_post01`), and Standing Iron Torch (Eternal) (`CastleKit_groundtorch`). Their regenerated PNGs use `BepInEx/cache/HarnessPrefabs/valheim-<game version>-icons-r2`; old `valheim-1.0.7-r1` files are preserved and ignored. Check first generation, cached reload, framing and transparency, authored icons, world re-entry, and absence of repeated render attempts or gameplay/network effects from the visual clones. A fully transparent render should retain the fallback icon and log the prefab name, without storing a new blank PNG.

Also check `TreasureChest_dvergrtower`, `TreasureChest_dvergrtown`, `blackmarble_head_big01`, and `blackmarble_slope_1x2`: each pair shares an original Piece sprite, which must not prevent snapshots of their actual models. Existing successful snapshot caches remain valid. On render failure the original icon should remain; native buildables excluded by HarnessPrefabs must keep their own icons. World exit must restore the original sprites before generated textures are released.

Hammer selection regression (requires actual Unity execution): select a normal wall, then click two different pieces from each HarnessPrefabs group and verify both the placement ghost and the built prefab match the clicked icon. Repeat after closing/reopening the menu, switching tags/tools, refreshing YAML, and leaving/re-entering the world. Native `Player.SetSelectedPiece(Piece)` looks up `PieceTable.m_availablePiecesByCategory`; the `UpdateAvailable` postfix adds admin pieces to this selection index only. Check Categories, Materials, Recent, and Favorites remain free of these pieces, including after favoriting/building them. Disable debugmode or `Show Harness Tabs`, revoke admin status, and change an active prefab rule to hidden: the tab/selection must be removed and the old admin ghost must not remain usable. Test ordinary clients and a dedicated server as well. The metadata checker validates the postfix target and private field injection; the isolated CLR tests cannot create Unity objects, click the menu, or prove placement behavior.
